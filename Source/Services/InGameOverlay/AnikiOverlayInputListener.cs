using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK.Events;
using AnikiHelper.Services.WebBrowser;

namespace AnikiHelper.Services.InGameOverlay
{
    internal sealed class AnikiOverlayInputListener : IDisposable
    {
        private const uint SDL_INIT_JOYSTICK = 0x00000200;
        private const uint SDL_INIT_GAMECONTROLLER = 0x00002000;
        private const uint SDL_INIT_EVENTS = 0x00004000;

        private const int SDL_CONTROLLER_BUTTON_A = 0;
        private const int SDL_CONTROLLER_BUTTON_B = 1;
        private const int SDL_CONTROLLER_BUTTON_X = 2;
        private const int SDL_CONTROLLER_BUTTON_Y = 3;
        private const int SDL_CONTROLLER_BUTTON_BACK = 4;
        private const int SDL_CONTROLLER_BUTTON_GUIDE = 5;
        private const int SDL_CONTROLLER_BUTTON_START = 6;
        private const int SDL_CONTROLLER_BUTTON_LEFTSTICK = 7;
        private const int SDL_CONTROLLER_BUTTON_RIGHTSTICK = 8;
        private const int SDL_CONTROLLER_BUTTON_LEFTSHOULDER = 9;
        private const int SDL_CONTROLLER_BUTTON_RIGHTSHOULDER = 10;
        private const int SDL_CONTROLLER_BUTTON_DPAD_UP = 11;
        private const int SDL_CONTROLLER_BUTTON_DPAD_DOWN = 12;
        private const int SDL_CONTROLLER_BUTTON_DPAD_LEFT = 13;
        private const int SDL_CONTROLLER_BUTTON_DPAD_RIGHT = 14;

        private const int SDL_CONTROLLER_AXIS_LEFTX = 0;
        private const int SDL_CONTROLLER_AXIS_LEFTY = 1;
        private const int SDL_CONTROLLER_AXIS_RIGHTX = 2;
        private const int SDL_CONTROLLER_AXIS_RIGHTY = 3;
        private const int SDL_CONTROLLER_AXIS_TRIGGERLEFT = 4;
        private const int SDL_CONTROLLER_AXIS_TRIGGERRIGHT = 5;

        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;

        private void DebugLog(string message)
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

        private void DebugLog(Exception exception, string message)
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
        private readonly Action onShortcutPressed;
        private readonly Action onVirtualKeyboardShortcutPressed;
        private readonly Action onGamepadMouseToggle;
        private readonly Func<bool> isGamepadMouseActive;
        private readonly Action<GamepadMouseInputState> onGamepadMouseInput;
        private readonly Action onGamepadMouseSuspendInput;
        private readonly Func<bool> isOverlayEnabled;
        private readonly Func<bool> isOverlayVisible;
        private readonly Action<ControllerInput> onOverlayButtonPressed;
        private readonly Func<bool> isWebBrowserActive;
        private readonly Action<WebBrowserGamepadInputState> onWebBrowserInput;

        private readonly object syncRoot = new object();
        private readonly List<IntPtr> controllers = new List<IntPtr>();

        private CancellationTokenSource cancellationTokenSource;
        private Task pollingTask;
        private bool isStarted;
        private bool sdlAvailable;

