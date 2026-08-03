using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;

namespace AnikiHelper.Services.WebBrowser
{
    public sealed class AnikiWebFavorite : ObservableObject
    {
        private string name = string.Empty;
        private string url = string.Empty;

        public string Name
        {
            get { return name ?? string.Empty; }
            set { SetValue(ref name, value ?? string.Empty); }
        }

        public string Url
        {
            get { return url ?? string.Empty; }
            set { SetValue(ref url, value ?? string.Empty); }
        }

        [DontSerialize]
        public string DisplayInitial
        {
            get
            {
                var value = (Name ?? string.Empty).Trim();
                return value.Length == 0 ? "W" : value.Substring(0, 1).ToUpperInvariant();
            }
        }

        [DontSerialize]
        public string DisplayHost
        {
            get
            {
                Uri uri;
                return Uri.TryCreate((Url ?? string.Empty).Trim(), UriKind.Absolute, out uri)
                    ? uri.Host
                    : (Url ?? string.Empty).Trim();
            }
        }

        public AnikiWebFavorite Clone()
        {
            return new AnikiWebFavorite
            {
                Name = Name,
                Url = Url
            };
        }
    }
}
