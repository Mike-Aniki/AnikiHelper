using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;

namespace AnikiHelper.Services.AnikiThemeSettings
{
    public class AnikiThemeSettingsFile
    {
        public Dictionary<string, AnikiPresetGroup> Presets { get; set; }
            = new Dictionary<string, AnikiPresetGroup>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AnikiThemeVariable> Variables { get; set; }
            = new Dictionary<string, AnikiThemeVariable>(StringComparer.OrdinalIgnoreCase);
    }

    public class AnikiPresetGroup : ObservableObject
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Title { get; set; }

        public string LocKey { get; set; }

        public string Description { get; set; }

        public string DescriptionLocKey { get; set; }

        public string Category { get; set; }

        public bool NeedRestart { get; set; }

        public Dictionary<string, AnikiPresetItem> Presets { get; set; }
            = new Dictionary<string, AnikiPresetItem>(StringComparer.OrdinalIgnoreCase);

        [DontSerialize]
        public ObservableCollection<AnikiPresetItem> Items { get; set; }
            = new ObservableCollection<AnikiPresetItem>();   

        private string selectedPresetKey;

        [DontSerialize]
        public string SelectedPresetKey
        {
            get => selectedPresetKey;
            set
            {
                var finalValue = value ?? string.Empty;

                if (string.Equals(selectedPresetKey, finalValue, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetValue(ref selectedPresetKey, finalValue);
                SelectionChangedAction?.Invoke(Id, finalValue);
            }
        }

        [DontSerialize]
        public string LocalizedName { get; set; }

        [DontSerialize]
        public string LocalizedDescription { get; set; }

        [DontSerialize]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LocalizedName))
                {
                    return LocalizedName;
                }

                if (!string.IsNullOrWhiteSpace(Title))
                {
                    return Title;
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }

