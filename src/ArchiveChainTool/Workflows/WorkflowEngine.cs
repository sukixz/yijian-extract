using System.Security.Cryptography;
using ArchiveChainTool.Archives;
using ArchiveChainTool.Storage;
using ArchiveChainTool.Workflows;

namespace ArchiveChainTool;

internal sealed record WorkflowProgress(int StepIndex, int StepCount, string Message);
internal sealed record WorkflowRunResult(string TaskDirectory, string FinalArtifact, string LogPath);

internal sealed class WorkflowEngine
{
    private readonly Func<IReadOnlyList<string>, string, Task<string?>> _chooseCandidate;
    private readonly Func<ExtractStepDefinition, string, Task<string?>> _requestPassword;
    private readonly IReadOnlyList<PasswordEntry> _passwordLibrary;

    public WorkflowEngine(
        Func<IReadOnlyList<string>, string, Task<string?>> chooseCandidate,
        Func<ExtractStepDefinition, string, Task<string?>> requestPassword,
        IReadOnlyList<PasswordEntry>? passwordLibrary = null)
    {
        _chooseCandidate = chooseCandidate;
        _requestPassword = requestPassword;
        _passwordLibrary = passwordLibrary ?? [];
    }

    public async Task<WorkflowRunResult> RunAsync(
        WorkflowDefinition workflow,
        string initialInput,
        string outputRoot,
        string sevenZipPath,
        IProgress<WorkflowProgress>? progress,
        CancellationToken token)
    {
        WorkflowStore.Validate(workflow);
        if (!File.Exists(initialInput))
        {
            throw new FileNotFoundException("输入文件不存在。", initialInput);
        }
        if (!File.Exists(sevenZipPath) || !File.Exists(Path.Combine(Path.GetDirectoryName(sevenZipPath)!, "7z.dll")))
        {
            throw new FileNotFoundException("便携包缺少 tools\\7zip\\7z.exe 或 7z.dll。请完整复制整个软件文件夹。", sevenZipPath);
        }

        var runName = $"{PathHelpers.SanitizeFileName(Path.GetFileNameWithoutExtension(initialInput))}_{DateTime.Now:yyyyMMdd_HHmmss_fff}";
        var taskDirectory = PathHelpers.CreateUniqueDirectory(outputRoot, runName);
        var logPath = Path.Combine(taskDirectory, "logs", "run.log");
        using var logger = new RunLogger(logPath);
        var backend = new SevenZipBackend(sevenZipPath, logger);
        var currentArtifact = initialInput;
        var enabledSteps = workflow.Steps.Where(step => step.Enabled).ToArray();

        try
        {
            logger.Write($"开始工作流：{workflow.Name}");
            logger.Write($"输入：{initialInput}");
            logger.Write($"任务目录：{taskDirectory}");

            if (workflow.ExecutionMode == ExecutionMode.AutomaticChain)
            {
                currentArtifact = await RunAutomaticAsync(
                    workflow, currentArtifact, taskDirectory, backend, logger, progress, token);
                logger.Write($"自动解压完成，最终工件：{currentArtifact}");
                return new(taskDirectory, currentArtifact, logPath);
            }

            for (var index = 0; index < enabledSteps.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var step = enabledSteps[index];
                progress?.Report(new(index + 1, enabledSteps.Length, $"正在执行：{step.Name}"));
                logger.Write($"步骤 {index + 1}/{enabledSteps.Length}：{step.Name} ({step.StepType})");

                var candidates = CandidateResolver.Find(currentArtifact, step.Candidate);
                var selected = await SelectCandidateAsync(candidates, step, token);
                logger.Write($"选择：{selected}");

                if (step is ExtractStepDefinition extract)
                {
                    currentArtifact = await ExecuteExtractAsync(
                        extract, selected, taskDirectory, index + 1, backend, logger, token);
                }
                else if (step is RenameStepDefinition rename)
                {
                    currentArtifact = ExecuteRename(rename, selected, taskDirectory, index + 1, logger);
                }
            }

            progress?.Report(new(enabledSteps.Length, enabledSteps.Length, "全部步骤完成。"));
            logger.Write($"全部步骤完成，最终工件：{currentArtifact}");
            return new(taskDirectory, currentArtifact, logPath);
        }
        catch (OperationCanceledException)
        {
            logger.Write("工作流已取消。");
            throw;
        }
        catch (Exception ex)
        {
            logger.Write($"工作流失败：{ex}");
            throw new WorkflowRunException(ex.Message, taskDirectory, logPath, ex);
        }
    }

