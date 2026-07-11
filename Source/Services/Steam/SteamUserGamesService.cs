using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AnikiHelper.Services
{
    public class SteamUserRecentGameSeed
    {
        public int AppId { get; set; }

        public string Name { get; set; }

        public int Playtime2Weeks { get; set; }

        public int PlaytimeForever { get; set; }

        public int Weight { get; set; }
    }

    public class SteamUserGamesService
    {
        private readonly ILogger logger;
        private readonly string userCacheFolder;
        private readonly string legacyUserCacheFolder;
        private readonly HttpClient httpClient;

        public SteamUserGamesService(ILogger logger, string pluginUserDataPath)
        {
            this.logger = logger;
            userCacheFolder = Path.Combine(pluginUserDataPath, "SteamStore", "StoreCache");
            legacyUserCacheFolder = Path.Combine(pluginUserDataPath, "SteamStore", "UserCache");
            Directory.CreateDirectory(userCacheFolder);

            httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<HashSet<int>> GetOwnedGameAppIdsAsync(string apiKey, string steamIdInput, TimeSpan maxAge)
        {
            var result = new HashSet<int>();

            try
            {
                var steamId64 = await ResolveSteamId64Async(apiKey, steamIdInput).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId64))
                {
                    return result;
                }

                var cachePath = GetCachePath("owned", steamId64);
                TryMigrateLegacyUserCacheFile("owned", steamId64, cachePath);
                var cached = LoadOwnedCache(cachePath, maxAge, steamId64);
                if (cached != null)
                {
                    foreach (var appId in cached.AppIds ?? new List<int>())
                    {
                        if (appId > 0)
                        {
                            result.Add(appId);
                        }
                    }

                    return result;
                }

                var url =
                    $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&include_appinfo=0&include_played_free_games=1";

                var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
                var root = JsonConvert.DeserializeObject<OwnedGamesResponseRoot>(json);

                var appIds = root?.Response?.Games?
                    .Where(x => x != null && x.AppId > 0)
                    .Select(x => x.AppId)
                    .Distinct()
                    .ToList() ?? new List<int>();

                SaveOwnedCache(cachePath, appIds, steamId64);

                foreach (var appId in appIds)
                {
                    result.Add(appId);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[Steam User Games] Failed to load owned games.");
            }

            return result;
        }

        public async Task<List<SteamUserRecentGameSeed>> GetRecentlyPlayedGameSeedsAsync(string apiKey, string steamIdInput, int count, TimeSpan maxAge)
        {
            try
            {
                var steamId64 = await ResolveSteamId64Async(apiKey, steamIdInput).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId64))
                {
                    return new List<SteamUserRecentGameSeed>();
                }

                var cachePath = GetCachePath("recent", steamId64);
                TryMigrateLegacyUserCacheFile("recent", steamId64, cachePath);
                var cached = LoadRecentCache(cachePath, maxAge, steamId64);
                if (cached != null)
                {
                    return cached.Seeds ?? new List<SteamUserRecentGameSeed>();
                }

                var safeCount = Math.Max(1, Math.Min(20, count));
                var url =
                    $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&count={safeCount}";

                var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
                var root = JsonConvert.DeserializeObject<RecentlyPlayedResponseRoot>(json);

                var seeds = root?.Response?.Games?
                    .Where(x => x != null && x.AppId > 0)
                    .Select((x, index) => new SteamUserRecentGameSeed
                    {
                        AppId = x.AppId,
                        Name = x.Name ?? string.Empty,
                        Playtime2Weeks = x.Playtime2Weeks,
                        PlaytimeForever = x.PlaytimeForever,
                        Weight = Math.Max(80, 260 - (index * 35)) + Math.Min(80, x.Playtime2Weeks / 10)
                    })
                    .GroupBy(x => x.AppId)
                    .Select(x => x.First())
                    .Take(safeCount)
                    .ToList() ?? new List<SteamUserRecentGameSeed>();

                SaveRecentCache(cachePath, seeds, steamId64);
                return seeds;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[Steam User Games] Failed to load recently played games.");
                return new List<SteamUserRecentGameSeed>();
            }
        }

        private async Task<string> ResolveSteamId64Async(string apiKey, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var value = input.Trim();
            if (value.Length == 17 && value.All(char.IsDigit))
            {
                return value;
            }

            var profilesMarker = "/profiles/";
            var idxProfiles = value.IndexOf(profilesMarker, StringComparison.OrdinalIgnoreCase);
            if (idxProfiles >= 0)
            {
                var after = value.Substring(idxProfiles + profilesMarker.Length);
                var digits = new string(after.TakeWhile(char.IsDigit).ToArray());
                if (!string.IsNullOrWhiteSpace(digits))
                {
                    return digits;
                }
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            string vanity;
            var idMarker = "/id/";
            var idxId = value.IndexOf(idMarker, StringComparison.OrdinalIgnoreCase);
            if (idxId >= 0)
            {
                var after = value.Substring(idxId + idMarker.Length);
                vanity = new string(after.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            }
            else
            {
                vanity = value;
            }

            if (string.IsNullOrWhiteSpace(vanity))
            {
                return null;
            }

            try
            {
                var url =
                    $"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key={Uri.EscapeDataString(apiKey)}&vanityurl={Uri.EscapeDataString(vanity)}";

                var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
                var root = JsonConvert.DeserializeObject<ResolveVanityResponseRoot>(json);

                if (root?.Response?.Success == 1 && !string.IsNullOrWhiteSpace(root.Response.SteamId))
                {
                    return root.Response.SteamId;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[Steam User Games] Failed to resolve Steam vanity URL.");
            }

            return null;
        }

        private string GetCachePath(string kind, string steamId64)
        {
            var safeKind = string.IsNullOrWhiteSpace(kind) ? "cache" : kind.Trim().ToLowerInvariant();
            switch (safeKind)
            {
                case "owned":
                    return Path.Combine(userCacheFolder, "user_owned.json");

                case "recent":
                    return Path.Combine(userCacheFolder, "user_recent.json");

                default:
                    return Path.Combine(userCacheFolder, $"user_{safeKind}.json");
            }
        }

        private void TryMigrateLegacyUserCacheFile(string kind, string steamId64, string targetPath)
        {
            try
            {
                if (File.Exists(targetPath) || string.IsNullOrWhiteSpace(legacyUserCacheFolder) || !Directory.Exists(legacyUserCacheFolder))
                {
                    return;
                }

                var safeKind = string.IsNullOrWhiteSpace(kind) ? "cache" : kind.Trim().ToLowerInvariant();
                var safeSteamId = string.IsNullOrWhiteSpace(steamId64) ? "unknown" : new string(steamId64.Where(char.IsDigit).ToArray());
                var legacyPath = Path.Combine(legacyUserCacheFolder, $"{safeKind}_{safeSteamId}.json");
                if (!File.Exists(legacyPath))
                {
                    return;
                }

                File.Copy(legacyPath, targetPath, false);

                if (safeKind == "owned")
                {
                    var entry = JsonConvert.DeserializeObject<OwnedGamesCacheEntry>(File.ReadAllText(targetPath)) ?? new OwnedGamesCacheEntry();
                    entry.SteamId64 = safeSteamId;
                    File.WriteAllText(targetPath, JsonConvert.SerializeObject(entry, Formatting.Indented));
                }
                else if (safeKind == "recent")
                {
                    var entry = JsonConvert.DeserializeObject<RecentGamesCacheEntry>(File.ReadAllText(targetPath)) ?? new RecentGamesCacheEntry();
                    entry.SteamId64 = safeSteamId;
                    File.WriteAllText(targetPath, JsonConvert.SerializeObject(entry, Formatting.Indented));
                }

                File.Delete(legacyPath);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[Steam User Games] Legacy user cache migration failed.");
            }
        }

        private OwnedGamesCacheEntry LoadOwnedCache(string path, TimeSpan maxAge, string steamId64)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var entry = JsonConvert.DeserializeObject<OwnedGamesCacheEntry>(File.ReadAllText(path));
                if (entry == null || DateTime.UtcNow - entry.LastUpdatedUtc > maxAge)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(entry.SteamId64) &&
                    !string.Equals(entry.SteamId64, steamId64, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return entry;
            }
            catch
            {
                return null;
            }
        }

        private RecentGamesCacheEntry LoadRecentCache(string path, TimeSpan maxAge, string steamId64)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var entry = JsonConvert.DeserializeObject<RecentGamesCacheEntry>(File.ReadAllText(path));
                if (entry == null || DateTime.UtcNow - entry.LastUpdatedUtc > maxAge)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(entry.SteamId64) &&
                    !string.Equals(entry.SteamId64, steamId64, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return entry;
            }
            catch
            {
                return null;
            }
        }

        private void SaveOwnedCache(string path, List<int> appIds, string steamId64)
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(new OwnedGamesCacheEntry
                {
                    LastUpdatedUtc = DateTime.UtcNow,
                    SteamId64 = steamId64 ?? string.Empty,
                    AppIds = appIds ?? new List<int>()
                }, Formatting.Indented));
            }
            catch
            {
            }
        }

        private void SaveRecentCache(string path, List<SteamUserRecentGameSeed> seeds, string steamId64)
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(new RecentGamesCacheEntry
                {
                    LastUpdatedUtc = DateTime.UtcNow,
                    SteamId64 = steamId64 ?? string.Empty,
                    Seeds = seeds ?? new List<SteamUserRecentGameSeed>()
                }, Formatting.Indented));
            }
            catch
            {
            }
        }

        private sealed class OwnedGamesCacheEntry
        {
            public DateTime LastUpdatedUtc { get; set; }
            public string SteamId64 { get; set; }
            public List<int> AppIds { get; set; } = new List<int>();
        }

        private sealed class RecentGamesCacheEntry
        {
            public DateTime LastUpdatedUtc { get; set; }
            public string SteamId64 { get; set; }
            public List<SteamUserRecentGameSeed> Seeds { get; set; } = new List<SteamUserRecentGameSeed>();
        }

        private sealed class OwnedGamesResponseRoot
        {
            [JsonProperty("response")]
            public OwnedGamesResponse Response { get; set; }
        }

        private sealed class OwnedGamesResponse
        {
            [JsonProperty("games")]
            public List<OwnedGameDto> Games { get; set; }
        }

        private sealed class OwnedGameDto
        {
            [JsonProperty("appid")]
            public int AppId { get; set; }
        }

        private sealed class RecentlyPlayedResponseRoot
        {
            [JsonProperty("response")]
            public RecentlyPlayedResponse Response { get; set; }
        }

        private sealed class RecentlyPlayedResponse
        {
            [JsonProperty("games")]
            public List<RecentlyPlayedGameDto> Games { get; set; }
        }

        private sealed class RecentlyPlayedGameDto
        {
            [JsonProperty("appid")]
            public int AppId { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("playtime_2weeks")]
            public int Playtime2Weeks { get; set; }

            [JsonProperty("playtime_forever")]
            public int PlaytimeForever { get; set; }
        }

        private sealed class ResolveVanityResponseRoot
        {
            [JsonProperty("response")]
            public ResolveVanityResponse Response { get; set; }
        }

        private sealed class ResolveVanityResponse
        {
            [JsonProperty("success")]
            public int Success { get; set; }

            [JsonProperty("steamid")]
            public string SteamId { get; set; }
        }
    }
}