                return Id;
            }
        }

        [DontSerialize]
        public string DisplayDescription
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LocalizedDescription))
                {
                    return LocalizedDescription;
                }

                return Description;
            }
        }

        [DontSerialize]
        public Action<string, string> SelectionChangedAction { get; set; }

        public void SetSelectedPresetKeySilently(string value)
        {
            selectedPresetKey = value ?? string.Empty;
            OnPropertyChanged(nameof(SelectedPresetKey));
        }
    }

    public class AnikiPresetItem : ObservableObject
    {
        public string Id { get; set; }

        public string GroupId { get; set; }

        public string Key { get; set; }

        public string Name { get; set; }

        public string Title { get; set; }

        public string LocKey { get; set; }

        public string Category { get; set; }

        public string Preview { get; set; }

        public bool NeedRestart { get; set; }

        public List<string> Files { get; set; }
            = new List<string>();

        public Dictionary<string, AnikiThemeValue> Constants { get; set; }
            = new Dictionary<string, AnikiThemeValue>(StringComparer.OrdinalIgnoreCase);    

        [DontSerialize]
        public string LocalizedName { get; set; }

        [DontSerialize]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LocalizedName))
                {
                    return LocalizedName;
                }

                if (!string.IsNullOrWhiteSpace(Title))
                {
                    return Title;
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }

                return Key;
            }
        }

        private bool isSelected;

        [DontSerialize]
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                SetValue(ref isSelected, value);
            }
        }
    }

    public class AnikiThemeVariable : AnikiThemeValue
    {
        [DontSerialize]
        public string Id { get; set; }

        public string Title { get; set; }

        public string Name { get; set; }

        public string LocKey { get; set; }

        public string Description { get; set; }

        public string DescriptionLocKey { get; set; }

        public string Category { get; set; }

        public string Preview { get; set; }

        public string Style { get; set; }

        public string TitleStyle { get; set; }

        public string HeaderKind { get; set; }

        public int CategoryOrder { get; set; } = 999;

        public bool NeedRestart { get; set; }

        public AnikiThemeSlider Slider { get; set; }

        [DontSerialize]
        public string LocalizedName { get; set; }

        [DontSerialize]
        public string LocalizedDescription { get; set; }

        [DontSerialize]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LocalizedName))
                {
                    return LocalizedName;
                }

                if (!string.IsNullOrWhiteSpace(Title))
                {
                    return Title;
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }

                return Id;
            }
        }

        [DontSerialize]
        public string DisplayDescription
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LocalizedDescription))
                {
                    return LocalizedDescription;
                }

                return Description;
            }
        }

        private bool currentBooleanValue;

        [DontSerialize]
        public bool CurrentBooleanValue
        {
            get => currentBooleanValue;
            set
            {
                if (currentBooleanValue == value)
                {
                    return;
                }

                SetValue(ref currentBooleanValue, value);
                ValueChangedAction?.Invoke(Id, value.ToString());
            }
        }

        private double currentDoubleValue;
        private Timer currentDoubleValueDebounceTimer;

        [DontSerialize]
        public double CurrentDoubleValue
        {
            get => currentDoubleValue;
            set
            {
                if (Math.Abs(currentDoubleValue - value) < 0.0001)
                {
                    return;
                }

                SetValue(ref currentDoubleValue, value);

                currentDoubleValueDebounceTimer?.Dispose();
                currentDoubleValueDebounceTimer = new Timer(_ =>
                {
                    try
                    {
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;

                        if (dispatcher != null)
                        {
                            dispatcher.BeginInvoke(new Action(() =>
                            {
                                ValueChangedAction?.Invoke(Id, value.ToString(CultureInfo.InvariantCulture));
                            }));
                        }
                        else
                        {
                            ValueChangedAction?.Invoke(Id, value.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    catch
                    {
                    }
                }, null, 350, Timeout.Infinite);
            }
        }

        private string currentStringValue;

        [DontSerialize]
        public string CurrentStringValue
        {
            get => currentStringValue;
            set
            {
                var finalValue = value ?? string.Empty;

                if (string.Equals(currentStringValue, finalValue, StringComparison.Ordinal))
                {
                    return;
                }

                SetValue(ref currentStringValue, finalValue);
                ValueChangedAction?.Invoke(Id, finalValue);
            }
        }

        [DontSerialize]
        public Action<string, string> ValueChangedAction { get; set; }

        public void SetCurrentBooleanValueSilently(bool value)
        {
            currentBooleanValue = value;
            OnPropertyChanged(nameof(CurrentBooleanValue));
        }

        public void SetCurrentDoubleValueSilently(double value)
        {
            currentDoubleValueDebounceTimer?.Dispose();
            currentDoubleValueDebounceTimer = null;

            currentDoubleValue = value;
            OnPropertyChanged(nameof(CurrentDoubleValue));
        }

        public void SetCurrentStringValueSilently(string value)
        {
            currentStringValue = value ?? string.Empty;
            OnPropertyChanged(nameof(CurrentStringValue));
        }
    }

    public class AnikiThemeValue : ObservableObject
    {
        public string Type { get; set; }

        public object Value { get; set; }

        public object Default { get; set; }

        [DontSerialize]
        public string EffectiveValue
        {
            get
            {
                return Value != null
                    ? Value.ToString()
                    : Default?.ToString();
            }
        }
    }

    public class AnikiThemeSlider
    {
        public object Min { get; set; }

        public object Max { get; set; }

        public object Step { get; set; }

        public object SmallChange { get; set; }

        public object LargeChange { get; set; }

        [DontSerialize]
        public double MinValue => ToDouble(Min, 0);

        [DontSerialize]
        public double MaxValue => ToDouble(Max, 100);

        [DontSerialize]
        public double StepValue => ToDouble(Step, 1);

        [DontSerialize]
        public double SmallChangeValue => ToDouble(SmallChange, StepValue);

        [DontSerialize]
        public double LargeChangeValue => ToDouble(LargeChange, StepValue * 5);

        private static double ToDouble(object value, double fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return fallback;
        }
    }

    public class AnikiThemeSettingsCategory
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string LocKey { get; set; }

        public string Icon { get; set; }

        public string WindowTitle { get; set; }

        public int Order { get; set; } = 999;

        public ObservableCollection<object> Items { get; set; }
            = new ObservableCollection<object>();
    }



    public class AnikiThemeSettingsStorageFile
    {
        public int SchemaVersion { get; set; } = 1;

        public Dictionary<string, string> Values { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> SelectedPresets { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class AnikiThemeSettingsLegacyConfigFile
    {
        public Dictionary<string, string> AnikiThemeSettingsValues { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> AnikiThemeSettingsSelectedPresets { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}