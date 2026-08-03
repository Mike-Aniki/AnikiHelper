using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AnikiHelper.Services.SteamFriends
{
    public sealed class SteamAuthenticatedApiException : Exception
    {
        public HttpStatusCode? StatusCode { get; }
        public bool RequiresReconnect =>
            StatusCode == HttpStatusCode.Unauthorized ||
            StatusCode == HttpStatusCode.Forbidden;

        public SteamAuthenticatedApiException(string message, HttpStatusCode? statusCode = null, Exception innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>
    /// Authenticated Steam Web API client.
    ///
    /// The caller supplies the webapi_token obtained from the existing Steam WebLogin
    /// session. No user-created Steam Web API key is required.
    /// </summary>
    public class SteamFriendsWebApiClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly HttpClient http;

        public SteamFriendsWebApiClient()
        {
            http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(12)
            };
        }

        public async Task<List<SteamFriend>> GetFriendsAsync(string accessToken, string steamId64)
        {
            EnsureAuthenticatedInput(accessToken, steamId64);

            // IFriendsListService is the authenticated service used by Steam's own web
            // clients. Keep two argument variants because Steam has returned both shapes
            // over time. The final ISteamUser URL is a non-key compatibility attempt only.
            var urls = new[]
            {
                BuildUrl(
                    "https://api.steampowered.com/IFriendsListService/GetFriendsList/v1/",
                    accessToken,
                    Param("steamid", steamId64),
                    Param("relationship", "friend")),
                BuildUrl(
                    "https://api.steampowered.com/IFriendsListService/GetFriendsList/v1/",
                    accessToken),
                BuildUrl(
                    "https://api.steampowered.com/ISteamUser/GetFriendList/v1/",
                    accessToken,
                    Param("steamid", steamId64),
                    Param("relationship", "friend"))
            };

            SteamAuthenticatedApiException lastError = null;
            foreach (var url in urls)
            {
                try
                {
                    var json = await GetResponseAsync(url, "friends list").ConfigureAwait(false);
                    if (TryParseFriends(json, out var friends))
                    {
                        return friends;
                    }

                    lastError = new SteamAuthenticatedApiException("Steam returned an unsupported friends list response.");
                }
                catch (SteamAuthenticatedApiException ex) when (
                    ex.StatusCode == HttpStatusCode.BadRequest ||
                    ex.StatusCode == HttpStatusCode.NotFound ||
                    ex.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new SteamAuthenticatedApiException("Steam did not return a supported friends list response.");
        }

        public async Task<List<string>> GetFriendSteamIdsAsync(string accessToken, string steamId64)
        {
            var friends = await GetFriendsAsync(accessToken, steamId64).ConfigureAwait(false);

            return friends
                .Select(f => f.SteamId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<List<SteamRecentlyPlayedGame>> GetRecentlyPlayedGamesAsync(string accessToken, string steamId64, int count = 3)
        {
            try
            {
                EnsureAuthenticatedInput(accessToken, steamId64);
                var safeCount = Math.Max(1, count);
                var url = BuildUrl(
                    "https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/",
                    accessToken,
                    Param("steamid", steamId64),
                    Param("count", safeCount.ToString()));

                var json = await GetResponseAsync(url, "recently played games").ConfigureAwait(false);
                var root = Serialization.FromJson<GetRecentlyPlayedGamesResponseRoot>(json);
                return root?.Response?.Games ?? new List<SteamRecentlyPlayedGame>();
            }
            catch (SteamAuthenticatedApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetRecentlyPlayedGames failed for '{steamId64}'.");
                return new List<SteamRecentlyPlayedGame>();
            }
        }

        public async Task<List<SteamRecentlyPlayedGame>> GetAllRecentlyPlayedGamesAsync(string accessToken, string steamId64)
        {
            try
            {
                EnsureAuthenticatedInput(accessToken, steamId64);
                var url = BuildUrl(
                    "https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/",
                    accessToken,
                    Param("steamid", steamId64),
                    Param("count", "0"));

                var json = await GetResponseAsync(url, "all recently played games").ConfigureAwait(false);
                var root = Serialization.FromJson<GetRecentlyPlayedGamesResponseRoot>(json);
                return root?.Response?.Games ?? new List<SteamRecentlyPlayedGame>();
            }
            catch (SteamAuthenticatedApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetAllRecentlyPlayedGames failed for '{steamId64}'.");
                return new List<SteamRecentlyPlayedGame>();
            }
        }

        public async Task<List<SteamOwnedGame>> GetOwnedGamesAsync(string accessToken, string steamId64)
        {
            try
            {
                EnsureAuthenticatedInput(accessToken, steamId64);
                var url = BuildUrl(
                    "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/",
                    accessToken,
                    Param("steamid", steamId64),
                    Param("include_played_free_games", "1"),
                    Param("include_appinfo", "0"));

                var json = await GetResponseAsync(url, "owned games").ConfigureAwait(false);
                var root = Serialization.FromJson<GetOwnedGamesResponseRoot>(json);
                return root?.Response?.Games ?? new List<SteamOwnedGame>();
            }
            catch (SteamAuthenticatedApiException)
            {
                throw;
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

        public async Task<int> GetSteamLevelAsync(string accessToken, string steamId64)
        {
            try
            {
                EnsureAuthenticatedInput(accessToken, steamId64);
                var url = BuildUrl(
                    "https://api.steampowered.com/IPlayerService/GetSteamLevel/v1/",
                    accessToken,
                    Param("steamid", steamId64));

                var json = await GetResponseAsync(url, "Steam level").ConfigureAwait(false);
                var root = Serialization.FromJson<GetSteamLevelResponseRoot>(json);
                return root?.Response?.PlayerLevel ?? 0;
            }
            catch (SteamAuthenticatedApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetSteamLevel failed for '{steamId64}'.");
                return 0;
            }
        }

        public async Task<List<SteamBadge>> GetBadgesAsync(string accessToken, string steamId64)
        {
            try
            {
                EnsureAuthenticatedInput(accessToken, steamId64);
                var url = BuildUrl(
                    "https://api.steampowered.com/IPlayerService/GetBadges/v1/",
                    accessToken,
                    Param("steamid", steamId64));

                var json = await GetResponseAsync(url, "Steam badges").ConfigureAwait(false);
                var root = Serialization.FromJson<GetBadgesResponseRoot>(json);
                return root?.Response?.Badges ?? new List<SteamBadge>();
            }
            catch (SteamAuthenticatedApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"GetBadges failed for '{steamId64}'.");
                return new List<SteamBadge>();
            }
        }

        public async Task<List<SteamPlayerSummary>> GetPlayerSummariesAsync(string accessToken, IEnumerable<string> steamIds)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new SteamAuthenticatedApiException("Steam login is required.");
            }

            var ids = steamIds?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (ids.Count == 0)
            {
                return new List<SteamPlayerSummary>();
            }

            var all = new List<SteamPlayerSummary>();
            foreach (var chunk in Chunk(ids, 100))
            {
                var joined = string.Join(",", chunk);
                var urls = new[]
                {
                    BuildUrl(
                        "https://api.steampowered.com/ISteamUserOAuth/GetUserSummaries/v2/",
                        accessToken,
                        Param("steamids", joined)),
                    BuildUrl(
                        "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/",
                        accessToken,
                        Param("steamids", joined))
                };

                SteamAuthenticatedApiException lastError = null;
                var parsed = false;
                foreach (var url in urls)
                {
                    try
                    {
                        var json = await GetResponseAsync(url, "player summaries").ConfigureAwait(false);
                        if (TryParsePlayerSummaries(json, out var players))
                        {
                            all.AddRange(players);
                            parsed = true;
                            break;
                        }

                        lastError = new SteamAuthenticatedApiException("Steam returned an unsupported player summaries response.");
                    }
                    catch (SteamAuthenticatedApiException ex) when (
                        ex.StatusCode == HttpStatusCode.BadRequest ||
                        ex.StatusCode == HttpStatusCode.NotFound ||
                        ex.StatusCode == HttpStatusCode.MethodNotAllowed)
                    {
                        lastError = ex;
                    }
                }

                if (!parsed)
                {
                    throw lastError ?? new SteamAuthenticatedApiException("Steam did not return a supported player summaries response.");
                }
            }

            return all
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.SteamId))
                .GroupBy(p => p.SteamId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private async Task<string> GetResponseAsync(string url, string operation)
        {
            try
            {
                using (var response = await http.GetAsync(url).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return body ?? string.Empty;
                    }

                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        throw new SteamAuthenticatedApiException(
                            "Your Steam session is no longer authorized. Reconnect your Steam account in Aniki Helper settings.",
                            response.StatusCode);
                    }

                    throw new SteamAuthenticatedApiException(
                        $"Steam {operation} request failed with HTTP {(int)response.StatusCode}.",
                        response.StatusCode);
                }
            }
            catch (SteamAuthenticatedApiException)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                throw new SteamAuthenticatedApiException($"Steam {operation} request timed out.");
            }
            catch (HttpRequestException)
            {
                throw new SteamAuthenticatedApiException($"Steam {operation} request failed because of a network error.");
            }
        }

        private static string BuildUrl(string baseUrl, string accessToken, params KeyValuePair<string, string>[] parameters)
        {
            var query = new List<string>
            {
                "access_token=" + Uri.EscapeDataString(accessToken ?? string.Empty)
            };

            foreach (var parameter in parameters ?? new KeyValuePair<string, string>[0])
            {
                if (!string.IsNullOrWhiteSpace(parameter.Key) && parameter.Value != null)
                {
                    query.Add(Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Value));
                }
            }

            return baseUrl.TrimEnd('?') + "?" + string.Join("&", query);
        }


        private static KeyValuePair<string, string> Param(string name, string value)
        {
            return new KeyValuePair<string, string>(name, value);
        }

        private static void EnsureAuthenticatedInput(string accessToken, string steamId64)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new SteamAuthenticatedApiException("Steam login is required.");
            }

            if (string.IsNullOrWhiteSpace(steamId64))
            {
                throw new SteamAuthenticatedApiException("The connected Steam account could not be identified.");
            }
        }

        private static bool TryParseFriends(string json, out List<SteamFriend> result)
        {
            result = new List<SteamFriend>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var root = JObject.Parse(json);
            var friends = root["friendslist"]?["friends"] as JArray
                ?? root["response"]?["friends"] as JArray
                ?? root["response"]?["friendslist"]?["friends"] as JArray;

            if (friends == null)
            {
                return false;
            }

            foreach (var item in friends.OfType<JObject>())
            {
                var steamId = FirstString(item, "steamid", "ulfriendid", "friendid");
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    continue;
                }

                var relationship = FirstString(item, "relationship", "efriendrelationship");
                if (string.IsNullOrWhiteSpace(relationship))
                {
                    // IFriendsListService/GetFriendsList already represents the authenticated
                    // friend list, and some response variants omit the relationship field.
                    relationship = "friend";
                }
                else if (relationship.All(char.IsDigit))
                {
                    // EFriendRelationship.Friend = 3. Do not accidentally include blocked,
                    // ignored or pending relationships if Steam returns the wider list.
                    relationship = relationship == "3" ? "friend" : relationship;
                }

                result.Add(new SteamFriend
                {
                    SteamId = steamId,
                    Relationship = relationship,
                    FriendSince = FirstLong(item, "friend_since", "rtfriend_since", "rt_friend_since")
                });
            }

            result = result
                .Where(f => string.Equals(f.Relationship, "friend", StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f.SteamId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            return true;
        }

        private static bool TryParsePlayerSummaries(string json, out List<SteamPlayerSummary> result)
        {
            result = new List<SteamPlayerSummary>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var root = JObject.Parse(json);
            var players = root["response"]?["players"] as JArray
                ?? root["players"] as JArray;

            if (players == null)
            {
                return false;
            }

            result = players.ToObject<List<SteamPlayerSummary>>() ?? new List<SteamPlayerSummary>();
            return true;
        }

        private static string FirstString(JObject source, params string[] names)
        {
            foreach (var name in names ?? Array.Empty<string>())
            {
                var value = source?[name];
                if (value != null && value.Type != JTokenType.Null)
                {
                    var text = value.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return string.Empty;
        }

        private static long FirstLong(JObject source, params string[] names)
        {
            foreach (var name in names ?? Array.Empty<string>())
            {
                var value = source?[name];
                if (value != null && long.TryParse(value.ToString(), out var parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }

        private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
            {
                yield return source.GetRange(i, Math.Min(size, source.Count - i));
            }
        }
    }
}
