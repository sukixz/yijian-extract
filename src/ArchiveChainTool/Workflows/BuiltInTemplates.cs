namespace ArchiveChainTool.Workflows;

internal static class BuiltInTemplates
{
    public static WorkflowDefinition FixedFourLayer() => new()
    {
        Name = "经典四层解压",
        Description = "RAR → ZIP → 伪装 JPG/RAR → ZIP → 最终文件夹",
        Steps =
        [
            Extract("解压文件1", ".rar", "rar"),
            Extract("解压文件2", ".zip", "zip"),
            new RenameStepDefinition
            {
                Name = "JPG 改名为 1.rar",
                TargetFileName = "1.rar",
                Candidate = Rule(".jpg", "rar")
            },
            Extract("解压文件3", ".rar", "rar"),
            new RenameStepDefinition
            {
                Name = "ZIP 改名为 2.zip",
                TargetFileName = "2.zip",
                Candidate = Rule(string.Empty, "zip")
            },
            Extract("解压最终 ZIP", ".zip", "zip")
        ]
    };

    public static WorkflowDefinition NewBlank() => new()
    {
        Name = "新方案",
        Steps = [new ExtractStepDefinition { Name = "解压第1层", Candidate = Rule(string.Empty, string.Empty) }]
    };

    private static ExtractStepDefinition Extract(string name, string extension, string format) => new()
    {
        Name = name,
        Candidate = Rule(extension, format),
        OutputName = name
    };

    private static CandidateRule Rule(string extensions, string formats) => new()
    {
        Extensions = extensions,
        Formats = formats,
        NamePattern = "*",
        Recursive = true,
        AskWhenMultiple = true
    };
}
