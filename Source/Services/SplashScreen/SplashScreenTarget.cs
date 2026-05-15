using Playnite.SDK.Models;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenTarget
    {
        public SplashScreenScope Scope { get; private set; }

        public string DisplayName { get; private set; }

        public string FolderPath { get; private set; }

        public string EmptyMessageKey { get; private set; }

        private SplashScreenTarget(
            SplashScreenScope scope,
            string displayName,
            string folderPath,
            string emptyMessageKey)
        {
            Scope = scope;
            DisplayName = displayName;
            FolderPath = folderPath;
            EmptyMessageKey = emptyMessageKey;
        }

        public static SplashScreenTarget FromGame(Game game, SplashScreenFolderService folderService)
        {
            return new SplashScreenTarget(
                SplashScreenScope.Game,
                game?.Name ?? "Unknown game",
                folderService.GetGameFolder(game),
                "SplashManager_NoSplashForGame");
        }

        public static SplashScreenTarget FromGlobal(SplashScreenFolderService folderService)
        {
            return new SplashScreenTarget(
                SplashScreenScope.Global,
                "Global",
                folderService.GlobalFolder,
                "SplashManager_NoGlobalSplash");
        }

        public static SplashScreenTarget FromSource(string sourceName, SplashScreenFolderService folderService)
        {
            var cleanName = SplashScreenFolderService.CleanFolderName(sourceName);

            return new SplashScreenTarget(
                SplashScreenScope.Source,
                sourceName ?? "Unknown source",
                System.IO.Path.Combine(folderService.SourcesFolder, cleanName),
                "SplashManager_NoSplashForSource");
        }

        public static SplashScreenTarget FromPlatform(string platformName, SplashScreenFolderService folderService)
        {
            var cleanName = SplashScreenFolderService.CleanFolderName(platformName);

            return new SplashScreenTarget(
                SplashScreenScope.Platform,
                platformName ?? "Unknown platform",
                System.IO.Path.Combine(folderService.PlatformsFolder, cleanName),
                "SplashManager_NoSplashForPlatform");
        }
    }
}