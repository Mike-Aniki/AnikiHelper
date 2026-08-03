using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace AnikiHelper.Services.UI
{
    /// <summary>
    /// Small bounded in-memory cache for decoded WPF images.
    /// Entries are invalidated automatically when a local source file changes.
    /// The cache is process-local and is released when Playnite closes.
    /// </summary>
    internal static class ImageMemoryCache
    {
        private const int MaximumEntryCount = 32;
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, CacheEntry> Entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource GetOrLoad(string source, int decodePixelWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            string normalizedSource;
            bool isLocalFile;
            DateTime lastWriteTimeUtc;

            if (!TryNormalizeSource(source, out normalizedSource, out isLocalFile, out lastWriteTimeUtc))
            {
                return null;
            }

            var cacheKey = normalizedSource + "|w=" + Math.Max(0, decodePixelWidth);

            lock (SyncRoot)
            {
                CacheEntry existing;
                if (Entries.TryGetValue(cacheKey, out existing))
                {
                    if (!isLocalFile || existing.LastWriteTimeUtc == lastWriteTimeUtc)
                    {
                        existing.LastAccessUtc = DateTime.UtcNow;
                        return existing.Bitmap;
                    }

                    Entries.Remove(cacheKey);
                }
            }

            var loadedBitmap = LoadBitmap(normalizedSource, decodePixelWidth);
            if (loadedBitmap == null)
            {
                return null;
            }

            lock (SyncRoot)
            {
                CacheEntry current;
                if (Entries.TryGetValue(cacheKey, out current))
                {
                    if (!isLocalFile || current.LastWriteTimeUtc == lastWriteTimeUtc)
                    {
                        current.LastAccessUtc = DateTime.UtcNow;
                        return current.Bitmap;
                    }

                    Entries.Remove(cacheKey);
                }

                Entries[cacheKey] = new CacheEntry
                {
                    Bitmap = loadedBitmap,
                    LastWriteTimeUtc = lastWriteTimeUtc,
                    LastAccessUtc = DateTime.UtcNow
                };

                TrimIfNeeded();
            }

            return loadedBitmap;
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Entries.Clear();
            }
        }

        private static BitmapSource LoadBitmap(string normalizedSource, int decodePixelWidth)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;

                if (decodePixelWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodePixelWidth;
                }

                bitmap.UriSource = new Uri(normalizedSource, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryNormalizeSource(
            string source,
            out string normalizedSource,
            out bool isLocalFile,
            out DateTime lastWriteTimeUtc)
        {
            normalizedSource = null;
            isLocalFile = false;
            lastWriteTimeUtc = DateTime.MinValue;

            try
            {
                Uri absoluteUri;
                if (Uri.TryCreate(source, UriKind.Absolute, out absoluteUri) &&
                    (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
                {
                    normalizedSource = absoluteUri.AbsoluteUri;
                    return true;
                }

                var localPath = source;
                if (Uri.TryCreate(source, UriKind.Absolute, out absoluteUri) && absoluteUri.IsFile)
                {
                    localPath = absoluteUri.LocalPath;
                }

                localPath = Path.GetFullPath(localPath);
                if (!File.Exists(localPath))
                {
                    return false;
                }

                isLocalFile = true;
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(localPath);
                normalizedSource = new Uri(localPath, UriKind.Absolute).AbsoluteUri;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TrimIfNeeded()
        {
            if (Entries.Count <= MaximumEntryCount)
            {
                return;
            }

            var keysToRemove = Entries
                .OrderBy(pair => pair.Value.LastAccessUtc)
                .Take(Entries.Count - MaximumEntryCount)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                Entries.Remove(key);
            }
        }

        private sealed class CacheEntry
        {
            public BitmapSource Bitmap { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
            public DateTime LastAccessUtc { get; set; }
        }
    }
}
