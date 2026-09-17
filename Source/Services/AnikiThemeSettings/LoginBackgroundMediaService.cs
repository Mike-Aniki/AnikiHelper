using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AnikiHelper.Services.AnikiThemeSettings
{
    internal sealed class LoginBackgroundMediaCatalog
    {
        [JsonProperty("formatVersion")]
        public int FormatVersion { get; set; }

        [JsonProperty("releaseTag")]
        public string ReleaseTag { get; set; }

        [JsonProperty("items")]
        public List<LoginBackgroundMediaCatalogItem> Items { get; set; } = new List<LoginBackgroundMediaCatalogItem>();
    }

    internal sealed class LoginBackgroundMediaCatalogItem
    {
        [JsonProperty("presetKey")]
        public string PresetKey { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonProperty("randomIndex")]
        public int? RandomIndex { get; set; }
    }

    internal sealed class GitHubReleaseInfo
    {
        [JsonProperty("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();
    }

    internal sealed class GitHubReleaseAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [JsonProperty("url")]
        public string ApiUrl { get; set; }
    }

    internal sealed class LoginBackgroundMediaService : IDisposable
    {
        public const string CatalogUrl = "https://raw.githubusercontent.com/Mike-Aniki/Aniki-ReMake/main/media-catalog.json";
        public const string RandomPresetKey = "Login1";
        public const string DefaultPresetKey = "Default";
        public const string CustomPresetKey = "Login43";
        public const string CustomFileName = "CustomLogin.mp4";
        public const string DefaultFileName = "Acceuil.mp4";
        public const int LuckyDayRandomIndex = 42;
        public const int CustomRandomIndex = 43;

        private const int SupportedCatalogFormatVersion = 1;
        private const string StartupVideoFolderName = "Startup Video";

        private static readonly Regex VideoSourceRegex = new Regex(
            "Startup Video[\\\\/](?<file>[^'\\\"}]+\\.mp4)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RandomTemplateRegex = new Regex(
            "<ControlTemplate\\s+x:Key=\"VideoTpl_(?<index>\\d+)\"[\\s\\S]*?</ControlTemplate>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RandomTemplateNameRegex = new Regex(
            @"<!--\s*(?<index>\d+)\s+(?<name>.*?)\s*-->\s*<ControlTemplate\s+x:Key=""VideoTpl_(?<templateIndex>\d+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex Sha256Regex = new Regex("^[A-Fa-f0-9]{64}$", RegexOptions.Compiled);

        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly HttpClient http;
        private readonly string rootFolder;
        private readonly string libraryFolder;
        private readonly string catalogCachePath;
        private readonly object fileSync = new object();
        private bool disposed;
        private bool selectionDownloadInProgress;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        public LoginBackgroundMediaService(IPlayniteAPI api, ILogger logger, string pluginUserDataPath)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.logger = logger;

            rootFolder = Path.Combine(pluginUserDataPath ?? string.Empty, "LoginBackgroundMedia");
            libraryFolder = Path.Combine(rootFolder, "Library");
            catalogCachePath = Path.Combine(rootFolder, "media-catalog.json");

            Directory.CreateDirectory(rootFolder);
            Directory.CreateDirectory(libraryFolder);

            http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AnikiHelper-LoginBackgroundMedia/2.0");
        }

        public string LibraryFolder => libraryFolder;

        public bool IsRandomPreset(string presetKey)
        {
            return string.Equals(presetKey, RandomPresetKey, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsDefaultPreset(string presetKey)
        {
            return string.Equals(presetKey, DefaultPresetKey, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCustomPreset(string presetKey)
        {
            return string.Equals(presetKey, CustomPresetKey, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryResolveRequiredVideoFile(AnikiPresetItem preset, string themePath, out string fileName)
        {
            fileName = null;

            if (preset == null || string.IsNullOrWhiteSpace(themePath) || preset.Files == null)
            {
                return false;
            }

            foreach (var relativeFile in preset.Files)
            {
                if (string.IsNullOrWhiteSpace(relativeFile))
                {
                    continue;
                }

                var resourcePath = CombineThemePath(themePath, relativeFile);
                if (!File.Exists(resourcePath))
                {
                    continue;
                }

                try
                {
                    var text = File.ReadAllText(resourcePath);
                    var match = VideoSourceRegex.Match(text);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var candidate = Path.GetFileName(match.Groups["file"].Value.Trim());
                    if (IsSafeVideoFileName(candidate))
                    {
                        fileName = candidate;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to inspect login preset resource: " + resourcePath);
                }
            }

            return false;
        }

        public bool IsVideoInstalled(string themePath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(themePath) || !IsSafeVideoFileName(fileName))
            {
                return false;
            }

            var target = GetThemeTargetPath(themePath, fileName);
            return File.Exists(target) && new FileInfo(target).Length > 0;
        }

        public bool IsPersistentVideoAvailable(string fileName)
        {
            try
            {
                if (!IsSafeVideoFileName(fileName))
                {
                    return false;
                }

                var path = GetPersistentPath(fileName);
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Restores only videos that are already owned by the Helper persistent library.
        /// Videos merely bundled with the theme are deliberately NOT adopted here: otherwise an
        /// existing full theme would populate the persistent library with every optional video at
        /// startup and on-demand downloads would never be exercised. This is local-only and performs
        /// no network access. A theme-bundled video is adopted only when the user actually selects it.
        /// </summary>
        public void SynchronizeManagedMedia(string themePath, IEnumerable<string> managedFileNames)
        {
            if (string.IsNullOrWhiteSpace(themePath) || managedFileNames == null)
            {
                return;
            }

            try
            {
                var names = managedFileNames
                    .Where(IsSafeVideoFileName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var fileName in names)
                {
                    try
                    {
                        if (IsPersistentVideoAvailable(fileName))
                        {
                            RestorePersistentVideoProjected(themePath, fileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to restore managed login media: " + fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Managed login media synchronization failed.");
            }
        }

        public bool EnsurePersistentVideoProjected(string themePath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(themePath) || !IsSafeVideoFileName(fileName))
            {
                return false;
            }

            lock (fileSync)
            {
                try
                {
                    AdoptOrRestoreFile(themePath, fileName);
                    return IsVideoInstalled(themePath, fileName);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to restore login background projection: " + fileName);
                    return false;
                }
            }
        }

        public bool RestorePersistentVideoProjected(string themePath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(themePath) || !IsSafeVideoFileName(fileName))
            {
                return false;
            }

            lock (fileSync)
            {
                try
                {
                    var persistentPath = GetPersistentPath(fileName);
                    if (!File.Exists(persistentPath) || new FileInfo(persistentPath).Length <= 0)
                    {
                        return IsVideoInstalled(themePath, fileName);
                    }

                    var targetPath = GetThemeTargetPath(themePath, fileName);
                    if (!File.Exists(targetPath))
                    {
                        CreateThemeProjection(targetPath, persistentPath);
                    }

                    return IsVideoInstalled(themePath, fileName);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to project persistent login background: " + fileName);
                    return false;
                }
            }
        }


        public IReadOnlyDictionary<int, string> GetRandomDisplayNames(string themePath)
        {
            var result = new Dictionary<int, string>();

            try
            {
                if (string.IsNullOrWhiteSpace(themePath) || !Directory.Exists(themePath))
                {
                    return result;
                }

                var randomXaml = Path.Combine(themePath, "Themes Option", "5.LoginScreen", "Connexion", "LoginRandom.xaml");
                if (!File.Exists(randomXaml))
                {
                    randomXaml = Directory.EnumerateFiles(themePath, "LoginRandom.xaml", SearchOption.AllDirectories).FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(randomXaml) || !File.Exists(randomXaml))
                {
                    return result;
                }

                var text = File.ReadAllText(randomXaml);
                foreach (Match match in RandomTemplateNameRegex.Matches(text))
                {
                    if (!int.TryParse(match.Groups["index"].Value, out var index) ||
                        !int.TryParse(match.Groups["templateIndex"].Value, out var templateIndex) ||
                        index != templateIndex ||
                        index <= 0 ||
                        index == LuckyDayRandomIndex ||
                        index == CustomRandomIndex)
                    {
                        continue;
                    }

                    var name = (match.Groups["name"].Value ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result[index] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to read Random Login display names.");
            }

            return result;
        }

        public IReadOnlyList<int> GetAvailableRandomIndexes(string themePath)
        {
            var result = new List<int>();

            try
            {
                if (string.IsNullOrWhiteSpace(themePath) || !Directory.Exists(themePath))
                {
                    return result;
                }

                var randomXaml = Path.Combine(themePath, "Themes Option", "5.LoginScreen", "Connexion", "LoginRandom.xaml");
                if (!File.Exists(randomXaml))
                {
                    randomXaml = Directory.EnumerateFiles(themePath, "LoginRandom.xaml", SearchOption.AllDirectories).FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(randomXaml) || !File.Exists(randomXaml))
                {
                    return result;
                }

                var text = File.ReadAllText(randomXaml);
                foreach (Match template in RandomTemplateRegex.Matches(text))
                {
                    int index;
                    if (!int.TryParse(template.Groups["index"].Value, out index) || index <= 0)
                    {
                        continue;
                    }

                    // 42 is reserved exclusively for the Lucky Day easter egg. It must never be
                    // part of the normal Random Login pool, even though its template stays in XAML.
                    if (index == LuckyDayRandomIndex || index == CustomRandomIndex)
                    {
                        continue;
                    }

                    var videoMatch = VideoSourceRegex.Match(template.Value);
                    if (!videoMatch.Success)
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(videoMatch.Groups["file"].Value.Trim());
                    if (!IsSafeVideoFileName(fileName))
                    {
                        continue;
                    }

                    if (IsPersistentVideoAvailable(fileName))
                    {
                        RestorePersistentVideoProjected(themePath, fileName);
                    }

                    if (IsVideoInstalled(themePath, fileName))
                    {
                        result.Add(index);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to build the installed Random Login pool.");
            }

            return result.Distinct().OrderBy(x => x).ToList();
        }

        public async Task<bool> EnsureVideoAvailableAsync(
            string presetKey,
            string displayName,
            string requiredFileName,
            string themePath)
        {
            ThrowIfDisposed();

            if (!IsSafeVideoFileName(requiredFileName) || string.IsNullOrWhiteSpace(themePath))
            {
                return false;
            }

            if (EnsurePersistentVideoProjected(themePath, requiredFileName))
            {
                return true;
            }

            if (selectionDownloadInProgress)
            {
                return false;
            }

            selectionDownloadInProgress = true;
            try
            {
                LoginBackgroundMediaCatalog catalog;
                try
                {
                    catalog = await LoadCatalogAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][LoginMedia] Could not load media catalog.");
                    ShowError(Localize(
                        "LOCAnikiHelperLoginMediaCatalogUnavailable",
                        "This login background is not available for download right now."));
                    return false;
                }

                var item = (catalog.Items ?? new List<LoginBackgroundMediaCatalogItem>())
                    .FirstOrDefault(x => x != null &&
                        (string.Equals(x.PresetKey, presetKey, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(x.FileName, requiredFileName, StringComparison.OrdinalIgnoreCase)));

                if (item == null || !ValidateCatalogItem(item, presetKey, requiredFileName))
                {
                    ShowError(Localize(
                        "LOCAnikiHelperLoginMediaCatalogUnavailable",
                        "This login background is not available for download right now."));
                    return false;
                }

                var friendlyName = string.IsNullOrWhiteSpace(displayName)
                    ? (string.IsNullOrWhiteSpace(item.Name) ? requiredFileName : item.Name)
                    : displayName;

                var prompt = string.Format(
                    Localize(
                        "LOCAnikiHelperLoginMediaDownloadPrompt",
                        "{0} is not installed.\n\nDownload this login background now?\nSize: {1}"),
                    friendlyName,
                    FormatBytes(item.Size));

                var confirmation = api.Dialogs.ShowMessage(
                    prompt,
                    Localize("LOCAnikiHelperLoginMediaDownloadTitle", "Download login background"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return false;
                }

                Exception downloadError = null;
                var cancelled = false;
                var persistentPath = GetPersistentPath(requiredFileName);

                api.Dialogs.ActivateGlobalProgress(async args =>
                {
                    try
                    {
                        Directory.CreateDirectory(libraryFolder);
                        args.ProgressMaxValue = 100;
                        args.CurrentProgressValue = 0;
                        args.IsIndeterminate = false;
                        args.Text = string.Format(
                            Localize("LOCAnikiHelperLoginMediaDownloading", "Downloading {0}..."),
                            friendlyName);

                        await DownloadAndVerifyAsync(item, persistentPath, percent => args.CurrentProgressValue = percent, args.CancelToken);
                        EnsurePersistentVideoProjected(themePath, requiredFileName);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    catch (Exception ex)
                    {
                        downloadError = ex;
                    }
                }, new GlobalProgressOptions(
                    string.Format(Localize("LOCAnikiHelperLoginMediaDownloading", "Downloading {0}..."), friendlyName),
                    true)
                {
                    IsIndeterminate = false
                });

                if (cancelled)
                {
                    return false;
                }

                if (downloadError != null)
                {
                    logger?.Warn(downloadError, "[AnikiHelper][LoginMedia] Login background download failed: " + requiredFileName);
                    ShowError(
                        Localize("LOCAnikiHelperLoginMediaDownloadFailed", "The login background could not be downloaded.") +
                        "\n\n" + downloadError.Message);
                    return false;
                }

                return IsVideoInstalled(themePath, requiredFileName);
            }
            finally
            {
                selectionDownloadInProgress = false;
            }
        }

        public int GetDownloadedVideosCount()
        {
            try
            {
                Directory.CreateDirectory(libraryFolder);
                return Directory.EnumerateFiles(libraryFolder, "*.mp4", SearchOption.TopDirectoryOnly)
                    .Where(path => !string.Equals(Path.GetFileName(path), CustomFileName, StringComparison.OrdinalIgnoreCase))
                    .Where(path => !string.Equals(Path.GetFileName(path), DefaultFileName, StringComparison.OrdinalIgnoreCase))
                    .Select(path => new FileInfo(path))
                    .Count(info => info.Exists && info.Length > 0);
            }
            catch
            {
                return 0;
            }
        }

        public long GetDownloadedVideosSizeBytes()
        {
            try
            {
                Directory.CreateDirectory(libraryFolder);
                return Directory.EnumerateFiles(libraryFolder, "*.mp4", SearchOption.TopDirectoryOnly)
                    .Where(path => !string.Equals(Path.GetFileName(path), CustomFileName, StringComparison.OrdinalIgnoreCase))
                    .Where(path => !string.Equals(Path.GetFileName(path), DefaultFileName, StringComparison.OrdinalIgnoreCase))
                    .Select(path => new FileInfo(path))
                    .Where(info => info.Exists && info.Length > 0)
                    .Sum(info => info.Length);
            }
            catch
            {
                return 0L;
            }
        }

        public List<string> ClearDownloadedVideos(string themePath)
        {
            var removed = new List<string>();

            lock (fileSync)
            {
                try
                {
                    Directory.CreateDirectory(libraryFolder);
                    var files = Directory.EnumerateFiles(libraryFolder, "*.mp4", SearchOption.TopDirectoryOnly)
                        .Where(path => !string.Equals(Path.GetFileName(path), CustomFileName, StringComparison.OrdinalIgnoreCase))
                        .Where(path => !string.Equals(Path.GetFileName(path), DefaultFileName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var path in files)
                    {
                        try
                        {
                            var fileName = Path.GetFileName(path);
                            if (!IsSafeVideoFileName(fileName))
                            {
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(themePath))
                            {
                                TryDelete(GetThemeTargetPath(themePath, fileName));
                            }

                            TryDelete(path);
                            removed.Add(fileName);
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to clear downloaded login video: " + path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][LoginMedia] Failed to clear downloaded login videos.");
                }
            }

            return removed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void AdoptOrRestoreFile(string themePath, string fileName)
        {
            var persistentPath = GetPersistentPath(fileName);
            var themeTargetPath = GetThemeTargetPath(themePath, fileName);
            var persistentExists = File.Exists(persistentPath) && new FileInfo(persistentPath).Length > 0;
            var themeExists = File.Exists(themeTargetPath) && new FileInfo(themeTargetPath).Length > 0;

            if (persistentExists)
            {
                if (!themeExists)
                {
                    CreateThemeProjection(themeTargetPath, persistentPath);
                }

                return;
            }

            if (!themeExists)
            {
                return;
            }

            Directory.CreateDirectory(libraryFolder);

            // Existing installs already contain all of these videos inside the theme. Create the
            // persistent Helper-side hard link directly to that file so migrating users keep their
            // media across theme updates without using twice the disk space.
            if (!TryCreateHardLink(persistentPath, themeTargetPath))
            {
                File.Copy(themeTargetPath, persistentPath, false);
                logger?.Info("[AnikiHelper][LoginMedia] Hard link unavailable; stored persistent copy for " + fileName);
            }
            else
            {
                logger?.Info("[AnikiHelper][LoginMedia] Adopted existing login video with hard link: " + fileName);
            }
        }

        private void ReplaceThemeProjection(string themePath, string fileName, string persistentPath)
        {
            var themeTargetPath = GetThemeTargetPath(themePath, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(themeTargetPath));

            TryDelete(themeTargetPath);
            CreateThemeProjection(themeTargetPath, persistentPath);
        }

        private void CreateThemeProjection(string themeTargetPath, string persistentPath)
        {
            if (!File.Exists(persistentPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(themeTargetPath));

            if (File.Exists(themeTargetPath))
            {
                return;
            }

            if (!TryCreateHardLink(themeTargetPath, persistentPath))
            {
                File.Copy(persistentPath, themeTargetPath, false);
                logger?.Info("[AnikiHelper][LoginMedia] Hard link unavailable; copied login video into theme: " + Path.GetFileName(themeTargetPath));
            }
            else
            {
                logger?.Info("[AnikiHelper][LoginMedia] Restored login video hard link into theme: " + Path.GetFileName(themeTargetPath));
            }
        }

        private bool TryCreateHardLink(string newLinkPath, string existingFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newLinkPath) || string.IsNullOrWhiteSpace(existingFilePath) ||
                    !File.Exists(existingFilePath) || File.Exists(newLinkPath))
                {
                    return false;
                }

                var newRoot = Path.GetPathRoot(Path.GetFullPath(newLinkPath));
                var existingRoot = Path.GetPathRoot(Path.GetFullPath(existingFilePath));
                if (!string.Equals(newRoot, existingRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(newLinkPath));
                return CreateHardLink(newLinkPath, existingFilePath, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Hard link creation failed; copy fallback will be used.");
                return false;
            }
        }

        private async Task<LoginBackgroundMediaCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
        {
            string json = null;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, CatalogUrl))
                {
                    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }

                var onlineCatalog = ParseAndValidateCatalog(json);
                WriteTextAtomic(catalogCachePath, json);
                return onlineCatalog;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][LoginMedia] Online media catalog load failed; trying cache.");
                if (!File.Exists(catalogCachePath))
                {
                    throw;
                }

                json = File.ReadAllText(catalogCachePath);
                return ParseAndValidateCatalog(json);
            }
        }

        private LoginBackgroundMediaCatalog ParseAndValidateCatalog(string json)
        {
            var catalog = JsonConvert.DeserializeObject<LoginBackgroundMediaCatalog>(json ?? string.Empty);
            if (catalog == null || catalog.FormatVersion != SupportedCatalogFormatVersion)
            {
                throw new InvalidDataException("Unsupported login background media catalog format.");
            }

            catalog.Items = catalog.Items ?? new List<LoginBackgroundMediaCatalogItem>();
            return catalog;
        }

        private bool ValidateCatalogItem(LoginBackgroundMediaCatalogItem item, string presetKey, string requiredFileName)
        {
            if (item == null ||
                !string.Equals(item.PresetKey ?? string.Empty, presetKey ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.FileName ?? string.Empty, requiredFileName ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !IsSafeVideoFileName(item.FileName) ||
                item.Size <= 0 ||
                !Sha256Regex.IsMatch(item.Sha256 ?? string.Empty))
            {
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private async Task<HttpResponseMessage> OpenDownloadResponseAsync(
            LoginBackgroundMediaCatalogItem item,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage directResponse = null;
            try
            {
                directResponse = await http.GetAsync(
                    item.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (directResponse.IsSuccessStatusCode)
                {
                    return directResponse;
                }

                // A GitHub release asset URL is normally enough, but GitHub can normalize an
                // uploaded asset name. If the catalog URL is stale/wrong, resolve the real asset
                // through the public Releases API instead of failing immediately with 404.
                if (directResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    directResponse.EnsureSuccessStatusCode();
                }
            }
            finally
            {
                if (directResponse != null && !directResponse.IsSuccessStatusCode)
                {
                    directResponse.Dispose();
                }
            }

            var asset = await ResolveGitHubReleaseAssetAsync(item, cancellationToken).ConfigureAwait(false);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    "The login background asset was not found in the GitHub release: " + item.FileName);
            }

            // Prefer GitHub's API asset endpoint because it identifies the asset by numeric id and
            // does not depend on filename escaping/normalization in /releases/download/... URLs.
            if (!string.IsNullOrWhiteSpace(asset.ApiUrl))
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, asset.ApiUrl))
                {
                    request.Headers.Accept.ParseAdd("application/octet-stream");
                    var response = await http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }

                    response.Dispose();
                }
            }

            if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                throw new FileNotFoundException(
                    "GitHub did not provide a download URL for: " + item.FileName);
            }

            var browserResponse = await http.GetAsync(
                asset.BrowserDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            browserResponse.EnsureSuccessStatusCode();
            return browserResponse;
        }

        private async Task<GitHubReleaseAsset> ResolveGitHubReleaseAssetAsync(
            LoginBackgroundMediaCatalogItem item,
            CancellationToken cancellationToken)
        {
            var tag = TryExtractReleaseTag(item.DownloadUrl);
            if (string.IsNullOrWhiteSpace(tag))
            {
                tag = "login-backgrounds-v1";
            }

            var apiUrl = "https://api.github.com/repos/Mike-Aniki/Aniki-ReMake/releases/tags/" + Uri.EscapeDataString(tag);
            using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var release = JsonConvert.DeserializeObject<GitHubReleaseInfo>(json);
                    var assets = release?.Assets ?? new List<GitHubReleaseAsset>();

                    var exact = assets.FirstOrDefault(x => x != null &&
                        string.Equals(x.Name ?? string.Empty, item.FileName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (exact != null)
                    {
                        return exact;
                    }

                    var expectedNormalized = NormalizeAssetName(item.FileName);
                    var normalized = assets.FirstOrDefault(x => x != null &&
                        string.Equals(NormalizeAssetName(x.Name), expectedNormalized, StringComparison.OrdinalIgnoreCase));
                    if (normalized != null)
                    {
                        logger?.Warn("[AnikiHelper][LoginMedia] GitHub normalized release asset name '" +
                            item.FileName + "' to '" + normalized.Name + "'.");
                        return normalized;
                    }

                    // Last safe fallback: use size only when it identifies exactly one release asset.
                    var sameSize = assets.Where(x => x != null && x.Size == item.Size).ToList();
                    if (sameSize.Count == 1)
                    {
                        logger?.Warn("[AnikiHelper][LoginMedia] Matched GitHub release asset by unique size: " +
                            sameSize[0].Name);
                        return sameSize[0];
                    }
                }
            }

            return null;
        }

        private static string TryExtractReleaseTag(string downloadUrl)
        {
            try
            {
                Uri uri;
                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri))
                {
                    return null;
                }

                const string marker = "/releases/download/";
                var path = uri.AbsolutePath;
                var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    return null;
                }

                start += marker.Length;
                var end = path.IndexOf('/', start);
                if (end <= start)
                {
                    return null;
                }

                return Uri.UnescapeDataString(path.Substring(start, end - start));
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private async Task DownloadAndVerifyAsync(
            LoginBackgroundMediaCatalogItem item,
            string persistentPath,
            Action<int> reportProgress,
            CancellationToken cancellationToken)
        {
            var tempPath = persistentPath + ".download-" + Guid.NewGuid().ToString("N");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(persistentPath));

                using (var response = await OpenDownloadResponseAsync(item, cancellationToken).ConfigureAwait(false))
                {
                    var total = response.Content.Headers.ContentLength.GetValueOrDefault(item.Size);
                    if (total <= 0)
                    {
                        total = item.Size;
                    }

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true))
                    {
                        var buffer = new byte[1024 * 128];
                        long received = 0;
                        int read;

                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            received += read;

                            if (total > 0)
                            {
                                var percent = (int)Math.Min(100, Math.Max(0, received * 100L / total));
                                reportProgress?.Invoke(percent);
                            }
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(tempPath);
                if (item.Size > 0 && fileInfo.Length != item.Size)
                {
                    throw new InvalidDataException("Downloaded file size does not match the catalog.");
                }

                var actualSha = ComputeSha256(tempPath);
                if (!string.Equals(actualSha, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(Localize(
                        "LOCAnikiHelperLoginMediaVerifyFailed",
                        "The downloaded file could not be verified."));
                }

                if (File.Exists(persistentPath))
                {
                    File.Delete(persistentPath);
                }

                File.Move(tempPath, persistentPath);
                reportProgress?.Invoke(100);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private string GetPersistentPath(string fileName)
        {
            if (!IsSafeVideoFileName(fileName))
            {
                throw new InvalidDataException("Unsafe login background file name.");
            }

            return Path.Combine(libraryFolder, fileName);
        }

        private static string GetThemeTargetPath(string themePath, string fileName)
        {
            return Path.Combine(themePath, StartupVideoFolderName, fileName);
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static bool IsSafeVideoFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetExtension(fileName), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) &&
                   fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static string CombineThemePath(string themePath, string relativePath)
        {
            var normalized = (relativePath ?? string.Empty)
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            return Path.Combine(themePath, normalized);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "?";
            }

            var mb = bytes / 1024d / 1024d;
            return mb >= 10d ? mb.ToString("0.0") + " MB" : mb.ToString("0.00") + " MB";
        }

        private static string Localize(string key, string fallback)
        {
            try
            {
                var value = ResourceProvider.GetString(key);
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private void ShowInformation(string message)
        {
            try
            {
                api.Dialogs.ShowMessage(
                    message,
                    "Aniki Helper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch
            {
            }
        }

        private void ShowError(string message)
        {
            try
            {
                api.Dialogs.ShowMessage(
                    message,
                    "Aniki Helper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
        }

        private static void WriteTextAtomic(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, content ?? string.Empty);
                File.Copy(temp, path, true);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(LoginBackgroundMediaService));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            http.Dispose();
        }
    }
}
