using ArchiveChainTool.Workflows;

namespace ArchiveChainTool.Storage;

internal sealed record TemplateDescriptor(string Name, string Description, bool IsBuiltIn, string? FilePath, WorkflowDefinition Workflow);

internal sealed class TemplateStore
{
    public string DirectoryPath { get; }

    public TemplateStore(string baseDirectory)
    {
        DirectoryPath = Path.Combine(baseDirectory, "data", "templates");
    }

    public IReadOnlyList<TemplateDescriptor> LoadAll()
    {
        var templates = new List<TemplateDescriptor>
        {
            new("经典四层解压", "内置只读模板", true, null, BuiltInTemplates.FixedFourLayer())
        };
        if (!Directory.Exists(DirectoryPath)) return templates;
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var workflow = WorkflowStore.Load(path);
                templates.Add(new(workflow.Name, workflow.Description, false, path, workflow));
            }
            catch { }
        }
        return templates;
    }

    public string SaveAsTemplate(WorkflowDefinition workflow, string name, string description)
    {
        Directory.CreateDirectory(DirectoryPath);
        var copy = WorkflowStore.Clone(workflow);
        copy.Id = Guid.NewGuid();
        copy.Name = name;
        copy.Description = description;
        foreach (var step in copy.Steps.OfType<ExtractStepDefinition>())
        {
            if (step.Password.Mode == PasswordMode.Stored)
            {
                step.Password = new PasswordOptions { Mode = PasswordMode.Prompt };
            }
            else
            {
                step.Password.Value = null;
            }
        }
        var path = Path.Combine(DirectoryPath, copy.Id + ".json");
        WorkflowStore.Save(path, copy);
        return path;
    }

    public void Delete(TemplateDescriptor template)
    {
        if (template.IsBuiltIn || template.FilePath is null) throw new InvalidOperationException("内置模板不能删除。");
        File.Delete(template.FilePath);
    }
}
