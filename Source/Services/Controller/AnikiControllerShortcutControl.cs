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
    }
}