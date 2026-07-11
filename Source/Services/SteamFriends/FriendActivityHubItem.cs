using System;

namespace AnikiHelper.Services.SteamFriends
{
    public class FriendActivityHubItem
    {
        public string slot { get; set; }
        public string activityType { get; set; }
        public string badgeText { get; set; }
        public string title { get; set; }
        public string subtitle { get; set; }
        public string friendName { get; set; }
        public string friendAvatar { get; set; }
        public string friendSteamId { get; set; }
        public int appid { get; set; }
        public string gameName { get; set; }
        public string gameImage { get; set; }
        public string achievementName { get; set; }
        public string achievementIcon { get; set; }
        public bool isPlaceholder { get; set; }
        public string footerIcon { get; set; }
        public DateTime? activityUtc { get; set; }
    }
}
