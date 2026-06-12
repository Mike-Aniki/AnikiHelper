using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnikiHelper.Services.InGameOverlay
{
    internal sealed class AnikiInGameOverlayWindow : Window
    {
        private readonly InGameOverlayService service;

        private Grid panel;
        private TranslateTransform panelTransform;

        private Image gameLogoImage;
        private Border gameLogoContainer;
        private TextBlock gameTitleText;

        private TextBlock sourceValueText;
        private TextBlock platformValueText;
        private TextBlock playtimeValueText;
        private TextBlock sessionValueText;
        private TextBlock mediaCountValueText;
        private TextBlock mediaLastCaptureValueText;
        private TextBlock achievementsUnlockedValueText;
        private TextBlock achievementsProgressValueText;
        private TextBlock footerTitleText;
        private TextBlock clockText;

        private Button returnButton;
        private TextBlock returnButtonIconText;
        private TextBlock returnButtonLabelText;
        private Button quitButton;
        private Button cancelQuitButton;
        private Button confirmQuitButton;
        private Button firstButton;
        private Button controllerFocusedButton;

        private Border latestAchievementCard;
        private Image latestAchievementIconImage;
        private TextBlock latestAchievementTitleText;
        private TextBlock latestAchievementDescriptionText;
        private TextBlock latestAchievementMetaText;

        private Border quitConfirmationPanel;
        private TextBlock quitConfirmationTitleText;
        private TextBlock quitConfirmationMessageText;
        private bool isQuitConfirmationVisible;

        private bool useControllerFocusVisual;
        private DateTime lastControllerNavigationTime = DateTime.MinValue;
        private int lastControllerNavigationDirection = 0;
        private DateTime lastControllerActionTime = DateTime.MinValue;
        private DateTime lastDirectOverlayControllerInputTime = DateTime.MinValue;
        private DispatcherTimer sessionTimer;
        private bool isHiding;

        public AnikiInGameOverlayWindow(InGameOverlayService service)
        {
            this.service = service;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = true;
            ShowInTaskbar = false;
            Focusable = true;
            Opacity = 1;

            BuildUi();
            CreateSessionTimer();

            PreviewKeyDown += OnPreviewKeyDown;

            Loaded += (s, e) =>
            {
                Activate();
                FocusOverlayButton();
            };

            Closed += (s, e) =>
            {
                try
                {
                    sessionTimer?.Stop();
                }
                catch
                {
                }
            };
        }

        public void Refresh()
        {
            RefreshHeader();
            RefreshButtons();
            RefreshInfoValues();

            if (footerTitleText != null)
            {
                footerTitleText.Text = Loc("LOCInGameOverlayFooterTitle", "Aniki Overlay");
            }
        }

        private void RefreshHeader()
        {
            var logoPath = service.CurrentGameLogoPath;
            var hasLogo = false;

            if (!string.IsNullOrWhiteSpace(logoPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bitmap.EndInit();

                    gameLogoImage.Source = bitmap;
                    hasLogo = true;
                }
                catch
                {
                    gameLogoImage.Source = null;
                    hasLogo = false;
                }
            }
            else
            {
                gameLogoImage.Source = null;
            }

            if (hasLogo && service.IsGameRunning)
            {
                gameLogoContainer.Visibility = Visibility.Visible;
                gameTitleText.Visibility = Visibility.Collapsed;
            }
            else
            {
                gameLogoContainer.Visibility = Visibility.Collapsed;
                gameTitleText.Visibility = Visibility.Visible;

                gameTitleText.Text = service.IsGameRunning
                    ? service.CurrentGameName
                    : Loc("LOCInGameOverlayNoGameRunning", "No game running");
            }
        }

        private void RefreshButtons()
        {
            var isRunning = service.IsGameRunning;


            if (quitButton != null)
            {
                quitButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
                quitButton.IsTabStop = isRunning;
                quitButton.Focusable = isRunning;
            }

            if (returnButton != null)
            {
                returnButton.Visibility = Visibility.Visible;
                returnButton.IsTabStop = true;
                returnButton.Focusable = true;
            }

            firstButton = returnButton;

            if (controllerFocusedButton == null ||
                controllerFocusedButton.Visibility != Visibility.Visible ||
                !controllerFocusedButton.IsEnabled)
            {
                controllerFocusedButton = firstButton;
            }

            if (!(Keyboard.FocusedElement is Button))
            {
                useControllerFocusVisual = true;
            }

            RefreshReturnButtonMode();
            UpdateAllButtonVisualStates();
        }

        private void RefreshReturnButtonMode()
        {
            try
            {
                if (returnButtonIconText == null || returnButtonLabelText == null)
                {
                    return;
                }

                if (service.OverlayOpenedFromPlaynite)
                {
                    returnButtonIconText.Text = "↩";
                    returnButtonLabelText.Text = Loc("LOCInGameOverlayReturnToGame", "Return to Game");
                }
                else
                {
                    returnButtonIconText.Text = "⌂";
                    returnButtonLabelText.Text = Loc("LOCInGameOverlayReturnToPlaynite", "Return to Playnite");
                }
            }
            catch
            {
            }
        }

        private void RefreshInfoValues()
        {
            if (!service.IsGameRunning)
            {
                if (sourceValueText != null)
                {
                    sourceValueText.Text = Loc("LOCInGameOverlayNoActiveGame", "No active game detected");
                }

                if (platformValueText != null)
                {
                    platformValueText.Text = "-";
                }

                if (playtimeValueText != null)
                {
                    playtimeValueText.Text = "-";
                }

                if (sessionValueText != null)
                {
                    sessionValueText.Text = "-";
                }

                if (mediaCountValueText != null)
                {
                    mediaCountValueText.Text = "-";
                }

                if (mediaLastCaptureValueText != null)
                {
                    mediaLastCaptureValueText.Text = "-";
                }

                if (achievementsUnlockedValueText != null)
                {
                    achievementsUnlockedValueText.Text = "-";
                }

                if (achievementsProgressValueText != null)
                {
                    achievementsProgressValueText.Text = "-";
                }

                return;
            }

            if (sourceValueText != null)
            {
                sourceValueText.Text = service.CurrentGameSourceName;
            }

            if (platformValueText != null)
            {
                platformValueText.Text = service.CurrentGamePlatformName;
            }

            if (playtimeValueText != null)
            {
                playtimeValueText.Text = service.CurrentGamePlaytimeValue;
            }

            if (sessionValueText != null)
            {
                sessionValueText.Text = service.CurrentGameSessionTimeValue;
            }

            if (mediaCountValueText != null)
            {
                mediaCountValueText.Text = service.CurrentGameMediaCountValue;
            }

            if (mediaLastCaptureValueText != null)
            {
                mediaLastCaptureValueText.Text = service.CurrentGameLatestCaptureValue;
            }

            if (achievementsUnlockedValueText != null)
            {
                achievementsUnlockedValueText.Text = service.CurrentGameAchievementsUnlockedValue;
            }

            if (achievementsProgressValueText != null)
            {
                achievementsProgressValueText.Text = service.CurrentGameAchievementsProgressValue;
            }

            RefreshLatestAchievementCard();
        }

        private void RefreshLatestAchievementCard()
        {
            if (latestAchievementCard == null)
            {
                return;
            }

            if (!service.HasCurrentGameLastAchievement)
            {
                latestAchievementCard.Visibility = Visibility.Collapsed;
                return;
            }

            latestAchievementCard.Visibility = Visibility.Visible;

            if (latestAchievementTitleText != null)
            {
                latestAchievementTitleText.Text = service.CurrentGameLastAchievementValue;
            }

            if (latestAchievementDescriptionText != null)
            {
                latestAchievementDescriptionText.Text = service.CurrentGameLastAchievementDescription;
            }

            if (latestAchievementMetaText != null)
            {
                var percent = service.CurrentGameLastAchievementPercentValue;
                var date = service.CurrentGameLastAchievementDateValue;

                if (!string.IsNullOrWhiteSpace(percent) && !string.IsNullOrWhiteSpace(date))
                {
                    latestAchievementMetaText.Text = percent + " • " + date;
                }
                else if (!string.IsNullOrWhiteSpace(percent))
                {
                    latestAchievementMetaText.Text = percent;
                }
                else
                {
                    latestAchievementMetaText.Text = date;
                }
            }

            if (latestAchievementIconImage != null)
            {
                var iconPath = service.CurrentGameLastAchievementIconPath;

                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();

                        latestAchievementIconImage.Source = bitmap;
                        latestAchievementIconImage.Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        latestAchievementIconImage.Source = null;
                        latestAchievementIconImage.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    latestAchievementIconImage.Source = null;
                    latestAchievementIconImage.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CreateSessionTimer()
        {
            sessionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };

            sessionTimer.Tick += (s, e) =>
            {
                try
                {
                    if (clockText != null)
                    {
                        clockText.Text = DateTime.Now.ToShortTimeString();
                    }

                    if (sessionValueText != null && service.IsGameRunning)
                    {
                        sessionValueText.Text = service.CurrentGameSessionTimeValue;
                    }
                }
                catch
                {
                }
            };

            sessionTimer.Start();
        }

        private string Loc(string key, string fallback)
        {
            try
            {
                var value = System.Windows.Application.Current.TryFindResource(key);

                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                if (value != null)
                {
                    var str = value.ToString();

                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        return str;
                    }
                }
            }
            catch
            {
            }

            return fallback;
        }

        private Brush GetBrushResource(string key, Brush fallback)
        {
            try
            {
                var value = System.Windows.Application.Current.TryFindResource(key);

                if (value is Brush brush)
                {
                    return brush;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private void BuildUi()
        {
            var root = new Grid
            {
                Width = 1920,
                Height = 1080
            };

            var viewbox = new Viewbox
            {
                Stretch = Stretch.UniformToFill,
                Child = root
            };

            Content = viewbox;

            var darkLayer = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                IsHitTestVisible = false
            };

            root.Children.Add(darkLayer);

            clockText = new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm"),
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 34, 46, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            clockText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            System.Windows.Controls.Panel.SetZIndex(clockText, 50);
            root.Children.Add(clockText);

            panelTransform = new TranslateTransform
            {
                X = 0,
                Y = 0
            };

            panel = new Grid
            {
                Width = 562,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                RenderTransform = panelTransform,
                Opacity = 1
            };

            System.Windows.Controls.Panel.SetZIndex(panel, 10);
            root.Children.Add(panel);

            var panelBackground = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                Background = new SolidColorBrush(Color.FromArgb(235, 14, 14, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            };

            panelBackground.SetResourceReference(Border.BackgroundProperty, "OverlayMenu");
            panelBackground.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");
            panel.Children.Add(panelBackground);

            var layout = new Grid();

            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(layout);

            var headerBackground = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(51, 0, 0, 0))
            };

            Grid.SetRow(headerBackground, 0);
            layout.Children.Add(headerBackground);

            var footerBackground = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(51, 0, 0, 0))
            };

            Grid.SetRow(footerBackground, 4);
            layout.Children.Add(footerBackground);

            var header = new Grid
            {
                MinHeight = 120,
                Margin = new Thickness(22, 16, 22, 16)
            };

            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            var headerStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            header.Children.Add(headerStack);

            gameLogoContainer = new Border
            {
                Width = 380,
                Height = 90,
                Background = Brushes.Transparent,
                Visibility = Visibility.Collapsed
            };

            gameLogoImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            gameLogoContainer.Child = gameLogoImage;
            headerStack.Children.Add(gameLogoContainer);

            gameTitleText = new TextBlock
            {
                Text = Loc("LOCInGameOverlayNoGameRunning", "No game running"),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxWidth = 490,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            gameTitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            headerStack.Children.Add(gameTitleText);

            var topSeparator = new Border
            {
                Height = 1,
                Margin = new Thickness(22, 0, 22, 0),
                Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255))
            };

            Grid.SetRow(topSeparator, 1);
            layout.Children.Add(topSeparator);

            var contentGrid = new Grid
            {
                Margin = new Thickness(16, 18, 16, 18)
            };

            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Quick actions title
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Separator
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Game info header
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Game info card
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Media header
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Media card
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Achievements header
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Achievements card
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(contentGrid, 2);
            layout.Children.Add(contentGrid);

            var actionTitle = new TextBlock
            {
                Text = Loc("LOCInGameOverlayQuickActions", "QUICK ACTIONS"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Opacity = 0.42,
                Margin = new Thickness(2, 0, 0, 10),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            actionTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(actionTitle, 0);
            contentGrid.Children.Add(actionTitle);

            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            Grid.SetRow(buttonStack, 1);
            contentGrid.Children.Add(buttonStack);

            returnButton = CreateButton("⌂", Loc("LOCInGameOverlayReturnToPlaynite", "Return to Playnite"), service.ReturnToPlaynite);
            quitButton = CreateButton("⏻", Loc("LOCInGameOverlayQuitGame", "Quit Game"), service.RequestQuitGame);

            buttonStack.Children.Add(returnButton);
            buttonStack.Children.Add(quitButton);

            var premiumSeparator = new Grid
            {
                Height = 44,
                Margin = new Thickness(0, 22, 0, 20)
            };

            premiumSeparator.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            premiumSeparator.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            premiumSeparator.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(premiumSeparator, 2);
            contentGrid.Children.Add(premiumSeparator);

            var sectionHeader = new Grid
            {
                Height = 26,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };

            sectionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sectionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sectionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(sectionHeader, 1);
            premiumSeparator.Children.Add(sectionHeader);

            var sectionAccent = new Border
            {
                Width = 4,
                Height = 22,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(214, 179, 106)),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.35,
                    Color = Color.FromRgb(214, 179, 106)
                }
            };

            Grid.SetColumn(sectionAccent, 0);
            sectionHeader.Children.Add(sectionAccent);

            var sectionTitle = new TextBlock
            {
                Text = Loc("LOCInGameOverlayGameDetails", "GAME DETAILS"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Opacity = 0.72,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 18, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            sectionTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(sectionTitle, 1);
            sectionHeader.Children.Add(sectionTitle);

            var sectionLine = new Border
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(72, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(24, 255, 255, 255), 0.45),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0)
                    }
                }
            };

            Grid.SetColumn(sectionLine, 2);
            sectionHeader.Children.Add(sectionLine);

            var goldUnderline = new Border
            {
                Height = 2,
                Width = 104,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(16, 0, 0, 0),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 214, 179, 106), 0.0),
                        new GradientStop(Color.FromArgb(220, 214, 179, 106), 0.22),
                        new GradientStop(Color.FromArgb(220, 214, 179, 106), 0.78),
                        new GradientStop(Color.FromArgb(0, 214, 179, 106), 1.0)
                    }
                }
            };

            Grid.SetRow(goldUnderline, 2);
            premiumSeparator.Children.Add(goldUnderline);

            var infoHeaderStrip = new Border
            {
                CornerRadius = new CornerRadius(7, 7, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                Padding = new Thickness(12, 8, 12, 8)
            };

            Grid.SetRow(infoHeaderStrip, 3);
            contentGrid.Children.Add(infoHeaderStrip);

            var infoHeaderText = new TextBlock
            {
                Text = Loc("LOCInGameOverlayGameInfo", "GAME INFO"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Opacity = 0.85,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            infoHeaderText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            infoHeaderStrip.Child = infoHeaderText;

            var infoCard = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 7, 7),
                Background = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 14)
            };

            Grid.SetRow(infoCard, 4);
            contentGrid.Children.Add(infoCard);

            var infoGrid = new Grid();

            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            infoCard.Child = infoGrid;

            AddInfoRow(infoGrid, 0, Loc("LOCInGameOverlaySource", "Source"), out sourceValueText);
            AddInfoRow(infoGrid, 1, Loc("LOCInGameOverlayPlatform", "Platform"), out platformValueText);
            AddInfoRow(infoGrid, 2, Loc("LOCInGameOverlayPlaytime", "Playtime"), out playtimeValueText);
            AddInfoRow(infoGrid, 3, Loc("LOCInGameOverlaySession", "Session"), out sessionValueText);

            var mediaHeaderStrip = CreateInfoHeaderStrip(Loc("LOCInGameOverlayMedia", "MEDIA"));
            Grid.SetRow(mediaHeaderStrip, 5);
            contentGrid.Children.Add(mediaHeaderStrip);

            var mediaCard = CreateInfoCard();
            Grid.SetRow(mediaCard, 6);
            contentGrid.Children.Add(mediaCard);

            var mediaGrid = CreateInfoGrid(2);
            mediaCard.Child = mediaGrid;

            AddInfoRow(mediaGrid, 0, Loc("LOCInGameOverlayMediaAvailable", "Captures"), out mediaCountValueText);
            AddInfoRow(mediaGrid, 1, Loc("LOCInGameOverlayMediaLatest", "Latest capture"), out mediaLastCaptureValueText);

            var achievementsHeaderStrip = CreateInfoHeaderStrip(Loc("LOCInGameOverlayAchievements", "ACHIEVEMENTS"));
            Grid.SetRow(achievementsHeaderStrip, 7);
            contentGrid.Children.Add(achievementsHeaderStrip);

            var achievementsCard = CreateInfoCard();
            Grid.SetRow(achievementsCard, 8);
            contentGrid.Children.Add(achievementsCard);

            var achievementsStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            achievementsCard.Child = achievementsStack;

            var achievementsGrid = CreateInfoGrid(2);

            AddInfoRow(achievementsGrid, 0, Loc("LOCInGameOverlayAchievementsUnlocked", "Unlocked"), out achievementsUnlockedValueText);
            AddInfoRow(achievementsGrid, 1, Loc("LOCInGameOverlayAchievementsProgress", "Progress"), out achievementsProgressValueText);

            achievementsStack.Children.Add(achievementsGrid);

            latestAchievementCard = CreateLatestAchievementCard();
            achievementsStack.Children.Add(latestAchievementCard);

            quitConfirmationPanel = CreateQuitConfirmationPanel();
            System.Windows.Controls.Panel.SetZIndex(quitConfirmationPanel, 100);
            root.Children.Add(quitConfirmationPanel);

            var bottomSeparator = new Border
            {
                Height = 1,
                Margin = new Thickness(22, 0, 22, 0),
                Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255))
            };

            Grid.SetRow(bottomSeparator, 3);
            layout.Children.Add(bottomSeparator);

            var footer = new Grid
            {
                Height = 58,
                Margin = new Thickness(22, 0, 22, 0)
            };

            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetRow(footer, 4);
            layout.Children.Add(footer);

            footerTitleText = new TextBlock
            {
                Text = Loc("LOCInGameOverlayFooterTitle", "Aniki Overlay"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            footerTitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            footer.Children.Add(footerTitleText);

            var backStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(backStack, 1);
            footer.Children.Add(backStack);

            var backBadge = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(237, 73, 73)),
                Margin = new Thickness(0, 0, 10, 0)
            };

            backStack.Children.Add(backBadge);

            var backLetter = new TextBlock
            {
                Text = "B",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };

            backBadge.Child = backLetter;

            var backText = new TextBlock
            {
                Text = Loc("LOCInGameOverlayBack", "Back"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            backText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            backStack.Children.Add(backText);

            Refresh();
        }

        private Border CreateInfoHeaderStrip(string title)
        {
            var headerStrip = new Border
            {
                CornerRadius = new CornerRadius(7, 7, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 0)
            };

            var headerText = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Opacity = 0.85,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            headerText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            headerStrip.Child = headerText;

            return headerStrip;
        }

        private Border CreateInfoCard()
        {
            return new Border
            {
                CornerRadius = new CornerRadius(0, 0, 7, 7),
                Background = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        private Border CreateLatestAchievementCard()
        {
            var card = new Border
            {
                Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 6, 0, 0)
            };

            var grid = new Grid();

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            latestAchievementIconImage = new Image
            {
                Width = 62,
                Height = 62,
                Stretch = Stretch.UniformToFill,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = Visibility.Collapsed
            };

            Grid.SetColumn(latestAchievementIconImage, 0);
            grid.Children.Add(latestAchievementIconImage);

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            var label = new TextBlock
            {
                Text = Loc("LOCInGameOverlayAchievementsLatest", "Latest achievement"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Opacity = 0.55,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(label);

            latestAchievementTitleText = new TextBlock
            {
                Text = "-",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            latestAchievementTitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(latestAchievementTitleText);

            latestAchievementDescriptionText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.68,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 38,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            latestAchievementDescriptionText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(latestAchievementDescriptionText);

            latestAchievementMetaText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Opacity = 0.55,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            latestAchievementMetaText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(latestAchievementMetaText);

            card.Child = grid;
            return card;
        }

        private Grid CreateInfoGrid(int rowCount)
        {
            var grid = new Grid();

            for (var i = 0; i < rowCount; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            return grid;
        }

        private void AddInfoRow(Grid parent, int row, string label, out TextBlock valueText)
        {
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.55,
                Margin = new Thickness(0, 0, 10, 10),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(labelText, row);
            Grid.SetColumn(labelText, 0);
            parent.Children.Add(labelText);

            valueText = new TextBlock
            {
                Text = "-",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(valueText, row);
            Grid.SetColumn(valueText, 1);
            parent.Children.Add(valueText);
        }

        private Button CreateButton(string icon, string text, Action action)
        {
            var button = new Button
            {
                Height = 58,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Focusable = true,
                IsTabStop = true,
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(18, 0, 18, 0),
                Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234)),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                FocusVisualStyle = null,
                Template = CreateOverlayButtonTemplate()
            };

            button.SetResourceReference(Control.ForegroundProperty, "TextBrush");

            var themeStyle = TryFindResource("AnikiInGameOverlayButtonStyle") as Style;
            if (themeStyle != null)
            {
                button.Style = themeStyle;
            }

            button.GotKeyboardFocus += (s, e) =>
            {
                useControllerFocusVisual = false;
                UpdateAllButtonVisualStates();
            };

            button.LostKeyboardFocus += (s, e) =>
            {
                UpdateAllButtonVisualStates();
            };

            button.MouseMove += (s, e) =>
            {
                useControllerFocusVisual = false;
                UpdateAllButtonVisualStates();
            };

            button.MouseLeave += (s, e) =>
            {
                UpdateAllButtonVisualStates();
            };

            var contentGrid = new Grid();

            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            iconText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(iconText, 0);
            contentGrid.Children.Add(iconText);

            if (action == service.ReturnToPlaynite)
            {
                returnButtonIconText = iconText;
            }

            var labelText = new TextBlock
            {
                Text = text,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(labelText, 1);
            contentGrid.Children.Add(labelText);

            if (action == service.ReturnToPlaynite)
            {
                returnButtonLabelText = labelText;
            }

            button.Content = contentGrid;

            button.Click += (s, e) =>
            {
                if (button == returnButton)
                {
                    if (service.OverlayOpenedFromPlaynite)
                    {
                        service.ReturnToGame();
                    }
                    else
                    {
                        service.ReturnToPlaynite();
                    }

                    return;
                }

                action?.Invoke();
            };

            UpdateButtonVisualState(button);

            return button;
        }

        private Border CreateQuitConfirmationPanel()
        {
            var panelBorder = new Border
            {
                Visibility = Visibility.Collapsed,
                Width = 560,
                CornerRadius = new CornerRadius(16),
                Background = GetBrushResource("OverlayMenu", new SolidColorBrush(Color.FromArgb(245, 12, 12, 20))),
                BorderBrush = GetBrushResource("MenuBorderBrush", new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(26),
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.55,
                    Color = Colors.Black
                }
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            panelBorder.Child = stack;

            quitConfirmationTitleText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            quitConfirmationTitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(quitConfirmationTitleText);

            quitConfirmationMessageText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 22),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            quitConfirmationMessageText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(quitConfirmationMessageText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            stack.Children.Add(buttons);

            cancelQuitButton = CreateButton("↩", Loc("LOCInGameOverlayCancel", "Cancel"), HideQuitConfirmation);
            confirmQuitButton = CreateButton("⏻", Loc("LOCInGameOverlayConfirmQuit", "Quit Game"), service.ConfirmQuitGame);

            buttons.Children.Add(cancelQuitButton);
            buttons.Children.Add(confirmQuitButton);

            return panelBorder;
        }

        private ControlTemplate CreateOverlayButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });

            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });

            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            presenter.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });

            border.AppendChild(presenter);
            template.VisualTree = border;

            return template;
        }

        private void UpdateButtonVisualState(Button button)
        {
            if (button == null)
            {
                return;
            }

            var isControllerActive = useControllerFocusVisual && button == controllerFocusedButton;
            var isKeyboardOrMouseActive = !useControllerFocusVisual &&
                                          (button.IsKeyboardFocusWithin || button.IsMouseOver);

            var isActive = isControllerActive || isKeyboardOrMouseActive;

            if (isActive)
            {
                button.SetResourceReference(Control.BackgroundProperty, "ControlBackgroundDarkBrush");
                button.SetResourceReference(Control.BorderBrushProperty, "FocusGameBorderBrush");
                button.BorderThickness = new Thickness(3);
            }
            else
            {
                button.Background = Brushes.Transparent;
                button.SetResourceReference(Control.BorderBrushProperty, "NoFocusBorderButtonBrush");
                button.BorderThickness = new Thickness(1);
            }
        }

        private void UpdateAllButtonVisualStates()
        {
            UpdateButtonVisualState(returnButton);
            UpdateButtonVisualState(quitButton);
            UpdateButtonVisualState(cancelQuitButton);
            UpdateButtonVisualState(confirmQuitButton);
        }

        public void ResetQuitConfirmationState()
        {
            try
            {
                isQuitConfirmationVisible = false;

                if (quitConfirmationPanel != null)
                {
                    quitConfirmationPanel.Visibility = Visibility.Collapsed;
                }

                controllerFocusedButton = firstButton;
                useControllerFocusVisual = true;

                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        public void HideImmediately()
        {
            try
            {
                ResetQuitConfirmationState();
                isHiding = false;

                BeginAnimation(Window.OpacityProperty, null);

                if (panel != null)
                {
                    panel.BeginAnimation(UIElement.OpacityProperty, null);
                    panel.Opacity = 1;
                }

                if (panelTransform != null)
                {
                    panelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    panelTransform.X = 0;
                }

                Hide();
            }
            catch
            {
                try
                {
                    Hide();
                }
                catch
                {
                }
            }
        }

        public void PlayShowAnimation()
        {
            try
            {
                isHiding = false;
                Opacity = 1;

                if (panel != null)
                {
                    panel.BeginAnimation(UIElement.OpacityProperty, null);
                    panel.Opacity = 1;
                }

                if (panelTransform != null)
                {
                    panelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    panelTransform.X = 0;
                }

                FocusOverlayButton();
            }
            catch
            {
            }
        }

        public void HideWithAnimation()
        {
            if (isHiding)
            {
                return;
            }

            try
            {
                isHiding = true;

                ResetQuitConfirmationState();

                BeginAnimation(Window.OpacityProperty, null);

                if (panel != null)
                {
                    panel.BeginAnimation(UIElement.OpacityProperty, null);
                    panel.Opacity = 1;
                }

                if (panelTransform != null)
                {
                    panelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    panelTransform.X = 0;
                }

                Hide();
            }
            catch
            {
                try
                {
                    Hide();
                }
                catch
                {
                }
            }
            finally
            {
                isHiding = false;
            }
        }

        public void ShowQuitConfirmation()
        {
            try
            {
                if (quitConfirmationPanel == null)
                {
                    return;
                }

                var title = Loc("LOCInGameOverlayQuitDialogTitle", "Quit {0}?");
                title = string.Format(title, service.CurrentGameName);

                var message = Loc("LOCInGameOverlayQuitDialogMessage", "This will try to close the active game window.");

                if (quitConfirmationTitleText != null)
                {
                    quitConfirmationTitleText.Text = title;
                }

                if (quitConfirmationMessageText != null)
                {
                    quitConfirmationMessageText.Text = message;
                }

                isQuitConfirmationVisible = true;
                quitConfirmationPanel.Visibility = Visibility.Visible;

                controllerFocusedButton = cancelQuitButton;
                useControllerFocusVisual = true;

                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private void HideQuitConfirmation()
        {
            try
            {
                isQuitConfirmationVisible = false;

                if (quitConfirmationPanel != null)
                {
                    quitConfirmationPanel.Visibility = Visibility.Collapsed;
                }

                controllerFocusedButton = quitButton;
                useControllerFocusVisual = true;

                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        public void HandleOverlayControllerInput(ControllerInput button)
        {
            System.Diagnostics.Debug.WriteLine("[AnikiHelper][OverlayWindow] Received: " + button);

            lastControllerActionTime = DateTime.Now;
            lastDirectOverlayControllerInputTime = DateTime.Now;

            switch (button)
            {
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    System.Diagnostics.Debug.WriteLine("[AnikiHelper][OverlayWindow] Move focus UP");
                    MoveForcedControllerFocus(-1);
                    break;

                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    System.Diagnostics.Debug.WriteLine("[AnikiHelper][OverlayWindow] Move focus DOWN");
                    MoveForcedControllerFocus(1);
                    break;

                case ControllerInput.A:
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(180);
                        ClickForcedControllerFocusedButton();
                    }), DispatcherPriority.Background);
                    break;

                case ControllerInput.B:
                case ControllerInput.Back:
                    if (isQuitConfirmationVisible)
                    {
                        HideQuitConfirmation();
                    }
                    else
                    {
                        service.HideOverlay();
                    }
                    break;
            }
        }

        public void HandleControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (args == null || args.State != ControllerInputState.Pressed)
            {
                return;
            }

            lastControllerActionTime = DateTime.Now;

            switch (args.Button)
            {
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveForcedControllerFocus(-1);
                    }
                    break;

                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveForcedControllerFocus(1);
                    }
                    break;

                case ControllerInput.A:
                    ClickForcedControllerFocusedButton();
                    break;

                case ControllerInput.B:
                case ControllerInput.Back:
                    if (isQuitConfirmationVisible)
                    {
                        HideQuitConfirmation();
                    }
                    else
                    {
                        service.HideOverlay();
                    }
                    break;
            }
        }

        private bool CanProcessControllerNavigation(int direction)
        {
            if (direction == 0)
            {
                return false;
            }

            var now = DateTime.Now;
            var elapsed = (now - lastControllerNavigationTime).TotalMilliseconds;

            if (elapsed < 80)
            {
                return false;
            }

            lastControllerNavigationDirection = direction;
            lastControllerNavigationTime = now;
            return true;
        }

        private void MoveForcedControllerFocus(int direction)
        {
            useControllerFocusVisual = true;

            if (isQuitConfirmationVisible)
            {
                controllerFocusedButton = controllerFocusedButton == cancelQuitButton
                    ? confirmQuitButton
                    : cancelQuitButton;

                FocusSelectedButtonWithoutTraversal();
                UpdateAllButtonVisualStates();
                return;
            }

            var buttons = GetMainControllerButtons();
            if (buttons.Length == 0)
            {
                return;
            }

            var index = Array.IndexOf(buttons, controllerFocusedButton);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + buttons.Length) % buttons.Length;
            }

            controllerFocusedButton = buttons[index];
            FocusSelectedButtonWithoutTraversal();
            UpdateAllButtonVisualStates();
        }

        private Button[] GetMainControllerButtons()
        {
            if (service.IsGameRunning)
            {
                return new[] { returnButton, quitButton }
                    .WhereButtonCanReceiveControllerFocus();
            }

            return new[] { returnButton }.WhereButtonCanReceiveControllerFocus();
        }

        private void ClickForcedControllerFocusedButton()
        {
            try
            {
                useControllerFocusVisual = true;

                var button = controllerFocusedButton;

                if (!CanButtonReceiveControllerFocus(button))
                {
                    button = isQuitConfirmationVisible ? cancelQuitButton : firstButton;
                    controllerFocusedButton = button;
                }

                if (!CanButtonReceiveControllerFocus(button))
                {
                    return;
                }

                if (isQuitConfirmationVisible)
                {
                    if (button == cancelQuitButton)
                    {
                        HideQuitConfirmation();
                        return;
                    }

                    if (button == confirmQuitButton)
                    {
                        service.ConfirmQuitGame();
                        return;
                    }

                    return;
                }


                if (button == returnButton)
                {
                    if (service.OverlayOpenedFromPlaynite)
                    {
                        service.ReturnToGame();
                    }
                    else
                    {
                        service.ReturnToPlaynite();
                    }

                    return;
                }

                if (button == quitButton)
                {
                    service.RequestQuitGame();
                    return;
                }
            }
            catch
            {
            }
        }

        private bool CanButtonReceiveControllerFocus(Button button)
        {
            return button != null &&
                   button.Visibility == Visibility.Visible &&
                   button.IsEnabled &&
                   button.Focusable;
        }

        private void FocusSelectedButtonWithoutTraversal()
        {
            try
            {
                if (CanButtonReceiveControllerFocus(controllerFocusedButton))
                {
                    controllerFocusedButton.Focus();
                    Keyboard.Focus(controllerFocusedButton);
                }
            }
            catch
            {
            }
        }

        public void FocusOverlayButton()
        {
            FocusOverlayButtonNow();
        }

        private void FocusOverlayButtonNow()
        {
            try
            {
                Activate();
                Focus();

                var target = firstButton;

                if (!CanButtonReceiveControllerFocus(target))
                {
                    target = returnButton;
                }

                if (!CanButtonReceiveControllerFocus(target))
                {
                    return;
                }

                controllerFocusedButton = target;
                useControllerFocusVisual = true;

                lastControllerNavigationTime = DateTime.MinValue;
                lastControllerNavigationDirection = 0;
                lastControllerActionTime = DateTime.MinValue;

                FocusSelectedButtonWithoutTraversal();
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var now = DateTime.Now;

            if ((now - lastDirectOverlayControllerInputTime).TotalMilliseconds < 250)
            {
                if (e.Key == Key.Up ||
                    e.Key == Key.Down ||
                    e.Key == Key.Left ||
                    e.Key == Key.Right ||
                    e.Key == Key.Enter ||
                    e.Key == Key.Space ||
                    e.Key == Key.Escape ||
                    e.Key == Key.Back)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                e.Handled = true;

                var direction = e.Key == Key.Down || e.Key == Key.Right ? 1 : -1;

                useControllerFocusVisual = true;
                MoveForcedControllerFocus(direction);

                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                e.Handled = true;
                ClickForcedControllerFocusedButton();
                return;
            }

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                e.Handled = true;

                if (isQuitConfirmationVisible)
                {
                    HideQuitConfirmation();
                }
                else
                {
                    service.HideOverlay();
                }
            }
        }
    }

    internal static class AnikiInGameOverlayButtonExtensions
    {
        public static Button[] WhereButtonCanReceiveControllerFocus(this Button[] buttons)
        {
            if (buttons == null)
            {
                return new Button[0];
            }

            var result = new System.Collections.Generic.List<Button>();

            foreach (var button in buttons)
            {
                if (button != null &&
                    button.Visibility == Visibility.Visible &&
                    button.IsEnabled &&
                    button.Focusable)
                {
                    result.Add(button);
                }
            }

            return result.ToArray();
        }
    }
}