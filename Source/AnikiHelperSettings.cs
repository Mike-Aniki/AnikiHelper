using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Timers;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using AnikiHelper.Services;

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



    public class AnikiHelperSettings : ObservableObject, ISettings, System.ComponentModel.INotifyPropertyChanged
    {
        private readonly global::AnikiHelper.AnikiHelper plugin;

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
        public AnikiWindowCommandProvider OpenWindow { get; }

        [DontSerialize]
        public AnikiWindowCommandProvider OpenChildWindow { get; }

        private bool isWelcomeHubOpen = true;
        public bool IsWelcomeHubOpen
        {
            get => isWelcomeHubOpen;
            set => SetValue(ref isWelcomeHubOpen, value);
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

        // Info snapshot 
        private string snapshotDateString;
        public string SnapshotDateString { get => snapshotDateString; set => SetValue(ref snapshotDateString, value); }

        // Options stats / display 
        private bool includeHidden = false;
        private int topPlayedMax = 10;
        private bool playtimeStoredInHours = false;
        private bool playtimeUseDaysFormat = false;

        // Dynamic colors / precache 
        private bool dynamicAutoPrecacheUserEnabled = true;

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


        // Watcher SuccessStory
        private FileSystemWatcher achievementsWatcher;
        private Timer debounceTimer;

        // Cache SuccessStory root (évite de rescanner le disque trop souvent)
        [DontSerialize]
        private string cachedSsRoot;

        [DontSerialize]
        private DateTime cachedSsRootCheckedUtc = DateTime.MinValue;


        #region Options (bindables)
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

        public Dictionary<Guid, string> CustomGameLaunchSplashImages { get; set; }
            = new Dictionary<Guid, string>();

        private bool shutdownVideoEnabled = true;
        public bool ShutdownVideoEnabled
        {
            get => shutdownVideoEnabled;
            set => SetValue(ref shutdownVideoEnabled, value);
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

            var saved = LoadSettingsSafe(plugin);
            if (saved != null)
            {
                IncludeHidden = saved.IncludeHidden;
                TopPlayedMax = saved.TopPlayedMax <= 0 ? 10 : saved.TopPlayedMax;
                PlaytimeStoredInHours = saved.PlaytimeStoredInHours;
                PlaytimeUseDaysFormat = saved.PlaytimeUseDaysFormat;

                OpenWelcomeHubOnStartup = saved.OpenWelcomeHubOnStartup;

                SteamStoreLanguage = string.IsNullOrWhiteSpace(saved.SteamStoreLanguage) ? "english" : saved.SteamStoreLanguage;
                SteamStoreRegion = string.IsNullOrWhiteSpace(saved.SteamStoreRegion) ? "US" : saved.SteamStoreRegion;
                SteamStoreEnabled = saved.SteamStoreEnabled;

                LoginRandomIndex = saved.LoginRandomIndex;
                LastLoginRandomIndex = saved.LastLoginRandomIndex;

                SteamPlayerCountEnabled = saved.SteamPlayerCountEnabled;
                AskSteamUpdateCacheAtStartup = saved.AskSteamUpdateCacheAtStartup;
                StartupIntroVideoEnabled = saved.StartupIntroVideoEnabled;
                GameLaunchSplashEnabled = saved.GameLaunchSplashEnabled;
                CustomGameLaunchSplashImages = saved.CustomGameLaunchSplashImages
                    ?? new Dictionary<Guid, string>();
                ShutdownVideoEnabled = saved.ShutdownVideoEnabled;
                LastSteamRecentCheckUtc = saved.LastSteamRecentCheckUtc;
                EventSoundsEnabled = saved.EventSoundsEnabled;

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

            InitializeWelcomeHubCommand = new RelayCommand<object>(
                param =>
                {
                    if (param is bool openAtStartup)
                    {
                        plugin?.InitializeWelcomeHubState(openAtStartup);
                    }
                }
            );

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
        public IPlayniteAPI Api => plugin?.PlayniteApi;

        public AnikiHelperSettingsViewModel(global::AnikiHelper.AnikiHelper plugin)
        {
            this.plugin = plugin;
            Settings = new AnikiHelperSettings(plugin);
        }

        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit()
        {     
            plugin.SavePluginSettings(Settings);
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
