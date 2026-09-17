using AnikiHelper.Services.Packs;
using AnikiHelper.Services.ColorPacks;
using AnikiHelper.Services.CommunityPacks;
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
using System.Security.Cryptography;
using System.Text;

namespace AnikiHelper.Services.CompletePacks
{
    public sealed class CompletePackImportResult
    {
        public string LocalId { get; set; }
        public string PackId { get; set; }
        public string PackName { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public bool WasAlreadyInLibrary { get; set; }
        public bool WasUpdated { get; set; }
    }

    public sealed class CompletePackApplySelection
    {
        public string CompletePackLocalId { get; set; }
        public string VisualPackLocalId { get; set; }
        public string ColorPackLocalId { get; set; }
        public string LoginPackLocalId { get; set; }
        public string SoundPackLocalId { get; set; }

        public bool HasVisualPack => !string.IsNullOrWhiteSpace(VisualPackLocalId);
        public bool HasColorPack => !string.IsNullOrWhiteSpace(ColorPackLocalId);
        public bool HasLoginPack => !string.IsNullOrWhiteSpace(LoginPackLocalId);
        public bool HasSoundPack => !string.IsNullOrWhiteSpace(SoundPackLocalId);
    }

    public sealed class CompletePackComponentPackIds
    {
        public string VisualPackId { get; set; }
        public string ColorPackId { get; set; }
        public string LoginPackId { get; set; }
        public string SoundPackId { get; set; }

        [JsonIgnore]
        public int Count => new[] { VisualPackId, ColorPackId, LoginPackId, SoundPackId }
            .Count(x => !string.IsNullOrWhiteSpace(x));
    }

    public sealed class CompletePackDeleteAnalysis
    {
        public CompletePackComponentPackIds ComponentPackIds { get; set; } = new CompletePackComponentPackIds();
        public List<string> SharedWithCompletePackNames { get; set; } = new List<string>();

        [JsonIgnore]
        public bool HasSharedComponents => SharedWithCompletePackNames != null && SharedWithCompletePackNames.Count > 0;
    }

    public sealed class CompletePackLibrarySnapshot
    {
        public int MaximumPacks { get; set; }
        public string ActivePackId { get; set; }
        public List<CompletePackLibraryPack> Packs { get; set; } = new List<CompletePackLibraryPack>();
    }

    public sealed class CompletePackLibraryPack
    {
        public string LocalId { get; set; }
        public string PackId { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string SourceFileName { get; set; }
        public string ContentHash { get; set; }
        public DateTime ImportedUtc { get; set; }

        [JsonIgnore]
        public bool IsActive { get; set; }

        [JsonIgnore]
        public string FolderPath { get; set; }

        [JsonIgnore]
        public long SizeBytes { get; set; }

        [JsonIgnore]
        public bool HasVisualPack { get; set; }

        [JsonIgnore]
        public bool HasColorPack { get; set; }

        [JsonIgnore]
        public bool HasLoginPack { get; set; }

        [JsonIgnore]
        public bool HasSoundPack { get; set; }
    }

    internal sealed class CompletePackLibraryIndex
    {
        public int Version { get; set; } = 1;
        public string ActivePackId { get; set; }
        public List<CompletePackLibraryPack> Packs { get; set; } = new List<CompletePackLibraryPack>();
    }

    internal sealed class CompletePackManifest
    {
        [JsonProperty("formatVersion")]
        public int FormatVersion { get; set; }

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

        [JsonProperty("components")]
        public CompletePackComponentsManifest Components { get; set; }
    }

    internal sealed class CompletePackComponentIdentityManifest
    {
        [JsonProperty("id")]
        public string Id { get; set; }
    }

    internal sealed class CompletePackComponentsManifest
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

    internal sealed class CompletePackImportService
    {
        public const int MaximumLibraryPacks = 20;

        private const int SupportedFormatVersion = 1;
        private const int MaximumArchiveEntries = 16;
        private const long MaximumManifestBytes = 64L * 1024L;
        private const long MaximumComponentArchiveBytes = 350L * 1024L * 1024L;
        private const long MaximumArchiveUncompressedBytes = 750L * 1024L * 1024L;
        private const string ManifestFileName = "completepack.json";
        private const string DefaultAuthor = "Unknown";
        private const string DefaultVisualComponentPath = "packs/visual.zip";
        private const string DefaultColorComponentPath = "packs/color.zip";
        private const string DefaultLoginComponentPath = "packs/login.zip";
        private const string DefaultSoundComponentPath = "packs/sound.zip";

        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly string pluginUserDataPath;
        private readonly string completePacksRoot;
        private readonly string libraryRoot;
        private readonly string indexFilePath;

