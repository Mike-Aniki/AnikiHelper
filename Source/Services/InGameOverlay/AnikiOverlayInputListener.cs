using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using AnikiHelper.Services.WebBrowser;

namespace AnikiHelper.Services.InGameOverlay
{
    /// <summary>Routes P10 input and reads Playnite SDK 6.17 controller state for analog/in-game fallback.</summary>
    internal sealed class AnikiOverlayInputListener : IDisposable
    {
        private const int GuideComboGraceMs = 180;
        private const int ShortcutChordGraceMs = 400;
        private const int VirtualKeyboardHoldDurationMs = 600;
        private const int VirtualKeyboardShortcutCooldownMs = 700;
        private const int GamepadMouseHoldDurationMs = 600;
        private const int GamepadMouseShortcutCooldownMs = 800;
        private const int LeftStickSoloClickMaxDurationMs = 500;
        private const int AnalogPollIntervalMs = 16;

        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;
        private readonly Action onShortcutPressed;
        private readonly Action onVirtualKeyboardShortcutPressed;
        private readonly Action onGamepadMouseToggle;
        private readonly Func<bool> isGamepadMouseActive;
        private readonly Action<GamepadMouseInputState> onGamepadMouseInput;
        private readonly Action onGamepadMouseSuspendInput;
        private readonly Func<bool> isOverlayEnabled;
        private readonly Func<bool> isOverlayVisible;
        private readonly Action<ControllerInput> onOverlayButtonPressed;
        private readonly Func<bool> shouldUsePolledDigitalFallback;
        private readonly Func<bool> isWebBrowserActive;
        private readonly Action<WebBrowserGamepadInputState> onWebBrowserInput;

        // P10 provides the physical L3/R3 button state directly. Keep the existing event name
        // so Video Center does not need to know where the button state comes from.
        internal static event Action LeftStickClicked;

        private readonly Dictionary<int, HashSet<ControllerInput>> heldButtonsByController =
            new Dictionary<int, HashSet<ControllerInput>>();
        private readonly HashSet<ControllerInput> polledFallbackHeldButtons = new HashSet<ControllerInput>();

        private DispatcherTimer analogTimer;
        private bool isStarted;
        private bool controllerStateReadSuccessLogged;
        private bool controllerStateReadFailureLogged;
        private bool polledDigitalFallbackActive;
        private bool polledDigitalFallbackSuccessLogged;
        private bool polledDigitalFallbackNoControllerLogged;

        private bool shortcutHeld;
        private bool virtualKeyboardShortcutHeld;
        private bool gamepadMouseShortcutHeld;
        private bool browserBackPressPending;
        private bool browserBackChordConsumed;
        private bool browserShortcutSuppressionActive;
        private bool leftStickSoloClickCandidate;

        private DateTime leftStickSoloPressedAt = DateTime.MinValue;
        private DateTime? guidePressedAt;
        private DateTime? virtualKeyboardHoldStartedAt;
        private DateTime? gamepadMouseHoldStartedAt;
        private DateTime lastShortcutTime = DateTime.MinValue;
        private DateTime lastStartPressedTime = DateTime.MinValue;
        private DateTime lastBackPressedTime = DateTime.MinValue;
        private DateTime lastYPressedTime = DateTime.MinValue;
        private DateTime lastVirtualKeyboardShortcutTime = DateTime.MinValue;
        private DateTime lastGamepadMouseShortcutTime = DateTime.MinValue;

        private struct ButtonTransition
        {
            public bool PressedNow;
            public bool ReleasedNow;
        }

        private struct AnalogState
        {
            public short LeftX;
            public short LeftY;
            public short RightX;
            public short RightY;
            public short LeftTrigger;
            public short RightTrigger;
        }

        public AnikiOverlayInputListener(
            IPlayniteAPI playniteApi,
            AnikiHelperSettings settings,
            ILogger logger,
            Action onShortcutPressed,
            Action onVirtualKeyboardShortcutPressed,
            Action onGamepadMouseToggle,
            Func<bool> isGamepadMouseActive,
            Action<GamepadMouseInputState> onGamepadMouseInput,
            Action onGamepadMouseSuspendInput,
            Func<bool> isOverlayEnabled,
            Func<bool> isOverlayVisible,
            Action<ControllerInput> onOverlayButtonPressed,
            Func<bool> shouldUsePolledDigitalFallback,
            Func<bool> isWebBrowserActive,
            Action<WebBrowserGamepadInputState> onWebBrowserInput)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.logger = logger;
            this.onShortcutPressed = onShortcutPressed;
            this.onVirtualKeyboardShortcutPressed = onVirtualKeyboardShortcutPressed;
            this.onGamepadMouseToggle = onGamepadMouseToggle;
            this.isGamepadMouseActive = isGamepadMouseActive;
            this.onGamepadMouseInput = onGamepadMouseInput;
            this.onGamepadMouseSuspendInput = onGamepadMouseSuspendInput;
            this.isOverlayEnabled = isOverlayEnabled;
            this.isOverlayVisible = isOverlayVisible;
            this.onOverlayButtonPressed = onOverlayButtonPressed;
            this.shouldUsePolledDigitalFallback = shouldUsePolledDigitalFallback;
            this.isWebBrowserActive = isWebBrowserActive;
            this.onWebBrowserInput = onWebBrowserInput;
        }

