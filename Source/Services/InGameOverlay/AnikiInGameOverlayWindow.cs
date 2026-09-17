using AnikiHelper.Services.MediaGallery;
using AnikiHelper.Services.UI;
using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
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
        private const string IconGame = "\uE768";
        private const string IconMedia = "\uE91B";
        private const string IconAudio = "\uE995";
        private const string IconFriends = "\uE716";
        private const string IconMusic = "\uE8D6";
        private const string IconTrophy = "\uE903";
        private const string IconKeyboard = "\uE765";
        private const string IconApps = "\uECAA";
        private const string IconLink = "\uEF71";

        private enum OverlaySection
        {
            Game,
            Media,
            Audio,
            Achievements
        }

        private sealed class AchievementsSelectedGameContext
        {
            public Guid Id { get; set; }
            public string DisplayName { get; set; }
            public string BackgroundImage { get; set; }
        }

        private sealed class AchievementsThemeContext
        {
            public object SelectedGame { get; set; }
        }

        private OverlaySection activeSection = OverlaySection.Game;
        private bool isSectionPanelOpen;
        private readonly List<Button> topBarButtons = new List<Button>();

        private StackPanel overlayButtonStack;
        private Button resumeButton;
        private TextBlock resumeButtonIconText;
        private TextBlock resumeButtonLabelText;
        private Border quickGameCard;
        private Border quickGameCoverBorder;
        private Image quickGameCoverImage;
        private TextBlock quickGameTitleText;
        private TextBlock quickGameMetaText;
        private TextBlock quickGamePlaytimeText;
        private TextBlock quickGameSessionText;
        private Grid quickGameAchievementRow;
        private TextBlock quickGameAchievementLabelText;
        private TextBlock quickGameAchievementValueText;
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
        private ContentControl friendsActionHost;
        private bool isFriendsActionVisible;
        private ContentControl friendsProfileHost;
        private bool isFriendsProfileVisible;
        private ContentControl lastCapturesHost;
        private bool isLastCapturesVisible;
        private Grid capturePreviewLayer;
        private Image capturePreviewImage;
        private TextBlock capturePreviewTitleText;
        private TextBlock capturePreviewMetaText;
        private TextBlock capturePreviewIndexText;
        private TextBlock capturePreviewFooterText;
        private Window captureVideoPreviewWindow;
        private MediaElement capturePreviewVideo;
        private TextBlock capturePreviewVideoTitleText;
        private TextBlock capturePreviewVideoMetaText;
        private TextBlock capturePreviewVideoIndexText;
        private bool capturePreviewVideoPaused;
        private bool captureVideoWindowClosingInternally;
        private AnikiMediaItem capturePreviewItem;
        private bool isCapturePreviewVisible;
        private ContentControl appsHost;
        private bool isAppsVisible;
        private ContentControl gameLinksHost;
        private bool isGameLinksVisible;
        private ButtonBase gameLinksCloseButton;
        private ContentControl achievementsHost;
        private bool isAchievementsVisible;
        private ContentControl achievementOptionsHost;
        private bool isAchievementOptionsVisible;
        private ContentControl achievementActionsHost;
        private bool isAchievementActionsVisible;
        private ContentControl achievementCaptureHost;
        private bool isAchievementCaptureVisible;
        private string achievementReturnApiName = string.Empty;
        private string achievementReturnName = string.Empty;
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
        private Button musicButton;
        private Button appsButton;
        private Button gameLinksButton;
        private Button achievementsSectionButton;
        private Button keyboardButton;
        private AnikiVirtualKeyboardView virtualKeyboardView;
        private bool virtualKeyboardOpenedDirectly;

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

        public bool SuppressInitialActivation { get; set; }
        private DateTime lastControllerNavigationTime = DateTime.MinValue;
        private int lastControllerNavigationDirection = 0;
        private DateTime lastControllerActionTime = DateTime.MinValue;
        private DateTime lastDirectOverlayControllerInputTime = DateTime.MinValue;
        private DateTime lastNativeOverlayNavigationUtc = DateTime.MinValue;
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
                   isGameLinksVisible ||
                   isAchievementsVisible ||
                   (virtualKeyboardView != null && virtualKeyboardView.IsOpen);
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
                if (SuppressInitialActivation)
                {
                    PrepareControllerFocusWithoutActivation();
                    return;
                }

                Activate();

                if (virtualKeyboardView != null && virtualKeyboardView.IsOpen)
                {
                    Focus();
                }
                else
                {
                    FocusOverlayButton();
                }
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
            RefreshQuickGameCard();
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
                            userAvatarImage.Source = ImageMemoryCache.GetOrLoad(avatarPath, 256);
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

                var bitmap = ImageMemoryCache.GetOrLoad(coverPath, 340);
                if (bitmap == null)
                {
                    throw new InvalidOperationException("Unable to load the game cover image.");
                }

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

                var bitmap = ImageMemoryCache.GetOrLoad(backgroundPath, 1920);
                if (bitmap == null)
                {
                    throw new InvalidOperationException("Unable to load the game background image.");
                }

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

            overlayButtonStack.Children.Clear();

            Action addDivider = () =>
            {
                overlayButtonStack.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(12, 8, 12, 14),
                    Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                    IsHitTestVisible = false
                });
            };

            Action<Button> addButton = button =>
            {
                if (button != null && button.Visibility == Visibility.Visible)
                {
                    overlayButtonStack.Children.Add(button);
                }
            };

            if (isRunning)
            {
                addButton(resumeButton);
                addButton(returnButton);
                addButton(keyboardButton);
                addDivider();

                addButton(achievementsSectionButton);
                addButton(friendsButton);
                addButton(mediaSectionButton);
                addButton(gameLinksButton);
                addDivider();

                addButton(musicButton);
                addButton(audioSectionButton);
                addButton(appsButton);
                addDivider();

                addButton(quitButton);
            }
            else
            {
                addButton(musicButton);
                addButton(audioSectionButton);
                addButton(appsButton);
                addDivider();

                addButton(friendsButton);
                addButton(mediaSectionButton);
                addButton(keyboardButton);
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
            SetSectionButtonEnabled(musicButton, true);
            SetSectionButtonEnabled(appsButton, true);
            SetSectionButtonEnabled(gameLinksButton, isRunning);
            SetSectionButtonEnabled(keyboardButton, true);
            SetSectionButtonEnabled(achievementsSectionButton, isRunning && service.IsPlayniteAchievementsInstalled);

            var openedFromPlaynite = isRunning && service.OverlayOpenedFromPlaynite;

            if (resumeButton != null)
            {
                resumeButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
                resumeButton.IsEnabled = isRunning;
                resumeButton.IsTabStop = isRunning;
                resumeButton.Focusable = isRunning;
            }

            if (resumeButtonIconText != null)
            {
                resumeButtonIconText.Text = IconGame;
            }

            if (resumeButtonLabelText != null)
            {
                resumeButtonLabelText.Text = openedFromPlaynite
                    ? Loc("LOCInGameOverlayReturnToGame", "Return to Game")
                    : Loc("LOCInGameOverlayResumeGame", "Resume Game");
            }

            if (quitButton != null)
            {
                quitButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
                quitButton.IsEnabled = isRunning;
                quitButton.IsTabStop = isRunning;
                quitButton.Focusable = isRunning;
            }

            // If the overlay was opened from Playnite, "Return to Playnite" would be
            // redundant. Keep only the game action and name it "Return to Game".
            var showReturnToPlaynite = isRunning && !openedFromPlaynite;
            if (returnButton != null)
            {
                returnButton.Visibility = showReturnToPlaynite ? Visibility.Visible : Visibility.Collapsed;
                returnButton.IsEnabled = showReturnToPlaynite;
                returnButton.IsTabStop = showReturnToPlaynite;
                returnButton.Focusable = showReturnToPlaynite;
            }

            RefreshReturnButtonMode();
            RebuildOverlayButtonOrder(isRunning);

            firstButton = isRunning
                ? resumeButton
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

                if (returnButtonIconText != null)
                {
                    returnButtonIconText.Text = service.IsGameRunning ? IconHome : IconBack;
                }

                returnButtonLabelText.Text = service.IsGameRunning
                    ? Loc("LOCInGameOverlayReturnToPlaynite", "Return to Playnite")
                    : Loc("LOCInGameOverlayClose", "Close");
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
                        var bitmap = ImageMemoryCache.GetOrLoad(iconPath, 256);
                        if (bitmap == null)
                        {
                            throw new InvalidOperationException("Unable to load the latest achievement icon.");
                        }

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

                    if (quickGameSessionText != null && service.IsGameRunning)
                    {
                        quickGameSessionText.Text = service.CurrentGameSessionTimeValue;
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

        private FrameworkElement CreateConnectedDevicesFooter()
        {
            // Keep this UI inside the Helper: the overlay must not require any Aniki ReMake
            // XAML modification. The bindings are the same ones used by QuickAccessMenu.xaml.
            const string xaml = @"
<Grid xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
      xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
      xmlns:pm=""clr-namespace:Playnite.Extensions.Markup;assembly=Playnite""
      HorizontalAlignment=""Left""
      VerticalAlignment=""Center""
      IsHitTestVisible=""False""
      Focusable=""False"">


    <StackPanel Orientation=""Horizontal""
                VerticalAlignment=""Center""
                HorizontalAlignment=""Left""
                IsHitTestVisible=""False""
                Focusable=""False"">
        <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""0,0,15,0"">
            <StackPanel.Style>
                <Style TargetType=""StackPanel"">
                    <Setter Property=""Visibility"" Value=""Collapsed""/>
                    <Style.Triggers>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=HasConnectedControllers, FallbackValue=False}"" Value=""True"">
                            <Setter Property=""Visibility"" Value=""Visible""/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <Viewbox Width=""22"" Height=""22"" Margin=""0,0,5,0"">
                <Path Data=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=PrimaryControllerIconGeometry, Converter={pm:PluginConverter Plugin=ControllerSessionManager, Converter=IconGeometryConverter}}""
                      Stretch=""Uniform"" Fill=""#FFFFFFFF"" Stroke=""#FFFFFFFF"" StrokeThickness=""0.45"" StrokeLineJoin=""Round""/>
            </Viewbox>
            <TextBlock Text=""1"" Foreground=""#FFFFFFFF"" FontSize=""13"" FontWeight=""Bold"" VerticalAlignment=""Center""/>
            <TextBlock Text=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=PrimaryControllerBatteryLabel}""
                       Foreground=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=PrimaryControllerBatteryBrush}""
                       FontSize=""13"" FontWeight=""SemiBold"" Margin=""6,0,0,0"" VerticalAlignment=""Center"">
                <TextBlock.Style>
                    <Style TargetType=""TextBlock"">
                        <Setter Property=""Visibility"" Value=""Collapsed""/>
                        <Style.Triggers>
                            <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=HasPrimaryControllerBattery, FallbackValue=False}"" Value=""True"">
                                <Setter Property=""Visibility"" Value=""Visible""/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
        </StackPanel>

        <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""0,0,15,0"">
            <StackPanel.Style>
                <Style TargetType=""StackPanel"">
                    <Setter Property=""Visibility"" Value=""Collapsed""/>
                    <Style.Triggers>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=ConnectedCount, FallbackValue=0}"" Value=""2""><Setter Property=""Visibility"" Value=""Visible""/></DataTrigger>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=ConnectedCount, FallbackValue=0}"" Value=""3""><Setter Property=""Visibility"" Value=""Visible""/></DataTrigger>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=ConnectedCount, FallbackValue=0}"" Value=""4""><Setter Property=""Visibility"" Value=""Visible""/></DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <Viewbox Width=""22"" Height=""22"" Margin=""0,0,5,0"">
                <TextBlock Text=""&#xE7FC;"" FontFamily=""{DynamicResource FontIcons}"" FontSize=""24"" Foreground=""#FF4DA3FF"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
            </Viewbox>
            <TextBlock Text=""2"" Foreground=""#FF4DA3FF"" FontSize=""13"" FontWeight=""Bold"" VerticalAlignment=""Center""/>
        </StackPanel>

        <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""0,0,15,0"">
            <StackPanel.Style>
                <Style TargetType=""StackPanel"">
                    <Setter Property=""Visibility"" Value=""Collapsed""/>
                    <Style.Triggers>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=ConnectedCount, FallbackValue=0}"" Value=""3""><Setter Property=""Visibility"" Value=""Visible""/></DataTrigger>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=ConnectedCount, FallbackValue=0}"" Value=""4""><Setter Property=""Visibility"" Value=""Visible""/></DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <Viewbox Width=""22"" Height=""22"" Margin=""0,0,5,0"">
                <TextBlock Text=""&#xE7FC;"" FontFamily=""{DynamicResource FontIcons}"" FontSize=""24"" Foreground=""#FFFF9A3D"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
            </Viewbox>
            <TextBlock Text=""3"" Foreground=""#FFFF9A3D"" FontSize=""13"" FontWeight=""Bold"" VerticalAlignment=""Center""/>
        </StackPanel>

        <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""0,0,15,0"">
            <StackPanel.Style>
                <Style TargetType=""StackPanel"">
                    <Setter Property=""Visibility"" Value=""Collapsed""/>
                    <Style.Triggers>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=ControllerSessionManager, Path=ConnectedCount, FallbackValue=0}"" Value=""4""><Setter Property=""Visibility"" Value=""Visible""/></DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <Viewbox Width=""22"" Height=""22"" Margin=""0,0,5,0"">
                <TextBlock Text=""&#xE7FC;"" FontFamily=""{DynamicResource FontIcons}"" FontSize=""24"" Foreground=""#FF55C878"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
            </Viewbox>
            <TextBlock Text=""4"" Foreground=""#FF55C878"" FontSize=""13"" FontWeight=""Bold"" VerticalAlignment=""Center""/>
        </StackPanel>

        <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
            <StackPanel.Style>
                <Style TargetType=""StackPanel"">
                    <Setter Property=""Visibility"" Value=""Collapsed""/>
                    <Style.Triggers>
                        <DataTrigger Binding=""{pm:PluginSettings Plugin=AudioSwitcher, Path=HasCurrentDeviceBattery, FallbackValue=False}"" Value=""True"">
                            <Setter Property=""Visibility"" Value=""Visible""/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <Viewbox Width=""19"" Height=""19"" Margin=""0,0,5,0"">
                <Path Data=""{pm:PluginSettings Plugin=AudioSwitcher, Path=BatteryIndicatorIconGeometry}""
                      Stretch=""Uniform""
                      Stroke=""{DynamicResource TextBrush}""
                      StrokeThickness=""2""
                      StrokeStartLineCap=""Round""
                      StrokeEndLineCap=""Round""
                      StrokeLineJoin=""Round""
                      Fill=""Transparent""/>
            </Viewbox>
            <TextBlock Text=""{pm:PluginSettings Plugin=AudioSwitcher, Path=BatteryIndicatorLabel}""
                       Foreground=""{DynamicResource TextBrush}""
                       FontSize=""13""
                       FontWeight=""SemiBold""
                       VerticalAlignment=""Center""/>
        </StackPanel>
    </StackPanel>
