using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName);
            Open(styleKey, false, focusTargetName);
        }

        public void OpenWindow(string styleKey, string focusTargetName)
        {
            Open(styleKey, false, focusTargetName);
        }

        public void OpenChildWindow(string styleKey)
        {
            Open(styleKey, true, null);
        }

        private void ParseOpenParameter(string parameter, out string styleKey, out string focusTargetName)
        {
            styleKey = parameter;
            focusTargetName = null;

            if (string.IsNullOrWhiteSpace(parameter) || !parameter.Contains("|"))
                return;

            var parts = parameter.Split('|');

            styleKey = parts.Length > 0 ? parts[0] : parameter;
            focusTargetName = parts.Length > 1 ? parts[1] : null;
        }

        public bool CloseTopWindow()
        {
            CleanupClosedWindows();

            if (!windows.Any())
                return false;

            var top = windows.Pop();

            if (top != null && top.IsVisible)
                top.Close();

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

        private void Open(string styleKey, bool forceChild, string focusTargetName)
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

                if (!string.IsNullOrWhiteSpace(focusTargetName))
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var target = FindVisualChildByName<FrameworkElement>(window, focusTargetName);

                        if (target != null)
                        {
                            target.Focus();
                            Keyboard.Focus(target);
                        }
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