using AnikiHelper.Services.AnikiThemeSettings;
using AnikiHelper.Services.MediaGallery;
using AnikiHelperFullscreen.Views;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AnikiHelper.Services.FirstSetup
{
    public sealed class AnikiFirstSetupViewModel : AnikiFirstSetupBindableBase
    {
        public const int WelcomePage = 0;
        public const int ProfilePage = 1;
        public const int StartupPage = 2;
        public const int InterfacePage = 3;
        public const int ColorPage = 4;
        public const int DetailsPage = 5;
        public const int ExperiencePage = 6;
        public const int TrailersPage = 7;
        public const int IntegrationsPage = 8;
        public const int HelperPage = 9;
        public const int CompletePage = 10;

        private static readonly Guid UniPlaySongPluginId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        private static readonly Guid PlayniteAchievementsPluginId = Guid.Parse("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");
        private static readonly Guid ScreenshotsVisualizerPluginId = Guid.Parse("c6c8276f-91bf-48e5-a1d1-4bee0b493488");
        private static readonly Guid ScreenshotUtilitiesPluginId = Guid.Parse("485d682f-73e9-4d54-b16f-b8dd49e88f90");
        private static readonly Guid ScreenshotUtilitiesLocalProviderPluginId = Guid.Parse("a049eff8-fd41-4dbc-9e35-01acc6b1a0cb");
        private static readonly Guid AudioSwitcherPluginId = Guid.Parse("708b6ec4-bf96-4c0d-bd9d-fe0aa04d6bf1");
        private static readonly Guid CustomFadeAnimationPluginId = Guid.Parse("155b27bc-6c8c-47dc-ae41-e74568b2fe9f");
        private static readonly Guid ExtraMetadataLoaderPluginId = Guid.Parse("705fdbca-e1fc-4004-b839-1d040b8b4429");
        private static readonly Guid ExtraMetadataToolsPluginId = Guid.Parse("2e0349ed-6da2-4095-9457-4c9fb544551e");

        private const string PlayniteAchievementsAddonId = "PlayniteAchievements";
        private const string UniPlaySongAddonId = "UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        private const string ScreenshotsVisualizerAddonId = "playnite-screenshotsvisualizer-plugin";
        private const string ScreenshotUtilitiesAddonId = "ScreenshotUtilities_485d682f-73e9-4d54-b16f-b8dd49e88f90";
        private const string ScreenshotUtilitiesLocalProviderAddonId = "ScreenshotUtilitiesLocalProvider_a049eff8-fd41-4dbc-9e35-01acc6b1a0cb";
        private const string AudioSwitcherAddonId = "PlayniteAudioSwitcher_708b6ec4-bf96-4c0d-bd9d-fe0aa04d6bf1";
        private const string CustomFadeAnimationAddonId = "CustomFadeAnim_155b27bc-6c8c-47dc-ae41-e74568b2fe9f";
        private const string ExtraMetadataLoaderAddonId = "ExtraMetadataLoader_705fdbca-e1fc-4004-b839-1d040b8b4429";
        private const string ExtraMetadataToolsAddonId = "Extra_Metadata_tools_2e0349ed-6da2-4095-9457-4c9fb544551e";

        private readonly global::AnikiHelper.AnikiHelper plugin;
        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;
        private readonly AnikiThemeSettingsService themeSettingsService;
        private readonly ILogger logger;

        private bool prepared;
        private bool restoreAllThemeDefaultsRequested;
        private bool isManualLaunch;
        private bool isOfferedLaunch;
        private bool isOfferVisible;
        private bool startupChanged;
        private bool loginScreenChanged;
        private bool interfaceChanged;
        private bool colorChanged;
        private bool detailsChanged;
        private bool experienceChanged;
        private bool trailersChanged;
        private bool avatarChanged;
        private string initialUserName;
        private bool isActive;
        private bool isApplying;
        private bool isClosing;
        private bool isAddonInstallFlowActive;
        private bool addonInstallNoticeShown;
        private readonly List<string> queuedAddonIds = new List<string>();
        private AnikiMediaProviderMode? queuedScreenshotProviderMode;
        private double applyProgress;
        private string applyStatus;
        private int currentPage;
        private AnikiFirstSetupChoice selectedStartup;
        private AnikiFirstSetupChoice selectedLoginScreen;
        private AnikiFirstSetupChoice selectedInterface;
        private AnikiFirstSetupChoice selectedColor;
        private AnikiFirstSetupChoice selectedDetails;
        private AnikiFirstSetupChoice selectedExperience;
        private AnikiFirstSetupChoice selectedTrailers;
        private AnikiFirstSetupChoice selectedAvatar;

        public AnikiFirstSetupViewModel(
            global::AnikiHelper.AnikiHelper plugin,
            IPlayniteAPI playniteApi,
            AnikiHelperSettings settings,
            AnikiThemeSettingsService themeSettingsService,
            ILogger logger)
        {
            this.plugin = plugin;
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.themeSettingsService = themeSettingsService;
            this.logger = logger;

            UserNameInput = new AnikiThemeVariable
            {
                Id = "FirstSetupUserName",
                Type = "String",
                Title = "Player name",
                LocalizedName = "Player name"
            };
            UserNameInput.SetCurrentStringValueSilently("Player");
            UserNameInput.PropertyChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(SummaryUserName));
            };

            EditUserNameCommand = new RelayCommand(() =>
                FullscreenSettingsView.AnikiThemeTextInputCommand.Execute(UserNameInput));
            ContinueCommand = new RelayCommand(MoveNext);
            BackCommand = new RelayCommand(MoveBack);
            UseDefaultsCommand = new RelayCommand(UseDefaults);
            FinishCommand = new RelayCommand(Finish);
            StartOfferedSetupCommand = new RelayCommand(StartOfferedSetup);
            SkipOfferCommand = new RelayCommand(SkipOffer);
            InstallPlayniteAchievementsCommand = new RelayCommand(TogglePlayniteAchievementsInstall);
            InstallUniPlaySongCommand = new RelayCommand(ToggleUniPlaySongInstall);
            InstallAudioSwitcherCommand = new RelayCommand(ToggleAudioSwitcherInstall);
            InstallCustomFadeAnimationCommand = new RelayCommand(ToggleCustomFadeAnimationInstall);
            InstallExtraMetadataCommand = new RelayCommand(ToggleExtraMetadataInstall);
            ChooseAndInstallScreenshotProviderCommand = new RelayCommand(ChooseOrCancelScreenshotProviderInstall);
            InstallScreenshotUtilitiesLocalProviderCommand = new RelayCommand(ToggleScreenshotUtilitiesLocalProviderInstall);
            InstallCurrentPendingAddonCommand = new RelayCommand(InstallCurrentPendingAddon);
            ContinuePendingAddonInstallCommand = new RelayCommand(ContinuePendingAddonInstall);
            FinishPendingAddonInstallCommand = new RelayCommand(FinishPendingAddonInstall);
        }

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> StartupChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> LoginScreenChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> InterfaceChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> ColorChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> DetailsChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> ExperienceChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> TrailerChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public ObservableCollection<AnikiFirstSetupChoice> AvatarChoices { get; }
            = new ObservableCollection<AnikiFirstSetupChoice>();

        [DontSerialize]
        public AnikiThemeVariable UserNameInput { get; }

        [DontSerialize]
        public ICommand EditUserNameCommand { get; }

        [DontSerialize]
        public ICommand ContinueCommand { get; }

        [DontSerialize]
        public ICommand BackCommand { get; }

        [DontSerialize]
        public ICommand UseDefaultsCommand { get; }

        [DontSerialize]
        public ICommand FinishCommand { get; }

        [DontSerialize]
        public ICommand StartOfferedSetupCommand { get; }

        [DontSerialize]
        public ICommand SkipOfferCommand { get; }

        [DontSerialize]
        public ICommand InstallPlayniteAchievementsCommand { get; }

        [DontSerialize]
        public ICommand InstallUniPlaySongCommand { get; }

        [DontSerialize]
        public ICommand InstallAudioSwitcherCommand { get; }

        [DontSerialize]
        public ICommand InstallCustomFadeAnimationCommand { get; }

        [DontSerialize]
        public ICommand InstallExtraMetadataCommand { get; }

        [DontSerialize]
        public ICommand ChooseAndInstallScreenshotProviderCommand { get; }

        [DontSerialize]
        public ICommand InstallScreenshotUtilitiesLocalProviderCommand { get; }

        [DontSerialize]
        public ICommand InstallCurrentPendingAddonCommand { get; }

        [DontSerialize]
        public ICommand ContinuePendingAddonInstallCommand { get; }

        [DontSerialize]
        public ICommand FinishPendingAddonInstallCommand { get; }

        [DontSerialize]
        public bool IsOfferVisible
        {
            get => isOfferVisible;
            private set => SetValue(ref isOfferVisible, value);
        }

        [DontSerialize]
        public bool IsActive
        {
            get => isActive;
            private set => SetValue(ref isActive, value);
        }

        [DontSerialize]
        public bool IsApplying
        {
            get => isApplying;
            private set
            {
                if (SetValue(ref isApplying, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                }
            }
        }

        [DontSerialize]
        public bool IsClosing
        {
            get => isClosing;
            private set
            {
                if (SetValue(ref isClosing, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                }
            }
        }

        [DontSerialize]
        public bool CanInteract => !IsApplying && !IsAddonInstallFlowActive && !IsClosing;

        [DontSerialize]
        public bool IsAddonInstallFlowActive
        {
            get => isAddonInstallFlowActive;
            private set
            {
                if (SetValue(ref isAddonInstallFlowActive, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                }
            }
        }

        [DontSerialize]
        public double ApplyProgress
        {
            get => applyProgress;
            private set
            {
                if (SetValue(ref applyProgress, value))
                {
                    OnPropertyChanged(nameof(ApplyProgressText));
                }
            }
        }

        [DontSerialize]
        public string ApplyProgressText => $"{Math.Round(ApplyProgress):0}%";

        [DontSerialize]
        public string ApplyStatus
        {
            get => applyStatus ?? string.Empty;
            private set => SetValue(ref applyStatus, value);
        }

        [DontSerialize]
        public int CurrentPage
        {
            get => currentPage;
            private set
            {
                var finalValue = Math.Max(WelcomePage, Math.Min(CompletePage, value));
                if (currentPage == finalValue)
                {
                    return;
                }

                SetValue(ref currentPage, finalValue);
                OnPropertyChanged(nameof(CurrentStepNumber));
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(CanGoBack));

                if (finalValue == IntegrationsPage || finalValue == HelperPage)
                {
                    RefreshStatuses();
                }

                if (finalValue > WelcomePage)
                {
                    plugin?.FocusFirstSetupControl(GetFocusTargetName(finalValue));
                }
            }
        }


        private static string GetFocusTargetName(int page)
        {
            switch (page)
            {
                case ProfilePage:
                    return "FirstSetupProfileNameButton";
                case StartupPage:
                    return "FirstSetupStartupChoices";
                case InterfacePage:
                    return "FirstSetupInterfaceChoices";
                case ColorPage:
                    return "FirstSetupColorChoices";
                case DetailsPage:
                    return "FirstSetupDetailsChoices";
                case ExperiencePage:
                    return "FirstSetupExperienceChoices";
                case TrailersPage:
                    return "FirstSetupTrailerChoices";
                case CompletePage:
                    return "FirstSetupFinishButton";
                default:
                    return "FirstSetupContinueButton";
            }
        }

        [DontSerialize]
        public int CurrentStepNumber => CurrentPage + 1;

        [DontSerialize]
        public int TotalStepCount => CompletePage + 1;

        [DontSerialize]
        public double ProgressPercent => (CurrentStepNumber / (double)TotalStepCount) * 100.0;

        [DontSerialize]
        public bool CanGoBack => CurrentPage > WelcomePage;

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedStartup
        {
            get => selectedStartup;
            set
            {
                if (ReferenceEquals(selectedStartup, value))
                {
                    return;
                }

                if (selectedStartup != null)
                {
                    selectedStartup.IsSelected = false;
                }

                SetValue(ref selectedStartup, value);

                if (selectedStartup != null)
                {
                    selectedStartup.IsSelected = true;
                }

                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedLoginScreen
        {
            get => selectedLoginScreen;
            set
            {
                if (ReferenceEquals(selectedLoginScreen, value))
                {
                    return;
                }

                if (selectedLoginScreen != null)
                {
                    selectedLoginScreen.IsSelected = false;
                }

                SetValue(ref selectedLoginScreen, value);

                if (selectedLoginScreen != null)
                {
                    selectedLoginScreen.IsSelected = true;
                }

                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedInterface
        {
            get => selectedInterface;
            set
            {
                if (ReferenceEquals(selectedInterface, value))
                {
                    return;
                }

                if (selectedInterface != null)
                {
                    selectedInterface.IsSelected = false;
                }

                SetValue(ref selectedInterface, value);

                if (selectedInterface != null)
                {
                    selectedInterface.IsSelected = true;
                }

                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedColor
        {
            get => selectedColor;
            set
            {
                if (ReferenceEquals(selectedColor, value))
                {
                    return;
                }

                if (selectedColor != null)
                {
                    selectedColor.IsSelected = false;
                }

                SetValue(ref selectedColor, value);

                if (selectedColor != null)
                {
                    selectedColor.IsSelected = true;
                }

                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedDetails
        {
            get => selectedDetails;
            set
            {
                if (ReferenceEquals(selectedDetails, value))
                {
                    return;
                }

                if (selectedDetails != null)
                {
                    selectedDetails.IsSelected = false;
                }

                SetValue(ref selectedDetails, value);

                if (selectedDetails != null)
                {
                    selectedDetails.IsSelected = true;
                }

                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedExperience
        {
            get => selectedExperience;
            set
            {
                if (ReferenceEquals(selectedExperience, value))
                {
                    return;
                }

                if (selectedExperience != null)
                {
                    selectedExperience.IsSelected = false;
                }

                SetValue(ref selectedExperience, value);

                if (selectedExperience != null)
                {
                    selectedExperience.IsSelected = true;
                }

                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedTrailers
        {
            get => selectedTrailers;
            set
            {
                if (ReferenceEquals(selectedTrailers, value))
                {
                    return;
                }

                if (selectedTrailers != null)
                {
                    selectedTrailers.IsSelected = false;
                }

                SetValue(ref selectedTrailers, value);

                if (selectedTrailers != null)
                {
                    selectedTrailers.IsSelected = true;
                }

                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public AnikiFirstSetupChoice SelectedAvatar
        {
            get => selectedAvatar;
            set
            {
                if (ReferenceEquals(selectedAvatar, value))
                {
                    return;
                }

                if (selectedAvatar != null)
                {
                    selectedAvatar.IsSelected = false;
                }

                SetValue(ref selectedAvatar, value);

                if (selectedAvatar != null)
                {
                    selectedAvatar.IsSelected = true;
                }

                OnPropertyChanged(nameof(SelectedAvatarPreviewPath));
                RaiseSummaryProperties();
            }
        }

        [DontSerialize]
        public string SelectedAvatarPreviewPath => SelectedAvatar?.PreviewPath ?? string.Empty;

        [DontSerialize]
        public string SummaryUserName => NormalizeUserName(UserNameInput?.CurrentStringValue);

        [DontSerialize]
        public string SummaryStartup => SelectedStartup?.Title ?? string.Empty;

        [DontSerialize]
        public string SummaryLoginScreen => SelectedLoginScreen?.Title ?? string.Empty;

        [DontSerialize]
        public string SummaryInterface => SelectedInterface?.Title ?? string.Empty;

        [DontSerialize]
        public string SummaryColor => SelectedColor?.Title ?? string.Empty;

        [DontSerialize]
        public string SummaryDetails => SelectedDetails?.Title ?? string.Empty;

        [DontSerialize]
        public string SummaryExperience => SelectedExperience?.Title ?? string.Empty;

        [DontSerialize]
        public string SummaryTrailers => SelectedTrailers?.Title ?? string.Empty;

        [DontSerialize]
        public bool SteamReady { get; private set; }

        [DontSerialize]
        public bool PlayniteAchievementsInstalled { get; private set; }

        [DontSerialize]
        public bool UniPlaySongInstalled { get; private set; }

        [DontSerialize]
        public bool AudioSwitcherInstalled { get; private set; }

        [DontSerialize]
        public bool CustomFadeAnimationInstalled { get; private set; }

        [DontSerialize]
        public bool ExtraMetadataLoaderInstalled { get; private set; }

        [DontSerialize]
        public bool ExtraMetadataToolsInstalled { get; private set; }

        [DontSerialize]
        public bool ExtraMetadataInstalled =>
            ExtraMetadataLoaderInstalled && ExtraMetadataToolsInstalled;

        [DontSerialize]
        public bool ExtraMetadataNeedsTools =>
            ExtraMetadataLoaderInstalled && !ExtraMetadataToolsInstalled;

        [DontSerialize]
        public bool ScreenshotsVisualizerInstalled { get; private set; }

        [DontSerialize]
        public bool ScreenshotUtilitiesInstalled { get; private set; }

        [DontSerialize]
        public bool ScreenshotUtilitiesLocalProviderInstalled { get; private set; }

        [DontSerialize]
        public bool ScreenshotPluginInstalled =>
            ScreenshotsVisualizerInstalled ||
            (ScreenshotUtilitiesInstalled && ScreenshotUtilitiesLocalProviderInstalled);

        [DontSerialize]
        public bool ScreenshotNoProviderInstalled =>
            !ScreenshotsVisualizerInstalled && !ScreenshotUtilitiesInstalled;

        [DontSerialize]
        public bool ScreenshotUtilitiesNeedsLocalProvider =>
            !ScreenshotsVisualizerInstalled &&
            ScreenshotUtilitiesInstalled &&
            !ScreenshotUtilitiesLocalProviderInstalled;

        [DontSerialize]
        public bool PlayniteAchievementsQueued => IsAddonQueued(PlayniteAchievementsAddonId);

        [DontSerialize]
        public bool UniPlaySongQueued => IsAddonQueued(UniPlaySongAddonId);

        [DontSerialize]
        public bool AudioSwitcherQueued => IsAddonQueued(AudioSwitcherAddonId);

        [DontSerialize]
        public bool CustomFadeAnimationQueued => IsAddonQueued(CustomFadeAnimationAddonId);

        [DontSerialize]
        public bool ExtraMetadataQueued =>
            IsAddonQueued(ExtraMetadataLoaderAddonId) ||
            IsAddonQueued(ExtraMetadataToolsAddonId);

        [DontSerialize]
        public bool ScreenshotProviderQueued =>
            IsAddonQueued(ScreenshotsVisualizerAddonId) ||
            IsAddonQueued(ScreenshotUtilitiesAddonId);

        [DontSerialize]
        public bool ScreenshotUtilitiesLocalProviderQueued =>
            IsAddonQueued(ScreenshotUtilitiesLocalProviderAddonId);

        [DontSerialize]
        public bool HasQueuedAddonInstallations => queuedAddonIds.Count > 0;

        [DontSerialize]
        public int QueuedAddonCount => queuedAddonIds.Count;

        [DontSerialize]
        public string QueuedAddonSummary => HasQueuedAddonInstallations
            ? string.Format(
                Loc(
                    "LOCFirstSetupQueuedAddonsSummary",
                    "{0} extension(s) will be installed after setup."),
                QueuedAddonCount)
            : string.Empty;

        [DontSerialize]
        public string FinishButtonText => HasQueuedAddonInstallations
            ? string.Format(
                Loc(
                    "LOCFirstSetupApplyAndInstallCount",
                    "Apply and install {0} extension(s)"),
                QueuedAddonCount)
            : Loc("LOCFirstSetupApplyRestart", "Apply settings");

        [DontSerialize]
        public bool HasPersistedPendingAddonInstallations =>
            settings?.PendingFirstSetupAddonInstallIds != null &&
            settings.PendingFirstSetupAddonInstallIds.Count > 0;

        [DontSerialize]
        public int PendingAddonInstallCount =>
            settings?.PendingFirstSetupAddonInstallIds?.Count ?? 0;

        [DontSerialize]
        public int PendingAddonInstallCurrentNumber =>
            PendingAddonInstallCount == 0
                ? 0
                : Math.Min(
                    Math.Max(0, settings.PendingFirstSetupAddonInstallIndex) + 1,
                    PendingAddonInstallCount);

        [DontSerialize]
        public bool PendingAddonInstallIsLast =>
            PendingAddonInstallCount > 0 &&
            PendingAddonInstallCurrentNumber >= PendingAddonInstallCount;

        [DontSerialize]
        public bool PendingAddonInstallCurrentLaunched =>
            settings?.PendingFirstSetupAddonInstallCurrentLaunched == true;

        [DontSerialize]
        public string PendingAddonInstallCurrentId => GetCurrentPersistedAddonId();

        [DontSerialize]
        public string PendingAddonInstallCurrentName =>
            GetAddonDisplayName(PendingAddonInstallCurrentId);

        [DontSerialize]
        public string PendingAddonInstallProgressText => PendingAddonInstallCount > 0
            ? string.Format(
                Loc(
                    "LOCFirstSetupAddonInstallProgress",
                    "Extension {0} of {1}"),
                PendingAddonInstallCurrentNumber,
                PendingAddonInstallCount)
            : string.Empty;

        [DontSerialize]
        public string PendingAddonInstallButtonText => PendingAddonInstallCurrentLaunched
            ? Loc("LOCFirstSetupAddonInstallRetry", "Retry installation")
            : Loc("LOCFirstSetupAddonInstallCurrent", "Install this extension");

        [DontSerialize]
        public bool CanContinuePendingAddonInstall =>
            IsAddonInstallFlowActive &&
            PendingAddonInstallCurrentLaunched &&
            !PendingAddonInstallIsLast;

        [DontSerialize]
        public bool CanFinishPendingAddonInstall =>
            IsAddonInstallFlowActive &&
            PendingAddonInstallCurrentLaunched &&
            PendingAddonInstallIsLast;

        [DontSerialize]
        public string WelcomePreviewPath => SetupPreview("Welcome", "Welcome.jpg");

        [DontSerialize]
        public string IntegrationsPreviewPath => SetupPreview("Information", "Integrations.jpg");

        [DontSerialize]
        public string HelperOverlayPreviewPath => SetupPreview("Helper", "Overlay.jpg");

        [DontSerialize]
        public string HelperKeyboardPreviewPath => SetupPreview("Helper", "Keyboard.jpg");

        [DontSerialize]
        public string HelperMousePreviewPath => SetupPreview("Helper", "Mouse.jpg");

        [DontSerialize]
        public string CompletePreviewPath => SetupPreview("Complete", "Complete.jpg");

        public void Prepare(bool manualLaunch = false)
        {
            try
            {
                if (!prepared)
                {
                    BuildChoices();
                    prepared = true;
                }

                isManualLaunch = manualLaunch;
                isOfferedLaunch = false;
                IsOfferVisible = false;
                IsAddonInstallFlowActive = false;
                ResetQueuedAddonSelections();
                ResetManualChangeTracking();

                if (manualLaunch)
                {
                    LoadCurrentSelections();
                }

                initialUserName = NormalizeUserName(UserNameInput?.CurrentStringValue);
                UserNameInput.LocalizedName = Loc("LOCFirstSetupProfileName", "Player name");
                RefreshStatuses();
                IsApplying = false;
                IsClosing = false;
                ApplyProgress = 0;
                ApplyStatus = string.Empty;
                CurrentPage = WelcomePage;
                IsActive = true;
                RaiseSummaryProperties();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Failed to prepare the initial setup.");
            }
        }

        public void PrepareOffer()
        {
            try
            {
                if (!prepared)
                {
                    BuildChoices();
                    prepared = true;
                }

                isManualLaunch = false;
                isOfferedLaunch = false;
                IsAddonInstallFlowActive = false;
                ResetQueuedAddonSelections();
                ResetManualChangeTracking();
                IsApplying = false;
                IsClosing = false;
                ApplyProgress = 0;
                ApplyStatus = string.Empty;
                CurrentPage = WelcomePage;
                IsOfferVisible = true;
                IsActive = true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Failed to prepare the setup offer.");
            }
        }

        private void StartOfferedSetup()
        {
            if (!IsOfferVisible || IsApplying)
            {
                return;
            }

            themeSettingsService.MarkInitialSetupOfferSeen();
            Prepare(manualLaunch: true);
            isOfferedLaunch = true;
            plugin?.FocusFirstSetupControl("FirstSetupStartButton");
        }

        private async void SkipOffer()
        {
            if (!IsOfferVisible || IsApplying)
            {
                return;
            }

            try
            {
                themeSettingsService.MarkInitialSetupOfferSeen();
                IsOfferVisible = false;
                IsActive = false;

                await plugin.CloseInitialSetupOfferAsync(
                    settings?.OpenWelcomeHubOnStartup == true);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Failed to close the setup offer.");
            }
        }

        public bool HandleBackRequest()
        {
            if (!IsActive)
            {
                return false;
            }

            if (IsApplying || IsAddonInstallFlowActive)
            {
                return true;
            }

            if (IsOfferVisible)
            {
                SkipOffer();
                return true;
            }

            if (CurrentPage > WelcomePage)
            {
                MoveBack();
            }
            else if (isOfferedLaunch)
            {
                CancelOfferedSetup();
            }
            else if (isManualLaunch)
            {
                CloseManualSetup();
            }

            // Always consume B/Escape while the initial setup is active.
            return true;
        }

        private async void CancelOfferedSetup()
        {
            try
            {
                IsActive = false;
                await plugin.CloseInitialSetupOfferAsync(
                    settings?.OpenWelcomeHubOnStartup == true);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Failed to cancel the offered setup.");
            }
        }

        private async void CloseManualSetup()
        {
            try
            {
                if (settings != null)
                {
                    settings.IsSecondaryMusicWindowOpen = false;
                }

                IsClosing = true;
                IsActive = false;

                if (plugin != null)
                {
                    await plugin.CompleteInitialSetupWithoutRestartAsync(
                        openWelcomeHub: false,
                        resumeDeferredStartupPrompts: false,
                        resumeWhatsNew: false);
                }

                IsClosing = false;
            }
            catch (Exception ex)
            {
                IsClosing = false;
                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Failed to close the manually opened setup.");

                try
                {
                    plugin?.CloseTopWindow();
                }
                catch
                {
                }
            }
        }

        private void ResetManualChangeTracking()
        {
            restoreAllThemeDefaultsRequested = false;
            startupChanged = false;
            loginScreenChanged = false;
            interfaceChanged = false;
            colorChanged = false;
            detailsChanged = false;
            experienceChanged = false;
            trailersChanged = false;
            avatarChanged = false;
        }

        private void SelectStartup(AnikiFirstSetupChoice choice)
        {
            SelectedStartup = choice;
            startupChanged = true;
        }

        private void SelectLoginScreen(AnikiFirstSetupChoice choice)
        {
            SelectedLoginScreen = choice;
            loginScreenChanged = true;
        }

        private void SelectInterface(AnikiFirstSetupChoice choice)
        {
            SelectedInterface = choice;
            interfaceChanged = true;
        }

        private void SelectColor(AnikiFirstSetupChoice choice)
        {
            SelectedColor = choice;
            colorChanged = true;
        }

        private void SelectDetails(AnikiFirstSetupChoice choice)
        {
            SelectedDetails = choice;
            detailsChanged = true;
        }

        private void SelectExperience(AnikiFirstSetupChoice choice)
        {
            SelectedExperience = choice;
            experienceChanged = true;
        }

        private void SelectTrailers(AnikiFirstSetupChoice choice)
        {
            SelectedTrailers = choice;
            trailersChanged = true;
        }

        private void SelectAvatar(AnikiFirstSetupChoice choice)
        {
            SelectedAvatar = choice;
            avatarChanged = true;
        }

        private void LoadCurrentSelections()
        {
            var currentUserName = GetStoredStringOption("UserName", "Player");
            UserNameInput.SetCurrentStringValueSilently(NormalizeUserName(currentUserName));

            SelectedStartup = Find(
                StartupChoices,
                settings?.OpenWelcomeHubOnStartup == true ? "Hub" : "Library")
                ?? StartupChoices.FirstOrDefault();

            SelectedLoginScreen = Find(
                LoginScreenChoices,
                GetStoredBoolOption("AcceuilOrNot") ? "Enabled" : "Disabled")
                ?? LoginScreenChoices.FirstOrDefault();

            SelectedInterface = Find(
                InterfaceChoices,
                ResolveCurrentInterfaceProfile())
                ?? Find(InterfaceChoices, "Standard")
                ?? InterfaceChoices.FirstOrDefault();

            SelectedColor = Find(
                ColorChoices,
                GetStoredPreset("Interface", "Default"))
                ?? Find(ColorChoices, "Default")
                ?? ColorChoices.FirstOrDefault();

            SelectedDetails = Find(
                DetailsChoices,
                GetStoredPreset("DetailsViewAlt", "Default"))
                ?? Find(DetailsChoices, "Default")
                ?? DetailsChoices.FirstOrDefault();

            SelectedAvatar = Find(
                AvatarChoices,
                GetStoredPreset("Avatar", "AvatarDefault"))
                ?? Find(AvatarChoices, "AvatarDefault")
                ?? AvatarChoices.FirstOrDefault();

            SelectedExperience = Find(
                ExperienceChoices,
                ResolveCurrentExperienceProfile())
                ?? Find(ExperienceChoices, "Balanced")
                ?? ExperienceChoices.FirstOrDefault();

            SelectedTrailers = Find(
                TrailerChoices,
                ResolveCurrentTrailerProfile())
                ?? Find(TrailerChoices, "DetailsOnly")
                ?? TrailerChoices.FirstOrDefault();
        }

        private string GetStoredStringOption(string key, string fallback)
        {
            try
            {
                if (settings?.AnikiThemeSettingsValues != null &&
                    settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue) &&
                    !string.IsNullOrWhiteSpace(storedValue))
                {
                    return storedValue;
                }

                if (settings?.Options != null &&
                    settings.Options.TryGetValue(key, out var currentValue) &&
                    currentValue != null)
                {
                    return currentValue.ToString();
                }
            }
            catch
            {
                // Fall back to the supplied default.
            }

            return fallback;
        }

        private bool GetStoredBoolOption(string key)
        {
            var value = GetStoredStringOption(key, string.Empty);
            return bool.TryParse(value, out var parsed) && parsed;
        }

        private string GetStoredPreset(string groupId, string fallback)
        {
            try
            {
                if (settings?.AnikiThemeSettingsSelectedPresets != null &&
                    settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var preset) &&
                    !string.IsNullOrWhiteSpace(preset))
                {
                    return preset;
                }
            }
            catch
            {
                // Fall back to the supplied default.
            }

            return fallback;
        }

        private double GetStoredDoubleOption(string key, double fallback)
        {
            var value = GetStoredStringOption(key, string.Empty);
            return double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : fallback;
        }

        private string ResolveCurrentInterfaceProfile()
        {
            if (MatchesInterfaceProfile("Clean"))
            {
                return "Clean";
            }

            if (MatchesInterfaceProfile("Enhanced"))
            {
                return "Enhanced";
            }

            if (MatchesInterfaceProfile("Complete"))
            {
                return "Complete";
            }

            if (MatchesInterfaceProfile("Standard"))
            {
                return "Standard";
            }

            // When the current configuration is custom, select the closest profile
            // for the preview without applying it unless the user validates that choice.
            if (GetStoredBoolOption("CompactGameInfoBar"))
            {
                return "Enhanced";
            }

            if (GetStoredBoolOption("DetailedSideInfoPanel") ||
                GetStoredBoolOption("ShowGameListBackground"))
            {
                return "Complete";
            }

            if (GetStoredBoolOption("NoBarMode") ||
                (!GetStoredBoolOption("ShowAchievementsButton") &&
                 !GetStoredBoolOption("ShowFriendsButton") &&
                 !GetStoredBoolOption("ShowMusicPlayerButton")))
            {
                return "Clean";
            }

            return "Standard";
        }

        private bool MatchesInterfaceProfile(string profile)
        {
            var expected = GetInterfaceProfileValues(profile);

            foreach (var item in expected)
            {
                if (item.Value is bool expectedBool)
                {
                    if (GetStoredBoolOption(item.Key) != expectedBool)
                    {
                        return false;
                    }
                }
                else
                {
                    var expectedDouble = Convert.ToDouble(
                        item.Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    var currentDouble = GetStoredDoubleOption(item.Key, double.NaN);

                    if (double.IsNaN(currentDouble) ||
                        Math.Abs(currentDouble - expectedDouble) > 0.001)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private Dictionary<string, object> GetInterfaceProfileValues(string profile)
        {
            var keys = new[]
            {
                "DisableCenterTopButtons",
                "DisableFilterBottomBar",
                "AutoHideTopButtonBar",
                "AutoHideBottomFilterBar",
                "OpacityBar",
                "OpacityBottomBar",
                "OpacityGameFrame",
                "OpacityShadeLeft",
                "OpacityShadeBottom",
                "ShowGameListBackground",
                "ControllerShortcutBar",
                "CompactGameInfoBar",
                "DetailedSideInfoPanel",
                "FeaturesSteamMain",
                "ShowAchievementsButton",
                "ShowFriendsButton",
                "ShowMusicPlayerButton",
                "ShowOverlayButton",
                "NoBarMode"
            };

            if (string.Equals(profile, "Standard", StringComparison.OrdinalIgnoreCase))
            {
                return keys.ToDictionary(
                    key => key,
                    key => themeSettingsService.GetDefaultOptionValue(key));
            }

            var values = new Dictionary<string, object>
            {
                ["DisableCenterTopButtons"] = false,
                ["DisableFilterBottomBar"] = false,
                ["AutoHideTopButtonBar"] = false,
                ["AutoHideBottomFilterBar"] = true,
                ["OpacityBar"] = 0.6,
                ["OpacityBottomBar"] = 0.8,
                ["OpacityGameFrame"] = 0.6,
                ["OpacityShadeLeft"] = 1.0,
                ["OpacityShadeBottom"] = 1.0,
                ["ShowGameListBackground"] = false,
                ["ControllerShortcutBar"] = true,
                ["CompactGameInfoBar"] = false,
                ["DetailedSideInfoPanel"] = false,
                ["FeaturesSteamMain"] = false,
                ["ShowAchievementsButton"] = true,
                ["ShowFriendsButton"] = true,
                ["ShowMusicPlayerButton"] = true,
                ["ShowOverlayButton"] = true,
                ["NoBarMode"] = false
            };

            if (string.Equals(profile, "Clean", StringComparison.OrdinalIgnoreCase))
            {
                values["AutoHideTopButtonBar"] = true;
                values["AutoHideBottomFilterBar"] = true;
                values["OpacityBar"] = 0.0;
                values["OpacityShadeLeft"] = 0.5;
                values["OpacityShadeBottom"] = 0.5;
                values["ControllerShortcutBar"] = false;
                values["ShowAchievementsButton"] = false;
                values["ShowFriendsButton"] = false;
                values["ShowMusicPlayerButton"] = false;
                values["ShowOverlayButton"] = true;
                values["NoBarMode"] = true;
            }
            else if (string.Equals(profile, "Enhanced", StringComparison.OrdinalIgnoreCase))
            {
                values["AutoHideTopButtonBar"] = false;
                values["AutoHideBottomFilterBar"] = true;
                values["OpacityBar"] = 0.0;
                values["OpacityShadeLeft"] = 0.5;
                values["OpacityShadeBottom"] = 0.5;
                values["ControllerShortcutBar"] = false;
                values["CompactGameInfoBar"] = true;
            }
            else if (string.Equals(profile, "Complete", StringComparison.OrdinalIgnoreCase))
            {
                values["AutoHideTopButtonBar"] = false;
                values["AutoHideBottomFilterBar"] = false;
                values["OpacityBar"] = 0.8;
                values["OpacityGameFrame"] = 0.8;
                values["ShowGameListBackground"] = true;
                values["DetailedSideInfoPanel"] = true;
                values["FeaturesSteamMain"] = false;
            }

            return values;
        }

        private string ResolveCurrentExperienceProfile()
        {
            if (MatchesExperienceProfile("Performance"))
            {
                return "Performance";
            }

            if (MatchesExperienceProfile("FullAnimated"))
            {
                return "FullAnimated";
            }

            if (MatchesExperienceProfile("Animated"))
            {
                return "Animated";
            }

            if (MatchesExperienceProfile("Balanced"))
            {
                return "Balanced";
            }

            // When the current configuration is custom, select the closest profile
            // for the preview without applying it unless the user validates that choice.
            if (GetStoredBoolOption("PerformanceMode"))
            {
                return "Performance";
            }

            var commonAnimationsEnabled = new[]
            {
                "CinematicFocusEffectTop",
                "CinematicFocusEffect",
                "LogoAnimation",
                "AnimatedBorderCover",
                "CoverReflection",
                "EnterDetailsZoom",
                "AnimationPanel"
            }.All(GetStoredBoolOption);

            if (commonAnimationsEnabled &&
                GetStoredBoolOption("AnimationBackground"))
            {
                return "FullAnimated";
            }

            if (commonAnimationsEnabled &&
                !GetStoredBoolOption("AnimationBackground"))
            {
                return "Animated";
            }

            return "Balanced";
        }

        private bool MatchesExperienceProfile(string profile)
        {
            var expected = GetExperienceProfileValues(profile);

            foreach (var item in expected)
            {
                if (item.Value is bool expectedBool &&
                    GetStoredBoolOption(item.Key) != expectedBool)
                {
                    return false;
                }
            }

            return true;
        }

        private Dictionary<string, object> GetExperienceProfileValues(string profile)
        {
            var keys = new[]
            {
                "PerformanceMode",
                "CinematicFocusEffectTop",
                "CinematicFocusEffect",
                "LogoAnimation",
                "AnimatedBorderCover",
                "CoverReflection",
                "EnterDetailsZoom",
                "AnimationPanel",
                "AnimationBackground"
            };

            if (string.Equals(profile, "Balanced", StringComparison.OrdinalIgnoreCase))
            {
                return keys.ToDictionary(
                    key => key,
                    key => themeSettingsService.GetDefaultOptionValue(key));
            }

            var values = keys.ToDictionary(
                key => key,
                key => (object)true);

            values["PerformanceMode"] = false;

            if (string.Equals(profile, "Performance", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in keys)
                {
                    values[key] = false;
                }

                values["PerformanceMode"] = false;
            }
            else if (string.Equals(profile, "Animated", StringComparison.OrdinalIgnoreCase))
            {
                values["AnimationBackground"] = false;
            }

            return values;
        }

        private string ResolveCurrentTrailerProfile()
        {
            var main = GetStoredBoolOption("TrailerOnMainView");
            var details = GetStoredBoolOption("TrailerOnDetailsView");
            if (main && details)
            {
                return "MainAndDetails";
            }

            if (details)
            {
                return "DetailsOnly";
            }

            return "Disabled";
        }

        private void BuildChoices()
        {
            StartupChoices.Clear();
            StartupChoices.Add(Choice("Hub", "LOCFirstSetupStartupHub", "LOCFirstSetupStartupHubDesc", SetupPreview("Startup", "Hub.jpg"), SelectStartup));
            StartupChoices.Add(Choice("Library", "LOCFirstSetupStartupLibrary", "LOCFirstSetupStartupLibraryDesc", SetupPreview("Startup", "Library.jpg"), SelectStartup));

            LoginScreenChoices.Clear();
            LoginScreenChoices.Add(Choice("Enabled", "LOCFirstSetupStartupLoginEnabled", "LOCFirstSetupStartupLoginEnabledDesc", SetupPreview("Startup", "Login.jpg"), SelectLoginScreen));
            LoginScreenChoices.Add(Choice("Disabled", "LOCFirstSetupStartupLoginDisabled", "LOCFirstSetupStartupLoginDisabledDesc", SetupPreview("Startup", "Login.jpg"), SelectLoginScreen));

            InterfaceChoices.Clear();
            InterfaceChoices.Add(Choice("Clean", "LOCFirstSetupInterfaceClean", "LOCFirstSetupInterfaceCleanDesc", SetupPreview("Interface", "Clean.jpg"), SelectInterface));
            InterfaceChoices.Add(Choice("Standard", "LOCFirstSetupInterfaceStandard", "LOCFirstSetupInterfaceStandardDesc", SetupPreview("Interface", "Standard.jpg"), SelectInterface));
            InterfaceChoices.Add(Choice("Enhanced", "LOCFirstSetupInterfaceEnhanced", "LOCFirstSetupInterfaceEnhancedDesc", SetupPreview("Interface", "Enhanced.jpg"), SelectInterface));
            InterfaceChoices.Add(Choice("Complete", "LOCFirstSetupInterfaceComplete", "LOCFirstSetupInterfaceCompleteDesc", SetupPreview("Interface", "Complete.jpg"), SelectInterface));

            ColorChoices.Clear();
            ColorChoices.Add(Choice("Default", "LOCFirstSetupColorDefault", "LOCFirstSetupColorDefaultDesc", SetupPreview("Colors", "Default.jpg"), SelectColor));
            ColorChoices.Add(Choice("OrbisBlue", "LOCFirstSetupColorBlue", "LOCFirstSetupColorBlueDesc", SetupPreview("Colors", "Blue.jpg"), SelectColor));
            ColorChoices.Add(Choice("ScarletRed", "LOCFirstSetupColorRed", "LOCFirstSetupColorRedDesc", SetupPreview("Colors", "Red.jpg"), SelectColor));
            ColorChoices.Add(Choice("HunterGreen", "LOCFirstSetupColorGreen", "LOCFirstSetupColorGreenDesc", SetupPreview("Colors", "Green.jpg"), SelectColor));
            ColorChoices.Add(Choice("MidnightPink", "LOCFirstSetupColorPink", "LOCFirstSetupColorPinkDesc", SetupPreview("Colors", "Pink.jpg"), SelectColor));
            ColorChoices.Add(Choice("SapphirePrestige", "LOCFirstSetupColorPrestige", "LOCFirstSetupColorPrestigeDesc", SetupPreview("Colors", "Prestige.jpg"), SelectColor));

            DetailsChoices.Clear();
            DetailsChoices.Add(Choice("Default", "LOCFirstSetupDetailsSignature", "LOCFirstSetupDetailsSignatureDesc", SetupPreview("Details", "Signature.jpg"), SelectDetails));
            DetailsChoices.Add(Choice("ModernLayout", "LOCFirstSetupDetailsModern", "LOCFirstSetupDetailsModernDesc", SetupPreview("Details", "Modern.jpg"), SelectDetails));
            DetailsChoices.Add(Choice("ClassicLayout", "LOCFirstSetupDetailsClassic", "LOCFirstSetupDetailsClassicDesc", SetupPreview("Details", "Classic.jpg"), SelectDetails));
            DetailsChoices.Add(Choice("OriginalLayout", "LOCFirstSetupDetailsOriginal", "LOCFirstSetupDetailsOriginalDesc", SetupPreview("Details", "Original.jpg"), SelectDetails));
            DetailsChoices.Add(Choice("ShowcaseLayout", "LOCFirstSetupDetailsShowcase", "LOCFirstSetupDetailsShowcaseDesc", SetupPreview("Details", "Showcase.jpg"), SelectDetails));
            DetailsChoices.Add(Choice("PanelLayout", "LOCFirstSetupDetailsPanel", "LOCFirstSetupDetailsPanelDesc", SetupPreview("Details", "Panel.jpg"), SelectDetails));

            ExperienceChoices.Clear();
            ExperienceChoices.Add(Choice("FullAnimated", "LOCFirstSetupExperienceFull", "LOCFirstSetupExperienceFullDesc", SetupPreview("Experience", "FullAnimated.jpg"), SelectExperience));
            ExperienceChoices.Add(Choice("Animated", "LOCFirstSetupExperienceAnimated", "LOCFirstSetupExperienceAnimatedDesc", SetupPreview("Experience", "Animated.jpg"), SelectExperience));
            ExperienceChoices.Add(Choice("Balanced", "LOCFirstSetupExperienceBalanced", "LOCFirstSetupExperienceBalancedDesc", SetupPreview("Experience", "Balanced.jpg"), SelectExperience, true));
            ExperienceChoices.Add(Choice("Performance", "LOCFirstSetupExperiencePerformance", "LOCFirstSetupExperiencePerformanceDesc", SetupPreview("Experience", "Performance.jpg"), SelectExperience));

            TrailerChoices.Clear();
            TrailerChoices.Add(Choice("Disabled", "LOCFirstSetupTrailersDisabled", "LOCFirstSetupTrailersDisabledDesc", SetupPreview("Trailers", "Disabled.jpg"), SelectTrailers));
            TrailerChoices.Add(Choice("DetailsOnly", "LOCFirstSetupTrailersDetails", "LOCFirstSetupTrailersDetailsDesc", SetupPreview("Trailers", "Details.jpg"), SelectTrailers));
            TrailerChoices.Add(Choice("MainAndDetails", "LOCFirstSetupTrailersMainDetails", "LOCFirstSetupTrailersMainDetailsDesc", SetupPreview("Trailers", "MainDetails.jpg"), SelectTrailers));

            AvatarChoices.Clear();
            foreach (var preset in themeSettingsService
                .GetPresetItems("Avatar")
                .OrderBy(GetAvatarSortOrder)
                .ThenBy(item => item?.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Take(40))
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.Key))
                {
                    continue;
                }

                var avatarChoice = new AnikiFirstSetupChoice
                {
                    Key = preset.Key,
                    Title = preset.DisplayName,
                    Description = string.Empty,
                    PreviewPath = themeSettingsService.ResolveThemeFilePath(preset.Preview),
                    IsRecommended = false
                };

                avatarChoice.SelectCommand = new RelayCommand(() => SelectAvatar(avatarChoice));
                AvatarChoices.Add(avatarChoice);
            }

            SelectedStartup = Find(StartupChoices, "Hub");
            SelectedLoginScreen = Find(LoginScreenChoices, "Disabled");
            SelectedInterface = Find(InterfaceChoices, "Standard");
            SelectedColor = Find(ColorChoices, "Default");
            SelectedDetails = Find(DetailsChoices, "Default");
            SelectedExperience = Find(ExperienceChoices, "Balanced");
            SelectedTrailers = Find(TrailerChoices, "DetailsOnly");
            SelectedAvatar = Find(AvatarChoices, "AvatarDefault") ?? AvatarChoices.FirstOrDefault();
        }

        private AnikiFirstSetupChoice Choice(
            string key,
            string titleResourceKey,
            string descriptionResourceKey,
            string previewPath,
            Action<AnikiFirstSetupChoice> selectAction,
            bool recommended = false)
        {
            var choice = new AnikiFirstSetupChoice
            {
                Key = key,
                Title = Loc(titleResourceKey, key),
                Description = Loc(descriptionResourceKey, string.Empty),
                PreviewPath = previewPath,
                IsRecommended = recommended
            };

            choice.SelectCommand = new RelayCommand(() => selectAction?.Invoke(choice));
            return choice;
        }

        private string Loc(string key, string fallback)
        {
            try
            {
                var value = Application.Current?.TryFindResource(key)?.ToString();
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private string SetupPreview(string folder, string fileName)
        {
            return themeSettingsService.ResolveThemeFilePath(
                Path.Combine("Themes Option", "11.First Setup", folder, fileName));
        }

        private static int GetAvatarSortOrder(AnikiPresetItem preset)
        {
            var key = preset?.Key ?? string.Empty;

            if (string.Equals(key, "AvatarDefault", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (key.StartsWith("Avatar", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(key.Substring("Avatar".Length), out var number))
            {
                return number;
            }

            return int.MaxValue;
        }

        private static AnikiFirstSetupChoice Find(
            IEnumerable<AnikiFirstSetupChoice> choices,
            string key)
        {
            return choices?.FirstOrDefault(choice =>
                string.Equals(choice?.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshStatuses()
        {
            SteamReady = settings?.SteamAccountServicesReady == true;
            PlayniteAchievementsInstalled = IsPluginInstalled(PlayniteAchievementsPluginId);
            UniPlaySongInstalled = IsPluginInstalled(UniPlaySongPluginId);
            AudioSwitcherInstalled = IsPluginInstalled(AudioSwitcherPluginId);
            CustomFadeAnimationInstalled = IsPluginInstalled(CustomFadeAnimationPluginId);
            ExtraMetadataLoaderInstalled = IsPluginInstalled(ExtraMetadataLoaderPluginId);
            ExtraMetadataToolsInstalled = IsExtraMetadataFullscreenHelperInstalled();
            ScreenshotsVisualizerInstalled = IsPluginInstalled(ScreenshotsVisualizerPluginId);
            ScreenshotUtilitiesInstalled = IsPluginInstalled(ScreenshotUtilitiesPluginId);
            ScreenshotUtilitiesLocalProviderInstalled =
                IsPluginInstalled(ScreenshotUtilitiesLocalProviderPluginId);

            OnPropertyChanged(nameof(SteamReady));
            OnPropertyChanged(nameof(PlayniteAchievementsInstalled));
            OnPropertyChanged(nameof(UniPlaySongInstalled));
            OnPropertyChanged(nameof(AudioSwitcherInstalled));
            OnPropertyChanged(nameof(CustomFadeAnimationInstalled));
            OnPropertyChanged(nameof(ExtraMetadataLoaderInstalled));
            OnPropertyChanged(nameof(ExtraMetadataToolsInstalled));
            OnPropertyChanged(nameof(ExtraMetadataInstalled));
            OnPropertyChanged(nameof(ExtraMetadataNeedsTools));
            OnPropertyChanged(nameof(ScreenshotsVisualizerInstalled));
            OnPropertyChanged(nameof(ScreenshotUtilitiesInstalled));
            OnPropertyChanged(nameof(ScreenshotUtilitiesLocalProviderInstalled));
            OnPropertyChanged(nameof(ScreenshotPluginInstalled));
            OnPropertyChanged(nameof(ScreenshotNoProviderInstalled));
            OnPropertyChanged(nameof(ScreenshotUtilitiesNeedsLocalProvider));
        }

        private void TogglePlayniteAchievementsInstall()
        {
            ToggleQueuedAddon(PlayniteAchievementsAddonId);
        }

        private void ToggleUniPlaySongInstall()
        {
            ToggleQueuedAddon(UniPlaySongAddonId);
        }

        private void ToggleAudioSwitcherInstall()
        {
            ToggleQueuedAddon(AudioSwitcherAddonId);
        }

        private void ToggleCustomFadeAnimationInstall()
        {
            ToggleQueuedAddon(CustomFadeAnimationAddonId);
        }

        private void ToggleExtraMetadataInstall()
        {
            if (ExtraMetadataQueued)
            {
                RemoveQueuedAddon(ExtraMetadataLoaderAddonId);
                RemoveQueuedAddon(ExtraMetadataToolsAddonId);
            }
            else
            {
                if (!ExtraMetadataLoaderInstalled)
                {
                    AddQueuedAddon(ExtraMetadataLoaderAddonId);
                }

                if (!ExtraMetadataToolsInstalled)
                {
                    AddQueuedAddon(ExtraMetadataToolsAddonId);
                }

                ShowAddonScheduledMessageOnce();
            }

            NotifyQueuedAddonStateChanged();
        }

        private void ToggleScreenshotUtilitiesLocalProviderInstall()
        {
            if (IsAddonQueued(ScreenshotUtilitiesLocalProviderAddonId))
            {
                RemoveQueuedAddon(ScreenshotUtilitiesLocalProviderAddonId);
                queuedScreenshotProviderMode = null;
            }
            else
            {
                AddQueuedAddon(ScreenshotUtilitiesLocalProviderAddonId);
                queuedScreenshotProviderMode = AnikiMediaProviderMode.ScreenshotUtilitiesLocal;
                ShowAddonScheduledMessageOnce();
            }

            NotifyQueuedAddonStateChanged();
        }

        private void ChooseOrCancelScreenshotProviderInstall()
        {
            if (ScreenshotProviderQueued)
            {
                RemoveQueuedAddon(ScreenshotsVisualizerAddonId);
                RemoveQueuedAddon(ScreenshotUtilitiesAddonId);
                RemoveQueuedAddon(ScreenshotUtilitiesLocalProviderAddonId);
                queuedScreenshotProviderMode = null;
                NotifyQueuedAddonStateChanged();
                return;
            }

            if (playniteApi?.Dialogs == null)
            {
                return;
            }

            var visualizerOption = new MessageBoxOption(
                Loc("LOCFirstSetupScreenshotChoiceVisualizer", "Screenshots Visualizer"));
            var utilitiesOption = new MessageBoxOption(
                Loc("LOCFirstSetupScreenshotChoiceUtilities", "Screenshot Utilities"));
            var cancelOption = new MessageBoxOption(
                Loc("LOCFirstSetupScreenshotChoiceCancel", "Cancel"));

            var result = playniteApi.Dialogs.ShowMessage(
                Loc(
                    "LOCFirstSetupScreenshotChoiceMessage",
                    "Choose the screenshot provider you want to install. Only one provider is required."),
                Loc("LOCFirstSetupScreenshotChoiceTitle", "Install a screenshot provider"),
                MessageBoxImage.Information,
                new List<MessageBoxOption>
                {
                    visualizerOption,
                    utilitiesOption,
                    cancelOption
                });

            if (result == visualizerOption)
            {
                AddQueuedAddon(ScreenshotsVisualizerAddonId);
                queuedScreenshotProviderMode = AnikiMediaProviderMode.ScreenshotsVisualizer;
                ShowAddonScheduledMessageOnce();
            }
            else if (result == utilitiesOption)
            {
                AddQueuedAddon(ScreenshotUtilitiesAddonId);

                // Screenshot Utilities needs its Local Provider before Aniki Helper can read
                // local captures. Keep it as a separate item in the installation sequence.
                if (!ScreenshotUtilitiesLocalProviderInstalled)
                {
                    AddQueuedAddon(ScreenshotUtilitiesLocalProviderAddonId);
                }

                queuedScreenshotProviderMode = AnikiMediaProviderMode.ScreenshotUtilitiesLocal;
                ShowAddonScheduledMessageOnce();
            }

            NotifyQueuedAddonStateChanged();
        }

        private void ToggleQueuedAddon(string addonId)
        {
            if (string.IsNullOrWhiteSpace(addonId))
            {
                return;
            }

            if (IsAddonQueued(addonId))
            {
                RemoveQueuedAddon(addonId);
            }
            else
            {
                AddQueuedAddon(addonId);
                ShowAddonScheduledMessageOnce();
            }

            NotifyQueuedAddonStateChanged();
        }

        private bool IsAddonQueued(string addonId)
        {
            return !string.IsNullOrWhiteSpace(addonId) &&
                   queuedAddonIds.Any(id =>
                       string.Equals(id, addonId, StringComparison.OrdinalIgnoreCase));
        }

        private void AddQueuedAddon(string addonId)
        {
            if (!string.IsNullOrWhiteSpace(addonId) && !IsAddonQueued(addonId))
            {
                queuedAddonIds.Add(addonId);
            }
        }

        private void RemoveQueuedAddon(string addonId)
        {
            queuedAddonIds.RemoveAll(id =>
                string.Equals(id, addonId, StringComparison.OrdinalIgnoreCase));
        }

        private void ResetQueuedAddonSelections()
        {
            queuedAddonIds.Clear();
            queuedScreenshotProviderMode = null;
            addonInstallNoticeShown = false;
            NotifyQueuedAddonStateChanged();
        }

        private void ShowAddonScheduledMessageOnce()
        {
            if (addonInstallNoticeShown || playniteApi?.Dialogs == null)
            {
                return;
            }

            addonInstallNoticeShown = true;
            playniteApi.Dialogs.ShowMessage(
                Loc(
                    "LOCFirstSetupAddonScheduledMessage",
                    "This extension will be installed after your setup settings have been applied. Playnite may ask to restart during the installation sequence."),
                Loc("LOCFirstSetupAddonScheduledTitle", "Installation scheduled"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void NotifyQueuedAddonStateChanged()
        {
            OnPropertyChanged(nameof(PlayniteAchievementsQueued));
            OnPropertyChanged(nameof(UniPlaySongQueued));
            OnPropertyChanged(nameof(AudioSwitcherQueued));
            OnPropertyChanged(nameof(CustomFadeAnimationQueued));
            OnPropertyChanged(nameof(ExtraMetadataQueued));
            OnPropertyChanged(nameof(ScreenshotProviderQueued));
            OnPropertyChanged(nameof(ScreenshotUtilitiesLocalProviderQueued));
            OnPropertyChanged(nameof(HasQueuedAddonInstallations));
            OnPropertyChanged(nameof(QueuedAddonCount));
            OnPropertyChanged(nameof(QueuedAddonSummary));
            OnPropertyChanged(nameof(FinishButtonText));
        }

        private void PersistQueuedAddonInstallations()
        {
            var pending = queuedAddonIds
                .Where(id => !IsAddonInstalledByAddonId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            settings.PendingFirstSetupAddonInstallIds = pending;
            settings.PendingFirstSetupAddonInstallIndex = 0;
            settings.PendingFirstSetupAddonInstallCurrentLaunched = false;
        }

        public bool PreparePendingAddonInstallations()
        {
            try
            {
                if (settings?.PendingFirstSetupAddonInstallIds == null ||
                    settings.PendingFirstSetupAddonInstallIds.Count == 0)
                {
                    return false;
                }

                NormalizePersistedAddonInstallQueueAfterRestart();

                if (settings.PendingFirstSetupAddonInstallIds.Count == 0)
                {
                    return false;
                }

                IsOfferVisible = false;
                IsApplying = false;
                IsAddonInstallFlowActive = true;
                IsActive = true;
                NotifyPendingAddonInstallStateChanged();
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Failed to prepare pending add-on installations.");
                return false;
            }
        }

        private void NormalizePersistedAddonInstallQueueAfterRestart()
        {
            if (settings?.PendingFirstSetupAddonInstallIds == null)
            {
                return;
            }

            var changed = false;
            settings.PendingFirstSetupAddonInstallIndex = Math.Max(
                0,
                settings.PendingFirstSetupAddonInstallIndex);

            while (settings.PendingFirstSetupAddonInstallIndex <
                   settings.PendingFirstSetupAddonInstallIds.Count)
            {
                var currentId = settings.PendingFirstSetupAddonInstallIds[
                    settings.PendingFirstSetupAddonInstallIndex];

                if (!IsAddonInstalledByAddonId(currentId))
                {
                    break;
                }

                settings.PendingFirstSetupAddonInstallIndex++;
                settings.PendingFirstSetupAddonInstallCurrentLaunched = false;
                changed = true;
            }

            if (settings.PendingFirstSetupAddonInstallIndex >=
                settings.PendingFirstSetupAddonInstallIds.Count)
            {
                ClearPersistedAddonInstallQueue(save: false);
                changed = true;
            }

            if (changed)
            {
                plugin?.SavePluginSettings(settings);
            }
        }

        private void InstallCurrentPendingAddon()
        {
            var addonId = GetCurrentPersistedAddonId();
            if (string.IsNullOrWhiteSpace(addonId))
            {
                return;
            }

            settings.PendingFirstSetupAddonInstallCurrentLaunched = true;
            plugin?.SavePluginSettings(settings);
            NotifyPendingAddonInstallStateChanged();
            OpenPlayniteAddonInstaller(addonId);
        }

        private void ContinuePendingAddonInstall()
        {
            if (!CanContinuePendingAddonInstall)
            {
                return;
            }

            settings.PendingFirstSetupAddonInstallIndex++;
            settings.PendingFirstSetupAddonInstallCurrentLaunched = false;
            plugin?.SavePluginSettings(settings);

            NormalizePersistedAddonInstallQueueAfterRestart();
            NotifyPendingAddonInstallStateChanged();
            plugin?.FocusFirstSetupControl("FirstSetupAddonInstallButton");
        }

        private async void FinishPendingAddonInstall()
        {
            if (!CanFinishPendingAddonInstall)
            {
                return;
            }

            ClearPersistedAddonInstallQueue(save: true);
            IsAddonInstallFlowActive = false;
            IsApplying = false;
            ApplyProgress = 0;
            ApplyStatus = string.Empty;

            if (settings != null)
            {
                settings.IsSecondaryMusicWindowOpen = false;
            }

            IsClosing = true;
            IsActive = false;

            await plugin.CompleteInitialSetupWithoutRestartAsync(
                settings?.OpenWelcomeHubOnStartup == true,
                resumeDeferredStartupPrompts: true,
                resumeWhatsNew: false);

            IsClosing = false;
        }

        private void ClearPersistedAddonInstallQueue(bool save)
        {
            settings.PendingFirstSetupAddonInstallIds = new List<string>();
            settings.PendingFirstSetupAddonInstallIndex = 0;
            settings.PendingFirstSetupAddonInstallCurrentLaunched = false;

            if (save)
            {
                plugin?.SavePluginSettings(settings);
            }

            NotifyPendingAddonInstallStateChanged();
        }

        private string GetCurrentPersistedAddonId()
        {
            if (settings?.PendingFirstSetupAddonInstallIds == null ||
                settings.PendingFirstSetupAddonInstallIds.Count == 0)
            {
                return string.Empty;
            }

            var index = Math.Max(0, settings.PendingFirstSetupAddonInstallIndex);
            if (index >= settings.PendingFirstSetupAddonInstallIds.Count)
            {
                return string.Empty;
            }

            return settings.PendingFirstSetupAddonInstallIds[index] ?? string.Empty;
        }

        private string GetAddonDisplayName(string addonId)
        {
            if (string.Equals(addonId, PlayniteAchievementsAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "PlayniteAchievements";
            }

            if (string.Equals(addonId, UniPlaySongAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "UniPlaySong";
            }

            if (string.Equals(addonId, AudioSwitcherAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Playnite Audio Switcher";
            }

            if (string.Equals(addonId, CustomFadeAnimationAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Custom Fade Animations";
            }

            if (string.Equals(addonId, ExtraMetadataLoaderAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Extra Metadata Loader";
            }

            if (string.Equals(addonId, ExtraMetadataToolsAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Extra Metadata Fullscreen Mode Helper";
            }

            if (string.Equals(addonId, ScreenshotsVisualizerAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Screenshots Visualizer";
            }

            if (string.Equals(addonId, ScreenshotUtilitiesAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Screenshot Utilities";
            }

            if (string.Equals(addonId, ScreenshotUtilitiesLocalProviderAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Screenshot Utilities - Local Provider";
            }

            return addonId ?? string.Empty;
        }

        private bool IsAddonInstalledByAddonId(string addonId)
        {
            if (string.Equals(addonId, PlayniteAchievementsAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(PlayniteAchievementsPluginId);
            }

            if (string.Equals(addonId, UniPlaySongAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(UniPlaySongPluginId);
            }

            if (string.Equals(addonId, AudioSwitcherAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(AudioSwitcherPluginId);
            }

            if (string.Equals(addonId, CustomFadeAnimationAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(CustomFadeAnimationPluginId);
            }

            if (string.Equals(addonId, ExtraMetadataLoaderAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(ExtraMetadataLoaderPluginId);
            }

            if (string.Equals(addonId, ExtraMetadataToolsAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsExtraMetadataFullscreenHelperInstalled();
            }

            if (string.Equals(addonId, ScreenshotsVisualizerAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(ScreenshotsVisualizerPluginId);
            }

            if (string.Equals(addonId, ScreenshotUtilitiesAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(ScreenshotUtilitiesPluginId);
            }

            if (string.Equals(addonId, ScreenshotUtilitiesLocalProviderAddonId, StringComparison.OrdinalIgnoreCase))
            {
                return IsPluginInstalled(ScreenshotUtilitiesLocalProviderPluginId);
            }

            return false;
        }

        private void NotifyPendingAddonInstallStateChanged()
        {
            OnPropertyChanged(nameof(HasPersistedPendingAddonInstallations));
            OnPropertyChanged(nameof(PendingAddonInstallCount));
            OnPropertyChanged(nameof(PendingAddonInstallCurrentNumber));
            OnPropertyChanged(nameof(PendingAddonInstallIsLast));
            OnPropertyChanged(nameof(PendingAddonInstallCurrentLaunched));
            OnPropertyChanged(nameof(PendingAddonInstallCurrentId));
            OnPropertyChanged(nameof(PendingAddonInstallCurrentName));
            OnPropertyChanged(nameof(PendingAddonInstallProgressText));
            OnPropertyChanged(nameof(PendingAddonInstallButtonText));
            OnPropertyChanged(nameof(CanContinuePendingAddonInstall));
            OnPropertyChanged(nameof(CanFinishPendingAddonInstall));
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
                        return parameters.Length == 1 &&
                               parameters[0].ParameterType == typeof(string);
                    });

                if (navigateMethod != null)
                {
                    navigateMethod.Invoke(null, new object[] { uri });
                    return;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Playnite NavigateUrl failed.");
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
                playniteApi?.Dialogs?.ShowErrorMessage(
                    Loc(
                        "LOCFirstSetupAddonInstallError",
                        "Playnite could not open the extension installer."),
                    "Aniki Helper");

                logger?.Warn(ex, "[AnikiHelper][FirstSetup] Add-on install URI failed.");
            }
        }

        private bool IsPluginInstalled(Guid pluginId)
        {
            try
            {
                return playniteApi?.Addons?.Plugins?.Any(addon => addon.Id == pluginId) == true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsExtraMetadataFullscreenHelperInstalled()
        {
            // Extra Metadata Fullscreen Mode Helper is a PowerShell script extension.
            // Script extensions are not always exposed through Addons.Plugins, so the
            // installed extension manifest is used as the authoritative fallback.
            return IsPluginInstalled(ExtraMetadataToolsPluginId) ||
                   IsExtensionManifestInstalled(
                       ExtraMetadataToolsAddonId,
                       "Extra Metadata Fullscreen Mode Helper",
                       "ExtraMetadataTools.psm1");
        }

        private bool IsExtensionManifestInstalled(params string[] markers)
        {
            if (markers == null || markers.Length == 0 || playniteApi?.Paths == null)
            {
                return false;
            }

            var normalizedMarkers = markers
                .Where(marker => !string.IsNullOrWhiteSpace(marker))
                .Select(NormalizeExtensionIdentity)
                .Where(marker => !string.IsNullOrWhiteSpace(marker))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedMarkers.Length == 0)
            {
                return false;
            }

            var extensionRoots = new[]
            {
                Path.Combine(playniteApi.Paths.ConfigurationPath ?? string.Empty, "Extensions"),
                Path.Combine(playniteApi.Paths.ApplicationPath ?? string.Empty, "Extensions")
            };

            foreach (var root in extensionRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    foreach (var manifestPath in Directory.EnumerateFiles(
                        root,
                        "extension.y*ml",
                        SearchOption.AllDirectories))
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
                                    var entry = manifest.FirstOrDefault(item =>
                                        string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

                                    if (entry.Value != null)
                                    {
                                        identities.Add(entry.Value.ToString());
                                    }
                                }
                            }

                            var normalizedIdentities = identities
                                .Where(identity => !string.IsNullOrWhiteSpace(identity))
                                .Select(NormalizeExtensionIdentity)
                                .Where(identity => !string.IsNullOrWhiteSpace(identity))
                                .ToArray();

                            if (normalizedMarkers.Any(marker => normalizedIdentities.Any(identity =>
                                string.Equals(identity, marker, StringComparison.OrdinalIgnoreCase))))
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

        private void MoveNext()
        {
            if (CurrentPage >= CompletePage)
            {
                return;
            }

            CurrentPage++;
            RaiseSummaryProperties();
        }

        private void MoveBack()
        {
            if (CurrentPage <= WelcomePage)
            {
                return;
            }

            CurrentPage--;
        }

        private void UseDefaults()
        {
            var defaultOptions = themeSettingsService.GetAllDefaultOptionValues();
            var defaultPresets = themeSettingsService.GetAllDefaultPresetSelections();

            restoreAllThemeDefaultsRequested = true;

            UserNameInput.SetCurrentStringValueSilently(
                GetDefaultString(defaultOptions, "UserName", "Player"));

            // The startup destination is stored by Aniki Helper rather than in the theme YAML.
            // Welcome Hub is the recommended default destination for a clean reset.
            SelectedStartup = Find(StartupChoices, "Hub")
                ?? StartupChoices.FirstOrDefault();

            SelectedLoginScreen = Find(
                LoginScreenChoices,
                GetDefaultBool(defaultOptions, "AcceuilOrNot") ? "Enabled" : "Disabled")
                ?? LoginScreenChoices.FirstOrDefault();

            SelectedInterface = Find(InterfaceChoices, "Standard")
                ?? InterfaceChoices.FirstOrDefault();

            SelectedColor = Find(
                ColorChoices,
                GetDefaultPreset(defaultPresets, "Interface", "Default"))
                ?? Find(ColorChoices, "Default")
                ?? ColorChoices.FirstOrDefault();

            SelectedDetails = Find(
                DetailsChoices,
                GetDefaultPreset(defaultPresets, "DetailsViewAlt", "Default"))
                ?? Find(DetailsChoices, "Default")
                ?? DetailsChoices.FirstOrDefault();

            SelectedExperience = Find(ExperienceChoices, "Balanced")
                ?? ExperienceChoices.FirstOrDefault();

            SelectedTrailers = Find(
                TrailerChoices,
                ResolveDefaultTrailerProfile(defaultOptions))
                ?? Find(TrailerChoices, "DetailsOnly")
                ?? TrailerChoices.FirstOrDefault();

            SelectedAvatar = Find(
                AvatarChoices,
                GetDefaultPreset(defaultPresets, "Avatar", "AvatarDefault"))
                ?? Find(AvatarChoices, "AvatarDefault")
                ?? AvatarChoices.FirstOrDefault();

            // All YAML-backed categories are intentionally marked as changed so that
            // the final Apply action also reflects the default selections in the summary.
            startupChanged = true;
            loginScreenChanged = true;
            interfaceChanged = true;
            colorChanged = true;
            detailsChanged = true;
            experienceChanged = true;
            trailersChanged = true;
            avatarChanged = true;

            CurrentPage = CompletePage;
            RaiseSummaryProperties();
        }

        private static string GetDefaultString(
            IReadOnlyDictionary<string, object> defaults,
            string key,
            string fallback)
        {
            if (defaults != null &&
                defaults.TryGetValue(key, out var value) &&
                value != null &&
                !string.IsNullOrWhiteSpace(value.ToString()))
            {
                return value.ToString().Trim();
            }

            return fallback;
        }

        private static bool GetDefaultBool(
            IReadOnlyDictionary<string, object> defaults,
            string key)
        {
            if (defaults == null ||
                !defaults.TryGetValue(key, out var value) ||
                value == null)
            {
                return false;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            return bool.TryParse(value.ToString(), out var parsed) && parsed;
        }

        private static string GetDefaultPreset(
            IReadOnlyDictionary<string, string> defaults,
            string groupId,
            string fallback)
        {
            if (defaults != null &&
                defaults.TryGetValue(groupId, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback;
        }

        private static string ResolveDefaultTrailerProfile(
            IReadOnlyDictionary<string, object> defaults)
        {
            var main = GetDefaultBool(defaults, "TrailerOnMainView");
            var details = GetDefaultBool(defaults, "TrailerOnDetailsView");

            if (main && details)
            {
                return "MainAndDetails";
            }

            return details ? "DetailsOnly" : "Disabled";
        }

        private async void Finish()
        {
            if (IsApplying)
            {
                return;
            }

            try
            {
                IsApplying = true;
                ApplyProgress = 0;

                await UpdateApplyProgressAsync(8, "LOCFirstSetupApplyingPrepare", "Preparing your configuration...", 260);

                var normalizedName = NormalizeUserName(UserNameInput?.CurrentStringValue);
                UserNameInput.SetCurrentStringValueSilently(normalizedName);

                var options = restoreAllThemeDefaultsRequested
                    ? themeSettingsService.GetAllDefaultOptionValues()
                    : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                foreach (var pair in BuildOptionValues(normalizedName))
                {
                    options[pair.Key] = pair.Value;
                }

                var presets = restoreAllThemeDefaultsRequested
                    ? themeSettingsService.GetAllDefaultPresetSelections()
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!isManualLaunch || colorChanged)
                {
                    presets["Interface"] = SelectedColor?.Key ?? "Default";
                }

                if (!isManualLaunch || detailsChanged)
                {
                    presets["DetailsViewAlt"] = SelectedDetails?.Key ?? "Default";
                }

                if (!isManualLaunch || avatarChanged)
                {
                    presets["Avatar"] = SelectedAvatar?.Key ?? "AvatarDefault";
                }

                if (!isManualLaunch || startupChanged)
                {
                    settings.OpenWelcomeHubOnStartup = string.Equals(
                        SelectedStartup?.Key,
                        "Hub",
                        StringComparison.OrdinalIgnoreCase);
                }

                settings.IsWelcomeHubOpen = false;

                await UpdateApplyProgressAsync(28, "LOCFirstSetupApplyingTheme", "Applying theme options...", 320);

                themeSettingsService.ApplyInitialSetupConfiguration(
                    options,
                    presets,
                    suppressRestartPrompt: true);

                await UpdateApplyProgressAsync(62, "LOCFirstSetupApplyingProfile", "Saving your profile...", 360);

                themeSettingsService.MarkInitialSetupCompleted();

                if (!isManualLaunch)
                {
                    plugin.MarkCurrentWhatsNewAsSeen();
                }

                if (queuedScreenshotProviderMode.HasValue)
                {
                    settings.MediaGalleryProvider = queuedScreenshotProviderMode.Value;
                }

                PersistQueuedAddonInstallations();
                settings.EndEdit();

                await UpdateApplyProgressAsync(88, "LOCFirstSetupApplyingFinish", "Finishing setup...", 360);
                await UpdateApplyProgressAsync(100, "LOCFirstSetupApplyingDone", "Configuration complete.", 520);

                var openWelcomeHub = string.Equals(
                    SelectedStartup?.Key,
                    "Hub",
                    StringComparison.OrdinalIgnoreCase);

                var resumeDeferredStartupPrompts = !isManualLaunch || isOfferedLaunch;
                var resumeWhatsNew = isOfferedLaunch;

                if (HasPersistedPendingAddonInstallations)
                {
                    IsApplying = false;
                    ApplyProgress = 0;
                    ApplyStatus = string.Empty;

                    if (PreparePendingAddonInstallations())
                    {
                        plugin?.FocusFirstSetupControl("FirstSetupAddonInstallButton");
                        return;
                    }
                }

                IsApplying = false;
                ApplyProgress = 0;
                ApplyStatus = string.Empty;

                if (settings != null)
                {
                    settings.IsSecondaryMusicWindowOpen = false;
                }

                IsClosing = true;
                IsActive = false;

                await plugin.CompleteInitialSetupWithoutRestartAsync(
                    openWelcomeHub,
                    resumeDeferredStartupPrompts,
                    resumeWhatsNew);

                IsClosing = false;
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "[AnikiHelper][FirstSetup] Failed to complete the initial setup.");
                IsApplying = false;
                IsClosing = false;
                ApplyProgress = 0;
                ApplyStatus = string.Empty;
            }
        }

        private async Task UpdateApplyProgressAsync(
            double progress,
            string localizationKey,
            string fallback,
            int delayMilliseconds)
        {
            ApplyStatus = Loc(localizationKey, fallback);
            ApplyProgress = Math.Max(0, Math.Min(100, progress));

            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds);
            }
        }

        private Dictionary<string, object> BuildOptionValues(string normalizedName)
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var userNameChanged = !string.Equals(
                initialUserName,
                normalizedName,
                StringComparison.Ordinal);

            if (!isManualLaunch || userNameChanged)
            {
                values["UserName"] = normalizedName;
            }

            if (!isManualLaunch || avatarChanged)
            {
                values["UseSteamAvatar"] = false;
            }

            if (!isManualLaunch || loginScreenChanged)
            {
                values["AcceuilOrNot"] = string.Equals(
                    SelectedLoginScreen?.Key,
                    "Enabled",
                    StringComparison.OrdinalIgnoreCase);
            }

            if (!isManualLaunch || interfaceChanged)
            {
                ApplyInterfaceChoice(values);
            }

            if (!isManualLaunch || experienceChanged)
            {
                ApplyExperienceChoice(values);
            }

            if (!isManualLaunch || trailersChanged)
            {
                ApplyTrailerChoice(values);
            }

            return values;
        }

        private void ApplyInterfaceChoice(IDictionary<string, object> values)
        {
            var profile = SelectedInterface?.Key ?? "Standard";

            foreach (var item in GetInterfaceProfileValues(profile))
            {
                values[item.Key] = item.Value;
            }
        }

        private void ApplyExperienceChoice(IDictionary<string, object> values)
        {
            var profile = SelectedExperience?.Key ?? "Balanced";

            foreach (var item in GetExperienceProfileValues(profile))
            {
                values[item.Key] = item.Value;
            }
        }

        private void ApplyTrailerChoice(IDictionary<string, object> values)
        {
            var profile = SelectedTrailers?.Key ?? "DetailsOnly";

            values["TrailerOnMainView"] = false;
            values["TrailerOnDetailsView"] = false;

            if (string.Equals(profile, "DetailsOnly", StringComparison.OrdinalIgnoreCase))
            {
                values["TrailerOnDetailsView"] = true;
            }
            else if (string.Equals(profile, "MainAndDetails", StringComparison.OrdinalIgnoreCase))
            {
                values["TrailerOnMainView"] = true;
                values["TrailerOnDetailsView"] = true;
            }
        }

        private static string NormalizeUserName(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? "Player" : normalized;
        }

        private void RaiseSummaryProperties()
        {
            OnPropertyChanged(nameof(SummaryUserName));
            OnPropertyChanged(nameof(SummaryStartup));
            OnPropertyChanged(nameof(SummaryLoginScreen));
            OnPropertyChanged(nameof(SummaryInterface));
            OnPropertyChanged(nameof(SummaryColor));
            OnPropertyChanged(nameof(SummaryDetails));
            OnPropertyChanged(nameof(SummaryExperience));
            OnPropertyChanged(nameof(SummaryTrailers));
        }
    }
}
