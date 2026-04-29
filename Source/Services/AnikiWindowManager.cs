using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

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
                {
                    return null;
                }

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

        public AnikiWindowManager(IPlayniteAPI playniteApi)
        {
            this.playniteApi = playniteApi;
        }

        public bool HasOpenWindow => windows.Any();

        public void OpenWindow(string styleKey)
        {
            Open(styleKey, false);
        }

        public void OpenChildWindow(string styleKey)
        {
            Open(styleKey, true);
        }

        public bool CloseTopWindow()
        {
            CleanupClosedWindows();

            if (!windows.Any())
            {
                return false;
            }

            var top = windows.Pop();

            if (top != null && top.IsVisible)
            {
                top.Close();
            }

            FocusTopWindow();
            return true;
        }

        public bool IsTopWindowActive()
        {
            CleanupClosedWindows();

            if (!windows.Any())
            {
                return false;
            }

            var top = windows.Peek();
            return top != null && top.IsActive;
        }

        private void Open(string styleKey, bool forceChild)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                CleanupClosedWindows();

                var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false
                });

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
                    window.Background = System.Windows.Media.Brushes.Transparent;
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
                    FocusTopWindow();
                };

                windows.Push(window);
                window.Show();
                window.Activate();
                window.Focus();
            });
        }

        private void RemoveWindow(Window window)
        {
            if (window == null || !windows.Contains(window))
            {
                return;
            }

            var rebuilt = windows.Reverse().Where(w => !ReferenceEquals(w, window)).ToList();

            windows.Clear();

            foreach (var item in rebuilt)
            {
                windows.Push(item);
            }
        }

        private void CleanupClosedWindows()
        {
            var opened = windows.Reverse()
                .Where(w => w != null && w.IsVisible)
                .ToList();

            windows.Clear();

            foreach (var item in opened)
            {
                windows.Push(item);
            }
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