using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;


namespace AnikiHelper
{
    public class SteamGlobalNewsItem
    {
        public string AppId { get; set; }
        public string GameName { get; set; }
        public string Title { get; set; }
        public string DateString { get; set; }
        public DateTime PublishedUtc { get; set; }
        public string Url { get; set; }
        public string CoverPath { get; set; }
        public string IconPath { get; set; }
        public string Summary { get; set; }
        public string ImageUrl { get; set; }
        public string HtmlDescription { get; set; }
        
    }

    public class SteamGlobalNewsService
    {
        // *** URL par défaut (tu peux remettre le flux Steam ici plus tard) ***
        private readonly string baseUrl = "https://feeds.feedburner.com/ign/games-all";

        // GUID du plugin Steam officiel Playnite (sert pour retrouver les jeux Steam)
        private static readonly Guid SteamPluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        private readonly IPlayniteAPI api;
        private readonly AnikiHelperSettings settings;
        private readonly string steamLanguage;

        // Cache des news globales
        private const int DisplayMaxItems = 20;  // Nombre de news visibles dans le thème.
        private const int CacheMaxItems = DisplayMaxItems * 2;   // taille max du cache JSON garde simplement *deux fois plus* que ce qu’on affiche.
        private const int CacheMaxDays = 30;     // on garde max jours dans le cache

        private static readonly string[] PatchKeywords = new[]
        {
            "patch",
            "patch notes",
            "hotfix",
            "update",
            "changelog",
            "hot fix",
            "bugfix",
            "maintenance",
            "mise à jour",
            "mise a jour",
            "correctif",
            "correctifs"
        };

        public SteamGlobalNewsService(IPlayniteAPI api, AnikiHelperSettings settings)
        {
            this.api = api;
            this.settings = settings;

            var playniteLang = api?.ApplicationSettings?.Language;
            steamLanguage = MapPlayniteLanguageToSteam(playniteLang);
        }

        // === CHEMIN CACHE JSON ===
        private string GetNewsCachePath()
        {
            var root = Path.Combine(api.Paths.ExtensionsDataPath, "96a983a3-3f13-4dce-a474-4052b718bb52");

            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            return Path.Combine(root, "CacheNews.json");
        }

        private static string MakeNewsKey(SteamGlobalNewsItem item)
        {
            var appId = item.AppId ?? "";
            var title = item.Title ?? "";
            var date = item.PublishedUtc.ToUniversalTime().ToString("O");
            return $"{appId}|{title}|{date}";
        }

        private List<SteamGlobalNewsItem> LoadNewsCache()
        {
            var path = GetNewsCachePath();
            if (!File.Exists(path))
            {
                return new List<SteamGlobalNewsItem>();
            }

            try
            {
                var cache = Serialization.FromJsonFile<List<SteamGlobalNewsItem>>(path);
                return cache ?? new List<SteamGlobalNewsItem>();
            }
            catch
            {
                return new List<SteamGlobalNewsItem>();
            }
        }

        private void SaveNewsCache(List<SteamGlobalNewsItem> items)
        {
            var path = GetNewsCachePath();

            try
            {
                var json = Serialization.ToJson(items);
                File.WriteAllText(path, json);
            }
            catch
            {
                // On ignore, pas de crash
            }
        }

        // Ajoute &l=xxx au besoin
        private static string AddLangToUrl(string url, string lang)
        {
            if (string.IsNullOrWhiteSpace(lang))
                return url;

            var sep = url.Contains("?") ? "&" : "?";
            return $"{url}{sep}l={lang}";
        }

