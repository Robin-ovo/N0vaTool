namespace N0vaCacheWallpaperTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => WriteCrash(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException +=
            (_, eventArgs) => WriteCrash(eventArgs.ExceptionObject as Exception);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            ToolPaths.EnsureCreated();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            WriteCrash(ex);
            MessageBox.Show(
                ex.ToString(),
                "N0va 缓存壁纸工具",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void WriteCrash(Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(ToolPaths.Root);
            File.AppendAllText(
                Path.Combine(ToolPaths.Root, "crash.log"),
                $"[{DateTime.Now:O}]\n{exception}\n\n");
        }
        catch
        {
        }
    }
}
