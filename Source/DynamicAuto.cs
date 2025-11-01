// Dynamic color theme driver + animated transitions.
// - Touches only: Glyph/Text/HLTB/Highlight/Glow colors, OverlayMenu, ButtonPlay,
//   Focus/NoFocus/Menu borders, and a few auxiliary color keys.
// - Color source: game Background first, then Cover as fallback.

using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;

namespace AnikiHelper
{
    internal static class DynamicAuto
    {
        private static IPlayniteAPI api;
        private static readonly ILogger log = LogManager.GetLogger();

        private static DispatcherTimer timer;
        private static Guid? lastGameId;

        private static CancellationTokenSource debounceCts;
        private static CancellationTokenSource animCts;

        private const int DebounceMs = 110;          // decrease = more reactive
        private const int TransitionMs = 220;        // total fade time
        private const int TransitionSteps = 11;      // + more steps = smoother

        private static bool lastActive = false;
        private static int tickGate = 0; // 0 = libre, 1 = in progress (re-entry barrier)


        // ONLY this keys 
        private static readonly string[] KeysToTouch = new[]
        {
            "GlyphColor","GlowFocusColor","TextColor","TextSecondaryColor","TextDetail",
            "TextAltDetail","TextHighlight","HltbAlt","DynamicGlowBackgroundPrimary",
            "OverlayStart","OverlayMid","OverlayEnd",
            "ButtonPlayMid","ButtonPlayEnd",
            "FocusStart","FocusMid","FocusEnd",
            "MenuBorderStart","MenuBorderEnd",
            "NoFocusStart","NoFocusEnd",
            "ControlBackgroundColor","SuccessStartColor",
            "GlowMidColor","GlowEndColor",
            "ShadeMidColor", "ShadeEndColor"

        };

        private static readonly string[] BrushesToTouch = new[]
        {
            "OverlayMenu","ButtonPlayColor","FocusGameBorderBrush","MenuBorderBrush","NoFocusBorderButtonBrush","SuccessMenu","DynamicGlowBackgroundSuccess","ShadeBackground"
        };

        private static readonly Dictionary<string, object> snapshot = new Dictionary<string, object>();

