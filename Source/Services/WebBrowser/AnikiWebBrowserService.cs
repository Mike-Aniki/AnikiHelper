using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnikiHelper.Services.WebBrowser
{
    internal struct WebBrowserGamepadInputState
    {
        public short LeftX;
        public short LeftY;
        public short RightX;
        public short RightY;
        public bool LeftClick;
        public bool ActivatePressed;
        public bool BackPressed;
        public bool ClosePressed;
        public bool KeyboardPressed;
        public bool AddressPressed;
        public bool EnterPressed;
        public bool PreviousPressed;
        public bool NextPressed;
        public bool DPadUpPressed;
        public bool DPadDownPressed;
        public bool DPadLeftPressed;
        public bool DPadRightPressed;
    }

    internal enum AnikiWebBrowserViewMode
    {
        Home,
        Web
    }

    /// <summary>
    /// Controller-friendly WebView2 browser with a native favorites home screen.
    /// The home screen supports both normal controller focus navigation and the
    /// same right-stick mouse cursor used inside web pages.
    /// </summary>
    internal sealed class AnikiWebBrowserService : IDisposable
    {
        private const int MinimumWidth = 900;
        private const int MinimumHeight = 600;
        private const int FooterHeight = 62;
        private const int DiscreteActionCooldownMs = 180;
        private const int HomeStickThreshold = 18000;
        private const int HomePointerThreshold = 6500;

        private const string ControllerCompatibilityScript = @"
(() => {
    try {
        Object.defineProperty(navigator, 'getGamepads', {
            configurable: true,
            value: () => []
        });
    } catch (_) { }

    window.addEventListener('gamepadconnected', event => {
        try { event.stopImmediatePropagation(); } catch (_) { }
    }, true);

    const styleId = 'aniki-controller-cursor-style';
    const ensureCursorStyle = () => {
        try {
            if (!document.documentElement || document.getElementById(styleId)) {
                return;
            }

            const style = document.createElement('style');
            style.id = styleId;
            style.textContent = `
                html, body, * { cursor: default !important; }
                a, a *, button, button *, summary, [role=""button""], [role=""link""],
                input[type=""button""], input[type=""submit""], input[type=""checkbox""],
                input[type=""radio""], select, label[for] { cursor: pointer !important; }
                input:not([type=""button""]):not([type=""submit""]):not([type=""checkbox""]):not([type=""radio""]),
                textarea, [contenteditable=""true""] { cursor: text !important; }
            `;
            document.documentElement.appendChild(style);
        } catch (_) { }
    };

    ensureCursorStyle();
    document.addEventListener('DOMContentLoaded', ensureCursorStyle, { once: true });
})();";

        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly Action openVirtualKeyboard;
        private readonly Func<IEnumerable<AnikiWebFavorite>> favoritesProvider;
        private readonly BrowserPointerController pointerController;
        private readonly string userDataFolder;

        private Window windowHost;
        private Grid browserArea;
        private AnikiWebBrowserHomeView homeView;
        private WebView2 webView;
        private TextBlock loadingText;
        private StackPanel footerLegendPanel;
        private TextBlock footerStatusText;
        private CoreWebView2Environment environment;

        private string pendingAddress = string.Empty;
        private string requestedTitle = string.Empty;
        private volatile bool browserWindowActive;
        private bool initializing;
        private bool closing;
        private bool disposed;
        private bool pointerSessionActive;
        private int sessionGeneration;
        private DateTime lastDiscreteActionUtc = DateTime.MinValue;
        private int lastHomeStickDirection;
        private bool homePointerMode;
        private AnikiWebBrowserViewMode viewMode = AnikiWebBrowserViewMode.Home;

        public event Action<bool> OpenStateChanged;

        public AnikiWebBrowserService(
            IPlayniteAPI api,
            ILogger logger,
            Action openVirtualKeyboard,
            Func<IEnumerable<AnikiWebFavorite>> favoritesProvider,
            string userDataFolder)
        {
            this.api = api;
            this.logger = logger;
            this.openVirtualKeyboard = openVirtualKeyboard;
            this.favoritesProvider = favoritesProvider;
            this.userDataFolder = ResolveUserDataFolder(api, userDataFolder);
            pointerController = new BrowserPointerController(logger, FooterHeight);
        }

        public bool IsOpen
        {
            get
            {
                var host = windowHost;
                return host != null && host.IsVisible;
            }
        }

        public bool IsControllerInputActive
        {
            get { return IsOpen && browserWindowActive; }
        }

        public void OpenHome()
        {
            InvokeOnUi(OpenHomeCore);
        }

        public void Open(string address, string title = null)
        {
            var normalized = NormalizeAddress(address);
            if (normalized == null)
            {
                ShowInvalidAddressMessage();
                return;
            }

            InvokeOnUi(delegate { OpenAddressCore(normalized, title); });
        }

        public void Close()
        {
            InvokeOnUi(CloseCore);
        }

        public Task ClearCacheAsync()
        {
            return RunProfileOperationAsync(
                profile => profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.DiskCache |
                    CoreWebView2BrowsingDataKinds.CacheStorage));
        }

        public Task ClearAllBrowserDataAsync()
        {
            return RunProfileOperationAsync(
                profile => profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.AllProfile));
        }

        public void ProcessControllerInput(WebBrowserGamepadInputState state)
        {
            if (!IsControllerInputActive)
            {
                pointerController.SuspendInput();
                ResetHomeStickState();
                return;
            }

            if (viewMode == AnikiWebBrowserViewMode.Web)
            {
                pointerController.ProcessInput(state);
            }
            else
            {
                if (HasHomePointerMovement(state))
                {
                    homePointerMode = true;
                }

                if (HasHomeDirectionalInput(state))
                {
                    homePointerMode = false;
                }

                pointerController.ProcessHomeInput(state, homePointerMode);
                ProcessHomeDirectionalInput(state);
            }

            if (!TryAcceptDiscreteAction(state))
            {
                return;
            }

            if (state.ClosePressed)
            {
                InvokeOnUi(CloseCore);
                return;
            }

            if (viewMode == AnikiWebBrowserViewMode.Home)
            {
                if (state.ActivatePressed)
                {
                    // In pointer mode, A is already converted to a native mouse click by
                    // BrowserPointerController. In focus mode, keep the classic D-Pad/LS
                    // activation behaviour.
                    if (!homePointerMode)
                    {
                        InvokeOnUi(delegate { homeView?.ActivateFocused(); });
                    }

                    return;
                }

                if (state.KeyboardPressed || state.AddressPressed)
                {
                    InvokeOnUi(delegate { homeView?.FocusSearchAndOpenKeyboard(); });
                    return;
                }

                if (state.EnterPressed)
                {
                    InvokeOnUi(delegate { homeView?.SubmitSearch(); });
                }

                return;
            }

            if (state.PreviousPressed)
            {
                InvokeOnUi(GoBackOrHomeCore);
                return;
            }

            if (state.NextPressed)
            {
                InvokeOnUi(GoForwardCore);
                return;
            }

            if (state.KeyboardPressed)
            {
                InvokeOnUi(OpenVirtualKeyboardCore);
                return;
            }

            if (state.AddressPressed)
            {
                InvokeOnUi(OpenAddressPromptCore);
                return;
            }

            if (state.EnterPressed)
            {
                InvokeOnUi(SendEnterCore);
            }
        }

        private bool TryAcceptDiscreteAction(WebBrowserGamepadInputState state)
        {
            if (!state.ActivatePressed &&
                !state.ClosePressed &&
                !state.KeyboardPressed &&
                !state.AddressPressed &&
                !state.EnterPressed &&
                !state.PreviousPressed &&
                !state.NextPressed)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if ((now - lastDiscreteActionUtc).TotalMilliseconds < DiscreteActionCooldownMs)
            {
                return false;
            }

            lastDiscreteActionUtc = now;
            return true;
        }

        private static bool HasHomePointerMovement(WebBrowserGamepadInputState state)
        {
            return Math.Abs((int)state.RightX) > HomePointerThreshold ||
                   Math.Abs((int)state.RightY) > HomePointerThreshold;
        }

        private static bool HasHomeDirectionalInput(WebBrowserGamepadInputState state)
        {
            if (state.DPadUpPressed ||
                state.DPadDownPressed ||
                state.DPadLeftPressed ||
                state.DPadRightPressed)
            {
                return true;
            }

            return GetHomeStickDirection(state.LeftX, state.LeftY) != 0;
        }

        private void ProcessHomeDirectionalInput(WebBrowserGamepadInputState state)
        {
            if (state.DPadUpPressed)
            {
                InvokeOnUi(delegate { homeView?.MoveFocus(FocusNavigationDirection.Up); });
                return;
            }

            if (state.DPadDownPressed)
            {
                InvokeOnUi(delegate { homeView?.MoveFocus(FocusNavigationDirection.Down); });
                return;
            }

            if (state.DPadLeftPressed)
            {
                InvokeOnUi(delegate { homeView?.MoveFocus(FocusNavigationDirection.Left); });
                return;
            }

            if (state.DPadRightPressed)
            {
                InvokeOnUi(delegate { homeView?.MoveFocus(FocusNavigationDirection.Right); });
                return;
            }

            var direction = GetHomeStickDirection(state.LeftX, state.LeftY);
            if (direction == 0)
            {
                ResetHomeStickState();
                return;
            }

            if (direction == lastHomeStickDirection)
            {
                return;
            }

            lastHomeStickDirection = direction;
            MoveHomeFocusForDirection(direction);
        }

        private void MoveHomeFocusForDirection(int direction)
        {
            FocusNavigationDirection focusDirection;
            switch (direction)
            {
                case 1:
                    focusDirection = FocusNavigationDirection.Up;
                    break;
                case 2:
                    focusDirection = FocusNavigationDirection.Down;
                    break;
                case 3:
                    focusDirection = FocusNavigationDirection.Left;
                    break;
                case 4:
                    focusDirection = FocusNavigationDirection.Right;
                    break;
                default:
                    return;
            }

            InvokeOnUi(delegate { homeView?.MoveFocus(focusDirection); });
        }

        private static int GetHomeStickDirection(short x, short y)
        {
            var absX = Math.Abs((int)x);
            var absY = Math.Abs((int)y);

            if (absX < HomeStickThreshold && absY < HomeStickThreshold)
            {
                return 0;
            }

            if (absY >= absX)
            {
                return y < 0 ? 1 : 2;
            }

            return x < 0 ? 3 : 4;
        }

        private void ResetHomeStickState()
        {
            lastHomeStickDirection = 0;
        }

        private void OpenHomeCore()
        {
            if (disposed)
            {
                return;
            }

            var resetCurrentPage = windowHost != null &&
                                   viewMode == AnikiWebBrowserViewMode.Web;

            EnsureWindowCreatedAndShown();
            ShowHomeCore(resetCurrentPage);
            BringBrowserToForeground();
        }

        private void OpenAddressCore(string normalizedAddress, string title)
        {
            if (disposed)
            {
                return;
            }

            EnsureWindowCreatedAndShown();
            ShowWebCore(normalizedAddress, title);
            BringBrowserToForeground();
        }

        private void EnsureWindowCreatedAndShown()
        {
            if (windowHost == null)
            {
                CreateBrowserWindow();
                OpenStateChanged?.Invoke(true);
            }

            if (!windowHost.IsVisible)
            {
                windowHost.Show();
            }
        }

        private void ShowHomeCore(bool clearCurrentPage)
        {
            if (windowHost == null)
            {
                return;
            }

            viewMode = AnikiWebBrowserViewMode.Home;
            requestedTitle = string.Empty;
            pendingAddress = string.Empty;
            ResetHomeStickState();
            homePointerMode = false;

            if (!pointerSessionActive)
            {
                pointerController.BeginSession(windowHost);
                pointerSessionActive = true;
            }
            else
            {
                pointerController.ResumeInput(windowHost);
            }

            if (clearCurrentPage)
            {
                ResetWebViewForHomeCore();
            }

            if (browserArea != null)
            {
                browserArea.Visibility = Visibility.Collapsed;
            }

            if (homeView != null)
            {
                homeView.RefreshFavorites(GetFavoritesSnapshot());
                homeView.Visibility = Visibility.Visible;
            }

            UpdateFooterForMode();
            UpdateWindowTitle();

            Application.Current?.Dispatcher?.BeginInvoke(
                new Action(delegate { homeView?.FocusInitial(); }),
                DispatcherPriority.Input);
        }

        private void ShowWebCore(string normalizedAddress, string title)
        {
            pendingAddress = normalizedAddress;
            requestedTitle = title ?? string.Empty;
            viewMode = AnikiWebBrowserViewMode.Web;
            ResetHomeStickState();
            homePointerMode = false;

            if (homeView != null)
            {
                homeView.Visibility = Visibility.Collapsed;
            }

            if (browserArea != null)
            {
                browserArea.Visibility = Visibility.Visible;
            }

            if (!pointerSessionActive)
            {
                pointerController.BeginSession(windowHost);
                pointerSessionActive = true;
            }
            else
            {
                pointerController.ResumeInput(windowHost);
            }

            UpdateFooterForMode();
            UpdateWindowTitle();

            if (webView?.CoreWebView2 != null)
            {
                webView.Visibility = Visibility.Visible;
                if (loadingText != null)
                {
                    loadingText.Visibility = Visibility.Collapsed;
                }

                NavigateCore(normalizedAddress);
                return;
            }

            if (!initializing)
            {
                var generation = ++sessionGeneration;
                initializing = true;
                if (loadingText != null)
                {
                    loadingText.Visibility = Visibility.Visible;
                }

                RunInitialization(generation, windowHost, webView);
            }
        }

        private async void RunInitialization(int generation, Window expectedHost, WebView2 expectedView)
        {
            try
            {
                await InitializeWebViewAsync(generation, expectedHost, expectedView);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                if (!IsCurrentSession(generation, expectedHost, expectedView))
                {
                    return;
                }

                logger?.Warn(ex, "[AnikiHelper][WebBrowser] Microsoft Edge WebView2 Runtime is missing.");
                CleanupCore(expectedHost, expectedView);
                ShowErrorMessage(
                    Loc(
                        "WebBrowser_RuntimeMissing",
                        "Microsoft Edge WebView2 Runtime is required to use the browser."),
                    ex.Message);
            }
            catch (Exception ex)
            {
                if (!IsCurrentSession(generation, expectedHost, expectedView))
                {
                    return;
                }

                logger?.Warn(ex, "[AnikiHelper][WebBrowser] WebView2 initialization failed.");
                CleanupCore(expectedHost, expectedView);
                ShowErrorMessage(
                    Loc("WebBrowser_OpenError", "The web browser could not be opened."),
                    ex.Message);
            }
        }

        private async Task InitializeWebViewAsync(
            int generation,
            Window expectedHost,
            WebView2 expectedView)
        {
            Directory.CreateDirectory(userDataFolder);

            var localEnvironment = environment;
            if (localEnvironment == null)
            {
                localEnvironment = await CoreWebView2Environment.CreateAsync(
                    null,
                    userDataFolder,
                    new CoreWebView2EnvironmentOptions());

                if (!IsCurrentSession(generation, expectedHost, expectedView))
                {
                    return;
                }

                environment = localEnvironment;
            }

            await expectedView.EnsureCoreWebView2Async(localEnvironment);

            if (!IsCurrentSession(generation, expectedHost, expectedView))
            {
                return;
            }

            ConfigureCoreWebView(expectedView.CoreWebView2);
            await expectedView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                ControllerCompatibilityScript);

            if (!IsCurrentSession(generation, expectedHost, expectedView))
            {
                return;
            }

            initializing = false;
            if (loadingText != null)
            {
                loadingText.Visibility = Visibility.Collapsed;
            }

            expectedView.Visibility = Visibility.Visible;

            if (viewMode == AnikiWebBrowserViewMode.Web && IsAllowedAddress(pendingAddress))
            {
                NavigateCore(pendingAddress);
                BringBrowserToForeground();
            }

            DebugLog(
                "[AnikiHelper][WebBrowser] WebView2 initialized. Profile=" + userDataFolder);
        }

        private void CreateBrowserWindow()
        {
            var owner = api?.Dialogs?.GetCurrentAppWindow();
            var host = new Window
            {
                Title = BuildWindowTitle(),
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.Manual,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                Background = Brushes.Black,
                Cursor = Cursors.Arrow,
                ForceCursor = true,
                Focusable = true
            };

            ConfigureWindowBounds(host, owner);

            if (owner != null && !ReferenceEquals(owner, host))
            {
                try
                {
                    host.Owner = owner;
                }
                catch
                {
                }
            }

            homeView = new AnikiWebBrowserHomeView(
                OpenSearchOrAddressFromHome,
                favorite =>
                {
                    if (favorite == null)
                    {
                        return;
                    }

                    var normalized = NormalizeAddress(favorite.Url);
                    if (normalized == null)
                    {
                        ShowInvalidAddressMessage();
                        return;
                    }

                    ShowWebCore(normalized, favorite.Name);
                },
                OpenHomeKeyboardCore,
                LoadActiveThemeImage("Images/Aniki.png"));

            browserArea = new Grid
            {
                Background = Brushes.Black,
                Visibility = Visibility.Collapsed
            };

            loadingText = new TextBlock
            {
                Text = Loc("WebBrowser_Initializing", "Initializing web browser…"),
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(30)
            };

            var view = CreateWebViewControl();

            browserArea.Children.Add(loadingText);
            browserArea.Children.Add(view);

            var mainArea = new Grid
            {
                Background = Brushes.Black
            };
            mainArea.Children.Add(homeView);
            mainArea.Children.Add(browserArea);

            var root = new Grid
            {
                Background = Brushes.Black
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FooterHeight) });

            Grid.SetRow(mainArea, 0);
            root.Children.Add(mainArea);

            var footer = CreateFooter();
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            host.Content = root;

            host.Closed += WindowHost_Closed;
            host.Activated += WindowHost_Activated;
            host.Deactivated += WindowHost_Deactivated;
            host.LocationChanged += WindowHost_LocationChanged;
            host.SizeChanged += WindowHost_SizeChanged;
            host.PreviewKeyDown += WindowHost_PreviewKeyDown;

            windowHost = host;
            webView = view;
            closing = false;
            browserWindowActive = false;
            pointerSessionActive = false;
            homePointerMode = false;
            viewMode = AnikiWebBrowserViewMode.Home;
            homeView.RefreshFavorites(GetFavoritesSnapshot());
            UpdateFooterForMode();
        }

        private WebView2 CreateWebViewControl()
        {
            return new WebView2
            {
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = true
            };
        }

        private void ResetWebViewForHomeCore()
        {
            var oldView = webView;
            if (oldView == null || browserArea == null)
            {
                return;
            }

            // Recreate only the WebView control while keeping the same persistent profile.
            // This stops page audio/scripts and clears the navigation history without
            // deleting cookies or making the user sign in again.
            sessionGeneration++;
            initializing = false;

            var oldCore = oldView.CoreWebView2;
            UnconfigureCoreWebView(oldCore);

            try { oldCore?.Stop(); } catch { }
            try { browserArea.Children.Remove(oldView); } catch { }
            try { oldView.Dispose(); } catch { }

            var replacement = CreateWebViewControl();
            browserArea.Children.Add(replacement);
            webView = replacement;

            if (loadingText != null)
            {
                loadingText.Visibility = Visibility.Collapsed;
            }
        }

        private FrameworkElement CreateFooter()
        {
            var footerGrid = new Grid
            {
                Margin = new Thickness(22, 0, 22, 0)
            };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = Loc("WebBrowser_WindowTitle", "Aniki Web Browser"),
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 24, 0)
            };
            Grid.SetColumn(title, 0);
            footerGrid.Children.Add(title);

            footerLegendPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var footerLegendViewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Child = footerLegendPanel
            };
            Grid.SetColumn(footerLegendViewbox, 1);
            footerGrid.Children.Add(footerLegendViewbox);

            footerStatusText = new TextBlock
            {
                Text = string.Empty,
                Foreground = Brushes.White,
                FontSize = 14,
                Opacity = 0.72,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(24, 0, 0, 0),
                MaxWidth = 360,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(footerStatusText, 2);
            footerGrid.Children.Add(footerStatusText);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 17, 20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = footerGrid
            };
        }

        private void UpdateFooterForMode()
        {
            if (footerLegendPanel == null)
            {
                return;
            }

            footerLegendPanel.Children.Clear();

            if (viewMode == AnikiWebBrowserViewMode.Home)
            {
                AddFooterLegendItem("ButtonPromptA", "A", "WebBrowser_LegendSelect", "Select");
                AddFooterLegendItem("ButtonPromptStart", "Start", "WebBrowser_LegendEnter", "Enter");
                AddFooterLegendItem("ButtonPromptBack", "View", "WebBrowser_LegendCloseView", "Close web view");
                AddFooterLegendItem("ButtonPromptX", "X", "WebBrowser_LegendKeyboard", "Keyboard");

                var count = GetFavoritesSnapshot().Count;
                SetFooterStatus(
                    count == 1
                        ? Loc("WebBrowser_OneFavorite", "1 favorite")
                        : string.Format(
                            Loc("WebBrowser_FavoriteCount", "{0} favorites"),
                            count));
            }
            else
            {
                AddFooterLegendItem("ButtonPromptA", "A", "WebBrowser_LegendSelect", "Select");
                AddFooterLegendItem("ButtonPromptLB", "LB", "WebBrowser_LegendPrevious", "Previous");
                AddFooterLegendItem("ButtonPromptRB", "RB", "WebBrowser_LegendNext", "Next");
                AddFooterLegendItem("ButtonPromptStart", "Start", "WebBrowser_LegendEnter", "Enter");
                AddFooterLegendItem("ButtonPromptBack", "View", "WebBrowser_LegendCloseView", "Close web view");
                AddFooterLegendItem("ButtonPromptX", "X", "WebBrowser_LegendKeyboard", "Keyboard");
                SetFooterStatus(GetDisplayHost(SafeGetCurrentAddress()));
            }
        }

        private void AddFooterLegendItem(
            string resourceKey,
            string fallbackButtonText,
            string labelResourceKey,
            string fallbackLabel)
        {
            if (footerLegendPanel == null)
            {
                return;
            }

            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0)
            };

            item.Children.Add(CreateControllerPrompt(resourceKey, fallbackButtonText));
            item.Children.Add(new TextBlock
            {
                Text = Loc(labelResourceKey, fallbackLabel),
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.92,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                TextWrapping = TextWrapping.NoWrap
            });

            footerLegendPanel.Children.Add(item);
        }

        private FrameworkElement CreateControllerPrompt(string resourceKey, string fallbackButtonText)
        {
            try
            {
                DataTemplate template = null;

                var resourceWindow = windowHost ?? api?.Dialogs?.GetCurrentAppWindow();
                if (resourceWindow != null)
                {
                    template = resourceWindow.TryFindResource(resourceKey) as DataTemplate;
                }

                if (template == null && Application.Current != null)
                {
                    template = Application.Current.TryFindResource(resourceKey) as DataTemplate;
                }

                if (template != null)
                {
                    return new ContentControl
                    {
                        Content = true,
                        ContentTemplate = template,
                        Width = 28,
                        Height = 28,
                        Focusable = false,
                        IsHitTestVisible = false,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch (Exception ex)
            {
                DebugLog("[AnikiHelper][WebBrowser] Controller prompt resource failed: " + resourceKey + " - " + ex.Message);
            }

            return new Border
            {
                MinWidth = 30,
                Height = 30,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = fallbackButtonText,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private void ConfigureCoreWebView(CoreWebView2 core)
        {
            if (core == null)
            {
                throw new InvalidOperationException("WebView2 Core was not created.");
            }

            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;

            core.NavigationStarting += Core_NavigationStarting;
            core.NavigationCompleted += Core_NavigationCompleted;
            core.SourceChanged += Core_SourceChanged;
            core.DocumentTitleChanged += Core_DocumentTitleChanged;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.PermissionRequested += Core_PermissionRequested;
            core.DownloadStarting += Core_DownloadStarting;
            core.ProcessFailed += Core_ProcessFailed;
            core.WindowCloseRequested += Core_WindowCloseRequested;
        }

        private void UnconfigureCoreWebView(CoreWebView2 core)
        {
            if (core == null)
            {
                return;
            }

            try { core.NavigationStarting -= Core_NavigationStarting; } catch { }
            try { core.NavigationCompleted -= Core_NavigationCompleted; } catch { }
            try { core.SourceChanged -= Core_SourceChanged; } catch { }
            try { core.DocumentTitleChanged -= Core_DocumentTitleChanged; } catch { }
            try { core.NewWindowRequested -= Core_NewWindowRequested; } catch { }
            try { core.PermissionRequested -= Core_PermissionRequested; } catch { }
            try { core.DownloadStarting -= Core_DownloadStarting; } catch { }
            try { core.ProcessFailed -= Core_ProcessFailed; } catch { }
            try { core.WindowCloseRequested -= Core_WindowCloseRequested; } catch { }
        }

        private void OpenSearchOrAddressFromHome(string input)
        {
            var address = NormalizeSearchOrAddress(input);
            if (address == null)
            {
                ShowInvalidAddressMessage();
                return;
            }

            ShowWebCore(address, null);
        }

        private void NavigateCore(string address)
        {
            if (!IsAllowedAddress(address))
            {
                ShowInvalidAddressMessage();
                return;
            }

            var core = webView?.CoreWebView2;
            if (core == null)
            {
                pendingAddress = address;
                return;
            }

            try
            {
                pendingAddress = address;
                core.Navigate(address);
                SetFooterStatus(Loc("WebBrowser_Loading", "Loading…"));
                DebugLog("[AnikiHelper][WebBrowser] Navigate: " + address);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][WebBrowser] Navigation failed.");
                ShowErrorMessage(
                    Loc("WebBrowser_NavigationError", "The web address could not be opened."),
                    ex.Message);
            }
        }

        private void SendEnterCore()
        {
            if (!IsOpen || viewMode != AnikiWebBrowserViewMode.Web)
            {
                return;
            }

            try
            {
                var host = windowHost;
                if (host != null)
                {
                    var handle = new WindowInteropHelper(host).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        SetForegroundWindow(handle);
                    }
                }

                webView?.Focus();
                if (webView != null)
                {
                    Keyboard.Focus(webView);
                }

                // Send a real Windows Enter key so WebView2 treats it exactly like a
                // physical keyboard press. This works for search boxes, forms, dialogs
                // and focused buttons, unlike a synthetic JavaScript KeyboardEvent.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null &&
                    !dispatcher.HasShutdownStarted &&
                    !dispatcher.HasShutdownFinished)
                {
                    dispatcher.BeginInvoke(new Action(delegate
                    {
                        keybd_event(VirtualKeyReturn, 0, 0, UIntPtr.Zero);
                        keybd_event(VirtualKeyReturn, 0, KeyEventKeyUp, UIntPtr.Zero);
                    }), DispatcherPriority.Input);
                }
                else
                {
                    keybd_event(VirtualKeyReturn, 0, 0, UIntPtr.Zero);
                    keybd_event(VirtualKeyReturn, 0, KeyEventKeyUp, UIntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Enter key injection failed.");
            }
        }

        private void RefreshCore()
        {
            try
            {
                webView?.CoreWebView2?.Reload();
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Refresh failed.");
            }
        }

        private void GoBackOrHomeCore()
        {
            try
            {
                var core = webView?.CoreWebView2;
                if (core != null && core.CanGoBack)
                {
                    core.GoBack();
                    return;
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Back navigation failed.");
            }

            ShowHomeCore(true);
        }

        private void GoForwardCore()
        {
            try
            {
                var core = webView?.CoreWebView2;
                if (core != null && core.CanGoForward)
                {
                    core.GoForward();
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Forward navigation failed.");
            }
        }

        private void OpenVirtualKeyboardCore()
        {
            if (!IsOpen || viewMode != AnikiWebBrowserViewMode.Web)
            {
                return;
            }

            pointerController.SuspendInput();

            try
            {
                webView?.Focus();
                openVirtualKeyboard?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][WebBrowser] Failed to open virtual keyboard.");
            }
        }

        private void OpenHomeKeyboardCore()
        {
            if (!IsOpen || viewMode != AnikiWebBrowserViewMode.Home)
            {
                return;
            }

            try
            {
                openVirtualKeyboard?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][WebBrowser] Failed to open home search keyboard.");
            }
        }

        private void OpenAddressPromptCore()
        {
            var host = windowHost;
            if (host == null || webView == null || viewMode != AnikiWebBrowserViewMode.Web)
            {
                return;
            }

            pointerController.SuspendInput();

            var currentAddress = SafeGetCurrentAddress();
            var wasVisible = host.IsVisible;

            try
            {
                if (wasVisible)
                {
                    host.Hide();
                }

                var result = api.Dialogs.SelectString(
                    Loc("WebBrowser_AddressPrompt", "Enter a web address"),
                    Loc("WebBrowser_AddressPrompt", "Enter a web address"),
                    currentAddress);

                if (result != null && result.Result)
                {
                    var normalized = NormalizeSearchOrAddress(result.SelectedString);
                    if (normalized == null)
                    {
                        ShowInvalidAddressMessage();
                    }
                    else
                    {
                        NavigateCore(normalized);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][WebBrowser] Address prompt failed.");
            }
            finally
            {
                try
                {
                    if (wasVisible && windowHost != null)
                    {
                        windowHost.Show();
                        BringBrowserToForeground();
                    }
                }
                catch
                {
                }
            }
        }

        private void CloseCore()
        {
            if (closing)
            {
                return;
            }

            closing = true;
            pointerController.SuspendInput();

            var host = windowHost;
            var view = webView;

            try
            {
                host?.Close();
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Window close failed.");
                CleanupCore(host, view);
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            dispatcher?.BeginInvoke(new Action(delegate
            {
                if (ReferenceEquals(windowHost, host))
                {
                    CleanupCore(host, view);
                }
            }), DispatcherPriority.Background);
        }

        private void WindowHost_Closed(object sender, EventArgs e)
        {
            CleanupCore(sender as Window, webView);
        }

        private void WindowHost_Activated(object sender, EventArgs e)
        {
            browserWindowActive = true;

            if (pointerSessionActive)
            {
                pointerController.ResumeInput(windowHost);
            }

            if (viewMode == AnikiWebBrowserViewMode.Web)
            {
                try { webView?.Focus(); } catch { }
            }
            else if (!homePointerMode)
            {
                Application.Current?.Dispatcher?.BeginInvoke(
                    new Action(delegate { homeView?.FocusInitial(); }),
                    DispatcherPriority.Input);
            }
        }

        private void WindowHost_Deactivated(object sender, EventArgs e)
        {
            browserWindowActive = false;
            pointerController.SuspendInput();
        }

        private void WindowHost_LocationChanged(object sender, EventArgs e)
        {
            if (pointerSessionActive)
            {
                pointerController.UpdateBounds(windowHost);
            }
        }

        private void WindowHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (pointerSessionActive)
            {
                pointerController.UpdateBounds(windowHost);
            }
        }

        private void WindowHost_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (viewMode == AnikiWebBrowserViewMode.Web)
                {
                    GoBackOrHomeCore();
                }
                else
                {
                    CloseCore();
                }

                e.Handled = true;
            }
            else if (e.Key == Key.F5 && viewMode == AnikiWebBrowserViewMode.Web)
            {
                RefreshCore();
                e.Handled = true;
            }
        }

        private void Core_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsAllowedAddress(e.Uri) || IsAllowedInternalAddress(e.Uri))
            {
                if (viewMode == AnikiWebBrowserViewMode.Web)
                {
                    SetFooterStatus(Loc("WebBrowser_Loading", "Loading…"));
                }

                return;
            }

            e.Cancel = true;
            logger?.Debug("[AnikiHelper][WebBrowser] Blocked navigation protocol: " + (e.Uri ?? string.Empty));
            SetFooterStatus(Loc("WebBrowser_BlockedProtocol", "Blocked unsupported link"));
        }

        private void Core_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (viewMode != AnikiWebBrowserViewMode.Web)
            {
                return;
            }

            if (e.IsSuccess)
            {
                SetFooterStatus(GetDisplayHost(SafeGetCurrentAddress()));
            }
            else
            {
                SetFooterStatus(Loc("WebBrowser_LoadFailed", "Page failed to load"));
                logger?.Debug(
                    "[AnikiHelper][WebBrowser] Navigation failed. Error=" + e.WebErrorStatus);
            }

            UpdateWindowTitle();
        }

        private void Core_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (viewMode != AnikiWebBrowserViewMode.Web)
            {
                return;
            }

            var address = SafeGetCurrentAddress();
            if (IsAllowedAddress(address))
            {
                pendingAddress = address;
            }

            SetFooterStatus(GetDisplayHost(address));
        }

        private void Core_DocumentTitleChanged(object sender, object e)
        {
            if (viewMode == AnikiWebBrowserViewMode.Web)
            {
                UpdateWindowTitle();
            }
        }

        private void Core_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            if (IsAllowedAddress(e.Uri))
            {
                ShowWebCore(e.Uri, requestedTitle);
            }
            else
            {
                SetFooterStatus(Loc("WebBrowser_BlockedProtocol", "Blocked unsupported link"));
            }
        }

        private void Core_PermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            e.State = CoreWebView2PermissionState.Deny;
            logger?.Debug(
                "[AnikiHelper][WebBrowser] Permission denied: " + e.PermissionKind);
        }

        private void Core_DownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            e.Cancel = true;
            SetFooterStatus(Loc("WebBrowser_DownloadBlocked", "Downloads are disabled"));
            logger?.Debug("[AnikiHelper][WebBrowser] Download blocked.");
        }

        private void Core_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            logger?.Warn(
                "[AnikiHelper][WebBrowser] WebView2 process failed: " + e.ProcessFailedKind);
            SetFooterStatus(Loc("WebBrowser_ProcessFailed", "Browser process stopped"));
        }

        private void Core_WindowCloseRequested(object sender, object e)
        {
            CloseCore();
        }

        private void BringBrowserToForeground()
        {
            var host = windowHost;
            if (host == null)
            {
                return;
            }

            try
            {
                if (!host.IsVisible)
                {
                    host.Show();
                }

                host.WindowState = WindowState.Normal;
                host.Activate();
                host.Focus();

                var handle = new WindowInteropHelper(host).Handle;
                if (handle != IntPtr.Zero)
                {
                    SetForegroundWindow(handle);
                }

                browserWindowActive = true;

                if (pointerSessionActive)
                {
                    pointerController.ResumeInput(host);
                }

                if (viewMode == AnikiWebBrowserViewMode.Web)
                {
                    webView?.Focus();
                }
                else if (!homePointerMode)
                {
                    homeView?.FocusInitial();
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Failed to focus browser window.");
            }
        }

        private void UpdateWindowTitle()
        {
            var host = windowHost;
            if (host == null)
            {
                return;
            }

            if (viewMode == AnikiWebBrowserViewMode.Home)
            {
                host.Title = Loc("WebBrowser_WindowTitle", "Aniki Web Browser");
                return;
            }

            try
            {
                var pageTitle = webView?.CoreWebView2?.DocumentTitle;
                var baseTitle = string.IsNullOrWhiteSpace(requestedTitle)
                    ? Loc("WebBrowser_WindowTitle", "Aniki Web Browser")
                    : requestedTitle.Trim();

                host.Title = string.IsNullOrWhiteSpace(pageTitle)
                    ? baseTitle
                    : pageTitle + " — " + baseTitle;
            }
            catch
            {
                host.Title = BuildWindowTitle();
            }
        }

        private string BuildWindowTitle()
        {
            return string.IsNullOrWhiteSpace(requestedTitle)
                ? Loc("WebBrowser_WindowTitle", "Aniki Web Browser")
                : requestedTitle.Trim();
        }

        private void SetFooterStatus(string value)
        {
            if (footerStatusText != null)
            {
                footerStatusText.Text = value ?? string.Empty;
            }
        }

        private List<AnikiWebFavorite> GetFavoritesSnapshot()
        {
            try
            {
                return (favoritesProvider?.Invoke() ?? Enumerable.Empty<AnikiWebFavorite>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                    .Select(x => x.Clone())
                    .ToList();
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Failed to read favorites.");
                return new List<AnikiWebFavorite>();
            }
        }

        private void CleanupCore(Window expectedHost, WebView2 expectedView)
        {
            if (windowHost == null && webView == null)
            {
                return;
            }

            if (expectedHost != null && windowHost != null && !ReferenceEquals(expectedHost, windowHost))
            {
                return;
            }

            sessionGeneration++;

            var host = windowHost ?? expectedHost;
            var view = webView ?? expectedView;
            var core = view?.CoreWebView2;

            windowHost = null;
            browserArea = null;
            homeView = null;
            webView = null;
            loadingText = null;
            footerStatusText = null;
            pendingAddress = string.Empty;
            requestedTitle = string.Empty;
            browserWindowActive = false;
            initializing = false;
            closing = false;
            pointerSessionActive = false;
            homePointerMode = false;
            viewMode = AnikiWebBrowserViewMode.Home;

            pointerController.EndSession();

            if (host != null)
            {
                try { host.Closed -= WindowHost_Closed; } catch { }
                try { host.Activated -= WindowHost_Activated; } catch { }
                try { host.Deactivated -= WindowHost_Deactivated; } catch { }
                try { host.LocationChanged -= WindowHost_LocationChanged; } catch { }
                try { host.SizeChanged -= WindowHost_SizeChanged; } catch { }
                try { host.PreviewKeyDown -= WindowHost_PreviewKeyDown; } catch { }
            }

            UnconfigureCoreWebView(core);

            try { core?.Stop(); } catch { }
            try { view?.Dispose(); } catch { }

            if (host != null && host.IsVisible)
            {
                try { host.Close(); } catch { }
            }

            OpenStateChanged?.Invoke(false);
            DebugLog("[AnikiHelper][WebBrowser] Closed.");
        }

        private bool IsCurrentSession(int generation, Window expectedHost, WebView2 expectedView)
        {
            return !disposed &&
                   !closing &&
                   generation == sessionGeneration &&
                   ReferenceEquals(windowHost, expectedHost) &&
                   ReferenceEquals(webView, expectedView);
        }

        private Task RunProfileOperationAsync(Func<CoreWebView2Profile, Task> operation)
        {
            if (operation == null)
            {
                return Task.FromResult(0);
            }

            return InvokeOnUiAsync(async delegate
            {
                if (IsOpen)
                {
                    CloseCore();
                    await Task.Delay(120);
                }

                Directory.CreateDirectory(userDataFolder);

                var hiddenHost = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Width = 2,
                    Height = 2,
                    Left = -32000,
                    Top = -32000,
                    Opacity = 0
                };

                var hiddenView = new WebView2();
                hiddenHost.Content = hiddenView;

                try
                {
                    hiddenHost.Show();

                    var localEnvironment = environment;
                    if (localEnvironment == null)
                    {
                        localEnvironment = await CoreWebView2Environment.CreateAsync(
                            null,
                            userDataFolder,
                            new CoreWebView2EnvironmentOptions());
                        environment = localEnvironment;
                    }

                    await hiddenView.EnsureCoreWebView2Async(localEnvironment);
                    await operation(hiddenView.CoreWebView2.Profile);
                }
                finally
                {
                    try { hiddenView.Dispose(); } catch { }
                    try { hiddenHost.Close(); } catch { }
                }
            });
        }

        private string SafeGetCurrentAddress()
        {
            try
            {
                return webView?.Source?.AbsoluteUri ??
                       webView?.CoreWebView2?.Source ??
                       string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeSearchOrAddress(string input)
        {
            var value = (input ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return null;
            }

            var direct = NormalizeAddress(value);
            if (direct != null &&
                (value.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                 value.IndexOf('.') >= 0 ||
                 value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                return direct;
            }

            return "https://www.google.com/search?q=" + Uri.EscapeDataString(value);
        }

        private static string NormalizeAddress(string input)
        {
            var value = (input ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!value.Contains("://"))
            {
                value = "https://" + value;
            }

            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                return null;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return uri.AbsoluteUri;
        }

        private static bool IsAllowedAddress(string address)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(address) ||
                !Uri.TryCreate(address.Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllowedInternalAddress(string address)
        {
            return string.Equals(
                (address ?? string.Empty).Trim(),
                "about:blank",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDisplayHost(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri)
                ? uri.Host
                : string.Empty;
        }

        private ImageSource LoadActiveThemeImage(string relativePath)
        {
            try
            {
                var themeId = api?.ApplicationSettings?.FullscreenTheme;
                if (string.IsNullOrWhiteSpace(themeId) ||
                    string.IsNullOrWhiteSpace(relativePath) ||
                    api?.Paths == null)
                {
                    return null;
                }

                var normalizedRelativePath = relativePath
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);

                var roots = new[]
                {
                    api.Paths.ConfigurationPath,
                    api.Paths.ApplicationPath
                }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var root in roots)
                {
                    var imagePath = Path.Combine(
                        root,
                        "Themes",
                        "Fullscreen",
                        themeId,
                        normalizedRelativePath);

                    if (!File.Exists(imagePath))
                    {
                        continue;
                    }

                    using (var stream = new FileStream(
                        imagePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(
                    ex,
                    "[AnikiHelper][WebBrowser] Failed to load the active theme logo.");
            }

            return null;
        }

        private static string ResolveUserDataFolder(IPlayniteAPI api, string requestedFolder)
        {
            if (!string.IsNullOrWhiteSpace(requestedFolder))
            {
                return requestedFolder;
            }

            var root = api?.Paths?.ExtensionsDataPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AnikiHelper");
            }

            return Path.Combine(root, "AnikiWebBrowser", "WebView2Profile");
        }

        private static void ConfigureWindowBounds(Window host, Window owner)
        {
            if (owner != null)
            {
                host.Left = owner.Left;
                host.Top = owner.Top;
                host.Width = Math.Max(
                    MinimumWidth,
                    owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width);
                host.Height = Math.Max(
                    MinimumHeight,
                    owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);
                return;
            }

            host.Left = 0;
            host.Top = 0;
            host.Width = Math.Max(MinimumWidth, SystemParameters.PrimaryScreenWidth);
            host.Height = Math.Max(MinimumHeight, SystemParameters.PrimaryScreenHeight);
        }

        private void ShowInvalidAddressMessage()
        {
            ShowErrorMessage(
                Loc("WebBrowser_InvalidAddress", "Enter a valid HTTP or HTTPS web address."),
                null);
        }

        private void ShowErrorMessage(string message, string detail)
        {
            var fullMessage = string.IsNullOrWhiteSpace(detail)
                ? message
                : message + Environment.NewLine + Environment.NewLine + detail;

            InvokeOnUi(delegate
            {
                try
                {
                    if (api?.Dialogs != null)
                    {
                        api.Dialogs.ShowMessage(
                            fullMessage,
                            Loc("WebBrowser_WindowTitle", "Aniki Web Browser"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    else
                    {
                        MessageBox.Show(
                            fullMessage,
                            "Aniki Helper",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch
                {
                    MessageBox.Show(
                        fullMessage,
                        "Aniki Helper",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });
        }

        private static string Loc(string key, string fallback)
        {
            try
            {
                var value = Application.Current?.TryFindResource(key) as string;
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private void InvokeOnUi(Action action)
        {
            if (action == null)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            }
        }

        private Task InvokeOnUiAsync(Func<Task> action)
        {
            if (action == null)
            {
                return Task.FromResult(0);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return Task.FromResult(0);
            }

            if (dispatcher.CheckAccess())
            {
                return action();
            }

            var completion = new TaskCompletionSource<bool>();
            dispatcher.BeginInvoke(new Action(async delegate
            {
                try
                {
                    await action();
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }), DispatcherPriority.Normal);

            return completion.Task;
        }

        private void DebugLog(string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug(message);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Close();
            pointerController.Dispose();
        }

        private const byte VirtualKeyReturn = 0x0D;
        private const uint KeyEventKeyUp = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    internal sealed class BrowserPointerController : IDisposable
    {
        private const int AxisDeadZone = 6500;
        private const double MaximumCursorPixelsPerFrame = 22.0;
        private const double WheelUnitsPerFrame = 88.0;
        private const double EdgeScrollZoneDip = 72.0;
        private const double EdgeScrollMinimumUnitsPerFrame = 3.0;
        private const double EdgeScrollMaximumUnitsPerFrame = 20.0;
        private const double EdgeScrollVelocitySmoothing = 0.18;
        private const int WheelDelta = 120;

        private readonly ILogger logger;
        private readonly double bottomInsetDip;
        private readonly object stateLock = new object();
        private readonly Input[] inputBuffer = new Input[1];

        private Window hostWindow;
        private bool sessionActive;
        private bool inputSuspended;
        private bool leftButtonDown;
        private bool ignoreClickUntilReleased;
        private double wheelAccumulator;
        private double edgeScrollVelocity;
        private double edgeWheelRemainder;
        private bool cursorVisibilityHeld;
        private int cursorVisibilityAdjustments;
        private Rect pointerBounds;
        private bool hasPointerBounds;
        private int edgeScrollZonePixels;
        private DateTime lastSendInputFailureLogUtc = DateTime.MinValue;

        public BrowserPointerController(ILogger logger, double bottomInsetDip)
        {
            this.logger = logger;
            this.bottomInsetDip = Math.Max(0, bottomInsetDip);
        }

        public void BeginSession(Window host)
        {
            lock (stateLock)
            {
                hostWindow = host;
                sessionActive = true;
                inputSuspended = false;
                leftButtonDown = false;
                ignoreClickUntilReleased = true;
                wheelAccumulator = 0;
                edgeScrollVelocity = 0;
                edgeWheelRemainder = 0;
                UpdatePointerBounds(hostWindow);
                AcquireVisibleCursor();
                LockCursorToPointerBounds();
            }

            CenterCursorInViewport(host);
        }

        public void ResumeInput(Window host)
        {
            lock (stateLock)
            {
                if (!sessionActive)
                {
                    return;
                }

                if (host != null)
                {
                    hostWindow = host;
                }

                inputSuspended = false;
                ignoreClickUntilReleased = true;
                wheelAccumulator = 0;
                edgeScrollVelocity = 0;
                edgeWheelRemainder = 0;
                UpdatePointerBounds(hostWindow);
                AcquireVisibleCursor();
                LockCursorToPointerBounds();
            }
        }

        public void UpdateBounds(Window host)
        {
            lock (stateLock)
            {
                if (!sessionActive || inputSuspended)
                {
                    return;
                }

                if (host != null)
                {
                    hostWindow = host;
                }

                UpdatePointerBounds(hostWindow);
                LockCursorToPointerBounds();
            }
        }

        public void ProcessInput(WebBrowserGamepadInputState state)
        {
            lock (stateLock)
            {
                if (!sessionActive || inputSuspended)
                {
                    return;
                }

                // Match Aniki Helper's global gamepad mouse controls:
                // right stick moves the pointer, left stick scrolls.
                var normalizedX = NormalizeAxis(state.RightX);
                var normalizedY = NormalizeAxis(state.RightY);
                var deltaX = CalculateCursorDelta(normalizedX);
                var deltaY = CalculateCursorDelta(normalizedY);

                if (deltaX != 0 || deltaY != 0)
                {
                    // Reuse the exact relative SendInput path used by Aniki Helper's global
                    // gamepad mouse. It remains reliable while WebView2 owns keyboard focus.
                    EnsureVisibleCursor();
                    SendMouseInput(deltaX, deltaY, 0, MouseEventMove);
                }

                if (ignoreClickUntilReleased)
                {
                    if (!state.LeftClick)
                    {
                        ignoreClickUntilReleased = false;
                    }
                }
                else
                {
                    UpdateLeftButton(state.LeftClick);
                }

                // SDL reports negative Y when the stick is pushed up. Windows expects a
                // positive wheel delta to scroll up, so the value is intentionally inverted.
                // The left stick is used for scrolling to stay consistent with the existing
                // Aniki Helper gamepad mouse feature. When the stick is idle, keeping the
                // cursor near the top or bottom edge automatically scrolls the page.
                var stickScrollDirection = -NormalizeAxis(state.LeftY);

                if (Math.Abs(stickScrollDirection) >= 0.01)
                {
                    // Keep the existing detented wheel behaviour for the left stick, which
                    // is intentionally the fast/manual scrolling control.
                    edgeScrollVelocity = 0;
                    edgeWheelRemainder = 0;

                    var curved = Math.Sign(stickScrollDirection) *
                                 Math.Pow(Math.Abs(stickScrollDirection), 1.25);
                    wheelAccumulator += curved * WheelUnitsPerFrame;

                    if (Math.Abs(wheelAccumulator) >= WheelDelta)
                    {
                        var steps = (int)(wheelAccumulator / WheelDelta);
                        var wheelData = steps * WheelDelta;
                        wheelAccumulator -= wheelData;
                        SendMouseInput(0, 0, wheelData, MouseEventWheel);
                    }
                }
                else
                {
                    // Edge scrolling uses high-resolution wheel deltas every frame instead
                    // of waiting for a full 120-unit wheel notch. That preserves the same
                    // overall speed while removing the visible low-speed jolts.
                    wheelAccumulator = 0;
                    var edgeScrollDirection = GetEdgeScrollDirection();

                    if (Math.Abs(edgeScrollDirection) < 0.01)
                    {
                        edgeScrollVelocity = 0;
                        edgeWheelRemainder = 0;
                    }
                    else
                    {
                        var edgeStrength = Math.Pow(Math.Abs(edgeScrollDirection), 1.35);
                        var edgeUnits = EdgeScrollMinimumUnitsPerFrame +
                                        ((EdgeScrollMaximumUnitsPerFrame -
                                          EdgeScrollMinimumUnitsPerFrame) * edgeStrength);
                        var targetVelocity = Math.Sign(edgeScrollDirection) * edgeUnits;

                        edgeScrollVelocity +=
                            (targetVelocity - edgeScrollVelocity) * EdgeScrollVelocitySmoothing;
                        edgeWheelRemainder += edgeScrollVelocity;

                        var wheelData = (int)Math.Truncate(edgeWheelRemainder);
                        if (wheelData != 0)
                        {
                            edgeWheelRemainder -= wheelData;
                            SendMouseInput(0, 0, wheelData, MouseEventWheel);
                        }
                    }
                }
            }
        }

        public void ProcessHomeInput(
            WebBrowserGamepadInputState state,
            bool pointerClickEnabled)
        {
            lock (stateLock)
            {
                if (!sessionActive || inputSuspended)
                {
                    return;
                }

                // The favorites home is a native WPF view. Use the same right-stick
                // pointer and native mouse click path as WebView2, but do not apply the
                // page scrolling logic because the left stick remains dedicated to
                // controller focus navigation on this screen.
                var normalizedX = NormalizeAxis(state.RightX);
                var normalizedY = NormalizeAxis(state.RightY);
                var deltaX = CalculateCursorDelta(normalizedX);
                var deltaY = CalculateCursorDelta(normalizedY);

                if (deltaX != 0 || deltaY != 0)
                {
                    EnsureVisibleCursor();
                    SendMouseInput(deltaX, deltaY, 0, MouseEventMove);
                }

                if (pointerClickEnabled)
                {
                    if (ignoreClickUntilReleased)
                    {
                        if (!state.LeftClick)
                        {
                            ignoreClickUntilReleased = false;
                        }
                    }
                    else
                    {
                        UpdateLeftButton(state.LeftClick);
                    }
                }
                else
                {
                    ReleaseLeftButton();
                    ignoreClickUntilReleased = state.LeftClick;
                }

                wheelAccumulator = 0;
                edgeScrollVelocity = 0;
                edgeWheelRemainder = 0;
            }
        }

        public void SuspendInput()
        {
            lock (stateLock)
            {
                if (!sessionActive)
                {
                    return;
                }

                ReleaseLeftButton();
                inputSuspended = true;
                ignoreClickUntilReleased = true;
                wheelAccumulator = 0;
                edgeScrollVelocity = 0;
                edgeWheelRemainder = 0;
                UnlockCursor();
                ReleaseVisibleCursor();
            }
        }

        public void EndSession()
        {
            lock (stateLock)
            {
                ReleaseLeftButton();
                sessionActive = false;
                inputSuspended = false;
                ignoreClickUntilReleased = false;
                wheelAccumulator = 0;
                edgeScrollVelocity = 0;
                edgeWheelRemainder = 0;
                hostWindow = null;
                hasPointerBounds = false;
                edgeScrollZonePixels = 0;
                UnlockCursor();
                ReleaseVisibleCursor();
            }
        }

        private void UpdateLeftButton(bool pressed)
        {
            if (pressed == leftButtonDown)
            {
                return;
            }

            SendMouseInput(0, 0, 0, pressed ? MouseEventLeftDown : MouseEventLeftUp);
            leftButtonDown = pressed;
        }

        private void ReleaseLeftButton()
        {
            if (!leftButtonDown)
            {
                return;
            }

            SendMouseInput(0, 0, 0, MouseEventLeftUp);
            leftButtonDown = false;
        }

        private static double NormalizeAxis(short value)
        {
            var absolute = Math.Abs((int)value);
            if (absolute <= AxisDeadZone)
            {
                return 0;
            }

            var normalized = (absolute - AxisDeadZone) / (32767.0 - AxisDeadZone);
            normalized = Math.Max(0, Math.Min(1, normalized));
            return value < 0 ? -normalized : normalized;
        }

        private static int CalculateCursorDelta(double normalized)
        {
            if (Math.Abs(normalized) < double.Epsilon)
            {
                return 0;
            }

            var magnitude = Math.Abs(normalized);
            var accelerated = 1.0 +
                              ((MaximumCursorPixelsPerFrame - 1.0) * Math.Pow(magnitude, 1.7));
            return (int)Math.Round(normalized < 0 ? -accelerated : accelerated);
        }

        private double GetEdgeScrollDirection()
        {
            if (!hasPointerBounds || edgeScrollZonePixels <= 0)
            {
                return 0;
            }

            NativePoint cursorPoint;
            if (!GetCursorPos(out cursorPoint))
            {
                return 0;
            }

            if (cursorPoint.X < pointerBounds.Left || cursorPoint.X >= pointerBounds.Right ||
                cursorPoint.Y < pointerBounds.Top || cursorPoint.Y >= pointerBounds.Bottom)
            {
                return 0;
            }

            var topEdgeLimit = pointerBounds.Top + edgeScrollZonePixels;
            if (cursorPoint.Y <= topEdgeLimit)
            {
                return Clamp01((topEdgeLimit - cursorPoint.Y) /
                               (double)edgeScrollZonePixels);
            }

            var bottomEdgeLimit = pointerBounds.Bottom - edgeScrollZonePixels;
            if (cursorPoint.Y >= bottomEdgeLimit)
            {
                return -Clamp01((cursorPoint.Y - bottomEdgeLimit) /
                                (double)edgeScrollZonePixels);
            }

            return 0;
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0, Math.Min(1, value));
        }

        private void UpdatePointerBounds(Window host)
        {
            hasPointerBounds = false;
            edgeScrollZonePixels = 0;

            try
            {
                if (host == null || !host.IsVisible)
                {
                    return;
                }

                var topLeft = host.PointToScreen(new Point(0, 0));
                var scaleX = 1.0;
                var scaleY = 1.0;
                var source = PresentationSource.FromVisual(host);
                var compositionTarget = source == null ? null : source.CompositionTarget;
                if (compositionTarget != null)
                {
                    var transformToDevice = compositionTarget.TransformToDevice;
                    scaleX = transformToDevice.M11;
                    scaleY = transformToDevice.M22;
                }

                var widthPixels = Math.Max(
                    1,
                    (int)Math.Round(host.ActualWidth * scaleX));
                var heightPixels = Math.Max(
                    1,
                    (int)Math.Round(host.ActualHeight * scaleY));
                var footerInsetPixels = Math.Max(
                    0,
                    (int)Math.Round(bottomInsetDip * scaleY));

                var left = (int)Math.Round(topLeft.X);
                var top = (int)Math.Round(topLeft.Y);
                var right = left + widthPixels;
                var bottom = top + Math.Max(1, heightPixels - footerInsetPixels);

                if (right <= left || bottom <= top)
                {
                    return;
                }

                pointerBounds = new Rect
                {
                    Left = left,
                    Top = top,
                    Right = right,
                    Bottom = bottom
                };
                edgeScrollZonePixels = Math.Max(
                    20,
                    (int)Math.Round(EdgeScrollZoneDip * scaleY));
                hasPointerBounds = true;
            }
            catch (Exception ex)
            {
                logger?.Debug(
                    ex,
                    "[AnikiHelper][WebBrowser] Failed to update browser pointer bounds.");

                try
                {
                    var handle = host == null
                        ? IntPtr.Zero
                        : new WindowInteropHelper(host).Handle;
                    Rect fallbackBounds;
                    if (handle != IntPtr.Zero &&
                        GetWindowRect(handle, out fallbackBounds))
                    {
                        fallbackBounds.Bottom = Math.Max(
                            fallbackBounds.Top + 1,
                            fallbackBounds.Bottom - (int)Math.Round(bottomInsetDip));

                        pointerBounds = fallbackBounds;
                        edgeScrollZonePixels = Math.Max(
                            20,
                            (int)Math.Round(EdgeScrollZoneDip));
                        hasPointerBounds = true;
                    }
                }
                catch
                {
                }
            }
        }

        private void MoveCursor(int deltaX, int deltaY)
        {
            try
            {
                NativePoint cursorPoint;
                if (!GetCursorPos(out cursorPoint))
                {
                    return;
                }

                var nextX = cursorPoint.X + deltaX;
                var nextY = cursorPoint.Y + deltaY;

                var host = hostWindow;
                var handle = host == null ? IntPtr.Zero : new WindowInteropHelper(host).Handle;
                Rect bounds;
                if (handle != IntPtr.Zero && GetWindowRect(handle, out bounds))
                {
                    nextX = Math.Max(bounds.Left + 1, Math.Min(bounds.Right - 2, nextX));
                    nextY = Math.Max(bounds.Top + 1, Math.Min(bounds.Bottom - 2, nextY));
                }

                if (!SetCursorPos(nextX, nextY) &&
                    (DateTime.UtcNow - lastSendInputFailureLogUtc).TotalSeconds >= 5)
                {
                    lastSendInputFailureLogUtc = DateTime.UtcNow;
                    logger?.Debug(
                        "[AnikiHelper][WebBrowser] SetCursorPos failed. Win32Error=" +
                        Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Cursor movement failed.");
            }
        }

        private void EnsureVisibleCursor()
        {
            if (!cursorVisibilityHeld)
            {
                AcquireVisibleCursor();
                return;
            }

            try
            {
                var info = new CursorInfo
                {
                    Size = Marshal.SizeOf(typeof(CursorInfo))
                };

                if (GetCursorInfo(ref info) && (info.Flags & CursorShowing) != 0)
                {
                    return;
                }

                // Playnite Fullscreen may hide the native cursor again after WebView2 takes
                // focus. Restore it only when Windows reports it hidden and remember each
                // counter adjustment so it can be undone when the browser closes.
                for (var attempt = 0; attempt < 16; attempt++)
                {
                    var displayCount = ShowCursor(true);
                    cursorVisibilityAdjustments++;
                    if (displayCount >= 0)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Failed to restore the native cursor.");
            }
        }

        private void AcquireVisibleCursor()
        {
            if (cursorVisibilityHeld)
            {
                return;
            }

            cursorVisibilityHeld = true;
            cursorVisibilityAdjustments = 0;

            try
            {
                // Playnite Fullscreen can keep the native cursor display counter below zero.
                // Raise it only for this browser session, then restore the exact count later.
                for (var attempt = 0; attempt < 16; attempt++)
                {
                    var displayCount = ShowCursor(true);
                    cursorVisibilityAdjustments++;
                    if (displayCount >= 0)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Failed to show the native cursor.");
            }
        }

        private void ReleaseVisibleCursor()
        {
            if (!cursorVisibilityHeld)
            {
                return;
            }

            cursorVisibilityHeld = false;

            try
            {
                for (var i = 0; i < cursorVisibilityAdjustments; i++)
                {
                    ShowCursor(false);
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] Failed to restore cursor visibility.");
            }
            finally
            {
                cursorVisibilityAdjustments = 0;
            }
        }

        private void SendMouseInput(int dx, int dy, int mouseData, uint flags)
        {
            inputBuffer[0] = new Input
            {
                Type = InputMouse,
                MouseInput = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = mouseData,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            };

            try
            {
                var sent = SendInput(1, inputBuffer, Marshal.SizeOf(typeof(Input)));
                if (sent == 0 &&
                    (DateTime.UtcNow - lastSendInputFailureLogUtc).TotalSeconds >= 5)
                {
                    lastSendInputFailureLogUtc = DateTime.UtcNow;
                    logger?.Debug(
                        "[AnikiHelper][WebBrowser] SendInput failed. Win32Error=" +
                        Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WebBrowser] SendInput threw an exception.");
            }
        }

        private void CenterCursorInViewport(Window host)
        {
            try
            {
                if (hasPointerBounds)
                {
                    var centerX = pointerBounds.Left +
                                  ((pointerBounds.Right - pointerBounds.Left) / 2);
                    var centerY = pointerBounds.Top +
                                  ((pointerBounds.Bottom - pointerBounds.Top) / 2);
                    SetCursorPos(centerX, centerY);
                    return;
                }

                if (host == null)
                {
                    return;
                }

                var point = host.PointToScreen(
                    new Point(host.ActualWidth / 2.0, host.ActualHeight / 2.0));
                SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y));
            }
            catch
            {
            }
        }

        private void LockCursorToPointerBounds()
        {
            try
            {
                if (hasPointerBounds)
                {
                    var bounds = pointerBounds;
                    ClipCursor(ref bounds);
                    return;
                }

                var host = hostWindow;
                if (host == null || !host.IsVisible)
                {
                    return;
                }

                var handle = new WindowInteropHelper(host).Handle;
                Rect windowRect;
                if (handle != IntPtr.Zero && GetWindowRect(handle, out windowRect))
                {
                    ClipCursor(ref windowRect);
                }
            }
            catch
            {
            }
        }

        private static void UnlockCursor()
        {
            try
            {
                ClipCursor(IntPtr.Zero);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            EndSession();
        }

        private const uint InputMouse = 0;
        private const uint MouseEventMove = 0x0001;
        private const int CursorShowing = 0x00000001;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint MouseEventWheel = 0x0800;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public MouseInput MouseInput;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx;
            public int Dy;
            public int MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int Size;
            public int Flags;
            public IntPtr CursorHandle;
            public NativePoint ScreenPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool show);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref Rect rect);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr rect);
    }
}
