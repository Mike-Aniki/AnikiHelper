using System;
using System.Collections.Generic;

namespace AnikiHelper.Services.SteamFriends
{
    public class FriendActivityRecentPlayedCache
    {
        public DateTime lastRefreshUtc { get; set; }
        public List<FriendActivityRecentPlayedEntry> entries { get; set; } = new List<FriendActivityRecentPlayedEntry>();
    }

    public class FriendActivityRecentPlayedEntry
    {
        public string friendSteamId { get; set; }
        public string friendName { get; set; }
        public string friendAvatar { get; set; }
        public int appid { get; set; }
        public string gameName { get; set; }
        public string gameImage { get; set; }
        public int playtime2WeeksMinutes { get; set; }
        public string playtime2WeeksDisplay { get; set; }
        public DateTime refreshedUtc { get; set; }
    }
}
