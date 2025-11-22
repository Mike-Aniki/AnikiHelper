using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AnikiHelper
{
    internal static class SettingsWindowStyler
    {
        private static DispatcherTimer timer;
        private static bool patchedThisOpen;

        public static void Start()
        {
            if (timer != null) return;

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            timer.Tick += Tick;
            timer.Start();

            Application.Current.Exit += (_, __) => { Stop(); };
        }

        public static void Stop()
        {
            try { timer?.Stop(); } catch { }
            timer = null;
            patchedThisOpen = false;
        }

        private static void Tick(object sender, EventArgs e)
        {
            var app = Application.Current;
            if (app == null) return;

            // Fullscreen uniquement
            var win = app.Windows.Cast<Window>().FirstOrDefault(w =>
            {
                var t = w.GetType().FullName ?? "";
                return t.IndexOf("Playnite.FullscreenApp.Windows.SettingsWindow", StringComparison.Ordinal) >= 0;
            });


            if (win == null)
            {
                patchedThisOpen = false; // reset for next opening
                return;
            }

            if (!patchedThisOpen)
            {
                patchedThisOpen = true;
                win.Dispatcher.InvokeAsync(() =>
                {
                    try { ApplyFix(win); } catch { /* best-effort */ }
                }, DispatcherPriority.Loaded);
            }
        }

        // Hides all TextBlocks whose Text is bound to OptionDescription
        private static void ApplyFix(Window settingsWindow)
        {
            var blocks = VisualTreeHelpers.FindVisualChildren<TextBlock>(settingsWindow)
                .Where(tb =>
                {
                    var be = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
                    return be?.ParentBinding?.Path?.Path == "OptionDescription";
                })
                .ToList();

            foreach (var tb in blocks)
                tb.Visibility = Visibility.Collapsed;
        }
    }
}
