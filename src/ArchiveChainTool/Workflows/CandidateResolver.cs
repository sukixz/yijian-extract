using System.Text.RegularExpressions;
using ArchiveChainTool.Workflows;

namespace ArchiveChainTool;

internal static class CandidateResolver
{
    public static IReadOnlyList<string> Find(string currentArtifact, CandidateRule rule)
    {
        IEnumerable<string> paths;
        if (File.Exists(currentArtifact))
        {
            paths = [currentArtifact];
        }
        else if (Directory.Exists(currentArtifact))
        {
            paths = EnumerateSafeFiles(currentArtifact, rule.Recursive);
        }
        else
        {
            throw new FileNotFoundException("当前步骤输入不存在。", currentArtifact);
        }

        var extensions = SplitValues(rule.Extensions)
            .Select(value => value.StartsWith('.') ? value : "." + value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var formats = SplitValues(rule.Formats)
            .Select(ParseFormat)
            .ToHashSet();
        var pattern = string.IsNullOrWhiteSpace(rule.NamePattern) ? "*" : rule.NamePattern.Trim();
        var regex = WildcardToRegex(pattern);

        return paths
            .Where(path => regex.IsMatch(Path.GetFileName(path)))
            .Where(path => extensions.Count == 0 || extensions.Contains(Path.GetExtension(path)))
            .Where(path => formats.Count == 0 || formats.Contains(ArchiveInspector.DetectFormat(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateSafeFiles(string directory, bool recursive)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                {
                    yield return file;
                }
            }
            if (!recursive)
            {
                continue;
            }
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static Regex WildcardToRegex(string wildcard) => new(
        "^" + Regex.Escape(wildcard).Replace("\\*", ".*").Replace("\\?", ".") + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string[] SplitValues(string value) => value.Split(
        [',', ';', ' ', '\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static ArchiveFormat ParseFormat(string value) => value.Trim().ToLowerInvariant() switch
    {
        "zip" => ArchiveFormat.Zip,
        "rar" or "rar4" or "rar5" => ArchiveFormat.Rar,
        "7z" or "sevenzip" => ArchiveFormat.SevenZip,
        "tar" => ArchiveFormat.Tar,
        "gz" or "gzip" => ArchiveFormat.GZip,
        "bz2" or "bzip2" => ArchiveFormat.BZip2,
        "xz" => ArchiveFormat.Xz,
        _ => throw new InvalidOperationException($"不支持的真实格式筛选值：{value}")
    };
}
