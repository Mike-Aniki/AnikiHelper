using AnikiHelper.Models;
using System.Collections.ObjectModel;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace AnikiHelper
{
    public class AnikiHelper : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        // ===================== Helpers dynamiques (pour JSON sans Newtonsoft) =====================
        private static IEnumerable<dynamic> AsDynEnumerable(object maybe)
        {
            if (maybe == null) yield break;
            if (maybe is string) yield break;
            if (maybe is System.Collections.IEnumerable en)
            {
                foreach (var x in en) yield return x;
            }
        }
        private static string DynStr(object v) => v?.ToString();
        private static DateTime? DynDate(object v)
        {
            if (v == null) return null;
            if (DateTime.TryParse(v.ToString(), out var dt)) return dt;
            if (long.TryParse(v.ToString(), out var unix)) return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            return null;
        }
        private static object DynProp(dynamic obj, string name)
        {
            try
            {
                var t = (object)obj;
                var pi = t?.GetType().GetProperty(name);
                return pi?.GetValue(t, null);
            }
            catch { return null; }
        }
        // ==========================================================================================

        // === Diagnostics et chemins ===
        private string GetDataRoot() => Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, Id.ToString());

        // ✅ helper pour nom de jeu
        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "(Unnamed Game)" : s;

        // VM + Settings
        public AnikiHelperSettingsViewModel SettingsVM { get; private set; }
        public AnikiHelperSettings Settings => SettingsVM.Settings;

        // GUID du plugin
        public override Guid Id { get; } = Guid.Parse("96a983a3-3f13-4dce-a474-4052b718bb52");

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

        private void LogMonthlyDir()
        {
            try { logger.Info($"[AnikiHelper] monthly dir = {GetMonthlyDir()}"); }
            catch { /* ignore */ }
        }

        // --- Optionnel : lecture GameActivity (si tu t'en sers plus tard) ---
        private ulong GetMonthMinutesFromGameActivity(Guid gameId, DateTime monthStart, DateTime monthEnd)
        {
            try
            {
                var extData = PlayniteApi.Paths.ExtensionsDataPath;
                if (!Directory.Exists(extData)) return 0UL;

                foreach (var dir in Directory.GetDirectories(extData))
                {
                    var gaDir = Path.Combine(dir, "GameActivity");
                    if (!Directory.Exists(gaDir)) continue;

                    var jsonPath = Path.Combine(gaDir, $"{gameId}.json");
                    if (!File.Exists(jsonPath)) continue;

                    dynamic root;
                    try { root = Serialization.FromJson<dynamic>(File.ReadAllText(jsonPath)); }
                    catch { continue; }

                    var sessions = AsDynEnumerable(DynProp(root, "Sessions"))
                                   ?? AsDynEnumerable(DynProp(root, "Activities"))
                                   ?? AsDynEnumerable(root);

                    ulong total = 0UL;

                    foreach (var tok in sessions)
                    {
                        var dateStr = DynStr(DynProp(tok, "DateSession")) ??
                                      DynStr(DynProp(tok, "Date")) ??
                                      DynStr(DynProp(tok, "Started")) ??
                                      DynStr(DynProp(tok, "Start"));

                        // secondes
                        double secs = 0.0;
                        var el = DynProp(tok, "ElapsedSeconds");
                        var du = DynProp(tok, "Duration");
                        var ms = DynProp(tok, "DurationMs");

                        double v1 = 0.0, v2 = 0.0, v3 = 0.0;


                        if (el != null && double.TryParse(el.ToString(), out v1)) secs = v1;
                        else if (du != null && double.TryParse(du.ToString(), out v2)) secs = v2;
                        else if (ms != null && double.TryParse(ms.ToString(), out v3)) secs = v3 / 1000.0;

                        if (string.IsNullOrWhiteSpace(dateStr) || secs <= 0) continue;

                        DateTime startUtcish;
                        if (!DateTime.TryParse(dateStr, out startUtcish)) continue;

                        DateTime start = startUtcish.ToLocalTime();
                        DateTime end = start.AddSeconds(secs).ToLocalTime();

                        if (end <= monthStart || start >= monthEnd) continue;

                        DateTime clipStart = start < monthStart ? monthStart : start;
                        DateTime clipEnd = end > monthEnd ? monthEnd : end;

                        int mins = (int)(clipEnd - clipStart).TotalMinutes;
                        if (mins > 0) total += (ulong)mins;
                    }

                    return total; // premier fichier trouvé suffit
                }
            }
            catch { }

            return 0UL;
        }


        public AnikiHelper(IPlayniteAPI api) : base(api)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

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

            // PlayniteApi.Database.Games.ItemUpdated += (_, __) => RecalcStatsSafe();
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            base.OnGameStopped(args);
            EnsureMonthlySnapshotSafe();
            RecalcStatsSafe();
        }

        // plus de rescanTimer → pas besoin d’override ici

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
            foreach (var g in games.Where(x => (x.Playtime / 60UL) == 0UL).OrderByDescending(x => x.Added).Take(5))
            {
                s.NeverPlayed.Add(new QuickItem { Name = SafeName(g.Name), Value = "" });
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
            // Scan manuel des updates d’extensions
            yield return new MainMenuItem
            {
                MenuSection = "@",
                Description = "Scanner les mises à jour d’extensions",
                Action = _ =>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await ScanAndPublishAsync().ConfigureAwait(false);
                            PlayniteApi.Dialogs.ShowMessage("Scan des mises à jour d’extensions terminé.", "AnikiHelper");
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "[AnikiHelper] Manual scan failed");
                            PlayniteApi.Dialogs.ShowErrorMessage($"Échec du scan des mises à jour : {ex.Message}", "AnikiHelper");
                        }
                    });
                }
            };
        }


        #endregion

        #region Add-ons: scan + notifications

        private async Task ScanAndPublishAsync()
        {
            var updates = await TryGetAddonUpdatesAsync().ConfigureAwait(false);
            UpdateThemeBindings(updates);
            if (!Settings.NotificationsEnabled) return;
            PublishNotifications(updates);
        }

        private void UpdateThemeBindings(List<AddonUpdateItem> updates)
        {
            var s = Settings;
            s.AddonUpdates.Clear();

            foreach (var u in updates)
            {
                s.AddonUpdates.Add(new
                {
                    Id = u.AddonId,
                    Name = u.Name,
                    Current = u.CurrentVersion?.ToString(),
                    New = u.NewVersion?.ToString(),
                    Display = u.ToString()
                });
            }

            s.AddonUpdatesCount = updates.Count;
            s.HasAddonUpdates = updates.Count > 0;
        }

        private void PublishNotifications(List<AddonUpdateItem> updates)
        {
            if (updates.Count == 0) return;

            if (Settings.NotifyIndividually)
            {
                foreach (var u in updates)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"AnikiHelper-AddonUpdate-{u.AddonId}",
                        $"Mise à jour disponible : {u.Name}\n{u.CurrentVersion} → {u.NewVersion}",
                        NotificationType.Info,
                        () => OpenAddonsManager()
                    ));
                }
            }
            else
            {
                var body = string.Join("\n", updates.Select(u => $"• {u.Name}  {u.CurrentVersion} → {u.NewVersion}"));
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "AnikiHelper-AddonUpdates",
                    $"Mises à jour d’extensions disponibles ({updates.Count})\n{body}",
                    NotificationType.Info,
                    () => OpenAddonsManager()
                ));
            }
        }

        private void OpenAddonsManager()
        {
            try { PlayniteApi.Dialogs.ShowMessage("Paramètres → Add-ons pour mettre à jour.", "AnikiHelper"); }
            catch { }
        }

        #endregion

        #region Helpers (installed + online index)

        private class InstalledAddonInfo { public string Name; public Version InstalledVersion; }
        private class OnlineAddonInfo { public string Name; public Version LatestVersion; }

        private Dictionary<string, InstalledAddonInfo> GetInstalledAddonsFromDisk()
        {
            var dict = new Dictionary<string, InstalledAddonInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var roots = new List<string>();
                var baseDir = Path.Combine(PlayniteApi.Paths.ConfigurationPath, "Extensions");
                if (Directory.Exists(baseDir)) roots.Add(baseDir);

                var devDir = Path.Combine(baseDir, "Dev");
                if (Directory.Exists(devDir)) roots.Add(devDir);

                foreach (var root in roots.Distinct())
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        var manifest = Path.Combine(dir, "extension.yaml");
                        if (!File.Exists(manifest)) manifest = Path.Combine(dir, "manifest.yaml");
                        if (!File.Exists(manifest)) continue;

                        string id, name; Version ver;
                        if (TryReadIdVersionFromYaml(manifest, out id, out ver, out name))
                        {
                            if (!dict.ContainsKey(id))
                            {
                                dict[id] = new InstalledAddonInfo { Name = name, InstalledVersion = ver };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] GetInstalledAddonsFromDisk failed");
            }

            return dict;
        }

        private bool TryReadIdVersionFromYaml(string path, out string id, out Version ver, out string name)
        {
            id = null; ver = null; name = null;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var l = line.Trim();
                    if (string.IsNullOrEmpty(l) || l.StartsWith("#")) continue;

                    if (id == null && l.StartsWith("Id:", StringComparison.OrdinalIgnoreCase))
                        id = l.Substring(3).Trim();
                    else if (ver == null && l.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    {
                        var s = l.Substring(8).Trim();
                        if (Version.TryParse(s, out var v)) ver = v;
                    }
                    else if (name == null && l.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                        name = l.Substring(5).Trim();

                    if (id != null && ver != null && name != null) break;
                }
                return !string.IsNullOrEmpty(id);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper] TryReadIdVersionFromYaml failed for {path}");
                return false;
            }
        }

        private async Task<Dictionary<string, OnlineAddonInfo>> FetchAddonsIndexAsync(string urlOverride)
        {
            var dict = new Dictionary<string, OnlineAddonInfo>(StringComparer.OrdinalIgnoreCase);
            var url = string.IsNullOrWhiteSpace(urlOverride) ? "https://playnite.link/addons.json" : urlOverride;

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(15);
                    var json = await http.GetStringAsync(url).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(json)) return dict;

                    dynamic arr = Serialization.FromJson<dynamic>(json);
                    if (arr == null) return dict;

                    foreach (var a in arr)
                    {
                        try
                        {
                            string id = a?.AddonId;
                            string nm = a?.Name;
                            Version latest = null;

                            var packages = a?.Packages;
                            if (packages != null)
                            {
                                foreach (var p in packages)
                                {
                                    string vs = p?.Version;
                                    if (Version.TryParse(vs, out var v))
                                    {
                                        if (latest == null || v > latest) latest = v;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(id) && latest != null)
                            {
                                dict[id] = new OnlineAddonInfo { Name = nm, LatestVersion = latest };
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper] FetchAddonsIndexAsync failed for {url}");
            }

            return dict;
        }

        private Task<List<AddonUpdateItem>> TryGetAddonUpdatesAsync()
        {
            return Task.Run(async () =>
            {
                var result = new List<AddonUpdateItem>();

                try
                {
                    var installed = GetInstalledAddonsFromDisk();
                    logger.Info($"[AnikiHelper] Installed addons found: {installed.Count}");
                    if (installed.Count == 0) return result;

                    var latest = await FetchAddonsIndexAsync(null).ConfigureAwait(false);
                    logger.Info($"[AnikiHelper] Online addons in index: {latest.Count}");
                    if (latest.Count == 0) return result;

                    foreach (var kv in installed)
                    {
                        var id = kv.Key;
                        var inst = kv.Value.InstalledVersion;

                        if (!latest.TryGetValue(id, out var info)) continue;

                        var latestV = info.LatestVersion;
                        if (latestV != null && inst != null && latestV > inst)
                        {
                            result.Add(new AddonUpdateItem
                            {
                                AddonId = id,
                                Name = string.IsNullOrWhiteSpace(info.Name) ? (kv.Value.Name ?? id) : info.Name,
                                CurrentVersion = inst,
                                NewVersion = latestV
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "[AnikiHelper] TryGetAddonUpdatesAsync failed");
                }

                return result
                    .GroupBy(u => string.IsNullOrEmpty(u.AddonId) ? u.Name : u.AddonId)
                    .Select(g => g.OrderByDescending(x => x.NewVersion).First())
                    .OrderBy(a => a.Name)
                    .ToList();
            });
        }

        // === Vues et paramètres (affiche la page Settings dans Playnite) ===
        public override ISettings GetSettings(bool firstRunSettings) => SettingsVM;

        public override UserControl GetSettingsView(bool firstRunSettings) =>
            new AnikiHelperSettingsView { DataContext = SettingsVM };

        #endregion
    }
}
