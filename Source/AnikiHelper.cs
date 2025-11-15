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

        // === Diagnostics et chemins ===
        private string GetDataRoot() => Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());

        // --- Cache des updates Steam déjà vues (SteamID -> dernier titre d'update) ---
        private string GetSteamUpdatesCachePath()
            => Path.Combine(GetDataRoot(), "steam_updates_cache.json");

        private Dictionary<string, string> LoadSteamUpdatesCache()
        {
            try
            {
                var path = GetSteamUpdatesCachePath();
                if (!File.Exists(path))
                {
                    return new Dictionary<string, string>();
                }

                var json = File.ReadAllText(path);
                return Serialization.FromJson<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private void SaveSteamUpdatesCache(Dictionary<string, string> cache)
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
                // on ignore en silence, ce n'est qu'un cache
            }
        }


        // ✅ helper pour nom de jeu
        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "(Unnamed Game)" : s;

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
        // Mémorise quels patchs (SteamID + Titre) sont considérés comme "nouveaux" pendant CETTE session Playnite
        private readonly HashSet<string> steamUpdateNewThisSession = new HashSet<string>();


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
                // pas grave
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
                // on ignore
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

            await UpdateSteamUpdateForGameAsync(g);
            await UpdateSteamPlayerCountForGameAsync(g);
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

                // On va chercher la dernière update sur le RSS Steam (avec langue + fallback EN)
                var result = await steamUpdateService.GetLatestUpdateAsync(steamId);
                if (result == null)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Settings.SteamUpdateError = "No update available";
                        Settings.SteamUpdateAvailable = false;
                        Settings.SteamUpdateIsNew = false;
                    });
                    return;
                }

                // --- Détection "nouvelle update" via cache JSON + 'grâce' sur la session courante ---
                bool isNew = false;
                var sessionKey = $"{steamId}|{result.Title}";

                try
                {
                    var cache = LoadSteamUpdatesCache();

                    // Cas 1 : le JSON ne connaît pas encore ce titre -> vrai nouveau patch global
                    if (!cache.TryGetValue(steamId, out var lastTitle) ||
                        !string.Equals(lastTitle, result.Title, StringComparison.Ordinal))
                    {
                        isNew = true;

                        // On enregistre tout de suite ce nouveau titre dans le cache persistant
                        cache[steamId] = result.Title;
                        SaveSteamUpdatesCache(cache);

                        // Et on se souvient que pour CETTE session, ce patch est "new"
                        steamUpdateNewThisSession.Add(sessionKey);
                    }
                    else
                    {
                        // Cas 2 : le JSON connaît déjà ce titre (patch déjà vu dans une session précédente)
                        // -> on regarde si, malgré ça, on l'a marqué "new" dans CETTE session
                        if (steamUpdateNewThisSession.Contains(sessionKey))
                        {
                            isNew = true;
                        }
                    }
                }
                catch
                {
                    // si le cache plante, on ne marque pas comme "new"
                    isNew = false;
                }


                // --- Push vers les Settings (binding du thème) ---
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Settings.SteamUpdateTitle = result.Title;
                    Settings.SteamUpdateDate = result.Published == DateTime.MinValue
                        ? string.Empty
                        : result.Published.ToString("dd/MM/yyyy HH:mm");

                    Settings.SteamUpdateHtml = result.HtmlBody;
                    Settings.SteamUpdateAvailable = true;
                    Settings.SteamUpdateError = string.Empty;

                    // 🔴 le bool qui servira au badge
                    Settings.SteamUpdateIsNew = isNew;
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] UpdateSteamUpdateForGameAsync failed.");
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Settings.SteamUpdateError = "Error while loading update";
                    Settings.SteamUpdateAvailable = false;
                    Settings.SteamUpdateIsNew = false;
                });
            }
        }

        private async Task UpdateSteamPlayerCountForGameAsync(Playnite.SDK.Models.Game game)
        {
            try
            {
                // Si la feature est désactivée, on nettoie et on sort
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

                        // Format "34 521 players online"
                        Settings.SteamCurrentPlayersString = $"{result.PlayerCount:N0} players online";
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

                        // On garde uniquement les jeux Steam avec un SteamID
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

                            // déjà présent dans le cache -> on saute
                            if (cache.ContainsKey(entry.SteamId))
                            {
                                continue;
                            }

                            // appel "sync" sur la méthode async
                            var result = steamUpdateService
                                .GetLatestUpdateAsync(entry.SteamId)
                                .GetAwaiter()
                                .GetResult();

                            if (result == null || string.IsNullOrWhiteSpace(result.Title))
                            {
                                continue;
                            }

                            cache[entry.SteamId] = result.Title;
                            updated++;

                            // petit throttle pour ne pas spammer l’API
                            System.Threading.Thread.Sleep(150);
                        }

                        SaveSteamUpdatesCache(cache);
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

        // Essaie de trouver le dossier "SuccessStory" dans ExtensionsData
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

        // Charge l'ensemble des succès "débloqués" pour un jeu (clés stables)
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



        // === Snapshot mensuel (dans le dossier de CETTE extension) ===
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
            SettingsVM = new AnikiHelperSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            // Langue Playnite -> Steam
            var playniteLang = api?.ApplicationSettings?.Language; // "fr_FR", "en_US", etc.
            steamUpdateService = new SteamUpdateLiteService(playniteLang);

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

            // --- Timer pour les updates Steam (debounce changement de jeu) ---
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



        #region Lifecycle

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

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

            // --- Reset "safe" de la notification au démarrage ---
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


            // === UI Fullscreen : uniquement en Fullscreen ===
            if (PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen)
            {
                AddonsUpdateStyler.Start();

                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                    () => DynamicAuto.Init(PlayniteApi),
                    System.Windows.Threading.DispatcherPriority.Loaded
                );
            }

            // 👉 Fullscreen only
            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(
                () => SettingsWindowStyler.Start(),
                System.Windows.Threading.DispatcherPriority.Loaded
            );

            // === Random login screen ===
            try
            {
                var rand = new Random();            // ou: new Random(int.Parse(DateTime.Today.ToString("yyyyMMdd")));
                const int max = 32;                 // nombre de vidéos dispo (1..29)

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

                foreach (var g in PlayniteApi.Database.Games)
                {
                    var currMinutes = g.Playtime / 60UL;

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
