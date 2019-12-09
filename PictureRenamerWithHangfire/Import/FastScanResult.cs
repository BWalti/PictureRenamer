namespace PictureRenamerWithHangfire.Import
{
    using System;

    using LiteDB;

    public class FastScanResult
    {
        public ObjectId Id { get; set; }

        public string RelativePath { get; set; }

        public DateTime CreationTimeUtc { get; set; }
        
        public DateTime LastWriteTimeUtc { get; set; }

        public long Size { get; set; }
    }
}