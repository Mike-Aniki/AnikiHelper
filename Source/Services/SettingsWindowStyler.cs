using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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

            // Fullscreen only
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
            // Hide all TextBlocks whose Text is bound to OptionDescription
            var blocks = VisualTreeHelpers.FindVisualChildren<TextBlock>(settingsWindow)
                .Where(tb =>
                {
                    var be = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
                    return be?.ParentBinding?.Path?.Path == "OptionDescription";
                })
                .ToList();

            foreach (var tb in blocks)
            {
                tb.Visibility = Visibility.Collapsed;
                tb.Focusable = false;
                tb.IsHitTestVisible = false;
                KeyboardNavigation.SetIsTabStop(tb, false);
            }

            // Prevent the fullscreen settings ScrollViewer from behaving like the focused element.
            foreach (var sv in VisualTreeHelpers.FindVisualChildren<ScrollViewer>(settingsWindow))
            {
                sv.Focusable = false;
                sv.IsTabStop = false;
                KeyboardNavigation.SetTabNavigation(sv, KeyboardNavigationMode.Continue);
                KeyboardNavigation.SetDirectionalNavigation(sv, KeyboardNavigationMode.Continue);
                KeyboardNavigation.SetControlTabNavigation(sv, KeyboardNavigationMode.Continue);
            }

            // Make normal panels continue navigation instead of cycling inside empty zones.
            foreach (var panel in VisualTreeHelpers.FindVisualChildren<Panel>(settingsWindow))
            {
                KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Continue);
                KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Continue);
                KeyboardNavigation.SetControlTabNavigation(panel, KeyboardNavigationMode.Continue);
            }

            // Force Up/Down to jump to the next real focusable setting instead of scrolling line by line.
            settingsWindow.PreviewKeyDown -= SettingsWindow_PreviewKeyDown;
            settingsWindow.PreviewKeyDown += SettingsWindow_PreviewKeyDown;
        }

        private static void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Down && e.Key != Key.Up)
            {
                return;
            }

            // Do not hijack text editing.
            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            // Do not hijack an opened ComboBox.
            if (Keyboard.FocusedElement is ComboBox comboBox && comboBox.IsDropDownOpen)
            {
                return;
            }

            var window = sender as Window;
            if (window == null)
            {
                return;
            }

            var direction = e.Key == Key.Down ? 1 : -1;

            if (MoveFocusToNextSettingControl(window, direction))
            {
                e.Handled = true;
            }
        }

        private static bool MoveFocusToNextSettingControl(Window window, int direction)
        {
            try
            {
                var controls = VisualTreeHelpers.FindVisualChildren<Control>(window)
                    .Where(c =>
                        c != null &&
                        c.IsVisible &&
                        c.IsEnabled &&
                        c.Focusable &&
                        c.IsTabStop &&
                        c.ActualWidth > 0 &&
                        c.ActualHeight > 0 &&
                        !(c is ScrollViewer))
                    .OrderBy(c => GetElementTop(c, window))
                    .ThenBy(c => GetElementLeft(c, window))
                    .ToList();

                if (controls.Count == 0)
                {
                    return false;
                }

                var focused = Keyboard.FocusedElement as DependencyObject;
                var focusedControl = focused as Control ?? FindParent<Control>(focused);

                int currentIndex = focusedControl != null
                    ? controls.IndexOf(focusedControl)
                    : -1;

                if (currentIndex < 0)
                {
                    currentIndex = FindNearestIndexFromCurrentPosition(controls, focused, window, direction);
                }

                int nextIndex = currentIndex + direction;

                if (nextIndex < 0)
                {
                    nextIndex = 0;
                }

                if (nextIndex >= controls.Count)
                {
                    nextIndex = controls.Count - 1;
                }

                if (nextIndex == currentIndex)
                {
                    return false;
                }

                var target = controls[nextIndex];
                target.Focus();
                Keyboard.Focus(target);
                target.BringIntoView();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int FindNearestIndexFromCurrentPosition(
            List<Control> controls,
            DependencyObject focused,
            Window window,
            int direction)
        {
            if (focused is FrameworkElement fe)
            {
                double currentTop = GetElementTop(fe, window);

                if (direction > 0)
                {
                    for (int i = 0; i < controls.Count; i++)
                    {
                        if (GetElementTop(controls[i], window) > currentTop)
                        {
                            return Math.Max(0, i - 1);
                        }
                    }

                    return controls.Count - 1;
                }
                else
                {
                    for (int i = controls.Count - 1; i >= 0; i--)
                    {
                        if (GetElementTop(controls[i], window) < currentTop)
                        {
                            return Math.Min(controls.Count - 1, i + 1);
                        }
                    }

                    return 0;
                }
            }

            return direction > 0 ? 0 : controls.Count - 1;
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                var parent = LogicalTreeHelper.GetParent(child) ?? VisualTreeHelper.GetParent(child);

                if (parent is T typedParent)
                {
                    return typedParent;
                }

                child = parent;
            }

            return null;
        }

        private static double GetElementTop(FrameworkElement element, Window window)
        {
            try
            {
                return element.TransformToAncestor(window).Transform(new Point(0, 0)).Y;
            }
            catch
            {
                return double.MaxValue;
            }
        }

        private static double GetElementLeft(FrameworkElement element, Window window)
        {
            try
            {
                return element.TransformToAncestor(window).Transform(new Point(0, 0)).X;
            }
            catch
            {
                return double.MaxValue;
            }
        }
    }
}
