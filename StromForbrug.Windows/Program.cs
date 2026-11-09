using Velopack;

namespace StromForbrug.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Must be the first thing to run: handles Velopack's install/update/uninstall hooks.
        VelopackApp.Build().Run();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
