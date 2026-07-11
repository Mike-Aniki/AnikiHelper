using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AnikiHelper.Services.SteamFriends
{
    public class SteamFriendsWebApiClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly HttpClient http;

        public SteamFriendsWebApiClient()
        {
            http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<List<SteamFriend>> GetFriendsAsync(string apiKey, string steamId64)
        {
            var url =
                $"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&relationship=friend";

            var json = await http.GetStringAsync(url).ConfigureAwait(false);
            var root = Serialization.FromJson<GetFriendListResponseRoot>(json);

            return root?.FriendsList?.Friends?
                .Where(f => string.Equals(f.Relationship, "friend", StringComparison.OrdinalIgnoreCase))
                .Where(f => !string.IsNullOrWhiteSpace(f.SteamId))
                .GroupBy(f => f.SteamId)
                .Select(g => g.First())
                .ToList()
                ?? new List<SteamFriend>();
        }

        public async Task<List<string>> GetFriendSteamIdsAsync(string apiKey, string steamId64)
        {
            var friends = await GetFriendsAsync(apiKey, steamId64).ConfigureAwait(false);

            return friends
                .Select(f => f.SteamId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
        }

        public async Task<List<SteamRecentlyPlayedGame>> GetRecentlyPlayedGamesAsync(string apiKey, string steamId64, int count = 3)
        {
            try
            {
                var safeCount = Math.Max(1, count);

                var url =
                    $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&count={safeCount}";

                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = Serialization.FromJson<GetRecentlyPlayedGamesResponseRoot>(json);

                return root?.Response?.Games ?? new List<SteamRecentlyPlayedGame>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetRecentlyPlayedGames failed for '{steamId64}'.");
                return new List<SteamRecentlyPlayedGame>();
            }
        }

        public async Task<List<SteamRecentlyPlayedGame>> GetAllRecentlyPlayedGamesAsync(string apiKey, string steamId64)
        {
            try
            {
                var url =
                    $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&count=0";

                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = Serialization.FromJson<GetRecentlyPlayedGamesResponseRoot>(json);

                return root?.Response?.Games ?? new List<SteamRecentlyPlayedGame>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetAllRecentlyPlayedGames failed for '{steamId64}'.");
                return new List<SteamRecentlyPlayedGame>();
            }
        }

        public async Task<List<SteamOwnedGame>> GetOwnedGamesAsync(string apiKey, string steamId64)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId64))
                {
                    return new List<SteamOwnedGame>();
                }

                var url =
                    $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&include_played_free_games=1&include_appinfo=0";

                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = Serialization.FromJson<GetOwnedGamesResponseRoot>(json);

                return root?.Response?.Games ?? new List<SteamOwnedGame>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetOwnedGames failed for '{steamId64}'.");
                return new List<SteamOwnedGame>();
            }
        }

        public async Task<string> GetSteamAppTypeAsync(int appId)
        {
            try
            {
                if (appId <= 0)
                {
                    return string.Empty;
                }

                var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic";
                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = JObject.Parse(json);
                var data = root[appId.ToString()]?["data"];

                return data?["type"]?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetSteamAppType failed for AppId={appId}.");
                return string.Empty;
            }
        }

        public async Task<int> GetSteamLevelAsync(string apiKey, string steamId64)
        {
            try
            {
                var url =
                    $"https://api.steampowered.com/IPlayerService/GetSteamLevel/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}";

                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = Serialization.FromJson<GetSteamLevelResponseRoot>(json);

                return root?.Response?.PlayerLevel ?? 0;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetSteamLevel failed for '{steamId64}'.");
                return 0;
            }
        }

        public async Task<List<SteamBadge>> GetBadgesAsync(string apiKey, string steamId64)
        {
            try
            {
                var url =
                    $"https://api.steampowered.com/IPlayerService/GetBadges/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}";

                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = Serialization.FromJson<GetBadgesResponseRoot>(json);

                return root?.Response?.Badges ?? new List<SteamBadge>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetBadges failed for '{steamId64}'.");
                return new List<SteamBadge>();
            }
        }

        public async Task<List<SteamPlayerSummary>> GetPlayerSummariesAsync(string apiKey, IEnumerable<string> steamIds)
        {
            var ids = steamIds?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
            if (ids.Count == 0)
            {
                return new List<SteamPlayerSummary>();
            }

            var all = new List<SteamPlayerSummary>();
            foreach (var chunk in Chunk(ids, 100))
            {
                var joined = string.Join(",", chunk);
                var url =
                    $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(apiKey)}&steamids={Uri.EscapeDataString(joined)}";

                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = Serialization.FromJson<GetPlayerSummariesResponseRoot>(json);

                var players = root?.Response?.Players;
                if (players != null)
                {
                    all.AddRange(players);
                }
            }

            return all;
        }

        public async Task<string> ResolveVanityUrlAsync(string apiKey, string vanity)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(vanity))
            {
                return null;
            }

            var url =
                $"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key={Uri.EscapeDataString(apiKey)}&vanityurl={Uri.EscapeDataString(vanity)}";

            try
            {
                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var root = Serialization.FromJson<ResolveVanityUrlResponseRoot>(json);

                if (root?.Response?.Success == 1 &&
                    !string.IsNullOrWhiteSpace(root.Response.SteamId))
                {
                    return root.Response.SteamId;
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"ResolveVanityURL failed for '{vanity}'.");
                return null;
            }
        }


        private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
        {
            for (int i = 0; i < source.Count; i += size)
            {
                yield return source.GetRange(i, Math.Min(size, source.Count - i));
            }
        }
    }
}
