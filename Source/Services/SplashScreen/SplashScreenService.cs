using Playnite.SDK.Models;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenService
    {
        public SplashScreenFolderService Folders { get; }
        public SplashScreenResolver Resolver { get; }

        public SplashScreenService(string pluginDataPath)
        {
            Folders = new SplashScreenFolderService(pluginDataPath);
            Resolver = new SplashScreenResolver(Folders);

            Folders.EnsureBaseFolders();
        }

        public SplashScreenMediaItem ResolveSharedFallback(Game game)
        {
            return Resolver.ResolveSharedFallback(game);
        }

        public SplashScreenMediaItem ResolveSplash(Game game, SplashScreenSelectionMode mode)
        {
            return Resolver.Resolve(game, mode);
        }
    }
}