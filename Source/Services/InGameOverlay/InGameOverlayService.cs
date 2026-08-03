using AnikiHelper.Services.Achievements;
using AnikiHelper.Services.MediaGallery;
using AnikiHelper.Services.WebBrowser;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AnikiHelper.Services.InGameOverlay
{
    internal sealed class InGameOverlayService : IDisposable
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;
        private readonly Func<bool> hasOpenCustomWindow;
        private readonly Func<bool> isWebBrowserActive;
        private readonly Action<WebBrowserGamepadInputState> onWebBrowserInput;

        private InGameOverlayHotkeyService hotkeyService;
        private AnikiOverlayInputListener inputListener;
        private GamepadMouseService gamepadMouseService;
        private AnikiInGameOverlayWindow overlayWindow;
        private bool overlayOpenedFromPlaynite;

        private readonly object overlayToggleLock = new object();
        private bool overlayToggleQueued;
        private volatile bool overlayOpenOrOpening;
        private DateTime lastOverlayToggleUtc = DateTime.MinValue;
        private const int OverlayToggleCooldownMs = 350;
        private int overlayForegroundRecoveryGeneration;
        private int controllerOverlayOpenGeneration;
        private int controllerVirtualKeyboardOpenGeneration;
        private const int ControllerShortcutFocusHandoffDelayMs = 140;
        private const int OverlayDPadFallbackDelayMs = 65;

        private Game currentGame;
        private DateTime? currentSessionStartTime;
        private Guid cachedAchievementGameId = Guid.Empty;
        private DateTime cachedAchievementCheckedUtc = DateTime.MinValue;
        private AchievementOverlaySummary cachedAchievementSummary;
        private PlayniteAchievementsReader playniteAchievementsReader;
        private readonly object achievementSummaryLock = new object();
        private int overlayDataRefreshGeneration;
        private int overlayPreloadGeneration;

        private int? currentGameProcessId;
        private IntPtr lastForegroundWindow = IntPtr.Zero;
        private int? lastForegroundWindowProcessId;

        // Dedicated target captured before the overlay takes focus. This is kept
        // separate from the game-return target so the keyboard can safely type in
        // Firefox, Notepad, Discord, Playnite, or any other foreground application.
        private IntPtr virtualKeyboardTargetWindow = IntPtr.Zero;
        private int? virtualKeyboardTargetProcessId;

        private readonly object gameSuspendLock = new object();
        private int? suspendedGameProcessId;
        private readonly List<int> suspendedGameThreadIds = new List<int>();
        private bool closeOverlayShouldResumeSuspendedGame = true;

        private bool controllerStart;
        private bool controllerBack;
        private bool controllerY;
        private DateTime lastControllerShortcutTime = DateTime.MinValue;
        private DateTime? controllerGuidePressedAt;
        private const int GuideShortPressMaxMs = 350;

        public InGameOverlayService(
            IPlayniteAPI playniteApi,
            AnikiHelperSettings settings,
            Func<bool> hasOpenCustomWindow = null,
            Func<bool> isWebBrowserActive = null,
            Action<WebBrowserGamepadInputState> onWebBrowserInput = null)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.hasOpenCustomWindow = hasOpenCustomWindow;
            this.isWebBrowserActive = isWebBrowserActive;
            this.onWebBrowserInput = onWebBrowserInput;
            logger = LogManager.GetLogger();
            playniteAchievementsReader = new PlayniteAchievementsReader(playniteApi, logger);
            gamepadMouseService = new GamepadMouseService(logger);
        }

        public bool IsGameRunning
        {
            get { return currentGame != null; }
        }

        internal bool IsWindowsVirtualKeyboardSelected
        {
            get
            {
                return settings != null &&
                       string.Equals(
                           settings.InGameOverlayVirtualKeyboardProvider,
                           "Windows",
                           StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsOverlayVisible
        {
            get { return overlayWindow != null && overlayWindow.IsVisible; }
        }

        public bool IsOverlayOpenOrOpening
        {
            get { return overlayOpenOrOpening; }
        }

        public bool IsPlayniteForeground
        {
            get { return IsPlayniteCurrentlyForeground(); }
        }

        public bool OverlayOpenedFromPlaynite
        {
            get { return overlayOpenedFromPlaynite; }
        }

        private static readonly Guid AudioSwitcherPluginId = Guid.Parse("708b6ec4-bf96-4c0d-bd9d-fe0aa04d6bf1");
        private static readonly Guid UniPlaySongPluginId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        private static readonly Guid PlayniteAchievementsPluginId = Guid.Parse("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");

        public bool IsAudioSwitcherInstalled
        {
            get
            {
                try
                {
                    return playniteApi?.Addons?.Plugins?.Any(p => p.Id == AudioSwitcherPluginId) == true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsUniPlaySongInstalled
        {
            get
            {
                try
                {
                    return playniteApi?.Addons?.Plugins?.Any(p => p.Id == UniPlaySongPluginId) == true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsPlayniteAchievementsInstalled
        {
            get
            {
                try
                {
                    return playniteApi?.Addons?.Plugins?.Any(p => p.Id == PlayniteAchievementsPluginId) == true;
                }
                catch
                {
                    return false;
                }
            }
        }


        public string SelfName
        {
            get
            {
                if (settings != null && !string.IsNullOrWhiteSpace(settings.SelfName))
                {
                    return settings.SelfName;
                }

                return string.Empty;
            }
        }

        public string SelfState
        {
            get
            {
                if (settings != null && !string.IsNullOrWhiteSpace(settings.SelfState))
                {
                    return settings.SelfState;
                }

                return "offline";
            }
        }

        public string SelfStateLoc
        {
            get
            {
                if (settings != null && !string.IsNullOrWhiteSpace(settings.SelfStateLoc))
                {
                    return settings.SelfStateLoc;
                }

                return Loc("LOCOffline", "Offline");
            }
        }

        public string SelfAvatarPath
        {
            get
            {
                try
                {
                    if (settings != null && !string.IsNullOrWhiteSpace(settings.SelfAvatar))
                    {
                        return settings.SelfAvatar;
                    }

                    var configurationPath = playniteApi?.Paths?.ConfigurationPath;
                    if (!string.IsNullOrWhiteSpace(configurationPath))
                    {
                        var profilePicturePath = Path.Combine(configurationPath, "ExtraMetadata", "Themes", "Common", "ProfilePicture.png");
                        if (File.Exists(profilePicturePath))
                        {
                            return profilePicturePath;
                        }
                    }
                }
                catch
                {
                }

                return string.Empty;
            }
        }

        public string CurrentGameName
        {
            get
            {
                if (currentGame == null || string.IsNullOrWhiteSpace(currentGame.Name))
                {
                    return string.Empty;
                }

                return currentGame.Name;
            }
        }

        public string CurrentGameSourceName
        {
            get
            {
                if (currentGame == null)
                {
                    return "-";
                }

                var source = GetSourceName(currentGame);
                return string.IsNullOrWhiteSpace(source) ? "-" : source;
            }
        }

        public string CurrentGamePlatformName
        {
            get
            {
                if (currentGame == null)
                {
                    return "-";
                }

                var platform = GetPlatformName(currentGame);
                return string.IsNullOrWhiteSpace(platform) ? "-" : platform;
            }
        }

        public string CurrentGameLogoPath
        {
            get
            {
                if (currentGame == null)
                {
                    return null;
                }

                var logoPath = TryFindExtraMetadataLogo(currentGame.Id);

                if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                {
                    return logoPath;
                }

                return null;
            }
        }

        public string CurrentGameCoverPath
        {
            get
            {
                if (currentGame == null)
                {
                    return null;
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(currentGame.CoverImage))
                    {
                        var coverPath = playniteApi?.Database?.GetFullFilePath(currentGame.CoverImage);
                        if (!string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath))
                        {
                            return coverPath;
                        }
                    }
                }
                catch
                {
                }

                return null;
            }
        }

        public string CurrentGameBackgroundPath
        {
            get
            {
                if (currentGame == null)
                {
                    return null;
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(currentGame.BackgroundImage))
                    {
                        var bgPath = playniteApi?.Database?.GetFullFilePath(currentGame.BackgroundImage);
                        if (!string.IsNullOrWhiteSpace(bgPath) && File.Exists(bgPath))
                        {
                            return bgPath;
                        }
                    }
                }
                catch
                {
                }

                return null;
            }
        }

        public string CurrentGamePlaytimeValue
        {
            get
            {
                if (currentGame == null)
                {
                    return "-";
                }

                var minutes = currentGame.Playtime / 60UL;
                return FormatMinutes(minutes);
            }
        }

        public string CurrentGameSessionTimeValue
        {
            get
            {
                if (currentGame == null || currentSessionStartTime == null)
                {
                    return "-";
                }

                var elapsed = DateTime.Now - currentSessionStartTime.Value;

                if (elapsed.TotalHours >= 1)
                {
                    return (int)elapsed.TotalHours + "h " + elapsed.Minutes.ToString("00");
                }

                if (elapsed.TotalMinutes >= 1)
                {
                    return Math.Max(1, (int)elapsed.TotalMinutes) + " min";
                }

                return Loc("LOCInGameOverlayLessThanOneMinute", "less than 1 min");
            }
        }

        public string CurrentGameMediaCountValue
        {
            get
            {
                var media = GetCurrentGameMediaSummary();
                if (media == null || media.MediaCount <= 0)
                {
                    return "-";
                }

                if (media.MediaCount == 1)
                {
                    return "1 capture";
                }

                return media.MediaCount + " captures";
            }
        }

        public string CurrentGameLatestCaptureValue
        {
            get
            {
                var media = GetCurrentGameMediaSummary();
                if (media == null || media.LatestCaptureDate == DateTime.MinValue)
                {
                    return "-";
                }

                return FormatRelativeTime(media.LatestCaptureDate);
            }
        }

        public string CurrentGameAchievementsUnlockedValue
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                if (summary == null || summary.Total <= 0)
                {
                    return "-";
                }

                return summary.Unlocked + " / " + summary.Total;
            }
        }

        public string CurrentGameAchievementsProgressValue
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                if (summary == null || summary.Total <= 0)
                {
                    return "-";
                }

                var percent = Math.Round((summary.Unlocked / (double)summary.Total) * 100.0);
                return percent.ToString("0") + "%";
            }
        }

        public string CurrentGameLastAchievementValue
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                if (summary == null || string.IsNullOrWhiteSpace(summary.LastUnlockedTitle))
                {
                    return "-";
                }

                return summary.LastUnlockedTitle;
            }
        }

        public string CurrentGameLastAchievementDescription
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                if (summary == null || string.IsNullOrWhiteSpace(summary.LastUnlockedDescription))
                {
                    return string.Empty;
                }

                return summary.LastUnlockedDescription;
            }
        }

        public string CurrentGameLastAchievementIconPath
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                if (summary == null || string.IsNullOrWhiteSpace(summary.LastUnlockedIconPath))
                {
                    return string.Empty;
                }

                return summary.LastUnlockedIconPath;
            }
        }

        public string CurrentGameLastAchievementPercentValue
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                if (summary == null || !summary.LastUnlockedPercent.HasValue)
                {
                    return string.Empty;
                }

                return summary.LastUnlockedPercent.Value.ToString("0.##") + "%";
            }
        }

        public string CurrentGameLastAchievementDateValue
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                if (summary == null || !summary.LastUnlockedDate.HasValue)
                {
                    return string.Empty;
                }

                return FormatRelativeTime(summary.LastUnlockedDate.Value);
            }
        }

        public bool HasCurrentGameLastAchievement
        {
            get
            {
                var summary = GetCachedCurrentGameAchievementSummary();
                return summary != null && !string.IsNullOrWhiteSpace(summary.LastUnlockedTitle);
            }
        }

        public void Start()
        {
            if (hotkeyService != null)
            {
                return;
            }

            var keyboardHotkey = settings?.InGameOverlayHotkey ?? "CtrlShiftF12";

            hotkeyService = new InGameOverlayHotkeyService(ToggleOverlay, keyboardHotkey);
            hotkeyService.Start();

            inputListener = new AnikiOverlayInputListener(
                settings,
                logger,
                QueueControllerOverlayOpen,
                QueueControllerVirtualKeyboardOpen,
                ToggleGamepadMouseMode,
                () => gamepadMouseService?.IsActive == true,
                state => gamepadMouseService?.ProcessInput(state),
                () => gamepadMouseService?.SuspendInput(),
                () => settings == null || settings.InGameOverlayEnabled,
                () => overlayWindow != null && overlayWindow.IsVisible,
                HandleOverlayControllerInput,
                isWebBrowserActive,
                onWebBrowserInput);

            inputListener.Start();

        }

        public void Stop()
        {
            ResumeSuspendedGameProcess();
            Interlocked.Increment(ref controllerOverlayOpenGeneration);
            Interlocked.Increment(ref controllerVirtualKeyboardOpenGeneration);

            try
            {
                hotkeyService?.Stop();
                hotkeyService = null;
            }
            catch
            {
            }

            try
            {
                inputListener?.Stop();
                inputListener = null;
            }
            catch
            {
            }

            try
            {
                gamepadMouseService?.Dispose();
                gamepadMouseService = null;
            }
            catch
            {
            }

            try
            {
                if (overlayWindow != null)
                {
                    overlayWindow.Close();
                    overlayWindow = null;
                }
            }
            catch
            {
            }
        }


        private void ToggleGamepadMouseMode()
        {
            try
            {
                if (gamepadMouseService == null)
                {
                    gamepadMouseService = new GamepadMouseService(logger);
                }

                gamepadMouseService.Toggle();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to toggle Gamepad Mouse mode.");
            }
        }

        public void SetCurrentGame(Game game, int? startedProcessId = null)
        {
            if (game != null && (currentGame == null || currentGame.Id != game.Id))
            {
                currentSessionStartTime = DateTime.Now;
            }

            currentGame = game;

            if (startedProcessId.HasValue && startedProcessId.Value > 0)
            {
                currentGameProcessId = startedProcessId.Value;
            }

            try
            {
                overlayWindow?.Refresh();
            }
            catch
            {
            }

            if (game != null)
            {
                ScheduleOverlayPreloadForGame(game.Id);
            }
        }

        public void ClearCurrentGame(Game game)
        {
            ResumeSuspendedGameProcess();
            Interlocked.Increment(ref overlayPreloadGeneration);

            settings.GameClosing = false;
            settings.ClosingGameName = string.Empty;

            if (game == null || currentGame == null || game.Id == currentGame.Id)
            {
                currentGame = null;
                currentSessionStartTime = null;
                currentGameProcessId = null;
                lastForegroundWindow = IntPtr.Zero;
                lastForegroundWindowProcessId = null;
            }

            HideOverlayWithoutRestoringGameFocus();
        }

        public void ToggleOverlay()
        {
            OpenOverlayInternal(ignoreEnabledSetting: false, source: "Shortcut");
        }

        public void OpenOverlayFromThemeButton()
        {
            OpenOverlayInternal(ignoreEnabledSetting: true, source: "ThemeButton");
        }

        private async void QueueControllerOverlayOpen()
        {
            if (settings != null && !settings.InGameOverlayEnabled)
            {
                return;
            }

            // Steam Big Picture uses the Guide button for its own navigation panel.
            // Ignore Aniki's Guide shortcut while Big Picture is the foreground window
            // so Steam can handle the same button normally. Other controller shortcuts
            // remain available inside Big Picture.
            if (IsSteamBigPictureGuideExclusionActive())
            {
                OverlayDebugLog(
                    "[Overlay][ControllerShortcut] Guide shortcut ignored because Steam Big Picture is foreground.");
                return;
            }

            // Capture the game before Guide can also be handled by Steam, Windows or
            // Playnite. Opening a little later lets those handlers finish first, then
            // Aniki Helper performs the final foreground handoff to the overlay.
            var requestedFromGame = IsForegroundCurrentGame();

            // Only replace the saved return target when the shortcut was pressed from
            // the game itself. When the overlay is opened from Playnite, the existing
            // lastForegroundWindow still points to the game that was minimized by
            // ReturnToPlaynite. Overwriting it here with Playnite makes the
            // "Return to game" button focus Playnite again instead of the game.
            if (requestedFromGame)
            {
                CaptureCurrentForegroundGameWindow();
            }

            CaptureVirtualKeyboardTargetWindow();

            var generation = Interlocked.Increment(ref controllerOverlayOpenGeneration);
            OverlayDebugLog(
                $"[Overlay][ControllerShortcut] Overlay handoff queued. " +
                $"DelayMs={ControllerShortcutFocusHandoffDelayMs}, Target={lastForegroundWindow}");

            try
            {
                await Task.Delay(ControllerShortcutFocusHandoffDelayMs).ConfigureAwait(false);

                if (generation != Volatile.Read(ref controllerOverlayOpenGeneration))
                {
                    return;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation != Volatile.Read(ref controllerOverlayOpenGeneration))
                    {
                        return;
                    }

                    OpenOverlayInternal(
                        ignoreEnabledSetting: false,
                        source: "ControllerShortcutHandoff",
                        preserveCapturedTarget: true,
                        openedFromPlayniteOverride: !requestedFromGame);
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Controller overlay handoff failed.");
            }
        }

        private async void QueueControllerVirtualKeyboardOpen()
        {
            if (settings != null && !settings.InGameOverlayEnabled)
            {
                return;
            }

            // L3/R3 (and the other controller keyboard chords) can also be observed by
            // the game or Playnite. Keep the original target now, wait for that input
            // frame to settle, then make the keyboard the final foreground window.
            CaptureVirtualKeyboardTargetWindow();
            CaptureCurrentForegroundGameWindow();

            var generation = Interlocked.Increment(ref controllerVirtualKeyboardOpenGeneration);
            OverlayDebugLog(
                $"[Overlay][VirtualKeyboard] Controller handoff queued. " +
                $"DelayMs={ControllerShortcutFocusHandoffDelayMs}, Target={virtualKeyboardTargetWindow}");

            try
            {
                await Task.Delay(ControllerShortcutFocusHandoffDelayMs).ConfigureAwait(false);

                if (generation != Volatile.Read(ref controllerVirtualKeyboardOpenGeneration))
                {
                    return;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation != Volatile.Read(ref controllerVirtualKeyboardOpenGeneration))
                    {
                        return;
                    }

                    OpenVirtualKeyboardDirectCore(captureTarget: false, source: "ControllerShortcutHandoff");
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Controller virtual-keyboard handoff failed.");
            }
        }

        public void OpenVirtualKeyboardDirect()
        {
            OpenVirtualKeyboardDirectCore(captureTarget: true, source: "Direct");
        }

        public void OpenVirtualKeyboardForWebBrowser()
        {
            OpenVirtualKeyboardDirectCore(
                captureTarget: true,
                source: "WebBrowser",
                ignoreEnabledSetting: true);
        }

        private void OpenVirtualKeyboardDirectCore(
            bool captureTarget,
            string source,
            bool ignoreEnabledSetting = false)
        {
            if (!ignoreEnabledSetting && settings != null && !settings.InGameOverlayEnabled)
            {
                return;
            }

            if (captureTarget)
            {
                // Capture the exact foreground window before the request reaches WPF
                // and before the overlay can take focus.
                CaptureVirtualKeyboardTargetWindow();
            }

            if (IsWindowsVirtualKeyboardSelected)
            {
                QueueWindowsVirtualKeyboardOpen(
                    hideOverlay: overlayWindow != null && overlayWindow.IsVisible,
                    source: source);
                return;
            }

            lock (overlayToggleLock)
            {
                if (overlayToggleQueued)
                {
                    return;
                }

                overlayToggleQueued = true;
                overlayOpenOrOpening = true;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                lock (overlayToggleLock)
                {
                    overlayToggleQueued = false;
                    overlayOpenOrOpening = false;
                }

                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (HasOpenCustomWindow())
                    {
                        overlayOpenOrOpening = false;
                        OverlayDebugLog("[Overlay][VirtualKeyboard] Direct open blocked because a custom window is already open.");
                        return;
                    }

                    EnsureOverlayWindowCreated();

                    if (overlayWindow == null)
                    {
                        overlayOpenOrOpening = false;
                        return;
                    }

                    SyncOverlayMouseInputWithPlaynite();
                    overlayWindow.Refresh();

                    // Prepare the keyboard while the WPF window is still hidden.
                    // This prevents the regular overlay menu from flashing first.
                    overlayWindow.ShowVirtualKeyboardDirect();

                    if (!overlayWindow.IsVisible)
                    {
                        overlayWindow.Show();
                    }

                    overlayWindow.Topmost = false;
                    overlayWindow.Topmost = true;
                    overlayWindow.Activate();
                    overlayWindow.Focus();

                    try
                    {
                        var overlayHandle = new System.Windows.Interop.WindowInteropHelper(overlayWindow).Handle;
                        if (overlayHandle != IntPtr.Zero)
                        {
                            ForceFocusWindow(overlayHandle);
                        }
                    }
                    catch
                    {
                    }

                    OverlayDebugLog($"[Overlay][VirtualKeyboard] Opened directly. Source={source}");
                    QueueOverlayForegroundRecovery(
                        overlayWindow,
                        $"VirtualKeyboard:{source}",
                        focusOverlayButton: false);
                }
                catch (Exception ex)
                {
                    overlayOpenOrOpening = false;
                    logger?.Warn(ex, "[AnikiHelper] Failed to open the virtual keyboard directly.");
                }
                finally
                {
                    lock (overlayToggleLock)
                    {
                        overlayToggleQueued = false;

                        if (overlayWindow == null || !overlayWindow.IsVisible)
                        {
                            overlayOpenOrOpening = false;
                        }
                    }
                }
            }));
        }

        public void OpenWindowsVirtualKeyboardFromOverlay()
        {
            QueueWindowsVirtualKeyboardOpen(hideOverlay: true, source: "OverlayButton");
        }

        private void QueueWindowsVirtualKeyboardOpen(bool hideOverlay, string source)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                logger?.Warn("[AnikiHelper] Windows virtual keyboard could not be opened because the UI dispatcher is unavailable.");
                return;
            }

            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await OpenWindowsVirtualKeyboardAsync(hideOverlay, source);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to open the Windows virtual keyboard.");
                }
            }));
        }

        private async Task OpenWindowsVirtualKeyboardAsync(bool hideOverlay, string source)
        {
            IntPtr targetWindow;
            int targetProcessId;

            if (!TryResolveVirtualKeyboardTarget(out targetWindow, out targetProcessId))
            {
                if (hideOverlay)
                {
                    HideOverlayImmediate();
                }

                logger?.Warn("[AnikiHelper] Windows virtual keyboard could not resolve a valid target window.");
                return;
            }

            if (hideOverlay)
            {
                HideOverlayImmediate();
            }

            var focused = await FocusVirtualKeyboardTargetWithRetriesAsync(
                targetWindow,
                targetProcessId,
                "windowsVirtualKeyboard");

            if (!focused)
            {
                logger?.Warn("[AnikiHelper] Windows virtual keyboard stopped because target focus could not be restored.");
                return;
            }

            await Task.Delay(WindowsVirtualKeyboardAfterFocusDelayMs);

            // Do not blindly call Toggle: if the touch keyboard is already visible,
            // Toggle would close it instead of opening it.
            if (IsWindowsInputPaneVisible())
            {
                OverlayDebugLog(
                    $"[Overlay][VirtualKeyboard] Windows keyboard is already visible. " +
                    $"Source={source}, PID={targetProcessId}, Window={targetWindow}");
                return;
            }

            // Starting TabTip is kept only as a bootstrap step. On recent Windows
            // builds it can start TextInputHost without actually showing the pane.
            string tabTipPath;
            if (TryResolveTabTipPath(out tabTipPath))
            {
                TryStartTabTipProcess(tabTipPath);
                await Task.Delay(WindowsVirtualKeyboardTabTipBootstrapDelayMs);

                if (IsWindowsInputPaneVisible())
                {
                    OverlayDebugLog(
                        $"[Overlay][VirtualKeyboard] Windows keyboard became visible after TabTip bootstrap. " +
                        $"Source={source}, PID={targetProcessId}, Window={targetWindow}");
                    return;
                }
            }
            else
            {
                OverlayDebugLog("[Overlay][VirtualKeyboard] TabTip.exe was not found; trying COM invocation directly.");
            }

            // ITipInvocation is the same shell COM path commonly used by desktop
            // applications to request the touch keyboard. It is intentionally kept
            // behind the experimental Windows keyboard provider.
            if (!TryToggleWindowsInputPane())
            {
                logger?.Warn("[AnikiHelper] Windows touch keyboard COM invocation failed.");
                return;
            }

            await Task.Delay(WindowsVirtualKeyboardVisibilityCheckDelayMs);

            var isVisible = IsWindowsInputPaneVisible();
            OverlayDebugLog(
                $"[Overlay][VirtualKeyboard] Windows keyboard COM invocation completed. " +
                $"Visible={isVisible}, Source={source}, PID={targetProcessId}, Window={targetWindow}");

            if (!isVisible)
            {
                logger?.Warn(
                    "[AnikiHelper] Windows touch keyboard was requested, but Windows did not report it as visible.");
            }
        }

        private void TryStartTabTipProcess(string tabTipPath)
        {
            if (string.IsNullOrWhiteSpace(tabTipPath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = tabTipPath,
                    WorkingDirectory = Path.GetDirectoryName(tabTipPath),
                    UseShellExecute = true
                });

                OverlayDebugLog($"[Overlay][VirtualKeyboard] TabTip bootstrap requested. Path={tabTipPath}");
            }
            catch (Exception ex)
            {
                // COM invocation may still work, so do not abort here.
                logger?.Warn(ex, "[AnikiHelper] Failed to start TabTip.exe; trying COM invocation.");
            }
        }

        private bool IsWindowsInputPaneVisible()
        {
            object frameworkInputPaneObject = null;

            try
            {
                frameworkInputPaneObject = new FrameworkInputPane();
                var frameworkInputPane = (IFrameworkInputPane)frameworkInputPaneObject;

                NativeRectangle location;
                var result = frameworkInputPane.Location(out location);
                if (result < 0)
                {
                    return false;
                }

                return location.Width > 0 && location.Height > 0;
            }
            catch (Exception ex)
            {
                OverlayDebugLog(
                    $"[Overlay][VirtualKeyboard] Could not query Windows input pane visibility: {ex.Message}");
                return false;
            }
            finally
            {
                ReleaseComObjectSafely(frameworkInputPaneObject);
            }
        }

        private bool TryToggleWindowsInputPane()
        {
            object uiHostObject = null;

            try
            {
                uiHostObject = new UIHostNoLaunch();
                var invocation = (ITipInvocation)uiHostObject;
                invocation.Toggle(GetDesktopWindow());
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] ITipInvocation.Toggle failed.");
                return false;
            }
            finally
            {
                ReleaseComObjectSafely(uiHostObject);
            }
        }

        private static void ReleaseComObjectSafely(object value)
        {
            if (value == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(value))
                {
                    Marshal.ReleaseComObject(value);
                }
            }
            catch
            {
            }
        }

        private static bool TryResolveTabTipPath(out string tabTipPath)
        {
            tabTipPath = null;

            var candidates = new List<string>();
            var commonProgramW6432 = Environment.GetEnvironmentVariable("CommonProgramW6432");
            var commonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);

            if (!string.IsNullOrWhiteSpace(commonProgramW6432))
            {
                candidates.Add(Path.Combine(commonProgramW6432, "microsoft shared", "ink", "TabTip.exe"));
            }

            if (!string.IsNullOrWhiteSpace(commonProgramFiles))
            {
                candidates.Add(Path.Combine(commonProgramFiles, "microsoft shared", "ink", "TabTip.exe"));
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                candidates.Add(Path.Combine(programFiles, "Common Files", "microsoft shared", "ink", "TabTip.exe"));
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                {
                    tabTipPath = candidate;
                    return true;
                }
            }

            return false;
        }

        private void OpenOverlayInternal(
            bool ignoreEnabledSetting,
            string source,
            bool preserveCapturedTarget = false,
            bool? openedFromPlayniteOverride = null)
        {
            if (!ignoreEnabledSetting && settings != null && !settings.InGameOverlayEnabled)
            {
                return;
            }

            // Capture the target immediately, before the request is queued on Playnite's
            // UI thread and before the overlay can steal focus. Capturing later can return
            // Playnite or the overlay instead of the application the user was typing in.
            if (!preserveCapturedTarget && (overlayWindow == null || !overlayWindow.IsVisible))
            {
                CaptureVirtualKeyboardTargetWindow();
            }

            // A custom Aniki window and the in-game overlay must never be active at the same time.
            // Check once before reserving the overlay opening, then again on the UI thread below.
            if (!overlayOpenOrOpening && HasOpenCustomWindow())
            {
                OverlayDebugLog($"[Overlay] Open blocked because a custom window is already open. Source={source}");
                return;
            }

            lock (overlayToggleLock)
            {
                var now = DateTime.UtcNow;

                if (overlayToggleQueued)
                {
                    return;
                }

                if ((now - lastOverlayToggleUtc).TotalMilliseconds < OverlayToggleCooldownMs)
                {
                    return;
                }

                overlayToggleQueued = true;
                overlayOpenOrOpening = true;
                lastOverlayToggleUtc = now;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                lock (overlayToggleLock)
                {
                    overlayToggleQueued = false;
                    overlayOpenOrOpening = false;
                }

                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (overlayWindow != null && overlayWindow.IsVisible)
                    {
                        overlayOpenOrOpening = true;
                        var visibleWindow = overlayWindow;
                        TryBringOverlayToForeground(visibleWindow, $"AlreadyVisible:{source}", focusOverlayButton: true);
                        QueueOverlayForegroundRecovery(visibleWindow, $"AlreadyVisible:{source}");
                        return;
                    }

                    // The custom page may have opened after the shortcut was detected but before
                    // this queued callback runs. This second check closes that race condition.
                    if (HasOpenCustomWindow())
                    {
                        overlayOpenOrOpening = false;
                        OverlayDebugLog($"[Overlay] Queued open cancelled because a custom window opened first. Source={source}");
                        return;
                    }

                    OverlayDebugLog($"[Overlay] Open requested. Source={source}, IgnoreEnabledSetting={ignoreEnabledSetting}");
                    ShowOverlay(openedFromPlayniteOverride);
                }
                catch (Exception ex)
                {
                    overlayOpenOrOpening = false;
                    ResumeSuspendedGameProcess();
                    logger.Error(ex, $"[AnikiHelper] Failed to open in-game overlay. Source={source}");
                }
                finally
                {
                    lock (overlayToggleLock)
                    {
                        overlayToggleQueued = false;

                        if (overlayWindow == null || !overlayWindow.IsVisible)
                        {
                            overlayOpenOrOpening = false;
                        }
                    }
                }
            }));
        }

        private bool HasOpenCustomWindow()
        {
            try
            {
                return hasOpenCustomWindow?.Invoke() == true;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to query custom-window state.");
                return false;
            }
        }

        private void HandleOverlayControllerInput(ControllerInput button)
        {
            OverlayDebugLog($"[OverlayInput] HandleOverlayControllerInput: {button}");

            // The D-pad can reach the overlay twice: once as an SDL button and once as a
            // native WPF/Playnite arrow key. Give the native path a very short opportunity
            // to handle it first, then use SDL only as a fallback. This keeps joystick and
            // D-pad navigation on the same focus path without losing compatibility in games
            // that do not forward controller navigation to WPF.
            if (IsDPadButton(button))
            {
                QueueOverlayDPadFallback(button);
                return;
            }

            if (!IsOverlayWindowForeground(button))
            {
                if ((button == ControllerInput.B || button == ControllerInput.Back) &&
                    TrySendEscapeToForegroundPlayniteWindow())
                {
                    OverlayDebugLog($"[OverlayInput] Sent Escape to foreground Playnite window. Button={button}");
                    return;
                }

                var visibleWindow = overlayWindow;
                if (visibleWindow != null && visibleWindow.IsVisible)
                {
                    QueueOverlayForegroundRecovery(visibleWindow, $"InputRecovery:{button}");
                }

                OverlayDebugLog($"[OverlayInput] Ignored because overlay is not foreground. Button={button}");
                return;
            }

            try
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (overlayWindow != null && overlayWindow.IsVisible)
                        {
                            overlayWindow.HandleOverlayControllerInput(button);
                        }
                    }
                    catch
                    {
                    }
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            catch
            {
            }
        }

        private static bool IsDPadButton(ControllerInput button)
        {
            return button == ControllerInput.DPadLeft ||
                   button == ControllerInput.DPadRight ||
                   button == ControllerInput.DPadUp ||
                   button == ControllerInput.DPadDown;
        }

        private async void QueueOverlayDPadFallback(ControllerInput button)
        {
            var windowAtRequest = overlayWindow;
            if (windowAtRequest == null || !windowAtRequest.IsVisible)
            {
                return;
            }

            var requestedUtc = DateTime.UtcNow;

            try
            {
                if (!IsOverlayWindowForeground(button))
                {
                    // This callback runs on the SDL polling thread. Queue the WPF focus
                    // recovery instead of touching the Window directly from this thread.
                    QueueOverlayForegroundRecovery(windowAtRequest, $"DPadRecovery:{button}");
                }

                await Task.Delay(OverlayDPadFallbackDelayMs).ConfigureAwait(false);

                var dispatcher = windowAtRequest.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!ReferenceEquals(overlayWindow, windowAtRequest) ||
                            !windowAtRequest.IsVisible)
                        {
                            return;
                        }

                        if (!windowAtRequest.IsActive || !windowAtRequest.IsKeyboardFocusWithin)
                        {
                            TryBringOverlayToForeground(windowAtRequest, $"DPadFallback:{button}", focusOverlayButton: true);
                        }

                        windowAtRequest.HandleOverlayDPadFallback(button, requestedUtc);
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug(ex, "[AnikiHelper][OverlayInput] D-pad fallback failed.");
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][OverlayInput] Failed to queue D-pad fallback.");
            }
        }

        private bool TrySendEscapeToForegroundPlayniteWindow()
        {
            try
            {
                if (overlayWindow == null || !overlayWindow.IsVisible)
                {
                    return false;
                }

                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                {
                    return false;
                }

                uint foregroundPid;
                GetWindowThreadProcessId(foregroundWindow, out foregroundPid);

                if (foregroundPid <= 0)
                {
                    return false;
                }

                var currentProcessId = Process.GetCurrentProcess().Id;

                // Sécurité : on envoie Escape seulement aux fenêtres Playnite/plugin,
                // jamais directement au jeu.
                if (foregroundPid != (uint)currentProcessId)
                {
                    return false;
                }

                PostMessage(foregroundWindow, WM_KEYDOWN, new IntPtr(VK_ESCAPE), IntPtr.Zero);
                PostMessage(foregroundWindow, WM_KEYUP, new IntPtr(VK_ESCAPE), IntPtr.Zero);

                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][OverlayInput] Failed to send Escape to foreground Playnite window.");
                return false;
            }
        }

        public bool HandleControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            // SDL handles the browser controls. Swallow Playnite's native controller
            // events while the browser owns focus so Start/Back/Y cannot open another
            // Fullscreen menu underneath the Chromium window.
            if (isWebBrowserActive?.Invoke() == true)
            {
                return true;
            }

            if (settings != null && !settings.InGameOverlayEnabled && !IsOverlayVisible)
            {
                return false;
            }

            if (args == null)
            {
                return false;
            }

            var shortcut = settings?.InGameOverlayControllerShortcut ?? "StartBack";

            if (string.Equals(shortcut, "Guide", StringComparison.OrdinalIgnoreCase) &&
                args.Button == ControllerInput.Guide)
            {
                if (args.State == ControllerInputState.Pressed)
                {
                    controllerGuidePressedAt = DateTime.Now;
                    return true;
                }

                if (controllerGuidePressedAt.HasValue)
                {
                    var heldMs = (DateTime.Now - controllerGuidePressedAt.Value).TotalMilliseconds;
                    controllerGuidePressedAt = null;

                    if (heldMs <= GuideShortPressMaxMs)
                    {
                        var now = DateTime.Now;

                        if ((now - lastControllerShortcutTime).TotalMilliseconds < 350)
                        {
                            return true;
                        }

                        lastControllerShortcutTime = now;
                        ToggleOverlay();
                        return true;
                    }

                    OverlayDebugLog($"[OverlayInput] Guide hold ignored. HeldMs={heldMs:0}");
                    return false;
                }

                return false;
            }

            var mostRecentPress = UpdateControllerState(args);

            if (overlayWindow != null && overlayWindow.IsVisible)
            {
                return true;
            }

            if (mostRecentPress == null)
            {
                return false;
            }

            if (IsControllerShortcutTriggered(mostRecentPress.Value))
            {
                var now = DateTime.Now;

                if ((now - lastControllerShortcutTime).TotalMilliseconds < 350)
                {
                    return true;
                }

                lastControllerShortcutTime = now;
                ToggleOverlay();
                return true;
            }

            return false;
        }

        private ControllerInput? UpdateControllerState(OnControllerButtonStateChangedArgs args)
        {
            var pressed = args.State == ControllerInputState.Pressed;

            switch (args.Button)
            {
                case ControllerInput.Start:
                    controllerStart = pressed;
                    break;

                case ControllerInput.Back:
                    controllerBack = pressed;
                    break;

                case ControllerInput.Y:
                    controllerY = pressed;
                    break;

                default:
                    break;
            }

            return pressed ? args.Button : (ControllerInput?)null;
        }

        private bool IsControllerShortcutTriggered(ControllerInput mostRecentPress)
        {
            var shortcut = settings?.InGameOverlayControllerShortcut ?? "StartBack";

            if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            switch (shortcut)
            {
                case "Guide":
                    return mostRecentPress == ControllerInput.Guide;

                case "BackY":
                    return (mostRecentPress == ControllerInput.Back || mostRecentPress == ControllerInput.Y) &&
                           controllerBack &&
                           controllerY;

                case "StartBack":
                default:
                    return (mostRecentPress == ControllerInput.Start || mostRecentPress == ControllerInput.Back) &&
                           controllerStart &&
                           controllerBack;
            }
        }


        private void EnsureOverlayWindowCreated()
        {
            if (overlayWindow != null)
            {
                return;
            }

            overlayWindow = new AnikiInGameOverlayWindow(this);
            overlayWindow.ShowInTaskbar = false;
            overlayWindow.Closed += OverlayWindow_Closed;
        }

        private void OverlayWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                if (closeOverlayShouldResumeSuspendedGame)
                {
                    ResumeSuspendedGameProcess();
                }
                else
                {
                    OverlayDebugLog("[Overlay][Suspend] Overlay closed while returning to Playnite. Keeping game suspended until ReturnToGame.");
                }
            }
            finally
            {
                Interlocked.Increment(ref overlayForegroundRecoveryGeneration);
                closeOverlayShouldResumeSuspendedGame = true;

                if (ReferenceEquals(overlayWindow, sender))
                {
                    overlayWindow = null;
                    overlayOpenOrOpening = false;
                }
                else if (overlayWindow == null)
                {
                    overlayOpenOrOpening = false;
                }
            }
        }

        private void ScheduleOverlayPreloadForGame(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            if (settings != null && !settings.InGameOverlayEnabled)
            {
                return;
            }

            var generation = Interlocked.Increment(ref overlayPreloadGeneration);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500).ConfigureAwait(false);

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher == null)
                    {
                        return;
                    }

                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        PreloadOverlayIfStillCurrent(gameId, generation);
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                catch (Exception ex)
                {
                    logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to schedule overlay preload.");
                }
            });
        }

        private void PreloadOverlayIfStillCurrent(Guid gameId, int generation)
        {
            try
            {
                if (generation != overlayPreloadGeneration)
                {
                    return;
                }

                if (currentGame == null || currentGame.Id != gameId)
                {
                    return;
                }

                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    return;
                }

                EnsureOverlayWindowCreated();

                if (overlayWindow == null || overlayWindow.IsVisible)
                {
                    return;
                }

                overlayWindow.Refresh();
                RefreshOverlayDataAfterShowAsync(overlayWindow, gameId);

                OverlayDebugLog($"[Overlay][Preload] Overlay window preloaded. Game={currentGame?.Name}");
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to preload overlay window.");
            }
        }


        private void RefreshOverlayDataAfterShowAsync(AnikiInGameOverlayWindow windowAtOpen, Guid gameIdAtOpen)
        {
            if (gameIdAtOpen == Guid.Empty)
            {
                return;
            }

            var gameAtOpen = currentGame;
            if (gameAtOpen == null || gameAtOpen.Id != gameIdAtOpen)
            {
                return;
            }

            try
            {
                var now = DateTime.UtcNow;

                lock (achievementSummaryLock)
                {
                    if (cachedAchievementGameId == gameIdAtOpen &&
                        cachedAchievementSummary != null &&
                        (now - cachedAchievementCheckedUtc) < TimeSpan.FromMinutes(2))
                    {
                        return;
                    }
                }
            }
            catch
            {
            }

            var generation = Interlocked.Increment(ref overlayDataRefreshGeneration);

            _ = Task.Run(() =>
            {
                AchievementOverlaySummary summary = null;

                try
                {
                    OverlayDebugLog("[OverlayCache] Async refresh START");

                    summary =
                        LoadPlayniteAchievementsSummary(gameAtOpen)
                        ?? new AchievementOverlaySummary();

                    OverlayDebugLog("[OverlayCache] Async refresh END");
                }
                catch (Exception ex)
                {
                    logger?.Debug(ex, "[AnikiHelper] Failed to refresh overlay data asynchronously.");
                    summary = new AchievementOverlaySummary();
                }

                try
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher == null)
                    {
                        return;
                    }

                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (generation != overlayDataRefreshGeneration)
                            {
                                return;
                            }

                            if (currentGame == null || currentGame.Id != gameIdAtOpen)
                            {
                                return;
                            }

                            lock (achievementSummaryLock)
                            {
                                cachedAchievementGameId = gameIdAtOpen;
                                cachedAchievementCheckedUtc = DateTime.UtcNow;
                                cachedAchievementSummary = summary;
                            }

                            if (overlayWindow == windowAtOpen && overlayWindow != null && overlayWindow.IsVisible)
                            {
                                overlayWindow.Refresh();
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.Debug(ex, "[AnikiHelper] Failed to apply async overlay data refresh.");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch
                {
                }
            });
        }

        private void SuspendGameAfterOverlayIsVisibleAsync(AnikiInGameOverlayWindow windowAtOpen, Guid gameIdAtOpen)
        {
            if (gameIdAtOpen == Guid.Empty)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(80);

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher == null)
                    {
                        return;
                    }

                    var canSuspend = false;

                    dispatcher.Invoke(() =>
                    {
                        canSuspend =
                            overlayWindow == windowAtOpen &&
                            overlayWindow != null &&
                            overlayWindow.IsVisible &&
                            currentGame != null &&
                            currentGame.Id == gameIdAtOpen;
                    });

                    if (!canSuspend)
                    {
                        return;
                    }

                    TrySuspendCurrentGameForOverlay();
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][Overlay] Delayed suspend failed.");
                    ResumeSuspendedGameProcess();
                }
            });
        }

        public void ShowOverlay(bool? openedFromPlayniteOverride = null)
        {
            overlayOpenOrOpening = true;
            overlayOpenedFromPlaynite = openedFromPlayniteOverride ??
                                         (IsPlayniteCurrentlyForeground() || currentGame == null);

            var shouldSuspendGameAfterOverlayIsVisible = currentGame != null && !overlayOpenedFromPlaynite;
            var gameIdAtOpen = currentGame != null ? currentGame.Id : Guid.Empty;

            if (shouldSuspendGameAfterOverlayIsVisible)
            {
                CaptureCurrentForegroundGameWindow();
            }
            else if (currentGame == null)
            {
                // Outside a game, remember the application that was active before the
                // overlay opened. The virtual keyboard can then restore that window and
                // send its text to Firefox, Notepad, Discord, or any other text field.
                CaptureCurrentForegroundGameWindow();
            }

            EnsureOverlayWindowCreated();

            if (overlayWindow == null)
            {
                overlayOpenOrOpening = false;
                return;
            }

            SyncOverlayMouseInputWithPlaynite();
            overlayWindow.PrepareForShowAnimation();

            if (!overlayWindow.IsVisible)
            {
                overlayWindow.Show();
            }

            overlayWindow.Topmost = false;
            overlayWindow.Topmost = true;
            overlayWindow.Activate();
            overlayWindow.Focus();

            try
            {
                overlayWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        overlayWindow?.Refresh();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to refresh in-game overlay after show.");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
            }

            overlayWindow.Topmost = false;
            overlayWindow.Topmost = true;
            overlayWindow.Activate();
            overlayWindow.Focus();

            try
            {
                var overlayHandle = new System.Windows.Interop.WindowInteropHelper(overlayWindow).Handle;
                if (overlayHandle != IntPtr.Zero)
                {
                    ForceFocusWindow(overlayHandle);
                }
            }
            catch
            {
            }

            overlayWindow.PlayShowAnimation();
            overlayWindow.FocusOverlayButton();

            var windowAtOpen = overlayWindow;
            QueueOverlayForegroundRecovery(windowAtOpen, "InitialShow");

            if (shouldSuspendGameAfterOverlayIsVisible)
            {
                SuspendGameAfterOverlayIsVisibleAsync(windowAtOpen, gameIdAtOpen);
            }

            RefreshOverlayDataAfterShowAsync(windowAtOpen, gameIdAtOpen);
        }

        private async void QueueOverlayForegroundRecovery(
            AnikiInGameOverlayWindow expectedWindow,
            string reason,
            bool focusOverlayButton = true)
        {
            if (expectedWindow == null)
            {
                return;
            }

            var generation = Interlocked.Increment(ref overlayForegroundRecoveryGeneration);
            // The later attempts are intentional: Guide can briefly bring Playnite
            // forward after our first focus call, depending on Steam Input and drivers.
            var retryDelaysMs = new[] { 0, 100, 220, 450, 800 };
            var previousDelay = 0;

            try
            {
                foreach (var absoluteDelay in retryDelaysMs)
                {
                    var waitMs = absoluteDelay - previousDelay;
                    previousDelay = absoluteDelay;

                    if (waitMs > 0)
                    {
                        await Task.Delay(waitMs).ConfigureAwait(false);
                    }

                    if (generation != Volatile.Read(ref overlayForegroundRecoveryGeneration))
                    {
                        return;
                    }

                    var dispatcher = expectedWindow.Dispatcher;
                    if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                    {
                        return;
                    }

                    var completion = new TaskCompletionSource<bool>();

                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (generation != Volatile.Read(ref overlayForegroundRecoveryGeneration) ||
                                !ReferenceEquals(overlayWindow, expectedWindow) ||
                                !expectedWindow.IsVisible)
                            {
                                completion.TrySetResult(false);
                                return;
                            }

                            completion.TrySetResult(
                                TryBringOverlayToForeground(
                                    expectedWindow,
                                    $"{reason}:Attempt{absoluteDelay}",
                                    focusOverlayButton));
                        }
                        catch (Exception ex)
                        {
                            logger?.Debug(ex, "[AnikiHelper][Overlay] Foreground recovery attempt failed.");
                            completion.TrySetResult(false);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Send);

                    if (await completion.Task.ConfigureAwait(false))
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Foreground recovery sequence failed.");
            }
        }

        private bool TryBringOverlayToForeground(
            AnikiInGameOverlayWindow targetWindow,
            string reason,
            bool focusOverlayButton)
        {
            if (targetWindow == null || !targetWindow.IsVisible)
            {
                return false;
            }

            try
            {
                var overlayHandle = new System.Windows.Interop.WindowInteropHelper(targetWindow).Handle;

                targetWindow.Topmost = false;
                targetWindow.Topmost = true;

                if (overlayHandle != IntPtr.Zero)
                {
                    ShowWindowAsync(overlayHandle, SW_SHOW);
                    BringWindowToTop(overlayHandle);
                    ForceFocusWindow(overlayHandle);
                }

                targetWindow.Activate();
                targetWindow.Focus();

                if (focusOverlayButton)
                {
                    targetWindow.FocusOverlayButton();
                }

                var foregroundHandle = GetForegroundWindow();
                var foregroundAcquired = overlayHandle != IntPtr.Zero && foregroundHandle == overlayHandle;
                var ready = foregroundAcquired &&
                            targetWindow.IsActive &&
                            targetWindow.IsKeyboardFocusWithin;

                OverlayDebugLog(
                    $"[Overlay][FocusRecovery] Reason={reason}, " +
                    $"Foreground={foregroundAcquired}, Active={targetWindow.IsActive}, " +
                    $"KeyboardFocusWithin={targetWindow.IsKeyboardFocusWithin}, Ready={ready}");

                return ready;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, $"[AnikiHelper][Overlay] Failed to bring overlay to foreground. Reason={reason}");
                return false;
            }
        }

        private void SyncOverlayMouseInputWithPlaynite()
        {
            if (overlayWindow == null)
            {
                return;
            }

            var mouseInputEnabled = IsPlayniteMouseInputEnabled();
            overlayWindow.IsHitTestVisible = mouseInputEnabled;

            OverlayDebugLog($"[Overlay] Mouse input synchronized with Playnite. Enabled={mouseInputEnabled}");
        }

        private bool IsPlayniteMouseInputEnabled()
        {
            try
            {
                // Playnite disables mouse interaction by setting IsHitTestVisible=false
                // on its fullscreen WindowBase instances when Hide mouse cursor is enabled.
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    return mainWindow.IsHitTestVisible;
                }

                var currentAppWindow = playniteApi?.Dialogs?.GetCurrentAppWindow();
                if (currentAppWindow != null)
                {
                    return currentAppWindow.IsHitTestVisible;
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to read Playnite mouse-input state.");
            }

            // Preserve mouse support if Playnite's window state cannot be read.
            return true;
        }

        private void CaptureVirtualKeyboardTargetWindow()
        {
            try
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero || !IsWindow(handle))
                {
                    virtualKeyboardTargetWindow = IntPtr.Zero;
                    virtualKeyboardTargetProcessId = null;
                    return;
                }

                // Never replace the target with the overlay itself.
                try
                {
                    if (overlayWindow != null)
                    {
                        var overlayHandle = new System.Windows.Interop.WindowInteropHelper(overlayWindow).Handle;
                        if (overlayHandle != IntPtr.Zero && handle == overlayHandle)
                        {
                            return;
                        }
                    }
                }
                catch
                {
                }

                uint processId;
                GetWindowThreadProcessId(handle, out processId);

                if (processId <= 0)
                {
                    virtualKeyboardTargetWindow = IntPtr.Zero;
                    virtualKeyboardTargetProcessId = null;
                    return;
                }

                virtualKeyboardTargetWindow = handle;
                virtualKeyboardTargetProcessId = (int)processId;

                OverlayDebugLog(
                    $"[Overlay][VirtualKeyboard] Target captured before overlay. " +
                    $"PID={processId}, Window={handle}");
            }
            catch (Exception ex)
            {
                virtualKeyboardTargetWindow = IntPtr.Zero;
                virtualKeyboardTargetProcessId = null;
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to capture the virtual keyboard target window.");
            }
        }

        private void CaptureCurrentForegroundGameWindow()
        {
            try
            {
                var handle = GetForegroundWindow();
                lastForegroundWindow = handle;
                lastForegroundWindowProcessId = null;

                if (handle != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(handle, out pid);

                    if (pid > 0)
                    {
                        lastForegroundWindowProcessId = (int)pid;
                    }
                }
            }
            catch
            {
                lastForegroundWindow = IntPtr.Zero;
                lastForegroundWindowProcessId = null;
            }
        }

        public void HideOverlay()
        {
            try
            {
                // Do not keep the overlay window alive after closing it.
                // Reusing the same hidden WPF window can show the last rendered frame
                // for a split second on the next Show(), before the new show animation
                // has a chance to reset opacity/translation. Closing/recreating the
                // window makes every opening behave like the first one.
                HideOverlayImmediate();
                RestoreGameFocus();
            }
            catch
            {
            }
        }

        public void HideOverlayWithoutRestoringGameFocus()
        {
            try
            {
                // Same as HideOverlay(), but keep focus restoration disabled for cases
                // where the caller intentionally wants Playnite or another window to keep focus.
                HideOverlayImmediate();

                lastForegroundWindow = IntPtr.Zero;
                lastForegroundWindowProcessId = null;
            }
            catch
            {
            }
        }


        private bool TrySuspendCurrentGameForOverlay()
        {
            ResumeSuspendedGameProcess();

            if (!ShouldSuspendCurrentGameForOverlay())
            {
                return false;
            }

            var processId = GetGameProcessIdToSuspend();
            if (!processId.HasValue || processId.Value <= 0)
            {
                return false;
            }

            var currentProcessId = Process.GetCurrentProcess().Id;
            if (processId.Value == currentProcessId)
            {
                OverlayDebugLog("[Overlay][Suspend] Refused to suspend current plugin/Playnite process.");
                return false;
            }

            var suspendedThreadIds = new List<int>();

            lock (gameSuspendLock)
            {
                try
                {
                    var process = Process.GetProcessById(processId.Value);
                    if (process == null)
                    {
                        return false;
                    }

                    try
                    {
                        if (process.HasExited)
                        {
                            return false;
                        }
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        OverlayDebugLog($"[Overlay][Suspend] Could not check HasExited, continuing anyway. Process={processId.Value}, Error={ex.Message}");
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }

                    var processName = process.ProcessName ?? string.Empty;
                    if (processName.IndexOf("Playnite", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        OverlayDebugLog("[Overlay][Suspend] Refused to suspend Playnite process.");
                        return false;
                    }

                    foreach (ProcessThread thread in process.Threads)
                    {
                        IntPtr threadHandle = IntPtr.Zero;

                        try
                        {
                            threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
                            if (threadHandle == IntPtr.Zero)
                            {
                                OverlayDebugLog($"[Overlay][Suspend] OpenThread failed. Process={processId.Value}, Thread={thread.Id}");
                                ResumeThreadIds(suspendedThreadIds);
                                return false;
                            }

                            var result = SuspendThread(threadHandle);
                            if (result == uint.MaxValue)
                            {
                                OverlayDebugLog($"[Overlay][Suspend] SuspendThread failed. Process={processId.Value}, Thread={thread.Id}");
                                ResumeThreadIds(suspendedThreadIds);
                                return false;
                            }

                            suspendedThreadIds.Add(thread.Id);
                        }
                        finally
                        {
                            if (threadHandle != IntPtr.Zero)
                            {
                                CloseHandle(threadHandle);
                            }
                        }
                    }

                    suspendedGameProcessId = processId.Value;
                    suspendedGameThreadIds.Clear();
                    suspendedGameThreadIds.AddRange(suspendedThreadIds);

                    OverlayDebugLog($"[Overlay][Suspend] Suspended process {processId.Value} ({suspendedGameThreadIds.Count} threads). Game={currentGame?.Name}");
                    return suspendedGameThreadIds.Count > 0;
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to suspend current game process.");
                    ResumeThreadIds(suspendedThreadIds);
                    suspendedGameProcessId = null;
                    suspendedGameThreadIds.Clear();
                    return false;
                }
            }
        }

        private bool ShouldSuspendCurrentGameForOverlay()
        {
            if (settings == null || !settings.IsInGameOverlaySuspendGameEnabled())
            {
                return false;
            }

            if (overlayOpenedFromPlaynite || currentGame == null || currentGame.Id == Guid.Empty)
            {
                return false;
            }

            if (settings.IsInGameOverlayNeverSuspendGame(currentGame.Id))
            {
                OverlayDebugLog($"[Overlay][Suspend] Skipped because game is in never suspend list. Game={currentGame.Name}");
                return false;
            }

            return true;
        }

        private int? GetGameProcessIdToSuspend()
        {
            if (lastForegroundWindowProcessId.HasValue && lastForegroundWindowProcessId.Value > 0)
            {
                return lastForegroundWindowProcessId.Value;
            }

            if (currentGameProcessId.HasValue && currentGameProcessId.Value > 0)
            {
                return currentGameProcessId.Value;
            }

            return null;
        }

        private bool IsGameProcessSuspended()
        {
            lock (gameSuspendLock)
            {
                return suspendedGameProcessId.HasValue && suspendedGameThreadIds.Count > 0;
            }
        }

        private void ResumeSuspendedGameProcess()
        {
            lock (gameSuspendLock)
            {
                if (!suspendedGameProcessId.HasValue || suspendedGameThreadIds.Count == 0)
                {
                    return;
                }

                try
                {
                    ResumeThreadIds(suspendedGameThreadIds);
                    OverlayDebugLog($"[Overlay][Suspend] Resumed process {suspendedGameProcessId.Value} ({suspendedGameThreadIds.Count} threads).");
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to resume suspended game process.");
                }
                finally
                {
                    suspendedGameProcessId = null;
                    suspendedGameThreadIds.Clear();
                }
            }
        }

        private void ResumeThreadIds(IEnumerable<int> threadIds)
        {
            if (threadIds == null)
            {
                return;
            }

            foreach (var threadId in threadIds.ToList())
            {
                IntPtr threadHandle = IntPtr.Zero;

                try
                {
                    threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)threadId);
                    if (threadHandle != IntPtr.Zero)
                    {
                        ResumeThread(threadHandle);
                    }
                }
                catch
                {
                }
                finally
                {
                    if (threadHandle != IntPtr.Zero)
                    {
                        CloseHandle(threadHandle);
                    }
                }
            }
        }

        private void RestoreGameFocus()
        {
            try
            {
                if (lastForegroundWindow != IntPtr.Zero)
                {
                    ForceFocusWindow(lastForegroundWindow);
                }
            }
            catch
            {
            }
        }

        private bool IsOverlayWindowForeground(ControllerInput button)
        {
            try
            {
                if (overlayWindow == null)
                {
                    return false;
                }

                bool isActive = false;
                bool isKeyboardFocusWithin = false;

                overlayWindow.Dispatcher.Invoke(() =>
                {
                    isActive = overlayWindow.IsActive;
                    isKeyboardFocusWithin = overlayWindow.IsKeyboardFocusWithin;
                });

                OverlayDebugLog(
                    $"[OverlayInput][FocusCheck] Button={button}, " +
                    $"IsActive={isActive}, IsKeyboardFocusWithin={isKeyboardFocusWithin}"
                );

                return isActive && isKeyboardFocusWithin;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][OverlayInput][FocusCheck] Failed.");
                return true;
            }
        }

        private bool IsSteamBigPictureGuideExclusionActive()
        {
            if (!string.Equals(
                settings?.InGameOverlayControllerShortcut,
                "Guide",
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var foregroundWindow = GetForegroundWindow();

                if (foregroundWindow == IntPtr.Zero || !IsWindow(foregroundWindow))
                {
                    return false;
                }

                uint foregroundPid;
                GetWindowThreadProcessId(foregroundWindow, out foregroundPid);

                if (foregroundPid <= 0)
                {
                    return false;
                }

                using (var foregroundProcess = Process.GetProcessById((int)foregroundPid))
                {
                    if (!string.Equals(
                        foregroundProcess.ProcessName,
                        "steamwebhelper",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                var titleLength = GetWindowTextLength(foregroundWindow);
                if (titleLength <= 0)
                {
                    return false;
                }

                var titleBuilder = new StringBuilder(titleLength + 1);
                if (GetWindowText(foregroundWindow, titleBuilder, titleBuilder.Capacity) <= 0)
                {
                    return false;
                }

                return titleBuilder
                    .ToString()
                    .IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex)
            {
                logger?.Debug(
                    ex,
                    "[AnikiHelper][Overlay] Failed to detect whether Steam Big Picture is foreground.");
                return false;
            }
        }

        private bool IsForegroundCurrentGame()
        {
            try
            {
                var foregroundWindow = GetForegroundWindow();

                if (foregroundWindow == IntPtr.Zero)
                {
                    return false;
                }

                uint foregroundPid;
                GetWindowThreadProcessId(foregroundWindow, out foregroundPid);

                if (foregroundPid <= 0)
                {
                    return false;
                }

                if (currentGameProcessId.HasValue &&
                    foregroundPid == (uint)currentGameProcessId.Value)
                {
                    return true;
                }

                if (lastForegroundWindowProcessId.HasValue &&
                    foregroundPid == (uint)lastForegroundWindowProcessId.Value)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsPlayniteCurrentlyForeground()
        {
            try
            {
                var foregroundWindow = GetForegroundWindow();
                var playniteWindow = Application.Current?.MainWindow;

                if (foregroundWindow == IntPtr.Zero || playniteWindow == null)
                {
                    return false;
                }

                var playniteHandle = new System.Windows.Interop.WindowInteropHelper(playniteWindow).Handle;

                return playniteHandle != IntPtr.Zero && foregroundWindow == playniteHandle;
            }
            catch
            {
                return false;
            }
        }

        private void OverlayDebugLog(string message)
        {
            try
            {
                if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug("[AnikiHelper]" + message);
                }
            }
            catch
            {
            }
        }


        private void HideOverlayImmediate(bool resumeSuspendedGame = true)
        {
            Interlocked.Increment(ref overlayForegroundRecoveryGeneration);
            Interlocked.Increment(ref controllerOverlayOpenGeneration);
            Interlocked.Increment(ref controllerVirtualKeyboardOpenGeneration);
            overlayOpenOrOpening = false;

            if (resumeSuspendedGame)
            {
                ResumeSuspendedGameProcess();
            }
            else
            {
                OverlayDebugLog("[Overlay][Suspend] Closing overlay without resuming suspended game.");
            }

            try
            {
                if (overlayWindow != null)
                {
                    closeOverlayShouldResumeSuspendedGame = resumeSuspendedGame;
                    overlayWindow.Close();
                    overlayWindow = null;
                }
                else
                {
                    closeOverlayShouldResumeSuspendedGame = true;
                }
            }
            catch
            {
                closeOverlayShouldResumeSuspendedGame = true;
                overlayWindow = null;
            }
        }

        private bool TryGetRunningProcess(int? processId, out Process process)
        {
            process = null;

            if (!processId.HasValue || processId.Value <= 0)
            {
                return false;
            }

            try
            {
                process = Process.GetProcessById(processId.Value);

                if (process == null || process.HasExited)
                {
                    process = null;
                    return false;
                }

                process.Refresh();
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                process = null;
                return false;
            }
            catch
            {
                process = null;
                return false;
            }
        }

        private Process TryFindCurrentGameProcess()
        {
            try
            {
                var installDirectory = currentGame?.InstallDirectory;

                if (string.IsNullOrWhiteSpace(installDirectory))
                {
                    return null;
                }

                var fullInstallDirectory = Path.GetFullPath(installDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                var currentProcessId = Process.GetCurrentProcess().Id;
                var candidates = new List<Process>();

                foreach (var process in Process.GetProcesses())
                {
                    if (process == null || process.Id == currentProcessId)
                    {
                        continue;
                    }

                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }

                        var executablePath = process.MainModule?.FileName;

                        if (string.IsNullOrWhiteSpace(executablePath))
                        {
                            continue;
                        }

                        var fullExecutablePath = Path.GetFullPath(executablePath);

                        if (!fullExecutablePath.StartsWith(fullInstallDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        candidates.Add(process);
                    }
                    catch
                    {
                        process.Dispose();
                    }
                }

                var resolved = candidates
                    .OrderByDescending(x =>
                    {
                        try
                        {
                            return x.MainWindowHandle != IntPtr.Zero;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ThenByDescending(x =>
                    {
                        try
                        {
                            return x.StartTime;
                        }
                        catch
                        {
                            return DateTime.MinValue;
                        }
                    })
                    .FirstOrDefault();

                foreach (var candidate in candidates)
                {
                    if (!ReferenceEquals(candidate, resolved))
                    {
                        candidate.Dispose();
                    }
                }

                if (resolved != null)
                {
                    currentGameProcessId = resolved.Id;
                    OverlayDebugLog($"[Overlay][Process] Resolved active game process. PID={resolved.Id}, Name={resolved.ProcessName}");
                }

                return resolved;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to resolve the active game process.");
                return null;
            }
        }

        private bool TryFocusProcessWindow(int? processId, string sourceName)
        {
            Process process;

            if (!TryGetRunningProcess(processId, out process))
            {
                return false;
            }

            try
            {
                process.Refresh();

                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    return false;
                }

                BeginReturnToGameFocus(
                    process.MainWindowHandle,
                    process.Id,
                    sourceName);

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                process.Dispose();
            }
        }

        private void BeginReturnToGameFocus(
            IntPtr initialWindowHandle,
            int processId,
            string sourceName)
        {
            _ = FocusGameWindowWithRetriesAsync(
                initialWindowHandle,
                processId,
                sourceName);
        }

        private async Task<bool> FocusGameWindowWithRetriesAsync(
            IntPtr initialWindowHandle,
            int processId,
            string sourceName,
            bool updateCurrentGameProcessId = true)
        {
            var targetWindow = initialWindowHandle;
            var playniteWindow = IntPtr.Zero;

            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    playniteWindow = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                }
            }
            catch
            {
                playniteWindow = IntPtr.Zero;
            }

            OverlayDebugLog(
                $"[Overlay][ReturnToGame] Focus scheduled. " +
                $"Source={sourceName}, PID={processId}, Window={targetWindow}"
            );

            try
            {
                // The game has just been resumed after being suspended by the overlay.
                // Give its UI thread a brief moment to process window activation messages.
                await Task.Delay(ReturnToGameInitialFocusDelayMs);

                if (playniteWindow != IntPtr.Zero && playniteWindow != targetWindow)
                {
                    ShowWindowAsync(playniteWindow, SW_MINIMIZE);
                }

                for (var attempt = 1; attempt <= ReturnToGameFocusMaxAttempts; attempt++)
                {
                    if (!IsWindow(targetWindow))
                    {
                        Process process;

                        if (!TryGetRunningProcess(processId, out process))
                        {
                            OverlayDebugLog(
                                $"[Overlay][ReturnToGame] Target process is no longer running. PID={processId}"
                            );
                            return false;
                        }

                        try
                        {
                            process.Refresh();
                            targetWindow = process.MainWindowHandle;
                        }
                        finally
                        {
                            process.Dispose();
                        }

                        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow))
                        {
                            OverlayDebugLog(
                                $"[Overlay][ReturnToGame] No valid target window on attempt {attempt}. PID={processId}"
                            );

                            await Task.Delay(ReturnToGameFocusRetryDelayMs);
                            continue;
                        }
                    }

                    ShowWindowAsync(targetWindow, SW_RESTORE);
                    BringWindowToTop(targetWindow);
                    ForceFocusWindow(targetWindow);

                    await Task.Delay(ReturnToGameFocusVerificationDelayMs);

                    var foregroundWindow = GetForegroundWindow();
                    uint foregroundPid;
                    GetWindowThreadProcessId(foregroundWindow, out foregroundPid);

                    if (foregroundWindow == targetWindow || foregroundPid == processId)
                    {
                        lastForegroundWindow = targetWindow;
                        lastForegroundWindowProcessId = processId;

                        if (updateCurrentGameProcessId)
                        {
                            currentGameProcessId = processId;
                        }

                        OverlayDebugLog(
                            $"[Overlay][ReturnToGame] Focus confirmed. " +
                            $"Source={sourceName}, PID={processId}, Attempt={attempt}"
                        );
                        return true;
                    }

                    OverlayDebugLog(
                        $"[Overlay][ReturnToGame] Focus attempt failed. " +
                        $"Source={sourceName}, PID={processId}, Attempt={attempt}, " +
                        $"ForegroundPID={foregroundPid}, ForegroundWindow={foregroundWindow}"
                    );

                    await Task.Delay(ReturnToGameFocusRetryDelayMs);
                }

                logger?.Warn(
                    $"[AnikiHelper] Failed to return focus to the game after " +
                    $"{ReturnToGameFocusMaxAttempts} attempts. PID={processId}, Source={sourceName}."
                );

                return false;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Delayed ReturnToGame focus failed.");
                return false;
            }
        }

        private async Task<bool> FocusVirtualKeyboardTargetWithRetriesAsync(
            IntPtr initialWindowHandle,
            int processId,
            string sourceName)
        {
            var targetWindow = initialWindowHandle;

            OverlayDebugLog(
                $"[Overlay][VirtualKeyboard] Focus scheduled. " +
                $"Source={sourceName}, PID={processId}, Window={targetWindow}");

            try
            {
                // The overlay has just been hidden. A short delay prevents Playnite's
                // closing activation messages from immediately taking focus back.
                await Task.Delay(VirtualKeyboardTargetInitialFocusDelayMs);

                for (var attempt = 1; attempt <= VirtualKeyboardTargetFocusMaxAttempts; attempt++)
                {
                    if (!IsWindow(targetWindow))
                    {
                        Process process;

                        if (!TryGetRunningProcess(processId, out process))
                        {
                            return false;
                        }

                        try
                        {
                            process.Refresh();
                            targetWindow = process.MainWindowHandle;
                        }
                        finally
                        {
                            process.Dispose();
                        }

                        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow))
                        {
                            await Task.Delay(VirtualKeyboardTargetFocusRetryDelayMs);
                            continue;
                        }
                    }

                    // SW_RESTORE changes an already maximized window back to its normal
                    // windowed size. Only restore a target that is actually minimized.
                    if (IsIconic(targetWindow))
                    {
                        ShowWindowAsync(targetWindow, SW_RESTORE);
                        await Task.Delay(VirtualKeyboardTargetRestoreDelayMs);
                    }

                    BringWindowToTop(targetWindow);
                    ForceFocusWindow(targetWindow);

                    await Task.Delay(VirtualKeyboardTargetFocusVerificationDelayMs);

                    var foregroundWindow = GetForegroundWindow();
                    if (foregroundWindow == targetWindow)
                    {
                        OverlayDebugLog(
                            $"[Overlay][VirtualKeyboard] Focus confirmed. " +
                            $"Source={sourceName}, PID={processId}, Attempt={attempt}");
                        return true;
                    }

                    uint foregroundPid;
                    GetWindowThreadProcessId(foregroundWindow, out foregroundPid);

                    OverlayDebugLog(
                        $"[Overlay][VirtualKeyboard] Focus attempt failed. " +
                        $"Source={sourceName}, PID={processId}, Attempt={attempt}, " +
                        $"ForegroundPID={foregroundPid}, ForegroundWindow={foregroundWindow}");

                    await Task.Delay(VirtualKeyboardTargetFocusRetryDelayMs);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to restore the virtual keyboard target window.");
            }

            return false;
        }

        public void ReturnToGame()
        {
            OverlayDebugLog("[Overlay][ReturnToGame] START");

            HideOverlayImmediate();

            try
            {
                if (lastForegroundWindow != IntPtr.Zero)
                {
                    try
                    {
                        uint windowPid;
                        GetWindowThreadProcessId(lastForegroundWindow, out windowPid);

                        Process capturedProcess;
                        if (windowPid > 0 &&
                            TryGetRunningProcess((int)windowPid, out capturedProcess))
                        {
                            try
                            {
                                BeginReturnToGameFocus(
                                    lastForegroundWindow,
                                    (int)windowPid,
                                    "capturedForegroundWindow");

                                return;
                            }
                            finally
                            {
                                capturedProcess.Dispose();
                            }
                        }
                    }
                    catch
                    {
                    }

                    lastForegroundWindow = IntPtr.Zero;
                }

                if (TryFocusProcessWindow(lastForegroundWindowProcessId, "lastForegroundWindowProcessId"))
                {
                    return;
                }

                lastForegroundWindowProcessId = null;

                if (TryFocusProcessWindow(currentGameProcessId, "currentGameProcessId"))
                {
                    return;
                }

                using (var resolvedProcess = TryFindCurrentGameProcess())
                {
                    if (resolvedProcess != null &&
                        resolvedProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        BeginReturnToGameFocus(
                            resolvedProcess.MainWindowHandle,
                            resolvedProcess.Id,
                            "resolvedActiveGameProcess");

                        return;
                    }
                }

                OverlayDebugLog("[Overlay][ReturnToGame] No valid game window found.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] ReturnToGame failed.");
            }
        }

        public void CloseDirectVirtualKeyboard()
        {
            _ = CloseDirectVirtualKeyboardAsync();
        }

        private async Task CloseDirectVirtualKeyboardAsync()
        {
            IntPtr targetWindow;
            int targetProcessId;

            var hasTarget = TryResolveVirtualKeyboardTarget(out targetWindow, out targetProcessId);

            HideOverlayImmediate();

            if (!hasTarget)
            {
                return;
            }

            await FocusVirtualKeyboardTargetWithRetriesAsync(
                targetWindow,
                targetProcessId,
                "virtualKeyboardCancel");
        }

        internal IntPtr GetVirtualKeyboardLayout()
        {
            try
            {
                IntPtr targetWindow;
                int targetProcessId;

                if (TryResolveVirtualKeyboardTarget(out targetWindow, out targetProcessId))
                {
                    uint ignoredProcessId;
                    var targetThreadId = GetWindowThreadProcessId(targetWindow, out ignoredProcessId);

                    if (targetThreadId != 0)
                    {
                        var keyboardLayout = GetKeyboardLayout(targetThreadId);
                        if (keyboardLayout != IntPtr.Zero)
                        {
                            return keyboardLayout;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to resolve the target keyboard layout.");
            }

            return GetKeyboardLayout(0);
        }

        public void SendVirtualKeyboardText(string text, bool pressEnter)
        {
            _ = SendVirtualKeyboardTextAsync(text ?? string.Empty, pressEnter);
        }

        private async Task SendVirtualKeyboardTextAsync(string text, bool pressEnter)
        {
            IntPtr targetWindow;
            int targetProcessId;

            if (!TryResolveVirtualKeyboardTarget(out targetWindow, out targetProcessId))
            {
                HideOverlayImmediate();
                logger?.Warn("[AnikiHelper] Virtual keyboard could not resolve a valid target window.");
                return;
            }

            OverlayDebugLog(
                $"[Overlay][VirtualKeyboard] Sending text. PID={targetProcessId}, " +
                $"Window={targetWindow}, Length={text.Length}, PressEnter={pressEnter}");

            HideOverlayImmediate();

            var focused = await FocusVirtualKeyboardTargetWithRetriesAsync(
                targetWindow,
                targetProcessId,
                "virtualKeyboard");

            if (!focused)
            {
                logger?.Warn("[AnikiHelper] Virtual keyboard stopped because target focus could not be restored.");
                return;
            }

            await Task.Delay(VirtualKeyboardAfterFocusDelayMs);

            // Closing the WPF overlay can briefly give focus back to Playnite after the first
            // focus confirmation. Verify the exact target window once more immediately before
            // injecting keyboard input.
            var foregroundWindow = GetForegroundWindow();

            if (foregroundWindow != targetWindow)
            {
                focused = await FocusVirtualKeyboardTargetWithRetriesAsync(
                    targetWindow,
                    targetProcessId,
                    "virtualKeyboardBeforeInput");

                if (!focused)
                {
                    logger?.Warn("[AnikiHelper] Virtual keyboard stopped because the target lost focus before input injection.");
                    return;
                }
            }

            try
            {
                uint ignoredProcessId;
                var targetThreadId = GetWindowThreadProcessId(targetWindow, out ignoredProcessId);
                var keyboardLayout = GetKeyboardLayout(targetThreadId);

                foreach (var character in text)
                {
                    SendCharacterInput(character, keyboardLayout);
                    await Task.Delay(VirtualKeyboardCharacterDelayMs);
                }

                if (pressEnter)
                {
                    SendVirtualKeyInput(VK_RETURN, keyboardLayout);
                }

                OverlayDebugLog("[Overlay][VirtualKeyboard] Text injection completed.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Virtual keyboard text injection failed.");
            }
        }

        private bool TryResolveVirtualKeyboardTarget(out IntPtr targetWindow, out int targetProcessId)
        {
            targetWindow = IntPtr.Zero;
            targetProcessId = 0;

            try
            {
                if (virtualKeyboardTargetWindow != IntPtr.Zero && IsWindow(virtualKeyboardTargetWindow))
                {
                    uint windowPid;
                    GetWindowThreadProcessId(virtualKeyboardTargetWindow, out windowPid);

                    if (windowPid > 0)
                    {
                        targetWindow = virtualKeyboardTargetWindow;
                        targetProcessId = (int)windowPid;
                        return true;
                    }
                }

                if (lastForegroundWindow != IntPtr.Zero && IsWindow(lastForegroundWindow))
                {
                    uint windowPid;
                    GetWindowThreadProcessId(lastForegroundWindow, out windowPid);

                    if (windowPid > 0)
                    {
                        targetWindow = lastForegroundWindow;
                        targetProcessId = (int)windowPid;
                        return true;
                    }
                }

                var processIds = new[]
                {
                    virtualKeyboardTargetProcessId,
                    lastForegroundWindowProcessId,
                    currentGameProcessId
                };

                foreach (var processId in processIds)
                {
                    Process process;

                    if (!TryGetRunningProcess(processId, out process))
                    {
                        continue;
                    }

                    try
                    {
                        process.Refresh();

                        if (process.MainWindowHandle != IntPtr.Zero && IsWindow(process.MainWindowHandle))
                        {
                            targetWindow = process.MainWindowHandle;
                            targetProcessId = process.Id;
                            return true;
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                using (var process = TryFindCurrentGameProcess())
                {
                    if (process != null &&
                        process.MainWindowHandle != IntPtr.Zero &&
                        IsWindow(process.MainWindowHandle))
                    {
                        targetWindow = process.MainWindowHandle;
                        targetProcessId = process.Id;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to resolve virtual keyboard target window.");
            }

            return false;
        }

        private static void SendCharacterInput(char character, IntPtr keyboardLayout)
        {
            var keyScan = VkKeyScanEx(character, keyboardLayout);

            if (keyScan == -1)
            {
                // Unicode is kept as a fallback for characters that do not exist on the
                // active keyboard layout. Games using Raw Input may ignore this fallback,
                // but ordinary Windows text controls will still accept it.
                SendUnicodeCharacterInput(character);
                return;
            }

            var virtualKey = (ushort)(keyScan & 0xFF);
            var modifiers = (byte)((keyScan >> 8) & 0xFF);
            var inputs = new List<INPUT>();

            if ((modifiers & 1) != 0)
            {
                inputs.Add(CreateMappedVirtualKeyInput(VK_SHIFT, false, keyboardLayout));
            }

            if ((modifiers & 2) != 0)
            {
                inputs.Add(CreateMappedVirtualKeyInput(VK_CONTROL, false, keyboardLayout));
            }

            if ((modifiers & 4) != 0)
            {
                inputs.Add(CreateMappedVirtualKeyInput(VK_MENU, false, keyboardLayout));
            }

            inputs.Add(CreateMappedVirtualKeyInput(virtualKey, false, keyboardLayout));
            inputs.Add(CreateMappedVirtualKeyInput(virtualKey, true, keyboardLayout));

            if ((modifiers & 4) != 0)
            {
                inputs.Add(CreateMappedVirtualKeyInput(VK_MENU, true, keyboardLayout));
            }

            if ((modifiers & 2) != 0)
            {
                inputs.Add(CreateMappedVirtualKeyInput(VK_CONTROL, true, keyboardLayout));
            }

            if ((modifiers & 1) != 0)
            {
                inputs.Add(CreateMappedVirtualKeyInput(VK_SHIFT, true, keyboardLayout));
            }

            SendKeyboardInputs(inputs.ToArray());
        }

        private static void SendUnicodeCharacterInput(char character)
        {
            var inputs = new[]
            {
                CreateUnicodeInput(character, false),
                CreateUnicodeInput(character, true)
            };

            SendKeyboardInputs(inputs);
        }

        private static void SendVirtualKeyInput(ushort virtualKey, IntPtr keyboardLayout)
        {
            var inputs = new[]
            {
                CreateMappedVirtualKeyInput(virtualKey, false, keyboardLayout),
                CreateMappedVirtualKeyInput(virtualKey, true, keyboardLayout)
            };

            SendKeyboardInputs(inputs);
        }

        private static INPUT CreateMappedVirtualKeyInput(
            ushort virtualKey,
            bool keyUp,
            IntPtr keyboardLayout)
        {
            var mappedScanCode = MapVirtualKeyEx(
                virtualKey,
                MAPVK_VK_TO_VSC_EX,
                keyboardLayout);

            if (mappedScanCode == 0)
            {
                return CreateVirtualKeyInput(virtualKey, keyUp);
            }

            var flags = KEYEVENTF_SCANCODE;
            var prefix = mappedScanCode & 0xFF00;

            if (prefix == 0xE000 || prefix == 0xE100)
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            if (keyUp)
            {
                flags |= KEYEVENTF_KEYUP;
            }

            return new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = 0,
                        ScanCode = (ushort)(mappedScanCode & 0xFF),
                        Flags = flags,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            };
        }

        private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
        {
            return new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = virtualKey,
                        ScanCode = 0,
                        Flags = keyUp ? KEYEVENTF_KEYUP : 0,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            };
        }

        private static INPUT CreateUnicodeInput(char character, bool keyUp)
        {
            return new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = 0,
                        ScanCode = character,
                        Flags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            };
        }

        private static void SendKeyboardInputs(INPUT[] inputs)
        {
            if (inputs == null || inputs.Length == 0)
            {
                return;
            }

            var sent = SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf(typeof(INPUT)));

            if (sent != inputs.Length)
            {
                throw new InvalidOperationException(
                    $"SendInput sent {sent} of {inputs.Length} keyboard events. Win32={Marshal.GetLastWin32Error()}");
            }
        }

        public void ReturnToPlaynite()
        {
            OverlayDebugLog("[Overlay][ReturnToPlaynite] START");

            var keepGameSuspended = IsGameProcessSuspended();

            HideOverlayImmediate(resumeSuspendedGame: false);

            try
            {
                if (lastForegroundWindow != IntPtr.Zero)
                {
                    if (keepGameSuspended)
                    {
                        OverlayDebugLog("[Overlay][ReturnToPlaynite] Game is suspended. Skipping game-window minimize to avoid blocking on a frozen game UI thread.");
                    }
                    else
                    {
                        OverlayDebugLog("[Overlay][ReturnToPlaynite] Minimizing captured game window before restoring Playnite.");
                        ShowWindowAsync(lastForegroundWindow, SW_MINIMIZE);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to minimize captured game window before returning to Playnite.");
            }

            OverlayDebugLog("[Overlay][ReturnToPlaynite] After HideOverlayImmediate");

            try
            {
                var window = Application.Current?.MainWindow;

                OverlayDebugLog($"[Overlay][ReturnToPlaynite] MainWindow null = {window == null}");

                if (window != null)
                {
                    OverlayDebugLog(
                        $"[Overlay][ReturnToPlaynite] Before restore | " +
                        $"IsVisible={window.IsVisible}, IsActive={window.IsActive}, " +
                        $"WindowState={window.WindowState}, Topmost={window.Topmost}, " +
                        $"IsFocused={window.IsFocused}, IsKeyboardFocusWithin={window.IsKeyboardFocusWithin}, " +
                        $"SafeFocus={keepGameSuspended}"
                    );

                    RestoreAndFocusPlayniteWindow(window, keepGameSuspended);

                    window.Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await TryFocusGameStatusButtonAsync(window);
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    OverlayDebugLog("[Overlay][ReturnToPlaynite] After RestoreAndFocusPlayniteWindow");
                }

                OverlayDebugLog("[Overlay][ReturnToPlaynite] END");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] ReturnToPlaynite failed.");
            }
        }

        public void OpenAppsWindow()
        {
            try
            {
                settings?.LoadOverlayApps();

                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowApps();
                    return;
                }

                var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var command = settings?.OpenChildWindow?["AppsWindowStyle|FocusFirst|NoDim"];
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to open AppsWindowStyle.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] OpenAppsWindow failed.");
            }
        }

        private void ExecuteFullscreenMainMenuCommand(string commandName)
        {
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow == null || mainWindow.DataContext == null)
                {
                    logger?.Warn($"[AnikiHelper][Overlay] Cannot execute {commandName}: MainWindow/DataContext not found.");
                    return;
                }

                var playniteAssemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playnite.dll");
                var playniteAssembly = Assembly.LoadFrom(playniteAssemblyPath);

                var windowBaseType = playniteAssembly.GetType("Playnite.Controls.WindowBase");
                if (windowBaseType == null)
                {
                    logger?.Warn($"[AnikiHelper][Overlay] Cannot execute {commandName}: WindowBase type not found.");
                    return;
                }

                var windowBase = Activator.CreateInstance(windowBaseType);
                var fullscreenAssembly = Application.Current.GetType().Assembly;

                var factoryType = fullscreenAssembly.GetType("Playnite.FullscreenApp.Windows.MainMenuWindowFactory");
                var modelType = fullscreenAssembly.GetType("Playnite.FullscreenApp.ViewModels.MainMenuViewModel");

                if (factoryType == null || modelType == null)
                {
                    logger?.Warn($"[AnikiHelper][Overlay] Cannot execute {commandName}: Fullscreen menu types not found.");
                    return;
                }

                var factory = Activator.CreateInstance(factoryType);
                var baseType = factoryType.BaseType;

                var windowProperty = baseType?.GetProperty("Window", BindingFlags.Public | BindingFlags.Instance);
                windowProperty?.GetSetMethod(true)?.Invoke(factory, new[] { windowBase });

                var initFinishedEventProperty = baseType?.GetProperty("initFinishedEvent", BindingFlags.NonPublic | BindingFlags.Instance);
                var initFinishedEvent = initFinishedEventProperty?.GetValue(factory) as AutoResetEvent;
                initFinishedEvent?.Set();

                var model = Activator.CreateInstance(modelType, new[] { factory, mainWindow.DataContext });
                var commandProperty = modelType.GetProperty(commandName);

                if (commandProperty == null)
                {
                    logger?.Warn($"[AnikiHelper][Overlay] MainMenu command not found: {commandName}");
                    return;
                }

                if (!(commandProperty.GetValue(model) is ICommand command))
                {
                    logger?.Warn($"[AnikiHelper][Overlay] MainMenu property is not an ICommand: {commandName}");
                    return;
                }

                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][Overlay] Failed to execute Fullscreen MainMenu command: {commandName}");
            }
        }

        public void OpenMusicPlayerWindow()
        {
            try
            {
                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowMusicPlayer();
                    return;
                }

                var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var command = settings?.OpenChildWindow?["MusicPlayerWindowStyle|FocusFirst|NoDim"];
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to open MusicPlayerWindowStyle.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] OpenMusicPlayerWindow failed.");
            }
        }


        public void OpenAudioSwitcherWindow()
        {
            try
            {
                if (!IsAudioSwitcherInstalled)
                {
                    logger?.Warn("[AnikiHelper][Overlay] AudioSwitcher button requested, but PlayniteAudioSwitcher is not installed.");
                    return;
                }

                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowAudioSwitcher();
                    return;
                }

                var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var command = settings?.OpenChildWindow?["AudioSwitcherWindowStyle|FocusFirst|NoDim"];
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to open AudioSwitcherWindowStyle.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] OpenAudioSwitcherWindow failed.");
            }
        }

        public void OpenUniPlaySongWindow()
        {
            try
            {
                if (!IsUniPlaySongInstalled)
                {
                    logger?.Warn("[AnikiHelper][Overlay] UniPlaySong button requested, but UniPlaySong is not installed.");
                    return;
                }

                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowUniPlaySong();
                    return;
                }

                var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var command = settings?.OpenChildWindow?["UniPlaySongWindowStyle|FocusFirst|NoDim"];
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to open UniPlaySongWindowStyle.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] OpenUniPlaySongWindow failed.");
            }
        }

        public void OpenFriendsWindow()
        {
            try
            {
                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowFriends();
                    return;
                }

                var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var command = settings?.OpenChildWindow?["FriendsWindowStyle|FocusFirst|NoDim"];
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to open FriendsWindowStyle.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] OpenFriendsWindow failed.");
            }
        }


        private object GetPlayniteAchievementsPluginInstance()
        {
            try
            {
                var pluginType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(assembly => assembly.GetType("PlayniteAchievements.PlayniteAchievementsPlugin", false))
                    .FirstOrDefault(type => type != null);

                return pluginType?
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
                    .GetValue(null);
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to locate PlayniteAchievements instance.");
                return null;
            }
        }

        private object GetPlayniteAchievementsSettings(object pluginInstance)
        {
            try
            {
                return pluginInstance?
                    .GetType()
                    .GetProperty("Settings", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(pluginInstance);
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to access PlayniteAchievements settings.");
                return null;
            }
        }

        private bool ExecutePlayniteAchievementsCommand(object pluginSettings, string propertyName, object parameter = null)
        {
            try
            {
                var command = pluginSettings?
                    .GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(pluginSettings) as ICommand;

                if (command == null || !command.CanExecute(parameter))
                {
                    return false;
                }

                command.Execute(parameter);
                return true;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][Overlay] Failed to execute PlayniteAchievements command " + propertyName + ".");
                return false;
            }
        }

        public bool ApplyOverlayAchievementSortToPlayniteAchievements()
        {
            try
            {
                var pluginInstance = GetPlayniteAchievementsPluginInstance();
                var pluginSettings = GetPlayniteAchievementsSettings(pluginInstance);
                if (pluginSettings == null)
                {
                    return false;
                }

                var lockedFirst = string.Equals(
                    settings?.OverlayAchievementsSortMode,
                    "LockedFirst",
                    StringComparison.OrdinalIgnoreCase);

                var sortKey = lockedFirst ? "Status" : "UnlockTime";
                var direction = lockedFirst ? "Ascending" : "Descending";

                var sortApplied = ExecutePlayniteAchievementsCommand(
                    pluginSettings,
                    "SortDynamicAchievementsCommand",
                    sortKey);

                var directionApplied = ExecutePlayniteAchievementsCommand(
                    pluginSettings,
                    "SetDynamicAchievementsSortDirectionCommand",
                    direction);

                return sortApplied || directionApplied;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to apply PlayniteAchievements overlay sorting.");
                return false;
            }
        }

        private bool PreparePlayniteAchievementsForRunningGame()
        {
            try
            {
                if (currentGame == null || currentGame.Id == Guid.Empty)
                {
                    return false;
                }

                var pluginInstance = GetPlayniteAchievementsPluginInstance();
                var pluginSettings = GetPlayniteAchievementsSettings(pluginInstance);
                if (pluginSettings == null)
                {
                    return false;
                }

                // JDD's dedicated command resolves the currently running Playnite game and
                // republishes all DynamicAchievements theme properties for that game.
                var gameApplied = ExecutePlayniteAchievementsCommand(
                    pluginSettings,
                    "FilterDynamicAchievementsByRunningGameCommand");

                if (!gameApplied)
                {
                    return false;
                }

                // The old Aniki overlay always displayed every achievement. Do not inherit a
                // filter that may have been selected previously in the normal achievement view.
                ExecutePlayniteAchievementsCommand(
                    pluginSettings,
                    "SetDynamicAchievementsFilterCommand",
                    "All");

                ApplyOverlayAchievementSortToPlayniteAchievements();
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to prepare PlayniteAchievements for the running game.");
                return false;
            }
        }

        public void OpenAchievementsWindow()
        {
            try
            {
                if (!IsPlayniteAchievementsInstalled)
                {
                    logger?.Warn("[AnikiHelper][Overlay] Achievements button requested, but PlayniteAchievements is not installed.");
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        PreparePlayniteAchievementsForRunningGame();

                        if (overlayWindow != null && overlayWindow.IsVisible)
                        {
                            overlayWindow.ShowAchievements();
                            return;
                        }

                        var command = settings?.OpenChildWindow?["AchievementsWindowStyle|FocusFirst|NoDim"];
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to open AchievementsWindowStyle.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] OpenAchievementsWindow failed.");
            }
        }

        private void LoadOverlayLastCaptures()
        {
            try
            {
                var gameId = currentGame != null ? currentGame.Id : Guid.Empty;
                var gameName = currentGame != null ? currentGame.Name : string.Empty;
                settings?.LoadOverlayLastCaptures(gameId, gameName);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to prepare Last Captures window.");
            }
        }

        public bool ShowCapturePreview(AnikiMediaItem mediaItem)
        {
            try
            {
                if (mediaItem == null || overlayWindow == null || !overlayWindow.IsVisible)
                {
                    return false;
                }

                var dispatcher = overlayWindow.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    return dispatcher.Invoke(new Func<bool>(() => overlayWindow.ShowCapturePreview(mediaItem)));
                }

                return overlayWindow.ShowCapturePreview(mediaItem);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to show capture preview.");
                return false;
            }
        }

        public List<AnikiMediaItem> GetOverlayLastCapturePreviewItems()
        {
            try
            {
                if (settings?.OverlayLastCaptureItems == null)
                {
                    return new List<AnikiMediaItem>();
                }

                return settings.OverlayLastCaptureItems
                    .Where(item => item != null)
                    .Where(item => !string.IsNullOrWhiteSpace(GetCapturePreviewImagePath(item)))
                    .ToList();
            }
            catch
            {
                return new List<AnikiMediaItem>();
            }
        }

        public string GetCapturePreviewImagePath(AnikiMediaItem mediaItem)
        {
            if (mediaItem == null)
            {
                return string.Empty;
            }

            try
            {
                if (!mediaItem.IsVideo &&
                    !string.IsNullOrWhiteSpace(mediaItem.FilePath) &&
                    File.Exists(mediaItem.FilePath) &&
                    IsSupportedCapturePreviewImage(mediaItem.FilePath))
                {
                    return mediaItem.FilePath;
                }

                var thumbnailPath = mediaItem.DisplayThumbnailPath;
                if (!string.IsNullOrWhiteSpace(thumbnailPath) &&
                    File.Exists(thumbnailPath) &&
                    IsSupportedCapturePreviewImage(thumbnailPath))
                {
                    return thumbnailPath;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool IsSupportedCapturePreviewImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension == ".jpg" ||
                   extension == ".jpeg" ||
                   extension == ".png" ||
                   extension == ".bmp" ||
                   extension == ".gif" ||
                   extension == ".tif" ||
                   extension == ".tiff";
        }

        public void OpenLastCapturesWindow()
        {
            try
            {
                LoadOverlayLastCaptures();

                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowLastCaptures();
                    return;
                }

                var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var command = settings?.OpenChildWindow?["LastCapturesWindowStyle|FocusFirst|NoDim"];
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to open LastCapturesWindowStyle.");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] OpenLastCapturesWindow failed.");
            }
        }

        public void RequestQuitGame()
        {
            if (currentGame == null)
            {
                return;
            }

            try
            {
                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowQuitConfirmation();
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to show in-game overlay quit confirmation.");
            }
        }

        public void ConfirmQuitGame()
        {
            if (currentGame == null)
            {
                return;
            }

            try
            {
                if (overlayWindow != null)
                {
                    overlayWindow.ResetQuitConfirmationState();
                }
            }
            catch
            {
            }

            settings.GameClosing = true;
            settings.ClosingGameName = currentGame?.Name ?? string.Empty;
            var capturedWindow = lastForegroundWindow;
            var capturedWindowPid = lastForegroundWindowProcessId;
            var startedPid = currentGameProcessId;

            Process startedProcess;
            if (!TryGetRunningProcess(startedPid, out startedProcess))
            {
                using (var resolvedProcess = TryFindCurrentGameProcess())
                {
                    if (resolvedProcess != null)
                    {
                        startedPid = resolvedProcess.Id;
                        currentGameProcessId = resolvedProcess.Id;
                    }
                }
            }
            else
            {
                startedProcess.Dispose();
            }

            if (startedPid.HasValue &&
                 capturedWindowPid.HasValue &&
                 startedPid.Value != capturedWindowPid.Value)
            {
                logger.Warn(
                    $"[AnikiHelper] Overlay quit PID mismatch. " +
                    $"Foreground PID={capturedWindowPid.Value}, " +
                    $"Started PID={startedPid.Value}. Trying foreground window anyway."
                );
            }

            HideOverlayImmediate();

            Task.Run(async () =>
            {
                try
                {
                    // Give the controller A release a short moment to finish before closing the game.
                    await Task.Delay(350).ConfigureAwait(false);

                    if (await TryCloseWindowProcessAsync(capturedWindow, capturedWindowPid).ConfigureAwait(false))
                    {
                        return;
                    }

                    if (await TryCloseStartedProcessAsync(startedPid).ConfigureAwait(false))
                    {
                        return;
                    }

                    logger.Warn("[AnikiHelper] In-game overlay could not close the current game. No valid game window/process was found.");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "[AnikiHelper] Failed to close game from in-game overlay.");
                }
            });
        }

        private async Task<bool> TryCloseWindowProcessAsync(IntPtr windowHandle, int? knownPid)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            var currentProcessId = Process.GetCurrentProcess().Id;

            try
            {
                uint pidFromWindow;
                GetWindowThreadProcessId(windowHandle, out pidFromWindow);

                var pid = knownPid.GetValueOrDefault();

                Process knownProcess;
                if (!TryGetRunningProcess(pid, out knownProcess))
                {
                    pid = pidFromWindow > 0 ? (int)pidFromWindow : 0;
                }
                else
                {
                    knownProcess.Dispose();

                    if (pidFromWindow > 0 && pid != (int)pidFromWindow)
                    {
                        pid = (int)pidFromWindow;
                    }
                }

                if (pid <= 0 || pid == currentProcessId)
                {
                    logger.Warn("[AnikiHelper] Refusing to close invalid or Playnite window process from overlay.");
                    return false;
                }

                // First try to ask the actual game window to close.
                try
                {
                    PostMessage(windowHandle, WM_SYSCOMMAND, new IntPtr(SC_CLOSE), IntPtr.Zero);
                    PostMessage(windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                catch
                {
                }

                if (await WaitForProcessExitAsync(pid, 1200).ConfigureAwait(false))
                {
                    return true;
                }

                return await TryCloseProcessIdAsync(pid, true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to close game using captured foreground window.");
                return false;
            }
        }

        private async Task<bool> TryCloseStartedProcessAsync(int? processId)
        {
            if (!processId.HasValue || processId.Value <= 0)
            {
                return false;
            }

            return await TryCloseProcessIdAsync(processId.Value, false).ConfigureAwait(false);
        }

        private async Task<bool> TryCloseProcessIdAsync(int processId, bool allowKill)
        {
            var currentProcessId = Process.GetCurrentProcess().Id;

            if (processId <= 0 || processId == currentProcessId)
            {
                return false;
            }

            try
            {
                var process = Process.GetProcessById(processId);

                if (process == null || process.HasExited)
                {
                    return true;
                }

                try
                {
                    process.Refresh();
                }
                catch
                {
                }

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    try
                    {
                        process.CloseMainWindow();
                    }
                    catch
                    {
                    }

                    if (await WaitForProcessExitAsync(processId, 1500).ConfigureAwait(false))
                    {
                        return true;
                    }

                    try
                    {
                        PostMessage(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch
                    {
                    }

                    if (await WaitForProcessExitAsync(processId, 1200).ConfigureAwait(false))
                    {
                        return true;
                    }
                }

                if (allowKill)
                {
                    try
                    {
                        process.Kill();
                        logger.Warn("[AnikiHelper] Game process was force killed from overlay: " + processId);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to force kill game process: " + processId);
                    }
                }

                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to close process id from overlay: " + processId);
                return false;
            }
        }

        private async Task<bool> WaitForProcessExitAsync(int processId, int timeoutMs)
        {
            var start = DateTime.UtcNow;

            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                try
                {
                    var process = Process.GetProcessById(processId);

                    if (process == null || process.HasExited)
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    return true;
                }
                catch
                {
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            return false;
        }

        private string TryFindExtraMetadataLogo(Guid gameId)
        {
            try
            {
                var gameFolder = GetExtraMetadataGameFolder(gameId);

                if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
                {
                    return null;
                }

                var candidates = new[]
                {
                    Path.Combine(gameFolder, "logo.png"),
                    Path.Combine(gameFolder, "logo.jpg"),
                    Path.Combine(gameFolder, "logo.jpeg"),
                    Path.Combine(gameFolder, "logo.webp")
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GetExtraMetadataGameFolder(Guid gameId)
        {
            var gameIdText = gameId.ToString();

            try
            {
                var configRoot = playniteApi?.Paths?.ConfigurationPath;
                if (!string.IsNullOrWhiteSpace(configRoot))
                {
                    var path = Path.Combine(configRoot, "ExtraMetadata", "games", gameIdText);

                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }

                var appRoot = playniteApi?.Paths?.ApplicationPath;
                if (!string.IsNullOrWhiteSpace(appRoot))
                {
                    var path = Path.Combine(appRoot, "ExtraMetadata", "games", gameIdText);

                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appDataPath, "Playnite", "ExtraMetadata", "games", gameIdText);
            }
            catch
            {
                return null;
            }
        }

        private AnikiMediaGameItem GetCurrentGameMediaSummary()
        {
            try
            {
                if (currentGame == null || settings == null)
                {
                    return null;
                }

                var games = settings.MediaGalleryGames;
                if (games != null)
                {
                    var cached = games.FirstOrDefault(x => x != null && x.GameId == currentGame.Id);
                    if (cached != null)
                    {
                        return cached;
                    }
                }

                var currentItems = settings.CurrentGameMediaItems;
                if (currentItems != null)
                {
                    var items = currentItems
                        .Where(x => x != null && x.GameId == currentGame.Id)
                        .ToList();

                    if (items.Count > 0)
                    {
                        return new AnikiMediaGameItem
                        {
                            GameId = currentGame.Id,
                            GameName = currentGame.Name,
                            MediaCount = items.Count,
                            ImageCount = items.Count(x => !x.IsVideo),
                            VideoCount = items.Count(x => x.IsVideo),
                            LatestCaptureDate = items.Max(x => x.CaptureDate),
                            OldestCaptureDate = items.Min(x => x.CaptureDate),
                            SourceProvider = items.FirstOrDefault()?.SourceProvider ?? string.Empty
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] Failed to read overlay media summary.");
            }

            return null;
        }

        private AchievementOverlaySummary GetCachedCurrentGameAchievementSummary()
        {
            try
            {
                if (currentGame == null)
                {
                    return null;
                }

                lock (achievementSummaryLock)
                {
                    if (cachedAchievementGameId == currentGame.Id && cachedAchievementSummary != null)
                    {
                        return cachedAchievementSummary;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private AchievementOverlaySummary GetCurrentGameAchievementSummary()
        {
            try
            {
                if (currentGame == null)
                {
                    return null;
                }

                var gameId = currentGame.Id;
                var gameAtCall = currentGame;
                var now = DateTime.UtcNow;

                lock (achievementSummaryLock)
                {
                    if (cachedAchievementGameId == gameId &&
                        cachedAchievementSummary != null &&
                        (now - cachedAchievementCheckedUtc) < TimeSpan.FromMinutes(2))
                    {
                        return cachedAchievementSummary;
                    }

                    cachedAchievementGameId = gameId;
                    cachedAchievementCheckedUtc = now;
                }

                OverlayDebugLog("[OverlayCache] Cache MISS");

                var summary =
                    LoadPlayniteAchievementsSummary(gameAtCall)
                    ?? new AchievementOverlaySummary();

                lock (achievementSummaryLock)
                {
                    if (currentGame != null && currentGame.Id == gameId)
                    {
                        cachedAchievementGameId = gameId;
                        cachedAchievementCheckedUtc = DateTime.UtcNow;
                        cachedAchievementSummary = summary;
                    }

                    return summary;
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] Failed to read overlay achievement summary.");
                return null;
            }
        }

        private AchievementOverlaySummary LoadPlayniteAchievementsSummary(Game game)
        {
            try
            {
                if (playniteAchievementsReader == null)
                {
                    playniteAchievementsReader = new PlayniteAchievementsReader(playniteApi, logger);
                }

                var summary = playniteAchievementsReader.LoadSummary(game);

                if (summary == null || summary.Total <= 0)
                {
                    return null;
                }

                return new AchievementOverlaySummary
                {
                    Unlocked = summary.Unlocked,
                    Total = summary.Total,
                    LastUnlockedTitle = summary.LastUnlockedTitle ?? string.Empty,
                    LastUnlockedDescription = summary.LastUnlockedDescription ?? string.Empty,
                    LastUnlockedIconPath = summary.LastUnlockedIconPath ?? string.Empty,
                    LastUnlockedPercent = summary.LastUnlockedPercent,
                    LastUnlockedRarity = summary.LastUnlockedRarity ?? string.Empty,
                    LastUnlockedDate = summary.LastUnlockedUtc
                };
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] Failed to load PlayniteAchievements summary for overlay.");
                return null;
            }
        }








        private string FormatRelativeTime(DateTime date)
        {
            try
            {
                var local = date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date;
                var elapsed = DateTime.Now - local;

                if (elapsed.TotalMinutes < 1)
                {
                    return Loc("LOCInGameOverlayJustNow", "just now");
                }

                if (elapsed.TotalMinutes < 60)
                {
                    var minutes = Math.Max(1, (int)elapsed.TotalMinutes);
                    return minutes == 1 ? "1 min ago" : minutes + " min ago";
                }

                if (elapsed.TotalHours < 24)
                {
                    var hours = Math.Max(1, (int)elapsed.TotalHours);
                    return hours == 1 ? "1 hour ago" : hours + " hours ago";
                }

                if (elapsed.TotalDays < 7)
                {
                    var days = Math.Max(1, (int)elapsed.TotalDays);
                    return days == 1 ? "yesterday" : days + " days ago";
                }

                return local.ToString("dd/MM/yyyy HH:mm");
            }
            catch
            {
                return "-";
            }
        }

        private string GetSourceName(Game game)
        {
            try
            {
                if (game.SourceId == Guid.Empty)
                {
                    return string.Empty;
                }

                var source = playniteApi.Database.Sources.Get(game.SourceId);
                return source?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetPlatformName(Game game)
        {
            try
            {
                if (game.PlatformIds == null || game.PlatformIds.Count == 0)
                {
                    return string.Empty;
                }

                var platform = playniteApi.Database.Platforms.Get(game.PlatformIds.First());
                return platform?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string FormatMinutes(ulong minutes)
        {
            var hours = minutes / 60UL;
            var mins = minutes % 60UL;

            if (hours <= 0)
            {
                return mins + " min";
            }

            return hours + "h " + mins.ToString("00");
        }

        private string Loc(string key, string fallback)
        {
            try
            {
                var value = System.Windows.Application.Current.TryFindResource(key);

                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                if (value != null)
                {
                    var str = value.ToString();

                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        return str;
                    }
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
            {
                return null;
            }

            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Name == name)
                {
                    return element;
                }

                var result = FindVisualChildByName<T>(child, name);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private async Task TryFocusGameStatusButtonAsync(Window window)
        {
            if (window == null)
            {
                return;
            }

            for (int attempt = 1; attempt <= GameStatusFocusMaxAttempts; attempt++)
            {
                try
                {
                    await Task.Delay(GameStatusFocusRetryDelayMs);

                    var gameStatusButton = FindVisualChildByName<FrameworkElement>(window, "GameStatusButton");

                    if (gameStatusButton != null &&
                        gameStatusButton.IsVisible &&
                        gameStatusButton.IsEnabled &&
                        gameStatusButton.Focusable)
                    {
                        OverlayDebugLog($"[Overlay][ReturnToPlaynite] Focusing GameStatusButton. Attempt={attempt}");

                        gameStatusButton.Focus();
                        Keyboard.Focus(gameStatusButton);
                        return;
                    }

                    OverlayDebugLog($"[Overlay][ReturnToPlaynite] GameStatusButton not ready. Attempt={attempt}");
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, $"[AnikiHelper] Failed to focus GameStatusButton. Attempt={attempt}");
                }
            }

            OverlayDebugLog("[Overlay][ReturnToPlaynite] GameStatusButton focus failed after retries.");
        }

        private void RestoreAndFocusPlayniteWindow(Window window, bool safeFocusOnly = false)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;

                if (handle == IntPtr.Zero)
                {
                    window.Show();
                    window.Activate();
                    return;
                }

                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                if (IsIconic(handle))
                {
                    ShowWindowAsync(handle, SW_RESTORE);
                }
                else
                {
                    ShowWindowAsync(handle, SW_SHOW);
                }

                window.Show();

                if (safeFocusOnly)
                {
                    // When the game process is intentionally suspended, the foreground thread can belong
                    // to the frozen game. Do not use AttachThreadInput/BringWindowToTop against that path:
                    // it can make Playnite appear frozen while Windows waits on the suspended UI thread.
                    var wasTopmost = window.Topmost;
                    window.Topmost = true;
                    SetForegroundWindow(handle);
                    window.Activate();
                    window.Focus();
                    window.Topmost = wasTopmost;
                    return;
                }

                BringWindowToTop(handle);
                ForceFocusWindow(handle);

                window.Activate();
                window.Focus();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to restore and focus Playnite window.");
            }
        }

        private static void ForceFocusWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var foregroundWindow = GetForegroundWindow();

                uint foregroundPid;
                uint targetPid;

                var foregroundThread = GetWindowThreadProcessId(foregroundWindow, out foregroundPid);
                var targetThread = GetWindowThreadProcessId(windowHandle, out targetPid);

                if (foregroundThread != 0 && targetThread != 0 && foregroundThread != targetThread)
                {
                    AttachThreadInput(foregroundThread, targetThread, true);
                    SetForegroundWindow(windowHandle);
                    AttachThreadInput(foregroundThread, targetThread, false);
                }
                else
                {
                    SetForegroundWindow(windowHandle);
                }
            }
            catch
            {
                try
                {
                    SetForegroundWindow(windowHandle);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width
            {
                get { return Math.Max(0, Right - Left); }
            }

            public int Height
            {
                get { return Math.Max(0, Bottom - Top); }
            }
        }

        [ComImport]
        [Guid("4CE576FA-83DC-4F88-951C-9D0782B4E376")]
        private class UIHostNoLaunch
        {
        }

        [ComImport]
        [Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITipInvocation
        {
            void Toggle(IntPtr windowHandle);
        }

        [ComImport]
        [Guid("D5120AA3-46BA-44C5-822D-CA8092C1FC72")]
        private class FrameworkInputPane
        {
        }

        [ComImport]
        [System.Security.SuppressUnmanagedCodeSecurity]
        [Guid("5752238B-24F0-495A-82F1-2FD593056796")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFrameworkInputPane
        {
            [PreserveSig]
            int Advise(
                [MarshalAs(UnmanagedType.IUnknown)] object window,
                [MarshalAs(UnmanagedType.IUnknown)] object handler,
                out int cookie);

            [PreserveSig]
            int AdviseWithHWND(
                IntPtr windowHandle,
                [MarshalAs(UnmanagedType.IUnknown)] object handler,
                out int cookie);

            [PreserveSig]
            int Unadvise(int cookie);

            [PreserveSig]
            int Location(out NativeRectangle inputPaneScreenLocation);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public InputUnion Data;
        }

        // INPUT contains a native union. The union size is determined by its
        // largest member (MOUSEINPUT), even when we only send keyboard input.
        // Declaring only KEYBDINPUT makes Marshal.SizeOf(INPUT) equal 32 bytes
        // on x64 instead of the 40 bytes required by user32!SendInput, which
        // causes ERROR_INVALID_PARAMETER (Win32 error 87).
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;

            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;

            [FieldOffset(0)]
            public HARDWAREINPUT Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint Message;
            public ushort ParameterLow;
            public ushort ParameterHigh;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short VkKeyScanEx(char character, IntPtr keyboardLayout);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKeyEx(
            uint code,
            uint mapType,
            IntPtr keyboardLayout);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint MAPVK_VK_TO_VSC_EX = 4;
        private const ushort VK_SHIFT = 0x10;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_MENU = 0x12;
        private const ushort VK_RETURN = 0x0D;
        private const int WindowsVirtualKeyboardAfterFocusDelayMs = 120;
        private const int WindowsVirtualKeyboardTabTipBootstrapDelayMs = 180;
        private const int WindowsVirtualKeyboardVisibilityCheckDelayMs = 300;
        private const int VirtualKeyboardAfterFocusDelayMs = 100;
        private const int VirtualKeyboardCharacterDelayMs = 5;
        private const int VirtualKeyboardTargetInitialFocusDelayMs = 60;
        private const int VirtualKeyboardTargetRestoreDelayMs = 80;
        private const int VirtualKeyboardTargetFocusVerificationDelayMs = 70;
        private const int VirtualKeyboardTargetFocusRetryDelayMs = 90;
        private const int VirtualKeyboardTargetFocusMaxAttempts = 8;

        private const uint THREAD_SUSPEND_RESUME = 0x0002;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_ESCAPE = 0x1B;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_CLOSE = 0xF060;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int SW_MINIMIZE = 6;
        private const int ReturnToGameInitialFocusDelayMs = 180;
        private const int ReturnToGameFocusVerificationDelayMs = 120;
        private const int ReturnToGameFocusRetryDelayMs = 140;
        private const int ReturnToGameFocusMaxAttempts = 8;
        private const int GameStatusFocusRetryDelayMs = 250;
        private const int GameStatusFocusMaxAttempts = 6;



        private sealed class AchievementOverlaySummary
        {
            public int Unlocked { get; set; }
            public int Total { get; set; }
            public string LastUnlockedTitle { get; set; }
            public string LastUnlockedDescription { get; set; }
            public string LastUnlockedIconPath { get; set; }
            public double? LastUnlockedPercent { get; set; }
            public string LastUnlockedRarity { get; set; }
            public DateTime? LastUnlockedDate { get; set; }
        }

    }
}
