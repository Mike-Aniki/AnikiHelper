// Dynamic color theme driver + animated transitions.
// - Touches only: Glyph/Text/HLTB/Highlight/Glow colors, OverlayMenu, ButtonPlay,
//   Focus/NoFocus/Menu borders, and a few auxiliary color keys.
// - Color source: game Background first, then Cover as fallback.

using Playnite.SDK;
using Playnite.SDK.Models;
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
        private const int TransitionMs = 250;        // total fade time
        private const int TransitionSteps = 12;      // + more steps = smoother

        private static bool lastActive = false;
        private static int tickGate = 0; // 0 = libre, 1 = in progress (re-entry barrier)

        // === Cache palettes en RAM pour éviter les recalculs inutiles ===
        private const int CacheMax = 1500; // sécurité RAM
        private static readonly Dictionary<string, Palette> paletteCache = new Dictionary<string, Palette>(4096);

        private static string MakeCacheKey(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var fi = new FileInfo(path);
                return $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return path ?? "";
            }
        }

        private static void PutCache(string key, Palette pal)
        {
            if (string.IsNullOrEmpty(key) || pal is null) return;
            if (paletteCache.Count > CacheMax) paletteCache.Clear();
            paletteCache[key] = pal;
        }

        // --- Cache persistant (JSON) des accents (hex) ---
        private static string cacheFilePath;
        private static readonly Dictionary<string, string> accentCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static DispatcherTimer saveCacheTimer;

        private static void LoadAccentCache()
        {
            try
            {
                if (File.Exists(cacheFilePath))
                {
                    var json = File.ReadAllText(cacheFilePath);
                    var data = Playnite.SDK.Data.Serialization.FromJson<Dictionary<string, string>>(json);
                    if (data != null)
                    {
                        accentCache.Clear();
                        foreach (var kv in data) accentCache[kv.Key] = kv.Value;
                    }
                }
            }
            catch { }
        }

        // Cleans cache entries pointing to deleted files
        private static void CleanupAccentCache()
        {
            try
            {
                var toRemove = accentCache.Keys.Where(k =>
                {
                    try
                    {
                        var path = k.Split('|')[0];
                        return !File.Exists(path);
                    }
                    catch { return false; }
                }).ToList();

                foreach (var key in toRemove)
                    accentCache.Remove(key);

                if (toRemove.Count > 0)
                    SaveAccentCacheNow(null, EventArgs.Empty);
            }
            catch { }
        }

        // Limits persistent cache to X entries (avoids huge JSON)
        private static void TrimAccentCacheByTicks(int maxEntries = 3000)
        {
            try
            {
                if (accentCache.Count <= maxEntries)
                    return;

                // 1) Construire une liste (clé, ticks) sans tuples
                var list = new List<KeyValuePair<string, long>>(accentCache.Count);
                foreach (var k in accentCache.Keys)
                {
                    long ticks = 0;
                    try
                    {
                        var parts = k.Split('|');
                        if (parts.Length >= 3)
                        {
                            long.TryParse(parts[2], out ticks);
                        }
                    }
                    catch { /* ignore */ }

                    list.Add(new KeyValuePair<string, long>(k, ticks));
                }

                // 2) Trier par ticks décroissant et ne garder que maxEntries
                list.Sort((a, b) => b.Value.CompareTo(a.Value)); // plus récents d'abord
                if (list.Count > maxEntries)
                    list.RemoveRange(maxEntries, list.Count - maxEntries);

                // 3) Construire l'ensemble des clés à conserver
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in list)
                    keep.Add(kv.Key);

                // 4) Supprimer le reste
                var toRemove = new List<string>();
                foreach (var k in accentCache.Keys)
                    if (!keep.Contains(k))
                        toRemove.Add(k);

                foreach (var k in toRemove)
                    accentCache.Remove(k);

                if (toRemove.Count > 0)
                    SaveAccentCacheNow(null, EventArgs.Empty);
            }
            catch { }
        }


        private static void ArmSaveAccentCache()
        {
            try
            {
                if (saveCacheTimer == null)
                {
                    saveCacheTimer = new DispatcherTimer();
                    saveCacheTimer.Interval = TimeSpan.FromSeconds(8);
                }

                // évite les abonnements multiples
                saveCacheTimer.Tick -= SaveAccentCacheNow;
                saveCacheTimer.Tick += SaveAccentCacheNow;

                saveCacheTimer.Stop();  // on repart propre
                saveCacheTimer.Start();
            }
            catch { }
        }

        private static void SaveAccentCacheNow(object sender, EventArgs e)
        {
            try
            {
                if (saveCacheTimer != null)
                    saveCacheTimer.Stop();

                var tmp = cacheFilePath + ".tmp";
                var json = Playnite.SDK.Data.Serialization.ToJson(accentCache, false);
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath));
                File.WriteAllText(tmp, json);
                if (File.Exists(cacheFilePath)) File.Delete(cacheFilePath);
                File.Move(tmp, cacheFilePath);
            }
            catch { }
        }


        // --- Extensions autorisées pour éviter les probes lents sur GUID.GUID ---
        private static readonly HashSet<string> AllowedExt =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".webp"
        };

        private static bool HasUsableImageExt(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && AllowedExt.Contains(ext);
        }

        // ONLY these keys
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
            "ShadeMidColor","ShadeEndColor"
        };

        private static readonly string[] BrushesToTouch = new[]
        {
            "OverlayMenu","ButtonPlayColor","FocusGameBorderBrush","MenuBorderBrush",
            "NoFocusBorderButtonBrush","SuccessMenu","DynamicGlowBackgroundSuccess","ShadeBackground"
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

            HookWindowFocus();

            // Nettoyage quand Playnite se ferme
            Application.Current.Exit += (_, __) =>
            {
                try
                {
                    if (timer != null) timer.Stop();
                    CancelWork();
                    UnhookWindowFocus();

                    // Flush persistent cache and clean up deferred timer
                    SaveAccentCacheNow(null, EventArgs.Empty);
                    if (saveCacheTimer != null)
                    {
                        saveCacheTimer.Stop();
                        saveCacheTimer.Tick -= SaveAccentCacheNow;
                        saveCacheTimer = null;
                    }
                }
                catch { }
            };

            // Safety measure in case Application.Current.Exit does not trigger
            AppDomain.CurrentDomain.ProcessExit += (_, __) =>
            {
                try
                {
                    if (timer != null) timer.Stop();
                    CancelWork();
                    UnhookWindowFocus();

                    SaveAccentCacheNow(null, EventArgs.Empty);
                    if (saveCacheTimer != null)
                    {
                        saveCacheTimer.Stop();
                        saveCacheTimer.Tick -= SaveAccentCacheNow;
                        saveCacheTimer = null;
                    }
                }
                catch { }
            };




            // 3.5) Préparer le cache persistant
           const string PluginId = "96a983a3-3f13-4dce-a474-4052b718bb52";

            // On crée le bon dossier dans ExtensionsData/<GUID>/
            var userDataPath = Path.Combine(api.Paths.ExtensionsDataPath, PluginId);
            Directory.CreateDirectory(userDataPath);

            cacheFilePath = Path.Combine(userDataPath, "palette_cache_v1.json");
            log.Info($"[DynColor] Cache path: {cacheFilePath}");

            LoadAccentCache();
            CleanupAccentCache();
            TrimAccentCacheByTicks(3000);


            // --- Selftest : forcer une écriture pour vérifier que le cache peut être créé ---
            accentCache["__selftest__"] = "00FFAA";
            SaveAccentCacheNow(null, EventArgs.Empty);  // crée le fichier à coup sûr
            accentCache.Remove("__selftest__");
            SaveAccentCacheNow(null, EventArgs.Empty);  // le réécrit sans la clé test


            // 4) Start the timer
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            timer.Tick += async (_, __) =>
            {
                // Gate: prevents 2 Ticks in parallel
                if (Interlocked.Exchange(ref tickGate, 1) == 1)
                    return;

                if (!IsAppActive())
                {
                    CancelWork();
                    Volatile.Write(ref tickGate, 0);
                    return;
                }

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
                        try { await Task.Delay(DebounceMs, ct); } catch { return; }
                        if (ct.IsCancellationRequested) return;

                        var current = api.MainView?.SelectedGames?.FirstOrDefault();
                        if (current is null) return;
                        if (lastGameId == current.Id) return;

                        // --- Resolve usable image + build palette HORS UI THREAD ---
                        var target = await Task.Run(() =>
                        {
                            if (!TryGetImageFor(current, out var src, out var used, out var ext, out var key))
                                return (Palette)null;

                            // 1) Cache RAM → instantané
                            if (!string.IsNullOrEmpty(key) && paletteCache.TryGetValue(key, out var cachedPal))
                                return cachedPal;

                            // 2) Cache persistant (accent hex) → palette re-générée sans décoder
                            if (!string.IsNullOrEmpty(key) && accentCache.TryGetValue(key, out var hex) && TryHexToColor(hex, out var accFromCache))
                            {
                                var palFromCache = BuildPalette(accFromCache);
                                PutCache(key, palFromCache); // pour accélérer ensuite
                                return palFromCache;
                            }

                            // 3) Décodage + calcul (fallback)
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
                            var pal = BuildPalette(accent);

                            // 4) Sauvegardes (RAM + persistant)
                            if (!string.IsNullOrEmpty(key))
                            {
                                PutCache(key, pal);
                                var hx = ColorToHex(accent);
                                if (!accentCache.TryGetValue(key, out var old) || !string.Equals(old, hx, StringComparison.OrdinalIgnoreCase))
                                {
                                    accentCache[key] = hx;
                                    log.Debug($"[DynColor] Cache update: key={key} -> #{hx}");
                                    ArmSaveAccentCache();
                                }
                            }
                            else
                            {
                                log.Debug("[DynColor] No cache key (image path null/invalide) -> no persist");
                            }


                            return pal;
                        }, ct);

                        if (ct.IsCancellationRequested || target is null) return;

                        // Palette prête → on fige le jeu courant
                        lastGameId = current.Id;

                        StartAnimatedTransition(target);
                    }
                    catch (Exception ex)
                    {
                        log.Warn(ex, "[AnikiHelper] Failed to build/apply palette");
                    }
                    finally
                    {
                        var oldCts = Interlocked.Exchange(ref debounceCts, null);
                        oldCts?.Dispose();
                    }
                }
                finally
                {
                    Volatile.Write(ref tickGate, 0);
                }
            };

            timer.Start();
        }

        // ======== Window focus/activity management ========

        private static bool IsAppActive()
        {
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                if (w == null) return true; // fail-open
                return w.IsActive && w.WindowState != WindowState.Minimized;
            }
            catch { return true; }
        }

        private static void CancelWork()
        {
            try { if (debounceCts != null) debounceCts.Cancel(); } catch { }
            try { if (animCts != null) animCts.Cancel(); } catch { }
        }

        private static void HookWindowFocus()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;

            w.Activated -= OnWindowActivated;
            w.Deactivated -= OnWindowDeactivated;
            w.StateChanged -= OnWindowStateChanged;

            w.Activated += OnWindowActivated;
            w.Deactivated += OnWindowDeactivated;
            w.StateChanged += OnWindowStateChanged;
        }

        private static void UnhookWindowFocus()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;

            w.Activated -= OnWindowActivated;
            w.Deactivated -= OnWindowDeactivated;
            w.StateChanged -= OnWindowStateChanged;
        }

        private static void OnWindowActivated(object s, EventArgs e)
        {
            if (IsDynamicAutoActive() && timer != null && !timer.IsEnabled)
            {
                
                CancelWork();          // in case something is left behind
                lastGameId = null;     // forces a recalculation
                timer.Start();
            }
        }



        private static void OnWindowDeactivated(object s, EventArgs e)
        {
            // Stops and cancels any tasks in progress when the app loses focus
            if (timer != null) timer.Stop();
            CancelWork();
            lastGameId = null; // avoid skipping the first game on the way back
            Volatile.Write(ref tickGate, 0);
        }

        private static void OnWindowStateChanged(object s, EventArgs e)
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;

            if (w.WindowState == WindowState.Minimized)
            {
                OnWindowDeactivated(s, e);
            }
            else if (w.IsActive)
            {
                OnWindowActivated(s, e);
            }
        }


        // --- Utility methods ---
        private static bool IsVideo(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp4" || ext == ".webm" || ext == ".avi" || ext == ".mkv";
        }

        // Decode helper: try any WIC-supported format (PNG/JPG, WEBP/AVIF if codec present).
        // Returns false if file can't be decoded (this triggers the real fallback).
        private static bool TryLoadBitmap(string path, out BitmapSource bmp)
        {
            bmp = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                using (var fs = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.SequentialScan))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                    var frame = dec.Frames[0];

                    // Skip icônes/miniatures trop petites
                    if (frame.PixelWidth < 256 || frame.PixelHeight < 256)
                        return false;

                    double scale = 96.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    scaled.Freeze();
                    bmp = scaled;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // Picks Background first, then Cover only if bg decode fails (true fallback).
        private static bool TryGetImageFor(Game g, out BitmapSource bmp, out string used, out string ext, out string cacheKey)
        {
            bmp = null; used = null; ext = null; cacheKey = null;

            var bg = api.Database.GetFullFilePath(g.BackgroundImage);
            if (!string.IsNullOrEmpty(bg) && !IsVideo(bg) && HasUsableImageExt(bg) && TryLoadBitmap(bg, out bmp))
            {
                used = "background";
                ext = Path.GetExtension(bg)?.ToLowerInvariant();
                cacheKey = MakeCacheKey(bg);
                return true;
            }

            var cover = api.Database.GetFullFilePath(g.CoverImage);
            if (!string.IsNullOrEmpty(cover) && !IsVideo(cover) && HasUsableImageExt(cover) && TryLoadBitmap(cover, out bmp))
            {
                used = "cover";
                ext = Path.GetExtension(cover)?.ToLowerInvariant();
                cacheKey = MakeCacheKey(cover);
                return true;
            }

            return false;
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
                if (dict.Contains(key)) snapshot[key] = dict[key];

            foreach (var key in BrushesToTouch)
                if (dict.Contains(key)) snapshot[key] = dict[key];
        }

        private static void RestoreSnapshot()
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            foreach (var kv in snapshot) dict[kv.Key] = kv.Value;
            snapshot.Clear();
        }

        // ---------- Dominant color ----------
        private static MediaColor GetDominantVividColor_FromPixels(byte[] pixels, int w, int h, int stride)
        {
            if (pixels == null || w <= 0 || h <= 0)
                return MediaColor.FromRgb(31, 35, 45);

            const int stepX = 2, stepY = 2;

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

                    int lum = Lum255(r, g, b);
                    if (lum < 31) continue;

                    considered++;
                    if (lum > 209) brightCount++;

                    if (IsLowSat(r, g, b)) continue;

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

            bool tooSmallPeak = maxCount < Math.Max(50, considered / 300);
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

        private static int Lum255(byte r, byte g, byte b) => (54 * r + 183 * g + 18 * b) / 255; // 0..255
        private static bool IsLowSat(byte r, byte g, byte b)
        {
            byte max = Math.Max(r, Math.Max(g, b));
            byte min = Math.Min(r, Math.Min(g, b));
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
            var safeAccent = MakeAccentReadable(accent);

            var overlayTop = safeAccent;
            var overlayMid = Darken(safeAccent, .45);
            var overlayBot = Darken(safeAccent, .75);

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
        private static bool IsClose(MediaColor a, MediaColor b, byte tol = 6)
        {
            int dR = a.R - b.R, dG = a.G - b.G, dB = a.B - b.B;
            return (dR * dR + dG * dG + dB * dB) <= (tol * tol * 3);
        }

        private static void StartAnimatedTransition(Palette target)
        {
            if (!IsDynamicAutoActive()) return;

            animCts?.Cancel();
            animCts?.Dispose();
            animCts = new CancellationTokenSource();
            var ct = animCts.Token;

            var current = SnapshotCurrentPalette(target);
            if (IsClose(current.Accent, target.Accent) &&
                IsClose(current.OverlayMid, target.OverlayMid) &&
                IsClose(current.MenuBorderEnd, target.MenuBorderEnd))
            {
                ApplyPalette_NoShade(target);
                return;
            }

            var from = SnapshotCurrentPalette(target);

            _ = Task.Run(async () =>
            {
                int steps = Math.Max(2, TransitionSteps);
                int stepDelay = Math.Max(10, TransitionMs / steps);

                for (int i = 1; i <= steps; i++)
                {
                    if (ct.IsCancellationRequested || !IsDynamicAutoActive()) return;
                    double t = (double)i / steps;
                    t = t * t * (3 - 2 * t); // smoothstep

                    var frame = LerpPalette(from, target, t);

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

        // ---------- Apply ----------
        private static void ApplyPalette_NoShade(Palette p)
        {
            if (!IsDynamicAutoActive()) return;

            SetColor("GlyphColor", p.Accent);
            SetColor("GlowFocusColor", p.Glow);

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

            UpdateOrSetLinearBrushV("OverlayMenu", p.OverlayTop, p.OverlayMid, p.OverlayBot);
            UpdateOrSetLinearBrushV("ButtonPlayColor", p.Accent, p.ButtonPlayMid, p.ButtonPlayEnd);
            UpdateOrSetLinearBrushDiag("FocusGameBorderBrush", p.FocusStart, p.FocusMid, p.FocusEnd);
            UpdateOrSetLinearBrushH("MenuBorderBrush", p.MenuBorderStart, p.MenuBorderEnd);
            UpdateOrSetLinearBrushDiag("NoFocusBorderButtonBrush", p.NoFocusStart, p.NoFocusEnd);
            UpdateOrSetLinearBrushH("SuccessMenu", p.SuccessStartColor, p.ControlBackgroundColor);

            var acc = p.ShadeMidColor;
            var shadeMid = Color.FromArgb(0x99, acc.R, acc.G, acc.B); // 60%
            var shadeEnd = Color.FromArgb(0xFF, acc.R, acc.G, acc.B); // 100%

            SetColor("ShadeMidColor", shadeMid);
            SetColor("ShadeEndColor", shadeEnd);

            var dict = Application.Current.Resources;
            if (dict != null && dict.Contains("ShadeBackground") && dict["ShadeBackground"] is LinearGradientBrush lb)
            {
                var clone = lb.Clone();

                foreach (var gs in clone.GradientStops)
                {
                    double o = gs.Offset;
                    if (Math.Abs(o - 0.00) < 0.001) gs.Color = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                    else if (Math.Abs(o - 0.20) < 0.001) gs.Color = shadeMid;
                    else if (Math.Abs(o - 0.45) < 0.001) gs.Color = shadeEnd;
                    else if (Math.Abs(o - 1.00) < 0.001) gs.Color = Color.FromArgb(0xFF, 0, 0, 0);
                }

                dict["ShadeBackground"] = clone;
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

        // ======== Auto-contrast / color helpers ========
        private static string ColorToHex(MediaColor c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
        private static bool TryHexToColor(string hex, out MediaColor c)
        {
            c = default;
            if (string.IsNullOrEmpty(hex) || hex.Length != 6) return false;
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                c = MediaColor.FromRgb(r, g, b);
                return true;
            }
            catch { return false; }
        }

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

        private static MediaColor GrayOf(MediaColor c)
        {
            byte y = (byte)Math.Round(0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B);
            return MediaColor.FromRgb(y, y, y);
        }
        private static MediaColor Desaturate(MediaColor c, double f)
        {
            f = (f < 0) ? 0 : (f > 1 ? 1 : f);
            var g = GrayOf(c);
            return Mix(c, g, f);
        }
        private static MediaColor MakeAccentReadable(MediaColor accent)
        {
            var c1 = Desaturate(accent, 0.20);
            double L = Luminance(c1);
            const double Lmax = 0.75;
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
