using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnikiHelper.Services.AnikiThemeSettings
{
    public class AnikiThemeSettingsService
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;

        private readonly List<ResourceDictionary> loadedDictionaries = new List<ResourceDictionary>();

        private readonly Dictionary<string, ResourceDictionary> resourceCache =
            new Dictionary<string, ResourceDictionary>(StringComparer.OrdinalIgnoreCase);

        private const int ThemeSettingsSchemaVersion = 1;

        private bool pendingRestartPrompt;
        private string currentThemePath;
        private AnikiThemeSettingsFile currentFile;
        private Action restartRequiredAction;

        private readonly string pluginUserDataPath;
        private readonly string themeSettingsFilePath;

        public AnikiThemeSettingsService(
            IPlayniteAPI playniteApi,
            AnikiHelperSettings settings,
            ILogger logger,
            string pluginUserDataPath)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.logger = logger;
            this.pluginUserDataPath = pluginUserDataPath;
            themeSettingsFilePath = Path.Combine(pluginUserDataPath, "ThemeSettings.json");
        }

        public void SetRestartRequiredAction(Action action)
        {
            restartRequiredAction = action;
        }

        private void MarkRestartRequired()
        {
            pendingRestartPrompt = true;

            try
            {
                restartRequiredAction?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to mark Playnite settings as requiring restart.");
            }
        }

        public void LoadAndApply()
        {
            try
            {
                currentThemePath = GetCurrentThemePath();

                if (string.IsNullOrWhiteSpace(currentThemePath))
                {
                    return;
                }

                var optionsPath = Path.Combine(currentThemePath, "AnikiThemeSettings.yaml");

                if (!File.Exists(optionsPath))
                {
                    logger?.Warn($"[AnikiHelper] AnikiThemeSettings.yaml not found: {optionsPath}");
                    return;
                }

                currentFile = Serialization.FromYamlFile<AnikiThemeSettingsFile>(optionsPath);

                if (currentFile == null)
                {
                    logger?.Warn($"[AnikiHelper] Failed to read AnikiThemeSettings.yaml: {optionsPath}");
                    return;
                }

                PostLoadPresets();
                PostLoadVariables();

                LoadThemeSettingsStorage();

                if (SanitizeThemeSettingsStorage())
                {
                    SaveThemeSettingsFile();
                }

                BuildCategories();

                Apply();

                StartPresetFilesPreload();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Aniki Theme Settings.");
            }
        }

        public void Apply()
        {
            try
            {
                if (currentFile == null)
                {
                    return;
                }

                RemoveLoadedDictionaries();

                UpdateSelectedPresetFlags();

                var optionValues = BuildOptionValues();

                SyncVariableBindableValues(optionValues);

                LoadSelectedPresetFiles();

                var generatedResource = BuildGeneratedResourceDictionary(optionValues);

                if (generatedResource != null)
                {
                    Application.Current.Resources.MergedDictionaries.Add(generatedResource);
                    loadedDictionaries.Add(generatedResource);
                }

                LoadLuckyDayResourceOverride();

                settings.Options.Update(optionValues);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to apply Aniki Theme Settings.");
            }
        }

        public void SetOptionValue(string key, object value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                var finalValue = value?.ToString() ?? string.Empty;

                if (settings.AnikiThemeSettingsValues.TryGetValue(key, out var currentValue) &&
                    string.Equals(currentValue, finalValue, StringComparison.Ordinal))
                {
                    return;
                }

                if (DoesVariableNeedRestart(key))
                {
                    MarkRestartRequired();
                }

                settings.AnikiThemeSettingsValues[key] = finalValue;

                ApplyExclusiveMainViewInfoOptions(key, finalValue);

                SaveSettings();

                Apply();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to set Aniki theme option: {key}");
            }
        }

        private void ApplyExclusiveMainViewInfoOptions(string changedKey, string finalValue)
        {
            if (!bool.TryParse(finalValue, out var enabled) || !enabled)
            {
                return;
            }

            if (string.Equals(changedKey, "ControllerShortcutBar", StringComparison.OrdinalIgnoreCase))
            {
                settings.AnikiThemeSettingsValues["CompactGameInfoBar"] = false.ToString();
            }
            else if (string.Equals(changedKey, "CompactGameInfoBar", StringComparison.OrdinalIgnoreCase))
            {
                settings.AnikiThemeSettingsValues["ControllerShortcutBar"] = false.ToString();
                settings.AnikiThemeSettingsValues["DetailedSideInfoPanel"] = false.ToString();
            }
            else if (string.Equals(changedKey, "DetailedSideInfoPanel", StringComparison.OrdinalIgnoreCase))
            {
                settings.AnikiThemeSettingsValues["CompactGameInfoBar"] = false.ToString();
            }
        }

        public void ToggleOptionValue(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                var currentValue = false;

                if (settings.Options.TryGetValue(key, out var value) && value is bool boolValue)
                {
                    currentValue = boolValue;
                }
                else if (settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue))
                {
                    bool.TryParse(storedValue, out currentValue);
                }

                settings.AnikiThemeSettingsValues[key] = (!currentValue).ToString();
                SaveSettings();

                Apply();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to toggle Aniki theme option: {key}");
            }
        }

        public void SelectPreset(string groupId, string presetKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(presetKey))
                {
                    return;
                }

                if (settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var currentPreset) &&
                    string.Equals(currentPreset, presetKey, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (DoesPresetNeedRestart(groupId, presetKey))
                {
                    MarkRestartRequired();
                }

                settings.AnikiThemeSettingsSelectedPresets[groupId] = presetKey;
                SaveSettings();

                Apply();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to select Aniki preset: {groupId}.{presetKey}");
            }
        }

        private bool DoesVariableNeedRestart(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) || currentFile?.Variables == null)
                {
                    return false;
                }

                return currentFile.Variables.TryGetValue(key, out var variable) &&
                       variable != null &&
                       variable.NeedRestart;
            }
            catch
            {
                return false;
            }
        }

        private bool DoesPresetNeedRestart(string groupId, string presetKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupId) ||
                    string.IsNullOrWhiteSpace(presetKey) ||
                    currentFile?.Presets == null)
                {
                    return false;
                }

                if (!currentFile.Presets.TryGetValue(groupId, out var group) ||
                    group == null)
                {
                    return false;
                }

                if (group.NeedRestart)
                {
                    return true;
                }

                if (group.Items == null)
                {
                    return false;
                }

                var preset = group.Items.FirstOrDefault(x =>
                    string.Equals(x.Key, presetKey, StringComparison.OrdinalIgnoreCase));

                return preset != null && preset.NeedRestart;
            }
            catch
            {
                return false;
            }
        }

        public void ShowRestartPromptIfNeeded(object settingsWindowContext = null)
        {
            try
            {
                if (!pendingRestartPrompt)
                {
                    return;
                }

                pendingRestartPrompt = false;

                dynamic ctx = settingsWindowContext;

                if (ctx == null)
                {
                    ctx = Application.Current.MainWindow?.DataContext;
                }

                if (ctx == null)
                {
                    logger?.Warn("[AnikiHelper] Could not trigger Playnite restart prompt: settings context is null.");
                    return;
                }

                ctx.AppSettings.Fullscreen.OnPropertyChanged("Theme");
                ctx.AppSettings.OnPropertyChanged("Theme");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to trigger Playnite restart prompt for Aniki Theme Settings.");
            }
        }

        public void ShowPreview(string presetId)
        {
            try
            {
                var preset = FindPreset(presetId);
                settings.AnikiThemeSettingsPreviewImage = preset?.Preview;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to show Aniki preset preview: {presetId}");
            }
        }

        public void HidePreview()
        {
            try
            {
                settings.AnikiThemeSettingsPreviewImage = null;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to hide Aniki preset preview.");
            }
        }

        public void Reload()
        {
            resourceCache.Clear();
            LoadAndApply();
        }

        public void SetOptionFromParameter(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            var values = ParseCommandParameter(parameter);

            if (values.TryGetValue("Key", out var key))
            {
                values.TryGetValue("Value", out var value);
                SetOptionValue(key, value);
                return;
            }

            // Simple fallback:
            // SomeKey=False
            var split = parameter.Split(new[] { '=' }, 2);

            if (split.Length == 2)
            {
                SetOptionValue(split[0].Trim(), split[1].Trim());
            }
        }

        public void ToggleOptionFromParameter(string parameter)
        {
            var values = ParseCommandParameter(parameter);

            if (!values.TryGetValue("Key", out var key))
            {
                key = parameter;
            }

            ToggleOptionValue(key);
        }

        public void SelectPresetFromParameter(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            var values = ParseCommandParameter(parameter);

            if (values.TryGetValue("Group", out var group) &&
                values.TryGetValue("Preset", out var preset))
            {
                SelectPreset(group, preset);
                return;
            }

            // Also support simple format:
            // Avatar.Avatar12
            var text = parameter.Trim();

            var dotIndex = text.IndexOf('.');
            if (dotIndex > 0 && dotIndex < text.Length - 1)
            {
                var groupId = text.Substring(0, dotIndex).Trim();
                var presetKey = text.Substring(dotIndex + 1).Trim();

                SelectPreset(groupId, presetKey);
            }
        }

        private void SaveSettings()
        {
            try
            {
                SaveThemeSettingsFile();

                // Keep normal plugin settings saved too, but theme settings themselves are now DontSerialize.
                settings.EndEdit();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save Aniki Theme Settings values.");
            }
        }

        private void LoadThemeSettingsStorage()
        {
            try
            {
                EnsureThemeSettingsDictionaries();

                if (File.Exists(themeSettingsFilePath))
                {
                    try
                    {
                        var storage = Serialization.FromJsonFile<AnikiThemeSettingsStorageFile>(themeSettingsFilePath);

                        settings.AnikiThemeSettingsValues = CopyDictionary(storage?.Values);
                        settings.AnikiThemeSettingsSelectedPresets = CopyDictionary(storage?.SelectedPresets);

                        if (settings?.EnableDebugLogs == true)
                        {
                            logger?.Info($"[AnikiHelper] Loaded ThemeSettings.json: {themeSettingsFilePath}");
                        }
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper] Failed to load ThemeSettings.json. A backup will be created and defaults will be rebuilt.");

                        try
                        {
                            var backupPath = Path.Combine(
                                pluginUserDataPath,
                                $"ThemeSettings.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.json");

                            File.Copy(themeSettingsFilePath, backupPath, true);
                            logger?.Warn($"[AnikiHelper] Corrupted ThemeSettings.json backup created: {backupPath}");
                        }
                        catch
                        {
                        }
                    }
                }

                // First version using the separated file:
                // migrate once from old config.json if possible.
                MigrateThemeSettingsFromLegacyConfig();

                if (settings?.EnableDebugLogs == true)
                {
                    logger?.Info("[AnikiHelper] ThemeSettings.json does not exist yet. It will be created from current YAML defaults.");
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to initialize ThemeSettings.json storage.");

                settings.AnikiThemeSettingsValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                settings.AnikiThemeSettingsSelectedPresets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void MigrateThemeSettingsFromLegacyConfig()
        {
            try
            {
                var legacyConfigPath = Path.Combine(pluginUserDataPath, "config.json");

                if (!File.Exists(legacyConfigPath))
                {
                    return;
                }

                var legacy = Serialization.FromJsonFile<AnikiThemeSettingsLegacyConfigFile>(legacyConfigPath);

                var legacyValues = CopyDictionary(legacy?.AnikiThemeSettingsValues);
                var legacyPresets = CopyDictionary(legacy?.AnikiThemeSettingsSelectedPresets);

                if (legacyValues.Count == 0 && legacyPresets.Count == 0)
                {
                    return;
                }

                settings.AnikiThemeSettingsValues = legacyValues;
                settings.AnikiThemeSettingsSelectedPresets = legacyPresets;

                logger?.Info("[AnikiHelper] Migrated Aniki Theme Settings values from old config.json to ThemeSettings.json.");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to migrate Aniki Theme Settings from old config.json.");
            }
        }

        private bool SanitizeThemeSettingsStorage()
        {
            var changed = false;

            try
            {
                EnsureThemeSettingsDictionaries();

                var validVariableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (currentFile?.Variables != null)
                {
                    foreach (var pair in currentFile.Variables)
                    {
                        var key = pair.Key;
                        var variable = pair.Value;

                        if (string.IsNullOrWhiteSpace(key) || variable == null)
                        {
                            continue;
                        }

                        if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        validVariableIds.Add(key);
                    }
                }

                foreach (var oldKey in settings.AnikiThemeSettingsValues.Keys.ToList())
                {
                    if (!validVariableIds.Contains(oldKey))
                    {
                        settings.AnikiThemeSettingsValues.Remove(oldKey);
                        changed = true;

                        logger?.Info($"[AnikiHelper] Removed obsolete Aniki theme option: {oldKey}");
                    }
                }

                if (currentFile?.Variables != null)
                {
                    foreach (var pair in currentFile.Variables)
                    {
                        var key = pair.Key;
                        var variable = pair.Value;

                        if (string.IsNullOrWhiteSpace(key) || variable == null)
                        {
                            continue;
                        }

                        if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var defaultValue = GetDefaultValueString(variable);

                        if (!settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue))
                        {
                            settings.AnikiThemeSettingsValues[key] = defaultValue;
                            changed = true;

                            logger?.Info($"[AnikiHelper] Added missing Aniki theme option with default: {key} = {defaultValue}");
                            continue;
                        }

                        if (!IsStoredValueValidForVariable(variable, storedValue))
                        {
                            settings.AnikiThemeSettingsValues[key] = defaultValue;
                            changed = true;

                            logger?.Warn($"[AnikiHelper] Reset invalid Aniki theme option value: {key} = {storedValue} -> {defaultValue}");
                        }
                    }
                }

                var validPresetGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (currentFile?.Presets != null)
                {
                    foreach (var pair in currentFile.Presets)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                        {
                            validPresetGroupIds.Add(pair.Key);
                        }
                    }
                }

                foreach (var oldGroupId in settings.AnikiThemeSettingsSelectedPresets.Keys.ToList())
                {
                    if (!validPresetGroupIds.Contains(oldGroupId))
                    {
                        settings.AnikiThemeSettingsSelectedPresets.Remove(oldGroupId);
                        changed = true;

                        logger?.Info($"[AnikiHelper] Removed obsolete Aniki preset group selection: {oldGroupId}");
                    }
                }

                if (currentFile?.Presets != null)
                {
                    foreach (var groupPair in currentFile.Presets)
                    {
                        var groupId = groupPair.Key;
                        var group = groupPair.Value;

                        if (string.IsNullOrWhiteSpace(groupId) || group?.Items == null || group.Items.Count == 0)
                        {
                            continue;
                        }

                        var defaultPresetKey = GetDefaultPresetKey(group);

                        if (string.IsNullOrWhiteSpace(defaultPresetKey))
                        {
                            continue;
                        }

                        if (!settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out var selectedKey))
                        {
                            settings.AnikiThemeSettingsSelectedPresets[groupId] = defaultPresetKey;
                            changed = true;

                            logger?.Info($"[AnikiHelper] Added missing Aniki preset selection with default: {groupId} = {defaultPresetKey}");
                            continue;
                        }

                        var presetStillExists = group.Items.Any(item =>
                            string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase));

                        if (!presetStillExists)
                        {
                            settings.AnikiThemeSettingsSelectedPresets[groupId] = defaultPresetKey;
                            changed = true;

                            logger?.Warn($"[AnikiHelper] Reset invalid Aniki preset selection: {groupId} = {selectedKey} -> {defaultPresetKey}");
                        }
                    }
                }

                return changed;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to sanitize Aniki Theme Settings storage.");
                return changed;
            }
        }

        private void SaveThemeSettingsFile()
        {
            try
            {
                EnsureThemeSettingsDictionaries();

                Directory.CreateDirectory(pluginUserDataPath);

                var storage = new AnikiThemeSettingsStorageFile
                {
                    SchemaVersion = ThemeSettingsSchemaVersion,
                    Values = CopyDictionary(settings.AnikiThemeSettingsValues),
                    SelectedPresets = CopyDictionary(settings.AnikiThemeSettingsSelectedPresets)
                };

                var json = Serialization.ToJson(storage, true);
                File.WriteAllText(themeSettingsFilePath, json);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to save ThemeSettings.json.");
            }
        }

        private void EnsureThemeSettingsDictionaries()
        {
            if (settings.AnikiThemeSettingsValues == null)
            {
                settings.AnikiThemeSettingsValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (settings.AnikiThemeSettingsSelectedPresets == null)
            {
                settings.AnikiThemeSettingsSelectedPresets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private Dictionary<string, string> CopyDictionary(Dictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (source == null)
            {
                return result;
            }

            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                result[pair.Key] = pair.Value ?? string.Empty;
            }

            return result;
        }

        private string GetDefaultValueString(AnikiThemeValue value)
        {
            var effective = value?.EffectiveValue;

            if (!string.IsNullOrWhiteSpace(effective))
            {
                return effective;
            }

            switch ((value?.Type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "boolean":
                case "bool":
                    return "False";

                case "int32":
                case "int":
                case "double":
                case "float":
                case "cornerradius":
                    return "0";

                case "visibility":
                    return "Collapsed";

                case "thickness":
                    return "0";

                case "color":
                case "solidcolorbrush":
                    return "#FFFFFFFF";

                case "timespan":
                    return "00:00:00";

                case "string":
                default:
                    return string.Empty;
            }
        }

        private bool IsStoredValueValidForVariable(AnikiThemeVariable variable, string storedValue)
        {
            if (variable == null)
            {
                return false;
            }

            var type = (variable.Type ?? string.Empty).Trim().ToLowerInvariant();

            if (type == "string")
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                return true;
            }

            try
            {
                switch (type)
                {
                    case "boolean":
                    case "bool":
                        return bool.TryParse(storedValue, out _);

                    case "int32":
                    case "int":
                        return int.TryParse(storedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

                    case "double":
                    case "float":
                    case "cornerradius":
                        return double.TryParse(storedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

                    case "visibility":
                        return string.Equals(storedValue, "Visible", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(storedValue, "Collapsed", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(storedValue, "Hidden", StringComparison.OrdinalIgnoreCase);

                    case "thickness":
                        return IsValidNumberList(storedValue, 1, 4);

                    case "color":
                    case "solidcolorbrush":
                        ColorConverter.ConvertFromString(storedValue);
                        return true;

                    case "timespan":
                        return TimeSpan.TryParse(storedValue, out _);

                    default:
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidNumberList(string value, int minParts, int maxParts)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(',');

            if (parts.Length < minParts || parts.Length > maxParts)
            {
                return false;
            }

            foreach (var part in parts)
            {
                if (!double.TryParse(part.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private string GetDefaultPresetKey(AnikiPresetGroup group)
        {
            if (group?.Items == null || group.Items.Count == 0)
            {
                return null;
            }

            var selected = group.Items.FirstOrDefault(p =>
                p.Key != null &&
                p.Key.EndsWith("Default", StringComparison.OrdinalIgnoreCase));

            if (selected == null)
            {
                selected = group.Items.FirstOrDefault(p =>
                    string.Equals(p.Key, "Default", StringComparison.OrdinalIgnoreCase));
            }

            return (selected ?? group.Items.FirstOrDefault())?.Key;
        }

        private string ResolveLocKey(string locKey, string fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(locKey) &&
                    Application.Current.TryFindResource(locKey) is string localized &&
                    !string.IsNullOrWhiteSpace(localized))
                {
                    return localized;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private Dictionary<string, string> ParseCommandParameter(string parameter)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(parameter))
            {
                return result;
            }

            var text = parameter.Trim();

            if (text.StartsWith("[") && text.EndsWith("]"))
            {
                text = text.Substring(1, text.Length - 2);
            }

            foreach (var part in text.Split(','))
            {
                var split = part.Split(new[] { '=' }, 2);

                if (split.Length != 2)
                {
                    continue;
                }

                result[split[0].Trim()] = split[1].Trim();
            }

            return result;
        }

        private AnikiPresetItem FindPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId) || currentFile?.Presets == null)
            {
                return null;
            }

            foreach (var group in currentFile.Presets.Values)
            {
                if (group?.Items == null)
                {
                    continue;
                }

                var preset = group.Items.FirstOrDefault(p =>
                    string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));

                if (preset != null)
                {
                    return preset;
                }
            }

            return null;
        }

        private string GetCurrentThemePath()
        {
            try
            {
                var themeId = playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? playniteApi.ApplicationSettings.FullscreenTheme
                    : playniteApi.ApplicationSettings.DesktopTheme;

                if (string.IsNullOrWhiteSpace(themeId))
                {
                    return null;
                }

                var roots = new List<string>();

                if (!playniteApi.ApplicationInfo.IsPortable)
                {
                    roots.Add(playniteApi.Paths.ConfigurationPath);
                }

                roots.Add(playniteApi.Paths.ApplicationPath);

                var modeFolder = playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? "Fullscreen"
                    : "Desktop";

                foreach (var root in roots)
                {
                    var themesFolder = Path.Combine(root, "Themes", modeFolder);

                    if (!Directory.Exists(themesFolder))
                    {
                        continue;
                    }

                    foreach (var themeDir in Directory.EnumerateDirectories(themesFolder))
                    {
                        var themeFile = Path.Combine(themeDir, "theme.yaml");

                        if (!File.Exists(themeFile))
                        {
                            continue;
                        }

                        try
                        {
                            var data = Serialization.FromYamlFile<Dictionary<string, object>>(themeFile);

                            if (data != null &&
                                data.TryGetValue("Id", out var idValue) &&
                                string.Equals(idValue?.ToString(), themeId, StringComparison.OrdinalIgnoreCase))
                            {
                                return themeDir;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to detect current theme path.");
            }

            return null;
        }

        private void PostLoadPresets()
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var groupId = groupPair.Key;
                var group = groupPair.Value;

                if (group == null)
                {
                    continue;
                }

                group.Id = groupId;
                group.Items.Clear();
                group.LocalizedName = ResolveLocKey(
                    group.LocKey,
                    !string.IsNullOrWhiteSpace(group.Title) ? group.Title :
                    !string.IsNullOrWhiteSpace(group.Name) ? group.Name :
                    group.Id);

                group.LocalizedDescription = ResolveLocKey(
                    group.DescriptionLocKey,
                    group.Description);

                group.SelectionChangedAction = (changedGroupId, selectedPresetKey) =>
                {
                    SelectPreset(changedGroupId, selectedPresetKey);
                };

                if (group.Presets == null)
                {
                    continue;
                }

                foreach (var presetPair in group.Presets)
                {
                    var presetKey = presetPair.Key;
                    var preset = presetPair.Value;

                    if (preset == null)
                    {
                        continue;
                    }

                    preset.GroupId = groupId;
                    preset.Key = presetKey;
                    preset.Id = groupId + "." + presetKey;
                    preset.LocalizedName = ResolveLocKey(
                        preset.LocKey,
                        !string.IsNullOrWhiteSpace(preset.Title) ? preset.Title :
                        !string.IsNullOrWhiteSpace(preset.Name) ? preset.Name :
                        preset.Key);

                    if (string.IsNullOrWhiteSpace(preset.Category))
                    {
                        preset.Category = group.Category;
                    }

                    if (!string.IsNullOrWhiteSpace(preset.Preview))
                    {
                        var previewPath = Path.Combine(currentThemePath, preset.Preview);
                        preset.Preview = File.Exists(previewPath) ? previewPath : null;
                    }

                    group.Items.Add(preset);
                }
            }
        }

        private void PostLoadVariables()
        {
            if (currentFile?.Variables == null)
            {
                return;
            }

            foreach (var pair in currentFile.Variables)
            {
                var key = pair.Key;
                var variable = pair.Value;

                if (variable == null)
                {
                    continue;
                }

                variable.Id = key;

                variable.LocalizedName = ResolveLocKey(
                    variable.LocKey,
                    !string.IsNullOrWhiteSpace(variable.Title) ? variable.Title :
                    !string.IsNullOrWhiteSpace(variable.Name) ? variable.Name :
                    variable.Id);

                variable.LocalizedDescription = ResolveLocKey(
                    variable.DescriptionLocKey,
                    variable.Description);

                variable.ValueChangedAction = (changedKey, changedValue) =>
                {
                    SetOptionValue(changedKey, changedValue);
                };

                if (string.IsNullOrWhiteSpace(variable.Category))
                {
                    variable.Category = "General";
                }

                if (!string.IsNullOrWhiteSpace(variable.Preview))
                {
                    var previewPath = Path.Combine(currentThemePath, variable.Preview);
                    variable.Preview = File.Exists(previewPath) ? previewPath : null;
                }
            }
        }

        private void BuildCategories()
        {
            try
            {
                settings.AnikiThemeSettingsCategories.Clear();

                var categories = new Dictionary<string, AnikiThemeSettingsCategory>(StringComparer.OrdinalIgnoreCase);

                AnikiThemeSettingsCategory GetOrCreateCategory(string categoryId)
                {
                    if (string.IsNullOrWhiteSpace(categoryId))
                    {
                        categoryId = "General";
                    }

                    if (!categories.TryGetValue(categoryId, out var category))
                    {
                        var categoryTitle = GetCategoryTitle(categoryId);
                        var categoryLocKey = GetCategoryLocKey(categoryId);

                        category = new AnikiThemeSettingsCategory
                        {
                            Id = categoryId,
                            Title = ResolveLocKey(categoryLocKey, categoryTitle),
                            LocKey = categoryLocKey,
                            Icon = GetCategoryIcon(categoryId)
                        };

                        categories[categoryId] = category;
                    }

                    return category;
                }

                if (currentFile?.Presets != null)
                {
                    foreach (var groupPair in currentFile.Presets)
                    {
                        var group = groupPair.Value;

                        if (group == null)
                        {
                            continue;
                        }

                        var category = GetOrCreateCategory(group.Category);
                        category.Items.Add(group);
                    }
                }

                if (currentFile?.Variables != null)
                {
                    foreach (var variablePair in currentFile.Variables)
                    {
                        var variable = variablePair.Value;

                        if (variable == null)
                        {
                            continue;
                        }

                        var category = GetOrCreateCategory(variable.Category);

                        if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(variable.HeaderKind, "PageTitle", StringComparison.OrdinalIgnoreCase))
                        {
                            category.WindowTitle = variable.DisplayName;

                            if (variable.CategoryOrder > 0)
                            {
                                category.Order = variable.CategoryOrder;
                            }

                            continue;
                        }

                        category.Items.Add(variable);
                    }
                }

                foreach (var category in categories.Values
                    .OrderBy(x => x.Order)
                    .ThenBy(x => GetCategorySortOrder(x.Id)))
                {
                    settings.AnikiThemeSettingsCategories.Add(category);
                }

                if (!settings.AnikiThemeSettingsCategories.Any(x =>
                        string.Equals(x.Id, settings.SelectedAnikiThemeSettingsCategoryId, StringComparison.OrdinalIgnoreCase)))
                {
                    settings.SelectedAnikiThemeSettingsCategoryId =
                        settings.AnikiThemeSettingsCategories.FirstOrDefault()?.Id ?? "General";
                }

                settings.RefreshSelectedAnikiThemeSettingsCategoryItems();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to build Aniki Theme Settings categories.");
            }
        }

        private string GetCategoryTitle(string categoryId)
        {
            switch ((categoryId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "general":
                    return "General";

                case "mainview":
                case "main view":
                    return "Main View";

                case "detailsview":
                case "details view":
                case "detail view":
                case "detail view settings":
                    return "Details View";

                case "achievements":
                case "achievement":
                case "trophy":
                case "trophy view":
                case "trophy view settings":
                    return "Achievements";

                case "visualeffects":
                case "visual effects":
                    return "Visual Effects";

                case "controller":
                case "controller / prompts":
                case "prompts":
                    return "Controller";

                case "advanced":
                case "extra":
                case "extra options":
                    return "Advanced";

                default:
                    return string.IsNullOrWhiteSpace(categoryId) ? "General" : categoryId.Trim();
            }
        }

        private string GetCategoryLocKey(string categoryId)
        {
            var cleanId = (categoryId ?? "General")
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("/", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);

            return "LOCAnikiThemeSettingsCategory" + cleanId;
        }

        private string GetCategoryIcon(string categoryId)
        {
            switch ((categoryId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "general":
                    return "\uE713";

                case "mainview":
                case "main view":
                    return "\uE80F";

                case "detailsview":
                case "details view":
                case "detail view":
                case "detail view settings":
                    return "\uE946";

                case "achievements":
                case "achievement":
                case "trophy":
                case "trophy view":
                case "trophy view settings":
                    return "\uE7C1";

                case "visualeffects":
                case "visual effects":
                    return "\uE790";

                case "controller":
                case "controller / prompts":
                case "prompts":
                    return "\uE7FC";

                case "advanced":
                case "extra":
                case "extra options":
                    return "\uE9F5";

                default:
                    return "\uE713";
            }
        }

        private int GetCategorySortOrder(string categoryId)
        {
            switch ((categoryId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "general":
                    return 0;

                case "mainview":
                case "main view":
                    return 10;

                case "detailsview":
                case "details view":
                case "detail view":
                case "detail view settings":
                    return 20;

                case "achievements":
                case "achievement":
                case "trophy":
                case "trophy view":
                case "trophy view settings":
                    return 30;

                case "visualeffects":
                case "visual effects":
                    return 40;

                case "controller":
                case "controller / prompts":
                case "prompts":
                    return 50;

                case "advanced":
                case "extra":
                case "extra options":
                    return 100;

                default:
                    return 999;
            }
        }

        private void UpdateSelectedPresetFlags()
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var group = groupPair.Value;
                var selectedPreset = GetSelectedPreset(groupPair.Key, group);

                if (group == null)
                {
                    continue;
                }

                group.SetSelectedPresetKeySilently(selectedPreset?.Key);

                if (group.Items == null)
                {
                    continue;
                }

                foreach (var preset in group.Items)
                {
                    preset.IsSelected = selectedPreset != null &&
                                        string.Equals(preset.Key, selectedPreset.Key, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void SyncVariableBindableValues(Dictionary<string, object> optionValues)
        {
            if (currentFile?.Variables == null || optionValues == null)
            {
                return;
            }

            foreach (var pair in currentFile.Variables)
            {
                var key = pair.Key;
                var variable = pair.Value;

                if (variable == null || string.IsNullOrWhiteSpace(variable.Type))
                {
                    continue;
                }

                if (!optionValues.TryGetValue(key, out var value))
                {
                    continue;
                }

                var type = variable.Type.Trim().ToLowerInvariant();

                switch (type)
                {
                    case "boolean":
                    case "bool":
                        variable.SetCurrentBooleanValueSilently(ToBool(value));
                        break;

                    case "double":
                    case "float":
                    case "int32":
                    case "int":
                        variable.SetCurrentDoubleValueSilently(ToDouble(value, 0));
                        break;

                    case "cornerradius":
                        variable.SetCurrentDoubleValueSilently(ToCornerRadiusUniformValue(value));
                        break;

                    case "string":
                        variable.SetCurrentStringValueSilently(value?.ToString() ?? string.Empty);
                        break;
                }
            }
        }

        private bool ToBool(object value)
        {
            if (value is bool boolValue)
            {
                return boolValue;
            }

            return string.Equals(value?.ToString(), "True", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private double ToDouble(object value, double fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (value is double doubleValue)
            {
                return doubleValue;
            }

            if (value is float floatValue)
            {
                return floatValue;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return fallback;
        }

        private double ToCornerRadiusUniformValue(object value)
        {
            if (value == null)
            {
                return 0;
            }

            if (value is CornerRadius cornerRadius)
            {
                return cornerRadius.TopLeft;
            }

            var text = value.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var parts = text.Split(',');

            if (parts.Length > 0 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var firstValue))
            {
                return firstValue;
            }

            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var uniform)
                ? uniform
                : 0;
        }

        private Dictionary<string, object> BuildOptionValues()
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            ApplyVariableValues(result);
            ApplyPresetConstants(result);

            return result;
        }

        private void ApplyVariableValues(Dictionary<string, object> result)
        {
            if (currentFile?.Variables == null)
            {
                return;
            }

            foreach (var pair in currentFile.Variables)
            {
                var key = pair.Key;
                var variable = pair.Value;

                if (variable == null)
                {
                    continue;
                }

                if (string.Equals(variable.Type, "Header", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawValue = GetStoredValueOrDefault(key, variable);
                var value = ConvertValue(variable.Type, rawValue);

                if (value != null)
                {
                    result[key] = value;
                }
            }
        }

        private void ApplyPresetConstants(Dictionary<string, object> result)
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var groupId = groupPair.Key;
                var selectedPreset = GetSelectedPreset(groupId, groupPair.Value);

                if (selectedPreset?.Constants == null)
                {
                    continue;
                }

                foreach (var constantPair in selectedPreset.Constants)
                {
                    var key = constantPair.Key;
                    var constant = constantPair.Value;

                    if (constant == null)
                    {
                        continue;
                    }

                    var value = ConvertValue(constant.Type, constant.Value ?? constant.Default);

                    if (value != null)
                    {
                        result[key] = value;
                    }
                }
            }
        }

        private string GetStoredValueOrDefault(string key, AnikiThemeValue value)
        {
            if (settings.AnikiThemeSettingsValues != null &&
                settings.AnikiThemeSettingsValues.TryGetValue(key, out var storedValue))
            {
                return storedValue;
            }

            return value?.EffectiveValue;
        }

        private AnikiPresetItem GetSelectedPreset(string groupId, AnikiPresetGroup group)
        {
            if (group?.Items == null || group.Items.Count == 0)
            {
                return null;
            }

            string selectedKey = null;

            if (settings.AnikiThemeSettingsSelectedPresets != null)
            {
                settings.AnikiThemeSettingsSelectedPresets.TryGetValue(groupId, out selectedKey);
            }

            var selected = !string.IsNullOrWhiteSpace(selectedKey)
                ? group.Items.FirstOrDefault(p => string.Equals(p.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                : null;

            if (selected == null)
            {
                selected = group.Items.FirstOrDefault(p =>
                    p.Key != null &&
                    p.Key.EndsWith("Default", StringComparison.OrdinalIgnoreCase));
            }

            if (selected == null)
            {
                selected = group.Items.FirstOrDefault(p =>
                    string.Equals(p.Key, "Default", StringComparison.OrdinalIgnoreCase));
            }

            return selected ?? group.Items.FirstOrDefault();
        }

        private void LoadSelectedPresetFiles()
        {
            if (currentFile?.Presets == null)
            {
                return;
            }

            foreach (var groupPair in currentFile.Presets)
            {
                var selectedPreset = GetSelectedPreset(groupPair.Key, groupPair.Value);

                if (selectedPreset?.Files == null)
                {
                    continue;
                }

                foreach (var relativeFile in selectedPreset.Files)
                {
                    if (string.IsNullOrWhiteSpace(relativeFile))
                    {
                        continue;
                    }

                    var filePath = Path.Combine(currentThemePath, relativeFile);

                    if (!File.Exists(filePath))
                    {
                        logger?.Warn($"[AnikiHelper] Aniki preset resource file not found: {filePath}");
                        continue;
                    }

                    try
                    {
                        var resource = GetOrLoadResourceDictionary(filePath);

                        if (resource != null)
                        {
                            Application.Current.Resources.MergedDictionaries.Add(resource);
                            loadedDictionaries.Add(resource);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, $"[AnikiHelper] Failed to load Aniki preset resource: {filePath}");
                    }
                }
            }
        }

        private ResourceDictionary GetOrLoadResourceDictionary(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            if (resourceCache.TryGetValue(filePath, out var cached))
            {
                return cached;
            }

            var fileUri = new Uri(filePath, UriKind.Absolute);

            using (var stream = File.OpenRead(filePath))
            {
                var parserContext = new ParserContext
                {
                    BaseUri = fileUri
                };

                var resource = (ResourceDictionary)XamlReader.Load(stream, parserContext);
                resource.Source = fileUri;

                resourceCache[filePath] = resource;
                return resource;
            }
        }

        private async void StartPresetFilesPreload()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher == null || currentFile?.Presets == null)
                {
                    return;
                }

                var files = GetAllPresetResourceFiles()
                    .Where(File.Exists)
                    .Where(CanPreloadResourceFile)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (files.Count == 0)
                {
                    return;
                }

                await Task.Delay(1500);

                foreach (var file in files)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            GetOrLoadResourceDictionary(file);
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn(ex, $"[AnikiHelper] Failed to preload Aniki preset resource: {file}");
                        }
                    }, DispatcherPriority.Background);

                    await Task.Delay(50);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to preload Aniki preset resources.");
            }
        }

        private bool CanPreloadResourceFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    return false;
                }

                var text = File.ReadAllText(filePath);

                // ThemeFile may fail when loaded manually through XamlReader during preload.
                // These files should only be loaded when actually selected.
                if (text.IndexOf("ThemeFile", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<string> GetAllPresetResourceFiles()
        {
            if (currentFile?.Presets == null || string.IsNullOrWhiteSpace(currentThemePath))
            {
                yield break;
            }

            foreach (var group in currentFile.Presets.Values)
            {
                if (group?.Items == null)
                {
                    continue;
                }

                foreach (var preset in group.Items)
                {
                    if (preset?.Files == null)
                    {
                        continue;
                    }

                    foreach (var relativeFile in preset.Files)
                    {
                        if (string.IsNullOrWhiteSpace(relativeFile))
                        {
                            continue;
                        }

                        yield return Path.Combine(currentThemePath, relativeFile);
                    }
                }
            }
        }

        private void LoadLuckyDayResourceOverride()
        {
            try
            {
                if (settings?.IsLuckyDay != true)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(currentThemePath))
                {
                    return;
                }

                var filePath = Path.Combine(
                    currentThemePath,
                    "Themes Option",
                    "2.Interface",
                    "Hidden",
                    "LuckyDay.xaml");

                if (!File.Exists(filePath))
                {
                    logger?.Warn($"[AnikiHelper] Lucky Day resource file not found: {filePath}");
                    return;
                }

                var resource = GetOrLoadResourceDictionary(filePath);

                if (resource != null)
                {
                    Application.Current.Resources.MergedDictionaries.Add(resource);
                    loadedDictionaries.Add(resource);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper] Failed to load Lucky Day resource override.");
            }
        }

        private ResourceDictionary BuildGeneratedResourceDictionary(Dictionary<string, object> values)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            var dictionary = new ResourceDictionary();

            foreach (var pair in values)
            {
                dictionary[pair.Key] = pair.Value;
            }

            return dictionary;
        }

        private void RemoveLoadedDictionaries()
        {
            foreach (var dictionary in loadedDictionaries.ToList())
            {
                try
                {
                    Application.Current.Resources.MergedDictionaries.Remove(dictionary);
                }
                catch
                {
                }
            }

            loadedDictionaries.Clear();
        }

        private object ConvertValue(string type, object rawValue)
        {
            var value = rawValue?.ToString();

            if (string.IsNullOrWhiteSpace(type))
            {
                return value;
            }

            try
            {
                switch (type.Trim().ToLowerInvariant())
                {
                    case "string":
                        return value ?? string.Empty;

                    case "boolean":
                    case "bool":
                        return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

                    case "int32":
                    case "int":
                        return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var intValue)
                            ? intValue
                            : 0;

                    case "double":
                        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue)
                            ? doubleValue
                            : 0d;

                    case "float":
                        return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var floatValue)
                            ? floatValue
                            : 0f;

                    case "visibility":
                        return string.Equals(value, "Visible", StringComparison.OrdinalIgnoreCase)
                            ? Visibility.Visible
                            : Visibility.Collapsed;

                    case "cornerradius":
                        return ParseCornerRadius(value);

                    case "thickness":
                        return ParseThickness(value);

                    case "color":
                        return ColorConverter.ConvertFromString(value);

                    case "solidcolorbrush":
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

                    case "timespan":
                        return TimeSpan.TryParse(value, out var timeSpan)
                            ? timeSpan
                            : TimeSpan.Zero;

                    default:
                        return value;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper] Failed to convert Aniki option value. Type={type}, Value={value}");
                return value;
            }
        }

        private CornerRadius ParseCornerRadius(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new CornerRadius(0);
            }

            var parts = value.Split(',');

            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var left) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var top) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var right) &&
                double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var bottom))
            {
                return new CornerRadius(left, top, right, bottom);
            }

            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var uniform)
                ? new CornerRadius(uniform)
                : new CornerRadius(0);
        }

        private Thickness ParseThickness(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new Thickness(0);
            }

            var parts = value.Split(',');

            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var left) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var top) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var right) &&
                double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var bottom))
            {
                return new Thickness(left, top, right, bottom);
            }

            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var uniform)
                ? new Thickness(uniform)
                : new Thickness(0);
        }
    }
}