        public CompletePackImportService(IPlayniteAPI api, string pluginUserDataPath, ILogger logger)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.logger = logger;
            this.pluginUserDataPath = pluginUserDataPath ?? string.Empty;
            completePacksRoot = AnikiPackStorage.GetAreaRoot(this.pluginUserDataPath, "CompletePacks");
            libraryRoot = Path.Combine(completePacksRoot, "Library");
            indexFilePath = Path.Combine(completePacksRoot, "index.json");
        }

        public CompletePackLibrarySnapshot GetLibrary()
        {
            EnsureLibraryFolders();
            var index = LoadIndex();
            if (RemoveMissingLibraryEntries(index))
            {
                SaveIndex(index);
            }

            PopulateRuntimeProperties(index);
            return new CompletePackLibrarySnapshot
            {
                MaximumPacks = MaximumLibraryPacks,
                ActivePackId = index.ActivePackId ?? string.Empty,
                Packs = index.Packs
                    .OrderByDescending(x => x.IsActive)
                    .ThenByDescending(x => x.ImportedUtc)
                    .ToList()
            };
        }

        public CompletePackImportResult Import(string zipFilePath)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("The selected Complete Pack ZIP file could not be found.", zipFilePath);
            }

            EnsureLibraryFolders();
            var stagingFolder = Path.Combine(completePacksRoot, ".import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingFolder);

            try
            {
                CompletePackManifest manifest;
                Dictionary<string, ZipArchiveEntry> componentEntries;

                using (var archive = ZipFile.OpenRead(zipFilePath))
                {
                    ValidateArchiveEnvelope(archive);
                    var manifestEntry = GetRequiredRootEntry(archive, ManifestFileName);
                    manifest = ReadManifest(manifestEntry);
                    ResolveDefaultComponentPaths(manifest, archive);
                    ValidateManifest(manifest);
                    componentEntries = ResolveComponentEntries(archive, manifest);

                    File.WriteAllText(
                        Path.Combine(stagingFolder, ManifestFileName),
                        JsonConvert.SerializeObject(manifest, Formatting.Indented),
                        new UTF8Encoding(false));
                    foreach (var pair in componentEntries)
                    {
                        var destination = GetStoredComponentPath(stagingFolder, pair.Key);
                        CopyEntry(pair.Value, destination);
                    }
                }

                ValidateStoredComponents(stagingFolder, manifest);

                var contentHash = ComputeFolderContentHash(stagingFolder);
                var index = LoadIndex();
                if (RemoveMissingLibraryEntries(index))
                {
                    SaveIndex(index);
                }

                var stablePackId = manifest.Id.Trim();
                var version = manifest.Version.Trim();
                var existing = index.Packs.FirstOrDefault(x =>
                    string.Equals(x.PackId, stablePackId, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (CommunityVisualPackService.CompareVersions(version, existing.Version) <= 0)
                    {
                        TryDeleteDirectory(stagingFolder);
                        return CreateImportResult(existing, true, false);
                    }

                    return UpdateExistingPack(index, existing, stagingFolder, zipFilePath, contentHash, manifest);
                }

                if (index.Packs.Count >= MaximumLibraryPacks)
                {
                    throw new InvalidOperationException(
                        $"The Complete Pack library is full ({MaximumLibraryPacks} packs maximum). Delete a pack before importing another one.");
                }

                var localId = CreateLocalId(stablePackId, index);
                var destinationFolder = Path.Combine(libraryRoot, localId);
                MoveDirectory(stagingFolder, destinationFolder);

                var record = new CompletePackLibraryPack
                {
                    LocalId = localId,
                    PackId = stablePackId,
                    Name = manifest.Name.Trim(),
                    Author = NormalizeAuthor(manifest.Author),
                    Version = version,
                    Description = manifest.Description?.Trim() ?? string.Empty,
                    SourceFileName = Path.GetFileName(zipFilePath) ?? string.Empty,
                    ContentHash = contentHash,
                    ImportedUtc = DateTime.UtcNow
                };

                index.Packs.Add(record);
                SaveIndex(index);
                logger?.Info($"[AnikiHelper][CompletePack] Imported '{record.Name}' ({record.LocalId}).");
                return CreateImportResult(record, false, false);
            }
            catch
            {
                TryDeleteDirectory(stagingFolder);
                throw;
            }
        }

        public CompletePackApplySelection PrepareApply(string localId)
        {
            var index = LoadIndex();
            var record = FindPack(index, localId);
            var folder = Path.Combine(libraryRoot, record.LocalId);
            var manifest = LoadStoredManifest(folder);
            ValidateManifest(manifest);

            var selection = new CompletePackApplySelection
            {
                CompletePackLocalId = record.LocalId
            };

            var visualHasSpecificPreview = false;
            var colorHasSpecificPreview = false;
            var loginHasSpecificPreview = false;
            var soundHasSpecificPreview = false;

            if (!string.IsNullOrWhiteSpace(manifest.Components?.Visual))
            {
                var path = GetStoredComponentPath(folder, NormalizeComponentPath(manifest.Components.Visual));
                var service = new VisualPackImportService(api, pluginUserDataPath, logger);
                selection.VisualPackLocalId = service.Import(path, false, false).LocalId;
                visualHasSpecificPreview = CommunityPackPreviewHelper.InheritPreviewFromPackArchive(
                    pluginUserDataPath, path, "visual", selection.VisualPackLocalId);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Components?.Color))
            {
                var path = GetStoredComponentPath(folder, NormalizeComponentPath(manifest.Components.Color));
                var service = new ColorPackImportService(api, pluginUserDataPath, logger);
                selection.ColorPackLocalId = service.Import(path, false).LocalId;
                colorHasSpecificPreview = CommunityPackPreviewHelper.InheritPreviewFromPackArchive(
                    pluginUserDataPath, path, "color", selection.ColorPackLocalId);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Components?.Login))
            {
                var path = GetStoredComponentPath(folder, NormalizeComponentPath(manifest.Components.Login));
                var service = new LoginPackImportService(api, pluginUserDataPath, logger);
                selection.LoginPackLocalId = service.Import(path, false).LocalId;
                loginHasSpecificPreview = CommunityPackPreviewHelper.InheritPreviewFromPackArchive(
                    pluginUserDataPath, path, "login", selection.LoginPackLocalId);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Components?.Sound))
            {
                var path = GetStoredComponentPath(folder, NormalizeComponentPath(manifest.Components.Sound));
                var service = new SoundPackImportService(api, pluginUserDataPath, logger);
                selection.SoundPackLocalId = service.Import(path, false).LocalId;
                soundHasSpecificPreview = CommunityPackPreviewHelper.InheritPreviewFromPackArchive(
                    pluginUserDataPath, path, "sound", selection.SoundPackLocalId);
            }

