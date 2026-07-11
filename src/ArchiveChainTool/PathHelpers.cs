namespace ArchiveChainTool;

internal static class PathHelpers
{
    public static string CreateUniqueDirectory(string parentDirectory, string preferredName)
    {
        Directory.CreateDirectory(parentDirectory);

        for (var index = 0; ; index++)
        {
            var suffix = index == 0 ? string.Empty : $"_{index}";
            var candidate = Path.Combine(parentDirectory, preferredName + suffix);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
            {
                // Another process won the name; try the next suffix.
            }
        }
    }

    public static string MoveWithUniqueName(string sourcePath, string preferredFileName)
    {
        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("无法确定文件所在目录。");
        var baseName = Path.GetFileNameWithoutExtension(preferredFileName);
        var extension = Path.GetExtension(preferredFileName);

        for (var index = 0; ; index++)
        {
            var suffix = index == 0 ? string.Empty : $"_{index}";
            var destination = Path.Combine(directory, baseName + suffix + extension);

            if (string.Equals(sourcePath, destination, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath;
            }
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                continue;
            }

            try
            {
                File.Move(sourcePath, destination, overwrite: false);
                return destination;
            }
            catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
            {
                // A concurrent process created it; try the next suffix.
            }
        }
    }

    public static void EnsureFileIsDirectChild(string filePath, string parentDirectory)
    {
        var fullFilePath = Path.GetFullPath(filePath);
        var fullParent = Path.GetFullPath(parentDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var actualParent = Path.GetDirectoryName(fullFilePath)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (!string.Equals(actualParent, fullParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请选择 EXE 所在目录中的初始 RAR 文件。");
        }
    }

    public static void EnsureEmptyDirectory(string directory)
    {
        if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new InvalidOperationException($"输出目录不是空目录：{directory}");
        }
    }

    public static void AuditExtractedTree(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var path in Directory.EnumerateFileSystemEntries(rootDirectory, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"检测到解压目录之外的路径：{path}");
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"检测到不允许的链接或重解析点：{path}");
            }
        }
    }

    public static void ValidateSimpleFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." ||
            fileName != Path.GetFileName(fileName) || fileName.EndsWith(' ') || fileName.EndsWith('.') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.Contains(':'))
        {
            throw new InvalidOperationException($"无效的目标文件名：{fileName}");
        }

        var stem = Path.GetFileNameWithoutExtension(fileName).TrimEnd('.', ' ');
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"目标文件名是 Windows 保留名称：{fileName}");
        }
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "解压任务" : sanitized;
    }
}
