using Playnite.SDK;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AnikiHelper
{
    internal static class PlayniteAchievementsBridge
    {
        // GUID PlayniteAchievements
        private static readonly Guid PlayniteAchievementsId = Guid.Parse("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");

        public static async Task RefreshSelectedGameAsync(IPlayniteAPI api)
        {
            var log = LogManager.GetLogger();

            try
            {
                var game = api?.MainView?.SelectedGames?.FirstOrDefault();
                if (game == null)
                {
                    log.Warn("[AnikiHelper] No selected game.");
                    return;
                }

                var paPlugin = api.Addons?.Plugins?.FirstOrDefault(p => p.Id == PlayniteAchievementsId);
                if (paPlugin == null)
                {
                    log.Warn("[AnikiHelper] PlayniteAchievements plugin not found.");
                    return;
                }

                // Preferred: call a dedicated bridge method if present.
                var paType = paPlugin.GetType();
                var direct = paType.GetMethod(
                    "RequestSingleGameScanAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(Guid) },
                    modifiers: null);

                if (direct != null)
                {
                    if (direct.Invoke(paPlugin, new object[] { game.Id }) is Task directTask)
                    {
                        await directTask.ConfigureAwait(false);
                    }
                    return;
                }

                // Fallback: use the public AchievementService property.
                var svcProp = paType.GetProperty(
                    "AchievementService",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

                var svc = svcProp?.GetValue(paPlugin, null);
                if (svc == null)
                {
                    log.Warn("[AnikiHelper] PlayniteAchievements.AchievementService not found.");
                    return;
                }

                var start = svc.GetType().GetMethod(
                    "StartManagedSingleGameScanAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(Guid) },
                    modifiers: null);

                if (start == null)
                {
                    log.Warn("[AnikiHelper] PlayniteAchievements scan method not found.");
                    return;
                }

                if (start.Invoke(svc, new object[] { game.Id }) is Task task)
                {
                    await task.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "[AnikiHelper] Error while calling PlayniteAchievements refresh via reflection.");
            }
        }
    }
}
