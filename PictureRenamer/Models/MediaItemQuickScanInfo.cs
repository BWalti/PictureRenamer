namespace PictureRenamer
{
    using System;
    using System.Collections.Generic;
    using LiteDB;

    public class MediaItemQuickScanInfo
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string FullName { get; set; }

        public string Name { get; set; }

        public long? Length { get; set; }

        public DateTime? LastWriteTimeUtc { get; set; }

        public ulong? Hash { get; set; }

        public ExifInfos Exif { get; set; }
    }

    public class ExifInfos
    {
        public Dictionary<int, string> ExifIfd0 { get; set; }

        public Dictionary<int, string> ExifSubIfs { get; set; }

        public Dictionary<int, string> Gps { get; set; }
    }
}