using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AnikiHelper
{
    internal static class FastScrollViewerService
    {
        private static bool isRunning;
        private static bool classHandlerRegistered;
        private static bool exitHandlerRegistered;

        private static readonly DependencyProperty IsPatchedProperty =
            DependencyProperty.RegisterAttached(
                "IsPatched",
                typeof(bool),
                typeof(FastScrollViewerService),
                new PropertyMetadata(false));

        public static void Start()
        {
            if (isRunning)
            {
                return;
            }

            isRunning = true;

            // Register a single global Loaded handler for ScrollViewer controls.
            // This replaces the previous full visual-tree scan that ran every 500 ms.
            if (!classHandlerRegistered)
            {
                EventManager.RegisterClassHandler(
                    typeof(ScrollViewer),
                    FrameworkElement.LoadedEvent,
                    new RoutedEventHandler(ScrollViewer_Loaded),
                    true);

                classHandlerRegistered = true;
            }

            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            if (!exitHandlerRegistered)
            {
                app.Exit += Application_Exit;
                exitHandlerRegistered = true;
            }

            // The service starts after Playnite's UI is already visible, so patch the
            // currently loaded controls once. Any control loaded later is handled by
            // ScrollViewer_Loaded without polling.
            PatchExistingScrollViewers(app);
        }

        public static void Stop()
        {
            isRunning = false;
        }

        private static void Application_Exit(object sender, ExitEventArgs e)
        {
            Stop();
        }

        private static void ScrollViewer_Loaded(object sender, RoutedEventArgs e)
        {
            if (!isRunning)
            {
                return;
            }

            PatchScrollViewer(sender as ScrollViewer);
        }

        private static void PatchExistingScrollViewers(Application app)
        {
            try
            {
                foreach (Window window in app.Windows)
                {
                    if (window == null || !window.IsVisible)
                    {
                        continue;
                    }

                    foreach (var scrollViewer in VisualTreeHelpers.FindVisualChildren<ScrollViewer>(window))
                    {
                        PatchScrollViewer(scrollViewer);
                    }
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static void PatchScrollViewer(ScrollViewer scrollViewer)
        {
            try
            {
                if (!ShouldPatch(scrollViewer))
                {
                    return;
                }

                if ((bool)scrollViewer.GetValue(IsPatchedProperty))
                {
                    return;
                }

                scrollViewer.SetValue(IsPatchedProperty, true);
                scrollViewer.Focusable = true;

                scrollViewer.PreviewKeyDown -= ScrollViewer_PreviewKeyDown;
                scrollViewer.PreviewKeyDown += ScrollViewer_PreviewKeyDown;
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
