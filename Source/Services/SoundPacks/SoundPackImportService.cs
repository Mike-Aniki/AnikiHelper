using AnikiHelper.Services.Packs;
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

namespace AnikiHelper.Services.SoundPacks
{
    public sealed class SoundPackImportResult
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

    public sealed class SoundPackLibrarySnapshot
    {
        public int MaximumPacks { get; set; }
        public string ActivePackId { get; set; }
        public List<SoundPackLibraryPack> Packs { get; set; } = new List<SoundPackLibraryPack>();
    }

    public sealed class SoundPackLibraryPack
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
    }

    internal sealed class SoundPackLibraryIndex
    {
        public int Version { get; set; } = 1;
        public string ActivePackId { get; set; }
        public List<SoundPackLibraryPack> Packs { get; set; } = new List<SoundPackLibraryPack>();
    }

    internal sealed class SoundPackManifest
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
    }

    internal sealed class SoundPackImportService
    {
        public const int MaximumLibraryPacks = 20;

        private const int SupportedFormatVersion = 1;
        private const int MaximumArchiveEntries = 26;
        private const long MaximumManifestBytes = 64L * 1024L;
        private const long MaximumSingleAudioBytes = 100L * 1024L * 1024L;
        private const long MaximumTotalAudioBytes = 300L * 1024L * 1024L;
        private const string ManifestFileName = "soundpack.json";
        private const string DefaultAuthor = "Unknown";

        public static readonly IReadOnlyList<string> SupportedAudioFiles = new[]
        {
            "navigation.wav",
            "activation.wav",
            "Noti.wav",
            "EnterGameDetails.wav",
            "ExitGameDetails.wav",
            "LoginConfirm.wav",
            "OpenPanel.wav",
            "OpenAdditionalView.wav",
            "CloseAdditionalView.wav",
            "HomeHubOpen.wav",
            "HomeHubClose.wav",
            "SessionSummary.wav",
            "Warning.wav",
            "ApplicationStopped.wav",
            "GameStarting.wav",
            "GameStarted.wav",
            "GameStopped.wav",
            "GameInstalled.wav",
            "GameUninstalled.wav",
            "LibraryUpdated.wav",
            "LoginOST.mp3",
            "HubOST.mp3",
            "SecondaryViewsOST.mp3",
            "ScreenSaverOST.mp3"
        };

        private static readonly HashSet<string> SupportedAudioFileSet =
            new HashSet<string>(SupportedAudioFiles, StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<string, string> SupportedAudioRelativePathByFileName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["navigation.wav"] = "audio/navigation.wav",
                ["activation.wav"] = "audio/activation.wav",
                ["Noti.wav"] = "audio/Noti.wav",
                ["EnterGameDetails.wav"] = "audio/EnterGameDetails.wav",
                ["ExitGameDetails.wav"] = "audio/ExitGameDetails.wav",
                ["LoginConfirm.wav"] = "audio/LoginConfirm.wav",
                ["OpenPanel.wav"] = "audio/OpenPanel.wav",
                ["OpenAdditionalView.wav"] = "audio/OpenAdditionalView.wav",
                ["CloseAdditionalView.wav"] = "audio/CloseAdditionalView.wav",
                ["HomeHubOpen.wav"] = "audio/HomeHubOpen.wav",
                ["HomeHubClose.wav"] = "audio/HomeHubClose.wav",
                ["SessionSummary.wav"] = "audio/SessionSummary.wav",
                ["Warning.wav"] = "audio/Warning.wav",
                ["ApplicationStopped.wav"] = "audio/Events/ApplicationStopped.wav",
                ["GameStarting.wav"] = "audio/Events/GameStarting.wav",
                ["GameStarted.wav"] = "audio/Events/GameStarted.wav",
                ["GameStopped.wav"] = "audio/Events/GameStopped.wav",
                ["GameInstalled.wav"] = "audio/Events/GameInstalled.wav",
                ["GameUninstalled.wav"] = "audio/Events/GameUninstalled.wav",
                ["LibraryUpdated.wav"] = "audio/Events/LibraryUpdated.wav",
                ["LoginOST.mp3"] = "audio/LoginOST.mp3",
                ["HubOST.mp3"] = "audio/HubOST.mp3",
                ["SecondaryViewsOST.mp3"] = "audio/SecondaryViewsOST.mp3",
                ["ScreenSaverOST.mp3"] = "audio/ScreenSaverOST.mp3"
            };

        private static readonly HashSet<string> SupportedAudioRelativePathSet =
            new HashSet<string>(SupportedAudioRelativePathByFileName.Values, StringComparer.OrdinalIgnoreCase);

        // Compatibility only: older Sound Packs / Creator builds may still contain this file.
        // It is accepted during import so existing packs do not suddenly fail, but it is no
        // longer exposed as a runtime sound and is omitted when a pack is exported again.
        private static readonly HashSet<string> ImportAcceptedAudioRelativePathSet =
            new HashSet<string>(SupportedAudioRelativePathSet, StringComparer.OrdinalIgnoreCase)
            {
                "audio/ChangeDisplay.wav"
            };

        private readonly ILogger logger;
        private readonly string soundPacksRoot;
        private readonly string libraryRoot;
        private readonly string indexFilePath;

        public SoundPackImportService(IPlayniteAPI api, string pluginUserDataPath, ILogger logger)
        {
            if (api == null)
            {
                throw new ArgumentNullException(nameof(api));
            }

            this.logger = logger;
            soundPacksRoot = AnikiPackStorage.GetAreaRoot(pluginUserDataPath, "SoundPacks");
            libraryRoot = Path.Combine(soundPacksRoot, "Library");
            indexFilePath = Path.Combine(soundPacksRoot, "index.json");
        }

        public SoundPackLibrarySnapshot GetLibrary()
        {
            EnsureLibraryFolders();
            var index = LoadIndex();
            if (RemoveMissingLibraryEntries(index))
            {
                SaveIndex(index);
            }

            PopulateRuntimeProperties(index);
            return new SoundPackLibrarySnapshot
            {
                MaximumPacks = MaximumLibraryPacks,
                ActivePackId = index.ActivePackId ?? string.Empty,
                Packs = index.Packs
                    .OrderByDescending(x => x.IsActive)
                    .ThenByDescending(x => x.ImportedUtc)
                    .ToList()
            };
        }

        public SoundPackImportResult Import(string zipFilePath, bool activateImportedPack = false)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("The selected Sound Pack ZIP file could not be found.", zipFilePath);
            }

            EnsureLibraryFolders();
            var stagingFolder = Path.Combine(soundPacksRoot, ".import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingFolder);

            try
            {
                SoundPackManifest manifest;
                List<ZipArchiveEntry> audioEntries;

                using (var archive = ZipFile.OpenRead(zipFilePath))
                {
                    ValidateArchiveEnvelope(archive);
                    var manifestEntry = GetRequiredRootEntry(archive, ManifestFileName);
                    manifest = ReadManifest(manifestEntry);
                    ValidateManifest(manifest);

                    audioEntries = archive.Entries
                        .Where(x => !string.IsNullOrEmpty(x.Name) &&
                                    ImportAcceptedAudioRelativePathSet.Contains(NormalizeArchivePath(x.FullName)))
                        .ToList();

                    ValidateAudioEntries(audioEntries);
                    CopyEntry(manifestEntry, Path.Combine(stagingFolder, ManifestFileName));
                    foreach (var entry in audioEntries)
                    {
                        var relativePath = NormalizeArchivePath(entry.FullName);
                        CopyEntry(entry, GetStoredAudioPath(stagingFolder, relativePath));
                    }
                }

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
                        if (activateImportedPack)
                        {
                            SetActivePack(existing.LocalId);
                        }

                        return CreateImportResult(existing, true, false);
                    }

                    var result = UpdateExistingPack(index, existing, stagingFolder, zipFilePath, contentHash, manifest);
                    if (activateImportedPack)
                    {
                        SetActivePack(existing.LocalId);
                    }

                    return result;
                }

                if (index.Packs.Count >= MaximumLibraryPacks)
                {
                    throw new InvalidOperationException(
                        $"The Sound Pack library is full ({MaximumLibraryPacks}/{MaximumLibraryPacks}). Delete a pack before importing another one.");
                }

                var localId = CreateLocalId(contentHash, index);
                var destinationFolder = Path.Combine(libraryRoot, localId);
                Directory.Move(stagingFolder, destinationFolder);

                var record = CreateLibraryRecord(localId, zipFilePath, contentHash, manifest);
                index.Packs.Add(record);
                SaveIndex(index);

                if (activateImportedPack)
                {
                    SetActivePack(localId);
                }

                logger?.Info($"[AnikiHelper][SoundPack] Imported '{record.Name}' ({record.Version}).");
                return CreateImportResult(record, false, false);
            }
            catch
            {
                TryDeleteDirectory(stagingFolder);
                throw;
            }
        }

        public void SetActivePack(string localId)
        {
            if (string.IsNullOrWhiteSpace(localId))
            {
                ClearActivePack();
                return;
            }

            EnsureLibraryFolders();
            var index = LoadIndex();
            if (RemoveMissingLibraryEntries(index))
            {
                SaveIndex(index);
            }

            var record = FindPack(index, localId);
            ValidateStoredPack(Path.Combine(libraryRoot, record.LocalId));
            if (string.Equals(index.ActivePackId, record.LocalId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            index.ActivePackId = record.LocalId;
            SaveIndex(index);
        }

        public void ClearActivePack()
        {
            EnsureLibraryFolders();
            var index = LoadIndex();
            if (string.IsNullOrWhiteSpace(index.ActivePackId))
            {
                return;
            }

            index.ActivePackId = string.Empty;
            SaveIndex(index);
        }

        public string GetAudioPath(string localId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(localId) || !SupportedAudioFileSet.Contains(fileName ?? string.Empty))
            {
                return string.Empty;
            }

            EnsureLibraryFolders();
            var index = LoadIndex();
            if (RemoveMissingLibraryEntries(index))
            {
                SaveIndex(index);
            }

            var record = FindPack(index, localId);
            var folder = Path.Combine(libraryRoot, record.LocalId);
            ValidateStoredPack(folder);
            var path = GetStoredAudioPath(folder, GetRelativeAudioPath(fileName));
            return File.Exists(path) ? path : string.Empty;
        }

        public string GetActiveAudioPath(string fileName)
        {
            if (!SupportedAudioFileSet.Contains(fileName ?? string.Empty))
            {
                return string.Empty;
            }

            EnsureLibraryFolders();
            var index = LoadIndex();
            if (RemoveMissingLibraryEntries(index))
            {
                SaveIndex(index);
            }

            if (string.IsNullOrWhiteSpace(index.ActivePackId))
            {
                return string.Empty;
            }

            var record = index.Packs.FirstOrDefault(x =>
                string.Equals(x.LocalId, index.ActivePackId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                return string.Empty;
            }

            var folder = Path.Combine(libraryRoot, record.LocalId);
            if (!Directory.Exists(folder))
            {
                return string.Empty;
            }

            var path = GetStoredAudioPath(folder, GetRelativeAudioPath(fileName));
            return File.Exists(path) ? path : string.Empty;
        }

        public void Delete(string localId)
        {
            EnsureLibraryFolders();
            var index = LoadIndex();
            var record = FindPack(index, localId);
            var folder = Path.Combine(libraryRoot, record.LocalId);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            index.Packs.Remove(record);
            if (string.Equals(index.ActivePackId, record.LocalId, StringComparison.OrdinalIgnoreCase))
            {
                index.ActivePackId = string.Empty;
            }

            SaveIndex(index);
            logger?.Info($"[AnikiHelper][SoundPack] Deleted '{record.Name}'.");
        }

        public void Export(string localId, string destinationZipPath)
        {
            if (string.IsNullOrWhiteSpace(destinationZipPath))
            {
                throw new ArgumentException("A destination ZIP path is required.", nameof(destinationZipPath));
            }

            EnsureLibraryFolders();
            var index = LoadIndex();
            var record = FindPack(index, localId);
            var folder = Path.Combine(libraryRoot, record.LocalId);
            ValidateStoredPack(folder);

            var destinationDirectory = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(destinationZipPath))
            {
                File.Delete(destinationZipPath);
            }

            using (var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create))
            {
                AddFileToArchive(archive, Path.Combine(folder, ManifestFileName), ManifestFileName);
                foreach (var fileName in SupportedAudioFiles)
                {
                    var relativePath = GetRelativeAudioPath(fileName);
                    var path = GetStoredAudioPath(folder, relativePath);
                    if (File.Exists(path))
                    {
                        AddFileToArchive(archive, path, relativePath);
                    }
                }
            }
        }

        private SoundPackImportResult UpdateExistingPack(
            SoundPackLibraryIndex index,
            SoundPackLibraryPack existing,
            string stagingFolder,
            string zipFilePath,
            string contentHash,
            SoundPackManifest manifest)
        {
            var destinationFolder = Path.Combine(libraryRoot, existing.LocalId);
            var backupFolder = destinationFolder + ".backup-" + Guid.NewGuid().ToString("N");

            try
            {
                if (Directory.Exists(destinationFolder))
                {
                    Directory.Move(destinationFolder, backupFolder);
                }

                Directory.Move(stagingFolder, destinationFolder);
                var updated = CreateLibraryRecord(existing.LocalId, zipFilePath, contentHash, manifest);
                CopyRecord(updated, existing);
                SaveIndex(index);
                TryDeleteDirectory(backupFolder);
                logger?.Info($"[AnikiHelper][SoundPack] Updated '{existing.Name}' to {existing.Version}.");
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

        private SoundPackLibraryPack CreateLibraryRecord(
            string localId,
            string zipFilePath,
            string contentHash,
            SoundPackManifest manifest)
        {
            return new SoundPackLibraryPack
            {
                LocalId = localId,
                PackId = manifest.Id.Trim(),
                Name = manifest.Name.Trim(),
                Author = NormalizeAuthor(manifest.Author),
                Version = manifest.Version.Trim(),
                Description = manifest.Description?.Trim() ?? string.Empty,
                SourceFileName = Path.GetFileName(zipFilePath) ?? string.Empty,
                ContentHash = contentHash ?? string.Empty,
                ImportedUtc = DateTime.UtcNow
            };
        }

        private static void CopyRecord(SoundPackLibraryPack source, SoundPackLibraryPack target)
        {
            target.PackId = source.PackId;
            target.Name = source.Name;
            target.Author = source.Author;
            target.Version = source.Version;
            target.Description = source.Description;
            target.SourceFileName = source.SourceFileName;
            target.ContentHash = source.ContentHash;
            target.ImportedUtc = source.ImportedUtc;
        }

        private static SoundPackImportResult CreateImportResult(SoundPackLibraryPack record, bool alreadyInstalled, bool updated)
        {
            return new SoundPackImportResult
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

        private static void ValidateArchiveEnvelope(ZipArchive archive)
        {
            if (archive == null)
            {
                throw new InvalidDataException("The Sound Pack ZIP could not be opened.");
            }

            var fileEntries = archive.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).ToList();
            if (fileEntries.Count < 2 || fileEntries.Count > MaximumArchiveEntries)
            {
                throw new InvalidDataException("The Sound Pack ZIP contains an invalid number of files.");
            }

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in fileEntries)
            {
                var normalized = NormalizeArchivePath(entry.FullName);
                if (string.IsNullOrWhiteSpace(normalized) ||
                    normalized.StartsWith("/", StringComparison.Ordinal) ||
                    normalized.StartsWith("../", StringComparison.Ordinal) ||
                    normalized.Contains("/../") ||
                    normalized.Contains(":") ||
                    normalized.EndsWith("/", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The Sound Pack contains an invalid file path.");
                }

                if (!seenPaths.Add(normalized))
                {
                    throw new InvalidDataException("The Sound Pack contains a duplicate file: " + normalized);
                }

                var isEmbeddedPreview =
                    string.Equals(normalized, "preview.jpg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, "preview.png", StringComparison.OrdinalIgnoreCase);

                if (!string.Equals(normalized, ManifestFileName, StringComparison.OrdinalIgnoreCase) &&
                    !isEmbeddedPreview &&
                    !ImportAcceptedAudioRelativePathSet.Contains(normalized))
                {
                    throw new InvalidDataException("The Sound Pack contains an unsupported file: " + normalized);
                }
            }
        }

        private static ZipArchiveEntry GetRequiredRootEntry(ZipArchive archive, string fileName)
        {
            var entry = archive.Entries.FirstOrDefault(x =>
                !string.IsNullOrEmpty(x.Name) &&
                string.Equals(x.FullName.Replace('\\', '/'), fileName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                throw new InvalidDataException("The Sound Pack is missing required file: " + fileName);
            }

            return entry;
        }

        private static SoundPackManifest ReadManifest(ZipArchiveEntry entry)
        {
            if (entry.Length <= 0 || entry.Length > MaximumManifestBytes)
            {
                throw new InvalidDataException("soundpack.json has an invalid size.");
            }

            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
            {
                var manifest = JsonConvert.DeserializeObject<SoundPackManifest>(reader.ReadToEnd());
                if (manifest == null)
                {
                    throw new InvalidDataException("soundpack.json could not be read.");
                }

                return manifest;
            }
        }

        private static void ValidateManifest(SoundPackManifest manifest)
        {
            if (manifest.FormatVersion != SupportedFormatVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported Sound Pack formatVersion '{manifest.FormatVersion}'. Expected {SupportedFormatVersion}.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.Type))
            {
                var normalizedType = new string(manifest.Type
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
                if (!normalizedType.Contains("sound") && !normalizedType.Contains("audio"))
                {
                    throw new InvalidDataException("soundpack.json has an invalid pack type.");
                }
            }

            ValidateManifestText(manifest.Id, "id", 120);
            ValidateManifestText(manifest.Name, "name", 160);
            ValidateOptionalManifestText(manifest.Author, "author", 160);
            ValidateManifestText(manifest.Version, "version", 64);

            if (!string.IsNullOrWhiteSpace(manifest.Description) && manifest.Description.Length > 1000)
            {
                throw new InvalidDataException("soundpack.json description is too long.");
            }
        }

        private static void ValidateManifestText(string value, string fieldName, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("soundpack.json is missing required field: " + fieldName);
            }

            if (value.Trim().Length > maximumLength)
            {
                throw new InvalidDataException("soundpack.json field is too long: " + fieldName);
            }
        }

        private static void ValidateOptionalManifestText(string value, string fieldName, int maximumLength)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maximumLength)
            {
                throw new InvalidDataException("soundpack.json field is too long: " + fieldName);
            }
        }

        private static string NormalizeAuthor(string author)
        {
            return string.IsNullOrWhiteSpace(author) ? DefaultAuthor : author.Trim();
        }

        private static void ValidateAudioEntries(List<ZipArchiveEntry> audioEntries)
        {
            if (audioEntries == null || audioEntries.Count == 0)
            {
                throw new InvalidDataException("The Sound Pack does not contain any supported audio file.");
            }

            long total = 0;
            foreach (var entry in audioEntries)
            {
                if (entry.Length <= 0 || entry.Length > MaximumSingleAudioBytes)
                {
                    throw new InvalidDataException(entry.Name + " has an invalid size.");
                }

                total += entry.Length;
                if (total > MaximumTotalAudioBytes)
                {
                    throw new InvalidDataException("The Sound Pack audio files are too large.");
                }
            }
        }

        private static string ComputeFolderContentHash(string folder)
        {
            using (var sha = SHA256.Create())
            using (var buffer = new MemoryStream())
            {
                foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .OrderBy(x => GetRelativeFilePath(folder, x), StringComparer.OrdinalIgnoreCase))
                {
                    var relativePath = GetRelativeFilePath(folder, path).Replace('\\', '/').ToLowerInvariant();
                    var nameBytes = Encoding.UTF8.GetBytes(relativePath);
                    buffer.Write(nameBytes, 0, nameBytes.Length);
                    var bytes = File.ReadAllBytes(path);
                    buffer.Write(bytes, 0, bytes.Length);
                }

                buffer.Position = 0;
                return BitConverter.ToString(sha.ComputeHash(buffer)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private string CreateLocalId(string contentHash, SoundPackLibraryIndex index)
        {
            var seed = string.IsNullOrWhiteSpace(contentHash) ? Guid.NewGuid().ToString("N") : contentHash.ToLowerInvariant();
            var baseId = "sound-" + seed.Substring(0, Math.Min(12, seed.Length));
            var candidate = baseId;
            var suffix = 2;

            while (index.Packs.Any(x => string.Equals(x.LocalId, candidate, StringComparison.OrdinalIgnoreCase)) ||
                   Directory.Exists(Path.Combine(libraryRoot, candidate)))
            {
                candidate = baseId + "-" + suffix++;
            }

            return candidate;
        }

        private static SoundPackLibraryPack FindPack(SoundPackLibraryIndex index, string localId)
        {
            var record = index?.Packs?.FirstOrDefault(x =>
                string.Equals(x.LocalId, localId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                throw new InvalidOperationException("The selected Sound Pack is no longer in the library.");
            }

            return record;
        }

        private void ValidateStoredPack(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("The Sound Pack folder could not be found.");
            }

            if (!File.Exists(Path.Combine(folder, ManifestFileName)))
            {
                throw new InvalidDataException("The Sound Pack library entry is incomplete.");
            }

            if (!SupportedAudioFiles.Any(x => File.Exists(GetStoredAudioPath(folder, GetRelativeAudioPath(x)))))
            {
                throw new InvalidDataException("The Sound Pack does not contain any supported audio file.");
            }
        }

        private void PopulateRuntimeProperties(SoundPackLibraryIndex index)
        {
            foreach (var pack in index.Packs)
            {
                var folder = Path.Combine(libraryRoot, pack.LocalId ?? string.Empty);
                pack.IsActive = string.Equals(index.ActivePackId, pack.LocalId, StringComparison.OrdinalIgnoreCase);
                pack.FolderPath = folder;
                pack.SizeBytes = Directory.Exists(folder)
                    ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(path =>
                    {
                        try { return new FileInfo(path).Length; }
                        catch { return 0L; }
                    })
                    : 0L;
            }
        }

        private bool RemoveMissingLibraryEntries(SoundPackLibraryIndex index)
        {
            if (index.Packs == null)
            {
                index.Packs = new List<SoundPackLibraryPack>();
                return true;
            }

            var removed = index.Packs.RemoveAll(x =>
                x == null || string.IsNullOrWhiteSpace(x.LocalId) ||
                !Directory.Exists(Path.Combine(libraryRoot, x.LocalId)) ||
                !File.Exists(Path.Combine(libraryRoot, x.LocalId, ManifestFileName))) > 0;

            if (!string.IsNullOrWhiteSpace(index.ActivePackId) &&
                !index.Packs.Any(x => string.Equals(x.LocalId, index.ActivePackId, StringComparison.OrdinalIgnoreCase)))
            {
                index.ActivePackId = string.Empty;
                removed = true;
            }

            return removed;
        }

        private SoundPackLibraryIndex LoadIndex()
        {
            EnsureLibraryFolders();
            if (!File.Exists(indexFilePath))
            {
                return new SoundPackLibraryIndex();
            }

            try
            {
                var index = JsonConvert.DeserializeObject<SoundPackLibraryIndex>(File.ReadAllText(indexFilePath));
                return index ?? new SoundPackLibraryIndex();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][SoundPack] Failed to read Sound Pack index; rebuilding an empty index.");
                return new SoundPackLibraryIndex();
            }
        }

        private void SaveIndex(SoundPackLibraryIndex index)
        {
            EnsureLibraryFolders();
            var json = JsonConvert.SerializeObject(index, Formatting.Indented);
            var tempPath = indexFilePath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (File.Exists(indexFilePath))
            {
                File.Delete(indexFilePath);
            }
            File.Move(tempPath, indexFilePath);
        }

        private void EnsureLibraryFolders()
        {
            Directory.CreateDirectory(soundPacksRoot);
            Directory.CreateDirectory(libraryRoot);
        }

        private static void CopyEntry(ZipArchiveEntry entry, string destinationPath)
        {
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            using (var source = entry.Open())
            using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
            }
        }

        private static string NormalizeArchivePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static string GetRelativeAudioPath(string fileName)
        {
            string relativePath;
            if (string.IsNullOrWhiteSpace(fileName) ||
                !SupportedAudioRelativePathByFileName.TryGetValue(fileName, out relativePath))
            {
                return string.Empty;
            }

            return relativePath;
        }

        private static string GetStoredAudioPath(string folder, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetRelativeFilePath(string rootFolder, string fullPath)
        {
            var root = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(fullPath);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length)
                : Path.GetFileName(full);
        }

        private static void AddFileToArchive(ZipArchive archive, string sourcePath, string entryName)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var input = File.OpenRead(sourcePath))
            using (var output = entry.Open())
            {
                input.CopyTo(output);
            }
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
