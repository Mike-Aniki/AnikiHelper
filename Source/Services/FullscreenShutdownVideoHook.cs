using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace AnikiHelper
{
    internal static class FullscreenShutdownVideoHook
    {
        private const int MainMenuScanIntervalMs = 250;
        

        private static DispatcherTimer timer;
        private static readonly HashSet<int> hookedButtons = new HashSet<int>();
        private static AnikiHelper plugin;

        public static void Start(AnikiHelper owner)
        {
            if (timer != null)
            {
                return;
            }

            plugin = owner;

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(MainMenuScanIntervalMs)
            };

            timer.Tick += Tick;
            timer.Start();

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
            plugin = null;

            hookedButtons.Clear();
        }

        private static void Tick(object sender, EventArgs e)
        {
            var app = Application.Current;
            if (app == null || plugin == null)
            {
                return;
            }

            foreach (var win in app.Windows.Cast<Window>())
            {
                var windowType = win.GetType().FullName ?? string.Empty;
                var dataContextType = win.DataContext?.GetType().FullName ?? string.Empty;

                bool looksLikeMainMenuWindow =
                    windowType.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    dataContextType.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0;

                // On scanne aussi la fenêtre principale pour trouver les boutons customs du thème.
                bool isMainWindow = ReferenceEquals(win, Application.Current?.MainWindow);

                if (!looksLikeMainMenuWindow && !isMainWindow)
                {
                    continue;
                }

                win.Dispatcher.InvokeAsync(() => ScanWindow(win), DispatcherPriority.Loaded);
            }
        }

        private static void ScanWindow(Window win)
        {
            foreach (var btn in VisualTreeHelpers.FindVisualChildren<Button>(win))
            {
                var key = btn.GetHashCode();
                if (hookedButtons.Contains(key))
                {
                    continue;
                }

                var name = btn.Name ?? string.Empty;
                var content = btn.Content?.ToString() ?? string.Empty;

                var be = BindingOperations.GetBindingExpression(btn, ButtonBase.CommandProperty);
                var commandPath = be?.ParentBinding?.Path?.Path ?? string.Empty;
                var commandType = btn.Command?.GetType().FullName ?? string.Empty;

                if (!LooksLikeExitPlayniteButton(name, content, commandPath, commandType))
                {
                    continue;
                }

                btn.Command = new RelayCommand(async () => await plugin.ShowShutdownVideoAndExitAsync());
                btn.CommandParameter = null;

                hookedButtons.Add(key);
            }
        }

        private static bool LooksLikeExitPlayniteButton(string name, string content, string commandPath, string commandType)
        {
            var blob = $"{name}|{content}|{commandPath}|{commandType}".ToLowerInvariant();

            // Exclusions système
            if (blob.Contains("restart") ||
                blob.Contains("reboot") ||
                blob.Contains("poweroff") ||
                blob.Contains("shutdownsystem") ||
                blob.Contains("shutdownpc") ||
                blob.Contains("shutdowncomputer") ||
                blob.Contains("sleep") ||
                blob.Contains("hibernate") ||
                blob.Contains("veille") ||
                blob.Contains("redém"))
            {
                return false;
            }

            // Exclusions spécifiques : boutons qui parlent de Playnite mais ne quittent pas Playnite
            if (name.Equals("GameStatusButton", StringComparison.OrdinalIgnoreCase) ||
                commandPath.IndexOf("CloseGameStatusCommand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                content.IndexOf("Open Playnite", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            // Ton bouton custom
            if (name.Equals("ExitCommand", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Bouton Playnite / commandes classiques
            if ((blob.Contains("playnite") &&
                 (blob.Contains("exit") ||
                  blob.Contains("quit") ||
                  blob.Contains("quitter") ||
                  blob.Contains("fermer")))
                || blob.Contains("exitplaynite")
                || blob.Contains("quitplaynite")
                || blob.Contains("shutdownplaynite")
                || commandPath.Equals("ExitCommand", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}