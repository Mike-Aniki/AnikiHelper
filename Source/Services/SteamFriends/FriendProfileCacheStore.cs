using Playnite.SDK.Data;
using System;
using System.IO;

namespace AnikiHelper.Services.SteamFriends
{
    public class FriendProfileCacheStore
    {
        private readonly string cacheDir;

        public FriendProfileCacheStore(string steamFriendCacheRootPath)
        {
            cacheDir = Path.Combine(steamFriendCacheRootPath, "FriendProfilesCache");
            Directory.CreateDirectory(cacheDir);
        }

        public CachedFriendProfile TryLoad(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return null;
            }

            var path = GetFilePath(steamId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                return Serialization.FromJson<CachedFriendProfile>(json);
            }
            catch
            {
                return null;
            }
        }

        public void Save(CachedFriendProfile entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.steamid) || entry.profile == null)
            {
                return;
            }

            try
            {
                var path = GetFilePath(entry.steamid);
                var json = Serialization.ToJson(entry);
                File.WriteAllText(path, json);
            }
            catch
            {
            }
        }

        private string GetFilePath(string steamId)
        {
            var safe = string.Concat(steamId.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(cacheDir, safe + ".json");
        }
    }
}
