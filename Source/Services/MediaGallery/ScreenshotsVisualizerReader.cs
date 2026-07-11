using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

namespace AnikiHelper.Services.MediaGallery
{
    public class ScreenshotsVisualizerReader
    {
        private const string ScreenshotsVisualizerPluginId = "playnite-screenshotsvisualizer-plugin";
        private static readonly Guid ScreenshotsVisualizerAddonId = Guid.Parse("c6c8276f-91bf-48e5-a1d1-4bee0b493488");
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

        public bool IsAvailable()
        {
            try
            {
                var root = FindScreenshotsVisualizerRoot();

                return !string.IsNullOrWhiteSpace(root)
                    && Directory.Exists(root);
            }
            catch
            {
                return false;
            }
        }

        public bool IsPluginInstalled()
        {
            try
            {
                if (playniteApi?.Addons?.Plugins?.Any(plugin =>
                    plugin != null && plugin.Id == ScreenshotsVisualizerAddonId) == true)
                {
                    return true;
                }
            }
            catch
            {
            }

            // Fallback for cases where Playnite has not exposed the loaded plugin instance
            // yet, while ScreenshotsVisualizer data is already available on disk.
            return IsAvailable();
        }

        public Task<bool> RefreshGameDataAsync(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return Task.FromResult(false);
            }

            return Task.Run(() => RefreshGameData(gameId));
        }

        private bool RefreshGameData(Guid gameId)
        {
            try
            {
                var visualizerPlugin = playniteApi?.Addons?.Plugins?.FirstOrDefault(plugin =>
                    plugin != null && plugin.Id == ScreenshotsVisualizerAddonId);

                if (visualizerPlugin == null)
                {
                    logger?.Warn("[AnikiHelper] Screenshots Visualizer plugin not found.");
                    return false;
                }

                var pluginDatabase = FindPluginDatabase(visualizerPlugin);
                if (pluginDatabase == null)
                {
                    logger?.Warn("[AnikiHelper] Screenshots Visualizer database not found.");
                    return false;
                }

                var databaseType = pluginDatabase.GetType();

                var getGameSettingsMethod = databaseType.GetMethod(
                    "GetGameSettings",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Guid) },
                    null
                );

                if (getGameSettingsMethod == null)
                {
                    logger?.Warn("[AnikiHelper] Screenshots Visualizer GetGameSettings method not found.");
                    return false;
                }

                var gameSettings = getGameSettingsMethod.Invoke(
                    pluginDatabase,
                    new object[] { gameId }
                );

                if (gameSettings == null)
                {
                    logger?.Warn("[AnikiHelper] Screenshots Visualizer returned no settings for the game.");
                    return false;
                }

