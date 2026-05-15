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

        private const int ForegroundCheckIntervalMs = 200;
        private const int PostFocusLossDelayMs = 1000;
        private const int FocusLossStabilityMs = 400;
        private const int HardSafetyExtraMs = 2000;

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

        public async Task CloseAfterGameStartedAsync(int minimumDurationMs, int maximumWaitMs)
        {
            try
            {
                CancelLaunchFailureSafety();

                var remainingMinimumDelay = GetRemainingMinimumDelay(minimumDurationMs);
                var hardSafetyDelay = remainingMinimumDelay + HardSafetyExtraMs;

                var normalCloseTask = CloseAfterMinimumAndFocusLossAsync(remainingMinimumDelay, maximumWaitMs);
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

                    logger.Info("[AnikiHelper] Playnite regained foreground during stability check. Keeping splash open.");
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