        private bool previousGuide;
        private bool previousStart;
        private bool previousBack;
        private bool previousY;
        private bool previousX;
        private bool previousA;
        private bool previousB;
        private bool previousLeftShoulder;
        private bool previousRightShoulder;
        private bool previousDPadUp;
        private bool previousDPadDown;
        private bool previousDPadLeft;
        private bool previousDPadRight;
        private bool shortcutHeld;
        private bool virtualKeyboardShortcutHeld;
        private bool gamepadMouseShortcutHeld;
        private bool browserBackPressPending;
        private bool browserBackChordConsumed;
        private bool browserShortcutSuppressionActive;
        private DateTime? virtualKeyboardHoldStartedAt;
        private DateTime? gamepadMouseHoldStartedAt;
        private DateTime lastShortcutTime = DateTime.MinValue;
        private DateTime lastStartPressedTime = DateTime.MinValue;
        private DateTime lastBackPressedTime = DateTime.MinValue;
        private DateTime lastYPressedTime = DateTime.MinValue;
        private DateTime lastVirtualKeyboardShortcutTime = DateTime.MinValue;
        private DateTime lastGamepadMouseShortcutTime = DateTime.MinValue;
        private DateTime lastRefreshTime = DateTime.MinValue;
        private int lastKnownJoystickCount = -1;
        private DateTime? guidePressedAt;
        private const int GuideComboGraceMs = 180;
        private const int ShortcutChordGraceMs = 400;
        private const int VirtualKeyboardHoldDurationMs = 600;
        private const int VirtualKeyboardShortcutCooldownMs = 700;
        private const int GamepadMouseHoldDurationMs = 600;
        private const int GamepadMouseShortcutCooldownMs = 800;

        public AnikiOverlayInputListener(
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
            Func<bool> isWebBrowserActive,
            Action<WebBrowserGamepadInputState> onWebBrowserInput)
        {
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
            cancellationTokenSource = new CancellationTokenSource();

            pollingTask = Task.Factory.StartNew(
                () => PollLoop(cancellationTokenSource.Token),
                cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public void Stop()
        {
            if (!isStarted)
            {
                return;
            }

            isStarted = false;

            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch
            {
            }

            try
            {
                pollingTask?.Wait(400);
            }
            catch
            {
            }

            pollingTask = null;

            try
            {
                cancellationTokenSource?.Dispose();
            }
            catch
            {
            }

            cancellationTokenSource = null;

            CloseControllers();

            if (sdlAvailable)
            {
                try
                {
                    SDL_QuitSubSystem(SDL_INIT_GAMECONTROLLER | SDL_INIT_JOYSTICK | SDL_INIT_EVENTS);
                }
                catch
                {
                }
            }

            sdlAvailable = false;
            lastKnownJoystickCount = -1;
        }

        private void PollLoop(CancellationToken token)
        {
            try
            {
                try
                {
                    var result = SDL_InitSubSystem(SDL_INIT_GAMECONTROLLER | SDL_INIT_JOYSTICK | SDL_INIT_EVENTS);
                    sdlAvailable = result == 0;

                    if (!sdlAvailable)
                    {
                        logger?.Warn("[AnikiHelper] SDL controller input listener could not initialize.");
                        return;
                    }

                }
                catch (DllNotFoundException ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] SDL2.dll not found. Controller overlay shortcut will keep using Playnite events only.");
                    return;
                }
                catch (EntryPointNotFoundException ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] SDL entry point missing. Controller overlay shortcut will keep using Playnite events only.");
                    return;
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper] SDL controller input listener failed to initialize.");
                    return;
                }

                RefreshControllers(force: true);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Keep a very light hot-plug safety check, but do not close and reopen
                        // stable controller handles every three seconds. A full refresh now only
                        // happens when SDL reports a topology change or a tracked controller is
                        // actually detached.
                        if ((DateTime.UtcNow - lastRefreshTime).TotalSeconds >= 3)
                        {
                            RefreshControllers();
                        }

                        SDL_GameControllerUpdate();
                        ReadControllers();
                    }
                    catch
                    {
                    }