        public static void Init(IPlayniteAPI playniteApi)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));

            // 1) No Desktop
            if (api.ApplicationInfo.Mode != ApplicationMode.Fullscreen)
                return;

            // 2) The resource must exist
            var dict = Application.Current?.Resources;
            if (dict == null || !dict.Contains("DynamicAutoEnabled"))
                return;

            // 3) And be true
            var enabled = dict["DynamicAutoEnabled"] as bool?;
            if (enabled != true)
                return;

            // 4) Start the timer
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += async (_, __) =>
            {
                // Gate: prevents 2 Ticks in parallel
                if (Interlocked.Exchange(ref tickGate, 1) == 1)
                    return;

                try
                {
                    if (api.ApplicationInfo.Mode == ApplicationMode.Desktop)
                        return;

                    bool active = IsDynamicAutoActive();

                    if (active && !lastActive)
                    {
                        CaptureSnapshot();
                        lastGameId = null;
                        lastActive = true;
                    }
                    if (!active && lastActive)
                    {
                        animCts?.Cancel();
                        animCts?.Dispose();
                        RestoreSnapshot();
                        lastActive = false;
                        return;
                    }
                    if (!active) return;

                    var initialGame = api.MainView?.SelectedGames?.FirstOrDefault();
                    if (initialGame is null) return;
                    if (lastGameId == initialGame.Id) return;

                    // === Debounce: swap propre (annule l'ancien, remplace par le nouveau) ===
                    var prev = Interlocked.Exchange(ref debounceCts, null);
                    prev?.Cancel();
                    prev?.Dispose();

                    var cts = new CancellationTokenSource();
                    debounceCts = cts;
                    var ct = cts.Token;

                    try
                    {
                        // anti-rebond
                        try { await Task.Delay(DebounceMs, ct); } catch { return; }
                        if (ct.IsCancellationRequested) return;

                        // Replay after waiting
                        var current = api.MainView?.SelectedGames?.FirstOrDefault();
                        if (current is null) return;
                        if (lastGameId == current.Id) return;

                        // Only now is the game validated
                        lastGameId = current.Id;

                        // Image path resolution
                        string imgPath = null;

                        if (!string.IsNullOrEmpty(current.BackgroundImage))
                        {
                            var p = api.Database.GetFullFilePath(current.BackgroundImage);
                            if (!string.IsNullOrEmpty(p) && File.Exists(p) && !IsVideo(p) && IsWpfImage(p))
                                imgPath = p;
                        }

                        if (string.IsNullOrEmpty(imgPath) && !string.IsNullOrEmpty(current.CoverImage))
                        {
                            var p = api.Database.GetFullFilePath(current.CoverImage);
                            if (!string.IsNullOrEmpty(p) && File.Exists(p) && !IsVideo(p) && IsWpfImage(p))
                                imgPath = p;
                        }

                        if (string.IsNullOrEmpty(imgPath)) return;

                        // Decoding + dominant extraction + palette
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bmp.UriSource = new Uri(imgPath, UriKind.Absolute);
                        bmp.DecodePixelWidth = 128;
                        bmp.EndInit();
                        bmp.Freeze();

                        BitmapSource src = bmp;
                        var pf = PixelFormats.Bgra32;
                        if (src.Format != pf)
                        {
                            src = new FormatConvertedBitmap(src, pf, null, 0);
                            src.Freeze();
                        }

                        int w = src.PixelWidth;
                        int h = src.PixelHeight;
                        int stride = (w * pf.BitsPerPixel + 7) / 8;
                        byte[] pixels = new byte[stride * h];
                        src.CopyPixels(pixels, stride, 0);

                        var accent = GetDominantVividColor_FromPixels(pixels, w, h, stride);
                        var target = BuildPalette(accent);

                        StartAnimatedTransition(target);
                    }
                    catch (Exception ex)
                    {
                        log.Warn(ex, "[AnikiHelper] Failed to build/apply palette");
                    }
                    finally
                    {
                        // "forget" the active instance and safely release cts
                        var oldCts = Interlocked.Exchange(ref debounceCts, null);
                        oldCts?.Dispose();

                    }
                }
                finally
                {
                    // Releases the barrier to allow the next tick
                    Volatile.Write(ref tickGate, 0);
                }
            };

            timer.Start();
        }




        // --- Color delta helper (avoid useless animations) ---
        private static bool IsClose(MediaColor a, MediaColor b, byte tol = 6)
        {
            int dR = a.R - b.R, dG = a.G - b.G, dB = a.B - b.B;
            return (dR * dR + dG * dG + dB * dB) <= (tol * tol * 3);
        }

        // --- Utility methods for safe file validation ---
        private static bool IsVideo(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp4" || ext == ".webm" || ext == ".avi" || ext == ".mkv";
        }

        private static bool IsWpfImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                   ext == ".bmp" || ext == ".gif" || ext == ".tif" ||
                   ext == ".tiff" || ext == ".ico";
        }


        private static bool IsDynamicAutoActive()
        {
            try
            {
                var res = Application.Current?.Resources?["DynamicAutoEnabled"];
                return res is bool b && b;
            }
            catch { return false; }
        }

        private static void CaptureSnapshot()
        {
            snapshot.Clear();
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            foreach (var key in KeysToTouch)
            {
                if (dict.Contains(key)) snapshot[key] = dict[key];
            }
            foreach (var key in BrushesToTouch)
            {
                if (dict.Contains(key)) snapshot[key] = dict[key];
            }
        }

        private static void RestoreSnapshot()
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            foreach (var kv in snapshot) dict[kv.Key] = kv.Value;
            snapshot.Clear();
        }

        // ---------- Dominante "vivid" ----------
        private static MediaColor GetDominantVividColor_FromPixels(byte[] pixels, int w, int h, int stride)
        {
            if (pixels == null || w <= 0 || h <= 0)
                return MediaColor.FromRgb(31, 35, 45);

            // Subsampling: 1 pixel out of 2 in X and Y is read
            const int stepX = 2, stepY = 2;

            // Center window 12%...88%
            int x0 = (int)(w * 0.12);
            int x1 = (int)(w * 0.88);
            int y0 = (int)(h * 0.12);
            int y1 = (int)(h * 0.88);

            int[] hist = new int[4096];
            int considered = 0;
            int brightCount = 0;

            for (int y = y0; y < y1; y += stepY)
            {
                int row = y * stride;
                for (int x = x0; x < x1; x += stepX)
                {
                    int i = row + x * 4;
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a < 16) continue;

                    // Luminance in 0..255 (integers)
                    int lum = Lum255(r, g, b);
                    if (lum < 31) continue;        // ≈ 0.12*255

                    considered++;
                    if (lum > 209) brightCount++;  // ≈ 0.82*255

                    // "Too gray" filter (integers)
                    if (IsLowSat(r, g, b)) continue;

                    // "Skin tone" filter: calculates hue only if relevant
                    if (SkinHueLikely(r, g, b))
                    {
                        byte max = Math.Max(r, Math.Max(g, b));
                        byte min = Math.Min(r, Math.Min(g, b));
                        double d = max - min;
                        if (d > 0)
                        {
                            double hue;
                            if (max == r) hue = ((g - b) / d) % 6.0;
                            else if (max == g) hue = ((b - r) / d) + 2.0;
                            else hue = ((r - g) / d) + 4.0;
                            hue *= 60.0;
                            if (hue < 0) hue += 360.0;

                            // sat < 0.45  ↔  (max-min)/max < 0.45 → (max-min)*100 < 45*max
                            bool satModerate = ((max - min) * 100) < (45 * Math.Max(1, (int)max));
                            if (hue >= 15 && hue <= 45 && satModerate)
                                continue;
                        }
                    }

                    int rq = r >> 4, gq = g >> 4, bq = b >> 4;
                    hist[(rq << 8) | (gq << 4) | bq]++;
                }
            }

            if (considered == 0)
                return MediaColor.FromRgb(31, 35, 45);

            int maxCount = 0, maxIdx = -1;
            for (int k = 0; k < hist.Length; k++)
                if (hist[k] > maxCount) { maxCount = hist[k]; maxIdx = k; }

            bool tooSmallPeak = maxCount < Math.Max(50, considered / 300);         // ~0.33%
            bool veryBrightBg = (brightCount / (double)considered) > 0.55;

            if ((maxIdx < 0 || tooSmallPeak) && veryBrightBg)
            {
                for (int i = 0; i < hist.Length; i++)
                    hist[i] = hist[i] / 2 + 1;

                maxCount = 0; maxIdx = -1;
                for (int k = 0; k < hist.Length; k++)
                    if (hist[k] > maxCount) { maxCount = hist[k]; maxIdx = k; }

                if (maxIdx < 0) return MediaColor.FromRgb(31, 35, 45);
            }

            int rr = ((maxIdx >> 8) & 0xF);
            int gg = ((maxIdx >> 4) & 0xF);
            int bb = (maxIdx & 0xF);
            byte R = (byte)(rr * 16 + 8);
            byte G = (byte)(gg * 16 + 8);
            byte B = (byte)(bb * 16 + 8);
            return MediaColor.FromRgb(R, G, B);
        }

        // Helpers perf (int math)
        private static int Lum255(byte r, byte g, byte b)
        {
            // approx 0.2126/0.7152/0.0722 → 54/183/18 (somme ≈ 255)
            return (54 * r + 183 * g + 18 * b) / 255; // 0..255
        }
        private static bool IsLowSat(byte r, byte g, byte b)
        {
            byte max = Math.Max(r, Math.Max(g, b));
            byte min = Math.Min(r, Math.Min(g, b));
            // sat < 0.18  ↔  (max-min)/max < 0.18 → (max-min)*100 < 18*max
            return (max - min) * 100 < 18 * Math.Max(1, (int)max);
        }
        private static bool SkinHueLikely(byte r, byte g, byte b)
        {
            byte max = Math.Max(r, Math.Max(g, b));
            byte min = Math.Min(r, Math.Min(g, b));
            int chroma = max - min;
            if (chroma < 20) return false; 
            int lum = Lum255(r, g, b);
            return lum > 64 && lum < 217;
        }


        // ---------- Palette ----------
        private sealed class Palette
        {
            public MediaColor Accent, Glow, Highlight, Secondary;
            public MediaColor Text, TextSecondary, TextDetail;

            public MediaColor OverlayTop, OverlayMid, OverlayBot;
            public MediaColor ButtonPlayMid, ButtonPlayEnd;
            public MediaColor FocusStart, FocusMid, FocusEnd;
            public MediaColor MenuBorderStart, MenuBorderEnd;
            public MediaColor NoFocusStart, NoFocusEnd;

            public MediaColor ShadeMidColor, ShadeEndColor;
            public MediaColor ControlBackgroundColor, SuccessStartColor;
            public MediaColor GlowMidColor, GlowEndColor;
        }


        private static Palette BuildPalette(MediaColor accent)
        {
            // 0) Tame aggressive colors 
            var safeAccent = MakeAccentReadable(accent);

            // 1) Accent-based overlays 
            var overlayTop = safeAccent;
            var overlayMid = Darken(safeAccent, .45);
            var overlayBot = Darken(safeAccent, .75);

            // 2) WHITE TEXT → fairly dark background 
            var textMain = Colors.White;
            var textSecondary = Mix(textMain, overlayMid, .25); 
            var textDetail = Mix(textMain, overlayMid, .45); 

            bool light = Luminance(safeAccent) > 0.65; 

            return new Palette
            {
                Accent = safeAccent,
                Glow = Lighten(safeAccent, .35),
                Highlight = Lighten(safeAccent, .25),
                Secondary = Mix(safeAccent, Colors.White, .50),

                Text = textMain,
                TextSecondary = textSecondary,
                TextDetail = textDetail,

                OverlayTop = overlayTop,
                OverlayMid = overlayMid,
                OverlayBot = overlayBot,

                ButtonPlayMid = Darken(safeAccent, .70),
                ButtonPlayEnd = Darken(safeAccent, .80),

                FocusStart = light ? Colors.Black : Colors.White,
                FocusMid = Lighten(safeAccent, .20),
                FocusEnd = safeAccent,

                MenuBorderStart = Lighten(safeAccent, .20),
                MenuBorderEnd = safeAccent,

                NoFocusStart = Darken(safeAccent, .70),
                NoFocusEnd = Darken(safeAccent, .50),

                ControlBackgroundColor = Darken(overlayBot, .10),
                SuccessStartColor = Darken(overlayMid, .10),

                GlowMidColor = Darken(overlayMid, .25),
                GlowEndColor = Darken(overlayBot, .45),

                ShadeMidColor = Darken(safeAccent, 0.55),
                ShadeEndColor = Darken(safeAccent, 0.80),
            };
        }



        // ---------- Animation ----------
        private static void StartAnimatedTransition(Palette target)
        {
            if (!IsDynamicAutoActive()) return;

            animCts?.Cancel();
            animCts?.Dispose();
            animCts = new CancellationTokenSource();
            var ct = animCts.Token;

            // Skip animation if colors are nearly identical
            var current = SnapshotCurrentPalette(target);
            if (IsClose(current.Accent, target.Accent) &&
                IsClose(current.OverlayMid, target.OverlayMid) &&
                IsClose(current.MenuBorderEnd, target.MenuBorderEnd))
            {
                ApplyPalette_NoShade(target); // apply once, no animation
                return;
            }

            // Snapshot of current colors.
            var from = SnapshotCurrentPalette(target);

            _ = Task.Run(async () =>
            {
                int steps = Math.Max(2, TransitionSteps);
                int stepDelay = Math.Max(10, TransitionMs / steps);

                for (int i = 1; i <= steps; i++)
                {
                    if (ct.IsCancellationRequested || !IsDynamicAutoActive()) return;
                    double t = (double)i / steps;
                    t = t * t * (3 - 2 * t); // ease-in-out (smoothstep)


                    var frame = LerpPalette(from, target, t);

                    // applies frame only if not cancelled, at rendering time
                    Application.Current?.Dispatcher?.InvokeAsync(
                        () =>
                        {
                            if (!ct.IsCancellationRequested && IsDynamicAutoActive())
                                ApplyPalette_NoShade(frame);
                        },
                        DispatcherPriority.Render
                    );

                    try { await Task.Delay(stepDelay, ct); } catch { return; }
                }
            }, ct);
        }


        private static Palette SnapshotCurrentPalette(Palette fallback)
        {
            // retrieves from Resources if present, otherwise takes fallback
            MediaColor get(string key, MediaColor fb)
            {
                var dict = Application.Current?.Resources;
                if (dict != null && dict.Contains(key))
                {
                    var v = dict[key];
                    if (v is MediaColor c) return c;
                    if (v is SolidColorBrush sb) return sb.Color;
                }
                return fb;
            }

            return new Palette
            {
                Accent = get("DynamicGlowBackgroundPrimary", fallback.Accent),
                Glow = get("GlowFocusColor", fallback.Glow),
                Highlight = get("TextHighlight", fallback.Highlight),
                Secondary = get("HltbAlt", fallback.Secondary),

                Text = get("TextColor", fallback.Text),
                TextSecondary = get("TextSecondaryColor", fallback.TextSecondary),
                TextDetail = get("TextDetail", fallback.TextDetail),

                OverlayTop = get("OverlayStart", fallback.OverlayTop),
                OverlayMid = get("OverlayMid", fallback.OverlayMid),
                OverlayBot = get("OverlayEnd", fallback.OverlayBot),

                ButtonPlayMid = get("ButtonPlayMid", fallback.ButtonPlayMid),
                ButtonPlayEnd = get("ButtonPlayEnd", fallback.ButtonPlayEnd),

                FocusStart = get("FocusStart", fallback.FocusStart),
                FocusMid = get("FocusMid", fallback.FocusMid),
                FocusEnd = get("FocusEnd", fallback.FocusEnd),

                MenuBorderStart = get("MenuBorderStart", fallback.MenuBorderStart),
                MenuBorderEnd = get("MenuBorderEnd", fallback.MenuBorderEnd),

                NoFocusStart = get("NoFocusStart", fallback.NoFocusStart),
                NoFocusEnd = get("NoFocusEnd", fallback.NoFocusEnd),

                ShadeMidColor = get("ShadeMidColor", fallback.ShadeMidColor),
                ShadeEndColor = get("ShadeEndColor", fallback.ShadeEndColor),

                ControlBackgroundColor = get("ControlBackgroundColor", fallback.ControlBackgroundColor),
                SuccessStartColor = get("SuccessStartColor", fallback.SuccessStartColor),

                GlowMidColor = get("GlowMidColor", fallback.GlowMidColor),
                GlowEndColor = get("GlowEndColor", fallback.GlowEndColor),

            };
        }

        private static Palette LerpPalette(Palette a, Palette b, double t)
        {
            MediaColor Lerp(MediaColor x, MediaColor y, double k) => MediaColor.FromRgb(
                (byte)(x.R + (y.R - x.R) * k),
                (byte)(x.G + (y.G - x.G) * k),
                (byte)(x.B + (y.B - x.B) * k));

            return new Palette
            {
                Accent = Lerp(a.Accent, b.Accent, t),
                Glow = Lerp(a.Glow, b.Glow, t),
                Highlight = Lerp(a.Highlight, b.Highlight, t),
                Secondary = Lerp(a.Secondary, b.Secondary, t),

                Text = Lerp(a.Text, b.Text, t),
                TextSecondary = Lerp(a.TextSecondary, b.TextSecondary, t),
                TextDetail = Lerp(a.TextDetail, b.TextDetail, t),

                OverlayTop = Lerp(a.OverlayTop, b.OverlayTop, t),
                OverlayMid = Lerp(a.OverlayMid, b.OverlayMid, t),
                OverlayBot = Lerp(a.OverlayBot, b.OverlayBot, t),

                ButtonPlayMid = Lerp(a.ButtonPlayMid, b.ButtonPlayMid, t),
                ButtonPlayEnd = Lerp(a.ButtonPlayEnd, b.ButtonPlayEnd, t),

                FocusStart = Lerp(a.FocusStart, b.FocusStart, t),
                FocusMid = Lerp(a.FocusMid, b.FocusMid, t),
                FocusEnd = Lerp(a.FocusEnd, b.FocusEnd, t),

                MenuBorderStart = Lerp(a.MenuBorderStart, b.MenuBorderStart, t),
                MenuBorderEnd = Lerp(a.MenuBorderEnd, b.MenuBorderEnd, t),

                NoFocusStart = Lerp(a.NoFocusStart, b.NoFocusStart, t),
                NoFocusEnd = Lerp(a.NoFocusEnd, b.NoFocusEnd, t),

                ShadeMidColor = Lerp(a.ShadeMidColor, b.ShadeMidColor, t),
                ShadeEndColor = Lerp(a.ShadeEndColor, b.ShadeEndColor, t),

                ControlBackgroundColor = Lerp(a.ControlBackgroundColor, b.ControlBackgroundColor, t),
                SuccessStartColor = Lerp(a.SuccessStartColor, b.SuccessStartColor, t),

                GlowMidColor = Lerp(a.GlowMidColor, b.GlowMidColor, t),
                GlowEndColor = Lerp(a.GlowEndColor, b.GlowEndColor, t),

            };
        }

        // ---------- Application ----------
        private static void ApplyPalette_NoShade(Palette p)
        {
            if (!IsDynamicAutoActive()) return;

            SetColor("GlyphColor", p.Accent);
            SetColor("GlowFocusColor", p.Glow);

            // (DELETED) :
            // SetColor("TextColor", p.Text);
            // SetColor("TextSecondaryColor", p.TextSecondary);
            // SetColor("TextDetail", p.TextDetail);
            // SetColor("TextAltDetail", p.TextSecondary);

            // The rest continues to apply as normal
            SetColor("TextHighlight", p.Highlight);
            SetColor("HltbAlt", p.Secondary);
            SetColor("DynamicGlowBackgroundPrimary", p.Accent);

            SetColor("OverlayStart", p.OverlayTop);
            SetColor("OverlayMid", p.OverlayMid);
            SetColor("OverlayEnd", p.OverlayBot);

            SetColor("ButtonPlayMid", p.ButtonPlayMid);
            SetColor("ButtonPlayEnd", p.ButtonPlayEnd);

            SetColor("FocusStart", p.FocusStart);
            SetColor("FocusMid", p.FocusMid);
            SetColor("FocusEnd", p.FocusEnd);

            SetColor("MenuBorderStart", p.MenuBorderStart);
            SetColor("MenuBorderEnd", p.MenuBorderEnd);

            SetColor("NoFocusStart", p.NoFocusStart);
            SetColor("NoFocusEnd", p.NoFocusEnd);

            SetColor("ControlBackgroundColor", p.ControlBackgroundColor);
            SetColor("SuccessStartColor", p.SuccessStartColor);

            SetColor("GlowMidColor", p.GlowMidColor);
            SetColor("GlowEndColor", p.GlowEndColor);

            // Brushes (unchanged)
            UpdateOrSetLinearBrushV("OverlayMenu", p.OverlayTop, p.OverlayMid, p.OverlayBot);
            UpdateOrSetLinearBrushV("ButtonPlayColor", p.Accent, p.ButtonPlayMid, p.ButtonPlayEnd);
            UpdateOrSetLinearBrushDiag("FocusGameBorderBrush", p.FocusStart, p.FocusMid, p.FocusEnd);
            UpdateOrSetLinearBrushH("MenuBorderBrush", p.MenuBorderStart, p.MenuBorderEnd);
            UpdateOrSetLinearBrushDiag("NoFocusBorderButtonBrush", p.NoFocusStart, p.NoFocusEnd);
            UpdateOrSetLinearBrushH("SuccessMenu", p.SuccessStartColor, p.ControlBackgroundColor);

            // === Dynamic Shade: updates colors ===
            var acc = p.ShadeMidColor;
            var shadeMid = Color.FromArgb(0x99, acc.R, acc.G, acc.B); // 60%
            var shadeEnd = Color.FromArgb(0xFF, acc.R, acc.G, acc.B); // 100%

            Application.Current.Resources["ShadeMidColor"] = shadeMid;
            Application.Current.Resources["ShadeEndColor"] = shadeEnd;

            // === Forced brush refresh ===
            var dict = Application.Current.Resources;
            if (dict["ShadeBackground"] is LinearGradientBrush lb)
            {
                // clone to make sure it's not Freezable
                var clone = lb.Clone();

                // replaces only stops 0.00 / 0.20 / 0.45 / 1.00
                foreach (var gs in clone.GradientStops)
                {
                    // tolerance on offsets to avoid approximate floats
                    double o = gs.Offset;
                    if (Math.Abs(o - 0.00) < 0.001) gs.Color = Color.FromArgb(0x00, 0x00, 0x00, 0x00); // transparent
                    else if (Math.Abs(o - 0.20) < 0.001) gs.Color = shadeMid;                            // 40% accent
                    else if (Math.Abs(o - 0.45) < 0.001) gs.Color = shadeEnd;                            // 80% accent
                    else if (Math.Abs(o - 1.00) < 0.001) gs.Color = Color.FromArgb(0xFF, 0, 0, 0);       // noir
                }

                dict["ShadeBackground"] = clone; // replaces the instance in the dictionary
            }



            var radial = new RadialGradientBrush(
                new GradientStopCollection {
            new GradientStop(p.Accent,      0.00),
            new GradientStop(p.GlowMidColor,0.70),
            new GradientStop(p.GlowEndColor,1.00)
                })
            { Center = new Point(.5, .4), GradientOrigin = new Point(.5, .4), RadiusX = .8, RadiusY = .9 };
            SetBrush("DynamicGlowBackgroundSuccess", radial);
        }


        // ---------- Helpers ----------
        private static void SetColor(string key, MediaColor c)
        {
            if (Application.Current?.Resources == null) return;
            Application.Current.Resources[key] = c;
        }
        private static void SetBrush(string key, MediaBrush b)
        {
            if (Application.Current?.Resources == null) return;
            Application.Current.Resources[key] = b;
        }

        // Update the Color resource AND update the same instance of mirror brushes if possible.
        // If the brush is freezable, we clone it, modify the color and replace the input.
        private static void SetColorAndMirrorBrushes(string colorKey, MediaColor c, params string[] mirrorBrushKeys)
        {
            SetColor(colorKey, c);
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            foreach (var bk in mirrorBrushKeys)
            {
                var existing = dict.Contains(bk) ? dict[bk] : null;

                if (existing is SolidColorBrush sb)
                {
                    if (sb.IsFrozen)
                    {
                        var clone = sb.Clone();
                        clone.Color = c;
                        dict[bk] = clone; // replace with a modifiable clone
                    }
                    else
                    {
                        sb.Color = c;     // modify the existing INSTANCE ⇒ StaticResource follows
                    }
                }
                else
                {
                    // If not a brush or absent, create brush
                    dict[bk] = new SolidColorBrush(c);
                }
            }
        }

        private static LinearGradientBrush MakeLinearV(params MediaColor[] stops)
        {
            var gs = new GradientStopCollection();
            for (int i = 0; i < stops.Length; i++)
                gs.Add(new GradientStop(stops[i], (double)i / (stops.Length - 1)));
            return new LinearGradientBrush(gs, new Point(0, 0), new Point(0, 1));
        }
        private static LinearGradientBrush MakeLinearH(params MediaColor[] stops)
        {
            var gs = new GradientStopCollection();
            for (int i = 0; i < stops.Length; i++)
                gs.Add(new GradientStop(stops[i], (double)i / (stops.Length - 1)));
            return new LinearGradientBrush(gs, new Point(0, 0), new Point(1, 0));
        }
        private static LinearGradientBrush MakeLinearDiag(params MediaColor[] stops)
        {
            var gs = new GradientStopCollection();
            for (int i = 0; i < stops.Length; i++)
                gs.Add(new GradientStop(stops[i], (double)i / (stops.Length - 1)));
            return new LinearGradientBrush(gs, new Point(0, 0), new Point(1, 1));
        }

        // --- In-place updates for linear gradient brushes (reduce allocations/GC) ---
        private static void UpdateOrSetLinearBrushV(string key, params MediaColor[] stops)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            var existing = dict.Contains(key) ? dict[key] : null;
            if (existing is LinearGradientBrush lb && !lb.IsFrozen && lb.GradientStops.Count == stops.Length)
            {
                for (int i = 0; i < stops.Length; i++)
                    if (lb.GradientStops[i].Color != stops[i])
                        lb.GradientStops[i].Color = stops[i];

                lb.StartPoint = new Point(0, 0);
                lb.EndPoint = new Point(0, 1);
            }
            else
            {
                dict[key] = MakeLinearV(stops);
            }
        }

        private static void UpdateOrSetLinearBrushH(string key, params MediaColor[] stops)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            var existing = dict.Contains(key) ? dict[key] : null;
            if (existing is LinearGradientBrush lb && !lb.IsFrozen && lb.GradientStops.Count == stops.Length)
            {
                for (int i = 0; i < stops.Length; i++)
                    if (lb.GradientStops[i].Color != stops[i])
                        lb.GradientStops[i].Color = stops[i];

                lb.StartPoint = new Point(0, 0);
                lb.EndPoint = new Point(1, 0);
            }
            else
            {
                dict[key] = MakeLinearH(stops);
            }
        }

        private static void UpdateOrSetLinearBrushDiag(string key, params MediaColor[] stops)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            var existing = dict.Contains(key) ? dict[key] : null;
            if (existing is LinearGradientBrush lb && !lb.IsFrozen && lb.GradientStops.Count == stops.Length)
            {
                for (int i = 0; i < stops.Length; i++)
                    if (lb.GradientStops[i].Color != stops[i])
                        lb.GradientStops[i].Color = stops[i];

                lb.StartPoint = new Point(0, 0);
                lb.EndPoint = new Point(1, 1);
            }
            else
            {
                dict[key] = MakeLinearDiag(stops);
            }
        }

        // ======== Auto-contraste texte ========
        private static double SrgbToLinear(byte c)
        {
            double cs = c / 255.0;
            return cs <= 0.04045 ? cs / 12.92 : Math.Pow((cs + 0.055) / 1.055, 2.4);
        }
        private static byte LinearToSrgb(double v)
        {
            v = (v < 0.0) ? 0.0 : (v > 1.0 ? 1.0 : v);
            double s = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
            return (byte)Math.Round(s * 255.0);
        }
        private static double RelativeLuminance(MediaColor c)
        {
            double r = SrgbToLinear(c.R);
            double g = SrgbToLinear(c.G);
            double b = SrgbToLinear(c.B);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }
        private static double ContrastRatioFromLums(double L1, double L2)
        {
            double lighter = Math.Max(L1, L2);
            double darker = Math.Min(L1, L2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        /// Calculates a GRAY text color 
        private static MediaColor AutoTextOn(MediaColor bg, double targetContrast = 4.5)
        {
            double Lbg = RelativeLuminance(bg);

            double cBlack = ContrastRatioFromLums(SrgbToLinear(0), Lbg);
            double cWhite = ContrastRatioFromLums(SrgbToLinear(255), Lbg);
            if (cBlack >= targetContrast || cWhite >= targetContrast)
            {
                return (cBlack >= cWhite) ? Colors.Black : Colors.White;
            }

            bool bgIsLight = Lbg >= 0.5;
            double lo = bgIsLight ? 0.0 : Lbg;
            double hi = bgIsLight ? Lbg : 1.0;
            MediaColor best = bgIsLight ? Colors.Black : Colors.White;
            double bestC = Math.Max(cBlack, cWhite);

            for (int i = 0; i < 18; i++)
            {
                double mid = (lo + hi) * 0.5;
                double c = ContrastRatioFromLums(mid, Lbg);
                if (c > bestC)
                {
                    bestC = c;
                    byte s = LinearToSrgb(mid);
                    best = MediaColor.FromRgb(s, s, s);
                }

                if (bgIsLight)
                {
                    if (ContrastRatioFromLums((lo + mid) * 0.5, Lbg) > c) hi = mid; else lo = mid;
                }
                else
                {
                    
                    if (ContrastRatioFromLums((hi + mid) * 0.5, Lbg) > c) lo = mid; else hi = mid;
                }
            }

            return best;
        }

        // ======== Taming/safe accent ========
        private static MediaColor GrayOf(MediaColor c)
        {
            byte y = (byte)Math.Round(0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B);
            return MediaColor.FromRgb(y, y, y);
        }

        /// Desaturates the color by blending it into a gray of the same luminance.
        /// f : 0 = unchanged, 1 = fully desaturated.
        private static MediaColor Desaturate(MediaColor c, double f)
        {
            f = (f < 0) ? 0 : (f > 1 ? 1 : f);
            var g = GrayOf(c);
            return Mix(c, g, f);
        }


        /// Reduces saturation and luminance of "garish" colors.
        private static MediaColor MakeAccentReadable(MediaColor accent)
        {
            // 1) A little desaturation 
            var c1 = Desaturate(accent, 0.20);

            // 2) If too light, darken 
            double L = Luminance(c1);                 
            const double Lmax = 0.75;    // acceptable ceiling for a legible background in white text
            if (L > Lmax)
            {
                
                double f = (L - Lmax) + 0.10;         
                f = (f < 0) ? 0 : (f > 0.85 ? 0.85 : f);
                c1 = Darken(c1, f);
            }

            return c1;
        }



        private static MediaColor Darken(MediaColor c, double f) => MediaColor.FromRgb(
            (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
        private static MediaColor Lighten(MediaColor c, double f) => MediaColor.FromRgb(
            (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
        private static MediaColor Mix(MediaColor a, MediaColor b, double t) => MediaColor.FromRgb(
            (byte)(a.R * (1 - t) + b.R * t), (byte)(a.G * (1 - t) + b.G * t), (byte)(a.B * (1 - t) + b.B * t));
        private static double Luminance(MediaColor c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
    }
}
