using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.SplashScreen
{
    internal static class SplashScreenMediaScanner
    {
        public static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".jfif"
        };

        public static readonly string[] VideoExtensions =
        {
            ".mp4", ".wmv", ".avi", ".mov", ".mkv"
        };

        public static readonly string[] SupportedExtensions =
            ImageExtensions.Concat(VideoExtensions).ToArray();

        public static string DialogFilter =>
            "Splash files|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.jfif;*.mp4;*.wmv;*.avi;*.mov;*.mkv";

        public static bool IsSupportedMedia(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return IsImageExtension(extension) || IsVideoExtension(extension);
        }

        public static bool IsImageFile(string filePath)
        {
            return IsImageExtension(Path.GetExtension(filePath));
        }

        public static bool IsVideoFile(string filePath)
        {
            return IsVideoExtension(Path.GetExtension(filePath));
        }

        public static bool IsImageExtension(string extension)
        {
            return ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsVideoExtension(string extension)
        {
            return VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        public static List<string> GetSupportedFiles(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return new List<string>();
            }

            return Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedMedia)
                .OrderBy(Path.GetFileName)
                .ToList();
        }
    }
}