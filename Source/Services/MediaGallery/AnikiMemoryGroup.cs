using System;
using System.Collections.Generic;

namespace AnikiHelper.Services.MediaGallery
{
    public class AnikiMemoryGroup
    {
        public Guid GameId { get; set; }

        public string GameName { get; set; }

        public DateTime MemoryDate { get; set; }

        public List<AnikiMediaItem> Screenshots { get; set; }
            = new List<AnikiMediaItem>();
    }
}