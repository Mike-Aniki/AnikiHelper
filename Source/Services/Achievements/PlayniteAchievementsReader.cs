using Playnite.SDK;
using Playnite.SDK.Models;
using SqlNado;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.Achievements
{
    internal sealed class PlayniteAchievementsReader
    {
        private const string PlayniteAchievementsPluginId = "PlayniteAchievements";

        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;

        public PlayniteAchievementsReader(IPlayniteAPI playniteApi, ILogger logger)
        {
            this.playniteApi = playniteApi;
            this.logger = logger;
        }

        public PlayniteAchievementsSummary LoadSummary(Game game)
        {
            if (game == null || game.Id == Guid.Empty)
            {
                if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Info("[AnikiHelper][Achievements][LoadSummary][STOP] Game is null or empty.");
                }

                return null;
            }

            try
            {
                var dbPath = FindDatabasePath();

                if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Info(
                        "[AnikiHelper][Achievements][LoadSummary][START] " +
                        "Game='" + game.Name + "' Id=" + game.Id +
                        " | DB path='" + (dbPath ?? "null") + "'"
                    );
                }

                if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
                {
                    if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                    {
                        logger?.Info(
                            "[AnikiHelper][Achievements][LoadSummary][STOP] DB missing. " +
                            "Game='" + game.Name + "'"
                        );
                    }

                    return null;
                }

                using (var db = new SQLiteDatabase(
                    dbPath,
                    SQLiteOpenOptions.SQLITE_OPEN_READONLY |
                    SQLiteOpenOptions.SQLITE_OPEN_FULLMUTEX))
                {
                    var progress = db.Load<ProgressRow>(
                        @"SELECT
                            ugp.Id AS UserGameProgressId,
                            ugp.GameId AS GameId,
                            ugp.AchievementsUnlocked AS AchievementsUnlocked,
                            ugp.TotalAchievements AS TotalAchievements
                          FROM UserGameProgress ugp
                          INNER JOIN Users u ON u.Id = ugp.UserId
                          INNER JOIN Games g ON g.Id = ugp.GameId
                          WHERE u.IsCurrentUser = 1
                            AND g.PlayniteGameId = ?
                          ORDER BY ugp.LastUpdatedUtc DESC
                          LIMIT 1;",
                        game.Id.ToString()).FirstOrDefault();

                    if (progress == null || progress.TotalAchievements <= 0)
                    {
                        if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                        {
                            logger?.Info(
                                "[AnikiHelper][Achievements][LoadSummary][STOP] No progress found. " +
                                "Game='" + game.Name + "'"
                            );
                        }

                        return null;
                    }

                    var lastUnlocked = db.Load<AchievementRow>(
                        @"SELECT
                            ad.DisplayName AS DisplayName,
                            ad.Description AS Description,
                            ad.UnlockedIconPath AS UnlockedIconPath,
                            ad.GlobalPercentUnlocked AS GlobalPercentUnlocked,
                            ad.Rarity AS Rarity,
                            ua.UnlockTimeUtc AS UnlockTimeUtc
                          FROM UserAchievements ua
                          INNER JOIN AchievementDefinitions ad
                            ON ad.Id = ua.AchievementDefinitionId
                          WHERE ua.UserGameProgressId = ?
                            AND ua.Unlocked = 1
                          ORDER BY ua.UnlockTimeUtc DESC
                          LIMIT 1;",
                        progress.UserGameProgressId).FirstOrDefault();

                    if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                    {
                        logger?.Info(
                            "[AnikiHelper][Achievements][LoadSummary][RESULT] " +
                            "Game='" + game.Name + "'" +
                            " | Unlocked=" + progress.AchievementsUnlocked +
                            " | Total=" + progress.TotalAchievements +
                            " | LastUnlocked='" + (lastUnlocked?.DisplayName ?? "") + "'"
                        );
                    }

                    return new PlayniteAchievementsSummary
                    {
                        Total = (int)progress.TotalAchievements,
                        Unlocked = (int)progress.AchievementsUnlocked,
                        LastUnlockedTitle = lastUnlocked?.DisplayName ?? string.Empty,
                        LastUnlockedDescription = lastUnlocked?.Description ?? string.Empty,
                        LastUnlockedIconPath = ResolveIconPath(dbPath, lastUnlocked?.UnlockedIconPath),
                        LastUnlockedPercent = lastUnlocked?.GlobalPercentUnlocked,
                        LastUnlockedRarity = lastUnlocked?.Rarity ?? string.Empty,
                        LastUnlockedUtc = ParseDate(lastUnlocked?.UnlockTimeUtc)
                    };
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to read PlayniteAchievements database.");
                return null;
            }
        }

        public List<AnikiAchievementMemoryItem> LoadAchievementMemories(int maxItems = 5000)
        {
            var result = new List<AnikiAchievementMemoryItem>();

            try
            {
                var dbPath = FindDatabasePath();
                if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Info("[AnikiHelper][Achievements][LoadMemories][START] DB path='" + (dbPath ?? "null") + "' MaxItems=" + maxItems);
                }
                if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
                {
                    if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                    {
                        logger?.Info("[AnikiHelper][Achievements][LoadMemories][STOP] DB missing.");
                    }

                    return result;
                }

                using (var db = new SQLiteDatabase(
                    dbPath,
                    SQLiteOpenOptions.SQLITE_OPEN_READONLY |
                    SQLiteOpenOptions.SQLITE_OPEN_FULLMUTEX))
                {
                    var rows = db.Load<AchievementMemoryRow>(
                        @"SELECT
                    g.PlayniteGameId AS PlayniteGameId,
                    '' AS GameName,
                    ad.DisplayName AS Title,
                    ad.Description AS Description,
                    ad.UnlockedIconPath AS IconPath,
                    ad.GlobalPercentUnlocked AS Percent,
                    ad.Rarity AS Rarity,
                    ua.UnlockTimeUtc AS UnlockTimeUtc
                  FROM UserAchievements ua
                  INNER JOIN AchievementDefinitions ad
                    ON ad.Id = ua.AchievementDefinitionId
                  INNER JOIN UserGameProgress ugp
                    ON ugp.Id = ua.UserGameProgressId
                  INNER JOIN Games g
                    ON g.Id = ugp.GameId
                  INNER JOIN Users u
                    ON u.Id = ugp.UserId
                  WHERE u.IsCurrentUser = 1
                    AND ua.Unlocked = 1
                    AND ua.UnlockTimeUtc IS NOT NULL
                  ORDER BY ua.UnlockTimeUtc DESC
                  LIMIT ?;",
                        maxItems).ToList();

                    if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                    {
                        logger?.Info("[AnikiHelper][Achievements][LoadMemories][Rows] Rows loaded=" + (rows?.Count ?? 0));
                    }

                    foreach (var row in rows)
                    {
                        if (row == null)
                        {
                            continue;
                        }

                        Guid gameId;
                        if (!Guid.TryParse(row.PlayniteGameId, out gameId))
                        {
                            continue;
                        }

                        var unlockDate = ParseDate(row.UnlockTimeUtc);
                        if (!unlockDate.HasValue)
                        {
                            continue;
                        }

                        result.Add(new AnikiAchievementMemoryItem
                        {
                            GameId = gameId,
                            GameName = GetGameNameFromPlaynite(gameId),
                            GameBackgroundPath = GetGameBackgroundPath(gameId),
                            Title = row.Title ?? string.Empty,
                            Description = row.Description ?? string.Empty,
                            IconPath = ResolveIconPath(dbPath, row.IconPath),
                            Percent = row.Percent,
                            Rarity = row.Rarity ?? string.Empty,
                            UnlockDate = unlockDate.Value
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load achievement memories from PlayniteAchievements.");
            }

            if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
            {
                logger?.Info("[AnikiHelper][Achievements][LoadMemories][RESULT] Items returned=" + result.Count);
            }

            return result;
        }

        public AnikiAchievementMemoryItem LoadRarestAchievementAllTime()
        {
            try
            {
                var dbPath = FindDatabasePath();

                if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Info("[AnikiHelper][Achievements][LoadRarestAllTime][START] DB path='" + (dbPath ?? "null") + "'");
                }

                if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
                {
                    return null;
                }

                using (var db = new SQLiteDatabase(
                    dbPath,
                    SQLiteOpenOptions.SQLITE_OPEN_READONLY |
                    SQLiteOpenOptions.SQLITE_OPEN_FULLMUTEX))
                {
                    var row = db.Load<AchievementMemoryRow>(
                        @"SELECT
                    g.PlayniteGameId AS PlayniteGameId,
                    '' AS GameName,
                    ad.DisplayName AS Title,
                    ad.Description AS Description,
                    ad.UnlockedIconPath AS IconPath,
                    ad.GlobalPercentUnlocked AS Percent,
                    ad.Rarity AS Rarity,
                    ua.UnlockTimeUtc AS UnlockTimeUtc
                  FROM UserAchievements ua
                  INNER JOIN AchievementDefinitions ad
                    ON ad.Id = ua.AchievementDefinitionId
                  INNER JOIN UserGameProgress ugp
                    ON ugp.Id = ua.UserGameProgressId
                  INNER JOIN Games g
                    ON g.Id = ugp.GameId
                  INNER JOIN Users u
                    ON u.Id = ugp.UserId
                  WHERE u.IsCurrentUser = 1
                    AND ua.Unlocked = 1
                    AND ua.UnlockTimeUtc IS NOT NULL
                    AND ad.GlobalPercentUnlocked IS NOT NULL
                  ORDER BY ad.GlobalPercentUnlocked ASC, ua.UnlockTimeUtc DESC
                  LIMIT 1;")
                        .FirstOrDefault();

                    if (row == null)
                    {
                        return null;
                    }

                    Guid gameId;
                    if (!Guid.TryParse(row.PlayniteGameId, out gameId))
                    {
                        return null;
                    }

                    var unlockDate = ParseDate(row.UnlockTimeUtc);
                    if (!unlockDate.HasValue)
                    {
                        return null;
                    }

                    var item = new AnikiAchievementMemoryItem
                    {
                        GameId = gameId,
                        GameName = GetGameNameFromPlaynite(gameId),
                        GameBackgroundPath = GetGameBackgroundPath(gameId),
                        Title = row.Title ?? string.Empty,
                        Description = row.Description ?? string.Empty,
                        IconPath = ResolveIconPath(dbPath, row.IconPath),
                        Percent = row.Percent,
                        Rarity = row.Rarity ?? string.Empty,
                        UnlockDate = unlockDate.Value
                    };

                    if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                    {
                        logger?.Info(
                            "[AnikiHelper][Achievements][LoadRarestAllTime][RESULT] " +
                            "Game='" + item.GameName + "'" +
                            " | Title='" + item.Title + "'" +
                            " | Percent=" + (item.Percent.HasValue ? item.Percent.Value.ToString("0.##") : "null")
                        );
                    }

                    return item;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load rarest PlayniteAchievements achievement all time.");
                return null;
            }
        }

        public List<AnikiAchievementMemoryItem> LoadAchievementMemoriesForGame(Guid gameId)
        {
            var result = new List<AnikiAchievementMemoryItem>();

            try
            {
                var dbPath = FindDatabasePath();

                if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Info(
                        "[AnikiHelper][Achievements][LoadMemoriesForGame][START] " +
                        "GameId=" + gameId +
                        " | DB path='" + (dbPath ?? "null") + "'"
                    );
                }

                if (string.IsNullOrWhiteSpace(dbPath))
                {
                    if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                    {
                        logger?.Info("[AnikiHelper][Achievements][LoadMemoriesForGame][STOP] DB missing. GameId=" + gameId);
                    }

                    return result;
                }

                using (var db = new SQLiteDatabase(
                    dbPath,
                    SQLiteOpenOptions.SQLITE_OPEN_READONLY |
                    SQLiteOpenOptions.SQLITE_OPEN_FULLMUTEX))
                {
                    var rows = db.Load<AchievementMemoryRow>(
                        @"SELECT
                    ad.DisplayName AS Title,
                    ad.Description AS Description,
                    ad.UnlockedIconPath AS IconPath,
                    ad.GlobalPercentUnlocked AS Percent,
                    ad.Rarity AS Rarity,
                    ua.UnlockTimeUtc AS UnlockTimeUtc
                  FROM UserAchievements ua
                  INNER JOIN AchievementDefinitions ad
                    ON ad.Id = ua.AchievementDefinitionId
                  INNER JOIN UserGameProgress ugp
                    ON ugp.Id = ua.UserGameProgressId
                  INNER JOIN Games g
                    ON g.Id = ugp.GameId
                  WHERE g.PlayniteGameId = ?
                    AND ua.Unlocked = 1
                    AND ua.UnlockTimeUtc IS NOT NULL
                  ORDER BY ua.UnlockTimeUtc DESC;",
                        gameId.ToString()).ToList();

                    if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                    {
                        logger?.Info("[AnikiHelper][Achievements][LoadMemoriesForGame][Rows] GameId=" + gameId + " Rows loaded=" + (rows?.Count ?? 0));
                    }

                    var game = playniteApi.Database.Games.Get(gameId);

                    foreach (var row in rows)
                    {
                        var unlockDate = ParseDate(row.UnlockTimeUtc);

                        if (!unlockDate.HasValue)
                        {
                            continue;
                        }

                        result.Add(new AnikiAchievementMemoryItem
                        {
                            GameId = gameId,
                            GameName = game?.Name ?? string.Empty,
                            GameBackgroundPath = GetGameBackgroundPath(gameId),
                            Title = row.Title,
                            Description = row.Description,
                            IconPath = ResolveIconPath(dbPath, row.IconPath),
                            Percent = row.Percent,
                            Rarity = row.Rarity,
                            UnlockDate = unlockDate.Value
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load achievement memories for game.");
            }

            if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
            {
                logger?.Info("[AnikiHelper][Achievements][LoadMemoriesForGame][RESULT] GameId=" + gameId + " Items returned=" + result.Count);
            }

            return result;
        }

        private string GetGameNameFromPlaynite(Guid gameId)
        {
            try
            {
                var game = playniteApi?.Database?.Games?.Get(gameId);
                return game?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void RefreshDisplayData(AnikiAchievementMemoryItem item)
        {
            if (item == null || item.GameId == Guid.Empty)
            {
                return;
            }

            var oldBg = item.GameBackgroundPath;

            item.GameName = GetGameNameFromPlaynite(item.GameId);
            item.GameBackgroundPath = GetGameBackgroundPath(item.GameId);

            if (AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
            {
                logger?.Info(
                    "[AnikiHelper][Achievements][MemoryDisplayRefresh] " +
                    "Game='" + item.GameName + "'" +
                    " | cacheBg='" + (oldBg ?? "") + "'" +
                    " | playniteBg='" + (item.GameBackgroundPath ?? "") + "'"
                );
            }
        }

        private string GetGameBackgroundPath(Guid gameId)
        {
            try
            {
                var game = playniteApi?.Database?.Games?.Get(gameId);
                if (game == null)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(game.BackgroundImage) &&
                    !game.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return playniteApi.Database.GetFullFilePath(game.BackgroundImage);
                }

                if (!string.IsNullOrWhiteSpace(game.CoverImage) &&
                    !game.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return playniteApi.Database.GetFullFilePath(game.CoverImage);
                }

                if (!string.IsNullOrWhiteSpace(game.Icon) &&
                    !game.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return playniteApi.Database.GetFullFilePath(game.Icon);
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private string FindDatabasePath()
        {
            try
            {
                var root = playniteApi?.Paths?.ExtensionsDataPath;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return null;
                }

                var candidates = Directory
                    .EnumerateFiles(root, "*.db", SearchOption.AllDirectories)
                    .Where(x =>
                        x.IndexOf("PlayniteAchievements", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.IndexOf("Achievements", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.IndexOf("achievement", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                return candidates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to search PlayniteAchievements database.");
                return null;
            }
        }

        private DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            DateTime parsed;
            if (DateTime.TryParse(value, out parsed))
            {
                return parsed;
            }

            return null;
        }

        private string ResolveIconPath(string dbPath, string iconPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(iconPath))
                {
                    return string.Empty;
                }

                if (Path.IsPathRooted(iconPath))
                {
                    return File.Exists(iconPath) ? iconPath : string.Empty;
                }

                if (iconPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return iconPath;
                }

                var pluginDataPath = Path.GetDirectoryName(dbPath);
                if (string.IsNullOrWhiteSpace(pluginDataPath))
                {
                    return string.Empty;
                }

                var fullPath = Path.Combine(pluginDataPath, iconPath);

                return File.Exists(fullPath) ? fullPath : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class ProgressRow
        {
            public long UserGameProgressId { get; set; }
            public long GameId { get; set; }
            public long AchievementsUnlocked { get; set; }
            public long TotalAchievements { get; set; }
        }

        private sealed class AchievementRow
        {
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string UnlockedIconPath { get; set; }
            public double? GlobalPercentUnlocked { get; set; }
            public string Rarity { get; set; }
            public string UnlockTimeUtc { get; set; }
        }

        private sealed class AchievementMemoryRow
        {
            public string PlayniteGameId { get; set; }
            public string GameName { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string IconPath { get; set; }
            public double? Percent { get; set; }
            public string Rarity { get; set; }
            public string UnlockTimeUtc { get; set; }
        }
    }

    internal sealed class PlayniteAchievementsSummary
    {
        public int Unlocked { get; set; }
        public int Total { get; set; }
        public string LastUnlockedTitle { get; set; }
        public string LastUnlockedDescription { get; set; }
        public string LastUnlockedIconPath { get; set; }
        public double? LastUnlockedPercent { get; set; }
        public string LastUnlockedRarity { get; set; }
        public DateTime? LastUnlockedUtc { get; set; }
    }
}