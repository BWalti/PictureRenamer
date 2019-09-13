namespace PictureRenamer
{
    using System;
    using System.IO;
    using Serilog;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.MetaData;
    using SixLabors.ImageSharp.PixelFormats;

    public class PhotoContext : IDisposable
    {
        private FileStream stream;
        private bool isOpen;

        public PhotoContext(FileInfo source, ProcessContext context)
        {
            this.Source = source;
            this.Context = context;
        }

        public ProcessContext Context { get; }

        public Exception Error { get; set; }

        public bool HasError => this.Error != null;

        public ulong? Hash { get; set; }

        public string PossibleTargetFileName { get; private set; }

        public string PossibleTargetPath { get; private set; }

        public FileInfo Source { get; }

        public FileInfo Target { get; set; }

        public Image<Rgba32> RgbaImage { get; private set; }
        

        public void Dispose()
        {
            this.EnsureClosed();
        }

        public bool TryOpen()
        {
            if (this.isOpen)
            {
                return true;
            }

            try
            {
                this.stream = File.OpenRead(this.Source.FullName);
                this.RgbaImage = Image.Load(this.stream);
                this.MetaData = this.RgbaImage?.MetaData?.Clone();
                //this.ExifValues = this.RgbaImage?.MetaData?.ExifProfile?.Values?.ToDictionary(value => value.Tag, value => value.Value);
                //this.ImageProperties = this.RgbaImage?.MetaData?.Properties.ToDictionary(prop => prop.Name, prop => prop.Value);
                
                this.isOpen = true;
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("Could not open image", e);
                this.RgbaImage?.Dispose();
                this.stream?.Dispose();
                return false;
            }
        }

        public ImageMetaData MetaData { get; set; }

        public void SetPossibleSolution(string targetPath, string targetFileName)
        {
            this.PossibleTargetPath = targetPath;
            this.PossibleTargetFileName = targetFileName;
        }

        public void EnsureClosed()
        {
            this.RgbaImage?.Dispose();
            this.stream?.Dispose();
            this.isOpen = false;
        }
    }
}