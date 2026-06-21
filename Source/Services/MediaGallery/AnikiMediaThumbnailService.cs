using Playnite.SDK;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace AnikiHelper.Services.MediaGallery
{
    public class AnikiMediaThumbnailService
    {
        private readonly string cacheDirectory;
        private readonly ILogger logger;

        private const int ThumbnailWidth = 520;
        private const int JpegQuality = 82;

        public AnikiMediaThumbnailService(string pluginUserDataPath, ILogger logger)
        {
            this.logger = logger;

            cacheDirectory = Path.Combine(pluginUserDataPath, "ScreenshotCache", "Thumbnails");

            try
            {
                Directory.CreateDirectory(cacheDirectory);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to create screenshot thumbnail cache directory.");
            }
        }

        public bool HasGeneratedImageThumbnail(AnikiMediaItem item)
        {
            try
            {
                if (item == null || item.IsVideo || string.IsNullOrWhiteSpace(item.FilePath))
                {
                    return false;
                }

                if (!File.Exists(item.FilePath))
                {
                    return false;
                }

                if (!IsSupportedImageFile(item.FilePath))
                {
                    return false;
                }

                var thumbnailPath = GetThumbnailPath(item.FilePath);

                return File.Exists(thumbnailPath) && new FileInfo(thumbnailPath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public string GetOrCreateThumbnail(AnikiMediaItem item)
        {
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
                {
                    return string.Empty;
                }

                if (!File.Exists(item.FilePath))
                {
                    return string.Empty;
                }

                // Vidéo : on utilise uniquement le thumbnail du provider s'il est valide.
                // Sinon le thème affichera "video preview unavailable".
                if (item.IsVideo)
                {
                    if (IsValidProviderThumbnail(item.ThumbnailPath, item.FilePath))
                    {
                        return item.ThumbnailPath;
                    }

                    return string.Empty;
                }

                // Image : si aucun thumbnail provider valide, Aniki génère son propre thumbnail.
                if (!IsSupportedImageFile(item.FilePath))
                {
                    return item.FilePath;
                }

                Directory.CreateDirectory(cacheDirectory);

                var thumbnailPath = GetThumbnailPath(item.FilePath);

                if (File.Exists(thumbnailPath) && new FileInfo(thumbnailPath).Length > 0)
                {
                    return thumbnailPath;
                }

                CreateImageThumbnail(item.FilePath, thumbnailPath);

                if (File.Exists(thumbnailPath) && new FileInfo(thumbnailPath).Length > 0)
                {
                    return thumbnailPath;
                }

                return item.FilePath;
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    "[AnikiHelper] Failed thumbnail for: " + item?.FilePath
                );

                return string.Empty;
            }
        }

        private static bool IsValidProviderThumbnail(string thumbnailPath, string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(thumbnailPath))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return false;
                }

                if (string.Equals(thumbnailPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!File.Exists(thumbnailPath))
                {
                    return false;
                }

                if (new FileInfo(thumbnailPath).Length <= 0)
                {
                    return false;
                }

                var ext = Path.GetExtension(thumbnailPath)?.ToLowerInvariant();

                return ext == ".jpg"
                    || ext == ".jpeg"
                    || ext == ".png"
                    || ext == ".webp"
                    || ext == ".bmp";
            }
            catch
            {
                return false;
            }
        }

        private string GetThumbnailPath(string sourcePath)
        {
            var lastWriteTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks;
            var key = Md5Hex(sourcePath + "|" + lastWriteTicks + "|thumb-v1");

            return Path.Combine(cacheDirectory, key + ".jpg");
        }

        private void CreateImageThumbnail(string sourcePath, string thumbnailPath)
        {
            BitmapImage bitmap = null;

            using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = ThumbnailWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }

            var encoder = new JpegBitmapEncoder
            {
                QualityLevel = JpegQuality
            };

            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var tempPath = thumbnailPath + ".tmp";

            using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(output);
            }

            if (File.Exists(thumbnailPath))
            {
                try
                {
                    File.Delete(thumbnailPath);
                }
                catch
                {
                }
            }

            File.Move(tempPath, thumbnailPath);
        }

        private static bool IsSupportedImageFile(string path)
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return false;
            }

            ext = ext.ToLowerInvariant();

            return ext == ".jpg"
                || ext == ".jpeg"
                || ext == ".png"
                || ext == ".bmp";
        }

        private static string Md5Hex(string text)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                var hash = md5.ComputeHash(bytes);

                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}