                var setDataMethod = databaseType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(
                                method.Name,
                                "SetDataFromSettings",
                                StringComparison.Ordinal))
                        {
                            return false;
                        }

                        var parameters = method.GetParameters();
                        return parameters.Length == 1
                            && parameters[0].ParameterType.IsInstanceOfType(gameSettings);
                    });

                if (setDataMethod == null)
                {
                    logger?.Warn("[AnikiHelper] Screenshots Visualizer SetDataFromSettings method not found.");
                    return false;
                }

                setDataMethod.Invoke(pluginDatabase, new[] { gameSettings });

                logger?.Debug(
                    "[AnikiHelper] Screenshots Visualizer game data refreshed for " + gameId
                );

                return true;
            }
            catch (TargetInvocationException ex)
            {
                logger?.Warn(
                    ex.InnerException ?? ex,
                    "[AnikiHelper] Screenshots Visualizer refresh failed."
                );

                return false;
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    "[AnikiHelper] Screenshots Visualizer refresh failed."
                );

                return false;
            }
        }

        private static object FindPluginDatabase(object pluginInstance)
        {
            if (pluginInstance == null)
            {
                return null;
            }

            for (var type = pluginInstance.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(
                    "PluginDatabase",
                    BindingFlags.DeclaredOnly
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                );

                if (property != null)
                {
                    try
                    {
                        var value = property.GetValue(
                            property.GetGetMethod(true)?.IsStatic == true ? null : pluginInstance,
                            null
                        );

                        if (value != null)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                    }
                }

                var field = type.GetField(
                    "PluginDatabase",
                    BindingFlags.DeclaredOnly
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                );

                if (field != null)
                {
                    try
                    {
                        var value = field.GetValue(field.IsStatic ? null : pluginInstance);
                        if (value != null)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        public DateTime? GetGameDataRefreshStamp(Guid gameId)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return null;
                }

                var root = FindScreenshotsVisualizerRoot();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return null;
                }

                var file = Path.Combine(root, gameId + ".json");
                if (!File.Exists(file))
                {
                    return null;
                }

                var fileWriteTime = File.GetLastWriteTime(file);
                DateTime? jsonRefreshTime = null;

                try
                {
                    var json = File.ReadAllText(file);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var data = JsonConvert.DeserializeObject<VisualizerFile>(json);
                        jsonRefreshTime = data?.DateLastRefresh;
                    }
                }
                catch (Exception ex)
                {
                    logger?.Debug(
                        ex,
                        "[AnikiHelper] Failed to read Screenshots Visualizer refresh date."
                    );
                }

                if (!jsonRefreshTime.HasValue)
                {
                    return fileWriteTime;
                }

                return fileWriteTime > jsonRefreshTime.Value
                    ? fileWriteTime
                    : jsonRefreshTime.Value;
            }
            catch (Exception ex)
            {
                logger?.Debug(
                    ex,
                    "[AnikiHelper] Failed to get Screenshots Visualizer refresh stamp."
                );

                return null;
            }
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

                    var isVideo = IsVideoFile(path);
                    var thumbnailPath = FindVisualizerThumbnailPath(path);

                    var durationString = isVideo
                        ? ResolveVisualizerVideoDuration(
                            item.duration,
                            path,
                            thumbnailPath
                        )
                        : string.Empty;

                    result.Add(new AnikiMediaItem
                    {
                        GameId = gameId,
                        GameName = data.Name ?? string.Empty,
                        Name = Path.GetFileName(path),
                        FilePath = path,
                        ThumbnailPath = thumbnailPath,
                        CaptureDate = captureDate,
                        DurationString = durationString,
                        IsVideo = isVideo,
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

        private static string ResolveVisualizerVideoDuration(
    string serializedDuration,
    string sourcePath,
    string thumbnailPath)
        {
            try
            {
                // Compatibilité avec les anciennes versions de Screenshots Visualizer
                // qui stockaient encore la durée dans le JSON.
                if (!string.IsNullOrWhiteSpace(serializedDuration)
                    && TimeSpan.TryParse(
                        serializedDuration,
                        CultureInfo.InvariantCulture,
                        out var serializedTime)
                    && serializedTime > TimeSpan.Zero)
                {
                    return serializedTime.ToString("c", CultureInfo.InvariantCulture);
                }

                if (string.IsNullOrWhiteSpace(sourcePath)
                    || string.IsNullOrWhiteSpace(thumbnailPath)
                    || !File.Exists(sourcePath)
                    || !File.Exists(thumbnailPath))
                {
                    return string.Empty;
                }

                var thumbnailName = Path.GetFileNameWithoutExtension(thumbnailPath);
                var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
                var fileSize = new FileInfo(sourcePath).Length;

                if (string.IsNullOrWhiteSpace(thumbnailName)
                    || string.IsNullOrWhiteSpace(sourceName))
                {
                    return string.Empty;
                }

                var prefix = sourceName
                    + "_"
                    + fileSize.ToString(CultureInfo.InvariantCulture)
                    + "_";

                const string suffix = "_Thumbnail";

                if (!thumbnailName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !thumbnailName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                var durationPartLength =
                    thumbnailName.Length
                    - prefix.Length
                    - suffix.Length;

                if (durationPartLength <= 0)
                {
                    return string.Empty;
                }

                var durationPart = thumbnailName.Substring(
                    prefix.Length,
                    durationPartLength
                );

                double durationSeconds;

                // Visualizer construit actuellement le nom avec la culture Windows.
                // En France, la valeur peut donc contenir une virgule.
                if (!double.TryParse(
                        durationPart,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out durationSeconds)
                    && !double.TryParse(
                        durationPart,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out durationSeconds)
                    && !double.TryParse(
                        durationPart.Replace(',', '.'),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out durationSeconds))
                {
                    return string.Empty;
                }

                if (double.IsNaN(durationSeconds)
                    || double.IsInfinity(durationSeconds)
                    || durationSeconds <= 0)
                {
                    return string.Empty;
                }

                return TimeSpan
                    .FromSeconds(durationSeconds)
                    .ToString("c", CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string FindVisualizerThumbnailPath(string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath)
                    || !File.Exists(sourcePath))
                {
                    return string.Empty;
                }

                var configurationPath = playniteApi?.Paths?.ConfigurationPath;

                if (string.IsNullOrWhiteSpace(configurationPath))
                {
                    return string.Empty;
                }

                var thumbnailsRoot = Path.Combine(
                    configurationPath,
                    "cache",
                    "ScreenshotsVisualizer",
                    "Thumbnails"
                );

                if (!Directory.Exists(thumbnailsRoot))
                {
                    return string.Empty;
                }

                var sourceName = Path.GetFileNameWithoutExtension(sourcePath);

                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    return string.Empty;
                }

                // Les miniatures image utilisent un nom parfaitement prévisible.
                if (!IsVideoFile(sourcePath))
                {
                    var imageThumbnailPath = Path.Combine(
                        thumbnailsRoot,
                        sourceName
                            + "_"
                            + sourceName
                            + "_Thumbnail.jpg"
                    );

                    if (File.Exists(imageThumbnailPath)
                        && new FileInfo(imageThumbnailPath).Length > 0)
                    {
                        return imageThumbnailPath;
                    }

                    return string.Empty;
                }

                // Les miniatures vidéo contiennent :
                // nom + taille du fichier + durée + suffixe.
                var fileSize = new FileInfo(sourcePath).Length;

                var prefix = sourceName
                    + "_"
                    + fileSize.ToString(CultureInfo.InvariantCulture)
                    + "_";

                const string suffix = "_Thumbnail.jpg";

                var searchPattern = prefix + "*" + suffix;

                return Directory
                    .EnumerateFiles(
                        thumbnailsRoot,
                        searchPattern,
                        SearchOption.TopDirectoryOnly
                    )
                    .Where(path =>
                    {
                        try
                        {
                            var name = Path.GetFileName(path);

                            return !string.IsNullOrWhiteSpace(name)
                                && name.StartsWith(
                                    prefix,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                && name.EndsWith(
                                    suffix,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                && new FileInfo(path).Length > 0;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                    ?? string.Empty;
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