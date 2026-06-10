using Playnite.SDK;
using Playnite.SDK.Controls;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AnikiHelper.Services.Controller
{
    public class AnikiControllerCommandsControl : PluginUserControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public MainMenuCommands MainMenu { get; } = new MainMenuCommands();

        public RelayCommand<object> ControllerHoldCommand => new RelayCommand<object>(parameter =>
        {
            if (parameter is Button[] buttons && buttons.Length == 2)
            {
                new AnikiControllerInput().RunTapOrHoldCommand(
                    buttons[0]?.Command,
                    buttons[1]?.Command,
                    buttons[0]?.CommandParameter,
                    buttons[1]?.CommandParameter
                );
            }
        });

        public RelayCommand<object> ControllerTapCommand => new RelayCommand<object>(parameter =>
        {
            if (parameter is ICommand command)
            {
                new AnikiControllerInput().RunTapOrHoldCommand(command, null, null, null);
            }
            else if (parameter is Button button)
            {
                new AnikiControllerInput().RunTapOrHoldCommand(
                    button.Command,
                    null,
                    button.CommandParameter,
                    null
                );
            }
        });

        public RelayCommand<object> Toggle => new RelayCommand<object>(parameter =>
        {
            try
            {
                var control = parameter as FrameworkElement;

                if (control == null && parameter is string elementName)
                {
                    control = FindElementByName(Application.Current?.MainWindow, elementName);
                }

                if (control == null)
                {
                    logger.Warn($"[AnikiHelper] Toggle command could not find element: {parameter}");
                    return;
                }

                var type = control.GetType();

                var commandProperty = type.GetProperty("Command");
                if (commandProperty != null && commandProperty.GetValue(control) is ICommand command)
                {
                    object commandParameter = null;

                    var commandParameterProperty = type.GetProperty("CommandParameter");
                    if (commandParameterProperty != null)
                    {
                        commandParameter = commandParameterProperty.GetValue(control);
                    }

                    if (command.CanExecute(commandParameter))
                    {
                        command.Execute(commandParameter);
                    }

                    return;
                }

                var isCheckedProperty = type.GetProperty("IsChecked");
                if (isCheckedProperty != null && isCheckedProperty.CanWrite)
                {
                    var currentValue = isCheckedProperty.GetValue(control);

                    if (currentValue is bool boolValue)
                    {
                        isCheckedProperty.SetValue(control, !boolValue);
                        return;
                    }

                    if (currentValue == null && isCheckedProperty.PropertyType == typeof(bool?))
                    {
                        isCheckedProperty.SetValue(control, true);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to execute Toggle command.");
            }
        });

        public RelayCommand<object> ChangeProperty => new RelayCommand<object>(parameter =>
        {
            try
            {
                var setterText = parameter?.ToString();

                if (string.IsNullOrWhiteSpace(setterText))
                {
                    return;
                }

                var elementName = ExtractSetterValue(setterText, "ElementName");
                var propertyName = ExtractSetterValue(setterText, "Property");
                var valueText = ExtractSetterValue(setterText, "Value");

                if (string.IsNullOrWhiteSpace(elementName) || string.IsNullOrWhiteSpace(propertyName))
                {
                    logger.Warn($"[AnikiHelper] Invalid ChangeProperty parameter: {setterText}");
                    return;
                }

                var target = FindElementByName(Application.Current?.MainWindow, elementName);

                if (target == null)
                {
                    logger.Warn($"[AnikiHelper] ChangeProperty could not find element: {elementName}");
                    return;
                }

                var property = target.GetType().GetProperty(propertyName);

                if (property == null || !property.CanWrite)
                {
                    logger.Warn($"[AnikiHelper] ChangeProperty could not find writable property: {elementName}.{propertyName}");
                    return;
                }

                var convertedValue = ConvertStringValue(valueText, property.PropertyType);
                property.SetValue(target, convertedValue);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to execute ChangeProperty command.");
            }
        });

        public class MainMenuCommands
        {
            public RelayCommand CloseCommand => CreateCommand("CloseCommand");
            public RelayCommand ExitCommand => CreateCommand("ExitCommand");
            public RelayCommand SwitchToDesktopCommand => CreateCommand("SwitchToDesktopCommand");
            public RelayCommand OpenSettingsCommand => CreateCommand("OpenSettingsCommand");
            public RelayCommand SelectRandomGameCommand => CreateCommand("SelectRandomGameCommand");
            public RelayCommand OpenPatreonCommand => CreateCommand("OpenPatreonCommand");
            public RelayCommand OpenKofiCommand => CreateCommand("OpenKofiCommand");
            public RelayCommand ShutdownSystemCommand => CreateCommand("ShutdownSystemCommand");
            public RelayCommand HibernateSystemCommand => CreateCommand("HibernateSystemCommand");
            public RelayCommand SleepSystemCommand => CreateCommand("SleepSystemCommand");
            public RelayCommand RestartSystemCommand => CreateCommand("RestartSystemCommand");
            public RelayCommand LockSystemCommand => CreateCommand("LockSystemCommand");
            public RelayCommand LogoutUserCommand => CreateCommand("LogoutUserCommand");
            public RelayCommand OpenClientsCommand => CreateCommand("OpenClientsCommand");
            public RelayCommand OpenToolsCommand => CreateCommand("OpenToolsCommand");
            public RelayCommand OpenExtensionsCommand => CreateCommand("OpenExtensionsCommand");
            public RelayCommand UpdateGamesCommand => CreateCommand("UpdateGamesCommand");
            public RelayCommand CancelProgressCommand => CreateCommand("CancelProgressCommand");
            public RelayCommand OpenHelpCommand => CreateCommand("OpenHelpCommand");
            public RelayCommand MinimizeCommand => CreateCommand("MinimizeCommand");

            private static RelayCommand CreateCommand(string commandName)
            {
                return new RelayCommand(() => ExecuteMainMenuCommand(commandName));
            }

            private static void ExecuteMainMenuCommand(string commandName)
            {
                try
                {
                    var model = GetMainMenuModel();

                    if (model == null)
                    {
                        logger.Warn($"[AnikiHelper] MainMenu model is null for command: {commandName}");
                        return;
                    }

                    var commandProperty = model.GetType().GetProperty(commandName);

                    if (commandProperty == null)
                    {
                        logger.Warn($"[AnikiHelper] MainMenu command not found: {commandName}");
                        return;
                    }

                    if (!(commandProperty.GetValue(model) is ICommand command))
                    {
                        logger.Warn($"[AnikiHelper] MainMenu property is not an ICommand: {commandName}");
                        return;
                    }

                    if (command.CanExecute(null))
                    {
                        command.Execute(null);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"[AnikiHelper] Failed to execute MainMenu command: {commandName}");
                }
            }

            private static dynamic GetMainMenuModel()
            {
                const string mainMenuWindowFactory = "Playnite.FullscreenApp.Windows.MainMenuWindowFactory";
                const string mainMenuViewModel = "Playnite.FullscreenApp.ViewModels.MainMenuViewModel";

                var playniteAssemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playnite.dll");
                var playniteAssembly = Assembly.LoadFrom(playniteAssemblyPath);

                var windowBaseType = playniteAssembly.GetType("Playnite.Controls.WindowBase");
                var windowBase = Activator.CreateInstance(windowBaseType);

                var fullscreenAssembly = Application.Current.GetType().Assembly;

                var factoryType = fullscreenAssembly.GetType(mainMenuWindowFactory);
                var factory = Activator.CreateInstance(factoryType);

                var windowProperty = factoryType.BaseType.GetProperty(
                    "Window",
                    BindingFlags.Public | BindingFlags.Instance);

                windowProperty.GetSetMethod(true).Invoke(factory, new[] { windowBase });

                var initFinishedEventProperty = factoryType.BaseType.GetProperty(
                    "initFinishedEvent",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var initFinishedEvent = initFinishedEventProperty.GetValue(factory) as AutoResetEvent;
                initFinishedEvent?.Set();

                var modelType = fullscreenAssembly.GetType(mainMenuViewModel);

                return Activator.CreateInstance(
                    modelType,
                    new[] { factory, Application.Current.MainWindow.DataContext });
            }
        }

        private static string ExtractSetterValue(string text, string key)
        {
            var match = Regex.Match(
                text,
                key + @"\s*=\s*([^,\]]+)",
                RegexOptions.IgnoreCase);

            return match.Success
                ? match.Groups[1].Value.Trim()
                : null;
        }

        private static FrameworkElement FindElementByName(DependencyObject root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (root is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            int childrenCount;

            try
            {
                childrenCount = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                childrenCount = 0;
            }

            for (var i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindElementByName(child, name);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static object ConvertStringValue(string value, Type targetType)
        {
            if (targetType == typeof(string))
            {
                return value;
            }

            if (targetType == typeof(bool))
            {
                return bool.TryParse(value, out var boolValue) && boolValue;
            }

            if (targetType == typeof(bool?))
            {
                return bool.TryParse(value, out var nullableBoolValue)
                    ? nullableBoolValue
                    : (bool?)null;
            }

            if (targetType == typeof(int))
            {
                return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var intValue)
                    ? intValue
                    : 0;
            }

            if (targetType == typeof(double))
            {
                return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue)
                    ? doubleValue
                    : 0d;
            }

            if (targetType == typeof(float))
            {
                return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var floatValue)
                    ? floatValue
                    : 0f;
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, value, true);
            }

            var converter = TypeDescriptor.GetConverter(targetType);

            if (converter != null && converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFromInvariantString(value);
            }

            return value;
        }
    }
}