                    Thread.Sleep(16);
                }
            }
            finally
            {
                CloseControllers();
            }
        }

        private void RefreshControllers(bool force = false)
        {
            lastRefreshTime = DateTime.UtcNow;

            try
            {
                var joystickCount = SDL_NumJoysticks();
                if (joystickCount < 0)
                {
                    return;
                }

                var hasDetachedController = false;

                lock (syncRoot)
                {
                    foreach (var controller in controllers)
                    {
                        if (controller == IntPtr.Zero || SDL_GameControllerGetAttached(controller) == 0)
                        {
                            hasDetachedController = true;
                            break;
                        }
                    }
                }

                if (!force &&
                    joystickCount == lastKnownJoystickCount &&
                    !hasDetachedController)
                {
                    return;
                }

                DebugLog(
                    $"[AnikiHelper][OverlayInput] Controller topology changed. " +
                    $"Force={force}, PreviousJoysticks={lastKnownJoystickCount}, " +
                    $"CurrentJoysticks={joystickCount}, Detached={hasDetachedController}");

                // Rebuild handles only after a real connection/disconnection change.
                // Under normal gameplay the same SDL handles remain open for the whole session.
                CloseControllers();

                var detectedGameControllers = 0;

                lock (syncRoot)
                {
                    for (var i = 0; i < joystickCount; i++)
                    {
                        if (SDL_IsGameController(i) != 1)
                        {
                            continue;
                        }

                        detectedGameControllers++;

                        var controller = SDL_GameControllerOpen(i);
                        if (controller != IntPtr.Zero)
                        {
                            controllers.Add(controller);
                        }
                    }

                    // If SDL detected compatible controllers but one failed to open, retry on the
                    // next safety scan instead of considering the topology permanently valid.
                    lastKnownJoystickCount = controllers.Count < detectedGameControllers
                        ? -1
                        : joystickCount;

                    DebugLog(
                        $"[AnikiHelper][OverlayInput] Controller handles refreshed. " +
                        $"DetectedControllers={detectedGameControllers}, " +
                        $"OpenedControllers={controllers.Count}");
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][OverlayInput] Controller topology refresh failed.");
            }
        }

        private void CloseControllers()
        {
            lock (syncRoot)
            {
                foreach (var controller in controllers)
                {
                    try
                    {
                        if (controller != IntPtr.Zero)
                        {
                            SDL_GameControllerClose(controller);
                        }
                    }
                    catch
                    {
                    }
                }

                controllers.Clear();
            }
        }

        private void ReadControllers()
        {
            if (controllers.Count == 0)
            {
                previousGuide = false;
                previousStart = false;
                previousBack = false;
                previousY = false;
                previousX = false;
                previousA = false;
                previousB = false;
                previousLeftShoulder = false;
                previousRightShoulder = false;
                previousDPadUp = false;
                previousDPadDown = false;
                previousDPadLeft = false;
                previousDPadRight = false;
                shortcutHeld = false;
                virtualKeyboardShortcutHeld = false;
                virtualKeyboardHoldStartedAt = null;
                gamepadMouseShortcutHeld = false;
                gamepadMouseHoldStartedAt = null;
                ResetBrowserShortcutState();
                onGamepadMouseSuspendInput?.Invoke();

                if (isWebBrowserActive?.Invoke() == true)
                {
                    onWebBrowserInput?.Invoke(new WebBrowserGamepadInputState());
                }

                return;
            }

            var guide = false;
            var start = false;
            var back = false;
            var y = false;
            var x = false;
            var a = false;
            var b = false;
            var leftShoulder = false;
            var rightShoulder = false;
            var dpadUp = false;
            var dpadDown = false;
            var dpadLeft = false;
            var dpadRight = false;
            var leftStick = false;
            var rightStick = false;
            short leftAxisX = 0;
            short leftAxisY = 0;
            short rightAxisX = 0;
            short rightAxisY = 0;
            short leftTrigger = 0;
            short rightTrigger = 0;

            lock (syncRoot)
            {
                foreach (var controller in controllers)
                {
                    if (controller == IntPtr.Zero)
                    {
                        continue;
                    }

                    guide = guide || IsPressed(controller, SDL_CONTROLLER_BUTTON_GUIDE);
                    start = start || IsPressed(controller, SDL_CONTROLLER_BUTTON_START);
                    back = back || IsPressed(controller, SDL_CONTROLLER_BUTTON_BACK);
                    y = y || IsPressed(controller, SDL_CONTROLLER_BUTTON_Y);
                    x = x || IsPressed(controller, SDL_CONTROLLER_BUTTON_X);
                    leftStick = leftStick || IsPressed(controller, SDL_CONTROLLER_BUTTON_LEFTSTICK);
                    rightStick = rightStick || IsPressed(controller, SDL_CONTROLLER_BUTTON_RIGHTSTICK);

                    a = a || IsPressed(controller, SDL_CONTROLLER_BUTTON_A);
                    b = b || IsPressed(controller, SDL_CONTROLLER_BUTTON_B);
                    leftShoulder = leftShoulder || IsPressed(controller, SDL_CONTROLLER_BUTTON_LEFTSHOULDER);
                    rightShoulder = rightShoulder || IsPressed(controller, SDL_CONTROLLER_BUTTON_RIGHTSHOULDER);
                    dpadUp = dpadUp || IsPressed(controller, SDL_CONTROLLER_BUTTON_DPAD_UP);
                    dpadDown = dpadDown || IsPressed(controller, SDL_CONTROLLER_BUTTON_DPAD_DOWN);
                    dpadLeft = dpadLeft || IsPressed(controller, SDL_CONTROLLER_BUTTON_DPAD_LEFT);
                    dpadRight = dpadRight || IsPressed(controller, SDL_CONTROLLER_BUTTON_DPAD_RIGHT);

                    leftAxisX = SelectAxisWithGreatestMagnitude(
                        leftAxisX,
                        ReadAxis(controller, SDL_CONTROLLER_AXIS_LEFTX));
                    leftAxisY = SelectAxisWithGreatestMagnitude(
                        leftAxisY,
                        ReadAxis(controller, SDL_CONTROLLER_AXIS_LEFTY));
                    rightAxisX = SelectAxisWithGreatestMagnitude(
                        rightAxisX,
                        ReadAxis(controller, SDL_CONTROLLER_AXIS_RIGHTX));
                    rightAxisY = SelectAxisWithGreatestMagnitude(
                        rightAxisY,
                        ReadAxis(controller, SDL_CONTROLLER_AXIS_RIGHTY));
                    leftTrigger = Math.Max(leftTrigger, ReadAxis(controller, SDL_CONTROLLER_AXIS_TRIGGERLEFT));
                    rightTrigger = Math.Max(rightTrigger, ReadAxis(controller, SDL_CONTROLLER_AXIS_TRIGGERRIGHT));
                }
            }

            var guidePressedNow = guide && !previousGuide;
            var guideReleasedNow = !guide && previousGuide;
            var startPressedNow = start && !previousStart;
            var backPressedNow = back && !previousBack;
            var backReleasedNow = !back && previousBack;
            var yPressedNow = y && !previousY;
            var xPressedNow = x && !previousX;

            var aPressedNow = a && !previousA;
            var bPressedNow = b && !previousB;
            var leftShoulderPressedNow = leftShoulder && !previousLeftShoulder;
            var rightShoulderPressedNow = rightShoulder && !previousRightShoulder;
            var dpadUpPressedNow = dpadUp && !previousDPadUp;
            var dpadDownPressedNow = dpadDown && !previousDPadDown;
            var dpadLeftPressedNow = dpadLeft && !previousDPadLeft;
            var dpadRightPressedNow = dpadRight && !previousDPadRight;

            var buttonTimestamp = DateTime.UtcNow;
            if (startPressedNow)
            {
                lastStartPressedTime = buttonTimestamp;
            }

            if (backPressedNow)
            {
                lastBackPressedTime = buttonTimestamp;
            }

            if (yPressedNow)
            {
                lastYPressedTime = buttonTimestamp;
            }

            previousGuide = guide;
            previousStart = start;
            previousBack = back;
            previousY = y;
            previousX = x;
            previousA = a;
            previousB = b;
            previousLeftShoulder = leftShoulder;
            previousRightShoulder = rightShoulder;
            previousDPadUp = dpadUp;
            previousDPadDown = dpadDown;
            previousDPadLeft = dpadLeft;
            previousDPadRight = dpadRight;

            if (isOverlayVisible != null && isOverlayVisible())
            {
                shortcutHeld = false;
                onGamepadMouseSuspendInput?.Invoke();

                if (dpadLeftPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL DPadLeft pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.DPadLeft);
                    return;
                }

                if (dpadRightPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL DPadRight pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.DPadRight);
                    return;
                }

                if (dpadUpPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL DPadUp pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.DPadUp);
                    return;
                }

                if (dpadDownPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL DPadDown pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.DPadDown);
                    return;
                }

                if (aPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL A pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.A);
                    return;
                }

                if (xPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL X pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.X);
                    return;
                }

                if (yPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL Y pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.Y);
                    return;
                }

                if (startPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL Start pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.Start);
                    return;
                }

                if (bPressedNow || backPressedNow)
                {
                    DebugLog("[AnikiHelper][OverlayInput] SDL B/Back pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.B);
                    return;
                }

                return;
            }

            if (isWebBrowserActive?.Invoke() == true)
            {
                shortcutHeld = false;
                virtualKeyboardShortcutHeld = false;
                gamepadMouseShortcutHeld = false;
                virtualKeyboardHoldStartedAt = null;
                gamepadMouseHoldStartedAt = null;
                onGamepadMouseSuspendInput?.Invoke();

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

                if (backPressedNow)
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

                var closePressedNow = backReleasedNow &&
                                      browserBackPressPending &&
                                      !browserBackChordConsumed;

                if (backReleasedNow)
                {
                    browserBackPressPending = false;
                    browserBackChordConsumed = false;
                }

                // A browser shortcut must not accidentally trigger its individual browser
                // action. In particular, Back + R3 must neither close the browser nor toggle
                // the global gamepad mouse mode after the browser window disappears.
                var suppressKeyboardButton = x && (back || guide);
                var suppressAddressButton = y && (back || guide);
                var suppressEnterButton = start && (back || leftStick);

                onWebBrowserInput?.Invoke(new WebBrowserGamepadInputState
                {
                    LeftX = leftAxisX,
                    LeftY = leftAxisY,
                    RightX = rightAxisX,
                    RightY = rightAxisY,
                    LeftClick = a,
                    ActivatePressed = aPressedNow,
                    BackPressed = bPressedNow,
                    ClosePressed = closePressedNow,
                    KeyboardPressed = xPressedNow && !suppressKeyboardButton,
                    AddressPressed = yPressedNow && !suppressAddressButton,
                    EnterPressed = startPressedNow && !suppressEnterButton,
                    PreviousPressed = leftShoulderPressedNow,
                    NextPressed = rightShoulderPressedNow,
                    DPadUpPressed = dpadUpPressedNow,
                    DPadDownPressed = dpadDownPressedNow,
                    DPadLeftPressed = dpadLeftPressedNow,
                    DPadRightPressed = dpadRightPressedNow
                });

                return;
            }

            if (browserShortcutSuppressionActive)
            {
                // The browser may have been closed while a global mouse/keyboard chord was
                // still physically held. Wait for every chord button to be released so the
                // same hold cannot immediately toggle another Aniki Helper feature.
                if (guide || start || back || y || x || leftStick || rightStick)
                {
                    shortcutHeld = false;
                    ResetVirtualKeyboardShortcutState();
                    ResetGamepadMouseShortcutState();
                    onGamepadMouseSuspendInput?.Invoke();
                    return;
                }

                browserShortcutSuppressionActive = false;
                ResetBrowserShortcutState();
            }
            else
            {
                browserBackPressPending = false;
                browserBackChordConsumed = false;
            }

            if (HandleGamepadMouseShortcut(
                guide,
                start,
                back,
                y,
                leftStick,
                rightStick))
            {
                onGamepadMouseSuspendInput?.Invoke();
                return;
            }

            if (HandleVirtualKeyboardShortcut(
                guide,
                back,
                x,
                leftStick,
                rightStick,
                guidePressedNow,
                backPressedNow,
                xPressedNow))
            {
                onGamepadMouseSuspendInput?.Invoke();
                return;
            }

            if (isGamepadMouseActive?.Invoke() == true)
            {
                onGamepadMouseInput?.Invoke(new GamepadMouseInputState
                {
                    RightX = rightAxisX,
                    RightY = rightAxisY,
                    LeftY = leftAxisY,
                    LeftTrigger = leftTrigger,
                    RightTrigger = rightTrigger,
                    LeftClick = a,
                    RightClick = x
                });
            }

            var shortcut = settings?.InGameOverlayControllerShortcut ?? "StartBack";

            // Release the latch as soon as the selected chord is no longer physically held.
            // Previously it was reset only when Guide, Start, Back and Y were all released in
            // the same polling frame. A single button reported as lingering by a game/driver
            // could therefore make several following shortcut attempts appear to be ignored.
            var selectedChordHeld = string.Equals(shortcut, "BackY", StringComparison.OrdinalIgnoreCase)
                ? back && y
                : string.Equals(shortcut, "StartBack", StringComparison.OrdinalIgnoreCase)
                    ? start && back
                    : guide;

            if (!selectedChordHeld)
            {
                shortcutHeld = false;
            }

            if (!guide && !start && !back && !y && !guideReleasedNow)
            {
                return;
            }

            if (shortcutHeld)
            {
                return;
            }

            if (string.Equals(shortcut, "Guide", StringComparison.OrdinalIgnoreCase))
            {
                var virtualKeyboardUsesGuideX = string.Equals(
                    settings?.InGameOverlayVirtualKeyboardShortcut,
                    "GuideX",
                    StringComparison.OrdinalIgnoreCase);
                var gamepadMouseUsesGuideY = string.Equals(
                    settings?.InGameOverlayGamepadMouseShortcut,
                    "GuideY",
                    StringComparison.OrdinalIgnoreCase);
                var guideIsSharedWithCombo = virtualKeyboardUsesGuideX || gamepadMouseUsesGuideY;

                if (guidePressedNow)
                {
                    guidePressedAt = DateTime.UtcNow;
                    DebugLog("[AnikiHelper][OverlayInput] SDL Guide pressed.");

                    // When Guide is not shared with the virtual-keyboard shortcut, react on
                    // the press edge instead of waiting for the release. The Guide release
                    // state can be reported late or missed by some games/input layers.
                    if (!guideIsSharedWithCombo)
                    {
                        guidePressedAt = null;
                        shortcutHeld = true;
                        DebugLog("[AnikiHelper][OverlayInput] Guide shortcut detected on press.");
                        TriggerShortcut();
                    }

                    return;
                }

                if (guideIsSharedWithCombo && guide && guidePressedAt.HasValue)
                {
                    var heldMs = (DateTime.UtcNow - guidePressedAt.Value).TotalMilliseconds;

                    // Give Guide+X a short window to complete. If X never arrives, open the
                    // overlay while Guide is still held instead of relying solely on release.
                    if (heldMs >= GuideComboGraceMs)
                    {
                        guidePressedAt = null;
                        shortcutHeld = true;
                        DebugLog($"[AnikiHelper][OverlayInput] Guide shortcut detected after combo grace. HeldMs={heldMs:0}");
                        TriggerShortcut();
                    }

                    return;
                }

                if (guideReleasedNow && guidePressedAt.HasValue)
                {
                    var heldMs = (DateTime.UtcNow - guidePressedAt.Value).TotalMilliseconds;
                    guidePressedAt = null;
                    shortcutHeld = true;
                    DebugLog($"[AnikiHelper][OverlayInput] Guide shortcut detected on release. HeldMs={heldMs:0}");
                    TriggerShortcut();
                    return;
                }

                return;
            }

            if (IsShortcutTriggered(guide, start, back, y, guidePressedNow, startPressedNow, backPressedNow, yPressedNow))
            {
                shortcutHeld = true;
                TriggerShortcut();
            }
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

            // Also reserve the controller combinations used by the main overlay shortcut.
            // They must never be interpreted as a direct browser-close press.
            var overlayShortcut = settings?.InGameOverlayControllerShortcut ?? "StartBack";
            var overlayChordHeld =
                string.Equals(overlayShortcut, "BackY", StringComparison.OrdinalIgnoreCase)
                    ? back && y
                    : string.Equals(overlayShortcut, "StartBack", StringComparison.OrdinalIgnoreCase)
                        ? start && back
                        : string.Equals(overlayShortcut, "Guide", StringComparison.OrdinalIgnoreCase) && guide;

            return mouseChordHeld || keyboardChordHeld || overlayChordHeld;
        }

        private void ResetBrowserShortcutState()
        {
            browserBackPressPending = false;
            browserBackChordConsumed = false;
            browserShortcutSuppressionActive = false;
        }

        private bool HandleGamepadMouseShortcut(
            bool guide,
            bool start,
            bool back,
            bool y,
            bool leftStick,
            bool rightStick)
        {
            var shortcut = settings?.InGameOverlayGamepadMouseShortcut ?? "BackR3";

            if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                ResetGamepadMouseShortcutState();

                if (isGamepadMouseActive?.Invoke() == true)
                {
                    onGamepadMouseToggle?.Invoke();
                }

                return false;
            }

            bool combinationHeld;

            switch (shortcut)
            {
                case "StartL3":
                    combinationHeld = start && leftStick;
                    break;

                case "GuideY":
                    combinationHeld = guide && y;
                    break;

                case "BackR3":
                default:
                    combinationHeld = back && rightStick;
                    break;
            }

            if (!combinationHeld)
            {
                ResetGamepadMouseShortcutState();
                return false;
            }

            if (string.Equals(shortcut, "GuideY", StringComparison.OrdinalIgnoreCase))
            {
                // Guide belongs to this chord now. Prevent the normal Guide overlay
                // action from firing when the buttons are released.
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

            if ((now - lastGamepadMouseShortcutTime).TotalMilliseconds <
                GamepadMouseShortcutCooldownMs)
            {
                return;
            }

            lastGamepadMouseShortcutTime = now;

            try
            {
                DebugLog("[AnikiHelper][GamepadMouse] Toggle shortcut detected.");
                onGamepadMouseToggle?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] SDL Gamepad Mouse shortcut callback failed.");
            }
        }

        private bool HandleVirtualKeyboardShortcut(
            bool guide,
            bool back,
            bool x,
            bool leftStick,
            bool rightStick,
            bool guidePressedNow,
            bool backPressedNow,
            bool xPressedNow)
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
            bool combinationPressedNow;

            switch (shortcut)
            {
                case "BackX":
                    combinationHeld = back && x;
                    combinationPressedNow = combinationHeld && (backPressedNow || xPressedNow);

                    if (!combinationHeld)
                    {
                        virtualKeyboardShortcutHeld = false;
                    }

                    virtualKeyboardHoldStartedAt = null;

                    if (combinationPressedNow && !virtualKeyboardShortcutHeld)
                    {
                        virtualKeyboardShortcutHeld = true;
                        TriggerVirtualKeyboardShortcut();
                        return true;
                    }

                    return combinationHeld;

                case "GuideX":
                    combinationHeld = guide && x;
                    combinationPressedNow = combinationHeld && (guidePressedNow || xPressedNow);

                    if (!combinationHeld)
                    {
                        virtualKeyboardShortcutHeld = false;
                    }

                    virtualKeyboardHoldStartedAt = null;

                    if (combinationPressedNow && !virtualKeyboardShortcutHeld)
                    {
                        // Prevent the normal short-Guide overlay shortcut from firing
                        // when Guide is released after Guide + X opened the keyboard.
                        guidePressedAt = null;
                        virtualKeyboardShortcutHeld = true;
                        TriggerVirtualKeyboardShortcut();
                        return true;
                    }

                    return combinationHeld;

                case "L3R3Hold":
                default:
                    combinationHeld = leftStick && rightStick;

                    if (!combinationHeld)
                    {
                        virtualKeyboardShortcutHeld = false;
                        virtualKeyboardHoldStartedAt = null;
                        return false;
                    }

                    if (virtualKeyboardShortcutHeld)
                    {
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
        }

        private void ResetVirtualKeyboardShortcutState()
        {
            virtualKeyboardShortcutHeld = false;
            virtualKeyboardHoldStartedAt = null;
        }

        private void TriggerVirtualKeyboardShortcut()
        {
            var now = DateTime.UtcNow;

            if ((now - lastVirtualKeyboardShortcutTime).TotalMilliseconds <
                VirtualKeyboardShortcutCooldownMs)
            {
                return;
            }

            lastVirtualKeyboardShortcutTime = now;

            try
            {
                onVirtualKeyboardShortcutPressed?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] SDL virtual keyboard shortcut callback failed.");
            }
        }

        private short ReadAxis(IntPtr controller, int axis)
        {
            try
            {
                return SDL_GameControllerGetAxis(controller, axis);
            }
            catch
            {
                return 0;
            }
        }

        private static short SelectAxisWithGreatestMagnitude(short current, short candidate)
        {
            return Math.Abs((int)candidate) > Math.Abs((int)current)
                ? candidate
                : current;
        }

        private bool IsPressed(IntPtr controller, int button)
        {
            try
            {
                return SDL_GameControllerGetButton(controller, button) != 0;
            }
            catch
            {
                return false;
            }
        }

        private bool IsShortcutTriggered(
            bool guide,
            bool start,
            bool back,
            bool y,
            bool guidePressedNow,
            bool startPressedNow,
            bool backPressedNow,
            bool yPressedNow)
        {
            if (isOverlayEnabled != null && !isOverlayEnabled())
            {
                return false;
            }

            var shortcut = settings?.InGameOverlayControllerShortcut ?? "StartBack";

            if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            switch (shortcut)
            {
                case "Guide":
                    return false;

                case "BackY":
                {
                    var directChord = back && y && (backPressedNow || yPressedNow);
                    var graceChord = (backPressedNow || yPressedNow) &&
                                     ArePressesWithinGrace(lastBackPressedTime, lastYPressedTime);

                    if (directChord || graceChord)
                    {
                        DebugLog(
                            $"[AnikiHelper][OverlayInput] Back+Y shortcut detected. " +
                            $"Direct={directChord}, Grace={graceChord}, Back={back}, Y={y}");
                    }

                    return directChord || graceChord;
                }

                case "StartBack":
                default:
                {
                    var directChord = start && back && (startPressedNow || backPressedNow);
                    var graceChord = (startPressedNow || backPressedNow) &&
                                     ArePressesWithinGrace(lastStartPressedTime, lastBackPressedTime);

                    if (directChord || graceChord)
                    {
                        DebugLog(
                            $"[AnikiHelper][OverlayInput] Start+Back shortcut detected. " +
                            $"Direct={directChord}, Grace={graceChord}, Start={start}, Back={back}");
                    }

                    return directChord || graceChord;
                }
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

        private void TriggerShortcut()
        {
            var now = DateTime.Now;

            if ((now - lastShortcutTime).TotalMilliseconds < 500)
            {
                return;
            }

            lastShortcutTime = now;
            

            try
            {
                onShortcutPressed?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] SDL controller shortcut callback failed.");
            }
        }

        public void Dispose()
        {
            Stop();
        }

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_InitSubSystem(uint flags);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_QuitSubSystem(uint flags);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_NumJoysticks();

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_IsGameController(int joystickIndex);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GameControllerOpen(int joystickIndex);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_GameControllerClose(IntPtr gamecontroller);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_GameControllerGetAttached(IntPtr gamecontroller);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_GameControllerUpdate();

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte SDL_GameControllerGetButton(IntPtr gamecontroller, int button);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern short SDL_GameControllerGetAxis(IntPtr gamecontroller, int axis);
    }
}
