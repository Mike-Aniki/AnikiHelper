using System;
using System.Collections.Generic;
using System.IO;

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

        public string Source { get; set; } = string.Empty;

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

        public string Screenshot4Url { get; set; }
        public string Screenshot4LocalPath { get; set; }

        public string Screenshot5Url { get; set; }
        public string Screenshot5LocalPath { get; set; }

        public string StoreCardImage
        {
            get
            {
                var useHeaderCard =
                    ComingSoon ||
                    string.Equals(Source, "Steam Popular Coming Soon", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Source, "Steam Popular New Releases", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Source, "Steam Most Wishlisted", StringComparison.OrdinalIgnoreCase);

                if (useHeaderCard)
                {
                    return FirstValidImagePath(
                        HeaderImageLocalPath,
                        HeaderImageUrl,
                        CapsuleImageLocalPath,
                        CapsuleImageUrl
                    );
                }

                return FirstValidImagePath(
                    CapsuleImageLocalPath,
                    CapsuleImageUrl,
                    HeaderImageLocalPath,
                    HeaderImageUrl
                );
            }
        }

        public string StoreHeroBackgroundImage
        {
            get
            {
                return FirstValidImagePath(
                    BackgroundImageLocalPath,
                    BackgroundImageUrl,
                    Screenshot1LocalPath,
                    Screenshot1Url
                );
            }
        }

        public string StoreHeroHeaderImage
        {
            get
            {
                return FirstValidImagePath(
                    HeaderImageLocalPath,
                    HeaderImageUrl,
                    CapsuleImageLocalPath,
                    CapsuleImageUrl
                );
            }
        }

        private static string FirstValidImagePath(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (IsBadSteamSharedImage(value))
                {
                    continue;
                }

                // Le thème ne doit recevoir que du local.
                if (!IsUrl(value) && File.Exists(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static bool IsUrl(string value)
        {
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBadSteamSharedImage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("/public/shared/images/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("footerLogo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("footer_logo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("valve_new", StringComparison.OrdinalIgnoreCase) >= 0;
        }


    }
}