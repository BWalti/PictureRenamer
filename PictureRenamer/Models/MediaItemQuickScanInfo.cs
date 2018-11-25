namespace PictureRenamer
{
    using System;
    using LiteDB;
    using PictureRenamer.Pipelines;

    public class MediaItemQuickScanInfo
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string FullName { get; set; }

        public string Name { get; set; }

        public long? Length { get; set; }

        public DateTime? LastWriteTimeUtc { get; set; }

        public ulong? Hash { get; set; }
        public DateTime? CreationTimeUtc { get; set; }
        public CustomMetaData MetaData { get; set; }
    }
}