using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Susharka;

public partial class App : Application
{
    private Mutex? _single;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = new Mutex(true, @"Local\Susharka.SingleInstance", out var first);
        if (!first)
        {
            // Already running (the tray icon may be hidden in the overflow): open its Settings, then leave.
            try { EventWaitHandle.OpenExisting(AppController.ShowSignalName).Set(); } catch (Exception) { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        _controller = new AppController();
        _controller.Start();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep living in the tray: a glitch in one gesture should not take the whole line down.
        System.Diagnostics.Debug.WriteLine(e.Exception);
        AppController.Log(e.Exception.ToString());
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
