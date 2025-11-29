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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;


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
        public string LocalImagePath { get; set; }


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
        private readonly ILogger logger = LogManager.GetLogger();


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

        // Mots à virer pour les flux qui spamment les promos / deals / Black Friday
        private static readonly string[] SpamKeywords = new[]
        {
            "black friday",
            "cashback",
            "amazon",
            "deal",
            "promo",
            "promotion",
            "bon plan",
            "bargain",
            "discount"
        };

        // Liste des flux RSS qui doivent être filtrés (anti-promo / anti-deals)
        private static readonly string[] SpamFilteredFeeds = new[]
        {
            "feeds.feedburner.com/ign/games-all",   
            "www.lacremedugaming.fr/feed",
            "www.actugaming.net/feed",
            "gamespot.com/feeds/mashup",
            "www.jeuxvideo.com/rss/rss.xml",
            "www.millenium.org/rss",
            "kotaku.com/rss",
            "www.jvfrance.com/feed",
            "gameinformer.com/news",
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

        // === CHEMIN CACHE IMAGES ===
        private string GetNewsImagesRoot()
        {
            var root = Path.Combine(
                api.Paths.ExtensionsDataPath,
                "96a983a3-3f13-4dce-a474-4052b718bb52",
                "NewsImages");

            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            return root;
        }

        private static string ComputeHash(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                var hash = md5.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        // Télécharge une image dans le cache et renvoie le chemin local
        private async Task<string> DownloadImageToCacheAsync(string imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    return string.Empty;
                }

                // Déduire l'extension depuis l'URL si possible
                string ext = null;
                try
                {
                    var uri = new Uri(imageUrl);
                    ext = Path.GetExtension(uri.AbsolutePath);
                }
                catch
                {
                    // ignore
                }

                // Fallback .jpg si extension chelou ou absente
                if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                {
                    ext = ".jpg";
                }

                var fileName = ComputeHash(imageUrl) + ext;
                var fullPath = Path.Combine(GetNewsImagesRoot(), fileName);

                // Si déjà sur disque → on réutilise
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }

                // Téléchargement brut (pas de resize, pas de WPF, pas de System.Drawing)
                using (var http = CreateHttpClient())
                {
                    var bytes = await http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);

                    if (bytes == null || bytes.Length == 0)
                    {
                        return string.Empty;
                    }

                    File.WriteAllBytes(fullPath, bytes);
                }

                return fullPath;
            }
            catch
            {
                // En cas d'erreur → pas de crash, juste pas d'image
                return string.Empty;
            }
        }


        // Sauvegarde un BitmapSource en JPEG
        private void SaveJpeg(string path, System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var fs = new FileStream(path, FileMode.Create))
            {
                encoder.Save(fs);
            }
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

        // Petit helper générique pour d'autres flux RSS 
        // On réutilise exactement le même parsing / filtrage de base.
        public Task<List<SteamGlobalNewsItem>> GetGenericFeedAsync(string url)
        {
            return LoadFeedAsync(url);
        }


        // === MÉTHODE PRINCIPALE : ce que le thème appelle ===
        public async Task<List<SteamGlobalNewsItem>> GetGlobalNewsAsync()
        {
            logger.Info("[SteamGlobalNewsService] GetGlobalNewsAsync() starting...");
            logger.Info($"CustomFeed={settings?.SteamNewsCustomFeedUrl}, LastRefreshUtc={settings?.SteamGlobalNewsLastRefreshUtc}");
            // 1) Charger le cache existant et virer les news trop vieilles
            var cache = LoadNewsCache();
            var nowUtc = DateTime.UtcNow;
            var minDateUtc = nowUtc.AddDays(-CacheMaxDays);

            cache = cache
                .Where(n => n.PublishedUtc >= minDateUtc)
                .ToList();

            // Vérifier si le JSON existe réellement
            bool cacheMissing = !File.Exists(GetNewsCachePath());

            // Si on a déjà scanné il y a moins de 3h ET que le JSON existe,
            // on renvoie simplement le cache (limité à DisplayMaxItems)
            if (!cacheMissing && settings?.SteamGlobalNewsLastRefreshUtc != null)
            {
                var lastUtc = settings.SteamGlobalNewsLastRefreshUtc.Value;
                if ((nowUtc - lastUtc).TotalHours < 3)
                {
                    logger.Info($"[SteamGlobalNewsService] SKIP scan → lastRefresh={lastUtc}, hoursSince={(nowUtc - lastUtc).TotalHours:F2}");

                    return cache
                        .OrderByDescending(n => n.PublishedUtc)
                        .Take(DisplayMaxItems)
                        .ToList();
                }
            }

            // 2) Charger un flux frais (custom → sinon baseUrl)
            List<SteamGlobalNewsItem> fresh = null;

            // Au départ, on reset le flag d’erreur du flux custom
            if (settings != null)
            {
                settings.SteamNewsCustomFeedInvalid = false;
            }

            // 2a) Flux custom défini par l’utilisateur ?
            var customUrl = settings?.SteamNewsCustomFeedUrl;
            if (!string.IsNullOrWhiteSpace(customUrl))
            {
                logger.Warn("[SteamGlobalNewsService] REFRESH TRIGGERED → scanning RSS feed NOW");

                fresh = await LoadFeedAsync(customUrl).ConfigureAwait(false);

                if (fresh == null || fresh.Count == 0)
                {
                    // Flux custom HS → on met le flag, le thème pourra afficher un warning
                    if (settings != null)
                    {
                        settings.SteamNewsCustomFeedInvalid = true;
                    }
                }
            }

            // 2b) Si pas de custom ou flux custom vide → flux par défaut (langue Playnite puis fallback anglais)
            if (fresh == null || fresh.Count == 0)
            {
                var urlLang = AddLangToUrl(baseUrl, steamLanguage);
                fresh = await LoadFeedAsync(urlLang).ConfigureAwait(false) ?? new List<SteamGlobalNewsItem>();

                if (fresh.Count == 0)
                {
                    var urlEn = AddLangToUrl(baseUrl, "english");
                    var fallback = await LoadFeedAsync(urlEn).ConfigureAwait(false);
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

            // 3) Fusion cache + fresh par clé unique (AppId + Titre + Date)
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
                // Le flux frais écrase la version ancienne du même article
                allDict[key] = item;
            }

            // 4) Nettoyage et limitation du cache
            var merged = allDict.Values
                .Where(n => n.PublishedUtc >= minDateUtc)
                .OrderByDescending(n => n.PublishedUtc)
                .Take(CacheMaxItems)
                .ToList();

            // 5) Sauvegarde sur disque
            SaveNewsCache(merged);

            // 5b) Met à jour le timestamp de dernier refresh
            if (settings != null)
            {
                settings.SteamGlobalNewsLastRefreshUtc = nowUtc;
            }

            // 6) On renvoie seulement les X dernières au thème
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
                    bool isGitHubFeed = feedUrl.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0;

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

                        var publishUtc = ParsePubDate(rawDate, minDateUtc);
                        if (publishUtc < minDateUtc)
                        {
                            continue;
                        }

                        // === TITLE ===
                        var titleRaw =
                            (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "title")
                            ?? string.Empty;

                        var title = titleRaw?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(title))
                        {
                            continue;
                        }

                        // --- Anti spam (Black Friday / Deals / Amazon / Promo) ---
                        // S'applique uniquement aux flux listés dans SpamFilteredFeeds
                        if (SpamFilteredFeeds.Any(url =>
                                feedUrl.IndexOf(url, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            var lower = title.ToLowerInvariant();
                            if (SpamKeywords.Any(k => lower.Contains(k)))
                            {
                                // On ignore complètement cette news
                                continue;
                            }
                        }

                        // Si c'est un flux GitHub, on préfixe TOUJOURS par le nom du repo,
                        // sauf si le titre contient déjà ce nom.
                        if (feedUrl.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var repoName = GetGitHubRepoNameFromFeedUrl(feedUrl);
                            if (!string.IsNullOrWhiteSpace(repoName))
                            {
                                // Pour éviter les doublons du style "Playnite – Playnite 10.0"
                                if (title.IndexOf(repoName, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    title = $"{repoName} – {title}";
                                }
                            }
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
                        // 1) On essaie d'abord de récupérer content:encoded (article complet)
                        var encodedRaw = node
                            .Descendants()
                            .FirstOrDefault(x => x.Name.LocalName == "encoded")
                            ?.Value;

                        // 2) GitHub Releases, Atom, etc. : <content type="html">...</content>
                        var contentRaw = node
                            .Elements()
                            .FirstOrDefault(x => x.Name.LocalName == "content")
                            ?.Value;

                        // 3) RSS classique : <description> ou <summary>
                        var descOrSummary =
                            (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "description")
                            ?? (string)node.Elements().FirstOrDefault(x => x.Name.LocalName == "summary");

                        // 4) Priorité : encoded > content > description/summary
                        string descRaw;
                        if (!string.IsNullOrWhiteSpace(encodedRaw))
                        {
                            descRaw = encodedRaw;                // article complet (IGN, etc.)
                        }
                        else if (!string.IsNullOrWhiteSpace(contentRaw))
                        {
                            descRaw = contentRaw;                // ex : GitHub Releases (content type="html")
                        }
                        else
                        {
                            descRaw = descOrSummary ?? string.Empty;
                        }



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

                        var cleanedHtml = CleanHtmlForNews(descRaw);
                        var fullText = StripHtml(descRaw);
                        var summaryText = fullText;

                        // HtmlDescription : on privilégie le HTML nettoyé.
                        string finalHtml;
                        if (!string.IsNullOrWhiteSpace(cleanedHtml))
                        {
                            finalHtml = cleanedHtml;
                        }
                        else
                        {
                            var encoded = System.Net.WebUtility.HtmlEncode(fullText ?? string.Empty);
                            finalHtml = $"<p>{encoded.Replace("\n", "<br/>")}</p>";
                        }

                        // === IMAGE: ignorer les avatars GitHub ===
                        if (isGitHubFeed &&
                            !string.IsNullOrEmpty(imageUrl) &&
                            imageUrl.IndexOf("avatars.githubusercontent.com", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            imageUrl = string.Empty;
                        }

                        // Télécharger l'image uniquement si elle est valide
                        var localImagePath = string.Empty;
                        if (!string.IsNullOrWhiteSpace(imageUrl))
                        {
                            localImagePath = await DownloadImageToCacheAsync(imageUrl);
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
                            ImageUrl = imageUrl,            // URL RSS d’origine (si tu veux l’ouvrir dans le navigateur)
                            HtmlDescription = finalHtml,    // HtmlTextView
                            LocalImagePath = localImagePath // chemin local pour <Image>
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

        private static string GetGitHubRepoNameFromFeedUrl(string feedUrl)
        {
            if (string.IsNullOrWhiteSpace(feedUrl))
                return string.Empty;

            try
            {
                var uri = new Uri(feedUrl);
                var segments = uri.AbsolutePath.Trim('/').Split('/');
                // ex: /ashpynov/PlayniteSound/releases.atom
                //      0        1            2
                if (segments.Length >= 2)
                {
                    return segments[1]; // PlayniteSound
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
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

