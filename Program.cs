using System;
using System.IO;
using System.Text;
using System.Windows;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (_, e) =>
            {
                WriteErrorLog(e.Exception);
                MessageBox.Show(
                    "Translate online 遇到启动或界面错误，详细信息已保存到：\n" +
                    GetLogPath() + "\n\n" + e.Exception.Message,
                    "Translate online 启动错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                e.Handled = true;
                app.Shutdown(-1);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                    WriteErrorLog(exception);
            };

            app.Run(new TranslateWindow());
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
            MessageBox.Show(
                "Translate online 无法启动。错误详情已保存到：\n" +
                GetLogPath() + "\n\n" + ex,
                "Translate online 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string GetLogPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TranslateOnline",
            "startup-error.log");

    private static void WriteErrorLog(Exception exception)
    {
        try
        {
            var path = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{exception}\n\n",
                new UTF8Encoding(false));
        }
        catch
        {
            // Logging must never prevent the error dialog from being shown.
        }
    }
}
