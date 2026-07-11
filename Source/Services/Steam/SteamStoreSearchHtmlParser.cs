using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace AnikiHelper.Services
{
    public static class SteamStoreSearchHtmlParser
    {
        public static List<SteamStoreItem> ParseStoreRows(string html, string sourceName)
        {
            var results = new List<SteamStoreItem>();

            if (string.IsNullOrWhiteSpace(html))
            {
                return results;
            }

            var rowRegex = new Regex(
                @"<a\s+[\s\S]*?class=""[^""]*search_result_row[^""]*""[\s\S]*?</a>",
                RegexOptions.IgnoreCase);

            var rows = rowRegex.Matches(html);
            foreach (Match row in rows)
            {
                var item = ParseRow(row.Value, sourceName, results.Count + 1);
                if (item != null)
                {
                    results.Add(item);
                }
            }

            // Some Steam recommendation pages expose extra app links outside search_result_row.
            // For Steam For You, keep those links as extra Steam-provided candidates so the
            // recommendation service can sort a larger candidate pool by popularity.
            // For other Store pages, preserve the old behavior and only use app-link fallback
            // when no normal rows were found.
            var isSteamRecommendationSource = IsSteamRecommendationSource(sourceName);
            var shouldAddAppLinkCandidates =
                results.Count == 0 ||
                isSteamRecommendationSource;

            if (shouldAddAppLinkCandidates)
            {
                AddAppLinkCandidates(html, sourceName, results, isSteamRecommendationSource ? 256 : 40);
            }

            return results
                .Where(x => x != null && x.AppId > 0)
                .GroupBy(x => x.AppId)
                .Select(x => x.First())
                .ToList();
        }

        private static bool IsSteamRecommendationSource(string sourceName)
        {
            return string.Equals(sourceName, "Steam For You", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sourceName, "Steam Recommender", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddAppLinkCandidates(string html, string sourceName, List<SteamStoreItem> results, int maxResults)
        {
            if (string.IsNullOrWhiteSpace(html) || results == null || results.Count >= maxResults)
            {
                return;
            }

            var seen = new HashSet<int>(results.Where(x => x != null).Select(x => x.AppId));

            // 1) Best case: real Steam app links with a slug. This preserves the Steam page order
            // and gives a usable title before appdetails enrichment.
            var appLinkPatterns = new[]
            {
                @"store\.steampowered\.com/app/(?<appid>\d+)(?:/(?<slug>[^/""?]+))?",
                @"(?:href=)?[""']?/app/(?<appid>\d+)(?:/(?<slug>[^/""'?#]+))?"
            };

            foreach (var appPattern in appLinkPatterns)
            {
                var appRegex = new Regex(appPattern, RegexOptions.IgnoreCase);
                foreach (Match match in appRegex.Matches(html))
                {
                    var slug = match.Groups["slug"].Success ? match.Groups["slug"].Value : string.Empty;
                    AddCandidateFromAppIdMatch(results, seen, sourceName, maxResults, match.Groups["appid"].Value, slug);
                    if (results.Count >= maxResults)
                    {
                        return;
                    }
                }
            }

            // 2) Interactive Recommender can expose app ids in JS/React data without normal
            // search_result_row anchors. Keep these only as extra Steam-provided candidates.
            var idPatterns = new[]
            {
                @"data-ds-appid=[""'](?<appid>\d+)[""']",
                @"data-appid=[""'](?<appid>\d+)[""']",
                @"data-app-id=[""'](?<appid>\d+)[""']",
                @"[""']appid[""']\s*:\s*[""']?(?<appid>\d+)[""']?",
                @"[""']app_id[""']\s*:\s*[""']?(?<appid>\d+)[""']?",
                @"[""']steam_appid[""']\s*:\s*[""']?(?<appid>\d+)[""']?"
            };

            foreach (var pattern in idPatterns)
            {
                foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
                {
                    AddCandidateFromAppIdMatch(results, seen, sourceName, maxResults, match.Groups["appid"].Value, string.Empty);
                    if (results.Count >= maxResults)
                    {
                        return;
                    }
                }
            }
        }

        private static void AddCandidateFromAppIdMatch(List<SteamStoreItem> results, HashSet<int> seen, string sourceName, int maxResults, string appIdText, string slug)
        {
            if (results == null || seen == null || results.Count >= maxResults)
            {
                return;
            }

            if (!int.TryParse(appIdText, out var appId) || appId <= 0)
            {
                return;
            }

            if (!seen.Add(appId))
            {
                return;
            }

            var fallbackName = Clean((slug ?? string.Empty).Replace("_", " ").Replace("-", " "));
            if (string.IsNullOrWhiteSpace(fallbackName))
            {
                fallbackName = "Steam App " + appId;
            }

            results.Add(new SteamStoreItem
            {
                AppId = appId,
                Name = fallbackName,
                StoreUrl = $"https://store.steampowered.com/app/{appId}/",
                HeaderImageUrl = BuildSteamAppImageUrl(appId, "header.jpg"),
                CapsuleImageUrl = BuildSteamAppImageUrl(appId, "capsule_616x353.jpg"),
                SearchImageUrl = string.Empty,
                Source = sourceName ?? string.Empty,
                SteamRank = results.Count + 1
            });
        }

        private static SteamStoreItem ParseRow(string block, string sourceName, int rank)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                return null;
            }

            var appIdMatch = Regex.Match(block, @"data-ds-appid=""(?<appid>\d+)""", RegexOptions.IgnoreCase);
            if (!appIdMatch.Success)
            {
                appIdMatch = Regex.Match(block, @"store\.steampowered\.com/app/(?<appid>\d+)/", RegexOptions.IgnoreCase);
            }

            if (!appIdMatch.Success || !int.TryParse(appIdMatch.Groups["appid"].Value, out var appId) || appId <= 0)
            {
                return null;
            }

            var titleMatch = Regex.Match(block, @"<span class=""title"">(?<title>[\s\S]*?)</span>", RegexOptions.IgnoreCase);
            var title = titleMatch.Success ? Clean(titleMatch.Groups["title"].Value) : string.Empty;

            if (string.IsNullOrWhiteSpace(title))
            {
                var ariaMatch = Regex.Match(block, @"aria-label=""(?<title>[^""]+)""", RegexOptions.IgnoreCase);
                title = ariaMatch.Success ? Clean(ariaMatch.Groups["title"].Value) : string.Empty;
            }

            var urlMatch = Regex.Match(block, @"href=""(?<url>https://store\.steampowered\.com/app/\d+/[^""]*)""", RegexOptions.IgnoreCase);
            var url = urlMatch.Success ? WebUtility.HtmlDecode(urlMatch.Groups["url"].Value) : $"https://store.steampowered.com/app/{appId}/";

            if (string.IsNullOrWhiteSpace(title) && urlMatch.Success)
            {
                var slugMatch = Regex.Match(url, @"/app/\d+/(?<slug>[^/?#]+)", RegexOptions.IgnoreCase);
                if (slugMatch.Success)
                {
                    title = Clean(slugMatch.Groups["slug"].Value.Replace("_", " ").Replace("-", " "));
                }
            }

            var dateMatch = Regex.Match(block, @"<div class=""search_released responsive_secondrow"">\s*(?<date>[\s\S]*?)\s*</div>", RegexOptions.IgnoreCase);
            var date = dateMatch.Success ? Clean(dateMatch.Groups["date"].Value) : string.Empty;

            var priceMatch = Regex.Match(block, @"data-price-final=""(?<price>\d+)""", RegexOptions.IgnoreCase);
            var discountMatch = Regex.Match(block, @"<div class=""[^""]*discount_pct[^""]*"">\s*(?<discount>-[0-9]+%)\s*</div>", RegexOptions.IgnoreCase);
            var finalPriceMatch = Regex.Match(block, @"<div class=""[^""]*discount_final_price[^""]*"">\s*(?<price>[\s\S]*?)\s*</div>", RegexOptions.IgnoreCase);
            var originalPriceMatch = Regex.Match(block, @"<div class=""[^""]*discount_original_price[^""]*"">\s*(?<price>[\s\S]*?)\s*</div>", RegexOptions.IgnoreCase);
            var priceFinal = 0;
            if (priceMatch.Success)
            {
                int.TryParse(priceMatch.Groups["price"].Value, out priceFinal);
            }

            var rowImage = FindBestSteamImageUrl(block, appId);
            var fallbackHeader = BuildSteamAppImageUrl(appId, "header.jpg");
            var fallbackCapsule = BuildSteamAppImageUrl(appId, "capsule_616x353.jpg");

            // Keep deterministic Steam app URLs as the primary HQ targets, but also keep the
            // original row image. Some Steam rows are bundles/packages/preorders: the deterministic
            // /steam/apps/{appid}/header.jpg can 404 even though the search row has a valid image.
            var discountDisplay = discountMatch.Success ? Clean(discountMatch.Groups["discount"].Value) : string.Empty;
            var discountPercent = ParseDiscountPercent(discountDisplay);

            return new SteamStoreItem
            {
                AppId = appId,
                Name = title,
                StoreUrl = url,
                Source = sourceName ?? string.Empty,
                SteamRank = rank,
                ReleaseDateDisplay = date,
                CapsuleImageUrl = fallbackCapsule,
                HeaderImageUrl = fallbackHeader,
                SearchImageUrl = rowImage,
                FinalPrice = priceFinal > 0 ? priceFinal.ToString() : string.Empty,
                FinalPriceDisplay = finalPriceMatch.Success ? Clean(finalPriceMatch.Groups["price"].Value) : string.Empty,
                OriginalPriceDisplay = originalPriceMatch.Success ? Clean(originalPriceMatch.Groups["price"].Value) : string.Empty,
                DiscountPercent = discountPercent,
                DiscountDisplay = discountDisplay
            };
        }

        private static int ParseDiscountPercent(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var match = Regex.Match(value, @"-?(?<value>\d+)%", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["value"].Value, out var percent))
            {
                return Math.Max(0, percent);
            }

            return 0;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(value, "<.*?>", string.Empty);
            cleaned = WebUtility.HtmlDecode(cleaned);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }


        private static string FindBestSteamImageUrl(string block, int appId)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                return string.Empty;
            }

            var candidates = new List<string>();
            var regex = new Regex(@"https?://[^""'\s,<>]+?(?:\.jpg|\.png|\.webp)(?:\?[^""'\s,<>]*)?", RegexOptions.IgnoreCase);
            foreach (Match match in regex.Matches(block))
            {
                var value = WebUtility.HtmlDecode(match.Value);
                if (IsUsableSteamImageUrl(value))
                {
                    candidates.Add(value);
                }
            }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => GetImageCandidateScore(appId, x))
                .FirstOrDefault() ?? string.Empty;
        }

        private static int GetImageCandidateScore(int appId, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var score = 1;
            var lower = value.ToLowerInvariant();
            var appToken = "/apps/" + appId.ToString(CultureInfo.InvariantCulture) + "/";

            if (lower.IndexOf(appToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 100;
            }

            if (lower.IndexOf("capsule_616x353", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 80;
            }
            else if (lower.IndexOf("header", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 60;
            }
            else if (lower.IndexOf("capsule", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 30;
            }

            if (lower.IndexOf("capsule_sm_120", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("capsule_184x69", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("capsule_231x87", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("search_capsule", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score -= 40;
            }

            return score;
        }

        private static bool IsUsableSteamImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.IndexOf("/public/shared/images/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("footerLogo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("footer_logo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("valve_new", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return value.IndexOf("steamstatic.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("akamaihd.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("steamcdn", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FirstValidSteamAppImageUrl(int appId, params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (IsValidSteamAppImageUrl(appId, value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static bool IsValidSteamAppImageUrl(int appId, string value)
        {
            if (appId <= 0 || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.IndexOf("/public/shared/images/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("footerLogo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("footer_logo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("valve_new", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            var appToken1 = "/apps/" + appId + "/";
            var appToken2 = "/app/" + appId;

            return value.IndexOf(appToken1, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(appToken2, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildSteamAppImageUrl(int appId, string fileName)
        {
            if (appId <= 0 || string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/{fileName}";
        }
    }
}
