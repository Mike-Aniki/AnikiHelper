using Newtonsoft.Json.Linq;
using Playnite.SDK;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AnikiHelper.Services.SteamFriends
{
    /// <summary>
    /// Non-blocking Steam game image resolver used by the Steam Friends UI.
    /// It prefers a local cache, starts missing downloads in the background, and calls onReady when a local image becomes available.
    /// </summary>
    public class SteamFriendsGameImageResolver : IDisposable
    {
        private readonly ILogger logger;
        private readonly string cacheDir;
        private readonly HttpClient http;
        private readonly SemaphoreSlim downloadGate = new SemaphoreSlim(2, 2);
        private readonly ConcurrentDictionary<int, byte> inProgress = new ConcurrentDictionary<int, byte>();
        private readonly ConcurrentDictionary<int, string> readyCallbacksLastValue = new ConcurrentDictionary<int, string>();
        private bool disposed;

        public SteamFriendsGameImageResolver(string pluginUserDataPath, ILogger logger)
        {
            this.logger = logger ?? LogManager.GetLogger();
            var root = string.IsNullOrWhiteSpace(pluginUserDataPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite", "AnikiHelper")
                : pluginUserDataPath;

            cacheDir = Path.Combine(root, "SteamFriendCache", "GameHeaderCache");
            Directory.CreateDirectory(cacheDir);

            http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            try
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AnikiHelper/1.0");
                http.DefaultRequestHeaders.Referrer = new Uri("https://store.steampowered.com/");
            }
            catch
            {
            }
        }

        public string GetGameImageSource(int appId, Action<string> onReady = null)
        {
            if (appId <= 0)
            {
                return null;
            }

            var localPath = GetCachePath(appId);
            if (IsUsableImageFile(localPath))
            {
                return ToFileUri(localPath);
            }

            StartBackgroundResolve(appId, onReady);

            // Keep a remote fallback for the current paint. The local cache will replace it after the background download.
            return GetPrimaryRemoteFallback(appId);
        }

        public void StartBackgroundResolve(int appId, Action<string> onReady = null)
        {
            if (disposed || appId <= 0)
            {
                return;
            }

            var localPath = GetCachePath(appId);
            if (IsUsableImageFile(localPath))
            {
                var uri = ToFileUri(localPath);
                SafeNotify(appId, uri, onReady);
                return;
            }

            if (!inProgress.TryAdd(appId, 0))
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await downloadGate.WaitAsync().ConfigureAwait(false);
                    var result = await ResolveAndCacheAsync(appId).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        SafeNotify(appId, result, onReady);
                    }
                }
                catch (Exception ex)
                {
                    try { logger.Debug(ex, $"[AnikiHelper][SteamFriends] Game image background resolve failed for AppId={appId}."); } catch { }
                }
                finally
                {
                    try { downloadGate.Release(); } catch { }
                    byte ignored;
                    inProgress.TryRemove(appId, out ignored);
                }
            });
        }

        public async Task<string> ResolveAndCacheAsync(int appId)
        {
            if (appId <= 0)
            {
                return null;
            }

            var localPath = GetCachePath(appId);
            if (IsUsableImageFile(localPath))
            {
                return ToFileUri(localPath);
            }

            var candidates = new List<string>();
            candidates.AddRange(BuildDirectCandidates(appId));
            candidates.AddRange(await GetAppDetailsImageCandidatesAsync(appId).ConfigureAwait(false));

            foreach (var url in candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var ok = await TryDownloadImageAsync(url, localPath).ConfigureAwait(false);
                if (ok && IsUsableImageFile(localPath))
                {
                    return ToFileUri(localPath);
                }
            }

            return null;
        }

        private IEnumerable<string> BuildDirectCandidates(int appId)
        {
            yield return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
            yield return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/capsule_616x353.jpg";
            yield return $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
            yield return $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/capsule_616x353.jpg";
            yield return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
            yield return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/capsule_616x353.jpg";
            yield return $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
            yield return $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/capsule_616x353.jpg";
        }

        private async Task<List<string>> GetAppDetailsImageCandidatesAsync(int appId)
        {
            var result = new List<string>();

            try
            {
                var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic";
                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return result;
                }

                var root = JObject.Parse(json);
                var data = root[appId.ToString()]?["data"];
                if (data == null || data.Type == JTokenType.Null)
                {
                    return result;
                }

                AddIfText(result, data["header_image"]);
                AddIfText(result, data["capsule_image"]);
                AddIfText(result, data["capsule_imagev5"]);
                AddIfText(result, data["library_hero"]);
                AddIfText(result, data["library_capsule"]);
            }
            catch
            {
            }

            return result;
        }

        private static void AddIfText(List<string> target, JToken token)
        {
            try
            {
                if (token != null && token.Type != JTokenType.Null)
                {
                    var value = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        target.Add(value.Trim());
                    }
                }
            }
            catch
            {
            }
        }

        private async Task<bool> TryDownloadImageAsync(string imageUrl, string localPath)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(localPath))
            {
                return false;
            }

            try
            {
                var bytes = await http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
                if (bytes == null || bytes.Length < 1024 || !LooksLikeImageBytes(bytes))
                {
                    return false;
                }

                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var tmp = localPath + ".tmp";
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }

                File.WriteAllBytes(tmp, bytes);
                if (!IsUsableImageFile(tmp))
                {
                    try { File.Delete(tmp); } catch { }
                    return false;
                }

                try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
                File.Move(tmp, localPath);
                return true;
            }
            catch
            {
                try
                {
                    var tmp = localPath + ".tmp";
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch
                {
                }

                return false;
            }
        }

        private void SafeNotify(int appId, string localFileUri, Action<string> onReady)
        {
            if (string.IsNullOrWhiteSpace(localFileUri) || onReady == null)
            {
                return;
            }

            try
            {
                string old;
                if (readyCallbacksLastValue.TryGetValue(appId, out old) && string.Equals(old, localFileUri, StringComparison.OrdinalIgnoreCase))
                {
                    // Avoid hammering the same UI callback for a cached image during rebuilds.
                    return;
                }

                readyCallbacksLastValue[appId] = localFileUri;
                onReady(localFileUri);
            }
            catch
            {
            }
        }

        private string GetCachePath(int appId)
        {
            return appId <= 0 ? null : Path.Combine(cacheDir, appId + ".jpg");
        }

        private static string GetPrimaryRemoteFallback(int appId)
        {
            return appId <= 0 ? null : $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
        }

        private static bool IsUsableImageFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return false;
                }

                var info = new FileInfo(path);
                if (info.Length < 1024)
                {
                    return false;
                }

                var bytes = new byte[Math.Min(16, (int)info.Length)];
                using (var fs = File.OpenRead(path))
                {
                    fs.Read(bytes, 0, bytes.Length);
                }

                return LooksLikeImageBytes(bytes);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeImageBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
            {
                return false;
            }

            return (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
                   (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) ||
                   (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) ||
                   (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46);
        }

        private static string ToFileUri(string path)
        {
            try { return new Uri(path).AbsoluteUri; } catch { return path; }
        }

        public void Dispose()
        {
            disposed = true;
            try { http.Dispose(); } catch { }
            try { downloadGate.Dispose(); } catch { }
        }
    }
}
