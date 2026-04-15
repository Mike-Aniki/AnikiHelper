using Playnite.SDK;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;

namespace AnikiHelper
{
    internal sealed class AnikiVideoOverlayWindow : Window
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly DispatcherTimer timeoutTimer;
        private readonly MediaElement mediaElement;
        private readonly string videoPath;

        public AnikiVideoOverlayWindow(string videoPath, TimeSpan timeout)
        {
            this.videoPath = videoPath ?? throw new ArgumentNullException(nameof(videoPath));

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            AllowsTransparency = false;
            Focusable = true;
            IsHitTestVisible = true;

            var root = new Grid
            {
                Background = Brushes.Black
            };

            mediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Play,
                UnloadedBehavior = MediaState.Close,
                Stretch = Stretch.UniformToFill,
                Focusable = false
            };

            if (File.Exists(videoPath))
            {
                mediaElement.Source = new Uri(videoPath, UriKind.Absolute);
            }

            root.Children.Add(mediaElement);
            Content = root;

            timeoutTimer = new DispatcherTimer
            {
                Interval = timeout
            };

            timeoutTimer.Tick += TimeoutTimer_Tick;

            ShowActivated = true;

            Loaded += (s, e2) =>
            {
                try
                {
                    Activate();
                    Focus();
                    Keyboard.Focus(this);
                }
                catch { }
            };

            Activated += (s, e2) =>
            {
                try
                {
                    Focus();
                    Keyboard.Focus(this);
                }
                catch { }
            };

            PreviewKeyDown += Overlay_PreviewKeyDown;

            Loaded += ShutdownVideoWindow_Loaded;
            Closed += ShutdownVideoWindow_Closed;

            mediaElement.MediaOpened += MediaElement_MediaOpened;
            mediaElement.MediaEnded += MediaElement_MediaEnded;
            mediaElement.MediaFailed += MediaElement_MediaFailed;
        }

        private void Overlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                e.Handled = true;

                try
                {
                    timeoutTimer.Stop();
                    mediaElement.Stop();
                    mediaElement.Close();
                    mediaElement.Source = null;
                }
                catch { }

                try
                {
                    Application.Current?.Shutdown();
                }
                catch { }
            }
        }

        private void ShutdownVideoWindow_Loaded(object sender, RoutedEventArgs e)
        {
            timeoutTimer.Start();
        }

        private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            mediaElement.Visibility = Visibility.Visible;
        }

        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                mediaElement.Stop();
                mediaElement.Visibility = Visibility.Collapsed;
                mediaElement.Source = null;
            }
            catch { }
        }

        private void MediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            logger.Warn($"[AnikiHelper] Shutdown video failed. Path='{videoPath}' Exists={File.Exists(videoPath)} Error='{e.ErrorException?.Message}'");

            try
            {
                mediaElement.Stop();
                mediaElement.Visibility = Visibility.Collapsed;
                mediaElement.Source = null;
            }
            catch { }
        }

        private void TimeoutTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                Close();
            }
            catch { }
        }

        private void ShutdownVideoWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                timeoutTimer.Stop();
                mediaElement.Stop();
                mediaElement.Close();
                mediaElement.Source = null;
            }
            catch { }
        }
    }
}