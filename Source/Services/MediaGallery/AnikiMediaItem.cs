using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnikiHelper.Services.MediaGallery
{
    public class AnikiMediaItem : ObservableObject
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

        private string name;
        public string Name
        {
            get => name;
            set => SetValue(ref name, value);
        }

        private string filePath;
        public string FilePath
        {
            get => filePath;
            set
            {
                SetValue(ref filePath, value);
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(Exists));
                OnPropertyChanged(nameof(ThumbnailOrFilePath));
                OnPropertyChanged(nameof(DisplayThumbnailPath));
                OnPropertyChanged(nameof(HasUsableThumbnail));
                OnPropertyChanged(nameof(FileSizeString));
            }
        }

        private string thumbnailPath;
        public string ThumbnailPath
        {
            get => thumbnailPath;
            set
            {
                SetValue(ref thumbnailPath, value);
                OnPropertyChanged(nameof(ThumbnailOrFilePath));
                OnPropertyChanged(nameof(DisplayThumbnailPath));
                OnPropertyChanged(nameof(HasUsableThumbnail));
            }
        }

        private DateTime captureDate;
        public DateTime CaptureDate
        {
            get => captureDate;
            set
            {
                SetValue(ref captureDate, value);
                OnPropertyChanged(nameof(CaptureDateString));
            }
        }

        private string durationString;
        public string DurationString
        {
            get => durationString;
            set
            {
                SetValue(ref durationString, value);
                OnPropertyChanged(nameof(DisplayDurationString));
            }
        }

        [DontSerialize]
        public string DisplayDurationString
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DurationString))
                {
                    return string.Empty;
                }

                if (!TimeSpan.TryParse(DurationString, out var duration))
                {
                    return DurationString;
                }

                if (duration.TotalSeconds <= 0)
                {
                    return string.Empty;
                }

                if (duration.TotalHours >= 1)
                {
                    return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
                }

                return $"{duration.Minutes:00}:{duration.Seconds:00}";
            }
        }

        private bool isVideo;
        public bool IsVideo
        {
            get => isVideo;
            set
            {
                SetValue(ref isVideo, value);
                OnPropertyChanged(nameof(IsImage));
            }
        }

        public bool IsImage => !IsVideo;

        private string sourceProvider;
        public string SourceProvider
        {
            get => sourceProvider;
            set => SetValue(ref sourceProvider, value);
        }

        public string FileName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilePath))
                {
                    return string.Empty;
                }

                return Path.GetFileName(FilePath);
            }
        }

        private int mediaIndex;
        public int MediaIndex
        {
            get => mediaIndex;
            set
            {
                SetValue(ref mediaIndex, value);
                OnPropertyChanged(nameof(MediaIndexString));
            }
        }

        private int mediaTotal;
        public int MediaTotal
        {
            get => mediaTotal;
            set
            {
                SetValue(ref mediaTotal, value);
                OnPropertyChanged(nameof(MediaIndexString));
            }
        }

        public string MediaIndexString
        {
            get
            {
                if (MediaTotal <= 0)
                {
                    return string.Empty;
                }

                return $"{MediaIndex}/{MediaTotal}";
            }
        }

        [DontSerialize]
        public string DisplayThumbnailPath
        {
            get
            {
                var path = ThumbnailPath;

                if (string.IsNullOrWhiteSpace(path))
                {
                    path = ThumbnailOrFilePath;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                var ext = Path.GetExtension(path).ToLowerInvariant();

                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".bmp")
                {
                    return path;
                }

                return null;
            }
        }

        [DontSerialize]
        public bool HasUsableThumbnail
        {
            get => !string.IsNullOrWhiteSpace(DisplayThumbnailPath);
        }

        public string FileSizeString
        {
            get
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
                    {
                        return string.Empty;
                    }

                    var length = new FileInfo(FilePath).Length;

                    if (length >= 1024L * 1024L * 1024L)
                    {
                        return $"{length / 1024d / 1024d / 1024d:0.##} GB";
                    }

                    if (length >= 1024L * 1024L)
                    {
                        return $"{length / 1024d / 1024d:0.##} MB";
                    }

                    if (length >= 1024L)
                    {
                        return $"{length / 1024d:0.##} KB";
                    }

                    return $"{length} B";
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public string ThumbnailOrFilePath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ThumbnailPath))
                {
                    return ThumbnailPath;
                }

                return FilePath ?? string.Empty;
            }
        }

        public string CaptureDateString
        {
            get
            {
                if (CaptureDate == DateTime.MinValue)
                {
                    return string.Empty;
                }

                return CaptureDate.ToString("dd/MM/yyyy HH:mm");
            }
        }

        public bool Exists
        {
            get
            {
                return !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
            }
        }
    }
}