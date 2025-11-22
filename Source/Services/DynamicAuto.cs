// Dynamic color theme driver + animated transitions.
// - Touches only: Glyph/Text/HLTB/Highlight/Glow colors, OverlayMenu, ButtonPlay,
//   Focus/NoFocus/Menu borders, and a few auxiliary color keys.
// - Color source: game Background first, then Cover as fallback.

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

        private static CancellationTokenSource debounceCts;
        private static CancellationTokenSource animCts;

        private const int DebounceMs = 30;          // decrease = more reactive
        private const int TransitionMs = 120;        // total fade time
        private const int TransitionSteps = 8;      // + more steps = smoother

        // --- Precache optionnel (désactivé par défaut via ressource) ---
        private const int PrecacheDelayMs = 20000;   // attendre après le boot
        private const int PrecacheGapMs = 600;    // 1 image / 600 ms
        private const int PrecacheMax = 100;    // cap par session

        // Activité utilisateur pour stopper le precache dès qu'on bouge
        private static long lastUserActivityTicks = 0; 
        private static bool IsIdleForMs(int ms) =>
        (Environment.TickCount - Interlocked.Read(ref lastUserActivityTicks)) >= ms;

        // Helper: lire un bool ressource pour activer/désactiver le precache        
        private static bool IsPrecacheEnabled()
        {
            try
            {
                var dict = Application.Current?.Resources;
                if (dict == null)
                    return false;

                // 1) DynamicAuto doit être activé par le thème
                var dynEnabled = dict["DynamicAutoEnabled"] as bool?;
                if (dynEnabled != true)
                    return false;

                // 2) Le user doit avoir coché le checkbox dans les settings
                var plugin = AnikiHelper.Instance;
                if (plugin?.Settings?.DynamicAutoPrecacheUserEnabled != true)
                    return false;

                // Tout est OK -> on autorise le pré-cache
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




            // 3.5) Prepare the persistent cache
            const string PluginId = "96a983a3-3f13-4dce-a474-4052b718bb52";

            // On crée le bon dossier dans ExtensionsData/<GUID>/
            var userDataPath = Path.Combine(api.Paths.ExtensionsDataPath, PluginId);
            Directory.CreateDirectory(userDataPath);

            cacheFilePath = Path.Combine(userDataPath, "palette_cache.json");
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

                        var current = api.MainView?.SelectedGames?.FirstOrDefault();
                        if (current is null) return;
                        if (lastGameId == current.Id) return;

                        Interlocked.Exchange(ref lastUserActivityTicks, (long)Environment.TickCount);


                        // --- Resolve usable image + build palette HORS UI THREAD ---
                        var target = await Task.Run(() =>
                        {
                            string source = "NONE";

                            try
                            {
                                if (!TryGetImageFor(current, out var src, out var used, out var ext, out var key))
                                {
                                    source = "NO_IMAGE";
                                    return (Palette)null;
                                }

                                // 1) RAM cache
                                if (!string.IsNullOrEmpty(key))
                                {
                                    Palette cachedPal;
                                    lock (paletteLock)
                                    {
                                        paletteCache.TryGetValue(key, out cachedPal);
                                    }
                                    if (cachedPal != null)
                                    {
                                        source = "RAM";
                                        return cachedPal;
                                    }
                                }

                                // 2) DISK cache 
                                if (!string.IsNullOrEmpty(key))
                                {
                                    PaletteDto dto = null;
                                    lock (cacheLock)
                                    {
                                        paletteCacheDisk.TryGetValue(key, out dto);
                                    }
                                    if (dto != null)
                                    {
                                        var palFromDisk = FromDto(dto);
                                        PutCache(key, palFromDisk);
                                        source = "DISK";
                                        return palFromDisk;
                                    }
                                }

                                // 3) Décodage + calcul
                                source = $"DECODE_{used ?? "unknown"}";

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
            if (IsPrecacheEnabled())
            {
                _ = RunPrecacheTrickleAsync();
            }


          

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

            int[] hist = new int[4096];
            int considered = 0;
            int brightCount = 0;
            int colorful = 0; // NEW

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

                    colorful++;   // <<< AJOUT


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
                // Ratio de pixels "vraiment colorés" dans une image très claire
                double colorfulRatio = considered > 0 ? (double)colorful / considered : 0.0;

                if (colorfulRatio >= 0.04) // ≥4% de pixels colorés → chercher une vraie teinte
                {
                    int bestIdx = -1;
                    double bestScore = -1;

                    for (int k = 0; k < hist.Length; k++)
                    {
                        int count = hist[k];
                        if (count <= 0) continue;

                        int rr2 = ((k >> 8) & 0xF), gg2 = ((k >> 4) & 0xF), bb2 = (k & 0xF);
                        byte R2 = (byte)(rr2 * 16 + 8), G2 = (byte)(gg2 * 16 + 8), B2 = (byte)(bb2 * 16 + 8);

                        // Évite les tons peau sur fonds clairs
                        if (SkinHueLikely(R2, G2, B2))
                            continue;

                        // Score "chroma pondérée" : privilégie la saturation, puis la fréquence
                        int chroma = Math.Max(R2, Math.Max(G2, B2)) - Math.Min(R2, Math.Min(G2, B2));
                        double score = chroma * (1.0 + Math.Log10(1 + count));

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestIdx = k;
                        }
                    }

                    if (bestIdx >= 0)
                    {
                        int r3 = ((bestIdx >> 8) & 0xF);
                        int g3 = ((bestIdx >> 4) & 0xF);
                        int b3 = (bestIdx & 0xF);
                        byte R3 = (byte)(r3 * 16 + 8);
                        byte G3 = (byte)(g3 * 16 + 8);
                        byte B3 = (byte)(b3 * 16 + 8);
                        return MediaColor.FromRgb(R3, G3, B3);
                    }
                    // sinon on tombera sur le fallback ci-dessous
                }

                // Cas "ultra clair ET quasi pas de couleur" (ex. Yakuza monochrome)
                return MediaColor.FromRgb(208, 211, 216); 
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

        // === DTO JSON pour stocker une palette complète ===
        private sealed class PaletteDto
        {
            public string Accent { get; set; }
            public string Glow { get; set; }
            public string Highlight { get; set; }
            public string Secondary { get; set; }

            public string Text { get; set; }
            public string TextSecondary { get; set; }
            public string TextDetail { get; set; }

            public string OverlayTop { get; set; }
            public string OverlayMid { get; set; }
            public string OverlayBot { get; set; }

            public string ButtonPlayMid { get; set; }
            public string ButtonPlayEnd { get; set; }

            public string FocusStart { get; set; }
            public string FocusMid { get; set; }
            public string FocusEnd { get; set; }

            public string MenuBorderStart { get; set; }
            public string MenuBorderEnd { get; set; }

            public string NoFocusStart { get; set; }
            public string NoFocusEnd { get; set; }

            public string ShadeMidColor { get; set; }
            public string ShadeEndColor { get; set; }

            public string ControlBackgroundColor { get; set; }
            public string SuccessStartColor { get; set; }

            public string GlowMidColor { get; set; }
            public string GlowEndColor { get; set; }
        }

        // Convertit MediaColor -> "RRGGBB"
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
            Accent = Hex(p.Accent),
            Glow = Hex(p.Glow),
            Highlight = Hex(p.Highlight),
            Secondary = Hex(p.Secondary),

            Text = Hex(p.Text),
            TextSecondary = Hex(p.TextSecondary),
            TextDetail = Hex(p.TextDetail),

            OverlayTop = Hex(p.OverlayTop),
            OverlayMid = Hex(p.OverlayMid),
            OverlayBot = Hex(p.OverlayBot),

            ButtonPlayMid = Hex(p.ButtonPlayMid),
            ButtonPlayEnd = Hex(p.ButtonPlayEnd),

            FocusStart = Hex(p.FocusStart),
            FocusMid = Hex(p.FocusMid),
            FocusEnd = Hex(p.FocusEnd),

            MenuBorderStart = Hex(p.MenuBorderStart),
            MenuBorderEnd = Hex(p.MenuBorderEnd),

            NoFocusStart = Hex(p.NoFocusStart),
            NoFocusEnd = Hex(p.NoFocusEnd),

            ShadeMidColor = Hex(p.ShadeMidColor),
            ShadeEndColor = Hex(p.ShadeEndColor),

            ControlBackgroundColor = Hex(p.ControlBackgroundColor),
            SuccessStartColor = Hex(p.SuccessStartColor),

            GlowMidColor = Hex(p.GlowMidColor),
            GlowEndColor = Hex(p.GlowEndColor),
        };

        private static Palette FromDto(PaletteDto d) => new Palette
        {
            Accent = FromHex(d.Accent),
            Glow = FromHex(d.Glow),
            Highlight = FromHex(d.Highlight),
            Secondary = FromHex(d.Secondary),

            Text = FromHex(d.Text),
            TextSecondary = FromHex(d.TextSecondary),
            TextDetail = FromHex(d.TextDetail),

            OverlayTop = FromHex(d.OverlayTop),
            OverlayMid = FromHex(d.OverlayMid),
            OverlayBot = FromHex(d.OverlayBot),

            ButtonPlayMid = FromHex(d.ButtonPlayMid),
            ButtonPlayEnd = FromHex(d.ButtonPlayEnd),

            FocusStart = FromHex(d.FocusStart),
            FocusMid = FromHex(d.FocusMid),
            FocusEnd = FromHex(d.FocusEnd),

            MenuBorderStart = FromHex(d.MenuBorderStart),
            MenuBorderEnd = FromHex(d.MenuBorderEnd),

            NoFocusStart = FromHex(d.NoFocusStart),
            NoFocusEnd = FromHex(d.NoFocusEnd),

            ShadeMidColor = FromHex(d.ShadeMidColor),
            ShadeEndColor = FromHex(d.ShadeEndColor),

            ControlBackgroundColor = FromHex(d.ControlBackgroundColor),
            SuccessStartColor = FromHex(d.SuccessStartColor),

            GlowMidColor = FromHex(d.GlowMidColor),
            GlowEndColor = FromHex(d.GlowEndColor),
        };


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
                // await Task.Delay(2200).ConfigureAwait(false);
                int steps = Math.Max(2, TransitionSteps);
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

        private static async Task RunPrecacheTrickleAsync()
        {
            try
            {
                await Task.Delay(PrecacheDelayMs);

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

                    await Task.Delay(PrecacheGapMs);

                    SaveAccentCacheNow(null, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                log.Warn(ex, "[DynColor] Trickle precache failed.");
            }
        }



    }
}
