using System;
using System.Collections.Generic;

namespace AnikiHelper.Services
{
    public class SteamAppDetailsCacheEntry
    {
        public DateTime LastUpdatedUtc { get; set; }

        public int AppId { get; set; }

        public string ShortDescription { get; set; }

        public string ReleaseDateDisplay { get; set; }

        public bool ComingSoon { get; set; }

        public bool IsPreorder { get; set; }

        public List<string> Developers { get; set; } = new List<string>();

        public List<string> Publishers { get; set; } = new List<string>();

        public string ControllerSupport { get; set; }

        public string SupportedLanguages { get; set; }

        public List<string> Genres { get; set; } = new List<string>();

        public List<string> Categories { get; set; } = new List<string>();

        public string BackgroundImageUrl { get; set; }

        public string BackgroundImageLocalPath { get; set; }

        public string FinalPriceDisplay { get; set; }

        public string OriginalPriceDisplay { get; set; }

        public string DiscountDisplay { get; set; }

        public int MetacriticScore { get; set; }

        public int RecommendationsTotal { get; set; }

        public int AchievementsTotal { get; set; }

        public int DlcCount { get; set; }

        public string Screenshot1Url { get; set; }
        public string Screenshot1LocalPath { get; set; }

        public string Screenshot2Url { get; set; }
        public string Screenshot2LocalPath { get; set; }

        public string Screenshot3Url { get; set; }
        public string Screenshot3LocalPath { get; set; }
    }
}