using Playnite.SDK;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AnikiHelper
{
    /// <summary>
    /// Temporarily lowers only Playnite Fullscreen's native navigation / activation
    /// sound handles. Aniki's MediaElement transition sounds are not affected.
    ///
    /// This intentionally uses reflection so Aniki Helper keeps depending only on the
    /// public Playnite SDK and does not need a reference to Playnite.FullscreenApp.
    /// </summary>
    internal static class NativeUiSoundSuppressor
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly object syncRoot = new object();

        private static DateTime navigationMutedUntilUtc = DateTime.MinValue;
        private static DateTime activationMutedUntilUtc = DateTime.MinValue;
        private static int restoreGeneration;
        private static bool navigationIsMuted;
        private static bool activationIsMuted;

        public static void Suppress(
            IPlayniteAPI playniteApi,
            bool navigation = true,
            bool activation = true,
            int durationMs = 320,
            string reason = null)
        {
            try
            {
                if ((!navigation && !activation) ||
                    durationMs <= 0 ||
                    playniteApi?.ApplicationInfo?.Mode != ApplicationMode.Fullscreen ||
                    !IsAnikiThemeActive())
                {
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                Action suppressAction = () => SuppressOnUiThread(
                    playniteApi,
                    navigation,
                    activation,
                    durationMs,
                    reason);

                if (dispatcher.CheckAccess())
                {
                    suppressAction();
                }
                else
                {
                    dispatcher.BeginInvoke(suppressAction, DispatcherPriority.Send);
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][NativeUiSound] Failed to request native UI sound suppression.");
            }
        }

        private static void SuppressOnUiThread(
            IPlayniteAPI playniteApi,
            bool navigation,
            bool activation,
            int durationMs,
            string reason)
        {
            try
            {
                var now = DateTime.UtcNow;
                var until = now.AddMilliseconds(Math.Max(60, durationMs));
                int generation;

                lock (syncRoot)
                {
                    if (navigation)
                    {
                        if (until > navigationMutedUntilUtc)
                        {
                            navigationMutedUntilUtc = until;
                        }

                        navigationIsMuted = true;
                    }

                    if (activation)
                    {
                        if (until > activationMutedUntilUtc)
                        {
                            activationMutedUntilUtc = until;
                        }

                        activationIsMuted = true;
                    }

                    generation = ++restoreGeneration;
                }

                SetNativeSoundVolumes(
                    navigation ? (double?)0.0 : null,
                    activation ? (double?)0.0 : null,
                    playniteApi);

                DebugLog(
                    $"[AnikiHelper][NativeUiSound] Suppress " +
                    $"Navigation={navigation}, Activation={activation}, Duration={durationMs}ms, " +
                    $"Reason={reason ?? "<none>"}");

                QueueRestore(playniteApi, generation, durationMs + 45);
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][NativeUiSound] Failed to mute Playnite native UI sounds.");
            }
        }

        private static async void QueueRestore(IPlayniteAPI playniteApi, int generation, int delayMs)
        {
            try
            {
                await Task.Delay(Math.Max(80, delayMs)).ConfigureAwait(false);

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() => RestoreExpired(playniteApi, generation)), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][NativeUiSound] Failed to queue native UI sound restore.");
            }
        }

        private static void RestoreExpired(IPlayniteAPI playniteApi, int generation)
        {
            try
            {
                bool restoreNavigation = false;
                bool restoreActivation = false;
                int nextDelayMs = 0;

                lock (syncRoot)
                {
                    // A newer call always owns the final restore. This avoids an older timer
                    // restoring a sound in the middle of a second transition.
                    if (generation != restoreGeneration)
                    {
                        return;
                    }

                    var now = DateTime.UtcNow;

                    if (navigationIsMuted)
                    {
                        if (now >= navigationMutedUntilUtc)
                        {
                            restoreNavigation = true;
                            navigationIsMuted = false;
                        }
                        else
                        {
                            nextDelayMs = Math.Max(
                                nextDelayMs,
                                (int)Math.Ceiling((navigationMutedUntilUtc - now).TotalMilliseconds) + 35);
                        }
                    }

                    if (activationIsMuted)
                    {
                        if (now >= activationMutedUntilUtc)
                        {
                            restoreActivation = true;
                            activationIsMuted = false;
                        }
                        else
                        {
                            nextDelayMs = Math.Max(
                                nextDelayMs,
                                (int)Math.Ceiling((activationMutedUntilUtc - now).TotalMilliseconds) + 35);
                        }
                    }
                }

                if (restoreNavigation || restoreActivation)
                {
                    var volume = GetCurrentInterfaceVolume(playniteApi);
                    SetNativeSoundVolumes(
                        restoreNavigation ? (double?)volume : null,
                        restoreActivation ? (double?)volume : null,
                        playniteApi);

                    DebugLog(
                        $"[AnikiHelper][NativeUiSound] Restored native sounds | " +
                        $"Navigation={restoreNavigation}, Activation={restoreActivation}, Volume={volume:0.###}");
                }

                if (nextDelayMs > 0)
                {
                    QueueRestore(playniteApi, generation, nextDelayMs);
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][NativeUiSound] Failed to restore Playnite native UI sounds.");
            }
        }

        private static void SetNativeSoundVolumes(double? navigationVolume, double? activationVolume, IPlayniteAPI playniteApi)
        {
            try
            {
                var fullscreenType = FindFullscreenApplicationType();
                if (fullscreenType == null)
                {
                    return;
                }

                var audioProperty = fullscreenType.GetProperty(
                    "Audio",
                    BindingFlags.Public | BindingFlags.Static);
                var audio = audioProperty?.GetValue(null, null);
                if (audio == null)
                {
                    return;
                }

                var setVolumeMethod = audio.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "SetSoundVolume", StringComparison.Ordinal) &&
                        method.GetParameters().Length == 2);

                if (setVolumeMethod == null)
                {
                    return;
                }

                if (navigationVolume.HasValue)
                {
                    var handle = GetSoundHandle(fullscreenType, "NavigateSound");
                    InvokeSetSoundVolume(audio, setVolumeMethod, handle, navigationVolume.Value);
                }

                if (activationVolume.HasValue)
                {
                    var handle = GetSoundHandle(fullscreenType, "ActivateSound");
                    InvokeSetSoundVolume(audio, setVolumeMethod, handle, activationVolume.Value);
                }
            }
            catch (Exception ex)
            {
                DebugLog(ex, "[AnikiHelper][NativeUiSound] Reflection call to Playnite audio engine failed.");
            }
        }

        private static Type FindFullscreenApplicationType()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType(
                        "Playnite.FullscreenApp.FullscreenApplication",
                        throwOnError: false,
                        ignoreCase: false);

                    if (type != null)
                    {
                        return type;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static IntPtr GetSoundHandle(Type fullscreenType, string propertyName)
        {
            try
            {
                var property = fullscreenType.GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Static);
                var value = property?.GetValue(null, null);

                if (value is IntPtr pointer)
                {
                    return pointer;
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private static void InvokeSetSoundVolume(object audio, MethodInfo method, IntPtr handle, double volume)
        {
            if (audio == null || method == null || handle == IntPtr.Zero)
            {
                return;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                return;
            }

            object convertedVolume;
            var targetType = Nullable.GetUnderlyingType(parameters[1].ParameterType) ?? parameters[1].ParameterType;

            if (targetType == typeof(float))
            {
                convertedVolume = (float)volume;
            }
            else if (targetType == typeof(double))
            {
                convertedVolume = volume;
            }
            else
            {
                convertedVolume = Convert.ChangeType(volume, targetType);
            }

            method.Invoke(audio, new[] { (object)handle, convertedVolume });
        }

        private static double GetCurrentInterfaceVolume(IPlayniteAPI playniteApi)
        {
            try
            {
                var fullscreenType = FindFullscreenApplicationType();
                var currentProperty = fullscreenType?.GetProperty(
                    "Current",
                    BindingFlags.Public | BindingFlags.Static);
                var current = currentProperty?.GetValue(null, null);
                if (current != null)
                {
                    var appSettings = current.GetType()
                        .GetProperty("AppSettings", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(current, null);
                    var fullscreen = appSettings?.GetType()
                        .GetProperty("Fullscreen", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(appSettings, null);
                    var value = fullscreen?.GetType()
                        .GetProperty("InterfaceVolume", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(fullscreen, null);

                    if (value != null)
                    {
                        var volume = Convert.ToDouble(value);
                        if (volume > 1.0)
                        {
                            volume /= 100.0;
                        }

                        return Math.Max(0.0, Math.Min(1.0, volume));
                    }
                }
            }
            catch
            {
            }

            // Safe fallback. The next normal Playnite volume change will also update the
            // handles, so a reflection failure must never leave the UI permanently silent.
            return 1.0;
        }

        private static bool IsAnikiThemeActive()
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

        private static void DebugLog(string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    global::AnikiHelper.AnikiLog.Debug(logger, message);
                }
            }
            catch
            {
            }
        }

        private static void DebugLog(Exception exception, string message)
        {
            try
            {
                if (global::AnikiHelper.AnikiHelper.Instance?.Settings?.EnableDebugLogs == true)
                {
                    global::AnikiHelper.AnikiLog.Debug(logger, exception, message);
                }
            }
            catch
            {
            }
        }
    }
}
