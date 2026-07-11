using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace AnikiHelper.Services
{
    public class SteamStoreRecommendationService
    {
        private const int SteamRecommenderCandidateScanLimit = 256;
        private const int SteamRecommenderDisplayLimit = 18;

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
        private readonly SteamStorePersonalizationService personalizationService;
        private readonly string recommendedCacheFolder;
        private readonly string legacyRecommendedCacheFolder;
        private readonly string legacyRecommendedFolder;
        private readonly string imageCacheFolder;
        private readonly HttpClient httpClient;

        public SteamStoreRecommendationService(
            ILogger logger,
            string pluginUserDataPath,
            SteamStoreService steamStoreService,
            SteamStorePersonalizationService personalizationService)
        {
            this.logger = logger;
            this.steamStoreService = steamStoreService;
            this.personalizationService = personalizationService ?? new SteamStorePersonalizationService();

            recommendedCacheFolder = Path.Combine(pluginUserDataPath, "SteamStore", "StoreCache");
            legacyRecommendedCacheFolder = Path.Combine(pluginUserDataPath, "SteamStore", "RecommendedCache");
            legacyRecommendedFolder = Path.Combine(pluginUserDataPath, "SteamStore", "Recommended");
            imageCacheFolder = Path.Combine(pluginUserDataPath, "SteamStore", "ImageCache");
            Directory.CreateDirectory(recommendedCacheFolder);
            Directory.CreateDirectory(imageCacheFolder);

            httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(12)
            };

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }

        public bool IsCacheMissingOrExpired(string profileKey, string language, string region, TimeSpan maxAge)
        {
            var path = GetCachePath(profileKey, language, region);
            if (!File.Exists(path))
            {
                return true;
            }

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age > maxAge)
            {
                return true;
            }

            try
            {
                var items = JsonConvert.DeserializeObject<List<SteamStoreItem>>(File.ReadAllText(path))
                    ?? new List<SteamStoreItem>();
                NormalizeRecommendedCardImages(items);

                var isStrictConnectedSteamSource =
                    (profileKey ?? string.Empty).StartsWith("steam_for_you_", StringComparison.OrdinalIgnoreCase) ||
                    (profileKey ?? string.Empty).StartsWith("steam_recommender_", StringComparison.OrdinalIgnoreCase);

                var visible = items.Where(x => x != null && x.AppId > 0).Take(isStrictConnectedSteamSource ? 20 : 12).ToList();
                if (visible.Count == 0)
                {
                    return true;
                }

                // Connected Steam source is already curated by Steam. Do not invalidate its cache
                // just because it contains future/owned/VR/special rows.
                if (!isStrictConnectedSteamSource && visible.Any(LooksLikeFutureOrComingSoon))
                {
                    DebugLog("[Recommended] Cache contains unreleased/preorder items; refresh required for playable-now filter.");
                    return true;
                }

                var goodCards = visible.Count(x => !string.IsNullOrWhiteSpace(x.StoreCardImage));
                if (visible.Count > 0 && goodCards < Math.Min(8, visible.Count))
                {
                    DebugLog($"[Recommended] Cache has only {goodCards}/{visible.Count} quality card images; refresh required.");
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        public List<SteamStoreItem> LoadFromCacheOnly(string profileKey, string language, string region)
        {
            try
            {
                var path = GetCachePath(profileKey, language, region);
                if (!File.Exists(path))
                {
                    return new List<SteamStoreItem>();
                }

                var items = JsonConvert.DeserializeObject<List<SteamStoreItem>>(File.ReadAllText(path))
                    ?? new List<SteamStoreItem>();

                NormalizeRecommendedCardImages(items);
                return items;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[Recommended] Failed to load AppID recommendation cache.");
                return new List<SteamStoreItem>();
            }
        }

        public async Task<List<SteamStoreItem>> RefreshFromSeedsAsync(
            IEnumerable<SteamStoreRecommendationSeed> seeds,
            SteamStorePersonalizationContext context,
            string profileKey,
            string language,
            string region,
            Action<int> reportProgress = null)
        {
            var rawSeeds = (seeds ?? Enumerable.Empty<SteamStoreRecommendationSeed>())
                .Where(x => x != null && x.AppId > 0)
                .ToList();

            var rejectedSeeds = rawSeeds
                .Where(x => personalizationService.LooksLikeBadRecommendationSeed(x))
                .GroupBy(x => x.AppId)
                .Select(x => x.First())
                .ToList();

            foreach (var rejected in rejectedSeeds)
            {
                DebugLog($"[Recommended] Seed skipped | {rejected.AppId}:{rejected.Name} | source={rejected.Source}");
            }

            var seedList = rawSeeds
                .Where(x => !personalizationService.LooksLikeBadRecommendationSeed(x))
                .GroupBy(x => x.AppId)
                .Select(g => new SteamStoreRecommendationSeed
                {
                    AppId = g.Key,
                    Name = g.Select(x => x.Name).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                    Source = string.Join(" + ", g.Select(x => x.Source).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)),
                    Weight = Math.Max(1, g.Sum(x => Math.Max(1, x.Weight)))
                })
                .OrderByDescending(x => x.Weight)
                .Take(3)
                .ToList();

            if (seedList.Count == 0)
            {
                return new List<SteamStoreItem>();
            }

            reportProgress?.Invoke(15);

            DebugLog($"[Recommended] AppID mode START | seeds={string.Join(", ", seedList.Select(x => x.AppId + ":" + x.Name))} | profile={profileKey}");

            var candidates = new Dictionary<int, RankedCandidate>();
            var seedAppIds = new HashSet<int>(seedList.Select(x => x.AppId));

            var seedIndex = 0;
            foreach (var seed in seedList)
            {
                seedIndex++;

                try
                {
                    var url = BuildMoreLikeThisUrl(seed.AppId, language, region);
                    var html = await httpClient.GetStringAsync(url).ConfigureAwait(false);

                    var parsed = SteamStoreSearchHtmlParser.ParseStoreRows(html, "Steam Recommended For You")
                        .Where(x => x != null && x.AppId > 0)
                        .Take(32)
                        .ToList();

                    DebugLog($"[Recommended] MoreLikeThis seed {seed.AppId} '{seed.Name}' -> parsed={parsed.Count}");

                    var rank = 0;
                    foreach (var item in parsed)
                    {
                        rank++;

                        if (item == null || item.AppId <= 0 || seedAppIds.Contains(item.AppId))
                        {
                            continue;
                        }

                        if (personalizationService.IsOwnedOrInLibrary(item, context) || personalizationService.LooksLikeNonGameContent(item))
                        {
                            continue;
                        }

                        if (!candidates.TryGetValue(item.AppId, out var candidate))
                        {
                            candidate = new RankedCandidate
                            {
                                Item = item,
                                Score = 0,
                                MatchedSeedCount = 0,
                                BestRank = rank
                            };
                            candidates[item.AppId] = candidate;
                        }

                        candidate.MatchedSeedCount++;
                        candidate.BestRank = Math.Min(candidate.BestRank <= 0 ? rank : candidate.BestRank, rank);
                        candidate.Score += Math.Max(20, seed.Weight) + Math.Max(0, 120 - (rank * 4));

                        if (candidate.Item.Tags == null)
                        {
                            candidate.Item.Tags = new List<string>();
                        }

                        var tag = !string.IsNullOrWhiteSpace(seed.Name)
                            ? "Similar to " + seed.Name
                            : "Similar to " + seed.AppId;

                        if (!candidate.Item.Tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)))
                        {
                            candidate.Item.Tags.Add(tag);
                        }
                    }

                    var progress = 15 + (int)Math.Round((seedIndex / (double)Math.Max(1, seedList.Count)) * 35);
                    reportProgress?.Invoke(Math.Min(50, progress));
                }
                catch (Exception exSeed)
                {
                    logger?.Warn(exSeed, $"[Recommended] MoreLikeThis failed for seed AppId={seed.AppId}.");
                }
            }

            var ordered = candidates.Values
                .Where(x => x?.Item != null)
                .OrderByDescending(x => x.MatchedSeedCount)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.BestRank <= 0 ? int.MaxValue : x.BestRank)
                .Take(28)
                .ToList();

            reportProgress?.Invoke(55);

            if (ordered.Count > 0 && steamStoreService != null)
            {
                var semaphore = new SemaphoreSlim(4);
                var done = 0;

                var enrichTasks = ordered.Select(async candidate =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await steamStoreService.EnrichStoreItemDetailsAsync(candidate.Item, language, region, downloadMedia: false).ConfigureAwait(false);
                    }
                    catch (Exception exEnrich)
                    {
                        logger?.Warn(exEnrich, $"[Recommended] Failed to enrich recommended AppId={candidate.Item?.AppId}.");
                    }
                    finally
                    {
                        semaphore.Release();
                        var current = Interlocked.Increment(ref done);
                        var progress = 55 + (int)Math.Round((current / (double)Math.Max(1, ordered.Count)) * 30);
                        reportProgress?.Invoke(Math.Min(85, progress));
                    }
                }).ToList();

                await Task.WhenAll(enrichTasks).ConfigureAwait(false);
            }

            var recommendationCandidates = ordered.Select(x =>
            {
                x.Item.RecommendationScore = x.Score + (x.MatchedSeedCount * 100);
                x.Item.Source = "Steam Recommended For You";
                return x.Item;
            })
                .ToList();

            var playableNowCandidates = recommendationCandidates
                .Where(x => !LooksLikeFutureOrComingSoon(x))
                .ToList();

            if (playableNowCandidates.Count >= 6)
            {
                DebugLog($"[Recommended] Playable-now filter ON | kept={playableNowCandidates.Count} | dropped={recommendationCandidates.Count - playableNowCandidates.Count}");
                LogDroppedRecommendedItems(recommendationCandidates.Except(playableNowCandidates).ToList(), "future-or-coming-soon");
                recommendationCandidates = playableNowCandidates;
            }
            else
            {
                DebugLog($"[Recommended] Playable-now filter skipped | kept={playableNowCandidates.Count} would be too low | total={recommendationCandidates.Count}");
            }

            var qualityCandidates = recommendationCandidates
                .Where(x => !LooksLikeAdultDatingOrLowQualityRecommended(x))
                .ToList();

            if (qualityCandidates.Count >= 8)
            {
                DebugLog($"[Recommended] Content quality filter ON | kept={qualityCandidates.Count} | dropped={recommendationCandidates.Count - qualityCandidates.Count}");
                LogDroppedRecommendedItems(recommendationCandidates.Except(qualityCandidates).ToList(), "dating-adult-low-quality");
                recommendationCandidates = qualityCandidates;
            }
            else
            {
                DebugLog($"[Recommended] Content quality filter skipped | kept={qualityCandidates.Count} would be too low | total={recommendationCandidates.Count}");
            }

            var filtered = personalizationService.FilterSection(
                    recommendationCandidates,
                    context,
                    "Recommended",
                    24)
                .OrderByDescending(x => x.RecommendationScore)
                .ThenBy(x => x.SteamRank <= 0 ? int.MaxValue : x.SteamRank)
                .Take(12)
                .ToList();

            await CacheListImagesAsync(filtered).ConfigureAwait(false);

            SaveCache(profileKey, language, region, filtered);

            var displayRank = 1;
            foreach (var item in filtered)
            {
                DebugLog($"[Recommended] AppID Result #{displayRank} | score={item.RecommendationScore} | {item.Name} | AppId={item.AppId}");
                displayRank++;
            }

            reportProgress?.Invoke(95);
            return filtered;
        }




        public async Task<List<SteamStoreItem>> RefreshFromSteamRecommendedHtmlAsync(
            string html,
            SteamStorePersonalizationContext context,
            string profileKey,
            string language,
            string region,
            Action<int> reportProgress = null,
            ISet<int> wishlistAppIds = null)
        {
            var parsed = SteamStoreSearchHtmlParser.ParseStoreRows(html, "Steam Recommender")
                .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.AppId)
                .Select(g => g.First())
                .Take(SteamRecommenderCandidateScanLimit)
                .ToList();

            ApplyWishlistFlagsFromWishlistSetOrRecommendedHtml(html, parsed, wishlistAppIds);

            var wishlistSource = wishlistAppIds != null && wishlistAppIds.Count > 0 ? "wishlistdata" : "recommended-html";
            DebugLog($"[Steam Recommender] parsed={parsed.Count} | wishlist={parsed.Count(x => x.IsInWishlist)} | wishlistSource={wishlistSource} | wishlistKnown={wishlistAppIds?.Count ?? 0}");
            reportProgress?.Invoke(25);

            // Connected Steam Recommender uses Steam Interactive Recommender only.
            // Do not merge with /recommended/: that page includes friends/contact rows.
            // Final display order is popularity-based, with no VR/app/name blacklist.
            var candidates = parsed
                .Take(SteamRecommenderCandidateScanLimit)
                .ToList();

            DebugLog($"[Steam Recommender] candidates from Steam order={candidates.Count} | scanLimit={SteamRecommenderCandidateScanLimit} | popularitySort=enabled");

            if (candidates.Count == 0)
            {
                SaveCache(profileKey, language, region, new List<SteamStoreItem>());
                return new List<SteamStoreItem>();
            }

            if (steamStoreService != null)
            {
                var semaphore = new SemaphoreSlim(4);
                var done = 0;

                var enrichLimit = Math.Min(SteamRecommenderCandidateScanLimit, candidates.Count);
                var enrichTasks = candidates.Take(enrichLimit).Select(async item =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await steamStoreService.EnrichStoreItemDetailsAsync(item, language, region, downloadMedia: false).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, $"[Steam Recommender] Enrich failed for AppId={item?.AppId}");
                    }
                    finally
                    {
                        semaphore.Release();
                        var current = Interlocked.Increment(ref done);
                        var progress = 25 + (int)Math.Round((current / (double)Math.Max(1, enrichLimit)) * 45);
                        reportProgress?.Invoke(Math.Min(70, progress));
                    }
                }).ToList();

                await Task.WhenAll(enrichTasks).ConfigureAwait(false);
            }

            var rank = 0;
            foreach (var item in candidates)
            {
                rank++;
                item.Source = "Steam Recommender";
                item.RecommendationScore = 900000 - (rank * 1000);
                if (item.SteamRank <= 0)
                {
                    item.SteamRank = rank;
                }
            }

            var playableCandidates = candidates
                .Where(x => !LooksLikeBlockedSteamRecommenderItem(x, context))
                .ToList();

            if (playableCandidates.Count != candidates.Count)
            {
                DebugLog($"[Steam Recommender] library/non-game filter ON | kept={playableCandidates.Count} | dropped={candidates.Count - playableCandidates.Count}");
                LogDroppedRecommendedItems(candidates.Except(playableCandidates).ToList(), "owned-played-or-non-game");
                candidates = playableCandidates;
            }
            else
            {
                DebugLog($"[Steam Recommender] library/non-game filter OK | kept={candidates.Count} | dropped=0");
            }

            var adultSafeCandidates = candidates
                .Where(x => !LooksLikeExplicitAdultContent(x))
                .ToList();

            if (adultSafeCandidates.Count != candidates.Count)
            {
                DebugLog($"[Steam Recommender] explicit adult filter ON | kept={adultSafeCandidates.Count} | dropped={candidates.Count - adultSafeCandidates.Count}");
                LogDroppedRecommendedItems(candidates.Except(adultSafeCandidates).ToList(), "explicit-adult-content");
                candidates = adultSafeCandidates;
            }
            else
            {
                DebugLog($"[Steam Recommender] explicit adult filter OK | kept={candidates.Count} | dropped=0");
            }

            // Important: connected Steam Recommender is already personalized by Steam.
            // Do not blacklist VR or specific game names.
            // We only remove items already owned/played/in library, obvious non-game content,
            // explicit adult/NSFW content, then reorder Steam's own candidates by popularity.
            var ranked = candidates
                .Select((item, index) => new
                {
                    Item = item,
                    OriginalIndex = index,
                    PopularityScore = GetSteamForYouPopularityScore(item)
                })
                .OrderByDescending(x => x.PopularityScore)
                .ThenBy(x => x.Item.SteamRank <= 0 ? int.MaxValue : x.Item.SteamRank)
                .ThenBy(x => x.OriginalIndex)
                .ToList();

            DebugLog($"[Steam Recommender] popularity sorted mode | candidates={candidates.Count} | kept={Math.Min(SteamRecommenderDisplayLimit, ranked.Count)} | utilityFilter=on | ownedPlayedFilter=on | adultFilter=explicit-only");

            var filtered = ranked
                .Take(SteamRecommenderDisplayLimit)
                .Select((x, index) =>
                {
                    // RecommendationScore is now the UI score after popularity sorting.
                    // Keep it high enough so Recommended hero uses this section correctly.
                    x.Item.RecommendationScore = 900000 - (index * 1000);
                    return x.Item;
                })
                .ToList();

            reportProgress?.Invoke(75);

            // If strict cleaning leaves the primary Recommender under the display target, do not
            // save/cache a partial list here. The caller will try /recommended/ as a supplement
            // and that final merged list will be the only one cached for the UI.
            if (filtered.Count < SteamRecommenderDisplayLimit)
            {
                DebugLog($"[Steam Recommender] primary clean result below target | count={filtered.Count} | target={SteamRecommenderDisplayLimit} | cacheSave=deferred-to-supplement");
                return filtered;
            }

            await CacheListImagesAsync(filtered).ConfigureAwait(false);
            reportProgress?.Invoke(90);

            SaveCache(profileKey, language, region, filtered);

            var displayRank = 1;
            foreach (var item in filtered)
            {
                DebugLog($"[Steam Recommender] Result #{displayRank} | score={item.RecommendationScore} | popularity={GetSteamForYouPopularityScore(item)} | reviews={item.RecommendationsTotal} | wishlist={item.IsInWishlist} | {item.Name} | AppId={item.AppId}");
                displayRank++;
            }

            reportProgress?.Invoke(95);
            return filtered;
        }



        public async Task<List<SteamStoreItem>> FillFromSteamRecommendedFeedHtmlAsync(
            string html,
            List<SteamStoreItem> existingItems,
            SteamStorePersonalizationContext context,
            string profileKey,
            string language,
            string region,
            Action<int> reportProgress = null,
            ISet<int> wishlistAppIds = null)
        {
            var existing = existingItems ?? new List<SteamStoreItem>();
            var existingIds = new HashSet<int>(existing.Where(x => x != null && x.AppId > 0).Select(x => x.AppId));

            if (existing.Count >= SteamRecommenderDisplayLimit)
            {
                DebugLog($"[Steam Recommended Supplement] skipped | existing={existing.Count} | target={SteamRecommenderDisplayLimit}");
                return existing.Take(SteamRecommenderDisplayLimit).ToList();
            }

            var parsed = SteamStoreSearchHtmlParser.ParseStoreRows(html, "Steam Recommended Supplement")
                .Where(x => x != null && x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.AppId)
                .Select(g => g.First())
                .Where(x => !existingIds.Contains(x.AppId))
                .Take(SteamRecommenderCandidateScanLimit)
                .ToList();

            ApplyWishlistFlagsFromWishlistSetOrRecommendedHtml(html, parsed, wishlistAppIds);
            DebugLog($"[Steam Recommended Supplement] parsed={parsed.Count} | existing={existing.Count} | target={SteamRecommenderDisplayLimit}");
            reportProgress?.Invoke(76);

            if (parsed.Count == 0)
            {
                SaveCache(profileKey, language, region, existing.Take(SteamRecommenderDisplayLimit).ToList());
                return existing.Take(SteamRecommenderDisplayLimit).ToList();
            }

            if (steamStoreService != null)
            {
                var semaphore = new SemaphoreSlim(4);
                var done = 0;
                var enrichTasks = parsed.Select(async item =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await steamStoreService.EnrichStoreItemDetailsAsync(item, language, region, downloadMedia: false).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, $"[Steam Recommended Supplement] Enrich failed for AppId={item?.AppId}");
                    }
                    finally
                    {
                        semaphore.Release();
                        var current = Interlocked.Increment(ref done);
                        var progress = 76 + (int)Math.Round((current / (double)Math.Max(1, parsed.Count)) * 9);
                        reportProgress?.Invoke(Math.Min(85, progress));
                    }
                }).ToList();

                await Task.WhenAll(enrichTasks).ConfigureAwait(false);
            }

            var rank = existing.Count;
            foreach (var item in parsed)
            {
                rank++;
                item.Source = "Steam Recommended Supplement";
                item.RecommendationScore = 500000 - (rank * 1000);
                if (item.SteamRank <= 0)
                {
                    item.SteamRank = rank;
                }
            }

            var playable = parsed.Where(x => !LooksLikeBlockedSteamRecommenderItem(x, context)).ToList();
            if (playable.Count != parsed.Count)
            {
                DebugLog($"[Steam Recommended Supplement] library/non-game filter ON | kept={playable.Count} | dropped={parsed.Count - playable.Count}");
                LogDroppedRecommendedItems(parsed.Except(playable).ToList(), "supplement-owned-played-or-non-game");
            }
            else
            {
                DebugLog($"[Steam Recommended Supplement] library/non-game filter OK | kept={playable.Count} | dropped=0");
            }

            var adultSafe = playable.Where(x => !LooksLikeExplicitAdultContent(x)).ToList();
            if (adultSafe.Count != playable.Count)
            {
                DebugLog($"[Steam Recommended Supplement] explicit adult filter ON | kept={adultSafe.Count} | dropped={playable.Count - adultSafe.Count}");
                LogDroppedRecommendedItems(playable.Except(adultSafe).ToList(), "supplement-explicit-adult-content");
            }
            else
            {
                DebugLog($"[Steam Recommended Supplement] explicit adult filter OK | kept={adultSafe.Count} | dropped=0");
            }

            var qualitySafe = adultSafe.Where(x => !LooksLikeLowQualityRecommendedSupplementItem(x)).ToList();
            if (qualitySafe.Count != adultSafe.Count)
            {
                DebugLog($"[Steam Recommended Supplement] quality/popularity filter ON | kept={qualitySafe.Count} | dropped={adultSafe.Count - qualitySafe.Count} | minReviews=1000 | wishlist/future=allowed");
                LogDroppedRecommendedItems(adultSafe.Except(qualitySafe).ToList(), "supplement-low-quality-friends-or-contact");
            }
            else
            {
                DebugLog($"[Steam Recommended Supplement] quality/popularity filter OK | kept={qualitySafe.Count} | dropped=0");
            }

            var needed = Math.Max(0, SteamRecommenderDisplayLimit - existing.Count);
            var supplement = qualitySafe
                .Select((item, index) => new
                {
                    Item = item,
                    OriginalIndex = index,
                    PopularityScore = GetSteamForYouPopularityScore(item)
                })
                .OrderByDescending(x => x.PopularityScore)
                .ThenBy(x => x.Item.SteamRank <= 0 ? int.MaxValue : x.Item.SteamRank)
                .ThenBy(x => x.OriginalIndex)
                .Take(needed)
                .Select(x => x.Item)
                .ToList();

            // Merge primary + supplement and sort the final display by the same popularity logic.
            // This avoids keeping high-quality supplement games artificially below very low-popularity
            // Recommender rows, while still never filling missing slots with friend/contact noise.
            var final = existing.Concat(supplement)
                .Where(x => x != null && x.AppId > 0)
                .GroupBy(x => x.AppId)
                .Select(g => g.First())
                .Select((item, index) => new
                {
                    Item = item,
                    OriginalIndex = index,
                    PopularityScore = GetSteamForYouPopularityScore(item)
                })
                .OrderByDescending(x => x.PopularityScore)
                .ThenBy(x => x.Item.Source == "Steam Recommender" ? 0 : 1)
                .ThenBy(x => x.Item.SteamRank <= 0 ? int.MaxValue : x.Item.SteamRank)
                .ThenBy(x => x.OriginalIndex)
                .Take(SteamRecommenderDisplayLimit)
                .Select(x => x.Item)
                .ToList();

            for (var i = 0; i < final.Count; i++)
            {
                final[i].RecommendationScore = 900000 - (i * 1000);
            }

            DebugLog($"[Steam Recommended Supplement] fill result | primary={existing.Count} | supplementParsed={parsed.Count} | supplementUsable={adultSafe.Count} | supplementQuality={qualitySafe.Count} | added={supplement.Count} | final={final.Count} | target={SteamRecommenderDisplayLimit}");

            reportProgress?.Invoke(88);
            await CacheListImagesAsync(final).ConfigureAwait(false);
            reportProgress?.Invoke(92);
            SaveCache(profileKey, language, region, final);

            var displayRank = 1;
            foreach (var item in final)
            {
                DebugLog($"[Steam Recommender] Final Result #{displayRank} | score={item.RecommendationScore} | popularity={GetSteamForYouPopularityScore(item)} | reviews={item.RecommendationsTotal} | source={item.Source} | wishlist={item.IsInWishlist} | {item.Name} | AppId={item.AppId}");
                displayRank++;
            }

            reportProgress?.Invoke(95);
            return final;
        }


        private bool LooksLikeBlockedSteamRecommenderItem(SteamStoreItem item, SteamStorePersonalizationContext context)
        {
            if (item == null)
            {
                return true;
            }

            // Remove real non-game Steam applications like Wallpaper Engine/software/hardware/DLC.
            // Do not remove VR games: VR is not a blocked app type here.
            if (IsNonGameSteamAppType(item.AppType))
            {
                return true;
            }

            // Steam Recommender can expose entries whose appdetails are unavailable. Those end up
            // as "Steam App 123456" and often create black/404 images in the UI.
            if (LooksLikePlaceholderSteamApp(item))
            {
                return true;
            }

            // Keep this as a content-type filter, not a game-name blacklist. It catches obvious
            // utilities/benchmarks/trainers such as 3DMark, PCMark, VRMark, Steam Link, etc.
            if (LooksLikeUtilityBenchmarkOrTool(item))
            {
                return true;
            }

            // Remove games already present in Playnite / Steam owned cache / Steam Family imports.
            if (personalizationService != null && personalizationService.IsOwnedOrInLibrary(item, context))
            {
                return true;
            }

            // Extra safety for items with playtime when the owned AppId/name mapping was incomplete.
            if (personalizationService != null && personalizationService.IsPlayedInLibrary(item, context))
            {
                return true;
            }

            // Keep this narrow. It catches obvious non-game/store filler like wallpaper, demos, DLC,
            // soundtrack, artbook, server tools, etc. It does not block VR as a category.
            if (personalizationService != null && personalizationService.LooksLikeNonGameContent(item))
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikePlaceholderSteamApp(SteamStoreItem item)
        {
            if (item == null || item.AppId <= 0)
            {
                return true;
            }

            var name = (item.Name ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(name) ||
                   name.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase);
        }

        private bool LooksLikeUtilityBenchmarkOrTool(SteamStoreItem item)
        {
            if (item == null)
            {
                return true;
            }

            var haystack = BuildRecommendedQualityText(item);
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            // This is intentionally NOT a VR filter. It targets applications/utilities that are
            // not normal games and can appear in Steam Recommender, especially benchmark apps.
            return haystack.Contains("3dmark") ||
                   haystack.Contains("pcmark") ||
                   haystack.Contains("vrmark") ||
                   haystack.Contains("benchmark") ||
                   haystack.Contains("benchmarking") ||
                   haystack.Contains("system information") ||
                   haystack.Contains("hardware monitoring") ||
                   haystack.Contains("performance test") ||
                   haystack.Contains("performance testing") ||
                   haystack.Contains("utility") ||
                   haystack.Contains("utilities") ||
                   haystack.Contains("software") ||
                   haystack.Contains("tool") ||
                   haystack.Contains("tools") ||
                   haystack.Contains("trainer") ||
                   haystack.Contains("aimlabs") ||
                   haystack.Contains("aim lab") ||
                   haystack.Contains("aim trainer") ||
                   haystack.Contains("aim training") ||
                   haystack.Contains("aim practice") ||
                   haystack.Contains("fps trainer") ||
                   haystack.Contains("fps training") ||
                   haystack.Contains("wallpaper") ||
                   haystack.Contains("steam controller") ||
                   haystack.Contains("steam deck") ||
                   haystack.Contains("steam link") ||
                   haystack.Contains("source sdk") ||
                   haystack.Contains("dedicated server");
        }

        private bool LooksLikeLowQualityRecommendedSupplementItem(SteamStoreItem item)
        {
            if (item == null)
            {
                return true;
            }

            // /recommended/ is only a fallback supplement. It can include friends/contact blocks.
            // Keep only supplement rows that have a solid Steam popularity signal, unless Steam
            // explicitly marks them as wishlist/future/preorder candidates. This removes rows like
            // VR friend suggestions, StarSavior, Yu-Gi-Oh, etc. without adding a VR blacklist.
            if (item.IsInWishlist || item.ComingSoon || item.IsPreorder)
            {
                return false;
            }

            if (item.RecommendationsTotal >= 1000)
            {
                return false;
            }

            if (item.MetacriticScore >= 70)
            {
                return false;
            }

            return true;
        }

        private bool LooksLikeExplicitAdultContent(SteamStoreItem item)
        {
            if (item == null)
            {
                return true;
            }

            var haystack = BuildRecommendedQualityText(item);
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            // Strict adult filter only: do not remove violent/mature games like Cyberpunk,
            // RDR2, Sekiro, etc. This only targets explicit sexual/NSFW signals.
            return haystack.Contains("adult only") ||
                   haystack.Contains("adult-only") ||
                   haystack.Contains("adults only") ||
                   haystack.Contains("sexual content") ||
                   haystack.Contains("strong sexual content") ||
                   haystack.Contains("explicit sexual") ||
                   haystack.Contains("nudity") ||
                   haystack.Contains("hentai") ||
                   haystack.Contains("erotic") ||
                   haystack.Contains("nsfw") ||
                   haystack.Contains("porn") ||
                   haystack.Contains("pornography") ||
                   haystack.Contains("18+") ||
                   haystack.Contains("sexuel") ||
                   haystack.Contains("contenu sexuel") ||
                   haystack.Contains("contenu a caractere sexuel") ||
                   haystack.Contains("contenu à caractère sexuel") ||
                   haystack.Contains("nudité") ||
                   haystack.Contains("nudite");
        }

        private static long GetSteamForYouPopularityScore(SteamStoreItem item)
        {
            if (item == null)
            {
                return long.MinValue;
            }

            long score = 0;

            // Main popularity signal from Steam appdetails: number of recommendations/reviews.
            // Clamp to avoid one huge F2P title completely destroying every other signal.
            if (item.RecommendationsTotal > 0)
            {
                score += Math.Min(item.RecommendationsTotal, 5000000) * 1000L;
            }

            if (item.MetacriticScore > 0)
            {
                score += item.MetacriticScore * 10000L;
            }

            if (item.DiscountPercent > 0)
            {
                score += item.DiscountPercent * 100L;
            }

            if (!string.IsNullOrWhiteSpace(item.FinalPriceDisplay))
            {
                score += 250L;
            }

            if (!string.IsNullOrWhiteSpace(item.HeaderImageUrl) || !string.IsNullOrWhiteSpace(item.CapsuleImageUrl))
            {
                score += 100L;
            }

            // Fallback only: when appdetails has no popularity data, preserve Steam's own order.
            if (item.SteamRank > 0)
            {
                score += Math.Max(0, 1000 - item.SteamRank);
            }

            return score;
        }



        private void ApplyWishlistFlagsFromWishlistSetOrRecommendedHtml(string html, IEnumerable<SteamStoreItem> items, ISet<int> wishlistAppIds)
        {
            var list = items == null ? new List<SteamStoreItem>() : items.Where(x => x != null && x.AppId > 0).ToList();
            if (list.Count == 0)
            {
                return;
            }

            if (wishlistAppIds != null && wishlistAppIds.Count > 0)
            {
                foreach (var item in list)
                {
                    item.IsInWishlist = wishlistAppIds.Contains(item.AppId);
                }

                return;
            }

            ApplyWishlistFlagsFromRecommendedHtml(html, list);
        }

        private void ApplyWishlistFlagsFromRecommendedHtml(string html, IEnumerable<SteamStoreItem> items)
        {
            if (string.IsNullOrWhiteSpace(html) || items == null)
            {
                return;
            }

            foreach (var item in items.Where(x => x != null && x.AppId > 0))
            {
                item.IsInWishlist = LooksWishlistedInRecommendedHtml(html, item.AppId);
            }
        }

        private static bool LooksWishlistedInRecommendedHtml(string html, int appId)
        {
            if (string.IsNullOrWhiteSpace(html) || appId <= 0)
            {
                return false;
            }

            var appIdText = appId.ToString();
            var searchIndex = 0;

            while (searchIndex < html.Length)
            {
                var index = html.IndexOf(appIdText, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                searchIndex = index + appIdText.Length;

                if (!LooksLikeSameAppLinkContext(html, index, appIdText))
                {
                    continue;
                }

                var block = GetHtmlContext(html, index, 2500, 3500);
                if (BlockContainsWishlistMarker(block))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeSameAppLinkContext(string html, int index, string appIdText)
        {
            var context = GetHtmlContext(html, index, 140, 180).ToLowerInvariant();
            var appPath1 = "/app/" + appIdText;
            var appPath2 = "app/" + appIdText;
            var dsAppId1 = "data-ds-appid=\"" + appIdText + "\"";
            var dsAppId2 = "data-ds-appid='" + appIdText + "'";

            return context.IndexOf(appPath1, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   context.IndexOf(appPath2, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   context.IndexOf(dsAppId1, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   context.IndexOf(dsAppId2, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetHtmlContext(string html, int index, int before, int after)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            var start = Math.Max(0, index - Math.Max(0, before));
            var end = Math.Min(html.Length, index + Math.Max(0, after));
            if (end <= start)
            {
                return string.Empty;
            }

            return html.Substring(start, end - start);
        }

        private static bool BlockContainsWishlistMarker(string block)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                return false;
            }

            var value = Regex.Replace(block, "<.*?>", " ");
            value = System.Net.WebUtility.HtmlDecode(value ?? string.Empty);
            value = Regex.Replace(value, @"\s+", " ").Trim().ToLowerInvariant();

            return value.Contains("dans la liste de souhait") ||
                   value.Contains("on your wishlist") ||
                   value.Contains("in your wishlist") ||
                   value.Contains("in wishlist") ||
                   value.Contains("wishlisted");
        }

        private bool LooksLikeUnsupportedSteamForYouItem(SteamStoreItem item, SteamStorePersonalizationContext context)
        {
            if (item == null)
            {
                return true;
            }

            if (IsNonGameSteamAppType(item.AppType))
            {
                return true;
            }

            // Do not call IsOwnedOrInLibrary or broad LooksLikeNonGameContent here.
            // This section is meant to reproduce Steam's connected /recommended/ list, and Steam may
            // legitimately include already-owned/played games, future games, or special recommender rows.
            // We only remove obvious non-game tiles and app types.

            var name = (item.Name ?? string.Empty).Trim();
            var nameLower = name.ToLowerInvariant();

            // No name blacklist here. If Steam Recommender returns a real VR game, keep it.

            return false;
        }

        private static bool IsNonGameSteamAppType(string appType)
        {
            if (string.IsNullOrWhiteSpace(appType))
            {
                return false;
            }

            var type = appType.Trim().ToLowerInvariant();

            return type == "dlc" ||
                   type == "demo" ||
                   type == "hardware" ||
                   type == "mod" ||
                   type == "music" ||
                   type == "series" ||
                   type == "software" ||
                   type == "video";
        }

        private bool LooksLikeFutureOrComingSoon(SteamStoreItem item)
        {
            if (item == null)
            {
                return true;
            }

            if (item.ComingSoon || item.IsPreorder)
            {
                return true;
            }

            var release = (item.ReleaseDateDisplay ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(release))
            {
                return false;
            }

            return release.Contains("à déterminer") ||
                   release.Contains("a determiner") ||
                   release.Contains("to be announced") ||
                   release.Contains("tba") ||
                   release.Contains("coming soon") ||
                   release.Contains("prochainement");
        }

        private bool LooksLikeAdultDatingOrLowQualityRecommended(SteamStoreItem item)
        {
            if (item == null)
            {
                return true;
            }

            var haystack = BuildRecommendedQualityText(item);
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            // For You is a premium recommendation row. Keep Store-wide adult/dating/social filler
            // out even if Steam MoreLikeThis returns it for story-rich/narrative seeds.
            return haystack.Contains("date everything") ||
                   haystack.Contains("house party") ||
                   haystack.Contains("dating sim") ||
                   haystack.Contains("dating simulator") ||
                   haystack.Contains("sexual content") ||
                   haystack.Contains("nudity") ||
                   haystack.Contains("hentai") ||
                   haystack.Contains("erotic") ||
                   haystack.Contains("nsfw") ||
                   haystack.Contains("adult only") ||
                   haystack.Contains("adult-only") ||
                   haystack.Contains("porn") ||
                   haystack.Contains("mature content") ||
                   haystack.Contains("sexuel") ||
                   haystack.Contains("nudité") ||
                   haystack.Contains("jeu de drague") ||
                   haystack.Contains("simulation de rencontre");
        }

        private string BuildRecommendedQualityText(SteamStoreItem item)
        {
            var parts = new List<string>();

            void Add(string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }

            Add(item.Name);
            Add(item.AppType);
            Add(item.ShortDescription);
            Add(item.Source);

            if (item.Tags != null)
            {
                parts.AddRange(item.Tags.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            if (item.Genres != null)
            {
                parts.AddRange(item.Genres.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            if (item.Categories != null)
            {
                parts.AddRange(item.Categories.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return string.Join(" | ", parts).ToLowerInvariant();
        }

        private void LogDroppedRecommendedItems(List<SteamStoreItem> dropped, string reason)
        {
            if (dropped == null || dropped.Count == 0)
            {
                return;
            }

            foreach (var item in dropped.Take(20))
            {
                DebugLog($"[Recommended] Dropped | reason={reason} | {item?.Name} | AppId={item?.AppId} | Release={item?.ReleaseDateDisplay} | ComingSoon={item?.ComingSoon} | Preorder={item?.IsPreorder}");
            }
        }

        private async Task CacheListImagesAsync(List<SteamStoreItem> items)
        {
            var list = items?.Where(x => x != null && x.AppId > 0).Take(20).ToList() ?? new List<SteamStoreItem>();
            if (list.Count == 0)
            {
                return;
            }

            var semaphore = new SemaphoreSlim(4);
            var tasks = list.Select(async item =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    item.HeaderImageLocalPath = await DownloadImageAsync(
                        item.AppId,
                        "header",
                        item.HeaderImageUrl,
                        BuildSteamAppImageUrl(item.AppId, "header.jpg"),
                        item.CapsuleImageUrl
                    ).ConfigureAwait(false);

                    item.CapsuleImageLocalPath = await DownloadImageAsync(
                        item.AppId,
                        "capsule_616x353",
                        item.CapsuleImageUrl,
                        BuildSteamAppImageUrl(item.AppId, "capsule_616x353.jpg"),
                        item.HeaderImageUrl
                    ).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(item.HeaderImageUrl))
                    {
                        item.HeaderImageUrl = BuildSteamAppImageUrl(item.AppId, "header.jpg");
                    }

                    if (string.IsNullOrWhiteSpace(item.CapsuleImageUrl))
                    {
                        item.CapsuleImageUrl = BuildSteamAppImageUrl(item.AppId, "capsule_616x353.jpg");
                    }

                    if (string.IsNullOrWhiteSpace(item.BackgroundImageLocalPath))
                    {
                        item.BackgroundImageLocalPath = item.HeaderImageLocalPath;
                    }
                }
                catch
                {
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var withLocalImages = list.Count(x =>
                !string.IsNullOrWhiteSpace(x.HeaderImageLocalPath) ||
                !string.IsNullOrWhiteSpace(x.CapsuleImageLocalPath));
            var withCardImage = list.Count(x => !string.IsNullOrWhiteSpace(x.StoreCardImage));
            DebugLog($"[Recommended] Image cache | requested={list.Count} | local={withLocalImages} | card={withCardImage}");
        }


        private void NormalizeRecommendedCardImages(List<SteamStoreItem> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (!IsGoodHeaderOrBannerLocalImage(item.HeaderImageLocalPath))
                {
                    item.HeaderImageLocalPath = string.Empty;
                }

                if (!IsGoodWideLocalImage(item.CapsuleImageLocalPath))
                {
                    item.CapsuleImageLocalPath = string.Empty;
                }
            }
        }

        private async Task<string> DownloadImageAsync(int appId, string suffix, params string[] urls)
        {
            if (appId <= 0 || urls == null)
            {
                return string.Empty;
            }

            var wantHeader = string.Equals(suffix, "header", StringComparison.OrdinalIgnoreCase);
            var wantWide = string.Equals(suffix, "capsule_616x353", StringComparison.OrdinalIgnoreCase);

            foreach (var url in urls)
            {
                if (!IsValidSteamAppImageUrl(appId, url) || IsLowQualitySteamSearchImage(url))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(imageCacheFolder);
                    var safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "image" : suffix;
                    var path = Path.Combine(imageCacheFolder, $"recommended_{appId}_{safeSuffix}.jpg");
                    var markerPath = path + ".source.txt";
                    var existingIsUsable = IsUsableLocalImageFile(path) &&
                        (!wantHeader || IsGoodHeaderOrBannerLocalImage(path)) &&
                        (!wantWide || IsGoodWideLocalImage(path));

                    if (existingIsUsable && File.Exists(markerPath))
                    {
                        var marker = File.ReadAllText(markerPath);
                        if (string.Equals(marker, url, StringComparison.OrdinalIgnoreCase))
                        {
                            return path;
                        }
                    }

                    var bytes = await httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
                    if (bytes == null || bytes.Length <= 1024)
                    {
                        if (existingIsUsable)
                        {
                            return path;
                        }

                        continue;
                    }

                    var tmpPath = path + ".tmp";
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

                    if (!IsUsableLocalImageFile(tmpPath) ||
                        (wantHeader && !IsGoodHeaderOrBannerLocalImage(tmpPath)) ||
                        (wantWide && !IsGoodWideLocalImage(tmpPath)))
                    {
                        try { File.Delete(tmpPath); } catch { }
                        if (existingIsUsable)
                        {
                            return path;
                        }

                        continue;
                    }

                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }

                        if (File.Exists(markerPath))
                        {
                            File.Delete(markerPath);
                        }
                    }
                    catch
                    {
                    }

                    File.Move(tmpPath, path);
                    File.WriteAllText(markerPath, url);
                    return path;
                }
                catch
                {
                }
            }

            return string.Empty;
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

        private static bool IsUsableLocalImageFile(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 1024;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetImageDimensions(string path, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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

        private string BuildMoreLikeThisUrl(int appId, string language, string region)
        {
            var safeLanguage = Uri.EscapeDataString(string.IsNullOrWhiteSpace(language) ? "english" : language.Trim());
            var safeRegion = Uri.EscapeDataString(string.IsNullOrWhiteSpace(region) ? "US" : region.Trim().ToUpperInvariant());
            return $"https://store.steampowered.com/recommended/morelike/app/{appId}/?l={safeLanguage}&cc={safeRegion}&os=win";
        }

        private string GetCachePath(string profileKey, string language, string region)
        {
            var safeLanguage = SanitizeCachePart(language);
            var safeRegion = SanitizeCachePart(region);
            var targetPath = Path.Combine(recommendedCacheFolder, $"steam_recommender_{safeLanguage}_{safeRegion}.json");
            TryMigrateLegacyRecommendedCache(profileKey, safeLanguage, safeRegion, targetPath);
            return targetPath;
        }

        private void TryMigrateLegacyRecommendedCache(string profileKey, string safeLanguage, string safeRegion, string targetPath)
        {
            try
            {
                if (File.Exists(targetPath))
                {
                    return;
                }

                var safeProfile = SanitizeCachePart(profileKey);
                var legacyFolders = new[] { legacyRecommendedCacheFolder, legacyRecommendedFolder }
                    .Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x))
                    .ToList();

                var candidates = new List<string>();
                foreach (var folder in legacyFolders)
                {
                    var exact = Path.Combine(folder, $"recommended_appid_{safeProfile}_{safeLanguage}_{safeRegion}.json");
                    if (File.Exists(exact))
                    {
                        candidates.Add(exact);
                    }

                    candidates.AddRange(Directory.GetFiles(folder, $"recommended_appid_*_{safeLanguage}_{safeRegion}.json"));
                }

                var sourcePath = candidates
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return;
                }

                File.Copy(sourcePath, targetPath, false);
                File.Delete(sourcePath);
                DebugLog($"[Recommended] Cache migrated -> {targetPath}");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[Recommended] Legacy cache migration failed.");
            }
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

            return result.Replace(" ", "_");
        }

        private void SaveCache(string profileKey, string language, string region, List<SteamStoreItem> items)
        {
            try
            {
                var path = GetCachePath(profileKey, language, region);
                File.WriteAllText(path, JsonConvert.SerializeObject(items ?? new List<SteamStoreItem>(), Formatting.Indented));
                DebugLog($"[Recommended] AppID cache saved: {(items?.Count ?? 0)} games -> {path}");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[Recommended] Failed to save AppID recommendation cache.");
            }
        }

        private sealed class RankedCandidate
        {
            public SteamStoreItem Item { get; set; }
            public int Score { get; set; }
            public int MatchedSeedCount { get; set; }
            public int BestRank { get; set; }
        }
    }
}
