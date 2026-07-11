namespace ArchiveChainTool.Batch;

internal static class InputBatchBuilder
{
    public static Task<IReadOnlyList<string>> BuildAsync(IEnumerable<string> droppedPaths, CancellationToken token) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in droppedPaths)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(path))
                {
                    TryAdd(path, files);
                }
                else if (Directory.Exists(path))
                {
                    ScanDirectory(path, files, token);
                }
            }
            return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }, token);

    private static void ScanDirectory(string root, HashSet<string> files, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    TryAdd(file, files);
                }
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private static void TryAdd(string path, HashSet<string> files)
    {
        try
        {
            if (ArchiveInspector.DetectFormat(path) != ArchiveFormat.Unknown)
            {
                files.Add(Path.GetFullPath(path));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
