using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections;
using AnikiHelper.Services.AnikiThemeSettings;
using System.Diagnostics;

namespace AnikiHelperFullscreen.Views
{
    public class AnikiThemeSettingsCategoryMenuHeaderModel
    {
        public string Title { get; set; }

        public object BackButton { get; set; }
    }

    public static class FullscreenSettingsView
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static void DebugLog(string message)
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
                // Never let debug logging break the plugin.
            }
        }

        private static void DebugLog(Exception exception, string message)
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
                // Never let debug logging break the plugin.
            }
        }

        private static List<UIElement> originalSettingsMenuChildren;
        private static object currentSettingsContext;
        private static int currentThemeSettingsSectionKey;
        private static int currentCommunityVisualPacksSectionKey;
        private static int currentLoginRandomPoolSectionKey;
        private static int currentThemeColorRandomPoolSectionKey;
        private static int currentVisualPackRandomPoolSectionKey;
        private static bool communityVisualPacksSectionOpen;
        private static bool loginRandomPoolSectionOpen;
        private static bool themeColorRandomPoolSectionOpen;
        private static bool visualPackRandomPoolSectionOpen;
        private static global::AnikiHelper.AnikiCommunityVisualPacksFullscreenController communityVisualPacksController;
        private static Window communityPacksWindow;


        public static RelayCommand<object> AnikiThemeTextInputCommand { get; } =
            new RelayCommand<object>((parameter) =>
            {
                TextInput(parameter as AnikiThemeVariable);
            });

        public static RelayCommand<object> OpenCommunityPacksCommand { get; } =
            new RelayCommand<object>((parameter) => OpenCommunityPacks(parameter as string));

        // Compatibility alias for any older XAML still referencing the Visual-only command.
        public static RelayCommand OpenCommunityVisualPacksCommand { get; } =
            new RelayCommand(() => OpenCommunityPacks("VisualPack"));

        public static RelayCommand CommunityVisualPacksBackCommand { get; } =
            new RelayCommand(() => ReturnFromCommunityVisualPacks());

        public static RelayCommand OpenLoginRandomPoolCommand { get; } =
            new RelayCommand(() => OpenLoginRandomPool());

        public static RelayCommand LoginRandomPoolBackCommand { get; } =
            new RelayCommand(() => ReturnFromLoginRandomPool());

        public static RelayCommand OpenThemeColorRandomPoolCommand { get; } =
            new RelayCommand(() => OpenThemeColorRandomPool());

        public static RelayCommand ThemeColorRandomPoolBackCommand { get; } =
            new RelayCommand(() => ReturnFromThemeColorRandomPool());

        public static RelayCommand OpenVisualPackRandomPoolCommand { get; } =
            new RelayCommand(() => OpenVisualPackRandomPool());

        public static RelayCommand VisualPackRandomPoolBackCommand { get; } =
            new RelayCommand(() => ReturnFromVisualPackRandomPool());

        // Public entry point used by Quick Access > Extras.
        public static void OpenCommunityPacksHub()
        {
            OpenCommunityPacks("CommunityHub");
        }

        public static void Init()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                Window.LoadedEvent,
                new RoutedEventHandler((sender, e) =>
                {
                    if (sender.GetType().Name != "SettingsWindow")
                    {
                        return;
                    }

                    try
                    {
                        Load(sender as DependencyObject);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "[AnikiHelper] Failed to inject fullscreen settings.");
                    }
                }));
        }

        private static bool IsAnikiThemeActive()
        {
            try
            {
                return Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }

        private static void Load(DependencyObject parent)
        {
            var swTotal = Stopwatch.StartNew();
            DebugLog("[AnikiHelper][SettingsPerf] Load started.");

            EnsureEnglishFallbackResources();
            EnsureDefaultStyleResources();

            if (!IsAnikiThemeActive())
            {
                return;
            }

            if (!(parent is Window window))
            {
                return;
            }

            // Native Playnite Settings still counts as an open Aniki-related window,
            // but it does not affect IsSecondaryMusicWindowOpen.
            var plugin = global::AnikiHelper.AnikiHelper.Instance;

            plugin?.SetAnikiWindowOpenState(true, "FullscreenSettingsView.Load");

            window.Closed -= OnSettingsWindowClosed;
            window.Closed += OnSettingsWindowClosed;
            window.PreviewKeyDown -= OnSettingsWindowPreviewKeyDown;
            window.PreviewKeyDown += OnSettingsWindowPreviewKeyDown;


            dynamic ctx = window.DataContext;

            global::AnikiHelper.AnikiHelper.Instance?.SetAnikiThemeSettingsRestartRequiredAction(() =>
            {
                MarkFullscreenThemeEdited(ctx);
            });

            dynamic sectionViews = ctx
                .GetType()
                .GetField("sectionViews", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(ctx);

            StackPanel stack = parent
                .FindVisualChildren<DependencyObject>("System.Windows.Controls.StackPanel")
                .FirstOrDefault() as StackPanel;

            if (stack == null)
            {
                return;
            }

            var assembly = Application.Current.GetType().Assembly;

            int anikiThemeSettingsCategorySectionKey = CreateHiddenSettingsSection(
                assembly,
                sectionViews,
                new Func<UserControl>(LoadAnikiThemeSettingsView));

            int anikiHelperSectionKey = CreateHiddenSettingsSection(
                assembly,
                sectionViews,
                new Func<UserControl>(LoadFullscreenSettingsView));

            int loginRandomPoolSectionKey = CreateHiddenSettingsSection(
                assembly,
                sectionViews,
                new Func<UserControl>(LoadLoginRandomPoolFullscreenView));

            int themeColorRandomPoolSectionKey = CreateHiddenSettingsSection(
                assembly,
                sectionViews,
                new Func<UserControl>(LoadThemeColorRandomPoolFullscreenView));

            int visualPackRandomPoolSectionKey = CreateHiddenSettingsSection(
                assembly,
                sectionViews,
                new Func<UserControl>(LoadVisualPackRandomPoolFullscreenView));

            // Community Packs now opens in its own fullscreen window instead of being
            // hosted as another Settings section. This keeps the controller focus tree
            // independent from Playnite's Settings menu.
            currentSettingsContext = ctx;
            currentThemeSettingsSectionKey = anikiThemeSettingsCategorySectionKey;
            currentCommunityVisualPacksSectionKey = 0;
            currentLoginRandomPoolSectionKey = loginRandomPoolSectionKey;
            currentThemeColorRandomPoolSectionKey = themeColorRandomPoolSectionKey;
            currentVisualPackRandomPoolSectionKey = visualPackRandomPoolSectionKey;

            InjectThemeSettingsMenuButton(
                assembly,
                ctx,
                stack,
                anikiThemeSettingsCategorySectionKey,
                anikiHelperSectionKey,
                insertIndex: 3);
        }

        private static void OnSettingsWindowClosed(object sender, EventArgs e)
        {
            try
            {
                originalSettingsMenuChildren = null;
                currentSettingsContext = null;
                currentThemeSettingsSectionKey = 0;
                currentCommunityVisualPacksSectionKey = 0;
                currentLoginRandomPoolSectionKey = 0;
                currentThemeColorRandomPoolSectionKey = 0;
                currentVisualPackRandomPoolSectionKey = 0;
                communityVisualPacksSectionOpen = false;
                loginRandomPoolSectionOpen = false;
                themeColorRandomPoolSectionOpen = false;
                visualPackRandomPoolSectionOpen = false;

                try
                {
                    if (communityPacksWindow != null)
                    {
                        communityPacksWindow.Close();
                    }
                }
                catch { }
                communityPacksWindow = null;

                try { communityVisualPacksController?.Dispose(); } catch { }
                communityVisualPacksController = null;

                if (sender is Window closedWindow)
                {
                    closedWindow.PreviewKeyDown -= OnSettingsWindowPreviewKeyDown;
                }

                var plugin = global::AnikiHelper.AnikiHelper.Instance;

                plugin?.SetAnikiWindowOpenState(false, "FullscreenSettingsView.Closed");
                plugin?.ShowAnikiThemeSettingsRestartPromptIfNeeded();
            }
            catch (Exception ex)
            {
                logger.Warn(
                    ex,
                    "[AnikiHelper] Failed to handle settings window close for Aniki Theme Settings.");
            }
        }

        private static int CreateHiddenSettingsSection(
            Assembly assembly,
            dynamic sectionViews,
            Func<UserControl> viewFactory,
            bool setDefaultDataContext = true)
        {
            Type sectionType = assembly.GetType("Playnite.FullscreenApp.Controls.SettingsSections.SettingsSectionControl");
            dynamic hostControl = Activator.CreateInstance(sectionType);

            UserControl control = viewFactory();
            if (setDefaultDataContext)
            {
                control.DataContext = global::AnikiHelper.AnikiHelper.Instance.SettingsVM;
            }

            hostControl.Content = control;

            if (hostControl is FrameworkElement hostElement)
            {
                hostElement.Loaded += OnLoad;
            }

            int nextKey = GetNextSectionKey(sectionViews);
            sectionViews[nextKey] = hostControl;

            return nextKey;
        }

        private static void InjectThemeSettingsMenuButton(
            Assembly assembly,
            dynamic ctx,
            StackPanel stack,
            int categorySectionKey,
            int anikiHelperSectionKey,
            int insertIndex)
        {
            try
            {
                foreach (var child in stack.Children.OfType<ContentControl>())
                {
                    if ((child.Content as string) == "Theme Settings")
                    {
                        return;
                    }
                }

                Type buttonExType = assembly.GetType("Playnite.FullscreenApp.Controls.ButtonEx");
                dynamic newBtn = Activator.CreateInstance(buttonExType);

                newBtn.Content = "Theme Settings";
                newBtn.ContentTemplate = Application.Current.TryFindResource("SettingsMenuAnikiThemeSettingsButtonTemplate") as DataTemplate;
                newBtn.Style = Application.Current.TryFindResource("SettingsMenuButton") as Style;
                newBtn.Command = new RelayCommand(() =>
                {
                    OpenAnikiThemeSettingsCategoryMenu(assembly, ctx, stack, categorySectionKey, anikiHelperSectionKey);
                });

                int finalIndex = Math.Min(insertIndex, stack.Children.Count);
                stack.Children.Insert(finalIndex, newBtn);

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to inject Theme Settings submenu button.");
            }
        }

        private static string GetThemeSettingCategoryTemplateKey(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return "AnikiThemeSettingsCategoryButtonTemplate";
            }

            var cleanId = new string(categoryId
                .Where(char.IsLetterOrDigit)
                .ToArray());

            return "ThemeSettingCategory" + cleanId + "Template";
        }

        private static void OpenAnikiThemeSettingsCategoryMenu(Assembly assembly, dynamic ctx, StackPanel stack, int categorySectionKey, int anikiHelperSectionKey)
        {
            try
            {
                if (stack == null)
                {
                    logger.Warn("[AnikiHelper] Cannot open Theme Settings category menu: stack is null.");
                    return;
                }

                originalSettingsMenuChildren = stack.Children
                    .OfType<UIElement>()
                    .ToList();

                stack.Children.Clear();

                Type buttonExType = assembly.GetType("Playnite.FullscreenApp.Controls.ButtonEx");

                if (buttonExType == null)
                {
                    logger.Warn("[AnikiHelper] ButtonEx type not found.");
                    return;
                }

                var settings = global::AnikiHelper.AnikiHelper.Instance?.Settings;

                if (settings?.AnikiThemeSettingsCategories == null)
                {
                    logger.Warn("[AnikiHelper] AnikiThemeSettingsCategories is null.");
                    return;
                }

                var titleText = Application.Current.TryFindResource("LOCAnikiThemeSettingsTitle") as string
                    ?? "Theme Settings";

                var backText = Application.Current.TryFindResource("LOCAnikiThemeSettingsBack") as string
                    ?? Application.Current.TryFindResource("LOCInGameOverlayBack") as string
                    ?? "← Back";

                var window = Window.GetWindow(stack);
                var panelHeight = stack.ActualHeight;

                if (panelHeight < 700 && window != null && window.ActualHeight > 0)
                {
                    panelHeight = window.ActualHeight - 130;
                }

                if (panelHeight < 700)
                {
                    panelHeight = 950;
                }

                var rootGrid = new Grid
                {
                    Height = panelHeight,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                // Row 0 = header
                // Row 1 = categories
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                dynamic headerBackButton = Activator.CreateInstance(buttonExType);

                headerBackButton.Content = "←";
                headerBackButton.Style =
                    Application.Current.TryFindResource("AnikiThemeSettingsCategoryMenuHeaderBackButtonStyle") as Style
                    ?? Application.Current.TryFindResource("SettingsMenuButton") as Style;

                headerBackButton.Command = new RelayCommand(() =>
                {
                    RestoreMainSettingsMenu(stack);
                });

                var headerContent = new AnikiThemeSettingsCategoryMenuHeaderModel
                {
                    Title = titleText,
                    BackButton = null
                };

                var headerControl = new ContentControl
                {
                    Content = headerContent,
                    ContentTemplate = Application.Current.TryFindResource("AnikiThemeSettingsCategoryMenuHeaderTemplate") as DataTemplate,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Focusable = false,
                    IsTabStop = false
                };

                // Purely visual category header: never let controller/keyboard navigation
                // stop on it as if it were an empty settings entry.
                KeyboardNavigation.SetIsTabStop(headerControl, false);

                Grid.SetRow(headerControl, 0);
                rootGrid.Children.Add(headerControl);

                var categoriesPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 45, 0, 0)
                };

                Grid.SetRow(categoriesPanel, 1);
                rootGrid.Children.Add(categoriesPanel);

                Control firstCategoryButton = null;

                foreach (var category in settings.AnikiThemeSettingsCategories)
                {
                    var categoryId = category?.Id;
                    var categoryTitle = category?.Title;

                    if (string.IsNullOrWhiteSpace(categoryId))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(categoryTitle))
                    {
                        categoryTitle = categoryId;
                    }

                    dynamic categoryButton = Activator.CreateInstance(buttonExType);

                    categoryButton.Content = category;
                    categoryButton.ContentTemplate =
                        Application.Current.TryFindResource(GetThemeSettingCategoryTemplateKey(categoryTitle)) as DataTemplate
                        ?? Application.Current.TryFindResource("AnikiThemeSettingsCategoryButtonTemplate") as DataTemplate;

                    categoryButton.Style = Application.Current.TryFindResource("SettingsMenuButton") as Style;
                    categoryButton.Command = new RelayCommand(() =>
                    {
                        settings.SelectAnikiThemeSettingsCategory(categoryId);
                        ctx.OpenSectionCommand.Execute(categorySectionKey.ToString());
                    });

                    if (firstCategoryButton == null && categoryButton is Control control)
                    {
                        firstCategoryButton = control;
                    }

                    categoriesPanel.Children.Add(categoryButton);
                }

                dynamic anikiHelperButton = Activator.CreateInstance(buttonExType);

                anikiHelperButton.Content = new AnikiThemeSettingsCategory
                {
                    Title = Application.Current.TryFindResource("LOCAnikiThemeSettingsAdvanced") as string ?? "Aniki Helper Settings",
                    Icon = "\uE950"
                };

                anikiHelperButton.ContentTemplate = Application.Current.TryFindResource("AnikiThemeSettingsCategoryButtonTemplate") as DataTemplate;
                anikiHelperButton.Style = Application.Current.TryFindResource("SettingsMenuButton") as Style;
                anikiHelperButton.Command = ctx.OpenSectionCommand;
                anikiHelperButton.CommandParameter = anikiHelperSectionKey.ToString();

                categoriesPanel.Children.Add(anikiHelperButton);
                stack.Children.Add(rootGrid);

                if (firstCategoryButton != null)
                {
                    firstCategoryButton.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        firstCategoryButton.Focus();
                        Keyboard.Focus(firstCategoryButton);
                    }), DispatcherPriority.Input);
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to open Theme Settings category menu.");
            }
        }

        private static void RestoreMainSettingsMenu(StackPanel stack)
        {
            try
            {
                if (originalSettingsMenuChildren == null || originalSettingsMenuChildren.Count == 0)
                {
                    return;
                }

                stack.Children.Clear();

                foreach (var child in originalSettingsMenuChildren)
                {
                    stack.Children.Add(child);
                }

                stack.UpdateLayout();

                originalSettingsMenuChildren = null;

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to restore main settings menu.");
            }
        }

        private static void MarkFullscreenThemeEdited(dynamic ctx)
        {
            try
            {
                if (ctx == null)
                {
                    logger.Warn("[AnikiHelper] Cannot mark restart required: settings context is null.");
                    return;
                }

                var field = ctx.GetType().GetField(
                    "editedFields",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                {
                    logger.Warn("[AnikiHelper] Cannot mark restart required: editedFields field not found.");
                    return;
                }

                if (!(field.GetValue(ctx) is IList editedFields))
                {
                    logger.Warn("[AnikiHelper] Cannot mark restart required: editedFields is not an IList.");
                    return;
                }

                if (!editedFields.Contains("Theme"))
                {
                    editedFields.Add("Theme");
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to mark fullscreen Theme as edited.");
            }
        }

        private static void InjectSettingsSection(
             Assembly assembly,
             dynamic ctx,
             dynamic sectionViews,
             StackPanel stack,
             string content,
             string templateKey,
             Func<UserControl> viewFactory,
             int insertIndex)
        {
            try
            {
                foreach (var child in stack.Children.OfType<ContentControl>())
                {
                    if ((child.Content as string) == content)
                    {
                        return;
                    }
                }

                Type sectionType = assembly.GetType("Playnite.FullscreenApp.Controls.SettingsSections.SettingsSectionControl");
                dynamic hostControl = Activator.CreateInstance(sectionType);

                UserControl control;

                try
                {
                    control = viewFactory();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"[AnikiHelper] Failed to load fullscreen settings view for section: {content}");

                    control = new UserControl
                    {
                        Content = new TextBlock
                        {
                            Text = $"Failed to load {content}. Check Aniki Helper logs.",
                            Foreground = Brushes.White,
                            FontSize = 28,
                            Margin = new Thickness(40),
                            TextWrapping = TextWrapping.Wrap
                        }
                    };
                }

                control.DataContext = global::AnikiHelper.AnikiHelper.Instance.SettingsVM;

                hostControl.Content = control;

                if (hostControl is FrameworkElement hostElement)
                {
                    hostElement.Loaded += OnLoad;
                }

                int nextKey = GetNextSectionKey(sectionViews);
                sectionViews[nextKey] = hostControl;

                Type buttonExType = assembly.GetType("Playnite.FullscreenApp.Controls.ButtonEx");
                dynamic newBtn = Activator.CreateInstance(buttonExType);

                newBtn.Content = content;
                newBtn.ContentTemplate = Application.Current.TryFindResource(templateKey) as DataTemplate;
                newBtn.Style = Application.Current.TryFindResource("SettingsMenuButton") as Style;
                newBtn.Command = ctx.OpenSectionCommand;
                newBtn.CommandParameter = nextKey.ToString();

                var finalIndex = Math.Min(insertIndex, stack.Children.Count);
                stack.Children.Insert(finalIndex, newBtn);

            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper] Failed to inject fullscreen settings section: {content}");
            }
        }

        private static int GetNextSectionKey(dynamic sectionViews)
        {
            try
            {
                var keys = (sectionViews.Keys as IEnumerable<int>)?.ToList();

                if (keys == null || keys.Count == 0)
                {
                    return 1;
                }

                return keys.Max() + 1;
            }
            catch
            {
                return 999;
            }
        }

        private static UserControl LoadFullscreenSettingsView()
        {
            string pluginAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceUri = new Uri(
                $"pack://application:,,,/{pluginAssemblyName};component/Views/AnikiHelperFullscreenSettingsView.xaml",
                UriKind.Absolute);

            var resource = Application.GetResourceStream(resourceUri);

            if (resource == null || resource.Stream == null)
            {
                throw new Exception("AnikiHelperFullscreenSettingsView.xaml resource not found.");
            }

            using (var stream = resource.Stream)
            {
                return (UserControl)XamlReader.Load(stream);
            }
        }

        private static UserControl LoadAnikiThemeSettingsView()
        {
            string pluginAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceUri = new Uri(
                $"pack://application:,,,/{pluginAssemblyName};component/Views/AnikiThemeSettingsFullscreenView.xaml",
                UriKind.Absolute);

            var resource = Application.GetResourceStream(resourceUri);

            if (resource == null || resource.Stream == null)
            {
                throw new Exception("AnikiThemeSettingsFullscreenView.xaml resource not found.");
            }

            using (var stream = resource.Stream)
            {
                var control = (UserControl)XamlReader.Load(stream);

                HookComboBoxNavigation(control);
                HookAnikiThemeSettingsPreview(control);

                return control;
            }
        }

        private static UserControl LoadLoginRandomPoolFullscreenView()
        {
            string pluginAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceUri = new Uri(
                $"pack://application:,,,/{pluginAssemblyName};component/Views/LoginRandomPoolFullscreenView.xaml",
                UriKind.Absolute);

            var resource = Application.GetResourceStream(resourceUri);
            if (resource == null || resource.Stream == null)
            {
                throw new Exception("LoginRandomPoolFullscreenView.xaml resource not found.");
            }

            using (var stream = resource.Stream)
            {
                return (UserControl)XamlReader.Load(stream);
            }
        }

        private static UserControl LoadThemeColorRandomPoolFullscreenView()
        {
            string pluginAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceUri = new Uri(
                $"pack://application:,,,/{pluginAssemblyName};component/Views/ThemeColorRandomPoolFullscreenView.xaml",
                UriKind.Absolute);

            var resource = Application.GetResourceStream(resourceUri);
            if (resource == null || resource.Stream == null)
            {
                throw new Exception("ThemeColorRandomPoolFullscreenView.xaml resource not found.");
            }

            using (var stream = resource.Stream)
            {
                return (UserControl)XamlReader.Load(stream);
            }
        }

        private static UserControl LoadVisualPackRandomPoolFullscreenView()
        {
            string pluginAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceUri = new Uri(
                $"pack://application:,,,/{pluginAssemblyName};component/Views/VisualPackRandomPoolFullscreenView.xaml",
                UriKind.Absolute);

            var resource = Application.GetResourceStream(resourceUri);
            if (resource == null || resource.Stream == null)
            {
                throw new Exception("VisualPackRandomPoolFullscreenView.xaml resource not found.");
            }

            using (var stream = resource.Stream)
            {
                return (UserControl)XamlReader.Load(stream);
            }
        }

        private static UserControl LoadCommunityVisualPacksFullscreenView()
        {
            var plugin = global::AnikiHelper.AnikiHelper.Instance;
            if (plugin == null)
            {
                throw new InvalidOperationException("Aniki Helper is not available.");
            }

            communityVisualPacksController = plugin.CreateCommunityVisualPacksFullscreenController();
            if (communityVisualPacksController?.Control == null)
            {
                throw new InvalidOperationException("Community Packs Fullscreen view could not be created.");
            }

            return communityVisualPacksController.Control;
        }

        private static void OpenLoginRandomPool()
        {
            try
            {
                if (currentSettingsContext == null || currentLoginRandomPoolSectionKey <= 0)
                {
                    logger.Warn("[AnikiHelper] Random Login Pool fullscreen section is not available.");
                    return;
                }

                global::AnikiHelper.AnikiHelper.Instance?.Settings?.RefreshLoginRandomPoolItems();
                loginRandomPoolSectionOpen = true;

                dynamic ctx = currentSettingsContext;
                ctx.OpenSectionCommand.Execute(currentLoginRandomPoolSectionKey.ToString());
            }
            catch (Exception ex)
            {
                loginRandomPoolSectionOpen = false;
                logger.Warn(ex, "[AnikiHelper] Failed to open Random Login Pool fullscreen section.");
            }
        }

        public static void ReturnFromLoginRandomPool()
        {
            try
            {
                loginRandomPoolSectionOpen = false;
                if (currentSettingsContext == null || currentThemeSettingsSectionKey <= 0)
                {
                    return;
                }

                dynamic ctx = currentSettingsContext;
                ctx.OpenSectionCommand.Execute(currentThemeSettingsSectionKey.ToString());
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to return from Random Login Pool fullscreen section.");
            }
        }

        private static void OpenThemeColorRandomPool()
        {
            try
            {
                if (currentSettingsContext == null || currentThemeColorRandomPoolSectionKey <= 0)
                {
                    logger.Warn("[AnikiHelper] Random Theme Color Pool fullscreen section is not available.");
                    return;
                }

                global::AnikiHelper.AnikiHelper.Instance?.Settings?.RefreshThemeColorRandomPoolItems();
                themeColorRandomPoolSectionOpen = true;

                dynamic ctx = currentSettingsContext;
                ctx.OpenSectionCommand.Execute(currentThemeColorRandomPoolSectionKey.ToString());
            }
            catch (Exception ex)
            {
                themeColorRandomPoolSectionOpen = false;
                logger.Warn(ex, "[AnikiHelper] Failed to open Random Theme Color Pool fullscreen section.");
            }
        }

        public static void ReturnFromThemeColorRandomPool()
        {
            try
            {
                themeColorRandomPoolSectionOpen = false;
                if (currentSettingsContext == null || currentThemeSettingsSectionKey <= 0)
                {
                    return;
                }

                dynamic ctx = currentSettingsContext;
                ctx.OpenSectionCommand.Execute(currentThemeSettingsSectionKey.ToString());
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to return from Random Theme Color Pool fullscreen section.");
            }
        }

        private static void OpenVisualPackRandomPool()
        {
            try
            {
                if (currentSettingsContext == null || currentVisualPackRandomPoolSectionKey <= 0)
                {
                    logger.Warn("[AnikiHelper] Random Visual Pack Pool fullscreen section is not available.");
                    return;
                }

                global::AnikiHelper.AnikiHelper.Instance?.Settings?.RefreshVisualPackRandomPoolItems();
                visualPackRandomPoolSectionOpen = true;

                dynamic ctx = currentSettingsContext;
                ctx.OpenSectionCommand.Execute(currentVisualPackRandomPoolSectionKey.ToString());
            }
            catch (Exception ex)
            {
                visualPackRandomPoolSectionOpen = false;
                logger.Warn(ex, "[AnikiHelper] Failed to open Random Visual Pack Pool fullscreen section.");
            }
        }

        public static void ReturnFromVisualPackRandomPool()
        {
            try
            {
                visualPackRandomPoolSectionOpen = false;
                if (currentSettingsContext == null || currentThemeSettingsSectionKey <= 0)
                {
                    return;
                }

                dynamic ctx = currentSettingsContext;
                ctx.OpenSectionCommand.Execute(currentThemeSettingsSectionKey.ToString());
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to return from Random Visual Pack Pool fullscreen section.");
            }
        }

        private static void OpenCommunityPacks(string presetGroupId)
        {
            try
            {
                var plugin = global::AnikiHelper.AnikiHelper.Instance;
                if (plugin == null)
                {
                    logger.Warn("[AnikiHelper] Community Packs Fullscreen view is not available.");
                    return;
                }

                var packType = GetCommunityPackTypeForPresetGroup(presetGroupId);
                if (string.IsNullOrWhiteSpace(packType))
                {
                    logger.Warn("[AnikiHelper] No Community Pack category is mapped for preset group: " + (presetGroupId ?? "<null>"));
                    return;
                }

                // If the window already exists, just switch the requested category and bring it back.
                if (communityPacksWindow != null)
                {
                    communityVisualPacksController?.PrepareForOpen(packType);
                    if (communityPacksWindow.WindowState == WindowState.Minimized)
                    {
                        communityPacksWindow.WindowState = WindowState.Normal;
                    }
                    communityPacksWindow.Activate();
                    communityVisualPacksController?.FocusInitial();
                    return;
                }

                var owner = Application.Current?.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => w != null && w.IsVisible && w.GetType().Name == "SettingsWindow")
                    ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w != null && w.IsActive)
                    ?? Application.Current?.MainWindow;

                var window = new Window();

                window.Title = Application.Current?.TryFindResource("CommunityHub_Title") as string ?? "Community Packs";
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.ShowInTaskbar = false;
                window.SizeToContent = SizeToContent.Manual;
                window.Background = Application.Current?.TryFindResource("BackgroundMenu") as Brush ?? Brushes.Black;

                // IMPORTANT: do not construct the Community Packs controller/XAML before ShowDialog().
                // XamlReader.Load on the full Community Packs view is one of the expensive parts of
                // opening this page. Showing this tiny loading surface first gives the button press
                // immediate visual feedback; the real control is then created on the next dispatcher pass.
                var openingRoot = new Grid();
                openingRoot.SetResourceReference(Panel.BackgroundProperty, "BackgroundMenu");

                var loadingStack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var loadingText = new TextBlock
                {
                    Text = "Loading Community Packs...",
                    FontSize = 34,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                loadingText.SetResourceReference(TextBlock.TextProperty, "CommunityPack_Loading");
                loadingText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                loadingText.SetResourceReference(TextBlock.FontFamilyProperty, "FontMain");

                loadingStack.Children.Add(loadingText);
                loadingStack.Children.Add(new ProgressBar
                {
                    Width = 360,
                    Height = 6,
                    Margin = new Thickness(0, 18, 0, 0),
                    IsIndeterminate = true
                });

                openingRoot.Children.Add(loadingStack);
                window.Content = openingRoot;

                if (owner != null)
                {
                    window.Owner = owner;
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = owner.Left;
                    window.Top = owner.Top;
                    var ownerWidth = owner.ActualWidth;
                    var ownerHeight = owner.ActualHeight;
                    if (double.IsNaN(ownerWidth) || ownerWidth <= 100)
                    {
                        ownerWidth = SystemParameters.PrimaryScreenWidth;
                    }
                    if (double.IsNaN(ownerHeight) || ownerHeight <= 100)
                    {
                        ownerHeight = SystemParameters.PrimaryScreenHeight;
                    }
                    window.Width = ownerWidth;
                    window.Height = ownerHeight;
                }
                else
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    window.WindowState = WindowState.Maximized;
                }

                window.PreviewKeyDown += OnCommunityPacksWindowPreviewKeyDown;
                window.ContentRendered += OnCommunityPacksWindowContentRendered;
                window.Closed += OnCommunityPacksWindowClosed;

                communityPacksWindow = window;
                communityVisualPacksSectionOpen = true;

                // Community Packs is a plugin-owned WPF window rather than a normal
                // OpenWindow style. Register it with AnikiWindowManager so controller B
                // follows the same central Cancel/Back path as the other Aniki windows.
                plugin.RegisterExternalFullscreenWindow(
                    window,
                    "CommunityPacksWindow",
                    isChild: true,
                    secondaryMusic: false);

                // Queue the real view before entering ShowDialog's nested dispatcher loop.
                // It runs only after the lightweight window has had a chance to render once.
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(communityPacksWindow, window) || !window.IsVisible)
                    {
                        return;
                    }

                    try
                    {
                        communityVisualPacksController = plugin.CreateCommunityVisualPacksFullscreenController();
                        if (communityVisualPacksController?.Control == null)
                        {
                            throw new InvalidOperationException("Community Packs Fullscreen view could not be created.");
                        }

                        communityVisualPacksController.PrepareForOpen(packType);
                        window.Content = communityVisualPacksController.Control;

                        // The real page has its own loading state and console zoom/fade animation.
                        // Queue focus after its first layout pass instead of focusing the temporary shell.
                        window.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                communityVisualPacksController?.FocusInitial();
                            }
                            catch
                            {
                            }
                        }), DispatcherPriority.Loaded);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "[AnikiHelper] Failed to build Community Packs Fullscreen view after opening the window.");
                        try { window.Close(); } catch { }
                    }
                }), DispatcherPriority.Background);

                // Modal ownership gives this Hub an independent focus tree and prevents
                // focus from escaping back into Theme Customization while it is open.
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                communityVisualPacksSectionOpen = false;
                try { communityPacksWindow?.Close(); } catch { }
                communityPacksWindow = null;
                try { communityVisualPacksController?.Dispose(); } catch { }
                communityVisualPacksController = null;
                logger.Warn(ex, "[AnikiHelper] Failed to open Community Packs Fullscreen window.");
            }
        }

        private static void OnCommunityPacksWindowContentRendered(object sender, EventArgs e)
        {
            try
            {
                communityVisualPacksController?.FocusInitial();
            }
            catch
            {
            }
        }

        private static void OnCommunityPacksWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e == null || e.IsRepeat)
            {
                return;
            }

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                e.Handled = true;

                try
                {
                    if (communityVisualPacksController?.HandleBackFromWindow() == true)
                    {
                        return;
                    }
                }
                catch
                {
                }

                try
                {
                    (sender as Window)?.Close();
                }
                catch
                {
                }
            }
        }

        private static void OnCommunityPacksWindowClosed(object sender, EventArgs e)
        {
            var closedWindow = sender as Window;
            if (closedWindow != null)
            {
                closedWindow.PreviewKeyDown -= OnCommunityPacksWindowPreviewKeyDown;
                closedWindow.ContentRendered -= OnCommunityPacksWindowContentRendered;
                closedWindow.Closed -= OnCommunityPacksWindowClosed;
            }

            communityVisualPacksSectionOpen = false;
            communityPacksWindow = null;

            try { communityVisualPacksController?.Dispose(); } catch { }
            communityVisualPacksController = null;

            try
            {
                var plugin = global::AnikiHelper.AnikiHelper.Instance;
                plugin?.RefreshCustomVisualPackThemeSettings();
                plugin?.RefreshCustomColorPackThemeSettings();
                plugin?.RefreshLoginPackThemeSettings();
                plugin?.RefreshSoundPackThemeSettings();
                plugin?.RefreshCompletePackThemeSettings();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to refresh Theme Settings after closing Community Packs.");
            }

            try
            {
                closedWindow?.Owner?.Activate();
            }
            catch
            {
            }
        }

        private static string GetCommunityPackTypeForPresetGroup(string presetGroupId)
        {
            switch ((presetGroupId ?? string.Empty).Trim())
            {
                // New unified Community Hub. It opens on Complete Packs by default.
                // The legacy per-pack parameters remain supported for compatibility
                // with older theme/plugin combinations and open the matching tab.
                case "CommunityHub": return "complete";
                case "VisualPack": return "visual";
                case "Interface": return "color";
                case "LoginBackground": return "login";
                case "SoundPack": return "sound";
                case "CompletePack": return "complete";
                default: return null;
            }
        }

        public static void ReturnFromCommunityVisualPacks()
        {
            try
            {
                if (communityPacksWindow != null)
                {
                    communityPacksWindow.Close();
                    return;
                }

                // Legacy fallback for older theme/plugin combinations that still host
                // Community Packs as a hidden Settings section.
                communityVisualPacksSectionOpen = false;
                var plugin = global::AnikiHelper.AnikiHelper.Instance;
                plugin?.RefreshCustomVisualPackThemeSettings();
                plugin?.RefreshCustomColorPackThemeSettings();
                plugin?.RefreshLoginPackThemeSettings();
                plugin?.RefreshSoundPackThemeSettings();
                plugin?.RefreshCompletePackThemeSettings();

                if (currentSettingsContext == null || currentThemeSettingsSectionKey <= 0)
                {
                    return;
                }

                dynamic ctx = currentSettingsContext;
                ctx.OpenSectionCommand.Execute(currentThemeSettingsSectionKey.ToString());
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to return from Community Packs Fullscreen window.");
            }
        }

        private static void OnSettingsWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            // Handle Up/Down ourselves for Random Pool screens, INCLUDING key-repeat.
            // Without this, the first press stays in the correct column but a held direction
            // falls back to WPF's default navigation and starts moving left/right.
            if ((loginRandomPoolSectionOpen || themeColorRandomPoolSectionOpen || visualPackRandomPoolSectionOpen) &&
                (e.Key == Key.Up || e.Key == Key.Down) &&
                TryHandleRandomPoolVerticalNavigation(e.Key))
            {
                e.Handled = true;
                return;
            }

            // Back/Escape should still fire only once per physical press.
            if (e.IsRepeat)
            {
                return;
            }

            if (e.Key != Key.Escape && e.Key != Key.Back)
            {
                return;
            }

            if (loginRandomPoolSectionOpen)
            {
                e.Handled = true;
                ReturnFromLoginRandomPool();
                return;
            }

            if (themeColorRandomPoolSectionOpen)
            {
                e.Handled = true;
                ReturnFromThemeColorRandomPool();
                return;
            }

            if (visualPackRandomPoolSectionOpen)
            {
                e.Handled = true;
                ReturnFromVisualPackRandomPool();
                return;
            }

            // The modern Community Hub is its own modal window and handles Back itself.
            // Keep this branch only for the legacy hidden-settings-section fallback.
            if (communityVisualPacksSectionOpen && communityPacksWindow == null)
            {
                e.Handled = true;
                ReturnFromCommunityVisualPacks();
            }
        }

        private static bool TryHandleRandomPoolVerticalNavigation(Key key)
        {
            try
            {
                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused == null)
                {
                    return false;
                }

                Button currentButton = null;
                var current = focused;

                // Resolve either a pool card OR one of the top action buttons.
                while (current != null)
                {
                    if (current is Button button)
                    {
                        currentButton = button;
                        break;
                    }

                    current = VisualTreeHelper.GetParent(current);
                }

                if (currentButton == null)
                {
                    return false;
                }

                var root = FindParentUserControl(currentButton);
                if (root == null)
                {
                    return false;
                }

                var rootElement = root as UIElement;

                Func<Button, Point> getPosition = button =>
                {
                    try
                    {
                        return rootElement != null
                            ? button.TranslatePoint(new Point(0, 0), rootElement)
                            : new Point(0, 0);
                    }
                    catch
                    {
                        return new Point(0, 0);
                    }
                };

                // UniformGrid is two columns. Sorting by visual Y then X gives:
                // row 1 left, row 1 right, row 2 left, row 2 right...
                var candidateButtons = root
                    .FindVisualChildren<Button>()
                    .Where(button =>
                        button.IsVisible &&
                        button.IsEnabled &&
                        string.Equals(button.Tag as string, "RandomPoolCandidate", StringComparison.Ordinal))
                    .Select(button => new
                    {
                        Button = button,
                        Position = getPosition(button)
                    })
                    .OrderBy(item => Math.Round(item.Position.Y / 10d) * 10d)
                    .ThenBy(item => item.Position.X)
                    .Select(item => item.Button)
                    .ToList();

                // The only non-candidate Buttons inside Random Pool views are the two
                // top actions (Select all / Clear all). Sort left-to-right so column 0
                // maps to Select all and column 1 maps to Clear all.
                var actionButtons = root
                    .FindVisualChildren<Button>()
                    .Where(button =>
                        button.IsVisible &&
                        button.IsEnabled &&
                        !string.Equals(button.Tag as string, "RandomPoolCandidate", StringComparison.Ordinal))
                    .Select(button => new
                    {
                        Button = button,
                        Position = getPosition(button)
                    })
                    .OrderBy(item => item.Position.Y)
                    .ThenBy(item => item.Position.X)
                    .Select(item => item.Button)
                    .ToList();

                Action<Button> focusButton = target =>
                {
                    if (target == null)
                    {
                        return;
                    }

                    target.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            target.Focus();
                            Keyboard.Focus(target);
                            target.BringIntoView();
                        }
                        catch
                        {
                        }
                    }), DispatcherPriority.Input);
                };

                var candidateIndex = candidateButtons.IndexOf(currentButton);
                if (candidateIndex >= 0)
                {
                    if (key == Key.Down)
                    {
                        var targetIndex = candidateIndex + 2;

                        // Bottom of the column: consume the key so WPF cannot jump sideways.
                        if (targetIndex >= candidateButtons.Count)
                        {
                            return true;
                        }

                        focusButton(candidateButtons[targetIndex]);
                        return true;
                    }

                    if (key == Key.Up)
                    {
                        var targetIndex = candidateIndex - 2;
                        if (targetIndex >= 0)
                        {
                            focusButton(candidateButtons[targetIndex]);
                            return true;
                        }

                        // First row -> top action in the matching column.
                        // Left card -> Select all, right card -> Clear all.
                        if (actionButtons.Count > 0)
                        {
                            var column = candidateIndex % 2;
                            var actionIndex = Math.Min(column, actionButtons.Count - 1);
                            focusButton(actionButtons[actionIndex]);
                        }

                        return true;
                    }

                    return false;
                }

                var actionButtonIndex = actionButtons.IndexOf(currentButton);
                if (actionButtonIndex >= 0)
                {
                    if (key == Key.Down)
                    {
                        // Top action -> first card in the same visual column.
                        if (candidateButtons.Count > 0)
                        {
                            var targetIndex = Math.Min(actionButtonIndex, 1);
                            if (targetIndex >= candidateButtons.Count)
                            {
                                targetIndex = 0;
                            }

                            focusButton(candidateButtons[targetIndex]);
                        }

                        return true;
                    }

                    if (key == Key.Up)
                    {
                        // Already at the top: consume the direction instead of letting
                        // WPF escape to another control/window.
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void TextInput(AnikiThemeVariable input)
        {
            if (input == null)
            {
                return;
            }

            try
            {
                var assembly = Application.Current.GetType().Assembly;
                var type = assembly.GetType("Playnite.FullscreenApp.Windows.TextInputWindow");

                if (type == null)
                {
                    logger.Warn("[AnikiHelper] TextInputWindow type not found.");
                    return;
                }

                dynamic inputWindow = Activator.CreateInstance(type);

                var oldValue = input.CurrentStringValue ?? string.Empty;
                var title = input.DisplayName ?? input.Id ?? string.Empty;

                Window owner = Application.Current.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow;

                dynamic result = inputWindow.ShowInput(
                    owner,
                    title,
                    "",
                    oldValue);

                if (result != null && result.Result)
                {
                    input.CurrentStringValue = result.SelectedString;
                }
                else
                {
                    input.CurrentStringValue = oldValue;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to open Playnite fullscreen text input.");
            }
        }

        private static void HookComboBoxNavigation(UserControl control)
        {
            if (control == null)
            {
                return;
            }

            control.AddHandler(
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(OnAnikiThemeSettingsPreviewKeyDown),
                true);
        }

        private static void HookAnikiThemeSettingsPreview(UserControl control)
        {
            if (control == null)
            {
                return;
            }

            // ThemeOptions-like behavior:
            // Preview is shown only when a ComboBox item is focused inside the opened dropdown.
            // The closed ComboBox itself does not show a preview.
            control.AddHandler(
                Keyboard.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnAnikiThemeSettingsPreviewGotFocus),
                true);

            control.AddHandler(
                Keyboard.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnAnikiThemeSettingsPreviewLostFocus),
                true);
        }

        private static void OnAnikiThemeSettingsPreviewGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                string preview = null;

                var source = e.NewFocus as DependencyObject;

                // Keep normal Playnite/WPF scrolling everywhere except at the two edges.
                AdjustScrollForFirstOrLastSetting(source);

                // 1. ComboBox item ouvert : preview du preset item.
                var comboBoxItem = FindParentComboBoxItem(source);
                if (comboBoxItem != null)
                {
                    preview = GetPreviewFromDataContext(comboBoxItem.DataContext);
                }
                else
                {
                    // 2. ComboBox fermée : pas de preview.
                    var comboBox = FindParentComboBox(source);
                    if (comboBox != null)
                    {
                        preview = null;
                    }
                    // 3. Checkbox / Slider / Button : preview de la variable.
                    else if (e.NewFocus is FrameworkElement element)
                    {
                        preview = GetPreviewFromDataContext(element.DataContext);
                    }
                }

                if (global::AnikiHelper.AnikiHelper.Instance?.Settings != null)
                {
                    global::AnikiHelper.AnikiHelper.Instance.Settings.AnikiThemeSettingsPreviewImage = preview;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to show Aniki Theme Settings preview.");
            }
        }

        private static void OnAnikiThemeSettingsPreviewLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                // If focus moves to another ComboBoxItem, keep the preview alive.
                var newComboBoxItem = FindParentComboBoxItem(e.NewFocus as DependencyObject);

                if (newComboBoxItem != null)
                {
                    return;
                }

                if (global::AnikiHelper.AnikiHelper.Instance?.Settings != null)
                {
                    global::AnikiHelper.AnikiHelper.Instance.Settings.AnikiThemeSettingsPreviewImage = null;
                }

            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to clear Aniki Theme Settings preview.");
            }
        }

        private static ComboBoxItem FindParentComboBoxItem(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ComboBoxItem comboBoxItem)
                {
                    return comboBoxItem;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static string GetPreviewFromDataContext(object data)
        {
            if (data is AnikiPresetItem preset)
            {
                return preset.Preview;
            }

            if (data is AnikiThemeChoiceItem choice)
            {
                return choice.Preview;
            }

            if (data is AnikiThemeVariable variable)
            {
                return variable.Preview;
            }

            return null;
        }

        private static void OnAnikiThemeSettingsPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                var source = e.OriginalSource as DependencyObject;

                // Prevent focus from escaping / looping above the first setting control.
                if (e.Key == Key.Up)
                {
                    var currentControl = FindParentSettingNavigationControl(source);

                    if (currentControl != null)
                    {
                        var root = FindParentUserControl(currentControl);
                        var controls = GetSettingNavigationControls(root);
                        var index = controls.IndexOf(currentControl);

                        if (index == 0)
                        {
                            e.Handled = true;
                            return;
                        }
                    }
                }

                var comboBox = FindParentComboBox(source);

                if (comboBox == null)
                {
                    return;
                }

                // If dropdown is open, let the ComboBox handle Up/Down normally.
                if (comboBox.IsDropDownOpen)
                {
                    if (e.Key == Key.Escape || e.Key == Key.Back)
                    {
                        comboBox.IsDropDownOpen = false;
                        e.Handled = true;
                    }

                    return;
                }

                // Open only with validation.
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    comboBox.IsDropDownOpen = true;
                    e.Handled = true;
                    return;
                }

                // When closed, arrows must leave the ComboBox.
                if (e.Key == Key.Down || e.Key == Key.Right)
                {
                    FocusNextControl(comboBox, forward: true);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Up || e.Key == Key.Left)
                {
                    FocusNextControl(comboBox, forward: false);
                    e.Handled = true;
                    return;
                }

                // Prevent focus from escaping / looping above the first setting control.
                if (e.Key == Key.Up)
                {
                    var currentControl = FindParentSettingNavigationControl(e.OriginalSource as DependencyObject);

                    if (currentControl != null)
                    {
                        var root = FindParentUserControl(currentControl);
                        var controls = GetSettingNavigationControls(root);
                        var index = controls.IndexOf(currentControl);

                        if (index == 0)
                        {
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper] Failed to handle Aniki Theme Settings ComboBox navigation.");
            }
        }

        private static void AdjustScrollForFirstOrLastSetting(DependencyObject source)
        {
            try
            {
                // Do not interfere with the items inside an opened ComboBox.
                if (FindParentComboBoxItem(source) != null)
                {
                    return;
                }

                var currentControl = FindParentSettingNavigationControl(source);

                if (currentControl == null)
                {
                    return;
                }

                var root = FindParentUserControl(currentControl);

                if (root == null)
                {
                    return;
                }

                // This list contains only visible + enabled settings.
                // That means if dependent settings below are greyed out,
                // the last enabled setting is still considered the last navigable one.
                var controls = GetSettingNavigationControls(root);
                var index = controls.IndexOf(currentControl);

                if (index < 0 || controls.Count == 0)
                {
                    return;
                }

                var isFirst = index == 0;
                var isLast = index == controls.Count - 1;

                // Important: do not touch scrolling for any option in between.
                if (!isFirst && !isLast)
                {
                    return;
                }

                var scrollViewer = FindParentScrollViewer(currentControl);

                if (scrollViewer == null)
                {
                    return;
                }

                // Let the standard BringIntoView finish first.
                currentControl.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var targetOffset = scrollViewer.VerticalOffset;

                        if (isFirst)
                        {
                            // Reveal the section title above the first option.
                            targetOffset -= 105d;
                        }
                        else if (isLast)
                        {
                            // Reveal a little more content below the last available option.
                            targetOffset += 145d;
                        }

                        targetOffset = Math.Max(
                            0d,
                            Math.Min(scrollViewer.ScrollableHeight, targetOffset));

                        if (Math.Abs(targetOffset - scrollViewer.VerticalOffset) > 1d)
                        {
                            scrollViewer.ScrollToVerticalOffset(targetOffset);
                        }
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.ContextIdle);
            }
            catch
            {
            }
        }

        private static ScrollViewer FindParentScrollViewer(DependencyObject source)
        {
            try
            {
                var current = source;

                while (current != null)
                {
                    if (current is ScrollViewer scrollViewer)
                    {
                        return scrollViewer;
                    }

                    current = VisualTreeHelper.GetParent(current);
                }
            }
            catch
            {
            }

            return null;
        }

        private static ComboBox FindParentComboBox(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ComboBox comboBox)
                {
                    return comboBox;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static Control FindParentSettingNavigationControl(DependencyObject source)
        {
            while (source != null)
            {
                if (source is Control control && IsRealSettingNavigationControl(control))
                {
                    return control;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static void FocusNextControl(Control currentControl, bool forward)
        {
            if (currentControl == null)
            {
                return;
            }

            var root = FindParentUserControl(currentControl);

            if (root == null)
            {
                return;
            }

            // Important: use the same visually-sorted settings list as the rest of the
            // Theme Customization navigation. The old visual-tree order grouped preset
            // ComboBoxes before Boolean options, so in Appearance navigation could jump
            // from Avatar directly to Login screen background and skip the two toggles
            // displayed between them.
            var focusableControls = GetSettingNavigationControls(root);

            if (focusableControls.Count == 0)
            {
                return;
            }

            var currentIndex = focusableControls.IndexOf(currentControl);

            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = forward
                ? currentIndex + 1
                : currentIndex - 1;

            if (nextIndex >= focusableControls.Count)
            {
                nextIndex = 0;
            }

            if (nextIndex < 0)
            {
                nextIndex = focusableControls.Count - 1;
            }

            var nextControl = focusableControls[nextIndex];

            nextControl.Dispatcher.BeginInvoke(new Action(() =>
            {
                nextControl.Focus();
                Keyboard.Focus(nextControl);
            }), DispatcherPriority.Input);
        }

        private static UserControl FindParentUserControl(DependencyObject source)
        {
            while (source != null)
            {
                if (source is UserControl userControl)
                {
                    return userControl;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static void EnsureEnglishFallbackResources()
        {
            TryAddFallback("AppTitle", "Aniki Helper");
            TryAddFallback("AppInfo", "Quick settings for Aniki Helper.");
            TryAddFallback("LOCAnikiThemeSettingsTitle", "Theme Settings");
            TryAddFallback("LOCAnikiThemeSettingsBack", "← Back");
            TryAddFallback("LOCAnikiThemeSettingsAdvanced", "Advanced");
            TryAddFallback("GroupGeneral", "General");
            TryAddFallback("GroupNews", "News");
            TryAddFallback("GroupDynamicColors", "Dynamic colors");

            TryAddFallback("WelcomeHubStartup_Title", "Home screen");
            TryAddFallback("WelcomeHubStartup_Enable", "Open the home screen on startup");
            TryAddFallback("WelcomeHubStartup_Help", "Shows the Aniki home screen when Playnite fullscreen starts.");

            TryAddFallback("GroupLibraryStats", "Library statistics");
            TryAddFallback("IncludeHidden", "Include hidden games");
            TryAddFallback("StatsHelp", "Includes hidden games when calculating library totals and profile statistics.");

            TryAddFallback("GroupDynColors", "Dynamic colors");
            TryAddFallback("L_EnableDynamicAutoPrecache", "Pre-load dynamic colors in the background");
            TryAddFallback("L_EnableDynamicAutoPrecache_Note", "Builds the dynamic color cache in the background to reduce delays while browsing.");

            TryAddFallback("GroupOverlay", "In-game overlay");
            TryAddFallback("InGameOverlay_Enable", "Enable in-game overlay");
            TryAddFallback("InGameOverlay_Enable_Help", "Displays a quick action overlay on top of the current game.");
            TryAddFallback("InGameOverlay_Hotkey_Title", "Overlay shortcut");
            TryAddFallback("InGameOverlay_Hotkey_Help", "Choose the keyboard shortcut used to open the in-game overlay. Restart Playnite after changing this setting.");
            TryAddFallback("LOCInGameOverlaySession", "Session");

            TryAddFallback("LOCInGameOverlaySource", "Source");
            TryAddFallback("LOCInGameOverlayPlatform", "Platform");
            TryAddFallback("LOCInGameOverlayPlaytime", "Playtime");
            TryAddFallback("LOCInGameOverlaySession", "Session");

            TryAddFallback("LOCInGameOverlayFooterTitle", "Aniki Overlay");
            TryAddFallback("LOCInGameOverlayNoGameRunning", "No game running");
            TryAddFallback("LOCInGameOverlayNoActiveGame", "No active game detected");
            TryAddFallback("LOCInGameOverlayLessThanOneMinute", "less than 1 min");
            TryAddFallback("LOCInGameOverlayQuitDialogTitle", "Quit {0}?");
            TryAddFallback("LOCInGameOverlayQuitDialogMessage", "This will try to close the active game window.");

            TryAddFallback("LOCInGameOverlayCancel", "Cancel");
            TryAddFallback("LOCInGameOverlayConfirmQuit", "Quit Game");

            TryAddFallback("InGameOverlay_ControllerShortcut_Title", "Controller shortcut");
            TryAddFallback("InGameOverlay_ControllerShortcut_Help", "Choose the controller shortcut used to open the in-game overlay. Guide button support is experimental and may be captured by Steam, Windows, or controller tools.");
            TryAddFallback("InGameOverlay_ControllerShortcut_StartBack", "Start + Back");
            TryAddFallback("InGameOverlay_ControllerShortcut_BackY", "Back + Y");
            TryAddFallback("InGameOverlay_ControllerShortcut_Guide", "Guide button");
            TryAddFallback("InGameOverlay_ControllerShortcut_Disabled", "Disabled");
            TryAddFallback("InGameOverlay_GameBehavior_Title", "Game behavior while overlay is open");
            TryAddFallback("InGameOverlay_GameBehavior_DoNothing", "Default: Do nothing");
            TryAddFallback("InGameOverlay_GameBehavior_SuspendGame", "Experimental: Suspend game while overlay is open");
            TryAddFallback("InGameOverlay_GameBehavior_Help", "Choose whether Aniki Helper should leave the game running normally or temporarily freeze the game process while the overlay is open to avoid double inputs.");
            TryAddFallback("InGameOverlay_GameBehavior_Warning", "Warning: not recommended for online games, multiplayer games, anti-cheat protected games, or games launched as administrator.");
            TryAddFallback("InGameOverlay_NeverSuspendList_Title", "Never suspend games");
            TryAddFallback("InGameOverlay_NeverSuspendList_Help", "Games added from right-click → Aniki Helper → Overlay Settings → Never suspend this game appear here. Uncheck a game to remove it from the never suspend list.");
            TryAddFallback("InGameOverlay_NeverSuspendList_Clear", "Clear never suspend list");
            TryAddFallback("LOCAnikiHelperOverlayNeverSuspendThisGame", "Never suspend this game");
            TryAddFallback("LOCAnikiHelperOverlayAllowSuspendThisGame", "Allow suspend for this game");

            TryAddFallback("LOCInGameOverlayCurrentGame", "Current game");
            TryAddFallback("LOCInGameOverlayQuickActions", "QUICK ACTIONS");
            TryAddFallback("LOCInGameOverlayResumeGame", "Resume Game");
            TryAddFallback("LOCInGameOverlayReturnToPlaynite", "Return to Playnite");
            TryAddFallback("LOCInGameOverlayQuitGame", "Quit Game");
            TryAddFallback("LOCInGameOverlayGameInfo", "GAME INFO");
            TryAddFallback("LOCInGameOverlayBack", "Back");

            TryAddFallback("MediaGallery_Title", "Capture gallery");
            TryAddFallback("MediaGallery_Desc", "Aniki Helper reads media from Screenshots Visualizer and Screenshot Utilities. Configure your screenshot folders in those plugins; Aniki Helper only reads their data and generates thumbnails for faster navigation in the theme.");
            TryAddFallback("MediaGallery_GenerateThumbnails", "Media thumbnails");
            TryAddFallback("MediaGallery_GenerateThumbnails_Button", "Generate thumbnails");
            TryAddFallback("MediaGallery_GenerateThumbnails_Help", "Pre-generates thumbnails for all images found through the supported screenshot plugins. The first generation can take a while, but future openings should be faster.");

            TryAddFallback("Video", "Intro and outro videos");
            TryAddFallback("StartupIntro_Enable", "Enable intro video");
            TryAddFallback("IntroVideo_Help", "Shows the startup video when entering fullscreen.");
            TryAddFallback("ShutdownVideo_Enable", "Enable outro video");
            TryAddFallback("ShutdownVideo_Help", "Shows the shutdown video when leaving fullscreen or closing Playnite.");

            TryAddFallback("SplashScreen", "Splash screen");
            TryAddFallback("GameLaunchSplash_Title", "Game launch screen");
            TryAddFallback("GameLaunchSplash_Enable", "Enable game splash screen");
            TryAddFallback("GameLaunchSplash_Help", "Shows a launch splash screen when starting a game.");

            TryAddFallback("GameLaunchSplash_Mode_Title", "Splash selection mode");
            TryAddFallback("GameLaunchSplash_Mode_Automatic", "Game priority");
            TryAddFallback("GameLaunchSplash_Mode_CustomPriority", "Custom priority");
            TryAddFallback("GameLaunchSplash_Mode_AlwaysSource", "Source priority");
            TryAddFallback("GameLaunchSplash_Mode_AlwaysPlatform", "Platform priority");
            TryAddFallback("GameLaunchSplash_Mode_AlwaysGlobal", "Global only");
            TryAddFallback("GameLaunchSplash_Mode_Help", "Choose which splash screen should be used when launching a game.");

            TryAddFallback("GameLaunchSplash_ShowLogo", "Show game logo on splash screen");
            TryAddFallback("GameLaunchSplash_LogoPosition_Title", "Logo position");
            TryAddFallback("GameLaunchSplash_LogoPosition_Help", "Choose where the game logo appears on the splash screen.");
            TryAddFallback("GameLaunchSplash_LogoPosition_LeftTop", "Left top");
            TryAddFallback("GameLaunchSplash_LogoPosition_LeftCenter", "Left center");
            TryAddFallback("GameLaunchSplash_LogoPosition_LeftBottom", "Left bottom");
            TryAddFallback("GameLaunchSplash_LogoPosition_CenterTop", "Center top");
            TryAddFallback("GameLaunchSplash_LogoPosition_Center", "Center");
            TryAddFallback("GameLaunchSplash_LogoPosition_CenterBottom", "Center bottom");
            TryAddFallback("GameLaunchSplash_LogoPosition_RightTop", "Right top");
            TryAddFallback("GameLaunchSplash_LogoPosition_RightCenter", "Right center");
            TryAddFallback("GameLaunchSplash_LogoPosition_RightBottom", "Right bottom");

            TryAddFallback("GameLaunchSplash_MinDuration_Title", "Minimum display duration");
            TryAddFallback("GameLaunchSplash_MinDuration_Help", "Defines how long the splash screen stays visible at minimum.");
            TryAddFallback("GameLaunchSplash_AutoDetectReady_Enable", "Auto-detect when the game window is ready");
            TryAddFallback("GameLaunchSplash_AutoDetectReady_Help", "Keeps the splash visible until the real game window is detected. Per-game custom timers still have priority.");
            TryAddFallback("GameLaunchSplash_MaxDuration_Title", "Auto-close safety timer");
            TryAddFallback("GameLaunchSplash_MaxDuration_Help", "Defines the maximum time Aniki Helper waits for the real game window before closing automatically.");

            TryAddFallback("GameLaunchSplash_VideoEndBehavior_Title", "When a splash video ends early");
            TryAddFallback("GameLaunchSplash_VideoEndBehavior_Help", "Choose what is shown if the video is shorter than the splash timer.");
            TryAddFallback("GameLaunchSplash_VideoEndBehavior_ShowGameBackground", "Show the game background");
            TryAddFallback("GameLaunchSplash_VideoEndBehavior_KeepLastFrame", "Keep the last video frame");

            TryAddFallback("GameLaunchSplash_VideoSound_Enable", "Enable splash video sound");
            TryAddFallback("GameLaunchSplash_VideoSound_Help", "Controls audio only for video splash screens.");
            TryAddFallback("GameLaunchSplash_VideoVolume_Title", "Video volume");

            TryAddFallback("EventSounds_Title", "Event sounds");
            TryAddFallback("EventSounds_Enable", "Enable event sounds");
            TryAddFallback("EventSounds_Help", "Plays Aniki Helper sounds for supported events.");

            TryAddFallback("GroupSteamFeatures", "Steam features");
            TryAddFallback("SteamUpdates_Enable", "Enable update checks");
            TryAddFallback("SteamUpdates_Enable_Help", "Checks Steam update data so the theme can display recent updates.");
            TryAddFallback("SteamPlayers_Enable", "Enable current player count");
            TryAddFallback("SteamPlayers_Help", "Displays live player count data for supported Steam games.");
            TryAddFallback("SteamStore_Enable", "Enable Steam Store data");
            TryAddFallback("SteamStore_Enable_Help", "Allows the home screen to display Steam Store deals and sections.");

            TryAddFallback("SteamStore_Language_Title", "Store language");
            TryAddFallback("SteamStore_Language_Desc", "Language used for Steam Store data.");
            TryAddFallback("SteamStore_Country_Title", "Store country / currency");
            TryAddFallback("SteamStore_Country_Desc", "Country and currency used for Steam Store prices.");

            TryAddFallback("SteamAccount_GroupTitle", "Steam account connection");
            TryAddFallback("SteamAccount_GroupDesc", "Connect once to use Steam Friends, My Wishlist, For You and personalized Steam recommendations.");
            TryAddFallback("SteamAccount_ConnectButton", "Connect to Steam");
            TryAddFallback("SteamAccount_CheckButton", "Check connection");
            TryAddFallback("SteamAccount_DisconnectButton", "Disconnect");
            TryAddFallback("SteamAccount_ConnectedTitle", "Connected to Steam");
            TryAddFallback("SteamAccount_DisconnectedTitle", "Not connected to Steam");
            TryAddFallback("SteamAccount_PrivacyNotice", "No Steam Web API key is required, and your Steam friends list does not need to be public.");
            TryAddFallback("SteamFullscreen_DisconnectedSummary", "Connect once to enable Steam Friends, My Wishlist, For You and personalized recommendations.");
            TryAddFallback("SteamFullscreen_PrivacyHelp", "No API key required. Your Steam friends list can stay private.");
            TryAddFallback("SteamFullscreen_ServicesLabel", "Steam services");
            TryAddFallback("SteamFullscreen_ServicesReady", "Ready");
            TryAddFallback("SteamFullscreen_ServicesMissing", "Refresh needed");
            TryAddFallback("SteamFullscreen_ConnectedSummary", "If Steam expires the session, use Check connection. You can reconnect at any time without creating an API key.");
            TryAddFallback("LOCSteamFriends_Title", "Steam Friends");
            TryAddFallback("LOCSteamFriends_Subtitle", "Shows your Steam friends status inside the Aniki theme.");
            TryAddFallback("LOCSteamFriends_Enable", "Enable Steam Friends integration");
            TryAddFallback("LOCSteamFriends_EnableHelp", "Turns the Steam Friends data used by the theme on or off.");
            TryAddFallback("LOCSteamFriends_ShowOffline", "Show offline friends");
            TryAddFallback("LOCSteamFriends_OfflineInfo", "Offline friends are optional and limited to keep navigation smooth.");
            TryAddFallback("LOCSteamFriends_NotifyOnGameStart", "Notify when a friend starts a game");
            TryAddFallback("LOCSteamFriends_NotifyOnGameStartHelp", "Shows an in-theme notification when a Steam friend starts playing.");
            TryAddFallback("LOCSteamFriends_NotifyOnConnect", "Notify when a friend comes online");
            TryAddFallback("LOCSteamFriends_NotifyOnConnectHelp", "Shows an in-theme notification when a Steam friend comes online.");
            TryAddFallback("SteamFullscreen_AccountLabel", "Steam account linked");
            TryAddFallback("SteamFullscreen_SteamIdLabel", "SteamID detected or configured");
            TryAddFallback("SteamFullscreen_ApiKeyLabel", "Steam Web API key");

            TryAddFallback("SteamFullscreen_StatusReady", "Configured");
            TryAddFallback("SteamFullscreen_StatusMissing", "Missing");

            TryAddFallback(
                "SteamFullscreen_ConfigurationReady",
                "Steam configuration is complete.");

            TryAddFallback(
                "SteamFullscreen_ConfigurationMissing",
                "Steam configuration is incomplete. Switch to Desktop Mode, then open the Aniki Helper settings to connect your Steam account or enter the missing information.");

            TryAddFallback("GroupAnikiFeatures", "Aniki news");
            TryAddFallback("NewsScan_Enable", "Enable news");
            TryAddFallback("NewsScan_Enable_Help", "Allows Aniki Helper to fetch news from the configured sources.");

            TryAddFallback("GroupAdditionalFeatures", "Additional features");
            TryAddFallback("MoreOptionsDesktop", "More options are available in the plugin settings from Playnite desktop mode.");
        }

        private static void EnsureDefaultStyleResources()
        {
            TryAddDefaultStyle("AnikiHelperSettingsScrollViewerStyle", typeof(ScrollViewer), style =>
            {
                style.BasedOn = Application.Current.TryFindResource(typeof(ScrollViewer)) as Style;
                style.Setters.Add(new Setter(ScrollViewer.PanningModeProperty, PanningMode.VerticalOnly));
                style.Setters.Add(new Setter(ScrollViewer.CanContentScrollProperty, false));
                style.Setters.Add(new Setter(Control.FocusableProperty, false));
                style.Setters.Add(new Setter(Control.IsTabStopProperty, false));
                style.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible));
                style.Setters.Add(new Setter(KeyboardNavigation.TabNavigationProperty, KeyboardNavigationMode.Cycle));
                style.Setters.Add(new Setter(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle));
            });

            TryAddDefaultStyle("TextAnikiHelperSettingsStyle", typeof(TextBlock), style =>
            {
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 34d));
                style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2)));
                style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 1200d));
            });

            TryAddDefaultStyle("TextAnikiHelperSettingsStyleSousTitre", typeof(TextBlock), style =>
            {
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 18d));
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromArgb(204, 255, 255, 255))));
                style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85d));
                style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
                style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 900d));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 20)));
            });

            TryAddDefaultStyle("TextAnikiHelperSettingsStyleMini", typeof(TextBlock), style =>
            {
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 15d));
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromArgb(204, 255, 255, 255))));
                style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8d));
                style.Setters.Add(new Setter(TextBlock.LineHeightProperty, 18d));
                style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));
                style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 900d));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, -5, 0, 15)));
            });

            TryAddDefaultStyle("AnikiHelperSettingsHeaderDockPanelStyle", typeof(DockPanel), style =>
            {
                style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
                style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
                style.Setters.Add(new Setter(DockPanel.LastChildFillProperty, false));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 24, 0, 10)));
                style.Setters.Add(new Setter(Control.FocusableProperty, false));
                style.Setters.Add(new Setter(KeyboardNavigation.IsTabStopProperty, false));
            });

            TryAddDefaultStyle("AnikiHelperSettingsSectionTitleSectionStyle", typeof(TextBlock), style =>
            {
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 34d));
                style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 10, 0, 10)));
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            });

            TryAddDefaultStyle("AnikiHelperSettingsSectionTitleOptionComboBoxStyle", typeof(TextBlock), style =>
            {
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 24d));
                style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0)));
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            });

            TryAddDefaultStyle("AnikiHelperSettingsCheckBoxStyle", typeof(CheckBox), style =>
            {
                style.BasedOn = Application.Current.TryFindResource(typeof(CheckBox)) as Style;
                style.Setters.Add(new Setter(Control.FontSizeProperty, 22d));
                style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 4)));
                style.Setters.Add(new Setter(Control.FocusableProperty, true));
                style.Setters.Add(new Setter(KeyboardNavigation.IsTabStopProperty, true));
            });

            TryAddDefaultStyle("AnikiHelperSettingsComboBoxStyle", typeof(ComboBox), style =>
            {
                style.BasedOn = Application.Current.TryFindResource(typeof(ComboBox)) as Style;
                style.Setters.Add(new Setter(Control.FontSizeProperty, 20d));
                style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 320d));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0)));
                style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
                style.Setters.Add(new Setter(Control.FocusableProperty, true));
                style.Setters.Add(new Setter(KeyboardNavigation.IsTabStopProperty, true));
            });

            TryAddDefaultStyle("AnikiHelperSettingsSliderStyle", typeof(Slider), style =>
            {
                style.BasedOn = Application.Current.TryFindResource(typeof(Slider)) as Style;
                style.Setters.Add(new Setter(Control.FocusableProperty, true));
                style.Setters.Add(new Setter(KeyboardNavigation.IsTabStopProperty, true));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0)));
            });

            TryAddDefaultStyle("AnikiHelperSettingsSectionHeaderStyle", typeof(StackPanel), style =>
            {
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 40, 0, 20)));
            });

            TryAddDefaultStyle("AnikiHelperSettingsSectionSeparatorStyle", typeof(Border), style =>
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5)
                };

                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                brush.GradientStops.Add(new GradientStop(Colors.White, 0.5));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));

                style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 2d));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(120, 10, 120, 0)));
                style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6d));
                style.Setters.Add(new Setter(Border.BackgroundProperty, brush));
            });

            // === Aniki Theme Settings style aliases ===
            // These keys can be overridden by the theme.
            // If the theme does not provide them, reuse Aniki Helper / Playnite settings styles.

            TryAddStyleAlias(
                "AnikiThemeSettingsScrollViewerStyle",
                "AnikiHelperSettingsScrollViewerStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsInputBoxStyle",
                "SettingsSectionInputBoxStyle",
                "AnikiHelperSettingsTextBoxStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsTextTitleStyle",
                "TextAnikiHelperSettingsStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsTextSubtitleStyle",
                "TextAnikiHelperSettingsStyleSousTitre");

            TryAddStyleAlias(
                "AnikiThemeSettingsTextMiniStyle",
                "TextAnikiHelperSettingsStyleMini");

            TryAddStyleAlias(
                "AnikiThemeSettingsSectionTitleStyle",
                "AnikiHelperSettingsSectionTitleSectionStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsSectionTextStyle",
                "SettingsSectionText",
                "AnikiHelperSettingsSectionTitleOptionComboBoxStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsHeaderDockPanelStyle",
                "AnikiHelperSettingsHeaderDockPanelStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsHeaderDockPanelMiniStyle",
                "AnikiHelperSettingsHeaderDockPanelStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsSectionHeaderStyle",
                "AnikiHelperSettingsSectionHeaderStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsSeparatorStyle",
                "AnikiHelperSettingsSectionSeparatorStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsComboBoxStyle",
                "SettingsSectionCombobox",
                "AnikiHelperSettingsComboBoxStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsCheckBoxStyle",
                "SettingsSectionCheckbox",
                "AnikiHelperSettingsCheckBoxStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsSliderStyle",
                "SettingsSectionSlider",
                "AnikiHelperSettingsSliderStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsTextBoxStyle",
                "AnikiHelperSettingsTextBoxStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsPreviewImageGridStyle",
                "ThemeOptionsPreviewImageGridStyle");

            TryAddStyleAlias(
                "AnikiThemeSettingsPreviewImageStyle",
                "ThemeOptionsPreviewImageStyle");
        }

        private static void TryAddDefaultStyle(string key, Type targetType, Action<Style> configure)
        {
            try
            {
                if (Application.Current.TryFindResource(key) != null)
                {
                    return;
                }

                var style = new Style(targetType);
                configure(style);
                Application.Current.Resources[key] = style;
            }
            catch
            {
            }
        }

        private static void TryAddStyleAlias(string newKey, params string[] fallbackKeys)
        {
            try
            {
                if (Application.Current.TryFindResource(newKey) != null)
                {
                    return;
                }

                foreach (var fallbackKey in fallbackKeys)
                {
                    if (Application.Current.TryFindResource(fallbackKey) is Style style)
                    {
                        Application.Current.Resources[newKey] = style;
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryAddResourceAlias(string newKey, params string[] fallbackKeys)
        {
            try
            {
                if (Application.Current.TryFindResource(newKey) != null)
                {
                    return;
                }

                foreach (var fallbackKey in fallbackKeys)
                {
                    var resource = Application.Current.TryFindResource(fallbackKey);

                    if (resource != null)
                    {
                        Application.Current.Resources[newKey] = resource;
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryAddFallback(string key, string value)
        {
            try
            {
                if (!Application.Current.Resources.Contains(key))
                {
                    Application.Current.Resources[key] = value;
                }
            }
            catch
            {
            }
        }

        private static void OnLoad(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = sender as DependencyObject;

                if (root == null)
                {
                    return;
                }

                root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    FocusFirstSettingControl(root);
                }), DispatcherPriority.ApplicationIdle);
            }
            catch
            {
            }
        }

        private static void FocusFirstSettingControl(DependencyObject root)
        {
            try
            {
                var firstControl = GetSettingNavigationControls(root).FirstOrDefault();

                if (firstControl == null)
                {
                    return;
                }

                firstControl.Focus();
                Keyboard.Focus(firstControl);
            }
            catch
            {
            }
        }

        private static List<Control> GetSettingNavigationControls(DependencyObject root)
        {
            if (root == null)
            {
                return new List<Control>();
            }

            var rootElement = root as UIElement;

            return root
                .FindVisualChildren<Control>()
                .Where(IsRealSettingNavigationControl)
                .Select(control =>
                {
                    try
                    {
                        var point = rootElement != null
                            ? control.TranslatePoint(new Point(0, 0), rootElement)
                            : new Point(0, 0);

                        return new
                        {
                            Control = control,
                            X = point.X,
                            Y = point.Y
                        };
                    }
                    catch
                    {
                        return new
                        {
                            Control = control,
                            X = 0d,
                            Y = 0d
                        };
                    }
                })
                .OrderBy(item => Math.Round(item.Y / 10d) * 10d)
                .ThenBy(item => item.X)
                .Select(item => item.Control)
                .ToList();
        }

        private static bool IsRealSettingNavigationControl(Control control)
        {
            if (control == null ||
                !control.IsVisible ||
                !control.IsEnabled)
            {
                return false;
            }

            // Ignore controls inside another setting control template.
            // Example: internal buttons inside ComboBoxEx, Slider parts, etc.
            if (HasParentSettingNavigationControl(control))
            {
                return false;
            }

            if (control is Slider)
            {
                return true;
            }

            if (control is ComboBox)
            {
                return true;
            }

            if (control is CheckBox)
            {
                return true;
            }

            if (control is Button)
            {
                return true;
            }

            return false;
        }

        private static bool HasParentSettingNavigationControl(Control control)
        {
            try
            {
                DependencyObject parent = VisualTreeHelper.GetParent(control);

                while (parent != null)
                {
                    if (parent is Slider ||
                        parent is ComboBox ||
                        parent is CheckBox ||
                        parent is Button)
                    {
                        return true;
                    }

                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
            catch
            {
            }

            return false;
        }

        private static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent, string typeName = null)
            where T : DependencyObject
        {
            if (parent == null)
            {
                yield break;
            }

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(parent);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int count = VisualTreeHelper.GetChildrenCount(current);

                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(current, i);

                    if (child is T result && (typeName == null || child.GetType().FullName == typeName))
                    {
                        yield return result;
                    }

                    queue.Enqueue(child);
                }
            }
        }
    }
}