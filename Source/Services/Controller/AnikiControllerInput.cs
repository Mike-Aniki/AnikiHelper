using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace AnikiHelper.Services.Controller
{
    public class AnikiControllerInput
    {
        private dynamic model;

        private static readonly Dictionary<ControllerInput, ControllerInputState> ControllerState =
            new Dictionary<ControllerInput, ControllerInputState>();

        public static event EventHandler<ControllerInput> ButtonDown;
        public static event EventHandler<ControllerInput> ButtonUp;
        public static event EventHandler<ControllerInput> ButtonChanged;

        public AnikiControllerInput()
        {
            dynamic currentModel = Application.Current?.MainWindow?.DataContext;

            if (currentModel != null && currentModel.GetType().Name == "FullscreenAppViewModel")
            {
                model = currentModel;
            }
        }

        public static void SetState(OnControllerButtonStateChangedArgs args)
        {
            ControllerState[args.Button] = args.State;

            if (args.State == ControllerInputState.Released)
            {
                ButtonUp?.Invoke(null, args.Button);
                ButtonChanged?.Invoke(null, args.Button);
            }
            else
            {
                ButtonDown?.Invoke(null, args.Button);
                ButtonChanged?.Invoke(null, args.Button);
            }
        }

        public static ControllerInput GetButton(string buttonName)
        {
            return Enum.TryParse(buttonName, true, out ControllerInput button)
                ? button
                : ControllerInput.None;
        }

        public static ControllerInputState GetState(ControllerInput button)
        {
            return ControllerState.ContainsKey(button)
                ? ControllerState[button]
                : ControllerInputState.Released;
        }

        public static ControllerInputState GetState(string buttonName)
        {
            var button = GetButton(buttonName);

            return ControllerState.ContainsKey(button)
                ? ControllerState[button]
                : ControllerInputState.Released;
        }

        public bool IsPressed(string buttonName)
        {
            return EnableGameControllerSupport &&
                   GetState(buttonName) == ControllerInputState.Pressed;
        }

        public bool EnableGameControllerSupport
        {
            get
            {
                try
                {
                    return model != null &&
                           model.AppSettings.Fullscreen.EnableGameControllerSupport;
                }
                catch
                {
                    return false;
                }
            }
        }

        private bool overrideProcessing;

        public bool OverrideProcessing
        {
            get
            {
                try
                {
                    return EnableGameControllerSupport &&
                           overrideProcessing &&
                           model != null &&
                           model.App != null &&
                           model.App.GameController != null &&
                           model.App.GameController.StandardProcessingEnabled == false;
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                try
                {
                    if (model == null || model.App == null || model.App.GameController == null)
                    {
                        return;
                    }

                    overrideProcessing = value;
                    model.App.GameController.StandardProcessingEnabled = !value && EnableGameControllerSupport;
                }
                catch
                {
                }
            }
        }

        public void RunTapOrHoldCommand(
            ICommand tapCommand,
            ICommand holdCommand,
            object tapParameter,
            object holdParameter,
            int holdDelayMs = 500)
        {
            DateTime startTime = DateTime.Now;

            bool tapDetected = false;
            bool cancelledByAnotherPress = false;

            EventHandler<ControllerInput> onButtonUp = null;
            EventHandler<ControllerInput> onButtonDown = null;

            onButtonUp = (sender, button) =>
            {
                tapDetected = true;
            };

            onButtonDown = (sender, button) =>
            {
                cancelledByAnotherPress = true;
            };

            Action removeHandlers = () =>
            {
                ButtonDown -= onButtonDown;
                ButtonUp -= onButtonUp;
            };

            var timer = new System.Timers.Timer(50)
            {
                AutoReset = false,
                Enabled = true
            };

            timer.Elapsed += (sender, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (cancelledByAnotherPress)
                    {
                        removeHandlers();
                        timer.Stop();
                        timer.Dispose();
                        return;
                    }

                    if (tapDetected)
                    {
                        removeHandlers();
                        timer.Stop();
                        timer.Dispose();

                        if (tapCommand != null && tapCommand.CanExecute(tapParameter))
                        {
                            tapCommand.Execute(tapParameter);
                        }

                        return;
                    }

                    if ((DateTime.Now - startTime).TotalMilliseconds > holdDelayMs)
                    {
                        removeHandlers();
                        timer.Stop();
                        timer.Dispose();

                        if (holdCommand != null && holdCommand.CanExecute(holdParameter))
                        {
                            holdCommand.Execute(holdParameter);
                        }

                        return;
                    }

                    ButtonUp -= onButtonUp;
                    ButtonDown -= onButtonDown;

                    ButtonUp += onButtonUp;
                    ButtonDown += onButtonDown;

                    timer.Start();
                });
            };

            timer.Start();
        }

        public void DefaultProcess(string buttonName, bool pressed)
        {
            try
            {
                if (model == null || InputManager.Current.PrimaryKeyboardDevice?.ActiveSource == null)
                {
                    return;
                }

                var playniteAssemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playnite.dll");
                var assembly = Assembly.LoadFrom(playniteAssemblyPath);

                var eventArgsType = assembly.GetType("Playnite.Input.GameControllerInputEventArgs");
                if (eventArgsType == null)
                {
                    return;
                }

                var inputState = pressed ? ControllerInputState.Pressed : ControllerInputState.Released;
                var controllerInput = GetButton(buttonName);

                var args = Activator.CreateInstance(
                    eventArgsType,
                    new object[] { Key.None, inputState, controllerInput }
                ) as InputEventArgs;

                var inputManagerField = model.App.GameController.GetType()
                    .GetField("inputManager", BindingFlags.Instance | BindingFlags.NonPublic);

                if (inputManagerField?.GetValue(model.App.GameController) is InputManager manager)
                {
                    var gameController = model.App.GameController;

                    var mapPadToKeyboard = gameController.GetType()
                        .GetMethod("MapPadToKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);

                    var simulateKeyInput = gameController.GetType()
                        .GetMethod("SimulateKeyInput", BindingFlags.Instance | BindingFlags.NonPublic);

                    if (mapPadToKeyboard == null || simulateKeyInput == null)
                    {
                        return;
                    }

                    bool previousStandardProcessing = gameController.StandardProcessingEnabled;

                    try
                    {
                        manager.ProcessInput(args);

                        var keyboard = mapPadToKeyboard.Invoke(
                            gameController,
                            new object[] { controllerInput });

                        gameController.StandardProcessingEnabled = EnableGameControllerSupport;
                        simulateKeyInput.Invoke(gameController, new object[] { keyboard, true });
                    }
                    finally
                    {
                        // Do not force custom override mode back on when the caller has just
                        // released it for a native Playnite window.
                        gameController.StandardProcessingEnabled = previousStandardProcessing;
                    }
                }
            }
            catch
            {
            }
        }
    }
}