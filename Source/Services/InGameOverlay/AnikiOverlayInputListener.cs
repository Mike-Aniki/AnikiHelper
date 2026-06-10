using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK.Events;

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
        private const int SDL_CONTROLLER_BUTTON_DPAD_UP = 11;
        private const int SDL_CONTROLLER_BUTTON_DPAD_DOWN = 12;

        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;
        private readonly Action onShortcutPressed;
        private readonly Func<bool> isOverlayEnabled;
        private readonly Func<bool> isOverlayVisible;
        private readonly Action<ControllerInput> onOverlayButtonPressed;

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
        private bool previousA;
        private bool previousB;
        private bool previousDPadUp;
        private bool previousDPadDown;
        private bool shortcutHeld;
        private DateTime lastShortcutTime = DateTime.MinValue;
        private DateTime lastRefreshTime = DateTime.MinValue;

        public AnikiOverlayInputListener(
            AnikiHelperSettings settings,
            ILogger logger,
            Action onShortcutPressed,
            Func<bool> isOverlayEnabled,
            Func<bool> isOverlayVisible,
            Action<ControllerInput> onOverlayButtonPressed)
        {
            this.settings = settings;
            this.logger = logger;
            this.onShortcutPressed = onShortcutPressed;
            this.isOverlayEnabled = isOverlayEnabled;
            this.isOverlayVisible = isOverlayVisible;
            this.onOverlayButtonPressed = onOverlayButtonPressed;
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

                RefreshControllers();

                while (!token.IsCancellationRequested)
                {
                    try
                    {
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

        private void RefreshControllers()
        {
            lastRefreshTime = DateTime.UtcNow;

            CloseControllers();

            try
            {
                var count = SDL_NumJoysticks();

                for (var i = 0; i < count; i++)
                {
                    if (SDL_IsGameController(i) != 1)
                    {
                        continue;
                    }

                    var controller = SDL_GameControllerOpen(i);
                    if (controller != IntPtr.Zero)
                    {
                        controllers.Add(controller);
                    }
                }

              
            }
            catch
            {
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
                previousA = false;
                previousB = false;
                previousDPadUp = false;
                previousDPadDown = false;
                shortcutHeld = false;
                return;
            }

            var guide = false;
            var start = false;
            var back = false;
            var y = false;
            var a = false;
            var b = false;
            var dpadUp = false;
            var dpadDown = false;

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

                    a = a || IsPressed(controller, SDL_CONTROLLER_BUTTON_A);
                    b = b || IsPressed(controller, SDL_CONTROLLER_BUTTON_B);
                    dpadUp = dpadUp || IsPressed(controller, SDL_CONTROLLER_BUTTON_DPAD_UP);
                    dpadDown = dpadDown || IsPressed(controller, SDL_CONTROLLER_BUTTON_DPAD_DOWN);
                }
            }

            var guidePressedNow = guide && !previousGuide;
            var startPressedNow = start && !previousStart;
            var backPressedNow = back && !previousBack;
            var yPressedNow = y && !previousY;

            var aPressedNow = a && !previousA;
            var bPressedNow = b && !previousB;
            var dpadUpPressedNow = dpadUp && !previousDPadUp;
            var dpadDownPressedNow = dpadDown && !previousDPadDown;

            previousGuide = guide;
            previousStart = start;
            previousBack = back;
            previousY = y;
            previousA = a;
            previousB = b;
            previousDPadUp = dpadUp;
            previousDPadDown = dpadDown;

            if (isOverlayVisible != null && isOverlayVisible())
            {
                shortcutHeld = false;

                if (dpadUpPressedNow)
                {
                    logger?.Debug("[AnikiHelper][OverlayInput] SDL DPadUp pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.DPadUp);
                    return;
                }

                if (dpadDownPressedNow)
                {
                    logger?.Debug("[AnikiHelper][OverlayInput] SDL DPadDown pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.DPadDown);
                    return;
                }

                if (aPressedNow)
                {
                    logger?.Debug("[AnikiHelper][OverlayInput] SDL A pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.A);
                    return;
                }

                if (bPressedNow || backPressedNow)
                {
                    logger?.Debug("[AnikiHelper][OverlayInput] SDL B/Back pressed.");
                    onOverlayButtonPressed?.Invoke(ControllerInput.B);
                    return;
                }

                return;
            }

            if (!guide && !start && !back && !y)
            {
                shortcutHeld = false;
                return;
            }

            if (shortcutHeld)
            {
                return;
            }

            if (IsShortcutTriggered(guide, start, back, y, guidePressedNow, startPressedNow, backPressedNow, yPressedNow))
            {
                shortcutHeld = true;
                TriggerShortcut();
            }
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
                    return guidePressedNow;

                case "BackY":
                    return back && y && (backPressedNow || yPressedNow);

                case "StartBack":
                default:
                    return start && back && (startPressedNow || backPressedNow);
            }
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
        private static extern void SDL_GameControllerUpdate();

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte SDL_GameControllerGetButton(IntPtr gamecontroller, int button);
    }
}
