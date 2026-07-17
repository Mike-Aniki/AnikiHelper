using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnikiHelper.Services
{
    public class SteamStorePersonalizationContext
    {
        public HashSet<int> OwnedSteamAppIds { get; set; } = new HashSet<int>();

        public HashSet<string> OwnedNormalizedNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HashSet<int> PlayedSteamAppIds { get; set; } = new HashSet<int>();

        public HashSet<string> PlayedNormalizedNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HashSet<int> WishlistSteamAppIds { get; set; } = new HashSet<int>();
    }

    public class SteamStoreRecommendationSeed
    {
        public int AppId { get; set; }

        public string Name { get; set; }

        public int Weight { get; set; }

        public string Source { get; set; }
    }

    public class SteamStorePersonalizationService
    {
        public List<SteamStoreItem> FilterSection(
            IEnumerable<SteamStoreItem> items,
            SteamStorePersonalizationContext context,
            string section,
            int maxItems)
        {
            var source = items?.Where(x => x != null).ToList() ?? new List<SteamStoreItem>();
            var result = new List<SteamStoreItem>();
            var seen = new HashSet<int>();

            foreach (var item in source)
            {
                if (item == null || item.AppId <= 0 || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                if (!seen.Add(item.AppId))
                {
                    continue;
                }

                ApplyStoreFlags(item, context);

                var filterOwned = ShouldFilterOwnedItems(section);
                if (filterOwned && item.IsInLibrary)
                {
                    continue;
                }

                if (LooksLikeNonGameContent(item))
                {
                    continue;
                }

                result.Add(item);

                if (maxItems > 0 && result.Count >= maxItems)
                {
                    break;
                }
            }

            return result;
        }


        public void ApplyStoreFlags(SteamStoreItem item, SteamStorePersonalizationContext context)
        {
            if (item == null)
            {
                return;
            }

            item.IsInLibrary = IsOwnedOrInLibrary(item, context);

            var isForYouItem =
                string.Equals(
                    item.Source,
                    "Steam Recommender",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    item.Source,
                    "Steam Recommended Supplement",
                    StringComparison.OrdinalIgnoreCase
                );

            // For You already received the freshly loaded real wishlist.
            // Do not overwrite that result with an older cached wishlist.
            if (!isForYouItem)
            {
                item.IsInWishlist =
                    item.IsInWishlist ||
                    (
                        context?.WishlistSteamAppIds != null &&
                        item.AppId > 0 &&
                        context.WishlistSteamAppIds.Contains(item.AppId)
                    );
            }
        }

        private static bool ShouldFilterOwnedItems(string section)
        {
            return string.Equals(section, "Recommended", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(section, "ForYou", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsOwnedOrInLibrary(SteamStoreItem item, SteamStorePersonalizationContext context)
        {
            if (item == null || context == null)
            {
                return false;
            }

            if (item.AppId > 0 && context.OwnedSteamAppIds != null && context.OwnedSteamAppIds.Contains(item.AppId))
            {
                return true;
            }

            var normalizedName = NormalizeName(item.Name);
            return !string.IsNullOrWhiteSpace(normalizedName) &&
                   context.OwnedNormalizedNames != null &&
                   context.OwnedNormalizedNames.Contains(normalizedName);
        }

        public bool IsPlayedInLibrary(SteamStoreItem item, SteamStorePersonalizationContext context)
        {
            if (item == null || context == null)
            {
                return false;
            }

            if (item.AppId > 0 && context.PlayedSteamAppIds != null && context.PlayedSteamAppIds.Contains(item.AppId))
            {
                return true;
            }

            var normalizedName = NormalizeName(item.Name);
            return !string.IsNullOrWhiteSpace(normalizedName) &&
                   context.PlayedNormalizedNames != null &&
                   context.PlayedNormalizedNames.Contains(normalizedName);
        }

        public bool LooksLikeNonGameContent(SteamStoreItem item)
        {
            if (item == null)
            {
                return true;
            }

            var title = item.Name ?? string.Empty;
            var n = NormalizeName(title);
            var text = NormalizeName(string.Join(" ", new[]
            {
                title,
                item.Source,
                string.Join(" ", item.Genres ?? new List<string>()),
                string.Join(" ", item.Categories ?? new List<string>()),
                string.Join(" ", item.Tags ?? new List<string>())
            }));

            if (string.IsNullOrWhiteSpace(n))
            {
                return true;
            }

            if (LooksLikeBlockedStoreText(n, text))
            {
                return true;
            }

            return false;
        }

        public bool LooksLikeBadRecommendationSeed(SteamStoreRecommendationSeed seed)
        {
            if (seed == null || seed.AppId <= 0)
            {
                return true;
            }

            var rawName = (seed.Name ?? string.Empty).ToLowerInvariant();
            var rawSource = (seed.Source ?? string.Empty).ToLowerInvariant();
            var rawText = (rawName + " " + rawSource).Trim();

            var name = NormalizeName(seed.Name ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            // Seeds must be real games. Do this check on raw text first because NormalizeName
            // intentionally removes words like "demo" when comparing owned titles.
            if (HasToken(rawText, "demo") ||
                HasToken(rawText, "prologue") ||
                HasToken(rawText, "playtest") ||
                HasToken(rawText, "beta") ||
                rawText.Contains("open beta") ||
                rawText.Contains("closed beta") ||
                rawText.Contains("technical test") ||
                rawText.Contains("stress test") ||
                rawText.Contains("benchmark") ||
                rawText.Contains("dedicated server") ||
                rawText.Contains("server tool") ||
                rawText.Contains("sdk") ||
                rawText.Contains("wallpaper") ||
                rawText.Contains("wallpaper engine") ||
                rawText.Contains("soundtrack") ||
                rawText.Contains("artbook") ||
                rawText.Contains("art book"))
            {
                return true;
            }

            return false;
        }

        private bool LooksLikeBlockedStoreText(string normalizedTitle, string normalizedText)
        {
            var n = normalizedTitle ?? string.Empty;
            var text = normalizedText ?? string.Empty;

            if (HasToken(n, "demo") ||
                HasToken(n, "prologue") ||
                HasToken(n, "playtest") ||
                HasToken(n, "ost") ||
                n.Contains("soundtrack") ||
                n.Contains("art book") ||
                n.Contains("artbook") ||
                n.Contains("wallpaper") ||
                n.Contains("supporter pack") ||
                n.Contains("upgrade pack") ||
                n.Contains("season pass") ||
                n.Contains("dedicated server") ||
                n.Contains("server tool") ||
                n.Contains("sdk") ||
                n.EndsWith(" bundle") ||
                n.Contains(" bundle ") ||
                n.EndsWith(" dlc") ||
                n.Contains(" dlc "))
            {
                return true;
            }

            return text.Contains("adult only") ||
                   text.Contains("sexual content") ||
                   text.Contains("nudity") ||
                   text.Contains("hentai") ||
                   text.Contains("nsfw") ||
                   text.Contains("porn") ||
                   text.Contains("sexual") ||
                   text.Contains("erotic") ||
                   text.Contains("nude") ||
                   text.Contains("naked") ||
                   text.Contains("lewd") ||
                   text.Contains("ecchi") ||
                   text.Contains("18+");
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var s = name.ToLowerInvariant();
            s = s.Replace("™", string.Empty)
                 .Replace("®", string.Empty)
                 .Replace("©", string.Empty)
                 .Replace(":", " ")
                 .Replace("-", " ")
                 .Replace("_", " ")
                 .Replace("’", "'");

            s = Regex.Replace(s, @"\b(game of the year|goty|deluxe|ultimate|complete|definitive|edition|remastered|remaster|soundtrack|ost|demo)\b", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[^a-z0-9]+", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }

        private static bool HasToken(string value, string token)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return Regex.IsMatch(
                value,
                $@"(^|[^a-z0-9]){Regex.Escape(token)}($|[^a-z0-9])",
                RegexOptions.IgnoreCase);
        }
    }
}
