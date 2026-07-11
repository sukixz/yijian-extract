using System.Text.Json;

namespace ArchiveChainTool.Workflows;

internal static class WorkflowStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string UserWorkflowDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "熠键解压",
        "Workflows");

    public static WorkflowDefinition Load(string path)
    {
        var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException("方案文件为空或格式无效。");
        if (workflow.SchemaVersion is not (1 or 2 or 3))
        {
            throw new InvalidOperationException($"不支持方案版本 {workflow.SchemaVersion}，当前程序支持版本 1、2 和 3。");
        }
        workflow.SchemaVersion = 3;
        foreach (var extract in workflow.Steps.OfType<ExtractStepDefinition>())
        {
            extract.Password ??= new PasswordOptions();
            extract.Password.LibraryEntryIds ??= [];
        }
        Validate(workflow);
        return workflow;
    }

    public static void Save(string path, WorkflowDefinition workflow)
    {
        foreach (var step in workflow.Steps)
        {
            if (step is ExtractStepDefinition extract)
            {
                extract.Password.LibraryEntryIds = extract.Password.LibraryEntryIds.Distinct().ToList();
                if (extract.Password.Mode != PasswordMode.Stored) extract.Password.Value = null;
                if (extract.Password.Mode != PasswordMode.PasswordLibrary) extract.Password.LibraryEntryIds.Clear();
            }
        }
        Validate(workflow);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(workflow, Options), new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public static WorkflowDefinition Clone(WorkflowDefinition workflow) =>
        JsonSerializer.Deserialize<WorkflowDefinition>(JsonSerializer.Serialize(workflow, Options), Options)
        ?? throw new InvalidOperationException("无法复制方案。");

    public static void Validate(WorkflowDefinition workflow)
    {
        if (workflow.ExecutionMode == ExecutionMode.CustomWorkflow &&
            (workflow.Steps.Count == 0 || workflow.Steps.All(step => !step.Enabled)))
        {
            throw new InvalidOperationException("方案至少需要一个启用的步骤。");
        }
        if (workflow.Steps.Count(step => step.Enabled) > 100)
        {
            throw new InvalidOperationException("启用的步骤不能超过 100 个。");
        }
        if (workflow.Automatic.MaxLayers is < 1 or > 100)
        {
            throw new InvalidOperationException("自动解压最大层数必须在 1 到 100 之间。");
        }
        foreach (var step in workflow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
            {
                throw new InvalidOperationException("每个步骤都必须填写名称。");
            }
            if (step is RenameStepDefinition rename)
            {
                PathHelpers.ValidateSimpleFileName(rename.TargetFileName);
            }
            if (step is ExtractStepDefinition extract &&
                extract.Password.Mode == PasswordMode.Stored &&
                string.IsNullOrEmpty(extract.Password.Value))
            {
                throw new InvalidOperationException($"步骤“{step.Name}”选择了固定密码，但没有填写密码。");
            }
        }
    }
}
