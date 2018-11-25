namespace PictureRenamer.Pipelines
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using CoenM.ImageHash;
    using CoenM.ImageHash.HashAlgorithms;
    using LiteDB;
    using Serilog;
    using SixLabors.ImageSharp.MetaData;
    using SixLabors.ImageSharp.MetaData.Profiles.Exif;

    public class FullMediaItemPipeline : IRunnablePipeline, IDisposable
    {
        private static readonly DataflowLinkOptions DataflowLinkOptions =
            new DataflowLinkOptions {PropagateCompletion = true};

        private readonly IImageHash imageHasher;
        private readonly DirectoryInfo inputDirectoryInfo;
        private readonly LiteCollection<MediaItemQuickScanInfo> mediaItemCollection;
        private readonly DirectoryInfo outputDirectoryInfo;
        private readonly DirectoryInfo recycleBin;
        private readonly LiteDatabase db;

        public FullMediaItemPipeline(DirectoryInfo inputDirectoryInfo, DirectoryInfo outputDirectoryInfo,
            DirectoryInfo recycleBin)
        {
            this.db = new LiteDatabase(Path.Combine(outputDirectoryInfo.FullName, "mediaItems.db"));
            this.mediaItemCollection = this.db.GetCollection<MediaItemQuickScanInfo>();
            this.mediaItemCollection.EnsureIndex(info => info.FullName);
            this.mediaItemCollection.EnsureIndex(info => info.Hash);

            this.inputDirectoryInfo = inputDirectoryInfo;
            this.outputDirectoryInfo = outputDirectoryInfo;
            this.recycleBin = recycleBin;
            this.imageHasher = new PerceptualHash();
        }

        /// <summary>
        ///     Move all from Input-Directory to Output-Directory, well named.
        ///     Then for each new item, invoke "duplicate" check. For that, hash needs to be calculated first
        /// </summary>
        /// <returns></returns>
        public Task Run()
        {
            var fileScannerBlock = BlockCreator.CreateFileScannerBlock();
            var hashBlock = this.CreateHashBlock();

            // either move to recycle as duplicate
            var moveToRecycleBin = BlockCreator.CreateMoverAction(this.recycleBin.FullName);
            var finish = new ActionBlock<PhotoContext>(context => { });

            // or continue with the meta data analysis
            var analysis = BlockCreator.ReadImageMetadataBlock();

            var suggestion = BlockCreator.CreateSuggestionBlock();
            var mover = BlockCreator.CreateMoverAction();
            var batched = new BatchBlock<PhotoContext>(100);
            var registerMetadata = this.CreateRegisterMetadataBlock();

            fileScannerBlock.LinkTo(hashBlock, DataflowLinkOptions);
            
            // if hash is known -> move to recycle
            // if unknown, calculate other metrics and continue processing...
            var checkDuplicate = this.CreateCheckDuplicateBlock(analysis, moveToRecycleBin);
            hashBlock.LinkTo(checkDuplicate, DataflowLinkOptions);

            analysis.LinkTo(suggestion, DataflowLinkOptions);
            suggestion.LinkTo(mover, DataflowLinkOptions);
            mover.LinkTo(batched, DataflowLinkOptions);
            batched.LinkTo(registerMetadata, DataflowLinkOptions);

            moveToRecycleBin.LinkTo(finish, DataflowLinkOptions);
            finish.Completion.ContinueWith(task =>
            {
                Log.Information("Moving to recycle bin completed..");
            });

            var dbDisposed = registerMetadata.Completion.ContinueWith(task =>
            {
                Log.Information("Processing of new pictures completed..");
                this.db.Dispose();
            });

            var processContext = new ProcessContext(this.inputDirectoryInfo, this.outputDirectoryInfo);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return Task.WhenAll(
                dbDisposed, 
                finish.Completion);
        }

        private ITargetBlock<PhotoContext[]> CreateRegisterMetadataBlock()
        {
            return new ActionBlock<PhotoContext[]>(contexts =>
            {
                var converted = contexts.Select(context => new MediaItemQuickScanInfo
                {
                    FullName = context.Target.FullName,
                    LastWriteTimeUtc = context.Target.LastWriteTimeUtc,
                    CreationTimeUtc = context.Target.CreationTimeUtc,
                    Hash = context.Hash,
                    Length = context.Target.Length,
                    Name = context.Target.Name,
                    MetaData = this.ConvertMetaData(context.MetaData)
                    //ExifValues = context.ExifValues,
                    //ImageProperties = context.ImageProperties
                });

                var insertBulk = this.mediaItemCollection.InsertBulk(converted);
                Log.Information($"Inserted {insertBulk}# image media into db");
            });
        }

        private CustomMetaData ConvertMetaData(ImageMetaData contextMetaData)
        {
            var exifProfile = contextMetaData.ExifProfile;

            return new CustomMetaData
            {
                HorizontalResolution = contextMetaData.HorizontalResolution,
                VerticalResolution = contextMetaData.VerticalResolution,
                ResolutionUnits = contextMetaData.ResolutionUnits,
                Aperture = exifProfile?.GetValue(ExifTag.ApertureValue)?.Value?.ToString(),
                ShutterSpeed = exifProfile?.GetValue(ExifTag.ShutterSpeedValue)?.Value?.ToString(),
                AmbientTemperature = exifProfile?.GetValue(ExifTag.AmbientTemperature)?.Value?.ToString(),
                BatteryLevel = exifProfile?.GetValue(ExifTag.BatteryLevel)?.Value?.ToString(),
                BrightnessValue = exifProfile?.GetValue(ExifTag.BrightnessValue)?.Value?.ToString(),
                DateTimeDigitized = exifProfile?.GetValue(ExifTag.DateTimeDigitized)?.Value?.ToString(),
                DateTimeOriginal = exifProfile?.GetValue(ExifTag.DateTimeOriginal)?.Value?.ToString(),
                DateTime = exifProfile?.GetValue(ExifTag.DateTime)?.Value?.ToString(),
                DigitalZoomRatio = exifProfile?.GetValue(ExifTag.DigitalZoomRatio)?.Value?.ToString(),
                ExposureIndex = exifProfile?.GetValue(ExifTag.ExposureIndex)?.Value?.ToString(),
                ExposureIndex2 = exifProfile?.GetValue(ExifTag.ExposureIndex2)?.Value?.ToString(),
                RecommendedExposureIndex = exifProfile?.GetValue(ExifTag.RecommendedExposureIndex)?.Value?.ToString(),
                ExposureMode = exifProfile?.GetValue(ExifTag.ExposureMode)?.Value?.ToString(),
                ExposureProgram = exifProfile?.GetValue(ExifTag.ExposureProgram)?.Value?.ToString(),
                ExposureTime = exifProfile?.GetValue(ExifTag.ExposureTime)?.Value?.ToString(),
                FNumber = exifProfile?.GetValue(ExifTag.FNumber)?.Value?.ToString(),
                LensMake = exifProfile?.GetValue(ExifTag.LensMake)?.Value?.ToString(),
                LensModel = exifProfile?.GetValue(ExifTag.LensModel)?.Value?.ToString(),
                LensInfo = exifProfile?.GetValue(ExifTag.LensInfo)?.Value?.ToString(),
                LensSerialNumber = exifProfile?.GetValue(ExifTag.LensSerialNumber)?.Value?.ToString(),
                Make = exifProfile?.GetValue(ExifTag.Make)?.Value?.ToString(),
                Model = exifProfile?.GetValue(ExifTag.Model)?.Value?.ToString(),
                FocalLength = exifProfile?.GetValue(ExifTag.FocalLength)?.Value?.ToString(),
                GPSLatitude = exifProfile?.GetValue(ExifTag.GPSLatitude)?.Value?.ToString(),
                GPSLongitude = exifProfile?.GetValue(ExifTag.GPSLongitude)?.Value?.ToString(),
                GPSTimestamp = exifProfile?.GetValue(ExifTag.GPSTimestamp)?.Value?.ToString(),
                GPSDateStamp = exifProfile?.GetValue(ExifTag.GPSDateStamp)?.Value?.ToString(),
                GPSSatellites = exifProfile?.GetValue(ExifTag.GPSSatellites)?.Value?.ToString(),
                ISOSpeed = exifProfile?.GetValue(ExifTag.ISOSpeed)?.Value?.ToString(),
                Orientation = exifProfile?.GetValue(ExifTag.Orientation)?.Value?.ToString(),
                PixelXDimension = exifProfile?.GetValue(ExifTag.PixelXDimension)?.Value?.ToString(),
                PixelYDimension = exifProfile?.GetValue(ExifTag.PixelYDimension)?.Value?.ToString(),
                PixelScale = exifProfile?.GetValue(ExifTag.PixelScale)?.Value?.ToString(),
                Saturation = exifProfile?.GetValue(ExifTag.Saturation)?.Value?.ToString(),
                SelfTimerMode = exifProfile?.GetValue(ExifTag.SelfTimerMode)?.Value?.ToString(),
                SceneType = exifProfile?.GetValue(ExifTag.SceneType)?.Value?.ToString(),
                SceneCaptureType = exifProfile?.GetValue(ExifTag.SceneCaptureType)?.Value?.ToString(),
                SensingMethod = exifProfile?.GetValue(ExifTag.SensingMethod)?.Value?.ToString(),
                SensingMethod2 = exifProfile?.GetValue(ExifTag.SensingMethod2)?.Value?.ToString(),
                Software = exifProfile?.GetValue(ExifTag.Software)?.Value?.ToString(),
                WhiteBalance = exifProfile?.GetValue(ExifTag.WhiteBalance)?.Value?.ToString(),
            };
        }

        private ITargetBlock<PhotoContext> CreateCheckDuplicateBlock(ITargetBlock<PhotoContext> analysis, ITargetBlock<PhotoContext> moveToRecycleBin)
        {
            var checkDuplicateBlock = new ActionBlock<PhotoContext>(context =>
                {
                    var existingEntry = this.mediaItemCollection.FindOne(image => image.Hash == context.Hash);
                    if (existingEntry == null)
                    {
                        Log.Information($"New picture found: {context.Source.FullName}");
                        analysis.Post(context);
                    }
                    else
                    {
                        Log.Warning($"Possible duplicate picture found: {context.Source.FullName}. Matches: {existingEntry.FullName}");
                        moveToRecycleBin.Post(context);
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1
                });

            checkDuplicateBlock.Completion.ContinueWith(task =>
            {
                analysis.Complete();
                moveToRecycleBin.Complete();
            });

            return checkDuplicateBlock;
        }

        private TransformBlock<PhotoContext, PhotoContext> CreateHashBlock()
        {
            return new TransformBlock<PhotoContext, PhotoContext>(context =>
                {
                    if (context.TryOpen())
                    {
                        var hash = this.imageHasher.Hash(context.RgbaImage);
                        context.Hash = hash;
                    }
                    else
                    {
                        // ignore
                        Log.Warning($"Could not calculate hash for {context.Source.FullName}");
                    }

                    return context;
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 8
                });
        }

        public void Dispose()
        {
            this.db?.Dispose();
        }
    }

    public class CustomMetaData
    {
        public double HorizontalResolution { get; set; }
        public string WhiteBalance { get; set; }
        public string Software { get; set; }
        public string SensingMethod2 { get; set; }
        public string SensingMethod { get; set; }
        public string SceneCaptureType { get; set; }
        public string SceneType { get; set; }
        public string SelfTimerMode { get; set; }
        public string Saturation { get; set; }
        public string PixelScale { get; set; }
        public string PixelYDimension { get; set; }
        public string PixelXDimension { get; set; }
        public string Orientation { get; set; }
        public string ISOSpeed { get; set; }
        public string GPSSatellites { get; set; }
        public string GPSDateStamp { get; set; }
        public string GPSTimestamp { get; set; }
        public string GPSLongitude { get; set; }
        public string GPSLatitude { get; set; }
        public string DigitalZoomRatio { get; set; }
        public string FocalLength { get; set; }
        public string Model { get; set; }
        public string Make { get; set; }
        public string LensSerialNumber { get; set; }
        public string LensInfo { get; set; }
        public string LensModel { get; set; }
        public string LensMake { get; set; }
        public string FNumber { get; set; }
        public string ExposureTime { get; set; }
        public string ExposureProgram { get; set; }
        public string ExposureMode { get; set; }
        public string RecommendedExposureIndex { get; set; }
        public string ExposureIndex2 { get; set; }
        public string ExposureIndex { get; set; }
        public string DateTime { get; set; }
        public string DateTimeOriginal { get; set; }
        public string DateTimeDigitized { get; set; }
        public string BrightnessValue { get; set; }
        public string BatteryLevel { get; set; }
        public string AmbientTemperature { get; set; }
        public string ShutterSpeed { get; set; }
        public string Aperture { get; set; }
        public PixelResolutionUnit ResolutionUnits { get; set; }
        public double VerticalResolution { get; set; }
    }
}