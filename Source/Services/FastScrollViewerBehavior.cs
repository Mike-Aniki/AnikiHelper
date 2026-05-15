using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AnikiHelper
{
    internal static class FastScrollViewerService
    {
        private static DispatcherTimer timer;

        private static readonly DependencyProperty IsPatchedProperty =
            DependencyProperty.RegisterAttached(
                "IsPatched",
                typeof(bool),
                typeof(FastScrollViewerService),
                new PropertyMetadata(false));

        public static void Start()
        {
            if (timer != null)
            {
                return;
            }

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            timer.Tick += Tick;
            timer.Start();

            Application.Current.Exit += (_, __) => Stop();
        }

        public static void Stop()
        {
            try
            {
                timer?.Stop();
            }
            catch
            {
            }

            timer = null;
        }

        private static void Tick(object sender, EventArgs e)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            foreach (Window window in app.Windows)
            {
                if (window == null || !window.IsVisible)
                {
                    continue;
                }

                PatchWindow(window);
            }
        }

        private static void PatchWindow(Window window)
        {
            try
            {
                foreach (var scrollViewer in VisualTreeHelpers.FindVisualChildren<ScrollViewer>(window))
                {
                    if (!ShouldPatch(scrollViewer))
                    {
                        continue;
                    }

                    if ((bool)scrollViewer.GetValue(IsPatchedProperty))
                    {
                        continue;
                    }

                    scrollViewer.SetValue(IsPatchedProperty, true);
                    scrollViewer.Focusable = true;

                    scrollViewer.PreviewKeyDown -= ScrollViewer_PreviewKeyDown;
                    scrollViewer.PreviewKeyDown += ScrollViewer_PreviewKeyDown;
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static bool ShouldPatch(ScrollViewer scrollViewer)
        {
            if (scrollViewer == null)
            {
                return false;
            }

            // Option 1:
            // <ScrollViewer Tag="AnikiFastScroll" />
            // <ScrollViewer Tag="AnikiFastScroll:150" />
            var tag = scrollViewer.Tag as string;
            if (!string.IsNullOrWhiteSpace(tag) &&
                tag.StartsWith("AnikiFastScroll", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Option 2:
            // <ScrollViewer x:Name="NewsFastScrollViewer" />
            var name = scrollViewer.Name;
            if (!string.IsNullOrWhiteSpace(name) &&
                name.IndexOf("FastScroll", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static void ScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null)
            {
                return;
            }

            if (e.Key != Key.Up &&
                e.Key != Key.Down &&
                e.Key != Key.PageUp &&
                e.Key != Key.PageDown)
            {
                return;
            }

            // Do not steal input from interactive controls if this behavior is reused later.
            if (Keyboard.FocusedElement is TextBox ||
                Keyboard.FocusedElement is ComboBox ||
                Keyboard.FocusedElement is Slider ||
                Keyboard.FocusedElement is ListBox ||
                Keyboard.FocusedElement is ListView)
            {
                return;
            }

            var step = GetStep(scrollViewer);

            if (e.Key == Key.Down)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + step);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - step);
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollViewer.ViewportHeight * 0.85);
                e.Handled = true;
            }
            else if (e.Key == Key.PageUp)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollViewer.ViewportHeight * 0.85);
                e.Handled = true;
            }
        }

        private static double GetStep(ScrollViewer scrollViewer)
        {
            const double defaultStep = 120.0;

            var tag = scrollViewer.Tag as string;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return defaultStep;
            }

            // Supported formats:
            // Tag="AnikiFastScroll"
            // Tag="AnikiFastScroll:150"
            var parts = tag.Split(':');
            if (parts.Length < 2)
            {
                return defaultStep;
            }

            double value;
            if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value;
            }

            if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.CurrentCulture, out value) && value > 0)
            {
                return value;
            }

            return defaultStep;
        }
    }
}