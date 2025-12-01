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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;


namespace AnikiHelper
{
    public class AnikiHelper : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly SteamGlobalNewsService steamGlobalNewsService;

        private readonly bool isFullscreenMode;

        public static AnikiHelper Instance { get; private set; }

        private const int GlobalNewsRefreshIntervalHours = 3;

        // Playnite News : 1 scan / 24h
        private const int PlayniteNewsRefreshIntervalHours = 24;

        // Playnite news feed 
        private static readonly string[] PlayniteNewsFeedUrls = new[]
        {
            "https://github.com/Mike-Aniki/AnikiHelper/releases.atom",
            "https://github.com/jonosellier/toggle-theme-playnite/releases.atom",
            "https://github.com/And360red/Solaris/releases.atom",
            "https://github.com/Mike-Aniki/Aniki-ReMake/releases.atom",
            "https://github.com/Lacro59/playnite-screenshotsvisualizer-plugin/releases.atom",
            "https://github.com/Lacro59/playnite-checkdlc-plugin/releases.atom",
            "https://github.com/Lacro59/playnite-backgroundchanger-plugin/releases.atom",
            "https://github.com/Jeshibu/PlayniteExtensions/releases.atom",
            "https://github.com/JosefNemec/Playnite/releases.atom",
            "https://github.com/ashpynov/PlayniteSound/releases.atom",
            "https://github.com/ashpynov/ThemeOptions/releases.atom",
            "https://github.com/Lacro59/playnite-successstory-plugin/releases.atom",
            "https://github.com/Lacro59/playnite-howlongtobeat-plugin/releases.atom"
        };


        // === Steam Deals (game-deals.app) ===
        private const int DealsRefreshIntervalHours = 12; // 12h 
        private const int DealsMaxDays = 7;               // keep for a maximum of 7 days
        private const int DealsMaxItems = 15;             // keep a maximum of 15 deals
        private const string DealsFeedUrl =
            "https://game-deals.app/rss/discounts/steam";


