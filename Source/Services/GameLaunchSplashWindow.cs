using Playnite.SDK.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AnikiHelper.Services
{
    public class GameLaunchSplashWindow : Window
    {
        public Guid GameId { get; }

        private readonly Grid root;
        private readonly Image backgroundImage;
        private readonly ScaleTransform backgroundScale;

        private bool isClosingAnimated;

        public GameLaunchSplashWindow(Game game, string backgroundPath)
        {
            GameId = game?.Id ?? Guid.Empty;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowState = WindowState.Maximized;
            Background = Brushes.Black;
            Focusable = false;
            ShowActivated = true;
            AllowsTransparency = false;
            Opacity = 0;

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

            if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
            {
                try
                {
                    backgroundImage.Source = new BitmapImage(new Uri(backgroundPath, UriKind.Absolute));
                }
                catch
                {
                    // Fallback noir
                }
            }

            root.Children.Add(backgroundImage);

            // Overlay global sombre
            var darkOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(95, 0, 0, 0))
            };
            root.Children.Add(darkOverlay);

            // Dégradé haut
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

            // Dégradé bas
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

            // Vignette douce gauche/droite
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

            Content = root;

            Loaded += GameLaunchSplashWindow_Loaded;
        }

        private void GameLaunchSplashWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartIntroAnimation();
            StartSlowZoomAnimation();
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
                To = 1.03,
                Duration = TimeSpan.FromMilliseconds(6000),
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            backgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            backgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
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
                        tcs.TrySetResult(true);
                    };

                    BeginAnimation(Window.OpacityProperty, fadeOut);
                }
                catch
                {
                    tcs.TrySetResult(true);
                }
            });

            return tcs.Task;
        }
    }
}