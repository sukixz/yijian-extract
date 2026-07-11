using System.Text.Json.Serialization;

namespace ArchiveChainTool.Workflows;

internal enum StepType { Extract, Rename }
internal enum PasswordMode { None, Stored, Prompt, PasswordLibrary }
internal enum CollisionPolicy { AppendNumber, Fail }
internal enum ExecutionMode { CustomWorkflow, AutomaticChain }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ExtractStepDefinition), "extract")]
[JsonDerivedType(typeof(RenameStepDefinition), "rename")]
internal abstract class WorkflowStepDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    [JsonIgnore] public abstract StepType StepType { get; }
    public CandidateRule Candidate { get; set; } = new();
}

internal sealed class ExtractStepDefinition : WorkflowStepDefinition
{
    [JsonIgnore] public override StepType StepType => StepType.Extract;
    public PasswordOptions Password { get; set; } = new();
    public string OutputName { get; set; } = "解压结果";
}

internal sealed class RenameStepDefinition : WorkflowStepDefinition
{
    [JsonIgnore] public override StepType StepType => StepType.Rename;
    public string TargetFileName { get; set; } = "新文件.zip";
    public CollisionPolicy CollisionPolicy { get; set; } = CollisionPolicy.AppendNumber;
}

internal sealed class CandidateRule
{
    public string Extensions { get; set; } = string.Empty;
    public string Formats { get; set; } = string.Empty;
    public string NamePattern { get; set; } = "*";
    public bool Recursive { get; set; } = true;
    public bool AskWhenMultiple { get; set; } = true;
}

internal sealed class PasswordOptions
{
    public PasswordMode Mode { get; set; }
    public string? Value { get; set; }
    public List<Guid> LibraryEntryIds { get; set; } = [];
}

internal sealed class WorkflowDefinition
{
    public int SchemaVersion { get; set; } = 3;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新方案";
    public string Description { get; set; } = string.Empty;
    public ExecutionMode ExecutionMode { get; set; }
    public AutomaticChainOptions Automatic { get; set; } = new();
    public bool KeepIntermediateFiles { get; set; } = true;
    public List<WorkflowStepDefinition> Steps { get; set; } = [];
}

internal sealed class AutomaticChainOptions
{
    public int MaxLayers { get; set; } = 10;
    public bool AskWhenMultiple { get; set; } = true;
    public bool CorrectDisguisedJpegExtension { get; set; } = true;
}
