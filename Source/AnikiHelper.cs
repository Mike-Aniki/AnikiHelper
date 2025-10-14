using AnikiHelper.Models;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace AnikiHelper
{
    public class AnikiHelper : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        // ✅ Ici ton helper (simple fonction utilitaire)
        private static string Safe(string s) =>
            string.IsNullOrWhiteSpace(s) ? "(Unnamed Game)" : s;

        // VM + Settings (noms alignés)
        public AnikiHelperSettingsViewModel SettingsVM { get; private set; }
        public AnikiHelperSettings Settings => SettingsVM.Settings;

        // Garde ton GUID si le plugin n’est pas publié
        public override Guid Id { get; } = Guid.Parse("96a983a3-3f13-4dce-a474-4052b718bb52");

        private Timer rescanTimer;

        public AnikiHelper(IPlayniteAPI api) : base(api)
        {
            // HTTPS correct
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            SettingsVM = new AnikiHelperSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            // IMPORTANT: ce nom doit matcher ton XAML {PluginSettings Plugin=AnikiHelper, ...}
            AddSettingsSupportSafe("AnikiHelper", "Settings");

            // Recalcule les stats quand les options impactantes changent
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
            // Lancer un premier scan d’updates + planifier le rescan si demandé
            _ = Task.Run(async () =>
            {
                try { await ScanAndPublishAsync().ConfigureAwait(false); }
                catch (Exception ex) { logger.Error(ex, "[AnikiHelper] Initial addon scan failed"); }

                var mins = Settings.RescanMinutes;
                if (mins > 0)
                {
                    rescanTimer = new Timer(async _ =>
                    {
                        try { await ScanAndPublishAsync().ConfigureAwait(false); }
                        catch (Exception ex) { logger.Error(ex, "[AnikiHelper] Rescan timer failed"); }
                    }, null, TimeSpan.FromMinutes(mins), TimeSpan.FromMinutes(mins));
                }
            });

            // Stats initiales
            try { RecalcStats(); }
            catch (Exception ex) { logger.Error(ex, "[AnikiHelper] Initial stats calc failed"); }

            try { Settings.RefreshDiskUsages(); } catch { }

            // Recalc quand la collection de jeux change
            if (PlayniteApi?.Database?.Games is INotifyCollectionChanged notif)
            {
                notif.CollectionChanged += (_, __) => RecalcStatsSafe();
            }

            // Recalc quand la DB s’ouvre (au démarrage / changement de bibliothèque)
            PlayniteApi.Database.DatabaseOpened += (_, __) => RecalcStatsSafe();
        }



        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            rescanTimer?.Dispose();
        }

        #endregion

        private void RecalcStatsSafe()
        {
            try { RecalcStats(); }
            catch (Exception ex) { logger.Error(ex, "[AnikiHelper] RecalcStats failed"); }
        }

        private static string PercentStringLocal(int part, int total)
        {
            return total <= 0 ? "0%" : $"{Math.Round(part * 100.0 / total)}%";
        }

        /// <summary>
        /// Calcule et expose: Totaux, TopPlayed, CompletionStates, GameProviders
        /// </summary>
        private void RecalcStats()
        {
            var s = Settings;

            // Playnite stocke Playtime en minutes → si jamais tu avais de l’heure brute, diviser par 60.
            Func<ulong, ulong> ToMinutes = raw => raw / 60UL;   // Playtime (seconds) -> minutes

            // Liste des jeux (avec option pour exclure les masqués)
            var games = PlayniteApi.Database.Games.ToList();
            if (!s.IncludeHidden)
            {
                games = games.Where(g => g.Hidden != true).ToList();
            }

            // === Totaux
            s.TotalCount = games.Count;
            s.InstalledCount = games.Count(g => g.IsInstalled == true);
            s.NotInstalledCount = games.Count(g => g.IsInstalled != true);
            s.HiddenCount = games.Count(g => g.Hidden == true);   // info de contexte
            s.FavoriteCount = games.Count(g => g.Favorite == true);

            // === Playtime total/moyen (en minutes)
            ulong totalMinutes = (ulong)games.Sum(g => (long)ToMinutes(g.Playtime));
            s.TotalPlaytimeMinutes = totalMinutes;

            var played = games.Where(g => ToMinutes(g.Playtime) > 0UL).ToList();
            s.AveragePlaytimeMinutes = (ulong)(
                played.Count == 0 ? 0 :
                played.Sum(g => (long)ToMinutes(g.Playtime)) / played.Count
            );

            // === TOP PLAYED
            s.TopPlayed.Clear();
            if (totalMinutes > 0UL)
            {
                foreach (var g in played
                    .OrderByDescending(g => g.Playtime)   // déjà en minutes → OK
                    .Take(Math.Max(1, Math.Min(50, s.TopPlayedMax))))
                {
                    var gMin = ToMinutes(g.Playtime);
                    var pct = Math.Round((double)gMin * 100.0 / totalMinutes);

                    s.TopPlayed.Add(new TopPlayedItem
                    {
                        Name = string.IsNullOrWhiteSpace(g.Name) ? "(Unnamed Game)" : g.Name,
                        PlaytimeString = AnikiHelperSettings.PlaytimeToString(gMin, s.PlaytimeUseDaysFormat),
                        PercentageString = $"{pct}%"
                    });
                }
            }

            // ===== CE MOIS-CI =====
            var now = DateTime.Now;
            var monthStart = new DateTime(now.Year, now.Month, 1);

            var monthGames = games
                .Where(g => g.LastActivity != null && g.LastActivity.Value.ToLocalTime() >= monthStart)
                .ToList();

            s.ThisMonthPlayedCount = monthGames.Count;

            // NB : c’est le temps cumulé *total* des jeux joués ce mois-ci (pas “ce mois” strict)
            ulong monthTotalMinutes = (ulong)monthGames.Sum(g => (long)ToMinutes(g.Playtime));
            s.ThisMonthPlayedTotalMinutes = monthTotalMinutes;

            // ===== Jeu le plus joué ce mois-ci =====
            var topMonth = monthGames
                .OrderByDescending(g => g.Playtime)
                .FirstOrDefault();

            if (topMonth != null)
            {
                s.ThisMonthTopGameName = Safe(topMonth.Name);
                s.ThisMonthTopGamePlaytime = AnikiHelperSettings.PlaytimeToString(
                    ToMinutes(topMonth.Playtime),
                    s.PlaytimeUseDaysFormat
                );
            }
            else
            {
                s.ThisMonthTopGameName = "—";
                s.ThisMonthTopGamePlaytime = "";
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

            // ---------- Listes "rapides" ----------
            // helper local (non-static) et pas de nom 's' en paramètre
            Func<string, string> SafeName = name =>
                string.IsNullOrWhiteSpace(name) ? "(Unnamed Game)" : name;

            // Joués récemment (LastActivity)
            s.RecentPlayed.Clear();
            foreach (var g in games
                .Where(x => x.LastActivity != null)
                .OrderByDescending(x => x.LastActivity)
                .Take(5))   // <- was 10
            {
                var dt = g.LastActivity?.ToLocalTime().ToString("dd/MM/yyyy");
                s.RecentPlayed.Add(new QuickItem { Name = SafeName(g.Name), Value = dt });
            }

            // Ajoutés récemment (Added)
            s.RecentAdded.Clear();
            foreach (var g in games
                .Where(x => x.Added != null)
                .OrderByDescending(x => x.Added)
                .Take(5))   // <- was 10
            {
                var dt = g.Added?.ToLocalTime().ToString("dd/MM/yyyy");
                s.RecentAdded.Add(new QuickItem { Name = SafeName(g.Name), Value = dt });
            }

            // Jamais joués (Playtime == 0)
            s.NeverPlayed.Clear();
            foreach (var g in games
                .Where(x => ToMinutes(x.Playtime) == 0UL)
                .OrderByDescending(x => x.Added)
                .Take(5))   // <- was 10
            {
                s.NeverPlayed.Add(new QuickItem { Name = SafeName(g.Name), Value = "" });
            }


            // === GAME PROVIDERS (Source.Name)
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
            // Scan manuel
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

            // Debug: lister les add-ons installés (lus sur disque)
            yield return new MainMenuItem
            {
                MenuSection = "@",
                Description = "[Debug] Lister add-ons installés",
                Action = _ =>
                {
                    try
                    {
                        var inst = GetInstalledAddonsFromDisk();
                        var lines = inst.Select(kv => $"{kv.Key}  {kv.Value.InstalledVersion}  ({kv.Value.Name})").ToList();
                        if (lines.Count == 0) lines.Add("— aucun manifest trouvé —");
                        PlayniteApi.Dialogs.ShowMessage(string.Join("\n", lines.Take(80)), "AnikiHelper • Installés");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "[AnikiHelper] Debug installed addons failed");
                    }
                }
            };

            // Debug: compter le nombre d’items dans l’index online
            yield return new MainMenuItem
            {
                MenuSection = "@",
                Description = "[Debug] Compter add-ons en ligne",
                Action = _ =>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var online = await FetchAddonsIndexAsync(null);
                            PlayniteApi.Dialogs.ShowMessage($"Index: {online.Count} add-ons", "AnikiHelper • Online");
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "[AnikiHelper] Debug online addons failed");
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

            if (!Settings.NotificationsEnabled)
                return;

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
            if (updates.Count == 0)
                return;

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
            try
            {
                PlayniteApi.Dialogs.ShowMessage("Paramètres → Add-ons pour mettre à jour.", "AnikiHelper");
            }
            catch { }
        }

        #endregion

        #region Helpers (installed + online index)

        private class InstalledAddonInfo
        {
            public string Name;
            public Version InstalledVersion;
        }

        private class OnlineAddonInfo
        {
            public string Name;
            public Version LatestVersion;
        }

        /// <summary>
        /// %AppData%\Playnite\Extensions\* et \Extensions\Dev\*
        /// </summary>
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
                        if (!File.Exists(manifest))
                            manifest = Path.Combine(dir, "manifest.yaml");
                        if (!File.Exists(manifest))
                            continue;

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

        /// <summary>Parse minimaliste YAML: Id, Version, Name.</summary>
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

        /// <summary>Récupère l’index des add-ons (addons.json) et retourne Id -> (Name, LatestVersion).</summary>
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
                        catch { /* ignore entrée mal formée */ }
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

        #endregion
    }
}
