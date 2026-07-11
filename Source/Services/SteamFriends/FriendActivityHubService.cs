using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AnikiHelper.Services.SteamFriends
{
    public class FriendActivityHubService : IDisposable
    {
        private static readonly Guid FriendsAchievementFeedPluginId = Guid.Parse("10f90193-72aa-4cdb-b16d-3e6b1f0feb17");
        private const string FriendsAchievementFeedCacheFileName = "friend_achievement_cache.json";
        private const int MaxRecentPlayedFriendsPerDailyRefresh = 20;
        private const int RecentPlayedGamesPerFriend = 5;
        private const int MaxAchievementAgeDays = 14;

        private readonly AnikiHelperSettings settings;
        private readonly SteamFriendsWebApiClient steamClient;
        private readonly ILogger logger;
        private readonly string cacheDir;
        private readonly string recentPlayedCachePath;
        private readonly string friendsAchievementFeedCachePath;
        private readonly SteamFriendsGameImageResolver gameImageResolver;

        private readonly object sync = new object();
        private FriendActivityRecentPlayedCache recentPlayedCache = new FriendActivityRecentPlayedCache();
        private List<FriendAchievementFeedEntry> achievementEntries = new List<FriendAchievementFeedEntry>();
        private DateTime lastAchievementCacheWriteUtc = DateTime.MinValue;
        private bool recentPlayedRefreshRunning;
        private string lastHubSignature;
        private readonly Dictionary<int, string> steamAppTypeCache = new Dictionary<int, string>();

        private List<FriendPresenceDto> lastPresenceSnapshot = new List<FriendPresenceDto>();
        private List<string> lastFriendIdsSnapshot = new List<string>();

        public FriendActivityHubService(
            AnikiHelperSettings settings,
            SteamFriendsWebApiClient steamClient,
            string pluginUserDataPath,
            string extensionsDataPath,
            ILogger logger,
            SteamFriendsGameImageResolver gameImageResolver)
        {
            this.settings = settings;
            this.steamClient = steamClient;
            this.logger = logger ?? LogManager.GetLogger();
            this.gameImageResolver = gameImageResolver;

            cacheDir = Path.Combine(pluginUserDataPath, "SteamFriendCache", "FriendActivityHubCache");
            Directory.CreateDirectory(cacheDir);

            recentPlayedCachePath = Path.Combine(cacheDir, "recent_played_daily.json");
            friendsAchievementFeedCachePath = Path.Combine(
                extensionsDataPath,
                FriendsAchievementFeedPluginId.ToString(),
                FriendsAchievementFeedCacheFileName);

            settings?.EnsureFriendActivityHubRuntimeCollections();
            LoadRecentPlayedCacheFromDisk();
            ReloadAchievementCacheIfChanged();
        }

        public void ClearUnavailable()
        {
            InvokeOnUi(() =>
            {
                settings.ShowHubFriendActivityPage = false;
                ReplaceHubItems(new List<FriendActivityHubItem>());
            });
        }

        public void UpdateAfterPresenceRefresh(
            string apiKey,
            string selfSteamId64,
            IList<string> friendIds,
            IList<FriendPresenceDto> presences)
        {
            try
            {
                var safeFriendIds = friendIds?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();

                var safePresences = presences?
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.steamid))
                    .Select(ClonePresence)
                    .ToList() ?? new List<FriendPresenceDto>();

                lock (sync)
                {
                    lastPresenceSnapshot = safePresences;
                    lastFriendIdsSnapshot = safeFriendIds;
                }

                if (settings?.SteamFriendsEnabled != true ||
                    string.IsNullOrWhiteSpace(apiKey) ||
                    string.IsNullOrWhiteSpace(selfSteamId64) ||
                    safeFriendIds.Count == 0)
                {
                    ClearUnavailable();
                    return;
                }

                ReloadAchievementCacheIfChanged();
                RebuildHubItems(safeFriendIds, safePresences, showPage: true);
                StartDailyRecentPlayedRefreshIfNeeded(apiKey, safeFriendIds, safePresences);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][FriendActivityHub] UpdateAfterPresenceRefresh failed.");
            }
        }

        public void Dispose()
        {
        }

        private void StartDailyRecentPlayedRefreshIfNeeded(
            string apiKey,
            List<string> friendIds,
            List<FriendPresenceDto> presences)
        {
            if (!ShouldRefreshRecentPlayedToday())
            {
                return;
            }

            lock (sync)
            {
                if (recentPlayedRefreshRunning)
                {
                    return;
                }

                recentPlayedRefreshRunning = true;
            }

            Task.Run(async () =>
            {
                try
                {
                    await RefreshRecentPlayedDailyAsync(apiKey, friendIds, presences).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper][FriendActivityHub] Daily recent played refresh failed.");
                }
                finally
                {
                    lock (sync)
                    {
                        recentPlayedRefreshRunning = false;
                    }
                }
            });
        }

        private bool ShouldRefreshRecentPlayedToday()
        {
            lock (sync)
            {
                if (recentPlayedCache == null || recentPlayedCache.lastRefreshUtc == DateTime.MinValue)
                {
                    return true;
                }

                return recentPlayedCache.lastRefreshUtc.Date < DateTime.UtcNow.Date;
            }
        }

        private async Task RefreshRecentPlayedDailyAsync(
            string apiKey,
            List<string> friendIds,
            List<FriendPresenceDto> presences)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || friendIds == null || friendIds.Count == 0)
            {
                return;
            }

            var presenceById = presences?
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.steamid))
                .GroupBy(p => p.steamid.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, FriendPresenceDto>(StringComparer.OrdinalIgnoreCase);

            var candidates = BuildRecentPlayedRefreshCandidates(friendIds, presences);
            var refreshedEntries = new List<FriendActivityRecentPlayedEntry>();
            var gate = new SemaphoreSlim(2, 2);
            var tasks = candidates.Select(async steamId =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var games = await steamClient.GetRecentlyPlayedGamesAsync(apiKey, steamId, RecentPlayedGamesPerFriend).ConfigureAwait(false);
                    var game = await SelectFirstAllowedRecentGameAsync(games).ConfigureAwait(false);
                    if (game == null || game.AppId <= 0 || string.IsNullOrWhiteSpace(game.Name))
                    {
                        return;
                    }

                    FriendPresenceDto presence;
                    presenceById.TryGetValue(steamId, out presence);

                    lock (refreshedEntries)
                    {
                        refreshedEntries.Add(new FriendActivityRecentPlayedEntry
                        {
                            friendSteamId = steamId,
                            friendName = !string.IsNullOrWhiteSpace(presence?.name) ? presence.name : steamId,
                            friendAvatar = presence?.avatar,
                            appid = game.AppId,
                            gameName = game.Name,
                            gameImage = GetSteamHeaderImageUrl(game.AppId),
                            playtime2WeeksMinutes = game.Playtime2Weeks,
                            playtime2WeeksDisplay = FormatMinutesToHours(game.Playtime2Weeks),
                            refreshedUtc = DateTime.UtcNow
                        });
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"[AnikiHelper][FriendActivityHub] Recently played refresh failed for '{steamId}'.");
                }
                finally
                {
                    try { gate.Release(); } catch { }
                }
            }).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var merged = MergeRecentPlayedEntries(refreshedEntries, presenceById);

            lock (sync)
            {
                recentPlayedCache = new FriendActivityRecentPlayedCache
                {
                    lastRefreshUtc = DateTime.UtcNow,
                    entries = merged
                };
            }

            SaveRecentPlayedCacheToDisk();

            List<string> latestFriendIds;
            List<FriendPresenceDto> latestPresences;
            lock (sync)
            {
                latestFriendIds = lastFriendIdsSnapshot?.ToList() ?? new List<string>();
                latestPresences = lastPresenceSnapshot?.Select(ClonePresence).ToList() ?? new List<FriendPresenceDto>();
            }

            RebuildHubItems(latestFriendIds, latestPresences, showPage: latestFriendIds.Count > 0);
        }

        private async Task<SteamRecentlyPlayedGame> SelectFirstAllowedRecentGameAsync(IEnumerable<SteamRecentlyPlayedGame> games)
        {
            if (games == null)
            {
                return null;
            }

            foreach (var game in games.Where(g => g != null && g.AppId > 0 && !string.IsNullOrWhiteSpace(g.Name)))
            {
                if (await IsSteamAppAllowedAsGameAsync(game.AppId, game.Name).ConfigureAwait(false))
                {
                    return game;
                }
            }

            return null;
        }

        private async Task<bool> IsSteamAppAllowedAsGameAsync(int appId, string appName)
        {
            if (appId <= 0 || SteamNonGameAppFilter.IsKnownNonGameSteamApp(appId, appName))
            {
                return false;
            }

            string cachedType = null;
            lock (sync)
            {
                steamAppTypeCache.TryGetValue(appId, out cachedType);
            }

            if (cachedType != null)
            {
                return !SteamNonGameAppFilter.IsNonGameSteamAppType(cachedType);
            }

            var appType = await steamClient.GetSteamAppTypeAsync(appId).ConfigureAwait(false);
            lock (sync)
            {
                steamAppTypeCache[appId] = appType ?? string.Empty;
            }

            return !SteamNonGameAppFilter.IsNonGameSteamAppType(appType);
        }

        private List<string> BuildRecentPlayedRefreshCandidates(List<string> friendIds, List<FriendPresenceDto> presences)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string> add = id =>
            {
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id.Trim()))
                {
                    result.Add(id.Trim());
                }
            };

            foreach (var p in presences?.Where(p => p != null && p.state == "ingame") ?? Enumerable.Empty<FriendPresenceDto>()) add(p.steamid);
            foreach (var p in presences?.Where(p => p != null && p.state != "offline") ?? Enumerable.Empty<FriendPresenceDto>()) add(p.steamid);

            List<FriendActivityRecentPlayedEntry> cached;
            lock (sync)
            {
                cached = recentPlayedCache?.entries?.ToList() ?? new List<FriendActivityRecentPlayedEntry>();
            }

            foreach (var e in cached) add(e?.friendSteamId);
            foreach (var id in friendIds ?? new List<string>()) add(id);

            return result.Take(MaxRecentPlayedFriendsPerDailyRefresh).ToList();
        }

        private List<FriendActivityRecentPlayedEntry> MergeRecentPlayedEntries(
            List<FriendActivityRecentPlayedEntry> refreshed,
            Dictionary<string, FriendPresenceDto> presenceById)
        {
            var merged = new List<FriendActivityRecentPlayedEntry>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in refreshed.OrderByDescending(e => e.refreshedUtc))
            {
                AddRecentPlayedEntry(merged, seenKeys, entry, presenceById);
            }

            List<FriendActivityRecentPlayedEntry> oldEntries;
            lock (sync)
            {
                oldEntries = recentPlayedCache?.entries?.ToList() ?? new List<FriendActivityRecentPlayedEntry>();
            }

            foreach (var entry in oldEntries.OrderByDescending(e => e.refreshedUtc))
            {
                AddRecentPlayedEntry(merged, seenKeys, entry, presenceById);
            }

            return merged.Take(40).ToList();
        }

        private void AddRecentPlayedEntry(
            List<FriendActivityRecentPlayedEntry> target,
            HashSet<string> seenKeys,
            FriendActivityRecentPlayedEntry entry,
            Dictionary<string, FriendPresenceDto> presenceById)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.friendSteamId) || entry.appid <= 0 ||
                SteamNonGameAppFilter.IsKnownNonGameSteamApp(entry.appid, entry.gameName))
            {
                return;
            }

            var key = entry.friendSteamId.Trim() + ":" + entry.appid;
            if (!seenKeys.Add(key))
            {
                return;
            }

            FriendPresenceDto presence;
            if (presenceById != null && presenceById.TryGetValue(entry.friendSteamId.Trim(), out presence))
            {
                if (!string.IsNullOrWhiteSpace(presence.name)) entry.friendName = presence.name;
                if (!string.IsNullOrWhiteSpace(presence.avatar)) entry.friendAvatar = presence.avatar;
            }

            if (string.IsNullOrWhiteSpace(entry.gameImage))
            {
                entry.gameImage = GetSteamHeaderImageUrl(entry.appid);
            }

            if (string.IsNullOrWhiteSpace(entry.playtime2WeeksDisplay))
            {
                entry.playtime2WeeksDisplay = FormatMinutesToHours(entry.playtime2WeeksMinutes);
            }

            target.Add(entry);
        }

        private void RebuildHubItems(List<string> friendIds, List<FriendPresenceDto> presences, bool showPage)
        {
            if (!showPage)
            {
                InvokeOnUi(() =>
                {
                    settings.ShowHubFriendActivityPage = false;
                    ReplaceHubItems(new List<FriendActivityHubItem>());
                });
                return;
            }

            var items = BuildFourCards(friendIds, presences);

            InvokeOnUi(() =>
            {
                settings.ShowHubFriendActivityPage = true;
                ReplaceHubItems(items);
            });
        }

        private List<FriendActivityHubItem> BuildFourCards(List<string> friendIds, List<FriendPresenceDto> presences)
        {
            var usedRecent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedPlaying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var result = new List<FriendActivityHubItem>();

            var recent1 = TakeRecentPlayed(usedRecent);
            result.Add(recent1 ?? CreatePlaceholder("Recent played", "No recent played activity yet."));

            var achievement = TakeRecentAchievement(friendIds, presences);
            if (achievement != null)
            {
                result.Add(achievement);
            }
            else
            {
                var recentFallback = TakeRecentPlayed(usedRecent);
                result.Add(recentFallback ?? CreatePlaceholder("Friend achievement", "No recent achievement in the feed yet."));
            }

            var playing1 = TakeCurrentlyPlaying(presences, usedPlaying);
            result.Add(playing1 ?? TakeRecentPlayed(usedRecent) ?? CreatePlaceholder("Currently playing", "No friend is currently in game."));

            var playing2 = TakeCurrentlyPlaying(presences, usedPlaying);
            result.Add(playing2 ?? TakeRecentPlayed(usedRecent) ?? CreatePlaceholder("Currently playing", "No second friend is currently in game."));

            return result.Take(4).ToList();
        }

        private FriendActivityHubItem TakeRecentPlayed(HashSet<string> usedRecent)
        {
            List<FriendActivityRecentPlayedEntry> entries;
            lock (sync)
            {
                entries = recentPlayedCache?.entries?.ToList() ?? new List<FriendActivityRecentPlayedEntry>();
            }

            foreach (var e in entries
                .Where(e => e != null && e.appid > 0 && !string.IsNullOrWhiteSpace(e.gameName) &&
                            !SteamNonGameAppFilter.IsKnownNonGameSteamApp(e.appid, e.gameName))
                .OrderByDescending(e => e.refreshedUtc))
            {
                var key = e.friendSteamId + ":" + e.appid;
                if (!usedRecent.Add(key))
                {
                    continue;
                }

                var subtitle = e.gameName;
                if (!string.IsNullOrWhiteSpace(e.playtime2WeeksDisplay) && e.playtime2WeeksMinutes > 0)
                {
                    subtitle += " · " + e.playtime2WeeksDisplay + " last 2 weeks";
                }

                return new FriendActivityHubItem
                {
                    slot = "RecentPlayed",
                    activityType = "recentplayed",
                    badgeText = "RECENTLY PLAYED",
                    title = (SafeText(e.friendName, "A friend") + " played recently"),
                    subtitle = subtitle,
                    friendName = e.friendName,
                    friendAvatar = e.friendAvatar,
                    footerIcon = e.friendAvatar,
                    friendSteamId = e.friendSteamId,
                    appid = e.appid,
                    gameName = e.gameName,
                    gameImage = e.gameImage,
                    isPlaceholder = false,
                    activityUtc = e.refreshedUtc
                };
            }

            return null;
        }

        private FriendActivityHubItem TakeRecentAchievement(List<string> friendIds, List<FriendPresenceDto> presences)
        {
            var allowedFriends = new HashSet<string>(friendIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var minUtc = DateTime.UtcNow.AddDays(-MaxAchievementAgeDays);

            List<FriendAchievementFeedEntry> entries;
            lock (sync)
            {
                entries = achievementEntries?.ToList() ?? new List<FriendAchievementFeedEntry>();
            }

            var entry = entries
                .Select(e => new { Entry = e, UnlockUtc = ParseFriendAchievementDateUtc(e?.FriendUnlockTimeUtc) })
                .Where(x => x.Entry != null)
                .Where(x => !string.IsNullOrWhiteSpace(x.Entry.FriendSteamId))
                .Where(x => allowedFriends.Count == 0 || allowedFriends.Contains(x.Entry.FriendSteamId.Trim()))
                .Where(x => x.UnlockUtc.HasValue && x.UnlockUtc.Value >= minUtc)
                .OrderByDescending(x => x.UnlockUtc.Value)
                .FirstOrDefault();

            if (entry == null)
            {
                return null;
            }

            var icon = entry.Entry.FriendAchievementIcon;
            if (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon))
            {
                icon = ToFileUri(icon);
            }

            FriendPresenceDto presence = null;
            if (presences != null && !string.IsNullOrWhiteSpace(entry.Entry.FriendSteamId))
            {
                presence = presences.FirstOrDefault(p => p != null &&
                                                        string.Equals(p.steamid, entry.Entry.FriendSteamId.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            var friendName = !string.IsNullOrWhiteSpace(entry.Entry.FriendPersonaName)
                ? entry.Entry.FriendPersonaName
                : presence?.name;

            var friendAvatar = !string.IsNullOrWhiteSpace(entry.Entry.FriendAvatarUrl)
                ? entry.Entry.FriendAvatarUrl
                : presence?.avatar;

            var gameImage = GetSteamHeaderImageUrl(entry.Entry.AppId);
            var achievementName = SafeText(entry.Entry.AchievementDisplayName, entry.Entry.AchievementApiName);
            var subtitle = achievementName;
            if (!string.IsNullOrWhiteSpace(entry.Entry.GameName))
            {
                subtitle += " · " + entry.Entry.GameName;
            }

            return new FriendActivityHubItem
            {
                slot = "Achievement",
                activityType = "achievement",
                badgeText = "TROPHY",
                title = SafeText(friendName, "A friend") + " unlocked an achievement",
                subtitle = subtitle,
                friendName = friendName,
                friendAvatar = friendAvatar,
                footerIcon = !string.IsNullOrWhiteSpace(icon) ? icon : friendAvatar,
                friendSteamId = entry.Entry.FriendSteamId,
                appid = entry.Entry.AppId,
                gameName = entry.Entry.GameName,
                gameImage = gameImage,
                achievementName = achievementName,
                achievementIcon = icon,
                isPlaceholder = false,
                activityUtc = entry.UnlockUtc
            };
        }

        private FriendActivityHubItem TakeCurrentlyPlaying(List<FriendPresenceDto> presences, HashSet<string> usedPlaying)
        {
            foreach (var p in presences?
                .Where(p => p != null && p.state == "ingame" && !string.IsNullOrWhiteSpace(p.game) &&
                            !SteamNonGameAppFilter.IsKnownNonGameSteamApp(p.appid, p.game))
                .OrderBy(p => p.name) ?? Enumerable.Empty<FriendPresenceDto>())
            {
                if (!usedPlaying.Add(p.steamid ?? string.Empty))
                {
                    continue;
                }

                var appId = p.appid;
                var gameImage = appId > 0 ? GetSteamHeaderImageUrl(appId) : null;

                if (appId <= 0 || string.IsNullOrWhiteSpace(gameImage))
                {
                    FriendActivityRecentPlayedEntry recentMatch = null;
                    lock (sync)
                    {
                        recentMatch = recentPlayedCache?.entries?
                            .FirstOrDefault(x => x != null &&
                                                 string.Equals(x.friendSteamId, p.steamid, StringComparison.OrdinalIgnoreCase) &&
                                                 string.Equals(x.gameName, p.game, StringComparison.OrdinalIgnoreCase));
                    }

                    if (recentMatch != null)
                    {
                        appId = recentMatch.appid;
                        gameImage = recentMatch.gameImage;
                    }
                }

                return new FriendActivityHubItem
                {
                    slot = "CurrentlyPlaying",
                    activityType = "playing",
                    badgeText = "NOW PLAYING",
                    title = SafeText(p.name, "A friend") + " is playing",
                    subtitle = p.game,
                    friendName = p.name,
                    friendAvatar = p.avatar,
                    footerIcon = p.avatar,
                    friendSteamId = p.steamid,
                    appid = appId,
                    gameName = p.game,
                    gameImage = gameImage,
                    isPlaceholder = false,
                    activityUtc = DateTime.UtcNow
                };
            }

            return null;
        }

        private FriendActivityHubItem CreatePlaceholder(string title, string subtitle)
        {
            return new FriendActivityHubItem
            {
                slot = "Placeholder",
                activityType = "placeholder",
                badgeText = "FRIENDS",
                title = title,
                subtitle = subtitle,
                footerIcon = string.Empty,
                isPlaceholder = true
            };
        }

        private void ReplaceHubItems(List<FriendActivityHubItem> items)
        {
            settings.EnsureFriendActivityHubRuntimeCollections();

            var signature = BuildHubSignature(items);
            if (signature == lastHubSignature)
            {
                return;
            }

            lastHubSignature = signature;
            settings.FriendActivityHubItems.Clear();
            foreach (var item in items ?? new List<FriendActivityHubItem>())
            {
                settings.FriendActivityHubItems.Add(item);
            }
        }

        private string BuildHubSignature(List<FriendActivityHubItem> items)
        {
            var sb = new StringBuilder();
            foreach (var item in items ?? new List<FriendActivityHubItem>())
            {
                sb.Append(item.slot).Append('|')
                  .Append(item.activityType).Append('|')
                  .Append(item.title).Append('|')
                  .Append(item.subtitle).Append('|')
                  .Append(item.friendSteamId).Append('|')
                  .Append(item.appid).Append('|')
                  .Append(item.gameImage).Append('|')
                  .Append(item.friendAvatar).Append('|')
                  .Append(item.achievementIcon).Append('|')
                  .Append(item.footerIcon).Append('#');
            }

            return sb.ToString();
        }

        private void LoadRecentPlayedCacheFromDisk()
        {
            try
            {
                if (!File.Exists(recentPlayedCachePath))
                {
                    return;
                }

                var json = File.ReadAllText(recentPlayedCachePath);
                var cache = Serialization.FromJson<FriendActivityRecentPlayedCache>(json);
                if (cache != null)
                {
                    lock (sync)
                    {
                        recentPlayedCache = cache;
                        if (recentPlayedCache.entries == null)
                        {
                            recentPlayedCache.entries = new List<FriendActivityRecentPlayedEntry>();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][FriendActivityHub] Failed to load recent played cache.");
            }
        }

        private void SaveRecentPlayedCacheToDisk()
        {
            try
            {
                FriendActivityRecentPlayedCache snapshot;
                lock (sync)
                {
                    snapshot = recentPlayedCache;
                }

                Directory.CreateDirectory(cacheDir);
                File.WriteAllText(recentPlayedCachePath, Serialization.ToJson(snapshot));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][FriendActivityHub] Failed to save recent played cache.");
            }
        }

        private void ReloadAchievementCacheIfChanged()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(friendsAchievementFeedCachePath) || !File.Exists(friendsAchievementFeedCachePath))
                {
                    lock (sync)
                    {
                        achievementEntries = new List<FriendAchievementFeedEntry>();
                        lastAchievementCacheWriteUtc = DateTime.MinValue;
                    }
                    return;
                }

                var writeUtc = File.GetLastWriteTimeUtc(friendsAchievementFeedCachePath);
                lock (sync)
                {
                    if (writeUtc == lastAchievementCacheWriteUtc && achievementEntries != null)
                    {
                        return;
                    }
                }

                string json;
                using (var fs = new FileStream(friendsAchievementFeedCachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }

                var cache = Serialization.FromJson<FriendAchievementFeedCache>(json);
                lock (sync)
                {
                    achievementEntries = cache?.Entries ?? new List<FriendAchievementFeedEntry>();
                    lastAchievementCacheWriteUtc = writeUtc;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][FriendActivityHub] Failed to reload FriendsAchievementFeed cache.");
            }
        }

        private static DateTime? ParseFriendAchievementDateUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(value, @"/Date\((\d+)");
                if (match.Success && long.TryParse(match.Groups[1].Value, out var ms))
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                }

                if (DateTimeOffset.TryParse(value, out var dto))
                {
                    return dto.UtcDateTime;
                }
            }
            catch
            {
            }

            return null;
        }

        private string GetSteamHeaderImageUrl(int appId)
        {
            if (appId <= 0)
            {
                return null;
            }

            return gameImageResolver != null
                ? gameImageResolver.GetGameImageSource(appId, localUri => OnHubGameImageReady(appId, localUri))
                : $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
        }

        private void OnHubGameImageReady(int appId, string localFileUri)
        {
            if (appId <= 0 || string.IsNullOrWhiteSpace(localFileUri))
            {
                return;
            }

            var changed = false;

            lock (sync)
            {
                var entries = recentPlayedCache?.entries;
                if (entries != null)
                {
                    foreach (var entry in entries.Where(e => e != null && e.appid == appId))
                    {
                        if (!string.Equals(entry.gameImage, localFileUri, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.gameImage = localFileUri;
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                SaveRecentPlayedCacheToDisk();
            }

            List<string> friendIds;
            List<FriendPresenceDto> presences;
            lock (sync)
            {
                friendIds = lastFriendIdsSnapshot?.ToList() ?? new List<string>();
                presences = lastPresenceSnapshot?.Select(ClonePresence).ToList() ?? new List<FriendPresenceDto>();
            }

            if (friendIds.Count > 0)
            {
                RebuildHubItems(friendIds, presences, showPage: true);
            }
        }

        private static string FormatMinutesToHours(int minutes)
        {
            if (minutes <= 0)
            {
                return string.Empty;
            }

            var hours = minutes / 60;
            var mins = minutes % 60;

            if (hours <= 0)
            {
                return mins + " min";
            }

            if (mins <= 0)
            {
                return hours + " h";
            }

            return hours + " h " + mins + " min";
        }

        private static string SafeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string ToFileUri(string path)
        {
            try { return new Uri(path).AbsoluteUri; } catch { return path; }
        }

        private static FriendPresenceDto ClonePresence(FriendPresenceDto source)
        {
            if (source == null)
            {
                return null;
            }

            return new FriendPresenceDto
            {
                name = source.name,
                state = source.state,
                stateLoc = source.stateLoc,
                game = source.game,
                appid = source.appid,
                steamid = source.steamid,
                avatar = source.avatar
            };
        }

        private static void InvokeOnUi(Action action)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess()) action();
                else disp.BeginInvoke(action);
            }
            catch
            {
                try { action(); } catch { }
            }
        }
    }
}
