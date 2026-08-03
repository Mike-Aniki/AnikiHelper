using Playnite.SDK;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace AnikiHelper.Services.InGameOverlay
{
    internal struct GamepadMouseInputState
    {
        public short RightX;
        public short RightY;
        public short LeftY;
        public short LeftTrigger;
        public short RightTrigger;
        public bool LeftClick;
        public bool RightClick;
    }

    internal sealed class GamepadMouseService : IDisposable
    {
        private const int AxisDeadZone = 7500;
        private const double MaximumCursorPixelsPerFrame = 20.0;
        private const double PreciseWheelUnitsPerFrame = 24.0;
        private const double FastWheelUnitsPerFrame = 84.0;

        private readonly ILogger logger;
        private readonly object stateLock = new object();
        private readonly Input[] inputBuffer = new Input[1];

        private bool isActive;
        private bool leftButtonDown;
        private bool rightButtonDown;
        private bool ignoreClickButtonsUntilReleased;
        private double wheelAccumulator;
        private DateTime lastSendInputFailureLogUtc = DateTime.MinValue;
        private int toastGeneration;
        private GamepadMouseToastWindow toastWindow;

        public GamepadMouseService(ILogger logger)
        {
            this.logger = logger;
        }

        public bool IsActive
        {
            get
            {
                lock (stateLock)
                {
                    return isActive;
                }
            }
        }

        public void Toggle()
        {
            bool enabled;

            lock (stateLock)
            {
                if (isActive)
                {
                    DeactivateCore();
                    enabled = false;
                }
                else
                {
                    isActive = true;
                    leftButtonDown = false;
                    rightButtonDown = false;
                    ignoreClickButtonsUntilReleased = true;
                    wheelAccumulator = 0;
                    enabled = true;
                }
            }

            DebugLog($"[AnikiHelper][GamepadMouse] Mode {(enabled ? "enabled" : "disabled")}.");
            if (enabled)
            {
                ShowToast("LOCGamepadMouseEnabledToast", "Gamepad Mouse enabled");
            }
            else
            {
                ShowToast("LOCGamepadMouseDisabledToast", "Gamepad Mouse disabled");
            }
        }

        public void ProcessInput(GamepadMouseInputState state)
        {
            lock (stateLock)
            {
                if (!isActive)
                {
                    return;
                }

                var normalizedX = NormalizeAxis(state.RightX);
                var normalizedY = NormalizeAxis(state.RightY);

                var deltaX = CalculateCursorDelta(normalizedX);
                var deltaY = CalculateCursorDelta(normalizedY);

                if (deltaX != 0 || deltaY != 0)
                {
                    SendMouseInput(deltaX, deltaY, 0, MouseEventMove);
                }

                if (ignoreClickButtonsUntilReleased)
                {
                    if (!state.LeftClick && !state.RightClick)
                    {
                        ignoreClickButtonsUntilReleased = false;
                    }
                }
                else
                {
                    UpdateMouseButton(state.LeftClick, ref leftButtonDown, MouseEventLeftDown, MouseEventLeftUp);
                    UpdateMouseButton(state.RightClick, ref rightButtonDown, MouseEventRightDown, MouseEventRightUp);
                }

                var leftTrigger = NormalizeTrigger(state.LeftTrigger);
                var rightTrigger = NormalizeTrigger(state.RightTrigger);
                var preciseWheelDirection = leftTrigger - rightTrigger;

                // PlayStation-style fast scrolling: the left stick scrolls vertically while
                // the right stick remains dedicated to cursor movement. SDL reports a
                // negative Y value when the stick is pushed up, whereas a positive Windows
                // wheel delta scrolls up, so the axis is intentionally inverted here.
                var leftStickWheelDirection = -NormalizeAxis(state.LeftY);
                var fastWheelDirection = ApplyWheelCurve(leftStickWheelDirection);

                if (Math.Abs(preciseWheelDirection) < 0.08 &&
                    Math.Abs(fastWheelDirection) < 0.01)
                {
                    wheelAccumulator = 0;
                }
                else
                {
                    wheelAccumulator +=
                        (preciseWheelDirection * PreciseWheelUnitsPerFrame) +
                        (fastWheelDirection * FastWheelUnitsPerFrame);

                    if (Math.Abs(wheelAccumulator) >= WheelDelta)
                    {
                        var wheelSteps = (int)(wheelAccumulator / WheelDelta);
                        var wheelData = wheelSteps * WheelDelta;
                        wheelAccumulator -= wheelData;
                        SendMouseInput(0, 0, wheelData, MouseEventWheel);
                    }
                }
            }
        }

        public void SuspendInput()
        {
            lock (stateLock)
            {
                if (!isActive)
                {
                    return;
                }

                ReleaseMouseButtons();
                ignoreClickButtonsUntilReleased = true;
                wheelAccumulator = 0;
            }
        }

        public void Deactivate(bool showToast)
        {
            var wasActive = false;

            lock (stateLock)
            {
                if (isActive)
                {
                    DeactivateCore();
                    wasActive = true;
                }
            }

            if (wasActive)
            {
                DebugLog("[AnikiHelper][GamepadMouse] Mode disabled.");

                if (showToast)
                {
                    ShowToast("LOCGamepadMouseDisabledToast", "Gamepad Mouse disabled");
                }
            }
        }

        private void DeactivateCore()
        {
            ReleaseMouseButtons();
            isActive = false;
            ignoreClickButtonsUntilReleased = false;
            wheelAccumulator = 0;
        }

        private void ReleaseMouseButtons()
        {
            if (leftButtonDown)
            {
                SendMouseInput(0, 0, 0, MouseEventLeftUp);
                leftButtonDown = false;
            }

            if (rightButtonDown)
            {
                SendMouseInput(0, 0, 0, MouseEventRightUp);
                rightButtonDown = false;
            }
        }

        private void UpdateMouseButton(bool isPressed, ref bool isDown, uint downFlag, uint upFlag)
        {
            if (isPressed == isDown)
            {
                return;
            }

            SendMouseInput(0, 0, 0, isPressed ? downFlag : upFlag);
            isDown = isPressed;
        }

        private static double NormalizeAxis(short value)
        {
            var absolute = Math.Abs((int)value);
            if (absolute <= AxisDeadZone)
            {
                return 0;
            }

            var normalized = (absolute - AxisDeadZone) / (32767.0 - AxisDeadZone);
            normalized = Math.Max(0, Math.Min(1, normalized));
            return value < 0 ? -normalized : normalized;
        }

        private static double ApplyWheelCurve(double normalized)
        {
            if (Math.Abs(normalized) < double.Epsilon)
            {
                return 0;
            }

            var magnitude = Math.Pow(Math.Abs(normalized), 1.25);
            return normalized < 0 ? -magnitude : magnitude;
        }

        private static double NormalizeTrigger(short value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(1, value / 32767.0));
        }

        private static int CalculateCursorDelta(double normalized)
        {
            if (Math.Abs(normalized) < double.Epsilon)
            {
                return 0;
            }

            var magnitude = Math.Abs(normalized);
            var accelerated = 1.0 + ((MaximumCursorPixelsPerFrame - 1.0) * Math.Pow(magnitude, 1.7));
            return (int)Math.Round(normalized < 0 ? -accelerated : accelerated);
        }

        private void SendMouseInput(int dx, int dy, int mouseData, uint flags)
        {
            inputBuffer[0] = new Input
            {
                Type = InputMouse,
                MouseInput = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = mouseData,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            };

            try
            {
                var sent = SendInput(1, inputBuffer, Marshal.SizeOf(typeof(Input)));
                if (sent == 0 &&
                    (DateTime.UtcNow - lastSendInputFailureLogUtc).TotalSeconds >= 5)
                {
                    lastSendInputFailureLogUtc = DateTime.UtcNow;
                    DebugLog($"[AnikiHelper][GamepadMouse] SendInput failed. Win32Error={Marshal.GetLastWin32Error()}");
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][GamepadMouse] SendInput threw an exception.");
            }
        }

        private void ShowToast(string resourceKey, string fallback)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            var generation = Interlocked.Increment(ref toastGeneration);

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    toastWindow?.Close();
                }
                catch
                {
                }

                try
                {
                    var message = Loc(resourceKey, fallback);
                    toastWindow = new GamepadMouseToastWindow(message);
                    toastWindow.Show();
                }
                catch (Exception ex)
                {
                    DebugLog(ex, "[AnikiHelper][GamepadMouse] Failed to show global toast.");
                    toastWindow = null;
                    return;
                }

                Task.Delay(1600).ContinueWith(_ =>
                {
                    try
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (generation != Volatile.Read(ref toastGeneration))
                            {
                                return;
                            }

                            try
                            {
                                toastWindow?.Close();
                            }
                            catch
                            {
                            }

                            toastWindow = null;
                        }));
                    }
                    catch
                    {
                    }
                }, TaskScheduler.Default);
            }));
        }

        private static string Loc(string key, string fallback)
        {
            try
            {
                var value = Application.Current?.TryFindResource(key) as string;
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

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
            }
        }

        public void Dispose()
        {
            Deactivate(showToast: false);
            Interlocked.Increment(ref toastGeneration);

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            toastWindow?.Close();
                        }
                        catch
                        {
                        }

                        toastWindow = null;
                    }));
                }
            }
            catch
            {
            }
        }

        private const uint InputMouse = 0;
        private const uint MouseEventMove = 0x0001;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint MouseEventRightDown = 0x0008;
        private const uint MouseEventRightUp = 0x0010;
        private const uint MouseEventWheel = 0x0800;
        private const int WheelDelta = 120;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public MouseInput MouseInput;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx;
            public int Dy;
            public int MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);
    }

    internal sealed class GamepadMouseToastWindow : Window
    {
        private const int ExtendedStyleIndex = -20;
        private const int ExtendedStyleTransparent = 0x00000020;
        private const int ExtendedStyleToolWindow = 0x00000080;
        private const int ExtendedStyleNoActivate = 0x08000000;
        private const uint SetWindowPositionNoSize = 0x0001;
        private const uint SetWindowPositionNoMove = 0x0002;
        private const uint SetWindowPositionNoActivate = 0x0010;
        private const uint SetWindowPositionShowWindow = 0x0040;
        private static readonly IntPtr TopMostHandle = new IntPtr(-1);

        public GamepadMouseToastWindow(string message)
        {
            Width = 430;
            Height = 68;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var text = new TextBlock
            {
                Text = message ?? string.Empty,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(238, 20, 20, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 12, 22, 12),
                Child = text,
                IsHitTestVisible = false
            };

            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                var handle = helper.Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var extendedStyle = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
                extendedStyle |= ExtendedStyleNoActivate | ExtendedStyleToolWindow | ExtendedStyleTransparent;
                SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(extendedStyle));

                PositionOnForegroundMonitor(handle);
                SetWindowPos(
                    handle,
                    TopMostHandle,
                    0,
                    0,
                    0,
                    0,
                    SetWindowPositionNoSize |
                    SetWindowPositionNoMove |
                    SetWindowPositionNoActivate |
                    SetWindowPositionShowWindow);
            }
            catch
            {
            }
        }

        private void PositionOnForegroundMonitor(IntPtr toastHandle)
        {
            try
            {
                var foreground = GetForegroundWindow();
                var monitor = MonitorFromWindow(
                    foreground != IntPtr.Zero ? foreground : toastHandle,
                    MonitorDefaultToNearest);

                var monitorInfo = new MonitorInfo
                {
                    Size = Marshal.SizeOf(typeof(MonitorInfo))
                };

                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
                {
                    Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
                    Top = 48;
                    return;
                }

                var dpi = VisualTreeHelper.GetDpi(this);
                var scaleX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                var scaleY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

                var workLeft = monitorInfo.WorkArea.Left / scaleX;
                var workTop = monitorInfo.WorkArea.Top / scaleY;
                var workWidth = (monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left) / scaleX;

                Left = workLeft + Math.Max(0, (workWidth - Width) / 2.0);
                Top = workTop + 52;
            }
            catch
            {
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
                Top = 48;
            }
        }

        private const uint MonitorDefaultToNearest = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr windowHandle, int index);

        private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(windowHandle, index)
                : GetWindowLongPtr32(windowHandle, index);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr newLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr32(IntPtr windowHandle, int index, IntPtr newLong);

        private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(windowHandle, index, newLong)
                : SetWindowLongPtr32(windowHandle, index, newLong);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
