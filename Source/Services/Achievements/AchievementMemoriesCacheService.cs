using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.Achievements
{
    internal sealed class AchievementMemoriesCacheService
    {
        private const int MaxItems = 5000;

        // Version 2 = réparation du cache des icônes de trophées.
        // Plus tard, si tu changes encore la logique du cache, tu passes à 3.
        private const int CacheSchemaVersion = 2;

        private readonly string cachePath;
        private readonly string cacheVersionPath;
        private readonly ILogger logger;

        public string CachePath => cachePath;

        public AchievementMemoriesCacheService(string pluginUserDataPath, ILogger logger)
        {
            this.logger = logger;

            var cacheRoot = Path.Combine(pluginUserDataPath, "AchievementCache");
            Directory.CreateDirectory(cacheRoot);

            cachePath = Path.Combine(cacheRoot, "achievement_memories_cache.json");
            cacheVersionPath = Path.Combine(cacheRoot, "achievement_memories_cache.version");
        }

        public bool NeedsRebuildForCurrentVersion()
        {
            try
            {
                if (!File.Exists(cachePath))
                {
                    return true;
                }

                if (!File.Exists(cacheVersionPath))
                {
                    return true;
                }

                var rawVersion = File.ReadAllText(cacheVersionPath).Trim();

                int version;
                if (!int.TryParse(rawVersion, out version))
                {
                    return true;
                }

                return version < CacheSchemaVersion;
            }
            catch
            {
                return true;
            }
        }

        public List<AnikiAchievementMemoryItem> Load()
        {
            try
            {
                if (!File.Exists(cachePath))
                {
                    return new List<AnikiAchievementMemoryItem>();
                }

                return Serialization.FromJsonFile<List<AnikiAchievementMemoryItem>>(cachePath)
                    ?? new List<AnikiAchievementMemoryItem>();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load achievement memories cache.");
                return new List<AnikiAchievementMemoryItem>();
            }
        }

        public void Save(IEnumerable<AnikiAchievementMemoryItem> items, bool markCurrentVersion = false)
        {
            try
            {
                var list = (items ?? Enumerable.Empty<AnikiAchievementMemoryItem>())
                    .Where(x => x != null)
                    .Where(x => x.GameId != Guid.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Title))
                    .OrderByDescending(x => x.UnlockDate)
                    .Take(MaxItems)
                    .ToList();

                File.WriteAllText(cachePath, Serialization.ToJson(list, true));

                if (markCurrentVersion)
                {
                    File.WriteAllText(cacheVersionPath, CacheSchemaVersion.ToString());
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save achievement memories cache.");
            }
        }
    }
}