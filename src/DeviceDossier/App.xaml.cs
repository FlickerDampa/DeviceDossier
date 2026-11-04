using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DeviceDossier.Core;
using DeviceDossier.Util;
using DeviceDossier.Views;

namespace DeviceDossier
{
    public partial class App : Application
    {
        public const string ElevatedFlag = "--elevated";

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => WriteCrashLog(a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { WriteCrashLog(a.Exception); a.SetObserved(); };

            if (Cli.Handles(e.Args))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int code = Cli.Run(e.Args);
                Shutdown(code);
                return;
            }

            var settings = AuditSettings.Load();
            if (settings.LoadedFromLegacyFile)
            {
                try
                {
                    settings.Save();
                }
                catch
                {
                }
            }
            bool alreadyTried = e.Args.Any(a => string.Equals(a, ElevatedFlag, StringComparison.OrdinalIgnoreCase));
            if (settings.RequestAdminAtStartup && !alreadyTried && !Elevation.IsAdministrator())
            {
                if (Elevation.TryRestartElevated(ElevatedFlag))
                {
                    Shutdown();
                    return;
                }
            }

            base.OnStartup(e);
            ThemeManager.Initialize(settings.Theme);
            var window = new MainWindow(settings);
            MainWindow = window;
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string log = WriteCrashLog(e.Exception);
            MessageBox.Show(
                "Something went wrong:\n\n" + e.Exception.Message + (log != null ? "\n\nDetails were saved to:\n" + log : ""),
                "DeviceDossier", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        public static string WriteCrashLog(Exception ex)
        {
            if (ex == null) return null;
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "DeviceDossier_error.log");
                File.AppendAllText(path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + Environment.NewLine + Environment.NewLine);
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
