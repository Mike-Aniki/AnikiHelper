using AnikiHelper.Services.Achievements;
using AnikiHelper.Services.MediaGallery;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
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

        private InGameOverlayHotkeyService hotkeyService;
        private AnikiOverlayInputListener inputListener;
        private AnikiInGameOverlayWindow overlayWindow;
        private bool overlayOpenedFromPlaynite;

        private readonly object overlayToggleLock = new object();
        private bool overlayToggleQueued;
        private volatile bool overlayOpenOrOpening;
        private DateTime lastOverlayToggleUtc = DateTime.MinValue;
        private const int OverlayToggleCooldownMs = 350;

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
            Func<bool> hasOpenCustomWindow = null)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.hasOpenCustomWindow = hasOpenCustomWindow;
            logger = LogManager.GetLogger();
            playniteAchievementsReader = new PlayniteAchievementsReader(playniteApi, logger);
        }

        public bool IsGameRunning
        {
            get { return currentGame != null; }
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
                ToggleOverlay,
                () => settings == null || settings.InGameOverlayEnabled,
                () => overlayWindow != null && overlayWindow.IsVisible,
                HandleOverlayControllerInput);

            inputListener.Start();

        }

        public void Stop()
        {
            ResumeSuspendedGameProcess();

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

        private void OpenOverlayInternal(bool ignoreEnabledSetting, string source)
        {
            if (!ignoreEnabledSetting && settings != null && !settings.InGameOverlayEnabled)
            {
                return;
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
                        overlayWindow.FocusOverlayButton();
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
                    ShowOverlay();
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

            if (!IsOverlayWindowForeground(button))
            {
                if ((button == ControllerInput.B || button == ControllerInput.Back) &&
                    TrySendEscapeToForegroundPlayniteWindow())
                {
                    OverlayDebugLog($"[OverlayInput] Sent Escape to foreground Playnite window. Button={button}");
                    return;
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
                        ?? LoadSuccessStoryAchievementSummary(gameAtOpen)
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

        public void ShowOverlay()
        {
            overlayOpenOrOpening = true;
            overlayOpenedFromPlaynite = IsPlayniteCurrentlyForeground() || currentGame == null;

            var shouldSuspendGameAfterOverlayIsVisible = currentGame != null && !overlayOpenedFromPlaynite;
            var gameIdAtOpen = currentGame != null ? currentGame.Id : Guid.Empty;

            if (shouldSuspendGameAfterOverlayIsVisible)
            {
                CaptureCurrentForegroundGameWindow();
            }
            else if (currentGame == null)
            {
                lastForegroundWindow = IntPtr.Zero;
                lastForegroundWindowProcessId = null;
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

            if (shouldSuspendGameAfterOverlayIsVisible)
            {
                SuspendGameAfterOverlayIsVisibleAsync(windowAtOpen, gameIdAtOpen);
            }

            RefreshOverlayDataAfterShowAsync(windowAtOpen, gameIdAtOpen);
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

        public void ReturnToGame()
        {
            OverlayDebugLog("[Overlay][ReturnToGame] START");

            try
            {
                HideOverlayImmediate();

                if (lastForegroundWindow != IntPtr.Zero)
                {
                    ShowWindow(lastForegroundWindow, SW_RESTORE);
                    ForceFocusWindow(lastForegroundWindow);

                    OverlayDebugLog("[Overlay][ReturnToGame] Tried lastForegroundWindow.");
                    return;
                }

                if (lastForegroundWindowProcessId.HasValue)
                {
                    var process = Process.GetProcessById(lastForegroundWindowProcessId.Value);

                    if (process != null && !process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        ForceFocusWindow(process.MainWindowHandle);

                        OverlayDebugLog("[Overlay][ReturnToGame] Focused lastForegroundWindowProcessId.");
                        return;
                    }
                }

                if (currentGameProcessId.HasValue)
                {
                    var process = Process.GetProcessById(currentGameProcessId.Value);

                    if (process != null && !process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        ForceFocusWindow(process.MainWindowHandle);

                        OverlayDebugLog("[Overlay][ReturnToGame] Focused currentGameProcessId.");
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


        private void LoadOverlayAchievements()
        {
            try
            {
                var gameId = currentGame != null ? currentGame.Id : Guid.Empty;
                var gameName = currentGame != null ? currentGame.Name : string.Empty;
                settings?.LoadOverlayAchievements(gameId, gameName);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][Overlay] Failed to prepare Achievements window.");
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

                LoadOverlayAchievements();

                if (overlayWindow != null && overlayWindow.IsVisible)
                {
                    overlayWindow.ShowAchievements();
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

                if (pid <= 0 && pidFromWindow > 0)
                {
                    pid = (int)pidFromWindow;
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
                return true;
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
                    ?? LoadSuccessStoryAchievementSummary(gameAtCall)
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

        private AchievementOverlaySummary LoadSuccessStoryAchievementSummary(Game game)
        {
            try
            {
                var ssRoot = FindSuccessStoryRoot();
                if (string.IsNullOrWhiteSpace(ssRoot) || !Directory.Exists(ssRoot))
                {
                    return null;
                }

                var files = Directory.EnumerateFiles(ssRoot, "*.json", SearchOption.AllDirectories).ToArray();
                if (files.Length == 0)
                {
                    return null;
                }

                foreach (var file in files)
                {
                    SsFile rootObj = null;

                    try
                    {
                        rootObj = Serialization.FromJsonFile<SsFile>(file);
                    }
                    catch
                    {
                        try
                        {
                            var arrOnly = Serialization.FromJsonFile<List<SsItem>>(file);
                            rootObj = new SsFile { Items = arrOnly };
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (rootObj == null)
                    {
                        continue;
                    }

                    var fileGameName = !string.IsNullOrWhiteSpace(rootObj.Name)
                        ? rootObj.Name
                        : (rootObj.Game?.Name ?? Path.GetFileNameWithoutExtension(file));

                    if (!IsSameGameForAchievements(game, fileGameName, file))
                    {
                        continue;
                    }

                    var items = rootObj.Items ?? rootObj.Achievements ?? new List<SsItem>();
                    if (items.Count == 0)
                    {
                        return null;
                    }

                    var unlocked = items.Where(IsAchievementUnlocked).ToList();
                    var latest = unlocked
                        .Select(x => new
                        {
                            Item = x,
                            Date = ParseAchievementUnlockDate(x)
                        })
                        .Where(x => x.Date != null)
                        .OrderByDescending(x => x.Date.Value)
                        .FirstOrDefault();

                    return new AchievementOverlaySummary
                    {
                        Unlocked = unlocked.Count,
                        Total = items.Count,
                        LastUnlockedTitle = latest != null ? GetAchievementTitle(latest.Item) : string.Empty
                    };
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] Failed to load SuccessStory achievement summary for overlay.");
            }

            return null;
        }

        private string FindSuccessStoryRoot()
        {
            try
            {
                var root = playniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return null;
                }

                var classic = Path.Combine(root, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                if (Directory.Exists(classic) && Directory.EnumerateFiles(classic, "*.json", SearchOption.AllDirectories).Any())
                {
                    return classic;
                }

                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (!dir.EndsWith("SuccessStory", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Any())
                    {
                        return dir;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool IsSameGameForAchievements(Game game, string achievementGameName, string filePath)
        {
            if (game == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(filePath) &&
                filePath.IndexOf(game.Id.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(achievementGameName) || string.IsNullOrWhiteSpace(game.Name))
            {
                return false;
            }

            return string.Equals(NormalizeGameName(achievementGameName), NormalizeGameName(game.Name), StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeGameName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray();

            return new string(chars);
        }

        private bool IsAchievementUnlocked(SsItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(item.DateUnlocked) && !item.DateUnlocked.StartsWith("0001-01-01"))
            {
                return true;
            }

            if (item.UnlockTime != null)
            {
                return true;
            }

            if (string.Equals(item.IsUnlock, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(item.Earned, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(item.Unlocked, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private DateTime? ParseAchievementUnlockDate(SsItem item)
        {
            if (item == null)
            {
                return null;
            }

            DateTime parsed;

            if (!string.IsNullOrWhiteSpace(item.DateUnlocked) && DateTime.TryParse(item.DateUnlocked, out parsed))
            {
                return parsed;
            }

            if (item.UnlockTime != null)
            {
                return DateTimeOffset.FromUnixTimeSeconds(item.UnlockTime.Value).LocalDateTime;
            }

            if (!string.IsNullOrWhiteSpace(item.UnlockTimestamp) && DateTime.TryParse(item.UnlockTimestamp, out parsed))
            {
                return parsed;
            }

            if (!string.IsNullOrWhiteSpace(item.LastUnlock) && DateTime.TryParse(item.LastUnlock, out parsed))
            {
                return parsed;
            }

            return null;
        }

        private string GetAchievementTitle(SsItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                return item.Name;
            }

            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                return item.Title;
            }

            return Loc("LOCInGameOverlayAchievement", "Achievement");
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
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
        private const int GameStatusFocusRetryDelayMs = 250;
        private const int GameStatusFocusMaxAttempts = 6;
        private sealed class SsFile
        {
            public string Name { get; set; }
            public SsGameInfo Game { get; set; }
            public List<SsItem> Items { get; set; }
            public List<SsItem> Achievements { get; set; }
        }

        private sealed class SsGameInfo
        {
            public string Name { get; set; }
        }

        private sealed class SsItem
        {
            public string Name { get; set; }
            public string Title { get; set; }
            public string DateUnlocked { get; set; }
            public long? UnlockTime { get; set; }
            public string UnlockTimestamp { get; set; }
            public string LastUnlock { get; set; }
            public string IsUnlock { get; set; }
            public string Earned { get; set; }
            public string Unlocked { get; set; }
        }

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
