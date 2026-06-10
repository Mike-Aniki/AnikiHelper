using System;

namespace AnikiHelper.Services.Achievements
{
    public class AnikiAchievementMemoryItem
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string GameBackgroundPath { get; set; }
        public string IconPath { get; set; }
        public double? Percent { get; set; }
        public string Rarity { get; set; }
        public DateTime UnlockDate { get; set; }

        public string UnlockDateString
        {
            get { return UnlockDate == DateTime.MinValue ? string.Empty : UnlockDate.ToString("dd/MM/yyyy"); }
        }

        public string PercentString
        {
            get { return Percent.HasValue ? Percent.Value.ToString("0.##") + "%" : string.Empty; }
        }

        public bool HasIcon
        {
            get { return !string.IsNullOrWhiteSpace(IconPath); }
        }
    }
}