    private async Task<string> RunAutomaticAsync(
        WorkflowDefinition workflow,
        string currentArtifact,
        string taskDirectory,
        SevenZipBackend backend,
        RunLogger logger,
        IProgress<WorkflowProgress>? progress,
        CancellationToken token)
    {
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? successfulPassword = null;
        var extractStep = new ExtractStepDefinition { Name = "自动解压", OutputName = "自动层" };

        for (var layer = 1; layer <= workflow.Automatic.MaxLayers; layer++)
        {
            token.ThrowIfCancellationRequested();
            var candidates = FindArchiveCandidates(currentArtifact);
            if (candidates.Count == 0)
            {
                progress?.Report(new(layer - 1, workflow.Automatic.MaxLayers, "没有发现下一层压缩包，自动解压结束。"));
                break;
            }
            var selected = candidates.Count == 1
                ? candidates[0]
                : await _chooseCandidate(candidates, $"自动解压第 {layer} 层")
                    ?? throw new OperationCanceledException("用户取消了候选选择。", token);

            var hash = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(selected), token));
            if (!seenHashes.Add(hash))
            {
                throw new InvalidOperationException("检测到重复的压缩包内容，已停止以避免无限循环。");
            }

            var archive = PrepareDisguisedArchive(selected, taskDirectory, layer, workflow.Automatic);
            progress?.Report(new(layer, workflow.Automatic.MaxLayers, $"自动解压第 {layer} 层：{Path.GetFileName(archive)}"));
            var password = await FindWorkingPasswordAsync(backend, archive, successfulPassword, extractStep, token);
            if (password is not null)
            {
                successfulPassword = password;
            }
            currentArtifact = await ExecuteExtractWithPasswordAsync(
                extractStep, archive, taskDirectory, layer, backend, logger, token, password, $"自动第{layer}层");
        }
        return currentArtifact;
    }

    private static IReadOnlyList<string> FindArchiveCandidates(string artifact)
    {
        if (File.Exists(artifact))
        {
            return ArchiveInspector.DetectFormat(artifact) == ArchiveFormat.Unknown ? [] : [artifact];
        }
        if (!Directory.Exists(artifact))
        {
            return [];
        }
        return Directory.EnumerateFiles(artifact, "*", SearchOption.AllDirectories)
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Where(path => ArchiveInspector.DetectFormat(path) != ArchiveFormat.Unknown)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string PrepareDisguisedArchive(
        string source,
        string taskDirectory,
        int layer,
        AutomaticChainOptions options)
    {
        var extension = Path.GetExtension(source);
        if (!options.CorrectDisguisedJpegExtension ||
            !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }
        var format = ArchiveInspector.DetectFormat(source);
        var targetExtension = format switch
        {
            ArchiveFormat.Rar => ".rar",
            ArchiveFormat.Zip => ".zip",
            _ => null
        };
        if (targetExtension is null)
        {
            return source;
        }
        var directory = Path.Combine(taskDirectory, $"auto-{layer:D2}-input");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + targetExtension);
        destination = EnsureUniqueFile(destination);
        File.Copy(source, destination, overwrite: false);
        return destination;
    }

    private async Task<string?> FindWorkingPasswordAsync(
        SevenZipBackend backend,
        string archive,
        string? successfulPassword,
        ExtractStepDefinition promptStep,
        CancellationToken token)
    {
        var noPassword = await backend.TestAsync(archive, null, token);
        if (noPassword.Succeeded)
        {
            return null;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(successfulPassword)) candidates.Add(successfulPassword);
        candidates.AddRange(_passwordLibrary.Select(entry => entry.Password));
        foreach (var password in candidates.Where(value => !string.IsNullOrEmpty(value)).Distinct().Take(50))
        {
            if ((await backend.TestAsync(archive, password, token)).Succeeded)
            {
                return password;
            }
        }

        var manual = await _requestPassword(promptStep, archive);
        if (manual is null)
        {
            throw new OperationCanceledException("用户取消了密码输入。", token);
        }
        if (!(await backend.TestAsync(archive, manual, token)).Succeeded)
        {
            throw new InvalidOperationException("输入的密码不正确，或压缩包已经损坏。");
        }
        return manual;
    }

    private static string EnsureUniqueFile(string preferred)
    {
        if (!File.Exists(preferred) && !Directory.Exists(preferred)) return preferred;
        var directory = Path.GetDirectoryName(preferred)!;
        var stem = Path.GetFileNameWithoutExtension(preferred);
        var extension = Path.GetExtension(preferred);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private async Task<string> ExecuteExtractWithPasswordAsync(
        ExtractStepDefinition step,
        string archive,
        string taskDirectory,
        int stepIndex,
        SevenZipBackend backend,
        RunLogger logger,
        CancellationToken token,
        string? password,
        string outputName)
    {
        var listResult = await backend.ListAsync(archive, password, token);
        if (!listResult.Succeeded) throw new InvalidOperationException("无法列举压缩包内容。");
        ValidateListedEntries(listResult.Output);
        var safeName = PathHelpers.SanitizeFileName(outputName);
        var partial = Path.Combine(taskDirectory, $"{stepIndex:D2}_{safeName}.partial");
        var final = EnsureUniqueDestination(Path.Combine(taskDirectory, $"{stepIndex:D2}_{safeName}"));
        Directory.CreateDirectory(partial);
        var result = await backend.ExtractAsync(archive, partial, password, token);
        if (!result.Succeeded) throw new InvalidOperationException($"解压失败（退出码 {result.ExitCode}）。");
        PathHelpers.AuditExtractedTree(partial);
        Directory.Move(partial, final);
        logger.Write($"步骤输出：{final}");
        return final;
    }

    private async Task<string> SelectCandidateAsync(
        IReadOnlyList<string> candidates,
        WorkflowStepDefinition step,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"步骤“{step.Name}”没有找到符合规则的文件。");
        }
        if (candidates.Count == 1)
        {
            return candidates[0];
        }
        if (!step.Candidate.AskWhenMultiple)
        {
            throw new InvalidOperationException($"步骤“{step.Name}”找到 {candidates.Count} 个候选，规则要求必须唯一。");
        }
        return await _chooseCandidate(candidates, step.Name)
            ?? throw new OperationCanceledException("用户取消了候选文件选择。", token);
    }

    private async Task<string> ExecuteExtractAsync(
        ExtractStepDefinition step,
        string archive,
        string taskDirectory,
        int stepIndex,
        SevenZipBackend backend,
        RunLogger logger,
        CancellationToken token)
    {
        string? password;
        if (step.Password.Mode == PasswordMode.PasswordLibrary)
        {
            password = null;
            var selectedEntries = step.Password.LibraryEntryIds
                .Select(id => _passwordLibrary.FirstOrDefault(entry => entry.Id == id))
                .Where(entry => entry is not null)
                .Cast<PasswordEntry>()
                .GroupBy(entry => entry.Password)
                .Select(group => group.First());
            foreach (var entry in selectedEntries)
            {
                if ((await backend.TestAsync(archive, entry.Password, token)).Succeeded)
                {
                    password = entry.Password;
                    break;
                }
            }
            if (password is null)
            {
                password = await _requestPassword(step, archive);
                if (password is null) throw new OperationCanceledException("用户取消了密码输入。", token);
            }
        }
        else
        {
            password = step.Password.Mode switch
            {
                PasswordMode.None => null,
                PasswordMode.Stored => step.Password.Value,
                PasswordMode.Prompt => await _requestPassword(step, archive),
                _ => null
            };
            if (step.Password.Mode == PasswordMode.Prompt && password is null)
                throw new OperationCanceledException("用户取消了密码输入。", token);
        }

        var listResult = await backend.ListAsync(archive, password, token);
        if (!listResult.Succeeded)
        {
            throw new InvalidOperationException($"无法列举压缩包（退出码 {listResult.ExitCode}）。密码可能错误或格式不受支持。");
        }
        ValidateListedEntries(listResult.Output);

        var testResult = await backend.TestAsync(archive, password, token);
        if (!testResult.Succeeded)
        {
            throw new InvalidOperationException($"压缩包测试失败（退出码 {testResult.ExitCode}）。密码可能错误或文件损坏。");
        }

        var safeName = PathHelpers.SanitizeFileName(string.IsNullOrWhiteSpace(step.OutputName) ? step.Name : step.OutputName);
        var partial = Path.Combine(taskDirectory, $"{stepIndex:D2}_{safeName}.partial");
        var final = Path.Combine(taskDirectory, $"{stepIndex:D2}_{safeName}");
        Directory.CreateDirectory(partial);
        PathHelpers.EnsureEmptyDirectory(partial);

        var result = await backend.ExtractAsync(archive, partial, password, token);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"解压失败（退出码 {result.ExitCode}）。");
        }
        PathHelpers.AuditExtractedTree(partial);
        if (!Directory.EnumerateFileSystemEntries(partial).Any())
        {
            throw new InvalidOperationException("解压成功但输出目录为空。");
        }
        final = EnsureUniqueDestination(final);
        Directory.Move(partial, final);
        logger.Write($"步骤输出：{final}");
        return final;
    }

    private static string ExecuteRename(
        RenameStepDefinition step,
        string source,
        string taskDirectory,
        int stepIndex,
        RunLogger logger)
    {
        PathHelpers.ValidateSimpleFileName(step.TargetFileName);
        var sourceFull = Path.GetFullPath(source);
        var taskRoot = Path.GetFullPath(taskDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!sourceFull.StartsWith(taskRoot, StringComparison.OrdinalIgnoreCase))
        {
            var copyDirectory = Path.Combine(taskDirectory, $"{stepIndex:D2}_重命名工作区");
            Directory.CreateDirectory(copyDirectory);
            var copyPath = Path.Combine(copyDirectory, Path.GetFileName(source));
            File.Copy(source, copyPath, overwrite: false);
            source = copyPath;
        }

        string destination;
        if (step.CollisionPolicy == CollisionPolicy.AppendNumber)
        {
            destination = PathHelpers.MoveWithUniqueName(source, step.TargetFileName);
        }
        else
        {
            destination = Path.Combine(Path.GetDirectoryName(source)!, step.TargetFileName);
            File.Move(source, destination, overwrite: false);
        }
        logger.Write($"重命名结果：{destination}");
        return destination;
    }

    private static void ValidateListedEntries(string output)
    {
        var count = 0;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("Path = ", StringComparison.Ordinal))
            {
                continue;
            }
            count++;
            if (count > 500_000)
            {
                throw new InvalidOperationException("归档条目超过安全上限 500,000。 ");
            }
            var path = line[7..].Trim();
            if (Path.IsPathRooted(path) || path.StartsWith('\\') || path.StartsWith('/') ||
                path.Split(['\\', '/']).Contains("..") || path.Contains(':'))
            {
                throw new InvalidOperationException($"归档包含不安全路径：{path}");
            }
        }
    }

    private static string EnsureUniqueDestination(string preferred)
    {
        if (!Directory.Exists(preferred) && !File.Exists(preferred))
        {
            return preferred;
        }
        for (var index = 1; ; index++)
        {
            var candidate = preferred + "_" + index;
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}

internal sealed class WorkflowRunException : Exception
{
    public string TaskDirectory { get; }
    public string LogPath { get; }

    public WorkflowRunException(string message, string taskDirectory, string logPath, Exception inner)
        : base(message, inner)
    {
        TaskDirectory = taskDirectory;
        LogPath = logPath;
    }
}
