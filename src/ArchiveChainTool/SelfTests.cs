namespace ArchiveChainTool;

using ArchiveChainTool.Workflows;

internal static class SelfTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ArchiveChainToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            TestFormatDetection(root);
            TestUniqueMove(root);
            TestCandidateSelection(root);
            TestPathAudit(root);
            TestWorkflowRoundTrip(root);
            TestAutomaticWorkflow(root);
            Console.WriteLine("SELF-TEST PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF-TEST FAILED: {ex}");
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // A failed cleanup must not hide a test result.
            }
        }
    }

    private static void TestFormatDetection(string root)
    {
        var zip = Path.Combine(root, "sample.zip");
        File.WriteAllBytes(zip, [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        Assert(ArchiveInspector.DetectFormat(zip) == ArchiveFormat.Zip, "ZIP 文件头识别失败");

        var rar4 = Path.Combine(root, "sample4.rar");
        File.WriteAllBytes(rar4, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);
        Assert(ArchiveInspector.DetectFormat(rar4) == ArchiveFormat.Rar, "RAR4 文件头识别失败");

        var disguised = Path.Combine(root, "sample.jpg");
        File.WriteAllBytes(disguised, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);
        Assert(ArchiveInspector.DetectFormat(disguised) == ArchiveFormat.Rar, "RAR5 伪装 JPG 识别失败");

        var jpg = Path.Combine(root, "real.jpg");
        File.WriteAllBytes(jpg, [0xFF, 0xD8, 0xFF, 0xE0]);
        Assert(ArchiveInspector.DetectFormat(jpg) == ArchiveFormat.Unknown, "真实 JPG 不应识别为归档");
    }

    private static void TestUniqueMove(string root)
    {
        var directory = Path.Combine(root, "move");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "1.rar"), "existing");
        var source = Path.Combine(directory, "input.jpg");
        File.WriteAllText(source, "source");

        var destination = PathHelpers.MoveWithUniqueName(source, "1.rar");
        Assert(Path.GetFileName(destination) == "1_1.rar", "同名避让未生成 1_1.rar");
        Assert(File.Exists(destination) && !File.Exists(source), "源文件未被正确重命名");
    }

    private static void TestCandidateSelection(string root)
    {
        var directory = Path.Combine(root, "candidates");
        Directory.CreateDirectory(directory);
        var candidate = Path.Combine(directory, "中文 文件.jpg");
        File.WriteAllBytes(candidate, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);
        File.WriteAllBytes(Path.Combine(directory, "picture.jpg"), [0xFF, 0xD8, 0xFF, 0xE0]);

        var rule = new CandidateRule { Extensions = ".jpg", Formats = "rar", NamePattern = "*", Recursive = true };
        var candidates = CandidateResolver.Find(directory, rule);
        Assert(candidates.Count == 1 && candidates[0] == candidate, "未选择唯一的伪装 RAR");

        File.WriteAllBytes(Path.Combine(directory, "second.jpg"), [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);
        Assert(CandidateResolver.Find(directory, rule).Count == 2, "应找到两个候选文件");
    }

    private static void TestWorkflowRoundTrip(string root)
    {
        var workflow = BuiltInTemplates.FixedFourLayer();
        var path = Path.Combine(root, "workflow.json");
        WorkflowStore.Save(path, workflow);
        var loaded = WorkflowStore.Load(path);
        Assert(loaded.Steps.Count == 6, "经典模板应包含六个显式步骤");
        Assert(loaded.Steps[2] is RenameStepDefinition, "第三步应为重命名");

        var passwordWorkflow = BuiltInTemplates.NewBlank();
        var extract = (ExtractStepDefinition)passwordWorkflow.Steps[0];
        extract.Password = new PasswordOptions { Mode = PasswordMode.Prompt };
        WorkflowStore.Save(Path.Combine(root, "password.json"), passwordWorkflow);
    }

    private static void TestAutomaticWorkflow(string root)
    {
        var workflow = BuiltInTemplates.NewBlank();
        workflow.ExecutionMode = ExecutionMode.AutomaticChain;
        workflow.Automatic.MaxLayers = 10;
        workflow.KeepIntermediateFiles = false;
        var path = Path.Combine(root, "automatic.json");
        WorkflowStore.Save(path, workflow);
        var loaded = WorkflowStore.Load(path);
        Assert(loaded.SchemaVersion == 3, "工作流应升级为 schema 3");
        Assert(loaded.ExecutionMode == ExecutionMode.AutomaticChain, "自动模式保存失败");
        Assert(loaded.Automatic.MaxLayers == 10, "自动层数保存失败");
    }

    private static void TestPathAudit(string root)
    {
        var directory = Path.Combine(root, "audit");
        Directory.CreateDirectory(Path.Combine(directory, "子目录"));
        File.WriteAllText(Path.Combine(directory, "子目录", "ok.txt"), "ok");
        PathHelpers.AuditExtractedTree(directory);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }
}
