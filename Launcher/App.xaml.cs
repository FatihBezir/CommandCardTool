using Sentry;
using System.Windows;
using System.Windows.Threading;

namespace LauncherWinUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = "https://c5fbadac932626fcef118351949df55f@o4509316925554688.ingest.us.sentry.io/4511153204756480";
                options.AutoSessionTracking = true;
                options.Environment = "production";
                options.Release = "generalsonline-launcher@040226";
#if DEBUG
                options.Debug = true;
#endif
            });

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            base.OnStartup(e);
            var window = new MainWindow();
            window.Show();
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            SentrySdk.CaptureException(e.Exception);
            SentrySdk.Flush(System.TimeSpan.FromSeconds(2));
        }
    }
}
