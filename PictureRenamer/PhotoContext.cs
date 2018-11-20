namespace PictureRenamer
{
    using System;
    using System.IO;

    using MetadataExtractor.Formats.Exif;

    public class PhotoContext
    {
        public PhotoContext(FileInfo source, ProcessContext context)
        {
            this.Source = source;
            this.Context = context;
        }

        public ProcessContext Context { get; }

        public Exception Error { get; set; }

        public ExifIfd0Directory ExifIfd0 { get; set; }

        public ExifSubIfdDirectory ExifSubIfs { get; set; }

        public GpsDirectory Gps { get; set; }

        public bool HasError => this.Error != null;

        public ulong Hash { get; set; }

        public string PossibleTargetFileName { get; private set; }

        public string PossibleTargetPath { get; private set; }

        public FileInfo Source { get; }

        public string Target { get; set; }

        public void SetPossibleSolution(string targetPath, string targetFileName)
        {
            this.PossibleTargetPath = targetPath;
            this.PossibleTargetFileName = targetFileName;
        }
    }
}