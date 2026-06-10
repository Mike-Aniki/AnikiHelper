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

            foreach (var item in values)
            {
                if (!ContainsKey(item.Key) || !Equals(this[item.Key], item.Value))
                {
                    this[item.Key] = item.Value;
                }
            }

            var toRemove = Keys
                .Where(key => !values.ContainsKey(key))
                .ToList();

            foreach (var key in toRemove)
            {
                Remove(key);
                OnPropertyChanged(key);
                OnPropertyChanged("Item[]");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}