using Playnite.SDK.Models;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenFolderService
    {
        private readonly string rootFolder;

        public SplashScreenFolderService(string pluginDataPath)
        {
            rootFolder = Path.Combine(pluginDataPath, "SplashScreens");
        }

        public string RootFolder => rootFolder;

        public string GlobalFolder => Path.Combine(rootFolder, "Global");

        public string SourcesFolder => Path.Combine(rootFolder, "Sources");

        public string PlatformsFolder => Path.Combine(rootFolder, "Platforms");

        public string GamesFolder => Path.Combine(rootFolder, "Games");

        public void EnsureBaseFolders()
        {
            Directory.CreateDirectory(GlobalFolder);
            Directory.CreateDirectory(SourcesFolder);
            Directory.CreateDirectory(PlatformsFolder);
            Directory.CreateDirectory(GamesFolder);
        }

        public string GetGameFolder(Game game)
        {
            if (game == null)
            {
                return Path.Combine(GamesFolder, "Unknown");
            }

            var gameId = game.Id.ToString();

            if (string.IsNullOrWhiteSpace(gameId))
            {
                return Path.Combine(GamesFolder, "Unknown");
            }

            return Path.Combine(GamesFolder, gameId);
        }

        public string GetSourceFolder(Game game)
        {
            var cleanName = CleanFolderName(game?.Source?.Name);
            return Path.Combine(SourcesFolder, cleanName);
        }

        public string GetPlatformFolder(Game game)
        {
            var platformName = game?.Platforms?.FirstOrDefault()?.Name;
            var cleanName = CleanFolderName(platformName);
            return Path.Combine(PlatformsFolder, cleanName);
        }

        public void EnsureGameFolder(Game game)
        {
            Directory.CreateDirectory(GetGameFolder(game));
        }

        public static string CleanFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Unknown";
            }

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalidChar, '_');
            }

            name = Regex.Replace(name, @"\s+", " ").Trim();

            if (name.Length > 80)
            {
                name = name.Substring(0, 80).Trim();
            }

            return name;
        }
    }
}