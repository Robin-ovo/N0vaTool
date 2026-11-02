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
            ToolLogger.Info(
                $"程序启动：版本={Application.ProductVersion}，" +
                $"目录={AppContext.BaseDirectory}，" +
                $"系统={Environment.OSVersion}");
            Application.Run(new MainForm());
            ToolLogger.Info("程序正常退出。");
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
        ToolLogger.Error("未处理异常", exception ?? new Exception("未知异常"));
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
