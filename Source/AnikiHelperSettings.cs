using AnikiHelper.Services;
using AnikiHelper.Services.Achievements;
using AnikiHelper.Services.AnikiThemeSettings;
using AnikiHelper.Services.MediaGallery;
using AnikiHelper.Services.SplashScreen;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
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

    public class AnikiHelperSettings : ObservableObject, ISettings, System.ComponentModel.INotifyPropertyChanged
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

        private AchievementMemoriesCacheService achievementMemoriesCacheService;
        private RarestAchievementCacheService rarestAchievementCacheService;
        private PlayniteAchievementsReader playniteAchievementsReader;

        [DontSerialize]
        private ILogger logger;

        [DontSerialize]
        public RelayCommand RefreshSuccessStoryCommand { get; }

        [DontSerialize]
        public RelayCommand<SteamStoreItem> OpenSteamStoreDetailsCommand { get; }
        public RelayCommand<object> OpenGameDetailsCommand { get; }
        public RelayCommand<object> ToggleWelcomeHubCommand { get; }
        public RelayCommand<object> CloseWelcomeHubCommand { get; }
        public RelayCommand<object> InitializeWelcomeHubCommand { get; }

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
        public RelayCommand<AnikiMediaItem> OpenScreenshotsForMediaItemCommand { get; }

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
        public AnikiWindowCommandProvider OpenChildWindow { get; }
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

        [DontSerialize]
        public RelayCommand CloseSteamStoreDetailsCommand { get; }

        [DontSerialize]
        public RelayCommand OpenSteamStorePageExternalCommand { get; }

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

        // Suggested game for the Welcome Hub

        private string suggestedGameName;
        public string SuggestedGameName
        {
            get => suggestedGameName;
            set => SetValue(ref suggestedGameName, value);
        }

        private string suggestedGameCoverPath;
        public string SuggestedGameCoverPath
        {
            get => suggestedGameCoverPath;
            set => SetValue(ref suggestedGameCoverPath, value);
        }

        private string suggestedGameBackgroundPath;
        public string SuggestedGameBackgroundPath
        {
            get => suggestedGameBackgroundPath;
            set => SetValue(ref suggestedGameBackgroundPath, value);
        }

        private string suggestedGameBackgroundPathA;
        public string SuggestedGameBackgroundPathA
        {
            get => suggestedGameBackgroundPathA;
            set => SetValue(ref suggestedGameBackgroundPathA, value);
        }

        private string suggestedGameBackgroundPathB;
        public string SuggestedGameBackgroundPathB
        {
            get => suggestedGameBackgroundPathB;
            set => SetValue(ref suggestedGameBackgroundPathB, value);
        }

        private bool suggestedGameShowLayerB;
        public bool SuggestedGameShowLayerB
        {
            get => suggestedGameShowLayerB;
            set => SetValue(ref suggestedGameShowLayerB, value);
        }

        // Reference games for the suggestion
        private string suggestedGameSourceName;
        public string SuggestedGameSourceName
        {
            get => suggestedGameSourceName;
            set => SetValue(ref suggestedGameSourceName, value);
        }

        // Reason for game suggestion

        private string suggestedGameReasonKey;
        public string SuggestedGameReasonKey
        {
            get => suggestedGameReasonKey;
            set => SetValue(ref suggestedGameReasonKey, value);
        }

        private string suggestedGameBannerText;
        public string SuggestedGameBannerText
        {
            get => suggestedGameBannerText;
            set => SetValue(ref suggestedGameBannerText, value);
        }

        

        // Rotation info for suggested game (top 3 / once per day)
        public Guid RefGameLastId { get; set; } = Guid.Empty;
        public DateTime RefGameLastChangeDate { get; set; } = DateTime.MinValue;


        private Guid suggestedGameLastId = Guid.Empty;
        public Guid SuggestedGameLastId
        {
            get => suggestedGameLastId;
            set => SetValue(ref suggestedGameLastId, value);
        }

        private DateTime suggestedGameLastChangeDate = DateTime.MinValue;
        public DateTime SuggestedGameLastChangeDate
        {
            get => suggestedGameLastChangeDate;
            set => SetValue(ref suggestedGameLastChangeDate, value);
        }


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
            set => SetValue(ref steamStoreEnabled, value);
        }

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

        private ObservableCollection<SteamStoreItem> steamStoreDeals = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreNewReleases = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreTopSellers = new ObservableCollection<SteamStoreItem>();
        private ObservableCollection<SteamStoreItem> steamStoreSpotlight = new ObservableCollection<SteamStoreItem>();

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
        public ObservableCollection<SteamStoreItem> SteamStoreSpotlight
        {
            get
            {
                RequestSteamStoreLoad();
                return steamStoreSpotlight;
            }
            set => SetValue(ref steamStoreSpotlight, value);
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

        private int gameLaunchSplashMaximumWaitMs = 6000;
        public int GameLaunchSplashMaximumWaitMs
        {
            get => gameLaunchSplashMaximumWaitMs;
            set => SetValue(ref gameLaunchSplashMaximumWaitMs, value);
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
            set => SetValue(ref loginRandomIndex, value);
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

                SteamStoreLanguage = string.IsNullOrWhiteSpace(saved.SteamStoreLanguage) ? "english" : saved.SteamStoreLanguage;
                SteamStoreRegion = string.IsNullOrWhiteSpace(saved.SteamStoreRegion) ? "US" : saved.SteamStoreRegion;
                SteamStoreEnabled = saved.SteamStoreEnabled;

                CustomFilterIconsFolder = saved.CustomFilterIconsFolder ?? string.Empty;
                CustomSourceIconsFolder = saved.CustomSourceIconsFolder ?? string.Empty;

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
                GameLaunchSplashMinimumDurationMs = saved.GameLaunchSplashMinimumDurationMs;
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

                    foreach (var it in saved.LastNotifications.Take(20))
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
                SuggestedGameLastId = saved.SuggestedGameLastId;
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

            if (CustomGameLaunchSplashImages == null)
            {
                CustomGameLaunchSplashImages = new Dictionary<Guid, string>();
            }

            if (CustomGameLaunchSplashMinimumDurations == null)
            {
                CustomGameLaunchSplashMinimumDurations = new Dictionary<Guid, int>();
            }

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

            RefreshCurrentGameMediaCommand = new RelayCommand(
                () =>
                {
                    RefreshCurrentGameMediaFromSelectedGame();
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

            OpenHelpLink = new AnikiWindowCommandProvider(
                linkKey => new RelayCommand(() => plugin?.OpenHelpLink(linkKey))
            );

            OpenSteamGameNewsWindowCommand = new RelayCommand(() => plugin?.OpenSteamGameNewsWindow());

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
                .Select(group => group
                    .OrderByDescending(x => x.IsVideo && HasValidProviderThumbnail(x))
                    .ThenByDescending(x => x.CaptureDate)
                    .First())
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
            MediaThumbnailPrecacheStatus = "Scanning media files...";

            return Task.Run(() =>
            {
                try
                {
                    plugin.PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                    {
                        progress.IsIndeterminate = true;
                        progress.Text = "Scanning media files...";

                        var loadedItems = LoadUnifiedMediaItems();
                        var providerThumbCount = loadedItems.Count(x => HasValidProviderThumbnail(x));
                        var imageItems = loadedItems
                            .Where(x => x != null)
                            .Where(x => !x.IsVideo)
                            .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                            .Where(x => File.Exists(x.FilePath))
                            .ToList();

                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            MediaThumbnailPrecacheTotal = imageItems.Count;
                            MediaThumbnailPrecacheDone = 0;
                            MediaThumbnailPrecacheStatus = "Generating thumbnails...";
                        }), DispatcherPriority.Background);

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
                                progress.Text = "Thumbnail generation cancelled.";
                                break;
                            }

                            var item = imageItems[i];
                            var done = i + 1;

                            progress.CurrentProgressValue = done;
                            progress.Text = $"Generating thumbnails... {done}/{imageItems.Count}";

                            try
                            {
                                mediaThumbnailService.GetOrCreateThumbnail(item);
                            }
                            catch (Exception ex)
                            {
                                logger?.Debug(ex, "[AnikiHelper] Failed to generate media thumbnail.");
                            }

                            if (done % 5 == 0 || done == imageItems.Count)
                            {
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    MediaThumbnailPrecacheDone = done;
                                    MediaThumbnailPrecacheStatus = "Generating thumbnails...";
                                }), DispatcherPriority.Background);
                            }
                        }

                        if (!progress.CancelToken.IsCancellationRequested)
                        {
                            progress.Text = "Updating media cache...";

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

                            progress.Text = "Thumbnails generated.";

                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                MediaThumbnailPrecacheDone = MediaThumbnailPrecacheTotal;
                                MediaThumbnailPrecacheStatus = "Thumbnails generated.";
                            }), DispatcherPriority.Background);
                        }
                    },
                    new GlobalProgressOptions("Generating media thumbnails")
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
                        MediaThumbnailPrecacheStatus = "Failed to generate thumbnails.";
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

                    var items = LoadUnifiedMediaItemsForGame(gameId);

                    if (mediaThumbnailService == null)
                    {
                        mediaThumbnailService = new AnikiMediaThumbnailService(plugin.GetPluginUserDataPath(), logger);
                    }

                    foreach (var item in items.Where(x => x != null && !x.IsVideo && !HasValidProviderThumbnail(x)))
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                            {
                                mediaThumbnailService.GetOrCreateThumbnail(item);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.Debug(ex, "[AnikiHelper] Failed to generate thumbnail during silent stopped-game refresh.");
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

                    screenshotMediaCacheService.UpdateGameInCaches(gameId, itemsWithThumbnails);

                    if (sessionStart.HasValue && sessionEnd.HasValue)
                    {
                        screenshotMediaCacheService.UpdateMemoryFromSession(gameId, itemsWithThumbnails, sessionStart.Value, sessionEnd.Value);
                    }

                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        LoadHubLatestMediaFromCache();
                        LoadHubMemoryFromCache();
                        LoadMediaGalleryGamesFromCache();
                    }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Silent media refresh after game stopped failed.");
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

                if (!File.Exists(achievementMemoriesCacheService.CachePath))
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
                                    logger?.Info($"[AnikiHelper] Playnite database ready ({gameCount} games). Building achievement memories cache...");
                                    await RefreshAchievementMemoriesAsync();
                                    return;
                                }

                                await Task.Delay(1000);
                            }

                            logger?.Warn("[AnikiHelper] Playnite database was not ready after 30 seconds. Achievement memories cache not created.");
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn(ex, "[AnikiHelper] Failed to create achievement memories cache.");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to ensure achievement memories cache.");
            }
        }

        public Task RefreshAchievementMemoriesAsync()
        {
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
                    achievementMemoriesCacheService.Save(items);

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
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to refresh achievement memories.");
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

                    achievementMemoriesCacheService.Save(existingItems);
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
            nameof(AnikiHelperSettings.EventSoundsEnabled),
            nameof(AnikiHelperSettings.NewsScanEnabled),
            nameof(AnikiHelperSettings.IncludeHidden),

            nameof(AnikiHelperSettings.StartupIntroVideoEnabled),
            nameof(AnikiHelperSettings.ShutdownVideoEnabled),

            nameof(AnikiHelperSettings.CustomFilterIconsFolder),
            nameof(AnikiHelperSettings.CustomSourceIconsFolder),

            nameof(AnikiHelperSettings.MediaGalleryProvider),

            nameof(AnikiHelperSettings.SteamUpdatesScanEnabled),
            nameof(AnikiHelperSettings.SteamPlayerCountEnabled),
            nameof(AnikiHelperSettings.SteamStoreEnabled),
            nameof(AnikiHelperSettings.SteamStoreLanguage),
            nameof(AnikiHelperSettings.SteamStoreRegion),

            nameof(AnikiHelperSettings.GameLaunchSplashEnabled),
            nameof(AnikiHelperSettings.GameLaunchSplashPauseUniPlaySong),
            nameof(AnikiHelperSettings.GameLaunchSplashSelectionMode),
            nameof(AnikiHelperSettings.GameLaunchSplashShowLogo),
            nameof(AnikiHelperSettings.GameLaunchSplashLogoPosition),
            nameof(AnikiHelperSettings.GameLaunchSplashMinimumDurationMs),
            nameof(AnikiHelperSettings.GameLaunchSplashMinimumDurationSeconds),
            nameof(AnikiHelperSettings.GameLaunchSplashMaximumWaitMs),
            nameof(AnikiHelperSettings.GameLaunchSplashVideoEndBehavior),
            nameof(AnikiHelperSettings.GameLaunchSplashVideoSoundEnabled),
            nameof(AnikiHelperSettings.GameLaunchSplashVideoVolume),

            nameof(AnikiHelperSettings.InGameOverlayEnabled),
            nameof(AnikiHelperSettings.InGameOverlayHotkey),
            nameof(AnikiHelperSettings.InGameOverlayControllerShortcut),

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
