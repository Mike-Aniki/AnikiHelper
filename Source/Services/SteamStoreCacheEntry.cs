using System;
using System.Collections.Generic;

namespace AnikiHelper.Services
{
    public class SteamStoreCacheEntry
    {
        public DateTime LastUpdatedUtc { get; set; }

        public List<SteamStoreItem> Items { get; set; } = new List<SteamStoreItem>();
    }
}