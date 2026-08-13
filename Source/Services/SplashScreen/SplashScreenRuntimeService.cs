using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Threading;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenRuntimeService
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static void DebugLog(string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug(message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private static void DebugLog(Exception exception, string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug(exception, message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private const int ForegroundCheckIntervalMs = 200;
        private const int PostFocusLossDelayMs = 1000;
        private const int FocusLossStabilityMs = 400;
        private const int GameReadyStableCheckCount = 3;
        private const int GameReadyStableCheckDelayMs = 500;
        private const int HardSafetyExtraMs = 2000;

        public sealed class GameReadyCandidate
        {
            public int ProcessId { get; private set; }
            public IntPtr WindowHandle { get; private set; }
            public string Source { get; private set; }

            public GameReadyCandidate(int processId, IntPtr windowHandle, string source)
            {
                ProcessId = processId;
                WindowHandle = windowHandle;
                Source = source ?? string.Empty;
            }

            public bool HasSameIdentity(GameReadyCandidate other)
            {
                return other != null &&
                       ProcessId == other.ProcessId &&
                       WindowHandle == other.WindowHandle;
            }
        }

        private GameLaunchSplashWindow currentSplashWindow;
        private DateTime? currentSplashShownAt;
        private CancellationTokenSource launchFailureSafetyCts;


        private readonly Func<bool> isPlayniteForegroundWindow;

        public SplashScreenRuntimeService(Func<bool> isPlayniteForegroundWindow)
        {
            this.isPlayniteForegroundWindow = isPlayniteForegroundWindow;
        }

        private void CancelLaunchFailureSafety()
        {
            try
            {
                launchFailureSafetyCts?.Cancel();
                launchFailureSafetyCts?.Dispose();
            }
            catch
            {
            }
            finally
            {
                launchFailureSafetyCts = null;
            }
        }

        public void Show(Game game, string backgroundPath, string fallbackBackgroundPath, bool showLogo, SplashScreenLogoPosition logoPosition, bool videoSoundEnabled, SplashScreenVideoEndBehavior videoEndBehavior, double videoVolume)
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Close();

                    currentSplashWindow = new GameLaunchSplashWindow(
                        game,
                        backgroundPath,
                        fallbackBackgroundPath,
                        showLogo,
                        logoPosition,
                        videoSoundEnabled,
                        videoEndBehavior,
                        videoVolume);

                    currentSplashShownAt = null;

                    currentSplashWindow.ContentRendered += (_, __) =>
                    {
                        currentSplashShownAt = DateTime.Now;
                    };

                    currentSplashWindow.Show();

                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!currentSplashShownAt.HasValue)
                        {
                            currentSplashShownAt = DateTime.Now;
                        }
                    }), DispatcherPriority.Render);
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to show game launch splash.");
            }
        }

        public void StartLaunchFailureSafety(int minimumDurationMs)
        {
            try
            {
                CancelLaunchFailureSafety();

                var delayMs = Math.Max(500, minimumDurationMs) + HardSafetyExtraMs;
                var cts = new CancellationTokenSource();
                launchFailureSafetyCts = cts;

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs, cts.Token);

                        if (cts.IsCancellationRequested)
                        {
                            return;
                        }

                        logger.Warn($"[AnikiHelper] Splash launch failure safety reached after {delayMs} ms. Forcing close.");
                        Close();
                    }
                    catch (TaskCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Splash launch failure safety failed.");
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to start splash launch failure safety.");
            }
        }

        public async Task CloseAfterFixedDurationAsync(int durationMs)
        {
            try
            {
                CancelLaunchFailureSafety();

                var remainingMinimumDelay = GetRemainingMinimumDelay(durationMs);
                if (remainingMinimumDelay > 0)
                {
                    await Task.Delay(remainingMinimumDelay);
                }

                Close();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Splash fixed duration close failed.");
                Close();
            }
        }

        public async Task CloseAfterGameStartedAsync(int minimumDurationMs, int maximumWaitMs)
        {
            await CloseAfterGameStartedAsync(minimumDurationMs, maximumWaitMs, null, false);
        }

        public async Task CloseAfterGameStartedAsync(int minimumDurationMs, int maximumWaitMs, Func<GameReadyCandidate> getGameReadyCandidate, bool autoDetectGameReady)
        {
            try
            {
                CancelLaunchFailureSafety();

                var remainingMinimumDelay = GetRemainingMinimumDelay(minimumDurationMs);
                var normalizedMaximumWait = Math.Max(0, maximumWaitMs);
                var hardSafetyDelay = remainingMinimumDelay + normalizedMaximumWait + HardSafetyExtraMs;

                var normalCloseTask = autoDetectGameReady && getGameReadyCandidate != null
                    ? CloseAfterMinimumAndGameReadyAsync(remainingMinimumDelay, normalizedMaximumWait, getGameReadyCandidate)
                    : CloseAfterMinimumAndFocusLossAsync(remainingMinimumDelay, normalizedMaximumWait);

                var hardSafetyTask = Task.Delay(hardSafetyDelay);
                var completedTask = await Task.WhenAny(normalCloseTask, hardSafetyTask);

                if (completedTask == hardSafetyTask)
                {
                    logger.Warn($"[AnikiHelper] Splash hard safety timeout reached after {hardSafetyDelay} ms. Forcing close.");
                    Close();
                }
                else
                {
                    await normalCloseTask;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Splash runtime close failed.");
                Close();
            }
        }

        private async Task CloseAfterMinimumAndGameReadyAsync(int remainingMinimumDelay, int maximumWaitMs, Func<GameReadyCandidate> getGameReadyCandidate)
        {
            if (remainingMinimumDelay > 0)
            {
                await Task.Delay(remainingMinimumDelay);
            }

            var waitedAfterMinimum = 0;

            while (waitedAfterMinimum <= maximumWaitMs)
            {
                var initialCandidate = GetGameReadyCandidateSafe(getGameReadyCandidate);
                if (initialCandidate != null)
                {
                    var isStable = true;
                    for (var stableCheckIndex = 0; stableCheckIndex < GameReadyStableCheckCount; stableCheckIndex++)
                    {
                        await Task.Delay(GameReadyStableCheckDelayMs);

                        var currentCandidate = GetGameReadyCandidateSafe(getGameReadyCandidate);
                        if (!initialCandidate.HasSameIdentity(currentCandidate))
                        {
                            isStable = false;
                            DebugLog(
                                $"[AnikiHelper] Game ready candidate changed during stability check. " +
                                $"InitialPid={initialCandidate.ProcessId}, InitialHandle=0x{initialCandidate.WindowHandle.ToInt64():X}, " +
                                $"CurrentPid={(currentCandidate == null ? 0 : currentCandidate.ProcessId)}, " +
                                $"CurrentHandle=0x{(currentCandidate == null ? 0L : currentCandidate.WindowHandle.ToInt64()):X}.");
                            break;
                        }
                    }
                    if (isStable)
                    {
                        DebugLog(
                            $"[AnikiHelper] Game ready candidate confirmed. " +
                            $"Source={initialCandidate.Source}, ProcessId={initialCandidate.ProcessId}, " +
                            $"Handle=0x{initialCandidate.WindowHandle.ToInt64():X}.");
                        Close();
                        return;
                    }

                    DebugLog("[AnikiHelper] Game ready detection was not stable yet. Keeping splash open.");
                }

                await Task.Delay(ForegroundCheckIntervalMs);
                waitedAfterMinimum += ForegroundCheckIntervalMs;
            }
            DebugLog($"[AnikiHelper] Game ready detection timeout reached after {maximumWaitMs} ms. Closing splash.");
            Close();
        }

        private GameReadyCandidate GetGameReadyCandidateSafe(Func<GameReadyCandidate> getGameReadyCandidate)
        {
            try
            {
                return getGameReadyCandidate?.Invoke();
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper] Game ready detection callback failed.");
                return null;
            }
        }

        private async Task CloseAfterMinimumAndFocusLossAsync(int remainingMinimumDelay, int maximumWaitMs)
        {
            if (remainingMinimumDelay > 0)
            {
                await Task.Delay(remainingMinimumDelay);
            }

            var waitedAfterStarted = 0;

            while (waitedAfterStarted < maximumWaitMs)
            {
                if (!isPlayniteForegroundWindow())
                {
                    await Task.Delay(FocusLossStabilityMs);

                    if (!isPlayniteForegroundWindow())
                    {
                        await Task.Delay(PostFocusLossDelayMs);
                        Close();
                        return;
                    }

                    DebugLog("[AnikiHelper] Playnite regained foreground during stability check. Keeping splash open.");
                }

                await Task.Delay(ForegroundCheckIntervalMs);
                waitedAfterStarted += ForegroundCheckIntervalMs;
            }

            Close();
        }

        public void Close()
        {
            try
            {
                CancelLaunchFailureSafety();

                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null)
                {
                    currentSplashWindow = null;
                    currentSplashShownAt = null;
                    return;
                }

                dispatcher.Invoke(async () =>
                {
                    try
                    {
                        if (currentSplashWindow == null)
                        {
                            return;
                        }

                        var splash = currentSplashWindow;
                        currentSplashWindow = null;
                        currentSplashShownAt = null;

                        await splash.BeginCloseAsync();
                        splash.Close();
                    }
                    catch
                    {
                        currentSplashWindow = null;
                        currentSplashShownAt = null;
                    }
                });
            }
            catch
            {
                currentSplashWindow = null;
                currentSplashShownAt = null;
            }
        }

        private int GetRemainingMinimumDelay(int minimumDurationMs)
        {
            if (currentSplashWindow == null)
            {
                return 0;
            }

            if (!currentSplashShownAt.HasValue)
            {
                return minimumDurationMs;
            }

            var elapsed = (int)(DateTime.Now - currentSplashShownAt.Value).TotalMilliseconds;
            return Math.Max(0, minimumDurationMs - elapsed);
        }
    }
}