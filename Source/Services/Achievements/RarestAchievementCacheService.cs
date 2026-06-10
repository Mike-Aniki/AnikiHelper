using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.IO;

namespace AnikiHelper.Services.Achievements
{
    internal sealed class RarestAchievementCacheService
    {
        private readonly string cachePath;
        private readonly ILogger logger;

        public string CachePath => cachePath;

        public RarestAchievementCacheService(string pluginUserDataPath, ILogger logger)
        {
            this.logger = logger;

            var cacheRoot = Path.Combine(pluginUserDataPath, "AchievementCache");
            Directory.CreateDirectory(cacheRoot);

            cachePath = Path.Combine(cacheRoot, "rarest_achievement_cache.json");
        }

        public AnikiAchievementMemoryItem Load()
        {
            try
            {
                if (!File.Exists(cachePath))
                    return null;

                return Serialization.FromJsonFile<AnikiAchievementMemoryItem>(cachePath);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load rarest achievement cache.");
                return null;
            }
        }

        public void Save(AnikiAchievementMemoryItem item)
        {
            try
            {
                if (item == null)
                    return;

                File.WriteAllText(cachePath, Serialization.ToJson(item, true));
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save rarest achievement cache.");
            }
        }
    }
}