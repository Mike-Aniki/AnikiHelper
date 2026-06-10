using Playnite.SDK;
using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace AnikiHelper
{
    internal sealed class EventSoundService
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const string ThemeFolderName = "Aniki_ReMake_bb8728bd-ac83-4324-88b1-ee5c586527d1";

        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;

        public EventSoundService(IPlayniteAPI api, AnikiHelperSettings settings)
        {
            playniteApi = api;
            this.settings = settings;
        }

        private bool CanPlay()
        {
            return playniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen
                && IsAnikiThemeActive()
                && (settings?.EventSoundsEnabled ?? true)
                && !(playniteApi?.ApplicationSettings?.Fullscreen?.IsMusicMuted ?? false);
        }

        private bool IsAnikiThemeActive()
        {
            try
            {
                return System.Windows.Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }

        private string GetThemeEventsFolder()
        {
            try
            {
                var configRoot = playniteApi?.Paths?.ConfigurationPath;
                if (!string.IsNullOrEmpty(configRoot))
                {
                    var themeFolder = Path.Combine(
                        configRoot,
                        "Themes",
                        "Fullscreen",
                        ThemeFolderName,
                        "Audio",
                        "Events");

                    if (Directory.Exists(themeFolder))
                    {
                        return themeFolder;
                    }
                }

                var appRoot = playniteApi?.Paths?.ApplicationPath;
                if (!string.IsNullOrEmpty(appRoot))
                {
                    var themeFolder = Path.Combine(
                        appRoot,
                        "Themes",
                        "Fullscreen",
                        ThemeFolderName,
                        "Audio",
                        "Events");

                    if (Directory.Exists(themeFolder))
                    {
                        return themeFolder;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void PlaySound(string fileName, bool synchronous = false)
        {
            try
            {
                if (!CanPlay())
                {
                    return;
                }

                var folder = GetThemeEventsFolder();
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                var fullPath = Path.Combine(folder, fileName);
                if (!File.Exists(fullPath))
                {
                    return;
                }

                Action playAction = () =>
                {
                    try
                    {
                        using (var stream = new MemoryStream(File.ReadAllBytes(fullPath)))
                        using (var player = new SoundPlayer(stream))
                        {
                            player.Load();
                            player.PlaySync();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, $"[AnikiHelper] Failed to play event sound: {fileName}");
                    }
                };

                if (synchronous)
                {
                    playAction();
                }
                else
                {
                    Task.Run(playAction);
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"[AnikiHelper] PlaySound failed: {fileName}");
            }
        }

        public void PlayApplicationStarted()
        {
            PlaySound("ApplicationStarted.wav");
        }

        public void PlayApplicationStopped()
        {
            PlaySound("ApplicationStopped.wav", synchronous: true);
        }

        public void PlayGameStarting()
        {
            PlaySound("GameStarting.wav");
        }

        public void PlayGameStarted()
        {
            PlaySound("GameStarted.wav");
        }

        public void PlayGameStopped()
        {
            PlaySound("GameStopped.wav");
        }

        public void PlayGameInstalled()
        {
            PlaySound("GameInstalled.wav");
        }

        public void PlayGameUninstalled()
        {
            PlaySound("GameUninstalled.wav");
        }

        public void PlayLibraryUpdated()
        {
            PlaySound("LibraryUpdated.wav");
        }

        public void PlayFullscreenViewChanged()
        {
            PlaySound("FullscreenViewChanged.wav");
        }
    }
}