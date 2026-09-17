using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
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
        private readonly Func<int> getHubMaxPage;
        private readonly Func<int, string> getHubPageScopeName;
        private bool started;

        // Cache named visual elements per window/root. A full visual-tree scan is done
        // only on the first lookup (or when a cached element becomes invalid), instead
        // of repeating one recursive scan for every directional key press.
        private readonly Dictionary<DependencyObject, NamedVisualCache> namedVisualCaches =
            new Dictionary<DependencyObject, NamedVisualCache>();

        private sealed class NamedVisualCache
        {
            public readonly Dictionary<string, WeakReference> Elements =
                new Dictionary<string, WeakReference>(StringComparer.Ordinal);

            public DateTime LastScanUtc;
        }

        public NavigationFixService(
            IPlayniteAPI api,
            Func<bool> isWelcomeHubOpen,
            Func<int> getHubCurrentPage = null,
            Action<int> setHubCurrentPage = null,
            Func<int> getHubMaxPage = null,
            Func<int, string> getHubPageScopeName = null)
        {
            this.api = api;
            this.isWelcomeHubOpen = isWelcomeHubOpen;
            this.getHubCurrentPage = getHubCurrentPage;
            this.setHubCurrentPage = setHubCurrentPage;
            this.getHubMaxPage = getHubMaxPage;
            this.getHubPageScopeName = getHubPageScopeName;
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

            namedVisualCaches.Clear();
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

                // The Steam Store can be opened above the Hub while HubRoot
                // is still visible behind it. Handle the Store first, otherwise the Hub
                // navigation branch eats the key and Store focus fixes never run.
                if (HandleSteamStoreNavigation(e, root, focused))
                {
                    return;
                }

                var hubRoot = FindCachedVisualChildByName<FrameworkElement>(root, "HubRoot");
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

        private bool HandleSteamStoreNavigation(KeyEventArgs e, DependencyObject root, object focused)
        {
            var overlay = FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreOverlay");

            if (overlay?.IsVisible != true)
            {
                return false;
            }

            var focusedElement = focused as DependencyObject;
            if (focusedElement != null && !IsDescendantOf(focusedElement, overlay))
            {
                return false;
            }

            var heroButton = FindCachedVisualChildByName<FrameworkElement>(overlay, "SteamStoreHeroButton");
            var itemsList = FindCachedVisualChildByName<ListBox>(overlay, "StoreItemsList");
            var storeListHasFocus = IsFocusInsideStoreItems(itemsList, focusedElement);

            // Force Store focus order: tabs -> hero -> list.
            if (storeListHasFocus)
            {
                if (e.Key == Key.Up)
                {
                    if (FocusElement(heroButton))
                    {
                        e.Handled = true;
                        return true;
                    }
                }

                if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    return true;
                }
            }

            if (e.Key == Key.Down && heroButton?.IsKeyboardFocusWithin == true)
            {
                if (FocusFirstFocusable(itemsList))
                {
                    e.Handled = true;
                    return true;
                }
            }

            if (e.Key == Key.Down && IsSteamStoreTabFocusWithin(overlay))
            {
                if (FocusElement(heroButton))
                {
                    e.Handled = true;
                    return true;
                }
            }

            return false;
        }


        private static bool IsFocusInsideStoreItems(ListBox itemsList, DependencyObject focusedElement)
        {
            if (itemsList == null)
            {
                return false;
            }

            if (itemsList.IsKeyboardFocusWithin)
            {
                return true;
            }

            if (focusedElement != null)
            {
                if (IsDescendantOf(focusedElement, itemsList))
                {
                    return true;
                }

                // Fallback for virtualized DataTemplate focus cases where WPF does not
                // always report ListBox.IsKeyboardFocusWithin reliably.
                var focusedFrameworkElement = focusedElement as FrameworkElement;
                if (focusedFrameworkElement != null)
                {
                    var ancestorList = FindVisualAncestor<ListBox>(focusedFrameworkElement);
                    if (ReferenceEquals(ancestorList, itemsList) || string.Equals(ancestorList?.Name, "StoreItemsList", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    try
                    {
                        if (focusedFrameworkElement.IsVisible && itemsList.IsVisible && itemsList.ActualWidth > 0 && itemsList.ActualHeight > 0)
                        {
                            var focusedPoint = focusedFrameworkElement.TransformToAncestor(itemsList).Transform(new Point(0, 0));
                            var focusedCenterY = focusedPoint.Y + (focusedFrameworkElement.ActualHeight / 2.0);
                            var focusedCenterX = focusedPoint.X + (focusedFrameworkElement.ActualWidth / 2.0);

                            if (focusedCenterY >= -40 &&
                                focusedCenterY <= itemsList.ActualHeight + 80 &&
                                focusedCenterX >= -80 &&
                                focusedCenterX <= itemsList.ActualWidth + 260)
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private bool IsSteamStoreTabFocusWithin(DependencyObject root)
        {
            return
                FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreTabDealsButton")?.IsKeyboardFocusWithin == true ||
                FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreTabRecommendedButton")?.IsKeyboardFocusWithin == true ||
                FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreTabMyWishlistButton")?.IsKeyboardFocusWithin == true ||
                FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreTabNewButton")?.IsKeyboardFocusWithin == true ||
                FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreTabPopularButton")?.IsKeyboardFocusWithin == true ||
                FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreTabWishlistedButton")?.IsKeyboardFocusWithin == true ||
                FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreTabUpcomingButton")?.IsKeyboardFocusWithin == true;
        }

        private void HandleMainNavigation(KeyEventArgs e, DependencyObject root, object focused)
        {
            var list = FindCachedVisualChildByName<ListBox>(root, "PART_ListGameItems");
            var changeViewButton = FindCachedVisualChildByName<ToggleButton>(root, "ChangeViewButton");
            var filters = FindCachedVisualChildByName<FrameworkElement>(root, "ItemsFilterPresets");
            var topBar = FindCachedVisualChildByName<FrameworkElement>(root, "TopMenu");
            var mainButtons = FindCachedVisualChildByName<FrameworkElement>(root, "MainButton");
            var quickAccess = FindCachedVisualChildByName<FrameworkElement>(root, "QuickAccessButton");
            var rightTopButtons = FindCachedVisualChildByName<FrameworkElement>(root, "RightTopButtons");
            var bottomBar = FindCachedVisualChildByName<FrameworkElement>(root, "BottomBar");

            if (topBar?.IsKeyboardFocusWithin == true)
            {
                if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    return;
                }

                // Direct-to-Library startup can leave focus on the top bar. WPF's default
                // spatial navigation then focuses the ListBox itself on the first Down press
                // and only reaches a game item on the second press. Move directly to the
                // selected/first game so the first controller input behaves like Hub -> Library.
                if (e.Key == Key.Down && list?.IsVisible == true)
                {
                    if (FocusSelectedOrFirstListItem(list))
                    {
                        e.Handled = true;
                    }

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

            // Safety net for startup/focus restoration cases where WPF has focused the
            // ListBox container but not one of its game items yet. Consume the first
            // directional input by placing focus on the current game instead of requiring
            // a second controller press.
            if (ReferenceEquals(focused, list) &&
                (e.Key == Key.Down || e.Key == Key.Right || e.Key == Key.Left))
            {
                if (FocusSelectedOrFirstListItem(list))
                {
                    e.Handled = true;
                }

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

        public bool HandleHubTriggerPageNavigation(string buttonName, string stateName)
        {
            try
            {
                var isLeftTrigger =
                    string.Equals(buttonName, "TriggerLeft", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "LeftTrigger", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "LT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "L2", StringComparison.OrdinalIgnoreCase);

                var isRightTrigger =
                    string.Equals(buttonName, "TriggerRight", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "RightTrigger", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "RT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonName, "R2", StringComparison.OrdinalIgnoreCase);

                if (!isLeftTrigger && !isRightTrigger)
                {
                    return false;
                }

                var isPressed = string.Equals(stateName, "Pressed", StringComparison.OrdinalIgnoreCase);
                var isReleased = string.Equals(stateName, "Released", StringComparison.OrdinalIgnoreCase);
                if (!isPressed && !isReleased)
                {
                    return false;
                }

                if (isWelcomeHubOpen != null && !isWelcomeHubOpen())
                {
                    return false;
                }

                if (setHubCurrentPage == null || getHubCurrentPage == null)
                {
                    return false;
                }

                var focused = Keyboard.FocusedElement as DependencyObject;
                var window = focused != null ? Window.GetWindow(focused) : null;
                var root = window as DependencyObject ?? Application.Current?.MainWindow as DependencyObject;
                if (root == null)
                {
                    return false;
                }

                var hubRoot = FindCachedVisualChildByName<FrameworkElement>(root, "HubRoot");
                if (hubRoot?.IsVisible != true)
                {
                    return false;
                }

                // The Store is displayed over the Hub. Do not steal LT/RT while that overlay
                // owns the screen; only the actual Welcome Hub gets first/last-page jumps.
                var steamStoreOverlay = FindCachedVisualChildByName<FrameworkElement>(root, "SteamStoreOverlay");
                if (steamStoreOverlay?.IsVisible == true)
                {
                    return false;
                }

                if (focused != null && !IsDescendantOf(focused, hubRoot))
                {
                    return false;
                }

                // Consume the release as well so Playnite cannot perform its native trigger action
                // after Aniki handled the press.
                if (isReleased)
                {
                    return true;
                }

                var currentPage = ClampHubPage(getHubCurrentPage());
                var targetPage = isLeftTrigger ? 1 : GetHubMaxPage();
                targetPage = ClampHubPage(targetPage);

                if (targetPage == currentPage)
                {
                    return true;
                }

                setHubCurrentPage(targetPage);
                ScheduleFocusCurrentHubPage(root, targetPage > currentPage ? 1 : -1);
                return true;
            }
            catch
            {
                return false;
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

            var hubRoot = FindCachedVisualChildByName<FrameworkElement>(root, "HubRoot");

            if (hubRoot?.IsVisible != true)
            {
                return false;
            }

            var focusedElement = Keyboard.FocusedElement as DependencyObject;

            if (focusedElement != null && !IsDescendantOf(focusedElement, hubRoot))
            {
                return false;
            }

            var currentPage = ClampHubPage(getHubCurrentPage());
            var nextPage = ClampHubPage(currentPage + direction);

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
            var hubTopBar = FindCachedVisualChildByName<FrameworkElement>(root, "HubTopBarBackground");
            var hubFirstCard = FindCachedVisualChildByName<FrameworkElement>(root, "ProfileCard");

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

            var hubRoot = FindCachedVisualChildByName<FrameworkElement>(root, "HubRoot");
            if (hubRoot?.IsVisible != true)
            {
                return false;
            }

            var hubTopBar = FindCachedVisualChildByName<FrameworkElement>(root, "HubTopBarBackground");
            if (hubTopBar?.IsKeyboardFocusWithin == true)
            {
                return false;
            }

            var pageScope = GetCurrentHubPageScope(root);
            if (pageScope?.IsVisible != true)
            {
                return false;
            }

            var focusedElement = focused as UIElement;

            // A lazy Hub page can temporarily contain no cards (notably the Store pages
            // before their first load). WPF then keeps focus on a collapsed card from the
            // previous page or drops it completely. In that state, allow Left/Right to keep
            // changing pages instead of trapping controller navigation on the empty page.
            if (focusedElement == null || !IsDescendantOf(focusedElement, pageScope))
            {
                var currentPageWithoutFocus = ClampHubPage(getHubCurrentPage());
                var nextPageWithoutFocus = ClampHubPage(currentPageWithoutFocus + direction);

                if (nextPageWithoutFocus == currentPageWithoutFocus)
                {
                    return true;
                }

                setHubCurrentPage(nextPageWithoutFocus);
                ScheduleFocusCurrentHubPage(root, direction);
                return true;
            }

            if (FocusFocusableOnSameRowInDirection(pageScope, focusedElement, direction))
            {
                return true;
            }

            var currentPage = ClampHubPage(getHubCurrentPage());
            var nextPage = ClampHubPage(currentPage + direction);

            if (nextPage == currentPage)
            {
                return true;
            }

            setHubCurrentPage(nextPage);
            ScheduleFocusCurrentHubPage(root, direction);

            return true;
        }

        private int GetHubMaxPage()
        {
            try
            {
                return Math.Max(1, getHubMaxPage != null ? getHubMaxPage() : 10);
            }
            catch
            {
                return 10;
            }
        }

        private int ClampHubPage(int page)
        {
            return Math.Max(1, Math.Min(GetHubMaxPage(), page));
        }

        private FrameworkElement GetCurrentHubPageScope(DependencyObject root)
        {
            var page = getHubCurrentPage != null ? ClampHubPage(getHubCurrentPage()) : 1;
            string name = null;

            try
            {
                name = getHubPageScopeName?.Invoke(page);
            }
            catch
            {
                name = null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = page == 1 ? "HubTopSection" : "HubThirdSection";
            }

            return FindCachedVisualChildByName<FrameworkElement>(root, name);
        }

        private void ScheduleFocusCurrentHubPage(DependencyObject root, int direction)
        {
            QueueHubPageFocusRetry(root, direction, 0, DispatcherPriority.Render);
        }

        private void QueueHubPageFocusRetry(DependencyObject root, int direction, int attempt, DispatcherPriority priority)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                try
                {
                    FocusCurrentHubPageOrSkip(root, direction, attempt);
                }
                catch
                {
                }
            }), priority);
        }

        private void FocusCurrentHubPageOrSkip(DependencyObject root, int direction, int attempts)
        {
            if (attempts > 5)
            {
                return;
            }

            var pageScope = GetCurrentHubPageScope(root);

            // Page vraiment absente / collapsed : on la saute.
            if (pageScope == null || pageScope.IsVisible != true)
            {
                var currentPage = ClampHubPage(getHubCurrentPage());
                var nextPage = ClampHubPage(currentPage + direction);

                if (nextPage == currentPage)
                {
                    return;
                }

                setHubCurrentPage(nextPage);

                QueueHubPageFocusRetry(root, direction, attempts + 1, DispatcherPriority.Render);
                return;
            }

            // Si le focus est déjà dans la page, on ne le vole pas.
            if (pageScope.IsKeyboardFocusWithin)
            {
                return;
            }

            // On tente direct.
            if (FocusHubPageEntryFocusable(pageScope, direction))
            {
                return;
            }

            // Page visible mais cartes lazy-load pas encore créées :
            // un seul retry à la fois, pas 3-4 retries empilés.
            QueueHubPageFocusRetry(root, direction, attempts + 1, DispatcherPriority.ApplicationIdle);
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

        private static bool FocusFocusableOnSameRowInDirection(DependencyObject pageScope, UIElement focusedElement, int direction)
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

            UIElement bestElement = null;
            var bestScore = double.MaxValue;

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

                if (bounds.Width <= 0 || bounds.Height <= 0)
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

                var dx = centerX - focusedCenterX;

                if (direction > 0 && dx <= 20)
                {
                    continue;
                }

                if (direction < 0 && dx >= -20)
                {
                    continue;
                }

                var score = Math.Abs(dx) + Math.Abs(centerY - focusedCenterY) * 0.25;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestElement = element;
                }
            }

            if (bestElement == null)
            {
                return false;
            }

            var frameworkElement = bestElement as FrameworkElement;
            if (frameworkElement != null)
            {
                return FocusElement(frameworkElement);
            }

            return bestElement.Focus();
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

        private static T FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;

            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
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

        private static bool FocusSelectedOrFirstListItem(ListBox list)
        {
            if (list == null || !list.IsVisible || !list.IsEnabled || list.Items.Count == 0)
            {
                return false;
            }

            try
            {
                object targetItem = list.SelectedItem;

                if (targetItem == null && list.Items.Count > 0)
                {
                    targetItem = list.Items[0];
                }

                if (targetItem != null)
                {
                    // The selected container can still be unrealized during the very first
                    // Fullscreen layout pass. Scroll/update once so virtualization can create it.
                    list.ScrollIntoView(targetItem);
                    list.UpdateLayout();

                    var container = list.ItemContainerGenerator.ContainerFromItem(targetItem) as ListBoxItem;
                    if (container != null &&
                        container.IsVisible &&
                        container.IsEnabled &&
                        container.Focusable)
                    {
                        container.BringIntoView();
                        container.Focus();
                        Keyboard.Focus(container);

                        var itemFocusScope = FocusManager.GetFocusScope(container);
                        FocusManager.SetFocusedElement(itemFocusScope, container);

                        return container.IsKeyboardFocusWithin || ReferenceEquals(Keyboard.FocusedElement, container);
                    }
                }

                // Fallback keeps the same behavior used by the existing Hub -> Library path.
                list.Focus();
                Keyboard.Focus(list);

                var listFocusScope = FocusManager.GetFocusScope(list);
                FocusManager.SetFocusedElement(listFocusScope, list);

                return list.IsKeyboardFocusWithin;
            }
            catch
            {
                return false;
            }
        }

        private static bool FocusElement(FrameworkElement element)
        {
            if (element == null || !element.IsVisible || !element.IsEnabled)
            {
                return false;
            }

            if (element.Focus())
            {
                Keyboard.Focus(element);

                var focusScope = FocusManager.GetFocusScope(element);
                FocusManager.SetFocusedElement(focusScope, element);

                return true;
            }

            return false;
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

        private T FindCachedVisualChildByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            NamedVisualCache cache;
            if (!namedVisualCaches.TryGetValue(root, out cache))
            {
                cache = new NamedVisualCache();
                namedVisualCaches[root] = cache;
                RebuildNamedVisualCache(root, cache);
            }

            var cached = GetValidCachedElement<T>(root, cache, name);
            if (cached != null)
            {
                return cached;
            }

            // Missing or stale controls may be created again by DataTemplates/page changes.
            // Throttle rebuilds so a missing optional element cannot cause a full scan for
            // every key-repeat event.
            if ((DateTime.UtcNow - cache.LastScanUtc).TotalMilliseconds >= 250)
            {
                RebuildNamedVisualCache(root, cache);
                cached = GetValidCachedElement<T>(root, cache, name);
            }

            return cached;
        }

        private static T GetValidCachedElement<T>(DependencyObject root, NamedVisualCache cache, string name)
            where T : FrameworkElement
        {
            WeakReference reference;
            if (!cache.Elements.TryGetValue(name, out reference))
            {
                return null;
            }

            var element = reference.Target as T;
            if (element == null || !element.IsLoaded || !IsDescendantOf(element, root))
            {
                cache.Elements.Remove(name);
                return null;
            }

            return element;
        }

        private static void RebuildNamedVisualCache(DependencyObject root, NamedVisualCache cache)
        {
            cache.Elements.Clear();
            CollectNamedVisualElements(root, cache.Elements);
            cache.LastScanUtc = DateTime.UtcNow;
        }

        private static void CollectNamedVisualElements(
            DependencyObject root,
            Dictionary<string, WeakReference> elements)
        {
            if (root == null)
            {
                return;
            }

            var frameworkElement = root as FrameworkElement;
            if (frameworkElement != null && !string.IsNullOrEmpty(frameworkElement.Name))
            {
                if (!elements.ContainsKey(frameworkElement.Name))
                {
                    elements[frameworkElement.Name] = new WeakReference(frameworkElement);
                }
            }

            int count;
            try
            {
                count = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                CollectNamedVisualElements(VisualTreeHelper.GetChild(root, i), elements);
            }
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
