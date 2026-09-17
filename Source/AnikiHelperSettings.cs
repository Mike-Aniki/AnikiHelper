using AnikiHelper.Services;
using AnikiHelper.Services.Achievements;
using AnikiHelper.Services.AnikiThemeSettings;
using AnikiHelper.Services.MediaGallery;
using AnikiHelper.Services.SplashScreen;
using AnikiHelper.Services.ScreenSaver;
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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Reflection;
using AnikiHelper.Services.DuplicateHider;
using AnikiHelper.Services.FirstSetup;
using AnikiHelper.Services.WebBrowser;
using AnikiHelper.Services.VideoPlayer;
using AnikiHelper.Services.ColorPacks;
using AnikiHelper.Services.CommunityPacks;
using AnikiHelper.Services.CompletePacks;
using AnikiHelper.Services.LoginPacks;
using AnikiHelper.Services.Randomization;
using AnikiHelper.Services.SoundPacks;
using AnikiHelper.Services.VisualPacks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace AnikiHelper
{
    // DTOs exposés au thème
    // DTOs exposed to the theme

    public sealed class AnikiVideoLibraryPathEntry : ObservableObject
    {
        private string path = string.Empty;
        private string displayName = string.Empty;
        private bool includeInHome = true;
        private bool includeRecentlyAdded = true;
        private bool onlineArtworkEnabled = true;
        private bool showOptions;

        public string Path
        {
            get => path;
            set => SetValue(ref path, value ?? string.Empty);
        }

        // Optional friendly label used by Desktop management and Fullscreen source/breadcrumb labels.
        public string DisplayName
        {
            get => displayName;
            set => SetValue(ref displayName, value ?? string.Empty);
        }

        // Per-library options. Existing configurations default to enabled.
        public bool IncludeInHome
        {
            get => includeInHome;
            set => SetValue(ref includeInHome, value);
        }

        public bool IncludeRecentlyAdded
        {
            get => includeRecentlyAdded;
            set => SetValue(ref includeRecentlyAdded, value);
        }

        public bool OnlineArtworkEnabled
        {
            get => onlineArtworkEnabled;
            set => SetValue(ref onlineArtworkEnabled, value);
        }

        [DontSerialize]
        public bool ShowOptions
        {
            get => showOptions;
            set => SetValue(ref showOptions, value);
        }

        [DontSerialize]
        public string EffectiveName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayName))
                {
                    return DisplayName.Trim();
                }
                try
                {
                    var normalized = (Path ?? string.Empty).Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                    var name = System.IO.Path.GetFileName(normalized);
                    return string.IsNullOrWhiteSpace(name) ? normalized : name;
                }
                catch
                {
                    return Path ?? string.Empty;
                }
            }
        }

        public AnikiVideoLibraryPathEntry Clone()
        {
            return new AnikiVideoLibraryPathEntry
            {
                Path = Path ?? string.Empty,
                DisplayName = DisplayName ?? string.Empty,
                IncludeInHome = IncludeInHome,
                IncludeRecentlyAdded = IncludeRecentlyAdded,
                OnlineArtworkEnabled = OnlineArtworkEnabled
            };
        }
    }

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

    public class AnikiGameLinkItem
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;

        [DontSerialize]
        public ICommand OpenCommand { get; set; }
    }

    // One selectable entry in the Hub Features & Apps settings list.
    // Built-in features use stable builtin:* identifiers, while Playnite Software Tools
    // keep their existing names as identifiers for backward compatibility.
    public class AnikiHubShortcutChoice
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsHeader { get; set; }
        public bool IsSelectable => !IsHeader;
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

        // Empty for Playnite Software Tools. Built-in Hub features use a stable builtin:* id.
        public string ActionId { get; set; } = string.Empty;

        [DontSerialize]
        public AppSoftware SourceApp { get; set; }

        public bool IsBuiltInFeature => !string.IsNullOrWhiteSpace(ActionId);

        public string TypeText => IsBuiltInFeature ? "FEATURE" : (IsScript ? "SCRIPT" : "APP");

        public string Details
        {
            get
            {
                if (IsBuiltInFeature)
                {
                    return string.Empty;
                }

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

    public class AnikiSplashDisabledGameItem : ObservableObject
    {
        [DontSerialize]
        private AnikiHelperSettings owner;

        public Guid GameId { get; set; }
        public string Name { get; set; }

        private bool isSplashDisabled = true;
        public bool IsSplashDisabled
        {
            get => isSplashDisabled;
            set
            {
                if (isSplashDisabled == value)
                {
                    return;
                }

                SetValue(ref isSplashDisabled, value);

                if (!value)
                {
                    owner?.SetGameLaunchSplashDisabled(GameId, false);
                }
            }
        }

        public AnikiSplashDisabledGameItem()
        {
        }

        public AnikiSplashDisabledGameItem(AnikiHelperSettings owner, Guid gameId, string name)
        {
            this.owner = owner;
            GameId = gameId;
            Name = string.IsNullOrWhiteSpace(name) ? gameId.ToString() : name;
            isSplashDisabled = true;
        }
    }

    public partial class AnikiHelperSettings : ObservableObject, ISettings, System.ComponentModel.INotifyPropertyChanged
    {
        private const int CurrentHubShortcutsDefaultsVersion = 2;
        private const int CurrentGameLaunchSplashMaximumWaitSettingsVersion = 1;

        private const string HubFeatureWebBrowserId = "builtin:web-browser";
        private const string HubFeatureMediaGalleryId = "builtin:media-gallery";
        private const string HubFeatureSteamFriendsId = "builtin:steam-friends";
        private const string HubFeatureSteamStoreId = "builtin:steam-store";
        private const string HubFeatureMusicPlayerId = "builtin:music-player";
        private const string HubFeatureVideoPlayerId = "builtin:video-player";

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
        private bool deferredStartupCacheWarmupQueued;

        [DontSerialize]
        private ILogger logger;
        [DontSerialize]
        public RelayCommand ClearInGameOverlayNeverSuspendGamesCommand { get; }

        [DontSerialize]
        public RelayCommand ClearGameLaunchSplashDisabledGamesCommand { get; }

        [DontSerialize]
        public RelayCommand PreviewScreenSaverCommand { get; }

        [DontSerialize]
        public RelayCommand TestGlobalNotificationCommand { get; }

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
        public RelayCommand<object> OpenMediaGalleryFullscreenViewerCommand { get; }

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
        public ObservableCollection<AnikiHubShortcutChoice> HubShortcutChoices { get; set; }
            = new ObservableCollection<AnikiHubShortcutChoice>();

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

            var hasUnresolvedSoftwareTool = HubAppItems != null && HubAppItems.Any(x =>
                x != null &&
                string.IsNullOrWhiteSpace(x.ActionId) &&
                x.SourceApp == null &&
                string.IsNullOrWhiteSpace(x.Path));

            if (HasHubApps && !hasUnresolvedSoftwareTool)
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
                AnikiHelper.Instance?.ApplyOverlayAchievementSortToPlayniteAchievements();
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

        // UI-facing collection for Capture Gallery. Keep the complete cached list separate so
        // the fullscreen page can be created with an empty ItemsSource, then materialize tiles
        // in small batches after the first frame has rendered. This avoids rebuilding every
        // cover/card synchronously before Window.Show() on subsequent openings.
        [DontSerialize]
        public ObservableCollection<AnikiMediaGameItem> VisibleMediaGalleryGames { get; set; }
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
        private readonly object unifiedMediaGameCacheLock = new object();

        [DontSerialize]
        private readonly Dictionary<Guid, UnifiedMediaGameCacheEntry> unifiedMediaGameCache =
            new Dictionary<Guid, UnifiedMediaGameCacheEntry>();

        [DontSerialize]
        private readonly Dictionary<Guid, Task<List<AnikiMediaItem>>> unifiedMediaGameLoads =
            new Dictionary<Guid, Task<List<AnikiMediaItem>>>();

        private static readonly TimeSpan UnifiedMediaGameCacheDuration = TimeSpan.FromSeconds(8);

        private sealed class UnifiedMediaGameCacheEntry
        {
            public DateTime CreatedUtc { get; set; }
            public List<AnikiMediaItem> Items { get; set; }
        }

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
        public AnikiFirstSetupViewModel FirstSetup { get; set; }

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


        [DontSerialize]
        public ObservableCollection<LoginRandomPoolItem> LoginRandomPoolItems { get; }
            = new ObservableCollection<LoginRandomPoolItem>();

        [DontSerialize]
        public RelayCommand<string> ToggleLoginRandomPoolCandidateCommand { get; }

        [DontSerialize]
        public RelayCommand SelectAllLoginRandomCandidatesCommand { get; }

        [DontSerialize]
        public RelayCommand ClearAllLoginRandomCandidatesCommand { get; }

        [DontSerialize]
        public bool LoginRandomPoolHasItems => LoginRandomPoolItems.Count > 0;

        [DontSerialize]
        public string LoginRandomPoolCountText
        {
            get
            {
                var selected = LoginRandomPoolItems.Count(x => x?.IsIncluded == true);
                var total = LoginRandomPoolItems.Count;
                var format = Application.Current?.TryFindResource("LoginRandomPool_CountFormat") as string
                    ?? "{0} / {1} selected";
                return string.Format(format, selected, total);
            }
        }

        public void RefreshLoginRandomPoolItems()
        {
            try
            {
                var candidates = plugin?.GetAvailableLoginRandomCandidates()
                    ?? new List<LoginRandomCandidate>();
                var excluded = new HashSet<string>(
                    LoginRandomExcludedCandidateIds ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

                LoginRandomPoolItems.Clear();

                foreach (var candidate in candidates
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                    .OrderBy(x => x.IsCommunityPack ? 1 : 0)
                    .ThenBy(x => x.IsCommunityPack ? x.DisplayName : string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.BuiltInIndex))
                {
                    LoginRandomPoolItems.Add(new LoginRandomPoolItem
                    {
                        Id = candidate.Id,
                        DisplayName = string.IsNullOrWhiteSpace(candidate.DisplayName) ? candidate.Id : candidate.DisplayName,
                        IsCommunityPack = candidate.IsCommunityPack,
                        IsIncluded = !excluded.Contains(candidate.Id)
                    });
                }

                OnPropertyChanged(nameof(LoginRandomPoolHasItems));
                OnPropertyChanged(nameof(LoginRandomPoolCountText));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginRandom] Failed to refresh the Random Login pool UI.");
            }
        }

        private void ToggleLoginRandomPoolCandidate(string candidateId)
        {
            if (string.IsNullOrWhiteSpace(candidateId))
            {
                return;
            }

            var item = LoginRandomPoolItems.FirstOrDefault(x =>
                x != null && string.Equals(x.Id, candidateId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                return;
            }

            item.IsIncluded = !item.IsIncluded;
            SaveLoginRandomPoolSelection();
        }

        private void SelectAllLoginRandomCandidates()
        {
            foreach (var item in LoginRandomPoolItems.Where(x => x != null))
            {
                item.IsIncluded = true;
            }

            SaveLoginRandomPoolSelection();
        }

        private void ClearAllLoginRandomCandidates()
        {
            foreach (var item in LoginRandomPoolItems.Where(x => x != null))
            {
                item.IsIncluded = false;
            }

            SaveLoginRandomPoolSelection();
        }

        private void SaveLoginRandomPoolSelection()
        {
            try
            {
                var availableIds = new HashSet<string>(
                    LoginRandomPoolItems.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id),
                    StringComparer.OrdinalIgnoreCase);

                // Preserve exclusions for temporarily unavailable/uninstalled candidates so a reinstall
                // keeps the user's previous choice. New candidates remain enabled by default.
                var excluded = new HashSet<string>(
                    (LoginRandomExcludedCandidateIds ?? new List<string>())
                        .Where(id => !string.IsNullOrWhiteSpace(id) && !availableIds.Contains(id)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var item in LoginRandomPoolItems.Where(x => x != null && !x.IsIncluded && !string.IsNullOrWhiteSpace(x.Id)))
                {
                    excluded.Add(item.Id);
                }

                LoginRandomExcludedCandidateIds = excluded.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                OnPropertyChanged(nameof(LoginRandomPoolCountText));
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginRandom] Failed to save the Random Login pool selection.");
            }
        }

        [DontSerialize]
        public ObservableCollection<ThemeColorRandomPoolItem> ThemeColorRandomPoolItems { get; }
            = new ObservableCollection<ThemeColorRandomPoolItem>();

        [DontSerialize]
        public RelayCommand<string> ToggleThemeColorRandomPoolCandidateCommand { get; }

        [DontSerialize]
        public RelayCommand SelectAllThemeColorRandomCandidatesCommand { get; }

        [DontSerialize]
        public RelayCommand ClearAllThemeColorRandomCandidatesCommand { get; }

        [DontSerialize]
        public bool ThemeColorRandomPoolHasItems => ThemeColorRandomPoolItems.Count > 0;

        [DontSerialize]
        public string ThemeColorRandomPoolCountText
        {
            get
            {
                var selected = ThemeColorRandomPoolItems.Count(x => x?.IsIncluded == true);
                var total = ThemeColorRandomPoolItems.Count;
                return $"{selected} / {total} selected";
            }
        }

        public void RefreshThemeColorRandomPoolItems()
        {
            try
            {
                var candidates = plugin?.GetAvailableThemeColorRandomCandidates()
                    ?? new List<ThemeColorRandomCandidate>();
                var excluded = new HashSet<string>(
                    ThemeColorRandomExcludedCandidateIds ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

                ThemeColorRandomPoolItems.Clear();

                foreach (var candidate in candidates
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                    .OrderBy(x => x.IsCommunityPack ? 1 : 0)
                    .ThenBy(x => x.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
                {
                    ThemeColorRandomPoolItems.Add(new ThemeColorRandomPoolItem
                    {
                        Id = candidate.Id,
                        DisplayName = string.IsNullOrWhiteSpace(candidate.DisplayName) ? candidate.Id : candidate.DisplayName,
                        IsCommunityPack = candidate.IsCommunityPack,
                        IsIncluded = !excluded.Contains(candidate.Id)
                    });
                }

                OnPropertyChanged(nameof(ThemeColorRandomPoolHasItems));
                OnPropertyChanged(nameof(ThemeColorRandomPoolCountText));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ThemeColorRandom] Failed to refresh the Random Theme Color pool UI.");
            }
        }

        private void ToggleThemeColorRandomPoolCandidate(string candidateId)
        {
            if (string.IsNullOrWhiteSpace(candidateId))
            {
                return;
            }

            var item = ThemeColorRandomPoolItems.FirstOrDefault(x =>
                x != null && string.Equals(x.Id, candidateId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                return;
            }

            item.IsIncluded = !item.IsIncluded;
            SaveThemeColorRandomPoolSelection();
        }

        private void SelectAllThemeColorRandomCandidates()
        {
            foreach (var item in ThemeColorRandomPoolItems.Where(x => x != null))
            {
                item.IsIncluded = true;
            }

            SaveThemeColorRandomPoolSelection();
        }

        private void ClearAllThemeColorRandomCandidates()
        {
            foreach (var item in ThemeColorRandomPoolItems.Where(x => x != null))
            {
                item.IsIncluded = false;
            }

            SaveThemeColorRandomPoolSelection();
        }

        private void SaveThemeColorRandomPoolSelection()
        {
            try
            {
                var availableIds = new HashSet<string>(
                    ThemeColorRandomPoolItems.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id),
                    StringComparer.OrdinalIgnoreCase);

                // Preserve exclusions for temporarily unavailable Color Packs. New candidates
                // are enabled by default when they first appear in the local library.
                var excluded = new HashSet<string>(
                    (ThemeColorRandomExcludedCandidateIds ?? new List<string>())
                        .Where(id => !string.IsNullOrWhiteSpace(id) && !availableIds.Contains(id)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var item in ThemeColorRandomPoolItems.Where(x => x != null && !x.IsIncluded && !string.IsNullOrWhiteSpace(x.Id)))
                {
                    excluded.Add(item.Id);
                }

                ThemeColorRandomExcludedCandidateIds = excluded.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                OnPropertyChanged(nameof(ThemeColorRandomPoolCountText));
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ThemeColorRandom] Failed to save the Random Theme Color pool selection.");
            }
        }

        [DontSerialize]
        public ObservableCollection<VisualPackRandomPoolItem> VisualPackRandomPoolItems { get; }
            = new ObservableCollection<VisualPackRandomPoolItem>();

        [DontSerialize]
        public RelayCommand<string> ToggleVisualPackRandomPoolCandidateCommand { get; }

        [DontSerialize]
        public RelayCommand SelectAllVisualPackRandomCandidatesCommand { get; }

        [DontSerialize]
        public RelayCommand ClearAllVisualPackRandomCandidatesCommand { get; }

        [DontSerialize]
        public bool VisualPackRandomPoolHasItems => VisualPackRandomPoolItems.Count > 0;

        [DontSerialize]
        public string VisualPackRandomPoolCountText
        {
            get
            {
                var selected = VisualPackRandomPoolItems.Count(x => x?.IsIncluded == true);
                var total = VisualPackRandomPoolItems.Count;
                return $"{selected} / {total} selected";
            }
        }

        public void RefreshVisualPackRandomPoolItems()
        {
            try
            {
                var candidates = plugin?.GetAvailableVisualPackRandomCandidates()
                    ?? new List<VisualPackRandomCandidate>();
                var excluded = new HashSet<string>(
                    VisualPackRandomExcludedCandidateIds ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

                VisualPackRandomPoolItems.Clear();

                foreach (var candidate in candidates
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                    .OrderBy(x => x.IsCommunityPack ? 1 : 0)
                    .ThenBy(x => x.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
                {
                    VisualPackRandomPoolItems.Add(new VisualPackRandomPoolItem
                    {
                        Id = candidate.Id,
                        DisplayName = string.IsNullOrWhiteSpace(candidate.DisplayName) ? candidate.Id : candidate.DisplayName,
                        IsCommunityPack = candidate.IsCommunityPack,
                        IsIncluded = !excluded.Contains(candidate.Id)
                    });
                }

                OnPropertyChanged(nameof(VisualPackRandomPoolHasItems));
                OnPropertyChanged(nameof(VisualPackRandomPoolCountText));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][VisualPackRandom] Failed to refresh the Random Visual Pack pool UI.");
            }
        }

        private void ToggleVisualPackRandomPoolCandidate(string candidateId)
        {
            if (string.IsNullOrWhiteSpace(candidateId))
            {
                return;
            }

            var item = VisualPackRandomPoolItems.FirstOrDefault(x =>
                x != null && string.Equals(x.Id, candidateId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                return;
            }

            item.IsIncluded = !item.IsIncluded;
            SaveVisualPackRandomPoolSelection();
        }

        private void SelectAllVisualPackRandomCandidates()
        {
            foreach (var item in VisualPackRandomPoolItems.Where(x => x != null))
            {
                item.IsIncluded = true;
            }

            SaveVisualPackRandomPoolSelection();
        }

        private void ClearAllVisualPackRandomCandidates()
        {
            foreach (var item in VisualPackRandomPoolItems.Where(x => x != null))
            {
                item.IsIncluded = false;
            }

            SaveVisualPackRandomPoolSelection();
        }

        private void SaveVisualPackRandomPoolSelection()
        {
            try
            {
                var availableIds = new HashSet<string>(
                    VisualPackRandomPoolItems.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id),
                    StringComparer.OrdinalIgnoreCase);

                // Keep exclusions for temporarily unavailable packs so reinstalling one
                // restores the user's previous choice. New packs are enabled by default.
                var excluded = new HashSet<string>(
                    (VisualPackRandomExcludedCandidateIds ?? new List<string>())
                        .Where(id => !string.IsNullOrWhiteSpace(id) && !availableIds.Contains(id)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var item in VisualPackRandomPoolItems.Where(x => x != null && !x.IsIncluded && !string.IsNullOrWhiteSpace(x.Id)))
                {
                    excluded.Add(item.Id);
                }

                VisualPackRandomExcludedCandidateIds = excluded.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                OnPropertyChanged(nameof(VisualPackRandomPoolCountText));
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][VisualPackRandom] Failed to save the Random Visual Pack pool selection.");
            }
        }

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

                // Show one Community Hub entry in Customization only. LoginBackground lives
                // in Appearance, so checking for any community-capable preset would create a
                // second Hub button there. CompletePack is used as a robust content fallback in
                // case the category id is renamed by a future theme version.
                var isCommunityHubCategory =
                    string.Equals(selectedCategory.Id, "Customization", StringComparison.OrdinalIgnoreCase) ||
                    selectedCategory.Items
                        .OfType<AnikiPresetGroup>()
                        .Any(group => string.Equals(group?.Id, "CompletePack", StringComparison.OrdinalIgnoreCase));

                if (isCommunityHubCategory)
                {
                    string ResolveCommunityText(string key, string fallback)
                    {
                        try
                        {
                            return Application.Current?.TryFindResource(key) as string ?? fallback;
                        }
                        catch
                        {
                            return fallback;
                        }
                    }

                    SelectedAnikiThemeSettingsCategoryItems.Add(new AnikiCommunityPacksMenuItem
                    {
                        DisplayName = ResolveCommunityText("CommunityHub_MenuTitle", "Community Packs"),
                        DisplayDescription = ResolveCommunityText(
                            "CommunityHub_MenuDescription",
                            "Browse, install, update and manage Complete, Visual, Login, Color and Sound Packs shared by the Aniki ReMake community.")
                    });
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

                // Update the loaded index first to avoid re-entrant page loads.
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
        private bool mediaGalleryGamesCacheLoaded;
        private int mediaGalleryGamesLoadVersion;
        private int visibleMediaGalleryGamesLoadVersion;
        private const int MediaGalleryGamesUiBatchSize = 6;
        private const int MediaGalleryGamesUiBatchDelayMs = 16;

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
        public ObservableCollection<AnikiGameLinkItem> SelectedGameLinks { get; }
            = new ObservableCollection<AnikiGameLinkItem>();

        // Kept for compatibility with existing theme bindings.
        // The Game Links button is now always visible and no per-game scan is performed.
        [DontSerialize]
        public bool HasSelectedGameLinks => true;

        [DontSerialize]
        private string selectedGameLinksGameName = string.Empty;

        [DontSerialize]
        public string SelectedGameLinksGameName
        {
            get => selectedGameLinksGameName;
            set => SetValue(ref selectedGameLinksGameName, value ?? string.Empty);
        }

        [DontSerialize]
        public RelayCommand OpenGameLinksWindowCommand { get; }

        private bool isQuickAccessFeaturesOpen = false;

        [DontSerialize]
        public bool IsQuickAccessFeaturesOpen
        {
            get => isQuickAccessFeaturesOpen;
            set => SetValue(ref isQuickAccessFeaturesOpen, value);
        }

        [DontSerialize]
        public RelayCommand OpenQuickAccessFeaturesCommand { get; }

        [DontSerialize]
        public RelayCommand CloseQuickAccessFeaturesCommand { get; }

        [DontSerialize]
        public RelayCommand<object> OpenQuickAccessFeatureCommand { get; }

        [DontSerialize]
        public RelayCommand OpenQuickAccessSoftwareToolsCommand { get; }

        [DontSerialize]
        public RelayCommand OpenQuickAccessAudioSwitcherCommand { get; }

        [DontSerialize]
        public RelayCommand OpenCommunityPacksHubCommand { get; }

        [DontSerialize]
        public RelayCommand OpenPackCreatorDownloadPageCommand { get; }

        [DontSerialize]
        public RelayCommand OpenQuickAccessUniPlaySongCommand { get; }

        [DontSerialize]
        public RelayCommand OpenQuickAccessExtraFromTopBarManagerCommand { get; }

        [DontSerialize]
        public RelayCommand<object> OpenTopBarFeatureCommand { get; }

        [DontSerialize]
        public AnikiWindowCommandProvider OpenChildWindow { get; }

        // -----------------------------------------------------------------
        // Achievement action menu context
        // -----------------------------------------------------------------
        [DontSerialize]
        private object selectedAchievementActionItem;

        [DontSerialize]
        public object SelectedAchievementActionItem
        {
            get => selectedAchievementActionItem;
            private set
            {
                if (ReferenceEquals(selectedAchievementActionItem, value))
                {
                    return;
                }

                selectedAchievementActionItem = value;
                selectedAchievementGoalState = null;
                selectedAchievementCapstoneState = null;
                OnPropertyChanged();

                OnPropertyChanged(nameof(SelectedAchievementName));
                OnPropertyChanged(nameof(SelectedAchievementDescription));
                OnPropertyChanged(nameof(SelectedAchievementIcon));
                OnPropertyChanged(nameof(SelectedAchievementIsGoal));
                OnPropertyChanged(nameof(SelectedAchievementIsCapstone));
                OnPropertyChanged(nameof(SelectedAchievementUnlocked));

                OnPropertyChanged(nameof(SelectedAchievementCleanCapturePath));
                OnPropertyChanged(nameof(SelectedAchievementNotificationCapturePath));
                OnPropertyChanged(nameof(SelectedAchievementFramedCapturePath));
                OnPropertyChanged(nameof(SelectedAchievementVideoCapturePath));

                OnPropertyChanged(nameof(SelectedAchievementHasCleanCapture));
                OnPropertyChanged(nameof(SelectedAchievementHasNotificationCapture));
                OnPropertyChanged(nameof(SelectedAchievementHasFramedCapture));
                OnPropertyChanged(nameof(SelectedAchievementHasVideoCapture));
                OnPropertyChanged(nameof(SelectedAchievementHasAnyCapture));
            }
        }

        [DontSerialize]
        public string SelectedAchievementName => GetSelectedAchievementString("Name");

        [DontSerialize]
        public string SelectedAchievementDescription => GetSelectedAchievementString("Description");

        [DontSerialize]
        public string SelectedAchievementIcon => GetSelectedAchievementString("Icon");

        [DontSerialize]
        private bool? selectedAchievementGoalState;

        [DontSerialize]
        private bool? selectedAchievementCapstoneState;

        // Stable identity used to recover controller focus after PA rebuilds/reorders
        // DynamicAchievements following a Goal/Capstone write.
        [DontSerialize]
        private string selectedAchievementFocusApiName = string.Empty;

        [DontSerialize]
        private string selectedAchievementFocusName = string.Empty;

        [DontSerialize]
        public bool SelectedAchievementIsGoal =>
            selectedAchievementGoalState ?? GetSelectedAchievementBool("IsGoal");

        [DontSerialize]
        public bool SelectedAchievementIsCapstone =>
            selectedAchievementCapstoneState ?? GetSelectedAchievementBool("IsCapstone");

        [DontSerialize]
        public bool SelectedAchievementUnlocked => GetSelectedAchievementBool("Unlocked");

        // These four names intentionally match the properties JDD proposed for
        // PlayniteAchievements. Reflection keeps Aniki Helper compatible before and
        // after PA adds them, without taking a compile-time dependency on PA.
        [DontSerialize]
        public string SelectedAchievementCleanCapturePath =>
            GetSelectedAchievementString("CleanCapturePath");

        [DontSerialize]
        public string SelectedAchievementNotificationCapturePath =>
            GetSelectedAchievementString("NotificationCapturePath");

        [DontSerialize]
        public string SelectedAchievementFramedCapturePath =>
            GetSelectedAchievementString("FramedCapturePath");

        [DontSerialize]
        public string SelectedAchievementVideoCapturePath =>
            GetSelectedAchievementString("VideoCapturePath");

        [DontSerialize]
        public bool SelectedAchievementHasCleanCapture =>
            IsUsableAchievementCapturePath(SelectedAchievementCleanCapturePath);

        [DontSerialize]
        public bool SelectedAchievementHasNotificationCapture =>
            IsUsableAchievementCapturePath(SelectedAchievementNotificationCapturePath);

        [DontSerialize]
        public bool SelectedAchievementHasFramedCapture =>
            IsUsableAchievementCapturePath(SelectedAchievementFramedCapturePath);

        [DontSerialize]
        public bool SelectedAchievementHasVideoCapture =>
            IsUsableAchievementCapturePath(SelectedAchievementVideoCapturePath);

        [DontSerialize]
        public bool SelectedAchievementHasAnyCapture =>
            SelectedAchievementHasCleanCapture ||
            SelectedAchievementHasNotificationCapture ||
            SelectedAchievementHasFramedCapture ||
            SelectedAchievementHasVideoCapture;

        [DontSerialize]
        private string selectedAchievementCapturePath = string.Empty;

        [DontSerialize]
        public string SelectedAchievementCapturePath
        {
            get => selectedAchievementCapturePath;
            private set => SetValue(ref selectedAchievementCapturePath, value ?? string.Empty);
        }

        [DontSerialize]
        private bool selectedAchievementCaptureIsVideo;

        [DontSerialize]
        public bool SelectedAchievementCaptureIsVideo
        {
            get => selectedAchievementCaptureIsVideo;
            private set => SetValue(ref selectedAchievementCaptureIsVideo, value);
        }

        [DontSerialize]
        public RelayCommand<object> OpenAchievementActionsCommand { get; }

        [DontSerialize]
        public RelayCommand ToggleSelectedAchievementGoalCommand { get; }

        [DontSerialize]
        public RelayCommand ToggleSelectedAchievementCapstoneCommand { get; }

        [DontSerialize]
        public RelayCommand<object> OpenSelectedAchievementCaptureCommand { get; }

        [DontSerialize]
        public RelayCommand OpenWebBrowserCommand { get; }

        [DontSerialize]
        public RelayCommand OpenWebBrowserHomeCommand { get; }

        [DontSerialize]
        public RelayCommand CloseWebBrowserCommand { get; }

        [DontSerialize]
        public AnikiWindowCommandProvider OpenWebBrowser { get; }

        [DontSerialize]
        public RelayCommand AddWebFavoriteCommand { get; }

        [DontSerialize]
        public RelayCommand<object> RemoveWebFavoriteCommand { get; }

        [DontSerialize]
        public RelayCommand<object> MoveWebFavoriteUpCommand { get; }

        [DontSerialize]
        public RelayCommand<object> MoveWebFavoriteDownCommand { get; }

        [DontSerialize]
        public RelayCommand ClearWebBrowserCacheCommand { get; }

        [DontSerialize]
        public RelayCommand ResetWebBrowserDataCommand { get; }

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
        public RelayCommand OpenFirstSetupCommand { get; }

        [DontSerialize]
        public RelayCommand OpenNotificationsCommand { get; }

        [DontSerialize]
        public RelayCommand OpenAchievementsCommand { get; }
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

                if (newValue == 1 && IsWelcomeHubOpen)
                {
                    QueueHubLibraryOverviewWarmup();
                }

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
        public bool ShowHubVideoCenterPage => HubVideoCenterPageEnabled && hubVideoCenterAvailableForSession;

        [DontSerialize]
        public int HubAppsPageNumber => ShowHubAppsPage ? 2 : 0;

        [DontSerialize]
        public int HubLibraryOverviewPageNumber => 2 + (ShowHubAppsPage ? 1 : 0);

        [DontSerialize]
        public int HubLibraryRecommendedPageNumber => HubLibraryOverviewPageNumber + 1;

        [DontSerialize]
        public int HubVideoCenterPageNumber => ShowHubVideoCenterPage ? HubLibraryRecommendedPageNumber + 1 : 0;

        [DontSerialize]
        public int HubFriendActivityPageNumber => HubLibraryRecommendedPageNumber + 1 + (ShowHubVideoCenterPage ? 1 : 0);

        [DontSerialize]
        public int HubLatestCapturesPageNumber => HubFriendActivityPageNumber + 1;

        [DontSerialize]
        public int HubAchievementMemoriesPageNumber => HubLatestCapturesPageNumber + 1;

        [DontSerialize]
        public int HubForYouStorePageNumber => HubAchievementMemoriesPageNumber + 1;

        [DontSerialize]
        public int HubStorePageNumber => HubForYouStorePageNumber + 1;

        [DontSerialize]
        public int HubUpcomingPageNumber => HubStorePageNumber + 1;

        [DontSerialize]
        public int HubMaxPage => HubUpcomingPageNumber;

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
        public bool HubIsPage11 => HubCurrentPage == 11;

        [DontSerialize]
        public bool HubIsAppsPage => HubAppsPageNumber > 0 && HubCurrentPage == HubAppsPageNumber;

        [DontSerialize]
        public bool HubIsLibraryOverviewPage => HubCurrentPage == HubLibraryOverviewPageNumber;

        [DontSerialize]
        public bool HubIsLibraryRecommendedPage => HubCurrentPage == HubLibraryRecommendedPageNumber;

        [DontSerialize]
        public bool HubIsVideoCenterPage => HubVideoCenterPageNumber > 0 && HubCurrentPage == HubVideoCenterPageNumber;

        [DontSerialize]
        public bool HubIsFriendActivityPage => HubCurrentPage == HubFriendActivityPageNumber;

        [DontSerialize]
        public bool HubIsLatestCapturesPage => HubCurrentPage == HubLatestCapturesPageNumber;

        [DontSerialize]
        public bool HubIsAchievementMemoriesPage => HubCurrentPage == HubAchievementMemoriesPageNumber;

        [DontSerialize]
        public bool HubIsForYouStorePage => HubCurrentPage == HubForYouStorePageNumber;

        [DontSerialize]
        public bool HubIsStorePage => HubCurrentPage == HubStorePageNumber;

        [DontSerialize]
        public bool HubIsUpcomingPage => HubCurrentPage == HubUpcomingPageNumber;

        [DontSerialize] public bool HubShowPageDot1 => IsHubPageAvailable(1);
        [DontSerialize] public bool HubShowPageDot2 => IsHubPageAvailable(2);
        [DontSerialize] public bool HubShowPageDot3 => IsHubPageAvailable(3);
        [DontSerialize] public bool HubShowPageDot4 => IsHubPageAvailable(4);
        [DontSerialize] public bool HubShowPageDot5 => IsHubPageAvailable(5);
        [DontSerialize] public bool HubShowPageDot6 => IsHubPageAvailable(6);
        [DontSerialize] public bool HubShowPageDot7 => IsHubPageAvailable(7);
        [DontSerialize] public bool HubShowPageDot8 => IsHubPageAvailable(8);
        [DontSerialize] public bool HubShowPageDot9 => IsHubPageAvailable(9);
        [DontSerialize] public bool HubShowPageDot10 => IsHubPageAvailable(10);
        [DontSerialize] public bool HubShowPageDot11 => IsHubPageAvailable(11);

        private bool IsHubPageAvailable(int page)
        {
            if (page < 1 || page > HubMaxPage) return false;
            if (page == 1) return true;
            if (HubAppsPageNumber > 0 && page == HubAppsPageNumber) return ShowHubAppsPage;
            if (page == HubLibraryOverviewPageNumber || page == HubLibraryRecommendedPageNumber) return true;
            if (HubVideoCenterPageNumber > 0 && page == HubVideoCenterPageNumber) return ShowHubVideoCenterPage;
            if (page == HubFriendActivityPageNumber) return ShowHubFriendActivityPage;
            if (page == HubLatestCapturesPageNumber) return HasHubMemory;
            if (page == HubAchievementMemoriesPageNumber) return HasHubAchievementMemory;
            if (page == HubForYouStorePageNumber || page == HubStorePageNumber || page == HubUpcomingPageNumber) return true;
            return false;
        }

        private void NotifyHubPageDotProperties()
        {
            OnPropertyChanged(nameof(HubShowPageDot1));
            OnPropertyChanged(nameof(HubShowPageDot2));
            OnPropertyChanged(nameof(HubShowPageDot3));
            OnPropertyChanged(nameof(HubShowPageDot4));
            OnPropertyChanged(nameof(HubShowPageDot5));
            OnPropertyChanged(nameof(HubShowPageDot6));
            OnPropertyChanged(nameof(HubShowPageDot7));
            OnPropertyChanged(nameof(HubShowPageDot8));
            OnPropertyChanged(nameof(HubShowPageDot9));
            OnPropertyChanged(nameof(HubShowPageDot10));
            OnPropertyChanged(nameof(HubShowPageDot11));
        }

        public string GetHubPageScopeName(int page)
        {
            if (page <= 1) return "HubTopSection";
            if (HubAppsPageNumber > 0 && page == HubAppsPageNumber) return "HubAppsSection";
            if (page == HubLibraryOverviewPageNumber) return "HubThirdSection";
            if (page == HubLibraryRecommendedPageNumber) return "HubLibraryRecommendedSection";
            if (HubVideoCenterPageNumber > 0 && page == HubVideoCenterPageNumber) return "HubVideoCenterSection";
            if (page == HubFriendActivityPageNumber) return "HubFriendActivitySection";
            if (page == HubLatestCapturesPageNumber) return "HubLatestCapturesSection";
            if (page == HubAchievementMemoriesPageNumber) return "HubAchievementMemoriesSection";
            if (page == HubForYouStorePageNumber) return "HubForYouStoreSection";
            if (page == HubStorePageNumber) return "HubStoreSection";
            if (page == HubUpcomingPageNumber) return "HubUpcomingSection";
            return "HubTopSection";
        }

        private void NotifyHubPageStateProperties()
        {
            OnPropertyChanged(nameof(HubCurrentPageTag));
            OnPropertyChanged(nameof(HubMaxPage));
            OnPropertyChanged(nameof(ShowHubVideoCenterPage));
            OnPropertyChanged(nameof(HubAppsPageNumber));
            OnPropertyChanged(nameof(HubLibraryOverviewPageNumber));
            OnPropertyChanged(nameof(HubLibraryRecommendedPageNumber));
            OnPropertyChanged(nameof(HubVideoCenterPageNumber));
            OnPropertyChanged(nameof(HubFriendActivityPageNumber));
            OnPropertyChanged(nameof(HubLatestCapturesPageNumber));
            OnPropertyChanged(nameof(HubAchievementMemoriesPageNumber));
            OnPropertyChanged(nameof(HubForYouStorePageNumber));
            OnPropertyChanged(nameof(HubStorePageNumber));
            OnPropertyChanged(nameof(HubUpcomingPageNumber));
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
            OnPropertyChanged(nameof(HubIsPage11));
            OnPropertyChanged(nameof(HubIsAppsPage));
            OnPropertyChanged(nameof(HubIsLibraryOverviewPage));
            OnPropertyChanged(nameof(HubIsLibraryRecommendedPage));
            OnPropertyChanged(nameof(HubIsVideoCenterPage));
            OnPropertyChanged(nameof(HubIsFriendActivityPage));
            OnPropertyChanged(nameof(HubIsLatestCapturesPage));
            OnPropertyChanged(nameof(HubIsAchievementMemoriesPage));
            OnPropertyChanged(nameof(HubIsForYouStorePage));
            OnPropertyChanged(nameof(HubIsStorePage));
            OnPropertyChanged(nameof(HubIsUpcomingPage));
            NotifyHubPageDotProperties();
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

        [DontSerialize]
        private bool hubVideoCenterAvailableForSession;

        [DontSerialize]
        private bool hubLibraryOverviewWarmupReady;

        [DontSerialize]
        public bool HubLibraryOverviewWarmupReady
        {
            get => hubLibraryOverviewWarmupReady;
            private set => SetValue(ref hubLibraryOverviewWarmupReady, value);
        }

        [DontSerialize]
        private int hubLibraryOverviewWarmupGeneration;

        private async void QueueHubLibraryOverviewWarmup()
        {
            if (!IsWelcomeHubOpen || IsWelcomeHubClosing || HubLibraryOverviewWarmupReady || HubCurrentPage != 1)
            {
                return;
            }

            var generation = ++hubLibraryOverviewWarmupGeneration;

            try
            {
                // Let page 1 finish its first layout/render pass before constructing the overview page.
                await Task.Delay(420);

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                await dispatcher.InvokeAsync(() =>
                {
                    if (generation != hubLibraryOverviewWarmupGeneration ||
                        !IsWelcomeHubOpen ||
                        IsWelcomeHubClosing ||
                        HubLibraryOverviewWarmupReady)
                    {
                        return;
                    }

                    HubLibraryOverviewWarmupReady = true;
                }, DispatcherPriority.Background);
            }
            catch
            {
                // Warm-up is only an optimization. Never let it affect Hub navigation.
            }
        }

        private void ResetHubLibraryOverviewWarmup()
        {
            hubLibraryOverviewWarmupGeneration++;
            HubLibraryOverviewWarmupReady = false;
        }

        private bool isWelcomeHubOpen = true;
        public bool IsWelcomeHubOpen
        {
            get => isWelcomeHubOpen;
            set
            {
                var opening = value && !isWelcomeHubOpen;
                SetValue(ref isWelcomeHubOpen, value);

                if (opening)
                {
                    // The Hub renders from local caches immediately, then validates playback
                    // history off the UI thread. This removes media deleted during the current
                    // Playnite session without blocking on an offline/sleeping NAS.
                    VideoPlayer?.ScheduleStaleMediaHistoryCleanup(force: true);
                    LatchHubVideoCenterPageAvailability();
                }

                if (value)
                {
                    QueueHubLibraryOverviewWarmup();
                }
                else
                {
                    ResetHubLibraryOverviewWarmup();
                }
            }
        }

        private void VideoPlayer_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e?.PropertyName, nameof(AnikiVideoPlayerService.HasHubVideoCenterItems), StringComparison.Ordinal))
            {
                return;
            }

            // Do not insert a slide while the user is already traversing the Hub. A newly
            // populated cache becomes eligible the next time the Hub is opened. If the page was
            // already latched visible, its four card contents can still refresh in place.
            if (!IsWelcomeHubOpen)
            {
                LatchHubVideoCenterPageAvailability();
            }
        }

        private void LatchHubVideoCenterPageAvailability()
        {
            var available = VideoPlayer?.HasHubVideoCenterItems == true;
            if (hubVideoCenterAvailableForSession == available)
            {
                OnPropertyChanged(nameof(ShowHubVideoCenterPage));
                return;
            }

            hubVideoCenterAvailableForSession = available;
            OnPropertyChanged(nameof(ShowHubVideoCenterPage));
            OnPropertyChanged(nameof(HubMaxPage));
            NotifyHubPageStateProperties();
            EnsureHubCurrentPageInRange();
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

        [DontSerialize]
        private bool isAnikiWindowOpen = false;

        [DontSerialize]
        public bool IsAnikiWindowOpen
        {
            get => isAnikiWindowOpen;
            set => SetValue(ref isAnikiWindowOpen, value);
        }

        [DontSerialize]
        private bool isSecondaryMusicWindowOpen = false;

        [DontSerialize]
        public bool IsSecondaryMusicWindowOpen
        {
            get => isSecondaryMusicWindowOpen;
            set => SetValue(ref isSecondaryMusicWindowOpen, value);
        }

        // Process-wide foreground state for theme audio.
        // Unlike Window.IsActive, this stays true when focus moves between Playnite
        // and an Aniki secondary window because both belong to the same WPF application.
        [DontSerialize]
        private bool isPlayniteApplicationActive = true;

        [DontSerialize]
        public bool IsPlayniteApplicationActive
        {
            get => isPlayniteApplicationActive;
            set => SetValue(ref isPlayniteApplicationActive, value);
        }

        [DontSerialize]
        private bool isMediaGalleryVideoPlaying = false;

        [DontSerialize]
        public bool IsMediaGalleryVideoPlaying
        {
            get => isMediaGalleryVideoPlaying;
            set => SetValue(ref isMediaGalleryVideoPlaying, value);
        }

        // Capture Gallery fullscreen video player. The volume is intentionally
        // independent from Playnite's background music volume and is persisted.
        private double mediaGalleryVideoVolume = 0.80;
        public double MediaGalleryVideoVolume
        {
            get => mediaGalleryVideoVolume;
            set
            {
                var clamped = Math.Max(0.0, Math.Min(1.0, value));
                SetValue(ref mediaGalleryVideoVolume, clamped);
                MediaGalleryVideoVolumeText = $"VOL {Math.Round(clamped * 100):0}%";
            }
        }

        [DontSerialize]
        private bool isMediaGalleryVideoControlsVisible = true;

        [DontSerialize]
        public bool IsMediaGalleryVideoControlsVisible
        {
            get => isMediaGalleryVideoControlsVisible;
            set => SetValue(ref isMediaGalleryVideoControlsVisible, value);
        }

        [DontSerialize]
        private double mediaGalleryVideoProgress = 0.0;

        [DontSerialize]
        public double MediaGalleryVideoProgress
        {
            get => mediaGalleryVideoProgress;
            set => SetValue(ref mediaGalleryVideoProgress, Math.Max(0.0, Math.Min(100.0, value)));
        }

        [DontSerialize]
        private string mediaGalleryVideoTimeText = "00:00 / --:--";

        [DontSerialize]
        public string MediaGalleryVideoTimeText
        {
            get => mediaGalleryVideoTimeText;
            set => SetValue(ref mediaGalleryVideoTimeText, value ?? "00:00 / --:--");
        }

        [DontSerialize]
        private string mediaGalleryVideoVolumeText = "VOL 80%";

        [DontSerialize]
        public string MediaGalleryVideoVolumeText
        {
            get => mediaGalleryVideoVolumeText;
            set => SetValue(ref mediaGalleryVideoVolumeText, value ?? string.Empty);
        }

        [DontSerialize]
        private string mediaGalleryVideoPlayPauseGlyph = "Ⅱ";

        [DontSerialize]
        public string MediaGalleryVideoPlayPauseGlyph
        {
            get => mediaGalleryVideoPlayPauseGlyph;
            set => SetValue(ref mediaGalleryVideoPlayPauseGlyph, value ?? "Ⅱ");
        }

        [DontSerialize]
        private bool isAnikiVideoPlayerPlaying = false;

        [DontSerialize]
        public bool IsAnikiVideoPlayerPlaying
        {
            get => isAnikiVideoPlayerPlaying;
            set => SetValue(ref isAnikiVideoPlayerPlaying, value);
        }

        // Standalone Aniki Video Player volume. Independent from background music.
        private double anikiVideoPlayerVolume = 0.80;
        public double AnikiVideoPlayerVolume
        {
            get => anikiVideoPlayerVolume;
            set => SetValue(ref anikiVideoPlayerVolume, Math.Max(0.0, Math.Min(1.0, value)));
        }

        // Optional network folders exposed by the fullscreen Videos feature.
        // Credentials are intentionally handled by Windows; Aniki Helper stores only labels/paths.
        private string videoNetworkLocation1Name = string.Empty;
        public string VideoNetworkLocation1Name
        {
            get => videoNetworkLocation1Name;
            set => SetValue(ref videoNetworkLocation1Name, value ?? string.Empty);
        }

        private string videoNetworkLocation1Path = string.Empty;
        public string VideoNetworkLocation1Path
        {
            get => videoNetworkLocation1Path;
            set => SetValue(ref videoNetworkLocation1Path, value ?? string.Empty);
        }

        private string videoNetworkLocation2Name = string.Empty;
        public string VideoNetworkLocation2Name
        {
            get => videoNetworkLocation2Name;
            set => SetValue(ref videoNetworkLocation2Name, value ?? string.Empty);
        }

        private string videoNetworkLocation2Path = string.Empty;
        public string VideoNetworkLocation2Path
        {
            get => videoNetworkLocation2Path;
            set => SetValue(ref videoNetworkLocation2Path, value ?? string.Empty);
        }

        private string videoNetworkLocation3Name = string.Empty;
        public string VideoNetworkLocation3Name
        {
            get => videoNetworkLocation3Name;
            set => SetValue(ref videoNetworkLocation3Name, value ?? string.Empty);
        }

        private string videoNetworkLocation3Path = string.Empty;
        public string VideoNetworkLocation3Path
        {
            get => videoNetworkLocation3Path;
            set => SetValue(ref videoNetworkLocation3Path, value ?? string.Empty);
        }

        private string videoNetworkLocation4Name = string.Empty;
        public string VideoNetworkLocation4Name
        {
            get => videoNetworkLocation4Name;
            set => SetValue(ref videoNetworkLocation4Name, value ?? string.Empty);
        }

        private string videoNetworkLocation4Path = string.Empty;
        public string VideoNetworkLocation4Path
        {
            get => videoNetworkLocation4Path;
            set => SetValue(ref videoNetworkLocation4Path, value ?? string.Empty);
        }

        // Optional media libraries used by Aniki Video Center's media-center views.
        // Empty paths simply hide the matching library; Browse remains available at all times.
        private string videoMoviesLibraryPath = string.Empty;
        public string VideoMoviesLibraryPath
        {
            get => videoMoviesLibraryPath;
            set
            {
                SetValue(ref videoMoviesLibraryPath, value ?? string.Empty);
                VideoPlayer?.RefreshLibraryConfiguration();
            }
        }

        private string videoSeriesLibraryPath = string.Empty;
        public string VideoSeriesLibraryPath
        {
            get => videoSeriesLibraryPath;
            set
            {
                SetValue(ref videoSeriesLibraryPath, value ?? string.Empty);
                VideoPlayer?.RefreshLibraryConfiguration();
            }
        }

        private string videoAnimeLibraryPath = string.Empty;
        public string VideoAnimeLibraryPath
        {
            get => videoAnimeLibraryPath;
            set
            {
                SetValue(ref videoAnimeLibraryPath, value ?? string.Empty);
                VideoPlayer?.RefreshLibraryConfiguration();
            }
        }

        // Fourth user-defined Video Center library. It is explicitly opt-in so users can remove
        // the category again without a blank Custom row being recreated behind their back.
        private bool videoCustomLibraryEnabled;
        public bool VideoCustomLibraryEnabled
        {
            get => videoCustomLibraryEnabled;
            set
            {
                if (videoCustomLibraryEnabled == value)
                {
                    return;
                }
                SetValue(ref videoCustomLibraryEnabled, value);

                if (!value)
                {
                    videoCustomLibraryPath = string.Empty;
                    if (VideoCustomLibraryPaths != null)
                    {
                        VideoCustomLibraryPaths.Clear();
                        VideoCustomLibraryPaths.Add(new AnikiVideoLibraryPathEntry());
                    }
                    OnPropertyChanged(nameof(VideoCustomLibraryPath));
                }
                else if (VideoCustomLibraryPaths != null && VideoCustomLibraryPaths.Count == 0)
                {
                    VideoCustomLibraryPaths.Add(new AnikiVideoLibraryPathEntry());
                }

                VideoPlayer?.RefreshLibraryConfiguration();
            }
        }

        // The display name is cosmetic only; the stable internal key remains "custom" so
        // renaming the category never breaks caches, favorites or artwork associations. Content
        // type controls which detail/scraper pipeline is reused (Movies / TV Shows / Anime).
        private string videoCustomLibraryPath = string.Empty;
        public string VideoCustomLibraryPath
        {
            get => videoCustomLibraryPath;
            set
            {
                SetValue(ref videoCustomLibraryPath, value ?? string.Empty);
                VideoPlayer?.RefreshLibraryConfiguration();
            }
        }

        private string videoCustomLibraryName = "Custom";
        public string VideoCustomLibraryName
        {
            get => string.IsNullOrWhiteSpace(videoCustomLibraryName) ? "Custom" : videoCustomLibraryName.Trim();
            set
            {
                SetValue(ref videoCustomLibraryName, value ?? string.Empty);
                VideoPlayer?.RefreshLibraryConfiguration();
            }
        }

        private string videoCustomLibraryContentType = "movies";
        public string VideoCustomLibraryContentType
        {
            get => NormalizeCustomLibraryContentType(videoCustomLibraryContentType);
            set
            {
                SetValue(ref videoCustomLibraryContentType, NormalizeCustomLibraryContentType(value));
                VideoPlayer?.RefreshLibraryConfiguration();
            }
        }

        private static string NormalizeCustomLibraryContentType(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "series": return "series";
                case "anime": return "anime";
                default: return "movies";
            }
        }

        // Multi-location Video Center libraries. The legacy single-path properties above are
        // kept for backward compatibility and mirror the first configured non-empty row.
        public ObservableCollection<AnikiVideoLibraryPathEntry> VideoMoviesLibraryPaths { get; set; }
            = new ObservableCollection<AnikiVideoLibraryPathEntry>();

        public ObservableCollection<AnikiVideoLibraryPathEntry> VideoSeriesLibraryPaths { get; set; }
            = new ObservableCollection<AnikiVideoLibraryPathEntry>();

        public ObservableCollection<AnikiVideoLibraryPathEntry> VideoAnimeLibraryPaths { get; set; }
            = new ObservableCollection<AnikiVideoLibraryPathEntry>();

        public ObservableCollection<AnikiVideoLibraryPathEntry> VideoCustomLibraryPaths { get; set; }
            = new ObservableCollection<AnikiVideoLibraryPathEntry>();

        public IReadOnlyList<AnikiVideoLibraryPathEntry> GetVideoLibraryEntries(string kind)
        {
            var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedKind == "custom" && !VideoCustomLibraryEnabled)
            {
                return Array.Empty<AnikiVideoLibraryPathEntry>();
            }

            var collection = GetVideoLibraryPathCollection(kind);
            if (collection == null)
            {
                return Array.Empty<AnikiVideoLibraryPathEntry>();
            }

            var result = collection
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Path))
                .GroupBy(x => x.Path.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();

            // Migration/fallback for old single-path settings.
            if (result.Count == 0)
            {
                string legacy = string.Empty;
                switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "movies": legacy = videoMoviesLibraryPath; break;
                    case "series": legacy = videoSeriesLibraryPath; break;
                    case "anime": legacy = videoAnimeLibraryPath; break;
                    case "custom": legacy = videoCustomLibraryPath; break;
                }
                if (!string.IsNullOrWhiteSpace(legacy))
                {
                    result.Add(new AnikiVideoLibraryPathEntry { Path = legacy.Trim() });
                }
            }

            return result;
        }

        public IReadOnlyList<string> GetVideoLibraryPaths(string kind)
        {
            return GetVideoLibraryEntries(kind)
                .Select(x => x.Path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void AddVideoLibraryPath(string kind)
        {
            var collection = GetVideoLibraryPathCollection(kind);
            if (collection == null)
            {
                return;
            }

            collection.Add(new AnikiVideoLibraryPathEntry());
        }

        public void RemoveVideoLibraryPath(string kind, AnikiVideoLibraryPathEntry entry)
        {
            var collection = GetVideoLibraryPathCollection(kind);
            if (collection == null || entry == null)
            {
                return;
            }

            collection.Remove(entry);
            if (collection.Count == 0)
            {
                if (string.Equals((kind ?? string.Empty).Trim(), "custom", StringComparison.OrdinalIgnoreCase))
                {
                    VideoCustomLibraryEnabled = false;
                }
                else
                {
                    collection.Add(new AnikiVideoLibraryPathEntry());
                }
            }
        }

        private ObservableCollection<AnikiVideoLibraryPathEntry> GetVideoLibraryPathCollection(string kind)
        {
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "movies": return VideoMoviesLibraryPaths;
                case "series": return VideoSeriesLibraryPaths;
                case "anime": return VideoAnimeLibraryPaths;
                case "custom": return VideoCustomLibraryPaths;
                default: return null;
            }
        }

        private static ObservableCollection<AnikiVideoLibraryPathEntry> BuildVideoLibraryPathCollection(
            IEnumerable<AnikiVideoLibraryPathEntry> savedEntries,
            string legacyPath)
        {
            var result = new ObservableCollection<AnikiVideoLibraryPathEntry>();
            foreach (var entry in savedEntries ?? Enumerable.Empty<AnikiVideoLibraryPathEntry>())
            {
                if (entry != null)
                {
                    result.Add(entry.Clone());
                }
            }

            if (result.Count == 0)
            {
                result.Add(new AnikiVideoLibraryPathEntry { Path = legacyPath ?? string.Empty });
            }

            return result;
        }

        private void InitializeVideoLibraryPathCollections(AnikiHelperSettings saved)
        {
            VideoMoviesLibraryPaths = BuildVideoLibraryPathCollection(saved?.VideoMoviesLibraryPaths, videoMoviesLibraryPath);
            VideoSeriesLibraryPaths = BuildVideoLibraryPathCollection(saved?.VideoSeriesLibraryPaths, videoSeriesLibraryPath);
            VideoAnimeLibraryPaths = BuildVideoLibraryPathCollection(saved?.VideoAnimeLibraryPaths, videoAnimeLibraryPath);
            VideoCustomLibraryPaths = BuildVideoLibraryPathCollection(saved?.VideoCustomLibraryPaths, videoCustomLibraryPath);

            AttachVideoLibraryPathCollection(VideoMoviesLibraryPaths);
            AttachVideoLibraryPathCollection(VideoSeriesLibraryPaths);
            AttachVideoLibraryPathCollection(VideoAnimeLibraryPaths);
            AttachVideoLibraryPathCollection(VideoCustomLibraryPaths);
            SyncLegacyVideoLibraryPaths();
        }

        private void AttachVideoLibraryPathCollection(ObservableCollection<AnikiVideoLibraryPathEntry> collection)
        {
            if (collection == null)
            {
                return;
            }

            collection.CollectionChanged += VideoLibraryPaths_CollectionChanged;
            foreach (var entry in collection)
            {
                if (entry != null)
                {
                    entry.PropertyChanged += VideoLibraryPathEntry_PropertyChanged;
                }
            }
        }

        private void VideoLibraryPaths_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AnikiVideoLibraryPathEntry entry in e.OldItems)
                {
                    if (entry != null)
                    {
                        entry.PropertyChanged -= VideoLibraryPathEntry_PropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (AnikiVideoLibraryPathEntry entry in e.NewItems)
                {
                    if (entry != null)
                    {
                        entry.PropertyChanged += VideoLibraryPathEntry_PropertyChanged;
                    }
                }
            }

            NotifyVideoLibraryPathsChanged();
        }

        private void VideoLibraryPathEntry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // ShowOptions is a desktop-settings UI state only. Every persisted library option can
            // change Home/Recently Added/scanning immediately, so refresh Video Center for those.
            if (!string.Equals(e?.PropertyName, nameof(AnikiVideoLibraryPathEntry.ShowOptions), StringComparison.Ordinal))
            {
                NotifyVideoLibraryPathsChanged();
            }
        }

        private void NotifyVideoLibraryPathsChanged()
        {
            SyncLegacyVideoLibraryPaths();
            OnPropertyChanged(nameof(VideoMoviesLibraryPaths));
            OnPropertyChanged(nameof(VideoSeriesLibraryPaths));
            OnPropertyChanged(nameof(VideoAnimeLibraryPaths));
            OnPropertyChanged(nameof(VideoCustomLibraryPaths));
            VideoPlayer?.RefreshLibraryConfiguration();
        }

        private void SyncLegacyVideoLibraryPaths()
        {
            videoMoviesLibraryPath = GetVideoLibraryPaths("movies").FirstOrDefault() ?? string.Empty;
            videoSeriesLibraryPath = GetVideoLibraryPaths("series").FirstOrDefault() ?? string.Empty;
            videoAnimeLibraryPath = GetVideoLibraryPaths("anime").FirstOrDefault() ?? string.Empty;
            videoCustomLibraryPath = GetVideoLibraryPaths("custom").FirstOrDefault() ?? string.Empty;

            OnPropertyChanged(nameof(VideoMoviesLibraryPath));
            OnPropertyChanged(nameof(VideoSeriesLibraryPath));
            OnPropertyChanged(nameof(VideoAnimeLibraryPath));
            OnPropertyChanged(nameof(VideoCustomLibraryPath));
        }

        private string videoThumbnailFfmpegPath = string.Empty;
        public string VideoThumbnailFfmpegPath
        {
            get => videoThumbnailFfmpegPath;
            set => SetValue(ref videoThumbnailFfmpegPath, value ?? string.Empty);
        }

        private string videoFfprobePath = string.Empty;
        public string VideoFfprobePath
        {
            get => videoFfprobePath;
            set => SetValue(ref videoFfprobePath, value ?? string.Empty);
        }

        // Global online artwork scraping switch. Provider selection is automatic.
        private bool videoOnlineArtworkEnabled = true;
        public bool VideoOnlineArtworkEnabled
        {
            get => videoOnlineArtworkEnabled;
            set
            {
                SetValue(ref videoOnlineArtworkEnabled, value);
                OnPropertyChanged(nameof(VideoTmdbConfigurationReady));
                VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        // Legacy per-provider switch kept for settings compatibility.
        // TMDB availability is now controlled by VideoOnlineArtworkEnabled + token presence.
        private bool videoTmdbArtworkEnabled = false;
        public bool VideoTmdbArtworkEnabled
        {
            get => videoTmdbArtworkEnabled;
            set
            {
                SetValue(ref videoTmdbArtworkEnabled, value);
                OnPropertyChanged(nameof(VideoTmdbConfigurationReady));
                VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        private string videoTmdbArtworkLanguage = string.Empty;
        public string VideoTmdbArtworkLanguage
        {
            get => videoTmdbArtworkLanguage;
            set
            {
                SetValue(ref videoTmdbArtworkLanguage, NormalizeVideoLanguageCode(value));
                VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        private const string VideoTmdbTokenEncryptionPrefix = "dpapi:tmdb-read-token:v1:";
        private static readonly byte[] VideoTmdbTokenEntropy =
            Encoding.UTF8.GetBytes("AnikiHelper.VideoTmdbReadAccessToken.v1");

        private string videoTmdbReadAccessToken = string.Empty;

        // Runtime-only clear text. Only the DPAPI encrypted field is serialized.
        [DontSerialize]
        [JsonIgnore]
        public string VideoTmdbReadAccessToken
        {
            get => videoTmdbReadAccessToken;
            set
            {
                var normalizedValue = (value ?? string.Empty).Trim();
                if (string.Equals(videoTmdbReadAccessToken, normalizedValue, StringComparison.Ordinal))
                {
                    return;
                }

                SetValue(ref videoTmdbReadAccessToken, normalizedValue);

                if (TryEncryptVideoTmdbToken(normalizedValue, out var encryptedValue))
                {
                    VideoTmdbReadAccessTokenEncrypted = encryptedValue;
                }
                else
                {
                    VideoTmdbReadAccessTokenEncrypted = string.Empty;
                    logger?.Error("[AnikiHelper][VideoCenter] TMDb API Read Access Token could not be encrypted. It will only remain available for the current session.");
                }

                OnPropertyChanged(nameof(VideoTmdbConfigurationReady));
                VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        private string videoTmdbReadAccessTokenEncrypted = string.Empty;
        public string VideoTmdbReadAccessTokenEncrypted
        {
            get => videoTmdbReadAccessTokenEncrypted;
            set => videoTmdbReadAccessTokenEncrypted = value ?? string.Empty;
        }

        [DontSerialize]
        private bool VideoTmdbTokenStorageNeedsSave { get; set; }

        [DontSerialize]
        public bool VideoTmdbConfigurationReady =>
            VideoOnlineArtworkEnabled && !string.IsNullOrWhiteSpace(VideoTmdbReadAccessToken);

        // Legacy per-provider switches kept for settings compatibility. Runtime provider routing is automatic.
        private bool videoTvmazeArtworkEnabled = true;
        public bool VideoTvmazeArtworkEnabled
        {
            get => videoTvmazeArtworkEnabled;
            set
            {
                SetValue(ref videoTvmazeArtworkEnabled, value);
                VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        private bool videoAnilistArtworkEnabled = true;
        public bool VideoAnilistArtworkEnabled
        {
            get => videoAnilistArtworkEnabled;
            set
            {
                SetValue(ref videoAnilistArtworkEnabled, value);
                VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        private bool videoAutoPlayNextEnabled = true;
        public bool VideoAutoPlayNextEnabled
        {
            get => videoAutoPlayNextEnabled;
            set => SetValue(ref videoAutoPlayNextEnabled, value);
        }

        // Optional playback-language preferences for Aniki Video Center.
        // Empty preferred language keeps LibVLC's default selection.
        private string videoPreferredAudioLanguage = string.Empty;
        public string VideoPreferredAudioLanguage
        {
            get => videoPreferredAudioLanguage;
            set => SetValue(ref videoPreferredAudioLanguage, NormalizeVideoLanguageCode(value));
        }

        // default = keep LibVLC/container default, off = force subtitles off,
        // preferred = select a matching subtitle track when available.
        private string videoSubtitlePreferenceMode = "default";
        public string VideoSubtitlePreferenceMode
        {
            get => videoSubtitlePreferenceMode;
            set
            {
                var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
                if (normalized != "off" && normalized != "preferred")
                {
                    normalized = "default";
                }

                SetValue(ref videoSubtitlePreferenceMode, normalized);
                OnPropertyChanged(nameof(VideoSubtitleSelection));
            }
        }

        private string videoPreferredSubtitleLanguage = string.Empty;
        public string VideoPreferredSubtitleLanguage
        {
            get => videoPreferredSubtitleLanguage;
            set
            {
                SetValue(ref videoPreferredSubtitleLanguage, NormalizeVideoLanguageCode(value));
                OnPropertyChanged(nameof(VideoSubtitleSelection));
            }
        }

        // Single UI value for subtitle behavior:
        // "default", "off", or an ISO language code such as "fr"/"en".
        // The two persisted fields above are kept for compatibility with existing settings.
        [DontSerialize]
        public string VideoSubtitleSelection
        {
            get
            {
                if (string.Equals(VideoSubtitlePreferenceMode, "off", StringComparison.OrdinalIgnoreCase))
                {
                    return "off";
                }

                if (string.Equals(VideoSubtitlePreferenceMode, "preferred", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(VideoPreferredSubtitleLanguage))
                {
                    return VideoPreferredSubtitleLanguage;
                }

                return "default";
            }
            set
            {
                var selection = NormalizeVideoLanguageCode(value);
                var raw = (value ?? string.Empty).Trim().ToLowerInvariant();

                if (raw == "off")
                {
                    VideoPreferredSubtitleLanguage = string.Empty;
                    VideoSubtitlePreferenceMode = "off";
                }
                else if (raw == "default" || string.IsNullOrWhiteSpace(raw))
                {
                    VideoPreferredSubtitleLanguage = string.Empty;
                    VideoSubtitlePreferenceMode = "default";
                }
                else
                {
                    VideoPreferredSubtitleLanguage = selection;
                    VideoSubtitlePreferenceMode = string.IsNullOrWhiteSpace(selection)
                        ? "default"
                        : "preferred";
                }

                OnPropertyChanged(nameof(VideoSubtitleSelection));
            }
        }

        private static string NormalizeVideoLanguageCode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized == "default" ||
                normalized == "automatic" ||
                normalized == "auto" ||
                normalized == "automatique" ||
                normalized == "predeterminado" ||
                normalized == "predeterminada")
            {
                return string.Empty;
            }

            switch (normalized)
            {
                case "fr":
                case "fra":
                case "fre":
                case "french":
                case "français":
                case "francais":
                case "francés":
                case "frances":
                case "francese":
                case "französisch":
                case "franzosisch":
                    return "fr";

                case "en":
                case "eng":
                case "english":
                case "anglais":
                case "inglés":
                case "ingles":
                case "inglese":
                case "englisch":
                    return "en";

                case "es":
                case "spa":
                case "spanish":
                case "español":
                case "espanol":
                case "espagnol":
                case "spagnolo":
                case "spanisch":
                    return "es";

                case "de":
                case "deu":
                case "ger":
                case "german":
                case "deutsch":
                case "allemand":
                case "alemán":
                case "aleman":
                case "tedesco":
                    return "de";

                case "it":
                case "ita":
                case "italian":
                case "italiano":
                case "italien":
                case "italienisch":
                    return "it";

                case "pt":
                case "por":
                case "portuguese":
                case "português":
                case "portugues":
                case "portugais":
                case "portugués":
                case "portoghese":
                    return "pt";

                case "ja":
                case "jpn":
                case "japanese":
                case "japonais":
                case "japonés":
                case "japones":
                case "giapponese":
                case "japanisch":
                case "日本語":
                    return "ja";

                case "ko":
                case "kor":
                case "korean":
                case "coréen":
                case "coreen":
                case "coreano":
                case "koreanisch":
                case "한국어":
                    return "ko";

                case "zh":
                case "zho":
                case "chi":
                case "chinese":
                case "chinois":
                case "chino":
                case "cinese":
                case "chinesisch":
                case "中文":
                    return "zh";

                case "ru":
                case "rus":
                case "russian":
                case "russe":
                case "ruso":
                case "russo":
                case "russisch":
                case "русский":
                    return "ru";

                case "nl":
                case "nld":
                case "dut":
                case "dutch":
                case "néerlandais":
                case "neerlandais":
                case "holandés":
                case "holandes":
                case "olandese":
                case "niederländisch":
                case "niederlandisch":
                    return "nl";

                case "pl":
                case "pol":
                case "polish":
                case "polonais":
                case "polaco":
                case "polacco":
                case "polnisch":
                case "polski":
                    return "pl";

                case "cs":
                case "ces":
                case "cze":
                case "czech":
                case "tchèque":
                case "tcheque":
                case "checo":
                case "ceco":
                case "tschechisch":
                case "čeština":
                    return "cs";

                case "tr":
                case "tur":
                case "turkish":
                case "turc":
                case "turco":
                case "türkisch":
                case "turkisch":
                case "türkçe":
                    return "tr";

                case "bg":
                case "bul":
                case "bulgarian":
                case "bulgare":
                case "búlgaro":
                case "bulgaro":
                case "български":
                    return "bg";

                case "ar":
                case "ara":
                case "arabic":
                case "arabe":
                case "árabe":
                case "arabo":
                case "arabisch":
                case "العربية":
                    return "ar";

                case "hi":
                case "hin":
                case "hindi":
                case "हिन्दी":
                case "हिंदी":
                    return "hi";

                default:
                    // Preserve an unknown code/value for forward compatibility. The ComboBox will
                    // simply show no selection until that language is explicitly supported.
                    return normalized;
            }
        }

        [DontSerialize]
        private AnikiVideoPlayerService videoPlayer;

        [DontSerialize]
        public AnikiVideoPlayerService VideoPlayer
        {
            get => videoPlayer;
            internal set
            {
                if (ReferenceEquals(videoPlayer, value)) return;
                if (videoPlayer != null) videoPlayer.PropertyChanged -= VideoPlayer_PropertyChanged;
                SetValue(ref videoPlayer, value);
                if (videoPlayer != null) videoPlayer.PropertyChanged += VideoPlayer_PropertyChanged;

                // On startup the Hub can already be visible before Video Center is constructed.
                // Latch availability once the persistent Video Center Home cache becomes available.
                if (IsWelcomeHubOpen)
                {
                    LatchHubVideoCenterPageAvailability();
                }
            }
        }

        [DontSerialize]
        private bool isWebBrowserOpen = false;

        [DontSerialize]
        public bool IsWebBrowserOpen
        {
            get => isWebBrowserOpen;
            set => SetValue(ref isWebBrowserOpen, value);
        }

        private bool openWelcomeHubOnStartup = true;
        public bool OpenWelcomeHubOnStartup
        {
            get => openWelcomeHubOnStartup;
            set => SetValue(ref openWelcomeHubOnStartup, value);
        }

        private bool hubVideoCenterPageEnabled = true;
        public bool HubVideoCenterPageEnabled
        {
            get => hubVideoCenterPageEnabled;
            set
            {
                if (hubVideoCenterPageEnabled == value) return;
                SetValue(ref hubVideoCenterPageEnabled, value);
                OnPropertyChanged(nameof(ShowHubVideoCenterPage));
                OnPropertyChanged(nameof(HubMaxPage));
                NotifyHubPageStateProperties();
                EnsureHubCurrentPageInRange();
            }
        }

        // New installations start with the Hub page enabled and four useful Aniki features.
        // Existing custom selections are preserved by the one-time migration in the constructor.
        private bool hubAppsEnabled = true;
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

        private string hubAppSlot1ToolName = HubFeatureWebBrowserId;
        public string HubAppSlot1ToolName
        {
            get => hubAppSlot1ToolName;
            set
            {
                SetValue(ref hubAppSlot1ToolName, NormalizeSettingText(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot2ToolName = HubFeatureMediaGalleryId;
        public string HubAppSlot2ToolName
        {
            get => hubAppSlot2ToolName;
            set
            {
                SetValue(ref hubAppSlot2ToolName, NormalizeSettingText(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot3ToolName = HubFeatureSteamFriendsId;
        public string HubAppSlot3ToolName
        {
            get => hubAppSlot3ToolName;
            set
            {
                SetValue(ref hubAppSlot3ToolName, NormalizeSettingText(value));
                RefreshHubApps();
            }
        }

        private string hubAppSlot4ToolName = HubFeatureVideoPlayerId;
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

        // Tracks the one-time migration that enables the redesigned Features & Apps page
        // only when the previous Hub Apps configuration was still untouched.
        public int HubShortcutsDefaultsVersion { get; set; }

        private string visualPackCreatorPath = string.Empty;
        public string VisualPackCreatorPath
        {
            get => visualPackCreatorPath;
            set => SetValue(ref visualPackCreatorPath, string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());
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

        private string customFilterBackgroundsFolder = string.Empty;
        public string CustomFilterBackgroundsFolder
        {
            get => customFilterBackgroundsFolder;
            set
            {
                var finalValue = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.Trim().Replace("\\", "/").TrimEnd('/');

                SetValue(ref customFilterBackgroundsFolder, finalValue);
            }
        }

        private string activeFilterPresetName = string.Empty;
        [DontSerialize]
        public string ActiveFilterPresetName
        {
            get => activeFilterPresetName;
            set => SetValue(ref activeFilterPresetName, value ?? string.Empty);
        }

        private string activeFilterBackgroundPath = string.Empty;
        [DontSerialize]
        public string ActiveFilterBackgroundPath
        {
            get => activeFilterBackgroundPath;
            set => SetValue(ref activeFilterBackgroundPath, value ?? string.Empty);
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

        // One-time migration marker: SteamGridDB Hero compatibility mode is forced off once
        // after updating to the Helper version that introduced this correction.
        public int SteamBannerResetMigrationVersion { get; set; } = 0;

        // Persistent queue used when the first setup schedules Playnite add-on installations.
        // The queue survives a Playnite restart so the installation flow can resume safely.
        public List<string> PendingFirstSetupAddonInstallIds { get; set; } = new List<string>();
        public int PendingFirstSetupAddonInstallIndex { get; set; } = 0;
        public bool PendingFirstSetupAddonInstallCurrentLaunched { get; set; } = false;

        public bool VersionCheckReady { get; set; } = true;
        public bool IsPluginUpdateRequired { get; set; } = false;
        public string RequiredAnikiHelperVersion { get; set; } = "";

        // Info snapshot 
        private string snapshotDateString;
        public string SnapshotDateString { get => snapshotDateString; set => SetValue(ref snapshotDateString, value); }

        // Options stats / display 
        private bool includeHidden = false;
        private bool showDesktopSidebarSettingsShortcut = true;
        private bool enableDebugLogs = false;
        private DateTime? debugLogsEnabledUtc;
        private static readonly TimeSpan DebugLogsAutoDisableDuration = TimeSpan.FromHours(24);
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

        private string hubMostPlayedName;
        public string HubMostPlayedName
        {
            get => hubMostPlayedName;
            set => SetValue(ref hubMostPlayedName, value);
        }

        private string hubMostPlayedPlaytime;
        public string HubMostPlayedPlaytime
        {
            get => hubMostPlayedPlaytime;
            set => SetValue(ref hubMostPlayedPlaytime, value);
        }

        private string hubMostPlayedBackgroundPath;
        public string HubMostPlayedBackgroundPath
        {
            get => hubMostPlayedBackgroundPath;
            set => SetValue(ref hubMostPlayedBackgroundPath, value);
        }

        private Guid hubMostPlayedGameId = Guid.Empty;
        public Guid HubMostPlayedGameId
        {
            get => hubMostPlayedGameId;
            set => SetValue(ref hubMostPlayedGameId, value);
        }

        private bool hubMostPlayedIsMonthly;
        public bool HubMostPlayedIsMonthly
        {
            get => hubMostPlayedIsMonthly;
            set => SetValue(ref hubMostPlayedIsMonthly, value);
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

        private const string SteamApiKeyEncryptionPrefix = "dpapi:v1:";

        private static readonly byte[] SteamApiKeyEntropy =
            Encoding.UTF8.GetBytes("AnikiHelper.SteamApiKey.v1");

        private string steamApiKey = string.Empty;

        // Runtime-only value used by the existing Steam services.
        // It is deliberately excluded from config.json.
        [DontSerialize]
        [JsonIgnore]
        public string SteamApiKey
        {
            get => steamApiKey;
            set
            {
                var normalizedValue = value ?? string.Empty;
                if (string.Equals(steamApiKey, normalizedValue, StringComparison.Ordinal))
                {
                    return;
                }

                SetValue(ref steamApiKey, normalizedValue);

                if (TryEncryptSteamApiKey(normalizedValue, out var encryptedValue))
                {
                    SteamApiKeyEncrypted = encryptedValue;
                }
                else
                {
                    // Do not keep a stale encrypted value for a different key.
                    SteamApiKeyEncrypted = string.Empty;
                    logger?.Error("[AnikiHelper] Steam Web API key could not be encrypted. It will only remain available for the current session.");
                }

                NotifySteamFriendsConfigurationPropertiesChanged();
            }
        }

        // This is the only Steam API key value serialized to config.json.
        // The clear-text SteamApiKey property above remains available to all existing code.
        private string steamApiKeyEncrypted = string.Empty;
        public string SteamApiKeyEncrypted
        {
            get => steamApiKeyEncrypted;
            set => steamApiKeyEncrypted = value ?? string.Empty;
        }

        [DontSerialize]
        private bool SteamApiKeyStorageNeedsSave { get; set; }

        private const string SteamWebApiTokenEncryptionPrefix = "dpapi:steam-web-token:v1:";

        private static readonly byte[] SteamWebApiTokenEntropy =
            Encoding.UTF8.GetBytes("AnikiHelper.SteamWebApiToken.v1");

        private string steamWebApiToken = string.Empty;

        // Runtime token obtained from the user's existing Steam WebLogin session.
        // It replaces the user-created Steam Web API key for friends and player data.
        // Keep it internal so Fullscreen themes cannot bind to and expose the bearer token.
        [DontSerialize]
        [JsonIgnore]
        internal string SteamWebApiToken
        {
            get => steamWebApiToken;
            set
            {
                var normalizedValue = value ?? string.Empty;
                if (string.Equals(steamWebApiToken, normalizedValue, StringComparison.Ordinal))
                {
                    return;
                }

                SetValue(ref steamWebApiToken, normalizedValue);

                if (TryEncryptSteamWebApiToken(normalizedValue, out var encryptedValue))
                {
                    SteamWebApiTokenEncrypted = encryptedValue;
                }
                else
                {
                    SteamWebApiTokenEncrypted = string.Empty;
                    logger?.Error("[AnikiHelper] Steam WebLogin token could not be encrypted. It will only remain available for the current session.");
                }

                NotifySteamFriendsConfigurationPropertiesChanged();
                OnPropertyChanged(nameof(SteamAccountServicesReady));
            }
        }

        // Only this DPAPI-protected value is serialized to config.json.
        private string steamWebApiTokenEncrypted = string.Empty;
        public string SteamWebApiTokenEncrypted
        {
            get => steamWebApiTokenEncrypted;
            set => steamWebApiTokenEncrypted = value ?? string.Empty;
        }

        [DontSerialize]
        private bool SteamWebApiTokenStorageNeedsSave { get; set; }

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
                OnPropertyChanged(nameof(SteamAccountDisplayName));
                NotifySteamFriendsConfigurationPropertiesChanged();
            }
        }

        [DontSerialize]
        public string SteamAccountDisplayName =>
            !string.IsNullOrWhiteSpace(SelfName)
                ? SelfName
                : (SteamAccountSteamId64 ?? string.Empty);


        // Compatibility alias for older theme bindings. It now represents an
        // authenticated Steam WebLogin token, not a user-created API key.
        [DontSerialize]
        public bool SteamFriendsHasSteamApiKey => !string.IsNullOrWhiteSpace(SteamWebApiToken);

        [DontSerialize]
        public bool SteamFriendsHasSteamId =>
            !string.IsNullOrWhiteSpace(SteamAccountSteamId64) ||
            !string.IsNullOrWhiteSpace(SteamId64);

        [DontSerialize]
        public bool SteamAccountServicesReady =>
            SteamAccountConnected &&
            SteamFriendsHasSteamId &&
            !string.IsNullOrWhiteSpace(SteamWebApiToken);

        [DontSerialize]
        public bool SteamFriendsHasRequiredConfig => SteamAccountServicesReady;

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

                if (!SteamAccountConnected)
                {
                    return "Connect your Steam account in Aniki Helper settings to use Steam Friends.";
                }

                if (!SteamFriendsHasSteamId)
                {
                    return "The connected Steam account could not be identified. Check the Steam connection.";
                }

                if (string.IsNullOrWhiteSpace(SteamWebApiToken))
                {
                    return "The Steam session is connected, but friend services are not ready. Click Check connection or reconnect Steam.";
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
            OnPropertyChanged(nameof(SteamAccountServicesReady));
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
            set
            {
                SetValue(ref steamAccountConnected, value);
                NotifySteamFriendsConfigurationPropertiesChanged();
                OnPropertyChanged(nameof(SteamAccountServicesReady));
            }
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

        // Limited collection intended for compact theme displays (first 7 friends only).
        [DontSerialize]
        public ObservableCollection<SteamFriendPlayedGameDto> SteamFriendsWhoPlayedCurrentGameVisible { get; private set; } = new ObservableCollection<SteamFriendPlayedGameDto>();

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

        private int steamFriendsWhoPlayedRemainingCount;
        [DontSerialize]
        public int SteamFriendsWhoPlayedRemainingCount
        {
            get => steamFriendsWhoPlayedRemainingCount;
            set
            {
                if (steamFriendsWhoPlayedRemainingCount == value)
                {
                    return;
                }

                SetValue(ref steamFriendsWhoPlayedRemainingCount, value);
                OnPropertyChanged(nameof(SteamFriendsWhoPlayedHasMore));
                OnPropertyChanged(nameof(SteamFriendsWhoPlayedRemainingText));
            }
        }

        [DontSerialize]
        public bool SteamFriendsWhoPlayedHasMore => SteamFriendsWhoPlayedRemainingCount > 0;

        [DontSerialize]
        public string SteamFriendsWhoPlayedRemainingText => SteamFriendsWhoPlayedHasMore
            ? $"+{SteamFriendsWhoPlayedRemainingCount}"
            : string.Empty;

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
            set
            {
                if (showHubFriendActivityPage == value) return;
                SetValue(ref showHubFriendActivityPage, value);
                NotifyHubPageDotProperties();
            }
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

            if (SteamFriendsWhoPlayedCurrentGameVisible == null)
            {
                SteamFriendsWhoPlayedCurrentGameVisible = new ObservableCollection<SteamFriendPlayedGameDto>();
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
            set
            {
                SetValue(ref selfName, value);
                OnPropertyChanged(nameof(SteamAccountDisplayName));
            }
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
            set
            {
                SetValue(ref selfAvatar, value);
                OnPropertyChanged(nameof(SelfAvatarAvailable));
            }
        }

        [DontSerialize]
        public bool SelfAvatarAvailable => !string.IsNullOrWhiteSpace(SelfAvatar);

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
        private ObservableCollection<SteamStoreItem> steamStoreDealsHub = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreNewReleases = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreTopSellers = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreUpcoming = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreUpcomingHub = new ObservableCollection<SteamStoreItem>();
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

        // Hub-only projection. Kept to four items so the Hub ItemsControl never
        // creates containers for the rest of the full Store collection.
        // HubCurrentPage already requests Store loading, so this getter stays side-effect free.
        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreDealsHub
        {
            get => steamStoreDealsHub;
            set => SetValue(ref steamStoreDealsHub, value);
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

        // Hub-only projection. The full Store keeps its normal 24-item collection.
        [DontSerialize]
        public ObservableCollection<SteamStoreItem> SteamStoreUpcomingHub
        {
            get => steamStoreUpcomingHub;
            set => SetValue(ref steamStoreUpcomingHub, value);
        }

        internal bool IsSteamStoreDealsCollection(ObservableCollection<SteamStoreItem> collection)
        {
            return ReferenceEquals(collection, steamStoreDeals);
        }

        internal bool IsSteamStoreUpcomingCollection(ObservableCollection<SteamStoreItem> collection)
        {
            return ReferenceEquals(collection, steamStoreUpcoming);
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
        private bool globalNotificationOverlayEnabled = true;
        public bool GlobalNotificationOverlayEnabled
        {
            get => globalNotificationOverlayEnabled;
            set => SetValue(ref globalNotificationOverlayEnabled, value);
        }

        // One-time default migration. Version 1 enables the Global Notification Overlay
        // for existing installations once, while preserving any later user choice.
        public int GlobalNotificationOverlayDefaultsVersion { get; set; } = 0;

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

        [DontSerialize]
        private ObservableCollection<AnikiSplashDisabledGameItem> gameLaunchSplashDisabledGameItems
            = new ObservableCollection<AnikiSplashDisabledGameItem>();

        [DontSerialize]
        public ObservableCollection<AnikiSplashDisabledGameItem> GameLaunchSplashDisabledGameItems
        {
            get => gameLaunchSplashDisabledGameItems;
            private set => SetValue(ref gameLaunchSplashDisabledGameItems, value ?? new ObservableCollection<AnikiSplashDisabledGameItem>());
        }
        #region Options (bindables)
        public bool ShowDesktopSidebarSettingsShortcut
        {
            get => showDesktopSidebarSettingsShortcut;
            set => SetValue(ref showDesktopSidebarSettingsShortcut, value);
        }

        public bool EnableDebugLogs
        {
            get => enableDebugLogs;
            set
            {
                if (enableDebugLogs == value)
                {
                    return;
                }

                SetValue(ref enableDebugLogs, value);
                DebugLogsEnabledUtc = value ? DateTime.UtcNow : (DateTime?)null;

                if (plugin != null)
                {
                    plugin.SavePluginSettings(this);
                }
            }
        }

        public DateTime? DebugLogsEnabledUtc
        {
            get => debugLogsEnabledUtc;
            set => SetValue(ref debugLogsEnabledUtc, value);
        }

        private static DateTime NormalizeDebugLogsUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            // This timestamp has always been written with DateTime.UtcNow. If an older
            // serializer loads it without a Kind, keep the stored clock value as UTC.
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
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

        private bool screenSaverEnabled = true;
        public bool ScreenSaverEnabled
        {
            get => screenSaverEnabled;
            set => SetValue(ref screenSaverEnabled, value);
        }

        private int screenSaverIdleDelayMinutes = 1;
        public int ScreenSaverIdleDelayMinutes
        {
            get => screenSaverIdleDelayMinutes;
            set
            {
                // Keep positive values as minutes for backward compatibility.
                // Negative values are reserved for the sub-minute ScreenSaver presets.
                var normalizedValue = value == -30 || value == -45
                    ? value
                    : Math.Max(1, Math.Min(120, value));

                SetValue(ref screenSaverIdleDelayMinutes, normalizedValue);
            }
        }

        private int screenSaverChangeIntervalSeconds = 15;
        public int ScreenSaverChangeIntervalSeconds
        {
            get => screenSaverChangeIntervalSeconds;
            set => SetValue(ref screenSaverChangeIntervalSeconds, Math.Max(5, Math.Min(300, value)));
        }

        private ScreenSaverSource screenSaverSource = global::AnikiHelper.Services.ScreenSaver.ScreenSaverSource.InstalledGames;
        public ScreenSaverSource ScreenSaverSource
        {
            get => screenSaverSource;
            set => SetValue(ref screenSaverSource, value);
        }

        private bool screenSaverUseSplashImages = true;
        public bool ScreenSaverUseSplashImages
        {
            get => screenSaverUseSplashImages;
            set => SetValue(ref screenSaverUseSplashImages, value);
        }

        private bool screenSaverAmbientMusicEnabled = true;
        public bool ScreenSaverAmbientMusicEnabled
        {
            get => screenSaverAmbientMusicEnabled;
            set
            {
                if (screenSaverAmbientMusicEnabled == value)
                {
                    return;
                }

                SetValue(ref screenSaverAmbientMusicEnabled, value);
                OnPropertyChanged(nameof(IsScreenSaverAmbientMusicActive));
            }
        }

        [DontSerialize]
        private bool isScreenSaverActive;

        [DontSerialize]
        public bool IsScreenSaverActive
        {
            get => isScreenSaverActive;
            set
            {
                if (isScreenSaverActive == value)
                {
                    return;
                }

                SetValue(ref isScreenSaverActive, value);
                OnPropertyChanged(nameof(IsScreenSaverAmbientMusicActive));
            }
        }

        [DontSerialize]
        public bool IsScreenSaverAmbientMusicActive =>
            IsScreenSaverActive && ScreenSaverAmbientMusicEnabled;

        private bool screenSaverShowLogo = true;
        public bool ScreenSaverShowLogo
        {
            get => screenSaverShowLogo;
            set => SetValue(ref screenSaverShowLogo, value);
        }

        private bool screenSaverShowInfoCard = true;
        public bool ScreenSaverShowInfoCard
        {
            get => screenSaverShowInfoCard;
            set => SetValue(ref screenSaverShowInfoCard, value);
        }

        private bool screenSaverAnimateBackground = true;
        public bool ScreenSaverAnimateBackground
        {
            get => screenSaverAnimateBackground;
            set => SetValue(ref screenSaverAnimateBackground, value);
        }

        private bool screenSaverUseFadeTransitions = true;
        public bool ScreenSaverUseFadeTransitions
        {
            get => screenSaverUseFadeTransitions;
            set => SetValue(ref screenSaverUseFadeTransitions, value);
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

        public const double DefaultGameLaunchSplashBackgroundDimming = 95d / 255d;
        public const int DefaultGameLaunchSplashMaximumWaitMs = 90000;

        private double gameLaunchSplashBackgroundDimming = DefaultGameLaunchSplashBackgroundDimming;
        public double GameLaunchSplashBackgroundDimming
        {
            get => gameLaunchSplashBackgroundDimming;
            set => SetValue(ref gameLaunchSplashBackgroundDimming, Math.Max(0, Math.Min(0.8, value)));
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

        public int GameLaunchSplashMaximumWaitSettingsVersion { get; set; } = 0;

        private int gameLaunchSplashMaximumWaitMs = DefaultGameLaunchSplashMaximumWaitMs;
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

        public Dictionary<Guid, int> CustomGameLaunchSplashMaximumWaits { get; set; }
            = new Dictionary<Guid, int>();

        public Dictionary<Guid, string> GameLaunchSplashDisabledGames { get; set; }
            = new Dictionary<Guid, string>();

        private bool shutdownVideoEnabled = true;
        public bool ShutdownVideoEnabled
        {
            get => shutdownVideoEnabled;
            set => SetValue(ref shutdownVideoEnabled, value);
        }

        // Kept for migration from the first browser prototype. The Fullscreen browser
        // now opens on its native favorites home page instead of a fixed URL.
        private string webBrowserHomeUrl = string.Empty;
        public string WebBrowserHomeUrl
        {
            get => webBrowserHomeUrl ?? string.Empty;
            set => SetValue(ref webBrowserHomeUrl, value ?? string.Empty);
        }

        private ObservableCollection<AnikiWebFavorite> webBrowserFavorites =
            new ObservableCollection<AnikiWebFavorite>();
        public ObservableCollection<AnikiWebFavorite> WebBrowserFavorites
        {
            get => webBrowserFavorites ?? (webBrowserFavorites = new ObservableCollection<AnikiWebFavorite>());
            set => SetValue(
                ref webBrowserFavorites,
                value ?? new ObservableCollection<AnikiWebFavorite>());
        }

        [DontSerialize]
        private string newWebFavoriteName = string.Empty;
        [DontSerialize]
        public string NewWebFavoriteName
        {
            get => newWebFavoriteName ?? string.Empty;
            set => SetValue(ref newWebFavoriteName, value ?? string.Empty);
        }

        [DontSerialize]
        private string newWebFavoriteUrl = string.Empty;
        [DontSerialize]
        public string NewWebFavoriteUrl
        {
            get => newWebFavoriteUrl ?? string.Empty;
            set => SetValue(ref newWebFavoriteUrl, value ?? string.Empty);
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

        private string inGameOverlayVirtualKeyboardProvider = "Aniki";
        public string InGameOverlayVirtualKeyboardProvider
        {
            get
            {
                return string.Equals(inGameOverlayVirtualKeyboardProvider, "Windows", StringComparison.OrdinalIgnoreCase)
                    ? "Windows"
                    : "Aniki";
            }
            set
            {
                var normalized = string.Equals(value, "Windows", StringComparison.OrdinalIgnoreCase)
                    ? "Windows"
                    : "Aniki";

                SetValue(ref inGameOverlayVirtualKeyboardProvider, normalized);
            }
        }

        private string inGameOverlayVirtualKeyboardShortcut = "L3R3Hold";
        public string InGameOverlayVirtualKeyboardShortcut
        {
            get
            {
                switch (inGameOverlayVirtualKeyboardShortcut)
                {
                    case "BackX":
                    case "GuideX":
                    case "Disabled":
                        return inGameOverlayVirtualKeyboardShortcut;

                    case "L3R3Hold":
                    default:
                        return "L3R3Hold";
                }
            }
            set
            {
                string normalized;

                switch (value)
                {
                    case "BackX":
                    case "GuideX":
                    case "Disabled":
                        normalized = value;
                        break;

                    case "L3R3Hold":
                    default:
                        normalized = "L3R3Hold";
                        break;
                }

                SetValue(ref inGameOverlayVirtualKeyboardShortcut, normalized);
            }
        }

        private string inGameOverlayGamepadMouseShortcut = "BackR3";
        public string InGameOverlayGamepadMouseShortcut
        {
            get
            {
                switch (inGameOverlayGamepadMouseShortcut)
                {
                    case "StartL3":
                    case "GuideY":
                    case "Disabled":
                        return inGameOverlayGamepadMouseShortcut;

                    case "BackR3":
                    default:
                        return "BackR3";
                }
            }
            set
            {
                string normalized;

                switch (value)
                {
                    case "StartL3":
                    case "GuideY":
                    case "Disabled":
                        normalized = value;
                        break;

                    case "BackR3":
                    default:
                        normalized = "BackR3";
                        break;
                }

                SetValue(ref inGameOverlayGamepadMouseShortcut, normalized);
            }
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

        [DontSerialize]
        private bool isPlayerProfileLoading;
        [DontSerialize]
        public bool IsPlayerProfileLoading
        {
            get => isPlayerProfileLoading;
            set => SetValue(ref isPlayerProfileLoading, value);
        }

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
                    NotifySoundPackRuntimePathProperties();
                }
            }
        }

        [DontSerialize]
        private string activeLoginPackVideoPath = string.Empty;

        [DontSerialize]
        public string ActiveLoginPackVideoPath
        {
            get => activeLoginPackVideoPath;
            set => SetValue(ref activeLoginPackVideoPath, value ?? string.Empty);
        }

        [DontSerialize]
        private string randomLoginCommunityVideoPath = string.Empty;

        [DontSerialize]
        public string RandomLoginCommunityVideoPath
        {
            get => randomLoginCommunityVideoPath;
            set
            {
                var finalValue = value ?? string.Empty;
                if (!string.Equals(randomLoginCommunityVideoPath, finalValue, StringComparison.Ordinal))
                {
                    SetValue(ref randomLoginCommunityVideoPath, finalValue);
                    OnPropertyChanged(nameof(IsRandomLoginCommunityPack));
                }
            }
        }

        [DontSerialize]
        public bool IsRandomLoginCommunityPack => !string.IsNullOrWhiteSpace(RandomLoginCommunityVideoPath);

        [DontSerialize]
        private string soundPackDefaultAudioRoot = string.Empty;
        [DontSerialize]
        public string SoundPackDefaultAudioRoot
        {
            get => soundPackDefaultAudioRoot;
            set
            {
                var normalized = value ?? string.Empty;
                if (!string.Equals(soundPackDefaultAudioRoot, normalized, StringComparison.Ordinal))
                {
                    SetValue(ref soundPackDefaultAudioRoot, normalized);
                    NotifySoundPackRuntimePathProperties();
                }
            }
        }

        [DontSerialize]
        private string soundPackLuckyAudioRoot = string.Empty;
        [DontSerialize]
        public string SoundPackLuckyAudioRoot
        {
            get => soundPackLuckyAudioRoot;
            set
            {
                var normalized = value ?? string.Empty;
                if (!string.Equals(soundPackLuckyAudioRoot, normalized, StringComparison.Ordinal))
                {
                    SetValue(ref soundPackLuckyAudioRoot, normalized);
                    NotifySoundPackRuntimePathProperties();
                }
            }
        }

        [DontSerialize]
        private string soundPackNotiPath = string.Empty;
        [DontSerialize]
        public string SoundPackNotiPath { get => ResolveSoundPackRuntimePath(soundPackNotiPath, "Noti.wav", true); set => SetValue(ref soundPackNotiPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackEnterGameDetailsPath = string.Empty;
        [DontSerialize]
        public string SoundPackEnterGameDetailsPath { get => ResolveSoundPackRuntimePath(soundPackEnterGameDetailsPath, "EnterGameDetails.wav"); set => SetValue(ref soundPackEnterGameDetailsPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackExitGameDetailsPath = string.Empty;
        [DontSerialize]
        public string SoundPackExitGameDetailsPath { get => ResolveSoundPackRuntimePath(soundPackExitGameDetailsPath, "ExitGameDetails.wav"); set => SetValue(ref soundPackExitGameDetailsPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackLoginConfirmPath = string.Empty;
        [DontSerialize]
        public string SoundPackLoginConfirmPath { get => ResolveSoundPackRuntimePath(soundPackLoginConfirmPath, "LoginConfirm.wav"); set => SetValue(ref soundPackLoginConfirmPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackOpenPanelPath = string.Empty;
        [DontSerialize]
        public string SoundPackOpenPanelPath { get => ResolveSoundPackRuntimePath(soundPackOpenPanelPath, "OpenPanel.wav"); set => SetValue(ref soundPackOpenPanelPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackOpenAdditionalViewPath = string.Empty;
        [DontSerialize]
        public string SoundPackOpenAdditionalViewPath { get => ResolveSoundPackRuntimePath(soundPackOpenAdditionalViewPath, "OpenAdditionalView.wav"); set => SetValue(ref soundPackOpenAdditionalViewPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackCloseAdditionalViewPath = string.Empty;
        [DontSerialize]
        public string SoundPackCloseAdditionalViewPath { get => ResolveSoundPackRuntimePath(soundPackCloseAdditionalViewPath, "CloseAdditionalView.wav"); set => SetValue(ref soundPackCloseAdditionalViewPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackHomeHubOpenPath = string.Empty;
        [DontSerialize]
        public string SoundPackHomeHubOpenPath { get => ResolveSoundPackRuntimePath(soundPackHomeHubOpenPath, "HomeHubOpen.wav"); set => SetValue(ref soundPackHomeHubOpenPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackHomeHubClosePath = string.Empty;
        [DontSerialize]
        public string SoundPackHomeHubClosePath { get => ResolveSoundPackRuntimePath(soundPackHomeHubClosePath, "HomeHubClose.wav"); set => SetValue(ref soundPackHomeHubClosePath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackSessionSummaryPath = string.Empty;
        [DontSerialize]
        public string SoundPackSessionSummaryPath { get => ResolveSoundPackRuntimePath(soundPackSessionSummaryPath, "SessionSummary.wav"); set => SetValue(ref soundPackSessionSummaryPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackWarningPath = string.Empty;
        [DontSerialize]
        public string SoundPackWarningPath { get => ResolveSoundPackRuntimePath(soundPackWarningPath, "Warning.wav"); set => SetValue(ref soundPackWarningPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackLoginOstPath = string.Empty;
        [DontSerialize]
        public string SoundPackLoginOstPath { get => ResolveSoundPackRuntimePath(soundPackLoginOstPath, "LoginOST.mp3"); set => SetValue(ref soundPackLoginOstPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackHubOstPath = string.Empty;
        [DontSerialize]
        public string SoundPackHubOstPath { get => ResolveSoundPackRuntimePath(soundPackHubOstPath, "HubOST.mp3"); set => SetValue(ref soundPackHubOstPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackSecondaryViewsOstPath = string.Empty;
        [DontSerialize]
        public string SoundPackSecondaryViewsOstPath { get => ResolveSoundPackRuntimePath(soundPackSecondaryViewsOstPath, "SecondaryViewsOST.mp3"); set => SetValue(ref soundPackSecondaryViewsOstPath, value ?? string.Empty); }

        [DontSerialize]
        private string soundPackScreenSaverOstPath = string.Empty;
        [DontSerialize]
        public string SoundPackScreenSaverOstPath { get => ResolveSoundPackRuntimePath(soundPackScreenSaverOstPath, "ScreenSaverOST.mp3"); set => SetValue(ref soundPackScreenSaverOstPath, value ?? string.Empty); }

        private string ResolveSoundPackRuntimePath(string activePath, string defaultFileName, bool useLuckyNotification = false)
        {
            if (!IsLuckyDay)
            {
                return activePath ?? string.Empty;
            }

            if (useLuckyNotification && !string.IsNullOrWhiteSpace(soundPackLuckyAudioRoot))
            {
                var luckyFileName = LuckyStyleIndex == 2 ? "Noti2.wav" : "Noti.wav";
                var luckyPath = Path.Combine(soundPackLuckyAudioRoot, luckyFileName);
                if (File.Exists(luckyPath))
                {
                    return luckyPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(soundPackDefaultAudioRoot))
            {
                var defaultPath = Path.Combine(soundPackDefaultAudioRoot, defaultFileName);
                if (File.Exists(defaultPath))
                {
                    return defaultPath;
                }
            }

            return activePath ?? string.Empty;
        }

        private void NotifySoundPackRuntimePathProperties()
        {
            OnPropertyChanged(nameof(SoundPackNotiPath));
            OnPropertyChanged(nameof(SoundPackEnterGameDetailsPath));
            OnPropertyChanged(nameof(SoundPackExitGameDetailsPath));
            OnPropertyChanged(nameof(SoundPackLoginConfirmPath));
            OnPropertyChanged(nameof(SoundPackOpenPanelPath));
            OnPropertyChanged(nameof(SoundPackOpenAdditionalViewPath));
            OnPropertyChanged(nameof(SoundPackCloseAdditionalViewPath));
            OnPropertyChanged(nameof(SoundPackHomeHubOpenPath));
            OnPropertyChanged(nameof(SoundPackHomeHubClosePath));
            OnPropertyChanged(nameof(SoundPackSessionSummaryPath));
            OnPropertyChanged(nameof(SoundPackWarningPath));
            OnPropertyChanged(nameof(SoundPackLoginOstPath));
            OnPropertyChanged(nameof(SoundPackHubOstPath));
            OnPropertyChanged(nameof(SoundPackSecondaryViewsOstPath));
            OnPropertyChanged(nameof(SoundPackScreenSaverOstPath));
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
                    NotifySoundPackRuntimePathProperties();
                }
            }
        }

        [DontSerialize]
        public bool IsLuckyStyle1 => LoginRandomIndex == 42 && LuckyStyleIndex == 1;

        [DontSerialize]
        public bool IsLuckyStyle2 => LoginRandomIndex == 42 && LuckyStyleIndex == 2;

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

        // Legacy anti-repetition value kept for backwards compatibility.
        public int LastLoginRandomIndex { get; set; }

        // Stable candidate id used by the shared random engine (builtin:12, loginpack:<packId>, ...).
        public string LastLoginRandomCandidateId { get; set; } = string.Empty;

        // Candidate blacklist. Empty means every locally available built-in/community Login is eligible.
        // The selector UI will edit this list; keeping it persisted here makes the random engine ready now.
        public List<string> LoginRandomExcludedCandidateIds { get; set; } = new List<string>();

        // Stable id of the Theme Color used on the previous Random session.
        // Used only to avoid an immediate repeat when the pool has another choice.
        public string LastThemeColorRandomCandidateId { get; set; } = string.Empty;

        // Theme Color candidate blacklist. Empty means every built-in/community color is eligible.
        public List<string> ThemeColorRandomExcludedCandidateIds { get; set; } = new List<string>();

        // Stable id of the Visual Pack used on the previous Random session.
        public string LastVisualPackRandomCandidateId { get; set; } = string.Empty;

        // Visual Pack candidate blacklist. Empty means every built-in/community Visual Pack is eligible.
        public List<string> VisualPackRandomExcludedCandidateIds { get; set; } = new List<string>();

        public AnikiHelperSettings() { }

        private static bool TryEncryptVideoTmdbToken(string clearText, out string encryptedValue)
        {
            encryptedValue = string.Empty;

            if (string.IsNullOrEmpty(clearText))
            {
                return true;
            }

            try
            {
                var clearBytes = Encoding.UTF8.GetBytes(clearText);
                var protectedBytes = ProtectedData.Protect(
                    clearBytes,
                    VideoTmdbTokenEntropy,
                    DataProtectionScope.CurrentUser);

                encryptedValue = VideoTmdbTokenEncryptionPrefix + Convert.ToBase64String(protectedBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecryptVideoTmdbToken(string encryptedValue, out string clearText)
        {
            clearText = string.Empty;

            if (string.IsNullOrWhiteSpace(encryptedValue))
            {
                return true;
            }

            if (!encryptedValue.StartsWith(VideoTmdbTokenEncryptionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var base64Value = encryptedValue.Substring(VideoTmdbTokenEncryptionPrefix.Length);
                var protectedBytes = Convert.FromBase64String(base64Value);
                var clearBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    VideoTmdbTokenEntropy,
                    DataProtectionScope.CurrentUser);

                clearText = Encoding.UTF8.GetString(clearBytes);
                return true;
            }
            catch
            {
                clearText = string.Empty;
                return false;
            }
        }

        private void RestoreVideoTmdbTokenFromConfig()
        {
            videoTmdbReadAccessToken = string.Empty;
            VideoTmdbTokenStorageNeedsSave = false;

            if (string.IsNullOrWhiteSpace(VideoTmdbReadAccessTokenEncrypted))
            {
                return;
            }

            if (TryDecryptVideoTmdbToken(VideoTmdbReadAccessTokenEncrypted, out var decryptedValue))
            {
                videoTmdbReadAccessToken = decryptedValue ?? string.Empty;
                return;
            }

            VideoTmdbReadAccessTokenEncrypted = string.Empty;
            VideoTmdbTokenStorageNeedsSave = true;
            logger?.Warn("[AnikiHelper][VideoCenter] The saved TMDb token could not be decrypted on this Windows account. Enter it again in Aniki Helper settings.");
        }

        private static bool TryEncryptSteamWebApiToken(string clearText, out string encryptedValue)
        {
            encryptedValue = string.Empty;

            if (string.IsNullOrEmpty(clearText))
            {
                return true;
            }

            try
            {
                var clearBytes = Encoding.UTF8.GetBytes(clearText);
                var protectedBytes = ProtectedData.Protect(
                    clearBytes,
                    SteamWebApiTokenEntropy,
                    DataProtectionScope.CurrentUser);

                encryptedValue = SteamWebApiTokenEncryptionPrefix + Convert.ToBase64String(protectedBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecryptSteamWebApiToken(string encryptedValue, out string clearText)
        {
            clearText = string.Empty;

            if (string.IsNullOrWhiteSpace(encryptedValue))
            {
                return true;
            }

            if (!encryptedValue.StartsWith(SteamWebApiTokenEncryptionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var base64Value = encryptedValue.Substring(SteamWebApiTokenEncryptionPrefix.Length);
                var protectedBytes = Convert.FromBase64String(base64Value);
                var clearBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    SteamWebApiTokenEntropy,
                    DataProtectionScope.CurrentUser);

                clearText = Encoding.UTF8.GetString(clearBytes);
                return true;
            }
            catch
            {
                clearText = string.Empty;
                return false;
            }
        }

        private void RestoreSteamWebApiTokenFromConfig()
        {
            steamWebApiToken = string.Empty;
            SteamWebApiTokenStorageNeedsSave = false;

            if (string.IsNullOrWhiteSpace(SteamWebApiTokenEncrypted))
            {
                return;
            }

            if (TryDecryptSteamWebApiToken(SteamWebApiTokenEncrypted, out var decryptedValue))
            {
                steamWebApiToken = decryptedValue ?? string.Empty;
                return;
            }

            SteamWebApiTokenEncrypted = string.Empty;
            SteamWebApiTokenStorageNeedsSave = true;
            logger?.Warn("[AnikiHelper] The saved Steam WebLogin token could not be decrypted on this Windows account. Reconnect Steam in Aniki Helper settings.");
        }

        private static bool TryEncryptSteamApiKey(string clearText, out string encryptedValue)
        {
            encryptedValue = string.Empty;

            if (string.IsNullOrEmpty(clearText))
            {
                return true;
            }

            try
            {
                var clearBytes = Encoding.UTF8.GetBytes(clearText);
                var protectedBytes = ProtectedData.Protect(
                    clearBytes,
                    SteamApiKeyEntropy,
                    DataProtectionScope.CurrentUser);

                encryptedValue = SteamApiKeyEncryptionPrefix + Convert.ToBase64String(protectedBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecryptSteamApiKey(string encryptedValue, out string clearText)
        {
            clearText = string.Empty;

            if (string.IsNullOrWhiteSpace(encryptedValue))
            {
                return true;
            }

            if (!encryptedValue.StartsWith(SteamApiKeyEncryptionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var base64Value = encryptedValue.Substring(SteamApiKeyEncryptionPrefix.Length);
                var protectedBytes = Convert.FromBase64String(base64Value);
                var clearBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    SteamApiKeyEntropy,
                    DataProtectionScope.CurrentUser);

                clearText = Encoding.UTF8.GetString(clearBytes);
                return true;
            }
            catch
            {
                clearText = string.Empty;
                return false;
            }
        }

        private void RestoreSteamApiKeyFromConfig(string configPath)
        {
            steamApiKey = string.Empty;
            SteamApiKeyStorageNeedsSave = false;

            string legacyValue = null;
            var legacyPropertyExists = false;

            // Inspect the raw JSON so migration works even if Playnite's serializer
            // ignores DontSerialize during deserialization.
            try
            {
                if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
                {
                    var root = JObject.Parse(File.ReadAllText(configPath));
                    var legacyToken = root[nameof(SteamApiKey)];

                    if (legacyToken != null)
                    {
                        legacyPropertyExists = true;
                        legacyValue = legacyToken.Type == JTokenType.String
                            ? legacyToken.Value<string>()
                            : null;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to inspect the legacy Steam Web API key during config migration.");
            }

            if (!string.IsNullOrWhiteSpace(SteamApiKeyEncrypted))
            {
                if (TryDecryptSteamApiKey(SteamApiKeyEncrypted, out var decryptedValue))
                {
                    steamApiKey = decryptedValue ?? string.Empty;

                    // Remove a leftover clear-text property if both formats exist.
                    SteamApiKeyStorageNeedsSave = legacyPropertyExists;
                    return;
                }

                // If an encrypted value is unusable but an old clear-text value is
                // still present, recover from it and immediately migrate it.
                if (!string.IsNullOrEmpty(legacyValue) &&
                    TryEncryptSteamApiKey(legacyValue, out var recoveredValue))
                {
                    steamApiKey = legacyValue;
                    SteamApiKeyEncrypted = recoveredValue;
                    SteamApiKeyStorageNeedsSave = true;
                    logger?.Info("[AnikiHelper] Steam Web API key recovered from legacy storage and migrated to encrypted storage.");
                    return;
                }

                // This normally means that config.json was copied from another
                // Windows account or computer. The user must enter the key again.
                SteamApiKeyEncrypted = string.Empty;
                SteamApiKeyStorageNeedsSave = true;
                logger?.Warn("[AnikiHelper] The encrypted Steam Web API key could not be decrypted on this Windows account. Please enter it again in the plugin settings.");
                return;
            }

            if (string.IsNullOrEmpty(legacyValue))
            {
                // Save once to remove an obsolete empty SteamApiKey property.
                SteamApiKeyStorageNeedsSave = legacyPropertyExists;
                return;
            }

            // Migration from versions that stored SteamApiKey in clear text.
            steamApiKey = legacyValue;

            if (TryEncryptSteamApiKey(legacyValue, out var migratedValue))
            {
                SteamApiKeyEncrypted = migratedValue;
                SteamApiKeyStorageNeedsSave = true;
                logger?.Info("[AnikiHelper] Steam Web API key migrated to encrypted storage.");
            }
            else
            {
                // Keep the old config untouched so the key is not lost.
                logger?.Error("[AnikiHelper] Failed to migrate the Steam Web API key to encrypted storage. The existing config.json was left unchanged.");
            }
        }

        private AnikiHelperSettings LoadSettingsSafe(global::AnikiHelper.AnikiHelper plugin)
        {
            try
            {
                var loadedSettings = plugin.LoadPluginSettings<AnikiHelperSettings>();

                if (loadedSettings != null)
                {
                    loadedSettings.logger = logger;
                    loadedSettings.RestoreSteamApiKeyFromConfig(
                        Path.Combine(plugin.GetPluginUserDataPath(), "config.json"));
                    loadedSettings.RestoreSteamWebApiTokenFromConfig();
                    loadedSettings.RestoreVideoTmdbTokenFromConfig();
                }

                return loadedSettings;
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
            : this(plugin, true)
        {
        }

        private AnikiHelperSettings(global::AnikiHelper.AnikiHelper plugin, bool loadPersistedSettings)
        {
            this.plugin = plugin;
            logger = LogManager.GetLogger();

            SetAnikiThemeOptionCommand = new RelayCommand<string>(p => plugin?.SetAnikiThemeOption(p));
            ToggleAnikiThemeOptionCommand = new RelayCommand<string>(p => plugin?.ToggleAnikiThemeOption(p));
            SelectAnikiThemePresetCommand = new RelayCommand<string>(p => plugin?.SelectAnikiThemePreset(p));
            ShowAnikiThemePresetPreviewCommand = new RelayCommand<string>(p => plugin?.ShowAnikiThemePresetPreview(p));
            HideAnikiThemePresetPreviewCommand = new RelayCommand(() => plugin?.HideAnikiThemePresetPreview());
            ReloadAnikiThemeSettingsCommand = new RelayCommand(() => plugin?.ReloadAnikiThemeSettings());
            SelectAnikiThemeSettingsCategoryCommand = new RelayCommand<string>(p => SelectAnikiThemeSettingsCategory(p));
            ToggleLoginRandomPoolCandidateCommand = new RelayCommand<string>(ToggleLoginRandomPoolCandidate);
            SelectAllLoginRandomCandidatesCommand = new RelayCommand(SelectAllLoginRandomCandidates);
            ClearAllLoginRandomCandidatesCommand = new RelayCommand(ClearAllLoginRandomCandidates);
            ToggleThemeColorRandomPoolCandidateCommand = new RelayCommand<string>(ToggleThemeColorRandomPoolCandidate);
            SelectAllThemeColorRandomCandidatesCommand = new RelayCommand(SelectAllThemeColorRandomCandidates);
            ClearAllThemeColorRandomCandidatesCommand = new RelayCommand(ClearAllThemeColorRandomCandidates);
            ToggleVisualPackRandomPoolCandidateCommand = new RelayCommand<string>(ToggleVisualPackRandomPoolCandidate);
            SelectAllVisualPackRandomCandidatesCommand = new RelayCommand(SelectAllVisualPackRandomCandidates);
            ClearAllVisualPackRandomCandidatesCommand = new RelayCommand(ClearAllVisualPackRandomCandidates);
            ClearInGameOverlayNeverSuspendGamesCommand = new RelayCommand(ClearInGameOverlayNeverSuspendGames);
            ClearGameLaunchSplashDisabledGamesCommand = new RelayCommand(ClearGameLaunchSplashDisabledGames);
            PreviewScreenSaverCommand = new RelayCommand(() => plugin?.PreviewScreenSaver());
            TestGlobalNotificationCommand = new RelayCommand(() => plugin?.ShowTestGlobalNotification());


            // Keep the constructor lightweight. Media, memories and achievement caches
            // are warmed after the first fullscreen render, while the full Media Gallery
            // remains lazy-loaded only when the user opens it.
            var hubShortcutsDefaultsMigrationApplied = false;
            var debugLogsStateNeedsSave = false;
            var globalNotificationOverlayMigrationApplied = false;
            var splashMaximumWaitMigrationApplied = false;
            var saved = loadPersistedSettings ? LoadSettingsSafe(plugin) : null;
            if (saved != null)
            {
                AnikiThemeSettingsValues = saved.AnikiThemeSettingsValues
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                AnikiThemeSettingsSelectedPresets = saved.AnikiThemeSettingsSelectedPresets
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Load debug state without using the public setter: using the setter here
                // would restart the 24-hour timer on every Playnite launch.
                enableDebugLogs = saved.EnableDebugLogs;
                debugLogsEnabledUtc = saved.DebugLogsEnabledUtc;
                showDesktopSidebarSettingsShortcut = saved.ShowDesktopSidebarSettingsShortcut;

                if (enableDebugLogs)
                {
                    var nowUtc = DateTime.UtcNow;

                    if (!debugLogsEnabledUtc.HasValue)
                    {
                        // Migration for users who already had Debug Mode enabled before the
                        // auto-expiration feature existed. Give the current troubleshooting
                        // session a fresh 24-hour window instead of disabling it immediately.
                        debugLogsEnabledUtc = nowUtc;
                        debugLogsStateNeedsSave = true;
                        logger?.Info("[AnikiHelper][Debug] Existing Debug Mode session registered for automatic 24-hour expiration.");
                    }
                    else
                    {
                        var enabledUtc = NormalizeDebugLogsUtc(debugLogsEnabledUtc.Value);

                        // Protect against an invalid/future timestamp that could otherwise keep
                        // Debug Mode enabled indefinitely after a clock change or bad config edit.
                        if (enabledUtc > nowUtc.AddMinutes(5))
                        {
                            debugLogsEnabledUtc = nowUtc;
                            debugLogsStateNeedsSave = true;
                            logger?.Info("[AnikiHelper][Debug] Debug Mode timestamp was invalid and has been reset.");
                        }
                        else if ((nowUtc - enabledUtc) >= DebugLogsAutoDisableDuration)
                        {
                            enableDebugLogs = false;
                            debugLogsEnabledUtc = null;
                            debugLogsStateNeedsSave = true;
                            logger?.Info("[AnikiHelper][Debug] Debug Mode automatically disabled after 24 hours.");
                        }
                        else if (enabledUtc != debugLogsEnabledUtc.Value ||
                                 debugLogsEnabledUtc.Value.Kind != DateTimeKind.Utc)
                        {
                            debugLogsEnabledUtc = enabledUtc;
                            debugLogsStateNeedsSave = true;
                        }
                    }
                }
                else if (debugLogsEnabledUtc.HasValue)
                {
                    // Keep the persisted state clean when Debug Mode is off.
                    debugLogsEnabledUtc = null;
                    debugLogsStateNeedsSave = true;
                }

                IncludeHidden = saved.IncludeHidden;
                TopPlayedMax = saved.TopPlayedMax <= 0 ? 10 : saved.TopPlayedMax;
                PlaytimeStoredInHours = saved.PlaytimeStoredInHours;
                PlaytimeUseDaysFormat = saved.PlaytimeUseDaysFormat;

                OpenWelcomeHubOnStartup = saved.OpenWelcomeHubOnStartup;
                HubVideoCenterPageEnabled = saved.HubVideoCenterPageEnabled;
                HubAppsEnabled = saved.HubAppsEnabled;
                HubAppSlot1ToolName = saved.HubAppSlot1ToolName ?? string.Empty;
                HubAppSlot2ToolName = saved.HubAppSlot2ToolName ?? string.Empty;
                HubAppSlot3ToolName = saved.HubAppSlot3ToolName ?? string.Empty;
                HubAppSlot4ToolName = saved.HubAppSlot4ToolName ?? string.Empty;
                HubAppSlot1BackgroundPath = saved.HubAppSlot1BackgroundPath ?? string.Empty;
                HubAppSlot2BackgroundPath = saved.HubAppSlot2BackgroundPath ?? string.Empty;
                HubAppSlot3BackgroundPath = saved.HubAppSlot3BackgroundPath ?? string.Empty;
                HubAppSlot4BackgroundPath = saved.HubAppSlot4BackgroundPath ?? string.Empty;
                HubShortcutsDefaultsVersion = saved.HubShortcutsDefaultsVersion;

                SteamStoreLanguage = string.IsNullOrWhiteSpace(saved.SteamStoreLanguage) ? "english" : saved.SteamStoreLanguage;
                SteamStoreRegion = string.IsNullOrWhiteSpace(saved.SteamStoreRegion) ? "US" : saved.SteamStoreRegion;
                SteamStoreEnabled = saved.SteamStoreEnabled;

                SteamFriendsEnabled = saved.SteamFriendsEnabled;
                SteamApiKey = saved.SteamApiKey ?? string.Empty;
                SteamId64 = saved.SteamId64 ?? string.Empty;
                SteamAccountSteamId64 = saved.SteamAccountSteamId64 ?? string.Empty;
                SteamAccountProfileUrl = saved.SteamAccountProfileUrl ?? string.Empty;
                SteamWebApiToken = saved.SteamWebApiToken ?? string.Empty;
                SteamAccountConnected = !string.IsNullOrWhiteSpace(SteamAccountSteamId64);
                SteamAccountStatus = SteamAccountConnected
                    ? (SteamAccountServicesReady
                        ? "Steam account remembered. Steam services are ready."
                        : "Steam account remembered. Click Check connection to refresh the Steam session.")
                    : "Not connected";
                ShowOffline = saved.ShowOffline;
                NotifyOnGameStart = saved.NotifyOnGameStart;
                NotifyOnConnect = saved.NotifyOnConnect;

                VisualPackCreatorPath = saved.VisualPackCreatorPath ?? string.Empty;
                CustomFilterIconsFolder = saved.CustomFilterIconsFolder ?? string.Empty;
                CustomFilterBackgroundsFolder = saved.CustomFilterBackgroundsFolder ?? string.Empty;
                CustomSourceIconsFolder = saved.CustomSourceIconsFolder ?? string.Empty;
                CustomBannerAboveCoverFolder = saved.CustomBannerAboveCoverFolder ?? string.Empty;
                CustomBannerOnCoverFolder = saved.CustomBannerOnCoverFolder ?? string.Empty;

                LoginRandomIndex = saved.LoginRandomIndex;
                LastLoginRandomIndex = saved.LastLoginRandomIndex;
                LastLoginRandomCandidateId = saved.LastLoginRandomCandidateId ?? string.Empty;
                LoginRandomExcludedCandidateIds = saved.LoginRandomExcludedCandidateIds != null
                    ? new List<string>(saved.LoginRandomExcludedCandidateIds)
                    : new List<string>();
                LastThemeColorRandomCandidateId = saved.LastThemeColorRandomCandidateId ?? string.Empty;
                ThemeColorRandomExcludedCandidateIds = saved.ThemeColorRandomExcludedCandidateIds != null
                    ? new List<string>(saved.ThemeColorRandomExcludedCandidateIds)
                    : new List<string>();
                LastVisualPackRandomCandidateId = saved.LastVisualPackRandomCandidateId ?? string.Empty;
                VisualPackRandomExcludedCandidateIds = saved.VisualPackRandomExcludedCandidateIds != null
                    ? new List<string>(saved.VisualPackRandomExcludedCandidateIds)
                    : new List<string>();
                LastSeenWhatsNewVersion = saved.LastSeenWhatsNewVersion ?? string.Empty;
                SteamBannerResetMigrationVersion = saved.SteamBannerResetMigrationVersion;
                PendingFirstSetupAddonInstallIds = saved.PendingFirstSetupAddonInstallIds != null
                    ? new List<string>(saved.PendingFirstSetupAddonInstallIds)
                    : new List<string>();
                PendingFirstSetupAddonInstallIndex = Math.Max(0, saved.PendingFirstSetupAddonInstallIndex);
                PendingFirstSetupAddonInstallCurrentLaunched =
                    saved.PendingFirstSetupAddonInstallCurrentLaunched;

                WebBrowserHomeUrl = saved.WebBrowserHomeUrl ?? string.Empty;
                WebBrowserFavorites = new ObservableCollection<AnikiWebFavorite>(
                    (saved.WebBrowserFavorites ?? new ObservableCollection<AnikiWebFavorite>())
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                        .Select(x => x.Clone()));

                // Migrate the single URL used by v1/v2 into the first favorite.
                if (WebBrowserFavorites.Count == 0 &&
                    !string.IsNullOrWhiteSpace(WebBrowserHomeUrl))
                {
                    var migratedUrl = NormalizeWebFavoriteUrl(WebBrowserHomeUrl);
                    if (!string.IsNullOrWhiteSpace(migratedUrl))
                    {
                        WebBrowserFavorites.Add(new AnikiWebFavorite
                        {
                            Name = GetWebFavoriteDefaultName(migratedUrl),
                            Url = migratedUrl
                        });
                    }
                }

                InGameOverlayEnabled = saved.InGameOverlayEnabled;

                InGameOverlayHotkey = string.IsNullOrWhiteSpace(saved.InGameOverlayHotkey)
                    ? "CtrlShiftF12"
                    : saved.InGameOverlayHotkey;

                InGameOverlayControllerShortcut = string.IsNullOrWhiteSpace(saved.InGameOverlayControllerShortcut)
                    ? "StartBack"
                    : saved.InGameOverlayControllerShortcut;

                InGameOverlayVirtualKeyboardProvider = string.IsNullOrWhiteSpace(saved.InGameOverlayVirtualKeyboardProvider)
                    ? "Aniki"
                    : saved.InGameOverlayVirtualKeyboardProvider;

                InGameOverlayVirtualKeyboardShortcut = string.IsNullOrWhiteSpace(saved.InGameOverlayVirtualKeyboardShortcut)
                    ? "L3R3Hold"
                    : saved.InGameOverlayVirtualKeyboardShortcut;

                InGameOverlayGamepadMouseShortcut = string.IsNullOrWhiteSpace(saved.InGameOverlayGamepadMouseShortcut)
                    ? "BackR3"
                    : saved.InGameOverlayGamepadMouseShortcut;

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
                var hasSavedScreenSaverSettings = saved.ScreenSaverIdleDelayMinutes != 0;
                ScreenSaverEnabled = hasSavedScreenSaverSettings ? saved.ScreenSaverEnabled : true;
                ScreenSaverIdleDelayMinutes = hasSavedScreenSaverSettings ? saved.ScreenSaverIdleDelayMinutes : 1;
                ScreenSaverChangeIntervalSeconds = saved.ScreenSaverChangeIntervalSeconds > 0 ? saved.ScreenSaverChangeIntervalSeconds : 15;
                ScreenSaverSource = hasSavedScreenSaverSettings ? saved.ScreenSaverSource : global::AnikiHelper.Services.ScreenSaver.ScreenSaverSource.InstalledGames;
                ScreenSaverUseSplashImages = hasSavedScreenSaverSettings ? saved.ScreenSaverUseSplashImages : true;
                ScreenSaverAmbientMusicEnabled = hasSavedScreenSaverSettings ? saved.ScreenSaverAmbientMusicEnabled : true;
                ScreenSaverShowLogo = hasSavedScreenSaverSettings ? saved.ScreenSaverShowLogo : true;
                ScreenSaverShowInfoCard = hasSavedScreenSaverSettings ? saved.ScreenSaverShowInfoCard : true;
                ScreenSaverAnimateBackground = hasSavedScreenSaverSettings ? saved.ScreenSaverAnimateBackground : true;
                ScreenSaverUseFadeTransitions = hasSavedScreenSaverSettings ? saved.ScreenSaverUseFadeTransitions : true;

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

                // GameLaunchSplashMaximumWaitMs existed before it was exposed/used by the UI.
                // Existing configs therefore commonly contain the old unused 15-second default.
                // Migrate those configs once to the current 90-second behavior. The version marker
                // is required so a user can intentionally choose 15 seconds afterwards.
                if (saved.GameLaunchSplashMaximumWaitSettingsVersion < CurrentGameLaunchSplashMaximumWaitSettingsVersion)
                {
                    GameLaunchSplashMaximumWaitMs = DefaultGameLaunchSplashMaximumWaitMs;
                    GameLaunchSplashMaximumWaitSettingsVersion = CurrentGameLaunchSplashMaximumWaitSettingsVersion;
                    splashMaximumWaitMigrationApplied = true;
                }
                else
                {
                    GameLaunchSplashMaximumWaitMs = saved.GameLaunchSplashMaximumWaitMs;
                    GameLaunchSplashMaximumWaitSettingsVersion = saved.GameLaunchSplashMaximumWaitSettingsVersion;
                }

                CustomGameLaunchSplashImages = saved.CustomGameLaunchSplashImages
                    ?? new Dictionary<Guid, string>();
                CustomGameLaunchSplashMinimumDurations = saved.CustomGameLaunchSplashMinimumDurations
                    ?? new Dictionary<Guid, int>();
                CustomGameLaunchSplashMaximumWaits = saved.CustomGameLaunchSplashMaximumWaits
                    ?? new Dictionary<Guid, int>();
                GameLaunchSplashDisabledGames = saved.GameLaunchSplashDisabledGames != null
                    ? new Dictionary<Guid, string>(saved.GameLaunchSplashDisabledGames)
                    : new Dictionary<Guid, string>();
                ShutdownVideoEnabled = saved.ShutdownVideoEnabled;
                LastSteamRecentCheckUtc = saved.LastSteamRecentCheckUtc;
                EventSoundsEnabled = saved.EventSoundsEnabled;
                MediaGalleryProvider = saved.MediaGalleryProvider;
                AnikiVideoPlayerVolume = saved.AnikiVideoPlayerVolume;
                VideoNetworkLocation1Name = saved.VideoNetworkLocation1Name ?? string.Empty;
                VideoNetworkLocation1Path = saved.VideoNetworkLocation1Path ?? string.Empty;
                VideoNetworkLocation2Name = saved.VideoNetworkLocation2Name ?? string.Empty;
                VideoNetworkLocation2Path = saved.VideoNetworkLocation2Path ?? string.Empty;
                VideoNetworkLocation3Name = saved.VideoNetworkLocation3Name ?? string.Empty;
                VideoNetworkLocation3Path = saved.VideoNetworkLocation3Path ?? string.Empty;
                VideoNetworkLocation4Name = saved.VideoNetworkLocation4Name ?? string.Empty;
                VideoNetworkLocation4Path = saved.VideoNetworkLocation4Path ?? string.Empty;
                VideoMoviesLibraryPath = saved.VideoMoviesLibraryPath ?? string.Empty;
                VideoSeriesLibraryPath = saved.VideoSeriesLibraryPath ?? string.Empty;
                VideoAnimeLibraryPath = saved.VideoAnimeLibraryPath ?? string.Empty;
                // Assign the backing field during migration. The public setter intentionally clears
                // paths when disabled, but the collections are initialized a little later below.
                videoCustomLibraryEnabled = saved.VideoCustomLibraryEnabled;
                VideoCustomLibraryPath = saved.VideoCustomLibraryPath ?? string.Empty;
                VideoCustomLibraryName = string.IsNullOrWhiteSpace(saved.VideoCustomLibraryName) ? "Custom" : saved.VideoCustomLibraryName;
                VideoCustomLibraryContentType = saved.VideoCustomLibraryContentType;
                VideoThumbnailFfmpegPath = saved.VideoThumbnailFfmpegPath ?? string.Empty;
                VideoFfprobePath = saved.VideoFfprobePath ?? string.Empty;
                VideoOnlineArtworkEnabled = saved.VideoOnlineArtworkEnabled;
                VideoTmdbArtworkEnabled = saved.VideoTmdbArtworkEnabled;
                VideoTmdbArtworkLanguage = saved.VideoTmdbArtworkLanguage ?? string.Empty;
                VideoTmdbReadAccessToken = saved.VideoTmdbReadAccessToken ?? string.Empty;
                VideoTvmazeArtworkEnabled = saved.VideoTvmazeArtworkEnabled;
                VideoAnilistArtworkEnabled = saved.VideoAnilistArtworkEnabled;
                VideoAutoPlayNextEnabled = saved.VideoAutoPlayNextEnabled;
                VideoPreferredAudioLanguage = saved.VideoPreferredAudioLanguage ?? string.Empty;
                VideoSubtitlePreferenceMode = string.IsNullOrWhiteSpace(saved.VideoSubtitlePreferenceMode)
                    ? "default"
                    : saved.VideoSubtitlePreferenceMode;
                VideoPreferredSubtitleLanguage = saved.VideoPreferredSubtitleLanguage ?? string.Empty;

                NewsScanEnabled = saved.NewsScanEnabled;
                LastNewsScanUtc = saved.LastNewsScanUtc;

                GlobalNotificationOverlayDefaultsVersion = saved.GlobalNotificationOverlayDefaultsVersion;
                if (GlobalNotificationOverlayDefaultsVersion < 1)
                {
                    GlobalNotificationOverlayEnabled = true;
                    GlobalNotificationOverlayDefaultsVersion = 1;
                    globalNotificationOverlayMigrationApplied = true;
                    logger?.Info("[AnikiHelper][Migration] Global Notification Overlay enabled by default.");
                }
                else
                {
                    GlobalNotificationOverlayEnabled = saved.GlobalNotificationOverlayEnabled;
                }

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
                HubMostPlayedName = saved.HubMostPlayedName ?? string.Empty;
                HubMostPlayedPlaytime = saved.HubMostPlayedPlaytime ?? string.Empty;
                HubMostPlayedBackgroundPath = saved.HubMostPlayedBackgroundPath ?? string.Empty;
                HubMostPlayedGameId = saved.HubMostPlayedGameId;
                HubMostPlayedIsMonthly = saved.HubMostPlayedIsMonthly;
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

            // Migrate the old single-path Video Center settings and wire change notifications.
            InitializeVideoLibraryPathCollections(saved);

            // Migration for builds that had the fourth library before the explicit enable switch.
            // A legacy configuration with a real custom path remains enabled. New configurations
            // that are explicitly disabled persist no custom path, so they stay disabled.
            if (!videoCustomLibraryEnabled && VideoCustomLibraryPaths != null &&
                VideoCustomLibraryPaths.Any(x => x != null && !string.IsNullOrWhiteSpace(x.Path)))
            {
                videoCustomLibraryEnabled = true;
                OnPropertyChanged(nameof(VideoCustomLibraryEnabled));
            }

            if (saved == null)
            {
                HubShortcutsDefaultsVersion = CurrentHubShortcutsDefaultsVersion;
            }
            else if (HubShortcutsDefaultsVersion < CurrentHubShortcutsDefaultsVersion)
            {
                var previousConfigurationWasUntouched =
                    !saved.HubAppsEnabled &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot1ToolName) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot2ToolName) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot3ToolName) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot4ToolName) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot1BackgroundPath) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot2BackgroundPath) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot3BackgroundPath) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot4BackgroundPath);

                // Version 2 replaces Steam Store with Aniki Media Center in the default Hub cards.
                // Migrate only the exact previous default layout so user customizations are preserved.
                var previousConfigurationWasDefaultLayout =
                    saved.HubAppsEnabled &&
                    string.Equals(saved.HubAppSlot1ToolName, HubFeatureWebBrowserId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(saved.HubAppSlot2ToolName, HubFeatureMediaGalleryId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(saved.HubAppSlot3ToolName, HubFeatureSteamFriendsId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(saved.HubAppSlot4ToolName, HubFeatureSteamStoreId, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot1BackgroundPath) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot2BackgroundPath) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot3BackgroundPath) &&
                    string.IsNullOrWhiteSpace(saved.HubAppSlot4BackgroundPath);

                if (previousConfigurationWasUntouched || previousConfigurationWasDefaultLayout)
                {
                    HubAppsEnabled = true;
                    HubAppSlot1ToolName = HubFeatureWebBrowserId;
                    HubAppSlot2ToolName = HubFeatureMediaGalleryId;
                    HubAppSlot3ToolName = HubFeatureSteamFriendsId;
                    HubAppSlot4ToolName = HubFeatureVideoPlayerId;
                    hubShortcutsDefaultsMigrationApplied = true;
                }

                HubShortcutsDefaultsVersion = CurrentHubShortcutsDefaultsVersion;
                hubShortcutsDefaultsMigrationApplied = true;
            }

            // Built-in choices are immediately available even before Playnite Software Tools
            // finish loading. LoadOverlayApps() later appends external applications.
            RebuildHubShortcutChoices(new List<AnikiOverlayAppItem>());
            RefreshHubApps();

            EnsureSteamFriendsRuntimeCollections();

            if (CustomGameLaunchSplashImages == null)
            {
                CustomGameLaunchSplashImages = new Dictionary<Guid, string>();
            }

            if (CustomGameLaunchSplashMinimumDurations == null)
            {
                CustomGameLaunchSplashMinimumDurations = new Dictionary<Guid, int>();
            }

            if (CustomGameLaunchSplashMaximumWaits == null)
            {
                CustomGameLaunchSplashMaximumWaits = new Dictionary<Guid, int>();
            }

            if (GameLaunchSplashDisabledGames == null)
            {
                GameLaunchSplashDisabledGames = new Dictionary<Guid, string>();
            }

            if (InGameOverlayNeverSuspendGames == null)
            {
                InGameOverlayNeverSuspendGames = new Dictionary<Guid, string>();
            }

            RefreshGameLaunchSplashCustomPriorityOptions();
            RefreshGameLaunchSplashDisabledGameItems();
            RefreshInGameOverlayNeverSuspendGameItems();

            if (saved == null)
            {
                GameLaunchSplashMaximumWaitSettingsVersion = CurrentGameLaunchSplashMaximumWaitSettingsVersion;
            }

            if (saved == null && loadPersistedSettings)
            {
                GlobalNotificationOverlayEnabled = true;
                GlobalNotificationOverlayDefaultsVersion = 1;

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
            else if (loadPersistedSettings && saved != null &&
                     (hubShortcutsDefaultsMigrationApplied ||
                      debugLogsStateNeedsSave ||
                      globalNotificationOverlayMigrationApplied ||
                      splashMaximumWaitMigrationApplied ||
                      saved.SteamApiKeyStorageNeedsSave ||
                      saved.SteamWebApiTokenStorageNeedsSave ||
                      saved.VideoTmdbTokenStorageNeedsSave))
            {
                try
                {
                    plugin.SavePluginSettings(this);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to save migrated settings.");
                }
            }
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

                    plugin?.OpenWindow("ScreenShotsThumbsWindowStyle|NoDim|SecondaryMusic|AdditionalViewSound");
                    plugin?.HookScreenshotsLazyLoad();

                    _ = RefreshCurrentGameMediaFromSelectedGameAsync();
                }

            );

            // Opening a Capture Gallery item is intentionally one-way. The focused
            // ToggleButton may still receive A while the fullscreen viewer is open,
            // but A must never toggle the viewer closed; B owns that action instead.
            OpenMediaGalleryFullscreenViewerCommand = new RelayCommand<object>(source =>
            {
                if (source is System.Windows.Controls.ListBox listBox)
                {
                    listBox.Tag = true;
                }
            });

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
                    OpenMediaGalleryGamesWindow();
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

            OpenQuickAccessFeaturesCommand = new RelayCommand(OpenQuickAccessFeatures);
            CloseQuickAccessFeaturesCommand = new RelayCommand(CloseQuickAccessFeatures);
            OpenQuickAccessFeatureCommand = new RelayCommand<object>(featureId =>
            {
                OpenQuickAccessFeature(featureId?.ToString());
            });

            OpenQuickAccessSoftwareToolsCommand = new RelayCommand(() =>
            {
                LoadOverlayApps();
                plugin?.OpenChildWindow("AppsWindowStyle|FocusFirst|NoDim");
                IsQuickAccessFeaturesOpen = false;
            });

            OpenQuickAccessAudioSwitcherCommand = new RelayCommand(() =>
            {
                plugin?.OpenChildWindow("AudioSwitcherWindowStyle|FocusFirst|RefocusAfterClick|OpenPanelSound");
                IsQuickAccessFeaturesOpen = false;
            });

            OpenCommunityPacksHubCommand = new RelayCommand(() =>
            {
                try
                {
                    IsQuickAccessFeaturesOpen = false;

                    Action openCommunityHub = () =>
                        AnikiHelperFullscreen.Views.FullscreenSettingsView.OpenCommunityPacksHub();

                    // Community Packs is a plugin-owned WPF window, not a theme OpenWindow style.
                    // When launched from Quick Access, fully close the outgoing Quick Access window
                    // first and let its dim/focus cleanup finish before opening the modal Hub.
                    // The Settings > Customization path still calls OpenCommunityPacksHub directly
                    // and therefore keeps its existing behavior unchanged.
                    if (plugin != null)
                    {
                        plugin.OpenAfterTopBarManagerClosed(openCommunityHub);
                    }
                    else
                    {
                        openCommunityHub();
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to open Community Packs from Quick Access.");
                }
            });

            OpenPackCreatorDownloadPageCommand = new RelayCommand(() =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/Mike-Aniki/AnikiPackCreator/releases/latest",
                        UseShellExecute = true
                    });
                    IsQuickAccessFeaturesOpen = false;
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to open the Aniki Pack Creator release page.");
                }
            });

            OpenQuickAccessUniPlaySongCommand = new RelayCommand(() =>
            {
                plugin?.OpenChildWindow("UniPlaySongWindowStyle|FocusFirst|NoDim");
                IsQuickAccessFeaturesOpen = false;
            });

            OpenQuickAccessExtraFromTopBarManagerCommand = new RelayCommand(() => plugin?.OpenQuickAccessExtraFromTopBarManager());

            OpenTopBarFeatureCommand = new RelayCommand<object>(featureId =>
            {
                OpenTopBarFeature(featureId?.ToString());
            });

            OpenChildWindow = new AnikiWindowCommandProvider(
                styleKey => new RelayCommand(() => plugin?.OpenChildWindow(styleKey))
            );

            OpenAchievementActionsCommand = new RelayCommand<object>(
                achievement =>
                {
                    if (!PrepareAchievementActionsForOverlay(achievement))
                    {
                        return;
                    }

                    // RefocusAfterClick keeps controller focus on the clicked physical
                    // action button even if PA rebuilds its list in the background.
                    plugin?.OpenChildWindow("AchievementActionsWindowStyle|FocusFirst|RefocusAfterClick|NoDim");
                }
            );

            ToggleSelectedAchievementGoalCommand = new RelayCommand(
                () => ExecuteSelectedAchievementItemCommand("ToggleAchievementGoalCommand", closeActionMenuAfterExecute: false)
            );

            ToggleSelectedAchievementCapstoneCommand = new RelayCommand(
                () => ExecuteSelectedAchievementItemCommand("ToggleAchievementCapstoneCommand", closeActionMenuAfterExecute: false)
            );

            OpenSelectedAchievementCaptureCommand = new RelayCommand<object>(
                captureKind =>
                {
                    if (!PrepareSelectedAchievementCaptureForOverlay(captureKind))
                    {
                        return;
                    }

                    // Keep the action menu underneath. B closes only the viewer and
                    // returns to the achievement menu, console-style.
                    plugin?.OpenChildWindow("AchievementCaptureViewerWindowStyle|NoDim");
                }
            );

            OpenWebBrowserCommand = new RelayCommand(
                () => plugin?.OpenWebBrowserHome()
            );

            OpenWebBrowserHomeCommand = new RelayCommand(
                () => plugin?.OpenWebBrowserHome()
            );

            CloseWebBrowserCommand = new RelayCommand(
                () => plugin?.CloseWebBrowser()
            );

            OpenWebBrowser = new AnikiWindowCommandProvider(
                address => new RelayCommand(() =>
                {
                    if (string.IsNullOrWhiteSpace(address) ||
                        string.Equals(address.Trim(), "Home", StringComparison.OrdinalIgnoreCase))
                    {
                        plugin?.OpenWebBrowserHome();
                    }
                    else
                    {
                        plugin?.OpenWebBrowser(address);
                    }
                })
            );

            AddWebFavoriteCommand = new RelayCommand(AddWebFavorite);
            RemoveWebFavoriteCommand = new RelayCommand<object>(RemoveWebFavorite);
            MoveWebFavoriteUpCommand = new RelayCommand<object>(
                favorite => MoveWebFavorite(favorite, -1));
            MoveWebFavoriteDownCommand = new RelayCommand<object>(
                favorite => MoveWebFavorite(favorite, 1));
            ClearWebBrowserCacheCommand = new RelayCommand(
                () => { _ = plugin?.ClearWebBrowserCacheAsync(); });
            ResetWebBrowserDataCommand = new RelayCommand(
                () => { _ = plugin?.ResetWebBrowserDataAsync(); });

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

            OpenGameLinksWindowCommand = new RelayCommand(
                () =>
                {
                    if (PrepareSelectedGameLinksWindow())
                    {
                        plugin?.OpenWindow("GameLinksWindowStyle|FocusFirst|SecondaryMusic|AdditionalViewSound");
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

            OpenFirstSetupCommand = new RelayCommand(() => plugin?.OpenFirstSetupFromHelpMenu());

            OpenNotificationsCommand = new RelayCommand(() => plugin?.OpenNotificationsMenuFromQuickAccess());

            OpenLockScreenCommand = new RelayCommand(() => plugin?.OpenLockScreenFromQuickAccess());

            OpenPowerMenuCommand = new RelayCommand(() => plugin?.OpenPowerMenuFromQuickAccess());

            OpenPlayniteSettingsCommand = new RelayCommand(() => plugin?.OpenPlayniteSettingsFromShortcut());

            OpenExternalClientsCommand = new RelayCommand(() => plugin?.OpenExternalClientsFromHelpMenu());

            OpenRandomGameCommand = new RelayCommand(() => plugin?.OpenRandomGameFromQuickAccess());

            UpdateGameLibraryCommand = new RelayCommand(() => plugin?.UpdateGameLibraryFromQuickAccess());

            OpenAchievementsCommand = new RelayCommand(() => plugin?.OpenAchievementsFromQuickAccess());
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
                            plugin?.OpenWebBrowser(url, SteamStoreDetailsTitle);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to open Steam Store page in Aniki Web Browser.");
                    }
                }
            );
        }

        public void QueueDeferredStartupCacheWarmup(int delayMs)
        {
            if (deferredStartupCacheWarmupQueued)
            {
                return;
            }

            deferredStartupCacheWarmupQueued = true;
            delayMs = Math.Max(0, delayMs);

            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    // No UI dispatcher is available yet. Keep only thread-safe work here;
                    // the visual Hub caches will be loaded by their normal refresh paths.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000).ConfigureAwait(false);
                        LoadHubAchievementMemoriesFromCacheWhenDatabaseReady();
                        EnsureAchievementMemoriesCacheExists();

                        await Task.Delay(2000).ConfigureAwait(false);
                        LoadDiskUsages();
                    });
                    return;
                }

                dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                        if (delayMs > 0)
                        {
                            await Task.Delay(delayMs);
                        }

                        var sw = Stopwatch.StartNew();

                        await LoadDeferredHubMediaCachesAsync();

                        // Achievement cache inspection/rebuild and drive enumeration are
                        // staggered so they don't all compete with the first Hub render.
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(2000).ConfigureAwait(false);
                            LoadHubAchievementMemoriesFromCacheWhenDatabaseReady();
                            EnsureAchievementMemoriesCacheExists();

                            // Drive enumeration can be slow with disconnected/network drives.
                            await Task.Delay(2000).ConfigureAwait(false);
                            LoadDiskUsages();
                        });

                        global::AnikiHelper.AnikiLog.Debug(logger, $"[AnikiHelper][Startup] Deferred cache warmup queued in {sw.ElapsedMilliseconds}ms.");
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper] Deferred startup cache warmup failed.");
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to queue deferred startup cache warmup.");
            }
        }

        private async Task LoadDeferredHubMediaCachesAsync()
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

                var cacheService = screenshotMediaCacheService;
                List<AnikiMediaItem> latestItems = null;
                List<AnikiMediaItem> memoryItems = null;
                string memorySubtitle = string.Empty;

                await Task.Run(() =>
                {
                    latestItems = cacheService.LoadLatestMediaCache()
                        .Where(x => x != null)
                        .ToList();

                    var selectedMemory = cacheService.LoadMemoriesCache()
                        .Where(x => x != null)
                        .Where(x => x.Screenshots != null && x.Screenshots.Count > 0)
                        .OrderByDescending(x => x.MemoryDate)
                        .Take(20)
                        .OrderBy(x => Guid.NewGuid())
                        .FirstOrDefault();

                    if (selectedMemory != null)
                    {
                        memoryItems = selectedMemory.Screenshots
                            .Where(x => x != null)
                            .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                            .Where(x => File.Exists(x.FilePath))
                            .Take(4)
                            .ToList();

                        memorySubtitle = $"{selectedMemory.GameName} • {selectedMemory.MemoryDate:dd/MM/yyyy}";
                    }
                    else
                    {
                        memoryItems = latestItems
                            .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                            .Where(x => File.Exists(x.FilePath))
                            .Take(4)
                            .ToList();
                    }
                }).ConfigureAwait(true);

                ReplaceMediaCollection(HubLatestMediaItems, latestItems);
                ReplaceMediaCollection(HubMemoryItems, memoryItems);
                HubMemorySubtitle = memorySubtitle;

                OnPropertyChanged(nameof(HubLatestMediaItems));
                OnPropertyChanged(nameof(HasHubLatestMedia));
                OnPropertyChanged(nameof(HubMemoryItems));
                OnPropertyChanged(nameof(HasHubMemory));
                NotifyHubPageDotProperties();
                OnPropertyChanged(nameof(HubMemorySubtitle));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Deferred Hub media cache load failed.");
            }
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
            return LoadUnifiedMediaItemsForGameAsync(gameId).GetAwaiter().GetResult();
        }

        private async Task<List<AnikiMediaItem>> LoadUnifiedMediaItemsForGameAsync(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return new List<AnikiMediaItem>();
            }

            Task<List<AnikiMediaItem>> loadTask;

            lock (unifiedMediaGameCacheLock)
            {
                UnifiedMediaGameCacheEntry cachedEntry;
                if (unifiedMediaGameCache.TryGetValue(gameId, out cachedEntry)
                    && cachedEntry?.Items != null
                    && DateTime.UtcNow - cachedEntry.CreatedUtc <= UnifiedMediaGameCacheDuration)
                {
                    return new List<AnikiMediaItem>(cachedEntry.Items);
                }

                if (!unifiedMediaGameLoads.TryGetValue(gameId, out loadTask))
                {
                    loadTask = Task.Run(() => LoadUnifiedMediaItemsForGameCore(gameId));
                    unifiedMediaGameLoads[gameId] = loadTask;
                }
            }

            try
            {
                var loadedItems = await loadTask.ConfigureAwait(false);

                lock (unifiedMediaGameCacheLock)
                {
                    unifiedMediaGameCache[gameId] = new UnifiedMediaGameCacheEntry
                    {
                        CreatedUtc = DateTime.UtcNow,
                        Items = loadedItems ?? new List<AnikiMediaItem>()
                    };
                }

                return new List<AnikiMediaItem>(loadedItems ?? new List<AnikiMediaItem>());
            }
            finally
            {
                lock (unifiedMediaGameCacheLock)
                {
                    Task<List<AnikiMediaItem>> currentTask;
                    if (unifiedMediaGameLoads.TryGetValue(gameId, out currentTask)
                        && ReferenceEquals(currentTask, loadTask))
                    {
                        unifiedMediaGameLoads.Remove(gameId);
                    }
                }
            }
        }

        private List<AnikiMediaItem> LoadUnifiedMediaItemsForGameCore(Guid gameId)
        {
            var allItems = new List<AnikiMediaItem>();

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

        private void InvalidateUnifiedMediaItemsForGame(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            lock (unifiedMediaGameCacheLock)
            {
                unifiedMediaGameCache.Remove(gameId);
            }
        }

        private List<AnikiMediaItem> NormalizeUnifiedMediaItems(IEnumerable<AnikiMediaItem> items)
        {
            var rawItems = (items ?? Enumerable.Empty<AnikiMediaItem>()).ToList();

            global::AnikiHelper.AnikiLog.Debug(logger, $"[AnikiHelper] Unified raw media count: {rawItems.Count}");
            global::AnikiHelper.AnikiLog.Debug(logger, $"[AnikiHelper] Visualizer count: {rawItems.Count(x => x.SourceProvider == "Screenshots Visualizer")}");
            global::AnikiHelper.AnikiLog.Debug(logger, $"[AnikiHelper] Utilities count: {rawItems.Count(x => x.SourceProvider == "Screenshot Utilities - Local")}");

            var duplicateCount = rawItems
                .Where(x => x != null)
                .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                .GroupBy(x => NormalizeMediaItemPath(x.FilePath), StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() > 1);

            global::AnikiHelper.AnikiLog.Debug(logger, $"[AnikiHelper] Unified duplicate file groups removed: {duplicateCount}");

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
                                global::AnikiHelper.AnikiLog.Debug(logger, ex, "[AnikiHelper] Failed to generate media thumbnail.");
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
                                global::AnikiHelper.AnikiLog.Debug(logger, ex, "[AnikiHelper] Failed to generate media thumbnail for selected game.");
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
                        global::AnikiHelper.AnikiLog.Debug(logger,
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
                            global::AnikiHelper.AnikiLog.Debug(logger,
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
                            global::AnikiHelper.AnikiLog.Debug(logger,
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
            // This method follows a real provider refresh or a game stop.
            // Force the next read to use the current JSON files instead of the short-lived cache.
            InvalidateUnifiedMediaItemsForGame(gameId);
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
                    global::AnikiHelper.AnikiLog.Debug(logger,
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

        private bool IsBuiltInHubFeatureId(string selectionId)
        {
            return string.Equals(selectionId, HubFeatureWebBrowserId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selectionId, HubFeatureMediaGalleryId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selectionId, HubFeatureSteamFriendsId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selectionId, HubFeatureSteamStoreId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selectionId, HubFeatureMusicPlayerId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selectionId, HubFeatureVideoPlayerId, StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveActiveThemeFeatureAssetPath(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || plugin?.PlayniteApi?.Paths == null)
                {
                    return string.Empty;
                }

                var themeId = plugin.PlayniteApi.ApplicationSettings?.FullscreenTheme;
                if (string.IsNullOrWhiteSpace(themeId))
                {
                    return string.Empty;
                }

                var roots = new[]
                {
                    plugin.PlayniteApi.Paths.ConfigurationPath,
                    plugin.PlayniteApi.Paths.ApplicationPath
                };

                foreach (var root in roots
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(
                        root,
                        "Themes",
                        "Fullscreen",
                        themeId,
                        "Images",
                        "Hub",
                        "Features",
                        fileName);

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to resolve a built-in Hub feature asset.");
            }

            return string.Empty;
        }

        private AnikiOverlayAppItem CreateBuiltInHubFeatureItem(string selectionId)
        {
            if (string.Equals(selectionId, HubFeatureWebBrowserId, StringComparison.OrdinalIgnoreCase))
            {
                return new AnikiOverlayAppItem
                {
                    Name = Loc("HubFeature_WebBrowser", "Aniki Web Browser"),
                    ActionId = HubFeatureWebBrowserId,
                    IconPath = ResolveActiveThemeFeatureAssetPath("WebBrowser_Icon.png"),
                    BackgroundImagePath = ResolveActiveThemeFeatureAssetPath("WebBrowser_Background.png")
                };
            }

            if (string.Equals(selectionId, HubFeatureMediaGalleryId, StringComparison.OrdinalIgnoreCase))
            {
                return new AnikiOverlayAppItem
                {
                    Name = Loc("HubFeature_MediaGallery", "Capture Gallery"),
                    ActionId = HubFeatureMediaGalleryId,
                    IconPath = ResolveActiveThemeFeatureAssetPath("MediaGallery_Icon.png"),
                    BackgroundImagePath = ResolveActiveThemeFeatureAssetPath("MediaGallery_Background.png")
                };
            }

            if (string.Equals(selectionId, HubFeatureSteamFriendsId, StringComparison.OrdinalIgnoreCase))
            {
                return new AnikiOverlayAppItem
                {
                    Name = Loc("HubFeature_SteamFriends", "Steam Friends"),
                    ActionId = HubFeatureSteamFriendsId,
                    IconPath = ResolveActiveThemeFeatureAssetPath("SteamFriends_Icon.png"),
                    BackgroundImagePath = ResolveActiveThemeFeatureAssetPath("SteamFriends_Background.png")
                };
            }

            if (string.Equals(selectionId, HubFeatureSteamStoreId, StringComparison.OrdinalIgnoreCase))
            {
                return new AnikiOverlayAppItem
                {
                    Name = Loc("HubFeature_SteamStore", "Steam Store"),
                    ActionId = HubFeatureSteamStoreId,
                    IconPath = ResolveActiveThemeFeatureAssetPath("SteamStore_Icon.png"),
                    BackgroundImagePath = ResolveActiveThemeFeatureAssetPath("SteamStore_Background.png")
                };
            }

            if (string.Equals(selectionId, HubFeatureMusicPlayerId, StringComparison.OrdinalIgnoreCase))
            {
                return new AnikiOverlayAppItem
                {
                    Name = Loc("HubFeature_MusicPlayer", "Music Player"),
                    ActionId = HubFeatureMusicPlayerId,
                    IconPath = ResolveActiveThemeFeatureAssetPath("MusicPlayer_Icon.png"),
                    BackgroundImagePath = ResolveActiveThemeFeatureAssetPath("MusicPlayer_Background.png")
                };
            }

            if (string.Equals(selectionId, HubFeatureVideoPlayerId, StringComparison.OrdinalIgnoreCase))
            {
                return new AnikiOverlayAppItem
                {
                    Name = Loc("HubFeature_VideoPlayer", "Aniki Media Center"),
                    ActionId = HubFeatureVideoPlayerId,
                    IconPath = ResolveActiveThemeFeatureAssetPath("AnikiMediaCenter_Icon.png"),
                    BackgroundImagePath = ResolveActiveThemeFeatureAssetPath("AnikiMediaCenter_Background.png")
                };
            }

            return null;
        }

        private void RebuildHubShortcutChoices(IList<AnikiOverlayAppItem> softwareItems)
        {
            HubShortcutChoices.Clear();

            HubShortcutChoices.Add(new AnikiHubShortcutChoice
            {
                Id = string.Empty,
                DisplayName = Loc("HubApps_None", "None")
            });

            HubShortcutChoices.Add(new AnikiHubShortcutChoice
            {
                Id = "header:aniki-features",
                DisplayName = Loc("HubApps_GroupAnikiFeatures", "ANIKI FEATURES"),
                IsHeader = true
            });

            var builtInFeatures = new[]
            {
                CreateBuiltInHubFeatureItem(HubFeatureWebBrowserId),
                CreateBuiltInHubFeatureItem(HubFeatureMediaGalleryId),
                CreateBuiltInHubFeatureItem(HubFeatureSteamFriendsId),
                CreateBuiltInHubFeatureItem(HubFeatureSteamStoreId),
                CreateBuiltInHubFeatureItem(HubFeatureMusicPlayerId),
                CreateBuiltInHubFeatureItem(HubFeatureVideoPlayerId)
            };

            foreach (var feature in builtInFeatures.Where(x => x != null))
            {
                HubShortcutChoices.Add(new AnikiHubShortcutChoice
                {
                    Id = feature.ActionId,
                    DisplayName = feature.Name
                });
            }

            var normalizedSoftwareItems = (softwareItems ?? new List<AnikiOverlayAppItem>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Name)
                .ToList();

            if (normalizedSoftwareItems.Count > 0)
            {
                HubShortcutChoices.Add(new AnikiHubShortcutChoice
                {
                    Id = "header:software-tools",
                    DisplayName = Loc("HubApps_GroupSoftwareTools", "SOFTWARE TOOLS"),
                    IsHeader = true
                });

                foreach (var app in normalizedSoftwareItems)
                {
                    HubShortcutChoices.Add(new AnikiHubShortcutChoice
                    {
                        Id = app.Name,
                        DisplayName = app.Name
                    });
                }
            }

            OnPropertyChanged(nameof(HubShortcutChoices));
        }

        private void RefreshHubApps()
        {
            var existingItems = HubAppItems?.ToList() ?? new List<AnikiOverlayAppItem>();

            try
            {
                var newItems = new List<AnikiOverlayAppItem>();

                var slots = new[]
                {
                    new { SelectionId = HubAppSlot1ToolName, BackgroundPath = HubAppSlot1BackgroundPath },
                    new { SelectionId = HubAppSlot2ToolName, BackgroundPath = HubAppSlot2BackgroundPath },
                    new { SelectionId = HubAppSlot3ToolName, BackgroundPath = HubAppSlot3BackgroundPath },
                    new { SelectionId = HubAppSlot4ToolName, BackgroundPath = HubAppSlot4BackgroundPath }
                };

                var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var slot in slots)
                {
                    if (string.IsNullOrWhiteSpace(slot.SelectionId) || usedIds.Contains(slot.SelectionId))
                    {
                        continue;
                    }

                    AnikiOverlayAppItem source = null;

                    if (IsBuiltInHubFeatureId(slot.SelectionId))
                    {
                        source = CreateBuiltInHubFeatureItem(slot.SelectionId);
                    }
                    else
                    {
                        source = OverlayAppItems?
                            .FirstOrDefault(x => string.Equals(x.Name, slot.SelectionId, StringComparison.OrdinalIgnoreCase));

                        if (source == null)
                        {
                            source = existingItems.FirstOrDefault(x =>
                                string.IsNullOrWhiteSpace(x.ActionId) &&
                                string.Equals(x.Name, slot.SelectionId, StringComparison.OrdinalIgnoreCase));
                        }

                        if (source == null)
                        {
                            // Keep a saved external Software Tool visible while Playnite is still
                            // loading its SoftwareApps collection. A later LoadOverlayApps() call
                            // replaces this placeholder with the real item.
                            source = new AnikiOverlayAppItem
                            {
                                Name = slot.SelectionId
                            };
                        }
                    }

                    if (source == null)
                    {
                        continue;
                    }

                    usedIds.Add(slot.SelectionId);

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
                        ActionId = source.ActionId ?? string.Empty,
                        SourceApp = source.SourceApp
                    });
                }

                HubAppItems.Clear();
                foreach (var item in newItems)
                {
                    HubAppItems.Add(item);
                }

                HubAppsEmptyText = HasSelectedHubAppSlot
                    ? Loc("HubApps_Loading", "Loading selected shortcuts...")
                    : Loc("HubApps_Empty", "Choose features or apps in Aniki Helper settings.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to refresh Hub shortcuts. Keeping previous state.");

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
            // Preserve the current Hub App selections.
            // Clearing SoftwareToolNamesForSelection temporarily resets the ComboBox
            // SelectedItem values in the Settings view.
            var savedHubAppSlot1ToolName = HubAppSlot1ToolName;
            var savedHubAppSlot2ToolName = HubAppSlot2ToolName;
            var savedHubAppSlot3ToolName = HubAppSlot3ToolName;
            var savedHubAppSlot4ToolName = HubAppSlot4ToolName;

            try
            {
                // Playnite's public IGameDatabaseAPI does not expose SoftwareApps.
                // The native Fullscreen Tools window uses the internal GameDatabase.SoftwareApps
                // collection, so we read it through reflection to keep the overlay Apps view
                // available without referencing Playnite internals directly.
                var apps = GetSoftwareAppsForOverlay();

                if ((apps == null || apps.Count == 0) && OverlayAppItems?.Count > 0)
                {
                    logger?.Warn(
                        "[AnikiHelper] Software Tools refresh returned 0 apps. " +
                        "Keeping the previous Software Tools list while built-in features remain available.");

                    RebuildHubShortcutChoices(OverlayAppItems.ToList());

                    HubAppSlot1ToolName = savedHubAppSlot1ToolName;
                    HubAppSlot2ToolName = savedHubAppSlot2ToolName;
                    HubAppSlot3ToolName = savedHubAppSlot3ToolName;
                    HubAppSlot4ToolName = savedHubAppSlot4ToolName;

                    RefreshHubApps();

                    OnPropertyChanged(nameof(OverlayAppItems));
                    OnPropertyChanged(nameof(HasOverlayApps));
                    OnPropertyChanged(nameof(SoftwareToolNamesForSelection));
                    OnPropertyChanged(nameof(HubShortcutChoices));
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

                foreach (var name in items
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    SoftwareToolNamesForSelection.Add(name);
                }

                RebuildHubShortcutChoices(items);

                // Restore the values that the Settings ComboBoxes may have temporarily
                // reset when SoftwareToolNamesForSelection was cleared.
                HubAppSlot1ToolName = savedHubAppSlot1ToolName;
                HubAppSlot2ToolName = savedHubAppSlot2ToolName;
                HubAppSlot3ToolName = savedHubAppSlot3ToolName;
                HubAppSlot4ToolName = savedHubAppSlot4ToolName;

                OverlayAppsEmptyText = "No apps configured. Add Software Tools in Playnite first.";
                RefreshHubApps();

                OnPropertyChanged(nameof(OverlayAppItems));
                OnPropertyChanged(nameof(HasOverlayApps));
                OnPropertyChanged(nameof(SoftwareToolNamesForSelection));
                OnPropertyChanged(nameof(HubShortcutChoices));
                OnPropertyChanged(nameof(OverlayAppsEmptyText));
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    "[AnikiHelper] Failed to load overlay apps. Keeping previous Hub Apps state when possible.");

                if ((OverlayAppItems?.Count > 0 || HubAppItems?.Count > 0) &&
                    HasSelectedHubAppSlot)
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
                RebuildHubShortcutChoices(new List<AnikiOverlayAppItem>());

                // Keep the configured slots even if loading Playnite's Software Tools fails.
                HubAppSlot1ToolName = savedHubAppSlot1ToolName;
                HubAppSlot2ToolName = savedHubAppSlot2ToolName;
                HubAppSlot3ToolName = savedHubAppSlot3ToolName;
                HubAppSlot4ToolName = savedHubAppSlot4ToolName;

                OverlayAppsEmptyText = "No apps configured. Add Software Tools in Playnite first.";
                RefreshHubApps();

                OnPropertyChanged(nameof(OverlayAppItems));
                OnPropertyChanged(nameof(HasOverlayApps));
                OnPropertyChanged(nameof(SoftwareToolNamesForSelection));
                OnPropertyChanged(nameof(HubShortcutChoices));
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
                if (item == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(item.ActionId))
                {
                    OpenBuiltInHubFeature(item.ActionId);
                    return;
                }

                var app = item.SourceApp;
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

        private object GetSelectedAchievementPropertyValue(string propertyName)
        {
            if (SelectedAchievementActionItem == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            try
            {
                var property = SelectedAchievementActionItem
                    .GetType()
                    .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

                return property?.GetValue(SelectedAchievementActionItem);
            }
            catch (Exception ex)
            {
                global::AnikiHelper.AnikiLog.Debug(logger,
                    ex,
                    $"[AnikiHelper][Achievements] Failed to read achievement property '{propertyName}'.");
                return null;
            }
        }

        private string GetSelectedAchievementString(string propertyName)
        {
            return GetSelectedAchievementPropertyValue(propertyName)?.ToString() ?? string.Empty;
        }

        private bool GetSelectedAchievementBool(string propertyName)
        {
            var value = GetSelectedAchievementPropertyValue(propertyName);

            if (value is bool boolValue)
            {
                return boolValue;
            }

            return value != null &&
                   bool.TryParse(value.ToString(), out var parsed) &&
                   parsed;
        }

        private static bool IsUsableAchievementCapturePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private string GetSelectedAchievementCapturePath(string captureKind)
        {
            if (string.Equals(captureKind, "Clean", StringComparison.OrdinalIgnoreCase))
            {
                return SelectedAchievementCleanCapturePath;
            }

            if (string.Equals(captureKind, "Notification", StringComparison.OrdinalIgnoreCase))
            {
                return SelectedAchievementNotificationCapturePath;
            }

            if (string.Equals(captureKind, "Framed", StringComparison.OrdinalIgnoreCase))
            {
                return SelectedAchievementFramedCapturePath;
            }

            if (string.Equals(captureKind, "Video", StringComparison.OrdinalIgnoreCase))
            {
                return SelectedAchievementVideoCapturePath;
            }

            return string.Empty;
        }

        internal bool PrepareAchievementActionsForOverlay(object achievement)
        {
            if (achievement == null)
            {
                return false;
            }

            SelectedAchievementActionItem = achievement;

            // Snapshot writeable states because PlayniteAchievements can rebuild the
            // DynamicAchievements collection immediately after a Goal/Capstone write.
            selectedAchievementGoalState = GetSelectedAchievementBool("IsGoal");
            selectedAchievementCapstoneState = GetSelectedAchievementBool("IsCapstone");

            selectedAchievementFocusApiName = GetSelectedAchievementString("ApiName");
            selectedAchievementFocusName = GetSelectedAchievementString("Name");

            OnPropertyChanged(nameof(SelectedAchievementIsGoal));
            OnPropertyChanged(nameof(SelectedAchievementIsCapstone));

            SelectedAchievementCapturePath = string.Empty;
            SelectedAchievementCaptureIsVideo = false;
            return true;
        }

        internal bool PrepareSelectedAchievementCaptureForOverlay(object captureKind)
        {
            var kind = captureKind?.ToString();
            var path = GetSelectedAchievementCapturePath(kind);

            if (!IsUsableAchievementCapturePath(path))
            {
                return false;
            }

            SelectedAchievementCapturePath = path;
            SelectedAchievementCaptureIsVideo =
                string.Equals(kind, "Video", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private void ExecuteSelectedAchievementItemCommand(
            string commandPropertyName,
            bool closeActionMenuAfterExecute)
        {
            try
            {
                var command = GetSelectedAchievementPropertyValue(commandPropertyName) as ICommand;

                if (command == null || !command.CanExecute(null))
                {
                    return;
                }

                command.Execute(null);

                if (string.Equals(
                        commandPropertyName,
                        "ToggleAchievementGoalCommand",
                        StringComparison.OrdinalIgnoreCase))
                {
                    selectedAchievementGoalState = !SelectedAchievementIsGoal;
                    OnPropertyChanged(nameof(SelectedAchievementIsGoal));
                }
                else if (string.Equals(
                             commandPropertyName,
                             "ToggleAchievementCapstoneCommand",
                             StringComparison.OrdinalIgnoreCase))
                {
                    selectedAchievementCapstoneState = !SelectedAchievementIsCapstone;
                    OnPropertyChanged(nameof(SelectedAchievementIsCapstone));
                }

                // Keep the modal open. PA is free to rebuild/reorder DynamicAchievements
                // behind it, while the controller focus stays inside this modal.
                if (closeActionMenuAfterExecute)
                {
                    plugin?.CloseTopWindow();
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    $"[AnikiHelper][Achievements] Failed to execute '{commandPropertyName}'.");
            }
        }

        public void RestoreSelectedAchievementListFocusAfterActionMenuClose()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (TryRestoreSelectedAchievementListFocus())
                    {
                        return;
                    }

                    // PA coalesces list rebuilds. One delayed fallback handles the case
                    // where the new DynamicAchievements collection is not ready yet.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(140).ConfigureAwait(false);

                        try
                        {
                            await dispatcher.InvokeAsync(
                                new Action(() => TryRestoreSelectedAchievementListFocus()),
                                DispatcherPriority.ContextIdle);
                        }
                        catch
                        {
                        }
                    });
                }),
                DispatcherPriority.ContextIdle);
        }

        private bool TryRestoreSelectedAchievementListFocus()
        {
            try
            {
                var achievementList = FindVisibleNamedElement<ListView>("DynamicAchievementList");
                if (achievementList == null)
                {
                    return false;
                }

                object targetItem = null;

                foreach (var item in achievementList.Items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var apiName = GetObjectStringProperty(item, "ApiName");

                    if (!string.IsNullOrWhiteSpace(selectedAchievementFocusApiName) &&
                        string.Equals(apiName, selectedAchievementFocusApiName, StringComparison.Ordinal))
                    {
                        targetItem = item;
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(selectedAchievementFocusApiName) &&
                        !string.IsNullOrWhiteSpace(selectedAchievementFocusName) &&
                        string.Equals(
                            GetObjectStringProperty(item, "Name"),
                            selectedAchievementFocusName,
                            StringComparison.CurrentCulture))
                    {
                        targetItem = item;
                        break;
                    }
                }

                if (targetItem == null)
                {
                    return false;
                }

                achievementList.SelectedItem = targetItem;
                achievementList.ScrollIntoView(targetItem);

                achievementList.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        try
                        {
                            var container =
                                achievementList.ItemContainerGenerator.ContainerFromItem(targetItem)
                                as ListViewItem;

                            if (container == null)
                            {
                                return;
                            }

                            var button = FindFocusableButton(container);
                            if (button != null)
                            {
                                button.Focus();
                                Keyboard.Focus(button);
                            }
                        }
                        catch (Exception ex)
                        {
                            global::AnikiHelper.AnikiLog.Debug(logger,
                                ex,
                                "[AnikiHelper][Achievements] Failed to focus rebuilt achievement row.");
                        }
                    }),
                    DispatcherPriority.Loaded);

                return true;
            }
            catch (Exception ex)
            {
                global::AnikiHelper.AnikiLog.Debug(logger,
                    ex,
                    "[AnikiHelper][Achievements] Achievement focus restoration failed.");
                return false;
            }
        }

        private static string GetObjectStringProperty(object source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            try
            {
                return source
                           .GetType()
                           .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                           ?.GetValue(source)
                           ?.ToString()
                       ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static T FindVisibleNamedElement<T>(string elementName)
            where T : FrameworkElement
        {
            if (Application.Current == null || string.IsNullOrWhiteSpace(elementName))
            {
                return null;
            }

            try
            {
                var windows = Application.Current.Windows
                    .OfType<Window>()
                    .Where(window => window != null && window.IsVisible)
                    .OrderByDescending(window => window.IsActive)
                    .ToList();

                foreach (var window in windows)
                {
                    var result = FindNamedVisualDescendant<T>(window, elementName);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static T FindNamedVisualDescendant<T>(DependencyObject root, string elementName)
            where T : FrameworkElement
        {
            if (root == null)
            {
                return null;
            }

            if (root is T typed &&
                string.Equals(typed.Name, elementName, StringComparison.Ordinal))
            {
                return typed;
            }

            int count;

            try
            {
                count = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                DependencyObject child;

                try
                {
                    child = VisualTreeHelper.GetChild(root, i);
                }
                catch
                {
                    continue;
                }

                var result = FindNamedVisualDescendant<T>(child, elementName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static ButtonBase FindFocusableButton(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is ButtonBase button &&
                button.Focusable &&
                button.IsEnabled &&
                button.IsVisible)
            {
                return button;
            }

            int count;

            try
            {
                count = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                DependencyObject child;

                try
                {
                    child = VisualTreeHelper.GetChild(root, i);
                }
                catch
                {
                    continue;
                }

                var result = FindFocusableButton(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        public void OpenQuickAccessFeatures()
        {
            IsQuickAccessFeaturesOpen = true;
            plugin?.FocusQuickAccessElement("FeatureWebBrowserButton");
        }

        public void CloseQuickAccessFeatures()
        {
            if (!IsQuickAccessFeaturesOpen)
            {
                return;
            }

            IsQuickAccessFeaturesOpen = false;
            plugin?.FocusQuickAccessElement("QuickFeaturesButton");
        }

        private void OpenTopBarFeature(string featureId)
        {
            if (string.IsNullOrWhiteSpace(featureId))
            {
                return;
            }

            Action openDestination = null;

            switch (featureId.Trim().ToLowerInvariant())
            {
                case "achievements":
                    openDestination = () => plugin?.OpenWindow("AchievementsWindow|SortButton|SecondaryMusic|AdditionalViewSound");
                    break;

                case "friends":
                    openDestination = () => plugin?.OpenWindow("FriendsStyle|SecondaryMusic|AdditionalViewSound");
                    break;

                case "music-player":
                    openDestination = () => plugin?.OpenWindow("MusicPlayerWindowStyle|FocusFirst|AdditionalViewSound");
                    break;

                case "web-browser":
                    openDestination = () => plugin?.OpenWebBrowserHome();
                    break;

                case "media-gallery":
                    openDestination = OpenMediaGalleryGamesWindow;
                    break;

                case "video-player":
                    openDestination = () => plugin?.OpenVideoPlayer();
                    break;

                case "community-packs":
                    openDestination = () =>
                        AnikiHelperFullscreen.Views.FullscreenSettingsView.OpenCommunityPacksHub();
                    break;

                case "software-tools":
                    openDestination = () =>
                    {
                        LoadOverlayApps();
                        plugin?.OpenChildWindow("AppsWindowStyle|FocusFirst|NoDim");
                    };
                    break;

                case "audio-switcher":
                    openDestination = () =>
                        plugin?.OpenChildWindow("AudioSwitcherWindowStyle|FocusFirst|RefocusAfterClick|OpenPanelSound");
                    break;

                case "controller-manager":
                    openDestination = () =>
                        plugin?.OpenWindow("ControllerManagerWindowStyle|ControllerManagerTesterButton|AdditionalViewSound");
                    break;
            }

            if (openDestination == null)
            {
                return;
            }

            plugin?.OpenAfterTopBarManagerClosed(openDestination);
        }

        private void OpenQuickAccessFeature(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            // Keep the submenu visible if Music Player cannot be opened, so the
            // user returns to the same focused item after dismissing the message.
            if (string.Equals(actionId, HubFeatureMusicPlayerId, StringComparison.OrdinalIgnoreCase) &&
                Application.Current?.Properties?.Contains("UniPlaySongPlugin") != true)
            {
                OpenBuiltInHubFeature(actionId);
                plugin?.FocusQuickAccessElement("FeatureMusicPlayerButton");
                return;
            }

            OpenBuiltInHubFeature(actionId);
            IsQuickAccessFeaturesOpen = false;
        }

        private void OpenBuiltInHubFeature(string actionId)
        {
            try
            {
                if (string.Equals(actionId, HubFeatureWebBrowserId, StringComparison.OrdinalIgnoreCase))
                {
                    plugin?.OpenWebBrowserHome();
                    return;
                }

                if (string.Equals(actionId, HubFeatureMediaGalleryId, StringComparison.OrdinalIgnoreCase))
                {
                    OpenMediaGalleryGamesWindow();
                    return;
                }

                if (string.Equals(actionId, HubFeatureSteamFriendsId, StringComparison.OrdinalIgnoreCase))
                {
                    plugin?.OpenWindow("FriendsStyle|SecondaryMusic|AdditionalViewSound");
                    return;
                }

                if (string.Equals(actionId, HubFeatureSteamStoreId, StringComparison.OrdinalIgnoreCase))
                {
                    plugin?.OpenWindow("SteamStoreStyle|FocusFirst|SecondaryMusic|AdditionalViewSound");
                    return;
                }

                if (string.Equals(actionId, HubFeatureMusicPlayerId, StringComparison.OrdinalIgnoreCase))
                {
                    var upsAvailable = Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true;
                    if (!upsAvailable)
                    {
                        plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                            Loc("HubFeature_MusicPlayerRequiresUPS", "UniPlaySong must be installed to open the Music Player."),
                            Loc("HubFeature_MusicPlayer", "Music Player"));
                        return;
                    }

                    plugin?.OpenWindow("MusicPlayerWindowStyle|FocusFirst|AdditionalViewSound");
                    return;
                }

                if (string.Equals(actionId, HubFeatureVideoPlayerId, StringComparison.OrdinalIgnoreCase))
                {
                    plugin?.OpenVideoPlayer();
                    return;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to open a built-in Hub feature.");
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
                NotifyHubPageDotProperties();
                OnPropertyChanged(nameof(HubMemorySubtitle));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load hub memory cache.");

                HubMemoryItems.Clear();
                HubMemorySubtitle = string.Empty;

                OnPropertyChanged(nameof(HubMemoryItems));
                OnPropertyChanged(nameof(HasHubMemory));
                NotifyHubPageDotProperties();
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
                    NotifyHubPageDotProperties();
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
                NotifyHubPageDotProperties();
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

                        global::AnikiHelper.AnikiLog.Debug(logger, "[AnikiHelper] Achievement memories loaded: " + items.Count);

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

                    global::AnikiHelper.AnikiLog.Debug(logger, "[AnikiHelper] Achievement memories loaded: " + items.Count);

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

                // The fullscreen grid binds to the lightweight visible collection, not the
                // complete cache. Rebuild it progressively so changing sort order does not
                // force every card/cover to be recreated in one UI frame.
                _ = PopulateVisibleMediaGalleryGamesAsync(sorted);
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

        private void ResetVisibleMediaGalleryGames()
        {
            // Cancel any population still running from the previous opening/sort operation.
            visibleMediaGalleryGamesLoadVersion++;
            VisibleMediaGalleryGames.Clear();
            OnPropertyChanged(nameof(VisibleMediaGalleryGames));
        }

        private async Task PopulateVisibleMediaGalleryGamesAsync(IEnumerable<AnikiMediaGameItem> games)
        {
            var items = (games ?? Enumerable.Empty<AnikiMediaGameItem>())
                .Where(x => x != null)
                .ToList();

            var requestVersion = ++visibleMediaGalleryGamesLoadVersion;
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null)
            {
                return;
            }

            // Always start from an empty visual collection. The complete MediaGalleryGames
            // cache remains intact in memory and is cheap to sort/count; only card creation is
            // staggered across frames.
            await dispatcher.InvokeAsync(new Action(() =>
            {
                if (requestVersion != visibleMediaGalleryGamesLoadVersion)
                {
                    return;
                }

                VisibleMediaGalleryGames.Clear();
                OnPropertyChanged(nameof(VisibleMediaGalleryGames));
            }), DispatcherPriority.DataBind);

            // Let the real Capture Gallery page render once (header, footer, empty grid) before
            // creating any cover cards. This is the key difference from the old behaviour where
            // Window.Show() had to wait for the entire WrapPanel to materialize.
            await dispatcher.InvokeAsync(new Action(() => { }), DispatcherPriority.ContextIdle);

            for (var startIndex = 0; startIndex < items.Count; startIndex += MediaGalleryGamesUiBatchSize)
            {
                if (requestVersion != visibleMediaGalleryGamesLoadVersion)
                {
                    return;
                }

                var batchStart = startIndex;
                var batchEnd = Math.Min(batchStart + MediaGalleryGamesUiBatchSize, items.Count);

                await dispatcher.InvokeAsync(new Action(() =>
                {
                    if (requestVersion != visibleMediaGalleryGamesLoadVersion)
                    {
                        return;
                    }

                    for (var i = batchStart; i < batchEnd; i++)
                    {
                        VisibleMediaGalleryGames.Add(items[i]);
                    }
                }), DispatcherPriority.Background);

                // Give WPF a render opportunity between batches so covers appear naturally and
                // controller input/audio are not blocked by one large layout pass.
                if (batchEnd < items.Count)
                {
                    await Task.Delay(MediaGalleryGamesUiBatchDelayMs).ConfigureAwait(false);
                }
            }
        }

        private void OpenMediaGalleryGamesWindow()
        {
            try
            {
                // Keep the complete cache in memory, but never bind that complete collection
                // directly while a new Window is being constructed. Otherwise the second and
                // subsequent openings synchronously recreate every card before Window.Show().
                var needsInitialLoad = !mediaGalleryGamesCacheLoaded;
                MediaGalleryLoading = needsInitialLoad;

                // The UI-facing list must be empty before the XAML is instantiated. That makes
                // the real page itself cheap to create and lets it appear immediately.
                ResetVisibleMediaGalleryGames();

                plugin?.OpenWindow("MediaGalleryGamesWindowStyle|NoDim|SecondaryMusic|AdditionalViewSound");

                if (needsInitialLoad)
                {
                    _ = LoadMediaGalleryGamesFromCacheForOpenAsync();
                }
                else
                {
                    // No disk read on subsequent openings: reuse the cached items and materialize
                    // their visual cards in small batches after the page has rendered.
                    var cachedItems = MediaGalleryGames.ToList();
                    _ = PopulateVisibleMediaGalleryGamesAsync(cachedItems);
                }
            }
            catch (Exception ex)
            {
                MediaGalleryLoading = false;
                logger?.Warn(ex, "[AnikiHelper] Failed to open media gallery games window.");
            }
        }

        private async Task LoadMediaGalleryGamesFromCacheForOpenAsync()
        {
            var requestVersion = ++mediaGalleryGamesLoadVersion;

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

                var cacheService = screenshotMediaCacheService;

                // Pure file I/O/deserialization stays off the UI thread.
                var games = await Task.Run(() => cacheService.LoadGamesCacheSnapshot()).ConfigureAwait(false);

                if (requestVersion != mediaGalleryGamesLoadVersion)
                {
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                List<AnikiMediaGameItem> sortedSnapshot = null;

                await dispatcher.InvokeAsync(new Action(() =>
                {
                    if (requestVersion != mediaGalleryGamesLoadVersion)
                    {
                        return;
                    }

                    sortedSnapshot = SortMediaGalleryGames(games).ToList();
                    ReplaceMediaGameCollection(MediaGalleryGames, sortedSnapshot);
                    mediaGalleryGamesCacheLoaded = true;
                    OnPropertyChanged(nameof(MediaGalleryGames));
                    MediaGalleryLoading = false;
                }), DispatcherPriority.DataBind);

                if (requestVersion != mediaGalleryGamesLoadVersion || sortedSnapshot == null)
                {
                    return;
                }

                await PopulateVisibleMediaGalleryGamesAsync(sortedSnapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to asynchronously load media gallery games cache.");

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (requestVersion != mediaGalleryGamesLoadVersion)
                        {
                            return;
                        }

                        MediaGalleryGames.Clear();
                        ResetVisibleMediaGalleryGames();
                        mediaGalleryGamesCacheLoaded = true;
                        OnPropertyChanged(nameof(MediaGalleryGames));
                        MediaGalleryLoading = false;
                    }), DispatcherPriority.DataBind);
                }
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
                var sorted = SortMediaGalleryGames(games).ToList();

                ReplaceMediaGameCollection(MediaGalleryGames, sorted);
                mediaGalleryGamesCacheLoaded = true;

                OnPropertyChanged(nameof(MediaGalleryGames));

                // If Capture Gallery is currently visible this refresh will update it smoothly;
                // if it is closed, the next OpenMediaGalleryGamesWindow call clears this visual
                // collection before creating the window anyway.
                _ = PopulateVisibleMediaGalleryGamesAsync(sorted);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load media gallery games cache.");

                MediaGalleryGames.Clear();
                ResetVisibleMediaGalleryGames();
                mediaGalleryGamesCacheLoaded = true;
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

                plugin?.OpenWindow("ScreenShotsThumbsWindowStyle|NoDim|SecondaryMusic|AdditionalViewSound");

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

                var loadedItems = await LoadUnifiedMediaItemsForGameAsync(gameId);
                var items = await Task.Run(() => ApplyThumbnailsToMediaItems(loadedItems));

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
        public bool IsGameLaunchSplashDisabled(Guid gameId)
        {
            return gameId != Guid.Empty &&
                   GameLaunchSplashDisabledGames != null &&
                   GameLaunchSplashDisabledGames.ContainsKey(gameId);
        }

        public void ToggleGameLaunchSplashDisabled(Playnite.SDK.Models.Game game)
        {
            if (game == null || game.Id == Guid.Empty)
            {
                return;
            }

            SetGameLaunchSplashDisabled(game.Id, !IsGameLaunchSplashDisabled(game.Id), game.Name);
        }

        public void SetGameLaunchSplashDisabled(Guid gameId, bool disabled, string gameName = null)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            if (GameLaunchSplashDisabledGames == null)
            {
                GameLaunchSplashDisabledGames = new Dictionary<Guid, string>();
            }

            if (disabled)
            {
                GameLaunchSplashDisabledGames[gameId] = string.IsNullOrWhiteSpace(gameName)
                    ? gameId.ToString()
                    : gameName;
            }
            else
            {
                GameLaunchSplashDisabledGames.Remove(gameId);
            }

            RefreshGameLaunchSplashDisabledGameItems();
            OnPropertyChanged(nameof(GameLaunchSplashDisabledGames));

            try
            {
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save game launch splash disabled list.");
            }
        }

        public void ClearGameLaunchSplashDisabledGames()
        {
            if (GameLaunchSplashDisabledGames == null || GameLaunchSplashDisabledGames.Count == 0)
            {
                return;
            }

            GameLaunchSplashDisabledGames.Clear();
            RefreshGameLaunchSplashDisabledGameItems();
            OnPropertyChanged(nameof(GameLaunchSplashDisabledGames));

            try
            {
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to clear game launch splash disabled list.");
            }
        }

        private void RefreshGameLaunchSplashDisabledGameItems()
        {
            var items = new ObservableCollection<AnikiSplashDisabledGameItem>();

            if (GameLaunchSplashDisabledGames != null)
            {
                foreach (var pair in GameLaunchSplashDisabledGames.OrderBy(x => x.Value ?? string.Empty))
                {
                    items.Add(new AnikiSplashDisabledGameItem(this, pair.Key, pair.Value));
                }
            }

            GameLaunchSplashDisabledGameItems = items;
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

        private void AddWebFavorite()
        {
            var normalizedUrl = NormalizeWebFavoriteUrl(NewWebFavoriteUrl);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
            {
                plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                    Loc("WebBrowser_FavoriteInvalidUrl", "Enter a valid HTTP or HTTPS address."),
                    Loc("SettingsNav_WebBrowser", "Web Browser"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var existing = WebBrowserFavorites.FirstOrDefault(x =>
                x != null &&
                string.Equals(
                    NormalizeWebFavoriteUrl(x.Url),
                    normalizedUrl,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                    Loc("WebBrowser_FavoriteAlreadyExists", "This address is already in your favorites."),
                    Loc("SettingsNav_WebBrowser", "Web Browser"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var name = (NewWebFavoriteName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = GetWebFavoriteDefaultName(normalizedUrl);
            }

            WebBrowserFavorites.Add(new AnikiWebFavorite
            {
                Name = name,
                Url = normalizedUrl
            });

            NewWebFavoriteName = string.Empty;
            NewWebFavoriteUrl = string.Empty;
            SaveWebBrowserFavoritesNow();
        }

        private void RemoveWebFavorite(object favoriteObject)
        {
            var favorite = favoriteObject as AnikiWebFavorite;
            if (favorite == null)
            {
                return;
            }

            WebBrowserFavorites.Remove(favorite);
            SaveWebBrowserFavoritesNow();
        }

        private void MoveWebFavorite(object favoriteObject, int offset)
        {
            var favorite = favoriteObject as AnikiWebFavorite;
            if (favorite == null || offset == 0)
            {
                return;
            }

            var currentIndex = WebBrowserFavorites.IndexOf(favorite);
            var targetIndex = currentIndex + offset;
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= WebBrowserFavorites.Count)
            {
                return;
            }

            WebBrowserFavorites.Move(currentIndex, targetIndex);
            SaveWebBrowserFavoritesNow();
        }

        private void SaveWebBrowserFavoritesNow()
        {
            try
            {
                plugin?.SavePluginSettings(this);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][WebBrowser] Failed to save favorites.");
            }
        }

        private static string NormalizeWebFavoriteUrl(string input)
        {
            var value = (input ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return null;
            }

            if (!value.Contains("://"))
            {
                value = "https://" + value;
            }

            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                return null;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return uri.AbsoluteUri;
        }

        private static string GetWebFavoriteDefaultName(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri))
            {
                return "Website";
            }

            var host = (uri.Host ?? string.Empty).Trim();
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                host = host.Substring(4);
            }

            var firstPart = host.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstPart))
            {
                return "Website";
            }

            return char.ToUpperInvariant(firstPart[0]) + firstPart.Substring(1);
        }

        private static readonly string[] DefaultOptionPropertyNames =
        {
            nameof(ShowDesktopSidebarSettingsShortcut),
            nameof(EnableDebugLogs),
            nameof(IncludeHidden),
            nameof(TopPlayedMax),
            nameof(PlaytimeStoredInHours),
            nameof(PlaytimeUseDaysFormat),
            nameof(DynamicAutoPrecacheUserEnabled),

            nameof(OpenWelcomeHubOnStartup),
            nameof(HubVideoCenterPageEnabled),
            nameof(HubAppsEnabled),
            nameof(HubAppSlot1ToolName),
            nameof(HubAppSlot2ToolName),
            nameof(HubAppSlot3ToolName),
            nameof(HubAppSlot4ToolName),
            nameof(HubAppSlot1BackgroundPath),
            nameof(HubAppSlot2BackgroundPath),
            nameof(HubAppSlot3BackgroundPath),
            nameof(HubAppSlot4BackgroundPath),
            nameof(HubShortcutsDefaultsVersion),

            nameof(EventSoundsEnabled),
            nameof(NewsScanEnabled),
            nameof(NewsSourceATitle),
            nameof(NewsSourceBTitle),
            nameof(NewsSourceAUrl),
            nameof(NewsSourceBUrl),

            nameof(VisualPackCreatorPath),
            nameof(CustomFilterIconsFolder),
            nameof(CustomFilterBackgroundsFolder),
            nameof(CustomSourceIconsFolder),
            nameof(CustomBannerAboveCoverFolder),
            nameof(CustomBannerOnCoverFolder),

            nameof(MediaGalleryProvider),
            nameof(AnikiVideoPlayerVolume),
            nameof(VideoNetworkLocation1Name),
            nameof(VideoNetworkLocation1Path),
            nameof(VideoNetworkLocation2Name),
            nameof(VideoNetworkLocation2Path),
            nameof(VideoNetworkLocation3Name),
            nameof(VideoNetworkLocation3Path),
            nameof(VideoNetworkLocation4Name),
            nameof(VideoNetworkLocation4Path),
            nameof(VideoMoviesLibraryPath),
            nameof(VideoSeriesLibraryPath),
            nameof(VideoAnimeLibraryPath),
            nameof(VideoCustomLibraryPath),
            nameof(VideoCustomLibraryEnabled),
            nameof(VideoCustomLibraryName),
            nameof(VideoCustomLibraryContentType),
            nameof(VideoMoviesLibraryPaths),
            nameof(VideoSeriesLibraryPaths),
            nameof(VideoAnimeLibraryPaths),
            nameof(VideoCustomLibraryPaths),
            nameof(VideoThumbnailFfmpegPath),
            nameof(VideoFfprobePath),
            nameof(VideoOnlineArtworkEnabled),
            nameof(VideoTmdbArtworkEnabled),
            nameof(VideoTmdbArtworkLanguage),
            nameof(VideoTvmazeArtworkEnabled),
            nameof(VideoAnilistArtworkEnabled),
            nameof(VideoAutoPlayNextEnabled),
            nameof(VideoPreferredAudioLanguage),
            nameof(VideoSubtitlePreferenceMode),
            nameof(VideoPreferredSubtitleLanguage),

            nameof(SteamUpdatesScanEnabled),
            nameof(SteamPlayerCountEnabled),
            nameof(SteamStoreEnabled),
            nameof(SteamStoreLanguage),
            nameof(SteamStoreRegion),
            nameof(SteamFriendsEnabled),
            nameof(ShowOffline),
            nameof(NotifyOnGameStart),
            nameof(NotifyOnConnect),
            nameof(AskSteamUpdateCacheAtStartup),

            nameof(StartupIntroVideoEnabled),
            nameof(ShutdownVideoEnabled),

            nameof(ScreenSaverEnabled),
            nameof(ScreenSaverIdleDelayMinutes),
            nameof(ScreenSaverChangeIntervalSeconds),
            nameof(ScreenSaverSource),
            nameof(ScreenSaverUseSplashImages),
            nameof(ScreenSaverAmbientMusicEnabled),
            nameof(ScreenSaverShowLogo),
            nameof(ScreenSaverShowInfoCard),
            nameof(ScreenSaverAnimateBackground),
            nameof(ScreenSaverUseFadeTransitions),

            nameof(GameLaunchSplashEnabled),
            nameof(GameLaunchSplashPauseUniPlaySong),
            nameof(GameLaunchSplashShowLogo),
            nameof(GameLaunchSplashVideoSoundEnabled),
            nameof(GameLaunchSplashVideoEndBehavior),
            nameof(GameLaunchSplashVideoVolume),
            nameof(GameLaunchSplashLogoPosition),
            nameof(GameLaunchSplashSelectionMode),
            nameof(GameLaunchSplashCustomPriority1),
            nameof(GameLaunchSplashCustomPriority2),
            nameof(GameLaunchSplashCustomPriority3),
            nameof(GameLaunchSplashCustomPriority4),
            nameof(GameLaunchSplashCustomPriority5),
            nameof(GameLaunchSplashMinimumDurationMs),
            nameof(GameLaunchSplashAutoDetectReadyEnabled),
            nameof(GameLaunchSplashMaximumWaitMs),

            nameof(WebBrowserHomeUrl),

            nameof(InGameOverlayEnabled),
            nameof(InGameOverlayHotkey),
            nameof(InGameOverlayControllerShortcut),
            nameof(InGameOverlayVirtualKeyboardProvider),
            nameof(InGameOverlayVirtualKeyboardShortcut),
            nameof(InGameOverlayGamepadMouseShortcut),
            nameof(InGameOverlayGameBehavior)
        };

        /// <summary>
        /// Restores user-facing Aniki Helper options to their original defaults without deleting
        /// accounts, favorites, per-game data, histories, caches or theme configuration data.
        /// </summary>
        public void RestoreOptionDefaults()
        {
            var defaults = new AnikiHelperSettings(plugin, false);
            var settingsType = typeof(AnikiHelperSettings);

            foreach (var propertyName in DefaultOptionPropertyNames)
            {
                try
                {
                    var property = settingsType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                    if (property == null || !property.CanRead || !property.CanWrite)
                    {
                        logger?.Warn("[AnikiHelper] Default reset skipped unavailable setting: " + propertyName);
                        continue;
                    }

                    property.SetValue(this, property.GetValue(defaults));
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to restore default value for setting: " + propertyName);
                }
            }

            // Keep the current defaults version so a later startup does not treat the reset Hub
            // shortcuts as an old configuration that still needs migration.
            HubShortcutsDefaultsVersion = CurrentHubShortcutsDefaultsVersion;

            // Recreate the dynamic Video Center library rows after a full settings reset.
            InitializeVideoLibraryPathCollections(null);

            RefreshHubApps();
            EnsureHubCurrentPageInRange();
            RefreshGameLaunchSplashCustomPriorityOptions();
            RefreshGameLaunchSplashDisabledGameItems();
            RefreshInGameOverlayNeverSuspendGameItems();
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
        private bool PrepareSelectedGameLinksWindow()
        {
            var selectedGame = plugin?.PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();
            return PrepareGameLinksWindow(selectedGame);
        }

        internal bool PrepareGameLinksWindow(Game game)
        {
            try
            {
                SelectedGameLinks.Clear();

                if (game == null)
                {
                    SelectedGameLinksGameName = string.Empty;
                    return false;
                }

                SelectedGameLinksGameName = game.Name ?? string.Empty;

                if (game.Links != null)
                {
                    foreach (var link in game.Links)
                    {
                        if (link == null || string.IsNullOrWhiteSpace(link.Url))
                        {
                            continue;
                        }

                        var linkUrl = link.Url.Trim();
                        var linkName = string.IsNullOrWhiteSpace(link.Name)
                            ? linkUrl
                            : link.Name.Trim();

                        var host = linkUrl;
                        if (Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri))
                        {
                            if (!string.IsNullOrWhiteSpace(uri.Host))
                            {
                                host = uri.IsDefaultPort
                                    ? uri.Host
                                    : uri.Authority;
                            }
                            else if (!string.IsNullOrWhiteSpace(uri.Scheme))
                            {
                                host = uri.Scheme + "://";
                            }
                        }

                        var item = new AnikiGameLinkItem
                        {
                            Name = linkName,
                            Url = linkUrl,
                            Host = host
                        };

                        item.OpenCommand = new RelayCommand(
                            () => plugin?.OpenGameLink(linkUrl, linkName)
                        );

                        SelectedGameLinks.Add(item);
                    }
                }

                // Always open the window. The theme displays an empty-state message
                // when the game has no valid links.
                return true;
            }
            catch
            {
                SelectedGameLinks.Clear();
                SelectedGameLinksGameName = string.Empty;
                return false;
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



    public sealed class VisualPackLibraryViewItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string SizeText { get; set; }
        public ImageSource PreviewImage { get; set; }
        public bool IsActive { get; set; }
        public bool CanApply => !IsActive;
        public bool CanDelete => true;
    }

    public sealed class ColorPackLibraryViewItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string SizeText { get; set; }
        public ImageSource PreviewImage { get; set; }
        public bool IsActive { get; set; }
        public bool CanDelete => true;
    }

    public sealed class LoginPackLibraryViewItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string SizeText { get; set; }
        public ImageSource PreviewImage { get; set; }
        public bool IsActive { get; set; }
        public bool CanDelete => true;
    }

    public sealed class SoundPackLibraryViewItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string SizeText { get; set; }
        public ImageSource PreviewImage { get; set; }
        public bool IsActive { get; set; }
        public bool CanDelete => true;
    }

    public sealed class CompletePackLibraryViewItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string SizeText { get; set; }
        public ImageSource PreviewImage { get; set; }
        public string ComponentsText { get; set; }
        public bool IsActive { get; set; }
        public bool CanApply => !IsActive;
        public bool CanDelete => true;
    }

    public class AnikiHelperSettingsViewModel : ObservableObject, ISettings
    {
        public AnikiHelperSettings Settings { get; set; }
        private readonly global::AnikiHelper.AnikiHelper plugin;
        private readonly DispatcherTimer saveDebounceTimer;

        public IPlayniteAPI Api => plugin?.PlayniteApi;

        public ObservableCollection<VisualPackLibraryViewItem> VisualPackLibraryPacks { get; } =
            new ObservableCollection<VisualPackLibraryViewItem>();

        public ObservableCollection<ColorPackLibraryViewItem> ColorPackLibraryPacks { get; } =
            new ObservableCollection<ColorPackLibraryViewItem>();

        public ObservableCollection<LoginPackLibraryViewItem> LoginPackLibraryPacks { get; } =
            new ObservableCollection<LoginPackLibraryViewItem>();

        public ObservableCollection<SoundPackLibraryViewItem> SoundPackLibraryPacks { get; } =
            new ObservableCollection<SoundPackLibraryViewItem>();

        public ObservableCollection<CompletePackLibraryViewItem> CompletePackLibraryPacks { get; } =
            new ObservableCollection<CompletePackLibraryViewItem>();

        private string completePackLibraryCountText = "0 / 20";
        public string CompletePackLibraryCountText
        {
            get => completePackLibraryCountText;
            private set => SetValue(ref completePackLibraryCountText, value ?? string.Empty);
        }

        private bool completePackLibraryCanImport = true;
        public bool CompletePackLibraryCanImport
        {
            get => completePackLibraryCanImport;
            private set => SetValue(ref completePackLibraryCanImport, value);
        }

        private bool completePackLibraryIsEmpty = true;
        public bool CompletePackLibraryIsEmpty
        {
            get => completePackLibraryIsEmpty;
            private set => SetValue(ref completePackLibraryIsEmpty, value);
        }

        private string soundPackLibraryCountText = "0 / 20";
        public string SoundPackLibraryCountText
        {
            get => soundPackLibraryCountText;
            private set => SetValue(ref soundPackLibraryCountText, value ?? string.Empty);
        }

        private bool soundPackLibraryCanImport = true;
        public bool SoundPackLibraryCanImport
        {
            get => soundPackLibraryCanImport;
            private set => SetValue(ref soundPackLibraryCanImport, value);
        }

        private bool soundPackLibraryIsEmpty = true;
        public bool SoundPackLibraryIsEmpty
        {
            get => soundPackLibraryIsEmpty;
            private set => SetValue(ref soundPackLibraryIsEmpty, value);
        }

        private string loginPackLibraryCountText = "0 / 20";
        public string LoginPackLibraryCountText
        {
            get => loginPackLibraryCountText;
            private set => SetValue(ref loginPackLibraryCountText, value ?? string.Empty);
        }

        private bool loginPackLibraryCanImport = true;
        public bool LoginPackLibraryCanImport
        {
            get => loginPackLibraryCanImport;
            private set => SetValue(ref loginPackLibraryCanImport, value);
        }

        private bool loginPackLibraryIsEmpty = true;
        public bool LoginPackLibraryIsEmpty
        {
            get => loginPackLibraryIsEmpty;
            private set => SetValue(ref loginPackLibraryIsEmpty, value);
        }

        private string colorPackLibraryCountText = "0 / 20";
        public string ColorPackLibraryCountText
        {
            get => colorPackLibraryCountText;
            private set => SetValue(ref colorPackLibraryCountText, value ?? string.Empty);
        }

        private bool colorPackLibraryCanImport = true;
        public bool ColorPackLibraryCanImport
        {
            get => colorPackLibraryCanImport;
            private set => SetValue(ref colorPackLibraryCanImport, value);
        }

        private bool colorPackLibraryIsEmpty = true;
        public bool ColorPackLibraryIsEmpty
        {
            get => colorPackLibraryIsEmpty;
            private set => SetValue(ref colorPackLibraryIsEmpty, value);
        }

        private string visualPackLibraryCountText = "0 / 20";
        public string VisualPackLibraryCountText
        {
            get => visualPackLibraryCountText;
            private set => SetValue(ref visualPackLibraryCountText, value ?? string.Empty);
        }

        private bool visualPackLibraryCanImport = true;
        public bool VisualPackLibraryCanImport
        {
            get => visualPackLibraryCanImport;
            private set => SetValue(ref visualPackLibraryCanImport, value);
        }

        private bool visualPackLibraryIsEmpty = true;
        public bool VisualPackLibraryIsEmpty
        {
            get => visualPackLibraryIsEmpty;
            private set => SetValue(ref visualPackLibraryIsEmpty, value);
        }

        public string DownloadedLoginVideosStatusText
        {
            get
            {
                var empty = Loc(
                    "DownloadedLoginVideos_StatusEmpty",
                    "No downloaded login backgrounds.");
                var format = Loc(
                    "DownloadedLoginVideos_StatusFormat",
                    "{0} downloaded background(s) · {1}");

                var count = plugin?.GetDownloadedLoginBackgroundVideosCount() ?? 0;
                var sizeBytes = plugin?.GetDownloadedLoginBackgroundVideosSizeBytes() ?? 0L;
                if (count <= 0 || sizeBytes <= 0)
                {
                    return empty;
                }

                return string.Format(format, count, global::AnikiHelper.Services.AnikiThemeSettings.LoginBackgroundMediaService.FormatBytes(sizeBytes));
            }
        }

        public bool HasDownloadedLoginVideos
        {
            get
            {
                var count = plugin?.GetDownloadedLoginBackgroundVideosCount() ?? 0;
                var sizeBytes = plugin?.GetDownloadedLoginBackgroundVideosSizeBytes() ?? 0L;
                return count > 0 && sizeBytes > 0;
            }
        }

        public void RefreshLoginBackgroundMediaState()
        {
            OnPropertyChanged(nameof(DownloadedLoginVideosStatusText));
            OnPropertyChanged(nameof(HasDownloadedLoginVideos));
        }

        public void ClearDownloadedLoginBackgroundVideos()
        {
            plugin?.ClearDownloadedLoginBackgroundVideos();
            RefreshLoginBackgroundMediaState();
        }

        public string GetLoginBackgroundMediaLibraryFolder()
        {
            return plugin?.GetLoginBackgroundMediaLibraryFolder() ?? string.Empty;
        }

        public bool VisualPackCreatorAvailable
        {
            get
            {
                var path = Settings?.VisualPackCreatorPath;
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
        }

        public string VisualPackCreatorPathDisplay
        {
            get
            {
                var path = Settings?.VisualPackCreatorPath;
                return string.IsNullOrWhiteSpace(path)
                    ? Loc("VisualPackCreator_NotConfigured", "Not configured")
                    : path;
            }
        }

        public void RefreshVisualPackCreatorState()
        {
            OnPropertyChanged(nameof(VisualPackCreatorAvailable));
            OnPropertyChanged(nameof(VisualPackCreatorPathDisplay));
        }

        public void RefreshCustomVisualPackLibrary()
        {
            try
            {
                var snapshot = plugin?.GetCustomVisualPackLibrary() ?? new VisualPackLibrarySnapshot
                {
                    MaximumPacks = VisualPackImportService.MaximumLibraryPacks
                };

                VisualPackLibraryPacks.Clear();

                foreach (var pack in snapshot.Packs ?? new List<VisualPackLibraryPack>())
                {
                    VisualPackLibraryPacks.Add(new VisualPackLibraryViewItem
                    {
                        Id = pack.Id ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(pack.Name) ? "Visual Pack" : pack.Name,
                        Author = pack.Author ?? string.Empty,
                        SizeText = FormatVisualPackFileSize(pack.SizeBytes),
                        PreviewImage = LoadInstalledPackPreview("visual", pack.Id, pack.PreviewPath),
                        IsActive = pack.IsActive
                    });
                }

                var maximum = snapshot.MaximumPacks > 0
                    ? snapshot.MaximumPacks
                    : VisualPackImportService.MaximumLibraryPacks;

                VisualPackLibraryCountText = string.Format(
                    Loc("VisualPackLibrary_CountFormat", "{0} / {1} community packs"),
                    VisualPackLibraryPacks.Count,
                    maximum);

                VisualPackLibraryCanImport = VisualPackLibraryPacks.Count < maximum;
                VisualPackLibraryIsEmpty = VisualPackLibraryPacks.Count == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] RefreshCustomVisualPackLibrary failed: " + ex.Message);
                VisualPackLibraryPacks.Clear();
                VisualPackLibraryCountText = "0 / " + VisualPackImportService.MaximumLibraryPacks;
                VisualPackLibraryCanImport = true;
                VisualPackLibraryIsEmpty = true;
            }
        }

        public void RefreshCustomColorPackLibrary()
        {
            try
            {
                var snapshot = plugin?.GetCustomColorPackLibrary() ?? new ColorPackLibrarySnapshot
                {
                    MaximumPacks = ColorPackImportService.MaximumLibraryPacks
                };

                ColorPackLibraryPacks.Clear();
                foreach (var pack in snapshot.Packs ?? new List<ColorPackLibraryPack>())
                {
                    ColorPackLibraryPacks.Add(new ColorPackLibraryViewItem
                    {
                        Id = pack.LocalId ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(pack.Name) ? "Color Pack" : pack.Name,
                        Author = pack.Author ?? string.Empty,
                        Version = pack.Version ?? string.Empty,
                        Description = pack.Description ?? string.Empty,
                        SizeText = FormatVisualPackFileSize(pack.SizeBytes),
                        PreviewImage = LoadInstalledPackPreview("color", pack.LocalId, null),
                        IsActive = pack.IsActive
                    });
                }

                var maximum = snapshot.MaximumPacks > 0
                    ? snapshot.MaximumPacks
                    : ColorPackImportService.MaximumLibraryPacks;
                ColorPackLibraryCountText = string.Format(
                    Loc("ColorPackLibrary_CountFormat", "{0} / {1} Color Packs"),
                    ColorPackLibraryPacks.Count,
                    maximum);
                ColorPackLibraryCanImport = ColorPackLibraryPacks.Count < maximum;
                ColorPackLibraryIsEmpty = ColorPackLibraryPacks.Count == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] RefreshCustomColorPackLibrary failed: " + ex.Message);
                ColorPackLibraryPacks.Clear();
                ColorPackLibraryCountText = "0 / " + ColorPackImportService.MaximumLibraryPacks;
                ColorPackLibraryCanImport = true;
                ColorPackLibraryIsEmpty = true;
            }
        }

        public void RefreshLoginPackLibrary()
        {
            try
            {
                var snapshot = plugin?.GetLoginPackLibrary() ?? new LoginPackLibrarySnapshot
                {
                    MaximumPacks = LoginPackImportService.MaximumLibraryPacks
                };

                LoginPackLibraryPacks.Clear();
                foreach (var pack in snapshot.Packs ?? new List<LoginPackLibraryPack>())
                {
                    LoginPackLibraryPacks.Add(new LoginPackLibraryViewItem
                    {
                        Id = pack.LocalId ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(pack.Name) ? "Login Pack" : pack.Name,
                        Author = pack.Author ?? string.Empty,
                        Version = pack.Version ?? string.Empty,
                        Description = pack.Description ?? string.Empty,
                        SizeText = FormatVisualPackFileSize(pack.SizeBytes),
                        PreviewImage = LoadInstalledPackPreview("login", pack.LocalId, null),
                        IsActive = pack.IsActive
                    });
                }

                var maximum = snapshot.MaximumPacks > 0
                    ? snapshot.MaximumPacks
                    : LoginPackImportService.MaximumLibraryPacks;
                LoginPackLibraryCountText = string.Format(
                    Loc("LoginPackLibrary_CountFormat", "{0} / {1} Login Packs"),
                    LoginPackLibraryPacks.Count,
                    maximum);
                LoginPackLibraryCanImport = LoginPackLibraryPacks.Count < maximum;
                LoginPackLibraryIsEmpty = LoginPackLibraryPacks.Count == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] RefreshLoginPackLibrary failed: " + ex.Message);
                LoginPackLibraryPacks.Clear();
                LoginPackLibraryCountText = "0 / " + LoginPackImportService.MaximumLibraryPacks;
                LoginPackLibraryCanImport = true;
                LoginPackLibraryIsEmpty = true;
            }
        }

        public void RefreshSoundPackLibrary()
        {
            try
            {
                var snapshot = plugin?.GetSoundPackLibrary() ?? new SoundPackLibrarySnapshot
                {
                    MaximumPacks = SoundPackImportService.MaximumLibraryPacks
                };

                SoundPackLibraryPacks.Clear();
                foreach (var pack in snapshot.Packs ?? new List<SoundPackLibraryPack>())
                {
                    SoundPackLibraryPacks.Add(new SoundPackLibraryViewItem
                    {
                        Id = pack.LocalId ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(pack.Name) ? "Sound Pack" : pack.Name,
                        Author = pack.Author ?? string.Empty,
                        Version = pack.Version ?? string.Empty,
                        Description = pack.Description ?? string.Empty,
                        SizeText = FormatVisualPackFileSize(pack.SizeBytes),
                        PreviewImage = LoadInstalledPackPreview("sound", pack.LocalId, null),
                        IsActive = pack.IsActive
                    });
                }

                var maximum = snapshot.MaximumPacks > 0
                    ? snapshot.MaximumPacks
                    : SoundPackImportService.MaximumLibraryPacks;
                SoundPackLibraryCountText = string.Format(
                    Loc("SoundPackLibrary_CountFormat", "{0} / {1} Sound Packs"),
                    SoundPackLibraryPacks.Count,
                    maximum);
                SoundPackLibraryCanImport = SoundPackLibraryPacks.Count < maximum;
                SoundPackLibraryIsEmpty = SoundPackLibraryPacks.Count == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] RefreshSoundPackLibrary failed: " + ex.Message);
                SoundPackLibraryPacks.Clear();
                SoundPackLibraryCountText = "0 / " + SoundPackImportService.MaximumLibraryPacks;
                SoundPackLibraryCanImport = true;
                SoundPackLibraryIsEmpty = true;
            }
        }

        public void RefreshCompletePackLibrary()
        {
            try
            {
                var snapshot = plugin?.GetCompletePackLibrary() ?? new CompletePackLibrarySnapshot
                {
                    MaximumPacks = CompletePackImportService.MaximumLibraryPacks
                };

                CompletePackLibraryPacks.Clear();
                foreach (var pack in snapshot.Packs ?? new List<CompletePackLibraryPack>())
                {
                    var componentNames = new List<string>();
                    if (pack.HasVisualPack) componentNames.Add(Loc("CompletePack_ComponentVisual", "Visual"));
                    if (pack.HasColorPack) componentNames.Add(Loc("CompletePack_ComponentColor", "Color"));
                    if (pack.HasLoginPack) componentNames.Add(Loc("CompletePack_ComponentLogin", "Login"));
                    if (pack.HasSoundPack) componentNames.Add(Loc("CompletePack_ComponentSound", "Sound"));

                    CompletePackLibraryPacks.Add(new CompletePackLibraryViewItem
                    {
                        Id = pack.LocalId ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(pack.Name) ? "Complete Pack" : pack.Name,
                        Author = pack.Author ?? string.Empty,
                        Version = pack.Version ?? string.Empty,
                        Description = pack.Description ?? string.Empty,
                        SizeText = FormatVisualPackFileSize(pack.SizeBytes),
                        PreviewImage = LoadInstalledPackPreview("complete", pack.LocalId, null),
                        ComponentsText = string.Join(" • ", componentNames),
                        IsActive = pack.IsActive
                    });
                }

                var maximum = snapshot.MaximumPacks > 0
                    ? snapshot.MaximumPacks
                    : CompletePackImportService.MaximumLibraryPacks;
                CompletePackLibraryCountText = string.Format(
                    Loc("CompletePackLibrary_CountFormat", "{0} / {1} Complete Packs"),
                    CompletePackLibraryPacks.Count,
                    maximum);
                CompletePackLibraryCanImport = CompletePackLibraryPacks.Count < maximum;
                CompletePackLibraryIsEmpty = CompletePackLibraryPacks.Count == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] RefreshCompletePackLibrary failed: " + ex.Message);
                CompletePackLibraryPacks.Clear();
                CompletePackLibraryCountText = "0 / " + CompletePackImportService.MaximumLibraryPacks;
                CompletePackLibraryCanImport = true;
                CompletePackLibraryIsEmpty = true;
            }
        }

        private ImageSource LoadInstalledPackPreview(string packType, string localPackId, string preferredPath)
        {
            ImageSource image = null;

            try
            {
                // A preview explicitly supplied by the pack creator always wins over
                // generated/local fallbacks such as a Visual Pack's MainBackground.jpg.
                var cachedCommunityPreview = CommunityPackPreviewHelper.TryGetCachedInstalledCommunityPreviewPath(
                    plugin?.GetPluginUserDataPath(),
                    packType,
                    localPackId);
                image = CommunityPackPreviewHelper.LoadImage(cachedCommunityPreview, 420);
                if (image != null)
                {
                    return image;
                }

                var inheritedPreview = CommunityPackPreviewHelper.TryGetInheritedPreviewPath(
                    plugin?.GetPluginUserDataPath(),
                    packType,
                    localPackId);
                image = CommunityPackPreviewHelper.LoadImage(inheritedPreview, 420);
                if (image != null)
                {
                    return image;
                }
            }
            catch
            {
            }

            image = CommunityPackPreviewHelper.LoadImage(preferredPath, 420);
            if (image != null)
            {
                return image;
            }

            return CommunityPackPreviewHelper.LoadFallback(420);
        }

        private static string FormatVisualPackFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
            }

            return Math.Max(0L, bytes / 1024L).ToString("0") + " KB";
        }

        public void OpenVideoCenterLibraryManager()
        {
            plugin?.OpenVideoCenterLibraryManager();
        }

        public void OpenVideoCenterIntroEndingManager()
        {
            plugin?.OpenVideoCenterIntroEndingManager();
        }

        [DontSerialize]
        public string HomePluginIconPath
        {
            get
            {
                try
                {
                    var assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    var iconPath = Path.Combine(assemblyFolder ?? string.Empty, "icon.png");
                    return File.Exists(iconPath) ? iconPath : null;
                }
                catch
                {
                    return null;
                }
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

        private string homeActiveFullscreenThemeName = string.Empty;
        public string HomeActiveFullscreenThemeName
        {
            get => homeActiveFullscreenThemeName;
            private set => SetValue(ref homeActiveFullscreenThemeName, value ?? string.Empty);
        }

        private bool homeCompatibleThemeDetected;
        public bool HomeCompatibleThemeDetected
        {
            get => homeCompatibleThemeDetected;
            private set => SetValue(ref homeCompatibleThemeDetected, value);
        }

        private bool homePlayniteAchievementsInstalled;
        public bool HomePlayniteAchievementsInstalled
        {
            get => homePlayniteAchievementsInstalled;
            private set => SetValue(ref homePlayniteAchievementsInstalled, value);
        }

        private bool homeScreenshotsVisualizerInstalled;
        public bool HomeScreenshotsVisualizerInstalled
        {
            get => homeScreenshotsVisualizerInstalled;
            private set
            {
                if (homeScreenshotsVisualizerInstalled == value)
                {
                    return;
                }

                SetValue(ref homeScreenshotsVisualizerInstalled, value);
                NotifyHomeScreenshotProviderStateChanged();
            }
        }

        private bool homeScreenshotUtilitiesInstalled;
        public bool HomeScreenshotUtilitiesInstalled
        {
            get => homeScreenshotUtilitiesInstalled;
            private set
            {
                if (homeScreenshotUtilitiesInstalled == value)
                {
                    return;
                }

                SetValue(ref homeScreenshotUtilitiesInstalled, value);
                NotifyHomeScreenshotProviderStateChanged();
            }
        }

        private bool homeScreenshotUtilitiesLocalProviderInstalled;
        public bool HomeScreenshotUtilitiesLocalProviderInstalled
        {
            get => homeScreenshotUtilitiesLocalProviderInstalled;
            private set
            {
                if (homeScreenshotUtilitiesLocalProviderInstalled == value)
                {
                    return;
                }

                SetValue(ref homeScreenshotUtilitiesLocalProviderInstalled, value);
                NotifyHomeScreenshotProviderStateChanged();
            }
        }

        private bool homeUniPlaySongInstalled;
        public bool HomeUniPlaySongInstalled
        {
            get => homeUniPlaySongInstalled;
            private set => SetValue(ref homeUniPlaySongInstalled, value);
        }

        public bool HomeSelectedScreenshotProviderInstalled
        {
            get
            {
                if (Settings == null)
                {
                    return false;
                }

                return Settings.MediaGalleryProvider == AnikiMediaProviderMode.ScreenshotUtilitiesLocal
                    ? HomeScreenshotUtilitiesInstalled && HomeScreenshotUtilitiesLocalProviderInstalled
                    : HomeScreenshotsVisualizerInstalled;
            }
        }

        public bool HomeAnyScreenshotProviderInstalled =>
            HomeScreenshotsVisualizerInstalled || HomeScreenshotUtilitiesInstalled;

        public bool HomeNoScreenshotProviderInstalled =>
            !HomeScreenshotsVisualizerInstalled && !HomeScreenshotUtilitiesInstalled;

        public bool HomeScreenshotUtilitiesNeedsLocalProvider =>
            HomeScreenshotUtilitiesInstalled && !HomeScreenshotUtilitiesLocalProviderInstalled;

        public string HomeScreenshotProviderDisplayName
        {
            get
            {
                if (HomeScreenshotsVisualizerInstalled && HomeScreenshotUtilitiesInstalled)
                {
                    return Settings?.MediaGalleryProviderName ?? string.Empty;
                }

                if (HomeScreenshotsVisualizerInstalled)
                {
                    return "Screenshots Visualizer";
                }

                if (HomeScreenshotUtilitiesInstalled)
                {
                    return "Screenshot Utilities";
                }

                return string.Empty;
            }
        }

        private void NotifyHomeScreenshotProviderStateChanged()
        {
            OnPropertyChanged(nameof(HomeSelectedScreenshotProviderInstalled));
            OnPropertyChanged(nameof(HomeAnyScreenshotProviderInstalled));
            OnPropertyChanged(nameof(HomeNoScreenshotProviderInstalled));
            OnPropertyChanged(nameof(HomeScreenshotUtilitiesNeedsLocalProvider));
            OnPropertyChanged(nameof(HomeScreenshotProviderDisplayName));
        }

        public string HomeSteamAccountDisplayName
        {
            get
            {
                var name = Settings?.SelfName?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.All(char.IsDigit))
                {
                    return string.Empty;
                }

                return name;
            }
        }

        public bool HomeSteamAccountNameAvailable =>
            !string.IsNullOrWhiteSpace(HomeSteamAccountDisplayName);

        public bool HomeGamepadMouseEnabled
        {
            get => Settings != null &&
                   !string.Equals(
                       Settings.InGameOverlayGamepadMouseShortcut,
                       "Disabled",
                       StringComparison.OrdinalIgnoreCase);
        }

        private const string PlayniteAchievementsAddonId = "PlayniteAchievements";
        private const string UniPlaySongAddonId = "UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        private const string ScreenshotsVisualizerAddonId = "playnite-screenshotsvisualizer-plugin";
        private const string ScreenshotUtilitiesAddonId = "ScreenshotUtilities_485d682f-73e9-4d54-b16f-b8dd49e88f90";
        private const string ScreenshotUtilitiesLocalProviderAddonId = "ScreenshotUtilitiesLocalProvider_a049eff8-fd41-4dbc-9e35-01acc6b1a0cb";

        public void InstallPlayniteAchievements()
        {
            OpenPlayniteAddonInstaller(PlayniteAchievementsAddonId);
        }

        public void InstallUniPlaySong()
        {
            OpenPlayniteAddonInstaller(UniPlaySongAddonId);
        }

        public void InstallScreenshotUtilitiesLocalProvider()
        {
            if (!HomeScreenshotUtilitiesInstalled)
            {
                OpenPlayniteAddonInstaller(ScreenshotUtilitiesAddonId);
                return;
            }

            OpenPlayniteAddonInstaller(ScreenshotUtilitiesLocalProviderAddonId);
        }

        public void ChooseAndInstallScreenshotProvider()
        {
            if (Api?.Dialogs == null)
            {
                return;
            }

            var visualizerOption = new MessageBoxOption(
                Loc("SettingsHome_ScreenshotChoice_Visualizer", "Screenshots Visualizer"));
            var utilitiesOption = new MessageBoxOption(
                Loc("SettingsHome_ScreenshotChoice_Utilities", "Screenshot Utilities"));
            var cancelOption = new MessageBoxOption(
                Loc("SettingsHome_ScreenshotChoice_Cancel", "Cancel"));

            var options = new List<MessageBoxOption>
            {
                visualizerOption,
                utilitiesOption,
                cancelOption
            };

            var result = Api.Dialogs.ShowMessage(
                Loc(
                    "SettingsHome_ScreenshotChoice_Message",
                    "Choose the screenshot provider you want to install. Only one provider is required."),
                Loc("SettingsHome_ScreenshotChoice_Title", "Install a screenshot provider"),
                MessageBoxImage.Information,
                options);

            if (result == visualizerOption)
            {
                Settings.MediaGalleryProvider = AnikiMediaProviderMode.ScreenshotsVisualizer;
                plugin?.SavePluginSettings(Settings);
                NotifyHomeScreenshotProviderStateChanged();
                OpenPlayniteAddonInstaller(ScreenshotsVisualizerAddonId);
            }
            else if (result == utilitiesOption)
            {
                Settings.MediaGalleryProvider = AnikiMediaProviderMode.ScreenshotUtilitiesLocal;
                plugin?.SavePluginSettings(Settings);
                NotifyHomeScreenshotProviderStateChanged();
                OpenPlayniteAddonInstaller(ScreenshotUtilitiesAddonId);
            }
        }

        private void OpenPlayniteAddonInstaller(string addonId)
        {
            if (string.IsNullOrWhiteSpace(addonId))
            {
                return;
            }

            var uri = "playnite://playnite/installaddon/" + addonId.Trim();

            try
            {
                // GlobalCommands is part of the running Playnite application and is not
                // exposed by the SDK reference used by Aniki Helper. Resolve it at runtime
                // so the plugin keeps building against Playnite.SDK only.
                var globalCommandsType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(assembly => assembly.GetType("Playnite.Commands.GlobalCommands", false))
                    .FirstOrDefault(type => type != null);

                var navigateMethod = globalCommandsType?
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "NavigateUrl", StringComparison.Ordinal))
                        {
                            return false;
                        }

                        var parameters = method.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(string);
                    });

                if (navigateMethod != null)
                {
                    navigateMethod.Invoke(null, new object[] { uri });
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] Playnite NavigateUrl failed: " + ex.Message);
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Api?.Dialogs?.ShowErrorMessage(
                    Loc(
                        "SettingsHome_AddonInstallError",
                        "Playnite could not open the extension installer."),
                    "Aniki Helper");

                Debug.WriteLine("[AnikiHelperSettingsViewModel] Add-on install URI failed: " + ex.Message);
            }
        }

        public void RefreshHomeDashboard()
        {
            try
            {
                RefreshHomeThemeStatus();

                HomePlayniteAchievementsInstalled = IsExtensionInstalled(
                    "PlayniteAchievements",
                    "Playnite Achievements");

                HomeScreenshotsVisualizerInstalled = IsExtensionInstalled(
                    "playnite-screenshotsvisualizer-plugin",
                    "ScreenshotsVisualizer",
                    "Screenshots Visualizer",
                    "c6c8276f-91bf-48e5-a1d1-4bee0b493488");

                // Use the exact extension IDs here. Matching the generic name
                // "Screenshot Utilities" also matched the Local Provider and caused
                // false positives on the Home dashboard.
                HomeScreenshotUtilitiesInstalled = IsExtensionInstalled(
                    "485d682f-73e9-4d54-b16f-b8dd49e88f90");

                HomeScreenshotUtilitiesLocalProviderInstalled = IsExtensionInstalled(
                    "a049eff8-fd41-4dbc-9e35-01acc6b1a0cb");

                HomeUniPlaySongInstalled =
                    (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true) ||
                    IsExtensionInstalled("UniPlaySong");

                OnPropertyChanged(nameof(HomeGamepadMouseEnabled));
                NotifyHomeScreenshotProviderStateChanged();
                OnPropertyChanged(nameof(HomeSteamAccountDisplayName));
                OnPropertyChanged(nameof(HomeSteamAccountNameAvailable));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] RefreshHomeDashboard failed: " + ex.Message);
            }
        }

        private void RefreshHomeThemeStatus()
        {
            var themeId = Api?.ApplicationSettings?.FullscreenTheme;
            var themeDirectory = FindFullscreenThemeDirectory(themeId);

            HomeActiveFullscreenThemeName = ReadThemeDisplayName(themeDirectory, themeId);
            HomeCompatibleThemeDetected = ThemeContainsAnikiHelperMarker(themeDirectory);
        }

        private string FindFullscreenThemeDirectory(string themeId)
        {
            if (string.IsNullOrWhiteSpace(themeId) || Api?.Paths == null)
            {
                return null;
            }

            var roots = new[]
            {
                Api.Paths.ConfigurationPath,
                Api.Paths.ApplicationPath
            };

            foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var directory = Path.Combine(root, "Themes", "Fullscreen", themeId);
                if (Directory.Exists(directory))
                {
                    return directory;
                }
            }

            return null;
        }

        private static string ReadThemeDisplayName(string themeDirectory, string fallbackThemeId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(themeDirectory))
                {
                    var manifestPath = Path.Combine(themeDirectory, "theme.yaml");
                    if (File.Exists(manifestPath))
                    {
                        var manifest = Serialization.FromYamlFile<Dictionary<string, object>>(manifestPath);
                        if (manifest != null)
                        {
                            var nameEntry = manifest.FirstOrDefault(x =>
                                string.Equals(x.Key, "Name", StringComparison.OrdinalIgnoreCase));

                            var name = nameEntry.Value?.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                return name.Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return string.IsNullOrWhiteSpace(fallbackThemeId)
                ? string.Empty
                : fallbackThemeId.Trim();
        }

        private static bool ThemeContainsAnikiHelperMarker(string themeDirectory)
        {
            if (string.IsNullOrWhiteSpace(themeDirectory) || !Directory.Exists(themeDirectory))
            {
                return false;
            }

            try
            {
                // Aniki ReMake exposes its compatibility marker from Constants.xaml.
                // Do not recursively scan/read every XAML file in the theme just to find
                // a resource whose location is known. This check is used by the Settings
                // dashboard and can run during plugin startup, so keeping it to one small
                // file avoids unnecessary synchronous disk I/O before the first theme frame.
                var constantsPath = Path.Combine(themeDirectory, "Constants.xaml");
                if (!File.Exists(constantsPath))
                {
                    return false;
                }

                var content = File.ReadAllText(constantsPath);
                return content.IndexOf("Aniki_ThemeMarker", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private bool IsExtensionInstalled(params string[] markers)
        {
            if (markers == null || markers.Length == 0 || Api?.Paths == null)
            {
                return false;
            }

            var validMarkers = markers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeExtensionIdentity)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (validMarkers.Length == 0)
            {
                return false;
            }

            var extensionRoots = new[]
            {
                Path.Combine(Api.Paths.ConfigurationPath ?? string.Empty, "Extensions"),
                Path.Combine(Api.Paths.ApplicationPath ?? string.Empty, "Extensions")
            };

            foreach (var root in extensionRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    foreach (var manifestPath in Directory.EnumerateFiles(root, "extension.y*ml", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var identities = new List<string>
                            {
                                Path.GetFileName(Path.GetDirectoryName(manifestPath))
                            };

                            var manifest = Serialization.FromYamlFile<Dictionary<string, object>>(manifestPath);
                            if (manifest != null)
                            {
                                foreach (var key in new[] { "Id", "Name", "Module" })
                                {
                                    var entry = manifest.FirstOrDefault(x =>
                                        string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

                                    if (entry.Value != null)
                                    {
                                        identities.Add(entry.Value.ToString());
                                    }
                                }
                            }

                            var normalizedIdentities = identities
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(NormalizeExtensionIdentity)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .ToArray();

                            if (validMarkers.Any(marker => normalizedIdentities.Any(identity =>
                                string.Equals(identity, marker, StringComparison.OrdinalIgnoreCase) ||
                                identity.Contains(marker) ||
                                marker.Contains(identity))))
                            {
                                return true;
                            }
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

            // Extension data folders can remain after uninstalling a plugin, so they are
            // intentionally ignored. Only an installed extension manifest is authoritative.
            return false;
        }

        private static string NormalizeExtensionIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static readonly HashSet<string> AutoSaveSettingNames = new HashSet<string>
        {
            nameof(AnikiHelperSettings.OpenWelcomeHubOnStartup),
            nameof(AnikiHelperSettings.HubVideoCenterPageEnabled),
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

            nameof(AnikiHelperSettings.VisualPackCreatorPath),
            nameof(AnikiHelperSettings.CustomFilterIconsFolder),
            nameof(AnikiHelperSettings.CustomFilterBackgroundsFolder),
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
            nameof(AnikiHelperSettings.SteamFriendsEnabled),

            nameof(AnikiHelperSettings.ScreenSaverEnabled),
            nameof(AnikiHelperSettings.ScreenSaverIdleDelayMinutes),
            nameof(AnikiHelperSettings.ScreenSaverChangeIntervalSeconds),
            nameof(AnikiHelperSettings.ScreenSaverSource),
            nameof(AnikiHelperSettings.ScreenSaverUseSplashImages),
            nameof(AnikiHelperSettings.ScreenSaverAmbientMusicEnabled),
            nameof(AnikiHelperSettings.ScreenSaverShowLogo),
            nameof(AnikiHelperSettings.ScreenSaverShowInfoCard),
            nameof(AnikiHelperSettings.ScreenSaverAnimateBackground),
            nameof(AnikiHelperSettings.ScreenSaverUseFadeTransitions),

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

            nameof(AnikiHelperSettings.WebBrowserHomeUrl),

            nameof(AnikiHelperSettings.InGameOverlayEnabled),
            nameof(AnikiHelperSettings.InGameOverlayHotkey),
            nameof(AnikiHelperSettings.InGameOverlayControllerShortcut),
            nameof(AnikiHelperSettings.InGameOverlayVirtualKeyboardProvider),
            nameof(AnikiHelperSettings.InGameOverlayVirtualKeyboardShortcut),
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
            RefreshHomeDashboard();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                return;
            }

            if (string.Equals(
                e.PropertyName,
                nameof(AnikiHelperSettings.InGameOverlayGamepadMouseShortcut),
                StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(HomeGamepadMouseEnabled));
            }

            if (string.Equals(e.PropertyName, nameof(AnikiHelperSettings.MediaGalleryProvider), StringComparison.Ordinal))
            {
                NotifyHomeScreenshotProviderStateChanged();
            }

            if (string.Equals(e.PropertyName, nameof(AnikiHelperSettings.VisualPackCreatorPath), StringComparison.Ordinal))
            {
                RefreshVisualPackCreatorState();
            }

            if (string.Equals(e.PropertyName, nameof(AnikiHelperSettings.SelfName), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(AnikiHelperSettings.SteamAccountConnected), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(AnikiHelperSettings.SteamAccountSteamId64), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(HomeSteamAccountDisplayName));
                OnPropertyChanged(nameof(HomeSteamAccountNameAvailable));
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

        public void RestoreDefaultPluginOptions()
        {
            saveDebounceTimer?.Stop();
            Settings.RestoreOptionDefaults();
            saveDebounceTimer?.Stop();
            plugin.SavePluginSettings(Settings);
            RefreshHomeDashboard();
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

        public void DeleteAllMonthlyStats()
        {
            try
            {
                plugin?.DeleteAllMonthlyStats();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] DeleteAllMonthlyStats failed: " + ex.Message);
                throw;
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

        public void ExportThemeConfiguration(string exportFilePath)
        {
            try
            {
                plugin?.ExportAnikiThemeConfiguration(exportFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ExportThemeConfiguration failed: " + ex.Message);
                throw;
            }
        }

        public void ImportThemeConfiguration(string importFilePath)
        {
            try
            {
                plugin?.ImportAnikiThemeConfiguration(importFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ImportThemeConfiguration failed: " + ex.Message);
                throw;
            }
        }

        public void OpenCommunityVisualPacksBrowser()
        {
            OpenCommunityPacksBrowser("visual");
        }

        public void OpenCommunityPacksBrowser(string packType)
        {
            try
            {
                plugin?.OpenCommunityPacksBrowser(packType);
                RefreshCustomVisualPackLibrary();
                RefreshCustomColorPackLibrary();
                RefreshLoginPackLibrary();
                RefreshSoundPackLibrary();
                RefreshCompletePackLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] OpenCommunityPacksBrowser failed: " + ex.Message);
                throw;
            }
        }

        public Services.VisualPacks.VisualPackImportResult ImportCustomVisualPack(string zipFilePath)
        {
            try
            {
                var result = plugin?.ImportCustomVisualPack(zipFilePath);
                RefreshCustomVisualPackLibrary();
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ImportCustomVisualPack failed: " + ex.Message);
                throw;
            }
        }

        public void ApplyCustomVisualPack(string packId)
        {
            try
            {
                plugin?.ApplyCustomVisualPack(packId);
                RefreshCustomVisualPackLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ApplyCustomVisualPack failed: " + ex.Message);
                throw;
            }
        }

        public void DeleteCustomVisualPack(string packId)
        {
            try
            {
                plugin?.DeleteCustomVisualPack(packId);
                RefreshCustomVisualPackLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] DeleteCustomVisualPack failed: " + ex.Message);
                throw;
            }
        }

        public void ExportCustomVisualPack(string packId, string destinationZipPath)
        {
            try
            {
                plugin?.ExportCustomVisualPack(packId, destinationZipPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnikiHelperSettingsViewModel] ExportCustomVisualPack failed: " + ex.Message);
                throw;
            }
        }

        public ColorPackImportResult ImportCustomColorPack(string zipFilePath)
        {
            try
            {
                var result = plugin?.ImportCustomColorPack(zipFilePath);
                RefreshCustomColorPackLibrary();
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ImportCustomColorPack failed: " + ex.Message);
                throw;
            }
        }

        public void ApplyCustomColorPack(string localId)
        {
            try
            {
                plugin?.ApplyCustomColorPack(localId);
                RefreshCustomColorPackLibrary();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ApplyCustomColorPack failed: " + ex.Message);
                throw;
            }
        }

        public void DeleteCustomColorPack(string localId)
        {
            try
            {
                plugin?.DeleteCustomColorPack(localId);
                RefreshCustomColorPackLibrary();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] DeleteCustomColorPack failed: " + ex.Message);
                throw;
            }
        }

        public void ExportCustomColorPack(string localId, string destinationZipPath)
        {
            try
            {
                plugin?.ExportCustomColorPack(localId, destinationZipPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ExportCustomColorPack failed: " + ex.Message);
                throw;
            }
        }

        public LoginPackImportResult ImportLoginPack(string zipFilePath)
        {
            try
            {
                var result = plugin?.ImportLoginPack(zipFilePath);
                RefreshLoginPackLibrary();
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ImportLoginPack failed: " + ex.Message);
                throw;
            }
        }

        public void DeleteLoginPack(string localId)
        {
            try
            {
                plugin?.DeleteLoginPack(localId);
                RefreshLoginPackLibrary();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] DeleteLoginPack failed: " + ex.Message);
                throw;
            }
        }

        public void ExportLoginPack(string localId, string destinationZipPath)
        {
            try
            {
                plugin?.ExportLoginPack(localId, destinationZipPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ExportLoginPack failed: " + ex.Message);
                throw;
            }
        }

        public SoundPackImportResult ImportSoundPack(string zipFilePath)
        {
            try
            {
                var result = plugin?.ImportSoundPack(zipFilePath);
                RefreshSoundPackLibrary();
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ImportSoundPack failed: " + ex.Message);
                throw;
            }
        }

        public void DeleteSoundPack(string localId)
        {
            try
            {
                plugin?.DeleteSoundPack(localId);
                RefreshSoundPackLibrary();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] DeleteSoundPack failed: " + ex.Message);
                throw;
            }
        }

        public void ExportSoundPack(string localId, string destinationZipPath)
        {
            try
            {
                plugin?.ExportSoundPack(localId, destinationZipPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ExportSoundPack failed: " + ex.Message);
                throw;
            }
        }

        public CompletePackImportResult ImportCompletePack(string zipFilePath)
        {
            try
            {
                var result = plugin?.ImportCompletePack(zipFilePath);
                RefreshCompletePackLibrary();
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ImportCompletePack failed: " + ex.Message);
                throw;
            }
        }

        public void ApplyCompletePack(string localId)
        {
            try
            {
                plugin?.ApplyCompletePack(localId);
                RefreshCustomVisualPackLibrary();
                RefreshCustomColorPackLibrary();
                RefreshLoginPackLibrary();
                RefreshSoundPackLibrary();
                RefreshCompletePackLibrary();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ApplyCompletePack failed: " + ex.Message);
                throw;
            }
        }

        public CompletePackDeleteAnalysis AnalyzeCompletePackDelete(string localId)
        {
            try
            {
                return plugin?.AnalyzeCompletePackDelete(localId) ?? new CompletePackDeleteAnalysis();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] AnalyzeCompletePackDelete failed: " + ex.Message);
                throw;
            }
        }

        public void DeleteCompletePack(string localId)
        {
            DeleteCompletePack(localId, false);
        }

        public void DeleteCompletePack(string localId, bool deleteIncludedPacks)
        {
            try
            {
                plugin?.DeleteCompletePack(localId, deleteIncludedPacks);
                RefreshCustomVisualPackLibrary();
                RefreshCustomColorPackLibrary();
                RefreshLoginPackLibrary();
                RefreshSoundPackLibrary();
                RefreshCompletePackLibrary();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] DeleteCompletePack failed: " + ex.Message);
                throw;
            }
        }

        public void ExportCompletePack(string localId, string destinationZipPath)
        {
            try
            {
                plugin?.ExportCompletePack(localId, destinationZipPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AnikiHelperSettingsViewModel] ExportCompletePack failed: " + ex.Message);
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
