using AnikiHelper.Services;
using AnikiHelper.Services.Achievements;
using AnikiHelper.Services.AnikiThemeSettings;
using AnikiHelper.Services.MediaGallery;
using AnikiHelper.Services.SplashScreen;
using AnikiHelper.Services.SteamFriends;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Reflection;
using AnikiHelper.Services.DuplicateHider;
using System.Windows.Media;


namespace AnikiHelper
{
    // DTOs exposés au thème
    // DTOs exposed to the theme

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

    // Overlay Apps / Software Tools item exposed to the theme.
    public class AnikiOverlayAppItem
    {
        public string Name { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public string BackgroundImagePath { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;
        public bool IsScript { get; set; }

        [DontSerialize]
        public AppSoftware SourceApp { get; set; }

        public string TypeText => IsScript ? "SCRIPT" : "APP";

        public string Details
        {
            get
            {
                if (IsScript)
                {
                    return "PowerShell script";
                }

                if (!string.IsNullOrWhiteSpace(Path))
                {
                    return string.IsNullOrWhiteSpace(Arguments) ? Path : $"{Path} {Arguments}";
                }

                return string.Empty;
            }
        }
    }

    public class AnikiOverlayAchievementItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public bool Unlocked { get; set; }
        public bool Hidden { get; set; }
        public string Rarity { get; set; } = string.Empty;
        public double? Percent { get; set; }
        public DateTime? UnlockDate { get; set; }
        public int? Points { get; set; }
        public int? ProgressNum { get; set; }
        public int? ProgressDenom { get; set; }
        public string TrophyType { get; set; } = string.Empty;
        public bool IsCapstone { get; set; }

        public string StatusText => Unlocked ? "UNLOCKED" : "LOCKED";

        public string RarityText
        {
            get
            {
                if (Percent.HasValue)
                {
                    return Percent.Value.ToString("0.##") + "%";
                }

                return string.IsNullOrWhiteSpace(Rarity) ? string.Empty : Rarity.ToUpperInvariant();
            }
        }

        public string RaritySentence
        {
            get
            {
                if (Percent.HasValue)
                {
                    return Percent.Value.ToString("0.##") + "% des joueurs ont débloqué ce succès.";
                }

                return string.IsNullOrWhiteSpace(Rarity) ? string.Empty : Rarity;
            }
        }

        public string UnlockDateRightText
        {
            get
            {
                if (!Unlocked)
                {
                    return string.Empty;
                }

                if (!UnlockDate.HasValue)
                {
                    return "Unlocked";
                }

                return UnlockDate.Value.ToString("dd/MM/yyyy");
            }
        }

        public string UnlockDateText
        {
            get
            {
                if (!Unlocked)
                {
                    return "Locked";
                }

                if (!UnlockDate.HasValue)
                {
                    return "Unlocked";
                }

                return "Unlocked " + UnlockDate.Value.ToString("dd/MM/yyyy");
            }
        }

        public bool HasProgress => ProgressNum.HasValue && ProgressDenom.HasValue && ProgressDenom.Value > 0;

        public string ProgressText
        {
            get
            {
                if (!HasProgress)
                {
                    return string.Empty;
                }

                return ProgressNum.Value + " / " + ProgressDenom.Value;
            }
        }

        public string MetaText
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(RarityText))
                {
                    parts.Add(RarityText);
                }

                if (Points.HasValue && Points.Value > 0)
                {
                    parts.Add(Points.Value + " pts");
                }

                if (!string.IsNullOrWhiteSpace(TrophyType))
                {
                    parts.Add(TrophyType.ToUpperInvariant());
                }

                if (IsCapstone)
                {
                    parts.Add("CAPSTONE");
                }

