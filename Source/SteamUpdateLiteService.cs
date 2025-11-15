using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnikiHelper
{
    public class SteamUpdateLiteResult
    {
        public string Title { get; set; }
        public DateTime Published { get; set; }
        public string HtmlBody { get; set; }
    }

    public class SteamUpdateLiteService
    {
        private const string FeedTemplate = "https://store.steampowered.com/feeds/news/app/{0}/?l={1}";
        private readonly string steamLanguage;

        // ============================================================
        //   PUBLIC : récupère la dernière update (Update / Patch Notes)
        // ============================================================
        public async Task<SteamUpdateLiteResult> GetLatestUpdateAsync(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId))
                return null;

            // 1) On tente dans la langue Playnite
            var result = await GetLatestUpdateForLanguageAsync(steamId, steamLanguage);
            if (result != null)
                return result;

            // 2) Fallback anglais si Playnite ≠ anglais
            if (!string.Equals(steamLanguage, "english", StringComparison.OrdinalIgnoreCase))
            {
                return await GetLatestUpdateForLanguageAsync(steamId, "english");
            }

            return null;
        }

        // ============================================================
        //      INTERNE : récupère update pour UNE langue donnée
        // ============================================================
        private async Task<SteamUpdateLiteResult> GetLatestUpdateForLanguageAsync(string steamId, string language)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.Accept] = "text/xml";
                    client.Headers[HttpRequestHeader.UserAgent] =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SteamUpdateLite";

                    var xml = await client.DownloadStringTaskAsync(string.Format(FeedTemplate, steamId, language));
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

                    DateTime ParseDate(string raw)
                    {
                        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal, out var dt))
                            return dt.ToLocalTime();

                        return DateTime.MinValue;
                    }

                    bool LooksLikeUpdate(string title)
                    {
                        if (string.IsNullOrWhiteSpace(title))
                            return false;

                        var t = title.ToLowerInvariant();

                        string[] keywords =
                        {
                            "patch", "patch notes",
                            "hotfix", "hot fix",
                            "changelog", "change log",
                            "update", "title update",
                            "major update", "minor update",
                            "balance update",
                            "bugfix", "bug fix", "fixes",
                            "release notes",
                            "version ", "version:", "ver.", "v."
                        };

                        if (keywords.Any(k => t.Contains(k)))
                            return true;

                        // Détection simple d’un format x.y ou x.y.z
                        for (int i = 0; i < t.Length - 2; i++)
                        {
                            if (char.IsDigit(t[i]) && t[i + 1] == '.' && char.IsDigit(t[i + 2]))
                                return true;
                        }

                        return false;
                    }

                    // === SEULEMENT UPDATE ===
                    var candidate = items
                        .Where(i => LooksLikeUpdate(i.Title))
                        .OrderByDescending(i => ParseDate(i.Pub))
                        .FirstOrDefault();

                    if (candidate == null)
                        return null;

                    return new SteamUpdateLiteResult
                    {
                        Title = candidate.Title,
                        Published = ParseDate(candidate.Pub),
                        HtmlBody = candidate.Desc
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        //         CONVERTIT Playnite → Steam (fr_FR → french)
        // ============================================================
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

        // ============================================================
        //                   FIX ENCODING STEAM
        // ============================================================
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
