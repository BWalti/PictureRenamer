namespace PictureRenamerWithHangfire
{
    using System.ComponentModel;
    using System.IO;

    using PictureRenamerWithHangfire.Controllers;
    using PictureRenamerWithHangfire.Import;

    public class ScanOptions
    {
        public string Input { get; set; }

        public string Output { get; set; }

        public string RecycleBin { get; set; }

        public string ScanDatabasePath => Path.Combine(this.Output, "scan.db");

        public string GetPathFor(Location location)
        {
            return location switch
                {
                    Location.Input => this.Input,
                    Location.Output => this.Output,
                    Location.RecycleBin => this.RecycleBin,
                    _ => throw new InvalidEnumArgumentException(nameof(location))
                };
        }
    }
}