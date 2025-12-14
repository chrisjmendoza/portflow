using System;
using System.Windows;

namespace PortFlow.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "PortFlow Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            try
            {
                MessageBox.Show(ex.ExceptionObject?.ToString() ?? "Unknown error",
                    "PortFlow Crash (Non-UI)", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* last ditch */ }
        };

        base.OnStartup(e);
    }
}
