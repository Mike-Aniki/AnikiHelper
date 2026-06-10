using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;

namespace AnikiHelper.Services.MediaGallery
{
    public class AnikiMediaGameItem : ObservableObject
    {
        private Guid gameId;
        public Guid GameId
        {
            get => gameId;
            set => SetValue(ref gameId, value);
        }

        private string gameName;
        public string GameName
        {
            get => gameName;
            set => SetValue(ref gameName, value);
        }

        private string coverPath;
        public string CoverPath
        {
            get => coverPath;
            set => SetValue(ref coverPath, value);
        }

        private int mediaCount;
        public int MediaCount
        {
            get
            {
                return mediaCount;
            }
            set
            {
                SetValue(ref mediaCount, value);
                OnPropertyChanged(nameof(MediaCountString));
            }
        }

        private int imageCount;
        public int ImageCount
        {
            get => imageCount;
            set => SetValue(ref imageCount, value);
        }

        private int videoCount;
        public int VideoCount
        {
            get => videoCount;
            set => SetValue(ref videoCount, value);
        }

        private DateTime latestCaptureDate;
        public DateTime LatestCaptureDate
        {
            get
            {
                return latestCaptureDate;
            }
            set
            {
                SetValue(ref latestCaptureDate, value);
                OnPropertyChanged(nameof(LatestCaptureDateString));
            }
        }

        private DateTime oldestCaptureDate;
        public DateTime OldestCaptureDate
        {
            get => oldestCaptureDate;
            set => SetValue(ref oldestCaptureDate, value);
        }

        private string sourceProvider;
        public string SourceProvider
        {
            get => sourceProvider;
            set => SetValue(ref sourceProvider, value);
        }

        public string MediaCountString
        {
            get
            {
                if (MediaCount <= 1)
                {
                    return MediaCount + " capture";
                }

                return MediaCount + " captures";
            }
        }

        public string LatestCaptureDateString
        {
            get
            {
                if (LatestCaptureDate == DateTime.MinValue)
                {
                    return string.Empty;
                }

                return LatestCaptureDate.ToString("dd/MM/yyyy HH:mm");
            }
        }

        public bool HasCover
        {
            get
            {
                return !string.IsNullOrWhiteSpace(CoverPath) && File.Exists(CoverPath);
            }
        }
    }
}