            // New Complete Packs carry one preview inside each nested pack. Use those
            // first. For legacy Complete Packs, keep the old behavior and reuse the
            // Complete Pack preview only when a child has no embedded preview.
            if (!visualHasSpecificPreview)
            {
                CommunityPackPreviewHelper.InheritPreviewFromInstalledCommunityPack(
                    pluginUserDataPath, "complete", record.LocalId, "visual", selection.VisualPackLocalId);
            }
            if (!colorHasSpecificPreview)
            {
                CommunityPackPreviewHelper.InheritPreviewFromInstalledCommunityPack(
                    pluginUserDataPath, "complete", record.LocalId, "color", selection.ColorPackLocalId);
            }
            if (!loginHasSpecificPreview)
            {
                CommunityPackPreviewHelper.InheritPreviewFromInstalledCommunityPack(
                    pluginUserDataPath, "complete", record.LocalId, "login", selection.LoginPackLocalId);
            }
            if (!soundHasSpecificPreview)
            {
                CommunityPackPreviewHelper.InheritPreviewFromInstalledCommunityPack(
                    pluginUserDataPath, "complete", record.LocalId, "sound", selection.SoundPackLocalId);
            }

            return selection;
        }

        public CompletePackComponentPackIds GetComponentPackIds(string localId)
        {
            EnsureLibraryFolders();
            var index = LoadIndex();
            if (RemoveMissingLibraryEntries(index))
            {
                SaveIndex(index);
            }

            var record = FindPack(index, localId);
            return GetComponentPackIds(record);
        }

        public CompletePackDeleteAnalysis AnalyzeDelete(string localId)
        {
            EnsureLibraryFolders();
            var index = LoadIndex();
            if (RemoveMissingLibraryEntries(index))
            {
                SaveIndex(index);
            }

            var record = FindPack(index, localId);
            var targetComponents = GetComponentPackIds(record);
            var sharedNames = new List<string>();

            foreach (var other in index.Packs.Where(x =>
                x != null &&
                !string.Equals(x.LocalId, record.LocalId, StringComparison.OrdinalIgnoreCase)))
            {
                CompletePackComponentPackIds otherComponents;
                try
                {
                    otherComponents = GetComponentPackIds(other);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to inspect components for '" + (other.Name ?? other.LocalId) + "'.");
                    continue;
                }

                if (SharesAnyComponent(targetComponents, otherComponents))
                {
                    var name = string.IsNullOrWhiteSpace(other.Name) ? other.LocalId : other.Name;
                    if (!string.IsNullOrWhiteSpace(name) &&
                        !sharedNames.Contains(name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        sharedNames.Add(name);
                    }
                }
            }

            return new CompletePackDeleteAnalysis
            {
                ComponentPackIds = targetComponents,
                SharedWithCompletePackNames = sharedNames
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };
        }

        private CompletePackComponentPackIds GetComponentPackIds(CompletePackLibraryPack record)
        {
            var result = new CompletePackComponentPackIds();
            if (record == null || string.IsNullOrWhiteSpace(record.LocalId))
            {
                return result;
            }

            var folder = Path.Combine(libraryRoot, record.LocalId);
            var manifest = LoadStoredManifest(folder);
            ValidateManifest(manifest);

            result.VisualPackId = ReadComponentPackId(folder, manifest.Components?.Visual, "visualpack.json");
            result.ColorPackId = ReadComponentPackId(folder, manifest.Components?.Color, "colorpack.json");
            result.LoginPackId = ReadComponentPackId(folder, manifest.Components?.Login, "loginpack.json");
            result.SoundPackId = ReadComponentPackId(folder, manifest.Components?.Sound, "soundpack.json");
            return result;
        }

        private string ReadComponentPackId(string completePackFolder, string componentRelativePath, string manifestFileName)
        {
            if (string.IsNullOrWhiteSpace(componentRelativePath))
            {
                return string.Empty;
            }

            try
            {
                var archivePath = GetStoredComponentPath(completePackFolder, NormalizeComponentPath(componentRelativePath));
                if (!File.Exists(archivePath))
                {
                    return string.Empty;
                }

                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    var entry = archive.Entries.FirstOrDefault(x =>
                        !string.IsNullOrEmpty(x.Name) &&
                        string.Equals(NormalizeArchivePath(x.FullName), manifestFileName, StringComparison.OrdinalIgnoreCase));
                    if (entry == null || entry.Length <= 0 || entry.Length > MaximumManifestBytes)
                    {
                        return string.Empty;
                    }

                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
                    {
                        var componentManifest = JsonConvert.DeserializeObject<CompletePackComponentIdentityManifest>(reader.ReadToEnd());
                        return componentManifest?.Id?.Trim() ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to read component identity from '" + componentRelativePath + "'.");
                return string.Empty;
            }
        }

        private static bool SharesAnyComponent(CompletePackComponentPackIds left, CompletePackComponentPackIds right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return SameNonEmptyId(left.VisualPackId, right.VisualPackId) ||
                   SameNonEmptyId(left.ColorPackId, right.ColorPackId) ||
                   SameNonEmptyId(left.LoginPackId, right.LoginPackId) ||
                   SameNonEmptyId(left.SoundPackId, right.SoundPackId);
        }

        private static bool SameNonEmptyId(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        public void SetActivePack(string localId)
        {
            var index = LoadIndex();
            var record = FindPack(index, localId);
            index.ActivePackId = record.LocalId;
            SaveIndex(index);
        }

        public void ClearActivePack()
        {
            var index = LoadIndex();
            if (string.IsNullOrWhiteSpace(index.ActivePackId))
            {
                return;
            }

            index.ActivePackId = string.Empty;
            SaveIndex(index);
        }

        public void Delete(string localId)
        {
            var index = LoadIndex();
            var record = FindPack(index, localId);
            var folder = Path.Combine(libraryRoot, record.LocalId);

            index.Packs.Remove(record);
            if (string.Equals(index.ActivePackId, record.LocalId, StringComparison.OrdinalIgnoreCase))
            {
                index.ActivePackId = string.Empty;
            }

            SaveIndex(index);
            TryDeleteDirectory(folder);
            logger?.Info($"[AnikiHelper][CompletePack] Deleted '{record.Name}' ({record.LocalId}). Component packs were left installed.");
        }

        public void Export(string localId, string destinationZipPath)
        {
            if (string.IsNullOrWhiteSpace(destinationZipPath))
            {
                throw new ArgumentException("An export destination is required.", nameof(destinationZipPath));
            }

            var index = LoadIndex();
            var record = FindPack(index, localId);
            var sourceFolder = Path.Combine(libraryRoot, record.LocalId);
            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException("The Complete Pack folder is missing.");
            }

            var parent = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (File.Exists(destinationZipPath))
            {
                File.Delete(destinationZipPath);
            }

            using (var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    archive.CreateEntryFromFile(file, relative.Replace('\\', '/'), CompressionLevel.Optimal);
                }
            }
        }

        public bool ActivePackContainsComponent(string componentName)
        {
            try
            {
                var index = LoadIndex();
                if (string.IsNullOrWhiteSpace(index.ActivePackId))
                {
                    return false;
                }

                var record = index.Packs.FirstOrDefault(x =>
                    string.Equals(x.LocalId, index.ActivePackId, StringComparison.OrdinalIgnoreCase));
                if (record == null)
                {
                    return false;
                }

                var manifest = LoadStoredManifest(Path.Combine(libraryRoot, record.LocalId));
                switch ((componentName ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "visual": return !string.IsNullOrWhiteSpace(manifest.Components?.Visual);
                    case "color": return !string.IsNullOrWhiteSpace(manifest.Components?.Color);
                    case "login": return !string.IsNullOrWhiteSpace(manifest.Components?.Login);
                    case "sound": return !string.IsNullOrWhiteSpace(manifest.Components?.Sound);
                    default: return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private CompletePackImportResult UpdateExistingPack(
            CompletePackLibraryIndex index,
            CompletePackLibraryPack existing,
            string stagingFolder,
            string sourceZipPath,
            string contentHash,
            CompletePackManifest manifest)
        {
            var destinationFolder = Path.Combine(libraryRoot, existing.LocalId);
            var backupFolder = destinationFolder + ".backup-" + Guid.NewGuid().ToString("N");

            try
            {
                if (Directory.Exists(destinationFolder))
                {
                    Directory.Move(destinationFolder, backupFolder);
                }

                MoveDirectory(stagingFolder, destinationFolder);
                existing.PackId = manifest.Id.Trim();
                existing.Name = manifest.Name.Trim();
                existing.Author = NormalizeAuthor(manifest.Author);
                existing.Version = manifest.Version.Trim();
                existing.Description = manifest.Description?.Trim() ?? string.Empty;
                existing.SourceFileName = Path.GetFileName(sourceZipPath) ?? string.Empty;
                existing.ContentHash = contentHash;
                existing.ImportedUtc = DateTime.UtcNow;
                if (string.Equals(index.ActivePackId, existing.LocalId, StringComparison.OrdinalIgnoreCase))
                {
                    // The library now contains a newer bundle than the one that was actually
                    // applied. Require an explicit Apply so the UI never labels unapplied
                    // component changes as active.
                    index.ActivePackId = string.Empty;
                }
                SaveIndex(index);
                TryDeleteDirectory(backupFolder);

                logger?.Info($"[AnikiHelper][CompletePack] Updated '{existing.Name}' ({existing.LocalId}) to {existing.Version}.");
                return CreateImportResult(existing, false, true);
            }
            catch
            {
                TryDeleteDirectory(destinationFolder);
                if (Directory.Exists(backupFolder))
                {
                    Directory.Move(backupFolder, destinationFolder);
                }

                throw;
            }
        }

        private void ValidateStoredComponents(string stagingFolder, CompletePackManifest manifest)
        {
            var validationRoot = Path.Combine(stagingFolder, ".validation");
            Directory.CreateDirectory(validationRoot);

            try
            {
                if (!string.IsNullOrWhiteSpace(manifest.Components?.Visual))
                {
                    var path = GetStoredComponentPath(stagingFolder, NormalizeComponentPath(manifest.Components.Visual));
                    new VisualPackImportService(api, Path.Combine(validationRoot, "visual"), logger).Import(path, false, false);
                }

                if (!string.IsNullOrWhiteSpace(manifest.Components?.Color))
                {
                    var path = GetStoredComponentPath(stagingFolder, NormalizeComponentPath(manifest.Components.Color));
                    new ColorPackImportService(api, Path.Combine(validationRoot, "color"), logger).Import(path, false);
                }

                if (!string.IsNullOrWhiteSpace(manifest.Components?.Login))
                {
                    var path = GetStoredComponentPath(stagingFolder, NormalizeComponentPath(manifest.Components.Login));
                    new LoginPackImportService(api, Path.Combine(validationRoot, "login"), logger).Import(path, false);
                }

                if (!string.IsNullOrWhiteSpace(manifest.Components?.Sound))
                {
                    var path = GetStoredComponentPath(stagingFolder, NormalizeComponentPath(manifest.Components.Sound));
                    new SoundPackImportService(api, Path.Combine(validationRoot, "sound"), logger).Import(path, false);
                }
            }
            finally
            {
                TryDeleteDirectory(validationRoot);
            }
        }

        private static CompletePackImportResult CreateImportResult(CompletePackLibraryPack record, bool alreadyInstalled, bool updated)
        {
            return new CompletePackImportResult
            {
                LocalId = record.LocalId,
                PackId = record.PackId,
                PackName = record.Name,
                Author = record.Author,
                Version = record.Version,
                Description = record.Description,
                WasAlreadyInLibrary = alreadyInstalled,
                WasUpdated = updated
            };
        }

        private void PopulateRuntimeProperties(CompletePackLibraryIndex index)
        {
            foreach (var pack in index.Packs)
            {
                var folder = Path.Combine(libraryRoot, pack.LocalId ?? string.Empty);
                pack.FolderPath = folder;
                pack.IsActive = string.Equals(index.ActivePackId, pack.LocalId, StringComparison.OrdinalIgnoreCase);
                pack.SizeBytes = GetDirectorySize(folder);

                try
                {
                    var manifest = LoadStoredManifest(folder);
                    pack.HasVisualPack = !string.IsNullOrWhiteSpace(manifest.Components?.Visual);
                    pack.HasColorPack = !string.IsNullOrWhiteSpace(manifest.Components?.Color);
                    pack.HasLoginPack = !string.IsNullOrWhiteSpace(manifest.Components?.Login);
                    pack.HasSoundPack = !string.IsNullOrWhiteSpace(manifest.Components?.Sound);
                }
                catch
                {
                    pack.HasVisualPack = false;
                    pack.HasColorPack = false;
                    pack.HasLoginPack = false;
                    pack.HasSoundPack = false;
                }
            }
        }

        private void ResolveDefaultComponentPaths(CompletePackManifest manifest, ZipArchive archive)
        {
            if (manifest.Components == null)
            {
                manifest.Components = new CompletePackComponentsManifest();
            }

            if (string.IsNullOrWhiteSpace(manifest.Components.Visual) && FindEntry(archive, DefaultVisualComponentPath) != null)
            {
                manifest.Components.Visual = DefaultVisualComponentPath;
            }
            if (string.IsNullOrWhiteSpace(manifest.Components.Color) && FindEntry(archive, DefaultColorComponentPath) != null)
            {
                manifest.Components.Color = DefaultColorComponentPath;
            }
            if (string.IsNullOrWhiteSpace(manifest.Components.Login) && FindEntry(archive, DefaultLoginComponentPath) != null)
            {
                manifest.Components.Login = DefaultLoginComponentPath;
            }
            if (string.IsNullOrWhiteSpace(manifest.Components.Sound) && FindEntry(archive, DefaultSoundComponentPath) != null)
            {
                manifest.Components.Sound = DefaultSoundComponentPath;
            }
        }

        private static Dictionary<string, ZipArchiveEntry> ResolveComponentEntries(ZipArchive archive, CompletePackManifest manifest)
        {
            var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            AddComponentEntry(result, archive, manifest.Components?.Visual, "Visual Pack");
            AddComponentEntry(result, archive, manifest.Components?.Color, "Color Pack");
            AddComponentEntry(result, archive, manifest.Components?.Login, "Login Pack");
            AddComponentEntry(result, archive, manifest.Components?.Sound, "Sound Pack");
            return result;
        }

        private static void AddComponentEntry(Dictionary<string, ZipArchiveEntry> result, ZipArchive archive, string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = NormalizeComponentPath(path);
            if (result.ContainsKey(normalized))
            {
                throw new InvalidDataException("completepack.json references the same component archive more than once.");
            }

            var entry = FindEntry(archive, normalized);
            if (entry == null || string.IsNullOrEmpty(entry.Name))
            {
                throw new InvalidDataException(label + " component is missing from the Complete Pack: " + normalized);
            }

            if (entry.Length <= 0 || entry.Length > MaximumComponentArchiveBytes)
            {
                throw new InvalidDataException(label + " component has an invalid size.");
            }

            result[normalized] = entry;
        }

        private static ZipArchiveEntry FindEntry(ZipArchive archive, string normalizedPath)
        {
            return archive.Entries.FirstOrDefault(x =>
                string.Equals(NormalizeArchivePath(x.FullName), NormalizeArchivePath(normalizedPath), StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateArchiveEnvelope(ZipArchive archive)
        {
            if (archive.Entries.Count > MaximumArchiveEntries)
            {
                throw new InvalidDataException("The Complete Pack contains too many archive entries.");
            }

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var path = NormalizeArchivePath(entry.FullName);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (path.StartsWith("../", StringComparison.Ordinal) || path.Contains("/../") || path.Contains(":"))
                {
                    throw new InvalidDataException("The Complete Pack contains an unsafe archive path.");
                }

                total += Math.Max(0L, entry.Length);
                if (total > MaximumArchiveUncompressedBytes)
                {
                    throw new InvalidDataException("The Complete Pack is too large when extracted.");
                }
            }
        }

        private static ZipArchiveEntry GetRequiredRootEntry(ZipArchive archive, string fileName)
        {
            var matches = archive.Entries
                .Where(x => !string.IsNullOrEmpty(x.Name) &&
                            string.Equals(NormalizeArchivePath(x.FullName), fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count != 1)
            {
                throw new InvalidDataException("The Complete Pack must contain exactly one " + fileName + " at the ZIP root.");
            }

            if (matches[0].Length <= 0 || matches[0].Length > MaximumManifestBytes)
            {
                throw new InvalidDataException(fileName + " has an invalid size.");
            }

            return matches[0];
        }

        private static CompletePackManifest ReadManifest(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
            {
                var json = reader.ReadToEnd();
                var manifest = JsonConvert.DeserializeObject<CompletePackManifest>(json);
                if (manifest == null)
                {
                    throw new InvalidDataException("completepack.json is empty or invalid.");
                }

                return manifest;
            }
        }

        private static CompletePackManifest LoadStoredManifest(string folder)
        {
            var path = Path.Combine(folder ?? string.Empty, ManifestFileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The Complete Pack manifest is missing.", path);
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var manifest = JsonConvert.DeserializeObject<CompletePackManifest>(json);
            if (manifest == null)
            {
                throw new InvalidDataException("completepack.json is empty or invalid.");
            }

            return manifest;
        }

        private static void ValidateManifest(CompletePackManifest manifest)
        {
            if (manifest.FormatVersion != SupportedFormatVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported Complete Pack formatVersion '{manifest.FormatVersion}'. Expected {SupportedFormatVersion}.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.Type))
            {
                var normalizedType = new string(manifest.Type
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
                if (!normalizedType.Contains("complete"))
                {
                    throw new InvalidDataException("completepack.json has an invalid pack type.");
                }
            }

            ValidateManifestText(manifest.Id, "id", 120);
            ValidateManifestText(manifest.Name, "name", 160);
            ValidateOptionalManifestText(manifest.Author, "author", 160);
            ValidateManifestText(manifest.Version, "version", 64);

            if (!string.IsNullOrWhiteSpace(manifest.Description) && manifest.Description.Length > 1000)
            {
                throw new InvalidDataException("completepack.json description is too long.");
            }

            if (manifest.Components == null)
            {
                throw new InvalidDataException("completepack.json does not contain any component.");
            }

            var componentPaths = new[]
            {
                manifest.Components.Visual,
                manifest.Components.Color,
                manifest.Components.Login,
                manifest.Components.Sound
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeComponentPath)
            .ToList();

            if (componentPaths.Count == 0)
            {
                throw new InvalidDataException("A Complete Pack must contain at least one component pack.");
            }

            if (componentPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != componentPaths.Count)
            {
                throw new InvalidDataException("completepack.json contains duplicate component paths.");
            }
        }

        private static string NormalizeComponentPath(string value)
        {
            var normalized = NormalizeArchivePath(value).TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized.StartsWith("../", StringComparison.Ordinal) ||
                normalized.Contains("/../") ||
                normalized.Contains(":") ||
                !normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("completepack.json contains an invalid component ZIP path.");
            }

            return normalized;
        }

        private static void ValidateManifestText(string value, string fieldName, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("completepack.json is missing required field: " + fieldName);
            }

            if (value.Trim().Length > maximumLength)
            {
                throw new InvalidDataException("completepack.json field is too long: " + fieldName);
            }
        }

        private static void ValidateOptionalManifestText(string value, string fieldName, int maximumLength)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maximumLength)
            {
                throw new InvalidDataException("completepack.json field is too long: " + fieldName);
            }
        }

        private static string NormalizeAuthor(string author)
        {
            return string.IsNullOrWhiteSpace(author) ? DefaultAuthor : author.Trim();
        }

        private static string NormalizeArchivePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim();
        }

        private static string GetStoredComponentPath(string root, string relativePath)
        {
            var normalized = NormalizeComponentPath(relativePath);
            var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var path = root;
            foreach (var segment in segments)
            {
                path = Path.Combine(path, segment);
            }
            return path;
        }

        private static void CopyEntry(ZipArchiveEntry entry, string destinationPath)
        {
            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using (var source = entry.Open())
            using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
            }
        }

        private CompletePackLibraryPack FindPack(CompletePackLibraryIndex index, string localId)
        {
            if (string.IsNullOrWhiteSpace(localId))
            {
                throw new ArgumentException("A Complete Pack id is required.", nameof(localId));
            }

            var record = index.Packs.FirstOrDefault(x =>
                string.Equals(x.LocalId, localId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                throw new InvalidOperationException("The selected Complete Pack is no longer installed.");
            }

            return record;
        }

        private bool RemoveMissingLibraryEntries(CompletePackLibraryIndex index)
        {
            var removed = index.Packs.RemoveAll(x =>
                x == null ||
                string.IsNullOrWhiteSpace(x.LocalId) ||
                !Directory.Exists(Path.Combine(libraryRoot, x.LocalId))) > 0;

            if (!string.IsNullOrWhiteSpace(index.ActivePackId) &&
                !index.Packs.Any(x => string.Equals(x.LocalId, index.ActivePackId, StringComparison.OrdinalIgnoreCase)))
            {
                index.ActivePackId = string.Empty;
                removed = true;
            }

            return removed;
        }

        private CompletePackLibraryIndex LoadIndex()
        {
            EnsureLibraryFolders();
            try
            {
                if (!File.Exists(indexFilePath))
                {
                    return new CompletePackLibraryIndex();
                }

                var json = File.ReadAllText(indexFilePath, Encoding.UTF8);
                var index = JsonConvert.DeserializeObject<CompletePackLibraryIndex>(json) ?? new CompletePackLibraryIndex();
                index.Packs = index.Packs ?? new List<CompletePackLibraryPack>();
                return index;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][CompletePack] Failed to read library index; starting with an empty library.");
                return new CompletePackLibraryIndex();
            }
        }

        private void SaveIndex(CompletePackLibraryIndex index)
        {
            EnsureLibraryFolders();
            var json = JsonConvert.SerializeObject(index, Formatting.Indented);
            var temporary = indexFilePath + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(indexFilePath))
            {
                File.Delete(indexFilePath);
            }
            File.Move(temporary, indexFilePath);
        }

        private void EnsureLibraryFolders()
        {
            Directory.CreateDirectory(completePacksRoot);
            Directory.CreateDirectory(libraryRoot);
        }

        private string CreateLocalId(string stablePackId, CompletePackLibraryIndex index)
        {
            var safe = new string((stablePackId ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-')
                .ToArray());
            safe = string.Join("-", safe.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safe))
            {
                safe = "complete-pack";
            }
            if (safe.Length > 64)
            {
                safe = safe.Substring(0, 64).Trim('-');
            }

            var candidate = safe;
            var suffix = 2;
            while (index.Packs.Any(x => string.Equals(x.LocalId, candidate, StringComparison.OrdinalIgnoreCase)) ||
                   Directory.Exists(Path.Combine(libraryRoot, candidate)))
            {
                candidate = safe + "-" + suffix.ToString();
                suffix++;
            }
            return candidate;
        }

        private static string ComputeFolderContentHash(string folder)
        {
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[64 * 1024];
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(x => x.IndexOf(Path.DirectorySeparatorChar + ".validation" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var relative = file.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    var nameBytes = Encoding.UTF8.GetBytes(relative.ToLowerInvariant());
                    sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);

                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            sha.TransformBlock(buffer, 0, read, null, 0);
                        }
                    }
                }

                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", string.Empty);
            }
        }

        private static long GetDirectorySize(string folder)
        {
            try
            {
                return Directory.Exists(folder)
                    ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length)
                    : 0L;
            }
            catch
            {
                return 0L;
            }
        }

        private static void MoveDirectory(string source, string destination)
        {
            if (Directory.Exists(destination))
            {
                throw new IOException("A Complete Pack folder with the same id already exists.");
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            Directory.Move(source, destination);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
