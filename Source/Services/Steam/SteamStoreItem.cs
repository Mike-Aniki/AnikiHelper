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

        // Original image URL parsed from Steam HTML rows. Used only as fallback when
        // deterministic Steam app images do not exist for bundles/packages/preorders.
        public string SearchImageUrl { get; set; }

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

        // Steam appdetails type (game, dlc, demo, software, video, etc.).
        // Used by Store filters to avoid non-game entries when needed.
        public string AppType { get; set; }

        public List<string> Genres { get; set; } = new List<string>();

        public List<string> Categories { get; set; } = new List<string>();

        public List<string> Tags { get; set; } = new List<string>();

        // Developer-written mature-content warning returned by Steam appdetails.
        // Example: "The game contains some naked and pornographic content."
        public string ContentDescriptorNotes { get; set; }

        // Raw Steam content descriptor IDs are stored for diagnostics and future filtering.
        public List<int> ContentDescriptorIds { get; set; } = new List<int>();

        public string ReleaseDateDisplay { get; set; }

        public bool ComingSoon { get; set; }

        public bool IsPreorder { get; set; }

        // True when the connected Steam Store page marks this game as already in the user wishlist.
        // Used by the theme to display a small "In wishlist" / "Dans la liste de souhaits" badge.
        public bool IsInWishlist { get; set; }

        // True when the AppId/name is already present in the local Playnite library
        // or the connected Steam owned-games list. Used by the theme to display an
        // "In Library" badge on Store cards.
        public bool IsInLibrary { get; set; }

        public List<string> Developers { get; set; } = new List<string>();

        public List<string> Publishers { get; set; } = new List<string>();

        public string ControllerSupport { get; set; }

        public string SupportedLanguages { get; set; }

        public int MetacriticScore { get; set; }

        public int RecommendationsTotal { get; set; }

        // Lower value = better Steam list position. Used only for UI choices like the hero/spotlight.
        public int SteamRank { get; set; }

        // For Recommended / For You only: score calculated by Aniki Helper after profile matching.
        // This must drive the Recommended hero instead of generic SteamRank.
        public int RecommendationScore { get; set; }

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

        public string StoreWideHeaderImage
        {
            get
            {
                return StoreCardImage;
            }
        }

        public string StoreCardImage
        {
            get
            {
                // Store list cards prefer stable local images, but connected Steam pages can
                // occasionally save before every image has been downloaded. In that case we
                // fall back to deterministic high-quality Steam app URLs, never to tiny
                // 231x87 search thumbnails.
                var local = FirstValidStoreCardImagePath(
                    HeaderImageLocalPath,
                    CapsuleImageLocalPath
                );

                if (!string.IsNullOrWhiteSpace(local))
                {
                    return local;
                }

                return FirstValidRemoteStoreImageUrl(
                    HeaderImageUrl,
                    CapsuleImageUrl,
                    BuildDeterministicSteamHeaderUrl(AppId),
                    BuildDeterministicSteamCapsuleUrl(AppId)
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
                var local = FirstValidStoreCardImagePath(
                    HeaderImageLocalPath,
                    CapsuleImageLocalPath
                );

                if (!string.IsNullOrWhiteSpace(local))
                {
                    return local;
                }

                return FirstValidRemoteStoreImageUrl(
                    HeaderImageUrl,
                    CapsuleImageUrl,
                    BuildDeterministicSteamHeaderUrl(AppId),
                    BuildDeterministicSteamCapsuleUrl(AppId)
                );
            }
        }

        private static string FirstValidImagePath(params string[] values)
        {
            return FirstValidStoreCardImagePath(values);
        }

        private static string FirstValidStoreCardImagePath(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            // Pass 1: Steam headers/banners. They are the desired Store format.
            foreach (var value in values)
            {
                if (IsGoodHeaderOrBannerLocalImage(value))
                {
                    return value;
                }
            }

            // Pass 2: true large capsules only. Never use 231x87 search thumbnails.
            foreach (var value in values)
            {
                if (IsGoodWideLocalImage(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string FirstValidRemoteStoreImageUrl(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (IsGoodRemoteSteamStoreImageUrl(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static bool IsGoodRemoteSteamStoreImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsBadSteamSharedImage(trimmed))
            {
                return false;
            }

            if (trimmed.IndexOf("capsule_231x87", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("microtrailer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("/public/shared/images/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return trimmed.IndexOf("/steam/apps/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (trimmed.IndexOf("/header", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("/capsule_616x353", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("/library_600x900", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildDeterministicSteamHeaderUrl(int appId)
        {
            if (appId <= 0)
            {
                return string.Empty;
            }

            return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
        }

        private static string BuildDeterministicSteamCapsuleUrl(int appId)
        {
            if (appId <= 0)
            {
                return string.Empty;
            }

            return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/capsule_616x353.jpg";
        }

        private static bool IsGoodHeaderOrBannerLocalImage(string value)
        {
            if (!IsUsableLocalImageFile(value))
            {
                return false;
            }

            if (!TryGetImageDimensions(value, out var width, out var height))
            {
                return false;
            }

            // Steam header.jpg is 460x215. Require at least that family of image.
            return width >= 400 && height >= 180;
        }

        private static bool IsGoodWideLocalImage(string value)
        {
            if (!IsUsableLocalImageFile(value))
            {
                return false;
            }

            if (!TryGetImageDimensions(value, out var width, out var height))
            {
                return false;
            }

            // Steam capsule_616x353 is the only capsule quality accepted for Store cards.
            return width >= 500 && height >= 250;
        }

        private static bool TryGetImageDimensions(string path, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(path) || IsUrl(path) || !File.Exists(path))
                {
                    return false;
                }

                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length < 24)
                    {
                        return false;
                    }

                    var header = new byte[24];
                    var read = stream.Read(header, 0, header.Length);
                    if (read < 24)
                    {
                        return false;
                    }

                    // PNG
                    if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                    {
                        width = (header[16] << 24) + (header[17] << 16) + (header[18] << 8) + header[19];
                        height = (header[20] << 24) + (header[21] << 16) + (header[22] << 8) + header[23];
                        return width > 0 && height > 0;
                    }

                    // GIF
                    if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46)
                    {
                        width = header[6] + (header[7] << 8);
                        height = header[8] + (header[9] << 8);
                        return width > 0 && height > 0;
                    }

                    // JPEG
                    if (header[0] != 0xFF || header[1] != 0xD8)
                    {
                        return false;
                    }

                    stream.Position = 2;
                    while (stream.Position + 9 < stream.Length)
                    {
                        var prefix = stream.ReadByte();
                        if (prefix != 0xFF)
                        {
                            continue;
                        }

                        int marker;
                        do
                        {
                            marker = stream.ReadByte();
                        }
                        while (marker == 0xFF);

                        if (marker < 0)
                        {
                            return false;
                        }

                        // Standalone markers with no length.
                        if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                        {
                            continue;
                        }

                        var hi = stream.ReadByte();
                        var lo = stream.ReadByte();
                        if (hi < 0 || lo < 0)
                        {
                            return false;
                        }

                        var length = (hi << 8) + lo;
                        if (length < 2 || stream.Position + length - 2 > stream.Length)
                        {
                            return false;
                        }

                        // SOF markers that contain dimensions.
                        if ((marker >= 0xC0 && marker <= 0xC3) ||
                            (marker >= 0xC5 && marker <= 0xC7) ||
                            (marker >= 0xC9 && marker <= 0xCB) ||
                            (marker >= 0xCD && marker <= 0xCF))
                        {
                            stream.ReadByte(); // precision
                            var h1 = stream.ReadByte();
                            var h2 = stream.ReadByte();
                            var w1 = stream.ReadByte();
                            var w2 = stream.ReadByte();
                            if (h1 < 0 || h2 < 0 || w1 < 0 || w2 < 0)
                            {
                                return false;
                            }

                            height = (h1 << 8) + h2;
                            width = (w1 << 8) + w2;
                            return width > 0 && height > 0;
                        }

                        stream.Position += length - 2;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsUsableLocalImageFile(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value) || IsUrl(value) || !File.Exists(value))
                {
                    return false;
                }

                var info = new FileInfo(value);
                if (info.Length < 1024)
                {
                    return false;
                }

                using (var stream = File.OpenRead(value))
                {
                    if (stream.Length < 8)
                    {
                        return false;
                    }

                    var b0 = stream.ReadByte();
                    var b1 = stream.ReadByte();
                    var b2 = stream.ReadByte();
                    var b3 = stream.ReadByte();

                    // JPEG, PNG, GIF, WEBP/RIFF. Steam Store images are usually JPEG, but this
                    // keeps the guard safe if Steam changes a source.
                    return (b0 == 0xFF && b1 == 0xD8) ||
                           (b0 == 0x89 && b1 == 0x50 && b2 == 0x4E && b3 == 0x47) ||
                           (b0 == 0x47 && b1 == 0x49 && b2 == 0x46) ||
                           (b0 == 0x52 && b1 == 0x49 && b2 == 0x46 && b3 == 0x46);
                }
            }
            catch
            {
                return false;
            }
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