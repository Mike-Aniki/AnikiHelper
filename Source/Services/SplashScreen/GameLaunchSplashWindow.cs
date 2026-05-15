using Playnite.SDK.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AnikiHelper.Services.SplashScreen
{
    public class GameLaunchSplashWindow : Window
    {
        public Guid GameId { get; }

        private readonly Grid root;
        private readonly Image backgroundImage;
        private readonly ScaleTransform backgroundScale;
        private readonly MediaElement backgroundVideo;
        private readonly bool isVideoSplash;
        private readonly SplashScreenVideoEndBehavior videoEndBehavior;

        private bool isClosingAnimated;

        public GameLaunchSplashWindow(
            Game game,
            string backgroundPath,
            string fallbackBackgroundPath,
            bool showLogo,
            SplashScreenLogoPosition logoPosition,
            bool videoSoundEnabled,
            SplashScreenVideoEndBehavior videoEndBehavior,
            double videoVolume)
        {
            GameId = game?.Id ?? Guid.Empty;
            this.videoEndBehavior = videoEndBehavior;

            isVideoSplash = !string.IsNullOrWhiteSpace(backgroundPath)
                && File.Exists(backgroundPath)
                && SplashScreenMediaScanner.IsVideoFile(backgroundPath);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowState = WindowState.Maximized;
            Background = Brushes.Black;
            Focusable = false;
            ShowActivated = true;
            AllowsTransparency = false;
            Opacity = isVideoSplash ? 1 : 0;

            root = new Grid
            {
                Background = Brushes.Black,
                ClipToBounds = true
            };

            backgroundScale = new ScaleTransform(1.0, 1.0);

            backgroundImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = backgroundScale
            };

            backgroundVideo = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Volume = Math.Max(0, Math.Min(1, videoVolume)),
                IsMuted = !videoSoundEnabled,
                Opacity = 0
            };

            backgroundVideo.MediaOpened += (_, __) =>
            {
                var fadeVideo = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(550),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                backgroundVideo.BeginAnimation(UIElement.OpacityProperty, fadeVideo);
            };

            backgroundVideo.MediaEnded += (_, __) =>
            {
                if (this.videoEndBehavior == SplashScreenVideoEndBehavior.ShowGameBackground)
                {
                    FadeOutEndedVideo();
                }
            };

            if (isVideoSplash)
            {
                if (videoEndBehavior == SplashScreenVideoEndBehavior.ShowGameBackground)
                {
                    AddStaticBackgroundLayer(fallbackBackgroundPath);

                    if (showLogo)
                    {
                        AddGameLogo(game, logoPosition);
                    }
                }

                try
                {
                    backgroundVideo.Source = new Uri(backgroundPath, UriKind.Absolute);
                    root.Children.Add(backgroundVideo);
                }
                catch
                {
                    // Fallback noir
                }
            }
            else
            {
                AddStaticBackgroundLayer(backgroundPath);

                if (showLogo)
                {
                    AddGameLogo(game, logoPosition);
                }
            }

            Content = root;

            Loaded += GameLaunchSplashWindow_Loaded;

            Closed += (_, __) =>
            {
                StopAndCloseVideo();
            };
        }

        private void AddStaticBackgroundLayer(string imagePath)
        {
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    backgroundImage.Source = new BitmapImage(new Uri(imagePath, UriKind.Absolute));
                }
                catch
                {
                    // Fallback noir
                }
            }

            root.Children.Add(backgroundImage);

            var darkOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(95, 0, 0, 0))
            };
            root.Children.Add(darkOverlay);

            var topGradient = new Border
            {
                VerticalAlignment = VerticalAlignment.Top,
                Height = 260,
                Background = new LinearGradientBrush(
                    Color.FromArgb(170, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    new Point(0.5, 0),
                    new Point(0.5, 1))
            };
            root.Children.Add(topGradient);

            var bottomGradient = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 340,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(200, 0, 0, 0),
                    new Point(0.5, 0),
                    new Point(0.5, 1))
            };
            root.Children.Add(bottomGradient);

            var sideOverlay = new Grid
            {
                IsHitTestVisible = false
            };

            sideOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            sideOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sideOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

            var leftShade = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(150, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    new Point(0, 0.5),
                    new Point(1, 0.5))
            };
            Grid.SetColumn(leftShade, 0);
            sideOverlay.Children.Add(leftShade);

            var rightShade = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(150, 0, 0, 0),
                    new Point(0, 0.5),
                    new Point(1, 0.5))
            };
            Grid.SetColumn(rightShade, 2);
            sideOverlay.Children.Add(rightShade);

            root.Children.Add(sideOverlay);
        }

        private void AddGameLogo(Game game, SplashScreenLogoPosition logoPosition)
        {
            try
            {
                if (game == null)
                {
                    return;
                }

                var extraMetadataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Playnite",
                    "ExtraMetadata",
                    "games",
                    game.Id.ToString(),
                    "Logo.png");

                if (!File.Exists(extraMetadataPath))
                {
                    return;
                }

                var logoImage = new Image
                {
                    Source = new BitmapImage(new Uri(extraMetadataPath, UriKind.Absolute)),
                    Stretch = Stretch.Uniform,
                    MaxWidth = 560,
                    MaxHeight = 220,
                    HorizontalAlignment = GetLogoHorizontalAlignment(logoPosition),
                    VerticalAlignment = GetLogoVerticalAlignment(logoPosition),
                    Margin = GetLogoMargin(logoPosition),
                    Opacity = 0.98,
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 32,
                        ShadowDepth = 0,
                        Opacity = 0.95
                    }
                };

                RenderOptions.SetBitmapScalingMode(logoImage, BitmapScalingMode.HighQuality);
                root.Children.Add(logoImage);
            }
            catch
            {
            }
        }

        

        private HorizontalAlignment GetLogoHorizontalAlignment(SplashScreenLogoPosition position)
        {
            switch (position)
            {
                case SplashScreenLogoPosition.LeftTop:
                case SplashScreenLogoPosition.LeftCenter:
                case SplashScreenLogoPosition.LeftBottom:
                    return HorizontalAlignment.Left;

                case SplashScreenLogoPosition.RightTop:
                case SplashScreenLogoPosition.RightCenter:
                case SplashScreenLogoPosition.RightBottom:
                    return HorizontalAlignment.Right;

                default:
                    return HorizontalAlignment.Center;
            }
        }

        private VerticalAlignment GetLogoVerticalAlignment(SplashScreenLogoPosition position)
        {
            switch (position)
            {
                case SplashScreenLogoPosition.LeftTop:
                case SplashScreenLogoPosition.CenterTop:
                case SplashScreenLogoPosition.RightTop:
                    return VerticalAlignment.Top;

                case SplashScreenLogoPosition.LeftBottom:
                case SplashScreenLogoPosition.CenterBottom:
                case SplashScreenLogoPosition.RightBottom:
                    return VerticalAlignment.Bottom;

                default:
                    return VerticalAlignment.Center;
            }
        }

        private Thickness GetLogoMargin(SplashScreenLogoPosition position)
        {
            switch (position)
            {
                case SplashScreenLogoPosition.LeftTop:
                    return new Thickness(120, 120, 0, 0);

                case SplashScreenLogoPosition.LeftCenter:
                    return new Thickness(120, 0, 0, 0);

                case SplashScreenLogoPosition.LeftBottom:
                    return new Thickness(120, 0, 0, 120);

                case SplashScreenLogoPosition.CenterTop:
                    return new Thickness(0, 120, 0, 0);

                case SplashScreenLogoPosition.CenterBottom:
                    return new Thickness(0, 0, 0, 120);

                case SplashScreenLogoPosition.RightTop:
                    return new Thickness(0, 120, 120, 0);

                case SplashScreenLogoPosition.RightCenter:
                    return new Thickness(0, 0, 120, 0);

                case SplashScreenLogoPosition.RightBottom:
                    return new Thickness(0, 0, 120, 120);

                default:
                    return new Thickness(0);
            }
        }

        private void GameLaunchSplashWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!isVideoSplash)
            {
                StartIntroAnimation();
            }

            if (backgroundVideo?.Source != null)
            {
                try
                {
                    backgroundVideo.Position = TimeSpan.Zero;
                    backgroundVideo.Play();
                }
                catch
                {
                }
            }
            else
            {
                StartSlowZoomAnimation();
            }
        }

        private void StartIntroAnimation()
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(Window.OpacityProperty, fadeIn);
        }

        private void StartSlowZoomAnimation()
        {
            var zoom = new DoubleAnimation
            {
                From = 1.0,
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(6000),
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            backgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            backgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        }

        private void FadeOutEndedVideo()
        {
            try
            {
                var fadeOut = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(450),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };

                fadeOut.Completed += (_, __) =>
                {
                    try
                    {
                        backgroundVideo.Stop();

                        if (backgroundImage?.Source != null)
                        {
                            StartSlowZoomAnimation();
                        }
                    }
                    catch
                    {
                    }
                };

                backgroundVideo.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            catch
            {
            }
        }

        private void StopAndCloseVideo()
        {
            try
            {
                backgroundVideo?.Stop();
                backgroundVideo?.Close();
            }
            catch
            {
            }
        }

        public Task BeginCloseAsync()
        {
            if (isClosingAnimated)
            {
                return Task.CompletedTask;
            }

            isClosingAnimated = true;

            var tcs = new TaskCompletionSource<bool>();

            Dispatcher.Invoke(() =>
            {
                try
                {
                    var fadeOut = new DoubleAnimation
                    {
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(350),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };

                    fadeOut.Completed += (s, e) =>
                    {
                        StopAndCloseVideo();
                        tcs.TrySetResult(true);
                    };

                    BeginAnimation(Window.OpacityProperty, fadeOut);
                }
                catch
                {
                    StopAndCloseVideo();
                    tcs.TrySetResult(true);
                }
            });

            return tcs.Task;
        }
    }
}