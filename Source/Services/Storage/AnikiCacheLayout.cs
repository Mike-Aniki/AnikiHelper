using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnikiHelper
{
    internal static class AnikiCacheLayout
    {
        private const string CacheFolderName = "Cache";
        private const string LayoutVersionFileName = ".layout-version";
        private const string LayoutV2BackupSuffix = ".pre-layout-v2";
        private const string LayoutV2TempSuffix = ".layout-v2.tmp";
        private const int CurrentLayoutVersion = 2;

        private sealed class LegacyPathMapping
        {
            public string LegacyRoot { get; set; }
            public string NewRoot { get; set; }
        }

        public static string CacheRoot(string pluginUserDataPath)
            => Path.Combine(pluginUserDataPath ?? string.Empty, CacheFolderName);

        public static string AchievementsRoot(string pluginUserDataPath)
            => Path.Combine(CacheRoot(pluginUserDataPath), "Achievements");

        public static string HubRoot(string pluginUserDataPath)
            => Path.Combine(CacheRoot(pluginUserDataPath), "Hub");

        public static string HubBackgroundsRoot(string pluginUserDataPath)
            => Path.Combine(HubRoot(pluginUserDataPath), "Backgrounds");

        public static string HubDataRoot(string pluginUserDataPath)
            => Path.Combine(HubRoot(pluginUserDataPath), "Data");

        public static string NewsRoot(string pluginUserDataPath)
            => Path.Combine(CacheRoot(pluginUserDataPath), "News");

        public static string ScreenshotsRoot(string pluginUserDataPath)
            => Path.Combine(CacheRoot(pluginUserDataPath), "Screenshots");

        public static string SteamRoot(string pluginUserDataPath)
            => Path.Combine(CacheRoot(pluginUserDataPath), "Steam");

        public static string SteamGameNewsImagesRoot(string pluginUserDataPath)
            => Path.Combine(SteamRoot(pluginUserDataPath), "GameNewsImages");

        public static string SteamFriendsRoot(string pluginUserDataPath)
            => Path.Combine(SteamRoot(pluginUserDataPath), "Friends");

        public static string SteamStoreRoot(string pluginUserDataPath)
            => Path.Combine(SteamRoot(pluginUserDataPath), "Store");

        public static string ThemeRoot(string pluginUserDataPath)
            => Path.Combine(CacheRoot(pluginUserDataPath), "Theme");

        public static string SteamUpdatesCachePath(string pluginUserDataPath)
            => Path.Combine(SteamRoot(pluginUserDataPath), "steam_updates_cache.json");

        public static string SteamAppIdMappingCachePath(string pluginUserDataPath)
            => Path.Combine(SteamRoot(pluginUserDataPath), "steam_appid_mapping_cache.json");

        public static string SteamGameNewsCachePath(string pluginUserDataPath)
            => Path.Combine(SteamRoot(pluginUserDataPath), "steam_game_news_cache.json");

        public static string DynamicPaletteCachePath(string pluginUserDataPath)
            => Path.Combine(ThemeRoot(pluginUserDataPath), "palette_cache_v2.json");

        public static void EnsureBaseFolders(string pluginUserDataPath)
        {
            Directory.CreateDirectory(CacheRoot(pluginUserDataPath));
            Directory.CreateDirectory(AchievementsRoot(pluginUserDataPath));
            Directory.CreateDirectory(HubBackgroundsRoot(pluginUserDataPath));
            Directory.CreateDirectory(HubDataRoot(pluginUserDataPath));
            Directory.CreateDirectory(NewsRoot(pluginUserDataPath));
            Directory.CreateDirectory(ScreenshotsRoot(pluginUserDataPath));
            Directory.CreateDirectory(SteamGameNewsImagesRoot(pluginUserDataPath));
            Directory.CreateDirectory(SteamFriendsRoot(pluginUserDataPath));
            Directory.CreateDirectory(SteamStoreRoot(pluginUserDataPath));
            Directory.CreateDirectory(ThemeRoot(pluginUserDataPath));
        }

        public static void MigrateLegacyLayout(string pluginUserDataPath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                return;
            }

            try
            {
                EnsureBaseFolders(pluginUserDataPath);

                MoveDirectoryContents(
                    Path.Combine(pluginUserDataPath, "AchievementCache"),
                    AchievementsRoot(pluginUserDataPath),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(pluginUserDataPath, "Hub Background Cache"),
                    HubBackgroundsRoot(pluginUserDataPath),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(pluginUserDataPath, "Hub Cache"),
                    HubDataRoot(pluginUserDataPath),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(pluginUserDataPath, "News Cache"),
                    NewsRoot(pluginUserDataPath),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(pluginUserDataPath, "ScreenshotCache"),
                    ScreenshotsRoot(pluginUserDataPath),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(pluginUserDataPath, "Steam Game News Images"),
                    SteamGameNewsImagesRoot(pluginUserDataPath),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(pluginUserDataPath, "SteamFriendCache"),
                    SteamFriendsRoot(pluginUserDataPath),
                    logger);

                // SteamStore also contains persistent state files. Only move cache folders.
                var legacySteamStoreRoot = Path.Combine(pluginUserDataPath, "SteamStore");
                var newSteamStoreRoot = SteamStoreRoot(pluginUserDataPath);

                MoveDirectoryContents(
                    Path.Combine(legacySteamStoreRoot, "StoreCache"),
                    Path.Combine(newSteamStoreRoot, "StoreCache"),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(legacySteamStoreRoot, "DetailsCache"),
                    Path.Combine(newSteamStoreRoot, "DetailsCache"),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(legacySteamStoreRoot, "ImageCache"),
                    Path.Combine(newSteamStoreRoot, "ImageCache"),
                    logger);

                // Old intermediary cache layouts are also moved so their existing
                // service-level migration logic can still consume them from Cache.
                MoveDirectoryContents(
                    Path.Combine(legacySteamStoreRoot, "UserCache"),
                    Path.Combine(newSteamStoreRoot, "UserCache"),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(legacySteamStoreRoot, "RecommendedCache"),
                    Path.Combine(newSteamStoreRoot, "RecommendedCache"),
                    logger);

                MoveDirectoryContents(
                    Path.Combine(legacySteamStoreRoot, "Recommended"),
                    Path.Combine(newSteamStoreRoot, "Recommended"),
                    logger);

                MoveFilePreferNewest(
                    Path.Combine(pluginUserDataPath, "steam_updates_cache.json"),
                    SteamUpdatesCachePath(pluginUserDataPath),
                    logger);

                MoveFilePreferNewest(
                    Path.Combine(pluginUserDataPath, "steam_appid_mapping_cache.json"),
                    SteamAppIdMappingCachePath(pluginUserDataPath),
                    logger);

                MoveFilePreferNewest(
                    Path.Combine(pluginUserDataPath, "steam_game_news_cache.json"),
                    SteamGameNewsCachePath(pluginUserDataPath),
                    logger);

                var themeRoot = ThemeRoot(pluginUserDataPath);
                string[] paletteFiles =
                {
                    "palette_cache_v2.json",
                    "palette_cache_v2.json.tmp",
                    "palette_cache.json",
                    "palette_cache.json.tmp",
                    "palette_cache_v1.json",
                    "palette_cache_v1.json.tmp"
                };

                foreach (var fileName in paletteFiles)
                {
                    MoveFilePreferNewest(
                        Path.Combine(pluginUserDataPath, fileName),
                        Path.Combine(themeRoot, fileName),
                        logger);
                }

                TryDeleteDirectoryIfEmpty(legacySteamStoreRoot);

                // Layout V2 repairs absolute paths stored inside JSON files after the
                // physical cache migration above. It intentionally runs even when the
                // old folders no longer exist, because a previous Helper build may
                // already have moved the files while leaving stale paths in JSON.
                if (!IsPrimaryLegacyPhysicalMigrationComplete(pluginUserDataPath, logger))
                {
                    logger?.Warn(
                        "[AnikiHelper][CacheMigration] Layout V2 JSON migration postponed because legacy cache files are still present.");
                    return;
                }

                MigrateJsonLayoutV2(pluginUserDataPath, logger);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CacheMigration] Legacy cache layout migration failed.");
            }
        }

        private static void MigrateJsonLayoutV2(string pluginUserDataPath, ILogger logger)
        {
            var versionPath = Path.Combine(CacheRoot(pluginUserDataPath), LayoutVersionFileName);

            if (!TryReadLayoutVersion(versionPath, logger, out var currentVersion))
            {
                return;
            }

            if (currentVersion >= CurrentLayoutVersion)
            {
                return;
            }

            var mappings = BuildLegacyPathMappings(pluginUserDataPath);
            var jsonFiles = new List<string>();
            var cacheRoot = CacheRoot(pluginUserDataPath);

            try
            {
                if (Directory.Exists(cacheRoot))
                {
                    jsonFiles.AddRange(Directory.GetFiles(cacheRoot, "*.json", SearchOption.AllDirectories));
                }

                var configPath = Path.Combine(pluginUserDataPath, "config.json");
                if (File.Exists(configPath))
                {
                    jsonFiles.Add(configPath);
                }

                jsonFiles.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CacheMigration] Failed to enumerate JSON files for layout V2 migration.");
                return;
            }

            var migrationSucceeded = true;
            var modifiedFileCount = 0;
            var modifiedValueCount = 0;

            foreach (var jsonPath in jsonFiles)
            {
                try
                {
                    var jsonText = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var rootToken = JToken.Parse(jsonText);
                    var changedValues = RebaseLegacyPathsInToken(
                        rootToken,
                        pluginUserDataPath,
                        mappings);

                    if (changedValues <= 0)
                    {
                        continue;
                    }

                    var serialized = rootToken.ToString(Formatting.Indented);

                    // Validate the serialized payload before touching the original file.
                    JToken.Parse(serialized);

                    if (!TryWriteMigratedJson(jsonPath, serialized, logger))
                    {
                        migrationSucceeded = false;
                        continue;
                    }

                    modifiedFileCount++;
                    modifiedValueCount += changedValues;
                }
                catch (Exception ex)
                {
                    migrationSucceeded = false;
                    logger?.Warn(
                        ex,
                        $"[AnikiHelper][CacheMigration] Layout V2 could not safely migrate JSON '{jsonPath}'. The original file was left untouched.");
                }
            }

            if (!migrationSucceeded)
            {
                logger?.Warn(
                    "[AnikiHelper][CacheMigration] Layout V2 migration was not fully successful. The layout version will not be advanced and migration will retry on a future startup.");
                return;
            }

            if (!TryWriteLayoutVersion(versionPath, CurrentLayoutVersion, logger))
            {
                return;
            }

            global::AnikiHelper.AnikiLog.Debug(
                logger,
                $"[AnikiHelper][CacheMigration] Layout V2 complete | JSON files updated={modifiedFileCount} | values rebased={modifiedValueCount}.");
        }

        private static List<LegacyPathMapping> BuildLegacyPathMappings(string pluginUserDataPath)
        {
            var steamStoreRoot = SteamStoreRoot(pluginUserDataPath);
            var steamFriendsRoot = SteamFriendsRoot(pluginUserDataPath);

            return new List<LegacyPathMapping>
            {
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "AchievementCache"),
                    NewRoot = AchievementsRoot(pluginUserDataPath)
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "Hub Background Cache"),
                    NewRoot = HubBackgroundsRoot(pluginUserDataPath)
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "Hub Cache"),
                    NewRoot = HubDataRoot(pluginUserDataPath)
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "News Cache"),
                    NewRoot = NewsRoot(pluginUserDataPath)
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "ScreenshotCache"),
                    NewRoot = ScreenshotsRoot(pluginUserDataPath)
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "Steam Game News Images"),
                    NewRoot = SteamGameNewsImagesRoot(pluginUserDataPath)
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamFriendCache"),
                    NewRoot = steamFriendsRoot
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamStore", "StoreCache"),
                    NewRoot = Path.Combine(steamStoreRoot, "StoreCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamStore", "DetailsCache"),
                    NewRoot = Path.Combine(steamStoreRoot, "DetailsCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamStore", "ImageCache"),
                    NewRoot = Path.Combine(steamStoreRoot, "ImageCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamStore", "UserCache"),
                    NewRoot = Path.Combine(steamStoreRoot, "UserCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamStore", "RecommendedCache"),
                    NewRoot = Path.Combine(steamStoreRoot, "RecommendedCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamStore", "Recommended"),
                    NewRoot = Path.Combine(steamStoreRoot, "Recommended")
                },

                // Very old layouts still supported by service-level migrations.
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamStoreImages"),
                    NewRoot = Path.Combine(steamStoreRoot, "ImageCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamFriendsAvatarCache"),
                    NewRoot = Path.Combine(steamFriendsRoot, "AvatarCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamFriendsGameHeaderCache"),
                    NewRoot = Path.Combine(steamFriendsRoot, "GameHeaderCache")
                },
                new LegacyPathMapping
                {
                    LegacyRoot = Path.Combine(pluginUserDataPath, "SteamFriendProfilesCache"),
                    NewRoot = Path.Combine(steamFriendsRoot, "FriendProfilesCache")
                }
            };
        }

        private static int RebaseLegacyPathsInToken(
            JToken token,
            string pluginUserDataPath,
            IList<LegacyPathMapping> mappings)
        {
            if (token == null)
            {
                return 0;
            }

            var valueToken = token as JValue;
            if (valueToken != null && valueToken.Type == JTokenType.String)
            {
                var currentValue = valueToken.Value as string;
                if (TryRebaseStoredString(
                    currentValue,
                    pluginUserDataPath,
                    mappings,
                    out var rebasedValue))
                {
                    valueToken.Value = rebasedValue;
                    return 1;
                }

                return 0;
            }

            var changed = 0;
            var container = token as JContainer;
            if (container != null)
            {
                foreach (var child in container.Children())
                {
                    changed += RebaseLegacyPathsInToken(
                        child,
                        pluginUserDataPath,
                        mappings);
                }
            }

            return changed;
        }

        private static bool TryRebaseStoredString(
            string value,
            string pluginUserDataPath,
            IList<LegacyPathMapping> mappings,
            out string rebasedValue)
        {
            rebasedValue = value;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // Normal absolute Windows path.
            if (TryRebaseFileSystemPath(
                value,
                pluginUserDataPath,
                mappings,
                out var rebasedPath))
            {
                rebasedValue = rebasedPath;
                return true;
            }

            // Cached Steam Friends values may be persisted as file:/// URIs.
            try
            {
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    var localPath = uri.LocalPath;
                    if (TryRebaseFileSystemPath(
                        localPath,
                        pluginUserDataPath,
                        mappings,
                        out rebasedPath))
                    {
                        rebasedValue = new Uri(rebasedPath, UriKind.Absolute).AbsoluteUri;
                        return true;
                    }
                }
            }
            catch
            {
                // Not a usable file URI. Leave the stored value untouched.
            }

            return false;
        }

        private static bool TryRebaseFileSystemPath(
            string candidatePath,
            string pluginUserDataPath,
            IList<LegacyPathMapping> mappings,
            out string rebasedPath)
        {
            rebasedPath = candidatePath;

            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            foreach (var mapping in mappings)
            {
                if (mapping == null ||
                    string.IsNullOrWhiteSpace(mapping.LegacyRoot) ||
                    string.IsNullOrWhiteSpace(mapping.NewRoot))
                {
                    continue;
                }

                if (TryGetRelativePathUnderLegacyRoot(
                    candidatePath,
                    mapping.LegacyRoot,
                    out var relativePath))
                {
                    rebasedPath = string.IsNullOrEmpty(relativePath)
                        ? mapping.NewRoot
                        : Path.Combine(mapping.NewRoot, relativePath);
                    return true;
                }
            }

            // SteamStoreCache predates the SteamStore folder itself. Its appdetails_*.json
            // files historically migrate to DetailsCache; every other JSON goes to StoreCache.
            var veryOldSteamStoreCacheRoot = Path.Combine(pluginUserDataPath, "SteamStoreCache");
            if (TryGetRelativePathUnderLegacyRoot(
                candidatePath,
                veryOldSteamStoreCacheRoot,
                out var steamStoreRelativePath))
            {
                var fileName = Path.GetFileName(steamStoreRelativePath) ?? string.Empty;
                var targetFolder = fileName.StartsWith("appdetails_", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(SteamStoreRoot(pluginUserDataPath), "DetailsCache")
                    : Path.Combine(SteamStoreRoot(pluginUserDataPath), "StoreCache");

                rebasedPath = string.IsNullOrEmpty(steamStoreRelativePath)
                    ? targetFolder
                    : Path.Combine(targetFolder, steamStoreRelativePath);
                return true;
            }

            return false;
        }

        private static bool TryGetRelativePathUnderLegacyRoot(
            string candidatePath,
            string legacyRoot,
            out string relativePath)
        {
            relativePath = null;

            if (string.IsNullOrWhiteSpace(candidatePath) ||
                string.IsNullOrWhiteSpace(legacyRoot))
            {
                return false;
            }

            string candidateNormalized;
            string legacyNormalized;

            try
            {
                if (!Path.IsPathRooted(candidatePath) || !Path.IsPathRooted(legacyRoot))
                {
                    return false;
                }

                // Canonicalize before the prefix check so a value containing ".." cannot
                // appear to live under an Aniki cache root while actually resolving outside it.
                candidateNormalized = NormalizeSeparators(Path.GetFullPath(candidatePath));
                legacyNormalized = NormalizeSeparators(Path.GetFullPath(legacyRoot))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return false;
            }

            if (string.Equals(candidateNormalized, legacyNormalized, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = string.Empty;
                return true;
            }

            if (candidateNormalized.Length <= legacyNormalized.Length ||
                !candidateNormalized.StartsWith(legacyNormalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var boundary = candidateNormalized[legacyNormalized.Length];
            if (boundary != Path.DirectorySeparatorChar &&
                boundary != Path.AltDirectorySeparatorChar)
            {
                return false;
            }

            relativePath = candidateNormalized
                .Substring(legacyNormalized.Length + 1)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return true;
        }

        private static string NormalizeSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        private static bool TryWriteMigratedJson(string jsonPath, string serializedJson, ILogger logger)
        {
            var backupPath = jsonPath + LayoutV2BackupSuffix;
            var tempPath = jsonPath + LayoutV2TempSuffix;

            try
            {
                if (!File.Exists(backupPath))
                {
                    File.Copy(jsonPath, backupPath, false);
                }

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                File.WriteAllText(tempPath, serializedJson, new UTF8Encoding(false));

                // Re-read the temp file before replacing the original. This catches an
                // incomplete/failed write without risking the user's existing JSON.
                JToken.Parse(File.ReadAllText(tempPath, Encoding.UTF8));

                File.Replace(tempPath, jsonPath, null, true);
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    $"[AnikiHelper][CacheMigration] Failed to safely write migrated JSON '{jsonPath}'. The backup/original was preserved.");

                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best effort cleanup only.
                }

                return false;
            }
        }

        private static bool TryReadLayoutVersion(string versionPath, ILogger logger, out int version)
        {
            version = 0;

            try
            {
                if (!File.Exists(versionPath))
                {
                    return true;
                }

                var text = File.ReadAllText(versionPath, Encoding.UTF8).Trim();
                if (int.TryParse(text, out version))
                {
                    return true;
                }

                logger?.Warn(
                    $"[AnikiHelper][CacheMigration] Invalid cache layout version '{text}'. Layout V2 migration will be retried.");
                version = 0;
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CacheMigration] Failed to read cache layout version.");
                return false;
            }
        }

        private static bool TryWriteLayoutVersion(string versionPath, int version, ILogger logger)
        {
            var tempPath = versionPath + ".tmp";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(versionPath));

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                File.WriteAllText(tempPath, version.ToString(), new UTF8Encoding(false));

                if (File.Exists(versionPath))
                {
                    File.Replace(tempPath, versionPath, null, true);
                }
                else
                {
                    File.Move(tempPath, versionPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CacheMigration] Failed to write cache layout version.");

                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best effort cleanup only.
                }

                return false;
            }
        }

        private static bool IsPrimaryLegacyPhysicalMigrationComplete(string pluginUserDataPath, ILogger logger)
        {
            try
            {
                var legacyDirectories = new[]
                {
                    Path.Combine(pluginUserDataPath, "AchievementCache"),
                    Path.Combine(pluginUserDataPath, "Hub Background Cache"),
                    Path.Combine(pluginUserDataPath, "Hub Cache"),
                    Path.Combine(pluginUserDataPath, "News Cache"),
                    Path.Combine(pluginUserDataPath, "ScreenshotCache"),
                    Path.Combine(pluginUserDataPath, "Steam Game News Images"),
                    Path.Combine(pluginUserDataPath, "SteamFriendCache"),
                    Path.Combine(pluginUserDataPath, "SteamStore", "StoreCache"),
                    Path.Combine(pluginUserDataPath, "SteamStore", "DetailsCache"),
                    Path.Combine(pluginUserDataPath, "SteamStore", "ImageCache"),
                    Path.Combine(pluginUserDataPath, "SteamStore", "UserCache"),
                    Path.Combine(pluginUserDataPath, "SteamStore", "RecommendedCache"),
                    Path.Combine(pluginUserDataPath, "SteamStore", "Recommended")
                };

                foreach (var directory in legacyDirectories)
                {
                    if (Directory.Exists(directory) &&
                        Directory.GetFileSystemEntries(directory).Length > 0)
                    {
                        return false;
                    }
                }

                var legacyFiles = new[]
                {
                    Path.Combine(pluginUserDataPath, "steam_updates_cache.json"),
                    Path.Combine(pluginUserDataPath, "steam_appid_mapping_cache.json"),
                    Path.Combine(pluginUserDataPath, "steam_game_news_cache.json"),
                    Path.Combine(pluginUserDataPath, "palette_cache_v2.json"),
                    Path.Combine(pluginUserDataPath, "palette_cache_v2.json.tmp"),
                    Path.Combine(pluginUserDataPath, "palette_cache.json"),
                    Path.Combine(pluginUserDataPath, "palette_cache.json.tmp"),
                    Path.Combine(pluginUserDataPath, "palette_cache_v1.json"),
                    Path.Combine(pluginUserDataPath, "palette_cache_v1.json.tmp")
                };

                foreach (var file in legacyFiles)
                {
                    if (File.Exists(file))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CacheMigration] Failed to verify the physical legacy cache migration.");
                return false;
            }
        }

        private static void MoveDirectoryContents(string sourceDirectory, string destinationDirectory, ILogger logger)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDirectory) ||
                    string.IsNullOrWhiteSpace(destinationDirectory) ||
                    !Directory.Exists(sourceDirectory) ||
                    string.Equals(
                        Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Directory.CreateDirectory(destinationDirectory);

                foreach (var sourceFile in Directory.GetFiles(sourceDirectory))
                {
                    MoveFilePreferNewest(
                        sourceFile,
                        Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)),
                        logger);
                }

                foreach (var sourceChildDirectory in Directory.GetDirectories(sourceDirectory))
                {
                    MoveDirectoryContents(
                        sourceChildDirectory,
                        Path.Combine(destinationDirectory, Path.GetFileName(sourceChildDirectory)),
                        logger);
                }

                TryDeleteDirectoryIfEmpty(sourceDirectory);
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    $"[AnikiHelper][CacheMigration] Failed to migrate directory '{sourceDirectory}' -> '{destinationDirectory}'.");
            }
        }

        private static void MoveFilePreferNewest(string sourcePath, string destinationPath, ILogger logger)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) ||
                    string.IsNullOrWhiteSpace(destinationPath) ||
                    !File.Exists(sourcePath))
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                if (!File.Exists(destinationPath))
                {
                    File.Move(sourcePath, destinationPath);
                    return;
                }

                // Both are cache files. Keep the newest copy so a partially migrated
                // installation cannot silently revert to older cached data.
                var sourceWriteUtc = File.GetLastWriteTimeUtc(sourcePath);
                var destinationWriteUtc = File.GetLastWriteTimeUtc(destinationPath);

                if (sourceWriteUtc > destinationWriteUtc)
                {
                    File.Delete(destinationPath);
                    File.Move(sourcePath, destinationPath);
                }
                else
                {
                    File.Delete(sourcePath);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    ex,
                    $"[AnikiHelper][CacheMigration] Failed to migrate file '{sourcePath}' -> '{destinationPath}'.");
            }
        }

        private static void TryDeleteDirectoryIfEmpty(string directory)
        {
            try
            {
                if (Directory.Exists(directory) &&
                    Directory.GetFileSystemEntries(directory).Length == 0)
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
