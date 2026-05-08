using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AnikiHelper
{
    public class SteamUpdateLiteResult
    {
        public string Title { get; set; }
        public DateTime Published { get; set; } // UTC
        public string HtmlBody { get; set; }
    }

    public class SteamUpdateLiteService
    {
        private const string FeedTemplate = "https://store.steampowered.com/feeds/news/app/{0}/?l={1}";
        private readonly string steamLanguage;

        // ✅ HttpClient partagé (évite de recréer/lag + support CancelToken)
        private static readonly HttpClient http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.ParseAdd("text/xml");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SteamUpdateLite");
            client.Timeout = TimeSpan.FromSeconds(20); // sécurité
            return client;
        }

        private static string ExtractFirstImageUrl(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var decoded = WebUtility.HtmlDecode(html);

            var imgMatch = Regex.Match(
                decoded,
                "<img[^>]+src=[\"']([^\"']+)[\"']",
                RegexOptions.IgnoreCase);

            if (imgMatch.Success)
                return WebUtility.HtmlDecode(imgMatch.Groups[1].Value);

            var directImageMatch = Regex.Match(
                decoded,
                @"https?://[^\s""'<>]+?\.(jpg|jpeg|png|webp|gif)(\?[^\s""'<>]*)?",
                RegexOptions.IgnoreCase);

            if (directImageMatch.Success)
                return WebUtility.HtmlDecode(directImageMatch.Value);

            return string.Empty;
        }

        // PUBLIC: retrieves the latest update (Update/Patch Notes)
        public async Task<SteamUpdateLiteResult> GetLatestUpdateAsync(string steamId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(steamId))
                return null;

            // 1) We try in the Playnite language
            var result = await GetLatestUpdateForLanguageAsync(steamId, steamLanguage, ct).ConfigureAwait(false);
            if (result != null)
                return result;

            // 2) English fallback if Playnite ≠ English
            if (!string.Equals(steamLanguage, "english", StringComparison.OrdinalIgnoreCase))
            {
                return await GetLatestUpdateForLanguageAsync(steamId, "english", ct).ConfigureAwait(false);
            }

            return null;
        }

        // Backward compatibility (si tu as encore des appels sans ct quelque part)
        public Task<SteamUpdateLiteResult> GetLatestUpdateAsync(string steamId)
            => GetLatestUpdateAsync(steamId, CancellationToken.None);

        public async Task<List<SteamGameNewsItem>> GetLatestNewsAsync(string steamId, int count, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(steamId))
                return new List<SteamGameNewsItem>();

            var result = await GetLatestNewsForLanguageAsync(steamId, steamLanguage, count, ct)
                .ConfigureAwait(false);

            if (result != null && result.Count > 0)
                return result;

            if (!string.Equals(steamLanguage, "english", StringComparison.OrdinalIgnoreCase))
            {
                return await GetLatestNewsForLanguageAsync(steamId, "english", count, ct)
                    .ConfigureAwait(false);
            }

            return new List<SteamGameNewsItem>();
        }

        private async Task<SteamUpdateLiteResult> GetLatestUpdateForLanguageAsync(string steamId, string language, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var url = string.Format(FeedTemplate, steamId, language);

                // ✅ Annulable
                using (var resp = await http.GetAsync(url, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                        return null;

                    var xml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(xml))
                        return null;

                    var doc = XDocument.Parse(xml);

                    var items = doc.Descendants("item").Select(x => new
                    {
                        Title = FixSteamEncoding(WebUtility.HtmlDecode((string)x.Element("title") ?? "")),
                        Desc = FixSteamEncoding(WebUtility.HtmlDecode((string)x.Element("description") ?? "")),
                        Pub = (string)x.Element("pubDate") ?? ""
                    }).ToList();

                    if (items.Count == 0)
                        return null;

                    DateTime ParseDateUtc(string raw)
                    {
                        // Steam RSS = RFC1123, souvent déjà UTC.
                        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var dt))
                        {
                            return dt.ToUniversalTime();
                        }

                        return DateTime.MinValue;
                    }

                    bool LooksLikeUpdate(string title)
                    {
                        if (string.IsNullOrWhiteSpace(title))
                            return false;

                        var t = title.ToLowerInvariant();

                        string[] keywords =
                        {
                            // EN
                            "patch", "patch notes",
                            "hotfix", "hot fix",
                            "changelog", "change log",
                            "update", "title update", "technical update",
                            "major update", "minor update",
                            "balance update",
                            "bugfix", "bug fix", "bug fixes", "bug fixed",
                            "release notes",

                            // FR
                            "mise à jour", "mise a jour",
                            "avis de maintenance",
                            "maintenance et de correctif",
                            "maintenance",
                            "correctif", "correctifs"
                        };

                        if (keywords.Any(k => t.Contains(k)))
                            return true;

                        // v1.2 / v1.2.3 / v. 1.2 etc.
                        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\bv\.?\s*\d+(\.\d+){1,3}\b"))
                            return true;

                        // 1.2 / 1.2.3 / 0.9.0.1 etc.
                        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\b\d+(\.\d+){1,3}\b"))
                            return true;

                        // Patch #10
                        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"patch\s*#\s*\d+"))
                            return true;

                        // Hotfix 12
                        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"hotfix\s*\d*"))
                            return true;

                        return false;
                    }

                    var candidate = items
                        .Where(i => LooksLikeUpdate(i.Title))
                        .OrderByDescending(i => ParseDateUtc(i.Pub))
                        .FirstOrDefault();

                    if (candidate == null)
                        return null;

                    return new SteamUpdateLiteResult
                    {
                        Title = candidate.Title,
                        Published = ParseDateUtc(candidate.Pub), // ✅ UTC
                        HtmlBody = candidate.Desc
                    };
                }
            }
            catch (OperationCanceledException)
            {
                // ✅ Annulation propre
                throw;
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<SteamGameNewsItem>> GetLatestNewsForLanguageAsync(
            string steamId,
            string language,
            int count,
            CancellationToken ct)
              {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var url = string.Format(FeedTemplate, steamId, language);

                    using (var resp = await http.GetAsync(url, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                            return new List<SteamGameNewsItem>();

                        var xml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(xml))
                            return new List<SteamGameNewsItem>();

                        var doc = XDocument.Parse(xml);

                        DateTime ParseDateUtc(string raw)
                        {
                            if (DateTime.TryParse(
                                raw,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var dt))
                            {
                                return dt.ToUniversalTime();
                            }

                            return DateTime.MinValue;
                        }

                        return doc.Descendants("item")
                            .Select(x =>
                            {
                                var pub = ParseDateUtc((string)x.Element("pubDate") ?? "");

                                var descRaw = (string)x.Element("description") ?? "";
                                var desc = FixSteamEncoding(WebUtility.HtmlDecode(descRaw));

                                var enclosureUrl = WebUtility.HtmlDecode(
                                    (string)x.Element("enclosure")?.Attribute("url") ?? string.Empty);

                                var imageUrl = string.Empty;

                                // 1) Priorité à l'image RSS officielle
                                if (!string.IsNullOrWhiteSpace(enclosureUrl))
                                {
                                    imageUrl = enclosureUrl;
                                }

                                // 2) Sinon on cherche dans le HTML
                                if (string.IsNullOrWhiteSpace(imageUrl))
                                {
                                    imageUrl = ExtractFirstImageUrl(descRaw);
                                }

                                if (string.IsNullOrWhiteSpace(imageUrl))
                                {
                                    imageUrl = ExtractFirstImageUrl(desc);
                                }

                                // 3) On ignore les placeholders YouTube
                                if (!string.IsNullOrWhiteSpace(imageUrl) &&
                                    imageUrl.IndexOf("youtube_16x9_placeholder.gif", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    imageUrl = string.Empty;
                                }

                                // 4) Fallback final : header Steam du jeu
                                if (string.IsNullOrWhiteSpace(imageUrl))
                                {
                                    imageUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{steamId}/header.jpg";
                                }

                                return new
                                {
                                    Title = FixSteamEncoding(WebUtility.HtmlDecode((string)x.Element("title") ?? "")),
                                    Desc = desc,
                                    ImageUrl = imageUrl,
                                    Link = WebUtility.HtmlDecode((string)x.Element("link") ?? ""),
                                    Published = pub
                                };
                            })
                            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
                            .OrderByDescending(x => x.Published)
                            .Take(Math.Max(1, count))
                            .Select(x => new SteamGameNewsItem
                            {
                                Title = x.Title,
                                Html = x.Desc,
                                Url = x.Link,
                                ImageUrl = x.ImageUrl,
                                DateString = x.Published == DateTime.MinValue
                                    ? string.Empty
                                    : x.Published.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                            })
                            .ToList();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return new List<SteamGameNewsItem>();
                }
        }

        // CONVERTIT Playnite → Steam (fr_FR → french)
        public SteamUpdateLiteService(string playniteLanguage)
        {
            steamLanguage = MapPlayniteLanguageToSteam(playniteLanguage);
        }

        private static string MapPlayniteLanguageToSteam(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "english";

            code = code.ToLowerInvariant();

            if (code.StartsWith("fr")) return "french";
            if (code.StartsWith("de")) return "german";
            if (code.StartsWith("es")) return "spanish";
            if (code.StartsWith("it")) return "italian";
            if (code.StartsWith("pt_br")) return "brazilian";
            if (code.StartsWith("pt")) return "portuguese";
            if (code.StartsWith("ru")) return "russian";
            if (code.StartsWith("pl")) return "polish";
            if (code.StartsWith("cs")) return "czech";
            if (code.StartsWith("ja")) return "japanese";
            if (code.StartsWith("ko")) return "koreana";
            if (code.StartsWith("zh_cn")) return "schinese";
            if (code.StartsWith("zh_tw")) return "tchinese";

            return "english";
        }

        //  FIX ENCODING STEAM
        private static int CountMojibake(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;

            string[] markers = { "Ã", "â", "Â", "ð", "ã", "€", "�" };
            int count = 0;

            foreach (var m in markers)
            {
                int idx = -1;
                while ((idx = s.IndexOf(m, idx + 1, StringComparison.Ordinal)) >= 0)
                    count++;
            }

            return count;
        }

        private static string FixSteamEncoding(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            var original = CountMojibake(s);
            if (original == 0)
                return s;

            var bytes = Encoding.GetEncoding(1252).GetBytes(s);
            var candidate = Encoding.UTF8.GetString(bytes);

            return CountMojibake(candidate) <= original ? candidate : s;
        }
    }
}
