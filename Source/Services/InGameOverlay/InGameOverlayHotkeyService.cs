using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AnikiHelper.Services.InGameOverlay
{
    internal sealed class InGameOverlayHotkeyService : IDisposable
    {
        private const int HotkeyId = 984321;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        private const uint VK_F12 = 0x7B;

        private const int WM_HOTKEY = 0x0312;

        private readonly Action onHotkeyPressed;
        private readonly string hotkeyPreset;

        private HwndSource source;
        private bool isStarted;

        public InGameOverlayHotkeyService(Action onHotkeyPressed, string hotkeyPreset)
        {
            this.onHotkeyPressed = onHotkeyPressed;
            this.hotkeyPreset = string.IsNullOrWhiteSpace(hotkeyPreset) ? "CtrlShiftF12" : hotkeyPreset;
        }

        public void Start()
        {
            if (isStarted)
            {
                return;
            }

            var parameters = new HwndSourceParameters("AnikiHelper_InGameOverlayHotkey")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0x800000
            };

            source = new HwndSource(parameters);
            source.AddHook(WndProc);

            var modifiers = GetModifiers(hotkeyPreset);

            RegisterHotKey(source.Handle, HotkeyId, modifiers, VK_F12);

            isStarted = true;
        }

        public void Stop()
        {
            if (!isStarted)
            {
                return;
            }

            try
            {
                if (source != null)
                {
                    UnregisterHotKey(source.Handle, HotkeyId);
                    source.RemoveHook(WndProc);
                    source.Dispose();
                }
            }
            catch
            {
            }

            source = null;
            isStarted = false;
        }

        private uint GetModifiers(string preset)
        {
            switch (preset)
            {
                case "CtrlF12":
                    return MOD_CONTROL;

                case "AltF12":
                    return MOD_ALT;

                case "CtrlAltF12":
                    return MOD_CONTROL | MOD_ALT;

                case "CtrlShiftF12":
                default:
                    return MOD_CONTROL | MOD_SHIFT;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                onHotkeyPressed?.Invoke();
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Stop();
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}