using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;

namespace AnikiHelper.Services
{
    public class AnikiWindowCommandProvider
    {
        private readonly Func<string, ICommand> commandFactory;
        private readonly Dictionary<string, ICommand> cache = new Dictionary<string, ICommand>();

        public AnikiWindowCommandProvider(Func<string, ICommand> commandFactory)
        {
            this.commandFactory = commandFactory;
        }

        public ICommand this[string styleKey]
        {
            get
            {
                if (string.IsNullOrWhiteSpace(styleKey))
                    return null;

                if (!cache.TryGetValue(styleKey, out var command))
                {
                    command = commandFactory(styleKey);
                    cache[styleKey] = command;
                }

                return command;
            }
        }
    }

    public class AnikiWindowManager
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly Stack<Window> windows = new Stack<Window>();
        private const string QuickAccessWindowStyleName = "QuickAccessWindowStyle";

        public AnikiWindowManager(IPlayniteAPI playniteApi)
        {
            this.playniteApi = playniteApi;
        }

        public bool HasOpenWindow => windows.Any();

        public void OpenWindow(string parameter)
        {
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick);
            Open(styleKey, false, focusTargetName, focusFirst, refocusAfterClick);
        }

        public void OpenWindow(string styleKey, string focusTargetName)
        {
            Open(styleKey, false, focusTargetName, false, false);
        }

        public void OpenChildWindow(string parameter)
        {
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick);
            Open(styleKey, true, focusTargetName, focusFirst, refocusAfterClick);
        }

        private void ParseOpenParameter(string parameter, out string styleKey, out string focusTargetName, out bool focusFirst, out bool refocusAfterClick)
        {
            styleKey = parameter;
            focusTargetName = null;
            focusFirst = false;
            refocusAfterClick = false;

            if (string.IsNullOrWhiteSpace(parameter) || !parameter.Contains("|"))
            {
                return;
            }

            var parts = parameter.Split('|');

            styleKey = parts.Length > 0 ? parts[0] : parameter;

            for (int i = 1; i < parts.Length; i++)
            {
                var option = parts[i]?.Trim();

                if (string.IsNullOrWhiteSpace(option))
                {
                    continue;
                }

                if (IsFocusFirstOption(option))
                {
                    focusFirst = true;
                    continue;
                }

                if (IsRefocusAfterClickOption(option))
                {
                    refocusAfterClick = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(focusTargetName))
                {
                    focusTargetName = option;
                }
            }
        }

        private static bool IsFocusFirstOption(string option)
        {
            return string.Equals(option, "FocusFirst", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "AutoFocusFirst", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "FirstFocus", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRefocusAfterClickOption(string option)
        {
            return string.Equals(option, "RefocusAfterClick", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "RefocusOnClick", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "RefocusAfterAction", StringComparison.OrdinalIgnoreCase);
        }

        public bool CloseTopWindow()
        {
            CleanupClosedWindows();

            if (!windows.Any())
                return false;

            var top = windows.Pop();

            if (top != null && top.IsVisible)
            {
                top.Hide();

                top.Dispatcher.BeginInvoke(new Action(() =>
                {
                    top.Close();
                }), DispatcherPriority.ApplicationIdle);
            }

            FocusTopWindow();
            return true;
        }

        public bool IsTopWindowActive()
        {
            CleanupClosedWindows();

            if (!windows.Any())
                return false;

            var top = windows.Peek();
            return top != null && top.IsActive;
        }

        private void Open(string styleKey, bool forceChild, string focusTargetName, bool focusFirst, bool refocusAfterClick)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                CleanupClosedWindows();

                if (IsPlayniteSettingsWindowOpen())
                {
                    return;
                }

                var existingWindow = windows.FirstOrDefault(w =>
                    w != null &&
                    w.IsVisible &&
                    string.Equals(w.Tag as string, styleKey, StringComparison.OrdinalIgnoreCase));

                if (existingWindow != null)
                {
                    existingWindow.Activate();
                    existingWindow.Focus();
                    return;
                }

                if (!string.Equals(styleKey, QuickAccessWindowStyleName, StringComparison.OrdinalIgnoreCase))
                {
                    CloseWindowByStyleKey(QuickAccessWindowStyleName);
                    CleanupClosedWindows();
                }

                var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false
                });

                window.Tag = styleKey;
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.WindowState = WindowState.Maximized;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var parent = playniteApi.Dialogs.GetCurrentAppWindow();
                if (parent != null)
                {
                    window.Width = parent.Width;
                    window.Height = parent.Height;
                }

                if (forceChild)
                {
                    window.AllowsTransparency = true;
                    window.Background = Brushes.Transparent;
                }

                var style = Application.Current.TryFindResource(styleKey) as Style;
                if (style != null)
                {
                    window.Content = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        Child = new Grid
                        {
                            Width = 1920,
                            Height = 1080,
                            Children =
                            {
                                new ContentControl
                                {
                                    Focusable = false,
                                    Style = style
                                }
                            }
                        }
                    };
                }

                window.Owner = forceChild && windows.Any()
                    ? windows.Peek()
                    : parent;

                window.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        CloseTopWindow();
                    }
                };

                window.Closed += (s, e) =>
                {
                    RemoveWindow(window);
                };

                windows.Push(window);

                window.Show();
                window.Activate();
                window.Focus();

                if (refocusAfterClick)
                {
                    AttachRefocusAfterClick(window);
                }

                if (!string.IsNullOrWhiteSpace(focusTargetName) || focusFirst)
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ApplyInitialFocus(window, focusTargetName, focusFirst);
                    }), DispatcherPriority.ApplicationIdle);
                }
            });
        }

        private static bool IsPlayniteSettingsWindowOpen()
        {
            return Application.Current.Windows
                .OfType<Window>()
                .Any(w =>
                    w.IsVisible &&
                    (w.GetType().FullName ?? "").IndexOf(
                        "SettingsWindow",
                        StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
                return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                var element = child as T;
                if (element != null && element.Name == name)
                    return element;

                var result = FindVisualChildByName<T>(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }

        private static void ApplyInitialFocus(Window window, string focusTargetName, bool focusFirst)
        {
            if (window == null || !window.IsVisible)
            {
                return;
            }

            window.UpdateLayout();

            FrameworkElement target = null;

            if (!string.IsNullOrWhiteSpace(focusTargetName))
            {
                target = FindVisualChildByName<FrameworkElement>(window, focusTargetName);
            }

            if (target == null && focusFirst)
            {
                var focusedElement = Keyboard.FocusedElement as FrameworkElement;

                if (focusedElement != null &&
                    !ReferenceEquals(focusedElement, window) &&
                    IsDescendantOf(focusedElement, window))
                {
                    return;
                }

                target = FindFirstFocusableElement(window);
            }

            if (target == null)
            {
                return;
            }

            target.Focus();
            Keyboard.Focus(target);

            var focusScope = FocusManager.GetFocusScope(target);

            if (focusScope != null)
            {
                FocusManager.SetFocusedElement(focusScope, target);
            }
        }

        private static FrameworkElement FindFirstFocusableElement(DependencyObject parent)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                var element = child as FrameworkElement;

                if (element != null &&
                    element.Focusable &&
                    element.IsEnabled &&
                    element.IsVisible)
                {
                    var control = element as Control;

                    if (control == null || control.IsTabStop)
                    {
                        return element;
                    }
                }

                var result = FindFirstFocusableElement(child);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null)
            {
                return false;
            }

            var current = child;

            while (current != null)
            {
                if (ReferenceEquals(current, parent))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void AttachRefocusAfterClick(Window window)
        {
            if (window == null)
            {
                return;
            }

            window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
            {
                var clickedElement = e.OriginalSource as DependencyObject;
                var clickedFocusable = FindNearestFocusableElement(clickedElement);
                var clickedTag = clickedFocusable != null ? clickedFocusable.Tag : null;

                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefocusWindowIfNeeded(window, clickedFocusable, clickedTag);
                }), DispatcherPriority.ApplicationIdle);

            }), true);
        }

        private static void RefocusWindowIfNeeded(Window window, FrameworkElement preferredTarget, object preferredTag)
        {
            if (window == null || !window.IsVisible)
            {
                return;
            }

            window.UpdateLayout();

            var focusedElement = Keyboard.FocusedElement as FrameworkElement;

            if (focusedElement != null &&
                IsValidFocusableTarget(focusedElement) &&
                IsDescendantOf(focusedElement, window))
            {
                return;
            }

            FrameworkElement target = null;

            // 1. Try to find the regenerated button with the same Tag / device Id.
            if (preferredTag != null && !string.IsNullOrWhiteSpace(preferredTag.ToString()))
            {
                target = FindFocusableElementByTag(window, preferredTag);
            }

            // 2. If the original clicked button still exists, reuse it.
            if (target == null &&
                preferredTarget != null &&
                IsValidFocusableTarget(preferredTarget) &&
                IsDescendantOf(preferredTarget, window))
            {
                target = preferredTarget;
            }

            // 3. Fallback only if nothing else works.
            if (target == null)
            {
                target = FindFirstFocusableElement(window);
            }

            if (target == null)
            {
                return;
            }

            target.Focus();
            Keyboard.Focus(target);

            var focusScope = FocusManager.GetFocusScope(target);

            if (focusScope != null)
            {
                FocusManager.SetFocusedElement(focusScope, target);
            }
        }

        private static FrameworkElement FindFocusableElementByTag(DependencyObject parent, object tag)
        {
            if (parent == null || tag == null)
            {
                return null;
            }

            var tagText = tag.ToString();

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var element = child as FrameworkElement;

                if (element != null &&
                    IsValidFocusableTarget(element) &&
                    element.Tag != null &&
                    string.Equals(element.Tag.ToString(), tagText, StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }

                var result = FindFocusableElementByTag(child, tag);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static FrameworkElement FindNearestFocusableElement(DependencyObject element)
        {
            var current = element;

            while (current != null)
            {
                var frameworkElement = current as FrameworkElement;

                if (frameworkElement != null && IsValidFocusableTarget(frameworkElement))
                {
                    return frameworkElement;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static bool IsValidFocusableTarget(FrameworkElement element)
        {
            if (element == null ||
                !element.Focusable ||
                !element.IsEnabled ||
                !element.IsVisible)
            {
                return false;
            }

            var control = element as Control;

            if (control != null && !control.IsTabStop)
            {
                return false;
            }

            return true;
        }

        private void CloseWindowByStyleKey(string styleKey)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
                return;

            var windowsToClose = windows
                .Where(w =>
                    w != null &&
                    w.IsVisible &&
                    string.Equals(w.Tag as string, styleKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var window in windowsToClose)
                window.Close();
        }

        private void RemoveWindow(Window window)
        {
            if (window == null || !windows.Contains(window))
                return;

            var rebuilt = windows.Reverse().Where(w => !ReferenceEquals(w, window)).ToList();

            windows.Clear();

            foreach (var item in rebuilt)
                windows.Push(item);
        }

        private void CleanupClosedWindows()
        {
            var opened = windows.Reverse()
                .Where(w => w != null && w.IsVisible)
                .ToList();

            windows.Clear();

            foreach (var item in opened)
                windows.Push(item);
        }

        private void FocusTopWindow()
        {
            CleanupClosedWindows();

            if (windows.Any())
            {
                var top = windows.Peek();
                top.Activate();
                top.Focus();
            }
            else
            {
                playniteApi.Dialogs.GetCurrentAppWindow()?.Activate();
            }
        }
    }
}