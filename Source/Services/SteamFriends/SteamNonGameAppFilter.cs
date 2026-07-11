using System;
using System.Collections.Generic;

namespace AnikiHelper.Services.SteamFriends
{
    internal static class SteamNonGameAppFilter
    {
        // Steam apps that are often returned by player summary / recently played / owned games,
        // but should not be shown as real games in the friends UI.
        private static readonly HashSet<int> KnownNonGameAppIds = new HashSet<int>
        {
            431960 // Wallpaper Engine
        };

        private static readonly HashSet<string> KnownNonGameAppNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wallpaper Engine"
        };

        public static bool IsKnownNonGameSteamApp(int appId)
        {
            return appId > 0 && KnownNonGameAppIds.Contains(appId);
        }

        public static bool IsKnownNonGameSteamAppName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && KnownNonGameAppNames.Contains(name.Trim());
        }

        public static bool IsKnownNonGameSteamApp(int appId, string name)
        {
            return IsKnownNonGameSteamApp(appId) || IsKnownNonGameSteamAppName(name);
        }

        public static bool IsNonGameSteamAppType(string appType)
        {
            if (string.IsNullOrWhiteSpace(appType))
            {
                return false;
            }

            var type = appType.Trim().ToLowerInvariant();
            return type == "application" ||
                   type == "dlc" ||
                   type == "demo" ||
                   type == "hardware" ||
                   type == "mod" ||
                   type == "music" ||
                   type == "series" ||
                   type == "software" ||
                   type == "tool" ||
                   type == "video";
        }
    }
}
