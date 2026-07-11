namespace ArchiveChainTool;

internal enum ArchiveFormat
{
    Unknown,
    Zip,
    Rar,
    SevenZip,
    Tar,
    GZip,
    BZip2,
    Xz
}

internal static class ArchiveInspector
{
    private static readonly byte[][] ZipSignatures =
    [
        [0x50, 0x4B, 0x03, 0x04],
        [0x50, 0x4B, 0x05, 0x06],
        [0x50, 0x4B, 0x07, 0x08]
    ];

    private static readonly byte[] Rar4Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00];
    private static readonly byte[] Rar5Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    public static ArchiveFormat DetectFormat(string filePath)
    {
        Span<byte> header = stackalloc byte[512];
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesRead = stream.Read(header);

        foreach (var signature in ZipSignatures)
        {
            if (StartsWith(header[..bytesRead], signature))
            {
                return ArchiveFormat.Zip;
            }
        }
        if (StartsWith(header[..bytesRead], Rar4Signature) || StartsWith(header[..bytesRead], Rar5Signature))
        {
            return ArchiveFormat.Rar;
        }
        if (StartsWith(header[..bytesRead], [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]))
        {
            return ArchiveFormat.SevenZip;
        }
        if (StartsWith(header[..bytesRead], [0x1F, 0x8B]))
        {
            return ArchiveFormat.GZip;
        }
        if (StartsWith(header[..bytesRead], [0x42, 0x5A, 0x68]))
        {
            return ArchiveFormat.BZip2;
        }
        if (StartsWith(header[..bytesRead], [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]))
        {
            return ArchiveFormat.Xz;
        }
        if (bytesRead >= 262 && header[257..262].SequenceEqual("ustar"u8))
        {
            return ArchiveFormat.Tar;
        }
        return ArchiveFormat.Unknown;
    }

    public static string FindExactlyOne(
        string rootDirectory,
        ArchiveFormat expectedFormat,
        string? requiredExtension,
        RunLogger logger)
    {
        var candidates = Directory
            .EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => requiredExtension is null ||
                string.Equals(Path.GetExtension(path), requiredExtension, StringComparison.OrdinalIgnoreCase))
            .Where(path => DetectFormat(path) == expectedFormat)
            .ToArray();

        logger.Write($"候选文件数量: {candidates.Length}");
        foreach (var candidate in candidates)
        {
            logger.Write($"候选: {candidate}");
        }

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"在 {rootDirectory} 中没有找到要求的{Describe(expectedFormat, requiredExtension)}。"),
            _ => throw new InvalidOperationException(
                $"在 {rootDirectory} 中找到多个符合条件的{Describe(expectedFormat, requiredExtension)}，无法安全确定目标。")
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix) =>
        value.Length >= prefix.Length && value[..prefix.Length].SequenceEqual(prefix);

    private static string Describe(ArchiveFormat format, string? extension)
    {
        var formatText = format == ArchiveFormat.Zip ? " ZIP 文件" : " RAR 文件";
        return extension is null ? formatText : $" {extension}（实际为{formatText.Trim()}）文件";
    }
}