</Grid>";

            try
            {
                var element = XamlReader.Parse(xaml) as FrameworkElement;
                if (element != null)
                {
                    return element;
                }
            }
            catch
            {
                // Keep the overlay usable if a future Playnite build changes its markup API.
            }

            // If the dynamic XAML cannot be created, keep this footer empty rather than
            // showing an unrelated fallback label.
            return new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Focusable = false
            };
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
                Background = new SolidColorBrush(Color.FromArgb(112, 0, 0, 0)),
                IsHitTestVisible = false,
                Opacity = 0
            };
            root.Children.Add(darkLayer);

            bottomDimLayer = new Border
            {
                Height = 210,
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
                        new GradientStop(Color.FromArgb(72, 0, 0, 0), 0.45),
                        new GradientStop(Color.FromArgb(160, 0, 0, 0), 1.0)
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
                Width = 430,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            panel.Children.Add(leftPanel);

            var panelBackground = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                Background = new SolidColorBrush(Color.FromArgb(246, 10, 14, 20)),
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
                MinHeight = 258,
                Margin = new Thickness(16, 14, 18, 14)
            };
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
            Grid.SetRow(identityHost, 0);
            header.Children.Add(identityHost);

            quickGameCard = CreateQuickGameCard();
            Grid.SetRow(quickGameCard, 1);
            header.Children.Add(quickGameCard);

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
                Margin = new Thickness(14, 14, 14, 10)
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

            resumeButton = CreateButton(IconGame, Loc("LOCInGameOverlayResumeGame", "Resume Game"), service.ReturnToGame);
            returnButton = CreateButton(IconHome, Loc("LOCInGameOverlayReturnToPlaynite", "Return to Playnite"), service.ReturnToPlaynite);
            keyboardButton = CreateButton(IconKeyboard, Loc("LOCInGameOverlayVirtualKeyboard", "Virtual Keyboard"), ShowVirtualKeyboard);
            mediaSectionButton = CreateButton(IconMedia, Loc("LOCInGameOverlayLastCaptures", "Last Captures"), service.OpenLastCapturesWindow);
            audioSectionButton = CreateButton(IconAudio, Loc("LOCInGameOverlayAudio", "Audio Switcher"), service.OpenAudioSwitcherWindow);
            friendsButton = CreateButton(IconFriends, Loc("LOCInGameOverlayFriends", "Friends"), service.OpenFriendsWindow);
            musicButton = CreateButton(IconMusic, Loc("LOCInGameOverlayMusic", "Music Player"), service.OpenMusicPlayerWindow);
            appsButton = CreateButton(IconApps, Loc("LOCInGameOverlayApps", "Apps"), service.OpenAppsWindow);
            gameLinksButton = CreateButton(IconLink, Loc("GameLinks_ButtonTooltip", "Game Links"), service.OpenGameLinksWindow, "FontIcoFont");
            achievementsSectionButton = CreateButton(IconTrophy, Loc("LOCInGameOverlayAchievements", "Achievements"), service.OpenAchievementsWindow, "FontIcomoon");
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

            footer.Children.Add(CreateConnectedDevicesFooter());

            sectionContentPanel = new Border
            {
                Width = 980,
                Height = 236,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(458, 146, 0, 0),
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
                Margin = new Thickness(458, 0, 34, 18),
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

            var mainHintBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.92,
                IsHitTestVisible = false
            };
            mainHintBar.Children.Add(CreateControllerHint("A", Loc("LOCSelectLabel", "Select")));
            mainHintBar.Children.Add(CreateControllerHint("B", Loc("LOCBackLabel", "Back")));

            var mainHintShell = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 32, 0),
                Padding = new Thickness(14, 8, 16, 8),
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromArgb(150, 5, 8, 12)),
                IsHitTestVisible = false,
                Child = mainHintBar
            };

            var bottomHintTemplate = FindThemeResource("AnikiControlCenterBottomHintTemplate") as DataTemplate;

            bottomHintHost = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = bottomHintTemplate != null ? 110 : 76,
                IsHitTestVisible = false,
                Focusable = false,
                Opacity = 0,
                Visibility = Visibility.Collapsed
            };

            if (bottomHintTemplate != null)
            {
                bottomHintHost.Content = service;
                bottomHintHost.ContentTemplate = bottomHintTemplate;
            }
            else
            {
                // Compatibility fallback for themes that do not provide the Aniki footer template.
                bottomHintHost.Content = mainHintShell;
            }
            System.Windows.Controls.Panel.SetZIndex(bottomHintHost, 12);
            root.Children.Add(bottomHintHost);

            virtualKeyboardView = new AnikiVirtualKeyboardView(Loc, service.GetVirtualKeyboardLayout, OnVirtualKeyboardSubmit, OnVirtualKeyboardClosed);
            System.Windows.Controls.Panel.SetZIndex(virtualKeyboardView, 300);
            root.Children.Add(virtualKeyboardView);

            RefreshSectionVisibility();
            Refresh();
        }

        private Border CreateQuickGameCard()
        {
            var card = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(12),
                MinHeight = 154,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            card.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.Child = grid;

            var coverViewport = new Grid
            {
                Width = 142,
                Height = 126,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            quickGameCoverBorder = new Border
            {
                Width = 84,
                Height = 126,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            quickGameCoverImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            quickGameCoverBorder.Child = quickGameCoverImage;
            coverViewport.Children.Add(quickGameCoverBorder);
            Grid.SetColumn(coverViewport, 0);
            grid.Children.Add(coverViewport);

            var infoGrid = new Grid
            {
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(infoGrid, 1);
            grid.Children.Add(infoGrid);

            quickGameTitleText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            quickGameTitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(quickGameTitleText, 0);
            infoGrid.Children.Add(quickGameTitleText);

            quickGameMetaText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.70,
                Margin = new Thickness(0, 5, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            quickGameMetaText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(quickGameMetaText, 1);
            infoGrid.Children.Add(quickGameMetaText);

            var statsGrid = new Grid
            {
                Margin = new Thickness(0, 9, 0, 0)
            };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var playtimeStack = new StackPanel { Orientation = Orientation.Vertical };
            playtimeStack.Children.Add(CreateQuickGameStatLabel(Loc("LOCInGameOverlayPlaytime", "Playtime")));
            quickGamePlaytimeText = CreateQuickGameStatValue();
            playtimeStack.Children.Add(quickGamePlaytimeText);
            Grid.SetColumn(playtimeStack, 0);
            statsGrid.Children.Add(playtimeStack);

            var sessionStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10, 0, 0, 0)
            };
            sessionStack.Children.Add(CreateQuickGameStatLabel(Loc("LOCInGameOverlaySession", "Session")));
            quickGameSessionText = CreateQuickGameStatValue();
            sessionStack.Children.Add(quickGameSessionText);
            Grid.SetColumn(sessionStack, 1);
            statsGrid.Children.Add(sessionStack);

            Grid.SetRow(statsGrid, 2);
            infoGrid.Children.Add(statsGrid);

            quickGameAchievementRow = new Grid
            {
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            quickGameAchievementRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var achievementTextStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            quickGameAchievementLabelText = new TextBlock
            {
                Text = Loc("LOCInGameOverlayAchievementsLatest", "Latest achievement"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.58,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            quickGameAchievementLabelText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            achievementTextStack.Children.Add(quickGameAchievementLabelText);

            quickGameAchievementValueText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            quickGameAchievementValueText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            achievementTextStack.Children.Add(quickGameAchievementValueText);

            Grid.SetColumn(achievementTextStack, 0);
            quickGameAchievementRow.Children.Add(achievementTextStack);
            Grid.SetRow(quickGameAchievementRow, 3);
            infoGrid.Children.Add(quickGameAchievementRow);

            return card;
        }

        private TextBlock CreateQuickGameStatLabel(string text)
        {
            var label = new TextBlock
            {
                Text = text ?? string.Empty,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.58,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return label;
        }

        private TextBlock CreateQuickGameStatValue()
        {
            var value = new TextBlock
            {
                Text = string.Empty,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return value;
        }

        private void ApplyQuickGameCoverAspectRatio()
        {
            if (quickGameCoverBorder == null)
            {
                return;
            }

            const double maxWidth = 142.0;
            const double maxHeight = 126.0;

            var widthRatio = Math.Max(1, service.CoverArtWidthRatio);
            var heightRatio = Math.Max(1, service.CoverArtHeightRatio);
            var aspect = widthRatio / (double)heightRatio;

            // Playnite allows custom ratios, not only its presets. Keep pathological custom
            // values usable inside the overlay while preserving every normal Playnite preset.
            aspect = Math.Max(0.45, Math.Min(2.50, aspect));

            double width;
            double height;
            if (aspect >= (maxWidth / maxHeight))
            {
                width = maxWidth;
                height = maxWidth / aspect;
            }
            else
            {
                height = maxHeight;
                width = maxHeight * aspect;
            }

            quickGameCoverBorder.Width = Math.Max(1.0, width);
            quickGameCoverBorder.Height = Math.Max(1.0, height);
        }

        private void RefreshQuickGameCard()
        {
            if (quickGameCard == null)
            {
                return;
            }

            if (!service.IsGameRunning)
            {
                quickGameCard.Visibility = Visibility.Collapsed;
                if (quickGameCoverImage != null)
                {
                    quickGameCoverImage.Source = null;
                }
                return;
            }

            quickGameCard.Visibility = Visibility.Visible;
            ApplyQuickGameCoverAspectRatio();

            if (quickGameTitleText != null)
            {
                quickGameTitleText.Text = service.CurrentGameName;
            }

            if (quickGameMetaText != null)
            {
                var platform = service.CurrentGamePlatformName;
                var source = service.CurrentGameSourceName;
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(platform) && platform != "-")
                {
                    parts.Add(platform);
                }

                if (!string.IsNullOrWhiteSpace(source) && source != "-")
                {
                    parts.Add(source);
                }

                quickGameMetaText.Text = parts.Count > 0 ? string.Join("  •  ", parts) : string.Empty;
            }

            if (quickGamePlaytimeText != null)
            {
                quickGamePlaytimeText.Text = service.CurrentGamePlaytimeValue;
            }

            if (quickGameSessionText != null)
            {
                quickGameSessionText.Text = service.CurrentGameSessionTimeValue;
            }

            if (quickGameAchievementRow != null && quickGameAchievementLabelText != null && quickGameAchievementValueText != null)
            {
                var unlocked = service.CurrentGameAchievementsUnlockedValue;
                var progress = service.CurrentGameAchievementsProgressValue;

                // The compact running-game card always shows global achievement progress.
                // Latest-achievement details remain available in the dedicated Achievements section.
                if (!string.IsNullOrWhiteSpace(unlocked) && unlocked != "-")
                {
                    quickGameAchievementLabelText.Text = Loc("LOCInGameOverlayAchievements", "Achievements");
                    quickGameAchievementValueText.Text = !string.IsNullOrWhiteSpace(progress) && progress != "-"
                        ? unlocked + "  •  " + progress
                        : unlocked;
                    quickGameAchievementRow.Visibility = Visibility.Visible;
                }
                else
                {
                    quickGameAchievementValueText.Text = string.Empty;
                    quickGameAchievementRow.Visibility = Visibility.Collapsed;
                }
            }

            if (quickGameCoverImage != null)
            {
                try
                {
                    var coverPath = service.CurrentGameCoverPath;
                    quickGameCoverImage.Source = !string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath)
                        ? ImageMemoryCache.GetOrLoad(coverPath, 320)
                        : null;
                }
                catch
                {
                    quickGameCoverImage.Source = null;
                }
            }
        }

        private StackPanel CreateControllerHint(string key, string label)
        {
            var hint = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(22, 0, 0, 0)
            };

            var keyBorder = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(230, 245, 241, 234)),
                Margin = new Thickness(0, 0, 9, 0)
            };
            keyBorder.Child = new TextBlock
            {
                Text = key,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Black
            };
            hint.Children.Add(keyBorder);

            var text = new TextBlock
            {
                Text = label,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            hint.Children.Add(text);

            return hint;
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
                sectionContentPanel.Width = 900;
                sectionContentPanel.Height = 260;
                sectionContentPanel.Margin = new Thickness(458, 146, 0, 0);
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
                // The old always-visible game card duplicated the compact game card in the sidebar.
                // Keep the data model alive for the optional section panels, but do not render the persistent card.
                persistentGameInfoPanel.Visibility = Visibility.Collapsed;
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

        private Button CreateButton(string icon, string text, Action action, string iconFontResource = "FontIcons")
        {
            var button = new Button
            {
                Height = 50,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Focusable = true,
                IsTabStop = true,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 0, 14, 0),
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

            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            iconText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            iconText.SetResourceReference(TextBlock.FontFamilyProperty, iconFontResource);
            iconText.FontWeight = FontWeights.Normal;
            Grid.SetColumn(iconText, 0);
            contentGrid.Children.Add(iconText);

            if (action == service.ReturnToGame)
            {
                resumeButtonIconText = iconText;
            }
            else if (action == service.ReturnToPlaynite)
            {
                returnButtonIconText = iconText;
            }

            var labelText = new TextBlock
            {
                Text = text,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(labelText, 1);
            contentGrid.Children.Add(labelText);

            if (action == service.ReturnToGame)
            {
                resumeButtonLabelText = labelText;
            }
            else if (action == service.ReturnToPlaynite)
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
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
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
            UpdateButtonVisualState(resumeButton);
            UpdateButtonVisualState(returnButton);
            UpdateButtonVisualState(gameSectionButton);
            UpdateButtonVisualState(mediaSectionButton);
            UpdateButtonVisualState(audioSectionButton);
            UpdateButtonVisualState(friendsButton);
            UpdateButtonVisualState(musicButton);
            UpdateButtonVisualState(appsButton);
            UpdateButtonVisualState(gameLinksButton);
            UpdateButtonVisualState(keyboardButton);
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
                isGameLinksVisible = false;
                isAchievementsVisible = false;

                if (virtualKeyboardView != null)
                {
                    virtualKeyboardView.Visibility = Visibility.Collapsed;
                }

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

                if (gameLinksHost != null)
                {
                    gameLinksHost.Visibility = Visibility.Collapsed;
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
                isGameLinksVisible = false;
                isAchievementsVisible = false;

                if (virtualKeyboardView != null)
                {
                    virtualKeyboardView.Visibility = Visibility.Collapsed;
                }

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

                if (gameLinksHost != null)
                {
                    gameLinksHost.Visibility = Visibility.Collapsed;
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

                if (SuppressInitialActivation)
                {
                    PrepareControllerFocusWithoutActivation();
                }
                else
                {
                    FocusOverlayButton();
                }
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
                HideGameLinks(false);
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
                HideGameLinks(false);
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
                HideGameLinks(false);
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
                HideGameLinks(false);
                HideAchievements(false);

                service.ClearFriendProfileFromOverlay();
                service.CloseFriendActionsFromOverlay();
                HideFriendProfileLayer(false);
                HideFriendActionsLayer(false);

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
                // Keep the compact Friends view designed specifically for the in-game overlay.
                // Friend actions/profile are still hosted locally in the same overlay window.
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

            // Friends is intentionally read-only inside the in-game overlay.
            // Friend cards remain focusable for controller navigation/scrolling, but clicking them does nothing.

            Panel.SetZIndex(friendsHost, 200);
            rootGrid.Children.Add(friendsHost);
        }

        private void OnFriendsOverlayButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!isFriendsVisible || isFriendsActionVisible || isFriendsProfileVisible)
                {
                    return;
                }

                var button = e.OriginalSource as DependencyObject;
                while (button != null && !(button is ButtonBase))
                {
                    button = VisualTreeHelper.GetParent(button);
                }

                var buttonBase = button as ButtonBase;
                var steamId = buttonBase?.CommandParameter as string;
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    return;
                }

                if (ShowFriendActionsLayer(steamId))
                {
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }

        private void EnsureFriendActionsHost()
        {
            if (friendsActionHost != null || rootGrid == null)
            {
                return;
            }

            friendsActionHost = new ContentControl
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
                // Prefer the lightweight overlay-specific action menu.
                // It keeps the user inside the in-game overlay and only offers Steam chat + Back.
                var style = FindThemeResource("FriendsActionOverlayStyle") as Style
                    ?? FindThemeResource("FriendsActionStyle") as Style;
                if (style != null)
                {
                    friendsActionHost.Style = style;
                }
            }
            catch
            {
            }

            // Mouse/touch activation follows the same local overlay flow as controller A.
            friendsActionHost.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnFriendActionsOverlayButtonClick), true);

            Panel.SetZIndex(friendsActionHost, 260);
            rootGrid.Children.Add(friendsActionHost);
        }

        private void OnFriendActionsOverlayButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!isFriendsVisible || !isFriendsActionVisible)
                {
                    return;
                }

                var source = e.OriginalSource as DependencyObject;
                while (source != null && !(source is ButtonBase))
                {
                    source = VisualTreeHelper.GetParent(source);
                }

                var button = source as ButtonBase;
                if (button == null)
                {
                    return;
                }

                var actionTag = button.Tag?.ToString();
                if (string.Equals(actionTag, "ChatAction", StringComparison.OrdinalIgnoreCase))
                {
                    service.OpenSelectedFriendChatFromOverlay();
                    HideFriendActionsLayer(true);
                    e.Handled = true;
                    return;
                }

                if (string.Equals(actionTag, "CancelAction", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(actionTag, "B", StringComparison.OrdinalIgnoreCase))
                {
                    HideFriendActionsLayer(true);
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }

        private void EnsureFriendProfileHost()
        {
            if (friendsProfileHost != null || rootGrid == null)
            {
                return;
            }

            friendsProfileHost = new ContentControl
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
                var style = FindThemeResource("FriendsStyleProfil") as Style;
                if (style != null)
                {
                    friendsProfileHost.Style = style;
                }
            }
            catch
            {
            }

            Panel.SetZIndex(friendsProfileHost, 270);
            rootGrid.Children.Add(friendsProfileHost);
        }

        private bool ShowFriendActionsLayer(string steamId)
        {
            if (!service.OpenFriendActionsFromOverlay(steamId))
            {
                return false;
            }

            EnsureFriendActionsHost();
            if (friendsActionHost == null)
            {
                return false;
            }

            HideFriendProfileLayer(false);
            isFriendsActionVisible = true;
            controllerFocusedFriendsElement = null;
            friendsActionHost.Visibility = Visibility.Visible;
            friendsActionHost.Opacity = 1;

            Dispatcher.BeginInvoke(new Action(FocusFirstFriendsElement), DispatcherPriority.Loaded);
            return true;
        }

        private void HideFriendActionsLayer(bool restoreFriendsFocus = true)
        {
            isFriendsActionVisible = false;
            controllerFocusedFriendsElement = null;

            if (friendsActionHost != null)
            {
                friendsActionHost.Visibility = Visibility.Collapsed;
            }

            service.CloseFriendActionsFromOverlay();

            if (restoreFriendsFocus && isFriendsVisible)
            {
                Dispatcher.BeginInvoke(new Action(FocusFirstFriendsElement), DispatcherPriority.Loaded);
            }
        }

        private bool ShowFriendProfileLayer()
        {
            if (!service.OpenSelectedFriendProfileFromOverlay())
            {
                return false;
            }

            EnsureFriendProfileHost();
            if (friendsProfileHost == null)
            {
                return false;
            }

            isFriendsActionVisible = false;
            if (friendsActionHost != null)
            {
                friendsActionHost.Visibility = Visibility.Collapsed;
            }

            isFriendsProfileVisible = true;
            controllerFocusedFriendsElement = null;
            friendsProfileHost.Visibility = Visibility.Visible;
            friendsProfileHost.Opacity = 1;

            Dispatcher.BeginInvoke(new Action(FocusFirstFriendsElement), DispatcherPriority.Loaded);
            return true;
        }

        private void HideFriendProfileLayer(bool returnToActions = true)
        {
            isFriendsProfileVisible = false;
            controllerFocusedFriendsElement = null;

            if (friendsProfileHost != null)
            {
                friendsProfileHost.Visibility = Visibility.Collapsed;
            }

            service.ClearFriendProfileFromOverlay();

            if (returnToActions && isFriendsVisible)
            {
                EnsureFriendActionsHost();
                if (friendsActionHost != null)
                {
                    isFriendsActionVisible = true;
                    friendsActionHost.Visibility = Visibility.Visible;
                    friendsActionHost.Opacity = 1;
                    Dispatcher.BeginInvoke(new Action(FocusFirstFriendsElement), DispatcherPriority.Loaded);
                }
            }
        }

        private void HideFriends(bool restoreControlCenterChrome = true)
        {
            try
            {
                isFriendsVisible = false;
                controllerFocusedFriendsElement = null;

                HideFriendProfileLayer(false);
                HideFriendActionsLayer(false);

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
                HideGameLinks(false);
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

                capturePreviewItem = mediaItem;

                if (mediaItem.IsVideo)
                {
                    var videoPath = service.GetCapturePreviewVideoPath(mediaItem);
                    if (!string.IsNullOrWhiteSpace(videoPath) && ShowCaptureVideoPreview(mediaItem, videoPath))
                    {
                        return true;
                    }
                }

                var imagePath = service.GetCapturePreviewImagePath(mediaItem);
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    capturePreviewItem = null;
                    return false;
                }

                if (!ShowCapturePreviewImage(mediaItem, imagePath, mediaItem.IsVideo))
                {
                    capturePreviewItem = null;
                    return false;
                }

                return true;
            }
            catch
            {
                capturePreviewItem = null;
                return false;
            }
        }

        private bool ShowCapturePreviewImage(AnikiMediaItem mediaItem, string imagePath, bool isVideoFallback)
        {
            try
            {
                var bitmap = LoadCapturePreviewBitmap(imagePath);
                if (bitmap == null)
                {
                    return false;
                }

                CloseCaptureVideoPreviewWindow();
                EnsureCapturePreviewLayer();
                if (capturePreviewLayer == null || capturePreviewImage == null)
                {
                    return false;
                }

                capturePreviewItem = mediaItem;
                capturePreviewImage.Source = bitmap;
                capturePreviewImage.Visibility = Visibility.Visible;

                UpdateCapturePreviewHeader(
                    capturePreviewTitleText,
                    capturePreviewMetaText,
                    mediaItem,
                    isVideoFallback);

                if (capturePreviewFooterText != null)
                {
                    capturePreviewFooterText.Text = "← / →     B  " + Loc("LOCBackLabel", "Back");
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

        private bool ShowCaptureVideoPreview(AnikiMediaItem mediaItem, string videoPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
                {
                    return false;
                }

                EnsureCaptureVideoPreviewWindow();
                if (captureVideoPreviewWindow == null || capturePreviewVideo == null)
                {
                    return false;
                }

                if (capturePreviewLayer != null)
                {
                    capturePreviewLayer.Visibility = Visibility.Collapsed;
                }

                if (capturePreviewImage != null)
                {
                    capturePreviewImage.Source = null;
                }

                capturePreviewItem = mediaItem;
                capturePreviewVideoPaused = false;

                UpdateCapturePreviewHeader(
                    capturePreviewVideoTitleText,
                    capturePreviewVideoMetaText,
                    mediaItem,
                    false);
                UpdateCapturePreviewIndex();

                try
                {
                    capturePreviewVideo.Stop();
                }
                catch
                {
                }

                capturePreviewVideo.Volume = service.GetCapturePreviewVideoVolume();
                capturePreviewVideo.Source = new Uri(Path.GetFullPath(videoPath), UriKind.Absolute);

                isCapturePreviewVisible = true;

                if (lastCapturesHost != null)
                {
                    lastCapturesHost.IsHitTestVisible = false;
                }

                if (!captureVideoPreviewWindow.IsVisible)
                {
                    captureVideoPreviewWindow.Show();
                }

                captureVideoPreviewWindow.Activate();
                captureVideoPreviewWindow.Focus();
                capturePreviewVideo.Play();

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

            var topBar = CreateCapturePreviewTopBar(
                out capturePreviewTitleText,
                out capturePreviewMetaText,
                out capturePreviewIndexText);
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

            capturePreviewFooterText = new TextBlock
            {
                Text = "← / →     B  " + Loc("LOCBackLabel", "Back"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };

            footerBorder.Child = capturePreviewFooterText;
            previewRoot.Children.Add(footerBorder);

            previewRoot.MouseLeftButtonUp += (sender, args) =>
            {
                args.Handled = true;
                HideCapturePreview();
            };

            Panel.SetZIndex(previewRoot, 1000);
            rootGrid.Children.Add(previewRoot);
        }

        private void EnsureCaptureVideoPreviewWindow()
        {
            if (captureVideoPreviewWindow != null)
            {
                return;
            }

            var videoWindow = new Window
            {
                Owner = this,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowState = WindowState.Maximized,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.Black,
                AllowsTransparency = false,
                Topmost = true,
                ShowActivated = true,
                ShowInTaskbar = false
            };

            var previewRoot = new Grid
            {
                Background = Brushes.Black,
                Focusable = true
            };

            var videoFrame = new Border
            {
                Margin = new Thickness(58, 80, 58, 112),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(16),
                Background = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            capturePreviewVideo = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ScrubbingEnabled = true,
                Volume = service.GetCapturePreviewVideoVolume()
            };

            capturePreviewVideo.MediaOpened += CapturePreviewVideo_MediaOpened;
            capturePreviewVideo.MediaEnded += CapturePreviewVideo_MediaEnded;
            capturePreviewVideo.MediaFailed += CapturePreviewVideo_MediaFailed;

            videoFrame.Child = capturePreviewVideo;
            previewRoot.Children.Add(videoFrame);

            var topBar = CreateCapturePreviewTopBar(
                out capturePreviewVideoTitleText,
                out capturePreviewVideoMetaText,
                out capturePreviewVideoIndexText);
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
                Text = "← / →     A  " + Loc("VideoPlayer_PlayPause", "Play / Pause") +
                       "     B  " + Loc("LOCBackLabel", "Back"),
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

            videoWindow.PreviewKeyDown += (sender, args) =>
            {
                if (HandleCapturePreviewKeyDown(args))
                {
                    args.Handled = true;
                }
            };

            videoWindow.Closed += CaptureVideoPreviewWindow_Closed;
            videoWindow.Content = previewRoot;
            captureVideoPreviewWindow = videoWindow;
        }

        private Grid CreateCapturePreviewTopBar(
            out TextBlock titleText,
            out TextBlock metaText,
            out TextBlock indexText)
        {
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

            titleText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 25,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 1420
            };

            metaText = new TextBlock
            {
                Margin = new Thickness(0, 3, 0, 0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 1420
            };

            titleStack.Children.Add(titleText);
            titleStack.Children.Add(metaText);
            Grid.SetColumn(titleStack, 0);
            topBar.Children.Add(titleStack);

            indexText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(indexText, 1);
            topBar.Children.Add(indexText);
            return topBar;
        }

        private void UpdateCapturePreviewHeader(
            TextBlock titleText,
            TextBlock metaText,
            AnikiMediaItem mediaItem,
            bool isVideoFallback)
        {
            if (mediaItem == null)
            {
                return;
            }

            if (titleText != null)
            {
                var title = !string.IsNullOrWhiteSpace(mediaItem.GameName)
                    ? mediaItem.GameName
                    : mediaItem.FileName;

                titleText.Text = string.IsNullOrWhiteSpace(title)
                    ? Loc("LOCImage", "Screenshot")
                    : title;
            }

            if (metaText != null)
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
                    metaParts.Add(isVideoFallback
                        ? Loc("Video", "Video thumbnail")
                        : Loc("Video", "Video"));
                }

                metaText.Text = string.Join("  •  ", metaParts);
            }
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
            var items = service.GetOverlayLastCapturePreviewItems();
            var indexText = string.Empty;

            if (items != null && items.Count > 0 && capturePreviewItem != null)
            {
                var index = items.FindIndex(item => IsSameCapturePreviewItem(item, capturePreviewItem));
                if (index >= 0)
                {
                    indexText = (index + 1) + " / " + items.Count;
                }
            }

            if (capturePreviewIndexText != null)
            {
                capturePreviewIndexText.Text = indexText;
            }

            if (capturePreviewVideoIndexText != null)
            {
                capturePreviewVideoIndexText.Text = indexText;
            }
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

        private void CapturePreviewVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!isCapturePreviewVisible || capturePreviewItem?.IsVideo != true || capturePreviewVideo == null)
                {
                    return;
                }

                capturePreviewVideo.Volume = service.GetCapturePreviewVideoVolume();
                if (!capturePreviewVideoPaused)
                {
                    capturePreviewVideo.Play();
                }
            }
            catch
            {
            }
        }

        private void CapturePreviewVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!isCapturePreviewVisible || capturePreviewVideo == null || capturePreviewVideoPaused)
                {
                    return;
                }

                capturePreviewVideo.Position = TimeSpan.Zero;
                capturePreviewVideo.Play();
            }
            catch
            {
            }
        }

        private void CapturePreviewVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            try
            {
                var item = capturePreviewItem;
                CloseCaptureVideoPreviewWindow();

                if (!isCapturePreviewVisible || item == null)
                {
                    return;
                }

                var thumbnailPath = service.GetCapturePreviewImagePath(item);
                if (!string.IsNullOrWhiteSpace(thumbnailPath) &&
                    ShowCapturePreviewImage(item, thumbnailPath, true))
                {
                    return;
                }

                HideCapturePreview();
            }
            catch
            {
                HideCapturePreview();
            }
        }

        private void ToggleCapturePreviewVideoPlayback()
        {
            try
            {
                if (capturePreviewItem?.IsVideo != true ||
                    capturePreviewVideo == null ||
                    captureVideoPreviewWindow?.IsVisible != true)
                {
                    return;
                }

                if (capturePreviewVideoPaused)
                {
                    capturePreviewVideoPaused = false;
                    capturePreviewVideo.Play();
                }
                else
                {
                    capturePreviewVideoPaused = true;
                    capturePreviewVideo.Pause();
                }
            }
            catch
            {
            }
        }

        private void CloseCaptureVideoPreviewWindow()
        {
            var window = captureVideoPreviewWindow;
            var video = capturePreviewVideo;

            if (video != null)
            {
                try { video.Stop(); } catch { }
                try { video.Source = null; } catch { }
            }

            if (window != null)
            {
                try
                {
                    captureVideoWindowClosingInternally = true;
                    window.Close();
                }
                catch
                {
                }
                finally
                {
                    captureVideoWindowClosingInternally = false;
                }
            }

            captureVideoPreviewWindow = null;
            capturePreviewVideo = null;
            capturePreviewVideoTitleText = null;
            capturePreviewVideoMetaText = null;
            capturePreviewVideoIndexText = null;
            capturePreviewVideoPaused = false;
        }

        private void CaptureVideoPreviewWindow_Closed(object sender, EventArgs e)
        {
            if (captureVideoWindowClosingInternally)
            {
                return;
            }

            captureVideoPreviewWindow = null;
            capturePreviewVideo = null;
            capturePreviewVideoTitleText = null;
            capturePreviewVideoMetaText = null;
            capturePreviewVideoIndexText = null;
            capturePreviewVideoPaused = false;

            if (isCapturePreviewVisible)
            {
                HideCapturePreview();
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

                CloseCaptureVideoPreviewWindow();

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
                    if (capturePreviewItem?.IsVideo == true)
                    {
                        ToggleCapturePreviewVideoPlayback();
                    }
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
                if (capturePreviewItem?.IsVideo == true)
                {
                    ToggleCapturePreviewVideoPlayback();
                }
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

                // GameAchievementsWindow normally inherits Playnite's main-view context
                // and binds its header/background/logo through SelectedGame. Because the
                // same theme style is hosted inside our overlay, provide only that missing
                // root context here. PluginSettings bindings remain independent.
                var runningGame = service.CurrentGameForThemeBindings;
                achievementsHost.DataContext = new AchievementsThemeContext
                {
                    SelectedGame = runningGame == null
                        ? null
                        : new AchievementsSelectedGameContext
                        {
                            Id = runningGame.Id,
                            DisplayName = service.CurrentGameName,
                            BackgroundImage = runningGame.BackgroundImage
                        }
                };

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideApps(false);
                HideGameLinks(false);

                HideAchievementCaptureLayer(false);
                HideAchievementActionsLayer(false);
                HideAchievementOptionsLayer(false);

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
                }), DispatcherPriority.ContextIdle);
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
                // Reuse the real game-achievements page from GameDetails.xaml. This keeps
                // its layout, filters, pin visuals and future theme changes in one place.
                var style = FindThemeResource("GameAchievementsWindow") as Style;
                if (style != null)
                {
                    achievementsHost.Style = style;
                }
                else
                {
                    achievementsHost.Content = new TextBlock
                    {
                        Text = "GameAchievementsWindow not found",
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

            achievementsHost.IsKeyboardFocusWithinChanged += (s, e) =>
            {
                if (!achievementsHost.IsKeyboardFocusWithin)
                {
                    ScheduleAchievementFocusRestore();
                }
            };

            Panel.SetZIndex(achievementsHost, 200);
            rootGrid.Children.Add(achievementsHost);
        }

        private void EnsureAchievementOptionsHost()
        {
            if (achievementOptionsHost != null || rootGrid == null)
            {
                return;
            }

            achievementOptionsHost = new ContentControl
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
                var style = FindThemeResource("AchievementsDetailsOptionsWindow") as Style;
                if (style != null)
                {
                    achievementOptionsHost.Style = style;
                }
            }
            catch
            {
            }

            Panel.SetZIndex(achievementOptionsHost, 250);
            rootGrid.Children.Add(achievementOptionsHost);
        }

        private bool ShowAchievementOptionsLayer()
        {
            EnsureAchievementOptionsHost();
            if (achievementOptionsHost == null)
            {
                return false;
            }

            HideAchievementCaptureLayer(false);
            HideAchievementActionsLayer(false);

            isAchievementOptionsVisible = true;
            controllerFocusedAchievementsElement = null;
            achievementOptionsHost.Visibility = Visibility.Visible;
            achievementOptionsHost.Opacity = 1;

            Dispatcher.BeginInvoke(new Action(FocusFirstAchievementOptionsElement), DispatcherPriority.ContextIdle);
            return true;
        }

        private void HideAchievementOptionsLayer(bool restoreSettingsFocus = true)
        {
            isAchievementOptionsVisible = false;
            controllerFocusedAchievementsElement = null;

            if (achievementOptionsHost != null)
            {
                achievementOptionsHost.Visibility = Visibility.Collapsed;
            }

            if (restoreSettingsFocus && isAchievementsVisible)
            {
                Dispatcher.BeginInvoke(new Action(FocusAchievementSettingsElement), DispatcherPriority.Loaded);
            }
        }

        private void EnsureAchievementActionsHost()
        {
            if (achievementActionsHost != null || rootGrid == null)
            {
                return;
            }

            achievementActionsHost = new ContentControl
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
                var style = FindThemeResource("AchievementActionsWindowStyle") as Style;
                if (style != null)
                {
                    achievementActionsHost.Style = style;
                }
            }
            catch
            {
            }

            Panel.SetZIndex(achievementActionsHost, 260);
            rootGrid.Children.Add(achievementActionsHost);
        }

        private void EnsureAchievementCaptureHost()
        {
            if (achievementCaptureHost != null || rootGrid == null)
            {
                return;
            }

            achievementCaptureHost = new ContentControl
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
                var style = FindThemeResource("AchievementCaptureViewerWindowStyle") as Style;
                if (style != null)
                {
                    achievementCaptureHost.Style = style;
                }
            }
            catch
            {
            }

            Panel.SetZIndex(achievementCaptureHost, 280);
            rootGrid.Children.Add(achievementCaptureHost);
        }

        private bool ShowAchievementActionsLayer(object achievement)
        {
            if (achievement == null || !service.PrepareAchievementActionsForOverlay(achievement))
            {
                return false;
            }

            achievementReturnApiName = GetObjectStringProperty(achievement, "ApiName");
            achievementReturnName = GetObjectStringProperty(achievement, "Name");

            EnsureAchievementActionsHost();
            if (achievementActionsHost == null)
            {
                return false;
            }

            HideAchievementCaptureLayer(false);
            isAchievementActionsVisible = true;
            controllerFocusedAchievementsElement = null;
            achievementActionsHost.Visibility = Visibility.Visible;
            achievementActionsHost.Opacity = 1;

            Dispatcher.BeginInvoke(new Action(FocusFirstAchievementsElement), DispatcherPriority.Loaded);
            return true;
        }

        private void HideAchievementActionsLayer(bool restoreAchievementFocus = true)
        {
            isAchievementActionsVisible = false;
            controllerFocusedAchievementsElement = null;

            if (achievementActionsHost != null)
            {
                achievementActionsHost.Visibility = Visibility.Collapsed;
            }

            if (restoreAchievementFocus && isAchievementsVisible)
            {
                Dispatcher.BeginInvoke(new Action(RestoreAchievementReturnFocus), DispatcherPriority.Loaded);
            }
        }

        private bool ShowAchievementCaptureLayer(object captureKind)
        {
            if (!service.PrepareAchievementCaptureForOverlay(captureKind))
            {
                return false;
            }

            EnsureAchievementCaptureHost();
            if (achievementCaptureHost == null)
            {
                return false;
            }

            isAchievementCaptureVisible = true;
            controllerFocusedAchievementsElement = null;
            achievementCaptureHost.Visibility = Visibility.Visible;
            achievementCaptureHost.Opacity = 1;
            return true;
        }

        private void HideAchievementCaptureLayer(bool restoreActionFocus = true)
        {
            isAchievementCaptureVisible = false;
            controllerFocusedAchievementsElement = null;

            if (achievementCaptureHost != null)
            {
                achievementCaptureHost.Visibility = Visibility.Collapsed;
            }

            if (restoreActionFocus && isAchievementActionsVisible)
            {
                Dispatcher.BeginInvoke(new Action(FocusFirstAchievementsElement), DispatcherPriority.Loaded);
            }
        }

        private void RestoreAchievementReturnFocus()
        {
            var elements = GetAchievementsControllerElements();
            if (elements.Length == 0)
            {
                return;
            }

            foreach (var element in elements)
            {
                var data = element?.DataContext;
                if (data == null)
                {
                    continue;
                }

                var apiName = GetObjectStringProperty(data, "ApiName");
                var name = GetObjectStringProperty(data, "Name");
                if ((!string.IsNullOrWhiteSpace(achievementReturnApiName) &&
                     string.Equals(apiName, achievementReturnApiName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(achievementReturnName) &&
                     string.Equals(name, achievementReturnName, StringComparison.OrdinalIgnoreCase)))
                {
                    FocusAchievementsElement(element);
                    return;
                }
            }

            var cards = GetAchievementCardElements();
            if (cards.Length > 0)
            {
                FocusAchievementsElement(cards[0]);
            }
            else
            {
                FocusAchievementSettingsElement();
            }
        }

        private void HideAchievements(bool restoreControlCenterChrome = true)
        {
            try
            {
                isAchievementsVisible = false;
                controllerFocusedAchievementsElement = null;

                HideAchievementCaptureLayer(false);
                HideAchievementActionsLayer(false);
                HideAchievementOptionsLayer(false);

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
                HideGameLinks(false);
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

        public void ShowGameLinks()
        {
            try
            {
                EnsureGameLinksHost();

                if (gameLinksHost == null)
                {
                    return;
                }

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideApps(false);
                HideAchievements(false);

                isGameLinksVisible = true;
                SetControlCenterChromeVisible(false);

                gameLinksHost.Visibility = Visibility.Visible;
                gameLinksHost.Opacity = 1;

                ConfigureGameLinksCloseButton();

                Activate();
                Focus();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        ConfigureGameLinksCloseButton();
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

        private void EnsureGameLinksHost()
        {
            if (gameLinksHost != null || rootGrid == null)
            {
                return;
            }

            gameLinksHost = new ContentControl
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
                var style = FindThemeResource("GameLinksWindowStyle") as Style;
                if (style != null)
                {
                    gameLinksHost.Style = style;
                }
                else
                {
                    gameLinksHost.Content = new TextBlock
                    {
                        Text = "GameLinksWindowStyle not found",
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

            Panel.SetZIndex(gameLinksHost, 200);
            rootGrid.Children.Add(gameLinksHost);
        }

        private void ConfigureGameLinksCloseButton()
        {
            try
            {
                if (gameLinksHost == null)
                {
                    return;
                }

                gameLinksHost.ApplyTemplate();
                gameLinksHost.UpdateLayout();

                var elements = new List<FrameworkElement>();
                CollectVisualChildren(gameLinksHost, elements);

                ButtonBase closeButton = null;
                foreach (var element in elements)
                {
                    var buttonBase = element as ButtonBase;
                    if (buttonBase == null)
                    {
                        continue;
                    }

                    if (string.Equals(buttonBase.Tag?.ToString(), "CancelAction", StringComparison.OrdinalIgnoreCase))
                    {
                        closeButton = buttonBase;
                        break;
                    }
                }

                if (closeButton == null)
                {
                    return;
                }

                if (!ReferenceEquals(gameLinksCloseButton, closeButton))
                {
                    if (gameLinksCloseButton != null)
                    {
                        gameLinksCloseButton.Click -= GameLinksCloseButton_Click;
                    }

                    gameLinksCloseButton = closeButton;
                    gameLinksCloseButton.Command = null;
                    gameLinksCloseButton.Click += GameLinksCloseButton_Click;
                }
            }
            catch
            {
            }
        }

        private void GameLinksCloseButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            HideGameLinks();
        }

        private void HideGameLinks(bool restoreControlCenterChrome = true)
        {
            try
            {
                isGameLinksVisible = false;
                controllerFocusedAppsElement = null;

                if (gameLinksHost != null)
                {
                    gameLinksHost.Visibility = Visibility.Collapsed;
                }

                if (restoreControlCenterChrome)
                {
                    SetControlCenterChromeVisible(true);

                    controllerFocusedButton = gameLinksButton ?? firstButton;
                    useControllerFocusVisual = true;
                    FocusSelectedButtonWithoutTraversal();
                    UpdateAllButtonVisualStates();
                }
            }
            catch
            {
            }
        }

        private void ShowVirtualKeyboard()
        {
            if (service.IsWindowsVirtualKeyboardSelected)
            {
                service.OpenWindowsVirtualKeyboardFromOverlay();
                return;
            }

            ShowVirtualKeyboardCore(openedDirectly: false);
        }

        public void ShowVirtualKeyboardDirect(string initialText = null)
        {
            ShowVirtualKeyboardCore(openedDirectly: true, initialText: initialText);
        }

        private void ShowVirtualKeyboardCore(bool openedDirectly, string initialText = null)
        {
            try
            {
                if (virtualKeyboardView == null)
                {
                    return;
                }

                virtualKeyboardOpenedDirectly = openedDirectly;

                HideMusicPlayer(false);
                HideAudioSwitcher(false);
                HideUniPlaySong(false);
                HideFriends(false);
                HideLastCaptures(false);
                HideApps(false);
                HideGameLinks(false);
                HideAchievements(false);

                SetControlCenterChromeVisible(false);
                virtualKeyboardView.Open(initialText);

                // Direct opening can happen while the WPF window is still hidden and
                // its constructor opacity is 0. Make only the keyboard visible before Show().
                BeginAnimation(Window.OpacityProperty, null);
                Opacity = 1;

                Activate();
                Focus();
            }
            catch
            {
                virtualKeyboardOpenedDirectly = false;
            }
        }

        private void HideVirtualKeyboard(bool restoreControlCenterChrome = true)
        {
            try
            {
                if (virtualKeyboardView == null || !virtualKeyboardView.IsOpen)
                {
                    return;
                }

                if (!restoreControlCenterChrome)
                {
                    virtualKeyboardView.Visibility = Visibility.Collapsed;
                    return;
                }

                virtualKeyboardView.Close();
            }
            catch
            {
            }
        }

        private void OnVirtualKeyboardClosed()
        {
            try
            {
                if (virtualKeyboardOpenedDirectly)
                {
                    virtualKeyboardOpenedDirectly = false;
                    service.HandleDirectVirtualKeyboardClosed();
                    return;
                }

                SetControlCenterChromeVisible(true);
                controllerFocusedButton = keyboardButton ?? firstButton;
                useControllerFocusVisual = true;
                FocusSelectedButtonWithoutTraversal();
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private void OnVirtualKeyboardSubmit(string text, bool pressEnter)
        {
            try
            {
                virtualKeyboardOpenedDirectly = false;
                service.HandleDirectVirtualKeyboardSubmit(text ?? string.Empty, pressEnter);
            }
            catch
            {
                virtualKeyboardOpenedDirectly = false;
                SetControlCenterChromeVisible(true);
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
                    if (CanProcessControllerAction())
                    {
                        ActivateMusicPlayerElement();
                    }
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

        private bool ActivateAudioSwitcherElement()
        {
            var current = GetCurrentAudioSwitcherElement();
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

                // A slider is already controlled by Left / Right in the overlay.
                if (current is RangeBase)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
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
                    if (CanProcessControllerAction())
                    {
                        ActivateAudioSwitcherElement();
                    }
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
                    if (CanProcessControllerAction())
                    {
                        ActivateUniPlaySongElement();
                    }
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
            if (!isFriendsVisible)
            {
                return new FrameworkElement[0];
            }

            var activeHost = isFriendsProfileVisible
                ? friendsProfileHost
                : (isFriendsActionVisible ? friendsActionHost : friendsHost);

            if (activeHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                activeHost.ApplyTemplate();
                activeHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(activeHost, allElements);

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
                var buttonBase = current as ButtonBase;
                if (buttonBase == null)
                {
                    return false;
                }

                if (isFriendsActionVisible)
                {
                    var actionTag = buttonBase.Tag?.ToString();
                    if (string.Equals(actionTag, "ChatAction", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(actionTag, "X", StringComparison.OrdinalIgnoreCase))
                    {
                        service.OpenSelectedFriendChatFromOverlay();
                        HideFriendActionsLayer(true);
                        return true;
                    }

                    if (string.Equals(actionTag, "B", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(actionTag, "CancelAction", StringComparison.OrdinalIgnoreCase))
                    {
                        HideFriendActionsLayer(true);
                        return true;
                    }
                }
                else if (!isFriendsProfileVisible)
                {
                    // Reuse the exact same routed Click path as mouse/touch. This avoids having
                    // separate activation logic for controller A and keeps both inputs in sync.
                    buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    return true;
                }

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
            catch
            {
                return true;
            }
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
                    // Friends is a read-only status view in the in-game overlay.
                    // Consume A so Playnite cannot activate anything behind the overlay.
                    return true;

                case ControllerInput.B:
                case ControllerInput.Back:
                    if (isFriendsProfileVisible)
                    {
                        HideFriendProfileLayer(true);
                    }
                    else if (isFriendsActionVisible)
                    {
                        HideFriendActionsLayer(true);
                    }
                    else
                    {
                        HideFriends();
                    }
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
                // Keep the Friends overlay read-only for keyboard/controller mirror input too.
                e.Handled = true;
                return true;
            }

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                e.Handled = true;
                if (isFriendsProfileVisible)
                {
                    HideFriendProfileLayer(true);
                }
                else if (isFriendsActionVisible)
                {
                    HideFriendActionsLayer(true);
                }
                else
                {
                    HideFriends();
                }
                return true;
            }

            return false;
        }

        private void FocusFirstAchievementsElement()
        {
            if (isAchievementOptionsVisible)
            {
                FocusFirstAchievementOptionsElement();
                return;
            }

            if (isAchievementActionsVisible)
            {
                var actionElements = GetAchievementsControllerElements();
                if (actionElements.Length > 0)
                {
                    FocusAchievementsElement(actionElements[0]);
                }
                return;
            }

            if (isAchievementCaptureVisible)
            {
                return;
            }

            var cards = GetAchievementCardElements();
            if (cards.Length > 0)
            {
                FocusAchievementsElement(cards[0]);
                return;
            }

            FocusAchievementSettingsElement();
        }

        private FrameworkElement GetAchievementSettingsElement()
        {
            if (achievementsHost == null)
            {
                return null;
            }

            try
            {
                achievementsHost.ApplyTemplate();
                achievementsHost.UpdateLayout();
                return achievementsHost.Template?.FindName("TrophiesMenuToggle", achievementsHost) as FrameworkElement;
            }
            catch
            {
                return null;
            }
        }

        private FrameworkElement GetAchievementListElement()
        {
            if (achievementsHost == null)
            {
                return null;
            }

            try
            {
                achievementsHost.ApplyTemplate();
                achievementsHost.UpdateLayout();
                return achievementsHost.Template?.FindName("DynamicAchievementList", achievementsHost) as FrameworkElement;
            }
            catch
            {
                return null;
            }
        }

        private FrameworkElement[] GetAchievementCardElements()
        {
            var listElement = GetAchievementListElement();
            if (listElement == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(listElement, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element) || element is RepeatButton)
                    {
                        continue;
                    }

                    var buttonBase = element as ButtonBase;
                    if (buttonBase == null)
                    {
                        continue;
                    }

                    var achievement = buttonBase.CommandParameter ?? buttonBase.DataContext;
                    if (achievement != null && HasObjectProperty(achievement, "ToggleAchievementGoalCommand"))
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

        private FrameworkElement[] GetAchievementOptionsElements()
        {
            if (!isAchievementOptionsVisible || achievementOptionsHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                achievementOptionsHost.ApplyTemplate();
                achievementOptionsHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(achievementOptionsHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element) || element is RepeatButton)
                    {
                        continue;
                    }

                    if (element is ComboBox || element is ToggleButton)
                    {
                        result.Add(element);
                        continue;
                    }

                    var buttonBase = element as ButtonBase;
                    if (buttonBase != null && buttonBase.Command != null)
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

        private FrameworkElement[] GetAchievementsControllerElements()
        {
            if (!isAchievementsVisible)
            {
                return new FrameworkElement[0];
            }

            if (isAchievementOptionsVisible)
            {
                return GetAchievementOptionsElements();
            }

            if (!isAchievementActionsVisible && !isAchievementCaptureVisible)
            {
                var result = new List<FrameworkElement>();
                var settingsElement = GetAchievementSettingsElement();
                if (CanElementReceiveControllerFocus(settingsElement))
                {
                    result.Add(settingsElement);
                }

                result.AddRange(GetAchievementCardElements());
                return result.ToArray();
            }

            var activeHost = isAchievementCaptureVisible ? achievementCaptureHost : achievementActionsHost;
            if (activeHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                activeHost.ApplyTemplate();
                activeHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(activeHost, allElements);

                var result = new List<FrameworkElement>();
                foreach (var element in allElements)
                {
                    if (!CanElementReceiveControllerFocus(element) || element is RepeatButton)
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

        private bool IsAchievementSettingsElement(FrameworkElement element)
        {
            var settingsElement = GetAchievementSettingsElement();
            return element != null && settingsElement != null && ReferenceEquals(element, settingsElement);
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

                if (!isAchievementOptionsVisible && !isAchievementActionsVisible && !isAchievementCaptureVisible &&
                    !IsAchievementSettingsElement(element))
                {
                    var data = (element as ButtonBase)?.CommandParameter ?? element.DataContext;
                    var apiName = GetObjectStringProperty(data, "ApiName");
                    var name = GetObjectStringProperty(data, "Name");
                    if (!string.IsNullOrWhiteSpace(apiName))
                    {
                        achievementReturnApiName = apiName;
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        achievementReturnName = name;
                    }
                }

                element.Focus();
                Keyboard.Focus(element);
                element.BringIntoView();
                UpdateAllButtonVisualStates();
            }
            catch
            {
            }
        }

        private void FocusAchievementSettingsElement()
        {
            var settingsElement = GetAchievementSettingsElement();
            if (CanElementReceiveControllerFocus(settingsElement))
            {
                FocusAchievementsElement(settingsElement);
            }
        }

        private void FocusFirstAchievementOptionsElement()
        {
            var elements = GetAchievementOptionsElements();
            if (elements.Length > 0)
            {
                FocusAchievementsElement(elements[0]);
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

            if (!isAchievementOptionsVisible && !isAchievementActionsVisible && !isAchievementCaptureVisible)
            {
                var cards = GetAchievementCardElements();
                if (cards.Length > 0)
                {
                    return cards[0];
                }
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

        private void MoveAchievementCardFocus(int direction)
        {
            var cards = GetAchievementCardElements();
            if (cards.Length == 0)
            {
                FocusAchievementSettingsElement();
                return;
            }

            var current = GetCurrentAchievementsElement();
            var index = Array.IndexOf(cards, current);
            if (index < 0)
            {
                index = direction < 0 ? cards.Length - 1 : 0;
            }
            else
            {
                index = (index + direction + cards.Length) % cards.Length;
            }

            FocusAchievementsElement(cards[index]);
        }

        private void FocusAchievementCardFromSettings()
        {
            var cards = GetAchievementCardElements();
            if (cards.Length == 0)
            {
                return;
            }

            if (controllerFocusedAchievementsElement != null &&
                Array.IndexOf(cards, controllerFocusedAchievementsElement) >= 0 &&
                CanElementReceiveControllerFocus(controllerFocusedAchievementsElement))
            {
                FocusAchievementsElement(controllerFocusedAchievementsElement);
                return;
            }

            foreach (var card in cards)
            {
                var data = (card as ButtonBase)?.CommandParameter ?? card.DataContext;
                var apiName = GetObjectStringProperty(data, "ApiName");
                var name = GetObjectStringProperty(data, "Name");
                if ((!string.IsNullOrWhiteSpace(achievementReturnApiName) &&
                     string.Equals(apiName, achievementReturnApiName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(achievementReturnName) &&
                     string.Equals(name, achievementReturnName, StringComparison.OrdinalIgnoreCase)))
                {
                    FocusAchievementsElement(card);
                    return;
                }
            }

            FocusAchievementsElement(cards[0]);
        }

        private void MoveAchievementOptionsFocus(int direction)
        {
            var current = GetCurrentAchievementsElement();
            var openCombo = current as ComboBox;
            if (openCombo != null && openCombo.IsDropDownOpen)
            {
                AdjustAchievementComboSelection(openCombo, direction);
                return;
            }

            MoveAchievementsFocus(direction);
        }

        private bool AdjustAchievementOptionsValue(int direction)
        {
            var combo = GetCurrentAchievementsElement() as ComboBox;
            if (combo == null)
            {
                return false;
            }

            AdjustAchievementComboSelection(combo, direction);
            return true;
        }

        private static void AdjustAchievementComboSelection(ComboBox combo, int direction)
        {
            if (combo == null || combo.Items.Count <= 0 || direction == 0)
            {
                return;
            }

            var index = combo.SelectedIndex;
            if (index < 0)
            {
                index = 0;
            }
            else
            {
                index = Math.Max(0, Math.Min(combo.Items.Count - 1, index + direction));
            }

            combo.SelectedIndex = index;
        }

        private bool ActivateAchievementOptionsElement()
        {
            var current = GetCurrentAchievementsElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                var combo = current as ComboBox;
                if (combo != null)
                {
                    combo.IsDropDownOpen = !combo.IsDropDownOpen;
                    return true;
                }

                var toggle = current as ToggleButton;
                if (toggle != null)
                {
                    toggle.IsChecked = toggle.IsChecked != true;
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase == null)
                {
                    return false;
                }

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
            catch
            {
                return true;
            }
        }

        private bool ActivateAchievementsElement()
        {
            if (isAchievementOptionsVisible)
            {
                return ActivateAchievementOptionsElement();
            }

            var current = GetCurrentAchievementsElement();
            if (current == null)
            {
                return false;
            }

            try
            {
                if (isAchievementCaptureVisible)
                {
                    return true;
                }

                var buttonBase = current as ButtonBase;
                if (buttonBase == null)
                {
                    return false;
                }

                if (isAchievementActionsVisible)
                {
                    var tag = buttonBase.Tag?.ToString();
                    if (string.Equals(tag, "CancelAction", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tag, "B", StringComparison.OrdinalIgnoreCase))
                    {
                        HideAchievementActionsLayer(true);
                        return true;
                    }

                    var captureKind = buttonBase.CommandParameter?.ToString();
                    if (string.Equals(captureKind, "Clean", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(captureKind, "Notification", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(captureKind, "Framed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(captureKind, "Video", StringComparison.OrdinalIgnoreCase))
                    {
                        return ShowAchievementCaptureLayer(captureKind);
                    }
                }
                else
                {
                    if (IsAchievementSettingsElement(current))
                    {
                        return ShowAchievementOptionsLayer();
                    }

                    var achievement = buttonBase.CommandParameter ?? buttonBase.DataContext;
                    if (HasObjectProperty(achievement, "ToggleAchievementGoalCommand") &&
                        ShowAchievementActionsLayer(achievement))
                    {
                        return true;
                    }
                }

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
            catch
            {
                return true;
            }
        }

        private void EnsureAchievementsMainFocus()
        {
            if (!isAchievementsVisible || isAchievementOptionsVisible || isAchievementActionsVisible ||
                isAchievementCaptureVisible)
            {
                return;
            }

            var settingsElement = GetAchievementSettingsElement();
            var cards = GetAchievementCardElements();

            if (controllerFocusedAchievementsElement != null &&
                CanElementReceiveControllerFocus(controllerFocusedAchievementsElement) &&
                (ReferenceEquals(controllerFocusedAchievementsElement, settingsElement) ||
                 Array.IndexOf(cards, controllerFocusedAchievementsElement) >= 0))
            {
                FocusAchievementsElement(controllerFocusedAchievementsElement);
                return;
            }

            FocusAchievementCardFromSettings();
        }

        private void ScheduleAchievementFocusRestore()
        {
            if (!isAchievementsVisible || isAchievementOptionsVisible || isAchievementActionsVisible ||
                isAchievementCaptureVisible || !IsActive)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!isAchievementsVisible || isAchievementOptionsVisible || isAchievementActionsVisible ||
                        isAchievementCaptureVisible || !IsActive || achievementsHost == null ||
                        achievementsHost.IsKeyboardFocusWithin)
                    {
                        return;
                    }

                    EnsureAchievementsMainFocus();
                }
                catch
                {
                }
            }), DispatcherPriority.Input);
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
                    if (isAchievementOptionsVisible)
                    {
                        if (CanProcessControllerNavigation(-1))
                        {
                            AdjustAchievementOptionsValue(-1);
                        }
                    }
                    else if (isAchievementActionsVisible)
                    {
                        if (CanProcessControllerNavigation(-1))
                        {
                            MoveAchievementsFocus(-1);
                        }
                    }
                    else if (!isAchievementCaptureVisible && CanProcessControllerNavigation(-1))
                    {
                        FocusAchievementSettingsElement();
                    }
                    return true;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                    if (isAchievementOptionsVisible)
                    {
                        if (CanProcessControllerNavigation(1))
                        {
                            AdjustAchievementOptionsValue(1);
                        }
                    }
                    else if (isAchievementActionsVisible)
                    {
                        if (CanProcessControllerNavigation(1))
                        {
                            MoveAchievementsFocus(1);
                        }
                    }
                    else if (!isAchievementCaptureVisible && IsAchievementSettingsElement(GetCurrentAchievementsElement()) &&
                             CanProcessControllerNavigation(1))
                    {
                        FocusAchievementCardFromSettings();
                    }
                    return true;

                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    if (!isAchievementCaptureVisible && CanProcessControllerNavigation(-1))
                    {
                        if (isAchievementOptionsVisible)
                        {
                            MoveAchievementOptionsFocus(-1);
                        }
                        else if (isAchievementActionsVisible)
                        {
                            MoveAchievementsFocus(-1);
                        }
                        else
                        {
                            MoveAchievementCardFocus(-1);
                        }
                    }
                    return true;

                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    if (!isAchievementCaptureVisible && CanProcessControllerNavigation(1))
                    {
                        if (isAchievementOptionsVisible)
                        {
                            MoveAchievementOptionsFocus(1);
                        }
                        else if (isAchievementActionsVisible)
                        {
                            MoveAchievementsFocus(1);
                        }
                        else
                        {
                            MoveAchievementCardFocus(1);
                        }
                    }
                    return true;

                case ControllerInput.A:
                    if (CanProcessControllerAction())
                    {
                        ActivateAchievementsElement();
                    }
                    return true;

                case ControllerInput.B:
                case ControllerInput.Back:
                    if (isAchievementCaptureVisible)
                    {
                        HideAchievementCaptureLayer(true);
                    }
                    else if (isAchievementOptionsVisible)
                    {
                        var combo = GetCurrentAchievementsElement() as ComboBox;
                        if (combo != null && combo.IsDropDownOpen)
                        {
                            combo.IsDropDownOpen = false;
                        }
                        else
                        {
                            HideAchievementOptionsLayer(true);
                        }
                    }
                    else if (isAchievementActionsVisible)
                    {
                        HideAchievementActionsLayer(true);
                    }
                    else
                    {
                        HideAchievements();
                    }
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

            if (e.Key == Key.Left)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(-1))
                {
                    if (isAchievementOptionsVisible)
                    {
                        AdjustAchievementOptionsValue(-1);
                    }
                    else if (isAchievementActionsVisible)
                    {
                        MoveAchievementsFocus(-1);
                    }
                    else if (!isAchievementCaptureVisible)
                    {
                        FocusAchievementSettingsElement();
                    }
                }
                return true;
            }

            if (e.Key == Key.Right)
            {
                e.Handled = true;
                if (CanProcessControllerNavigation(1))
                {
                    if (isAchievementOptionsVisible)
                    {
                        AdjustAchievementOptionsValue(1);
                    }
                    else if (isAchievementActionsVisible)
                    {
                        MoveAchievementsFocus(1);
                    }
                    else if (!isAchievementCaptureVisible && IsAchievementSettingsElement(GetCurrentAchievementsElement()))
                    {
                        FocusAchievementCardFromSettings();
                    }
                }
                return true;
            }

            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                e.Handled = true;
                var direction = e.Key == Key.Up ? -1 : 1;
                if (!isAchievementCaptureVisible && CanProcessControllerNavigation(direction))
                {
                    if (isAchievementOptionsVisible)
                    {
                        MoveAchievementOptionsFocus(direction);
                    }
                    else if (isAchievementActionsVisible)
                    {
                        MoveAchievementsFocus(direction);
                    }
                    else
                    {
                        MoveAchievementCardFocus(direction);
                    }
                }
                return true;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                e.Handled = true;
                if (CanProcessControllerAction())
                {
                    ActivateAchievementsElement();
                }
                return true;
            }

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                e.Handled = true;
                if (isAchievementCaptureVisible)
                {
                    HideAchievementCaptureLayer(true);
                }
                else if (isAchievementOptionsVisible)
                {
                    var combo = GetCurrentAchievementsElement() as ComboBox;
                    if (combo != null && combo.IsDropDownOpen)
                    {
                        combo.IsDropDownOpen = false;
                    }
                    else
                    {
                        HideAchievementOptionsLayer(true);
                    }
                }
                else if (isAchievementActionsVisible)
                {
                    HideAchievementActionsLayer(true);
                }
                else
                {
                    HideAchievements();
                }
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
            var activeHost = isAppsVisible
                ? appsHost
                : (isGameLinksVisible ? gameLinksHost : null);

            if (activeHost == null)
            {
                return new FrameworkElement[0];
            }

            try
            {
                activeHost.ApplyTemplate();
                activeHost.UpdateLayout();

                var allElements = new List<FrameworkElement>();
                CollectVisualChildren(activeHost, allElements);

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
            if (!isAppsVisible && !isGameLinksVisible)
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
                    // While the overlay is visible, Playnite's standard controller processing
                    // is intentionally disabled. Activate the focused hosted control explicitly
                    // from the public SDK button event instead of relying on a second input path.
                    if (CanProcessControllerAction())
                    {
                        ActivateAppsElement();
                    }
                    return true;
            }

            return false;
        }

        private bool HandleAppsPreviewKeyDown(KeyEventArgs e)
        {
            if ((!isAppsVisible && !isGameLinksVisible) || e == null)
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
                    if (CanProcessControllerAction())
                    {
                        ActivateLastCapturesElement();
                    }
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

        private static bool HasObjectProperty(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                return instance.GetType().GetProperty(propertyName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetObjectStringProperty(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            try
            {
                return instance.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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

        public void HandleOverlayDPadFallback(ControllerInput button, DateTime requestedUtc)
        {
            if (button != ControllerInput.DPadLeft &&
                button != ControllerInput.DPadRight &&
                button != ControllerInput.DPadUp &&
                button != ControllerInput.DPadDown)
            {
                HandleOverlayControllerInput(button);
                return;
            }

            // If WPF/Playnite already translated this physical D-pad press into an arrow
            // key after the SDL callback was raised, native navigation has already happened.
            // Do not move a second time.
            if (lastNativeOverlayNavigationUtc >= requestedUtc.AddMilliseconds(-100))
            {
                return;
            }

            HandleOverlayControllerInput(button);
        }

        public void HandleOverlayControllerInput(ControllerInput button)
        {
            System.Diagnostics.Debug.WriteLine("[AnikiHelper][OverlayWindow] Received: " + button);

            if (virtualKeyboardView != null && virtualKeyboardView.HandleControllerInput(button))
            {
                lastDirectOverlayControllerInputTime = DateTime.Now;
                return;
            }

            if (HandleCapturePreviewControllerInput(button))
            {
                lastDirectOverlayControllerInputTime = DateTime.Now;
                return;
            }

            // Hosted overlay views explicitly activate their focused control from the
            // Playnite controller event. Remember the press so any WPF Enter/Space mirror of
            // the same physical A press is swallowed instead of activating the control twice.
            lastDirectOverlayControllerInputTime = DateTime.Now;

            if ((button == ControllerInput.B || button == ControllerInput.Back) &&
                (DateTime.Now - lastCapturePreviewClosedTime).TotalMilliseconds < 250)
            {
                return;
            }

            // Friends and Achievements have their own nested overlay navigation (list ->
            // action/profile and list -> action/capture), so let their dedicated handlers
            // decide what B means instead of closing the whole child view here.
            if ((isMusicPlayerVisible || isAudioSwitcherVisible || isUniPlaySongVisible || isLastCapturesVisible || isAppsVisible || isGameLinksVisible) &&
                (button == ControllerInput.B || button == ControllerInput.Back))
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
                else if (isLastCapturesVisible)
                {
                    HideLastCaptures();
                }
                else if (isAppsVisible)
                {
                    HideApps();
                }
                else if (isGameLinksVisible)
                {
                    HideGameLinks();
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

                case ControllerInput.Guide:
                    service.HideOverlay();
                    break;
            }
        }

        public void HandleControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (args == null || args.State != ControllerInputState.Pressed)
            {
                return;
            }

            if (virtualKeyboardView != null && virtualKeyboardView.HandleControllerInput(args.Button))
            {
                lastDirectOverlayControllerInputTime = DateTime.Now;
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

            if ((isMusicPlayerVisible || isAudioSwitcherVisible || isUniPlaySongVisible || isLastCapturesVisible || isAppsVisible || isGameLinksVisible) &&
                (args.Button == ControllerInput.B || args.Button == ControllerInput.Back))
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
                else if (isLastCapturesVisible)
                {
                    HideLastCaptures();
                }
                else if (isAppsVisible)
                {
                    HideApps();
                }
                else if (isGameLinksVisible)
                {
                    HideGameLinks();
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

            // Playnite controller events and WPF key mirroring can report the same physical press; share a short duplicate-input gate.
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

            if (isAppsVisible || isGameLinksVisible)
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
                return new[] { resumeButton, returnButton, keyboardButton, achievementsSectionButton, friendsButton, mediaSectionButton, gameLinksButton, musicButton, audioSectionButton, appsButton, quitButton }
                    .WhereButtonCanReceiveControllerFocus();
            }

            return new[] { musicButton, audioSectionButton, appsButton, friendsButton, mediaSectionButton, keyboardButton }
                .WhereButtonCanReceiveControllerFocus();
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


                if (button == resumeButton)
                {
                    service.ReturnToGame();
                    return;
                }

                if (button == returnButton)
                {
                    if (!service.IsGameRunning)
                    {
                        service.HideOverlay();
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

                if (button == appsButton)
                {
                    service.OpenAppsWindow();
                    return;
                }

                if (button == gameLinksButton)
                {
                    service.OpenGameLinksWindow();
                    return;
                }

                if (button == keyboardButton)
                {
                    ShowVirtualKeyboard();
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

        public void PrepareControllerFocusWithoutActivation()
        {
            try
            {
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
                UpdateAllButtonVisualStates();
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

            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                lastNativeOverlayNavigationUtc = DateTime.UtcNow;
            }

            if (virtualKeyboardView != null && virtualKeyboardView.HandlePreviewKeyDown(e))
            {
                return;
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
                else if (isGameLinksVisible)
                {
                    HideGameLinks();
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
