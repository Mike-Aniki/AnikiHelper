using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AnikiHelper.Services.EasterEgg
{
    /// <summary>
    /// Lightweight, event-based Konami Code detector for controller input only.
    /// It does not poll input and does not consume controller events.
    /// </summary>
    public class KonamiCodeService
    {
        private enum KonamiToken
        {
            None,
            Up,
            Down,
            Left,
            Right,
            B,
            A
        }

        private static readonly KonamiToken[] Sequence = new[]
        {
            KonamiToken.Up,
            KonamiToken.Up,
            KonamiToken.Down,
            KonamiToken.Down,
            KonamiToken.Left,
            KonamiToken.Right,
            KonamiToken.Left,
            KonamiToken.Right,
            KonamiToken.B,
            KonamiToken.A
        };

        private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ActiveDuration = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan DuplicateInputGuard = TimeSpan.FromMilliseconds(90);

        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;
        private readonly Action<string> debugLog;
        private readonly Action onKonamiCodeCompleted;

        private int sequenceIndex;
        private DateTime lastInputUtc = DateTime.MinValue;
        private DateTime lastTriggerUtc = DateTime.MinValue;
        private KonamiToken lastToken = KonamiToken.None;
        private DateTime lastTokenUtc = DateTime.MinValue;
        private CancellationTokenSource hideCts;

        public KonamiCodeService(AnikiHelperSettings settings, ILogger logger, Action<string> debugLog = null, Action onKonamiCodeCompleted = null)
        {
            this.settings = settings;
            this.logger = logger;
            this.debugLog = debugLog;
            this.onKonamiCodeCompleted = onKonamiCodeCompleted;
        }

        public void ProcessControllerInput(OnControllerButtonStateChangedArgs args)
        {
            try
            {
                if (args == null || settings == null)
                {
                    return;
                }

                if (args.State != ControllerInputState.Pressed)
                {
                    return;
                }

                var token = NormalizeButton(args.Button);
                if (token == KonamiToken.None)
                {
                    ResetProgress();
                    return;
                }

                var now = DateTime.UtcNow;

                if (lastToken == token && now - lastTokenUtc < DuplicateInputGuard)
                {
                    return;
                }

                lastToken = token;
                lastTokenUtc = now;

                if (sequenceIndex > 0 && now - lastInputUtc > InputTimeout)
                {
                    ResetProgress();
                }

                lastInputUtc = now;

                if (token == Sequence[sequenceIndex])
                {
                    sequenceIndex++;

                    if (sequenceIndex >= Sequence.Length)
                    {
                        ResetProgress();
                        TriggerEasterEgg(now);
                    }

                    return;
                }

                // If the current button can be the first step, immediately restart from step 1.
                sequenceIndex = token == Sequence[0] ? 1 : 0;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Konami Code processing failed.");
                ResetProgress();
            }
        }

        private KonamiToken NormalizeButton(ControllerInput button)
        {
            var name = button.ToString();

            switch (name)
            {
                case "DPadUp":
                case "LeftStickUp":
                    return KonamiToken.Up;

                case "DPadDown":
                case "LeftStickDown":
                    return KonamiToken.Down;

                case "DPadLeft":
                case "LeftStickLeft":
                    return KonamiToken.Left;

                case "DPadRight":
                case "LeftStickRight":
                    return KonamiToken.Right;

                case "B":
                    return KonamiToken.B;

                case "A":
                    return KonamiToken.A;

                default:
                    return KonamiToken.None;
            }
        }

        private void TriggerEasterEgg(DateTime now)
        {
            if (now - lastTriggerUtc < CooldownDuration)
            {
                return;
            }

            lastTriggerUtc = now;
            debugLog?.Invoke("[AnikiHelper][Konami] Konami Code completed. Easter egg triggered. Konami Mode enabled for this session.");

            SetModeOnUi(true);
            SetActiveOnUi(true);
            PlayCompletedSoundSafe();

            hideCts?.Cancel();
            hideCts?.Dispose();
            hideCts = new CancellationTokenSource();
            var token = hideCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ActiveDuration, token).ConfigureAwait(false);

                    if (!token.IsCancellationRequested)
                    {
                        SetActiveOnUi(false);
                    }
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] Failed to hide Konami Easter Egg.");
                }
            }, token);
        }

        private void SetActiveOnUi(bool active)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    settings.IsKonamiEasterEggActive = active;
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    settings.IsKonamiEasterEggActive = active;
                }));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to update Konami Easter Egg state.");
            }
        }


        private void SetModeOnUi(bool active)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    settings.IsKonamiModeActive = active;
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    settings.IsKonamiModeActive = active;
                }));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to update Konami Mode state.");
            }
        }

        private void PlayCompletedSoundSafe()
        {
            try
            {
                onKonamiCodeCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to play Konami Code completion sound.");
            }
        }

        private void ResetProgress()
        {
            sequenceIndex = 0;
            lastInputUtc = DateTime.MinValue;
        }
    }
}
