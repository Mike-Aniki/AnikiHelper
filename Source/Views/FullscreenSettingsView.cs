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

namespace AnikiHelperFullscreen.Views
{
    public static class FullscreenSettingsView
    {
        private static readonly ILogger logger = LogManager.GetLogger();

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

        private static void Load(DependencyObject parent)
        {
            EnsureEnglishFallbackResources();
            EnsureDefaultStyleResources();

            if (!(Application.Current.TryFindResource("SettingsMenuAnikiHelperButtonTemplate") is DataTemplate menuTemplate))
            {
                return;
            }

            if (!(parent is Window window))
            {
                return;
            }

            dynamic ctx = window.DataContext;

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

            foreach (var child in stack.Children.OfType<ContentControl>())
            {
                if ((child.Content as string) == "Aniki Helper")
                {
                    return;
                }
            }

            var assembly = Application.Current.GetType().Assembly;

            Type sectionType = assembly.GetType("Playnite.FullscreenApp.Controls.SettingsSections.SettingsSectionControl");
            dynamic hostControl = Activator.CreateInstance(sectionType);

            UserControl control = LoadFullscreenSettingsView();
            control.DataContext = global::AnikiHelper.AnikiHelper.Instance.SettingsVM;

            hostControl.Content = control;

            if (hostControl is FrameworkElement hostElement)
            {
                hostElement.Loaded += OnLoad;
            }

            int nextKey = (sectionViews.Keys as IEnumerable<int>).ToList().Max() + 1;
            sectionViews[nextKey] = hostControl;

            Type buttonExType = assembly.GetType("Playnite.FullscreenApp.Controls.ButtonEx");
            dynamic newBtn = Activator.CreateInstance(buttonExType);

            newBtn.Content = "Aniki Helper";
            newBtn.ContentTemplate = menuTemplate;
            newBtn.Style = Application.Current.TryFindResource("SettingsMenuButton") as Style;
            newBtn.Command = ctx.OpenSectionCommand;
            newBtn.CommandParameter = nextKey.ToString();

            int insertIndex = Math.Min(3, stack.Children.Count);
            stack.Children.Insert(insertIndex, newBtn);
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

        private static void EnsureEnglishFallbackResources()
        {
            TryAddFallback("AppTitle", "Aniki Helper");
            TryAddFallback("AppInfo", "Quick settings for Aniki Helper.");

            TryAddFallback("WelcomeHubStartup_Title", "Home screen");
            TryAddFallback("WelcomeHubStartup_Enable", "Open the home screen on startup");
            TryAddFallback("WelcomeHubStartup_Help", "Shows the Aniki home screen when Playnite fullscreen starts.");

            TryAddFallback("GroupLibraryStats", "Library statistics");
            TryAddFallback("IncludeHidden", "Include hidden games");
            TryAddFallback("StatsHelp", "Includes hidden games when calculating library totals and profile statistics.");

            TryAddFallback("GroupDynColors", "Dynamic colors");
            TryAddFallback("L_EnableDynamicAutoPrecache", "Pre-load dynamic colors in the background");
            TryAddFallback("L_EnableDynamicAutoPrecache_Note", "Builds the dynamic color cache in the background to reduce delays while browsing.");

            TryAddFallback("Video", "Intro and outro videos");
            TryAddFallback("StartupIntro_Enable", "Enable intro video");
            TryAddFallback("IntroVideo_Help", "Shows the startup video when entering fullscreen.");
            TryAddFallback("ShutdownVideo_Enable", "Enable outro video");
            TryAddFallback("ShutdownVideo_Help", "Shows the shutdown video when leaving fullscreen or closing Playnite.");

            TryAddFallback("SplashScreen", "Splash screen");
            TryAddFallback("GameLaunchSplash_Enable", "Enable game splash screen");
            TryAddFallback("GameLaunchSplash_Help", "Shows a launch splash screen when starting a game.");

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
                (sender as DependencyObject)
                    .FindVisualChildren<Control>()
                    .FirstOrDefault(ctrl => ctrl.Focusable && ctrl.IsVisible)
                    ?.Focus();
            }
            catch
            {
            }
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