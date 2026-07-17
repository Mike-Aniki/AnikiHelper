using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnikiHelper.Services.SteamFriends
{
    public class AnikiSteamFriendsService : IDisposable
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;
        private readonly string pluginUserDataPath;
        private readonly ILogger logger;

        private void DebugLog(string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug(message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }

        private void DebugLog(Exception exception, string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    logger?.Debug(exception, message);
                }
            }
            catch
            {
                // Never let debug logging break the plugin.
            }
        }
        private readonly Action<string> debugLog;
        private readonly Func<bool> isAnikiThemeActive;
        private readonly Action saveSettings;

        private DispatcherTimer refreshTimer;
        private bool isRefreshing;
        private DateTime lastSuccessUtc = DateTime.MinValue;

        private readonly SteamFriendsWebApiClient steamClient;
        private Window selfStatusWindow;
        private Window friendProfileWindow;
        private Window friendActionsWindow;
        private DateTime friendActionsOpenedUtc = DateTime.MinValue;

        private const int FixedRefreshSeconds = 60;
        private const int FixedMaxFriendsShown = 15;
        private const int FixedMaxOfflineShown = 40;

        private DateTime pausedUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan PauseOnGameStart = TimeSpan.FromMinutes(10);

        private List<string> cachedFriendIds;
        private DateTime friendIdsLastFetchUtc = DateTime.MinValue;
        private readonly TimeSpan friendIdsCacheTtl = TimeSpan.FromHours(6);

        private string cachedResolvedSteamId64;
        private string cachedSteamIdInput;
        private DateTime steamIdResolveLastUtc = DateTime.MinValue;
        private readonly TimeSpan steamIdResolveTtl = TimeSpan.FromHours(24);

        private readonly Dictionary<string, string> lastState = new Dictionary<string, string>();
        private readonly Dictionary<string, string> lastGame = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> lastToastUtc = new Dictionary<string, DateTime>();

        private bool hasBaseline;
        private CancellationTokenSource toastCts;
        private static readonly TimeSpan ToastCooldown = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(6);

        private string lastUiSignature;
        private readonly object presenceSnapshotLock = new object();
        private List<FriendPresenceDto> lastPresenceSnapshot = new List<FriendPresenceDto>();

        private readonly FriendProfileCacheStore friendProfileCache;
        private readonly FriendActivityHubService friendActivityHubService;
        private readonly TimeSpan friendProfileCacheTtl = TimeSpan.FromHours(24);

        private readonly object friendsPlayedGamesCacheLock = new object();
        private readonly SemaphoreSlim friendsPlayedGamesRefreshGate = new SemaphoreSlim(1, 1);
        private SteamFriendsPlayedGamesCache friendsPlayedGamesCache = new SteamFriendsPlayedGamesCache();
        private bool friendsPlayedGamesCacheLoaded;
        private bool friendsPlayedGamesAutoRefreshScheduled;
        private bool friendsPlayedGamesAutoRefreshAttempted;
        private int currentDetailsSteamAppId;
        private const string friendsPlayedGamesCacheFileName = "friends_played_games.json";
        private readonly TimeSpan friendsPlayedGamesCacheTtl = TimeSpan.FromHours(24);
        private readonly TimeSpan friendsPlayedGamesAutoRefreshDelay = TimeSpan.FromSeconds(60);
        private string FriendsPlayedGamesCachePath => Path.Combine(steamFriendCacheDir, "FriendsPlayedGamesCache", friendsPlayedGamesCacheFileName);

        private List<SteamFriend> cachedFriendsExtended;
        private DateTime cachedFriendsExtendedLastFetchUtc = DateTime.MinValue;
        private readonly TimeSpan cachedFriendsExtendedTtl = TimeSpan.FromHours(6);

        private readonly string steamFriendCacheDir;
        private readonly string avatarCacheDir;
        private readonly string gameHeaderCacheDir;
        private readonly SteamFriendsGameImageResolver gameImageResolver;

        private readonly HttpClient http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly SemaphoreSlim avatarDlSem = new SemaphoreSlim(2, 2);
        private readonly SemaphoreSlim selfAvatarRefreshGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, byte> avatarDlInProgress = new ConcurrentDictionary<string, byte>();

        private readonly SemaphoreSlim gameHeaderDlSem = new SemaphoreSlim(2, 2);
        private readonly ConcurrentDictionary<int, byte> gameHeaderDlInProgress = new ConcurrentDictionary<int, byte>();
        private readonly ConcurrentDictionary<int, string> steamAppTypeCache = new ConcurrentDictionary<int, string>();

        private static readonly Guid friendsAchievementFeedPluginId = Guid.Parse("10f90193-72aa-4cdb-b16d-3e6b1f0feb17");
        private const string friendsAchievementFeedCacheFileName = "friend_achievement_cache.json";
        private const int MaxRecentFriendAchievements = 2;
        private const int MaxAvatarDownloadsPerRefresh = 4;

        private DateTime lastAvatarCleanupUtc = DateTime.MinValue;
        private readonly TimeSpan avatarCleanupInterval = TimeSpan.FromHours(24);
        private readonly TimeSpan avatarMaxAge = TimeSpan.FromDays(30);

        private string FriendsAchievementFeedCachePath => Path.Combine(
            playniteApi.Paths.ExtensionsDataPath,
            friendsAchievementFeedPluginId.ToString(),
            friendsAchievementFeedCacheFileName);

        public AnikiSteamFriendsService(
            IPlayniteAPI playniteApi,
            AnikiHelperSettings settings,
            string pluginUserDataPath,
            ILogger logger,
            Action<string> debugLog,
            Func<bool> isAnikiThemeActive,
            Action saveSettings)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.pluginUserDataPath = pluginUserDataPath;
            this.logger = logger ?? LogManager.GetLogger();
            this.debugLog = debugLog;
            this.isAnikiThemeActive = isAnikiThemeActive;
            this.saveSettings = saveSettings;

            steamClient = new SteamFriendsWebApiClient();

            steamFriendCacheDir = Path.Combine(pluginUserDataPath, "SteamFriendCache");
            avatarCacheDir = Path.Combine(steamFriendCacheDir, "AvatarCache");
            gameHeaderCacheDir = Path.Combine(steamFriendCacheDir, "GameHeaderCache");

            Directory.CreateDirectory(steamFriendCacheDir);
            Directory.CreateDirectory(avatarCacheDir);
            Directory.CreateDirectory(gameHeaderCacheDir);

            MigrateLegacySteamFriendCacheFolders();

            friendProfileCache = new FriendProfileCacheStore(steamFriendCacheDir);
            gameImageResolver = new SteamFriendsGameImageResolver(pluginUserDataPath, this.logger);
            friendActivityHubService = new FriendActivityHubService(
                settings,
                steamClient,
                pluginUserDataPath,
                playniteApi.Paths.ExtensionsDataPath,
                this.logger,
                gameImageResolver);

            settings?.EnsureSteamFriendsRuntimeCollections();
            LoadFriendsPlayedGamesCacheFromDisk();
            UpdateFriendsPlayedGamesCacheStatus();
            BindCommands();
        }

        private void MigrateLegacySteamFriendCacheFolders()
        {
            try
            {
                MigrateDirectoryContents(Path.Combine(pluginUserDataPath, "SteamFriendsAvatarCache"), avatarCacheDir);
                MigrateDirectoryContents(Path.Combine(pluginUserDataPath, "SteamFriendsGameHeaderCache"), gameHeaderCacheDir);
                MigrateDirectoryContents(Path.Combine(pluginUserDataPath, "SteamFriendProfilesCache"), Path.Combine(steamFriendCacheDir, "FriendProfilesCache"));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][SteamFriends] Failed to migrate legacy Steam Friends cache folders.");
            }
        }

        private void MigrateDirectoryContents(string oldDir, string newDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(oldDir) || string.IsNullOrWhiteSpace(newDir))
                {
                    return;
                }

                if (!Directory.Exists(oldDir))
                {
                    return;
                }

                Directory.CreateDirectory(newDir);

                foreach (var oldFile in Directory.GetFiles(oldDir, "*", SearchOption.AllDirectories))
                {
                    var relative = oldFile.Substring(oldDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var newFile = Path.Combine(newDir, relative);
                    var newFileDir = Path.GetDirectoryName(newFile);

                    if (!string.IsNullOrWhiteSpace(newFileDir))
                    {
                        Directory.CreateDirectory(newFileDir);
                    }

                    if (File.Exists(newFile))
                    {
                        continue;
                    }

                    try
                    {
                        File.Move(oldFile, newFile);
                    }
                    catch
                    {
                        File.Copy(oldFile, newFile, false);
                    }
                }

                try
                {
                    if (!Directory.EnumerateFileSystemEntries(oldDir).Any())
                    {
                        Directory.Delete(oldDir, false);
                    }
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper][SteamFriends] Failed to migrate cache folder '{oldDir}' to '{newDir}'.");
            }
        }

        public void BindCommands()
        {
            if (settings == null)
            {
                return;
            }

            settings.SetStatusOnlineCommand = new SteamFriendsCommand(() => SetSteamStatus("online"));
            settings.SetStatusAwayCommand = new SteamFriendsCommand(() => SetSteamStatus("away"));
            settings.SetStatusBusyCommand = new SteamFriendsCommand(() => SetSteamStatus("busy"));
            settings.SetStatusInvisibleCommand = new SteamFriendsCommand(() => SetSteamStatus("invisible"));
            settings.SetStatusOfflineCommand = new SteamFriendsCommand(() => SetSteamStatus("offline"));
            settings.OpenSteamCommand = new SteamFriendsCommand(OpenSteam);
            settings.OpenSelfStatusWindowCommand = new SteamFriendsCommand(OpenSelfStatusWindow);

            settings.OpenFriendProfileCommand = new SteamFriendsParameterCommand(parameter =>
            {
                var steamId = parameter as string;
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    return;
                }

                settings.IsFriendProfileOpen = true;
                _ = OpenFriendProfileAsync(steamId);
            });

            settings.OpenFriendProfileWindowCommand = new SteamFriendsParameterCommand(parameter =>
            {
                var steamId = parameter as string;
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    return;
                }

                settings.IsFriendProfileOpen = true;
                _ = OpenFriendProfileAsync(steamId);
                OpenFriendProfileWindow();
            });

            settings.RefreshSelectedFriendProfileCommand = new SteamFriendsCommand(() =>
            {
                if (!string.IsNullOrWhiteSpace(settings.SelectedFriendSteamId))
                {
                    _ = OpenFriendProfileAsync(settings.SelectedFriendSteamId, true);
                }
            });

            settings.ClearFriendProfileCommand = new SteamFriendsCommand(() =>
            {
                settings.IsFriendProfileOpen = false;
                settings.SelectedFriendSteamId = null;
                settings.SelectedFriendProfile = null;
                settings.FriendProfileError = null;
            });

            settings.OpenFriendChatCommand = new SteamFriendsParameterCommand(parameter => OpenFriendChat(parameter as string));

            settings.RefreshFriendsPlayedGamesCacheCommand = new SteamFriendsCommand(() =>
            {
                _ = RefreshFriendsPlayedGamesCacheAsync(true);
            });

            settings.OpenFriendActionsMenuCommand = new SteamFriendsParameterCommand(parameter => OpenFriendActions(parameter as string, false));
            settings.OpenFriendActionsWindowCommand = new SteamFriendsParameterCommand(parameter => OpenFriendActions(parameter as string, true));

            settings.CloseFriendActionsMenuCommand = new SteamFriendsCommand(() =>
            {
                settings.IsFriendActionsMenuOpen = false;
                settings.SelectedFriendForActions = null;
            });

            settings.OpenSelectedFriendProfileCommand = new SteamFriendsCommand(() =>
            {
                var steamId = settings.SelectedFriendForActions?.steamid;
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    return;
                }

                CloseFriendActionsWindow();
                settings.IsFriendProfileOpen = true;
                _ = OpenFriendProfileAsync(steamId);
                OpenFriendProfileWindow();
            });

            settings.OpenSelectedFriendChatCommand = new SteamFriendsCommand(() =>
            {
                var steamId = settings.SelectedFriendForActions?.steamid;
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    return;
                }

                CloseFriendActionsWindow();
                OpenFriendChat(steamId);
            });
        }

        public void Start()
        {
            if (!ShouldRunTimer())
            {
                Stop();
                return;
            }

            if (refreshTimer != null)
            {
                refreshTimer.Interval = TimeSpan.FromSeconds(FixedRefreshSeconds);
                if (!refreshTimer.IsEnabled)
                {
                    refreshTimer.Start();
                }

                _ = RefreshSteamPresenceAsync();
                ScheduleDeferredFriendsPlayedGamesRefreshIfNeeded();
                return;
            }

            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(FixedRefreshSeconds) };
            refreshTimer.Tick += (s, e) => { _ = RefreshSteamPresenceAsync(); };
            refreshTimer.Start();

            _ = RefreshSteamPresenceAsync();
            ScheduleDeferredFriendsPlayedGamesRefreshIfNeeded();
        }

        public void Stop()
        {
            try { refreshTimer?.Stop(); } catch { }
            refreshTimer = null;
            hasBaseline = false;
            try { friendActivityHubService?.ClearUnavailable(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            try { friendActivityHubService?.Dispose(); } catch { }
            try { gameImageResolver?.Dispose(); } catch { }
            try { toastCts?.Cancel(); toastCts?.Dispose(); } catch { }
            try { http?.Dispose(); } catch { }
        }

        public void OnGameStarted()
        {
            if (!ShouldRunTimer())
            {
                return;
            }

            pausedUntilUtc = DateTime.UtcNow.Add(PauseOnGameStart);
        }

        public void OnGameStopped()
        {
            pausedUntilUtc = DateTime.MinValue;
            hasBaseline = false;
            Start();
        }

        public void ForceRefresh()
        {
            if (ShouldRunTimer())
            {
                _ = RefreshSteamPresenceAsync();
            }
        }

        public async Task ForceRefreshAndWaitAsync(int timeoutMs = 10000)
        {
            if (!ShouldRunTimer())
            {
                return;
            }

            var waited = 0;
            while (isRefreshing && waited < timeoutMs)
            {
                await Task.Delay(200).ConfigureAwait(false);
                waited += 200;
            }

            await RefreshSteamPresenceAsync().ConfigureAwait(false);
        }

        public bool HandleControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (args == null || args.State != ControllerInputState.Pressed)
            {
                return false;
            }

            if (friendActionsWindow != null && friendActionsWindow.IsVisible)
            {
                var justOpenedActionsMenu = (DateTime.UtcNow - friendActionsOpenedUtc).TotalMilliseconds < 350;
                if (justOpenedActionsMenu)
                {
                    return true;
                }

                if (args.Button == ControllerInput.A)
                {
                    settings.OpenSelectedFriendProfileCommand?.Execute(null);
                    return true;
                }

                if (args.Button == ControllerInput.X)
                {
                    settings.OpenSelectedFriendChatCommand?.Execute(null);
                    return true;
                }

                if (args.Button == ControllerInput.B)
                {
                    CloseFriendActionsWindow();
                    return true;
                }
            }

            if (args.Button == ControllerInput.B)
            {
                if (selfStatusWindow != null && selfStatusWindow.IsVisible)
                {
                    return CloseSelfStatusWindow();
                }

                if (friendProfileWindow != null && friendProfileWindow.IsVisible)
                {
                    return CloseFriendProfileWindow();
                }
            }

            return false;
        }

        private bool ShouldRunTimer()
        {
            if (settings == null || settings.SteamFriendsEnabled != true)
            {
                ClearRuntimeIfDisabled();
                return false;
            }

            if (playniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
            {
                return false;
            }

            try
            {
                if (isAnikiThemeActive != null && !isAnikiThemeActive())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private void ApplyMissingConfigurationState()
        {
            if (settings == null)
            {
                return;
            }

            InvokeOnUi(() =>
            {
                settings.OnlineCount = 0;
                settings.InGameCount = 0;
                settings.OfflineCount = 0;
                settings.Friends?.Clear();
                settings.SteamFriendsPlayingCurrentGame?.Clear();
                settings.SteamFriendsPlayingCurrentGameAvailable = false;
                settings.SteamFriendsPlayingCurrentGameCount = 0;
                settings.SteamFriendsPlayingCurrentGameSummary = string.Empty;
                settings.SteamFriendsWhoPlayedCurrentGame?.Clear();
                settings.SteamFriendsWhoPlayedAvailable = false;
                settings.SteamFriendsWhoPlayedLoading = false;
                settings.SteamFriendsWhoPlayedCount = 0;
                settings.SteamFriendsWhoPlayedSummary = string.Empty;
                settings.SteamFriendsWhoPlayedError = settings.SteamFriendsSetupMessage;
                settings.ToastIsVisible = false;

                settings.IsSteamRunning = IsSteamClientRunning();
                settings.IsSteamLaunching = false;
                settings.SteamLaunchMessage = null;
                settings.SelfName = null;
                settings.SelfAvatar = null;
                settings.SelfGame = null;
                settings.SelfState = "missingconfig";
                settings.SelfStateLoc = settings.SteamFriendsStatusButtonText;
                settings.LastError = settings.SteamFriendsSetupMessage;
                settings.LastUpdateUtc = DateTime.MinValue;
            });
        }

        private void ClearRuntimeIfDisabled()
        {
            if (settings == null)
            {
                return;
            }

            InvokeOnUi(() =>
            {
                settings.OnlineCount = 0;
                settings.InGameCount = 0;
                settings.OfflineCount = 0;
                settings.Friends?.Clear();
                settings.SteamFriendsPlayingCurrentGame?.Clear();
                settings.SteamFriendsPlayingCurrentGameAvailable = false;
                settings.SteamFriendsPlayingCurrentGameCount = 0;
                settings.SteamFriendsPlayingCurrentGameSummary = string.Empty;
                settings.SteamFriendsWhoPlayedCurrentGame?.Clear();
                settings.SteamFriendsWhoPlayedAvailable = false;
                settings.SteamFriendsWhoPlayedCount = 0;
                settings.SteamFriendsWhoPlayedSummary = string.Empty;
                settings.SteamFriendsWhoPlayedError = string.Empty;
                settings.ToastIsVisible = false;
                settings.IsSteamRunning = IsSteamClientRunning();
                settings.IsSteamLaunching = false;
                settings.SteamLaunchMessage = null;
                settings.SelfName = null;
                settings.SelfAvatar = null;
                settings.SelfGame = null;
                settings.SelfState = "disabled";
                settings.SelfStateLoc = "Steam Friends disabled";
                settings.LastError = "Steam Friends is disabled in Aniki Helper settings.";
                settings.LastUpdateUtc = DateTime.MinValue;
            });
        }

        private bool IsSteamClientRunning()
        {
            try
            {
                // Steam can sometimes be detected as steamwebhelper instead of only steam.exe.
                // Keep this permissive so the status menu does not wrongly say Steam is closed.
                return Process.GetProcessesByName("steam").Any()
                    || Process.GetProcessesByName("steamwebhelper").Any()
                    || Process.GetProcessesByName("steamservice").Any();
            }
            catch
            {
                return false;
            }
        }

        public bool IsSteamRunningNow()
        {
            return IsSteamClientRunning();
        }

        private void OpenFriendActions(string steamId, bool openWindow)
        {
            if (string.IsNullOrWhiteSpace(steamId) || settings?.Friends == null)
            {
                return;
            }

            var friend = settings.Friends.FirstOrDefault(x => x != null && string.Equals(x.steamid, steamId, StringComparison.OrdinalIgnoreCase));
            if (friend == null)
            {
                return;
            }

            settings.SelectedFriendForActions = friend;
            settings.IsFriendActionsMenuOpen = true;

            if (openWindow)
            {
                OpenFriendActionsWindow();
            }
        }

        public void OpenFriendProfileWindow()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (friendProfileWindow != null && friendProfileWindow.IsVisible)
                {
                    friendProfileWindow.Activate();
                    friendProfileWindow.Focus();
                    return;
                }

                var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowMinimizeButton = false });
                var style = Application.Current.TryFindResource("FriendsStyleProfil") as Style;
                if (style != null)
                {
                    window.Content = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        Child = new Grid
                        {
                            Width = 1920,
                            Height = 1080,
                            Children = { new ContentControl { Focusable = false, Style = style } }
                        }
                    };
                }

                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.WindowState = WindowState.Maximized;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var parent = playniteApi.Dialogs.GetCurrentAppWindow();
                if (parent != null)
                {
                    window.Width = parent.Width;
                    window.Height = parent.Height;
                    window.Owner = parent;
                }

                window.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        CloseFriendProfileWindow();
                    }
                };

                window.Closed += (s, e) =>
                {
                    friendProfileWindow = null;
                    if (settings != null)
                    {
                        settings.IsFriendProfileOpen = false;
                    }
                };

                friendProfileWindow = window;
                window.Show();
                window.Activate();
                window.Focus();
            });
        }

        public void OpenFriendActionsWindow()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (friendActionsWindow != null && friendActionsWindow.IsVisible)
                {
                    friendActionsWindow.Activate();
                    friendActionsWindow.Focus();
                    return;
                }

                settings.IsFriendActionsMenuOpen = true;

                var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowMinimizeButton = false });
                var style = Application.Current.TryFindResource("FriendsActionStyle") as Style;
                if (style != null)
                {
                    window.Content = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        Child = new Grid
                        {
                            Width = 1920,
                            Height = 1080,
                            Children = { new ContentControl { Focusable = false, Style = style } }
                        }
                    };
                }

                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.WindowState = WindowState.Maximized;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var parent = playniteApi.Dialogs.GetCurrentAppWindow();
                if (parent != null)
                {
                    window.Width = parent.Width;
                    window.Height = parent.Height;
                    window.Owner = parent;
                }

                window.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        CloseFriendActionsWindow();
                    }
                };

                window.Closed += (s, e) =>
                {
                    friendActionsWindow = null;
                    settings.IsFriendActionsMenuOpen = false;
                    settings.SelectedFriendForActions = null;
                };

                friendActionsOpenedUtc = DateTime.UtcNow;
                friendActionsWindow = window;
                window.Show();
                window.Activate();
                window.Focus();
            });
        }

        public void OpenSelfStatusWindow()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (selfStatusWindow != null && selfStatusWindow.IsVisible)
                {
                    selfStatusWindow.Activate();
                    selfStatusWindow.Focus();
                    return;
                }

                var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowMinimizeButton = false });
                var style = Application.Current.TryFindResource("FriendsSelfStatusStyle") as Style;
                if (style != null)
                {
                    window.Content = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        Child = new Grid
                        {
                            Width = 1920,
                            Height = 1080,
                            Children = { new ContentControl { Focusable = false, Style = style } }
                        }
                    };
                }

                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.WindowState = WindowState.Maximized;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var parent = playniteApi.Dialogs.GetCurrentAppWindow();
                if (parent != null)
                {
                    window.Width = parent.Width;
                    window.Height = parent.Height;
                    window.Owner = parent;
                }

                window.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        CloseSelfStatusWindow();
                    }
                };

                window.Closed += (s, e) => selfStatusWindow = null;

                selfStatusWindow = window;
                window.Show();
                window.Activate();
                window.Focus();
            });
        }

        public bool CloseFriendProfileWindow()
        {
            if (friendProfileWindow == null || !friendProfileWindow.IsVisible)
            {
                return false;
            }

            friendProfileWindow.Close();
            friendProfileWindow = null;
            return true;
        }

        public bool CloseFriendActionsWindow()
        {
            if (friendActionsWindow == null || !friendActionsWindow.IsVisible)
            {
                settings.IsFriendActionsMenuOpen = false;
                settings.SelectedFriendForActions = null;
                return false;
            }

            friendActionsWindow.Close();
            friendActionsWindow = null;
            settings.IsFriendActionsMenuOpen = false;
            settings.SelectedFriendForActions = null;
            return true;
        }

        public bool CloseSelfStatusWindow()
        {
            if (selfStatusWindow == null || !selfStatusWindow.IsVisible)
            {
                return false;
            }

            selfStatusWindow.Close();
            selfStatusWindow = null;
            return true;
        }

        private void ShowToast(string message, string avatar)
        {
            if (!ShouldRunTimer())
            {
                return;
            }

            toastCts?.Cancel();
            toastCts = new CancellationTokenSource();
            var ct = toastCts.Token;

            InvokeOnUi(() =>
            {
                settings.ToastMessage = message;
                settings.ToastAvatar = avatar;
                settings.ToastToken = DateTime.UtcNow.Ticks;
                settings.ToastFlip = !settings.ToastFlip;
                settings.ToastIsVisible = true;
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ToastDuration, ct).ConfigureAwait(false);
                    InvokeOnUi(() => settings.ToastIsVisible = false);
                }
                catch
                {
                }
            });
        }

        private void DetectAndQueueToast(List<FriendPresenceDto> dtos, string selfSteamId64)
        {
            if (dtos == null || dtos.Count == 0)
            {
                return;
            }

            if (!settings.NotifyOnConnect && !settings.NotifyOnGameStart)
            {
                UpdateBaseline(dtos, selfSteamId64);
                hasBaseline = true;
                return;
            }

            if (!hasBaseline)
            {
                UpdateBaseline(dtos, selfSteamId64);
                hasBaseline = true;
                return;
            }

            var now = DateTime.UtcNow;

            foreach (var f in dtos)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.steamid))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(selfSteamId64) && f.steamid == selfSteamId64)
                {
                    continue;
                }

                var newState = f.state ?? "offline";
                var newGame = string.IsNullOrWhiteSpace(f.game) ? null : f.game;
                lastState.TryGetValue(f.steamid, out var oldState);
                lastGame.TryGetValue(f.steamid, out var oldGame);
                oldState = oldState ?? "offline";

                var shouldToast = false;
                string message = null;

                if (settings.NotifyOnConnect && oldState == "offline" && newState != "offline")
                {
                    shouldToast = true;
                    var tpl = GetStringSafe("LOCSteamFriendsToast_Online", "{0} is now {1}");
                    message = string.Format(tpl, f.name ?? "Friend", f.stateLoc ?? newState);
                }

                if (!shouldToast && settings.NotifyOnGameStart)
                {
                    var becameInGame = oldState != "ingame" && newState == "ingame";
                    var startedGame = string.IsNullOrWhiteSpace(oldGame) && !string.IsNullOrWhiteSpace(newGame);

                    if (becameInGame || startedGame)
                    {
                        shouldToast = true;
                        var tpl = GetStringSafe("LOCSteamFriendsToast_GameStart", "{0} started playing {1}");
                        message = string.Format(tpl, f.name ?? "Friend", !string.IsNullOrWhiteSpace(newGame) ? newGame : LocalizeStateTheme("ingame"));
                    }
                }

                if (shouldToast)
                {
                    if (lastToastUtc.TryGetValue(f.steamid, out var last) && (now - last) < ToastCooldown)
                    {
                        lastState[f.steamid] = newState;
                        lastGame[f.steamid] = newGame;
                        continue;
                    }

                    lastToastUtc[f.steamid] = now;
                    ShowToast(message, f.avatar);

                    lastState[f.steamid] = newState;
                    lastGame[f.steamid] = newGame;
                    break;
                }

                lastState[f.steamid] = newState;
                lastGame[f.steamid] = newGame;
            }
        }

        private void UpdateBaseline(List<FriendPresenceDto> dtos, string selfSteamId64)
        {
            foreach (var f in dtos)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.steamid))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(selfSteamId64) && f.steamid == selfSteamId64)
                {
                    continue;
                }

                lastState[f.steamid] = f.state ?? "offline";
                lastGame[f.steamid] = string.IsNullOrWhiteSpace(f.game) ? null : f.game;
            }
        }


        public void ResetCurrentGameFriendDetails()
        {
            currentDetailsSteamAppId = 0;

            InvokeOnUi(() =>
            {
                settings?.EnsureSteamFriendsRuntimeCollections();

                if (settings == null)
                {
                    return;
                }

                settings.SteamFriendsPlayingCurrentGame?.Clear();
                settings.SteamFriendsPlayingCurrentGameAvailable = false;
                settings.SteamFriendsPlayingCurrentGameCount = 0;
                settings.SteamFriendsPlayingCurrentGameSummary = string.Empty;

                settings.SteamFriendsWhoPlayedCurrentGame?.Clear();
                settings.SteamFriendsWhoPlayedAvailable = false;
                settings.SteamFriendsWhoPlayedLoading = false;
                settings.SteamFriendsWhoPlayedCount = 0;
                settings.SteamFriendsWhoPlayedSummary = string.Empty;
                settings.SteamFriendsWhoPlayedError = string.Empty;
            });
        }

        public void UpdateCurrentGameFriendDetails(int steamAppId)
        {
            if (steamAppId <= 0 || settings?.SteamFriendsEnabled != true ||
                SteamNonGameAppFilter.IsKnownNonGameSteamApp(steamAppId))
            {
                ResetCurrentGameFriendDetails();
                return;
            }

            currentDetailsSteamAppId = steamAppId;
            settings?.EnsureSteamFriendsRuntimeCollections();

            List<FriendPresenceDto> snapshot;
            lock (presenceSnapshotLock)
            {
                snapshot = lastPresenceSnapshot?.Select(ClonePresenceForDetails).Where(x => x != null).ToList() ?? new List<FriendPresenceDto>();
            }

            if (snapshot.Count == 0 && settings?.Friends != null)
            {
                snapshot = settings.Friends.Select(ClonePresenceForDetails).Where(x => x != null).ToList();
            }

            UpdateFriendsPlayingCurrentGameFromPresence(snapshot);
            UpdateFriendsWhoPlayedCurrentGameFromCache();
            ScheduleDeferredFriendsPlayedGamesRefreshIfNeeded();
            _ = VerifyCurrentGameIsRealSteamGameAsync(steamAppId);
        }

        private async Task VerifyCurrentGameIsRealSteamGameAsync(int steamAppId)
        {
            try
            {
                if (!await IsSteamAppAllowedAsGameAsync(steamAppId).ConfigureAwait(false))
                {
                    if (currentDetailsSteamAppId == steamAppId)
                    {
                        ResetCurrentGameFriendDetails();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper][SteamFriends] Failed to verify Steam app type for AppId={steamAppId}.");
            }
        }

        private void UpdateFriendsPlayingCurrentGameFromPresence(IEnumerable<FriendPresenceDto> presences)
        {
            var appId = currentDetailsSteamAppId;
            if (appId <= 0 || settings == null)
            {
                return;
            }

            var matches = presences?
                .Where(f => f != null && f.appid == appId && !string.IsNullOrWhiteSpace(f.steamid) &&
                            !SteamNonGameAppFilter.IsKnownNonGameSteamApp(f.appid, f.game))
                .GroupBy(f => f.steamid, StringComparer.OrdinalIgnoreCase)
                .Select(g => ClonePresenceForDetails(g.First()))
                .OrderBy(f => f.name)
                .ToList() ?? new List<FriendPresenceDto>();

            InvokeOnUi(() =>
            {
                settings.EnsureSteamFriendsRuntimeCollections();
                settings.SteamFriendsPlayingCurrentGame.Clear();

                foreach (var item in matches)
                {
                    settings.SteamFriendsPlayingCurrentGame.Add(item);
                }

                settings.SteamFriendsPlayingCurrentGameCount = matches.Count;
                settings.SteamFriendsPlayingCurrentGameAvailable = matches.Count > 0;
                settings.SteamFriendsPlayingCurrentGameSummary = BuildCurrentPlayingSummary(matches);
            });
        }

        private void UpdateFriendsWhoPlayedCurrentGameFromCache()
        {
            var appId = currentDetailsSteamAppId;
            if (appId <= 0 || settings == null)
            {
                return;
            }

            LoadFriendsPlayedGamesCacheFromDisk();

            List<SteamFriendPlayedGameDto> matches;
            lock (friendsPlayedGamesCacheLock)
            {
                var key = appId.ToString();
                if (friendsPlayedGamesCache?.games != null && friendsPlayedGamesCache.games.TryGetValue(key, out var cached) && cached != null)
                {
                    matches = cached
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.steamid))
                        .Select(ClonePlayedGameDto)
                        .OrderByDescending(x => x.playtimeForeverMinutes)
                        .ThenBy(x => x.name)
                        .ToList();
                }
                else
                {
                    matches = new List<SteamFriendPlayedGameDto>();
                }
            }

            InvokeOnUi(() =>
            {
                settings.EnsureSteamFriendsRuntimeCollections();
                settings.SteamFriendsWhoPlayedCurrentGame.Clear();

                foreach (var item in matches)
                {
                    settings.SteamFriendsWhoPlayedCurrentGame.Add(item);
                }

                settings.SteamFriendsWhoPlayedCount = matches.Count;
                settings.SteamFriendsWhoPlayedAvailable = matches.Count > 0;
                settings.SteamFriendsWhoPlayedSummary = BuildAlreadyPlayedSummary(matches);
                settings.SteamFriendsWhoPlayedError = string.Empty;
                settings.SteamFriendsWhoPlayedLoading = settings.SteamFriendsPlayedGamesCacheRefreshing;
            });
        }

        public void ScheduleDeferredFriendsPlayedGamesRefreshIfNeeded()
        {
            if (settings?.SteamFriendsEnabled != true || friendsPlayedGamesAutoRefreshAttempted || friendsPlayedGamesAutoRefreshScheduled)
            {
                return;
            }

            LoadFriendsPlayedGamesCacheFromDisk();
            if (!IsFriendsPlayedGamesCacheStale())
            {
                return;
            }

            friendsPlayedGamesAutoRefreshScheduled = true;
            friendsPlayedGamesAutoRefreshAttempted = true;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(friendsPlayedGamesAutoRefreshDelay).ConfigureAwait(false);

                    if (!ShouldRunDeferredFriendsPlayedGamesRefresh())
                    {
                        friendsPlayedGamesAutoRefreshScheduled = false;
                        return;
                    }

                    await RefreshFriendsPlayedGamesCacheAsync(false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper][SteamFriends] Deferred friends played games refresh failed.");
                }
                finally
                {
                    friendsPlayedGamesAutoRefreshScheduled = false;
                }
            });
        }

        private bool ShouldRunDeferredFriendsPlayedGamesRefresh()
        {
            if (settings?.SteamFriendsEnabled != true || !IsFriendsPlayedGamesCacheStale())
            {
                return false;
            }

            try
            {
                if (playniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen)
                {
                    return false;
                }

                if (isAnikiThemeActive != null && !isAnikiThemeActive())
                {
                    return false;
                }

                if (settings.IsFastNavigating)
                {
                    return false;
                }

                if (IsAnyGameRunningOrLaunching())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private bool IsAnyGameRunningOrLaunching()
        {
            try
            {
                return playniteApi?.Database?.Games?.Any(x => x != null && (x.IsRunning || x.IsLaunching)) == true;
            }
            catch
            {
                return true;
            }
        }

        private async Task RefreshFriendsPlayedGamesCacheAsync(bool force)
        {
            if (settings == null || settings.SteamFriendsEnabled != true)
            {
                return;
            }

            LoadFriendsPlayedGamesCacheFromDisk();
            if (!force && !IsFriendsPlayedGamesCacheStale())
            {
                UpdateFriendsPlayedGamesCacheStatus();
                UpdateFriendsWhoPlayedCurrentGameFromCache();
                return;
            }

            if (!await friendsPlayedGamesRefreshGate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                var apiKey = settings.SteamApiKey?.Trim();
                var steamIdInput = GetEffectiveSteamIdInput();
                var steamId64 = await ResolveSteamId64Async(apiKey, steamIdInput).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId64))
                {
                    InvokeOnUi(() =>
                    {
                        settings.SteamFriendsPlayedGamesCacheStatus = GetStringSafe("LOCSteamFriends_PlayedCache_MissingConfig", "Missing Steam API key or Steam account.");
                        settings.SteamFriendsPlayedGamesCacheRefreshing = false;
                        settings.SteamFriendsWhoPlayedLoading = false;
                        settings.SteamFriendsWhoPlayedError = settings.SteamFriendsPlayedGamesCacheStatus;
                    });
                    return;
                }

                InvokeOnUi(() =>
                {
                    settings.SteamFriendsPlayedGamesCacheRefreshing = true;
                    settings.SteamFriendsWhoPlayedLoading = true;
                    settings.SteamFriendsPlayedGamesCacheStatus = GetStringSafe("LOCSteamFriends_PlayedCache_Preparing", "Preparing friends played games refresh...");
                });

                var friends = await GetExtendedFriendsAsync(apiKey, steamId64).ConfigureAwait(false);
                var friendIds = friends?
                    .Where(f => f != null && !string.IsNullOrWhiteSpace(f.SteamId))
                    .Select(f => f.SteamId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();

                if (friendIds.Count == 0)
                {
                    InvokeOnUi(() =>
                    {
                        settings.SteamFriendsPlayedGamesCacheStatus = GetStringSafe("LOCSteamFriends_PlayedCache_NoFriends", "No Steam friends returned by Steam API.");
                        settings.SteamFriendsPlayedGamesCacheRefreshing = false;
                        settings.SteamFriendsWhoPlayedLoading = false;
                    });
                    return;
                }

                var summaries = await steamClient.GetPlayerSummariesAsync(apiKey, friendIds).ConfigureAwait(false);
                var summariesById = summaries?
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.SteamId))
                    .GroupBy(s => s.SteamId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, SteamPlayerSummary>(StringComparer.OrdinalIgnoreCase);

                var newCache = new SteamFriendsPlayedGamesCache
                {
                    lastRefreshUtc = DateTime.UtcNow,
                    games = new Dictionary<string, List<SteamFriendPlayedGameDto>>()
                };

                var processed = 0;
                var avatarDownloadsScheduled = 0;
                foreach (var friendId in friendIds)
                {
                    processed++;

                    InvokeOnUi(() =>
                    {
                        var tpl = GetStringSafe("LOCSteamFriends_PlayedCache_Refreshing", "Refreshing friends played games... {0}/{1}");
                        settings.SteamFriendsPlayedGamesCacheStatus = string.Format(tpl, processed, friendIds.Count);
                    });

                    var ownedGames = await steamClient.GetOwnedGamesAsync(apiKey, friendId).ConfigureAwait(false);
                    if (ownedGames == null || ownedGames.Count == 0)
                    {
                        await Task.Delay(120).ConfigureAwait(false);
                        continue;
                    }

                    summariesById.TryGetValue(friendId, out var summary);
                    var friendName = summary?.PersonaName;
                    if (string.IsNullOrWhiteSpace(friendName))
                    {
                        friendName = friendId;
                    }

                    var avatar = GetFriendAvatarSource(friendId, summary?.AvatarFull);
                    if (summary != null && !string.IsNullOrWhiteSpace(summary.AvatarFull) && avatarDownloadsScheduled < MaxAvatarDownloadsPerRefresh)
                    {
                        avatarDownloadsScheduled++;
                        _ = CacheAvatarAsync(friendId, summary.AvatarFull);
                    }

                    foreach (var owned in ownedGames.Where(g => g != null && g.AppId > 0 && g.PlaytimeForever > 0 &&
                                                         !SteamNonGameAppFilter.IsKnownNonGameSteamApp(g.AppId, g.Name)))
                    {
                        var key = owned.AppId.ToString();
                        if (!newCache.games.TryGetValue(key, out var list))
                        {
                            list = new List<SteamFriendPlayedGameDto>();
                            newCache.games[key] = list;
                        }

                        list.Add(new SteamFriendPlayedGameDto
                        {
                            steamid = friendId,
                            name = friendName,
                            avatar = avatar,
                            appid = owned.AppId,
                            playtimeForeverMinutes = owned.PlaytimeForever,
                            playtimeForeverDisplay = FormatMinutesToHours(owned.PlaytimeForever)
                        });
                    }

                    await Task.Delay(120).ConfigureAwait(false);
                }

                lock (friendsPlayedGamesCacheLock)
                {
                    friendsPlayedGamesCache = newCache;
                    friendsPlayedGamesCacheLoaded = true;
                }

                SaveFriendsPlayedGamesCacheToDisk();

                InvokeOnUi(() =>
                {
                    var tpl = GetStringSafe("LOCSteamFriends_PlayedCache_Done", "Friends played games refreshed: {0} friends scanned.");
                    settings.SteamFriendsPlayedGamesCacheStatus = string.Format(tpl, processed);
                    settings.SteamFriendsPlayedGamesCacheRefreshing = false;
                    settings.SteamFriendsWhoPlayedLoading = false;
                });

                UpdateFriendsPlayedGamesCacheStatus(false);
                UpdateFriendsWhoPlayedCurrentGameFromCache();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][SteamFriends] Failed to refresh friends played games cache.");
                InvokeOnUi(() =>
                {
                    settings.SteamFriendsPlayedGamesCacheStatus = GetStringSafe("LOCSteamFriends_PlayedCache_Error", "Failed to refresh friends played games.");
                    settings.SteamFriendsPlayedGamesCacheRefreshing = false;
                    settings.SteamFriendsWhoPlayedLoading = false;
                    settings.SteamFriendsWhoPlayedError = settings.SteamFriendsPlayedGamesCacheStatus;
                });
            }
            finally
            {
                friendsPlayedGamesRefreshGate.Release();
            }
        }

        private void LoadFriendsPlayedGamesCacheFromDisk()
        {
            lock (friendsPlayedGamesCacheLock)
            {
                if (friendsPlayedGamesCacheLoaded)
                {
                    return;
                }
            }

            try
            {
                var path = FriendsPlayedGamesCachePath;
                if (!File.Exists(path))
                {
                    lock (friendsPlayedGamesCacheLock)
                    {
                        friendsPlayedGamesCache = new SteamFriendsPlayedGamesCache();
                        friendsPlayedGamesCacheLoaded = true;
                    }
                    return;
                }

                var json = File.ReadAllText(path);
                var cache = Serialization.FromJson<SteamFriendsPlayedGamesCache>(json) ?? new SteamFriendsPlayedGamesCache();
                if (cache.games == null)
                {
                    cache.games = new Dictionary<string, List<SteamFriendPlayedGameDto>>();
                }

                foreach (var entry in cache.games.Values.Where(v => v != null))
                {
                    foreach (var item in entry.Where(x => x != null))
                    {
                        if (string.IsNullOrWhiteSpace(item.playtimeForeverDisplay))
                        {
                            item.playtimeForeverDisplay = FormatMinutesToHours(item.playtimeForeverMinutes);
                        }
                    }
                }

                lock (friendsPlayedGamesCacheLock)
                {
                    friendsPlayedGamesCache = cache;
                    friendsPlayedGamesCacheLoaded = true;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][SteamFriends] Failed to load friends played games cache.");
                lock (friendsPlayedGamesCacheLock)
                {
                    friendsPlayedGamesCache = new SteamFriendsPlayedGamesCache();
                    friendsPlayedGamesCacheLoaded = true;
                }
            }
        }

        private void SaveFriendsPlayedGamesCacheToDisk()
        {
            try
            {
                SteamFriendsPlayedGamesCache snapshot;
                lock (friendsPlayedGamesCacheLock)
                {
                    snapshot = friendsPlayedGamesCache ?? new SteamFriendsPlayedGamesCache();
                }

                var path = FriendsPlayedGamesCachePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, Serialization.ToJson(snapshot));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][SteamFriends] Failed to save friends played games cache.");
            }
        }

        private bool IsFriendsPlayedGamesCacheStale()
        {
            LoadFriendsPlayedGamesCacheFromDisk();

            lock (friendsPlayedGamesCacheLock)
            {
                if (friendsPlayedGamesCache == null || friendsPlayedGamesCache.lastRefreshUtc == DateTime.MinValue)
                {
                    return true;
                }

                return (DateTime.UtcNow - friendsPlayedGamesCache.lastRefreshUtc) > friendsPlayedGamesCacheTtl;
            }
        }

        private void UpdateFriendsPlayedGamesCacheStatus(bool preserveDoneStatus = true)
        {
            LoadFriendsPlayedGamesCacheFromDisk();

            DateTime lastRefresh;
            var stale = true;
            lock (friendsPlayedGamesCacheLock)
            {
                lastRefresh = friendsPlayedGamesCache?.lastRefreshUtc ?? DateTime.MinValue;
                stale = lastRefresh == DateTime.MinValue || (DateTime.UtcNow - lastRefresh) > friendsPlayedGamesCacheTtl;
            }

            InvokeOnUi(() =>
            {
                if (settings == null)
                {
                    return;
                }

                settings.SteamFriendsPlayedGamesCacheStale = stale;

                if (settings.SteamFriendsPlayedGamesCacheRefreshing)
                {
                    return;
                }

                if (lastRefresh == DateTime.MinValue)
                {
                    settings.SteamFriendsPlayedGamesCacheStatus = GetStringSafe("LOCSteamFriends_PlayedCache_Empty", "Friends played games cache not created yet.");
                    return;
                }

                if (preserveDoneStatus && !string.IsNullOrWhiteSpace(settings.SteamFriendsPlayedGamesCacheStatus) &&
                    settings.SteamFriendsPlayedGamesCacheStatus.IndexOf(GetStringSafe("LOCSteamFriends_PlayedCache_DoneMarker", "refreshed"), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }

                var tpl = stale
                    ? GetStringSafe("LOCSteamFriends_PlayedCache_Stale", "Friends played games cache is old. Last refresh: {0}.")
                    : GetStringSafe("LOCSteamFriends_PlayedCache_Loaded", "Friends played games cache loaded. Last refresh: {0}.");

                settings.SteamFriendsPlayedGamesCacheStatus = string.Format(tpl, lastRefresh.ToLocalTime().ToString("g"));
            });
        }

        private string BuildCurrentPlayingSummary(List<FriendPresenceDto> matches)
        {
            if (matches == null || matches.Count == 0)
            {
                return string.Empty;
            }

            if (matches.Count == 1)
            {
                return string.Format(GetStringSafe("LOCSteamFriends_CurrentPlaying_One", "{0} is playing now"), matches[0].name ?? "Friend");
            }

            if (matches.Count == 2)
            {
                return string.Format(GetStringSafe("LOCSteamFriends_CurrentPlaying_Two", "{0} and {1} are playing now"), matches[0].name ?? "Friend", matches[1].name ?? "Friend");
            }

            return string.Format(GetStringSafe("LOCSteamFriends_CurrentPlaying_Many", "{0}, {1} and {2} others are playing now"), matches[0].name ?? "Friend", matches[1].name ?? "Friend", matches.Count - 2);
        }

        private string BuildAlreadyPlayedSummary(List<SteamFriendPlayedGameDto> matches)
        {
            if (matches == null || matches.Count == 0)
            {
                return string.Empty;
            }

            if (matches.Count == 1)
            {
                return string.Format(GetStringSafe("LOCSteamFriends_AlreadyPlayed_One", "{0} has already played this game"), matches[0].name ?? "Friend");
            }

            if (matches.Count == 2)
            {
                return string.Format(GetStringSafe("LOCSteamFriends_AlreadyPlayed_Two", "{0} and {1} have already played this game"), matches[0].name ?? "Friend", matches[1].name ?? "Friend");
            }

            return string.Format(GetStringSafe("LOCSteamFriends_AlreadyPlayed_Many", "{0}, {1} and {2} other friends have already played this game"), matches[0].name ?? "Friend", matches[1].name ?? "Friend", matches.Count - 2);
        }

        private FriendPresenceDto ClonePresenceForDetails(FriendPresenceDto source)
        {
            if (source == null)
            {
                return null;
            }

            return new FriendPresenceDto
            {
                name = source.name,
                state = source.state,
                stateLoc = source.stateLoc,
                game = source.game,
                appid = source.appid,
                steamid = source.steamid,
                avatar = source.avatar
            };
        }

        private SteamFriendPlayedGameDto ClonePlayedGameDto(SteamFriendPlayedGameDto source)
        {
            if (source == null)
            {
                return null;
            }

            return new SteamFriendPlayedGameDto
            {
                steamid = source.steamid,
                name = source.name,
                avatar = source.avatar,
                appid = source.appid,
                playtimeForeverMinutes = source.playtimeForeverMinutes,
                playtimeForeverDisplay = string.IsNullOrWhiteSpace(source.playtimeForeverDisplay)
                    ? FormatMinutesToHours(source.playtimeForeverMinutes)
                    : source.playtimeForeverDisplay
            };
        }

        private async Task<bool> IsSteamAppAllowedAsGameAsync(int appId)
        {
            if (appId <= 0)
            {
                return false;
            }

            if (SteamNonGameAppFilter.IsKnownNonGameSteamApp(appId))
            {
                return false;
            }

            string cachedType;
            if (steamAppTypeCache.TryGetValue(appId, out cachedType))
            {
                return !SteamNonGameAppFilter.IsNonGameSteamAppType(cachedType);
            }

            var appType = await steamClient.GetSteamAppTypeAsync(appId).ConfigureAwait(false);
            steamAppTypeCache[appId] = appType ?? string.Empty;

            return !SteamNonGameAppFilter.IsNonGameSteamAppType(appType);
        }

        private string GetEffectiveSteamIdInput()
        {
            if (!string.IsNullOrWhiteSpace(settings?.SteamAccountSteamId64))
            {
                return settings.SteamAccountSteamId64.Trim();
            }

            return settings?.SteamId64?.Trim() ?? string.Empty;
        }

        private async Task RefreshSteamPresenceAsync()
        {
            if (isRefreshing || !ShouldRunTimer())
            {
                return;
            }

            if (DateTime.UtcNow < pausedUntilUtc)
            {
                return;
            }

            var steamRunning = IsSteamClientRunning();
            InvokeOnUi(() => settings.IsSteamRunning = steamRunning);

            var apiKey = settings.SteamApiKey?.Trim();
            var steamIdInput = GetEffectiveSteamIdInput();
            var steamId64 = await ResolveSteamId64Async(apiKey, steamIdInput).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId64))
            {
                ApplyMissingConfigurationState();

                try { friendActivityHubService?.ClearUnavailable(); } catch { }
                lastUiSignature = null;
                return;
            }

            isRefreshing = true;

            try
            {
                var nowUtc = DateTime.UtcNow;
                MaybeCleanupAvatarCache(nowUtc);

                var shouldRefreshFriendIds = cachedFriendIds == null || cachedFriendIds.Count == 0 || (nowUtc - friendIdsLastFetchUtc) > friendIdsCacheTtl;
                if (shouldRefreshFriendIds)
                {
                    var ids = await steamClient.GetFriendSteamIdsAsync(apiKey, steamId64).ConfigureAwait(false);
                    cachedFriendIds = ids != null ? ids.ToList() : new List<string>();
                    friendIdsLastFetchUtc = nowUtc;
                }

                if (cachedFriendIds == null || cachedFriendIds.Count == 0)
                {
                    InvokeOnUi(() =>
                    {
                        settings.OnlineCount = 0;
                        settings.InGameCount = 0;
                        settings.OfflineCount = 0;
                        settings.Friends.Clear();
                        settings.LastError = "No friends returned by Steam API.";
                        settings.LastUpdateUtc = DateTime.MinValue;
                    });

                    try { friendActivityHubService?.ClearUnavailable(); } catch { }
                    lastUiSignature = "empty";
                    lastSuccessUtc = nowUtc;
                    return;
                }

                var idsForSummaries = cachedFriendIds.ToList();
                if (!idsForSummaries.Contains(steamId64))
                {
                    idsForSummaries.Add(steamId64);
                }

                var players = await steamClient.GetPlayerSummariesAsync(apiKey, idsForSummaries).ConfigureAwait(false);
                var self = players?.FirstOrDefault(p => p.SteamId == steamId64);
                var friendPlayers = players?.Where(p => p.SteamId != steamId64).ToList() ?? new List<SteamPlayerSummary>();

                var avatarDownloadsScheduled = 0;

                var dtos = friendPlayers.Select(p =>
                {
                    var rawState = MapState(p);
                    var dto = new FriendPresenceDto
                    {
                        name = p.PersonaName,
                        state = rawState,
                        stateLoc = LocalizeStateTheme(rawState),
                        game = string.IsNullOrWhiteSpace(p.GameExtraInfo) ? null : p.GameExtraInfo,
                        appid = ParseSteamAppId(p.GameId),
                        steamid = p.SteamId
                    };

                    var localPath = GetAvatarFilePath(dto.steamid);
                    if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                    {
                        dto.avatar = ToFileUri(localPath);
                    }
                    else
                    {
                        dto.avatar = p.AvatarFull;

                        if (avatarDownloadsScheduled < MaxAvatarDownloadsPerRefresh)
                        {
                            avatarDownloadsScheduled++;
                            _ = CacheAvatarAsync(dto.steamid, p.AvatarFull);
                        }
                    }

                    return dto;
                }).ToList();

                lock (presenceSnapshotLock)
                {
                    lastPresenceSnapshot = dtos.Select(ClonePresenceForDetails).Where(x => x != null).ToList();
                }

                UpdateFriendsPlayingCurrentGameFromPresence(dtos);

                DetectAndQueueToast(dtos, steamId64);
                UpdateSelfInfo(self, steamId64, steamRunning);

                var onlineCount = dtos.Count(d => d.state != "offline");
                var inGameCount = dtos.Count(d => d.state == "ingame");
                var offlineCount = dtos.Count(d => d.state == "offline");

                try
                {
                    friendActivityHubService?.UpdateAfterPresenceRefresh(apiKey, steamId64, cachedFriendIds, dtos);
                }
                catch
                {
                }

                var onlineTop = dtos
                    .Where(d => d.state != "offline")
                    .OrderBy(d => Rank(d.state))
                    .ThenBy(d => d.name)
                    .Take(FixedMaxFriendsShown)
                    .ToList();

                var offlineList = new List<FriendPresenceDto>();
                if (settings.ShowOffline && FixedMaxOfflineShown > 0)
                {
                    offlineList = dtos
                        .Where(d => d.state == "offline")
                        .OrderBy(d => d.name)
                        .Take(FixedMaxOfflineShown)
                        .ToList();

                    foreach (var f in offlineList)
                    {
                        var localPath = GetAvatarFilePath(f.steamid);
                        f.avatar = !string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath) ? ToFileUri(localPath) : null;
                    }
                }

                var signature = BuildUiSignature(onlineCount, inGameCount, offlineCount, onlineTop, offlineList);
                if (signature == lastUiSignature)
                {
                    InvokeOnUi(() =>
                    {
                        settings.LastError = null;
                        settings.LastUpdateUtc = nowUtc;
                    });

                    lastSuccessUtc = nowUtc;
                    return;
                }

                lastUiSignature = signature;

                InvokeOnUi(() =>
                {
                    settings.OnlineCount = onlineCount;
                    settings.InGameCount = inGameCount;
                    settings.OfflineCount = offlineCount;
                    settings.Friends.Clear();

                    foreach (var item in onlineTop)
                    {
                        settings.Friends.Add(item);
                    }

                    foreach (var item in offlineList)
                    {
                        settings.Friends.Add(item);
                    }

                    settings.LastError = null;
                    settings.LastUpdateUtc = nowUtc;
                });

                lastSuccessUtc = nowUtc;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper][SteamFriends] Failed to refresh Steam friends presence.");

                if (DateTime.UtcNow - lastSuccessUtc > TimeSpan.FromMinutes(10))
                {
                    InvokeOnUi(() =>
                    {
                        settings.OnlineCount = 0;
                        settings.InGameCount = 0;
                        settings.OfflineCount = 0;
                        settings.Friends.Clear();
                        settings.LastUpdateUtc = DateTime.MinValue;
                        settings.LastError = "Steam API error.";
                    });

                    lastUiSignature = null;
                }
            }
            finally
            {
                isRefreshing = false;
            }
        }

        public async Task RefreshSelfAvatarOnlyAsync()
        {
            if (settings == null ||
                !await selfAvatarRefreshGate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                var apiKey = settings.SteamApiKey?.Trim();
                var steamIdInput = GetEffectiveSteamIdInput();
                var steamId64 = await ResolveSteamId64Async(apiKey, steamIdInput).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(steamId64))
                {
                    InvokeOnUi(() => settings.SelfAvatar = null);
                    return;
                }

                var localPath = GetAvatarFilePath(steamId64);
                if (IsUsableImageFile(localPath))
                {
                    var cachedAvatar = ToFileUri(localPath);
                    InvokeOnUi(() => settings.SelfAvatar = cachedAvatar);
                }
                else
                {
                    InvokeOnUi(() => settings.SelfAvatar = null);
                }

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return;
                }

                var players = await steamClient
                    .GetPlayerSummariesAsync(apiKey, new[] { steamId64 })
                    .ConfigureAwait(false);

                var self = players?.FirstOrDefault(player =>
                    player != null &&
                    string.Equals(player.SteamId, steamId64, StringComparison.OrdinalIgnoreCase));

                if (self == null)
                {
                    return;
                }

                string avatarSource;
                if (IsUsableImageFile(localPath))
                {
                    avatarSource = ToFileUri(localPath);
                }
                else
                {
                    avatarSource = self.AvatarFull;
                    _ = CacheAvatarAsync(steamId64, self.AvatarFull);
                }

                InvokeOnUi(() => settings.SelfAvatar = avatarSource);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][SteamFriends] Failed to refresh the Steam profile avatar.");
            }
            finally
            {
                selfAvatarRefreshGate.Release();
            }
        }

        private void UpdateSelfInfo(SteamPlayerSummary self, string steamId64, bool steamRunning)
        {
            if (self != null)
            {
                var myState = MapState(self);
                string myAvatar = null;
                var myLocalPath = GetAvatarFilePath(steamId64);

                if (!string.IsNullOrWhiteSpace(myLocalPath) && File.Exists(myLocalPath))
                {
                    myAvatar = ToFileUri(myLocalPath);
                }
                else
                {
                    myAvatar = self.AvatarFull;
                    _ = CacheAvatarAsync(steamId64, self.AvatarFull);
                }

                InvokeOnUi(() =>
                {
                    settings.SelfName = self.PersonaName;
                    settings.SelfAvatar = myAvatar;

                    if (!steamRunning)
                    {
                        settings.SelfState = "notrunning";
                        settings.SelfStateLoc = GetStringSafe("LOCSteamNotRunning", "Steam not running");
                        settings.SelfGame = null;
                    }
                    else
                    {
                        settings.SelfState = myState;
                        settings.SelfStateLoc = LocalizeStateTheme(myState);
                        settings.SelfGame = string.IsNullOrWhiteSpace(self.GameExtraInfo) ? null : self.GameExtraInfo;
                    }
                });
            }
            else
            {
                InvokeOnUi(() =>
                {
                    settings.SelfName = null;
                    settings.SelfAvatar = null;
                    settings.SelfState = !steamRunning ? "notrunning" : "offline";
                    settings.SelfStateLoc = !steamRunning ? GetStringSafe("LOCSteamNotRunning", "Steam not running") : LocalizeStateTheme("offline");
                    settings.SelfGame = null;
                });
            }
        }

        public async Task OpenFriendProfileAsync(string friendSteamId, bool forceRefresh = false)
        {
            if (settings == null || string.IsNullOrWhiteSpace(friendSteamId))
            {
                return;
            }

            var apiKey = settings.SteamApiKey?.Trim();
            var steamIdInput = GetEffectiveSteamIdInput();
            var selfSteamId64 = await ResolveSteamId64Async(apiKey, steamIdInput).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(selfSteamId64))
            {
                InvokeOnUi(() =>
                {
                    settings.IsFriendProfileOpen = true;
                    settings.SelectedFriendSteamId = friendSteamId;
                    settings.SelectedFriendProfile = null;
                    settings.FriendProfileError = "Missing Steam API key or Steam account.";
                    settings.IsFriendProfileLoading = false;
                });
                return;
            }

            InvokeOnUi(() =>
            {
                settings.IsFriendProfileOpen = true;
                settings.SelectedFriendSteamId = friendSteamId;
                // Do not keep the previous friend's profile visible while the new profile is loading.
                settings.SelectedFriendProfile = null;
                settings.FriendProfileError = null;
                settings.IsFriendProfileLoading = true;
            });

            try
            {
                if (!forceRefresh)
                {
                    var cached = friendProfileCache.TryLoad(friendSteamId);
                    if (cached?.profile != null && (DateTime.UtcNow - cached.cachedAtUtc) <= friendProfileCacheTtl)
                    {
                        PrepareCachedProfile(cached.profile, friendSteamId);
                        InvokeOnUi(() =>
                        {
                            if (settings.SelectedFriendSteamId == friendSteamId)
                            {
                                settings.SelectedFriendProfile = cached.profile;
                                settings.FriendProfileError = null;
                            }

                            settings.IsFriendProfileLoading = false;
                        });
                        return;
                    }
                }

                var summaryTask = steamClient.GetPlayerSummariesAsync(apiKey, new[] { friendSteamId });
                var allRecentGamesTask = steamClient.GetAllRecentlyPlayedGamesAsync(apiKey, friendSteamId);
                var friendsTask = GetExtendedFriendsAsync(apiKey, selfSteamId64);
                var steamLevelTask = steamClient.GetSteamLevelAsync(apiKey, friendSteamId);
                var badgesTask = steamClient.GetBadgesAsync(apiKey, friendSteamId);

                await Task.WhenAll(summaryTask, allRecentGamesTask, friendsTask, steamLevelTask, badgesTask).ConfigureAwait(false);

                var summary = summaryTask.Result?.FirstOrDefault();
                if (summary == null)
                {
                    throw new Exception("Friend summary not returned by Steam.");
                }

                var allRecentGames = allRecentGamesTask.Result ?? new List<SteamRecentlyPlayedGame>();
                var filteredRecentGames = allRecentGames
                    .Where(g => g != null && !SteamNonGameAppFilter.IsKnownNonGameSteamApp(g.AppId, g.Name))
                    .ToList();
                var topRecentGames = filteredRecentGames.Take(3).ToList();
                var recent2WeeksTotalMinutes = filteredRecentGames.Sum(g => g.Playtime2Weeks);
                var badges = badgesTask.Result ?? new List<SteamBadge>();
                var steamFriend = friendsTask.Result?.FirstOrDefault(f => f?.SteamId == friendSteamId);

                if (!string.IsNullOrWhiteSpace(summary.AvatarFull))
                {
                    // For the friend profile header, prefer a real local file when possible.
                    // It avoids empty WPF Image controls when remote Steam avatar loading is delayed/blocked.
                    await CacheAvatarAsync(friendSteamId, summary.AvatarFull).ConfigureAwait(false);
                }

                var avatar = GetFriendAvatarSource(friendSteamId, summary.AvatarFull);

                var rawState = MapState(summary);
                var profile = new FriendProfileDto
                {
                    steamid = friendSteamId,
                    name = summary.PersonaName,
                    avatar = avatar,
                    state = rawState,
                    stateLoc = LocalizeStateTheme(rawState),
                    game = string.IsNullOrWhiteSpace(summary.GameExtraInfo) ? null : summary.GameExtraInfo,
                    isProfilePublic = summary.CommunityVisibilityState == 3,
                    lastLogoffUtc = UnixToUtc(summary.LastLogOff),
                    friendSinceUtc = steamFriend != null ? UnixToUtc(steamFriend.FriendSince) : null,
                    steamLevel = steamLevelTask.Result,
                    badgesCount = badges.Count,
                    recentPlaytime2WeeksMinutes = recent2WeeksTotalMinutes,
                    recentPlaytime2WeeksDisplay = FormatMinutesToHours(recent2WeeksTotalMinutes),
                    recentGames = topRecentGames.Select(g => new RecentGameDto
                    {
                        appid = g.AppId,
                        name = g.Name,
                        playtime2WeeksMinutes = g.Playtime2Weeks,
                        playtimeForeverMinutes = g.PlaytimeForever,
                        playtime2WeeksDisplay = FormatMinutesToHours(g.Playtime2Weeks),
                        playtimeForeverDisplay = FormatMinutesToHours(g.PlaytimeForever),
                        headerImageUrl = GetGameHeaderSource(g.AppId)
                    }).ToList(),
                    recentAchievements = LoadRecentFriendAchievements(friendSteamId)
                };

                friendProfileCache.Save(new CachedFriendProfile
                {
                    steamid = friendSteamId,
                    cachedAtUtc = DateTime.UtcNow,
                    profile = profile
                });

                InvokeOnUi(() =>
                {
                    if (settings.SelectedFriendSteamId == friendSteamId)
                    {
                        settings.SelectedFriendProfile = profile;
                        settings.FriendProfileError = null;
                    }

                    settings.IsFriendProfileLoading = false;
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[AnikiHelper][SteamFriends] Failed to open friend profile for '{friendSteamId}'.");

                InvokeOnUi(() =>
                {
                    if (settings.SelectedFriendSteamId == friendSteamId)
                    {
                        settings.SelectedFriendProfile = null;
                        settings.FriendProfileError = "Failed to load friend profile.";
                    }

                    settings.IsFriendProfileLoading = false;
                });
            }
        }

        private void PrepareCachedProfile(FriendProfileDto profile, string friendSteamId)
        {
            if (profile?.recentGames != null)
            {
                profile.recentGames = profile.recentGames
                    .Where(game => game != null && !SteamNonGameAppFilter.IsKnownNonGameSteamApp(game.appid, game.name))
                    .ToList();

                foreach (var game in profile.recentGames)
                {
                    if (string.IsNullOrWhiteSpace(game.playtimeForeverDisplay))
                    {
                        game.playtimeForeverDisplay = FormatMinutesToHours(game.playtimeForeverMinutes);
                    }

                    if (string.IsNullOrWhiteSpace(game.playtime2WeeksDisplay))
                    {
                        game.playtime2WeeksDisplay = FormatMinutesToHours(game.playtime2WeeksMinutes);
                    }

                    if (string.IsNullOrWhiteSpace(game.headerImageUrl) || !game.headerImageUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    {
                        game.headerImageUrl = GetGameHeaderSource(game.appid);
                    }
                }
            }

            if (profile.recentPlaytime2WeeksMinutes <= 0 && profile.recentGames != null)
            {
                profile.recentPlaytime2WeeksMinutes = profile.recentGames.Sum(g => g.playtime2WeeksMinutes);
            }

            if (string.IsNullOrWhiteSpace(profile.recentPlaytime2WeeksDisplay))
            {
                profile.recentPlaytime2WeeksDisplay = FormatMinutesToHours(profile.recentPlaytime2WeeksMinutes);
            }

            profile.avatar = GetFriendAvatarSource(friendSteamId, profile.avatar);
            profile.recentAchievements = LoadRecentFriendAchievements(friendSteamId);
        }

        private async Task<List<SteamFriend>> GetExtendedFriendsAsync(string apiKey, string steamId64)
        {
            var nowUtc = DateTime.UtcNow;
            if (cachedFriendsExtended != null && cachedFriendsExtended.Count > 0 && (nowUtc - cachedFriendsExtendedLastFetchUtc) < cachedFriendsExtendedTtl)
            {
                return cachedFriendsExtended;
            }

            var friends = await steamClient.GetFriendsAsync(apiKey, steamId64).ConfigureAwait(false);
            cachedFriendsExtended = friends ?? new List<SteamFriend>();
            cachedFriendsExtendedLastFetchUtc = nowUtc;
            return cachedFriendsExtended;
        }

        private async Task<string> ResolveSteamId64Async(string apiKey, string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
            {
                return null;
            }

            var input = userInput.Trim();
            if (input.Length == 17 && input.All(char.IsDigit))
            {
                return input;
            }

            var nowUtc = DateTime.UtcNow;
            if (cachedSteamIdInput == input && !string.IsNullOrWhiteSpace(cachedResolvedSteamId64) && (nowUtc - steamIdResolveLastUtc) < steamIdResolveTtl)
            {
                return cachedResolvedSteamId64;
            }

            var profilesMarker = "/profiles/";
            var idxProfiles = input.IndexOf(profilesMarker, StringComparison.OrdinalIgnoreCase);
            if (idxProfiles >= 0)
            {
                var after = input.Substring(idxProfiles + profilesMarker.Length);
                var digits = new string(after.TakeWhile(char.IsDigit).ToArray());
                if (!string.IsNullOrWhiteSpace(digits))
                {
                    cachedSteamIdInput = input;
                    cachedResolvedSteamId64 = digits;
                    steamIdResolveLastUtc = nowUtc;
                    return digits;
                }
            }

            var idMarker = "/id/";
            var idxId = input.IndexOf(idMarker, StringComparison.OrdinalIgnoreCase);
            string vanity = null;

            if (idxId >= 0)
            {
                var after = input.Substring(idxId + idMarker.Length);
                vanity = new string(after.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            }
            else
            {
                vanity = input;
            }

            if (string.IsNullOrWhiteSpace(vanity) || string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            try
            {
                var resolved = await steamClient.ResolveVanityUrlAsync(apiKey, vanity).ConfigureAwait(false);
                cachedSteamIdInput = input;
                cachedResolvedSteamId64 = resolved;
                steamIdResolveLastUtc = nowUtc;
                return resolved;
            }
            catch
            {
                return null;
            }
        }

        private void SetSteamStatus(string status)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(status))
                {
                    return;
                }

                // Do not block the command only because process detection failed.
                // The Steam URI is the real action; if Steam is running it will change the status,
                // and if Steam is not running the URI handler may open it.
                var uri = $"steam://friends/status/{status}";
                DebugLog($"[AnikiHelper][SteamFriends] SetSteamStatus: {status} -> {uri} | steamRunning={IsSteamClientRunning()}");

                try
                {
                    Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
                }
                catch
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c start \"\" \"{uri}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }

                InvokeOnUi(() =>
                {
                    settings.IsSteamRunning = true;

                    var localState = status == "invisible" ? "offline" : status;
                    settings.SelfState = localState;
                    settings.SelfStateLoc = LocalizeStateTheme(localState);
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[AnikiHelper][SteamFriends] Failed to change Steam status.");
                InvokeOnUi(() => settings.IsSteamRunning = IsSteamClientRunning());
            }
        }

        private void OpenFriendChat(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = $"steam://friends/message/{steamId}", UseShellExecute = true });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c start \"\" \"steam://friends/message/{steamId}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }
            }
        }

        private void OpenSteam()
        {
            if (settings.IsSteamLaunching)
            {
                return;
            }

            try
            {
                settings.IsSteamLaunching = true;
                settings.SteamLaunchMessage = GetStringSafe("LOCSteamLaunching", "Launching Steam...");

                var exe = GetSteamExe();
                if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "-silent",
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(exe)
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "steam://open/main", UseShellExecute = true });
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(7000).ConfigureAwait(false);

                    var steamDetected = false;
                    for (var i = 0; i < 30; i++)
                    {
                        if (IsSteamRunningNow())
                        {
                            steamDetected = true;
                            break;
                        }

                        await Task.Delay(500).ConfigureAwait(false);
                    }

                    if (!steamDetected)
                    {
                        InvokeOnUi(() =>
                        {
                            settings.IsSteamLaunching = false;
                            settings.SteamLaunchMessage = GetStringSafe("LOCSteamLaunchFailed", "Steam did not start. Please run Steam manually.");
                        });
                        return;
                    }

                    InvokeOnUi(() =>
                    {
                        settings.IsSteamLaunching = false;
                        settings.SteamLaunchMessage = null;
                    });

                    for (var i = 0; i < 20; i++)
                    {
                        await ForceRefreshAndWaitAsync().ConfigureAwait(false);
                        var state = settings?.SelfState;
                        if (!string.IsNullOrWhiteSpace(state) && !state.Equals("notrunning", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                });
            }
            catch (Exception ex)
            {
                InvokeOnUi(() =>
                {
                    settings.IsSteamLaunching = false;
                    settings.SteamLaunchMessage = null;
                });

                logger.Warn(ex, "[AnikiHelper][SteamFriends] Open Steam failed.");
            }
        }

        private static string GetSteamExe()
        {
            string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Trim('"');

            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var steamExe = Normalize(key?.GetValue("SteamExe") as string);
                    if (!string.IsNullOrWhiteSpace(steamExe)) return steamExe;

                    var steamPath = Normalize(key?.GetValue("SteamPath") as string);
                    if (!string.IsNullOrWhiteSpace(steamPath)) return Path.Combine(steamPath, "steam.exe");
                }
            }
            catch { }

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var installPath = Normalize(key?.GetValue("InstallPath") as string);
                    if (!string.IsNullOrWhiteSpace(installPath)) return Path.Combine(installPath, "steam.exe");
                }
            }
            catch { }

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Valve\Steam"))
                {
                    var installPath = Normalize(key?.GetValue("InstallPath") as string);
                    if (!string.IsNullOrWhiteSpace(installPath)) return Path.Combine(installPath, "steam.exe");
                }
            }
            catch { }

            return null;
        }

        private Regex fafMsJsonDateRegex = new Regex(@"\/Date\((\-?\d+)(?:[+-]\d+)?\)\/", RegexOptions.Compiled);

        private DateTime? ParseFafMsJsonDateToUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            try
            {
                var match = fafMsJsonDateRegex.Match(value);
                if (!match.Success) return null;
                if (!long.TryParse(match.Groups[1].Value, out var ms)) return null;
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }

        private string FormatDateTimeForFriendAchievement(DateTime? utcDate)
        {
            if (!utcDate.HasValue) return string.Empty;
            try { return utcDate.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm"); } catch { return string.Empty; }
        }

        private List<RecentFriendAchievementDto> LoadRecentFriendAchievements(string friendSteamId, int maxItems = MaxRecentFriendAchievements)
        {
            var result = new List<RecentFriendAchievementDto>();

            try
            {
                if (string.IsNullOrWhiteSpace(friendSteamId) || !File.Exists(FriendsAchievementFeedCachePath))
                {
                    return result;
                }

                string json;
                using (var fs = new FileStream(FriendsAchievementFeedCachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(json)) return result;

                var cache = Playnite.SDK.Data.Serialization.FromJson<FriendAchievementFeedCache>(json);
                var entries = cache?.Entries ?? new List<FriendAchievementFeedEntry>();

                return entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.FriendSteamId) && string.Equals(e.FriendSteamId.Trim(), friendSteamId.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(e =>
                    {
                        var unlockUtc = ParseFafMsJsonDateToUtc(e.FriendUnlockTimeUtc);
                        var icon = e.FriendAchievementIcon;
                        if (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon))
                        {
                            icon = new Uri(icon).AbsoluteUri;
                        }

                        return new RecentFriendAchievementDto
                        {
                            achievementApiName = e.AchievementApiName,
                            achievementDisplayName = e.AchievementDisplayName,
                            achievementDescription = e.AchievementDescription,
                            appid = e.AppId,
                            playniteGameId = e.PlayniteGameId,
                            gameName = e.GameName,
                            icon = icon,
                            unlockTimeUtc = unlockUtc,
                            unlockTimeDisplay = FormatDateTimeForFriendAchievement(unlockUtc),
                            rarity = null
                        };
                    })
                    .OrderByDescending(x => x.unlockTimeUtc ?? DateTime.MinValue)
                    .Take(Math.Max(1, maxItems))
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[AnikiHelper][SteamFriends] Failed to load recent friend achievements for '{friendSteamId}'.");
                return new List<RecentFriendAchievementDto>();
            }
        }

        private string BuildUiSignature(int online, int ingame, int offline, List<FriendPresenceDto> onlineTop, List<FriendPresenceDto> offlineList)
        {
            var sb = new StringBuilder(512);
            sb.Append("o=").Append(online).Append("|g=").Append(ingame).Append("|f=").Append(offline).Append("|");

            if (onlineTop != null)
            {
                foreach (var f in onlineTop)
                {
                    sb.Append(f.steamid ?? "").Append("|");
                    sb.Append(f.state ?? "").Append("|");
                    sb.Append(f.stateLoc ?? "").Append("|");
                    sb.Append(f.game ?? "").Append("|");
                    sb.Append(f.avatar ?? "").Append("|");
                }
            }

            sb.Append("#");

            if (offlineList != null)
            {
                foreach (var f in offlineList)
                {
                    sb.Append(f.steamid ?? "").Append("|");
                    sb.Append(f.avatar ?? "").Append("|");
                    sb.Append(f.stateLoc ?? "").Append("|");
                }
            }

            return sb.ToString();
        }

        private string GetSteamHeaderImageUrl(int appId)
        {
            return appId <= 0 ? null : $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
        }

        private string GetGameHeaderFilePath(int appId)
        {
            return appId <= 0 ? null : Path.Combine(gameHeaderCacheDir, appId + ".jpg");
        }

        private string GetGameHeaderSource(int appId)
        {
            if (appId <= 0) return null;
            return gameImageResolver != null
                ? gameImageResolver.GetGameImageSource(appId, localUri => UpdateSelectedFriendProfileGameImage(appId, localUri))
                : GetSteamHeaderImageUrl(appId);
        }

        private void UpdateSelectedFriendProfileGameImage(int appId, string localFileUri)
        {
            if (appId <= 0 || string.IsNullOrWhiteSpace(localFileUri) || settings == null)
            {
                return;
            }

            InvokeOnUi(() =>
            {
                try
                {
                    var current = settings.SelectedFriendProfile;
                    if (current?.recentGames == null || current.recentGames.Count == 0)
                    {
                        return;
                    }

                    if (!current.recentGames.Any(g => g != null && g.appid == appId && !string.Equals(g.headerImageUrl, localFileUri, StringComparison.OrdinalIgnoreCase)))
                    {
                        return;
                    }

                    var updated = CloneFriendProfile(current);
                    foreach (var game in updated.recentGames.Where(g => g != null && g.appid == appId))
                    {
                        game.headerImageUrl = localFileUri;
                    }

                    settings.SelectedFriendProfile = updated;

                    if (!string.IsNullOrWhiteSpace(updated.steamid))
                    {
                        try
                        {
                            friendProfileCache.Save(new CachedFriendProfile
                            {
                                steamid = updated.steamid,
                                cachedAtUtc = DateTime.UtcNow,
                                profile = updated
                            });
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            });
        }

        private FriendProfileDto CloneFriendProfile(FriendProfileDto source)
        {
            if (source == null)
            {
                return null;
            }

            return new FriendProfileDto
            {
                steamid = source.steamid,
                name = source.name,
                avatar = source.avatar,
                state = source.state,
                stateLoc = source.stateLoc,
                game = source.game,
                isProfilePublic = source.isProfilePublic,
                lastLogoffUtc = source.lastLogoffUtc,
                friendSinceUtc = source.friendSinceUtc,
                steamLevel = source.steamLevel,
                badgesCount = source.badgesCount,
                recentPlaytime2WeeksMinutes = source.recentPlaytime2WeeksMinutes,
                recentPlaytime2WeeksDisplay = source.recentPlaytime2WeeksDisplay,
                recentGames = (source.recentGames ?? new List<RecentGameDto>()).Select(g => g == null ? null : new RecentGameDto
                {
                    appid = g.appid,
                    name = g.name,
                    playtime2WeeksMinutes = g.playtime2WeeksMinutes,
                    playtimeForeverMinutes = g.playtimeForeverMinutes,
                    playtime2WeeksDisplay = g.playtime2WeeksDisplay,
                    playtimeForeverDisplay = g.playtimeForeverDisplay,
                    headerImageUrl = g.headerImageUrl
                }).Where(g => g != null).ToList(),
                recentAchievements = (source.recentAchievements ?? new List<RecentFriendAchievementDto>()).Select(a => a == null ? null : new RecentFriendAchievementDto
                {
                    achievementApiName = a.achievementApiName,
                    achievementDisplayName = a.achievementDisplayName,
                    achievementDescription = a.achievementDescription,
                    appid = a.appid,
                    playniteGameId = a.playniteGameId,
                    gameName = a.gameName,
                    icon = a.icon,
                    unlockTimeUtc = a.unlockTimeUtc,
                    unlockTimeDisplay = a.unlockTimeDisplay,
                    rarity = a.rarity
                }).Where(a => a != null).ToList()
            };
        }

        private async Task CacheGameHeaderAsync(int appId, string imageUrl)
        {
            if (appId <= 0 || string.IsNullOrWhiteSpace(imageUrl)) return;
            var path = GetGameHeaderFilePath(appId);
            if (string.IsNullOrWhiteSpace(path) || IsUsableImageFile(path)) return;
            if (!gameHeaderDlInProgress.TryAdd(appId, 0)) return;

            try
            {
                await gameHeaderDlSem.WaitAsync().ConfigureAwait(false);
                if (File.Exists(path)) return;
                var data = await http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch { }
            finally
            {
                try { gameHeaderDlSem.Release(); } catch { }
                gameHeaderDlInProgress.TryRemove(appId, out _);
            }
        }

        private string GetFriendAvatarSource(string steamId, string remoteAvatar = null)
        {
            var localPath = GetAvatarFilePath(steamId);
            if (IsUsableImageFile(localPath))
            {
                return ToFileUri(localPath);
            }

            if (!string.IsNullOrWhiteSpace(remoteAvatar))
            {
                return remoteAvatar;
            }

            try
            {
                var runtimeFriend = settings?.Friends?.FirstOrDefault(f =>
                    f != null &&
                    !string.IsNullOrWhiteSpace(f.steamid) &&
                    string.Equals(f.steamid.Trim(), steamId?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(runtimeFriend?.avatar))
                {
                    return runtimeFriend.avatar;
                }
            }
            catch
            {
            }

            return null;
        }

        private bool IsUsableImageFile(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) &&
                       File.Exists(path) &&
                       new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private string GetAvatarFilePath(string steamId)
        {
            return string.IsNullOrWhiteSpace(steamId) ? null : Path.Combine(avatarCacheDir, steamId + ".jpg");
        }

        private int ParseSteamAppId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            try
            {
                if (long.TryParse(value.Trim(), out var raw) && raw > 0 && raw <= int.MaxValue)
                {
                    return (int)raw;
                }
            }
            catch
            {
            }

            return 0;
        }

        private string ToFileUri(string path)
        {
            try { return new Uri(path).AbsoluteUri; } catch { return path; }
        }

        private async Task CacheAvatarAsync(string steamId, string avatarUrl)
        {
            if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(avatarUrl)) return;
            var path = GetAvatarFilePath(steamId);
            if (string.IsNullOrWhiteSpace(path) || IsUsableImageFile(path)) return;
            if (!avatarDlInProgress.TryAdd(steamId, 0)) return;

            try
            {
                await avatarDlSem.WaitAsync().ConfigureAwait(false);
                if (IsUsableImageFile(path)) return;
                var data = await http.GetByteArrayAsync(avatarUrl).ConfigureAwait(false);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { var tmp = path + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            finally
            {
                avatarDlInProgress.TryRemove(steamId, out _);
                try { avatarDlSem.Release(); } catch { }
            }
        }

        private void MaybeCleanupAvatarCache(DateTime nowUtc)
        {
            if (nowUtc - lastAvatarCleanupUtc < avatarCleanupInterval) return;
            lastAvatarCleanupUtc = nowUtc;

            Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(avatarCacheDir)) return;
                    foreach (var f in Directory.GetFiles(avatarCacheDir, "*.jpg"))
                    {
                        DateTime lastWriteUtc;
                        try { lastWriteUtc = File.GetLastWriteTimeUtc(f); } catch { continue; }
                        if ((nowUtc - lastWriteUtc) > avatarMaxAge)
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[AnikiHelper][SteamFriends] Avatar cache cleanup failed.");
                }
            });
        }

        private string MapState(SteamPlayerSummary p)
        {
            if (p == null) return "offline";
            if (!string.IsNullOrWhiteSpace(p.GameExtraInfo)) return "ingame";

            switch (p.PersonaState)
            {
                case 0: return "offline";
                case 1: return "online";
                case 2: return "busy";
                case 3: return "away";
                case 4: return "snooze";
                case 5: return "online";
                case 6: return "online";
                default: return "offline";
            }
        }

        private int Rank(string state)
        {
            switch (state)
            {
                case "ingame": return 0;
                case "online": return 1;
                case "away": return 2;
                case "busy": return 3;
                case "snooze": return 4;
                default: return 9;
            }
        }

        private string GetStringSafe(string key, string fallback)
        {
            try
            {
                var s = playniteApi?.Resources?.GetString(key);
                if (string.IsNullOrWhiteSpace(s) || s == key || (s.StartsWith("<!", StringComparison.Ordinal) && s.EndsWith("!>", StringComparison.Ordinal)))
                {
                    return fallback;
                }

                return s;
            }
            catch
            {
                return fallback;
            }
        }

        private string LocalizeStateTheme(string state)
        {
            switch (state)
            {
                case "online": return GetStringSafe("LOCSteamOnline", "Online");
                case "ingame": return GetStringSafe("LOCSteamInGame", "In game");
                case "away": return GetStringSafe("LOCSteamAway", "Away");
                case "busy": return GetStringSafe("LOCSteamBusy", "Busy");
                case "snooze": return GetStringSafe("LOCSteamSnooze", "Snooze");
                case "offline": return GetStringSafe("LOCSteamOffline", "Offline");
                default: return GetStringSafe("LOCSteamOffline", "Offline");
            }
        }

        private string FormatMinutesToHours(int minutes)
        {
            if (minutes <= 0) return "0 h";
            var hours = minutes / 60;
            var remainingMinutes = minutes % 60;
            if (hours <= 0) return $"{remainingMinutes} min";
            if (remainingMinutes <= 0) return $"{hours} h";
            return $"{hours} h {remainingMinutes:00}";
        }

        private DateTime? UnixToUtc(long unixSeconds)
        {
            if (unixSeconds <= 0) return null;
            try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime; } catch { return null; }
        }

        private void InvokeOnUi(Action action)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess()) action();
                else disp.BeginInvoke(action);
            }
            catch
            {
                try { action(); } catch { }
            }
        }
    }
}
