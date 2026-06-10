using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AnikiHelper.Services.UI
{
    internal static class MainMenuStyler
    {
        private static DispatcherTimer timer;

        public static void Start()
        {
            if (timer != null)
            {
                return;
            }

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            timer.Tick += Tick;
            timer.Start();

            Application.Current.Exit += (_, __) => Stop();
        }

        public static void Stop()
        {
            try { timer?.Stop(); } catch { }
            timer = null;
        }

        private static void Tick(object sender, EventArgs e)
        {
            try
            {
                if (!(Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled))
                {
                    return;
                }

                var mainMenuWindow = Application.Current.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w =>
                    {
                        var t = w.GetType().FullName ?? "";
                        return t.IndexOf("Playnite.FullscreenApp.Windows.MainMenuWindow", StringComparison.Ordinal) >= 0;
                    });

                if (mainMenuWindow == null)
                {
                    return;
                }

                HideMainMenuPowerButtons(mainMenuWindow);
            }
            catch
            {
                // best-effort
            }
        }

        private static void HideMainMenuPowerButtons(Window mainMenuWindow)
        {
            var templatesToHide = new[]
            {
                "MainMenuShutdowButtonTemplate",
                "MainMenuSuspendButtonTemplate",
                "MainMenuHibernateButtonTemplate",
                "MainMenuRestartButtonTemplate",
                "MainMenuLockSystemButtonTemplate",
                "MainMenuSwithDesktopButtonTemplate",
                "MainMenuExitPlayniteButtonTemplate",
                "MainMenuMinimizeButtonTemplate",
                "MainMenuHelpButtonTemplate",
                "MainMenuPatreonButtonTemplate",
                "MainMenuLogoutUserButtonTemplate"
            };

            foreach (var button in VisualTreeHelpers.FindVisualChildren<Button>(mainMenuWindow))
            {
                foreach (var key in templatesToHide)
                {
                    var template = Application.Current.TryFindResource(key) as DataTemplate;

                    if (template != null && ReferenceEquals(button.ContentTemplate, template))
                    {
                        button.Visibility = Visibility.Collapsed;
                        button.Focusable = false;
                        button.IsTabStop = false;
                        KeyboardNavigation.SetIsTabStop(button, false);
                        break;
                    }
                }
            }
        }
    }
}