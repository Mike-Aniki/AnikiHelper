using AnikiHelper.Services.CommunityPacks;
using AnikiHelper.Services.Packs;
using AnikiHelper.Services.VisualPacks;
using AnikiHelperFullscreen.Views;
using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnikiHelper
{
    public sealed class AnikiCommunityVisualPacksFullscreenController : IDisposable
    {
        private static readonly string[] HubPackTypes =
        {
            "complete",
            "visual",
            "login",
            "color",
            "sound"
        };

        private readonly global::AnikiHelper.AnikiHelper plugin;
        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly string pluginUserDataPath;
        private readonly Dictionary<string, CommunityPackService> services =
            new Dictionary<string, CommunityPackService>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CommunityVisualPackViewItem> allPacks =
            new List<CommunityVisualPackViewItem>();
        private readonly CommunityVisualPacksViewModel viewModel = new CommunityVisualPacksViewModel();
        private readonly string hubStatePath;
        private readonly string downloadStatsCachePath;
        private readonly Dictionary<string, long> downloadCountsByPackId =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> seenPackKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly DateTime legacyNewSinceUtc;
        private bool seenPackIdentityStateInitialized;

        private CancellationTokenSource refreshCts;
        private string packType = "complete";
        private string sortMode = "newest";
        private bool loadedOnce;
        private bool disposed;

        public UserControl Control { get; }

        private sealed class CommunityHubState
        {
            public int Version { get; set; }
            public DateTime LastSeenUtc { get; set; }
            public List<string> SeenPackKeys { get; set; } = new List<string>();
        }

        private sealed class CommunityDownloadStatsCache
        {
            public DateTime FetchedUtc { get; set; }
            public Dictionary<string, long> Counts { get; set; } = new Dictionary<string, long>();
        }

        private sealed class GitHubReleaseInfo
        {
            [JsonProperty("assets")]
            public List<GitHubReleaseAssetInfo> Assets { get; set; } = new List<GitHubReleaseAssetInfo>();
        }

        private sealed class GitHubReleaseAssetInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }

            [JsonProperty("download_count")]
            public long DownloadCount { get; set; }
        }

        public AnikiCommunityVisualPacksFullscreenController(
            global::AnikiHelper.AnikiHelper plugin,
            IPlayniteAPI api,
            string pluginUserDataPath,
            ILogger logger)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.logger = logger;
            this.pluginUserDataPath = pluginUserDataPath ?? string.Empty;

            var communityRoot = AnikiPackStorage.GetAreaRoot(this.pluginUserDataPath, "CommunityPacks");
            hubStatePath = Path.Combine(communityRoot, "hub-state.json");
            downloadStatsCachePath = Path.Combine(communityRoot, "download-stats.json");

            var seenState = LoadSeenState();
            seenPackIdentityStateInitialized = seenState != null && seenState.Version >= 2;

            if (seenState?.SeenPackKeys != null)
            {
                foreach (var key in seenState.SeenPackKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        seenPackKeys.Add(key.Trim());
                    }
                }
            }

            legacyNewSinceUtc = GetLegacyNewSinceUtc(seenState);

            InitializeTabs();
            UpdateHeaderTexts();
            InitializeFooterShortcuts();

            Control = LoadView();
            Control.DataContext = viewModel;
            Control.Loaded += OnLoaded;
            Control.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick), true);
            Control.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), true);
            Control.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), true);
        }

        private static UserControl LoadView()
        {
            var pluginAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceUri = new Uri(
                $"pack://application:,,,/{pluginAssemblyName};component/Views/AnikiCommunityVisualPacksFullscreenView.xaml",
                UriKind.Absolute);

            var resource = Application.GetResourceStream(resourceUri);
            if (resource == null || resource.Stream == null)
            {
                throw new InvalidOperationException("AnikiCommunityVisualPacksFullscreenView.xaml resource not found.");
            }

            using (var stream = resource.Stream)
            {
                return (UserControl)XamlReader.Load(stream);
            }
        }

        public void PrepareForOpen(string requestedPackType)
        {
            if (disposed)
            {
                return;
            }

            try
            {
                packType = CommunityPackService.NormalizePackType(requestedPackType);
            }
            catch
            {
                packType = "complete";
            }

            CloseDetails(restoreFocus: false);
            UpdateTabs();
            ApplyCurrentView();

            if (loadedOnce)
            {
                _ = RefreshAsync();
            }
        }

        public void PrepareForOpen()
        {
            PrepareForOpen(packType);
        }

        private CommunityPackService GetService(string type)
        {
            var normalized = CommunityPackService.NormalizePackType(type);
            CommunityPackService service;
            if (services.TryGetValue(normalized, out service) && service != null)
            {
                return service;
            }

            service = new CommunityPackService(plugin, api, pluginUserDataPath, logger, normalized);
            services[normalized] = service;
            return service;
        }

        private void InitializeFooterShortcuts()
        {
            viewModel.FooterPrimaryActionText = Loc("CommunityPack_ViewDetails", "Details");
            viewModel.BackCommand = new RelayCommand(HandleBack);
            viewModel.PreviousCategoryCommand = new RelayCommand(() =>
            {
                if (!viewModel.IsDetailsOpen)
                {
                    CycleCategory(-1);
                }
            });
            viewModel.NextCategoryCommand = new RelayCommand(() =>
            {
                if (!viewModel.IsDetailsOpen)
                {
                    CycleCategory(1);
                }
            });
            viewModel.SortShortcutCommand = new RelayCommand(() =>
            {
                if (!viewModel.IsDetailsOpen)
                {
                    CycleSortShortcut();
                }
            });
        }

        private void InitializeTabs()
        {
            viewModel.Tabs.Clear();
            viewModel.Tabs.Add(new CommunityPackTabItem
            {
                PackType = "complete",
                DisplayName = Loc("CommunityHub_TabComplete", "Complete")
            });
            viewModel.Tabs.Add(new CommunityPackTabItem
            {
                PackType = "visual",
                DisplayName = Loc("CommunityHub_TabVisual", "Visual")
            });
            viewModel.Tabs.Add(new CommunityPackTabItem
            {
                PackType = "login",
                DisplayName = Loc("CommunityHub_TabLogin", "Login")
            });
            viewModel.Tabs.Add(new CommunityPackTabItem
            {
                PackType = "color",
                DisplayName = Loc("CommunityHub_TabColor", "Colors")
            });
            viewModel.Tabs.Add(new CommunityPackTabItem
            {
                PackType = "sound",
                DisplayName = Loc("CommunityHub_TabSound", "Sounds")
            });

            UpdateTabs();
            UpdateToolbarTexts();
        }

        private void UpdateHeaderTexts()
        {
            viewModel.WindowTitle = Loc("CommunityHub_Title", "Community Packs");
            viewModel.WindowDescription = Loc(
                "CommunityHub_Description",
                "Browse, install and manage Complete, Visual, Login, Color and Sound Packs shared by the Aniki ReMake community.");
            viewModel.DetailsTitle = Loc("CommunityPack_DetailsTitle", "Pack Details");
            viewModel.DetailsReportText = Loc("CommunityPack_Report", "Report Pack");
            viewModel.DetailsBackText = Loc("CommunityPack_Back", "Back");
            UpdateEmptyText();
        }

        private void UpdateTabs()
        {
            foreach (var tab in viewModel.Tabs)
            {
                if (tab == null)
                {
                    continue;
                }

                tab.IsSelected = string.Equals(tab.PackType, packType, StringComparison.OrdinalIgnoreCase);
                tab.Count = allPacks.Count(x => string.Equals(x.PackType, tab.PackType, StringComparison.OrdinalIgnoreCase));
                // The tab star is a one-time discovery indicator only.
                // Do NOT use item.IsNew here: card NEW badges intentionally remain visible
                // for the whole publication day, while tab stars must disappear once the
                // catalog has already been seen by the user.
                tab.NewCount = allPacks.Count(x =>
                    string.Equals(x.PackType, tab.PackType, StringComparison.OrdinalIgnoreCase) &&
                    IsUnseenPackForTabStar(x.Source));
            }

            viewModel.ActiveSectionTitle = GetLocalizedPackTypeName(packType);
        }

        private void UpdateToolbarTexts()
        {
            string sortName;
            switch (sortMode)
            {
                case "updated":
                    sortName = Loc("CommunityHub_SortUpdated", "Recently updated");
                    break;
                case "popular":
                    sortName = Loc("CommunityHub_SortPopular", "Most popular");
                    break;
                case "name_asc":
                    sortName = Loc("CommunityHub_SortName", "Name A-Z");
                    break;
                case "name_desc":
                    sortName = Loc("CommunityHub_SortNameDesc", "Name Z-A");
                    break;
                default:
                    sortName = Loc("CommunityHub_SortNewest", "Newest");
                    break;
            }

            viewModel.SortText = string.Format(
                Loc("CommunityHub_SortFormat", "Sort: {0}"),
                sortName);
        }

        private void UpdateEmptyText()
        {
            var typeName = GetLocalizedPackTypeName(packType);
            viewModel.EmptyText = string.Format(
                Loc("CommunityPack_EmptyFormat", "No Community {0} are currently available."),
                typeName);
        }

        private static string GetLocalizedPackTypeName(string type)
        {
            switch (CommunityPackService.NormalizePackType(type))
            {
                case "visual": return Loc("CommunityPack_TypeVisual", "Visual Packs");
                case "color": return Loc("CommunityPack_TypeColor", "Color Packs");
                case "login": return Loc("CommunityPack_TypeLogin", "Login Packs");
                case "sound": return Loc("CommunityPack_TypeSound", "Sound Packs");
                case "complete": return Loc("CommunityPack_TypeComplete", "Complete Packs");
                default: return Loc("CommunityPack_TypeAll", "Packs");
            }
        }

        private CommunityHubState LoadSeenState()
        {
            try
            {
                if (File.Exists(hubStatePath))
                {
                    return JsonConvert.DeserializeObject<CommunityHubState>(File.ReadAllText(hubStatePath));
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Community Hub state could not be loaded.");
            }

            return null;
        }

        private static DateTime GetLegacyNewSinceUtc(CommunityHubState state)
        {
            // Migration safety: the previous timestamp-only system could advance LastSeenUtc
            // even when a stale/cached catalog did not contain a newly published pack.
            // On the first identity-based pass, always keep at least the last 7 days eligible
            // for NEW so a recently missed pack can surface once.
            var recentSafetyCutoff = DateTime.UtcNow.AddDays(-7);

            if (state == null || state.LastSeenUtc <= new DateTime(2000, 1, 1))
            {
                return recentSafetyCutoff;
            }

            var lastSeenUtc = state.LastSeenUtc.Kind == DateTimeKind.Utc
                ? state.LastSeenUtc
                : state.LastSeenUtc.ToUniversalTime();

            return lastSeenUtc < recentSafetyCutoff ? lastSeenUtc : recentSafetyCutoff;
        }

        private static string GetSeenPackKey(CommunityPackCatalogItem source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Id))
            {
                return string.Empty;
            }

            string normalizedType;
            try
            {
                normalizedType = CommunityPackService.NormalizePackType(source.Type);
            }
            catch
            {
                normalizedType = (source.Type ?? string.Empty).Trim().ToLowerInvariant();
            }

            if (string.IsNullOrWhiteSpace(normalizedType))
            {
                return string.Empty;
            }

            return normalizedType + ":" + source.Id.Trim();
        }

        private void SaveSeenState(IEnumerable<CommunityPackCatalogItem> catalogPacks)
        {
            try
            {
                if (catalogPacks != null)
                {
                    foreach (var source in catalogPacks)
                    {
                        var key = GetSeenPackKey(source);
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            seenPackKeys.Add(key);
                        }
                    }
                }

                var directory = Path.GetDirectoryName(hubStatePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(
                    new CommunityHubState
                    {
                        Version = 2,
                        LastSeenUtc = DateTime.UtcNow,
                        SeenPackKeys = seenPackKeys
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    },
                    Formatting.Indented);

                var temporary = hubStatePath + ".tmp";
                File.WriteAllText(temporary, json);
                File.Copy(temporary, hubStatePath, true);
                File.Delete(temporary);

                seenPackIdentityStateInitialized = true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Community Hub state could not be saved.");
            }
        }

        private static DateTime ParseCatalogDate(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(
                    value ?? string.Empty,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }

        private bool IsUnseenPackForTabStar(CommunityPackCatalogItem source)
        {
            if (source == null)
            {
                return false;
            }

            var key = GetSeenPackKey(source);
            if (seenPackIdentityStateInitialized && !string.IsNullOrWhiteSpace(key))
            {
                return !seenPackKeys.Contains(key);
            }

            // Same migration bridge as the previous one-time NEW system, but without
            // the "published today" override used by card badges.
            var published = ParseCatalogDate(source.PublishedAt);
            return published != DateTime.MinValue && published > legacyNewSinceUtc;
        }

        private bool IsNewPack(CommunityPackCatalogItem source)
        {
            if (source == null)
            {
                return false;
            }

            var published = ParseCatalogDate(source.PublishedAt);

            // A pack published today stays NEW for the whole local calendar day, even if
            // the user has already opened/reopened the Community Shop during that day.
            // publishedAt is a catalog publication date (currently yyyy-MM-dd), so compare
            // the calendar date directly instead of shifting it through a time zone.
            if (published != DateTime.MinValue && published.Date == DateTime.Now.Date)
            {
                return true;
            }

            // Keep the existing identity-based behavior too: if the user did not open the
            // Community Shop when a pack was released, that pack is still NEW the first time
            // it appears for that user. Once seen, later version updates keep the same key and
            // are represented by UPDATE instead of becoming NEW again.
            var key = GetSeenPackKey(source);
            if (seenPackIdentityStateInitialized && !string.IsNullOrWhiteSpace(key))
            {
                return !seenPackKeys.Contains(key);
            }

            // First run / migration from the old timestamp-only state. Keep the old date
            // signal as a one-time bridge, with the 7-day safety window above.
            return published != DateTime.MinValue && published > legacyNewSinceUtc;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (disposed || loadedOnce)
            {
                return;
            }

            loadedOnce = true;
            await RefreshAsync();
            FocusInitial();
        }

        public void FocusInitial()
        {
            if (disposed || Control == null)
            {
                return;
            }

            try
            {
                Control.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var target = FindSelectedTabButton()
                                 ?? FindVisualChildren<ButtonBase>(Control)
                                     .FirstOrDefault(button => button != null && button.IsVisible && button.IsEnabled);
                    if (target != null)
                    {
                        FocusButton(target);
                    }
                }));
            }
            catch
            {
            }
        }

        private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (disposed)
            {
                return;
            }

            var button = FindParentButton(e?.NewFocus as DependencyObject);
            UpdateFooterPrimaryAction(button);
        }

        private void UpdateFooterPrimaryAction(ButtonBase button)
        {
            var action = (button as FrameworkElement)?.Tag as string;
            var item = button?.DataContext as CommunityVisualPackViewItem;

            if (string.Equals(action, "CardAction", StringComparison.Ordinal) && item != null)
            {
                viewModel.FooterPrimaryActionText = Loc("CommunityPack_ViewDetails", "Details");
                return;
            }

            if (IsDetailsAction(action))
            {
                viewModel.FooterPrimaryActionText = Loc("LOCButtonPrompt_Select", "Select");
                return;
            }

            viewModel.FooterPrimaryActionText = Loc("LOCButtonPrompt_Select", "Select");
        }

        private void UpdateFooterPrimaryAction(CommunityVisualPackViewItem item)
        {
            if (item == null)
            {
                viewModel.FooterPrimaryActionText = Loc("LOCButtonPrompt_Select", "Select");
                return;
            }

            if (item.IsBusy && !string.IsNullOrWhiteSpace(item.ActionText))
            {
                viewModel.FooterPrimaryActionText = item.ActionText;
            }
            else if (item.UpdateAvailable)
            {
                viewModel.FooterPrimaryActionText = Loc("CommunityPack_Update", "Update");
            }
            else if (item.IsInstalled)
            {
                viewModel.FooterPrimaryActionText = Loc("CommunityPack_Uninstall", "Uninstall");
            }
            else
            {
                viewModel.FooterPrimaryActionText = Loc("CommunityPack_Install", "Install");
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (disposed || e == null || e.IsRepeat)
            {
                return;
            }

            var currentButton = FindParentButton(e.OriginalSource as DependencyObject);
            if (currentButton == null)
            {
                return;
            }

            var action = (currentButton as FrameworkElement)?.Tag as string;

            if (viewModel.IsDetailsOpen)
            {
                if (!IsDetailsAction(action))
                {
                    FocusFirstDetailsButton();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Left || e.Key == Key.Right)
                {
                    var buttons = FindVisualChildren<ButtonBase>(Control)
                        .Where(button =>
                            button != null &&
                            button.IsVisible &&
                            button.IsEnabled &&
                            IsDetailsAction((button as FrameworkElement)?.Tag as string))
                        .Select(button => new
                        {
                            Button = button,
                            Position = GetPosition(button)
                        })
                        .Where(x => x.Position.HasValue)
                        .OrderBy(x => x.Position.Value.X)
                        .Select(x => x.Button)
                        .ToList();

                    if (buttons.Count > 0)
                    {
                        var currentIndex = buttons.IndexOf(currentButton);
                        if (currentIndex < 0)
                        {
                            currentIndex = 0;
                        }

                        var direction = e.Key == Key.Left ? -1 : 1;
                        var nextIndex = (currentIndex + direction + buttons.Count) % buttons.Count;
                        FocusButton(buttons[nextIndex]);
                    }

                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Up || e.Key == Key.Down)
                {
                    e.Handled = true;
                    return;
                }
            }

            // Hub-style top bar: categories move horizontally, Down enters the card grid.
            if (string.Equals(action, "SelectTab", StringComparison.Ordinal))
            {
                if (e.Key == Key.Left || e.Key == Key.Right)
                {
                    var direction = e.Key == Key.Left ? -1 : 1;
                    var target = FindAdjacentTabButton(currentButton, direction);
                    if (target == null && direction > 0)
                    {
                        target = FindNearestTaggedButton(currentButton, "CycleSort");
                    }

                    if (target != null)
                    {
                        FocusButton(target);
                    }

                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Down)
                {
                    var target = FindNearestTopRowCardButton(currentButton);
                    if (target != null)
                    {
                        FocusButton(target);
                    }

                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (IsToolbarAction(action))
            {
                if (e.Key == Key.Left || e.Key == Key.Right)
                {
                    ButtonBase target = null;
                    if (string.Equals(action, "CycleSort", StringComparison.Ordinal) && e.Key == Key.Left)
                    {
                        target = FindLastTabButton();
                    }

                    if (target != null)
                    {
                        FocusButton(target);
                    }

                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Down)
                {
                    var target = FindNearestTopRowCardButton(currentButton);
                    if (target != null)
                    {
                        FocusButton(target);
                    }

                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (!IsCardAction(action))
            {
                return;
            }

            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                var direction = e.Key == Key.Left ? -1 : 1;
                var target = FindAdjacentCardButtonHorizontal(currentButton, direction);
                if (target != null)
                {
                    FocusButton(target);
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                var direction = e.Key == Key.Up ? -1 : 1;
                var target = FindAdjacentCardButton(currentButton, direction);
                if (target != null)
                {
                    FocusButton(target);
                }
                else if (direction < 0)
                {
                    var topBarTarget = FindNearestTopBarButton(currentButton);
                    if (topBarTarget != null)
                    {
                        FocusButton(topBarTarget);
                    }
                }

                e.Handled = true;
            }
        }

        private static bool IsToolbarAction(string action)
        {
            return string.Equals(action, "CycleSort", StringComparison.Ordinal);
        }

        private static bool IsCardAction(string action)
        {
            return string.Equals(action, "CardAction", StringComparison.Ordinal) ||
                   string.Equals(action, "InstallOrUpdate", StringComparison.Ordinal) ||
                   string.Equals(action, "Uninstall", StringComparison.Ordinal);
        }

        private static bool IsDetailsAction(string action)
        {
            return string.Equals(action, "DetailsInstallOrUpdate", StringComparison.Ordinal) ||
                   string.Equals(action, "DetailsUninstall", StringComparison.Ordinal) ||
                   string.Equals(action, "DetailsReport", StringComparison.Ordinal) ||
                   string.Equals(action, "DetailsClose", StringComparison.Ordinal);
        }

        private void FocusButton(ButtonBase button)
        {
            if (button == null)
            {
                return;
            }

            button.Focus();
            Keyboard.Focus(button);
            UpdateFooterPrimaryAction(button);

            var card = FindCardRoot(button);
            if (card == null)
            {
                button.BringIntoView();
                return;
            }

            EnsureCardFullyVisible(card);

            try
            {
                button.Dispatcher.BeginInvoke(new Action(() => EnsureCardFullyVisible(card)));
            }
            catch
            {
            }
        }

        private ButtonBase FindSelectedTabButton()
        {
            return FindVisualChildren<ButtonBase>(Control)
                .FirstOrDefault(button =>
                    button != null &&
                    button.IsVisible &&
                    button.IsEnabled &&
                    string.Equals((button as FrameworkElement)?.Tag as string, "SelectTab", StringComparison.Ordinal) &&
                    (button.DataContext as CommunityPackTabItem)?.IsSelected == true);
        }

        private ButtonBase FindAdjacentTabButton(ButtonBase currentButton, int direction)
        {
            var tabs = FindVisualChildren<ButtonBase>(Control)
                .Where(button =>
                    button != null &&
                    button.IsVisible &&
                    button.IsEnabled &&
                    string.Equals((button as FrameworkElement)?.Tag as string, "SelectTab", StringComparison.Ordinal))
                .Select(button => new { Button = button, Point = GetPosition(button) })
                .Where(x => x.Point.HasValue)
                .OrderBy(x => x.Point.Value.X)
                .Select(x => x.Button)
                .ToList();

            if (tabs.Count == 0)
            {
                return null;
            }

            var index = tabs.IndexOf(currentButton);
            if (index < 0)
            {
                return tabs[0];
            }

            var next = index + direction;
            if (next < 0 || next >= tabs.Count)
            {
                return null;
            }

            return tabs[next];
        }

        private ButtonBase FindLastTabButton()
        {
            return FindVisualChildren<ButtonBase>(Control)
                .Where(button =>
                    button != null &&
                    button.IsVisible &&
                    button.IsEnabled &&
                    string.Equals((button as FrameworkElement)?.Tag as string, "SelectTab", StringComparison.Ordinal))
                .Select(button => new { Button = button, Point = GetPosition(button) })
                .Where(x => x.Point.HasValue)
                .OrderByDescending(x => x.Point.Value.X)
                .Select(x => x.Button)
                .FirstOrDefault();
        }

        private ButtonBase FindNearestTaggedButton(ButtonBase sourceButton, params string[] actions)
        {
            var accepted = new HashSet<string>(actions ?? new string[0], StringComparer.Ordinal);
            var candidates = FindVisualChildren<ButtonBase>(Control)
                .Where(button => button != null && button.IsVisible && button.IsEnabled)
                .Where(button => accepted.Contains((button as FrameworkElement)?.Tag as string ?? string.Empty))
                .Select(button => new
                {
                    Button = button,
                    Point = GetPosition(button)
                })
                .Where(x => x.Point.HasValue)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var sourcePoint = GetPosition(sourceButton);
            if (!sourcePoint.HasValue)
            {
                return candidates[0].Button;
            }

            var sourceCenterX = sourcePoint.Value.X + (sourceButton.ActualWidth / 2.0);
            return candidates
                .OrderBy(x => Math.Abs((x.Point.Value.X + (x.Button.ActualWidth / 2.0)) - sourceCenterX))
                .First()
                .Button;
        }

        private ButtonBase FindNearestTopBarButton(ButtonBase sourceButton)
        {
            var candidates = FindVisualChildren<ButtonBase>(Control)
                .Where(button => button != null && button.IsVisible && button.IsEnabled)
                .Where(button =>
                {
                    var tag = (button as FrameworkElement)?.Tag as string;
                    return string.Equals(tag, "SelectTab", StringComparison.Ordinal) ||
                           string.Equals(tag, "CycleSort", StringComparison.Ordinal);
                })
                .Select(button => new { Button = button, Point = GetPosition(button) })
                .Where(x => x.Point.HasValue)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var sourcePoint = GetPosition(sourceButton);
            var sourceCenterX = sourcePoint.HasValue
                ? sourcePoint.Value.X + (sourceButton.ActualWidth / 2.0)
                : 0.0;

            return candidates
                .OrderBy(x => Math.Abs((x.Point.Value.X + (x.Button.ActualWidth / 2.0)) - sourceCenterX))
                .First()
                .Button;
        }

        private void QueueRestorePackFocus(string packId, string preferredAction = null)
        {
            if (disposed || string.IsNullOrWhiteSpace(packId) || Control == null)
            {
                return;
            }

            if (viewModel.IsDetailsOpen)
            {
                try
                {
                    Control.Dispatcher.BeginInvoke(new Action(FocusFirstDetailsButton));
                }
                catch
                {
                    FocusFirstDetailsButton();
                }
                return;
            }

            try
            {
                Control.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Control.Dispatcher.BeginInvoke(new Action(() => RestorePackFocus(packId, preferredAction)));
                    }
                    catch
                    {
                        RestorePackFocus(packId, preferredAction);
                    }
                }));
            }
            catch
            {
                RestorePackFocus(packId, preferredAction);
            }
        }

        private void RestorePackFocus(string packId, string preferredAction)
        {
            if (disposed || string.IsNullOrWhiteSpace(packId))
            {
                return;
            }

            var candidates = FindVisualChildren<ButtonBase>(Control)
                .Where(button => button != null && button.IsVisible && button.IsEnabled)
                .Where(button =>
                {
                    var item = button.DataContext as CommunityVisualPackViewItem;
                    var action = (button as FrameworkElement)?.Tag as string;
                    return item != null &&
                           string.Equals(item.Id, packId, StringComparison.OrdinalIgnoreCase) &&
                           IsCardAction(action);
                })
                .ToList();

            if (candidates.Count == 0)
            {
                FocusFirstCardOrSelectedTab();
                return;
            }

            ButtonBase target = null;
            if (!string.IsNullOrWhiteSpace(preferredAction))
            {
                target = candidates.FirstOrDefault(button =>
                    string.Equals((button as FrameworkElement)?.Tag as string, preferredAction, StringComparison.Ordinal));
            }

            target = target ?? candidates[0];
            FocusButton(target);
        }

        private bool IsTopRowCardButton(ButtonBase button)
        {
            var card = FindCardRoot(button);
            var cardPoint = GetPosition(card);
            if (card == null || !cardPoint.HasValue)
            {
                return false;
            }

            var cards = GetCardRootsWithPositions();
            if (cards.Count == 0)
            {
                return false;
            }

            var topY = cards.Min(x => x.Point.Y);
            return Math.Abs(cardPoint.Value.Y - topY) <= 24.0;
        }

        private ButtonBase FindNearestTopRowCardButton(ButtonBase sourceButton)
        {
            var cards = GetCardRootsWithPositions();
            if (cards.Count == 0)
            {
                return null;
            }

            var topY = cards.Min(x => x.Point.Y);
            var topRow = cards
                .Where(x => Math.Abs(x.Point.Y - topY) <= 24.0)
                .ToList();

            var sourcePoint = GetPosition(sourceButton);
            var sourceCenterX = sourcePoint.HasValue
                ? sourcePoint.Value.X + (sourceButton.ActualWidth / 2.0)
                : topRow[0].Point.X + (topRow[0].Card.ActualWidth / 2.0);

            var targetCard = topRow
                .OrderBy(x => Math.Abs((x.Point.X + (x.Card.ActualWidth / 2.0)) - sourceCenterX))
                .First()
                .Card;

            return FindNearestActionButtonInCard(targetCard, sourceCenterX);
        }

        private ButtonBase FindAdjacentCardButton(ButtonBase sourceButton, int direction)
        {
            var sourceCard = FindCardRoot(sourceButton);
            var sourceCardPoint = GetPosition(sourceCard);
            if (sourceCard == null || !sourceCardPoint.HasValue)
            {
                return null;
            }

            var cards = GetCardRootsWithPositions()
                .Where(x => !ReferenceEquals(x.Card, sourceCard))
                .ToList();

            if (cards.Count == 0)
            {
                return null;
            }

            const double rowTolerance = 24.0;
            List<CardPosition> row;

            if (direction < 0)
            {
                var above = cards.Where(x => x.Point.Y < sourceCardPoint.Value.Y - rowTolerance).ToList();
                if (above.Count == 0)
                {
                    return null;
                }

                var rowY = above.Max(x => x.Point.Y);
                row = above.Where(x => Math.Abs(x.Point.Y - rowY) <= rowTolerance).ToList();
            }
            else
            {
                var below = cards.Where(x => x.Point.Y > sourceCardPoint.Value.Y + rowTolerance).ToList();
                if (below.Count == 0)
                {
                    return null;
                }

                var rowY = below.Min(x => x.Point.Y);
                row = below.Where(x => Math.Abs(x.Point.Y - rowY) <= rowTolerance).ToList();
            }

            if (row.Count == 0)
            {
                return null;
            }

            var sourceCardCenterX = sourceCardPoint.Value.X + (sourceCard.ActualWidth / 2.0);
            var targetCard = row
                .OrderBy(x => Math.Abs((x.Point.X + (x.Card.ActualWidth / 2.0)) - sourceCardCenterX))
                .First()
                .Card;

            var sourceButtonPoint = GetPosition(sourceButton);
            var preferredButtonX = sourceButtonPoint.HasValue
                ? sourceButtonPoint.Value.X + (sourceButton.ActualWidth / 2.0)
                : sourceCardCenterX;

            return FindNearestActionButtonInCard(targetCard, preferredButtonX);
        }

        private ButtonBase FindAdjacentCardButtonHorizontal(ButtonBase sourceButton, int direction)
        {
            var sourceCard = FindCardRoot(sourceButton);
            var sourceCardPoint = GetPosition(sourceCard);
            if (sourceCard == null || !sourceCardPoint.HasValue)
            {
                return null;
            }

            const double rowTolerance = 28.0;
            var sourceCenterY = sourceCardPoint.Value.Y + (sourceCard.ActualHeight / 2.0);
            var candidates = GetCardRootsWithPositions()
                .Where(x => !ReferenceEquals(x.Card, sourceCard))
                .Where(x => Math.Abs((x.Point.Y + (x.Card.ActualHeight / 2.0)) - sourceCenterY) <= rowTolerance)
                .Where(x => direction < 0
                    ? x.Point.X < sourceCardPoint.Value.X - 8.0
                    : x.Point.X > sourceCardPoint.Value.X + 8.0)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var targetCard = direction < 0
                ? candidates.OrderByDescending(x => x.Point.X).First().Card
                : candidates.OrderBy(x => x.Point.X).First().Card;

            var sourceCenterX = sourceCardPoint.Value.X + (sourceCard.ActualWidth / 2.0);
            return FindNearestActionButtonInCard(targetCard, sourceCenterX);
        }

        private ButtonBase FindNearestActionButtonInCard(FrameworkElement card, double preferredCenterX)
        {
            if (card == null)
            {
                return null;
            }

            var candidates = FindVisualChildren<ButtonBase>(card)
                .Where(button => button != null && button.IsVisible && button.IsEnabled)
                .Where(button => IsCardAction((button as FrameworkElement)?.Tag as string))
                .Select(button => new
                {
                    Button = button,
                    Point = GetPosition(button)
                })
                .Where(x => x.Point.HasValue)
                .OrderBy(x => Math.Abs((x.Point.Value.X + (x.Button.ActualWidth / 2.0)) - preferredCenterX))
                .ToList();

            return candidates.Count > 0 ? candidates[0].Button : null;
        }

        private List<CardPosition> GetCardRootsWithPositions()
        {
            return FindVisualChildren<FrameworkElement>(Control)
                .Where(element => element != null && element.IsVisible)
                .Where(element => string.Equals(element.Tag as string, "CommunityCard", StringComparison.Ordinal))
                .Select(element => new
                {
                    Card = element,
                    Point = GetPosition(element)
                })
                .Where(x => x.Point.HasValue)
                .Select(x => new CardPosition
                {
                    Card = x.Card,
                    Point = x.Point.Value
                })
                .ToList();
        }

        private sealed class CardPosition
        {
            public FrameworkElement Card { get; set; }
            public Point Point { get; set; }
        }

        private void EnsureCardFullyVisible(FrameworkElement card)
        {
            if (card == null)
            {
                return;
            }

            var scrollViewer = Control?.FindName("CatalogScrollViewer") as ScrollViewer;
            if (scrollViewer == null || scrollViewer.ViewportHeight <= 0)
            {
                card.BringIntoView();
                return;
            }

            try
            {
                var point = card.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                var top = point.Y;
                var bottom = top + card.ActualHeight;
                const double padding = 10.0;

                if (top < padding)
                {
                    scrollViewer.ScrollToVerticalOffset(
                        Math.Max(0, scrollViewer.VerticalOffset + top - padding));
                }
                else if (bottom > scrollViewer.ViewportHeight - padding)
                {
                    scrollViewer.ScrollToVerticalOffset(
                        scrollViewer.VerticalOffset + (bottom - (scrollViewer.ViewportHeight - padding)));
                }
            }
            catch
            {
                card.BringIntoView();
            }
        }

        private FrameworkElement FindCardRoot(DependencyObject source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element &&
                    string.Equals(element.Tag as string, "CommunityCard", StringComparison.Ordinal))
                {
                    return element;
                }

                try
                {
                    source = System.Windows.Media.VisualTreeHelper.GetParent(source);
                }
                catch
                {
                    source = LogicalTreeHelper.GetParent(source);
                }
            }

            return null;
        }

        private Point? GetPosition(FrameworkElement element)
        {
            try
            {
                if (element == null || Control == null || !element.IsVisible)
                {
                    return null;
                }

                return element.TransformToAncestor(Control).Transform(new Point(0, 0));
            }
            catch
            {
                return null;
            }
        }

        private static ButtonBase FindParentButton(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ButtonBase button)
                {
                    return button;
                }

                try
                {
                    source = System.Windows.Media.VisualTreeHelper.GetParent(source);
                }
                catch
                {
                    source = LogicalTreeHelper.GetParent(source);
                }
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
            {
                yield break;
            }

            var count = 0;
            try
            {
                count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                yield break;
            }

            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (var descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        private async void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (disposed)
            {
                return;
            }

            var element = e.OriginalSource as DependencyObject;
            FrameworkElement buttonElement = null;
            while (element != null)
            {
                if (element is ButtonBase button)
                {
                    buttonElement = button as FrameworkElement;
                    break;
                }

                DependencyObject parent = null;
                try
                {
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(element);
                }
                catch
                {
                    parent = LogicalTreeHelper.GetParent(element);
                }

                element = parent;
            }

            if (buttonElement == null)
            {
                return;
            }

            var action = buttonElement.Tag as string;
            if (string.IsNullOrWhiteSpace(action))
            {
                return;
            }

            e.Handled = true;

            switch (action)
            {
                case "Refresh":
                    await RefreshAsync();
                    break;

                case "SelectTab":
                    SelectTab((buttonElement.DataContext as CommunityPackTabItem)?.PackType);
                    break;

                case "CycleSort":
                    CycleSort();
                    break;

                case "CardAction":
                {
                    var item = buttonElement.DataContext as CommunityVisualPackViewItem;
                    if (item == null || item.IsBusy)
                    {
                        break;
                    }

                    OpenDetails(item);
                    break;
                }

                case "InstallOrUpdate":
                    await InstallOrUpdateAsync(buttonElement.DataContext as CommunityVisualPackViewItem);
                    break;

                case "Uninstall":
                    Uninstall(buttonElement.DataContext as CommunityVisualPackViewItem);
                    break;

                case "DetailsInstallOrUpdate":
                    await InstallOrUpdateAsync(viewModel.SelectedPack);
                    FocusFirstDetailsButton();
                    break;

                case "DetailsUninstall":
                    Uninstall(viewModel.SelectedPack);
                    FocusFirstDetailsButton();
                    break;

                case "DetailsReport":
                    ReportPack(viewModel.SelectedPack);
                    FocusFirstDetailsButton();
                    break;

                case "DetailsClose":
                    CloseDetails(restoreFocus: true);
                    break;
            }
        }

        public bool HandleBackFromWindow()
        {
            if (!viewModel.IsDetailsOpen)
            {
                return false;
            }

            CloseDetails(restoreFocus: true);
            return true;
        }

        private void HandleBack()
        {
            if (viewModel.IsDetailsOpen)
            {
                CloseDetails(restoreFocus: true);
                return;
            }

            try
            {
                FullscreenSettingsView.CommunityVisualPacksBackCommand?.Execute(null);
            }
            catch
            {
            }
        }

        private void OpenDetails(CommunityVisualPackViewItem item)
        {
            if (disposed || item == null)
            {
                return;
            }

            viewModel.SelectedPack = item;
            viewModel.IsDetailsOpen = true;
            viewModel.FooterPrimaryActionText = Loc("LOCButtonPrompt_Select", "Select");

            try
            {
                Control?.Dispatcher.BeginInvoke(new Action(FocusFirstDetailsButton));
            }
            catch
            {
                FocusFirstDetailsButton();
            }
        }

        private void CloseDetails(bool restoreFocus)
        {
            var selectedId = viewModel.SelectedPack?.Id;
            viewModel.IsDetailsOpen = false;
            viewModel.SelectedPack = null;
            viewModel.FooterPrimaryActionText = Loc("CommunityPack_ViewDetails", "Details");

            if (restoreFocus && !string.IsNullOrWhiteSpace(selectedId))
            {
                QueueRestorePackFocus(selectedId, "CardAction");
            }
        }

        private void FocusFirstDetailsButton()
        {
            if (disposed || Control == null || !viewModel.IsDetailsOpen)
            {
                return;
            }

            var buttons = FindVisualChildren<ButtonBase>(Control)
                .Where(button =>
                    button != null &&
                    button.IsVisible &&
                    button.IsEnabled &&
                    IsDetailsAction((button as FrameworkElement)?.Tag as string))
                .Select(button => new
                {
                    Button = button,
                    Position = GetPosition(button)
                })
                .Where(x => x.Position.HasValue)
                .OrderBy(x => x.Position.Value.X)
                .Select(x => x.Button)
                .ToList();

            FocusButton(buttons.FirstOrDefault());
        }

        private void ReportPack(CommunityVisualPackViewItem item)
        {
            if (item == null)
            {
                return;
            }

            var confirmText = string.Format(
                Loc(
                    "CommunityPack_ReportConfirm",
                    "Report '{0}'?\n\nReports are only for serious inappropriate content such as pornographic/sexual, racist/hateful, violent/shocking, stolen/misleading, or other seriously inappropriate content.\n\nDo not report low-resolution images, poor quality, personal preferences, missing assets, or other minor issues."),
                item.Name);

            var confirmation = ShowDimmedMessage(
                confirmText,
                viewModel.WindowTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var reportType = GetReportPackType(item.PackType);
                var url = "https://github.com/Mike-Aniki/AnikiCommunityPacks/issues/new"
                    + "?template=report-pack.yml"
                    + "&title=" + Uri.EscapeDataString("[Report] " + item.Name)
                    + "&pack-name=" + Uri.EscapeDataString(item.Name)
                    + "&pack-type=" + Uri.EscapeDataString(reportType);

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Failed to open pack report form.");
                ShowDimmedErrorMessage(
                    Loc("CommunityPack_ReportOpenError", "The Community Pack report form could not be opened:") + Environment.NewLine + ex.Message,
                    viewModel.WindowTitle);
            }
        }

        private static string GetReportPackType(string type)
        {
            switch ((type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "visual":
                    return "Visual Pack";
                case "login":
                    return "Login Pack";
                case "sound":
                    return "Sound Pack";
                case "color":
                    return "Color Pack";
                case "complete":
                    return "Complete Pack";
                default:
                    return type ?? string.Empty;
            }
        }

        private void SelectTab(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            try
            {
                packType = CommunityPackService.NormalizePackType(type);
            }
            catch
            {
                return;
            }

            UpdateTabs();
            ApplyCurrentView();

            try
            {
                (Control?.FindName("CatalogScrollViewer") as ScrollViewer)?.ScrollToTop();
            }
            catch
            {
            }
        }

        private void CycleCategory(int direction)
        {
            var currentIndex = Array.FindIndex(
                HubPackTypes,
                type => string.Equals(type, packType, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var nextIndex = (currentIndex + direction + HubPackTypes.Length) % HubPackTypes.Length;
            SelectTab(HubPackTypes[nextIndex]);

            try
            {
                Control?.Dispatcher.BeginInvoke(new Action(FocusFirstCardOrSelectedTab));
            }
            catch
            {
                FocusFirstCardOrSelectedTab();
            }
        }

        private void CycleSortShortcut()
        {
            var focusedPack = GetFocusedPackItem();
            CycleSort();
            RestoreShortcutFocus(focusedPack);
        }

        private CommunityVisualPackViewItem GetFocusedPackItem()
        {
            var focusedButton = FindParentButton(Keyboard.FocusedElement as DependencyObject);
            return focusedButton?.DataContext as CommunityVisualPackViewItem;
        }

        private void RestoreShortcutFocus(CommunityVisualPackViewItem previousItem)
        {
            if (previousItem != null)
            {
                QueueRestorePackFocus(previousItem.Id, "CardAction");
                return;
            }

            try
            {
                Control?.Dispatcher.BeginInvoke(new Action(FocusFirstCardOrSelectedTab));
            }
            catch
            {
                FocusFirstCardOrSelectedTab();
            }
        }

        private void FocusFirstCardOrSelectedTab()
        {
            if (disposed || Control == null)
            {
                return;
            }

            var firstCard = FindVisualChildren<ButtonBase>(Control)
                .FirstOrDefault(button =>
                    button != null &&
                    button.IsVisible &&
                    button.IsEnabled &&
                    string.Equals((button as FrameworkElement)?.Tag as string, "CardAction", StringComparison.Ordinal));

            FocusButton(firstCard ?? FindSelectedTabButton());
        }

        private void CycleSort()
        {
            switch (sortMode)
            {
                case "newest":
                    sortMode = "updated";
                    break;
                case "updated":
                    sortMode = "popular";
                    break;
                case "popular":
                    sortMode = "name_asc";
                    break;
                case "name_asc":
                    sortMode = "name_desc";
                    break;
                default:
                    sortMode = "newest";
                    break;
            }

            UpdateToolbarTexts();
            ApplyCurrentView();
        }

        private void ApplyCurrentView()
        {
            if (disposed)
            {
                return;
            }

            IEnumerable<CommunityVisualPackViewItem> query = allPacks
                .Where(x => x != null && string.Equals(x.PackType, packType, StringComparison.OrdinalIgnoreCase));


            switch (sortMode)
            {
                case "updated":
                    // catalog.json also sets updatedAt = publishedAt for packs that have
                    // never actually been updated. Treat an item as "updated" only when
                    // its update date is strictly newer than its original publication date.
                    query = query
                        .OrderByDescending(x =>
                            x.UpdatedDate != DateTime.MinValue &&
                            (x.PublishedDate == DateTime.MinValue || x.UpdatedDate.Date > x.PublishedDate.Date))
                        .ThenByDescending(x =>
                            x.UpdatedDate != DateTime.MinValue &&
                            (x.PublishedDate == DateTime.MinValue || x.UpdatedDate.Date > x.PublishedDate.Date)
                                ? x.UpdatedDate
                                : DateTime.MinValue)
                        .ThenByDescending(x => x.PublishedDate)
                        .ThenBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "popular":
                    query = query
                        .OrderByDescending(GetDownloadCount)
                        .ThenByDescending(x => x.PublishedDate)
                        .ThenBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "name_asc":
                    query = query
                        .OrderBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "name_desc":
                    query = query
                        .OrderByDescending(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    query = query
                        .OrderByDescending(x => x.PublishedDate)
                        .ThenByDescending(x => x.UpdatedDate)
                        .ThenBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
            }

            var visible = query.ToList();

            // Popular is a lightweight discovery badge, not a public score.
            // It is calculated independently for each pack category:
            // top 20%, maximum 3 packs, with at least 10 total downloads.
            var popularPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in allPacks
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.PackType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var categoryItems = category.ToList();
                if (categoryItems.Count == 0)
                {
                    continue;
                }

                var popularCount = Math.Min(
                    3,
                    Math.Max(1, (int)Math.Ceiling(categoryItems.Count * 0.20)));

                foreach (var popularItem in categoryItems
                    .Where(x => GetDownloadCount(x) >= 10)
                    .OrderByDescending(GetDownloadCount)
                    .ThenByDescending(x => x.PublishedDate)
                    .Take(popularCount))
                {
                    popularPackIds.Add(popularItem.Id);
                }
            }

            foreach (var item in allPacks)
            {
                if (item == null) continue;
                item.IsPopular = popularPackIds.Contains(item.Id);
            }

            viewModel.Packs.Clear();
            foreach (var item in visible)
            {
                viewModel.Packs.Add(item);
            }

            viewModel.IsEmpty = viewModel.Packs.Count == 0;
            viewModel.CountText = string.Format(
                Loc("CommunityPack_CountFormat", "{0} pack(s)"),
                viewModel.Packs.Count);
            UpdateEmptyText();

            if (refreshCts != null && !refreshCts.IsCancellationRequested)
            {
                _ = LoadVisiblePreviewsAsync(refreshCts.Token);
            }
        }

        private async Task RefreshAsync()
        {
            if (disposed)
            {
                return;
            }

            refreshCts?.Cancel();
            refreshCts?.Dispose();
            refreshCts = new CancellationTokenSource();
            var currentRefreshCts = refreshCts;
            var token = currentRefreshCts.Token;

            viewModel.IsLoading = true;
            viewModel.StatusText = Loc("CommunityPack_Loading", "Loading Community Packs...");
            viewModel.CountText = string.Empty;
            viewModel.IsEmpty = false;
            viewModel.Packs.Clear();
            allPacks.Clear();
            UpdateTabs();

            try
            {
                // The catalog is downloaded only once. Per-type services are still used for
                // install/update/uninstall state because each pack library has its own index.
                var catalog = await GetService("complete").GetAllCatalogAsync(token);
                token.ThrowIfCancellationRequested();

                var installedByType = new Dictionary<string, Dictionary<string, CommunityPackInstallation>>(StringComparer.OrdinalIgnoreCase);
                foreach (var type in HubPackTypes)
                {
                    installedByType[type] = GetService(type).GetInstalledPacks();
                }

                foreach (var source in catalog.Packs ?? new List<CommunityPackCatalogItem>())
                {
                    if (source == null)
                    {
                        continue;
                    }

                    string sourceType;
                    try
                    {
                        sourceType = CommunityPackService.NormalizePackType(source.Type);
                    }
                    catch
                    {
                        continue;
                    }

                    var item = new CommunityVisualPackViewItem
                    {
                        Source = source,
                        IsNew = IsNewPack(source)
                    };

                    Dictionary<string, CommunityPackInstallation> installed;
                    installedByType.TryGetValue(sourceType, out installed);
                    ApplyInstalledState(item, installed);
                    allPacks.Add(item);
                }

                // GitHub exposes a download_count for every release asset. Load those
                // counters in parallel with previews so the default Newest view opens
                // immediately; the Popular sort refreshes itself when counts arrive.
                var downloadStatsTask = RefreshDownloadCountsAsync(token);

                UpdateTabs();
                ApplyCurrentView();

                viewModel.StatusText = catalog.UsedCachedCatalog
                    ? Loc("CommunityPack_Cached", "Showing the last cached community catalog.")
                    : string.Empty;

                await LoadVisiblePreviewsAsync(token);
                await downloadStatsTask;

                // Refresh the current view so Popular badges appear as soon as the
                // GitHub download counters are available, even in Newest/Updated/A-Z.
                ApplyCurrentView();
                SaveSeenState(catalog.Packs);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Community Hub refresh failed.");
                allPacks.Clear();
                viewModel.Packs.Clear();
                UpdateTabs();
                viewModel.IsEmpty = true;
                viewModel.CountText = string.Empty;
                viewModel.StatusText = Loc("CommunityPack_LoadError", "The Community Packs catalog could not be loaded.") + " " + ex.Message;
            }
            finally
            {
                // A cancelled older refresh must not hide the loading state of a newer one.
                if (object.ReferenceEquals(refreshCts, currentRefreshCts))
                {
                    viewModel.IsLoading = false;
                }
            }
        }

        private long GetDownloadCount(CommunityVisualPackViewItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
            {
                return 0;
            }

            long count;
            return downloadCountsByPackId.TryGetValue(item.Id, out count) ? count : item.DownloadCount;
        }

        private async Task RefreshDownloadCountsAsync(CancellationToken token)
        {
            // Keep the public GitHub API lightweight: reuse counters for 30 minutes.
            var cached = LoadDownloadStatsCache();
            if (cached != null &&
                cached.FetchedUtc > DateTime.UtcNow.AddMinutes(-30) &&
                cached.Counts != null && cached.Counts.Count > 0 &&
                allPacks.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                    .All(x => cached.Counts.ContainsKey(x.Id)))
            {
                ReplaceDownloadCounts(cached.Counts);
                return;
            }

            try
            {
                var releases = new List<GitHubReleaseInfo>();
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("AnikiHelper-CommunityPacks/1.0");
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    // 100 is GitHub's maximum page size. Continue only if a full page is returned.
                    for (var page = 1; page <= 10; page++)
                    {
                        token.ThrowIfCancellationRequested();
                        var url = "https://api.github.com/repos/Mike-Aniki/AnikiCommunityPacks/releases?per_page=100&page=" + page;
                        using (var response = await client.GetAsync(url, token))
                        {
                            response.EnsureSuccessStatusCode();
                            var json = await response.Content.ReadAsStringAsync();
                            var pageReleases = JsonConvert.DeserializeObject<List<GitHubReleaseInfo>>(json)
                                ?? new List<GitHubReleaseInfo>();
                            releases.AddRange(pageReleases);
                            if (pageReleases.Count < 100)
                            {
                                break;
                            }
                        }
                    }
                }

                var assets = releases
                    .Where(r => r?.Assets != null)
                    .SelectMany(r => r.Assets)
                    .Where(a => a != null)
                    .ToList();

                var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in allPacks)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Id))
                    {
                        continue;
                    }

                    long total = 0;
                    foreach (var asset in assets)
                    {
                        if (AssetBelongsToPack(asset, item))
                        {
                            total += Math.Max(0, asset.DownloadCount);
                        }
                    }
                    counts[item.Id] = total;
                }

                ReplaceDownloadCounts(counts);
                SaveDownloadStatsCache(new CommunityDownloadStatsCache
                {
                    FetchedUtc = DateTime.UtcNow,
                    Counts = counts
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] GitHub download statistics could not be loaded.");
                if (cached?.Counts != null && cached.Counts.Count > 0)
                {
                    ReplaceDownloadCounts(cached.Counts);
                }
            }
        }

        private static bool AssetBelongsToPack(GitHubReleaseAssetInfo asset, CommunityVisualPackViewItem item)
        {
            if (asset == null || item?.Source == null || string.IsNullOrWhiteSpace(item.Id))
            {
                return false;
            }

            // Exact URL handles any unusual legacy asset naming.
            if (!string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
                !string.IsNullOrWhiteSpace(item.Source.DownloadUrl) &&
                string.Equals(asset.BrowserDownloadUrl.Trim(), item.Source.DownloadUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Normal Community Pack assets are <stable-pack-id>-v<version>.zip.
            // Matching the stable id intentionally sums downloads from older versions too.
            var assetName = asset.Name ?? string.Empty;
            return assetName.StartsWith(item.Id + "-v", StringComparison.OrdinalIgnoreCase) &&
                   assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private void ReplaceDownloadCounts(Dictionary<string, long> counts)
        {
            downloadCountsByPackId.Clear();
            if (counts == null)
            {
                return;
            }

            foreach (var pair in counts)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    downloadCountsByPackId[pair.Key] = Math.Max(0, pair.Value);
                }
            }

            foreach (var item in allPacks)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id))
                {
                    continue;
                }

                long count;
                item.DownloadCount = downloadCountsByPackId.TryGetValue(item.Id, out count)
                    ? Math.Max(0, count)
                    : 0;
            }
        }

        private CommunityDownloadStatsCache LoadDownloadStatsCache()
        {
            try
            {
                if (!File.Exists(downloadStatsCachePath))
                {
                    return null;
                }

                var cache = JsonConvert.DeserializeObject<CommunityDownloadStatsCache>(
                    File.ReadAllText(downloadStatsCachePath));
                return cache;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Download statistics cache could not be read.");
                return null;
            }
        }

        private void SaveDownloadStatsCache(CommunityDownloadStatsCache cache)
        {
            try
            {
                var directory = Path.GetDirectoryName(downloadStatsCachePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    downloadStatsCachePath,
                    JsonConvert.SerializeObject(cache, Formatting.Indented));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Download statistics cache could not be saved.");
            }
        }

        private async Task LoadVisiblePreviewsAsync(CancellationToken token)
        {
            var pending = viewModel.Packs
                .Where(x => x != null && x.PreviewImage == null)
                .ToList();

            if (pending.Count == 0)
            {
                return;
            }

            var previewTasks = pending.Select(x => LoadPreviewAsync(x, token)).ToArray();
            await Task.WhenAll(previewTasks);
        }

        private async Task LoadPreviewAsync(CommunityVisualPackViewItem item, CancellationToken token)
        {
            if (item?.Source == null)
            {
                return;
            }

            try
            {
                var path = await GetService(item.PackType).GetPreviewPathAsync(item.Source, token);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 760;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                if (image.CanFreeze)
                {
                    image.Freeze();
                }

                item.PreviewImage = image;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelper][CommunityPacks][Fullscreen] Preview load failed: " + ex.Message);
            }
        }

        private MessageBoxResult ShowDimmedMessage(
            string text,
            string caption,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            viewModel.IsDialogOpen = true;
            try
            {
                try
                {
                    Control?.UpdateLayout();
                    Control?.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
                }
                catch { }
                return api.Dialogs.ShowMessage(text, caption, buttons, image);
            }
            finally
            {
                viewModel.IsDialogOpen = false;
            }
        }

        private void ShowDimmedErrorMessage(string text, string caption)
        {
            viewModel.IsDialogOpen = true;
            try
            {
                try
                {
                    Control?.UpdateLayout();
                    Control?.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
                }
                catch { }
                api.Dialogs.ShowErrorMessage(text, caption);
            }
            finally
            {
                viewModel.IsDialogOpen = false;
            }
        }

        private async Task InstallOrUpdateAsync(CommunityVisualPackViewItem item)
        {
            if (item == null || item.IsBusy || (!item.UpdateAvailable && item.IsInstalled))
            {
                return;
            }

            var service = GetService(item.PackType);
            var wasUpdate = item.UpdateAvailable;

            if (!wasUpdate && !item.IsInstalled)
            {
                var confirmText = string.Format(
                    Loc("CommunityPack_InstallConfirm", "Do you want to install '{0}'?"),
                    item.Name);
                var confirmation = ShowDimmedMessage(
                    confirmText,
                    viewModel.WindowTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    QueueRestorePackFocus(item.Id, "CardAction");
                    return;
                }
            }

            SetAllBusy(true);
            item.ActionText = wasUpdate
                ? Loc("CommunityPack_Updating", "Updating...")
                : Loc("CommunityPack_Installing", "Installing...");
            UpdateFooterPrimaryAction(item);
            viewModel.StatusText = string.Format(
                wasUpdate
                    ? Loc("CommunityPack_UpdatingStatus", "Updating {0}...")
                    : Loc("CommunityPack_InstallingStatus", "Installing {0}..."),
                item.Name);

            try
            {
                var result = await service.InstallOrUpdateAsync(item.Source, CancellationToken.None);
                RefreshInstalledStates();

                viewModel.StatusText = string.Format(
                    wasUpdate
                        ? Loc("CommunityPack_UpdateSuccess", "'{0}' was updated to version {1}.")
                        : Loc("CommunityPack_InstallSuccess", "'{0}' was installed. You can now select it in Aniki ReMake settings."),
                    result.PackName,
                    result.Version);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Install/update failed for " + item.Id + ".");
                viewModel.StatusText = Loc("CommunityPack_InstallError", "The Community Pack could not be installed:") + " " + ex.Message;
                ShowDimmedErrorMessage(
                    Loc("CommunityPack_InstallError", "The Community Pack could not be installed:") + Environment.NewLine + ex.Message,
                    viewModel.WindowTitle);
            }
            finally
            {
                SetAllBusy(false);
                RefreshInstalledStates();
                QueueRestorePackFocus(item.Id, "CardAction");
            }
        }

        private void Uninstall(CommunityVisualPackViewItem item)
        {
            if (item == null || item.IsBusy || !item.IsInstalled)
            {
                return;
            }

            var service = GetService(item.PackType);
            var deleteIncludedPacks = false;
            if (string.Equals(item.PackType, "complete", StringComparison.OrdinalIgnoreCase))
            {
                var choiceText = string.Format(
                    Loc(
                        "CompletePack_DeleteChoice",
                        "Delete Complete Pack '{0}'?\n\nYES = Delete the Complete Pack and all of its installed Visual, Color, Login and Sound Packs.\n\nNO = Delete the Complete Pack only and keep its included packs installed.\n\nCANCEL = Keep everything."),
                    item.Name);

                var choice = ShowDimmedMessage(
                    choiceText,
                    viewModel.WindowTitle,
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.None)
                {
                    QueueRestorePackFocus(item.Id, "CardAction");
                    return;
                }

                deleteIncludedPacks = choice == MessageBoxResult.Yes;
                if (deleteIncludedPacks)
                {
                    var analysis = service.AnalyzeCompletePackUninstall(item.Id);
                    if (analysis?.HasSharedComponents == true)
                    {
                        var sharedPackNames = string.Join(
                            Environment.NewLine,
                            analysis.SharedWithCompletePackNames.Select(x => "• " + x));
                        var sharedWarning = string.Format(
                            Loc(
                                "CompletePack_DeleteSharedWarning",
                                "Some included packs are also used by these installed Complete Packs:\n\n{0}\n\nDeleting the included packs will remove those installed component copies too. The other Complete Packs remain in your library and can reinstall their components if you apply them again.\n\nContinue?"),
                            sharedPackNames);

                        var sharedConfirmation = ShowDimmedMessage(
                            sharedWarning,
                            viewModel.WindowTitle,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (sharedConfirmation != MessageBoxResult.Yes)
                        {
                            QueueRestorePackFocus(item.Id, "CardAction");
                            return;
                        }
                    }
                }
            }
            else
            {
                var confirmText = string.Format(
                    Loc("CommunityPack_UninstallConfirm", "Uninstall '{0}' from the local pack library?"),
                    item.Name);

                var confirmation = ShowDimmedMessage(
                    confirmText,
                    viewModel.WindowTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    QueueRestorePackFocus(item.Id, "CardAction");
                    return;
                }
            }

            SetAllBusy(true);
            viewModel.StatusText = string.Format(
                Loc("CommunityPack_UninstallingStatus", "Uninstalling {0}..."),
                item.Name);

            try
            {
                service.Uninstall(item.Id, deleteIncludedPacks);
                RefreshInstalledStates();
                viewModel.StatusText = string.Format(
                    deleteIncludedPacks
                        ? Loc("CompletePack_DeleteSuccessWithComponents", "Complete Pack '{0}' and its included packs were deleted.")
                        : Loc("CommunityPack_UninstallSuccess", "'{0}' was uninstalled."),
                    item.Name);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks][Fullscreen] Uninstall failed for " + item.Id + ".");
                viewModel.StatusText = Loc("CommunityPack_UninstallError", "The Community Pack could not be uninstalled:") + " " + ex.Message;
                ShowDimmedErrorMessage(
                    Loc("CommunityPack_UninstallError", "The Community Pack could not be uninstalled:") + Environment.NewLine + ex.Message,
                    viewModel.WindowTitle);
            }
            finally
            {
                SetAllBusy(false);
                RefreshInstalledStates();
                QueueRestorePackFocus(item.Id, "CardAction");
            }
        }

        private void SetAllBusy(bool busy)
        {
            foreach (var pack in allPacks)
            {
                pack.IsBusy = busy;
            }
        }

        private void RefreshInstalledStates()
        {
            foreach (var type in HubPackTypes)
            {
                Dictionary<string, CommunityPackInstallation> installed;
                try
                {
                    installed = GetService(type).GetInstalledPacks();
                }
                catch
                {
                    installed = new Dictionary<string, CommunityPackInstallation>(StringComparer.OrdinalIgnoreCase);
                }

                foreach (var pack in allPacks.Where(x =>
                    x != null && string.Equals(x.PackType, type, StringComparison.OrdinalIgnoreCase)))
                {
                    ApplyInstalledState(pack, installed);
                }
            }

            UpdateTabs();
            ApplyCurrentView();
        }

        private static void ApplyInstalledState(
            CommunityVisualPackViewItem item,
            Dictionary<string, CommunityPackInstallation> installed)
        {
            if (item?.Source == null)
            {
                return;
            }

            CommunityPackInstallation record;
            if (installed != null && installed.TryGetValue(item.Id, out record) && record != null)
            {
                item.IsInstalled = true;
                item.InstalledVersion = record.Version ?? string.Empty;

                var update = false;
                try
                {
                    update = CommunityVisualPackService.CompareVersions(item.Version, record.Version) > 0;
                }
                catch
                {
                }

                item.UpdateAvailable = update;
                if (update)
                {
                    item.StatusLabel = Loc("CommunityPack_UpdateAvailable", "UPDATE");
                    item.ActionText = Loc("CommunityPack_Update", "Update");
                    item.StatusBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(151, 102, 37));
                }
                else
                {
                    item.StatusLabel = Loc("CommunityPack_Installed", "INSTALLED");
                    item.ActionText = Loc("CommunityPack_Installed", "Installed");
                    item.StatusBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 122, 81));
                }
            }
            else
            {
                item.IsInstalled = false;
                item.InstalledVersion = string.Empty;
                item.UpdateAvailable = false;
                item.StatusLabel = Loc("CommunityPack_Available", "AVAILABLE");
                item.ActionText = Loc("CommunityPack_Install", "Install");
                item.StatusBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 88, 105));
            }
        }

        private static string Loc(string key, string fallback)
        {
            try
            {
                return Application.Current?.TryFindResource(key) as string ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try { refreshCts?.Cancel(); } catch { }
            try { refreshCts?.Dispose(); } catch { }
            refreshCts = null;

            try
            {
                Control.Loaded -= OnLoaded;
                Control.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
                Control.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
                Control.RemoveHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus));
            }
            catch
            {
            }

            foreach (var service in services.Values.ToList())
            {
                try { service?.Dispose(); } catch { }
            }
            services.Clear();
        }
    }
}
