namespace PictureRenamerWithHangfire.Import
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using CoenM.ImageHash;
    using CoenM.ImageHash.HashAlgorithms;

    using LiteDB;

    using MetadataExtractor.Util;

    using Microsoft.Extensions.Options;

    public class CalculateHashes
    {
        private static readonly PerceptualHash Hash = new PerceptualHash();


        private static readonly HashSet<FileType> AllowedFileTypes = new HashSet<FileType>
                                                                         {
                                                                             FileType.Jpeg,
                                                                             FileType.Gif,
                                                                             FileType.Png,
                                                                             FileType.Bmp,
                                                                             FileType.Arw,
                                                                             FileType.Cr2,
                                                                             FileType.Crw,
                                                                             FileType.Orf,
                                                                             FileType.Nef,
                                                                             FileType.Rw2,
                                                                         };

        private static readonly Regex MediaFileNamePattern = new Regex(
            @"\.(jpg|png|jpeg|thm|orf|psd|arw|cr2|mov|mp4)",
            RegexOptions.IgnoreCase);

        private readonly LiteDatabase liteDatabase;

        private readonly IOptions<ScanOptions> options;

        private static readonly FileType[] HashableTypes;

        public CalculateHashes(LiteDatabase liteDatabase, IOptions<ScanOptions> options)
        {
            this.liteDatabase = liteDatabase;
            this.options = options;
        }

        static CalculateHashes()
        {
            HashableTypes = new[] { FileType.Jpeg, FileType.Gif, FileType.Png, FileType.Bmp };
        }

        public void CreateDeepScanResult(Location location, FastScanResult scanResult)
        {
            var deepScanResultsCollection = this.liteDatabase.GetCollection<DeepScanResult>($"{location}{nameof(DeepScanResult)}");
            var deepScanResult = new DeepScanResult
                                     {
                                         FastScanResultId = scanResult.Id
                                     };

            if (!MediaFileNamePattern.IsMatch(Path.GetExtension(scanResult.RelativePath)))
            {
                deepScanResult.IsMediaFile = false;
            }
            else
            {
                var path = this.options.Value.GetPathFor(location);
                using var stream = File.OpenRead(Path.Combine(path, scanResult.RelativePath));
                var detectFileType = FileTypeDetector.DetectFileType(stream);

                if (HashableTypes.Contains(detectFileType))
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    deepScanResult.PerceptualHash = Hash.Hash(stream);
                    deepScanResult.FileType = detectFileType;
                }
            }

            deepScanResultsCollection.Insert(deepScanResult);
        }
    }
}