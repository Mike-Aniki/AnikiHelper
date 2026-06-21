using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AnikiHelper.Services
{
    public class SteamUpcomingGamesService
    {
        private readonly ILogger logger;
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
            logger.Info("[Upcoming] RefreshAsync START");

            var targetCachePath = GetSearchCachePath("popularcomingsoon", language, region);

            var url = "https://store.steampowered.com/search/?filter=popularcomingsoon&os=win&count=100";

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

                var html = await client.GetStringAsync(url);

                var games = ParseGames(html)
                    .Where(x => x.PriceFinal > 0)
                    .OrderBy(x => GetReleaseSortDate(x.ReleaseDate))
                    .ThenBy(x => x.SteamRank)
                    .Take(20)
                    .ToList();

                foreach (var game in games)
                {
                    await EnrichUpcomingGameAsync(game, language, region);
                }

                foreach (var game in games.Take(5))
                {
                    logger.Info(
                        $"[Upcoming TEST] {game.Name} | Capsule={game.CapsuleImageLocalPath}"
                    );
                }

                File.WriteAllText(targetCachePath, JsonConvert.SerializeObject(games, Formatting.Indented));

                logger.Info($"[Upcoming] Cache saved: {games.Count} games -> {targetCachePath}");

                return games;
            }
        }

        public async Task<List<SteamUpcomingGameItem>> RefreshWishlistedAsync(string language, string region)
        {
            var targetCachePath = GetSearchCachePath("popularwishlist", language, region);

            return await RefreshSearchListAsync(
                "Wishlisted",
                "Steam Most Wishlisted",
                "https://store.steampowered.com/search/?filter=popularwishlist&os=win&count=100",
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

        private async Task<List<SteamUpcomingGameItem>> RefreshSearchListAsync(string logName, string sourceName, string url, string targetCachePath, string language, string region, bool sortByReleaseDate, bool requirePaidPrice)
        {
            logger.Info($"[{logName}] RefreshSearchListAsync START");

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

                var html = await client.GetStringAsync(url);

                IEnumerable<SteamUpcomingGameItem> query = ParseGames(html);

                if (requirePaidPrice)
                {
                    query = query.Where(x => x.PriceFinal > 0);
                }

                if (sortByReleaseDate)
                {
                    query = query
                        .OrderBy(x => GetReleaseSortDate(x.ReleaseDate))
                        .ThenBy(x => x.SteamRank);
                }
                else
                {
                    query = query.OrderBy(x => x.SteamRank);
                }

                var games = query
                    .Take(20)
                    .ToList();

                foreach (var game in games)
                {
                    game.Source = sourceName;
                    await EnrichUpcomingGameAsync(game, language, region);
                }

                File.WriteAllText(targetCachePath, JsonConvert.SerializeObject(games, Formatting.Indented));

                logger.Info($"[{logName}] Cache saved: {games.Count} games -> {targetCachePath}");

                return games;
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

                foreach (var item in items.Where(x => x != null).Take(20))
                {
                    // Pour Upcoming / Wishlisted / New Releases, le header est l'image principale.
                    if (!string.IsNullOrWhiteSpace(item.HeaderImage))
                    {
                        if (string.IsNullOrWhiteSpace(item.HeaderImageLocalPath))
                        {
                            return true;
                        }

                        if (!File.Exists(item.HeaderImageLocalPath))
                        {
                            return true;
                        }
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

                // Marqueur texte pour savoir quelle URL a créé ce fichier.
                // On évite .url parce que Windows l'affiche comme un raccourci internet.
                var sourceMarkerPath = localPath + ".source.txt";

                // Ancien marqueur .url à supprimer si présent.
                var oldUrlMarkerPath = localPath + ".url";

                if (File.Exists(localPath) &&
                    new FileInfo(localPath).Length > 1024 &&
                    File.Exists(sourceMarkerPath))
                {
                    var cachedUrl = File.ReadAllText(sourceMarkerPath);

                    if (string.Equals(cachedUrl, imageUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return localPath;
                    }
                }

                // Cache ancien, mauvais, ou sans marqueur : on le remplace proprement.
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

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                    using (var response = await client.GetAsync(imageUrl))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return string.Empty;
                        }

                        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

                        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        {
                            return string.Empty;
                        }

                        var bytes = await response.Content.ReadAsByteArrayAsync();

                        if (bytes == null || bytes.Length <= 1024)
                        {
                            return string.Empty;
                        }

                        File.WriteAllBytes(localPath, bytes);
                        File.WriteAllText(sourceMarkerPath, imageUrl);
                    }
                }

                return File.Exists(localPath) ? localPath : string.Empty;
            }
            catch
            {
                return string.Empty;
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

                await steamStoreService.EnrichStoreItemDetailsAsync(storeItem, language, region);

                var isComingSoon = storeItem.ComingSoon || game.ComingSoon;
                var useHeaderCard =
                    isComingSoon ||
                    string.Equals(game.Source, "Steam Popular Coming Soon", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(game.Source, "Steam Popular New Releases", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(game.Source, "Steam Most Wishlisted", StringComparison.OrdinalIgnoreCase);

                // Upcoming / Wishlisted à venir : on utilise surtout les headers.
                // New Releases : on télécharge la vraie capsule 616x353 pour les cartes normales.
                var capsuleUrl = FirstValidSteamAppImageUrl(
                    game.AppId,
                    game.CapsuleImageUrl,
                    storeItem.CapsuleImageUrl,
                    fallbackCapsuleUrl,
                    fallbackHeaderUrl
                );

                var capsuleLocalPath = await DownloadFirstUpcomingImageAsync(
                     game.AppId,
                     "capsule_616x353",
                     fallbackCapsuleUrl,
                     storeItem.CapsuleImageUrl,
                     game.CapsuleImageUrl
                 );

                var headerUrl = FirstValidSteamAppImageUrl(
                     game.AppId,
                     storeItem.HeaderImageUrl,
                     game.HeaderImage,
                     fallbackHeaderUrl
                 );

                var headerLocalPath = await DownloadFirstUpcomingImageAsync(
                    game.AppId,
                    "header",
                    storeItem.HeaderImageUrl,
                    game.HeaderImage,
                    fallbackHeaderUrl
                );

                var backgroundUrl = FirstValidSteamAppImageUrl(
                    game.AppId,
                    storeItem.BackgroundImageUrl,
                    game.BackgroundImageUrl,
                    fallbackBackgroundUrl,
                    storeItem.Screenshot1Url
                );

                var backgroundLocalPath = await DownloadFirstUpcomingImageAsync(
                    game.AppId,
                    "background",
                    storeItem.BackgroundImageUrl,
                    game.BackgroundImageUrl,
                    fallbackBackgroundUrl,
                    storeItem.Screenshot1Url,
                    headerUrl,
                    capsuleUrl
                );

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

            logger.Info($"[Upcoming] Parsed {uniqueResults.Count} valid items.");

            return uniqueResults;
        }

        private bool ShouldSkip(string title, string block)
        {
            var text = (title + " " + block).ToLowerInvariant();

            return text.Contains("free to play")
                || text.Contains(">free<")
                || text.Contains("discount_final_price free")
                || text.Contains("demo")
                || text.Contains("prologue")
                || text.Contains("soundtrack")
                || text.Contains("adult only")
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