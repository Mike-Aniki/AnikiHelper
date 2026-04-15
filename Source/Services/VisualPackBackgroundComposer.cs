using Playnite.SDK;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnikiHelper
{
    internal static class VisualPackBackgroundComposer
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static DispatcherTimer timer;

        private static string lastMainMenuSignature = string.Empty;
        private static string lastSettingSignature = string.Empty;
        private static string lastFrameSettingSignature = string.Empty;
        private static string lastBoxMessageSignature = string.Empty;
        private static string lastGameMenuSignature = string.Empty;
        private static string lastItemMenuSignature = string.Empty;

        private const string MainMenuOutputKey = "MainMenuActiveVisualPackBackground";
        private const string SettingOutputKey = "SettingActiveVisualPackBackground";
        private const string FrameSettingOutputKey = "FrameSettingActiveVisualPackBackground";
        private const string BoxMessageOutputKey = "BoxMessageActiveVisualPackBackground";
        private const string GameMenuOutputKey = "GameMenuActiveVisualPackBackground";
        private const string ItemMenuOutputKey = "ItemMenuActiveVisualPackBackground";

        private const double ImageOpacityMultiplier = 0.10;

        public static void Start()
        {
            if (timer != null)
            {
                return;
            }

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            timer.Tick += Timer_Tick;
            timer.Start();

            try
            {
                UpdateAllCompositeBrushesIfNeeded();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] VisualPackBackgroundComposer initial update failed.");
            }

            try
            {
                Application.Current.Exit += (_, __) => Stop();
            }
            catch { }
        }

        public static void Stop()
        {
            try
            {
                if (timer != null)
                {
                    timer.Stop();
                    timer.Tick -= Timer_Tick;
                    timer = null;
                }

                lastMainMenuSignature = string.Empty;
                lastSettingSignature = string.Empty;
                lastFrameSettingSignature = string.Empty;
                lastBoxMessageSignature = string.Empty;
                lastGameMenuSignature = string.Empty;
                lastItemMenuSignature = string.Empty;
            }
            catch { }
        }

        private static void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                UpdateAllCompositeBrushesIfNeeded();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] VisualPackBackgroundComposer tick failed.");
            }
        }

        private static void UpdateAllCompositeBrushesIfNeeded()
        {
            UpdateCompositeBrushIfNeeded(
                sourcePrefix: "MainMenuImage",
                outputKey: MainMenuOutputKey,
                overlayKey: "OverlayMenu",
                lastSignatureRef: ref lastMainMenuSignature);

            UpdateCompositeBrushIfNeeded(
                sourcePrefix: "SettingImage",
                outputKey: SettingOutputKey,
                overlayKey: "OverlayMenu",
                lastSignatureRef: ref lastSettingSignature);

            UpdateCompositeBrushIfNeeded(
                sourcePrefix: "FrameSettingImage",
                outputKey: FrameSettingOutputKey,
                overlayKey: "OverlayMenu",
                lastSignatureRef: ref lastFrameSettingSignature);

            UpdateCompositeBrushIfNeeded(
                sourcePrefix: "BoxMessageImage",
                outputKey: BoxMessageOutputKey,
                overlayKey: "OverlayMenu",
                lastSignatureRef: ref lastBoxMessageSignature);

            UpdateCompositeBrushIfNeeded(
                sourcePrefix: "GameMenuImage",
                outputKey: GameMenuOutputKey,
                overlayKey: "OverlayMenu",
                lastSignatureRef: ref lastGameMenuSignature);

            UpdateCompositeBrushIfNeeded(
                sourcePrefix: "ItemMenuImage",
                outputKey: ItemMenuOutputKey,
                overlayKey: "OverlayMenu",
                lastSignatureRef: ref lastItemMenuSignature);
        }

        private static void UpdateCompositeBrushIfNeeded(
            string sourcePrefix,
            string outputKey,
            string overlayKey,
            ref string lastSignatureRef)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            var overlayBrush = app.TryFindResource(overlayKey) as Brush;
            if (overlayBrush == null)
            {
                return;
            }

            if (!TryGetCurrentBackgroundIndex(out var index))
            {
                return;
            }

            var sourceKey = $"{sourcePrefix}{index}";
            var sourceBrush = app.TryFindResource(sourceKey) as Brush;
            if (sourceBrush == null)
            {
                return;
            }

            var signature =
                $"SRC:{sourcePrefix}|IDX:{index}|OV:{MakeBrushSignature(overlayBrush)}|IMG:{MakeBrushSignature(sourceBrush)}";

            if (string.Equals(signature, lastSignatureRef, StringComparison.Ordinal))
            {
                return;
            }

            var compositeBrush = BuildCompositeBrush(overlayBrush, sourceBrush);
            if (compositeBrush == null)
            {
                return;
            }

            app.Resources[outputKey] = compositeBrush;
            lastSignatureRef = signature;
        }

        private static bool TryGetCurrentBackgroundIndex(out int index)
        {
            index = 0;

            try
            {
                var raw = Application.Current?.TryFindResource("BackgroundImageIndex");
                if (raw == null)
                {
                    return false;
                }

                if (raw is int i)
                {
                    index = i;
                    return true;
                }

                if (raw is long l)
                {
                    index = (int)l;
                    return true;
                }

                if (raw is short s)
                {
                    index = s;
                    return true;
                }

                if (int.TryParse(raw.ToString(), out var parsed))
                {
                    index = parsed;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static Brush BuildCompositeBrush(Brush overlayBrush, Brush imageBrush)
        {
            try
            {
                var overlayClone = overlayBrush.CloneCurrentValue();
                var imageClone = imageBrush.CloneCurrentValue();

                imageClone.Opacity = Math.Max(
                    0.0,
                    Math.Min(1.0, imageClone.Opacity * ImageOpacityMultiplier)
                );

                var imageRect = GetSourceRect(imageClone);

                var drawingGroup = new DrawingGroup();

                drawingGroup.Children.Add(
                    new GeometryDrawing(
                        overlayClone,
                        null,
                        new RectangleGeometry(imageRect))
                );

                drawingGroup.Children.Add(
                    new GeometryDrawing(
                        imageClone,
                        null,
                        new RectangleGeometry(imageRect))
                );

                var result = new DrawingBrush(drawingGroup)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };

                if (result.CanFreeze)
                {
                    result.Freeze();
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper] Failed to build composite brush.");
                return null;
            }
        }

        private static Rect GetSourceRect(Brush brush)
        {
            if (brush is ImageBrush imageBrush &&
                imageBrush.ImageSource is BitmapSource bitmap &&
                bitmap.PixelWidth > 0 &&
                bitmap.PixelHeight > 0)
            {
                return new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
            }

            return new Rect(0, 0, 500, 1080);
        }

        private static string MakeBrushSignature(Brush brush)
        {
            if (brush == null)
            {
                return "NULL";
            }

            try
            {
                switch (brush)
                {
                    case SolidColorBrush solid:
                        return $"SOLID:{solid.Color}:{solid.Opacity}";

                    case LinearGradientBrush gradient:
                        var parts = "";
                        foreach (var stop in gradient.GradientStops)
                        {
                            parts += $"{stop.Offset}:{stop.Color}|";
                        }
                        return $"LGB:{parts}:{gradient.Opacity}";

                    case ImageBrush image:
                        var src = image.ImageSource?.ToString() ?? "NULL";
                        return $"IMG:{src}:{image.Opacity}:{image.Stretch}";

                    case DrawingBrush drawing:
                        return $"DRAW:{drawing.Opacity}:{drawing.Stretch}";

                    default:
                        return $"{brush.GetType().FullName}:{brush.Opacity}";
                }
            }
            catch
            {
                return brush.ToString();
            }
        }
    }
}