using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AnikiHelper.Services.ScreenSaver
{
    internal sealed class AnikiScreenSaverWindow : Window
    {
        private readonly Grid root;
        private readonly Grid slideLayer;
        private readonly Image backgroundImage;
        private readonly ScaleTransform backgroundScale;
        private readonly Image logoImage;
        private readonly TextBlock logoFallbackTitle;
        private readonly StackPanel logoPanel;
        private readonly Border infoCard;
        private readonly Grid infoCardGlassRoot;
        private readonly VisualBrush infoCardBackdropBrush;
        private readonly TextBlock gameTitle;
        private readonly TextBlock playtimeLabel;
        private readonly TextBlock playtimeValue;
        private readonly TextBlock achievementsLabel;
        private readonly TextBlock achievementsValue;
        private readonly TextBlock lastPlayedLabel;
        private readonly TextBlock lastPlayedValue;
        private readonly TextBlock statusText;
        private int transitionToken;
        private Point lastMousePosition;
        private bool mousePositionInitialized;
        private bool dismissRaised;
        private bool nextZoomIn = true;

        public event Action DismissRequested;

        public AnikiScreenSaverWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowState = WindowState.Maximized;
            Background = Brushes.Black;
            Topmost = true;
            ShowActivated = true;
            AllowsTransparency = false;
            Focusable = true;
            Cursor = Cursors.None;

            root = new Grid
            {
                Background = Brushes.Black,
                ClipToBounds = true
            };

            slideLayer = new Grid
            {
                Opacity = 0,
                ClipToBounds = true
            };

            backgroundScale = new ScaleTransform(1.0, 1.0);
            backgroundImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = backgroundScale,
                IsHitTestVisible = false
            };
            slideLayer.Children.Add(backgroundImage);

            slideLayer.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                IsHitTestVisible = false
            });

            slideLayer.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 430,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(220, 0, 0, 0),
                    new Point(0.5, 0),
                    new Point(0.5, 1)),
                IsHitTestVisible = false
            });

            slideLayer.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 760,
                Background = new LinearGradientBrush(
                    Color.FromArgb(145, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    new Point(0, 0.5),
                    new Point(1, 0.5)),
                IsHitTestVisible = false
            });

            logoImage = new Image
            {
                Stretch = Stretch.Uniform,
                MaxWidth = 570,
                MaxHeight = 230,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 30,
                    ShadowDepth = 0,
                    Opacity = 0.95
                }
            };

            logoFallbackTitle = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 52,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 680,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 26,
                    ShadowDepth = 0,
                    Opacity = 1
                }
            };

            logoPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(76, 0, 0, 72),
                IsHitTestVisible = false
            };
            logoPanel.Children.Add(logoImage);
            logoPanel.Children.Add(logoFallbackTitle);
            slideLayer.Children.Add(logoPanel);

            gameTitle = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 31,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            };

            playtimeLabel = CreateInfoLabel();
            playtimeValue = CreateInfoValue();
            achievementsLabel = CreateInfoLabel();
            achievementsValue = CreateInfoValue();
            lastPlayedLabel = CreateInfoLabel();
            lastPlayedValue = CreateInfoValue();

            var infoGrid = new Grid();
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddInfoRow(infoGrid, 0, playtimeLabel, playtimeValue);
            AddInfoRow(infoGrid, 1, achievementsLabel, achievementsValue);
            AddInfoRow(infoGrid, 2, lastPlayedLabel, lastPlayedValue);

            statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(234, 190, 103)),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var infoContent = new StackPanel();
            infoContent.Children.Add(gameTitle);
            infoContent.Children.Add(infoGrid);
            infoContent.Children.Add(statusText);

            infoCardBackdropBrush = new VisualBrush
            {
                Visual = backgroundImage,
                ViewboxUnits = BrushMappingMode.Absolute,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            var frostedBackdrop = new Border
            {
                Margin = new Thickness(-28),
                Background = infoCardBackdropBrush,
                IsHitTestVisible = false,
                Effect = new BlurEffect
                {
                    Radius = 11,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                }
            };

            var glassTint = new Border
            {
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(26, 34, 40, 54), 0),
                        new GradientStop(Color.FromArgb(34, 14, 19, 29), 0.52),
                        new GradientStop(Color.FromArgb(44, 7, 10, 17), 1)
                    },
                    new Point(0.15, 0),
                    new Point(0.85, 1)),
                IsHitTestVisible = false
            };

            var themeDarken = new Border
            {
                Background = new SolidColorBrush(
                Color.FromArgb(128, 0, 0, 0)),
                IsHitTestVisible = false
            };

            var glassHighlight = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(8, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0),
                    new Point(0.7, 0.75)),
                IsHitTestVisible = false
            };

            var infoContentHost = new Border
            {
                Padding = new Thickness(28, 24, 28, 24),
                Background = Brushes.Transparent,
                Child = infoContent
            };

            infoCardGlassRoot = new Grid();
            infoCardGlassRoot.Children.Add(frostedBackdrop);
            infoCardGlassRoot.Children.Add(glassTint);
            infoCardGlassRoot.Children.Add(themeDarken);
            infoCardGlassRoot.Children.Add(glassHighlight);
            infoCardGlassRoot.Children.Add(infoContentHost);

            infoCard = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 68, 66),
                Width = 455,
                CornerRadius = new CornerRadius(18),
                Background = Brushes.Transparent,
                BorderBrush = ResolveThemeBrush(
                    "MenuBorderBrush",
                    new SolidColorBrush(Color.FromArgb(92, 255, 255, 255))),
                BorderThickness = new Thickness(1),
                Child = infoCardGlassRoot,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 34,
                    ShadowDepth = 6,
                    Opacity = 0.58
                }
            };
            infoCardGlassRoot.SizeChanged += HandleInfoCardLayoutChanged;
            root.SizeChanged += HandleInfoCardLayoutChanged;
            slideLayer.Children.Add(infoCard);

            root.Children.Add(slideLayer);
            Content = root;

            PreviewKeyDown += HandleKeyInput;
            PreviewMouseDown += HandleMouseInput;
            PreviewMouseWheel += HandleMouseWheelInput;
            PreviewMouseMove += HandleMouseMove;
            PreviewTouchDown += HandleTouchInput;
            Deactivated += HandleDeactivated;
            Closed += HandleClosed;
        }

        public void ShowSlide(
            ScreenSaverSlide slide,
            bool showLogo,
            bool showInfoCard,
            bool animateBackground,
            bool useFadeTransition,
            TimeSpan displayDuration)
        {
            if (slide == null)
            {
                return;
            }

            var token = Interlocked.Increment(ref transitionToken);

            // Do not stop the current background zoom before the fade-out.
            // Keeping it alive prevents the outgoing image from snapping back to 1:1.
            slideLayer.BeginAnimation(UIElement.OpacityProperty, null);

            Action apply = () =>
            {
                if (token != transitionToken)
                {
                    return;
                }

                // The old image is now hidden (or is being replaced immediately when fades are disabled).
                // Stop its animation only now, then prepare the new image at the correct end of the zoom range.
                backgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                backgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

                var zoomIn = nextZoomIn;
                var initialScale = animateBackground && !zoomIn ? 1.065 : 1.0;
                backgroundScale.ScaleX = initialScale;
                backgroundScale.ScaleY = initialScale;

                backgroundImage.Source = slide.BackgroundImage;
                logoImage.Source = slide.LogoImage;
                logoImage.Visibility = showLogo && slide.LogoImage != null
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                logoFallbackTitle.Text = slide.GameName ?? string.Empty;
                logoFallbackTitle.Visibility = showLogo && slide.LogoImage == null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                logoPanel.Visibility = showLogo ? Visibility.Visible : Visibility.Collapsed;

                gameTitle.Text = slide.GameName ?? string.Empty;
                gameTitle.Visibility = showLogo ? Visibility.Collapsed : Visibility.Visible;
                playtimeLabel.Text = slide.PlaytimeLabel ?? string.Empty;
                playtimeValue.Text = slide.PlaytimeValue ?? string.Empty;
                achievementsLabel.Text = slide.AchievementsLabel ?? string.Empty;
                achievementsValue.Text = slide.AchievementsValue ?? string.Empty;
                lastPlayedLabel.Text = slide.LastPlayedLabel ?? string.Empty;
                lastPlayedValue.Text = slide.LastPlayedValue ?? string.Empty;
                statusText.Text = slide.StatusValue ?? string.Empty;
                infoCard.Visibility = showInfoCard ? Visibility.Visible : Visibility.Collapsed;
                UpdateInfoCardBackdrop();

                if (animateBackground)
                {
                    StartSlowZoom(displayDuration, zoomIn);
                    nextZoomIn = !nextZoomIn;
                }
            };

            if (!useFadeTransition || slideLayer.Opacity <= 0.01)
            {
                apply();
                FadeLayerTo(1, TimeSpan.FromMilliseconds(useFadeTransition ? 550 : 0));
                return;
            }

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(320),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };

            fadeOut.Completed += (_, __) =>
            {
                if (token != transitionToken)
                {
                    return;
                }

                slideLayer.Opacity = 0;
                apply();
                FadeLayerTo(1, TimeSpan.FromMilliseconds(500));
            };

            slideLayer.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        }

        public void CloseImmediately()
        {
            Interlocked.Increment(ref transitionToken);
            try
            {
                Close();
            }
            catch
            {
            }
        }

        private void FadeLayerTo(double opacity, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                slideLayer.BeginAnimation(UIElement.OpacityProperty, null);
                slideLayer.Opacity = opacity;
                return;
            }

            var fade = new DoubleAnimation
            {
                To = opacity,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            fade.Completed += (_, __) =>
            {
                slideLayer.BeginAnimation(UIElement.OpacityProperty, null);
                slideLayer.Opacity = opacity;
            };

            slideLayer.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }

        private void StartSlowZoom(TimeSpan displayDuration, bool zoomIn)
        {
            var duration = displayDuration + TimeSpan.FromSeconds(2);
            if (duration < TimeSpan.FromSeconds(6))
            {
                duration = TimeSpan.FromSeconds(6);
            }

            var fromScale = zoomIn ? 1.0 : 1.065;
            var toScale = zoomIn ? 1.065 : 1.0;

            backgroundScale.ScaleX = fromScale;
            backgroundScale.ScaleY = fromScale;

            var zoomX = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            var zoomY = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            backgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoomX, HandoffBehavior.SnapshotAndReplace);
            backgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoomY, HandoffBehavior.SnapshotAndReplace);
        }

        private void StopAnimations()
        {
            try
            {
                slideLayer.BeginAnimation(UIElement.OpacityProperty, null);
                backgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                backgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            catch
            {
            }
        }

        private void HandleInfoCardLayoutChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateInfoCardBackdrop();
        }

        private void UpdateInfoCardBackdrop()
        {
            if (root.ActualWidth <= 0 || root.ActualHeight <= 0 ||
                infoCard.ActualWidth <= 0 || infoCard.ActualHeight <= 0 ||
                infoCardGlassRoot.ActualWidth <= 0 || infoCardGlassRoot.ActualHeight <= 0)
            {
                return;
            }

            const double overscan = 28;
            var contentLeft = root.ActualWidth - infoCard.Margin.Right - infoCard.ActualWidth
                + infoCard.BorderThickness.Left;
            var contentTop = root.ActualHeight - infoCard.Margin.Bottom - infoCard.ActualHeight
                + infoCard.BorderThickness.Top;

            infoCardBackdropBrush.Viewbox = new Rect(
                contentLeft - overscan,
                contentTop - overscan,
                infoCardGlassRoot.ActualWidth + (overscan * 2),
                infoCardGlassRoot.ActualHeight + (overscan * 2));

            infoCardGlassRoot.Clip = new RectangleGeometry(
                new Rect(0, 0, infoCardGlassRoot.ActualWidth, infoCardGlassRoot.ActualHeight),
                17,
                17);
        }

        private static Brush ResolveThemeBrush(string resourceKey, Brush fallback)
        {
            try
            {
                var resource = Application.Current?.TryFindResource(resourceKey);

                if (resource is Brush brush)
                {
                    return brush;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static Color ResolveThemeColor(string resourceKey, Color fallback)
        {
            try
            {
                var resource = Application.Current?.TryFindResource(resourceKey);

                if (resource is Color color)
                {
                    return color;
                }

                if (resource is SolidColorBrush brush)
                {
                    return brush.Color;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static TextBlock CreateInfoLabel()
        {
            return new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(232, 235, 242)),
                FontSize = 17,
                Margin = new Thickness(0, 5, 28, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static TextBlock CreateInfoValue()
        {
            return new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 5, 0, 5),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static void AddInfoRow(Grid grid, int row, UIElement label, UIElement value)
        {
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }

        private void HandleKeyInput(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            RaiseDismissRequested();
        }

        private void HandleMouseInput(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            RaiseDismissRequested();
        }

        private void HandleMouseWheelInput(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            RaiseDismissRequested();
        }

        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(this);
            if (!mousePositionInitialized)
            {
                lastMousePosition = position;
                mousePositionInitialized = true;
                return;
            }

            if (Math.Abs(position.X - lastMousePosition.X) >= 2 ||
                Math.Abs(position.Y - lastMousePosition.Y) >= 2)
            {
                e.Handled = true;
                RaiseDismissRequested();
            }
        }

        private void HandleTouchInput(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            RaiseDismissRequested();
        }

        private void HandleDeactivated(object sender, EventArgs e)
        {
            RaiseDismissRequested();
        }

        private void RaiseDismissRequested()
        {
            if (dismissRaised)
            {
                return;
            }

            dismissRaised = true;
            DismissRequested?.Invoke();
        }

        private void HandleClosed(object sender, EventArgs e)
        {
            StopAnimations();
            backgroundImage.Source = null;
            logoImage.Source = null;
            Cursor = Cursors.Arrow;
        }
    }
}
