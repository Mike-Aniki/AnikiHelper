using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace AnikiHelper
{
    public class SteamPlayerCountResult
    {
        public bool Success { get; set; }
        public int PlayerCount { get; set; }
        public string Error { get; set; }
    }

    // DTO 
    public class SteamPlayerCountApiResponse
    {
        public InnerResponse response { get; set; }

        public class InnerResponse
        {
            public int result { get; set; }
            public int player_count { get; set; }
        }
    }

    public class SteamPlayerCacheEntry
    {
        public DateTime FetchedAt { get; set; }
        public int Count { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class SteamPlayerCountService
    {
        private readonly Dictionary<string, SteamPlayerCacheEntry> cache
            = new Dictionary<string, SteamPlayerCacheEntry>();

        private readonly TimeSpan cacheDuration = TimeSpan.FromMinutes(5);

        public async Task<SteamPlayerCountResult> GetCurrentPlayersAsync(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return new SteamPlayerCountResult
                {
                    Success = false,
                    PlayerCount = 0,
                    Error = "No Steam ID"
                };
            }

            if (cache.TryGetValue(steamId, out var entry))
            {
                if (DateTime.Now - entry.FetchedAt < cacheDuration)
                {
                    return new SteamPlayerCountResult
                    {
                        Success = entry.Success,
                        PlayerCount = entry.Count,
                        Error = entry.Error
                    };
                }
            }

            try
            {
                var url = $"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={steamId}";

                using (var client = new HttpClient())
                {
                    var json = await client.GetStringAsync(url);
                    var parsed = Serialization.FromJson<SteamPlayerCountApiResponse>(json);

                    var response = parsed?.response;
                    if (response == null || response.result != 1)
                    {
                        var err = "No data";
                        cache[steamId] = new SteamPlayerCacheEntry
                        {
                            FetchedAt = DateTime.Now,
                            Count = 0,
                            Success = false,
                            Error = err
                        };

                        return new SteamPlayerCountResult
                        {
                            Success = false,
                            PlayerCount = 0,
                            Error = err
                        };
                    }

                    var count = response.player_count;

                    cache[steamId] = new SteamPlayerCacheEntry
                    {
                        FetchedAt = DateTime.Now,
                        Count = count,
                        Success = true,
                        Error = null
                    };

                    return new SteamPlayerCountResult
                    {
                        Success = true,
                        PlayerCount = count,
                        Error = null
                    };
                }
            }
            catch (Exception ex)
            {
                var err = ex.Message;

                cache[steamId] = new SteamPlayerCacheEntry
                {
                    FetchedAt = DateTime.Now,
                    Count = 0,
                    Success = false,
                    Error = err
                };

                return new SteamPlayerCountResult
                {
                    Success = false,
                    PlayerCount = 0,
                    Error = err
                };
            }
        }
    }
}
