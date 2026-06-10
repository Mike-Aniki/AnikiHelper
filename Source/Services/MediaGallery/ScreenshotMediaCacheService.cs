using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.MediaGallery
{
    public class ScreenshotMediaCacheService
    {
        private const int LatestMediaMaxItems = 4;
        private const int MemoriesMaxGroups = 80;
        private const int MemoryScreenshotsMaxItems = 4;

        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;
        private readonly string cacheRoot;

        public ScreenshotMediaCacheService(IPlayniteAPI playniteApi, string pluginUserDataPath, ILogger logger)
        {
            this.playniteApi = playniteApi;
            this.logger = logger;

            cacheRoot = Path.Combine(pluginUserDataPath, "ScreenshotCache");

            try
            {
                Directory.CreateDirectory(cacheRoot);
                Directory.CreateDirectory(Path.Combine(cacheRoot, "Thumbnails"));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to create ScreenshotCache folders.");
            }
        }

        public string LatestMediaCachePath
        {
            get
            {
                return Path.Combine(cacheRoot, "media_latest_cache.json");
            }
        }

        public string GamesCachePath
        {
            get
            {
                return Path.Combine(cacheRoot, "media_games_cache.json");
            }
        }

        public string MemoriesCachePath
        {
            get
            {
                return Path.Combine(cacheRoot, "media_memories_cache.json");
            }
        }

        public List<AnikiMediaItem> LoadLatestMediaCache()
        {
            try
            {
                if (!File.Exists(LatestMediaCachePath))
                {
                    return new List<AnikiMediaItem>();
                }

                var items = Serialization.FromJsonFile<List<AnikiMediaItem>>(LatestMediaCachePath);
                return items ?? new List<AnikiMediaItem>();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load latest media cache.");
                return new List<AnikiMediaItem>();
            }
        }

        public List<AnikiMediaGameItem> LoadGamesCache()
        {
            try
            {
                if (!File.Exists(GamesCachePath))
                {
                    return new List<AnikiMediaGameItem>();
                }

                var items = Serialization.FromJsonFile<List<AnikiMediaGameItem>>(GamesCachePath);
                return items ?? new List<AnikiMediaGameItem>();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load media games cache.");
                return new List<AnikiMediaGameItem>();
            }
        }

        public List<AnikiMemoryGroup> LoadMemoriesCache()
        {
            try
            {
                if (!File.Exists(MemoriesCachePath))
                {
                    return new List<AnikiMemoryGroup>();
                }

                var items = Serialization.FromJsonFile<List<AnikiMemoryGroup>>(MemoriesCachePath);
                return items ?? new List<AnikiMemoryGroup>();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load memories cache.");
                return new List<AnikiMemoryGroup>();
            }
        }

        private void SaveMemoriesCache(IEnumerable<AnikiMemoryGroup> memories)
        {
            try
            {
                Directory.CreateDirectory(cacheRoot);

                var list = (memories ?? Enumerable.Empty<AnikiMemoryGroup>())
                    .Where(x => x != null)
                    .Where(x => x.GameId != Guid.Empty)
                    .Where(x => x.Screenshots != null && x.Screenshots.Count > 0)
                    .OrderByDescending(x => x.MemoryDate)
                    .Take(MemoriesMaxGroups)
                    .ToList();

                var json = Serialization.ToJson(list, true);
                File.WriteAllText(MemoriesCachePath, json);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save memories cache.");
            }
        }

        public void SaveLatestMediaCache(IEnumerable<AnikiMediaItem> allItems)
        {
            try
            {
                Directory.CreateDirectory(cacheRoot);

                var latestItems = (allItems ?? Enumerable.Empty<AnikiMediaItem>())
                    .Where(x => x != null)
                    .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                    .Where(x => File.Exists(x.FilePath))
                    .OrderByDescending(x => x.CaptureDate)
                    .Take(LatestMediaMaxItems)
                    .ToList();

                var json = Serialization.ToJson(latestItems, true);
                File.WriteAllText(LatestMediaCachePath, json);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save latest media cache.");
            }
        }

        public void SaveGamesCache(IEnumerable<AnikiMediaItem> allItems)
        {
            try
            {
                Directory.CreateDirectory(cacheRoot);

                var items = (allItems ?? Enumerable.Empty<AnikiMediaItem>())
                    .Where(x => x != null)
                    .Where(x => x.GameId != Guid.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                    .Where(x => File.Exists(x.FilePath))
                    .ToList();

                var games = items
                    .GroupBy(x => x.GameId)
                    .Select(group =>
                    {
                        var first = group.FirstOrDefault();
                        var game = GetGame(group.Key);

                        var latestDate = group.Max(x => x.CaptureDate);
                        var oldestDate = group.Min(x => x.CaptureDate);

                        return new AnikiMediaGameItem
                        {
                            GameId = group.Key,
                            GameName = game?.Name ?? first?.GameName ?? string.Empty,
                            CoverPath = GetGameCoverPath(game),
                            MediaCount = group.Count(),
                            ImageCount = group.Count(x => !x.IsVideo),
                            VideoCount = group.Count(x => x.IsVideo),
                            LatestCaptureDate = latestDate,
                            OldestCaptureDate = oldestDate,
                            SourceProvider = first?.SourceProvider ?? string.Empty
                        };
                    })
                    .OrderByDescending(x => x.LatestCaptureDate)
                    .ToList();

                var json = Serialization.ToJson(games, true);
                File.WriteAllText(GamesCachePath, json);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save media games cache.");
            }
        }

        public void RebuildCaches(IEnumerable<AnikiMediaItem> allItems)
        {
            var list = (allItems ?? Enumerable.Empty<AnikiMediaItem>()).ToList();

            SaveLatestMediaCache(list);
            SaveGamesCache(list);
            BuildMemoriesFromExistingMedia(list);
        }

        public void BuildMemoriesFromExistingMedia(IEnumerable<AnikiMediaItem> allItems)
        {
            try
            {
                var items = (allItems ?? Enumerable.Empty<AnikiMediaItem>())
                    .Where(x => x != null)
                    .Where(x => x.GameId != Guid.Empty)
                    .Where(x => !x.IsVideo)
                    .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                    .Where(x => File.Exists(x.FilePath))
                    .ToList();

                var memories = items
                    .GroupBy(x => new
                    {
                        x.GameId,
                        Date = x.CaptureDate.Date
                    })
                    .Select(group =>
                    {
                        var first = group.FirstOrDefault();
                        var game = GetGame(group.Key.GameId);

                        return new AnikiMemoryGroup
                        {
                            GameId = group.Key.GameId,
                            GameName = game?.Name ?? first?.GameName ?? string.Empty,
                            MemoryDate = group.Key.Date,
                            Screenshots = group
                                .OrderByDescending(x => x.CaptureDate)
                                .Take(MemoryScreenshotsMaxItems)
                                .ToList()
                        };
                    })
                    .Where(x => x.Screenshots.Count >= 4)
                    .OrderByDescending(x => x.MemoryDate)
                    .Take(MemoriesMaxGroups)
                    .ToList();

                SaveMemoriesCache(memories);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to build memories from existing media.");
            }
        }

        public void RebuildMemoriesForGame(Guid gameId, IEnumerable<AnikiMediaItem> gameItems)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return;
                }

                var newMemories = (gameItems ?? Enumerable.Empty<AnikiMediaItem>())
                    .Where(x => x != null)
                    .Where(x => x.GameId == gameId)
                    .Where(x => !x.IsVideo)
                    .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                    .Where(x => File.Exists(x.FilePath))
                    .GroupBy(x => x.CaptureDate.Date)
                    .Select(group =>
                    {
                        var first = group.FirstOrDefault();
                        var game = GetGame(gameId);

                        return new AnikiMemoryGroup
                        {
                            GameId = gameId,
                            GameName = game?.Name ?? first?.GameName ?? string.Empty,
                            MemoryDate = group.Key,
                            Screenshots = group
                                .OrderByDescending(x => x.CaptureDate)
                                .Take(MemoryScreenshotsMaxItems)
                                .ToList()
                        };
                    })
                    .Where(x => x.Screenshots.Count >= 4)
                    .ToList();

                var memories = LoadMemoriesCache()
                    .Where(x => x != null)
                    .Where(x => x.GameId != gameId)
                    .ToList();

                memories.AddRange(newMemories);

                SaveMemoriesCache(memories);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to rebuild memories for game.");
            }
        }

        public void UpdateMemoryFromSession(Guid gameId, IEnumerable<AnikiMediaItem> gameItems, DateTime sessionStart, DateTime sessionEnd)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return;
                }

                var sessionItems = (gameItems ?? Enumerable.Empty<AnikiMediaItem>())
                    .Where(x => x != null)
                    .Where(x => x.GameId == gameId)
                    .Where(x => !x.IsVideo)
                    .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                    .Where(x => File.Exists(x.FilePath))
                    .Where(x => x.CaptureDate >= sessionStart.AddSeconds(-10))
                    .Where(x => x.CaptureDate <= sessionEnd.AddSeconds(10))
                    .OrderByDescending(x => x.CaptureDate)
                    .Take(MemoryScreenshotsMaxItems)
                    .ToList();

                if (sessionItems.Count < 4)
                {
                    return;
                }

                var game = GetGame(gameId);
                var first = sessionItems.FirstOrDefault();

                var newMemory = new AnikiMemoryGroup
                {
                    GameId = gameId,
                    GameName = game?.Name ?? first?.GameName ?? string.Empty,
                    MemoryDate = sessionStart.Date,
                    Screenshots = sessionItems
                };

                var memories = LoadMemoriesCache();

                memories = memories
                    .Where(x => x != null)
                    .Where(x => !(x.GameId == gameId && x.MemoryDate.Date == newMemory.MemoryDate.Date))
                    .ToList();

                memories.Insert(0, newMemory);

                SaveMemoriesCache(memories);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to update memory from session.");
            }
        }

        public void UpdateGameInCaches(Guid gameId, IEnumerable<AnikiMediaItem> gameItems)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return;
                }

                Directory.CreateDirectory(cacheRoot);

                var items = (gameItems ?? Enumerable.Empty<AnikiMediaItem>())
                    .Where(x => x != null)
                    .Where(x => x.GameId == gameId)
                    .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                    .Where(x => File.Exists(x.FilePath))
                    .OrderByDescending(x => x.CaptureDate)
                    .ToList();

                var latestItems = LoadLatestMediaCache()
                    .Where(x => x != null && x.GameId != gameId)
                    .ToList();

                latestItems.AddRange(items);

                var latestJson = Serialization.ToJson(
                    latestItems
                        .Where(x => x != null)
                        .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                        .Where(x => File.Exists(x.FilePath))
                        .OrderByDescending(x => x.CaptureDate)
                        .Take(LatestMediaMaxItems)
                        .ToList(),
                    true
                );

                File.WriteAllText(LatestMediaCachePath, latestJson);

                var games = LoadGamesCache()
                    .Where(x => x != null && x.GameId != gameId)
                    .ToList();

                if (items.Count > 0)
                {
                    var game = GetGame(gameId);
                    var first = items.FirstOrDefault();

                    games.Add(new AnikiMediaGameItem
                    {
                        GameId = gameId,
                        GameName = game?.Name ?? first?.GameName ?? string.Empty,
                        CoverPath = GetGameCoverPath(game),
                        MediaCount = items.Count,
                        ImageCount = items.Count(x => !x.IsVideo),
                        VideoCount = items.Count(x => x.IsVideo),
                        LatestCaptureDate = items.Max(x => x.CaptureDate),
                        OldestCaptureDate = items.Min(x => x.CaptureDate),
                        SourceProvider = first?.SourceProvider ?? string.Empty
                    });
                }

                var gamesJson = Serialization.ToJson(
                    games
                        .OrderByDescending(x => x.LatestCaptureDate)
                        .ToList(),
                    true
                );

                File.WriteAllText(GamesCachePath, gamesJson);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to update media cache for game.");
            }
        }

        private Game GetGame(Guid gameId)
        {
            try
            {
                if (gameId == Guid.Empty)
                {
                    return null;
                }

                return playniteApi?.Database?.Games?.Get(gameId);
            }
            catch
            {
                return null;
            }
        }

        private string GetGameCoverPath(Game game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(game.CoverImage))
                {
                    var path = playniteApi?.Database?.GetFullFilePath(game.CoverImage);
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }

                if (!string.IsNullOrWhiteSpace(game.BackgroundImage))
                {
                    var path = playniteApi?.Database?.GetFullFilePath(game.BackgroundImage);
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }

                if (!string.IsNullOrWhiteSpace(game.Icon))
                {
                    var path = playniteApi?.Database?.GetFullFilePath(game.Icon);
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}