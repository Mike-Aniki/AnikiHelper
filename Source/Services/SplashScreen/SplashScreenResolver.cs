using Playnite.SDK.Models;
using System;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenResolver
    {
        private readonly SplashScreenFolderService folderService;
        private readonly Random random = new Random();

        public SplashScreenResolver(SplashScreenFolderService folderService)
        {
            this.folderService = folderService;
        }

        private SplashScreenMediaItem FindMainMedia(string folder, SplashScreenScope scope)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return null;
            }

            var mainFilePath = Path.Combine(folder, "main_splash.txt");
            if (!File.Exists(mainFilePath))
            {
                return null;
            }

            var fileName = File.ReadAllText(mainFilePath)?.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var selectedFile = Path.Combine(folder, fileName);
            if (!File.Exists(selectedFile) || !SplashScreenMediaScanner.IsSupportedMedia(selectedFile))
            {
                return null;
            }

            return new SplashScreenMediaItem
            {
                FilePath = selectedFile,
                Scope = scope,
                IsVideo = SplashScreenMediaScanner.IsVideoFile(selectedFile)
            };
        }

        public SplashScreenMediaItem Resolve(Game game, SplashScreenSelectionMode mode)
        {
            if (game == null)
            {
                return null;
            }

            switch (mode)
            {
                case SplashScreenSelectionMode.CustomPriority:
                    return ResolveCustomPriority(game);

                case SplashScreenSelectionMode.AlwaysSource:
                    return ResolveAlwaysSource(game);

                case SplashScreenSelectionMode.AlwaysPlatform:
                    return ResolveAlwaysPlatform(game);

                case SplashScreenSelectionMode.AlwaysGlobal:
                    return ResolveAlwaysGlobal();

                case SplashScreenSelectionMode.Automatic:
                default:
                    return ResolveAutomatic(game);
            }
        }

        private SplashScreenMediaItem ResolveAutomatic(Game game)
        {
            var gameFolder = folderService.GetGameFolder(game);

            return FindMainMedia(gameFolder, SplashScreenScope.Game)
                ?? FindRandomMedia(gameFolder, SplashScreenScope.Game);
        }

        public SplashScreenMediaItem ResolveSharedFallback(Game game)
        {
            if (game == null)
            {
                return null;
            }

            var sourceFolder = folderService.GetSourceFolder(game);
            var platformFolder = folderService.GetPlatformFolder(game);
            var globalFolder = folderService.GlobalFolder;

            return FindMainMedia(sourceFolder, SplashScreenScope.Source)
                ?? FindRandomMedia(sourceFolder, SplashScreenScope.Source)
                ?? FindMainMedia(platformFolder, SplashScreenScope.Platform)
                ?? FindRandomMedia(platformFolder, SplashScreenScope.Platform)
                ?? FindMainMedia(globalFolder, SplashScreenScope.Global)
                ?? FindRandomMedia(globalFolder, SplashScreenScope.Global);
        }

        private SplashScreenMediaItem ResolveCustomPriority(Game game)
        {
            var gameFolder = folderService.GetGameFolder(game);
            var sourceFolder = folderService.GetSourceFolder(game);
            var platformFolder = folderService.GetPlatformFolder(game);
            var globalFolder = folderService.GlobalFolder;

            return FindMainMedia(gameFolder, SplashScreenScope.Game)
                ?? FindRandomMedia(gameFolder, SplashScreenScope.Game)
                ?? FindMainMedia(sourceFolder, SplashScreenScope.Source)
                ?? FindRandomMedia(sourceFolder, SplashScreenScope.Source)
                ?? FindMainMedia(platformFolder, SplashScreenScope.Platform)
                ?? FindRandomMedia(platformFolder, SplashScreenScope.Platform)
                ?? FindMainMedia(globalFolder, SplashScreenScope.Global)
                ?? FindRandomMedia(globalFolder, SplashScreenScope.Global);
        }

        private SplashScreenMediaItem ResolveAlwaysSource(Game game)
        {
            var sourceFolder = folderService.GetSourceFolder(game);
            var globalFolder = folderService.GlobalFolder;

            return FindMainMedia(sourceFolder, SplashScreenScope.Source)
                ?? FindRandomMedia(sourceFolder, SplashScreenScope.Source)
                ?? FindMainMedia(globalFolder, SplashScreenScope.Global)
                ?? FindRandomMedia(globalFolder, SplashScreenScope.Global);
        }

        private SplashScreenMediaItem ResolveAlwaysPlatform(Game game)
        {
            var platformFolder = folderService.GetPlatformFolder(game);
            var globalFolder = folderService.GlobalFolder;

            return FindMainMedia(platformFolder, SplashScreenScope.Platform)
                ?? FindRandomMedia(platformFolder, SplashScreenScope.Platform)
                ?? FindMainMedia(globalFolder, SplashScreenScope.Global)
                ?? FindRandomMedia(globalFolder, SplashScreenScope.Global);
        }

        private SplashScreenMediaItem ResolveAlwaysGlobal()
        {
            var globalFolder = folderService.GlobalFolder;

            return FindMainMedia(globalFolder, SplashScreenScope.Global)
                ?? FindRandomMedia(globalFolder, SplashScreenScope.Global);
        }

        private SplashScreenMediaItem FindRandomMedia(string folder, SplashScreenScope scope)
        {
            var files = SplashScreenMediaScanner.GetSupportedFiles(folder);

            if (files.Count == 0)
            {
                return null;
            }

            var selectedFile = files[random.Next(files.Count)];

            return new SplashScreenMediaItem
            {
                FilePath = selectedFile,
                Scope = scope,
                IsVideo = SplashScreenMediaScanner.IsVideoFile(selectedFile)
            };
        }

        
    }
}