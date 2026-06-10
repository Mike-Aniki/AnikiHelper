using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.MediaGallery
{
    public class ScreenshotsVisualizerReader
    {
        private const string ScreenshotsVisualizerPluginId = "playnite-screenshotsvisualizer-plugin";
        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;

        private class VisualizerFile
        {
            public List<VisualizerItem> Items { get; set; }
            public List<string> ScreenshotsFolders { get; set; }
            public DateTime? DateLastRefresh { get; set; }
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        private class VisualizerItem
        {
            public string FileName { get; set; }

            // Le JSON de Screenshots Visualizer utilise bien "Modifed" et pas "Modified".
            public DateTime? Modifed { get; set; }

            public string duration { get; set; }
        }

        public ScreenshotsVisualizerReader(IPlayniteAPI playniteApi, ILogger logger)
        {
            this.playniteApi = playniteApi;
            this.logger = logger;
        }

        public List<AnikiMediaItem> LoadAll()
        {
            var result = new List<AnikiMediaItem>();

            try
            {
                var root = FindScreenshotsVisualizerRoot();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    logger?.Warn("[AnikiHelper] Screenshots Visualizer folder not found.");
                    return result;
                }

                var files = Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).ToList();

                foreach (var file in files)
                {
                    result.AddRange(LoadFile(file));
                }

                return ApplyMediaIndexes(
                    result.OrderByDescending(x => x.CaptureDate)
                );
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Screenshots Visualizer media.");
                return ApplyMediaIndexes(result);
            }
        }

        public List<AnikiMediaItem> LoadForGame(Guid gameId)
        {
            var result = new List<AnikiMediaItem>();

            try
            {
                if (gameId == Guid.Empty)
                {
                    return result;
                }

                var root = FindScreenshotsVisualizerRoot();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return result;
                }

                var file = Path.Combine(root, gameId + ".json");
                if (!File.Exists(file))
                {
                    return result;
                }

                return ApplyMediaIndexes(
                    LoadFile(file).OrderByDescending(x => x.CaptureDate)
                );
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Screenshots Visualizer media for game.");
                return ApplyMediaIndexes(result);
            }
        }

        public bool HasMediaForGame(Guid gameId)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return false;
                }

                var root = FindScreenshotsVisualizerRoot();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return false;
                }

                var file = Path.Combine(root, gameId + ".json");
                if (!File.Exists(file))
                {
                    return false;
                }

                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var data = JsonConvert.DeserializeObject<VisualizerFile>(json);
                if (data == null || data.Items == null || data.Items.Count == 0)
                {
                    return false;
                }

                foreach (var item in data.Items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.FileName))
                    {
                        continue;
                    }

                    if (File.Exists(item.FileName))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] Failed to check Screenshots Visualizer media availability.");
            }

            return false;
        }

        private List<AnikiMediaItem> LoadFile(string jsonPath)
        {
            var result = new List<AnikiMediaItem>();

            try
            {
                if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                {
                    return result;
                }

                var json = File.ReadAllText(jsonPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return result;
                }

                var data = JsonConvert.DeserializeObject<VisualizerFile>(json);
                if (data == null || data.Items == null || data.Items.Count == 0)
                {
                    return result;
                }

                var gameId = data.Id;

                if (gameId == Guid.Empty)
                {
                    var fileName = Path.GetFileNameWithoutExtension(jsonPath);
                    Guid parsedId;
                    if (Guid.TryParse(fileName, out parsedId))
                    {
                        gameId = parsedId;
                    }
                }

                foreach (var item in data.Items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.FileName))
                    {
                        continue;
                    }

                    var path = item.FileName;

                    // On ignore les fichiers supprimés.
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var captureDate = item.Modifed ?? File.GetLastWriteTime(path);

                    result.Add(new AnikiMediaItem
                    {
                        GameId = gameId,
                        GameName = data.Name ?? string.Empty,
                        Name = Path.GetFileName(path),
                        FilePath = path,
                        ThumbnailPath = FindVisualizerThumbnailPath(path),
                        CaptureDate = captureDate,
                        DurationString = item.duration ?? string.Empty,
                        IsVideo = IsVideoFile(path),
                        SourceProvider = "Screenshots Visualizer"
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to parse Screenshots Visualizer JSON: " + jsonPath);
            }

            return result;
        }

        private string FindVisualizerThumbnailPath(string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return string.Empty;
                }

                var cachePath = playniteApi?.Paths?.ConfigurationPath;
                if (string.IsNullOrWhiteSpace(cachePath))
                {
                    return string.Empty;
                }

                var thumbnailsRoot = Path.Combine(
                    cachePath,
                    "cache",
                    "ScreenshotsVisualizer",
                    "Thumbnails"
                );

                if (!Directory.Exists(thumbnailsRoot))
                {
                    return string.Empty;
                }

                var fileName = Path.GetFileName(sourcePath);
                var fileNameNoExt = Path.GetFileNameWithoutExtension(sourcePath);

                var direct = Path.Combine(thumbnailsRoot, fileName);
                if (File.Exists(direct))
                {
                    return direct;
                }

                var candidates = Directory.EnumerateFiles(thumbnailsRoot, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(x =>
                        string.Equals(Path.GetFileNameWithoutExtension(x), fileNameNoExt, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(x).IndexOf(fileNameNoExt, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                return candidates.FirstOrDefault(x => File.Exists(x)) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<AnikiMediaItem> ApplyMediaIndexes(IEnumerable<AnikiMediaItem> items)
        {
            var list = (items ?? Enumerable.Empty<AnikiMediaItem>())
                .Where(x => x != null)
                .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                .GroupBy(x => NormalizeMediaPath(x.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.CaptureDate).First())
                .OrderByDescending(x => x.CaptureDate)
                .ToList();

            for (int i = 0; i < list.Count; i++)
            {
                list[i].MediaIndex = i + 1;
                list[i].MediaTotal = list.Count;
            }

            return list;
        }

        private static string NormalizeMediaPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }

        private string FindScreenshotsVisualizerRoot()
        {
            try
            {
                var extensionsDataPath = playniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrWhiteSpace(extensionsDataPath) || !Directory.Exists(extensionsDataPath))
                {
                    return null;
                }

                // Chemin normal
                var classicPath = Path.Combine(
                    extensionsDataPath,
                    ScreenshotsVisualizerPluginId,
                    "ScreenshotsVisualizer"
                );

                if (Directory.Exists(classicPath))
                {
                    return classicPath;
                }

                // Fallback : si jamais l'utilisateur a une structure différente.
                foreach (var dir in Directory.EnumerateDirectories(extensionsDataPath, "ScreenshotsVisualizer", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly).Any())
                        {
                            return dir;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to find Screenshots Visualizer folder.");
            }

            return null;
        }

        private static bool IsVideoFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return false;
            }

            ext = ext.ToLowerInvariant();

            return ext == ".mp4"
                || ext == ".mkv"
                || ext == ".webm"
                || ext == ".avi"
                || ext == ".mov"
                || ext == ".wmv";
        }
    }
}