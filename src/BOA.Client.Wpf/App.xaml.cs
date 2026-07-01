using System;
using System.Windows;

namespace BOA.Client.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            System.IO.File.WriteAllText("c:\\Users\\Yusuf\\Desktop\\BOA\\wpf_error.txt", "UnhandledException: " + ex?.ToString());
        };

        this.DispatcherUnhandledException += (sender, args) =>
        {
            System.IO.File.WriteAllText("c:\\Users\\Yusuf\\Desktop\\BOA\\wpf_error.txt", "DispatcherUnhandledException: " + args.Exception?.ToString());
            args.Handled = false;
        };

        base.OnStartup(e);
    }
}
