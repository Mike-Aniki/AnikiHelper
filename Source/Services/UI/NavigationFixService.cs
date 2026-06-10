using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AnikiHelper.Services.UI
{
    public class NavigationFixService
    {
        private readonly IPlayniteAPI api;
        private bool started;

        public NavigationFixService(IPlayniteAPI api, Func<bool> isWelcomeHubOpen)
        {
            this.api = api;
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            AttachToOpenWindows();

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                AttachToOpenWindows();
            });

            started = true;
        }

        public void Stop()
        {
            if (Application.Current?.Windows != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    window.PreviewKeyDown -= OnPreviewKeyDown;
                }
            }

            started = false;
        }

        private void AttachToOpenWindows()
        {
            if (Application.Current?.Windows == null)
            {
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (window == null)
                {
                    continue;
                }

                window.PreviewKeyDown -= OnPreviewKeyDown;
                window.PreviewKeyDown += OnPreviewKeyDown;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (api?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Left && e.Key != Key.Right)
                {
                    return;
                }

                var root = sender as DependencyObject ?? Application.Current?.MainWindow;
                if (root == null)
                {
                    return;
                }

                var focused = Keyboard.FocusedElement;

                var hubRoot = FindVisualChildByName<FrameworkElement>(root, "HubRoot");
                if (hubRoot?.IsVisible == true)
                {
                    HandleHubNavigation(e, root, focused);
                    return;
                }

                HandleMainNavigation(e, root, focused);
            }
            catch
            {
                // Never break Playnite navigation for a comfort fix.
            }
        }

        private void HandleMainNavigation(KeyEventArgs e, DependencyObject root, object focused)
        {
            var list = FindVisualChildByName<ListBox>(root, "PART_ListGameItems");
            var changeViewButton = FindVisualChildByName<ToggleButton>(root, "ChangeViewButton");
            var filters = FindVisualChildByName<FrameworkElement>(root, "ItemsFilterPresets");
            var topBar = FindVisualChildByName<FrameworkElement>(root, "TopMenu");
            var mainButtons = FindVisualChildByName<FrameworkElement>(root, "MainButton");
            var quickAccess = FindVisualChildByName<FrameworkElement>(root, "QuickAccessButton");
            var rightTopButtons = FindVisualChildByName<FrameworkElement>(root, "RightTopButtons");
            var bottomBar = FindVisualChildByName<FrameworkElement>(root, "BottomBar");

            if (topBar?.IsKeyboardFocusWithin == true)
            {
                if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Left)
                {
                    FocusPreviousFocusableInContainer(topBar, focused);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Right)
                {
                    FocusNextFocusableInContainer(topBar, focused);
                    e.Handled = true;
                    return;
                }
            }

            if (filters?.IsKeyboardFocusWithin == true)
            {
                if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Left)
                {
                    FocusPreviousFocusableInContainer(filters, focused);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Right)
                {
                    FocusNextFocusableInContainer(filters, focused);
                    e.Handled = true;
                    return;
                }
            }

            var isHorizontalView = changeViewButton?.IsChecked == true;

            if (!isHorizontalView)
            {
                return;
            }

            if (list == null)
            {
                return;
            }

            if (e.Key == Key.Right &&
                quickAccess?.IsKeyboardFocusWithin == true &&
                mainButtons?.IsVisible != true)
            {
                if (FocusFirstFocusable(rightTopButtons))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Left &&
                rightTopButtons?.IsKeyboardFocusWithin == true &&
                mainButtons?.IsVisible != true)
            {
                if (FocusFirstFocusable(quickAccess))
                {
                    e.Handled = true;
                }

                return;
            }

            if (!list.IsKeyboardFocusWithin)
            {
                return;
            }

            if (e.Key == Key.Up)
            {
                if (FocusFirstFocusable(quickAccess))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Down)
            {
                if (bottomBar?.IsVisible == true && filters?.IsVisible == true)
                {
                    if (FocusFirstFocusable(filters))
                    {
                        e.Handled = true;
                    }
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        private void HandleHubNavigation(KeyEventArgs e, DependencyObject root, object focused)
        {
            var hubTopBar = FindVisualChildByName<FrameworkElement>(root, "HubTopBarBackground");
            var hubFirstCard = FindVisualChildByName<FrameworkElement>(root, "ProfileCard");

            if (e.Key == Key.Up && hubFirstCard?.IsKeyboardFocusWithin == true)
            {
                if (FocusFirstFocusable(hubTopBar))
                {
                    e.Handled = true;
                }

                return;
            }

            if (hubTopBar?.IsKeyboardFocusWithin != true)
            {
                return;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                if (FocusFirstFocusable(hubFirstCard))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Right)
            {
                FocusNextFocusableInContainer(hubTopBar, focused);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left)
            {
                FocusPreviousFocusableInContainer(hubTopBar, focused);
                e.Handled = true;
            }
        }

        private static bool FocusNextFocusableInContainer(DependencyObject container, object focusedElement)
        {
            var items = GetFocusableChildren(container);

            if (items.Count == 0)
            {
                return false;
            }

            var index = items.FindIndex(x => ReferenceEquals(x, focusedElement));

            if (index >= 0 && index < items.Count - 1)
            {
                return items[index + 1].Focus();
            }

            return false;
        }

        private static bool FocusPreviousFocusableInContainer(DependencyObject container, object focusedElement)
        {
            var items = GetFocusableChildren(container);

            if (items.Count == 0)
            {
                return false;
            }

            var index = items.FindIndex(x => ReferenceEquals(x, focusedElement));

            if (index > 0)
            {
                return items[index - 1].Focus();
            }

            return false;
        }

        private static System.Collections.Generic.List<UIElement> GetFocusableChildren(DependencyObject root)
        {
            var result = new System.Collections.Generic.List<UIElement>();

            if (root == null)
            {
                return result;
            }

            if (root is UIElement element &&
                element.Focusable &&
                element.IsVisible &&
                element.IsEnabled)
            {
                result.Add(element);
            }

            var count = VisualTreeHelper.GetChildrenCount(root);

            for (var i = 0; i < count; i++)
            {
                result.AddRange(GetFocusableChildren(VisualTreeHelper.GetChild(root, i)));
            }

            return result;
        }

        private static bool FocusFirstFocusable(DependencyObject root)
        {
            if (root == null)
            {
                return false;
            }

            if (root is UIElement element &&
                element.Focusable &&
                element.IsVisible &&
                element.IsEnabled)
            {
                return element.Focus();
            }

            var count = VisualTreeHelper.GetChildrenCount(root);

            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (FocusFirstFocusable(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);

            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Name == name)
                {
                    return element;
                }

                var result = FindVisualChildByName<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}