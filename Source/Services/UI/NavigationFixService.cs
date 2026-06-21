using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnikiHelper.Services.UI
{
    public class NavigationFixService
    {
        private readonly IPlayniteAPI api;
        private readonly Func<bool> isWelcomeHubOpen;
        private readonly Func<int> getHubCurrentPage;
        private readonly Action<int> setHubCurrentPage;
        private bool started;

        public NavigationFixService(
            IPlayniteAPI api,
            Func<bool> isWelcomeHubOpen,
            Func<int> getHubCurrentPage = null,
            Action<int> setHubCurrentPage = null)
        {
            this.api = api;
            this.isWelcomeHubOpen = isWelcomeHubOpen;
            this.getHubCurrentPage = getHubCurrentPage;
            this.setHubCurrentPage = setHubCurrentPage;
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

        public bool HandleHubHorizontalControllerNavigation(string buttonName, string stateName)
        {
            try
            {
                if (!string.Equals(stateName, "Pressed", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var isRightShoulder =
                    string.Equals(buttonName, "RightShoulder", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "RB", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "R1", StringComparison.OrdinalIgnoreCase);

                var isLeftShoulder =
                    string.Equals(buttonName, "LeftShoulder", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "LB", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "L1", StringComparison.OrdinalIgnoreCase);

                if (!isRightShoulder && !isLeftShoulder)
                {
                    return false;
                }

                var focused = Keyboard.FocusedElement;
                var focusedDependencyObject = focused as DependencyObject;
                var window = focusedDependencyObject != null ? Window.GetWindow(focusedDependencyObject) : null;
                var root = window as DependencyObject ?? Application.Current?.MainWindow as DependencyObject;

                if (root == null)
                {
                    return false;
                }

                if (isRightShoulder)
                {
                    return HandleHubDirectPageNavigation(root, 1);
                }

                if (isLeftShoulder)
                {
                    return HandleHubDirectPageNavigation(root, -1);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool HandleHubDirectPageNavigation(DependencyObject root, int direction)
        {
            if (isWelcomeHubOpen != null && !isWelcomeHubOpen())
            {
                return false;
            }

            if (setHubCurrentPage == null || getHubCurrentPage == null)
            {
                return false;
            }

            var hubRoot = FindVisualChildByName<FrameworkElement>(root, "HubRoot");

            if (hubRoot?.IsVisible != true)
            {
                return false;
            }

            var focusedElement = Keyboard.FocusedElement as DependencyObject;

            if (focusedElement != null && !IsDescendantOf(focusedElement, hubRoot))
            {
                return false;
            }

            var currentPage = Math.Max(1, Math.Min(6, getHubCurrentPage()));
            var nextPage = Math.Max(1, Math.Min(6, currentPage + direction));

            if (nextPage == currentPage)
            {
                return true;
            }

            setHubCurrentPage(nextPage);
            ScheduleFocusCurrentHubPage(root, direction);

            return true;
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

            if (hubTopBar?.IsKeyboardFocusWithin == true)
            {
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
                    return;
                }
            }

            if (e.Key == Key.Right)
            {
                if (HandleHubHorizontalPageEdgeNavigation(root, focused, 1))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Left)
            {
                if (HandleHubHorizontalPageEdgeNavigation(root, focused, -1))
                {
                    e.Handled = true;
                }
            }
        }

        private bool HandleHubHorizontalPageEdgeNavigation(DependencyObject root, object focused, int direction)
        {
            if (isWelcomeHubOpen != null && !isWelcomeHubOpen())
            {
                return false;
            }

            if (setHubCurrentPage == null || getHubCurrentPage == null)
            {
                return false;
            }

            var hubRoot = FindVisualChildByName<FrameworkElement>(root, "HubRoot");
            if (hubRoot?.IsVisible != true)
            {
                return false;
            }

            var hubTopBar = FindVisualChildByName<FrameworkElement>(root, "HubTopBarBackground");
            if (hubTopBar?.IsKeyboardFocusWithin == true)
            {
                return false;
            }

            var focusedElement = focused as UIElement;
            if (focusedElement == null)
            {
                return false;
            }

            var pageScope = GetCurrentHubPageScope(root);
            if (pageScope?.IsVisible != true)
            {
                return false;
            }

            if (!IsDescendantOf(focusedElement, pageScope))
            {
                return false;
            }

            if (HasFocusableOnSameRowInDirection(pageScope, focusedElement, direction))
            {
                return false;
            }

            var currentPage = Math.Max(1, Math.Min(6, getHubCurrentPage()));
            var nextPage = Math.Max(1, Math.Min(6, currentPage + direction));

            if (nextPage == currentPage)
            {
                return true;
            }

            setHubCurrentPage(nextPage);
            ScheduleFocusCurrentHubPage(root, direction);

            return true;
        }

        private FrameworkElement GetCurrentHubPageScope(DependencyObject root)
        {
            var page = getHubCurrentPage != null ? Math.Max(1, Math.Min(6, getHubCurrentPage())) : 1;
            var name = "HubTopSection";

            switch (page)
            {
                case 1:
                    name = "HubTopSection";
                    break;
                case 2:
                    name = "HubThirdSection";
                    break;
                case 3:
                    name = "HubLatestCapturesSection";
                    break;
                case 4:
                    name = "HubAchievementMemoriesSection";
                    break;
                case 5:
                    name = "HubUpcomingSection";
                    break;
                case 6:
                    name = "HubStoreSection";
                    break;
            }

            return FindVisualChildByName<FrameworkElement>(root, name);
        }

        private void ScheduleFocusCurrentHubPage(DependencyObject root, int direction)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                try
                {
                    FocusCurrentHubPageOrSkip(root, direction, 0);
                }
                catch
                {
                }
            }), DispatcherPriority.ContextIdle);
        }

        private void FocusCurrentHubPageOrSkip(DependencyObject root, int direction, int attempts)
        {
            if (attempts > 6)
            {
                return;
            }

            var pageScope = GetCurrentHubPageScope(root);

            var focused = FocusHubPageEntryFocusable(pageScope, direction);

            if (focused)
            {
                return;
            }

            var currentPage = Math.Max(1, Math.Min(6, getHubCurrentPage()));
            var nextPage = Math.Max(1, Math.Min(6, currentPage + direction));

            if (nextPage == currentPage)
            {
                return;
            }

            setHubCurrentPage(nextPage);

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                FocusCurrentHubPageOrSkip(root, direction, attempts + 1);
            }), DispatcherPriority.ContextIdle);
        }

        private static bool FocusHubPageEntryFocusable(DependencyObject pageScope, int direction)
        {
            if (pageScope == null)
            {
                return false;
            }

            var items = GetFocusableChildren(pageScope);
            UIElement bestElement = null;
            Rect bestBounds = Rect.Empty;

            foreach (var element in items)
            {
                Rect bounds;
                if (!TryGetBoundsRelativeTo(pageScope, element, out bounds))
                {
                    continue;
                }

                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    continue;
                }

                if (bestElement == null || IsBetterHubEntryCandidate(bounds, bestBounds, direction))
                {
                    bestElement = element;
                    bestBounds = bounds;
                }
            }

            return bestElement != null && bestElement.Focus();
        }

        private static bool IsBetterHubEntryCandidate(Rect candidate, Rect current, int direction)
        {
            const double tolerance = 12.0;

            if (direction > 0)
            {
                // Page suivante : on arrive depuis la droite,
                // donc focus sur le bord gauche de la nouvelle page.
                if (candidate.Left < current.Left - tolerance)
                {
                    return true;
                }

                if (Math.Abs(candidate.Left - current.Left) <= tolerance)
                {
                    if (candidate.Top < current.Top - tolerance)
                    {
                        return true;
                    }

                    if (Math.Abs(candidate.Top - current.Top) <= tolerance)
                    {
                        return (candidate.Width * candidate.Height) > (current.Width * current.Height);
                    }
                }

                return false;
            }

            // Page précédente : on arrive depuis la gauche,
            // donc focus sur le bord droit de la nouvelle page.
            if (candidate.Right > current.Right + tolerance)
            {
                return true;
            }

            if (Math.Abs(candidate.Right - current.Right) <= tolerance)
            {
                if (candidate.Top < current.Top - tolerance)
                {
                    return true;
                }

                if (Math.Abs(candidate.Top - current.Top) <= tolerance)
                {
                    return (candidate.Width * candidate.Height) > (current.Width * current.Height);
                }
            }

            return false;
        }

        private static bool HasFocusableOnSameRowInDirection(DependencyObject pageScope, UIElement focusedElement, int direction)
        {
            var elements = GetFocusableChildren(pageScope);

            if (elements.Count == 0)
            {
                return false;
            }

            Rect focusedBounds;
            if (!TryGetBoundsRelativeTo(pageScope, focusedElement, out focusedBounds))
            {
                return false;
            }

            var focusedCenterY = focusedBounds.Top + focusedBounds.Height / 2.0;
            var focusedCenterX = focusedBounds.Left + focusedBounds.Width / 2.0;

            foreach (var element in elements)
            {
                if (ReferenceEquals(element, focusedElement))
                {
                    continue;
                }

                Rect bounds;
                if (!TryGetBoundsRelativeTo(pageScope, element, out bounds))
                {
                    continue;
                }

                var centerY = bounds.Top + bounds.Height / 2.0;
                var centerX = bounds.Left + bounds.Width / 2.0;

                var sameRowTolerance = Math.Max(80.0, Math.Min(focusedBounds.Height, bounds.Height) * 0.75);

                if (Math.Abs(centerY - focusedCenterY) > sameRowTolerance)
                {
                    continue;
                }

                if (direction > 0 && centerX > focusedCenterX + 20)
                {
                    return true;
                }

                if (direction < 0 && centerX < focusedCenterX - 20)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetBoundsRelativeTo(DependencyObject ancestor, UIElement element, out Rect bounds)
        {
            bounds = Rect.Empty;

            try
            {
                if (ancestor == null || element == null || element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0)
                {
                    return false;
                }

                var visualAncestor = ancestor as Visual;

                if (visualAncestor == null)
                {
                    return false;
                }

                var transform = element.TransformToAncestor(visualAncestor);
                bounds = transform.TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
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

        private static bool FocusLastFocusable(DependencyObject root)
        {
            var items = GetFocusableChildren(root);

            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Focus())
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