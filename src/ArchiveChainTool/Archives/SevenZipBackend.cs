using System.Diagnostics;
using System.Text;

namespace ArchiveChainTool.Archives;

internal sealed record ArchiveCommandResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode is 0 or 1;
}

internal sealed class SevenZipBackend
{
    private const int MaxCapturedCharacters = 1_000_000;
    private readonly string _executablePath;
    private readonly RunLogger _logger;

    public SevenZipBackend(string executablePath, RunLogger logger)
    {
        _executablePath = executablePath;
        _logger = logger;
    }

    public Task<ArchiveCommandResult> ListAsync(string archive, string? password, CancellationToken token) =>
        RunAsync("列举压缩包", BuildArguments("l", archive, null, password, "-slt", "-ba"), password, token);

    public Task<ArchiveCommandResult> TestAsync(string archive, string? password, CancellationToken token) =>
        RunAsync("测试压缩包", BuildArguments("t", archive, null, password, "-y"), password, token);

    public Task<ArchiveCommandResult> ExtractAsync(
        string archive,
        string outputDirectory,
        string? password,
        CancellationToken token) =>
        RunAsync("解压压缩包", BuildArguments("x", archive, outputDirectory, password, "-y", "-aoa"), password, token);

    private static List<string> BuildArguments(
        string command,
        string archive,
        string? output,
        string? password,
        params string[] options)
    {
        var arguments = new List<string> { command };
        arguments.AddRange(options);
        if (output is not null)
        {
            arguments.Add($"-o{output}");
        }
        if (password is not null)
        {
            arguments.Add("-p");
        }
        else
        {
            arguments.Add("-p-");
        }
        arguments.Add("--");
        arguments.Add(archive);
        return arguments;
    }

    private async Task<ArchiveCommandResult> RunAsync(
        string operation,
        IReadOnlyList<string> arguments,
        string? password,
        CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var displayArguments = arguments.Select(argument =>
            password is not null && argument.StartsWith("-p", StringComparison.Ordinal)
                ? "-p<redacted>"
                : argument);
        _logger.Write($"{operation}: \"{_executablePath}\" {string.Join(' ', displayArguments)}");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动随附的 7-Zip 引擎。");
        }
        if (password is not null)
        {
            await process.StandardInput.WriteLineAsync(password);
            process.StandardInput.Close();
        }

        using var registration = token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
        });

        var stdoutTask = ReadLimitedAsync(process.StandardOutput, password);
        var stderrTask = ReadLimitedAsync(process.StandardError, password);
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        var output = (await stdoutTask) + Environment.NewLine + (await stderrTask);
        _logger.Write($"7-Zip 退出码: {process.ExitCode}");
        if (!string.IsNullOrWhiteSpace(output))
        {
            _logger.WriteBlock("7zip-output", output);
        }
        return new ArchiveCommandResult(process.ExitCode, output);
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, string? secret)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer);
            if (count == 0)
            {
                break;
            }
            if (result.Length < MaxCapturedCharacters)
            {
                result.Append(buffer, 0, Math.Min(count, MaxCapturedCharacters - result.Length));
            }
        }
        if (result.Length == MaxCapturedCharacters)
        {
            result.AppendLine().Append("[输出已截断]");
        }
        var text = result.ToString();
        return string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "<redacted>", StringComparison.Ordinal);
    }
}