        // === MÉTHODE PRINCIPALE : ce que le thème appelle ===
        public async Task<List<SteamGlobalNewsItem>> GetGlobalNewsAsync()
        {
            // 1) Charger le cache existant et virer les news trop vieilles
            var cache = LoadNewsCache();
            var nowUtc = DateTime.UtcNow;
            var minDateUtc = nowUtc.AddDays(-CacheMaxDays);

            cache = cache
                .Where(n => n.PublishedUtc >= minDateUtc)
                .ToList();

            // 2) Charger un flux frais (custom → sinon baseUrl)
            List<SteamGlobalNewsItem> fresh = null;

            // Au départ, on reset le flag d’erreur
            if (settings != null)
            {
                settings.SteamNewsCustomFeedInvalid = false;
            }

            // 2a) Flux custom défini par l’utilisateur ?
            var customUrl = settings?.SteamNewsCustomFeedUrl;
            if (!string.IsNullOrWhiteSpace(customUrl))
            {
                fresh = await LoadFeedAsync(customUrl);

                if (fresh == null || fresh.Count == 0)
                {
                    // Flux custom HS → on met le flag, on retombera sur le flux par défaut
                    if (settings != null)
                    {
                        settings.SteamNewsCustomFeedInvalid = true;
                    }
                }
            }

            // 2b) Si pas de custom ou flux custom vide → flux par défaut (avec langue + fallback anglais)
            if (fresh == null || fresh.Count == 0)
            {
                var urlLang = AddLangToUrl(baseUrl, steamLanguage);
                fresh = await LoadFeedAsync(urlLang) ?? new List<SteamGlobalNewsItem>();

                if (fresh.Count == 0)
                {
                    var urlEn = AddLangToUrl(baseUrl, "english");
                    var fallback = await LoadFeedAsync(urlEn);
                    if (fallback != null)
                    {
                        fresh = fallback;
                    }
                }
            }

            if (fresh == null)
            {
                fresh = new List<SteamGlobalNewsItem>();
            }

            // 3) Fusion cache + fresh par clé unique
            var allDict = new Dictionary<string, SteamGlobalNewsItem>();

            foreach (var item in cache)
            {
                var key = MakeNewsKey(item);
                if (!allDict.ContainsKey(key))
                {
                    allDict[key] = item;
                }
            }

            foreach (var item in fresh)
            {
                var key = MakeNewsKey(item);
                allDict[key] = item; // écrase l’ancienne version
            }

            // 4) Nettoyage et limitation du cache
            var merged = allDict.Values
                .Where(n => n.PublishedUtc >= minDateUtc)
                .OrderByDescending(n => n.PublishedUtc)
                .Take(CacheMaxItems)
                .ToList();

            // 5) Sauvegarde
            SaveNewsCache(merged);

            // 6) On renvoie seulement les X premières au thème
            return merged
                .OrderByDescending(n => n.PublishedUtc)
                .Take(DisplayMaxItems)
                .ToList();
        }



        private HttpClient CreateHttpClient()
        {
            var http = new HttpClient();

            // User-Agent de navigateur réel pour éviter le blocage anti-bots
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/122.0 Safari/537.36");

            // Demande explicitement du XML
            http.DefaultRequestHeaders.Accept.ParseAdd(
                "application/rss+xml, text/xml, */*");

            return http;
        }


