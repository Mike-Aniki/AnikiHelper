using AnikiHelper.Services;
using AnikiHelper.Services.SplashScreen;
using AnikiHelper.Services.SteamFriends;
using AnikiHelper.Services.Controller;
using AnikiHelper.Services.InGameOverlay;
using AnikiHelper.Services.MediaGallery;
using AnikiHelper.Services.AnikiThemeSettings;
using AnikiHelper.Services.UI;
using AnikiHelper.Services.EasterEgg;
using Microsoft.Win32;
using Newtonsoft.Json;
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

        // Hub News can generate hundreds of per-item DEBUG lines on startup.
        // Keep these logs silent by default so the Hub stays smooth and the log file stays readable.
        private void HubNewsDebug(string message)
        {
            // Intentionally quiet. Keep Info/Warn/Error summaries only.
        }

        private void HubNewsDebug(Exception ex, string message)
        {
            // Intentionally quiet. Keep Info/Warn/Error summaries only.
        }

        private void InitializeLuckyDaySession()
        {
            try
            {
                var sw = Stopwatch.StartNew();

                OnUi(() =>
                {
                    var swUi = Stopwatch.StartNew();
                    var rand = new Random();
                    const int max = 42;

                    int pick;
                    if (Settings.LastLoginRandomIndex >= 1 && Settings.LastLoginRandomIndex <= max && max > 1)
                    {
                        do
                        {
                            pick = rand.Next(1, max + 1);
                        }
                        while (pick == Settings.LastLoginRandomIndex);
                    }
                    else
                    {
                        pick = rand.Next(1, max + 1);
                    }

                    Settings.LoginRandomIndex = pick;
                    Settings.LastLoginRandomIndex = pick;

                    if (Settings.IsLuckyDay)
                    {
                        Settings.LuckyStyleIndex = rand.Next(1, 3);
                        QueueLuckyDaySoundAfterStartup();
                    }
                    else
                    {
                        Settings.LuckyStyleIndex = 0;
                    }

                    DebugLog($"[AnikiHelper][LuckyDay] Random login pick took {swUi.ElapsedMilliseconds}ms | pick={pick} | lucky={Settings.IsLuckyDay} | luckyStyle={Settings.LuckyStyleIndex}");
                });

                SaveSettingsSafe();
                DebugLog($"[AnikiHelper][LuckyDay] Session init took {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Lucky Day session init failed.");
            }
        }


        private void QueueLuckyDaySoundAfterStartup()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    eventSoundService?.PlayLuckyDay();
                    return;
                }

                dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        // Do not open/read the audio file during the first startup render.
                        await Task.Delay(500);
                        eventSoundService?.PlayLuckyDay();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to play delayed Lucky Day sound.");
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to queue Lucky Day sound.");
            }
        }

        private readonly SteamGlobalNewsService rssNewsService;
        private readonly EventSoundService eventSoundService;
        private readonly AnikiWindowManager anikiWindowManager;
        private readonly InGameOverlayService inGameOverlayService;
        private readonly AnikiThemeSettingsService anikiThemeSettingsService;
        private readonly NavigationFixService horizontalFocusFixService;
        private readonly KonamiCodeService konamiCodeService;

        private const string RequiredAnikiAuthorCredit = "Mike Aniki";
        private const string RequiredAnikiBrandName = "Aniki";

        private string cachedAnikiThemeId;
        private bool? cachedAnikiThemeAuthorization;

        private readonly bool isFullscreenMode;

        // Video 
        private bool startupVideoSequenceRunning;
        private static readonly TimeSpan StartupVideoDuration = TimeSpan.FromSeconds(7);
        private static readonly TimeSpan StartupVideoFailSafeTimeout = TimeSpan.FromSeconds(30);
        private const string StartupVideoFileName = "Startup.mp4";
        private const string StartupLuckyDay1VideoFileName = "Startup_LuckyDay1.mp4";
        private const string StartupLuckyDay2VideoFileName = "Startup_LuckyDay2.mp4";

        private bool shutdownVideoSequenceRunning;
        private static readonly TimeSpan ShutdownVideoDuration = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShutdownVideoFailSafeTimeout = TimeSpan.FromSeconds(30);

        private const string ShutdownThemeFolderName = "Aniki_ReMake_bb8728bd-ac83-4324-88b1-ee5c586527d1";
        private const string ShutdownVideoFolderName = "Startup Video";
        private const string ShutdownVideoFileName = "Shutdown.mp4";
        private const string ShutdownLuckyDay1VideoFileName = "Shutdown_LuckyDay1.mp4";
        private const string ShutdownLuckyDay2VideoFileName = "Shutdown_LuckyDay2.mp4";

        private SplashScreenRuntimeService splashScreenRuntimeService;
        private const int GameLaunchSplashMinimumDurationMs = 2400;
        private const int GameLaunchSplashMaxWaitAfterGameStartedMs = 15000;
        private const int GameLaunchSplashMaximumMinimumDurationMs = 600000;
        private const string CustomSplashTagName = "[Aniki] Custom Splash";

        private bool uniPlaySongGameStartingPauseHeld;
        private Guid? uniPlaySongGameStartingPauseGameId;

        private readonly System.Threading.SemaphoreSlim steamStoreOpenLock = new System.Threading.SemaphoreSlim(1, 1);
        private DateTime lastSteamStoreOpenRequestUtc = DateTime.MinValue;
        private DateTime lastSteamStoreCacheOnlyLoadUtc = DateTime.MinValue;
        private DateTime lastSteamStoreAuthProbeUtc = DateTime.MinValue;
        private static readonly TimeSpan SteamStoreOpenRequestThrottle = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SteamStoreCacheOnlyLoadThrottle = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan SteamStoreAuthProbeReuseWindow = TimeSpan.FromSeconds(30);
        private DateTime lastMediaGalleryRefreshShortcutUtc = DateTime.MinValue;
        private DateTime lastSteamAuthToastUtc = DateTime.MinValue;
        private DateTime lastSteamAuthRequiredToastUtc = DateTime.MinValue;
        private DateTime lastControllerInputUtc = DateTime.MinValue;
        private readonly System.Threading.SemaphoreSlim startupSteamNotificationRefreshLock = new System.Threading.SemaphoreSlim(1, 1);
        private const int MaxWishlistNotificationsPerRefresh = 3;
        private static readonly TimeSpan StartupSteamAuthCheckDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan StartupSteamAuthReminderInterval = TimeSpan.FromDays(1);
        private static readonly TimeSpan StartupSteamNotificationInitialDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan StartupSteamNotificationRetryDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StartupSteamNotificationDailyInterval = TimeSpan.FromDays(1);
        private const int StartupSteamNotificationMaxIdleAttempts = 16;


        // Steam Store loading progress animation
        private int steamStoreProgressAnimationToken = 0;

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
        private bool hubStartupCachePrimed = false;

        private bool hubStartupVisibleCacheLoaded = false;

        private class WelcomeHubStartupCache
        {
            public DateTime SavedUtc { get; set; }

            public int TotalCount { get; set; }
            public int InstalledCount { get; set; }
            public int NotInstalledCount { get; set; }
            public int HiddenCount { get; set; }
            public int FavoriteCount { get; set; }

            public ulong TotalPlaytimeMinutes { get; set; }
            public ulong AveragePlaytimeMinutes { get; set; }

            public string ProfileGenreKey { get; set; }
            public string ProfileGenreLabel { get; set; }
            public string ProfileTopPlatformName { get; set; }
            public string ProfileTopFranchiseName { get; set; }
            public string ProfileTopTagName { get; set; }

            public int ThisMonthPlayedCount { get; set; }
            public ulong ThisMonthPlayedTotalMinutes { get; set; }
            public string ThisMonthTopGameName { get; set; }
            public string ThisMonthTopGamePlaytime { get; set; }
            public string ThisMonthTopGameCoverPath { get; set; }
            public string ThisMonthTopGameBackgroundPath { get; set; }
            public Guid ThisMonthTopGameId { get; set; }

            public int ThisYearPlayedCount { get; set; }
            public ulong ThisYearPlayedTotalMinutes { get; set; }
            public string ThisYearTopGameName { get; set; }
            public string ThisYearTopGamePlaytime { get; set; }
            public string ThisYearTopGameCoverPath { get; set; }
            public string ThisYearTopGameBackgroundPath { get; set; }
            public Guid ThisYearTopGameId { get; set; }

            public string RecentPlayedBackgroundPath { get; set; }

            public string HubRecentAddedName { get; set; }
            public string HubRecentAddedDate { get; set; }
            public string HubRecentAddedBackgroundPath { get; set; }
            public Guid HubRecentAddedGameId { get; set; }

            public string HubNeverPlayedName { get; set; }
            public string HubNeverPlayedDate { get; set; }
            public string HubNeverPlayedBackgroundPath { get; set; }
            public Guid HubNeverPlayedGameId { get; set; }

            public List<TopPlayedItem> TopPlayed { get; set; } = new List<TopPlayedItem>();
            public List<CompletionStatItem> CompletionStates { get; set; } = new List<CompletionStatItem>();
            public List<ProviderStatItem> GameProviders { get; set; } = new List<ProviderStatItem>();
            public List<QuickItem> RecentPlayed { get; set; } = new List<QuickItem>();
            public List<QuickItem> RecentAdded { get; set; } = new List<QuickItem>();
            public List<QuickItem> NeverPlayed { get; set; } = new List<QuickItem>();

            public List<SteamRecentUpdateItem> SteamRecentUpdates { get; set; } = new List<SteamRecentUpdateItem>();
            public List<HubLibraryRecommendedGameItem> HubLibraryRecommendedGames { get; set; } = new List<HubLibraryRecommendedGameItem>();
        }

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
        // Rotation équilibrée : 3 news Source A + 2 news Source B + 1 dernière news Playnite/add-ons, puis ordre aléatoire.
        private void UpdateLatestNewsRotationFromList(IList<SteamGlobalNewsItem> items)
        {
            try
            {
                latestNewsRotation.Clear();
                latestNewsRotationIndex = 0;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sourceACount = 0;
                var sourceBCount = 0;
                var playniteCount = 0;

                Func<SteamGlobalNewsItem, string, bool> addUnique = (item, source) =>
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Title))
                    {
                        return false;
                    }

                    var key = MakePlayniteNewsKey(item);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        key = item.Title ?? string.Empty;
                    }

                    if (!seen.Add(key))
                    {
                        HubNewsDebug($"[Hub News] rotation skip duplicate | source={source} | title={ShortForLog(item.Title)}");
                        return false;
                    }

                    latestNewsRotation.Add(item);
                    HubNewsDebug($"[Hub News] rotation add | source={source} | title={ShortForLog(item.Title)} | game={Safe(item.GameName)} | date={item.DateString ?? string.Empty}");
                    return true;
                };

                if (items != null)
                {
                    foreach (var item in items
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title))
                        .OrderByDescending(x => x.PublishedUtc)
                        .Take(3))
                    {
                        if (addUnique(item, "sourceA"))
                        {
                            sourceACount++;
                        }
                    }
                }

                var sourceB = Settings?.SteamGlobalNewsB;
                if (sourceB != null && sourceB.Count > 0)
                {
                    foreach (var item in sourceB
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title))
                        .OrderByDescending(x => x.PublishedUtc)
                        .Take(2))
                    {
                        if (addUnique(item, "sourceB"))
                        {
                            sourceBCount++;
                        }
                    }
                }

                var playniteNews = Settings?.PlayniteNews;
                if (playniteNews != null && playniteNews.Count > 0)
                {
                    var latestPlayniteNews = playniteNews
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title))
                        .OrderByDescending(x => x.PublishedUtc)
                        .FirstOrDefault();

                    if (addUnique(latestPlayniteNews, "playnite-updates"))
                    {
                        playniteCount++;
                    }
                }

                if (latestNewsRotation.Count > 1)
                {
                    for (var i = latestNewsRotation.Count - 1; i > 0; i--)
                    {
                        var j = hubRandom.Next(i + 1);
                        if (i == j)
                        {
                            continue;
                        }

                        var tmp = latestNewsRotation[i];
                        latestNewsRotation[i] = latestNewsRotation[j];
                        latestNewsRotation[j] = tmp;
                    }

                    HubNewsDebug($"[Hub News] rotation order shuffled | count={latestNewsRotation.Count} | random=True");
                }

                ApplyLatestNewsSnapshot(latestNewsRotation.Count > 0 ? latestNewsRotation[0] : null);

                logger.Info($"[Hub News] rotation refreshed | count={latestNewsRotation.Count} | sourceA={sourceACount}/3 | sourceB={sourceBCount}/2 | playniteUpdates={playniteCount}/1 | random=True");

                for (var i = 0; i < latestNewsRotation.Count; i++)
                {
                    var item = latestNewsRotation[i];
                    HubNewsDebug($"[Hub News] rotation item | index={i + 1}/{latestNewsRotation.Count} | title={ShortForLog(item?.Title)} | game={Safe(item?.GameName)} | date={item?.DateString ?? string.Empty}");
                }
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

        private void UpdateLibraryNewsRotationFromUpdates(IList<SteamRecentUpdateItem> fallbackUpdates)
        {
            try
            {
                const int targetRotationCount = 6;
                const int targetPrimaryCount = 2;
                const int targetUpdateCount = 2;
                const int targetSecondaryCount = 2;

                libraryNewsRotation.Clear();
                libraryNewsRotationIndex = 0;

                var seenGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Func<SteamRecentUpdateItem, string, bool> addUnique = (item, source) =>
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Title) || libraryNewsRotation.Count >= targetRotationCount)
                    {
                        return false;
                    }

                    var gameKey = GetLibraryNewsGameKey(item);
                    if (string.IsNullOrWhiteSpace(gameKey))
                    {
                        gameKey = NormalizeForKey(item.GameName ?? string.Empty);
                    }

                    if (!string.IsNullOrWhiteSpace(gameKey) && seenGames.Contains(gameKey))
                    {
                        HubNewsDebug($"[Hub Library News] rotation skip duplicate game | source={source} | gameKey={gameKey} | game={Safe(item.GameName)} | title={ShortForLog(item.Title)}");
                        return false;
                    }

                    var itemKey = $"{gameKey}|{NormalizeForKey(item.Title ?? string.Empty)}";
                    if (!seenItems.Add(itemKey))
                    {
                        HubNewsDebug($"[Hub Library News] rotation skip duplicate item | source={source} | game={Safe(item.GameName)} | title={ShortForLog(item.Title)}");
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(gameKey))
                    {
                        seenGames.Add(gameKey);
                    }

                    libraryNewsRotation.Add(item);
                    HubNewsDebug($"[Hub Library News] rotation add | source={source} | badge={GetLibraryNewsBadgeText(item)} | appId={Safe(item.SteamAppId)} | game={Safe(item.GameName)} | title={ShortForLog(item.Title)}");
                    return true;
                };

                Func<IEnumerable<SteamRecentUpdateItem>, string, int, int> addCategory = (items, source, maxAdd) =>
                {
                    var added = 0;
                    if (items == null || maxAdd <= 0)
                    {
                        return 0;
                    }

                    foreach (var item in items)
                    {
                        if (libraryNewsRotation.Count >= targetRotationCount || added >= maxAdd)
                        {
                            break;
                        }

                        if (addUnique(item, source))
                        {
                            added++;
                        }
                    }

                    return added;
                };

                var gameNewsItems = BuildLibraryNewsItemsFromGameNewsCache()
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title))
                    .ToList();

                var primaryItems = gameNewsItems
                    .Where(IsLibraryNewsPrimaryBadge)
                    .ToList();

                var secondaryItems = gameNewsItems
                    .Where(IsLibraryNewsSecondaryBadge)
                    .ToList();

                var saleItems = gameNewsItems
                    .Where(IsLibraryNewsSaleBadge)
                    .ToList();

                var globalUpdateItems = (fallbackUpdates ?? new List<SteamRecentUpdateItem>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title))
                    .Where(IsHubLibraryGlobalUpdateFresh)
                    .OrderByDescending(x => ParseSteamNewsDateUtc(x.DateString))
                    .ToList();

                // Rotation équilibrée From Your Library :
                // 2 meilleurs DLC/EVENT, 2 meilleures UPDATE globales, 2 meilleures NEWS/TRAILER/GAMEPLAY/ANNOUNCE.
                // SALE reste seulement un fallback si une catégorie n'a pas assez de contenu récent.
                var primaryAdded = addCategory(primaryItems, "game-news-cache-primary-2", targetPrimaryCount);
                var updateAdded = addCategory(globalUpdateItems, "global-updates-priority-2", targetUpdateCount);
                var secondaryAdded = addCategory(secondaryItems, "game-news-cache-secondary-2", targetSecondaryCount);

                HubNewsDebug($"[Hub Library News] rotation category summary | target={targetRotationCount} | primary={primaryAdded}/{targetPrimaryCount} | updates={updateAdded}/{targetUpdateCount} | secondary={secondaryAdded}/{targetSecondaryCount} | count={libraryNewsRotation.Count}");

                // Fill intelligent si une catégorie ne peut pas fournir assez de contenu.
                if (libraryNewsRotation.Count < targetRotationCount)
                {
                    addCategory(globalUpdateItems, "global-updates-extra-fill", targetRotationCount - libraryNewsRotation.Count);
                }

                if (libraryNewsRotation.Count < targetRotationCount)
                {
                    addCategory(primaryItems, "game-news-cache-primary-extra-fill", targetRotationCount - libraryNewsRotation.Count);
                }

                if (libraryNewsRotation.Count < targetRotationCount)
                {
                    addCategory(secondaryItems, "game-news-cache-secondary-extra-fill", targetRotationCount - libraryNewsRotation.Count);
                }

                if (libraryNewsRotation.Count < targetRotationCount)
                {
                    addCategory(saleItems, "game-news-cache-sale-last-fill", targetRotationCount - libraryNewsRotation.Count);
                }

                if (libraryNewsRotation.Count > 1)
                {
                    for (var i = libraryNewsRotation.Count - 1; i > 0; i--)
                    {
                        var j = hubRandom.Next(i + 1);
                        if (i == j)
                        {
                            continue;
                        }

                        var tmp = libraryNewsRotation[i];
                        libraryNewsRotation[i] = libraryNewsRotation[j];
                        libraryNewsRotation[j] = tmp;
                    }

                    HubNewsDebug($"[Hub Library News] rotation order shuffled | count={libraryNewsRotation.Count} | random=True");
                }

                ApplyLibraryNewsSnapshot(libraryNewsRotation.Count > 0 ? libraryNewsRotation[0] : null);

                logger.Info($"[Hub Library News] rotation refreshed | count={libraryNewsRotation.Count} | source=balanced-2-dlc-event-2-global-update-2-news-random | uniqueGames={seenGames.Count}");

                for (var i = 0; i < libraryNewsRotation.Count; i++)
                {
                    var item = libraryNewsRotation[i];
                    HubNewsDebug($"[Hub Library News] rotation item | index={i + 1}/{libraryNewsRotation.Count} | badge={GetLibraryNewsBadgeText(item)} | appId={Safe(item?.SteamAppId)} | game={Safe(item?.GameName)} | title={ShortForLog(item?.Title)} | date={item?.DateString ?? string.Empty}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to update LibraryNews rotation.");
            }
        }

        private void ApplyLibraryNewsSnapshot(SteamRecentUpdateItem item)
        {
            try
            {
                OnUi(() =>
                {
                    if (item == null)
                    {
                        Settings.LibraryNewsTitle = string.Empty;
                        Settings.LibraryNewsGameName = string.Empty;
                        Settings.LibraryNewsDateString = string.Empty;
                        Settings.LibraryNewsSummary = string.Empty;
                        Settings.LibraryNewsBadgeText = string.Empty;
                        Settings.LibraryNewsImagePath = string.Empty;
                        Settings.LibraryNewsImagePathA = string.Empty;
                        Settings.LibraryNewsImagePathB = string.Empty;
                        Settings.LibraryNewsShowLayerB = false;

                        libraryNewsCrossfadeInitialized = false;
                        return;
                    }

                    Settings.LibraryNewsTitle = item.Title ?? string.Empty;
                    Settings.LibraryNewsGameName = item.GameName ?? string.Empty;
                    Settings.LibraryNewsDateString = item.DateString ?? string.Empty;
                    Settings.LibraryNewsSummary = BuildLibraryNewsSummary(item);
                    Settings.LibraryNewsBadgeText = GetLibraryNewsBadgeText(item);

                    var imagePath = !string.IsNullOrWhiteSpace(item.BackgroundPath)
                        ? item.BackgroundPath
                        : (item.CoverPath ?? string.Empty);

                    Settings.LibraryNewsImagePath = imagePath;

                    if (!libraryNewsCrossfadeInitialized)
                    {
                        Settings.LibraryNewsImagePathA = imagePath;
                        Settings.LibraryNewsImagePathB = imagePath;
                        Settings.LibraryNewsShowLayerB = false;
                        libraryNewsCrossfadeInitialized = true;
                        return;
                    }

                    if (Settings.LibraryNewsShowLayerB)
                    {
                        Settings.LibraryNewsImagePathA = imagePath;
                        Settings.LibraryNewsShowLayerB = false;
                    }
                    else
                    {
                        Settings.LibraryNewsImagePathB = imagePath;
                        Settings.LibraryNewsShowLayerB = true;
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to apply LibraryNews snapshot.");
            }
        }

        private void RotateLibraryNewsIfNeeded()
        {
            if (libraryNewsRotation.Count <= 1)
            {
                return;
            }

            libraryNewsRotationIndex++;
            if (libraryNewsRotationIndex >= libraryNewsRotation.Count)
            {
                libraryNewsRotationIndex = 0;
            }

            ApplyLibraryNewsSnapshot(libraryNewsRotation[libraryNewsRotationIndex]);
        }

        private static string BuildLibraryNewsSummary(SteamRecentUpdateItem item)
        {
            try
            {
                var raw = item?.Html ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return item?.DateString ?? string.Empty;
                }

                var text = Regex.Replace(raw, "<[^>]+>", " ", RegexOptions.Singleline);
                text = WebUtility.HtmlDecode(text ?? string.Empty);
                text = Regex.Replace(text, @"\s+", " ").Trim();

                if (string.IsNullOrWhiteSpace(text))
                {
                    return item?.DateString ?? string.Empty;
                }

                const int max = 150;
                if (text.Length > max)
                {
                    text = text.Substring(0, max).TrimEnd() + "…";
                }

                return text;
            }
            catch
            {
                return item?.DateString ?? string.Empty;
            }
        }

        private static string GetLibraryNewsBadgeText(SteamRecentUpdateItem item)
        {
            return GetLibraryNewsBadgeTextFromParts(item?.Title ?? string.Empty, item?.Html ?? string.Empty);
        }

        private static string GetLibraryNewsBadgeText(SteamGameNewsItem item)
        {
            return GetLibraryNewsBadgeTextFromParts(item?.Title ?? string.Empty, item?.Html ?? string.Empty);
        }

        private static string GetLibraryNewsBadgeTextFromParts(string title, string html)
        {
            var safeTitle = title ?? string.Empty;
            var safeHtml = html ?? string.Empty;
            var titleText = safeTitle.ToLowerInvariant();
            var fullText = (safeTitle + " " + safeHtml).ToLowerInvariant();

            // Badges spécifiques basés sur le titre en priorité.
            // Exemple : une news dont le corps mentionne un DLC mais dont le titre parle d'un trailer
            // doit afficher TRAILER, pas DLC.
            if (IsLibraryNewsTrailerTitle(titleText))
            {
                return "TRAILER";
            }

            if (IsLibraryNewsGameplayTitle(titleText))
            {
                return "GAMEPLAY";
            }

            if (IsLibraryNewsCommunityTitle(titleText))
            {
                return "COMMUNITY";
            }

            if (IsLibraryNewsSaleText(fullText))
            {
                return "SALE";
            }

            // DLC is now title-driven only. The body of Steam news often mentions DLC/expansion
            // in unrelated announcements, which was causing false DLC badges like Discord/news posts.
            if (IsLibraryNewsDlcTitle(titleText))
            {
                return "DLC";
            }

            if (IsLibraryNewsAnnouncementTitle(titleText))
            {
                return "ANNOUNCE";
            }

            // Les updates sont détectées surtout depuis le titre pour éviter qu'une news/event
            // soit classée UPDATE juste parce que le body mentionne le mot "update".
            if (IsLibraryNewsUpdateTitle(titleText))
            {
                return "UPDATE";
            }

            if (fullText.Contains("event") || fullText.Contains("festival") || fullText.Contains("weekend") || fullText.Contains("anniversary") || fullText.Contains("showcase"))
            {
                return "EVENT";
            }

            return "NEWS";
        }

        private static string GetLibraryNewsBadgeTextFromText(string text)
        {
            return GetLibraryNewsBadgeTextFromParts(text, string.Empty);
        }

        private static bool IsLibraryNewsUpdateTitle(string text)
        {
            var t = (text ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(t))
            {
                return false;
            }

            return Regex.IsMatch(t, @"\b(patch|patch notes|hotfix|update|version|build)\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(t, @"\bver\.?\s*\d", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(t, @"\bv\d+(?:\.\d+)+", RegexOptions.IgnoreCase);
        }

        private static bool IsLibraryNewsTrailerTitle(string text)
        {
            var t = (text ?? string.Empty).ToLowerInvariant();
            return Regex.IsMatch(t, @"\b(trailer|teaser)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsLibraryNewsGameplayTitle(string text)
        {
            var t = (text ?? string.Empty).ToLowerInvariant();
            return Regex.IsMatch(t, @"\b(gameplay|hands[- ]?on|deep dive|combat first impressions?)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsLibraryNewsCommunityTitle(string text)
        {
            var t = (text ?? string.Empty).ToLowerInvariant();
            return Regex.IsMatch(t, @"\b(discord|community|forum|forums|dev diary|devlog|developer diary|community calendar)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsLibraryNewsAnnouncementTitle(string text)
        {
            var t = (text ?? string.Empty).ToLowerInvariant();
            return Regex.IsMatch(t, @"\b(announce|announced|announcing|announcement|revealed|reveal|wishlist now|coming soon|coming this|launching|launches|release date|releases|arrives|available on|available now)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsLibraryNewsDlcTitle(string text)
        {
            var t = text ?? string.Empty;

            // Title-only strong DLC signals. Do not inspect the body here: body text often mentions
            // DLC/expansions as context and caused false badges like "Join the official Mafia Discord".
            return Regex.IsMatch(t, @"\b(dlc|expansion|season pass|story pack|content pack|collab\.? pack|collaboration pack|music pass|music pack|song pack|pack vol\.?|add[- ]?on|new songs?|new dlc)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsLibraryNewsSaleText(string text)
        {
            var t = (text ?? string.Empty).ToLowerInvariant();
            return t.Contains("sale") ||
                   t.Contains("discount") ||
                   t.Contains("discounted") ||
                   t.Contains("% off") ||
                   t.Contains("save ") ||
                   t.Contains("saving") ||
                   t.Contains("deal") ||
                   t.Contains("bundle") ||
                   t.Contains("special offer");
        }


        private static bool IsHubLibraryGlobalUpdateFresh(SteamRecentUpdateItem item)
        {
            var dt = ParseSteamNewsDateUtc(item?.DateString);
            if (dt == DateTime.MinValue)
            {
                return true;
            }

            return (DateTime.UtcNow - dt).TotalDays <= 42.0;
        }

        private static bool IsLibraryNewsUpdateBadge(string badge)
        {
            return string.Equals(badge, "UPDATE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLibraryNewsPrimaryBadge(SteamRecentUpdateItem item)
        {
            var badge = GetLibraryNewsBadgeText(item);
            return string.Equals(badge, "DLC", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(badge, "EVENT", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLibraryNewsSecondaryBadge(SteamRecentUpdateItem item)
        {
            var badge = GetLibraryNewsBadgeText(item);
            return string.Equals(badge, "NEWS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(badge, "TRAILER", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(badge, "GAMEPLAY", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(badge, "ANNOUNCE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(badge, "COMMUNITY", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLibraryNewsSaleBadge(SteamRecentUpdateItem item)
        {
            return string.Equals(GetLibraryNewsBadgeText(item), "SALE", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeForKey(string value)
        {
            var s = (value ?? string.Empty).ToLowerInvariant();
            s = s.Replace("™", string.Empty)
                 .Replace("®", string.Empty)
                 .Replace("©", string.Empty)
                 .Replace("’", "'");
            s = Regex.Replace(s, @"\b(game of the year|goty|deluxe|ultimate|complete|definitive|edition|remastered|remaster)\b", " ");
            s = Regex.Replace(s, @"[^a-z0-9]+", " ");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static string GetLibraryNewsGameKey(SteamRecentUpdateItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.SteamAppId))
            {
                return "steam:" + item.SteamAppId.Trim();
            }

            return "name:" + NormalizeForKey(item.GameName ?? string.Empty);
        }

        private static bool IsValidSteamAppIdText(string value)
        {
            var v = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v) || v == "0")
            {
                return false;
            }

            return Regex.IsMatch(v, @"^\d{2,12}$");
        }

        private static bool IsHubLibraryNewsScanExcludedGameTitle(string name)
        {
            var n = (name ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(n))
            {
                return true;
            }

            return Regex.IsMatch(n, @"(^|[\s\-_:])(?:demo|beta|bêta|béta|playtest)(?:$|[\s\-_:])", RegexOptions.IgnoreCase);
        }

        private static bool IsHubLibraryNewsCandidateFresh(HubLibraryNewsCandidate candidate)
        {
            if (candidate == null || candidate.PublishedUtc == DateTime.MinValue)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            var publishedUtc = candidate.PublishedUtc.Kind == DateTimeKind.Utc
                ? candidate.PublishedUtc
                : candidate.PublishedUtc.ToUniversalTime();

            // Steam peut parfois renvoyer une date très légèrement future selon locale/timezone.
            if (publishedUtc > now.AddHours(12))
            {
                return false;
            }

            var age = now - publishedUtc;
            if (age.TotalDays < -1)
            {
                return false;
            }

            var text = ((candidate.News?.Title ?? string.Empty) + " " + (candidate.News?.Html ?? string.Empty));
            var isSale = string.Equals(candidate.Badge, "SALE", StringComparison.OrdinalIgnoreCase) || IsLibraryNewsSaleText(text);

            // Sales/promos : trop périssables, 2 semaines max.
            if (isSale)
            {
                return age.TotalDays <= 14.0;
            }

            // Toutes les autres news, y compris DLC/EVENT, 6 semaines max.
            return age.TotalDays <= 42.0;
        }

        private static int GetLibraryNewsBadgePriority(string badge)
        {
            switch ((badge ?? string.Empty).ToUpperInvariant())
            {
                case "DLC": return 5;
                case "EVENT": return 4;
                case "UPDATE": return 3;
                case "TRAILER": return 2;
                case "GAMEPLAY": return 2;
                case "ANNOUNCE": return 2;
                case "COMMUNITY": return 2;
                case "NEWS": return 2;
                case "SALE": return 1;
                default: return 0;
            }
        }

        private static DateTime ParseSteamNewsDateUtc(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
            {
                return DateTime.MinValue;
            }

            DateTime dt;
            var formats = new[]
            {
                "dd/MM/yyyy HH:mm",
                "d/M/yyyy HH:mm",
                "MM/dd/yyyy HH:mm",
                "M/d/yyyy HH:mm",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssZ"
            };

            if (DateTime.TryParseExact(dateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt) ||
                DateTime.TryParse(dateString, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt) ||
                DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
            {
                return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            }

            return DateTime.MinValue;
        }

        private Playnite.SDK.Models.Game FindGameBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId) || PlayniteApi?.Database?.Games == null)
            {
                return null;
            }

            try
            {
                var direct = PlayniteApi.Database.Games
                    .FirstOrDefault(g => string.Equals(GetSteamGameId(g), steamId, StringComparison.OrdinalIgnoreCase));

                if (direct != null)
                {
                    return direct;
                }

                Guid mappedGameId = Guid.Empty;
                lock (steamAppIdMappingCacheLock)
                {
                    var match = steamAppIdMappingCache
                        .FirstOrDefault(x => x.Value != null &&
                                             string.Equals(x.Value.SteamAppId, steamId, StringComparison.OrdinalIgnoreCase));
                    if (!match.Equals(default(KeyValuePair<Guid, SteamAppIdMappingEntry>)))
                    {
                        mappedGameId = match.Key;
                    }
                }

                if (mappedGameId != Guid.Empty)
                {
                    return PlayniteApi.Database.Games.FirstOrDefault(g => g.Id == mappedGameId);
                }
            }
            catch
            {
                // best effort only
            }

            return null;
        }

        private IEnumerable<SteamRecentUpdateItem> BuildLibraryNewsItemsFromGameNewsCache()
        {
            var result = new List<HubLibraryNewsCandidate>();

            try
            {
                Dictionary<string, SteamGameNewsCacheEntry> snapshot;
                lock (steamGameNewsCacheLock)
                {
                    snapshot = new Dictionary<string, SteamGameNewsCacheEntry>(steamGameNewsCache);
                }

                var skippedUpdatesFromGameCache = 0;

                foreach (var pair in snapshot)
                {
                    var steamId = pair.Key;
                    var entry = pair.Value;
                    if (entry?.Items == null || entry.Items.Count == 0)
                    {
                        continue;
                    }

                    var game = FindGameBySteamId(steamId);
                    if (game == null)
                    {
                        continue;
                    }

                    foreach (var news in entry.Items.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title)))
                    {
                        var dt = ParseSteamNewsDateUtc(news.DateString);
                        var badge = GetLibraryNewsBadgeText(news);

                        // Les updates de la carte From Your Library viennent uniquement de la liste globale
                        // Steam Recent Updates pour éviter les images génériques "UPDATE vX" des news par jeu.
                        if (IsLibraryNewsUpdateBadge(badge))
                        {
                            skippedUpdatesFromGameCache++;
                            HubNewsDebug($"[Hub Library News] cache skip update-source | appId={steamId} | game={Safe(game?.Name)} | title={ShortForLog(news.Title)} | reason=updates-use-global-list");
                            continue;
                        }

                        var localImage = !string.IsNullOrWhiteSpace(news.LocalImagePath) && File.Exists(news.LocalImagePath)
                            ? news.LocalImagePath
                            : string.Empty;

                        result.Add(new HubLibraryNewsCandidate
                        {
                            SteamId = steamId,
                            Game = game,
                            News = news,
                            PublishedUtc = dt == DateTime.MinValue ? entry.LastFetchedUtc : dt,
                            Badge = badge,
                            BadgePriority = GetLibraryNewsBadgePriority(badge),
                            ImagePath = localImage
                        });
                    }
                }

                var freshResult = result
                    .Where(IsHubLibraryNewsCandidateFresh)
                    .ToList();

                HubNewsDebug($"[Hub Library News] cache filter | total={result.Count} | keptFresh={freshResult.Count} | filteredOut={result.Count - freshResult.Count} | skippedUpdatesFromGameCache={skippedUpdatesFromGameCache} | maxAgeDays=42 | saleMaxAgeDays=14");

                foreach (var candidate in freshResult
                    .OrderByDescending(x => x.BadgePriority)
                    .ThenByDescending(x => x.PublishedUtc)
                    .Take(10))
                {
                    HubNewsDebug($"[Hub Library News] cache keep | appId={candidate.SteamId} | game={Safe(candidate.Game?.Name)} | badge={candidate.Badge} | ageDays={GetAgeDaysForLog(candidate.PublishedUtc):0.0} | publishedUtc={FormatUtcForLog(candidate.PublishedUtc)} | title={ShortForLog(candidate.News?.Title)}");
                }

                foreach (var candidate in result
                    .Where(x => !IsHubLibraryNewsCandidateFresh(x))
                    .OrderByDescending(x => x.PublishedUtc)
                    .Take(10))
                {
                    HubNewsDebug($"[Hub Library News] cache drop | appId={candidate.SteamId} | game={Safe(candidate.Game?.Name)} | badge={candidate.Badge} | ageDays={GetAgeDaysForLog(candidate.PublishedUtc):0.0} | publishedUtc={FormatUtcForLog(candidate.PublishedUtc)} | title={ShortForLog(candidate.News?.Title)}");
                }

                if (result.Count > 0 && freshResult.Count == 0)
                {
                    logger.Info("[Hub Library News] game-news-cache filtered out all candidates because they are too old; fallback updates will be used if available.");
                }

                // 1 seule news max par jeu, puis tri avec un petit bonus pour DLC/EVENT/NEWS.
                return freshResult
                    .GroupBy(x => x.SteamId ?? string.Empty)
                    .Select(g => g
                        .OrderByDescending(x => x.BadgePriority)
                        .ThenByDescending(x => x.PublishedUtc)
                        .FirstOrDefault())
                    .Where(x => x != null)
                    .OrderByDescending(x => x.BadgePriority)
                    .ThenByDescending(x => x.PublishedUtc)
                    .Take(30)
                    .Select(x =>
                    {
                        var game = x.Game;
                        var imagePath = !string.IsNullOrWhiteSpace(x.ImagePath)
                            ? x.ImagePath
                            : GetBestHubCardBackgroundPath(game);

                        return new SteamRecentUpdateItem
                        {
                            SteamAppId = x.SteamId ?? string.Empty,
                            GameName = Safe(game?.Name),
                            Title = x.News.Title ?? string.Empty,
                            DateString = x.PublishedUtc == DateTime.MinValue
                                ? (x.News.DateString ?? string.Empty)
                                : x.PublishedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                            CoverPath = GetGameCoverPath(game) ?? string.Empty,
                            BackgroundPath = imagePath ?? string.Empty,
                            IconPath = GetGameIconPath(game) ?? string.Empty,
                            IsRecent = x.PublishedUtc != DateTime.MinValue && (DateTime.UtcNow - x.PublishedUtc).TotalDays <= 14.0,
                            Html = x.News.Html ?? string.Empty
                        };
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Hub Library News] BuildLibraryNewsItemsFromGameNewsCache failed.");
                return new List<SteamRecentUpdateItem>();
            }
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

            // Cache-first startup: only load the cache into memory here.
            // The final mixed rotation is rebuilt once by LoadNewsFromCacheIfNeeded() after A + B are loaded.
            // Avoid SaveSettingsSafe() during startup cache reads to keep the Hub opening smooth.
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

        private class HubLibraryNewsCandidate
        {
            public string SteamId { get; set; }
            public Playnite.SDK.Models.Game Game { get; set; }
            public SteamGameNewsItem News { get; set; }
            public DateTime PublishedUtc { get; set; }
            public string Badge { get; set; }
            public int BadgePriority { get; set; }
            public string ImagePath { get; set; }
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
                        var backgroundPath = GetGameBackgroundOrCoverPath(game);
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
                            SteamAppId = steamId ?? string.Empty,
                            GameName = Safe(e.GameName ?? game?.Name),
                            Title = e.Title ?? string.Empty,
                            DateString = dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                            CoverPath = coverPath ?? string.Empty,
                            BackgroundPath = backgroundPath ?? string.Empty,
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

                UpdateLibraryNewsRotationFromUpdates(list);

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to refresh Steam recent updates.");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Settings.SteamRecentUpdates.Clear();
                });
                UpdateLibraryNewsRotationFromUpdates(null);
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

        private string GetGameBackgroundOrCoverPath(Playnite.SDK.Models.Game game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            string path = null;

            if (!string.IsNullOrEmpty(game.BackgroundImage))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.BackgroundImage);
            }
            else if (!string.IsNullOrEmpty(game.CoverImage))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.CoverImage);
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
                    return Loc("LOCNotificationTitlePlayniteNews", "Playnite news");

                case "steamUpdate":
                    return Loc("LOCNotificationTitleSteamUpdate", "Game update");

                case "gameEnded":
                    return Loc("LOCNotificationTitleGameEnded", "Game session ended");

                case "steamAuth":
                    return Loc("LOCNotificationTitleSteamAuth", "Steam account");

                case "wishlistDeal":
                    return Loc("LOCNotificationTitleWishlistDeal", "Wishlist deal");

                case "wishlistReleased":
                    return Loc("LOCNotificationTitleWishlistReleased", "Wishlist release");

                default:
                    return Loc("LOCNotificationTitleDefault", "Notification");
            }
        }

        private void AddLastNotificationOnUi(string title, string message, string type = null, string imagePath = null)
        {
            // Do not store game session ended notifications in the notification history.
            // The session popup still works because it uses SessionNotificationArmed,
            // not LastNotifications.
            if (string.Equals(type, "gameEnded", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

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

        private static string LocFormat(string key, string fallback, params object[] args)
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, Loc(key, fallback), args ?? new object[0]);
            }
            catch
            {
                try
                {
                    return string.Format(CultureInfo.CurrentCulture, fallback ?? string.Empty, args ?? new object[0]);
                }
                catch
                {
                    return fallback ?? string.Empty;
                }
            }
        }

        private void ShowSteamSessionExpiredToastThrottled()
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - lastSteamAuthToastUtc) < TimeSpan.FromHours(12))
                {
                    return;
                }

                lastSteamAuthToastUtc = now;
                ShowGlobalToast(
                    Loc("LOCSteamSessionExpiredToast", "Steam session expired. Please reconnect to Steam in Aniki Helper settings."),
                    "steamAuth"
                );
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] Steam auth toast failed.");
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

        private static string ShortForLog(string value, int max = 90)
        {
            value = value ?? string.Empty;
            value = Regex.Replace(value, @"\s+", " ").Trim();

            if (max > 0 && value.Length > max)
            {
                return value.Substring(0, max).TrimEnd() + "…";
            }

            return value;
        }

        private static string FormatUtcForLog(DateTime utc)
        {
            if (utc == DateTime.MinValue)
            {
                return "unknown";
            }

            var normalized = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            return normalized.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z";
        }

        private static string FormatNullableDateForLog(DateTime? value)
        {
            if (value == null || value.Value == DateTime.MinValue)
            {
                return "none";
            }

            return value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static double GetAgeDaysForLog(DateTime utc)
        {
            if (utc == DateTime.MinValue)
            {
                return -1.0;
            }

            var normalized = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            return (DateTime.UtcNow - normalized).TotalDays;
        }

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
        private readonly DispatcherTimer libraryNewsRotationTimer;
        private readonly DispatcherTimer navigationSettleTimer;
        private readonly SemaphoreSlim newsRefreshGate = new SemaphoreSlim(1, 1);


        private readonly List<SteamGlobalNewsItem> latestNewsRotation = new List<SteamGlobalNewsItem>();
        private int latestNewsRotationIndex = 0;
        private bool latestNewsCrossfadeInitialized = false;

        private readonly List<SteamRecentUpdateItem> libraryNewsRotation = new List<SteamRecentUpdateItem>();
        private int libraryNewsRotationIndex = 0;
        private bool libraryNewsCrossfadeInitialized = false;
        private DateTime lastHubLibraryNewsTargetedRefreshUtc = DateTime.MinValue;

        // Hub News startup policy: show cached content immediately, then refresh lightly in background.
        private const int HubLibraryNewsRotationStartupDelayMs = 10000;
        private const int HubLibraryNewsBackgroundRefreshStartupDelayMs = 60000;
        private const int HubLibraryNewsBackgroundRefreshMaxGames = 5;
        private const int HubLibraryNewsBackgroundRefreshTimeoutSeconds = 45;
        private const int HubLibraryNewsBackgroundRefreshItemDelayMs = 250;


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
        private SteamAccountSessionService steamAccountSessionService;
        private SteamUpcomingGamesService steamUpcomingGamesService;
        private SteamUserGamesService steamUserGamesService;
        private SteamStorePersonalizationService steamStorePersonalizationService;
        private SteamStoreRecommendationService steamStoreRecommendationService;
        private string lastLoggedSteamRecommendationProfileKey = string.Empty;
        private AnikiSteamFriendsService steamFriendsService;

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
                // 1) Jeu provenant directement du plugin Steam.
                if (game.PluginId == SteamPluginId && IsValidSteamAppIdText(game.GameId))
                {
                    return game.GameId.Trim();
                }

                // 1b) Certains jeux importés/custom gardent un GameId numérique avec une source Steam.
                var sourceName = game.Source?.Name ?? string.Empty;
                if (sourceName.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0 && IsValidSteamAppIdText(game.GameId))
                {
                    return game.GameId.Trim();
                }

                // 2) Sinon, trouver un lien Steam dans Game.Links.
                if (game.Links != null)
                {
                    foreach (var link in game.Links)
                    {
                        var url = link?.Url;
                        if (string.IsNullOrWhiteSpace(url))
                            continue;

                        var patterns = new[]
                        {
                            @"store\.steampowered\.com/(?:agecheck/)?app/(\d+)",
                            @"steamcommunity\.com/app/(\d+)",
                            @"steam://rungameid/(\d+)"
                        };

                        foreach (var pattern in patterns)
                        {
                            var m = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
                            if (m.Success && IsValidSteamAppIdText(m.Groups[1].Value))
                            {
                                return m.Groups[1].Value.Trim();
                            }
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

        private void ResetSteamFriendsDetailsForCurrentGame()
        {
            try
            {
                steamFriendsService?.ResetCurrentGameFriendDetails();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to reset Steam friends details for current game.");
            }
        }

        private void UpdateSteamFriendsDetailsForGame(Playnite.SDK.Models.Game game)
        {
            try
            {
                var steamId = GetSteamGameId(game);
                if (string.IsNullOrWhiteSpace(steamId) || !int.TryParse(steamId, out var appId) || appId <= 0)
                {
                    ResetSteamFriendsDetailsForCurrentGame();
                    return;
                }

                steamFriendsService?.UpdateCurrentGameFriendDetails(appId);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to update Steam friends details for current game.");
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
                ResetSteamFriendsDetailsForCurrentGame();
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

                // Refresh the Hub news card too: it now includes the latest Playnite/add-on update
                // as one item in the Source A / Source B / Playnite update rotation.
                UpdateLatestNewsRotationFromList(Settings?.SteamGlobalNewsA?.ToList());

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

        private async Task<List<Playnite.SDK.Models.Game>> GetHubLibraryNewsScanCandidatesAsync(int maxGames = 15, CancellationToken ct = default(CancellationToken))
        {
            var result = new List<Playnite.SDK.Models.Game>();
            var seen = new HashSet<Guid>();
            var seenSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Func<IEnumerable<Playnite.SDK.Models.Game>, string, int, Task> addRangeAsync = async (games, reason, maxForReason) =>
            {
                if (games == null)
                {
                    return;
                }

                var addedForReason = 0;
                var checkedForReason = 0;

                foreach (var g in games)
                {
                    ct.ThrowIfCancellationRequested();

                    if (g == null)
                    {
                        continue;
                    }

                    checkedForReason++;
                    if (checkedForReason > 40)
                    {
                        HubNewsDebug($"[Hub Library News] scan candidate stop category | reason={reason} | cause=lookup-limit | checked={checkedForReason - 1} | added={addedForReason}");
                        break;
                    }

                    if (result.Count >= maxGames)
                    {
                        HubNewsDebug($"[Hub Library News] scan candidate stop category | reason={reason} | cause=max-reached | max={maxGames}");
                        break;
                    }

                    if (addedForReason >= maxForReason)
                    {
                        HubNewsDebug($"[Hub Library News] scan candidate stop category | reason={reason} | cause=category-quota | quota={maxForReason}");
                        break;
                    }

                    if (IsHubLibraryNewsScanExcludedGameTitle(g.Name))
                    {
                        HubNewsDebug($"[Hub Library News] scan candidate skip | reason={reason} | name={Safe(g.Name)} | cause=demo-or-beta-title");
                        continue;
                    }

                    if (seen.Contains(g.Id))
                    {
                        HubNewsDebug($"[Hub Library News] scan candidate skip | reason={reason} | name={Safe(g.Name)} | cause=duplicate");
                        continue;
                    }

                    var directSteamId = GetSteamGameId(g);
                    var mappedSteamId = string.Empty;
                    var steamIdSource = string.Empty;
                    var steamIdForLog = string.Empty;

                    if (!string.IsNullOrWhiteSpace(directSteamId))
                    {
                        steamIdForLog = directSteamId;
                        steamIdSource = "game-data";
                    }
                    else
                    {
                        lock (steamAppIdMappingCacheLock)
                        {
                            if (steamAppIdMappingCache.TryGetValue(g.Id, out var mapped) &&
                                mapped != null &&
                                !string.IsNullOrWhiteSpace(mapped.SteamAppId))
                            {
                                mappedSteamId = mapped.SteamAppId;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(mappedSteamId))
                        {
                            steamIdForLog = mappedSteamId;
                            steamIdSource = "mapping-cache";
                        }
                        else
                        {
                            // Recherche online ciblée uniquement pendant le scan Hub, pour les jeux récents
                            // qui n'ont pas encore de lien Steam/AppId dans Playnite.
                            var resolved = await ResolveSteamGameIdAsync(g, ct, true).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(resolved))
                            {
                                steamIdForLog = resolved;
                                steamIdSource = "online-lookup";
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(steamIdForLog) || !IsValidSteamAppIdText(steamIdForLog))
                    {
                        HubNewsDebug($"[Hub Library News] scan candidate skip | reason={reason} | name={Safe(g.Name)} | cause=no-steam-appid | lastActivity={FormatNullableDateForLog(g.LastActivity)} | added={FormatNullableDateForLog(g.Added)} | favorite={g.Favorite} | installed={g.IsInstalled}");
                        continue;
                    }

                    if (!seenSteamIds.Add(steamIdForLog))
                    {
                        HubNewsDebug($"[Hub Library News] scan candidate skip | reason={reason} | name={Safe(g.Name)} | appId={steamIdForLog} | cause=duplicate-steam-appid");
                        continue;
                    }

                    seen.Add(g.Id);
                    result.Add(g);
                    addedForReason++;

                    HubNewsDebug($"[Hub Library News] scan candidate add | index={result.Count}/{maxGames} | reason={reason} | name={Safe(g.Name)} | steamId={steamIdForLog} | steamIdSource={steamIdSource} | lastActivity={FormatNullableDateForLog(g.LastActivity)} | added={FormatNullableDateForLog(g.Added)} | favorite={g.Favorite} | installed={g.IsInstalled}");
                }
            };

            try
            {
                var games = PlayniteApi?.Database?.Games?.ToList() ?? new List<Playnite.SDK.Models.Game>();

                HubNewsDebug($"[Hub Library News] scan candidates START | libraryGames={games.Count} | max={maxGames} | exclude=demo,beta | take-next-valid=True | onlineLookup=True");

                await addRangeAsync(games
                    .Where(g => g.LastActivity != null)
                    .OrderByDescending(g => g.LastActivity), "recently-played", 5).ConfigureAwait(false);

                await addRangeAsync(games
                    .Where(g => g.Added != null)
                    .OrderByDescending(g => g.Added), "recently-added", 5).ConfigureAwait(false);

                await addRangeAsync(games
                    .Where(g => g.Favorite == true || g.IsInstalled == true)
                    .OrderByDescending(g => g.LastActivity ?? g.Added ?? DateTime.MinValue), "favorite-or-installed", 5).ConfigureAwait(false);

                HubNewsDebug($"[Hub Library News] scan candidates END | selected={result.Count} | names={ShortForLog(string.Join(", ", result.Select(x => Safe(x.Name))), 240)}");
            }
            catch (OperationCanceledException)
            {
                HubNewsDebug($"[Hub Library News] GetHubLibraryNewsScanCandidates cancelled | selected={result.Count}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Hub Library News] GetHubLibraryNewsScanCandidates failed.");
            }

            return result;
        }


        private bool IsSteamGameNewsCacheFreshForHub(SteamGameNewsCacheEntry entry)
        {
            if (entry?.Items == null || entry.Items.Count == 0 || entry.LastFetchedUtc == DateTime.MinValue)
            {
                return false;
            }

            return DateTime.UtcNow - entry.LastFetchedUtc < SteamGameNewsCacheDuration;
        }

        private async Task RefreshSteamGameNewsCacheForHubCardAsync(Playnite.SDK.Models.Game game, CancellationToken ct)
        {
            if (game == null)
            {
                HubNewsDebug("[Hub Library News] scan app skipped | reason=null-game");
                return;
            }

            var gameName = Safe(game.Name);
            if (IsHubLibraryNewsScanExcludedGameTitle(gameName))
            {
                HubNewsDebug($"[Hub Library News] scan app skipped | game={gameName} | reason=demo-or-beta-title");
                return;
            }

            var steamId = await ResolveSteamGameIdAsync(game, ct, true).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(steamId) || !IsValidSteamAppIdText(steamId))
            {
                HubNewsDebug($"[Hub Library News] scan app skipped | game={gameName} | reason=no-steam-appid");
                return;
            }

            SteamGameNewsCacheEntry cachedEntry = null;
            lock (steamGameNewsCacheLock)
            {
                steamGameNewsCache.TryGetValue(steamId, out cachedEntry);
            }

            var cachedItems = cachedEntry?.Items?.Count ?? 0;
            var cacheFresh = IsSteamGameNewsCacheFreshForHub(cachedEntry);
            var cacheAgeMinutes = cachedEntry != null && cachedEntry.LastFetchedUtc != DateTime.MinValue
                ? (DateTime.UtcNow - cachedEntry.LastFetchedUtc).TotalMinutes
                : -1.0;

            HubNewsDebug($"[Hub Library News] scan app | appId={steamId} | name={gameName} | cachedItems={cachedItems} | lastFetchedUtc={FormatUtcForLog(cachedEntry?.LastFetchedUtc ?? DateTime.MinValue)} | cacheAgeMinutes={cacheAgeMinutes:0.0} | cacheFresh={cacheFresh}");

            if (cacheFresh)
            {
                HubNewsDebug($"[Hub Library News] scan app skipped | appId={steamId} | name={gameName} | reason=cache-fresh | cachedItems={cachedItems}");
                return;
            }

            await steamGameNewsGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                HubNewsDebug($"[Hub Library News] fetch START | appId={steamId} | name={gameName} | count=8");

                var news = await steamUpdateService.GetLatestNewsAsync(steamId, 8, ct).ConfigureAwait(false);
                if (news == null || news.Count == 0)
                {
                    HubNewsDebug($"[Hub Library News] fetch EMPTY | appId={steamId} | name={gameName}");
                    return;
                }

                foreach (var item in news)
                {
                    ct.ThrowIfCancellationRequested();
                    item.LocalImagePath = await Task.Run(() => DownloadSteamGameNewsImage(item.ImageUrl), ct).ConfigureAwait(false);
                    item.Html = CleanHtml(item.Html ?? string.Empty);
                }

                var latest = news
                    .Select(x => new { Item = x, PublishedUtc = ParseSteamNewsDateUtc(x?.DateString) })
                    .OrderByDescending(x => x.PublishedUtc)
                    .FirstOrDefault();

                HubNewsDebug($"[Hub Library News] fetch RESULT | appId={steamId} | name={gameName} | items={news.Count} | latestUtc={FormatUtcForLog(latest?.PublishedUtc ?? DateTime.MinValue)} | latestTitle={ShortForLog(latest?.Item?.Title)}");

                lock (steamGameNewsCacheLock)
                {
                    steamGameNewsCache[steamId] = new SteamGameNewsCacheEntry
                    {
                        LastFetchedUtc = DateTime.UtcNow,
                        Items = news
                    };

                    steamGameNewsCacheDirty = true;
                }

                HubNewsDebug($"[Hub Library News] cache SAVE | appId={steamId} | name={gameName} | items={news.Count} | dirty=True");
            }
            finally
            {
                steamGameNewsGate.Release();
            }
        }


        private async Task RefreshHubLibraryNewsTargetedAsync()
        {
            var sw = Stopwatch.StartNew();

            try
            {
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen || !IsAnikiThemeActive())
                {
                    HubNewsDebug("[Hub Library News] targeted refresh skipped | reason=not-fullscreen-or-not-aniki-theme");
                    return;
                }

                var elapsedSinceLast = DateTime.UtcNow - lastHubLibraryNewsTargetedRefreshUtc;
                if (elapsedSinceLast < TimeSpan.FromHours(6))
                {
                    var remaining = TimeSpan.FromHours(6) - elapsedSinceLast;
                    HubNewsDebug($"[Hub Library News] targeted refresh skipped | reason=cooldown | elapsedMinutes={elapsedSinceLast.TotalMinutes:0.0} | remainingMinutes={remaining.TotalMinutes:0.0}");
                    return;
                }

                lastHubLibraryNewsTargetedRefreshUtc = DateTime.UtcNow;

                HubNewsDebug($"[Hub Library News] targeted refresh START | cacheFirst=True | startupDelayMs={HubLibraryNewsBackgroundRefreshStartupDelayMs} | cooldownHours=6 | maxGames={HubLibraryNewsBackgroundRefreshMaxGames}");

                if (!IsSteamRecentScanAllowed())
                {
                    HubNewsDebug("[Hub Library News] targeted refresh skipped | reason=steam-scan-not-allowed-after-delay");
                    return;
                }

                var scanned = 0;
                var candidates = new List<Playnite.SDK.Models.Game>();
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(HubLibraryNewsBackgroundRefreshTimeoutSeconds)))
                {
                    candidates = await GetHubLibraryNewsScanCandidatesAsync(HubLibraryNewsBackgroundRefreshMaxGames, cts.Token).ConfigureAwait(false);
                    if (candidates.Count == 0)
                    {
                        HubNewsDebug("[Hub Library News] targeted refresh skipped | reason=no-candidates");
                        return;
                    }

                    HubNewsDebug($"[Hub Library News] targeted scan loop START | candidates={candidates.Count} | names={ShortForLog(string.Join(", ", candidates.Select(x => Safe(x.Name))), 240)}");

                    foreach (var game in candidates)
                    {
                        if (scanned >= HubLibraryNewsBackgroundRefreshMaxGames)
                        {
                            HubNewsDebug($"[Hub Library News] targeted scan loop STOP | reason=max-scanned | scanned={scanned}");
                            break;
                        }

                        if (!IsSteamRecentScanAllowed())
                        {
                            HubNewsDebug($"[Hub Library News] targeted scan loop STOP | reason=steam-scan-not-allowed | scanned={scanned}");
                            break;
                        }

                        try
                        {
                            HubNewsDebug($"[Hub Library News] targeted scan item START | index={scanned + 1}/{candidates.Count} | name={Safe(game?.Name)}");
                            await RefreshSteamGameNewsCacheForHubCardAsync(game, cts.Token).ConfigureAwait(false);
                            scanned++;
                            HubNewsDebug($"[Hub Library News] targeted scan item END | index={scanned}/{candidates.Count} | name={Safe(game?.Name)}");
                        }
                        catch (OperationCanceledException)
                        {
                            HubNewsDebug($"[Hub Library News] targeted scan loop CANCELLED | scanned={scanned}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            HubNewsDebug(ex, $"[Hub Library News] targeted scan failed for {game?.Name}");
                        }

                        await Task.Delay(HubLibraryNewsBackgroundRefreshItemDelayMs, cts.Token).ConfigureAwait(false);
                    }
                }

                FlushSteamGameNewsCacheIfNeeded();
                UpdateLibraryNewsRotationFromUpdates(Settings?.SteamRecentUpdates?.ToList());

                logger.Info($"[Hub Library News] background refresh completed | scanned={scanned}/{candidates.Count} | elapsedMs={sw.ElapsedMilliseconds}");
            }
            catch (OperationCanceledException)
            {
                HubNewsDebug($"[Hub Library News] targeted refresh cancelled | elapsedMs={sw.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Hub Library News] targeted refresh failed.");
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
        "Occasionnel",
        "Jeu solo",
        "Singleplayer",
        "Single-player",
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

            if (text.Contains("soulslike") ||
                text.Contains("souls-like") ||
                text.Contains("souls like") ||
                text.Contains("dark souls") ||
                text.Contains("demon's souls") ||
                text.Contains("demons souls") ||
                text.Contains("elden ring"))
                return "souls";

            if (text.Contains("jrpg") || text.Contains("j-rpg") ||
                text.Contains("tour par tour") || text.Contains("turn-based"))
                return "jrpg";

            if (text.Contains("action et aventure") ||
                text.Contains("action adventure") ||
                text.Contains("action aventure") ||
                text.Contains("third person") ||
                text.Contains("3e personne") ||
                text.Contains("3ᵉ personne") ||
                text.Contains("infiltration") ||
                text.Contains("stealth") ||
                text.Contains("assassinat") ||
                text.Contains("hack and slash"))
                return "action_adventure";

            if (text.Contains("story rich") ||
                text.Contains("scenario riche") ||
                text.Contains("scénario riche") ||
                text.Contains("choices matter") ||
                text.Contains("choix multiples") ||
                text.Contains("point and click") ||
                text.Contains("pointer et cliquer") ||
                text.Contains("interactive fiction") ||
                text.Contains("aventure interactive") ||
                text.Contains("visual novel") ||
                text.Contains("roman graphique"))
                return "narrative";

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



        private void RecalcHubLibraryRecommendedGames()
        {
            var s = Settings;

            // Reset the visible 4-card Hub recommendation list only.
            ReplaceHubLibraryRecommendedGames(null);

            var games = PlayniteApi.Database.Games.ToList();
            if (!s.IncludeHidden)
            {
                games = games.Where(g => g.Hidden != true).ToList();
            }

            if (games.Count == 0)
            {
                return;
            }

            // 1) RefGame (Top 3 sur 14 jours + sticky journée)
            List<Playnite.SDK.Models.Game> refTop3;
            Playnite.SDK.Models.Game refGame = GetOrSelectRefGameForToday(games, out refTop3);

            if (refGame == null)
            {
                return;
            }

            var refName = Safe(refGame.Name);

            // 2) Build candidates used by the 4-card Hub recommendation section.
            var candidates = BuildHubRecommendationCandidatesForReference(games, refGame, null);

            if (candidates.Count == 0)
            {
                return;
            }

            var ordered = candidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Game.Name ?? string.Empty)
                .ToList();

            var topCandidates = ordered.Take(4).ToList();
            if (topCandidates.Count == 0)
            {
                return;
            }

            var hubRecommended = BuildDiverseHubLibraryRecommendations(games, refTop3, refGame, topCandidates);
            ReplaceHubLibraryRecommendedGames(hubRecommended);
            DebugLog($"[Hub Library Recommended] diversified refresh | count={hubRecommended.Count} | refs={BuildHubReferenceLog(refTop3, refGame)} | stickyRef={refName}");

            SaveSettingsSafe();
        }

        private List<HubRecommendationCandidate> BuildHubRecommendationCandidatesForReference(
            IList<Playnite.SDK.Models.Game> games,
            Playnite.SDK.Models.Game refGame,
            ISet<Guid> excludedGameIds)
        {
            var candidates = new List<HubRecommendationCandidate>();

            if (games == null || refGame == null)
            {
                return candidates;
            }

            Func<ulong, ulong> ToMinutes = raw => raw / 60UL;

            var refGenres = GetGenreNames(refGame).ToList();
            var refTags = GetTagNames(refGame)
                .Where(IsUsefulProfileTag)
                .ToList();
            var refDevs = GetDeveloperNames(refGame).ToList();
            var refPubs = GetPublisherNames(refGame).ToList();
            var refFam = DetectFamily(refGenres, refTags);

            foreach (var g in games)
            {
                if (g == null)
                    continue;

                if (g.Id == refGame.Id)
                    continue;

                if (excludedGameIds != null && excludedGameIds.Contains(g.Id))
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

                candidates.Add(new HubRecommendationCandidate
                {
                    Game = g,
                    Score = score,
                    Reason = string.IsNullOrEmpty(reason) ? string.Empty : reason
                });
            }

            return candidates;
        }

        private List<HubLibraryRecommendedGameItem> BuildDiverseHubLibraryRecommendations(
            IList<Playnite.SDK.Models.Game> games,
            IList<Playnite.SDK.Models.Game> refTop3,
            Playnite.SDK.Models.Game refGame,
            IList<HubRecommendationCandidate> fallbackTopCandidates)
        {
            var result = new List<HubLibraryRecommendedGameItem>();
            var usedRecommendedIds = new HashSet<Guid>();
            var referenceGames = new List<Playnite.SDK.Models.Game>();

            Action<Playnite.SDK.Models.Game> addReference = game =>
            {
                if (game == null || game.Id == Guid.Empty)
                    return;

                if (referenceGames.Any(x => x != null && x.Id == game.Id))
                    return;

                referenceGames.Add(game);
            };

            if (refTop3 != null)
            {
                foreach (var game in refTop3.Take(3))
                {
                    addReference(game);
                }
            }

            addReference(refGame);

            var referenceIds = new HashSet<Guid>(referenceGames.Select(x => x.Id));

            // First pass: one card per top reference game.
            foreach (var reference in referenceGames.Take(3))
            {
                var excluded = new HashSet<Guid>(referenceIds);
                foreach (var id in usedRecommendedIds)
                {
                    excluded.Add(id);
                }

                var best = BuildHubRecommendationCandidatesForReference(games, reference, excluded)
                    .OrderByDescending(c => c.Score)
                    .ThenBy(c => c.Game.Name ?? string.Empty)
                    .FirstOrDefault();

                if (best == null)
                {
                    continue;
                }

                var item = BuildHubLibraryRecommendedGameItem(best, Safe(reference.Name));
                if (item != null && item.GameId != Guid.Empty)
                {
                    result.Add(item);
                    usedRecommendedIds.Add(item.GameId);
                }
            }

            // Second pass: fill the 4th card with the best remaining match across all references.
            if (result.Count < 4 && referenceGames.Count > 0)
            {
                var combined = new List<Tuple<HubRecommendationCandidate, string>>();

                foreach (var reference in referenceGames.Take(3))
                {
                    var excluded = new HashSet<Guid>(referenceIds);
                    foreach (var id in usedRecommendedIds)
                    {
                        excluded.Add(id);
                    }

                    foreach (var candidate in BuildHubRecommendationCandidatesForReference(games, reference, excluded))
                    {
                        if (candidate?.Game == null)
                            continue;

                        combined.Add(Tuple.Create(candidate, Safe(reference.Name)));
                    }
                }

                foreach (var pair in combined
                    .OrderByDescending(x => x.Item1.Score)
                    .ThenBy(x => x.Item1.Game.Name ?? string.Empty))
                {
                    if (result.Count >= 4)
                        break;

                    if (pair.Item1?.Game == null || usedRecommendedIds.Contains(pair.Item1.Game.Id))
                        continue;

                    var item = BuildHubLibraryRecommendedGameItem(pair.Item1, pair.Item2);
                    if (item != null && item.GameId != Guid.Empty)
                    {
                        result.Add(item);
                        usedRecommendedIds.Add(item.GameId);
                    }
                }
            }

            // Last fallback: old sticky-ref list, only if not enough cards exist.
            if (result.Count < 4 && fallbackTopCandidates != null)
            {
                var fallbackRefName = Safe(refGame?.Name);

                foreach (var candidate in fallbackTopCandidates)
                {
                    if (result.Count >= 4)
                        break;

                    if (candidate?.Game == null || usedRecommendedIds.Contains(candidate.Game.Id) || referenceIds.Contains(candidate.Game.Id))
                        continue;

                    var item = BuildHubLibraryRecommendedGameItem(candidate, fallbackRefName);
                    if (item != null && item.GameId != Guid.Empty)
                    {
                        result.Add(item);
                        usedRecommendedIds.Add(item.GameId);
                    }
                }
            }

            return result.Take(4).ToList();
        }

        private void ReplaceHubLibraryRecommendedGames(IList<HubLibraryRecommendedGameItem> items)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                Settings.HubLibraryRecommendedGames.Clear();

                if (items == null)
                {
                    return;
                }

                foreach (var item in items.Take(4))
                {
                    if (item != null && item.GameId != Guid.Empty)
                    {
                        Settings.HubLibraryRecommendedGames.Add(item);
                    }
                }
            });
        }

        private HubLibraryRecommendedGameItem BuildHubLibraryRecommendedGameItem(HubRecommendationCandidate candidate, string refName)
        {
            var snapshot = BuildHubRecommendationSnapshot(candidate, refName);
            if (snapshot == null || snapshot.GameId == Guid.Empty)
            {
                return null;
            }

            return new HubLibraryRecommendedGameItem
            {
                GameId = snapshot.GameId,
                Name = snapshot.Name ?? string.Empty,
                CoverPath = snapshot.CoverPath ?? string.Empty,
                BackgroundPath = snapshot.BackgroundPath ?? string.Empty,
                SourceName = snapshot.SourceName ?? string.Empty,
                ReasonKey = snapshot.ReasonKey ?? string.Empty,
                BannerText = snapshot.BannerText ?? string.Empty
            };
        }

        private HubRecommendationSnapshot BuildHubRecommendationSnapshot(HubRecommendationCandidate candidate, string refName)
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

            return new HubRecommendationSnapshot
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

        private string BuildHubReferenceLog(IList<Playnite.SDK.Models.Game> refTop3, Playnite.SDK.Models.Game refGame)
        {
            try
            {
                var refs = new List<string>();

                if (refTop3 != null)
                {
                    refs.AddRange(refTop3
                        .Where(g => g != null)
                        .Take(3)
                        .Select(g => Safe(g.Name)));
                }

                if (refGame != null && !refs.Any(x => string.Equals(x, Safe(refGame.Name), StringComparison.OrdinalIgnoreCase)))
                {
                    refs.Add(Safe(refGame.Name));
                }

                return string.Join(", ", refs.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
            catch
            {
                return string.Empty;
            }
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

        private class HubRecommendationSnapshot
        {
            public Guid GameId { get; set; } = Guid.Empty;
            public string Name { get; set; }
            public string CoverPath { get; set; }
            public string BackgroundPath { get; set; }
            public string SourceName { get; set; }
            public string ReasonKey { get; set; }
            public string BannerText { get; set; }
        }

        private class HubRecommendationCandidate
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
            inGameOverlayService = new InGameOverlayService(
                api,
                Settings,
                () => anikiWindowManager?.HasOpenWindow == true);
            anikiWindowManager.SetOverlayOpenStateProvider(
                () => inGameOverlayService?.IsOverlayOpenOrOpening == true);
            konamiCodeService = new KonamiCodeService(
                Settings,
                logger,
                DebugLog,
                () =>
                {
                    eventSoundService?.PlayKonamiCodeAccepted();
                    anikiThemeSettingsService?.LoadKonamiModeResourceOverride(true);
                });

            anikiThemeSettingsService = new AnikiThemeSettingsService(
                api,
                Settings,
                logger,
                GetPluginUserDataPath());

            steamStoreService = new SteamStoreService(api, GetPluginUserDataPath());
            steamAccountSessionService = new SteamAccountSessionService(api, logger);
            steamUpcomingGamesService = new SteamUpcomingGamesService(logger, GetPluginUserDataPath(), steamStoreService);
            steamUserGamesService = new SteamUserGamesService(logger, GetPluginUserDataPath());
            steamStorePersonalizationService = new SteamStorePersonalizationService();
            steamStoreRecommendationService = new SteamStoreRecommendationService(logger, GetPluginUserDataPath(), steamStoreService, steamStorePersonalizationService);
            steamFriendsService = new AnikiSteamFriendsService(
                api,
                Settings,
                GetPluginUserDataPath(),
                logger,
                DebugLog,
                IsAnikiThemeActive,
                SaveSettingsSafe);
            splashScreenService = new SplashScreenService(GetPluginUserDataPath());
            splashScreenRuntimeService = new SplashScreenRuntimeService(IsPlayniteForegroundWindow);

            horizontalFocusFixService = new NavigationFixService(
                api,
                () => Settings.IsWelcomeHubOpen,
                () => Settings.HubCurrentPage,
                page => Settings.HubCurrentPage = page,
                () => Settings.ShowHubAppsPage);

            _ = Task.Run(() =>
            {
                try
                {
                    CleanupLegacyNewsCache();
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] CleanupLegacyNewsCache failed.");
                }
            });

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

                if (e.PropertyName == nameof(Settings.SteamFriendsEnabled) ||
                    e.PropertyName == nameof(Settings.SteamApiKey) ||
                    e.PropertyName == nameof(Settings.SteamId64) ||
                    e.PropertyName == nameof(Settings.SteamAccountSteamId64) ||
                    e.PropertyName == nameof(Settings.ShowOffline) ||
                    e.PropertyName == nameof(Settings.NotifyOnGameStart) ||
                    e.PropertyName == nameof(Settings.NotifyOnConnect))
                {
                    if (Settings.SteamFriendsEnabled)
                    {
                        steamFriendsService?.Start();
                    }
                    else
                    {
                        steamFriendsService?.Stop();
                    }
                }

                if (e.PropertyName == nameof(Settings.SteamStoreEnabled) && Settings.SteamStoreEnabled == false)
                {
                    OnUi(() =>
                    {
                        Settings.SteamStoreDeals.Clear();
                        Settings.SteamStoreNewReleases.Clear();
                        Settings.SteamStoreTopSellers.Clear();
                        Settings.SteamStoreUpcoming.Clear();
                        Settings.SteamStoreWishlisted.Clear();
                        Settings.SteamStoreMyWishlist.Clear();
                        Settings.SteamStoreRecommended.Clear();
                        Settings.SteamStoreRecommendedHub.Clear();
                        Settings.NotifyHubForYouStorePageStateChanged();

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
                        Settings.SteamStoreDetailsScreenshot4 = string.Empty;
                        Settings.SteamStoreDetailsScreenshot5 = string.Empty;
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

                libraryNewsRotationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(15)
                };
                libraryNewsRotationTimer.Tick += LibraryNewsRotationTimer_Tick;
            }
        }


        public async void ConnectSteamAccountFromSettings()
        {
            if (steamAccountSessionService == null || Settings == null)
            {
                return;
            }

            // The Connect button is only shown while disconnected. If the command is
            // somehow invoked while already connected, do nothing instead of reopening Steam.
            if (Settings.SteamAccountConnected)
            {
                return;
            }

            logger.Info("[SteamAccount] Settings login requested");

            OnUi(() =>
            {
                Settings.SteamAccountBusy = true;
                Settings.SteamAccountStatus = "Opening Steam login...";
            });

            try
            {
                var result = await steamAccountSessionService
                    .AuthenticateInteractiveAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                ApplySteamAccountSession(
                    result,
                    result?.IsConnected == true
                        ? "Connected to Steam."
                        : (result?.Error ?? "Steam login cancelled."));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[SteamAccount] Connect failed.");
                OnUi(() => Settings.SteamAccountStatus = "Steam connection failed.");
            }
            finally
            {
                OnUi(() => Settings.SteamAccountBusy = false);
                SaveSettingsSafe();
                steamFriendsService?.Start();
            }
        }

        public async void CheckSteamAccountFromSettings()
        {
            if (steamAccountSessionService == null || Settings == null)
            {
                return;
            }

            logger.Info("[SteamAccount] Settings session check requested");

            OnUi(() =>
            {
                Settings.SteamAccountBusy = true;
                Settings.SteamAccountStatus = "Checking Steam session...";
            });

            try
            {
                var result = await steamAccountSessionService
                    .ProbeAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                if (result?.IsConnected == true)
                {
                    ApplySteamAccountSession(result, "Steam session is connected.");

                    OnUi(() => PlayniteApi.Dialogs.ShowMessage(
                        "✓ " + Loc("SteamAccount_ConnectedTitle", "Connected to Steam") +
                        Environment.NewLine + Environment.NewLine +
                        Loc(
                            "SteamAccount_ConnectedDesc",
                            "Your Steam Store session is active. Personalized Store sections are ready to use."),
                        Loc("SteamAccount_CheckButton", "Check connection"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));
                }
                else if (result != null && string.IsNullOrWhiteSpace(result.Error))
                {
                    // Steam explicitly answered that the Store session is logged out.
                    ApplySteamAccountSession(result, "Steam session is not connected.");

                    OnUi(() => PlayniteApi.Dialogs.ShowMessage(
                        Loc("SteamAccount_DisconnectedTitle", "Not connected to Steam") +
                        Environment.NewLine + Environment.NewLine +
                        Loc(
                            "SteamAccount_DisconnectedDesc",
                            "Sign in to enable My Wishlist, For You and other personalized Steam Store features."),
                        Loc("SteamAccount_CheckButton", "Check connection"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                }
                else
                {
                    // A temporary WebView/network failure is not proof that the user logged out.
                    // Keep the current state, but clearly report that the check could not complete.
                    ApplySteamAccountSession(
                        result,
                        result?.Error ?? "Steam session check failed.");

                    OnUi(() => PlayniteApi.Dialogs.ShowMessage(
                        "The Steam connection could not be checked. Your current session was kept unchanged. Please try again.",
                        Loc("SteamAccount_CheckButton", "Check connection"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[SteamAccount] Probe failed.");
                OnUi(() =>
                {
                    Settings.SteamAccountStatus = "Steam session check failed.";
                    PlayniteApi.Dialogs.ShowMessage(
                        "The Steam connection could not be checked. Your current session was kept unchanged. Please try again.",
                        Loc("SteamAccount_CheckButton", "Check connection"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
            finally
            {
                OnUi(() => Settings.SteamAccountBusy = false);
                SaveSettingsSafe();
            }
        }

        private Task ScheduleStartupSteamAuthCheckAsync()
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (Settings?.SteamStoreEnabled != true)
                    {
                        return;
                    }

                    await Task.Delay(StartupSteamAuthCheckDelay).ConfigureAwait(false);

                    if (Settings?.SteamStoreEnabled != true)
                    {
                        return;
                    }

                    // Opening the Store validates the same Store-domain session. If a Store refresh
                    // is already running, let it finish first, then reuse its probe result instead
                    // of opening a second hidden WebView for the same account page.
                    if (steamStoreOpenLock.CurrentCount == 0)
                    {
                        await steamStoreOpenLock.WaitAsync().ConfigureAwait(false);
                        steamStoreOpenLock.Release();
                    }

                    if (HasRecentSteamStoreAuthProbe())
                    {
                        DebugLog("[Startup Steam Auth] skipped | Store refresh already validated the session");
                        return;
                    }

                    var state = await LoadStartupSteamNotificationRefreshStateAsync().ConfigureAwait(false)
                        ?? new StartupSteamNotificationRefreshState();

                    var nowUtc = DateTime.UtcNow;
                    var wasConnectedBeforeProbe = Settings?.SteamAccountConnected == true &&
                        !string.IsNullOrWhiteSpace(Settings?.SteamAccountSteamId64);
                    var hasKnownAccount =
                        Settings?.SteamAccountConnected == true ||
                        !string.IsNullOrWhiteSpace(Settings?.SteamAccountSteamId64);

                    DebugLog($"[Startup Steam Auth] START | known={hasKnownAccount} | wasConnected={wasConnectedBeforeProbe}");

                    if (hasKnownAccount && steamAccountSessionService != null)
                    {
                        try
                        {
                            var probe = await steamAccountSessionService.ProbeAsync(CancellationToken.None).ConfigureAwait(false);
                            var connected = probe?.IsConnected == true;

                            ApplySteamAccountSession(
                                probe,
                                connected ? "Steam session is connected." : "Steam session is not connected.",
                                allowVisibleStoreUiRebuild: false
                            );

                            state.LastAuthProbeUtc = DateTime.UtcNow;

                            // If the user was connected before and Steam now rejects the session,
                            // ApplySteamAccountSession already shows the stronger "session expired" toast.
                            // Do not also show the generic "please authenticate" reminder.
                            if (wasConnectedBeforeProbe && !connected)
                            {
                                await SaveStartupSteamNotificationRefreshStateAsync(state).ConfigureAwait(false);
                                SaveSettingsSafe();
                                DebugLog("[Startup Steam Auth] END | session expired notification path");
                                return;
                            }

                            if (connected)
                            {
                                await SaveStartupSteamNotificationRefreshStateAsync(state).ConfigureAwait(false);
                                SaveSettingsSafe();
                                DebugLog("[Startup Steam Auth] END | connected");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Do not mark the account disconnected on a generic network/startup failure.
                            logger.Warn(ex, "[Startup Steam Auth] Steam auth probe failed.");
                            return;
                        }
                    }

                    // Steam has never been configured, or no valid account is currently known.
                    // Do not show a global notification: the configuration status is already
                    // visible in the fullscreen settings.
                    DebugLog("[Startup Steam Auth] no configured Steam account; generic reminder skipped");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[Startup Steam Auth] startup auth check failed.");
                }
            });
        }

        private Task ScheduleStartupSteamNotificationRefreshAsync()
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (Settings?.SteamStoreEnabled != true)
                    {
                        return;
                    }

                    if (!HasSteamAccountForBackgroundNotificationRefresh())
                    {
                        DebugLog("[Startup Steam Notify] skipped | no Steam account/session known");
                        return;
                    }

                    await Task.Delay(StartupSteamNotificationInitialDelay).ConfigureAwait(false);

                    for (var attempt = 1; attempt <= StartupSteamNotificationMaxIdleAttempts; attempt++)
                    {
                        if (IsStartupSteamNotificationRefreshSafeNow())
                        {
                            await RunStartupSteamNotificationRefreshAsync().ConfigureAwait(false);
                            return;
                        }

                        DebugLog($"[Startup Steam Notify] idle gate not ready | attempt={attempt}/{StartupSteamNotificationMaxIdleAttempts}");
                        await Task.Delay(StartupSteamNotificationRetryDelay).ConfigureAwait(false);
                    }

                    DebugLog("[Startup Steam Notify] skipped | Playnite never reached a safe idle state");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[Startup Steam Notify] delayed refresh failed.");
                }
            });
        }

        private async Task RunStartupSteamNotificationRefreshAsync()
        {
            if (!await startupSteamNotificationRefreshLock.WaitAsync(0).ConfigureAwait(false))
            {
                DebugLog("[Startup Steam Notify] skipped | refresh already running");
                return;
            }

            try
            {
                if (Settings?.SteamStoreEnabled != true || !HasSteamAccountForBackgroundNotificationRefresh())
                {
                    return;
                }

                var state = await LoadStartupSteamNotificationRefreshStateAsync().ConfigureAwait(false)
                    ?? new StartupSteamNotificationRefreshState();

                var nowUtc = DateTime.UtcNow;
                var authDue = state.LastAuthProbeUtc == DateTime.MinValue ||
                    (nowUtc - state.LastAuthProbeUtc) >= StartupSteamNotificationDailyInterval;

                if (authDue && HasRecentSteamStoreAuthProbe())
                {
                    state.LastAuthProbeUtc = lastSteamStoreAuthProbeUtc;
                    authDue = false;
                    DebugLog("[Startup Steam Notify] auth probe reused from current Store session");
                }

                var wishlistDue = state.LastWishlistRefreshUtc == DateTime.MinValue ||
                    (nowUtc - state.LastWishlistRefreshUtc) >= StartupSteamNotificationDailyInterval;

                if (!authDue && !wishlistDue)
                {
                    DebugLog($"[Startup Steam Notify] skipped | already checked today | auth={state.LastAuthProbeUtc:o} | wishlist={state.LastWishlistRefreshUtc:o}");
                    return;
                }

                if (!IsStartupSteamNotificationRefreshSafeNow())
                {
                    DebugLog("[Startup Steam Notify] skipped | state changed before refresh");
                    return;
                }

                DebugLog($"[Startup Steam Notify] START | authDue={authDue} | wishlistDue={wishlistDue}");

                if (authDue && steamAccountSessionService != null)
                {
                    try
                    {
                        var probe = await steamAccountSessionService.ProbeAsync(CancellationToken.None).ConfigureAwait(false);
                        ApplySteamAccountSession(
                            probe,
                            probe?.IsConnected == true ? "Steam session is connected." : "Steam session is not connected.",
                            allowVisibleStoreUiRebuild: false
                        );
                    }
                    catch (Exception ex)
                    {
                        // Do not mark the account disconnected on a generic startup/network failure.
                        // The expired-session toast is only shown when Steam explicitly reports that
                        // the previous connected session is no longer valid.
                        logger.Warn(ex, "[Startup Steam Notify] Steam auth probe failed.");
                    }
                    finally
                    {
                        state.LastAuthProbeUtc = DateTime.UtcNow;
                    }
                }

                var connectedAfterAuth = CanUseConnectedSteamStoreAccount();

                if (wishlistDue && connectedAfterAuth && IsStartupSteamNotificationRefreshSafeNow())
                {
                    try
                    {
                        var language = GetResolvedSteamStoreLanguage();
                        var region = GetResolvedSteamStoreRegion();
                        var personalizationContext = await BuildSteamStorePersonalizationContextAsync().ConfigureAwait(false);

                        await RefreshSteamStoreMyWishlistFromSteamAsync(
                            personalizationContext,
                            language,
                            region,
                            reportProgress: null
                        ).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[Startup Steam Notify] My Wishlist background refresh failed.");
                    }
                    finally
                    {
                        state.LastWishlistRefreshUtc = DateTime.UtcNow;
                    }
                }
                else if (wishlistDue)
                {
                    state.LastWishlistRefreshUtc = DateTime.UtcNow;
                    DebugLog($"[Startup Steam Notify] wishlist skipped | connected={connectedAfterAuth}");
                }

                await SaveStartupSteamNotificationRefreshStateAsync(state).ConfigureAwait(false);
                SaveSettingsSafe();

                DebugLog("[Startup Steam Notify] END");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Startup Steam Notify] refresh failed.");
            }
            finally
            {
                startupSteamNotificationRefreshLock.Release();
            }
        }

        private bool HasSteamAccountForBackgroundNotificationRefresh()
        {
            try
            {
                if (Settings == null)
                {
                    return false;
                }

                return Settings.SteamAccountConnected ||
                    !string.IsNullOrWhiteSpace(Settings.SteamAccountSteamId64) ||
                    !string.IsNullOrWhiteSpace(Settings.SteamId64);
            }
            catch
            {
                return false;
            }
        }

        private bool IsStartupSteamNotificationRefreshSafeNow()
        {
            try
            {
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return false;
                }

                if (!IsAnikiThemeActive())
                {
                    return false;
                }

                if (Settings == null || Settings.SteamStoreEnabled != true)
                {
                    return false;
                }

                if (!IsPlayniteForegroundWindow())
                {
                    return false;
                }

                if ((DateTime.UtcNow - lastControllerInputUtc) < TimeSpan.FromSeconds(20))
                {
                    return false;
                }

                if (IsAnyPlayniteGameRunningOrLaunching())
                {
                    return false;
                }

                if (Settings.SteamStoreLoading ||
                    Settings.SteamStoreDetailsVisible ||
                    Settings.SteamStoreScreenshotViewerVisible ||
                    Settings.IsWelcomeHubClosing)
                {
                    return false;
                }

                if (IsSteamStoreViewVisible())
                {
                    return false;
                }

                if (inGameOverlayService != null && inGameOverlayService.IsOverlayVisible)
                {
                    return false;
                }

                if (anikiWindowManager != null && anikiWindowManager.HasOpenWindow)
                {
                    return false;
                }

                if (IsSecondaryWindowVisibleForKonami())
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsAnyPlayniteGameRunningOrLaunching()
        {
            try
            {
                return PlayniteApi?.Database?.Games?.Any(x => x != null && (x.IsRunning || x.IsLaunching)) == true;
            }
            catch
            {
                return false;
            }
        }

        private string GetStartupSteamNotificationRefreshStatePath()
        {
            return Path.Combine(GetPluginUserDataPath(), "SteamStore", "StartupSteamNotificationRefreshState.json");
        }

        private async Task<StartupSteamNotificationRefreshState> LoadStartupSteamNotificationRefreshStateAsync()
        {
            try
            {
                var path = GetStartupSteamNotificationRefreshStatePath();
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = await Task.Run(() => File.ReadAllText(path)).ConfigureAwait(false);
                return await Task.Run(() => JsonConvert.DeserializeObject<StartupSteamNotificationRefreshState>(json)).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private async Task SaveStartupSteamNotificationRefreshStateAsync(StartupSteamNotificationRefreshState state)
        {
            try
            {
                var path = GetStartupSteamNotificationRefreshStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = await Task.Run(() => JsonConvert.SerializeObject(state ?? new StartupSteamNotificationRefreshState(), Formatting.Indented)).ConfigureAwait(false);
                await Task.Run(() => File.WriteAllText(path, json)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Startup Steam Notify] Failed to save refresh state.");
            }
        }

        private sealed class StartupSteamNotificationRefreshState
        {
            public DateTime LastAuthProbeUtc { get; set; }
            public DateTime LastWishlistRefreshUtc { get; set; }
            public DateTime LastAuthReminderUtc { get; set; }
        }

        public void DisconnectSteamAccountFromSettings()
        {
            try
            {
                steamAccountSessionService?.ClearSession();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[SteamAccount] Disconnect failed.");
            }

            OnUi(() =>
            {
                Settings.SteamAccountConnected = false;
                Settings.SteamAccountBusy = false;
                Settings.SteamAccountSteamId64 = string.Empty;
                Settings.SteamAccountProfileUrl = string.Empty;
                Settings.SteamAccountStatus = "Disconnected";
                ClearAuthenticatedSteamStoreSectionsOnUi();
                UpdateSteamStoreActiveSection();
                UpdateSteamStoreAvailabilityState();
            });

            SaveSettingsSafe();
        }

        private void ApplySteamAccountSession(SteamAccountSessionInfo session, string fallbackStatus, bool allowVisibleStoreUiRebuild = true)
        {
            var shouldNotifySessionExpired = false;
            var shouldReloadPersonalStoreData = false;

            OnUi(() =>
            {
                // A WebView timeout, DNS/network failure or temporary CEF error must not be
                // interpreted as an explicit Steam logout. In that case, keep the previous
                // authenticated state and the already loaded personal Store collections.
                var transientCheckFailure = session == null ||
                    (session.IsConnected != true && !string.IsNullOrWhiteSpace(session.Error));

                if (transientCheckFailure)
                {
                    Settings.SteamAccountStatus = !string.IsNullOrWhiteSpace(fallbackStatus)
                        ? fallbackStatus
                        : (!string.IsNullOrWhiteSpace(session?.Error)
                            ? session.Error
                            : "Steam session check failed.");
                    return;
                }

                // This is an explicit Store-domain answer (connected or genuinely logged out).
                // Reuse it briefly so startup jobs do not open the same hidden account page twice.
                lastSteamStoreAuthProbeUtc = DateTime.UtcNow;

                var connected = session.IsConnected;
                var previousSteamId = Settings.SteamAccountSteamId64?.Trim() ?? string.Empty;
                var wasConnected = Settings.SteamAccountConnected && !string.IsNullOrWhiteSpace(previousSteamId);

                Settings.SteamAccountConnected = connected;

                if (connected)
                {
                    var steamId = session.SteamId64?.Trim() ?? string.Empty;
                    Settings.SteamAccountSteamId64 = steamId;
                    Settings.SteamAccountProfileUrl = string.IsNullOrWhiteSpace(steamId)
                        ? string.Empty
                        : "https://steamcommunity.com/profiles/" + steamId;

                    // Keep the legacy field synchronized so existing Steam Friends and Steam API code keep working.
                    Settings.SteamId64 = steamId;
                    Settings.SteamAccountStatus = "Connected: " + steamId;

                    shouldReloadPersonalStoreData = !wasConnected ||
                        !string.Equals(previousSteamId, steamId, StringComparison.Ordinal);

                    if (shouldReloadPersonalStoreData)
                    {
                        // A Hub cache-only request may have happened before authentication was ready.
                        // Invalidate both throttles so Wishlist / For You can be loaded immediately.
                        lastSteamStoreCacheOnlyLoadUtc = DateTime.MinValue;
                        lastSteamStoreOpenRequestUtc = DateTime.MinValue;
                    }

                    UpdateSteamStoreActiveSectionIfAllowed(allowVisibleStoreUiRebuild);
                    UpdateSteamStoreAvailabilityState();
                }
                else
                {
                    Settings.SteamAccountStatus = !string.IsNullOrWhiteSpace(fallbackStatus)
                        ? fallbackStatus
                        : "Not connected";

                    ClearAuthenticatedSteamStoreSectionsOnUi();
                    UpdateSteamStoreActiveSectionIfAllowed(allowVisibleStoreUiRebuild);
                    UpdateSteamStoreAvailabilityState();

                    shouldNotifySessionExpired = wasConnected;
                }
            });

            if (shouldReloadPersonalStoreData)
            {
                _ = OnSteamStoreViewOpenedAsync();
            }

            if (shouldNotifySessionExpired)
            {
                ShowSteamSessionExpiredToastThrottled();
            }
        }

        private void ClearAuthenticatedSteamStoreSectionsOnUi()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.SteamStoreMyWishlist.Clear();
            Settings.SteamStoreRecommended.Clear();
            Settings.SteamStoreRecommendedHub.Clear();
            Settings.NotifyHubForYouStorePageStateChanged();
        }


        private bool IsSteamStoreCurrentVisibleSectionEmpty()
        {
            if (Settings == null)
            {
                return true;
            }

            return (Settings.SteamStoreCurrentItems?.Count ?? 0) == 0 &&
                   (Settings.SteamStoreCurrentListItems?.Count ?? 0) == 0 &&
                   string.IsNullOrWhiteSpace(Settings.SteamStoreHeroName);
        }

        private bool HasSteamStoreCacheUiData()
        {
            if (Settings == null)
            {
                return false;
            }

            return (Settings.SteamStoreDeals?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreTopSellers?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreNewReleases?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreUpcoming?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreWishlisted?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreMyWishlist?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreRecommended?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreCurrentItems?.Count ?? 0) > 0 ||
                   (Settings.SteamStoreCurrentListItems?.Count ?? 0) > 0 ||
                   !string.IsNullOrWhiteSpace(Settings.SteamStoreHeroName);
        }

        private void UpdateSteamStoreActiveSectionIfAllowed(bool allowVisibleStoreUiRebuild)
        {
            if (Settings == null)
            {
                return;
            }

            var selectedSection = Settings.SteamStoreSelectedSection ?? string.Empty;

            // Auth-only pages must still rebuild so the hero auth message can appear/disappear.
            var selectedSectionIsAuthOnly = IsSteamStoreSectionAuthOnly(selectedSection);

            if (allowVisibleStoreUiRebuild || selectedSectionIsAuthOnly || IsSteamStoreCurrentVisibleSectionEmpty())
            {
                UpdateSteamStoreActiveSection();
                return;
            }

            DebugLog($"[Steam Store] skipped visible section rebuild | section={selectedSection} | current={(Settings.SteamStoreCurrentItems?.Count ?? 0)} | list={(Settings.SteamStoreCurrentListItems?.Count ?? 0)} | hero={Settings.SteamStoreHeroName}");
        }

        private bool HasRecentSteamStoreAuthProbe()
        {
            var lastProbeUtc = lastSteamStoreAuthProbeUtc;
            return lastProbeUtc != DateTime.MinValue &&
                (DateTime.UtcNow - lastProbeUtc) < SteamStoreAuthProbeReuseWindow;
        }

        private string GetEffectiveSteamIdInput()
        {
            if (!string.IsNullOrWhiteSpace(Settings?.SteamAccountSteamId64))
            {
                return Settings.SteamAccountSteamId64.Trim();
            }

            return Settings?.SteamId64?.Trim() ?? string.Empty;
        }

        private string GetSteamForYouCacheKey(SteamRecommendationProfile recommendationProfile)
        {
            // Keep the public cache name stable and user-independent.
            // The language and region are appended by SteamStoreRecommendationService,
            // so the final file is: SteamStore/StoreCache/steam_recommender_<language>_<region>.json
            return "steam_recommender";
        }

        private bool CanUseConnectedSteamStoreAccount()
        {
            return Settings?.SteamAccountConnected == true &&
                !string.IsNullOrWhiteSpace(Settings?.SteamAccountSteamId64);
        }

        private string GetConnectedSteamAccountId64()
        {
            return Settings?.SteamAccountSteamId64?.Trim() ?? string.Empty;
        }

        private string GetSteamMyWishlistCacheKey()
        {
            // My Wishlist is personal account data. Keep the cache bound to the
            // authenticated Steam account so another user/session never reuses it.
            var steamId = GetConnectedSteamAccountId64();
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return "steam_mywishlist_no_account";
            }

            var safeSteamId = new string(steamId.Where(char.IsDigit).ToArray());
            return string.IsNullOrWhiteSpace(safeSteamId)
                ? "steam_mywishlist_no_account"
                : "steam_mywishlist_" + safeSteamId;
        }

        private static bool IsSteamWishlistFinalUrlValid(string finalUrl, string steamId64)
        {
            if (string.IsNullOrWhiteSpace(finalUrl))
            {
                return false;
            }

            if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host ?? string.Empty;
            if (!host.Equals("store.steampowered.com", StringComparison.OrdinalIgnoreCase) &&
                !host.EndsWith(".steampowered.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = (uri.AbsolutePath ?? string.Empty).Replace('\\', '/');

            if (path.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            // Steam redirects some users with a custom profile URL to /wishlist/id/{name}/.
            if (path.IndexOf("/wishlist/id/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(steamId64))
            {
                return false;
            }

            return path.IndexOf("/wishlist/profiles/" + steamId64, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (path.IndexOf("/wishlist/profiles/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 path.IndexOf(steamId64, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildSteamAppStoreUrl(int appId)
        {
            return appId > 0 ? "https://store.steampowered.com/app/" + appId.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string HashForCacheKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var bytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
                return string.Concat(bytes.Take(8).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        private async Task<List<SteamStoreItem>> RefreshSteamStoreMyWishlistFromSteamAsync(
            SteamStorePersonalizationContext personalizationContext,
            string language,
            string region,
            Action<int> reportProgress,
            bool allowVisibleStoreUiRebuild = true)
        {
            var result = new List<SteamStoreItem>();

            if (steamAccountSessionService == null || steamStoreService == null)
            {
                return null;
            }

            if (!CanUseConnectedSteamStoreAccount())
            {
                logger.Info("[Steam My Wishlist] skipped | Steam Store account is not authenticated | keeping current cache.");
                return null;
            }

            var steamId = GetConnectedSteamAccountId64();
            if (string.IsNullOrWhiteSpace(steamId))
            {
                logger.Info("[Steam My Wishlist] skipped | authenticated SteamID64 missing | keeping current cache.");
                return null;
            }

            var wishlist = await steamAccountSessionService.GetWishlistAppIdsAsync(
                steamId,
                language,
                region,
                CancellationToken.None
            ).ConfigureAwait(false);

            // A Wishlist Web API response does not use the CEF Store session, so it must not
            // overwrite the current authentication state. The WebView fallback still does.
            if (wishlist?.LoadedFromWebApi != true && wishlist?.Session != null)
            {
                ApplySteamAccountSession(
                    wishlist.Session,
                    wishlist.Session.IsConnected ? "Connected to Steam." : (wishlist.Error ?? "Steam Store session is not connected."),
                    allowVisibleStoreUiRebuild
                );
            }

            if (wishlist?.LoadedFromWebApi != true && wishlist?.Session?.IsConnected != true)
            {
                logger.Info($"[Steam My Wishlist] unavailable | error={wishlist?.Error ?? "Steam Store session is not connected."} | finalUrl={wishlist?.FinalUrl ?? string.Empty} | keeping current cache");
                return null;
            }

            if (wishlist?.Success != true)
            {
                logger.Info($"[Steam My Wishlist] unavailable | error={wishlist?.Error ?? "unknown"} | finalUrl={wishlist?.FinalUrl ?? string.Empty} | keeping current cache");
                return null;
            }

            if (wishlist.LoadedFromWebApi != true && !IsSteamWishlistFinalUrlValid(wishlist.FinalUrl, steamId))
            {
                logger.Info($"[Steam My Wishlist] rejected invalid wishlist page | finalUrl={wishlist.FinalUrl ?? string.Empty} | steamId={steamId} | keeping current cache");
                return null;
            }

            var orderedAppIds = (wishlist.OrderedAppIds != null && wishlist.OrderedAppIds.Count > 0
                    ? wishlist.OrderedAppIds
                    : (wishlist.AppIds ?? new HashSet<int>()).ToList())
                .Where(x => x > 0)
                .Distinct()
                .Take(20)
                .ToList();

            logger.Info($"[Steam My Wishlist] loaded appids | source={(wishlist.LoadedFromWebApi ? "web-api" : "store-page")} | total={wishlist.AppIds?.Count ?? 0} | selected={orderedAppIds.Count} | finalUrl={wishlist.FinalUrl}");

            if (personalizationContext != null)
            {
                foreach (var appId in wishlist.AppIds ?? new HashSet<int>())
                {
                    if (appId > 0)
                    {
                        personalizationContext.WishlistSteamAppIds.Add(appId);
                    }
                }
            }

            var rank = 0;
            foreach (var appId in orderedAppIds)
            {
                rank++;
                var item = new SteamStoreItem
                {
                    AppId = appId,
                    Name = "Steam App " + appId.ToString(CultureInfo.InvariantCulture),
                    StoreUrl = BuildSteamAppStoreUrl(appId),
                    Source = "Steam User Wishlist",
                    SteamRank = rank,
                    IsInWishlist = true
                };

                try
                {
                    await steamStoreService.EnrichStoreItemDetailsAsync(
                        item,
                        language,
                        region,
                        downloadMedia: false
                    ).ConfigureAwait(false);

                    if (steamStorePersonalizationService != null && steamStorePersonalizationService.LooksLikeNonGameContent(item))
                    {
                        logger.Info($"[Steam My Wishlist] dropped non-game | {item.Name} | AppId={item.AppId} | type={item.AppType}");
                        continue;
                    }

                    item.IsInWishlist = true;
                    result.Add(item);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"[Steam My Wishlist] failed to enrich AppId={appId}");
                }

                if (reportProgress != null)
                {
                    reportProgress(Math.Min(95, 20 + (int)Math.Round((rank / Math.Max(1.0, orderedAppIds.Count)) * 65)));
                }
            }

            result = PersonalizeSteamStoreSection(result, personalizationContext, "MyWishlist", 20)
                .Take(20)
                .ToList();

            if (orderedAppIds.Count > 0 && result.Count == 0)
            {
                logger.Info("[Steam My Wishlist] all item enrichments failed | keeping current cache.");
                return null;
            }

            foreach (var item in result)
            {
                item.IsInWishlist = true;
            }

            await steamStoreService.CacheStoreListImagesForSectionAsync(
                result,
                "STORE My Wishlist",
                language,
                region
            ).ConfigureAwait(false);

            await steamStoreService.SaveUserWishlistCacheAsync(
                GetSteamMyWishlistCacheKey(),
                language,
                region,
                result
            ).ConfigureAwait(false);

            await ProcessWishlistNotificationsAsync(result).ConfigureAwait(false);

            logger.Info($"[Steam My Wishlist] cache saved | count={result.Count} | key={GetSteamMyWishlistCacheKey()}");
            return result;
        }

        private async Task ProcessWishlistNotificationsAsync(List<SteamStoreItem> wishlistItems)
        {
            try
            {
                var items = (wishlistItems ?? new List<SteamStoreItem>())
                    .Where(x => x != null && x.AppId > 0)
                    .Take(20)
                    .ToList();

                if (items.Count == 0)
                {
                    return;
                }

                var cache = await LoadWishlistNotificationCacheAsync().ConfigureAwait(false);
                var isFirstScan = cache == null || cache.Items == null || cache.Items.Count == 0;
                if (cache == null)
                {
                    cache = new WishlistNotificationCacheRoot();
                }

                var byAppId = (cache.Items ?? new List<WishlistNotificationCacheItem>())
                    .Where(x => x != null && x.AppId > 0)
                    .GroupBy(x => x.AppId)
                    .ToDictionary(x => x.Key, x => x.First());

                var notificationsSent = 0;

                foreach (var item in items)
                {
                    WishlistNotificationCacheItem oldEntry;
                    byAppId.TryGetValue(item.AppId, out oldEntry);

                    if (!isFirstScan && oldEntry != null && notificationsSent < MaxWishlistNotificationsPerRefresh)
                    {
                        var dealKey = BuildWishlistDealNotificationKey(item);
                        if (IsWishlistDealNotificationCandidate(oldEntry, item, dealKey))
                        {
                            var discount = item.DiscountPercent > 0
                                ? "-" + item.DiscountPercent.ToString(CultureInfo.InvariantCulture) + "%"
                                : item.DiscountDisplay ?? string.Empty;

                            ShowGlobalToast(
                                LocFormat("LOCWishlistDealToast", "{0} from your wishlist is now {1} off.", Safe(item.Name), discount),
                                "wishlistDeal"
                            );

                            oldEntry.LastDealNotifiedKey = dealKey;
                            notificationsSent++;
                        }
                    }

                    if (!isFirstScan && oldEntry != null && notificationsSent < MaxWishlistNotificationsPerRefresh)
                    {
                        if (IsWishlistReleaseNotificationCandidate(oldEntry, item))
                        {
                            ShowGlobalToast(
                                LocFormat("LOCWishlistReleasedToast", "{0} from your wishlist is now available.", Safe(item.Name)),
                                "wishlistReleased"
                            );

                            oldEntry.ReleaseNotified = true;
                            notificationsSent++;
                        }
                    }

                    byAppId[item.AppId] = BuildWishlistNotificationCacheItem(item, oldEntry);
                }

                cache.LastUpdatedUtc = DateTime.UtcNow;
                cache.Items = byAppId.Values
                    .Where(x => x != null && x.AppId > 0)
                    .OrderByDescending(x => x.LastSeenUtc)
                    .Take(200)
                    .ToList();

                await SaveWishlistNotificationCacheAsync(cache).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Wishlist Notify] Failed to process wishlist notifications.");
            }
        }

        private bool IsWishlistDealNotificationCandidate(WishlistNotificationCacheItem oldEntry, SteamStoreItem item, string dealKey)
        {
            if (oldEntry == null || item == null || string.IsNullOrWhiteSpace(dealKey))
            {
                return false;
            }

            if (!IsSteamStoreGameItem(item))
            {
                return false;
            }

            if (item.DiscountPercent <= 0 || oldEntry.LastDiscountPercent > 0)
            {
                return false;
            }

            return !string.Equals(oldEntry.LastDealNotifiedKey, dealKey, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsWishlistReleaseNotificationCandidate(WishlistNotificationCacheItem oldEntry, SteamStoreItem item)
        {
            if (oldEntry == null || item == null || oldEntry.ReleaseNotified)
            {
                return false;
            }

            if (!IsSteamStoreGameItem(item))
            {
                return false;
            }

            var wasUnreleased = oldEntry.WasComingSoon || oldEntry.WasPreorder;
            var nowReleased = !item.ComingSoon && !item.IsPreorder;

            if (!wasUnreleased || !nowReleased)
            {
                return false;
            }

            if (!IsWishlistReleaseDateSafeForNotification(item.ReleaseDateDisplay, item))
            {
                logger.Info($"[Wishlist Notify] release candidate rejected | AppId={item.AppId} | Name={item.Name} | Release='{item.ReleaseDateDisplay}' | ComingSoon={item.ComingSoon} | Preorder={item.IsPreorder}");
                return false;
            }

            return true;
        }

        private static bool IsSteamStoreGameItem(SteamStoreItem item)
        {
            return item != null && string.Equals(item.AppType ?? string.Empty, "game", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWishlistReleaseDateSafeForNotification(string releaseDateDisplay, SteamStoreItem item)
        {
            var text = (releaseDateDisplay ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return !string.IsNullOrWhiteSpace(item?.FinalPriceDisplay) || item?.DiscountPercent > 0;
            }

            var lower = text.ToLowerInvariant();
            var blockedTokens = new[]
            {
                "coming soon", "coming", "soon", "tba", "tbd", "to be announced",
                "wishlist now", "announce", "announced", "quarter", "q1", "q2", "q3", "q4"
            };

            if (blockedTokens.Any(x => lower.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            var currentYear = DateTime.Now.Year;
            foreach (Match match in Regex.Matches(text, @"\b(20\d{2}|19\d{2})\b"))
            {
                int year;
                if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                {
                    if (Regex.IsMatch(text.Trim(), @"^(20\d{2}|19\d{2})$"))
                    {
                        return false;
                    }

                    if (year > currentYear)
                    {
                        return false;
                    }
                }
            }

            DateTime parsed;
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsed) ||
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
            {
                return parsed.Date <= DateTime.Now.Date.AddDays(1);
            }

            return true;
        }

        private string BuildWishlistDealNotificationKey(SteamStoreItem item)
        {
            if (item == null || item.AppId <= 0 || item.DiscountPercent <= 0)
            {
                return string.Empty;
            }

            return string.Join(
                ":",
                item.AppId.ToString(CultureInfo.InvariantCulture),
                item.DiscountPercent.ToString(CultureInfo.InvariantCulture),
                (item.FinalPriceDisplay ?? string.Empty).Trim()
            );
        }

        private WishlistNotificationCacheItem BuildWishlistNotificationCacheItem(SteamStoreItem item, WishlistNotificationCacheItem previous)
        {
            previous = previous ?? new WishlistNotificationCacheItem();
            previous.AppId = item.AppId;
            previous.Name = item.Name ?? string.Empty;
            previous.LastDiscountPercent = item.DiscountPercent;
            previous.LastFinalPriceDisplay = item.FinalPriceDisplay ?? string.Empty;
            previous.WasComingSoon = item.ComingSoon;
            previous.WasPreorder = item.IsPreorder;
            previous.LastReleaseDateDisplay = item.ReleaseDateDisplay ?? string.Empty;
            previous.LastSeenUtc = DateTime.UtcNow;
            return previous;
        }

        private string GetWishlistNotificationCachePath()
        {
            return Path.Combine(GetPluginUserDataPath(), "SteamStore", "WishlistNotificationCache.json");
        }

        private async Task<WishlistNotificationCacheRoot> LoadWishlistNotificationCacheAsync()
        {
            try
            {
                var path = GetWishlistNotificationCachePath();
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = await Task.Run(() => File.ReadAllText(path)).ConfigureAwait(false);
                return await Task.Run(() => JsonConvert.DeserializeObject<WishlistNotificationCacheRoot>(json)).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private async Task SaveWishlistNotificationCacheAsync(WishlistNotificationCacheRoot cache)
        {
            try
            {
                var path = GetWishlistNotificationCachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = await Task.Run(() => JsonConvert.SerializeObject(cache ?? new WishlistNotificationCacheRoot(), Formatting.Indented)).ConfigureAwait(false);
                await Task.Run(() => File.WriteAllText(path, json)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Wishlist Notify] Failed to save wishlist notification cache.");
            }
        }

        private sealed class WishlistNotificationCacheRoot
        {
            public DateTime LastUpdatedUtc { get; set; }
            public List<WishlistNotificationCacheItem> Items { get; set; } = new List<WishlistNotificationCacheItem>();
        }

        private sealed class WishlistNotificationCacheItem
        {
            public int AppId { get; set; }
            public string Name { get; set; }
            public int LastDiscountPercent { get; set; }
            public string LastFinalPriceDisplay { get; set; }
            public bool WasComingSoon { get; set; }
            public bool WasPreorder { get; set; }
            public string LastReleaseDateDisplay { get; set; }
            public string LastDealNotifiedKey { get; set; }
            public bool ReleaseNotified { get; set; }
            public DateTime LastSeenUtc { get; set; }
        }

        private async Task<List<SteamStoreItem>> RefreshSteamStoreForYouFromSteamAsync(
            SteamStorePersonalizationContext personalizationContext,
            SteamRecommendationProfile recommendationProfile,
            string language,
            string region,
            Action<int> reportProgress,
            bool allowVisibleStoreUiRebuild = true)
        {
            if (steamAccountSessionService == null || steamStoreRecommendationService == null)
            {
                return null;
            }

            var page = await steamAccountSessionService.GetRecommendedPageHtmlAsync(language, region, CancellationToken.None).ConfigureAwait(false);
            if (page?.Session != null)
            {
                ApplySteamAccountSession(page.Session, page.Success ? "Connected to Steam." : page.Error, allowVisibleStoreUiRebuild);
            }

            if (page?.Success != true || string.IsNullOrWhiteSpace(page.Html))
            {
                logger.Info($"[Steam Recommender] unavailable | error={page?.Error ?? "unknown"} | finalUrl={page?.FinalUrl ?? string.Empty} | keeping current cache");
                return null;
            }

            logger.Info($"[Steam Recommender] connected page loaded | finalUrl={page.FinalUrl} | html={page.Html.Length}");

            // Build-safe strict mode:
            // Do not call Steam wishlistdata here. For You now uses Steam Interactive Recommender first.
            // If strict filters leave too few games, /recommended/ is used only as a low-priority supplement.
            logger.Info("[Steam Recommender] wishlistdata disabled | recommender-primary strict mode v5");

            var cacheKey = GetSteamForYouCacheKey(recommendationProfile);
            var primaryItems = await steamStoreRecommendationService.RefreshFromSteamRecommendedHtmlAsync(
                page.Html,
                personalizationContext,
                cacheKey,
                language,
                region,
                reportProgress
            ).ConfigureAwait(false);

            if (primaryItems != null && primaryItems.Count >= 18)
            {
                return primaryItems;
            }

            logger.Info($"[Steam Recommender] primary below target | count={primaryItems?.Count ?? 0} | target=18 | trying /recommended/ supplement");

            var supplementPage = await steamAccountSessionService.GetRecommendedFeedPageHtmlAsync(language, region, CancellationToken.None).ConfigureAwait(false);
            if (supplementPage?.Session != null)
            {
                ApplySteamAccountSession(supplementPage.Session, supplementPage.Success ? "Connected to Steam." : supplementPage.Error, allowVisibleStoreUiRebuild);
            }

            if (supplementPage?.Success != true || string.IsNullOrWhiteSpace(supplementPage.Html))
            {
                logger.Info($"[Steam Recommended Supplement] unavailable | error={supplementPage?.Error ?? "unknown"} | finalUrl={supplementPage?.FinalUrl ?? string.Empty}");

                if (primaryItems != null && primaryItems.Count > 0)
                {
                    return primaryItems;
                }

                logger.Info("[Steam Recommender] refresh produced no usable items | keeping current cache.");
                return null;
            }

            logger.Info($"[Steam Recommended Supplement] connected page loaded | finalUrl={supplementPage.FinalUrl} | html={supplementPage.Html.Length}");

            var finalItems = await steamStoreRecommendationService.FillFromSteamRecommendedFeedHtmlAsync(
                supplementPage.Html,
                primaryItems ?? new List<SteamStoreItem>(),
                personalizationContext,
                cacheKey,
                language,
                region,
                reportProgress
            ).ConfigureAwait(false);

            if (finalItems == null || finalItems.Count == 0)
            {
                logger.Info("[Steam Recommender] refresh produced no usable items | keeping current cache.");
                return null;
            }

            return finalItems;
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

        public void OpenSteamStoreScreenshotViewer(object image)
        {
            try
            {
                var imagePath = image?.ToString();

                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return;
                }

                var finalImage = FirstSteamStoreImage(imagePath);

                if (string.IsNullOrWhiteSpace(finalImage))
                {
                    return;
                }

                OnUi(() =>
                {
                    Settings.SteamStoreScreenshotViewerImage = finalImage;
                    Settings.SteamStoreScreenshotViewerVisible = true;
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open Steam Store screenshot viewer.");
            }
        }

        public void CloseSteamStoreScreenshotViewer()
        {
            try
            {
                OnUi(() =>
                {
                    Settings.SteamStoreScreenshotViewerVisible = false;
                    Settings.SteamStoreScreenshotViewerImage = string.Empty;
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to close Steam Store screenshot viewer.");
            }
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
                    Settings.SteamStoreScreenshotViewerVisible = false;
                    Settings.SteamStoreScreenshotViewerImage = string.Empty;

                    Settings.SteamStoreDetailsTitle = item.Name ?? string.Empty;
                    Settings.SteamStoreDetailsImage = FirstSteamStoreImage(
                        item.HeaderImageLocalPath,
                        item.HeaderImageUrl,
                        item.CapsuleImageLocalPath,
                        item.CapsuleImageUrl
                    );
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
                    Settings.SteamStoreDetailsImage = FirstSteamStoreImage(
                        item.HeaderImageLocalPath,
                        item.HeaderImageUrl,
                        item.CapsuleImageLocalPath,
                        item.CapsuleImageUrl
                    );

                    Settings.SteamStoreDetailsBackgroundImage = FirstSteamStoreImage(
                        item.BackgroundImageLocalPath,
                        item.BackgroundImageUrl,
                        item.HeaderImageLocalPath,
                        item.HeaderImageUrl,
                        Settings.SteamStoreDetailsImage
                    );

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

                    Settings.SteamStoreDetailsScreenshot1 = FirstSteamStoreImage(item.Screenshot1LocalPath, item.Screenshot1Url);
                    Settings.SteamStoreDetailsScreenshot2 = FirstSteamStoreImage(item.Screenshot2LocalPath, item.Screenshot2Url);
                    Settings.SteamStoreDetailsScreenshot3 = FirstSteamStoreImage(item.Screenshot3LocalPath, item.Screenshot3Url);
                    Settings.SteamStoreDetailsScreenshot4 = FirstSteamStoreImage(item.Screenshot4LocalPath, item.Screenshot4Url);
                    Settings.SteamStoreDetailsScreenshot5 = FirstSteamStoreImage(item.Screenshot5LocalPath, item.Screenshot5Url);
                    Settings.SteamStoreDetailsLoading = false;
                });

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to open Steam Store details.");

                OnUi(() =>
                {
                    Settings.SteamStoreScreenshotViewerVisible = false;
                    Settings.SteamStoreScreenshotViewerImage = string.Empty;

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

        private SteamStoreItem ConvertUpcomingToStoreItem(SteamUpcomingGameItem item)
        {
            if (item == null)
            {
                return null;
            }

            var headerImageUrl = !string.IsNullOrWhiteSpace(item.HeaderImage)
                ? item.HeaderImage
                : item.CapsuleImageUrl;

            var headerImageLocalPath = !string.IsNullOrWhiteSpace(item.HeaderImageLocalPath)
                ? item.HeaderImageLocalPath
                : item.CapsuleImageLocalPath;

            // The Store UI is mostly designed around wide Steam headers. Keep capsule as fallback only.
            var capsuleImageUrl = !string.IsNullOrWhiteSpace(headerImageUrl)
                ? headerImageUrl
                : item.CapsuleImageUrl;

            var capsuleImageLocalPath = !string.IsNullOrWhiteSpace(headerImageLocalPath)
                ? headerImageLocalPath
                : item.CapsuleImageLocalPath;

            var backgroundImageLocalPath = !string.IsNullOrWhiteSpace(item.BackgroundImageLocalPath)
                ? item.BackgroundImageLocalPath
                : headerImageLocalPath;

            var backgroundImageUrl = !string.IsNullOrWhiteSpace(item.BackgroundImageUrl)
                ? item.BackgroundImageUrl
                : headerImageUrl;

            return new SteamStoreItem
            {
                AppId = item.AppId,
                Name = item.Name,
                StoreUrl = item.StoreUrl,
                Source = item.Source,

                HeaderImageUrl = headerImageUrl,
                HeaderImageLocalPath = headerImageLocalPath,

                CapsuleImageUrl = capsuleImageUrl,
                CapsuleImageLocalPath = capsuleImageLocalPath,

                BackgroundImageUrl = backgroundImageUrl,
                BackgroundImageLocalPath = backgroundImageLocalPath,

                ShortDescription = item.ShortDescription,
                ReleaseDateDisplay = item.ReleaseDateDisplay,
                FinalPriceDisplay = item.FinalPriceDisplay,
                OriginalPriceDisplay = item.OriginalPriceDisplay,
                DiscountDisplay = item.DiscountDisplay,
                SteamRank = item.SteamRank,
                ComingSoon = item.ComingSoon,
                IsPreorder = item.IsPreorder,
                Genres = item.Genres ?? new List<string>(),
                Categories = item.Categories ?? new List<string>(),
                Tags = item.Tags ?? new List<string>(),
                Developers = item.Developers ?? new List<string>(),
                Publishers = item.Publishers ?? new List<string>(),
                Screenshot1Url = item.Screenshot1Url,
                Screenshot1LocalPath = item.Screenshot1LocalPath,
                Screenshot2Url = item.Screenshot2Url,
                Screenshot2LocalPath = item.Screenshot2LocalPath,
                Screenshot3Url = item.Screenshot3Url,
                Screenshot3LocalPath = item.Screenshot3LocalPath,
                Screenshot4Url = item.Screenshot4Url,
                Screenshot4LocalPath = item.Screenshot4LocalPath,
                Screenshot5Url = item.Screenshot5Url,
                Screenshot5LocalPath = item.Screenshot5LocalPath
            };
        }


        private SteamRecommendationProfile BuildSteamRecommendationProfile()
        {
            try
            {
                var games = PlayniteApi?.Database?.Games?.ToList() ?? new List<Playnite.SDK.Models.Game>();
                if (Settings?.IncludeHidden != true)
                {
                    games = games.Where(g => g != null && g.Hidden != true).ToList();
                }

                if (games.Count == 0)
                {
                    return null;
                }

                List<Playnite.SDK.Models.Game> refTop3;
                var refGame = GetOrSelectRefGameForToday(games, out refTop3);
                if (refGame == null)
                {
                    return null;
                }

                var seedGames = new List<Playnite.SDK.Models.Game>();

                // Recommended / For You should be based on the first 3 reference games,
                // not only the single sticky RefGame of the day.
                // Put refTop3 first so the three strongest recent/reference games drive the profile.
                if (refTop3 != null)
                {
                    foreach (var game in refTop3.Take(3))
                    {
                        AddRecommendationSeedGame(seedGames, game);
                    }
                }

                // Keep the sticky ref game as fallback, or as a 4th seed if it was not part of top3.
                AddRecommendationSeedGame(seedGames, refGame);

                if (seedGames.Count == 0)
                {
                    return null;
                }

                var profileSeeds = seedGames.Take(3).ToList();

                var genres = new List<string>();
                var tags = new List<string>();
                var developers = new List<string>();
                var publishers = new List<string>();

                foreach (var game in profileSeeds)
                {
                    genres.AddRange(GetGenreNames(game));
                    tags.AddRange(GetTagNames(game).Where(IsUsefulProfileTag));
                    developers.AddRange(GetDeveloperNames(game));
                    publishers.AddRange(GetPublisherNames(game));
                }

                var referenceNames = profileSeeds
                    .Select(g => Safe(g?.Name))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var profile = new SteamRecommendationProfile
                {
                    ReferenceName = referenceNames.FirstOrDefault() ?? Safe(refGame.Name),
                    ReferenceNames = referenceNames,
                    Genres = GetSpecificKeywords(genres)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(14)
                        .ToList(),
                    Tags = GetSpecificKeywords(tags)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(18)
                        .ToList(),
                    Developers = developers
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(6)
                        .ToList(),
                    Publishers = publishers
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(6)
                        .ToList(),
                    OwnedSteamAppIds = GetOwnedSteamAppIds(games),
                    OwnedNormalizedNames = GetOwnedNormalizedGameNames(games),
                    SeedSteamAppIds = GetRecommendationSeedAppIds(profileSeeds),
                    SeedSourceNames = referenceNames
                };

                // Real buy recommendations: keep each of the Top 3 games as a separate weighted signal.
                // This avoids one vague tag like "story rich" or "point and click" taking over the whole profile.
                profile.SearchTermWeights = BuildSteamRecommendationTermWeights(profileSeeds);
                profile.FamilyWeights = BuildSteamRecommendationFamilyWeights(profileSeeds);
                profile.PreferredFamilies = profile.FamilyWeights
                    .OrderByDescending(x => x.Value)
                    .Select(x => x.Key)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(4)
                    .ToList();
                profile.Family = SelectDominantRecommendationFamily(profile.FamilyWeights);

                profile.SearchTerms = BuildSteamRecommendationSearchTerms(profile);
                if (profile.SearchTerms.Count == 0 && (profile.SeedSteamAppIds == null || profile.SeedSteamAppIds.Count == 0))
                {
                    return null;
                }

                profile.CacheKey = BuildSteamRecommendationCacheKey(profile);

                var profileLogKey = profile.CacheKey + "|" + string.Join(",", profile.SeedSteamAppIds ?? new List<int>());
                if (!string.Equals(lastLoggedSteamRecommendationProfileKey, profileLogKey, StringComparison.OrdinalIgnoreCase))
                {
                    lastLoggedSteamRecommendationProfileKey = profileLogKey;
                    // Quiet by default: this profile can be rebuilt several times by Hub/Store bindings.
                }

                return profile;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to build Steam recommendation profile.");
                return null;
            }
        }


        private List<int> GetRecommendationSeedAppIds(IEnumerable<Playnite.SDK.Models.Game> seedGames)
        {
            var result = new List<int>();

            foreach (var game in seedGames ?? Enumerable.Empty<Playnite.SDK.Models.Game>())
            {
                var appId = TryGetKnownSteamAppId(game);
                if (appId > 0 && !result.Contains(appId))
                {
                    result.Add(appId);
                }
            }

            return result;
        }

        private int TryGetKnownSteamAppId(Playnite.SDK.Models.Game game)
        {
            if (game == null)
            {
                return 0;
            }

            try
            {
                var direct = GetSteamGameId(game);
                if (int.TryParse(direct, out var directAppId) && directAppId > 0)
                {
                    return directAppId;
                }

                var cached = GetCachedSteamAppId(game);
                if (int.TryParse(cached, out var cachedAppId) && cachedAppId > 0)
                {
                    return cachedAppId;
                }
            }
            catch
            {
            }

            return 0;
        }

        private string GetCachedSteamAppId(Playnite.SDK.Models.Game game)
        {
            if (game == null)
            {
                return null;
            }

            try
            {
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
            }
            catch
            {
            }

            return null;
        }

        private void AddRecommendationSeedGame(List<Playnite.SDK.Models.Game> seedGames, Playnite.SDK.Models.Game game)
        {
            if (seedGames == null || game == null)
            {
                return;
            }

            if (seedGames.Any(x => x != null && x.Id == game.Id))
            {
                return;
            }

            seedGames.Add(game);
        }

        private Dictionary<string, int> BuildSteamRecommendationTermWeights(List<Playnite.SDK.Models.Game> seedGames)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (seedGames == null || seedGames.Count == 0)
            {
                return result;
            }

            var weights = new[] { 100, 70, 55 };

            for (var i = 0; i < seedGames.Count && i < 3; i++)
            {
                var game = seedGames[i];
                if (game == null)
                {
                    continue;
                }

                var weight = weights[Math.Min(i, weights.Length - 1)];
                foreach (var term in BuildSteamRecommendationTermsForSeedGame(game))
                {
                    AddWeightedSteamRecommendationTerm(result, term, weight);
                }
            }

            // If action signals are present, do not let point & click dominate the whole profile.
            var hasAction = result.Keys.Any(IsActionRecommendationTerm);
            if (hasAction && result.ContainsKey("point and click"))
            {
                result["point and click"] = Math.Min(result["point and click"], 35);
            }

            return result;
        }

        private Dictionary<string, int> BuildSteamRecommendationFamilyWeights(List<Playnite.SDK.Models.Game> seedGames)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (seedGames == null || seedGames.Count == 0)
            {
                return result;
            }

            var weights = new[] { 100, 70, 55 };

            for (var i = 0; i < seedGames.Count && i < 3; i++)
            {
                var game = seedGames[i];
                if (game == null)
                {
                    continue;
                }

                var family = DetectRecommendationSeedFamily(game);
                if (string.IsNullOrWhiteSpace(family) || string.Equals(family, "generic", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var weight = weights[Math.Min(i, weights.Length - 1)];
                if (!result.ContainsKey(family))
                {
                    result[family] = 0;
                }

                result[family] += weight;
            }

            return result;
        }

        private string SelectDominantRecommendationFamily(Dictionary<string, int> familyWeights)
        {
            if (familyWeights == null || familyWeights.Count == 0)
            {
                return "generic";
            }

            var ordered = familyWeights
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .OrderByDescending(x => x.Value)
                .ToList();

            if (ordered.Count == 0)
            {
                return "generic";
            }

            if (ordered.Count == 1)
            {
                return ordered[0].Key;
            }

            // If the top family is clearly dominant, use it. Otherwise keep it mixed to avoid hard tunnel vision.
            if (ordered[0].Value >= ordered[1].Value * 1.45)
            {
                return ordered[0].Key;
            }

            return "mixed";
        }

        private List<string> BuildSteamRecommendationTermsForSeedGame(Playnite.SDK.Models.Game game)
        {
            var result = new List<string>();
            if (game == null)
            {
                return result;
            }

            var metadataValues = new List<string>();
            metadataValues.AddRange(GetGenreNames(game));
            metadataValues.AddRange(GetTagNames(game).Where(IsUsefulProfileTag));

            var values = new List<string>();
            values.Add(Safe(game.Name));
            values.AddRange(metadataValues);

            var text = NormalizeTextForGenre(string.Join(" | ", values));

            var hasActionAdventure = ContainsAnyRecommendationSignal(text,
                "action et aventure", "action aventure", "action adventure", "action-aventure");
            var hasThirdPerson = ContainsAnyRecommendationSignal(text,
                "3e personne", "3eme personne", "3ᵉ personne", "third person", "3rd person", "tps");
            var hasStealth = ContainsAnyRecommendationSignal(text,
                "infiltration", "stealth", "furtif", "assassinats", "assassination");
            var hasShooter = ContainsAnyRecommendationSignal(text,
                "tir", "shooter", "fps", "gun", "armes", "third person shooter");
            var hasCombat = ContainsAnyRecommendationSignal(text,
                "combat", "melee", "mêlée", "sword", "epee", "épée", "katana", "samurai", "onimusha");
            var hasHackSlash = ContainsAnyRecommendationSignal(text,
                "hack and slash", "hack n slash", "hack'n'slash", "samurai", "onimusha");
            var hasCinematic = ContainsAnyRecommendationSignal(text,
                "cinematic", "cinematique", "cinematographique", "cinématique", "cinématographique");
            var hasStory = ContainsAnyRecommendationSignal(text,
                "story rich", "scenario riche", "scénario riche", "narrative", "narratif", "riche en histoire");
            var hasChoices = ContainsAnyRecommendationSignal(text,
                "choices matter", "choix multiples", "choix");
            var hasPointClick = ContainsAnyRecommendationSignal(text,
                "point and click", "point & click", "pointer et cliquer");
            var hasInteractive = ContainsAnyRecommendationSignal(text,
                "interactive fiction", "fiction interactive", "aventure interactive");
            var hasDark = ContainsAnyRecommendationSignal(text,
                "sombre", "dark", "dark fantasy");

            if (hasActionAdventure)
            {
                result.Add("action adventure");
            }

            if (hasThirdPerson && (hasActionAdventure || hasShooter || hasCombat))
            {
                result.Add(hasShooter ? "third person shooter" : "third person action");
            }
            else if (hasThirdPerson)
            {
                result.Add("third person");
            }

            if (hasStealth)
            {
                result.Add("stealth action");
            }

            if (hasShooter)
            {
                result.Add(hasThirdPerson ? "third person shooter" : "shooter");
            }

            if (hasCombat)
            {
                result.Add("action combat");
            }

            if (hasHackSlash)
            {
                result.Add("hack and slash");
            }

            if (hasCinematic && (hasActionAdventure || hasCombat || hasShooter || hasStealth))
            {
                result.Add("cinematic action");
            }
            else if (hasCinematic)
            {
                result.Add("story rich");
            }

            if (hasDark && (hasCombat || hasHackSlash))
            {
                result.Add("dark fantasy");
            }

            if (hasStory)
            {
                result.Add("story rich");
            }

            if (hasChoices)
            {
                result.Add("choices matter");
            }

            if (hasInteractive)
            {
                result.Add("interactive fiction");
            }

            // Point & click is useful only as a support signal. It should not dominate action/cinematic profiles.
            if (hasPointClick && !hasActionAdventure && !hasShooter && !hasCombat)
            {
                result.Add("point and click");
            }

            foreach (var value in metadataValues)
            {
                var term = CanonicalizeSteamRecommendationSearchTerm(value);
                if (!string.IsNullOrWhiteSpace(term) && ShouldUseSteamRecommendationSearchTerm(term))
                {
                    result.Add(term);
                }
            }

            return result
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private string DetectRecommendationSeedFamily(Playnite.SDK.Models.Game game)
        {
            if (game == null)
            {
                return "generic";
            }

            var values = new List<string>();
            values.Add(Safe(game.Name));
            values.AddRange(GetGenreNames(game));
            values.AddRange(GetTagNames(game).Where(IsUsefulProfileTag));

            return DetectRecommendationSignalFamily(values);
        }

        private string DetectRecommendationSignalFamily(IEnumerable<string> values)
        {
            var text = NormalizeTextForGenre(string.Join(" | ", values ?? Enumerable.Empty<string>()));
            if (string.IsNullOrWhiteSpace(text))
            {
                return "generic";
            }

            if (ContainsAnyRecommendationSignal(text, "soulslike", "souls-like", "dark souls", "elden ring"))
                return "souls";
            if (ContainsAnyRecommendationSignal(text, "jrpg", "j-rpg", "tour par tour", "turn based"))
                return "jrpg";
            if (ContainsAnyRecommendationSignal(text, "survival horror", "psychological horror", "horreur"))
                return "horror";
            if (ContainsAnyRecommendationSignal(text, "action et aventure", "action aventure", "action adventure", "3e personne", "3eme personne", "third person", "infiltration", "stealth", "assassinats", "combat", "hack and slash", "onimusha", "samurai"))
                return "action_adventure";
            if (ContainsAnyRecommendationSignal(text, "shooter", "fps", "tps", "tir", "gun"))
                return "shooter";
            if (ContainsAnyRecommendationSignal(text, "story rich", "scenario riche", "scénario riche", "choices matter", "choix multiples", "point and click", "interactive fiction", "aventure interactive", "visual novel", "roman graphique"))
                return "narrative";
            if (ContainsAnyRecommendationSignal(text, "rogue", "roguelite", "roguelike"))
                return "roguelite";
            if (ContainsAnyRecommendationSignal(text, "platformer", "plateforme", "metroidvania"))
                return "platformer";
            if (ContainsAnyRecommendationSignal(text, "racing", "course", "voiture"))
                return "racing";
            if (ContainsAnyRecommendationSignal(text, "sport", "football", "basket"))
                return "sport";

            return "generic";
        }

        private bool ContainsAnyRecommendationSignal(string normalizedText, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(normalizedText) || values == null)
            {
                return false;
            }

            foreach (var value in values)
            {
                var n = NormalizeTextForGenre(value);
                if (!string.IsNullOrWhiteSpace(n) && normalizedText.Contains(n))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddWeightedSteamRecommendationTerm(Dictionary<string, int> weights, string value, int weight)
        {
            if (weights == null || string.IsNullOrWhiteSpace(value) || weight <= 0)
            {
                return;
            }

            var term = CanonicalizeSteamRecommendationSearchTerm(value);
            if (string.IsNullOrWhiteSpace(term) || !ShouldUseSteamRecommendationSearchTerm(term))
            {
                return;
            }

            if (!weights.ContainsKey(term))
            {
                weights[term] = 0;
            }

            weights[term] += weight;
        }

        private bool IsActionRecommendationTerm(string term)
        {
            var n = NormalizeTextForGenre(term);
            return n == "action adventure" ||
                   n == "third person action" ||
                   n == "third person shooter" ||
                   n == "stealth action" ||
                   n == "cinematic action" ||
                   n == "action combat" ||
                   n == "hack and slash" ||
                   n == "combat" ||
                   n == "shooter" ||
                   n == "dark fantasy";
        }

        private string GetRecommendationTermBucket(string term)
        {
            var n = NormalizeTextForGenre(term);
            if (string.IsNullOrWhiteSpace(n))
            {
                return "generic";
            }

            if (n == "point and click") return "point_click";
            if (n == "story rich" || n == "choices matter" || n == "interactive fiction" || n == "visual novel") return "narrative";
            if (IsActionRecommendationTerm(n)) return "action";
            if (n.Contains("horror")) return "horror";
            if (n.Contains("racing") || n.Contains("driving")) return "racing";
            if (n.Contains("sport")) return "sport";
            if (n.Contains("rogue")) return "roguelite";
            if (n.Contains("platformer") || n.Contains("metroidvania")) return "platformer";

            return n;
        }

        private List<string> BuildSteamRecommendationSearchTerms(SteamRecommendationProfile profile)
        {
            var terms = new List<string>();
            if (profile == null)
            {
                return terms;
            }

            var family = (profile.Family ?? string.Empty).ToLowerInvariant();

            // Prefer weighted terms built independently from each of the Top 3 recent games.
            // Example: 007 + Onimusha + Dispatch should produce action/stealth/combat/story terms,
            // not collapse into one global "narrative" profile.
            if (profile.SearchTermWeights != null && profile.SearchTermWeights.Count > 0)
            {
                var hasActionSignals = profile.SearchTermWeights.Keys.Any(IsActionRecommendationTerm);
                var narrativeCount = 0;

                foreach (var entry in profile.SearchTermWeights
                    .Where(x => ShouldUseSteamRecommendationSearchTerm(x.Key))
                    .OrderByDescending(x => x.Value)
                    .ThenByDescending(x => GetRecommendationSearchTermPriority(x.Key, family))
                    .ThenBy(x => x.Key))
                {
                    var term = entry.Key;
                    var bucket = GetRecommendationTermBucket(term);

                    // If action/stealth/combat signals exist, keep narrative terms as support, not as the whole search.
                    if (hasActionSignals && string.Equals(bucket, "narrative", StringComparison.OrdinalIgnoreCase))
                    {
                        narrativeCount++;
                        if (narrativeCount > 2)
                        {
                            continue;
                        }
                    }

                    AddSteamRecommendationSearchTerm(terms, term);
                    if (terms.Count >= 9)
                    {
                        break;
                    }
                }
            }

            if (terms.Count == 0)
            {
                switch (family)
                {
                    case "action_adventure":
                        AddSteamRecommendationSearchTerm(terms, "action adventure");
                        AddSteamRecommendationSearchTerm(terms, "third person action");
                        AddSteamRecommendationSearchTerm(terms, "stealth action");
                        AddSteamRecommendationSearchTerm(terms, "cinematic action");
                        break;

                    case "souls":
                        AddSteamRecommendationSearchTerm(terms, "soulslike");
                        AddSteamRecommendationSearchTerm(terms, "dark fantasy");
                        AddSteamRecommendationSearchTerm(terms, "action rpg");
                        break;

                    case "jrpg":
                        AddSteamRecommendationSearchTerm(terms, "jrpg");
                        AddSteamRecommendationSearchTerm(terms, "turn based rpg");
                        AddSteamRecommendationSearchTerm(terms, "japanese rpg");
                        break;

                    case "narrative":
                        AddSteamRecommendationSearchTerm(terms, "story rich");
                        AddSteamRecommendationSearchTerm(terms, "choices matter");
                        AddSteamRecommendationSearchTerm(terms, "interactive fiction");
                        AddSteamRecommendationSearchTerm(terms, "point and click");
                        break;

                    case "horror":
                        AddSteamRecommendationSearchTerm(terms, "survival horror");
                        AddSteamRecommendationSearchTerm(terms, "psychological horror");
                        break;

                    case "shooter":
                        AddSteamRecommendationSearchTerm(terms, "third person shooter");
                        AddSteamRecommendationSearchTerm(terms, "fps");
                        AddSteamRecommendationSearchTerm(terms, "shooter");
                        break;

                    case "roguelite":
                        AddSteamRecommendationSearchTerm(terms, "roguelite");
                        AddSteamRecommendationSearchTerm(terms, "roguelike");
                        break;

                    case "platformer":
                        AddSteamRecommendationSearchTerm(terms, "metroidvania");
                        AddSteamRecommendationSearchTerm(terms, "platformer");
                        break;

                    case "racing":
                        AddSteamRecommendationSearchTerm(terms, "racing");
                        AddSteamRecommendationSearchTerm(terms, "driving");
                        break;

                    case "fighting":
                    case "anime_fight":
                        AddSteamRecommendationSearchTerm(terms, "fighting");
                        AddSteamRecommendationSearchTerm(terms, "versus fighting");
                        break;

                    case "sport":
                        AddSteamRecommendationSearchTerm(terms, "sports");
                        break;
                }
            }

            var candidates = new List<string>();

            foreach (var tag in profile.Tags ?? new List<string>())
            {
                AddSteamRecommendationSearchTerm(candidates, tag);
            }

            foreach (var genre in profile.Genres ?? new List<string>())
            {
                AddSteamRecommendationSearchTerm(candidates, genre);
            }

            var orderedCandidates = candidates
                .Where(ShouldUseSteamRecommendationSearchTerm)
                .OrderByDescending(x => profile.SearchTermWeights != null && profile.SearchTermWeights.ContainsKey(x) ? profile.SearchTermWeights[x] : 0)
                .ThenByDescending(x => GetRecommendationSearchTermPriority(x, family))
                .ThenBy(x => candidates.IndexOf(x))
                .ToList();

            foreach (var candidate in orderedCandidates)
            {
                AddSteamRecommendationSearchTerm(terms, candidate);
                if (terms.Count >= 9)
                {
                    break;
                }
            }

            // Developer search is only a last fallback. Otherwise small studio names often return weak/noisy matches.
            if (terms.Count == 0)
            {
                foreach (var developer in (profile.Developers ?? new List<string>()).Take(1))
                {
                    AddSteamRecommendationSearchTerm(terms, developer);
                }
            }

            if (terms.Count == 0)
            {
                AddSteamRecommendationSearchTerm(terms, profile.ReferenceName);
            }

            return terms
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(9)
                .ToList();
        }

        private void AddSteamRecommendationSearchTerm(List<string> terms, string value)
        {
            if (terms == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var term = CanonicalizeSteamRecommendationSearchTerm(value);
            if (string.IsNullOrWhiteSpace(term))
            {
                return;
            }

            if (term.Length < 3 || term.Length > 60)
            {
                return;
            }

            if (GenericGenreTagNames.Contains(term))
            {
                return;
            }

            if (term.StartsWith("[", StringComparison.OrdinalIgnoreCase) || term.Contains(":"))
            {
                return;
            }

            if (!ShouldUseSteamRecommendationSearchTerm(term))
            {
                return;
            }

            if (!terms.Any(x => string.Equals(x, term, StringComparison.OrdinalIgnoreCase)))
            {
                terms.Add(term);
            }
        }

        private string CanonicalizeSteamRecommendationSearchTerm(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var raw = value.Trim();
            var n = NormalizeTextForGenre(raw);

            if (string.IsNullOrWhiteSpace(n))
            {
                return null;
            }

            if (n.Contains("third person shooter") ||
                n.Contains("tir a la 3e personne") ||
                n.Contains("tir a la 3eme personne") ||
                n.Contains("tir a la 3ᵉ personne"))
            {
                return "third person shooter";
            }

            if (n.Contains("third person action") ||
                n.Contains("3e personne") ||
                n.Contains("3eme personne") ||
                n.Contains("3ᵉ personne") ||
                n.Contains("third person") ||
                n.Contains("3rd person") ||
                n.Contains("tps"))
            {
                return "third person action";
            }

            if (n.Contains("stealth action") || n.Contains("infiltration") || n.Contains("stealth") || n.Contains("furtif") || n.Contains("assassinat"))
            {
                return "stealth action";
            }

            if (n.Contains("action adventure") || n.Contains("action et aventure") || n.Contains("action aventure") || n.Contains("action-aventure"))
            {
                return "action adventure";
            }

            if (n.Contains("cinematic action") || n.Contains("cinematique action") || n.Contains("cinematographique action"))
            {
                return "cinematic action";
            }

            if (n.Contains("action combat") || n == "combat")
            {
                return "action combat";
            }

            if (n.Contains("third person shooter") || n == "shooter" || n == "tir")
            {
                return "shooter";
            }

            // Metadata that is too vague or harmful for recommendations.
            if (n == "atmosphere" ||
                n == "atmospheric" ||
                n == "atmosphere riche" ||
                n == "ambiance" ||
                n == "singleplayer" ||
                n == "single player" ||
                n == "single-player" ||
                n == "jeu solo" ||
                n == "nudity" ||
                n == "nudite" ||
                n == "contenu a caractere sexuel" ||
                n == "sexual content" ||
                n == "adult content" ||
                n == "action" ||
                n == "aventure" ||
                n == "adventure" ||
                n == "casual" ||
                n == "occasionnel" ||
                n == "comic" ||
                n == "comic book" ||
                n == "bande dessinee" ||
                n == "bd" ||
                n == "serie" ||
                n == "série")
            {
                return null;
            }

            if (n == "cinematic" || n == "cinematique" || n == "cinematographique")
            {
                return "story rich";
            }

            if (n == "multiple choices" || n == "choix multiples" || n.Contains("choices matter") || n.Contains("choix"))
            {
                return "choices matter";
            }

            if (n.Contains("story rich") || n.Contains("scenario riche") || n.Contains("scénario riche") || n.Contains("riche en histoire") || n.Contains("narratif") || n.Contains("narrative"))
            {
                return "story rich";
            }

            if (n.Contains("point and click") || n.Contains("pointer et cliquer") || n.Contains("point & click"))
            {
                return "point and click";
            }

            if (n.Contains("interactive fiction") || n.Contains("fiction interactive") || n.Contains("interactive adventure") || n.Contains("aventure interactive"))
            {
                return "interactive fiction";
            }

            if (n.Contains("visual novel"))
            {
                return "visual novel";
            }

            if (n.Contains("graphic novel") || n.Contains("roman graphique"))
            {
                return "story rich";
            }

            if (n.Contains("superhero") || n.Contains("super heros") || n.Contains("super héros") || n.Contains("superheros") || n.Contains("superhéro"))
            {
                return "superhero";
            }

            if (n.Contains("humor") || n.Contains("humour") || n.Contains("comedy") || n.Contains("comedie"))
            {
                return "comedy";
            }

            if (n.Contains("romance") || n.Contains("romantique"))
            {
                return "romance";
            }

            if (n.Contains("soulslike") || n.Contains("souls-like") || n.Contains("souls like"))
            {
                return "soulslike";
            }

            if (n.Contains("dark fantasy") || n.Contains("fantasy sombre"))
            {
                return "dark fantasy";
            }

            if (n.Contains("survival horror") || n.Contains("horreur survie"))
            {
                return "survival horror";
            }

            if (n.Contains("psychological horror") || n.Contains("horreur psychologique"))
            {
                return "psychological horror";
            }

            if (n.Contains("roguelite") || n.Contains("rogue-lite"))
            {
                return "roguelite";
            }

            if (n.Contains("roguelike") || n.Contains("rogue-like"))
            {
                return "roguelike";
            }

            if (n.Contains("metroidvania"))
            {
                return "metroidvania";
            }

            if (n.Contains("turn based") || n.Contains("tour par tour"))
            {
                return "turn based";
            }

            if (n.Contains("hack and slash") || n.Contains("hack'n'slash") || n.Contains("hack n slash"))
            {
                return "hack and slash";
            }

            return raw;
        }


        private bool ShouldUseSteamRecommendationSearchTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return false;
            }

            var n = NormalizeTextForGenre(term);
            if (string.IsNullOrWhiteSpace(n))
            {
                return false;
            }

            string[] badSearchTerms =
            {
                "comic book",
                "graphic novel",
                "art book",
                "artbook",
                "comedy",
                "humor",
                "humour",
                "romance",
                "singleplayer",
                "single player",
                "jeu solo",
                "nudity",
                "sexual content",
                "demo",
                "prologue",
                "dlc",
                "bundle",
                "pack",
                "creator",
                "kit",
                "tool",
                "controller",
                "controleur",
                "contrôleur",
                "3d",
                "atmosphere",
                "atmospheric",
                "ambiance"
            };

            return !badSearchTerms.Any(x => string.Equals(n, x, StringComparison.OrdinalIgnoreCase));
        }

        private int GetRecommendationSearchTermPriority(string term, string family)
        {
            var n = NormalizeTextForGenre(term);

            if (string.IsNullOrWhiteSpace(n))
            {
                return 0;
            }

            if (n == "soulslike") return 120;
            if (n == "action adventure" || n == "third person action" || n == "third person shooter") return 118;
            if (n == "stealth action" || n == "cinematic action" || n == "action combat") return 116;
            if (n == "survival horror" || n == "psychological horror") return 115;
            if (n == "hack and slash") return 112;
            if (n == "jrpg" || n == "turn based rpg") return 110;
            if (n == "story rich") return 105;
            if (n == "choices matter") return 100;
            if (n == "point and click") return 95;
            if (n == "interactive fiction") return 90;
            if (n == "visual novel") return 88;
            if (n == "superhero") return 84;
            if (n == "dark fantasy" || n == "action rpg") return 80;
            if (n == "metroidvania" || n == "roguelite" || n == "roguelike") return 78;
            if (n == "fps" || n == "shooter") return 74;
            if (n == "racing" || n == "sports") return 70;

            return string.Equals(family, "generic", StringComparison.OrdinalIgnoreCase) ? 35 : 50;
        }

        private bool IsWeakRecommendationScoreTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return true;
            }

            var n = NormalizeTextForGenre(term);
            if (string.IsNullOrWhiteSpace(n))
            {
                return true;
            }

            string[] weakTerms =
            {
                "comic book",
                "graphic novel",
                "comedy",
                "humor",
                "humour",
                "romance",
                "singleplayer",
                "single player",
                "jeu solo",
                "atmosphere",
                "atmospheric",
                "ambiance",
                "point and click",
                "controller",
                "controleur",
                "contrôleur",
                "3d"
            };

            return weakTerms.Any(x => string.Equals(n, x, StringComparison.OrdinalIgnoreCase));
        }

        private string BuildSteamRecommendationCacheKey(SteamRecommendationProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    return "default";
                }

                var raw = string.Join("|", new[]
                {
                    string.Join(",", profile.ReferenceNames ?? new List<string>()),
                    profile.ReferenceName ?? string.Empty,
                    profile.Family ?? string.Empty,
                    string.Join(",", profile.PreferredFamilies ?? new List<string>()),
                    string.Join(",", profile.SearchTerms ?? new List<string>()),
                    string.Join(",", profile.SeedSteamAppIds ?? new List<int>()),
                    string.Join(",", (profile.SearchTermWeights ?? new Dictionary<string, int>()).OrderByDescending(x => x.Value).Select(x => x.Key + ":" + x.Value.ToString(CultureInfo.InvariantCulture)))
                });

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return "default";
                }

                using (var sha1 = SHA1.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(raw);
                    var hash = sha1.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant().Substring(0, 16);
                }
            }
            catch
            {
                return "default";
            }
        }

        private static bool IsExternalWishlistOnlySource(Playnite.SDK.Models.Game game)
        {
            try
            {
                var sourceName = game?.Source?.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    return false;
                }

                sourceName = sourceName.Trim();
                return string.Equals(sourceName, "GG.deals Wishlist", StringComparison.OrdinalIgnoreCase) ||
                       (sourceName.IndexOf("gg.deals", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        sourceName.IndexOf("wishlist", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch
            {
                return false;
            }
        }

        private HashSet<int> GetOwnedSteamAppIds(IEnumerable<Playnite.SDK.Models.Game> games)
        {
            var result = new HashSet<int>();

            if (games == null)
            {
                return result;
            }

            foreach (var game in games)
            {
                try
                {
                    // Games imported only as an external wishlist are visible in Playnite,
                    // but they should not be treated as owned/in-library Store items.
                    if (IsExternalWishlistOnlySource(game))
                    {
                        continue;
                    }

                    var appId = TryGetKnownSteamAppId(game);
                    if (appId > 0)
                    {
                        result.Add(appId);
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        private HashSet<string> GetOwnedNormalizedGameNames(IEnumerable<Playnite.SDK.Models.Game> games)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (games == null)
            {
                return result;
            }

            foreach (var game in games)
            {
                try
                {
                    // Same rule as AppIds: GG.deals Wishlist entries are wishlist-only,
                    // not real owned library entries.
                    if (IsExternalWishlistOnlySource(game))
                    {
                        continue;
                    }

                    var normalized = NormalizeSteamSearchName(game?.Name);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        result.Add(normalized);
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        private HashSet<int> GetPlayedSteamAppIds(IEnumerable<Playnite.SDK.Models.Game> games)
        {
            var result = new HashSet<int>();

            if (games == null)
            {
                return result;
            }

            foreach (var game in games)
            {
                try
                {
                    if (game == null || game.Playtime == 0UL)
                    {
                        continue;
                    }

                    var appId = TryGetKnownSteamAppId(game);
                    if (appId > 0)
                    {
                        result.Add(appId);
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        private HashSet<string> GetPlayedNormalizedGameNames(IEnumerable<Playnite.SDK.Models.Game> games)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (games == null)
            {
                return result;
            }

            foreach (var game in games)
            {
                try
                {
                    if (game == null || game.Playtime == 0UL)
                    {
                        continue;
                    }

                    var normalized = NormalizeSteamSearchName(game.Name);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        result.Add(normalized);
                    }
                }
                catch
                {
                }
            }

            return result;
        }


        private List<SteamStoreItem> BuildRankedSteamRecommendations(IEnumerable<SteamStoreItem> source, SteamRecommendationProfile profile)
        {
            var ranked = new List<SteamRecommendationRankedCandidate>();

            if (source == null || profile == null)
            {
                return new List<SteamStoreItem>();
            }

            foreach (var item in source.Where(x => x != null))
            {
                if (item.AppId <= 0 || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                if (profile.OwnedSteamAppIds != null && profile.OwnedSteamAppIds.Contains(item.AppId))
                {
                    continue;
                }

                var normalizedName = NormalizeSteamSearchName(item.Name);
                if (!string.IsNullOrWhiteSpace(normalizedName) &&
                    profile.OwnedNormalizedNames != null &&
                    profile.OwnedNormalizedNames.Contains(normalizedName))
                {
                    continue;
                }

                if (LooksBlockedSteamRecommendation(item))
                {
                    continue;
                }

                var score = ScoreSteamRecommendation(item, profile);
                var minScore = string.Equals(profile.Family, "generic", StringComparison.OrdinalIgnoreCase)
                    ? 55
                    : 65;

                if (score < minScore)
                {
                    logger.Info($"[Recommended] Skip low score | score={score} < {minScore} | {item.Name} | reasons={BuildSteamRecommendationReason(item, profile, score)}");
                    continue;
                }

                item.Source = "Steam Recommended For You";

                ranked.Add(new SteamRecommendationRankedCandidate
                {
                    Item = item,
                    Score = score
                });
            }

            var ordered = ranked
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item?.SteamRank <= 0 ? int.MaxValue : x.Item.SteamRank)
                .ThenByDescending(x => x.Item?.RecommendationsTotal ?? 0)
                .ThenByDescending(x => x.Item?.DiscountPercent ?? 0)
                .ThenBy(x => x.Item?.Name ?? string.Empty)
                .ToList();

            var selected = DiversifySteamRecommendationResults(ordered, profile, 12);

            var rank = 1;
            foreach (var candidate in selected)
            {
                logger.Info(
                    $"[Recommended] Result #{rank} | score={candidate.Score} | {candidate.Item?.Name} | reasons={BuildSteamRecommendationReason(candidate.Item, profile, candidate.Score)}"
                );
                rank++;
            }

            return selected
                .Select(x => x.Item)
                .Where(x => x != null)
                .ToList();
        }

        private List<SteamRecommendationRankedCandidate> DiversifySteamRecommendationResults(List<SteamRecommendationRankedCandidate> ordered, SteamRecommendationProfile profile, int maxResults)
        {
            var selected = new List<SteamRecommendationRankedCandidate>();
            var bucketCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var selectedAppIds = new HashSet<int>();

            foreach (var candidate in ordered ?? new List<SteamRecommendationRankedCandidate>())
            {
                if (candidate?.Item == null || selectedAppIds.Contains(candidate.Item.AppId))
                {
                    continue;
                }

                var bucket = GetSteamRecommendationCandidateBucket(candidate.Item, profile);
                var maxForBucket = string.Equals(bucket, "point_click", StringComparison.OrdinalIgnoreCase) ? 1 : 3;

                if (!bucketCounts.ContainsKey(bucket))
                {
                    bucketCounts[bucket] = 0;
                }

                if (bucketCounts[bucket] >= maxForBucket)
                {
                    continue;
                }

                selected.Add(candidate);
                selectedAppIds.Add(candidate.Item.AppId);
                bucketCounts[bucket]++;

                if (selected.Count >= maxResults)
                {
                    return selected;
                }
            }

            // Fill with remaining strong candidates if diversity caps made the list too short.
            foreach (var candidate in ordered ?? new List<SteamRecommendationRankedCandidate>())
            {
                if (candidate?.Item == null || selectedAppIds.Contains(candidate.Item.AppId))
                {
                    continue;
                }

                selected.Add(candidate);
                selectedAppIds.Add(candidate.Item.AppId);

                if (selected.Count >= maxResults)
                {
                    break;
                }
            }

            return selected;
        }

        private string GetSteamRecommendationCandidateBucket(SteamStoreItem item, SteamRecommendationProfile profile)
        {
            if (item == null || profile == null)
            {
                return "generic";
            }

            var itemTextParts = BuildSteamRecommendationTextParts(item);
            var matchedTerm = (profile.SearchTerms ?? new List<string>())
                .Where(x => SteamRecommendationContainsKeyword(itemTextParts, x))
                .OrderByDescending(x => GetRecommendationTermWeight(profile, x))
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(matchedTerm))
            {
                return GetRecommendationTermBucket(matchedTerm);
            }

            return DetectRecommendationSignalFamily(itemTextParts);
        }

        private List<string> BuildSteamRecommendationTextParts(SteamStoreItem item)
        {
            var result = new List<string>();
            if (item == null)
            {
                return result;
            }

            result.Add(item.Name ?? string.Empty);
            result.Add(item.ShortDescription ?? string.Empty);
            result.AddRange(item.Genres ?? new List<string>());
            result.AddRange(item.Categories ?? new List<string>());
            result.AddRange(item.Tags ?? new List<string>());
            result.AddRange(item.Developers ?? new List<string>());
            result.AddRange(item.Publishers ?? new List<string>());
            return result;
        }

        private int GetRecommendationTermWeight(SteamRecommendationProfile profile, string term)
        {
            if (profile?.SearchTermWeights != null && !string.IsNullOrWhiteSpace(term) && profile.SearchTermWeights.ContainsKey(term))
            {
                return profile.SearchTermWeights[term];
            }

            return GetRecommendationSearchTermPriority(term, profile?.Family ?? string.Empty);
        }

        private int CountStrongRecommendationTermMatches(SteamStoreItem item, SteamRecommendationProfile profile)
        {
            if (item == null || profile == null)
            {
                return 0;
            }

            var itemTextParts = BuildSteamRecommendationTextParts(item);
            var count = 0;

            foreach (var term in profile.SearchTerms ?? new List<string>())
            {
                if (IsWeakRecommendationScoreTerm(term))
                {
                    continue;
                }

                if (SteamRecommendationContainsKeyword(itemTextParts, term))
                {
                    count++;
                }
            }

            return count;
        }

        private string BuildSteamRecommendationReason(SteamStoreItem item, SteamRecommendationProfile profile, int score)
        {
            if (item == null || profile == null)
            {
                return "none";
            }

            var reasons = new List<string>();
            var itemGenres = (item.Genres ?? new List<string>())
                .Concat(item.Categories ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var itemTextParts = new List<string>();
            itemTextParts.Add(item.Name ?? string.Empty);
            itemTextParts.Add(item.ShortDescription ?? string.Empty);
            itemTextParts.AddRange(itemGenres);
            itemTextParts.AddRange(item.Tags ?? new List<string>());
            itemTextParts.AddRange(item.Developers ?? new List<string>());
            itemTextParts.AddRange(item.Publishers ?? new List<string>());

            var itemFamily = DetectFamily(
                itemGenres
                    .Concat(item.Tags ?? new List<string>())
                    .Concat(new[] { item.Name ?? string.Empty, item.ShortDescription ?? string.Empty }),
                null
            );

            reasons.Add("score=" + score.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(profile.Family) && !string.IsNullOrWhiteSpace(itemFamily))
            {
                reasons.Add("family=" + profile.Family + "/" + itemFamily);
            }

            var sharedGenres = (profile.Genres ?? new List<string>())
                .Intersect(itemGenres, StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
            if (sharedGenres.Count > 0)
            {
                reasons.Add("genres=" + string.Join(", ", sharedGenres));
            }

            var matchedTags = (profile.Tags ?? new List<string>())
                .Where(x => SteamRecommendationContainsKeyword(itemTextParts, x))
                .Take(4)
                .ToList();
            if (matchedTags.Count > 0)
            {
                reasons.Add("tags=" + string.Join(", ", matchedTags));
            }

            var matchedTerms = (profile.SearchTerms ?? new List<string>())
                .Where(x => SteamRecommendationContainsKeyword(itemTextParts, x))
                .Take(4)
                .ToList();
            if (matchedTerms.Count > 0)
            {
                reasons.Add("terms=" + string.Join(", ", matchedTerms));
            }

            var sharedDevelopers = (profile.Developers ?? new List<string>())
                .Intersect(item.Developers ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (sharedDevelopers.Count > 0)
            {
                reasons.Add("devs=" + string.Join(", ", sharedDevelopers));
            }

            var sharedPublishers = (profile.Publishers ?? new List<string>())
                .Intersect(item.Publishers ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (sharedPublishers.Count > 0)
            {
                reasons.Add("pubs=" + string.Join(", ", sharedPublishers));
            }

            if (item.RecommendationsTotal > 0)
            {
                reasons.Add("steamReviews=" + item.RecommendationsTotal.ToString("N0"));
            }

            if (item.DiscountPercent > 0)
            {
                reasons.Add("discount=" + item.DiscountPercent.ToString(CultureInfo.InvariantCulture) + "%");
            }

            return reasons.Count > 0 ? string.Join(" | ", reasons) : "none";
        }

        private int ScoreSteamRecommendation(SteamStoreItem item, SteamRecommendationProfile profile)
        {
            if (item == null || profile == null)
            {
                return 0;
            }

            var itemGenres = (item.Genres ?? new List<string>())
                .Concat(item.Categories ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var itemTextParts = BuildSteamRecommendationTextParts(item);

            var itemFamily = DetectRecommendationSignalFamily(itemTextParts);

            if (AreFamiliesIncompatible(profile.Family, itemFamily))
            {
                return 0;
            }

            var score = 0;

            var strongMatches = CountStrongRecommendationTermMatches(item, profile);
            if (strongMatches == 0)
            {
                // Same developer/publisher can still be useful, otherwise this is usually a noisy Steam result.
                var sameDev = (profile.Developers ?? new List<string>()).Intersect(item.Developers ?? new List<string>(), StringComparer.OrdinalIgnoreCase).Any();
                var samePub = (profile.Publishers ?? new List<string>()).Intersect(item.Publishers ?? new List<string>(), StringComparer.OrdinalIgnoreCase).Any();
                if (!sameDev && !samePub)
                {
                    return 0;
                }
            }

            if (!string.IsNullOrWhiteSpace(profile.Family) &&
                !string.Equals(profile.Family, "generic", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile.Family, "mixed", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.Family, itemFamily, StringComparison.OrdinalIgnoreCase))
            {
                score += 60;
            }

            if (profile.PreferredFamilies != null && profile.PreferredFamilies.Any(x => string.Equals(x, itemFamily, StringComparison.OrdinalIgnoreCase)))
            {
                score += 25;
            }

            var usefulProfileGenres = (profile.Genres ?? new List<string>())
                .Select(CanonicalizeSteamRecommendationSearchTerm)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsWeakRecommendationScoreTerm(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var genre in usefulProfileGenres)
            {
                if (SteamRecommendationContainsKeyword(itemGenres, genre))
                {
                    score += 12;
                }
            }

            var usefulProfileTags = (profile.Tags ?? new List<string>())
                .Select(CanonicalizeSteamRecommendationSearchTerm)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsWeakRecommendationScoreTerm(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var tag in usefulProfileTags)
            {
                if (SteamRecommendationContainsKeyword(itemTextParts, tag))
                {
                    score += 16;
                }
            }

            var sharedDevelopers = (profile.Developers ?? new List<string>())
                .Intersect(item.Developers ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            score += sharedDevelopers.Count * 70;

            var sharedPublishers = (profile.Publishers ?? new List<string>())
                .Intersect(item.Publishers ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            score += sharedPublishers.Count * 18;

            foreach (var term in profile.SearchTerms ?? new List<string>())
            {
                if (SteamRecommendationContainsKeyword(itemTextParts, term))
                {
                    var weight = GetRecommendationTermWeight(profile, term);
                    var isWeak = IsWeakRecommendationScoreTerm(term);
                    score += isWeak ? 6 : Math.Max(12, Math.Min(48, weight / 3));
                }
            }

            if (item.SteamRank > 0)
            {
                score += Math.Max(0, 28 - Math.Min(28, item.SteamRank));
            }

            if (item.RecommendationsTotal > 0)
            {
                score += Math.Min(35, (int)Math.Round(Math.Log10(item.RecommendationsTotal + 1) * 9));
            }

            if (item.DiscountPercent > 0 || !string.IsNullOrWhiteSpace(item.DiscountDisplay))
            {
                score += 6;
            }

            if (!string.IsNullOrWhiteSpace(item.FinalPriceDisplay))
            {
                score += 6;
            }

            if (!string.IsNullOrWhiteSpace(item.StoreCardImage))
            {
                score += 8;
            }

            if (string.IsNullOrWhiteSpace(item.StoreCardImage))
            {
                score -= 20;
            }

            return Math.Max(0, score);
        }

        private bool SteamRecommendationContainsKeyword(IEnumerable<string> values, string keyword)
        {
            if (values == null || string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            var needle = NormalizeTextForGenre(keyword);
            if (string.IsNullOrWhiteSpace(needle) || needle.Length < 3)
            {
                return false;
            }

            foreach (var value in values)
            {
                var haystack = NormalizeTextForGenre(value);
                if (string.IsNullOrWhiteSpace(haystack))
                {
                    continue;
                }

                if (haystack.Contains(needle))
                {
                    return true;
                }
            }

            return false;
        }

        private bool LooksBlockedSteamRecommendation(SteamStoreItem item)
        {
            if (item == null)
            {
                return true;
            }

            var title = (item.Name ?? string.Empty).ToLowerInvariant();
            var normalizedTitle = NormalizeTextForGenre(item.Name ?? string.Empty);
            var text = string.Join(" ", new[]
            {
                item.Name ?? string.Empty,
                item.ShortDescription ?? string.Empty,
                string.Join(" ", item.Genres ?? new List<string>()),
                string.Join(" ", item.Categories ?? new List<string>()),
                string.Join(" ", item.Tags ?? new List<string>())
            }).ToLowerInvariant();
            var normalizedText = NormalizeTextForGenre(text);

            if (title.Contains("art book") ||
                title.Contains("artbook") ||
                title.Contains("digital art") ||
                title.Contains("wallpaper") ||
                title.Contains("soundtrack") ||
                title.Contains(" ost") ||
                title.EndsWith(" ost", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" - comic book") ||
                title.Contains(": comic book") ||
                title.Contains("& comic book") ||
                title.EndsWith(" comic book", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.Contains(" game creator") ||
                normalizedTitle.Contains(" creator") ||
                normalizedTitle.Contains(" tool") ||
                normalizedTitle.Contains(" toolkit") ||
                normalizedTitle.Contains(" kit") ||
                normalizedTitle.Contains(" template") ||
                normalizedTitle.Contains(" chapter 0") ||
                normalizedTitle.Contains(" chapter zero") ||
                normalizedTitle.Contains(" bundle") ||
                normalizedTitle.Contains(" dlc") ||
                normalizedTitle.Contains(" pack") ||
                (normalizedTitle.Contains(" click") && !normalizedTitle.Contains("point and click")))
            {
                return true;
            }

            string[] blockedKeywords =
            {
                "demo",
                "prologue",
                "soundtrack",
                "ost",
                "art book",
                "artbook",
                "wallpaper",
                "digital art",
                "season pass",
                "upgrade pack",
                "starter pack",
                "currency pack",
                "downloadable content",
                " dlc",
                "bonus content",
                "supporter pack",
                "deluxe upgrade",
                "creator",
                "game creator",
                "toolkit",
                " template",
                "chapter 0",
                "chapter zero",
                "hentai",
                "adult only",
                "nsfw",
                "porn",
                "sexual content",
                "nudity",
                "erotic",
                "18+"
            };

            foreach (var keyword in blockedKeywords)
            {
                if (text.Contains(keyword) || normalizedText.Contains(NormalizeTextForGenre(keyword)))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReplaceSteamStoreRecommendedCollections(List<SteamStoreItem> items)
        {
            var safeItems = items ?? new List<SteamStoreItem>();

            ReplaceSteamStoreCollection(Settings.SteamStoreRecommended, safeItems);
            ReplaceSteamStoreCollection(Settings.SteamStoreRecommendedHub, safeItems.Take(4).ToList());

            Settings.NotifyHubForYouStorePageStateChanged();
        }

        private class SteamRecommendationProfile
        {
            public string ReferenceName { get; set; }
            public List<string> ReferenceNames { get; set; } = new List<string>();
            public string Family { get; set; }
            public string CacheKey { get; set; }
            public List<string> SearchTerms { get; set; } = new List<string>();
            public List<string> Genres { get; set; } = new List<string>();
            public List<string> Tags { get; set; } = new List<string>();
            public List<string> Developers { get; set; } = new List<string>();
            public List<string> Publishers { get; set; } = new List<string>();
            public Dictionary<string, int> SearchTermWeights { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> FamilyWeights { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<string> PreferredFamilies { get; set; } = new List<string>();
            public HashSet<int> OwnedSteamAppIds { get; set; } = new HashSet<int>();
            public HashSet<string> OwnedNormalizedNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<int> SeedSteamAppIds { get; set; } = new List<int>();
            public List<string> SeedSourceNames { get; set; } = new List<string>();
        }

        private class SteamRecommendationRankedCandidate
        {
            public SteamStoreItem Item { get; set; }
            public int Score { get; set; }
        }
        public void SetSteamStoreSection(string section)
        {
            if (Settings == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(section))
            {
                section = "Featured";
            }

            Settings.SteamStoreSelectedSection = section;
            UpdateSteamStoreActiveSection(preserveFocusedCard: false);
            ResetSteamStoreItemsListToStart();
        }

        public void SwitchSteamStoreSection(int direction)
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
                        if (Settings == null)
                        {
                            return;
                        }

                        if (Settings.SteamStoreDetailsVisible ||
                            (Settings.SteamStoreLoading && !Settings.SteamStoreAvailable))
                        {
                            return;
                        }

                        if (!IsSteamStoreViewVisible())
                        {
                            return;
                        }

                        string[] sections =
                        {
                    "Deals",
                    "Recommended",
                    "MyWishlist",
                    "New",
                    "Popular",
                    "Wishlisted",
                    "Upcoming"
                };

                        var currentSection = Settings.SteamStoreSelectedSection ?? "Deals";

                        var currentIndex = Array.FindIndex(
                            sections,
                            x => string.Equals(x, currentSection, StringComparison.OrdinalIgnoreCase)
                        );

                        if (currentIndex < 0)
                        {
                            currentIndex = 0;
                        }

                        var nextIndex = currentIndex + direction;

                        if (nextIndex < 0)
                        {
                            nextIndex = sections.Length - 1;
                        }

                        if (nextIndex >= sections.Length)
                        {
                            nextIndex = 0;
                        }

                        SetSteamStoreSection(sections[nextIndex]);

                        FocusSteamStoreHeroButton();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to switch Steam Store section.");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] SwitchSteamStoreSection failed.");
            }
        }

        private bool IsSteamStoreViewVisible()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    return dispatcher.Invoke(new Func<bool>(IsSteamStoreViewVisible));
                }

                var app = Application.Current;

                if (app == null)
                {
                    return false;
                }

                return app.Windows
                    .OfType<Window>()
                    .Where(w => w.IsVisible)
                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                    .Any(x => x.Name == "SteamStoreOverlay" && x.IsVisible);
            }
            catch
            {
                return false;
            }
        }

        private void FocusSteamStoreHeroButton()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    return;
                }

                Action focusAction = () =>
                {
                    try
                    {
                        var app = Application.Current;

                        if (app == null || Settings == null)
                        {
                            return;
                        }

                        var visibleElements = app.Windows
                            .OfType<Window>()
                            .Where(w => w.IsVisible)
                            .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                            .ToList();

                        var overlay = visibleElements
                            .FirstOrDefault(x => x.Name == "SteamStoreOverlay");

                        var heroButton = visibleElements
                            .FirstOrDefault(x => x.Name == "SteamStoreHeroButton");

                        var activeTabName = GetSteamStoreActiveTabButtonName();

                        var activeTabButton = visibleElements
                            .FirstOrDefault(x => x.Name == activeTabName);

                        overlay?.UpdateLayout();

                        // Rebuild directional focus path, but immediately move to Hero
                        // in the same UI cycle so the tab focus does not visibly flash.
                        if (activeTabButton is UIElement tabElement && tabElement.IsVisible)
                        {
                            tabElement.Focus();
                            Keyboard.Focus(tabElement);

                            var tabFocusScope = FocusManager.GetFocusScope(tabElement);
                            FocusManager.SetFocusedElement(tabFocusScope, tabElement);
                        }

                        overlay?.UpdateLayout();

                        if (heroButton is UIElement heroElement && heroElement.IsVisible)
                        {
                            heroElement.Focus();
                            Keyboard.Focus(heroElement);

                            var heroFocusScope = FocusManager.GetFocusScope(heroElement);
                            FocusManager.SetFocusedElement(heroFocusScope, heroElement);
                        }
                    }
                    catch
                    {
                    }
                };

                if (dispatcher.CheckAccess())
                {
                    focusAction();
                }
                else
                {
                    _ = dispatcher.BeginInvoke(focusAction, DispatcherPriority.Send);
                }
            }
            catch
            {
            }
        }

        private async void ResetSteamStoreItemsListToStart()
        {
            try
            {
                await Task.Delay(40);

                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    return;
                }

                _ = dispatcher.BeginInvoke(new Action(ResetSteamStoreItemsListToStartOnUi), DispatcherPriority.ContextIdle);

                // Do it a second time after layout/data-template refresh.
                // The Steam Store section change rebuilds the list and the internal ScrollViewer can restore
                // its previous horizontal offset if we reset too early.
                await Task.Delay(120);

                _ = dispatcher.BeginInvoke(new Action(ResetSteamStoreItemsListToStartOnUi), DispatcherPriority.ContextIdle);
            }
            catch
            {
            }
        }

        private void ResetSteamStoreItemsListToStartOnUi()
        {
            try
            {
                var app = Application.Current;

                if (app == null)
                {
                    return;
                }

                var visibleElements = app.Windows
                    .OfType<Window>()
                    .Where(w => w.IsVisible)
                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                    .ToList();

                var listBox = visibleElements
                    .OfType<ListBox>()
                    .FirstOrDefault(x => x.Name == "StoreItemsList" && x.IsVisible);

                if (listBox == null)
                {
                    return;
                }

                listBox.UpdateLayout();

                if (listBox.Items.Count > 0)
                {
                    listBox.SelectedIndex = 0;
                    listBox.ScrollIntoView(listBox.Items[0]);
                }

                var scrollViewer = listBox.FindVisualChildren<ScrollViewer>().FirstOrDefault();

                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToHome();
                    scrollViewer.ScrollToLeftEnd();
                    scrollViewer.ScrollToHorizontalOffset(0);
                }

                listBox.UpdateLayout();
            }
            catch
            {
            }
        }

        private string GetSteamStoreActiveTabButtonName()
        {
            var section = Settings?.SteamStoreSelectedSection ?? "Deals";

            switch (section)
            {
                case "New":
                    return "SteamStoreTabNewButton";

                case "Upcoming":
                    return "SteamStoreTabUpcomingButton";

                case "Wishlisted":
                    return "SteamStoreTabWishlistedButton";

                case "MyWishlist":
                    return "SteamStoreTabMyWishlistButton";

                case "Popular":
                    return "SteamStoreTabPopularButton";

                case "Recommended":
                    return "SteamStoreTabRecommendedButton";

                case "Deals":
                default:
                    return "SteamStoreTabDealsButton";
            }
        }

        private void UpdateSteamStoreAvailabilityState()
        {
            if (Settings == null)
            {
                return;
            }

            var hasAnyData =
                Settings.SteamStoreDeals.Count > 0 ||
                Settings.SteamStoreTopSellers.Count > 0 ||
                Settings.SteamStoreNewReleases.Count > 0 ||
                Settings.SteamStoreUpcoming.Count > 0 ||
                Settings.SteamStoreWishlisted.Count > 0 ||
                Settings.SteamStoreMyWishlist.Count > 0 ||
                Settings.SteamStoreRecommended.Count > 0;

            Settings.SteamStoreAvailable = hasAnyData;

            if (hasAnyData)
            {
                Settings.SteamStoreError = string.Empty;
            }
            else if (string.IsNullOrWhiteSpace(Settings.SteamStoreError))
            {
                Settings.SteamStoreError = "No store data available";
            }
        }

        private static bool IsSteamStoreSectionAuthOnly(string section)
        {
            return string.Equals(section, "MyWishlist", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(section, "Recommended", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSteamStoreActiveSection(bool preserveFocusedCard = true)
        {
            if (Settings == null)
            {
                return;
            }

            var focusSnapshot = preserveFocusedCard
                ? CaptureSteamStoreFocusSnapshot()
                : null;

            var section = Settings.SteamStoreSelectedSection ?? "Featured";
            var authRequiredForSection = IsSteamStoreSectionAuthOnly(section) && !CanUseConnectedSteamStoreAccount();

            IEnumerable<SteamStoreItem> source = null;
            var title = "Featured";

            switch (section)
            {
                case "New":
                    source = Settings.SteamStoreNewReleases;
                    title = "New Releases";
                    break;

                case "Upcoming":
                    source = Settings.SteamStoreUpcoming;
                    title = "Upcoming";
                    break;

                case "Wishlisted":
                    source = Settings.SteamStoreWishlisted;
                    title = "Most Wishlisted";
                    break;

                case "MyWishlist":
                    source = Settings.SteamStoreMyWishlist;
                    title = "My Wishlist";
                    break;

                case "Popular":
                    source = Settings.SteamStoreTopSellers;
                    title = "Popular";
                    break;

                case "Recommended":
                    source = Settings.SteamStoreRecommended;
                    title = "Recommended For You";
                    break;

                case "Deals":
                default:
                    source = Settings.SteamStoreDeals;
                    title = "Deals";
                    section = "Deals";
                    break;
            }

            Settings.SteamStoreSelectedSection = section;
            Settings.SteamStoreSelectedSectionTitle = title;
            Settings.SteamStoreSelectedSectionRequiresSteamAuth = authRequiredForSection;

            if (authRequiredForSection)
            {
                source = Enumerable.Empty<SteamStoreItem>();
            }

            Settings.SteamStoreCurrentItems.Clear();
            Settings.SteamStoreCurrentListItems.Clear();

            if (source != null)
            {
                foreach (var item in source.Where(x => x != null))
                {
                    Settings.SteamStoreCurrentItems.Add(item);
                }
            }

            Settings.SteamStoreHeroItem = SelectSteamStoreHeroItem(Settings.SteamStoreCurrentItems, section);

            foreach (var item in Settings.SteamStoreCurrentItems.Where(x => x != null))
            {
                if (IsSameSteamStoreItem(item, Settings.SteamStoreHeroItem))
                {
                    continue;
                }

                Settings.SteamStoreCurrentListItems.Add(item);
            }

            UpdateSteamStoreHeroProperties(Settings.SteamStoreHeroItem);

            QueueSteamStoreFocusRestore(focusSnapshot);

            // Quiet by default: hero selection can be recalculated by XAML bindings while the Hub is opening.
        }

        private sealed class SteamStoreFocusSnapshot
        {
            public string Section { get; set; }
            public bool WasHeroFocused { get; set; }
            public int AppId { get; set; }
            public string Name { get; set; }
            public int ListIndex { get; set; }
        }

        private SteamStoreFocusSnapshot CaptureSteamStoreFocusSnapshot()
        {
            try
            {
                var app = Application.Current;
                var focused = Keyboard.FocusedElement as DependencyObject;

                if (app == null || focused == null)
                {
                    return null;
                }

                var visibleElements = app.Windows
                    .OfType<Window>()
                    .Where(w => w.IsVisible)
                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                    .ToList();

                var overlay = visibleElements.FirstOrDefault(x => x.Name == "SteamStoreOverlay");
                if (overlay == null || !overlay.IsVisible || !IsDescendantOrSelf(focused, overlay))
                {
                    return null;
                }

                var heroButton = visibleElements.FirstOrDefault(x => x.Name == "SteamStoreHeroButton");
                if (heroButton != null && IsDescendantOrSelf(focused, heroButton))
                {
                    return new SteamStoreFocusSnapshot
                    {
                        Section = Settings.SteamStoreSelectedSection ?? string.Empty,
                        WasHeroFocused = true,
                        AppId = Settings.SteamStoreHeroItem?.AppId ?? 0,
                        Name = Settings.SteamStoreHeroItem?.Name ?? string.Empty,
                        ListIndex = -1
                    };
                }

                var listBox = visibleElements
                    .OfType<ListBox>()
                    .FirstOrDefault(x => x.Name == "StoreItemsList" && x.IsVisible);

                if (listBox == null || !IsDescendantOrSelf(focused, listBox))
                {
                    return null;
                }

                var item = FindSteamStoreItemDataContext(focused);
                if (item == null)
                {
                    return null;
                }

                return new SteamStoreFocusSnapshot
                {
                    Section = Settings.SteamStoreSelectedSection ?? string.Empty,
                    WasHeroFocused = false,
                    AppId = item.AppId,
                    Name = item.Name ?? string.Empty,
                    ListIndex = Settings.SteamStoreCurrentListItems.IndexOf(item)
                };
            }
            catch
            {
                return null;
            }
        }

        private SteamStoreItem FindSteamStoreItemDataContext(DependencyObject element)
        {
            var current = element;

            while (current != null)
            {
                var frameworkElement = current as FrameworkElement;
                var item = frameworkElement?.DataContext as SteamStoreItem;
                if (item != null)
                {
                    return item;
                }

                DependencyObject parent = null;

                try
                {
                    parent = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    parent = null;
                }

                if (parent == null && current is FrameworkElement parentElement)
                {
                    parent = parentElement.Parent;
                }

                current = parent;
            }

            return null;
        }

        private void QueueSteamStoreFocusRestore(SteamStoreFocusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            _ = dispatcher.BeginInvoke(
                new Action(() => RestoreSteamStoreFocusOnUi(snapshot, true)),
                DispatcherPriority.ContextIdle);

            _ = Task.Run(async () =>
            {
                await Task.Delay(120).ConfigureAwait(false);

                try
                {
                    _ = dispatcher.BeginInvoke(
                        new Action(() => RestoreSteamStoreFocusOnUi(snapshot, false)),
                        DispatcherPriority.ContextIdle);
                }
                catch
                {
                }
            });
        }

        private void RestoreSteamStoreFocusOnUi(SteamStoreFocusSnapshot snapshot, bool forceFirstPass)
        {
            try
            {
                if (snapshot == null || Settings == null || Settings.SteamStoreDetailsVisible || !IsSteamStoreViewVisible())
                {
                    return;
                }

                if (!string.Equals(
                    snapshot.Section ?? string.Empty,
                    Settings.SteamStoreSelectedSection ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var app = Application.Current;
                if (app == null)
                {
                    return;
                }

                var visibleElements = app.Windows
                    .OfType<Window>()
                    .Where(w => w.IsVisible)
                    .SelectMany(w => w.FindVisualChildren<FrameworkElement>())
                    .ToList();

                var heroButton = visibleElements.FirstOrDefault(x => x.Name == "SteamStoreHeroButton");
                var listBox = visibleElements
                    .OfType<ListBox>()
                    .FirstOrDefault(x => x.Name == "StoreItemsList" && x.IsVisible);

                if (!forceFirstPass)
                {
                    var currentFocus = Keyboard.FocusedElement as DependencyObject;
                    var alreadyHasStoreFocus = currentFocus != null &&
                        ((heroButton != null && IsDescendantOrSelf(currentFocus, heroButton)) ||
                         (listBox != null && IsDescendantOrSelf(currentFocus, listBox)));

                    if (alreadyHasStoreFocus)
                    {
                        return;
                    }
                }

                if (snapshot.WasHeroFocused)
                {
                    FocusSteamStoreElement(heroButton as UIElement);
                    return;
                }

                SteamStoreItem target = null;

                if (snapshot.AppId > 0)
                {
                    target = Settings.SteamStoreCurrentListItems
                        .FirstOrDefault(x => x != null && x.AppId == snapshot.AppId);
                }

                if (target == null && !string.IsNullOrWhiteSpace(snapshot.Name))
                {
                    target = Settings.SteamStoreCurrentListItems
                        .FirstOrDefault(x => x != null && string.Equals(x.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));
                }

                var heroMatches = Settings.SteamStoreHeroItem != null &&
                    ((snapshot.AppId > 0 && Settings.SteamStoreHeroItem.AppId == snapshot.AppId) ||
                     (!string.IsNullOrWhiteSpace(snapshot.Name) &&
                      string.Equals(Settings.SteamStoreHeroItem.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase)));

                if (target == null && heroMatches)
                {
                    FocusSteamStoreElement(heroButton as UIElement);
                    return;
                }

                if (target == null && Settings.SteamStoreCurrentListItems.Count > 0)
                {
                    var fallbackIndex = Math.Max(0, Math.Min(snapshot.ListIndex, Settings.SteamStoreCurrentListItems.Count - 1));
                    target = Settings.SteamStoreCurrentListItems[fallbackIndex];
                }

                if (target == null || listBox == null)
                {
                    FocusSteamStoreElement(heroButton as UIElement);
                    return;
                }

                listBox.SelectedItem = target;
                listBox.ScrollIntoView(target);
                listBox.UpdateLayout();

                var container = listBox.ItemContainerGenerator.ContainerFromItem(target) as DependencyObject;
                var targetButton = container?.FindVisualChildren<ButtonBase>().FirstOrDefault();

                if (targetButton == null)
                {
                    targetButton = listBox.FindVisualChildren<ButtonBase>()
                        .FirstOrDefault(button =>
                        {
                            var buttonItem = button.DataContext as SteamStoreItem;
                            return buttonItem != null && IsSameSteamStoreItem(buttonItem, target);
                        });
                }

                FocusSteamStoreElement(targetButton);
            }
            catch
            {
            }
        }

        private void FocusSteamStoreElement(UIElement element)
        {
            if (element == null || !element.IsVisible)
            {
                return;
            }

            element.Focus();
            Keyboard.Focus(element);

            var focusScope = FocusManager.GetFocusScope(element);
            FocusManager.SetFocusedElement(focusScope, element);
        }

        private SteamStoreItem SelectSteamStoreHeroItem(IEnumerable<SteamStoreItem> items, string section)
        {
            var list = items?.Where(x => x != null).ToList() ?? new List<SteamStoreItem>();
            if (list.Count == 0)
            {
                return null;
            }

            var selected = list
                .OrderByDescending(x => GetSteamStoreHeroPopularityScore(x, section))
                .ThenBy(x => x.SteamRank > 0 ? x.SteamRank : int.MaxValue)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.Name) ? string.Empty : x.Name)
                .FirstOrDefault();

            return selected ?? list.FirstOrDefault();
        }

        private long GetSteamStoreHeroPopularityScore(SteamStoreItem item, string section)
        {
            if (item == null)
            {
                return long.MinValue;
            }

            long score = 0;

            if (string.Equals(section, "Recommended", StringComparison.OrdinalIgnoreCase) && item.RecommendationScore > 0)
            {
                score += item.RecommendationScore * 100000L;
            }

            var rankScore = item.SteamRank > 0 ? Math.Max(0, 100000 - item.SteamRank) : 0;

            if (string.Equals(section, "Deals", StringComparison.OrdinalIgnoreCase))
            {
                // For Deals, a real discount should matter more than raw list position.
                // This avoids picking a popular item that is barely a deal just because it is rank #1/#2.
                score += rankScore * 1000L;
                score += Math.Max(0, item.DiscountPercent) * 500000L;

                if (!string.IsNullOrWhiteSpace(item.OriginalPriceDisplay))
                {
                    score += 250000L;
                }
            }
            else
            {
                // Steam search/category order is already the best lightweight popularity signal for
                // Upcoming, Wishlisted, New Releases and Top Sellers. Lower rank = more popular.
                score += rankScore * 1000L;
            }

            // AppDetails data is available for some sections/items. Use it as an extra signal,
            // but don't force heavy media downloads just to choose the hero.
            if (item.RecommendationsTotal > 0)
            {
                score += item.RecommendationsTotal * 10L;
            }

            if (item.MetacriticScore > 0)
            {
                score += item.MetacriticScore * 1000L;
            }

            if (!string.Equals(section, "Deals", StringComparison.OrdinalIgnoreCase) && item.DiscountPercent > 0)
            {
                score += item.DiscountPercent * 100L;
            }

            if (!string.IsNullOrWhiteSpace(item.HeaderImageLocalPath) || !string.IsNullOrWhiteSpace(item.CapsuleImageLocalPath))
            {
                score += 250;
            }

            if (!string.IsNullOrWhiteSpace(item.FinalPriceDisplay))
            {
                score += 100;
            }

            return score;
        }

        private bool IsSameSteamStoreItem(SteamStoreItem a, SteamStoreItem b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a.AppId > 0 && b.AppId > 0)
            {
                return a.AppId == b.AppId;
            }

            return string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSteamStoreHeroProperties(SteamStoreItem hero)
        {
            if (Settings == null)
            {
                return;
            }

            if (hero == null)
            {
                Settings.SteamStoreHeroName = string.Empty;
                Settings.SteamStoreHeroDescription = string.Empty;
                Settings.SteamStoreHeroImage = string.Empty;
                Settings.SteamStoreHeroBackgroundImage = string.Empty;
                Settings.SteamStoreHeroPrice = string.Empty;
                Settings.SteamStoreHeroOriginalPrice = string.Empty;
                Settings.SteamStoreHeroDiscount = string.Empty;
                Settings.SteamStoreHeroReleaseDate = string.Empty;
                return;
            }

            Settings.SteamStoreHeroName = hero.Name ?? string.Empty;
            Settings.SteamStoreHeroDescription = hero.ShortDescription ?? string.Empty;
            Settings.SteamStoreHeroPrice = hero.FinalPriceDisplay ?? string.Empty;
            Settings.SteamStoreHeroOriginalPrice = hero.OriginalPriceDisplay ?? string.Empty;
            Settings.SteamStoreHeroDiscount = hero.DiscountDisplay ?? string.Empty;
            Settings.SteamStoreHeroReleaseDate = hero.ReleaseDateDisplay ?? string.Empty;

            // Hero uses the same wide-header priority as all Store cards.
            Settings.SteamStoreHeroImage = hero.StoreCardImage ?? string.Empty;

            // Fond du hero : vrai background/screenshot uniquement.
            Settings.SteamStoreHeroBackgroundImage = FirstSteamStoreImage(
                hero.BackgroundImageLocalPath,
                hero.BackgroundImageUrl,
                hero.Screenshot1LocalPath,
                hero.Screenshot1Url
            );
        }

        private async Task RefreshSteamStoreHeroDetailsAsync(SteamStoreItem hero)
        {
            if (hero == null || steamStoreService == null || Settings == null)
            {
                return;
            }

            try
            {
                var alreadyHasDescription = !string.IsNullOrWhiteSpace(hero.ShortDescription);
                var alreadyHasBackground =
                    !string.IsNullOrWhiteSpace(hero.BackgroundImageLocalPath) ||
                    !string.IsNullOrWhiteSpace(hero.BackgroundImageUrl) ||
                    !string.IsNullOrWhiteSpace(hero.Screenshot1LocalPath) ||
                    !string.IsNullOrWhiteSpace(hero.Screenshot1Url);

                if (alreadyHasDescription && alreadyHasBackground)
                {
                    return;
                }

                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();

                // Hero/list refresh stays light. Heavy media is downloaded when the details view opens.
                await steamStoreService.EnrichStoreItemDetailsAsync(hero, language, region, downloadMedia: false);

                OnUi(() =>
                {
                    if (Settings != null && Settings.SteamStoreHeroItem == hero)
                    {
                        UpdateSteamStoreHeroProperties(hero);
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to refresh Steam Store hero details.");
            }
        }

        private static string FirstSteamStoreImage(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            // Prefer local files when they exist.
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || IsBadSteamSharedImage(value))
                {
                    continue;
                }

                if (!IsSteamStoreUrl(value) && File.Exists(value))
                {
                    return value;
                }
            }

            // Fallback to remote Steam CDN URLs so cards do not stay black if an image download failed.
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || IsBadSteamSharedImage(value))
                {
                    continue;
                }

                if (IsSteamStoreUrl(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static bool IsSteamStoreUrl(string value)
        {
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBadSteamSharedImage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("/public/shared/images/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("footerLogo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("footer_logo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("valve_new", StringComparison.OrdinalIgnoreCase) >= 0;
        }



        private async Task<SteamStorePersonalizationContext> BuildSteamStorePersonalizationContextAsync()
        {
            var context = new SteamStorePersonalizationContext();

            try
            {
                var games = PlayniteApi?.Database?.Games?.ToList() ?? new List<Playnite.SDK.Models.Game>();
                if (Settings?.IncludeHidden != true)
                {
                    games = games.Where(g => g != null && g.Hidden != true).ToList();
                }

                var externalWishlistOnlyCount = 0;
                try
                {
                    externalWishlistOnlyCount = games.Count(IsExternalWishlistOnlySource);
                }
                catch
                {
                    externalWishlistOnlyCount = 0;
                }

                if (externalWishlistOnlyCount > 0)
                {
                    // Quiet by default. This can run every time Hub/Store cache is requested.
                }

                var libraryAppIds = GetOwnedSteamAppIds(games);
                foreach (var appId in libraryAppIds)
                {
                    if (appId > 0)
                    {
                        context.OwnedSteamAppIds.Add(appId);
                    }
                }

                var names = GetOwnedNormalizedGameNames(games);
                foreach (var name in names)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        context.OwnedNormalizedNames.Add(name);
                    }
                }

                var playedAppIds = GetPlayedSteamAppIds(games);
                foreach (var appId in playedAppIds)
                {
                    if (appId > 0)
                    {
                        context.PlayedSteamAppIds.Add(appId);
                    }
                }

                var playedNames = GetPlayedNormalizedGameNames(games);
                foreach (var name in playedNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        context.PlayedNormalizedNames.Add(name);
                    }
                }

                if (steamUserGamesService != null && Settings != null)
                {
                    var steamOwned = await steamUserGamesService.GetOwnedGameAppIdsAsync(
                        Settings.SteamApiKey,
                        GetEffectiveSteamIdInput(),
                        TimeSpan.FromHours(12)
                    ).ConfigureAwait(false);

                    foreach (var appId in steamOwned)
                    {
                        if (appId > 0)
                        {
                            context.OwnedSteamAppIds.Add(appId);
                        }
                    }
                }

                if (steamStoreService != null && CanUseConnectedSteamStoreAccount())
                {
                    var language = GetResolvedSteamStoreLanguage();
                    var region = GetResolvedSteamStoreRegion();
                    var cachedWishlist = await steamStoreService.GetUserWishlistFromCacheOnlyAsync(
                        GetSteamMyWishlistCacheKey(),
                        language,
                        region
                    ).ConfigureAwait(false);

                    foreach (var appId in (cachedWishlist ?? new List<SteamStoreItem>()).Select(x => x?.AppId ?? 0))
                    {
                        if (appId > 0)
                        {
                            context.WishlistSteamAppIds.Add(appId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to build Steam Store personalization context.");
            }

            return context;
        }

        private async Task<List<SteamStoreRecommendationSeed>> BuildSteamStoreRecommendationSeedsAsync(SteamRecommendationProfile profile)
        {
            var result = new List<SteamStoreRecommendationSeed>();

            try
            {
                if (profile != null && profile.SeedSteamAppIds != null)
                {
                    var index = 0;
                    foreach (var appId in profile.SeedSteamAppIds.Where(x => x > 0).Distinct().Take(5))
                    {
                        var name = profile.SeedSourceNames != null && profile.SeedSourceNames.Count > index
                            ? profile.SeedSourceNames[index]
                            : string.Empty;

                        result.Add(new SteamStoreRecommendationSeed
                        {
                            AppId = appId,
                            Name = name,
                            Source = "Playnite recent",
                            Weight = Math.Max(100, 260 - (index * 45))
                        });

                        index++;
                    }
                }

                if (profile != null && profile.ReferenceNames != null && profile.ReferenceNames.Count > 0)
                {
                    var games = PlayniteApi?.Database?.Games?.ToList() ?? new List<Playnite.SDK.Models.Game>();
                    var selectedByName = games
                        .Where(g => g != null && profile.ReferenceNames.Any(n => string.Equals(n, g.Name, StringComparison.OrdinalIgnoreCase)))
                        .Take(4)
                        .ToList();

                    var resolvedIndex = 0;
                    foreach (var game in selectedByName)
                    {
                        var alreadyKnown = result.Any(x => x != null && string.Equals(x.Name, game.Name, StringComparison.OrdinalIgnoreCase));
                        if (alreadyKnown)
                        {
                            continue;
                        }

                        var resolved = await ResolveSteamGameIdAsync(game, CancellationToken.None, true).ConfigureAwait(false);
                        if (int.TryParse(resolved, out var resolvedAppId) && resolvedAppId > 0)
                        {
                            result.Add(new SteamStoreRecommendationSeed
                            {
                                AppId = resolvedAppId,
                                Name = game.Name ?? string.Empty,
                                Source = "Playnite resolved",
                                Weight = Math.Max(90, 220 - (resolvedIndex * 35))
                            });
                            resolvedIndex++;
                        }
                    }
                }

                if (steamUserGamesService != null && Settings != null)
                {
                    var recentSteamSeeds = await steamUserGamesService.GetRecentlyPlayedGameSeedsAsync(
                        Settings.SteamApiKey,
                        GetEffectiveSteamIdInput(),
                        5,
                        TimeSpan.FromHours(6)
                    ).ConfigureAwait(false);

                    foreach (var seed in recentSteamSeeds ?? new List<SteamUserRecentGameSeed>())
                    {
                        if (seed == null || seed.AppId <= 0)
                        {
                            continue;
                        }

                        result.Add(new SteamStoreRecommendationSeed
                        {
                            AppId = seed.AppId,
                            Name = seed.Name ?? string.Empty,
                            Source = "Steam recent",
                            Weight = Math.Max(80, seed.Weight)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to build Steam Store recommendation seeds.");
            }

            var groupedSeeds = result
                .Where(x => x != null && x.AppId > 0)
                .GroupBy(x => x.AppId)
                .Select(g => new SteamStoreRecommendationSeed
                {
                    AppId = g.Key,
                    Name = g.Select(x => x.Name).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                    Source = string.Join(" + ", g.Select(x => x.Source).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)),
                    Weight = g.Sum(x => Math.Max(1, x.Weight))
                })
                .OrderByDescending(x => x.Weight)
                .ToList();

            var filteredSeeds = groupedSeeds
                .Where(x => steamStorePersonalizationService == null || !steamStorePersonalizationService.LooksLikeBadRecommendationSeed(x))
                .Take(4)
                .ToList();

            var skippedSeeds = groupedSeeds
                .Where(x => steamStorePersonalizationService != null && steamStorePersonalizationService.LooksLikeBadRecommendationSeed(x))
                .Take(6)
                .ToList();

            foreach (var seed in skippedSeeds)
            {
                logger.Info($"[Recommended] Seed filtered before request | {seed.AppId}:{seed.Name} | source={seed.Source}");
            }

            return filteredSeeds;
        }

        private List<SteamStoreItem> PersonalizeSteamStoreSection(
            IEnumerable<SteamStoreItem> items,
            SteamStorePersonalizationContext context,
            string section,
            int maxItems)
        {
            if (steamStorePersonalizationService == null)
            {
                var list = (items ?? Enumerable.Empty<SteamStoreItem>())
                    .Where(x => x != null)
                    .Take(maxItems > 0 ? maxItems : int.MaxValue)
                    .ToList();

                ApplySteamStoreFlags(list, context);
                return list;
            }

            return steamStorePersonalizationService.FilterSection(items, context, section, maxItems);
        }

        private void ApplySteamStoreFlags(IEnumerable<SteamStoreItem> items, SteamStorePersonalizationContext context)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items.Where(x => x != null))
            {
                if (steamStorePersonalizationService != null)
                {
                    steamStorePersonalizationService.ApplyStoreFlags(item, context);
                }
                else if (context != null)
                {
                    item.IsInLibrary = item.AppId > 0 && context.OwnedSteamAppIds != null && context.OwnedSteamAppIds.Contains(item.AppId);
                    item.IsInWishlist = item.IsInWishlist || (item.AppId > 0 && context.WishlistSteamAppIds != null && context.WishlistSteamAppIds.Contains(item.AppId));
                }
            }
        }

        private async Task<List<SteamStoreItem>> RefreshSteamStoreRecommendedByAppIdAsync(
            SteamRecommendationProfile recommendationProfile,
            SteamStorePersonalizationContext personalizationContext,
            string language,
            string region,
            Action<int> reportProgress)
        {
            if (recommendationProfile == null || steamStoreRecommendationService == null)
            {
                return new List<SteamStoreItem>();
            }

            var seeds = await BuildSteamStoreRecommendationSeedsAsync(recommendationProfile).ConfigureAwait(false);
            if (seeds == null || seeds.Count == 0)
            {
                return new List<SteamStoreItem>();
            }

            return await steamStoreRecommendationService.RefreshFromSeedsAsync(
                seeds,
                personalizationContext,
                recommendationProfile.CacheKey,
                language,
                region,
                reportProgress
            ).ConfigureAwait(false);
        }

        private void ReplaceSteamStoreCollection(System.Collections.ObjectModel.ObservableCollection<SteamStoreItem> target, System.Collections.Generic.List<SteamStoreItem> items)
        {
            if (target == null)
            {
                return;
            }

            items = items ?? new System.Collections.Generic.List<SteamStoreItem>();

            // Important:
            // Si la collection contient déjà les mêmes cartes, on ne la vide pas.
            // Sinon WPF détruit les ButtonEx existants et le focus peut repartir sur la top bar,
            // surtout sur la page Deals du Hub.
            if (AreSteamStoreCollectionsEquivalentForUi(target, items))
            {
                return;
            }

            target.Clear();

            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private bool AreSteamStoreCollectionsEquivalentForUi(
    System.Collections.ObjectModel.ObservableCollection<SteamStoreItem> current,
    System.Collections.Generic.List<SteamStoreItem> incoming)
        {
            if (current == null || incoming == null)
            {
                return false;
            }

            if (current.Count != incoming.Count)
            {
                return false;
            }

            for (var i = 0; i < current.Count; i++)
            {
                if (!AreSteamStoreItemsEquivalentForUi(current[i], incoming[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool AreSteamStoreItemsEquivalentForUi(SteamStoreItem current, SteamStoreItem incoming)
        {
            if (current == null || incoming == null)
            {
                return false;
            }

            return current.AppId == incoming.AppId &&
                   string.Equals(current.Name, incoming.Name, StringComparison.Ordinal) &&
                   string.Equals(current.HeaderImageLocalPath, incoming.HeaderImageLocalPath, StringComparison.Ordinal) &&
                   string.Equals(current.CapsuleImageLocalPath, incoming.CapsuleImageLocalPath, StringComparison.Ordinal) &&
                   string.Equals(current.FinalPriceDisplay, incoming.FinalPriceDisplay, StringComparison.Ordinal) &&
                   string.Equals(current.OriginalPriceDisplay, incoming.OriginalPriceDisplay, StringComparison.Ordinal) &&
                   string.Equals(current.DiscountDisplay, incoming.DiscountDisplay, StringComparison.Ordinal) &&
                   string.Equals(current.ReleaseDateDisplay, incoming.ReleaseDateDisplay, StringComparison.Ordinal) &&
                   current.IsInWishlist == incoming.IsInWishlist &&
                   current.IsInLibrary == incoming.IsInLibrary;
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

            var nowUtc = DateTime.UtcNow;
            if (HasSteamStoreCacheUiData() && (nowUtc - lastSteamStoreCacheOnlyLoadUtc) < SteamStoreCacheOnlyLoadThrottle)
            {
                return;
            }

            lastSteamStoreCacheOnlyLoadUtc = nowUtc;

            var language = GetResolvedSteamStoreLanguage();
            var region = GetResolvedSteamStoreRegion();
            var recommendationProfile = BuildSteamRecommendationProfile();
            var personalizationContext = await BuildSteamStorePersonalizationContextAsync().ConfigureAwait(false);

            var dealsTask = steamStoreService.GetDealsFromCacheOnlyAsync(language, region);
            var topSellersTask = steamStoreService.GetTopSellersFromCacheOnlyAsync(language, region);

            var upcomingItems = steamUpcomingGamesService.LoadFromCacheOnly(language, region)
                .Select(ConvertUpcomingToStoreItem)
                .Where(x => x != null)
                .ToList();

            var wishlistedItems = steamUpcomingGamesService.LoadWishlistedFromCacheOnly(language, region)
                .Select(ConvertUpcomingToStoreItem)
                .Where(x => x != null)
                .ToList();

            var canUseConnectedSteamStoreAccount = CanUseConnectedSteamStoreAccount();
            var myWishlistItems = steamStoreService != null && canUseConnectedSteamStoreAccount
                ? await steamStoreService.GetUserWishlistFromCacheOnlyAsync(GetSteamMyWishlistCacheKey(), language, region).ConfigureAwait(false)
                : new List<SteamStoreItem>();

            if (personalizationContext != null && canUseConnectedSteamStoreAccount)
            {
                foreach (var appId in (myWishlistItems ?? new List<SteamStoreItem>()).Select(x => x?.AppId ?? 0))
                {
                    if (appId > 0)
                    {
                        personalizationContext.WishlistSteamAppIds.Add(appId);
                    }
                }
            }

            var newReleaseItems = steamUpcomingGamesService.LoadNewReleasesFromCacheOnly(language, region)
                .Select(ConvertUpcomingToStoreItem)
                .Where(x => x != null)
                .ToList();

            var recommendedItems = new List<SteamStoreItem>();
            if (canUseConnectedSteamStoreAccount && recommendationProfile != null && steamStoreRecommendationService != null)
            {
                // Strict For You cache-only mode:
                // load only the real Steam /recommender/ cache. No old profile cache, no keyword cache, no fake fallback.
                recommendedItems = steamStoreRecommendationService.LoadFromCacheOnly(GetSteamForYouCacheKey(recommendationProfile), language, region)
                    ?? new List<SteamStoreItem>();

                // Quiet cache-only load; repeated Hub/Store bindings should not spam logs.
            }
            else if (!canUseConnectedSteamStoreAccount)
            {
                // Quiet cache-only skip; auth UI state already communicates this.
            }

            recommendedItems = (recommendedItems ?? new List<SteamStoreItem>()).Take(20).ToList();
            ApplySteamStoreFlags(recommendedItems, personalizationContext);

            await Task.WhenAll(dealsTask, topSellersTask);

            var personalizedMyWishlistItems = PersonalizeSteamStoreSection(
                myWishlistItems,
                personalizationContext,
                "MyWishlist",
                20);

            OnUi(() =>
            {
                ReplaceSteamStoreCollection(Settings.SteamStoreDeals, PersonalizeSteamStoreSection(dealsTask.Result, personalizationContext, "Deals", 24));
                ReplaceSteamStoreCollection(Settings.SteamStoreNewReleases, PersonalizeSteamStoreSection(newReleaseItems, personalizationContext, "New", 24));
                ReplaceSteamStoreCollection(Settings.SteamStoreTopSellers, PersonalizeSteamStoreSection(topSellersTask.Result, personalizationContext, "TopSellers", 24));
                ReplaceSteamStoreCollection(Settings.SteamStoreUpcoming, PersonalizeSteamStoreSection(upcomingItems, personalizationContext, "Upcoming", 24));
                ReplaceSteamStoreCollection(Settings.SteamStoreWishlisted, PersonalizeSteamStoreSection(wishlistedItems, personalizationContext, "Wishlisted", 24));

                // Cache-only loading must never erase already visible authenticated data.
                // An empty cache can simply mean that authentication was not ready yet or that
                // the cache file has not been written. A confirmed logout is handled elsewhere.
                if (canUseConnectedSteamStoreAccount)
                {
                    if ((personalizedMyWishlistItems?.Count ?? 0) > 0 || Settings.SteamStoreMyWishlist.Count == 0)
                    {
                        ReplaceSteamStoreCollection(Settings.SteamStoreMyWishlist, personalizedMyWishlistItems);
                    }

                    if ((recommendedItems?.Count ?? 0) > 0 || Settings.SteamStoreRecommended.Count == 0)
                    {
                        ReplaceSteamStoreRecommendedCollections(recommendedItems);
                    }
                }

                UpdateSteamStoreActiveSection();
                UpdateSteamStoreAvailabilityState();

                SaveSettingsSafe();
            });
        }

        private Task PreloadSteamStoreBackgroundsAsync(List<SteamStoreItem> items, string language, string region)
        {
            // Kept as a no-op for compatibility with older call sites.
            // Store lists must not download heavy background/screenshots anymore;
            // OpenSteamStoreDetails performs the media download on demand.
            return Task.CompletedTask;
        }

        private async Task RefreshSteamStoreAllAsync(bool manageLoading = true, bool allowVisibleStoreUiRebuild = true)
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

            if (manageLoading)
            {
                OnUi(() =>
                {
                    Settings.SteamStoreLoading = true;
                    Settings.SteamStoreError = string.Empty;
                });
            }
            else
            {
                OnUi(() =>
                {
                    Settings.SteamStoreError = string.Empty;
                });
            }

            try
            {
                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();
                var personalizationContext = await BuildSteamStorePersonalizationContextAsync().ConfigureAwait(false);

                var dealsTask = steamStoreService.GetDealsAsync(language, region, TimeSpan.FromDays(1));
                var topSellersTask = steamStoreService.GetTopSellersAsync(language, region, TimeSpan.FromDays(1));

                await Task.WhenAll(dealsTask, topSellersTask);

                // Backgrounds/screenshots are no longer preloaded for lists.
                // They are downloaded lazily when the details view opens.

                OnUi(() =>
                {
                    ReplaceSteamStoreCollection(Settings.SteamStoreDeals, PersonalizeSteamStoreSection(dealsTask.Result, personalizationContext, "Deals", 24));
                    ReplaceSteamStoreCollection(Settings.SteamStoreTopSellers, PersonalizeSteamStoreSection(topSellersTask.Result, personalizationContext, "TopSellers", 24));
                    UpdateSteamStoreActiveSectionIfAllowed(allowVisibleStoreUiRebuild);

                    var hasAnyData =
                        Settings.SteamStoreDeals.Count > 0 ||
                        Settings.SteamStoreTopSellers.Count > 0 ||
                        Settings.SteamStoreNewReleases.Count > 0 ||
                        Settings.SteamStoreUpcoming.Count > 0 ||
                        Settings.SteamStoreWishlisted.Count > 0 ||
                        Settings.SteamStoreRecommended.Count > 0;

                    Settings.SteamStoreAvailable = hasAnyData;
                    Settings.SteamStoreError = hasAnyData ? string.Empty : "No store data available";

                    if (manageLoading)
                    {
                        Settings.SteamStoreLoading = false;
                    }

                    SaveSettingsSafe();
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] RefreshSteamStoreAllAsync failed.");

                OnUi(() =>
                {
                    if (manageLoading)
                    {
                        Settings.SteamStoreLoading = false;
                        Settings.SteamStoreAvailable = false;
                        Settings.SteamStoreError = "Error while loading Steam Store";
                    }
                    else
                    {
                        Settings.SteamStoreError = "Error while loading Steam Store";
                    }
                });
            }
        }

        private void SetSteamStoreLoadingProgress(int progress)
        {
            if (Settings == null)
            {
                return;
            }

            var targetProgress = Math.Max(0, Math.Min(100, progress));
            var animationToken = System.Threading.Interlocked.Increment(ref steamStoreProgressAnimationToken);

            _ = AnimateSteamStoreLoadingProgressAsync(targetProgress, animationToken);
        }

        private async Task AnimateSteamStoreLoadingProgressAsync(int targetProgress, int animationToken)
        {
            try
            {
                while (Settings != null)
                {
                    if (animationToken != steamStoreProgressAnimationToken)
                    {
                        return;
                    }

                    var currentProgress = Settings.SteamStoreLoadingProgress;

                    if (currentProgress >= targetProgress)
                    {
                        return;
                    }

                    var remaining = targetProgress - currentProgress;

                    var step = remaining > 25
                        ? 2
                        : 1;

                    var nextProgress = Math.Min(targetProgress, currentProgress + step);

                    OnUi(() =>
                    {
                        if (Settings != null)
                        {
                            Settings.SteamStoreLoadingProgress = nextProgress;
                        }
                    });

                    await Task.Delay(35);
                }
            }
            catch
            {
            }
        }

        public async Task OnSteamStoreViewOpenedAsync()
        {
            if (Settings?.SteamStoreEnabled != true)
            {
                return;
            }

            // Important:
            // XAML bindings can call this several times while the Store view is visible.
            // We do not queue refresh calls. If one refresh is already running, we ignore the new call.
            if (!await steamStoreOpenLock.WaitAsync(0))
            {
                return;
            }

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

                await LoadSteamStoreCacheOnlyAsync();

                // Hub bindings can request Store data too. In that case we only load the cache.
                // Do not start the real Store-view throttle here, otherwise opening the Store
                // shortly after the Hub can suppress its network refresh for 30 seconds.
                if (!IsSteamStoreViewVisible())
                {
                    return;
                }

                var nowUtc = DateTime.UtcNow;
                if (HasSteamStoreCacheUiData() && (nowUtc - lastSteamStoreOpenRequestUtc) < SteamStoreOpenRequestThrottle)
                {
                    return;
                }

                lastSteamStoreOpenRequestUtc = nowUtc;

                var language = GetResolvedSteamStoreLanguage();
                var region = GetResolvedSteamStoreRegion();
                var recommendationProfile = BuildSteamRecommendationProfile();
                var personalizationContext = await BuildSteamStorePersonalizationContextAsync().ConfigureAwait(false);

                var storeMustRefresh = await steamStoreService.IsAnyStoreCacheMissingOrExpiredAsync(
                    language,
                    region,
                    TimeSpan.FromDays(1)
                );

                var upcomingMustRefresh = steamUpcomingGamesService.IsCacheMissingOrExpired(
                    language,
                    region,
                    TimeSpan.FromDays(1)
                );

                var wishlistedMustRefresh = steamUpcomingGamesService.IsWishlistedCacheMissingOrExpired(
                    language,
                    region,
                    TimeSpan.FromDays(1)
                );

                var newReleasesMustRefresh = steamUpcomingGamesService.IsNewReleasesCacheMissingOrExpired(
                    language,
                    region,
                    TimeSpan.FromDays(1)
                );

                var canUseConnectedSteamStoreAccount = CanUseConnectedSteamStoreAccount();

                var myWishlistMustRefresh = steamStoreService != null &&
                    canUseConnectedSteamStoreAccount &&
                    await steamStoreService.IsUserWishlistCacheMissingOrExpiredAsync(
                        GetSteamMyWishlistCacheKey(),
                        language,
                        region,
                        TimeSpan.FromDays(1)
                    ).ConfigureAwait(false);

                var recommendedCacheKey = GetSteamForYouCacheKey(recommendationProfile);

                var recommendedMustRefresh = canUseConnectedSteamStoreAccount &&
                    recommendationProfile != null &&
                    (steamStoreRecommendationService == null ||
                     steamStoreRecommendationService.IsCacheMissingOrExpired(
                        recommendedCacheKey,
                        language,
                        region,
                        TimeSpan.FromDays(1)
                    ));

                // The saved flag only says that a Steam account was connected previously.
                // Before loading personalized Store pages, verify the real Store-domain CEF
                // session now. Community cookies are deliberately not accepted here.
                if ((myWishlistMustRefresh || recommendedMustRefresh) &&
                    steamAccountSessionService != null)
                {
                    try
                    {
                        var storeSession = await steamAccountSessionService
                            .ProbeAsync(CancellationToken.None)
                            .ConfigureAwait(false);

                        ApplySteamAccountSession(
                            storeSession,
                            storeSession?.IsConnected == true
                                ? "Steam Store session is connected."
                                : "Steam Store session is not connected.",
                            allowVisibleStoreUiRebuild: false
                        );

                        canUseConnectedSteamStoreAccount = CanUseConnectedSteamStoreAccount();
                        if (!canUseConnectedSteamStoreAccount)
                        {
                            myWishlistMustRefresh = false;
                            recommendedMustRefresh = false;
                            logger.Info("[Steam Store] personal refresh skipped | Store-domain session is not connected.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // A network/CEF failure is not a logout. Keep the current cache and do
                        // not start the two personal refreshes with an unverified session.
                        logger.Warn(ex, "[Steam Store] Store session validation failed; personal cache preserved.");
                        myWishlistMustRefresh = false;
                        recommendedMustRefresh = false;
                    }
                }

                if (!storeMustRefresh && !upcomingMustRefresh && !wishlistedMustRefresh && !myWishlistMustRefresh && !newReleasesMustRefresh && !recommendedMustRefresh)
                {
                    return;
                }

                OnUi(() =>
                {
                    Settings.SteamStoreLoading = true;
                    Settings.SteamStoreError = string.Empty;
                    Settings.SteamStoreLoadingProgress = 5;
                });

                try
                {
                    logger.Info(
                        $"[AnikiHelper] Steam Store refresh START | Store={storeMustRefresh} | Upcoming={upcomingMustRefresh} | MostWishlisted={wishlistedMustRefresh} | MyWishlist={myWishlistMustRefresh} | New={newReleasesMustRefresh} | Recommended={recommendedMustRefresh} | Lang={language} | Region={region}"
                    );

                    var refreshTasks = new List<Task>();

                    var totalRefreshSteps = 0;

                    if (storeMustRefresh)
                    {
                        totalRefreshSteps++;
                    }

                    if (upcomingMustRefresh)
                    {
                        totalRefreshSteps++;
                    }

                    if (wishlistedMustRefresh)
                    {
                        totalRefreshSteps++;
                    }

                    if (myWishlistMustRefresh)
                    {
                        totalRefreshSteps++;
                    }

                    if (newReleasesMustRefresh)
                    {
                        totalRefreshSteps++;
                    }

                    if (recommendedMustRefresh)
                    {
                        totalRefreshSteps++;
                    }

                    var completedRefreshSteps = 0;
                    var progressLock = new object();

                    Action reportRefreshStepDone = () =>
                    {
                        int completed;

                        lock (progressLock)
                        {
                            completedRefreshSteps++;
                            completed = completedRefreshSteps;
                        }

                        var progress = totalRefreshSteps <= 0
                            ? 100
                            : Math.Min(95, 10 + (int)Math.Round((completed / (double)totalRefreshSteps) * 85));

                        SetSteamStoreLoadingProgress(progress);
                    };

                    SetSteamStoreLoadingProgress(10);

                    if (storeMustRefresh)
                    {
                        refreshTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await RefreshSteamStoreAllAsync(false, false);
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Steam Store classic refresh failed.");
                            }
                            finally
                            {
                                reportRefreshStepDone();
                            }
                        }));
                    }

                    if (upcomingMustRefresh)
                    {
                        refreshTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var upcoming = await steamUpcomingGamesService.RefreshAsync(language, region);

                                var upcomingStoreItems = PersonalizeSteamStoreSection(
                                    upcoming
                                        .Select(ConvertUpcomingToStoreItem)
                                        .Where(x => x != null),
                                    personalizationContext,
                                    "Upcoming",
                                    24);

                                OnUi(() =>
                                {
                                    ReplaceSteamStoreCollection(Settings.SteamStoreUpcoming, upcomingStoreItems);
                                    UpdateSteamStoreActiveSectionIfAllowed(false);
                                });
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Steam Upcoming refresh failed.");
                            }
                            finally
                            {
                                reportRefreshStepDone();
                            }
                        }));
                    }

                    if (wishlistedMustRefresh)
                    {
                        refreshTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var wishlisted = await steamUpcomingGamesService.RefreshWishlistedAsync(language, region);

                                var wishlistedStoreItems = PersonalizeSteamStoreSection(
                                    wishlisted
                                        .Select(ConvertUpcomingToStoreItem)
                                        .Where(x => x != null),
                                    personalizationContext,
                                    "Wishlisted",
                                    24);

                                OnUi(() =>
                                {
                                    ReplaceSteamStoreCollection(Settings.SteamStoreWishlisted, wishlistedStoreItems);
                                    UpdateSteamStoreActiveSectionIfAllowed(false);
                                });
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Steam Wishlisted refresh failed.");
                            }
                            finally
                            {
                                reportRefreshStepDone();
                            }
                        }));
                    }

                    if (myWishlistMustRefresh)
                    {
                        refreshTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var myWishlistStoreItems = await RefreshSteamStoreMyWishlistFromSteamAsync(
                                    personalizationContext,
                                    language,
                                    region,
                                    progress => SetSteamStoreLoadingProgress(progress),
                                    allowVisibleStoreUiRebuild: false
                                ).ConfigureAwait(false);

                                OnUi(() =>
                                {
                                    if (myWishlistStoreItems != null)
                                    {
                                        ReplaceSteamStoreCollection(Settings.SteamStoreMyWishlist, myWishlistStoreItems);
                                    }
                                    else
                                    {
                                        logger.Info("[Steam My Wishlist] refresh failed | current collection preserved.");
                                    }

                                    UpdateSteamStoreActiveSectionIfAllowed(false);
                                });
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Steam My Wishlist refresh failed.");
                            }
                            finally
                            {
                                reportRefreshStepDone();
                            }
                        }));
                    }

                    if (newReleasesMustRefresh)
                    {
                        refreshTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var newReleases = await steamUpcomingGamesService.RefreshNewReleasesAsync(language, region);

                                var newReleaseStoreItems = PersonalizeSteamStoreSection(
                                    newReleases
                                        .Select(ConvertUpcomingToStoreItem)
                                        .Where(x => x != null),
                                    personalizationContext,
                                    "New",
                                    24);

                                OnUi(() =>
                                {
                                    ReplaceSteamStoreCollection(Settings.SteamStoreNewReleases, newReleaseStoreItems);
                                    UpdateSteamStoreActiveSectionIfAllowed(false);
                                });
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Steam New Releases refresh failed.");
                            }
                            finally
                            {
                                reportRefreshStepDone();
                            }
                        }));
                    }

                    if (recommendedMustRefresh && recommendationProfile != null)
                    {
                        refreshTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var recommendedStoreItems = await RefreshSteamStoreForYouFromSteamAsync(
                                    personalizationContext,
                                    recommendationProfile,
                                    language,
                                    region,
                                    progress => SetSteamStoreLoadingProgress(progress),
                                    allowVisibleStoreUiRebuild: false
                                ).ConfigureAwait(false);

                                if (recommendedStoreItems == null)
                                {
                                    logger.Info("[Steam Recommender] refresh failed | current collection preserved.");

                                    OnUi(() =>
                                    {
                                        UpdateSteamStoreActiveSectionIfAllowed(false);
                                    });
                                }
                                else
                                {
                                    recommendedStoreItems = recommendedStoreItems.Take(20).ToList();
                                    ApplySteamStoreFlags(recommendedStoreItems, personalizationContext);

                                    OnUi(() =>
                                    {
                                        ReplaceSteamStoreRecommendedCollections(recommendedStoreItems);
                                        UpdateSteamStoreActiveSectionIfAllowed(false);
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, "[AnikiHelper] Steam Recommended refresh failed.");
                            }
                            finally
                            {
                                reportRefreshStepDone();
                            }
                        }));
                    }

                    await Task.WhenAll(refreshTasks);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] OnSteamStoreViewOpenedAsync global refresh failed.");

                    OnUi(() =>
                    {
                        Settings.SteamStoreError = "Error while loading Steam Store";
                    });
                }
                finally
                {
                    OnUi(() =>
                    {
                        UpdateSteamStoreActiveSectionIfAllowed(false);
                        UpdateSteamStoreAvailabilityState();
                    });

                    SetSteamStoreLoadingProgress(100);

                    await Task.Delay(900);

                    OnUi(() =>
                    {
                        Settings.SteamStoreLoading = false;
                        SaveSettingsSafe();
                    });
                }
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

                var personalizationContext = await BuildSteamStorePersonalizationContextAsync().ConfigureAwait(false);
                var items = PersonalizeSteamStoreSection(
                    await steamStoreService.GetDealsAsync(language, region, TimeSpan.FromDays(1)).ConfigureAwait(false),
                    personalizationContext,
                    "Deals",
                    24);

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

                var personalizationContext = await BuildSteamStorePersonalizationContextAsync().ConfigureAwait(false);
                var items = PersonalizeSteamStoreSection(
                    await steamStoreService.GetTopSellersAsync(language, region, TimeSpan.FromDays(1)).ConfigureAwait(false),
                    personalizationContext,
                    "TopSellers",
                    24);

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
            anikiWindowManager?.OpenWindow(parameter);
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

        private ButtonBase FindButtonInOpenWindows(string buttonName)
        {
            if (string.IsNullOrWhiteSpace(buttonName))
            {
                return null;
            }

            var app = Application.Current;
            if (app == null)
            {
                return null;
            }

            foreach (var window in app.Windows.OfType<Window>())
            {
                if (window == null)
                {
                    continue;
                }

                var button = FindVisualChildByName<ButtonBase>(window, buttonName);
                if (button != null)
                {
                    return button;
                }
            }

            return null;
        }

        private static void ExecuteButton(ButtonBase button)
        {
            if (button == null)
            {
                return;
            }

            if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
            {
                button.Command.Execute(button.CommandParameter);
                return;
            }

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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

                Action openAction = () =>
                {
                    try
                    {
                        // Ferme visuellement le Quick Access.
                        // CloseTopWindow() fait déjà Hide() puis Close() en idle, donc c'est mieux que Close direct.
                        CloseTopWindow();

                        var button = FindButtonInOpenWindows("HiddenOpenAchievementsButton");

                        if (button == null)
                        {
                            logger.Warn("[AnikiHelper] HiddenOpenAchievementsButton was not found.");
                            return;
                        }

                        ExecuteButton(button);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] OpenAchievementsFromQuickAccess inner failed.");
                    }
                };

                if (dispatcher.CheckAccess())
                {
                    openAction();
                }
                else
                {
                    dispatcher.BeginInvoke(openAction, DispatcherPriority.Normal);
                }
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

        public void ExecuteMusicTransportCommand(string commandKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(commandKey))
                {
                    return;
                }

                var normalizedCommand = commandKey.Trim().ToLowerInvariant();

                // When the music player is hosted inside the in-game overlay, avoid going
                // through playnite:// URI dispatch first. URI dispatch can be unreliable
                // while a game owns focus. UniPlaySong exposes the same active-media
                // transport service in the Playnite process, so call it directly when possible.
                if (TryExecuteUniPlaySongActiveMediaCommand(normalizedCommand))
                {
                    return;
                }

                string uri = null;

                switch (normalizedCommand)
                {
                    case "previous":
                    case "prev":
                        uri = "playnite://uniplaysong/previous";
                        break;

                    case "play":
                        uri = "playnite://uniplaysong/play";
                        break;

                    case "pause":
                        uri = "playnite://uniplaysong/pause";
                        break;

                    case "playpause":
                    case "toggleplaypause":
                    case "playpausetoggle":
                    case "toggle":
                        uri = "playnite://uniplaysong/playpausetoggle";
                        break;

                    case "next":
                    case "skip":
                        uri = "playnite://uniplaysong/next";
                        break;

                    case "mute":
                    case "togglemute":
                        uri = "playnite://uniplaysong/togglemute";
                        break;

                    case "stop":
                        uri = "playnite://uniplaysong/stop";
                        break;

                    case "restart":
                        uri = "playnite://uniplaysong/restart";
                        break;
                }

                if (string.IsNullOrWhiteSpace(uri))
                {
                    logger.Warn($"[AnikiHelper] Unknown music transport command: {commandKey}");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper] Failed to execute music transport command: {commandKey}");
            }
        }

        private bool TryExecuteUniPlaySongActiveMediaCommand(string normalizedCommand)
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

                var activeMediaServiceField = uniPlaySongPlugin.GetType().GetField(
                    "_activeMediaService",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                var activeMediaService = activeMediaServiceField?.GetValue(uniPlaySongPlugin);

                if (activeMediaService == null)
                {
                    return false;
                }

                string methodName = null;

                switch (normalizedCommand)
                {
                    case "previous":
                    case "prev":
                        methodName = "Previous";
                        break;

                    case "playpause":
                    case "toggleplaypause":
                    case "playpausetoggle":
                    case "toggle":
                        methodName = "PlayPause";
                        break;

                    case "next":
                    case "skip":
                        methodName = "Next";
                        break;

                    case "mute":
                    case "togglemute":
                        methodName = "ToggleMute";
                        break;
                }

                if (string.IsNullOrWhiteSpace(methodName))
                {
                    return false;
                }

                var method = activeMediaService.GetType().GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (method == null || method.GetParameters().Length != 0)
                {
                    return false;
                }

                method.Invoke(activeMediaService, null);
                return true;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper] Failed to execute UniPlaySong active media command: {normalizedCommand}");
                return false;
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
                        "ControllerShortcuts_Hub",
                        "ControllerShortcutsHub",
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
                    case "ControllerShortcutsHub":
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
                // 1) First condition: the active theme must include your XAML marker.
                var hasAnikiMarker = System.Windows.Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled;

                if (!hasAnikiMarker)
                {
                    return false;
                }

                // 2) Second condition: the active theme must keep Aniki branding and Mike Aniki credit.
                var activeThemeId = GetActiveThemeId();

                if (string.IsNullOrWhiteSpace(activeThemeId))
                {
                    return false;
                }

                // Avoid reading theme.yaml every time.
                if (cachedAnikiThemeAuthorization.HasValue &&
                    string.Equals(cachedAnikiThemeId, activeThemeId, StringComparison.OrdinalIgnoreCase))
                {
                    return cachedAnikiThemeAuthorization.Value;
                }

                string reason;
                var isAuthorized = IsAnikiThemeManifestAuthorized(activeThemeId, out reason);

                cachedAnikiThemeId = activeThemeId;
                cachedAnikiThemeAuthorization = isAuthorized;

                if (isAuthorized)
                {
                    DebugLog("[AnikiHelper] Aniki theme authorization OK: " + reason);
                }
                else
                {
                    logger.Warn("[AnikiHelper] Advanced features disabled: " + reason);
                }

                return isAuthorized;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to check Aniki theme authorization.");
                return false;
            }
        }

        private string GetActiveThemeId()
        {
            try
            {
                if (PlayniteApi == null || PlayniteApi.ApplicationSettings == null)
                {
                    return null;
                }

                // Aniki Helper is made for fullscreen themes.
                // We always check the currently selected fullscreen theme.
                return PlayniteApi.ApplicationSettings.FullscreenTheme;
            }
            catch
            {
                return null;
            }
        }

        private bool IsAnikiThemeManifestAuthorized(string activeThemeId, out string reason)
        {
            reason = "Unknown authorization state.";

            try
            {
                var manifestPath = GetActiveThemeManifestPath(activeThemeId);

                if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                {
                    reason = "theme.yaml not found for active theme id: " + activeThemeId;
                    return false;
                }

                var manifest = Serialization.FromYamlFile<Dictionary<string, object>>(manifestPath);

                if (manifest == null)
                {
                    reason = "theme.yaml could not be read: " + manifestPath;
                    return false;
                }

                var id = GetManifestValue(manifest, "Id");
                var name = GetManifestValue(manifest, "Name");

                // Playnite normally uses "Author", but "Authors" is also accepted here
                // so the check stays tolerant if you ever change the metadata format.
                var author = GetManifestValue(manifest, "Author");

                if (string.IsNullOrWhiteSpace(author))
                {
                    author = GetManifestValue(manifest, "Authors");
                }

                var hasMikeAnikiCredit = ContainsIgnoreCase(author, RequiredAnikiAuthorCredit);
                var hasAnikiBrand = ContainsIgnoreCase(name, RequiredAnikiBrandName) ||
                                    ContainsIgnoreCase(id, RequiredAnikiBrandName);

                if (!hasMikeAnikiCredit)
                {
                    reason = "theme marker found, but Author does not contain " + RequiredAnikiAuthorCredit + ". Current Author: " + (author ?? "") + ".";
                    return false;
                }

                if (!hasAnikiBrand)
                {
                    reason = "theme marker found, but Name or Id does not contain " + RequiredAnikiBrandName + ". Current Name: " + (name ?? "") + ", Id: " + (id ?? "") + ".";
                    return false;
                }

                reason = "theme authorized. Name=" + (name ?? "") + ", Id=" + (id ?? "") + ", Author=" + (author ?? "") + ".";
                return true;
            }
            catch (Exception ex)
            {
                reason = "theme manifest authorization failed: " + ex.Message;
                return false;
            }
        }

        private string GetActiveThemeManifestPath(string activeThemeId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(activeThemeId) ||
                    PlayniteApi == null ||
                    PlayniteApi.ApplicationInfo == null ||
                    PlayniteApi.Paths == null)
                {
                    return null;
                }

                var roots = new List<string>();

                // Installed Playnite:
                // Usually the user themes are in ConfigurationPath.
                //
                // Portable Playnite:
                // ConfigurationPath is still the correct config root to check.
                roots.Add(PlayniteApi.Paths.ConfigurationPath);

                // Fallback:
                // Useful in case a theme is stored near the Playnite executable,
                // especially with portable/custom setups.
                roots.Add(PlayniteApi.Paths.ApplicationPath);

                var modeFolder = "Fullscreen";

                foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var themesFolder = Path.Combine(root, "Themes", modeFolder);

                    if (!Directory.Exists(themesFolder))
                    {
                        continue;
                    }

                    foreach (var themeDir in Directory.EnumerateDirectories(themesFolder))
                    {
                        var manifestPath = Path.Combine(themeDir, "theme.yaml");

                        if (!File.Exists(manifestPath))
                        {
                            continue;
                        }

                        try
                        {
                            var manifest = Serialization.FromYamlFile<Dictionary<string, object>>(manifestPath);
                            var id = GetManifestValue(manifest, "Id");

                            if (string.Equals(id, activeThemeId, StringComparison.OrdinalIgnoreCase))
                            {
                                return manifestPath;
                            }
                        }
                        catch
                        {
                            // Ignore invalid theme manifests and continue scanning.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to locate active theme manifest.");
            }

            return null;
        }

        private string GetManifestValue(Dictionary<string, object> manifest, string key)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            object value;

            if (manifest.TryGetValue(key, out value))
            {
                return value == null ? null : value.ToString();
            }

            foreach (var pair in manifest)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value == null ? null : pair.Value.ToString();
                }
            }

            return null;
        }

        private bool ContainsIgnoreCase(string value, string expectedText)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(expectedText) &&
                   value.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) >= 0;
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

        private void LibraryNewsRotationTimer_Tick(object sender, EventArgs e)
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

                RotateLibraryNewsIfNeeded();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] LibraryNewsRotationTimer_Tick failed.");
            }
        }

        private async Task StartLibraryNewsRotationWithDelayAsync(int delayMs)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (!IsAnikiThemeActive())
                {
                    return;
                }

                OnUi(() => libraryNewsRotationTimer?.Start());
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] StartLibraryNewsRotationWithDelayAsync failed.");
            }
        }

        private async Task RefreshHubLibraryNewsTargetedWithDelayAsync(int delayMs)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                await RefreshHubLibraryNewsTargetedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Delayed Hub Library News refresh failed.");
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
            var limit = DateTime.Now.AddDays(-14);

            // Top 3 : joué dans les 14 jours, tri Playtime -> PlayCount -> LastActivity.
            // Important: always compute this before the sticky RefGame return,
            // otherwise Recommended/For You silently falls back to a single reference game.
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

            // Si on a déjà un RefGame choisi aujourd'hui, on le réutilise,
            // but keep the freshly computed Top 3 for the recommendation profile.
            if (s.RefGameLastId != Guid.Empty && s.RefGameLastChangeDate.Date == today)
            {
                var cached = games.FirstOrDefault(g => g.Id == s.RefGameLastId)
                             ?? PlayniteApi.Database.Games.Get(s.RefGameLastId);

                if (cached != null)
                {
                    if (!top3.Any(g => g != null && g.Id == cached.Id))
                    {
                        // Keep sticky game available as fallback seed without replacing the real Top 3.
                        top3.Add(cached);
                    }

                    return cached;
                }

                // si jeu supprimé / plus dans la liste filtrée
                s.RefGameLastId = Guid.Empty;
            }

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

            if (refGame != null && !top3.Any(g => g != null && g.Id == refGame.Id))
            {
                top3.Add(refGame);
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

        private string ResolveVideoPath(string fileName)
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
                        fileName);

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
                        fileName);

                    if (File.Exists(themeVideo))
                    {
                        return themeVideo;
                    }
                }

                return Path.Combine(GetDataRoot(), "ShutdownVideo", fileName);
            }
            catch
            {
                return Path.Combine(GetDataRoot(), "ShutdownVideo", fileName);
            }
        }

        private string GetLuckyStartupVideoFileName()
        {
            try
            {
                if (Settings?.IsLuckyDay == true)
                {
                    switch (Settings.LuckyStyleIndex)
                    {
                        case 1:
                            return StartupLuckyDay1VideoFileName;
                        case 2:
                            return StartupLuckyDay2VideoFileName;
                    }
                }
            }
            catch { }

            return StartupVideoFileName;
        }

        private string GetLuckyShutdownVideoFileName()
        {
            try
            {
                if (Settings?.IsLuckyDay == true)
                {
                    switch (Settings.LuckyStyleIndex)
                    {
                        case 1:
                            return ShutdownLuckyDay1VideoFileName;
                        case 2:
                            return ShutdownLuckyDay2VideoFileName;
                    }
                }
            }
            catch { }

            return ShutdownVideoFileName;
        }

        private string GetStartupVideoPath()
        {
            var preferredFileName = GetLuckyStartupVideoFileName();

            if (!string.Equals(preferredFileName, StartupVideoFileName, StringComparison.OrdinalIgnoreCase))
            {
                var luckyVideoPath = ResolveVideoPath(preferredFileName);
                if (File.Exists(luckyVideoPath))
                {
                    return luckyVideoPath;
                }
            }

            return ResolveVideoPath(StartupVideoFileName);
        }

        private string GetShutdownVideoPath()
        {
            var preferredFileName = GetLuckyShutdownVideoFileName();

            if (!string.Equals(preferredFileName, ShutdownVideoFileName, StringComparison.OrdinalIgnoreCase))
            {
                var luckyVideoPath = ResolveVideoPath(preferredFileName);
                if (File.Exists(luckyVideoPath))
                {
                    return luckyVideoPath;
                }
            }

            return ResolveVideoPath(ShutdownVideoFileName);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct GameReadyWindowRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out GameReadyWindowRect lpRect);


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

                // Cache Playnite AVANT d’afficher l’overlay vidéo.
                // Ça évite le flash de l’interface au début de la vidéo.
                HidePlayniteWindowsForStartup(null);

                overlay = new AnikiVideoOverlayWindow(videoPath, StartupVideoFailSafeTimeout);
                overlay.Show();
                overlay.Activate();

                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // Sécurité : si une fenêtre Playnite apparaît après le premier hide,
                // on la recache, mais on ne cache pas l’overlay vidéo.
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
                var isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
                DebugLog($"[AnikiHelper][OnApplicationStarted] Fullscreen check took {sw.ElapsedMilliseconds}ms | fullscreen={isFullscreen}");

                if (!isFullscreen)
                {
                    sw.Restart();
                    eventSoundService.PlayApplicationStarted();
                    DebugLog($"[AnikiHelper][OnApplicationStarted] PlayApplicationStarted took {sw.ElapsedMilliseconds}ms");

                    DebugLog($"[AnikiHelper][OnApplicationStarted] STOP not fullscreen | total={swTotal.ElapsedMilliseconds}ms");
                    return;
                }

                sw.Restart();
                var isAnikiThemeActive = IsAnikiThemeActive();
                DebugLog($"[AnikiHelper][OnApplicationStarted] IsAnikiThemeActive took {sw.ElapsedMilliseconds}ms | active={isAnikiThemeActive}");

                var delayApplicationStartedSoundUntilAfterVideo = isAnikiThemeActive && Settings.StartupIntroVideoEnabled;

                if (!delayApplicationStartedSoundUntilAfterVideo)
                {
                    sw.Restart();
                    eventSoundService.PlayApplicationStarted();
                    DebugLog($"[AnikiHelper][OnApplicationStarted] PlayApplicationStarted took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    InitializeLuckyDaySession();
                }

                // IMPORTANT: keep theme settings/options early.
                // Delaying this makes the UI appear with wrong theme/options during startup.
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
                        steamFriendsService?.Start();
                        DebugLog($"[AnikiHelper][OnApplicationStarted] steamFriendsService.Start took {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to start Steam Friends service.");
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
                hubStartupCachePrimed = false;
                DebugLog($"[AnikiHelper][OnApplicationStarted] hubPage3CardsInitialized reset took {sw.ElapsedMilliseconds}ms");

                QueueWelcomeHubCriticalCachePrime(isAnikiThemeActive);
                QueueStartupFocusRecovery(isAnikiThemeActive);

                sw.Restart();
                PlayniteApi.Database.DatabaseOpened += (_, __) =>
                {
                    QueueDatabaseOpenedStatsRefresh();
                };
                DebugLog($"[AnikiHelper][OnApplicationStarted] DatabaseOpened handler attach took {sw.ElapsedMilliseconds}ms");

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

                // Small early UI service. Keep this before the delayed block because the theme can use it for layout bindings.
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

                if (isAnikiThemeActive && Settings.ShutdownVideoEnabled)
                {
                    sw.Restart();
                    FullscreenShutdownVideoHook.Start(this);
                    DebugLog($"[AnikiHelper][OnApplicationStarted] FullscreenShutdownVideoHook.Start took {sw.ElapsedMilliseconds}ms");
                }

                // Queue the startup video before the heavy non-critical startup work.
                // If the intro option is disabled, no black video overlay is created here.
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
                            finally
                            {
                                try
                                {
                                    var swSound = Stopwatch.StartNew();
                                    eventSoundService.PlayApplicationStarted();
                                    DebugLog($"[AnikiHelper][OnApplicationStarted][StartupVideo] PlayApplicationStarted after video took {swSound.ElapsedMilliseconds}ms");
                                }
                                catch (Exception ex)
                                {
                                    logger.Warn(ex, "[AnikiHelper] Startup sound after video failed.");
                                }
                            }
                        },
                        System.Windows.Threading.DispatcherPriority.Send
                    );
                    DebugLog($"[AnikiHelper][OnApplicationStarted] Startup video InvokeAsync queued took {sw.ElapsedMilliseconds}ms");
                }

                QueuePostStartupNonCriticalWork(isAnikiThemeActive);

                DebugLog($"[AnikiHelper][OnApplicationStarted] END queued non-critical work | total={swTotal.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper][OnApplicationStarted] FATAL ERROR after {swTotal.ElapsedMilliseconds}ms");
            }
        }

        private void QueueStartupFocusRecovery(bool isAnikiThemeActive)
        {
            try
            {
                if (!isAnikiThemeActive)
                {
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        // Do not fight the Aniki startup video. It already restores Playnite focus when it closes.
                        if (Settings?.StartupIntroVideoEnabled == true)
                        {
                            await Task.Delay((int)StartupVideoDuration.TotalMilliseconds + 250);
                        }

                        // Handheld shells / AnyFSE / BootVid can briefly return Playnite visually without
                        // giving WPF a valid keyboard focus. Retry a few times instead of forcing a specific
                        // target such as the game list, because the theme may start on Login, Hub, or Library.
                        int[] delaysMs = new[] { 150, 700, 1500, 3000, 5000 };

                        for (int i = 0; i < delaysMs.Length; i++)
                        {
                            await Task.Delay(delaysMs[i]);
                            RestorePlayniteStartupFocus($"startup-recovery-{i + 1}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Startup focus recovery failed.");
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to queue startup focus recovery.");
            }
        }

        private void RestorePlayniteStartupFocus(string context)
        {
            try
            {
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (IsAnyAnikiModalOrOverlayWindowOpen())
                {
                    return;
                }

                var app = Application.Current;
                var main = app?.MainWindow;

                if (main == null || !main.IsVisible)
                {
                    return;
                }

                bool hasValidFocus = HasValidMainWindowKeyboardFocus(main);

                if (hasValidFocus && IsPlayniteForegroundWindow())
                {
                    return;
                }

                var handle = new WindowInteropHelper(main).Handle;

                try
                {
                    if (handle != IntPtr.Zero)
                    {
                        SetForegroundWindow(handle);
                    }
                }
                catch { }

                try
                {
                    main.Activate();
                    main.Focus();
                }
                catch { }

                // Last pass: if WPF still has no valid focused element, ask WPF to focus the first
                // visible/focusable element in the current screen. This is intentionally generic:
                // Login screen, Hub, and Library keep their own natural focus target.
                try
                {
                    if (!HasValidMainWindowKeyboardFocus(main))
                    {
                        if (main.Content is FrameworkElement root)
                        {
                            root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                        }
                        else
                        {
                            main.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                        }
                    }
                }
                catch { }

                DebugLog($"[AnikiHelper][StartupFocusRecovery] {context} | restored | foreground={IsPlayniteForegroundWindow()} | validFocus={HasValidMainWindowKeyboardFocus(main)}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper] Startup focus recovery pass failed ({context}).");
            }
        }

        private bool HasValidMainWindowKeyboardFocus(Window main)
        {
            try
            {
                if (main == null || !main.IsVisible || !main.IsKeyboardFocusWithin)
                {
                    return false;
                }

                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused == null)
                {
                    return false;
                }

                return IsDescendantOrSelf(focused, main);
            }
            catch
            {
                return false;
            }
        }

        private bool IsDescendantOrSelf(DependencyObject current, DependencyObject target)
        {
            try
            {
                while (current != null)
                {
                    if (ReferenceEquals(current, target))
                    {
                        return true;
                    }

                    DependencyObject parent = null;

                    try
                    {
                        parent = VisualTreeHelper.GetParent(current);
                    }
                    catch
                    {
                        parent = null;
                    }

                    if (parent == null && current is FrameworkElement element)
                    {
                        parent = element.Parent;
                    }

                    if (parent == null && current is FrameworkContentElement contentElement)
                    {
                        parent = contentElement.Parent as DependencyObject;
                    }

                    current = parent;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsAnyAnikiModalOrOverlayWindowOpen()
        {
            try
            {
                return Application.Current?.Windows
                    .OfType<Window>()
                    .Any(w =>
                    {
                        if (w == null || !w.IsVisible || ReferenceEquals(w, Application.Current.MainWindow))
                        {
                            return false;
                        }

                        if (w is AnikiVideoOverlayWindow)
                        {
                            return true;
                        }

                        var typeName = w.GetType().FullName ?? string.Empty;

                        return w.Tag is string ||
                               typeName.IndexOf("SettingsWindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               typeName.IndexOf("TextInputWindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               typeName.IndexOf("Aniki", StringComparison.OrdinalIgnoreCase) >= 0;
                    }) == true;
            }
            catch
            {
                return false;
            }
        }

        private string GetWelcomeHubStartupCachePath()
        {
            return Path.Combine(GetDataRoot(), "Hub Cache", "WelcomeHubStartupCache.json");
        }

        private static void ReplaceObservableCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();

            if (source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                if (item != null)
                {
                    target.Add(item);
                }
            }
        }

        private void LoadWelcomeHubStartupCacheFast()
        {
            try
            {
                if (hubStartupVisibleCacheLoaded || Settings == null)
                {
                    return;
                }

                var path = GetWelcomeHubStartupCachePath();

                if (!File.Exists(path))
                {
                    DebugLog("[AnikiHelper][HubStartupCache] No startup cache found.");
                    return;
                }

                var cache = Serialization.FromJsonFile<WelcomeHubStartupCache>(path);

                if (cache == null)
                {
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;

                Action apply = () =>
                {
                    try
                    {
                        var s = Settings;

                        if (s == null)
                        {
                            return;
                        }

                        s.TotalCount = cache.TotalCount;
                        s.InstalledCount = cache.InstalledCount;
                        s.NotInstalledCount = cache.NotInstalledCount;
                        s.HiddenCount = cache.HiddenCount;
                        s.FavoriteCount = cache.FavoriteCount;

                        s.TotalPlaytimeMinutes = cache.TotalPlaytimeMinutes;
                        s.AveragePlaytimeMinutes = cache.AveragePlaytimeMinutes;

                        s.ProfileGenreKey = cache.ProfileGenreKey ?? string.Empty;
                        s.ProfileGenreLabel = cache.ProfileGenreLabel ?? string.Empty;
                        s.ProfileTopPlatformName = cache.ProfileTopPlatformName ?? string.Empty;
                        s.ProfileTopFranchiseName = cache.ProfileTopFranchiseName ?? string.Empty;
                        s.ProfileTopTagName = cache.ProfileTopTagName ?? string.Empty;

                        s.ThisMonthPlayedCount = cache.ThisMonthPlayedCount;
                        s.ThisMonthPlayedTotalMinutes = cache.ThisMonthPlayedTotalMinutes;
                        s.ThisMonthTopGameName = cache.ThisMonthTopGameName ?? string.Empty;
                        s.ThisMonthTopGamePlaytime = cache.ThisMonthTopGamePlaytime ?? string.Empty;
                        s.ThisMonthTopGameCoverPath = cache.ThisMonthTopGameCoverPath ?? string.Empty;
                        s.ThisMonthTopGameBackgroundPath = cache.ThisMonthTopGameBackgroundPath ?? string.Empty;
                        s.ThisMonthTopGameId = cache.ThisMonthTopGameId;

                        s.ThisYearPlayedCount = cache.ThisYearPlayedCount;
                        s.ThisYearPlayedTotalMinutes = cache.ThisYearPlayedTotalMinutes;
                        s.ThisYearTopGameName = cache.ThisYearTopGameName ?? string.Empty;
                        s.ThisYearTopGamePlaytime = cache.ThisYearTopGamePlaytime ?? string.Empty;
                        s.ThisYearTopGameCoverPath = cache.ThisYearTopGameCoverPath ?? string.Empty;
                        s.ThisYearTopGameBackgroundPath = cache.ThisYearTopGameBackgroundPath ?? string.Empty;
                        s.ThisYearTopGameId = cache.ThisYearTopGameId;

                        s.RecentPlayedBackgroundPath = cache.RecentPlayedBackgroundPath ?? string.Empty;

                        s.HubRecentAddedName = cache.HubRecentAddedName ?? string.Empty;
                        s.HubRecentAddedDate = cache.HubRecentAddedDate ?? string.Empty;
                        s.HubRecentAddedBackgroundPath = cache.HubRecentAddedBackgroundPath ?? string.Empty;
                        s.HubRecentAddedGameId = cache.HubRecentAddedGameId;

                        s.HubNeverPlayedName = cache.HubNeverPlayedName ?? string.Empty;
                        s.HubNeverPlayedDate = cache.HubNeverPlayedDate ?? string.Empty;
                        s.HubNeverPlayedBackgroundPath = cache.HubNeverPlayedBackgroundPath ?? string.Empty;
                        s.HubNeverPlayedGameId = cache.HubNeverPlayedGameId;

                        ReplaceObservableCollection(s.TopPlayed, cache.TopPlayed);
                        ReplaceObservableCollection(s.CompletionStates, cache.CompletionStates);
                        ReplaceObservableCollection(s.GameProviders, cache.GameProviders);
                        ReplaceObservableCollection(s.RecentPlayed, cache.RecentPlayed);
                        ReplaceObservableCollection(s.RecentAdded, cache.RecentAdded);
                        ReplaceObservableCollection(s.NeverPlayed, cache.NeverPlayed);
                        ReplaceObservableCollection(s.SteamRecentUpdates, cache.SteamRecentUpdates);

                        s.HubLibraryRecommendedGames =
                            new ObservableCollection<HubLibraryRecommendedGameItem>(
                                cache.HubLibraryRecommendedGames ?? new List<HubLibraryRecommendedGameItem>()
                            );

                        try
                        {
                            if (s.SteamGlobalNewsA != null && s.SteamGlobalNewsA.Count > 0)
                            {
                                UpdateLatestNewsRotationFromList(s.SteamGlobalNewsA.ToList());
                            }

                            if (s.SteamRecentUpdates != null && s.SteamRecentUpdates.Count > 0)
                            {
                                UpdateLibraryNewsRotationFromUpdates(s.SteamRecentUpdates.ToList());
                            }
                        }
                        catch { }

                        hubStartupVisibleCacheLoaded = true;

                        DebugLog($"[AnikiHelper][HubStartupCache] Visible cache loaded. SavedUtc={cache.SavedUtc:o}");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to apply welcome hub startup cache.");
                    }
                };

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(apply);
                }
                else
                {
                    apply();
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to load welcome hub startup cache.");
            }
        }

        private void SaveWelcomeHubStartupCacheSnapshotSafe()
        {
            try
            {
                if (Settings == null)
                {
                    return;
                }

                var s = Settings;

                var cache = new WelcomeHubStartupCache
                {
                    SavedUtc = DateTime.UtcNow,

                    TotalCount = s.TotalCount,
                    InstalledCount = s.InstalledCount,
                    NotInstalledCount = s.NotInstalledCount,
                    HiddenCount = s.HiddenCount,
                    FavoriteCount = s.FavoriteCount,

                    TotalPlaytimeMinutes = s.TotalPlaytimeMinutes,
                    AveragePlaytimeMinutes = s.AveragePlaytimeMinutes,

                    ProfileGenreKey = s.ProfileGenreKey,
                    ProfileGenreLabel = s.ProfileGenreLabel,
                    ProfileTopPlatformName = s.ProfileTopPlatformName,
                    ProfileTopFranchiseName = s.ProfileTopFranchiseName,
                    ProfileTopTagName = s.ProfileTopTagName,

                    ThisMonthPlayedCount = s.ThisMonthPlayedCount,
                    ThisMonthPlayedTotalMinutes = s.ThisMonthPlayedTotalMinutes,
                    ThisMonthTopGameName = s.ThisMonthTopGameName,
                    ThisMonthTopGamePlaytime = s.ThisMonthTopGamePlaytime,
                    ThisMonthTopGameCoverPath = s.ThisMonthTopGameCoverPath,
                    ThisMonthTopGameBackgroundPath = s.ThisMonthTopGameBackgroundPath,
                    ThisMonthTopGameId = s.ThisMonthTopGameId,

                    ThisYearPlayedCount = s.ThisYearPlayedCount,
                    ThisYearPlayedTotalMinutes = s.ThisYearPlayedTotalMinutes,
                    ThisYearTopGameName = s.ThisYearTopGameName,
                    ThisYearTopGamePlaytime = s.ThisYearTopGamePlaytime,
                    ThisYearTopGameCoverPath = s.ThisYearTopGameCoverPath,
                    ThisYearTopGameBackgroundPath = s.ThisYearTopGameBackgroundPath,
                    ThisYearTopGameId = s.ThisYearTopGameId,

                    RecentPlayedBackgroundPath = s.RecentPlayedBackgroundPath,

                    HubRecentAddedName = s.HubRecentAddedName,
                    HubRecentAddedDate = s.HubRecentAddedDate,
                    HubRecentAddedBackgroundPath = s.HubRecentAddedBackgroundPath,
                    HubRecentAddedGameId = s.HubRecentAddedGameId,

                    HubNeverPlayedName = s.HubNeverPlayedName,
                    HubNeverPlayedDate = s.HubNeverPlayedDate,
                    HubNeverPlayedBackgroundPath = s.HubNeverPlayedBackgroundPath,
                    HubNeverPlayedGameId = s.HubNeverPlayedGameId,

                    TopPlayed = s.TopPlayed?.ToList() ?? new List<TopPlayedItem>(),
                    CompletionStates = s.CompletionStates?.ToList() ?? new List<CompletionStatItem>(),
                    GameProviders = s.GameProviders?.ToList() ?? new List<ProviderStatItem>(),
                    RecentPlayed = s.RecentPlayed?.ToList() ?? new List<QuickItem>(),
                    RecentAdded = s.RecentAdded?.ToList() ?? new List<QuickItem>(),
                    NeverPlayed = s.NeverPlayed?.ToList() ?? new List<QuickItem>(),
                    SteamRecentUpdates = s.SteamRecentUpdates?.ToList() ?? new List<SteamRecentUpdateItem>(),
                    HubLibraryRecommendedGames = s.HubLibraryRecommendedGames?.ToList() ?? new List<HubLibraryRecommendedGameItem>()
                };

                _ = Task.Run(() =>
                {
                    try
                    {
                        var path = GetWelcomeHubStartupCachePath();
                        Directory.CreateDirectory(Path.GetDirectoryName(path));

                        var json = Serialization.ToJson(cache, true);
                        File.WriteAllText(path, json);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to write welcome hub startup cache.");
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to snapshot welcome hub startup cache.");
            }
        }

        private void QueueWelcomeHubCriticalCachePrime(bool isAnikiThemeActive)
        {
            try
            {
                if (!isAnikiThemeActive)
                {
                    return;
                }

                // 1) Affichage immédiat depuis le dernier cache connu.
                // Pas de gros recalcul ici.
                LoadWelcomeHubStartupCacheFast();

                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000).ConfigureAwait(false);
                        PrimeWelcomeHubCriticalCache();
                    });

                    return;
                }

                // 2) Recalcul plus tard, une fois que Playnite a eu le temps d'afficher l'UI.
                dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                        var delayMs = Settings?.StartupIntroVideoEnabled == true
                            ? (int)StartupVideoDuration.TotalMilliseconds + 500
                            : 1500;

                        await Task.Delay(delayMs);

                        await dispatcher.InvokeAsync(
                            () => PrimeWelcomeHubCriticalCache(),
                            DispatcherPriority.ApplicationIdle
                        );
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Delayed welcome hub critical cache prime failed.");
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to queue welcome hub critical cache prime.");
            }
        }

        private void PrimeWelcomeHubCriticalCache()
        {
            if (hubStartupCachePrimed)
            {
                return;
            }

            var swTotal = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            try
            {
                DebugLog("[AnikiHelper][HubStartupPrime] START");

                hubPage3CardsInitialized = false;

                sw.Restart();
                RecalcStatsSafe();
                DebugLog($"[AnikiHelper][HubStartupPrime] RecalcStatsSafe took {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LoadNewsFromCacheIfNeeded();
                DebugLog($"[AnikiHelper][HubStartupPrime] LoadNewsFromCacheIfNeeded took {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                RefreshSteamRecentUpdatesFromCache();
                DebugLog($"[AnikiHelper][HubStartupPrime] RefreshSteamRecentUpdatesFromCache took {sw.ElapsedMilliseconds}ms");

                hubStartupCachePrimed = true;

                SaveWelcomeHubStartupCacheSnapshotSafe();

                DebugLog($"[AnikiHelper][HubStartupPrime] END total={swTotal.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Welcome hub critical cache prime failed.");
            }
        }

        private void QueueDatabaseOpenedStatsRefresh()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    RunDatabaseOpenedStatsRefresh();
                    return;
                }

                dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var delayMs = Settings?.StartupIntroVideoEnabled == true
                            ? (int)StartupVideoDuration.TotalMilliseconds + 1200
                            : 3000;

                        await Task.Delay(delayMs);
                        RunDatabaseOpenedStatsRefresh();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Delayed DatabaseOpened stats refresh failed.");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to queue DatabaseOpened stats refresh.");
            }
        }

        private void RunDatabaseOpenedStatsRefresh()
        {
            var swDb = Stopwatch.StartNew();
            DebugLog("[AnikiHelper][DatabaseOpened] START delayed");

            hubPage3CardsInitialized = false;
            DebugLog($"[AnikiHelper][DatabaseOpened] hubPage3CardsInitialized reset at {swDb.ElapsedMilliseconds}ms");

            try
            {
                Settings?.LoadOverlayApps();
                DebugLog($"[AnikiHelper][DatabaseOpened] LoadOverlayApps/HubApps at {swDb.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to refresh Software Tools for Hub Apps after database opened.");
            }

            EnsureMonthlySnapshotSafe();
            DebugLog($"[AnikiHelper][DatabaseOpened] EnsureMonthlySnapshotSafe at {swDb.ElapsedMilliseconds}ms");

            RecalcStatsSafe();
            DebugLog($"[AnikiHelper][DatabaseOpened] RecalcStatsSafe at {swDb.ElapsedMilliseconds}ms");

            SaveWelcomeHubStartupCacheSnapshotSafe();

            DebugLog($"[AnikiHelper][DatabaseOpened] END total={swDb.ElapsedMilliseconds}ms");
        }

        private void QueuePostStartupNonCriticalWork(bool isAnikiThemeActive)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    RunPostStartupNonCriticalWork(isAnikiThemeActive);
                    return;
                }

                dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var delayMs = isAnikiThemeActive ? 2500 : 500;

                        if (isAnikiThemeActive && Settings?.StartupIntroVideoEnabled == true)
                        {
                            delayMs = (int)StartupVideoDuration.TotalMilliseconds + 300;
                        }

                        DebugLog($"[AnikiHelper][PostStartup] queued | delay={delayMs}ms | startupVideo={Settings?.StartupIntroVideoEnabled}");

                        await Task.Delay(delayMs);
                        RunPostStartupNonCriticalWork(isAnikiThemeActive);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Delayed post-startup work failed.");
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to queue post-startup work.");
            }
        }

        private void RunPostStartupNonCriticalWork(bool isAnikiThemeActive)
        {
            var swTotal = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            DebugLog("[AnikiHelper][PostStartup] START");

            try
            {
                if (!hubStartupCachePrimed)
                {
                    hubPage3CardsInitialized = false;
                    DebugLog($"[AnikiHelper][PostStartup] hubPage3CardsInitialized reset took {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    DebugLog("[AnikiHelper][PostStartup] hub core cache already primed; keeping page 1 data alive.");
                }

                if (isAnikiThemeActive)
                {
                    try
                    {
                        sw.Restart();
                        Settings?.LoadOverlayApps();
                        DebugLog($"[AnikiHelper][PostStartup] LoadOverlayApps/HubApps took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to refresh Software Tools for Hub Apps during startup.");
                    }
                }

                sw.Restart();
                EnsureMonthlySnapshotSafe();
                DebugLog($"[AnikiHelper][PostStartup] EnsureMonthlySnapshotSafe took {sw.ElapsedMilliseconds}ms");

                if (!hubStartupCachePrimed)
                {
                    sw.Restart();
                    RecalcStatsSafe();
                    DebugLog($"[AnikiHelper][PostStartup] RecalcStatsSafe took {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    DebugLog("[AnikiHelper][PostStartup] RecalcStatsSafe skipped; hub data was loaded before login close.");
                }

                DebugLog("[AnikiHelper][PostStartup] Profile genre cache mode active.");

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    AddonsUpdateStyler.Start();
                    DebugLog($"[AnikiHelper][PostStartup] AddonsUpdateStyler.Start took {sw.ElapsedMilliseconds}ms");
                }

                sw.Restart();
                DebugLog("[AnikiHelper][PostStartup] Dynamic color init START");
                EnsureDynamicColorCacheVersion();
                DebugLog($"[AnikiHelper][PostStartup] EnsureDynamicColorCacheVersion at {sw.ElapsedMilliseconds}ms");
                DynamicAuto.Init(PlayniteApi);
                DebugLog($"[AnikiHelper][PostStartup] DynamicAuto.Init at {sw.ElapsedMilliseconds}ms");

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    SettingsWindowStyler.Start();
                    DebugLog($"[AnikiHelper][PostStartup] SettingsWindowStyler.Start took {sw.ElapsedMilliseconds}ms");

                    sw.Restart();
                    FastScrollViewerService.Start();
                    DebugLog($"[AnikiHelper][PostStartup] FastScrollViewerService.Start took {sw.ElapsedMilliseconds}ms");

                    sw.Restart();
                    VisualPackBackgroundComposer.Start();
                    DebugLog($"[AnikiHelper][PostStartup] VisualPackBackgroundComposer.Start took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    sw.Restart();
                    newsRotationTimer?.Start();
                    DebugLog($"[AnikiHelper][PostStartup] newsRotationTimer.Start took {sw.ElapsedMilliseconds}ms");

                    sw.Restart();
                    _ = StartLibraryNewsRotationWithDelayAsync(HubLibraryNewsRotationStartupDelayMs);
                    DebugLog($"[AnikiHelper][PostStartup] Hub news rotation timer scheduled from cache took {sw.ElapsedMilliseconds}ms");
                }

                if (isAnikiThemeActive)
                {
                    if (!hubStartupCachePrimed)
                    {
                        try
                        {
                            sw.Restart();
                            LoadNewsFromCacheIfNeeded();
                            DebugLog($"[AnikiHelper][PostStartup] LoadNewsFromCacheIfNeeded took {sw.ElapsedMilliseconds}ms");
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "[AnikiHelper] LoadNewsFromCacheIfNeeded failed.");
                        }

                        try
                        {
                            sw.Restart();
                            RefreshSteamRecentUpdatesFromCache();
                            _ = RefreshHubLibraryNewsTargetedWithDelayAsync(HubLibraryNewsBackgroundRefreshStartupDelayMs);
                            DebugLog($"[AnikiHelper][PostStartup] Hub news cache loaded + light background refresh scheduled took {sw.ElapsedMilliseconds}ms");
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "[AnikiHelper] RefreshSteamRecentUpdatesFromCache failed.");
                        }
                    }
                    else
                    {
                        try
                        {
                            sw.Restart();
                            _ = RefreshHubLibraryNewsTargetedWithDelayAsync(HubLibraryNewsBackgroundRefreshStartupDelayMs);
                            DebugLog($"[AnikiHelper][PostStartup] Hub cache already primed; background refresh scheduled took {sw.ElapsedMilliseconds}ms");
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "[AnikiHelper] Hub background refresh schedule failed.");
                        }
                    }

                    if (Settings.NewsScanEnabled)
                    {
                        try
                        {
                            sw.Restart();
                            _ = ScheduleGlobalSteamNewsRefreshAsync();
                            DebugLog($"[AnikiHelper][PostStartup] ScheduleGlobalSteamNewsRefreshAsync launch took {sw.ElapsedMilliseconds}ms");
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
                        DebugLog($"[AnikiHelper][PostStartup] SchedulePlayniteNewsRefreshAsync launch took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] SchedulePlayniteNewsRefreshAsync failed.");
                    }
                }

                try
                {
                    if (isAnikiThemeActive)
                    {
                        sw.Restart();
                        TryAskForSteamUpdateCacheOnStartup();
                        DebugLog($"[AnikiHelper][PostStartup] TryAskForSteamUpdateCacheOnStartup took {sw.ElapsedMilliseconds}ms");
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
                        DebugLog($"[AnikiHelper][PostStartup] ScheduleSteamRecentUpdatesScanAsync launch took {sw.ElapsedMilliseconds}ms");
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
                    DebugLog($"[AnikiHelper][PostStartup] CheckRequiredPluginVersionAfterFullscreenStartupAsync launch took {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] CheckRequiredPluginVersionAfterFullscreenStartupAsync failed.");
                }

                try
                {
                    sw.Restart();
                    _ = CheckWhatsNewAfterStartupAsync();
                    DebugLog($"[AnikiHelper][PostStartup] CheckWhatsNewAfterStartupAsync launch took {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] CheckWhatsNewAfterStartupAsync failed.");
                }

                if (isAnikiThemeActive)
                {
                    try
                    {
                        sw.Restart();
                        _ = ScheduleStartupSteamAuthCheckAsync();
                        DebugLog($"[AnikiHelper][PostStartup] ScheduleStartupSteamAuthCheckAsync launch took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] ScheduleStartupSteamAuthCheckAsync failed.");
                    }

                    try
                    {
                        sw.Restart();
                        _ = ScheduleStartupSteamNotificationRefreshAsync();
                        DebugLog($"[AnikiHelper][PostStartup] ScheduleStartupSteamNotificationRefreshAsync launch took {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] ScheduleStartupSteamNotificationRefreshAsync failed.");
                    }
                }

                DebugLog($"[AnikiHelper][PostStartup] END total={swTotal.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper][PostStartup] FATAL ERROR after {swTotal.ElapsedMilliseconds}ms");
            }
        }


        private bool HandleMediaGalleryRefreshShortcut(OnControllerButtonStateChangedArgs args)
        {
            try
            {
                if (args == null)
                {
                    return false;
                }

                if (args.State != ControllerInputState.Pressed)
                {
                    return false;
                }

                if (args.Button != ControllerInput.Y)
                {
                    return false;
                }

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return false;
                }

                if (!IsAnikiThemeActive())
                {
                    return false;
                }

                if (!IsMediaGalleryGamesWindowOpen())
                {
                    return false;
                }

                var now = DateTime.UtcNow;

                if ((now - lastMediaGalleryRefreshShortcutUtc).TotalMilliseconds < 600)
                {
                    return true;
                }

                lastMediaGalleryRefreshShortcutUtc = now;

                if (Settings != null)
                {
                    _ = Settings.RefreshMediaGalleryLibraryAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Media Gallery refresh shortcut failed.");
                return true;
            }
        }

        private bool IsMediaGalleryGamesWindowOpen()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    return dispatcher.Invoke(new Func<bool>(IsMediaGalleryGamesWindowOpen));
                }

                return Application.Current.Windows
                    .OfType<Window>()
                    .Any(w =>
                        w.IsVisible &&
                        string.Equals(w.Tag as string, "MediaGalleryGamesWindowStyle", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private bool HandleSteamStoreScreenshotViewerInput(OnControllerButtonStateChangedArgs args)
        {
            try
            {
                if (args == null)
                {
                    return false;
                }

                if (args.State != ControllerInputState.Pressed)
                {
                    return false;
                }

                // Important :
                // Si le viewer plein écran screenshot n'est pas ouvert,
                // on ne consomme PAS L1/R1, pour laisser la page Store changer d'onglet.
                if (Settings?.SteamStoreScreenshotViewerVisible != true)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(Settings.SteamStoreScreenshotViewerImage))
                {
                    return false;
                }

                if (Settings.SteamStoreDetailsVisible != true)
                {
                    return false;
                }

                if (args.Button == ControllerInput.B)
                {
                    CloseSteamStoreScreenshotViewer();
                    return true;
                }

                if (args.Button == ControllerInput.RightShoulder)
                {
                    return SwitchSteamStoreScreenshotViewerImage(1);
                }

                if (args.Button == ControllerInput.LeftShoulder)
                {
                    return SwitchSteamStoreScreenshotViewerImage(-1);
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Steam Store screenshot viewer input failed.");
                return false;
            }
        }

        private bool SwitchSteamStoreScreenshotViewerImage(int direction)
        {
            try
            {
                if (Settings == null)
                {
                    return false;
                }

                if (Settings.SteamStoreScreenshotViewerVisible != true)
                {
                    return false;
                }

                var currentIndex = GetSteamStoreScreenshotIndex(Settings.SteamStoreScreenshotViewerImage);
                var nextIndex = currentIndex + direction;

                if (nextIndex < 1)
                {
                    nextIndex = 5;
                }

                if (nextIndex > 5)
                {
                    nextIndex = 1;
                }

                var nextImage = GetSteamStoreScreenshotByIndex(nextIndex);

                for (var i = 0; i < 5 && string.IsNullOrWhiteSpace(nextImage); i++)
                {
                    nextIndex += direction;

                    if (nextIndex < 1)
                    {
                        nextIndex = 5;
                    }

                    if (nextIndex > 5)
                    {
                        nextIndex = 1;
                    }

                    nextImage = GetSteamStoreScreenshotByIndex(nextIndex);
                }

                nextImage = FirstSteamStoreImage(nextImage);

                if (string.IsNullOrWhiteSpace(nextImage))
                {
                    return false;
                }

                OnUi(() =>
                {
                    Settings.SteamStoreScreenshotViewerImage = nextImage;
                });

                return true;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to switch Steam Store screenshot viewer image.");
                return false;
            }
        }

        private int GetSteamStoreScreenshotIndex(string imagePath)
        {
            try
            {
                if (Settings == null || string.IsNullOrWhiteSpace(imagePath))
                {
                    return 1;
                }

                if (string.Equals(imagePath, Settings.SteamStoreDetailsScreenshot2, StringComparison.OrdinalIgnoreCase))
                {
                    return 2;
                }

                if (string.Equals(imagePath, Settings.SteamStoreDetailsScreenshot3, StringComparison.OrdinalIgnoreCase))
                {
                    return 3;
                }

                if (string.Equals(imagePath, Settings.SteamStoreDetailsScreenshot4, StringComparison.OrdinalIgnoreCase))
                {
                    return 4;
                }

                if (string.Equals(imagePath, Settings.SteamStoreDetailsScreenshot5, StringComparison.OrdinalIgnoreCase))
                {
                    return 5;
                }

                return 1;
            }
            catch
            {
                return 1;
            }
        }

        private string GetSteamStoreScreenshotByIndex(int index)
        {
            if (Settings == null)
            {
                return string.Empty;
            }

            switch (index)
            {
                case 2:
                    return Settings.SteamStoreDetailsScreenshot2 ?? string.Empty;

                case 3:
                    return Settings.SteamStoreDetailsScreenshot3 ?? string.Empty;

                case 4:
                    return Settings.SteamStoreDetailsScreenshot4 ?? string.Empty;

                case 5:
                    return Settings.SteamStoreDetailsScreenshot5 ?? string.Empty;

                case 1:
                default:
                    return Settings.SteamStoreDetailsScreenshot1 ?? string.Empty;
            }
        }

        private bool HandleSteamStoreShoulderNavigation(OnControllerButtonStateChangedArgs args)
        {
            try
            {
                if (args == null)
                {
                    return false;
                }

                if (args.State != ControllerInputState.Pressed)
                {
                    return false;
                }

                int direction = 0;

                if (args.Button == ControllerInput.RightShoulder)
                {
                    direction = 1;
                }
                else if (args.Button == ControllerInput.LeftShoulder)
                {
                    direction = -1;
                }
                else
                {
                    return false;
                }

                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return false;
                }

                if (!IsAnikiThemeActive())
                {
                    return false;
                }

                if (Settings?.SteamStoreEnabled != true)
                {
                    return false;
                }

                if (Settings.SteamStoreDetailsVisible ||
                    (Settings.SteamStoreLoading && !Settings.SteamStoreAvailable))
                {
                    return false;
                }

                if (!IsSteamStoreViewVisible())
                {
                    return false;
                }

                SwitchSteamStoreSection(direction);
                return true;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Steam Store shoulder navigation failed.");
                return false;
            }
        }

        private bool ShouldProcessKonamiCode()
        {
            try
            {
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return false;
                }

                if (!IsAnikiThemeActive())
                {
                    return false;
                }

                if (!IsPlayniteForegroundWindow())
                {
                    return false;
                }

                if (Settings == null)
                {
                    return false;
                }

                // Bloque le Konami Code sur le Hub / écran d'accueil.
                if (Settings.IsWelcomeHubOpen || Settings.IsWelcomeHubClosing)
                {
                    return false;
                }

                // Bloque sur le Steam Store et ses sous-vues.
                if (Settings.SteamStoreDetailsVisible || Settings.SteamStoreScreenshotViewerVisible)
                {
                    return false;
                }

                if (IsSteamStoreViewVisible())
                {
                    return false;
                }

                // Bloque dans l'overlay in-game.
                if (inGameOverlayService != null && inGameOverlayService.IsOverlayVisible)
                {
                    return false;
                }

                // Bloque dans les fenêtres du plugin : Quick Access / Main Menu, News, Settings custom, Screenshots, etc.
                if (anikiWindowManager != null && anikiWindowManager.HasOpenWindow)
                {
                    return false;
                }

                // Sécurité en plus : bloque si une fenêtre secondaire Playnite/plugin est ouverte.
                if (IsSecondaryWindowVisibleForKonami())
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to check Konami Code allowed context.");
                return false;
            }
        }

        private bool IsSecondaryWindowVisibleForKonami()
        {
            try
            {
                var app = Application.Current;

                if (app == null)
                {
                    return false;
                }

                if (app.Dispatcher != null && !app.Dispatcher.CheckAccess())
                {
                    return app.Dispatcher.Invoke(new Func<bool>(IsSecondaryWindowVisibleForKonami));
                }

                var mainWindow = app.MainWindow;

                return app.Windows
                    .OfType<Window>()
                    .Any(w =>
                        w != null &&
                        w.IsVisible &&
                        !ReferenceEquals(w, mainWindow) &&
                        !(w is AnikiVideoOverlayWindow));
            }
            catch
            {
                return false;
            }
        }

        public void OpenInGameOverlayFromThemeButton()
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

                inGameOverlayService?.OpenOverlayFromThemeButton();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open in-game overlay from theme button.");
            }
        }

        public bool OpenOverlayCapturePreview(AnikiMediaItem mediaItem)
        {
            try
            {
                return inGameOverlayService?.ShowCapturePreview(mediaItem) == true;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open overlay capture preview.");
                return false;
            }
        }

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (args != null && args.State == ControllerInputState.Pressed)
            {
                lastControllerInputUtc = DateTime.UtcNow;
            }

            var allowInGameOverlay =
                PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen &&
                IsAnikiThemeActive();

            if (args != null && args.State == ControllerInputState.Pressed)
            {
                if (ShouldProcessKonamiCode())
                {
                    konamiCodeService?.ProcessControllerInput(args);
                }
                else
                {
                    konamiCodeService?.CancelCurrentSequence();
                }
            }

            if (allowInGameOverlay)
            {

                if (inGameOverlayService != null && inGameOverlayService.HandleControllerButtonStateChanged(args))
                {
                    return;
                }

            }

            if (steamFriendsService != null && steamFriendsService.HandleControllerButtonStateChanged(args))
            {
                return;
            }

            if (HandleMediaGalleryRefreshShortcut(args))
            {
                return;
            }

            AnikiControllerInput.SetState(args);

            // Steam Store is displayed over the Hub.
            // Handle Store inputs before Hub navigation so RB/LB are not swallowed by the Hub handler.
            if (HandleSteamStoreScreenshotViewerInput(args))
            {
                return;
            }

            if (HandleSteamStoreShoulderNavigation(args))
            {
                return;
            }

            if (horizontalFocusFixService != null &&
                horizontalFocusFixService.HandleHubHorizontalControllerNavigation(
                    args.Button.ToString(),
                    args.State.ToString()))
            {
                return;
            }

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
            logger.Debug("AnikiHelper shutdown: start");

            try
            {
                logger.Debug("AnikiHelper shutdown: before PlayApplicationStopped");
                eventSoundService.PlayApplicationStopped();
                logger.Debug("AnikiHelper shutdown: after PlayApplicationStopped");
            }
            catch (Exception e)
            {
                logger.Error(e, "AnikiHelper shutdown: PlayApplicationStopped failed");
            }

            try { steamFriendsService?.Dispose(); } catch { }
            try { steamUpdateTimer?.Stop(); } catch { }
            try { steamUpdatesCacheFlushTimer?.Stop(); } catch { }
            try { steamGameNewsCacheFlushTimer?.Stop(); } catch { }
            try { newsRotationTimer?.Stop(); } catch { }
            try { libraryNewsRotationTimer?.Stop(); } catch { }
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
                logger.Debug("AnikiHelper shutdown: before FlushSteamGameNewsCacheIfNeeded");
                FlushSteamGameNewsCacheIfNeeded();
                logger.Debug("AnikiHelper shutdown: after FlushSteamGameNewsCacheIfNeeded");
            }
            catch (Exception e)
            {
                logger.Error(e, "AnikiHelper shutdown: FlushSteamGameNewsCacheIfNeeded failed");
            }

            try { FullscreenShutdownVideoHook.Stop(); } catch { }

            try
            {
                logger.Debug("AnikiHelper shutdown: before inGameOverlayService.Stop");
                inGameOverlayService?.Stop();
                logger.Debug("AnikiHelper shutdown: after inGameOverlayService.Stop");
            }
            catch (Exception e)
            {
                logger.Error(e, "AnikiHelper shutdown: inGameOverlayService.Stop failed");
            }

            base.OnApplicationStopped(args);

            logger.Debug("AnikiHelper shutdown: end");
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
                // Cache-first startup: wait longer before any online refresh so the Hub opens instantly.
                await Task.Delay(60000);

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

        // Schedules a Playnite Actu scan (GitHub feed) after startup, delayed to keep the Hub cache-first.
        private async Task SchedulePlayniteNewsRefreshAsync()
        {
            try
            {
                await Task.Delay(60000);
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

                try
                {
                    eventSoundService?.PlayFullscreenViewChanged();
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper][FullscreenViewChanged] Failed to play fullscreen view changed sound.");
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
                    ResetSteamFriendsDetailsForCurrentGame();

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
                    ResetSteamFriendsDetailsForCurrentGame();

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
                    ResetSteamFriendsDetailsForCurrentGame();

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

                        UpdateSteamFriendsDetailsForGame(game);

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

            Settings?.UpdateSelectedGameInstallSizeNoDecimal(g);

            Settings?.RefreshDuplicateHiderAvailability(g);

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
                ResetSteamFriendsDetailsForCurrentGame();
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
                ResetSteamFriendsDetailsForCurrentGame();
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
                ResetSteamFriendsDetailsForCurrentGame();
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

            if (isInFullscreenDetailsView)
            {
                UpdateSteamFriendsDetailsForGame(g);
            }

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

                splashScreenRuntimeService?.Close();
                ReleaseUniPlaySongGameStartingPause(game.Id);
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

                var autoDetectGameReady = Settings?.GameLaunchSplashAutoDetectReadyEnabled ?? true;
                var maximumWait = Settings?.GameLaunchSplashMaximumWaitMs ?? GameLaunchSplashMaxWaitAfterGameStartedMs;
                var launchFailureSafetyDuration = hasCustomDuration || !autoDetectGameReady
                    ? minimumDuration
                    : Math.Max(minimumDuration, maximumWait);

                DebugLog(
                    $"[AnikiHelper][Splash][Timer] " +
                    $"Game='{game.Name}', " +
                    $"Default={defaultDuration}, " +
                    $"HasCustom={hasCustomDuration}, " +
                    $"Final={minimumDuration}, " +
                    $"AutoDetectReady={autoDetectGameReady}, " +
                    $"MaximumWait={maximumWait}, " +
                    $"LaunchFailureSafety={launchFailureSafetyDuration}"
                );

                splashScreenRuntimeService.StartLaunchFailureSafety(launchFailureSafetyDuration);
                DebugLog($"[AnikiHelper][Splash][Safety] Launch failure safety started. Duration={launchFailureSafetyDuration}ms");

                StartUniPlaySongLaunchFailureRelease(game.Id, launchFailureSafetyDuration);
                DebugLog($"[AnikiHelper][Splash][UPS] Launch failure release scheduled. Duration={launchFailureSafetyDuration}ms");

                DebugLog($"[AnikiHelper][GameStarting][END] Game='{game.Name}'");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper][GameStarting][ERROR] Failed while starting game splash. Game='{args?.Game?.Name ?? "NULL"}'");
            }
        }

        private bool IsGameLaunchSplashGameReady(Game game, int? startedProcessId)
        {
            try
            {
                if (game == null)
                {
                    return false;
                }

                IntPtr readyWindow;
                if (startedProcessId.HasValue &&
                    TryFindGameReadyWindowForProcess(startedProcessId.Value, out readyWindow))
                {
                    DebugLog(
                        $"[AnikiHelper][Splash][GameReady] " +
                        $"Detected game window. Game='{game.Name}', " +
                        $"ProcessId={startedProcessId.Value}, " +
                        $"Handle={readyWindow}"
                    );
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLog($"[AnikiHelper][Splash][GameReady] Detection failed. Game='{game?.Name ?? "NULL"}', Error={ex.Message}");
                return false;
            }
        }

        private bool TryFindGameReadyWindowForProcess(int processId, out IntPtr readyWindow)
        {
            readyWindow = IntPtr.Zero;

            if (processId <= 0)
            {
                return false;
            }

            try
            {
                var process = Process.GetProcessById(processId);

                if (IsIgnoredGameReadyProcess(process.ProcessName))
                {
                    return false;
                }

                var mainWindowHandle = process.MainWindowHandle;
                if (IsUsableGameReadyWindow(mainWindowHandle, processId))
                {
                    readyWindow = mainWindowHandle;
                    return true;
                }
            }
            catch
            {
                // Some protected or already-exited processes can throw here.
                // EnumWindows below is safer and will simply fail closed if the PID is gone.
            }

            var foundWindow = IntPtr.Zero;

            try
            {
                EnumWindows((hWnd, lParam) =>
                {
                    uint windowProcessId;
                    GetWindowThreadProcessId(hWnd, out windowProcessId);

                    if (windowProcessId == processId && IsUsableGameReadyWindow(hWnd, processId))
                    {
                        foundWindow = hWnd;
                        return false;
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                foundWindow = IntPtr.Zero;
            }

            readyWindow = foundWindow;
            return readyWindow != IntPtr.Zero;
        }

        private bool IsUsableGameReadyWindow(IntPtr hWnd, int expectedProcessId)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                {
                    return false;
                }

                uint windowProcessId;
                GetWindowThreadProcessId(hWnd, out windowProcessId);

                if (windowProcessId != expectedProcessId)
                {
                    return false;
                }

                try
                {
                    var process = Process.GetProcessById(expectedProcessId);
                    if (IsIgnoredGameReadyProcess(process.ProcessName))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }

                GameReadyWindowRect rect;
                if (!GetWindowRect(hWnd, out rect))
                {
                    return false;
                }

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;

                if (width < 320 || height < 180)
                {
                    return false;
                }

                var title = GetWindowTitleSafe(hWnd);
                if (IsIgnoredGameReadyTitle(title))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private string GetWindowTitleSafe(IntPtr hWnd)
        {
            try
            {
                var builder = new StringBuilder(512);
                GetWindowText(hWnd, builder, builder.Capacity);
                return builder.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsIgnoredGameReadyProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return true;
            }

            var name = processName.Trim().ToLowerInvariant();

            return name == "playnite" ||
                   name == "playnite.fullscreenapp" ||
                   name == "playnite.desktopapp" ||
                   name == "steam" ||
                   name == "steamwebhelper" ||
                   name == "epicgameslauncher" ||
                   name == "epicwebhelper" ||
                   name == "eadesktop" ||
                   name == "ealink" ||
                   name == "ubisoftconnect" ||
                   name == "upc" ||
                   name == "galaxyclient" ||
                   name == "battle.net" ||
                   name == "agent" ||
                   name == "explorer" ||
                   name == "discord" ||
                   name == "devenv" ||
                   name == "blend" ||
                   name == "code" ||
                   name == "chrome" ||
                   name == "firefox" ||
                   name == "msedge";
        }

        private bool IsIgnoredGameReadyTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var normalized = title.ToLowerInvariant();

            return normalized.Contains("microsoft blend") ||
                   normalized.Contains("visual studio") ||
                   normalized.Contains("playnite") ||
                   normalized.Contains("discord") ||
                   normalized.Contains("explorer");
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
                var priorityOrder = GetGameLaunchSplashPriorityOrder(mode);

                return ResolveGameLaunchSplashByPriority(game, priorityOrder);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to resolve splash background.");
            }

            return null;
        }

        private IReadOnlyList<SplashScreenPriorityTarget> GetGameLaunchSplashPriorityOrder(SplashScreenSelectionMode mode)
        {
            switch (mode)
            {
                // UI label: Source priority
                case SplashScreenSelectionMode.AlwaysSource:
                    return new[]
                    {
                        SplashScreenPriorityTarget.Source,
                        SplashScreenPriorityTarget.GameCustom,
                        SplashScreenPriorityTarget.GameBackground,
                        SplashScreenPriorityTarget.Platform,
                        SplashScreenPriorityTarget.Global
                    };

                // UI label: Platform priority
                case SplashScreenSelectionMode.AlwaysPlatform:
                    return new[]
                    {
                        SplashScreenPriorityTarget.Platform,
                        SplashScreenPriorityTarget.GameCustom,
                        SplashScreenPriorityTarget.GameBackground,
                        SplashScreenPriorityTarget.Source,
                        SplashScreenPriorityTarget.Global
                    };

                // UI label: Global only
                case SplashScreenSelectionMode.AlwaysGlobal:
                    return new[]
                    {
                        SplashScreenPriorityTarget.Global
                    };

                case SplashScreenSelectionMode.CustomPriority:
                    var customOrder = Settings?.GetGameLaunchSplashCustomPriorityOrder();
                    if (customOrder != null && customOrder.Count > 0)
                    {
                        return customOrder;
                    }

                    goto case SplashScreenSelectionMode.Automatic;

                // UI label: Game priority
                case SplashScreenSelectionMode.Automatic:
                default:
                    return new[]
                    {
                        SplashScreenPriorityTarget.GameCustom,
                        SplashScreenPriorityTarget.GameBackground,
                        SplashScreenPriorityTarget.Source,
                        SplashScreenPriorityTarget.Platform,
                        SplashScreenPriorityTarget.Global
                    };
            }
        }

        private string ResolveGameLaunchSplashByPriority(Game game, IEnumerable<SplashScreenPriorityTarget> priorityOrder)
        {
            if (game == null)
            {
                return null;
            }

            var used = new HashSet<SplashScreenPriorityTarget>();

            foreach (var target in priorityOrder ?? Enumerable.Empty<SplashScreenPriorityTarget>())
            {
                if (target == SplashScreenPriorityTarget.None || used.Contains(target))
                {
                    continue;
                }

                used.Add(target);

                if (target == SplashScreenPriorityTarget.GameBackground)
                {
                    var gameBackground = GetPlayniteGameBackground(game);
                    if (!string.IsNullOrWhiteSpace(gameBackground) && File.Exists(gameBackground))
                    {
                        return gameBackground;
                    }

                    continue;
                }

                var media = splashScreenService?.ResolvePriorityTarget(game, target);
                if (!string.IsNullOrWhiteSpace(media?.FilePath) && File.Exists(media.FilePath))
                {
                    return media.FilePath;
                }

                if (target == SplashScreenPriorityTarget.GameCustom)
                {
                    var legacyCustom = GetStoredCustomSplashPath(game);
                    if (!string.IsNullOrWhiteSpace(legacyCustom) && File.Exists(legacyCustom))
                    {
                        return legacyCustom;
                    }
                }
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

                try { steamFriendsService?.OnGameStarted(); } catch { }

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
                        var autoDetectGameReady = Settings?.GameLaunchSplashAutoDetectReadyEnabled ?? true;
                        var startedProcessId = args?.StartedProcessId;

                        DebugLog(
                            $"[AnikiHelper][Splash][CloseAfterGameStartedTask][Settings] " +
                            $"Game='{g?.Name ?? "NULL"}', " +
                            $"MinimumDuration={minimumDuration}, " +
                            $"HasCustomDuration={hasCustomDuration}, " +
                            $"AutoDetectReady={autoDetectGameReady}, " +
                            $"StartedProcessId={startedProcessId}, " +
                            $"MaximumWait={maximumWait}"
                        );

                        if (hasCustomDuration || !autoDetectGameReady)
                        {
                            await splashScreenRuntimeService.CloseAfterFixedDurationAsync(minimumDuration);
                            DebugLog($"[AnikiHelper][Splash][CloseAfterGameStartedTask][Result] Fixed duration close finished. Game='{g?.Name ?? "NULL"}'");
                        }
                        else
                        {
                            await splashScreenRuntimeService.CloseAfterGameStartedAsync(
                                minimumDuration,
                                maximumWait,
                                () => IsGameLaunchSplashGameReady(g, startedProcessId),
                                true);

                            DebugLog($"[AnikiHelper][Splash][CloseAfterGameStartedTask][Result] Auto game ready close finished. Game='{g?.Name ?? "NULL"}'");
                        }

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

                if (sessionStartAt.ContainsKey(g.Id))
                {
                    DebugLog($"[AnikiHelper][GameStarted][RESTART] Same game started again before previous stop cleanup. Resetting session. Game='{g.Name}', Id={g.Id}");
                    sessionStartAt.Remove(g.Id);
                    sessionStartPlaytimeMinutes.Remove(g.Id);
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
                try { steamFriendsService?.OnGameStopped(); } catch { }
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

                if (!sessionStartAt.ContainsKey(g.Id))
                {
                    DebugLog($"[AnikiHelper][GameStopped][SKIP] Duplicate/unknown stop ignored. Game='{g.Name}', Id={g.Id}");

                    splashScreenRuntimeService?.Close();
                    ReleaseUniPlaySongGameStartingPause(g.Id);
                    inGameOverlayService?.ClearCurrentGame(g);

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

        private void RecalcHubLibraryRecommendedGamesSafe()
        {
            try
            {
                RecalcHubLibraryRecommendedGames();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] RecalcHubLibraryRecommendedGames failed");
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

            // Recalcule les 4 cartes Hub "Recommended from your library"
            RecalcHubLibraryRecommendedGamesSafe();
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

        private async void RefreshAchievementCacheForGame(Guid gameId, string gameName)
        {
            if (Settings == null || gameId == Guid.Empty)
            {
                return;
            }

            try
            {
                await Settings.RefreshAchievementMemoriesForGameAsync(gameId);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to refresh achievement cache for game.");

                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Failed to refresh achievements cache for this game."
                    + Environment.NewLine
                    + Environment.NewLine
                    + ex.Message,
                    "Aniki Helper"
                );
            }
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

            // ===== ACHIEVEMENTS =====
            yield return new GameMenuItem
            {
                MenuSection = "Aniki Helper|Achievements Memories",
                Description = ResourceProvider.GetString("LOCAnikiHelperRefreshAchievementCacheForGame"),
                Action = (_) =>
                {
                    RefreshAchievementCacheForGame(game.Id, game.Name);
                }
            };

            // ===== OVERLAY SETTINGS =====
            yield return new GameMenuItem
            {
                MenuSection = "Aniki Helper|Overlay Settings",
                Description = Settings != null && Settings.IsInGameOverlayNeverSuspendGame(game.Id)
                    ? ResourceProvider.GetString("LOCAnikiHelperOverlayAllowSuspendThisGame")
                    : ResourceProvider.GetString("LOCAnikiHelperOverlayNeverSuspendThisGame"),
                Action = (_) =>
                {
                    Settings?.ToggleInGameOverlayNeverSuspendGame(game);
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
