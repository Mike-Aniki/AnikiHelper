using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace AnikiHelper
{
    public class AnikiHelper : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly SteamGlobalNewsService steamGlobalNewsService;

        public static AnikiHelper Instance { get; private set; }

        private const int GlobalNewsRefreshIntervalHours = 3;


        // === Diagnostics and paths ===
        private string GetDataRoot() => Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());

        // Charge les news globales depuis CacheNews.json si Settings.SteamGlobalNews est vide
        private void LoadNewsFromCacheIfNeeded()
        {
            try
            {
                // Si on a déjà des news en mémoire, on ne touche à rien
                if (Settings.SteamGlobalNews != null && Settings.SteamGlobalNews.Count > 0)
                {
                    return;
                }

                var path = Path.Combine(GetDataRoot(), "CacheNews.json");
                if (!File.Exists(path))
                {
                    return;
                }

                // On lit le JSON existant
                var cached = Serialization.FromJsonFile<List<SteamGlobalNewsItem>>(path);
                if (cached == null || cached.Count == 0)
                {
                    return;
                }

                // On ne garde que les dernières news, triées par date
                var ordered = cached
                    .OrderByDescending(n => n.PublishedUtc)
                    .Take(30)   // 30 == DisplayMaxItems, adapte si besoin
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

                // Format actuel : steamId -> SteamUpdateCacheEntry
                var asNew = Serialization.FromJson<Dictionary<string, SteamUpdateCacheEntry>>(json);
                if (asNew != null)
                {
                    return asNew;
                }

                // Old format: steamId -> title (converted on the fly)
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
                // ce n'est qu'un cache, on ignore
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
                            // We find the game corresponding to this SteamID
                            game = PlayniteApi.Database.Games
                                .FirstOrDefault(g => GetSteamGameId(g) == steamId);
                        }
                        catch
                        {
                            // if it breaks, the game remains null
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

        // === Global toast helper ===
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
                // visuel only
            }
        }

        // === Localisation helper (utilise les ressources du thème) ===
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


        // ✅ helper for game name
        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "(Unnamed Game)" : s;

        // Nettoie le HTML Steam pour l'affichage en Fullscreen
        private static string CleanHtml(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var html = raw;

            try
            {
                // 1) Supprimer complètement les images
                html = Regex.Replace(html, "<img[^>]*>", string.Empty, RegexOptions.IgnoreCase);

                // 2) Remplacer les liens par leur texte interne (on garde le contenu, pas le href)
                html = Regex.Replace(
                    html,
                    "<a[^>]*>(.*?)</a>",
                    "$1",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                );

                // 3) Supprimer les attributs class="..." et style="..."
                html = Regex.Replace(html, "\\sclass=\"[^\"]*\"", string.Empty, RegexOptions.IgnoreCase);
                html = Regex.Replace(html, "\\sstyle=\"[^\"]*\"", string.Empty, RegexOptions.IgnoreCase);

                // 4) Supprimer les <p> vides
                html = Regex.Replace(html, "<p>\\s*</p>", string.Empty, RegexOptions.IgnoreCase);

                // 5) Réduire les gros paquets de <br> successifs
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
                // si ça foire, on renvoie simplement le HTML brut
                return raw;
            }

            return html;
        }


        // VM + Settings
        public AnikiHelperSettingsViewModel SettingsVM { get; private set; }
        public AnikiHelperSettings Settings => SettingsVM.Settings;

        // GUID du plugin
        public override Guid Id { get; } = Guid.Parse("96a983a3-3f13-4dce-a474-4052b718bb52");


        // === Session tracking (start/stop) ===
        private readonly Dictionary<Guid, DateTime> sessionStartAt = new Dictionary<Guid, DateTime>();
        private readonly Dictionary<Guid, ulong> sessionStartPlaytimeMinutes = new Dictionary<Guid, ulong>(); // Playnite = secondes -> minutes stockées ici
        private readonly Dictionary<Guid, HashSet<string>> sessionStartUnlocked = new Dictionary<Guid, HashSet<string>>();

        // === Steam Update (badge "new" pour la session en cours) ===
        private readonly HashSet<string> steamUpdateNewThisSession = new HashSet<string>();

        // === Steam Update (toasts déjà affichés pour cette session) ===
        private readonly HashSet<string> steamUpdateToastShownThisSession = new HashSet<string>();

        // === Steam Update (RSS simplifié) ===
        private readonly SteamUpdateLiteService steamUpdateService;
        private readonly DispatcherTimer steamUpdateTimer;
        private Playnite.SDK.Models.Game pendingUpdateGame;

        // === Steam current players ===
        private readonly SteamPlayerCountService steamPlayerCountService = new SteamPlayerCountService();

        // GUID du plugin Steam officiel (Playnite)
        private static readonly Guid SteamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");


        // Format minutes -> "3h27"
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
                // 1) Jeu provenant directement du plugin Steam
                if (game.PluginId == SteamPluginId && !string.IsNullOrWhiteSpace(game.GameId))
                {
                    return game.GameId;
                }

                // 2) Sinon, on tente de trouver un lien Steam dans Game.Links
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
                // on s'en fout, on retourne null si ça foire
            }

            return null;
        }

        // Retourne les N derniers jeux joués qui ont un SteamID valable
        private List<Playnite.SDK.Models.Game> GetRecentSteamGames(int maxGames)
        {
            try
            {
                var games = PlayniteApi.Database.Games
                    .Where(g => g.LastActivity != null)                 // déjà lancés
                    .OrderByDescending(g => g.LastActivity)             // du plus récent au plus ancien
                    .Take(Math.Max(1, maxGames))                        // sécurité
                    .ToList();

                // on ne garde que ceux qui ont un SteamID
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

            // Si le scan des mises à jour est désactivé, on ne fait PAS l'appel patchnote
            if (Settings.SteamUpdatesScanEnabled)
            {
                await UpdateSteamUpdateForGameAsync(g);
            }
            else
            {
                
                ResetSteamUpdate();
            }

            // Compteur de joueurs Steam reste actif même si les updates sont coupées
            await UpdateSteamPlayerCountForGameAsync(g);
        }


        private async Task RefreshGlobalSteamNewsAsync(bool force = false)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var last = Settings.SteamGlobalNewsLastRefreshUtc;

                // Log minimal d'entrée
                logger.Debug($"[NewsScan] Start (force={force}, enabled={Settings.NewsScanEnabled})");

                // Scan désactivé
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




        private async Task TryScanGlobalNewsAsync(bool force, bool silent)
        {
            var logger = LogManager.GetLogger();

            try
            {
                // --- Option désactivée ---
                if (!Settings.NewsScanEnabled)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                var lastScan = Settings.LastNewsScanUtc;

                // --- Cooldown (si pas forcé) ---
                if (!force && lastScan != DateTime.MinValue)
                {
                    var hoursSince = (now - lastScan).TotalHours;
                    if (hoursSince < GlobalNewsRefreshIntervalHours)
                    {
                        return; // Trop tôt → stop
                    }
                }

                // --- Scan RSS ---
                var svc = new SteamGlobalNewsService(PlayniteApi, Settings);
                var items = await svc.GetGlobalNewsAsync();

                // --- Mise à jour UI + settings ---
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Settings.SteamGlobalNews.Clear();
                    foreach (var it in items)
                        Settings.SteamGlobalNews.Add(it);

                    Settings.LastNewsScanUtc = now;
                    SavePluginSettings(Settings);
                });

                // Log minimal
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


        // Met à jour le cache Steam pour un jeu donné au démarrage
        // Renvoie true si un toast a été envoyé, false sinon.
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

                // 🔴 CAS 1 : aucune entrée dans le cache pour ce jeu → on initialise SANS toast
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
                    // pas de steamUpdateNewThisSession, pas de toast
                    return false;
                }

                // 🟢 CAS 2 : le jeu est déjà connu dans le cache → on peut parler de "nouvelle maj"
                var lastPublished = cachedEntry.LastPublishedUtc;
                var sessionKey = $"{steamId}|{result.Title}";

                // "vraie" nouvelle update = date Steam plus récente que celle du cache
                bool isRealNew = published > lastPublished;

                if (isRealNew)
                {
                    // maj complète du cache
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
                            Loc("LOCSteamUpdateToast", "New update available for {0}"),
                            Safe(game.Name));

                        ShowGlobalToast(msg, "steamUpdate");
                        notified = true;
                    }
                }
                else
                {
                    // Pas une nouvelle version, mais on enrichit le cache si nécessaire
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



        // Au démarrage : vérifie les updates Steam pour les N derniers jeux joués (en tâche de fond)
        private async Task CheckSteamUpdatesForRecentGamesAsync(int maxGames = 30)
        {
            try
            {
                // 1) Vérif mode + focus
                if (!IsSteamRecentScanAllowed())
                {
                    return;
                }

                // 2) Limite de fréquence : pas plus d'un scan toutes les 2 heures
                var nowUtc = DateTime.UtcNow;
                var last = Settings.LastSteamRecentCheckUtc;

                if (last.HasValue && (nowUtc - last.Value).TotalHours < 2)
                {
                    // dernier scan trop récent → on sort
                    return;
                }

                // On enregistre tout de suite le timestamp pour ne pas rescanner 10 fois au démarrage
                Settings.LastSteamRecentCheckUtc = nowUtc;
                SavePluginSettings(Settings);

                // 3) Candidats = jeux avec une LastActivity, triés du plus récent au plus ancien
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

                    // Si on perd le focus en cours de route, on arrête
                    if (!IsSteamRecentScanAllowed())
                    {
                        break;
                    }

                    var steamId = GetSteamGameId(g);
                    if (string.IsNullOrWhiteSpace(steamId))
                    {
                        continue; // pas de SteamID => on ignore simplement
                    }

                    scanned++;

                    var notified = await UpdateSteamUpdateCacheOnlyForGameAsync(g);

                    // Si un toast a été affiché, on laisse ton anim vivre sa vie (~9 s)
                    // Sinon, petit délai pour ne pas spammer l'API.
                    var delayMs = notified ? 10000 : 500;

                    // On découpe le delay en petits morceaux pour pouvoir
                    // arrêter proprement si la fenêtre perd le focus.
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

                // On reconstruit la liste des 10 dernières updates depuis le cache
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

                // === 1) On tente d'abord le CACHE ===
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
                        Settings.SteamUpdateIsNew = false; // le badge "NEW" sera géré après par la réponse Steam
                    });

                    hadUsableCache = true;
                }

                // === 2) Puis on essaie d'appeler Steam pour rafraîchir ===
                var result = await steamUpdateService.GetLatestUpdateAsync(steamId);
                if (result == null || string.IsNullOrWhiteSpace(result.Title))
                {
                    // Pas de résultat Steam
                    if (!hadUsableCache)
                    {
                        // Aucun cache exploitable -> afficher une erreur
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            Settings.SteamUpdateError = "No update available";
                            Settings.SteamUpdateAvailable = false;
                            Settings.SteamUpdateIsNew = false;
                        });
                    }
                    // Si on avait déjà quelque chose en cache, on laisse l'affichage tel quel
                    return;
                }

                var cleanedHtml = CleanHtml(result.HtmlBody ?? string.Empty);

                // --- Detection of "new update" ---
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

                // === Pas encore de cache pour ce jeu ===
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

                    // uniquement si la DATE est plus récente que celle du cache.
                    bool isRealNew =
                        lastPublished == DateTime.MinValue ||        // vieux cache sans date fiable
                        published > lastPublished;                   // publication plus récente

                    if (isRealNew)
                    {
                        // ✅ Nouvelle update -> badge NEW + maj du cache
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
                        // ❌ Même version (même date) -> pas NEW,
                        // mais on peut quand même mettre à jour le texte/HTML (changement de langue etc.)

                        if (string.IsNullOrWhiteSpace(cachedEntry?.Html) ||
                            !string.Equals(cachedEntry.Title, result.Title, StringComparison.Ordinal))
                        {
                            cachedEntry.Html = cleanedHtml;
                            cachedEntry.Title = result.Title;
                            cachedEntry.LastPublishedUtc = published;
                            cache[steamId] = cachedEntry;
                            SaveSteamUpdatesCache(cache);
                        }

                        // On garde le badge NEW si on l'avait déjà déclenché dans cette session
                        if (steamUpdateNewThisSession.Contains(sessionKey))
                        {
                            isNew = true;
                        }
                    }
                }


                // --- 3) On pousse la version "fraîche" dans les Settings (par-dessus le cache) ---
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

                // ✅ Toast global uniquement si c'est une nouvelle update (titre différent du cache)
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

                // Si on n'avait rien en cache, on affiche une erreur ;
                // si on avait déjà un patchnote depuis le cache, on ne touche plus à l'UI.
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

                            // déjà présent avec Html -> on saute
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




        // ====== SuccessStory helpers ======

        // Try to find the "SuccessStory" folder in ExtensionsData
        private string FindSuccessStoryRoot()
        {
            try
            {
                var root = PlayniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

                // cas "classique"
                var classic = Path.Combine(root, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                if (Directory.Exists(classic)) return classic;

                // fallback: chercher récursivement un dossier se terminant par "SuccessStory"
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (dir.EndsWith("SuccessStory", StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
            catch { }
            return null;
        }

        // Loads all "unlocked" achievements for a game (stable keys)
        private HashSet<string> GetUnlockedAchievementKeysForGame(Playnite.SDK.Models.Game game)
        {
            var set = new HashSet<string>();
            try
            {
                var ssRoot = FindSuccessStoryRoot();
                if (string.IsNullOrEmpty(ssRoot) || game == null) return set;

                var files = Directory.EnumerateFiles(ssRoot, "*.json", SearchOption.AllDirectories).ToArray();
                if (files.Length == 0) return set;

                foreach (var file in files)
                {
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch { continue; }

                    // Lecture “souple” via Playnite Serialization -> dynamic
                    dynamic rootObj;
                    try { rootObj = Serialization.FromJson<dynamic>(text); }
                    catch { continue; }

                    // Essayer de récupérer le nom du jeu dans le JSON sans types forts
                    string fileGameName = null;
                    try
                    {
                        // rootObj.Name ou rootObj.Game.Name (si présent)
                        fileGameName = (string)(rootObj?.Name ?? rootObj?.Game?.Name);
                    }
                    catch { /* ignore */ }

                    bool maybeSameGame;
                    if (!string.IsNullOrWhiteSpace(fileGameName))
                    {
                        maybeSameGame = string.Equals(fileGameName, game.Name, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        // fallback large : chercher le nom du jeu dans le texte
                        var gname = game.Name ?? "";
                        maybeSameGame = !string.IsNullOrWhiteSpace(gname) &&
                                        text.IndexOf(gname, StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    if (!maybeSameGame)
                        continue;

                    // Récupérer la collection d’items (Items ou Achievements)
                    IEnumerable<object> items = null;
                    try
                    {
                        items = (IEnumerable<object>)(rootObj?.Items);
                        if (items == null)
                            items = (IEnumerable<object>)(rootObj?.Achievements);
                    }
                    catch { items = null; }

                    if (items == null)
                        continue;

                    foreach (var it in items)
                    {
                        bool unlocked = false;
                        try
                        {
                            // accéder aux champs dynamiques prudemment
                            var d = (dynamic)it;
                            string dateUnlocked = null;
                            try { dateUnlocked = (string)d.DateUnlocked; } catch { }
                            long? unlockTime = null;
                            try { unlockTime = (long?)d.UnlockTime; } catch { }
                            string isUnlock = null, earned = null, unlockedStr = null;
                            try { isUnlock = (string)d.IsUnlock; } catch { }
                            try { earned = (string)d.Earned; } catch { }
                            try { unlockedStr = (string)d.Unlocked; } catch { }

                            if (!string.IsNullOrWhiteSpace(dateUnlocked) && !dateUnlocked.StartsWith("0001-01-01")) unlocked = true;
                            if (unlockTime != null) unlocked = true;
                            if (string.Equals(isUnlock, "true", StringComparison.OrdinalIgnoreCase)) unlocked = true;
                            if (string.Equals(earned, "true", StringComparison.OrdinalIgnoreCase)) unlocked = true;
                            if (string.Equals(unlockedStr, "true", StringComparison.OrdinalIgnoreCase)) unlocked = true;
                        }
                        catch { }

                        if (!unlocked) continue;

                        string id = null, name = null, title = null;
                        try { id = (string)((dynamic)it).Id; } catch { }
                        try { name = (string)((dynamic)it).Name; } catch { }
                        try { title = (string)((dynamic)it).Title; } catch { }

                        var key = !string.IsNullOrWhiteSpace(id) ? id
                                : !string.IsNullOrWhiteSpace(name) ? name
                                : !string.IsNullOrWhiteSpace(title) ? title
                                : null;

                        if (!string.IsNullOrWhiteSpace(key))
                            set.Add(key);
                    }
                }
            }
            catch { }
            return set;
        }



        // === Monthly snapshot (in the folder for THIS extension) ===
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
                    // minutes (Playnite stocke playtime en secondes)
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
                    logger.Info($"[AnikiHelper] Created monthly snapshot automatically: {file}");
                }

                UpdateSnapshotInfoProperty(monthStart);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] EnsureCurrentMonthSnapshotExists failed");
            }
        }

        // Reset snapshot (repart de maintenant)
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

        // --- Supprime le cache de couleurs dynamiques (JSON disque + RAM) ---
        public void ClearDynamicColorCache()
        {
            try
            {
                // 1) Purge RAM + timers côté moteur
                DynamicAuto.ClearPersistentCache(alsoRam: true);

                // 2) Supprime les fichiers de cache disque (nouveau + tmp + ancien v1)
                var dir = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());
                var fileNew = Path.Combine(dir, "palette_cache.json");
                var fileTmp = fileNew + ".tmp";
                var fileOld = Path.Combine(dir, "palette_cache_v1.json"); // pour nettoyer l’héritage

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

        // --- Supprime le cache de News ---
        public void ClearNewsCache()
        {
            try
            {
                // 1) Vider la liste en mémoire
                Settings.SteamGlobalNews?.Clear();

                // 2) Reset des timestamps pour forcer un rescan
                Settings.SteamGlobalNewsLastRefreshUtc = null;
                Settings.LastNewsScanUtc = DateTime.MinValue;

                // 3) Supprimer le fichier JSON
                var jsonPath = Path.Combine(GetDataRoot(), "CacheNews.json");
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                }

                // 4) Supprimer le dossier NewsImages (miniatures)
                var imgRoot = Path.Combine(GetDataRoot(), "NewsImages");
                if (Directory.Exists(imgRoot))
                {
                    Directory.Delete(imgRoot, true);
                }

                // 5) Sauvegarder les settings mis à jour
                SavePluginSettings(Settings);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] ClearNewsCache failed.");
            }
        }



        // Met à jour le texte d’info snapshot
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

            // --- ViewModel ---
            SettingsVM = new AnikiHelperSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            // Charger les settings Playnite
            var saved = LoadPluginSettings<AnikiHelperSettings>();
            if (saved != null)
            {
                SettingsVM.Settings = saved;
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

            // Timer pour les updates Steam (debounce changement de jeu)
            steamUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            steamUpdateTimer.Tick += steamUpdateTimer_Tick;
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

        // === Settings plumbing pour l’UI des Add-ons ===
        public override ISettings GetSettings(bool firstRunSettings)
        {
            // Utilise le même ViewModel que tu crées dans le constructeur
            return SettingsVM ?? (SettingsVM = new AnikiHelperSettingsViewModel(this));
        }

        // === Settings UI pour l’onglet Add-ons ===
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
                // Si le scan des mises à jour est désactivé, on ne propose rien
                if (!Settings.SteamUpdatesScanEnabled)
                {
                    return;
                }

                // On ne fait ça qu'en mode Fullscreen
                if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                // Si le cache existe déjà OU qu'on a déjà posé la question -> on ne fait rien
                var cachePath = GetSteamUpdatesCachePath();
                if (File.Exists(cachePath) || !Settings.AskSteamUpdateCacheAtStartup)
                {
                    return;
                }

                // Texte et titre (avec support de localisation si tu ajoutes les clés plus tard)
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
                    // On ne reproposera plus automatiquement
                    Settings.AskSteamUpdateCacheAtStartup = false;
                    SavePluginSettings(Settings);

                    // Lance le scan global (fenêtre de progression Playnite)
                    _ = InitializeSteamUpdatesCacheForAllGamesAsync();
                }
                else
                {
                    // L'utilisateur ne veut pas => on ne repose plus la question
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
                    // On ne sait pas → on ne bloque pas
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
            // Si le scan des mises à jour est désactivé on block
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

            // ----------------------------------------------
            // 🟥 MODE DESKTOP → NE RIEN FAIRE
            // ----------------------------------------------
            if (PlayniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            // ----------------------------------------------
            // 🟩 FULLSCREEN SEULEMENT À PARTIR D'ICI
            // ----------------------------------------------

            // --- Snapshots + Stats ---
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

            // --- Reset "safe" des notifications au démarrage ---
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
            // 1) charger immédiatement ce qu'il y a dans le JSON
            try
            {
                LoadNewsFromCacheIfNeeded();
            }
            catch { }

            // --- Steam Recent Updates (10 dernières mises à jour) ---
            try
            {
                RefreshSteamRecentUpdatesFromCache();
            }
            catch { }


            // 2) lancer un scan RSS différé de 10s, limité à 1 fois / 3h
            //    (seulement si le scan des news est activé dans les paramètres)
            if (Settings.NewsScanEnabled)
            {
                try
                {
                    _ = ScheduleGlobalSteamNewsRefreshAsync();
                }
                catch { }
            }


            // --- Random login screen ---
            try
            {
                var rand = new Random();
                const int max = 32;

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

            // --- Prompt éventuel pour construire le cache global Steam ---
            try
            {
                TryAskForSteamUpdateCacheOnStartup();
            }
            catch { }

            // --- Scan auto des mises à jour Steam (Fullscreen only) ---
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
                // Délai après démarrage pour laisser Playnite respirer
                await Task.Delay(TimeSpan.FromSeconds(9));

                // Si entre temps on a perdu le focus ou qu'on n'est pas en Fullscreen → on ne fait rien
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
                LogManager.GetLogger().Info("[NEWS DEBUG] ScheduleGlobalSteamNewsRefreshAsync START → waiting 6s...");
                await Task.Delay(6000);

                if (!Settings.NewsScanEnabled)
                {
                    LogManager.GetLogger().Info("[NEWS DEBUG] Schedule: ABORT → NewsScanEnabled = false");
                    return;
                }

                LogManager.GetLogger().Info("[NEWS DEBUG] Schedule: CALL TryScanGlobalNewsAsync(force:false, silent:true)");
                await TryScanGlobalNewsAsync(force: false, silent: true);
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "[NEWS ERROR] ScheduleGlobalSteamNewsRefreshAsync failed.");
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

            sessionStartUnlocked[g.Id] = GetUnlockedAchievementKeysForGame(g);
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            base.OnGameStopped(args);

            var g = args?.Game;
            if (g != null)
            {
                // --- 1) Durée de session ---
                var start = sessionStartAt.ContainsKey(g.Id) ? sessionStartAt[g.Id] : DateTime.Now;
                var elapsed = DateTime.Now - start;
                var sessionMinutes = (int)Math.Max(0, Math.Round(elapsed.TotalMinutes));

                // --- 2) Total playtime ---
                var totalMinutes = (int)(g.Playtime / 60UL);
                if (totalMinutes <= 0 && sessionStartPlaytimeMinutes.ContainsKey(g.Id))
                {
                    totalMinutes = (int)sessionStartPlaytimeMinutes[g.Id] + sessionMinutes;
                }

                // --- 3) Nouveaux succès ---
                var before = sessionStartUnlocked.ContainsKey(g.Id) ? sessionStartUnlocked[g.Id] : new HashSet<string>();
                var after = GetUnlockedAchievementKeysForGame(g);
                var newCount = after.Except(before).Count();

                // --- 4) Push vers Settings (pour le thème) ---
                // ⚠️ TOUT ce qui est bindé par le XAML doit être mis à jour sur le thread UI
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var s = Settings;

                    s.SessionGameName = string.IsNullOrWhiteSpace(g.Name) ? "(Unnamed Game)" : g.Name;
                    s.SessionDurationString = FormatHhMmFromMinutes(sessionMinutes);
                    s.SessionTotalPlaytimeString = FormatHhMmFromMinutes(Math.Max(0, totalMinutes));

                    // Si tu veux masquer la ligne trophées quand 0, mets chaîne vide + le bool pour ton DataTrigger
                    s.SessionNewAchievementsString = newCount > 0
                        ? $"+{newCount} trophée{(newCount > 1 ? "s" : "")}"
                        : string.Empty;               // <- vide quand 0

                    s.SessionNewAchievementsCount = newCount;
                    s.SessionHasNewAchievements = newCount > 0;



                    // Déclencheur : changer une valeur arbitraire + flip PUIS armer
                    s.SessionNotificationStamp = Guid.NewGuid().ToString();
                    s.SessionNotificationFlip = !s.SessionNotificationFlip;   // 1) change l’état cible
                    s.SessionNotificationArmed = true;                         // 2) armé après → une seule branche s’active


                });

                // --- 5) Nettoyage cache ---
                sessionStartAt.Remove(g.Id);
                sessionStartPlaytimeMinutes.Remove(g.Id);
                sessionStartUnlocked.Remove(g.Id);
            }

            EnsureMonthlySnapshotSafe();
            RecalcStatsSafe();
        }





        #endregion

        private void RecalcStatsSafe()
        {
            try { RecalcStats(); }
            catch (Exception ex) { logger.Error(ex, "[AnikiHelper] RecalcStats failed"); }
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

            // ===== CE MOIS-CI (delta vs snapshot) =====
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
                    // ⚠ Ignore les jeux qui n'ont pas été joués ce mois-ci
                    if (g.LastActivity == null || g.LastActivity < monthStart)
                    {
                        continue;
                    }

                    var currMinutes = ToMinutes(g.Playtime);

                    if (snapshot.TryGetValue(g.Id, out var baseMinutes))
                    {
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
        }

        #region Menus

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield break; // aucun menu
        }


        #endregion
    }

    // --- Helper visuel pour parcourir la hiérarchie WPF ---
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
