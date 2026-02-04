using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AnikiHelper
{
    internal static class SuccessStoryBridge
    {
        // GUID SuccessStory
        private static readonly Guid SsId = Guid.Parse("cebe6d32-8c46-4459-b993-5a5189d60788");

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

                // Get the SuccessStory plugin instance
                var ssPlugin = api.Addons?.Plugins?.FirstOrDefault(p => p.Id == SsId);
                if (ssPlugin == null)
                {
                    log.Warn("[AnikiHelper] SuccessStory plugin not found.");
                    return;
                }

                // Retrieve the database
                object pluginDb = null;
                var ssType = ssPlugin.GetType();

                // Search for "PluginDatabase"
                if (pluginDb == null)
                {
                    var pi = ssType.GetProperty(
                        "PluginDatabase",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
                    );
                    if (pi != null)
                    {
                        pluginDb = pi.GetValue(ssPlugin, null);
                    }
                }

                if (pluginDb == null)
                {
                    var fi = ssType.GetField(
                        "PluginDatabase",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
                    );
                    if (fi != null)
                    {
                        pluginDb = fi.GetValue(ssPlugin);
                    }
                }

                // Fallback: static version 
                if (pluginDb == null)
                {
                    var spi = ssType.GetProperty(
                        "PluginDatabase",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
                    );
                    if (spi != null)
                    {
                        pluginDb = spi.GetValue(null, null);
                    }
                }

                if (pluginDb == null)
                {
                    var sfi = ssType.GetField(
                        "PluginDatabase",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
                    );
                    if (sfi != null)
                    {
                        pluginDb = sfi.GetValue(null);
                    }
                }

                // Scanning all props/fields for "SuccessStoryDatabase"
                if (pluginDb == null)
                {
                    var props = ssType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    foreach (var p in props)
                    {
                        try
                        {
                            var val = p.GetValue(ssPlugin, null);
                            if (val != null && val.GetType().Name.IndexOf("SuccessStoryDatabase", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                pluginDb = val;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (pluginDb == null)
                {
                    var fields = ssType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    foreach (var f in fields)
                    {
                        try
                        {
                            var val = f.GetValue(ssPlugin);
                            if (val != null && val.GetType().Name.IndexOf("SuccessStoryDatabase", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                pluginDb = val;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                // Ultime fallback
                if (pluginDb == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!asm.FullName.StartsWith("SuccessStory", StringComparison.OrdinalIgnoreCase)) continue;

                        foreach (var t in asm.GetTypes())
                        {
                            if (t.Name.IndexOf("SuccessStoryDatabase", StringComparison.OrdinalIgnoreCase) < 0) continue;

                            var sp = t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            foreach (var p in sp)
                            {
                                try
                                {
                                    var val = p.GetValue(null, null);
                                    if (val != null) { pluginDb = val; break; }
                                }
                                catch { }
                            }
                            if (pluginDb != null) break;

                            var sf = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            foreach (var f in sf)
                            {
                                try
                                {
                                    var val = f.GetValue(null);
                                    if (val != null) { pluginDb = val; break; }
                                }
                                catch { }
                            }
                            if (pluginDb != null) break;
                        }
                        if (pluginDb != null) break;
                    }
                }


                if (pluginDb == null)
                    return;


                // Invoke Refresh
                var dbType = pluginDb.GetType();

                var mRefreshGuid = dbType.GetMethod("Refresh", new[] { typeof(Guid) });
                var mRefreshList = dbType.GetMethod("Refresh", new[] { typeof(System.Collections.Generic.List<Guid>) });
                var mRefreshData = dbType.GetMethod("RefreshData", new[] { typeof(Playnite.SDK.Models.Game) });

                try
                {
                    {
                        if (mRefreshGuid != null)
                            mRefreshGuid.Invoke(pluginDb, new object[] { game.Id });
                        else if (mRefreshList != null)
                            mRefreshList.Invoke(pluginDb, new object[] { new System.Collections.Generic.List<Guid> { game.Id } });
                        else if (mRefreshData != null)
                            mRefreshData.Invoke(pluginDb, new object[] { game });
                        else
                            return;
                    }
                }
                catch (TargetInvocationException tie)
                {
                    log.Error(tie.InnerException ?? tie, "[AnikiHelper] SuccessStory.Refresh threw.");
                    return;
                }


                await Task.Delay(1500); 
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "[AnikiHelper] Error while calling SuccessStory refresh via reflection.");
            }
        }


    }
}
