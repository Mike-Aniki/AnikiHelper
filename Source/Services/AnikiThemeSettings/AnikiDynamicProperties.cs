using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace AnikiHelper.Services.AnikiThemeSettings
{
    public class AnikiDynamicProperties : Dictionary<string, object>, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public AnikiDynamicProperties()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public new object this[string propertyName]
        {
            get
            {
                TryGetValue(propertyName, out var value);
                return value;
            }
            set
            {
                base[propertyName] = value;
                OnPropertyChanged(propertyName);
                OnPropertyChanged("Item[]");
            }
        }

        public void Set(string key, object value)
        {
            this[key] = value;
        }

        public void Update(IDictionary<string, object> values)
        {
            if (values == null)
            {
                return;
            }

            var changedKeys = new List<string>();

            foreach (var item in values)
            {
                if (!ContainsKey(item.Key) || !Equals(base[item.Key], item.Value))
                {
                    base[item.Key] = item.Value;
                    changedKeys.Add(item.Key);
                }
            }

            var toRemove = Keys
                .Where(key => !values.ContainsKey(key))
                .ToList();

            foreach (var key in toRemove)
            {
                base.Remove(key);
                changedKeys.Add(key);
            }

            if (changedKeys.Count == 0)
            {
                return;
            }

            foreach (var key in changedKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                OnPropertyChanged(key);
            }

            // WPF indexer bindings listen to Item[]. Raising it once avoids dozens of expensive refreshes at startup.
            OnPropertyChanged("Item[]");
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}