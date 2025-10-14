// AnikiHelperSettings.cs
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
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

        // ===== Storage =====
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

        public string ThisMonthTopGameName { get => thisMonthTopGameName; set => SetValue(ref thisMonthTopGameName, value); }
        public string ThisMonthTopGamePlaytime { get => thisMonthTopGamePlaytime; set => SetValue(ref thisMonthTopGamePlaytime, value); }


        // === Stats "ce mois-ci" ===
        private int thisMonthPlayedCount;
        private ulong thisMonthPlayedTotalMinutes;

        public int ThisMonthPlayedCount
        {
            get => thisMonthPlayedCount;
            set => SetValue(ref thisMonthPlayedCount, value);
        }

        public ulong ThisMonthPlayedTotalMinutes
        {
            get => thisMonthPlayedTotalMinutes;
            set
            {
                SetValue(ref thisMonthPlayedTotalMinutes, value);
                OnPropertyChanged(nameof(ThisMonthPlayedTotalString));
            }
        }

        // Chaîne prête pour le XAML (format identique à tes autres durées)
        public string ThisMonthPlayedTotalString =>
            PlaytimeToString(ThisMonthPlayedTotalMinutes, PlaytimeUseDaysFormat);


        // ===== Listes exposées =====
        private ObservableCollection<TopPlayedItem> topPlayed = new ObservableCollection<TopPlayedItem>();
        private ObservableCollection<CompletionStatItem> completionStates = new ObservableCollection<CompletionStatItem>();
        private ObservableCollection<ProviderStatItem> gameProviders = new ObservableCollection<ProviderStatItem>();

        private ObservableCollection<QuickItem> recentPlayed = new ObservableCollection<QuickItem>();
        private ObservableCollection<QuickItem> recentAdded = new ObservableCollection<QuickItem>();
        private ObservableCollection<QuickItem> neverPlayed = new ObservableCollection<QuickItem>();

        // Succès récents (Top 5)
        public ObservableCollection<RecentAchievementItem> RecentAchievements { get; } = new ObservableCollection<RecentAchievementItem>();

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

        #region Stats (valeurs + chaînes prêtes XAML)
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

        #region Collections
        public ObservableCollection<TopPlayedItem> TopPlayed { get => topPlayed; set => SetValue(ref topPlayed, value); }
        public ObservableCollection<CompletionStatItem> CompletionStates { get => completionStates; set => SetValue(ref completionStates, value); }
        public ObservableCollection<ProviderStatItem> GameProviders { get => gameProviders; set => SetValue(ref gameProviders, value); }
        public ObservableCollection<QuickItem> RecentPlayed { get => recentPlayed; set => SetValue(ref recentPlayed, value); }
        public ObservableCollection<QuickItem> RecentAdded { get => recentAdded; set => SetValue(ref recentAdded, value); }
        public ObservableCollection<QuickItem> NeverPlayed { get => neverPlayed; set => SetValue(ref neverPlayed, value); }
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

            // Succès récents
            LoadRecentAchievements(5);
            TryStartAchievementsWatcher();

            // Storage au démarrage
            LoadDiskUsages();
        }

        // ====== SUCCÈS RÉCENTS ======
        public void RefreshRecentAchievements() => LoadRecentAchievements(5);

        private void LoadRecentAchievements(int take = 5)
        {
            RecentAchievements.Clear();

            try
            {
                if (plugin?.PlayniteApi == null)
                    return;

                var extData = plugin.PlayniteApi.Paths.ExtensionsDataPath;
                var ssRoot = Path.Combine(extData, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                if (!Directory.Exists(ssRoot))
                    return;

                string[] files;
                try { files = Directory.GetFiles(ssRoot, "*.json", SearchOption.AllDirectories); }
                catch { return; }
                if (files.Length == 0) return;

                var items = files.SelectMany(file =>
                {
                    try
                    {
                        var j = JToken.Parse(File.ReadAllText(file));
                        var listTok = j.SelectToken("Items") ?? j;
                        var list = (listTok as JArray) ?? (listTok.HasValues ? new JArray(listTok.Children()) : new JArray());

                        var gameName =
                            (string)(j.SelectToken("Name") ??
                                     j.SelectToken("Game")?["Name"] ??
                                     j.SelectToken("GameName"))
                            ?? Path.GetFileNameWithoutExtension(file);

                        return list.OfType<JToken>().Select(a =>
                        {
                            bool unlockedFlag = false;

                            var dateUnlockedStr = a["DateUnlocked"]?.ToString();
                            if (!string.IsNullOrEmpty(dateUnlockedStr) && !dateUnlockedStr.StartsWith("0001-01-01"))
                                unlockedFlag = true;

                            if (!unlockedFlag && a["UnlockTime"] != null)
                                unlockedFlag = true;

                            if (!unlockedFlag) return null;

                            DateTime? dt = null;
                            if (!string.IsNullOrEmpty(dateUnlockedStr) && DateTime.TryParse(dateUnlockedStr, out var parsed))
                                dt = parsed;

                            if (dt == null && long.TryParse(a["UnlockTime"]?.ToString(), out var unix))
                                dt = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

                            if (dt == null) return null;

                            var title = (string)(a["Name"] ?? a["Title"]) ?? "(Achievement)";
                            var desc = (string)(a["Description"] ?? a["Desc"]) ?? "";
                            var icon = (string)(a["IconUnlocked"] ?? a["Icon"] ?? a["ImageUrl"]) ?? "";

                            return new RecentAchievementItem
                            {
                                Game = gameName,
                                Title = title,
                                Desc = desc,
                                Unlocked = dt.Value,
                                IconPath = icon
                            };
                        }).Where(x => x != null);
                    }
                    catch
                    {
                        return Enumerable.Empty<RecentAchievementItem>();
                    }
                })
                .OrderByDescending(x => x.Unlocked)
                .Take(take)
                .ToList();

                foreach (var it in items)
                    RecentAchievements.Add(it);
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
                if (plugin?.PlayniteApi == null)
                    return;

                var extData = plugin.PlayniteApi.Paths.ExtensionsDataPath;
                var ssRoot = Path.Combine(extData, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                if (!Directory.Exists(ssRoot))
                    return;

                achievementsWatcher?.Dispose();
                achievementsWatcher = new FileSystemWatcher(ssRoot, "*.json")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
                };

                debounceTimer?.Dispose();
                debounceTimer = new Timer(400) { AutoReset = false };
                debounceTimer.Elapsed += (_, __) => LoadRecentAchievements(5);

                FileSystemEventHandler onFsEvent = (_, __) => { debounceTimer.Stop(); debounceTimer.Start(); };
                RenamedEventHandler onRenamed = (_, __) => { debounceTimer.Stop(); debounceTimer.Start(); };

                achievementsWatcher.Created += onFsEvent;
                achievementsWatcher.Changed += onFsEvent;
                achievementsWatcher.Deleted += onFsEvent;
                achievementsWatcher.Renamed += onRenamed;
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
    }
}