        public void Start()
        {
            if (isStarted)
            {
                return;
            }

            isStarted = true;
            StartAnalogTimer();

            DebugLog(
                "[AnikiHelper][OverlayInput][P10] Native button routing started. " +
                "Analog state is read through Playnite SDK 6.17 GetConnectedControllers2(); " +
                "Aniki Helper no longer reads SDL controller handles directly.");
        }

        public void Stop()
        {
            if (!isStarted)
            {
                return;
            }

            isStarted = false;
            StopAnalogTimer();
            onGamepadMouseSuspendInput?.Invoke();

            heldButtonsByController.Clear();
            polledFallbackHeldButtons.Clear();
            polledDigitalFallbackActive = false;
            ResetTransientState();

            DebugLog("[AnikiHelper][OverlayInput][P10] Native controller router stopped.");
        }

        public void HandleControllerConnected(OnControllerConnectedArgs args)
        {
            try
            {
                var controller = args?.Controller;
                if (controller == null)
                {
                    return;
                }

                DebugLog(
                    $"[AnikiHelper][OverlayInput][P10] Controller connected. " +
                    $"InstanceId={controller.InstanceId}, Name='{controller.Name}', Enabled={controller.Enabled}. " +
                    "State is provided by Playnite SDK.");
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput][P10] Failed to register connected controller.");
            }
        }

        public void HandleControllerDisconnected(OnControllerDisconnectedArgs args)
        {
            try
            {
                var controller = args?.Controller;
                if (controller == null)
                {
                    return;
                }

                heldButtonsByController.Remove(controller.InstanceId);
                polledFallbackHeldButtons.Clear();
                polledDigitalFallbackActive = false;
                ResetTransientStateAfterTopologyChange();

                DebugLog(
                    $"[AnikiHelper][OverlayInput][P10] Controller disconnected. " +
                    $"InstanceId={controller.InstanceId}, Name='{controller.Name}'.");
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput][P10] Failed to unregister disconnected controller.");
            }
        }

        /// <summary>Processes a P10 button-state update and reports whether Aniki consumed it.</summary>
        public bool HandleControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (!isStarted || args == null)
            {
                return false;
            }

            var transition = UpdateButtonState(args);
            return ProcessButtonTransition(args.Button, transition, "P10");
        }

        private bool ProcessButtonTransition(ControllerInput button, ButtonTransition transition, string source)
        {
            UpdatePressTimestamps(button, transition.PressedNow);
            UpdateLeftStickSoloCandidate(button, transition);

            // The overlay/Aniki keyboard must win over the Browser. During a browser keyboard
            // session the Browser stays visible, but B/Back belongs to the keyboard until it closes.
            if (isOverlayVisible?.Invoke() == true)
            {
                shortcutHeld = false;
                onGamepadMouseSuspendInput?.Invoke();

                if (transition.PressedNow)
                {
                    if (string.Equals(source, "SDK-Poll", StringComparison.Ordinal))
                    {
                        DebugLog($"[AnikiHelper][OverlayInput][SDK-Poll] Overlay button pressed: {button}.");
                    }

                    RouteOverlayButton(button);
                }

                return true;
            }

            if (isWebBrowserActive?.Invoke() == true)
            {
                shortcutHeld = false;
                ResetVirtualKeyboardShortcutState();
                ResetGamepadMouseShortcutState();
                onGamepadMouseSuspendInput?.Invoke();

                RouteBrowserButton(button, transition);
                return true;
            }

            if (HandleBrowserPostCloseSuppression())
            {
                return false;
            }

            var gamepadMouseChordActive = ProcessGamepadMouseShortcut();
            var virtualKeyboardChordActive = ProcessVirtualKeyboardShortcut();

            if (gamepadMouseChordActive || virtualKeyboardChordActive)
            {
                onGamepadMouseSuspendInput?.Invoke();
                return false;
            }

            if (transition.ReleasedNow && button == ControllerInput.LeftStick)
            {
                TryRaiseLeftStickSoloClick();
            }

            if (ProcessOverlayShortcutEvent(button, transition))
            {
                return true;
            }

            return false;
        }

        private void StartAnalogTimer()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    logger?.Warn("[AnikiHelper] Analog controller bridge could not start because the WPF dispatcher is unavailable.");
                    return;
                }

                Action start = () =>
                {
                    if (!isStarted || analogTimer != null || dispatcher.HasShutdownStarted)
                    {
                        return;
                    }

                    analogTimer = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(AnalogPollIntervalMs)
                    };
                    analogTimer.Tick += AnalogTimer_Tick;
                    analogTimer.Start();
                };

                if (dispatcher.CheckAccess())
                {
                    start();
                }
                else
                {
                    dispatcher.Invoke(start);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to start analog controller bridge timer.");
            }
        }

        private void StopAnalogTimer()
        {
            var timer = analogTimer;
            analogTimer = null;

            if (timer == null)
            {
                return;
            }

            try
            {
                var dispatcher = timer.Dispatcher;
                Action stop = () =>
                {
                    try { timer.Stop(); } catch { }
                    try { timer.Tick -= AnalogTimer_Tick; } catch { }
                };

                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    return;
                }

                if (dispatcher.CheckAccess())
                {
                    stop();
                }
                else
                {
                    dispatcher.Invoke(stop);
                }
            }
            catch
            {
                // Dispatcher shutdown will tear down the timer. There is no background worker.
            }
        }

        private void AnalogTimer_Tick(object sender, EventArgs e)
        {
            if (!isStarted)
            {
                return;
            }

            try
            {
                var usePolledDigitalFallback = false;
                try { usePolledDigitalFallback = shouldUsePolledDigitalFallback?.Invoke() == true; } catch { }

                if (usePolledDigitalFallback)
                {
                    PollSdkDigitalFallback();
                }
                else if (polledDigitalFallbackActive)
                {
                    StopSdkDigitalFallback();
                }

                // Hold-based shortcuts are driven by the currently active digital source:
                // P10 events while Playnite owns input, or SDK-polled button state when a fallback
                // is requested. The same state machine is reused for both paths.
                if (isOverlayVisible?.Invoke() != true && isWebBrowserActive?.Invoke() != true)
                {
                    if (HandleBrowserPostCloseSuppression())
                    {
                        return;
                    }

                    var mouseChord = ProcessGamepadMouseShortcut();
                    var keyboardChord = ProcessVirtualKeyboardShortcut();

                    if (mouseChord || keyboardChord)
                    {
                        onGamepadMouseSuspendInput?.Invoke();
                        return;
                    }

                    ProcessGuideShortcutGrace();
                }

                var overlayVisible = isOverlayVisible?.Invoke() == true;
                if (overlayVisible)
                {
                    onGamepadMouseSuspendInput?.Invoke();
                    return;
                }

                var browserActive = isWebBrowserActive?.Invoke() == true;
                var mouseActive = isGamepadMouseActive?.Invoke() == true;

                if (!browserActive && !mouseActive)
                {
                    return;
                }

                if (mouseActive && string.Equals(
                    settings?.InGameOverlayGamepadMouseShortcut,
                    "Disabled",
                    StringComparison.OrdinalIgnoreCase))
                {
                    onGamepadMouseToggle?.Invoke();
                    mouseActive = false;
                }

                if (!browserActive && !mouseActive)
                {
                    return;
                }

                var analog = ReadAnalogState();

                if (browserActive)
                {
                    onGamepadMouseSuspendInput?.Invoke();
                    onWebBrowserInput?.Invoke(new WebBrowserGamepadInputState
                    {
                        LeftX = analog.LeftX,
                        LeftY = analog.LeftY,
                        RightX = analog.RightX,
                        RightY = analog.RightY,
                        LeftClick = IsHeld(ControllerInput.A)
                    });
                    return;
                }

                if (mouseActive)
                {
                    onGamepadMouseInput?.Invoke(new GamepadMouseInputState
                    {
                        RightX = analog.RightX,
                        RightY = analog.RightY,
                        LeftY = analog.LeftY,
                        LeftTrigger = analog.LeftTrigger,
                        RightTrigger = analog.RightTrigger,
                        LeftClick = IsHeld(ControllerInput.A),
                        RightClick = IsHeld(ControllerInput.X)
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput][P10] Controller timer tick failed.");
            }
        }

        private void PollSdkDigitalFallback()
        {
            if (!polledDigitalFallbackActive)
            {
                // A source handoff can happen between a press and its release. Drop stale
                // event-driven state before the SDK-polled state becomes the source of truth.
                heldButtonsByController.Clear();
                polledFallbackHeldButtons.Clear();
                ResetTransientState();
                polledDigitalFallbackActive = true;
                polledDigitalFallbackNoControllerLogged = false;

                DebugLog(
                    "[AnikiHelper][OverlayInput][SDK-Poll] Digital fallback enabled. " +
                    "Reading ButtonInputState from Playnite SDK 6.17.");
            }

            var controllers = GetConnectedControllers2Safe();
            if (controllers == null || !controllers.Any(controller => controller != null && controller.Enabled))
            {
                if (!polledDigitalFallbackNoControllerLogged)
                {
                    polledDigitalFallbackNoControllerLogged = true;
                    DebugLog("[AnikiHelper][OverlayInput][SDK-Poll] Waiting for an enabled Playnite controller.");
                }

                return;
            }

            polledDigitalFallbackNoControllerLogged = false;

            var states = new Dictionary<ControllerInput, bool>
            {
                [ControllerInput.A] = false,
                [ControllerInput.B] = false,
                [ControllerInput.X] = false,
                [ControllerInput.Y] = false,
                [ControllerInput.Back] = false,
                [ControllerInput.Guide] = false,
                [ControllerInput.Start] = false,
                [ControllerInput.LeftStick] = false,
                [ControllerInput.RightStick] = false,
                [ControllerInput.LeftShoulder] = false,
                [ControllerInput.RightShoulder] = false,
                [ControllerInput.DPadUp] = false,
                [ControllerInput.DPadDown] = false,
                [ControllerInput.DPadLeft] = false,
                [ControllerInput.DPadRight] = false
            };

            try
            {
                foreach (var controller in controllers)
                {
                    if (controller == null || !controller.Enabled)
                    {
                        continue;
                    }

                    foreach (var button in states.Keys.ToArray())
                    {
                        states[button] |= IsSdkButtonPressed(controller, button);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput][SDK-Poll] Failed to read ButtonInputState.");
                return;
            }

            foreach (var state in states)
            {
                var transition = UpdatePolledFallbackButtonState(state.Key, state.Value);
                if (!transition.PressedNow && !transition.ReleasedNow)
                {
                    continue;
                }

                ProcessButtonTransition(state.Key, transition, "SDK-Poll");
            }

            if (!polledDigitalFallbackSuccessLogged)
            {
                polledDigitalFallbackSuccessLogged = true;
                DebugLog("[AnikiHelper][OverlayInput][SDK-Poll] Digital controller state is available.");
            }
        }

        private void StopSdkDigitalFallback()
        {
            polledDigitalFallbackActive = false;
            polledFallbackHeldButtons.Clear();

            // Do not clear event-driven state here: a fresh P10 event may already have arrived
            // before this timer observes the source handoff.
            ResetTransientState();
            DebugLog("[AnikiHelper][OverlayInput][SDK-Poll] Digital fallback disabled; native P10 event routing resumed.");
        }

        private ButtonTransition UpdatePolledFallbackButtonState(ControllerInput button, bool pressed)
        {
            var wasHeld = polledFallbackHeldButtons.Contains(button);

            if (pressed)
            {
                polledFallbackHeldButtons.Add(button);
            }
            else
            {
                polledFallbackHeldButtons.Remove(button);
            }

            var isHeld = polledFallbackHeldButtons.Contains(button);
            return new ButtonTransition
            {
                PressedNow = isHeld && !wasHeld,
                ReleasedNow = !isHeld && wasHeld
            };
        }

        private IReadOnlyList<IGamepad> GetConnectedControllers2Safe()
        {
            try
            {
                var controllers = playniteApi?.GetConnectedControllers2();
                controllerStateReadFailureLogged = false;
                return controllers;
            }
            catch (Exception ex)
            {
                if (!controllerStateReadFailureLogged)
                {
                    controllerStateReadFailureLogged = true;
                    DebugLog(ex, "[AnikiHelper][OverlayInput][SDK6.17] GetConnectedControllers2 failed.");
                }

                return null;
            }
        }

        internal bool IsButtonCurrentlyPressed(ControllerInput button)
        {
            try
            {
                var controllers = GetConnectedControllers2Safe();
                if (controllers != null)
                {
                    foreach (var controller in controllers)
                    {
                        if (controller != null && controller.Enabled && IsSdkButtonPressed(controller, button))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput][SDK6.17] Failed to read current button state.");
            }

            // Fallback to the event-driven state if a controller snapshot is temporarily
            // unavailable during a focus handoff.
            return IsHeld(button);
        }

        private static bool IsSdkButtonPressed(IGamepad controller, ControllerInput button)
        {
            if (controller?.ButtonInputState == null)
            {
                return false;
            }

            return controller.ButtonInputState.TryGetValue(button, out var state) &&
                   state == ControllerInputState.Pressed;
        }

        private static short GetSdkAxis(IGamepad controller, ControllerInput axis)
        {
            if (controller?.AnalogInputState == null)
            {
                return 0;
            }

            return controller.AnalogInputState.TryGetValue(axis, out var value) ? value : (short)0;
        }

        private AnalogState ReadAnalogState()
        {
            var result = new AnalogState();
            var controllers = GetConnectedControllers2Safe();

            if (controllers == null)
            {
                return result;
            }

            try
            {
                var enabledControllerFound = false;

                foreach (var controller in controllers)
                {
                    if (controller == null || !controller.Enabled)
                    {
                        continue;
                    }

                    enabledControllerFound = true;

                    result.LeftX = SelectAxisWithGreatestMagnitude(
                        result.LeftX,
                        GetSdkAxis(controller, ControllerInput.LeftStickX));
                    result.LeftY = SelectAxisWithGreatestMagnitude(
                        result.LeftY,
                        GetSdkAxis(controller, ControllerInput.LeftStickY));
                    result.RightX = SelectAxisWithGreatestMagnitude(
                        result.RightX,
                        GetSdkAxis(controller, ControllerInput.RightStickX));
                    result.RightY = SelectAxisWithGreatestMagnitude(
                        result.RightY,
                        GetSdkAxis(controller, ControllerInput.RightStickY));
                    result.LeftTrigger = Math.Max(
                        result.LeftTrigger,
                        GetSdkAxis(controller, ControllerInput.TriggerLeft));
                    result.RightTrigger = Math.Max(
                        result.RightTrigger,
                        GetSdkAxis(controller, ControllerInput.TriggerRight));
                }

                if (enabledControllerFound && !controllerStateReadSuccessLogged)
                {
                    controllerStateReadSuccessLogged = true;
                    DebugLog(
                        "[AnikiHelper][OverlayInput][SDK6.17] Analog state is available through GetConnectedControllers2().");
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput][SDK6.17] Analog state read failed.");
            }

            return result;
        }

        private ButtonTransition UpdateButtonState(OnControllerButtonStateChangedArgs args)
        {
            var controllerId = args.Controller?.InstanceId ?? int.MinValue;

            var wasHeld = IsHeld(args.Button);

            if (!heldButtonsByController.TryGetValue(controllerId, out var buttons))
            {
                buttons = new HashSet<ControllerInput>();
                heldButtonsByController[controllerId] = buttons;
            }

            if (args.State == ControllerInputState.Pressed)
            {
                buttons.Add(args.Button);
            }
            else
            {
                buttons.Remove(args.Button);
                if (buttons.Count == 0)
                {
                    heldButtonsByController.Remove(controllerId);
                }
            }

            var isHeld = IsHeld(args.Button);
            return new ButtonTransition
            {
                PressedNow = isHeld && !wasHeld,
                ReleasedNow = !isHeld && wasHeld
            };
        }

        private bool IsHeld(ControllerInput button)
        {
            if (polledFallbackHeldButtons.Contains(button))
            {
                return true;
            }

            foreach (var buttons in heldButtonsByController.Values)
            {
                if (buttons.Contains(button))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdatePressTimestamps(ControllerInput button, bool pressedNow)
        {
            if (!pressedNow)
            {
                return;
            }

            var now = DateTime.UtcNow;
            switch (button)
            {
                case ControllerInput.Start:
                    lastStartPressedTime = now;
                    break;
                case ControllerInput.Back:
                    lastBackPressedTime = now;
                    break;
                case ControllerInput.Y:
                    lastYPressedTime = now;
                    break;
            }
        }

        private void UpdateLeftStickSoloCandidate(ControllerInput button, ButtonTransition transition)
        {
            var now = DateTime.UtcNow;

            if (button == ControllerInput.LeftStick && transition.PressedNow)
            {
                leftStickSoloClickCandidate = true;
                leftStickSoloPressedAt = now;
            }

            // L3 is shared by L3+R3 keyboard and Start+L3 mouse mode. Any chord partner
            // cancels the solo-click candidate exactly like the previous SDL implementation.
            if (leftStickSoloClickCandidate &&
                (IsHeld(ControllerInput.RightStick) || IsHeld(ControllerInput.Start)))
            {
                leftStickSoloClickCandidate = false;
            }
        }

        private void TryRaiseLeftStickSoloClick()
        {
            var heldMs = leftStickSoloPressedAt == DateTime.MinValue
                ? double.MaxValue
                : (DateTime.UtcNow - leftStickSoloPressedAt).TotalMilliseconds;

            var raise = leftStickSoloClickCandidate &&
                        heldMs >= 0 &&
                        heldMs <= LeftStickSoloClickMaxDurationMs;

            leftStickSoloClickCandidate = false;
            leftStickSoloPressedAt = DateTime.MinValue;

            if (!raise)
            {
                return;
            }

            try
            {
                LeftStickClicked?.Invoke();
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput][P10] L3 short-click listener failed.");
            }
        }

        private void RouteOverlayButton(ControllerInput button)
        {
            ControllerInput? routed = null;

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.DPadRight:
                case ControllerInput.DPadUp:
                case ControllerInput.DPadDown:
                case ControllerInput.A:
                case ControllerInput.X:
                case ControllerInput.Y:
                case ControllerInput.Start:
                    routed = button;
                    break;

                case ControllerInput.B:
                case ControllerInput.Back:
                    routed = ControllerInput.B;
                    break;
            }

            if (!routed.HasValue)
            {
                return;
            }

            DebugLog($"[AnikiHelper][OverlayInput] Overlay button pressed: {button} -> {routed.Value}.");
            onOverlayButtonPressed?.Invoke(routed.Value);
        }

        private void RouteBrowserButton(ControllerInput button, ButtonTransition transition)
        {
            var guide = IsHeld(ControllerInput.Guide);
            var start = IsHeld(ControllerInput.Start);
            var back = IsHeld(ControllerInput.Back);
            var y = IsHeld(ControllerInput.Y);
            var x = IsHeld(ControllerInput.X);
            var leftStick = IsHeld(ControllerInput.LeftStick);
            var rightStick = IsHeld(ControllerInput.RightStick);

            var browserShortcutChordHeld = IsBrowserShortcutChordHeld(
                guide,
                start,
                back,
                y,
                x,
                leftStick,
                rightStick);

            if (browserShortcutChordHeld)
            {
                browserShortcutSuppressionActive = true;
            }

            if (button == ControllerInput.Back && transition.PressedNow)
            {
                browserBackPressPending = true;
                browserBackChordConsumed = false;
            }

            if (browserBackPressPending && back &&
                (guide || start || y || x || leftStick || rightStick))
            {
                browserBackChordConsumed = true;
                browserShortcutSuppressionActive = true;
            }

            var closePressedNow = button == ControllerInput.Back &&
                                  transition.ReleasedNow &&
                                  browserBackPressPending &&
                                  !browserBackChordConsumed;

            if (button == ControllerInput.Back && transition.ReleasedNow)
            {
                browserBackPressPending = false;
                browserBackChordConsumed = false;
            }

            var suppressKeyboardButton = x && (back || guide);
            var suppressAddressButton = y && (back || guide);
            var suppressEnterButton = start && (back || leftStick);

            // Ignore threshold stick events here; browser analog input comes from the axis bridge.
            var shouldDispatchToBrowser =
                button == ControllerInput.A ||
                closePressedNow ||
                (transition.PressedNow &&
                 (button == ControllerInput.B ||
                  button == ControllerInput.X ||
                  button == ControllerInput.Y ||
                  button == ControllerInput.Start ||
                  button == ControllerInput.LeftShoulder ||
                  button == ControllerInput.RightShoulder ||
                  button == ControllerInput.DPadUp ||
                  button == ControllerInput.DPadDown ||
                  button == ControllerInput.DPadLeft ||
                  button == ControllerInput.DPadRight));

            if (!shouldDispatchToBrowser)
            {
                return;
            }

            onWebBrowserInput?.Invoke(new WebBrowserGamepadInputState
            {
                LeftClick = IsHeld(ControllerInput.A),
                ActivatePressed = button == ControllerInput.A && transition.PressedNow,
                BackPressed = button == ControllerInput.B && transition.PressedNow,
                ClosePressed = closePressedNow,
                KeyboardPressed = button == ControllerInput.X && transition.PressedNow && !suppressKeyboardButton,
                AddressPressed = button == ControllerInput.Y && transition.PressedNow && !suppressAddressButton,
                EnterPressed = button == ControllerInput.Start && transition.PressedNow && !suppressEnterButton,
                PreviousPressed = button == ControllerInput.LeftShoulder && transition.PressedNow,
                NextPressed = button == ControllerInput.RightShoulder && transition.PressedNow,
                DPadUpPressed = button == ControllerInput.DPadUp && transition.PressedNow,
                DPadDownPressed = button == ControllerInput.DPadDown && transition.PressedNow,
                DPadLeftPressed = button == ControllerInput.DPadLeft && transition.PressedNow,
                DPadRightPressed = button == ControllerInput.DPadRight && transition.PressedNow
            });
        }

        private bool HandleBrowserPostCloseSuppression()
        {
            if (!browserShortcutSuppressionActive)
            {
                browserBackPressPending = false;
                browserBackChordConsumed = false;
                return false;
            }

            if (IsAnyBrowserChordButtonHeld())
            {
                shortcutHeld = false;
                ResetVirtualKeyboardShortcutState();
                ResetGamepadMouseShortcutState();
                onGamepadMouseSuspendInput?.Invoke();
                return true;
            }

            ResetBrowserShortcutState();
            return false;
        }

        private bool IsAnyBrowserChordButtonHeld()
        {
            return IsHeld(ControllerInput.Guide) ||
                   IsHeld(ControllerInput.Start) ||
                   IsHeld(ControllerInput.Back) ||
                   IsHeld(ControllerInput.Y) ||
                   IsHeld(ControllerInput.X) ||
                   IsHeld(ControllerInput.LeftStick) ||
                   IsHeld(ControllerInput.RightStick);
        }

        private bool IsBrowserShortcutChordHeld(
            bool guide,
            bool start,
            bool back,
            bool y,
            bool x,
            bool leftStick,
            bool rightStick)
        {
            var mouseShortcut = settings?.InGameOverlayGamepadMouseShortcut ?? "BackR3";
            var keyboardShortcut = settings?.InGameOverlayVirtualKeyboardShortcut ?? "L3R3Hold";

            var mouseChordHeld =
                string.Equals(mouseShortcut, "StartL3", StringComparison.OrdinalIgnoreCase)
                    ? start && leftStick
                    : string.Equals(mouseShortcut, "GuideY", StringComparison.OrdinalIgnoreCase)
                        ? guide && y
                        : !string.Equals(mouseShortcut, "Disabled", StringComparison.OrdinalIgnoreCase) &&
                          back && rightStick;

            var keyboardChordHeld =
                string.Equals(keyboardShortcut, "BackX", StringComparison.OrdinalIgnoreCase)
                    ? back && x
                    : string.Equals(keyboardShortcut, "GuideX", StringComparison.OrdinalIgnoreCase)
                        ? guide && x
                        : !string.Equals(keyboardShortcut, "Disabled", StringComparison.OrdinalIgnoreCase) &&
                          leftStick && rightStick;

            var overlayShortcut = settings?.InGameOverlayControllerShortcut ?? "StartBack";
            var overlayChordHeld =
                string.Equals(overlayShortcut, "BackY", StringComparison.OrdinalIgnoreCase)
                    ? back && y
                    : string.Equals(overlayShortcut, "StartBack", StringComparison.OrdinalIgnoreCase)
                        ? start && back
                        : string.Equals(overlayShortcut, "Guide", StringComparison.OrdinalIgnoreCase) && guide;

            return mouseChordHeld || keyboardChordHeld || overlayChordHeld;
        }

        private bool ProcessGamepadMouseShortcut()
        {
            var shortcut = settings?.InGameOverlayGamepadMouseShortcut ?? "BackR3";

            if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                ResetGamepadMouseShortcutState();
                return false;
            }

            bool combinationHeld;
            switch (shortcut)
            {
                case "StartL3":
                    combinationHeld = IsHeld(ControllerInput.Start) && IsHeld(ControllerInput.LeftStick);
                    break;
                case "GuideY":
                    combinationHeld = IsHeld(ControllerInput.Guide) && IsHeld(ControllerInput.Y);
                    break;
                case "BackR3":
                default:
                    combinationHeld = IsHeld(ControllerInput.Back) && IsHeld(ControllerInput.RightStick);
                    break;
            }

            if (!combinationHeld)
            {
                ResetGamepadMouseShortcutState();
                return false;
            }

            if (string.Equals(shortcut, "GuideY", StringComparison.OrdinalIgnoreCase))
            {
                guidePressedAt = null;
            }

            if (gamepadMouseShortcutHeld)
            {
                return true;
            }

            if (!gamepadMouseHoldStartedAt.HasValue)
            {
                gamepadMouseHoldStartedAt = DateTime.UtcNow;
                return true;
            }

            if ((DateTime.UtcNow - gamepadMouseHoldStartedAt.Value).TotalMilliseconds >=
                GamepadMouseHoldDurationMs)
            {
                gamepadMouseShortcutHeld = true;
                TriggerGamepadMouseToggle();
            }

            return true;
        }

        private void ResetGamepadMouseShortcutState()
        {
            gamepadMouseShortcutHeld = false;
            gamepadMouseHoldStartedAt = null;
        }

        private void TriggerGamepadMouseToggle()
        {
            var now = DateTime.UtcNow;
            if ((now - lastGamepadMouseShortcutTime).TotalMilliseconds < GamepadMouseShortcutCooldownMs)
            {
                return;
            }

            lastGamepadMouseShortcutTime = now;

            try
            {
                DebugLog("[AnikiHelper][GamepadMouse][P10] Toggle shortcut detected.");
                onGamepadMouseToggle?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] P10 Gamepad Mouse shortcut callback failed.");
            }
        }

        private bool ProcessVirtualKeyboardShortcut()
        {
            if (isOverlayEnabled != null && !isOverlayEnabled())
            {
                ResetVirtualKeyboardShortcutState();
                return false;
            }

            var shortcut = settings?.InGameOverlayVirtualKeyboardShortcut ?? "L3R3Hold";
            if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                ResetVirtualKeyboardShortcutState();
                return false;
            }

            bool combinationHeld;
            bool requiresHold;

            switch (shortcut)
            {
                case "BackX":
                    combinationHeld = IsHeld(ControllerInput.Back) && IsHeld(ControllerInput.X);
                    requiresHold = false;
                    break;
                case "GuideX":
                    combinationHeld = IsHeld(ControllerInput.Guide) && IsHeld(ControllerInput.X);
                    requiresHold = false;
                    break;
                case "L3R3Hold":
                default:
                    combinationHeld = IsHeld(ControllerInput.LeftStick) && IsHeld(ControllerInput.RightStick);
                    requiresHold = true;
                    break;
            }

            if (!combinationHeld)
            {
                ResetVirtualKeyboardShortcutState();
                return false;
            }

            if (string.Equals(shortcut, "GuideX", StringComparison.OrdinalIgnoreCase))
            {
                guidePressedAt = null;
            }

            if (virtualKeyboardShortcutHeld)
            {
                return true;
            }

            if (!requiresHold)
            {
                virtualKeyboardShortcutHeld = true;
                TriggerVirtualKeyboardShortcut();
                return true;
            }

            if (!virtualKeyboardHoldStartedAt.HasValue)
            {
                virtualKeyboardHoldStartedAt = DateTime.UtcNow;
                return true;
            }

            if ((DateTime.UtcNow - virtualKeyboardHoldStartedAt.Value).TotalMilliseconds >=
                VirtualKeyboardHoldDurationMs)
            {
                virtualKeyboardShortcutHeld = true;
                TriggerVirtualKeyboardShortcut();
            }

            return true;
        }

        private void ResetVirtualKeyboardShortcutState()
        {
            virtualKeyboardShortcutHeld = false;
            virtualKeyboardHoldStartedAt = null;
        }

        private void TriggerVirtualKeyboardShortcut()
        {
            var now = DateTime.UtcNow;
            if ((now - lastVirtualKeyboardShortcutTime).TotalMilliseconds < VirtualKeyboardShortcutCooldownMs)
            {
                return;
            }

            lastVirtualKeyboardShortcutTime = now;

            try
            {
                DebugLog("[AnikiHelper][OverlayInput][P10] Virtual keyboard shortcut detected.");
                onVirtualKeyboardShortcutPressed?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] P10 virtual keyboard shortcut callback failed.");
            }
        }

        private bool ProcessOverlayShortcutEvent(ControllerInput button, ButtonTransition transition)
        {
            if (isOverlayEnabled != null && !isOverlayEnabled())
            {
                shortcutHeld = false;
                guidePressedAt = null;
                return false;
            }

            var shortcut = settings?.InGameOverlayControllerShortcut ?? "StartBack";
            if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                shortcutHeld = false;
                guidePressedAt = null;
                return false;
            }

            if (!IsSelectedOverlayChordHeld(shortcut))
            {
                shortcutHeld = false;
            }

            if (string.Equals(shortcut, "Guide", StringComparison.OrdinalIgnoreCase))
            {
                if (button != ControllerInput.Guide)
                {
                    return false;
                }

                if (transition.PressedNow)
                {
                    // Guide is also handled by Windows/Steam/Xbox at the system level.
                    // Opening our WPF overlay while the physical Guide press is still
                    // being processed can make Window.Show() stall for roughly a second.
                    // Record the press here and open on release instead. Shared Guide
                    // chords (Guide+X / Guide+Y) keep their existing 180 ms grace logic.
                    guidePressedAt = DateTime.UtcNow;
                    return false;
                }

                if (transition.ReleasedNow && guidePressedAt.HasValue)
                {
                    guidePressedAt = null;
                    shortcutHeld = true;
                    TriggerShortcut("Guide release");
                    return true;
                }

                return false;
            }

            if (!transition.PressedNow || shortcutHeld)
            {
                return false;
            }

            bool triggered;
            if (string.Equals(shortcut, "BackY", StringComparison.OrdinalIgnoreCase))
            {
                if (button != ControllerInput.Back && button != ControllerInput.Y)
                {
                    return false;
                }

                var directChord = IsHeld(ControllerInput.Back) && IsHeld(ControllerInput.Y);
                var graceChord = ArePressesWithinGrace(lastBackPressedTime, lastYPressedTime);
                triggered = directChord || graceChord;
            }
            else
            {
                if (button != ControllerInput.Start && button != ControllerInput.Back)
                {
                    return false;
                }

                var directChord = IsHeld(ControllerInput.Start) && IsHeld(ControllerInput.Back);
                var graceChord = ArePressesWithinGrace(lastStartPressedTime, lastBackPressedTime);
                triggered = directChord || graceChord;
            }

            if (!triggered)
            {
                return false;
            }

            shortcutHeld = true;
            TriggerShortcut(shortcut);
            return true;
        }

        private void ProcessGuideShortcutGrace()
        {
            if (!guidePressedAt.HasValue || shortcutHeld)
            {
                return;
            }

            if (isOverlayEnabled != null && !isOverlayEnabled())
            {
                guidePressedAt = null;
                return;
            }

            if (!string.Equals(
                settings?.InGameOverlayControllerShortcut,
                "Guide",
                StringComparison.OrdinalIgnoreCase))
            {
                guidePressedAt = null;
                return;
            }

            if (!IsHeld(ControllerInput.Guide))
            {
                return;
            }

            if (!IsGuideSharedWithAnotherShortcut())
            {
                return;
            }

            if ((DateTime.UtcNow - guidePressedAt.Value).TotalMilliseconds < GuideComboGraceMs)
            {
                return;
            }

            guidePressedAt = null;
            shortcutHeld = true;
            TriggerShortcut("Guide combo grace");
        }

        private bool IsGuideSharedWithAnotherShortcut()
        {
            return string.Equals(
                       settings?.InGameOverlayVirtualKeyboardShortcut,
                       "GuideX",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       settings?.InGameOverlayGamepadMouseShortcut,
                       "GuideY",
                       StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSelectedOverlayChordHeld(string shortcut)
        {
            if (string.Equals(shortcut, "BackY", StringComparison.OrdinalIgnoreCase))
            {
                return IsHeld(ControllerInput.Back) && IsHeld(ControllerInput.Y);
            }

            if (string.Equals(shortcut, "Guide", StringComparison.OrdinalIgnoreCase))
            {
                return IsHeld(ControllerInput.Guide);
            }

            return IsHeld(ControllerInput.Start) && IsHeld(ControllerInput.Back);
        }

        private void TriggerShortcut(string source)
        {
            var now = DateTime.UtcNow;
            if ((now - lastShortcutTime).TotalMilliseconds < 500)
            {
                return;
            }

            lastShortcutTime = now;

            try
            {
                DebugLog($"[AnikiHelper][OverlayInput][P10] Overlay shortcut detected. Source={source}.");
                onShortcutPressed?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] P10 controller overlay shortcut callback failed.");
            }
        }

        private static bool ArePressesWithinGrace(DateTime firstPress, DateTime secondPress)
        {
            if (firstPress == DateTime.MinValue || secondPress == DateTime.MinValue)
            {
                return false;
            }

            return Math.Abs((firstPress - secondPress).TotalMilliseconds) <= ShortcutChordGraceMs;
        }

        private static short SelectAxisWithGreatestMagnitude(short current, short candidate)
        {
            return Math.Abs((int)candidate) > Math.Abs((int)current)
                ? candidate
                : current;
        }

        private void ResetBrowserShortcutState()
        {
            browserBackPressPending = false;
            browserBackChordConsumed = false;
            browserShortcutSuppressionActive = false;
        }

        private void ResetTransientStateAfterTopologyChange()
        {
            shortcutHeld = false;
            guidePressedAt = null;
            ResetVirtualKeyboardShortcutState();
            ResetGamepadMouseShortcutState();
            ResetBrowserShortcutState();
            leftStickSoloClickCandidate = false;
            leftStickSoloPressedAt = DateTime.MinValue;
            onGamepadMouseSuspendInput?.Invoke();
        }

        private void ResetTransientState()
        {
            ResetTransientStateAfterTopologyChange();
            lastStartPressedTime = DateTime.MinValue;
            lastBackPressedTime = DateTime.MinValue;
            lastYPressedTime = DateTime.MinValue;
        }

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
                // Debug logging must never affect controller processing.
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
                // Debug logging must never affect controller processing.
            }
        }

        public void Dispose()
        {
            Stop();
        }

    }
}
