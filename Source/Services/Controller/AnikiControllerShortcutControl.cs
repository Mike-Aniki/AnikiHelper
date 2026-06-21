using Playnite.SDK;
using Playnite.SDK.Controls;
using Playnite.SDK.Events;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AnikiHelper.Services.Controller
{
    public class AnikiControllerShortcutControl : PluginUserControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly bool suppressDefaults;
        private readonly bool global;
        private readonly AnikiControllerInput controller;

        private bool isControlActive;

        private static readonly object ActiveControlsLock = new object();

        private static readonly HashSet<AnikiControllerShortcutControl> ActiveControls =
            new HashSet<AnikiControllerShortcutControl>();

        private InputBindingCollection actualBindings;

        public new InputBindingCollection InputBindings
        {
            set
            {
                try
                {
                    if (value == actualBindings)
                    {
                        return;
                    }

                    if (Parent is ContentControl parent)
                    {
                        parent.InputBindings.Clear();
                        parent.InputBindings.AddRange(value);
                        actualBindings = value;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper] Failed to swap controller bindings.");
                }
            }
        }

        static AnikiControllerShortcutControl()
        {
            TagProperty.OverrideMetadata(
                typeof(AnikiControllerShortcutControl),
                new FrameworkPropertyMetadata(null, OnTagChanged)
            );
        }

        public AnikiControllerShortcutControl(bool suppressDefaults = false, bool global = false)
        {
            this.suppressDefaults = suppressDefaults;
            this.global = global;

            controller = new AnikiControllerInput();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            AnikiControllerInput.ButtonDown += OnButtonDown;

            TryRegisterFocusWatchers();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TryWatchParentTag();
            UpdateOverrideProcessing();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            SetControlActive(false);
            AnikiControllerInput.ButtonDown -= OnButtonDown;
        }

        private void TryWatchParentTag()
        {
            try
            {
                if (Parent is ContentControl parent)
                {
                    DependencyPropertyDescriptor.FromProperty(
                        ContentControl.TagProperty,
                        typeof(ContentControl)
                    )?.AddValueChanged(parent, (s, e) =>
                    {
                        UpdateOverrideProcessing();
                    });

                }

            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to watch parent Tag.");
            }
        }

        private void TryRegisterFocusWatchers()
        {
            try
            {
                dynamic model = Application.Current?.MainWindow?.DataContext;

                if (model != null && model.GetType().Name == "FullscreenAppViewModel")
                {
                    if (model.AppSettings?.Fullscreen is INotifyPropertyChanged fullscreenSettings)
                    {
                        fullscreenSettings.PropertyChanged += (o, e) =>
                        {
                            if (e.PropertyName == "EnableGameControllerSupport")
                            {
                                UpdateOverrideProcessing();
                            }
                        };
                    }

                    if (model.App is INotifyPropertyChanged app)
                    {
                        app.PropertyChanged += (o, e) =>
                        {
                            if (e.PropertyName == "IsActive")
                            {
                                UpdateOverrideProcessing();
                            }
                        };
                    }

                    EventManager.RegisterClassHandler(
                        typeof(UIElement),
                        Keyboard.GotKeyboardFocusEvent,
                        new KeyboardFocusChangedEventHandler((sender, args) =>
                        {
                            UpdateOverrideProcessing();
                        })
                    );
                }
            }
            catch
            {
            }
        }

        private bool scheduled;

        private void UpdateOverrideProcessing()
        {
            if (scheduled)
            {
                return;
            }

            scheduled = true;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                scheduled = false;
                DoUpdateOverrideProcessing();
            }), DispatcherPriority.DataBind);
        }

        private void DoUpdateOverrideProcessing()
        {
            bool active = false;
            object sourceTag = null;

            try
            {
                if (Parent is ContentControl parent)
                {
                    sourceTag = parent.Tag;
                }
                else
                {
                    sourceTag = Tag;
                }

                active = sourceTag?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) ?? false;

                if (sourceTag is InputBindingCollection collection)
                {
                    active = true;
                    InputBindings = collection;
                }

                bool finalActive = active && (global || IsParentFocused(this));

                if (finalActive && IsShortcutBlockedByOpenWindow(this))
                {
                    finalActive = false;
                }

                SetControlActive(finalActive);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to update controller shortcut processing.");
            }
        }

        private void SetControlActive(bool active)
        {
            lock (ActiveControlsLock)
            {
                isControlActive = active;

                if (active)
                {
                    ActiveControls.Add(this);
                }
                else
                {
                    ActiveControls.Remove(this);
                }

                controller.OverrideProcessing = ActiveControls.Count > 0;
            }
        }

        private static void OnTagChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is AnikiControllerShortcutControl control)
            {
                control.UpdateOverrideProcessing();
            }
        }

        private void OnButtonDown(object sender, ControllerInput button)
        {
            try
            {
                if (!isControlActive)
                {
                    return;
                }

                if (!(Parent is ContentControl parent))
                {
                    return;
                }

                bool playniteForeground = IsCurrentProcessForeground();
                bool gameRunning = IsAnyGameRunning();

                if (!playniteForeground)
                {
                    if (gameRunning)
                    {
                        logger.Debug($"[AnikiHelper][Controller] Input ignored because a game is running and Playnite is not foreground. Button={button}");
                        return;
                    }

                    if (IsThemeShortcutButton(button))
                    {
                        logger.Debug($"[AnikiHelper][Controller] Shortcut ignored because Playnite is not foreground. Button={button}");
                        return;
                    }
                }

                // Playnite native virtual keyboard uses its own controller bindings.
                // When it is opened from the main fullscreen view/search box, Aniki shortcuts
                // must not steal Start/Back/Y/X/etc. Otherwise Start opens Quick Access instead
                // of validating the keyboard with the native DONE action.
                //
                // Keep the old SettingsWindow protection: in Settings, Start/Back stay blocked
                // to avoid reopening/leaving Quick Access stuck above the settings window.
                if (IsPlayniteTextInputWindowOpen() && !IsSettingsWindowOpen())
                {
                    controller.DefaultProcess(button.ToString(), true);
                    return;
                }

                if (IsShortcutBlockedByOpenWindow(parent))
                {
                    SetControlActive(false);
                    return;
                }

                foreach (var binding in parent.InputBindings)
                {

                    if (binding.GetType().Name != "GameControllerInputBinding")
                    {
                        continue;
                    }

                    dynamic gamepadBinding = binding;

                    string bindingButton = gamepadBinding.Button.ToString();
                    string pressedButton = button.ToString();

                    if (!bindingButton.Equals(pressedButton))
                    {
                        continue;
                    }

                    if (IsStartOrBack(pressedButton) && IsShortcutBlockedByOpenWindow(parent))
                    {
                        return;
                    }

                    if (gamepadBinding.Command != null &&
                        gamepadBinding.Command.CanExecute(gamepadBinding.CommandParameter))
                    {
                        gamepadBinding.Command.Execute(gamepadBinding.CommandParameter);
                    }
                    else
                    {
                        logger.Warn($"[AnikiHelper][Controller] Command is null or cannot execute for {pressedButton}");
                    }

                    return;
                }

                if (!suppressDefaults)
                {
                    controller.DefaultProcess(button.ToString(), true);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to process controller shortcut.");
            }
        }

        private static bool IsStartOrBack(string button)
        {
            return button.Equals("Start", StringComparison.OrdinalIgnoreCase) ||
                   button.Equals("Back", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsShortcutBlockedByOpenWindow(DependencyObject shortcutHost)
        {
            try
            {
                var blockingWindows = Application.Current.Windows
                    .OfType<Window>()
                    .Where(w => w.IsVisible && IsAnikiOrSettingsWindow(w))
                    .ToList();

                if (!blockingWindows.Any())
                {
                    return false;
                }

                var hostWindow = Window.GetWindow(shortcutHost);

                if (hostWindow != null &&
                    blockingWindows.Any(w => ReferenceEquals(w, hostWindow)))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAnikiOrSettingsWindowOpen()
        {
            return Application.Current.Windows
                .OfType<Window>()
                .Any(w => w.IsVisible && IsAnikiOrSettingsWindow(w));
        }

        private static bool IsAnikiOrSettingsWindow(Window window)
        {
            if (window == null)
            {
                return false;
            }

            return window.Tag is string ||
                   IsWindowType(window, "SettingsWindow");
        }

        private static bool IsSettingsWindowOpen()
        {
            return Application.Current.Windows
                .OfType<Window>()
                .Any(w => w.IsVisible && IsWindowType(w, "SettingsWindow"));
        }

        private static bool IsPlayniteTextInputWindowOpen()
        {
            return Application.Current.Windows
                .OfType<Window>()
                .Any(w => w.IsVisible && IsWindowType(w, "TextInputWindow"));
        }

        private static bool IsWindowType(Window window, string typeName)
        {
            return (window.GetType().FullName ?? string.Empty).IndexOf(
                typeName,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsParentFocused(DependencyObject current)
        {
            var parent = VisualTreeHelper.GetParent(current);

            if (parent == null)
            {
                return false;
            }

            if (parent is UIElement parentElement && parentElement.IsKeyboardFocusWithin)
            {
                return true;
            }

            return IsParentFocused(parent);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static bool IsAnyGameRunning()
        {
            try
            {
                dynamic model = Application.Current?.MainWindow?.DataContext;

                if (model?.App?.IsGameRunning == true)
                {
                    return true;
                }

                if (model?.App?.CurrentGame != null)
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

        private static bool IsThemeShortcutButton(ControllerInput button)
        {
            return button == ControllerInput.Start ||
                   button == ControllerInput.Back ||
                   button == ControllerInput.Y;
        }

        private static bool IsCurrentProcessForeground()
        {
            try
            {
                var foregroundWindow = GetForegroundWindow();

                if (foregroundWindow == IntPtr.Zero)
                {
                    return false;
                }

                GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);

                return foregroundProcessId == (uint)Process.GetCurrentProcess().Id;
            }
            catch
            {
                return true;
            }
        }
    }
}