using System.Collections.Generic;

namespace AnikiHelper.Services
{
    public class SteamStoreItem
    {
        public int AppId { get; set; }

        public string Name { get; set; }

        public string CapsuleImageUrl { get; set; }

        public string CapsuleImageLocalPath { get; set; }

        public string HeaderImageUrl { get; set; }

        public string HeaderImageLocalPath { get; set; }

        public string BackgroundImageUrl { get; set; }

        public string BackgroundImageLocalPath { get; set; }

        public string ShortDescription { get; set; }

        public string FinalPrice { get; set; }

        public string OriginalPrice { get; set; }

        public int DiscountPercent { get; set; }

        public string Currency { get; set; }

        public string FinalPriceDisplay { get; set; }

        public string OriginalPriceDisplay { get; set; }

        public string DiscountDisplay { get; set; }

        public string StoreUrl { get; set; }

        public List<string> Genres { get; set; } = new List<string>();

        public List<string> Categories { get; set; } = new List<string>();

        public List<string> Tags { get; set; } = new List<string>();

        public string ReleaseDateDisplay { get; set; }

        public bool ComingSoon { get; set; }

        public bool IsPreorder { get; set; }

        public List<string> Developers { get; set; } = new List<string>();

        public List<string> Publishers { get; set; } = new List<string>();

        public string ControllerSupport { get; set; }

        public string SupportedLanguages { get; set; }

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