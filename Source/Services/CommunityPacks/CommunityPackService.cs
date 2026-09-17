using AnikiHelper.Services.Packs;
using AnikiHelper.Services.ColorPacks;
using AnikiHelper.Services.CompletePacks;
using AnikiHelper.Services.LoginPacks;
using AnikiHelper.Services.SoundPacks;
using AnikiHelper.Services.VisualPacks;
using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AnikiHelper.Services.CommunityPacks
{
    public sealed class CommunityPackCatalog
    {
        [JsonProperty("formatVersion")]
        public int FormatVersion { get; set; }

        [JsonProperty("packs")]
        public List<CommunityPackCatalogItem> Packs { get; set; } = new List<CommunityPackCatalogItem>();
    }

    public sealed class CommunityPackPreviewSet
    {
        [JsonProperty("visual")]
        public string Visual { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("login")]
        public string Login { get; set; }

        [JsonProperty("sound")]
        public string Sound { get; set; }
    }

    public sealed class CommunityPackComponentIds
    {
        [JsonProperty("visual")]
        public string Visual { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("login")]
        public string Login { get; set; }

        [JsonProperty("sound")]
        public string Sound { get; set; }
    }

    public sealed class CommunityPackCatalogItem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("previewUrl")]
        public string PreviewUrl { get; set; }

        [JsonProperty("packPreviews")]
        public CommunityPackPreviewSet PackPreviews { get; set; }

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonProperty("componentIds")]
        public CommunityPackComponentIds ComponentIds { get; set; }

        [JsonProperty("parentCompletePackId")]
        public string ParentCompletePackId { get; set; }

        [JsonProperty("generatedFromCompletePack")]
        public bool GeneratedFromCompletePack { get; set; }

        [JsonIgnore]
        public string ParentCompletePackName { get; set; }

        [JsonProperty("publishedAt")]
        public string PublishedAt { get; set; }

        [JsonProperty("updatedAt")]
        public string UpdatedAt { get; set; }

        [JsonProperty("featured")]
        public bool Featured { get; set; }
    }

    public sealed class CommunityPackInstallation
    {
        public string CommunityId { get; set; }
        public string LocalPackId { get; set; }
        public string Version { get; set; }
        public DateTime InstalledUtc { get; set; }
    }

    internal sealed class CommunityPackInstallationIndex
    {
        public int Version { get; set; } = 1;
        public List<CommunityPackInstallation> Packs { get; set; } = new List<CommunityPackInstallation>();
    }

    internal sealed class CommunityPackManifest
    {
        [JsonProperty("formatVersion")]
        public int FormatVersion { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public sealed class CommunityPackCatalogResult
    {
        public List<CommunityPackCatalogItem> Packs { get; set; } = new List<CommunityPackCatalogItem>();
        public bool UsedCachedCatalog { get; set; }
    }

    public sealed class CommunityPackInstallResult
    {
        public string PackName { get; set; }
        public string Version { get; set; }
        public bool WasUpdate { get; set; }
        public string LocalPackId { get; set; }
    }

    internal sealed class CommonImportResult
    {
        public string LocalId { get; set; }
        public bool WasAlreadyInLibrary { get; set; }
    }

    internal sealed class LocalPackIdentity
    {
        public string LocalId { get; set; }
        public string StablePackId { get; set; }
        public string Version { get; set; }
    }

    public sealed class CommunityPackService : IDisposable
    {
        public const string CatalogUrl = "https://raw.githubusercontent.com/Mike-Aniki/AnikiCommunityPacks/main/catalog.json";
        private const int SupportedCatalogFormatVersion = 1;
        private const int SupportedPackFormatVersion = 1;

        private static readonly HashSet<string> SupportedPackTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "visual", "color", "login", "sound", "complete"
        };

        private static readonly Regex SafeIdRegex = new Regex("^[A-Za-z0-9._-]{3,120}$", RegexOptions.Compiled);

        private readonly global::AnikiHelper.AnikiHelper plugin;
        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly HttpClient http;
        private readonly string packType;
        private readonly string pluginUserDataPath;
        private readonly string cacheRoot;
        private readonly string previewCacheRoot;
        private readonly string catalogCachePath;
        private readonly string installationsPath;
        private bool disposed;

        public CommunityPackService(
            global::AnikiHelper.AnikiHelper plugin,
            IPlayniteAPI api,
            string pluginUserDataPath,
            ILogger logger,
            string packType)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.logger = logger;
            this.packType = NormalizePackType(packType);
            this.pluginUserDataPath = pluginUserDataPath ?? string.Empty;

            cacheRoot = AnikiPackStorage.GetAreaRoot(this.pluginUserDataPath, "CommunityPacks");
            previewCacheRoot = Path.Combine(cacheRoot, "Previews");
            catalogCachePath = Path.Combine(cacheRoot, "catalog.json");
            installationsPath = Path.Combine(cacheRoot, "Installations", this.packType + ".json");

            Directory.CreateDirectory(cacheRoot);
            Directory.CreateDirectory(previewCacheRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(installationsPath));
            MigrateLegacyVisualInstallations(pluginUserDataPath);

            http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AnikiHelper-CommunityPacks/1.0");
        }

        public string PackType => packType;

        public Task<CommunityPackCatalogResult> GetCatalogAsync(CancellationToken cancellationToken)
        {
            return GetCatalogCoreAsync(cancellationToken, includeAllTypes: false);
        }

        public Task<CommunityPackCatalogResult> GetAllCatalogAsync(CancellationToken cancellationToken)
        {
            return GetCatalogCoreAsync(cancellationToken, includeAllTypes: true);
        }

        private async Task<CommunityPackCatalogResult> GetCatalogCoreAsync(
            CancellationToken cancellationToken,
            bool includeAllTypes)
        {
            ThrowIfDisposed();
            string json = null;
            var usedCache = false;

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

                ValidateCatalog(json);
                WriteTextAtomic(catalogCachePath, json);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks] Online catalog load failed.");
                if (!File.Exists(catalogCachePath))
                {
                    throw new InvalidOperationException("The Community Packs catalog could not be downloaded and no cached catalog is available.", ex);
                }

                json = File.ReadAllText(catalogCachePath);
                ValidateCatalog(json);
                usedCache = true;
            }

            var catalog = JsonConvert.DeserializeObject<CommunityPackCatalog>(json) ?? new CommunityPackCatalog();
            var allPacks = catalog.Packs ?? new List<CommunityPackCatalogItem>();
            var completePacksById = allPacks
                .Where(x => x != null &&
                            string.Equals(x.Type, "complete", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in allPacks)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ParentCompletePackId))
                {
                    continue;
                }

                CommunityPackCatalogItem parent;
                if (completePacksById.TryGetValue(item.ParentCompletePackId, out parent))
                {
                    item.ParentCompletePackName = parent?.Name ?? string.Empty;
                }
            }

            return new CommunityPackCatalogResult
            {
                UsedCachedCatalog = usedCache,
                Packs = allPacks
                    .Where(x => includeAllTypes || string.Equals(x?.Type, packType, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Featured)
                    .ThenByDescending(x => ParseDateSafe(x.UpdatedAt))
                    .ThenBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };
        }

        public async Task<string> GetPreviewPathAsync(CommunityPackCatalogItem item, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateCatalogItem(item);

            if (!TryGetHttpsUri(item.PreviewUrl, out var previewUri))
            {
                return null;
            }

            var extension = GetSafeImageExtension(previewUri.AbsolutePath);
            var path = Path.Combine(
                previewCacheRoot,
                MakeFileNameSafe(item.Type) + "-" + MakeFileNameSafe(item.Id) + "-" + MakeFileNameSafe(item.Version) + extension);

            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                return path;
            }

            try
            {
                using (var response = await http.GetAsync(previewUri, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0)
                    {
                        return null;
                    }

                    var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.WriteAllBytes(temporary, bytes);
                        File.Copy(temporary, path, true);
                    }
                    finally
                    {
                        TryDeleteFile(temporary);
                    }
                }

                CleanupOldPreviewVersions(item, path);
                return path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks] Preview download failed for " + item.Id + ".");
                return null;
            }
        }

        public Dictionary<string, CommunityPackInstallation> GetInstalledPacks()
        {
            ThrowIfDisposed();
            var index = LoadInstallationIndex();
            var localPacks = GetLocalPackIdentities();
            var localById = localPacks
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.LocalId))
                .GroupBy(x => x.LocalId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var changed = index.Packs.RemoveAll(x =>
                x == null ||
                string.IsNullOrWhiteSpace(x.CommunityId) ||
                string.IsNullOrWhiteSpace(x.LocalPackId) ||
                !localById.ContainsKey(x.LocalPackId)) > 0;

            foreach (var record in index.Packs)
            {
                LocalPackIdentity local;
                if (record == null ||
                    string.IsNullOrWhiteSpace(record.LocalPackId) ||
                    !localById.TryGetValue(record.LocalPackId, out local))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(local.Version) &&
                    !string.Equals(record.Version, local.Version, StringComparison.OrdinalIgnoreCase))
                {
                    record.Version = local.Version;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveInstallationIndex(index);
            }

            var result = index.Packs
                .GroupBy(x => x.CommunityId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.InstalledUtc).First(), StringComparer.OrdinalIgnoreCase);

            // Complete Packs import their child packs directly into the same local
            // libraries as individually installed packs. Also recognize those packs
            // (and manual imports) by their stable manifest ID so the Community pages
            // immediately show them as installed without forcing a re-download.
            foreach (var local in localPacks)
            {
                if (local == null ||
                    string.IsNullOrWhiteSpace(local.LocalId) ||
                    string.IsNullOrWhiteSpace(local.StablePackId) ||
                    result.ContainsKey(local.StablePackId))
                {
                    continue;
                }

                result[local.StablePackId] = new CommunityPackInstallation
                {
                    CommunityId = local.StablePackId,
                    LocalPackId = local.LocalId,
                    Version = local.Version ?? string.Empty,
                    InstalledUtc = DateTime.MinValue
                };
            }

            return result;
        }

        public async Task<CommunityPackInstallResult> InstallOrUpdateAsync(CommunityPackCatalogItem item, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateCatalogItem(item);
            if (!string.Equals(item.Type, packType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The selected Community Pack does not match this pack category.");
            }

            if (!TryGetHttpsUri(item.DownloadUrl, out var downloadUri))
            {
                throw new InvalidDataException("The Community Pack download URL is invalid or is not HTTPS.");
            }

            var installationIndex = LoadInstallationIndex();
            var existing = installationIndex.Packs.FirstOrDefault(x =>
                x != null && string.Equals(x.CommunityId, item.Id, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var matchingLocalPack = GetLocalPackIdentities().FirstOrDefault(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.StablePackId) &&
                    string.Equals(x.StablePackId, item.Id, StringComparison.OrdinalIgnoreCase));

                if (matchingLocalPack != null)
                {
                    existing = new CommunityPackInstallation
                    {
                        CommunityId = item.Id,
                        LocalPackId = matchingLocalPack.LocalId,
                        Version = matchingLocalPack.Version ?? string.Empty,
                        InstalledUtc = DateTime.MinValue
                    };
                }
            }

            var localIds = GetLocalPackIds();
            if (existing != null && !localIds.Contains(existing.LocalPackId))
            {
                installationIndex.Packs.Remove(existing);
                existing = null;
                SaveInstallationIndex(installationIndex);
            }

            var wasUpdate = existing != null;
            var wasVisualActive = packType == "visual" && existing != null &&
                                  string.Equals(GetActivePackId(), existing.LocalPackId, StringComparison.OrdinalIgnoreCase);

            if (wasUpdate && CommunityVisualPackService.CompareVersions(item.Version, existing.Version) <= 0)
            {
                return new CommunityPackInstallResult
                {
                    PackName = item.Name,
                    Version = existing.Version,
                    WasUpdate = false,
                    LocalPackId = existing.LocalPackId
                };
            }

            var tempFolder = Path.Combine(cacheRoot, "Temp");
            Directory.CreateDirectory(tempFolder);
            var tempZip = Path.Combine(tempFolder,
                MakeFileNameSafe(item.Type) + "-" + MakeFileNameSafe(item.Id) + "-" + MakeFileNameSafe(item.Version) + "-" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                using (var response = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(tempZip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                    }
                }

                logger?.Info($"[AnikiHelper][CommunityPacks] Download completed for '{item.Name}' ({item.Type}/{item.Id} {item.Version}).");
                ValidateDownloadedManifest(tempZip, item);
                logger?.Info($"[AnikiHelper][CommunityPacks] Manifest validated for '{item.Name}'.");

                CommonImportResult importResult = null;
                var dispatcher = Application.Current?.Dispatcher;
                Action importAction = () =>
                {
                    logger?.Info($"[AnikiHelper][CommunityPacks] Starting local import for '{item.Name}'.");
                    importResult = ImportPackage(tempZip, wasUpdate);
                    logger?.Info($"[AnikiHelper][CommunityPacks] Local import completed for '{item.Name}' -> '{importResult?.LocalId ?? "<null>"}'.");
                };
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(importAction);
                }
                else
                {
                    importAction();
                }

                if (importResult == null || string.IsNullOrWhiteSpace(importResult.LocalId))
                {
                    throw new InvalidOperationException("Aniki Helper did not return a valid local pack after import.");
                }

                // Keep the Community preview cached for the installed-library cards.
                try
                {
                    await GetPreviewPathAsync(item, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][CommunityPacks] Installed pack preview could not be cached.");
                }

                if (packType == "visual" && existing != null &&
                    !string.Equals(existing.LocalPackId, importResult.LocalId, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Action deleteOld = () => plugin.DeleteCustomVisualPack(existing.LocalPackId);
                        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(deleteOld); else deleteOld();
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][CommunityPacks] Old Visual Pack version could not be removed after update.");
                        if (!importResult.WasAlreadyInLibrary)
                        {
                            try
                            {
                                Action rollback = () => plugin.DeleteCustomVisualPack(importResult.LocalId);
                                if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(rollback); else rollback();
                            }
                            catch (Exception cleanupEx)
                            {
                                logger?.Warn(cleanupEx, "[AnikiHelper][CommunityPacks] Failed to roll back the newly imported Visual Pack update.");
                            }
                        }
                        throw;
                    }
                }

                if (wasVisualActive && !string.IsNullOrWhiteSpace(importResult.LocalId))
                {
                    // Preserve which Visual Pack is active without touching live theme resources.
                    // The normal refresh/apply path runs after the Community Hub closes.
                    Action preserveActive = () =>
                    {
                        var visualService = new VisualPackImportService(api, pluginUserDataPath, logger);
                        visualService.SetActivePack(importResult.LocalId);
                    };
                    if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(preserveActive); else preserveActive();
                }

                installationIndex = LoadInstallationIndex();
                installationIndex.Packs.RemoveAll(x => x != null && string.Equals(x.CommunityId, item.Id, StringComparison.OrdinalIgnoreCase));
                installationIndex.Packs.Add(new CommunityPackInstallation
                {
                    CommunityId = item.Id,
                    LocalPackId = importResult.LocalId,
                    Version = item.Version,
                    InstalledUtc = DateTime.UtcNow
                });
                SaveInstallationIndex(installationIndex);

                // Do NOT refresh/apply Theme Settings while the Community Hub is still open.
                // The underlying Fullscreen Settings window remains alive behind this modal window,
                // and live resource/preset refreshes during an import can destabilize WPF/Playnite.
                // FullscreenSettingsView.OnCommunityPacksWindowClosed already refreshes every pack
                // library once, after this modal Hub has been torn down.
                logger?.Info($"[AnikiHelper][CommunityPacks] Installation index saved for '{item.Name}'. UI refresh deferred until Community Hub closes.");

                return new CommunityPackInstallResult
                {
                    PackName = item.Name,
                    Version = item.Version,
                    WasUpdate = wasUpdate,
                    LocalPackId = importResult.LocalId
                };
            }
            finally
            {
                TryDeleteFile(tempZip);
            }
        }

        public CompletePackDeleteAnalysis AnalyzeCompletePackUninstall(string communityId)
        {
            ThrowIfDisposed();
            if (!string.Equals(packType, "complete", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(communityId))
            {
                return new CompletePackDeleteAnalysis();
            }

            var installationIndex = LoadInstallationIndex();
            var existing = installationIndex.Packs.FirstOrDefault(x =>
                x != null && string.Equals(x.CommunityId, communityId, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var matchingLocalPack = GetLocalPackIdentities().FirstOrDefault(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.StablePackId) &&
                    string.Equals(x.StablePackId, communityId, StringComparison.OrdinalIgnoreCase));

                if (matchingLocalPack == null)
                {
                    return new CompletePackDeleteAnalysis();
                }

                existing = new CommunityPackInstallation
                {
                    CommunityId = communityId,
                    LocalPackId = matchingLocalPack.LocalId,
                    Version = matchingLocalPack.Version ?? string.Empty,
                    InstalledUtc = DateTime.MinValue
                };
            }

            return plugin.AnalyzeCompletePackDelete(existing.LocalPackId);
        }

        public bool Uninstall(string communityId)
        {
            return Uninstall(communityId, false);
        }

        public bool Uninstall(string communityId, bool deleteIncludedPacks)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(communityId)) return false;

            var installationIndex = LoadInstallationIndex();
            var existing = installationIndex.Packs.FirstOrDefault(x =>
                x != null && string.Equals(x.CommunityId, communityId, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var matchingLocalPack = GetLocalPackIdentities().FirstOrDefault(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.StablePackId) &&
                    string.Equals(x.StablePackId, communityId, StringComparison.OrdinalIgnoreCase));

                if (matchingLocalPack == null)
                {
                    return false;
                }

                existing = new CommunityPackInstallation
                {
                    CommunityId = communityId,
                    LocalPackId = matchingLocalPack.LocalId,
                    Version = matchingLocalPack.Version ?? string.Empty,
                    InstalledUtc = DateTime.MinValue
                };
            }

            var dispatcher = Application.Current?.Dispatcher;
            Action delete = () =>
            {
                if (string.Equals(packType, "complete", StringComparison.OrdinalIgnoreCase))
                {
                    plugin.DeleteCompletePack(existing.LocalPackId, deleteIncludedPacks);
                }
                else
                {
                    DeleteLocalPack(existing.LocalPackId);
                }
            };
            if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(delete); else delete();

            installationIndex = LoadInstallationIndex();
            installationIndex.Packs.RemoveAll(x => x != null && string.Equals(x.CommunityId, communityId, StringComparison.OrdinalIgnoreCase));
            SaveInstallationIndex(installationIndex);
            RefreshPackUi();
            return true;
        }

        public static string NormalizePackType(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (!SupportedPackTypes.Contains(normalized))
            {
                throw new ArgumentException("Unsupported Community Pack type: " + (value ?? "<empty>"), nameof(value));
            }
            return normalized;
        }

        public static string GetPackTypeDisplayName(string packType)
        {
            switch (NormalizePackType(packType))
            {
                case "visual": return "Visual Packs";
                case "color": return "Color Packs";
                case "login": return "Login Packs";
                case "sound": return "Sound Packs";
                case "complete": return "Complete Packs";
                default: return "Community Packs";
            }
        }

        private CommonImportResult ImportPackage(string zipPath, bool wasUpdate)
        {
            switch (packType)
            {
                case "visual":
                {
                    var service = new VisualPackImportService(api, pluginUserDataPath, logger);
                    var visual = service.Import(zipPath, wasUpdate, false);
                    return visual == null
                        ? null
                        : new CommonImportResult
                        {
                            LocalId = visual.LocalId,
                            WasAlreadyInLibrary = visual.WasAlreadyInLibrary
                        };
                }

                case "color":
                {
                    var service = new ColorPackImportService(api, pluginUserDataPath, logger);
                    var color = service.Import(zipPath, false);
                    return color == null
                        ? null
                        : new CommonImportResult
                        {
                            LocalId = color.LocalId,
                            WasAlreadyInLibrary = color.WasAlreadyInLibrary
                        };
                }

                case "login":
                {
                    var service = new LoginPackImportService(api, pluginUserDataPath, logger);
                    var login = service.Import(zipPath, false);
                    return login == null
                        ? null
                        : new CommonImportResult
                        {
                            LocalId = login.LocalId,
                            WasAlreadyInLibrary = login.WasAlreadyInLibrary
                        };
                }

                case "sound":
                {
                    var service = new SoundPackImportService(api, pluginUserDataPath, logger);
                    var sound = service.Import(zipPath, false);
                    return sound == null
                        ? null
                        : new CommonImportResult
                        {
                            LocalId = sound.LocalId,
                            WasAlreadyInLibrary = sound.WasAlreadyInLibrary
                        };
                }

                case "complete":
                {
                    var service = new CompletePackImportService(api, pluginUserDataPath, logger);
                    var complete = service.Import(zipPath);
                    return complete == null
                        ? null
                        : new CommonImportResult
                        {
                            LocalId = complete.LocalId,
                            WasAlreadyInLibrary = complete.WasAlreadyInLibrary
                        };
                }

                default:
                    throw new InvalidOperationException("Unsupported Community Pack type.");
            }
        }

        private void DeleteLocalPack(string localId)
        {
            if (string.IsNullOrWhiteSpace(localId)) return;
            switch (packType)
            {
                case "visual": plugin.DeleteCustomVisualPack(localId); break;
                case "color": plugin.DeleteCustomColorPack(localId); break;
                case "login": plugin.DeleteLoginPack(localId); break;
                case "sound": plugin.DeleteSoundPack(localId); break;
                case "complete": plugin.DeleteCompletePack(localId); break;
            }
        }

        private List<LocalPackIdentity> GetLocalPackIdentities()
        {
            switch (packType)
            {
                case "visual":
                    return (plugin.GetCustomVisualPackLibrary()?.Packs ?? new List<VisualPackLibraryPack>())
                        .Where(x => x != null)
                        .Select(x => new LocalPackIdentity
                        {
                            LocalId = x.LocalId,
                            StablePackId = x.PackId,
                            Version = x.Version
                        })
                        .ToList();

                case "color":
                    return (plugin.GetCustomColorPackLibrary()?.Packs ?? new List<ColorPackLibraryPack>())
                        .Where(x => x != null)
                        .Select(x => new LocalPackIdentity
                        {
                            LocalId = x.LocalId,
                            StablePackId = x.PackId,
                            Version = x.Version
                        })
                        .ToList();

                case "login":
                    return (plugin.GetLoginPackLibrary()?.Packs ?? new List<LoginPackLibraryPack>())
                        .Where(x => x != null)
                        .Select(x => new LocalPackIdentity
                        {
                            LocalId = x.LocalId,
                            StablePackId = x.PackId,
                            Version = x.Version
                        })
                        .ToList();

                case "sound":
                    return (plugin.GetSoundPackLibrary()?.Packs ?? new List<SoundPackLibraryPack>())
                        .Where(x => x != null)
                        .Select(x => new LocalPackIdentity
                        {
                            LocalId = x.LocalId,
                            StablePackId = x.PackId,
                            Version = x.Version
                        })
                        .ToList();

                case "complete":
                    return (plugin.GetCompletePackLibrary()?.Packs ?? new List<CompletePackLibraryPack>())
                        .Where(x => x != null)
                        .Select(x => new LocalPackIdentity
                        {
                            LocalId = x.LocalId,
                            StablePackId = x.PackId,
                            Version = x.Version
                        })
                        .ToList();

                default:
                    return new List<LocalPackIdentity>();
            }
        }

        private HashSet<string> GetLocalPackIds()
        {
            return new HashSet<string>(
                GetLocalPackIdentities()
                    .Select(x => x?.LocalId)
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }

        private string GetActivePackId()
        {
            switch (packType)
            {
                case "visual": return plugin.GetCustomVisualPackLibrary()?.ActivePackId ?? string.Empty;
                case "color": return plugin.GetCustomColorPackLibrary()?.ActivePackId ?? string.Empty;
                case "login": return plugin.GetLoginPackLibrary()?.ActivePackId ?? string.Empty;
                case "sound": return plugin.GetSoundPackLibrary()?.ActivePackId ?? string.Empty;
                case "complete": return plugin.GetCompletePackLibrary()?.ActivePackId ?? string.Empty;
                default: return string.Empty;
            }
        }

        private void RefreshPackUi()
        {
            switch (packType)
            {
                case "visual": plugin.RefreshCustomVisualPackThemeSettings(); break;
                case "color": plugin.RefreshCustomColorPackThemeSettings(); break;
                case "login": plugin.RefreshLoginPackThemeSettings(); break;
                case "sound": plugin.RefreshSoundPackThemeSettings(); break;
                case "complete": plugin.RefreshCompletePackThemeSettings(); break;
            }
        }

        private string GetManifestFileName()
        {
            switch (packType)
            {
                case "visual": return "visualpack.json";
                case "color": return "colorpack.json";
                case "login": return "loginpack.json";
                case "sound": return "soundpack.json";
                case "complete": return "completepack.json";
                default: throw new InvalidOperationException("Unsupported Community Pack type.");
            }
        }

        private string GetExpectedManifestType()
        {
            switch (packType)
            {
                // Visual Pack exports intentionally do not contain a type field.
                case "visual": return null;
                case "color": return "colorPack";
                case "login": return "loginPack";
                case "sound": return "soundPack";
                case "complete": return "completePack";
                default: throw new InvalidOperationException("Unsupported Community Pack type.");
            }
        }

        private void ValidateDownloadedManifest(string zipPath, CommunityPackCatalogItem item)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var manifestName = GetManifestFileName();
                    var entries = archive.Entries
                        .Where(x => !string.IsNullOrEmpty(x.Name) &&
                                    string.Equals(x.FullName.Replace('\\', '/').Trim('/'), manifestName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (entries.Count != 1)
                    {
                        throw new InvalidDataException("The downloaded Community Pack must contain exactly one " + manifestName + " file at the ZIP root.");
                    }

                    CommunityPackManifest manifest;
                    using (var stream = entries[0].Open())
                    using (var reader = new StreamReader(stream))
                    {
                        manifest = JsonConvert.DeserializeObject<CommunityPackManifest>(reader.ReadToEnd());
                    }

                    if (manifest == null || manifest.FormatVersion != SupportedPackFormatVersion)
                    {
                        throw new InvalidDataException("The downloaded Community Pack manifest has an unsupported format version.");
                    }
                    var expectedManifestType = GetExpectedManifestType();
                    if (!string.IsNullOrWhiteSpace(expectedManifestType))
                    {
                        if (!string.Equals(manifest.Type ?? string.Empty, expectedManifestType, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("The downloaded Community Pack type does not match the catalog.");
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(manifest.Type))
                    {
                        throw new InvalidDataException("The downloaded Community Pack manifest contains an unexpected type value.");
                    }
                    if (!string.Equals(manifest.Id ?? string.Empty, item.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The downloaded Community Pack id does not match the catalog.");
                    }
                    if (!string.Equals(manifest.Version ?? string.Empty, item.Version ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The downloaded Community Pack version does not match the catalog.");
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("The downloaded Community Pack is not a valid ZIP package: " + ex.Message, ex);
            }
        }

        private void ValidateCatalog(string json)
        {
            CommunityPackCatalog catalog;
            try
            {
                catalog = JsonConvert.DeserializeObject<CommunityPackCatalog>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("The Community Packs catalog is not valid JSON.", ex);
            }

            if (catalog == null || catalog.FormatVersion != SupportedCatalogFormatVersion)
            {
                throw new InvalidDataException("Unsupported Community Packs catalog format.");
            }

            var allPacks = catalog.Packs ?? new List<CommunityPackCatalogItem>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<string, CommunityPackCatalogItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in allPacks)
            {
                ValidateCatalogItem(item);
                if (!ids.Add(item.Id))
                {
                    throw new InvalidDataException("The Community catalog contains a duplicate pack id: " + item.Id);
                }

                byId[item.Id] = item;
            }

            foreach (var item in allPacks)
            {
                if (string.IsNullOrWhiteSpace(item?.ParentCompletePackId))
                {
                    continue;
                }

                CommunityPackCatalogItem parent;
                if (!byId.TryGetValue(item.ParentCompletePackId, out parent) ||
                    !string.Equals(parent?.Type, "complete", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Community Pack '" + item.Id + "' references an invalid parent Complete Pack.");
                }
            }
        }

        private static void ValidateCatalogItem(CommunityPackCatalogItem item)
        {
            if (item == null) throw new InvalidDataException("The Community catalog contains an empty pack entry.");
            if (!SupportedPackTypes.Contains((item.Type ?? string.Empty).Trim()))
                throw new InvalidDataException("Community Pack '" + (item.Id ?? "<empty>") + "' has an invalid type.");
            if (string.IsNullOrWhiteSpace(item.Id) || !SafeIdRegex.IsMatch(item.Id))
                throw new InvalidDataException("Invalid Community Pack id: " + (item.Id ?? "<empty>"));
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException("Community Pack '" + item.Id + "' has no name.");

            CommunityVisualPackService.CompareVersions(item.Version, item.Version);
            if (!string.IsNullOrWhiteSpace(item.PreviewUrl) && !TryGetHttpsUri(item.PreviewUrl, out _))
                throw new InvalidDataException("Community Pack '" + item.Id + "' has an invalid preview URL.");

            if (item.PackPreviews != null)
            {
                if (!string.Equals((item.Type ?? string.Empty).Trim(), "complete", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Only Complete Packs can define component preview URLs.");

                foreach (var previewUrl in new[]
                {
                    item.PackPreviews.Visual,
                    item.PackPreviews.Color,
                    item.PackPreviews.Login,
                    item.PackPreviews.Sound
                })
                {
                    if (!string.IsNullOrWhiteSpace(previewUrl) && !TryGetHttpsUri(previewUrl, out _))
                        throw new InvalidDataException("Community Pack '" + item.Id + "' has an invalid component preview URL.");
                }
            }

            var isComplete = string.Equals((item.Type ?? string.Empty).Trim(), "complete", StringComparison.OrdinalIgnoreCase);
            if (item.ComponentIds != null && !isComplete)
                throw new InvalidDataException("Only Complete Packs can define component IDs.");

            if (!string.IsNullOrWhiteSpace(item.ParentCompletePackId))
            {
                if (isComplete || !SafeIdRegex.IsMatch(item.ParentCompletePackId))
                    throw new InvalidDataException("Community Pack '" + item.Id + "' has an invalid parent Complete Pack ID.");
            }

            if (item.GeneratedFromCompletePack && string.IsNullOrWhiteSpace(item.ParentCompletePackId))
                throw new InvalidDataException("Community Pack '" + item.Id + "' is marked as generated from a Complete Pack but has no parent Complete Pack ID.");

            if (!TryGetHttpsUri(item.DownloadUrl, out _))
                throw new InvalidDataException("Community Pack '" + item.Id + "' has an invalid download URL.");
        }

        private CommunityPackInstallationIndex LoadInstallationIndex()
        {
            if (!File.Exists(installationsPath)) return new CommunityPackInstallationIndex();
            try
            {
                var index = JsonConvert.DeserializeObject<CommunityPackInstallationIndex>(File.ReadAllText(installationsPath)) ?? new CommunityPackInstallationIndex();
                index.Packs = index.Packs ?? new List<CommunityPackInstallation>();
                return index;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks] Installation index was invalid and has been reset for " + packType + ".");
                return new CommunityPackInstallationIndex();
            }
        }

        private void SaveInstallationIndex(CommunityPackInstallationIndex index)
        {
            index = index ?? new CommunityPackInstallationIndex();
            index.Version = 1;
            index.Packs = index.Packs ?? new List<CommunityPackInstallation>();
            WriteTextAtomic(installationsPath, JsonConvert.SerializeObject(index, Formatting.Indented));
        }

        private void MigrateLegacyVisualInstallations(string pluginUserDataPath)
        {
            if (packType != "visual" || File.Exists(installationsPath)) return;
            try
            {
                var legacyOldRoot = Path.Combine(pluginUserDataPath ?? string.Empty, "VisualPacks", "Community", "installations.json");
                var legacyMovedRoot = Path.Combine(AnikiPackStorage.GetAreaRoot(pluginUserDataPath, "VisualPacks"), "Community", "installations.json");
                var legacy = File.Exists(legacyMovedRoot) ? legacyMovedRoot : legacyOldRoot;
                if (File.Exists(legacy))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(installationsPath));
                    File.Copy(legacy, installationsPath, false);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CommunityPacks] Legacy Visual Pack installation links could not be migrated.");
            }
        }

        private static void WriteTextAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, text ?? string.Empty, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(temporary, path, true);
                    File.Delete(temporary);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                TryDeleteFile(temporary);
            }
        }

        private static DateTime ParseDateSafe(string value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed : DateTime.MinValue;
        }

        private static bool TryGetHttpsUri(string value, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
                !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            uri = candidate;
            return true;
        }

        private static string GetSafeImageExtension(string path)
        {
            var extension = Path.GetExtension(path ?? string.Empty);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
            return ".jpg";
        }

        private static string MakeFileNameSafe(string value)
        {
            var result = string.IsNullOrWhiteSpace(value) ? "pack" : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) result = result.Replace(c, '_');
            return result;
        }

        private void CleanupOldPreviewVersions(CommunityPackCatalogItem item, string keepPath)
        {
            try
            {
                var prefix = MakeFileNameSafe(item.Type) + "-" + MakeFileNameSafe(item.Id) + "-";
                foreach (var file in Directory.GetFiles(previewCacheRoot, prefix + "*"))
                {
                    if (!string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase)) TryDeleteFile(file);
                }
            }
            catch { }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CommunityPackService));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            http.Dispose();
        }
    }
}
