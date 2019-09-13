namespace PictureRenamer
{
    using System;
    using Newtonsoft.Json;
    using SixLabors.ImageSharp.MetaData;

    public class ImageInformationContainer
    {
        public ImageMetaData ImageMetaData { get; set; }

        public FileSystemProperties FileSystemProperties { get; set; }
        public string Id { get; set; }
    }

    public class FileSystemProperties
    {
        public DateTime LastWriteTimeUtc { get; set; }
        public DateTime CreationTimeUtc { get; set; }
        public DateTime LastAccessTimeUtc { get; set; }
        public long Length { get; set; }
    }
}