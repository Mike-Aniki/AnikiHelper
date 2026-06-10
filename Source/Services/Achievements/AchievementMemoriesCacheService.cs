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

        private readonly string cachePath;
        public string CachePath => cachePath;
        private readonly ILogger logger;

        public AchievementMemoriesCacheService(string pluginUserDataPath, ILogger logger)
        {
            this.logger = logger;

            var cacheRoot = Path.Combine(pluginUserDataPath, "AchievementCache");
            Directory.CreateDirectory(cacheRoot);

            cachePath = Path.Combine(cacheRoot, "achievement_memories_cache.json");
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

        public void Save(IEnumerable<AnikiAchievementMemoryItem> items)
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
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save achievement memories cache.");
            }
        }
    }
}