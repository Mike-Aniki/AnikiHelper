using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.MediaGallery
{
    public class ScreenshotUtilitiesReader
    {
        private const string ScreenshotUtilitiesPluginId = "485d682f-73e9-4d54-b16f-b8dd49e88f90";

        // Provider Local de Screenshot Utilities
        private const string LocalProviderId = "a049eff8-fd41-4dbc-9e35-01acc6b1a0cb";

        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;

        private class UtilitiesFile
        {
            public string gameIdentifier { get; set; }
            public string id { get; set; }
            public string name { get; set; }
            public UtilitiesProvider provider { get; set; }
            public List<UtilitiesScreenshot> screenshots { get; set; }
        }

        private class UtilitiesProvider
        {
            public string id { get; set; }
            public string name { get; set; }
        }

        private class UtilitiesScreenshot
        {
            public string id { get; set; }
            public string name { get; set; }
            public string path { get; set; }
            public string thumbnailPath { get; set; }
            public int type { get; set; }
        }

        public ScreenshotUtilitiesReader(IPlayniteAPI playniteApi, ILogger logger)
        {
            this.playniteApi = playniteApi;
            this.logger = logger;
        }

        public List<AnikiMediaItem> LoadAllLocal()
        {
            var result = new List<AnikiMediaItem>();

            try
            {
                var root = FindScreenshotUtilitiesRoot();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    logger?.Warn("[AnikiHelper] Screenshot Utilities folder not found.");
                    return result;
                }

                var files = Directory
                    .EnumerateFiles(root, LocalProviderId + ".json", SearchOption.AllDirectories)
                    .ToList();

                foreach (var file in files)
                {
                    result.AddRange(LoadLocalFile(file));
                }

                return ApplyMediaIndexes(
                    result.OrderByDescending(x => x.CaptureDate)
                );
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Screenshot Utilities local media.");
                return ApplyMediaIndexes(result);
            }
        }

        public List<AnikiMediaItem> LoadLocalForGame(Guid gameId)
        {
            var result = new List<AnikiMediaItem>();

            try
            {
                if (gameId == Guid.Empty)
                {
                    return result;
                }

                var root = FindScreenshotUtilitiesRoot();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return result;
                }

                var file = Path.Combine(
                    root,
                    gameId.ToString(),
                    LocalProviderId,
                    LocalProviderId + ".json"
                );

                if (!File.Exists(file))
                {
                    return result;
                }

                return ApplyMediaIndexes(
                    LoadLocalFile(file).OrderByDescending(x => x.CaptureDate)
                );
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Screenshot Utilities local media for game.");
                return ApplyMediaIndexes(result);
            }
        }

        public bool HasLocalMediaForGame(Guid gameId)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return false;
                }

                var root = FindScreenshotUtilitiesRoot();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return false;
                }

                var file = Path.Combine(
                    root,
                    gameId.ToString(),
                    LocalProviderId,
                    LocalProviderId + ".json"
                );

                if (!File.Exists(file))
                {
                    return false;
                }

                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var data = JsonConvert.DeserializeObject<UtilitiesFile>(json);
                if (data == null)
                {
                    return false;
                }

                var providerId = data.provider?.id ?? data.id ?? string.Empty;
                if (!string.Equals(providerId, LocalProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (data.screenshots == null || data.screenshots.Count == 0)
                {
                    return false;
                }

                foreach (var screenshot in data.screenshots)
                {
                    if (screenshot == null || string.IsNullOrWhiteSpace(screenshot.path))
                    {
                        continue;
                    }

                    if (File.Exists(screenshot.path))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper] Failed to check Screenshot Utilities local media availability.");
            }

            return false;
        }

        private List<AnikiMediaItem> LoadLocalFile(string jsonPath)
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

                var data = JsonConvert.DeserializeObject<UtilitiesFile>(json);
                if (data == null)
                {
                    return result;
                }

                var providerId = data.provider?.id ?? data.id ?? string.Empty;
                if (!string.Equals(providerId, LocalProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                if (data.screenshots == null || data.screenshots.Count == 0)
                {
                    return result;
                }

                Guid gameId = Guid.Empty;

                if (!string.IsNullOrWhiteSpace(data.gameIdentifier))
                {
                    Guid.TryParse(data.gameIdentifier, out gameId);
                }

                if (gameId == Guid.Empty)
                {
                    var directory = Directory.GetParent(Path.GetDirectoryName(jsonPath));
                    if (directory != null)
                    {
                        Guid parsedId;
                        if (Guid.TryParse(directory.Name, out parsedId))
                        {
                            gameId = parsedId;
                        }
                    }
                }

                var gameName = GetGameName(gameId);

                foreach (var screenshot in data.screenshots)
                {
                    if (screenshot == null)
                    {
                        continue;
                    }

                    var path = screenshot.path;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var thumbnailPath = screenshot.thumbnailPath;
                    if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
                    {
                        thumbnailPath = string.Empty;
                    }

                    var captureDate = File.GetLastWriteTime(path);

                    result.Add(new AnikiMediaItem
                    {
                        GameId = gameId,
                        GameName = gameName,
                        Name = CleanName(screenshot.name, path),
                        FilePath = path,
                        ThumbnailPath = thumbnailPath,
                        CaptureDate = captureDate,
                        DurationString = string.Empty,
                        IsVideo = IsVideoFile(path),
                        SourceProvider = "Screenshot Utilities - Local"
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to parse Screenshot Utilities JSON: " + jsonPath);
            }

            return result;
        }

        private string FindScreenshotUtilitiesRoot()
        {
            try
            {
                var extensionsDataPath = playniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrWhiteSpace(extensionsDataPath) || !Directory.Exists(extensionsDataPath))
                {
                    return null;
                }

                var classicPath = Path.Combine(extensionsDataPath, ScreenshotUtilitiesPluginId);

                if (Directory.Exists(classicPath))
                {
                    return classicPath;
                }

                foreach (var dir in Directory.EnumerateDirectories(extensionsDataPath, ScreenshotUtilitiesPluginId, SearchOption.AllDirectories))
                {
                    if (Directory.Exists(dir))
                    {
                        return dir;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to find Screenshot Utilities folder.");
            }

            return null;
        }

        private string GetGameName(Guid gameId)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return string.Empty;
                }

                Game game = playniteApi?.Database?.Games?.Get(gameId);
                return game?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string CleanName(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Path.GetFileName(path);
            }

            var clean = name.Trim();

            if (clean.StartsWith("Global:", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring("Global:".Length).Trim();
            }

            return string.IsNullOrWhiteSpace(clean) ? Path.GetFileName(path) : clean;
        }

        private static List<AnikiMediaItem> ApplyMediaIndexes(IEnumerable<AnikiMediaItem> items)
        {
            var list = items?.ToList() ?? new List<AnikiMediaItem>();

            for (int i = 0; i < list.Count; i++)
            {
                list[i].MediaIndex = i + 1;
                list[i].MediaTotal = list.Count;
            }

            return list;
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