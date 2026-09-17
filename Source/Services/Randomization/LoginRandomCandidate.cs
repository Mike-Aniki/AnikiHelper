using System;

namespace AnikiHelper.Services.Randomization
{
    public sealed class LoginRandomCandidate
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public int BuiltInIndex { get; set; }
        public string CommunityPackId { get; set; }
        public string CommunityLocalId { get; set; }
        public string VideoPath { get; set; }

        public bool IsCommunityPack => !string.IsNullOrWhiteSpace(CommunityLocalId);
    }

    public sealed class LoginRandomPoolItem : System.Collections.Generic.ObservableObject
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool IsCommunityPack { get; set; }

        private bool isIncluded;
        public bool IsIncluded
        {
            get => isIncluded;
            set => SetValue(ref isIncluded, value);
        }
    }


    public sealed class VisualPackRandomCandidate
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string PresetKey { get; set; }
        public string CommunityPackId { get; set; }
        public string CommunityLocalId { get; set; }

        public bool IsCommunityPack => !string.IsNullOrWhiteSpace(CommunityLocalId);
    }

    public sealed class VisualPackRandomPoolItem : System.Collections.Generic.ObservableObject
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool IsCommunityPack { get; set; }

        private bool isIncluded;
        public bool IsIncluded
        {
            get => isIncluded;
            set => SetValue(ref isIncluded, value);
        }
    }

    public sealed class ThemeColorRandomCandidate
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string PresetKey { get; set; }
        public string CommunityPackId { get; set; }
        public string CommunityLocalId { get; set; }

        public bool IsCommunityPack => !string.IsNullOrWhiteSpace(CommunityLocalId);
    }

    public sealed class ThemeColorRandomPoolItem : System.Collections.Generic.ObservableObject
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool IsCommunityPack { get; set; }

        private bool isIncluded;
        public bool IsIncluded
        {
            get => isIncluded;
            set => SetValue(ref isIncluded, value);
        }
    }
}
