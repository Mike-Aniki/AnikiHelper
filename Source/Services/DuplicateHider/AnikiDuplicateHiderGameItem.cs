using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Media;

namespace AnikiHelper.Services.DuplicateHider
{
    public class AnikiDuplicateHiderGameItem : ObservableObject
    {
        public Guid GameId { get; set; } = Guid.Empty;

        public string Name { get; set; } = string.Empty;

        public string SourceName { get; set; } = string.Empty;

        public string PlatformName { get; set; } = string.Empty;

        public string DisplayString { get; set; } = string.Empty;

        public ImageSource Icon { get; set; }

        private bool isCurrent;
        public bool IsCurrent
        {
            get => isCurrent;
            set => SetValue(ref isCurrent, value);
        }

        public string Label
        {
            get
            {
                return GetFriendlyVersionName(SourceName, PlatformName, Name);
            }
        }

        private static string GetFriendlyVersionName(string sourceName, string platformName, string gameName)
        {
            var source = CleanName(sourceName);
            var platform = CleanPlatformName(platformName);

            // Launchers PC connus : on affiche la source.
            if (EqualsIgnoreCase(source, "Steam"))
            {
                return "Steam";
            }

            if (ContainsIgnoreCase(source, "Epic"))
            {
                return "Epic Games";
            }

            if (EqualsIgnoreCase(source, "GOG") || ContainsIgnoreCase(source, "GOG"))
            {
                return "GOG";
            }

            if (ContainsIgnoreCase(source, "Ubisoft"))
            {
                return "Ubisoft Connect";
            }

            if (EqualsIgnoreCase(source, "EA") || ContainsIgnoreCase(source, "EA app") || ContainsIgnoreCase(source, "Origin"))
            {
                return "EA app";
            }

            if (ContainsIgnoreCase(source, "Battle.net") || ContainsIgnoreCase(source, "Blizzard"))
            {
                return "Battle.net";
            }

            if (ContainsIgnoreCase(source, "Amazon"))
            {
                return "Amazon Games";
            }

            if (ContainsIgnoreCase(source, "itch"))
            {
                return "itch.io";
            }

            // Si Playnite a une vraie plateforme console, on affiche la plateforme.
            // Exemple : Sony PlayStation 4 -> PlayStation 4
            // Exemple : Sony PlayStation 5 -> PlayStation 5
            // Exemple inconnu : Sega Dreamcast -> Sega Dreamcast
            if (!string.IsNullOrWhiteSpace(platform) &&
                !EqualsIgnoreCase(platform, "PC") &&
                !EqualsIgnoreCase(platform, "Windows"))
            {
                return platform;
            }

            // Si pas de plateforme utile, on affiche la source.
            if (!string.IsNullOrWhiteSpace(source))
            {
                return source;
            }

            // Dernier fallback plateforme.
            if (!string.IsNullOrWhiteSpace(platform))
            {
                return platform;
            }

            return gameName ?? string.Empty;
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim();
        }

        private static string CleanPlatformName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = value.Trim();

            cleaned = cleaned.Replace("Sony PlayStation", "PlayStation");
            cleaned = cleaned.Replace("Microsoft Xbox", "Xbox");
            cleaned = cleaned.Replace("Nintendo Switch", "Switch");

            cleaned = cleaned.Replace("PC (Windows)", "PC");
            cleaned = cleaned.Replace("Windows", "PC");

            return cleaned.Trim();
        }

        private static bool EqualsIgnoreCase(string value, string compare)
        {
            return string.Equals(value, compare, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string value, string search)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(search))
            {
                return false;
            }

            return value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public ICommand SelectCommand { get; set; }
    }
}