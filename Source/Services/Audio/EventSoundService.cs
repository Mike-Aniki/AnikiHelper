using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnikiHelper
{
    internal sealed class EventSoundService
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const string ThemeFolderName = "Aniki_ReMake_bb8728bd-ac83-4324-88b1-ee5c586527d1";

        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;

        private readonly List<MediaPlayer> activePlayers = new List<MediaPlayer>();

        private readonly object volumeLock = new object();
        private double cachedInterfaceVolume = 1.0;
        private DateTime lastVolumeReadUtc = DateTime.MinValue;

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
                return Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled;
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

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                var volume = GetFullscreenInterfaceVolume();

                if (synchronous)
                {
                    if (dispatcher.CheckAccess())
                    {
                        PlayMediaPlayerOnDispatcher(fullPath, fileName, volume, true);
                    }
                    else
                    {
                        dispatcher.Invoke(
                            new Action(() => PlayMediaPlayerOnDispatcher(fullPath, fileName, volume, true)),
                            DispatcherPriority.Send);
                    }
                }
                else
                {
                    dispatcher.BeginInvoke(
                        new Action(() => PlayMediaPlayerOnDispatcher(fullPath, fileName, volume, false)),
                        DispatcherPriority.Send);
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"[AnikiHelper] PlaySound failed: {fileName}");
            }
        }

        private void PlayMediaPlayerOnDispatcher(string fullPath, string fileName, double volume, bool waitForEnd)
        {
            MediaPlayer player = null;
            DispatcherFrame frame = null;
            DispatcherTimer timeoutTimer = null;

            try
            {
                player = new MediaPlayer
                {
                    Volume = volume
                };

                EventHandler mediaEnded = null;
                EventHandler<ExceptionEventArgs> mediaFailed = null;

                Action cleanup = () =>
                {
                    try
                    {
                        if (timeoutTimer != null)
                        {
                            timeoutTimer.Stop();
                            timeoutTimer.Tick -= null;
                        }

                        if (player != null)
                        {
                            player.MediaEnded -= mediaEnded;
                            player.MediaFailed -= mediaFailed;
                            player.Close();
                            activePlayers.Remove(player);
                        }

                        if (frame != null)
                        {
                            frame.Continue = false;
                        }
                    }
                    catch
                    {
                    }
                };

                mediaEnded = (s, e) => cleanup();

                mediaFailed = (s, e) =>
                {
                    logger.Debug(e.ErrorException, $"[AnikiHelper] Event sound media failed: {fileName}");
                    cleanup();
                };

                player.MediaEnded += mediaEnded;
                player.MediaFailed += mediaFailed;

                activePlayers.Add(player);

                player.Open(new Uri(fullPath, UriKind.Absolute));
                player.Position = TimeSpan.Zero;
                player.Play();

                if (waitForEnd)
                {
                    frame = new DispatcherFrame();

                    timeoutTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(5)
                    };

                    timeoutTimer.Tick += (s, e) =>
                    {
                        try
                        {
                            timeoutTimer.Stop();
                            cleanup();
                        }
                        catch
                        {
                        }
                    };

                    timeoutTimer.Start();

                    Dispatcher.PushFrame(frame);
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"[AnikiHelper] Failed to play event sound: {fileName}");

                try
                {
                    if (player != null)
                    {
                        player.Close();
                        activePlayers.Remove(player);
                    }

                    if (frame != null)
                    {
                        frame.Continue = false;
                    }
                }
                catch
                {
                }
            }
        }

        private double GetFullscreenInterfaceVolume()
        {
            try
            {
                lock (volumeLock)
                {
                    if (lastVolumeReadUtc != DateTime.MinValue &&
                        (DateTime.UtcNow - lastVolumeReadUtc).TotalSeconds < 2)
                    {
                        return cachedInterfaceVolume;
                    }

                    cachedInterfaceVolume = ReadFullscreenInterfaceVolumeFromFile();
                    lastVolumeReadUtc = DateTime.UtcNow;

                    return cachedInterfaceVolume;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] Failed to get cached InterfaceVolume.");
                return 1.0;
            }
        }

        private double ReadFullscreenInterfaceVolumeFromFile()
        {
            try
            {
                var fullscreenConfigPath = Path.Combine(
                    playniteApi.Paths.ConfigurationPath,
                    "fullscreenConfig.json"
                );

                if (!File.Exists(fullscreenConfigPath))
                {
                    logger.Debug($"[AnikiHelper] fullscreenConfig.json not found: {fullscreenConfigPath}");
                    return 1.0;
                }

                var json = File.ReadAllText(fullscreenConfigPath);

                var match = Regex.Match(
                    json,
                    "\"InterfaceVolume\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)",
                    RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    logger.Debug("[AnikiHelper] InterfaceVolume not found in fullscreenConfig.json.");
                    return 1.0;
                }

                if (!double.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var volume))
                {
                    logger.Debug($"[AnikiHelper] Failed to parse InterfaceVolume: {match.Groups[1].Value}");
                    return 1.0;
                }

                if (volume > 1.0)
                {
                    volume = volume / 100.0;
                }

                if (volume < 0.0)
                {
                    return 0.0;
                }

                if (volume > 1.0)
                {
                    return 1.0;
                }

                return volume;
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "[AnikiHelper] Failed to read InterfaceVolume from fullscreenConfig.json.");
                return 1.0;
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

        public void PlayLuckyDay()
        {
            PlaySound("LuckyDay.wav");
        }
    }
}