        // === Diagnostics and paths ===
        private string GetDataRoot() => Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());

        // News update for Welcome Hub
        private void UpdateLatestNewsFromList(IList<SteamGlobalNewsItem> items)
        {
            try
            {
                if (items == null || items.Count == 0)
                {
                    Settings.LatestNewsTitle = string.Empty;
                    Settings.LatestNewsDateString = string.Empty;
                    Settings.LatestNewsSummary = string.Empty;
                    Settings.LatestNewsGameName = string.Empty;
                    Settings.LatestNewsLocalImagePath = string.Empty;
                    return;
                }

                var latest = items[0];

                Settings.LatestNewsTitle = latest.Title ?? string.Empty;
                Settings.LatestNewsDateString = latest.DateString ?? string.Empty;
                Settings.LatestNewsSummary = latest.Summary ?? string.Empty;
                Settings.LatestNewsGameName = latest.GameName ?? string.Empty;
                Settings.LatestNewsLocalImagePath = latest.LocalImagePath ?? string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to update LatestNews snapshot.");
            }
        }

        // Charge les news globales depuis CacheNews.json si Settings.SteamGlobalNews est vide 
        // Load global news from CacheNews.json if Settings.SteamGlobalNews is empty 

        private void LoadNewsFromCacheIfNeeded()
        {
            try
            {
                if (Settings.SteamGlobalNews != null && Settings.SteamGlobalNews.Count > 0)
                {
                    UpdateLatestNewsFromList(Settings.SteamGlobalNews.ToList());
                    return;
                }

                var path = Path.Combine(GetDataRoot(), "CacheNews.json");
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
                    if (Settings.SteamGlobalNews == null)
                    {
                        Settings.SteamGlobalNews =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }
                    else
                    {
                        Settings.SteamGlobalNews.Clear();
                    }

                    foreach (var it in ordered)
                    {
                        Settings.SteamGlobalNews.Add(it);
                    }
                });

                UpdateLatestNewsFromList(ordered);
                SavePluginSettings(Settings); 
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to load global news from cache.");
            }
        }



        private class SteamUpdateCacheEntry
        {
            public string Title { get; set; }
            public string GameName { get; set; }
            public DateTime LastPublishedUtc { get; set; }
            public string Html { get; set; }
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
                            LastPublishedUtc = DateTime.UtcNow
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
                    var target = Settings.SteamRecentUpdates;
                    target.Clear();
                    foreach (var it in list)
                    {
                        target.Add(it);
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
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var s = Settings;

                    s.GlobalToastMessage = string.IsNullOrWhiteSpace(message)
                        ? string.Empty
                        : message;

                    s.GlobalToastType = type ?? string.Empty;

                    s.GlobalToastStamp = Guid.NewGuid().ToString();

                    s.GlobalToastFlip = false;
                    s.GlobalToastFlip = true;
                });
            }
            catch
            {

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
        private readonly DispatcherTimer dealsTimer;


        // Steam current players
        private readonly SteamPlayerCountService steamPlayerCountService = new SteamPlayerCountService();

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

            if (Settings.SteamUpdatesScanEnabled)
            {
                await UpdateSteamUpdateForGameAsync(g);
            }
            else
            {
                
                ResetSteamUpdate();
            }

            await UpdateSteamPlayerCountForGameAsync(g);
        }


        private async Task RefreshGlobalSteamNewsAsync(bool force = false)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var last = Settings.SteamGlobalNewsLastRefreshUtc;

                logger.Debug($"[NewsScan] Start (force={force}, enabled={Settings.NewsScanEnabled})");

                if (!Settings.NewsScanEnabled)
                {
                    logger.Debug("[NewsScan] Skipped (disabled)");
                    return;
                }

                // Cooldown (3h)
                if (!force && last.HasValue)
                {
                    var hours = (nowUtc - last.Value).TotalHours;
                    if (hours < GlobalNewsRefreshIntervalHours)
                    {
                        logger.Debug($"[NewsScan] Skipped (cooldown {hours:F1}h < {GlobalNewsRefreshIntervalHours}h)");
                        return;
                    }
                }

                // Scan
                var items = await steamGlobalNewsService.GetGlobalNewsAsync().ConfigureAwait(false);
                if (items == null)
                {
                    logger.Warn("[NewsScan] Service returned NULL");
                    items = new List<SteamGlobalNewsItem>();
                }

                logger.Debug($"[NewsScan] Items fetched: {items.Count}");

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (Settings.SteamGlobalNews == null)
                    {
                        Settings.SteamGlobalNews =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }

                    Settings.SteamGlobalNews.Clear();
                    foreach (var it in items)
                        Settings.SteamGlobalNews.Add(it);

                    Settings.SteamGlobalNewsLastRefreshUtc = nowUtc;
                    SavePluginSettings(Settings);

                    logger.Debug("[NewsScan] Updated settings & memory list");
                });

                UpdateLatestNewsFromList(items);

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[NewsScan] ERROR");

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (Settings.SteamGlobalNews == null)
                    {
                        Settings.SteamGlobalNews =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }

                    Settings.SteamGlobalNews.Clear();
                    Settings.SteamGlobalNews.Add(new SteamGlobalNewsItem
                    {
                        GameName = "Error",
                        Title = "Error while loading global Steam news",
                        DateString = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                        Summary = ex.Message
                    });
                });
            }
        }


        // Playnite News
        private async Task RefreshPlayniteNewsAsync(bool force = false, bool silent = false)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var last = Settings.PlayniteNewsLastRefreshUtc;

                // Cooldown 24h
                if (!force && last.HasValue)
                {
                    var hours = (nowUtc - last.Value).TotalHours;
                    if (hours < PlayniteNewsRefreshIntervalHours)
                    {
                        if (!silent)
                        {
                            logger.Debug($"[PlayniteNews] Skipped (cooldown {hours:F1}h < {PlayniteNewsRefreshIntervalHours}h)");
                        }
                        return;
                    }
                }

                var allItems = new List<SteamGlobalNewsItem>();

                foreach (var url in PlayniteNewsFeedUrls)
                {
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    try
                    {
                        var items = await steamGlobalNewsService
                            .GetGenericFeedAsync(url)
                            .ConfigureAwait(false);

                        if (items != null)
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

                if (allItems.Count == 0)
                {
                    allItems = new List<SteamGlobalNewsItem>();
                }

                // On garde juste les 10 dernières (only keep the last 10.)
                var ordered = allItems
                    .OrderByDescending(n => n.PublishedUtc)
                    .Take(10)
                    .ToList();

                // Détection “NEW”
                var previousKey = Settings.PlayniteNewsLastKey ?? string.Empty;
                var topItem = ordered.FirstOrDefault();
                var newKey = topItem != null ? MakePlayniteNewsKey(topItem) : string.Empty;

                bool hasNew = false;

                if (!string.IsNullOrEmpty(newKey) &&
                    !string.Equals(previousKey, newKey, StringComparison.Ordinal))
                {
                    hasNew = true;
                }

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // Sécu
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

                    SavePluginSettings(Settings);
                });

                // Toast global 
                if (hasNew && topItem != null)
                {
                    var title = topItem.Title?.Trim();

                    var msg = string.IsNullOrWhiteSpace(title)
                        ? "New add-on update available"
                        : title;

                    ShowGlobalToast(msg, "playniteNews");
                }

            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[PlayniteNews] RefreshPlayniteNewsAsync failed.");
            }
        }


        // Steam Deals via game-deals.app
        private async Task RefreshDealsAsync(bool force = false, bool silent = false)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var last = Settings.LastDealsScanUtc;

                // Cooldown 12h
                if (!force && last.HasValue)
                {
                    var hours = (nowUtc - last.Value).TotalHours;
                    if (hours < DealsRefreshIntervalHours)
                    {
                        if (!silent)
                        {
                            logger.Debug($"[Deals] Skipped (cooldown {hours:F1}h < {DealsRefreshIntervalHours}h)");
                        }
                        return;
                    }
                }

                var items = await steamGlobalNewsService
                    .GetGenericFeedAsync(DealsFeedUrl)
                    .ConfigureAwait(false);

                if (items == null)
                {
                    items = new List<SteamGlobalNewsItem>();
                }

                // 7 day  max + 15 items max
                var threshold = nowUtc.AddDays(-DealsMaxDays);

                var filtered = items
                    .Where(n => n.PublishedUtc > threshold)
                    .OrderByDescending(n => n.PublishedUtc)
                    .Take(DealsMaxItems)
                    .ToList();

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (Settings.Deals == null)
                    {
                        Settings.Deals =
                            new System.Collections.ObjectModel.ObservableCollection<SteamGlobalNewsItem>();
                    }

                    Settings.Deals.Clear();
                    foreach (var it in filtered)
                    {
                        Settings.Deals.Add(it);
                    }

                    Settings.LastDealsScanUtc = nowUtc;
                    SavePluginSettings(Settings);
                });
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    logger.Warn(ex, "[Deals] RefreshDealsAsync failed.");
                }
            }
        }

        private async void DealsTimer_Tick(object sender, EventArgs e)
        {
            // Only start the scan if you are in full-screen mode.
            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            await RefreshDealsAsync(force: false, silent: true);
        }



        private async Task TryScanGlobalNewsAsync(bool force, bool silent)
        {
            var logger = LogManager.GetLogger();

            try
            {
                if (!Settings.NewsScanEnabled)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                var lastScan = Settings.LastNewsScanUtc;

                if (!force && lastScan != DateTime.MinValue)
                {
                    var hoursSince = (now - lastScan).TotalHours;
                    if (hoursSince < GlobalNewsRefreshIntervalHours)
                    {
                        return; 
                    }
                }

                // Scan RSS 
                var svc = new SteamGlobalNewsService(PlayniteApi, Settings);
                var items = await svc.GetGlobalNewsAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Settings.SteamGlobalNews.Clear();
                    foreach (var it in items)
                        Settings.SteamGlobalNews.Add(it);

                    Settings.LastNewsScanUtc = now;

                    SavePluginSettings(Settings);
                });

                UpdateLatestNewsFromList(items);

                SavePluginSettings(Settings);

                // Log 
                if (!silent)
                {
                    logger.Info("[AnikiHelper] Global News refreshed.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] TryScanGlobalNewsAsync failed.");
            }
        }



        private async Task<bool> UpdateSteamUpdateCacheOnlyForGameAsync(Playnite.SDK.Models.Game game)
        {
            var notified = false;

            try
            {
                var steamId = GetSteamGameId(game);
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    return false;
                }

                var result = await steamUpdateService.GetLatestUpdateAsync(steamId);
                if (result == null || string.IsNullOrWhiteSpace(result.Title))
                {
                    return false;
                }

                var cleanedHtml = CleanHtml(result.HtmlBody ?? string.Empty);

                var cache = LoadSteamUpdatesCache();
                cache.TryGetValue(steamId, out var cachedEntry);

                var published = result.Published;
                if (published == DateTime.MinValue)
                {
                    published = DateTime.UtcNow;
                }
                else
                {
                    published = published.ToUniversalTime();
                }

                if (cachedEntry == null)
                {
                    cache[steamId] = new SteamUpdateCacheEntry
                    {
                        Title = result.Title,
                        GameName = Safe(game.Name),
                        LastPublishedUtc = published,
                        Html = cleanedHtml
                    };

                    SaveSteamUpdatesCache(cache);
                    return false;
                }

                var lastPublished = cachedEntry.LastPublishedUtc;
                var sessionKey = $"{steamId}|{result.Title}";

                bool isRealNew = published > lastPublished;

                if (isRealNew)
                {
                    // complete cache update
                    cachedEntry.Title = result.Title;
                    cachedEntry.GameName = Safe(game.Name);
                    cachedEntry.LastPublishedUtc = published;
                    cachedEntry.Html = cleanedHtml;
                    cache[steamId] = cachedEntry;
                    SaveSteamUpdatesCache(cache);

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
                        cache[steamId] = cachedEntry;
                        SaveSteamUpdatesCache(cache);
                    }
                }
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
            try
            {
                // 1) Check mode + focus
                if (!IsSteamRecentScanAllowed())
                {
                    return;
                }

                // 2) Frequency limit: no more than one scan every 2 hours
                var nowUtc = DateTime.UtcNow;
                var last = Settings.LastSteamRecentCheckUtc;

                if (last.HasValue && (nowUtc - last.Value).TotalHours < 4)
                {
                    return;
                }

                Settings.LastSteamRecentCheckUtc = nowUtc;
                SavePluginSettings(Settings);

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

                    // If we lose focus along the way, we stop.
                    if (!IsSteamRecentScanAllowed())
                    {
                        break;
                    }

                    var steamId = GetSteamGameId(g);
                    if (string.IsNullOrWhiteSpace(steamId))
                    {
                        continue; 
                    }

                    scanned++;

                    var notified = await UpdateSteamUpdateCacheOnlyForGameAsync(g);

                    // short delay to avoid spamming the API.
                    var delayMs = notified ? 10000 : 500;

                    int remaining = delayMs;
                    const int step = 200; // 200 ms

                    while (remaining > 0)
                    {
                        if (!IsSteamRecentScanAllowed())
                        {
                            return;
                        }

                        var chunk = Math.Min(step, remaining);
                        await Task.Delay(chunk);
                        remaining -= chunk;
                    }
                }

                RefreshSteamRecentUpdatesFromCache();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] CheckSteamUpdatesForRecentGamesAsync failed.");
            }
        }


        private async Task UpdateSteamUpdateForGameAsync(Playnite.SDK.Models.Game game)
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

                // 1) First, we try the CACHE
                var cache = LoadSteamUpdatesCache();

                cache.TryGetValue(steamId, out var cachedEntry);
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

                // 2) Then we try to call Steam to refresh
                var result = await steamUpdateService.GetLatestUpdateAsync(steamId);
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

                cache.TryGetValue(steamId, out cachedEntry);
                var lastTitle = cachedEntry?.Title;

                var published = result.Published;
                if (published == DateTime.MinValue)
                {
                    published = DateTime.UtcNow;
                }
                else
                {
                    published = published.ToUniversalTime();
                }

                // No cache for this game yet
                if (string.IsNullOrWhiteSpace(lastTitle))
                {
                    cache[steamId] = new SteamUpdateCacheEntry
                    {
                        Title = result.Title,
                        GameName = Safe(game.Name),
                        LastPublishedUtc = published,
                        Html = cleanedHtml
                    };

                    SaveSteamUpdatesCache(cache);

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

                        cache[steamId] = new SteamUpdateCacheEntry
                        {
                            Title = result.Title,
                            GameName = Safe(game.Name),
                            LastPublishedUtc = published,
                            Html = cleanedHtml
                        };

                        SaveSteamUpdatesCache(cache);
                        steamUpdateNewThisSession.Add(sessionKey);
                    }
                    else
                    {
                        // Same version (same date) -> not NEW

                        if (string.IsNullOrWhiteSpace(cachedEntry?.Html) ||
                            !string.Equals(cachedEntry.Title, result.Title, StringComparison.Ordinal))
                        {
                            cachedEntry.Html = cleanedHtml;
                            cachedEntry.Title = result.Title;
                            cachedEntry.LastPublishedUtc = published;
                            cache[steamId] = cachedEntry;
                            SaveSteamUpdatesCache(cache);
                        }

                        if (steamUpdateNewThisSession.Contains(sessionKey))
                        {
                            isNew = true;
                        }
                    }
                }


                // 3) Push the "fresh" version into Settings
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


                            // "sync" call on the async method
                            var result = steamUpdateService
                                .GetLatestUpdateAsync(entry.SteamId)
                                .GetAwaiter()
                                .GetResult();

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
                                Html = cleanedHtml
                            };



                            updated++;

                            // small throttle to avoid spamming the API
                            System.Threading.Thread.Sleep(150);
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

            const int RefDaysLimit = 45;     
            var now = DateTime.Now;

            // 1) Trouver le jeu de référence (Find the reference game)

            Playnite.SDK.Models.Game refGame = null;

            // 1A) recent progress 
            try
            {
                var snapshots = LoadAllMonthlySnapshots();
                var recentMinutes = ComputeRecentMinutesFromSnapshots(snapshots, games);

                ulong bestMinutes = 0;
                Guid bestId = Guid.Empty;

                foreach (var kv in recentMinutes)
                {
                    if (kv.Value == 0)
                        continue;

                    var g = games.FirstOrDefault(x => x.Id == kv.Key);
                    if (g == null)
                        continue;

                    // Game played within the last 45 days
                    if (g.LastActivity == null ||
                       (now - g.LastActivity.Value).TotalDays > RefDaysLimit)
                    {
                        continue;
                    }

                    if (kv.Value > bestMinutes)
                    {
                        bestMinutes = kv.Value;
                        bestId = kv.Key;
                    }
                }

                if (bestId != Guid.Empty)
                {
                    refGame = games.FirstOrDefault(g => g.Id == bestId);
                }
            }
            catch
            {
                
            }

            // 1B) Fallback 
            if (refGame == null)
            {
                try
                {
                    var monthStart = new DateTime(now.Year, now.Month, 1);
                    var snapshot = LoadMonthSnapshot(monthStart);

                    ulong topDelta = 0;
                    Guid bestId = Guid.Empty;

                    foreach (var g in games)
                    {
                        // Récence
                        if (g.LastActivity == null ||
                           (now - g.LastActivity.Value).TotalDays > RefDaysLimit)
                        {
                            continue;
                        }

                        var currMinutes = ToMinutes(g.Playtime);

                        if (!snapshot.TryGetValue(g.Id, out var baseMinutes))
                        {
                            continue;
                        }

                        var delta = currMinutes > baseMinutes ? (currMinutes - baseMinutes) : 0UL;

                        if (delta > topDelta)
                        {
                            topDelta = delta;
                            bestId = g.Id;
                        }
                    }

                    if (bestId != Guid.Empty)
                    {
                        refGame = games.FirstOrDefault(g => g.Id == bestId);
                    }
                }
                catch
                {
                }
            }

            // 1C) Fallback : last game played in the last 45 days
            if (refGame == null)
            {
                refGame = games
                    .Where(g => g.LastActivity != null &&
                                (now - g.LastActivity.Value).TotalDays <= RefDaysLimit)
                    .OrderByDescending(g => g.LastActivity)
                    .FirstOrDefault();
            }

            if (refGame == null)
            {
                return; 
            }

            var refName = Safe(refGame.Name);
            var refGenres = GetGenreNames(refGame).ToList();
            var refTags = GetTagNames(refGame).ToList();
            var refDevs = GetDeveloperNames(refGame).ToList();
            var refPubs = GetPublisherNames(refGame).ToList();

            // 2) Trouver la meilleure suggestion (Find the best suggestion)

            int bestScore = 0;
            Playnite.SDK.Models.Game bestGame = null;
            string bestReason = string.Empty;

            foreach (var g in games)
            {
                if (g.Id == refGame.Id)
                    continue;

                // Exclude games marked as "finished"
                if (IsGameFinished(g))
                    continue;

                // Candidate data
                var genres = GetGenreNames(g).ToList();
                var tags = GetTagNames(g).ToList();
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

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGame = g;
                    bestReason = reason;
                }
            }

            if (bestGame == null)
                return;

            // 3) Construction of the banner text

            string banner = string.Empty;

            if (!string.IsNullOrEmpty(bestReason) && !string.IsNullOrEmpty(refName))
            {
                switch (bestReason)
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

            // 4) Construction des chemins (Road construction)

            string coverPath = GetGameCoverPath(bestGame);

            string bgPath = null;
            if (!string.IsNullOrEmpty(bestGame.BackgroundImage))
                bgPath = PlayniteApi.Database.GetFullFilePath(bestGame.BackgroundImage);
            if (string.IsNullOrEmpty(bgPath) && !string.IsNullOrEmpty(bestGame.CoverImage))
                bgPath = PlayniteApi.Database.GetFullFilePath(bestGame.CoverImage);
            if (string.IsNullOrEmpty(bgPath) && !string.IsNullOrEmpty(bestGame.Icon))
                bgPath = PlayniteApi.Database.GetFullFilePath(bestGame.Icon);

            // 5) Push vers Settings

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                s.SuggestedGameSourceName = refName;
                s.SuggestedGameName = Safe(bestGame.Name);
                s.SuggestedGameCoverPath = string.IsNullOrEmpty(coverPath) ? string.Empty : coverPath;
                s.SuggestedGameBackgroundPath = string.IsNullOrEmpty(bgPath) ? string.Empty : bgPath;
                s.SuggestedGameReasonKey = bestReason ?? string.Empty;
                s.SuggestedGameBannerText = banner ?? string.Empty;
            });
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

        private class MonthlySnapshotInfo
        {
            public DateTime MonthStart { get; set; }
            public Dictionary<Guid, ulong> Minutes { get; set; }
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

                logger.Debug($"[AnikiHelper] Monthly snapshot: added {Safe(g.Name)} ({g.Id}) for {monthStart:yyyy-MM} with base={baseMinutes} minutes.");
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

        // Clears the dynamic color cache 
        public void ClearDynamicColorCache()
        {
            try
            {
                DynamicAuto.ClearPersistentCache(alsoRam: true);

                var dir = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());
                var fileNew = Path.Combine(dir, "palette_cache.json");
                var fileTmp = fileNew + ".tmp";
                var fileOld = Path.Combine(dir, "palette_cache_v1.json"); 

                int deleted = 0;
                if (File.Exists(fileNew)) { File.Delete(fileNew); deleted++; }
                if (File.Exists(fileTmp)) { File.Delete(fileTmp); deleted++; }
                if (File.Exists(fileOld)) { File.Delete(fileOld); deleted++; }

                logger.Info($"[AnikiHelper] Cleared dynamic color cache. Files deleted: {deleted}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] ClearDynamicColorCache failed.");
                throw;
            }
        }

        // Deletes the News cache
        public void ClearNewsCache()
        {
            try
            {
                // 1) Clear the list in memory
                Settings.SteamGlobalNews?.Clear();
                Settings.PlayniteNews?.Clear();
                Settings.Deals?.Clear();
                Settings.PlayniteNewsHasNew = false;
                Settings.PlayniteNewsLastKey = string.Empty;

                // 2) Reset des timestamps pour forcer un rescan
                Settings.SteamGlobalNewsLastRefreshUtc = null;
                Settings.LastNewsScanUtc = DateTime.MinValue;
                Settings.PlayniteNewsLastRefreshUtc = null;
                Settings.LastDealsScanUtc = null;

                // 3) Delete the JSON file
                var jsonPath = Path.Combine(GetDataRoot(), "CacheNews.json");
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                }

                // 4) Delete the NewsImages folder (thumbnails)
                var imgRoot = Path.Combine(GetDataRoot(), "NewsImages");
                if (Directory.Exists(imgRoot))
                {
                    Directory.Delete(imgRoot, true);
                }

                // 5) Save the updated settings
                SavePluginSettings(Settings);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ClearNewsCache failed.");
            }
        }

        // Updates the snapshot info text
        private void UpdateSnapshotInfoProperty(DateTime monthStart)
        {
            try
            {
                var file = GetMonthFilePath(monthStart);
                if (File.Exists(file))
                {
                    var dt = File.GetLastWriteTime(file);
                    Settings.SnapshotDateString = $"Snapshot {monthStart:MM/yyyy} : {dt:dd/MM/yyyy HH:mm}";
                }
                else
                {
                    Settings.SnapshotDateString = $"Snapshot {monthStart:MM/yyyy} : (aucun)";
                }
            }
            catch
            {
                Settings.SnapshotDateString = $"Snapshot {monthStart:MM/yyyy} : (indisponible)";
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
            steamGlobalNewsService = new SteamGlobalNewsService(api, Settings);

            AddSettingsSupportSafe("AnikiHelper", "Settings");

            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Settings.IncludeHidden) ||
                    e.PropertyName == nameof(Settings.TopPlayedMax) ||
                    e.PropertyName == nameof(Settings.PlaytimeUseDaysFormat))
                {
                    RecalcStatsSafe();
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

                // Timer pour les deals (on check toutes les heures, mais avec cooldown 12h)
                dealsTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromHours(1)
                };
                dealsTimer.Tick += DealsTimer_Tick;
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
                    SavePluginSettings(Settings);

                    _ = InitializeSteamUpdatesCacheForAllGamesAsync();
                }
                else
                {
                    Settings.AskSteamUpdateCacheAtStartup = false;
                    SavePluginSettings(Settings);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] TryAskForSteamUpdateCacheOnStartup failed.");
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

        private bool IsSteamRecentScanAllowed()
        {
            if (!Settings.SteamUpdatesScanEnabled)
                return false;

            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                return false;

            if (!IsMainWindowActive())
                return false;

            return true;
        }


        #region Lifecycle

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            
            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            EnsureMonthlySnapshotSafe();
            RecalcStatsSafe();

            PlayniteApi.Database.DatabaseOpened += (_, __) =>
            {
                EnsureMonthlySnapshotSafe();
                RecalcStatsSafe();
            };

            if (PlayniteApi?.Database?.Games is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged += (_, __) => RecalcStatsSafe();
            }

            try
            {
                Settings.SessionGameName = string.Empty;
                Settings.SessionDurationString = string.Empty;
                Settings.SessionTotalPlaytimeString = string.Empty;
                Settings.SessionNewAchievementsString = string.Empty;
                Settings.SessionHasNewAchievements = false;
                Settings.SessionNewAchievementsCount = 0;
                Settings.SessionNotificationStamp = string.Empty;
                Settings.SessionNotificationArmed = false;
            }
            catch { }

            // --- UI Fullscreen ---
            AddonsUpdateStyler.Start();

            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                () => DynamicAuto.Init(PlayniteApi),
                System.Windows.Threading.DispatcherPriority.Loaded
            );

            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                () => SettingsWindowStyler.Start(),
                System.Windows.Threading.DispatcherPriority.Loaded
            );

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

            // --- Steam Deals (game-deals.app) ---
            try
            {
                _ = RefreshDealsAsync(force: false, silent: true);

                dealsTimer?.Start();
            }
            catch { }


            // Random login screen
            try
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
                SavePluginSettings(Settings);
            }
            catch { }

            try
            {
                TryAskForSteamUpdateCacheOnStartup();
            }
            catch { }

            try
            {
                _ = ScheduleSteamRecentUpdatesScanAsync(30);
            }
            catch { }
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

                await TryScanGlobalNewsAsync(force: false, silent: true);
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


        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            base.OnGameStarted(args);

            var g = args?.Game;
            if (g == null) return;

            sessionStartAt[g.Id] = DateTime.Now;
            sessionStartPlaytimeMinutes[g.Id] = (g.Playtime / 60UL);

        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            base.OnGameStopped(args);

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
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var s = Settings;

                s.SessionGameName = string.IsNullOrWhiteSpace(g.Name) ? "(Unnamed Game)" : g.Name;
                s.SessionDurationString = FormatHhMmFromMinutes(sessionMinutes);
                s.SessionTotalPlaytimeString = FormatHhMmFromMinutes(Math.Max(0, totalMinutes));

                s.SessionNewAchievementsString = string.Empty;
                s.SessionNewAchievementsCount = 0;
                s.SessionHasNewAchievements = false;

                s.SessionNotificationStamp = Guid.NewGuid().ToString();
                s.SessionNotificationFlip = !s.SessionNotificationFlip;
                s.SessionNotificationArmed = true;
            });

            EnsureGameInCurrentMonthSnapshot(g, sessionMinutes);

            // 4) Cache cleanup
            sessionStartAt.Remove(g.Id);
            sessionStartPlaytimeMinutes.Remove(g.Id);

            // 5) Recalcul stats + snapshot 
            EnsureMonthlySnapshotSafe();
            RecalcStatsSafe();
        }

        #endregion

        private void RecalcStatsSafe()
        {
            try { RecalcStats(); }
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


        private static string PercentStringLocal(int part, int total) =>
            total <= 0 ? "0%" : $"{Math.Round(part * 100.0 / total)}%";

        /// <summary>Calcule et expose: Totaux, TopPlayed, CompletionStates, GameProviders</summary>
        private void RecalcStats()
        {
            var s = Settings;

            Func<ulong, ulong> ToMinutes = raw => raw / 60UL;

            var games = PlayniteApi.Database.Games.ToList();
            if (!s.IncludeHidden)
            {
                games = games.Where(g => g.Hidden != true).ToList();
            }

            // === Totaux
            s.TotalCount = games.Count;
            s.InstalledCount = games.Count(g => g.IsInstalled == true);
            s.NotInstalledCount = games.Count(g => g.IsInstalled != true);
            s.HiddenCount = games.Count(g => g.Hidden == true);
            s.FavoriteCount = games.Count(g => g.Favorite == true);

            // === Playtime total/moyen (minutes)
            ulong totalMinutes = (ulong)games.Sum(g => (long)ToMinutes(g.Playtime));
            s.TotalPlaytimeMinutes = totalMinutes;

            var played = games.Where(g => ToMinutes(g.Playtime) > 0UL).ToList();
            s.AveragePlaytimeMinutes = (ulong)(played.Count == 0 ? 0 : played.Sum(g => (long)ToMinutes(g.Playtime)) / played.Count);

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
                        PlaytimeString = AnikiHelperSettings.PlaytimeToString(gMin, s.PlaytimeUseDaysFormat),
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
                    // Ignore les jeux qui n'ont pas été joués ce mois-ci (Ignore games that have not been played this month)
                    if (g.LastActivity == null || g.LastActivity < monthStart)
                    {
                        continue;
                    }

                    var currMinutes = ToMinutes(g.Playtime);

                    // si le jeu n'est PAS dans le snapshot, on l'IGNORE pour ce mois (if the game is NOT in the snapshot, IGNORE it for this month)
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
                    s.ThisMonthTopGamePlaytime = AnikiHelperSettings.PlaytimeToString(topMinutes, s.PlaytimeUseDaysFormat);

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
                    s.ThisMonthTopGameName = "—";
                    s.ThisMonthTopGamePlaytime = "";
                    s.ThisMonthTopGameCoverPath = string.Empty;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Month snapshot calc failed; continuing with other sections.");
                s.ThisMonthPlayedCount = 0;
                s.ThisMonthPlayedTotalMinutes = 0;
                s.ThisMonthTopGameName = "—";
                s.ThisMonthTopGamePlaytime = "";
                s.ThisMonthTopGameCoverPath = string.Empty;
            }

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

            // === Listes rapides
            string SafeName(string name) => string.IsNullOrWhiteSpace(name) ? "(Unnamed Game)" : name;

            s.RecentPlayed.Clear();
            foreach (var g in games.Where(x => x.LastActivity != null).OrderByDescending(x => x.LastActivity).Take(5))
            {
                var dt = g.LastActivity?.ToLocalTime().ToString("dd/MM/yyyy");
                s.RecentPlayed.Add(new QuickItem { Name = SafeName(g.Name), Value = dt });
            }

            s.RecentAdded.Clear();
            foreach (var g in games.Where(x => x.Added != null).OrderByDescending(x => x.Added).Take(5))
            {
                var dt = g.Added?.ToLocalTime().ToString("dd/MM/yyyy");
                s.RecentAdded.Add(new QuickItem { Name = SafeName(g.Name), Value = dt });
            }

            s.NeverPlayed.Clear();

            var neverPlayed = games
                .Where(g => g.Playtime == 0UL && g.PlayCount == 0UL && g.LastActivity == null)
                .OrderBy(g => g.Added.HasValue ? g.Added.Value : DateTime.MinValue) // force tri du plus ancien
                .ThenBy(g => g.Name)
                .Take(5);

            foreach (var g in neverPlayed)
            {
                var addedStr = g.Added.HasValue
                    ? g.Added.Value.ToLocalTime().ToString("dd/MM/yyyy")
                    : "";
                s.NeverPlayed.Add(new QuickItem { Name = Safe(g.Name), Value = addedStr });
            }

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

            // Calcul du jeu suggéré à partir des stats + snapshot
            RecalcSuggestedGameSafe();
        }

        #region Menus

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
