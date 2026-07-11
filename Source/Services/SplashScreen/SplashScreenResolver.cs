using Playnite.SDK.Models;
using System;
using System.IO;

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
                case SplashScreenSelectionMode.AlwaysSource:
                    return ResolveTarget(game, SplashScreenPriorityTarget.Source);

                case SplashScreenSelectionMode.AlwaysPlatform:
                    return ResolveTarget(game, SplashScreenPriorityTarget.Platform);

                case SplashScreenSelectionMode.AlwaysGlobal:
                    return ResolveTarget(game, SplashScreenPriorityTarget.Global);

                case SplashScreenSelectionMode.CustomPriority:
                case SplashScreenSelectionMode.Automatic:
                default:
                    return ResolveTarget(game, SplashScreenPriorityTarget.GameCustom);
            }
        }

        public SplashScreenMediaItem ResolveSharedFallback(Game game)
        {
            if (game == null)
            {
                return null;
            }

            return ResolveTarget(game, SplashScreenPriorityTarget.Source)
                ?? ResolveTarget(game, SplashScreenPriorityTarget.Platform)
                ?? ResolveTarget(game, SplashScreenPriorityTarget.Global);
        }

        public SplashScreenMediaItem ResolveTarget(Game game, SplashScreenPriorityTarget target)
        {
            if (game == null && target != SplashScreenPriorityTarget.Global)
            {
                return null;
            }

            switch (target)
            {
                case SplashScreenPriorityTarget.GameCustom:
                    return ResolveGameCustom(game);

                case SplashScreenPriorityTarget.Source:
                    return ResolveSourceOnly(game);

                case SplashScreenPriorityTarget.Platform:
                    return ResolvePlatformOnly(game);

                case SplashScreenPriorityTarget.Global:
                    return ResolveGlobalOnly();

                case SplashScreenPriorityTarget.GameBackground:
                case SplashScreenPriorityTarget.None:
                default:
                    return null;
            }
        }

        private SplashScreenMediaItem ResolveGameCustom(Game game)
        {
            var gameFolder = folderService.GetGameFolder(game);

            return FindMainMedia(gameFolder, SplashScreenScope.Game)
                ?? FindRandomMedia(gameFolder, SplashScreenScope.Game);
        }

        private SplashScreenMediaItem ResolveSourceOnly(Game game)
        {
            var sourceFolder = folderService.GetSourceFolder(game);

            return FindMainMedia(sourceFolder, SplashScreenScope.Source)
                ?? FindRandomMedia(sourceFolder, SplashScreenScope.Source);
        }

        private SplashScreenMediaItem ResolvePlatformOnly(Game game)
        {
            var platformFolder = folderService.GetPlatformFolder(game);

            return FindMainMedia(platformFolder, SplashScreenScope.Platform)
                ?? FindRandomMedia(platformFolder, SplashScreenScope.Platform);
        }

        private SplashScreenMediaItem ResolveGlobalOnly()
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
