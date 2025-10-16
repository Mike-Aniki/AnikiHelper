// AnikiHelperSettings.cs
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Timers;


namespace AnikiHelper
{
    // ===== DTOs exposés au thème =====
    public class TopPlayedItem
    {
        public string Name { get; set; }
        public string PlaytimeString { get; set; }
        public string PercentageString { get; set; }
    }

    public class CompletionStatItem
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public string PercentageString { get; set; }
    }

    public class ProviderStatItem
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public string PercentageString { get; set; }
    }

    public class QuickItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class RecentAchievementItem
    {
        public string Game { get; set; }
        public string Title { get; set; }
        public string Desc { get; set; }
        public DateTime Unlocked { get; set; }
        public string UnlockedString => Unlocked.ToString("dd/MM/yyyy HH:mm");
        public string IconPath { get; set; }
    }

    // --- DTOs pour SuccessStory (désérialisation typée) ---
    internal class SsGame
    {
        public string Name { get; set; }
    }

    internal class SsItem
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Desc { get; set; }

        public string DateUnlocked { get; set; }   // ISO 8601
        public long? UnlockTime { get; set; }     // unix (secondes)
        public string UnlockTimestamp { get; set; }
        public string LastUnlock { get; set; }

        public string UrlUnlocked { get; set; }    // image
        public string IconUnlocked { get; set; }
        public string ImageUrl { get; set; }

        public string IsUnlock { get; set; }       // "true"/"false"
        public string Earned { get; set; }
        public string Unlocked { get; set; }

        public double? RarityValue { get; set; }   // souvent 0–100
        public double? Percent { get; set; }       // parfois 0–100
        public double? Percentage { get; set; }    // parfois 0–100
        public string Rarity { get; set; }         // ex: "Ultra Rare", "Common", ou "3.4%"
        public string RarityName { get; set; }     // variante textuelle
    }

    internal class SsFile
    {
        public string Name { get; set; }           // nom du jeu
        public SsGame Game { get; set; }           // parfois { "Game": { "Name": ... } }
        public List<SsItem> Items { get; set; }    // format courant
        public List<SsItem> Achievements { get; set; } // autres versions
    }

    public class RareAchievementItem
    {
        public string Game { get; set; }
        public string Title { get; set; }
        public double Percent { get; set; }               // plus petit = plus rare
        public string PercentString => $"{Percent:0.##}%";
        public DateTime Unlocked { get; set; }
        public string IconPath { get; set; }
    }



    public class DiskUsageItem
    {
        public string Label { get; set; }              // "C:\"
        public string TotalSpaceString { get; set; }   // "931 GB"
        public string FreeSpaceString { get; set; }    // "636 GB"
        public double UsedPercentage { get; set; }     // 0..100
        public int UsedTenthsInt => (int)Math.Round(UsedPercentage / 10.0);
    }

    public class AnikiHelperSettings : ObservableObject, ISettings, INotifyPropertyChanged
    {
        private readonly global::AnikiHelper.AnikiHelper plugin;

        // ===== Updates d’extensions (optionnel) =====
        private bool hasAddonUpdates;
        private int addonUpdatesCount;
        private ObservableCollection<object> addonUpdates = new ObservableCollection<object>();

        // Info snapshot affichée en page de paramètres
        private string snapshotDateString;
        public string SnapshotDateString { get => snapshotDateString; set => SetValue(ref snapshotDateString, value); }

        // ===== Options générales =====
        private bool notificationsEnabled = true;
        private bool notifyIndividually = true;
        private int rescanMinutes = 60;

        // ===== Options stats =====
        private bool includeHidden = false;
        private int topPlayedMax = 10;

        // ===== Affichage temps de jeu =====
        private bool playtimeStoredInHours = false; // Playnite = minutes
        private bool playtimeUseDaysFormat = false;

        // ===== Stockage =====
        private readonly ObservableCollection<DiskUsageItem> diskUsages = new ObservableCollection<DiskUsageItem>();
        public ObservableCollection<DiskUsageItem> DiskUsages => diskUsages;

        // ===== Stats (valeurs) =====
        private int totalCount;
        private int installedCount;
        private int notInstalledCount;
        private int hiddenCount;
        private int favoriteCount;
        private ulong totalPlaytimeMinutes;
        private ulong averagePlaytimeMinutes;

        // ===== JEU LE PLUS JOUE DU MOIS =====
        private string thisMonthTopGameName;
        private string thisMonthTopGamePlaytime;
        private string thisMonthTopGameCoverPath;

        public string ThisMonthTopGameName { get => thisMonthTopGameName; set => SetValue(ref thisMonthTopGameName, value); }
        public string ThisMonthTopGamePlaytime { get => thisMonthTopGamePlaytime; set => SetValue(ref thisMonthTopGamePlaytime, value); }
        public string ThisMonthTopGameCoverPath { get => thisMonthTopGameCoverPath; set => SetValue(ref thisMonthTopGameCoverPath, value); }

        // === Stats "ce mois-ci" ===
        private int thisMonthPlayedCount;
        private ulong thisMonthPlayedTotalMinutes;

        public int ThisMonthPlayedCount { get => thisMonthPlayedCount; set => SetValue(ref thisMonthPlayedCount, value); }

        public ulong ThisMonthPlayedTotalMinutes
        {
            get => thisMonthPlayedTotalMinutes;
            set { SetValue(ref thisMonthPlayedTotalMinutes, value); OnPropertyChanged(nameof(ThisMonthPlayedTotalString)); }
        }

        public string ThisMonthPlayedTotalString =>
            PlaytimeToString(ThisMonthPlayedTotalMinutes, PlaytimeUseDaysFormat);

        // ===== Listes exposées =====
        private ObservableCollection<TopPlayedItem> topPlayed = new ObservableCollection<TopPlayedItem>();
        private ObservableCollection<CompletionStatItem> completionStates = new ObservableCollection<CompletionStatItem>();
        private ObservableCollection<ProviderStatItem> gameProviders = new ObservableCollection<ProviderStatItem>();
        private ObservableCollection<QuickItem> recentPlayed = new ObservableCollection<QuickItem>();
        private ObservableCollection<QuickItem> recentAdded = new ObservableCollection<QuickItem>();
        private ObservableCollection<QuickItem> neverPlayed = new ObservableCollection<QuickItem>();

        public ObservableCollection<TopPlayedItem> TopPlayed { get => topPlayed; set => SetValue(ref topPlayed, value); }
        public ObservableCollection<CompletionStatItem> CompletionStates { get => completionStates; set => SetValue(ref completionStates, value); }
        public ObservableCollection<ProviderStatItem> GameProviders { get => gameProviders; set => SetValue(ref gameProviders, value); }
        public ObservableCollection<QuickItem> RecentPlayed { get => recentPlayed; set => SetValue(ref recentPlayed, value); }
        public ObservableCollection<QuickItem> RecentAdded { get => recentAdded; set => SetValue(ref recentAdded, value); }
        public ObservableCollection<QuickItem> NeverPlayed { get => neverPlayed; set => SetValue(ref neverPlayed, value); }

        // Succès récents (Top 3)
        public ObservableCollection<RecentAchievementItem> RecentAchievements { get; } = new ObservableCollection<RecentAchievementItem>();

        // Succès rares (Top 3)
        public ObservableCollection<RareAchievementItem> RareTop { get; } = new ObservableCollection<RareAchievementItem>();


        // Watcher SuccessStory
        private FileSystemWatcher achievementsWatcher;
        private Timer debounceTimer;

        #region Exposés au thème (updates)
        public bool HasAddonUpdates { get => hasAddonUpdates; set => SetValue(ref hasAddonUpdates, value); }
        public int AddonUpdatesCount { get => addonUpdatesCount; set => SetValue(ref addonUpdatesCount, value); }
        public ObservableCollection<object> AddonUpdates { get => addonUpdates; set => SetValue(ref addonUpdates, value); }
        #endregion

        #region Options
        public bool NotificationsEnabled { get => notificationsEnabled; set => SetValue(ref notificationsEnabled, value); }
        public bool NotifyIndividually { get => notifyIndividually; set => SetValue(ref notifyIndividually, value); }
        public int RescanMinutes { get => rescanMinutes; set => SetValue(ref rescanMinutes, Math.Max(0, value)); }
        public bool IncludeHidden { get => includeHidden; set => SetValue(ref includeHidden, value); }

        public int TopPlayedMax
        {
            get => topPlayedMax;
            set => SetValue(ref topPlayedMax, Math.Max(1, Math.Min(50, value)));
        }

        public bool PlaytimeStoredInHours { get => playtimeStoredInHours; set => SetValue(ref playtimeStoredInHours, value); }

        public bool PlaytimeUseDaysFormat
        {
            get => playtimeUseDaysFormat;
            set
            {
                var changed = playtimeUseDaysFormat != value;
                SetValue(ref playtimeUseDaysFormat, value);
                if (changed)
                {
                    OnPropertyChanged(nameof(TotalPlaytimeString));
                    OnPropertyChanged(nameof(AveragePlaytimeString));
                }
            }
        }
        #endregion

        #region Stats + strings
        public int TotalCount { get => totalCount; set => SetValue(ref totalCount, value); }

        public int InstalledCount
        {
            get => installedCount;
            set { SetValue(ref installedCount, value); OnPropertyChanged(nameof(InstalledPercentString)); }
        }

        public int NotInstalledCount
        {
            get => notInstalledCount;
            set { SetValue(ref notInstalledCount, value); OnPropertyChanged(nameof(NotInstalledPercentString)); }
        }

        public int HiddenCount
        {
            get => hiddenCount;
            set { SetValue(ref hiddenCount, value); OnPropertyChanged(nameof(HiddenPercentString)); }
        }

        public int FavoriteCount
        {
            get => favoriteCount;
            set { SetValue(ref favoriteCount, value); OnPropertyChanged(nameof(FavoritePercentString)); }
        }

        public ulong TotalPlaytimeMinutes
        {
            get => totalPlaytimeMinutes;
            set { SetValue(ref totalPlaytimeMinutes, value); OnPropertyChanged(nameof(TotalPlaytimeString)); }
        }

        public ulong AveragePlaytimeMinutes
        {
            get => averagePlaytimeMinutes;
            set { SetValue(ref averagePlaytimeMinutes, value); OnPropertyChanged(nameof(AveragePlaytimeString)); }
        }

        public string InstalledPercentString => PercentString(InstalledCount, TotalCount);
        public string NotInstalledPercentString => PercentString(NotInstalledCount, TotalCount);
        public string HiddenPercentString => PercentString(HiddenCount, TotalCount);
        public string FavoritePercentString => PercentString(FavoriteCount, TotalCount);

        public string TotalPlaytimeString => PlaytimeToString(TotalPlaytimeMinutes, PlaytimeUseDaysFormat);
        public string AveragePlaytimeString => PlaytimeToString(AveragePlaytimeMinutes, PlaytimeUseDaysFormat);
        #endregion

        public AnikiHelperSettings() { }

        public AnikiHelperSettings(global::AnikiHelper.AnikiHelper plugin)
        {
            this.plugin = plugin;

            var saved = plugin.LoadPluginSettings<AnikiHelperSettings>();
            if (saved != null)
            {
                NotificationsEnabled = saved.NotificationsEnabled;
                NotifyIndividually = saved.NotifyIndividually;
                RescanMinutes = saved.RescanMinutes;

                IncludeHidden = saved.IncludeHidden;
                TopPlayedMax = saved.TopPlayedMax <= 0 ? 10 : saved.TopPlayedMax;

                PlaytimeStoredInHours = saved.PlaytimeStoredInHours;
                PlaytimeUseDaysFormat = saved.PlaytimeUseDaysFormat;
            }

            // Succès récents + watcher
            LoadRecentAchievements(3);
            LoadRareTop(3);
            TryStartAchievementsWatcher();

            // Storage au démarrage
            LoadDiskUsages();
        }

        // ====== SUCCÈS RÉCENTS ======
        public void RefreshRecentAchievements() => LoadRecentAchievements(3);

        // Trouve automatiquement le dossier SuccessStory (portable/normal)
        private string FindSuccessStoryRoot()
        {
            try
            {
                var root = plugin?.PlayniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

                // chemin "classique"
                var classic = Path.Combine(root, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                if (Directory.Exists(classic) &&
                    Directory.EnumerateFiles(classic, "*.json", SearchOption.AllDirectories).Any())
                    return classic;

                // scan large : n'importe quel dossier nommé "SuccessStory" contenant des .json
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (!dir.EndsWith("SuccessStory", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Any())
                        return dir;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private void LoadRecentAchievements(int take = 3)
        {
            RecentAchievements.Clear();

            try
            {
                if (plugin?.PlayniteApi == null)
                    return;

                var ssRoot = FindSuccessStoryRoot();
                if (string.IsNullOrEmpty(ssRoot) || !Directory.Exists(ssRoot))
                    return;

                string[] files;
                try { files = Directory.EnumerateFiles(ssRoot, "*.json", SearchOption.AllDirectories).ToArray(); }
                catch { return; }
                if (files.Length == 0) return;

                DateTime? ParseWhen(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && DateTime.TryParse(it.DateUnlocked, out var d1))
                        return d1;

                    if (it.UnlockTime != null)
                        return DateTimeOffset.FromUnixTimeSeconds(it.UnlockTime.Value).LocalDateTime;

                    if (!string.IsNullOrWhiteSpace(it.UnlockTimestamp) && DateTime.TryParse(it.UnlockTimestamp, out var d2))
                        return d2;

                    if (!string.IsNullOrWhiteSpace(it.LastUnlock) && DateTime.TryParse(it.LastUnlock, out var d3))
                        return d3;

                    return null;
                }

                bool IsUnlocked(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && !it.DateUnlocked.StartsWith("0001-01-01"))
                        return true;

                    if (it.UnlockTime != null) return true;

                    if (string.Equals(it.IsUnlock, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(it.Earned, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(it.Unlocked, "true", StringComparison.OrdinalIgnoreCase)) return true;

                    return false;
                }

                var results = new List<RecentAchievementItem>();

                foreach (var file in files)
                {
                    SsFile rootObj = null;
                    try
                    {
                        var text = File.ReadAllText(file);
                        rootObj = Serialization.FromJson<SsFile>(text);
                    }
                    catch
                    {
                        // si le root est un tableau (cas rares), on tente comme liste directe
                        try
                        {
                            var arrOnly = Serialization.FromJson<List<SsItem>>(File.ReadAllText(file));
                            rootObj = new SsFile { Items = arrOnly };
                        }
                        catch { continue; }
                    }

                    if (rootObj == null) continue;

                    var items = rootObj.Items ?? rootObj.Achievements ?? new List<SsItem>();
                    if (items.Count == 0) continue;

                    var gameName = !string.IsNullOrWhiteSpace(rootObj.Name)
                        ? rootObj.Name
                        : (rootObj.Game?.Name ?? Path.GetFileNameWithoutExtension(file));

                    foreach (var it in items)
                    {
                        if (!IsUnlocked(it)) continue;

                        var when = ParseWhen(it);
                        if (when == null) continue;

                        var title = !string.IsNullOrWhiteSpace(it.Name) ? it.Name
                                   : !string.IsNullOrWhiteSpace(it.Title) ? it.Title
                                   : "(Achievement)";

                        var desc = !string.IsNullOrWhiteSpace(it.Description) ? it.Description
                                   : (it.Desc ?? "");

                        var icon = !string.IsNullOrWhiteSpace(it.UrlUnlocked) ? it.UrlUnlocked
                                   : !string.IsNullOrWhiteSpace(it.IconUnlocked) ? it.IconUnlocked
                                   : (it.ImageUrl ?? "");

                        results.Add(new RecentAchievementItem
                        {
                            Game = gameName,
                            Title = title,
                            Desc = desc,
                            Unlocked = when.Value,
                            IconPath = icon
                        });
                    }
                }

                foreach (var it in results
                    .OrderByDescending(r => r.Unlocked)
                    .Take(take))
                {
                    RecentAchievements.Add(it);
                }
            }
            catch
            {
                // silencieux
            }
        }

        public void RefreshRareAchievements() => LoadRareTop(3);

        private void LoadRareTop(int take = 3)
        {
            RareTop.Clear();

            try
            {
                if (plugin?.PlayniteApi == null)
                    return;

                var ssRoot = FindSuccessStoryRoot();
                if (string.IsNullOrEmpty(ssRoot) || !Directory.Exists(ssRoot))
                    return;

                string[] files;
                try { files = Directory.EnumerateFiles(ssRoot, "*.json", SearchOption.AllDirectories).ToArray(); }
                catch { return; }
                if (files.Length == 0) return;

                // util : lecture date de déblocage (même logique que les succès récents)
                DateTime? ParseWhen(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && DateTime.TryParse(it.DateUnlocked, out var d1))
                        return d1;

                    if (it.UnlockTime != null)
                        return DateTimeOffset.FromUnixTimeSeconds(it.UnlockTime.Value).LocalDateTime;

                    if (!string.IsNullOrWhiteSpace(it.UnlockTimestamp) && DateTime.TryParse(it.UnlockTimestamp, out var d2))
                        return d2;

                    if (!string.IsNullOrWhiteSpace(it.LastUnlock) && DateTime.TryParse(it.LastUnlock, out var d3))
                        return d3;

                    return null;
                }

                bool IsUnlocked(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && !it.DateUnlocked.StartsWith("0001-01-01"))
                        return true;

                    if (it.UnlockTime != null) return true;

                    if (string.Equals(it.IsUnlock, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(it.Earned, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(it.Unlocked, "true", StringComparison.OrdinalIgnoreCase)) return true;

                    return false;
                }

                // Essaie plusieurs champs possibles pour la rareté (%)
                double? TryGetRarityPercent(SsItem it)
                {
                    // 1) numériques directs (0–100)
                    if (it.RarityValue is double rv && rv >= 0 && rv <= 100) return rv;
                    if (it.Percent is double p && p >= 0 && p <= 100) return p;
                    if (it.Percentage is double pc && pc >= 0 && pc <= 100) return pc;

                    // 2) textes "3.4%" / "3.4" dans Rarity / RarityName
                    string[] texts = { it.Rarity, it.RarityName };
                    foreach (var txt in texts)
                    {
                        if (string.IsNullOrWhiteSpace(txt)) continue;
                        var raw = txt.Replace("%", "").Trim();
                        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
                        {
                            if (v >= 0 && v <= 100) return v;        // déjà %
                            if (v > 0 && v <= 1) return v * 100.0;   // ratio → %
                        }
                    }

                    return null;
                }

                var pool = new List<RareAchievementItem>();

                foreach (var file in files)
                {
                    SsFile rootObj = null;
                    try
                    {
                        var text = File.ReadAllText(file);
                        rootObj = Serialization.FromJson<SsFile>(text);
                    }
                    catch
                    {
                        // si le root est un tableau d’items
                        try
                        {
                            var arrOnly = Serialization.FromJson<List<SsItem>>(File.ReadAllText(file));
                            rootObj = new SsFile { Items = arrOnly };
                        }
                        catch { continue; }
                    }

                    if (rootObj == null) continue;

                    var items = rootObj.Items ?? rootObj.Achievements ?? new List<SsItem>();
                    if (items.Count == 0) continue;

                    var gameName = !string.IsNullOrWhiteSpace(rootObj.Name)
                        ? rootObj.Name
                        : (rootObj.Game?.Name ?? Path.GetFileNameWithoutExtension(file));

                    foreach (var it in items)
                    {
                        if (!IsUnlocked(it)) continue;
                        if (ParseWhen(it) == null) continue; // on garde uniquement ceux datés (cohérent avec “récents”)

                        var r = TryGetRarityPercent(it);
                        if (r == null) continue;

                        var title = !string.IsNullOrWhiteSpace(it.Name) ? it.Name
                                   : !string.IsNullOrWhiteSpace(it.Title) ? it.Title
                                   : "(Achievement)";

                        var icon = !string.IsNullOrWhiteSpace(it.UrlUnlocked) ? it.UrlUnlocked
                                   : !string.IsNullOrWhiteSpace(it.IconUnlocked) ? it.IconUnlocked
                                   : (it.ImageUrl ?? "");

                        pool.Add(new RareAchievementItem
                        {
                            Game = gameName,
                            Title = title,
                            Percent = r.Value,
                            IconPath = icon,
                            Unlocked = ParseWhen(it) ?? DateTime.MinValue
                        });
                    }
                }

                var cutoff = DateTime.Now.AddYears(-1);

                foreach (var it in pool
                    .Where(x => x.Unlocked > cutoff)              // uniquement débloqués après 2023
                    .OrderBy(x => x.Percent)                      // du plus rare au moins rare
                    .ThenByDescending(x => x.Unlocked)            // et les plus récents d'abord
                    .Take(take))
                {
                    RareTop.Add(it);
                }


            }
            catch
            {
                // silencieux
            }
        }



        private void TryStartAchievementsWatcher()
        {
            try
            {
                if (plugin?.PlayniteApi == null) return;

                var ssRoot = FindSuccessStoryRoot();
                if (string.IsNullOrEmpty(ssRoot) || !Directory.Exists(ssRoot)) return;

                achievementsWatcher?.Dispose();
                achievementsWatcher = new FileSystemWatcher(ssRoot, "*.json")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
                };

                debounceTimer?.Dispose();
                debounceTimer = new Timer(400) { AutoReset = false };
                debounceTimer.Elapsed += (_, __) =>
                {
                    LoadRecentAchievements(3);
                    LoadRareTop(3);
                };
                FileSystemEventHandler pulse = (_, __) => { debounceTimer.Stop(); debounceTimer.Start(); };
                achievementsWatcher.Created += pulse;
                achievementsWatcher.Changed += pulse;
                achievementsWatcher.Deleted += pulse;
                achievementsWatcher.Renamed += (_, __) => { debounceTimer.Stop(); debounceTimer.Start(); };
            }
            catch { }
        }


        // ===== ISettings =====
        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit() => plugin.SavePluginSettings(this);

        public bool VerifySettings(out List<string> errors)
        {
            errors = null;
            return true;
        }

        // ===== Helpers =====
        private static string PercentString(int part, int total) =>
            total <= 0 ? "0%" : $"{Math.Round(part * 100.0 / total)}%";

        public static string PlaytimeToString(ulong minutes, bool useDays)
        {
            if (useDays)
            {
                if (minutes >= 60 * 24)
                {
                    var days = minutes / (60u * 24u);
                    var hours = (minutes % (60u * 24u)) / 60u;
                    return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
                }
                if (minutes >= 60)
                {
                    var hours = minutes / 60u;
                    var mins = minutes % 60u;
                    return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
                }
                return $"{minutes}m";
            }
            else
            {
                var hours = minutes / 60u;
                var mins = minutes % 60u;
                if (hours == 0) return $"{mins}m";
                return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
            }
        }

        private static string FormatBytesToReadable(ulong bytes)
        {
            const double KB = 1024.0, MB = KB * 1024.0, GB = MB * 1024.0, TB = GB * 1024.0;
            var b = (double)bytes;
            if (b >= TB) return $"{b / TB:0.##} TB";
            if (b >= GB) return $"{b / GB:0.##} GB";
            if (b >= MB) return $"{b / MB:0.##} MB";
            if (b >= KB) return $"{b / KB:0.##} KB";
            return $"{bytes} B";
        }

        public void RefreshDiskUsages() => LoadDiskUsages();

        private void LoadDiskUsages()
        {
            try
            {
                var list = new List<DiskUsageItem>();
                foreach (var di in DriveInfo.GetDrives())
                {
                    if (!di.IsReady) continue;
                    if (di.DriveType != DriveType.Fixed && di.DriveType != DriveType.Removable && di.DriveType != DriveType.Network) continue;

                    ulong total = (ulong)Math.Max(0, di.TotalSize);
                    ulong free = (ulong)Math.Max(0, di.TotalFreeSpace);
                    ulong used = total >= free ? total - free : 0UL;
                    double pct = total > 0 ? (used * 100.0 / total) : 0.0;

                    list.Add(new DiskUsageItem
                    {
                        Label = string.IsNullOrWhiteSpace(di.Name) ? di.RootDirectory.FullName : di.Name,
                        TotalSpaceString = FormatBytesToReadable(total),
                        FreeSpaceString = FormatBytesToReadable(free),
                        UsedPercentage = Math.Round(pct)
                    });
                }

                list = list.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList();

                diskUsages.Clear();
                foreach (var it in list) diskUsages.Add(it);
            }
            catch
            {
                // jamais bloquer l’UI
            }
        }
    }

    public class AnikiHelperSettingsViewModel : ObservableObject, ISettings
    {
        public AnikiHelperSettings Settings { get; set; }
        private readonly global::AnikiHelper.AnikiHelper plugin;

        public AnikiHelperSettingsViewModel(global::AnikiHelper.AnikiHelper plugin)
        {
            this.plugin = plugin;
            Settings = new AnikiHelperSettings(plugin);
        }

        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit() => plugin.SavePluginSettings(Settings);

        public bool VerifySettings(out List<string> errors)
        {
            errors = null;
            return true;
        }

        // Bouton de la page Settings (si tu l’utilises)
        public void ResetMonthlySnapshot()
        {
            try { plugin?.ResetMonthlySnapshot(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ResetMonthlySnapshot failed: " + ex.Message);
            }
        }
    }
}
