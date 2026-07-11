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

        private static void DebugLog(string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug(message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private static void DebugLog(Exception exception, string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug(exception, message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private readonly IPlayniteAPI playniteApi;
        private readonly string steamStoreRootFolder;
        private readonly string storeCacheFolder;
        private readonly string detailsCacheFolder;
        private readonly string imageCacheFolder;
        private readonly object appDetailsCacheLock = new object();
        private static readonly HttpClient httpClient = new HttpClient();

        private const int MaxCachedImages = 2000;

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

        public async Task<List<SteamStoreItem>> GetUserWishlistFromCacheOnlyAsync(string profileKey, string language, string countryCode)
        {
            var cacheKey = BuildUserWishlistCacheKey(profileKey, language, countryCode);
            TryMigrateLegacyUserWishlistCache(profileKey, language, countryCode, cacheKey);
            var cached = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
            return cached?.Items ?? new List<SteamStoreItem>();
        }

        public async Task<bool> IsUserWishlistCacheMissingOrExpiredAsync(string profileKey, string language, string countryCode, TimeSpan maxAge)
        {
            var cacheKey = BuildUserWishlistCacheKey(profileKey, language, countryCode);
            TryMigrateLegacyUserWishlistCache(profileKey, language, countryCode, cacheKey);
            return await IsCacheMissingOrExpiredAsync(cacheKey, maxAge).ConfigureAwait(false);
        }

        public async Task SaveUserWishlistCacheAsync(string profileKey, string language, string countryCode, List<SteamStoreItem> items)
        {
            var cacheKey = BuildUserWishlistCacheKey(profileKey, language, countryCode);
            await SaveCacheAsync(cacheKey, items ?? new List<SteamStoreItem>()).ConfigureAwait(false);
        }

        public Task CacheStoreListImagesForSectionAsync(List<SteamStoreItem> items, string sourceName, string language = "english", string countryCode = "US")
        {
            return CacheStoreListImagesAsync(items, sourceName, language, countryCode);
        }

        private static string BuildUserWishlistCacheKey(string profileKey, string language, string countryCode)
        {
            var profile = SanitizeCachePart(string.IsNullOrWhiteSpace(profileKey) ? "no_account" : profileKey);
            var lang = SanitizeCachePart(string.IsNullOrWhiteSpace(language) ? "english" : language);
            var cc = SanitizeCachePart(string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode);

            // AnikiHelper sends profileKey as "steam_mywishlist_<steamid>".
            // Keep it as-is to avoid producing "steam_mywishlist_steam_mywishlist_<steamid>".
            if (profile.StartsWith("steam_mywishlist_", StringComparison.OrdinalIgnoreCase))
            {
                return $"{profile}_{lang}_{cc}".ToLowerInvariant();
            }

            return $"steam_mywishlist_{profile}_{lang}_{cc}".ToLowerInvariant();
        }

        private static string BuildLegacyUserWishlistCacheKey(string profileKey, string language, string countryCode)
        {
            var profile = SanitizeCachePart(string.IsNullOrWhiteSpace(profileKey) ? "unknown" : profileKey);
            var lang = SanitizeCachePart(string.IsNullOrWhiteSpace(language) ? "english" : language);
            var cc = SanitizeCachePart(string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode);
            return $"mywishlist_{profile}_{lang}_{cc}".ToLowerInvariant();
        }

        private void TryMigrateLegacyUserWishlistCache(string profileKey, string language, string countryCode, string newCacheKey)
        {
            try
            {
                var targetPath = GetCachePath(newCacheKey);
                if (File.Exists(targetPath))
                {
                    return;
                }

                var legacyKey = BuildLegacyUserWishlistCacheKey(profileKey, language, countryCode);
                var legacyPath = GetCachePath(legacyKey);
                if (!File.Exists(legacyPath))
                {
                    return;
                }

                File.Copy(legacyPath, targetPath, false);
                File.Delete(legacyPath);
                DebugLog($"[SteamStoreCache] Migrated wishlist cache '{legacyKey}' -> '{newCacheKey}'.");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Steam Store wishlist cache migration failed.");
            }
        }


        public async Task<bool> IsAnyStoreCacheMissingOrExpiredAsync(string language, string countryCode, TimeSpan maxAge)
        {
            var dealsKey = $"deals_{language}_{countryCode}".ToLowerInvariant();
            var topSellersKey = $"topsellers_{language}_{countryCode}".ToLowerInvariant();

            return await IsCacheMissingOrExpiredAsync(dealsKey, maxAge).ConfigureAwait(false)
                || await IsCacheMissingOrExpiredAsync(topSellersKey, maxAge).ConfigureAwait(false);
        }

        private async Task<bool> IsCacheMissingOrExpiredAsync(string cacheKey, TimeSpan maxAge)
        {
            var cache = await LoadCacheAnyAgeAsync(cacheKey).ConfigureAwait(false);
            if (cache == null)
            {
                return true;
            }

            var minimumCount = GetMinimumStoreCacheCount(cacheKey);
            var currentCount = cache.Items?.Count(x => x != null && x.AppId > 0) ?? 0;
            if (minimumCount > 0 && currentCount < minimumCount)
            {
                DebugLog($"[SteamStoreCache] Cache '{cacheKey}' has only {currentCount} items, refresh required.");
                return true;
            }

            return (DateTime.UtcNow - cache.LastUpdatedUtc) > maxAge;
        }

        private int GetMinimumStoreCacheCount(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return 0;
            }

            if (cacheKey.IndexOf("deals", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cacheKey.IndexOf("topsellers", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 18;
            }

            return 0;
        }

        private async Task<List<SteamStoreItem>> FetchDealsAsync(string language, string countryCode)
        {
            var featured = await FetchCategory(language, countryCode, "specials", "STORE Deals").ConfigureAwait(false);

            // Use the sale page sorted by top sellers. The plain specials page can surface too many
            // weak/low-quality discount items, and only 24 source items is too low after owned/library filtering.
            var search = await FetchSearchStoreRowsAsync(
                $"https://store.steampowered.com/search/?filter=topsellers&specials=1&os=win&count=100&cc={countryCode}&l={language}",
                "STORE Deals",
                3,
                80
            ).ConfigureAwait(false);

            return await MergeAndPrepareStoreRowsAsync(featured, search, "STORE Deals", 72, language, countryCode).ConfigureAwait(false);
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
            var featured = await FetchCategory(language, countryCode, "top_sellers", "STORE TopSellers").ConfigureAwait(false);
            var search = await FetchSearchStoreRowsAsync(
                $"https://store.steampowered.com/search/?filter=topsellers&os=win&count=100&cc={countryCode}&l={language}",
                "STORE TopSellers",
                3,
                80
            ).ConfigureAwait(false);

            return await MergeAndPrepareStoreRowsAsync(featured, search, "STORE TopSellers", 60, language, countryCode).ConfigureAwait(false);
        }

        private async Task<List<SteamStoreItem>> FetchSearchStoreRowsAsync(string baseUrl, string sourceName, int maxPages, int maxItems)
        {
            var results = new Dictionary<int, SteamStoreItem>();
            const int pageSize = 100;

            try
            {
                for (var page = 0; page < Math.Max(1, maxPages); page++)
                {
                    var start = page * pageSize;
                    var url = BuildPagedSteamSearchUrl(baseUrl, start, pageSize);
                    var html = await httpClient.GetStringAsync(url).ConfigureAwait(false);

                    var parsed = SteamStoreSearchHtmlParser.ParseStoreRows(html, sourceName)
                        .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                        .Where(x => !ShouldExcludeStoreItem(x.Name))
                        .ToList();

                    foreach (var item in parsed)
                    {
                        if (results.ContainsKey(item.AppId))
                        {
                            continue;
                        }

                        item.SteamRank = start + Math.Max(1, item.SteamRank);
                        item.Source = sourceName;
                        results[item.AppId] = item;
                    }

                    DebugLog($"{sourceName}: search page {page + 1}/{maxPages} parsed={parsed.Count} unique={results.Count}");

                    if (parsed.Count == 0 || results.Count >= maxItems)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"{sourceName}: search fallback failed");
            }

            return results.Values
                .OrderBy(x => x.SteamRank <= 0 ? int.MaxValue : x.SteamRank)
                .Take(maxItems)
                .ToList();
        }

        private async Task<List<SteamStoreItem>> MergeAndPrepareStoreRowsAsync(
            IEnumerable<SteamStoreItem> primary,
            IEnumerable<SteamStoreItem> fallback,
            string sourceName,
            int maxItems,
            string language = "english",
            string countryCode = "US")
        {
            var isDeals = string.Equals(sourceName, "STORE Deals", StringComparison.OrdinalIgnoreCase);

            var merged = (primary ?? Enumerable.Empty<SteamStoreItem>())
                .Concat(fallback ?? Enumerable.Empty<SteamStoreItem>())
                .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                .Where(x => !ShouldExcludeStoreItem(x.Name))
                .Where(x => !isDeals || LooksLikeRealDeal(x))
                .GroupBy(x => x.AppId)
                .Select(g => g
                    .OrderByDescending(x => GetStoreSectionCandidateScore(x, sourceName))
                    .ThenBy(x => x.SteamRank <= 0 ? int.MaxValue : x.SteamRank)
                    .First())
                .OrderByDescending(x => GetStoreSectionCandidateScore(x, sourceName))
                .ThenBy(x => x.SteamRank <= 0 ? int.MaxValue : x.SteamRank)
                .Take(maxItems > 0 ? maxItems : int.MaxValue)
                .ToList();

            await CacheStoreListImagesAsync(merged, sourceName, language, countryCode).ConfigureAwait(false);

            DebugLog($"{sourceName}: merged cache candidates={merged.Count}");
            return merged;
        }

        private static long GetStoreSectionCandidateScore(SteamStoreItem item, string sourceName)
        {
            if (item == null)
            {
                return long.MinValue;
            }

            long rankScore = item.SteamRank > 0 ? Math.Max(0, 100000 - item.SteamRank) : 0L;
            long score = rankScore;

            if (string.Equals(sourceName, "STORE Deals", StringComparison.OrdinalIgnoreCase))
            {
                // Deals must stay popular, but the discount must matter a lot more than it did before.
                score += Math.Max(0, item.DiscountPercent) * 3500L;

                if (!string.IsNullOrWhiteSpace(item.OriginalPriceDisplay))
                {
                    score += 8000;
                }

                if (!string.IsNullOrWhiteSpace(item.FinalPriceDisplay))
                {
                    score += 2000;
                }
            }

            if (item.RecommendationsTotal > 0)
            {
                score += Math.Min(25000, item.RecommendationsTotal / 10);
            }

            return score;
        }

        private static bool LooksLikeRealDeal(SteamStoreItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.DiscountPercent > 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(item.DiscountDisplay) && item.DiscountDisplay.Trim().StartsWith("-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(item.OriginalPriceDisplay) && !string.IsNullOrWhiteSpace(item.FinalPriceDisplay);
        }

        private async Task CacheStoreListImagesAsync(List<SteamStoreItem> items, string sourceName, string language = "english", string countryCode = "US")
        {
            var list = items?.Where(x => x != null && x.AppId > 0).Take(72).ToList() ?? new List<SteamStoreItem>();
            var diagCount = 0;

            foreach (var item in list)
            {
                var originalSearchUrl = item.SearchImageUrl;
                var originalCapsuleUrl = item.CapsuleImageUrl;
                var originalHeaderUrl = item.HeaderImageUrl;
                var hqWide = BuildSteamAppImageUrl(item.AppId, "capsule_616x353.jpg");
                var hqHeader = BuildSteamAppImageUrl(item.AppId, "header.jpg");

                // Keep HQ deterministic URLs for normal apps, but never lose the original
                // HTML/appdetails URLs. Some Steam rows are bundles/packages/preorders where
                // deterministic /apps/{appid}/ image URLs are empty/404 while the row/details
                // image is valid.
                EnsureHighQualityStoreImageUrls(item);

                var wideId = item.AppId + 5000000;
                var headerId = item.AppId + 6000000;
                var detailsHeaderId = item.AppId + 1000000;

                // Always go through GetOrCacheFirstImageAsync first. It has URL markers and can
                // replace an old low-quality fallback with the real HQ Steam image when it becomes
                // available. If the HQ URL fails, it safely keeps the existing local file.
                var expectedWide = await GetOrCacheFirstImageAsync(
                    wideId,
                    hqWide,
                    item.CapsuleImageUrl,
                    originalCapsuleUrl,
                    originalSearchUrl,
                    item.HeaderImageUrl,
                    originalHeaderUrl,
                    hqHeader
                ).ConfigureAwait(false);

                if (!IsGoodWideLocalImage(expectedWide))
                {
                    expectedWide = FirstGoodWideLocalImage(
                        FindCachedImageForId(wideId),
                        item.CapsuleImageLocalPath
                    );
                }

                item.CapsuleImageLocalPath = IsGoodWideLocalImage(expectedWide) ? expectedWide : string.Empty;

                var expectedHeader = await GetOrCacheFirstImageAsync(
                    headerId,
                    hqHeader,
                    item.HeaderImageUrl,
                    originalHeaderUrl,
                    originalSearchUrl,
                    item.CapsuleImageUrl,
                    originalCapsuleUrl,
                    hqWide
                ).ConfigureAwait(false);

                if (!IsGoodHeaderOrBannerLocalImage(expectedHeader))
                {
                    expectedHeader = FirstGoodHeaderLocalImage(
                        FindCachedImageForId(headerId),
                        FindCachedImageForId(detailsHeaderId),
                        item.HeaderImageLocalPath
                    );
                }

                item.HeaderImageLocalPath = IsGoodHeaderOrBannerLocalImage(expectedHeader) ? expectedHeader : string.Empty;

                // Last-resort: if the list card is still empty, use appdetails header only.
                // This is intentionally limited to failed images, so the Store does not download
                // backgrounds/screenshots for every card.
                if (string.IsNullOrWhiteSpace(item.StoreCardImage) || ShouldTryUpgradeStoreCardImage(item, sourceName))
                {
                    await TryRepairStoreCardImageFromDetailsAsync(item, language, countryCode, wideId, headerId, sourceName).ConfigureAwait(false);
                }

                if (diagCount < 24)
                {
                    diagCount++;
                    DebugLog($"{sourceName}: ImageDiag #{diagCount} | AppId={item.AppId} | Name={item.Name} | card={(string.IsNullOrWhiteSpace(item.StoreCardImage) ? "EMPTY" : "OK")} | wide={DescribeLocalImage(item.CapsuleImageLocalPath)} | header={DescribeLocalImage(item.HeaderImageLocalPath)} | searchUrl={ShortImageUrl(item.SearchImageUrl)} | wideUrl={ShortImageUrl(item.CapsuleImageUrl)} | headerUrl={ShortImageUrl(item.HeaderImageUrl)}");
                }
            }
        }

        private async Task TryRepairStoreCardImageFromDetailsAsync(SteamStoreItem item, string language, string countryCode, int wideId, int headerId, string sourceName)
        {
            if (item == null || item.AppId <= 0)
            {
                return;
            }

            try
            {
                await EnrichStoreItemDetailsAsync(
                    item,
                    string.IsNullOrWhiteSpace(language) ? "english" : language,
                    string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode,
                    downloadMedia: false
                ).ConfigureAwait(false);

                var preferHeader = ShouldPreferHeaderCardImage(item, sourceName);

                var repairedWide = await GetOrCacheFirstImageAsync(
                    wideId,
                    item.CapsuleImageUrl,
                    BuildSteamAppImageUrl(item.AppId, "capsule_616x353.jpg"),
                    item.SearchImageUrl
                ).ConfigureAwait(false);

                if (IsGoodWideLocalImage(repairedWide))
                {
                    item.CapsuleImageLocalPath = repairedWide;
                }

                var repairedHeader = await GetOrCacheFirstImageAsync(
                    headerId,
                    item.HeaderImageUrl,
                    BuildSteamAppImageUrl(item.AppId, "header.jpg"),
                    item.SearchImageUrl,
                    item.CapsuleImageUrl
                ).ConfigureAwait(false);

                if (IsGoodHeaderOrBannerLocalImage(repairedHeader))
                {
                    item.HeaderImageLocalPath = repairedHeader;
                }

                if (!string.IsNullOrWhiteSpace(item.StoreCardImage))
                {
                    DebugLog($"STORE ImageRepair OK | AppId={item.AppId} | Name={item.Name} | prefer={(preferHeader ? "header" : "wide")} | card={(string.IsNullOrWhiteSpace(item.StoreCardImage) ? "EMPTY" : "OK")} | wide={DescribeLocalImage(item.CapsuleImageLocalPath)} | header={DescribeLocalImage(item.HeaderImageLocalPath)}");
                }
                else
                {
                    logger.Warn($"STORE ImageRepair FAILED | AppId={item.AppId} | Name={item.Name} | wideUrl={ShortImageUrl(item.CapsuleImageUrl)} | headerUrl={ShortImageUrl(item.HeaderImageUrl)} | searchUrl={ShortImageUrl(item.SearchImageUrl)}");
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"STORE ImageRepair ERROR | AppId={item.AppId} | Name={item.Name}");
            }
        }

        private static bool ShouldTryUpgradeStoreCardImage(SteamStoreItem item, string sourceName)
        {
            if (item == null)
            {
                return false;
            }

            // Store cards are header-first now. If we only have a wide image or a weak/empty
            // card, try appdetails once to recover the real Steam header.
            return !IsGoodHeaderOrBannerLocalImage(item.HeaderImageLocalPath) ||
                   string.IsNullOrWhiteSpace(item.StoreCardImage);
        }

        private static bool ShouldPreferHeaderCardImage(SteamStoreItem item, string sourceName)
        {
            if (item != null && item.ComingSoon)
            {
                return true;
            }

            var source = FirstNonEmpty(item?.Source, sourceName);
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            return source.IndexOf("Coming Soon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   source.IndexOf("Upcoming", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   source.IndexOf("NewReleases", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   source.IndexOf("New Releases", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   source.IndexOf("Wishlisted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   source.IndexOf("Wishlist", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<string> GetOrCacheFirstImageAsync(int cacheImageId, params string[] imageUrls)
        {
            if (imageUrls == null)
            {
                return string.Empty;
            }

            foreach (var imageUrl in imageUrls
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => !IsBadSteamSharedImage(x))
                .Where(x => !IsLowQualitySteamSearchImage(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var localPath = await GetOrCacheImageAsync(imageUrl, cacheImageId).ConfigureAwait(false);
                if (IsUsableLocalImageFile(localPath))
                {
                    return localPath;
                }
            }

            return string.Empty;
        }

        private static string FirstGoodWideLocalImage(params string[] paths)
        {
            if (paths == null)
            {
                return string.Empty;
            }

            foreach (var path in paths)
            {
                if (IsGoodWideLocalImage(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static string FirstGoodHeaderLocalImage(params string[] paths)
        {
            if (paths == null)
            {
                return string.Empty;
            }

            foreach (var path in paths)
            {
                if (IsGoodHeaderOrBannerLocalImage(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static string DescribeLocalImage(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return "empty";
                }

                if (IsUrl(path))
                {
                    return "url";
                }

                if (!File.Exists(path))
                {
                    return "missing:" + Path.GetFileName(path);
                }

                var info = new FileInfo(path);
                if (TryGetImageDimensions(path, out var width, out var height))
                {
                    return $"ok:{Path.GetFileName(path)}:{width}x{height}:{info.Length}";
                }

                return $"ok:{Path.GetFileName(path)}:unknown:{info.Length}";
            }
            catch
            {
                return "error";
            }
        }

        private static string ShortImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "empty";
            }

            try
            {
                var uri = new Uri(value);
                return Path.GetFileName(uri.AbsolutePath);
            }
            catch
            {
                return value.Length > 48 ? value.Substring(0, 48) : value;
            }
        }

        private static void EnsureHighQualityStoreImageUrls(SteamStoreItem item)
        {
            if (item == null || item.AppId <= 0)
            {
                return;
            }

            // Do not blindly overwrite existing URLs anymore. The parser/details cache may hold
            // the only valid Steam image for packages/bundles/preorders. Deterministic URLs are
            // still used as primary download candidates by CacheStoreListImagesAsync.
            if (string.IsNullOrWhiteSpace(item.CapsuleImageUrl) || IsLowQualitySteamSearchImage(item.CapsuleImageUrl))
            {
                item.CapsuleImageUrl = BuildSteamAppImageUrl(item.AppId, "capsule_616x353.jpg");
            }

            if (string.IsNullOrWhiteSpace(item.HeaderImageUrl) || IsLowQualitySteamSearchImage(item.HeaderImageUrl))
            {
                item.HeaderImageUrl = BuildSteamAppImageUrl(item.AppId, "header.jpg");
            }
        }

        private static bool IsLowQualitySteamSearchImage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return value.IndexOf("capsule_sm_120", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("capsule_184x69", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("capsule_231x87", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("search_capsule", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBadSteamSharedImage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return value.IndexOf("/public/shared/images/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("footerLogo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("footer_logo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("valve_new", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUrl(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsGoodHeaderOrBannerLocalImage(string path)
        {
            if (!IsUsableLocalImageFile(path))
            {
                return false;
            }

            if (!TryGetImageDimensions(path, out var width, out var height))
            {
                return false;
            }

            return width >= 400 && height >= 180;
        }

        private static bool IsGoodWideLocalImage(string path)
        {
            if (!IsUsableLocalImageFile(path))
            {
                return false;
            }

            if (!TryGetImageDimensions(path, out var width, out var height))
            {
                return false;
            }

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

                    if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                    {
                        width = (header[16] << 24) + (header[17] << 16) + (header[18] << 8) + header[19];
                        height = (header[20] << 24) + (header[21] << 16) + (header[22] << 8) + header[23];
                        return width > 0 && height > 0;
                    }

                    if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46)
                    {
                        width = header[6] + (header[7] << 8);
                        height = header[8] + (header[9] << 8);
                        return width > 0 && height > 0;
                    }

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

                        if ((marker >= 0xC0 && marker <= 0xC3) ||
                            (marker >= 0xC5 && marker <= 0xC7) ||
                            (marker >= 0xC9 && marker <= 0xCB) ||
                            (marker >= 0xCD && marker <= 0xCF))
                        {
                            stream.ReadByte();
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

        private static bool IsUsableLocalImageFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || IsUrl(path) || !File.Exists(path))
                {
                    return false;
                }

                var info = new FileInfo(path);
                if (info.Length < 1024)
                {
                    return false;
                }

                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length < 8)
                    {
                        return false;
                    }

                    var b0 = stream.ReadByte();
                    var b1 = stream.ReadByte();
                    var b2 = stream.ReadByte();
                    var b3 = stream.ReadByte();

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

        private static string BuildPagedSteamSearchUrl(string baseUrl, int start, int count)
        {
            var result = string.IsNullOrWhiteSpace(baseUrl) ? "https://store.steampowered.com/search/?os=win" : baseUrl;

            result = System.Text.RegularExpressions.Regex.Replace(result, @"([?&])start=\d+", "$1start=" + start, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!System.Text.RegularExpressions.Regex.IsMatch(result, @"[?&]start=", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                result += (result.Contains("?") ? "&" : "?") + "start=" + start;
            }

            result = System.Text.RegularExpressions.Regex.Replace(result, @"([?&])count=\d+", "$1count=" + count, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!System.Text.RegularExpressions.Regex.IsMatch(result, @"[?&]count=", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                result += (result.Contains("?") ? "&" : "?") + "count=" + count;
            }

            return result;
        }

        private static string BuildSteamAppImageUrl(int appId, string fileName)
        {
            if (appId <= 0 || string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/{fileName}";
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
                "adult only",
                "nsfw",
                "porn",
                "pornography",
                "sex",
                "sexual",
                "erotic",
                "erotica",
                "nudity",
                "nude",
                "naked",
                "lewd",
                "ecchi",
                "busty",
                "boob",
                "boobs",
                "breast",
                "breasts",
                "milf",
                "waifu",
                "tentacle",
                "succubus",
                "18+"
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

                        if (ShouldExcludeStoreItem(name))
                        {
                            continue;
                        }

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

                        if (string.IsNullOrWhiteSpace(capsuleUrl) || IsLowQualitySteamSearchImage(capsuleUrl))
                        {
                            capsuleUrl = BuildSteamAppImageUrl(appId, "capsule_616x353.jpg");
                        }

                        if (string.IsNullOrWhiteSpace(headerImage) || IsLowQualitySteamSearchImage(headerImage))
                        {
                            headerImage = BuildSteamAppImageUrl(appId, "header.jpg");
                        }

                        var localHeaderPath = await GetOrCacheImageAsync(headerImage, appId + 6000000);
                        localHeaderPath = IsGoodHeaderOrBannerLocalImage(localHeaderPath) ? localHeaderPath : string.Empty;

                        // Capsule is only a last-resort fallback for old XAML/bindings. Do not block
                        // card display on capsule cache because the Store now uses headers first.
                        var localCapsulePath = await GetOrCacheImageAsync(capsuleUrl, appId + 5000000);
                        localCapsulePath = IsGoodWideLocalImage(localCapsulePath) ? localCapsulePath : string.Empty;

                        results.Add(new SteamStoreItem
                        {
                            AppId = appId,
                            SteamRank = results.Count + 1,
                            Name = name,
                            Source = logPrefix ?? string.Empty,
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
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("short_description")]
            public string ShortDescription { get; set; }

            [JsonProperty("release_date")]
            public SteamReleaseDateData ReleaseDate { get; set; }

            [JsonProperty("genres")]
            public List<SteamNamedItem> Genres { get; set; }

            [JsonProperty("header_image")]
            public string HeaderImage { get; set; }

            [JsonProperty("capsule_image")]
            public string CapsuleImage { get; set; }

            [JsonProperty("capsule_imagev5")]
            public string CapsuleImageV5 { get; set; }

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

        private static string GetScreenshotImageUrl(List<SteamScreenshotData> screenshots, int index)
        {
            if (screenshots == null || screenshots.Count <= index || screenshots[index] == null)
            {
                return string.Empty;
            }

            var screenshot = screenshots[index];

            if (!string.IsNullOrWhiteSpace(screenshot.PathFull))
            {
                return screenshot.PathFull;
            }

            return screenshot.PathThumbnail ?? string.Empty;
        }

        public async Task EnrichStoreItemDetailsAsync(SteamStoreItem item, string language, string countryCode, bool downloadMedia = true)
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
                var needsNameRefresh = LooksLikePlaceholderSteamAppName(item.Name) && string.IsNullOrWhiteSpace(freshCache.Name);
                ApplyDetailsCacheToItem(item, freshCache);

                // Store refreshes can save light appdetails cache entries with URLs only.
                // When the details view opens, download the heavy media from those cached URLs
                // instead of returning early with empty local background/screenshots.
                if (!downloadMedia && !needsNameRefresh)
                {
                    return;
                }

                if (!needsNameRefresh)
                {
                    var mediaDownloadedFromCache = await EnsureStoreItemMediaDownloadedAsync(item).ConfigureAwait(false);
                    if (mediaDownloadedFromCache)
                    {
                        SaveAppDetailsCache(cacheKey, CreateAppDetailsCacheEntry(item));
                        DebugLog($"STORE details media downloaded from cache URLs for AppId={item.AppId}");
                    }

                    if (HasAnyLocalDetailsMedia(item))
                    {
                        return;
                    }
                }

                DebugLog($"STORE details cache incomplete for AppId={item.AppId}; fetching appdetails again.");
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

                    if (!string.IsNullOrWhiteSpace(envelope.Data.Name))
                    {
                        item.Name = envelope.Data.Name;
                    }

                    item.AppType = envelope.Data.Type ?? item.AppType ?? string.Empty;
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
                        var shot1 = GetScreenshotImageUrl(envelope.Data.Screenshots, 0);
                        var shot2 = GetScreenshotImageUrl(envelope.Data.Screenshots, 1);
                        var shot3 = GetScreenshotImageUrl(envelope.Data.Screenshots, 2);
                        var shot4 = GetScreenshotImageUrl(envelope.Data.Screenshots, 3);
                        var shot5 = GetScreenshotImageUrl(envelope.Data.Screenshots, 4);

                        item.Screenshot1Url = shot1 ?? string.Empty;
                        item.Screenshot2Url = shot2 ?? string.Empty;
                        item.Screenshot3Url = shot3 ?? string.Empty;
                        item.Screenshot4Url = shot4 ?? string.Empty;
                        item.Screenshot5Url = shot5 ?? string.Empty;

                        if (downloadMedia)
                        {
                            item.Screenshot1LocalPath = await GetOrCacheImageAsync(item.Screenshot1Url, item.AppId + 4000000);
                            item.Screenshot2LocalPath = await GetOrCacheImageAsync(item.Screenshot2Url, item.AppId + 4000001);
                            item.Screenshot3LocalPath = await GetOrCacheImageAsync(item.Screenshot3Url, item.AppId + 4000002);
                            item.Screenshot4LocalPath = await GetOrCacheImageAsync(item.Screenshot4Url, item.AppId + 4000003);
                            item.Screenshot5LocalPath = await GetOrCacheImageAsync(item.Screenshot5Url, item.AppId + 4000004);
                        }
                    }

                    item.BackgroundImageUrl = backgroundUrl ?? string.Empty;

                    if (downloadMedia && !string.IsNullOrWhiteSpace(backgroundUrl))
                    {
                        item.BackgroundImageLocalPath = await GetOrCacheImageAsync(backgroundUrl, item.AppId + 3000000);
                    }

                    if (!string.IsNullOrWhiteSpace(envelope.Data.HeaderImage))
                    {
                        item.HeaderImageUrl = envelope.Data.HeaderImage;
                        if (downloadMedia)
                        {
                            item.HeaderImageLocalPath = await GetOrCacheImageAsync(envelope.Data.HeaderImage, item.AppId + 1000000);
                        }
                    }

                    var detailsCapsuleImage = FirstNonEmpty(envelope.Data.CapsuleImage, envelope.Data.CapsuleImageV5);
                    if (!string.IsNullOrWhiteSpace(detailsCapsuleImage) &&
                        !IsBadSteamSharedImage(detailsCapsuleImage) &&
                        !IsLowQualitySteamSearchImage(detailsCapsuleImage))
                    {
                        item.CapsuleImageUrl = detailsCapsuleImage;
                        if (downloadMedia)
                        {
                            item.CapsuleImageLocalPath = await GetOrCacheImageAsync(detailsCapsuleImage, item.AppId + 5000000);
                        }
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

                    SaveAppDetailsCache(cacheKey, CreateAppDetailsCacheEntry(item));
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

            item.Name = FirstNonEmpty(cache.Name, item.Name);
            item.AppType = FirstNonEmpty(cache.AppType, item.AppType);
            item.ShortDescription = FirstNonEmpty(cache.ShortDescription, item.ShortDescription);
            item.ReleaseDateDisplay = FirstNonEmpty(cache.ReleaseDateDisplay, item.ReleaseDateDisplay);
            item.ComingSoon = cache.ComingSoon;
            item.IsPreorder = cache.IsPreorder;
            item.Developers = cache.Developers ?? item.Developers ?? new List<string>();
            item.Publishers = cache.Publishers ?? item.Publishers ?? new List<string>();
            item.ControllerSupport = FirstNonEmpty(cache.ControllerSupport, item.ControllerSupport);
            item.SupportedLanguages = FirstNonEmpty(cache.SupportedLanguages, item.SupportedLanguages);
            item.Genres = cache.Genres ?? item.Genres ?? new List<string>();
            item.Categories = cache.Categories ?? item.Categories ?? new List<string>();

            item.BackgroundImageUrl = FirstNonEmpty(cache.BackgroundImageUrl, item.BackgroundImageUrl);

            // Do NOT preserve a list-card fallback here. Upcoming/New/Recommended cards may store
            // Header/Capsule as BackgroundImageLocalPath so the card has a fast image. If we kept
            // that value here, EnsureStoreItemMediaDownloadedAsync would think the real background
            // already exists and would skip downloading it for the details view.
            item.BackgroundImageLocalPath = IsExistingLocalFile(cache.BackgroundImageLocalPath)
                ? cache.BackgroundImageLocalPath
                : string.Empty;

            item.FinalPriceDisplay = FirstNonEmpty(cache.FinalPriceDisplay, item.FinalPriceDisplay);
            item.OriginalPriceDisplay = FirstNonEmpty(cache.OriginalPriceDisplay, item.OriginalPriceDisplay);
            item.DiscountDisplay = FirstNonEmpty(cache.DiscountDisplay, item.DiscountDisplay);
            item.HeaderImageUrl = FirstNonEmpty(cache.HeaderImageUrl, item.HeaderImageUrl);
            item.HeaderImageLocalPath = IsUsableLocalImageFile(cache.HeaderImageLocalPath)
                ? cache.HeaderImageLocalPath
                : (IsUsableLocalImageFile(item.HeaderImageLocalPath) ? item.HeaderImageLocalPath : string.Empty);

            item.MetacriticScore = cache.MetacriticScore;
            item.RecommendationsTotal = cache.RecommendationsTotal;
            item.AchievementsTotal = cache.AchievementsTotal;
            item.DlcCount = cache.DlcCount;

            item.Screenshot1Url = FirstNonEmpty(cache.Screenshot1Url, item.Screenshot1Url);
            item.Screenshot1LocalPath = FirstExistingLocalPath(cache.Screenshot1LocalPath, item.Screenshot1LocalPath);
            item.Screenshot2Url = FirstNonEmpty(cache.Screenshot2Url, item.Screenshot2Url);
            item.Screenshot2LocalPath = FirstExistingLocalPath(cache.Screenshot2LocalPath, item.Screenshot2LocalPath);
            item.Screenshot3Url = FirstNonEmpty(cache.Screenshot3Url, item.Screenshot3Url);
            item.Screenshot3LocalPath = FirstExistingLocalPath(cache.Screenshot3LocalPath, item.Screenshot3LocalPath);
            item.Screenshot4Url = FirstNonEmpty(cache.Screenshot4Url, item.Screenshot4Url);
            item.Screenshot4LocalPath = FirstExistingLocalPath(cache.Screenshot4LocalPath, item.Screenshot4LocalPath);
            item.Screenshot5Url = FirstNonEmpty(cache.Screenshot5Url, item.Screenshot5Url);
            item.Screenshot5LocalPath = FirstExistingLocalPath(cache.Screenshot5LocalPath, item.Screenshot5LocalPath);
        }

        private static bool LooksLikePlaceholderSteamAppName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Trim().StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string FirstExistingLocalPath(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (IsExistingLocalFile(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private async Task<bool> EnsureStoreItemMediaDownloadedAsync(SteamStoreItem item)
        {
            if (item == null || item.AppId <= 0)
            {
                return false;
            }

            // Details view media is downloaded on demand, but it should not be sequential.
            // Background + screenshots in sequence can make the details window feel frozen.
            var tasks = new List<Task<bool>>
            {
                EnsureLocalImageAsync(
                    () => item.HeaderImageLocalPath,
                    value => item.HeaderImageLocalPath = value,
                    item.HeaderImageUrl,
                    item.AppId + 1000000
                ),

                EnsureLocalImageAsync(
                    () => item.BackgroundImageLocalPath,
                    value => item.BackgroundImageLocalPath = value,
                    item.BackgroundImageUrl,
                    item.AppId + 3000000
                ),

                EnsureLocalImageAsync(
                    () => item.Screenshot1LocalPath,
                    value => item.Screenshot1LocalPath = value,
                    item.Screenshot1Url,
                    item.AppId + 4000000
                ),

                EnsureLocalImageAsync(
                    () => item.Screenshot2LocalPath,
                    value => item.Screenshot2LocalPath = value,
                    item.Screenshot2Url,
                    item.AppId + 4000001
                ),

                EnsureLocalImageAsync(
                    () => item.Screenshot3LocalPath,
                    value => item.Screenshot3LocalPath = value,
                    item.Screenshot3Url,
                    item.AppId + 4000002
                ),

                EnsureLocalImageAsync(
                    () => item.Screenshot4LocalPath,
                    value => item.Screenshot4LocalPath = value,
                    item.Screenshot4Url,
                    item.AppId + 4000003
                ),

                EnsureLocalImageAsync(
                    () => item.Screenshot5LocalPath,
                    value => item.Screenshot5LocalPath = value,
                    item.Screenshot5Url,
                    item.AppId + 4000004
                )
            };

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.Any(x => x);
        }

        private async Task<bool> EnsureLocalImageAsync(Func<string> getLocalPath, Action<string> setLocalPath, string imageUrl, int cacheImageId)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || getLocalPath == null || setLocalPath == null)
            {
                return false;
            }

            var currentLocalPath = getLocalPath();
            if (!string.IsNullOrWhiteSpace(currentLocalPath) && File.Exists(currentLocalPath))
            {
                return false;
            }

            var localPath = await GetOrCacheImageAsync(imageUrl, cacheImageId).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(localPath))
            {
                return false;
            }

            setLocalPath(localPath);
            return true;
        }

        private static bool HasAnyLocalDetailsMedia(SteamStoreItem item)
        {
            if (item == null)
            {
                return false;
            }

            return IsExistingLocalFile(item.BackgroundImageLocalPath) ||
                   IsExistingLocalFile(item.Screenshot1LocalPath) ||
                   IsExistingLocalFile(item.Screenshot2LocalPath) ||
                   IsExistingLocalFile(item.Screenshot3LocalPath) ||
                   IsExistingLocalFile(item.Screenshot4LocalPath) ||
                   IsExistingLocalFile(item.Screenshot5LocalPath);
        }

        private static bool IsExistingLocalFile(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private static SteamAppDetailsCacheEntry CreateAppDetailsCacheEntry(SteamStoreItem item)
        {
            return new SteamAppDetailsCacheEntry
            {
                LastUpdatedUtc = DateTime.UtcNow,
                AppId = item.AppId,
                Name = item.Name,
                AppType = item.AppType,
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
                HeaderImageUrl = item.HeaderImageUrl,
                HeaderImageLocalPath = item.HeaderImageLocalPath,

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
                Screenshot3LocalPath = item.Screenshot3LocalPath,
                Screenshot4Url = item.Screenshot4Url,
                Screenshot4LocalPath = item.Screenshot4LocalPath,
                Screenshot5Url = item.Screenshot5Url,
                Screenshot5LocalPath = item.Screenshot5LocalPath
            };
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
                    EnsureHighQualityStoreImageUrls(item);

                    // Normalize list images back to quality-checked cache ids.
                    // 231x87 Steam search thumbnails must not survive cache reloads.
                    var wide = FindCachedImageForId(item.AppId + 5000000);
                    var header = FindCachedImageForId(item.AppId + 6000000);
                    if (!IsGoodHeaderOrBannerLocalImage(header))
                    {
                        // Details view used this legacy/details header cache id.
                        header = FindCachedImageForId(item.AppId + 1000000);
                    }

                    item.CapsuleImageLocalPath = IsGoodWideLocalImage(wide) ? wide : string.Empty;
                    item.HeaderImageLocalPath = IsGoodHeaderOrBannerLocalImage(header) ? header : string.Empty;
                }
                catch
                {
                    item.HeaderImageLocalPath = string.Empty;
                    item.CapsuleImageLocalPath = string.Empty;
                }
            }
        }

        private string FindCachedImageForId(int imageId)
        {
            try
            {
                if (imageId <= 0 || !Directory.Exists(imageCacheFolder))
                {
                    return string.Empty;
                }

                var existing = Directory.GetFiles(imageCacheFolder, $"app_{imageId}.*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(IsUsableLocalImageFile);

                return existing ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool NeedsStoreCardImageRefresh(string cacheKey, List<SteamStoreItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return false;
            }

            var isStoreSection = cacheKey.IndexOf("deals", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 cacheKey.IndexOf("topsellers", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isStoreSection)
            {
                return false;
            }

            var visible = items.Where(x => x != null && x.AppId > 0).Take(24).ToList();
            if (visible.Count == 0)
            {
                return false;
            }

            var goodCards = visible.Count(x => !string.IsNullOrWhiteSpace(x.StoreCardImage));
            return goodCards < Math.Min(12, visible.Count);
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

                if (NeedsStoreCardImageRefresh(cacheKey, cache.Items))
                {
                    DebugLog($"[SteamStoreCache] Cache '{cacheKey}' contains low-quality or missing Store card images; refresh required.");
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

        private static string SanitizeCachePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var safe = new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            while (safe.Contains("__"))
            {
                safe = safe.Replace("__", "_");
            }

            return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe.Trim('_');
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

        private static bool LooksLikeImageBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8)
            {
                return false;
            }

            return (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
                   (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) ||
                   (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) ||
                   (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46);
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
                var sourceMarkerPath = localPath + ".source.txt";
                var oldUrlMarkerPath = localPath + ".url";

                var existingIsUsable = IsUsableLocalImageFile(localPath);
                if (existingIsUsable && File.Exists(sourceMarkerPath))
                {
                    try
                    {
                        var cachedUrl = File.ReadAllText(sourceMarkerPath);
                        if (string.Equals(cachedUrl, imageUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            TryTouchFile(localPath);
                            return localPath;
                        }
                    }
                    catch
                    {
                    }
                }

                var bytes = await httpClient.GetByteArrayAsync(imageUrl);
                if (bytes == null || bytes.Length < 1024 || !LooksLikeImageBytes(bytes))
                {
                    return existingIsUsable ? localPath : string.Empty;
                }

                var tmpPath = localPath + ".tmp";
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

                File.WriteAllBytes(tmpPath, bytes);

                if (!IsUsableLocalImageFile(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                    return existingIsUsable ? localPath : string.Empty;
                }

                try
                {
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }

                    if (File.Exists(sourceMarkerPath))
                    {
                        File.Delete(sourceMarkerPath);
                    }

                    if (File.Exists(oldUrlMarkerPath))
                    {
                        File.Delete(oldUrlMarkerPath);
                    }
                }
                catch
                {
                }

                File.Move(tmpPath, localPath);
                File.WriteAllText(sourceMarkerPath, imageUrl);

                CleanupImageCacheIfNeeded();

                return localPath;
            }
            catch
            {
                try
                {
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

                    var fallbackPath = Path.Combine(imageCacheFolder, $"app_{appId}{ext}");
                    return IsUsableLocalImageFile(fallbackPath) ? fallbackPath : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
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
                    .Where(f =>
                        !string.Equals(f.Extension, ".url", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(f.Extension, ".txt", StringComparison.OrdinalIgnoreCase))
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
                        var oldUrlMarkerPath = file.FullName + ".url";
                        var sourceMarkerPath = file.FullName + ".source.txt";

                        file.Delete();

                        if (File.Exists(oldUrlMarkerPath))
                        {
                            File.Delete(oldUrlMarkerPath);
                        }

                        if (File.Exists(sourceMarkerPath))
                        {
                            File.Delete(sourceMarkerPath);
                        }
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

                lock (appDetailsCacheLock)
                {
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
                var path = GetAppDetailsCachePath(cacheKey);

                lock (appDetailsCacheLock)
                {
                    File.WriteAllText(path, json);
                }
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