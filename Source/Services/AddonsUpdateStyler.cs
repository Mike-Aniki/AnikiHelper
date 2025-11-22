using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnikiHelper
{
    internal static class AddonsUpdateStyler
    {
        private static DispatcherTimer timer;
        private static readonly HashSet<IntPtr> processed = new HashSet<IntPtr>();

        /// Starts a lightweight watcher that applies styling when the AddonsUpdateWindow appears.
        public static void Start()
        {
            if (timer != null) return;

            // Ajout :
            Application.Current.Exit += (_, __) => { timer?.Stop(); timer = null; processed.Clear(); };

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            timer.Tick += Tick;
            timer.Start();
        }

        /// Periodically scans open windows and hooks the AddonsUpdateWindow once per instance.
        private static void Tick(object sender, EventArgs e)
        {
            var app = Application.Current;
            if (app == null) return;

            foreach (Window w in app.Windows)
            {
                // Target only: Playnite.FullscreenApp.Windows.AddonsUpdateWindow
                if (!string.Equals(w.GetType().FullName,
                                   "Playnite.FullscreenApp.Windows.AddonsUpdateWindow",
                                   StringComparison.Ordinal))
                {
                    continue;
                }

                var handle = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                if (handle == IntPtr.Zero || processed.Contains(handle)) continue;

                processed.Add(handle);
                w.Closed += (_, __) => processed.Remove(handle);

                // Defer styling until the visual tree is constructed
                w.Dispatcher.InvokeAsync(() => ApplyToInternalBorders(w), DispatcherPriority.Loaded);
            }
        }

        /// Applies theme-defined styles to the two inner Borders (content and footer).
        private static void ApplyToInternalBorders(Window win)
        {
            try
            {
                // Collect all Border children in the visual tree
                var borders = FindVisualChildren<Border>(win).ToList();
                if (borders.Count == 0) return;

                // Fetch styles defined in Constants.xaml
                var topStyle = Application.Current.TryFindResource("Aniki_AddonsUpdateWindowStyle_Top") as Style;
                var bottomStyle = Application.Current.TryFindResource("Aniki_AddonsUpdateWindowStyle_Bottom") as Style;
                if (topStyle == null && bottomStyle == null) return;

                // Apply styles to specific grid positions only
                foreach (var b in borders)
                {
                    var row = Grid.GetRow(b);
                    var col = Grid.GetColumn(b);

                    // Top panel: Grid.Column=1, Grid.Row=1
                    if (col == 1 && row == 1 && topStyle != null)
                    {
                        // Remove local values defined by Playnite XAML so style setters can take effect
                        b.ClearValue(Border.BackgroundProperty);
                        b.ClearValue(Border.CornerRadiusProperty);
                        b.ClearValue(Border.BorderThicknessProperty);
                        b.ClearValue(Border.BorderBrushProperty);

                        b.Style = topStyle;
                        continue;
                    }

                    // Bottom panel (buttons): Grid.Column=1, Grid.Row=2
                    if (col == 1 && row == 2 && bottomStyle != null)
                    {
                        b.ClearValue(Border.BackgroundProperty);
                        b.ClearValue(Border.CornerRadiusProperty);
                        b.ClearValue(Border.BorderThicknessProperty);
                        b.ClearValue(Border.BorderBrushProperty);

                        b.Style = bottomStyle;
                        continue;
                    }
                }
            }
            catch
            {
                // Intentionally ignore; styling is best-effort and should never break the window
            }
        }

        /// Enumerates all descendants of a given type in the visual tree.
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    yield return match;

                foreach (var sub in FindVisualChildren<T>(child))
                    yield return sub;
            }
        }
    }
}