                return string.Join("  •  ", parts);
            }
        }
    }

    public class SplashScreenPriorityOption
    {
        public SplashScreenPriorityTarget Value { get; set; }
        public string Label { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Label) ? Value.ToString() : Label;
        }
    }

    public class HubLibraryRecommendedGameItem
    {
        public Guid GameId { get; set; } = Guid.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoverPath { get; set; } = string.Empty;
        public string BackgroundPath { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string ReasonKey { get; set; } = string.Empty;
        public string BannerText { get; set; } = string.Empty;
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

    // DTOs SuccessStory

    internal class SsGame { public string Name { get; set; } }

    internal class SsItem
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Desc { get; set; }
        public string DateUnlocked { get; set; }
        public long? UnlockTime { get; set; }
        public string UnlockTimestamp { get; set; }
        public string LastUnlock { get; set; }
        public string UrlUnlocked { get; set; }
        public string IconUnlocked { get; set; }
        public string ImageUrl { get; set; }
        public string IsUnlock { get; set; }
        public string Earned { get; set; }
        public string Unlocked { get; set; }
        public double? RarityValue { get; set; }
        public double? Percent { get; set; }
        public double? Percentage { get; set; }
        public string Rarity { get; set; }
        public string RarityName { get; set; }
    }

    internal class SsFile
    {
        public string Name { get; set; }
        public SsGame Game { get; set; }
        public List<SsItem> Items { get; set; }
        public List<SsItem> Achievements { get; set; }
    }

    public class RareAchievementItem
    {
        public string Game { get; set; }
        public string Title { get; set; }
        public double Percent { get; set; }
        public string PercentString => $"{Percent:0.##}%";
        public DateTime Unlocked { get; set; }
        public string IconPath { get; set; }
    }

    public class DiskUsageItem
    {
        public string Label { get; set; }
        public string TotalSpaceString { get; set; }
        public string FreeSpaceString { get; set; }
        public double UsedPercentage { get; set; }
        public int UsedTenthsInt => (int)Math.Round(UsedPercentage / 10.0);
    }


    public class SteamRecentUpdateItem : ObservableObject
    {
        private string steamAppId;
        public string SteamAppId
        {
            get => steamAppId;
            set => SetValue(ref steamAppId, value);
        }

        private string gameName;
        public string GameName
        {
            get => gameName;
            set => SetValue(ref gameName, value);
        }

        private string title;
        public string Title
        {
            get => title;
            set => SetValue(ref title, value);
        }

        private string dateString;
        public string DateString
        {
            get => dateString;
            set => SetValue(ref dateString, value);
        }

        private string coverPath;
        public string CoverPath
        {
            get => coverPath;
            set => SetValue(ref coverPath, value);
        }

        private string backgroundPath;
        public string BackgroundPath
        {
            get => backgroundPath;
            set => SetValue(ref backgroundPath, value);
        }

        private string iconPath;
        public string IconPath
        {
            get => iconPath;
            set => SetValue(ref iconPath, value);
        }

        // Badge NEW
        private bool isRecent;
        public bool IsRecent
        {
            get => isRecent;
            set => SetValue(ref isRecent, value);
        }

        // Complete HTML content of the patch note 
        private string html;
        public string Html
        {
            get => html;
            set => SetValue(ref html, value);
        }
    }

    public class SteamGameNewsItem : ObservableObject
    {
        private string title;
        public string Title
        {
            get => title;
            set => SetValue(ref title, value);
        }

        private string dateString;
        public string DateString
        {
            get => dateString;
            set => SetValue(ref dateString, value);
        }

        private string html;
        public string Html
        {
            get => html;
            set => SetValue(ref html, value);
        }

        private string url;
        public string Url
        {
            get => url;
            set => SetValue(ref url, value);
        }

        private string imageUrl;
        public string ImageUrl
        {
            get => imageUrl;
            set => SetValue(ref imageUrl, value);
        }

        private string localImagePath;
        public string LocalImagePath
        {
            get => localImagePath;
            set => SetValue(ref localImagePath, value);
        }
    }

    public class AnikiNotificationItem : ObservableObject
    {
        private string title;
        public string Title
        {
            get => title;
            set => SetValue(ref title, value);
        }

        private string message;
        public string Message
        {
            get => message;
            set => SetValue(ref message, value);
        }

        private string type;
        public string Type
        {
            get => type;
            set => SetValue(ref type, value);
        }

        private string dateString;
        public string DateString
        {
            get => dateString;
            set => SetValue(ref dateString, value);
        }

        private string imagePath;
        public string ImagePath
        {
            get => imagePath;
            set => SetValue(ref imagePath, value);
        }
    }

    public class WhatsNewSlideItem : ObservableObject
    {
        private string title;
        public string Title
        {
            get => title;
            set => SetValue(ref title, value);
        }

        private string text;
        public string Text
        {
            get => text;
            set => SetValue(ref text, value);
        }

        private string imagePath;
        public string ImagePath
        {
            get => imagePath;
            set => SetValue(ref imagePath, value);
        }
    }

    public static class AnikiVersionComparer
    {
        public static int CompareVersions(string version1, string version2)
        {
            if (string.IsNullOrWhiteSpace(version1) || string.IsNullOrWhiteSpace(version2))
            {
                return -1;
            }

            var v1 = Array.ConvertAll(version1.Split('.'), int.Parse);
            var v2 = Array.ConvertAll(version2.Split('.'), int.Parse);

            for (int i = 0; i < Math.Max(v1.Length, v2.Length); i++)
            {
                int part1 = i < v1.Length ? v1[i] : 0;
                int part2 = i < v2.Length ? v2[i] : 0;

                if (part1 > part2)
                {
                    return 1;
                }

                if (part1 < part2)
                {
                    return -1;
                }
            }

            return 0;
        }

        public static bool MinimalVersion(string minVersion, string actualVersion)
        {
            return CompareVersions(minVersion, actualVersion) <= 0;
        }
    }

    public class AnikiMinimalVersion : Dictionary<string, object>
    {
        private static string GetPluginVersion()
        {
            try
            {
                string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                string pluginManifestFile = Path.Combine(pluginFolder, "extension.yaml");

                if (!File.Exists(pluginManifestFile))
                {
                    pluginManifestFile = Path.Combine(pluginFolder, "Extension.yaml");
                }

                if (!File.Exists(pluginManifestFile))
                {
                    return "0.0.0";
                }

                var info = Serialization.FromYamlFile<Dictionary<string, object>>(pluginManifestFile);

                if (info != null && info.ContainsKey("Version") && info["Version"] != null)
                {
                    return info["Version"].ToString();
                }
            }
            catch
            {
            }

            return "0.0.0";
        }

        public static string PluginVersion = GetPluginVersion();

        public new object this[string version]
        {
            get
            {
                try
                {
                    return AnikiVersionComparer.MinimalVersion(version, PluginVersion);
                }
                catch
                {
                    return false;
                }
            }
            set
            {
            }
        }
    }


    public class AnikiOverlayNeverSuspendGameItem : ObservableObject
    {
        [DontSerialize]
        private AnikiHelperSettings owner;

        public Guid GameId { get; set; }
        public string Name { get; set; }

        private bool isNeverSuspendEnabled = true;
        public bool IsNeverSuspendEnabled
        {
            get => isNeverSuspendEnabled;
            set
            {
                if (isNeverSuspendEnabled == value)
                {
                    return;
                }

                SetValue(ref isNeverSuspendEnabled, value);

                if (!value)
                {
                    owner?.SetInGameOverlayNeverSuspend(GameId, false);
                }
            }
        }

        public AnikiOverlayNeverSuspendGameItem()
        {
        }

        public AnikiOverlayNeverSuspendGameItem(AnikiHelperSettings owner, Guid gameId, string name)
        {
            this.owner = owner;
            GameId = gameId;
            Name = string.IsNullOrWhiteSpace(name) ? gameId.ToString() : name;
            isNeverSuspendEnabled = true;
        }
    }

    public partial class AnikiHelperSettings : ObservableObject, ISettings, System.ComponentModel.INotifyPropertyChanged
    {
        private readonly global::AnikiHelper.AnikiHelper plugin;

        [DontSerialize]
        private ScreenshotsVisualizerReader screenshotsVisualizerReader;

        [DontSerialize]
        private ScreenshotUtilitiesReader screenshotUtilitiesReader;

        [DontSerialize]
        private AnikiMediaThumbnailService mediaThumbnailService;

        [DontSerialize]
        private ScreenshotMediaCacheService screenshotMediaCacheService;

        [DontSerialize]
        private readonly object overlayLastCapturesRefreshLock = new object();

        [DontSerialize]
        private bool overlayLastCapturesRefreshRunning;

        [DontSerialize]
        private Guid overlayLastCapturesGameId = Guid.Empty;

        [DontSerialize]
        private string overlayLastCapturesGameName = string.Empty;

        private AchievementMemoriesCacheService achievementMemoriesCacheService;
        private RarestAchievementCacheService rarestAchievementCacheService;
        private PlayniteAchievementsReader playniteAchievementsReader;

        [DontSerialize]
        private readonly object achievementMemoriesRefreshLock = new object();

        [DontSerialize]
        private bool achievementMemoriesRefreshRunning;

        [DontSerialize]
        private ILogger logger;

        [DontSerialize]
        public RelayCommand RefreshSuccessStoryCommand { get; }

        [DontSerialize]
        public RelayCommand ClearInGameOverlayNeverSuspendGamesCommand { get; }

        [DontSerialize]
        public RelayCommand<SteamStoreItem> OpenSteamStoreDetailsCommand { get; }
        public RelayCommand<object> OpenGameDetailsCommand { get; }
        public RelayCommand<object> ToggleWelcomeHubCommand { get; }
        public RelayCommand<object> CloseWelcomeHubCommand { get; }
        public RelayCommand<object> InitializeWelcomeHubCommand { get; }
        public RelayCommand HubNextPageCommand { get; }
        public RelayCommand HubPreviousPageCommand { get; }
        public RelayCommand<object> HubSetPageCommand { get; }

        [DontSerialize]
        public RelayCommand OpenSteamStoreHeroDetailsCommand { get; }

        [DontSerialize]
        public RelayCommand<object> SetSteamStoreSectionCommand { get; }

        [DontSerialize]
        private bool gameClosing;

        [DontSerialize]
        public bool GameClosing
        {
            get => gameClosing;
            set => SetValue(ref gameClosing, value);
        }

        [DontSerialize]
        private string selectedGameInstallSizeNoDecimal = string.Empty;

        [DontSerialize]
        public string SelectedGameInstallSizeNoDecimal
        {
            get => selectedGameInstallSizeNoDecimal;
            set => SetValue(ref selectedGameInstallSizeNoDecimal, value);
        }

        public void UpdateSelectedGameInstallSizeNoDecimal(Playnite.SDK.Models.Game game)
        {
            try
            {
                if (game == null || game.InstallSize == null || game.InstallSize == 0)
                {
                    SelectedGameInstallSizeNoDecimal = string.Empty;
                    return;
                }

                double size = game.InstallSize.Value;
                string unit = "B";

                if (size >= 1024)
                {
                    size /= 1024;
                    unit = "KB";
                }

                if (size >= 1024)
                {
                    size /= 1024;
                    unit = "MB";
                }

                if (size >= 1024)
                {
                    size /= 1024;
                    unit = "GB";
                }

                if (size >= 1024)
                {
                    size /= 1024;
                    unit = "TB";
                }

                SelectedGameInstallSizeNoDecimal = $"{size:0} {unit}";
            }
            catch
            {
                SelectedGameInstallSizeNoDecimal = string.Empty;
            }
        }

        [DontSerialize]
        private string closingGameName = string.Empty;

        [DontSerialize]
        public string ClosingGameName
        {
            get => closingGameName;
            set => SetValue(ref closingGameName, value);
        }

        [DontSerialize]
        public RelayCommand RefreshMediaGalleryCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshCurrentGameMediaCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshOverlayLastCapturesCommand { get; }

        [DontSerialize]
        public RelayCommand OpenScreenshotsWindowCommand { get; }

        [DontSerialize]
        public RelayCommand OpenMediaGalleryGamesWindowCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshMediaGalleryLibraryCommand { get; }

        [DontSerialize]
        public ObservableCollection<AnikiMediaItem> CurrentGameMediaItems { get; set; }
            = new ObservableCollection<AnikiMediaItem>();

        [DontSerialize]
        public ObservableCollection<AnikiMediaItem> VisibleCurrentGameMediaItems { get; set; }
            = new ObservableCollection<AnikiMediaItem>();

        [DontSerialize]
        public ObservableCollection<AnikiMediaItem> HubLatestMediaItems { get; set; }
            = new ObservableCollection<AnikiMediaItem>();

        [DontSerialize]
        public bool HasHubLatestMedia
        {
            get => HubLatestMediaItems != null && HubLatestMediaItems.Count > 0;
        }

        [DontSerialize]
        public ObservableCollection<AnikiOverlayAppItem> OverlayAppItems { get; set; }
            = new ObservableCollection<AnikiOverlayAppItem>();

        [DontSerialize]
        public bool HasOverlayApps
        {
            get => OverlayAppItems != null && OverlayAppItems.Count > 0;
        }

        [DontSerialize]
        public ObservableCollection<string> SoftwareToolNamesForSelection { get; set; }
            = new ObservableCollection<string>();

        [DontSerialize]
        public ObservableCollection<AnikiOverlayAppItem> HubAppItems { get; set; }
            = new ObservableCollection<AnikiOverlayAppItem>();

        [DontSerialize]
        public bool HasHubApps
        {
            get => HubAppItems != null && HubAppItems.Count > 0;
        }

        [DontSerialize]
        public bool ShowHubAppsPage
        {
            get => HubAppsEnabled && (HasHubApps || HasSelectedHubAppSlot);
        }

        [DontSerialize]
        private bool isHubAppsSoftwareToolsLoading;

        [DontSerialize]
        private bool HasSelectedHubAppSlot
        {
            get
            {
                return !string.IsNullOrWhiteSpace(HubAppSlot1ToolName)
                    || !string.IsNullOrWhiteSpace(HubAppSlot2ToolName)
                    || !string.IsNullOrWhiteSpace(HubAppSlot3ToolName)
                    || !string.IsNullOrWhiteSpace(HubAppSlot4ToolName);
            }
        }

        private void EnsureHubAppsSoftwareToolsLoaded()
        {
            if (!HubAppsEnabled || !HasSelectedHubAppSlot || isHubAppsSoftwareToolsLoading)
            {
                return;
            }

            var hasResolvedHubApps = HubAppItems != null && HubAppItems.Any(x =>
                x != null && (x.SourceApp != null || !string.IsNullOrWhiteSpace(x.Path)));

            if (HasHubApps && hasResolvedHubApps)
            {
                return;
            }

            try
            {
                isHubAppsSoftwareToolsLoading = true;
                LoadOverlayApps();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to lazy-load Software Tools for Hub Apps.");
            }
            finally
            {
                isHubAppsSoftwareToolsLoading = false;
            }
        }

        [DontSerialize]
        private string hubAppsEmptyText = "Select apps in Aniki Helper settings first.";

        [DontSerialize]
        public string HubAppsEmptyText
        {
            get => hubAppsEmptyText;
            set => SetValue(ref hubAppsEmptyText, value ?? string.Empty);
        }

        [DontSerialize]
        private string overlayAppsEmptyText = "No apps configured. Add Software Tools in Playnite first.";

        [DontSerialize]
        public string OverlayAppsEmptyText
        {
            get => overlayAppsEmptyText;
            set => SetValue(ref overlayAppsEmptyText, value ?? string.Empty);
        }

        [DontSerialize]
        public ObservableCollection<AnikiMediaItem> OverlayLastCaptureItems { get; set; }
            = new ObservableCollection<AnikiMediaItem>();

        [DontSerialize]
        public bool HasOverlayLastCaptures
        {
            get => OverlayLastCaptureItems != null && OverlayLastCaptureItems.Count > 0;
        }

        [DontSerialize]
        private string overlayLastCapturesTitle = "Last Captures";

        [DontSerialize]
        public string OverlayLastCapturesTitle
        {
            get => overlayLastCapturesTitle;
            set => SetValue(ref overlayLastCapturesTitle, value ?? string.Empty);
        }

        [DontSerialize]
        private string overlayLastCapturesSubtitle = string.Empty;

        [DontSerialize]
        public string OverlayLastCapturesSubtitle
        {
            get => overlayLastCapturesSubtitle;
            set => SetValue(ref overlayLastCapturesSubtitle, value ?? string.Empty);
        }

        [DontSerialize]
        private string overlayLastCapturesEmptyText = "No captures found.";

        [DontSerialize]
        public string OverlayLastCapturesEmptyText
        {
            get => overlayLastCapturesEmptyText;
            set => SetValue(ref overlayLastCapturesEmptyText, value ?? string.Empty);
        }

        [DontSerialize]
        private bool isRefreshingOverlayLastCaptures;

        [DontSerialize]
        public bool IsRefreshingOverlayLastCaptures
        {
            get => isRefreshingOverlayLastCaptures;
            private set
            {
                SetValue(ref isRefreshingOverlayLastCaptures, value);
                OnPropertyChanged(nameof(OverlayLastCapturesRefreshButtonText));
            }
        }

        [DontSerialize]
        public string OverlayLastCapturesRefreshButtonText
        {
            get
            {
                return IsRefreshingOverlayLastCaptures
                    ? Loc("LOCAnikiOverlayRefreshingCaptures", "Refreshing...")
                    : Loc("LOCAnikiOverlayRefreshCaptures", "Refresh captures");
            }
        }

        [DontSerialize]
        public bool CanRefreshOverlayLastCaptures
        {
            get
            {
                if (overlayLastCapturesGameId == Guid.Empty)
                {
                    return false;
                }

                if (screenshotsVisualizerReader == null)
                {
                    screenshotsVisualizerReader = new ScreenshotsVisualizerReader(
                        plugin.PlayniteApi,
                        logger
                    );
                }

                // Depending on the Playnite/plugin load state, the plugin instance may not
                // always be exposed through Addons.Plugins. The data folder and an existing
                // game JSON are valid fallbacks for this overlay integration.
                return screenshotsVisualizerReader.IsPluginInstalled()
                    || screenshotsVisualizerReader.IsAvailable()
                    || screenshotsVisualizerReader.HasMediaForGame(overlayLastCapturesGameId);
            }
        }

        [DontSerialize]
        public Visibility OverlayLastCapturesRefreshButtonVisibility
        {
            get => CanRefreshOverlayLastCaptures
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        [DontSerialize]
        public ObservableCollection<AnikiOverlayAchievementItem> OverlayAchievementItems { get; set; }
            = new ObservableCollection<AnikiOverlayAchievementItem>();

        [DontSerialize]
        public bool HasOverlayAchievements
        {
            get => OverlayAchievementItems != null && OverlayAchievementItems.Count > 0;
        }

        [DontSerialize]
        private string overlayAchievementsTitle = "Achievements";

        [DontSerialize]
        public string OverlayAchievementsTitle
        {
            get => overlayAchievementsTitle;
            set => SetValue(ref overlayAchievementsTitle, value ?? string.Empty);
        }

        [DontSerialize]
        private string overlayAchievementsSubtitle = string.Empty;

        [DontSerialize]
        public string OverlayAchievementsSubtitle
        {
            get => overlayAchievementsSubtitle;
            set => SetValue(ref overlayAchievementsSubtitle, value ?? string.Empty);
        }

        [DontSerialize]
        private string overlayAchievementsEmptyText = "No achievements found.";

        [DontSerialize]
        public string OverlayAchievementsEmptyText
        {
            get => overlayAchievementsEmptyText;
            set => SetValue(ref overlayAchievementsEmptyText, value ?? string.Empty);
        }

        [DontSerialize]
        private string overlayAchievementsProgressText = string.Empty;

        [DontSerialize]
        public string OverlayAchievementsProgressText
        {
            get => overlayAchievementsProgressText;
            set => SetValue(ref overlayAchievementsProgressText, value ?? string.Empty);
        }

        [DontSerialize]
        private int overlayAchievementsUnlockedCount;

        [DontSerialize]
        public int OverlayAchievementsUnlockedCount
        {
            get => overlayAchievementsUnlockedCount;
            set => SetValue(ref overlayAchievementsUnlockedCount, value);
        }

        [DontSerialize]
        private int overlayAchievementsTotalCount;

        [DontSerialize]
        public int OverlayAchievementsTotalCount
        {
            get => overlayAchievementsTotalCount;
            set => SetValue(ref overlayAchievementsTotalCount, value);
        }

        [DontSerialize]
        private string overlayAchievementsSortMode = "LastUnlocked";

        [DontSerialize]
        public string OverlayAchievementsSortMode
        {
            get => overlayAchievementsSortMode;
            set
            {
                var normalized = string.Equals(value, "LockedFirst", StringComparison.OrdinalIgnoreCase)
                    ? "LockedFirst"
                    : "LastUnlocked";

                if (string.Equals(overlayAchievementsSortMode, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetValue(ref overlayAchievementsSortMode, normalized);
                ApplyOverlayAchievementsSort();
                NotifyOverlayAchievementsSortChanged();
            }
        }

        [DontSerialize]
        public string OverlayAchievementsSortButtonText
        {
            get
            {
                return string.Equals(overlayAchievementsSortMode, "LockedFirst", StringComparison.OrdinalIgnoreCase)
                    ? "Locked first"
                    : "Last unlocked";
            }
        }

        [DontSerialize]
        public string OverlayAchievementsSortDescription
        {
            get
            {
                return string.Equals(overlayAchievementsSortMode, "LockedFirst", StringComparison.OrdinalIgnoreCase)
                    ? "Locked achievements are shown first."
                    : "Newest unlocked achievements are shown first.";
            }
        }

        [DontSerialize]
        public ObservableCollection<AnikiMediaItem> HubMemoryItems { get; set; }
            = new ObservableCollection<AnikiMediaItem>();

        public ObservableCollection<AnikiAchievementMemoryItem> HubAchievementMemoryItems { get; } =
            new ObservableCollection<AnikiAchievementMemoryItem>();

        private AnikiAchievementMemoryItem rarestPlayniteAchievementAllTime;

        public AnikiAchievementMemoryItem RarestPlayniteAchievementAllTime
        {
            get => rarestPlayniteAchievementAllTime;
            set => SetValue(ref rarestPlayniteAchievementAllTime, value);
        }

        public bool HasRarestPlayniteAchievementAllTime
        {
            get => RarestPlayniteAchievementAllTime != null;
        }

        public bool HasHubAchievementMemory
        {
            get { return HubAchievementMemoryItems.Count > 0; }
        }

        private string hubAchievementMemoryPeriod = string.Empty;

        [DontSerialize]
        public string HubAchievementMemoryPeriod
        {
            get => hubAchievementMemoryPeriod;
            set => SetValue(ref hubAchievementMemoryPeriod, value);
        }

        [DontSerialize]
        public bool HasHubMemory
        {
            get => HubMemoryItems != null && HubMemoryItems.Count > 0;
        }

        private string hubMemorySubtitle = string.Empty;

        [DontSerialize]
        public string HubMemorySubtitle
        {
            get => hubMemorySubtitle;
            set => SetValue(ref hubMemorySubtitle, value);
        }

        [DontSerialize]
        public ObservableCollection<AnikiMediaGameItem> MediaGalleryGames { get; set; }
            = new ObservableCollection<AnikiMediaGameItem>();

        private string mediaGalleryGamesSortMode = "LatestCaptureDesc";
        public string MediaGalleryGamesSortMode
        {
            get => mediaGalleryGamesSortMode;
            set
            {
                SetValue(ref mediaGalleryGamesSortMode, value);
                ApplyMediaGalleryGamesSort();
            }
        }

        [DontSerialize]
        public RelayCommand<AnikiMediaGameItem> OpenScreenshotsForMediaGameCommand { get; }

        [DontSerialize]
        public RelayCommand<AnikiOverlayAppItem> OpenOverlayAppCommand { get; }

        [DontSerialize]
        public RelayCommand ToggleOverlayAchievementsSortCommand { get; }

        [DontSerialize]
        public RelayCommand<AnikiMediaItem> OpenScreenshotsForMediaItemCommand { get; }

        [DontSerialize]
        public RelayCommand<AnikiMediaItem> OpenOverlayCapturePreviewCommand { get; }

        [DontSerialize]
        private int currentGameMediaLoadedCount;

        [DontSerialize]
        public int CurrentGameMediaLoadedCount
        {
            get => currentGameMediaLoadedCount;
            set => SetValue(ref currentGameMediaLoadedCount, value);
        }

        [DontSerialize]
        private bool currentGameMediaCanLoadMore;

        [DontSerialize]
        public bool CurrentGameMediaCanLoadMore
        {
            get => currentGameMediaCanLoadMore;
            set => SetValue(ref currentGameMediaCanLoadMore, value);
        }

        [DontSerialize]
        private bool currentGameMediaLoading;

        [DontSerialize]
        private Guid currentGameMediaActiveGameId = Guid.Empty;

        [DontSerialize]
        private int currentGameMediaLoadVersion = 0;

        [DontSerialize]
        private bool currentGameMediaPageLoading;

        [DontSerialize]
        public bool CurrentGameMediaLoading
        {
            get => currentGameMediaLoading;
            set => SetValue(ref currentGameMediaLoading, value);
        }

        private int currentGameMediaPageSize = 18;
        public int CurrentGameMediaPageSize
        {
            get => currentGameMediaPageSize;
            set => SetValue(ref currentGameMediaPageSize, Math.Max(9, Math.Min(60, value)));
        }

        [DontSerialize]
        public RelayCommand GenerateMediaThumbnailsCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshAchievementMemoriesCommand { get; }

        [DontSerialize]
        private bool isRefreshingAchievementMemories;

        [DontSerialize]
        public bool IsRefreshingAchievementMemories
        {
            get => isRefreshingAchievementMemories;
            set
            {
                SetValue(ref isRefreshingAchievementMemories, value);
                OnPropertyChanged(nameof(AchievementMemoriesRefreshButtonText));
            }
        }

        [DontSerialize]
        private string achievementMemoriesRefreshStatus = string.Empty;

        [DontSerialize]
        public string AchievementMemoriesRefreshStatus
        {
            get => achievementMemoriesRefreshStatus;
            set => SetValue(ref achievementMemoriesRefreshStatus, value ?? string.Empty);
        }

        [DontSerialize]
        public string AchievementMemoriesRefreshButtonText
        {
            get
            {
                return IsRefreshingAchievementMemories
                    ? Loc("AchievementCache_Status_Scanning", "Scanning achievements...")
                    : Loc("AchievementCache_Rebuild_Button", "Rebuild Achievements Cache");
            }
        }

        [DontSerialize]
        private bool mediaThumbnailPrecacheLoading;

        [DontSerialize]
        private readonly HashSet<Guid> stoppedGameMediaRefreshRunning = new HashSet<Guid>();

        [DontSerialize]
        public bool MediaThumbnailPrecacheLoading
        {
            get => mediaThumbnailPrecacheLoading;
            set => SetValue(ref mediaThumbnailPrecacheLoading, value);
        }

        [DontSerialize]
        private int mediaThumbnailPrecacheDone;

        [DontSerialize]
        public int MediaThumbnailPrecacheDone
        {
            get => mediaThumbnailPrecacheDone;
            set
            {
                SetValue(ref mediaThumbnailPrecacheDone, value);
                OnPropertyChanged(nameof(MediaThumbnailPrecacheProgressText));
                OnPropertyChanged(nameof(MediaThumbnailPrecacheProgressPercent));
            }
        }

        [DontSerialize]
        private int mediaThumbnailPrecacheTotal;

        [DontSerialize]
        public int MediaThumbnailPrecacheTotal
        {
            get => mediaThumbnailPrecacheTotal;
            set
            {
                SetValue(ref mediaThumbnailPrecacheTotal, value);
                OnPropertyChanged(nameof(MediaThumbnailPrecacheProgressText));
                OnPropertyChanged(nameof(MediaThumbnailPrecacheProgressPercent));
            }
        }

        [DontSerialize]
        private string mediaThumbnailPrecacheStatus = string.Empty;

        [DontSerialize]
        public string MediaThumbnailPrecacheStatus
        {
            get => mediaThumbnailPrecacheStatus;
            set => SetValue(ref mediaThumbnailPrecacheStatus, value);
        }

        [DontSerialize]
        public string MediaThumbnailPrecacheProgressText
        {
            get
            {
                if (MediaThumbnailPrecacheTotal <= 0)
                {
                    return "0 / 0";
                }

                return MediaThumbnailPrecacheDone + " / " + MediaThumbnailPrecacheTotal;
            }
        }

        [DontSerialize]
        public double MediaThumbnailPrecacheProgressPercent
        {
            get
            {
                if (MediaThumbnailPrecacheTotal <= 0)
                {
                    return 0;
                }

                return Math.Max(0, Math.Min(100, (MediaThumbnailPrecacheDone / (double)MediaThumbnailPrecacheTotal) * 100.0));
            }
            set
            {
                // Required because ProgressBar.Value may try to write back to the binding.
                // The real value is computed from MediaThumbnailPrecacheDone / MediaThumbnailPrecacheTotal.
            }
        }

        // === Aniki Theme Settings ===

        [DontSerialize]
        public AnikiDynamicProperties Options { get; } = new AnikiDynamicProperties();

        [DontSerialize]
        private string aspectRatio = "dsp169";

        [DontSerialize]
        public string AspectRatio
        {
            get => aspectRatio;
            set => SetValue(ref aspectRatio, string.IsNullOrWhiteSpace(value) ? "dsp169" : value);
        }

        [DontSerialize]
        public ObservableCollection<AnikiThemeSettingsCategory> AnikiThemeSettingsCategories { get; }
            = new ObservableCollection<AnikiThemeSettingsCategory>();

        [DontSerialize]
        public ObservableCollection<object> SelectedAnikiThemeSettingsCategoryItems { get; }
            = new ObservableCollection<object>();

        [DontSerialize]
        public string SelectedCategoryDisplayName
        {
            get
            {
                var selectedCategory = AnikiThemeSettingsCategories
                    .FirstOrDefault(x => string.Equals(
                        x.Id,
                        SelectedAnikiThemeSettingsCategoryId,
                        StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(selectedCategory?.WindowTitle))
                {
                    return selectedCategory.WindowTitle;
                }

                return selectedCategory?.Title ?? string.Empty;
            }
        }

        private string selectedAnikiThemeSettingsCategoryId = "General";

        [DontSerialize]
        public string SelectedAnikiThemeSettingsCategoryId
        {
            get => selectedAnikiThemeSettingsCategoryId;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value) ? "General" : value;

                if (string.Equals(selectedAnikiThemeSettingsCategoryId, finalValue, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetValue(ref selectedAnikiThemeSettingsCategoryId, finalValue);
                RefreshSelectedAnikiThemeSettingsCategoryItems();
                OnPropertyChanged(nameof(SelectedCategoryDisplayName));
            }
        }

        [DontSerialize]
        public RelayCommand<string> SelectAnikiThemeSettingsCategoryCommand { get; }

        [DontSerialize]
        public Dictionary<string, string> AnikiThemeSettingsValues { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [DontSerialize]
        public Dictionary<string, string> AnikiThemeSettingsSelectedPresets { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string anikiThemeSettingsPreviewImage;

        [DontSerialize]
        public string AnikiThemeSettingsPreviewImage
        {
            get => anikiThemeSettingsPreviewImage;
            set => SetValue(ref anikiThemeSettingsPreviewImage, value);
        }

        [DontSerialize]
        public RelayCommand<string> SetAnikiThemeOptionCommand { get; }

        [DontSerialize]
        public RelayCommand<string> ToggleAnikiThemeOptionCommand { get; }

        [DontSerialize]
        public RelayCommand<string> SelectAnikiThemePresetCommand { get; }

        [DontSerialize]
        public RelayCommand<string> ShowAnikiThemePresetPreviewCommand { get; }

        [DontSerialize]
        public RelayCommand HideAnikiThemePresetPreviewCommand { get; }

        [DontSerialize]
        public RelayCommand ReloadAnikiThemeSettingsCommand { get; }

        public void SelectAnikiThemeSettingsCategory(string categoryId)
        {
            SelectedAnikiThemeSettingsCategoryId = string.IsNullOrWhiteSpace(categoryId)
                ? "General"
                : categoryId;
        }

        public void RefreshSelectedAnikiThemeSettingsCategoryItems()
        {
            try
            {
                SelectedAnikiThemeSettingsCategoryItems.Clear();

                var selectedCategory = AnikiThemeSettingsCategories
                    .FirstOrDefault(x => string.Equals(
                        x.Id,
                        SelectedAnikiThemeSettingsCategoryId,
                        StringComparison.OrdinalIgnoreCase));

                if (selectedCategory == null)
                {
                    selectedCategory = AnikiThemeSettingsCategories.FirstOrDefault();
                }

                if (selectedCategory == null || selectedCategory.Items == null)
                {
                    OnPropertyChanged(nameof(SelectedAnikiThemeSettingsCategoryItems));
                    return;
                }

                foreach (var item in selectedCategory.Items)
                {
                    SelectedAnikiThemeSettingsCategoryItems.Add(item);
                }

                OnPropertyChanged(nameof(SelectedAnikiThemeSettingsCategoryItems));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to refresh selected Aniki Theme Settings category items.");
            }
        }

        public void LoadMoreCurrentGameMediaItems()
        {
            if (currentGameMediaPageLoading)
            {
                return;
            }

            try
            {
                currentGameMediaPageLoading = true;

                if (CurrentGameMediaItems == null || CurrentGameMediaItems.Count == 0)
                {
                    CurrentGameMediaLoadedCount = 0;
                    CurrentGameMediaCanLoadMore = false;
                    return;
                }

                if (!CurrentGameMediaCanLoadMore && CurrentGameMediaLoadedCount >= CurrentGameMediaItems.Count)
                {
                    return;
                }

                var start = CurrentGameMediaLoadedCount;

                if (start < 0)
                {
                    start = 0;
                }

                if (start > CurrentGameMediaItems.Count)
                {
                    start = CurrentGameMediaItems.Count;
                }

                var take = Math.Max(9, CurrentGameMediaPageSize);

                var nextItems = CurrentGameMediaItems
                    .Skip(start)
                    .Take(take)
                    .ToList();

                if (nextItems.Count == 0)
                {
                    CurrentGameMediaLoadedCount = VisibleCurrentGameMediaItems.Count;
                    CurrentGameMediaCanLoadMore = false;
                    return;
                }

                // Important:
                // Update the loaded index BEFORE adding items to the visible collection.
                // This prevents ScrollChanged / SelectionChanged from re-entering this method
                // and loading the same page again while items are still being added.
                CurrentGameMediaLoadedCount = start + nextItems.Count;
                CurrentGameMediaCanLoadMore = CurrentGameMediaLoadedCount < CurrentGameMediaItems.Count;

                var visibleKeys = new HashSet<string>(
                    VisibleCurrentGameMediaItems
                        .Where(x => x != null)
                        .Select(GetMediaUniqueKey)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase
                );

                foreach (var item in nextItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var key = GetMediaUniqueKey(item);

                    // Defensive protection:
                    // If the same exact media file is already visible, do not add it again.
                    if (!string.IsNullOrWhiteSpace(key) && visibleKeys.Contains(key))
                    {
                        continue;
                    }

                    VisibleCurrentGameMediaItems.Add(item);

                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        visibleKeys.Add(key);
                    }
                }

                OnPropertyChanged(nameof(VisibleCurrentGameMediaItems));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load more current game media items.");
            }
            finally
            {
                currentGameMediaPageLoading = false;
            }
        }
        private static string GetMediaUniqueKey(AnikiMediaItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.FilePath))
            {
                try
                {
                    return Path.GetFullPath(item.FilePath)
                        .Trim()
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    return item.FilePath.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
            {
                return item.ThumbnailPath.Trim();
            }

            return $"{item.GameId}|{item.FileName}|{item.CaptureDate:O}";
        }

        [DontSerialize]
        public ObservableCollection<AnikiMediaItem> MediaGalleryItems { get; set; }
            = new ObservableCollection<AnikiMediaItem>();

        [DontSerialize]
        public ObservableCollection<AnikiMediaItem> VisibleMediaGalleryItems { get; set; }
            = new ObservableCollection<AnikiMediaItem>();

        private AnikiMediaProviderMode mediaGalleryProvider = AnikiMediaProviderMode.ScreenshotsVisualizer;
        public AnikiMediaProviderMode MediaGalleryProvider
        {
            get => mediaGalleryProvider;
            set
            {
                if (mediaGalleryProvider == value)
                {
                    return;
                }

                mediaGalleryProvider = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MediaGalleryProviderName));
            }
        }

        public string MediaGalleryProviderName
        {
            get
            {
                switch (MediaGalleryProvider)
                {
                    case AnikiMediaProviderMode.ScreenshotUtilitiesLocal:
                        return "Screenshot Utilities - Local";

                    case AnikiMediaProviderMode.ScreenshotsVisualizer:
                    default:
                        return "Screenshots Visualizer";
                }
            }
        }

        [DontSerialize]
        private bool mediaGalleryLoading;
        [DontSerialize]
        public bool MediaGalleryLoading
        {
            get => mediaGalleryLoading;
            set => SetValue(ref mediaGalleryLoading, value);
        }

        [DontSerialize]
        private string mediaGalleryStatusText = string.Empty;
        [DontSerialize]
        public string MediaGalleryStatusText
        {
            get => mediaGalleryStatusText;
            set => SetValue(ref mediaGalleryStatusText, value);
        }

        [DontSerialize]
        private int mediaGalleryCount;
        [DontSerialize]
        public int MediaGalleryCount
        {
            get => mediaGalleryCount;
            set => SetValue(ref mediaGalleryCount, value);
        }

        [DontSerialize]
        private bool hasCurrentGameMedia;

        [DontSerialize]
        public bool HasCurrentGameMedia
        {
            get => hasCurrentGameMedia;
            set => SetValue(ref hasCurrentGameMedia, value);
        }

        [DontSerialize]
        public AnikiWindowCommandProvider OpenWindow { get; }

        [DontSerialize]
        public ObservableCollection<AnikiDuplicateHiderGameItem> DuplicateHiderGameVersions { get; }
        = new ObservableCollection<AnikiDuplicateHiderGameItem>();

        [DontSerialize]
        private bool hasDuplicateHiderVersions;

        [DontSerialize]
        public bool HasDuplicateHiderVersions
        {
            get => hasDuplicateHiderVersions;
            set => SetValue(ref hasDuplicateHiderVersions, value);
        }

        [DontSerialize]
        public RelayCommand OpenDuplicateHiderVersionsWindowCommand { get; }

        [DontSerialize]
        public AnikiWindowCommandProvider OpenChildWindow { get; }

        [DontSerialize]
        public RelayCommand OpenInGameOverlayCommand { get; }

        [DontSerialize]
        public AnikiWindowCommandProvider OpenInGameOverlay { get; }

        public ICommand OpenSteamGameNewsWindowCommand { get; }

        [DontSerialize]
        public RelayCommand CloseTopWindowCommand { get; }

        [DontSerialize]
        public RelayCommand OpenWhatsNewCommand { get; }

        [DontSerialize]
        public RelayCommand OpenNotificationsCommand { get; }

        [DontSerialize]
        public RelayCommand OpenAchievementsCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshRecentAchievementsCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshInstalledAchievementsCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshFavoritesAchievementsCommand { get; }

        [DontSerialize]
        public RelayCommand RefreshFullAchievementsCommand { get; }

        [DontSerialize]
        private bool isQuickAccessToMainMenuDimActive;

        [DontSerialize]
        public bool IsQuickAccessToMainMenuDimActive
        {
            get => isQuickAccessToMainMenuDimActive;
            set => SetValue(ref isQuickAccessToMainMenuDimActive, value);
        }

        [DontSerialize]
        public RelayCommand OpenLockScreenCommand { get; }

        [DontSerialize]
        public RelayCommand OpenPowerMenuCommand { get; }

        [DontSerialize]
        public RelayCommand OpenExternalClientsCommand { get; }

        [DontSerialize]
        public RelayCommand OpenRandomGameCommand { get; }

        [DontSerialize]
        public RelayCommand UpdateGameLibraryCommand { get; }

        [DontSerialize]
        public AnikiWindowCommandProvider OpenHelpLink { get; }

        [DontSerialize]
        public AnikiWindowCommandProvider MusicTransport { get; }

        [DontSerialize]
        public RelayCommand NextNewsTabCommand { get; }

        [DontSerialize]
        public RelayCommand PreviousNewsTabCommand { get; }

        [DontSerialize]
        public RelayCommand QuickOptionsPreviousSectionCommand { get; }

        [DontSerialize]
        public RelayCommand QuickOptionsNextSectionCommand { get; }

        [DontSerialize]
        public RelayCommand CloseHubToLibraryCommand { get; }

        [DontSerialize]
        public RelayCommand OpenPlayniteMainMenuCommand { get; }

        public RelayCommand OpenPlayniteSettingsCommand { get; }

        [DontSerialize]
        private int hubCurrentPage = 1;

        [DontSerialize]
        public int HubCurrentPage
        {
            get => hubCurrentPage;
            set
            {
                var newValue = Math.Max(1, Math.Min(HubMaxPage, value));

                if (hubCurrentPage == newValue)
                {
                    return;
                }

                HubPreviousPage = hubCurrentPage;
                HubPageDirection = newValue > hubCurrentPage ? "Forward" : "Backward";

                SetValue(ref hubCurrentPage, newValue);

                NotifyHubPageStateProperties();
                if (newValue >= 2)
                {
                    RequestSteamStoreLoad();
                }
            }
        }

        [DontSerialize]
        private int hubPreviousPage = 1;

        [DontSerialize]
        public int HubPreviousPage
        {
            get => hubPreviousPage;
            set => SetValue(ref hubPreviousPage, value);
        }

        [DontSerialize]
        private string hubPageDirection = "None";

        [DontSerialize]
        public string HubPageDirection
        {
            get => hubPageDirection;
            set => SetValue(ref hubPageDirection, value ?? "None");
        }

        [DontSerialize]
        public int HubMaxPage => ShowHubAppsPage ? 10 : 9;

        [DontSerialize]
        public string HubCurrentPageTag => $"Page{HubCurrentPage}";

        [DontSerialize]
        public bool HubIsPage1 => HubCurrentPage == 1;

        [DontSerialize]
        public bool HubIsPage2 => HubCurrentPage == 2;

        [DontSerialize]
        public bool HubIsPage3 => HubCurrentPage == 3;

        [DontSerialize]
        public bool HubIsPage4 => HubCurrentPage == 4;

        [DontSerialize]
        public bool HubIsPage5 => HubCurrentPage == 5;

        [DontSerialize]
        public bool HubIsPage6 => HubCurrentPage == 6;

        [DontSerialize]
        public bool HubIsPage7 => HubCurrentPage == 7;

        [DontSerialize]
        public bool HubIsPage8 => HubCurrentPage == 8;

        [DontSerialize]
        public bool HubIsPage9 => HubCurrentPage == 9;

        [DontSerialize]
        public bool HubIsPage10 => HubCurrentPage == 10;

        [DontSerialize]
        public bool HubIsAppsPage => ShowHubAppsPage && HubCurrentPage == 3;

        [DontSerialize]
        public bool HubIsLibraryRecommendedPage => HubCurrentPage == (ShowHubAppsPage ? 4 : 3);

        [DontSerialize]
        public bool HubIsFriendActivityPage => HubCurrentPage == (ShowHubAppsPage ? 5 : 4);

        [DontSerialize]
        public bool HubIsLatestCapturesPage => HubCurrentPage == (ShowHubAppsPage ? 6 : 5);

        [DontSerialize]
        public bool HubIsAchievementMemoriesPage => HubCurrentPage == (ShowHubAppsPage ? 7 : 6);

        [DontSerialize]
        public bool HubIsForYouStorePage => HubCurrentPage == (ShowHubAppsPage ? 8 : 7);

        [DontSerialize]
        public bool HubIsStorePage => HubCurrentPage == (ShowHubAppsPage ? 9 : 8);

        [DontSerialize]
        public bool HubIsUpcomingPage => HubCurrentPage == (ShowHubAppsPage ? 10 : 9);

        private void NotifyHubPageStateProperties()
        {
            OnPropertyChanged(nameof(HubCurrentPageTag));
            OnPropertyChanged(nameof(HubIsPage1));
            OnPropertyChanged(nameof(HubIsPage2));
            OnPropertyChanged(nameof(HubIsPage3));
            OnPropertyChanged(nameof(HubIsPage4));
            OnPropertyChanged(nameof(HubIsPage5));
            OnPropertyChanged(nameof(HubIsPage6));
            OnPropertyChanged(nameof(HubIsPage7));
            OnPropertyChanged(nameof(HubIsPage8));
            OnPropertyChanged(nameof(HubIsPage9));
            OnPropertyChanged(nameof(HubIsPage10));
            OnPropertyChanged(nameof(HubIsAppsPage));
            OnPropertyChanged(nameof(HubIsLibraryRecommendedPage));
            OnPropertyChanged(nameof(HubIsFriendActivityPage));
            OnPropertyChanged(nameof(HubIsLatestCapturesPage));
            OnPropertyChanged(nameof(HubIsAchievementMemoriesPage));
            OnPropertyChanged(nameof(HubIsForYouStorePage));
            OnPropertyChanged(nameof(HubIsStorePage));
            OnPropertyChanged(nameof(HubIsUpcomingPage));
        }

        public void SetHubPage(object page)
        {
            EnsureHubAppsSoftwareToolsLoaded();

            if (page == null)
            {
                return;
            }

            var text = page.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            text = text.Replace("Page", "").Trim();

            if (int.TryParse(text, out var pageNumber))
            {
                HubCurrentPage = pageNumber;
            }
        }

        public void NextHubPage()
        {
            EnsureHubAppsSoftwareToolsLoaded();
            HubCurrentPage++;
        }

        public void PreviousHubPage()
        {
            EnsureHubAppsSoftwareToolsLoaded();
            HubCurrentPage--;
        }

        private bool isWelcomeHubOpen = true;
        public bool IsWelcomeHubOpen
        {
            get => isWelcomeHubOpen;
            set => SetValue(ref isWelcomeHubOpen, value);
        }

        [DontSerialize]
        private bool isFastNavigating = false;

        [DontSerialize]
        public bool IsFastNavigating
        {
            get => isFastNavigating;
            set => SetValue(ref isFastNavigating, value);
        }

        private bool isWelcomeHubClosing = false;
        public bool IsWelcomeHubClosing
        {
            get => isWelcomeHubClosing;
            set => SetValue(ref isWelcomeHubClosing, value);
        }

        private bool openWelcomeHubOnStartup = true;
        public bool OpenWelcomeHubOnStartup
        {
            get => openWelcomeHubOnStartup;
            set => SetValue(ref openWelcomeHubOnStartup, value);
        }

        private bool hubAppsEnabled = false;
        public bool HubAppsEnabled
        {
            get => hubAppsEnabled;
            set
            {
                SetValue(ref hubAppsEnabled, value);
                RefreshHubApps();
                EnsureHubCurrentPageInRange();
            }
        }

        private string hubAppSlot1ToolName = string.Empty;
        public string HubAppSlot1ToolName
        {
            get => hubAppSlot1ToolName;
            set
            {
                SetValue(ref hubAppSlot1ToolName, NormalizeSettingText(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot2ToolName = string.Empty;
        public string HubAppSlot2ToolName
        {
            get => hubAppSlot2ToolName;
            set
            {
                SetValue(ref hubAppSlot2ToolName, NormalizeSettingText(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot3ToolName = string.Empty;
        public string HubAppSlot3ToolName
        {
            get => hubAppSlot3ToolName;
            set
            {
                SetValue(ref hubAppSlot3ToolName, NormalizeSettingText(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot4ToolName = string.Empty;
        public string HubAppSlot4ToolName
        {
            get => hubAppSlot4ToolName;
            set
            {
                SetValue(ref hubAppSlot4ToolName, NormalizeSettingText(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot1BackgroundPath = string.Empty;
        public string HubAppSlot1BackgroundPath
        {
            get => hubAppSlot1BackgroundPath;
            set
            {
                SetValue(ref hubAppSlot1BackgroundPath, NormalizeExternalPath(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot2BackgroundPath = string.Empty;
        public string HubAppSlot2BackgroundPath
        {
            get => hubAppSlot2BackgroundPath;
            set
            {
                SetValue(ref hubAppSlot2BackgroundPath, NormalizeExternalPath(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot3BackgroundPath = string.Empty;
        public string HubAppSlot3BackgroundPath
        {
            get => hubAppSlot3BackgroundPath;
            set
            {
                SetValue(ref hubAppSlot3BackgroundPath, NormalizeExternalPath(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot4BackgroundPath = string.Empty;
        public string HubAppSlot4BackgroundPath
        {
            get => hubAppSlot4BackgroundPath;
            set
            {
                SetValue(ref hubAppSlot4BackgroundPath, NormalizeExternalPath(value));
                RefreshHubApps();
            }
        }

        private string customFilterIconsFolder = string.Empty;
        public string CustomFilterIconsFolder
        {
            get => customFilterIconsFolder;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.Trim().Replace("\\", "/").TrimEnd('/');

                SetValue(ref customFilterIconsFolder, finalValue);
            }
        }

        private string customSourceIconsFolder = string.Empty;
        public string CustomSourceIconsFolder
        {
            get => customSourceIconsFolder;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.Trim().Replace("\\", "/").TrimEnd('/');

                SetValue(ref customSourceIconsFolder, finalValue);
            }
        }

        private string customBannerAboveCoverFolder = string.Empty;
        public string CustomBannerAboveCoverFolder
        {
            get => customBannerAboveCoverFolder;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.Trim().Replace("\\", "/").TrimEnd('/');

                SetValue(ref customBannerAboveCoverFolder, finalValue);
            }
        }

        private string customBannerOnCoverFolder = string.Empty;
        public string CustomBannerOnCoverFolder
        {
            get => customBannerOnCoverFolder;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.Trim().Replace("\\", "/").TrimEnd('/');

                SetValue(ref customBannerOnCoverFolder, finalValue);
            }
        }

        [DontSerialize]
        public RelayCommand CloseSteamStoreDetailsCommand { get; }

        [DontSerialize]
        public RelayCommand OpenSteamStorePageExternalCommand { get; }

        [DontSerialize]
        public RelayCommand<object> OpenSteamStoreScreenshotViewerCommand { get; }

        [DontSerialize]
        public RelayCommand CloseSteamStoreScreenshotViewerCommand { get; }

        private bool steamStoreDetailsVisible;
        public bool SteamStoreDetailsVisible
        {
            get => steamStoreDetailsVisible;
            set => SetValue(ref steamStoreDetailsVisible, value);
        }

        private string steamStoreDetailsTitle;
        public string SteamStoreDetailsTitle
        {
            get => steamStoreDetailsTitle;
            set => SetValue(ref steamStoreDetailsTitle, value);
        }

        private string steamStoreDetailsImage;
        public string SteamStoreDetailsImage
        {
            get => steamStoreDetailsImage;
            set => SetValue(ref steamStoreDetailsImage, value);
        }

        private string steamStoreDetailsBackgroundImage;
        public string SteamStoreDetailsBackgroundImage
        {
            get => steamStoreDetailsBackgroundImage;
            set => SetValue(ref steamStoreDetailsBackgroundImage, value);
        }

        private string steamStoreDetailsDescription;
        public string SteamStoreDetailsDescription
        {
            get => steamStoreDetailsDescription;
            set => SetValue(ref steamStoreDetailsDescription, value);
        }

        private string steamStoreDetailsPrice;
        public string SteamStoreDetailsPrice
        {
            get => steamStoreDetailsPrice;
            set => SetValue(ref steamStoreDetailsPrice, value);
        }

        private string steamStoreDetailsDiscount;
        public string SteamStoreDetailsDiscount
        {
            get => steamStoreDetailsDiscount;
            set => SetValue(ref steamStoreDetailsDiscount, value);
        }

        private string steamStoreDetailsOriginalPrice;
        public string SteamStoreDetailsOriginalPrice
        {
            get => steamStoreDetailsOriginalPrice;
            set => SetValue(ref steamStoreDetailsOriginalPrice, value);
        }

        private string steamStoreDetailsMetacriticScore;
        public string SteamStoreDetailsMetacriticScore
        {
            get => steamStoreDetailsMetacriticScore;
            set => SetValue(ref steamStoreDetailsMetacriticScore, value);
        }

        private string steamStoreDetailsRecommendationsTotal;
        public string SteamStoreDetailsRecommendationsTotal
        {
            get => steamStoreDetailsRecommendationsTotal;
            set => SetValue(ref steamStoreDetailsRecommendationsTotal, value);
        }

        private string steamStoreDetailsAchievementsTotal;
        public string SteamStoreDetailsAchievementsTotal
        {
            get => steamStoreDetailsAchievementsTotal;
            set => SetValue(ref steamStoreDetailsAchievementsTotal, value);
        }

        private string steamStoreDetailsDlcCount;
        public string SteamStoreDetailsDlcCount
        {
            get => steamStoreDetailsDlcCount;
            set => SetValue(ref steamStoreDetailsDlcCount, value);
        }

        private string steamStoreDetailsScreenshot1;
        public string SteamStoreDetailsScreenshot1
        {
            get => steamStoreDetailsScreenshot1;
            set => SetValue(ref steamStoreDetailsScreenshot1, value);
        }

        private string steamStoreDetailsScreenshot2;
        public string SteamStoreDetailsScreenshot2
        {
            get => steamStoreDetailsScreenshot2;
            set => SetValue(ref steamStoreDetailsScreenshot2, value);
        }

        private string steamStoreDetailsScreenshot3;
        public string SteamStoreDetailsScreenshot3
        {
            get => steamStoreDetailsScreenshot3;
            set => SetValue(ref steamStoreDetailsScreenshot3, value);
        }

        private string steamStoreDetailsScreenshot4;
        public string SteamStoreDetailsScreenshot4
        {
            get => steamStoreDetailsScreenshot4;
            set => SetValue(ref steamStoreDetailsScreenshot4, value);
        }

        private string steamStoreDetailsScreenshot5;
        public string SteamStoreDetailsScreenshot5
        {
            get => steamStoreDetailsScreenshot5;
            set => SetValue(ref steamStoreDetailsScreenshot5, value);
        }

        private bool steamStoreScreenshotViewerVisible;
        public bool SteamStoreScreenshotViewerVisible
        {
            get => steamStoreScreenshotViewerVisible;
            set => SetValue(ref steamStoreScreenshotViewerVisible, value);
        }

        private string steamStoreScreenshotViewerImage;
        public string SteamStoreScreenshotViewerImage
        {
            get => steamStoreScreenshotViewerImage;
            set => SetValue(ref steamStoreScreenshotViewerImage, value);
        }

        private int steamStoreDetailsAppId;
        public int SteamStoreDetailsAppId
        {
            get => steamStoreDetailsAppId;
            set => SetValue(ref steamStoreDetailsAppId, value);
        }

        private string steamStoreDetailsStoreUrl;
        public string SteamStoreDetailsStoreUrl
        {
            get => steamStoreDetailsStoreUrl;
            set => SetValue(ref steamStoreDetailsStoreUrl, value);
        }

        private string steamStoreDetailsReleaseDate;
        public string SteamStoreDetailsReleaseDate
        {
            get => steamStoreDetailsReleaseDate;
            set => SetValue(ref steamStoreDetailsReleaseDate, value);
        }

        private bool steamStoreDetailsIsPreorder;
        public bool SteamStoreDetailsIsPreorder
        {
            get => steamStoreDetailsIsPreorder;
            set => SetValue(ref steamStoreDetailsIsPreorder, value);
        }

        private bool steamStoreDetailsLoading;
        public bool SteamStoreDetailsLoading
        {
            get => steamStoreDetailsLoading;
            set => SetValue(ref steamStoreDetailsLoading, value);
        }

        private string steamStoreDetailsDevelopers;
        public string SteamStoreDetailsDevelopers
        {
            get => steamStoreDetailsDevelopers;
            set => SetValue(ref steamStoreDetailsDevelopers, value);
        }

        private string steamStoreDetailsPublishers;
        public string SteamStoreDetailsPublishers
        {
            get => steamStoreDetailsPublishers;
            set => SetValue(ref steamStoreDetailsPublishers, value);
        }

        private string steamStoreDetailsGenres;
        public string SteamStoreDetailsGenres
        {
            get => steamStoreDetailsGenres;
            set => SetValue(ref steamStoreDetailsGenres, value);
        }

        private string steamStoreDetailsCategories;
        public string SteamStoreDetailsCategories
        {
            get => steamStoreDetailsCategories;
            set => SetValue(ref steamStoreDetailsCategories, value);
        }

        private string steamStoreDetailsSupportedLanguages;
        public string SteamStoreDetailsSupportedLanguages
        {
            get => steamStoreDetailsSupportedLanguages;
            set => SetValue(ref steamStoreDetailsSupportedLanguages, value);
        }

        private string steamStoreDetailsControllerSupport;
        public string SteamStoreDetailsControllerSupport
        {
            get => steamStoreDetailsControllerSupport;
            set => SetValue(ref steamStoreDetailsControllerSupport, value);
        }

        [DontSerialize]
        public AnikiMinimalVersion MinimalVersion { get; } = new AnikiMinimalVersion();

        [DontSerialize]
        public string Version
        {
            get => AnikiMinimalVersion.PluginVersion;
        }

        [DontSerialize]
        public bool IsInstalled
        {
            get => true;
        }

        public string InstalledPluginVersion { get; set; } = "";
        public bool VersionCheckReady { get; set; } = true;
        public bool IsPluginUpdateRequired { get; set; } = false;
        public string RequiredAnikiHelperVersion { get; set; } = "";

        // Info snapshot 
        private string snapshotDateString;
        public string SnapshotDateString { get => snapshotDateString; set => SetValue(ref snapshotDateString, value); }

        // Options stats / display 
        private bool includeHidden = false;
        private bool enableDebugLogs = false;
        private int topPlayedMax = 10;
        private bool playtimeStoredInHours = false;
        private bool playtimeUseDaysFormat = false;

        // Dynamic colors / precache 
        private bool dynamicAutoPrecacheUserEnabled = true;
        public string DynamicColorCacheVersion { get; set; } = "";

        // Storage 
        private readonly ObservableCollection<DiskUsageItem> diskUsages = new ObservableCollection<DiskUsageItem>();
        public ObservableCollection<DiskUsageItem> DiskUsages => diskUsages;

        // Stats (values)
        private int totalCount;
        private int installedCount;
        private int notInstalledCount;
        private int hiddenCount;
        private int favoriteCount;
        private ulong totalPlaytimeMinutes;
        private ulong averagePlaytimeMinutes;

        // Reference games for the Hub library recommendation section.
        public Guid RefGameLastId { get; set; } = Guid.Empty;
        public DateTime RefGameLastChangeDate { get; set; } = DateTime.MinValue;


        // MOST PLAYED GAME OF THE MONTH

        private string thisMonthTopGameName;
        private string thisMonthTopGamePlaytime;
        private string thisMonthTopGameCoverPath;
        private string thisMonthTopGameBackgroundPath;

        public string ThisMonthTopGameName { get => thisMonthTopGameName; set => SetValue(ref thisMonthTopGameName, value); }
        public string ThisMonthTopGamePlaytime { get => thisMonthTopGamePlaytime; set => SetValue(ref thisMonthTopGamePlaytime, value); }
        public string ThisMonthTopGameCoverPath { get => thisMonthTopGameCoverPath; set => SetValue(ref thisMonthTopGameCoverPath, value); }
        public string ThisMonthTopGameBackgroundPath { get => thisMonthTopGameBackgroundPath; set => SetValue(ref thisMonthTopGameBackgroundPath, value); }
        private Guid thisMonthTopGameId = Guid.Empty;
        public Guid ThisMonthTopGameId
        {
            get => thisMonthTopGameId;
            set => SetValue(ref thisMonthTopGameId, value);
        }

        // This month's stats
        private int thisMonthPlayedCount;
        private ulong thisMonthPlayedTotalMinutes;

        public int ThisMonthPlayedCount { get => thisMonthPlayedCount; set => SetValue(ref thisMonthPlayedCount, value); }
        public ulong ThisMonthPlayedTotalMinutes
        {
            get => thisMonthPlayedTotalMinutes;
            set { SetValue(ref thisMonthPlayedTotalMinutes, value); OnPropertyChanged(nameof(ThisMonthPlayedTotalString)); }
        }
        public string ThisMonthPlayedTotalString => PlaytimeToString(ThisMonthPlayedTotalMinutes, false);

        // MOST PLAYED GAME OF THE YEAR

        private string thisYearTopGameName;
        private string thisYearTopGamePlaytime;
        private string thisYearTopGameCoverPath;
        private string thisYearTopGameBackgroundPath;

        public string ThisYearTopGameName { get => thisYearTopGameName; set => SetValue(ref thisYearTopGameName, value); }
        public string ThisYearTopGamePlaytime { get => thisYearTopGamePlaytime; set => SetValue(ref thisYearTopGamePlaytime, value); }
        public string ThisYearTopGameCoverPath { get => thisYearTopGameCoverPath; set => SetValue(ref thisYearTopGameCoverPath, value); }
        public string ThisYearTopGameBackgroundPath { get => thisYearTopGameBackgroundPath; set => SetValue(ref thisYearTopGameBackgroundPath, value); }
        private Guid thisYearTopGameId = Guid.Empty;
        public Guid ThisYearTopGameId
        {
            get => thisYearTopGameId;
            set => SetValue(ref thisYearTopGameId, value);
        }

        // This year's stats
        private int thisYearPlayedCount;
        private ulong thisYearPlayedTotalMinutes;

        public int ThisYearPlayedCount { get => thisYearPlayedCount; set => SetValue(ref thisYearPlayedCount, value); }
        public ulong ThisYearPlayedTotalMinutes
        {
            get => thisYearPlayedTotalMinutes;
            set
            {
                SetValue(ref thisYearPlayedTotalMinutes, value);
                OnPropertyChanged(nameof(ThisYearPlayedTotalString));
            }
        }
        public string ThisYearPlayedTotalString => PlaytimeToString(ThisYearPlayedTotalMinutes, false);

        // Genre Profil
        private string profileGenreKey;
        public string ProfileGenreKey
        {
            get => profileGenreKey;
            set => SetValue(ref profileGenreKey, value);
        }

        private string profileGenreLabel;
        public string ProfileGenreLabel
        {
            get => profileGenreLabel;
            set => SetValue(ref profileGenreLabel, value);
        }

        private DateTime lastProfileGenreScanUtc = DateTime.MinValue;
        public DateTime LastProfileGenreScanUtc
        {
            get => lastProfileGenreScanUtc;
            set => SetValue(ref lastProfileGenreScanUtc, value);
        }

        // Session summary
        private string sessionGameName;
        public string SessionGameName { get => sessionGameName; set => SetValue(ref sessionGameName, value); }

        private string sessionDurationString;
        public string SessionDurationString { get => sessionDurationString; set => SetValue(ref sessionDurationString, value); }

        private string sessionNewAchievementsString;
        public string SessionNewAchievementsString { get => sessionNewAchievementsString; set => SetValue(ref sessionNewAchievementsString, value); }

        private string sessionTotalPlaytimeString;
        public string SessionTotalPlaytimeString { get => sessionTotalPlaytimeString; set => SetValue(ref sessionTotalPlaytimeString, value); }
        private string sessionGameBackgroundPath;
        public string SessionGameBackgroundPath
        {
            get => sessionGameBackgroundPath;
            set => SetValue(ref sessionGameBackgroundPath, value);
        }

        private Guid sessionGameId = Guid.Empty;
        public Guid SessionGameId
        {
            get => sessionGameId;
            set => SetValue(ref sessionGameId, value);
        }

        private string recentPlayedBackgroundPath;
        public string RecentPlayedBackgroundPath
        {
            get => recentPlayedBackgroundPath;
            set => SetValue(ref recentPlayedBackgroundPath, value);
        }

        private string hubRecentAddedName;
        public string HubRecentAddedName
        {
            get => hubRecentAddedName;
            set => SetValue(ref hubRecentAddedName, value);
        }

        private string hubRecentAddedDate;
        public string HubRecentAddedDate
        {
            get => hubRecentAddedDate;
            set => SetValue(ref hubRecentAddedDate, value);
        }

        private string hubRecentAddedBackgroundPath;
        public string HubRecentAddedBackgroundPath
        {
            get => hubRecentAddedBackgroundPath;
            set => SetValue(ref hubRecentAddedBackgroundPath, value);
        }

        private Guid hubRecentAddedGameId = Guid.Empty;
        public Guid HubRecentAddedGameId
        {
            get => hubRecentAddedGameId;
            set => SetValue(ref hubRecentAddedGameId, value);
        }

        private string hubNeverPlayedName;
        public string HubNeverPlayedName
        {
            get => hubNeverPlayedName;
            set => SetValue(ref hubNeverPlayedName, value);
        }

        private string hubNeverPlayedDate;
        public string HubNeverPlayedDate
        {
            get => hubNeverPlayedDate;
            set => SetValue(ref hubNeverPlayedDate, value);
        }

        private string hubNeverPlayedBackgroundPath;
        public string HubNeverPlayedBackgroundPath
        {
            get => hubNeverPlayedBackgroundPath;
            set => SetValue(ref hubNeverPlayedBackgroundPath, value);
        }

        private Guid hubNeverPlayedGameId = Guid.Empty;
        public Guid HubNeverPlayedGameId
        {
            get => hubNeverPlayedGameId;
            set => SetValue(ref hubNeverPlayedGameId, value);
        }

        private Guid recentPlayedGameId = Guid.Empty;
        public Guid RecentPlayedGameId
        {
            get => recentPlayedGameId;
            set => SetValue(ref recentPlayedGameId, value);
        }

        // Unique stamp for each notification
        private string sessionNotificationStamp;
        public string SessionNotificationStamp { get => sessionNotificationStamp; set => SetValue(ref sessionNotificationStamp, value); }

        // Session notification
        private bool sessionNotificationFlip;
        public bool SessionNotificationFlip
        {
            get => sessionNotificationFlip;
            set => SetValue(ref sessionNotificationFlip, value);
        }

        private bool sessionNotificationArmed;
        public bool SessionNotificationArmed
        {
            get => sessionNotificationArmed;
            set => SetValue(ref sessionNotificationArmed, value);
        }


        // New trophies
        private int sessionNewAchievementsCount;
        public int SessionNewAchievementsCount
        {
            get => sessionNewAchievementsCount;
            set => SetValue(ref sessionNewAchievementsCount, value);
        }

        private bool sessionHasNewAchievements;
        public bool SessionHasNewAchievements
        {
            get => sessionHasNewAchievements;
            set => SetValue(ref sessionHasNewAchievements, value);
        }

        // === Steam Update / Patch notes ===

        // News headline
        private string steamUpdateTitle;
        public string SteamUpdateTitle
        {
            get => steamUpdateTitle;
            set => SetValue(ref steamUpdateTitle, value);
        }

        private bool steamUpdateIsNew;
        public bool SteamUpdateIsNew
        {
            get => steamUpdateIsNew;
            set => SetValue(ref steamUpdateIsNew, value);
        }



        // Date 
        private string steamUpdateDate;
        public string SteamUpdateDate
        {
            get => steamUpdateDate;
            set => SetValue(ref steamUpdateDate, value);
        }

        // Content HTML
        private string steamUpdateHtml;
        public string SteamUpdateHtml
        {
            get => steamUpdateHtml;
            set => SetValue(ref steamUpdateHtml, value);
        }

        // Update Available
        private bool steamUpdateAvailable;
        public bool SteamUpdateAvailable
        {
            get => steamUpdateAvailable;
            set => SetValue(ref steamUpdateAvailable, value);
        }

        private string steamUpdateError;
        public string SteamUpdateError
        {
            get => steamUpdateError;
            set => SetValue(ref steamUpdateError, value);
        }

        // === Steam Game News ===
        private bool steamGameNewsLoading;
        public bool SteamGameNewsLoading
        {
            get => steamGameNewsLoading;
            set => SetValue(ref steamGameNewsLoading, value);
        }

        private bool steamGameNewsAvailable;
        public bool SteamGameNewsAvailable
        {
            get => steamGameNewsAvailable;
            set => SetValue(ref steamGameNewsAvailable, value);
        }

        private string steamGameNewsError;
        public string SteamGameNewsError
        {
            get => steamGameNewsError;
            set => SetValue(ref steamGameNewsError, value);
        }


        // Steam Current Players (nombre de joueurs connectés)

        private string steamCurrentPlayersString;
        public string SteamCurrentPlayersString
        {
            get => steamCurrentPlayersString;
            set => SetValue(ref steamCurrentPlayersString, value);
        }

        private bool steamCurrentPlayersAvailable;
        public bool SteamCurrentPlayersAvailable
        {
            get => steamCurrentPlayersAvailable;
            set => SetValue(ref steamCurrentPlayersAvailable, value);
        }

        private string steamCurrentPlayersError;
        public string SteamCurrentPlayersError
        {
            get => steamCurrentPlayersError;
            set => SetValue(ref steamCurrentPlayersError, value);
        }

        // Global News Steam

        public ObservableCollection<SteamGlobalNewsItem> SteamGlobalNewsA { get; set; }
       = new ObservableCollection<SteamGlobalNewsItem>();
        public ObservableCollection<SteamGlobalNewsItem> SteamGlobalNewsB { get; set; }
            = new ObservableCollection<SteamGlobalNewsItem>();

        public DateTime? SteamGlobalNewsALastRefreshUtc { get; set; }
        public DateTime? SteamGlobalNewsBLastRefreshUtc { get; set; }

        // --- Custom News Sources ---

        private string newsSourceATitle = "News";
        public string NewsSourceATitle
        {
            get => newsSourceATitle;
            set => SetValue(ref newsSourceATitle, value);
        }

        private string newsSourceBTitle = "Reviews";
        public string NewsSourceBTitle
        {
            get => newsSourceBTitle;
            set => SetValue(ref newsSourceBTitle, value);
        }

        private const string DefaultNewsSourceAUrl = "https://gameinformer.com/news.xml";
        private const string DefaultNewsSourceBUrl = "https://gameinformer.com/reviews.xml";

        private string newsSourceAUrl = DefaultNewsSourceAUrl;
        public string NewsSourceAUrl
        {
            get => string.IsNullOrWhiteSpace(newsSourceAUrl) ? DefaultNewsSourceAUrl : newsSourceAUrl;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value) ? DefaultNewsSourceAUrl : value.Trim();
                SetValue(ref newsSourceAUrl, finalValue);
            }
        }

        private string newsSourceBUrl = DefaultNewsSourceBUrl;
        public string NewsSourceBUrl
        {
            get => string.IsNullOrWhiteSpace(newsSourceBUrl) ? DefaultNewsSourceBUrl : newsSourceBUrl;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value) ? DefaultNewsSourceBUrl : value.Trim();
                SetValue(ref newsSourceBUrl, finalValue);
            }
        }

        public string LastCachedNewsSourceAUrl { get; set; }
        public string LastCachedNewsSourceBUrl { get; set; }

        // Snapshot for Welcome Hub

        private string latestNewsTitle;
        public string LatestNewsTitle
        {
            get => latestNewsTitle;
            set => SetValue(ref latestNewsTitle, value);
        }

        private string latestNewsDateString;
        public string LatestNewsDateString
        {
            get => latestNewsDateString;
            set => SetValue(ref latestNewsDateString, value);
        }

        private string latestNewsSummary;
        public string LatestNewsSummary
        {
            get => latestNewsSummary;
            set => SetValue(ref latestNewsSummary, value);
        }

        private string latestNewsGameName;
        public string LatestNewsGameName
        {
            get => latestNewsGameName;
            set => SetValue(ref latestNewsGameName, value);
        }

        private string latestNewsLocalImagePath;
        public string LatestNewsLocalImagePath
        {
            get => latestNewsLocalImagePath;
            set => SetValue(ref latestNewsLocalImagePath, value);
        }

        private string latestNewsLocalImagePathA;
        public string LatestNewsLocalImagePathA
        {
            get => latestNewsLocalImagePathA;
            set => SetValue(ref latestNewsLocalImagePathA, value);
        }

        private string latestNewsLocalImagePathB;
        public string LatestNewsLocalImagePathB
        {
            get => latestNewsLocalImagePathB;
            set => SetValue(ref latestNewsLocalImagePathB, value);
        }

        private bool latestNewsShowLayerB;
        public bool LatestNewsShowLayerB
        {
            get => latestNewsShowLayerB;
            set => SetValue(ref latestNewsShowLayerB, value);
        }

        // Snapshot for Welcome Hub - From your library card
        private string libraryNewsTitle;
        public string LibraryNewsTitle
        {
            get => libraryNewsTitle;
            set => SetValue(ref libraryNewsTitle, value);
        }

        private string libraryNewsGameName;
        public string LibraryNewsGameName
        {
            get => libraryNewsGameName;
            set => SetValue(ref libraryNewsGameName, value);
        }

        private string libraryNewsDateString;
        public string LibraryNewsDateString
        {
            get => libraryNewsDateString;
            set => SetValue(ref libraryNewsDateString, value);
        }

        private string libraryNewsSummary;
        public string LibraryNewsSummary
        {
            get => libraryNewsSummary;
            set => SetValue(ref libraryNewsSummary, value);
        }

        private string libraryNewsBadgeText;
        public string LibraryNewsBadgeText
        {
            get => libraryNewsBadgeText;
            set => SetValue(ref libraryNewsBadgeText, value);
        }

        private string libraryNewsImagePath;
        public string LibraryNewsImagePath
        {
            get => libraryNewsImagePath;
            set => SetValue(ref libraryNewsImagePath, value);
        }

        private string libraryNewsImagePathA;
        public string LibraryNewsImagePathA
        {
            get => libraryNewsImagePathA;
            set => SetValue(ref libraryNewsImagePathA, value);
        }

        private string libraryNewsImagePathB;
        public string LibraryNewsImagePathB
        {
            get => libraryNewsImagePathB;
            set => SetValue(ref libraryNewsImagePathB, value);
        }

        private bool libraryNewsShowLayerB;
        public bool LibraryNewsShowLayerB
        {
            get => libraryNewsShowLayerB;
            set => SetValue(ref libraryNewsShowLayerB, value);
        }



        // Playnite News 

        // Last 10 from GitHub
        public ObservableCollection<SteamGlobalNewsItem> PlayniteNews { get; set; }
            = new ObservableCollection<SteamGlobalNewsItem>();

        // Last scanned date
        public DateTime? PlayniteNewsLastRefreshUtc { get; set; }

        // Key to the latest news 
        private string playniteNewsLastKey;
        public string PlayniteNewsLastKey
        {
            get => playniteNewsLastKey;
            set => SetValue(ref playniteNewsLastKey, value);
        }

        // badge “NEW” for Playnite news
        private bool playniteNewsHasNew;
        public bool PlayniteNewsHasNew
        {
            get => playniteNewsHasNew;
            set => SetValue(ref playniteNewsHasNew, value);
        }

        // Steam Store localization / pricing

        private string steamStoreLanguage = "english";
        public string SteamStoreLanguage
        {
            get => steamStoreLanguage;
            set => SetValue(ref steamStoreLanguage, value);
        }

        private string steamStoreRegion = "US";
        public string SteamStoreRegion
        {
            get => steamStoreRegion;
            set => SetValue(ref steamStoreRegion, value);
        }

        private bool steamStoreEnabled = true;
        public bool SteamStoreEnabled
        {
            get => steamStoreEnabled;
            set
            {
                SetValue(ref steamStoreEnabled, value);
                NotifyHubForYouStorePageStateChanged();
            }
        }



        // Steam Friends integration (ported from Steam Friends Fullscreen, without Windows notifications)
        private bool steamFriendsEnabled = true;
        public bool SteamFriendsEnabled
        {
            get => steamFriendsEnabled;
            set
            {
                SetValue(ref steamFriendsEnabled, value);
                NotifySteamFriendsConfigurationPropertiesChanged();
            }
        }

        private string steamApiKey = string.Empty;
        public string SteamApiKey
        {
            get => steamApiKey;
            set
            {
                SetValue(ref steamApiKey, value ?? string.Empty);
                NotifySteamFriendsConfigurationPropertiesChanged();
            }
        }

        private string steamId64 = string.Empty;
        public string SteamId64
        {
            get => steamId64;
            set
            {
                SetValue(ref steamId64, value ?? string.Empty);
                NotifySteamFriendsConfigurationPropertiesChanged();
            }
        }


        private string steamAccountSteamId64 = string.Empty;
        public string SteamAccountSteamId64
        {
            get => steamAccountSteamId64;
            set
            {
                SetValue(ref steamAccountSteamId64, value ?? string.Empty);
                NotifySteamFriendsConfigurationPropertiesChanged();
            }
        }


        [DontSerialize]
        public bool SteamFriendsHasSteamApiKey => !string.IsNullOrWhiteSpace(SteamApiKey);

        [DontSerialize]
        public bool SteamFriendsHasSteamId =>
            !string.IsNullOrWhiteSpace(SteamAccountSteamId64) ||
            !string.IsNullOrWhiteSpace(SteamId64);

        [DontSerialize]
        public bool SteamFriendsHasRequiredConfig => SteamFriendsHasSteamApiKey && SteamFriendsHasSteamId;

        [DontSerialize]
        public bool SteamFriendsFeatureDisabled => SteamFriendsEnabled != true;

        [DontSerialize]
        public bool SteamFriendsMissingConfiguration => SteamFriendsEnabled == true && !SteamFriendsHasRequiredConfig;

        [DontSerialize]
        public bool SteamFriendsReady => SteamFriendsEnabled == true && SteamFriendsHasRequiredConfig;

        [DontSerialize]
        public Visibility SteamFriendsStatusVisibility => SteamFriendsEnabled ? Visibility.Visible : Visibility.Collapsed;

        [DontSerialize]
        public Visibility SteamFriendsSetupMessageVisibility => SteamFriendsMissingConfiguration ? Visibility.Visible : Visibility.Collapsed;

        [DontSerialize]
        public Visibility SteamFriendsRuntimeVisibility => SteamFriendsReady ? Visibility.Visible : Visibility.Collapsed;

        [DontSerialize]
        public Visibility SteamFriendsOpenSteamButtonVisibility => SteamFriendsReady && !IsSteamRunning
            ? Visibility.Visible
            : Visibility.Collapsed;

        [DontSerialize]
        public Visibility SteamFriendsChangeStatusVisibility => SteamFriendsReady && IsSteamRunning
            ? Visibility.Visible
            : Visibility.Collapsed;

        [DontSerialize]
        public string SteamFriendsConfigurationState
        {
            get
            {
                if (SteamFriendsEnabled != true)
                {
                    return "disabled";
                }

                return SteamFriendsHasRequiredConfig ? "ready" : "missingconfig";
            }
        }

        [DontSerialize]
        public string SteamFriendsSetupTitle
        {
            get
            {
                if (SteamFriendsEnabled != true)
                {
                    return "Steam Friends is disabled";
                }

                return SteamFriendsHasRequiredConfig
                    ? "Steam Friends is ready"
                    : "Steam Friends setup required";
            }
        }

        [DontSerialize]
        public string SteamFriendsSetupMessage
        {
            get
            {
                if (SteamFriendsEnabled != true)
                {
                    return "Steam Friends is disabled in Aniki Helper settings.";
                }

                if (!SteamFriendsHasSteamApiKey && !SteamFriendsHasSteamId)
                {
                    return "Steam Friends is enabled, but your Steam API key and SteamID64 / profile URL are missing.";
                }

                if (!SteamFriendsHasSteamApiKey)
                {
                    return "Steam Friends is enabled, but your Steam API key is missing.";
                }

                if (!SteamFriendsHasSteamId)
                {
                    return "Steam Friends is enabled, but no SteamID64 or Steam profile URL is configured.";
                }

                return string.Empty;
            }
        }

        [DontSerialize]
        public string SteamFriendsStatusButtonText
        {
            get
            {
                if (SteamFriendsEnabled != true)
                {
                    return "Steam Friends disabled";
                }

                if (!SteamFriendsHasRequiredConfig)
                {
                    return "Configure Steam Friends";
                }

                if (IsSteamLaunching)
                {
                    return string.IsNullOrWhiteSpace(SteamLaunchMessage) ? "Launching Steam..." : SteamLaunchMessage;
                }

                return IsSteamRunning ? SelfStateLoc : "Open Steam";
            }
        }

        private void NotifySteamFriendsConfigurationPropertiesChanged()
        {
            OnPropertyChanged(nameof(SteamFriendsHasSteamApiKey));
            OnPropertyChanged(nameof(SteamFriendsHasSteamId));
            OnPropertyChanged(nameof(SteamFriendsHasRequiredConfig));
            OnPropertyChanged(nameof(SteamFriendsFeatureDisabled));
            OnPropertyChanged(nameof(SteamFriendsMissingConfiguration));
            OnPropertyChanged(nameof(SteamFriendsReady));
            OnPropertyChanged(nameof(SteamFriendsStatusVisibility));
            OnPropertyChanged(nameof(SteamFriendsSetupMessageVisibility));
            OnPropertyChanged(nameof(SteamFriendsRuntimeVisibility));
            OnPropertyChanged(nameof(SteamFriendsOpenSteamButtonVisibility));
            OnPropertyChanged(nameof(SteamFriendsChangeStatusVisibility));
            OnPropertyChanged(nameof(SteamFriendsConfigurationState));
            OnPropertyChanged(nameof(SteamFriendsSetupTitle));
            OnPropertyChanged(nameof(SteamFriendsSetupMessage));
            OnPropertyChanged(nameof(SteamFriendsStatusButtonText));
        }

        private string steamAccountProfileUrl = string.Empty;
        public string SteamAccountProfileUrl
        {
            get => steamAccountProfileUrl;
            set => SetValue(ref steamAccountProfileUrl, value ?? string.Empty);
        }

        [DontSerialize]
        private bool steamAccountConnected;
        [DontSerialize]
        public bool SteamAccountConnected
        {
            get => steamAccountConnected;
            set => SetValue(ref steamAccountConnected, value);
        }

        [DontSerialize]
        private bool steamAccountBusy;
        [DontSerialize]
        public bool SteamAccountBusy
        {
            get => steamAccountBusy;
            set => SetValue(ref steamAccountBusy, value);
        }

        [DontSerialize]
        private string steamAccountStatus = "Not connected";
        [DontSerialize]
        public string SteamAccountStatus
        {
            get => steamAccountStatus;
            set => SetValue(ref steamAccountStatus, value ?? string.Empty);
        }

        private bool showOffline = false;
        public bool ShowOffline
        {
            get => showOffline;
            set => SetValue(ref showOffline, value);
        }

        private bool notifyOnGameStart = true;
        public bool NotifyOnGameStart
        {
            get => notifyOnGameStart;
            set => SetValue(ref notifyOnGameStart, value);
        }

        private bool notifyOnConnect = false;
        public bool NotifyOnConnect
        {
            get => notifyOnConnect;
            set => SetValue(ref notifyOnConnect, value);
        }

        // Steam Friends runtime state exposed to the theme
        [DontSerialize]
        public ObservableCollection<FriendPresenceDto> Friends { get; private set; } = new ObservableCollection<FriendPresenceDto>();

        [DontSerialize]
        public ObservableCollection<FriendActivityHubItem> FriendActivityHubItems { get; private set; } = new ObservableCollection<FriendActivityHubItem>();


        [DontSerialize]
        public ObservableCollection<SteamFriendPlayedGameDto> SteamFriendsWhoPlayedCurrentGame { get; private set; } = new ObservableCollection<SteamFriendPlayedGameDto>();

        [DontSerialize]
        public ObservableCollection<FriendPresenceDto> SteamFriendsPlayingCurrentGame { get; private set; } = new ObservableCollection<FriendPresenceDto>();

        private bool steamFriendsWhoPlayedAvailable;
        [DontSerialize]
        public bool SteamFriendsWhoPlayedAvailable
        {
            get => steamFriendsWhoPlayedAvailable;
            set => SetValue(ref steamFriendsWhoPlayedAvailable, value);
        }

        private bool steamFriendsWhoPlayedLoading;
        [DontSerialize]
        public bool SteamFriendsWhoPlayedLoading
        {
            get => steamFriendsWhoPlayedLoading;
            set => SetValue(ref steamFriendsWhoPlayedLoading, value);
        }

        private int steamFriendsWhoPlayedCount;
        [DontSerialize]
        public int SteamFriendsWhoPlayedCount
        {
            get => steamFriendsWhoPlayedCount;
            set => SetValue(ref steamFriendsWhoPlayedCount, value);
        }

        private string steamFriendsWhoPlayedSummary;
        [DontSerialize]
        public string SteamFriendsWhoPlayedSummary
        {
            get => steamFriendsWhoPlayedSummary;
            set => SetValue(ref steamFriendsWhoPlayedSummary, value ?? string.Empty);
        }

        private string steamFriendsWhoPlayedError;
        [DontSerialize]
        public string SteamFriendsWhoPlayedError
        {
            get => steamFriendsWhoPlayedError;
            set => SetValue(ref steamFriendsWhoPlayedError, value ?? string.Empty);
        }

        private bool steamFriendsPlayedGamesCacheRefreshing;
        [DontSerialize]
        public bool SteamFriendsPlayedGamesCacheRefreshing
        {
            get => steamFriendsPlayedGamesCacheRefreshing;
            set => SetValue(ref steamFriendsPlayedGamesCacheRefreshing, value);
        }

        private bool steamFriendsPlayedGamesCacheStale = true;
        [DontSerialize]
        public bool SteamFriendsPlayedGamesCacheStale
        {
            get => steamFriendsPlayedGamesCacheStale;
            set => SetValue(ref steamFriendsPlayedGamesCacheStale, value);
        }

        private string steamFriendsPlayedGamesCacheStatus;
        [DontSerialize]
        public string SteamFriendsPlayedGamesCacheStatus
        {
            get => steamFriendsPlayedGamesCacheStatus;
            set => SetValue(ref steamFriendsPlayedGamesCacheStatus, value ?? string.Empty);
        }

        private bool steamFriendsPlayingCurrentGameAvailable;
        [DontSerialize]
        public bool SteamFriendsPlayingCurrentGameAvailable
        {
            get => steamFriendsPlayingCurrentGameAvailable;
            set => SetValue(ref steamFriendsPlayingCurrentGameAvailable, value);
        }

        private int steamFriendsPlayingCurrentGameCount;
        [DontSerialize]
        public int SteamFriendsPlayingCurrentGameCount
        {
            get => steamFriendsPlayingCurrentGameCount;
            set => SetValue(ref steamFriendsPlayingCurrentGameCount, value);
        }

        private string steamFriendsPlayingCurrentGameSummary;
        [DontSerialize]
        public string SteamFriendsPlayingCurrentGameSummary
        {
            get => steamFriendsPlayingCurrentGameSummary;
            set => SetValue(ref steamFriendsPlayingCurrentGameSummary, value ?? string.Empty);
        }

        private bool showHubFriendActivityPage;
        [DontSerialize]
        public bool ShowHubFriendActivityPage
        {
            get => showHubFriendActivityPage;
            set => SetValue(ref showHubFriendActivityPage, value);
        }

        public void EnsureFriendActivityHubRuntimeCollections()
        {
            if (FriendActivityHubItems == null)
            {
                FriendActivityHubItems = new ObservableCollection<FriendActivityHubItem>();
            }
        }

        public void EnsureSteamFriendsRuntimeCollections()
        {
            if (Friends == null)
            {
                Friends = new ObservableCollection<FriendPresenceDto>();
            }

            if (SteamFriendsWhoPlayedCurrentGame == null)
            {
                SteamFriendsWhoPlayedCurrentGame = new ObservableCollection<SteamFriendPlayedGameDto>();
            }

            if (SteamFriendsPlayingCurrentGame == null)
            {
                SteamFriendsPlayingCurrentGame = new ObservableCollection<FriendPresenceDto>();
            }

            EnsureFriendActivityHubRuntimeCollections();
        }

        private bool toastIsVisible;
        [DontSerialize]
        public bool ToastIsVisible
        {
            get => toastIsVisible;
            set => SetValue(ref toastIsVisible, value);
        }

        private bool toastFlip;
        [DontSerialize]
        public bool ToastFlip
        {
            get => toastFlip;
            set => SetValue(ref toastFlip, value);
        }

        private string toastMessage;
        [DontSerialize]
        public string ToastMessage
        {
            get => toastMessage;
            set => SetValue(ref toastMessage, value);
        }

        private string toastAvatar;
        [DontSerialize]
        public string ToastAvatar
        {
            get => toastAvatar;
            set => SetValue(ref toastAvatar, value);
        }

        private long toastToken;
        [DontSerialize]
        public long ToastToken
        {
            get => toastToken;
            set => SetValue(ref toastToken, value);
        }

        private int onlineCount;
        [DontSerialize]
        public int OnlineCount
        {
            get => onlineCount;
            set => SetValue(ref onlineCount, value);
        }

        private int inGameCount;
        [DontSerialize]
        public int InGameCount
        {
            get => inGameCount;
            set => SetValue(ref inGameCount, value);
        }

        private int offlineCount;
        [DontSerialize]
        public int OfflineCount
        {
            get => offlineCount;
            set => SetValue(ref offlineCount, value);
        }

        private DateTime lastUpdateUtc = DateTime.MinValue;
        [DontSerialize]
        public DateTime LastUpdateUtc
        {
            get => lastUpdateUtc;
            set => SetValue(ref lastUpdateUtc, value);
        }

        private string lastError;
        [DontSerialize]
        public string LastError
        {
            get => lastError;
            set => SetValue(ref lastError, value);
        }

        [DontSerialize]
        public bool IsStale
        {
            get
            {
                if (LastUpdateUtc == DateTime.MinValue)
                {
                    return true;
                }

                return (DateTime.UtcNow - LastUpdateUtc) > TimeSpan.FromSeconds(180);
            }
        }

        private bool isSteamRunning;
        [DontSerialize]
        public bool IsSteamRunning
        {
            get => isSteamRunning;
            set
            {
                SetValue(ref isSteamRunning, value);
                NotifySteamFriendsConfigurationPropertiesChanged();
            }
        }

        private bool isSteamLaunching;
        [DontSerialize]
        public bool IsSteamLaunching
        {
            get => isSteamLaunching;
            set
            {
                SetValue(ref isSteamLaunching, value);
                OnPropertyChanged(nameof(SteamFriendsStatusButtonText));
            }
        }

        private string steamLaunchMessage;
        [DontSerialize]
        public string SteamLaunchMessage
        {
            get => steamLaunchMessage;
            set
            {
                SetValue(ref steamLaunchMessage, value);
                OnPropertyChanged(nameof(SteamFriendsStatusButtonText));
            }
        }

        private string selfName;
        [DontSerialize]
        public string SelfName
        {
            get => selfName;
            set => SetValue(ref selfName, value);
        }

        private string selfState = "offline";
        [DontSerialize]
        public string SelfState
        {
            get => selfState;
            set => SetValue(ref selfState, value);
        }

        private string selfGame;
        [DontSerialize]
        public string SelfGame
        {
            get => selfGame;
            set => SetValue(ref selfGame, value);
        }

        private string selfAvatar;
        [DontSerialize]
        public string SelfAvatar
        {
            get => selfAvatar;
            set => SetValue(ref selfAvatar, value);
        }

        private string selfStateLoc = "Offline";

        [DontSerialize]
        public string SelfStateLoc
        {
            get => selfStateLoc;
            set
            {
                SetValue(ref selfStateLoc, value);
                OnPropertyChanged(nameof(SteamFriendsStatusButtonText));
            }
        }

        private FriendProfileDto selectedFriendProfile;
        [DontSerialize]
        public FriendProfileDto SelectedFriendProfile
        {
            get => selectedFriendProfile;
            set => SetValue(ref selectedFriendProfile, value);
        }

        private bool isFriendProfileLoading;
        [DontSerialize]
        public bool IsFriendProfileLoading
        {
            get => isFriendProfileLoading;
            set => SetValue(ref isFriendProfileLoading, value);
        }

        private string selectedFriendSteamId;
        [DontSerialize]
        public string SelectedFriendSteamId
        {
            get => selectedFriendSteamId;
            set => SetValue(ref selectedFriendSteamId, value);
        }

        private string friendProfileError;
        [DontSerialize]
        public string FriendProfileError
        {
            get => friendProfileError;
            set => SetValue(ref friendProfileError, value);
        }

        private bool isFriendProfileOpen;
        [DontSerialize]
        public bool IsFriendProfileOpen
        {
            get => isFriendProfileOpen;
            set => SetValue(ref isFriendProfileOpen, value);
        }

        private bool isFriendActionsMenuOpen;
        [DontSerialize]
        public bool IsFriendActionsMenuOpen
        {
            get => isFriendActionsMenuOpen;
            set => SetValue(ref isFriendActionsMenuOpen, value);
        }

        private FriendPresenceDto selectedFriendForActions;
        [DontSerialize]
        public FriendPresenceDto SelectedFriendForActions
        {
            get => selectedFriendForActions;
            set => SetValue(ref selectedFriendForActions, value);
        }

        [DontSerialize] public ICommand SetStatusOnlineCommand { get; set; }
        [DontSerialize] public ICommand SetStatusAwayCommand { get; set; }
        [DontSerialize] public ICommand SetStatusBusyCommand { get; set; }
        [DontSerialize] public ICommand SetStatusInvisibleCommand { get; set; }
        [DontSerialize] public ICommand SetStatusOfflineCommand { get; set; }
        [DontSerialize] public ICommand OpenSteamCommand { get; set; }
        [DontSerialize] public ICommand ConnectSteamAccountCommand { get; set; }
        [DontSerialize] public ICommand CheckSteamAccountCommand { get; set; }
        [DontSerialize] public ICommand DisconnectSteamAccountCommand { get; set; }
        [DontSerialize] public ICommand OpenSelfStatusWindowCommand { get; set; }
        [DontSerialize] public ICommand OpenFriendProfileCommand { get; set; }
        [DontSerialize] public ICommand OpenFriendProfileWindowCommand { get; set; }
        [DontSerialize] public ICommand RefreshSelectedFriendProfileCommand { get; set; }
        [DontSerialize] public ICommand ClearFriendProfileCommand { get; set; }
        [DontSerialize] public ICommand OpenFriendChatCommand { get; set; }
        [DontSerialize] public ICommand OpenFriendActionsWindowCommand { get; set; }
        [DontSerialize] public ICommand OpenFriendActionsMenuCommand { get; set; }
        [DontSerialize] public ICommand CloseFriendActionsMenuCommand { get; set; }
        [DontSerialize] public ICommand OpenSelectedFriendProfileCommand { get; set; }
        [DontSerialize] public ICommand OpenSelectedFriendChatCommand { get; set; }
        [DontSerialize] public ICommand RefreshFriendsPlayedGamesCacheCommand { get; set; }

        private bool steamStoreLoading;
        public bool SteamStoreLoading
        {
            get => steamStoreLoading;
            set => SetValue(ref steamStoreLoading, value);
        }

        private bool steamStoreAvailable;
        public bool SteamStoreAvailable
        {
            get => steamStoreAvailable;
            set => SetValue(ref steamStoreAvailable, value);
        }

        private string steamStoreError;
        public string SteamStoreError
        {
            get => steamStoreError;
            set => SetValue(ref steamStoreError, value);
        }

        private int steamStoreLoadingProgress;
        public int SteamStoreLoadingProgress
        {
            get => steamStoreLoadingProgress;
            set => SetValue(ref steamStoreLoadingProgress, value);
        }

        private ObservableCollection<SteamStoreItem> steamStoreDeals = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreNewReleases = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreTopSellers = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreUpcoming = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreWishlisted = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreMyWishlist = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreRecommended = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreRecommendedHub = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<HubLibraryRecommendedGameItem> hubLibraryRecommendedGames = new ObservableCollection<HubLibraryRecommendedGameItem>();

        private string steamStoreSelectedSection = "Deals";
        public string SteamStoreSelectedSection
        {
            get => steamStoreSelectedSection;
            set => SetValue(ref steamStoreSelectedSection, value);
        }

        private string steamStoreSelectedSectionTitle = "Deals";
        public string SteamStoreSelectedSectionTitle
        {
            get => steamStoreSelectedSectionTitle;
            set => SetValue(ref steamStoreSelectedSectionTitle, value);
        }

        [DontSerialize]
        private bool steamStoreSelectedSectionRequiresSteamAuth;
        [DontSerialize]
        public bool SteamStoreSelectedSectionRequiresSteamAuth
        {
            get => steamStoreSelectedSectionRequiresSteamAuth;
            set => SetValue(ref steamStoreSelectedSectionRequiresSteamAuth, value);
        }

        private ObservableCollection<SteamStoreItem> steamStoreCurrentItems = new ObservableCollection<SteamStoreItem>();

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreCurrentItems
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreCurrentItems;
            }
            set => SetValue(ref steamStoreCurrentItems, value);
        }

        private ObservableCollection<SteamStoreItem> steamStoreCurrentListItems = new ObservableCollection<SteamStoreItem>();

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreCurrentListItems
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreCurrentListItems;
            }
            set => SetValue(ref steamStoreCurrentListItems, value);
        }

        private SteamStoreItem steamStoreHeroItem;

        [DontSerialize]
        public SteamStoreItem SteamStoreHeroItem
        {
            get => steamStoreHeroItem;
            set => SetValue(ref steamStoreHeroItem, value);
        }

        private string steamStoreHeroName = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroName
        {
            get => steamStoreHeroName;
            set => SetValue(ref steamStoreHeroName, value);
        }

        private string steamStoreHeroDescription = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroDescription
        {
            get => steamStoreHeroDescription;
            set => SetValue(ref steamStoreHeroDescription, value);
        }

        private string steamStoreHeroImage = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroImage
        {
            get => steamStoreHeroImage;
            set => SetValue(ref steamStoreHeroImage, value);
        }

        private string steamStoreHeroBackgroundImage = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroBackgroundImage
        {
            get => steamStoreHeroBackgroundImage;
            set => SetValue(ref steamStoreHeroBackgroundImage, value);
        }

        private string steamStoreHeroPrice = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroPrice
        {
            get => steamStoreHeroPrice;
            set => SetValue(ref steamStoreHeroPrice, value);
        }

        private string steamStoreHeroOriginalPrice = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroOriginalPrice
        {
            get => steamStoreHeroOriginalPrice;
            set => SetValue(ref steamStoreHeroOriginalPrice, value);
        }

        private string steamStoreHeroDiscount = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroDiscount
        {
            get => steamStoreHeroDiscount;
            set => SetValue(ref steamStoreHeroDiscount, value);
        }

        private string steamStoreHeroReleaseDate = string.Empty;

        [DontSerialize]
        public string SteamStoreHeroReleaseDate
        {
            get => steamStoreHeroReleaseDate;
            set => SetValue(ref steamStoreHeroReleaseDate, value);
        }

        private void RequestSteamStoreLoad()
        {
            if (plugin?.Settings?.SteamStoreEnabled != true)
            {
                return;
            }

            _ = plugin?.OnSteamStoreViewOpenedAsync();
        }

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreDeals
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreDeals;
            }
            set => SetValue(ref steamStoreDeals, value);
        }

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreNewReleases
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreNewReleases;
            }
            set => SetValue(ref steamStoreNewReleases, value);
        }

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreTopSellers
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreTopSellers;
            }
            set => SetValue(ref steamStoreTopSellers, value);
        }


        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreUpcoming
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreUpcoming;
            }
            set => SetValue(ref steamStoreUpcoming, value);
        }

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreWishlisted
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreWishlisted;
            }
            set => SetValue(ref steamStoreWishlisted, value);
        }

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreMyWishlist
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreMyWishlist;
            }
            set => SetValue(ref steamStoreMyWishlist, value);
        }

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreRecommended
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreRecommended;
            }
            set => SetValue(ref steamStoreRecommended, value);
        }

        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreRecommendedHub
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreRecommendedHub;
            }
            set
            {
                SetValue(ref steamStoreRecommendedHub, value);
                NotifyHubForYouStorePageStateChanged();
            }
        }

        [DontSerialize]
        public bool ShowHubForYouStorePage
        {
            get => SteamStoreEnabled && steamStoreRecommendedHub != null && steamStoreRecommendedHub.Count > 0;
        }

        public void NotifyHubForYouStorePageStateChanged()
        {
            OnPropertyChanged(nameof(SteamStoreRecommendedHub));
            OnPropertyChanged(nameof(ShowHubForYouStorePage));
        }

        [DontSerialize]
        public ObservableCollection<HubLibraryRecommendedGameItem> HubLibraryRecommendedGames
        {
            get => hubLibraryRecommendedGames;
            set => SetValue(ref hubLibraryRecommendedGames, value);
        }



        // Enable or disable scanning
        private bool newsScanEnabled = true;
        public bool NewsScanEnabled
        {
            get => newsScanEnabled;
            set
            {
                bool changed = newsScanEnabled != value;

                SetValue(ref newsScanEnabled, value);

                if (changed && !value)
                {
                    SteamGlobalNewsALastRefreshUtc = null;
                    SteamGlobalNewsBLastRefreshUtc = null;
                    LastNewsScanUtc = DateTime.MinValue;
                }
            }
        }

        public DateTime LastNewsScanUtc { get; set; } = DateTime.MinValue;

        // Global toast notification
        private string globalToastMessage;
        public string GlobalToastMessage
        {
            get => globalToastMessage;
            set => SetValue(ref globalToastMessage, value);
        }

        private string globalToastType;
        public string GlobalToastType
        {
            get => globalToastType;
            set => SetValue(ref globalToastType, value);
        }

        private string globalToastStamp;
        public string GlobalToastStamp
        {
            get => globalToastStamp;
            set => SetValue(ref globalToastStamp, value);
        }

        private bool globalToastFlip;
        public bool GlobalToastFlip
        {
            get => globalToastFlip;
            set => SetValue(ref globalToastFlip, value);
        }


        // Listes exposées (Exhibited lists)

        public ObservableCollection<TopPlayedItem> TopPlayed { get; } = new ObservableCollection<TopPlayedItem>();
        public ObservableCollection<CompletionStatItem> CompletionStates { get; } = new ObservableCollection<CompletionStatItem>();
        public ObservableCollection<ProviderStatItem> GameProviders { get; } = new ObservableCollection<ProviderStatItem>();
        public ObservableCollection<QuickItem> RecentPlayed { get; } = new ObservableCollection<QuickItem>();
        public ObservableCollection<QuickItem> RecentAdded { get; } = new ObservableCollection<QuickItem>();
        public ObservableCollection<QuickItem> NeverPlayed { get; } = new ObservableCollection<QuickItem>();

        // Recent Trophy (Top 3)
        public ObservableCollection<RecentAchievementItem> RecentAchievements { get; } = new ObservableCollection<RecentAchievementItem>();

        // Rare Trophy (Top 3)
        public ObservableCollection<RareAchievementItem> RareTop { get; } = new ObservableCollection<RareAchievementItem>();

        // Latest Steam game updates (Top 10)
        public ObservableCollection<SteamRecentUpdateItem> SteamRecentUpdates { get; } = new ObservableCollection<SteamRecentUpdateItem>();
        public ObservableCollection<SteamGameNewsItem> SteamGameNews { get; } = new ObservableCollection<SteamGameNewsItem>();

        // Last notifications generated by Aniki Helper
        private ObservableCollection<AnikiNotificationItem> lastNotifications = new ObservableCollection<AnikiNotificationItem>();
        public ObservableCollection<AnikiNotificationItem> LastNotifications
        {
            get => lastNotifications;
            set => SetValue(ref lastNotifications, value ?? new ObservableCollection<AnikiNotificationItem>());
        }

        [DontSerialize]
        private ObservableCollection<AnikiOverlayNeverSuspendGameItem> inGameOverlayNeverSuspendGameItems
            = new ObservableCollection<AnikiOverlayNeverSuspendGameItem>();

        [DontSerialize]
        public ObservableCollection<AnikiOverlayNeverSuspendGameItem> InGameOverlayNeverSuspendGameItems
        {
            get => inGameOverlayNeverSuspendGameItems;
            private set => SetValue(ref inGameOverlayNeverSuspendGameItems, value ?? new ObservableCollection<AnikiOverlayNeverSuspendGameItem>());
        }

        // Watcher SuccessStory
        private FileSystemWatcher achievementsWatcher;
        private Timer debounceTimer;

        // Cache SuccessStory root (évite de rescanner le disque trop souvent)
        [DontSerialize]
        private string cachedSsRoot;

        [DontSerialize]
        private DateTime cachedSsRootCheckedUtc = DateTime.MinValue;


        #region Options (bindables)
        public bool EnableDebugLogs
        {
            get => enableDebugLogs;
            set
            {
                var changed = enableDebugLogs != value;
                SetValue(ref enableDebugLogs, value);

                if (changed && plugin != null)
                {
                    plugin.SavePluginSettings(this);
                }
            }
        }
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

        // Enables/disables DynamicAuto pre-caching
        public bool DynamicAutoPrecacheUserEnabled
        {
            get => dynamicAutoPrecacheUserEnabled;
            set => SetValue(ref dynamicAutoPrecacheUserEnabled, value);
        }

        // Enables or disables the retrieval of the number of Steam players
        private bool steamPlayerCountEnabled = false;
        public bool SteamPlayerCountEnabled
        {
            get => steamPlayerCountEnabled;
            set => SetValue(ref steamPlayerCountEnabled, value);
        }

        // Enables or disables scanning for Games updates
        private bool steamUpdatesScanEnabled = true;
        public bool SteamUpdatesScanEnabled
        {
            get => steamUpdatesScanEnabled;
            set => SetValue(ref steamUpdatesScanEnabled, value);
        }

        // Enables or disables video/splash
        private bool startupIntroVideoEnabled = true;
        public bool StartupIntroVideoEnabled
        {
            get => startupIntroVideoEnabled;
            set => SetValue(ref startupIntroVideoEnabled, value);
        }

        private bool gameLaunchSplashEnabled = true;
        public bool GameLaunchSplashEnabled
        {
            get => gameLaunchSplashEnabled;
            set => SetValue(ref gameLaunchSplashEnabled, value);
        }

        private bool gameLaunchSplashShowLogo = true;
        public bool GameLaunchSplashShowLogo
        {
            get => gameLaunchSplashShowLogo;
            set => SetValue(ref gameLaunchSplashShowLogo, value);
        }

        private bool gameLaunchSplashPauseUniPlaySong = true;
        public bool GameLaunchSplashPauseUniPlaySong
        {
            get => gameLaunchSplashPauseUniPlaySong;
            set => SetValue(ref gameLaunchSplashPauseUniPlaySong, value);
        }

        private bool gameLaunchSplashVideoSoundEnabled = true;
        public bool GameLaunchSplashVideoSoundEnabled
        {
            get => gameLaunchSplashVideoSoundEnabled;
            set => SetValue(ref gameLaunchSplashVideoSoundEnabled, value);
        }

        private SplashScreenVideoEndBehavior gameLaunchSplashVideoEndBehavior = SplashScreenVideoEndBehavior.ShowGameBackground;
        public SplashScreenVideoEndBehavior GameLaunchSplashVideoEndBehavior
        {
            get => gameLaunchSplashVideoEndBehavior;
            set => SetValue(ref gameLaunchSplashVideoEndBehavior, value);
        }

        private double gameLaunchSplashVideoVolume = 0.5;
        public double GameLaunchSplashVideoVolume
        {
            get => gameLaunchSplashVideoVolume;
            set => SetValue(ref gameLaunchSplashVideoVolume, Math.Max(0, Math.Min(1, value)));
        }

        private SplashScreenLogoPosition gameLaunchSplashLogoPosition = SplashScreenLogoPosition.LeftCenter;
        public SplashScreenLogoPosition GameLaunchSplashLogoPosition
        {
            get => gameLaunchSplashLogoPosition;
            set => SetValue(ref gameLaunchSplashLogoPosition, value);
        }

        private SplashScreenSelectionMode gameLaunchSplashSelectionMode = SplashScreenSelectionMode.Automatic;
        public SplashScreenSelectionMode GameLaunchSplashSelectionMode
        {
            get => gameLaunchSplashSelectionMode;
            set => SetValue(ref gameLaunchSplashSelectionMode, value);
        }

        [DontSerialize]
        private bool isRefreshingGameLaunchSplashCustomPriorityOptions;

        private SplashScreenPriorityTarget gameLaunchSplashCustomPriority1 = SplashScreenPriorityTarget.GameCustom;
        public SplashScreenPriorityTarget GameLaunchSplashCustomPriority1
        {
            get => gameLaunchSplashCustomPriority1;
            set
            {
                if (gameLaunchSplashCustomPriority1 == value)
                {
                    return;
                }

                SetValue(ref gameLaunchSplashCustomPriority1, value);
                RefreshGameLaunchSplashCustomPriorityOptions();
            }
        }

        private SplashScreenPriorityTarget gameLaunchSplashCustomPriority2 = SplashScreenPriorityTarget.GameBackground;
        public SplashScreenPriorityTarget GameLaunchSplashCustomPriority2
        {
            get => gameLaunchSplashCustomPriority2;
            set
            {
                if (gameLaunchSplashCustomPriority2 == value)
                {
                    return;
                }

                SetValue(ref gameLaunchSplashCustomPriority2, value);
                RefreshGameLaunchSplashCustomPriorityOptions();
            }
        }

        private SplashScreenPriorityTarget gameLaunchSplashCustomPriority3 = SplashScreenPriorityTarget.Platform;
        public SplashScreenPriorityTarget GameLaunchSplashCustomPriority3
        {
            get => gameLaunchSplashCustomPriority3;
            set
            {
                if (gameLaunchSplashCustomPriority3 == value)
                {
                    return;
                }

                SetValue(ref gameLaunchSplashCustomPriority3, value);
                RefreshGameLaunchSplashCustomPriorityOptions();
            }
        }

        private SplashScreenPriorityTarget gameLaunchSplashCustomPriority4 = SplashScreenPriorityTarget.Source;
        public SplashScreenPriorityTarget GameLaunchSplashCustomPriority4
        {
            get => gameLaunchSplashCustomPriority4;
            set
            {
                if (gameLaunchSplashCustomPriority4 == value)
                {
                    return;
                }

                SetValue(ref gameLaunchSplashCustomPriority4, value);
                RefreshGameLaunchSplashCustomPriorityOptions();
            }
        }

        private SplashScreenPriorityTarget gameLaunchSplashCustomPriority5 = SplashScreenPriorityTarget.Global;
        public SplashScreenPriorityTarget GameLaunchSplashCustomPriority5
        {
            get => gameLaunchSplashCustomPriority5;
            set
            {
                if (gameLaunchSplashCustomPriority5 == value)
                {
                    return;
                }

                SetValue(ref gameLaunchSplashCustomPriority5, value);
                RefreshGameLaunchSplashCustomPriorityOptions();
            }
        }

        [DontSerialize]
        public ObservableCollection<SplashScreenPriorityOption> GameLaunchSplashCustomPriority1Options { get; }
            = new ObservableCollection<SplashScreenPriorityOption>();

        [DontSerialize]
        public ObservableCollection<SplashScreenPriorityOption> GameLaunchSplashCustomPriority2Options { get; }
            = new ObservableCollection<SplashScreenPriorityOption>();

        [DontSerialize]
        public ObservableCollection<SplashScreenPriorityOption> GameLaunchSplashCustomPriority3Options { get; }
            = new ObservableCollection<SplashScreenPriorityOption>();

        [DontSerialize]
        public ObservableCollection<SplashScreenPriorityOption> GameLaunchSplashCustomPriority4Options { get; }
            = new ObservableCollection<SplashScreenPriorityOption>();

        [DontSerialize]
        public ObservableCollection<SplashScreenPriorityOption> GameLaunchSplashCustomPriority5Options { get; }
            = new ObservableCollection<SplashScreenPriorityOption>();

        [DontSerialize]
        public SplashScreenPriorityOption GameLaunchSplashCustomPriority1SelectedOption
        {
            get => FindGameLaunchSplashPriorityOption(GameLaunchSplashCustomPriority1Options, GameLaunchSplashCustomPriority1);
            set
            {
                if (value != null)
                {
                    GameLaunchSplashCustomPriority1 = value.Value;
                }
            }
        }

        [DontSerialize]
        public SplashScreenPriorityOption GameLaunchSplashCustomPriority2SelectedOption
        {
            get => FindGameLaunchSplashPriorityOption(GameLaunchSplashCustomPriority2Options, GameLaunchSplashCustomPriority2);
            set
            {
                if (value != null)
                {
                    GameLaunchSplashCustomPriority2 = value.Value;
                }
            }
        }

        [DontSerialize]
        public SplashScreenPriorityOption GameLaunchSplashCustomPriority3SelectedOption
        {
            get => FindGameLaunchSplashPriorityOption(GameLaunchSplashCustomPriority3Options, GameLaunchSplashCustomPriority3);
            set
            {
                if (value != null)
                {
                    GameLaunchSplashCustomPriority3 = value.Value;
                }
            }
        }

        [DontSerialize]
        public SplashScreenPriorityOption GameLaunchSplashCustomPriority4SelectedOption
        {
            get => FindGameLaunchSplashPriorityOption(GameLaunchSplashCustomPriority4Options, GameLaunchSplashCustomPriority4);
            set
            {
                if (value != null)
                {
                    GameLaunchSplashCustomPriority4 = value.Value;
                }
            }
        }

        [DontSerialize]
        public SplashScreenPriorityOption GameLaunchSplashCustomPriority5SelectedOption
        {
            get => FindGameLaunchSplashPriorityOption(GameLaunchSplashCustomPriority5Options, GameLaunchSplashCustomPriority5);
            set
            {
                if (value != null)
                {
                    GameLaunchSplashCustomPriority5 = value.Value;
                }
            }
        }

        public IReadOnlyList<SplashScreenPriorityTarget> GetGameLaunchSplashCustomPriorityOrder()
        {
            var order = new[]
            {
                GameLaunchSplashCustomPriority1,
                GameLaunchSplashCustomPriority2,
                GameLaunchSplashCustomPriority3,
                GameLaunchSplashCustomPriority4,
                GameLaunchSplashCustomPriority5
            };

            var result = new List<SplashScreenPriorityTarget>();
            var used = new HashSet<SplashScreenPriorityTarget>();

            foreach (var target in order)
            {
                if (target == SplashScreenPriorityTarget.None || used.Contains(target))
                {
                    continue;
                }

                used.Add(target);
                result.Add(target);
            }

            return result;
        }

        private void RefreshGameLaunchSplashCustomPriorityOptions()
        {
            if (isRefreshingGameLaunchSplashCustomPriorityOptions)
            {
                return;
            }

            isRefreshingGameLaunchSplashCustomPriorityOptions = true;

            try
            {
                EnsureUniqueGameLaunchSplashCustomPriorities();

                var selections = new[]
                {
                    GameLaunchSplashCustomPriority1,
                    GameLaunchSplashCustomPriority2,
                    GameLaunchSplashCustomPriority3,
                    GameLaunchSplashCustomPriority4,
                    GameLaunchSplashCustomPriority5
                };

                RefreshGameLaunchSplashPriorityOptionsForSlot(GameLaunchSplashCustomPriority1Options, GameLaunchSplashCustomPriority1, selections);
                RefreshGameLaunchSplashPriorityOptionsForSlot(GameLaunchSplashCustomPriority2Options, GameLaunchSplashCustomPriority2, selections);
                RefreshGameLaunchSplashPriorityOptionsForSlot(GameLaunchSplashCustomPriority3Options, GameLaunchSplashCustomPriority3, selections);
                RefreshGameLaunchSplashPriorityOptionsForSlot(GameLaunchSplashCustomPriority4Options, GameLaunchSplashCustomPriority4, selections);
                RefreshGameLaunchSplashPriorityOptionsForSlot(GameLaunchSplashCustomPriority5Options, GameLaunchSplashCustomPriority5, selections);
                NotifyGameLaunchSplashCustomPrioritySelectionsChanged();
            }
            finally
            {
                isRefreshingGameLaunchSplashCustomPriorityOptions = false;
            }
        }

        private void EnsureUniqueGameLaunchSplashCustomPriorities()
        {
            var used = new HashSet<SplashScreenPriorityTarget>();
            var values = new[]
            {
                gameLaunchSplashCustomPriority1,
                gameLaunchSplashCustomPriority2,
                gameLaunchSplashCustomPriority3,
                gameLaunchSplashCustomPriority4,
                gameLaunchSplashCustomPriority5
            };

            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (value == SplashScreenPriorityTarget.None)
                {
                    continue;
                }

                if (used.Contains(value))
                {
                    SetGameLaunchSplashCustomPriorityBacking(i, SplashScreenPriorityTarget.None);
                    continue;
                }

                used.Add(value);
            }
        }

        private void SetGameLaunchSplashCustomPriorityBacking(int index, SplashScreenPriorityTarget value)
        {
            switch (index)
            {
                case 0:
                    if (gameLaunchSplashCustomPriority1 != value)
                    {
                        gameLaunchSplashCustomPriority1 = value;
                        OnPropertyChanged(nameof(GameLaunchSplashCustomPriority1));
                    }
                    break;

                case 1:
                    if (gameLaunchSplashCustomPriority2 != value)
                    {
                        gameLaunchSplashCustomPriority2 = value;
                        OnPropertyChanged(nameof(GameLaunchSplashCustomPriority2));
                    }
                    break;

                case 2:
                    if (gameLaunchSplashCustomPriority3 != value)
                    {
                        gameLaunchSplashCustomPriority3 = value;
                        OnPropertyChanged(nameof(GameLaunchSplashCustomPriority3));
                    }
                    break;

                case 3:
                    if (gameLaunchSplashCustomPriority4 != value)
                    {
                        gameLaunchSplashCustomPriority4 = value;
                        OnPropertyChanged(nameof(GameLaunchSplashCustomPriority4));
                    }
                    break;

                case 4:
                    if (gameLaunchSplashCustomPriority5 != value)
                    {
                        gameLaunchSplashCustomPriority5 = value;
                        OnPropertyChanged(nameof(GameLaunchSplashCustomPriority5));
                    }
                    break;
            }
        }

        private SplashScreenPriorityOption FindGameLaunchSplashPriorityOption(
            ObservableCollection<SplashScreenPriorityOption> options,
            SplashScreenPriorityTarget value)
        {
            if (options == null)
            {
                return null;
            }

            return options.FirstOrDefault(x => x != null && x.Value == value);
        }

        private void NotifyGameLaunchSplashCustomPrioritySelectionsChanged()
        {
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority1));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority2));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority3));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority4));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority5));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority1SelectedOption));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority2SelectedOption));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority3SelectedOption));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority4SelectedOption));
            OnPropertyChanged(nameof(GameLaunchSplashCustomPriority5SelectedOption));
        }

        private void RefreshGameLaunchSplashPriorityOptionsForSlot(
            ObservableCollection<SplashScreenPriorityOption> options,
            SplashScreenPriorityTarget currentValue,
            SplashScreenPriorityTarget[] allSelections)
        {
            if (options == null)
            {
                return;
            }

            var usedByOtherSlots = new HashSet<SplashScreenPriorityTarget>(
                (allSelections ?? new SplashScreenPriorityTarget[0])
                    .Where(x => x != SplashScreenPriorityTarget.None && x != currentValue));

            var allowed = new[]
            {
                SplashScreenPriorityTarget.GameCustom,
                SplashScreenPriorityTarget.GameBackground,
                SplashScreenPriorityTarget.Platform,
                SplashScreenPriorityTarget.Source,
                SplashScreenPriorityTarget.Global,
                SplashScreenPriorityTarget.None
            }
            .Where(x => x == SplashScreenPriorityTarget.None || x == currentValue || !usedByOtherSlots.Contains(x))
            .ToList();

            options.Clear();

            foreach (var target in allowed)
            {
                options.Add(new SplashScreenPriorityOption
                {
                    Value = target,
                    Label = GetGameLaunchSplashPriorityTargetLabel(target)
                });
            }
        }

        private string GetGameLaunchSplashPriorityTargetLabel(SplashScreenPriorityTarget target)
        {
            string key;
            string fallback;

            switch (target)
            {
                case SplashScreenPriorityTarget.GameCustom:
                    key = "GameLaunchSplash_CustomPriority_Target_GameCustom";
                    fallback = "Game custom";
                    break;

                case SplashScreenPriorityTarget.GameBackground:
                    key = "GameLaunchSplash_CustomPriority_Target_GameBackground";
                    fallback = "Game background";
                    break;

                case SplashScreenPriorityTarget.Platform:
                    key = "GameLaunchSplash_CustomPriority_Target_Platform";
                    fallback = "Platform";
                    break;

                case SplashScreenPriorityTarget.Source:
                    key = "GameLaunchSplash_CustomPriority_Target_Source";
                    fallback = "Source";
                    break;

                case SplashScreenPriorityTarget.Global:
                    key = "GameLaunchSplash_CustomPriority_Target_Global";
                    fallback = "Global";
                    break;

                case SplashScreenPriorityTarget.None:
                default:
                    key = "GameLaunchSplash_CustomPriority_Target_None";
                    fallback = "None";
                    break;
            }

            try
            {
                var label = ResourceProvider.GetString(key);
                return string.IsNullOrWhiteSpace(label) ? fallback : label;
            }
            catch
            {
                return fallback;
            }
        }

        private int gameLaunchSplashMinimumDurationMs = 2400;
        public int GameLaunchSplashMinimumDurationMs
        {
            get => gameLaunchSplashMinimumDurationMs;
            set
            {
                SetValue(ref gameLaunchSplashMinimumDurationMs, Math.Max(500, Math.Min(600000, value)));
                OnPropertyChanged(nameof(GameLaunchSplashMinimumDurationSeconds));
                OnPropertyChanged(nameof(GameLaunchSplashMinimumDurationDisplay));
            }
        }

        public double GameLaunchSplashMinimumDurationSeconds
        {
            get => GameLaunchSplashMinimumDurationMs / 1000.0;
            set => GameLaunchSplashMinimumDurationMs = (int)Math.Round(Math.Max(0.5, Math.Min(600, value)) * 1000);
        }

        public string GameLaunchSplashMinimumDurationDisplay
        {
            get
            {
                var seconds = GameLaunchSplashMinimumDurationSeconds;

                if (seconds >= 60)
                {
                    var minutes = seconds / 60.0;
                    return $"{minutes:0.##} min";
                }

                return $"{seconds:0.##} sec";
            }
        }

        private bool gameLaunchSplashAutoDetectReadyEnabled = true;
        public bool GameLaunchSplashAutoDetectReadyEnabled
        {
            get => gameLaunchSplashAutoDetectReadyEnabled;
            set => SetValue(ref gameLaunchSplashAutoDetectReadyEnabled, value);
        }

        private int gameLaunchSplashMaximumWaitMs = 15000;
        public int GameLaunchSplashMaximumWaitMs
        {
            get => gameLaunchSplashMaximumWaitMs;
            set
            {
                SetValue(ref gameLaunchSplashMaximumWaitMs, Math.Max(1000, Math.Min(120000, value)));
                OnPropertyChanged(nameof(GameLaunchSplashMaximumWaitSeconds));
                OnPropertyChanged(nameof(GameLaunchSplashMaximumWaitDisplay));
            }
        }

        public double GameLaunchSplashMaximumWaitSeconds
        {
            get => GameLaunchSplashMaximumWaitMs / 1000.0;
            set => GameLaunchSplashMaximumWaitMs = (int)Math.Round(Math.Max(1, Math.Min(120, value)) * 1000);
        }

        public string GameLaunchSplashMaximumWaitDisplay
        {
            get
            {
                var seconds = GameLaunchSplashMaximumWaitSeconds;

                if (seconds >= 60)
                {
                    var minutes = seconds / 60.0;
                    return $"{minutes:0.##} min";
                }

                return $"{seconds:0.##} sec";
            }
        }

        public Dictionary<Guid, string> CustomGameLaunchSplashImages { get; set; }
            = new Dictionary<Guid, string>();

        public Dictionary<Guid, int> CustomGameLaunchSplashMinimumDurations { get; set; }
            = new Dictionary<Guid, int>();

        private bool shutdownVideoEnabled = true;
        public bool ShutdownVideoEnabled
        {
            get => shutdownVideoEnabled;
            set => SetValue(ref shutdownVideoEnabled, value);
        }

        private bool inGameOverlayEnabled = false;
        public bool InGameOverlayEnabled
        {
            get => inGameOverlayEnabled;
            set => SetValue(ref inGameOverlayEnabled, value);
        }

        private string inGameOverlayHotkey = "CtrlShiftF12";
        public string InGameOverlayHotkey
        {
            get => string.IsNullOrWhiteSpace(inGameOverlayHotkey) ? "CtrlShiftF12" : inGameOverlayHotkey;
            set => SetValue(ref inGameOverlayHotkey, string.IsNullOrWhiteSpace(value) ? "CtrlShiftF12" : value);
        }

        private string inGameOverlayControllerShortcut = "StartBack";
        public string InGameOverlayControllerShortcut
        {
            get => string.IsNullOrWhiteSpace(inGameOverlayControllerShortcut) ? "StartBack" : inGameOverlayControllerShortcut;
            set => SetValue(ref inGameOverlayControllerShortcut, string.IsNullOrWhiteSpace(value) ? "StartBack" : value);
        }

        private string inGameOverlayGameBehavior = "DoNothing";
        public string InGameOverlayGameBehavior
        {
            get
            {
                return string.Equals(inGameOverlayGameBehavior, "SuspendGame", StringComparison.OrdinalIgnoreCase)
                    ? "SuspendGame"
                    : "DoNothing";
            }
            set
            {
                var normalized = string.Equals(value, "SuspendGame", StringComparison.OrdinalIgnoreCase)
                    ? "SuspendGame"
                    : "DoNothing";

                SetValue(ref inGameOverlayGameBehavior, normalized);
            }
        }

        public Dictionary<Guid, string> InGameOverlayNeverSuspendGames { get; set; }
            = new Dictionary<Guid, string>();

        private bool eventSoundsEnabled = true;
        public bool EventSoundsEnabled
        {
            get => eventSoundsEnabled;
            set => SetValue(ref eventSoundsEnabled, value);
        }

        // Enables the Steam cache creation prompt at startup
        private bool askSteamUpdateCacheAtStartup = true;
        public bool AskSteamUpdateCacheAtStartup
        {
            get => askSteamUpdateCacheAtStartup;
            set => SetValue(ref askSteamUpdateCacheAtStartup, value);
        }

        // Timestamp of the last automatic scan of games updates 
        private DateTime? lastSteamRecentCheckUtc;
        public DateTime? LastSteamRecentCheckUtc
        {
            get => lastSteamRecentCheckUtc;
            set => SetValue(ref lastSteamRecentCheckUtc, value);
        }

        // Displaying the date of the last scan
        [DontSerialize]
        public string LastSteamRecentCheckDisplay
        {
            get
            {
                if (LastSteamRecentCheckUtc == null)
                    return "Never";

                var local = LastSteamRecentCheckUtc.Value.ToLocalTime();
                return local.ToString("g"); // Exemple : 01/11/2025 14:35
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

        public string TotalPlaytimeString => PlaytimeToString(TotalPlaytimeMinutes, true);
        public string AveragePlaytimeString => PlaytimeToString(AveragePlaytimeMinutes, false);

        private string profileTopPlatformName;
        public string ProfileTopPlatformName
        {
            get => profileTopPlatformName;
            set => SetValue(ref profileTopPlatformName, value);
        }

        private string profileTopFranchiseName;
        public string ProfileTopFranchiseName
        {
            get => profileTopFranchiseName;
            set => SetValue(ref profileTopFranchiseName, value);
        }

        private string profileTopTagName;
        public string ProfileTopTagName
        {
            get => profileTopTagName;
            set => SetValue(ref profileTopTagName, value);
        }

        #endregion

        // --- What's New ---
        public string LastSeenWhatsNewVersion { get; set; } = string.Empty;

        [DontSerialize]
        private string whatsNewVersion;
        [DontSerialize]
        public string WhatsNewVersion
        {
            get => whatsNewVersion;
            set => SetValue(ref whatsNewVersion, value);
        }

        [DontSerialize]
        private string whatsNewTitle;
        [DontSerialize]
        public string WhatsNewTitle
        {
            get => whatsNewTitle;
            set => SetValue(ref whatsNewTitle, value);
        }

        [DontSerialize]
        private string whatsNewSubtitle;
        [DontSerialize]
        public string WhatsNewSubtitle
        {
            get => whatsNewSubtitle;
            set => SetValue(ref whatsNewSubtitle, value);
        }

        [DontSerialize]
        public ObservableCollection<WhatsNewSlideItem> WhatsNewSlides { get; set; }
            = new ObservableCollection<WhatsNewSlideItem>();

        // --- Random login screen ---
        private int loginRandomIndex;
        public int LoginRandomIndex
        {
            get => loginRandomIndex;
            set
            {
                if (loginRandomIndex != value)
                {
                    SetValue(ref loginRandomIndex, value);
                    OnPropertyChanged(nameof(IsLuckyDay));
                    OnPropertyChanged(nameof(IsLuckyStyle1));
                    OnPropertyChanged(nameof(IsLuckyStyle2));
                }
            }
        }

        [DontSerialize]
        public bool IsLuckyDay => LoginRandomIndex == 42;

        private int luckyStyleIndex;
        public int LuckyStyleIndex
        {
            get => luckyStyleIndex;
            set
            {
                if (luckyStyleIndex != value)
                {
                    SetValue(ref luckyStyleIndex, value);
                    OnPropertyChanged(nameof(IsLuckyStyle1));
                    OnPropertyChanged(nameof(IsLuckyStyle2));
                }
            }
        }

        [DontSerialize]
        public bool IsLuckyStyle1 => IsLuckyDay && LuckyStyleIndex == 1;

        [DontSerialize]
        public bool IsLuckyStyle2 => IsLuckyDay && LuckyStyleIndex == 2;

        [DontSerialize]
        private bool isKonamiEasterEggActive;
        [DontSerialize]
        public bool IsKonamiEasterEggActive
        {
            get => isKonamiEasterEggActive;
            set => SetValue(ref isKonamiEasterEggActive, value);
        }

        [DontSerialize]
        private bool isKonamiModeActive;
        [DontSerialize]
        public bool IsKonamiModeActive
        {
            get => isKonamiModeActive;
            set => SetValue(ref isKonamiModeActive, value);
        }

        // anti-repetition between two launches
        public int LastLoginRandomIndex { get; set; }

        public AnikiHelperSettings() { }

        private AnikiHelperSettings LoadSettingsSafe(global::AnikiHelper.AnikiHelper plugin)
        {
            try
            {
                return plugin.LoadPluginSettings<AnikiHelperSettings>();
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "[AnikiHelper] Failed to load config.json. The file is probably corrupted. A new one will be created.");

                try
                {
                    var configPath = Path.Combine(plugin.GetPluginUserDataPath(), "config.json");

                    if (File.Exists(configPath))
                    {
                        var backupPath = Path.Combine(
                            plugin.GetPluginUserDataPath(),
                            $"config.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.json"
                        );

                        File.Move(configPath, backupPath);

                        logger?.Warn($"[AnikiHelper] Corrupted config.json moved to: {backupPath}");
                    }
                }
                catch (Exception backupEx)
                {
                    logger?.Error(backupEx, "[AnikiHelper] Failed to backup corrupted config.json.");
                }

                return null;
            }
        }

        public AnikiHelperSettings(global::AnikiHelper.AnikiHelper plugin)
        {
            this.plugin = plugin;
            logger = LogManager.GetLogger();

            screenshotsVisualizerReader = new ScreenshotsVisualizerReader(plugin.PlayniteApi, logger);
            screenshotUtilitiesReader = new ScreenshotUtilitiesReader(plugin.PlayniteApi, logger);
            mediaThumbnailService = new AnikiMediaThumbnailService(plugin.GetPluginUserDataPath(), logger);
            screenshotMediaCacheService = new ScreenshotMediaCacheService(plugin.PlayniteApi, plugin.GetPluginUserDataPath(), logger);

            SetAnikiThemeOptionCommand = new RelayCommand<string>(p => plugin?.SetAnikiThemeOption(p));
            ToggleAnikiThemeOptionCommand = new RelayCommand<string>(p => plugin?.ToggleAnikiThemeOption(p));
            SelectAnikiThemePresetCommand = new RelayCommand<string>(p => plugin?.SelectAnikiThemePreset(p));
            ShowAnikiThemePresetPreviewCommand = new RelayCommand<string>(p => plugin?.ShowAnikiThemePresetPreview(p));
            HideAnikiThemePresetPreviewCommand = new RelayCommand(() => plugin?.HideAnikiThemePresetPreview());
            ReloadAnikiThemeSettingsCommand = new RelayCommand(() => plugin?.ReloadAnikiThemeSettings());
            SelectAnikiThemeSettingsCategoryCommand = new RelayCommand<string>(p => SelectAnikiThemeSettingsCategory(p));
            ClearInGameOverlayNeverSuspendGamesCommand = new RelayCommand(ClearInGameOverlayNeverSuspendGames);


            LoadHubLatestMediaFromCache();
            LoadHubMemoryFromCache();
            LoadHubAchievementMemoriesFromCacheWhenDatabaseReady();
            EnsureAchievementMemoriesCacheExists();
            LoadMediaGalleryGamesFromCache();

            var saved = LoadSettingsSafe(plugin);
            if (saved != null)
            {
                AnikiThemeSettingsValues = saved.AnikiThemeSettingsValues
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                AnikiThemeSettingsSelectedPresets = saved.AnikiThemeSettingsSelectedPresets
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                EnableDebugLogs = saved.EnableDebugLogs;

                IncludeHidden = saved.IncludeHidden;
                TopPlayedMax = saved.TopPlayedMax <= 0 ? 10 : saved.TopPlayedMax;
                PlaytimeStoredInHours = saved.PlaytimeStoredInHours;
                PlaytimeUseDaysFormat = saved.PlaytimeUseDaysFormat;

                OpenWelcomeHubOnStartup = saved.OpenWelcomeHubOnStartup;
                HubAppsEnabled = saved.HubAppsEnabled;
                HubAppSlot1ToolName = saved.HubAppSlot1ToolName ?? string.Empty;
                HubAppSlot2ToolName = saved.HubAppSlot2ToolName ?? string.Empty;
                HubAppSlot3ToolName = saved.HubAppSlot3ToolName ?? string.Empty;
                HubAppSlot4ToolName = saved.HubAppSlot4ToolName ?? string.Empty;
                HubAppSlot1BackgroundPath = saved.HubAppSlot1BackgroundPath ?? string.Empty;
                HubAppSlot2BackgroundPath = saved.HubAppSlot2BackgroundPath ?? string.Empty;
                HubAppSlot3BackgroundPath = saved.HubAppSlot3BackgroundPath ?? string.Empty;
                HubAppSlot4BackgroundPath = saved.HubAppSlot4BackgroundPath ?? string.Empty;

                SteamStoreLanguage = string.IsNullOrWhiteSpace(saved.SteamStoreLanguage) ? "english" : saved.SteamStoreLanguage;
                SteamStoreRegion = string.IsNullOrWhiteSpace(saved.SteamStoreRegion) ? "US" : saved.SteamStoreRegion;
                SteamStoreEnabled = saved.SteamStoreEnabled;

                SteamFriendsEnabled = saved.SteamFriendsEnabled;
                SteamApiKey = saved.SteamApiKey ?? string.Empty;
                SteamId64 = saved.SteamId64 ?? string.Empty;
                SteamAccountSteamId64 = saved.SteamAccountSteamId64 ?? string.Empty;
                SteamAccountProfileUrl = saved.SteamAccountProfileUrl ?? string.Empty;
                SteamAccountConnected = !string.IsNullOrWhiteSpace(SteamAccountSteamId64);
                SteamAccountStatus = SteamAccountConnected ? "Steam account remembered. Click Check Steam account to verify session." : "Not connected";
                ShowOffline = saved.ShowOffline;
                NotifyOnGameStart = saved.NotifyOnGameStart;
                NotifyOnConnect = saved.NotifyOnConnect;

                CustomFilterIconsFolder = saved.CustomFilterIconsFolder ?? string.Empty;
                CustomSourceIconsFolder = saved.CustomSourceIconsFolder ?? string.Empty;
                CustomBannerAboveCoverFolder = saved.CustomBannerAboveCoverFolder ?? string.Empty;
                CustomBannerOnCoverFolder = saved.CustomBannerOnCoverFolder ?? string.Empty;

                LoginRandomIndex = saved.LoginRandomIndex;
                LastLoginRandomIndex = saved.LastLoginRandomIndex;
                LastSeenWhatsNewVersion = saved.LastSeenWhatsNewVersion ?? string.Empty;

                InGameOverlayEnabled = saved.InGameOverlayEnabled;

                InGameOverlayHotkey = string.IsNullOrWhiteSpace(saved.InGameOverlayHotkey)
                    ? "CtrlShiftF12"
                    : saved.InGameOverlayHotkey;

                InGameOverlayControllerShortcut = string.IsNullOrWhiteSpace(saved.InGameOverlayControllerShortcut)
                    ? "StartBack"
                    : saved.InGameOverlayControllerShortcut;

                InGameOverlayGameBehavior = string.IsNullOrWhiteSpace(saved.InGameOverlayGameBehavior)
                    ? "DoNothing"
                    : saved.InGameOverlayGameBehavior;

                InGameOverlayNeverSuspendGames = saved.InGameOverlayNeverSuspendGames != null
                    ? new Dictionary<Guid, string>(saved.InGameOverlayNeverSuspendGames)
                    : new Dictionary<Guid, string>();

                SteamPlayerCountEnabled = saved.SteamPlayerCountEnabled;
                SteamUpdatesScanEnabled = saved.SteamUpdatesScanEnabled;
                AskSteamUpdateCacheAtStartup = saved.AskSteamUpdateCacheAtStartup;
                StartupIntroVideoEnabled = saved.StartupIntroVideoEnabled;
                GameLaunchSplashEnabled = saved.GameLaunchSplashEnabled;
                GameLaunchSplashPauseUniPlaySong = saved.GameLaunchSplashPauseUniPlaySong;
                GameLaunchSplashShowLogo = saved.GameLaunchSplashShowLogo;
                GameLaunchSplashVideoSoundEnabled = saved.GameLaunchSplashVideoSoundEnabled;
                GameLaunchSplashVideoEndBehavior = saved.GameLaunchSplashVideoEndBehavior;
                GameLaunchSplashVideoVolume = saved.GameLaunchSplashVideoVolume;
                GameLaunchSplashLogoPosition = saved.GameLaunchSplashLogoPosition;
                GameLaunchSplashSelectionMode = saved.GameLaunchSplashSelectionMode;
                GameLaunchSplashCustomPriority1 = saved.GameLaunchSplashCustomPriority1;
                GameLaunchSplashCustomPriority2 = saved.GameLaunchSplashCustomPriority2;
                GameLaunchSplashCustomPriority3 = saved.GameLaunchSplashCustomPriority3;
                GameLaunchSplashCustomPriority4 = saved.GameLaunchSplashCustomPriority4;
                GameLaunchSplashCustomPriority5 = saved.GameLaunchSplashCustomPriority5;
                GameLaunchSplashMinimumDurationMs = saved.GameLaunchSplashMinimumDurationMs;
                GameLaunchSplashAutoDetectReadyEnabled = saved.GameLaunchSplashAutoDetectReadyEnabled;
                GameLaunchSplashMaximumWaitMs = saved.GameLaunchSplashMaximumWaitMs;
                CustomGameLaunchSplashImages = saved.CustomGameLaunchSplashImages
                    ?? new Dictionary<Guid, string>();
                CustomGameLaunchSplashMinimumDurations = saved.CustomGameLaunchSplashMinimumDurations
                    ?? new Dictionary<Guid, int>();
                ShutdownVideoEnabled = saved.ShutdownVideoEnabled;
                LastSteamRecentCheckUtc = saved.LastSteamRecentCheckUtc;
                EventSoundsEnabled = saved.EventSoundsEnabled;
                MediaGalleryProvider = saved.MediaGalleryProvider;

                NewsScanEnabled = saved.NewsScanEnabled;
                LastNewsScanUtc = saved.LastNewsScanUtc;

                NewsSourceATitle = string.IsNullOrWhiteSpace(saved.NewsSourceATitle) ? "News" : saved.NewsSourceATitle;
                NewsSourceBTitle = string.IsNullOrWhiteSpace(saved.NewsSourceBTitle) ? "Reviews" : saved.NewsSourceBTitle;
                NewsSourceAUrl = string.IsNullOrWhiteSpace(saved.NewsSourceAUrl)
                    ? "https://gameinformer.com/news.xml"
                    : saved.NewsSourceAUrl;
                NewsSourceBUrl = string.IsNullOrWhiteSpace(saved.NewsSourceBUrl)
                    ? "https://gameinformer.com/reviews.xml"
                    : saved.NewsSourceBUrl;

                LastCachedNewsSourceAUrl = saved.LastCachedNewsSourceAUrl ?? string.Empty;
                LastCachedNewsSourceBUrl = saved.LastCachedNewsSourceBUrl ?? string.Empty;

                DynamicAutoPrecacheUserEnabled = saved.DynamicAutoPrecacheUserEnabled;
                DynamicColorCacheVersion = saved.DynamicColorCacheVersion ?? string.Empty;

                ProfileGenreKey = saved.ProfileGenreKey ?? string.Empty;
                ProfileGenreLabel = saved.ProfileGenreLabel ?? string.Empty;
                LastProfileGenreScanUtc = saved.LastProfileGenreScanUtc;

                SteamGlobalNewsALastRefreshUtc = saved.SteamGlobalNewsALastRefreshUtc;
                SteamGlobalNewsBLastRefreshUtc = saved.SteamGlobalNewsBLastRefreshUtc;

                if (saved.SteamGlobalNewsA != null && saved.SteamGlobalNewsA.Any())
                {
                    SteamGlobalNewsA.Clear();
                    foreach (var it in saved.SteamGlobalNewsA)
                    {
                        SteamGlobalNewsA.Add(it);
                    }
                }

                if (saved.SteamGlobalNewsB != null && saved.SteamGlobalNewsB.Any())
                {
                    SteamGlobalNewsB.Clear();
                    foreach (var it in saved.SteamGlobalNewsB)
                    {
                        SteamGlobalNewsB.Add(it);
                    }
                }

                PlayniteNewsLastRefreshUtc = saved.PlayniteNewsLastRefreshUtc;
                PlayniteNewsLastKey = saved.PlayniteNewsLastKey;
                PlayniteNewsHasNew = saved.PlayniteNewsHasNew;

                if (saved.PlayniteNews != null && saved.PlayniteNews.Any())
                {
                    PlayniteNews.Clear();
                    foreach (var it in saved.PlayniteNews)
                    {
                        PlayniteNews.Add(it);
                    }
                }

                if (saved.LastNotifications != null && saved.LastNotifications.Any())
                {
                    LastNotifications.Clear();

                    foreach (var it in saved.LastNotifications
                        .Where(x => x != null
                            && !string.Equals(x.Type, "gameEnded", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(x.Title, "Game session ended", StringComparison.OrdinalIgnoreCase))
                        .Take(20))
                    {
                        LastNotifications.Add(it);
                    }
                }

                SessionGameName = saved.SessionGameName ?? string.Empty;
                SessionDurationString = saved.SessionDurationString ?? string.Empty;
                SessionTotalPlaytimeString = saved.SessionTotalPlaytimeString ?? string.Empty;
                SessionGameBackgroundPath = saved.SessionGameBackgroundPath ?? string.Empty;
                SessionGameId = saved.SessionGameId;
                ThisMonthTopGameId = saved.ThisMonthTopGameId;
                ThisYearTopGameId = saved.ThisYearTopGameId;
                RecentPlayedBackgroundPath = saved.RecentPlayedBackgroundPath ?? string.Empty;
                HubRecentAddedName = saved.HubRecentAddedName ?? string.Empty;
                HubRecentAddedDate = saved.HubRecentAddedDate ?? string.Empty;
                HubRecentAddedBackgroundPath = saved.HubRecentAddedBackgroundPath ?? string.Empty;
                HubRecentAddedGameId = saved.HubRecentAddedGameId;
                RecentPlayedGameId = saved.RecentPlayedGameId;

                HubNeverPlayedName = saved.HubNeverPlayedName ?? string.Empty;
                HubNeverPlayedDate = saved.HubNeverPlayedDate ?? string.Empty;
                HubNeverPlayedBackgroundPath = saved.HubNeverPlayedBackgroundPath ?? string.Empty;
                HubNeverPlayedGameId = saved.HubNeverPlayedGameId;
                IsWelcomeHubOpen = saved.IsWelcomeHubOpen;
            }

            EnsureSteamFriendsRuntimeCollections();

            if (CustomGameLaunchSplashImages == null)
            {
                CustomGameLaunchSplashImages = new Dictionary<Guid, string>();
            }

            if (CustomGameLaunchSplashMinimumDurations == null)
            {
                CustomGameLaunchSplashMinimumDurations = new Dictionary<Guid, int>();
            }

            if (InGameOverlayNeverSuspendGames == null)
            {
                InGameOverlayNeverSuspendGames = new Dictionary<Guid, string>();
            }

            RefreshGameLaunchSplashCustomPriorityOptions();
            RefreshInGameOverlayNeverSuspendGameItems();
            LoadOverlayApps();

            if (saved == null)
            {
                try
                {
                    plugin.SavePluginSettings(this);
                    logger?.Info("[AnikiHelper] A new clean config.json has been created.");
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to create a new clean config.json.");
                }
            }

            // Bouton "Refresh SuccessStory"
            RefreshSuccessStoryCommand = new RelayCommand(
                async () => await SuccessStoryBridge.RefreshSelectedGameAsync(plugin.PlayniteApi),
                () => plugin?.PlayniteApi?.MainView?.SelectedGames?.Any() == true
            );

            RefreshMediaGalleryCommand = new RelayCommand(
                () =>
                {
                    RefreshMediaGallery();
                }
            );

            GenerateMediaThumbnailsCommand = new RelayCommand(
                () =>
                {
                    _ = GenerateAllMediaThumbnailsAsync();
                }
            );

            RefreshAchievementMemoriesCommand = new RelayCommand(
                () =>
                {
                    _ = RefreshAchievementMemoriesWithProgressAsync();
                },
                () => !IsRefreshingAchievementMemories
            );

            RefreshCurrentGameMediaCommand = new RelayCommand(
                () =>
                {
                    RefreshCurrentGameMediaFromSelectedGame();
                }
            );

            RefreshOverlayLastCapturesCommand = new RelayCommand(
                () =>
                {
                    _ = RefreshOverlayLastCapturesAsync();
                }
            );

            OpenScreenshotsWindowCommand = new RelayCommand(
                () =>
                {
                    PrepareCurrentGameMediaLoading();

                    plugin?.OpenWindow("ScreenShotsThumbsWindowStyle");
                    plugin?.HookScreenshotsLazyLoad();

                    _ = RefreshCurrentGameMediaFromSelectedGameAsync();
                }

            );

            OpenOverlayAppCommand = new RelayCommand<AnikiOverlayAppItem>(
                appItem => OpenOverlayApp(appItem)
            );

            ToggleOverlayAchievementsSortCommand = new RelayCommand(ToggleOverlayAchievementsSort);

            OpenScreenshotsForMediaGameCommand = new RelayCommand<AnikiMediaGameItem>(
                mediaGame =>
                {
                    if (mediaGame == null || mediaGame.GameId == Guid.Empty)
                    {
                        return;
                    }

                    _ = OpenScreenshotsWindowForGameAsync(mediaGame.GameId);
                }
            );

            OpenScreenshotsForMediaItemCommand = new RelayCommand<AnikiMediaItem>(
                mediaItem =>
                {
                    if (mediaItem == null || mediaItem.GameId == Guid.Empty)
                    {
                        return;
                    }

                    _ = OpenScreenshotsWindowForGameAsync(mediaItem.GameId);
                }
            );

            OpenOverlayCapturePreviewCommand = new RelayCommand<AnikiMediaItem>(
                mediaItem =>
                {
                    if (mediaItem == null)
                    {
                        return;
                    }

                    plugin?.OpenOverlayCapturePreview(mediaItem);
                }
            );

            RefreshMediaGalleryLibraryCommand = new RelayCommand(
                () =>
                {
                    _ = RefreshMediaGalleryLibraryAsync();
                }
            );

            OpenMediaGalleryGamesWindowCommand = new RelayCommand(
                () =>
                {
                    LoadMediaGalleryGamesFromCache();
                    plugin?.OpenWindow("MediaGalleryGamesWindowStyle");
                }
            );

            OpenGameDetailsCommand = new RelayCommand<object>(
                gameObj =>
                {
                    if (gameObj is Guid gameId && gameId != Guid.Empty)
                    {
                        plugin?.OpenGameDetails(gameId);
                    }
                },
                gameObj => gameObj is Guid gameId && gameId != Guid.Empty
            );

            ToggleWelcomeHubCommand = new RelayCommand<object>(
                _ =>
                {
                    if (IsWelcomeHubOpen)
                    {
                        plugin?.CloseWelcomeHub();
                    }
                    else
                    {
                        plugin?.OpenWelcomeHub();
                    }
                }
            );

            OpenWindow = new AnikiWindowCommandProvider(
                styleKey => new RelayCommand(() => plugin?.OpenWindow(styleKey))
            );

            OpenChildWindow = new AnikiWindowCommandProvider(
                styleKey => new RelayCommand(() => plugin?.OpenChildWindow(styleKey))
            );

            OpenInGameOverlayCommand = new RelayCommand(() => plugin?.OpenInGameOverlayFromThemeButton());

            OpenInGameOverlay = new AnikiWindowCommandProvider(
                _ => new RelayCommand(() => plugin?.OpenInGameOverlayFromThemeButton())
            );

            OpenHelpLink = new AnikiWindowCommandProvider(
                linkKey => new RelayCommand(() => plugin?.OpenHelpLink(linkKey))
            );

            MusicTransport = new AnikiWindowCommandProvider(
                commandKey => new RelayCommand(() => plugin?.ExecuteMusicTransportCommand(commandKey))
            );

            OpenSteamGameNewsWindowCommand = new RelayCommand(() => plugin?.OpenSteamGameNewsWindow());

            OpenDuplicateHiderVersionsWindowCommand = new RelayCommand(
                () =>
                {
                    if (PrepareDuplicateHiderVersionsWindow())
                    {
                        plugin?.OpenChildWindow("DuplicateHiderVersionsWindowStyle|FocusFirst");
                    }
                }
            );

            HubNextPageCommand = new RelayCommand(() =>
            {
                NextHubPage();
            });

            HubPreviousPageCommand = new RelayCommand(() =>
            {
                PreviousHubPage();
            });

            HubSetPageCommand = new RelayCommand<object>(page =>
            {
                SetHubPage(page);
            });

            InitializeWelcomeHubCommand = new RelayCommand<object>(
                param =>
                {
                    if (param is bool openAtStartup)
                    {
                        plugin?.InitializeWelcomeHubState(openAtStartup);
                    }
                }
            );

            CloseTopWindowCommand = new RelayCommand(() => plugin?.CloseTopWindow());

            OpenWhatsNewCommand = new RelayCommand(() => plugin?.OpenWhatsNewFromMenu());

            OpenNotificationsCommand = new RelayCommand(() => plugin?.OpenNotificationsMenuFromQuickAccess());

            OpenLockScreenCommand = new RelayCommand(() => plugin?.OpenLockScreenFromQuickAccess());

            OpenPowerMenuCommand = new RelayCommand(() => plugin?.OpenPowerMenuFromQuickAccess());

            OpenPlayniteSettingsCommand = new RelayCommand(() => plugin?.OpenPlayniteSettingsFromShortcut());

            OpenExternalClientsCommand = new RelayCommand(() => plugin?.OpenExternalClientsFromHelpMenu());

            OpenRandomGameCommand = new RelayCommand(() => plugin?.OpenRandomGameFromQuickAccess());

            UpdateGameLibraryCommand = new RelayCommand(() => plugin?.UpdateGameLibraryFromQuickAccess());

            OpenAchievementsCommand = new RelayCommand(() => plugin?.OpenAchievementsFromQuickAccess());

            RefreshRecentAchievementsCommand = new RelayCommand(() =>
                plugin?.TriggerHiddenButtonAfterClosingTopWindow("HiddenRecentRefreshButton"));

            RefreshInstalledAchievementsCommand = new RelayCommand(() =>
                plugin?.TriggerHiddenButtonAfterClosingTopWindow("HiddenInstalledRefreshButton"));

            RefreshFavoritesAchievementsCommand = new RelayCommand(() =>
                plugin?.TriggerHiddenButtonAfterClosingTopWindow("HiddenFavoritesRefreshButton"));

            RefreshFullAchievementsCommand = new RelayCommand(() =>
                plugin?.TriggerHiddenButtonAfterClosingTopWindow("HiddenFullRefreshButton"));

            NextNewsTabCommand = new RelayCommand(() => plugin?.SwitchNewsTab(true));

            PreviousNewsTabCommand = new RelayCommand(() => plugin?.SwitchNewsTab(false));

            CloseHubToLibraryCommand = new RelayCommand(() => plugin?.CloseHubToLibraryFromShortcut());

            QuickOptionsPreviousSectionCommand = new RelayCommand(() => plugin?.SwitchQuickOptionsSection(-1));

            QuickOptionsNextSectionCommand = new RelayCommand(() => plugin?.SwitchQuickOptionsSection(1));

            OpenPlayniteMainMenuCommand = new RelayCommand(() => { });

            CloseWelcomeHubCommand = new RelayCommand<object>(
                _ =>
                {
                    plugin?.CloseWelcomeHub();
                }
            );

            OpenSteamStoreDetailsCommand = new RelayCommand<SteamStoreItem>(
                item => plugin?.OpenSteamStoreDetails(item),
                item => item != null
            );

            OpenSteamStoreHeroDetailsCommand = new RelayCommand(
                () =>
                {
                    if (SteamStoreHeroItem != null)
                    {
                        plugin?.OpenSteamStoreDetails(SteamStoreHeroItem);
                    }
                }
            );

            SetSteamStoreSectionCommand = new RelayCommand<object>(
                section =>
                {
                    plugin?.SetSteamStoreSection(section?.ToString());
                }
            );


            ConnectSteamAccountCommand = new RelayCommand(() => plugin?.ConnectSteamAccountFromSettings());
            CheckSteamAccountCommand = new RelayCommand(() => plugin?.CheckSteamAccountFromSettings());
            DisconnectSteamAccountCommand = new RelayCommand(() => plugin?.DisconnectSteamAccountFromSettings());

            CloseSteamStoreDetailsCommand = new RelayCommand(
                () =>
                {
                    plugin.Settings.SteamStoreDetailsVisible = false;
                    plugin.Settings.SteamStoreDetailsLoading = false;
                    plugin.Settings.SteamStoreDetailsTitle = string.Empty;
                    plugin.Settings.SteamStoreDetailsImage = string.Empty;
                    plugin.Settings.SteamStoreDetailsBackgroundImage = string.Empty;
                    plugin.Settings.SteamStoreDetailsDescription = string.Empty;
                    plugin.Settings.SteamStoreDetailsPrice = string.Empty;
                    plugin.Settings.SteamStoreDetailsDiscount = string.Empty;
                    plugin.Settings.SteamStoreDetailsOriginalPrice = string.Empty;
                    plugin.Settings.SteamStoreDetailsMetacriticScore = string.Empty;
                    plugin.Settings.SteamStoreDetailsRecommendationsTotal = string.Empty;
                    plugin.Settings.SteamStoreDetailsAchievementsTotal = string.Empty;
                    plugin.Settings.SteamStoreDetailsDlcCount = string.Empty;
                    plugin.Settings.SteamStoreDetailsScreenshot1 = string.Empty;
                    plugin.Settings.SteamStoreDetailsScreenshot2 = string.Empty;
                    plugin.Settings.SteamStoreDetailsScreenshot3 = string.Empty;
                    plugin.Settings.SteamStoreDetailsScreenshot4 = string.Empty;
                    plugin.Settings.SteamStoreDetailsScreenshot5 = string.Empty;
                    plugin.Settings.SteamStoreScreenshotViewerVisible = false;
                    plugin.Settings.SteamStoreScreenshotViewerImage = string.Empty;
                    plugin.Settings.SteamStoreDetailsReleaseDate = string.Empty;
                    plugin.Settings.SteamStoreDetailsSupportedLanguages = string.Empty;
                    plugin.Settings.SteamStoreDetailsIsPreorder = false;
                    plugin.Settings.SteamStoreDetailsDevelopers = string.Empty;
                    plugin.Settings.SteamStoreDetailsPublishers = string.Empty;
                    plugin.Settings.SteamStoreDetailsGenres = string.Empty;
                    plugin.Settings.SteamStoreDetailsCategories = string.Empty;
                    plugin.Settings.SteamStoreDetailsControllerSupport = string.Empty;
                    plugin.Settings.SteamStoreDetailsAppId = 0;
                    plugin.Settings.SteamStoreDetailsStoreUrl = string.Empty;
                }
            );

            OpenSteamStoreScreenshotViewerCommand = new RelayCommand<object>(
                image =>
                {
                    plugin?.OpenSteamStoreScreenshotViewer(image);
                },
                image => image != null && !string.IsNullOrWhiteSpace(image.ToString())
            );

            CloseSteamStoreScreenshotViewerCommand = new RelayCommand(
                () =>
                {
                    plugin?.CloseSteamStoreScreenshotViewer();
                }
            );

            OpenSteamStorePageExternalCommand = new RelayCommand(
                () =>
                {
                    try
                    {
                        var url = plugin?.Settings?.SteamStoreDetailsStoreUrl;

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to open Steam Store page.");
                    }
                }
            );

            // Recent Trophy + watcher
            LoadRecentAchievements(3);
            LoadRareTop(3);
            TryStartAchievementsWatcher();

            // Startup storage
            LoadDiskUsages();
        }

        public void RefreshMediaGallery()
        {
            try
            {
                MediaGalleryLoading = true;
                MediaGalleryStatusText = "Loading media gallery...";

                var items = LoadUnifiedMediaItems();

                ReplaceMediaCollection(MediaGalleryItems, items);

                MediaGalleryCount = MediaGalleryItems.Count;
                MediaGalleryStatusText = MediaGalleryCount + " media item(s) loaded.";

                RefreshCurrentGameMediaFromSelectedGame();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to refresh media gallery.");
                MediaGalleryStatusText = "Failed to load media gallery.";
            }
            finally
            {
                MediaGalleryLoading = false;
            }
        }

        private List<AnikiMediaItem> LoadUnifiedMediaItems()
        {
            var allItems = new List<AnikiMediaItem>();

            try
            {
                if (screenshotsVisualizerReader == null)
                {
                    screenshotsVisualizerReader = new ScreenshotsVisualizerReader(plugin.PlayniteApi, logger);
                }

                allItems.AddRange(screenshotsVisualizerReader.LoadAll());
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Screenshots Visualizer media.");
            }

            try
            {
                if (screenshotUtilitiesReader == null)
                {
                    screenshotUtilitiesReader = new ScreenshotUtilitiesReader(plugin.PlayniteApi, logger);
                }

                allItems.AddRange(screenshotUtilitiesReader.LoadAllLocal());
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Screenshot Utilities media.");
            }

            return NormalizeUnifiedMediaItems(allItems);
        }

        private List<AnikiMediaItem> LoadUnifiedMediaItemsForGame(Guid gameId)
        {
            var allItems = new List<AnikiMediaItem>();

            if (gameId == Guid.Empty)
            {
                return allItems;
            }

            try
            {
                if (screenshotsVisualizerReader == null)
                {
                    screenshotsVisualizerReader = new ScreenshotsVisualizerReader(plugin.PlayniteApi, logger);
                }

                allItems.AddRange(screenshotsVisualizerReader.LoadForGame(gameId));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Visualizer media for game.");
            }

            try
            {
                if (screenshotUtilitiesReader == null)
                {
                    screenshotUtilitiesReader = new ScreenshotUtilitiesReader(plugin.PlayniteApi, logger);
                }

                allItems.AddRange(screenshotUtilitiesReader.LoadLocalForGame(gameId));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Utilities media for game.");
            }

            return NormalizeUnifiedMediaItems(allItems);
        }

        private List<AnikiMediaItem> NormalizeUnifiedMediaItems(IEnumerable<AnikiMediaItem> items)
        {
            var rawItems = (items ?? Enumerable.Empty<AnikiMediaItem>()).ToList();

            logger?.Info($"[AnikiHelper] Unified raw media count: {rawItems.Count}");
            logger?.Info($"[AnikiHelper] Visualizer count: {rawItems.Count(x => x.SourceProvider == "Screenshots Visualizer")}");
            logger?.Info($"[AnikiHelper] Utilities count: {rawItems.Count(x => x.SourceProvider == "Screenshot Utilities - Local")}");

            var duplicateCount = rawItems
                .Where(x => x != null)
                .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                .GroupBy(x => NormalizeMediaItemPath(x.FilePath), StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() > 1);

            logger?.Info($"[AnikiHelper] Unified duplicate file groups removed: {duplicateCount}");

            var list = rawItems.Where(x => x != null)
                .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                .Where(x => File.Exists(x.FilePath))
                .GroupBy(x => NormalizeMediaItemPath(x.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var candidates = group.ToList();

                    var selected = candidates
                        .OrderByDescending(x =>
                            !string.IsNullOrWhiteSpace(x.DurationString))
                        .ThenByDescending(x =>
                            HasValidProviderThumbnail(x))
                        .ThenByDescending(x =>
                            x.CaptureDate)
                        .First();

                    // Récupère la meilleure miniature même si elle vient
                    // de l'autre provider.
                    if (!HasValidProviderThumbnail(selected))
                    {
                        var thumbnailSource = candidates
                            .FirstOrDefault(x => HasValidProviderThumbnail(x));

                        if (thumbnailSource != null)
                        {
                            selected.ThumbnailPath = thumbnailSource.ThumbnailPath;
                        }
                    }

                    // Conserve la durée fournie par Visualizer même si
                    // l'élément principal vient de Screenshot Utilities.
                    if (string.IsNullOrWhiteSpace(selected.DurationString))
                    {
                        var durationSource = candidates
                            .FirstOrDefault(x =>
                                !string.IsNullOrWhiteSpace(x.DurationString));

                        if (durationSource != null)
                        {
                            selected.DurationString = durationSource.DurationString;
                        }
                    }

                    return selected;
                })
                .OrderByDescending(x => x.CaptureDate)
                .ToList();

            for (int i = 0; i < list.Count; i++)
            {
                list[i].MediaIndex = i + 1;
                list[i].MediaTotal = list.Count;
            }

            return list;
        }

        private bool HasValidProviderThumbnail(AnikiMediaItem item)
        {
            try
            {
                if (item == null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(item.ThumbnailPath))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(item.FilePath))
                {
                    return false;
                }

                if (string.Equals(item.ThumbnailPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return File.Exists(item.ThumbnailPath) && new FileInfo(item.ThumbnailPath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private string NormalizeMediaItemPath(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path?.Trim() ?? string.Empty;
            }
        }

        private List<AnikiMediaItem> ApplyThumbnailsToMediaItems(IEnumerable<AnikiMediaItem> items)
        {
            var result = items?.ToList() ?? new List<AnikiMediaItem>();

            try
            {
                if (mediaThumbnailService == null)
                {
                    mediaThumbnailService = new AnikiMediaThumbnailService(plugin.GetPluginUserDataPath(), logger);
                }

                foreach (var item in result)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var thumbnailPath = mediaThumbnailService.GetOrCreateThumbnail(item);

                    if (!string.IsNullOrWhiteSpace(thumbnailPath))
                    {
                        item.ThumbnailPath = thumbnailPath;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to apply media thumbnails.");
            }

            return result;
        }

        public Task GenerateAllMediaThumbnailsAsync()
        {
            if (MediaThumbnailPrecacheLoading)
            {
                return Task.CompletedTask;
            }

            MediaThumbnailPrecacheLoading = true;
            MediaThumbnailPrecacheDone = 0;
            MediaThumbnailPrecacheTotal = 0;
            MediaThumbnailPrecacheStatus = ResourceProvider.GetString("LOCAnikiHelperScanningAllMedia");

            return Task.Run(() =>
            {
                int scannedCount = 0;
                int createdCount = 0;
                int alreadyCachedCount = 0;
                int failedCount = 0;

                try
                {
                    plugin.PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                    {
                        progress.IsIndeterminate = true;
                        progress.Text = ResourceProvider.GetString("MediaGallery_Status_Scanning");

                        var loadedItems = LoadUnifiedMediaItems();

                        var imageItems = loadedItems
                            .Where(x => x != null)
                            .Where(x => !x.IsVideo)
                            .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                            .Where(x => File.Exists(x.FilePath))
                            .ToList();

                        scannedCount = imageItems.Count;

                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            MediaThumbnailPrecacheTotal = scannedCount;
                            MediaThumbnailPrecacheDone = 0;
                            MediaThumbnailPrecacheStatus = string.Format(
                                ResourceProvider.GetString("LOCAnikiHelperGeneratingThumbnailsProgress"),
                                0,
                                scannedCount,
                                0
                            );
                        }), DispatcherPriority.Background);

                        progress.IsIndeterminate = false;
                        progress.ProgressMaxValue = scannedCount;
                        progress.CurrentProgressValue = 0;

                        if (mediaThumbnailService == null)
                        {
                            mediaThumbnailService = new AnikiMediaThumbnailService(plugin.GetPluginUserDataPath(), logger);
                        }

                        for (int i = 0; i < imageItems.Count; i++)
                        {
                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                var cancelledText = string.Format(
                                    ResourceProvider.GetString("LOCAnikiHelperThumbnailGenerationCancelledDetailed"),
                                    createdCount,
                                    scannedCount
                                );

                                progress.Text = cancelledText;

                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    MediaThumbnailPrecacheStatus = cancelledText;
                                }), DispatcherPriority.Background);

                                break;
                            }

                            var item = imageItems[i];
                            var done = i + 1;

                            var alreadyHadGeneratedThumbnail = mediaThumbnailService.HasGeneratedImageThumbnail(item);

                            if (alreadyHadGeneratedThumbnail)
                            {
                                alreadyCachedCount++;
                            }

                            progress.CurrentProgressValue = done;

                            var progressText = string.Format(
                                ResourceProvider.GetString("MediaGallery_Status_Progress_Detailed"),
                                done,
                                scannedCount,
                                createdCount
                            );

                            progress.Text = progressText;

                            try
                            {
                                var thumbnailPath = mediaThumbnailService.GetOrCreateThumbnail(item);

                                var hasValidGeneratedThumbnail =
                                    !string.IsNullOrWhiteSpace(thumbnailPath) &&
                                    !string.Equals(thumbnailPath, item.FilePath, StringComparison.OrdinalIgnoreCase) &&
                                    File.Exists(thumbnailPath);

                                if (hasValidGeneratedThumbnail && !alreadyHadGeneratedThumbnail)
                                {
                                    createdCount++;
                                }

                                if (!hasValidGeneratedThumbnail)
                                {
                                    failedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                failedCount++;
                                logger?.Debug(ex, "[AnikiHelper] Failed to generate media thumbnail.");
                            }

                            if (done % 5 == 0 || done == scannedCount)
                            {
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    MediaThumbnailPrecacheDone = done;
                                    MediaThumbnailPrecacheStatus = string.Format(
                                        ResourceProvider.GetString("MediaGallery_Status_Progress_Detailed"),
                                        done,
                                        scannedCount,
                                        createdCount
                                    );
                                }), DispatcherPriority.Background);
                            }
                        }

                        if (!progress.CancelToken.IsCancellationRequested)
                        {
                            progress.Text = ResourceProvider.GetString("LOCAnikiHelperUpdatingMediaCache");

                            try
                            {
                                if (screenshotMediaCacheService == null)
                                {
                                    screenshotMediaCacheService = new ScreenshotMediaCacheService(
                                        plugin.PlayniteApi,
                                        plugin.GetPluginUserDataPath(),
                                        logger
                                    );
                                }

                                var itemsWithThumbnails = ApplyThumbnailsToMediaItems(loadedItems);
                                screenshotMediaCacheService.RebuildCaches(itemsWithThumbnails);
                                LoadHubMemoryFromCache();
                                _ = RefreshAchievementMemoriesAsync();

                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    LoadHubLatestMediaFromCache();
                                    LoadMediaGalleryGamesFromCache();
                                }), DispatcherPriority.Background);
                            }
                            catch (Exception ex)
                            {
                                logger?.Warn(ex, "[AnikiHelper] Failed to rebuild screenshot media cache.");
                            }

                            var doneText = string.Format(
                                ResourceProvider.GetString("LOCAnikiHelperThumbnailsGeneratedDetailed"),
                                createdCount,
                                scannedCount,
                                alreadyCachedCount,
                                failedCount
                            );

                            progress.Text = doneText;

                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                MediaThumbnailPrecacheDone = MediaThumbnailPrecacheTotal;
                                MediaThumbnailPrecacheStatus = doneText;
                            }), DispatcherPriority.Background);
                        }
                    },
                    new GlobalProgressOptions(ResourceProvider.GetString("LOCAnikiHelperGeneratingGameThumbnails"))
                    {
                        IsIndeterminate = false,
                        Cancelable = true
                    });
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to generate all media thumbnails.");

                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        MediaThumbnailPrecacheStatus = ResourceProvider.GetString("LOCAnikiHelperThumbnailGenerationFailed");
                    }), DispatcherPriority.Background);
                }
                finally
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        MediaThumbnailPrecacheLoading = false;
                    }), DispatcherPriority.Background);
                }
            });
        }

        public async Task RefreshMediaGalleryLibraryAsync()
        {
            try
            {
                await GenerateAllMediaThumbnailsAsync();

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    LoadHubLatestMediaFromCache();
                    LoadHubAchievementMemoriesFromCache();
                    LoadMediaGalleryGamesFromCache();
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to refresh media gallery library.");
            }
        }

        public Task GenerateMediaThumbnailsForGameAsync(Guid gameId, string gameName = "")
        {
            if (gameId == Guid.Empty)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                try
                {
                    plugin.PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                    {
                        progress.IsIndeterminate = true;
                        progress.Text = ResourceProvider.GetString("LOCAnikiHelperScanningMediaForThisGame");

                        var loadedItems = LoadUnifiedMediaItemsForGame(gameId);

                        var imageItems = loadedItems
                            .Where(x => x != null)
                            .Where(x => !x.IsVideo)
                            .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                            .Where(x => File.Exists(x.FilePath))
                            .ToList();

                        progress.IsIndeterminate = false;
                        progress.ProgressMaxValue = imageItems.Count;
                        progress.CurrentProgressValue = 0;

                        if (mediaThumbnailService == null)
                        {
                            mediaThumbnailService = new AnikiMediaThumbnailService(plugin.GetPluginUserDataPath(), logger);
                        }

                        for (int i = 0; i < imageItems.Count; i++)
                        {
                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                progress.Text = ResourceProvider.GetString("LOCAnikiHelperThumbnailGenerationCancelled");
                                break;
                            }

                            var item = imageItems[i];
                            var done = i + 1;

                            progress.CurrentProgressValue = done;
                            progress.Text = string.Format(
                                ResourceProvider.GetString("LOCAnikiHelperGeneratingThumbnailsForGame"),
                                gameName,
                                done,
                                imageItems.Count
                            );

                            try
                            {
                                mediaThumbnailService.GetOrCreateThumbnail(item);
                            }
                            catch (Exception ex)
                            {
                                logger?.Debug(ex, "[AnikiHelper] Failed to generate media thumbnail for selected game.");
                            }
                        }

                        if (!progress.CancelToken.IsCancellationRequested)
                        {
                            progress.Text = ResourceProvider.GetString("LOCAnikiHelperUpdatingMediaCache");

                            if (screenshotMediaCacheService == null)
                            {
                                screenshotMediaCacheService = new ScreenshotMediaCacheService(
                                    plugin.PlayniteApi,
                                    plugin.GetPluginUserDataPath(),
                                    logger
                                );
                            }

                            var itemsWithThumbnails = ApplyThumbnailsToMediaItems(loadedItems);

                            screenshotMediaCacheService.UpdateGameInCaches(gameId, itemsWithThumbnails);
                            screenshotMediaCacheService.RebuildMemoriesForGame(gameId, itemsWithThumbnails);

                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                LoadHubLatestMediaFromCache();
                                LoadHubMemoryFromCache();
                                LoadMediaGalleryGamesFromCache();
                            }), DispatcherPriority.Background);

                            progress.Text = ResourceProvider.GetString("LOCAnikiHelperThumbnailsGenerated");
                        }
                    },
                    new GlobalProgressOptions(ResourceProvider.GetString("LOCAnikiHelperGeneratingGameThumbnails"))
                    {
                        IsIndeterminate = false,
                        Cancelable = true
                    });
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to generate thumbnails for selected game.");
                }
            });
        }

        public Task RefreshStoppedGameMediaSilentAsync(Guid gameId, int delayMs = 6000, DateTime? sessionStart = null, DateTime? sessionEnd = null)
        {
            if (gameId == Guid.Empty)
            {
                return Task.CompletedTask;
            }

            lock (stoppedGameMediaRefreshRunning)
            {
                if (stoppedGameMediaRefreshRunning.Contains(gameId))
                {
                    return Task.CompletedTask;
                }

                stoppedGameMediaRefreshRunning.Add(gameId);
            }

            return Task.Run(async () =>
            {
                try
                {
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs);
                    }

                    if (screenshotsVisualizerReader == null)
                    {
                        screenshotsVisualizerReader = new ScreenshotsVisualizerReader(
                            plugin.PlayniteApi,
                            logger
                        );
                    }

                    var minimumVisualizerRefreshDate = sessionEnd.HasValue
                        ? sessionEnd.Value.AddSeconds(-2)
                        : DateTime.MinValue;

                    var visualizerStampBeforeFirstPass =
                        screenshotsVisualizerReader.GetGameDataRefreshStamp(gameId);

                    var visualizerWasStillRefreshing =
                        sessionEnd.HasValue
                        && screenshotsVisualizerReader.IsAvailable()
                        && (
                            !visualizerStampBeforeFirstPass.HasValue
                            || visualizerStampBeforeFirstPass.Value < minimumVisualizerRefreshDate
                        );

                    // Premier passage après le délai habituel.
                    RefreshStoppedGameMediaCacheOnce(
                        gameId,
                        sessionStart,
                        sessionEnd
                    );

                    if (visualizerWasStillRefreshing)
                    {
                        logger?.Debug(
                            "[AnikiHelper] Screenshots Visualizer data is still stale after game stop. Waiting for its refresh."
                        );

                        var timeoutAt = DateTime.UtcNow.AddSeconds(45);
                        var visualizerRefreshCompleted = false;

                        while (DateTime.UtcNow < timeoutAt)
                        {
                            await Task.Delay(1000);

                            var currentStamp =
                                screenshotsVisualizerReader.GetGameDataRefreshStamp(gameId);

                            if (currentStamp.HasValue
                                && currentStamp.Value >= minimumVisualizerRefreshDate)
                            {
                                visualizerRefreshCompleted = true;
                                break;
                            }
                        }

                        if (visualizerRefreshCompleted)
                        {
                            logger?.Debug(
                                "[AnikiHelper] Screenshots Visualizer refresh completed. Updating Aniki media cache again."
                            );

                            RefreshStoppedGameMediaCacheOnce(
                                gameId,
                                sessionStart,
                                sessionEnd
                            );
                        }
                        else
                        {
                            logger?.Debug(
                                "[AnikiHelper] Timed out while waiting for Screenshots Visualizer refresh."
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warn(
                        ex,
                        "[AnikiHelper] Silent media refresh after game stopped failed."
                    );
                }
                finally
                {
                    lock (stoppedGameMediaRefreshRunning)
                    {
                        stoppedGameMediaRefreshRunning.Remove(gameId);
                    }
                }
            });
        }

        private void RefreshStoppedGameMediaCacheOnce(
    Guid gameId,
    DateTime? sessionStart,
    DateTime? sessionEnd)
        {
            var items = LoadUnifiedMediaItemsForGame(gameId);

            if (mediaThumbnailService == null)
            {
                mediaThumbnailService = new AnikiMediaThumbnailService(
                    plugin.GetPluginUserDataPath(),
                    logger
                );
            }

            foreach (var item in items.Where(x =>
                x != null
                && !x.IsVideo
                && !HasValidProviderThumbnail(x)))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.FilePath)
                        && File.Exists(item.FilePath))
                    {
                        mediaThumbnailService.GetOrCreateThumbnail(item);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Debug(
                        ex,
                        "[AnikiHelper] Failed to generate thumbnail during silent stopped-game refresh."
                    );
                }
            }

            var itemsWithThumbnails = ApplyThumbnailsToMediaItems(items);

            if (screenshotMediaCacheService == null)
            {
                screenshotMediaCacheService = new ScreenshotMediaCacheService(
                    plugin.PlayniteApi,
                    plugin.GetPluginUserDataPath(),
                    logger
                );
            }

            screenshotMediaCacheService.UpdateGameInCaches(
                gameId,
                itemsWithThumbnails
            );

            if (sessionStart.HasValue && sessionEnd.HasValue)
            {
                screenshotMediaCacheService.UpdateMemoryFromSession(
                    gameId,
                    itemsWithThumbnails,
                    sessionStart.Value,
                    sessionEnd.Value
                );
            }

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                LoadHubLatestMediaFromCache();
                LoadHubMemoryFromCache();
                LoadMediaGalleryGamesFromCache();
            }), DispatcherPriority.Background);
        }

        public void RefreshCurrentGameMediaFromSelectedGame()
        {
            try
            {
                var game = plugin?.PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (game == null)
                {
                    CurrentGameMediaItems.Clear();
                    VisibleCurrentGameMediaItems.Clear();
                    CurrentGameMediaLoadedCount = 0;
                    CurrentGameMediaCanLoadMore = false;
                    HasCurrentGameMedia = false;
                    return;
                }

                RefreshCurrentGameMediaForGame(game.Id);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to refresh current game media.");
            }
        }

        public void LoadHubLatestMediaFromCache()
        {
            try
            {
                if (screenshotMediaCacheService == null)
                {
                    screenshotMediaCacheService = new ScreenshotMediaCacheService(
                        plugin.PlayniteApi,
                        plugin.GetPluginUserDataPath(),
                        logger
                    );
                }

                var items = screenshotMediaCacheService.LoadLatestMediaCache();

                ReplaceMediaCollection(HubLatestMediaItems, items);

                OnPropertyChanged(nameof(HubLatestMediaItems));
                OnPropertyChanged(nameof(HasHubLatestMedia));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load hub latest media cache.");

                HubLatestMediaItems.Clear();
                OnPropertyChanged(nameof(HubLatestMediaItems));
                OnPropertyChanged(nameof(HasHubLatestMedia));
            }
        }

        private static string NormalizeSettingText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeExternalPath(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace("\\", "/");
        }

        private string ResolveExternalImagePath(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return string.Empty;
                }

                var normalized = NormalizeExternalPath(imagePath);

                if (Path.IsPathRooted(normalized) && File.Exists(normalized))
                {
                    return normalized;
                }

                return File.Exists(normalized) ? normalized : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void EnsureHubCurrentPageInRange()
        {
            if (HubCurrentPage > HubMaxPage)
            {
                HubCurrentPage = HubMaxPage;
                return;
            }

            OnPropertyChanged(nameof(HubMaxPage));
            NotifyHubPageStateProperties();
        }

        private void RefreshHubApps()
        {
            var existingItems = HubAppItems?.ToList() ?? new List<AnikiOverlayAppItem>();

            try
            {
                var newItems = new List<AnikiOverlayAppItem>();

                var slots = new[]
                {
                    new { ToolName = HubAppSlot1ToolName, BackgroundPath = HubAppSlot1BackgroundPath },
                    new { ToolName = HubAppSlot2ToolName, BackgroundPath = HubAppSlot2BackgroundPath },
                    new { ToolName = HubAppSlot3ToolName, BackgroundPath = HubAppSlot3BackgroundPath },
                    new { ToolName = HubAppSlot4ToolName, BackgroundPath = HubAppSlot4BackgroundPath }
                };

                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var slot in slots)
                {
                    if (string.IsNullOrWhiteSpace(slot.ToolName))
                    {
                        continue;
                    }

                    if (usedNames.Contains(slot.ToolName))
                    {
                        continue;
                    }

                    var source = OverlayAppItems?
                        .FirstOrDefault(x => string.Equals(x.Name, slot.ToolName, StringComparison.OrdinalIgnoreCase));

                    if (source == null)
                    {
                        source = existingItems
                            .FirstOrDefault(x => string.Equals(x.Name, slot.ToolName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (source == null)
                    {
                        // Keep the page visible when the saved slot exists, but Playnite Software Tools
                        // are not available yet. A later LoadOverlayApps() call will replace it with
                        // the real Software Tool item.
                        source = new AnikiOverlayAppItem
                        {
                            Name = slot.ToolName ?? string.Empty,
                            BackgroundImagePath = ResolveExternalImagePath(slot.BackgroundPath)
                        };
                    }

                    usedNames.Add(slot.ToolName);

                    newItems.Add(new AnikiOverlayAppItem
                    {
                        Name = source.Name ?? string.Empty,
                        IconPath = source.IconPath ?? string.Empty,
                        BackgroundImagePath = !string.IsNullOrWhiteSpace(slot.BackgroundPath)
                            ? ResolveExternalImagePath(slot.BackgroundPath)
                            : (source.BackgroundImagePath ?? string.Empty),
                        Path = source.Path ?? string.Empty,
                        Arguments = source.Arguments ?? string.Empty,
                        WorkingDir = source.WorkingDir ?? string.Empty,
                        IsScript = source.IsScript,
                        SourceApp = source.SourceApp
                    });
                }

                HubAppItems.Clear();
                foreach (var item in newItems)
                {
                    HubAppItems.Add(item);
                }

                HubAppsEmptyText = HasSelectedHubAppSlot
                    ? "Loading selected Software Tools..."
                    : "Select apps in Aniki Helper settings first.";
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to refresh Hub apps. Keeping previous Hub Apps state.");

                if (HubAppItems == null || HubAppItems.Count == 0)
                {
                    HubAppItems.Clear();
                    foreach (var item in existingItems)
                    {
                        HubAppItems.Add(item);
                    }
                }
            }

            OnPropertyChanged(nameof(HubAppItems));
            OnPropertyChanged(nameof(HasHubApps));
            OnPropertyChanged(nameof(ShowHubAppsPage));
            OnPropertyChanged(nameof(HubMaxPage));
            OnPropertyChanged(nameof(HubAppsEmptyText));
            NotifyHubPageStateProperties();
            EnsureHubCurrentPageInRange();
        }

        public void LoadOverlayApps()
        {
            try
            {
                // Playnite's public IGameDatabaseAPI does not expose SoftwareApps.
                // The native Fullscreen Tools window uses the internal GameDatabase.SoftwareApps
                // collection, so we read it through reflection to keep the overlay Apps view
                // available without referencing Playnite internals directly.
                var apps = GetSoftwareAppsForOverlay();

                if ((apps == null || apps.Count == 0) && (OverlayAppItems?.Count > 0 || HubAppItems?.Count > 0) && HasSelectedHubAppSlot)
                {
                    logger?.Warn("[AnikiHelper] Software Tools refresh returned 0 apps. Keeping the previous Hub Apps list to avoid hiding the Hub page during fullscreen startup.");
                    RefreshHubApps();
                    OnPropertyChanged(nameof(OverlayAppItems));
                    OnPropertyChanged(nameof(HasOverlayApps));
                    OnPropertyChanged(nameof(SoftwareToolNamesForSelection));
                    OnPropertyChanged(nameof(OverlayAppsEmptyText));
                    return;
                }

                var items = (apps ?? new List<AppSoftware>())
                    .Where(x => x != null)
                    .OrderBy(x => x.Name)
                    .Select(x => new AnikiOverlayAppItem
                    {
                        Name = x.Name ?? string.Empty,
                        IconPath = ResolveDatabaseFilePath(x.Icon),
                        BackgroundImagePath = string.Empty,
                        Path = x.Path ?? string.Empty,
                        Arguments = x.Arguments ?? string.Empty,
                        WorkingDir = x.WorkingDir ?? string.Empty,
                        IsScript = x.AppType == AppSoftwareType.Script,
                        SourceApp = x
                    })
                    .ToList();

                OverlayAppItems.Clear();
                foreach (var item in items)
                {
                    OverlayAppItems.Add(item);
                }

                SoftwareToolNamesForSelection.Clear();
                SoftwareToolNamesForSelection.Add(string.Empty);
                foreach (var name in items.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    SoftwareToolNamesForSelection.Add(name);
                }

                OverlayAppsEmptyText = "No apps configured. Add Software Tools in Playnite first.";
                RefreshHubApps();

                OnPropertyChanged(nameof(OverlayAppItems));
                OnPropertyChanged(nameof(HasOverlayApps));
                OnPropertyChanged(nameof(SoftwareToolNamesForSelection));
                OnPropertyChanged(nameof(OverlayAppsEmptyText));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load overlay apps. Keeping previous Hub Apps state when possible.");

                if ((OverlayAppItems?.Count > 0 || HubAppItems?.Count > 0) && HasSelectedHubAppSlot)
                {
                    RefreshHubApps();
                    OnPropertyChanged(nameof(OverlayAppItems));
                    OnPropertyChanged(nameof(HasOverlayApps));
                    OnPropertyChanged(nameof(SoftwareToolNamesForSelection));
                    OnPropertyChanged(nameof(OverlayAppsEmptyText));
                    return;
                }

                OverlayAppItems.Clear();
                SoftwareToolNamesForSelection.Clear();
                SoftwareToolNamesForSelection.Add(string.Empty);
                OverlayAppsEmptyText = "No apps configured. Add Software Tools in Playnite first.";
                RefreshHubApps();

                OnPropertyChanged(nameof(OverlayAppItems));
                OnPropertyChanged(nameof(HasOverlayApps));
                OnPropertyChanged(nameof(SoftwareToolNamesForSelection));
                OnPropertyChanged(nameof(OverlayAppsEmptyText));
            }
        }

        private List<AppSoftware> GetSoftwareAppsForOverlay()
        {
            try
            {
                var dbApi = plugin?.PlayniteApi?.Database;
                if (dbApi == null)
                {
                    return new List<AppSoftware>();
                }

                object internalDatabase = null;
                var dbApiType = dbApi.GetType();

                var databaseField = dbApiType.GetField("database", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (databaseField != null)
                {
                    internalDatabase = databaseField.GetValue(dbApi);
                }

                if (internalDatabase == null)
                {
                    var databaseProperty = dbApiType.GetProperty("Database", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (databaseProperty != null)
                    {
                        internalDatabase = databaseProperty.GetValue(dbApi, null);
                    }
                }

                if (internalDatabase == null)
                {
                    logger?.Warn("[AnikiHelper] Cannot load overlay apps: internal Playnite database object not found.");
                    return new List<AppSoftware>();
                }

                var softwareAppsProperty = internalDatabase.GetType().GetProperty("SoftwareApps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var softwareApps = softwareAppsProperty?.GetValue(internalDatabase, null);
                if (softwareApps == null)
                {
                    logger?.Warn("[AnikiHelper] Cannot load overlay apps: SoftwareApps collection not found.");
                    return new List<AppSoftware>();
                }

                var result = new List<AppSoftware>();

                if (softwareApps is System.Collections.IEnumerable enumerable)
                {
                    foreach (var entry in enumerable)
                    {
                        if (entry is AppSoftware app)
                        {
                            result.Add(app);
                        }
                    }
                }

                if (result.Count == 0)
                {
                    var itemsProperty = softwareApps.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var items = itemsProperty?.GetValue(softwareApps, null) as System.Collections.IEnumerable;
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            var valueProperty = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                            if (valueProperty?.GetValue(item, null) is AppSoftware app)
                            {
                                result.Add(app);
                            }
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to read Playnite Software Tools through reflection.");
                return new List<AppSoftware>();
            }
        }

        private string ResolveDatabaseFilePath(string databaseFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(databaseFilePath))
                {
                    return string.Empty;
                }

                if (Path.IsPathRooted(databaseFilePath) && File.Exists(databaseFilePath))
                {
                    return databaseFilePath;
                }

                var fullPath = plugin?.PlayniteApi?.Database?.GetFullFilePath(databaseFilePath);
                return string.IsNullOrWhiteSpace(fullPath) ? databaseFilePath : fullPath;
            }
            catch
            {
                return databaseFilePath ?? string.Empty;
            }
        }

        private void OpenOverlayApp(AnikiOverlayAppItem item)
        {
            try
            {
                var app = item?.SourceApp;
                if (app == null)
                {
                    StartOverlayAppFromPathFallback(item);
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    StartSoftwareToolViaMainModel(app);
                    return;
                }

                dispatcher.BeginInvoke(new Action(() => StartSoftwareToolViaMainModel(app)), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to open overlay app.");
            }
        }

        private void StartOverlayAppFromPathFallback(AnikiOverlayAppItem item)
        {
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Path))
                {
                    logger?.Warn("[AnikiHelper] Cannot start Hub app: Software Tool source and path are missing.");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = item.Path,
                    Arguments = item.Arguments ?? string.Empty,
                    UseShellExecute = true
                };

                if (!string.IsNullOrWhiteSpace(item.WorkingDir) && Directory.Exists(item.WorkingDir))
                {
                    startInfo.WorkingDirectory = item.WorkingDir;
                }

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to start Hub app from path fallback.");
            }
        }

        private void StartSoftwareToolViaMainModel(AppSoftware app)
        {
            try
            {
                if (app == null)
                {
                    return;
                }

                var mainWindow = Application.Current?.MainWindow;
                var dataContext = mainWindow?.DataContext;
                if (dataContext == null)
                {
                    logger?.Warn("[AnikiHelper] Cannot start software tool: Playnite DataContext not found.");
                    return;
                }

                var method = dataContext.GetType().GetMethod("StartSoftwareTool", new[] { typeof(AppSoftware) });
                if (method == null)
                {
                    method = dataContext.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .FirstOrDefault(m =>
                            string.Equals(m.Name, "StartSoftwareTool", StringComparison.Ordinal) &&
                            m.GetParameters().Length == 1 &&
                            m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(AppSoftware)));
                }

                if (method == null)
                {
                    logger?.Warn("[AnikiHelper] Cannot start software tool: StartSoftwareTool method not found.");
                    return;
                }

                method.Invoke(dataContext, new object[] { app });
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to start software tool from overlay.");
            }
        }

        public void LoadOverlayAchievements(Guid gameId, string gameName)
        {
            try
            {
                OverlayAchievementsTitle = "Achievements";

                if (gameId == Guid.Empty)
                {
                    OverlayAchievementItems.Clear();
                    OverlayAchievementsSubtitle = "No game is currently running.";
                    OverlayAchievementsEmptyText = "Launch a game to view its achievements.";
                    OverlayAchievementsProgressText = string.Empty;
                    OverlayAchievementsUnlockedCount = 0;
                    OverlayAchievementsTotalCount = 0;
                    NotifyOverlayAchievementsChanged();
                    return;
                }

                var reader = new PlayniteAchievementsReader(plugin.PlayniteApi, logger);
                var achievements = reader.LoadAchievementsForGame(gameId)
                    .Where(x => x != null)
                    .ToList();

                var finalGameName = string.IsNullOrWhiteSpace(gameName) ? "current game" : gameName;
                OverlayAchievementsSubtitle = finalGameName;
                OverlayAchievementsEmptyText = "No PlayniteAchievements data found for " + finalGameName + ".";

                ReplaceAchievementCollection(OverlayAchievementItems, SortOverlayAchievements(achievements));

                OverlayAchievementsTotalCount = achievements.Count;
                OverlayAchievementsUnlockedCount = achievements.Count(x => x.Unlocked);
                OverlayAchievementsProgressText = OverlayAchievementsTotalCount > 0
                    ? OverlayAchievementsUnlockedCount + " / " + OverlayAchievementsTotalCount
                    : string.Empty;

                NotifyOverlayAchievementsChanged();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load overlay achievements.");

                OverlayAchievementItems.Clear();
                OverlayAchievementsTitle = "Achievements";
                OverlayAchievementsSubtitle = string.Empty;
                OverlayAchievementsEmptyText = "No PlayniteAchievements data found.";
                OverlayAchievementsProgressText = string.Empty;
                OverlayAchievementsUnlockedCount = 0;
                OverlayAchievementsTotalCount = 0;
                NotifyOverlayAchievementsChanged();
            }
        }

        private void ToggleOverlayAchievementsSort()
        {
            OverlayAchievementsSortMode = string.Equals(OverlayAchievementsSortMode, "LockedFirst", StringComparison.OrdinalIgnoreCase)
                ? "LastUnlocked"
                : "LockedFirst";
        }

        private void ApplyOverlayAchievementsSort()
        {
            if (OverlayAchievementItems == null || OverlayAchievementItems.Count <= 1)
            {
                return;
            }

            ReplaceAchievementCollection(OverlayAchievementItems, SortOverlayAchievements(OverlayAchievementItems));
            OnPropertyChanged(nameof(OverlayAchievementItems));
            OnPropertyChanged(nameof(HasOverlayAchievements));
        }

        private List<AnikiOverlayAchievementItem> SortOverlayAchievements(IEnumerable<AnikiOverlayAchievementItem> items)
        {
            var list = items == null
                ? new List<AnikiOverlayAchievementItem>()
                : items.Where(x => x != null).ToList();

            if (string.Equals(OverlayAchievementsSortMode, "LockedFirst", StringComparison.OrdinalIgnoreCase))
            {
                return list
                    .OrderBy(x => x.Unlocked ? 1 : 0)
                    .ThenByDescending(x => x.UnlockDate ?? DateTime.MinValue)
                    .ThenBy(x => x.Title ?? string.Empty)
                    .ToList();
            }

            return list
                .OrderBy(x => x.Unlocked ? 0 : 1)
                .ThenByDescending(x => x.UnlockDate ?? DateTime.MinValue)
                .ThenBy(x => x.Title ?? string.Empty)
                .ToList();
        }

        private void NotifyOverlayAchievementsSortChanged()
        {
            OnPropertyChanged(nameof(OverlayAchievementsSortMode));
            OnPropertyChanged(nameof(OverlayAchievementsSortButtonText));
            OnPropertyChanged(nameof(OverlayAchievementsSortDescription));
        }

        private void NotifyOverlayAchievementsChanged()
        {
            OnPropertyChanged(nameof(OverlayAchievementItems));
            OnPropertyChanged(nameof(HasOverlayAchievements));
            OnPropertyChanged(nameof(OverlayAchievementsTitle));
            OnPropertyChanged(nameof(OverlayAchievementsSubtitle));
            OnPropertyChanged(nameof(OverlayAchievementsEmptyText));
            OnPropertyChanged(nameof(OverlayAchievementsProgressText));
            OnPropertyChanged(nameof(OverlayAchievementsUnlockedCount));
            OnPropertyChanged(nameof(OverlayAchievementsTotalCount));
            NotifyOverlayAchievementsSortChanged();
        }

        private void ReplaceAchievementCollection(ObservableCollection<AnikiOverlayAchievementItem> target, IEnumerable<AnikiOverlayAchievementItem> items)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();

            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                if (item != null)
                {
                    target.Add(item);
                }
            }
        }

        private async Task RefreshOverlayLastCapturesAsync()
        {
            Guid gameId;
            string gameName;

            lock (overlayLastCapturesRefreshLock)
            {
                if (overlayLastCapturesRefreshRunning)
                {
                    return;
                }

                gameId = overlayLastCapturesGameId;
                gameName = overlayLastCapturesGameName;

                if (gameId == Guid.Empty)
                {
                    return;
                }

                overlayLastCapturesRefreshRunning = true;
            }

            SetOverlayLastCapturesRefreshing(true);

            try
            {
                if (screenshotsVisualizerReader == null)
                {
                    screenshotsVisualizerReader = new ScreenshotsVisualizerReader(
                        plugin.PlayniteApi,
                        logger
                    );
                }

                var refreshed = await screenshotsVisualizerReader.RefreshGameDataAsync(gameId);
                if (!refreshed)
                {
                    return;
                }

                await Task.Run(() =>
                {
                    RefreshStoppedGameMediaCacheOnce(
                        gameId,
                        null,
                        null
                    );
                });

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    LoadOverlayLastCaptures(gameId, gameName);
                }
                else
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        LoadOverlayLastCaptures(gameId, gameName);
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    "[AnikiHelper] Failed to refresh overlay captures from Screenshots Visualizer."
                );
            }
            finally
            {
                lock (overlayLastCapturesRefreshLock)
                {
                    overlayLastCapturesRefreshRunning = false;
                }

                SetOverlayLastCapturesRefreshing(false);
            }
        }

        private void SetOverlayLastCapturesRefreshing(bool isRefreshing)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsRefreshingOverlayLastCaptures = isRefreshing;
                    }), DispatcherPriority.Background);

                    return;
                }

                IsRefreshingOverlayLastCaptures = isRefreshing;
            }
            catch
            {
            }
        }

        public void LoadOverlayLastCaptures(Guid gameId, string gameName)
        {
            overlayLastCapturesGameId = gameId;
            overlayLastCapturesGameName = gameName ?? string.Empty;
            OnPropertyChanged(nameof(CanRefreshOverlayLastCaptures));
            OnPropertyChanged(nameof(OverlayLastCapturesRefreshButtonVisibility));
            OnPropertyChanged(nameof(OverlayLastCapturesRefreshButtonText));

            try
            {
                if (screenshotMediaCacheService == null)
                {
                    screenshotMediaCacheService = new ScreenshotMediaCacheService(
                        plugin.PlayniteApi,
                        plugin.GetPluginUserDataPath(),
                        logger
                    );
                }

                OverlayLastCapturesTitle = "Last Captures";

                List<AnikiMediaItem> items;

                if (gameId != Guid.Empty)
                {
                    var finalGameName = string.IsNullOrWhiteSpace(gameName) ? "current game" : gameName;
                    OverlayLastCapturesSubtitle = $"Latest captures for {finalGameName}.";
                    OverlayLastCapturesEmptyText = $"No captures found for {finalGameName}.";

                    items = LoadUnifiedMediaItemsForGame(gameId)
                        .Where(x => x != null)
                        .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                        .Where(x => File.Exists(x.FilePath))
                        .OrderByDescending(x => x.CaptureDate)
                        .Take(3)
                        .ToList();

                    items = ApplyThumbnailsToMediaItems(items);
                }
                else
                {
                    OverlayLastCapturesSubtitle = "Latest captures from your library.";
                    OverlayLastCapturesEmptyText = "No captures found. Refresh the media gallery first.";

                    items = screenshotMediaCacheService.LoadLatestMediaCache()
                        .Where(x => x != null)
                        .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                        .Where(x => File.Exists(x.FilePath))
                        .OrderByDescending(x => x.CaptureDate)
                        .Take(3)
                        .ToList();

                    items = ApplyThumbnailsToMediaItems(items);
                }

                ReplaceMediaCollection(OverlayLastCaptureItems, items);

                OnPropertyChanged(nameof(OverlayLastCaptureItems));
                OnPropertyChanged(nameof(HasOverlayLastCaptures));
                OnPropertyChanged(nameof(OverlayLastCapturesTitle));
                OnPropertyChanged(nameof(OverlayLastCapturesSubtitle));
                OnPropertyChanged(nameof(OverlayLastCapturesEmptyText));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load overlay last captures.");

                OverlayLastCaptureItems.Clear();
                OverlayLastCapturesTitle = "Last Captures";
                OverlayLastCapturesSubtitle = string.Empty;
                OverlayLastCapturesEmptyText = "No captures found.";

                OnPropertyChanged(nameof(OverlayLastCaptureItems));
                OnPropertyChanged(nameof(HasOverlayLastCaptures));
                OnPropertyChanged(nameof(OverlayLastCapturesTitle));
                OnPropertyChanged(nameof(OverlayLastCapturesSubtitle));
                OnPropertyChanged(nameof(OverlayLastCapturesEmptyText));
            }
        }

        public void LoadHubMemoryFromCache()
        {
            try
            {
                if (screenshotMediaCacheService == null)
                {
                    screenshotMediaCacheService = new ScreenshotMediaCacheService(
                        plugin.PlayniteApi,
                        plugin.GetPluginUserDataPath(),
                        logger
                    );
                }

                var memories = screenshotMediaCacheService.LoadMemoriesCache()
                    .Where(x => x != null)
                    .Where(x => x.Screenshots != null && x.Screenshots.Count > 0)
                    .OrderByDescending(x => x.MemoryDate)
                    .Take(20)
                    .ToList();

                var selectedMemory = memories
                    .OrderBy(x => Guid.NewGuid())
                    .FirstOrDefault();

                if (selectedMemory != null)
                {
                    var memoryItems = selectedMemory.Screenshots
                        .Where(x => x != null)
                        .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                        .Where(x => File.Exists(x.FilePath))
                        .Take(4)
                        .ToList();

                    ReplaceMediaCollection(HubMemoryItems, memoryItems);
                    HubMemorySubtitle = $"{selectedMemory.GameName} • {selectedMemory.MemoryDate:dd/MM/yyyy}";
                }
                else
                {
                    var fallbackItems = screenshotMediaCacheService.LoadLatestMediaCache()
                        .Where(x => x != null)
                        .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                        .Where(x => File.Exists(x.FilePath))
                        .Take(4)
                        .ToList();

                    ReplaceMediaCollection(HubMemoryItems, fallbackItems);
                    HubMemorySubtitle = string.Empty;
                }

                OnPropertyChanged(nameof(HubMemoryItems));
                OnPropertyChanged(nameof(HasHubMemory));
                OnPropertyChanged(nameof(HubMemorySubtitle));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load hub memory cache.");

                HubMemoryItems.Clear();
                HubMemorySubtitle = string.Empty;

                OnPropertyChanged(nameof(HubMemoryItems));
                OnPropertyChanged(nameof(HasHubMemory));
                OnPropertyChanged(nameof(HubMemorySubtitle));
            }
        }

        public void LoadHubAchievementMemoriesFromCache()
        {
            try
            {
                if (achievementMemoriesCacheService == null)
                {
                    achievementMemoriesCacheService = new AchievementMemoriesCacheService(
                        plugin.GetPluginUserDataPath(),
                        logger
                    );
                }

                var allItems = achievementMemoriesCacheService.Load()
                    .Where(x => x != null)
                    .Where(x => x.UnlockDate != DateTime.MinValue)
                    .ToList();

                var groups = allItems
                    .GroupBy(x => new { x.UnlockDate.Year, x.UnlockDate.Month })
                    .Where(g => g.Count() >= 4)
                    .ToList();

                var selectedGroup = groups
                    .OrderBy(x => Guid.NewGuid())
                    .FirstOrDefault();

                var selected = new List<AnikiAchievementMemoryItem>();

                if (selectedGroup != null)
                {
                    var monthItems = selectedGroup
                        .Where(x => x != null)
                        .ToList();

                    selected = monthItems
                        .GroupBy(x => x.GameId)
                        .Select(g => g
                            .OrderBy(x => x.Percent ?? double.MaxValue)
                            .ThenByDescending(x => x.UnlockDate)
                            .First())
                        .OrderBy(x => x.Percent ?? double.MaxValue)
                        .ThenByDescending(x => x.UnlockDate)
                        .Take(4)
                        .ToList();

                    if (selected.Count < 4)
                    {
                        var alreadySelected = new HashSet<string>(
                            selected.Select(x => $"{x.GameId}|{x.Title}|{x.UnlockDate:O}"),
                            StringComparer.OrdinalIgnoreCase
                        );

                        var fillers = monthItems
                            .OrderBy(x => x.Percent ?? double.MaxValue)
                            .ThenByDescending(x => x.UnlockDate)
                            .Where(x => !alreadySelected.Contains($"{x.GameId}|{x.Title}|{x.UnlockDate:O}"))
                            .Take(4 - selected.Count)
                            .ToList();

                        selected.AddRange(fillers);
                    }
                }

                var period = string.Empty;

                if (selectedGroup != null)
                {
                    var date = new DateTime(selectedGroup.Key.Year, selectedGroup.Key.Month, 1);
                    var monthName = date.ToString("MMMM");
                    monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);

                    period = $"{monthName} {selectedGroup.Key.Year}";
                }

                if (playniteAchievementsReader == null)
                {
                    playniteAchievementsReader = new PlayniteAchievementsReader(plugin.PlayniteApi, logger);
                }

                foreach (var item in selected)
                {
                    playniteAchievementsReader.RefreshDisplayData(item);
                }

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    HubAchievementMemoryItems.Clear();

                    foreach (var item in selected)
                    {
                        HubAchievementMemoryItems.Add(item);
                    }

                    HubAchievementMemoryPeriod = period;

                    OnPropertyChanged(nameof(HubAchievementMemoryItems));
                    OnPropertyChanged(nameof(HasHubAchievementMemory));
                    OnPropertyChanged(nameof(HubAchievementMemoryPeriod));
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load achievement memories cache.");

                HubAchievementMemoryItems.Clear();
                HubAchievementMemoryPeriod = string.Empty;

                OnPropertyChanged(nameof(HubAchievementMemoryItems));
                OnPropertyChanged(nameof(HasHubAchievementMemory));
                OnPropertyChanged(nameof(HubAchievementMemoryPeriod));
            }
        }

        private void LoadRarestPlayniteAchievementAllTimeFromCache()
        {
            try
            {
                if (rarestAchievementCacheService == null)
                {
                    rarestAchievementCacheService = new RarestAchievementCacheService(
                        plugin.GetPluginUserDataPath(),
                        logger
                    );
                }

                RarestPlayniteAchievementAllTime = rarestAchievementCacheService.Load();

                OnPropertyChanged(nameof(RarestPlayniteAchievementAllTime));
                OnPropertyChanged(nameof(HasRarestPlayniteAchievementAllTime));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load rarest achievement cache.");
            }
        }

        public void LoadHubAchievementMemoriesFromCacheWhenDatabaseReady()
        {
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 30; i++)
                    {
                        var gameCount = plugin?.PlayniteApi?.Database?.Games?.Count ?? 0;

                        if (gameCount > 0)
                        {
                            LoadHubAchievementMemoriesFromCache();
                            LoadRarestPlayniteAchievementAllTimeFromCache();
                            return;
                        }

                        await Task.Delay(1000);
                    }

                    LoadHubAchievementMemoriesFromCache();
                    LoadRarestPlayniteAchievementAllTimeFromCache();
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to load achievement memories when database ready.");
                }
            });
        }

        public void EnsureAchievementMemoriesCacheExists()
        {
            try
            {
                if (achievementMemoriesCacheService == null)
                {
                    achievementMemoriesCacheService = new AchievementMemoriesCacheService(
                        plugin.GetPluginUserDataPath(),
                        logger
                    );
                }

                if (!achievementMemoriesCacheService.NeedsRebuildForCurrentVersion())
                {
                    return;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < 30; i++)
                        {
                            var gameCount = plugin?.PlayniteApi?.Database?.Games?.Count ?? 0;

                            if (gameCount > 0)
                            {
                                logger?.Info(
                                    "[AnikiHelper] Achievement memories cache is missing or outdated. " +
                                    "Rebuilding cache in background..."
                                );

                                await Task.Delay(5000);

                                await RefreshAchievementMemoriesAsync();
                                return;
                            }

                            await Task.Delay(1000);
                        }

                        logger?.Warn(
                            "[AnikiHelper] Playnite database was not ready after 30 seconds. " +
                            "Achievement memories cache rebuild skipped."
                        );
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper] Failed to create or repair achievement memories cache.");
                    }
                });
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to ensure achievement memories cache.");
            }
        }

        private string Loc(string key, string fallback)
        {
            try
            {
                var value = Application.Current?.TryFindResource(key) as string;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private void SetAchievementMemoriesRefreshState(bool isRefreshing, string status)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsRefreshingAchievementMemories = isRefreshing;
                        AchievementMemoriesRefreshStatus = status ?? string.Empty;
                    }), DispatcherPriority.Background);

                    return;
                }

                IsRefreshingAchievementMemories = isRefreshing;
                AchievementMemoriesRefreshStatus = status ?? string.Empty;
            }
            catch
            {
            }
        }

        public Task RefreshAchievementMemoriesWithProgressAsync()
        {
            lock (achievementMemoriesRefreshLock)
            {
                if (achievementMemoriesRefreshRunning)
                {
                    return Task.CompletedTask;
                }

                achievementMemoriesRefreshRunning = true;
            }

            SetAchievementMemoriesRefreshState(true, "Scanning achievements...");

            return Task.Run(() =>
            {
                try
                {
                    plugin.PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                    {
                        progress.IsIndeterminate = true;
                        progress.Text = "Scanning achievements...";

                        if (playniteAchievementsReader == null)
                        {
                            playniteAchievementsReader = new PlayniteAchievementsReader(plugin.PlayniteApi, logger);
                        }

                        if (achievementMemoriesCacheService == null)
                        {
                            achievementMemoriesCacheService = new AchievementMemoriesCacheService(
                                plugin.GetPluginUserDataPath(),
                                logger
                            );
                        }

                        var items = playniteAchievementsReader.LoadAchievementMemories(5000);

                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            progress.Text = "Achievements cache rebuild cancelled.";

                            SetAchievementMemoriesRefreshState(
                                false,
                                "Achievements cache rebuild cancelled."
                            );

                            return;
                        }

                        progress.IsIndeterminate = false;
                        progress.ProgressMaxValue = 4;
                        progress.CurrentProgressValue = 1;
                        progress.Text = "Saving achievements cache...";

                        logger?.Info("[AnikiHelper] Achievement memories loaded: " + items.Count);

                        achievementMemoriesCacheService.Save(items, true);

                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            progress.Text = "Achievements cache rebuild cancelled.";

                            SetAchievementMemoriesRefreshState(
                                false,
                                "Achievements cache rebuild cancelled."
                            );

                            return;
                        }

                        progress.CurrentProgressValue = 2;
                        progress.Text = "Scanning rarest achievement...";

                        if (rarestAchievementCacheService == null)
                        {
                            rarestAchievementCacheService = new RarestAchievementCacheService(
                                plugin.GetPluginUserDataPath(),
                                logger
                            );
                        }

                        var rarestAchievement = playniteAchievementsReader.LoadRarestAchievementAllTime();

                        if (rarestAchievement != null)
                        {
                            rarestAchievementCacheService.Save(rarestAchievement);
                        }

                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            progress.Text = "Achievements cache rebuild cancelled.";

                            SetAchievementMemoriesRefreshState(
                                false,
                                "Achievements cache rebuild cancelled."
                            );

                            return;
                        }

                        progress.CurrentProgressValue = 3;
                        progress.Text = "Updating Hub achievement data...";

                        LoadHubAchievementMemoriesFromCache();
                        LoadRarestPlayniteAchievementAllTimeFromCache();

                        progress.CurrentProgressValue = 4;
                        progress.Text = "Achievements cache rebuilt.";

                        SetAchievementMemoriesRefreshState(
                            false,
                            "Achievements cache rebuilt: " + items.Count + " items."
                        );
                    },
                    new GlobalProgressOptions("Rebuilding achievements cache")
                    {
                        IsIndeterminate = true,
                        Cancelable = true
                    });
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to refresh achievement memories with progress dialog.");

                    SetAchievementMemoriesRefreshState(
                        false,
                        "Achievements cache rebuild failed."
                    );
                }
                finally
                {
                    lock (achievementMemoriesRefreshLock)
                    {
                        achievementMemoriesRefreshRunning = false;
                    }
                }
            });
        }

        public Task RefreshAchievementMemoriesAsync()
        {
            lock (achievementMemoriesRefreshLock)
            {
                if (achievementMemoriesRefreshRunning)
                {
                    return Task.CompletedTask;
                }

                achievementMemoriesRefreshRunning = true;
            }

            SetAchievementMemoriesRefreshState(
                true,
                Loc("AchievementCache_Status_Scanning", "Scanning achievements...")
            );

            return Task.Run(() =>
            {
                try
                {
                    if (playniteAchievementsReader == null)
                    {
                        playniteAchievementsReader = new PlayniteAchievementsReader(plugin.PlayniteApi, logger);
                    }

                    if (achievementMemoriesCacheService == null)
                    {
                        achievementMemoriesCacheService = new AchievementMemoriesCacheService(
                            plugin.GetPluginUserDataPath(),
                            logger
                        );
                    }

                    var items = playniteAchievementsReader.LoadAchievementMemories(5000);

                    logger?.Info("[AnikiHelper] Achievement memories loaded: " + items.Count);

                    achievementMemoriesCacheService.Save(items, true);

                    if (rarestAchievementCacheService == null)
                    {
                        rarestAchievementCacheService = new RarestAchievementCacheService(
                            plugin.GetPluginUserDataPath(),
                            logger
                        );
                    }

                    var rarestAchievement = playniteAchievementsReader.LoadRarestAchievementAllTime();

                    if (rarestAchievement != null)
                    {
                        rarestAchievementCacheService.Save(rarestAchievement);
                    }

                    LoadHubAchievementMemoriesFromCache();
                    LoadRarestPlayniteAchievementAllTimeFromCache();

                    SetAchievementMemoriesRefreshState(
                        false,
                        string.Format(
                            Loc("AchievementCache_Status_Done", "Achievements cache rebuilt: {0} items."),
                            items.Count
                        )
                    );
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to refresh achievement memories.");

                    SetAchievementMemoriesRefreshState(
                        false,
                        Loc("AchievementCache_Status_Failed", "Achievements cache rebuild failed.")
                    );
                }
                finally
                {
                    lock (achievementMemoriesRefreshLock)
                    {
                        achievementMemoriesRefreshRunning = false;
                    }
                }
            });
        }

        public Task RefreshAchievementMemoriesForGameAsync(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                try
                {
                    if (playniteAchievementsReader == null)
                    {
                        playniteAchievementsReader = new PlayniteAchievementsReader(plugin.PlayniteApi, logger);
                    }

                    if (achievementMemoriesCacheService == null)
                    {
                        achievementMemoriesCacheService = new AchievementMemoriesCacheService(
                            plugin.GetPluginUserDataPath(),
                            logger
                        );
                    }

                    var newItems = playniteAchievementsReader.LoadAchievementMemoriesForGame(gameId);

                    var existingItems = achievementMemoriesCacheService.Load()
                        .Where(x => x != null)
                        .Where(x => x.GameId != gameId)
                        .ToList();

                    existingItems.AddRange(newItems);

                    achievementMemoriesCacheService.Save(existingItems, false);

                    LoadHubAchievementMemoriesFromCache();
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to refresh achievement memories for game.");
                }
            });
        }

        private IEnumerable<AnikiMediaGameItem> SortMediaGalleryGames(IEnumerable<AnikiMediaGameItem> games)
        {
            var list = games ?? Enumerable.Empty<AnikiMediaGameItem>();

            switch (MediaGalleryGamesSortMode)
            {
                case "LatestCaptureAsc":
                    return list.OrderBy(x => x.LatestCaptureDate).ThenBy(x => x.GameName);

                case "MediaCountDesc":
                    return list.OrderByDescending(x => x.MediaCount).ThenBy(x => x.GameName);

                case "MediaCountAsc":
                    return list.OrderBy(x => x.MediaCount).ThenBy(x => x.GameName);

                case "GameNameAsc":
                    return list.OrderBy(x => x.GameName);

                case "GameNameDesc":
                    return list.OrderByDescending(x => x.GameName);

                case "LatestCaptureDesc":
                default:
                    return list.OrderByDescending(x => x.LatestCaptureDate).ThenBy(x => x.GameName);
            }
        }

        public void ApplyMediaGalleryGamesSort()
        {
            try
            {
                var sorted = SortMediaGalleryGames(MediaGalleryGames).ToList();

                ReplaceMediaGameCollection(MediaGalleryGames, sorted);

                OnPropertyChanged(nameof(MediaGalleryGames));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to apply media gallery games sort.");
            }
        }

        private void ReplaceMediaGameCollection(
            ObservableCollection<AnikiMediaGameItem> target,
            IEnumerable<AnikiMediaGameItem> items)
        {
            target.Clear();

            foreach (var item in items ?? Enumerable.Empty<AnikiMediaGameItem>())
            {
                target.Add(item);
            }
        }

        public void LoadMediaGalleryGamesFromCache()
        {
            try
            {
                if (screenshotMediaCacheService == null)
                {
                    screenshotMediaCacheService = new ScreenshotMediaCacheService(
                        plugin.PlayniteApi,
                        plugin.GetPluginUserDataPath(),
                        logger
                    );
                }

                var games = screenshotMediaCacheService.LoadGamesCache();

                ReplaceMediaGameCollection(MediaGalleryGames, SortMediaGalleryGames(games));

                OnPropertyChanged(nameof(MediaGalleryGames));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load media gallery games cache.");

                MediaGalleryGames.Clear();
                OnPropertyChanged(nameof(MediaGalleryGames));
            }
        }

        public void ClearCurrentGameMediaState()
        {
            try
            {
                var alreadyClear =
                    currentGameMediaActiveGameId == Guid.Empty &&
                    CurrentGameMediaItems.Count == 0 &&
                    VisibleCurrentGameMediaItems.Count == 0 &&
                    CurrentGameMediaLoadedCount == 0 &&
                    CurrentGameMediaCanLoadMore == false &&
                    CurrentGameMediaLoading == false &&
                    HasCurrentGameMedia == false;

                if (alreadyClear)
                {
                    return;
                }

                currentGameMediaLoadVersion++;
                currentGameMediaActiveGameId = Guid.Empty;
                currentGameMediaPageLoading = false;

                CurrentGameMediaItems.Clear();
                VisibleCurrentGameMediaItems.Clear();

                CurrentGameMediaLoadedCount = 0;
                CurrentGameMediaCanLoadMore = false;
                CurrentGameMediaLoading = false;
                HasCurrentGameMedia = false;

                OnPropertyChanged(nameof(CurrentGameMediaItems));
                OnPropertyChanged(nameof(VisibleCurrentGameMediaItems));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to clear current game media state.");
            }
        }

        public void PrepareCurrentGameMediaLoading()
        {
            try
            {
                currentGameMediaPageLoading = false;

                CurrentGameMediaLoading = true;

                CurrentGameMediaItems.Clear();
                VisibleCurrentGameMediaItems.Clear();

                CurrentGameMediaLoadedCount = 0;
                CurrentGameMediaCanLoadMore = false;
                HasCurrentGameMedia = false;

                OnPropertyChanged(nameof(CurrentGameMediaItems));
                OnPropertyChanged(nameof(VisibleCurrentGameMediaItems));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to prepare current game media loading.");
            }
        }

        public async Task OpenScreenshotsWindowForGameAsync(Guid gameId)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return;
                }

                PrepareCurrentGameMediaLoading();

                plugin?.OpenWindow("ScreenShotsThumbsWindowStyle");

                await Application.Current.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render
                );

                await Task.Delay(120);

                plugin?.HookScreenshotsLazyLoad();

                await RefreshCurrentGameMediaForGameAsync(gameId);
            }
            catch (Exception ex)
            {
                ClearCurrentGameMediaState();
                logger?.Warn(ex, "[AnikiHelper] Failed to open screenshots window for media game.");
            }
        }

        public async Task RefreshCurrentGameMediaFromSelectedGameAsync()
        {
            try
            {
                var game = plugin?.PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (game == null)
                {
                    ClearCurrentGameMediaState();
                    return;
                }

                await RefreshCurrentGameMediaForGameAsync(game.Id);
            }
            catch (Exception ex)
            {
                ClearCurrentGameMediaState();
                logger?.Warn(ex, "[AnikiHelper] Failed to load current game media async.");
            }
        }



        public async Task RefreshCurrentGameMediaForGameAsync(Guid gameId)
        {
            var requestVersion = ++currentGameMediaLoadVersion;
            currentGameMediaActiveGameId = gameId;

            try
            {
                if (gameId == Guid.Empty)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (requestVersion != currentGameMediaLoadVersion)
                        {
                            return;
                        }

                        ClearCurrentGameMediaState();
                    });

                    return;
                }

                CurrentGameMediaLoading = true;

                var items = await Task.Run(() =>
                {
                    var loadedItems = LoadUnifiedMediaItemsForGame(gameId);
                    return ApplyThumbnailsToMediaItems(loadedItems);
                });

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (requestVersion != currentGameMediaLoadVersion || currentGameMediaActiveGameId != gameId)
                    {
                        return;
                    }

                    ReplaceMediaCollection(CurrentGameMediaItems, items);

                    VisibleCurrentGameMediaItems.Clear();
                    CurrentGameMediaLoadedCount = 0;
                    CurrentGameMediaCanLoadMore = false;

                    HasCurrentGameMedia = CurrentGameMediaItems.Count > 0;

                    LoadMoreCurrentGameMediaItems();

                    OnPropertyChanged(nameof(CurrentGameMediaItems));
                    OnPropertyChanged(nameof(VisibleCurrentGameMediaItems));

                    CurrentGameMediaLoading = false;
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (requestVersion != currentGameMediaLoadVersion || currentGameMediaActiveGameId != gameId)
                    {
                        return;
                    }

                    CurrentGameMediaItems.Clear();
                    VisibleCurrentGameMediaItems.Clear();
                    CurrentGameMediaLoadedCount = 0;
                    CurrentGameMediaCanLoadMore = false;
                    HasCurrentGameMedia = false;
                    CurrentGameMediaLoading = false;
                });

                logger?.Warn(ex, "[AnikiHelper] Failed to load current game media by game id.");
            }
        }

        public void RefreshCurrentGameMediaForGame(Guid gameId)
        {
            try
            {
                CurrentGameMediaLoading = true;

                if (gameId == Guid.Empty)
                {
                    CurrentGameMediaItems.Clear();
                    VisibleCurrentGameMediaItems.Clear();
                    CurrentGameMediaLoadedCount = 0;
                    CurrentGameMediaCanLoadMore = false;
                    HasCurrentGameMedia = false;
                    return;
                }

                var items = LoadUnifiedMediaItemsForGame(gameId);

                ReplaceMediaCollection(CurrentGameMediaItems, ApplyThumbnailsToMediaItems(items));

                VisibleCurrentGameMediaItems.Clear();
                CurrentGameMediaLoadedCount = 0;
                CurrentGameMediaCanLoadMore = false;

                HasCurrentGameMedia = CurrentGameMediaItems.Count > 0;

                LoadMoreCurrentGameMediaItems();

                OnPropertyChanged(nameof(CurrentGameMediaItems));
                OnPropertyChanged(nameof(VisibleCurrentGameMediaItems));
            }
            catch (Exception ex)
            {
                CurrentGameMediaItems.Clear();
                VisibleCurrentGameMediaItems.Clear();
                CurrentGameMediaLoadedCount = 0;
                CurrentGameMediaCanLoadMore = false;
                HasCurrentGameMedia = false;

                logger?.Warn(ex, "[AnikiHelper] Failed to load current game media.");
            }
            finally
            {
                CurrentGameMediaLoading = false;
            }
        }

        private void ReplaceMediaCollection(ObservableCollection<AnikiMediaItem> target, IEnumerable<AnikiMediaItem> items)
        {
            if (target == null)
            {
                return;
            }

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

        // Recent Trophy
        public void RefreshRecentAchievements() => LoadRecentAchievements(3);

        private string GetSuccessStoryRootCached()
        {
            try
            {
                var now = DateTime.UtcNow;

                // Si on a déjà un chemin en cache, on le réutilise tant qu'il existe
                // et qu'on ne veut pas recheck trop souvent (10 min)
                if (!string.IsNullOrWhiteSpace(cachedSsRoot))
                {
                    if (Directory.Exists(cachedSsRoot))
                    {
                        if ((now - cachedSsRootCheckedUtc) < TimeSpan.FromMinutes(10))
                        {
                            return cachedSsRoot;
                        }

                        // Si ça fait + de 10 min, on revalide rapidement
                        if (Directory.EnumerateFiles(cachedSsRoot, "*.json", SearchOption.AllDirectories).Any())
                        {
                            cachedSsRootCheckedUtc = now;
                            return cachedSsRoot;
                        }
                    }
                }

                // Sinon, on recherche (ta méthode existante)
                var found = FindSuccessStoryRoot();

                cachedSsRoot = found;
                cachedSsRootCheckedUtc = now;

                return found;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] GetSuccessStoryRootCached failed.");
                return null;
            }
        }


        private string FindSuccessStoryRoot()
        {
            try
            {
                var root = plugin?.PlayniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

                var classic = Path.Combine(root, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                if (Directory.Exists(classic) &&
                    Directory.EnumerateFiles(classic, "*.json", SearchOption.AllDirectories).Any())
                    return classic;

                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (!dir.EndsWith("SuccessStory", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Any())
                        return dir;
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] FindSuccessStoryRoot failed.");
            }
            return null;

        }

        private IEnumerable<string> EnumerateSsCacheDirs(string ssRoot)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string p) { if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) set.Add(p); }

            if (!string.IsNullOrWhiteSpace(ssRoot))
                Add(Path.Combine(ssRoot, "CacheIcons"));

            var cfg = plugin?.PlayniteApi?.Paths?.ConfigurationPath;
            var extData = plugin?.PlayniteApi?.Paths?.ExtensionsDataPath;

            if (!string.IsNullOrWhiteSpace(cfg))
                Add(Path.Combine(cfg, "Cache", "SuccessStory"));

            if (!string.IsNullOrWhiteSpace(cfg))
            {
                var root = Path.GetFullPath(Path.Combine(cfg, ".."));
                Add(Path.Combine(root, "Cache", "SuccessStory"));
            }

            if (!string.IsNullOrWhiteSpace(extData))
            {
                var p1 = Directory.GetParent(extData)?.FullName;
                if (!string.IsNullOrWhiteSpace(p1)) Add(Path.Combine(p1, "Cache", "SuccessStory"));
                var p2 = Directory.GetParent(p1 ?? string.Empty)?.FullName;
                if (!string.IsNullOrWhiteSpace(p2)) Add(Path.Combine(p2, "Cache", "SuccessStory"));
            }

            return set;
        }

        private static string Md5Hex(string s)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
                var hash = md5.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private string TryGetSsCachedImage(string url, string ssRoot)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var key = Md5Hex(url);

            foreach (var dir in EnumerateSsCacheDirs(ssRoot))
            {
                var noExt = Path.Combine(dir, key);
                if (File.Exists(noExt)) return noExt;

                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                {
                    var p = noExt + ext;
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        private void LoadRecentAchievements(int take = 3)
        {
            List<RecentAchievementItem> computed;

            try
            {
                if (plugin?.PlayniteApi == null) return;

                var ssRoot = GetSuccessStoryRootCached();
                if (string.IsNullOrEmpty(ssRoot) || !Directory.Exists(ssRoot)) return;

                string[] files;
                try { files = Directory.EnumerateFiles(ssRoot, "*.json", SearchOption.AllDirectories).ToArray(); }
                catch { return; }
                if (files.Length == 0) return;

                DateTime? ParseWhen(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && DateTime.TryParse(it.DateUnlocked, out var d1)) return d1;
                    if (it.UnlockTime != null) return DateTimeOffset.FromUnixTimeSeconds(it.UnlockTime.Value).LocalDateTime;
                    if (!string.IsNullOrWhiteSpace(it.UnlockTimestamp) && DateTime.TryParse(it.UnlockTimestamp, out var d2)) return d2;
                    if (!string.IsNullOrWhiteSpace(it.LastUnlock) && DateTime.TryParse(it.LastUnlock, out var d3)) return d3;
                    return null;
                }

                bool IsUnlocked(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && !it.DateUnlocked.StartsWith("0001-01-01")) return true;
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

                        var desc = !string.IsNullOrWhiteSpace(it.Description) ? it.Description : (it.Desc ?? "");

                        var rawIcon = !string.IsNullOrWhiteSpace(it.UrlUnlocked) ? it.UrlUnlocked
                                    : !string.IsNullOrWhiteSpace(it.IconUnlocked) ? it.IconUnlocked
                                    : (it.ImageUrl ?? "");

                        string icon = rawIcon;
                        if (!string.IsNullOrWhiteSpace(rawIcon) &&
                            rawIcon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            var cached = TryGetSsCachedImage(rawIcon, ssRoot);
                            if (!string.IsNullOrEmpty(cached))
                                icon = cached;
                        }

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

                computed = results
                    .OrderByDescending(r => r.Unlocked)
                    .Take(take)
                    .ToList();
            }
            catch
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Apply()
            {
                RecentAchievements.Clear();
                foreach (var it in computed)
                    RecentAchievements.Add(it);
            }

            if (dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                dispatcher.BeginInvoke((Action)Apply);
            }

        }

        public void RefreshRareAchievements() => LoadRareTop(3);

        private void LoadRareTop(int take = 3)
        {
            List<RareAchievementItem> computed;

            try
            {
                if (plugin?.PlayniteApi == null) return;

                var ssRoot = GetSuccessStoryRootCached();
                if (string.IsNullOrEmpty(ssRoot) || !Directory.Exists(ssRoot)) return;

                string[] files;
                try { files = Directory.EnumerateFiles(ssRoot, "*.json", SearchOption.AllDirectories).ToArray(); }
                catch { return; }
                if (files.Length == 0) return;

                DateTime? ParseWhen(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && DateTime.TryParse(it.DateUnlocked, out var d1)) return d1;
                    if (it.UnlockTime != null) return DateTimeOffset.FromUnixTimeSeconds(it.UnlockTime.Value).LocalDateTime;
                    if (!string.IsNullOrWhiteSpace(it.UnlockTimestamp) && DateTime.TryParse(it.UnlockTimestamp, out var d2)) return d2;
                    if (!string.IsNullOrWhiteSpace(it.LastUnlock) && DateTime.TryParse(it.LastUnlock, out var d3)) return d3;
                    return null;
                }

                bool IsUnlocked(SsItem it)
                {
                    if (!string.IsNullOrWhiteSpace(it.DateUnlocked) && !it.DateUnlocked.StartsWith("0001-01-01")) return true;
                    if (it.UnlockTime != null) return true;
                    if (string.Equals(it.IsUnlock, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(it.Earned, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(it.Unlocked, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                }

                double? TryGetRarityPercent(SsItem it)
                {
                    if (it.RarityValue is double rv && rv >= 0 && rv <= 100) return rv;
                    if (it.Percent is double p && p >= 0 && p <= 100) return p;
                    if (it.Percentage is double pc && pc >= 0 && pc <= 100) return pc;

                    string[] texts = { it.Rarity, it.RarityName };
                    foreach (var txt in texts)
                    {
                        if (string.IsNullOrWhiteSpace(txt)) continue;
                        var raw = txt.Replace("%", "").Trim();
                        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
                        {
                            if (v >= 0 && v <= 100) return v;
                            if (v > 0 && v <= 1) return v * 100.0;
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

                        var r = TryGetRarityPercent(it);
                        if (r == null) continue;

                        var title = !string.IsNullOrWhiteSpace(it.Name) ? it.Name
                                   : !string.IsNullOrWhiteSpace(it.Title) ? it.Title
                                   : "(Achievement)";

                        var rawIcon = !string.IsNullOrWhiteSpace(it.UrlUnlocked) ? it.UrlUnlocked
                                    : !string.IsNullOrWhiteSpace(it.IconUnlocked) ? it.IconUnlocked
                                    : (it.ImageUrl ?? "");

                        string icon = rawIcon;
                        if (!string.IsNullOrWhiteSpace(rawIcon) &&
                            rawIcon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            var cached = TryGetSsCachedImage(rawIcon, ssRoot);
                            if (!string.IsNullOrEmpty(cached))
                                icon = cached;
                        }

                        pool.Add(new RareAchievementItem
                        {
                            Game = gameName,
                            Title = title,
                            Percent = r.Value,
                            IconPath = icon,
                            Unlocked = when.Value
                        });
                    }
                }

                var cutoff = DateTime.Now.AddYears(-1);
                computed = pool
                    .Where(x => x.Unlocked > cutoff)
                    .OrderBy(x => x.Percent)
                    .ThenByDescending(x => x.Unlocked)
                    .Take(take)
                    .ToList();
            }
            catch
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Apply()
            {
                RareTop.Clear();
                foreach (var it in computed)
                    RareTop.Add(it);
            }

            if (dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                dispatcher.BeginInvoke((Action)Apply);
            }

        }

        private void TryStartAchievementsWatcher()
        {
            try
            {
                if (plugin?.PlayniteApi == null) return;

                var ssRoot = GetSuccessStoryRootCached();
                if (string.IsNullOrEmpty(ssRoot) || !Directory.Exists(ssRoot)) return;

                achievementsWatcher?.Dispose();
                achievementsWatcher = new FileSystemWatcher(ssRoot, "*.json")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    InternalBufferSize = 64 * 1024
                };

                debounceTimer?.Dispose();
                debounceTimer = new Timer(1500) { AutoReset = false };
                debounceTimer.Elapsed += async (_, __) =>
                {
                    try
                    {
                        cachedSsRootCheckedUtc = DateTime.MinValue;

                        LoadRecentAchievements(3);
                        LoadRareTop(3);

                        if (RecentAchievements.Count == 0)
                        {
                            await Task.Delay(1200);
                            LoadRecentAchievements(3);
                        }
                    }
                    catch { }
                };

                FileSystemEventHandler pulse = (_, __) => { debounceTimer.Stop(); debounceTimer.Start(); };
                achievementsWatcher.Created += pulse;
                achievementsWatcher.Changed += pulse;
                achievementsWatcher.Deleted += pulse;
                achievementsWatcher.Renamed += (_, __) => { debounceTimer.Stop(); debounceTimer.Start(); };
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to start SuccessStory watcher.");
            }
        }



        public bool IsInGameOverlaySuspendGameEnabled()
        {
            return string.Equals(InGameOverlayGameBehavior, "SuspendGame", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsInGameOverlayNeverSuspendGame(Guid gameId)
        {
            return gameId != Guid.Empty &&
                   InGameOverlayNeverSuspendGames != null &&
                   InGameOverlayNeverSuspendGames.ContainsKey(gameId);
        }

        public void ToggleInGameOverlayNeverSuspendGame(Playnite.SDK.Models.Game game)
        {
            if (game == null || game.Id == Guid.Empty)
            {
                return;
            }

            SetInGameOverlayNeverSuspend(game.Id, !IsInGameOverlayNeverSuspendGame(game.Id), game.Name);
        }

        public void SetInGameOverlayNeverSuspend(Guid gameId, bool neverSuspend, string gameName = null)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            if (InGameOverlayNeverSuspendGames == null)
            {
                InGameOverlayNeverSuspendGames = new Dictionary<Guid, string>();
            }

            if (neverSuspend)
            {
                InGameOverlayNeverSuspendGames[gameId] = string.IsNullOrWhiteSpace(gameName)
                    ? gameId.ToString()
                    : gameName;
            }
            else if (InGameOverlayNeverSuspendGames.ContainsKey(gameId))
            {
                InGameOverlayNeverSuspendGames.Remove(gameId);
            }

            RefreshInGameOverlayNeverSuspendGameItems();
            OnPropertyChanged(nameof(InGameOverlayNeverSuspendGames));

            try
            {
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save in-game overlay never suspend list.");
            }
        }

        public void ClearInGameOverlayNeverSuspendGames()
        {
            if (InGameOverlayNeverSuspendGames == null || InGameOverlayNeverSuspendGames.Count == 0)
            {
                return;
            }

            InGameOverlayNeverSuspendGames.Clear();
            RefreshInGameOverlayNeverSuspendGameItems();
            OnPropertyChanged(nameof(InGameOverlayNeverSuspendGames));

            try
            {
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to clear in-game overlay never suspend list.");
            }
        }

        private void RefreshInGameOverlayNeverSuspendGameItems()
        {
            var items = new ObservableCollection<AnikiOverlayNeverSuspendGameItem>();

            if (InGameOverlayNeverSuspendGames != null)
            {
                foreach (var pair in InGameOverlayNeverSuspendGames.OrderBy(x => x.Value ?? string.Empty))
                {
                    items.Add(new AnikiOverlayNeverSuspendGameItem(this, pair.Key, pair.Value));
                }
            }

            InGameOverlayNeverSuspendGameItems = items;
        }

        // ===== ISettings =====
        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit()
        {
            plugin.SavePluginSettings(this);
        }



        public bool VerifySettings(out List<string> errors) { errors = null; return true; }

        // ===== Helpers =====
        private static string PercentString(int part, int total) =>
            total <= 0 ? "0%" : $"{Math.Round(part * 100.0 / total)}%";

        public static string PlaytimeToString(ulong minutes, bool useDays)
        {
            if (useDays && minutes >= 60 * 24)
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

                    if (di.DriveType != DriveType.Fixed &&
                        di.DriveType != DriveType.Removable &&
                        di.DriveType != DriveType.Network)
                    {
                        continue;
                    }

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

                void Apply()
                {
                    diskUsages.Clear();
                    foreach (var it in list)
                        diskUsages.Add(it);
                }

                var dispatcher = Application.Current?.Dispatcher;

                // Si pas de dispatcher, on tente quand même d’appliquer direct (cas rare)
                if (dispatcher == null)
                {
                    Apply();
                    return;
                }

                // Si déjà sur le thread UI, on applique direct
                if (dispatcher.CheckAccess())
                {
                    Apply();
                    return;
                }

                // Sinon non bloquant
                dispatcher.BeginInvoke((Action)Apply);
            }
            catch
            {
                // volontairement silencieux
            }
        }

        public void RefreshDuplicateHiderAvailability(Playnite.SDK.Models.Game selectedGame)
        {
            try
            {
                if (selectedGame == null)
                {
                    HasDuplicateHiderVersions = false;
                    return;
                }

                if (!TryGetDuplicateHiderCopies(selectedGame, out var copies))
                {
                    HasDuplicateHiderVersions = false;
                    return;
                }

                HasDuplicateHiderVersions = copies != null && copies.Count > 1;
            }
            catch
            {
                HasDuplicateHiderVersions = false;
            }
        }

        private bool PrepareDuplicateHiderVersionsWindow()
        {
            try
            {
                DuplicateHiderGameVersions.Clear();

                var selectedGame = plugin?.PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();

                if (selectedGame == null)
                {
                    HasDuplicateHiderVersions = false;
                    return false;
                }

                if (!TryGetDuplicateHiderCopies(selectedGame, out var copies))
                {
                    HasDuplicateHiderVersions = false;
                    return false;
                }

                if (copies == null || copies.Count <= 1)
                {
                    HasDuplicateHiderVersions = false;
                    return false;
                }

                HasDuplicateHiderVersions = true;

                var duplicateHiderInstance = GetDuplicateHiderInstance();

                foreach (var copy in copies)
                {
                    if (copy == null)
                    {
                        continue;
                    }

                    var gameId = copy.Id;

                    var item = new AnikiDuplicateHiderGameItem
                    {
                        GameId = gameId,
                        Name = copy.Name ?? string.Empty,
                        SourceName = copy.Source?.Name ?? string.Empty,
                        PlatformName = copy.Platforms?.FirstOrDefault()?.Name ?? string.Empty,
                        DisplayString = GetDuplicateHiderDisplayString(duplicateHiderInstance, copy),
                        Icon = TryGetDuplicateHiderIcon(copy),
                        IsCurrent = copy.Id == selectedGame.Id
                    };

                    item.SelectCommand = new RelayCommand(
                        () =>
                        {
                            SelectDuplicateHiderGame(gameId);
                        }
                    );

                    DuplicateHiderGameVersions.Add(item);
                }

                return DuplicateHiderGameVersions.Count > 1;
            }
            catch
            {
                DuplicateHiderGameVersions.Clear();
                HasDuplicateHiderVersions = false;
                return false;
            }
        }

        private object GetDuplicateHiderInstance()
        {
            try
            {
                var type = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(a => a.GetType("DuplicateHider.DuplicateHiderPlugin", false))
                    .FirstOrDefault(t => t != null);

                if (type == null)
                {
                    return null;
                }

                return type
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetDuplicateHiderCopies(Playnite.SDK.Models.Game selectedGame, out List<Playnite.SDK.Models.Game> copies)
        {
            copies = new List<Playnite.SDK.Models.Game>();

            try
            {
                if (selectedGame == null)
                {
                    return false;
                }

                var instance = GetDuplicateHiderInstance();

                if (instance == null)
                {
                    return false;
                }

                var method = instance.GetType().GetMethod(
                    "GetCopies",
                    BindingFlags.Public | BindingFlags.Instance);

                if (method == null)
                {
                    return false;
                }

                var result = method.Invoke(instance, new object[] { selectedGame }) as IEnumerable<Playnite.SDK.Models.Game>;

                if (result == null)
                {
                    return false;
                }

                copies = result
                    .Where(g => g != null)
                    .ToList();

                return copies.Count > 0;
            }
            catch
            {
                copies = new List<Playnite.SDK.Models.Game>();
                return false;
            }
        }

        private string GetDuplicateHiderDisplayString(object duplicateHiderInstance, Playnite.SDK.Models.Game game)
        {
            try
            {
                if (duplicateHiderInstance == null || game == null)
                {
                    return GetDuplicateHiderFallbackLabel(game);
                }

                var settings = duplicateHiderInstance
                    .GetType()
                    .GetProperty("Settings", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(duplicateHiderInstance);

                var displayString = settings?
                    .GetType()
                    .GetProperty("DisplayString", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(settings) as string;

                var method = duplicateHiderInstance
                    .GetType()
                    .GetMethod("ExpandDisplayString", BindingFlags.Public | BindingFlags.Instance);

                if (method == null || string.IsNullOrWhiteSpace(displayString))
                {
                    return GetDuplicateHiderFallbackLabel(game);
                }

                var expanded = method.Invoke(duplicateHiderInstance, new object[] { game, displayString }) as string;

                if (!string.IsNullOrWhiteSpace(expanded))
                {
                    return expanded;
                }

                return GetDuplicateHiderFallbackLabel(game);
            }
            catch
            {
                return GetDuplicateHiderFallbackLabel(game);
            }
        }

        private string GetDuplicateHiderFallbackLabel(Playnite.SDK.Models.Game game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            var platform = game.Platforms?.FirstOrDefault()?.Name ?? string.Empty;
            var source = game.Source?.Name ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(platform) && !string.IsNullOrWhiteSpace(source))
            {
                if (!string.Equals(platform, source, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{source} / {platform}";
                }

                return platform;
            }

            if (!string.IsNullOrWhiteSpace(platform))
            {
                return platform;
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                return source;
            }

            return game.Name ?? string.Empty;
        }

        private ImageSource TryGetDuplicateHiderIcon(Playnite.SDK.Models.Game game)
        {
            try
            {
                if (game == null)
                {
                    return null;
                }

                var pluginType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(a => a.GetType("DuplicateHider.DuplicateHiderPlugin", false))
                    .FirstOrDefault(t => t != null);

                if (pluginType == null)
                {
                    return null;
                }

                var iconCache = pluginType
                    .GetField("SourceIconCache", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(null);

                if (iconCache == null)
                {
                    return null;
                }

                var method = iconCache
                    .GetType()
                    .GetMethod("GetOrGenerate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null)
                {
                    return null;
                }

                return method.Invoke(iconCache, new object[] { game }) as ImageSource;
            }
            catch
            {
                return null;
            }
        }

        private void SelectDuplicateHiderGame(Guid gameId)
        {
            try
            {
                var instance = GetDuplicateHiderInstance();

                if (instance != null)
                {
                    var method = instance.GetType().GetMethod(
                        "SelectGame",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (method != null)
                    {
                        method.Invoke(instance, new object[] { gameId });
                    }
                    else
                    {
                        plugin?.PlayniteApi?.MainView?.SelectGame(gameId);
                    }
                }
                else
                {
                    plugin?.PlayniteApi?.MainView?.SelectGame(gameId);
                }
            }
            catch
            {
                try
                {
                    plugin?.PlayniteApi?.MainView?.SelectGame(gameId);
                }
                catch
                {
                }
            }
            finally
            {
                try
                {
                    plugin?.CloseTopWindow();
                }
                catch
                {
                }
            }
        }
    }



    public class AnikiHelperSettingsViewModel : ObservableObject, ISettings
    {
        public AnikiHelperSettings Settings { get; set; }
        private readonly global::AnikiHelper.AnikiHelper plugin;
        private readonly DispatcherTimer saveDebounceTimer;

        public IPlayniteAPI Api => plugin?.PlayniteApi;

        private static readonly HashSet<string> AutoSaveSettingNames = new HashSet<string>
        {
            nameof(AnikiHelperSettings.OpenWelcomeHubOnStartup),
            nameof(AnikiHelperSettings.HubAppsEnabled),
            nameof(AnikiHelperSettings.HubAppSlot1ToolName),
            nameof(AnikiHelperSettings.HubAppSlot2ToolName),
            nameof(AnikiHelperSettings.HubAppSlot3ToolName),
            nameof(AnikiHelperSettings.HubAppSlot4ToolName),
            nameof(AnikiHelperSettings.HubAppSlot1BackgroundPath),
            nameof(AnikiHelperSettings.HubAppSlot2BackgroundPath),
            nameof(AnikiHelperSettings.HubAppSlot3BackgroundPath),
            nameof(AnikiHelperSettings.HubAppSlot4BackgroundPath),
            nameof(AnikiHelperSettings.EventSoundsEnabled),
            nameof(AnikiHelperSettings.NewsScanEnabled),
            nameof(AnikiHelperSettings.IncludeHidden),

            nameof(AnikiHelperSettings.StartupIntroVideoEnabled),
            nameof(AnikiHelperSettings.ShutdownVideoEnabled),

            nameof(AnikiHelperSettings.CustomFilterIconsFolder),
            nameof(AnikiHelperSettings.CustomSourceIconsFolder),
            nameof(AnikiHelperSettings.CustomBannerAboveCoverFolder),
            nameof(AnikiHelperSettings.CustomBannerOnCoverFolder),

            nameof(AnikiHelperSettings.MediaGalleryProvider),

            nameof(AnikiHelperSettings.SteamUpdatesScanEnabled),
            nameof(AnikiHelperSettings.SteamPlayerCountEnabled),
            nameof(AnikiHelperSettings.SteamStoreEnabled),
            nameof(AnikiHelperSettings.SteamStoreLanguage),
            nameof(AnikiHelperSettings.SteamStoreRegion),
            nameof(AnikiHelperSettings.NotifyOnConnect),
            nameof(AnikiHelperSettings.NotifyOnGameStart),
            nameof(AnikiHelperSettings.ShowOffline),
            nameof(AnikiHelperSettings.SteamId64),
            nameof(AnikiHelperSettings.SteamAccountSteamId64),
            nameof(AnikiHelperSettings.SteamAccountProfileUrl),
            nameof(AnikiHelperSettings.SteamApiKey),
            nameof(AnikiHelperSettings.SteamFriendsEnabled),

            nameof(AnikiHelperSettings.GameLaunchSplashEnabled),
            nameof(AnikiHelperSettings.GameLaunchSplashPauseUniPlaySong),
            nameof(AnikiHelperSettings.GameLaunchSplashSelectionMode),
            nameof(AnikiHelperSettings.GameLaunchSplashCustomPriority1),
            nameof(AnikiHelperSettings.GameLaunchSplashCustomPriority2),
            nameof(AnikiHelperSettings.GameLaunchSplashCustomPriority3),
            nameof(AnikiHelperSettings.GameLaunchSplashCustomPriority4),
            nameof(AnikiHelperSettings.GameLaunchSplashCustomPriority5),
            nameof(AnikiHelperSettings.GameLaunchSplashShowLogo),
            nameof(AnikiHelperSettings.GameLaunchSplashLogoPosition),
            nameof(AnikiHelperSettings.GameLaunchSplashMinimumDurationMs),
            nameof(AnikiHelperSettings.GameLaunchSplashMinimumDurationSeconds),
            nameof(AnikiHelperSettings.GameLaunchSplashAutoDetectReadyEnabled),
            nameof(AnikiHelperSettings.GameLaunchSplashMaximumWaitMs),
            nameof(AnikiHelperSettings.GameLaunchSplashMaximumWaitSeconds),
            nameof(AnikiHelperSettings.GameLaunchSplashVideoEndBehavior),
            nameof(AnikiHelperSettings.GameLaunchSplashVideoSoundEnabled),
            nameof(AnikiHelperSettings.GameLaunchSplashVideoVolume),

            nameof(AnikiHelperSettings.InGameOverlayEnabled),
            nameof(AnikiHelperSettings.InGameOverlayHotkey),
            nameof(AnikiHelperSettings.InGameOverlayControllerShortcut),
            nameof(AnikiHelperSettings.InGameOverlayGameBehavior),

            nameof(AnikiHelperSettings.DynamicAutoPrecacheUserEnabled)
        };

        public AnikiHelperSettingsViewModel(global::AnikiHelper.AnikiHelper plugin)
        {
            this.plugin = plugin;
            Settings = new AnikiHelperSettings(plugin);

            saveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            saveDebounceTimer.Tick += (s, e) =>
            {
                saveDebounceTimer.Stop();
                plugin.SavePluginSettings(Settings);
            };

            Settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                return;
            }

            if (!AutoSaveSettingNames.Contains(e.PropertyName))
            {
                return;
            }

            saveDebounceTimer.Stop();
            saveDebounceTimer.Start();
        }

        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit()
        {
            saveDebounceTimer?.Stop();
            plugin.SavePluginSettings(Settings);
        }

        public void OpenLogsFolder()
        {
            try
            {
                var folder = plugin?.PlayniteApi?.Paths?.ConfigurationPath;

                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", folder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] OpenLogsFolder failed: " + ex.Message);
            }
        }

        public void ClearLogFile()
        {
            try
            {
                var folder = plugin?.PlayniteApi?.Paths?.ConfigurationPath;

                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                var logPath = Path.Combine(folder, "extensions.log");

                if (File.Exists(logPath))
                {
                    File.WriteAllText(logPath, string.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ClearLogFile failed: " + ex.Message);
            }
        }


        public bool VerifySettings(out List<string> errors) { errors = null; return true; }

        public void ResetMonthlySnapshot()
        {
            try { plugin?.ResetMonthlySnapshot(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ResetMonthlySnapshot failed: " + ex.Message);
            }
        }

        public void ExportMonthlyBackup(string exportFilePath)
        {
            try
            {
                plugin?.ExportMonthlyBackup(exportFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ExportMonthlyBackup failed: " + ex.Message);
                throw;
            }
        }

        public void ImportMonthlyBackup(string importFilePath)
        {
            try
            {
                plugin?.ImportMonthlyBackup(importFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ImportMonthlyBackup failed: " + ex.Message);
                throw;
            }
        }

        // Clears the dynamic color cache 
        public void ClearColorCache()
        {
            try
            {
                plugin.ClearDynamicColorCache();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnikiHelper] Failed to clear color cache: {ex.Message}");
                throw;
            }
        }

        // Clears the news cache

        public void ClearNewsCacheA()
        {
            try
            {
                plugin?.ClearNewsCacheA();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnikiHelperSettingsViewModel] ClearNewsCacheA failed: {ex.Message}");
                throw;
            }
        }

        public void ClearNewsCacheB()
        {
            try
            {
                plugin?.ClearNewsCacheB();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnikiHelperSettingsViewModel] ClearNewsCacheB failed: {ex.Message}");
                throw;
            }
        }

        public void OpenSourceSplashScreenManager()
        {
            plugin?.OpenSourceSplashScreenManager();
        }

        public void OpenPlatformSplashScreenManager()
        {
            plugin?.OpenPlatformSplashScreenManager();
        }

        public void OpenGlobalSplashScreenManager()
        {
            try
            {
                plugin?.OpenGlobalSplashScreenManager();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] OpenGlobalSplashScreenManager failed: " + ex.Message);
                throw;
            }
        }


        // Initializes the Steam update cache for all Steam games
        public async Task InitializeSteamUpdatesCacheAsync()
        {
            try
            {
                if (plugin != null)
                {
                    await plugin.InitializeSteamUpdatesCacheForAllGamesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnikiHelperSettingsViewModel] InitializeSteamUpdatesCacheAsync failed: {ex.Message}");
                throw;
            }
        }

    }
}
