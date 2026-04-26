using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AnikiHelper.Services
{
    public class SteamStoreService
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly string steamStoreRootFolder;
        private readonly string storeCacheFolder;
        private readonly string detailsCacheFolder;
        private readonly string imageCacheFolder;
        private static readonly HttpClient httpClient = new HttpClient();

        private const int MaxCachedImages = 400;

        public SteamStoreService(IPlayniteAPI playniteApi, string pluginUserDataPath)
        {
            this.playniteApi = playniteApi;
            steamStoreRootFolder = Path.Combine(pluginUserDataPath, "SteamStore");
            storeCacheFolder = Path.Combine(steamStoreRootFolder, "StoreCache");
            detailsCacheFolder = Path.Combine(steamStoreRootFolder, "DetailsCache");
            imageCacheFolder = Path.Combine(steamStoreRootFolder, "ImageCache");

            Directory.CreateDirectory(steamStoreRootFolder);
            Directory.CreateDirectory(storeCacheFolder);
            Directory.CreateDirectory(detailsCacheFolder);
            Directory.CreateDirectory(imageCacheFolder);
            MigrateLegacySteamStoreFolders(pluginUserDataPath);
        }

        public async Task<List<SteamStoreItem>> GetDealsAsync(string language, string countryCode, TimeSpan maxAge)
        {
            
            var cacheKey = $"deals_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAsync(cacheKey, maxAge);
            if (cached != null)
            {
                return cached.Items ?? new List<SteamStoreItem>();
            }

            var items = await FetchDealsAsync(language, countryCode);
            await SaveCacheAsync(cacheKey, items);
            return items;
        }

        public async Task<List<SteamStoreItem>> GetNewReleasesAsync(string language, string countryCode, TimeSpan maxAge)
        {
            var cacheKey = $"newreleases_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAsync(cacheKey, maxAge);
            if (cached != null)
            {
                return cached.Items ?? new List<SteamStoreItem>();
            }

            var items = await FetchNewReleasesAsync(language, countryCode);
            await SaveCacheAsync(cacheKey, items);
            return items;
        }

        public async Task<List<SteamStoreItem>> GetTopSellersAsync(string language, string countryCode, TimeSpan maxAge)
        {
            var cacheKey = $"topsellers_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAsync(cacheKey, maxAge);
            if (cached != null)
            {
                return cached.Items ?? new List<SteamStoreItem>();
            }

            var items = await FetchTopSellersAsync(language, countryCode);
            await SaveCacheAsync(cacheKey, items);
            return items;
        }

        public async Task<List<SteamStoreItem>> GetSpotlightAsync(string language, string countryCode, TimeSpan maxAge)
        {
            var cacheKey = $"spotlight_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAsync(cacheKey, maxAge);
            if (cached != null)
            {
                return cached.Items ?? new List<SteamStoreItem>();
            }

            var items = await FetchSpotlightAsync(language, countryCode);
            await SaveCacheAsync(cacheKey, items);
            return items;
        }

        public async Task<List<SteamStoreItem>> GetDealsFromCacheOnlyAsync(string language, string countryCode)
        {
            var cacheKey = $"deals_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
            return cached?.Items ?? new List<SteamStoreItem>();
        }

        public async Task<List<SteamStoreItem>> GetNewReleasesFromCacheOnlyAsync(string language, string countryCode)
        {
            var cacheKey = $"newreleases_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
            return cached?.Items ?? new List<SteamStoreItem>();
        }

       

        public async Task<List<SteamStoreItem>> GetTopSellersFromCacheOnlyAsync(string language, string countryCode)
        {
            var cacheKey = $"topsellers_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
            return cached?.Items ?? new List<SteamStoreItem>();
        }

        public async Task<List<SteamStoreItem>> GetSpotlightFromCacheOnlyAsync(string language, string countryCode)
        {
            var cacheKey = $"spotlight_{language}_{countryCode}".ToLowerInvariant();
            var cached = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
            return cached?.Items ?? new List<SteamStoreItem>();
        }

        public async Task<bool> IsAnyStoreCacheMissingOrExpiredAsync(string language, string countryCode, TimeSpan maxAge)
        {
            var dealsKey = $"deals_{language}_{countryCode}".ToLowerInvariant();
            var newKey = $"newreleases_{language}_{countryCode}".ToLowerInvariant();
            var topSellersKey = $"topsellers_{language}_{countryCode}".ToLowerInvariant();
            var spotlightKey = $"spotlight_{language}_{countryCode}".ToLowerInvariant();

            return await IsCacheMissingOrExpiredAsync(dealsKey, maxAge).ConfigureAwait(false)
                || await IsCacheMissingOrExpiredAsync(newKey, maxAge).ConfigureAwait(false)
                || await IsCacheMissingOrExpiredAsync(topSellersKey, maxAge).ConfigureAwait(false)
                || await IsCacheMissingOrExpiredAsync(spotlightKey, maxAge).ConfigureAwait(false);
        }

        private async Task<bool> IsCacheMissingOrExpiredAsync(string cacheKey, TimeSpan maxAge)
        {
            var cache = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
            if (cache == null)
            {
                return true;
            }

            return (DateTime.UtcNow - cache.LastUpdatedUtc) > maxAge;
        }

        private async Task<List<SteamStoreItem>> FetchDealsAsync(string language, string countryCode)
        {
            return await FetchCategory(language, countryCode, "specials", "STORE Deals");
        }

        private async Task<List<SteamStoreItem>> FetchNewReleasesAsync(string language, string countryCode)
        {
            var results = await FetchCategory(language, countryCode, "new_releases", "STORE NewReleases");

            return results
                .Where(x =>
                    x != null &&
                    x.AppId > 0 &&
                    !string.IsNullOrWhiteSpace(x.Name) &&
                    string.IsNullOrWhiteSpace(x.DiscountDisplay))
                .GroupBy(x => x.AppId)
                .Select(g => g.First())
                .ToList();
        }

        

        private async Task<List<SteamStoreItem>> FetchTopSellersAsync(string language, string countryCode)
        {
            var results = new List<SteamStoreItem>();

            try
            {
                var url = $"https://store.steampowered.com/api/featuredcategories?cc={countryCode}&l={language}";

                var json = await httpClient.GetStringAsync(url);

                var root = JObject.Parse(json);
                var items = root["top_sellers"]?["items"] as JArray;

                if (items == null)
                {
                    return results;
                }

                foreach (var item in items)
                {
                    try
                    {
                        int appId = item["id"]?.Value<int>() ?? 0;
                        string name = item["name"]?.Value<string>() ?? string.Empty;
                        int discount = item["discount_percent"]?.Value<int>() ?? 0;

                        string finalPrice = string.Empty;
                        string originalPrice = string.Empty;
                        string currency = string.Empty;

                        var finalPriceToken = item["final_price"];
                        if (finalPriceToken != null && finalPriceToken.Type != JTokenType.Null)
                        {
                            finalPrice = FormatSteamPrice(finalPriceToken.Value<long>());
                        }

                        var originalPriceToken = item["original_price"];
                        if (originalPriceToken != null && originalPriceToken.Type != JTokenType.Null)
                        {
                            originalPrice = FormatSteamPrice(originalPriceToken.Value<long>());
                        }

                        var currencyToken = item["currency"];
                        if (currencyToken != null && currencyToken.Type != JTokenType.Null)
                        {
                            currency = currencyToken.Value<string>();
                        }

                        var capsuleUrl = (item["large_capsule_image"] != null && item["large_capsule_image"].Type != JTokenType.Null)
                            ? item["large_capsule_image"].Value<string>()
                            : string.Empty;

                        var headerImage = (item["header_image"] != null && item["header_image"].Type != JTokenType.Null)
                            ? item["header_image"].Value<string>()
                            : string.Empty;

                        var localCapsulePath = await GetOrCacheImageAsync(capsuleUrl, appId);
                        var localHeaderPath = await GetOrCacheImageAsync(headerImage, appId + 1000000);

                        results.Add(new SteamStoreItem
                        {
                            AppId = appId,
                            Name = name,
                            CapsuleImageUrl = capsuleUrl,
                            CapsuleImageLocalPath = localCapsulePath,
                            HeaderImageLocalPath = localHeaderPath,
                            HeaderImageUrl = headerImage,
                            FinalPrice = finalPrice,
                            OriginalPrice = originalPrice,
                            DiscountPercent = discount,
                            Currency = currency,
                            FinalPriceDisplay = FormatPriceDisplay(finalPrice, currency),
                            OriginalPriceDisplay = FormatOriginalPriceDisplay(originalPrice, finalPrice, currency),
                            DiscountDisplay = FormatDiscountDisplay(discount, finalPrice, originalPrice),
                            StoreUrl = appId > 0 ? $"https://store.steampowered.com/app/{appId}" : string.Empty
                        });
                    }
                    catch (Exception exItem)
                    {
                        logger.Warn(exItem, "STORE TopSellers item parse error");
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex, "STORE ERROR TopSellers");
            }


            var finalResults = results
                .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.AppId)
                .Select(g => g.First())
                .ToList();

            var filteredResults = finalResults
                .Where(x => !ShouldExcludeStoreItem(x.Name))
                .ToList();

            return filteredResults;
        }

        private bool ShouldExcludeStoreItem(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var text = name.ToLowerInvariant();

            string[] blockedKeywords =
            {
                "hentai",
                "adult",
                "nsfw",
                "porn",
                "sex",
                "sexual",
                "erotic",
                "18+",
                "succubus"
            };

            foreach (var keyword in blockedKeywords)
            {
                if (text.Contains(keyword))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<List<SteamStoreItem>> FetchCategory(string language, string countryCode, string category, string logPrefix)
        {
            var results = new List<SteamStoreItem>();

            try
            {
                var url = $"https://store.steampowered.com/api/featuredcategories?cc={countryCode}&l={language}";

                var json = await httpClient.GetStringAsync(url);

                var root = JObject.Parse(json);
                var items = root[category]?["items"] as JArray;


                if (items == null)
                {
                    logger.Warn($"{logPrefix}: {category}.items is NULL");
                    return results;
                }

                foreach (var item in items)
                {
                    try
                    {
                        int appId = item["id"]?.Value<int>() ?? 0;
                        string name = item["name"]?.Value<string>() ?? string.Empty;
                        int discount = item["discount_percent"]?.Value<int>() ?? 0;

                        string finalPrice = string.Empty;
                        string originalPrice = string.Empty;
                        string currency = string.Empty;

                        var finalPriceToken = item["final_price"];
                        if (finalPriceToken != null && finalPriceToken.Type != JTokenType.Null)
                        {
                            finalPrice = FormatSteamPrice(finalPriceToken.Value<long>());
                        }

                        var originalPriceToken = item["original_price"];
                        if (originalPriceToken != null && originalPriceToken.Type != JTokenType.Null)
                        {
                            originalPrice = FormatSteamPrice(originalPriceToken.Value<long>());
                        }

                        var currencyToken = item["currency"];
                        if (currencyToken != null && currencyToken.Type != JTokenType.Null)
                        {
                            currency = currencyToken.Value<string>();
                        }

                        var capsuleUrl = (item["large_capsule_image"] != null && item["large_capsule_image"].Type != JTokenType.Null)
                            ? item["large_capsule_image"].Value<string>()
                            : string.Empty;

                        var headerImage = (item["header_image"] != null && item["header_image"].Type != JTokenType.Null)
                            ? item["header_image"].Value<string>()
                            : string.Empty;

                        var localCapsulePath = await GetOrCacheImageAsync(capsuleUrl, appId);
                        var localHeaderPath = await GetOrCacheImageAsync(headerImage, appId + 1000000);

                        results.Add(new SteamStoreItem
                        {
                            AppId = appId,
                            Name = name,
                            CapsuleImageUrl = capsuleUrl,
                            CapsuleImageLocalPath = localCapsulePath,
                            HeaderImageUrl = headerImage,
                            HeaderImageLocalPath = localHeaderPath,
                            FinalPrice = finalPrice,
                            OriginalPrice = originalPrice,
                            DiscountPercent = discount,
                            Currency = currency,
                            FinalPriceDisplay = FormatPriceDisplay(finalPrice, currency),
                            OriginalPriceDisplay = FormatOriginalPriceDisplay(originalPrice, finalPrice, currency),
                            DiscountDisplay = FormatDiscountDisplay(discount, finalPrice, originalPrice),
                            StoreUrl = appId > 0 ? $"https://store.steampowered.com/app/{appId}" : string.Empty
                        });
                    }
                    catch (Exception exItem)
                    {
                        logger.Warn(exItem, $"{logPrefix}: item parse error");
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex, $"{logPrefix}: fetch failed");
            }

            return results
                .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.AppId)
                .Select(g => g.First())
                .ToList();
        }

        private async Task<List<SteamStoreItem>> FetchSpotlightAsync(string language, string countryCode)
        {
            var results = new List<SteamStoreItem>();

            try
            {
                var url = $"https://store.steampowered.com/api/featuredcategories?cc={countryCode}&l={language}";
                var json = await httpClient.GetStringAsync(url);

                var root = JObject.Parse(json);

                foreach (var property in root.Properties())
                {
                    if (!(property.Value is JObject section))
                    {
                        continue;
                    }

                    var sectionId = section["id"]?.Value<string>() ?? string.Empty;

                    if (!string.Equals(sectionId, "cat_spotlight", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(sectionId, "cat_dailydeal", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var items = section["items"] as JArray;
                    if (items == null)
                    {
                        continue;
                    }

                    foreach (var item in items)
                    {
                        try
                        {
                            var name = item["name"]?.Value<string>() ?? string.Empty;
                            var urlItem = item["url"]?.Value<string>() ?? string.Empty;

                            var headerImage = (item["header_image"] != null && item["header_image"].Type != JTokenType.Null)
                                ? item["header_image"].Value<string>()
                                : string.Empty;

                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(headerImage))
                            {
                                continue;
                            }

                            int appId = 0;

                            if (!string.IsNullOrWhiteSpace(urlItem))
                            {
                                var marker = "/app/";
                                var idx = urlItem.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                                if (idx >= 0)
                                {
                                    var idPart = new string(
                                        urlItem.Substring(idx + marker.Length)
                                            .TakeWhile(char.IsDigit)
                                            .ToArray());

                                    int.TryParse(idPart, out appId);
                                }
                            }

                            var localHeaderPath = await GetOrCacheImageAsync(headerImage, appId > 0 ? appId + 2000000 : name.GetHashCode());

                            results.Add(new SteamStoreItem
                            {
                                AppId = appId,
                                Name = name,
                                HeaderImageUrl = headerImage,
                                HeaderImageLocalPath = localHeaderPath,
                                CapsuleImageUrl = headerImage,
                                CapsuleImageLocalPath = localHeaderPath,
                                FinalPrice = string.Empty,
                                OriginalPrice = string.Empty,
                                DiscountPercent = 0,
                                Currency = string.Empty,
                                FinalPriceDisplay = string.Empty,
                                OriginalPriceDisplay = string.Empty,
                                DiscountDisplay = string.Empty,
                                StoreUrl = urlItem
                            });
                        }
                        catch (Exception exItem)
                        {
                            logger.Warn(exItem, "STORE Spotlight item parse error");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "STORE ERROR Spotlight");
            }

            return results
                .Where(x => x != null &&
                            !string.IsNullOrWhiteSpace(x.Name) &&
                            !string.IsNullOrWhiteSpace(x.HeaderImageUrl) &&
                            !ShouldExcludeStoreItem(x.Name))
                .GroupBy(x => !string.IsNullOrWhiteSpace(x.StoreUrl) ? x.StoreUrl : x.Name)
                .Select(g => g.First())
                .Take(6)
                .ToList();
        }

        private static string FormatSteamPrice(long valueInCents)
        {
            return (valueInCents / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FormatPriceDisplay(string price, string currency)
        {
            if (string.IsNullOrWhiteSpace(price))
            {
                return string.Empty;
            }

            if (string.Equals(price, "0.00", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(price, "0", StringComparison.OrdinalIgnoreCase))
            {
                return "Free";
            }

            currency = (currency ?? string.Empty).Trim().ToUpperInvariant();

            switch (currency)
            {
                case "USD":
                    return "$" + price;
                case "EUR":
                    return price + " €";
                case "GBP":
                    return "£" + price;
                case "JPY":
                    return "¥" + price;
                case "BRL":
                    return "R$ " + price;
                case "PLN":
                    return price + " zł";
                case "RUB":
                    return price + " ₽";
                case "CAD":
                    return "C$" + price;
                default:
                    return string.IsNullOrWhiteSpace(currency)
                        ? price
                        : price + " " + currency;
            }
        }

        private static string FormatDiscountDisplay(int discountPercent, string finalPrice, string originalPrice)
        {
            if (discountPercent <= 0)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(finalPrice) || string.IsNullOrWhiteSpace(originalPrice))
            {
                return string.Empty;
            }

            if (string.Equals(finalPrice, originalPrice, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return "-" + discountPercent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        private sealed class SteamAppDetailsEnvelope
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("data")]
            public SteamAppDetailsData Data { get; set; }
        }

        private sealed class SteamAppDetailsData
        {
            [JsonProperty("short_description")]
            public string ShortDescription { get; set; }

            [JsonProperty("release_date")]
            public SteamReleaseDateData ReleaseDate { get; set; }

            [JsonProperty("genres")]
            public List<SteamNamedItem> Genres { get; set; }

            [JsonProperty("categories")]
            public List<SteamNamedItem> Categories { get; set; }

            [JsonProperty("developers")]
            public List<string> Developers { get; set; }

            [JsonProperty("publishers")]
            public List<string> Publishers { get; set; }

            [JsonProperty("controller_support")]
            public string ControllerSupport { get; set; }

            [JsonProperty("supported_languages")]
            public string SupportedLanguages { get; set; }

            [JsonProperty("background")]
            public string Background { get; set; }

            [JsonProperty("background_raw")]
            public string BackgroundRaw { get; set; }

            [JsonProperty("price_overview")]
            public SteamPriceOverviewData PriceOverview { get; set; }

            [JsonProperty("metacritic")]
            public SteamMetacriticData Metacritic { get; set; }

            [JsonProperty("recommendations")]
            public SteamRecommendationsData Recommendations { get; set; }

            [JsonProperty("achievements")]
            public SteamAchievementsData Achievements { get; set; }

            [JsonProperty("screenshots")]
            public List<SteamScreenshotData> Screenshots { get; set; }

            [JsonProperty("dlc")]
            public List<int> Dlc { get; set; }
        }

        private sealed class SteamReleaseDateData
        {
            [JsonProperty("coming_soon")]
            public bool ComingSoon { get; set; }

            [JsonProperty("date")]
            public string Date { get; set; }

        }

        private sealed class SteamNamedItem
        {
            [JsonProperty("description")]
            public string Description { get; set; }
        }

        private sealed class SteamPriceOverviewData
        {
            [JsonProperty("initial_formatted")]
            public string InitialFormatted { get; set; }

            [JsonProperty("final_formatted")]
            public string FinalFormatted { get; set; }

            [JsonProperty("discount_percent")]
            public int DiscountPercent { get; set; }
        }

        private sealed class SteamMetacriticData
        {
            [JsonProperty("score")]
            public int Score { get; set; }
        }

        private sealed class SteamRecommendationsData
        {
            [JsonProperty("total")]
            public int Total { get; set; }
        }

        private sealed class SteamAchievementsData
        {
            [JsonProperty("total")]
            public int Total { get; set; }
        }

        private sealed class SteamScreenshotData
        {
            [JsonProperty("path_thumbnail")]
            public string PathThumbnail { get; set; }

            [JsonProperty("path_full")]
            public string PathFull { get; set; }
        }

        public async Task EnrichStoreItemDetailsAsync(SteamStoreItem item, string language, string countryCode)
        {
            if (item == null || item.AppId <= 0)
            {
                return;
            }

            var cacheKey = $"appdetails_{item.AppId}_{language}_{countryCode}".ToLowerInvariant();
            var maxAge = TimeSpan.FromHours(24);

            // 1) Cache frais
            var freshCache = LoadAppDetailsCache(cacheKey, maxAge);
            if (freshCache != null)
            {
                ApplyDetailsCacheToItem(item, freshCache);
                return;
            }

            try
            {
                var url = $"https://store.steampowered.com/api/appdetails?appids={item.AppId}&l={language}&cc={countryCode}";

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                using (var response = await httpClient.GetAsync(url, cts.Token))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.Warn($"STORE details failed status={(int)response.StatusCode} for AppId={item.AppId}");

                        var expiredCache = LoadAppDetailsCache(cacheKey, maxAge, allowExpiredFallback: true);
                        if (expiredCache != null)
                        {
                            ApplyDetailsCacheToItem(item, expiredCache);
                        }

                        return;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        var expiredCache = LoadAppDetailsCache(cacheKey, maxAge, allowExpiredFallback: true);
                        if (expiredCache != null)
                        {
                            ApplyDetailsCacheToItem(item, expiredCache);
                        }

                        return;
                    }

                    var root = JsonConvert.DeserializeObject<Dictionary<string, SteamAppDetailsEnvelope>>(json);
                    if (root == null || !root.TryGetValue(item.AppId.ToString(), out var envelope))
                    {
                        var expiredCache = LoadAppDetailsCache(cacheKey, maxAge, allowExpiredFallback: true);
                        if (expiredCache != null)
                        {
                            ApplyDetailsCacheToItem(item, expiredCache);
                        }

                        return;
                    }

                    if (envelope == null || !envelope.Success || envelope.Data == null)
                    {
                        var expiredCache = LoadAppDetailsCache(cacheKey, maxAge, allowExpiredFallback: true);
                        if (expiredCache != null)
                        {
                            ApplyDetailsCacheToItem(item, expiredCache);
                        }

                        return;
                    }

                    item.ShortDescription = envelope.Data.ShortDescription ?? string.Empty;
                    item.ReleaseDateDisplay = envelope.Data.ReleaseDate?.Date ?? string.Empty;
                    item.ComingSoon = envelope.Data.ReleaseDate?.ComingSoon ?? false;
                    item.IsPreorder = item.ComingSoon || IsFutureReleaseDate(item.ReleaseDateDisplay);
                    item.Developers = envelope.Data.Developers ?? new List<string>();
                    item.Publishers = envelope.Data.Publishers ?? new List<string>();
                    item.ControllerSupport = envelope.Data.ControllerSupport ?? string.Empty;
                    item.SupportedLanguages = envelope.Data.SupportedLanguages ?? string.Empty;
                    var backgroundUrl = !string.IsNullOrWhiteSpace(envelope.Data.BackgroundRaw)
                        ? envelope.Data.BackgroundRaw
                        : envelope.Data.Background;

                    if (envelope.Data.PriceOverview != null)
                    {
                        item.FinalPriceDisplay = envelope.Data.PriceOverview.FinalFormatted ?? item.FinalPriceDisplay ?? string.Empty;
                        item.OriginalPriceDisplay = envelope.Data.PriceOverview.InitialFormatted ?? item.OriginalPriceDisplay ?? string.Empty;

                        item.DiscountPercent = envelope.Data.PriceOverview.DiscountPercent;

                        item.DiscountDisplay = envelope.Data.PriceOverview.DiscountPercent > 0
                            ? "-" + envelope.Data.PriceOverview.DiscountPercent.ToString(CultureInfo.InvariantCulture) + "%"
                            : string.Empty;
                    }

                    item.MetacriticScore = envelope.Data.Metacritic?.Score ?? 0;
                    item.RecommendationsTotal = envelope.Data.Recommendations?.Total ?? 0;
                    item.AchievementsTotal = envelope.Data.Achievements?.Total ?? 0;
                    item.DlcCount = envelope.Data.Dlc?.Count ?? 0;

                    if (envelope.Data.Screenshots != null && envelope.Data.Screenshots.Count > 0)
                    {
                        var shot1 = envelope.Data.Screenshots.Count > 0 ? envelope.Data.Screenshots[0]?.PathThumbnail : string.Empty;
                        var shot2 = envelope.Data.Screenshots.Count > 1 ? envelope.Data.Screenshots[1]?.PathThumbnail : string.Empty;
                        var shot3 = envelope.Data.Screenshots.Count > 2 ? envelope.Data.Screenshots[2]?.PathThumbnail : string.Empty;

                        item.Screenshot1Url = shot1 ?? string.Empty;
                        item.Screenshot2Url = shot2 ?? string.Empty;
                        item.Screenshot3Url = shot3 ?? string.Empty;

                        item.Screenshot1LocalPath = await GetOrCacheImageAsync(item.Screenshot1Url, item.AppId + 4000000);
                        item.Screenshot2LocalPath = await GetOrCacheImageAsync(item.Screenshot2Url, item.AppId + 4000001);
                        item.Screenshot3LocalPath = await GetOrCacheImageAsync(item.Screenshot3Url, item.AppId + 4000002);
                    }

                    item.BackgroundImageUrl = backgroundUrl ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(backgroundUrl))
                    {
                        item.BackgroundImageLocalPath = await GetOrCacheImageAsync(backgroundUrl, item.AppId + 3000000);
                    }


                    if (envelope.Data.Genres != null && envelope.Data.Genres.Count > 0)
                    {
                        item.Genres = envelope.Data.Genres
                            .Select(x => x?.Description)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct()
                            .ToList();
                    }

                    if (envelope.Data.Categories != null && envelope.Data.Categories.Count > 0)
                    {
                        item.Categories = envelope.Data.Categories
                            .Select(x => x?.Description)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct()
                            .ToList();
                    }

                    var cacheEntry = new SteamAppDetailsCacheEntry
                    {
                        LastUpdatedUtc = DateTime.UtcNow,
                        AppId = item.AppId,
                        ShortDescription = item.ShortDescription,
                        ReleaseDateDisplay = item.ReleaseDateDisplay,
                        ComingSoon = item.ComingSoon,
                        IsPreorder = item.IsPreorder,
                        Developers = item.Developers ?? new List<string>(),
                        Publishers = item.Publishers ?? new List<string>(),
                        ControllerSupport = item.ControllerSupport,
                        SupportedLanguages = item.SupportedLanguages,
                        Genres = item.Genres ?? new List<string>(),
                        Categories = item.Categories ?? new List<string>(),

                        BackgroundImageUrl = item.BackgroundImageUrl,
                        BackgroundImageLocalPath = item.BackgroundImageLocalPath,

                        FinalPriceDisplay = item.FinalPriceDisplay,
                        OriginalPriceDisplay = item.OriginalPriceDisplay,
                        DiscountDisplay = item.DiscountDisplay,

                        MetacriticScore = item.MetacriticScore,
                        RecommendationsTotal = item.RecommendationsTotal,
                        AchievementsTotal = item.AchievementsTotal,
                        DlcCount = item.DlcCount,

                        Screenshot1Url = item.Screenshot1Url,
                        Screenshot1LocalPath = item.Screenshot1LocalPath,
                        Screenshot2Url = item.Screenshot2Url,
                        Screenshot2LocalPath = item.Screenshot2LocalPath,
                        Screenshot3Url = item.Screenshot3Url,
                        Screenshot3LocalPath = item.Screenshot3LocalPath
                    };

                    SaveAppDetailsCache(cacheKey, cacheEntry);
                }
            }
            catch (OperationCanceledException)
            {
                logger.Warn($"STORE details timeout for AppId={item.AppId}");

                var expiredCache = LoadAppDetailsCache(cacheKey, maxAge, allowExpiredFallback: true);
                if (expiredCache != null)
                {
                    ApplyDetailsCacheToItem(item, expiredCache);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"STORE details enrich failed for AppId={item.AppId}");

                var expiredCache = LoadAppDetailsCache(cacheKey, maxAge, allowExpiredFallback: true);
                if (expiredCache != null)
                {
                    ApplyDetailsCacheToItem(item, expiredCache);
                }
            }
        }

        private void ApplyDetailsCacheToItem(SteamStoreItem item, SteamAppDetailsCacheEntry cache)
        {
            if (item == null || cache == null)
            {
                return;
            }

            item.ShortDescription = cache.ShortDescription ?? string.Empty;
            item.ReleaseDateDisplay = cache.ReleaseDateDisplay ?? string.Empty;
            item.ComingSoon = cache.ComingSoon;
            item.IsPreorder = cache.IsPreorder;
            item.Developers = cache.Developers ?? new List<string>();
            item.Publishers = cache.Publishers ?? new List<string>();
            item.ControllerSupport = cache.ControllerSupport ?? string.Empty;
            item.SupportedLanguages = cache.SupportedLanguages ?? string.Empty;
            item.Genres = cache.Genres ?? new List<string>();
            item.Categories = cache.Categories ?? new List<string>();
            item.BackgroundImageUrl = cache.BackgroundImageUrl ?? string.Empty;
            item.BackgroundImageLocalPath = cache.BackgroundImageLocalPath ?? string.Empty;
            item.FinalPriceDisplay = cache.FinalPriceDisplay ?? item.FinalPriceDisplay ?? string.Empty;
            item.OriginalPriceDisplay = cache.OriginalPriceDisplay ?? item.OriginalPriceDisplay ?? string.Empty;
            item.DiscountDisplay = cache.DiscountDisplay ?? item.DiscountDisplay ?? string.Empty;

            item.MetacriticScore = cache.MetacriticScore;
            item.RecommendationsTotal = cache.RecommendationsTotal;
            item.AchievementsTotal = cache.AchievementsTotal;
            item.DlcCount = cache.DlcCount;

            item.Screenshot1Url = cache.Screenshot1Url ?? string.Empty;
            item.Screenshot1LocalPath = cache.Screenshot1LocalPath ?? string.Empty;
            item.Screenshot2Url = cache.Screenshot2Url ?? string.Empty;
            item.Screenshot2LocalPath = cache.Screenshot2LocalPath ?? string.Empty;
            item.Screenshot3Url = cache.Screenshot3Url ?? string.Empty;
            item.Screenshot3LocalPath = cache.Screenshot3LocalPath ?? string.Empty;
        }

        private bool IsFutureReleaseDate(string releaseDateDisplay)
        {
            if (string.IsNullOrWhiteSpace(releaseDateDisplay))
            {
                return false;
            }

            var formats = new[]
            {
        "MMM d, yyyy",
        "MMM dd, yyyy",
        "MMMM d, yyyy",
        "MMMM dd, yyyy",
        "yyyy",
        "MMM yyyy",
        "MMMM yyyy"
    };

            if (DateTime.TryParseExact(
                releaseDateDisplay,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedDate))
            {
                return parsedDate.Date > DateTime.Today;
            }

            if (DateTime.TryParse(
                releaseDateDisplay,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsedDate))
            {
                return parsedDate.Date > DateTime.Today;
            }

            return false;
        }

        private void MigrateLegacySteamStoreFolders(string pluginUserDataPath)
        {
            try
            {
                var legacyStoreCache = Path.Combine(pluginUserDataPath, "SteamStoreCache");
                var legacyImageCache = Path.Combine(pluginUserDataPath, "SteamStoreImages");

                if (Directory.Exists(legacyStoreCache))
                {
                    foreach (var file in Directory.GetFiles(legacyStoreCache, "*.json"))
                    {
                        try
                        {
                            var fileName = Path.GetFileName(file);
                            var targetFolder = fileName.StartsWith("appdetails_", StringComparison.OrdinalIgnoreCase)
                                ? detailsCacheFolder
                                : storeCacheFolder;

                            var targetPath = Path.Combine(targetFolder, fileName);

                            if (!File.Exists(targetPath))
                            {
                                File.Copy(file, targetPath);
                            }

                            // Si le fichier cible existe maintenant, on supprime l'ancien
                            if (File.Exists(targetPath))
                            {
                                File.Delete(file);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, $"Steam Store legacy cache file migration failed: {file}");
                        }
                    }

                    TryDeleteDirectoryIfEmpty(legacyStoreCache);
                }

                if (Directory.Exists(legacyImageCache))
                {
                    foreach (var file in Directory.GetFiles(legacyImageCache))
                    {
                        try
                        {
                            var fileName = Path.GetFileName(file);
                            var targetPath = Path.Combine(imageCacheFolder, fileName);

                            if (!File.Exists(targetPath))
                            {
                                File.Copy(file, targetPath);
                            }

                            // Si le fichier cible existe maintenant, on supprime l'ancien
                            if (File.Exists(targetPath))
                            {
                                File.Delete(file);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, $"Steam Store legacy image file migration failed: {file}");
                        }
                    }

                    TryDeleteDirectoryIfEmpty(legacyImageCache);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Steam Store legacy cache migration failed.");
            }
        }

        private void TryDeleteDirectoryIfEmpty(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                var hasFiles = Directory.GetFiles(path).Length > 0;
                var hasDirectories = Directory.GetDirectories(path).Length > 0;

                if (!hasFiles && !hasDirectories)
                {
                    Directory.Delete(path, false);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"Failed to delete empty legacy directory: {path}");
            }
        }

        private static string FormatOriginalPriceDisplay(string originalPrice, string finalPrice, string currency)
        {
            if (string.IsNullOrWhiteSpace(originalPrice))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(finalPrice))
            {
                return string.Empty;
            }

            if (string.Equals(originalPrice, finalPrice, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return FormatPriceDisplay(originalPrice, currency);
        }

        private void NormalizeCachedItemImagePaths(List<SteamStoreItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (var item in items)
            {
                if (item == null || item.AppId <= 0)
                {
                    continue;
                }

                try
                {
                    // Si le chemin actuel existe encore, on ne touche à rien
                    if (!string.IsNullOrWhiteSpace(item.CapsuleImageLocalPath) &&
                        File.Exists(item.CapsuleImageLocalPath))
                    {
                        continue;
                    }

                    // Sinon on essaie de retrouver l'image dans le nouveau dossier
                    var possibleFiles = Directory.GetFiles(imageCacheFolder, $"app_{item.AppId}.*", SearchOption.TopDirectoryOnly);
                    var existing = possibleFiles.FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
                    {
                        item.CapsuleImageLocalPath = existing;
                        continue;
                    }

                    // Fallback si rien de local n'est trouvé
                    item.CapsuleImageLocalPath = item.CapsuleImageUrl ?? string.Empty;
                }
                catch
                {
                    item.CapsuleImageLocalPath = item.CapsuleImageUrl ?? string.Empty;
                }
            }
        }

        private async Task<SteamStoreCacheEntry> LoadCacheAsync(string cacheKey, TimeSpan maxAge)
        {
            try
            {
                var cache = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
                if (cache == null)
                {
                    return null;
                }

                var age = DateTime.UtcNow - cache.LastUpdatedUtc;
                if (age > maxAge)
                {
                    return null;
                }

                return cache;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"STORE cache load failed: {cacheKey}");
                return null;
            }
        }

        private async Task<SteamStoreCacheEntry> LoadCacheAnyAgeAsync(string cacheKey)
        {
            try
            {
                var path = GetCachePath(cacheKey);
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = await Task.Run(() => File.ReadAllText(path)).ConfigureAwait(false);

                var cache = await Task.Run(() =>
                    JsonConvert.DeserializeObject<SteamStoreCacheEntry>(json)
                ).ConfigureAwait(false);

                if (cache == null)
                {
                    return null;
                }

                await Task.Run(() => NormalizeCachedItemImagePaths(cache.Items)).ConfigureAwait(false);

                return cache;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"STORE cache load(any age) failed: {cacheKey}");
                return null;
            }
        }

        private async Task SaveCacheAsync(string cacheKey, List<SteamStoreItem> items)
        {
            try
            {
                var entry = new SteamStoreCacheEntry
                {
                    LastUpdatedUtc = DateTime.UtcNow,
                    Items = items ?? new List<SteamStoreItem>()
                };

                var json = await Task.Run(() =>
                    JsonConvert.SerializeObject(entry, Formatting.Indented)
                ).ConfigureAwait(false);

                await Task.Run(() => File.WriteAllText(GetCachePath(cacheKey), json)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"STORE cache save failed: {cacheKey}");
            }
        }

        private async Task<string> GetOrCacheImageAsync(string imageUrl, int appId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    return string.Empty;
                }

                var ext = ".jpg";
                try
                {
                    var uri = new Uri(imageUrl);
                    var candidateExt = Path.GetExtension(uri.AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(candidateExt) && candidateExt.Length <= 5)
                    {
                        ext = candidateExt;
                    }
                }
                catch
                {
                }

                var safeName = $"app_{appId}{ext}";
                var localPath = Path.Combine(imageCacheFolder, safeName);

                if (File.Exists(localPath))
                {
                    TryTouchFile(localPath);
                    return localPath;
                }

                var bytes = await httpClient.GetByteArrayAsync(imageUrl);
                File.WriteAllBytes(localPath, bytes);

                CleanupImageCacheIfNeeded();

                return localPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CleanupImageCacheIfNeeded()
        {
            try
            {
                if (!Directory.Exists(imageCacheFolder))
                {
                    return;
                }

                var files = new DirectoryInfo(imageCacheFolder)
                    .GetFiles("*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                if (files.Count <= MaxCachedImages)
                {
                    return;
                }

                foreach (var file in files.Skip(MaxCachedImages))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryTouchFile(string path)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch
            {
            }
        }

        private SteamAppDetailsCacheEntry LoadAppDetailsCache(string cacheKey, TimeSpan maxAge, bool allowExpiredFallback = false)
        {
            try
            {
                var path = GetAppDetailsCachePath(cacheKey);
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = File.ReadAllText(path);
                var cache = JsonConvert.DeserializeObject<SteamAppDetailsCacheEntry>(json);
                if (cache == null)
                {
                    return null;
                }

                var age = DateTime.UtcNow - cache.LastUpdatedUtc;
                if (age > maxAge && !allowExpiredFallback)
                {
                    return null;
                }

                return cache;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"STORE appdetails cache load failed: {cacheKey}");
                return null;
            }
        }

        private void SaveAppDetailsCache(string cacheKey, SteamAppDetailsCacheEntry entry)
        {
            try
            {
                var json = JsonConvert.SerializeObject(entry, Formatting.Indented);
                File.WriteAllText(GetAppDetailsCachePath(cacheKey), json);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"STORE appdetails cache save failed: {cacheKey}");
            }
        }

        private string GetAppDetailsCachePath(string cacheKey)
        {
            return Path.Combine(detailsCacheFolder, $"{cacheKey}.json");
        }

        private string GetCachePath(string cacheKey)
        {
            return Path.Combine(storeCacheFolder, $"{cacheKey}.json");
        }
    }
}