using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using AnikiHelper.Services.ColorPacks;
using AnikiHelper.Services.CompletePacks;
using AnikiHelper.Services.LoginPacks;
using AnikiHelper.Services.Randomization;
using AnikiHelper.Services.SoundPacks;
using AnikiHelper.Services.VisualPacks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnikiHelper.Services.AnikiThemeSettings
{
    public class AnikiThemeSettingsService
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;

        private void DebugLog(string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    global::AnikiHelper.AnikiLog.Debug(logger, message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private void DebugLog(Exception exception, string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    global::AnikiHelper.AnikiLog.Debug(logger, exception, message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private readonly List<ResourceDictionary> loadedDictionaries = new List<ResourceDictionary>();

        private readonly Dictionary<string, ResourceDictionary> resourceCache =
            new Dictionary<string, ResourceDictionary>(StringComparer.OrdinalIgnoreCase);

        private const int ThemeSettingsSchemaVersion = 3;

        private static readonly HashSet<string> TopBarShortcutOptionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShowAchievementsButton",
            "ShowFriendsButton",
            "ShowMusicPlayerButton",
            "ShowWebBrowserButton",
            "ShowMediaGalleryButton",
            "ShowVideoPlayerButton",
            "ShowCommunityPacksButton",
            "ShowSoftwareToolsButton",
            "ShowAudioSwitcherButton",
            "ShowControllerManagerButton"
        };

        private const int TopBarShortcutLimit = 4;

        public const int CurrentInitialSetupVersion = 1;
        public const int CurrentInitialSetupOfferVersion = 1;

        private bool pendingRestartPrompt;
        private int initialSetupVersion;
        private int initialSetupOfferVersion;
        private bool initialSetupAutomaticRequired;
        private bool initialSetupStateLoaded;
        private string currentThemePath;
        private AnikiThemeSettingsFile currentFile;
        private Action restartRequiredAction;

        private readonly string pluginUserDataPath;
        private readonly string themeSettingsFilePath;
        private readonly LoginBackgroundMediaService loginBackgroundMediaService;
        private readonly VisualPackImportService visualPackImportService;
        private VisualPackLibrarySnapshot customVisualPackLibrarySnapshot;
        private VisualPackRandomCandidate activeRandomVisualPackCandidate;
        private readonly ColorPackImportService colorPackImportService;
        private ColorPackLibrarySnapshot customColorPackLibrarySnapshot;
        private ThemeColorRandomCandidate activeRandomThemeColorCandidate;
        private readonly LoginPackImportService loginPackImportService;
        private LoginPackLibrarySnapshot loginPackLibrarySnapshot;
        private readonly SoundPackImportService soundPackImportService;
        private SoundPackLibrarySnapshot soundPackLibrarySnapshot;
        private readonly CompletePackImportService completePackImportService;
        private CompletePackLibrarySnapshot completePackLibrarySnapshot;
        private bool applyingCompletePack;
        private int loginBackgroundSelectionGeneration;

        private const string VisualPackPresetGroupId = "VisualPack";
        private const string VisualPackFilterVariableId = "VisualPackType";
        private const string CustomVisualPackFilterValue = "Custom";
        private const string RandomVisualPackFilterValue = "Random";
        private const string CustomVisualPackPresetKey = "Custom";
        internal const string CustomVisualPackVirtualKeyPrefix = "__AnikiHelperVisualPack__:";

        private const string ThemeColorPresetGroupId = "Interface";
        private const string ThemeColorFilterVariableId = "ThemeColorStyle";
        private const string CustomColorPackFilterValue = "Custom";
        private const string RandomThemeColorFilterValue = "Random";
        private const string CustomColorPackPresetKey = "Custom";
        internal const string CustomColorPackVirtualKeyPrefix = "__AnikiHelperColorPack__:";

        private const string LoginBackgroundPresetGroupId = "LoginBackground";
        private const string LoginBackgroundFilterVariableId = "LoginBackgroundType";
        private const string RandomLoginFilterValue = "Random";
        private const string LoginPackFilterValue = "Community";
        private const string LegacyCustomLoginPresetKey = "Login43";
        internal const string LoginPackVirtualKeyPrefix = "__AnikiHelperLoginPack__:";
        private const string LoginPackThemeFile = @"Themes Option\5.LoginScreen\Connexion\LoginPack.xaml";

        private const string SoundPackPresetGroupId = "SoundPack";
        private const string DefaultSoundPackPresetKey = "Default";
        internal const string SoundPackVirtualKeyPrefix = "__AnikiHelperSoundPack__:";

        private const string CompletePackPresetGroupId = "CompletePack";
        private const string NoCompletePackPresetKey = "None";
        internal const string CompletePackVirtualKeyPrefix = "__AnikiHelperCompletePack__:";

        private static readonly Dictionary<string, string> CustomVisualPackResourceFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MainMenuImage153"] = "MainMenu.jpg",
                ["SettingImage153"] = "SettingsBackground.jpg",
                ["FrameSettingImage153"] = "FrameSettingsBackground.jpg",
                ["BoxMessageImage153"] = "MessageBox.jpg",
                ["GameMenuImage153"] = "GameMenu.jpg",
                ["ItemMenuImage153"] = "ItemMenu.jpg",
                ["WelcomeImage153"] = "Welcome.jpg",
                ["MainBackgroundImage153"] = "MainBackground.jpg",
                ["StatViewImage153"] = "StatView.jpg",
                ["FriendsViewImage153"] = "FriendsView.jpg",
                ["LoginImage153"] = "Login.jpg",
                ["SuccessMainImage153"] = "AchievementsView.jpg",
                ["MediaViewImage153"] = "MediaView.jpg",
                ["StoreViewImage153"] = "StoreView.jpg"
            };

        // Focused-cover styles are applied directly to PART_ListGameItems by the Helper.
        // Playnite assigns ItemContainerStyle as a local value, so a Setter/Trigger on the
        // ListBox style cannot reliably replace it. Keeping the switch here means the OFF
        // path uses the exact original ListGameItemStyle with zero per-card feature binding.
        private string pendingFocusedCoverItemStyleKey = "ListGameItemStyle";
        private Window focusedCoverStyleHookedWindow;

        // Focused-cover trailer overlay: one global visual outside the native game-list
        // ScrollViewer. Nothing polls the visual tree; the two one-shot timers only run
        // after a game selection has remained stable long enough.
        private readonly DispatcherTimer focusedCoverOverlayDelayTimer;
        private readonly DispatcherTimer focusedCoverOverlayMediaDelayTimer;
        private Game pendingFocusedCoverOverlayGame;
        private Guid focusedCoverOverlayActiveGameId = Guid.Empty;
        private bool focusedCoverOverlayEnabled;
        private bool focusedCoverOverlayTrailerMode;
        private bool focusedCoverOverlayWaitForSelectionChange;
        private Guid focusedCoverOverlaySuppressedGameId = Guid.Empty;
        private Window focusedCoverOverlayHookedWindow;
        private string focusedCoverOverlayVideoPath;
        private string focusedCoverOverlayBackgroundPath;
        private string focusedCoverOverlayLogoPath;

        // Cached once per fullscreen visual tree. Normal controller navigation does not scan it.
        private Grid focusedCoverOverlayHost;
        private ListBox focusedCoverOverlayGameList;
        private Canvas focusedCoverOverlayLayer;
        private Grid focusedCoverOverlayPanel;
        private Border focusedCoverOverlayCover;
        private Border focusedCoverOverlaySilver;
        private Border focusedCoverOverlayShade;
        private Image focusedCoverOverlayBackground;
        private MediaElement focusedCoverOverlayMedia;
        private MediaElement focusedCoverOverlayEventMedia;
        private Guid focusedCoverOverlayMediaRequestGameId = Guid.Empty;
        private Image focusedCoverOverlayLogo;
        private Border focusedCoverOverlayEdge;
        private Border focusedCoverOverlaySweep;
        private ListBox focusedCoverOverlayObservedGameList;
        private System.Windows.Controls.Primitives.ToggleButton focusedCoverOverlayObservedViewToggle;

        // Main View media-card fallback. The image source is assigned directly instead of
        // binding another FadeImage to SelectedGame.DisplayBackgroundImageObject, because a
        // second FadeImage consumer interferes with BackgroundChanger's own crossfade.
        // A short one-shot delay mirrors the old SourceUpdateDelay and avoids decoding every
        // background crossed during rapid controller navigation.
        private readonly DispatcherTimer mainMediaCardFallbackDelayTimer;
        private Game pendingMainMediaCardFallbackGame;
        private bool mainMediaCardFallbackEnabled;
        private Image mainMediaCardFallbackImage;

        public AnikiThemeSettingsService(
            IPlayniteAPI playniteApi,
            AnikiHelperSettings settings,
            ILogger logger,
            string pluginUserDataPath)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.logger = logger;
            this.pluginUserDataPath = pluginUserDataPath;
            themeSettingsFilePath = Path.Combine(pluginUserDataPath, "ThemeSettings.json");
            loginBackgroundMediaService = new LoginBackgroundMediaService(playniteApi, logger, pluginUserDataPath);
            visualPackImportService = new VisualPackImportService(playniteApi, pluginUserDataPath, logger);
            colorPackImportService = new ColorPackImportService(playniteApi, pluginUserDataPath, logger);
            loginPackImportService = new LoginPackImportService(playniteApi, pluginUserDataPath, logger);
            soundPackImportService = new SoundPackImportService(playniteApi, pluginUserDataPath, logger);
            completePackImportService = new CompletePackImportService(playniteApi, pluginUserDataPath, logger);
            TrySynchronizeNativeSoundFilesEarly();

            focusedCoverOverlayDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(900)
            };
            focusedCoverOverlayDelayTimer.Tick += OnFocusedCoverOverlayDelayElapsed;

            focusedCoverOverlayMediaDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(110)
            };
            focusedCoverOverlayMediaDelayTimer.Tick += OnFocusedCoverOverlayMediaDelayElapsed;

            mainMediaCardFallbackDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            mainMediaCardFallbackDelayTimer.Tick += OnMainMediaCardFallbackDelayElapsed;
        }

        public bool ShouldShowInitialSetup =>
            initialSetupStateLoaded &&
            initialSetupAutomaticRequired &&
            initialSetupVersion < CurrentInitialSetupVersion;

        public bool ShouldOfferInitialSetup =>
            initialSetupStateLoaded &&
            !initialSetupAutomaticRequired &&
            initialSetupVersion < CurrentInitialSetupVersion &&
            initialSetupOfferVersion < CurrentInitialSetupOfferVersion;

        public bool HasPendingInitialSetupExperience =>
            ShouldShowInitialSetup || ShouldOfferInitialSetup;

        public int InitialSetupVersion => initialSetupVersion;

        public int InitialSetupOfferVersion => initialSetupOfferVersion;

        public string CurrentThemePath => currentThemePath ?? string.Empty;

        public void SetRestartRequiredAction(Action action)
        {
            restartRequiredAction = action;
        }

        private void MarkRestartRequired()
        {
            pendingRestartPrompt = true;

            try
            {
                restartRequiredAction?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to mark Playnite settings as requiring restart.");
            }
        }

        public void LoadAndApply()
        {
            try
            {
                currentThemePath = GetCurrentThemePath();

                if (string.IsNullOrWhiteSpace(currentThemePath))
                {
                    return;
                }

                var optionsPath = Path.Combine(currentThemePath, "AnikiThemeSettings.yaml");

                if (!File.Exists(optionsPath))
                {
                    logger?.Warn($"[AnikiHelper] AnikiThemeSettings.yaml not found: {optionsPath}");
                    return;
                }

                currentFile = Serialization.FromYamlFile<AnikiThemeSettingsFile>(optionsPath);

                if (currentFile == null)
                {
                    logger?.Warn($"[AnikiHelper] Failed to read AnikiThemeSettings.yaml: {optionsPath}");
                    return;
                }

                PostLoadPresets();
                PostLoadVariables();
                RefreshInstalledLoginPackItems();
                RefreshInstalledSoundPackItems();
                RefreshInstalledCompletePackItems();

                // Preserve already-installed optional login videos before a theme update can remove
                // them, and restore Helper-managed files back into the theme when needed. This is
                // local-only: no catalog/network access happens during startup.
                SynchronizeLoginBackgroundMedia();

                LoadThemeSettingsStorage();

                var storageChanged = MigrateLegacyMainBackgroundOptions();
                storageChanged = MigrateLegacyMainViewMediaCardOption() || storageChanged;
                storageChanged = MigrateLegacyMainViewBottomBarOption() || storageChanged;
                storageChanged = MigrateLegacyFocusedCoverPreviewOption() || storageChanged;
                storageChanged = MigrateLegacyBackgroundDisplayModeOption() || storageChanged;
                storageChanged = MigrateLegacyPlatformBannerPositionOption() || storageChanged;
                storageChanged = MigratePresetFilterSelections() || storageChanged;
                storageChanged = EnsureLegacyCustomLoginSelectionMigrated() || storageChanged;
                storageChanged = SanitizeThemeSettingsStorage() || storageChanged;
                storageChanged = EnsureManagedLoginSelectionValid() || storageChanged;
                storageChanged = EnsureSelectedLoginPackRuntime() || storageChanged;
                storageChanged = EnsureSelectedSoundPackRuntime() || storageChanged;
                storageChanged = EnsureSelectedCompletePackRuntime() || storageChanged;
                storageChanged = RefreshAllPresetFilters(true) || storageChanged;
                storageChanged = ApplyOptionDependenciesToStorage() || storageChanged;

                if (storageChanged)
                {
                    SaveThemeSettingsFile();
                }

                BuildCategories();

                Apply();

                // 153.Custom files can be locked by WPF. If Custom is the persisted category,
                // restore the active library pack directly into runtime resources on every start.
                if (IsCustomVisualPackFilterActive())
                {
                    customVisualPackLibrarySnapshot = RefreshCustomVisualPackLibrarySnapshot();
                    RefreshCustomVisualPackRuntimeImages();
                    global::AnikiHelper.VisualPackBackgroundComposer.RefreshNow();
                }

                StartPresetFilesPreload();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Aniki Theme Settings.");
            }
        }

        public void Apply()
        {
            try
            {
                if (currentFile == null)
                {
                    return;
                }

                RemoveLoadedDictionaries();

                UpdateSelectedPresetFlags();

                var optionValues = BuildOptionValues();
                ApplyDerivedMainViewMediaCardOptions(optionValues);
                ApplyDerivedMainViewBottomBarOptions(optionValues);
                ApplyDerivedFocusedCoverPreviewOptions(optionValues);
                ApplyDerivedBackgroundDisplayModeOptions(optionValues);
                ApplyDerivedPlatformBannerPositionOptions(optionValues);

                SyncVariableBindableValues(optionValues);

                LoadSelectedPresetFiles();
                LoadRandomVisualPackOverride();
                LoadSelectedCustomColorPack();
                LoadRandomThemeColorOverride();

                var generatedResource = BuildGeneratedResourceDictionary(optionValues);

                if (generatedResource != null)
                {
                    Application.Current.Resources.MergedDictionaries.Add(generatedResource);
                    loadedDictionaries.Add(generatedResource);
                }

                LoadLuckyDayResourceOverride();
                LoadKonamiModeResourceOverride();

                // Expose the live count through Options, which is the binding surface already used by the theme.
                var topBarPinnedCount = GetEnabledTopBarShortcutCount();
                optionValues["TopBarPinnedCount"] = topBarPinnedCount;
                optionValues["TopBarPinnedCountText"] = topBarPinnedCount + " / " + TopBarShortcutLimit;

                settings.Options.Update(optionValues);

                ConfigureMainMediaCardFallback(optionValues);
                ScheduleFocusedCoverItemStyleApply(optionValues);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to apply Aniki Theme Settings.");
            }
        }

        private void ScheduleFocusedCoverItemStyleApply(Dictionary<string, object> optionValues)
        {
            try
            {
                // Older themes that do not expose these options must remain completely untouched.
                if (currentFile?.Variables == null ||
                    (!currentFile.Variables.ContainsKey("FocusedCoverPreview") &&
                     !currentFile.Variables.ContainsKey("MicroTrailerOnFocusedCover") &&
                     !currentFile.Variables.ContainsKey("BackgroundOnFocusedCover")))
                {
                    return;
                }

                var microTrailerEnabled = optionValues != null &&
                                          optionValues.TryGetValue("MicroTrailerOnFocusedCover", out var microTrailerValue) &&
                                          ToBool(microTrailerValue);
                var backgroundEnabled = optionValues != null &&
                                        optionValues.TryGetValue("BackgroundOnFocusedCover", out var backgroundValue) &&
                                        ToBool(backgroundValue);
                var performanceModeEnabled = optionValues != null &&
                                             optionValues.TryGetValue("PerformanceMode", out var performanceModeValue) &&
                                             ToBool(performanceModeValue);

                var globalFocusedCoverOverlayEnabled =
                    !performanceModeEnabled && (microTrailerEnabled || backgroundEnabled);
                var trailerModeEnabled = !performanceModeEnabled && microTrailerEnabled;

                // Both focused-cover modes now use the same single global overlay. Keep every
                // realized game card on the native style so there is no premium visual tree per card.
                pendingFocusedCoverItemStyleKey = "ListGameItemStyle";

                SetFocusedCoverOverlayEnabled(globalFocusedCoverOverlayEnabled, trailerModeEnabled);

                var app = Application.Current;
                var dispatcher = app?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                // Run after the current settings/resource update has completed. If the fullscreen
                // visual tree is still being created, a second ContextIdle pass catches it.
                dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (TryApplyFocusedCoverItemStyle(pendingFocusedCoverItemStyleKey))
                    {
                        return;
                    }

                    var window = Application.Current?.MainWindow;
                    if (window != null && !window.IsLoaded)
                    {
                        HookFocusedCoverStyleWindowLoaded(window);
                        return;
                    }

                    dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        TryApplyFocusedCoverItemStyle(pendingFocusedCoverItemStyleKey);
                    }));
                }));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to schedule focused-cover item style update.");
            }
        }

        private void HookFocusedCoverStyleWindowLoaded(Window window)
        {
            try
            {
                if (window == null)
                {
                    return;
                }

                if (focusedCoverStyleHookedWindow != null &&
                    !ReferenceEquals(focusedCoverStyleHookedWindow, window))
                {
                    focusedCoverStyleHookedWindow.Loaded -= OnFocusedCoverStyleWindowLoaded;
                }

                focusedCoverStyleHookedWindow = window;
                window.Loaded -= OnFocusedCoverStyleWindowLoaded;
                window.Loaded += OnFocusedCoverStyleWindowLoaded;
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverStyle] Failed to hook MainWindow.Loaded.");
            }
        }

        private void OnFocusedCoverStyleWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Window window)
                {
                    window.Loaded -= OnFocusedCoverStyleWindowLoaded;
                }

                focusedCoverStyleHookedWindow = null;

                Application.Current?.Dispatcher?.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(() => TryApplyFocusedCoverItemStyle(pendingFocusedCoverItemStyleKey)));
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverStyle] Failed after MainWindow.Loaded.");
            }
        }

        private bool TryApplyFocusedCoverItemStyle(string styleKey)
        {
            try
            {
                var app = Application.Current;
                var window = app?.MainWindow;
                if (window == null || string.IsNullOrWhiteSpace(styleKey))
                {
                    return false;
                }

                var gameList = FindVisualChildByName<ListBox>(window, "PART_ListGameItems");
                if (gameList == null)
                {
                    return false;
                }

                var style = window.TryFindResource(styleKey) as Style ??
                            app.TryFindResource(styleKey) as Style;
                if (style == null)
                {
                    DebugLog($"[AnikiHelper][FocusedCoverStyle] Resource not found: {styleKey}");
                    return false;
                }

                if (!ReferenceEquals(gameList.ItemContainerStyle, style))
                {
                    // Direct local assignment intentionally wins over the ItemContainerStyle value
                    // Playnite applies to its native game list. This happens only when settings are
                    // applied, never during normal cover navigation.
                    gameList.ItemContainerStyle = style;
                    DebugLog($"[AnikiHelper][FocusedCoverStyle] Applied {styleKey} to PART_ListGameItems.");
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugLog(ex, $"[AnikiHelper][FocusedCoverStyle] Failed to apply style {styleKey}.");
                return false;
            }
        }

        private static T FindVisualChildByName<T>(DependencyObject root, string name)
            where T : FrameworkElement
        {
            if (root == null)
            {
                return null;
            }

            if (root is T rootMatch &&
                string.Equals(rootMatch.Name, name, StringComparison.Ordinal))
            {
                return rootMatch;
            }

            int childCount;
            try
            {
                childCount = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                return null;
            }

            for (var i = 0; i < childCount; i++)
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

                var match = FindVisualChildByName<T>(child, name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private void SetFocusedCoverOverlayEnabled(bool enabled, bool trailerMode)
        {
            var modeChanged = focusedCoverOverlayTrailerMode != trailerMode;
            focusedCoverOverlayEnabled = enabled;
            focusedCoverOverlayTrailerMode = enabled && trailerMode;

            if (!enabled)
            {
                focusedCoverOverlayWaitForSelectionChange = false;
                focusedCoverOverlaySuppressedGameId = Guid.Empty;
                CancelFocusedCoverOverlay(true);
                return;
            }

            if (modeChanged)
            {
                CancelFocusedCoverOverlay(false);
            }

            try
            {
                var selectedGame = playniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                NotifyFocusedCoverGameSelected(selectedGame);
            }
            catch
            {
                // Selection events will retry naturally.
            }
        }

        public void NotifyFocusedCoverGameSelected(Game game)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => NotifyFocusedCoverGameSelected(game)));
                    return;
                }

                ScheduleMainMediaCardFallback(game);

                focusedCoverOverlayDelayTimer?.Stop();
                focusedCoverOverlayMediaDelayTimer?.Stop();
                HideFocusedCoverOverlayVisual();
                pendingFocusedCoverOverlayGame = game;

                if (!focusedCoverOverlayEnabled ||
                    game == null ||
                    playniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                if (focusedCoverOverlayWaitForSelectionChange)
                {
                    if (game.Id == focusedCoverOverlaySuppressedGameId)
                    {
                        pendingFocusedCoverOverlayGame = null;
                        return;
                    }

                    focusedCoverOverlayWaitForSelectionChange = false;
                    focusedCoverOverlaySuppressedGameId = Guid.Empty;
                }

                focusedCoverOverlayDelayTimer.Start();
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverOverlay] Failed to schedule selected game.");
            }
        }

        public void NotifyFocusedCoverViewChanged(bool isDetailsView)
        {
            try
            {
                CancelFocusedCoverOverlay(false);

                if (!focusedCoverOverlayEnabled)
                {
                    return;
                }

                if (isDetailsView)
                {
                    var selectedGame = playniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                    focusedCoverOverlaySuppressedGameId = selectedGame?.Id ?? Guid.Empty;
                    focusedCoverOverlayWaitForSelectionChange = focusedCoverOverlaySuppressedGameId != Guid.Empty;
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverOverlay] Failed to react to fullscreen view change.");
            }
        }

        public void StopFocusedCoverOverlay()
        {
            try
            {
                focusedCoverOverlayEnabled = false;
                focusedCoverOverlayTrailerMode = false;
                focusedCoverOverlayWaitForSelectionChange = false;
                focusedCoverOverlaySuppressedGameId = Guid.Empty;
                CancelFocusedCoverOverlay(true);
            }
            catch
            {
            }
        }

        private void CancelFocusedCoverOverlay(bool unhookWindow)
        {
            try { focusedCoverOverlayDelayTimer?.Stop(); } catch { }
            try { focusedCoverOverlayMediaDelayTimer?.Stop(); } catch { }

            pendingFocusedCoverOverlayGame = null;
            focusedCoverOverlayActiveGameId = Guid.Empty;
            focusedCoverOverlayVideoPath = null;
            focusedCoverOverlayBackgroundPath = null;
            focusedCoverOverlayLogoPath = null;

            HideFocusedCoverOverlayVisual();

            if (unhookWindow && focusedCoverOverlayHookedWindow != null)
            {
                try
                {
                    focusedCoverOverlayHookedWindow.Deactivated -= OnFocusedCoverOverlayWindowDeactivated;
                    focusedCoverOverlayHookedWindow.Activated -= OnFocusedCoverOverlayWindowActivated;
                }
                catch { }

                focusedCoverOverlayHookedWindow = null;
            }
        }

        private Window ResolveFocusedCoverOverlayWindow()
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    return null;
                }

                // Fast path after the first successful resolve: no visual-tree scan.
                if (focusedCoverOverlayHookedWindow != null &&
                    focusedCoverOverlayHookedWindow.IsLoaded &&
                    focusedCoverOverlayGameList != null &&
                    focusedCoverOverlayLayer != null)
                {
                    return focusedCoverOverlayHookedWindow;
                }

                var mainWindow = app.MainWindow;
                if (IsFocusedCoverOverlayWindow(mainWindow))
                {
                    return mainWindow;
                }

                foreach (Window candidate in app.Windows)
                {
                    if (candidate == null || ReferenceEquals(candidate, mainWindow))
                    {
                        continue;
                    }

                    if (IsFocusedCoverOverlayWindow(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverOverlay] Failed to resolve fullscreen window.");
            }

            return null;
        }

        private bool IsFocusedCoverOverlayWindow(Window window)
        {
            if (window == null || !window.IsLoaded)
            {
                return false;
            }

            try
            {
                // Require both controls so a desktop/secondary Playnite window cannot be selected
                // accidentally. This scan only happens after focus has been stable for 900 ms.
                return FindVisualChildByName<ListBox>(window, "PART_ListGameItems") != null &&
                       FindVisualChildByName<Canvas>(window, "AnikiFocusedCoverOverlayLayer") != null;
            }
            catch
            {
                return false;
            }
        }

        private void HookFocusedCoverOverlayWindow(Window window)
        {
            if (window == null || ReferenceEquals(focusedCoverOverlayHookedWindow, window))
            {
                return;
            }

            if (focusedCoverOverlayHookedWindow != null)
            {
                try
                {
                    focusedCoverOverlayHookedWindow.Deactivated -= OnFocusedCoverOverlayWindowDeactivated;
                    focusedCoverOverlayHookedWindow.Activated -= OnFocusedCoverOverlayWindowActivated;
                }
                catch { }
            }

            ClearFocusedCoverOverlayVisualCache();
            focusedCoverOverlayHookedWindow = window;
            window.Deactivated += OnFocusedCoverOverlayWindowDeactivated;
            window.Activated += OnFocusedCoverOverlayWindowActivated;
        }

        private void OnFocusedCoverOverlayWindowDeactivated(object sender, EventArgs e)
        {
            CancelFocusedCoverOverlay(false);
        }

        private void OnFocusedCoverOverlayWindowActivated(object sender, EventArgs e)
        {
            if (!focusedCoverOverlayEnabled)
            {
                return;
            }

            try
            {
                var selectedGame = playniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                NotifyFocusedCoverGameSelected(selectedGame);
            }
            catch
            {
            }
        }

        private void HookFocusedCoverOverlayLayoutEvents(Window window)
        {
            try
            {
                var gameList = focusedCoverOverlayGameList;
                if (!ReferenceEquals(focusedCoverOverlayObservedGameList, gameList))
                {
                    if (focusedCoverOverlayObservedGameList != null)
                    {
                        focusedCoverOverlayObservedGameList.IsVisibleChanged -= OnFocusedCoverOverlayGameListVisibilityChanged;
                        focusedCoverOverlayObservedGameList.Unloaded -= OnFocusedCoverOverlayGameListUnloaded;
                        focusedCoverOverlayObservedGameList.IsKeyboardFocusWithinChanged -= OnFocusedCoverOverlayGameListKeyboardFocusWithinChanged;
                    }

                    focusedCoverOverlayObservedGameList = gameList;
                    if (focusedCoverOverlayObservedGameList != null)
                    {
                        focusedCoverOverlayObservedGameList.IsVisibleChanged += OnFocusedCoverOverlayGameListVisibilityChanged;
                        focusedCoverOverlayObservedGameList.Unloaded += OnFocusedCoverOverlayGameListUnloaded;
                        focusedCoverOverlayObservedGameList.IsKeyboardFocusWithinChanged += OnFocusedCoverOverlayGameListKeyboardFocusWithinChanged;
                    }
                }

                var toggle = FindVisualChildByName<System.Windows.Controls.Primitives.ToggleButton>(window, "ChangeViewButton");
                if (!ReferenceEquals(focusedCoverOverlayObservedViewToggle, toggle))
                {
                    if (focusedCoverOverlayObservedViewToggle != null)
                    {
                        focusedCoverOverlayObservedViewToggle.Checked -= OnFocusedCoverOverlayLayoutToggleChanged;
                        focusedCoverOverlayObservedViewToggle.Unchecked -= OnFocusedCoverOverlayLayoutToggleChanged;
                    }

                    focusedCoverOverlayObservedViewToggle = toggle;
                    if (focusedCoverOverlayObservedViewToggle != null)
                    {
                        focusedCoverOverlayObservedViewToggle.Checked += OnFocusedCoverOverlayLayoutToggleChanged;
                        focusedCoverOverlayObservedViewToggle.Unchecked += OnFocusedCoverOverlayLayoutToggleChanged;
                    }
                }
            }
            catch
            {
            }
        }

        private void UnhookFocusedCoverOverlayLayoutEvents()
        {
            try
            {
                if (focusedCoverOverlayObservedGameList != null)
                {
                    focusedCoverOverlayObservedGameList.IsVisibleChanged -= OnFocusedCoverOverlayGameListVisibilityChanged;
                    focusedCoverOverlayObservedGameList.Unloaded -= OnFocusedCoverOverlayGameListUnloaded;
                    focusedCoverOverlayObservedGameList.IsKeyboardFocusWithinChanged -= OnFocusedCoverOverlayGameListKeyboardFocusWithinChanged;
                }
            }
            catch { }

            try
            {
                if (focusedCoverOverlayObservedViewToggle != null)
                {
                    focusedCoverOverlayObservedViewToggle.Checked -= OnFocusedCoverOverlayLayoutToggleChanged;
                    focusedCoverOverlayObservedViewToggle.Unchecked -= OnFocusedCoverOverlayLayoutToggleChanged;
                }
            }
            catch { }

            focusedCoverOverlayObservedGameList = null;
            focusedCoverOverlayObservedViewToggle = null;
        }

        private void OnFocusedCoverOverlayGameListKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!focusedCoverOverlayEnabled)
            {
                return;
            }

            var gameList = sender as ListBox;
            if (gameList == null)
            {
                return;
            }

            if (!gameList.IsKeyboardFocusWithin)
            {
                // Leaving the game cards (bottom bar, filters, details, another control, etc.)
                // must immediately stop and release the focused-cover preview.
                CancelFocusedCoverOverlay(false);
                return;
            }

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                dispatcher?.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!focusedCoverOverlayEnabled || !gameList.IsKeyboardFocusWithin)
                    {
                        return;
                    }

                    NotifyFocusedCoverGameSelected(playniteApi?.MainView?.SelectedGames?.FirstOrDefault());
                }));
            }
            catch
            {
            }
        }

        private void OnFocusedCoverOverlayGameListVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!focusedCoverOverlayEnabled)
            {
                return;
            }

            var gameList = sender as ListBox;
            if (gameList == null || !gameList.IsVisible)
            {
                CancelFocusedCoverOverlay(false);
                return;
            }

            if (!gameList.IsKeyboardFocusWithin)
            {
                return;
            }

            try
            {
                NotifyFocusedCoverGameSelected(playniteApi?.MainView?.SelectedGames?.FirstOrDefault());
            }
            catch { }
        }

        private void OnFocusedCoverOverlayGameListUnloaded(object sender, RoutedEventArgs e)
        {
            CancelFocusedCoverOverlay(false);
            ClearFocusedCoverOverlayVisualCache();
        }

        private void OnFocusedCoverOverlayLayoutToggleChanged(object sender, RoutedEventArgs e)
        {
            if (!focusedCoverOverlayEnabled)
            {
                return;
            }

            CancelFocusedCoverOverlay(false);

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                dispatcher?.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!focusedCoverOverlayEnabled ||
                        focusedCoverOverlayGameList == null ||
                        !focusedCoverOverlayGameList.IsKeyboardFocusWithin)
                    {
                        return;
                    }

                    NotifyFocusedCoverGameSelected(playniteApi?.MainView?.SelectedGames?.FirstOrDefault());
                }));
            }
            catch { }
        }

        private void OnFocusedCoverOverlayDelayElapsed(object sender, EventArgs e)
        {
            focusedCoverOverlayDelayTimer.Stop();

            try
            {
                var game = pendingFocusedCoverOverlayGame;
                if (!focusedCoverOverlayEnabled || game == null)
                {
                    return;
                }

                var selectedGame = playniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (selectedGame == null || selectedGame.Id != game.Id)
                {
                    return;
                }

                var window = ResolveFocusedCoverOverlayWindow();
                if (window == null || !window.IsVisible)
                {
                    DebugLog("[AnikiHelper][FocusedCoverOverlay] No fullscreen window containing both PART_ListGameItems and the overlay layer was found.");
                    return;
                }

                HookFocusedCoverOverlayWindow(window);

                if (!EnsureFocusedCoverOverlayVisualCache(window))
                {
                    DebugLog("[AnikiHelper][FocusedCoverOverlay] Fullscreen visual cache could not be resolved.");
                    return;
                }

                var host = focusedCoverOverlayHost;
                var gameList = focusedCoverOverlayGameList;
                var layer = focusedCoverOverlayLayer;
                var panel = focusedCoverOverlayPanel;
                var cover = focusedCoverOverlayCover;
                var silver = focusedCoverOverlaySilver;
                var background = focusedCoverOverlayBackground;
                var shade = focusedCoverOverlayShade;
                var edge = focusedCoverOverlayEdge;
                var sweep = focusedCoverOverlaySweep;

                if (!gameList.IsVisible || gameList.SelectedIndex < 0 || gameList.SelectedItem == null)
                {
                    DebugLog($"[AnikiHelper][FocusedCoverOverlay] Game list not ready. Visible={gameList.IsVisible}, SelectedIndex={gameList.SelectedIndex}, SelectedItem={(gameList.SelectedItem != null)}.");
                    return;
                }

                if (!gameList.IsKeyboardFocusWithin)
                {
                    DebugLog("[AnikiHelper][FocusedCoverOverlay] Preview cancelled because focus left the game list.");
                    CancelFocusedCoverOverlay(false);
                    return;
                }

                var container = gameList.ItemContainerGenerator.ContainerFromItem(gameList.SelectedItem) as ListBoxItem ??
                                gameList.ItemContainerGenerator.ContainerFromIndex(gameList.SelectedIndex) as ListBoxItem;
                if (container == null || !container.IsVisible || container.ActualWidth < 2 || container.ActualHeight < 2)
                {
                    DebugLog($"[AnikiHelper][FocusedCoverOverlay] Selected container not ready. Found={(container != null)}, Visible={container?.IsVisible}, Size={container?.ActualWidth:0}x{container?.ActualHeight:0}.");
                    return;
                }

                Rect coverBounds;
                try
                {
                    coverBounds = container.TransformToAncestor(host).TransformBounds(
                        new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                }
                catch
                {
                    return;
                }

                var hostWidth = host.ActualWidth;
                var hostHeight = host.ActualHeight;
                if (hostWidth < 10 || hostHeight < 10)
                {
                    return;
                }

                var coverWidth = coverBounds.Width;
                var coverHeight = coverBounds.Height;
                const double horizontalExpansion = 200.0;
                const double verticalExpansion = 0.0;
                const double safeEdge = 12.0;
                const double targetAspectRatio = 16.0 / 10.0;

                
                var desiredWidth = coverWidth + horizontalExpansion;
                var desiredHeight = coverHeight + verticalExpansion;

                if ((desiredWidth / desiredHeight) < targetAspectRatio)
                {
                    desiredWidth = desiredHeight * targetAspectRatio;
                }
                else
                {
                    desiredHeight = desiredWidth / targetAspectRatio;
                }

                // Keep the exact 16:9 ratio if the target frame has to be reduced to stay on screen.
                var availableWidth = Math.Max(1.0, hostWidth - (safeEdge * 2));
                var availableHeight = Math.Max(1.0, hostHeight - (safeEdge * 2));
                var fitScale = Math.Min(1.0, Math.Min(availableWidth / desiredWidth, availableHeight / desiredHeight));
                var finalWidth = desiredWidth * fitScale;
                var finalHeight = desiredHeight * fitScale;

                var desiredLeft = coverBounds.Left - ((finalWidth - coverWidth) / 2.0);
                var desiredTop = coverBounds.Top - ((finalHeight - coverHeight) / 2.0);
                var maxLeft = Math.Max(safeEdge, hostWidth - finalWidth - safeEdge);
                var maxTop = Math.Max(safeEdge, hostHeight - finalHeight - safeEdge);
                var finalLeft = Math.Max(safeEdge, Math.Min(desiredLeft, maxLeft));
                var finalTop = Math.Max(safeEdge, Math.Min(desiredTop, maxTop));

                ClearFocusedCoverOverlayAnimations(window);

                panel.Width = coverWidth;
                panel.Height = coverHeight;
                Canvas.SetLeft(panel, finalLeft);
                Canvas.SetTop(panel, finalTop);

                var translate = new TranslateTransform(
                    coverBounds.Left - finalLeft,
                    coverBounds.Top - finalTop);
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(translate);
                panel.RenderTransform = transformGroup;

                panel.Opacity = 0;
                cover.Opacity = 1;
                cover.Background = new VisualBrush(container)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };

                silver.Opacity = 0;
                // Keep the transition wash truly white. Main.xaml also defines it as white,
                // but set it here as well because this service owns the runtime overlay state.
                silver.Background = Brushes.White;

                if (edge != null) edge.Opacity = 0;
                if (sweep != null) sweep.Opacity = 0;

                var extraMetadataRoot = (window.TryFindResource("ExtraMetadataPath") ?? Application.Current?.TryFindResource("ExtraMetadataPath"))?.ToString();
                var gameFolder = !string.IsNullOrWhiteSpace(extraMetadataRoot)
                    ? Path.Combine(extraMetadataRoot, "ExtraMetadata", "games", game.Id.ToString())
                    : null;

                focusedCoverOverlayVideoPath = focusedCoverOverlayTrailerMode && !string.IsNullOrWhiteSpace(gameFolder)
                    ? Path.Combine(gameFolder, "VideoMicroTrailer.mp4")
                    : null;
                focusedCoverOverlayLogoPath = !string.IsNullOrWhiteSpace(gameFolder)
                    ? Path.Combine(gameFolder, "logo.png")
                    : null;

                var hasVideo = focusedCoverOverlayTrailerMode && IsExistingLocalFile(focusedCoverOverlayVideoPath);
                focusedCoverOverlayBackgroundPath = hasVideo ? null : ResolveGameBackgroundPath(game);

                var hasBackground = IsExistingLocalFile(focusedCoverOverlayBackgroundPath);
                var hasLogo = IsExistingLocalFile(focusedCoverOverlayLogoPath);
                var hasRevealContent = hasVideo || hasBackground;

                // Background previews are static images, so prepare the destination image before the
                // expansion begins. Unlike the video path, the destination frame is available immediately,
                // which lets us build a true cover/background crossfade during the normal zoom motion.
                if (!hasVideo && hasBackground && background != null)
                {
                    background.BeginAnimation(UIElement.OpacityProperty, null);
                    background.Source = LoadOverlayBitmap(focusedCoverOverlayBackgroundPath, 720);
                    // Keep the destination background fully rendered behind the cover from frame 1.
                    // The cover and white wash hide it until the handoff, so there is no visible
                    // background fade-in / "arrival" during the zoom.
                    background.Opacity = 1;
                    background.Visibility = Visibility.Visible;
                }

                focusedCoverOverlayActiveGameId = game.Id;
                layer.Visibility = Visibility.Visible;
                panel.Visibility = Visibility.Visible;

                panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(70))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

                // Keep the validated normal geometric zoom for both video and background modes.
                // The only difference between the two paths is how cover/background content crossfades inside it.
                panel.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(coverWidth, finalWidth, TimeSpan.FromMilliseconds(480))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

                panel.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(coverHeight, finalHeight, TimeSpan.FromMilliseconds(480))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translate.X, 0, TimeSpan.FromMilliseconds(480))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 0, TimeSpan.FromMilliseconds(480))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

                if (edge != null)
                {
                    edge.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.72, TimeSpan.FromMilliseconds(360))
                    {
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.HoldEnd
                    }, HandoffBehavior.SnapshotAndReplace);
                }

                if (hasRevealContent)
                {
                    if (hasVideo)
                    {
                        // Keep the cover fully visible until MediaOpened confirms that the trailer is ready.
                        // This avoids ever exposing the empty MediaElement / panel background during the zoom.
                        var coverHold = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                        coverHold.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        coverHold.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
                        cover.BeginAnimation(UIElement.OpacityProperty, coverHold, HandoffBehavior.SnapshotAndReplace);

                        // Premium exposure wash: it starts with the zoom, builds progressively instead of flashing,
                        // and remains as a soft light veil until the first trailer frame is ready.
                        var colorWashHold = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                        colorWashHold.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        colorWashHold.KeyFrames.Add(new EasingDoubleKeyFrame(0.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70)), new SineEase { EasingMode = EasingMode.EaseOut }));
                        colorWashHold.KeyFrames.Add(new EasingDoubleKeyFrame(0.28, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                        colorWashHold.KeyFrames.Add(new EasingDoubleKeyFrame(0.34, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new SineEase { EasingMode = EasingMode.EaseOut }));
                        colorWashHold.KeyFrames.Add(new LinearDoubleKeyFrame(0.34, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
                        silver.BeginAnimation(UIElement.OpacityProperty, colorWashHold, HandoffBehavior.SnapshotAndReplace);
                    }
                    else
                    {
                        // Crossfade the background near the end of the cover zoom to hide recropping.
                        var coverFade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                        coverFade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        coverFade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185))));
                        coverFade.KeyFrames.Add(new EasingDoubleKeyFrame(0.88, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270)), new SineEase { EasingMode = EasingMode.EaseOut }));
                        coverFade.KeyFrames.Add(new EasingDoubleKeyFrame(0.34, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(390)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                        coverFade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(515)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                        cover.BeginAnimation(UIElement.OpacityProperty, coverFade, HandoffBehavior.SnapshotAndReplace);

                        if (background != null)
                        {
                            // No background opacity animation here: it is already fully visible behind the cover.
                            // Only the cover opacity and white exposure wash animate the transition.
                            background.BeginAnimation(UIElement.OpacityProperty, null);
                            background.Opacity = 1;
                        }

                        // Stronger exposure bridge than V18: it hides the cover stretching during the
                        // first half of the zoom, then masks the late cover -> background handoff.
                        var colorWashFade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                        colorWashFade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        colorWashFade.KeyFrames.Add(new EasingDoubleKeyFrame(0.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60)), new SineEase { EasingMode = EasingMode.EaseOut }));
                        colorWashFade.KeyFrames.Add(new EasingDoubleKeyFrame(0.36, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(155)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                        colorWashFade.KeyFrames.Add(new EasingDoubleKeyFrame(0.48, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270)), new SineEase { EasingMode = EasingMode.EaseOut }));
                        colorWashFade.KeyFrames.Add(new LinearDoubleKeyFrame(0.48, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(355))));
                        colorWashFade.KeyFrames.Add(new EasingDoubleKeyFrame(0.24, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                        colorWashFade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(660)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                        silver.BeginAnimation(UIElement.OpacityProperty, colorWashFade, HandoffBehavior.SnapshotAndReplace);

                        // Let the bright handoff finish first, then re-introduce the normal shade for the logo.
                        if (shade != null)
                        {
                            var backgroundShade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                            backgroundShade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                            backgroundShade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
                            backgroundShade.KeyFrames.Add(new EasingDoubleKeyFrame(0.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(680)), new SineEase { EasingMode = EasingMode.EaseOut }));
                            backgroundShade.KeyFrames.Add(new EasingDoubleKeyFrame(0.26, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), new SineEase { EasingMode = EasingMode.EaseOut }));
                            shade.BeginAnimation(UIElement.OpacityProperty, backgroundShade, HandoffBehavior.SnapshotAndReplace);
                        }
                    }
                }

                if (hasVideo || hasBackground || hasLogo)
                {
                    focusedCoverOverlayMediaDelayTimer.Stop();
                    focusedCoverOverlayMediaDelayTimer.Start();
                }

                DebugLog($"[AnikiHelper][FocusedCoverOverlay] Opened global overlay for '{game.Name}' at ({finalLeft:0},{finalTop:0}) {finalWidth:0}x{finalHeight:0}.");
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverOverlay] Failed to reveal overlay.");
                HideFocusedCoverOverlayVisual();
            }
        }

        private void OnFocusedCoverOverlayMediaDelayElapsed(object sender, EventArgs e)
        {
            focusedCoverOverlayMediaDelayTimer.Stop();

            try
            {
                if (!focusedCoverOverlayEnabled || focusedCoverOverlayActiveGameId == Guid.Empty)
                {
                    return;
                }

                var selectedGame = playniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (selectedGame == null || selectedGame.Id != focusedCoverOverlayActiveGameId)
                {
                    return;
                }

                var window = focusedCoverOverlayHookedWindow ?? ResolveFocusedCoverOverlayWindow();
                if (window == null || !window.IsVisible)
                {
                    return;
                }

                if (!EnsureFocusedCoverOverlayVisualCache(window))
                {
                    return;
                }

                if (focusedCoverOverlayGameList == null || !focusedCoverOverlayGameList.IsKeyboardFocusWithin)
                {
                    CancelFocusedCoverOverlay(false);
                    return;
                }

                var background = focusedCoverOverlayBackground;
                var media = focusedCoverOverlayMedia;
                var logo = focusedCoverOverlayLogo;
                var shade = focusedCoverOverlayShade;

                var hasVideo = focusedCoverOverlayTrailerMode && IsExistingLocalFile(focusedCoverOverlayVideoPath);
                var hasBackground = !hasVideo && IsExistingLocalFile(focusedCoverOverlayBackgroundPath);

                if (hasVideo)
                {
                    background.Source = null;
                    background.Opacity = 0;
                    background.Visibility = Visibility.Collapsed;

                    focusedCoverOverlayMediaRequestGameId = focusedCoverOverlayActiveGameId;
                    media.BeginAnimation(UIElement.OpacityProperty, null);
                    media.Opacity = 0;
                    media.Source = new Uri(focusedCoverOverlayVideoPath, UriKind.Absolute);
                    media.Visibility = Visibility.Visible;
                    // The actual cover/silver -> video crossfade starts from MediaOpened.
                }
                else if (hasBackground)
                {
                    // The background was prepared before the zoom and its crossfade is already running.
                    // The media-delay timer remains useful for the logo, but must not restart the image fade.
                    media.Source = null;
                    media.Opacity = 0;
                    media.Visibility = Visibility.Collapsed;
                }

                if (hasVideo)
                {
                    // Keep the dark shade out of the cover -> trailer transition.
                    // It will fade in only after MediaOpened, once the white exposure wash is already releasing.
                    shade.BeginAnimation(UIElement.OpacityProperty, null);
                    shade.Opacity = 0;
                }
                else if (!hasBackground)
                {
                    // Logo-only fallback keeps the legacy shade behavior. Background previews already
                    // started their shade animation together with the cover/background crossfade.
                    shade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.26, TimeSpan.FromMilliseconds(360))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(110),
                        FillBehavior = FillBehavior.HoldEnd
                    }, HandoffBehavior.SnapshotAndReplace);
                }

                if (IsExistingLocalFile(focusedCoverOverlayLogoPath))
                {
                    logo.Source = LoadOverlayBitmap(focusedCoverOverlayLogoPath, 320);
                    logo.Visibility = Visibility.Visible;

                    var logoDelayMs = hasVideo ? 2100 : 1250;

                    logo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(560))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(logoDelayMs),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.HoldEnd
                    }, HandoffBehavior.SnapshotAndReplace);

                    var logoScale = new ScaleTransform(0.98, 0.98);
                    var logoTranslate = new TranslateTransform(0, 48);
                    var logoGroup = new TransformGroup();
                    logoGroup.Children.Add(logoScale);
                    logoGroup.Children.Add(logoTranslate);
                    logo.RenderTransform = logoGroup;

                    logoTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(48, 0, TimeSpan.FromMilliseconds(650))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(logoDelayMs),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.HoldEnd
                    }, HandoffBehavior.SnapshotAndReplace);

                    var logoScaleAnimation = new DoubleAnimation(0.98, 1.0, TimeSpan.FromMilliseconds(650))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(logoDelayMs),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.HoldEnd
                    };
                    logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, logoScaleAnimation, HandoffBehavior.SnapshotAndReplace);
                    logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, logoScaleAnimation.Clone(), HandoffBehavior.SnapshotAndReplace);
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverOverlay] Failed to load delayed media.");
            }
        }


        private void OnFocusedCoverOverlayMediaOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                var media = sender as MediaElement;
                if (media == null ||
                    media != focusedCoverOverlayMedia ||
                    focusedCoverOverlayMediaRequestGameId == Guid.Empty ||
                    focusedCoverOverlayMediaRequestGameId != focusedCoverOverlayActiveGameId)
                {
                    return;
                }

                var selectedGame = playniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (selectedGame == null || selectedGame.Id != focusedCoverOverlayActiveGameId)
                {
                    return;
                }

                if (focusedCoverOverlayGameList == null || !focusedCoverOverlayGameList.IsKeyboardFocusWithin)
                {
                    CancelFocusedCoverOverlay(false);
                    return;
                }

                var cover = focusedCoverOverlayCover;
                var silver = focusedCoverOverlaySilver;
                var shade = focusedCoverOverlayShade;
                if (cover == null || silver == null)
                {
                    return;
                }

                var currentCoverOpacity = cover.Opacity;
                var currentSilverOpacity = silver.Opacity;

                cover.BeginAnimation(UIElement.OpacityProperty, null);
                cover.Opacity = currentCoverOpacity;
                silver.BeginAnimation(UIElement.OpacityProperty, null);
                silver.Opacity = currentSilverOpacity;

                // MediaOpened can fire a little before the first decoded frame is painted.
                // Give the MediaElement a short safety window, then crossfade underneath the light veil.
                var mediaReveal = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                mediaReveal.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                mediaReveal.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75))));
                mediaReveal.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                media.BeginAnimation(UIElement.OpacityProperty, mediaReveal, HandoffBehavior.SnapshotAndReplace);

                // The cover starts leaving only after the trailer has begun appearing behind it.
                // The overlap removes the "blank panel" risk while keeping the transition fluid.
                var coverRelease = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                coverRelease.KeyFrames.Add(new LinearDoubleKeyFrame(currentCoverOpacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                coverRelease.KeyFrames.Add(new LinearDoubleKeyFrame(currentCoverOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(95))));
                coverRelease.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(390)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                cover.BeginAnimation(UIElement.OpacityProperty, coverRelease, HandoffBehavior.SnapshotAndReplace);

                // Small exposure bloom at the exact handoff, then a long soft release revealing the trailer.
                // This is the visible "premium" bridge, not a full white flash.
                var transitionPeak = Math.Max(currentSilverOpacity, 0.44);
                var colorWashBridge = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                colorWashBridge.KeyFrames.Add(new LinearDoubleKeyFrame(currentSilverOpacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                colorWashBridge.KeyFrames.Add(new EasingDoubleKeyFrame(transitionPeak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), new SineEase { EasingMode = EasingMode.EaseOut }));
                colorWashBridge.KeyFrames.Add(new LinearDoubleKeyFrame(transitionPeak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(175))));
                colorWashBridge.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(540)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                silver.BeginAnimation(UIElement.OpacityProperty, colorWashBridge, HandoffBehavior.SnapshotAndReplace);

                // Once the image handoff is established, restore the normal cinematic shade for logo readability.
                if (shade != null)
                {
                    shade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.26, TimeSpan.FromMilliseconds(380))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(260),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.HoldEnd
                    }, HandoffBehavior.SnapshotAndReplace);
                }

            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverOverlay] Failed to start MediaOpened crossfade.");
            }
        }

        private void OnFocusedCoverOverlayMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            try
            {
                var media = sender as MediaElement;
                if (media == null ||
                    media != focusedCoverOverlayMedia ||
                    !focusedCoverOverlayTrailerMode ||
                    focusedCoverOverlayMediaRequestGameId == Guid.Empty ||
                    focusedCoverOverlayMediaRequestGameId != focusedCoverOverlayActiveGameId)
                {
                    return;
                }

                focusedCoverOverlayMediaRequestGameId = Guid.Empty;
                try { media.Source = null; } catch { }
                media.Opacity = 0;
                media.Visibility = Visibility.Collapsed;

                var background = focusedCoverOverlayBackground;
                var cover = focusedCoverOverlayCover;
                var silver = focusedCoverOverlaySilver;

                if (background == null || cover == null || silver == null)
                {
                    return;
                }

                var fallbackPath = ResolveGameBackgroundPath(playniteApi?.MainView?.SelectedGames?.FirstOrDefault());
                if (!IsExistingLocalFile(fallbackPath))
                {
                    return;
                }

                background.Source = LoadOverlayBitmap(fallbackPath, 720);
                background.Visibility = Visibility.Visible;
                background.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(480))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

                var currentCoverOpacity = cover.Opacity;
                var currentSilverOpacity = silver.Opacity;
                cover.BeginAnimation(UIElement.OpacityProperty, null);
                cover.Opacity = currentCoverOpacity;
                silver.BeginAnimation(UIElement.OpacityProperty, null);
                silver.Opacity = currentSilverOpacity;

                cover.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(currentCoverOpacity, 0, TimeSpan.FromMilliseconds(380))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

                silver.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(Math.Max(currentSilverOpacity, 0.16), 0, TimeSpan.FromMilliseconds(460))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);

            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][FocusedCoverOverlay] Media failed and fallback could not be revealed.");
            }
        }

        private void ConfigureMainMediaCardFallback(Dictionary<string, object> optionValues)
        {
            try
            {
                mainMediaCardFallbackEnabled = optionValues != null &&
                                               optionValues.TryGetValue("MediaCardOnMainView", out var rawEnabled) &&
                                               ToBool(rawEnabled);

                if (!mainMediaCardFallbackEnabled)
                {
                    mainMediaCardFallbackDelayTimer?.Stop();
                    pendingMainMediaCardFallbackGame = null;
                    ClearMainMediaCardFallbackImage();
                    return;
                }

                ScheduleMainMediaCardFallback(playniteApi?.MainView?.SelectedGames?.FirstOrDefault());
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][MainMediaCard] Failed to configure background fallback.");
            }
        }

        private void ScheduleMainMediaCardFallback(Game game)
        {
            try
            {
                mainMediaCardFallbackDelayTimer?.Stop();
                pendingMainMediaCardFallbackGame = game;

                // Clear the previous game's bitmap immediately. The black parent Border remains
                // visible until the new selection has been stable for 350 ms.
                ClearMainMediaCardFallbackImage();

                if (!mainMediaCardFallbackEnabled ||
                    game == null ||
                    playniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return;
                }

                mainMediaCardFallbackDelayTimer?.Start();
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][MainMediaCard] Failed to schedule background fallback.");
            }
        }

        private void OnMainMediaCardFallbackDelayElapsed(object sender, EventArgs e)
        {
            mainMediaCardFallbackDelayTimer?.Stop();

            try
            {
                if (!mainMediaCardFallbackEnabled ||
                    playniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    ClearMainMediaCardFallbackImage();
                    return;
                }

                var game = pendingMainMediaCardFallbackGame;
                var selectedGame = playniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (game == null || selectedGame == null || selectedGame.Id != game.Id)
                {
                    return;
                }

                var image = ResolveMainMediaCardFallbackImage();
                if (image == null)
                {
                    return;
                }

                var backgroundPath = ResolveGameBackgroundPath(game);
                var bitmap = LoadOverlayBitmap(backgroundPath, 720);
                image.Source = bitmap;

                DebugLog(bitmap != null
                    ? $"[AnikiHelper][MainMediaCard] Loaded fallback background for '{game.Name}'."
                    : $"[AnikiHelper][MainMediaCard] No local fallback background for '{game.Name}'.");
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][MainMediaCard] Failed to load background fallback.");
                ClearMainMediaCardFallbackImage();
            }
        }

        private Image ResolveMainMediaCardFallbackImage()
        {
            try
            {
                if (mainMediaCardFallbackImage != null && mainMediaCardFallbackImage.IsLoaded)
                {
                    return mainMediaCardFallbackImage;
                }

                mainMediaCardFallbackImage = null;

                var app = Application.Current;
                if (app == null)
                {
                    return null;
                }

                var mainWindow = app.MainWindow;
                var image = FindVisualChildByName<Image>(mainWindow, "TrailerCardFallbackImage");
                if (image != null)
                {
                    mainMediaCardFallbackImage = image;
                    return image;
                }

                foreach (Window candidate in app.Windows)
                {
                    if (candidate == null || ReferenceEquals(candidate, mainWindow) || !candidate.IsLoaded)
                    {
                        continue;
                    }

                    image = FindVisualChildByName<Image>(candidate, "TrailerCardFallbackImage");
                    if (image != null)
                    {
                        mainMediaCardFallbackImage = image;
                        return image;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][MainMediaCard] Failed to resolve fallback image.");
            }

            return null;
        }

        private void ClearMainMediaCardFallbackImage()
        {
            try
            {
                // Never scan the visual tree just to clear the card during navigation.
                // The image is resolved lazily only after the 350 ms settle delay.
                var image = mainMediaCardFallbackImage;
                if (image != null && image.IsLoaded)
                {
                    image.Source = null;
                }
                else
                {
                    mainMediaCardFallbackImage = null;
                }
            }
            catch
            {
                mainMediaCardFallbackImage = null;
            }
        }

        private string ResolveGameBackgroundPath(Game game)
        {
            try
            {
                if (game == null || string.IsNullOrWhiteSpace(game.BackgroundImage))
                {
                    return null;
                }

                if (Uri.TryCreate(game.BackgroundImage, UriKind.Absolute, out var remoteUri) &&
                    (remoteUri.Scheme == Uri.UriSchemeHttp || remoteUri.Scheme == Uri.UriSchemeHttps))
                {
                    return null;
                }

                return playniteApi?.Database?.GetFullFilePath(game.BackgroundImage);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsExistingLocalFile(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static BitmapImage LoadOverlayBitmap(string path, int decodePixelWidth)
        {
            if (!IsExistingLocalFile(path))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodePixelWidth;
                }
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                {
                    bitmap.Freeze();
                }
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void ClearFocusedCoverOverlayVisualCache()
        {
            UnhookFocusedCoverOverlayLayoutEvents();
            if (focusedCoverOverlayEventMedia != null)
            {
                try
                {
                    focusedCoverOverlayEventMedia.MediaOpened -= OnFocusedCoverOverlayMediaOpened;
                    focusedCoverOverlayEventMedia.MediaFailed -= OnFocusedCoverOverlayMediaFailed;
                }
                catch { }
            }
            focusedCoverOverlayEventMedia = null;
            focusedCoverOverlayMediaRequestGameId = Guid.Empty;
            focusedCoverOverlayHost = null;
            focusedCoverOverlayGameList = null;
            focusedCoverOverlayLayer = null;
            focusedCoverOverlayPanel = null;
            focusedCoverOverlayCover = null;
            focusedCoverOverlaySilver = null;
            focusedCoverOverlayShade = null;
            focusedCoverOverlayBackground = null;
            focusedCoverOverlayMedia = null;
            focusedCoverOverlayLogo = null;
            focusedCoverOverlayEdge = null;
            focusedCoverOverlaySweep = null;
        }

        private bool EnsureFocusedCoverOverlayVisualCache(Window window)
        {
            if (window == null)
            {
                return false;
            }

            if (focusedCoverOverlayHost != null &&
                focusedCoverOverlayGameList != null &&
                focusedCoverOverlayLayer != null &&
                focusedCoverOverlayPanel != null &&
                focusedCoverOverlayCover != null &&
                focusedCoverOverlaySilver != null &&
                focusedCoverOverlayShade != null &&
                focusedCoverOverlayBackground != null &&
                focusedCoverOverlayMedia != null &&
                focusedCoverOverlayLogo != null &&
                focusedCoverOverlayEdge != null &&
                focusedCoverOverlaySweep != null &&
                focusedCoverOverlayPanel.IsLoaded)
            {
                return true;
            }

            focusedCoverOverlayHost = FindVisualChildByName<Grid>(window, "PART_MainHost");
            focusedCoverOverlayGameList = FindVisualChildByName<ListBox>(window, "PART_ListGameItems");
            focusedCoverOverlayLayer = FindVisualChildByName<Canvas>(window, "AnikiFocusedCoverOverlayLayer");
            focusedCoverOverlayPanel = FindVisualChildByName<Grid>(window, "AnikiFocusedCoverOverlayPanel");
            focusedCoverOverlayCover = FindVisualChildByName<Border>(window, "AnikiFocusedCoverOverlayCover");
            focusedCoverOverlaySilver = FindVisualChildByName<Border>(window, "AnikiFocusedCoverOverlaySilver");
            focusedCoverOverlayShade = FindVisualChildByName<Border>(window, "AnikiFocusedCoverOverlayShade");
            focusedCoverOverlayBackground = FindVisualChildByName<Image>(window, "AnikiFocusedCoverOverlayBackground");
            focusedCoverOverlayMedia = FindVisualChildByName<MediaElement>(window, "AnikiFocusedCoverOverlayMedia");
            if (focusedCoverOverlayEventMedia != focusedCoverOverlayMedia)
            {
                if (focusedCoverOverlayEventMedia != null)
                {
                    try
                    {
                        focusedCoverOverlayEventMedia.MediaOpened -= OnFocusedCoverOverlayMediaOpened;
                        focusedCoverOverlayEventMedia.MediaFailed -= OnFocusedCoverOverlayMediaFailed;
                    }
                    catch { }
                }

                focusedCoverOverlayEventMedia = focusedCoverOverlayMedia;
                if (focusedCoverOverlayEventMedia != null)
                {
                    focusedCoverOverlayEventMedia.MediaOpened += OnFocusedCoverOverlayMediaOpened;
                    focusedCoverOverlayEventMedia.MediaFailed += OnFocusedCoverOverlayMediaFailed;
                }
            }
            focusedCoverOverlayLogo = FindVisualChildByName<Image>(window, "AnikiFocusedCoverOverlayLogo");
            focusedCoverOverlayEdge = FindVisualChildByName<Border>(window, "AnikiFocusedCoverOverlayEdge");
            focusedCoverOverlaySweep = FindVisualChildByName<Border>(window, "AnikiFocusedCoverOverlaySweep");
            HookFocusedCoverOverlayLayoutEvents(window);

            return focusedCoverOverlayHost != null &&
                   focusedCoverOverlayGameList != null &&
                   focusedCoverOverlayLayer != null &&
                   focusedCoverOverlayPanel != null &&
                   focusedCoverOverlayCover != null &&
                   focusedCoverOverlaySilver != null &&
                   focusedCoverOverlayShade != null &&
                   focusedCoverOverlayBackground != null &&
                   focusedCoverOverlayMedia != null &&
                   focusedCoverOverlayLogo != null &&
                   focusedCoverOverlayEdge != null &&
                   focusedCoverOverlaySweep != null;
        }


        private void ClearFocusedCoverOverlayAnimations(Window window)
        {
            if (window == null || !EnsureFocusedCoverOverlayVisualCache(window))
            {
                return;
            }

            try
            {
                var panel = focusedCoverOverlayPanel;
                var cover = focusedCoverOverlayCover;
                var silver = focusedCoverOverlaySilver;
                var background = focusedCoverOverlayBackground;
                var media = focusedCoverOverlayMedia;
                var logo = focusedCoverOverlayLogo;
                var shade = focusedCoverOverlayShade;
                var edge = focusedCoverOverlayEdge;
                var sweep = focusedCoverOverlaySweep;

                panel.BeginAnimation(UIElement.OpacityProperty, null);
                panel.BeginAnimation(FrameworkElement.WidthProperty, null);
                panel.BeginAnimation(FrameworkElement.HeightProperty, null);
                if (panel.Clip is RectangleGeometry panelClip)
                {
                    panelClip.BeginAnimation(RectangleGeometry.RectProperty, null);
                }
                panel.Clip = null;
                cover.BeginAnimation(UIElement.OpacityProperty, null);
                silver.BeginAnimation(UIElement.OpacityProperty, null);
                background.BeginAnimation(UIElement.OpacityProperty, null);
                media.BeginAnimation(UIElement.OpacityProperty, null);
                logo.BeginAnimation(UIElement.OpacityProperty, null);
                shade.BeginAnimation(UIElement.OpacityProperty, null);
                edge?.BeginAnimation(UIElement.OpacityProperty, null);
                sweep?.BeginAnimation(UIElement.OpacityProperty, null);

                var sweepTranslate = sweep?.RenderTransform as TranslateTransform;
                sweepTranslate?.BeginAnimation(TranslateTransform.XProperty, null);

                var panelGroup = panel.RenderTransform as TransformGroup;
                var scale = panelGroup?.Children.OfType<ScaleTransform>().FirstOrDefault();
                var translate = panelGroup?.Children.OfType<TranslateTransform>().FirstOrDefault();
                scale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                translate?.BeginAnimation(TranslateTransform.XProperty, null);
                translate?.BeginAnimation(TranslateTransform.YProperty, null);

                var logoGroup = logo.RenderTransform as TransformGroup;
                var logoTranslate = logoGroup?.Children.OfType<TranslateTransform>().FirstOrDefault();
                var logoScale = logoGroup?.Children.OfType<ScaleTransform>().FirstOrDefault();
                logoTranslate?.BeginAnimation(TranslateTransform.YProperty, null);
                logoScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                logoScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            catch
            {
            }
        }

        private void HideFocusedCoverOverlayVisual()
        {
            try
            {
                // Never resolve/search the visual tree from Hide(). This method is called on
                // every selection change, so it must be effectively free until an overlay has
                // actually been opened and cached.
                var window = focusedCoverOverlayHookedWindow;
                if (window == null ||
                    focusedCoverOverlayLayer == null ||
                    focusedCoverOverlayPanel == null)
                {
                    return;
                }

                ClearFocusedCoverOverlayAnimations(window);

                var layer = focusedCoverOverlayLayer;
                var panel = focusedCoverOverlayPanel;
                var cover = focusedCoverOverlayCover;
                var silver = focusedCoverOverlaySilver;
                var background = focusedCoverOverlayBackground;
                var media = focusedCoverOverlayMedia;
                var logo = focusedCoverOverlayLogo;
                var shade = focusedCoverOverlayShade;
                var edge = focusedCoverOverlayEdge;
                var sweep = focusedCoverOverlaySweep;
                focusedCoverOverlayMediaRequestGameId = Guid.Empty;

                if (media != null)
                {
                    try { media.Source = null; } catch { }
                    media.Opacity = 0;
                    media.Visibility = Visibility.Collapsed;
                }

                if (background != null)
                {
                    background.Source = null;
                    background.Opacity = 0;
                    background.Visibility = Visibility.Collapsed;
                }

                if (logo != null)
                {
                    logo.Source = null;
                    logo.Opacity = 0;
                    logo.Visibility = Visibility.Collapsed;
                }

                if (cover != null)
                {
                    cover.Background = Brushes.Black;
                    cover.Opacity = 1;
                }


                if (silver != null) silver.Opacity = 0;
                if (shade != null) shade.Opacity = 0;
                if (edge != null) edge.Opacity = 0;
                if (sweep != null) sweep.Opacity = 0;
                if (panel != null)
                {
                    panel.Opacity = 0;
                    panel.Visibility = Visibility.Collapsed;
                }
                if (layer != null) layer.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private bool IsTopBarShortcutEnabled(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            // The persisted theme value is the source of truth. The popup never writes
            // directly into Options; Options is refreshed only after a successful change.
            if (settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue) &&
                bool.TryParse(storedValue, out var storedBool))
            {
                return storedBool;
            }

            if (settings.Options.TryGetValue(key, out var optionValue) && optionValue is bool optionBool)
            {
                return optionBool;
            }

            return false;
        }

        private int GetEnabledTopBarShortcutCount(string exceptKey = null)
        {
            var count = 0;

            foreach (var optionKey in TopBarShortcutOptionKeys)
            {
                if (!string.IsNullOrWhiteSpace(exceptKey) &&
                    string.Equals(optionKey, exceptKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsTopBarShortcutEnabled(optionKey))
                {
                    count++;
                }
            }

            return count;
        }

        private bool CanEnableTopBarShortcut(string key)
        {
            if (!TopBarShortcutOptionKeys.Contains(key) || IsTopBarShortcutEnabled(key))
            {
                return true;
            }

            return GetEnabledTopBarShortcutCount(key) < TopBarShortcutLimit;
        }

        public void SetOptionValue(string key, object value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                var finalValue = value?.ToString() ?? string.Empty;

                if (TopBarShortcutOptionKeys.Contains(key) &&
                    bool.TryParse(finalValue, out var requestedTopBarState) &&
                    requestedTopBarState &&
                    !CanEnableTopBarShortcut(key))
                {
                    logger?.Info($"[AnikiHelper] Top bar shortcut limit reached; ignoring enable request for {key}.");
                    return;
                }

                if (settings.AnikiThemeSettingsValues.TryGetValue(key, out var currentValue) &&
                    string.Equals(currentValue, finalValue, StringComparison.Ordinal))
                {
                    return;
                }

                if (DoesVariableNeedRestart(key))
                {
                    MarkRestartRequired();
                }

                settings.AnikiThemeSettingsValues[key] = finalValue;

                if (string.Equals(key, ThemeColorFilterVariableId, StringComparison.OrdinalIgnoreCase))
                {
                    // A deliberate category change starts a fresh Random Color session when
                    // the user enters Random again, while startup keeps one pick for the session.
                    activeRandomThemeColorCandidate = null;

                    // Dynamic contains a single preset. Selecting the category is equivalent
                    // to selecting that preset directly, including its restart requirement.
                    if (string.Equals(finalValue, "Dynamic", StringComparison.OrdinalIgnoreCase))
                    {
                        MarkRestartRequired();
                    }
                }

                if (string.Equals(key, VisualPackFilterVariableId, StringComparison.OrdinalIgnoreCase))
                {
                    // Same behavior for Random Visual Pack: one stable pick per session,
                    // but entering Random manually generates a fresh session pick.
                    activeRandomVisualPackCandidate = null;
                }

                ApplyExclusiveMainViewInfoOptions(key, finalValue);

                // Changing a preset filter (for example VisualPackType) must only refresh
                // the choices shown in the dependent ComboBox. It must not silently
                // select/apply the first preset of the newly selected category.
                RefreshPresetFiltersForVariable(key, false);
                ApplyOptionDependenciesToStorage();

                SaveSettings();

                Apply();

                if (string.Equals(key, VisualPackFilterVariableId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(finalValue, CustomVisualPackFilterValue, StringComparison.OrdinalIgnoreCase))
                {
                    customVisualPackLibrarySnapshot = RefreshCustomVisualPackLibrarySnapshot();
                    RefreshCustomVisualPackRuntimeImages();
                    global::AnikiHelper.VisualPackBackgroundComposer.RefreshNow();
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to set Aniki theme option: {key}");
            }
        }

        private void ApplyExclusiveMainViewInfoOptions(string changedKey, string finalValue)
        {
            if (!bool.TryParse(finalValue, out var enabled) || !enabled)
            {
                return;
            }

            // Legacy themes used separate booleans for the Main View information bars.
            // Newer themes use MainViewBottomBar, so don't create obsolete storage keys.
            if (currentFile?.Variables == null || !currentFile.Variables.ContainsKey("MainViewBottomBar"))
            {
                if (string.Equals(changedKey, "ControllerShortcutBar", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AnikiThemeSettingsValues["CompactGameInfoBar"] = false.ToString();
                }
                else if (string.Equals(changedKey, "CompactGameInfoBar", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AnikiThemeSettingsValues["ControllerShortcutBar"] = false.ToString();
                    settings.AnikiThemeSettingsValues["DetailedSideInfoPanel"] = false.ToString();
                }
                else if (string.Equals(changedKey, "DetailedSideInfoPanel", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AnikiThemeSettingsValues["CompactGameInfoBar"] = false.ToString();
                }
            }

            // Legacy themes also used two mutually-exclusive focused-cover booleans.
            if (currentFile?.Variables == null || !currentFile.Variables.ContainsKey("FocusedCoverPreview"))
            {
                if (string.Equals(changedKey, "MicroTrailerOnFocusedCover", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AnikiThemeSettingsValues["BackgroundOnFocusedCover"] = false.ToString();
                }
                else if (string.Equals(changedKey, "BackgroundOnFocusedCover", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AnikiThemeSettingsValues["MicroTrailerOnFocusedCover"] = false.ToString();
                }
            }
        }

        private bool ApplyOptionDependenciesToStorage()
        {
            var changed = false;

            if (currentFile?.Variables == null || settings?.AnikiThemeSettingsValues == null)
            {
                return false;
            }

            // Migration/safety for legacy themes that still expose both focused-cover booleans.
            if (!currentFile.Variables.ContainsKey("FocusedCoverPreview") &&
                IsBooleanThemeOptionEnabled("MicroTrailerOnFocusedCover") &&
                IsBooleanThemeOptionEnabled("BackgroundOnFocusedCover"))
            {
                settings.AnikiThemeSettingsValues["BackgroundOnFocusedCover"] = false.ToString();
                changed = true;
            }

            foreach (var pair in currentFile.Variables)
            {
                var key = pair.Key;
                var variable = pair.Value;

                if (string.IsNullOrWhiteSpace(key) || variable == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(variable.DependsOn) &&
                    string.IsNullOrWhiteSpace(variable.DependsOn2))
                {
                    variable.SetIsEnabledSilently(true);
                    continue;
                }

                var dependencySatisfied = IsThemeOptionDependencySatisfied(variable);
                variable.SetIsEnabledSilently(dependencySatisfied);

                if (!dependencySatisfied && variable.AutoDisableWhenDependencyMissing && IsBooleanThemeOptionEnabled(key))
                {
                    settings.AnikiThemeSettingsValues[key] = false.ToString();
                    changed = true;
                }
            }

            return changed;
        }

        private bool IsThemeOptionDependencySatisfied(AnikiThemeVariable variable)
        {
            if (variable == null)
            {
                return true;
            }

            return IsSingleThemeOptionDependencySatisfied(
                       variable.DependsOn,
                       variable.DependsOnValue,
                       variable.DependsOnNotValue) &&
                   IsSingleThemeOptionDependencySatisfied(
                       variable.DependsOn2,
                       variable.DependsOn2Value,
                       variable.DependsOn2NotValue);
        }

        private bool IsSingleThemeOptionDependencySatisfied(string dependencyId, object expectedValue, object notExpectedValue)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return true;
            }

            if (currentFile?.Variables != null &&
                currentFile.Variables.TryGetValue(dependencyId, out var dependency) &&
                dependency != null)
            {
                var dependencyType = (dependency.Type ?? string.Empty).Trim().ToLowerInvariant();
                var actualValue = GetStoredValueOrDefault(dependencyId, dependency);

                if (notExpectedValue != null)
                {
                    if (dependencyType == "boolean" || dependencyType == "bool")
                    {
                        return ToBool(actualValue) != ToBool(notExpectedValue);
                    }

                    return !string.Equals(
                        actualValue ?? string.Empty,
                        notExpectedValue.ToString() ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (dependencyType == "boolean" || dependencyType == "bool")
                {
                    var expectedBoolean = expectedValue == null ? true : ToBool(expectedValue);
                    return ToBool(actualValue) == expectedBoolean;
                }

                var expectedText = expectedValue?.ToString() ?? string.Empty;
                return string.Equals(actualValue ?? string.Empty, expectedText, StringComparison.OrdinalIgnoreCase);
            }

            if (currentFile?.Presets != null &&
                currentFile.Presets.TryGetValue(dependencyId, out var presetGroup) &&
                presetGroup != null)
            {
                var actualPreset = GetSelectedPreset(dependencyId, presetGroup)?.Key ?? string.Empty;

                if (notExpectedValue != null)
                {
                    return !string.Equals(
                        actualPreset,
                        notExpectedValue.ToString() ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
                }

                return string.Equals(
                    actualPreset,
                    expectedValue?.ToString() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }

            // Legacy fallback for dependencies targeting a raw boolean option.
            if (notExpectedValue != null)
            {
                return IsBooleanThemeOptionEnabled(dependencyId) != ToBool(notExpectedValue);
            }

            var fallbackExpectedValue = expectedValue == null ? true : ToBool(expectedValue);
            return IsBooleanThemeOptionEnabled(dependencyId) == fallbackExpectedValue;
        }

        private bool IsBooleanThemeOptionEnabled(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (settings?.AnikiThemeSettingsValues != null &&
                settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue))
            {
                return ToBool(storedValue);
            }

            if (currentFile?.Variables != null &&
                currentFile.Variables.TryGetValue(key, out var variable))
            {
                return ToBool(variable?.EffectiveValue);
            }

            if (settings?.Options != null && settings.Options.TryGetValue(key, out var optionValue))
            {
                return ToBool(optionValue);
            }

            return false;
        }

        public void ToggleOptionValue(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                var currentValue = false;

                if (settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue))
                {
                    bool.TryParse(storedValue, out currentValue);
                }
                else if (settings.Options.TryGetValue(key, out var value) && value is bool boolValue)
                {
                    currentValue = boolValue;
                }

                var finalValue = (!currentValue).ToString();

                if (!currentValue && TopBarShortcutOptionKeys.Contains(key) && !CanEnableTopBarShortcut(key))
                {
                    logger?.Info($"[AnikiHelper] Top bar shortcut limit reached; ignoring toggle request for {key}.");
                    return;
                }

                settings.AnikiThemeSettingsValues[key] = finalValue;

                ApplyExclusiveMainViewInfoOptions(key, finalValue);
                ApplyOptionDependenciesToStorage();

                SaveSettings();

                Apply();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to toggle Aniki theme option: {key}");
            }
        }

        public void SelectPreset(string groupId, string presetKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(presetKey))
                {
                    return;
                }

                if (TrySelectInstalledCompletePack(groupId, presetKey))
                {
                    return;
                }

                ClearActiveCompletePackForComponentSelection(groupId);

                if (TrySelectInstalledCustomVisualPack(groupId, presetKey))
                {
                    return;
                }

                if (TrySelectInstalledCustomColorPack(groupId, presetKey))
                {
                    return;
                }

                if (TrySelectInstalledLoginPack(groupId, presetKey))
                {
                    return;
                }

                if (TrySelectInstalledSoundPack(groupId, presetKey))
                {
                    return;
                }

                var loginSelectionGeneration = 0;
                if (string.Equals(groupId, "LoginBackground", StringComparison.OrdinalIgnoreCase))
                {
                    loginSelectionGeneration = ++loginBackgroundSelectionGeneration;
                }

                if (TryBeginLoginBackgroundDownload(groupId, presetKey, loginSelectionGeneration))
                {
                    return;
                }

                ApplyPresetSelectionCore(groupId, presetKey);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to select Aniki preset: {groupId}.{presetKey}");
            }
        }

        private bool TrySelectInstalledCustomVisualPack(string groupId, string presetKey)
        {
            if (!string.Equals(groupId, VisualPackPresetGroupId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetCustomVisualPackId(presetKey, out var packId))
            {
                return false;
            }

            try
            {
                // ComboBoxEx can raise both its own SelectionChanged event and the normal
                // TwoWay binding update. Do not apply the same pack twice.
                var libraryBeforeApply = customVisualPackLibrarySnapshot ?? visualPackImportService.GetLibrary();
                var alreadyActive = string.Equals(
                    libraryBeforeApply?.ActivePackId ?? string.Empty,
                    packId,
                    StringComparison.OrdinalIgnoreCase);

                if (!alreadyActive)
                {
                    // In Fullscreen, WPF keeps the current 153.Custom JPEG files open.
                    // Persist the library selection only; runtime ImageBrush resources are
                    // loaded directly from the selected pack below, so no locked theme file
                    // has to be overwritten while Playnite is running.
                    visualPackImportService.SetActivePack(packId);
                }

                settings.AnikiThemeSettingsValues[VisualPackFilterVariableId] = CustomVisualPackFilterValue;
                settings.AnikiThemeSettingsSelectedPresets[VisualPackPresetGroupId] = CustomVisualPackPresetKey;
                ApplyOptionDependenciesToStorage();
                SaveSettings();

                customVisualPackLibrarySnapshot = visualPackImportService.GetLibrary();

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(VisualPackPresetGroupId, out var visualPackGroup) &&
                    visualPackGroup != null)
                {
                    RefreshPresetGroupFilter(VisualPackPresetGroupId, visualPackGroup, false);
                }

                Apply();
                RefreshCustomVisualPackRuntimeImages(packId);
                global::AnikiHelper.VisualPackBackgroundComposer.RefreshNow();

                logger?.Info($"[AnikiHelper][VisualPack] Selected installed pack '{packId}' from Fullscreen theme settings.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][VisualPack] Failed to select installed pack '{packId}' from Fullscreen theme settings.");

                try
                {
                    customVisualPackLibrarySnapshot = visualPackImportService.GetLibrary();
                    if (currentFile?.Presets != null &&
                        currentFile.Presets.TryGetValue(VisualPackPresetGroupId, out var visualPackGroup) &&
                        visualPackGroup != null)
                    {
                        RefreshPresetGroupFilter(VisualPackPresetGroupId, visualPackGroup, false);
                    }
                }
                catch
                {
                }
            }

            return true;
        }

        private bool TryGetCustomVisualPackId(string key, out string packId)
        {
            packId = null;

            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(CustomVisualPackVirtualKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = key.Substring(CustomVisualPackVirtualKeyPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            packId = value;
            return true;
        }

        private string GetCustomVisualPackVirtualKey(string packId)
        {
            return string.IsNullOrWhiteSpace(packId)
                ? string.Empty
                : CustomVisualPackVirtualKeyPrefix + packId;
        }

        private VisualPackLibrarySnapshot RefreshCustomVisualPackLibrarySnapshot()
        {
            try
            {
                customVisualPackLibrarySnapshot = visualPackImportService.GetLibrary();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][VisualPack] Failed to refresh installed Visual Pack library for Fullscreen settings.");
                customVisualPackLibrarySnapshot = customVisualPackLibrarySnapshot ?? new VisualPackLibrarySnapshot();
            }

            return customVisualPackLibrarySnapshot ?? new VisualPackLibrarySnapshot();
        }

        public void RefreshInstalledCustomVisualPacks()
        {
            try
            {
                customVisualPackLibrarySnapshot = RefreshCustomVisualPackLibrarySnapshot();

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(VisualPackPresetGroupId, out var visualPackGroup) &&
                    visualPackGroup != null)
                {
                    RefreshPresetGroupFilter(VisualPackPresetGroupId, visualPackGroup, false);
                }

                if (IsCustomVisualPackFilterActive())
                {
                    RefreshCustomVisualPackRuntimeImages();
                    global::AnikiHelper.VisualPackBackgroundComposer.RefreshNow();
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][VisualPack] Failed to refresh installed Custom Visual Packs.");
            }
        }

        private List<AnikiPresetItem> BuildInstalledCustomVisualPackItems(string groupId, VisualPackLibrarySnapshot snapshot)
        {
            var result = new List<AnikiPresetItem>();
            if (snapshot?.Packs == null)
            {
                return result;
            }

            foreach (var pack in snapshot.Packs)
            {
                if (pack == null || string.IsNullOrWhiteSpace(pack.Id))
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(pack.Name) ? pack.Id : pack.Name.Trim();

                result.Add(new AnikiPresetItem
                {
                    Id = groupId + "." + GetCustomVisualPackVirtualKey(pack.Id),
                    GroupId = groupId,
                    Key = GetCustomVisualPackVirtualKey(pack.Id),
                    Name = displayName,
                    LocalizedName = displayName,
                    VisualPackCategory = CustomVisualPackFilterValue
                });
            }

            return result;
        }

        private bool IsCustomVisualPackLibraryFilter(string groupId, string filterValue)
        {
            return string.Equals(groupId, VisualPackPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(filterValue, CustomVisualPackFilterValue, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsRandomVisualPackFilterActive()
        {
            try
            {
                if (currentFile?.Variables == null ||
                    !currentFile.Variables.TryGetValue(VisualPackFilterVariableId, out var variable) ||
                    variable == null)
                {
                    return false;
                }

                var value = GetStoredValueOrDefault(VisualPackFilterVariableId, variable) ?? string.Empty;
                return string.Equals(value, RandomVisualPackFilterValue, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void RefreshCustomVisualPackRuntimeImages(string packId = null)
        {
            try
            {
                if (Application.Current == null)
                {
                    return;
                }

                var snapshot = customVisualPackLibrarySnapshot ?? RefreshCustomVisualPackLibrarySnapshot();
                var selectedPackId = string.IsNullOrWhiteSpace(packId)
                    ? snapshot?.ActivePackId
                    : packId;

                string sourceFolder;
                string sourceLabel;

                if (string.IsNullOrWhiteSpace(selectedPackId))
                {
                    var themePath = !string.IsNullOrWhiteSpace(currentThemePath)
                        ? currentThemePath
                        : GetFullscreenThemePath();

                    sourceFolder = string.IsNullOrWhiteSpace(themePath)
                        ? string.Empty
                        : Path.Combine(themePath, "Themes Option", "2.Interface", "Images", "153.Custom");
                    sourceLabel = "theme fallback";
                }
                else
                {
                    // Load from Helper's persistent library, not from 153.Custom. WPF's original
                    // ThemeFile BitmapImages keep those theme JPEGs locked for the lifetime of the
                    // Fullscreen visual tree. Loading the selected library images fully into memory
                    // lets us swap DynamicResource brushes instantly without touching locked files.
                    sourceFolder = visualPackImportService.GetPackFolder(selectedPackId);
                    sourceLabel = "library pack '" + selectedPackId + "'";
                }

                if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
                {
                    return;
                }

                foreach (var pair in CustomVisualPackResourceFiles)
                {
                    var imagePath = Path.Combine(sourceFolder, pair.Value);
                    if (!File.Exists(imagePath))
                    {
                        continue;
                    }

                    BitmapSource bitmap;
                    using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        // BitmapFrame + OnLoad is intentionally used instead of BitmapImage
                        // with IgnoreImageCache. On .NET Framework, StreamSource combined with
                        // IgnoreImageCache can enter the URI image cache with a null key.
                        bitmap = BitmapFrame.Create(
                            stream,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                    }

                    if (bitmap.CanFreeze)
                    {
                        bitmap.Freeze();
                    }

                    var brush = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.UniformToFill
                    };

                    if (brush.CanFreeze)
                    {
                        brush.Freeze();
                    }

                    Application.Current.Resources[pair.Key] = brush;
                }

                logger?.Info($"[AnikiHelper][VisualPack] Runtime Custom resources loaded from {sourceLabel}.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][VisualPack] Failed to refresh Custom Visual Pack image resources.");
            }
        }

        private bool IsCustomVisualPackFilterActive()
        {
            try
            {
                if (currentFile?.Variables == null ||
                    !currentFile.Variables.TryGetValue(VisualPackFilterVariableId, out var variable) ||
                    variable == null)
                {
                    return false;
                }

                var value = GetStoredValueOrDefault(VisualPackFilterVariableId, variable) ?? string.Empty;
                return string.Equals(value, CustomVisualPackFilterValue, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool TrySelectInstalledCustomColorPack(string groupId, string presetKey)
        {
            if (!string.Equals(groupId, ThemeColorPresetGroupId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetCustomColorPackId(presetKey, out var localId))
            {
                return false;
            }

            try
            {
                var snapshot = customColorPackLibrarySnapshot ?? colorPackImportService.GetLibrary();
                if (!string.Equals(snapshot?.ActivePackId, localId, StringComparison.OrdinalIgnoreCase))
                {
                    colorPackImportService.SetActivePack(localId);
                }

                settings.AnikiThemeSettingsValues[ThemeColorFilterVariableId] = CustomColorPackFilterValue;
                settings.AnikiThemeSettingsSelectedPresets[ThemeColorPresetGroupId] = CustomColorPackPresetKey;
                ApplyOptionDependenciesToStorage();
                SaveSettings();

                customColorPackLibrarySnapshot = colorPackImportService.GetLibrary();
                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(ThemeColorPresetGroupId, out var themeColorGroup) &&
                    themeColorGroup != null)
                {
                    RefreshPresetGroupFilter(ThemeColorPresetGroupId, themeColorGroup, false);
                }

                Apply();
                logger?.Info($"[AnikiHelper][ColorPack] Selected installed pack '{localId}' from Fullscreen theme settings.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][ColorPack] Failed to select installed pack '{localId}' from Fullscreen theme settings.");

                try
                {
                    customColorPackLibrarySnapshot = colorPackImportService.GetLibrary();
                    if (currentFile?.Presets != null &&
                        currentFile.Presets.TryGetValue(ThemeColorPresetGroupId, out var themeColorGroup) &&
                        themeColorGroup != null)
                    {
                        RefreshPresetGroupFilter(ThemeColorPresetGroupId, themeColorGroup, false);
                    }
                }
                catch
                {
                }
            }

            return true;
        }

        private static bool TryGetCustomColorPackId(string key, out string localId)
        {
            localId = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(CustomColorPackVirtualKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = key.Substring(CustomColorPackVirtualKeyPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            localId = value;
            return true;
        }

        private static string GetCustomColorPackVirtualKey(string localId)
        {
            return string.IsNullOrWhiteSpace(localId)
                ? string.Empty
                : CustomColorPackVirtualKeyPrefix + localId;
        }

        private ColorPackLibrarySnapshot RefreshCustomColorPackLibrarySnapshot()
        {
            try
            {
                customColorPackLibrarySnapshot = colorPackImportService.GetLibrary();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ColorPack] Failed to refresh the installed Color Pack library for Fullscreen settings.");
                customColorPackLibrarySnapshot = customColorPackLibrarySnapshot ?? new ColorPackLibrarySnapshot();
            }

            return customColorPackLibrarySnapshot ?? new ColorPackLibrarySnapshot();
        }

        public void RefreshInstalledCustomColorPacks()
        {
            try
            {
                customColorPackLibrarySnapshot = RefreshCustomColorPackLibrarySnapshot();
                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(ThemeColorPresetGroupId, out var themeColorGroup) &&
                    themeColorGroup != null)
                {
                    RefreshPresetGroupFilter(ThemeColorPresetGroupId, themeColorGroup, false);
                }

                if (IsCustomColorPackFilterActive())
                {
                    Apply();
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ColorPack] Failed to refresh installed Custom Color Packs.");
            }
        }

        private static List<AnikiPresetItem> BuildInstalledCustomColorPackItems(
            string groupId,
            ColorPackLibrarySnapshot snapshot)
        {
            var result = new List<AnikiPresetItem>();
            if (snapshot?.Packs == null)
            {
                return result;
            }

            foreach (var pack in snapshot.Packs)
            {
                if (pack == null || string.IsNullOrWhiteSpace(pack.LocalId))
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(pack.Name) ? pack.LocalId : pack.Name.Trim();
                result.Add(new AnikiPresetItem
                {
                    Id = groupId + "." + GetCustomColorPackVirtualKey(pack.LocalId),
                    GroupId = groupId,
                    Key = GetCustomColorPackVirtualKey(pack.LocalId),
                    Name = displayName,
                    LocalizedName = displayName,
                    FilterValue = CustomColorPackFilterValue
                });
            }

            return result;
        }

        private static bool IsCustomColorPackLibraryFilter(string groupId, string filterValue)
        {
            return string.Equals(groupId, ThemeColorPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(filterValue, CustomColorPackFilterValue, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLoginPackLibraryFilter(string groupId, string filterValue)
        {
            return string.Equals(groupId, LoginBackgroundPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(filterValue, LoginPackFilterValue, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCustomColorPackFilterActive()
        {
            try
            {
                if (currentFile?.Variables == null ||
                    !currentFile.Variables.TryGetValue(ThemeColorFilterVariableId, out var variable) ||
                    variable == null)
                {
                    return false;
                }

                var value = GetStoredValueOrDefault(ThemeColorFilterVariableId, variable) ?? string.Empty;
                return string.Equals(value, CustomColorPackFilterValue, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsRandomThemeColorFilterActive()
        {
            try
            {
                if (currentFile?.Variables == null ||
                    !currentFile.Variables.TryGetValue(ThemeColorFilterVariableId, out var variable) ||
                    variable == null)
                {
                    return false;
                }

                var value = GetStoredValueOrDefault(ThemeColorFilterVariableId, variable) ?? string.Empty;
                return string.Equals(value, RandomThemeColorFilterValue, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void LoadSelectedCustomColorPack()
        {
            if (!IsCustomColorPackFilterActive() || Application.Current == null)
            {
                return;
            }

            try
            {
                var snapshot = customColorPackLibrarySnapshot ?? RefreshCustomColorPackLibrarySnapshot();
                var localId = snapshot?.ActivePackId;
                if (string.IsNullOrWhiteSpace(localId))
                {
                    return;
                }

                var dictionary = colorPackImportService.LoadResourceDictionary(localId);
                if (dictionary != null)
                {
                    // Defense in depth: imported files are normalized without this key,
                    // but runtime loading must never allow a Color Pack to select images.
                    dictionary.Remove("BackgroundImageIndex");
                    Application.Current.Resources.MergedDictionaries.Add(dictionary);
                    loadedDictionaries.Add(dictionary);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ColorPack] Failed to load the active Color Pack resources.");
            }
        }

        private void ApplyPresetSelectionCore(string groupId, string presetKey)
        {
            if (string.Equals(groupId, LoginBackgroundPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                !TryGetLoginPackId(presetKey, out _))
            {
                DeactivateLoginPackRuntime();
            }

            if (string.Equals(groupId, SoundPackPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                !TryGetSoundPackId(presetKey, out _))
            {
                DeactivateSoundPackRuntime();
            }

            if (string.Equals(groupId, CompletePackPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                !TryGetCompletePackId(presetKey, out _))
            {
                completePackImportService.ClearActivePack();
                completePackLibrarySnapshot = completePackImportService.GetLibrary();
            }

            if (settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var currentPreset) &&
                string.Equals(currentPreset, presetKey, StringComparison.OrdinalIgnoreCase))
            {
                if (SyncPresetFilterFromSelection(groupId, presetKey))
                {
                    SaveSettings();
                    Apply();
                }

                return;
            }

            if (DoesPresetNeedRestart(groupId, presetKey))
            {
                MarkRestartRequired();
            }

            settings.AnikiThemeSettingsSelectedPresets[groupId] = presetKey;
            SyncPresetFilterFromSelection(groupId, presetKey);
            ApplyOptionDependenciesToStorage();
            SaveSettings();

            Apply();
        }

        private bool TryBeginLoginBackgroundDownload(string groupId, string presetKey, int selectionGeneration)
        {
            if (!string.Equals(groupId, "LoginBackground", StringComparison.OrdinalIgnoreCase) ||
                loginBackgroundMediaService == null ||
                loginBackgroundMediaService.IsDefaultPreset(presetKey) ||
                loginBackgroundMediaService.IsRandomPreset(presetKey) ||
                currentFile?.Presets == null ||
                !currentFile.Presets.TryGetValue(groupId, out var group) ||
                group?.Items == null)
            {
                return false;
            }

            var preset = group.Items.FirstOrDefault(x =>
                x != null && string.Equals(x.Key, presetKey, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
            {
                return false;
            }

            var themePath = !string.IsNullOrWhiteSpace(currentThemePath)
                ? currentThemePath
                : GetFullscreenThemePath();
            if (string.IsNullOrWhiteSpace(themePath) ||
                !loginBackgroundMediaService.TryResolveRequiredVideoFile(preset, themePath, out var requiredFileName))
            {
                return false;
            }

            if (loginBackgroundMediaService.EnsurePersistentVideoProjected(themePath, requiredFileName))
            {
                return false;
            }

            settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var previousPresetKey);
            RestorePresetSelectionVisual(groupId, previousPresetKey);

            _ = DownloadLoginBackgroundAndSelectAsync(
                groupId,
                presetKey,
                preset.DisplayName,
                requiredFileName,
                themePath,
                selectionGeneration);

            return true;
        }

        private async Task DownloadLoginBackgroundAndSelectAsync(
            string groupId,
            string presetKey,
            string displayName,
            string requiredFileName,
            string themePath,
            int selectionGeneration)
        {
            try
            {
                var available = await loginBackgroundMediaService.EnsureVideoAvailableAsync(
                    presetKey,
                    displayName,
                    requiredFileName,
                    themePath);

                if (!available || selectionGeneration != loginBackgroundSelectionGeneration)
                {
                    return;
                }

                ApplyPresetSelectionCore(groupId, presetKey);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][LoginMedia] Failed to install/select login background: {presetKey}");
            }
        }

        private void RestorePresetSelectionVisual(string groupId, string presetKey)
        {
            try
            {
                if (currentFile?.Presets == null ||
                    !currentFile.Presets.TryGetValue(groupId, out var group) ||
                    group == null)
                {
                    return;
                }

                group.SetSelectedPresetKeySilently(presetKey ?? string.Empty);
            }
            catch
            {
            }
        }

        public IReadOnlyList<VisualPackRandomCandidate> GetAvailableVisualPackRandomCandidates()
        {
            var result = new List<VisualPackRandomCandidate>();

            try
            {
                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(VisualPackPresetGroupId, out var group) &&
                    group?.Items != null)
                {
                    foreach (var preset in group.Items)
                    {
                        if (preset == null || string.IsNullOrWhiteSpace(preset.Key) ||
                            string.Equals(preset.Key, CustomVisualPackPresetKey, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        result.Add(new VisualPackRandomCandidate
                        {
                            Id = "visualpack:preset:" + preset.Key.Trim(),
                            DisplayName = string.IsNullOrWhiteSpace(preset.DisplayName) ? preset.Key : preset.DisplayName,
                            PresetKey = preset.Key
                        });
                    }
                }

                // Installed Visual Packs are already local. No network/catalog access is used
                // by Random at startup.
                var library = visualPackImportService?.GetLibrary();
                foreach (var pack in library?.Packs ?? new List<VisualPackLibraryPack>())
                {
                    if (pack == null || string.IsNullOrWhiteSpace(pack.LocalId))
                    {
                        continue;
                    }

                    var stablePackId = !string.IsNullOrWhiteSpace(pack.PackId)
                        ? pack.PackId.Trim()
                        : pack.LocalId.Trim();

                    result.Add(new VisualPackRandomCandidate
                    {
                        Id = "visualpack:pack:" + stablePackId,
                        DisplayName = string.IsNullOrWhiteSpace(pack.Name) ? stablePackId : pack.Name,
                        CommunityPackId = stablePackId,
                        CommunityLocalId = pack.LocalId
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][VisualPackRandom] Failed to build the local Random Visual Pack candidate pool.");
            }

            return result
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private VisualPackRandomCandidate EnsureRandomVisualPackSessionCandidate()
        {
            if (!IsRandomVisualPackFilterActive())
            {
                activeRandomVisualPackCandidate = null;
                return null;
            }

            var available = GetAvailableVisualPackRandomCandidates().ToList();
            var excluded = new HashSet<string>(
                settings.VisualPackRandomExcludedCandidateIds ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            var enabled = available
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id) && !excluded.Contains(x.Id))
                .ToList();

            if (activeRandomVisualPackCandidate != null &&
                enabled.Any(x => string.Equals(x.Id, activeRandomVisualPackCandidate.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return activeRandomVisualPackCandidate;
            }

            var picked = RandomSelectionService.Pick(
                enabled,
                candidate => candidate.Id,
                settings.LastVisualPackRandomCandidateId,
                new Random());

            activeRandomVisualPackCandidate = picked;

            if (picked != null &&
                !string.Equals(settings.LastVisualPackRandomCandidateId, picked.Id, StringComparison.OrdinalIgnoreCase))
            {
                settings.LastVisualPackRandomCandidateId = picked.Id;
                try
                {
                    settings.EndEdit();
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][VisualPackRandom] Failed to persist the last Random Visual Pack candidate.");
                }
            }

            DebugLog($"[AnikiHelper][VisualPackRandom] available={available.Count} | enabled={enabled.Count} | pick={picked?.Id ?? "<default>"}");
            return picked;
        }

        private void LoadRandomVisualPackOverride()
        {
            if (!IsRandomVisualPackFilterActive() || Application.Current == null)
            {
                return;
            }

            try
            {
                var picked = EnsureRandomVisualPackSessionCandidate();
                if (picked == null)
                {
                    // Empty/fully excluded pool falls back to the theme's base Visual Pack.
                    global::AnikiHelper.VisualPackBackgroundComposer.RefreshNow();
                    return;
                }

                if (currentFile?.Presets == null ||
                    !currentFile.Presets.TryGetValue(VisualPackPresetGroupId, out var group) ||
                    group?.Items == null)
                {
                    return;
                }

                AnikiPresetItem presetToLoad;

                if (picked.IsCommunityPack)
                {
                    // Installed packs use the theme's Custom index (153), while the image brushes
                    // themselves are loaded directly from Helper's persistent local library.
                    presetToLoad = group.Items.FirstOrDefault(x => x != null &&
                        string.Equals(x.Key, CustomVisualPackPresetKey, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    presetToLoad = group.Items.FirstOrDefault(x => x != null &&
                        string.Equals(x.Key, picked.PresetKey, StringComparison.OrdinalIgnoreCase));
                }

                if (presetToLoad?.Files != null)
                {
                    foreach (var relativeFile in presetToLoad.Files)
                    {
                        if (string.IsNullOrWhiteSpace(relativeFile))
                        {
                            continue;
                        }

                        var filePath = Path.Combine(currentThemePath, relativeFile);
                        if (!File.Exists(filePath))
                        {
                            logger?.Warn($"[AnikiHelper][VisualPackRandom] Visual Pack resource file not found: {filePath}");
                            continue;
                        }

                        var resource = GetOrLoadResourceDictionary(filePath);
                        if (resource != null)
                        {
                            Application.Current.Resources.MergedDictionaries.Add(resource);
                            loadedDictionaries.Add(resource);
                        }
                    }
                }

                if (picked.IsCommunityPack)
                {
                    RefreshCustomVisualPackRuntimeImages(picked.CommunityLocalId);
                }

                global::AnikiHelper.VisualPackBackgroundComposer.RefreshNow();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][VisualPackRandom] Failed to apply the Random Visual Pack override.");
            }
        }

        public IReadOnlyList<ThemeColorRandomCandidate> GetAvailableThemeColorRandomCandidates()
        {
            var result = new List<ThemeColorRandomCandidate>();

            try
            {
                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(ThemeColorPresetGroupId, out var group) &&
                    group?.Items != null)
                {
                    foreach (var preset in group.Items)
                    {
                        if (preset == null || string.IsNullOrWhiteSpace(preset.Key) ||
                            string.Equals(preset.Key, CustomColorPackPresetKey, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(GetPresetFilterValue(preset), RandomThemeColorFilterValue, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        result.Add(new ThemeColorRandomCandidate
                        {
                            Id = "themecolor:preset:" + preset.Key.Trim(),
                            DisplayName = string.IsNullOrWhiteSpace(preset.DisplayName) ? preset.Key : preset.DisplayName,
                            PresetKey = preset.Key
                        });
                    }
                }

                // Community Color Packs are already installed locally. Random startup never
                // downloads anything and does not depend on the Community catalog/network.
                var library = customColorPackLibrarySnapshot ?? colorPackImportService?.GetLibrary();
                foreach (var pack in library?.Packs ?? new List<ColorPackLibraryPack>())
                {
                    if (pack == null || string.IsNullOrWhiteSpace(pack.LocalId))
                    {
                        continue;
                    }

                    var stablePackId = !string.IsNullOrWhiteSpace(pack.PackId)
                        ? pack.PackId.Trim()
                        : pack.LocalId.Trim();

                    result.Add(new ThemeColorRandomCandidate
                    {
                        Id = "themecolor:colorpack:" + stablePackId,
                        DisplayName = string.IsNullOrWhiteSpace(pack.Name) ? stablePackId : pack.Name,
                        CommunityPackId = stablePackId,
                        CommunityLocalId = pack.LocalId
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ThemeColorRandom] Failed to build the local Random Theme Color candidate pool.");
            }

            return result
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private ThemeColorRandomCandidate EnsureRandomThemeColorSessionCandidate()
        {
            if (!IsRandomThemeColorFilterActive())
            {
                activeRandomThemeColorCandidate = null;
                return null;
            }

            var available = GetAvailableThemeColorRandomCandidates().ToList();
            var excluded = new HashSet<string>(
                settings.ThemeColorRandomExcludedCandidateIds ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            var enabled = available
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id) && !excluded.Contains(x.Id))
                .ToList();

            if (activeRandomThemeColorCandidate != null &&
                enabled.Any(x => string.Equals(x.Id, activeRandomThemeColorCandidate.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return activeRandomThemeColorCandidate;
            }

            var picked = RandomSelectionService.Pick(
                enabled,
                candidate => candidate.Id,
                settings.LastThemeColorRandomCandidateId,
                new Random());

            activeRandomThemeColorCandidate = picked;

            if (picked != null &&
                !string.Equals(settings.LastThemeColorRandomCandidateId, picked.Id, StringComparison.OrdinalIgnoreCase))
            {
                settings.LastThemeColorRandomCandidateId = picked.Id;
                try
                {
                    settings.EndEdit();
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][ThemeColorRandom] Failed to persist the last Random Theme Color candidate.");
                }
            }

            DebugLog($"[AnikiHelper][ThemeColorRandom] available={available.Count} | enabled={enabled.Count} | pick={picked?.Id ?? "<default>"}");
            return picked;
        }

        private void LoadRandomThemeColorOverride()
        {
            if (!IsRandomThemeColorFilterActive() || Application.Current == null)
            {
                return;
            }

            try
            {
                var picked = EnsureRandomThemeColorSessionCandidate();
                if (picked == null)
                {
                    // An empty/fully excluded pool intentionally falls back to the theme's base color.
                    return;
                }

                if (picked.IsCommunityPack)
                {
                    var dictionary = colorPackImportService?.LoadResourceDictionary(picked.CommunityLocalId);
                    if (dictionary != null)
                    {
                        dictionary.Remove("BackgroundImageIndex");
                        Application.Current.Resources.MergedDictionaries.Add(dictionary);
                        loadedDictionaries.Add(dictionary);
                    }

                    return;
                }

                if (currentFile?.Presets == null ||
                    !currentFile.Presets.TryGetValue(ThemeColorPresetGroupId, out var group) ||
                    group?.Items == null)
                {
                    return;
                }

                var preset = group.Items.FirstOrDefault(x => x != null &&
                    string.Equals(x.Key, picked.PresetKey, StringComparison.OrdinalIgnoreCase));
                if (preset?.Files == null)
                {
                    return;
                }

                foreach (var relativeFile in preset.Files)
                {
                    if (string.IsNullOrWhiteSpace(relativeFile))
                    {
                        continue;
                    }

                    var filePath = Path.Combine(currentThemePath, relativeFile);
                    if (!File.Exists(filePath))
                    {
                        logger?.Warn($"[AnikiHelper][ThemeColorRandom] Theme Color resource file not found: {filePath}");
                        continue;
                    }

                    var resource = GetOrLoadResourceDictionary(filePath);
                    if (resource != null)
                    {
                        Application.Current.Resources.MergedDictionaries.Add(resource);
                        loadedDictionaries.Add(resource);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ThemeColorRandom] Failed to apply the Random Theme Color override.");
            }
        }

        public IReadOnlyList<int> GetAvailableLoginRandomIndexes()
        {
            try
            {
                var themePath = !string.IsNullOrWhiteSpace(currentThemePath)
                    ? currentThemePath
                    : GetFullscreenThemePath();

                if (!string.IsNullOrWhiteSpace(themePath))
                {
                    SynchronizeLoginBackgroundMedia(themePath);
                }

                return loginBackgroundMediaService?.GetAvailableRandomIndexes(themePath)
                    ?? new List<int>();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to query installed Random Login backgrounds.");
                return new List<int>();
            }
        }

        public IReadOnlyList<LoginRandomCandidate> GetAvailableLoginRandomCandidates()
        {
            var result = new List<LoginRandomCandidate>();

            try
            {
                var themePath = !string.IsNullOrWhiteSpace(currentThemePath)
                    ? currentThemePath
                    : GetFullscreenThemePath();
                var builtInNames = loginBackgroundMediaService?.GetRandomDisplayNames(themePath)
                    ?? new Dictionary<int, string>();

                foreach (var index in GetAvailableLoginRandomIndexes())
                {
                    if (index <= 0 ||
                        index == LoginBackgroundMediaService.LuckyDayRandomIndex ||
                        index == LoginBackgroundMediaService.CustomRandomIndex)
                    {
                        continue;
                    }

                    result.Add(new LoginRandomCandidate
                    {
                        Id = "builtin:" + index,
                        DisplayName = builtInNames.TryGetValue(index, out var builtInName) && !string.IsNullOrWhiteSpace(builtInName)
                            ? builtInName
                            : "Login " + index,
                        BuiltInIndex = index
                    });
                }

                // Community Login Packs are already local at this point. Random startup must never
                // download anything or depend on GitHub/network availability.
                var library = loginPackImportService?.GetLibrary();
                foreach (var pack in library?.Packs ?? new List<LoginPackLibraryPack>())
                {
                    if (pack == null || string.IsNullOrWhiteSpace(pack.LocalId))
                    {
                        continue;
                    }

                    var videoPath = pack.VideoPath;
                    if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
                    {
                        videoPath = loginPackImportService?.GetVideoPath(pack.LocalId);
                    }

                    if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
                    {
                        continue;
                    }

                    var stablePackId = !string.IsNullOrWhiteSpace(pack.PackId)
                        ? pack.PackId.Trim()
                        : pack.LocalId.Trim();

                    result.Add(new LoginRandomCandidate
                    {
                        Id = "loginpack:" + stablePackId,
                        DisplayName = string.IsNullOrWhiteSpace(pack.Name) ? stablePackId : pack.Name,
                        CommunityPackId = stablePackId,
                        CommunityLocalId = pack.LocalId,
                        VideoPath = videoPath
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginRandom] Failed to build the local Random Login candidate pool.");
            }

            return result
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private bool DoesVariableNeedRestart(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) || currentFile?.Variables == null)
                {
                    return false;
                }

                return currentFile.Variables.TryGetValue(key, out var variable) &&
                       variable != null &&
                       variable.NeedRestart;
            }
            catch
            {
                return false;
            }
        }

        private bool DoesPresetNeedRestart(string groupId, string presetKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupId) ||
                    string.IsNullOrWhiteSpace(presetKey) ||
                    currentFile?.Presets == null)
                {
                    return false;
                }

                if (!currentFile.Presets.TryGetValue(groupId, out var group) ||
                    group == null)
                {
                    return false;
                }

                if (group.NeedRestart)
                {
                    return true;
                }

                if (group.Items == null)
                {
                    return false;
                }

                var preset = group.Items.FirstOrDefault(x =>
                    string.Equals(x.Key, presetKey, StringComparison.OrdinalIgnoreCase));

                return preset != null && preset.NeedRestart;
            }
            catch
            {
                return false;
            }
        }

        public void ShowRestartPromptIfNeeded(object settingsWindowContext = null)
        {
            try
            {
                if (!pendingRestartPrompt)
                {
                    return;
                }

                pendingRestartPrompt = false;

                dynamic ctx = settingsWindowContext;

                if (ctx == null)
                {
                    ctx = Application.Current.MainWindow?.DataContext;
                }

                if (ctx == null)
                {
                    logger?.Warn("[AnikiHelper] Could not trigger Playnite restart prompt: settings context is null.");
                    return;
                }

                ctx.AppSettings.Fullscreen.OnPropertyChanged("Theme");
                ctx.AppSettings.OnPropertyChanged("Theme");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to trigger Playnite restart prompt for Aniki Theme Settings.");
            }
        }

        public IReadOnlyList<AnikiPresetItem> GetPresetItems(string groupId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupId) ||
                    currentFile?.Presets == null ||
                    !currentFile.Presets.TryGetValue(groupId, out var group) ||
                    group?.Items == null)
                {
                    return new List<AnikiPresetItem>();
                }

                return group.Items.ToList();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to read Aniki preset items: {groupId}");
                return new List<AnikiPresetItem>();
            }
        }

        public string ResolveThemeFilePath(string relativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    return string.Empty;
                }

                if (Path.IsPathRooted(relativePath))
                {
                    return relativePath;
                }

                if (string.IsNullOrWhiteSpace(currentThemePath))
                {
                    return relativePath;
                }

                var normalized = relativePath
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);

                return Path.Combine(currentThemePath, normalized);
            }
            catch
            {
                return relativePath ?? string.Empty;
            }
        }

        public object GetDefaultOptionValue(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) ||
                    currentFile?.Variables == null ||
                    !currentFile.Variables.TryGetValue(key, out var variable) ||
                    variable == null)
                {
                    return false;
                }

                return ConvertValue(variable.Type, variable.Default ?? variable.Value);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to read Aniki theme default value: {key}");
                return false;
            }
        }

        public Dictionary<string, object> GetAllDefaultOptionValues()
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (currentFile?.Variables == null)
                {
                    return result;
                }

                foreach (var pair in currentFile.Variables)
                {
                    var key = pair.Key;
                    var variable = pair.Value;

                    if (string.IsNullOrWhiteSpace(key) ||
                        variable == null ||
                        string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result[key] = ConvertValue(
                        variable.Type,
                        variable.Default ?? variable.Value);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to read all Aniki theme default option values.");
            }

            return result;
        }

        public Dictionary<string, string> GetAllDefaultPresetSelections()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (currentFile?.Presets == null)
                {
                    return result;
                }

                foreach (var pair in currentFile.Presets)
                {
                    var groupId = pair.Key;
                    var defaultPresetKey = GetDefaultPresetKey(pair.Value);

                    if (!string.IsNullOrWhiteSpace(groupId) &&
                        !string.IsNullOrWhiteSpace(defaultPresetKey))
                    {
                        result[groupId] = defaultPresetKey;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to read all Aniki theme default preset selections.");
            }

            return result;
        }

        public bool ApplyInitialSetupConfiguration(
            IDictionary<string, object> optionValues,
            IDictionary<string, string> presetSelections,
            bool suppressRestartPrompt = false)
        {
            var restartRequired = false;

            try
            {
                EnsureThemeSettingsDictionaries();

                if (optionValues != null)
                {
                    foreach (var pair in optionValues)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) ||
                            currentFile?.Variables == null ||
                            !currentFile.Variables.TryGetValue(pair.Key, out var variable) ||
                            variable == null ||
                            string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var finalValue = pair.Value?.ToString() ?? string.Empty;

                        if (settings.AnikiThemeSettingsValues.TryGetValue(pair.Key, out var currentValue) &&
                            string.Equals(currentValue, finalValue, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (variable.NeedRestart)
                        {
                            restartRequired = true;
                        }

                        settings.AnikiThemeSettingsValues[pair.Key] = finalValue;
                    }
                }

                ApplyOptionDependenciesToStorage();

                if (presetSelections != null)
                {
                    foreach (var pair in presetSelections)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) ||
                            string.IsNullOrWhiteSpace(pair.Value) ||
                            currentFile?.Presets == null ||
                            !currentFile.Presets.TryGetValue(pair.Key, out var group) ||
                            group?.Items == null ||
                            !group.Items.Any(item =>
                                string.Equals(item.Key, pair.Value, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (settings.AnikiThemeSettingsSelectedPresets.TryGetValue(pair.Key, out var currentPreset) &&
                            string.Equals(currentPreset, pair.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            SyncPresetFilterFromSelection(pair.Key, pair.Value);
                            continue;
                        }

                        if (DoesPresetNeedRestart(pair.Key, pair.Value))
                        {
                            restartRequired = true;
                        }

                        settings.AnikiThemeSettingsSelectedPresets[pair.Key] = pair.Value;
                        SyncPresetFilterFromSelection(pair.Key, pair.Value);
                    }
                }

                RefreshAllPresetFilters(true);

                if (restartRequired && !suppressRestartPrompt)
                {
                    MarkRestartRequired();
                }

                SaveSettings();
                Apply();

                return restartRequired;
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "[AnikiHelper] Failed to apply the initial Aniki setup configuration.");
                return restartRequired;
            }
        }

        public void MarkInitialSetupCompleted()
        {
            try
            {
                initialSetupVersion = CurrentInitialSetupVersion;
                initialSetupOfferVersion = CurrentInitialSetupOfferVersion;
                initialSetupAutomaticRequired = false;
                initialSetupStateLoaded = true;
                SaveThemeSettingsFile();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to mark the initial setup as completed.");
            }
        }

        public void MarkInitialSetupOfferSeen()
        {
            try
            {
                initialSetupOfferVersion = CurrentInitialSetupOfferVersion;
                initialSetupAutomaticRequired = false;
                initialSetupStateLoaded = true;
                SaveThemeSettingsFile();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to mark the initial setup offer as seen.");
            }
        }

        public void ShowPreview(string presetId)
        {
            try
            {
                var preset = FindPreset(presetId);
                settings.AnikiThemeSettingsPreviewImage = preset?.Preview;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to show Aniki preset preview: {presetId}");
            }
        }

        public void HidePreview()
        {
            try
            {
                settings.AnikiThemeSettingsPreviewImage = null;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to hide Aniki preset preview.");
            }
        }

        public void Reload()
        {
            resourceCache.Clear();
            LoadAndApply();
        }

        public void ExportThemeConfiguration(string exportFilePath)
        {
            if (string.IsNullOrWhiteSpace(exportFilePath))
            {
                throw new ArgumentException("The export file path is empty.", nameof(exportFilePath));
            }

            try
            {
                if (!File.Exists(themeSettingsFilePath))
                {
                    throw new FileNotFoundException(
                        "ThemeSettings.json was not found. Open the compatible Fullscreen theme once so its configuration can be created.",
                        themeSettingsFilePath);
                }

                var directory = Path.GetDirectoryName(exportFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var sourcePath = Path.GetFullPath(themeSettingsFilePath);
                var destinationPath = Path.GetFullPath(exportFilePath);

                if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, destinationPath, true);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to export ThemeSettings.json.");
                throw;
            }
        }

        public void ImportThemeConfiguration(string importFilePath)
        {
            if (string.IsNullOrWhiteSpace(importFilePath))
            {
                throw new ArgumentException("The import file path is empty.", nameof(importFilePath));
            }

            if (!File.Exists(importFilePath))
            {
                throw new FileNotFoundException("The theme configuration file was not found.", importFilePath);
            }

            try
            {
                // Validate the selected file before replacing the plugin's stored configuration.
                var importedStorage = Serialization.FromJsonFile<AnikiThemeSettingsStorageFile>(importFilePath);

                if (importedStorage == null ||
                    importedStorage.Values == null ||
                    importedStorage.SelectedPresets == null)
                {
                    throw new InvalidDataException("The selected file is not a valid ThemeSettings.json configuration.");
                }

                Directory.CreateDirectory(pluginUserDataPath);

                var sourcePath = Path.GetFullPath(importFilePath);
                var destinationPath = Path.GetFullPath(themeSettingsFilePath);

                if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    var temporaryPath = destinationPath + ".import.tmp";

                    try
                    {
                        File.Copy(sourcePath, temporaryPath, true);
                        File.Copy(temporaryPath, destinationPath, true);
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(temporaryPath))
                            {
                                File.Delete(temporaryPath);
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                // Keep the in-memory storage synchronized so a later save cannot restore
                // the values that were loaded before the import.
                settings.AnikiThemeSettingsValues = CopyDictionary(importedStorage.Values);
                settings.AnikiThemeSettingsSelectedPresets = CopyDictionary(importedStorage.SelectedPresets);

                initialSetupVersion = importedStorage.InitialSetupVersion ?? 0;
                initialSetupOfferVersion = importedStorage.InitialSetupOfferVersion ?? 0;
                initialSetupAutomaticRequired = importedStorage.InitialSetupAutomaticRequired ?? false;
                initialSetupStateLoaded = true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to import ThemeSettings.json.");
                throw;
            }
        }

        private bool ImportedConfigurationRequiresRestart(
            Dictionary<string, string> previousValues,
            Dictionary<string, string> previousPresets)
        {
            try
            {
                if (currentFile?.Variables != null)
                {
                    foreach (var pair in currentFile.Variables)
                    {
                        var key = pair.Key;
                        var variable = pair.Value;

                        if (string.IsNullOrWhiteSpace(key) || variable == null || !variable.NeedRestart)
                        {
                            continue;
                        }

                        string previousValue = null;
                        string currentValue = null;

                        previousValues?.TryGetValue(key, out previousValue);
                        settings.AnikiThemeSettingsValues?.TryGetValue(key, out currentValue);

                        if (!string.Equals(previousValue ?? string.Empty, currentValue ?? string.Empty, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }

                if (currentFile?.Presets != null)
                {
                    foreach (var pair in currentFile.Presets)
                    {
                        var groupId = pair.Key;

                        string previousPreset = null;
                        string currentPreset = null;

                        previousPresets?.TryGetValue(groupId, out previousPreset);
                        settings.AnikiThemeSettingsSelectedPresets?.TryGetValue(groupId, out currentPreset);

                        if (string.Equals(previousPreset ?? string.Empty, currentPreset ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (DoesPresetNeedRestart(groupId, currentPreset))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to determine whether imported theme settings require a restart.");
            }

            return false;
        }

        public void SetOptionFromParameter(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            var values = ParseCommandParameter(parameter);

            if (values.TryGetValue("Key", out var key))
            {
                values.TryGetValue("Value", out var value);
                SetOptionValue(key, value);
                return;
            }

            // Simple fallback:
            // SomeKey=False
            var split = parameter.Split(new[] { '=' }, 2);

            if (split.Length == 2)
            {
                SetOptionValue(split[0].Trim(), split[1].Trim());
            }
        }

        public void ToggleOptionFromParameter(string parameter)
        {
            var values = ParseCommandParameter(parameter);

            if (!values.TryGetValue("Key", out var key))
            {
                key = parameter;
            }

            ToggleOptionValue(key);
        }

        public void SelectPresetFromParameter(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            var values = ParseCommandParameter(parameter);

            if (values.TryGetValue("Group", out var group) &&
                values.TryGetValue("Preset", out var preset))
            {
                SelectPreset(group, preset);
                return;
            }

            // Also support simple format:
            // Avatar.Avatar12
            var text = parameter.Trim();

            var dotIndex = text.IndexOf('.');
            if (dotIndex > 0 && dotIndex < text.Length - 1)
            {
                var groupId = text.Substring(0, dotIndex).Trim();
                var presetKey = text.Substring(dotIndex + 1).Trim();

                SelectPreset(groupId, presetKey);
            }
        }

        private bool EnsureThemeSettingsStorageLoadedForPackOperation()
        {
            if (initialSetupStateLoaded)
            {
                return true;
            }

            try
            {
                // Desktop mode intentionally does not run LoadAndApply(), but pack-library
                // actions can still need to change Fullscreen selections. Hydrate the persisted
                // ThemeSettings.json first so an empty startup dictionary can never overwrite it.
                LoadThemeSettingsStorage();
                return initialSetupStateLoaded;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load ThemeSettings.json before a pack operation.");
                return false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                // Safety net for Desktop mode: never replace ThemeSettings.json before its
                // persisted state (including setup-wizard state) has been loaded.
                if (!initialSetupStateLoaded)
                {
                    logger?.Warn("[AnikiHelper] Skipped ThemeSettings.json save because theme settings storage is not loaded.");
                    return;
                }

                SaveThemeSettingsFile();

                // Keep normal plugin settings saved too, but theme settings themselves are now DontSerialize.
                settings.EndEdit();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save Aniki Theme Settings values.");
            }
        }

        private void LoadThemeSettingsStorage()
        {
            var corruptedStorageRecovered = false;

            try
            {
                EnsureThemeSettingsDictionaries();

                if (File.Exists(themeSettingsFilePath))
                {
                    try
                    {
                        var storage = Serialization.FromJsonFile<AnikiThemeSettingsStorageFile>(themeSettingsFilePath);

                        settings.AnikiThemeSettingsValues = CopyDictionary(storage?.Values);
                        settings.AnikiThemeSettingsSelectedPresets = CopyDictionary(storage?.SelectedPresets);

                        if (storage?.InitialSetupVersion != null)
                        {
                            // A stored setup version means this installation already knows about the
                            // onboarding system. Version 0 without the newer offer fields is treated as
                            // a genuine first-install setup that still needs to open automatically.
                            initialSetupVersion = storage.InitialSetupVersion.Value;
                            initialSetupOfferVersion = storage.InitialSetupOfferVersion
                                ?? CurrentInitialSetupOfferVersion;
                            initialSetupAutomaticRequired = storage.InitialSetupAutomaticRequired
                                ?? (!storage.InitialSetupOfferVersion.HasValue &&
                                    initialSetupVersion < CurrentInitialSetupVersion);
                        }
                        else
                        {
                            // The file predates the onboarding system. Offer the assistant once, but
                            // never force the full wizard on an existing user.
                            initialSetupVersion = 0;
                            initialSetupOfferVersion = 0;
                            initialSetupAutomaticRequired = false;
                        }

                        initialSetupStateLoaded = true;

                        if (settings?.EnableDebugLogs == true)
                        {
                            DebugLog($"[AnikiHelper] Loaded ThemeSettings.json: {themeSettingsFilePath}");
                        }

                        return;
                    }
                    catch (Exception ex)
                    {
                        corruptedStorageRecovered = true;
                        logger?.Warn(ex, "[AnikiHelper] Failed to load ThemeSettings.json. A backup will be created and defaults will be rebuilt.");

                        try
                        {
                            var backupPath = Path.Combine(
                                pluginUserDataPath,
                                $"ThemeSettings.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.json");

                            File.Copy(themeSettingsFilePath, backupPath, true);
                            logger?.Warn($"[AnikiHelper] Corrupted ThemeSettings.json backup created: {backupPath}");
                        }
                        catch
                        {
                        }
                    }
                }

                // First version using the separated file:
                // migrate once from old config.json if possible.
                var migratedLegacySettings = MigrateThemeSettingsFromLegacyConfig();

                if (corruptedStorageRecovered || migratedLegacySettings)
                {
                    // This is an existing installation. Offer the assistant once, but keep all
                    // current settings untouched unless the user explicitly completes it.
                    initialSetupVersion = 0;
                    initialSetupOfferVersion = 0;
                    initialSetupAutomaticRequired = false;
                }
                else
                {
                    // No separated storage and no legacy theme values means a genuine first install.
                    // Open the full assistant automatically and do not show the legacy-user offer.
                    initialSetupVersion = 0;
                    initialSetupOfferVersion = CurrentInitialSetupOfferVersion;
                    initialSetupAutomaticRequired = true;
                }

                initialSetupStateLoaded = true;

                if (settings?.EnableDebugLogs == true)
                {
                    DebugLog("[AnikiHelper] ThemeSettings.json does not exist yet. It will be created from current YAML defaults.");
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to initialize ThemeSettings.json storage.");

                settings.AnikiThemeSettingsValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                settings.AnikiThemeSettingsSelectedPresets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                initialSetupVersion = CurrentInitialSetupVersion;
                initialSetupOfferVersion = CurrentInitialSetupOfferVersion;
                initialSetupAutomaticRequired = false;
                initialSetupStateLoaded = true;
            }
        }

        private bool MigrateThemeSettingsFromLegacyConfig()
        {
            try
            {
                var legacyConfigPath = Path.Combine(pluginUserDataPath, "config.json");

                if (!File.Exists(legacyConfigPath))
                {
                    return false;
                }

                var legacy = Serialization.FromJsonFile<AnikiThemeSettingsLegacyConfigFile>(legacyConfigPath);

                var legacyValues = CopyDictionary(legacy?.AnikiThemeSettingsValues);
                var legacyPresets = CopyDictionary(legacy?.AnikiThemeSettingsSelectedPresets);

                if (legacyValues.Count == 0 && legacyPresets.Count == 0)
                {
                    return false;
                }

                settings.AnikiThemeSettingsValues = legacyValues;
                settings.AnikiThemeSettingsSelectedPresets = legacyPresets;

                DebugLog("[AnikiHelper] Migrated Aniki Theme Settings values from old config.json to ThemeSettings.json.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to migrate Aniki Theme Settings from old config.json.");
                return false;
            }
        }

        private bool MigrateLegacyMainBackgroundOptions()
        {
            try
            {
                if (currentFile?.Presets == null ||
                    !currentFile.Presets.ContainsKey("MainBackground") ||
                    settings?.AnikiThemeSettingsSelectedPresets == null ||
                    settings.AnikiThemeSettingsValues == null)
                {
                    return false;
                }

                if (settings.AnikiThemeSettingsSelectedPresets.TryGetValue("MainBackground", out var currentSelection) &&
                    !string.IsNullOrWhiteSpace(currentSelection))
                {
                    return false;
                }

                var hasNoBackground = settings.AnikiThemeSettingsValues.TryGetValue("NoBackground", out var noBackgroundValue);
                var hasFilterBackground = settings.AnikiThemeSettingsValues.TryGetValue("BackgroundByFilter", out var filterValue);
                var hasVisualPackBackground = settings.AnikiThemeSettingsValues.TryGetValue("BackgroundPackVisual", out var visualPackValue);

                if (!hasNoBackground && !hasFilterBackground && !hasVisualPackBackground)
                {
                    return false;
                }

                var selectedMode = "Game";

                if (hasNoBackground && ToBool(noBackgroundValue))
                {
                    selectedMode = "None";
                }
                else if (hasFilterBackground && ToBool(filterValue))
                {
                    selectedMode = "Filter";
                }
                else if (hasVisualPackBackground && ToBool(visualPackValue))
                {
                    selectedMode = "VisualPack";
                }

                settings.AnikiThemeSettingsSelectedPresets["MainBackground"] = selectedMode;
                logger?.Info($"[AnikiHelper][Migration] Main background mode migrated to: {selectedMode}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Migration] Failed to migrate legacy background options.");
                return false;
            }
        }

        private bool MigrateLegacyMainViewMediaCardOption()
        {
            try
            {
                if (settings?.AnikiThemeSettingsValues == null ||
                    currentFile?.Variables == null ||
                    !currentFile.Variables.ContainsKey("MainViewMediaCard"))
                {
                    return false;
                }

                if (settings.AnikiThemeSettingsValues.ContainsKey("MainViewMediaCard"))
                {
                    return false;
                }

                var migratedValue = "Disabled";

                if (settings.AnikiThemeSettingsValues.TryGetValue("TrailerCardOnMainView", out var legacyCardValue) &&
                    ToBool(legacyCardValue))
                {
                    migratedValue = "Trailer";
                }

                settings.AnikiThemeSettingsValues["MainViewMediaCard"] = migratedValue;
                settings.AnikiThemeSettingsValues.Remove("TrailerCardOnMainView");

                logger?.Info($"[AnikiHelper][Migration] Main View media card migrated to: {migratedValue}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Migration] Failed to migrate legacy Main View trailer card option.");
                return false;
            }
        }

        private bool MigrateLegacyMainViewBottomBarOption()
        {
            try
            {
                if (settings?.AnikiThemeSettingsValues == null ||
                    currentFile?.Variables == null ||
                    !currentFile.Variables.ContainsKey("MainViewBottomBar"))
                {
                    return false;
                }

                if (settings.AnikiThemeSettingsValues.ContainsKey("MainViewBottomBar"))
                {
                    return false;
                }

                var migratedValue = "ControllerShortcuts";

                if (settings.AnikiThemeSettingsValues.TryGetValue("CompactGameInfoBar", out var compactValue) &&
                    ToBool(compactValue))
                {
                    migratedValue = "CompactGameInfo";
                }
                else if (settings.AnikiThemeSettingsValues.TryGetValue("ControllerShortcutBar", out var controllerValue) &&
                         !ToBool(controllerValue))
                {
                    migratedValue = "Disabled";
                }

                settings.AnikiThemeSettingsValues["MainViewBottomBar"] = migratedValue;
                settings.AnikiThemeSettingsValues.Remove("ControllerShortcutBar");
                settings.AnikiThemeSettingsValues.Remove("CompactGameInfoBar");

                logger?.Info($"[AnikiHelper][Migration] Main View bottom bar migrated to: {migratedValue}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Migration] Failed to migrate Main View bottom bar options.");
                return false;
            }
        }

        private bool MigrateLegacyFocusedCoverPreviewOption()
        {
            try
            {
                if (settings?.AnikiThemeSettingsValues == null ||
                    currentFile?.Variables == null ||
                    !currentFile.Variables.ContainsKey("FocusedCoverPreview"))
                {
                    return false;
                }

                if (settings.AnikiThemeSettingsValues.ContainsKey("FocusedCoverPreview"))
                {
                    return false;
                }

                var migratedValue = "Disabled";

                if (settings.AnikiThemeSettingsValues.TryGetValue("MicroTrailerOnFocusedCover", out var microValue) &&
                    ToBool(microValue))
                {
                    migratedValue = "MicroTrailer";
                }
                else if (settings.AnikiThemeSettingsValues.TryGetValue("BackgroundOnFocusedCover", out var backgroundValue) &&
                         ToBool(backgroundValue))
                {
                    migratedValue = "BackgroundLogo";
                }

                settings.AnikiThemeSettingsValues["FocusedCoverPreview"] = migratedValue;
                settings.AnikiThemeSettingsValues.Remove("MicroTrailerOnFocusedCover");
                settings.AnikiThemeSettingsValues.Remove("BackgroundOnFocusedCover");

                logger?.Info($"[AnikiHelper][Migration] Focused cover preview migrated to: {migratedValue}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Migration] Failed to migrate focused cover preview options.");
                return false;
            }
        }

        private bool MigrateLegacyBackgroundDisplayModeOption()
        {
            try
            {
                if (settings?.AnikiThemeSettingsValues == null ||
                    currentFile?.Variables == null ||
                    !currentFile.Variables.ContainsKey("BackgroundDisplayMode"))
                {
                    return false;
                }

                if (settings.AnikiThemeSettingsValues.ContainsKey("BackgroundDisplayMode"))
                {
                    return false;
                }

                var migratedValue = "FillCrop";

                if (settings.AnikiThemeSettingsValues.TryGetValue("SteamBanner", out var heroValue) &&
                    ToBool(heroValue))
                {
                    migratedValue = "FitHero";
                }
                else if (settings.AnikiThemeSettingsValues.TryGetValue("BackgroundStretchMode", out var stretchValue) &&
                         ToBool(stretchValue))
                {
                    migratedValue = "Stretch";
                }

                settings.AnikiThemeSettingsValues["BackgroundDisplayMode"] = migratedValue;
                settings.AnikiThemeSettingsValues.Remove("SteamBanner");
                settings.AnikiThemeSettingsValues.Remove("BackgroundStretchMode");

                logger?.Info($"[AnikiHelper][Migration] Background display mode migrated to: {migratedValue}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Migration] Failed to migrate background display mode options.");
                return false;
            }
        }

        private bool MigrateLegacyPlatformBannerPositionOption()
        {
            try
            {
                if (settings?.AnikiThemeSettingsValues == null ||
                    currentFile?.Variables == null ||
                    !currentFile.Variables.ContainsKey("PlatformBannerPosition"))
                {
                    return false;
                }

                if (settings.AnikiThemeSettingsValues.ContainsKey("PlatformBannerPosition"))
                {
                    return false;
                }

                var migratedValue = "AboveCover";

                if (settings.AnikiThemeSettingsValues.TryGetValue("PlatformBannerOverlay", out var overlayValue) &&
                    ToBool(overlayValue))
                {
                    migratedValue = "Overlay";
                }

                settings.AnikiThemeSettingsValues["PlatformBannerPosition"] = migratedValue;
                settings.AnikiThemeSettingsValues.Remove("PlatformBannerOverlay");

                logger?.Info($"[AnikiHelper][Migration] Platform banner position migrated to: {migratedValue}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Migration] Failed to migrate platform banner position.");
                return false;
            }
        }

        private bool SanitizeThemeSettingsStorage()
        {
            var changed = false;

            try
            {
                EnsureThemeSettingsDictionaries();

                var validVariableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (currentFile?.Variables != null)
                {
                    foreach (var pair in currentFile.Variables)
                    {
                        var key = pair.Key;
                        var variable = pair.Value;

                        if (string.IsNullOrWhiteSpace(key) || variable == null)
                        {
                            continue;
                        }

                        if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        validVariableIds.Add(key);
                    }
                }

                foreach (var oldKey in settings.AnikiThemeSettingsValues.Keys.ToList())
                {
                    if (!validVariableIds.Contains(oldKey))
                    {
                        settings.AnikiThemeSettingsValues.Remove(oldKey);
                        changed = true;

                        DebugLog($"[AnikiHelper] Removed obsolete Aniki theme option: {oldKey}");
                    }
                }

                if (currentFile?.Variables != null)
                {
                    foreach (var pair in currentFile.Variables)
                    {
                        var key = pair.Key;
                        var variable = pair.Value;

                        if (string.IsNullOrWhiteSpace(key) || variable == null)
                        {
                            continue;
                        }

                        if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var defaultValue = GetDefaultValueString(variable);

                        if (!settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue))
                        {
                            settings.AnikiThemeSettingsValues[key] = defaultValue;
                            changed = true;

                            DebugLog($"[AnikiHelper] Added missing Aniki theme option with default: {key} = {defaultValue}");
                            continue;
                        }

                        if (!IsStoredValueValidForVariable(variable, storedValue))
                        {
                            settings.AnikiThemeSettingsValues[key] = defaultValue;
                            changed = true;

                            logger?.Warn($"[AnikiHelper] Reset invalid Aniki theme option value: {key} = {storedValue} -> {defaultValue}");
                        }
                    }
                }

                // A previous popup build could leave more than four shortcuts enabled.
                // Normalize persisted state once so the top bar always starts from a valid 4-pin maximum.
                var enabledTopBarShortcutCount = 0;
                foreach (var optionKey in TopBarShortcutOptionKeys)
                {
                    if (!settings.AnikiThemeSettingsValues.TryGetValue(optionKey, out var storedTopBarValue) ||
                        !bool.TryParse(storedTopBarValue, out var topBarEnabled) ||
                        !topBarEnabled)
                    {
                        continue;
                    }

                    if (enabledTopBarShortcutCount < TopBarShortcutLimit)
                    {
                        enabledTopBarShortcutCount++;
                        continue;
                    }

                    settings.AnikiThemeSettingsValues[optionKey] = false.ToString();
                    changed = true;
                    DebugLog($"[AnikiHelper] Disabled extra top bar shortcut to enforce the {TopBarShortcutLimit}-pin limit: {optionKey}");
                }

                var validPresetGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (currentFile?.Presets != null)
                {
                    foreach (var pair in currentFile.Presets)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                        {
                            validPresetGroupIds.Add(pair.Key);
                        }
                    }
                }

                foreach (var oldGroupId in settings.AnikiThemeSettingsSelectedPresets.Keys.ToList())
                {
                    if (!validPresetGroupIds.Contains(oldGroupId))
                    {
                        settings.AnikiThemeSettingsSelectedPresets.Remove(oldGroupId);
                        changed = true;

                        DebugLog($"[AnikiHelper] Removed obsolete Aniki preset group selection: {oldGroupId}");
                    }
                }

                if (currentFile?.Presets != null)
                {
                    foreach (var groupPair in currentFile.Presets)
                    {
                        var groupId = groupPair.Key;
                        var group = groupPair.Value;

                        if (string.IsNullOrWhiteSpace(groupId) || group?.Items == null || group.Items.Count == 0)
                        {
                            continue;
                        }

                        var defaultPresetKey = GetDefaultPresetKey(group);

                        if (string.IsNullOrWhiteSpace(defaultPresetKey))
                        {
                            continue;
                        }

                        if (!settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var selectedKey))
                        {
                            settings.AnikiThemeSettingsSelectedPresets[groupId] = defaultPresetKey;
                            changed = true;

                            DebugLog($"[AnikiHelper] Added missing Aniki preset selection with default: {groupId} = {defaultPresetKey}");
                            continue;
                        }

                        var presetStillExists = group.Items.Any(item =>
                            string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase));

                        if (!presetStillExists)
                        {
                            settings.AnikiThemeSettingsSelectedPresets[groupId] = defaultPresetKey;
                            changed = true;

                            logger?.Warn($"[AnikiHelper] Reset invalid Aniki preset selection: {groupId} = {selectedKey} -> {defaultPresetKey}");
                        }
                    }
                }

                return changed;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to sanitize Aniki Theme Settings storage.");
                return changed;
            }
        }

        private void SaveThemeSettingsFile()
        {
            try
            {
                EnsureThemeSettingsDictionaries();

                Directory.CreateDirectory(pluginUserDataPath);

                var storage = new AnikiThemeSettingsStorageFile
                {
                    SchemaVersion = ThemeSettingsSchemaVersion,
                    InitialSetupVersion = initialSetupVersion,
                    InitialSetupOfferVersion = initialSetupOfferVersion,
                    InitialSetupAutomaticRequired = initialSetupAutomaticRequired,
                    Values = CopyDictionary(settings.AnikiThemeSettingsValues),
                    SelectedPresets = CopyDictionary(settings.AnikiThemeSettingsSelectedPresets)
                };

                var json = Serialization.ToJson(storage, true);
                File.WriteAllText(themeSettingsFilePath, json);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save ThemeSettings.json.");
            }
        }

        private void EnsureThemeSettingsDictionaries()
        {
            if (settings.AnikiThemeSettingsValues == null)
            {
                settings.AnikiThemeSettingsValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (settings.AnikiThemeSettingsSelectedPresets == null)
            {
                settings.AnikiThemeSettingsSelectedPresets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private Dictionary<string, string> CopyDictionary(Dictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (source == null)
            {
                return result;
            }

            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                result[pair.Key] = pair.Value ?? string.Empty;
            }

            return result;
        }

        private string GetDefaultValueString(AnikiThemeValue value)
        {
            var effective = value?.EffectiveValue;

            if (!string.IsNullOrWhiteSpace(effective))
            {
                return effective;
            }

            switch ((value?.Type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "boolean":
                case "bool":
                    return "False";

                case "int32":
                case "int":
                case "double":
                case "float":
                case "cornerradius":
                    return "0";

                case "visibility":
                    return "Collapsed";

                case "thickness":
                    return "0";

                case "color":
                case "solidcolorbrush":
                    return "#FFFFFFFF";

                case "timespan":
                    return "00:00:00";

                case "string":
                case "choice":
                case "enum":
                default:
                    return string.Empty;
            }
        }

        private bool IsStoredValueValidForVariable(AnikiThemeVariable variable, string storedValue)
        {
            if (variable == null)
            {
                return false;
            }

            var type = (variable.Type ?? string.Empty).Trim().ToLowerInvariant();

            if (type == "string")
            {
                return true;
            }

            if (type == "choice" || type == "enum")
            {
                return variable.Choices == null ||
                       variable.Choices.Count == 0 ||
                       variable.Choices.Any(choice => choice != null &&
                           string.Equals(choice.Value, storedValue, StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                return true;
            }

            try
            {
                switch (type)
                {
                    case "boolean":
                    case "bool":
                        return bool.TryParse(storedValue, out _);

                    case "int32":
                    case "int":
                        return int.TryParse(storedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

                    case "double":
                    case "float":
                    case "cornerradius":
                        return double.TryParse(storedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

                    case "visibility":
                        return string.Equals(storedValue, "Visible", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(storedValue, "Collapsed", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(storedValue, "Hidden", StringComparison.OrdinalIgnoreCase);

                    case "thickness":
                        return IsValidNumberList(storedValue, 1, 4);

                    case "color":
                    case "solidcolorbrush":
                        ColorConverter.ConvertFromString(storedValue);
                        return true;

                    case "timespan":
                        return TimeSpan.TryParse(storedValue, out _);

                    default:
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidNumberList(string value, int minParts, int maxParts)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(',');

            if (parts.Length < minParts || parts.Length > maxParts)
            {
                return false;
            }

            foreach (var part in parts)
            {
                if (!double.TryParse(part.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private string GetDefaultPresetKey(AnikiPresetGroup group)
        {
            if (group?.Items == null || group.Items.Count == 0)
            {
                return null;
            }

            var selected = group.Items.FirstOrDefault(p =>
                p.Key != null &&
                p.Key.EndsWith("Default", StringComparison.OrdinalIgnoreCase));

            if (selected == null)
            {
                selected = group.Items.FirstOrDefault(p =>
                    string.Equals(p.Key, "Default", StringComparison.OrdinalIgnoreCase));
            }

            return (selected ?? group.Items.FirstOrDefault())?.Key;
        }

        private string ResolveLocKey(string locKey, string fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(locKey) &&
                    Application.Current.TryFindResource(locKey) is string localized &&
                    !string.IsNullOrWhiteSpace(localized))
                {
                    return localized;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private Dictionary<string, string> ParseCommandParameter(string parameter)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(parameter))
            {
                return result;
            }

            var text = parameter.Trim();

            if (text.StartsWith("[") && text.EndsWith("]"))
            {
                text = text.Substring(1, text.Length - 2);
            }

            foreach (var part in text.Split(','))
            {
                var split = part.Split(new[] { '=' }, 2);

                if (split.Length != 2)
                {
                    continue;
                }

                result[split[0].Trim()] = split[1].Trim();
            }

            return result;
        }

        private AnikiPresetItem FindPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId) || currentFile?.Presets == null)
            {
                return null;
            }

            foreach (var group in currentFile.Presets.Values)
            {
                if (group?.Items == null)
                {
                    continue;
                }

                var preset = group.Items.FirstOrDefault(p =>
                    string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));

                if (preset != null)
                {
                    return preset;
                }
            }

            return null;
        }

        private void SynchronizeLoginBackgroundMedia()
        {
            SynchronizeLoginBackgroundMedia(currentThemePath);
        }

        private void SynchronizeLoginBackgroundMedia(string themePath)
        {
            try
            {
                if (loginBackgroundMediaService == null || string.IsNullOrWhiteSpace(themePath) || !Directory.Exists(themePath))
                {
                    return;
                }

                var managedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue("LoginBackground", out var group) &&
                    group?.Items != null)
                {
                    foreach (var preset in group.Items)
                    {
                        if (preset == null ||
                            loginBackgroundMediaService.IsDefaultPreset(preset.Key) ||
                            loginBackgroundMediaService.IsRandomPreset(preset.Key))
                        {
                            continue;
                        }

                        if (loginBackgroundMediaService.TryResolveRequiredVideoFile(preset, themePath, out var fileName))
                        {
                            managedFiles.Add(fileName);
                        }
                    }
                }

                // Akatsuki used to be Random-only. Keep it during migration from older full theme
                // builds so adding the new normal Akatsuki preset cannot make existing users lose it.
                managedFiles.Add("AcceuilAkatsuki.mp4");

                // Legacy CustomLogin.mp4 is intentionally not projected anymore. It may remain
                // in the Helper data folder for non-destructive migration, but it is no longer
                // part of the active login system.
                loginBackgroundMediaService.SynchronizeManagedMedia(themePath, managedFiles);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to synchronize persistent login media.");
            }
        }

        private bool EnsureLegacyCustomLoginSelectionMigrated()
        {
            try
            {
                if (settings?.AnikiThemeSettingsSelectedPresets == null ||
                    !settings.AnikiThemeSettingsSelectedPresets.TryGetValue(LoginBackgroundPresetGroupId, out var selectedPreset) ||
                    (!string.Equals(selectedPreset, LegacyCustomLoginPresetKey, StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(selectedPreset, "Custom", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                settings.AnikiThemeSettingsSelectedPresets[LoginBackgroundPresetGroupId] = LoginBackgroundMediaService.DefaultPresetKey;
                DeactivateLoginPackRuntime();
                logger?.Info("[AnikiHelper][LoginPack] Legacy Custom Login selection migrated to Default.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginPack] Failed to migrate legacy Custom Login selection.");
                return false;
            }
        }

        private bool EnsureManagedLoginSelectionValid()
        {
            try
            {
                if (settings?.AnikiThemeSettingsSelectedPresets == null ||
                    !settings.AnikiThemeSettingsSelectedPresets.TryGetValue("LoginBackground", out var selectedPreset) ||
                    string.IsNullOrWhiteSpace(selectedPreset) ||
                    loginBackgroundMediaService.IsDefaultPreset(selectedPreset) ||
                    loginBackgroundMediaService.IsRandomPreset(selectedPreset) ||
                    loginBackgroundMediaService.IsCustomPreset(selectedPreset) ||
                    TryGetLoginPackId(selectedPreset, out _))
                {
                    return false;
                }

                var themePath = !string.IsNullOrWhiteSpace(currentThemePath)
                    ? currentThemePath
                    : GetFullscreenThemePath();

                var preset = GetPresetItems("LoginBackground")
                    ?.FirstOrDefault(x => x != null && string.Equals(x.Key, selectedPreset, StringComparison.OrdinalIgnoreCase));
                if (preset == null)
                {
                    return false;
                }

                if (!loginBackgroundMediaService.TryResolveRequiredVideoFile(preset, themePath, out var fileName) ||
                    string.IsNullOrWhiteSpace(fileName))
                {
                    return false;
                }

                if (loginBackgroundMediaService.EnsurePersistentVideoProjected(themePath, fileName) ||
                    loginBackgroundMediaService.IsVideoInstalled(themePath, fileName))
                {
                    return false;
                }

                settings.AnikiThemeSettingsSelectedPresets["LoginBackground"] = LoginBackgroundMediaService.DefaultPresetKey;
                logger?.Info("[AnikiHelper][LoginMedia] Managed login video is missing; Login Background was reset to Default.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to validate Login Background selection.");
                return false;
            }
        }

        public string GetLoginBackgroundMediaLibraryFolder()
        {
            return loginBackgroundMediaService?.LibraryFolder ?? string.Empty;
        }

        public int GetDownloadedLoginBackgroundVideosCount()
        {
            return loginBackgroundMediaService?.GetDownloadedVideosCount() ?? 0;
        }

        public long GetDownloadedLoginBackgroundVideosSizeBytes()
        {
            return loginBackgroundMediaService?.GetDownloadedVideosSizeBytes() ?? 0L;
        }

        public void ClearDownloadedLoginBackgroundVideos()
        {
            var themePath = GetFullscreenThemePath();
            var removedFileNames = loginBackgroundMediaService?.ClearDownloadedVideos(themePath) ?? new List<string>();

            try
            {
                if (removedFileNames.Count > 0 && settings?.AnikiThemeSettingsSelectedPresets != null &&
                    settings.AnikiThemeSettingsSelectedPresets.TryGetValue("LoginBackground", out var selectedPreset) &&
                    !string.IsNullOrWhiteSpace(selectedPreset) &&
                    !loginBackgroundMediaService.IsDefaultPreset(selectedPreset) &&
                    !loginBackgroundMediaService.IsRandomPreset(selectedPreset) &&
                    !loginBackgroundMediaService.IsCustomPreset(selectedPreset) &&
                    !TryGetLoginPackId(selectedPreset, out _))
                {
                    var preset = GetPresetItems("LoginBackground")
                        ?.FirstOrDefault(x => x != null && string.Equals(x.Key, selectedPreset, StringComparison.OrdinalIgnoreCase));

                    if (preset != null && loginBackgroundMediaService.TryResolveRequiredVideoFile(preset, themePath, out var fileName) &&
                        removedFileNames.Any(x => string.Equals(x, fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        settings.AnikiThemeSettingsSelectedPresets["LoginBackground"] = LoginBackgroundMediaService.DefaultPresetKey;

                        if (currentFile != null)
                        {
                            SaveSettings();
                            Apply();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to reset Login Background selection after library cleanup.");
            }
        }

        private void RefreshInstalledLoginPackItems()
        {
            try
            {
                loginPackLibrarySnapshot = loginPackImportService.GetLibrary();

                if (currentFile?.Presets == null ||
                    !currentFile.Presets.TryGetValue(LoginBackgroundPresetGroupId, out var group) ||
                    group?.Items == null)
                {
                    return;
                }

                var oldVirtualItems = group.Items
                    .Where(x => x != null && TryGetLoginPackId(x.Key, out _))
                    .ToList();

                foreach (var item in oldVirtualItems)
                {
                    group.Items.Remove(item);
                }

                foreach (var item in BuildInstalledLoginPackItems(LoginBackgroundPresetGroupId, loginPackLibrarySnapshot))
                {
                    group.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginPack] Failed to refresh installed Login Packs for Fullscreen settings.");
                loginPackLibrarySnapshot = loginPackLibrarySnapshot ?? new LoginPackLibrarySnapshot();
            }
        }

        public void RefreshInstalledLoginPacks()
        {
            try
            {
                RefreshInstalledLoginPackItems();

                // Desktop settings can refresh the pack library without having loaded the
                // Fullscreen YAML/runtime yet. Never hydrate/save ThemeSettings.json from that
                // path: the next Fullscreen LoadAndApply() will reconcile a deleted active pack
                // safely after the complete theme settings file has been loaded.
                if (currentFile == null)
                {
                    return;
                }

                if (!EnsureThemeSettingsStorageLoadedForPackOperation())
                {
                    return;
                }

                var selectionChanged = EnsureSelectedLoginPackRuntime();
                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(LoginBackgroundPresetGroupId, out var group) &&
                    group != null)
                {
                    RefreshPresetGroupFilter(LoginBackgroundPresetGroupId, group, true);
                }

                if (selectionChanged)
                {
                    SaveSettings();
                }

                Apply();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginPack] Failed to refresh installed Login Packs.");
            }
        }

        public void SelectInstalledLoginPack(string localId)
        {
            if (string.IsNullOrWhiteSpace(localId))
            {
                return;
            }

            SelectPreset(LoginBackgroundPresetGroupId, GetLoginPackVirtualKey(localId));
        }

        private bool TrySelectInstalledLoginPack(string groupId, string presetKey)
        {
            if (!string.Equals(groupId, LoginBackgroundPresetGroupId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetLoginPackId(presetKey, out var localId))
            {
                return false;
            }

            try
            {
                loginPackImportService.SetActivePack(localId);
                settings.ActiveLoginPackVideoPath = loginPackImportService.GetVideoPath(localId);
                loginPackLibrarySnapshot = loginPackImportService.GetLibrary();

                settings.AnikiThemeSettingsValues[LoginBackgroundFilterVariableId] = LoginPackFilterValue;
                settings.AnikiThemeSettingsSelectedPresets[LoginBackgroundPresetGroupId] = presetKey;
                ApplyOptionDependenciesToStorage();
                SaveSettings();

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(LoginBackgroundPresetGroupId, out var group) &&
                    group != null)
                {
                    group.SetSelectedPresetKeySilently(presetKey);
                    RefreshPresetGroupFilter(LoginBackgroundPresetGroupId, group, false);
                }

                Apply();
                logger?.Info($"[AnikiHelper][LoginPack] Selected installed pack '{localId}' from Fullscreen theme settings.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][LoginPack] Failed to select installed pack '{localId}'.");
                RefreshInstalledLoginPackItems();
                EnsureSelectedLoginPackRuntime();
            }

            return true;
        }

        private bool EnsureSelectedLoginPackRuntime()
        {
            try
            {
                if (settings?.AnikiThemeSettingsSelectedPresets == null ||
                    !settings.AnikiThemeSettingsSelectedPresets.TryGetValue(LoginBackgroundPresetGroupId, out var selectedPreset) ||
                    !TryGetLoginPackId(selectedPreset, out var localId))
                {
                    DeactivateLoginPackRuntime();
                    return false;
                }

                var snapshot = loginPackLibrarySnapshot ?? loginPackImportService.GetLibrary();
                var exists = snapshot?.Packs?.Any(x => x != null &&
                    string.Equals(x.LocalId, localId, StringComparison.OrdinalIgnoreCase)) == true;

                if (!exists)
                {
                    settings.AnikiThemeSettingsSelectedPresets[LoginBackgroundPresetGroupId] = LoginBackgroundMediaService.DefaultPresetKey;
                    DeactivateLoginPackRuntime();
                    logger?.Info("[AnikiHelper][LoginPack] Selected Login Pack is missing; Login Background was reset to Default.");
                    return true;
                }

                if (!string.Equals(snapshot.ActivePackId, localId, StringComparison.OrdinalIgnoreCase))
                {
                    loginPackImportService.SetActivePack(localId);
                    loginPackLibrarySnapshot = loginPackImportService.GetLibrary();
                }

                settings.ActiveLoginPackVideoPath = loginPackImportService.GetVideoPath(localId);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginPack] Failed to synchronize the selected Login Pack runtime; resetting Login Background to Default.");

                if (settings?.AnikiThemeSettingsSelectedPresets != null)
                {
                    settings.AnikiThemeSettingsSelectedPresets[LoginBackgroundPresetGroupId] = LoginBackgroundMediaService.DefaultPresetKey;
                }

                DeactivateLoginPackRuntime();
                return true;
            }
        }

        private void DeactivateLoginPackRuntime()
        {
            try
            {
                loginPackImportService?.ClearActivePack();
                loginPackLibrarySnapshot = loginPackImportService?.GetLibrary();
            }
            catch
            {
            }

            if (settings != null)
            {
                settings.ActiveLoginPackVideoPath = string.Empty;
            }
        }

        private static bool TryGetLoginPackId(string key, out string localId)
        {
            localId = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(LoginPackVirtualKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = key.Substring(LoginPackVirtualKeyPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            localId = value;
            return true;
        }

        private static string GetLoginPackVirtualKey(string localId)
        {
            return string.IsNullOrWhiteSpace(localId)
                ? string.Empty
                : LoginPackVirtualKeyPrefix + localId;
        }

        private static List<AnikiPresetItem> BuildInstalledLoginPackItems(
            string groupId,
            LoginPackLibrarySnapshot snapshot)
        {
            var result = new List<AnikiPresetItem>();
            if (snapshot?.Packs == null)
            {
                return result;
            }

            foreach (var pack in snapshot.Packs)
            {
                if (pack == null || string.IsNullOrWhiteSpace(pack.LocalId))
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(pack.Name) ? pack.LocalId : pack.Name.Trim();
                result.Add(new AnikiPresetItem
                {
                    Id = groupId + "." + GetLoginPackVirtualKey(pack.LocalId),
                    GroupId = groupId,
                    Key = GetLoginPackVirtualKey(pack.LocalId),
                    Name = displayName,
                    LocalizedName = displayName,
                    FilterValue = LoginPackFilterValue,
                    Files = new List<string> { LoginPackThemeFile }
                });
            }

            return result;
        }

        private void RefreshInstalledSoundPackItems()
        {
            try
            {
                soundPackLibrarySnapshot = soundPackImportService.GetLibrary();

                if (currentFile?.Presets == null ||
                    !currentFile.Presets.TryGetValue(SoundPackPresetGroupId, out var group) ||
                    group?.Items == null)
                {
                    return;
                }

                var oldVirtualItems = group.Items
                    .Where(x => x != null && TryGetSoundPackId(x.Key, out _))
                    .ToList();

                foreach (var item in oldVirtualItems)
                {
                    group.Items.Remove(item);
                }

                foreach (var item in BuildInstalledSoundPackItems(SoundPackPresetGroupId, soundPackLibrarySnapshot))
                {
                    group.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][SoundPack] Failed to refresh installed Sound Packs for Fullscreen settings.");
                soundPackLibrarySnapshot = soundPackLibrarySnapshot ?? new SoundPackLibrarySnapshot();
            }
        }

        public void RefreshInstalledSoundPacks()
        {
            try
            {
                RefreshInstalledSoundPackItems();

                // See RefreshInstalledLoginPacks(): a Desktop library refresh must never write
                // Fullscreen ThemeSettings.json. Runtime fallback is deferred to LoadAndApply().
                if (currentFile == null)
                {
                    return;
                }

                if (!EnsureThemeSettingsStorageLoadedForPackOperation())
                {
                    return;
                }

                var selectionChanged = EnsureSelectedSoundPackRuntime();

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(SoundPackPresetGroupId, out var group) &&
                    group != null)
                {
                    RefreshPresetGroupFilter(SoundPackPresetGroupId, group, true);
                }

                if (selectionChanged)
                {
                    SaveSettings();
                }

                Apply();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][SoundPack] Failed to refresh installed Sound Packs.");
            }
        }

        private bool TrySelectInstalledSoundPack(string groupId, string presetKey)
        {
            if (!string.Equals(groupId, SoundPackPresetGroupId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetSoundPackId(presetKey, out var localId))
            {
                return false;
            }

            try
            {
                soundPackImportService.SetActivePack(localId);
                soundPackLibrarySnapshot = soundPackImportService.GetLibrary();
                settings.AnikiThemeSettingsSelectedPresets[SoundPackPresetGroupId] = presetKey;

                UpdateSoundPackRuntimePaths(localId);
                SynchronizeNativeSoundFiles(localId);
                ApplyOptionDependenciesToStorage();
                SaveSettings();

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(SoundPackPresetGroupId, out var group) &&
                    group != null)
                {
                    group.SetSelectedPresetKeySilently(presetKey);
                    RefreshPresetGroupFilter(SoundPackPresetGroupId, group, false);
                }

                MarkRestartRequired();
                Apply();
                logger?.Info($"[AnikiHelper][SoundPack] Selected installed pack '{localId}' from Fullscreen theme settings.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][SoundPack] Failed to select installed pack '{localId}'.");
                RefreshInstalledSoundPackItems();
                EnsureSelectedSoundPackRuntime();
            }

            return true;
        }

        private bool EnsureSelectedSoundPackRuntime()
        {
            try
            {
                if (settings?.AnikiThemeSettingsSelectedPresets == null ||
                    !settings.AnikiThemeSettingsSelectedPresets.TryGetValue(SoundPackPresetGroupId, out var selectedPreset) ||
                    !TryGetSoundPackId(selectedPreset, out var localId))
                {
                    DeactivateSoundPackRuntime();
                    return false;
                }

                var snapshot = soundPackLibrarySnapshot ?? soundPackImportService.GetLibrary();
                var exists = snapshot?.Packs?.Any(x => x != null &&
                    string.Equals(x.LocalId, localId, StringComparison.OrdinalIgnoreCase)) == true;

                if (!exists)
                {
                    settings.AnikiThemeSettingsSelectedPresets[SoundPackPresetGroupId] = DefaultSoundPackPresetKey;
                    DeactivateSoundPackRuntime();
                    logger?.Info("[AnikiHelper][SoundPack] Selected Sound Pack is missing; Sound Pack was reset to Default.");
                    return true;
                }

                if (!string.Equals(snapshot.ActivePackId, localId, StringComparison.OrdinalIgnoreCase))
                {
                    soundPackImportService.SetActivePack(localId);
                    soundPackLibrarySnapshot = soundPackImportService.GetLibrary();
                }

                UpdateSoundPackRuntimePaths(localId);
                SynchronizeNativeSoundFiles(localId);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][SoundPack] Failed to synchronize the selected Sound Pack runtime; resetting to Default.");

                if (settings?.AnikiThemeSettingsSelectedPresets != null)
                {
                    settings.AnikiThemeSettingsSelectedPresets[SoundPackPresetGroupId] = DefaultSoundPackPresetKey;
                }

                DeactivateSoundPackRuntime();
                return true;
            }
        }

        private void DeactivateSoundPackRuntime()
        {
            try
            {
                soundPackImportService?.ClearActivePack();
                soundPackLibrarySnapshot = soundPackImportService?.GetLibrary();
            }
            catch
            {
            }

            UpdateSoundPackRuntimePaths(null);
            SynchronizeNativeSoundFiles(null);
        }

        private static bool TryGetSoundPackId(string key, out string localId)
        {
            localId = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(SoundPackVirtualKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = key.Substring(SoundPackVirtualKeyPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            localId = value;
            return true;
        }

        private static string GetSoundPackVirtualKey(string localId)
        {
            return string.IsNullOrWhiteSpace(localId)
                ? string.Empty
                : SoundPackVirtualKeyPrefix + localId;
        }

        private static List<AnikiPresetItem> BuildInstalledSoundPackItems(
            string groupId,
            SoundPackLibrarySnapshot snapshot)
        {
            var result = new List<AnikiPresetItem>();
            if (snapshot?.Packs == null)
            {
                return result;
            }

            foreach (var pack in snapshot.Packs)
            {
                if (pack == null || string.IsNullOrWhiteSpace(pack.LocalId))
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(pack.Name) ? pack.LocalId : pack.Name.Trim();
                result.Add(new AnikiPresetItem
                {
                    Id = groupId + "." + GetSoundPackVirtualKey(pack.LocalId),
                    GroupId = groupId,
                    Key = GetSoundPackVirtualKey(pack.LocalId),
                    Name = displayName,
                    LocalizedName = displayName
                });
            }

            return result;
        }

        private void RefreshInstalledCompletePackItems()
        {
            try
            {
                completePackLibrarySnapshot = completePackImportService.GetLibrary();

                if (currentFile?.Presets == null ||
                    !currentFile.Presets.TryGetValue(CompletePackPresetGroupId, out var group) ||
                    group?.Items == null)
                {
                    return;
                }

                var oldVirtualItems = group.Items
                    .Where(x => x != null && TryGetCompletePackId(x.Key, out _))
                    .ToList();

                foreach (var item in oldVirtualItems)
                {
                    group.Items.Remove(item);
                }

                foreach (var item in BuildInstalledCompletePackItems(CompletePackPresetGroupId, completePackLibrarySnapshot))
                {
                    group.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to refresh installed Complete Packs for Fullscreen settings.");
                completePackLibrarySnapshot = completePackLibrarySnapshot ?? new CompletePackLibrarySnapshot();
            }
        }

        public void RefreshInstalledCompletePacks()
        {
            try
            {
                RefreshInstalledCompletePackItems();

                // Import/delete operations from Desktop only update the Complete Pack library.
                // Do not touch ThemeSettings.json until Fullscreen has loaded the YAML/runtime.
                if (currentFile == null)
                {
                    return;
                }

                if (!EnsureThemeSettingsStorageLoadedForPackOperation())
                {
                    return;
                }

                var selectionChanged = EnsureSelectedCompletePackRuntime();

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(CompletePackPresetGroupId, out var group) &&
                    group != null)
                {
                    RefreshPresetGroupFilter(CompletePackPresetGroupId, group, true);
                }

                if (selectionChanged)
                {
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to refresh installed Complete Packs.");
            }
        }

        private bool TrySelectInstalledCompletePack(string groupId, string presetKey)
        {
            if (!string.Equals(groupId, CompletePackPresetGroupId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetCompletePackId(presetKey, out var localId))
            {
                return false;
            }

            try
            {
                var selection = completePackImportService.PrepareApply(localId);
                ApplyCompletePack(selection);
                logger?.Info($"[AnikiHelper][CompletePack] Selected installed pack '{localId}' from Fullscreen theme settings.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][CompletePack] Failed to apply installed pack '{localId}'.");
                RefreshInstalledCompletePackItems();
                EnsureSelectedCompletePackRuntime();
                playniteApi?.Dialogs?.ShowErrorMessage(
                    "The Complete Pack could not be applied:" + Environment.NewLine + ex.Message,
                    "Aniki Helper");
            }

            return true;
        }

        public void ApplyCompletePack(CompletePackApplySelection selection)
        {
            if (selection == null || string.IsNullOrWhiteSpace(selection.CompletePackLocalId))
            {
                throw new ArgumentException("A Complete Pack selection is required.", nameof(selection));
            }

            if (!EnsureThemeSettingsStorageLoadedForPackOperation())
            {
                throw new InvalidOperationException("Aniki ReMake theme settings could not be loaded.");
            }

            applyingCompletePack = true;
            try
            {
                // The component importers may have just added or updated packs. Refresh the
                // dynamic Fullscreen lists before selecting their virtual keys.
                customVisualPackLibrarySnapshot = visualPackImportService.GetLibrary();
                customColorPackLibrarySnapshot = colorPackImportService.GetLibrary();
                RefreshInstalledLoginPackItems();
                RefreshInstalledSoundPackItems();

                if (selection.HasVisualPack)
                {
                    TrySelectInstalledCustomVisualPack(
                        VisualPackPresetGroupId,
                        GetCustomVisualPackVirtualKey(selection.VisualPackLocalId));
                    AssertComponentActive(
                        "Visual Pack",
                        selection.VisualPackLocalId,
                        visualPackImportService.GetLibrary()?.ActivePackId);
                }

                if (selection.HasColorPack)
                {
                    TrySelectInstalledCustomColorPack(
                        ThemeColorPresetGroupId,
                        GetCustomColorPackVirtualKey(selection.ColorPackLocalId));
                    AssertComponentActive(
                        "Color Pack",
                        selection.ColorPackLocalId,
                        colorPackImportService.GetLibrary()?.ActivePackId);
                }

                if (selection.HasLoginPack)
                {
                    TrySelectInstalledLoginPack(
                        LoginBackgroundPresetGroupId,
                        GetLoginPackVirtualKey(selection.LoginPackLocalId));
                    AssertComponentActive(
                        "Login Pack",
                        selection.LoginPackLocalId,
                        loginPackImportService.GetLibrary()?.ActivePackId);
                }

                if (selection.HasSoundPack)
                {
                    TrySelectInstalledSoundPack(
                        SoundPackPresetGroupId,
                        GetSoundPackVirtualKey(selection.SoundPackLocalId));
                    AssertComponentActive(
                        "Sound Pack",
                        selection.SoundPackLocalId,
                        soundPackImportService.GetLibrary()?.ActivePackId);
                }

                completePackImportService.SetActivePack(selection.CompletePackLocalId);
                completePackLibrarySnapshot = completePackImportService.GetLibrary();
                var completeKey = GetCompletePackVirtualKey(selection.CompletePackLocalId);
                settings.AnikiThemeSettingsSelectedPresets[CompletePackPresetGroupId] = completeKey;
                SaveSettings();

                RefreshInstalledCompletePackItems();
                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(CompletePackPresetGroupId, out var group) &&
                    group != null)
                {
                    group.SetSelectedPresetKeySilently(completeKey);
                    RefreshPresetGroupFilter(CompletePackPresetGroupId, group, false);
                }

                Apply();
                logger?.Info($"[AnikiHelper][CompletePack] Applied Complete Pack '{selection.CompletePackLocalId}'.");
            }
            finally
            {
                applyingCompletePack = false;
            }
        }

        private static void AssertComponentActive(string label, string expectedLocalId, string actualLocalId)
        {
            if (!string.Equals(expectedLocalId ?? string.Empty, actualLocalId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(label + " could not be activated while applying the Complete Pack.");
            }
        }

        private bool EnsureSelectedCompletePackRuntime()
        {
            try
            {
                if (settings?.AnikiThemeSettingsSelectedPresets == null ||
                    !settings.AnikiThemeSettingsSelectedPresets.TryGetValue(CompletePackPresetGroupId, out var selectedPreset) ||
                    !TryGetCompletePackId(selectedPreset, out var localId))
                {
                    completePackImportService.ClearActivePack();
                    completePackLibrarySnapshot = completePackImportService.GetLibrary();
                    return false;
                }

                var snapshot = completePackLibrarySnapshot ?? completePackImportService.GetLibrary();
                var exists = snapshot?.Packs?.Any(x => x != null &&
                    string.Equals(x.LocalId, localId, StringComparison.OrdinalIgnoreCase)) == true;

                if (!exists)
                {
                    settings.AnikiThemeSettingsSelectedPresets[CompletePackPresetGroupId] = NoCompletePackPresetKey;
                    completePackImportService.ClearActivePack();
                    completePackLibrarySnapshot = completePackImportService.GetLibrary();
                    logger?.Info("[AnikiHelper][CompletePack] Selected Complete Pack is missing; state was reset to No Complete Pack.");
                    return true;
                }

                if (!string.Equals(snapshot.ActivePackId, localId, StringComparison.OrdinalIgnoreCase))
                {
                    // The Complete Pack index is authoritative for whether a bundle has really
                    // been applied. This also handles an imported update: the bundle stays
                    // installed but is no longer marked active until Apply is pressed again.
                    if (string.IsNullOrWhiteSpace(snapshot.ActivePackId))
                    {
                        settings.AnikiThemeSettingsSelectedPresets[CompletePackPresetGroupId] = NoCompletePackPresetKey;
                    }
                    else
                    {
                        var activeExists = snapshot.Packs?.Any(x => x != null &&
                            string.Equals(x.LocalId, snapshot.ActivePackId, StringComparison.OrdinalIgnoreCase)) == true;
                        settings.AnikiThemeSettingsSelectedPresets[CompletePackPresetGroupId] = activeExists
                            ? GetCompletePackVirtualKey(snapshot.ActivePackId)
                            : NoCompletePackPresetKey;
                    }
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to synchronize Complete Pack state.");
                settings.AnikiThemeSettingsSelectedPresets[CompletePackPresetGroupId] = NoCompletePackPresetKey;
                try { completePackImportService.ClearActivePack(); } catch { }
                return true;
            }
        }

        public void NotifyCompletePackComponentChanged(string componentName)
        {
            // In Desktop mode the Fullscreen YAML/runtime is deliberately not loaded. Clearing
            // the Complete Pack library's active marker is enough; persisting the corresponding
            // ThemeSettings selection is deferred until Fullscreen LoadAndApply(). This prevents
            // a pack deletion from ever replacing the user's full ThemeSettings.json with a
            // partially initialized Desktop state (username/avatar/setup wizard included).
            if (currentFile == null)
            {
                try
                {
                    completePackImportService?.ClearActivePack();
                    completePackLibrarySnapshot = completePackImportService?.GetLibrary();
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to clear Complete Pack runtime state from Desktop.");
                }

                return;
            }

            if (!EnsureThemeSettingsStorageLoadedForPackOperation())
            {
                return;
            }

            switch ((componentName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "visual":
                    ClearActiveCompletePackForComponentSelection(VisualPackPresetGroupId);
                    break;
                case "color":
                    ClearActiveCompletePackForComponentSelection(ThemeColorPresetGroupId);
                    break;
                case "login":
                    ClearActiveCompletePackForComponentSelection(LoginBackgroundPresetGroupId);
                    break;
                case "sound":
                    ClearActiveCompletePackForComponentSelection(SoundPackPresetGroupId);
                    break;
            }
        }

        private void ClearActiveCompletePackForComponentSelection(string groupId)
        {
            if (applyingCompletePack || completePackImportService == null)
            {
                return;
            }

            string componentName = null;
            if (string.Equals(groupId, VisualPackPresetGroupId, StringComparison.OrdinalIgnoreCase))
            {
                componentName = "visual";
            }
            else if (string.Equals(groupId, ThemeColorPresetGroupId, StringComparison.OrdinalIgnoreCase))
            {
                componentName = "color";
            }
            else if (string.Equals(groupId, LoginBackgroundPresetGroupId, StringComparison.OrdinalIgnoreCase))
            {
                componentName = "login";
            }
            else if (string.Equals(groupId, SoundPackPresetGroupId, StringComparison.OrdinalIgnoreCase))
            {
                componentName = "sound";
            }

            if (string.IsNullOrWhiteSpace(componentName))
            {
                return;
            }

            try
            {
                completePackImportService.ClearActivePack();
                completePackLibrarySnapshot = completePackImportService.GetLibrary();
                settings.AnikiThemeSettingsSelectedPresets[CompletePackPresetGroupId] = NoCompletePackPresetKey;

                if (currentFile?.Presets != null &&
                    currentFile.Presets.TryGetValue(CompletePackPresetGroupId, out var group) &&
                    group != null)
                {
                    group.SetSelectedPresetKeySilently(NoCompletePackPresetKey);
                }

                SaveSettings();
                logger?.Info($"[AnikiHelper][CompletePack] Complete Pack state cleared because its {componentName} component was changed manually.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to clear Complete Pack state after a component change.");
            }
        }

        private static bool TryGetCompletePackId(string key, out string localId)
        {
            localId = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(CompletePackVirtualKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = key.Substring(CompletePackVirtualKeyPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            localId = value;
            return true;
        }

        private static string GetCompletePackVirtualKey(string localId)
        {
            return string.IsNullOrWhiteSpace(localId)
                ? string.Empty
                : CompletePackVirtualKeyPrefix + localId;
        }

        private static List<AnikiPresetItem> BuildInstalledCompletePackItems(
            string groupId,
            CompletePackLibrarySnapshot snapshot)
        {
            var result = new List<AnikiPresetItem>();
            if (snapshot?.Packs == null)
            {
                return result;
            }

            foreach (var pack in snapshot.Packs)
            {
                if (pack == null || string.IsNullOrWhiteSpace(pack.LocalId))
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(pack.Name) ? pack.LocalId : pack.Name.Trim();
                result.Add(new AnikiPresetItem
                {
                    Id = groupId + "." + GetCompletePackVirtualKey(pack.LocalId),
                    GroupId = groupId,
                    Key = GetCompletePackVirtualKey(pack.LocalId),
                    Name = displayName,
                    LocalizedName = displayName
                });
            }

            return result;
        }

        private void UpdateSoundPackRuntimePaths(string localId)
        {
            if (settings == null)
            {
                return;
            }

            var themePath = !string.IsNullOrWhiteSpace(currentThemePath)
                ? currentThemePath
                : GetFullscreenThemePath();
            var audioRoot = string.IsNullOrWhiteSpace(themePath)
                ? string.Empty
                : Path.Combine(themePath, "audio");

            // Lucky Day has priority over user Sound Packs. These roots let the settings
            // bindings switch immediately to Lucky/default theme audio without mutating
            // the selected Sound Pack or its stored runtime paths.
            settings.SoundPackDefaultAudioRoot = audioRoot;
            settings.SoundPackLuckyAudioRoot = string.IsNullOrWhiteSpace(audioRoot)
                ? string.Empty
                : Path.Combine(audioRoot, "Lucky");

            settings.SoundPackNotiPath = ResolveSoundPackOrDefaultPath(localId, "Noti.wav", audioRoot);
            settings.SoundPackEnterGameDetailsPath = ResolveSoundPackOrDefaultPath(localId, "EnterGameDetails.wav", audioRoot);
            settings.SoundPackExitGameDetailsPath = ResolveSoundPackOrDefaultPath(localId, "ExitGameDetails.wav", audioRoot);
            settings.SoundPackLoginConfirmPath = ResolveSoundPackOrDefaultPath(localId, "LoginConfirm.wav", audioRoot);
            settings.SoundPackOpenPanelPath = ResolveSoundPackOrDefaultPath(localId, "OpenPanel.wav", audioRoot);
            settings.SoundPackOpenAdditionalViewPath = ResolveSoundPackOrDefaultPath(localId, "OpenAdditionalView.wav", audioRoot);
            settings.SoundPackCloseAdditionalViewPath = ResolveSoundPackOrDefaultPath(localId, "CloseAdditionalView.wav", audioRoot);
            settings.SoundPackHomeHubOpenPath = ResolveSoundPackOrDefaultPath(localId, "HomeHubOpen.wav", audioRoot);
            settings.SoundPackHomeHubClosePath = ResolveSoundPackOrDefaultPath(localId, "HomeHubClose.wav", audioRoot);
            settings.SoundPackSessionSummaryPath = ResolveSoundPackOrDefaultPath(localId, "SessionSummary.wav", audioRoot);
            settings.SoundPackWarningPath = ResolveSoundPackOrDefaultPath(localId, "Warning.wav", audioRoot);
            settings.SoundPackLoginOstPath = ResolveSoundPackOrDefaultPath(localId, "LoginOST.mp3", audioRoot);
            settings.SoundPackHubOstPath = ResolveSoundPackOrDefaultPath(localId, "HubOST.mp3", audioRoot);
            settings.SoundPackSecondaryViewsOstPath = ResolveSoundPackOrDefaultPath(localId, "SecondaryViewsOST.mp3", audioRoot);
            settings.SoundPackScreenSaverOstPath = ResolveSoundPackOrDefaultPath(localId, "ScreenSaverOST.mp3", audioRoot);
        }

        private string ResolveSoundPackOrDefaultPath(string localId, string fileName, string audioRoot)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(localId))
                {
                    var customPath = soundPackImportService?.GetAudioPath(localId, fileName);
                    if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                    {
                        return customPath;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][SoundPack] Failed to resolve '{fileName}' from the active Sound Pack.");
            }

            if (string.IsNullOrWhiteSpace(audioRoot))
            {
                return string.Empty;
            }

            return Path.Combine(audioRoot, fileName);
        }

        private void TrySynchronizeNativeSoundFilesEarly()
        {
            try
            {
                var snapshot = soundPackImportService?.GetLibrary();
                if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ActivePackId))
                {
                    return;
                }

                SynchronizeNativeSoundFiles(snapshot.ActivePackId, GetFullscreenThemePath());
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][SoundPack] Early native sound synchronization failed.");
            }
        }

        private void SynchronizeNativeSoundFiles(string localId, string themePathOverride = null)
        {
            try
            {
                var themePath = !string.IsNullOrWhiteSpace(themePathOverride)
                    ? themePathOverride
                    : (!string.IsNullOrWhiteSpace(currentThemePath) ? currentThemePath : GetFullscreenThemePath());
                if (string.IsNullOrWhiteSpace(themePath))
                {
                    return;
                }

                var audioRoot = Path.Combine(themePath, "audio");
                var defaultsRoot = Path.Combine(audioRoot, "defaults");
                if (!Directory.Exists(audioRoot))
                {
                    return;
                }

                SynchronizeNativeSoundFile(localId, "navigation.wav", audioRoot, defaultsRoot);
                SynchronizeNativeSoundFile(localId, "activation.wav", audioRoot, defaultsRoot);
                SynchronizeNativeSoundFile(localId, "OpenAdditionalView.wav", audioRoot, defaultsRoot);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][SoundPack] Failed to synchronize Playnite native sound files.");
            }
        }

        private void SynchronizeNativeSoundFile(string localId, string fileName, string audioRoot, string defaultsRoot)
        {
            var targetPath = Path.Combine(audioRoot, fileName);
            var defaultPath = Path.Combine(defaultsRoot, fileName);
            string sourcePath = null;

            if (!string.IsNullOrWhiteSpace(localId))
            {
                try
                {
                    var customPath = soundPackImportService?.GetAudioPath(localId, fileName);
                    if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                    {
                        sourcePath = customPath;
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, $"[AnikiHelper][SoundPack] Failed to resolve native sound '{fileName}'.");
                }
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                if (File.Exists(defaultPath))
                {
                    sourcePath = defaultPath;
                }
                else
                {
                    // Compatibility for installations where only the Helper was updated first.
                    // Never snapshot a potentially customized working file while a pack is active.
                    if (string.IsNullOrWhiteSpace(localId) && File.Exists(targetPath))
                    {
                        Directory.CreateDirectory(defaultsRoot);
                        File.Copy(targetPath, defaultPath, true);
                        sourcePath = defaultPath;
                    }
                    else
                    {
                        logger?.Warn($"[AnikiHelper][SoundPack] Default native sound is missing: {defaultPath}");
                        return;
                    }
                }
            }

            if (File.Exists(targetPath) && AreFilesIdentical(sourcePath, targetPath))
            {
                return;
            }

            Directory.CreateDirectory(audioRoot);
            File.Copy(sourcePath, targetPath, true);
            logger?.Info($"[AnikiHelper][SoundPack] Synchronized native sound '{fileName}'.");
        }

        private static bool AreFilesIdentical(string firstPath, string secondPath)
        {
            try
            {
                var firstInfo = new FileInfo(firstPath);
                var secondInfo = new FileInfo(secondPath);
                if (!firstInfo.Exists || !secondInfo.Exists || firstInfo.Length != secondInfo.Length)
                {
                    return false;
                }

                const int bufferSize = 8192;
                var firstBuffer = new byte[bufferSize];
                var secondBuffer = new byte[bufferSize];
                using (var first = File.OpenRead(firstPath))
                using (var second = File.OpenRead(secondPath))
                {
                    while (true)
                    {
                        var firstRead = first.Read(firstBuffer, 0, firstBuffer.Length);
                        var secondRead = second.Read(secondBuffer, 0, secondBuffer.Length);
                        if (firstRead != secondRead)
                        {
                            return false;
                        }

                        if (firstRead == 0)
                        {
                            return true;
                        }

                        for (var i = 0; i < firstRead; i++)
                        {
                            if (firstBuffer[i] != secondBuffer[i])
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private string GetFullscreenThemePath()
        {
            try
            {
                var themeId = playniteApi?.ApplicationSettings?.FullscreenTheme;
                if (string.IsNullOrWhiteSpace(themeId))
                {
                    return null;
                }

                var roots = new List<string>();

                if (playniteApi?.ApplicationInfo?.IsPortable != true)
                {
                    roots.Add(playniteApi.Paths.ConfigurationPath);
                }

                roots.Add(playniteApi.Paths.ApplicationPath);

                foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var themesFolder = Path.Combine(root, "Themes", "Fullscreen");
                    if (!Directory.Exists(themesFolder))
                    {
                        continue;
                    }

                    foreach (var themeDir in Directory.EnumerateDirectories(themesFolder))
                    {
                        var themeFile = Path.Combine(themeDir, "theme.yaml");
                        if (!File.Exists(themeFile))
                        {
                            continue;
                        }

                        try
                        {
                            var data = Serialization.FromYamlFile<Dictionary<string, object>>(themeFile);
                            if (data != null &&
                                data.TryGetValue("Id", out var idValue) &&
                                string.Equals(idValue?.ToString(), themeId, StringComparison.OrdinalIgnoreCase))
                            {
                                return themeDir;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to detect Fullscreen theme path.");
            }

            return null;
        }

        private string GetCurrentThemePath()
        {
            try
            {
                var themeId = playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? playniteApi.ApplicationSettings.FullscreenTheme
                    : playniteApi.ApplicationSettings.DesktopTheme;

                if (string.IsNullOrWhiteSpace(themeId))
                {
                    return null;
                }

                var roots = new List<string>();

                if (!playniteApi.ApplicationInfo.IsPortable)
                {
                    roots.Add(playniteApi.Paths.ConfigurationPath);
                }

                roots.Add(playniteApi.Paths.ApplicationPath);

                var modeFolder = playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? "Fullscreen"
                    : "Desktop";

                foreach (var root in roots)
                {
                    var themesFolder = Path.Combine(root, "Themes", modeFolder);

                    if (!Directory.Exists(themesFolder))
                    {
                        continue;
                    }

                    foreach (var themeDir in Directory.EnumerateDirectories(themesFolder))
                    {
                        var themeFile = Path.Combine(themeDir, "theme.yaml");

                        if (!File.Exists(themeFile))
                        {
                            continue;
                        }

                        try
                        {
                            var data = Serialization.FromYamlFile<Dictionary<string, object>>(themeFile);

                            if (data != null &&
                                data.TryGetValue("Id", out var idValue) &&
                                string.Equals(idValue?.ToString(), themeId, StringComparison.OrdinalIgnoreCase))
                            {
                                return themeDir;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to detect current theme path.");
            }

            return null;
        }

        private void PostLoadPresets()
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var groupId = groupPair.Key;
                var group = groupPair.Value;

                if (group == null)
                {
                    continue;
                }

                group.Id = groupId;
                group.Items.Clear();
                group.FilteredItems.Clear();
                group.LocalizedName = ResolveLocKey(
                    group.LocKey,
                    !string.IsNullOrWhiteSpace(group.Title) ? group.Title :
                    !string.IsNullOrWhiteSpace(group.Name) ? group.Name :
                    group.Id);

                group.LocalizedDescription = ResolveLocKey(
                    group.DescriptionLocKey,
                    group.Description);

                group.EmptySelectionText = string.IsNullOrWhiteSpace(group.EmptySelectionLocKey)
                    ? string.Empty
                    : ResolveLocKey(group.EmptySelectionLocKey, "Select a Visual Pack...");

                group.SelectionChangedAction = (changedGroupId, selectedPresetKey) =>
                {
                    SelectPreset(changedGroupId, selectedPresetKey);
                };

                if (group.Presets == null)
                {
                    continue;
                }

                foreach (var presetPair in group.Presets)
                {
                    var presetKey = presetPair.Key;
                    var preset = presetPair.Value;

                    if (preset == null)
                    {
                        continue;
                    }

                    preset.GroupId = groupId;
                    preset.Key = presetKey;
                    preset.Id = groupId + "." + presetKey;
                    preset.LocalizedName = ResolveLocKey(
                        preset.LocKey,
                        !string.IsNullOrWhiteSpace(preset.Title) ? preset.Title :
                        !string.IsNullOrWhiteSpace(preset.Name) ? preset.Name :
                        preset.Key);

                    if (string.IsNullOrWhiteSpace(preset.Category))
                    {
                        preset.Category = group.Category;
                    }

                    if (!string.IsNullOrWhiteSpace(preset.Preview))
                    {
                        var previewPath = Path.Combine(currentThemePath, preset.Preview);
                        preset.Preview = File.Exists(previewPath) ? previewPath : null;
                    }

                    group.Items.Add(preset);
                    group.FilteredItems.Add(preset);
                }
            }
        }

        private bool MigratePresetFilterSelections()
        {
            var changed = false;

            try
            {
                if (currentFile?.Presets == null || currentFile.Variables == null)
                {
                    return false;
                }

                // Login1 used to live inside the Standard category even though it is the
                // Random-at-each-launch preset. Preserve existing Random users when the
                // theme moves Random to its own first-level category.
                if (settings.AnikiThemeSettingsSelectedPresets.TryGetValue(LoginBackgroundPresetGroupId, out var legacyLoginPreset) &&
                    string.Equals(legacyLoginPreset, LoginBackgroundMediaService.RandomPresetKey, StringComparison.OrdinalIgnoreCase) &&
                    (!settings.AnikiThemeSettingsValues.TryGetValue(LoginBackgroundFilterVariableId, out var storedLoginFilter) ||
                     !string.Equals(storedLoginFilter, RandomLoginFilterValue, StringComparison.OrdinalIgnoreCase)))
                {
                    settings.AnikiThemeSettingsValues[LoginBackgroundFilterVariableId] = RandomLoginFilterValue;
                    changed = true;
                    DebugLog("[AnikiHelper] Migrated LoginBackgroundType to Random for the existing Random Login preset.");
                }

                foreach (var groupPair in currentFile.Presets)
                {
                    var groupId = groupPair.Key;
                    var group = groupPair.Value;

                    if (group == null || string.IsNullOrWhiteSpace(group.FilterBy) ||
                        !currentFile.Variables.TryGetValue(group.FilterBy, out var filterVariable) ||
                        filterVariable == null ||
                        settings.AnikiThemeSettingsValues.ContainsKey(group.FilterBy))
                    {
                        continue;
                    }

                    var selectedPreset = GetSelectedPreset(groupId, group);
                    var inferredFilter = GetPresetFilterValue(selectedPreset);

                    if (string.IsNullOrWhiteSpace(inferredFilter))
                    {
                        inferredFilter = GetDefaultValueString(filterVariable);
                    }

                    if (!string.IsNullOrWhiteSpace(inferredFilter) &&
                        IsStoredValueValidForVariable(filterVariable, inferredFilter))
                    {
                        settings.AnikiThemeSettingsValues[group.FilterBy] = inferredFilter;
                        changed = true;
                        DebugLog($"[AnikiHelper] Migrated preset filter {group.FilterBy} = {inferredFilter} from {groupId}.{selectedPreset?.Key}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to migrate preset filters.");
            }

            return changed;
        }

        private bool RefreshAllPresetFilters(bool ensureValidSelection)
        {
            var changed = false;

            if (currentFile?.Presets == null)
            {
                return false;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                changed = RefreshPresetGroupFilter(groupPair.Key, groupPair.Value, ensureValidSelection) || changed;
            }

            return changed;
        }

        private bool RefreshPresetFiltersForVariable(string variableId, bool ensureValidSelection)
        {
            var changed = false;

            if (string.IsNullOrWhiteSpace(variableId) || currentFile?.Presets == null)
            {
                return false;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var group = groupPair.Value;
                if (group == null ||
                    !string.Equals(group.FilterBy, variableId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                changed = RefreshPresetGroupFilter(groupPair.Key, group, ensureValidSelection) || changed;
            }

            return changed;
        }

        private bool RefreshPresetGroupFilter(string groupId, AnikiPresetGroup group, bool ensureValidSelection)
        {
            if (group?.Items == null || group.FilteredItems == null)
            {
                return false;
            }

            var visibleItems = group.Items.ToList();
            var filterValue = string.Empty;
            var isCustomVisualPackLibrary = false;
            var isRandomVisualPackMode = false;
            var isCustomColorPackLibrary = false;
            var isRandomThemeColorMode = false;
            var isDynamicThemeColorMode = false;
            var isLoginPackLibrary = false;
            VisualPackLibrarySnapshot customLibrary = null;
            ColorPackLibrarySnapshot customColorLibrary = null;
            LoginPackLibrarySnapshot loginLibrary = null;

            if (!string.IsNullOrWhiteSpace(group.FilterBy) &&
                currentFile?.Variables != null &&
                currentFile.Variables.TryGetValue(group.FilterBy, out var filterVariable) &&
                filterVariable != null)
            {
                filterValue = GetStoredValueOrDefault(group.FilterBy, filterVariable) ?? string.Empty;
                isCustomVisualPackLibrary = IsCustomVisualPackLibraryFilter(groupId, filterValue);
                isRandomVisualPackMode = string.Equals(groupId, VisualPackPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(filterValue, RandomVisualPackFilterValue, StringComparison.OrdinalIgnoreCase);
                isCustomColorPackLibrary = IsCustomColorPackLibraryFilter(groupId, filterValue);
                isRandomThemeColorMode = string.Equals(groupId, ThemeColorPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(filterValue, RandomThemeColorFilterValue, StringComparison.OrdinalIgnoreCase);
                isDynamicThemeColorMode = string.Equals(groupId, ThemeColorPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                                          string.Equals(filterValue, "Dynamic", StringComparison.OrdinalIgnoreCase);
                isLoginPackLibrary = IsLoginPackLibraryFilter(groupId, filterValue);

                if (isRandomVisualPackMode)
                {
                    // Random is a first-level mode, not a Visual Pack preset. Keep the user's
                    // previous concrete selection persisted and replace the selector with the
                    // Configure Random Pool button in Fullscreen Settings.
                    visibleItems = new List<AnikiPresetItem>();
                }
                else if (isRandomThemeColorMode)
                {
                    // Random is a first-level mode, not a color preset. Keep the previous
                    // concrete Theme Color selection persisted and replace the selector with
                    // the Configure Random Pool button in Fullscreen Settings.
                    visibleItems = new List<AnikiPresetItem>();
                }
                else if (isCustomVisualPackLibrary)
                {
                    customLibrary = RefreshCustomVisualPackLibrarySnapshot();
                    visibleItems = BuildInstalledCustomVisualPackItems(groupId, customLibrary);
                }
                else if (isCustomColorPackLibrary)
                {
                    customColorLibrary = RefreshCustomColorPackLibrarySnapshot();
                    visibleItems = BuildInstalledCustomColorPackItems(groupId, customColorLibrary);
                }
                else if (isLoginPackLibrary)
                {
                    loginLibrary = loginPackLibrarySnapshot ?? loginPackImportService.GetLibrary();
                    visibleItems = BuildInstalledLoginPackItems(groupId, loginLibrary);
                }
                else
                {
                    var filtered = group.Items
                        .Where(item => item != null &&
                                       string.Equals(GetPresetFilterValue(item), filterValue, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    // A malformed or older theme must never leave the preset selector empty.
                    if (filtered.Count > 0)
                    {
                        visibleItems = filtered;
                    }
                }
            }

            // Custom is no longer a disabled single preset. The existing selector becomes the
            // installed-pack selector while the real persisted theme preset remains "Custom".
            group.IsSelectionEnabled = !isDynamicThemeColorMode &&
                                       (isCustomVisualPackLibrary ||
                                        isCustomColorPackLibrary ||
                                        isLoginPackLibrary ||
                                        string.IsNullOrWhiteSpace(group.DisableSelectionWhenFilterValue) ||
                                        !string.Equals(filterValue, group.DisableSelectionWhenFilterValue, StringComparison.OrdinalIgnoreCase));

            var sameItems = group.FilteredItems.Count == visibleItems.Count &&
                            group.FilteredItems.Select(x => x?.Key).SequenceEqual(visibleItems.Select(x => x?.Key), StringComparer.OrdinalIgnoreCase);

            if (!sameItems)
            {
                group.FilteredItems.Clear();
                foreach (var item in visibleItems)
                {
                    group.FilteredItems.Add(item);
                }
            }

            var changed = false;

            if (isRandomVisualPackMode)
            {
                settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var storedPreset);
                group.HasVisibleSelection = false;
                group.SetSelectedPresetKeySilently(storedPreset ?? string.Empty);
                return false;
            }

            if (isRandomThemeColorMode)
            {
                settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var storedPreset);
                group.HasVisibleSelection = false;
                group.SetSelectedPresetKeySilently(storedPreset ?? string.Empty);
                return false;
            }

            if (isCustomVisualPackLibrary)
            {
                if (!settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var storedThemePreset) ||
                    !string.Equals(storedThemePreset, CustomVisualPackPresetKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.AnikiThemeSettingsSelectedPresets[groupId] = CustomVisualPackPresetKey;
                    changed = true;
                }

                var activeVirtualKey = GetCustomVisualPackVirtualKey(customLibrary?.ActivePackId);
                var selectedVisible = visibleItems.FirstOrDefault(item => item != null &&
                    string.Equals(item.Key, activeVirtualKey, StringComparison.OrdinalIgnoreCase));

                group.HasVisibleSelection = selectedVisible != null;
                group.SetSelectedPresetKeySilently(selectedVisible?.Key ?? string.Empty);
                return changed;
            }

            if (isCustomColorPackLibrary)
            {
                if (!settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var storedThemePreset) ||
                    !string.Equals(storedThemePreset, CustomColorPackPresetKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.AnikiThemeSettingsSelectedPresets[groupId] = CustomColorPackPresetKey;
                    changed = true;
                }

                var activeVirtualKey = GetCustomColorPackVirtualKey(customColorLibrary?.ActivePackId);
                var selectedVisible = visibleItems.FirstOrDefault(item => item != null &&
                    string.Equals(item.Key, activeVirtualKey, StringComparison.OrdinalIgnoreCase));

                group.HasVisibleSelection = selectedVisible != null;
                group.SetSelectedPresetKeySilently(selectedVisible?.Key ?? string.Empty);
                return changed;
            }

            if (isLoginPackLibrary)
            {
                settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var storedLoginPreset);
                var selectedVisible = visibleItems.FirstOrDefault(item => item != null &&
                    string.Equals(item.Key, storedLoginPreset, StringComparison.OrdinalIgnoreCase));

                if (selectedVisible == null && !string.IsNullOrWhiteSpace(loginLibrary?.ActivePackId))
                {
                    var activeVirtualKey = GetLoginPackVirtualKey(loginLibrary.ActivePackId);
                    selectedVisible = visibleItems.FirstOrDefault(item => item != null &&
                        string.Equals(item.Key, activeVirtualKey, StringComparison.OrdinalIgnoreCase));
                }

                group.HasVisibleSelection = selectedVisible != null;
                group.SetSelectedPresetKeySilently(selectedVisible?.Key ?? string.Empty);
                return changed;
            }

            settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var selectedKey);
            var normalSelectedVisible = visibleItems.FirstOrDefault(item => item != null &&
                string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase));

            // Normal filter changes only change what is displayed; they must not apply
            // the first preset of the new category. Older theme schemas may still use a
            // disabled selector for another filter value, so preserve that behavior there.
            var mustSelectFilteredPreset = ensureValidSelection || !group.IsSelectionEnabled;
            if (mustSelectFilteredPreset && normalSelectedVisible == null && visibleItems.Count > 0)
            {
                normalSelectedVisible = visibleItems[0];
                settings.AnikiThemeSettingsSelectedPresets[groupId] = normalSelectedVisible.Key;
                selectedKey = normalSelectedVisible.Key;
                changed = true;
            }

            group.HasVisibleSelection = normalSelectedVisible != null;
            group.SetSelectedPresetKeySilently(normalSelectedVisible?.Key ?? selectedKey);
            return changed;
        }

        private bool SyncPresetFilterFromSelection(string groupId, string presetKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(presetKey) ||
                    currentFile?.Presets == null || currentFile.Variables == null ||
                    !currentFile.Presets.TryGetValue(groupId, out var group) || group == null ||
                    string.IsNullOrWhiteSpace(group.FilterBy) ||
                    !currentFile.Variables.TryGetValue(group.FilterBy, out var filterVariable) || filterVariable == null)
                {
                    return false;
                }

                var selectedPreset = group.Items?.FirstOrDefault(item => item != null &&
                    string.Equals(item.Key, presetKey, StringComparison.OrdinalIgnoreCase));

                var filterValue = GetPresetFilterValue(selectedPreset);
                if (string.IsNullOrWhiteSpace(filterValue) || !IsStoredValueValidForVariable(filterVariable, filterValue))
                {
                    return false;
                }

                var changed = !settings.AnikiThemeSettingsValues.TryGetValue(group.FilterBy, out var currentFilter) ||
                              !string.Equals(currentFilter, filterValue, StringComparison.OrdinalIgnoreCase);

                if (changed)
                {
                    settings.AnikiThemeSettingsValues[group.FilterBy] = filterValue;
                }

                RefreshPresetGroupFilter(groupId, group, false);
                return changed;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to sync preset filter for {groupId}.{presetKey}");
                return false;
            }
        }

        private static string GetPresetFilterValue(AnikiPresetItem preset)
        {
            if (preset == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(preset.FilterValue)
                ? preset.FilterValue
                : preset.VisualPackCategory ?? string.Empty;
        }

        private void PostLoadVariables()
        {
            if (currentFile?.Variables == null)
            {
                return;
            }

            foreach (var pair in currentFile.Variables)
            {
                var key = pair.Key;
                var variable = pair.Value;

                if (variable == null)
                {
                    continue;
                }

                variable.Id = key;

                variable.LocalizedName = ResolveLocKey(
                    variable.LocKey,
                    !string.IsNullOrWhiteSpace(variable.Title) ? variable.Title :
                    !string.IsNullOrWhiteSpace(variable.Name) ? variable.Name :
                    variable.Id);

                variable.LocalizedDescription = ResolveLocKey(
                    variable.DescriptionLocKey,
                    variable.Description);

                if (variable.Choices != null)
                {
                    foreach (var choice in variable.Choices)
                    {
                        if (choice == null)
                        {
                            continue;
                        }

                        choice.LocalizedName = ResolveLocKey(
                            choice.LocKey,
                            !string.IsNullOrWhiteSpace(choice.Title) ? choice.Title :
                            !string.IsNullOrWhiteSpace(choice.Name) ? choice.Name :
                            choice.Value);

                        choice.LocalizedDescription = ResolveLocKey(
                            choice.DescriptionLocKey,
                            choice.Description);

                        if (!string.IsNullOrWhiteSpace(choice.Preview))
                        {
                            var choicePreviewPath = Path.Combine(currentThemePath, choice.Preview);
                            choice.Preview = File.Exists(choicePreviewPath) ? choicePreviewPath : null;
                        }
                    }
                }

                variable.ValueChangedAction = (changedKey, changedValue) =>
                {
                    SetOptionValue(changedKey, changedValue);
                };

                if (string.IsNullOrWhiteSpace(variable.Category))
                {
                    variable.Category = "General";
                }

                if (!string.IsNullOrWhiteSpace(variable.Preview))
                {
                    var previewPath = Path.Combine(currentThemePath, variable.Preview);
                    variable.Preview = File.Exists(previewPath) ? previewPath : null;
                }
            }

            foreach (var pair in currentFile.Variables)
            {
                var variable = pair.Value;

                if (variable == null)
                {
                    continue;
                }

                var messages = new List<string>();

                var firstMessage = BuildThemeOptionDependencyMessage(
                    variable.DependsOn,
                    variable.DependsOnValue,
                    variable.DependsOnNotValue);

                if (!string.IsNullOrWhiteSpace(firstMessage))
                {
                    messages.Add(firstMessage);
                }

                var secondMessage = BuildThemeOptionDependencyMessage(
                    variable.DependsOn2,
                    variable.DependsOn2Value,
                    variable.DependsOn2NotValue);

                if (!string.IsNullOrWhiteSpace(secondMessage))
                {
                    messages.Add(secondMessage);
                }

                variable.DependencyMessage = messages.Count > 0
                    ? string.Join(Environment.NewLine, messages)
                    : null;
            }
        }

        private string BuildThemeOptionDependencyMessage(string dependencyId, object expectedValue, object notExpectedValue)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return null;
            }

            if (currentFile?.Variables != null &&
                currentFile.Variables.TryGetValue(dependencyId, out var dependency) &&
                dependency != null)
            {
                var dependencyType = (dependency.Type ?? string.Empty).Trim().ToLowerInvariant();

                if (notExpectedValue != null)
                {
                    var blockedValue = GetDependencyDisplayValue(dependency, notExpectedValue);
                    var messageFormat = ResolveLocKey(
                        "LOCThemeOptionRequiresNotValue",
                        "Requires “{0}” not to be set to “{1}”.");

                    return string.Format(
                        CultureInfo.CurrentCulture,
                        messageFormat,
                        dependency.DisplayName,
                        blockedValue);
                }

                if (dependencyType == "boolean" || dependencyType == "bool")
                {
                    var expectedBoolean = expectedValue == null || ToBool(expectedValue);
                    var messageFormat = expectedBoolean
                        ? ResolveLocKey("LOCThemeOptionRequiresEnabled", "Requires “{0}” to be enabled.")
                        : ResolveLocKey("LOCThemeOptionRequiresDisabled", "Requires “{0}” to be disabled.");

                    return string.Format(CultureInfo.CurrentCulture, messageFormat, dependency.DisplayName);
                }

                var expectedDisplayValue = GetDependencyDisplayValue(dependency, expectedValue);
                var valueMessageFormat = ResolveLocKey(
                    "LOCThemeOptionRequiresValue",
                    "Requires “{0}” to be set to “{1}”.");

                return string.Format(
                    CultureInfo.CurrentCulture,
                    valueMessageFormat,
                    dependency.DisplayName,
                    expectedDisplayValue);
            }

            if (currentFile?.Presets != null &&
                currentFile.Presets.TryGetValue(dependencyId, out var presetGroup) &&
                presetGroup != null)
            {
                var dependencyName = presetGroup.DisplayName;
                var targetValue = notExpectedValue ?? expectedValue;
                var targetText = targetValue?.ToString() ?? string.Empty;
                var targetDisplay = presetGroup.Items?.FirstOrDefault(item => item != null &&
                    string.Equals(item.Key, targetText, StringComparison.OrdinalIgnoreCase))?.DisplayName
                    ?? targetText;

                var messageFormat = notExpectedValue != null
                    ? ResolveLocKey("LOCThemeOptionRequiresNotValue", "Requires “{0}” not to be set to “{1}”.")
                    : ResolveLocKey("LOCThemeOptionRequiresValue", "Requires “{0}” to be set to “{1}”.");

                return string.Format(
                    CultureInfo.CurrentCulture,
                    messageFormat,
                    dependencyName,
                    targetDisplay);
            }

            return null;
        }

        private string GetDependencyDisplayValue(AnikiThemeVariable dependency, object rawValue)
        {
            var text = rawValue?.ToString() ?? string.Empty;

            return dependency?.Choices?.FirstOrDefault(choice => choice != null &&
                string.Equals(choice.Value, text, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? text;
        }

        private void BuildCategories()
        {
            try
            {
                settings.AnikiThemeSettingsCategories.Clear();

                var categories = new Dictionary<string, AnikiThemeSettingsCategory>(StringComparer.OrdinalIgnoreCase);

                AnikiThemeSettingsCategory GetOrCreateCategory(string categoryId)
                {
                    if (string.IsNullOrWhiteSpace(categoryId))
                    {
                        categoryId = "General";
                    }

                    if (!categories.TryGetValue(categoryId, out var category))
                    {
                        var categoryTitle = GetCategoryTitle(categoryId);
                        var categoryLocKey = GetCategoryLocKey(categoryId);

                        category = new AnikiThemeSettingsCategory
                        {
                            Id = categoryId,
                            Title = ResolveLocKey(categoryLocKey, categoryTitle),
                            LocKey = categoryLocKey,
                            Icon = GetCategoryIcon(categoryId)
                        };

                        categories[categoryId] = category;
                    }

                    return category;
                }

                if (currentFile?.Presets != null)
                {
                    foreach (var groupPair in currentFile.Presets)
                    {
                        var group = groupPair.Value;

                        if (group == null)
                        {
                            continue;
                        }

                        var category = GetOrCreateCategory(group.Category);
                        category.Items.Add(group);
                    }
                }

                if (currentFile?.Variables != null)
                {
                    foreach (var variablePair in currentFile.Variables)
                    {
                        var variable = variablePair.Value;

                        if (variable == null)
                        {
                            continue;
                        }

                        // Hidden variables still participate in option loading/apply/defaults,
                        // but are intentionally omitted from the settings UI.
                        if (variable.Hidden)
                        {
                            continue;
                        }

                        var category = GetOrCreateCategory(variable.Category);

                        if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(variable.HeaderKind, "PageTitle", StringComparison.OrdinalIgnoreCase))
                        {
                            category.WindowTitle = variable.DisplayName;

                            if (variable.CategoryOrder > 0)
                            {
                                category.Order = variable.CategoryOrder;
                            }

                            continue;
                        }

                        category.Items.Add(variable);
                    }
                }

                foreach (var category in categories.Values)
                {
                    var orderedItems = category.Items
                        .Select((item, index) => new { Item = item, Index = index })
                        .OrderBy(x => GetThemeSettingsItemOrder(x.Item))
                        .ThenBy(x => x.Index)
                        .Select(x => x.Item)
                        .ToList();

                    category.Items.Clear();
                    foreach (var item in orderedItems)
                    {
                        category.Items.Add(item);
                    }
                }

                foreach (var category in categories.Values
                    .OrderBy(x => x.Order)
                    .ThenBy(x => GetCategorySortOrder(x.Id)))
                {
                    settings.AnikiThemeSettingsCategories.Add(category);
                }

                if (!settings.AnikiThemeSettingsCategories.Any(x =>
                        string.Equals(x.Id, settings.SelectedAnikiThemeSettingsCategoryId, StringComparison.OrdinalIgnoreCase)))
                {
                    settings.SelectedAnikiThemeSettingsCategoryId =
                        settings.AnikiThemeSettingsCategories.FirstOrDefault()?.Id ?? "General";
                }

                settings.RefreshSelectedAnikiThemeSettingsCategoryItems();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to build Aniki Theme Settings categories.");
            }
        }

        private int GetThemeSettingsItemOrder(object item)
        {
            if (item is AnikiPresetGroup presetGroup)
            {
                return presetGroup.Order;
            }

            if (item is AnikiThemeVariable variable)
            {
                return variable.Order;
            }

            return 999;
        }

        private string GetCategoryTitle(string categoryId)
        {
            switch ((categoryId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "general":
                    return "General";

                case "mainview":
                case "main view":
                    return "Main View";

                case "detailsview":
                case "details view":
                case "detail view":
                case "detail view settings":
                    return "Details View";

                case "achievements":
                case "achievement":
                case "trophy":
                case "trophy view":
                case "trophy view settings":
                    return "Achievements";

                case "visualeffects":
                case "visual effects":
                    return "Visual Effects";

                case "controller":
                case "controller / prompts":
                case "prompts":
                    return "Controller";

                case "advanced":
                case "extra":
                case "extra options":
                    return "Advanced";

                default:
                    return string.IsNullOrWhiteSpace(categoryId) ? "General" : categoryId.Trim();
            }
        }

        private string GetCategoryLocKey(string categoryId)
        {
            var cleanId = (categoryId ?? "General")
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("/", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);

            return "LOCAnikiThemeSettingsCategory" + cleanId;
        }

        private string GetCategoryIcon(string categoryId)
        {
            switch ((categoryId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "general":
                case "appearance":
                    return "\uE713";

                case "mainview":
                case "main view":
                    return "\uE80F";

                case "detailsview":
                case "details view":
                case "detail view":
                case "detail view settings":
                    return "\uE946";

                case "achievements":
                case "achievement":
                case "trophy":
                case "trophy view":
                case "trophy view settings":
                    return "\uE7C1";

                case "visualeffects":
                case "visual effects":
                    return "\uE790";

                case "backgrounds":
                case "background":
                    return "\uE75B";

                case "videos":
                case "video":
                    return "\uE714";

                case "controller":
                case "controller / prompts":
                case "prompts":
                    return "\uE7FC";

                case "advanced":
                case "extra":
                case "extra options":
                    return "\uE9F5";

                default:
                    return "\uE713";
            }
        }

        private int GetCategorySortOrder(string categoryId)
        {
            switch ((categoryId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "general":
                    return 0;

                case "mainview":
                case "main view":
                    return 10;

                case "detailsview":
                case "details view":
                case "detail view":
                case "detail view settings":
                    return 20;

                case "achievements":
                case "achievement":
                case "trophy":
                case "trophy view":
                case "trophy view settings":
                    return 80;

                case "backgrounds":
                case "background":
                    return 60;

                case "videos":
                case "video":
                    return 70;

                case "visualeffects":
                case "visual effects":
                    return 40;

                case "controller":
                case "controller / prompts":
                case "prompts":
                    return 50;

                case "advanced":
                case "extra":
                case "extra options":
                    return 100;

                default:
                    return 999;
            }
        }

        private void UpdateSelectedPresetFlags()
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var group = groupPair.Value;
                var selectedPreset = GetSelectedPreset(groupPair.Key, group);

                if (group == null)
                {
                    continue;
                }

                var filterValue = !string.IsNullOrWhiteSpace(group.FilterBy) &&
                                  currentFile?.Variables != null &&
                                  currentFile.Variables.TryGetValue(group.FilterBy, out var selectedFilterVariable) &&
                                  selectedFilterVariable != null
                    ? GetStoredValueOrDefault(group.FilterBy, selectedFilterVariable) ?? string.Empty
                    : string.Empty;

                if (IsCustomVisualPackLibraryFilter(groupPair.Key, filterValue))
                {
                    var snapshot = customVisualPackLibrarySnapshot ?? RefreshCustomVisualPackLibrarySnapshot();
                    group.SetSelectedPresetKeySilently(GetCustomVisualPackVirtualKey(snapshot?.ActivePackId));
                }
                else if (IsCustomColorPackLibraryFilter(groupPair.Key, filterValue))
                {
                    var snapshot = customColorPackLibrarySnapshot ?? RefreshCustomColorPackLibrarySnapshot();
                    group.SetSelectedPresetKeySilently(GetCustomColorPackVirtualKey(snapshot?.ActivePackId));
                }
                else
                {
                    group.SetSelectedPresetKeySilently(selectedPreset?.Key);
                }

                if (group.Items == null)
                {
                    continue;
                }

                foreach (var preset in group.Items)
                {
                    preset.IsSelected = selectedPreset != null &&
                                        string.Equals(preset.Key, selectedPreset.Key, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void SyncVariableBindableValues(Dictionary<string, object> optionValues)
        {
            if (currentFile?.Variables == null || optionValues == null)
            {
                return;
            }

            foreach (var pair in currentFile.Variables)
            {
                var key = pair.Key;
                var variable = pair.Value;

                if (variable == null || string.IsNullOrWhiteSpace(variable.Type))
                {
                    continue;
                }

                if (!optionValues.TryGetValue(key, out var value))
                {
                    continue;
                }

                var type = variable.Type.Trim().ToLowerInvariant();

                switch (type)
                {
                    case "boolean":
                    case "bool":
                        variable.SetCurrentBooleanValueSilently(ToBool(value));
                        break;

                    case "double":
                    case "float":
                    case "int32":
                    case "int":
                        variable.SetCurrentDoubleValueSilently(ToDouble(value, 0));
                        break;

                    case "cornerradius":
                        variable.SetCurrentDoubleValueSilently(ToCornerRadiusUniformValue(value));
                        break;

                    case "string":
                    case "choice":
                    case "enum":
                        variable.SetCurrentStringValueSilently(value?.ToString() ?? string.Empty);
                        break;
                }
            }
        }

        private bool ToBool(object value)
        {
            if (value is bool boolValue)
            {
                return boolValue;
            }

            return string.Equals(value?.ToString(), "True", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private double ToDouble(object value, double fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (value is double doubleValue)
            {
                return doubleValue;
            }

            if (value is float floatValue)
            {
                return floatValue;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return fallback;
        }

        private double ToCornerRadiusUniformValue(object value)
        {
            if (value == null)
            {
                return 0;
            }

            if (value is CornerRadius cornerRadius)
            {
                return cornerRadius.TopLeft;
            }

            var text = value.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var parts = text.Split(',');

            if (parts.Length > 0 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var firstValue))
            {
                return firstValue;
            }

            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var uniform)
                ? uniform
                : 0;
        }

        private Dictionary<string, object> BuildOptionValues()
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            ApplyVariableValues(result);
            ApplyPresetConstants(result);

            return result;
        }

        private void ApplyDerivedMainViewMediaCardOptions(Dictionary<string, object> optionValues)
        {
            if (optionValues == null)
            {
                return;
            }

            var mode = optionValues.TryGetValue("MainViewMediaCard", out var rawMode)
                ? rawMode?.ToString() ?? "Disabled"
                : "Disabled";

            var trailerCard = string.Equals(mode, "Trailer", StringComparison.OrdinalIgnoreCase);
            var backgroundCard = string.Equals(mode, "GameBackground", StringComparison.OrdinalIgnoreCase);

            optionValues["MediaCardOnMainView"] = trailerCard || backgroundCard;
            optionValues["TrailerCardOnMainView"] = trailerCard;
            optionValues["BackgroundCardOnMainView"] = backgroundCard;
        }

        private void ApplyDerivedMainViewBottomBarOptions(Dictionary<string, object> optionValues)
        {
            if (optionValues == null)
            {
                return;
            }

            var mode = optionValues.TryGetValue("MainViewBottomBar", out var rawMode)
                ? rawMode?.ToString() ?? "ControllerShortcuts"
                : "ControllerShortcuts";

            optionValues["ControllerShortcutBar"] =
                string.Equals(mode, "ControllerShortcuts", StringComparison.OrdinalIgnoreCase);
            optionValues["CompactGameInfoBar"] =
                string.Equals(mode, "CompactGameInfo", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDerivedFocusedCoverPreviewOptions(Dictionary<string, object> optionValues)
        {
            if (optionValues == null)
            {
                return;
            }

            var mode = optionValues.TryGetValue("FocusedCoverPreview", out var rawMode)
                ? rawMode?.ToString() ?? "Disabled"
                : "Disabled";

            optionValues["MicroTrailerOnFocusedCover"] =
                string.Equals(mode, "MicroTrailer", StringComparison.OrdinalIgnoreCase);
            optionValues["BackgroundOnFocusedCover"] =
                string.Equals(mode, "BackgroundLogo", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDerivedBackgroundDisplayModeOptions(Dictionary<string, object> optionValues)
        {
            if (optionValues == null)
            {
                return;
            }

            var mode = optionValues.TryGetValue("BackgroundDisplayMode", out var rawMode)
                ? rawMode?.ToString() ?? "FillCrop"
                : "FillCrop";

            optionValues["BackgroundStretchMode"] =
                string.Equals(mode, "Stretch", StringComparison.OrdinalIgnoreCase);
            optionValues["SteamBanner"] =
                string.Equals(mode, "FitHero", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDerivedPlatformBannerPositionOptions(Dictionary<string, object> optionValues)
        {
            if (optionValues == null)
            {
                return;
            }

            var mode = optionValues.TryGetValue("PlatformBannerPosition", out var rawMode)
                ? rawMode?.ToString() ?? "AboveCover"
                : "AboveCover";

            optionValues["PlatformBannerOverlay"] =
                string.Equals(mode, "Overlay", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyVariableValues(Dictionary<string, object> result)
        {
            if (currentFile?.Variables == null)
            {
                return;
            }

            foreach (var pair in currentFile.Variables)
            {
                var key = pair.Key;
                var variable = pair.Value;

                if (variable == null)
                {
                    continue;
                }

                if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawValue = GetStoredValueOrDefault(key, variable);
                var value = ConvertValue(variable.Type, rawValue);

                if (value != null)
                {
                    result[key] = value;
                }
            }
        }

        private void ApplyPresetConstants(Dictionary<string, object> result)
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var groupId = groupPair.Key;
                var selectedPreset = GetSelectedPreset(groupId, groupPair.Value);

                if (selectedPreset?.Constants == null)
                {
                    continue;
                }

                foreach (var constantPair in selectedPreset.Constants)
                {
                    var key = constantPair.Key;
                    var constant = constantPair.Value;

                    if (constant == null)
                    {
                        continue;
                    }

                    var value = ConvertValue(constant.Type, constant.Value ?? constant.Default);

                    if (value != null)
                    {
                        result[key] = value;
                    }
                }
            }
        }

        private string GetStoredValueOrDefault(string key, AnikiThemeValue value)
        {
            if (settings.AnikiThemeSettingsValues != null &&
                settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue))
            {
                return storedValue;
            }

            return value?.EffectiveValue;
        }

        private AnikiPresetItem GetSelectedPreset(string groupId, AnikiPresetGroup group)
        {
            if (group?.Items == null || group.Items.Count == 0)
            {
                return null;
            }

            string selectedKey = null;

            if (settings.AnikiThemeSettingsSelectedPresets != null)
            {
                settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out selectedKey);
            }

            var selected = !string.IsNullOrWhiteSpace(selectedKey)
                ? group.Items.FirstOrDefault(p => string.Equals(p.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                : null;

            if (selected == null)
            {
                selected = group.Items.FirstOrDefault(p =>
                    p.Key != null &&
                    p.Key.EndsWith("Default", StringComparison.OrdinalIgnoreCase));
            }

            if (selected == null)
            {
                selected = group.Items.FirstOrDefault(p =>
                    string.Equals(p.Key, "Default", StringComparison.OrdinalIgnoreCase));
            }

            return selected ?? group.Items.FirstOrDefault();
        }

        private void LoadSelectedPresetFiles()
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                // Visual Pack Random keeps the user's persisted normal selection untouched.
                // Do not load that persisted pack underneath the session-only random override.
                if (string.Equals(groupPair.Key, VisualPackPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                    IsRandomVisualPackFilterActive())
                {
                    continue;
                }

                // Theme Color Random keeps the user's persisted normal selection untouched.
                // Do not load that persisted color underneath the session-only random override.
                if (string.Equals(groupPair.Key, ThemeColorPresetGroupId, StringComparison.OrdinalIgnoreCase) &&
                    IsRandomThemeColorFilterActive())
                {
                    continue;
                }

                var selectedPreset = GetSelectedPreset(groupPair.Key, groupPair.Value);

                if (selectedPreset?.Files == null)
                {
                    continue;
                }

                foreach (var relativeFile in selectedPreset.Files)
                {
                    if (string.IsNullOrWhiteSpace(relativeFile))
                    {
                        continue;
                    }

                    var filePath = Path.Combine(currentThemePath, relativeFile);

                    if (!File.Exists(filePath))
                    {
                        logger?.Warn($"[AnikiHelper] Aniki preset resource file not found: {filePath}");
                        continue;
                    }

                    try
                    {
                        var resource = GetOrLoadResourceDictionary(filePath);

                        if (resource != null)
                        {
                            Application.Current.Resources.MergedDictionaries.Add(resource);
                            loadedDictionaries.Add(resource);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, $"[AnikiHelper] Failed to load Aniki preset resource: {filePath}");
                    }
                }
            }
        }

        private ResourceDictionary GetOrLoadResourceDictionary(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            if (resourceCache.TryGetValue(filePath, out var cached))
            {
                return cached;
            }

            var fileUri = new Uri(filePath, UriKind.Absolute);

            using (var stream = File.OpenRead(filePath))
            {
                var parserContext = new ParserContext
                {
                    BaseUri = fileUri
                };

                var resource = (ResourceDictionary)XamlReader.Load(stream, parserContext);
                resource.Source = fileUri;

                resourceCache[filePath] = resource;
                return resource;
            }
        }

        private async void StartPresetFilesPreload()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null || currentFile?.Presets == null)
                {
                    return;
                }

                // Do not enumerate every preset XAML during LoadAndApply().
                // An async void method runs synchronously until its first await, so the old code
                // was scanning and reading all preset files before Playnite could finish rendering.
                await Task.Delay(3000);

                var files = await Task.Run(() =>
                    GetAllPresetResourceFiles()
                        .Where(File.Exists)
                        .Where(CanPreloadResourceFile)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList());

                if (files.Count == 0)
                {
                    return;
                }

                foreach (var file in files)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            GetOrLoadResourceDictionary(file);
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn(ex, $"[AnikiHelper] Failed to preload Aniki preset resource: {file}");
                        }
                    }, DispatcherPriority.Background);

                    await Task.Delay(50);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to preload Aniki preset resources.");
            }
        }

        private bool CanPreloadResourceFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    return false;
                }

                var text = File.ReadAllText(filePath);

                // ThemeFile may fail when loaded manually through XamlReader during preload.
                // These files should only be loaded when actually selected.
                if (text.IndexOf("ThemeFile", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<string> GetAllPresetResourceFiles()
        {
            if (currentFile?.Presets == null || string.IsNullOrWhiteSpace(currentThemePath))
            {
                yield break;
            }

            foreach (var group in currentFile.Presets.Values)
            {
                if (group?.Items == null)
                {
                    continue;
                }

                foreach (var preset in group.Items)
                {
                    if (preset?.Files == null)
                    {
                        continue;
                    }

                    foreach (var relativeFile in preset.Files)
                    {
                        if (string.IsNullOrWhiteSpace(relativeFile))
                        {
                            continue;
                        }

                        yield return Path.Combine(currentThemePath, relativeFile);
                    }
                }
            }
        }

        private void LoadLuckyDayResourceOverride()
        {
            try
            {
                if (settings?.IsLuckyDay != true)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(currentThemePath))
                {
                    return;
                }

                var luckyStyleIndex = settings.LuckyStyleIndex <= 1 ? 1 : settings.LuckyStyleIndex;
                var fileName = luckyStyleIndex == 1
                    ? "LuckyDay.xaml"
                    : $"LuckyDay{luckyStyleIndex}.xaml";

                var filePath = Path.Combine(
                    currentThemePath,
                    "Themes Option",
                    "2.Interface",
                    "Hidden",
                    fileName);

                if (!File.Exists(filePath) && luckyStyleIndex != 1)
                {
                    var fallbackPath = Path.Combine(
                        currentThemePath,
                        "Themes Option",
                        "2.Interface",
                        "Hidden",
                        "LuckyDay.xaml");

                    logger?.Warn($"[AnikiHelper] Lucky Day style resource file not found: {filePath}. Falling back to: {fallbackPath}");
                    filePath = fallbackPath;
                }

                if (!File.Exists(filePath))
                {
                    logger?.Warn($"[AnikiHelper] Lucky Day resource file not found: {filePath}");
                    return;
                }

                var resource = GetOrLoadResourceDictionary(filePath);

                if (resource != null)
                {
                    Application.Current.Resources.MergedDictionaries.Add(resource);
                    loadedDictionaries.Add(resource);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Lucky Day resource override.");
            }
        }

        public void LoadKonamiModeResourceOverride(bool force = false)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => LoadKonamiModeResourceOverride(force)), DispatcherPriority.Loaded);
                    return;
                }

                if (!force && settings?.IsKonamiModeActive != true)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(currentThemePath))
                {
                    currentThemePath = GetCurrentThemePath();
                }

                if (string.IsNullOrWhiteSpace(currentThemePath))
                {
                    return;
                }

                var filePath = Path.Combine(
                    currentThemePath,
                    "Themes Option",
                    "2.Interface",
                    "Hidden",
                    "KonamiMode.xaml");

                if (!File.Exists(filePath))
                {
                    logger?.Warn($"[AnikiHelper] Konami Mode resource file not found: {filePath}");
                    return;
                }

                var resource = GetOrLoadResourceDictionary(filePath);

                if (resource != null && !loadedDictionaries.Contains(resource))
                {
                    Application.Current.Resources.MergedDictionaries.Add(resource);
                    loadedDictionaries.Add(resource);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Konami Mode resource override.");
            }
        }

        private ResourceDictionary BuildGeneratedResourceDictionary(Dictionary<string, object> values)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            var dictionary = new ResourceDictionary();

            foreach (var pair in values)
            {
                dictionary[pair.Key] = pair.Value;
            }

            return dictionary;
        }

        private void RemoveLoadedDictionaries()
        {
            foreach (var dictionary in loadedDictionaries.ToList())
            {
                try
                {
                    Application.Current.Resources.MergedDictionaries.Remove(dictionary);
                }
                catch
                {
                }
            }

            loadedDictionaries.Clear();
        }

        private object ConvertValue(string type, object rawValue)
        {
            var value = rawValue?.ToString();

            if (string.IsNullOrWhiteSpace(type))
            {
                return value;
            }

            try
            {
                switch (type.Trim().ToLowerInvariant())
                {
                    case "string":
                    case "choice":
                    case "enum":
                        return value ?? string.Empty;

                    case "boolean":
                    case "bool":
                        return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

                    case "int32":
                    case "int":
                        return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var intValue)
                            ? intValue
                            : 0;

                    case "double":
                        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue)
                            ? doubleValue
                            : 0d;

                    case "float":
                        return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var floatValue)
                            ? floatValue
                            : 0f;

                    case "visibility":
                        return string.Equals(value, "Visible", StringComparison.OrdinalIgnoreCase)
                            ? Visibility.Visible
                            : Visibility.Collapsed;

                    case "cornerradius":
                        return ParseCornerRadius(value);

                    case "thickness":
                        return ParseThickness(value);

                    case "color":
                        return ColorConverter.ConvertFromString(value);

                    case "solidcolorbrush":
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

                    case "timespan":
                        return TimeSpan.TryParse(value, out var timeSpan)
                            ? timeSpan
                            : TimeSpan.Zero;

                    default:
                        return value;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to convert Aniki option value. Type={type}, Value={value}");
                return value;
            }
        }

        private CornerRadius ParseCornerRadius(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new CornerRadius(0);
            }

            var parts = value.Split(',');

            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var left) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var top) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var right) &&
                double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var bottom))
            {
                return new CornerRadius(left, top, right, bottom);
            }

            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var uniform)
                ? new CornerRadius(uniform)
                : new CornerRadius(0);
        }

        private Thickness ParseThickness(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new Thickness(0);
            }

            var parts = value.Split(',');

            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var left) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var top) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var right) &&
                double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var bottom))
            {
                return new Thickness(left, top, right, bottom);
            }

            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var uniform)
                ? new Thickness(uniform)
                : new Thickness(0);
        }
    }
}
