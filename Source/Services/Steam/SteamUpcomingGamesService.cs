using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AnikiHelper.Services
{
    public class SteamUpcomingGamesService
    {
        private readonly ILogger logger;

        private void DebugLog(string message)
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

        private void DebugLog(Exception exception, string message)
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
        private readonly SteamStoreService steamStoreService;
        private readonly string steamStoreCacheFolder;
        private readonly string imageCacheFolder;

        public SteamUpcomingGamesService(ILogger logger, string pluginUserDataPath, SteamStoreService steamStoreService)
        {
            this.logger = logger;
            this.steamStoreService = steamStoreService;

            steamStoreCacheFolder = Path.Combine(pluginUserDataPath, "SteamStore", "StoreCache");
            imageCacheFolder = Path.Combine(pluginUserDataPath, "SteamStore", "ImageCache");

            Directory.CreateDirectory(steamStoreCacheFolder);
            Directory.CreateDirectory(imageCacheFolder);
        }

        private string GetSearchCachePath(string filterKey, string language, string region)
        {
            var safeFilterKey = SanitizeCachePart(filterKey);
            var safeLanguage = SanitizeCachePart(language);
            var safeRegion = SanitizeCachePart(region);

            return Path.Combine(
                steamStoreCacheFolder,
                $"{safeFilterKey}_{safeLanguage}_{safeRegion}.json"
            );
        }

        private static string SanitizeCachePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "default";
            }

            var result = value.Trim().ToLowerInvariant();

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalidChar, '_');
            }

            result = result.Replace(" ", "_");

            return result;
        }

        public async Task<List<SteamUpcomingGameItem>> RefreshAsync(string language, string region)
        {
            DebugLog("[Upcoming] RefreshAsync START");

            var targetCachePath = GetSearchCachePath("popularcomingsoon", language, region);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);

                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36"
                );

                client.DefaultRequestHeaders.Accept.ParseAdd(
                    "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
                );

                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                const int pageSize = 100;
                const int maxWishlistedPages = 4;
                const int maxComingSoonPages = 8;
                const int targetCount = 24;

                var parsedByAppId = new Dictionary<int, SteamUpcomingGameItem>();

                // Use Steam "Most Wishlisted" as the main quality signal.
                // It contains the big unreleased games even when Steam does not expose a price yet.
                await ScanUpcomingSearchPagesAsync(
                    client,
                    "popularwishlist",
                    "Most Wishlisted primary",
                    pageSize,
                    maxWishlistedPages,
                    parsedByAppId,
                    language,
                    region,
                    "Steam Most Wishlisted",
                    0
                );

                // Fill with Popular Coming Soon after the wishlist results.
                await ScanUpcomingSearchPagesAsync(
                    client,
                    "popularcomingsoon",
                    "Popular Coming Soon fallback",
                    pageSize,
                    maxComingSoonPages,
                    parsedByAppId,
                    language,
                    region,
                    "Steam Popular Coming Soon",
                    100000
                );

                var allParsedGames = parsedByAppId.Values
                    .Where(IsGoodUpcomingCandidate)
                    .ToList();

                var games = allParsedGames
                    .OrderByDescending(GetUpcomingSelectionScore)
                    .ThenBy(x => x.SteamRank <= 0 ? int.MaxValue : x.SteamRank)
                    .Take(targetCount)
                    .ToList();

                var paidOrPreorderCount = allParsedGames.Count(x => x.PriceFinal > 0 || x.IsPreorder);
                DebugLog($"[Upcoming] Candidate pool | total={allParsedGames.Count} | paid/preorder={paidOrPreorderCount} | selected={games.Count}");

                if (games.Count == 0)
                {
                    var cached = LoadCacheFromPath(targetCachePath, "Upcoming");
                    if (cached.Count > 0)
                    {
                        logger.Warn($"[Upcoming] Refresh returned 0 games. Keeping previous cache with {cached.Count} games instead of overwriting it.");
                        return cached;
                    }

                    logger.Warn("[Upcoming] Refresh returned 0 games and no previous cache is available. Empty cache will not be written.");
                    return games;
                }

                await EnrichGamesInParallelAsync(games, language, region, "Steam Popular Coming Soon", 4);

                var displayRank = 1;
                foreach (var game in games.Take(24))
                {
                    DebugLog(
                        $"[Upcoming] Selected | #{displayRank} | {game.Name} | AppId={game.AppId} | Source={game.Source} | Rank={game.SteamRank} | Score={GetUpcomingSelectionScore(game)} | Price={game.PriceFinal} | Release={game.ReleaseDateDisplay ?? game.ReleaseDate} | Header={(string.IsNullOrWhiteSpace(game.HeaderImageLocalPath) ? "missing" : "ok")}"
                    );
                    displayRank++;
                }

                File.WriteAllText(targetCachePath, JsonConvert.SerializeObject(games, Formatting.Indented));

                DebugLog($"[Upcoming] Cache saved: {games.Count} games -> {targetCachePath}");

                return games;
            }
        }

        private async Task ScanUpcomingSearchPagesAsync(
            HttpClient client,
            string filter,
            string label,
            int pageSize,
            int maxPages,
            Dictionary<int, SteamUpcomingGameItem> parsedByAppId,
            string language,
            string region,
            string sourceName,
            int sourceRankOffset)
        {
            for (var page = 0; page < maxPages; page++)
            {
                var start = page * pageSize;
                var url = BuildSteamSearchUrl(filter, language, region, start, pageSize);

                var html = await client.GetStringAsync(url);

                var pageItems = ParseGames(html)
                    .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                    .ToList();

                foreach (var item in pageItems)
                {
                    item.SteamRank = sourceRankOffset + start + Math.Max(1, item.SteamRank);
                    item.Source = sourceName;

                    if (parsedByAppId.TryGetValue(item.AppId, out var existing))
                    {
                        if (item.SteamRank > 0 && (existing.SteamRank <= 0 || item.SteamRank < existing.SteamRank))
                        {
                            parsedByAppId[item.AppId] = item;
                        }

                        continue;
                    }

                    parsedByAppId[item.AppId] = item;
                }

                var paidCandidateCount = parsedByAppId.Values.Count(x => x.PriceFinal > 0 || x.IsPreorder);

                DebugLog($"[Upcoming] Scan {label} page {page + 1}/{maxPages} | parsed={pageItems.Count} | unique={parsedByAppId.Count} | paid/preorder={paidCandidateCount}");

                if (pageItems.Count == 0)
                {
                    break;
                }
            }
        }

        private string BuildSteamSearchUrl(string filter, string language, string region, int start, int count)
        {
            var safeFilter = Uri.EscapeDataString(string.IsNullOrWhiteSpace(filter) ? "popularcomingsoon" : filter.Trim());
            var safeLanguage = Uri.EscapeDataString(string.IsNullOrWhiteSpace(language) ? "english" : language.Trim());
            var safeRegion = Uri.EscapeDataString(string.IsNullOrWhiteSpace(region) ? "US" : region.Trim().ToUpperInvariant());

            // Do not force os=win here. Steam often hides unreleased big games from the Windows filter
            // until the store page is finalized, while they still appear on the official wishlist page.
            return $"https://store.steampowered.com/search/?filter={safeFilter}&count={count}&start={start}&l={safeLanguage}&cc={safeRegion}";
        }

        private long GetUpcomingSelectionScore(SteamUpcomingGameItem item)
        {
            if (item == null)
            {
                return long.MinValue;
            }

            var rank = item.SteamRank > 0 ? item.SteamRank : 999999;
            long score = Math.Max(0, 300000 - rank) * 1000L;

            // Price/preorder is now only a quality bonus. It must not hide big unreleased games.
            if (item.PriceFinal > 0)
            {
                score += 350000L;
            }

            if (item.IsPreorder)
            {
                score += 250000L;
            }

            var release = ((item.ReleaseDateDisplay ?? item.ReleaseDate) ?? string.Empty).Trim().ToLowerInvariant();
            if (LooksLikeExactReleaseDate(release))
            {
                score += 180000L;
            }
            else if (LooksLikeYearOnlyReleaseDate(release))
            {
                score += 45000L;
            }
            else if (LooksLikeUnknownReleaseDate(release))
            {
                score -= 90000L;
            }

            if (!string.IsNullOrWhiteSpace(item.HeaderImageLocalPath) || !string.IsNullOrWhiteSpace(item.CapsuleImageLocalPath))
            {
                score += 50000L;
            }

            return score;
        }

        private bool IsGoodUpcomingCandidate(SteamUpcomingGameItem item)
        {
            if (item == null || item.AppId <= 0 || string.IsNullOrWhiteSpace(item.Name))
            {
                return false;
            }

            if (ShouldSkip(item.Name, item.Name))
            {
                return false;
            }

            if (TitleLooksLikeStoreHardware(item.Name))
            {
                return false;
            }

            return true;
        }

        public async Task<List<SteamUpcomingGameItem>> RefreshWishlistedAsync(string language, string region)
        {
            var targetCachePath = GetSearchCachePath("popularwishlist", language, region);

            return await RefreshSearchListAsync(
                "Wishlisted",
                "Steam Most Wishlisted",
                "https://store.steampowered.com/search/?filter=popularwishlist&count=100",
                targetCachePath,
                language,
                region,
                false,
                false
            );
        }

        public async Task<List<SteamUpcomingGameItem>> RefreshNewReleasesAsync(string language, string region)
        {
            var targetCachePath = GetSearchCachePath("popularnew_released_desc", language, region);

            return await RefreshSearchListAsync(
                "New Releases",
                "Steam Popular New Releases",
                "https://store.steampowered.com/search/?filter=popularnew&sort_by=Released_DESC&os=win&count=100",
                targetCachePath,
                language,
                region,
                false,
                true
            );
        }


        public async Task<List<SteamUpcomingGameItem>> RefreshRecommendedAsync(IEnumerable<string> searchTerms, string profileKey, string language, string region, Action<int> reportProgress = null)
        {
            var filterKey = BuildRecommendedCacheFilterKey(profileKey);
            var targetCachePath = GetSearchCachePath(filterKey, language, region);

            var terms = (searchTerms ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => x.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(9)
                .ToList();

            if (terms.Count == 0)
            {
                return new List<SteamUpcomingGameItem>();
            }

            reportProgress?.Invoke(15);
            DebugLog($"[Recommended] RefreshRecommendedAsync START | terms={string.Join(", ", terms)} | profile={profileKey}");

            var combined = new List<SteamUpcomingGameItem>();
            var seenAppIds = new HashSet<int>();

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);

                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36"
                );

                client.DefaultRequestHeaders.Accept.ParseAdd(
                    "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
                );

                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                var termIndex = 0;

                foreach (var term in terms)
                {
                    try
                    {
                        termIndex++;
                        var url = BuildRecommendedSearchUrl(term, language, region);
                        var html = await client.GetStringAsync(url);

                        var parsed = ParseGames(html)
                            .Where(x => x != null && x.AppId > 0)
                            .OrderBy(x => x.SteamRank)
                            .Take(8)
                            .ToList();

                        DebugLog($"[Recommended] Search term #{termIndex}/{terms.Count} '{term}' -> parsed={parsed.Count}");

                        foreach (var game in parsed)
                        {
                            if (game == null || game.AppId <= 0)
                            {
                                continue;
                            }

                            if (game.Tags == null)
                            {
                                game.Tags = new List<string>();
                            }

                            if (!game.Tags.Any(x => string.Equals(x, term, StringComparison.OrdinalIgnoreCase)))
                            {
                                game.Tags.Add(term);
                            }

                            if (!seenAppIds.Add(game.AppId))
                            {
                                var existing = combined.FirstOrDefault(x => x != null && x.AppId == game.AppId);
                                if (existing != null)
                                {
                                    if (existing.Tags == null)
                                    {
                                        existing.Tags = new List<string>();
                                    }

                                    if (!existing.Tags.Any(x => string.Equals(x, term, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        existing.Tags.Add(term);
                                    }
                                }

                                continue;
                            }

                            game.Source = "Steam Recommended For You";
                            combined.Add(game);
                        }

                        var searchProgress = 15 + (int)Math.Round((termIndex / (double)Math.Max(1, terms.Count)) * 35);
                        reportProgress?.Invoke(Math.Min(50, searchProgress));

                        if (combined.Count >= 40)
                        {
                            break;
                        }
                    }
                    catch (Exception exTerm)
                    {
                        logger.Warn(exTerm, $"[Recommended] Search term failed: {term}");
                    }
                }
            }

            var games = combined
                .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.AppId)
                .Select(x => x.First())
                .OrderBy(x => x.SteamRank <= 0 ? int.MaxValue : x.SteamRank)
                .Take(24)
                .ToList();

            reportProgress?.Invoke(55);

            if (games.Count > 0)
            {
                var semaphore = new SemaphoreSlim(4);
                var enrichedCount = 0;

                var enrichTasks = games.Select(async game =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        game.Source = "Steam Recommended For You";
                        await EnrichUpcomingGameAsync(game, language, region);
                    }
                    finally
                    {
                        semaphore.Release();

                        var done = Interlocked.Increment(ref enrichedCount);
                        var enrichProgress = 55 + (int)Math.Round((done / (double)Math.Max(1, games.Count)) * 35);
                        reportProgress?.Invoke(Math.Min(90, enrichProgress));
                    }
                }).ToList();

                await Task.WhenAll(enrichTasks);
            }

            foreach (var game in games)
            {
                game.Source = "Steam Recommended For You";
            }

            File.WriteAllText(targetCachePath, JsonConvert.SerializeObject(games, Formatting.Indented));

            reportProgress?.Invoke(95);
            DebugLog($"[Recommended] Cache saved: {games.Count} games -> {targetCachePath}");

            return games;
        }

        public bool IsRecommendedCacheMissingOrExpired(string profileKey, string language, string region, TimeSpan maxAge)
        {
            var filterKey = BuildRecommendedCacheFilterKey(profileKey);
            var targetCachePath = GetSearchCachePath(filterKey, language, region);

            if (!File.Exists(targetCachePath))
            {
                return true;
            }

            if (CacheHasMissingLocalImages(filterKey, language, region))
            {
                return true;
            }

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(targetCachePath);
            return age > maxAge;
        }

        public List<SteamUpcomingGameItem> LoadRecommendedFromCacheOnly(string profileKey, string language, string region)
        {
            try
            {
                var filterKey = BuildRecommendedCacheFilterKey(profileKey);
                var targetCachePath = GetSearchCachePath(filterKey, language, region);

                if (!File.Exists(targetCachePath))
                {
                    return new List<SteamUpcomingGameItem>();
                }

                var json = File.ReadAllText(targetCachePath);

                return JsonConvert.DeserializeObject<List<SteamUpcomingGameItem>>(json)
                    ?? new List<SteamUpcomingGameItem>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Recommended] Failed to load cache.");
                return new List<SteamUpcomingGameItem>();
            }
        }

        private string BuildRecommendedCacheFilterKey(string profileKey)
        {
            if (string.IsNullOrWhiteSpace(profileKey))
            {
                profileKey = "default";
            }

            return "recommended_" + SanitizeCachePart(profileKey);
        }

        private string BuildRecommendedSearchUrl(string searchTerm, string language, string region)
        {
            var term = Uri.EscapeDataString(searchTerm ?? string.Empty);
            var safeLanguage = Uri.EscapeDataString(string.IsNullOrWhiteSpace(language) ? "english" : language.Trim());
            var safeRegion = Uri.EscapeDataString(string.IsNullOrWhiteSpace(region) ? "US" : region.Trim().ToUpperInvariant());

            return $"https://store.steampowered.com/search/?term={term}&os=win&count=100&l={safeLanguage}&cc={safeRegion}";
        }

        private async Task<List<SteamUpcomingGameItem>> RefreshSearchListAsync(string logName, string sourceName, string url, string targetCachePath, string language, string region, bool sortByReleaseDate, bool requirePaidPrice)
        {
            DebugLog($"[{logName}] RefreshSearchListAsync START");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);

                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36"
                );

                client.DefaultRequestHeaders.Accept.ParseAdd(
                    "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
                );

                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                const int pageSize = 100;
                const int maxPages = 3;
                const int targetCount = 24;

                var parsedByAppId = new Dictionary<int, SteamUpcomingGameItem>();

                for (var page = 0; page < maxPages; page++)
                {
                    var start = page * pageSize;
                    var pageUrl = BuildPagedSteamSearchUrl(url, start, pageSize);
                    var html = await client.GetStringAsync(pageUrl);

                    var pageItems = ParseGames(html)
                        .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                        .ToList();

                    foreach (var item in pageItems)
                    {
                        if (parsedByAppId.ContainsKey(item.AppId))
                        {
                            continue;
                        }

                        item.SteamRank = start + Math.Max(1, item.SteamRank);
                        parsedByAppId[item.AppId] = item;
                    }

                    DebugLog($"[{logName}] Scan page {page + 1}/{maxPages} | parsed={pageItems.Count} | unique={parsedByAppId.Count}");

                    if (pageItems.Count == 0 || parsedByAppId.Count >= 60)
                    {
                        break;
                    }
                }

                IEnumerable<SteamUpcomingGameItem> query = parsedByAppId.Values;

                if (requirePaidPrice)
                {
                    query = query.Where(x => x.PriceFinal > 0);
                }

                if (sortByReleaseDate)
                {
                    query = query
                        .OrderByDescending(x => x.SteamRank > 0 && x.SteamRank <= 50)
                        .ThenBy(x => GetReleaseSortDate(x.ReleaseDate))
                        .ThenBy(x => x.SteamRank);
                }
                else
                {
                    query = query.OrderBy(x => x.SteamRank);
                }

                var games = query
                    .Take(targetCount)
                    .ToList();

                DebugLog($"[{logName}] Selected {games.Count}/{parsedByAppId.Count} scanned games for cache.");

                if (games.Count == 0)
                {
                    var cached = LoadCacheFromPath(targetCachePath, logName);
                    if (cached.Count > 0)
                    {
                        logger.Warn($"[{logName}] Refresh returned 0 games. Keeping previous cache with {cached.Count} games instead of overwriting it.");
                        return cached;
                    }

                    logger.Warn($"[{logName}] Refresh returned 0 games and no previous cache is available. Empty cache will not be written.");
                    return games;
                }

                await EnrichGamesInParallelAsync(games, language, region, sourceName, 4);

                File.WriteAllText(targetCachePath, JsonConvert.SerializeObject(games, Formatting.Indented));

                DebugLog($"[{logName}] Cache saved: {games.Count} games -> {targetCachePath}");

                return games;
            }
        }

        private static string BuildPagedSteamSearchUrl(string baseUrl, int start, int count)
        {
            var result = string.IsNullOrWhiteSpace(baseUrl) ? "https://store.steampowered.com/search/?os=win" : baseUrl;

            result = Regex.Replace(result, @"([?&])start=\d+", "$1start=" + start, RegexOptions.IgnoreCase);
            if (!Regex.IsMatch(result, @"[?&]start=", RegexOptions.IgnoreCase))
            {
                result += (result.Contains("?") ? "&" : "?") + "start=" + start;
            }

            result = Regex.Replace(result, @"([?&])count=\d+", "$1count=" + count, RegexOptions.IgnoreCase);
            if (!Regex.IsMatch(result, @"[?&]count=", RegexOptions.IgnoreCase))
            {
                result += (result.Contains("?") ? "&" : "?") + "count=" + count;
            }

            return result;
        }

        private List<SteamUpcomingGameItem> LoadCacheFromPath(string cachePath, string logName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
                {
                    return new List<SteamUpcomingGameItem>();
                }

                var json = File.ReadAllText(cachePath);
                var cached = JsonConvert.DeserializeObject<List<SteamUpcomingGameItem>>(json)
                    ?? new List<SteamUpcomingGameItem>();

                cached = cached
                    .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                    .ToList();

                DebugLog($"[{logName}] Previous cache loaded: {cached.Count} games -> {cachePath}");
                return cached;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[{logName}] Failed to load previous cache from {cachePath}.");
                return new List<SteamUpcomingGameItem>();
            }
        }

        public bool IsCacheMissingOrExpired(string language, string region, TimeSpan maxAge)
        {
            var targetCachePath = GetSearchCachePath("popularcomingsoon", language, region);

            if (!File.Exists(targetCachePath))
            {
                return true;
            }

            if (CacheHasMissingLocalImages("popularcomingsoon", language, region))
            {
                return true;
            }

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(targetCachePath);
            return age > maxAge;
        }

        public bool IsWishlistedCacheMissingOrExpired(string language, string region, TimeSpan maxAge)
        {
            var targetCachePath = GetSearchCachePath("popularwishlist", language, region);

            if (!File.Exists(targetCachePath))
            {
                return true;
            }

            if (CacheHasMissingLocalImages("popularwishlist", language, region))
            {
                return true;
            }

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(targetCachePath);
            return age > maxAge;
        }

        public bool IsNewReleasesCacheMissingOrExpired(string language, string region, TimeSpan maxAge)
        {
            var targetCachePath = GetSearchCachePath("popularnew_released_desc", language, region);

            if (!File.Exists(targetCachePath))
            {
                return true;
            }

            if (CacheHasMissingLocalImages("popularnew_released_desc", language, region))
            {
                return true;
            }

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(targetCachePath);
            return age > maxAge;
        }

        private int GetMinimumCacheCount(string filterKey)
        {
            if (string.IsNullOrWhiteSpace(filterKey))
            {
                return 0;
            }

            if (filterKey.IndexOf("popularcomingsoon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 18;
            }

            if (filterKey.IndexOf("popularwishlist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filterKey.IndexOf("popularnew", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 18;
            }

            return 0;
        }

        private bool CacheHasMissingLocalImages(string filterKey, string language, string region)
        {
            try
            {
                var targetCachePath = GetSearchCachePath(filterKey, language, region);

                if (!File.Exists(targetCachePath))
                {
                    return true;
                }

                var json = File.ReadAllText(targetCachePath);

                var items = JsonConvert.DeserializeObject<List<SteamUpcomingGameItem>>(json)
                    ?? new List<SteamUpcomingGameItem>();

                var minimumCount = GetMinimumCacheCount(filterKey);
                if (minimumCount > 0 && items.Count(x => x != null && x.AppId > 0) < minimumCount)
                {
                    DebugLog($"[SteamStoreCache] Cache '{filterKey}' has only {items.Count(x => x != null && x.AppId > 0)} items, refresh required.");
                    return true;
                }

                foreach (var item in items.Where(x => x != null).Take(20))
                {
                    // A cache is valid as long as the card has at least one local image.
                    // Do not force a refresh just because the header is missing: the capsule
                    // may already be valid and refreshing again can overwrite a good cache
                    // during navigation.
                    var hasHeader = !string.IsNullOrWhiteSpace(item.HeaderImageLocalPath) && File.Exists(item.HeaderImageLocalPath);
                    var hasCapsule = !string.IsNullOrWhiteSpace(item.CapsuleImageLocalPath) && File.Exists(item.CapsuleImageLocalPath);

                    if (!hasHeader && !hasCapsule)
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(item.BackgroundImageLocalPath) &&
                        !File.Exists(item.BackgroundImageLocalPath))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Screenshot1LocalPath) &&
                        !File.Exists(item.Screenshot1LocalPath))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Screenshot2LocalPath) &&
                        !File.Exists(item.Screenshot2LocalPath))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Screenshot3LocalPath) &&
                        !File.Exists(item.Screenshot3LocalPath))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Screenshot4LocalPath) &&
                        !File.Exists(item.Screenshot4LocalPath))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Screenshot5LocalPath) &&
                        !File.Exists(item.Screenshot5LocalPath))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        public List<SteamUpcomingGameItem> LoadFromCacheOnly(string language, string region)
        {
            try
            {
                var targetCachePath = GetSearchCachePath("popularcomingsoon", language, region);

                if (!File.Exists(targetCachePath))
                {
                    return new List<SteamUpcomingGameItem>();
                }

                var json = File.ReadAllText(targetCachePath);

                return JsonConvert.DeserializeObject<List<SteamUpcomingGameItem>>(json)
                    ?? new List<SteamUpcomingGameItem>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Upcoming] Failed to load cache.");
                return new List<SteamUpcomingGameItem>();
            }
        }

        public List<SteamUpcomingGameItem> LoadWishlistedFromCacheOnly(string language, string region)
        {
            try
            {
                var targetCachePath = GetSearchCachePath("popularwishlist", language, region);

                if (!File.Exists(targetCachePath))
                {
                    return new List<SteamUpcomingGameItem>();
                }

                var json = File.ReadAllText(targetCachePath);

                return JsonConvert.DeserializeObject<List<SteamUpcomingGameItem>>(json)
                    ?? new List<SteamUpcomingGameItem>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[Wishlisted] Failed to load cache.");
                return new List<SteamUpcomingGameItem>();
            }
        }

        public List<SteamUpcomingGameItem> LoadNewReleasesFromCacheOnly(string language, string region)
        {
            try
            {
                var targetCachePath = GetSearchCachePath("popularnew_released_desc", language, region);

                if (!File.Exists(targetCachePath))
                {
                    return new List<SteamUpcomingGameItem>();
                }

                var json = File.ReadAllText(targetCachePath);

                return JsonConvert.DeserializeObject<List<SteamUpcomingGameItem>>(json)
                    ?? new List<SteamUpcomingGameItem>();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[New Releases] Failed to load cache.");
                return new List<SteamUpcomingGameItem>();
            }
        }

        private DateTime GetReleaseSortDate(string releaseDate)
        {
            if (string.IsNullOrWhiteSpace(releaseDate))
            {
                return DateTime.MaxValue;
            }

            if (DateTime.TryParse(
                releaseDate,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedDate))
            {
                return parsedDate;
            }

            if (int.TryParse(releaseDate.Trim(), out var year))
            {
                return new DateTime(year, 12, 31);
            }

            return DateTime.MaxValue;
        }

        private async Task EnrichGamesInParallelAsync(List<SteamUpcomingGameItem> games, string language, string region, string sourceName, int maxDegreeOfParallelism)
        {
            if (games == null || games.Count == 0)
            {
                return;
            }

            var degree = Math.Max(1, Math.Min(6, maxDegreeOfParallelism));
            var semaphore = new SemaphoreSlim(degree);
            var tasks = games.Select(async game =>
            {
                if (game == null)
                {
                    return;
                }

                await semaphore.WaitAsync();
                try
                {
                    game.Source = sourceName;
                    await EnrichUpcomingGameAsync(game, language, region);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }

        private async Task<string> DownloadUpcomingImageAsync(string imageUrl, int appId, string suffix)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    return string.Empty;
                }

                if (!IsValidSteamAppImageUrl(appId, imageUrl))
                {
                    return string.Empty;
                }

                Directory.CreateDirectory(imageCacheFolder);

                var safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "image" : suffix;
                var localPath = Path.Combine(imageCacheFolder, $"upcoming_{appId}_{safeSuffix}.jpg");
                var sourceMarkerPath = localPath + ".source.txt";
                var oldUrlMarkerPath = localPath + ".url";

                var existingIsUsable = File.Exists(localPath) && new FileInfo(localPath).Length > 1024;
                if (existingIsUsable && File.Exists(sourceMarkerPath))
                {
                    try
                    {
                        var cachedUrl = File.ReadAllText(sourceMarkerPath);
                        if (string.Equals(cachedUrl, imageUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            return localPath;
                        }
                    }
                    catch
                    {
                    }
                }

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                    using (var response = await client.GetAsync(imageUrl))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return existingIsUsable ? localPath : string.Empty;
                        }

                        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

                        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        {
                            return existingIsUsable ? localPath : string.Empty;
                        }

                        var bytes = await response.Content.ReadAsByteArrayAsync();

                        if (bytes == null || bytes.Length <= 1024)
                        {
                            return existingIsUsable ? localPath : string.Empty;
                        }

                        var tmpPath = localPath + ".tmp";
                        try
                        {
                            if (File.Exists(tmpPath))
                            {
                                File.Delete(tmpPath);
                            }
                        }
                        catch
                        {
                        }

                        File.WriteAllBytes(tmpPath, bytes);

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
                    }
                }

                return File.Exists(localPath) ? localPath : string.Empty;
            }
            catch
            {
                try
                {
                    var safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "image" : suffix;
                    var localPath = Path.Combine(imageCacheFolder, $"upcoming_{appId}_{safeSuffix}.jpg");
                    return File.Exists(localPath) && new FileInfo(localPath).Length > 1024 ? localPath : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private async Task<string> DownloadFirstUpcomingImageAsync(int appId, string suffix, params string[] imageUrls)
        {
            if (imageUrls == null)
            {
                return string.Empty;
            }

            foreach (var imageUrl in imageUrls)
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    continue;
                }

                if (!IsValidSteamAppImageUrl(appId, imageUrl))
                {
                    continue;
                }

                var localPath = await DownloadUpcomingImageAsync(imageUrl, appId, suffix);

                if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                {
                    return localPath;
                }
            }

            return string.Empty;
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

        private async Task EnrichUpcomingGameAsync(SteamUpcomingGameItem game, string language, string region)
        {
            if (game == null || game.AppId <= 0 || steamStoreService == null)
            {
                return;
            }

            try
            {
                var fallbackCapsuleUrl = BuildSteamAppImageUrl(game.AppId, "capsule_616x353.jpg");
                var fallbackHeaderUrl = BuildSteamAppImageUrl(game.AppId, "header.jpg");
                var fallbackBackgroundUrl = BuildSteamAppImageUrl(game.AppId, "library_hero.jpg");

                var safeGameHeaderUrl = FirstValidSteamAppImageUrl(game.AppId, game.HeaderImage, fallbackHeaderUrl);
                var safeGameCapsuleUrl = FirstValidSteamAppImageUrl(game.AppId, game.CapsuleImageUrl, fallbackCapsuleUrl);

                var storeItem = new SteamStoreItem
                {
                    AppId = game.AppId,
                    Name = game.Name,
                    StoreUrl = game.StoreUrl,
                    HeaderImageUrl = safeGameHeaderUrl,
                    CapsuleImageUrl = safeGameCapsuleUrl,
                    FinalPriceDisplay = game.FinalPriceDisplay,
                    OriginalPriceDisplay = game.OriginalPriceDisplay,
                    DiscountDisplay = game.DiscountDisplay
                };

                // Store lists stay light: appdetails gives us metadata and remote media URLs,
                // but background/screenshots are downloaded only when the details view opens.
                await steamStoreService.EnrichStoreItemDetailsAsync(storeItem, language, region, downloadMedia: false);

                var isComingSoon = storeItem.ComingSoon || game.ComingSoon;
                var useHeaderCard =
                    isComingSoon ||
                    string.Equals(game.Source, "Steam Popular Coming Soon", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(game.Source, "Steam Popular New Releases", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(game.Source, "Steam Most Wishlisted", StringComparison.OrdinalIgnoreCase);

                // Store cards are now header-first. Capsule is downloaded only as fallback.
                var headerUrl = FirstValidSteamAppImageUrl(
                     game.AppId,
                     storeItem.HeaderImageUrl,
                     game.HeaderImage,
                     fallbackHeaderUrl
                 );

                var headerLocalPath = await DownloadFirstUpcomingImageAsync(
                    game.AppId,
                    "header",
                    headerUrl,
                    storeItem.HeaderImageUrl,
                    game.HeaderImage,
                    fallbackHeaderUrl
                );

                var capsuleUrl = FirstValidSteamAppImageUrl(
                    game.AppId,
                    storeItem.CapsuleImageUrl,
                    game.CapsuleImageUrl,
                    fallbackCapsuleUrl,
                    headerUrl
                );

                var capsuleLocalPath = await DownloadFirstUpcomingImageAsync(
                     game.AppId,
                     "capsule_616x353",
                     capsuleUrl,
                     storeItem.CapsuleImageUrl,
                     game.CapsuleImageUrl,
                     fallbackCapsuleUrl
                 );

                var backgroundUrl = FirstValidSteamAppImageUrl(
                    game.AppId,
                    storeItem.BackgroundImageUrl,
                    game.BackgroundImageUrl,
                    fallbackBackgroundUrl,
                    storeItem.Screenshot1Url,
                    headerUrl,
                    capsuleUrl
                );

                // Do not download the real background here. The list/card can safely use
                // the already downloaded header/capsule fallback. The real background and
                // screenshots are downloaded lazily by OpenSteamStoreDetails.
                var backgroundLocalPath = FirstNonEmpty(headerLocalPath, capsuleLocalPath);

                game.HeaderImage = headerUrl;
                game.HeaderImageLocalPath = headerLocalPath;

                game.CapsuleImageUrl = capsuleUrl;
                game.CapsuleImageLocalPath = capsuleLocalPath;

                game.BackgroundImageUrl = backgroundUrl;
                game.BackgroundImageLocalPath = backgroundLocalPath;

                game.ShortDescription = FirstNonEmpty(
                    storeItem.ShortDescription,
                    game.ShortDescription
                );

                game.ReleaseDateDisplay = FirstNonEmpty(
                    storeItem.ReleaseDateDisplay,
                    game.ReleaseDateDisplay,
                    game.ReleaseDate
                );

                game.ComingSoon = storeItem.ComingSoon;
                game.IsPreorder = storeItem.IsPreorder;

                game.Genres = storeItem.Genres ?? new List<string>();
                game.Categories = storeItem.Categories ?? new List<string>();
                game.Developers = storeItem.Developers ?? new List<string>();
                game.Publishers = storeItem.Publishers ?? new List<string>();

                game.Screenshot1Url = storeItem.Screenshot1Url;
                game.Screenshot1LocalPath = storeItem.Screenshot1LocalPath;

                game.Screenshot2Url = storeItem.Screenshot2Url;
                game.Screenshot2LocalPath = storeItem.Screenshot2LocalPath;

                game.Screenshot3Url = storeItem.Screenshot3Url;
                game.Screenshot3LocalPath = storeItem.Screenshot3LocalPath;

                game.Screenshot4Url = storeItem.Screenshot4Url;
                game.Screenshot4LocalPath = storeItem.Screenshot4LocalPath;

                game.Screenshot5Url = storeItem.Screenshot5Url;
                game.Screenshot5LocalPath = storeItem.Screenshot5LocalPath;

                game.FinalPriceDisplay = FirstNonEmpty(
                    storeItem.FinalPriceDisplay,
                    game.FinalPriceDisplay
                );

                game.OriginalPriceDisplay = FirstNonEmpty(
                    storeItem.OriginalPriceDisplay,
                    game.OriginalPriceDisplay
                );

                game.DiscountDisplay = FirstNonEmpty(
                    storeItem.DiscountDisplay,
                    game.DiscountDisplay
                );
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[Upcoming] Failed to enrich AppId={game.AppId} | {game.Name}");
            }
        }

        private List<SteamUpcomingGameItem> ParseGames(string html)
        {
            var results = new List<SteamUpcomingGameItem>();

            if (string.IsNullOrWhiteSpace(html))
            {
                logger.Warn("[Upcoming] HTML is empty.");
                return results;
            }

            var rowRegex = new Regex(
                @"<a\s+[\s\S]*?class=""[^""]*search_result_row[^""]*""[\s\S]*?</a>",
                RegexOptions.IgnoreCase);

            var rows = rowRegex.Matches(html);

            foreach (Match row in rows)
            {
                var block = row.Value;

                var appIdMatch = Regex.Match(block, @"data-ds-appid=""(?<appid>\d+)""", RegexOptions.IgnoreCase);
                var urlMatch = Regex.Match(block, @"href=""(?<url>https://store\.steampowered\.com/app/\d+/[^""]*)""", RegexOptions.IgnoreCase);
                var titleMatch = Regex.Match(block, @"<span class=""title"">(?<title>.*?)</span>", RegexOptions.IgnoreCase);
                var dateMatch = Regex.Match(block, @"<div class=""search_released responsive_secondrow"">\s*(?<date>.*?)\s*</div>", RegexOptions.IgnoreCase);
                var priceMatch = Regex.Match(block, @"data-price-final=""(?<price>\d+)""", RegexOptions.IgnoreCase);
                var capsuleMatch = Regex.Match(block, @"<img src=""(?<img>https://[^""]+)""", RegexOptions.IgnoreCase);

                if (!appIdMatch.Success || !titleMatch.Success)
                {
                    continue;
                }

                if (!int.TryParse(appIdMatch.Groups["appid"].Value, out var appId))
                {
                    continue;
                }

                var title = Clean(titleMatch.Groups["title"].Value);
                var date = dateMatch.Success ? Clean(dateMatch.Groups["date"].Value) : "";
                var url = urlMatch.Success ? urlMatch.Groups["url"].Value : $"https://store.steampowered.com/app/{appId}/";
                var parsedCapsule = capsuleMatch.Success ? capsuleMatch.Groups["img"].Value : "";
                var fallbackCapsule = BuildSteamAppImageUrl(appId, "capsule_616x353.jpg");
                var fallbackHeader = BuildSteamAppImageUrl(appId, "header.jpg");

                if (!IsValidSteamAppImageUrl(appId, parsedCapsule))
                {
                    parsedCapsule = string.Empty;
                }

                // Important : on garde d'abord l'image réellement trouvée dans la page Steam Search,
                // mais uniquement si elle appartient bien au jeu.
                var capsule = FirstValidSteamAppImageUrl(
                    appId,
                    parsedCapsule,
                    fallbackCapsule,
                    fallbackHeader
                );

                var price = 0;
                if (priceMatch.Success)
                {
                    int.TryParse(priceMatch.Groups["price"].Value, out price);
                }

                var steamRank = results.Count + 1;

                if (ShouldSkip(title, block))
                {
                    continue;
                }

                results.Add(new SteamUpcomingGameItem
                {
                    AppId = appId,
                    Name = title,
                    ReleaseDate = date,
                    ReleaseDateDisplay = date,
                    StoreUrl = url,
                    HeaderImage = FirstValidSteamAppImageUrl(appId, fallbackHeader, parsedCapsule),
                    CapsuleImageUrl = capsule,
                    Source = "Steam Popular Coming Soon",
                    PriceFinal = price,
                    SteamRank = steamRank,
                    CachedAt = DateTime.Now
                });
            }

            var uniqueResults = results
                .GroupBy(x => x.AppId)
                .Select(x => x.First())
                .ToList();

            DebugLog($"[Upcoming] Parsed {uniqueResults.Count} valid items.");

            return uniqueResults;
        }

        private bool ShouldSkip(string title, string block)
        {
            var name = (title ?? string.Empty).ToLowerInvariant();
            var row = (block ?? string.Empty).ToLowerInvariant();
            var text = (name + " " + row).ToLowerInvariant();

            // Important:
            // Do NOT apply every exclusion keyword to the full Steam row HTML.
            // Some words like "bundle" can appear in Steam markup/classes and make every paid game look invalid.
            // Content-type exclusions are title-based; adult/free flags can still use the row HTML.
            if (TitleLooksLikeNonGameContent(name))
            {
                return true;
            }

            return row.Contains("free to play")
                || row.Contains(">free<")
                || row.Contains("discount_final_price free")
                || text.Contains(" adult only")
                || text.Contains("sexual content")
                || text.Contains("nudity")
                || text.Contains("hentai")
                || text.Contains("nsfw")
                || text.Contains("porn")
                || text.Contains("sexual")
                || text.Contains("erotic")
                || text.Contains("nude")
                || text.Contains("naked")
                || text.Contains("lewd")
                || text.Contains("ecchi")
                || text.Contains("busty")
                || text.Contains("boob")
                || text.Contains("boobs")
                || text.Contains("breast")
                || text.Contains("breasts")
                || text.Contains("milf")
                || text.Contains("waifu")
                || text.Contains("tentacle")
                || text.Contains("succubus")
                || text.Contains("18+");
        }

        private bool TitleLooksLikeNonGameContent(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return HasTitleToken(name, "demo")
                || HasTitleToken(name, "prologue")
                || name.Contains("soundtrack")
                || name.Contains("ost")
                || name.Contains(" artbook")
                || name.Contains("art book")
                || name.Contains("wallpaper")
                || name.Contains("supporter pack")
                || name.Contains("upgrade pack")
                || name.Contains("story pack")
                || name.Contains("season pass")
                || name.EndsWith(" bundle")
                || name.Contains(" bundle:")
                || name.Contains(" bundle -")
                || name.Contains(" dlc")
                || name.EndsWith(" dlc");
        }

        private bool HasTitleToken(string value, string token)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return Regex.IsMatch(
                value,
                $@"(^|[^a-z0-9]){Regex.Escape(token)}($|[^a-z0-9])",
                RegexOptions.IgnoreCase
            );
        }


        private bool LooksLikeExactReleaseDate(string release)
        {
            if (string.IsNullOrWhiteSpace(release))
            {
                return false;
            }

            var value = release.Trim().ToLowerInvariant();

            if (LooksLikeUnknownReleaseDate(value) || LooksLikeYearOnlyReleaseDate(value))
            {
                return false;
            }

            // Examples: "9 juil. 2026", "25 sept. 2026", "feb 23, 2027", "2026-09-25".
            return Regex.IsMatch(value, @"\b\d{1,2}\b.*\b\d{4}\b")
                || Regex.IsMatch(value, @"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b")
                || Regex.IsMatch(value, @"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec|janv|févr|fevr|mars|avr|mai|juin|juil|août|aout|déc|dec)\b.*\b\d{1,2}\b.*\b\d{4}\b");
        }

        private bool LooksLikeYearOnlyReleaseDate(string release)
        {
            if (string.IsNullOrWhiteSpace(release))
            {
                return false;
            }

            var value = release.Trim().ToLowerInvariant();
            return Regex.IsMatch(value, @"^\d{4}$")
                || Regex.IsMatch(value, @"^(q[1-4]|1er trimestre|2e trimestre|3e trimestre|4e trimestre|premier trimestre|deuxième trimestre|deuxieme trimestre|troisième trimestre|troisieme trimestre|quatrième trimestre|quatrieme trimestre)\s+\d{4}$");
        }

        private bool LooksLikeUnknownReleaseDate(string release)
        {
            if (string.IsNullOrWhiteSpace(release))
            {
                return true;
            }

            var value = release.Trim().ToLowerInvariant();
            return value.Contains("à déterminer")
                || value.Contains("a determiner")
                || value.Contains("to be announced")
                || value.Contains("tba")
                || value.Contains("coming soon")
                || value.Contains("prochainement")
                || value.Contains("bientôt")
                || value.Contains("bientot");
        }

        private bool TitleLooksLikeStoreHardware(string title)
        {
            var name = (title ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return name.Contains("steam deck")
                || name.Contains("steam frame")
                || name.Contains("steam machine")
                || name.Contains("valve index")
                || name.Contains("vr headset")
                || name.Contains("controller bundle")
                || name.Contains("hardware");
        }

        private string Clean(string value)
        {
            return Regex.Replace(value ?? string.Empty, "<.*?>", string.Empty)
                .Replace("&amp;", "&")
                .Replace("&quot;", "\"")
                .Replace("&#39;", "'")
                .Replace("&nbsp;", " ")
                .Trim();
        }
    }
}