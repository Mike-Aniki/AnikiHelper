using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Runtime.InteropServices;
using AnikiHelper.Services;
using System.Globalization;
using Microsoft.Win32;

namespace AnikiHelper
{
    public class AnikiHelper : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly SteamGlobalNewsService rssNewsService;
        private readonly EventSoundService eventSoundService;
        private readonly AnikiWindowManager anikiWindowManager;

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

        private GameLaunchSplashWindow currentGameLaunchSplash;
        private DateTime? currentGameLaunchSplashShownAt;
        private const int GameLaunchSplashMinimumDurationMs = 2000;
        private const int GameLaunchSplashForegroundCheckIntervalMs = 200;
        private const int GameLaunchSplashMaxWaitAfterGameStartedMs = 6000;
        private const int GameLaunchSplashPostFocusLossDelayMs = 1000;
        private const int GameLaunchSplashFocusLossStabilityMs = 400;
        private const string CustomSplashTagName = "[Aniki] Custom Splash";

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
                logger.Info($"[AnikiHelper] SetWelcomeHubState -> IsWelcomeHubOpen = {isOpen}");
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

            logger.Info($"[AnikiHelper] InitializeWelcomeHubState -> IsWelcomeHubOpen = {openAtStartup}");
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
                logger.Info($"[AnikiHelper] Cache load {sourceKey}: file not found -> {path}");
                return;
            }

            var cached = Serialization.FromJsonFile<List<SteamGlobalNewsItem>>(path);
            if (cached == null || cached.Count == 0)
            {
                logger.Info($"[AnikiHelper] Cache load {sourceKey}: file exists but contains 0 items -> {path}");
                return;
            }

            logger.Info($"[AnikiHelper] Cache load {sourceKey}: loaded {cached.Count} items from {path}");

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
            public string Html { get; set; }

            // True once this Steam game has already been checked at least once.
            public bool HasBeenScanned { get; set; }
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
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] ShowGlobalToast failed.");
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

        // Games Steam Update (RSS simplified)
        private readonly SteamUpdateLiteService steamUpdateService;
        private readonly DispatcherTimer steamUpdateTimer;
        private Playnite.SDK.Models.Game pendingUpdateGame;
        private readonly DispatcherTimer newsRotationTimer;
        private readonly DispatcherTimer suggestedRotationTimer;
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

        // Anti-freeze: 1 update à la fois + annulation si navigation rapide
        private readonly SemaphoreSlim steamUpdateGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource steamUpdateCts;

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
                // ignorer
            }
        }

        private void ResetSteamPlayerCount()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var s = Settings;
                    s.SteamCurrentPlayersString = string.Empty;
                    s.SteamCurrentPlayersAvailable = false;
                    s.SteamCurrentPlayersError = string.Empty;
                });
            }
            catch
            {
                // ignorer
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

                await UpdateSteamPlayerCountForGameAsync(g);
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

                logger.Info(
                    $"[AnikiHelper] RefreshGlobalSteamNewsAsync: itemsA={itemsA?.Count ?? 0}, itemsB={itemsB?.Count ?? 0}, " +
                    $"currentA={Settings.SteamGlobalNewsA?.Count ?? 0}, currentB={Settings.SteamGlobalNewsB?.Count ?? 0}");

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

                logger.Info(
                    $"[AnikiHelper] Refresh apply result: finalA={Settings.SteamGlobalNewsA?.Count ?? 0}, finalB={Settings.SteamGlobalNewsB?.Count ?? 0}, " +
                    $"sameA={sameA}, sameB={sameB}");

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
                var steamId = GetSteamGameId(game);
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
                logger.Info("[SteamUpdates] Recent games scan started.");
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

                    var steamId = GetSteamGameId(g);
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

                var steamId = GetSteamGameId(game);
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

                // Navigation path: do not call Steam here.
                // Network scans are handled by background scan methods.
                if (!hadUsableCache)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Settings.SteamUpdateError = "No update available";
                        Settings.SteamUpdateAvailable = false;
                        Settings.SteamUpdateIsNew = false;
                    });
                }

                // If we already have cache, don't refresh from Steam during navigation.
                // Background scans will refresh it later.
                if (hadUsableCache)
                {
                    RefreshSteamRecentUpdatesFromCache();
                    return;
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

                var result = await steamPlayerCountService.GetCurrentPlayersAsync(steamId);

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
                                Html = cleanedHtml,
                                HasBeenScanned = true
                            };



                            updated++;

                            // small throttle to avoid spamming the API
                            Task.Delay(150, progress.CancelToken).GetAwaiter().GetResult();
                        }

                        SaveSteamUpdatesCache(cache);
                        RefreshSteamRecentUpdatesFromCache();
                        logger.Info($"[AnikiHelper] InitializeSteamUpdatesCacheForAllGamesAsync completed. Steam games={steamGames.Count}, new cached entries={updated}");
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

        private string NormalizeProfileGenreKey(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
            {
                return null;
            }

            var g = genre.Trim().ToLowerInvariant();

            if (g.Contains("souls")) return "SOULSLIKE";
            if (g.Contains("metroidvania")) return "METROIDVANIA";

            if (g.Contains("action rpg") || g.Contains("action-rpg") || g.Contains("arpg"))
                return "ACTION_RPG";

            if (g.Contains("jrpg") || g.Contains("j-rpg") || g.Contains("rpg japonais"))
                return "JRPG";

            if (g == "rpg" || g.Contains("role-playing") || g.Contains("jeu de rôle"))
                return "RPG";

            if (g.Contains("survival horror"))
                return "SURVIVAL_HORROR";

            if (g.Contains("horror") || g.Contains("horreur"))
                return "HORROR";

            if (g.Contains("fps") || g.Contains("first-person shooter"))
                return "FPS";

            if (g.Contains("third-person shooter") || g.Contains("tps") || g.Contains("shooter"))
                return "SHOOTER";

            if (g.Contains("plateforme") || g.Contains("platformer"))
                return "PLATFORMER";

            if (g.Contains("course") || g.Contains("racing") || g.Contains("driving"))
                return "RACING";

            if (g.Contains("combat") || g.Contains("fighting"))
                return "FIGHTING";

            if (g.Contains("stratégie") || g.Contains("strategy") || g.Contains("tactical"))
                return "STRATEGY";

            if (g.Contains("simulation") || g.Contains("simulator"))
                return "SIMULATION";

            if (g.Contains("infiltration") || g.Contains("stealth"))
                return "STEALTH";

            if (g.Contains("aventure") || g.Contains("adventure"))
                return "ADVENTURE";

            return null;
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

                    scores[key] += gameMinutes;
                }
            }

            if (scores.Count == 0)
            {
                return "VARIETY";
            }

            var ordered = scores
                .OrderByDescending(x => x.Value)
                .ToList();

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
                    logger.Info($"[AnikiHelper] Created monthly snapshot: {file}");
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

                    ulong m0;
                    a.Minutes.TryGetValue(id, out m0);
                    ulong m1;
                    b.Minutes.TryGetValue(id, out m1);

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
                ulong baseMinutes;
                last.Minutes.TryGetValue(g.Id, out baseMinutes);
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
                    logger.Info($"[AnikiHelper] Monthly snapshot created for {monthStart:yyyy-MM} at {file}.");
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
                var now = DateTime.Now;
                var monthStart = new DateTime(now.Year, now.Month, 1);
                var file = GetMonthFilePath(monthStart);

                var snapshot = PlayniteApi.Database.Games.ToDictionary(g => g.Id, g => g.Playtime / 60UL);
                var json = Serialization.ToJson(snapshot, true);
                Directory.CreateDirectory(Path.GetDirectoryName(file) ?? GetMonthlyDir());
                File.WriteAllText(file, json);

                UpdateSnapshotInfoProperty(monthStart);
                RecalcStatsSafe();
                PlayniteApi.Dialogs.ShowMessage("Snapshot mensuel recréé. Les stats repartent de maintenant.", "AnikiHelper");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ResetMonthlySnapshot failed");
                PlayniteApi.Dialogs.ShowErrorMessage($"Échec du reset du snapshot : {ex.Message}", "AnikiHelper");
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

                logger.Info($"[AnikiHelper] Monthly backup exported: {exportFilePath}");
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

                logger.Info($"[AnikiHelper] Monthly backup imported: {importFilePath} | months={restoredMonths} entries={restoredEntries} skipped={skippedEntries}");

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

                logger.Info($"[AnikiHelper] Cleared dynamic color cache. Files deleted: {deleted}");
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
                    logger.Info($"[AnikiHelper] Dynamic color cache is older than {RequiredCacheVersion}. Clearing cache.");

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

        public AnikiHelper(IPlayniteAPI api) : base(api)
        {
            Instance = this;

            // Fullscreen or not
            isFullscreenMode = api?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;

            // ViewModel 
            SettingsVM = new AnikiHelperSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

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
            steamStoreService = new SteamStoreService(api, GetPluginUserDataPath());

            CleanupLegacyNewsCache();

            AddSettingsSupportSafe("AnikiHelper", "Settings");

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

                // Timer: flush du cache sur disque en différé (évite write pendant hover)
                steamUpdatesCacheFlushTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(20)
                };
                steamUpdatesCacheFlushTimer.Tick += (s, e) => FlushSteamUpdatesCacheIfNeeded();
                steamUpdatesCacheFlushTimer.Start();

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

                SaveSettingsSafe();
            });
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

        public void OpenWindow(string styleKey)
        {
            anikiWindowManager?.OpenWindow(styleKey);
        }

        public void OpenChildWindow(string styleKey)
        {
            anikiWindowManager?.OpenChildWindow(styleKey);
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

            if (!await WaitForPlayniteForegroundAsync(TimeSpan.FromSeconds(2)))
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

                await Task.Delay(100);

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
            base.OnApplicationStarted(args);
            eventSoundService.PlayApplicationStarted();

            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            var isAnikiThemeActive = IsAnikiThemeActive();

            try
            {
                if (isAnikiThemeActive)
                {
                    OnUi(() =>
                    {
                        Settings.IsWelcomeHubOpen = Settings.OpenWelcomeHubOnStartup;
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to initialize welcome hub startup state.");
            }

            hubPage3CardsInitialized = false;
            EnsureMonthlySnapshotSafe();
            RecalcStatsSafe();

            PlayniteApi.Database.DatabaseOpened += (_, __) =>
            {
                hubPage3CardsInitialized = false;
                EnsureMonthlySnapshotSafe();
                RecalcStatsSafe();
            };

            if (PlayniteApi?.Database?.Games is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged += (_, __) => RecalcStatsSafe();
            }

            try
            {
                OnUi(() =>
                {
                    Settings.SessionNotificationStamp = string.Empty;
                    Settings.SessionNotificationArmed = false;
                });
            }
            catch { }


            // --- UI Fullscreen ---
            if (isAnikiThemeActive)
            {
                AddonsUpdateStyler.Start();
            }

            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                () =>
                {
                    EnsureDynamicColorCacheVersion();
                    DynamicAuto.Init(PlayniteApi);
                },
                System.Windows.Threading.DispatcherPriority.Loaded
            );

            if (isAnikiThemeActive)
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                    () => SettingsWindowStyler.Start(),
                    System.Windows.Threading.DispatcherPriority.Loaded
                );
            }

            if (isAnikiThemeActive)
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                    () => VisualPackBackgroundComposer.Start(),
                    System.Windows.Threading.DispatcherPriority.Loaded
                );
            }

            if (isAnikiThemeActive && Settings.ShutdownVideoEnabled)
            {
                FullscreenShutdownVideoHook.Start(this);
            }

            if (isAnikiThemeActive && Settings.StartupIntroVideoEnabled)
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                    async () =>
                    {
                        try
                        {
                            await ShowStartupVideoAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "[AnikiHelper] Startup video launch failed.");
                        }
                    },
                    System.Windows.Threading.DispatcherPriority.Send
                );
            }

            if (isAnikiThemeActive)
            {
                newsRotationTimer?.Start();
                _ = StartSuggestedRotationWithDelayAsync(3000);
            }

            // --- News globales Steam ---
            try
            {
                LoadNewsFromCacheIfNeeded();
            }
            catch { }

            try
            {
                RefreshSteamRecentUpdatesFromCache();
            }
            catch { }


            // 2) lancer un scan RSS différé de 10s, limité à 1 fois / 3h
            if (Settings.NewsScanEnabled)
            {
                try
                {
                    _ = ScheduleGlobalSteamNewsRefreshAsync();
                }
                catch { }
            }

            // Playnite News
            try
            {
                _ = SchedulePlayniteNewsRefreshAsync();
            }
            catch { }

            


            // Random login screen
            try
            {
                OnUi(() =>
                {
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
                });

                SaveSettingsSafe();
            }
            catch { }


            try
            {
                if (isAnikiThemeActive)
                {
                    TryAskForSteamUpdateCacheOnStartup();
                }
            }
            catch { }

            try
            {
                _ = ScheduleSteamRecentUpdatesScanAsync(30);
            }
            catch { }
        }

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
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

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            eventSoundService.PlayApplicationStopped();

            try { steamUpdateTimer?.Stop(); } catch { }
            try { steamUpdatesCacheFlushTimer?.Stop(); } catch { }
            try { newsRotationTimer?.Stop(); } catch { }
            try { suggestedRotationTimer?.Stop(); } catch { }

            try
            {
                steamUpdateCts?.Cancel();
                steamUpdateCts?.Dispose();
            }
            catch { }

            try { FullscreenShutdownVideoHook.Stop(); } catch { }

            base.OnApplicationStopped(args);
        }


        private async Task ScheduleSteamRecentUpdatesScanAsync(int maxGames)
        {
            try
            {
                logger.Info("[SteamUpdates] Background scan scheduled.");
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


        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            base.OnGameSelected(args);

            Interlocked.Exchange(ref lastSteamUpdateUserActivityTicks, Environment.TickCount);

            var g = args?.NewValue?.FirstOrDefault();
            if (g == null)
            {
                ResetSteamUpdate();
                ResetSteamPlayerCount();
                return;
            }

            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                ResetSteamUpdate();
                ResetSteamPlayerCount();
                return;
            }

            pendingUpdateGame = g;
            steamUpdateTimer.Stop();
            steamUpdateTimer.Start();
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            base.OnGameStarting(args);
            eventSoundService.PlayGameStarting();

            try
            {
                if (!(Settings?.GameLaunchSplashEnabled ?? false))
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

                var game = args?.Game;
                if (game == null)
                {
                    return;
                }

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CloseCurrentGameLaunchSplash();

                    var bgPath = GetBestGameLaunchSplashBackground(game);
                    currentGameLaunchSplash = new GameLaunchSplashWindow(game, bgPath);
                    currentGameLaunchSplash.Show();
                    currentGameLaunchSplashShownAt = DateTime.Now;
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to show game launch splash.");
            }
        }

        private string GetBestGameLaunchSplashBackground(Game game)
        {
            try
            {
                var custom = GetStoredCustomSplashPath(game);
                if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
                {
                    return custom;
                }

                if (!string.IsNullOrWhiteSpace(game?.BackgroundImage))
                {
                    var bg = PlayniteApi.Database.GetFullFilePath(game.BackgroundImage);
                    if (!string.IsNullOrWhiteSpace(bg) && File.Exists(bg))
                    {
                        return bg;
                    }
                }

                if (!string.IsNullOrWhiteSpace(game?.CoverImage))
                {
                    var cover = PlayniteApi.Database.GetFullFilePath(game.CoverImage);
                    if (!string.IsNullOrWhiteSpace(cover) && File.Exists(cover))
                    {
                        return cover;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to resolve splash background.");
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


        private void CloseCurrentGameLaunchSplash()
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    currentGameLaunchSplash = null;
                    currentGameLaunchSplashShownAt = null;
                    return;
                }

                dispatcher.Invoke(async () =>
                {
                    try
                    {
                        if (currentGameLaunchSplash != null)
                        {

                            var splash = currentGameLaunchSplash;
                            currentGameLaunchSplash = null;
                            currentGameLaunchSplashShownAt = null;

                            await splash.BeginCloseAsync();
                            splash.Close();
                        }
                    }
                    catch
                    {
                        currentGameLaunchSplash = null;
                        currentGameLaunchSplashShownAt = null;
                    }
                });
            }
            catch
            {
                currentGameLaunchSplash = null;
                currentGameLaunchSplashShownAt = null;
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            base.OnGameStarted(args);
            eventSoundService.PlayGameStarted();

            Task.Run(async () =>
            {
                try
                {
                    int remainingMinimumDelay = 0;

                    if (currentGameLaunchSplashShownAt.HasValue)
                    {
                        var elapsed = (int)(DateTime.Now - currentGameLaunchSplashShownAt.Value).TotalMilliseconds;
                        remainingMinimumDelay = Math.Max(0, GameLaunchSplashMinimumDurationMs - elapsed);
                    }


                    if (remainingMinimumDelay > 0)
                    {
                        await Task.Delay(remainingMinimumDelay);
                    }

                    int waitedAfterStarted = 0;

                    while (waitedAfterStarted < GameLaunchSplashMaxWaitAfterGameStartedMs)
                    {
                        if (!IsPlayniteForegroundWindow())
                        {

                            await Task.Delay(GameLaunchSplashFocusLossStabilityMs);

                            if (!IsPlayniteForegroundWindow())
                            {
                                await Task.Delay(GameLaunchSplashPostFocusLossDelayMs);

                                CloseCurrentGameLaunchSplash();
                                break;
                            }
                            else
                            {
                                logger.Info("[AnikiHelper] Playnite regained foreground during stability check. Keeping splash open.");
                            }
                        }

                        await Task.Delay(GameLaunchSplashForegroundCheckIntervalMs);
                        waitedAfterStarted += GameLaunchSplashForegroundCheckIntervalMs;
                    }

                    if (waitedAfterStarted >= GameLaunchSplashMaxWaitAfterGameStartedMs)
                    {
                        CloseCurrentGameLaunchSplash();
                    }
                }
                catch (Exception)
                {
                    CloseCurrentGameLaunchSplash();
                }
            });

            var g = args?.Game;
            if (g == null) return;

            sessionStartAt[g.Id] = DateTime.Now;
            sessionStartPlaytimeMinutes[g.Id] = (g.Playtime / 60UL);
        }


        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            base.OnGameStopped(args);
            eventSoundService.PlayGameStopped();
            CloseCurrentGameLaunchSplash();

            var g = args?.Game;
            if (g == null)
            {
                return;
            }

            // 1) Session duration
            var start = sessionStartAt.ContainsKey(g.Id) ? sessionStartAt[g.Id] : DateTime.Now;
            var elapsed = DateTime.Now - start;
            var sessionMinutes = (int)Math.Max(0, Math.Round(elapsed.TotalMinutes));

            // 2) Total playtime
            var totalMinutes = (int)(g.Playtime / 60UL);
            if (totalMinutes <= 0 && sessionStartPlaytimeMinutes.ContainsKey(g.Id))
            {
                totalMinutes = (int)sessionStartPlaytimeMinutes[g.Id] + sessionMinutes;
            }

            // 3) Push vers Settings 
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                var s = Settings;

                s.SessionGameId = g.Id;
                s.SessionGameName = string.IsNullOrWhiteSpace(g.Name) ? "(Unnamed Game)" : g.Name;
                s.SessionDurationString = FormatHhMmFromMinutes(sessionMinutes);
                s.SessionTotalPlaytimeString = FormatHhMmFromMinutes(Math.Max(0, totalMinutes));

                string bgPath = null;

                if (!string.IsNullOrEmpty(g.BackgroundImage) &&
                    !g.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    bgPath = PlayniteApi.Database.GetFullFilePath(g.BackgroundImage);
                }

                if (string.IsNullOrEmpty(bgPath) &&
                    !string.IsNullOrEmpty(g.CoverImage) &&
                    !g.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    bgPath = PlayniteApi.Database.GetFullFilePath(g.CoverImage);
                }

                if (string.IsNullOrEmpty(bgPath) &&
                    !string.IsNullOrEmpty(g.Icon) &&
                    !g.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    bgPath = PlayniteApi.Database.GetFullFilePath(g.Icon);
                }

                s.SessionGameBackgroundPath = string.IsNullOrEmpty(bgPath) ? string.Empty : bgPath;

                s.SessionNotificationStamp = Guid.NewGuid().ToString();
                s.SessionNotificationFlip = !s.SessionNotificationFlip;
                s.SessionNotificationArmed = true;

                SaveSettingsSafe();
            }));

            EnsureGameInCurrentMonthSnapshot(g, sessionMinutes);

            // 4) Cache cleanup
            sessionStartAt.Remove(g.Id);
            sessionStartPlaytimeMinutes.Remove(g.Id);

            // 5) Recalcul stats + snapshot
            EnsureMonthlySnapshotSafe();
            RecalcStatsSafe(true);
        }

        #endregion

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

            s.ProfileGenreKey = BuildProfileGenreKey(played);
            s.ProfileGenreLabel = GetLocalizedProfileGenreLabel(s.ProfileGenreKey);
            s.ProfileTopPlatformName = topPlatform;
            s.ProfileTopFranchiseName = topFranchise;
            s.ProfileTopTagName = topTag;

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
                            ulong m0 = 0;
                            ulong m1 = 0;

                            a.Minutes.TryGetValue(id, out m0);
                            b.Minutes.TryGetValue(id, out m1);

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

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var game = args?.Games?.FirstOrDefault();
            if (game == null)
            {
                yield break;
            }

            var customPath = GetStoredCustomSplashPath(game);

            if (string.IsNullOrWhiteSpace(customPath))
            {
                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper",
                    Description = ResourceProvider.GetString("LOCAnikiHelperSetCustomSplashImage"),
                    Action = (_) => SetCustomSplashImage(game)
                };

                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper",
                    Description = ResourceProvider.GetString("LOCAnikiHelperOpenCustomSplashFolder"),
                    Action = (_) => OpenCustomSplashImageFolder(game)
                };
            }
            else
            {
                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper",
                    Description = ResourceProvider.GetString("LOCAnikiHelperReplaceCustomSplashImage"),
                    Action = (_) => SetCustomSplashImage(game)
                };

                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper",
                    Description = ResourceProvider.GetString("LOCAnikiHelperPreviewCustomSplashImage"),
                    Action = (_) => PreviewCustomSplashImage(game)
                };

                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper",
                    Description = ResourceProvider.GetString("LOCAnikiHelperRemoveCustomSplashImage"),
                    Action = (_) => RemoveCustomSplashImage(game)
                };

                yield return new GameMenuItem
                {
                    MenuSection = "Aniki Helper",
                    Description = ResourceProvider.GetString("LOCAnikiHelperOpenCustomSplashFolder"),
                    Action = (_) => OpenCustomSplashImageFolder(game)
                };
            }
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield break; // aucun menu
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
