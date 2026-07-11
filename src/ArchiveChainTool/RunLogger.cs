namespace ArchiveChainTool;

internal sealed class RunLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sync = new();

    public string LogPath { get; }

    public RunLogger(string logPath)
    {
        LogPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _writer = new StreamWriter(logPath, append: false, new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    public void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (_sync)
        {
            _writer.WriteLine(line);
        }
        Console.WriteLine(line);
    }

    public void WriteBlock(string title, string content)
    {
        lock (_sync)
        {
            _writer.WriteLine($"--- {title} ---");
            _writer.WriteLine(content.TrimEnd());
            _writer.WriteLine($"--- /{title} ---");
        }
    }

    public void Dispose() => _writer.Dispose();
}
