using AnikiHelper.Services;
using AnikiHelper.Services.SplashScreen;
using AnikiHelper.Services.Controller;
using AnikiHelper.Services.InGameOverlay;
using AnikiHelper.Services.AnikiThemeSettings;
using AnikiHelper.Services.UI;
using Microsoft.Win32;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;


namespace AnikiHelper
{
    public class AnikiHelper : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private void DebugLog(string message)
        {
            try
            {
                if (Settings?.EnableDebugLogs == true)
                {
                    logger.Info(message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private readonly SteamGlobalNewsService rssNewsService;
        private readonly EventSoundService eventSoundService;
        private readonly AnikiWindowManager anikiWindowManager;
        private readonly InGameOverlayService inGameOverlayService;
        private readonly AnikiThemeSettingsService anikiThemeSettingsService;
        private readonly NavigationFixService horizontalFocusFixService;

        private readonly bool isFullscreenMode;

        // Video 
        private bool startupVideoSequenceRunning;
        private static readonly TimeSpan StartupVideoDuration = TimeSpan.FromSeconds(7);
        private static readonly TimeSpan StartupVideoFailSafeTimeout = TimeSpan.FromSeconds(30);
        private const string StartupVideoFileName = "Startup.mp4";

        private bool shutdownVideoSequenceRunning;
        private static readonly TimeSpan ShutdownVideoDuration = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShutdownVideoFailSafeTimeout = TimeSpan.FromSeconds(30);

        private const string ShutdownThemeFolderName = "Aniki_ReMake_bb8728bd-ac83-4324-88b1-ee5c586527d1";
        private const string ShutdownVideoFolderName = "Startup Video";
        private const string ShutdownVideoFileName = "Shutdown.mp4";

        private SplashScreenRuntimeService splashScreenRuntimeService;
        private const int GameLaunchSplashMinimumDurationMs = 2400;
        private const int GameLaunchSplashMaxWaitAfterGameStartedMs = 6000;
        private const int GameLaunchSplashMaximumMinimumDurationMs = 600000;
        private const string CustomSplashTagName = "[Aniki] Custom Splash";

        private bool uniPlaySongGameStartingPauseHeld;
        private Guid? uniPlaySongGameStartingPauseGameId;

        private readonly System.Threading.SemaphoreSlim steamStoreOpenLock = new System.Threading.SemaphoreSlim(1, 1);
        private DateTime lastSteamStoreOpenRequestUtc = DateTime.MinValue;

        public static AnikiHelper Instance { get; private set; }

        private const int GlobalNewsRefreshIntervalHours = 3;

        // Playnite News : 1 scan / 24h
        private const int PlayniteNewsRefreshIntervalHours = 24;

        // Playnite news feed 
        private static readonly string[] PlayniteNewsFeedUrls = new[]
        {
            "https://github.com/Mike-Aniki/AnikiHelper/releases.atom",
            "https://github.com/Mike-Aniki/Steam_Friends_Fullscreen/releases.atom",
            "https://github.com/aHuddini/UniPlaySong/releases.atom",
            "https://github.com/Mike-Aniki/Aniki-ReMake/releases.atom",
            "https://github.com/Lacro59/playnite-screenshotsvisualizer-plugin/releases.atom",
            "https://github.com/Lacro59/playnite-checkdlc-plugin/releases.atom",
            "https://github.com/Lacro59/playnite-backgroundchanger-plugin/releases.atom",
            "https://github.com/Jeshibu/PlayniteExtensions/releases.atom",
            "https://github.com/JosefNemec/Playnite/releases.atom",
            "https://github.com/ashpynov/PlayniteSound/releases.atom",
            "https://github.com/ashpynov/ThemeOptions/releases.atom",
            "https://github.com/justin-delano/PlayniteAchievements/releases.atom",
            "https://github.com/Lacro59/playnite-howlongtobeat-plugin/releases.atom",
            "https://github.com/jonosellier/NowPlaying/releases.atom",
            "https://github.com/HerrKnarz/Playnite-Extensions/releases.atom"
        };




        private readonly Random hubRandom = new Random();
        private bool hubPage3CardsInitialized = false;

        private CancellationTokenSource databaseStatsDebounceCts;
        private readonly object databaseStatsDebounceLock = new object();

        private class SteamPlayerCountCacheEntry
        {
            public bool Success { get; set; }
            public int PlayerCount { get; set; }
            public string Error { get; set; }
            public DateTime CachedAtUtc { get; set; }
        }

        private readonly Dictionary<string, SteamPlayerCountCacheEntry> steamPlayerCountCache =
            new Dictionary<string, SteamPlayerCountCacheEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly object steamPlayerCountCacheLock = new object();

        private static readonly TimeSpan SteamPlayerCountCacheDuration = TimeSpan.FromMinutes(10);

        // === Diagnostics and paths ===
        private string GetDataRoot() => Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());

        private void CleanupLegacyNewsCache()
        {
            try
            {
                var root = GetDataRoot();

                var legacyFiles = new[]
                {
            Path.Combine(root, "CacheNews.json")
        };

                var legacyDirs = new[]
                {
            Path.Combine(root, "NewsImages"),
            Path.Combine(root, "DealsImages")
        };

                foreach (var file in legacyFiles)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"[AnikiHelper] Failed to delete legacy file: {file}");
                    }
                }

                foreach (var dir in legacyDirs)
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"[AnikiHelper] Failed to delete legacy folder: {dir}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] CleanupLegacyNewsCache failed.");
            }
        }

        // News update for Welcome Hub
        private void UpdateLatestNewsRotationFromList(IList<SteamGlobalNewsItem> items)
        {
            try
            {
                latestNewsRotation.Clear();
                latestNewsRotationIndex = 0;

                if (items != null)
                {
                    foreach (var item in items
                        .OrderByDescending(x => x.PublishedUtc)
                        .Take(5))
                    {
                        latestNewsRotation.Add(item);
                    }
                }

                ApplyLatestNewsSnapshot(latestNewsRotation.Count > 0 ? latestNewsRotation[0] : null);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to update LatestNews rotation.");
            }
        }

        private void ApplyLatestNewsSnapshot(SteamGlobalNewsItem item)
        {
            try
            {
                OnUi(() =>
                {
                    if (item == null)
                    {
                        Settings.LatestNewsTitle = string.Empty;
                        Settings.LatestNewsDateString = string.Empty;
                        Settings.LatestNewsSummary = string.Empty;
                        Settings.LatestNewsGameName = string.Empty;
                        Settings.LatestNewsLocalImagePath = string.Empty;

                        Settings.LatestNewsLocalImagePathA = string.Empty;
                        Settings.LatestNewsLocalImagePathB = string.Empty;
                        Settings.LatestNewsShowLayerB = false;

                        latestNewsCrossfadeInitialized = false;
                        return;
                    }

                    // Compat / anciens bindings si jamais tu les utilises ailleurs
                    Settings.LatestNewsTitle = item.Title ?? string.Empty;
                    Settings.LatestNewsDateString = item.DateString ?? string.Empty;
                    Settings.LatestNewsSummary = item.Summary ?? string.Empty;
                    Settings.LatestNewsGameName = item.GameName ?? string.Empty;
                    Settings.LatestNewsLocalImagePath = item.LocalImagePath ?? string.Empty;

                    var title = item.Title ?? string.Empty;
                    var imagePath = item.LocalImagePath ?? string.Empty;

                    // Premier affichage : on remplit seulement A
                    if (!latestNewsCrossfadeInitialized)
                    {
                        Settings.LatestNewsLocalImagePathA = imagePath;

                        Settings.LatestNewsLocalImagePathB = imagePath;

                        Settings.LatestNewsShowLayerB = false;
                        latestNewsCrossfadeInitialized = true;
                        return;
                    }

                    // Si B est visible, on prépare A puis on fade-out B
                    if (Settings.LatestNewsShowLayerB)
                    {
                        Settings.LatestNewsLocalImagePathA = imagePath;

                        Settings.LatestNewsShowLayerB = false;
                    }
                    else
                    {
                        // Si A est visible, on prépare B puis on fade-in B
                        Settings.LatestNewsLocalImagePathB = imagePath;

                        Settings.LatestNewsShowLayerB = true;
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to apply LatestNews snapshot.");
            }
        }

        private void RotateLatestNewsIfNeeded()
        {
            if (latestNewsRotation.Count <= 1)
            {
                return;
            }

            latestNewsRotationIndex++;
            if (latestNewsRotationIndex >= latestNewsRotation.Count)
            {
                latestNewsRotationIndex = 0;
            }

            ApplyLatestNewsSnapshot(latestNewsRotation[latestNewsRotationIndex]);
        }

        public void CloseWelcomeHub()
        {
            if (Settings != null)
            {
                Settings.IsWelcomeHubClosing = false;
                Settings.IsWelcomeHubOpen = false;
            }
        }

        public void StartClosingWelcomeHub()
        {
            if (Settings != null)
            {
                Settings.IsWelcomeHubClosing = true;
            }
        }

        public void FinishClosingWelcomeHub()
        {
            if (Settings != null)
            {
                Settings.IsWelcomeHubClosing = false;
                Settings.IsWelcomeHubOpen = false;
            }
        }

        public void SetWelcomeHubState(bool isOpen)
        {
            if (Settings != null)
            {
                Settings.IsWelcomeHubOpen = isOpen;
            }
        }

        public void OpenWelcomeHub()
        {
            if (Settings != null)
            {
                Settings.IsWelcomeHubClosing = false;
                Settings.IsWelcomeHubOpen = true;
            }
        }

        public void InitializeWelcomeHubState(bool openAtStartup)
        {
            if (openAtStartup)
            {
                OpenWelcomeHub();
            }
            else
            {
                CloseWelcomeHub();
            }

        }



        public void OpenGameDetails(Guid gameId)
        {

            if (gameId == Guid.Empty || PlayniteApi?.MainView == null)
            {
                logger.Warn("[AnikiHelper] OpenGameDetails aborted: empty gameId or MainView null.");
                return;
            }

            try
            {
                var game = PlayniteApi.Database?.Games?.Get(gameId);
                if (game == null)
                {
                    logger.Warn($"[AnikiHelper] Game not found for id={gameId}");
                    return;
                }

                StartClosingWelcomeHub();

                PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        // laisse jouer l'anim du hub
                        await Task.Delay(180);

                        PlayniteApi.MainView.SelectGame(gameId);

                        // laisse le temps à Playnite d'appliquer la nouvelle sélection
                        await Task.Delay(310);

                        if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                        {
                            PlayniteApi.MainView.ToggleFullscreenView();
                        }

                        // petit délai pour laisser la vue détails se poser
                        await Task.Delay(50);

                        FinishClosingWelcomeHub();
                    }
                    catch (Exception innerEx)
                    {
                        logger.Error(innerEx, $"[AnikiHelper] Failed inside UI dispatcher for {gameId}.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper] Failed to open game details for {gameId}.");
            }
        }

        public void HookScreenshotsLazyLoad()
        {
            _ = HookScreenshotsLazyLoadRetryAsync();
        }

        private async Task HookScreenshotsLazyLoadRetryAsync()
        {
            try
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    var hooked = false;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            foreach (Window window in System.Windows.Application.Current.Windows)
                            {
                                var listBox = FindVisualChildByName<ListBox>(window, "ThumbGrid");
                                if (listBox == null)
                                {
                                    continue;
                                }

                                listBox.SelectionChanged -= ScreenshotsThumbGrid_SelectionChanged;
                                listBox.SelectionChanged += ScreenshotsThumbGrid_SelectionChanged;

                                var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
                                if (scrollViewer != null)
                                {
                                    scrollViewer.ScrollChanged -= ScreenshotsThumbGrid_ScrollChanged;
                                    scrollViewer.ScrollChanged += ScreenshotsThumbGrid_ScrollChanged;
                                }

                                hooked = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "[AnikiHelper] Failed to hook screenshot lazy loading.");
                        }
                    }, DispatcherPriority.Background);

                    if (hooked)
                    {
                        return;
                    }

                    await Task.Delay(200);
                }

                logger.Warn("[AnikiHelper] Screenshot lazy loading hook failed: ThumbGrid not found after retries.");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] HookScreenshotsLazyLoadRetryAsync failed.");
            }
        }

        private void ScreenshotsThumbGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var listBox = sender as ListBox;
                if (listBox == null)
                {
                    return;
                }

                var count = listBox.Items.Count;
                var index = listBox.SelectedIndex;

                if (count <= 0 || index < 0)
                {
                    return;
                }

                if (index >= count - 6)
                {
                    Settings?.LoadMoreCurrentGameMediaItems();
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] Screenshot selection lazy load failed.");
            }
        }

        private void ScreenshotsThumbGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                var scrollViewer = sender as ScrollViewer;
                if (scrollViewer == null)
                {
                    return;
                }

                if (scrollViewer.ScrollableHeight <= 0)
                {
                    return;
                }

                var remaining = scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset;

                if (remaining <= 300)
                {
                    Settings?.LoadMoreCurrentGameMediaItems();
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] Screenshot scroll lazy load failed.");
            }
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                var element = child as T;
                if (element != null && element.Name == name)
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

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                var result = child as T;
                if (result != null)
                {
                    return result;
                }

                result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void LoadNewsFromCacheIfNeeded()
        {
            try
            {
                LoadNewsSourceFromCache("A");
                LoadNewsSourceFromCache("B");

                if (Settings.SteamGlobalNewsA != null && Settings.SteamGlobalNewsA.Count > 0)
                {
                    UpdateLatestNewsRotationFromList(Settings.SteamGlobalNewsA.ToList());
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to load news from cache.");
            }
        }

        private void LoadNewsSourceFromCache(string sourceKey)
        {
            var newsRoot = Path.Combine(GetDataRoot(), "News Cache");
            var path = Path.Combine(newsRoot, $"CacheNews_{sourceKey}.json");

            if (!File.Exists(path))
            {
                return;
            }

            var cached = Serialization.FromJsonFile<List<SteamGlobalNewsItem>>(path);
            if (cached == null || cached.Count == 0)
            {
                return;
            }

            var ordered = cached
                .OrderByDescending(n => n.PublishedUtc)
                .Take(30)
                .ToList();

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem> target = null;

                if (sourceKey == "A")
                {
                    if (Settings.SteamGlobalNewsA == null)
                    {
                        Settings.SteamGlobalNewsA =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }
                    else
                    {
                        Settings.SteamGlobalNewsA.Clear();
                    }

                    target = Settings.SteamGlobalNewsA;
                }
                else if (sourceKey == "B")
                {
                    if (Settings.SteamGlobalNewsB == null)
                    {
                        Settings.SteamGlobalNewsB =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }
                    else
                    {
                        Settings.SteamGlobalNewsB.Clear();
                    }

                    target = Settings.SteamGlobalNewsB;
                }

                if (target != null)
                {
                    foreach (var it in ordered)
                    {
                        target.Add(it);
                    }
                }
            });

            if (sourceKey == "A")
            {
                UpdateLatestNewsRotationFromList(ordered);
            }

            SaveSettingsSafe();
        }

        private class SteamUpdateCacheEntry
        {
            public string Title { get; set; }
            public string GameName { get; set; }
            public DateTime LastPublishedUtc { get; set; }

            // Last time Aniki Helper checked Steam for this game.
            public DateTime LastCheckedUtc { get; set; }

            public string Html { get; set; }

            // True once this Steam game has already been checked at least once.
            public bool HasBeenScanned { get; set; }
        }

        private class SteamGameNewsCacheEntry
        {
            public DateTime LastFetchedUtc { get; set; }
            public List<SteamGameNewsItem> Items { get; set; } = new List<SteamGameNewsItem>();
        }

        private class SteamAppIdMappingEntry
        {
            public Guid GameId { get; set; }
            public string GameName { get; set; }
            public string SourceName { get; set; }
            public string SteamAppId { get; set; }
            public string SteamName { get; set; }
            public double Confidence { get; set; }
            public DateTime ResolvedAtUtc { get; set; }
        }

        private static string MakePlayniteNewsKey(SteamGlobalNewsItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var title = item.Title ?? string.Empty;
            var url = item.Url ?? string.Empty;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            return $"{title}|{url}";
        }

        // --- Cache of Steam updates already viewed (SteamID -> latest update title) ---
        private string GetSteamUpdatesCachePath()
            => Path.Combine(GetDataRoot(), "steam_updates_cache.json");

        private Dictionary<string, SteamUpdateCacheEntry> LoadSteamUpdatesCache()
        {
            var path = GetSteamUpdatesCachePath();
            if (!File.Exists(path))
            {
                return new Dictionary<string, SteamUpdateCacheEntry>();
            }

            try
            {
                var json = File.ReadAllText(path);

                var asNew = Serialization.FromJson<Dictionary<string, SteamUpdateCacheEntry>>(json);
                if (asNew != null)
                {
                    return asNew;
                }

                // Old format
                var asOld = Serialization.FromJson<Dictionary<string, string>>(json);
                if (asOld != null)
                {
                    var converted = new Dictionary<string, SteamUpdateCacheEntry>();
                    foreach (var kv in asOld)
                    {
                        converted[kv.Key] = new SteamUpdateCacheEntry
                        {
                            Title = kv.Value,
                            GameName = null,
                            LastPublishedUtc = DateTime.UtcNow,
                            LastCheckedUtc = DateTime.MinValue,
                            HasBeenScanned = true
                        };
                    }
                    return converted;
                }

                return new Dictionary<string, SteamUpdateCacheEntry>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to load Steam updates cache.");
                return new Dictionary<string, SteamUpdateCacheEntry>();
            }
        }


        private void SaveSteamUpdatesCache(Dictionary<string, SteamUpdateCacheEntry> cache)
        {
            try
            {
                var path = GetSteamUpdatesCachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = Serialization.ToJson(cache, true);
                File.WriteAllText(path, json);
            }
            catch
            {

            }
        }

        private void FlushSteamUpdatesCacheIfNeeded()
        {
            Dictionary<string, SteamUpdateCacheEntry> snapshot = null;

            lock (steamUpdatesCacheLock)
            {
                if (!steamUpdatesCacheDirty)
                {
                    return;
                }

                snapshot = new Dictionary<string, SteamUpdateCacheEntry>(steamUpdatesCache);
                steamUpdatesCacheDirty = false;
            }

            // Écriture hors UI thread
            Task.Run(() => SaveSteamUpdatesCache(snapshot));
        }

        private string GetSteamAppIdMappingCachePath()
    => Path.Combine(GetDataRoot(), "steam_appid_mapping_cache.json");

        private Dictionary<Guid, SteamAppIdMappingEntry> LoadSteamAppIdMappingCache()
        {
            var path = GetSteamAppIdMappingCachePath();

            if (!File.Exists(path))
            {
                return new Dictionary<Guid, SteamAppIdMappingEntry>();
            }

            try
            {
                var json = File.ReadAllText(path);
                var cache = Serialization.FromJson<Dictionary<Guid, SteamAppIdMappingEntry>>(json);

                return cache ?? new Dictionary<Guid, SteamAppIdMappingEntry>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to load Steam AppID mapping cache.");
                return new Dictionary<Guid, SteamAppIdMappingEntry>();
            }
        }

        private void SaveSteamAppIdMappingCache(Dictionary<Guid, SteamAppIdMappingEntry> cache)
        {
            try
            {
                var path = GetSteamAppIdMappingCachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var json = Serialization.ToJson(cache, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to save Steam AppID mapping cache.");
            }
        }

        private void FlushSteamAppIdMappingCacheIfNeeded()
        {
            Dictionary<Guid, SteamAppIdMappingEntry> snapshot = null;

            lock (steamAppIdMappingCacheLock)
            {
                if (!steamAppIdMappingCacheDirty)
                {
                    return;
                }

                snapshot = new Dictionary<Guid, SteamAppIdMappingEntry>(steamAppIdMappingCache);
                steamAppIdMappingCacheDirty = false;
            }

            Task.Run(() => SaveSteamAppIdMappingCache(snapshot));
        }

        private string GetSteamGameNewsCachePath()
            => Path.Combine(GetDataRoot(), "steam_game_news_cache.json");

        private Dictionary<string, SteamGameNewsCacheEntry> LoadSteamGameNewsCache()
        {
            var path = GetSteamGameNewsCachePath();

            if (!File.Exists(path))
            {
                return new Dictionary<string, SteamGameNewsCacheEntry>();
            }

            try
            {
                var json = File.ReadAllText(path);
                var cache = Serialization.FromJson<Dictionary<string, SteamGameNewsCacheEntry>>(json);

                return cache ?? new Dictionary<string, SteamGameNewsCacheEntry>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to load Steam game news cache.");
                return new Dictionary<string, SteamGameNewsCacheEntry>();
            }
        }

        private void SaveSteamGameNewsCache(Dictionary<string, SteamGameNewsCacheEntry> cache)
        {
            try
            {
                var path = GetSteamGameNewsCachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var json = Serialization.ToJson(cache, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to save Steam game news cache.");
            }
        }

        private void FlushSteamGameNewsCacheIfNeeded()
        {
            Dictionary<string, SteamGameNewsCacheEntry> snapshot = null;

            lock (steamGameNewsCacheLock)
            {
                if (!steamGameNewsCacheDirty)
                {
                    return;
                }

                snapshot = new Dictionary<string, SteamGameNewsCacheEntry>(steamGameNewsCache);
                steamGameNewsCacheDirty = false;
            }

            Task.Run(() => SaveSteamGameNewsCache(snapshot));
        }

        // Builds a list of the last 10 Steam updates for the theme
        public void RefreshSteamRecentUpdatesFromCache()
        {
            try
            {
                var cache = LoadSteamUpdatesCache();

                var list = cache
                    .Where(kv => kv.Value != null && kv.Value.LastPublishedUtc != DateTime.MinValue)
                    .OrderByDescending(kv => kv.Value.LastPublishedUtc)
                    .Take(10)
                    .Select(kv =>
                    {
                        var steamId = kv.Key;
                        var e = kv.Value;

                        Playnite.SDK.Models.Game game = null;
                        try
                        {
                            game = PlayniteApi.Database.Games
                                .FirstOrDefault(g => GetSteamGameId(g) == steamId);
                        }
                        catch
                        {

                        }

                        // Paths calculated on the fly -> nothing in the JSON
                        var coverPath = GetGameCoverPath(game);
                        var iconPath = GetGameIconPath(game);

                        var dt = e.LastPublishedUtc;
                        if (dt == DateTime.MinValue)
                        {
                            dt = DateTime.UtcNow;
                        }
                        // recent update if less than 48 hours ago
                        var isRecent = (DateTime.UtcNow - dt).TotalDays <= 2.0;

                        return new SteamRecentUpdateItem
                        {
                            GameName = Safe(e.GameName ?? game?.Name),
                            Title = e.Title ?? string.Empty,
                            DateString = dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                            CoverPath = coverPath ?? string.Empty,
                            IconPath = iconPath ?? string.Empty,
                            IsRecent = isRecent,
                            Html = e.Html ?? string.Empty
                        };
                    })
                    .ToList();

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Settings.SteamRecentUpdates.Clear();
                    foreach (var it in list)
                    {
                        Settings.SteamRecentUpdates.Add(it);
                    }
                });


            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to refresh Steam recent updates.");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Settings.SteamRecentUpdates.Clear();
                });
            }
        }




        private string GetGameCoverPath(Playnite.SDK.Models.Game game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            string path = null;

            if (!string.IsNullOrEmpty(game.CoverImage))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.CoverImage);
            }
            else if (!string.IsNullOrEmpty(game.BackgroundImage))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.BackgroundImage);
            }
            else if (!string.IsNullOrEmpty(game.Icon))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.Icon);
            }

            return string.IsNullOrEmpty(path) ? string.Empty : path;
        }

        private string GetGameIconPath(Playnite.SDK.Models.Game game)
        {
            if (game == null || string.IsNullOrEmpty(game.Icon))
            {
                return string.Empty;
            }

            var path = PlayniteApi.Database.GetFullFilePath(game.Icon);
            return string.IsNullOrEmpty(path) ? string.Empty : path;
        }

        // Global toast helper
        private void ShowGlobalToast(string message, string type = null)
        {
            try
            {
                var disp = System.Windows.Application.Current?.Dispatcher;
                if (disp == null) return;

                disp.BeginInvoke(new Action(() =>
                {
                    var s = Settings;

                    s.GlobalToastMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
                    s.GlobalToastType = type ?? string.Empty;
                    s.GlobalToastStamp = Guid.NewGuid().ToString();

                    s.GlobalToastFlip = false;
                    s.GlobalToastFlip = true;

                    AddLastNotificationOnUi(
                        title: GetNotificationTitleFromType(type),
                        message: message,
                        type: type,
                        imagePath: null
                    );

                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] ShowGlobalToast failed.");
            }
        }

        private string GetNotificationTitleFromType(string type)
        {
            switch (type)
            {
                case "playniteNews":
                    return "Playnite news";

                case "steamUpdate":
                    return "Game update";

                case "gameEnded":
                    return "Game session ended";

                default:
                    return "Notification";
            }
        }

        private void AddLastNotificationOnUi(string title, string message, string type = null, string imagePath = null)
        {
            try
            {
                if (Settings == null)
                    return;

                if (Settings.LastNotifications == null)
                    Settings.LastNotifications = new ObservableCollection<AnikiNotificationItem>();

                var item = new AnikiNotificationItem
                {
                    Title = string.IsNullOrWhiteSpace(title) ? "Notification" : title,
                    Message = message ?? string.Empty,
                    Type = type ?? string.Empty,
                    ImagePath = imagePath ?? string.Empty,
                    DateString = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                };

                Settings.LastNotifications.Insert(0, item);

                while (Settings.LastNotifications.Count > 30)
                {
                    Settings.LastNotifications.RemoveAt(Settings.LastNotifications.Count - 1);
                }

            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] AddLastNotificationOnUi failed.");
            }
        }


        // Localisation helper
        private static string Loc(string key, string fallback)
        {
            try
            {
                var txt = System.Windows.Application.Current?.TryFindResource(key) as string;
                return string.IsNullOrWhiteSpace(txt) ? fallback : txt;
            }
            catch
            {
                return fallback;
            }
        }

        private int saveQueued = 0;

        private void SaveSettingsSafe()
        {
            try
            {
                // évite les sauvegardes en rafale
                if (Interlocked.Exchange(ref saveQueued, 1) == 1)
                    return;

                // Sauvegarde sur le thread UI, priorité basse (ne bloque pas la navigation)
                _ = OnUiAsync(() =>
                {
                    try
                    {
                        SavePluginSettings(Settings);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        Interlocked.Exchange(ref saveQueued, 0);
                    }
                }, DispatcherPriority.Background);
            }
            catch
            {
            }
        }




        private void OnUi(Action action)
        {
            try
            {
                var d = Application.Current?.Dispatcher;
                if (d == null || d.CheckAccess())
                {
                    action();
                }
                else
                {
                    d.Invoke(action);
                }
            }
            catch
            {
                // no crash
            }
        }

        private async Task OnUiAsync(Action action, DispatcherPriority priority = DispatcherPriority.Background)
        {
            try
            {
                var d = Application.Current?.Dispatcher;
                if (d == null || d.CheckAccess())
                {
                    action();
                }
                else
                {
                    await d.InvokeAsync(action, priority);
                }
            }
            catch
            {
                // no crash
            }
        }






        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "(Unnamed Game)" : s;

        private string GetSteamGameNewsImagesDir()
        {
            var dir = Path.Combine(GetDataRoot(), "Steam Game News Images");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Md5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }



        private string DownloadSteamGameNewsImage(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return string.Empty;

            try
            {
                var uri = new Uri(imageUrl);

                var ext = Path.GetExtension(uri.LocalPath);

                if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5)
                    ext = ".jpg";

                var fileName = Md5(imageUrl) + ext;
                var path = Path.Combine(GetSteamGameNewsImagesDir(), fileName);

                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
                    return path;
                }

                if (File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "AnikiHelper");
                    client.DownloadFile(imageUrl, path);
                    CleanupSteamGameNewsImageCacheIfNeeded();
                }

                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    try { File.Delete(path); } catch { }
                    return string.Empty;
                }

                return path;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CleanupSteamGameNewsImageCacheIfNeeded()
        {
            try
            {
                var dir = GetSteamGameNewsImagesDir();
                if (!Directory.Exists(dir))
                {
                    return;
                }

                var files = new DirectoryInfo(dir)
                    .GetFiles("*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                if (files.Count <= MaxSteamGameNewsImages)
                {
                    return;
                }

                foreach (var file in files.Skip(MaxSteamGameNewsImages))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static string CleanHtml(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var html = raw;

            try
            {
                // 1) Delete images completely
                html = Regex.Replace(html, "<img[^>]*>", string.Empty, RegexOptions.IgnoreCase);

                // 2) Remplace les liens par des texte (Replace links with text)
                html = Regex.Replace(
                    html,
                    "<a[^>]*>(.*?)</a>",
                    "$1",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                );

                // 3) Supprimer les attributs class et style (Remove class and style attributes)

                html = Regex.Replace(html, "\\sclass=\"[^\"]*\"", string.Empty, RegexOptions.IgnoreCase);
                html = Regex.Replace(html, "\\sstyle=\"[^\"]*\"", string.Empty, RegexOptions.IgnoreCase);

                // 4) Supprime les <p> vides (Remove empty <p> tags)
                html = Regex.Replace(html, "<p>\\s*</p>", string.Empty, RegexOptions.IgnoreCase);

                // 5) Réduire les gros paquets de <br> successifs (Reduce large successive <br> tags)
                html = Regex.Replace(
                    html,
                    "(<br\\s*/?>\\s*){3,}",
                    "<br /><br />",
                    RegexOptions.IgnoreCase
                );

                html = html.Trim();
            }
            catch
            {
                return raw;
            }

            return html;
        }


        // VM + Settings
        public AnikiHelperSettingsViewModel SettingsVM { get; private set; }
        public AnikiHelperSettings Settings => SettingsVM.Settings;

        // GUID du plugin
        public override Guid Id { get; } = Guid.Parse("96a983a3-3f13-4dce-a474-4052b718bb52");


        // Session tracking
        private readonly Dictionary<Guid, DateTime> sessionStartAt = new Dictionary<Guid, DateTime>();
        private readonly Dictionary<Guid, ulong> sessionStartPlaytimeMinutes = new Dictionary<Guid, ulong>(); // Playnite = secondes -> minutes stockées ici


        // Games Update toast "new"
        private readonly HashSet<string> steamUpdateNewThisSession = new HashSet<string>();
        private readonly HashSet<string> steamUpdateToastShownThisSession = new HashSet<string>();

        private readonly SplashScreenService splashScreenService;

        // Games Steam Update (RSS simplified)
        private readonly SteamUpdateLiteService steamUpdateService;
        private readonly DispatcherTimer steamUpdateTimer;
        private Playnite.SDK.Models.Game pendingUpdateGame;
        private readonly DispatcherTimer newsRotationTimer;
        private readonly DispatcherTimer suggestedRotationTimer;
        private readonly DispatcherTimer navigationSettleTimer;
        private readonly SemaphoreSlim newsRefreshGate = new SemaphoreSlim(1, 1);


        private readonly List<SteamGlobalNewsItem> latestNewsRotation = new List<SteamGlobalNewsItem>();
        private int latestNewsRotationIndex = 0;
        private bool latestNewsCrossfadeInitialized = false;

        private readonly List<SuggestedGameSnapshot> suggestedGameRotation = new List<SuggestedGameSnapshot>();
        private int suggestedGameRotationIndex = 0;
        private bool suggestedGameCrossfadeInitialized = false;

        // Steam updates cache (RAM + flush différé)
        private readonly object steamUpdatesCacheLock = new object();
        private Dictionary<string, SteamUpdateCacheEntry> steamUpdatesCache = new Dictionary<string, SteamUpdateCacheEntry>();
        private bool steamUpdatesCacheDirty = false;
        private DispatcherTimer steamUpdatesCacheFlushTimer;

        // Steam game news cache
        private readonly object steamGameNewsCacheLock = new object();
        private Dictionary<string, SteamGameNewsCacheEntry> steamGameNewsCache = new Dictionary<string, SteamGameNewsCacheEntry>();
        private bool steamGameNewsCacheDirty = false;
        private DispatcherTimer steamGameNewsCacheFlushTimer;

        private readonly SemaphoreSlim steamGameNewsGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource steamGameNewsCts;

        private static readonly TimeSpan SteamGameNewsCacheDuration = TimeSpan.FromHours(12);
        private const int MaxSteamGameNewsImages = 300;
        private readonly object steamAppIdMappingCacheLock = new object();
        private Dictionary<Guid, SteamAppIdMappingEntry> steamAppIdMappingCache = new Dictionary<Guid, SteamAppIdMappingEntry>();
        private bool steamAppIdMappingCacheDirty = false;

        private static readonly TimeSpan SteamAppIdMappingCacheDuration = TimeSpan.FromDays(30);

        // Anti-freeze: 1 update à la fois + annulation si navigation rapide
        private readonly SemaphoreSlim steamUpdateGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource steamUpdateCts;

        private bool isInFullscreenDetailsView = false;
        private Guid lastDetailsMediaGameId = Guid.Empty;

        // Used to pause background Steam update scans while the user is navigating.
        private long lastSteamUpdateUserActivityTicks = 0;


        // Steam current players
        private readonly SteamPlayerCountService steamPlayerCountService = new SteamPlayerCountService();
        private SteamStoreService steamStoreService;

        // GUID plugin Steam officiel
        private static readonly Guid SteamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");


        // Format minutes
        private static string FormatHhMmFromMinutes(int minutes)
        {
            if (minutes < 0) minutes = 0;
            var h = minutes / 60;
            var m = minutes % 60;
            return $"{h}h{m:00}";
        }

        private string GetSteamGameId(Playnite.SDK.Models.Game game)
        {
            if (game == null)
                return null;

            try
            {
                // 1) Jeu provenant directement du plugin Steam (Game coming directly from the Steam plugin
                if (game.PluginId == SteamPluginId && !string.IsNullOrWhiteSpace(game.GameId))
                {
                    return game.GameId;
                }

                // 2) Sinon, trouver un lien Steam dans Game.Links (Otherwise, find a Steam link in Game.Links)
                if (game.Links != null)
                {
                    foreach (var link in game.Links)
                    {
                        var url = link?.Url;
                        if (string.IsNullOrWhiteSpace(url))
                            continue;

                        var m = Regex.Match(url, @"store\.steampowered\.com/app/(\d+)", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            return m.Groups[1].Value;
                        }
                    }
                }
            }
            catch
            {

            }

            return null;
        }

        private static string NormalizeSteamSearchName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var s = name.ToLowerInvariant();

            s = s.Replace("™", "")
                 .Replace("®", "")
                 .Replace("©", "")
                 .Replace(":", " ")
                 .Replace("-", " ")
                 .Replace("_", " ")
                 .Replace("’", "'");

            s = Regex.Replace(s, @"\b(game of the year|goty|deluxe|ultimate|complete|definitive|edition|remastered|remaster|soundtrack|ost|demo)\b", "");
            s = Regex.Replace(s, @"[^a-z0-9]+", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }

        private static double SteamNameConfidence(string playniteName, string steamName)
        {
            var a = NormalizeSteamSearchName(playniteName);
            var b = NormalizeSteamSearchName(steamName);

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return 0;

            if (a == b)
                return 1.0;

            if (b.Contains(a) || a.Contains(b))
                return 0.92;

            var aw = new HashSet<string>(a.Split(' '));
            var bw = new HashSet<string>(b.Split(' '));

            if (aw.Count == 0 || bw.Count == 0)
                return 0;

            var common = aw.Intersect(bw).Count();
            var total = aw.Union(bw).Count();

            return total == 0 ? 0 : (double)common / total;
        }

        private static bool IsBadSteamSearchCandidate(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            var n = name.ToLowerInvariant();

            return n.Contains("soundtrack")
                || n.Contains("demo")
                || n.Contains("dlc")
                || n.Contains("artbook")
                || n.Contains("bonus content");
        }

        private async Task<SteamAppIdMappingEntry> SearchSteamAppIdByNameAsync(Game game, CancellationToken ct)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
                return null;

            try
            {
                var query = Uri.EscapeDataString(game.Name);
                var url = $"https://store.steampowered.com/api/storesearch/?term={query}&l=english&cc=US";

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 AnikiHelper");

                    var json = await client.GetStringAsync(url).ConfigureAwait(false);

                    ct.ThrowIfCancellationRequested();

                    var result = Serialization.FromJson<SteamStoreSearchResponse>(json);
                    if (result?.items == null || result.items.Count == 0)
                        return null;

                    var candidates = result.items
                        .Where(x => x != null)
                        .Where(x => string.Equals(x.type, "app", StringComparison.OrdinalIgnoreCase))
                        .Where(x => x.id > 0)
                        .Where(x => !IsBadSteamSearchCandidate(x.name))
                        .Select(x => new
                        {
                            Item = x,
                            Confidence = SteamNameConfidence(game.Name, x.name)
                        })
                        .OrderByDescending(x => x.Confidence)
                        .ToList();

                    if (candidates.Count == 0)
                        return null;

                    var best = candidates[0];

                    // Seuil strict pour éviter les faux matchs.
                    if (best.Confidence < 0.92)
                        return null;

                    // Si deux résultats sont trop proches, on refuse.
                    if (candidates.Count > 1 && candidates[1].Confidence >= 0.90)
                        return null;

                    return new SteamAppIdMappingEntry
                    {
                        GameId = game.Id,
                        GameName = game.Name,
                        SourceName = game.Source?.Name ?? string.Empty,
                        SteamAppId = best.Item.id.ToString(),
                        SteamName = best.Item.name ?? string.Empty,
                        Confidence = best.Confidence,
                        ResolvedAtUtc = DateTime.UtcNow
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"[AnikiHelper] Steam AppID lookup failed for {game?.Name}");
                return null;
            }
        }

        private async Task<string> ResolveSteamGameIdAsync(Game game, CancellationToken ct, bool allowOnlineLookup = true)
        {
            if (game == null)
                return null;

            // 1. Méthode actuelle : Steam officiel ou lien Steam Store.
            var direct = GetSteamGameId(game);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            // 2. Cache mapping par Game.Id Playnite.
            SteamAppIdMappingEntry cached = null;

            lock (steamAppIdMappingCacheLock)
            {
                steamAppIdMappingCache.TryGetValue(game.Id, out cached);
            }

            if (cached != null &&
                !string.IsNullOrWhiteSpace(cached.SteamAppId) &&
                cached.ResolvedAtUtc != DateTime.MinValue &&
                DateTime.UtcNow - cached.ResolvedAtUtc < SteamAppIdMappingCacheDuration)
            {
                return cached.SteamAppId;
            }

            if (!allowOnlineLookup)
                return null;

            // 3. Recherche Steam par nom.
            ct.ThrowIfCancellationRequested();

            var resolved = await SearchSteamAppIdByNameAsync(game, ct).ConfigureAwait(false);

            if (resolved == null || string.IsNullOrWhiteSpace(resolved.SteamAppId))
                return null;

            lock (steamAppIdMappingCacheLock)
            {
                steamAppIdMappingCache[game.Id] = resolved;
                steamAppIdMappingCacheDirty = true;
            }

            FlushSteamAppIdMappingCacheIfNeeded();

            return resolved.SteamAppId;
        }

        // Retourne les N derniers jeux joués qui ont un SteamID valable (returns the last N games played that have a valid SteamID)
        private List<Playnite.SDK.Models.Game> GetRecentSteamGames(int maxGames)
        {
            try
            {
                var games = PlayniteApi.Database.Games
                    .Where(g => g.LastActivity != null)
                    .OrderByDescending(g => g.LastActivity)
                    .Take(Math.Max(1, maxGames))
                    .ToList();

                return games
                    .Where(g => !string.IsNullOrWhiteSpace(GetSteamGameId(g)))
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] GetRecentSteamGames failed.");
                return new List<Playnite.SDK.Models.Game>();
            }
        }


        private void ResetSteamUpdate()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var s = Settings;
                    if (s == null)
                        return;

                    if (string.IsNullOrEmpty(s.SteamUpdateTitle) &&
                        string.IsNullOrEmpty(s.SteamUpdateDate) &&
                        string.IsNullOrEmpty(s.SteamUpdateHtml) &&
                        s.SteamUpdateAvailable == false &&
                        string.IsNullOrEmpty(s.SteamUpdateError) &&
                        s.SteamUpdateIsNew == false)
                    {
                        return;
                    }

                    s.SteamUpdateTitle = string.Empty;
                    s.SteamUpdateDate = string.Empty;
                    s.SteamUpdateHtml = string.Empty;
                    s.SteamUpdateAvailable = false;
                    s.SteamUpdateError = string.Empty;
                    s.SteamUpdateIsNew = false;
                });
            }
            catch
            {
            }
        }

        private void ResetSteamGameNews()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var s = Settings;
                    if (s == null)
                        return;

                    if (s.SteamGameNews.Count == 0 &&
                        s.SteamGameNewsAvailable == false &&
                        string.IsNullOrEmpty(s.SteamGameNewsError))
                    {
                        return;
                    }

                    s.SteamGameNews.Clear();
                    s.SteamGameNewsAvailable = false;
                    s.SteamGameNewsError = string.Empty;
                });
            }
            catch
            {
            }
        }

        private void ResetSteamPlayerCount()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var s = Settings;
                    if (s == null)
                        return;

                    if (string.IsNullOrEmpty(s.SteamCurrentPlayersString) &&
                        s.SteamCurrentPlayersAvailable == false &&
                        string.IsNullOrEmpty(s.SteamCurrentPlayersError))
                    {
                        return;
                    }

                    s.SteamCurrentPlayersString = string.Empty;
                    s.SteamCurrentPlayersAvailable = false;
                    s.SteamCurrentPlayersError = string.Empty;
                });
            }
            catch
            {
            }
        }



        private async void steamUpdateTimer_Tick(object sender, EventArgs e)
        {
            steamUpdateTimer.Stop();

            var g = pendingUpdateGame;
            pendingUpdateGame = null;

            if (g == null)
            {
                ResetSteamUpdate();
                ResetSteamGameNews();
                ResetSteamPlayerCount();
                return;
            }

            steamUpdateCts?.Cancel();
            steamUpdateCts?.Dispose();
            steamUpdateCts = new CancellationTokenSource();
            var ct = steamUpdateCts.Token;

            bool acquired = false;

            try
            {
                await steamUpdateGate.WaitAsync(ct);
                acquired = true;

                if (Settings.SteamUpdatesScanEnabled)
                {
                    await UpdateSteamUpdateForGameAsync(g, ct);
                }
                else
                {
                    ResetSteamUpdate();
                }

            }
            catch (OperationCanceledException)
            {
                // normal
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] steamUpdateTimer_Tick failed.");
            }
            finally
            {
                if (acquired)
                {
                    steamUpdateGate.Release();
                }
            }
        }




        private async Task RefreshGlobalSteamNewsAsync(bool force = false)
        {
            if (!await newsRefreshGate.WaitAsync(0))
            {
                return;
            }

            try
            {
                bool enabled = false;
                string sourceAUrl = string.Empty;
                string sourceBUrl = string.Empty;
                DateTime? lastA = null;
                DateTime? lastB = null;
                string lastCachedAUrl = string.Empty;
                string lastCachedBUrl = string.Empty;

                OnUi(() =>
                {
                    enabled = Settings.NewsScanEnabled;

                    sourceAUrl = Settings.NewsSourceAUrl ?? string.Empty;
                    sourceBUrl = Settings.NewsSourceBUrl ?? string.Empty;

                    lastA = Settings.SteamGlobalNewsALastRefreshUtc;
                    lastB = Settings.SteamGlobalNewsBLastRefreshUtc;

                    lastCachedAUrl = Settings.LastCachedNewsSourceAUrl ?? string.Empty;
                    lastCachedBUrl = Settings.LastCachedNewsSourceBUrl ?? string.Empty;
                });

                if (!enabled)
                {
                    return;
                }

                var itemsA = new List<SteamGlobalNewsItem>();
                var itemsB = new List<SteamGlobalNewsItem>();

                itemsA = await rssNewsService.GetNewsForSourceAsync(
                    "A",
                    sourceAUrl,
                    lastA,
                    lastCachedAUrl,
                    force).ConfigureAwait(false) ?? new List<SteamGlobalNewsItem>();

                itemsB = await rssNewsService.GetNewsForSourceAsync(
                    "B",
                    sourceBUrl,
                    lastB,
                    lastCachedBUrl,
                    force).ConfigureAwait(false) ?? new List<SteamGlobalNewsItem>();

                bool sameA = false;
                bool sameB = false;

                OnUi(() =>
                {
                    sameA = Settings.SteamGlobalNewsA != null &&
                            Settings.SteamGlobalNewsA.Count == itemsA.Count &&
                            Settings.SteamGlobalNewsA
                                .Select(x => $"{x.Title}|{x.DateString}|{x.Url}")
                                .SequenceEqual(itemsA.Select(x => $"{x.Title}|{x.DateString}|{x.Url}"));

                    sameB = Settings.SteamGlobalNewsB != null &&
                            Settings.SteamGlobalNewsB.Count == itemsB.Count &&
                            Settings.SteamGlobalNewsB
                                .Select(x => $"{x.Title}|{x.DateString}|{x.Url}")
                                .SequenceEqual(itemsB.Select(x => $"{x.Title}|{x.DateString}|{x.Url}"));
                });

                if (!sameA || !sameB)
                {
                    OnUi(() =>
                    {
                        if (Settings.SteamGlobalNewsA == null)
                        {
                            Settings.SteamGlobalNewsA =
                                new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                        }

                        if (Settings.SteamGlobalNewsB == null)
                        {
                            Settings.SteamGlobalNewsB =
                                new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                        }

                        if (!sameA && itemsA.Count > 0)
                        {
                            Settings.SteamGlobalNewsA.Clear();
                            foreach (var it in itemsA)
                            {
                                Settings.SteamGlobalNewsA.Add(it);
                            }
                        }

                        if (!sameB && itemsB.Count > 0)
                        {
                            Settings.SteamGlobalNewsB.Clear();
                            foreach (var it in itemsB)
                            {
                                Settings.SteamGlobalNewsB.Add(it);
                            }
                        }
                    });

                    SaveSettingsSafe();
                }

                if (itemsA != null && itemsA.Count > 0)
                {
                    UpdateLatestNewsRotationFromList(itemsA);
                }
                else if (Settings.SteamGlobalNewsA != null && Settings.SteamGlobalNewsA.Count > 0)
                {
                    UpdateLatestNewsRotationFromList(Settings.SteamGlobalNewsA.ToList());
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[NewsScan] ERROR");

                OnUi(() =>
                {
                    if (Settings.SteamGlobalNewsA == null)
                    {
                        Settings.SteamGlobalNewsA =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }

                    Settings.SteamGlobalNewsA.Clear();
                    Settings.SteamGlobalNewsA.Add(new SteamGlobalNewsItem
                    {
                        GameName = "Error",
                        Title = "Error while loading news source A",
                        DateString = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                        Summary = ex.Message
                    });
                });
            }
            finally
            {
                newsRefreshGate.Release();
            }
        }


        // Playnite News
        private async Task RefreshPlayniteNewsAsync(bool force = false, bool silent = false)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;

                // ⚠️ Lecture Settings sur UI thread (sécurité)
                DateTime? last = null;
                string previousKey = string.Empty;

                OnUi(() =>
                {
                    last = Settings.PlayniteNewsLastRefreshUtc;
                    previousKey = Settings.PlayniteNewsLastKey ?? string.Empty;
                });

                // Cooldown 24h
                if (!force && last.HasValue)
                {
                    var hours = (nowUtc - last.Value).TotalHours;
                    if (hours < PlayniteNewsRefreshIntervalHours)
                    {
                        return;
                    }
                }

                // 1) Fetch feeds OFF UI thread
                var allItems = new List<SteamGlobalNewsItem>();

                foreach (var url in PlayniteNewsFeedUrls)
                {
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    try
                    {
                        var items = await rssNewsService
                            .GetGenericFeedAsync(url)
                            .ConfigureAwait(false);

                        if (items != null && items.Count > 0)
                        {
                            allItems.AddRange(items);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!silent)
                        {
                            logger.Warn(ex, $"[PlayniteNews] Failed to load feed: {url}");
                        }
                    }
                }

                // 2) Keep only the last 10
                var ordered = allItems
                    .OrderByDescending(n => n.PublishedUtc)
                    .Take(10)
                    .ToList();

                // 3) NEW detection
                var topItem = ordered.FirstOrDefault();
                var newKey = topItem != null ? MakePlayniteNewsKey(topItem) : string.Empty;

                bool hasNew =
                    !string.IsNullOrEmpty(newKey) &&
                    !string.Equals(previousKey, newKey, StringComparison.Ordinal);

                // 4) Apply to Settings ON UI thread (no Save inside UI)
                OnUi(() =>
                {
                    if (Settings.PlayniteNews == null)
                    {
                        Settings.PlayniteNews =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }

                    Settings.PlayniteNews.Clear();
                    foreach (var it in ordered)
                    {
                        Settings.PlayniteNews.Add(it);
                    }

                    Settings.PlayniteNewsLastRefreshUtc = nowUtc;
                    Settings.PlayniteNewsHasNew = hasNew;

                    if (hasNew)
                    {
                        Settings.PlayniteNewsLastKey = newKey;
                    }
                });

                // ✅ Save OFF UI thread
                SaveSettingsSafe();

                // 5) Global toast if NEW
                if (hasNew && topItem != null)
                {
                    var title = topItem.Title?.Trim();

                    var msg = string.IsNullOrWhiteSpace(title)
                        ? "New add-on update available"
                        : title;

                    ShowGlobalToast(msg, "playniteNews");

                    // Reset the NEW flag (ON UI) + save
                    OnUi(() => Settings.PlayniteNewsHasNew = false);
                    SaveSettingsSafe();
                }
                else
                {
                    // Reset if no new item but flag is still true
                    bool needReset = false;

                    OnUi(() => needReset = Settings.PlayniteNewsHasNew);

                    if (needReset)
                    {
                        OnUi(() => Settings.PlayniteNewsHasNew = false);
                        SaveSettingsSafe();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[PlayniteNews] RefreshPlayniteNewsAsync failed.");
            }
        }


        private async Task<bool> UpdateSteamUpdateCacheOnlyForGameAsync(Playnite.SDK.Models.Game game, CancellationToken ct)
        {
            var notified = false;

            try
            {
                var steamId = await ResolveSteamGameIdAsync(game, ct, false).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    return false;
                }

                // Appel réseau (annulable)
                ct.ThrowIfCancellationRequested();
                var result = await steamUpdateService.GetLatestUpdateAsync(steamId, ct);
                ct.ThrowIfCancellationRequested();

                if (result == null || string.IsNullOrWhiteSpace(result.Title))
                {
                    return false;
                }

                var cleanedHtml = CleanHtml(result.HtmlBody ?? string.Empty);

                // Date
                var published = result.Published;
                if (published == DateTime.MinValue)
                {
                    published = DateTime.UtcNow;
                }


                // Lire l'entrée depuis le cache RAM
                SteamUpdateCacheEntry cachedEntry;
                lock (steamUpdatesCacheLock)
                {
                    steamUpdatesCache.TryGetValue(steamId, out cachedEntry);
                }

                // First scan: create baseline, no notification
                if (cachedEntry == null || !cachedEntry.HasBeenScanned)
                {
                    lock (steamUpdatesCacheLock)
                    {
                        steamUpdatesCache[steamId] = new SteamUpdateCacheEntry
                        {
                            Title = result.Title,
                            GameName = Safe(game.Name),
                            LastPublishedUtc = published,
                            LastCheckedUtc = DateTime.UtcNow,
                            Html = cleanedHtml,
                            HasBeenScanned = true
                        };
                        steamUpdatesCacheDirty = true;
                    }

                    return false;
                }

                var lastPublished = cachedEntry.LastPublishedUtc;
                var sessionKey = $"{steamId}|{result.Title}";
                bool isRealNew = published > lastPublished;

                if (isRealNew)
                {
                    // Update complet + NEW
                    lock (steamUpdatesCacheLock)
                    {
                        cachedEntry.Title = result.Title;
                        cachedEntry.GameName = Safe(game.Name);
                        cachedEntry.LastPublishedUtc = published;
                        cachedEntry.LastCheckedUtc = DateTime.UtcNow;
                        cachedEntry.Html = cleanedHtml;
                        cachedEntry.HasBeenScanned = true;

                        steamUpdatesCache[steamId] = cachedEntry;
                        steamUpdatesCacheDirty = true;
                    }

                    steamUpdateNewThisSession.Add(sessionKey);

                    if (!steamUpdateToastShownThisSession.Contains(sessionKey))
                    {
                        steamUpdateToastShownThisSession.Add(sessionKey);

                        var msg = string.Format(
                            Loc("LOCSteamUpdateToast", "New update for {0}"),
                            Safe(game.Name));

                        ShowGlobalToast(msg, "steamUpdate");
                        notified = true;
                    }
                }
                else
                {
                    // Pas réellement nouveau -> on complète le cache si besoin
                    bool needsUpdate = false;

                    if (string.IsNullOrWhiteSpace(cachedEntry.Html) && !string.IsNullOrEmpty(cleanedHtml))
                    {
                        cachedEntry.Html = cleanedHtml;
                        needsUpdate = true;
                    }

                    if (!string.IsNullOrWhiteSpace(result.Title) &&
                        !string.Equals(cachedEntry.Title, result.Title, StringComparison.Ordinal))
                    {
                        cachedEntry.Title = result.Title;
                        needsUpdate = true;
                    }

                    if (cachedEntry.LastPublishedUtc == DateTime.MinValue && published != DateTime.MinValue)
                    {
                        cachedEntry.LastPublishedUtc = published;
                        needsUpdate = true;
                    }

                    cachedEntry.LastCheckedUtc = DateTime.UtcNow;
                    needsUpdate = true;

                    if (needsUpdate)
                    {
                        lock (steamUpdatesCacheLock)
                        {
                            steamUpdatesCache[steamId] = cachedEntry;
                            steamUpdatesCacheDirty = true;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal si navigation rapide / annulation
                return false;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] UpdateSteamUpdateCacheOnlyForGameAsync failed.");
            }

            return notified;
        }




        // On startup: check for updates (in the background)
        private async Task CheckSteamUpdatesForRecentGamesAsync(int maxGames = 20)
        {
            CancellationTokenSource cts = null;

            try
            {
                // 1) Check mode + focus
                if (!IsSteamRecentScanAllowed())
                {
                    return;
                }

                // Token local pour cette scan (utile pour Task.Delay + futur cancel si tu veux)
                cts = new CancellationTokenSource();
                var ct = cts.Token;

                // 2) Frequency limit
                var nowUtc = DateTime.UtcNow;

                DateTime? last = null;
                OnUi(() => last = Settings.LastSteamRecentCheckUtc);

                if (last.HasValue && (nowUtc - last.Value).TotalHours < 4)
                {
                    return;
                }

                OnUi(() => Settings.LastSteamRecentCheckUtc = nowUtc);
                SaveSettingsSafe();


                var allRecent = PlayniteApi.Database.Games
                    .Where(g => g.LastActivity != null)
                    .OrderByDescending(g => g.LastActivity)
                    .ToList();

                if (allRecent.Count == 0)
                {
                    return;
                }

                int scanned = 0;

                foreach (var g in allRecent)
                {
                    if (scanned >= maxGames)
                    {
                        break;
                    }

                    // Stop if focus/mode changes
                    if (!IsSteamRecentScanAllowed())
                    {
                        break;
                    }

                    // Pause background scan while the user is navigating.
                    if ((Environment.TickCount - Interlocked.Read(ref lastSteamUpdateUserActivityTicks)) < 3000)
                    {
                        await Task.Delay(1000, ct);
                        continue;
                    }

                    var steamId = await ResolveSteamGameIdAsync(g, ct, false).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(steamId))
                    {
                        continue;
                    }

                    scanned++;

                    // ✅ ct existe maintenant
                    var notified = await UpdateSteamUpdateCacheOnlyForGameAsync(g, ct);

                    // short delay to avoid spamming the API.
                    var delayMs = notified ? 10000 : 500;

                    int remaining = delayMs;
                    const int step = 200;

                    while (remaining > 0)
                    {
                        if (!IsSteamRecentScanAllowed())
                        {
                            return;
                        }

                        ct.ThrowIfCancellationRequested();

                        var chunk = Math.Min(step, remaining);

                        // ✅ delay annulable
                        await Task.Delay(chunk, ct);

                        remaining -= chunk;
                    }
                }

                RefreshSteamRecentUpdatesFromCache();
            }
            catch (OperationCanceledException)
            {
                // normal si cancel un jour
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] CheckSteamUpdatesForRecentGamesAsync failed.");
            }
            finally
            {
                cts?.Dispose();
            }
        }



        private async Task UpdateSteamUpdateForGameAsync(Playnite.SDK.Models.Game game, CancellationToken ct)
        {
            try
            {
                ResetSteamUpdate();

                var steamId = await ResolveSteamGameIdAsync(game, ct, true).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Settings.SteamUpdateError = "No update available (no Steam ID)";
                        Settings.SteamUpdateAvailable = false;
                        Settings.SteamUpdateIsNew = false;
                    });
                    return;
                }

                // 1) First, we try the CACHE (RAM)


                SteamUpdateCacheEntry cachedEntry;
                lock (steamUpdatesCacheLock)
                {
                    steamUpdatesCache.TryGetValue(steamId, out cachedEntry);
                }
                bool hadUsableCache = false;

                if (cachedEntry != null &&
                    (!string.IsNullOrWhiteSpace(cachedEntry.Title) ||
                     !string.IsNullOrWhiteSpace(cachedEntry.Html)))
                {
                    var dtCached = cachedEntry.LastPublishedUtc;
                    if (dtCached == DateTime.MinValue)
                    {
                        dtCached = DateTime.UtcNow;
                    }

                    var cachedHtml = cachedEntry.Html ?? string.Empty;
                    var cachedTitle = string.IsNullOrWhiteSpace(cachedEntry.Title)
                        ? Safe(game.Name)
                        : cachedEntry.Title;

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Settings.SteamUpdateTitle = cachedTitle;
                        Settings.SteamUpdateDate = dtCached.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                        Settings.SteamUpdateHtml = cachedHtml;
                        Settings.SteamUpdateAvailable = true;
                        Settings.SteamUpdateError = string.Empty;
                        Settings.SteamUpdateIsNew = false;
                    });

                    hadUsableCache = true;
                }

                // No usable cache yet.
                // Keep the panel in a neutral/loading state while Steam is checked.
                if (!hadUsableCache)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Settings.SteamUpdateError = string.Empty;
                        Settings.SteamUpdateAvailable = false;
                        Settings.SteamUpdateIsNew = false;
                    });
                }

                // If cache exists and was checked recently, do not call Steam during navigation.
                // If the cache is old, we keep the cached content displayed and refresh Steam in the background.
                if (hadUsableCache)
                {
                    var lastChecked = cachedEntry?.LastCheckedUtc ?? DateTime.MinValue;

                    if (lastChecked != DateTime.MinValue &&
                        (DateTime.UtcNow - lastChecked).TotalHours < 24)
                    {
                        RefreshSteamRecentUpdatesFromCache();
                        return;
                    }
                }


                // 2) Then we try to call Steam to refresh
                ct.ThrowIfCancellationRequested();
                var result = await steamUpdateService.GetLatestUpdateAsync(steamId, ct);
                ct.ThrowIfCancellationRequested();
                if (result == null || string.IsNullOrWhiteSpace(result.Title))
                {
                    // No Steam results
                    if (!hadUsableCache)
                    {
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            Settings.SteamUpdateError = "No update available";
                            Settings.SteamUpdateAvailable = false;
                            Settings.SteamUpdateIsNew = false;
                        });
                    }
                    return;
                }

                var cleanedHtml = CleanHtml(result.HtmlBody ?? string.Empty);

                // Detection of "new update" 
                bool isNew = false;
                var sessionKey = $"{steamId}|{result.Title}";

                string lastTitle;
                lock (steamUpdatesCacheLock)
                {
                    steamUpdatesCache.TryGetValue(steamId, out cachedEntry);
                    lastTitle = cachedEntry?.Title;
                }


                var published = result.Published;
                if (published == DateTime.MinValue)
                {
                    published = DateTime.UtcNow;
                }


                // First scan: create baseline, no notification
                if (cachedEntry == null || !cachedEntry.HasBeenScanned)
                {
                    lock (steamUpdatesCacheLock)
                    {
                        steamUpdatesCache[steamId] = new SteamUpdateCacheEntry
                        {
                            Title = result.Title,
                            GameName = Safe(game.Name),
                            LastPublishedUtc = published,
                            LastCheckedUtc = DateTime.UtcNow,
                            Html = cleanedHtml,
                            HasBeenScanned = true
                        };

                        steamUpdatesCacheDirty = true;
                    }

                    isNew = false;
                    steamUpdateNewThisSession.Remove(sessionKey);
                }
                else
                {

                    var lastPublished = cachedEntry?.LastPublishedUtc ?? DateTime.MinValue;

                    // only if the DATE is more recent than the cache date.
                    bool isRealNew =
                        lastPublished == DateTime.MinValue ||
                        published > lastPublished;

                    if (isRealNew)
                    {
                        // New update -> NEW badge + cache update
                        isNew = true;

                        lock (steamUpdatesCacheLock)
                        {
                            steamUpdatesCache[steamId] = new SteamUpdateCacheEntry
                            {
                                Title = result.Title,
                                GameName = Safe(game.Name),
                                LastPublishedUtc = published,
                                LastCheckedUtc = DateTime.UtcNow,
                                Html = cleanedHtml,
                                HasBeenScanned = true
                            };

                            steamUpdatesCacheDirty = true;
                        }
                        steamUpdateNewThisSession.Add(sessionKey);

                    }
                    else
                    {
                        // Same version (same date) -> not NEW

                        if (string.IsNullOrWhiteSpace(cachedEntry?.Html) ||
                            !string.Equals(cachedEntry.Title, result.Title, StringComparison.Ordinal))
                        {
                            lock (steamUpdatesCacheLock)
                            {
                                cachedEntry.Html = cleanedHtml;
                                cachedEntry.Title = result.Title;
                                cachedEntry.LastPublishedUtc = published;
                                cachedEntry.LastCheckedUtc = DateTime.UtcNow;

                                steamUpdatesCache[steamId] = cachedEntry;
                                steamUpdatesCacheDirty = true;
                            }

                        }

                        else
                        {
                            lock (steamUpdatesCacheLock)
                            {
                                cachedEntry.LastCheckedUtc = DateTime.UtcNow;
                                steamUpdatesCache[steamId] = cachedEntry;
                                steamUpdatesCacheDirty = true;
                            }
                        }

                        if (steamUpdateNewThisSession.Contains(sessionKey))
                        {
                            isNew = true;
                        }
                    }
                }


                // 3) Push the "fresh" version into Settings
                ct.ThrowIfCancellationRequested();
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {

                    Settings.SteamUpdateTitle = result.Title;

                    var dt = published;
                    Settings.SteamUpdateDate = dt == DateTime.MinValue
                        ? string.Empty
                        : dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

                    Settings.SteamUpdateHtml = cleanedHtml;
                    Settings.SteamUpdateAvailable = true;
                    Settings.SteamUpdateError = string.Empty;
                    Settings.SteamUpdateIsNew = isNew;
                });

                // Global toast only if it's a new update
                if (isNew)
                {
                    if (!steamUpdateToastShownThisSession.Contains(sessionKey))
                    {
                        steamUpdateToastShownThisSession.Add(sessionKey);

                        var gameName = Safe(game.Name);

                        var msg = string.Format(
                            Loc("LOCSteamUpdateToast", "New update available for {0}"),
                            gameName);

                        ShowGlobalToast(msg, "steamUpdate");
                    }
                }
                RefreshSteamRecentUpdatesFromCache();
            }
            catch (OperationCanceledException)
            {
                // normal si navigation rapide / annulation
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] UpdateSteamUpdateForGameAsync failed.");

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (!Settings.SteamUpdateAvailable && string.IsNullOrWhiteSpace(Settings.SteamUpdateHtml))
                    {
                        Settings.SteamUpdateError = "Error while loading update";
                        Settings.SteamUpdateAvailable = false;
                        Settings.SteamUpdateIsNew = false;
                    }
                });
            }
        }

        private void PushSteamGameNewsToSettings(List<SteamGameNewsItem> items)
        {
            OnUi(() =>
            {
                Settings.SteamGameNews.Clear();

                foreach (var item in items ?? new List<SteamGameNewsItem>())
                {
                    Settings.SteamGameNews.Add(item);
                }

                Settings.SteamGameNewsAvailable = Settings.SteamGameNews.Count > 0;
                Settings.SteamGameNewsError = Settings.SteamGameNewsAvailable
                    ? string.Empty
                    : "No news available";
            });
        }

        private async Task LoadSteamGameNewsForWindowAsync(Playnite.SDK.Models.Game game, CancellationToken ct)
        {
            try
            {
                OnUi(() =>
                {
                    Settings.SteamGameNewsLoading = true;
                    Settings.SteamGameNewsError = string.Empty;
                });

                await UpdateSteamGameNewsForGameAsync(game, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // normal si fermeture/changement rapide
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] LoadSteamGameNewsForWindowAsync failed.");
            }
            finally
            {
                OnUi(() =>
                {
                    Settings.SteamGameNewsLoading = false;
                });
            }
        }

        private async Task UpdateSteamGameNewsForGameAsync(Playnite.SDK.Models.Game game, CancellationToken ct)
        {
            try
            {
                ResetSteamGameNews();

                var steamId = await ResolveSteamGameIdAsync(game, ct, true).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    OnUi(() =>
                    {
                        Settings.SteamGameNewsError = "No Steam ID";
                        Settings.SteamGameNewsAvailable = false;
                    });

                    return;
                }

                SteamGameNewsCacheEntry cachedEntry = null;

                lock (steamGameNewsCacheLock)
                {
                    steamGameNewsCache.TryGetValue(steamId, out cachedEntry);
                }

                var hasCache = cachedEntry?.Items != null && cachedEntry.Items.Count > 0;

                var cacheHasAllImages =
                    hasCache &&
                    cachedEntry.Items.All(x =>
                        !string.IsNullOrWhiteSpace(x.LocalImagePath) &&
                        File.Exists(x.LocalImagePath) &&
                        new FileInfo(x.LocalImagePath).Length > 0);

                var cacheIsFresh =
                    hasCache &&
                    cacheHasAllImages &&
                    cachedEntry.LastFetchedUtc != DateTime.MinValue &&
                    DateTime.UtcNow - cachedEntry.LastFetchedUtc < SteamGameNewsCacheDuration;

                if (hasCache)
                {
                    PushSteamGameNewsToSettings(cachedEntry.Items);
                }

                if (cacheIsFresh)
                {
                    return;
                }

                ct.ThrowIfCancellationRequested();

                await steamGameNewsGate.WaitAsync(ct);

                try
                {
                    ct.ThrowIfCancellationRequested();

                    var news = await steamUpdateService.GetLatestNewsAsync(steamId, 8, ct);

                    ct.ThrowIfCancellationRequested();

                    if (news == null || news.Count == 0)
                    {
                        if (!hasCache)
                        {
                            OnUi(() =>
                            {
                                Settings.SteamGameNewsError = "No news available";
                                Settings.SteamGameNewsAvailable = false;
                            });
                        }

                        return;
                    }

                    foreach (var item in news)
                    {
                        item.LocalImagePath = await Task.Run(() =>
                        DownloadSteamGameNewsImage(item.ImageUrl), ct).ConfigureAwait(false);
                        item.Html = CleanHtml(item.Html ?? string.Empty);
                    }

                    lock (steamGameNewsCacheLock)
                    {
                        steamGameNewsCache[steamId] = new SteamGameNewsCacheEntry
                        {
                            LastFetchedUtc = DateTime.UtcNow,
                            Items = news
                        };

                        steamGameNewsCacheDirty = true;
                    }

                    PushSteamGameNewsToSettings(news);
                }
                finally
                {
                    steamGameNewsGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // normal si changement rapide ou fermeture de vue
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] UpdateSteamGameNewsForGameAsync failed.");

                OnUi(() =>
                {
                    if (!Settings.SteamGameNewsAvailable)
                    {
                        Settings.SteamGameNewsError = "Error while loading news";
                        Settings.SteamGameNewsAvailable = false;
                    }
                });
            }
        }

        private async Task UpdateSteamPlayerCountForGameAsync(Playnite.SDK.Models.Game game)
        {
            try
            {
                if (!Settings.SteamPlayerCountEnabled)
                {
                    ResetSteamPlayerCount();
                    return;
                }

                ResetSteamPlayerCount();

                var steamId = GetSteamGameId(game);
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Settings.SteamCurrentPlayersError = "No Steam ID";
                        Settings.SteamCurrentPlayersAvailable = false;
                    });
                    return;
                }

                SteamPlayerCountCacheEntry cachedEntry = null;

                lock (steamPlayerCountCacheLock)
                {
                    if (steamPlayerCountCache.TryGetValue(steamId, out var entry) &&
                        DateTime.UtcNow - entry.CachedAtUtc < SteamPlayerCountCacheDuration)
                    {
                        cachedEntry = entry;
                    }
                }

                if (cachedEntry != null)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (!cachedEntry.Success)
                        {
                            Settings.SteamCurrentPlayersError = string.IsNullOrWhiteSpace(cachedEntry.Error)
                                ? "No data"
                                : cachedEntry.Error;
                            Settings.SteamCurrentPlayersAvailable = false;
                            Settings.SteamCurrentPlayersString = string.Empty;
                        }
                        else
                        {
                            Settings.SteamCurrentPlayersError = string.Empty;
                            Settings.SteamCurrentPlayersAvailable = true;
                            Settings.SteamCurrentPlayersString = $"{cachedEntry.PlayerCount:N0}";
                        }
                    });

                    return;
                }

                var result = await steamPlayerCountService.GetCurrentPlayersAsync(steamId);

                lock (steamPlayerCountCacheLock)
                {
                    steamPlayerCountCache[steamId] = new SteamPlayerCountCacheEntry
                    {
                        Success = result.Success,
                        PlayerCount = result.PlayerCount,
                        Error = result.Error,
                        CachedAtUtc = DateTime.UtcNow
                    };
                }

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (!result.Success)
                    {
                        Settings.SteamCurrentPlayersError = string.IsNullOrWhiteSpace(result.Error)
                            ? "No data"
                            : result.Error;
                        Settings.SteamCurrentPlayersAvailable = false;
                        Settings.SteamCurrentPlayersString = string.Empty;
                    }
                    else
                    {
                        Settings.SteamCurrentPlayersError = string.Empty;
                        Settings.SteamCurrentPlayersAvailable = true;
                        Settings.SteamCurrentPlayersString = $"{result.PlayerCount:N0}";
                    }
                });
            }
            catch
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Settings.SteamCurrentPlayersError = "Error while loading players";
                    Settings.SteamCurrentPlayersAvailable = false;
                    Settings.SteamCurrentPlayersString = string.Empty;
                });
            }
        }

        public Task InitializeSteamUpdatesCacheForAllGamesAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                    {
                        var games = PlayniteApi.Database.Games.ToList();
                        var cache = LoadSteamUpdatesCache();

                        // Only Steam games with a SteamID are kept.
                        var steamGames = games
                            .Select(g => new { Game = g, SteamId = GetSteamGameId(g) })
                            .Where(x => !string.IsNullOrWhiteSpace(x.SteamId))
                            .ToList();

                        progress.ProgressMaxValue = steamGames.Count;

                        string baseText =
                            (string)Application.Current?.TryFindResource("SteamInitCache_ProgressText")
                            ?? "Initializing Steam update cache... {0}/{1} Steam games scanned";

                        int index = 0;
                        int updated = 0;

                        foreach (var entry in steamGames)
                        {
                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            index++;
                            progress.CurrentProgressValue = index;
                            progress.Text = string.Format(baseText, index, steamGames.Count);

                            if (cache.TryGetValue(entry.SteamId, out var existing) &&
                                !string.IsNullOrWhiteSpace(existing?.Html))
                            {
                                continue;
                            }


                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            var result = steamUpdateService
                                .GetLatestUpdateAsync(entry.SteamId, progress.CancelToken)
                                .GetAwaiter()
                                .GetResult();


                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                break;
                            }


                            if (result == null || string.IsNullOrWhiteSpace(result.Title))
                            {
                                continue;
                            }

                            var cleanedHtml = CleanHtml(result.HtmlBody);

                            cache[entry.SteamId] = new SteamUpdateCacheEntry
                            {
                                Title = result.Title,
                                GameName = Safe(entry.Game.Name),
                                LastPublishedUtc = result.Published == DateTime.MinValue
                                    ? DateTime.UtcNow
                                    : result.Published.ToUniversalTime(),
                                LastCheckedUtc = DateTime.UtcNow,
                                Html = cleanedHtml,
                                HasBeenScanned = true
                            };



                            updated++;

                            // small throttle to avoid spamming the API
                            Task.Delay(150, progress.CancelToken).GetAwaiter().GetResult();
                        }

                        SaveSteamUpdatesCache(cache);
                        RefreshSteamRecentUpdatesFromCache();
                    },
                    new GlobalProgressOptions(
                        (string)Application.Current?.TryFindResource("SteamInitCache_ProgressTitle")
                        ?? "Initializing Steam update cache")
                    {
                        IsIndeterminate = false,
                        Cancelable = true
                    });
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] InitializeSteamUpdatesCacheForAllGamesAsync failed.");
                    throw;
                }
            });
        }




        // SuccessStory helpers

        // Try to find the "SuccessStory" folder in ExtensionsData
        private string FindSuccessStoryRoot()
        {
            try
            {
                var root = PlayniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;


                var classic = Path.Combine(root, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                if (Directory.Exists(classic)) return classic;

                // fallback
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (dir.EndsWith("SuccessStory", StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
            catch { }
            return null;
        }



        // Helpers for scoring suggested games

        private IEnumerable<string> GetGenreNames(Playnite.SDK.Models.Game g)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (g == null)
            {
                return result;
            }

            // Direct ownership (if available)
            try
            {
                if (g.Genres != null)
                {
                    foreach (var meta in g.Genres)
                    {
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            // Fallback via GenreIds
            try
            {
                if (g.GenreIds != null)
                {
                    foreach (var id in g.GenreIds)
                    {
                        var meta = PlayniteApi.Database.Genres.Get(id);
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private IEnumerable<string> GetTagNames(Playnite.SDK.Models.Game g)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (g == null)
            {
                return result;
            }

            try
            {
                if (g.Tags != null)
                {
                    foreach (var meta in g.Tags)
                    {
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (g.TagIds != null)
                {
                    foreach (var id in g.TagIds)
                    {
                        var meta = PlayniteApi.Database.Tags.Get(id);
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private IEnumerable<string> GetSeriesNames(Playnite.SDK.Models.Game g)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (g == null)
            {
                return result;
            }

            try
            {
                if (g.Series != null)
                {
                    foreach (var meta in g.Series)
                    {
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (g.SeriesIds != null)
                {
                    foreach (var id in g.SeriesIds)
                    {
                        var meta = PlayniteApi.Database.Series.Get(id);
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private IEnumerable<string> GetDeveloperNames(Playnite.SDK.Models.Game g)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (g == null)
            {
                return result;
            }

            try
            {
                if (g.Developers != null)
                {
                    foreach (var meta in g.Developers)
                    {
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (g.DeveloperIds != null)
                {
                    foreach (var id in g.DeveloperIds)
                    {
                        var meta = PlayniteApi.Database.Companies.Get(id);
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private IEnumerable<string> GetPublisherNames(Playnite.SDK.Models.Game g)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (g == null)
            {
                return result;
            }

            try
            {
                if (g.Publishers != null)
                {
                    foreach (var meta in g.Publishers)
                    {
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (g.PublisherIds != null)
                {
                    foreach (var id in g.PublisherIds)
                    {
                        var meta = PlayniteApi.Database.Companies.Get(id);
                        var name = meta?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        // "Game finish" detection 
        private bool IsGameFinished(Playnite.SDK.Models.Game g)
        {
            if (g == null)
            {
                return false;
            }

            string name = null;
            try { name = g.CompletionStatus?.Name; } catch { }

            if (string.IsNullOrWhiteSpace(name) && g.CompletionStatusId != Guid.Empty)
            {
                try
                {
                    var meta = PlayniteApi.Database.CompletionStatuses.Get(g.CompletionStatusId);
                    name = meta?.Name;
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            name = name.ToLowerInvariant();

            return name.Contains("terminé")
                   || name.Contains("fini")
                   || name.Contains("completed")
                   || name.Contains("beaten")
                   || name.Contains("finished");
        }

        //  Helpers pour les suggestions

        // Genres/tags that are too generic
        private static readonly HashSet<string> GenericGenreTagNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
        "Action",
        "Aventure",
        "Action, Aventure",
        "RPG",
        "Jeu de rôle",
        "Indépendant",
        "Indie",
        "Simulation",
        "Stratégie",
        "Casual",
        "Adventure",
        "Action, Adventure",
        "Role-playing game",
        "Independent",
        "Indie",
        "Simulation",
        "Strategy",
        "Casual",
        "Indie game",
        "Jeu indé"
            };

        // Keep only "specific" keywords 
        private static IEnumerable<string> GetSpecificKeywords(IEnumerable<string> names)
        {
            if (names == null)
            {
                yield break;
            }

            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n))
                {
                    continue;
                }

                var trimmed = n.Trim();

                if (GenericGenreTagNames.Contains(trimmed))
                {
                    continue;
                }

                yield return trimmed;
            }
        }

        private string NormalizeTextForGenre(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace("’", "'")
                .Replace("–", "-")
                .Replace("—", "-")
                .Replace("_", " ")
                .Replace("/", " ");
        }

        private string NormalizeProfileGenreKey(string genre)
        {
            var g = NormalizeTextForGenre(genre);

            if (string.IsNullOrWhiteSpace(g))
            {
                return null;
            }

            // Ignored / generic metadata tags
            if (g.Contains("indie") ||
                g.Contains("independant") ||
                g.Contains("casual") ||
                g.Contains("occasionnel") ||
                g.Contains("free to play") ||
                g.Contains("free-to-play") ||
                g.Contains("gratuit") ||
                g.Contains("early access") ||
                g.Contains("acces anticipe") ||
                g.Contains("remake") ||
                g.Contains("remaster") ||
                g.Contains("compilation") ||
                g.Contains("classic") ||
                g.Contains("classique") ||
                g.Contains("open world") ||
                g.Contains("open-world") ||
                g.Contains("monde ouvert") ||
                g.Contains("sci-fi") ||
                g.Contains("science fiction") ||
                g.Contains("crime") ||
                g.Contains("superhero") ||
                g.Contains("super-heros") ||
                g.Contains("anime") ||
                g.Contains("live service") ||
                g.Contains("online"))
            {
                return null;
            }

            // Very specific genres first
            if (g.Contains("soulslike") ||
                g.Contains("souls-like") ||
                g.Contains("soul's like") ||
                g.Contains("souls like") ||
                g.Contains("souls"))
                return "SOULSLIKE";

            if (g.Contains("metroidvania"))
                return "METROIDVANIA";

            if (g.Contains("survival horror") ||
                g.Contains("horreur de survie") ||
                g.Contains("survie horreur"))
                return "SURVIVAL_HORROR";

            if (g.Contains("roguelike") || g.Contains("roguelite"))
                return "ROGUELIKE";

            if (g.Contains("jrpg") ||
                g.Contains("j-rpg") ||
                g.Contains("japanese rpg") ||
                g.Contains("rpg japonais"))
                return "JRPG";

            if (g.Contains("action rpg") ||
                g.Contains("action-rpg") ||
                g.Contains("arpg") ||
                g.Contains("action role-playing") ||
                g.Contains("action role playing"))
                return "ACTION_RPG";

            // RPG
            if (g == "rpg" ||
                g.Contains("role-playing") ||
                g.Contains("role playing") ||
                g.Contains("role-playing game") ||
                g.Contains("jeux de roles") ||
                g.Contains("jeu de role") ||
                g.Contains("juego de rol") ||
                g.Contains("rol") ||
                g.Contains("rollenspiel") ||
                g.Contains("gioco di ruolo") ||
                g.Contains("jogo de interpretacao") ||
                g.Contains("jogo de representacao") ||
                g.Contains("gra fabularna") ||
                g.Contains("fabularna") ||
                g.Contains("roolipeli") ||
                g.Contains("rollspel") ||
                g.Contains("rollespill") ||
                g.Contains("szerepjatek") ||
                g.Contains("joc de rol") ||
                g.Contains("rol yapma") ||
                g.Contains("role-playingova") ||
                g.Contains("ролев") ||
                g.Contains("ролева"))
                return "RPG";

            // Shooter / FPS
            if (g.Contains("fps") ||
                g.Contains("first person shooter") ||
                g.Contains("first-person shooter") ||
                g.Contains("first person") ||
                g.Contains("tir a la premiere personne") ||
                g.Contains("ego shooter") ||
                g.Contains("ego-shooter"))
                return "FPS";

            if (g.Contains("shooter") ||
                g.Contains("third person shooter") ||
                g.Contains("third-person shooter") ||
                g.Contains("third person") ||
                g.Contains("tps") ||
                g.Contains("jeux de tir") ||
                g.Contains("jeu de tir") ||
                g.Contains("tir") ||
                g.Contains("sparatutto") ||
                g.Contains("strzelanka") ||
                g.Contains("schietspel") ||
                g.Contains("ampumapeli") ||
                g.Contains("skjutspel") ||
                g.Contains("skytespill") ||
                g.Contains("lövöldözős") ||
                g.Contains("lovedozos") ||
                g.Contains("shmup") ||
                g.Contains("стрелба") ||
                g.Contains("шутер"))
                return "SHOOTER";

            // Horror
            if (g.Contains("horror") ||
                g.Contains("horreur") ||
                g.Contains("terror") ||
                g.Contains("terreur") ||
                g.Contains("horrorpeli") ||
                g.Contains("ужасы") ||
                g.Contains("хорър"))
                return "HORROR";

            // Platformer
            if (g.Contains("platformer") ||
                g.Contains("platform") ||
                g.Contains("plateforme") ||
                g.Contains("jeu de plateforme") ||
                g.Contains("jeux de plateforme") ||
                g.Contains("plataformas") ||
                g.Contains("platformspel") ||
                g.Contains("tasohyppely") ||
                g.Contains("plattform") ||
                g.Contains("platformow") ||
                g.Contains("platforma") ||
                g.Contains("платформ") ||
                g.Contains("plošinov") ||
                g.Contains("plosinov"))
                return "PLATFORMER";

            // Racing
            if (g.Contains("racing") ||
                g.Contains("driving") ||
                g.Contains("course automobile") ||
                g.Contains("course et pilotage") ||
                g == "course" ||
                g.Contains("carreras") ||
                g.Contains("rennspiel") ||
                g.Contains("corse") ||
                g.Contains("corrida") ||
                g.Contains("corridas") ||
                g.Contains("wyścigi") ||
                g.Contains("wyscigi") ||
                g.Contains("race") ||
                g.Contains("racen") ||
                g.Contains("ajopeli") ||
                g.Contains("racespel") ||
                g.Contains("verseny") ||
                g.Contains("гонки") ||
                g.Contains("състез"))
                return "RACING";

            // Fighting
            if (g.Contains("fighting") ||
                g.Contains("versus fighting") ||
                g.Contains("combat") ||
                g.Contains("sports de combat") ||
                g.Contains("lucha") ||
                g.Contains("kampf") ||
                g.Contains("combattimento") ||
                g.Contains("bijatyka") ||
                g.Contains("vechtspel") ||
                g.Contains("taistelupeli") ||
                g.Contains("kampsport") ||
                g.Contains("harc") ||
                g.Contains("bataie") ||
                g.Contains("bătăie") ||
                g.Contains("dovus") ||
                g.Contains("dövüş") ||
                g.Contains("бой") ||
                g.Contains("боев"))
                return "FIGHTING";

            // Strategy
            if (g.Contains("strategy") ||
                g.Contains("stratégie") ||
                g.Contains("strategie") ||
                g.Contains("estrategia") ||
                g.Contains("strategia") ||
                g.Contains("strategi") ||
                g.Contains("strategiczne") ||
                g.Contains("strategiapeli") ||
                g.Contains("strategie") ||
                g.Contains("tactical") ||
                g.Contains("tactique") ||
                g.Contains("turn based") ||
                g.Contains("turn-based") ||
                g.Contains("tour par tour") ||
                g.Contains("turowa") ||
                g.Contains("taktik") ||
                g.Contains("тактичес") ||
                g.Contains("стратег"))
                return "STRATEGY";

            // Simulation
            if (g.Contains("simulation") ||
                g.Contains("simulator") ||
                g.Contains("simulateur") ||
                g.Contains("simulacion") ||
                g.Contains("simulador") ||
                g.Contains("simulazione") ||
                g.Contains("simulatie") ||
                g.Contains("symulacja") ||
                g.Contains("simulointi") ||
                g.Contains("simulering") ||
                g.Contains("szimulator") ||
                g.Contains("симулятор") ||
                g.Contains("симулац"))
                return "SIMULATION";

            // Stealth
            if (g.Contains("stealth") ||
                g.Contains("infiltration") ||
                g.Contains("furtif") ||
                g.Contains("furtivo") ||
                g.Contains("sigilo") ||
                g.Contains("skradanka") ||
                g.Contains("sluip") ||
                g.Contains("lopakod") ||
                g.Contains("lopakod") ||
                g.Contains("sneak") ||
                g.Contains("скрыт") ||
                g.Contains("стелт"))
                return "STEALTH";

            // MMO
            if (g.Contains("mmo") ||
                g.Contains("mmorpg") ||
                g.Contains("massively multiplayer") ||
                g.Contains("massivement multijoueur") ||
                g.Contains("multijoueur massif") ||
                g.Contains("masowo wieloosob") ||
                g.Contains("massaal multiplayer") ||
                g.Contains("многопольз") ||
                g.Contains("много играчи"))
                return "MMO";

            // Sports
            if (g == "sport" ||
                g == "sports" ||
                g.Contains("soccer") ||
                g.Contains("football") ||
                g.Contains("basketball") ||
                g.Contains("tennis") ||
                g.Contains("sportif") ||
                g.Contains("sportowe") ||
                g.Contains("esportes") ||
                g.Contains("deportes") ||
                g.Contains("sportspel") ||
                g.Contains("urheilu") ||
                g.Contains("спорт"))
                return "SPORTS";

            // Adventure last because it is very generic
            if (g.Contains("adventure") ||
                g.Contains("aventure") ||
                g.Contains("action adventure") ||
                g.Contains("action-adventure") ||
                g.Contains("action et aventure") ||
                g.Contains("action aventure") ||
                g.Contains("aventura") ||
                g.Contains("abenteuer") ||
                g.Contains("avventura") ||
                g.Contains("przygod") ||
                g.Contains("avontuur") ||
                g.Contains("seikkailu") ||
                g.Contains("aventuri") ||
                g.Contains("macera") ||
                g.Contains("приключ") ||
                g.Contains("приключен"))
                return "ADVENTURE";

            return null;
        }

        private double GetProfileGenreRecencyWeight(Game game)
        {
            if (game == null || !game.LastActivity.HasValue)
            {
                return 0.50;
            }

            var days = (DateTime.Now - game.LastActivity.Value).TotalDays;
            var years = Math.Floor(days / 365.0);

            if (years <= 0)
            {
                return 1.00; // année en cours
            }

            var weight = Math.Pow(0.65, years);

            return Math.Max(0.05, weight);
        }

        private string BuildProfileGenreKey(IEnumerable<Game> games)
        {
            if (games == null)
            {
                return "VARIETY";
            }

            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var g in games)
            {
                if (g == null)
                {
                    continue;
                }

                var rawGenres = GetGenreNames(g).ToList();
                if (rawGenres.Count == 0)
                {
                    continue;
                }

                var filteredGenres = GetSpecificKeywords(rawGenres).ToList();
                var genresToUse = filteredGenres.Count > 0 ? filteredGenres : rawGenres;

                var gameMinutes = g.Playtime / 60.0;
                var weightedMinutes = gameMinutes * GetProfileGenreRecencyWeight(g);
                if (gameMinutes <= 0)
                {
                    continue;
                }

                foreach (var genre in genresToUse.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var key = NormalizeProfileGenreKey(genre);
                    
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (!scores.ContainsKey(key))
                    {
                        scores[key] = 0;
                    }

                    scores[key] += weightedMinutes;
                }
            }

            if (scores.Count == 0)
            {
                return "VARIETY";
            }

            var ordered = scores
                .OrderByDescending(x => x.Value)
                .ToList();

            DebugLog("========== PROFILE GENRE SCORES ==========");

            foreach (var s in ordered)
            {
                DebugLog($"[ProfileGenre] {s.Key} = {s.Value:F0} min ({s.Value / 60:F1}h)");
            }

            DebugLog("=========================================");

            var top = ordered[0];
            var secondValue = ordered.Count > 1 ? ordered[1].Value : 0;

            // Pas assez de matière : on évite un faux "genre dominant"
            if (top.Value < 300) // 5h cumulées
            {
                return "VARIETY";
            }

            // Si les 2 premiers genres sont trop proches, on reste générique
            if (secondValue > 0 && top.Value < secondValue * 1.15)
            {
                return "VARIETY";
            }

            return top.Key;
        }

        private void RecalcProfileGenre(IEnumerable<Game> games)
        {
            var genreKey = BuildProfileGenreKey(games);

            Settings.ProfileGenreKey = genreKey;
            Settings.ProfileGenreLabel = GetLocalizedProfileGenreLabel(genreKey);
            Settings.LastProfileGenreScanUtc = DateTime.UtcNow;

            SaveSettingsSafe();
        }

        private string GetLocalizedProfileGenreLabel(string key)
        {
            switch (key)
            {
                case "SOULSLIKE":
                    return Loc("ProfileGenre_Soulslike", "Souls-like fan");

                case "METROIDVANIA":
                    return Loc("ProfileGenre_Metroidvania", "Metroidvania fan");

                case "ACTION_RPG":
                    return Loc("ProfileGenre_ActionRpg", "Action RPG fan");

                case "JRPG":
                    return Loc("ProfileGenre_Jrpg", "JRPG fan");

                case "RPG":
                    return Loc("ProfileGenre_Rpg", "RPG fan");

                case "SURVIVAL_HORROR":
                    return Loc("ProfileGenre_SurvivalHorror", "Survival horror fan");

                case "HORROR":
                    return Loc("ProfileGenre_Horror", "Horror fan");

                case "FPS":
                    return Loc("ProfileGenre_Fps", "FPS fan");

                case "SHOOTER":
                    return Loc("ProfileGenre_Shooter", "Shooter fan");

                case "PLATFORMER":
                    return Loc("ProfileGenre_Platformer", "Platformer fan");

                case "RACING":
                    return Loc("ProfileGenre_Racing", "Racing fan");

                case "FIGHTING":
                    return Loc("ProfileGenre_Fighting", "Fighting fan");

                case "STRATEGY":
                    return Loc("ProfileGenre_Strategy", "Strategy fan");

                case "SIMULATION":
                    return Loc("ProfileGenre_Simulation", "Simulation fan");

                case "STEALTH":
                    return Loc("ProfileGenre_Stealth", "Stealth fan");

                case "ADVENTURE":
                    return Loc("ProfileGenre_Adventure", "Adventure fan");

                case "ROGUELIKE":
                    return Loc("ProfileGenre_Roguelike", "Roguelike fan");

                case "MMO":
                    return Loc("ProfileGenre_Mmo", "MMO fan");

                case "SPORTS":
                    return Loc("ProfileGenre_Sports", "Sports fan");

                case "VARIETY":
                default:
                    return Loc("ProfileGenre_Variety", "Variety gamer");
            }
        }

        private static readonly HashSet<string> IgnoredProfileTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Singleplayer",
            "Single-player",
            "Multiplayer",
            "Multi-player",
            "Co-op",
            "Cooperative",
            "Online Co-Op",
            "Local Co-Op",
            "Local Multiplayer",
            "Online Multiplayer",
            "Full Controller Support",
            "Partial Controller Support",
            "Controller",
            "Steam Achievements",
            "Cloud Saves",
            "Family Sharing",
            "Remote Play on Phone",
            "Remote Play on Tablet",
            "Remote Play on TV",
            "Remote Play Together"
        };

        private static bool IsUsefulProfileTag(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var trimmed = name.Trim();

            // Ignore technical / metadata tags like:
            // [Game Engine] ..., [People] ..., [UPS] ..., [HLTB] ...
            if (trimmed.StartsWith("[", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Ignore structured / technical tags like:
            // icon:, noicon:, composer: Toshiki Aida
            if (trimmed.Contains(":"))
            {
                return false;
            }

            return !IgnoredProfileTags.Contains(trimmed);
        }

        private string BuildWeightedTopValue(IEnumerable<Game> games, Func<Game, IEnumerable<string>> selector, Func<string, bool> validator = null)
        {
            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (games == null)
            {
                return string.Empty;
            }

            foreach (var g in games)
            {
                if (g == null || g.Playtime == 0UL)
                {
                    continue;
                }

                var weight = Math.Max(1.0, g.Playtime / 60.0);

                IEnumerable<string> values;
                try
                {
                    values = selector(g) ?? Enumerable.Empty<string>();
                }
                catch
                {
                    continue;
                }

                foreach (var value in values
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (validator != null && !validator(value))
                    {
                        continue;
                    }

                    if (!scores.ContainsKey(value))
                    {
                        scores[value] = 0;
                    }

                    scores[value] += weight;
                }
            }

            if (scores.Count == 0)
            {
                return string.Empty;
            }

            return scores
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .First()
                .Key;
        }

        private string BuildTopPlatformName(IEnumerable<Game> games)
        {
            return BuildWeightedTopValue(
                games,
                g =>
                {
                    var src = g.Source?.Name;
                    return string.IsNullOrWhiteSpace(src)
                        ? Enumerable.Empty<string>()
                        : new[] { src.Trim() };
                });
        }

        private string BuildTopFranchiseName(IEnumerable<Game> games)
        {
            var playtimeScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var gameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (games == null)
            {
                return string.Empty;
            }

            foreach (var g in games)
            {
                if (g == null || g.Playtime == 0UL)
                {
                    continue;
                }

                var weight = Math.Max(1.0, g.Playtime / 60.0);

                var seriesNames = GetSeriesNames(g)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var series in seriesNames)
                {
                    if (!playtimeScores.ContainsKey(series))
                    {
                        playtimeScores[series] = 0;
                        gameCounts[series] = 0;
                    }

                    playtimeScores[series] += weight;
                    gameCounts[series]++;
                }
            }

            var eligible = playtimeScores
                .Where(x => gameCounts.ContainsKey(x.Key) && gameCounts[x.Key] >= 2)
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .ToList();

            if (eligible.Count == 0)
            {
                return string.Empty;
            }

            return eligible.First().Key;
        }

        private string BuildTopTagName(IEnumerable<Game> games)
        {
            return BuildWeightedTopValue(games, g => GetTagNames(g), IsUsefulProfileTag);
        }

        // Essaie de ranger un jeu dans une "famille" grossière
        private static string DetectFamily(IEnumerable<string> genres, IEnumerable<string> tags)
        {
            var all = new List<string>();
            if (genres != null) all.AddRange(genres);
            if (tags != null) all.AddRange(tags);

            var text = string.Join(" | ", all).ToLowerInvariant();

            if (text.Contains("soulslike") || text.Contains("souls-like"))
                return "souls";

            if (text.Contains("jrpg") || text.Contains("j-rpg") ||
                text.Contains("tour par tour") || text.Contains("turn-based"))
                return "jrpg";

            if (text.Contains("combat") || text.Contains("fighting") ||
                text.Contains("versus") || text.Contains("vs"))
                return "fighting";

            if (text.Contains("anime") || text.Contains("manga"))
                return "anime_fight";

            if (text.Contains("shooter") || text.Contains("fps") || text.Contains("tps") ||
                text.Contains("tir") || text.Contains("gun"))
                return "shooter";

            if (text.Contains("rogue") || text.Contains("roguelite") || text.Contains("roguelike"))
                return "roguelite";

            if (text.Contains("plateforme") || text.Contains("platformer") || text.Contains("metroidvania"))
                return "platformer";

            if (text.Contains("sport") || text.Contains("football") || text.Contains("basket"))
                return "sport";

            if (text.Contains("course") || text.Contains("racing") || text.Contains("voiture"))
                return "racing";

            if (text.Contains("party") || text.Contains("party game") ||
                text.Contains("fête") || text.Contains("multijoueur local") ||
                text.Contains("local co-op") || text.Contains("coop en local"))
                return "party";

            return "generic";
        }

        // Certaines familles ne doivent *jamais* être suggérées entre elles (Certain families should *never* be suggested to each other)
        private static bool AreFamiliesIncompatible(string refFam, string candFam)
        {
            if (string.IsNullOrEmpty(refFam) ||
                string.IsNullOrEmpty(candFam) ||
                string.Equals(refFam, candFam, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            refFam = refFam.ToLowerInvariant();
            candFam = candFam.ToLowerInvariant();

            if ((refFam == "anime_fight" && (candFam == "shooter" || candFam == "party")) ||
                (candFam == "anime_fight" && (refFam == "shooter" || refFam == "party")))
            {
                return true;
            }

            if (refFam == "souls" && (candFam == "sport" || candFam == "racing" || candFam == "party"))
                return true;
            if (candFam == "souls" && (refFam == "sport" || refFam == "racing" || refFam == "party"))
                return true;

            if (refFam == "jrpg" && (candFam == "shooter" || candFam == "sport"))
                return true;
            if (candFam == "jrpg" && (refFam == "shooter" || refFam == "sport"))
                return true;

            return false;
        }



        private void RecalcSuggestedGame()
        {
            var s = Settings;

            // Reset
            s.SuggestedGameName = string.Empty;
            s.SuggestedGameCoverPath = string.Empty;
            s.SuggestedGameBackgroundPath = string.Empty;
            s.SuggestedGameSourceName = string.Empty;
            s.SuggestedGameReasonKey = string.Empty;
            s.SuggestedGameBannerText = string.Empty;

            var games = PlayniteApi.Database.Games.ToList();
            if (!s.IncludeHidden)
            {
                games = games.Where(g => g.Hidden != true).ToList();
            }

            if (games.Count == 0)
            {
                return;
            }

            Func<ulong, ulong> ToMinutes = raw => raw / 60UL;

            // 1) RefGame (Top 3 sur 14 jours + sticky journée)
            List<Playnite.SDK.Models.Game> refTop3;
            Playnite.SDK.Models.Game refGame = GetOrSelectRefGameForToday(games, out refTop3);

            if (refGame == null)
            {
                return;
            }


            var refName = Safe(refGame.Name);
            var refGenres = GetGenreNames(refGame).ToList();
            var refTags = GetTagNames(refGame)
                .Where(IsUsefulProfileTag)
                .ToList();
            var refDevs = GetDeveloperNames(refGame).ToList();
            var refPubs = GetPublisherNames(refGame).ToList();

            // 2) Trouver la meilleure suggestion (Find the best suggestion)

            var candidates = new List<SuggestedGameCandidate>();

            foreach (var g in games)
            {
                if (g.Id == refGame.Id)
                    continue;

                // Exclude games marked as "finished"
                if (IsGameFinished(g))
                    continue;

                // Exclude "saturated" games from suggestions
                if (g.Playtime >= 30UL * 60UL)
                {
                    if (g.LastActivity.HasValue &&
                        (DateTime.Now - g.LastActivity.Value).TotalDays > 60)
                    {
                        continue;
                    }
                }

                // Candidate data
                var genres = GetGenreNames(g).ToList();
                var tags = GetTagNames(g)
                    .Where(IsUsefulProfileTag)
                    .ToList();
                var devs = GetDeveloperNames(g).ToList();
                var pubs = GetPublisherNames(g).ToList();

                // Détection univers/famille
                var refFam = DetectFamily(refGenres, refTags);
                var candFam = DetectFamily(genres, tags);

                // Incohérence forte
                if (AreFamiliesIncompatible(refFam, candFam))
                    continue;

                int score = 0;
                string reason = string.Empty;

                // Common genres
                var sharedGenres = refGenres.Intersect(genres, StringComparer.OrdinalIgnoreCase).ToList();
                if (sharedGenres.Count > 0)
                {
                    score += sharedGenres.Count * 15;
                    reason = "SameGenre";
                }

                // Common tags
                var sharedTags = refTags.Intersect(tags, StringComparer.OrdinalIgnoreCase).ToList();
                if (sharedTags.Count > 0)
                {
                    score += sharedTags.Count * 20;
                    if (string.IsNullOrEmpty(reason))
                        reason = "SimilarTags";
                }

                // Same developer
                var sharedDevs = refDevs.Intersect(devs, StringComparer.OrdinalIgnoreCase).ToList();
                if (sharedDevs.Count > 0)
                {
                    score += sharedDevs.Count * 60;
                    reason = "SameDeveloper";
                }

                // Same publisher
                var sharedPubs = refPubs.Intersect(pubs, StringComparer.OrdinalIgnoreCase).ToList();
                if (sharedPubs.Count > 0)
                {
                    score += sharedPubs.Count * 15;
                    if (string.IsNullOrEmpty(reason))
                        reason = "SamePublisher";
                }

                // Bonus backlog 
                var minutes = ToMinutes(g.Playtime);
                if (minutes == 0)
                    score += 25;
                else if (minutes < 120) // < 2h 
                    score += 15;

                // Bonus "installed"
                if (g.IsInstalled == true)
                    score += 10;

                if (score <= 0)
                    continue;

                candidates.Add(new SuggestedGameCandidate
                {
                    Game = g,
                    Score = score,
                    Reason = string.IsNullOrEmpty(reason) ? string.Empty : reason
                });
            }

            // 2bis) Top 3 + rotation une fois par jour

            if (candidates.Count == 0)
            {
                return;
            }

            // Date du jour (locale)
            var today = DateTime.Now.Date;

            var ordered = candidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Game.Name ?? string.Empty)
                .ToList();

            // On garde un top 3 (ou moins si pas assez de jeux)
            var topCandidates = ordered.Take(3).ToList();
            if (topCandidates.Count == 0)
            {
                return;
            }

            SuggestedGameCandidate selected = null;

            // Si on a déjà un jeu pour aujourd'hui ET qu'il est encore candidat, on le garde
            if (s.SuggestedGameLastId != Guid.Empty &&
                s.SuggestedGameLastChangeDate.Date == today)
            {
                selected = candidates.FirstOrDefault(c => c.Game.Id == s.SuggestedGameLastId);
            }

            // Sinon on choisit un jeu de départ dans le top 3
            if (selected == null)
            {
                var rand = new Random();
                selected = topCandidates[rand.Next(topCandidates.Count)];

                s.SuggestedGameLastId = selected.Game.Id;
                s.SuggestedGameLastChangeDate = today;
            }

            UpdateSuggestedGamesRotation(topCandidates, selected, refName);
            SaveSettingsSafe();
        }

        // Monthly snapshot
        private string GetMonthlyDir()
        {
            var dir = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString(), "monthly");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetMonthFilePath(DateTime monthStart) => Path.Combine(GetMonthlyDir(), $"{monthStart:yyyy-MM}.json");

        private Dictionary<Guid, ulong> LoadMonthSnapshot(DateTime monthStart)
        {
            var file = GetMonthFilePath(monthStart);
            try
            {
                if (!File.Exists(file))
                {
                    var snap = PlayniteApi.Database.Games.ToDictionary(g => g.Id, g => g.Playtime / 60UL);
                    var json = Serialization.ToJson(snap, true);
                    File.WriteAllText(file, json);
                    DebugLog($"[AnikiHelper] Created monthly snapshot: {file}");
                    return snap;
                }

                var jsonText = File.ReadAllText(file);
                var dict = Serialization.FromJson<Dictionary<Guid, ulong>>(jsonText);
                return dict ?? new Dictionary<Guid, ulong>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] LoadMonthSnapshot failed");
                return new Dictionary<Guid, ulong>();
            }
        }

        private void UpdateSuggestedGamesRotation(IList<SuggestedGameCandidate> topCandidates, SuggestedGameCandidate selected, string refName)
        {
            suggestedGameRotation.Clear();
            suggestedGameRotationIndex = 0;

            if (topCandidates == null || topCandidates.Count == 0)
            {
                ApplySuggestedGameSnapshot(null);
                return;
            }

            var ordered = new List<SuggestedGameCandidate>();

            if (selected != null)
            {
                ordered.Add(selected);
            }

            foreach (var candidate in topCandidates)
            {
                if (candidate?.Game == null)
                {
                    continue;
                }

                if (selected != null && candidate.Game.Id == selected.Game.Id)
                {
                    continue;
                }

                ordered.Add(candidate);
            }

            foreach (var candidate in ordered.Take(3))
            {
                var snapshot = BuildSuggestedGameSnapshot(candidate, refName);
                if (snapshot != null)
                {
                    suggestedGameRotation.Add(snapshot);
                }
            }

            ApplySuggestedGameSnapshot(suggestedGameRotation.Count > 0 ? suggestedGameRotation[0] : null);
        }

        private SuggestedGameSnapshot BuildSuggestedGameSnapshot(SuggestedGameCandidate candidate, string refName)
        {
            var game = candidate?.Game;
            if (game == null)
            {
                return null;
            }

            var reason = candidate.Reason ?? string.Empty;
            string banner = string.Empty;

            if (!string.IsNullOrEmpty(reason) && !string.IsNullOrEmpty(refName))
            {
                switch (reason)
                {
                    case "SameGenre":
                        banner = string.Format(
                            Loc("SuggestBanner_SameGenre", "Same genre as {0}"),
                            refName);
                        break;

                    case "SimilarTags":
                        banner = string.Format(
                            Loc("SuggestBanner_SimilarTags", "Similar tags to {0}"),
                            refName);
                        break;

                    case "SameDeveloper":
                        banner = string.Format(
                            Loc("SuggestBanner_SameDeveloper", "Same developer as {0}"),
                            refName);
                        break;

                    case "SamePublisher":
                        banner = string.Format(
                            Loc("SuggestBanner_SamePublisher", "Same publisher as {0}"),
                            refName);
                        break;
                }
            }

            string coverPath = GetGameCoverPath(game);

            string bgPath = null;
            if (!string.IsNullOrEmpty(game.BackgroundImage))
                bgPath = PlayniteApi.Database.GetFullFilePath(game.BackgroundImage);
            if (string.IsNullOrEmpty(bgPath) && !string.IsNullOrEmpty(game.CoverImage))
                bgPath = PlayniteApi.Database.GetFullFilePath(game.CoverImage);
            if (string.IsNullOrEmpty(bgPath) && !string.IsNullOrEmpty(game.Icon))
                bgPath = PlayniteApi.Database.GetFullFilePath(game.Icon);

            return new SuggestedGameSnapshot
            {
                GameId = game.Id,
                SourceName = refName ?? string.Empty,
                Name = Safe(game.Name),
                CoverPath = string.IsNullOrEmpty(coverPath) ? string.Empty : coverPath,
                BackgroundPath = string.IsNullOrEmpty(bgPath) ? string.Empty : bgPath,
                ReasonKey = reason,
                BannerText = banner ?? string.Empty
            };
        }

        private void ApplySuggestedGameSnapshot(SuggestedGameSnapshot item)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (item == null)
                {
                    Settings.SuggestedGameName = string.Empty;
                    Settings.SuggestedGameCoverPath = string.Empty;
                    Settings.SuggestedGameBackgroundPath = string.Empty;
                    Settings.SuggestedGameSourceName = string.Empty;
                    Settings.SuggestedGameReasonKey = string.Empty;
                    Settings.SuggestedGameBannerText = string.Empty;
                    Settings.SuggestedGameLastId = Guid.Empty;

                    Settings.SuggestedGameBackgroundPathA = string.Empty;
                    Settings.SuggestedGameBackgroundPathB = string.Empty;
                    Settings.SuggestedGameShowLayerB = false;

                    suggestedGameCrossfadeInitialized = false;
                    return;
                }

                // Texte / infos fixes
                Settings.SuggestedGameSourceName = item.SourceName ?? string.Empty;
                Settings.SuggestedGameName = item.Name ?? string.Empty;
                Settings.SuggestedGameCoverPath = item.CoverPath ?? string.Empty;
                Settings.SuggestedGameBackgroundPath = item.BackgroundPath ?? string.Empty;
                Settings.SuggestedGameReasonKey = item.ReasonKey ?? string.Empty;
                Settings.SuggestedGameBannerText = item.BannerText ?? string.Empty;
                Settings.SuggestedGameLastId = item.GameId;

                var bgPath = item.BackgroundPath ?? string.Empty;

                // Premier affichage : on remplit A et B avec la même image
                if (!suggestedGameCrossfadeInitialized)
                {
                    Settings.SuggestedGameBackgroundPathA = bgPath;
                    Settings.SuggestedGameBackgroundPathB = bgPath;
                    Settings.SuggestedGameShowLayerB = false;

                    suggestedGameCrossfadeInitialized = true;
                    return;
                }

                // Si B est visible, on prépare A puis on révèle A en faisant disparaître B
                if (Settings.SuggestedGameShowLayerB)
                {
                    Settings.SuggestedGameBackgroundPathA = bgPath;
                    Settings.SuggestedGameShowLayerB = false;
                }
                else
                {
                    // Si A est visible, on prépare B puis on fade-in B
                    Settings.SuggestedGameBackgroundPathB = bgPath;
                    Settings.SuggestedGameShowLayerB = true;
                }
            });
        }

        private void RotateSuggestedGamesIfNeeded()
        {
            if (suggestedGameRotation.Count <= 1)
            {
                return;
            }

            suggestedGameRotationIndex++;
            if (suggestedGameRotationIndex >= suggestedGameRotation.Count)
            {
                suggestedGameRotationIndex = 0;
            }

            ApplySuggestedGameSnapshot(suggestedGameRotation[suggestedGameRotationIndex]);
        }

        private class SuggestedGameSnapshot
        {
            public Guid GameId { get; set; } = Guid.Empty;
            public string Name { get; set; }
            public string CoverPath { get; set; }
            public string BackgroundPath { get; set; }
            public string SourceName { get; set; }
            public string ReasonKey { get; set; }
            public string BannerText { get; set; }
        }

        private class SuggestedGameCandidate
        {
            public Playnite.SDK.Models.Game Game { get; set; }
            public int Score { get; set; }
            public string Reason { get; set; }
        }

        private class MonthlySnapshotInfo
        {
            public DateTime MonthStart { get; set; }
            public Dictionary<Guid, ulong> Minutes { get; set; }
        }

        private class MonthlyBackupPackage
        {
            public int Version { get; set; } = 1;
            public DateTime ExportedAt { get; set; }
            public List<MonthlyBackupMonth> Months { get; set; } = new List<MonthlyBackupMonth>();
        }

        private class MonthlyBackupMonth
        {
            public string MonthKey { get; set; } // ex: "2026-04"
            public List<MonthlyBackupEntry> Games { get; set; } = new List<MonthlyBackupEntry>();
        }

        private class MonthlyBackupEntry
        {
            public string OriginalGameId { get; set; }
            public string Name { get; set; }
            public string PluginId { get; set; }
            public string LibraryGameId { get; set; }
            public ulong Minutes { get; set; }
        }

        private Dictionary<Guid, ulong> ComputeRecentMinutesFromSnapshots(
            List<MonthlySnapshotInfo> snapshots,
            List<Playnite.SDK.Models.Game> games)
        {
            var result = new Dictionary<Guid, ulong>();

            if (snapshots == null || snapshots.Count == 0 || games == null || games.Count == 0)
            {
                return result;
            }

            const int MaxMonths = 4;
            if (snapshots.Count > MaxMonths)
            {
                snapshots = snapshots.Skip(snapshots.Count - MaxMonths).ToList();
            }

            var validGameIds = new HashSet<Guid>(games.Select(g => g.Id));

            // 1) Mois terminés 
            for (int i = 0; i < snapshots.Count - 1; i++)
            {
                var a = snapshots[i];
                var b = snapshots[i + 1];

                var ids = new HashSet<Guid>(a.Minutes.Keys);
                foreach (var id in b.Minutes.Keys)
                {
                    ids.Add(id);
                }

                foreach (var id in ids)
                {
                    if (!validGameIds.Contains(id))
                    {
                        continue; // jeu supprimé de la bibliothèque
                    }

                    if (!a.Minutes.TryGetValue(id, out var m0))
                    {
                        continue;
                    }

                    if (!b.Minutes.TryGetValue(id, out var m1))
                    {
                        continue;
                    }

                    if (m1 > m0)
                    {
                        var delta = m1 - m0;
                        ulong acc;
                        result.TryGetValue(id, out acc);
                        result[id] = acc + delta;
                    }
                }
            }

            // 2) Last month vs. current playtime 
            var last = snapshots[snapshots.Count - 1];
            Func<ulong, ulong> ToMinutes = raw => raw / 60UL;

            foreach (var g in games)
            {
                if (!last.Minutes.TryGetValue(g.Id, out var baseMinutes))
                {
                    continue;
                }

                var curr = ToMinutes(g.Playtime);

                if (curr > baseMinutes)
                {
                    var delta = curr - baseMinutes;
                    ulong acc;
                    result.TryGetValue(g.Id, out acc);
                    result[g.Id] = acc + delta;
                }
            }

            return result;
        }


        private List<MonthlySnapshotInfo> LoadAllMonthlySnapshots()
        {
            var list = new List<MonthlySnapshotInfo>();

            try
            {
                var monthlyDir = GetMonthlyDir();
                if (!Directory.Exists(monthlyDir))
                {
                    return list;
                }

                var files = Directory.GetFiles(monthlyDir, "*.json");
                foreach (var path in files)
                {
                    var name = Path.GetFileNameWithoutExtension(path);

                    DateTime monthStart;
                    if (!DateTime.TryParseExact(
                            name,
                            "yyyy-MM",
                            null,
                            System.Globalization.DateTimeStyles.None,
                            out monthStart))
                    {
                        continue;
                    }

                    try
                    {
                        var json = File.ReadAllText(path);
                        var dict = Serialization.FromJson<Dictionary<Guid, ulong>>(json);
                        if (dict == null)
                        {
                            continue;
                        }

                        list.Add(new MonthlySnapshotInfo
                        {
                            MonthStart = monthStart,
                            Minutes = dict
                        });
                    }
                    catch
                    {

                    }
                }

                // sorted from oldest to newest
                list.Sort((a, b) => a.MonthStart.CompareTo(b.MonthStart));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] LoadAllMonthlySnapshots failed");
            }

            return list;
        }

        private string SafePluginId(Game game)
        {
            if (game?.PluginId == null || game.PluginId == Guid.Empty)
            {
                return string.Empty;
            }

            return game.PluginId.ToString();
        }

        private string SafeLibraryGameId(Game game)
        {
            return game?.GameId?.Trim() ?? string.Empty;
        }

        private Game FindGameForBackupEntry(MonthlyBackupEntry entry, List<Game> games)
        {
            if (entry == null || games == null || games.Count == 0)
            {
                return null;
            }

            // 1) Match fort : PluginId + LibraryGameId
            if (!string.IsNullOrWhiteSpace(entry.PluginId) &&
                !string.IsNullOrWhiteSpace(entry.LibraryGameId))
            {
                var strongMatch = games.FirstOrDefault(g =>
                    string.Equals(SafePluginId(g), entry.PluginId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(SafeLibraryGameId(g), entry.LibraryGameId, StringComparison.OrdinalIgnoreCase));

                if (strongMatch != null)
                {
                    return strongMatch;
                }
            }

            // 2) Fallback : nom exact (insensible à la casse)
            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                var nameMatches = games
                    .Where(g => string.Equals(g?.Name?.Trim(), entry.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (nameMatches.Count == 1)
                {
                    return nameMatches[0];
                }
            }

            return null;
        }

        private void EnsureCurrentMonthSnapshotExists()
        {
            try
            {
                var now = DateTime.Now;
                var monthStart = new DateTime(now.Year, now.Month, 1);
                var file = GetMonthFilePath(monthStart);

                if (!File.Exists(file))
                {
                    var snapshot = PlayniteApi.Database.Games.ToDictionary(g => g.Id, g => g.Playtime / 60UL);
                    var json = Serialization.ToJson(snapshot, true);
                    Directory.CreateDirectory(Path.GetDirectoryName(file) ?? GetMonthlyDir());
                    File.WriteAllText(file, json);
                    DebugLog($"[AnikiHelper] Monthly snapshot created for {monthStart:yyyy-MM} at {file}.");
                }

                UpdateSnapshotInfoProperty(monthStart);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] EnsureCurrentMonthSnapshotExists failed");
            }
        }

        private void EnsureGameInCurrentMonthSnapshot(Playnite.SDK.Models.Game g, int sessionMinutes)
        {
            if (g == null)
            {
                return;
            }

            try
            {
                var now = DateTime.Now;
                var monthStart = new DateTime(now.Year, now.Month, 1);

                var snapshot = LoadMonthSnapshot(monthStart);

                // Si le jeu est déjà dans le snapshot, on ne touche à rien
                if (snapshot.ContainsKey(g.Id))
                {
                    return;
                }

                // On veut comme base le temps de jeu AVANT la session
                ulong baseMinutes;

                if (sessionStartPlaytimeMinutes != null &&
                    sessionStartPlaytimeMinutes.TryGetValue(g.Id, out var startMinutes))
                {
                    baseMinutes = startMinutes;
                }
                else
                {
                    // Fallback
                    var current = (ulong)(g.Playtime / 60UL);
                    if (sessionMinutes > 0 && current > (ulong)sessionMinutes)
                    {
                        baseMinutes = current - (ulong)sessionMinutes;
                    }
                    else
                    {
                        baseMinutes = current;
                    }
                }

                snapshot[g.Id] = baseMinutes;

                var file = GetMonthFilePath(monthStart);
                var json = Serialization.ToJson(snapshot, true);
                Directory.CreateDirectory(Path.GetDirectoryName(file) ?? GetMonthlyDir());
                File.WriteAllText(file, json);

            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper] Failed to ensure game {g?.Id} is in current month snapshot.");
            }
        }


        // Reset snapshot
        public void ResetMonthlySnapshot()
        {
            try
            {
                var confirm = PlayniteApi.Dialogs.ShowMessage(
                    "This will delete the current month tracking data and recreate the snapshot from now. Continue?",
                    "Aniki Helper",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }

                var now = DateTime.Now;
                var monthStart = new DateTime(now.Year, now.Month, 1);
                var file = GetMonthFilePath(monthStart);

                var snapshot = PlayniteApi.Database.Games.ToDictionary(g => g.Id, g => g.Playtime / 60UL);
                var json = Serialization.ToJson(snapshot, true);

                Directory.CreateDirectory(Path.GetDirectoryName(file) ?? GetMonthlyDir());
                File.WriteAllText(file, json);

                UpdateSnapshotInfoProperty(monthStart);
                RecalcStatsSafe();

                PlayniteApi.Dialogs.ShowMessage(
                    "Current month tracking has been reset. Stats will restart from now.",
                    "Aniki Helper"
                );
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ResetMonthlySnapshot failed");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    $"Failed to reset monthly tracking: {ex.Message}",
                    "Aniki Helper"
                );
            }
        }

        public void ExportMonthlyBackup(string exportFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exportFilePath))
                {
                    throw new Exception("Export path is empty.");
                }

                var snapshots = LoadAllMonthlySnapshots();
                var allGames = PlayniteApi.Database.Games.ToList();

                var package = new MonthlyBackupPackage
                {
                    Version = 1,
                    ExportedAt = DateTime.Now
                };

                foreach (var snap in snapshots.OrderBy(s => s.MonthStart))
                {
                    var month = new MonthlyBackupMonth
                    {
                        MonthKey = snap.MonthStart.ToString("yyyy-MM")
                    };

                    foreach (var kv in snap.Minutes)
                    {
                        var game = allGames.FirstOrDefault(g => g.Id == kv.Key);

                        month.Games.Add(new MonthlyBackupEntry
                        {
                            OriginalGameId = kv.Key.ToString(),
                            Name = game?.Name ?? string.Empty,
                            PluginId = SafePluginId(game),
                            LibraryGameId = SafeLibraryGameId(game),
                            Minutes = kv.Value
                        });
                    }

                    package.Months.Add(month);
                }

                var json = Serialization.ToJson(package, true);

                var dir = Path.GetDirectoryName(exportFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(exportFilePath, json);

                DebugLog($"[AnikiHelper] Monthly backup exported: {exportFilePath}");
                PlayniteApi.Dialogs.ShowMessage("Monthly backup exported successfully.", "AnikiHelper");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ExportMonthlyBackup failed");
                PlayniteApi.Dialogs.ShowErrorMessage($"Monthly backup export failed: {ex.Message}", "AnikiHelper");
            }
        }

        public void ImportMonthlyBackup(string importFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(importFilePath) || !File.Exists(importFilePath))
                {
                    throw new Exception("Backup file not found.");
                }

                var json = File.ReadAllText(importFilePath);
                var package = Serialization.FromJson<MonthlyBackupPackage>(json);

                if (package == null || package.Months == null || package.Months.Count == 0)
                {
                    throw new Exception("Backup file is empty or invalid.");
                }

                var allGames = PlayniteApi.Database.Games.ToList();

                int restoredMonths = 0;
                int restoredEntries = 0;
                int skippedEntries = 0;

                foreach (var month in package.Months)
                {
                    if (month == null || string.IsNullOrWhiteSpace(month.MonthKey))
                    {
                        continue;
                    }

                    if (!DateTime.TryParseExact(
                        month.MonthKey,
                        "yyyy-MM",
                        null,
                        System.Globalization.DateTimeStyles.None,
                        out var monthStart))
                    {
                        continue;
                    }

                    var rebuiltSnapshot = new Dictionary<Guid, ulong>();

                    foreach (var entry in month.Games ?? Enumerable.Empty<MonthlyBackupEntry>())
                    {
                        var game = FindGameForBackupEntry(entry, allGames);
                        if (game == null)
                        {
                            skippedEntries++;
                            continue;
                        }

                        rebuiltSnapshot[game.Id] = entry.Minutes;
                        restoredEntries++;
                    }

                    var file = GetMonthFilePath(monthStart);
                    var monthJson = Serialization.ToJson(rebuiltSnapshot, true);

                    Directory.CreateDirectory(Path.GetDirectoryName(file) ?? GetMonthlyDir());
                    File.WriteAllText(file, monthJson);

                    restoredMonths++;
                }

                EnsureMonthlySnapshotSafe();
                RecalcStatsSafe();

                DebugLog($"[AnikiHelper] Monthly backup imported: {importFilePath} | months={restoredMonths} entries={restoredEntries} skipped={skippedEntries}");

                PlayniteApi.Dialogs.ShowMessage(
                    $"Monthly backup imported.\n\nMonths restored: {restoredMonths}\nEntries restored: {restoredEntries}\nEntries skipped: {skippedEntries}",
                    "AnikiHelper");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ImportMonthlyBackup failed");
                PlayniteApi.Dialogs.ShowErrorMessage($"Monthly backup import failed: {ex.Message}", "AnikiHelper");
            }
        }

        // Clears the dynamic color cache 
        public void ClearDynamicColorCache()
        {
            try
            {
                DynamicAuto.ClearPersistentCache(alsoRam: true);

                var dir = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());

                var files = new[]
                {
            Path.Combine(dir, "palette_cache_v2.json"),
            Path.Combine(dir, "palette_cache_v2.json.tmp"),
            Path.Combine(dir, "palette_cache.json"),
            Path.Combine(dir, "palette_cache.json.tmp"),
            Path.Combine(dir, "palette_cache_v1.json"),
            Path.Combine(dir, "palette_cache_v1.json.tmp")
        };

                int deleted = 0;

                foreach (var file in files)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }

                DebugLog($"[AnikiHelper] Cleared dynamic color cache. Files deleted: {deleted}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] ClearDynamicColorCache failed.");
                throw;
            }
        }

        private void EnsureDynamicColorCacheVersion()
        {
            try
            {
                const string RequiredCacheVersion = "1.3.3";

                var current = Settings?.DynamicColorCacheVersion;

                if (string.IsNullOrWhiteSpace(current) ||
                    !Version.TryParse(current, out var currentVersion) ||
                    currentVersion < new Version(1, 3, 3))
                {
                    DebugLog($"[AnikiHelper] Dynamic color cache is older than {RequiredCacheVersion}. Clearing cache.");

                    ClearDynamicColorCache();

                    Settings.DynamicColorCacheVersion = RequiredCacheVersion;
                    SavePluginSettings(Settings);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to check dynamic color cache version.");
            }
        }

        // Deletes the News cache

        public void ClearNewsCacheA()
        {
            try
            {
                OnUi(() =>
                {
                    Settings.SteamGlobalNewsA?.Clear();
                    Settings.SteamGlobalNewsALastRefreshUtc = null;
                    Settings.LastCachedNewsSourceAUrl = string.Empty;
                });

                var newsRoot = Path.Combine(GetDataRoot(), "News Cache");

                var jsonPath = Path.Combine(newsRoot, "CacheNews_A.json");
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                }

                var imgRoot = Path.Combine(newsRoot, "NewsImages_A");
                if (Directory.Exists(imgRoot))
                {
                    Directory.Delete(imgRoot, true);
                }

                SaveSettingsSafe();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ClearNewsCacheA failed.");
            }
        }

        public void ClearNewsCacheB()
        {
            try
            {
                OnUi(() =>
                {
                    Settings.SteamGlobalNewsB?.Clear();
                    Settings.SteamGlobalNewsBLastRefreshUtc = null;
                    Settings.LastCachedNewsSourceBUrl = string.Empty;
                });

                var newsRoot = Path.Combine(GetDataRoot(), "News Cache");

                var jsonPath = Path.Combine(newsRoot, "CacheNews_B.json");
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                }

                var imgRoot = Path.Combine(newsRoot, "NewsImages_B");
                if (Directory.Exists(imgRoot))
                {
                    Directory.Delete(imgRoot, true);
                }

                SaveSettingsSafe();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ClearNewsCacheB failed.");
            }
        }

        // Updates the snapshot info text
        private void UpdateSnapshotInfoProperty(DateTime monthStart)
        {
            try
            {
                var file = GetMonthFilePath(monthStart);

                string text;

                if (File.Exists(file))
                {
                    var created = File.GetCreationTime(file);
                    var modified = File.GetLastWriteTime(file);
                    var dt = created < modified ? created : modified;
                    text = $"Snapshot {monthStart:MM/yyyy} : {dt:dd/MM/yyyy HH:mm}";
                }
                else
                {
                    text = $"Snapshot {monthStart:MM/yyyy} : (aucun)";
                }

                OnUi(() => Settings.SnapshotDateString = text);
            }
            catch
            {
                OnUi(() => Settings.SnapshotDateString = $"Snapshot {monthStart:MM/yyyy} : (indisponible)");
            }
        }


        private void EnsureMonthlySnapshotSafe()
        {
            try { EnsureCurrentMonthSnapshotExists(); }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] EnsureMonthlySnapshot crashed; continuing without monthly snapshot.");
            }
        }

        private void UpdateFullscreenAspectRatio()
        {
            try
            {
                if (Settings == null)
                {
                    return;
                }

                var window = Application.Current?.MainWindow;

                if (window == null)
                {
                    return;
                }

                var width = window.ActualWidth;
                var height = window.ActualHeight;

                if (width <= 0 || height <= 0)
                {
                    return;
                }

                Settings.AspectRatio = GetAspectRatioKey(width, height);

                DebugLog($"[AnikiHelper] Fullscreen aspect ratio detected: {Settings.AspectRatio} ({width:0}x{height:0})");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to update fullscreen aspect ratio.");
            }
        }

        private static string GetAspectRatioKey(double width, double height)
        {
            if (height <= 0)
            {
                return "dsp169";
            }

            var ratio = width / height;

            // 16:10 = 1.60
            if (Math.Abs(ratio - 1.6) <= 0.06)
            {
                return "dsp1610";
            }

            // Ultrawide: 21:9 / 3440x1440 / 2560x1080
            if (ratio >= 2.15)
            {
                return "dsp219";
            }

            // Default: 16:9 and close ratios.
            return "dsp169";
        }

        private void OnFullscreenWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateFullscreenAspectRatio();
        }

        public AnikiHelper(IPlayniteAPI api) : base(api)
        {
            Instance = this;

            // Fullscreen or not
            isFullscreenMode = api?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;

            // ViewModel 
            SettingsVM = new AnikiHelperSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            UpdateVersionCheckState();

            if (isFullscreenMode)
            {
                global::AnikiHelperFullscreen.Views.FullscreenSettingsView.Init();
            }

            // Sécu jamais de Settings null (Security never from Settings null)
            if (SettingsVM.Settings == null)
            {
                SettingsVM.Settings = new AnikiHelperSettings(this);
            }

            // Langue Playnite -> Steam
            var playniteLang = api?.ApplicationSettings?.Language;

            steamUpdateService = new SteamUpdateLiteService(playniteLang);
            rssNewsService = new SteamGlobalNewsService(api, Settings);
            eventSoundService = new EventSoundService(api, Settings);
            anikiWindowManager = new AnikiWindowManager(api);
            inGameOverlayService = new InGameOverlayService(api, Settings);

            anikiThemeSettingsService = new AnikiThemeSettingsService(
                api,
                Settings,
                logger,
                GetPluginUserDataPath());

            steamStoreService = new SteamStoreService(api, GetPluginUserDataPath());
            splashScreenService = new SplashScreenService(GetPluginUserDataPath());
            splashScreenRuntimeService = new SplashScreenRuntimeService(IsPlayniteForegroundWindow);

            horizontalFocusFixService = new NavigationFixService(api, () => Settings.IsWelcomeHubOpen);

            CleanupLegacyNewsCache();

            AddSettingsSupportSafe("AnikiHelper", "Settings");
            AddControllerSupportSafe();

            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Settings.IncludeHidden) ||
                    e.PropertyName == nameof(Settings.TopPlayedMax) ||
                    e.PropertyName == nameof(Settings.PlaytimeUseDaysFormat))
                {
                    RecalcStatsSafe();
                }

                if (e.PropertyName == nameof(Settings.SteamStoreEnabled) && Settings.SteamStoreEnabled == false)
                {
                    OnUi(() =>
                    {
                        Settings.SteamStoreDeals.Clear();
                        Settings.SteamStoreNewReleases.Clear();
                        Settings.SteamStoreTopSellers.Clear();
                        Settings.SteamStoreSpotlight.Clear();

                        Settings.SteamStoreDetailsVisible = false;
                        Settings.SteamStoreDetailsLoading = false;
                        Settings.SteamStoreDetailsTitle = string.Empty;
                        Settings.SteamStoreDetailsImage = string.Empty;
                        Settings.SteamStoreDetailsBackgroundImage = string.Empty;
                        Settings.SteamStoreDetailsDescription = string.Empty;
                        Settings.SteamStoreDetailsPrice = string.Empty;
                        Settings.SteamStoreDetailsDiscount = string.Empty;
                        Settings.SteamStoreDetailsOriginalPrice = string.Empty;
                        Settings.SteamStoreDetailsMetacriticScore = string.Empty;
                        Settings.SteamStoreDetailsRecommendationsTotal = string.Empty;
                        Settings.SteamStoreDetailsAchievementsTotal = string.Empty;
                        Settings.SteamStoreDetailsDlcCount = string.Empty;
                        Settings.SteamStoreDetailsSupportedLanguages = string.Empty;
                        Settings.SteamStoreDetailsScreenshot1 = string.Empty;
                        Settings.SteamStoreDetailsScreenshot2 = string.Empty;
                        Settings.SteamStoreDetailsScreenshot3 = string.Empty;
                        Settings.SteamStoreDetailsReleaseDate = string.Empty;
                        Settings.SteamStoreDetailsIsPreorder = false;
                        Settings.SteamStoreDetailsDevelopers = string.Empty;
                        Settings.SteamStoreDetailsPublishers = string.Empty;
                        Settings.SteamStoreDetailsGenres = string.Empty;
                        Settings.SteamStoreDetailsCategories = string.Empty;
                        Settings.SteamStoreDetailsControllerSupport = string.Empty;
                        Settings.SteamStoreDetailsAppId = 0;
                        Settings.SteamStoreDetailsStoreUrl = string.Empty;
                    });

                    SaveSettingsSafe();
                }
            };

            // Timers only in Fullscreen mode
            if (isFullscreenMode)
            {
                // Timer pour les updates Steam (debounce changement de jeu)
                steamUpdateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(800)
                };
                steamUpdateTimer.Tick += steamUpdateTimer_Tick;

                navigationSettleTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(250)
                };

                navigationSettleTimer.Tick += (s, e) =>
                {
                    navigationSettleTimer.Stop();

                    try
                    {
                        if (Settings != null)
                        {
                            Settings.IsFastNavigating = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to settle fast navigation state.");
                    }
                };

                // Charger le cache Steam Updates une seule fois (hors UI thread)
                Task.Run(() =>
                {
                    var loaded = LoadSteamUpdatesCache();
                    lock (steamUpdatesCacheLock)
                    {
                        steamUpdatesCache = loaded ?? new Dictionary<string, SteamUpdateCacheEntry>();
                        steamUpdatesCacheDirty = false;
                    }
                });

                Task.Run(() =>
                {
                    var loaded = LoadSteamGameNewsCache();

                    lock (steamGameNewsCacheLock)
                    {
                        steamGameNewsCache = loaded ?? new Dictionary<string, SteamGameNewsCacheEntry>();
                        steamGameNewsCacheDirty = false;
                    }
                });

                Task.Run(() =>
                {
                    var loaded = LoadSteamAppIdMappingCache();

                    lock (steamAppIdMappingCacheLock)
                    {
                        steamAppIdMappingCache = loaded ?? new Dictionary<Guid, SteamAppIdMappingEntry>();
                        steamAppIdMappingCacheDirty = false;
                    }
                });

                // Timer: flush du cache sur disque en différé (évite write pendant hover)
                steamUpdatesCacheFlushTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(20)
                };
                steamUpdatesCacheFlushTimer.Tick += (s, e) => FlushSteamUpdatesCacheIfNeeded();
                steamUpdatesCacheFlushTimer.Start();

                steamGameNewsCacheFlushTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(20)
                };
                steamGameNewsCacheFlushTimer.Tick += (s, e) => FlushSteamGameNewsCacheIfNeeded();
                steamGameNewsCacheFlushTimer.Start();

                newsRotationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(15)
                };
                newsRotationTimer.Tick += NewsRotationTimer_Tick;

                suggestedRotationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(15)
                };
                suggestedRotationTimer.Tick += SuggestedRotationTimer_Tick;
            }
        }

        public string GetResolvedSteamStoreLanguage()
        {
            if (!string.IsNullOrWhiteSpace(Settings?.SteamStoreLanguage))
            {
                return Settings.SteamStoreLanguage.Trim().ToLowerInvariant();
            }

            return "english";
        }

        public string GetResolvedSteamStoreRegion()
        {
            if (!string.IsNullOrWhiteSpace(Settings?.SteamStoreRegion))
            {
                return Settings.SteamStoreRegion.Trim().ToUpperInvariant();
            }

            return "US";
        }

        public async void OpenSteamStoreDetails(SteamStoreItem item)
        {
            if (Settings?.SteamStoreEnabled != true)
            {
                return;
            }

            if (item == null)
            {
                return;
            }

            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            if (!IsAnikiThemeActive())
            {
                return;
            }

            try
            {
                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                OpenChildWindow("SteamStoreDetailsStyle");

                OnUi(() =>
                {
                    Settings.SteamStoreDetailsTitle = item.Name ?? string.Empty;
                    Settings.SteamStoreDetailsImage = item.HeaderImageLocalPath ?? item.HeaderImageUrl ?? string.Empty;
                    Settings.SteamStoreDetailsBackgroundImage =
                        item.BackgroundImageLocalPath ??
                        item.BackgroundImageUrl ??
                        item.HeaderImageLocalPath ??
                        item.HeaderImageUrl ??
                        string.Empty;
                    Settings.SteamStoreDetailsDescription = "Loading details...";
                    Settings.SteamStoreDetailsPrice = item.FinalPriceDisplay ?? string.Empty;
                    Settings.SteamStoreDetailsDiscount = item.DiscountDisplay ?? string.Empty;
                    Settings.SteamStoreDetailsOriginalPrice = item.OriginalPriceDisplay ?? string.Empty;
                    Settings.SteamStoreDetailsAppId = item.AppId;
                    Settings.SteamStoreDetailsStoreUrl = item.StoreUrl ?? $"https://store.steampowered.com/app/{item.AppId}/";
                    Settings.SteamStoreDetailsReleaseDate = "Loading...";
                    Settings.SteamStoreDetailsIsPreorder = false;
                    Settings.SteamStoreDetailsDevelopers = "Loading...";
                    Settings.SteamStoreDetailsPublishers = "Loading...";
                    Settings.SteamStoreDetailsGenres = "Loading...";
                    Settings.SteamStoreDetailsCategories = "Loading...";
                    Settings.SteamStoreDetailsSupportedLanguages = "Loading...";
                    Settings.SteamStoreDetailsControllerSupport = "Loading...";
                    Settings.SteamStoreDetailsLoading = true;
                    Settings.SteamStoreDetailsVisible = true;
                });

                await steamStoreService.EnrichStoreItemDetailsAsync(item, language, region);

                OnUi(() =>
                {
                    Settings.SteamStoreDetailsBackgroundImage =
                        item.BackgroundImageLocalPath ??
                        item.BackgroundImageUrl ??
                        item.HeaderImageLocalPath ??
                        item.HeaderImageUrl ??
                        Settings.SteamStoreDetailsImage ??
                        string.Empty;

                    Settings.SteamStoreDetailsDescription = string.IsNullOrWhiteSpace(item.ShortDescription)
                        ? "No description available for this title."
                        : item.ShortDescription;

                    Settings.SteamStoreDetailsReleaseDate = string.IsNullOrWhiteSpace(item.ReleaseDateDisplay)
                        ? "Release date unavailable"
                        : item.ReleaseDateDisplay;

                    Settings.SteamStoreDetailsIsPreorder = item.ComingSoon || item.IsPreorder;

                    Settings.SteamStoreDetailsDiscount = item.DiscountDisplay ?? string.Empty;

                    Settings.SteamStoreDetailsDevelopers = item.Developers != null && item.Developers.Count > 0
                        ? string.Join(", ", item.Developers)
                        : "Unknown";

                    Settings.SteamStoreDetailsPublishers = item.Publishers != null && item.Publishers.Count > 0
                        ? string.Join(", ", item.Publishers)
                        : "Unknown";

                    Settings.SteamStoreDetailsGenres = item.Genres != null && item.Genres.Count > 0
                        ? string.Join(", ", item.Genres)
                        : "Unknown";

                    Settings.SteamStoreDetailsCategories = item.Categories != null && item.Categories.Count > 0
                        ? string.Join(", ", item.Categories)
                        : "Unknown";

                    Settings.SteamStoreDetailsSupportedLanguages = string.IsNullOrWhiteSpace(item.SupportedLanguages)
                        ? "Unknown"
                        : item.SupportedLanguages;

                    Settings.SteamStoreDetailsControllerSupport = string.IsNullOrWhiteSpace(item.ControllerSupport)
                        ? "Unknown"
                        : item.ControllerSupport;

                    Settings.SteamStoreDetailsPrice = item.FinalPriceDisplay ?? string.Empty;
                    Settings.SteamStoreDetailsDiscount = item.DiscountDisplay ?? string.Empty;
                    Settings.SteamStoreDetailsOriginalPrice = item.OriginalPriceDisplay ?? string.Empty;
                    Settings.SteamStoreDetailsMetacriticScore = item.MetacriticScore > 0
                        ? item.MetacriticScore.ToString()
                        : string.Empty;

                    Settings.SteamStoreDetailsRecommendationsTotal = item.RecommendationsTotal > 0
                        ? item.RecommendationsTotal.ToString("N0")
                        : string.Empty;

                    Settings.SteamStoreDetailsAchievementsTotal = item.AchievementsTotal > 0
                        ? item.AchievementsTotal.ToString()
                        : string.Empty;

                    Settings.SteamStoreDetailsDlcCount = item.DlcCount > 0
                        ? item.DlcCount.ToString()
                        : string.Empty;

                    Settings.SteamStoreDetailsScreenshot1 = item.Screenshot1LocalPath ?? item.Screenshot1Url ?? string.Empty;
                    Settings.SteamStoreDetailsScreenshot2 = item.Screenshot2LocalPath ?? item.Screenshot2Url ?? string.Empty;
                    Settings.SteamStoreDetailsScreenshot3 = item.Screenshot3LocalPath ?? item.Screenshot3Url ?? string.Empty;
                    Settings.SteamStoreDetailsLoading = false;
                });

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to open Steam Store details.");

                OnUi(() =>
                {
                    Settings.SteamStoreDetailsLoading = false;
                    Settings.SteamStoreDetailsVisible = true;

                    Settings.SteamStoreDetailsDescription = "Unable to load store details.";
                    Settings.SteamStoreDetailsReleaseDate = "Unavailable";
                    Settings.SteamStoreDetailsDevelopers = "Unknown";
                    Settings.SteamStoreDetailsPublishers = "Unknown";
                    Settings.SteamStoreDetailsGenres = "Unknown";
                    Settings.SteamStoreDetailsCategories = "Unknown";
                    Settings.SteamStoreDetailsSupportedLanguages = "Unknown";
                    Settings.SteamStoreDetailsControllerSupport = "Unknown";
                });
            }
        }

        private void ReplaceSteamStoreCollection(System.Collections.ObjectModel.ObservableCollection<SteamStoreItem> target, System.Collections.Generic.List<SteamStoreItem> items)
        {
            target.Clear();

            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private async Task LoadSteamStoreCacheOnlyAsync()
        {
            if (Settings?.SteamStoreEnabled != true)
            {
                return;
            }

            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            if (!IsAnikiThemeActive())
            {
                return;
            }

            var language = GetResolvedSteamStoreLanguage();
            var region = GetResolvedSteamStoreRegion();

            var dealsTask = steamStoreService.GetDealsFromCacheOnlyAsync(language, region);
            var newTask = steamStoreService.GetNewReleasesFromCacheOnlyAsync(language, region);
            var topSellersTask = steamStoreService.GetTopSellersFromCacheOnlyAsync(language, region);
            var spotlightTask = steamStoreService.GetSpotlightFromCacheOnlyAsync(language, region);

            await Task.WhenAll(dealsTask, newTask, topSellersTask, spotlightTask);

            OnUi(() =>
            {
                ReplaceSteamStoreCollection(Settings.SteamStoreDeals, dealsTask.Result);
                ReplaceSteamStoreCollection(Settings.SteamStoreNewReleases, newTask.Result);
                ReplaceSteamStoreCollection(Settings.SteamStoreTopSellers, topSellersTask.Result);
                ReplaceSteamStoreCollection(Settings.SteamStoreSpotlight, spotlightTask.Result);
            });
        }

        private async Task PreloadSteamStoreBackgroundsAsync(List<SteamStoreItem> items, string language, string region)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (var item in items.Take(10))
            {
                try
                {
                    if (item == null || item.AppId <= 0)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(item.BackgroundImageLocalPath))
                    {
                        continue;
                    }

                    await steamStoreService.EnrichStoreItemDetailsAsync(item, language, region);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to preload Steam Store background.");
                }
            }
        }

        private async Task RefreshSteamStoreAllAsync()
        {
            if (Settings?.SteamStoreEnabled != true)
            {
                return;
            }

            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            if (!IsAnikiThemeActive())
            {
                return;
            }

            OnUi(() =>
            {
                Settings.SteamStoreLoading = true;
                Settings.SteamStoreError = string.Empty;
            });

            try
            {
                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                var dealsTask = steamStoreService.GetDealsAsync(language, region, TimeSpan.FromDays(1));
                var newTask = steamStoreService.GetNewReleasesAsync(language, region, TimeSpan.FromDays(1));
                var topSellersTask = steamStoreService.GetTopSellersAsync(language, region, TimeSpan.FromDays(1));
                var spotlightTask = steamStoreService.GetSpotlightAsync(language, region, TimeSpan.FromDays(1));

                await Task.WhenAll(dealsTask, newTask, topSellersTask, spotlightTask);

                await PreloadSteamStoreBackgroundsAsync(dealsTask.Result, language, region);
                await PreloadSteamStoreBackgroundsAsync(newTask.Result, language, region);
                await PreloadSteamStoreBackgroundsAsync(topSellersTask.Result, language, region);
                await PreloadSteamStoreBackgroundsAsync(spotlightTask.Result, language, region);

                OnUi(() =>
                {
                    ReplaceSteamStoreCollection(Settings.SteamStoreDeals, dealsTask.Result);
                    ReplaceSteamStoreCollection(Settings.SteamStoreNewReleases, newTask.Result);
                    ReplaceSteamStoreCollection(Settings.SteamStoreTopSellers, topSellersTask.Result);
                    ReplaceSteamStoreCollection(Settings.SteamStoreSpotlight, spotlightTask.Result);

                    var hasAnyData =
                        Settings.SteamStoreDeals.Count > 0 ||
                        Settings.SteamStoreNewReleases.Count > 0 ||
                        Settings.SteamStoreTopSellers.Count > 0 ||
                        Settings.SteamStoreSpotlight.Count > 0;

                    Settings.SteamStoreAvailable = hasAnyData;
                    Settings.SteamStoreError = hasAnyData ? string.Empty : "No store data available";
                    Settings.SteamStoreLoading = false;

                    SaveSettingsSafe();
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] RefreshSteamStoreAllAsync failed.");

                OnUi(() =>
                {
                    Settings.SteamStoreLoading = false;
                    Settings.SteamStoreAvailable = false;
                    Settings.SteamStoreError = "Error while loading Steam Store";
                });
            }
        }

        public async Task OnSteamStoreViewOpenedAsync()
        {
            if (Settings?.SteamStoreEnabled != true)
            {
                return;
            }

            await steamStoreOpenLock.WaitAsync();

            try
            {
                if ((DateTime.UtcNow - lastSteamStoreOpenRequestUtc) < TimeSpan.FromSeconds(2))
                {
                    return;
                }

                lastSteamStoreOpenRequestUtc = DateTime.UtcNow;

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                await LoadSteamStoreCacheOnlyAsync();

                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                var mustRefresh = await steamStoreService.IsAnyStoreCacheMissingOrExpiredAsync(
                    language,
                    region,
                    TimeSpan.FromDays(1)
                );

                if (!mustRefresh)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RefreshSteamStoreAllAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] OnSteamStoreViewOpenedAsync refresh failed.");
                    }
                });
            }
            finally
            {
                steamStoreOpenLock.Release();
            }
        }

        public async Task RefreshSteamDealsAsync()
        {
            try
            {
                if (Settings?.SteamStoreEnabled != true)
                {
                    return;
                }

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                var items = await steamStoreService.GetDealsAsync(language, region, TimeSpan.FromDays(1));

                Settings.SteamStoreDeals.Clear();

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        Settings.SteamStoreDeals.Add(item);
                    }
                }

                SaveSettingsSafe();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to refresh Steam deals.");
            }
        }

        public async Task RefreshSteamNewReleasesAsync()
        {
            try
            {
                if (Settings?.SteamStoreEnabled != true)
                {
                    return;
                }

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                var items = await steamStoreService.GetNewReleasesAsync(language, region, TimeSpan.FromDays(1));

                Settings.SteamStoreNewReleases.Clear();

                foreach (var item in items)
                {
                    Settings.SteamStoreNewReleases.Add(item);
                }

                SaveSettingsSafe();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to refresh Steam new releases.");
            }
        }

        public async Task RefreshSteamTopSellersAsync()
        {
            try
            {
                if (Settings?.SteamStoreEnabled != true)
                {
                    return;
                }

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                var items = await steamStoreService.GetTopSellersAsync(language, region, TimeSpan.FromDays(1));

                Settings.SteamStoreTopSellers.Clear();

                foreach (var item in items)
                {
                    Settings.SteamStoreTopSellers.Add(item);
                }

                SaveSettingsSafe();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to refresh Steam top sellers titles.");
            }
        }

        public async Task RefreshSteamSpotlightAsync()
        {
            try
            {
                if (Settings?.SteamStoreEnabled != true)
                {
                    return;
                }

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                var items = await steamStoreService.GetSpotlightAsync(language, region, TimeSpan.FromDays(1));

                Settings.SteamStoreSpotlight.Clear();

                foreach (var item in items)
                {
                    Settings.SteamStoreSpotlight.Add(item);
                }

                SaveSettingsSafe();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to refresh Steam spotlight.");
            }
        }

        public void OpenSteamGameNewsWindow()
        {
            try
            {
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                OpenChildWindow("GameNewsWindowStyle");

                var game = PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (game == null)
                {
                    ResetSteamGameNews();
                    return;
                }

                steamGameNewsCts?.Cancel();
                steamGameNewsCts?.Dispose();
                steamGameNewsCts = new CancellationTokenSource();

                _ = LoadSteamGameNewsForWindowAsync(game, steamGameNewsCts.Token);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenSteamGameNewsWindow failed.");
            }
        }

        public void OpenWindow(string parameter)
        {
            var styleKey = parameter;
            string focusTargetName = null;

            if (!string.IsNullOrWhiteSpace(parameter) && parameter.Contains("|"))
            {
                var parts = parameter.Split('|');
                styleKey = parts[0];
                focusTargetName = parts.Length > 1 ? parts[1] : null;
            }

            anikiWindowManager?.OpenWindow(styleKey, focusTargetName);
        }

        public void OpenChildWindow(string styleKey)
        {
            anikiWindowManager?.OpenChildWindow(styleKey);
        }

        public void CloseTopWindow()
        {
            anikiWindowManager?.CloseTopWindow();
        }

        public void OpenWhatsNewFromMenu()
        {
            _ = CheckWhatsNewAfterStartupAsync(true);
        }

        public void OpenNotificationsMenuFromQuickAccess()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Ferme la fenêtre Quick Access avant d'ouvrir les notifications Playnite
                        CloseTopWindow();

                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var app = Application.Current;
                                if (app == null)
                                {
                                    return;
                                }

                                var button = app.Windows
                                    .OfType<Window>()
                                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                                    .FirstOrDefault(x => x.Name == "PART_ButtonNotifications") as ButtonBase;

                                if (button == null)
                                {
                                    logger.Warn("[AnikiHelper] PART_ButtonNotifications was not found.");
                                    return;
                                }

                                if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                                {
                                    button.Command.Execute(button.CommandParameter);
                                    return;
                                }

                                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Failed to trigger PART_ButtonNotifications.");
                            }
                        }), DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] OpenNotificationsMenuFromQuickAccess inner failed.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenNotificationsMenuFromQuickAccess failed.");
            }
        }

        public void OpenAchievementsFromQuickAccess()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Ferme la fenêtre Quick Access avant d’ouvrir Achievements
                        CloseTopWindow();

                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var app = Application.Current;
                                if (app == null)
                                {
                                    return;
                                }

                                var button = app.Windows
                                    .OfType<Window>()
                                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                                    .FirstOrDefault(x => x.Name == "HiddenOpenAchievementsButton") as System.Windows.Controls.Primitives.ButtonBase;

                                if (button == null)
                                {
                                    logger.Warn("[AnikiHelper] HiddenOpenAchievementsButton was not found.");
                                    return;
                                }

                                if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                                {
                                    button.Command.Execute(button.CommandParameter);
                                    return;
                                }

                                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Failed to trigger HiddenOpenAchievementsButton.");
                            }
                        }), DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] OpenAchievementsFromQuickAccess inner failed.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenAchievementsFromQuickAccess failed.");
            }
        }

        public void TriggerHiddenButtonAfterClosingTopWindow(string buttonName)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        CloseTopWindow();

                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var app = Application.Current;
                                if (app == null)
                                {
                                    return;
                                }

                                var button = app.Windows
                                    .OfType<Window>()
                                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                                    .FirstOrDefault(x => x.Name == buttonName) as System.Windows.Controls.Primitives.ButtonBase;

                                if (button == null)
                                {
                                    logger.Warn("[AnikiHelper] Hidden button was not found: " + buttonName);
                                    return;
                                }

                                if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                                {
                                    button.Command.Execute(button.CommandParameter);
                                    return;
                                }

                                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Failed to trigger hidden button: " + buttonName);
                            }
                        }), DispatcherPriority.ApplicationIdle);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] TriggerHiddenButtonAfterClosingTopWindow inner failed.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] TriggerHiddenButtonAfterClosingTopWindow failed.");
            }
        }

        public void SwitchNewsTab(bool next)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var app = Application.Current;
                        if (app == null)
                        {
                            return;
                        }

                        var elements = app.Windows
                            .OfType<Window>()
                            .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                            .ToList();

                        var modeNews = elements.FirstOrDefault(x => x.Name == "ModeNewsInternal") as ToggleButton;
                        var modeDeals = elements.FirstOrDefault(x => x.Name == "ModeDealsInternal") as ToggleButton;
                        var modeUpdates = elements.FirstOrDefault(x => x.Name == "ModeUpdatesInternal") as ToggleButton;
                        var modePlaynite = elements.FirstOrDefault(x => x.Name == "ModePlayniteInternal") as ToggleButton;

                        if (modeNews == null || modeDeals == null || modeUpdates == null || modePlaynite == null)
                        {
                            logger.Warn("[AnikiHelper] News tab toggles were not found.");
                            return;
                        }

                        var tabs = new[]
                        {
                    modeNews,
                    modeDeals,
                    modeUpdates,
                    modePlaynite
                };

                        int currentIndex = 0;

                        for (int i = 0; i < tabs.Length; i++)
                        {
                            if (tabs[i].IsChecked == true)
                            {
                                currentIndex = i;
                                break;
                            }
                        }

                        int newIndex;

                        if (next)
                        {
                            newIndex = currentIndex + 1;
                            if (newIndex >= tabs.Length)
                            {
                                newIndex = 0;
                            }
                        }
                        else
                        {
                            newIndex = currentIndex - 1;
                            if (newIndex < 0)
                            {
                                newIndex = tabs.Length - 1;
                            }
                        }

                        for (int i = 0; i < tabs.Length; i++)
                        {
                            tabs[i].IsChecked = i == newIndex;
                        }

                        // Focus optionnel sur le bouton d'onglet correspondant
                        string focusName = "NewsTabBtn";

                        if (newIndex == 1)
                        {
                            focusName = "DealsTabBtn";
                        }
                        else if (newIndex == 2)
                        {
                            focusName = "UpdatesTabBtn";
                        }
                        else if (newIndex == 3)
                        {
                            focusName = "PlayniteTabBtn";
                        }

                        var focusTarget = elements.FirstOrDefault(x => x.Name == focusName);
                        if (focusTarget != null)
                        {
                            focusTarget.Focus();
                            Keyboard.Focus(focusTarget);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to switch news tab.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] SwitchNewsTab failed.");
            }
        }

        public void SwitchQuickOptionsSection(int direction)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var app = Application.Current;
                        if (app == null)
                        {
                            return;
                        }

                        var elements = app.Windows
                            .OfType<Window>()
                            .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                            .ToList();

                        // Ordre réel de tes sections dans QuickOptionsView.xaml
                        string[] modeNames =
                        {
                    "ModeSystemInternal",
                    "ModeThemeInternal",
                    "ModeDetailsInternal",
                    "ModeDataInternal",
                    "ModeVisualInternal",
                    "ModeTrailerInternal",
                    "ModeAudioInternal",
                    "ModeLayoutInternal"
                };

                        string[] buttonNames =
                        {
                    "ButtonSystem",
                    "ButtonThemeOption",
                    "ButtonThemeOptionDetails",
                    "ButtonData",
                    "ButtonVisual",
                    "ButtonTrailer",
                    "ButtonAudio",
                    "ButtonLayout"
                };

                        int currentIndex = -1;

                        for (int i = 0; i < modeNames.Length; i++)
                        {
                            var mode = elements
                                .FirstOrDefault(x => x.Name == modeNames[i]) as ToggleButton;

                            if (mode != null && mode.IsChecked == true)
                            {
                                currentIndex = i;
                                break;
                            }
                        }

                        if (currentIndex < 0)
                        {
                            currentIndex = 0;
                        }

                        int nextIndex = currentIndex + direction;

                        if (nextIndex < 0)
                        {
                            nextIndex = modeNames.Length - 1;
                        }
                        else if (nextIndex >= modeNames.Length)
                        {
                            nextIndex = 0;
                        }

                        string nextButtonName = buttonNames[nextIndex];

                        var button = elements
                            .FirstOrDefault(x => x.Name == nextButtonName) as ButtonBase;

                        if (button == null)
                        {
                            logger.Warn("[AnikiHelper] Quick Options section button not found: " + nextButtonName);
                            return;
                        }

                        button.Focus();
                        Keyboard.Focus(button);

                        // Tes sections changent via EventTrigger ButtonBase.Click,
                        // donc on simule le clic.
                        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to switch Quick Options section.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] SwitchQuickOptionsSection failed.");
            }
        }

        public void OpenLockScreenFromQuickAccess()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Ferme le Quick Access avant d'ouvrir la vue login
                        CloseTopWindow();

                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var app = Application.Current;
                                if (app == null)
                                {
                                    return;
                                }

                                var lockScreen = app.Windows
                                    .OfType<Window>()
                                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                                    .FirstOrDefault(x => x.Name == "LockScreen") as System.Windows.Controls.Primitives.ToggleButton;

                                if (lockScreen == null)
                                {
                                    logger.Warn("[AnikiHelper] LockScreen was not found.");
                                    return;
                                }

                                lockScreen.IsChecked = true;

                                if (lockScreen.Command != null && lockScreen.Command.CanExecute(lockScreen.CommandParameter))
                                {
                                    lockScreen.Command.Execute(lockScreen.CommandParameter);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Failed to trigger LockScreen.");
                            }
                        }), DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] OpenLockScreenFromQuickAccess inner failed.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenLockScreenFromQuickAccess failed.");
            }
        }

        public void OpenPowerMenuFromQuickAccess()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Ferme la fenêtre Quick Access avant d'ouvrir le menu Power
                        CloseTopWindow();

                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var app = Application.Current;
                                if (app == null)
                                {
                                    return;
                                }

                                var buttonPower = app.Windows
                                    .OfType<Window>()
                                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                                    .FirstOrDefault(x => x.Name == "ButtonPower") as System.Windows.Controls.Primitives.ToggleButton;

                                if (buttonPower == null)
                                {
                                    logger.Warn("[AnikiHelper] ButtonPower was not found.");
                                    return;
                                }

                                // Dans ton thème : IsChecked=False ouvre le menu Power
                                buttonPower.IsChecked = false;

                                var exitButton = app.Windows
                                    .OfType<Window>()
                                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                                    .FirstOrDefault(x => x.Name == "ExitCommand");

                                if (exitButton != null)
                                {
                                    exitButton.Focus();
                                    Keyboard.Focus(exitButton);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Failed to open Power menu.");
                            }
                        }), DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] OpenPowerMenuFromQuickAccess inner failed.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenPowerMenuFromQuickAccess failed.");
            }
        }

        public void OpenExternalClientsFromHelpMenu()
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var appAssembly = Application.Current.GetType().Assembly;

                        var fullscreenVm = Application.Current.MainWindow?.DataContext;
                        if (fullscreenVm == null)
                        {
                            logger.Warn("[AnikiHelper] Fullscreen main DataContext not found.");
                            return;
                        }

                        var windowFactoryType = appAssembly.GetType("Playnite.FullscreenApp.Windows.GameClientsMenuWindowFactory");
                        var viewModelType = appAssembly.GetType("Playnite.FullscreenApp.ViewModels.GameClientsMenuViewModel");

                        if (windowFactoryType == null || viewModelType == null)
                        {
                            logger.Warn("[AnikiHelper] GameClientsMenu types not found.");
                            return;
                        }

                        var windowFactory = Activator.CreateInstance(windowFactoryType);
                        var vm = Activator.CreateInstance(viewModelType, windowFactory, fullscreenVm);

                        var openView = viewModelType.GetMethod("OpenView");
                        openView?.Invoke(vm, null);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to open external clients menu.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenExternalClientsFromHelpMenu failed.");
            }
        }

        public void OpenRandomGameFromQuickAccess()
        {
            OpenRandomGameDirect();
        }

        public void UpdateGameLibraryFromQuickAccess()
        {
            UpdateGameLibraryDirect();
        }

        public void CloseHubToLibraryFromShortcut()
        {
            try
            {
                var dispatcher = PlayniteApi?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        if (Settings == null)
                        {
                            return;
                        }

                        // Si le Hub est déjà fermé, inutile de relancer la fermeture
                        if (!Settings.IsWelcomeHubOpen)
                        {
                            return;
                        }

                        // Lance l'état de fermeture pour laisser jouer tes animations XAML
                        StartClosingWelcomeHub();

                        await Task.Delay(180);

                        // Ferme réellement le Hub
                        FinishClosingWelcomeHub();

                        await Task.Delay(80);

                        // Redonne le focus à la liste de jeux si elle existe
                        var gameList = Application.Current.Windows
                            .OfType<Window>()
                            .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                            .FirstOrDefault(x => x.Name == "PART_ListGameItems");

                        if (gameList != null)
                        {
                            gameList.Focus();
                            Keyboard.Focus(gameList);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to close Hub to Library.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] CloseHubToLibraryFromShortcut failed.");
            }
        }

        public async void OpenPlayniteMainMenuFromShortcut()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                await dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        if (Settings != null)
                        {
                            Settings.IsQuickAccessToMainMenuDimActive = true;
                        }

                        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                        CloseTopWindow();

                        await dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var app = Application.Current;
                                if (app == null)
                                {
                                    return;
                                }

                                var button = app.Windows
                                    .OfType<Window>()
                                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                                    .FirstOrDefault(x => x.Name == "PART_ButtonMainMenu") as ButtonBase;

                                if (button == null)
                                {
                                    logger.Warn("[AnikiHelper] PART_ButtonMainMenu was not found.");

                                    if (Settings != null)
                                    {
                                        Settings.IsQuickAccessToMainMenuDimActive = false;
                                    }

                                    return;
                                }

                                if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                                {
                                    button.Command.Execute(button.CommandParameter);
                                }
                                else
                                {
                                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                                }

                                var timer = new DispatcherTimer
                                {
                                    Interval = TimeSpan.FromMilliseconds(800)
                                };

                                timer.Tick += (s, e) =>
                                {
                                    timer.Stop();

                                    if (Settings != null)
                                    {
                                        Settings.IsQuickAccessToMainMenuDimActive = false;
                                    }
                                };

                                timer.Start();
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Failed to trigger PART_ButtonMainMenu.");

                                if (Settings != null)
                                {
                                    Settings.IsQuickAccessToMainMenuDimActive = false;
                                }
                            }
                        }, DispatcherPriority.Normal);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] OpenPlayniteMainMenuFromShortcut inner failed.");

                        if (Settings != null)
                        {
                            Settings.IsQuickAccessToMainMenuDimActive = false;
                        }
                    }
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenPlayniteMainMenuFromShortcut failed.");

                if (Settings != null)
                {
                    Settings.IsQuickAccessToMainMenuDimActive = false;
                }
            }
        }

        public async void OpenPlayniteSettingsFromShortcut()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                await dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        CloseTopWindow();

                        var appAssembly = Application.Current.GetType().Assembly;
                        var fullscreenVm = Application.Current.MainWindow?.DataContext;

                        if (fullscreenVm == null)
                        {
                            logger.Warn("[AnikiHelper] Fullscreen main DataContext not found.");
                            return;
                        }

                        var windowFactoryType = appAssembly.GetType("Playnite.FullscreenApp.Windows.SettingsWindowFactory");
                        var viewModelType = appAssembly.GetType("Playnite.FullscreenApp.ViewModels.SettingsViewModel");

                        if (windowFactoryType == null || viewModelType == null)
                        {
                            logger.Warn("[AnikiHelper] Settings fullscreen types not found.");
                            return;
                        }

                        var windowFactory = Activator.CreateInstance(windowFactoryType);
                        var vm = Activator.CreateInstance(viewModelType, windowFactory, fullscreenVm);

                        var openView = viewModelType.GetMethod("OpenView");
                        openView?.Invoke(vm, null);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to open fullscreen Settings.");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenPlayniteSettingsFromShortcut failed.");
            }
        }

        private void OpenRandomGameDirect()
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        CloseTopWindow();

                        var fullscreenVm = Application.Current.MainWindow?.DataContext;
                        if (fullscreenVm == null)
                        {
                            logger.Warn("[AnikiHelper] Fullscreen main DataContext not found.");
                            return;
                        }

                        var method = fullscreenVm.GetType().GetMethod("SelectRandomGame");
                        if (method == null)
                        {
                            logger.Warn("[AnikiHelper] SelectRandomGame method not found.");
                            return;
                        }

                        method.Invoke(fullscreenVm, null);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to open random game.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] OpenRandomGameDirect failed.");
            }
        }

        private void UpdateGameLibraryDirect()
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        CloseTopWindow();

                        var fullscreenVm = Application.Current.MainWindow?.DataContext;
                        if (fullscreenVm == null)
                        {
                            logger.Warn("[AnikiHelper] Fullscreen main DataContext not found.");
                            return;
                        }

                        var appSettingsProp = fullscreenVm.GetType().GetProperty("AppSettings");
                        var appSettings = appSettingsProp?.GetValue(fullscreenVm);

                        var downloadMetadataProp = appSettings?.GetType().GetProperty("DownloadMetadataOnImport");
                        var downloadMetadata = downloadMetadataProp?.GetValue(appSettings) is bool b && b;

                        var method = fullscreenVm.GetType().GetMethod(
                            "UpdateLibrary",
                            new Type[] { typeof(bool), typeof(bool), typeof(bool) });

                        if (method == null)
                        {
                            logger.Warn("[AnikiHelper] UpdateLibrary(bool,bool,bool) method not found.");
                            return;
                        }

                        var result = method.Invoke(fullscreenVm, new object[]
                        {
                    downloadMetadata,
                    true,
                    true
                        }) as Task;

                        if (result != null)
                        {
                            await result;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to update library.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] UpdateGameLibraryDirect failed.");
            }
        }

        public void OpenHelpLink(string linkKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(linkKey))
                {
                    return;
                }

                string url = null;

                switch (linkKey)
                {
                    case "Guide":
                        url = "https://github.com/Mike-Aniki/Aniki-ReMake/wiki/Guide";
                        break;

                    case "Issues":
                        url = "https://github.com/Mike-Aniki/Aniki-ReMake/issues";
                        break;

                    case "GitHub":
                        url = "https://github.com/Mike-Aniki/Aniki-ReMake";
                        break;

                    case "Discord":
                        url = "https://discord.gg/BrtABqe";
                        break;

                    case "Youtube":
                        url = "https://www.youtube.com/@Mike-Aniki";
                        break;

                    case "AboutMe":
                        url = "https://mike-aniki.github.io/Aniki-ReMake/";
                        break;

                    case "Kofi":
                        url = "https://ko-fi.com/mikeaniki";
                        break;
                }

                if (string.IsNullOrWhiteSpace(url))
                {
                    logger.Warn($"[AnikiHelper] Unknown help link key: {linkKey}");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper] Failed to open help link: {linkKey}");
            }
        }

        private void AddControllerSupportSafe()
        {
            try
            {
                AddCustomElementSupport(new AddCustomElementSupportArgs
                {
                    SourceName = "AnikiHelper",
                    ElementList = new List<string>
                    {
                        "ControllerCommands",
                        "ControllerShortcuts",
                        "ControllerOverride",
                        "GlobalControllerShortcuts",
                        "GlobalControllerShortcutsHub",
                        "GlobalControllerOverride"
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] AddCustomElementSupport for controller shortcuts is unavailable.");
            }
        }

        private void AddSettingsSupportSafe(string sourceName, string settingsRootPropertyName)
        {
            try
            {
                AddSettingsSupport(new AddSettingsSupportArgs
                {
                    SourceName = sourceName,
                    SettingsRoot = settingsRootPropertyName
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "AddSettingsSupport indisponible sur cette version de Playnite.");
            }
        }

        private Version GetPluginVersionFromExtensionYaml()
        {
            try
            {
                string pluginFolder = Path.GetDirectoryName(GetType().Assembly.Location);
                string yamlPath = Path.Combine(pluginFolder, "extension.yaml");

                if (!File.Exists(yamlPath))
                {
                    logger.Warn("[AnikiHelper] extension.yaml not found for version check.");
                    return null;
                }

                string yaml = File.ReadAllText(yamlPath);

                var match = Regex.Match(
                    yaml,
                    @"^\s*Version\s*:\s*[""']?(?<version>[0-9]+(?:\.[0-9]+){1,3})[""']?\s*$",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    logger.Warn("[AnikiHelper] Version field not found in extension.yaml.");
                    return null;
                }

                string versionText = match.Groups["version"].Value;

                if (Version.TryParse(versionText, out var version))
                {
                    return version;
                }

                logger.Warn($"[AnikiHelper] Unable to parse extension.yaml version: {versionText}");
                return null;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to read extension.yaml version.");
                return null;
            }
        }

        public void UpdateVersionCheckState()
        {
            try
            {
                var currentVersion = GetPluginVersionFromExtensionYaml()
                    ?? GetType().Assembly.GetName().Version;

                Settings.InstalledPluginVersion = currentVersion != null
                    ? $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}"
                    : "";

                Settings.VersionCheckReady = true;
                Settings.IsPluginUpdateRequired = false;
                Settings.RequiredAnikiHelperVersion = "";

                var requiredResource = Application.Current.TryFindResource("RequiredAnikiHelperVersion") as string;

                if (string.IsNullOrWhiteSpace(requiredResource))
                {
                    SaveSettingsSafe();
                    return;
                }

                Settings.RequiredAnikiHelperVersion = requiredResource.Trim();

                if (Version.TryParse(Settings.RequiredAnikiHelperVersion, out var requiredVersion) &&
                    currentVersion != null)
                {
                    Settings.IsPluginUpdateRequired = currentVersion < requiredVersion;

                }
                else
                {
                    logger.Warn("[AnikiHelper] Version check failed: unable to parse required or installed version.");
                }

                SaveSettingsSafe();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] UpdateVersionCheckState failed.");

                try
                {
                    Settings.VersionCheckReady = true;
                    Settings.IsPluginUpdateRequired = false;
                    SaveSettingsSafe();
                }
                catch { }
            }
        }

        public void SetAnikiThemeOption(string parameter)
        {
            anikiThemeSettingsService?.SetOptionFromParameter(parameter);
        }

        public void ToggleAnikiThemeOption(string parameter)
        {
            anikiThemeSettingsService?.ToggleOptionFromParameter(parameter);
        }

        public void SelectAnikiThemePreset(string parameter)
        {
            anikiThemeSettingsService?.SelectPresetFromParameter(parameter);
        }

        public void ShowAnikiThemePresetPreview(string presetId)
        {
            anikiThemeSettingsService?.ShowPreview(presetId);
        }

        public void HideAnikiThemePresetPreview()
        {
            anikiThemeSettingsService?.HidePreview();
        }

        public void ReloadAnikiThemeSettings()
        {
            anikiThemeSettingsService?.Reload();
        }

        public void SetAnikiThemeSettingsRestartRequiredAction(Action action)
        {
            anikiThemeSettingsService?.SetRestartRequiredAction(action);
        }

        public void ShowAnikiThemeSettingsRestartPromptIfNeeded()
        {
            anikiThemeSettingsService?.ShowRestartPromptIfNeeded();
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            try
            {
                var controlType = args.Name?.Split('_')[0];

                switch (controlType)
                {
                    case "ControllerCommands":
                        return new AnikiControllerCommandsControl();

                    case "ControllerShortcuts":
                        return new AnikiControllerShortcutControl(suppressDefaults: false, global: false);

                    case "ControllerOverride":
                        return new AnikiControllerShortcutControl(suppressDefaults: true, global: false);

                    case "GlobalControllerShortcuts":
                        return new AnikiControllerShortcutControl(suppressDefaults: false, global: true);

                    case "GlobalControllerShortcutsHub":
                        return new AnikiControllerShortcutControl(suppressDefaults: false, global: true);

                    case "GlobalControllerOverride":
                        return new AnikiControllerShortcutControl(suppressDefaults: true, global: true);

                    default:
                        throw new ArgumentException($"Unknown Aniki Helper controller control: {args.Name}");
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper] Failed to create controller control: {args?.Name}");
                return base.GetGameViewControl(args);
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsVM ?? (SettingsVM = new AnikiHelperSettingsViewModel(this));
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            var view = new AnikiHelperSettingsView
            {
                DataContext = SettingsVM ?? (SettingsVM = new AnikiHelperSettingsViewModel(this))
            };
            return view;
        }

        private void TryAskForSteamUpdateCacheOnStartup()
        {
            try
            {
                if (!Settings.SteamUpdatesScanEnabled)
                {
                    return;
                }

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                var cachePath = GetSteamUpdatesCachePath();
                if (File.Exists(cachePath) || !Settings.AskSteamUpdateCacheAtStartup)
                {
                    return;
                }

                var message = Loc(
                    "SteamInitCachePrompt_Message",
                    "Aniki Helper can create a one-time Steam update cache for your Steam games.\n\n" +
                    "This prevents the red \"new update\" icon from appearing the first time you visit each game.\n" +
                    "This scan may take some time on large libraries.\n\n" +
                    "Do you want to build the cache now?");

                var title = Loc(
                    "SteamInitCachePrompt_Title",
                    "Build Steam update cache?");


                var result = PlayniteApi.Dialogs.ShowMessage(
                    message,
                    title,
                    MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    Settings.AskSteamUpdateCacheAtStartup = false;
                    SaveSettingsSafe();

                    _ = InitializeSteamUpdatesCacheForAllGamesAsync();
                }
                else
                {
                    Settings.AskSteamUpdateCacheAtStartup = false;
                    SaveSettingsSafe();
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] TryAskForSteamUpdateCacheOnStartup failed.");
            }
        }

        private class WhatsNewPackage
        {
            public string Version { get; set; }
            public Dictionary<string, string> Title { get; set; }
            public Dictionary<string, string> Subtitle { get; set; }
            public List<WhatsNewSlide> Slides { get; set; }
        }

        private class WhatsNewSlide
        {
            public Dictionary<string, string> Title { get; set; }
            public Dictionary<string, string> Text { get; set; }
            public string Image { get; set; }
        }

        private string GetPlayniteLanguageCode()
        {
            var lang = PlayniteApi?.ApplicationSettings?.Language;

            if (string.IsNullOrWhiteSpace(lang))
            {
                return "en";
            }

            lang = lang.Trim().ToLowerInvariant();

            if (lang.StartsWith("fr"))
            {
                return "fr";
            }

            if (lang.StartsWith("es"))
            {
                return "es";
            }

            if (lang.StartsWith("de"))
            {
                return "de";
            }

            if (lang.StartsWith("it"))
            {
                return "it";
            }

            if (lang.StartsWith("pt"))
            {
                return "pt";
            }

            return "en";
        }

        private string ResolveWhatsNewText(Dictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var lang = GetPlayniteLanguageCode();

            if (values.TryGetValue(lang, out var localized) && !string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            if (values.TryGetValue("en", out var english) && !string.IsNullOrWhiteSpace(english))
            {
                return english;
            }

            return values.Values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }

        private async Task CheckWhatsNewAfterStartupAsync(bool forceOpen = false)
        {
            try
            {
                if (!isFullscreenMode || !IsAnikiThemeActive())
                {
                    return;
                }

                // Delay pour éviter de se lancer en même temps que l’intro / le hub
                if (!forceOpen)
                {
                    await Task.Delay(6000);
                }

                var themeRoot = Path.Combine(
                    PlayniteApi.Paths.ConfigurationPath,
                    "Themes",
                    "Fullscreen",
                    ShutdownThemeFolderName
                );

                var whatsNewDir = Path.Combine(themeRoot, "Extra", "WhatsNew");
                var jsonPath = Path.Combine(whatsNewDir, "whatsnew.json");

                if (!File.Exists(jsonPath))
                {
                    return;
                }

                var json = File.ReadAllText(jsonPath);
                var data = Serialization.FromJson<WhatsNewPackage>(json);

                if (data == null || string.IsNullOrWhiteSpace(data.Version))
                {
                    return;
                }

                if (!forceOpen &&
                    Version.TryParse(Settings.LastSeenWhatsNewVersion, out var lastSeenVersion) &&
                    Version.TryParse(data.Version, out var currentVersion) &&
                    currentVersion <= lastSeenVersion)
                {
                    return;
                }

                await OnUiAsync(() =>
                {
                    Settings.WhatsNewVersion = data.Version ?? string.Empty;
                    Settings.WhatsNewTitle = ResolveWhatsNewText(data.Title);
                    Settings.WhatsNewSubtitle = ResolveWhatsNewText(data.Subtitle);
                    Settings.WhatsNewSlides.Clear();

                    foreach (var slide in data.Slides ?? new List<WhatsNewSlide>())
                    {
                        if (string.IsNullOrWhiteSpace(slide.Image))
                        {
                            continue;
                        }

                        Settings.WhatsNewSlides.Add(new WhatsNewSlideItem
                        {
                            Title = ResolveWhatsNewText(slide.Title),
                            Text = ResolveWhatsNewText(slide.Text),
                            ImagePath = Path.Combine(whatsNewDir, slide.Image)
                        });
                    }

                    OpenChildWindow("WhatsNewWindowStyle");
                });

                if (!forceOpen)
                {
                    Settings.LastSeenWhatsNewVersion = data.Version;
                    SaveSettingsSafe();
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] CheckWhatsNewAfterStartupAsync failed.");
            }
        }

        private bool IsAnikiThemeActive()
        {
            try
            {
                return System.Windows.Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }

        // --- Helpers focus / autorisation scan Steam ---
        private bool IsMainWindowActive()
        {
            try
            {
                var win = System.Windows.Application.Current?.MainWindow;
                if (win == null)
                {
                    return true;
                }

                if (!win.IsVisible)
                    return false;

                if (win.WindowState == System.Windows.WindowState.Minimized)
                    return false;

                return win.IsActive;
            }
            catch
            {
                return true;
            }
        }

        private bool IsNamedElementVisible(string elementName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(elementName))
                {
                    return false;
                }

                var win = System.Windows.Application.Current?.MainWindow;
                if (win == null || !win.IsVisible)
                {
                    return false;
                }

                if (win.WindowState == System.Windows.WindowState.Minimized)
                {
                    return false;
                }

                var element = win
                    .FindVisualChildren<FrameworkElement>()
                    .FirstOrDefault(x => x.Name == elementName);

                if (element == null)
                {
                    return false;
                }

                return element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0;
            }
            catch
            {
                return false;
            }
        }

        private void NewsRotationTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                if (!IsMainWindowActive())
                {
                    return;
                }

                if (!IsNamedElementVisible("WelcomeHubNewsPanel"))
                {
                    return;
                }

                RotateLatestNewsIfNeeded();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] NewsRotationTimer_Tick failed.");
            }
        }

        private void SuggestedRotationTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                if (!IsMainWindowActive())
                {
                    return;
                }

                if (!IsNamedElementVisible("WelcomeHubSuggestedPanel"))
                {
                    return;
                }

                RotateSuggestedGamesIfNeeded();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] SuggestedRotationTimer_Tick failed.");
            }
        }

        private async Task StartSuggestedRotationWithDelayAsync(int delayMs)
        {
            try
            {
                await Task.Delay(delayMs);

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                suggestedRotationTimer?.Start();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] StartSuggestedRotationWithDelayAsync failed.");
            }
        }

        private bool IsSteamRecentScanAllowed()
        {
            bool enabled = false;
            OnUi(() => enabled = Settings.SteamUpdatesScanEnabled);
            if (!enabled)
                return false;


            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                return false;

            if (!IsMainWindowActive())
                return false;

            return true;
        }

        private Playnite.SDK.Models.Game GetOrSelectRefGameForToday(
    List<Playnite.SDK.Models.Game> games,
    out List<Playnite.SDK.Models.Game> top3)
        {
            top3 = new List<Playnite.SDK.Models.Game>();

            var s = Settings;
            if (games == null || games.Count == 0)
            {
                return null;
            }

            var today = DateTime.Now.Date;

            // Si on a déjà un RefGame choisi aujourd'hui, on le réutilise
            if (s.RefGameLastId != Guid.Empty && s.RefGameLastChangeDate.Date == today)
            {
                var cached = games.FirstOrDefault(g => g.Id == s.RefGameLastId)
                             ?? PlayniteApi.Database.Games.Get(s.RefGameLastId);

                if (cached != null)
                {
                    return cached;
                }

                // si jeu supprimé / plus dans la liste filtrée
                s.RefGameLastId = Guid.Empty;
            }

            var limit = DateTime.Now.AddDays(-14);

            // Top 3 : joué dans les 14 jours, tri Playtime -> PlayCount -> LastActivity
            var candidates = games
                .Where(g =>
                    g != null
                    && g.LastActivity.HasValue
                    && g.LastActivity.Value >= limit
                    && (g.Playtime > 0UL || g.PlayCount > 0UL))
                .OrderByDescending(g => g.Playtime)
                .ThenByDescending(g => g.PlayCount)
                .ThenByDescending(g => g.LastActivity)
                .ToList();

            top3 = candidates.Take(3).ToList();

            // RefGame = le dernier lancé parmi le Top 3
            Playnite.SDK.Models.Game refGame = null;

            if (top3.Count > 0)
            {
                refGame = top3
                    .OrderByDescending(g => g.LastActivity ?? DateTime.MinValue)
                    .FirstOrDefault();
            }

            // Fallback : dernier jeu joué
            if (refGame == null)
            {
                refGame = games
                    .Where(g => g.LastActivity.HasValue)
                    .OrderByDescending(g => g.LastActivity)
                    .FirstOrDefault();
            }

            // Sticky journée
            if (refGame != null)
            {
                s.RefGameLastId = refGame.Id;
                s.RefGameLastChangeDate = today;
                SaveSettingsSafe();
            }

            return refGame;
        }

        private string GetStartupVideoPath()
        {
            try
            {
                var configRoot = PlayniteApi?.Paths?.ConfigurationPath;
                if (!string.IsNullOrEmpty(configRoot))
                {
                    var themeVideo = Path.Combine(
                        configRoot,
                        "Themes",
                        "Fullscreen",
                        ShutdownThemeFolderName,
                        ShutdownVideoFolderName,
                        StartupVideoFileName);

                    if (File.Exists(themeVideo))
                    {
                        return themeVideo;
                    }
                }

                var appRoot = PlayniteApi?.Paths?.ApplicationPath;
                if (!string.IsNullOrEmpty(appRoot))
                {
                    var themeVideo = Path.Combine(
                        appRoot,
                        "Themes",
                        "Fullscreen",
                        ShutdownThemeFolderName,
                        ShutdownVideoFolderName,
                        StartupVideoFileName);

                    if (File.Exists(themeVideo))
                    {
                        return themeVideo;
                    }
                }

                return Path.Combine(GetDataRoot(), "ShutdownVideo", StartupVideoFileName);
            }
            catch
            {
                return Path.Combine(GetDataRoot(), "ShutdownVideo", StartupVideoFileName);
            }
        }

        private string GetShutdownVideoPath()
        {
            try
            {
                var configRoot = PlayniteApi?.Paths?.ConfigurationPath;
                if (!string.IsNullOrEmpty(configRoot))
                {
                    var themeVideo = Path.Combine(
                        configRoot,
                        "Themes",
                        "Fullscreen",
                        ShutdownThemeFolderName,
                        ShutdownVideoFolderName,
                        ShutdownVideoFileName);

                    if (File.Exists(themeVideo))
                    {
                        return themeVideo;
                    }
                }

                var appRoot = PlayniteApi?.Paths?.ApplicationPath;
                if (!string.IsNullOrEmpty(appRoot))
                {
                    var themeVideo = Path.Combine(
                        appRoot,
                        "Themes",
                        "Fullscreen",
                        ShutdownThemeFolderName,
                        ShutdownVideoFolderName,
                        ShutdownVideoFileName);

                    if (File.Exists(themeVideo))
                    {
                        return themeVideo;
                    }
                }

                return Path.Combine(GetDataRoot(), "ShutdownVideo", ShutdownVideoFileName);
            }
            catch
            {
                return Path.Combine(GetDataRoot(), "ShutdownVideo", ShutdownVideoFileName);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private bool TrySetUniPlaySongGameStartingPause(bool pause)
        {
            try
            {
                var app = Application.Current;

                if (app == null || app.Properties == null || !app.Properties.Contains("UniPlaySongPlugin"))
                {
                    return false;
                }

                var uniPlaySongPlugin = app.Properties["UniPlaySongPlugin"];

                if (uniPlaySongPlugin == null)
                {
                    return false;
                }

                var playbackServiceField = uniPlaySongPlugin.GetType().GetField(
                    "_playbackService",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                var playbackService = playbackServiceField?.GetValue(uniPlaySongPlugin);

                if (playbackService == null)
                {
                    return false;
                }

                var methodName = pause ? "AddPauseSource" : "RemovePauseSource";

                var method = playbackService.GetType().GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                if (method == null)
                {
                    return false;
                }

                var parameters = method.GetParameters();

                if (parameters.Length != 1)
                {
                    return false;
                }

                var pauseSourceType = parameters[0].ParameterType;
                var gameStartingSource = Enum.Parse(pauseSourceType, "GameStarting");

                method.Invoke(playbackService, new object[] { gameStartingSource });

                return true;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to control UniPlaySong GameStarting pause.");
                return false;
            }
        }

        private void HoldUniPlaySongGameStartingPause(Guid gameId)
        {
            if (TrySetUniPlaySongGameStartingPause(true))
            {
                uniPlaySongGameStartingPauseHeld = true;
                uniPlaySongGameStartingPauseGameId = gameId;
            }
        }

        private void ReleaseUniPlaySongGameStartingPause(Guid? gameId = null)
        {
            if (!uniPlaySongGameStartingPauseHeld)
            {
                return;
            }

            if (gameId.HasValue &&
                uniPlaySongGameStartingPauseGameId.HasValue &&
                uniPlaySongGameStartingPauseGameId.Value != gameId.Value)
            {
                return;
            }

            TrySetUniPlaySongGameStartingPause(false);

            uniPlaySongGameStartingPauseHeld = false;
            uniPlaySongGameStartingPauseGameId = null;
        }

        private void StartUniPlaySongLaunchFailureRelease(Guid gameId, int minimumDurationMs)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(500, minimumDurationMs) + 2500);

                    var game = PlayniteApi?.Database?.Games?.Get(gameId);

                    if (game == null || (!game.IsRunning && !game.IsLaunching))
                    {
                        ReleaseUniPlaySongGameStartingPause(gameId);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to release UniPlaySong launch failure pause.");
                }
            });
        }

        private bool IsPlayniteForegroundWindow()
        {
            try
            {
                var foregroundHandle = GetForegroundWindow();
                if (foregroundHandle == IntPtr.Zero)
                {
                    return false;
                }

                GetWindowThreadProcessId(foregroundHandle, out uint foregroundProcessId);
                return foregroundProcessId == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                return false;
            }
        }

        private bool IsKeyDown(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        private void DebugLogFocusState(string context)
        {
            try
            {
                if (Settings?.EnableDebugLogs != true)
                {
                    return;
                }

                var foregroundHandle = GetForegroundWindow();
                GetWindowThreadProcessId(foregroundHandle, out uint pid);

                var title = new StringBuilder(512);
                GetWindowText(foregroundHandle, title, title.Capacity);

                var processName = "unknown";

                try
                {
                    if (pid > 0)
                    {
                        processName = Process.GetProcessById((int)pid).ProcessName;
                    }
                }
                catch { }

                string wpfState = "WPF=unavailable";

                try
                {
                    var app = System.Windows.Application.Current;

                    if (app?.Dispatcher != null)
                    {
                        Action readWpf = () =>
                        {
                            var win = app.MainWindow;
                            var handle = win != null ? new WindowInteropHelper(win).Handle : IntPtr.Zero;

                            var focused = Keyboard.FocusedElement;
                            var focusedName = focused == null ? "NULL" : focused.GetType().FullName;

                            if (focused is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                            {
                                focusedName += $" Name='{fe.Name}'";
                            }

                            wpfState =
                                $"MainWindowHandle={handle}, " +
                                $"IsSameWindow={foregroundHandle == handle}, " +
                                $"WindowIsActive={win?.IsActive}, " +
                                $"WindowIsFocused={win?.IsFocused}, " +
                                $"KeyboardFocusWithin={win?.IsKeyboardFocusWithin}, " +
                                $"WindowState={win?.WindowState}, " +
                                $"WindowVisibility={win?.Visibility}, " +
                                $"FocusedElement='{focusedName}'";
                        };

                        if (app.Dispatcher.CheckAccess())
                        {
                            readWpf();
                        }
                        else
                        {
                            app.Dispatcher.Invoke(readWpf, DispatcherPriority.Send);
                        }
                    }
                }
                catch (Exception ex)
                {
                    wpfState = $"WPF_ERROR={ex.GetType().Name}: {ex.Message}";
                }

                DebugLog(
                    $"[AnikiHelper][FocusDebug][{context}] " +
                    $"ForegroundPid={pid}, " +
                    $"ForegroundProcess='{processName}', " +
                    $"ForegroundTitle='{title}', " +
                    $"IsPlayniteForegroundProcess={pid == (uint)Process.GetCurrentProcess().Id}, " +
                    $"{wpfState}, " +
                    $"Keys: LWin={IsKeyDown(0x5B)}, RWin={IsKeyDown(0x5C)}, Alt={IsKeyDown(0x12)}, Ctrl={IsKeyDown(0x11)}, Shift={IsKeyDown(0x10)}"
                );
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper][FocusDebug][{context}] Failed to read focus state.");
            }
        }

        private void StartPostGameFocusDebugTrace(string gameName)
        {
            if (Settings?.EnableDebugLogs != true)
            {
                return;
            }

            var sw = Stopwatch.StartNew();

            Task.Run(async () =>
            {
                for (int i = 0; i <= 40; i++)
                {
                    await Task.Delay(250);
                    DebugLogFocusState($"AfterGameStopped REAL={sw.ElapsedMilliseconds}ms | Game='{gameName}'");
                }
            });
        }

        private async Task<bool> WaitForPlayniteForegroundAsync(TimeSpan timeout)
        {
            var start = DateTime.Now;

            while (DateTime.Now - start < timeout)
            {
                if (IsPlayniteForegroundWindow())
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return false;
        }

        internal async Task ShowStartupVideoAsync()
        {
            if (startupVideoSequenceRunning)
            {
                return;
            }

            if (!(Settings?.StartupIntroVideoEnabled ?? true))
            {
                return;
            }

            if (!await WaitForPlayniteForegroundAsync(TimeSpan.FromSeconds(1)))
            {
                return;
            }

            startupVideoSequenceRunning = true;

            AnikiVideoOverlayWindow overlay = null;

            try
            {
                var videoPath = GetStartupVideoPath();

                if (!File.Exists(videoPath))
                {
                    return;
                }

                overlay = new AnikiVideoOverlayWindow(videoPath, StartupVideoFailSafeTimeout);
                overlay.Show();
                overlay.Activate();

                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                HidePlayniteWindowsForStartup(overlay);

                await Task.Delay(StartupVideoDuration);

                try
                {
                    overlay.Close();
                    overlay = null;
                }
                catch { }

                await RestorePlayniteWindowsAfterStartupAsync();

                await Task.Delay(120);

                await RestorePlayniteWindowsAfterStartupAsync();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] ShowStartupVideoAsync failed.");
            }
            finally
            {
                try
                {
                    overlay?.Close();
                }
                catch { }

                startupVideoSequenceRunning = false;
            }
        }

        internal async Task ShowShutdownVideoAndExitAsync()
        {
            if (shutdownVideoSequenceRunning)
            {
                return;
            }

            if (!(Settings?.ShutdownVideoEnabled ?? true))
            {
                Application.Current?.Shutdown();
                return;
            }

            shutdownVideoSequenceRunning = true;

            AnikiVideoOverlayWindow overlay = null;
            bool shutdownRequested = false;

            try
            {
                var videoPath = GetShutdownVideoPath();

                if (!File.Exists(videoPath))
                {
                    Application.Current?.Shutdown();
                    return;
                }

                overlay = new AnikiVideoOverlayWindow(videoPath, ShutdownVideoFailSafeTimeout);
                overlay.Show();
                overlay.Activate();

                await Task.Delay(ShutdownVideoDuration);

                HidePlayniteWindowsExcept(overlay);

                shutdownRequested = true;
                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] ShowShutdownVideoAndExitAsync failed.");
            }
            finally
            {
                if (!shutdownRequested)
                {
                    try
                    {
                        overlay?.Close();
                    }
                    catch { }
                }

                shutdownVideoSequenceRunning = false;
            }
        }

        private void HidePlayniteWindowsForStartup(Window overlay)
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    return;
                }

                foreach (Window win in app.Windows)
                {
                    if (ReferenceEquals(win, overlay))
                    {
                        continue;
                    }

                    try
                    {
                        win.Opacity = 0;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] HidePlayniteWindowsForStartup failed.");
            }
        }

        private async Task RestorePlayniteWindowsAfterStartupAsync()
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    return;
                }

                Window main = app.MainWindow;

                foreach (Window win in app.Windows)
                {
                    if (win is AnikiVideoOverlayWindow)
                    {
                        continue;
                    }

                    try
                    {
                        win.Opacity = 1;
                    }
                    catch { }
                }

                await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

                try
                {
                    if (main != null)
                    {
                        main.Activate();
                        main.Focus();

                        bool oldTopmost = main.Topmost;
                        main.Topmost = true;
                        main.Topmost = oldTopmost;
                    }
                }
                catch { }

                await Task.Delay(100);

                try
                {
                    if (main != null)
                    {
                        main.Activate();
                        main.Focus();
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] RestorePlayniteWindowsAfterStartupAsync failed.");
            }
        }

        private void HidePlayniteWindowsExcept(Window overlay)
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    return;
                }

                foreach (Window win in app.Windows)
                {
                    if (ReferenceEquals(win, overlay))
                    {
                        continue;
                    }

                    try
                    {
                        win.Opacity = 0;
                        win.Visibility = Visibility.Hidden;
                        win.ShowInTaskbar = false;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] HidePlayniteWindowsExcept failed.");
            }
        }

        #region Lifecycle

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            var swTotal = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            DebugLog("[AnikiHelper][OnApplicationStarted] START");

            try
            {
                base.OnApplicationStarted(args);
                DebugLog($"[AnikiHelper][OnApplicationStarted] base.OnApplicationStarted took {swTotal.ElapsedMilliseconds}ms");

                sw.Restart();
                eventSoundService.PlayApplicationStarted();
                DebugLog($"[AnikiHelper][OnApplicationStarted] PlayApplicationStarted took {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                var isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
                DebugLog($"[AnikiHelper][OnApplicationStarted] Fullscreen check took {sw.ElapsedMilliseconds}ms | fullscreen={isFullscreen}");

                if (!isFullscreen)
                {
                    DebugLog($"[AnikiHelper][OnApplicationStarted] STOP not fullscreen | total={swTotal.ElapsedMilliseconds}ms");
                    return;
                }

                sw.Restart();
                var isAnikiThemeActive = IsAnikiThemeActive();
                DebugLog($"[AnikiHelper][OnApplicationStarted] IsAnikiThemeActive took {sw.ElapsedMilliseconds}ms | active={isAnikiThemeActive}");

                try
                {
                    if (isAnikiThemeActive)
                    {
                        sw.Restart();
                        anikiThemeSettingsService?.LoadAndApply();
                        DebugLog($"[AnikiHelper][OnApplicationStarted] anikiThemeSettingsService.LoadAndApply took {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to load Aniki Theme Settings.");
                }

                try
                {
                    if (isAnikiThemeActive)
                    {
                        sw.Restart();
                        inGameOverlayService?.Start();
                        DebugLog($"[AnikiHelper][OnApplicationStarted] inGameOverlayService.Start took {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to start in-game overlay service.");
                }

                try
                {
                    if (isAnikiThemeActive)
                    {
                        sw.Restart();

                        Application.Current?.Dispatcher?.InvokeAsync(
                            () =>
                            {
                                horizontalFocusFixService?.Start();
                            },
                            DispatcherPriority.Loaded
                        );

                        DebugLog($"[AnikiHelper][OnApplicationStarted] HorizontalFocusFixService queued took {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to start horizontal focus fix service.");
                }

                try
                {
                    if (isAnikiThemeActive)
                    {
                        sw.Restart();
                        OnUi(() =>
                        {
                            Settings.IsWelcomeHubOpen = Settings.OpenWelcomeHubOnStartup;
                        });
                        DebugLog($"[AnikiHelper][OnApplicationStarted] Welcome hub OnUi took {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to initialize welcome hub startup state.");
                }

                sw.Restart();
                hubPage3CardsInitialized = false;
                DebugLog($"[AnikiHelper][OnApplicationStarted] hubPage3CardsInitialized reset took {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                EnsureMonthlySnapshotSafe();
                DebugLog($"[AnikiHelper][OnApplicationStarted] EnsureMonthlySnapshotSafe took {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                RecalcStatsSafe();
                DebugLog($"[AnikiHelper][OnApplicationStarted] RecalcStatsSafe took {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                PlayniteApi.Database.DatabaseOpened += (_, __) =>
                {
                    var swDb = Stopwatch.StartNew();
                    DebugLog("[AnikiHelper][DatabaseOpened] START");

                    hubPage3CardsInitialized = false;
                    DebugLog($"[AnikiHelper][DatabaseOpened] hubPage3CardsInitialized reset at {swDb.ElapsedMilliseconds}ms");

                    EnsureMonthlySnapshotSafe();
                    DebugLog($"[AnikiHelper][DatabaseOpened] EnsureMonthlySnapshotSafe at {swDb.ElapsedMilliseconds}ms");

                    RecalcStatsSafe();
                    DebugLog($"[AnikiHelper][DatabaseOpened] RecalcStatsSafe at {swDb.ElapsedMilliseconds}ms");

                    DebugLog($"[AnikiHelper][DatabaseOpened] END total={swDb.ElapsedMilliseconds}ms");
                };
                DebugLog($"[AnikiHelper][OnApplicationStarted] DatabaseOpened handler attach took {sw.ElapsedMilliseconds}ms");

                // Profile genre is intentionally cache-based.
                DebugLog("[AnikiHelper][OnApplicationStarted] Profile genre cache mode active.");

                try
                {
                    sw.Restart();
                    OnUi(() =>
                    {
                        Settings.SessionNotificationStamp = string.Empty;
                        Settings.SessionNotificationArmed = false;
                    });
                    DebugLog($"[AnikiHelper][OnApplicationStarted] Reset session notification OnUi took {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to reset session notification state.");
                }

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    AddonsUpdateStyler.Start();
                    DebugLog($"[AnikiHelper][OnApplicationStarted] AddonsUpdateStyler.Start took {sw.ElapsedMilliseconds}ms");
                }

                sw.Restart();
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                    () =>
                    {
                        var swUi = Stopwatch.StartNew();
                        DebugLog("[AnikiHelper][OnApplicationStarted][UI Loaded] Dynamic color init START");

                        EnsureDynamicColorCacheVersion();
                        DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] EnsureDynamicColorCacheVersion at {swUi.ElapsedMilliseconds}ms");

                        DynamicAuto.Init(PlayniteApi);
                        DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] DynamicAuto.Init at {swUi.ElapsedMilliseconds}ms");

                        DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] Dynamic color init END total={swUi.ElapsedMilliseconds}ms");
                    },
                    System.Windows.Threading.DispatcherPriority.Loaded
                );
                DebugLog($"[AnikiHelper][OnApplicationStarted] Dynamic color InvokeAsync queued took {sw.ElapsedMilliseconds}ms");

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                        () =>
                        {
                            var swUi = Stopwatch.StartNew();
                            SettingsWindowStyler.Start();
                            DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] SettingsWindowStyler.Start took {swUi.ElapsedMilliseconds}ms");
                        },
                        System.Windows.Threading.DispatcherPriority.Loaded
                    );
                    DebugLog($"[AnikiHelper][OnApplicationStarted] SettingsWindowStyler InvokeAsync queued took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    Application.Current?.Dispatcher?.InvokeAsync(
                        () =>
                        {
                            var swUi = Stopwatch.StartNew();

                            UpdateFullscreenAspectRatio();
                            DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] UpdateFullscreenAspectRatio took {swUi.ElapsedMilliseconds}ms");

                            var window = Application.Current?.MainWindow;

                            if (window != null)
                            {
                                window.SizeChanged -= OnFullscreenWindowSizeChanged;
                                window.SizeChanged += OnFullscreenWindowSizeChanged;
                            }

                            DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] Window SizeChanged hook total={swUi.ElapsedMilliseconds}ms | windowNull={window == null}");
                        },
                        DispatcherPriority.Loaded
                    );
                    DebugLog($"[AnikiHelper][OnApplicationStarted] Aspect ratio InvokeAsync queued took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                        () =>
                        {
                            var swUi = Stopwatch.StartNew();
                            FastScrollViewerService.Start();
                            DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] FastScrollViewerService.Start took {swUi.ElapsedMilliseconds}ms");
                        },
                        System.Windows.Threading.DispatcherPriority.Loaded
                    );
                    DebugLog($"[AnikiHelper][OnApplicationStarted] FastScrollViewerService InvokeAsync queued took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                        () =>
                        {
                            var swUi = Stopwatch.StartNew();
                            VisualPackBackgroundComposer.Start();
                            DebugLog($"[AnikiHelper][OnApplicationStarted][UI Loaded] VisualPackBackgroundComposer.Start took {swUi.ElapsedMilliseconds}ms");
                        },
                        System.Windows.Threading.DispatcherPriority.Loaded
                    );
                    DebugLog($"[AnikiHelper][OnApplicationStarted] VisualPackBackgroundComposer InvokeAsync queued took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive && Settings.ShutdownVideoEnabled)
                {
                    sw.Restart();
                    FullscreenShutdownVideoHook.Start(this);
                    DebugLog($"[AnikiHelper][OnApplicationStarted] FullscreenShutdownVideoHook.Start took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive && Settings.StartupIntroVideoEnabled)
                {
                    sw.Restart();
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                        async () =>
                        {
                            var swUi = Stopwatch.StartNew();
                            DebugLog("[AnikiHelper][OnApplicationStarted][StartupVideo] START");

                            try
                            {
                                await ShowStartupVideoAsync();
                                DebugLog($"[AnikiHelper][OnApplicationStarted][StartupVideo] ShowStartupVideoAsync finished total={swUi.ElapsedMilliseconds}ms");
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Startup video launch failed.");
                            }
                        },
                        System.Windows.Threading.DispatcherPriority.Send
                    );
                    DebugLog($"[AnikiHelper][OnApplicationStarted] Startup video InvokeAsync queued took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    newsRotationTimer?.Start();
                    DebugLog($"[AnikiHelper][OnApplicationStarted] newsRotationTimer.Start took {sw.ElapsedMilliseconds}ms");

                    sw.Restart();
                    _ = StartSuggestedRotationWithDelayAsync(3000);
                    DebugLog($"[AnikiHelper][OnApplicationStarted] StartSuggestedRotationWithDelayAsync launch took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    try
                    {
                        sw.Restart();
                        LoadNewsFromCacheIfNeeded();
                        DebugLog($"[AnikiHelper][OnApplicationStarted] LoadNewsFromCacheIfNeeded took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] LoadNewsFromCacheIfNeeded failed.");
                    }

                    try
                    {
                        sw.Restart();
                        RefreshSteamRecentUpdatesFromCache();
                        DebugLog($"[AnikiHelper][OnApplicationStarted] RefreshSteamRecentUpdatesFromCache took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] RefreshSteamRecentUpdatesFromCache failed.");
                    }

                    if (Settings.NewsScanEnabled)
                    {
                        try
                        {
                            sw.Restart();
                            _ = ScheduleGlobalSteamNewsRefreshAsync();
                            DebugLog($"[AnikiHelper][OnApplicationStarted] ScheduleGlobalSteamNewsRefreshAsync launch took {sw.ElapsedMilliseconds}ms");
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "[AnikiHelper] ScheduleGlobalSteamNewsRefreshAsync failed.");
                        }
                    }

                    try
                    {
                        sw.Restart();
                        _ = SchedulePlayniteNewsRefreshAsync();
                        DebugLog($"[AnikiHelper][OnApplicationStarted] SchedulePlayniteNewsRefreshAsync launch took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] SchedulePlayniteNewsRefreshAsync failed.");
                    }
                }

                try
                {
                    sw.Restart();
                    OnUi(() =>
                    {
                        var swUi = Stopwatch.StartNew();

                        var rand = new Random();
                        const int max = 41;

                        int pick;
                        if (Settings.LastLoginRandomIndex >= 1 && Settings.LastLoginRandomIndex <= max && max > 1)
                        {
                            do { pick = rand.Next(1, max + 1); } while (pick == Settings.LastLoginRandomIndex);
                        }
                        else
                        {
                            pick = rand.Next(1, max + 1);
                        }

                        Settings.LoginRandomIndex = pick;
                        Settings.LastLoginRandomIndex = pick;

                        DebugLog($"[AnikiHelper][OnApplicationStarted][UI] Random login pick took {swUi.ElapsedMilliseconds}ms | pick={pick}");
                    });
                    DebugLog($"[AnikiHelper][OnApplicationStarted] Random login OnUi took {sw.ElapsedMilliseconds}ms");

                    sw.Restart();
                    SaveSettingsSafe();
                    DebugLog($"[AnikiHelper][OnApplicationStarted] SaveSettingsSafe after random login took {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Random login screen init failed.");
                }

                try
                {
                    if (isAnikiThemeActive)
                    {
                        sw.Restart();
                        TryAskForSteamUpdateCacheOnStartup();
                        DebugLog($"[AnikiHelper][OnApplicationStarted] TryAskForSteamUpdateCacheOnStartup took {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] TryAskForSteamUpdateCacheOnStartup failed.");
                }

                if (isAnikiThemeActive)
                {
                    try
                    {
                        sw.Restart();
                        _ = ScheduleSteamRecentUpdatesScanAsync(30);
                        DebugLog($"[AnikiHelper][OnApplicationStarted] ScheduleSteamRecentUpdatesScanAsync launch took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] ScheduleSteamRecentUpdatesScanAsync failed.");
                    }
                }

                try
                {
                    sw.Restart();
                    _ = CheckRequiredPluginVersionAfterFullscreenStartupAsync();
                    DebugLog($"[AnikiHelper][OnApplicationStarted] CheckRequiredPluginVersionAfterFullscreenStartupAsync launch took {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] CheckRequiredPluginVersionAfterFullscreenStartupAsync failed.");
                }

                try
                {
                    sw.Restart();
                    _ = CheckWhatsNewAfterStartupAsync();
                    DebugLog($"[AnikiHelper][OnApplicationStarted] CheckWhatsNewAfterStartupAsync launch took {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] CheckWhatsNewAfterStartupAsync failed.");
                }

                DebugLog($"[AnikiHelper][OnApplicationStarted] END total={swTotal.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper][OnApplicationStarted] FATAL ERROR after {swTotal.ElapsedMilliseconds}ms");
            }
        }

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            var allowInGameOverlay =
                PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen &&
                IsAnikiThemeActive();

            if (allowInGameOverlay)
            {

                if (inGameOverlayService != null && inGameOverlayService.HandleControllerButtonStateChanged(args))
                {
                    return;
                }

                if (inGameOverlayService != null && inGameOverlayService.IsGameRunning)
                {
                    return;
                }
            }

            AnikiControllerInput.SetState(args);

            if (args.Button == ControllerInput.B && args.State == ControllerInputState.Pressed)
            {
                if (anikiWindowManager != null && anikiWindowManager.IsTopWindowActive())
                {
                    if (anikiWindowManager.CloseTopWindow())
                    {
                        return;
                    }
                }
            }

            base.OnControllerButtonStateChanged(args);
        }

        private async Task CheckRequiredPluginVersionAfterFullscreenStartupAsync()
        {
            try
            {
                if (!isFullscreenMode)
                {
                    return;
                }

                await Task.Delay(5000);

                await OnUiAsync(() =>
                {
                    UpdateVersionCheckState();
                }, DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Version check after fullscreen startup failed.");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            eventSoundService.PlayApplicationStopped();

            try { steamUpdateTimer?.Stop(); } catch { }
            try { steamUpdatesCacheFlushTimer?.Stop(); } catch { }
            try { steamGameNewsCacheFlushTimer?.Stop(); } catch { }
            try { newsRotationTimer?.Stop(); } catch { }
            try { suggestedRotationTimer?.Stop(); } catch { }
            try { navigationSettleTimer?.Stop(); } catch { }

            try
            {
                steamUpdateCts?.Cancel();
                steamUpdateCts?.Dispose();
            }
            catch { }

            try
            {
                steamGameNewsCts?.Cancel();
                steamGameNewsCts?.Dispose();
            }
            catch { }

            try
            {
                FlushSteamGameNewsCacheIfNeeded();
            }
            catch { }

            try { FullscreenShutdownVideoHook.Stop(); } catch { }
            try { inGameOverlayService?.Stop(); } catch { }

            base.OnApplicationStopped(args);
        }


        private async Task ScheduleSteamRecentUpdatesScanAsync(int maxGames)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(9));

                if (!IsSteamRecentScanAllowed())
                {
                    return;
                }

                await CheckSteamUpdatesForRecentGamesAsync(maxGames);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] ScheduleSteamRecentUpdatesScanAsync failed.");
            }
        }

        private async Task ScheduleGlobalSteamNewsRefreshAsync()
        {
            try
            {
                // Attente de 6s après démarrage, silencieuse
                await Task.Delay(6000);

                if (!Settings.NewsScanEnabled)
                {
                    return;
                }

                await RefreshGlobalSteamNewsAsync(force: false);
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "[NEWS ERROR] ScheduleGlobalSteamNewsRefreshAsync failed.");
            }
        }

        // Schedules a Playnite Actu scan (GitHub feed) ~8 seconds after startup
        private async Task SchedulePlayniteNewsRefreshAsync()
        {
            try
            {
                await Task.Delay(8000);
                await RefreshPlayniteNewsAsync(force: false, silent: true);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[PlayniteNews] SchedulePlayniteNewsRefreshAsync failed.");
            }
        }

        public override void OnFullscreenViewChanged(OnFullscreenViewChangedArgs args)
        {
            DebugLog(
                $"[AnikiHelper][FullscreenViewChanged][START] " +
                $"NewView={args?.NewView}"
            );

            DebugLogFocusState("FullscreenViewChanged START");

            try
            {
                base.OnFullscreenViewChanged(args);

                DebugLogFocusState("After base.OnFullscreenViewChanged");

                var isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;

                if (!isFullscreen)
                {
                    DebugLog("[AnikiHelper][FullscreenViewChanged][STOP] Playnite is not in Fullscreen mode.");
                    return;
                }

                var isDetailsView = args?.NewView == FullscreenView.Details;
                var isAnikiTheme = IsAnikiThemeActive();

                DebugLog(
                    $"[AnikiHelper][FullscreenViewChanged][State] " +
                    $"Fullscreen={isFullscreen}, " +
                    $"IsDetailsView={isDetailsView}, " +
                    $"AnikiTheme={isAnikiTheme}"
                );

                if (!isDetailsView)
                {
                    DebugLog(
                        $"[AnikiHelper][FullscreenViewChanged][LeavingDetails] " +
                        $"NewView={args?.NewView}. Clearing details state."
                    );

                    isInFullscreenDetailsView = false;
                    lastDetailsMediaGameId = Guid.Empty;

                    try
                    {
                        steamUpdateCts?.Cancel();
                        steamUpdateCts?.Dispose();
                        steamUpdateCts = null;

                        DebugLog("[AnikiHelper][FullscreenViewChanged][SteamTasks] Existing Steam details task cancelled.");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper][FullscreenViewChanged] Failed to cancel Steam details task while leaving details.");
                    }

                    ResetSteamUpdate();
                    ResetSteamGameNews();
                    ResetSteamPlayerCount();

                    Settings?.ClearCurrentGameMediaState();

                    DebugLog("[AnikiHelper][FullscreenViewChanged][RESULT] Details state cleared because new view is not Details.");
                    return;
                }

                if (!isAnikiTheme)
                {
                    DebugLog("[AnikiHelper][FullscreenViewChanged][STOP] Details view opened, but Aniki theme is not active. Clearing state.");

                    isInFullscreenDetailsView = false;
                    lastDetailsMediaGameId = Guid.Empty;

                    try
                    {
                        steamUpdateCts?.Cancel();
                        steamUpdateCts?.Dispose();
                        steamUpdateCts = null;

                        DebugLog("[AnikiHelper][FullscreenViewChanged][SteamTasks] Existing Steam details task cancelled because theme is not active.");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper][FullscreenViewChanged] Failed to cancel Steam details task when theme inactive.");
                    }

                    ResetSteamUpdate();
                    ResetSteamGameNews();
                    ResetSteamPlayerCount();

                    Settings?.ClearCurrentGameMediaState();

                    DebugLog("[AnikiHelper][FullscreenViewChanged][RESULT] State cleared because Aniki theme is not active.");
                    return;
                }

                isInFullscreenDetailsView = true;

                var game = PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();

                if (game == null)
                {
                    DebugLog("[AnikiHelper][FullscreenViewChanged][STOP] Details view opened but no selected game found.");

                    lastDetailsMediaGameId = Guid.Empty;

                    ResetSteamUpdate();
                    ResetSteamGameNews();
                    ResetSteamPlayerCount();

                    Settings?.ClearCurrentGameMediaState();

                    DebugLog("[AnikiHelper][FullscreenViewChanged][RESULT] Details state cleared because selected game is null.");
                    return;
                }

                lastDetailsMediaGameId = game.Id;

                DebugLog(
                    $"[AnikiHelper][FullscreenViewChanged][Details] " +
                    $"SelectedGame='{game.Name}', Id={game.Id}"
                );

                Settings?.ClearCurrentGameMediaState();

                DebugLog("[AnikiHelper][FullscreenViewChanged][Media] Current game media state cleared. Screenshots will load only when the screenshot window is opened.");

                try
                {
                    steamUpdateCts?.Cancel();
                    steamUpdateCts?.Dispose();

                    DebugLog("[AnikiHelper][FullscreenViewChanged][SteamTasks] Previous Steam details task cancelled before starting a new one.");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper][FullscreenViewChanged] Failed to cancel previous Steam details task.");
                }

                steamUpdateCts = new CancellationTokenSource();
                var ct = steamUpdateCts.Token;

                var playerCountEnabled = Settings?.SteamPlayerCountEnabled == true;
                var steamUpdatesEnabled = Settings?.SteamUpdatesScanEnabled == true;

                DebugLog(
                    $"[AnikiHelper][FullscreenViewChanged][SteamDetails] " +
                    $"PlayerCountEnabled={playerCountEnabled}, " +
                    $"SteamUpdatesEnabled={steamUpdatesEnabled}, " +
                    $"Game='{game.Name}'"
                );

                _ = Task.Run(async () =>
                {
                    DebugLog(
                        $"[AnikiHelper][SteamDetailsTask][START] " +
                        $"Game='{game.Name}', Id={game.Id}"
                    );

                    try
                    {
                        await Task.Delay(500, ct);

                        if (ct.IsCancellationRequested)
                        {
                            DebugLog($"[AnikiHelper][SteamDetailsTask][CANCELLED] Cancelled before player count. Game='{game.Name}'");
                            return;
                        }

                        if (Settings != null && Settings.SteamPlayerCountEnabled)
                        {
                            DebugLog($"[AnikiHelper][SteamDetailsTask][PlayerCount] Updating player count. Game='{game.Name}'");
                            await UpdateSteamPlayerCountForGameAsync(game);
                            DebugLog($"[AnikiHelper][SteamDetailsTask][PlayerCount] Update finished. Game='{game.Name}'");
                        }
                        else
                        {
                            DebugLog($"[AnikiHelper][SteamDetailsTask][PlayerCount] Skipped. Setting disabled or Settings null. Game='{game.Name}'");
                        }

                        await Task.Delay(1000, ct);

                        if (ct.IsCancellationRequested)
                        {
                            DebugLog($"[AnikiHelper][SteamDetailsTask][CANCELLED] Cancelled before Steam update scan. Game='{game.Name}'");
                            return;
                        }

                        if (Settings != null && Settings.SteamUpdatesScanEnabled)
                        {
                            DebugLog($"[AnikiHelper][SteamDetailsTask][Updates] Updating Steam update data. Game='{game.Name}'");
                            await UpdateSteamUpdateForGameAsync(game, ct);
                            DebugLog($"[AnikiHelper][SteamDetailsTask][Updates] Update finished. Game='{game.Name}'");
                        }
                        else
                        {
                            DebugLog($"[AnikiHelper][SteamDetailsTask][Updates] Skipped. Setting disabled or Settings null. Game='{game.Name}'");
                        }

                        DebugLog($"[AnikiHelper][SteamDetailsTask][END] Game='{game.Name}'");
                    }
                    catch (TaskCanceledException)
                    {
                        DebugLog($"[AnikiHelper][SteamDetailsTask][CANCELLED] TaskCanceledException. Game='{game.Name}'");
                    }
                    catch (OperationCanceledException)
                    {
                        DebugLog($"[AnikiHelper][SteamDetailsTask][CANCELLED] OperationCanceledException. Game='{game.Name}'");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"[AnikiHelper][SteamDetailsTask][ERROR] Failed to scan Steam details data. Game='{game.Name}'");
                    }
                });

                DebugLog($"[AnikiHelper][FullscreenViewChanged][RESULT] Steam details task scheduled. Game='{game.Name}'");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][FullscreenViewChanged][ERROR] OnFullscreenViewChanged failed.");
            }
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            base.OnGameSelected(args);

            Interlocked.Exchange(ref lastSteamUpdateUserActivityTicks, Environment.TickCount);

            var g = args?.NewValue?.FirstOrDefault();

            DynamicAuto.NotifyGameSelected(g);

            if (g == null)
            {
                try
                {
                    if (Settings != null)
                    {
                        Settings.IsFastNavigating = false;
                    }

                    navigationSettleTimer?.Stop();
                }
                catch { }

                ResetSteamUpdate();
                ResetSteamGameNews();
                ResetSteamPlayerCount();
                return;
            }

            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                try
                {
                    if (Settings != null)
                    {
                        Settings.IsFastNavigating = false;
                    }

                    navigationSettleTimer?.Stop();
                }
                catch { }

                ResetSteamUpdate();
                ResetSteamGameNews();
                ResetSteamPlayerCount();
                return;
            }

            if (!IsAnikiThemeActive())
            {
                try
                {
                    if (Settings != null)
                    {
                        Settings.IsFastNavigating = false;
                    }

                    navigationSettleTimer?.Stop();
                }
                catch { }

                ResetSteamUpdate();
                ResetSteamGameNews();
                ResetSteamPlayerCount();
                return;
            }

            try
            {
                if (Settings != null)
                {
                    Settings.IsFastNavigating = true;
                }

                navigationSettleTimer?.Stop();
                navigationSettleTimer?.Start();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to update fast navigation state.");
            }

            if (isInFullscreenDetailsView && Settings != null && g != null && g.Id != lastDetailsMediaGameId)
            {
                lastDetailsMediaGameId = g.Id;

                // Changing game must cancel/clear any old media load,
                Settings.ClearCurrentGameMediaState();
            }

            pendingUpdateGame = g;
            steamUpdateTimer?.Stop();

        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            DebugLog($"[AnikiHelper][GameStarting][START] Game='{args?.Game?.Name ?? "NULL"}' Id={args?.Game?.Id}");

            try
            {
                base.OnGameStarting(args);

                eventSoundService.PlayGameStarting();

                var game = args?.Game;
                if (game == null)
                {
                    DebugLog("[AnikiHelper][GameStarting][STOP] Game is null.");
                    return;
                }

                var splashEnabled = Settings?.GameLaunchSplashEnabled ?? false;
                var isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
                var isAnikiTheme = IsAnikiThemeActive();

                DebugLog(
                    $"[AnikiHelper][GameStarting][State] " +
                    $"Game='{game.Name}', " +
                    $"Fullscreen={isFullscreen}, " +
                    $"AnikiTheme={isAnikiTheme}, " +
                    $"SplashEnabled={splashEnabled}"
                );

                if (!splashEnabled)
                {
                    DebugLog($"[AnikiHelper][GameStarting][STOP] Splash disabled in settings. Game='{game.Name}'");
                    return;
                }

                if (!isFullscreen)
                {
                    DebugLog($"[AnikiHelper][GameStarting][STOP] Playnite is not in Fullscreen mode. Game='{game.Name}'");
                    return;
                }

                if (!isAnikiTheme)
                {
                    DebugLog($"[AnikiHelper][GameStarting][STOP] Aniki theme is not active. Game='{game.Name}'");
                    return;
                }

                var pauseUps = Settings?.GameLaunchSplashPauseUniPlaySong ?? true;

                DebugLog(
                    $"[AnikiHelper][Splash][UPS] " +
                    $"PauseUniPlaySong={pauseUps}, " +
                    $"Game='{game.Name}'"
                );

                if (pauseUps)
                {
                    HoldUniPlaySongGameStartingPause(game.Id);
                    DebugLog($"[AnikiHelper][Splash][UPS] Hold pause requested. Game='{game.Name}' Id={game.Id}");
                }

                var bgPath = GetBestGameLaunchSplashBackground(game);
                var fallbackBackgroundPath = GetPlayniteGameBackground(game);

                DebugLog(
                    $"[AnikiHelper][Splash][Background] " +
                    $"Game='{game.Name}', " +
                    $"Selected='{(string.IsNullOrEmpty(bgPath) ? "NULL" : bgPath)}', " +
                    $"Fallback='{(string.IsNullOrEmpty(fallbackBackgroundPath) ? "NULL" : fallbackBackgroundPath)}'"
                );

                var showLogo = Settings?.GameLaunchSplashShowLogo ?? true;
                var logoPosition = Settings?.GameLaunchSplashLogoPosition ?? SplashScreenLogoPosition.LeftCenter;
                var videoSoundEnabled = Settings?.GameLaunchSplashVideoSoundEnabled ?? false;
                var videoEndBehavior = Settings?.GameLaunchSplashVideoEndBehavior ?? SplashScreenVideoEndBehavior.ShowGameBackground;
                var videoVolume = Settings?.GameLaunchSplashVideoVolume ?? 0.5;

                DebugLog(
                    $"[AnikiHelper][Splash][Settings] " +
                    $"ShowLogo={showLogo}, " +
                    $"LogoPosition={logoPosition}, " +
                    $"VideoSound={videoSoundEnabled}, " +
                    $"VideoEndBehavior={videoEndBehavior}, " +
                    $"VideoVolume={videoVolume}"
                );

                splashScreenRuntimeService.Show(
                    game,
                    bgPath,
                    fallbackBackgroundPath,
                    showLogo,
                    logoPosition,
                    videoSoundEnabled,
                    videoEndBehavior,
                    videoVolume);

                DebugLog($"[AnikiHelper][Splash][RESULT] Show requested. Game='{game.Name}'");

                var minimumDuration = Settings?.GameLaunchSplashMinimumDurationMs ?? GameLaunchSplashMinimumDurationMs;
                var defaultDuration = minimumDuration;
                var hasCustomDuration = false;

                if (Settings?.CustomGameLaunchSplashMinimumDurations != null &&
                    Settings.CustomGameLaunchSplashMinimumDurations.TryGetValue(game.Id, out var customDuration))
                {
                    minimumDuration = customDuration;
                    hasCustomDuration = true;
                }

                DebugLog(
                    $"[AnikiHelper][Splash][Timer] " +
                    $"Game='{game.Name}', " +
                    $"Default={defaultDuration}, " +
                    $"HasCustom={hasCustomDuration}, " +
                    $"Final={minimumDuration}"
                );

                splashScreenRuntimeService.StartLaunchFailureSafety(minimumDuration);
                DebugLog($"[AnikiHelper][Splash][Safety] Launch failure safety started. Duration={minimumDuration}ms");

                StartUniPlaySongLaunchFailureRelease(game.Id, minimumDuration);
                DebugLog($"[AnikiHelper][Splash][UPS] Launch failure release scheduled. Duration={minimumDuration}ms");

                DebugLog($"[AnikiHelper][GameStarting][END] Game='{game.Name}'");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper][GameStarting][ERROR] Failed while starting game splash. Game='{args?.Game?.Name ?? "NULL"}'");
            }
        }

        private void MigrateLegacyCustomSplashToGameFolder(Game game)
        {
            try
            {
                var legacyPath = GetStoredCustomSplashPath(game);
                if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath))
                {
                    return;
                }

                var gameFolder = splashScreenService?.Folders?.GetGameFolder(game);
                if (string.IsNullOrWhiteSpace(gameFolder))
                {
                    return;
                }

                Directory.CreateDirectory(gameFolder);

                var hasExistingSplash = Directory.EnumerateFiles(gameFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Any(file =>
                    {
                        var ext = Path.GetExtension(file);
                        return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ext, ".jfif", StringComparison.OrdinalIgnoreCase);
                    });

                if (hasExistingSplash)
                {
                    return;
                }

                var extension = Path.GetExtension(legacyPath);
                var destinationPath = Path.Combine(gameFolder, $"legacy-custom-splash{extension}");

                File.Copy(legacyPath, destinationPath, false);

                DebugLog($"[AnikiHelper] Migrated legacy custom splash to game folder: {destinationPath}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to migrate legacy custom splash.");
            }
        }

        private string GetBestGameLaunchSplashBackground(Game game)
        {
            try
            {
                MigrateLegacyCustomSplashToGameFolder(game);

                var mode = Settings?.GameLaunchSplashSelectionMode ?? SplashScreenSelectionMode.Automatic;

                if (mode == SplashScreenSelectionMode.Automatic)
                {
                    var gameSplash = splashScreenService?.ResolveSplash(game, SplashScreenSelectionMode.Automatic);

                    if (!string.IsNullOrWhiteSpace(gameSplash?.FilePath) && File.Exists(gameSplash.FilePath))
                    {
                        return gameSplash.FilePath;
                    }

                    var custom = GetStoredCustomSplashPath(game);
                    if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
                    {
                        return custom;
                    }

                    var playniteBackground = GetPlayniteGameBackground(game);
                    if (!string.IsNullOrWhiteSpace(playniteBackground))
                    {
                        return playniteBackground;
                    }

                    var sharedFallback = splashScreenService?.ResolveSharedFallback(game);

                    if (!string.IsNullOrWhiteSpace(sharedFallback?.FilePath) && File.Exists(sharedFallback.FilePath))
                    {
                        return sharedFallback.FilePath;
                    }

                    return null;
                }

                var v2Splash = splashScreenService?.ResolveSplash(game, mode);

                if (!string.IsNullOrWhiteSpace(v2Splash?.FilePath) && File.Exists(v2Splash.FilePath))
                {
                    return v2Splash.FilePath;
                }

                var legacyCustom = GetStoredCustomSplashPath(game);
                if (!string.IsNullOrWhiteSpace(legacyCustom) && File.Exists(legacyCustom))
                {
                    return legacyCustom;
                }

                return GetPlayniteGameBackground(game);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to resolve splash background.");
            }

            return null;
        }

        private string GetPlayniteGameBackground(Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.BackgroundImage))
            {
                return null;
            }

            var bg = PlayniteApi.Database.GetFullFilePath(game.BackgroundImage);

            if (!string.IsNullOrWhiteSpace(bg) && File.Exists(bg))
            {
                return bg;
            }

            return null;
        }

        private Tag GetOrCreateCustomSplashTag()
        {
            var existing = PlayniteApi.Database.Tags
                .FirstOrDefault(t => string.Equals(t.Name, CustomSplashTagName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                return existing;
            }

            var tag = new Tag(CustomSplashTagName);
            PlayniteApi.Database.Tags.Add(tag);
            return tag;
        }

        private void UpdateCustomSplashTag(Game game, bool hasCustomSplash)
        {
            if (game == null)
            {
                return;
            }

            try
            {
                var dbGame = PlayniteApi.Database.Games.Get(game.Id);
                if (dbGame == null)
                {
                    return;
                }

                if (dbGame.TagIds == null)
                {
                    dbGame.TagIds = new List<Guid>();
                }

                var tag = GetOrCreateCustomSplashTag();

                if (hasCustomSplash)
                {
                    if (!dbGame.TagIds.Contains(tag.Id))
                    {
                        dbGame.TagIds.Add(tag.Id);
                        PlayniteApi.Database.Games.Update(dbGame);
                    }
                }
                else
                {
                    if (dbGame.TagIds.Contains(tag.Id))
                    {
                        dbGame.TagIds.Remove(tag.Id);
                        PlayniteApi.Database.Games.Update(dbGame);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to update custom splash tag.");
            }
        }

        private string GetCustomSplashImagesFolder()
        {
            var folder = Path.Combine(GetPluginUserDataPath(), "CustomSplashImages");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private string GetStoredCustomSplashPath(Game game)
        {
            if (game == null || Settings?.CustomGameLaunchSplashImages == null)
            {
                return null;
            }

            if (Settings.CustomGameLaunchSplashImages.TryGetValue(game.Id, out var path) &&
                !string.IsNullOrWhiteSpace(path) &&
                File.Exists(path))
            {
                return path;
            }

            return null;
        }

        private void RemoveExistingCustomSplashFiles(Guid gameId)
        {
            try
            {
                var folder = GetCustomSplashImagesFolder();
                foreach (var file in Directory.EnumerateFiles(folder, $"{gameId}.*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private void SetCustomSplashImage(Game game)
        {
            if (game == null)
            {
                return;
            }

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = string.Format(ResourceProvider.GetString("LOCAnikiHelperChooseSplashImageForGame"), game.Name),
                    Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp;*.bmp",
                    Multiselect = false,
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var sourcePath = dialog.FileName;
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    return;
                }

                var folder = GetCustomSplashImagesFolder();
                var extension = Path.GetExtension(sourcePath);
                var destPath = Path.Combine(folder, $"{game.Id}{extension}");

                RemoveExistingCustomSplashFiles(game.Id);
                File.Copy(sourcePath, destPath, true);

                if (Settings.CustomGameLaunchSplashImages == null)
                {
                    Settings.CustomGameLaunchSplashImages = new Dictionary<Guid, string>();
                }
                Settings.CustomGameLaunchSplashImages[game.Id] = destPath;

                SavePluginSettings(Settings);
                UpdateCustomSplashTag(game, true);

                PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCAnikiHelperCustomSplashImageSaved")
                    + Environment.NewLine
                    + game.Name,
                    "Aniki Helper");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to set custom splash image.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCAnikiHelperSetCustomSplashImageFailed")
                    + Environment.NewLine + Environment.NewLine
                    + ex.Message,
                    "Aniki Helper");
            }
        }

        private void RemoveCustomSplashImage(Game game)
        {
            if (game == null)
            {
                return;
            }

            try
            {
                var existing = GetStoredCustomSplashPath(game);
                if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
                {
                    try
                    {
                        File.Delete(existing);
                    }
                    catch
                    {
                    }
                }

                Settings.CustomGameLaunchSplashImages?.Remove(game.Id);
                SavePluginSettings(Settings);
                UpdateCustomSplashTag(game, false);

                PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCAnikiHelperCustomSplashImageRemoved")
                    + Environment.NewLine
                    + game.Name,
                    "Aniki Helper");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to remove custom splash image.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCAnikiHelperRemoveCustomSplashImageFailed")
                    + Environment.NewLine + Environment.NewLine
                    + ex.Message,
                    "Aniki Helper");
            }
        }

        private void PreviewCustomSplashImage(Game game)
        {
            if (game == null)
            {
                return;
            }

            var path = GetStoredCustomSplashPath(game);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCAnikiHelperNoCustomSplashImage"),
                    "Aniki Helper");
                return;
            }

            try
            {
                var workArea = SystemParameters.WorkArea;
                var previewWidth = workArea.Width * 0.72;
                var previewHeight = workArea.Height * 0.72;

                var image = new System.Windows.Controls.Image
                {
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(path, UriKind.Absolute))
                };

                var imageBorder = new System.Windows.Controls.Border
                {
                    Background = System.Windows.Media.Brushes.Black,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Child = image
                };

                var outerBorder = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14),
                    CornerRadius = new CornerRadius(14),
                    Child = imageBorder
                };

                var root = new System.Windows.Controls.Grid
                {
                    Background = new SolidColorBrush(Color.FromRgb(12, 12, 12))
                };

                root.Children.Add(outerBorder);

                var window = new System.Windows.Window
                {
                    Title = $"{ResourceProvider.GetString("LOCAnikiHelperPreviewTitle")} - {game.Name}",
                    Width = previewWidth,
                    Height = previewHeight,
                    MinWidth = 700,
                    MinHeight = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.CanResize,
                    Background = new SolidColorBrush(Color.FromRgb(12, 12, 12)),
                    Content = root
                };

                window.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        window.Close();
                    }
                };

                window.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
                    {
                        window.Close();
                    }
                };

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to preview splash image.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCAnikiHelperPreviewSplashImageFailed")
                    + Environment.NewLine + Environment.NewLine
                    + ex.Message,
                    "Aniki Helper");
            }
        }

        private void OpenCustomSplashImageFolder(Game game)
        {
            try
            {
                string folderPath = null;

                var customPath = GetStoredCustomSplashPath(game);
                if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                {
                    folderPath = Path.GetDirectoryName(customPath);
                }

                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    folderPath = GetCustomSplashImagesFolder();
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to open custom splash image folder.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCAnikiHelperOpenCustomSplashFolderFailed")
                    + Environment.NewLine + Environment.NewLine
                    + ex.Message,
                    "Aniki Helper");
            }
        }

        private void OpenGameSplashFolder(Game game)
        {
            try
            {
                if (game == null)
                {
                    return;
                }

                var folder = splashScreenService?.Folders?.GetGameFolder(game);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                Directory.CreateDirectory(folder);
                Process.Start(folder);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open game splash folder.");
            }
        }

        private void OpenSplashScreenTargetPicker(
    string title,
    string description,
    IEnumerable<SplashScreenTarget> targets)
        {
            try
            {
                var targetList = targets?
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.DisplayName))
                    .OrderBy(x => x.DisplayName)
                    .ToList();

                if (targetList == null || targetList.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("SplashPicker_NoItemFound"),
                        "Aniki Helper");
                    return;
                }

                var view = new SplashScreenTargetPickerWindow(
                    title,
                    description,
                    targetList,
                    target =>
                    {
                        OpenSplashScreenManager(target, target.DisplayName);
                    });

                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true
                });

                window.Title = "Aniki Helper - " + title;
                window.Width = 620;
                window.Height = 620;
                window.MinWidth = 520;
                window.MinHeight = 420;
                window.Content = view;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open splash target picker.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("SplashPicker_OpenFailed") + "\n" + ex.Message,
                    "Aniki Helper");
            }
        }

        public void OpenSourceSplashScreenManager()
        {
            try
            {
                var targets = PlayniteApi.Database.Sources
                    .Where(source => source != null && !string.IsNullOrWhiteSpace(source.Name))
                    .Select(source => SplashScreenTarget.FromSource(source.Name, splashScreenService.Folders))
                    .ToList();

                OpenSplashScreenTargetPicker(
                    ResourceProvider.GetString("SplashPicker_SourceTitle"),
                    ResourceProvider.GetString("SplashPicker_SourceDescription"),
                    targets);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open source splash screen manager.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("SplashPicker_SourceOpenFailed") + "\n" + ex.Message,
                    "Aniki Helper");
            }
        }

        public void OpenPlatformSplashScreenManager()
        {
            try
            {
                var targets = PlayniteApi.Database.Platforms
                    .Where(platform => platform != null && !string.IsNullOrWhiteSpace(platform.Name))
                    .Select(platform => SplashScreenTarget.FromPlatform(platform.Name, splashScreenService.Folders))
                    .ToList();

                OpenSplashScreenTargetPicker(
                    ResourceProvider.GetString("SplashPicker_PlatformTitle"),
                    ResourceProvider.GetString("SplashPicker_PlatformDescription"),
                    targets);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open platform splash screen manager.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("SplashPicker_PlatformOpenFailed") + "\n" + ex.Message,
                    "Aniki Helper");
            }
        }

        public void OpenGlobalSplashScreenManager()
        {
            try
            {
                var target = SplashScreenTarget.FromGlobal(splashScreenService.Folders);
                OpenSplashScreenManager(target, ResourceProvider.GetString("SplashPicker_GlobalTitle"));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open global splash screen manager.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("SplashPicker_GlobalOpenFailed") + "\n" + ex.Message,
                    "Aniki Helper");
            }
        }

        private void OpenSplashScreenManager(Game game)
        {
            try
            {
                if (game == null)
                {
                    return;
                }

                var target = SplashScreenTarget.FromGame(game, splashScreenService.Folders);
                OpenSplashScreenManager(target, game.Name);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open splash screen manager.");
            }
        }

        private void OpenSplashScreenManager(SplashScreenTarget target, string title)
        {
            if (target == null)
            {
                return;
            }

            var view = new SplashScreenManagerWindow(target);

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Title = $"{ResourceProvider.GetString("SplashManager_WindowTitle")} - {title}";
            window.Width = 1100;
            window.Height = 720;
            window.MinWidth = 900;
            window.MinHeight = 560;
            window.Content = view;
            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            window.ShowDialog();
        }

        private void SetGameSplashMinimumTimer(Game game)
        {
            if (game == null)
            {
                return;
            }

            try
            {
                var currentValueMs = Settings?.CustomGameLaunchSplashMinimumDurations != null &&
                                     Settings.CustomGameLaunchSplashMinimumDurations.TryGetValue(game.Id, out var customValue)
                    ? customValue
                    : Settings?.GameLaunchSplashMinimumDurationMs ?? GameLaunchSplashMinimumDurationMs;

                var currentValueSeconds = currentValueMs / 1000.0;

                var result = PlayniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCAnikiHelperSetGameSplashMinimumTimerPrompt"),
                    ResourceProvider.GetString("LOCAnikiHelperSetGameSplashMinimumTimerPrompt"),
                    currentValueSeconds.ToString("0.##", CultureInfo.InvariantCulture));

                if (!result.Result)
                {
                    return;
                }

                var input = result.SelectedString;

                if (string.IsNullOrWhiteSpace(input))
                {
                    return;
                }

                input = input.Replace(',', '.');

                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var valueSeconds))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        ResourceProvider.GetString("LOCAnikiHelperInvalidTimerValue"),
                        "Aniki Helper");
                    return;
                }

                var value = (int)Math.Round(Math.Max(0.5, Math.Min(600, valueSeconds)) * 1000);
                if (Settings.CustomGameLaunchSplashMinimumDurations == null)
                {
                    Settings.CustomGameLaunchSplashMinimumDurations = new Dictionary<Guid, int>();
                }

                Settings.CustomGameLaunchSplashMinimumDurations[game.Id] = value;
                SavePluginSettings(Settings);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to set game splash minimum timer.");
            }
        }

        private void SetGameSplashMinimumTimerValue(Game game, int value)
        {
            if (game == null)
            {
                return;
            }

            try
            {
                value = Math.Max(500, Math.Min(GameLaunchSplashMaximumMinimumDurationMs, value));

                if (Settings.CustomGameLaunchSplashMinimumDurations == null)
                {
                    Settings.CustomGameLaunchSplashMinimumDurations = new Dictionary<Guid, int>();
                }

                Settings.CustomGameLaunchSplashMinimumDurations[game.Id] = value;
                SavePluginSettings(Settings);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to set game splash minimum timer preset.");
            }
        }

        private IEnumerable<GameMenuItem> GetGameSplashMinimumTimerPresetItems(Game game)
        {
            var menuSection = "Aniki Helper|Splash Screen|Set minimum timer for this game";

            var presets = new[]
            {
                new { Label = "1 sec", Milliseconds = 1000 },
                new { Label = "2.4 sec", Milliseconds = 2400 },
                new { Label = "3 sec", Milliseconds = 3000 },
                new { Label = "4 sec", Milliseconds = 4000 },
                new { Label = "5 sec", Milliseconds = 5000 },
                new { Label = "6 sec", Milliseconds = 6000 },
                new { Label = "7 sec", Milliseconds = 7000 },
                new { Label = "8 sec", Milliseconds = 8000 },
                new { Label = "9 sec", Milliseconds = 9000 },
                new { Label = "10 sec", Milliseconds = 10000 },
                new { Label = "11 sec", Milliseconds = 11000 },
                new { Label = "12 sec", Milliseconds = 12000 },
                new { Label = "13 sec", Milliseconds = 13000 },
                new { Label = "14 sec", Milliseconds = 14000 },
                new { Label = "15 sec", Milliseconds = 15000 },
                new { Label = "16 sec", Milliseconds = 16000 },
                new { Label = "17 sec", Milliseconds = 17000 },
                new { Label = "18 sec", Milliseconds = 18000 },
                new { Label = "19 sec", Milliseconds = 19000 },
                new { Label = "20 sec", Milliseconds = 20000 },
                new { Label = "25 sec", Milliseconds = 25000 },
                new { Label = "30 sec", Milliseconds = 30000 },
                new { Label = "45 sec", Milliseconds = 45000 },
                new { Label = "1 min", Milliseconds = 60000 },
                new { Label = "1 min 30 sec", Milliseconds = 90000 },
                new { Label = "2 min", Milliseconds = 120000 },
                new { Label = "3 min", Milliseconds = 180000 },
                new { Label = "5 min", Milliseconds = 300000 },
                new { Label = "10 min", Milliseconds = 600000 }
            };

            foreach (var preset in presets)
            {
                yield return new GameMenuItem
                {
                    MenuSection = menuSection,
                    Description = preset.Label,
                    Action = (_) => SetGameSplashMinimumTimerValue(game, preset.Milliseconds)
                };
            }
        }

        private void ResetGameSplashMinimumTimer(Game game)
        {
            if (game == null)
            {
                return;
            }

            try
            {
                Settings?.CustomGameLaunchSplashMinimumDurations?.Remove(game.Id);
                SavePluginSettings(Settings);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to reset game splash minimum timer.");
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var swTotal = Stopwatch.StartNew();

            DebugLog(
                $"[AnikiHelper][GameStarted][START] " +
                $"Game='{args?.Game?.Name ?? "NULL"}', " +
                $"Id={args?.Game?.Id}, " +
                $"StartedProcessId={args?.StartedProcessId}"
            );

            DebugLogFocusState("GameStarted START");

            try
            {
                base.OnGameStarted(args);

                DebugLogFocusState("After base.OnGameStarted");

                eventSoundService.PlayGameStarted();
                DebugLog($"[AnikiHelper][GameStarted][Sound] Game started sound requested. Game='{args?.Game?.Name ?? "NULL"}'");

                var g = args?.Game;
                var isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
                var isAnikiTheme = IsAnikiThemeActive();

                if (g != null && (Settings?.GameLaunchSplashPauseUniPlaySong ?? true))
                {
                    HoldUniPlaySongGameStartingPause(g.Id);
                    DebugLog($"[AnikiHelper][UPS][GameSession] Hold pause requested while game is running. Game='{g.Name}' Id={g.Id}");
                }

                DebugLog(
                    $"[AnikiHelper][GameStarted][State] " +
                    $"Fullscreen={isFullscreen}, " +
                    $"AnikiTheme={isAnikiTheme}, " +
                    $"GameNull={g == null}"
                );

                if (isFullscreen && isAnikiTheme)
                {
                    inGameOverlayService?.SetCurrentGame(g, args?.StartedProcessId);

                    DebugLog(
                        $"[AnikiHelper][Overlay][SetCurrentGame] " +
                        $"Requested. Game='{g?.Name ?? "NULL"}', " +
                        $"ProcessId={args?.StartedProcessId}"
                    );
                }
                else
                {
                    DebugLog(
                        $"[AnikiHelper][Overlay][SkipSetCurrentGame] " +
                        $"Reason={(isFullscreen ? "ThemeNotActive" : "NotFullscreen")}, " +
                        $"Game='{g?.Name ?? "NULL"}'"
                    );
                }

                Task.Run(async () =>
                {
                    DebugLog($"[AnikiHelper][Splash][CloseAfterGameStartedTask][START] Game='{g?.Name ?? "NULL"}'");

                    try
                    {
                        var minimumDuration = Settings?.GameLaunchSplashMinimumDurationMs ?? GameLaunchSplashMinimumDurationMs;
                        var hasCustomDuration = false;

                        if (g != null &&
                            Settings?.CustomGameLaunchSplashMinimumDurations != null &&
                            Settings.CustomGameLaunchSplashMinimumDurations.TryGetValue(g.Id, out var customDuration))
                        {
                            minimumDuration = customDuration;
                            hasCustomDuration = true;
                        }

                        var maximumWait = Settings?.GameLaunchSplashMaximumWaitMs ?? GameLaunchSplashMaxWaitAfterGameStartedMs;

                        DebugLog(
                            $"[AnikiHelper][Splash][CloseAfterGameStartedTask][Settings] " +
                            $"Game='{g?.Name ?? "NULL"}', " +
                            $"MinimumDuration={minimumDuration}, " +
                            $"HasCustomDuration={hasCustomDuration}, " +
                            $"MaximumWait={maximumWait}"
                        );

                        await splashScreenRuntimeService.CloseAfterGameStartedAsync(minimumDuration, maximumWait);

                        DebugLog($"[AnikiHelper][Splash][CloseAfterGameStartedTask][Result] CloseAfterGameStartedAsync finished. Game='{g?.Name ?? "NULL"}'");

                        DebugLog($"[AnikiHelper][UPS][KeepHeld] UniPlaySong pause kept while game is running. Game='{g?.Name ?? "NULL"}'");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"[AnikiHelper][Splash][CloseAfterGameStartedTask][ERROR] Failed. Game='{g?.Name ?? "NULL"}'");
                    }

                    DebugLog($"[AnikiHelper][Splash][CloseAfterGameStartedTask][END] Game='{g?.Name ?? "NULL"}'");
                });

                DebugLog($"[AnikiHelper][Splash][CloseAfterGameStartedTask][Schedule] Task scheduled. Game='{g?.Name ?? "NULL"}'");

                if (g == null)
                {
                    DebugLog($"[AnikiHelper][GameStarted][STOP] Game is null. Session tracking skipped. TotalDuration={swTotal.ElapsedMilliseconds}ms");
                    return;
                }

                sessionStartAt[g.Id] = DateTime.Now;
                sessionStartPlaytimeMinutes[g.Id] = (g.Playtime / 60UL);

                DebugLog(
                    $"[AnikiHelper][Session][StartTracking] " +
                    $"Game='{g.Name}', " +
                    $"Start='{sessionStartAt[g.Id]:yyyy-MM-dd HH:mm:ss}', " +
                    $"StartPlaytimeMinutes={sessionStartPlaytimeMinutes[g.Id]}"
                );

                DebugLog(
                    $"[AnikiHelper][GameStarted][END] " +
                    $"Game='{g.Name}', Id={g.Id}, " +
                    $"TotalDuration={swTotal.ElapsedMilliseconds}ms"
                );
            }
            catch (Exception ex)
            {
                logger.Error(
                    ex,
                    $"[AnikiHelper][GameStarted][FATAL] " +
                    $"Unexpected error after {swTotal.ElapsedMilliseconds}ms. " +
                    $"Game='{args?.Game?.Name ?? "NULL"}'"
                );
            }
        }


        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var swTotal = Stopwatch.StartNew();

            DebugLog(
                $"[AnikiHelper][GameStopped][START] " +
                $"Game='{args?.Game?.Name ?? "NULL"}', " +
                $"Id={args?.Game?.Id}"
            );

            DebugLogFocusState("GameStopped START");
            StartPostGameFocusDebugTrace(args?.Game?.Name ?? "NULL");

            try
            {
                base.OnGameStopped(args);
                DebugLogFocusState("After base.OnGameStopped");

                var g = args?.Game;

                eventSoundService.PlayGameStopped();
                DebugLog($"[AnikiHelper][GameStopped][Sound] Game stopped sound requested. Game='{g?.Name ?? "NULL"}'");

                splashScreenRuntimeService.Close();
                DebugLog($"[AnikiHelper][Splash][Close] Close requested after game stop. Game='{g?.Name ?? "NULL"}'");

                ReleaseUniPlaySongGameStartingPause(g?.Id);
                DebugLog($"[AnikiHelper][UPS][Release] Game starting pause released. Game='{g?.Name ?? "NULL"}', Id={g?.Id}");

                var isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
                var isAnikiTheme = IsAnikiThemeActive();

                DebugLog(
                    $"[AnikiHelper][GameStopped][State] " +
                    $"Fullscreen={isFullscreen}, " +
                    $"AnikiTheme={isAnikiTheme}, " +
                    $"GameNull={g == null}"
                );

                if (isFullscreen && isAnikiTheme)
                {
                    inGameOverlayService?.ClearCurrentGame(g);
                    DebugLogFocusState("After ClearCurrentGame");
                    DebugLog($"[AnikiHelper][Overlay][ClearCurrentGame] Requested after game stop. Game='{g?.Name ?? "NULL"}'");
                }
                else
                {
                    DebugLog(
                        $"[AnikiHelper][Overlay][SkipClearCurrentGame] " +
                        $"Reason={(isFullscreen ? "ThemeNotActive" : "NotFullscreen")}"
                    );
                }

                if (g == null)
                {
                    DebugLog("[AnikiHelper][GameStopped][STOP] Game is null. Session, stats, media and achievements refresh skipped.");
                    return;
                }

                var start = sessionStartAt.ContainsKey(g.Id) ? sessionStartAt[g.Id] : DateTime.Now;
                var memorySessionStart = start;
                var memorySessionEnd = DateTime.Now;
                var elapsed = DateTime.Now - start;
                var sessionMinutes = (int)Math.Max(0, Math.Round(elapsed.TotalMinutes));

                var totalMinutes = (int)(g.Playtime / 60UL);
                var totalPlaytimeFallbackUsed = false;

                if (totalMinutes <= 0 && sessionStartPlaytimeMinutes.ContainsKey(g.Id))
                {
                    totalMinutes = (int)sessionStartPlaytimeMinutes[g.Id] + sessionMinutes;
                    totalPlaytimeFallbackUsed = true;
                }

                DebugLog(
                    $"[AnikiHelper][Session][Computed] " +
                    $"Game='{g.Name}', " +
                    $"SessionStart='{memorySessionStart:yyyy-MM-dd HH:mm:ss}', " +
                    $"SessionEnd='{memorySessionEnd:yyyy-MM-dd HH:mm:ss}', " +
                    $"SessionMinutes={sessionMinutes}, " +
                    $"TotalMinutes={totalMinutes}, " +
                    $"FallbackUsed={totalPlaytimeFallbackUsed}"
                );

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    DebugLog($"[AnikiHelper][SessionNotification][START] Preparing UI notification. Game='{g.Name}'");

                    try
                    {
                        var s = Settings;

                        if (s == null)
                        {
                            DebugLog($"[AnikiHelper][SessionNotification][STOP] Settings is null. Game='{g.Name}'");
                            return;
                        }

                        s.SessionGameId = g.Id;
                        s.SessionGameName = string.IsNullOrWhiteSpace(g.Name) ? "(Unnamed Game)" : g.Name;
                        s.SessionDurationString = FormatHhMmFromMinutes(sessionMinutes);
                        s.SessionTotalPlaytimeString = FormatHhMmFromMinutes(Math.Max(0, totalMinutes));

                        DebugLog(
                            $"[AnikiHelper][SessionNotification][Data] " +
                            $"Game='{s.SessionGameName}', " +
                            $"SessionDuration='{s.SessionDurationString}', " +
                            $"TotalPlaytime='{s.SessionTotalPlaytimeString}'"
                        );

                        s.SessionGameBackgroundPath = GetBestHubCardBackgroundPath(g);

                        AddLastNotificationOnUi(
                            title: "Game session ended",
                            message: $"{s.SessionGameName} - {s.SessionDurationString}",
                            type: "gameEnded",
                            imagePath: s.SessionGameBackgroundPath
                        );

                        DebugLog(
                            $"[AnikiHelper][SessionNotification][History] " +
                            $"Notification added to history. " +
                            $"Title='Game session ended', " +
                            $"Message='{s.SessionGameName} - {s.SessionDurationString}'"
                        );

                        SaveSettingsSafe();
                        DebugLog("[AnikiHelper][SessionNotification][Save] Settings save requested after session notification.");

                        s.SessionNotificationStamp = Guid.NewGuid().ToString();
                        s.SessionNotificationFlip = !s.SessionNotificationFlip;

                        DebugLogFocusState("Before SessionNotificationArmed");

                        s.SessionNotificationArmed = true;

                        DebugLogFocusState("After SessionNotificationArmed");

                        DebugLog(
                            $"[AnikiHelper][SessionNotification][Popup] " +
                            $"Popup armed. " +
                            $"Stamp='{s.SessionNotificationStamp}', " +
                            $"Flip={s.SessionNotificationFlip}, " +
                            $"Armed={s.SessionNotificationArmed}"
                        );

                        DebugLog($"[AnikiHelper][SessionNotification][END] UI notification prepared. Game='{s.SessionGameName}'");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"[AnikiHelper][SessionNotification][ERROR] Failed to prepare session notification. Game='{g.Name}'");
                    }
                }));

                DebugLog($"[AnikiHelper][SessionNotification][Schedule] UI notification scheduled. Game='{g.Name}'");

                EnsureGameInCurrentMonthSnapshot(g, sessionMinutes);

                DebugLog(
                    $"[AnikiHelper][Stats][MonthlySnapshot] " +
                    $"Game ensured in current month snapshot. " +
                    $"Game='{g.Name}', SessionMinutes={sessionMinutes}"
                );

                sessionStartAt.Remove(g.Id);
                sessionStartPlaytimeMinutes.Remove(g.Id);

                DebugLog($"[AnikiHelper][Session][Cleanup] Session dictionaries cleaned. Game='{g.Name}', Id={g.Id}");

                EnsureMonthlySnapshotSafe();
                DebugLog("[AnikiHelper][Stats][MonthlySnapshot] Monthly snapshot refresh requested.");

                RecalcStatsSafe(true);
                DebugLog("[AnikiHelper][Stats][Recalc] Stats recalculation requested. Force=True");

                try
                {
                    var refreshAllowed =
                        PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen &&
                        IsAnikiThemeActive();

                    DebugLog(
                        $"[AnikiHelper][PostGameRefresh][State] " +
                        $"Allowed={refreshAllowed}, " +
                        $"Fullscreen={PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen}, " +
                        $"AnikiTheme={IsAnikiThemeActive()}, " +
                        $"Game='{g.Name}'"
                    );

                    if (refreshAllowed)
                    {
                        DebugLogFocusState("Before Schedule MediaRefresh");
                        _ = Settings?.RefreshStoppedGameMediaSilentAsync(g.Id, 6000, memorySessionStart, memorySessionEnd);

                        DebugLog(
                            $"[AnikiHelper][MediaRefresh][Schedule] " +
                            $"Stopped game media refresh scheduled. " +
                            $"Game='{g.Name}', Delay=6000ms, " +
                            $"SessionStart='{memorySessionStart:yyyy-MM-dd HH:mm:ss}', " +
                            $"SessionEnd='{memorySessionEnd:yyyy-MM-dd HH:mm:ss}'"
                        );

                        _ = Settings?.RefreshAchievementMemoriesForGameAsync(g.Id);
                        DebugLogFocusState("After Schedule AchievementsRefresh");

                        DebugLog(
                            $"[AnikiHelper][Achievements][ScheduleRefresh] " +
                            $"Achievement memories refresh scheduled. Game='{g.Name}', Id={g.Id}"
                        );
                    }
                    else
                    {
                        DebugLog($"[AnikiHelper][PostGameRefresh][SKIP] Refresh skipped. Game='{g.Name}'");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"[AnikiHelper][PostGameRefresh][ERROR] Failed to schedule post-game refresh. Game='{g.Name}'");
                }

                DebugLog(
                    $"[AnikiHelper][GameStopped][END] " +
                    $"Game='{g.Name}', Id={g.Id}, " +
                    $"TotalDuration={swTotal.ElapsedMilliseconds}ms"
                );
            }
            catch (Exception ex)
            {
                logger.Error(
                    ex,
                    $"[AnikiHelper][GameStopped][FATAL] " +
                    $"Unexpected error after {swTotal.ElapsedMilliseconds}ms. " +
                    $"Game='{args?.Game?.Name ?? "NULL"}'"
                );
            }
        }

        #endregion

        private void DebounceRecalcStatsSafe()
        {
            try
            {
                CancellationTokenSource cts;

                lock (databaseStatsDebounceLock)
                {
                    try
                    {
                        databaseStatsDebounceCts?.Cancel();
                        databaseStatsDebounceCts?.Dispose();
                    }
                    catch { }

                    databaseStatsDebounceCts = new CancellationTokenSource();
                    cts = databaseStatsDebounceCts;
                }

                _ = DebounceRecalcStatsSafeAsync(cts);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] DebounceRecalcStatsSafe setup failed.");
            }
        }

        private async Task DebounceRecalcStatsSafeAsync(CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(1500, cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!cts.Token.IsCancellationRequested)
                    {
                        RecalcStatsSafe();
                    }
                }, DispatcherPriority.Background);
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] DebounceRecalcStatsSafe failed.");
            }
            finally
            {
                lock (databaseStatsDebounceLock)
                {
                    if (ReferenceEquals(databaseStatsDebounceCts, cts))
                    {
                        databaseStatsDebounceCts = null;
                    }
                }

                try
                {
                    cts.Dispose();
                }
                catch { }
            }
        }

        private void RecalcStatsSafe(bool runtimeOnly = false)
        {
            try { RecalcStats(runtimeOnly); }
            catch (Exception ex) { logger.Error(ex, "[AnikiHelper] RecalcStats failed"); }
        }

        private void RecalcSuggestedGameSafe()
        {
            try
            {
                RecalcSuggestedGame();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] RecalcSuggestedGame failed");
            }
        }

        private string GetBestHubCardBackgroundPath(Game game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            string bgPath = null;

            if (!string.IsNullOrEmpty(game.BackgroundImage) &&
                !game.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bgPath = PlayniteApi.Database.GetFullFilePath(game.BackgroundImage);
            }

            if (string.IsNullOrEmpty(bgPath) &&
                !string.IsNullOrEmpty(game.CoverImage) &&
                !game.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bgPath = PlayniteApi.Database.GetFullFilePath(game.CoverImage);
            }

            if (string.IsNullOrEmpty(bgPath) &&
                !string.IsNullOrEmpty(game.Icon) &&
                !game.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bgPath = PlayniteApi.Database.GetFullFilePath(game.Icon);
            }

            return string.IsNullOrEmpty(bgPath) ? string.Empty : bgPath;
        }

        private static string PercentStringLocal(int part, int total) =>
            total <= 0 ? "0%" : $"{Math.Round(part * 100.0 / total)}%";

        /// <summary>Recalcule les statistiques du plugin (mode complet ou allégé selon runtimeOnly).</summary>
        private void RecalcStats(bool runtimeOnly = false)
        {
            var s = Settings;

            Func<ulong, ulong> ToMinutes = raw => raw / 60UL;

            var games = PlayniteApi.Database.Games.ToList();
            if (!s.IncludeHidden)
            {
                games = games.Where(g => g.Hidden != true).ToList();
            }

            if (!runtimeOnly)
            {
                // === Totaux
                s.TotalCount = games.Count;
                s.InstalledCount = games.Count(g => g.IsInstalled == true);
                s.NotInstalledCount = games.Count(g => g.IsInstalled != true);
                s.HiddenCount = games.Count(g => g.Hidden == true);
                s.FavoriteCount = games.Count(g => g.Favorite == true);
            }

            // === Playtime total/moyen (minutes)
            ulong totalMinutes = (ulong)games.Sum(g => (long)ToMinutes(g.Playtime));
            s.TotalPlaytimeMinutes = totalMinutes;

            var played = games.Where(g => ToMinutes(g.Playtime) > 0UL).ToList();

            var recentPlayedForProfile = played
                .Where(g => g.LastActivity != null && g.LastActivity >= DateTime.Now.AddYears(-2))
                .ToList();

            if (!runtimeOnly)
            {
                var topPlatform = BuildTopPlatformName(recentPlayedForProfile);
                if (string.IsNullOrWhiteSpace(topPlatform))
                {
                    topPlatform = BuildTopPlatformName(played);
                }

                var topFranchise = BuildTopFranchiseName(recentPlayedForProfile);
                if (string.IsNullOrWhiteSpace(topFranchise))
                {
                    topFranchise = BuildTopFranchiseName(played);
                }

                var topTag = BuildTopTagName(recentPlayedForProfile);
                if (string.IsNullOrWhiteSpace(topTag))
                {
                    topTag = BuildTopTagName(played);
                }

                s.AveragePlaytimeMinutes = (ulong)(played.Count == 0 ? 0 : played.Sum(g => (long)ToMinutes(g.Playtime)) / played.Count);

                if (string.IsNullOrWhiteSpace(s.ProfileGenreKey) ||
                    s.LastProfileGenreScanUtc == DateTime.MinValue ||
                    (DateTime.UtcNow - s.LastProfileGenreScanUtc).TotalDays >= 30)
                {
                    DebugLog("[ProfileGenre] Cache empty or expired, recalculating.");
                    RecalcProfileGenre(played);
                }
                else
                {
                    DebugLog("[ProfileGenre] Cache valid, skipping recalculation.");
                }
                s.ProfileTopPlatformName = topPlatform;
                s.ProfileTopFranchiseName = topFranchise;
                s.ProfileTopTagName = topTag;
            }

            if (!runtimeOnly)
            {
                // === TOP PLAYED
                s.TopPlayed.Clear();

                if (totalMinutes > 0UL)
                {
                    foreach (var g in played
                        .OrderByDescending(g => g.Playtime)
                        .Take(Math.Max(1, Math.Min(50, s.TopPlayedMax))))
                    {
                        var gMin = ToMinutes(g.Playtime);
                        var pct = Math.Round((double)gMin * 100.0 / totalMinutes);

                        s.TopPlayed.Add(new TopPlayedItem
                        {
                            Name = Safe(g.Name),
                            PlaytimeString = AnikiHelperSettings.PlaytimeToString(gMin, false),
                            PercentageString = $"{pct}%"
                        });
                    }
                }
            }

            // THIS MONTH 
            try
            {
                var now = DateTime.Now;
                var monthStart = new DateTime(now.Year, now.Month, 1);

                var snapshot = LoadMonthSnapshot(monthStart);

                int playedCount = 0;
                ulong monthTotalMinutes = 0UL;
                ulong topMinutes = 0UL;
                Guid topGameId = Guid.Empty;

                foreach (var g in games)
                {
                    if (g.LastActivity == null || g.LastActivity < monthStart)
                    {
                        continue;
                    }

                    var currMinutes = ToMinutes(g.Playtime);

                    if (!snapshot.TryGetValue(g.Id, out var baseMinutes))
                    {
                        continue;
                    }

                    var delta = currMinutes > baseMinutes ? (currMinutes - baseMinutes) : 0UL;
                    if (delta > 0)
                    {
                        playedCount++;
                        monthTotalMinutes += delta;

                        if (delta > topMinutes)
                        {
                            topMinutes = delta;
                            topGameId = g.Id;
                        }
                    }
                }

                s.ThisMonthPlayedCount = playedCount;
                s.ThisMonthPlayedTotalMinutes = monthTotalMinutes;

                if (topGameId != Guid.Empty)
                {
                    var topGame = PlayniteApi.Database.Games[topGameId];
                    s.ThisMonthTopGameName = Safe(topGame?.Name);
                    s.ThisMonthTopGameId = topGameId;
                    s.ThisMonthTopGamePlaytime = AnikiHelperSettings.PlaytimeToString(topMinutes, false);

                    string coverPath = null;
                    if (!string.IsNullOrEmpty(topGame?.CoverImage))
                        coverPath = PlayniteApi.Database.GetFullFilePath(topGame.CoverImage);
                    if (string.IsNullOrEmpty(coverPath) && !string.IsNullOrEmpty(topGame?.BackgroundImage))
                        coverPath = PlayniteApi.Database.GetFullFilePath(topGame.BackgroundImage);
                    if (string.IsNullOrEmpty(coverPath) && !string.IsNullOrEmpty(topGame?.Icon))
                        coverPath = PlayniteApi.Database.GetFullFilePath(topGame.Icon);

                    s.ThisMonthTopGameCoverPath = string.IsNullOrEmpty(coverPath) ? string.Empty : coverPath;

                    string bgPath = null;

                    if (!string.IsNullOrEmpty(topGame?.BackgroundImage) &&
                        !topGame.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bgPath = PlayniteApi.Database.GetFullFilePath(topGame.BackgroundImage);
                    }

                    if (string.IsNullOrEmpty(bgPath) &&
                        !string.IsNullOrEmpty(topGame?.CoverImage) &&
                        !topGame.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bgPath = PlayniteApi.Database.GetFullFilePath(topGame.CoverImage);
                    }

                    if (string.IsNullOrEmpty(bgPath) &&
                        !string.IsNullOrEmpty(topGame?.Icon) &&
                        !topGame.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bgPath = PlayniteApi.Database.GetFullFilePath(topGame.Icon);
                    }

                    s.ThisMonthTopGameBackgroundPath = string.IsNullOrEmpty(bgPath) ? string.Empty : bgPath;
                }
                else
                {
                    s.ThisMonthTopGameId = Guid.Empty;
                    s.ThisMonthTopGameName = string.Empty;
                    s.ThisMonthTopGamePlaytime = string.Empty;
                    s.ThisMonthTopGameCoverPath = string.Empty;
                    s.ThisMonthTopGameBackgroundPath = string.Empty;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Month snapshot calc failed; continuing with other sections.");
                s.ThisMonthPlayedCount = 0;
                s.ThisMonthPlayedTotalMinutes = 0;
                s.ThisMonthTopGameId = Guid.Empty;
                s.ThisMonthTopGameName = string.Empty;
                s.ThisMonthTopGamePlaytime = string.Empty;
                s.ThisMonthTopGameCoverPath = string.Empty;
                s.ThisMonthTopGameBackgroundPath = string.Empty;
            }

            // THIS YEAR
            try
            {
                var now = DateTime.Now;
                var yearStart = new DateTime(now.Year, 1, 1);

                var snapshots = LoadAllMonthlySnapshots()
                    .Where(x => x.MonthStart >= yearStart)
                    .OrderBy(x => x.MonthStart)
                    .ToList();

                var yearMinutesByGame = new Dictionary<Guid, ulong>();

                if (snapshots.Count > 0)
                {
                    // Mois terminés
                    for (int i = 0; i < snapshots.Count - 1; i++)
                    {
                        var a = snapshots[i];
                        var b = snapshots[i + 1];

                        var ids = new HashSet<Guid>(a.Minutes.Keys);
                        foreach (var id in b.Minutes.Keys)
                        {
                            ids.Add(id);
                        }

                        foreach (var id in ids)
                        {
                            if (!a.Minutes.TryGetValue(id, out var m0))
                            {
                                continue;
                            }

                            if (!b.Minutes.TryGetValue(id, out var m1))
                            {
                                continue;
                            }

                            if (m1 > m0)
                            {
                                ulong acc = 0;
                                yearMinutesByGame.TryGetValue(id, out acc);
                                yearMinutesByGame[id] = acc + (m1 - m0);
                            }
                        }
                    }

                    // Dernier snapshot -> playtime actuel
                    var last = snapshots[snapshots.Count - 1];

                    foreach (var g in games)
                    {
                        if (!last.Minutes.TryGetValue(g.Id, out var baseMinutes))
                        {
                            continue;
                        }

                        var currMinutes = ToMinutes(g.Playtime);

                        if (currMinutes > baseMinutes)
                        {
                            ulong acc = 0;
                            yearMinutesByGame.TryGetValue(g.Id, out acc);
                            yearMinutesByGame[g.Id] = acc + (currMinutes - baseMinutes);
                        }
                    }
                }

                s.ThisYearPlayedCount = yearMinutesByGame.Count(x => x.Value > 0);
                s.ThisYearPlayedTotalMinutes = yearMinutesByGame.Values.Aggregate(0UL, (a, b) => a + b);

                var topYearEntry = yearMinutesByGame
                    .OrderByDescending(x => x.Value)
                    .FirstOrDefault();

                if (topYearEntry.Key != Guid.Empty)
                {
                    var topGame = PlayniteApi.Database.Games[topYearEntry.Key];
                    var topMinutes = topYearEntry.Value;

                    s.ThisYearTopGameName = Safe(topGame?.Name);
                    s.ThisYearTopGameId = topYearEntry.Key;
                    s.ThisYearTopGamePlaytime = AnikiHelperSettings.PlaytimeToString(topMinutes, false);

                    string coverPath = null;
                    if (!string.IsNullOrEmpty(topGame?.CoverImage))
                        coverPath = PlayniteApi.Database.GetFullFilePath(topGame.CoverImage);
                    if (string.IsNullOrEmpty(coverPath) && !string.IsNullOrEmpty(topGame?.BackgroundImage))
                        coverPath = PlayniteApi.Database.GetFullFilePath(topGame.BackgroundImage);
                    if (string.IsNullOrEmpty(coverPath) && !string.IsNullOrEmpty(topGame?.Icon))
                        coverPath = PlayniteApi.Database.GetFullFilePath(topGame.Icon);

                    s.ThisYearTopGameCoverPath = string.IsNullOrEmpty(coverPath) ? string.Empty : coverPath;

                    string bgPath = null;

                    if (!string.IsNullOrEmpty(topGame?.BackgroundImage) &&
                        !topGame.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bgPath = PlayniteApi.Database.GetFullFilePath(topGame.BackgroundImage);
                    }

                    if (string.IsNullOrEmpty(bgPath) &&
                        !string.IsNullOrEmpty(topGame?.CoverImage) &&
                        !topGame.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bgPath = PlayniteApi.Database.GetFullFilePath(topGame.CoverImage);
                    }

                    if (string.IsNullOrEmpty(bgPath) &&
                        !string.IsNullOrEmpty(topGame?.Icon) &&
                        !topGame.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bgPath = PlayniteApi.Database.GetFullFilePath(topGame.Icon);
                    }

                    s.ThisYearTopGameBackgroundPath = string.IsNullOrEmpty(bgPath) ? string.Empty : bgPath;
                }
                else
                {
                    s.ThisYearTopGameId = Guid.Empty;
                    s.ThisYearTopGameName = string.Empty;
                    s.ThisYearTopGamePlaytime = string.Empty;
                    s.ThisYearTopGameCoverPath = string.Empty;
                    s.ThisYearTopGameBackgroundPath = string.Empty;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Year snapshot calc failed; continuing with other sections.");
                s.ThisYearPlayedCount = 0;
                s.ThisYearPlayedTotalMinutes = 0;
                s.ThisYearTopGameId = Guid.Empty;
                s.ThisYearTopGameName = string.Empty;
                s.ThisYearTopGamePlaytime = string.Empty;
                s.ThisYearTopGameCoverPath = string.Empty;
                s.ThisYearTopGameBackgroundPath = string.Empty;
            }

            if (!runtimeOnly)
            {
                // === COMPLETION STATES
                var compDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in games)
                {
                    string name = null;
                    try { name = g.CompletionStatus?.Name; } catch { }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        try
                        {
                            if (g.CompletionStatusId != Guid.Empty)
                            {
                                var meta = PlayniteApi.Database.CompletionStatuses.Get(g.CompletionStatusId);
                                name = meta?.Name;
                            }
                        }
                        catch { }
                    }

                    if (string.IsNullOrWhiteSpace(name)) name = "—";
                    if (!compDict.ContainsKey(name)) compDict[name] = 0;
                    compDict[name]++;
                }

                s.CompletionStates.Clear();
                foreach (var kv in compDict.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
                {
                    s.CompletionStates.Add(new CompletionStatItem
                    {
                        Name = kv.Key,
                        Value = kv.Value,
                        PercentageString = PercentStringLocal(kv.Value, s.TotalCount)
                    });
                }
            }

            // === Listes rapides
            string SafeName(string name) => string.IsNullOrWhiteSpace(name) ? "(Unnamed Game)" : name;

            s.RecentPlayed.Clear();

            var recentPlayedList = games
                .Where(x => x.LastActivity != null)
                .OrderByDescending(x => x.LastActivity)
                .ToList();

            foreach (var g in recentPlayedList.Take(5))
            {
                var dt = g.LastActivity?.ToLocalTime().ToString("dd/MM/yyyy");
                s.RecentPlayed.Add(new QuickItem
                {
                    Name = SafeName(g.Name),
                    Value = dt
                });
            }

            var recentPlayedPool = recentPlayedList.Take(5).ToList();

            s.RecentPlayedBackgroundPath = recentPlayedPool.Count > 0
                ? GetBestHubCardBackgroundPath(recentPlayedPool[hubRandom.Next(recentPlayedPool.Count)])
                : string.Empty;

            if (!runtimeOnly)
            {
                s.RecentAdded.Clear();

                var recentAddedList = games
                    .Where(x => x.Added != null)
                    .OrderByDescending(x => x.Added)
                    .ToList();

                foreach (var g in recentAddedList.Take(5))
                {
                    var dt = g.Added?.ToLocalTime().ToString("dd/MM/yyyy");
                    s.RecentAdded.Add(new QuickItem
                    {
                        Name = SafeName(g.Name),
                        Value = dt
                    });
                }
            }


            s.NeverPlayed.Clear();

            var neverPlayedPool = games
                .Where(g => g.Playtime == 0UL && g.PlayCount == 0UL && g.LastActivity == null)
                .ToList();

            foreach (var g in neverPlayedPool
                .OrderBy(g => g.Added.HasValue ? g.Added.Value : DateTime.MinValue)
                .ThenBy(g => g.Name)
                .Take(5))
            {
                var addedStr = g.Added.HasValue
                    ? g.Added.Value.ToLocalTime().ToString("dd/MM/yyyy")
                    : string.Empty;

                s.NeverPlayed.Add(new QuickItem
                {
                    Name = Safe(g.Name),
                    Value = addedStr
                });
            }

            if (!runtimeOnly && !hubPage3CardsInitialized)
            {
                var recentAddedPool = games
                    .Where(g => g.Added != null)
                    .OrderByDescending(g => g.Added)
                    .Take(3)
                    .ToList();

                if (recentAddedPool.Count > 0)
                {
                    var selectedRecent = recentAddedPool[hubRandom.Next(recentAddedPool.Count)];

                    s.HubRecentAddedName = SafeName(selectedRecent.Name);
                    s.HubRecentAddedGameId = selectedRecent.Id;
                    s.HubRecentAddedDate = selectedRecent.Added.HasValue
                        ? selectedRecent.Added.Value.ToLocalTime().ToString("dd/MM/yyyy")
                        : string.Empty;
                    s.HubRecentAddedBackgroundPath = GetBestHubCardBackgroundPath(selectedRecent);
                }
                else
                {
                    s.HubRecentAddedGameId = Guid.Empty;
                    s.HubRecentAddedName = string.Empty;
                    s.HubRecentAddedDate = string.Empty;
                    s.HubRecentAddedBackgroundPath = string.Empty;
                }

                if (neverPlayedPool.Count > 0)
                {
                    var selectedNeverPlayed = neverPlayedPool[hubRandom.Next(neverPlayedPool.Count)];

                    s.HubNeverPlayedName = SafeName(selectedNeverPlayed.Name);
                    s.HubNeverPlayedGameId = selectedNeverPlayed.Id;
                    s.HubNeverPlayedDate = selectedNeverPlayed.Added.HasValue
                        ? selectedNeverPlayed.Added.Value.ToLocalTime().ToString("dd/MM/yyyy")
                        : string.Empty;
                    s.HubNeverPlayedBackgroundPath = GetBestHubCardBackgroundPath(selectedNeverPlayed);
                }
                else
                {
                    s.HubNeverPlayedGameId = Guid.Empty;
                    s.HubNeverPlayedName = string.Empty;
                    s.HubNeverPlayedDate = string.Empty;
                    s.HubNeverPlayedBackgroundPath = string.Empty;
                }

                hubPage3CardsInitialized = true;
            }


            if (!runtimeOnly)
            {
                // === GAME PROVIDERS
                var provDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in games)
                {
                    string src = null;
                    try { src = g.Source?.Name; } catch { }
                    if (string.IsNullOrWhiteSpace(src)) src = "—";
                    if (!provDict.ContainsKey(src)) provDict[src] = 0;
                    provDict[src]++;
                }

                s.GameProviders.Clear();
                foreach (var kv in provDict.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
                {
                    s.GameProviders.Add(new ProviderStatItem
                    {
                        Name = kv.Key,
                        Value = kv.Value,
                        PercentageString = PercentStringLocal(kv.Value, s.TotalCount)
                    });
                }
            }

            // Calcul du jeu suggéré à partir des stats + snapshot
            RecalcSuggestedGameSafe();
        }

        #region Menus

        private void OpenGameLink(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to open game link.");

                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Failed to open the selected link."
                    + Environment.NewLine
                    + Environment.NewLine
                    + ex.Message,
                    "Aniki Helper");
            }
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            base.OnGameInstalled(args);
            eventSoundService.PlayGameInstalled();
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            base.OnGameUninstalled(args);
            eventSoundService.PlayGameUninstalled();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            base.OnLibraryUpdated(args);
            eventSoundService.PlayLibraryUpdated();
        }

        private async void GenerateThumbnailsForGame(Guid gameId, string gameName)
        {
            if (Settings == null)
            {
                return;
            }

            await Settings.GenerateMediaThumbnailsForGameAsync(gameId, gameName);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var game = args?.Games?.FirstOrDefault();
            if (game == null)
            {
                yield break;
            }

            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen && !IsAnikiThemeActive())
            {
                yield break;
            }

            // ===== PLAYNITE GAME LINKS =====
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                if (game.Links != null && game.Links.Count > 0)
                {
                    foreach (var link in game.Links)
                    {
                        if (link == null || string.IsNullOrWhiteSpace(link.Url))
                        {
                            continue;
                        }

                        var linkName = string.IsNullOrWhiteSpace(link.Name)
                            ? link.Url
                            : link.Name;

                        yield return new GameMenuItem
                        {
                            MenuSection = "Aniki Helper|Game Links",
                            Description = linkName,
                            Action = (_) => OpenGameLink(link.Url)
                        };
                    }
                }
            }

            // ===== MEDIA GALLERY =====
            yield return new GameMenuItem
            {
                MenuSection = "Aniki Helper|Media Gallery",
                Description = ResourceProvider.GetString("LOCAnikiHelperGenerateThumbnailsForGame"),
                Action = (_) =>
                {
                    GenerateThumbnailsForGame(game.Id, game.Name);
                }
            };

            // ===== SPLASH SCREEN =====
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper|Splash Screen",
                    Description = ResourceProvider.GetString("LOCAnikiHelperManageSplashScreens"),
                    Action = (_) => OpenSplashScreenManager(game)
                };
            }

            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper|Splash Screen",
                    Description = ResourceProvider.GetString("LOCAnikiHelperSetGameSplashMinimumTimer"),
                    Action = (_) => SetGameSplashMinimumTimer(game)
                };
            }
            else
            {
                foreach (var presetItem in GetGameSplashMinimumTimerPresetItems(game))
                {
                    yield return presetItem;
                }
            }

            yield return new GameMenuItem
            {
                MenuSection = "Aniki Helper|Splash Screen",
                Description = ResourceProvider.GetString("LOCAnikiHelperResetGameSplashMinimumTimer"),
                Action = (_) => ResetGameSplashMinimumTimer(game)
            };
        }
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield break; // aucun menu
        }

        private class SteamStoreSearchResponse
        {
            public int total { get; set; }
            public List<SteamStoreSearchItem> items { get; set; }
        }

        private class SteamStoreSearchItem
        {
            public string type { get; set; }
            public string name { get; set; }
            public int id { get; set; }
        }

        #endregion
    }

    public static class VisualTreeHelpers
    {
        public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T match)
                    yield return match;

                foreach (var sub in FindVisualChildren<T>(child))
                    yield return sub;
            }
        }
    }
}
