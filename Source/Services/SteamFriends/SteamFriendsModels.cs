using Playnite.SDK.Data;
using System;
using System.Collections.Generic;

namespace AnikiHelper.Services.SteamFriends
{
    public class SteamFriend
    {
        [SerializationPropertyName("steamid")]
        public string SteamId { get; set; }

        [SerializationPropertyName("relationship")]
        public string Relationship { get; set; }

        [SerializationPropertyName("friend_since")]
        public long FriendSince { get; set; }
    }

    public class GetFriendListResponseRoot
    {
        [SerializationPropertyName("friendslist")]
        public FriendsList FriendsList { get; set; }
    }

    public class FriendsList
    {
        [SerializationPropertyName("friends")]
        public List<SteamFriend> Friends { get; set; }
    }

    public class SteamPlayerSummary
    {
        [SerializationPropertyName("steamid")]
        public string SteamId { get; set; }

        [SerializationPropertyName("personaname")]
        public string PersonaName { get; set; }

        [SerializationPropertyName("personastate")]
        public int PersonaState { get; set; }

        [SerializationPropertyName("communityvisibilitystate")]
        public int CommunityVisibilityState { get; set; }

        [SerializationPropertyName("lastlogoff")]
        public long LastLogOff { get; set; }

        [SerializationPropertyName("profileurl")]
        public string ProfileUrl { get; set; }

        [SerializationPropertyName("gameextrainfo")]
        public string GameExtraInfo { get; set; }

        [SerializationPropertyName("gameid")]
        public string GameId { get; set; }

        [SerializationPropertyName("avatarfull")]
        public string AvatarFull { get; set; }
    }

    public class GetPlayerSummariesResponseRoot
    {
        [SerializationPropertyName("response")]
        public PlayerSummariesResponse Response { get; set; }
    }

    public class PlayerSummariesResponse
    {
        [SerializationPropertyName("players")]
        public List<SteamPlayerSummary> Players { get; set; }
    }

    public class ResolveVanityUrlResponseRoot
    {
        [SerializationPropertyName("response")]
        public ResolveVanityUrlResponse Response { get; set; }
    }

    public class ResolveVanityUrlResponse
    {
        [SerializationPropertyName("success")]
        public int Success { get; set; }

        [SerializationPropertyName("steamid")]
        public string SteamId { get; set; }

        [SerializationPropertyName("message")]
        public string Message { get; set; }
    }

    public class FriendPresenceDto
    {
        public string name { get; set; }
        public string state { get; set; }
        public string stateLoc { get; set; }
        public string game { get; set; }
        public int appid { get; set; }
        public string steamid { get; set; }
        public string avatar { get; set; }
    }

    public class RecentGameDto
    {
        public int appid { get; set; }
        public string name { get; set; }
        public int playtime2WeeksMinutes { get; set; }
        public int playtimeForeverMinutes { get; set; }
        public string playtime2WeeksDisplay { get; set; }
        public string playtimeForeverDisplay { get; set; }
        public string headerImageUrl { get; set; }
    }

    public class FriendProfileDto
    {
        public string steamid { get; set; }
        public string name { get; set; }
        public string avatar { get; set; }
        public string state { get; set; }
        public string stateLoc { get; set; }
        public string game { get; set; }
        public bool isProfilePublic { get; set; }
        public DateTime? lastLogoffUtc { get; set; }
        public DateTime? friendSinceUtc { get; set; }
        public int steamLevel { get; set; }
        public int badgesCount { get; set; }
        public int recentPlaytime2WeeksMinutes { get; set; }
        public string recentPlaytime2WeeksDisplay { get; set; }
        public List<RecentGameDto> recentGames { get; set; } = new List<RecentGameDto>();
        public List<RecentFriendAchievementDto> recentAchievements { get; set; } = new List<RecentFriendAchievementDto>();
    }

    public class CachedFriendProfile
    {
        public string steamid { get; set; }
        public DateTime cachedAtUtc { get; set; }
        public FriendProfileDto profile { get; set; }
    }

