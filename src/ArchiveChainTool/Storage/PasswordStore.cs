using System.Text.Json;

namespace ArchiveChainTool.Storage;

internal sealed class PasswordEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

internal sealed class PasswordVault
{
    public int SchemaVersion { get; set; } = 1;
    public List<PasswordEntry> Entries { get; set; } = [];
}

internal sealed class PasswordStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string FilePath { get; }

    public PasswordStore(string baseDirectory)
    {
        FilePath = Path.Combine(baseDirectory, "data", "passwords.json");
    }

    public PasswordVault Load()
    {
        if (!File.Exists(FilePath))
        {
            return new PasswordVault();
        }
        return JsonSerializer.Deserialize<PasswordVault>(File.ReadAllText(FilePath), Options)
            ?? new PasswordVault();
    }

    public void Save(PasswordVault vault)
    {
        foreach (var entry in vault.Entries.Where(entry => entry.Id == Guid.Empty)) entry.Id = Guid.NewGuid();
        Validate(vault);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(vault, Options), new System.Text.UTF8Encoding(false));
        File.Move(temp, FilePath, overwrite: true);
    }

    private static void Validate(PasswordVault vault)
    {
        if (vault.Entries.Any(entry => string.IsNullOrWhiteSpace(entry.Name)))
        {
            throw new InvalidOperationException("密码条目名称不能为空。");
        }
        if (vault.Entries.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("密码条目名称不能重复。");
        }
    }
}
