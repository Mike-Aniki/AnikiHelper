using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AnikiHelper.Services.UI
{
    internal static class MainMenuStyler
    {
        private const int WatchIntervalMs = 500;

        private static DispatcherTimer timer;
        private static readonly HashSet<Window> trackedWindows = new HashSet<Window>();

        public static void Start()
        {
            if (timer != null)
            {
                return;
            }

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(WatchIntervalMs)
            };

            timer.Tick += Tick;
            timer.Start();

            // Apply immediately if the menu is already open.
            Tick(null, EventArgs.Empty);

            Application.Current.Exit += (_, __) => Stop();
        }

        public static void Stop()
        {
            try
            {
                if (timer != null)
                {
                    timer.Tick -= Tick;
                    timer.Stop();
                }
            }
            catch { }

            timer = null;
            trackedWindows.Clear();
        }

        private static void Tick(object sender, EventArgs e)
        {
            try
            {
                if (!(Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled))
                {
                    return;
                }

                foreach (var mainMenuWindow in Application.Current.Windows
                    .OfType<Window>()
                    .Where(IsMainMenuWindow)
                    .ToArray())
                {
                    if (!mainMenuWindow.IsLoaded || !trackedWindows.Add(mainMenuWindow))
                    {
                        continue;
                    }

                    mainMenuWindow.Closed += OnMainMenuWindowClosed;
                    ApplyTemporaryPassesAsync(mainMenuWindow);
                }
            }
            catch
            {
                // best-effort
            }
        }

        private static bool IsMainMenuWindow(Window window)
        {
            var typeName = window?.GetType().FullName ?? string.Empty;
            return typeName.IndexOf(
                "Playnite.FullscreenApp.Windows.MainMenuWindow",
                StringComparison.Ordinal) >= 0;
        }

        private static void OnMainMenuWindowClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnMainMenuWindowClosed;
                trackedWindows.Remove(window);
            }
        }

        private static async void ApplyTemporaryPassesAsync(Window window)
        {
            // A few short-lived passes cover controls generated after Loaded,
            // without traversing the visual tree every 250 ms for the whole session.
            var delays = new[] { 0, 150, 400, 800 };

            foreach (var delay in delays)
            {
                if (delay > 0)
                {
                    await Task.Delay(delay).ConfigureAwait(true);
                }

                if (!trackedWindows.Contains(window) || !window.IsLoaded)
                {
                    return;
                }

                try
                {
                    HideMainMenuPowerButtons(window);
                }
                catch
                {
                    // best-effort visual patch
                }
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
