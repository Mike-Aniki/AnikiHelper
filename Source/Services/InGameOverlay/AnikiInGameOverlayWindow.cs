using AnikiHelper.Services.MediaGallery;
using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnikiHelper.Services.InGameOverlay
{
    internal sealed class AnikiInGameOverlayWindow : Window
    {
        private readonly InGameOverlayService service;

        private const string IconBack = "\uE72B";
        private const string IconHome = "\uE80F";
        private const string IconPower = "\uE7E8";
        private const string IconGame = "\uE7FC";
        private const string IconMedia = "\uE714";
        private const string IconAudio = "\uE995";
        private const string IconFriends = "\uE716";
        private const string IconApps = "\uECAA";
        private const string IconMusic = "\uE189";
        private const string IconUniPlaySong = "\uE7F3";
        private const string IconTrophy = "\uE7C1";

        private enum OverlaySection
        {
            Game,
            Media,
            Audio,
            Achievements
        }

        private OverlaySection activeSection = OverlaySection.Game;
        private bool isSectionPanelOpen;
        private readonly List<Button> topBarButtons = new List<Button>();

        private StackPanel overlayButtonStack;
        private Grid rootGrid;
        private Grid panel;
        private TranslateTransform panelTransform;
        private Border darkLayer;
        private ContentControl musicPlayerHost;
        private bool isMusicPlayerVisible;
        private ContentControl audioSwitcherHost;
        private bool isAudioSwitcherVisible;
        private ContentControl uniPlaySongHost;
        private bool isUniPlaySongVisible;
        private ContentControl friendsHost;
        private bool isFriendsVisible;
        private ContentControl lastCapturesHost;
        private bool isLastCapturesVisible;
        private Grid capturePreviewLayer;
        private Image capturePreviewImage;
        private TextBlock capturePreviewTitleText;
        private TextBlock capturePreviewMetaText;
        private TextBlock capturePreviewIndexText;
        private AnikiMediaItem capturePreviewItem;
        private bool isCapturePreviewVisible;
        private ContentControl appsHost;
        private bool isAppsVisible;
        private ContentControl achievementsHost;
        private bool isAchievementsVisible;
        private Border bottomDimLayer;
        private ContentControl bottomHintHost;

        private Image gameLogoImage;
        private Border gameLogoContainer;
        private TextBlock gameTitleText;
        private Border gameCoverCard;
        private Image gameCoverImage;
        private RectangleGeometry gameCoverClip;
        private ColumnDefinition gameCoverColumn;

        private TextBlock sourceValueText;
        private TextBlock platformValueText;
        private TextBlock playtimeValueText;
        private TextBlock sessionValueText;
        private TextBlock mediaCountValueText;
        private TextBlock mediaLastCaptureValueText;
        private TextBlock mediaCountPanelValueText;
        private TextBlock mediaLastCapturePanelValueText;
        private TextBlock achievementsUnlockedValueText;
        private TextBlock achievementsProgressValueText;
        private TextBlock achievementsUnlockedPanelValueText;
        private TextBlock achievementsProgressPanelValueText;
        private TextBlock footerTitleText;
        private TextBlock clockText;
        private Image userAvatarImage;
        private TextBlock userNameText;
        private TextBlock userStatusText;
        private Border userStatusDot;

        private Border sectionContentPanel;
        private Border persistentGameInfoPanel;
        private Image sectionBackgroundImage;
        private Grid gameSectionPanel;
        private Grid mediaSectionPanel;
        private Grid audioSectionPanel;
        private Grid achievementsSectionPanel;
        private Button gameSectionButton;
        private Button mediaSectionButton;
        private Button audioSectionButton;
        private Button friendsButton;
        private Button appsButton;
        private Button uniPlaySongButton;
        private Button musicButton;
        private Button achievementsSectionButton;

        private Button returnButton;
        private TextBlock returnButtonIconText;
        private TextBlock returnButtonLabelText;
        private Button quitButton;
        private Button cancelQuitButton;
        private Button confirmQuitButton;
        private Button firstButton;
        private Button controllerFocusedButton;
        private FrameworkElement controllerFocusedMusicPlayerElement;
        private FrameworkElement controllerFocusedAudioSwitcherElement;
        private FrameworkElement controllerFocusedUniPlaySongElement;
        private FrameworkElement controllerFocusedFriendsElement;
        private FrameworkElement controllerFocusedLastCapturesElement;
        private FrameworkElement controllerFocusedAppsElement;
        private FrameworkElement controllerFocusedAchievementsElement;

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
        private DateTime lastCapturePreviewClosedTime = DateTime.MinValue;
        private DispatcherTimer sessionTimer;
        private bool isHiding;

        private bool IsOverlayChildViewVisible()
        {
            return isMusicPlayerVisible ||
                   isAudioSwitcherVisible ||
                   isUniPlaySongVisible ||
                   isFriendsVisible ||
                   isLastCapturesVisible ||
                   isCapturePreviewVisible ||
                   isAppsVisible ||
                   isAchievementsVisible;
        }


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
            Opacity = 0;

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
                    HideCapturePreview(false);
                    sessionTimer?.Stop();
                }
                catch
                {
                }
            };
        }

        public void Refresh()
        {
            RefreshUserInfo();
            RefreshHeader();
            RefreshButtons();
            RefreshInfoValues();

        }


        private object FindThemeResource(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            try
            {
                var localResource = TryFindResource(key);
                if (localResource != null)
                {
                    return localResource;
                }
            }
            catch
            {
            }

            try
            {
                var mainWindow = System.Windows.Application.Current != null ? System.Windows.Application.Current.MainWindow : null;
                if (mainWindow != null)
                {
                    var mainWindowResource = mainWindow.TryFindResource(key);
                    if (mainWindowResource != null)
                    {
                        return mainWindowResource;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (System.Windows.Application.Current != null)
                {
                    var appResource = System.Windows.Application.Current.TryFindResource(key);
                    if (appResource != null)
                    {
                        return appResource;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private void ApplyThemeAvatarStyle(Border avatarBorder)
        {
            if (avatarBorder == null)
            {
                return;
            }

            try
            {
                var mainWindow = System.Windows.Application.Current != null ? System.Windows.Application.Current.MainWindow : null;

                if (mainWindow != null)
                {
                    for (var i = 0; i <= 61; i++)
                    {
                        var key = "Avatar" + i;

                        if (!avatarBorder.Resources.Contains(key))
                        {
                            var avatarResource = mainWindow.TryFindResource(key);

                            if (avatarResource != null)
                            {
                                avatarBorder.Resources[key] = avatarResource;
                            }
                        }
                    }

                    if (!avatarBorder.Resources.Contains("Avatar99"))
                    {
                        var luckyAvatarResource = mainWindow.TryFindResource("Avatar99");

                        if (luckyAvatarResource != null)
                        {
                            avatarBorder.Resources["Avatar99"] = luckyAvatarResource;
                        }
                    }

                    var selectedAvatarStyle = mainWindow.TryFindResource("SelectedAvatarBorderStyle") as Style;

                    if (selectedAvatarStyle != null)
                    {
                        avatarBorder.Style = selectedAvatarStyle;
                        return;
                    }
                }
            }
            catch
            {
            }

            avatarBorder.SetResourceReference(FrameworkElement.StyleProperty, "SelectedAvatarBorderStyle");
        }

        private void RefreshUserInfo()
        {
            try
            {
                if (userNameText != null)
                {
                    var name = service.SelfName;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        var resourceName = System.Windows.Application.Current.TryFindResource("UserName");
                        name = resourceName != null ? resourceName.ToString() : string.Empty;
                    }

                    userNameText.Text = string.IsNullOrWhiteSpace(name) ? "User Name" : name;
                }

                if (userStatusText != null)
                {
                    userStatusText.Text = string.IsNullOrWhiteSpace(service.SelfStateLoc) ? Loc("LOCInGameOverlayOffline", "Offline") : service.SelfStateLoc;
                }

                if (userStatusDot != null)
                {
                    var state = service.SelfState ?? "offline";
                    var resourceKey = "StatusOfflineBrush";

                    if (string.Equals(state, "online", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceKey = "StatusOnlineBrush";
                    }
                    else if (string.Equals(state, "away", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceKey = "StatusAwayBrush";
                    }
                    else if (string.Equals(state, "busy", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceKey = "StatusBusyBrush";
                    }
                    else if (string.Equals(state, "ingame", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceKey = "StatusInGameBrush";
                    }

                    userStatusDot.SetResourceReference(Border.BackgroundProperty, resourceKey);
                }

                if (userAvatarImage != null)
                {
                    var avatarPath = service.SelfAvatarPath;
                    userAvatarImage.Source = null;

                    if (!string.IsNullOrWhiteSpace(avatarPath))
                    {
                        try
                        {
                            Uri uri;
                            if (avatarPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                                avatarPath.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                                avatarPath.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                            {
                                uri = new Uri(avatarPath, UriKind.Absolute);
                            }
                            else
                            {
                                uri = new Uri(Path.GetFullPath(avatarPath), UriKind.Absolute);
                            }

                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = uri;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            userAvatarImage.Source = bitmap;
                        }
                        catch
                        {
                            userAvatarImage.Source = null;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void RefreshHeader()
        {
            RefreshGameCover();
            RefreshGameBackground();

            if (gameLogoContainer != null)
            {
                gameLogoContainer.Visibility = Visibility.Collapsed;
            }

            if (gameTitleText != null)
            {
                gameTitleText.Text = service.IsGameRunning
                    ? service.CurrentGameName
                    : Loc("LOCInGameOverlayNoGameRunning", "No game running");
                gameTitleText.Visibility = service.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshGameCover()
        {
            try
            {
                if (gameCoverCard == null || gameCoverImage == null)
                {
                    return;
                }

                if (!service.IsGameRunning)
                {
                    gameCoverImage.Source = null;
                    gameCoverImage.Width = double.NaN;
                    gameCoverImage.Height = double.NaN;
                    gameCoverCard.Width = double.NaN;
                    gameCoverCard.Height = double.NaN;
                    gameCoverCard.Visibility = Visibility.Collapsed;
                    if (gameCoverClip != null)
                    {
                        gameCoverClip.Rect = Rect.Empty;
                    }
                    if (gameCoverColumn != null)
                    {
                        gameCoverColumn.Width = new GridLength(0);
                    }
                    return;
                }

                var coverPath = service.CurrentGameCoverPath;
                if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
                {
                    gameCoverImage.Source = null;
                    gameCoverImage.Width = double.NaN;
                    gameCoverImage.Height = double.NaN;
                    gameCoverCard.Width = double.NaN;
                    gameCoverCard.Height = double.NaN;
                    gameCoverCard.Visibility = Visibility.Collapsed;
                    if (gameCoverClip != null)
                    {
                        gameCoverClip.Rect = Rect.Empty;
                    }
                    if (gameCoverColumn != null)
                    {
                        gameCoverColumn.Width = new GridLength(0);
                    }
                    return;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(coverPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                var maxWidth = 170.0;
                var maxHeight = 118.0;
                var width = bitmap.PixelWidth > 0 ? bitmap.PixelWidth : maxWidth;
                var height = bitmap.PixelHeight > 0 ? bitmap.PixelHeight : maxHeight;
                var scale = Math.Min(maxWidth / width, maxHeight / height);

                gameCoverImage.Width = Math.Max(1, width * scale);
                gameCoverImage.Height = Math.Max(1, height * scale);
                gameCoverImage.Source = bitmap;

                if (gameCoverClip != null)
                {
                    gameCoverClip.Rect = new Rect(0, 0, gameCoverImage.Width, gameCoverImage.Height);
                }

                gameCoverCard.Width = gameCoverImage.Width;
                gameCoverCard.Height = gameCoverImage.Height;
                gameCoverCard.Visibility = Visibility.Visible;

                if (gameCoverColumn != null)
                {
                    gameCoverColumn.Width = new GridLength(gameCoverImage.Width + 26);
                }
            }
            catch
            {
                try
                {
                    if (gameCoverImage != null)
                    {
                        gameCoverImage.Source = null;
                        gameCoverImage.Width = double.NaN;
                        gameCoverImage.Height = double.NaN;
                    }

                    if (gameCoverCard != null)
                    {
                        gameCoverCard.Width = double.NaN;
                        gameCoverCard.Height = double.NaN;
                        gameCoverCard.Visibility = Visibility.Collapsed;
                    }

                    if (gameCoverClip != null)
                    {
                        gameCoverClip.Rect = Rect.Empty;
                    }

                    if (gameCoverColumn != null)
                    {
                        gameCoverColumn.Width = new GridLength(0);
                    }
                }
                catch
                {
                }
            }
        }

        private void RefreshGameBackground()
        {
            try
            {
                if (sectionBackgroundImage == null)
                {
                    return;
                }

                if (!service.IsGameRunning)
                {
                    sectionBackgroundImage.Source = null;
                    sectionBackgroundImage.Visibility = Visibility.Collapsed;
                    return;
                }

                var backgroundPath = service.CurrentGameBackgroundPath;
                if (string.IsNullOrWhiteSpace(backgroundPath) || !File.Exists(backgroundPath))
                {
                    sectionBackgroundImage.Source = null;
                    sectionBackgroundImage.Visibility = Visibility.Collapsed;
                    return;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(backgroundPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                sectionBackgroundImage.Source = bitmap;
                sectionBackgroundImage.Visibility = Visibility.Visible;
            }
            catch
            {
                try
                {
                    if (sectionBackgroundImage != null)
                    {
                        sectionBackgroundImage.Source = null;
                        sectionBackgroundImage.Visibility = Visibility.Collapsed;
                    }
                }
                catch
                {
                }
            }
        }

        private void RebuildOverlayButtonOrder(bool isRunning)
        {
            if (overlayButtonStack == null)
            {
                return;
            }

            var orderedButtons = isRunning
                ? new[]
                {
                    returnButton,
                    achievementsSectionButton,
                    friendsButton,
                    mediaSectionButton,
                    musicButton,
                    uniPlaySongButton,
                    audioSectionButton,
                    appsButton,
                    quitButton
                }
                : new[]
                {
                    musicButton,
                    uniPlaySongButton,
                    audioSectionButton,
                    appsButton,
                    friendsButton,
                    mediaSectionButton,
                    returnButton,
                    achievementsSectionButton,
                    quitButton
                };

            overlayButtonStack.Children.Clear();

            foreach (var button in orderedButtons)
            {
                if (button != null)
                {
                    overlayButtonStack.Children.Add(button);
                }
            }
        }

        private void RefreshButtons()
        {
            var isRunning = service.IsGameRunning;

            if (!isRunning && isSectionPanelOpen &&
                (activeSection == OverlaySection.Game || activeSection == OverlaySection.Achievements))
            {
                isSectionPanelOpen = false;
                activeSection = OverlaySection.Media;
                RefreshSectionVisibility();
            }

            SetSectionButtonEnabled(gameSectionButton, false);
            SetSectionButtonEnabled(mediaSectionButton, true);
            SetSectionButtonEnabled(audioSectionButton, service.IsAudioSwitcherInstalled);
            SetSectionButtonEnabled(friendsButton, true);
            SetSectionButtonEnabled(appsButton, true);
            SetSectionButtonEnabled(uniPlaySongButton, service.IsUniPlaySongInstalled);
            SetSectionButtonEnabled(musicButton, true);
            SetSectionButtonEnabled(achievementsSectionButton, isRunning && service.IsPlayniteAchievementsInstalled);

            if (quitButton != null)
            {
                quitButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
                quitButton.IsEnabled = isRunning;
                quitButton.IsTabStop = isRunning;
                quitButton.Focusable = isRunning;
            }

            if (returnButton != null)
            {
                returnButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
                returnButton.IsEnabled = isRunning;
                returnButton.IsTabStop = isRunning;
                returnButton.Focusable = isRunning;
            }

            RefreshReturnButtonMode();
            RebuildOverlayButtonOrder(isRunning);

            firstButton = isRunning
                ? returnButton
                : musicButton;

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

            UpdateAllButtonVisualStates();
        }

        private void SetSectionButtonEnabled(Button button, bool isEnabled)
        {
            if (button == null)
            {
                return;
            }

            button.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = isEnabled;
            button.IsTabStop = isEnabled;
            button.Focusable = isEnabled;
        }

        private void RefreshReturnButtonMode()
        {
            try
            {
                if (returnButtonLabelText == null)
                {
                    return;
                }

                if (!service.IsGameRunning)
                {
                    if (returnButtonIconText != null)
                    {
                        returnButtonIconText.Text = IconBack;
                    }

                    returnButtonLabelText.Text = Loc("LOCInGameOverlayClose", "Close");
                }
                else if (service.OverlayOpenedFromPlaynite)
                {
                    if (returnButtonIconText != null)
                    {
                        returnButtonIconText.Text = IconGame;
                    }

                    returnButtonLabelText.Text = Loc("LOCInGameOverlayGame", "Game");
                }
                else
                {
                    if (returnButtonIconText != null)
                    {
                        returnButtonIconText.Text = IconHome;
                    }

                    returnButtonLabelText.Text = Loc("LOCInGameOverlayPlaynite", "Playnite");
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

                SetText("-", mediaCountValueText, mediaCountPanelValueText);
                SetText("-", mediaLastCaptureValueText, mediaLastCapturePanelValueText);
                SetText("-", achievementsUnlockedValueText, achievementsUnlockedPanelValueText);
                SetText("-", achievementsProgressValueText, achievementsProgressPanelValueText);

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

            SetText(service.CurrentGameMediaCountValue, mediaCountValueText, mediaCountPanelValueText);
            SetText(service.CurrentGameLatestCaptureValue, mediaLastCaptureValueText, mediaLastCapturePanelValueText);
            SetText(service.CurrentGameAchievementsUnlockedValue, achievementsUnlockedValueText, achievementsUnlockedPanelValueText);
            SetText(service.CurrentGameAchievementsProgressValue, achievementsProgressValueText, achievementsProgressPanelValueText);

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

        private void SetText(string value, params TextBlock[] targets)
        {
            if (targets == null)
            {
                return;
            }

            foreach (var target in targets)
            {
                if (target != null)
                {
                    target.Text = value;
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

            rootGrid = root;

            var viewbox = new Viewbox
            {
                Stretch = Stretch.UniformToFill,
                Child = root
            };

            Content = viewbox;

            darkLayer = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(176, 0, 0, 0)),
                IsHitTestVisible = false,
                Opacity = 0
            };
            root.Children.Add(darkLayer);

            bottomDimLayer = new Border
            {
                Height = 430,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsHitTestVisible = false,
                Opacity = 0,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
                        new GradientStop(Color.FromArgb(130, 0, 0, 0), 0.45),
                        new GradientStop(Color.FromArgb(235, 0, 0, 0), 1.0)
                    }
                }
            };
            root.Children.Add(bottomDimLayer);

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
                X = -44,
                Y = 0
            };

            panel = new Grid
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                RenderTransform = panelTransform,
                Opacity = 0
            };
            System.Windows.Controls.Panel.SetZIndex(panel, 10);
            root.Children.Add(panel);

            var leftPanel = new Grid
            {
                Width = 470,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            panel.Children.Add(leftPanel);

            var panelBackground = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                Background = new SolidColorBrush(Color.FromArgb(235, 14, 14, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255))
            };
            panelBackground.SetResourceReference(Border.BackgroundProperty, "OverlayMenu");
            panelBackground.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");
            leftPanel.Children.Add(panelBackground);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftPanel.Children.Add(layout);

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
                MinHeight = 96,
                Margin = new Thickness(12, 10, 18, 10)
            };
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            var identityHost = new ContentControl
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = service
            };

            var identityTemplate = FindThemeResource("AnikiInGameOverlayIdentityTemplate") as DataTemplate;
            if (identityTemplate == null)
            {
                identityTemplate = FindThemeResource("AnikiControlCenterIdentityTemplate") as DataTemplate;
            }

            if (identityTemplate != null)
            {
                identityHost.ContentTemplate = identityTemplate;
            }
            else
            {
                identityHost.Content = Loc("LOCInGameOverlayUser", "User");
                identityHost.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                identityHost.FontSize = 24;
                identityHost.FontWeight = FontWeights.SemiBold;
            }
            header.Children.Add(identityHost);

            gameLogoContainer = new Border { Visibility = Visibility.Collapsed };
            gameLogoImage = new Image();
            gameTitleText = new TextBlock { Visibility = Visibility.Collapsed };

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
                Margin = new Thickness(16, 16, 16, 12)
            };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(contentGrid, 2);
            layout.Children.Add(contentGrid);

            overlayButtonStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            Grid.SetRow(overlayButtonStack, 0);
            Grid.SetRowSpan(overlayButtonStack, 3);
            contentGrid.Children.Add(overlayButtonStack);

            returnButton = CreateButton(IconHome, Loc("LOCInGameOverlayPlaynite", "Playnite"), service.ReturnToPlaynite);
            mediaSectionButton = CreateButton(IconMedia, Loc("LOCInGameOverlayLastCaptures", "Last Captures"), service.OpenLastCapturesWindow);
            audioSectionButton = CreateButton(IconAudio, Loc("LOCInGameOverlayAudio", "Audio Switcher"), service.OpenAudioSwitcherWindow);
            friendsButton = CreateButton(IconFriends, Loc("LOCInGameOverlayFriends", "Friends"), service.OpenFriendsWindow);
            appsButton = CreateButton(IconApps, Loc("LOCInGameOverlayApps", "Apps"), service.OpenAppsWindow);
            uniPlaySongButton = CreateButton(IconUniPlaySong, Loc("LOCInGameOverlayUniPlaySong", "UniPlaySong"), service.OpenUniPlaySongWindow);
            musicButton = CreateButton(IconMusic, Loc("LOCInGameOverlayMusic", "Music Player"), service.OpenMusicPlayerWindow);
            achievementsSectionButton = CreateButton(IconTrophy, Loc("LOCInGameOverlayAchievements", "Achievements"), service.OpenAchievementsWindow);
            quitButton = CreateButton(IconPower, Loc("LOCInGameOverlayQuitGame", "Quit Game"), service.RequestQuitGame);

            RebuildOverlayButtonOrder(service.IsGameRunning);

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
            Grid.SetRow(footer, 4);
            layout.Children.Add(footer);

            var panelBackHint = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.86
            };

            var panelBackKey = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(210, 245, 241, 234)),
                Margin = new Thickness(0, 0, 10, 0)
            };
            var panelBackKeyText = new TextBlock
            {
                Text = "B",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Black
            };
            panelBackKey.Child = panelBackKeyText;
            panelBackHint.Children.Add(panelBackKey);

            var panelBackText = new TextBlock
            {
                Text = Loc("LOCBackLabel", "Back"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            panelBackText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            panelBackHint.Children.Add(panelBackText);
            footer.Children.Add(panelBackHint);

            footerTitleText = new TextBlock
            {
                Text = "Aniki Overlay",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.42,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            footerTitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            footer.Children.Add(footerTitleText);

            sectionContentPanel = new Border
            {
                Width = 980,
                Height = 236,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(504, 146, 0, 0),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(226, 14, 14, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(68, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.42,
                    Color = Colors.Black
                }
            };
            sectionContentPanel.SetResourceReference(Border.BackgroundProperty, "OverlayMenu");
            sectionContentPanel.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");
            panel.Children.Add(sectionContentPanel);

            var sectionFrameRoot = new Grid
            {
                ClipToBounds = true
            };
            sectionContentPanel.Child = sectionFrameRoot;

            sectionBackgroundImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = 0.2,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            System.Windows.Controls.Panel.SetZIndex(sectionBackgroundImage, 0);
            sectionFrameRoot.Children.Add(sectionBackgroundImage);

            var sectionRoot = new Grid
            {
                Margin = new Thickness(18)
            };
            System.Windows.Controls.Panel.SetZIndex(sectionRoot, 1);
            sectionFrameRoot.Children.Add(sectionRoot);

            gameSectionPanel = CreateGameSectionPanel();
            mediaSectionPanel = CreateMediaSectionPanel();
            audioSectionPanel = CreateAudioSectionPanel();
            achievementsSectionPanel = CreateAchievementsSectionPanel();

            sectionRoot.Children.Add(mediaSectionPanel);
            sectionRoot.Children.Add(audioSectionPanel);
            sectionRoot.Children.Add(achievementsSectionPanel);

            persistentGameInfoPanel = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(504, 0, 34, 18),
                Padding = new Thickness(18, 12, 20, 14),
                Background = new LinearGradientBrush(
                    Color.FromArgb(176, 7, 9, 14),
                    Color.FromArgb(72, 7, 9, 14),
                    new Point(0.5, 1.0),
                    new Point(0.5, 0.0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            persistentGameInfoPanel.Child = gameSectionPanel;
            panel.Children.Add(persistentGameInfoPanel);

            quitConfirmationPanel = CreateQuitConfirmationPanel();
            System.Windows.Controls.Panel.SetZIndex(quitConfirmationPanel, 100);
            root.Children.Add(quitConfirmationPanel);

            bottomHintHost = new ContentControl
            {
                Content = service,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 100,
                IsHitTestVisible = false,
                Focusable = false,
                Opacity = 0,
                Visibility = Visibility.Collapsed
            };

            var bottomHintTemplate = FindThemeResource("AnikiControlCenterBottomHintTemplate") as DataTemplate;
            if (bottomHintTemplate != null)
            {
                bottomHintHost.ContentTemplate = bottomHintTemplate;
            }
            else
            {
                var fallbackHint = new TextBlock
                {
                    Text = Loc("LOCInGameOverlayBackHint", "B / Back to close"),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Opacity = 0.7,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 20, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
                };
                fallbackHint.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                bottomHintHost.Content = fallbackHint;
            }
            System.Windows.Controls.Panel.SetZIndex(bottomHintHost, 12);
            root.Children.Add(bottomHintHost);

            RefreshSectionVisibility();
            Refresh();
        }

        private Grid CreateGameSectionPanel()
        {
            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            gameTitleText = new TextBlock
            {
                Text = service.IsGameRunning ? service.CurrentGameName : string.Empty,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0.96,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            gameTitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(gameTitleText, 0);
            outer.Children.Add(gameTitleText);

            var grid = new Grid();
            Grid.SetRow(grid, 1);
            outer.Children.Add(grid);

            gameCoverColumn = new ColumnDefinition { Width = new GridLength(0) };
            grid.ColumnDefinitions.Add(gameCoverColumn);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

            gameCoverCard = new Border
            {
                MaxWidth = 170,
                MaxHeight = 118,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 14, 0),
                Visibility = Visibility.Collapsed,
                SnapsToDevicePixels = true
            };
            gameCoverCard.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");

            var coverGrid = new Grid { ClipToBounds = true };
            gameCoverClip = new RectangleGeometry { RadiusX = 9, RadiusY = 9 };
            gameCoverImage = new Image
            {
                MaxWidth = 170,
                MaxHeight = 118,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                SnapsToDevicePixels = true,
                Clip = gameCoverClip
            };
            coverGrid.Children.Add(gameCoverImage);
            gameCoverCard.Child = coverGrid;
            Grid.SetColumn(gameCoverCard, 0);
            grid.Children.Add(gameCoverCard);

            var infoStack = CreateSectionColumn(Loc("LOCInGameOverlayGameInfo", "Game info"));
            Grid.SetColumn(infoStack, 1);
            grid.Children.Add(infoStack);

            var infoGrid = CreateInfoGrid(4);
            AddInfoRow(infoGrid, 0, Loc("LOCInGameOverlaySource", "Source"), out sourceValueText);
            AddInfoRow(infoGrid, 1, Loc("LOCInGameOverlayPlatform", "Platform"), out platformValueText);
            AddInfoRow(infoGrid, 2, Loc("LOCInGameOverlayPlaytime", "Playtime"), out playtimeValueText);
            AddInfoRow(infoGrid, 3, Loc("LOCInGameOverlaySession", "Session"), out sessionValueText);
            infoStack.Children.Add(infoGrid);

            var mediaStack = CreateSectionColumn(Loc("LOCInGameOverlayMedia", "Media"));
            mediaStack.Margin = new Thickness(18, 0, 18, 0);
            Grid.SetColumn(mediaStack, 2);
            grid.Children.Add(mediaStack);

            var mediaMiniGrid = CreateInfoGrid(2);
            AddInfoRow(mediaMiniGrid, 0, Loc("LOCInGameOverlayMediaAvailable", "Captures"), out mediaCountValueText);
            AddInfoRow(mediaMiniGrid, 1, Loc("LOCInGameOverlayMediaLatest", "Latest capture"), out mediaLastCaptureValueText);
            mediaStack.Children.Add(mediaMiniGrid);

            var achievementStack = CreateSectionColumn(Loc("LOCInGameOverlayAchievements", "Achievements"));
            Grid.SetColumn(achievementStack, 3);
            grid.Children.Add(achievementStack);

            var achievementMiniGrid = CreateInfoGrid(2);
            AddInfoRow(achievementMiniGrid, 0, Loc("LOCInGameOverlayAchievementsUnlocked", "Unlocked"), out achievementsUnlockedValueText);
            AddInfoRow(achievementMiniGrid, 1, Loc("LOCInGameOverlayAchievementsProgress", "Progress"), out achievementsProgressValueText);
            achievementStack.Children.Add(achievementMiniGrid);

            return outer;
        }

        private Grid CreateMediaSectionPanel()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var summaryStack = CreateSectionColumn(Loc("LOCInGameOverlayMedia", "Media"));
            Grid.SetColumn(summaryStack, 0);
            grid.Children.Add(summaryStack);

            var summary = new TextBlock
            {
                Text = Loc("LOCInGameOverlayMediaCenterHint", "Quick view of captures linked to the current game."),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 20, 16),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            summary.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            summaryStack.Children.Add(summary);

            var duplicatedMediaGrid = CreateInfoGrid(2);
            AddInfoRow(duplicatedMediaGrid, 0, Loc("LOCInGameOverlayMediaAvailable", "Captures"), out mediaCountPanelValueText);
            AddInfoRow(duplicatedMediaGrid, 1, Loc("LOCInGameOverlayMediaLatest", "Latest capture"), out mediaLastCapturePanelValueText);
            summaryStack.Children.Add(duplicatedMediaGrid);

            var placeholderStack = CreateSectionColumn(Loc("LOCInGameOverlayComingSoon", "Coming soon"));
            Grid.SetColumn(placeholderStack, 1);
            grid.Children.Add(placeholderStack);

            var placeholder = new TextBlock
            {
                Text = Loc("LOCInGameOverlayMediaFutureHint", "This panel can later host UPS / Spotify controls without touching the overlay window logic."),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            placeholderStack.Children.Add(placeholder);

            return grid;
        }

        private Grid CreateAudioSectionPanel()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var audioStack = CreateSectionColumn(Loc("LOCInGameOverlayAudio", "Audio"));
            Grid.SetColumn(audioStack, 0);
            grid.Children.Add(audioStack);

            var summary = new TextBlock
            {
                Text = Loc("LOCInGameOverlayAudioCenterHint", "Quick audio controls will live here: output device, master volume and later per-app volume."),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 20, 16),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            summary.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            audioStack.Children.Add(summary);

            var placeholderStack = CreateSectionColumn(Loc("LOCInGameOverlayComingSoon", "Coming soon"));
            Grid.SetColumn(placeholderStack, 1);
            grid.Children.Add(placeholderStack);

            var placeholder = new TextBlock
            {
                Text = Loc("LOCInGameOverlayAudioFutureHint", "This first version only changes the Control Center layout. Audio source and volume controls can be wired after the UI is validated."),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            placeholderStack.Children.Add(placeholder);

            return grid;
        }

        private Grid CreateAchievementsSectionPanel()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var summaryStack = CreateSectionColumn(Loc("LOCInGameOverlayAchievements", "Achievements"));
            Grid.SetColumn(summaryStack, 0);
            grid.Children.Add(summaryStack);

            var achievementGrid = CreateInfoGrid(2);
            AddInfoRow(achievementGrid, 0, Loc("LOCInGameOverlayAchievementsUnlocked", "Unlocked"), out achievementsUnlockedPanelValueText);
            AddInfoRow(achievementGrid, 1, Loc("LOCInGameOverlayAchievementsProgress", "Progress"), out achievementsProgressPanelValueText);
            summaryStack.Children.Add(achievementGrid);

            var latestStack = CreateSectionColumn(Loc("LOCInGameOverlayAchievementsLatest", "Latest achievement"));
            latestStack.Margin = new Thickness(18, 0, 0, 0);
            Grid.SetColumn(latestStack, 1);
            grid.Children.Add(latestStack);

            latestAchievementCard = CreateLatestAchievementCard();
            latestAchievementCard.Margin = new Thickness(0, 2, 0, 0);
            latestStack.Children.Add(latestAchievementCard);

            return grid;
        }

        private StackPanel CreateSectionColumn(string title)
        {
            var outer = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var header = new TextBlock
            {
                Text = title,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Opacity = 0.85,
                Margin = new Thickness(0, 0, 0, 16),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            header.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            outer.Children.Add(header);

            return outer;
        }


        private Button CreateTopBarTextButton(string text, Action action, double minWidth)
        {
            var button = new Button
            {
                MinWidth = minWidth,
                Height = 70,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Focusable = true,
                IsTabStop = true,
                Margin = new Thickness(8, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234)),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 3),
                FocusVisualStyle = null,
                Template = CreateTopBarTextButtonTemplate()
            };

            button.SetResourceReference(Control.ForegroundProperty, "TextBrush");

            var themeStyle = TryFindResource("AnikiControlCenterTopButtonStyle") as Style;
            if (themeStyle == null)
            {
                themeStyle = TryFindResource("AnikiInGameOverlayTopButtonStyle") as Style;
            }

            if (themeStyle != null)
            {
                button.Style = themeStyle;
            }

            var labelText = new TextBlock
            {
                Text = text,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            button.Content = labelText;

            if (action == service.ReturnToPlaynite)
            {
                returnButtonLabelText = labelText;
            }

            topBarButtons.Add(button);

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

            button.Click += (s, e) =>
            {
                if (button == returnButton)
                {
                    if (!service.IsGameRunning)
                    {
                        service.HideOverlay();
                    }
                    else if (service.OverlayOpenedFromPlaynite)
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

        private Button CreateControlCenterButton(string icon, string text, Action action, double width)
        {
            var button = new Button
            {
                Width = width,
                Height = 58,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Focusable = true,
                IsTabStop = true,
                Margin = new Thickness(4, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(8),
                Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234)),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                FocusVisualStyle = null,
                Template = CreateOverlayButtonTemplate()
            };

            button.SetResourceReference(Control.ForegroundProperty, "TextBrush");

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

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 26,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 7),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            iconText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            iconText.SetResourceReference(TextBlock.FontFamilyProperty, "FontIcons");
            stack.Children.Add(iconText);

            if (action == service.ReturnToPlaynite)
            {
                returnButtonIconText = iconText;
            }

            var labelText = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(labelText);

            if (action == service.ReturnToPlaynite)
            {
                returnButtonLabelText = labelText;
            }

            button.Content = stack;
            button.Click += (s, e) =>
            {
                if (button == returnButton)
                {
                    if (!service.IsGameRunning)
                    {
                        service.HideOverlay();
                    }
                    else if (service.OverlayOpenedFromPlaynite)
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

        private void SelectSection(OverlaySection section)
        {
            if (isSectionPanelOpen && activeSection == section)
            {
                isSectionPanelOpen = false;
            }
            else
            {
                activeSection = section;
                isSectionPanelOpen = true;
            }

            RefreshSectionVisibility();
            UpdateAllButtonVisualStates();
        }

        private void RefreshSectionVisibility()
        {
            if (sectionContentPanel != null)
            {
                var showPanel = isSectionPanelOpen && activeSection != OverlaySection.Game;
                sectionContentPanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
                sectionContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
                sectionContentPanel.VerticalAlignment = VerticalAlignment.Top;
                sectionContentPanel.Width = 920;
                sectionContentPanel.Height = 260;
                sectionContentPanel.Margin = new Thickness(504, 146, 0, 0);
                sectionContentPanel.CornerRadius = new CornerRadius(18);
                sectionContentPanel.BorderThickness = new Thickness(1);
                sectionContentPanel.Effect = new DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.42,
                    Color = Colors.Black
                };
                sectionContentPanel.SetResourceReference(Border.BackgroundProperty, "OverlayMenu");
                sectionContentPanel.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");
            }

            if (sectionBackgroundImage != null)
            {
                if (sectionBackgroundImage.Source != null && service.IsGameRunning && isSectionPanelOpen && activeSection != OverlaySection.Game)
                {
                    sectionBackgroundImage.Visibility = Visibility.Visible;
                }
                else
                {
                    sectionBackgroundImage.Visibility = Visibility.Collapsed;
                }
            }

            if (persistentGameInfoPanel != null)
            {
                persistentGameInfoPanel.Visibility = service.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
            }

            if (gameSectionPanel != null)
            {
                gameSectionPanel.Visibility = service.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
            }

            if (mediaSectionPanel != null)
            {
                mediaSectionPanel.Visibility = isSectionPanelOpen && activeSection == OverlaySection.Media ? Visibility.Visible : Visibility.Collapsed;
            }

            if (audioSectionPanel != null)
            {
                audioSectionPanel.Visibility = isSectionPanelOpen && activeSection == OverlaySection.Audio && service.IsAudioSwitcherInstalled ? Visibility.Visible : Visibility.Collapsed;
            }

            if (achievementsSectionPanel != null)
            {
                achievementsSectionPanel.Visibility = isSectionPanelOpen && activeSection == OverlaySection.Achievements ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private bool IsSelectedSectionButton(Button button)
        {
            return isSectionPanelOpen &&
                   ((button == gameSectionButton && activeSection == OverlaySection.Game) ||
                    (button == mediaSectionButton && activeSection == OverlaySection.Media) ||
                    (button == audioSectionButton && activeSection == OverlaySection.Audio) ||
                    (button == achievementsSectionButton && activeSection == OverlaySection.Achievements));
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
                FontSize = 15,
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

            var themeStyle = FindThemeResource("AnikiControlCenterMenuButtonStyle") as Style;
            if (themeStyle == null)
            {
                themeStyle = FindThemeResource("AnikiInGameOverlayButtonStyle") as Style;
            }

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
            iconText.SetResourceReference(TextBlock.FontFamilyProperty, "FontIcons");
            iconText.FontWeight = FontWeights.Normal;
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
                    if (!service.IsGameRunning)
                    {
                        service.HideOverlay();
                    }
                    else if (service.OverlayOpenedFromPlaynite)
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
                FontSize = 15,
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

            cancelQuitButton = CreateButton(IconBack, Loc("LOCInGameOverlayCancel", "Cancel"), HideQuitConfirmation);
            confirmQuitButton = CreateButton(IconPower, Loc("LOCInGameOverlayConfirmQuit", "Quit Game"), service.ConfirmQuitGame);

            buttons.Children.Add(cancelQuitButton);
            buttons.Children.Add(confirmQuitButton);

            return panelBorder;
        }


        private ControlTemplate CreateTopBarTextButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var root = new FrameworkElementFactory(typeof(Grid));
            root.Name = "Root";
            root.SetValue(Grid.SnapsToDevicePixelsProperty, true);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var hoverBackground = new FrameworkElementFactory(typeof(Border));
            hoverBackground.Name = "HoverBackground";
            hoverBackground.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            hoverBackground.SetValue(Border.OpacityProperty, 0.0);
            hoverBackground.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            root.AppendChild(hoverBackground);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.Name = "ContentHost";
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.OpacityProperty, 0.82);
            presenter.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            root.AppendChild(presenter);

            var underline = new FrameworkElementFactory(typeof(Border));
            underline.Name = "FocusBorder";
            underline.SetValue(Border.HeightProperty, 3.0);
            underline.SetValue(Border.WidthProperty, 78.0);
            underline.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            underline.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            underline.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            underline.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 1));
            underline.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            root.AppendChild(underline);

            template.VisualTree = root;
            return template;
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

            var isSelectedSection = IsSelectedSectionButton(button);
            var isActive = isControllerActive || isKeyboardOrMouseActive;

            if (topBarButtons.Contains(button))
            {
                button.Background = Brushes.Transparent;
                button.BorderThickness = new Thickness(0, 0, 0, 3);
                button.Opacity = button.IsEnabled ? 1 : 0.35;

                if (isActive || isSelectedSection)
                {
                    button.SetResourceReference(Control.BorderBrushProperty, "FocusGameBorderBrush");
                }
                else
                {
                    button.BorderBrush = Brushes.Transparent;
                }

                return;
            }

            if (isActive || isSelectedSection)
            {
                button.SetResourceReference(Control.BackgroundProperty, "ButtonBackgroundFocus");
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
            UpdateButtonVisualState(gameSectionButton);
            UpdateButtonVisualState(mediaSectionButton);
            UpdateButtonVisualState(audioSectionButton);
            UpdateButtonVisualState(friendsButton);
            UpdateButtonVisualState(appsButton);
            UpdateButtonVisualState(uniPlaySongButton);
            UpdateButtonVisualState(musicButton);
            UpdateButtonVisualState(achievementsSectionButton);
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

        public void PrepareForShowAnimation()
        {
            try
            {
                isHiding = false;

                BeginAnimation(Window.OpacityProperty, null);
                Opacity = 0;

                if (darkLayer != null)
                {
                    darkLayer.BeginAnimation(UIElement.OpacityProperty, null);
                    darkLayer.Opacity = 0;
                }

                if (bottomDimLayer != null)
                {
                    bottomDimLayer.BeginAnimation(UIElement.OpacityProperty, null);
                    bottomDimLayer.Opacity = 0;
                }

                if (panel != null)
                {
                    panel.BeginAnimation(UIElement.OpacityProperty, null);
                    panel.Opacity = 0;
                }

                if (bottomHintHost != null)
                {
                    bottomHintHost.BeginAnimation(UIElement.OpacityProperty, null);
                    bottomHintHost.Opacity = 0;
                }

                if (panelTransform != null)
                {
                    panelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    panelTransform.X = -44;
                    panelTransform.BeginAnimation(TranslateTransform.YProperty, null);
                    panelTransform.Y = 0;
                }
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
                isSectionPanelOpen = false;
                isMusicPlayerVisible = false;
                controllerFocusedMusicPlayerElement = null;
                isAudioSwitcherVisible = false;
                isUniPlaySongVisible = false;
                isFriendsVisible = false;
                isLastCapturesVisible = false;
                isAppsVisible = false;
                isAchievementsVisible = false;
                controllerFocusedFriendsElement = null;
                controllerFocusedLastCapturesElement = null;
                controllerFocusedAppsElement = null;
                controllerFocusedAchievementsElement = null;

                if (musicPlayerHost != null)
                {
                    musicPlayerHost.Visibility = Visibility.Collapsed;
                }

                if (audioSwitcherHost != null)
                {
                    audioSwitcherHost.Visibility = Visibility.Collapsed;
                }

                if (uniPlaySongHost != null)
                {
                    uniPlaySongHost.Visibility = Visibility.Collapsed;
                }

                if (friendsHost != null)
                {
                    friendsHost.Visibility = Visibility.Collapsed;
                }

                if (lastCapturesHost != null)
                {
                    lastCapturesHost.Visibility = Visibility.Collapsed;
                }

                if (appsHost != null)
                {
                    appsHost.Visibility = Visibility.Collapsed;
                }

                if (achievementsHost != null)
                {
                    achievementsHost.Visibility = Visibility.Collapsed;
                }

                SetControlCenterChromeVisible(true);
                RefreshSectionVisibility();

                BeginAnimation(Window.OpacityProperty, null);
                Opacity = 0;

                if (darkLayer != null)
                {
                    darkLayer.BeginAnimation(UIElement.OpacityProperty, null);
                    darkLayer.Opacity = 0;
                }

                if (bottomDimLayer != null)
                {
                    bottomDimLayer.BeginAnimation(UIElement.OpacityProperty, null);
                    bottomDimLayer.Opacity = 0;
                }

                if (panel != null)
                {
                    panel.BeginAnimation(UIElement.OpacityProperty, null);
                    panel.Opacity = 0;
                }

                if (bottomHintHost != null)
                {
                    bottomHintHost.BeginAnimation(UIElement.OpacityProperty, null);
                    bottomHintHost.Opacity = 0;
                }

                if (panelTransform != null)
                {
                    panelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    panelTransform.X = -44;
                    panelTransform.BeginAnimation(TranslateTransform.YProperty, null);
                    panelTransform.Y = 0;
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
                isMusicPlayerVisible = false;
                controllerFocusedMusicPlayerElement = null;
                isAudioSwitcherVisible = false;
                isUniPlaySongVisible = false;
                isAppsVisible = false;
                isAchievementsVisible = false;
                controllerFocusedAppsElement = null;
                controllerFocusedAchievementsElement = null;

                if (musicPlayerHost != null)
                {
                    musicPlayerHost.Visibility = Visibility.Collapsed;
                }

                if (audioSwitcherHost != null)
                {
                    audioSwitcherHost.Visibility = Visibility.Collapsed;
                }

                if (uniPlaySongHost != null)
                {
                    uniPlaySongHost.Visibility = Visibility.Collapsed;
                }

                if (appsHost != null)
                {
                    appsHost.Visibility = Visibility.Collapsed;
                }

                if (achievementsHost != null)
                {
                    achievementsHost.Visibility = Visibility.Collapsed;
                }

                SetControlCenterChromeVisible(true);
                isSectionPanelOpen = service.IsGameRunning;
                activeSection = service.IsGameRunning ? OverlaySection.Game : OverlaySection.Media;
                RefreshSectionVisibility();
                RefreshButtons();
                UpdateAllButtonVisualStates();

                // The window is already prepared while it is still hidden, before Show().
                // Do not reset visual values here: doing it after Show()/Activate() creates
                // a visible jump on reused overlay windows.
                BeginAnimation(Window.OpacityProperty, null);
                Opacity = 1;

                if (darkLayer != null)
                {
                    darkLayer.BeginAnimation(UIElement.OpacityProperty, null);
                    darkLayer.Opacity = 0;
                    var darkFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    darkLayer.BeginAnimation(UIElement.OpacityProperty, darkFade);
                }

                if (bottomDimLayer != null)
                {
                    bottomDimLayer.BeginAnimation(UIElement.OpacityProperty, null);
                    bottomDimLayer.Opacity = 0;
                    var bottomFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    bottomDimLayer.BeginAnimation(UIElement.OpacityProperty, bottomFade);
                }

                if (panel != null)
                {
                    panel.BeginAnimation(UIElement.OpacityProperty, null);
                    panel.Opacity = 0;
                    var panelFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    panel.BeginAnimation(UIElement.OpacityProperty, panelFade);
                }

                if (bottomHintHost != null)
                {
                    bottomHintHost.BeginAnimation(UIElement.OpacityProperty, null);
                    bottomHintHost.Opacity = 0;
                    var hintFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(40),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    bottomHintHost.BeginAnimation(UIElement.OpacityProperty, hintFade);
                }

                if (panelTransform != null)
                {
                    panelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    panelTransform.X = -44;
                    panelTransform.Y = 0;
                    var slide = new DoubleAnimation(-44, 0, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    panelTransform.BeginAnimation(TranslateTransform.XProperty, slide);
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
                HideImmediately();
            }
            finally
            {
                isHiding = false;
            }
        }

        public void ShowMusicPlayer()
        {
            try
            {
                EnsureMusicPlayerHost();

                if (musicPlayerHost == null)
                {
                    return;
                }

                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideApps(false);
                HideAchievements(false);

                isMusicPlayerVisible = true;
                SetControlCenterChromeVisible(false);

                musicPlayerHost.Visibility = Visibility.Visible;
                musicPlayerHost.Opacity = 1;

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FocusFirstMusicPlayerElement();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void EnsureMusicPlayerHost()
        {
            if (musicPlayerHost != null || rootGrid == null)
            {
                return;
            }

            musicPlayerHost = new ContentControl
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Visibility = Visibility.Collapsed
            };

            try
            {
                var style = FindThemeResource("MusicPlayerWindowStyle") as Style;
                if (style != null)
                {
                    musicPlayerHost.Style = style;
                }
                else
                {
                    musicPlayerHost.Content = new TextBlock
                    {
                        Text = "MusicPlayerWindowStyle not found",
                        FontSize = 30,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
            }

            Panel.SetZIndex(musicPlayerHost, 200);
            rootGrid.Children.Add(musicPlayerHost);
        }

        private void HideMusicPlayer(bool restoreControlCenterChrome = true)
        {
            try
            {
                isMusicPlayerVisible = false;
                controllerFocusedMusicPlayerElement = null;

                if (musicPlayerHost != null)
                {
                    musicPlayerHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = musicButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        public void ShowAudioSwitcher()
        {
            try
            {
                EnsureAudioSwitcherHost();

                if (audioSwitcherHost == null)
                {
                    return;
                }

                HideMusicPlayer(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideApps(false);
                HideAchievements(false);

                isAudioSwitcherVisible = true;
                SetControlCenterChromeVisible(false);

                audioSwitcherHost.Visibility = Visibility.Visible;
                audioSwitcherHost.Opacity = 1;

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FocusFirstAudioSwitcherButton();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void EnsureAudioSwitcherHost()
        {
            if (audioSwitcherHost != null || rootGrid == null)
            {
                return;
            }

            audioSwitcherHost = new ContentControl
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Visibility = Visibility.Collapsed
            };

            try
            {
                var style = FindThemeResource("AudioSwitcherWindowStyle") as Style;
                if (style != null)
                {
                    audioSwitcherHost.Style = style;
                }
                else
                {
                    audioSwitcherHost.Content = new TextBlock
                    {
                        Text = "AudioSwitcherWindowStyle not found",
                        FontSize = 30,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
            }

            Panel.SetZIndex(audioSwitcherHost, 200);
            rootGrid.Children.Add(audioSwitcherHost);
        }

        private void HideAudioSwitcher(bool restoreControlCenterChrome = true)
        {
            try
            {
                isAudioSwitcherVisible = false;
                controllerFocusedAudioSwitcherElement = null;

                if (audioSwitcherHost != null)
                {
                    audioSwitcherHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = audioSectionButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        public void ShowUniPlaySong()
        {
            try
            {
                EnsureUniPlaySongHost();

                if (uniPlaySongHost == null)
                {
                    return;
                }

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideApps(false);
                HideAchievements(false);

                isUniPlaySongVisible = true;
                SetControlCenterChromeVisible(false);

                uniPlaySongHost.Visibility = Visibility.Visible;
                uniPlaySongHost.Opacity = 1;

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FocusFirstUniPlaySongElement();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void EnsureUniPlaySongHost()
        {
            if (uniPlaySongHost != null || rootGrid == null)
            {
                return;
            }

            uniPlaySongHost = new ContentControl
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Visibility = Visibility.Collapsed
            };

            try
            {
                var style = FindThemeResource("UniPlaySongWindowStyle") as Style;
                if (style != null)
                {
                    uniPlaySongHost.Style = style;
                }
                else
                {
                    uniPlaySongHost.Content = new TextBlock
                    {
                        Text = "UniPlaySongWindowStyle not found",
                        FontSize = 30,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
            }

            Panel.SetZIndex(uniPlaySongHost, 200);
            rootGrid.Children.Add(uniPlaySongHost);
        }

        private void HideUniPlaySong(bool restoreControlCenterChrome = true)
        {
            try
            {
                isUniPlaySongVisible = false;
                controllerFocusedUniPlaySongElement = null;

                if (uniPlaySongHost != null)
                {
                    uniPlaySongHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = uniPlaySongButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        public void ShowFriends()
        {
            try
            {
                EnsureFriendsHost();

                if (friendsHost == null)
                {
                    return;
                }

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideLastCaptures(false);
                HideApps(false);
                HideAchievements(false);

                isFriendsVisible = true;
                SetControlCenterChromeVisible(false);

                friendsHost.Visibility = Visibility.Visible;
                friendsHost.Opacity = 1;

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FocusFirstFriendsElement();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void EnsureFriendsHost()
        {
            if (friendsHost != null || rootGrid == null)
            {
                return;
            }

            friendsHost = new ContentControl
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Visibility = Visibility.Collapsed
            };

            try
            {
                var style = FindThemeResource("FriendsWindowStyle") as Style;
                if (style != null)
                {
                    friendsHost.Style = style;
                }
                else
                {
                    friendsHost.Content = new TextBlock
                    {
                        Text = "FriendsWindowStyle not found",
                        FontSize = 30,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
            }

            Panel.SetZIndex(friendsHost, 200);
            rootGrid.Children.Add(friendsHost);
        }

        private void HideFriends(bool restoreControlCenterChrome = true)
        {
            try
            {
                isFriendsVisible = false;
                controllerFocusedFriendsElement = null;

                if (friendsHost != null)
                {
                    friendsHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = friendsButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        public void ShowLastCaptures()
        {
            try
            {
                EnsureLastCapturesHost();

                if (lastCapturesHost == null)
                {
                    return;
                }

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideApps(false);
                HideAchievements(false);
                HideCapturePreview(false);

                isLastCapturesVisible = true;
                SetControlCenterChromeVisible(false);

                lastCapturesHost.Visibility = Visibility.Visible;
                lastCapturesHost.Opacity = 1;

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FocusFirstLastCapturesElement();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void EnsureLastCapturesHost()
        {
            if (lastCapturesHost != null || rootGrid == null)
            {
                return;
            }

            lastCapturesHost = new ContentControl
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Visibility = Visibility.Collapsed
            };

            try
            {
                var style = FindThemeResource("LastCapturesWindowStyle") as Style;
                if (style != null)
                {
                    lastCapturesHost.Style = style;
                }
                else
                {
                    lastCapturesHost.Content = new TextBlock
                    {
                        Text = "LastCapturesWindowStyle not found",
                        FontSize = 30,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
            }

            Panel.SetZIndex(lastCapturesHost, 200);
            rootGrid.Children.Add(lastCapturesHost);
        }

        private void HideLastCaptures(bool restoreControlCenterChrome = true)
        {
            try
            {
                HideCapturePreview(false);
                isLastCapturesVisible = false;
                controllerFocusedLastCapturesElement = null;

                if (lastCapturesHost != null)
                {
                    lastCapturesHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    // Last Captures replaces the old Media placeholder panel.
                    // When closing the dedicated view, restore the overlay chrome but keep
                    // the legacy Media panel closed so reopening/pressing the button does
                    // not show the old placeholder frame.
                    if (activeSection == OverlaySection.Media)
                    {
                        isSectionPanelOpen = false;
                        RefreshSectionVisibility();
                    }

                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = mediaSectionButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        public bool ShowCapturePreview(AnikiMediaItem mediaItem)
        {
            try
            {
                if (!isLastCapturesVisible || mediaItem == null)
                {
                    return false;
                }

                var imagePath = service.GetCapturePreviewImagePath(mediaItem);
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return false;
                }

                var bitmap = LoadCapturePreviewBitmap(imagePath);
                if (bitmap == null)
                {
                    return false;
                }

                EnsureCapturePreviewLayer();
                if (capturePreviewLayer == null || capturePreviewImage == null)
                {
                    return false;
                }

                capturePreviewItem = mediaItem;
                capturePreviewImage.Source = bitmap;

                if (capturePreviewTitleText != null)
                {
                    var title = !string.IsNullOrWhiteSpace(mediaItem.GameName)
                        ? mediaItem.GameName
                        : mediaItem.FileName;

                    capturePreviewTitleText.Text = string.IsNullOrWhiteSpace(title)
                        ? Loc("LOCImage", "Screenshot")
                        : title;
                }

                if (capturePreviewMetaText != null)
                {
                    var metaParts = new List<string>();

                    if (!string.IsNullOrWhiteSpace(mediaItem.CaptureDateString))
                    {
                        metaParts.Add(mediaItem.CaptureDateString);
                    }

                    if (!string.IsNullOrWhiteSpace(mediaItem.SourceProvider))
                    {
                        metaParts.Add(mediaItem.SourceProvider);
                    }

                    if (mediaItem.IsVideo)
                    {
                        metaParts.Add(Loc("Video", "Video thumbnail"));
                    }

                    capturePreviewMetaText.Text = string.Join("  •  ", metaParts);
                }

                UpdateCapturePreviewIndex();

                isCapturePreviewVisible = true;
                capturePreviewLayer.Visibility = Visibility.Visible;
                capturePreviewLayer.Opacity = 1;

                if (lastCapturesHost != null)
                {
                    lastCapturesHost.IsHitTestVisible = false;
                }

                Activate();
                Focus();
                capturePreviewLayer.Focus();
                Keyboard.Focus(capturePreviewLayer);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureCapturePreviewLayer()
        {
            if (capturePreviewLayer != null || rootGrid == null)
            {
                return;
            }

            var previewRoot = new Grid
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(248, 0, 0, 0)),
                Focusable = true,
                Visibility = Visibility.Collapsed
            };

            capturePreviewLayer = previewRoot;

            var imageFrame = new Border
            {
                Margin = new Thickness(58, 80, 58, 112),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(5, 5, 5)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            capturePreviewImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                SnapsToDevicePixels = true
            };

            RenderOptions.SetBitmapScalingMode(capturePreviewImage, BitmapScalingMode.HighQuality);
            imageFrame.Child = capturePreviewImage;
            previewRoot.Children.Add(imageFrame);

            var topBar = new Grid
            {
                Height = 64,
                Margin = new Thickness(70, 10, 70, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };

            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            capturePreviewTitleText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 25,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 1420
            };

            capturePreviewMetaText = new TextBlock
            {
                Margin = new Thickness(0, 3, 0, 0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 1420
            };

            titleStack.Children.Add(capturePreviewTitleText);
            titleStack.Children.Add(capturePreviewMetaText);
            Grid.SetColumn(titleStack, 0);
            topBar.Children.Add(titleStack);

            capturePreviewIndexText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(capturePreviewIndexText, 1);
            topBar.Children.Add(capturePreviewIndexText);
            previewRoot.Children.Add(topBar);

            var footerBorder = new Border
            {
                Padding = new Thickness(18, 9, 18, 9),
                Margin = new Thickness(0, 0, 0, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(185, 18, 18, 18)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };

            footerBorder.Child = new TextBlock
            {
                Text = "← / →     B  " + Loc("LOCBackLabel", "Back"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };

            previewRoot.Children.Add(footerBorder);

            previewRoot.MouseLeftButtonUp += (sender, args) =>
            {
                args.Handled = true;
                HideCapturePreview();
            };

            Panel.SetZIndex(previewRoot, 1000);
            rootGrid.Children.Add(previewRoot);
        }

        private BitmapSource LoadCapturePreviewBitmap(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    return null;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateCapturePreviewIndex()
        {
            if (capturePreviewIndexText == null)
            {
                return;
            }

            var items = service.GetOverlayLastCapturePreviewItems();
            if (items == null || items.Count == 0 || capturePreviewItem == null)
            {
                capturePreviewIndexText.Text = string.Empty;
                return;
            }

            var index = items.FindIndex(item => IsSameCapturePreviewItem(item, capturePreviewItem));
            capturePreviewIndexText.Text = index >= 0
                ? (index + 1) + " / " + items.Count
                : string.Empty;
        }

        private static bool IsSameCapturePreviewItem(AnikiMediaItem left, AnikiMediaItem right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.ThumbnailPath, right.ThumbnailPath, StringComparison.OrdinalIgnoreCase);
        }

        private void MoveCapturePreview(int direction)
        {
            try
            {
                var items = service.GetOverlayLastCapturePreviewItems();
                if (items == null || items.Count <= 1)
                {
                    return;
                }

                var index = items.FindIndex(item => IsSameCapturePreviewItem(item, capturePreviewItem));
                if (index < 0)
                {
                    index = 0;
                }
                else
                {
                    index = (index + direction + items.Count) % items.Count;
                }

                ShowCapturePreview(items[index]);
            }
            catch
            {
            }
        }

        private void HideCapturePreview(bool restoreLastCapturesFocus = true)
        {
            try
            {
                if (isCapturePreviewVisible)
                {
                    lastCapturePreviewClosedTime = DateTime.Now;
                }

                isCapturePreviewVisible = false;
                capturePreviewItem = null;

                if (capturePreviewImage != null)
                {
                    capturePreviewImage.Source = null;
                }

                if (capturePreviewLayer != null)
                {
                    capturePreviewLayer.Visibility = Visibility.Collapsed;
                }

                if (lastCapturesHost != null)
                {
                    lastCapturesHost.IsHitTestVisible = true;
                }

                if (!restoreLastCapturesFocus || !isLastCapturesVisible)
                {
                    return;
                }

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (controllerFocusedLastCapturesElement != null &&
                            CanElementReceiveControllerFocus(controllerFocusedLastCapturesElement))
                        {
                            FocusLastCapturesElement(controllerFocusedLastCapturesElement);
                        }
                        else
                        {
                            FocusFirstLastCapturesElement();
                        }
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private bool HandleCapturePreviewControllerInput(ControllerInput button)
        {
            if (!isCapturePreviewVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveCapturePreview(-1);
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveCapturePreview(1);
                    }
                    return true;

                case ControllerInput.B:
                case ControllerInput.Back:
                    HideCapturePreview();
                    return true;

                case ControllerInput.A:
                    return true;
            }

            return false;
        }

        private bool HandleCapturePreviewKeyDown(KeyEventArgs e)
        {
            if (!isCapturePreviewVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                MoveCapturePreview(-1);
                return true;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                MoveCapturePreview(1);
                return true;
            }

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                e.Handled = true;
                HideCapturePreview();
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                e.Handled = true;
                return true;
            }

            return false;
        }

        public void ShowAchievements()
        {
            try
            {
                EnsureAchievementsHost();

                if (achievementsHost == null)
                {
                    return;
                }

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideApps(false);

                isAchievementsVisible = true;
                SetControlCenterChromeVisible(false);

                achievementsHost.Visibility = Visibility.Visible;
                achievementsHost.Opacity = 1;

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FocusFirstAchievementsElement();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void EnsureAchievementsHost()
        {
            if (achievementsHost != null || rootGrid == null)
            {
                return;
            }

            achievementsHost = new ContentControl
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Visibility = Visibility.Collapsed
            };

            try
            {
                var style = FindThemeResource("AchievementsWindowStyle") as Style;
                if (style != null)
                {
                    achievementsHost.Style = style;
                }
                else
                {
                    achievementsHost.Content = new TextBlock
                    {
                        Text = "AchievementsWindowStyle not found",
                        FontSize = 30,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
            }

            Panel.SetZIndex(achievementsHost, 200);
            rootGrid.Children.Add(achievementsHost);
        }

        private void HideAchievements(bool restoreControlCenterChrome = true)
        {
            try
            {
                isAchievementsVisible = false;
                controllerFocusedAchievementsElement = null;

                if (achievementsHost != null)
                {
                    achievementsHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    if (activeSection == OverlaySection.Achievements)
                    {
                        isSectionPanelOpen = false;
                        RefreshSectionVisibility();
                    }

                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = achievementsSectionButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        public void ShowApps()
        {
            try
            {
                EnsureAppsHost();

                if (appsHost == null)
                {
                    return;
                }

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideAchievements(false);

                isAppsVisible = true;
                SetControlCenterChromeVisible(false);

                appsHost.Visibility = Visibility.Visible;
                appsHost.Opacity = 1;

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        FocusFirstAppsElement();
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void EnsureAppsHost()
        {
            if (appsHost != null || rootGrid == null)
            {
                return;
            }

            appsHost = new ContentControl
            {
                Width = 1920,
                Height = 1080,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Visibility = Visibility.Collapsed
            };

            try
            {
                var style = FindThemeResource("AppsWindowStyle") as Style;
                if (style != null)
                {
                    appsHost.Style = style;
                }
                else
                {
                    appsHost.Content = new TextBlock
                    {
                        Text = "AppsWindowStyle not found",
                        FontSize = 30,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
            }

            Panel.SetZIndex(appsHost, 200);
            rootGrid.Children.Add(appsHost);
        }

        private void HideApps(bool restoreControlCenterChrome = true)
        {
            try
            {
                isAppsVisible = false;
                isAchievementsVisible = false;
                controllerFocusedAppsElement = null;
                controllerFocusedAchievementsElement = null;

                if (appsHost != null)
                {
                    appsHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = appsButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        private void SetControlCenterChromeVisible(bool visible)
        {
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (darkLayer != null)
            {
                darkLayer.Visibility = visibility;
            }

            if (bottomDimLayer != null)
            {
                bottomDimLayer.Visibility = visibility;
            }

            if (clockText != null)
            {
                clockText.Visibility = visibility;
            }

            if (panel != null)
            {
                panel.Visibility = visibility;
            }

            if (bottomHintHost != null)
            {
                bottomHintHost.Visibility = visibility;
            }
        }

        private void FocusFirstMusicPlayerElement()
        {
            var elements = GetMusicPlayerControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            FocusMusicPlayerElement(elements[0]);
        }

        private Button[] GetMusicPlayerButtons()
        {
            if (!isMusicPlayerVisible || musicPlayerHost == null)
            {
                return new Button[0];
            }

            try
            {
                musicPlayerHost.ApplyTemplate();
                musicPlayerHost.UpdateLayout();

                var result = new List<Button>();
                CollectVisualChildren(musicPlayerHost, result);
                return result.ToArray().WhereButtonCanReceiveControllerFocus();
            }
            catch
            {
                return new Button[0];
            }
        }

        private FrameworkElement[] GetMusicPlayerControllerElements()
        {
            if (!isMusicPlayerVisible || musicPlayerHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                musicPlayerHost.ApplyTemplate();
                musicPlayerHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(musicPlayerHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    var isMusicRadioSourceToggle = string.Equals(element.Name, "MusicRadioSourceToggle", StringComparison.OrdinalIgnoreCase);

                    if (isMusicRadioSourceToggle)
                    {
                        element.Focusable = true;
                        KeyboardNavigation.SetIsTabStop(element, true);
                    }

                    if (!CanElementReceiveControllerFocus(element))
                    {
                        continue;
                    }

                    if (element is RepeatButton)
                    {
                        continue;
                    }

                    // Music player can contain ButtonEx, ToggleButton, CheckBoxEx, or named custom toggles.
                    // Do not restrict this to Button only, otherwise theme CheckBoxEx controls are skipped.
                    if (element is ButtonBase || element is RangeBase || isMusicRadioSourceToggle)
                    {
                        result.Add(element);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return new FrameworkElement[0];
            }
        }

        private void FocusMusicPlayerElement(FrameworkElement element)
        {
            try
            {
                if (!CanElementReceiveControllerFocus(element))
                {
                    return;
                }

                controllerFocusedMusicPlayerElement = element;
                controllerFocusedButton = element as Button;
                useControllerFocusVisual = true;

                element.Focus();
                Keyboard.Focus(element);
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private FrameworkElement GetCurrentMusicPlayerElement()
        {
            var elements = GetMusicPlayerControllerElements();
            if (elements.Length == 0)
            {
                return null;
            }

            foreach (var element in elements)
            {
                if (element != null && element.IsKeyboardFocusWithin)
                {
                    return element;
                }
            }

            if (controllerFocusedMusicPlayerElement != null &&
                Array.IndexOf(elements, controllerFocusedMusicPlayerElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedMusicPlayerElement))
            {
                return controllerFocusedMusicPlayerElement;
            }

            return elements[0];
        }

        private void MoveMusicPlayerFocus(int direction)
        {
            var elements = GetMusicPlayerControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            var current = GetCurrentMusicPlayerElement();
            var index = Array.IndexOf(elements, current);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + elements.Length) % elements.Length;
            }

            FocusMusicPlayerElement(elements[index]);
        }

        private bool ActivateMusicPlayerElement()
        {
            var current = GetCurrentMusicPlayerElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                var toggle = current as ToggleButton;
                if (toggle != null)
                {
                    toggle.IsChecked = toggle.IsChecked != true;
                    return true;
                }

                // Supports CheckBoxEx and other Playnite toggle controls without referencing their exact type.
                var isCheckedProperty = current.GetType().GetProperty("IsChecked");
                if (isCheckedProperty != null && isCheckedProperty.CanRead && isCheckedProperty.CanWrite)
                {
                    var rawValue = isCheckedProperty.GetValue(current, null);
                    var currentValue = false;

                    if (rawValue is bool boolValue)
                    {
                        currentValue = boolValue;
                    }

                    isCheckedProperty.SetValue(current, !currentValue, null);
                    return true;
                }

                var button = current as Button;
                if (button != null)
                {
                    controllerFocusedButton = button;
                    ClickForcedControllerFocusedButton();
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase != null)
                {
                    if (buttonBase.Command != null && buttonBase.Command.CanExecute(buttonBase.CommandParameter))
                    {
                        buttonBase.Command.Execute(buttonBase.CommandParameter);
                    }
                    else
                    {
                        buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    }
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool HandleMusicPlayerControllerInput(ControllerInput button)
        {
            if (!isMusicPlayerVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveMusicPlayerFocus(-1);
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveMusicPlayerFocus(1);
                    }
                    return true;

                case ControllerInput.A:
                    // Hosted overlay views already receive native controller activation
                    // through Playnite/WPF. Forcing another click here makes buttons/toggles
                    // execute twice, so we consume only the custom overlay A input.
                    return true;
            }

            return false;
        }

        private bool HandleMusicPlayerPreviewKeyDown(KeyEventArgs e)
        {
            if (!isMusicPlayerVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    MoveMusicPlayerFocus(-1);
                }
                return true;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    MoveMusicPlayerFocus(1);
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                // Stop the overlay global Enter/Space handler, but keep the event unhandled
                // so the focused hosted control can process its native activation once.
                e.Handled = false;
                return true;
            }

            return false;
        }

        private void FocusFirstAudioSwitcherButton()
        {
            var elements = GetAudioSwitcherControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            FocusAudioSwitcherElement(elements[0]);
        }

        private Button[] GetAudioSwitcherButtons()
        {
            if (!isAudioSwitcherVisible || audioSwitcherHost == null)
            {
                return new Button[0];
            }

            try
            {
                audioSwitcherHost.ApplyTemplate();
                audioSwitcherHost.UpdateLayout();

                var result = new List<Button>();
                CollectVisualChildren(audioSwitcherHost, result);
                return result.ToArray().WhereButtonCanReceiveControllerFocus();
            }
            catch
            {
                return new Button[0];
            }
        }

        private FrameworkElement[] GetAudioSwitcherControllerElements()
        {
            if (!isAudioSwitcherVisible || audioSwitcherHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                audioSwitcherHost.ApplyTemplate();
                audioSwitcherHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(audioSwitcherHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element))
                    {
                        continue;
                    }

                    // Only expose real navigation targets to the overlay controller router.
                    // Do not include Slider template internals such as RepeatButton.
                    if (element is RepeatButton)
                    {
                        continue;
                    }

                    if (element is Button || element is RangeBase)
                    {
                        result.Add(element);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return new FrameworkElement[0];
            }
        }

        private bool CanElementReceiveControllerFocus(FrameworkElement element)
        {
            return element != null &&
                   element.Visibility == Visibility.Visible &&
                   element.IsEnabled &&
                   element.Focusable;
        }

        private void FocusAudioSwitcherElement(FrameworkElement element)
        {
            try
            {
                if (!CanElementReceiveControllerFocus(element))
                {
                    return;
                }

                controllerFocusedAudioSwitcherElement = element;
                controllerFocusedButton = element as Button;
                useControllerFocusVisual = true;

                element.Focus();
                Keyboard.Focus(element);
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private FrameworkElement GetCurrentAudioSwitcherElement()
        {
            var elements = GetAudioSwitcherControllerElements();
            if (elements.Length == 0)
            {
                return null;
            }

            foreach (var element in elements)
            {
                if (element != null && element.IsKeyboardFocusWithin)
                {
                    return element;
                }
            }

            if (controllerFocusedAudioSwitcherElement != null &&
                Array.IndexOf(elements, controllerFocusedAudioSwitcherElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedAudioSwitcherElement))
            {
                return controllerFocusedAudioSwitcherElement;
            }

            return elements[0];
        }

        private void MoveAudioSwitcherFocus(int direction)
        {
            var elements = GetAudioSwitcherControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            var current = GetCurrentAudioSwitcherElement();
            var index = Array.IndexOf(elements, current);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + elements.Length) % elements.Length;
            }

            FocusAudioSwitcherElement(elements[index]);
        }

        private bool TryAdjustAudioSwitcherSlider(int direction)
        {
            var current = GetCurrentAudioSwitcherElement();
            var range = current as RangeBase;
            if (range == null)
            {
                return false;
            }

            try
            {
                var step = range.SmallChange;
                if (step <= 0)
                {
                    step = range.LargeChange;
                }

                if (step <= 0)
                {
                    step = 5;
                }

                var value = range.Value + (direction * step);
                if (value < range.Minimum)
                {
                    value = range.Minimum;
                }
                else if (value > range.Maximum)
                {
                    value = range.Maximum;
                }

                range.Value = value;
                return true;
            }
            catch
            {
                return true;
            }
        }

        private bool HandleAudioSwitcherControllerInput(ControllerInput button)
        {
            if (!isAudioSwitcherVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                    if (CanProcessControllerNavigation(-1))
                    {
                        if (!TryAdjustAudioSwitcherSlider(-1))
                        {
                            MoveAudioSwitcherFocus(-1);
                        }
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                    if (CanProcessControllerNavigation(1))
                    {
                        if (!TryAdjustAudioSwitcherSlider(1))
                        {
                            MoveAudioSwitcherFocus(1);
                        }
                    }
                    return true;

                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveAudioSwitcherFocus(-1);
                    }
                    return true;

                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveAudioSwitcherFocus(1);
                    }
                    return true;

                case ControllerInput.A:
                    // Hosted overlay views already receive native controller activation
                    // through Playnite/WPF. Forcing another click here makes buttons/toggles
                    // execute twice, so we consume only the custom overlay A input.
                    return true;
            }

            return false;
        }

        private bool HandleAudioSwitcherPreviewKeyDown(KeyEventArgs e)
        {
            if (!isAudioSwitcherVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    if (!TryAdjustAudioSwitcherSlider(-1))
                    {
                        MoveAudioSwitcherFocus(-1);
                    }
                }
                return true;
            }

            if (e.Key == Key.Right)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    if (!TryAdjustAudioSwitcherSlider(1))
                    {
                        MoveAudioSwitcherFocus(1);
                    }
                }
                return true;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    MoveAudioSwitcherFocus(-1);
                }
                return true;
            }

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    MoveAudioSwitcherFocus(1);
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                // Stop the overlay global Enter/Space handler, but keep the event unhandled
                // so the focused hosted control can process its native activation once.
                e.Handled = false;
                return true;
            }

            return false;
        }

        private void FocusFirstUniPlaySongElement()
        {
            var elements = GetUniPlaySongControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            FocusUniPlaySongElement(elements[0]);
        }

        private FrameworkElement[] GetUniPlaySongControllerElements()
        {
            if (!isUniPlaySongVisible || uniPlaySongHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                uniPlaySongHost.ApplyTemplate();
                uniPlaySongHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(uniPlaySongHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element))
                    {
                        continue;
                    }

                    if (element is RepeatButton)
                    {
                        continue;
                    }

                    if (element is ButtonBase || element is RangeBase)
                    {
                        result.Add(element);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return new FrameworkElement[0];
            }
        }

        private void FocusUniPlaySongElement(FrameworkElement element)
        {
            try
            {
                if (!CanElementReceiveControllerFocus(element))
                {
                    return;
                }

                controllerFocusedUniPlaySongElement = element;
                controllerFocusedButton = element as Button;
                useControllerFocusVisual = true;

                element.Focus();
                Keyboard.Focus(element);
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private FrameworkElement GetCurrentUniPlaySongElement()
        {
            var elements = GetUniPlaySongControllerElements();
            if (elements.Length == 0)
            {
                return null;
            }

            foreach (var element in elements)
            {
                if (element != null && element.IsKeyboardFocusWithin)
                {
                    return element;
                }
            }

            if (controllerFocusedUniPlaySongElement != null &&
                Array.IndexOf(elements, controllerFocusedUniPlaySongElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedUniPlaySongElement))
            {
                return controllerFocusedUniPlaySongElement;
            }

            return elements[0];
        }

        private void MoveUniPlaySongFocus(int direction)
        {
            var elements = GetUniPlaySongControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            var current = GetCurrentUniPlaySongElement();
            var index = Array.IndexOf(elements, current);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + elements.Length) % elements.Length;
            }

            FocusUniPlaySongElement(elements[index]);
        }

        private bool ActivateUniPlaySongElement()
        {
            var current = GetCurrentUniPlaySongElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                var toggle = current as ToggleButton;
                if (toggle != null)
                {
                    toggle.IsChecked = toggle.IsChecked != true;
                    return true;
                }

                var button = current as Button;
                if (button != null)
                {
                    controllerFocusedButton = button;
                    ClickForcedControllerFocusedButton();
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase != null)
                {
                    if (buttonBase.Command != null && buttonBase.Command.CanExecute(buttonBase.CommandParameter))
                    {
                        buttonBase.Command.Execute(buttonBase.CommandParameter);
                    }
                    else
                    {
                        buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    }
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool HandleUniPlaySongControllerInput(ControllerInput button)
        {
            if (!isUniPlaySongVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveUniPlaySongFocus(-1);
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveUniPlaySongFocus(1);
                    }
                    return true;

                case ControllerInput.A:
                    // Hosted overlay views already receive native controller activation
                    // through Playnite/WPF. Forcing another click here makes buttons/toggles
                    // execute twice, so we consume only the custom overlay A input.
                    return true;
            }

            return false;
        }

        private bool HandleUniPlaySongPreviewKeyDown(KeyEventArgs e)
        {
            if (!isUniPlaySongVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    MoveUniPlaySongFocus(-1);
                }
                return true;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    MoveUniPlaySongFocus(1);
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                // Stop the overlay global Enter/Space handler, but keep the event unhandled
                // so the focused hosted control can process its native activation once.
                e.Handled = false;
                return true;
            }

            return false;
        }

        private void FocusFirstFriendsElement()
        {
            var elements = GetFriendsControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            FocusFriendsElement(elements[0]);
        }

        private FrameworkElement[] GetFriendsControllerElements()
        {
            if (!isFriendsVisible || friendsHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                friendsHost.ApplyTemplate();
                friendsHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(friendsHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element))
                    {
                        continue;
                    }

                    if (element is RepeatButton)
                    {
                        continue;
                    }

                    if (element is ButtonBase || element is RangeBase)
                    {
                        result.Add(element);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return new FrameworkElement[0];
            }
        }

        private void FocusFriendsElement(FrameworkElement element)
        {
            try
            {
                if (!CanElementReceiveControllerFocus(element))
                {
                    return;
                }

                controllerFocusedFriendsElement = element;
                controllerFocusedButton = element as Button;
                useControllerFocusVisual = true;

                element.Focus();
                Keyboard.Focus(element);
                element.BringIntoView();
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private FrameworkElement GetCurrentFriendsElement()
        {
            var elements = GetFriendsControllerElements();
            if (elements.Length == 0)
            {
                return null;
            }

            foreach (var element in elements)
            {
                if (element != null && element.IsKeyboardFocusWithin)
                {
                    return element;
                }
            }

            if (controllerFocusedFriendsElement != null &&
                Array.IndexOf(elements, controllerFocusedFriendsElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedFriendsElement))
            {
                return controllerFocusedFriendsElement;
            }

            return elements[0];
        }

        private void MoveFriendsFocus(int direction)
        {
            var elements = GetFriendsControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            var current = GetCurrentFriendsElement();
            var index = Array.IndexOf(elements, current);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + elements.Length) % elements.Length;
            }

            FocusFriendsElement(elements[index]);
        }

        private bool ActivateFriendsElement()
        {
            var current = GetCurrentFriendsElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                var button = current as Button;
                if (button != null)
                {
                    controllerFocusedButton = button;
                    ClickForcedControllerFocusedButton();
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase != null)
                {
                    if (buttonBase.Command != null && buttonBase.Command.CanExecute(buttonBase.CommandParameter))
                    {
                        buttonBase.Command.Execute(buttonBase.CommandParameter);
                    }
                    else
                    {
                        buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    }
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool HandleFriendsControllerInput(ControllerInput button)
        {
            if (!isFriendsVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveFriendsFocus(-1);
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveFriendsFocus(1);
                    }
                    return true;

                case ControllerInput.A:
                    // Hosted overlay views already receive native controller activation
                    // through Playnite/WPF. Forcing another click here makes buttons/toggles
                    // execute twice, so we consume only the custom overlay A input.
                    return true;

                case ControllerInput.B:
                case ControllerInput.Back:
                    HideFriends();
                    return true;
            }

            return false;
        }

        private bool HandleFriendsPreviewKeyDown(KeyEventArgs e)
        {
            if (!isFriendsVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    MoveFriendsFocus(-1);
                }
                return true;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    MoveFriendsFocus(1);
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                // Stop the overlay global Enter/Space handler, but keep the event unhandled
                // so the focused hosted control can process its native activation once.
                e.Handled = false;
                return true;
            }

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                e.Handled = true;
                HideFriends();
                return true;
            }

            return false;
        }

        private void FocusFirstAchievementsElement()
        {
            var elements = GetAchievementsControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            FocusAchievementsElement(elements[0]);
        }

        private FrameworkElement[] GetAchievementsControllerElements()
        {
            if (!isAchievementsVisible || achievementsHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                achievementsHost.ApplyTemplate();
                achievementsHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(achievementsHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element))
                    {
                        continue;
                    }

                    if (element is RepeatButton)
                    {
                        continue;
                    }

                    if (element is ButtonBase || element is RangeBase)
                    {
                        result.Add(element);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return new FrameworkElement[0];
            }
        }

        private void FocusAchievementsElement(FrameworkElement element)
        {
            try
            {
                if (!CanElementReceiveControllerFocus(element))
                {
                    return;
                }

                controllerFocusedAchievementsElement = element;
                controllerFocusedButton = element as Button;
                useControllerFocusVisual = true;

                element.Focus();
                Keyboard.Focus(element);
                element.BringIntoView();
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private FrameworkElement GetCurrentAchievementsElement()
        {
            var elements = GetAchievementsControllerElements();
            if (elements.Length == 0)
            {
                return null;
            }

            foreach (var element in elements)
            {
                if (element != null && element.IsKeyboardFocusWithin)
                {
                    return element;
                }
            }

            if (controllerFocusedAchievementsElement != null &&
                Array.IndexOf(elements, controllerFocusedAchievementsElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedAchievementsElement))
            {
                return controllerFocusedAchievementsElement;
            }

            return elements[0];
        }

        private void MoveAchievementsFocus(int direction)
        {
            var elements = GetAchievementsControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            var current = GetCurrentAchievementsElement();
            var index = Array.IndexOf(elements, current);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + elements.Length) % elements.Length;
            }

            FocusAchievementsElement(elements[index]);
        }

        private bool ActivateAchievementsElement()
        {
            var current = GetCurrentAchievementsElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                var button = current as Button;
                if (button != null)
                {
                    controllerFocusedButton = button;
                    ClickForcedControllerFocusedButton();
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase != null)
                {
                    if (buttonBase.Command != null && buttonBase.Command.CanExecute(buttonBase.CommandParameter))
                    {
                        buttonBase.Command.Execute(buttonBase.CommandParameter);
                    }
                    else
                    {
                        buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    }
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool HandleAchievementsControllerInput(ControllerInput button)
        {
            if (!isAchievementsVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveAchievementsFocus(-1);
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveAchievementsFocus(1);
                    }
                    return true;

                case ControllerInput.A:
                    // Hosted overlay views already receive native controller activation
                    // through Playnite/WPF. Forcing another click here makes buttons/toggles
                    // execute twice, so we consume only the custom overlay A input.
                    return true;
            }

            return false;
        }

        private bool HandleAchievementsPreviewKeyDown(KeyEventArgs e)
        {
            if (!isAchievementsVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    MoveAchievementsFocus(-1);
                }
                return true;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    MoveAchievementsFocus(1);
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                // Stop the overlay global Enter/Space handler, but keep the event unhandled
                // so the focused hosted control can process its native activation once.
                e.Handled = false;
                return true;
            }

            return false;
        }

        private void FocusFirstAppsElement()
        {
            var elements = GetAppsControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            FocusAppsElement(elements[0]);
        }

        private FrameworkElement[] GetAppsControllerElements()
        {
            if (!isAppsVisible || appsHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                appsHost.ApplyTemplate();
                appsHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(appsHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element))
                    {
                        continue;
                    }

                    if (element is RepeatButton)
                    {
                        continue;
                    }

                    if (element is ButtonBase || element is RangeBase)
                    {
                        result.Add(element);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return new FrameworkElement[0];
            }
        }

        private void FocusAppsElement(FrameworkElement element)
        {
            try
            {
                if (!CanElementReceiveControllerFocus(element))
                {
                    return;
                }

                controllerFocusedAppsElement = element;
                controllerFocusedButton = element as Button;
                useControllerFocusVisual = true;

                element.Focus();
                Keyboard.Focus(element);
                element.BringIntoView();
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private FrameworkElement GetCurrentAppsElement()
        {
            var elements = GetAppsControllerElements();
            if (elements.Length == 0)
            {
                return null;
            }

            foreach (var element in elements)
            {
                if (element != null && element.IsKeyboardFocusWithin)
                {
                    return element;
                }
            }

            if (controllerFocusedAppsElement != null &&
                Array.IndexOf(elements, controllerFocusedAppsElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedAppsElement))
            {
                return controllerFocusedAppsElement;
            }

            return elements[0];
        }

        private void MoveAppsFocus(int direction)
        {
            var elements = GetAppsControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            var current = GetCurrentAppsElement();
            var index = Array.IndexOf(elements, current);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + elements.Length) % elements.Length;
            }

            FocusAppsElement(elements[index]);
        }

        private bool ActivateAppsElement()
        {
            var current = GetCurrentAppsElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                var button = current as Button;
                if (button != null)
                {
                    controllerFocusedButton = button;
                    ClickForcedControllerFocusedButton();
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase != null)
                {
                    if (buttonBase.Command != null && buttonBase.Command.CanExecute(buttonBase.CommandParameter))
                    {
                        buttonBase.Command.Execute(buttonBase.CommandParameter);
                    }
                    else
                    {
                        buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    }
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool HandleAppsControllerInput(ControllerInput button)
        {
            if (!isAppsVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveAppsFocus(-1);
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveAppsFocus(1);
                    }
                    return true;

                case ControllerInput.A:
                    // Hosted overlay views already receive native controller activation
                    // through Playnite/WPF. Forcing another click here makes buttons/toggles
                    // execute twice, so we consume only the custom overlay A input.
                    return true;
            }

            return false;
        }

        private bool HandleAppsPreviewKeyDown(KeyEventArgs e)
        {
            if (!isAppsVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    MoveAppsFocus(-1);
                }
                return true;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    MoveAppsFocus(1);
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                // Stop the overlay global Enter/Space handler, but keep the event unhandled
                // so the focused hosted control can process its native activation once.
                e.Handled = false;
                return true;
            }

            return false;
        }

        private void FocusFirstLastCapturesElement()
        {
            var elements = GetLastCapturesControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            FocusLastCapturesElement(elements[0]);
        }

        private FrameworkElement[] GetLastCapturesControllerElements()
        {
            if (!isLastCapturesVisible || lastCapturesHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                lastCapturesHost.ApplyTemplate();
                lastCapturesHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(lastCapturesHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element))
                    {
                        continue;
                    }

                    if (element is RepeatButton)
                    {
                        continue;
                    }

                    if (element is ButtonBase || element is RangeBase)
                    {
                        result.Add(element);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return new FrameworkElement[0];
            }
        }

        private void FocusLastCapturesElement(FrameworkElement element)
        {
            try
            {
                if (!CanElementReceiveControllerFocus(element))
                {
                    return;
                }

                controllerFocusedLastCapturesElement = element;
                controllerFocusedButton = element as Button;
                useControllerFocusVisual = true;

                element.Focus();
                Keyboard.Focus(element);
                element.BringIntoView();
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private FrameworkElement GetCurrentLastCapturesElement()
        {
            var elements = GetLastCapturesControllerElements();
            if (elements.Length == 0)
            {
                return null;
            }

            foreach (var element in elements)
            {
                if (element != null && element.IsKeyboardFocusWithin)
                {
                    return element;
                }
            }

            if (controllerFocusedLastCapturesElement != null &&
                Array.IndexOf(elements, controllerFocusedLastCapturesElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedLastCapturesElement))
            {
                return controllerFocusedLastCapturesElement;
            }

            return elements[0];
        }

        private void MoveLastCapturesFocus(int direction)
        {
            var elements = GetLastCapturesControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            var current = GetCurrentLastCapturesElement();
            var index = Array.IndexOf(elements, current);
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = (index + direction + elements.Length) % elements.Length;
            }

            FocusLastCapturesElement(elements[index]);
        }

        private bool ActivateLastCapturesElement()
        {
            var current = GetCurrentLastCapturesElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                var button = current as Button;
                if (button != null)
                {
                    controllerFocusedButton = button;
                    ClickForcedControllerFocusedButton();
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase != null)
                {
                    if (buttonBase.Command != null && buttonBase.Command.CanExecute(buttonBase.CommandParameter))
                    {
                        buttonBase.Command.Execute(buttonBase.CommandParameter);
                    }
                    else
                    {
                        buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    }
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool HandleLastCapturesControllerInput(ControllerInput button)
        {
            if (!isLastCapturesVisible)
            {
                return false;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveLastCapturesFocus(-1);
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveLastCapturesFocus(1);
                    }
                    return true;

                case ControllerInput.A:
                    // Hosted overlay views already receive native controller activation
                    // through Playnite/WPF. Forcing another click here makes buttons/toggles
                    // execute twice, so we consume only the custom overlay A input.
                    return true;
            }

            return false;
        }

        private bool HandleLastCapturesPreviewKeyDown(KeyEventArgs e)
        {
            if (!isLastCapturesVisible || e == null)
            {
                return false;
            }

            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    MoveLastCapturesFocus(-1);
                }
                return true;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    MoveLastCapturesFocus(1);
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                // Stop the overlay global Enter/Space handler, but keep the event unhandled
                // so the focused hosted control can process its native activation once.
                e.Handled = false;
                return true;
            }

            return false;
        }

        private static void CollectVisualChildren<T>(DependencyObject parent, List<T> result)
            where T : DependencyObject
        {
            if (parent == null || result == null)
            {
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    result.Add(typedChild);
                }

                CollectVisualChildren(child, result);
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

            if (HandleCapturePreviewControllerInput(button))
            {
                lastDirectOverlayControllerInputTime = DateTime.Now;
                return;
            }

            var isChildViewVisible = IsOverlayChildViewVisible();

            // In hosted overlay views, A is already handled by the focused Playnite/WPF control.
            // Do not mark it as a direct overlay input, otherwise OnPreviewKeyDown may block
            // the native Enter/Space event or the custom click can execute a second time.
            if (!(isChildViewVisible && button == ControllerInput.A))
            {
                lastDirectOverlayControllerInputTime = DateTime.Now;
            }

            if ((button == ControllerInput.B || button == ControllerInput.Back) &&
                (DateTime.Now - lastCapturePreviewClosedTime).TotalMilliseconds < 250)
            {
                return;
            }

            if ((isMusicPlayerVisible || isAudioSwitcherVisible || isUniPlaySongVisible || isFriendsVisible || isLastCapturesVisible || isAppsVisible || isAchievementsVisible) && (button == ControllerInput.B || button == ControllerInput.Back))
            {
                if (isMusicPlayerVisible)
                {
                    HideMusicPlayer();
                }
                else if (isAudioSwitcherVisible)
                {
                    HideAudioSwitcher();
                }
                else if (isUniPlaySongVisible)
                {
                    HideUniPlaySong();
                }
                else if (isFriendsVisible)
                {
                    HideFriends();
                }
                else if (isLastCapturesVisible)
                {
                    HideLastCaptures();
                }
                else if (isAppsVisible)
                {
                    HideApps();
                }
                else
                {
                    HideAchievements();
                }

                return;
            }

            if (HandleMusicPlayerControllerInput(button))
            {
                return;
            }

            if (HandleAudioSwitcherControllerInput(button))
            {
                return;
            }

            if (HandleUniPlaySongControllerInput(button))
            {
                return;
            }

            if (HandleFriendsControllerInput(button))
            {
                return;
            }

            if (HandleLastCapturesControllerInput(button))
            {
                return;
            }

            if (HandleAppsControllerInput(button))
            {
                return;
            }

            if (HandleAchievementsControllerInput(button))
            {
                return;
            }

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    System.Diagnostics.Debug.WriteLine("[AnikiHelper][OverlayWindow] Move focus PREVIOUS");
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveForcedControllerFocus(-1);
                    }
                    break;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    System.Diagnostics.Debug.WriteLine("[AnikiHelper][OverlayWindow] Move focus NEXT");
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveForcedControllerFocus(1);
                    }
                    break;

                case ControllerInput.A:
                    if (CanProcessControllerAction())
                    {
                        Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            await System.Threading.Tasks.Task.Delay(80);
                            ClickForcedControllerFocusedButton();
                        }), DispatcherPriority.Background);
                    }
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

            if (HandleCapturePreviewControllerInput(args.Button))
            {
                lastDirectOverlayControllerInputTime = DateTime.Now;
                return;
            }

            if ((args.Button == ControllerInput.B || args.Button == ControllerInput.Back) &&
                (DateTime.Now - lastCapturePreviewClosedTime).TotalMilliseconds < 250)
            {
                return;
            }

            if ((isMusicPlayerVisible || isAudioSwitcherVisible || isUniPlaySongVisible || isFriendsVisible || isLastCapturesVisible || isAppsVisible || isAchievementsVisible) && (args.Button == ControllerInput.B || args.Button == ControllerInput.Back))
            {
                if (isMusicPlayerVisible)
                {
                    HideMusicPlayer();
                }
                else if (isAudioSwitcherVisible)
                {
                    HideAudioSwitcher();
                }
                else if (isUniPlaySongVisible)
                {
                    HideUniPlaySong();
                }
                else if (isFriendsVisible)
                {
                    HideFriends();
                }
                else if (isLastCapturesVisible)
                {
                    HideLastCaptures();
                }
                else if (isAppsVisible)
                {
                    HideApps();
                }
                else
                {
                    HideAchievements();
                }

                return;
            }

            if (HandleMusicPlayerControllerInput(args.Button))
            {
                return;
            }

            if (HandleAudioSwitcherControllerInput(args.Button))
            {
                return;
            }

            if (HandleUniPlaySongControllerInput(args.Button))
            {
                return;
            }

            if (HandleFriendsControllerInput(args.Button))
            {
                return;
            }

            if (HandleLastCapturesControllerInput(args.Button))
            {
                return;
            }

            if (HandleAppsControllerInput(args.Button))
            {
                return;
            }

            if (HandleAchievementsControllerInput(args.Button))
            {
                return;
            }

            switch (args.Button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (CanProcessControllerNavigation(-1))
                    {
                        MoveForcedControllerFocus(-1);
                    }
                    break;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (CanProcessControllerNavigation(1))
                    {
                        MoveForcedControllerFocus(1);
                    }
                    break;

                case ControllerInput.A:
                    if (CanProcessControllerAction())
                    {
                        ClickForcedControllerFocusedButton();
                    }
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

            // One physical controller press can arrive twice:
            // 1) through the overlay SDL listener
            // 2) through WPF/Playnite PreviewKeyDown as an arrow key
            //
            // Keep a short shared gate for both paths. 160ms is enough
            // to block the duplicate, but still feels much faster than 280ms.
            if (elapsed < 160)
            {
                return false;
            }

            lastControllerNavigationDirection = direction;
            lastControllerNavigationTime = now;
            return true;
        }

        private bool CanProcessControllerAction()
        {
            var now = DateTime.Now;
            var elapsed = (now - lastControllerActionTime).TotalMilliseconds;

            if (elapsed < 220)
            {
                return false;
            }

            lastControllerActionTime = now;
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

            if (isMusicPlayerVisible)
            {
                MoveMusicPlayerFocus(direction);
                return;
            }

            if (isAudioSwitcherVisible)
            {
                MoveAudioSwitcherFocus(direction);
                return;
            }

            if (isUniPlaySongVisible)
            {
                MoveUniPlaySongFocus(direction);
                return;
            }

            if (isFriendsVisible)
            {
                MoveFriendsFocus(direction);
                return;
            }

            if (isLastCapturesVisible)
            {
                MoveLastCapturesFocus(direction);
                return;
            }

            if (isAppsVisible)
            {
                MoveAppsFocus(direction);
                return;
            }

            if (isAchievementsVisible)
            {
                MoveAchievementsFocus(direction);
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
            var audioSwitcherButtons = GetAudioSwitcherButtons();
            if (audioSwitcherButtons.Length > 0)
            {
                return audioSwitcherButtons;
            }

            var musicPlayerButtons = GetMusicPlayerButtons();
            if (musicPlayerButtons.Length > 0)
            {
                return musicPlayerButtons;
            }

            if (service.IsGameRunning)
            {
                return new[] { returnButton, achievementsSectionButton, friendsButton, mediaSectionButton, musicButton, uniPlaySongButton, audioSectionButton, appsButton, quitButton }
                    .WhereButtonCanReceiveControllerFocus();
            }

            return new[] { musicButton, uniPlaySongButton, audioSectionButton, appsButton, friendsButton, mediaSectionButton }.WhereButtonCanReceiveControllerFocus();
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
                    if (!service.IsGameRunning)
                    {
                        service.HideOverlay();
                    }
                    else if (service.OverlayOpenedFromPlaynite)
                    {
                        service.ReturnToGame();
                    }
                    else
                    {
                        service.ReturnToPlaynite();
                    }

                    return;
                }

                if (button == gameSectionButton)
                {
                    SelectSection(OverlaySection.Game);
                    return;
                }

                if (button == mediaSectionButton)
                {
                    service.OpenLastCapturesWindow();
                    return;
                }

                if (button == audioSectionButton)
                {
                    service.OpenAudioSwitcherWindow();
                    return;
                }

                if (button == friendsButton)
                {
                    service.OpenFriendsWindow();
                    return;
                }

                if (button == appsButton)
                {
                    service.OpenAppsWindow();
                    return;
                }

                if (button == uniPlaySongButton)
                {
                    service.OpenUniPlaySongWindow();
                    return;
                }

                if (button == musicButton)
                {
                    service.OpenMusicPlayerWindow();
                    return;
                }

                if (button == achievementsSectionButton)
                {
                    service.OpenAchievementsWindow();
                    return;
                }

                if (button == quitButton)
                {
                    service.RequestQuitGame();
                    return;
                }

                if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                {
                    button.Command.Execute(button.CommandParameter);
                    return;
                }

                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
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
                    var mainButtons = GetMainControllerButtons();
                    target = mainButtons.Length > 0 ? mainButtons[0] : null;
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

            if (HandleCapturePreviewKeyDown(e))
            {
                return;
            }

            if ((e.Key == Key.Escape || e.Key == Key.Back) &&
                (now - lastCapturePreviewClosedTime).TotalMilliseconds < 250)
            {
                e.Handled = true;
                return;
            }

            if (HandleMusicPlayerPreviewKeyDown(e))
            {
                return;
            }

            if (HandleAudioSwitcherPreviewKeyDown(e))
            {
                return;
            }

            if (HandleUniPlaySongPreviewKeyDown(e))
            {
                return;
            }

            if (HandleFriendsPreviewKeyDown(e))
            {
                return;
            }

            if (HandleLastCapturesPreviewKeyDown(e))
            {
                return;
            }

            if (HandleAppsPreviewKeyDown(e))
            {
                return;
            }

            if (HandleAchievementsPreviewKeyDown(e))
            {
                return;
            }

            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                e.Handled = true;

                var direction = e.Key == Key.Down || e.Key == Key.Right ? 1 : -1;

                if (CanProcessControllerNavigation(direction))
                {
                    useControllerFocusVisual = true;
                    MoveForcedControllerFocus(direction);
                }

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

                if (isMusicPlayerVisible)
                {
                    HideMusicPlayer();
                }
                else if (isAudioSwitcherVisible)
                {
                    HideAudioSwitcher();
                }
                else if (isUniPlaySongVisible)
                {
                    HideUniPlaySong();
                }
                else if (isFriendsVisible)
                {
                    HideFriends();
                }
                else if (isLastCapturesVisible)
                {
                    HideLastCaptures();
                }
                else if (isAppsVisible)
                {
                    HideApps();
                }
                else if (isAchievementsVisible)
                {
                    HideAchievements();
                }
                else if (isQuitConfirmationVisible)
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