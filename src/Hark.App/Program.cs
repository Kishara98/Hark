using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Hark.Core;

namespace Hark.App
{
    internal static class Program
    {
        private const int OpenHotkeyId = 1;
        private const string QuitSignal = "Hark.Quit";

        [STAThread]
        private static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--quit") >= 0)
            {
                // Ask a running Hark to close normally, so its tray icon goes away (install and uninstall use this).
                if (EventWaitHandle.TryOpenExisting(QuitSignal, out var running))
                {
                    using (running)
                    {
                        running.Set();
                    }
                }
                return 0;
            }
            using (var mutex = new Mutex(true, "Hark.SingleInstance", out var first))
            using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitSignal))
            {
                if (!first)
                {
                    return 0;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    AppLog.Write("unhandled: " + e.Exception);
                    e.Handled = true;
                };
                ThreadPool.RegisterWaitForSingleObject(quit, (state, timedOut) =>
                {
                    AppLog.Write("quit requested");
                    app.Dispatcher.BeginInvoke(new Action(app.Shutdown));
                }, null, Timeout.Infinite, true);
                var controller = new Controller();
                HotkeyManager hotkeys = null;
                TrayController tray = null;

                void Open(string sessionId)
                {
                    var session = controller.State.Sessions.FirstOrDefault(x => x.SessionId == sessionId);
                    if (session == null)
                    {
                        AppLog.Write("open: session is gone " + sessionId);
                        return;
                    }
                    var window = WindowMatcher.Pick(WindowFocuser.List(), session.Source, session.Project);
                    if (window == null)
                    {
                        AppLog.Write("no window for " + session.Project);
                        return;
                    }
                    var ok = WindowFocuser.Focus(window);
                    AppLog.Write($"open {session.Project}: {window.ProcessName} \"{window.Title}\" focused={ok}");
                    // VS Code hands the link to the window in front, so the right window goes first.
                    var link = ChatLink.For(session.Source, session.SessionId);
                    if (link != null)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
                            AppLog.Write("opened chat link for session " + session.SessionId);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Write("chat link failed: " + ex.Message);
                        }
                    }
                }

                controller.OpenSession += Open;
                controller.WaitingChanged += waiting => tray?.SetWaiting(waiting);
                app.Startup += (s, e) =>
                {
                    AppLog.Write("Hark started");
                    tray = new TrayController(controller.Settings.Paused);
                    tray.PauseChanged += controller.SetPaused;
                    tray.Quit += app.Shutdown;
                    tray.ResetPill += controller.ResetPillPosition;
                    hotkeys = new HotkeyManager();
                    hotkeys.Pressed += (o, id) =>
                    {
                        var target = controller.TopCard?.SessionId ?? controller.PillTarget;
                        if (id == OpenHotkeyId && target != null)
                        {
                            controller.RequestOpen(target);
                        }
                    };
                    controller.Start();
                };
                // The shortcut is held only while a card or the pill can open something, so other apps keep it otherwise.
                controller.OpenAvailableChanged += available =>
                {
                    if (hotkeys == null)
                    {
                        return;
                    }
                    if (!available)
                    {
                        hotkeys.Unregister(OpenHotkeyId);
                        return;
                    }
                    var text = controller.Settings.OpenHotkey;
                    if (!Hotkey.TryParse(text, out var key) || !hotkeys.Register(OpenHotkeyId, key))
                    {
                        AppLog.Write("could not register the open shortcut " + text);
                    }
                };
                app.Exit += (s, e) =>
                {
                    hotkeys?.Dispose();
                    tray?.Dispose();
                    controller.Stop();
                    AppLog.Write("Hark stopped");
                };
                return app.Run();
            }
        }
    }
}
