namespace PictureRenamerWithHangfire.Import
{
    using LiteDB;

    using MetadataExtractor.Util;

    public class DeepScanResult
    {
        public ObjectId Id { get; set; }

        public ObjectId FastScanResultId { get; set; }

        public bool IsMediaFile { get; set; }

        public FileType FileType { get; set; }

        public ulong PerceptualHash { get; set; }
    }
}