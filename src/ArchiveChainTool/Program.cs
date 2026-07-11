using System.Windows.Forms;

namespace ArchiveChainTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
        {
            return SelfTests.Run();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return Environment.ExitCode;
    }
}
