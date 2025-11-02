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


namespace AnikiHelper
{
    public class AnikiHelper : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        // === Diagnostics et chemins ===
        private string GetDataRoot() => Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());

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


        // Format minutes -> "3h27"
        private static string FormatHhMmFromMinutes(int minutes)
        {
            if (minutes < 0) minutes = 0;
            var h = minutes / 60;
            var m = minutes % 60;
            return $"{h}h{m:00}";
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

        // --- Supprime le cache de couleurs dynamiques (fichier JSON uniquement) ---
        public void ClearDynamicColorCache()
        {
            try
            {
                var dir = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());
                var file = Path.Combine(dir, "palette_cache_v1.json");

                if (File.Exists(file))
                {
                    File.Delete(file);
                    logger.Info($"[AnikiHelper] Deleted palette cache file: {file}");
                }
                else
                {
                    logger.Info("[AnikiHelper] Palette cache file not found; nothing to delete.");
                }
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