    public class FriendAchievementFeedCache
    {
        public string LastUpdatedUtc { get; set; }
        public List<FriendAchievementFeedEntry> Entries { get; set; } = new List<FriendAchievementFeedEntry>();
    }

    public class FriendAchievementFeedEntry
    {
        public string AchievementApiName { get; set; }
        public string AchievementDisplayName { get; set; }
        public string AchievementDescription { get; set; }
        public int AppId { get; set; }
        public Guid? PlayniteGameId { get; set; }
        public string GameName { get; set; }
        public string FriendPersonaName { get; set; }
        public string FriendAvatarUrl { get; set; }
        public string FriendSteamId { get; set; }
        public string FriendAchievementIcon { get; set; }
        public string FriendUnlockTimeUtc { get; set; }
        public bool IsRevealed { get; set; }
        public bool HideAchievementsLockedForSelf { get; set; }
        public string SelfAchievementIcon { get; set; }
        public string SelfUnlockTime { get; set; }
    }

    public class RecentFriendAchievementDto
    {
        public string achievementApiName { get; set; }
        public string achievementDisplayName { get; set; }
        public string achievementDescription { get; set; }
        public int appid { get; set; }
        public Guid? playniteGameId { get; set; }
        public string gameName { get; set; }
        public string icon { get; set; }
        public DateTime? unlockTimeUtc { get; set; }
        public string unlockTimeDisplay { get; set; }
        public string rarity { get; set; }
    }

    public class GetSteamLevelResponseRoot
    {
        [SerializationPropertyName("response")]
        public SteamLevelResponse Response { get; set; }
    }

    public class SteamLevelResponse
    {
        [SerializationPropertyName("player_level")]
        public int PlayerLevel { get; set; }
    }

    public class GetBadgesResponseRoot
    {
        [SerializationPropertyName("response")]
        public SteamBadgesResponse Response { get; set; }
    }

    public class SteamBadgesResponse
    {
        [SerializationPropertyName("badges")]
        public List<SteamBadge> Badges { get; set; }
    }

    public class SteamBadge
    {
        [SerializationPropertyName("badgeid")]
        public int BadgeId { get; set; }

        [SerializationPropertyName("level")]
        public int Level { get; set; }

        [SerializationPropertyName("appid")]
        public int? AppId { get; set; }
    }

    public class GetRecentlyPlayedGamesResponseRoot
    {
        [SerializationPropertyName("response")]
        public RecentlyPlayedGamesResponse Response { get; set; }
    }

    public class RecentlyPlayedGamesResponse
    {
        [SerializationPropertyName("total_count")]
        public int TotalCount { get; set; }

        [SerializationPropertyName("games")]
        public List<SteamRecentlyPlayedGame> Games { get; set; }
    }

    public class SteamRecentlyPlayedGame
    {
        [SerializationPropertyName("appid")]
        public int AppId { get; set; }

        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("playtime_2weeks")]
        public int Playtime2Weeks { get; set; }

        [SerializationPropertyName("playtime_forever")]
        public int PlaytimeForever { get; set; }
    }

    public class GetOwnedGamesResponseRoot
    {
        [SerializationPropertyName("response")]
        public OwnedGamesResponse Response { get; set; }
    }

    public class OwnedGamesResponse
    {
        [SerializationPropertyName("game_count")]
        public int GameCount { get; set; }

        [SerializationPropertyName("games")]
        public List<SteamOwnedGame> Games { get; set; }
    }

    public class SteamOwnedGame
    {
        [SerializationPropertyName("appid")]
        public int AppId { get; set; }

        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("playtime_forever")]
        public int PlaytimeForever { get; set; }
    }

    public class SteamFriendPlayedGameDto
    {
        public string steamid { get; set; }
        public string name { get; set; }
        public string avatar { get; set; }
        public int appid { get; set; }
        public int playtimeForeverMinutes { get; set; }
        public string playtimeForeverDisplay { get; set; }
    }

    public class SteamFriendsPlayedGamesCache
    {
        public DateTime lastRefreshUtc { get; set; }
        public Dictionary<string, List<SteamFriendPlayedGameDto>> games { get; set; } = new Dictionary<string, List<SteamFriendPlayedGameDto>>();
    }

}