        // === Chargement + parsing d’un flux unique ===
        private async Task<List<SteamGlobalNewsItem>> LoadFeedAsync(string feedUrl)
        {
            try
            {
                using (var http = CreateHttpClient())
                {
                    var xml = await http.GetStringAsync(feedUrl).ConfigureAwait(false);
                    var doc = XDocument.Parse(xml);

                    var nowUtc = DateTime.UtcNow;
                    int maxItems = 30;
                    int maxDays = 30;
                    var minDateUtc = nowUtc.AddDays(-maxDays);

                    var result = new List<SteamGlobalNewsItem>();

                    var nodes = doc.Descendants().Where(e =>
                    {
                        var name = e.Name.LocalName.ToLowerInvariant();
                        return name == "item" || name == "entry";
                    });

                    foreach (var node in nodes)
                    {
                        // === DATE ===
                        string rawDate =
                            (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "pubDate")
                            ?? (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "updated")
                            ?? (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "date")
                            ?? string.Empty;

                        var publishUtc = ParsePubDate(rawDate, nowUtc);
                        if (publishUtc < minDateUtc)
                        {
                            continue;
                        }

                        // === TITLE ===
                        var titleRaw =
                            (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "title")
                            ?? string.Empty;
                        var title = titleRaw.Trim();
                        if (string.IsNullOrEmpty(title))
                        {
                            continue;
                        }

                        // Détection flux custom (non Steam)
                        bool isCustom = !feedUrl.Contains("steampowered");

                        // Si ce n'est PAS un flux custom (donc Steam) → appliquer le filtre patch notes
                        if (!isCustom)
                        {
                            var lowerTitle = title.ToLowerInvariant();
                            if (PatchKeywords.Any(k => lowerTitle.Contains(k)))
                            {
                                continue; // On ignore cette news
                            }
                        }

                        // === LINK ===
                        var linkElem = node.Elements().FirstOrDefault(x => x.Name.LocalName == "link");
                        string link = string.Empty;
                        if (linkElem != null)
                        {
                            link = (string)linkElem.Value ?? string.Empty;
                            link = link.Trim();

                            if (string.IsNullOrEmpty(link))
                            {
                                var hrefAttr = linkElem.Attribute("href");
                                if (hrefAttr != null)
                                {
                                    link = hrefAttr.Value.Trim();
                                }
                            }
                        }

                        var appId = ExtractAppIdFromUrl(link) ?? "global";

                        // === GAME MATCH (si c'est vraiment du Steam) ===
                        Game matchingGame = null;
                        if (appId != "global")
                        {
                            matchingGame = api.Database.Games
                                .FirstOrDefault(g =>
                                    g.PluginId == SteamPluginId &&
                                    string.Equals(g.GameId, appId, StringComparison.OrdinalIgnoreCase));
                        }

                        var gameName = matchingGame?.Name ?? string.Empty;

                        // === DESCRIPTION / SUMMARY ===
                        // On essaie d'abord de récupérer content:encoded (article complet)
                        var encodedRaw = node
                            .Descendants()
                            .FirstOrDefault(x => x.Name.LocalName == "encoded")
                            ?.Value;

                        var descOrSummary =
                            (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "description")
                            ?? (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "summary");

                        var descRaw = !string.IsNullOrWhiteSpace(encodedRaw)
                            ? encodedRaw                // article complet (IGN, etc.)
                            : (descOrSummary ?? string.Empty);



                        // ==== IMAGE : priorité enclosure / media:content / media:thumbnail / img dans description ====
                        string imageUrl = string.Empty;

                        // Fallback image depuis le contenu encodé (article complet) si dispo
                        if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrWhiteSpace(encodedRaw))
                        {
                            var imgFromEncoded = ExtractFirstImageUrl(encodedRaw);
                            if (!string.IsNullOrEmpty(imgFromEncoded))
                            {
                                imageUrl = imgFromEncoded;
                            }
                        }

                        // 1) <enclosure url="...">
                        var enclosure = node.Element("enclosure");
                        if (enclosure != null)
                        {
                            var urlAttr = enclosure.Attribute("url");
                            if (urlAttr != null)
                            {
                                imageUrl = urlAttr.Value;
                            }
                        }

                        // 2) <media:content url="...">
                        if (string.IsNullOrEmpty(imageUrl))
                        {
                            var media = node.Elements()
                                .FirstOrDefault(x =>
                                    x.Name.LocalName == "content" &&
                                    x.Attribute("url") != null);

                            if (media != null)
                            {
                                var url = (string)media.Attribute("url");
                                var type = (string)media.Attribute("type");

                                if (!string.IsNullOrEmpty(url) &&
                                    (type?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? true))
                                {
                                    imageUrl = url;
                                }
                            }
                        }

                        // 2b) <media:thumbnail>
                        if (string.IsNullOrEmpty(imageUrl))
                        {
                            var thumb = node.Elements()
                                .FirstOrDefault(x => x.Name.LocalName == "thumbnail");

                            if (thumb != null)
                            {
                                var url = (string)thumb.Attribute("url") ?? thumb.Value?.Trim();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    imageUrl = url;
                                }
                            }
                        }

                        // 3) fallback: <img> dans description
                        if (string.IsNullOrEmpty(imageUrl))
                        {
                            imageUrl = ExtractFirstImageUrl(descRaw);
                        }

                        // === TEXTE A AFFICHER ===

                        // HTML nettoyé pour HtmlTextView (on garde <p>, <br>, etc.)
                        var cleanedHtml = CleanHtmlForNews(descRaw);

                        // Texte brut pour le summary
                        var fullText = StripHtml(descRaw);

                        // Summary 
                        var summaryText = fullText;
                      

                        // HtmlDescription : on privilégie le HTML nettoyé.
                        // Si pour une raison quelconque il est vide, on encode le texte brut en <p> simple.
                        string finalHtml;
                        if (!string.IsNullOrWhiteSpace(cleanedHtml))
                        {
                            finalHtml = cleanedHtml;   // article HTML (IGN, etc.)
                        }
                        else
                        {
                            var encoded = System.Net.WebUtility.HtmlEncode(fullText ?? string.Empty);
                            finalHtml = $"<p>{encoded.Replace("\n", "<br/>")}</p>";
                        }

                        var newsItem = new SteamGlobalNewsItem
                        {
                            AppId = appId,
                            GameName = gameName,
                            Title = title,
                            PublishedUtc = publishUtc,
                            DateString = publishUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                            Url = link,
                            CoverPath = GetGameCoverPath(matchingGame),
                            IconPath = GetGameIconPath(matchingGame),
                            Summary = summaryText,
                            ImageUrl = imageUrl,
                            HtmlDescription = finalHtml   // ton HtmlTextView affiche ça
                        };


                        result.Add(newsItem);

                        if (result.Count >= maxItems)
                        {
                            break;
                        }
                    }

                    return result;
                }
            }
            catch
            {
                // Si le RSS est pourri, on renvoie null → le caller gère fallback + flag
                return null;
            }
        }




        // === Helpers existants (inchangés) ===

        private static DateTime ParsePubDate(string raw, DateTime fallbackUtc)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallbackUtc;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var dt))
            {
                return dt.ToUniversalTime();
            }

            return fallbackUtc;
        }

        private static string ExtractAppIdFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var match = Regex.Match(url, @"\/app\/(\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string StripHtml(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var withoutTags = Regex.Replace(input, "<.*?>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
        }

        private static string ExtractFirstImageUrl(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var mediaMatch = Regex.Match(html, "media:content[^>]+url=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (mediaMatch.Success)
                return mediaMatch.Groups[1].Value;

            var imgMatch = Regex.Match(html, "<img[^>]+src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (imgMatch.Success)
                return imgMatch.Groups[1].Value;

            return string.Empty;
        }

        private static string CleanSteamHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

            html = Regex.Replace(
                html,
                "<div[^>]+class=\"[^\"]*bb_video[^\"]*\"[^>]*>.*?</div>",
                string.Empty,
                options);

            html = Regex.Replace(
                html,
                "<video[^>]*>.*?</video>",
                string.Empty,
                options);

            html = Regex.Replace(
                html,
                "<iframe[^>]*>.*?</iframe>",
                string.Empty,
                options);

            html = Regex.Replace(
                html,
                "<div[^>]*>(\\s|&nbsp;|&#160;|<br\\s*/?>)*</div>",
                string.Empty,
                options);

            html = Regex.Replace(
                html,
                "<p[^>]*>[\\sーー\\-_=]+</p>",
                "<hr/>",
                options);

            html = Regex.Replace(
                html,
                "<p[^>]*>(\\s|&nbsp;|&#160;|<br\\s*/?>)*</p>",
                string.Empty,
                options);

            html = Regex.Replace(
                html,
                @"^(\s|<br\s*/?>|&nbsp;|&#160;)+",
                string.Empty,
                options);

            html = Regex.Replace(
                html,
                "(<br\\s*/?>\\s*){3,}",
                "<br/><br/>",
                options);

            return html.Trim();
        }

        private static string NormalizeSteamImages(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

            return Regex.Replace(
                html,
                "<img(?<attrs>[^>]*?)src=[\"'](?<url>[^\"']+)[\"'](?<end>[^>]*)>",
                m =>
                {
                    var url = m.Groups["url"].Value;

                    string finalTag =
                        $"<p style=\"text-align:center; margin:20px 0;\">" +
                        $"<img src=\"{url}\" " +
                        $"style=\"max-width:900px; width:100%; height:auto; border-radius:8px;\" />" +
                        $"</p>";

                    return finalTag;
                },
                options);
        }





        private static string CleanHtmlForNews(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

            // 0) Décoder les entités HTML (&lt; &gt; &#234; etc.)
            html = WebUtility.HtmlDecode(html);

            // 0b) Virer un éventuel wrapper CDATA
            html = html.Replace("<![CDATA[", string.Empty)
                       .Replace("]]>", string.Empty);

            // 1) Supprimer les commentaires HTML
            html = Regex.Replace(html, "<!--.*?-->", string.Empty, options);

            // 2) Virer scripts / styles / head / meta / link / noscript
            html = Regex.Replace(html, "<(script|style|head|meta|link|noscript)[^>]*>.*?</\\1>",
                                 string.Empty, options);

            // 3) Virer iframes / object / embed / video (on ne les affiche pas)
            html = Regex.Replace(html, "<(iframe|object|embed|video)[^>]*>.*?</\\1>",
                                 string.Empty, options);

            // 4) Neutraliser les handlers JS et "javascript:" dans les attributs
            html = Regex.Replace(html, @"on\w+\s*=\s*""[^""]*""", string.Empty, options);
            html = Regex.Replace(html, @"on\w+\s*=\s*'[^']*'", string.Empty, options);
            html = Regex.Replace(html, @"javascript\s*:", string.Empty, options);

            // 5) Supprimer les <div> / <p> totalement vides
            html = Regex.Replace(html, "<(div|p)[^>]*>\\s*</\\1>", string.Empty, options);

            // 6) Normaliser les titres <h1>–<h6> en <p><strong>…</strong></p>
            html = Regex.Replace(html, "<h[1-6][^>]*>", "<p><strong>", options);
            html = Regex.Replace(html, "</h[1-6]>", "</strong></p>", options);

            // 7) Compacter les <br> successifs (3+ → 2)
            html = Regex.Replace(html, "(<br\\s*/?>\\s*){3,}", "<br/><br/>", options);

            // 7b) Supprimer les balises <a> mais garder le texte (pas de style "lien" dans le thème)
            html = Regex.Replace(
                html,
                "<a[^>]*>(?<text>.*?)</a>",
                "${text}",
                options
            );

            // 8) Trim
            html = html.Trim();

            // 9) Normaliser les images (taille + centrage)
            html = NormalizeSteamImages(html);

            // 10) Wrapper léger pour la lisibilité (taille quasi normale)
            html =
                "<div style=\"line-height:1.5; font-size:102%;\">" +
                html +
                "</div>";

            return html;
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

        private static string GetGameCoverPath(Game game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            return game.CoverImage ?? string.Empty;
        }

        private static string GetGameIconPath(Game game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            return game.Icon ?? string.Empty;
        }
    }
}

