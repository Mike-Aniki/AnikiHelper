using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace AnikiHelper
{
    internal static class DynamicAuto
    {
        private static IPlayniteAPI api;
        private static readonly ILogger log = LogManager.GetLogger();

        private static DispatcherTimer timer;
        private static Guid? lastGameId;
        private static Guid? pendingGameId;

        private static CancellationTokenSource debounceCts;
        private static CancellationTokenSource animCts;

        private const int DebounceMs = 300;          // decrease = more reactive
        private const int TransitionMs = 150;        // total fade time
        private const int TransitionSteps = 4;      // + more steps = smoother

        // Precache optionnel
        private const int PrecacheDelayMs = 20000;   // attendre après le boot
        private const int PrecacheGapMs = 1000;    // 1 image / 600 ms
        private const int PrecacheMax = 100;    // cap par session

        // stopper le precache dès qu'on bouge
        private static long lastUserActivityTicks = 0; 
        private static bool IsIdleForMs(int ms) =>
        (Environment.TickCount - Interlocked.Read(ref lastUserActivityTicks)) >= ms;

        // Helper     
        private static bool IsPrecacheEnabled()
        {
            try
            {
                var dict = Application.Current?.Resources;
                if (dict == null)
                    return false;

                // 1) DynamicAuto must be enabled by the theme
                var dynEnabled = dict["DynamicAutoEnabled"] as bool?;
                if (dynEnabled != true)
                    return false;

                // 2) The user must have checked the checkbox in the settings.
                var plugin = AnikiHelper.Instance;
                if (plugin?.Settings?.DynamicAutoPrecacheUserEnabled != true)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }







        private static bool lastActive = false;
        private static int tickGate = 0; // 0 = libre, 1 = in progress (re-entry barrier)

        // === Cache palettes en RAM pour éviter les recalculs inutiles ===
        private const int CacheMax = 1500; // sécurité RAM
        private static readonly Dictionary<string, Palette> paletteCache = new Dictionary<string, Palette>(4096);
        private static readonly object cacheLock = new object();
        private static readonly object paletteLock = new object();

        private static string MakeCacheKey(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var fi = new FileInfo(path);
                return $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch { return null; }
        }




        private static void PutCache(string key, Palette pal)
        {
            if (string.IsNullOrEmpty(key) || pal is null) return;
            lock (paletteLock)
            {
                if (paletteCache.Count > CacheMax)
                    paletteCache.Clear();
                paletteCache[key] = pal;
            }
        }


        // --- Cache persistant (JSON) de la palette complète ---
        private static string cacheFilePath;
        // Cache disque : palette complète (clé = hash image)
        private static readonly Dictionary<string, PaletteDto> paletteCacheDisk =
            new Dictionary<string, PaletteDto>(StringComparer.OrdinalIgnoreCase);

        private static DispatcherTimer saveCacheTimer;

        private static void LoadAccentCache()
        {
            try
            {
                if (!File.Exists(cacheFilePath)) return;
                var json = File.ReadAllText(cacheFilePath);

                // Format cible : palette complète
                var full = Playnite.SDK.Data.Serialization.FromJson<Dictionary<string, PaletteDto>>(json);
                if (full != null)
                {
                    lock (cacheLock)
                    {
                        paletteCacheDisk.Clear();
                        foreach (var kv in full)
                            paletteCacheDisk[kv.Key] = kv.Value;
                    }
                    return;
                }

                // --- MIGRATION depuis anciens formats ---
                // Ancien v2: Dictionary<string, string[]>  (arr[0] = Accent)
                var v2 = Playnite.SDK.Data.Serialization.FromJson<Dictionary<string, string[]>>(json);
                if (v2 != null)
                {
                    lock (cacheLock)
                    {
                        paletteCacheDisk.Clear();
                        foreach (var kv in v2)
                        {
                            var arr = kv.Value;
                            if (arr == null || arr.Length == 0) continue;
                            var hex = arr[0];
                            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 6) continue;
                            var pal = BuildPalette(FromHex(hex));
                            paletteCacheDisk[kv.Key] = ToDto(pal);
                        }
                    }
                    SaveAccentCacheNow(null, EventArgs.Empty);
                    return;
                }

                // Très ancien v1: Dictionary<string,string> (Accent direct)
                var v1 = Playnite.SDK.Data.Serialization.FromJson<Dictionary<string, string>>(json);
                if (v1 != null)
                {
                    lock (cacheLock)
                    {
                        paletteCacheDisk.Clear();
                        foreach (var kv in v1)
                        {
                            var hex = kv.Value;
                            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 6) continue;
                            var pal = BuildPalette(FromHex(hex));
                            paletteCacheDisk[kv.Key] = ToDto(pal);
                        }
                    }
                    SaveAccentCacheNow(null, EventArgs.Empty);
                }
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
                    saveCacheTimer.Interval = TimeSpan.FromSeconds(45);
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
                string json;
                lock (cacheLock)
                {
                    json = Playnite.SDK.Data.Serialization.ToJson(paletteCacheDisk, true);
                }
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
            "OverlayMenu_Top",
            "OverlayMenu_Mid",
            "OverlayMenu_Bottom",
            "GameListFrameBackground",
            "BackgroundItemEndPoint",

            "MenuBorderPrimaryColor",
            "MenuBorderSecondaryColor",

            "TopBar_Top",
            "TopBar_Bottom",
            "BottomBar_Top",
            "BottomBar_Bottom",

            "ButtonPlay_Top",
            "ButtonPlay_Bottom",

            "GameFocus_Left",
            "GameFocus_Right",

            "SecondaryViewBackground_Center",
            "SecondaryViewBackground_Mid",
            "SecondaryViewBackground_End",

            "SeparatorListGame",
            "SeparatorTopBar_Mid",

            "HubCardBottomBorder",
            "HubBannerBorder",
            "HubAchievementsBorder",
            "HubAchievementsIconBorder",
            "TextHubPercentAchievement",

            "TextHighlightStatView",
            "AccentCardStat",

            "TextHighlightNewsView",

            "SuccessBannerBorder_Right",
            "FocusSuccessCardBorder_Right",

        };

        private static readonly string[] BrushesToTouch = new[]
        {
            "DynamicGlowBackgroundSuccess"
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




            // 3.5) Prepare the persistent cache
            const string PluginId = "96a983a3-3f13-4dce-a474-4052b718bb52";

            // On crée le bon dossier dans ExtensionsData/<GUID>/
            var userDataPath = Path.Combine(api.Paths.ExtensionsDataPath, PluginId);
            Directory.CreateDirectory(userDataPath);

            cacheFilePath = Path.Combine(userDataPath, "palette_cache_v2.json");
            LoadAccentCache();


            // 4) Start the timer
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
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
                    if (pendingGameId == initialGame.Id)
                        return;

                    pendingGameId = initialGame.Id;

                    // === Debounce: swap propre  ===
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

                        var current = initialGame;
                        if (lastGameId == current.Id) return;

                        var selectedNow = api.MainView?.SelectedGames?.FirstOrDefault();
                        if (selectedNow == null || selectedNow.Id != current.Id)
                        {
                            return;
                        }

                        Interlocked.Exchange(ref lastUserActivityTicks, (long)Environment.TickCount);


                        // --- Resolve usable image + build palette HORS UI THREAD ---
                        var target = await Task.Run(() =>
                        {
                            try
                            {
                                string key = null;

                                // 1) Cache sans décoder l'image
                                if (TryGetImageKeyOnly(current, out key) && !string.IsNullOrEmpty(key))
                                {
                                    Palette cachedPal;
                                    lock (paletteLock)
                                    {
                                        paletteCache.TryGetValue(key, out cachedPal);
                                    }

                                    if (cachedPal != null)
                                    {
                                        return cachedPal;
                                    }

                                    PaletteDto dto;
                                    lock (cacheLock)
                                    {
                                        paletteCacheDisk.TryGetValue(key, out dto);
                                    }

                                    if (dto != null)
                                    {
                                        var palFromDisk = FromDto(dto);
                                        PutCache(key, palFromDisk);
                                        return palFromDisk;
                                    }
                                }

                                // 2) Seulement si pas de cache : charger/décoder l'image
                                if (!TryGetImageFor(current, out var src, out var used, out var ext, out key))
                                {
                                    return (Palette)null;
                                }

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

                                if (!string.IsNullOrEmpty(key))
                                {
                                    PutCache(key, pal);
                                    lock (cacheLock)
                                    {
                                        paletteCacheDisk[key] = ToDto(pal);
                                    }
                                    ArmSaveAccentCache();
                                }

                                return pal;
                            }
                            catch
                            {
                                return null;
                            }
                        }, ct);



                        if (ct.IsCancellationRequested || target is null) return;

                        
                        lastGameId = current.Id;

                        StartAnimatedTransition(target);
                    }
                    catch (Exception ex)
                    {
                        log.Warn(ex, "[AnikiHelper] Failed to build/apply palette");
                    }
                    finally
                    {
                        pendingGameId = null;

                        var oldCts = Interlocked.Exchange(ref debounceCts, null);
                        oldCts?.Dispose();
                    }
                }
                finally
                {
                    Volatile.Write(ref tickGate, 0);
                }
            };

            // 4) Start the timer
            timer.Start();
            // Optionnel : precache goutte-à-goutte, ne démarre que si activé via ressource
            _ = Task.Run(async () =>
            {
                await Task.Delay(PrecacheDelayMs);

                if (IsPrecacheEnabled())
                {
                    await RunPrecacheTrickleAsync(skipInitialDelay: true);
                }
            });




        }

        public static void ClearPersistentCache(bool alsoRam = true)
        {
            try
            {
                if (saveCacheTimer != null)
                {
                    saveCacheTimer.Stop();
                    saveCacheTimer.Tick -= SaveAccentCacheNow;
                }

                lock (cacheLock)
                {
                    paletteCacheDisk.Clear();
                }


                if (!string.IsNullOrEmpty(cacheFilePath) && File.Exists(cacheFilePath))
                {
                    try { File.Delete(cacheFilePath); }
                    catch (Exception ex) { log.Warn(ex, "[DynColor] Couldn't delete cache file"); }
                }

                if (alsoRam)
                {
                    lock (paletteLock)
                    {
                        paletteCache.Clear();
                    }
                }

                log.Info("[DynColor] Color cache purged (json + ram). It will rebuild automatically.");
            }
            catch (Exception ex)
            {
                log.Error(ex, "[DynColor] ClearPersistentCache failed.");
            }
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

        private static string SafeFullPath(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath)) return null;
            try { return api.Database.GetFullFilePath(dbPath); }
            catch { return null; }
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

            // SAFE: ne pas appeler GetFullFilePath si la prop est vide/null
            var bgPath = SafeFullPath(g.BackgroundImage);
            if (!string.IsNullOrEmpty(bgPath) && !IsVideo(bgPath) && HasUsableImageExt(bgPath) && TryLoadBitmap(bgPath, out bmp))
            {
                used = "background";
                ext = Path.GetExtension(bgPath)?.ToLowerInvariant();
                cacheKey = MakeCacheKey(bgPath);
                return true;
            }

            var coverPath = SafeFullPath(g.CoverImage);
            if (!string.IsNullOrEmpty(coverPath) && !IsVideo(coverPath) && HasUsableImageExt(coverPath) && TryLoadBitmap(coverPath, out bmp))
            {
                used = "cover";
                ext = Path.GetExtension(coverPath)?.ToLowerInvariant();
                cacheKey = MakeCacheKey(coverPath);
                return true;
            }

            return false;
        }

        // Retourne uniquement la clé de cache, sans décoder l'image
        private static bool TryGetImageKeyOnly(Game g, out string cacheKey)
        {
            cacheKey = null;

            var bgPath = SafeFullPath(g.BackgroundImage);
            if (!string.IsNullOrEmpty(bgPath) && !IsVideo(bgPath) && HasUsableImageExt(bgPath))
            {
                cacheKey = MakeCacheKey(bgPath);
                if (!string.IsNullOrEmpty(cacheKey))
                    return true;
            }

            var coverPath = SafeFullPath(g.CoverImage);
            if (!string.IsNullOrEmpty(coverPath) && !IsVideo(coverPath) && HasUsableImageExt(coverPath))
            {
                cacheKey = MakeCacheKey(coverPath);
                if (!string.IsNullOrEmpty(cacheKey))
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

            double[] hist = new double[4096];
            int considered = 0;
            int brightCount = 0;
            int colorful = 0;

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
                    if (a < 16)
                        continue;

                    int lum = Lum255(r, g, b);
                    if (lum < 31)
                        continue;

                    considered++;
                    if (lum > 209)
                        brightCount++;

                    if (IsLowSat(r, g, b))
                        continue;

                    if (SkinHueLikely(r, g, b))
                    {
                        byte max = Math.Max(r, Math.Max(g, b));
                        byte min = Math.Min(r, Math.Min(g, b));
                        double d = max - min;

                        if (d > 0)
                        {
                            double hue;
                            if (max == r)
                                hue = ((g - b) / d) % 6.0;
                            else if (max == g)
                                hue = ((b - r) / d) + 2.0;
                            else
                                hue = ((r - g) / d) + 4.0;

                            hue *= 60.0;
                            if (hue < 0)
                                hue += 360.0;

                            bool satModerate = ((max - min) * 100) < (45 * Math.Max(1, (int)max));
                            if (hue >= 15 && hue <= 45 && satModerate)
                                continue;
                        }
                    }

                    colorful++;

                    int rq = r >> 4;
                    int gq = g >> 4;
                    int bq = b >> 4;
                    int chromaPixel = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                    double saturationWeight = 1.0 + (chromaPixel / 255.0) * 1.8;
                    double brightnessWeight = lum > 205 ? 0.75 : 1.0;

                    hist[(rq << 8) | (gq << 4) | bq] += saturationWeight * brightnessWeight;
                }
            }

            if (considered == 0)
                return MediaColor.FromRgb(31, 35, 45);

            double peakCount = 0;
            int peakIdx = -1;

            for (int k = 0; k < hist.Length; k++)
            {
                if (hist[k] > peakCount)
                {
                    peakCount = hist[k];
                    peakIdx = k;
                }
            }

            bool tooSmallPeak = peakCount < Math.Max(50.0, considered / 300.0);
            bool veryBrightBg = (brightCount / (double)considered) > 0.55;

            if ((peakIdx < 0 || tooSmallPeak) && veryBrightBg)
            {
                double colorfulRatio = considered > 0 ? (double)colorful / considered : 0.0;

                if (colorfulRatio >= 0.04)
                {
                    int brightBestIdx = -1;
                    double brightBestScore = -1.0;

                    for (int k = 0; k < hist.Length; k++)
                    {
                        double count = hist[k];
                        if (count <= 0)
                            continue;

                        int rr2 = ((k >> 8) & 0xF);
                        int gg2 = ((k >> 4) & 0xF);
                        int bb2 = (k & 0xF);

                        byte R2 = (byte)(rr2 * 16 + 8);
                        byte G2 = (byte)(gg2 * 16 + 8);
                        byte B2 = (byte)(bb2 * 16 + 8);

                        if (SkinHueLikely(R2, G2, B2))
                            continue;

                        int chroma = Math.Max(R2, Math.Max(G2, B2)) - Math.Min(R2, Math.Min(G2, B2));
                        double score = chroma * (1.0 + Math.Log10(1 + count));

                        if (score > brightBestScore)
                        {
                            brightBestScore = score;
                            brightBestIdx = k;
                        }
                    }

                    if (brightBestIdx >= 0)
                    {
                        int r3 = ((brightBestIdx >> 8) & 0xF);
                        int g3 = ((brightBestIdx >> 4) & 0xF);
                        int b3 = (brightBestIdx & 0xF);

                        byte R3 = (byte)(r3 * 16 + 8);
                        byte G3 = (byte)(g3 * 16 + 8);
                        byte B3 = (byte)(b3 * 16 + 8);

                        return MediaColor.FromRgb(R3, G3, B3);
                    }
                }

                return MediaColor.FromRgb(208, 211, 216);
            }

            int finalIdx = -1;
            double finalScore = -1.0;

            for (int k = 0; k < hist.Length; k++)
            {
                double count = hist[k];
                if (count <= 0)
                    continue;

                int rq = ((k >> 8) & 0xF);
                int gq = ((k >> 4) & 0xF);
                int bq = (k & 0xF);

                byte R = (byte)(rq * 16 + 8);
                byte G = (byte)(gq * 16 + 8);
                byte B = (byte)(bq * 16 + 8);

                int chroma = Math.Max(R, Math.Max(G, B)) - Math.Min(R, Math.Min(G, B));
                int lum = Lum255(R, G, B);
                double hue = Hue360(R, G, B);

                double chromaBoost = veryBrightBg ? (1.0 + chroma / 62.0) : (1.0 + chroma / 105.0);
                double vividBonus = chroma > 110 ? 1.15 : (chroma > 80 ? 1.07 : 1.0);

                double lumPenalty = lum > 230 ? 0.70 : (lum > 215 ? 0.86 : 1.0);
                double darkPenalty = lum < 22 ? 0.55 : (lum < 38 ? 0.80 : 1.0);
                double muddyPenalty = chroma < 38 ? 0.78 : 1.0;

                // Évite les verts/jaunes ternes qui donnent du kaki sale
                double olivePenalty = 1.0;
                if (hue >= 50 && hue <= 105 && chroma < 95)
                    olivePenalty = veryBrightBg ? 0.68 : 0.80;

                // Bonus léger pour rouges/magenta/violets (utile pour Persona / fonds à identité forte)
                double dramaticBonus = 1.0;
                if ((hue <= 20 || hue >= 320) && chroma > 55)
                    dramaticBonus = 1.12;

                double score =
                    count *
                    chromaBoost *
                    vividBonus *
                    lumPenalty *
                    darkPenalty *
                    muddyPenalty *
                    olivePenalty *
                    dramaticBonus;

                if (score > finalScore)
                {
                    finalScore = score;
                    finalIdx = k;
                }
            }

            if (finalIdx < 0)
                return MediaColor.FromRgb(31, 35, 45);

            int rr = ((finalIdx >> 8) & 0xF);
            int gg = ((finalIdx >> 4) & 0xF);
            int bb = (finalIdx & 0xF);

            byte finalR = (byte)(rr * 16 + 8);
            byte finalG = (byte)(gg * 16 + 8);
            byte finalB = (byte)(bb * 16 + 8);

            var finalColor = MediaColor.FromRgb(finalR, finalG, finalB);

            double brightRatio = brightCount / (double)considered;

            // Cas fonds très clairs : éviter les couleurs trop ternes/pastel
            if (brightRatio > 0.55)
            {
                finalColor = Saturate(finalColor, 0.25);
            }

            // Cas jaune/vert sale : pousser vers un or plus propre
            double finalHue = Hue360(finalColor);
            int finalChroma = Chroma(finalColor);

            if (finalHue >= 50 && finalHue <= 105 && finalChroma < 95)
            {
                var gold = MediaColor.FromRgb(210, 170, 70);
                finalColor = Mix(finalColor, gold, 0.30);
                finalColor = Saturate(finalColor, 0.20);
            }

            // Recalcule après modification
            finalHue = Hue360(finalColor);
            finalChroma = Chroma(finalColor);

            // Évite les thèmes trop plats sur les fonds très lumineux
            if (brightRatio > 0.55 && finalChroma > 80)
            {
                finalColor = Darken(finalColor, 0.10);
            }

            // Boost léger des couleurs chaudes pour un rendu plus cinématique
            if (finalHue >= 0 && finalHue <= 40 && finalChroma > 60)
            {
                finalColor = Saturate(finalColor, 0.10);
            }

            return finalColor;
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

        private sealed class PaletteDto
        {
            public string Accent { get; set; }
        }

        private static string Hex(MediaColor c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

        private static MediaColor FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 6)
                return MediaColor.FromRgb(0, 0, 0);

            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return MediaColor.FromRgb(r, g, b);
        }

        private static PaletteDto ToDto(Palette p) => new PaletteDto
        {
            Accent = Hex(p.Accent)
        };

        private static Palette FromDto(PaletteDto d) => BuildPalette(FromHex(d.Accent));




        // ---------- Palette ----------
        private sealed class Palette
        {
            public MediaColor Accent;

            public MediaColor OverlayTop;
            public MediaColor OverlayMid;
            public MediaColor OverlayBottom;

            public MediaColor GameListFrameBackground;
            public MediaColor BackgroundItemEndPoint;

            public MediaColor MenuBorderPrimary;
            public MediaColor MenuBorderSecondary;

            public MediaColor TopBarTop;
            public MediaColor TopBarBottom;
            public MediaColor BottomBarTop;
            public MediaColor BottomBarBottom;

            public MediaColor ButtonPlayTop;
            public MediaColor ButtonPlayBottom;

            public MediaColor GameFocusLeft;
            public MediaColor GameFocusRight;

            public MediaColor SecondaryCenter;
            public MediaColor SecondaryMid;
            public MediaColor SecondaryEnd;

            public MediaColor SeparatorAccent;

            public MediaColor HubAccent;
            public MediaColor StatAccent;
            public MediaColor NewsAccent;
            public MediaColor SuccessAccent;

            public MediaColor DynamicGlowPrimary;
        }

        private static Palette BuildPalette(MediaColor accent)
        {
            var a = MakeAccentReadable(accent);

            double hue = Hue360(a);
            bool redFamily = (hue <= 20 || hue >= 330);
            bool purpleFamily = (hue >= 280 && hue <= 329);

            var light = Saturate(Lighten(a, redFamily ? 0.24 : 0.28), redFamily ? 0.18 : 0.12);
            var lighter = Saturate(Lighten(a, redFamily ? 0.36 : 0.42), redFamily ? 0.22 : 0.16);

            if (purpleFamily)
            {
                light = Saturate(Lighten(a, 0.28), 0.18);
                lighter = Saturate(Lighten(a, 0.42), 0.22);
            }

            var deep = Darken(a, 0.32);
            var deeper = Darken(a, 0.50);
            var darkest = Darken(a, 0.70);

            return new Palette
            {
                Accent = a,

                OverlayTop = deep,
                OverlayMid = deeper,
                OverlayBottom = darkest,

                GameListFrameBackground = Darken(a, 0.56),
                BackgroundItemEndPoint = WithAlpha(Darken(a, 0.78), 0x44),

                MenuBorderPrimary = WithAlpha(Colors.White, 0xF0),
                MenuBorderSecondary = WithAlpha(light, 0xB8),

                TopBarTop = Darken(a, 0.16),
                TopBarBottom = Darken(a, 0.54),

                BottomBarTop = Darken(a, 0.28),
                BottomBarBottom = Darken(a, 0.68),

                ButtonPlayTop = Darken(a, 0.06),
                ButtonPlayBottom = Darken(a, 0.34),

                GameFocusLeft = Colors.White,
                GameFocusRight = WithAlpha(lighter, 0xD0),

                SecondaryCenter = Darken(a, 0.30),
                SecondaryMid = Darken(a, 0.48),
                SecondaryEnd = Darken(a, 0.76),

                SeparatorAccent = WithAlpha(light, 0x88),

                HubAccent = WithAlpha(light, 0x92),
                StatAccent = WithAlpha(light, 0xC8),
                NewsAccent = light,
                SuccessAccent = WithAlpha(light, 0xC8),

                DynamicGlowPrimary = Darken(a, 0.20)
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
            

            var from = SnapshotCurrentPalette(target);

            _ = Task.Run(async () =>
            {
                // await Task.Delay(2200).ConfigureAwait(false);
                int steps = Math.Max(1, TransitionSteps);
                int stepDelay = Math.Max(10, TransitionMs / steps);

                for (int i = 1; i <= steps; i++)
                {
                    if (ct.IsCancellationRequested || !IsDynamicAutoActive()) return;
                    double t = 0.05 + 0.95 * (double)i / steps; // démarre un peu plus loin pour éviter le délai visuel
                    t = t * t * (3 - 2 * t); // smoothstep,


                    var frame = LerpPalette(from, target, t);

                    Application.Current?.Dispatcher?.InvokeAsync(
                        () =>
                        {
                            if (!ct.IsCancellationRequested && IsDynamicAutoActive())
                                ApplyPalette_NoShade(frame, includeHeavyBrushes: i == steps);
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
                Accent = fallback.Accent,

                OverlayTop = get("OverlayMenu_Top", fallback.OverlayTop),
                OverlayMid = get("OverlayMenu_Mid", fallback.OverlayMid),
                OverlayBottom = get("OverlayMenu_Bottom", fallback.OverlayBottom),

                GameListFrameBackground = get("GameListFrameBackground", fallback.GameListFrameBackground),
                BackgroundItemEndPoint = get("BackgroundItemEndPoint", fallback.BackgroundItemEndPoint),

                MenuBorderPrimary = get("MenuBorderPrimaryColor", fallback.MenuBorderPrimary),
                MenuBorderSecondary = get("MenuBorderSecondaryColor", fallback.MenuBorderSecondary),

                TopBarTop = get("TopBar_Top", fallback.TopBarTop),
                TopBarBottom = get("TopBar_Bottom", fallback.TopBarBottom),
                BottomBarTop = get("BottomBar_Top", fallback.BottomBarTop),
                BottomBarBottom = get("BottomBar_Bottom", fallback.BottomBarBottom),

                ButtonPlayTop = get("ButtonPlay_Top", fallback.ButtonPlayTop),
                ButtonPlayBottom = get("ButtonPlay_Bottom", fallback.ButtonPlayBottom),

                GameFocusLeft = get("GameFocus_Left", fallback.GameFocusLeft),
                GameFocusRight = get("GameFocus_Right", fallback.GameFocusRight),

                SecondaryCenter = get("SecondaryViewBackground_Center", fallback.SecondaryCenter),
                SecondaryMid = get("SecondaryViewBackground_Mid", fallback.SecondaryMid),
                SecondaryEnd = get("SecondaryViewBackground_End", fallback.SecondaryEnd),

                SeparatorAccent = get("SeparatorListGame", fallback.SeparatorAccent),

                HubAccent = get("HubCardBottomBorder", fallback.HubAccent),
                StatAccent = get("AccentCardStat", fallback.StatAccent),
                NewsAccent = get("TextHighlightNewsView", fallback.NewsAccent),
                SuccessAccent = get("FocusSuccessCardBorder_Right", fallback.SuccessAccent),

                DynamicGlowPrimary = get("DynamicGlowBackgroundPrimary", fallback.DynamicGlowPrimary)
            };
        }

        private static Palette LerpPalette(Palette a, Palette b, double t)
        {
            MediaColor Lerp(MediaColor x, MediaColor y, double k) => MediaColor.FromArgb(
                (byte)(x.A + (y.A - x.A) * k),
                (byte)(x.R + (y.R - x.R) * k),
                (byte)(x.G + (y.G - x.G) * k),
                (byte)(x.B + (y.B - x.B) * k));

            return new Palette
            {
                Accent = Lerp(a.Accent, b.Accent, t),

                OverlayTop = Lerp(a.OverlayTop, b.OverlayTop, t),
                OverlayMid = Lerp(a.OverlayMid, b.OverlayMid, t),
                OverlayBottom = Lerp(a.OverlayBottom, b.OverlayBottom, t),

                GameListFrameBackground = Lerp(a.GameListFrameBackground, b.GameListFrameBackground, t),
                BackgroundItemEndPoint = Lerp(a.BackgroundItemEndPoint, b.BackgroundItemEndPoint, t),

                MenuBorderPrimary = Lerp(a.MenuBorderPrimary, b.MenuBorderPrimary, t),
                MenuBorderSecondary = Lerp(a.MenuBorderSecondary, b.MenuBorderSecondary, t),

                TopBarTop = Lerp(a.TopBarTop, b.TopBarTop, t),
                TopBarBottom = Lerp(a.TopBarBottom, b.TopBarBottom, t),
                BottomBarTop = Lerp(a.BottomBarTop, b.BottomBarTop, t),
                BottomBarBottom = Lerp(a.BottomBarBottom, b.BottomBarBottom, t),

                ButtonPlayTop = Lerp(a.ButtonPlayTop, b.ButtonPlayTop, t),
                ButtonPlayBottom = Lerp(a.ButtonPlayBottom, b.ButtonPlayBottom, t),

                GameFocusLeft = Lerp(a.GameFocusLeft, b.GameFocusLeft, t),
                GameFocusRight = Lerp(a.GameFocusRight, b.GameFocusRight, t),

                SecondaryCenter = Lerp(a.SecondaryCenter, b.SecondaryCenter, t),
                SecondaryMid = Lerp(a.SecondaryMid, b.SecondaryMid, t),
                SecondaryEnd = Lerp(a.SecondaryEnd, b.SecondaryEnd, t),

                SeparatorAccent = Lerp(a.SeparatorAccent, b.SeparatorAccent, t),

                HubAccent = Lerp(a.HubAccent, b.HubAccent, t),
                StatAccent = Lerp(a.StatAccent, b.StatAccent, t),
                NewsAccent = Lerp(a.NewsAccent, b.NewsAccent, t),
                SuccessAccent = Lerp(a.SuccessAccent, b.SuccessAccent, t),

                DynamicGlowPrimary = Lerp(a.DynamicGlowPrimary, b.DynamicGlowPrimary, t)
            };
        }

        // ---------- Apply ----------
        private static void ApplyPalette_NoShade(Palette p, bool includeHeavyBrushes = true)
        {
            if (!IsDynamicAutoActive())
                return;

            // -----------------------------
            // 1) Update COLOR resources
            // -----------------------------
            SetColor("OverlayMenu_Top", p.OverlayTop);
            SetColor("OverlayMenu_Mid", p.OverlayMid);
            SetColor("OverlayMenu_Bottom", p.OverlayBottom);

            SetColor("GameListFrameBackground", p.GameListFrameBackground);
            SetColor("BackgroundItemEndPoint", p.BackgroundItemEndPoint);

            SetColor("MenuBorderPrimaryColor", p.MenuBorderPrimary);
            SetColor("MenuBorderSecondaryColor", p.MenuBorderSecondary);

            SetColor("TopBar_Top", p.TopBarTop);
            SetColor("TopBar_Bottom", p.TopBarBottom);
            SetColor("BottomBar_Top", p.BottomBarTop);
            SetColor("BottomBar_Bottom", p.BottomBarBottom);

            SetColor("ButtonPlay_Top", p.ButtonPlayTop);
            SetColor("ButtonPlay_Bottom", p.ButtonPlayBottom);

            SetColor("GameFocus_Left", p.GameFocusLeft);
            SetColor("GameFocus_Right", p.GameFocusRight);

            SetColor("SecondaryViewBackground_Center", p.SecondaryCenter);
            SetColor("SecondaryViewBackground_Mid", p.SecondaryMid);
            SetColor("SecondaryViewBackground_End", p.SecondaryEnd);

            SetColor("SeparatorListGame", p.SeparatorAccent);
            SetColor("SeparatorTopBar_Mid", p.SeparatorAccent);

            SetColor("HubCardBottomBorder", p.HubAccent);
            SetColor("HubBannerBorder", p.HubAccent);
            SetColor("HubAchievementsBorder", p.HubAccent);
            SetColor("HubAchievementsIconBorder", p.HubAccent);
            SetColor("TextHubPercentAchievement", p.HubAccent);

            SetColor("TextHighlightStatView", p.StatAccent);
            SetColor("AccentCardStat", p.StatAccent);

            SetColor("TextHighlightNewsView", p.NewsAccent);

            SetColor("SuccessBannerBorder_Right", p.SuccessAccent);
            SetColor("FocusSuccessCardBorder_Right", p.SuccessAccent);

            SetColor("DynamicGlowBackgroundPrimary", p.DynamicGlowPrimary);

            // -----------------------------
            // 2) Light BRUSH resources
            // Updated during transition
            // -----------------------------

            UpdateOrSetLinearBrushV("OverlayMenu",
                p.OverlayTop,
                p.OverlayMid,
                p.OverlayBottom);

            UpdateOrSetLinearBrushV("TopBar",
                p.TopBarTop,
                p.TopBarBottom);

            UpdateOrSetLinearBrushV("BottomBar",
                p.BottomBarTop,
                p.BottomBarBottom);

            SetBrush("GameListFrameBackgroundBrush",
                new SolidColorBrush(p.GameListFrameBackground));

            UpdateOrSetLinearBrushDiag("BackgroundItem",
                p.OverlayTop,
                p.OverlayMid,
                p.BackgroundItemEndPoint);

            UpdateOrSetLinearBrushH("MenuBorderBrush",
                p.MenuBorderPrimary,
                p.MenuBorderSecondary);

            UpdateOrSetLinearBrushV2("ButtonPlayColor",
                p.ButtonPlayTop, 0.10,
                p.ButtonPlayBottom, 0.90);

            UpdateOrSetLinearBrushDiag("FocusGameBorderBrush",
                p.GameFocusLeft,
                p.GameFocusRight);

            SetBrush("SeparatorListGameBrush", new SolidColorBrush(p.SeparatorAccent));

            UpdateOrSetSolidBrush("HubCardBottomBorderBrush", p.HubAccent);
            SetBrush("HubBannerBorderBrush", new SolidColorBrush(p.HubAccent));
            SetBrush("HubAchievementsBorderBrush", new SolidColorBrush(p.HubAccent));
            SetBrush("HubAchievementsIconBorderBrush", new SolidColorBrush(p.HubAccent));
            SetBrush("TextHubPercentAchievementBrush", new SolidColorBrush(p.HubAccent));

            SetBrush("TextHighlightStatViewBrush", new SolidColorBrush(p.StatAccent));
            SetBrush("AccentCardStatBrush", new SolidColorBrush(p.StatAccent));

            SetBrush("TextHighlightNewsViewBrush", new SolidColorBrush(p.NewsAccent));

            // -----------------------------
            // 3) Heavy BRUSH resources
            // Only updated at the end of transition
            // -----------------------------
            if (!includeHeavyBrushes)
                return;

            SetBrush("SecondaryViewBackground",
                new RadialGradientBrush(
                    new GradientStopCollection
                    {
                new GradientStop(p.SecondaryCenter, 0.0),
                new GradientStop(p.SecondaryMid, 0.7),
                new GradientStop(p.SecondaryEnd, 1.0)
                    })
                {
                    Center = new Point(.5, .4),
                    GradientOrigin = new Point(.5, .4),
                    RadiusX = .8,
                    RadiusY = .9
                });

            var sepLeft = GetColorResource("SeparatorTopBar_Left", MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            var sepRight = GetColorResource("SeparatorTopBar_Right", MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

            SetBrush("SeparatorTopBarBrush",
                new LinearGradientBrush(
                    new GradientStopCollection
                    {
                new GradientStop(sepLeft, 0.0),
                new GradientStop(p.SeparatorAccent, 0.5),
                new GradientStop(sepRight, 1.0)
                    },
                    new Point(0, 0),
                    new Point(1, 0)));

            var successLeft = GetColorResource("SuccessBannerBorder_Left", MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

            SetBrush("SuccessBannerBorder",
                new LinearGradientBrush(
                    new GradientStopCollection
                    {
                new GradientStop(successLeft, 0.0),
                new GradientStop(p.SuccessAccent, 1.0)
                    },
                    new Point(0, 1),
                    new Point(1, 0)));

            var focusSuccessLeft = GetColorResource("FocusSuccessCardBorder_Left", Colors.White);

            SetBrush("FocusSuccessCardBorder",
                new LinearGradientBrush(
                    new GradientStopCollection
                    {
                new GradientStop(focusSuccessLeft, 0.0),
                new GradientStop(p.SuccessAccent, 1.0)
                    },
                    new Point(0, 1),
                    new Point(1, 0)));

            SetBrush("DynamicGlowBackgroundSuccess",
                new RadialGradientBrush(
                    new GradientStopCollection
                    {
                new GradientStop(p.DynamicGlowPrimary, 0.00),
                new GradientStop(Darken(p.OverlayTop, .20), 0.65),
                new GradientStop(Colors.Black, 1.00)
                    })
                {
                    Center = new Point(.5, .4),
                    GradientOrigin = new Point(.5, .4),
                    RadiusX = .8,
                    RadiusY = .9
                });
        }

        // ---------- Helpers ----------

        private static void UpdateOrSetLinearBrushV2(string key,
    MediaColor c0, double o0,
    MediaColor c1, double o1)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            var existing = dict.Contains(key) ? dict[key] : null;
            if (existing is LinearGradientBrush lb && !lb.IsFrozen && lb.GradientStops.Count == 2)
            {
                lb.StartPoint = new Point(0, 0);
                lb.EndPoint = new Point(0, 1);

                lb.GradientStops[0].Offset = o0;
                lb.GradientStops[0].Color = c0;

                lb.GradientStops[1].Offset = o1;
                lb.GradientStops[1].Color = c1;
            }
            else
            {
                dict[key] = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                new GradientStop(c0, o0),
                new GradientStop(c1, o1)
                    },
                    new Point(0, 0),
                    new Point(0, 1));
            }
        }

        private static void UpdateOrSetSolidBrush(string key, MediaColor color)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            var existing = dict.Contains(key) ? dict[key] : null;
            if (existing is SolidColorBrush sb && !sb.IsFrozen)
            {
                if (sb.Color != color)
                    sb.Color = color;
            }
            else
            {
                dict[key] = new SolidColorBrush(color);
            }
        }

        private static double Hue360(byte r, byte g, byte b)
        {
            byte max = Math.Max(r, Math.Max(g, b));
            byte min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            if (d <= 0.0)
                return 0.0;

            double hue;
            if (max == r)
                hue = ((g - b) / d) % 6.0;
            else if (max == g)
                hue = ((b - r) / d) + 2.0;
            else
                hue = ((r - g) / d) + 4.0;

            hue *= 60.0;
            if (hue < 0)
                hue += 360.0;

            return hue;
        }

        private static double Hue360(MediaColor c) => Hue360(c.R, c.G, c.B);

        private static MediaColor BiasFromMuddyYellowGreen(MediaColor c)
        {
            double hue = Hue360(c);
            int chroma = Chroma(c);
            double lum = Luminance(c);

            // Cas typique Mario / Dispatch : jaune-vert terne -> on pousse vers un or plus propre
            if (hue >= 50 && hue <= 105 && chroma < 95 && lum > 0.35)
            {
                var gold = MediaColor.FromRgb(214, 184, 82);
                var mixed = Mix(c, gold, lum > 0.55 ? 0.30 : 0.18);
                return Saturate(mixed, lum > 0.55 ? 0.32 : 0.18);
            }

            return c;
        }

        private static byte ClampToByte(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)Math.Round(v);
        }

        private static int Chroma(MediaColor c)
        {
            return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
        }

        private static MediaColor Saturate(MediaColor c, double f)
        {
            // f = 0.30 => +30% d'éloignement du gris
            double y = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

            return MediaColor.FromRgb(
                ClampToByte(y + (c.R - y) * (1.0 + f)),
                ClampToByte(y + (c.G - y) * (1.0 + f)),
                ClampToByte(y + (c.B - y) * (1.0 + f))
            );
        }

        private static MediaColor GetColorResource(string key, MediaColor fallback)
        {
            var dict = Application.Current?.Resources;
            if (dict == null)
                return fallback;

            if (!dict.Contains(key))
                return fallback;

            var value = dict[key];
            if (value is MediaColor c)
                return c;

            if (value is SolidColorBrush sb)
                return sb.Color;

            return fallback;
        }

        private static void SetColor(string key, MediaColor c)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            if (dict.Contains(key) && dict[key] is MediaColor existing && existing == c)
                return;

            dict[key] = c;
        }

        private static void SetBrush(string key, MediaBrush b)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            if (dict.Contains(key))
            {
                if (dict[key] is SolidColorBrush oldSolid && b is SolidColorBrush newSolid)
                {
                    if (oldSolid.Color == newSolid.Color)
                        return;
                }
            }

            dict[key] = b;
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
            var c = accent;
            int chroma = Chroma(c);
            double lum = Luminance(c);
            double hue = Hue360(c);

            c = BiasFromMuddyYellowGreen(c);

            chroma = Chroma(c);
            lum = Luminance(c);
            hue = Hue360(c);

            // Si trop terne, on redonne de la vie
            if (chroma < 48)
            {
                if (lum > 0.55)
                    c = Saturate(c, 0.60);
                else if (lum > 0.32)
                    c = Saturate(c, 0.38);
                else
                    c = Saturate(c, 0.20);
            }
            // Rouge / magenta / violet : on garde plus de personnalité
            else if ((hue <= 20 || hue >= 320) && chroma >= 70)
            {
                c = Saturate(c, 0.10);
            }
            // Si trop néon, on calme juste un peu
            else if (chroma > 160 && lum > 0.40)
            {
                c = Desaturate(c, 0.06);
            }

            // Contrôle lumière
            lum = Luminance(c);
            if (lum > 0.80)
                c = Darken(c, 0.30);
            else if (lum > 0.70)
                c = Darken(c, 0.18);

            return c;
        }

        private static MediaColor WithAlpha(MediaColor c, byte a)
        {
            return MediaColor.FromArgb(a, c.R, c.G, c.B);
        }
        private static MediaColor Darken(MediaColor c, double f) => MediaColor.FromRgb(
            (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

        private static MediaColor Lighten(MediaColor c, double f) => MediaColor.FromRgb(
            (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

        private static MediaColor Mix(MediaColor a, MediaColor b, double t) => MediaColor.FromRgb(
            (byte)(a.R * (1 - t) + b.R * t), (byte)(a.G * (1 - t) + b.G * t), (byte)(a.B * (1 - t) + b.B * t));

        private static double Luminance(MediaColor c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private static async Task RunPrecacheTrickleAsync(bool skipInitialDelay = false)
        {
            try
            {
                if (!skipInitialDelay)
                {
                    await Task.Delay(PrecacheDelayMs);
                }

                var games = api.Database.Games?.ToList() ?? new List<Game>();
                int total = games.Count;
                int added = 0, skippedNoImage = 0, skippedAlready = 0;

                foreach (var g in games)
                {
                    if (added >= PrecacheMax)
                        break;

                    // Stop si Playnite n'est plus actif OU si l'option s'est désactivée
                    if (!IsAppActive() || !IsPrecacheEnabled())
                        break;

                    if (!IsIdleForMs(3000))
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    // 1) On récupère uniquement la clé sans décoder
                    if (!TryGetImageKeyOnly(g, out var key) || string.IsNullOrEmpty(key))
                    {
                        skippedNoImage++;
                        continue;
                    }

                    // 2) Cache RAM ?
                    bool inRam;
                    lock (paletteLock) inRam = paletteCache.ContainsKey(key);
                    if (inRam)
                    {
                        skippedAlready++;
                        continue;
                    }

                    // 3) Cache Disque ?
                    bool inDisk;
                    lock (cacheLock) inDisk = paletteCacheDisk.ContainsKey(key);
                    if (inDisk)
                    {
                        skippedAlready++;
                        continue;
                    }

                    // 4) Ici seulement on décode l'image
                    if (!TryGetImageFor(g, out var bmp, out var used, out _, out _))
                    {
                        skippedNoImage++;
                        continue;
                    }

                    var pf = PixelFormats.Bgra32;
                    if (bmp.Format != pf)
                    {
                        bmp = new FormatConvertedBitmap(bmp, pf, null, 0);
                        bmp.Freeze();
                    }

                    int w = bmp.PixelWidth, h = bmp.PixelHeight;
                    int stride = (w * pf.BitsPerPixel + 7) / 8;
                    byte[] pixels = new byte[stride * h];
                    bmp.CopyPixels(pixels, stride, 0);

                    var accent = GetDominantVividColor_FromPixels(pixels, w, h, stride);
                    var pal = BuildPalette(accent);

                    PutCache(key, pal);
                    lock (cacheLock) { paletteCacheDisk[key] = ToDto(pal); }

                    added++;

                    ArmSaveAccentCache();

                    await Task.Delay(PrecacheGapMs);
                }
            }
            catch (Exception ex)
            {
                log.Warn(ex, "[DynColor] Trickle precache failed.");
            }
        }



    }
}
