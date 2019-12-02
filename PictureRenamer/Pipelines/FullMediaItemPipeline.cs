namespace PictureRenamer.Pipelines
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using CoenM.ImageHash;
    using CoenM.ImageHash.HashAlgorithms;
    using LiteDB;

    using PictureRenamer.Models;
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
            this.ScanTarget();

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

            var databaseDisposed = registerMetadata.Completion.ContinueWith(task =>
            {
                Log.Information("Processing of new pictures completed..");
                this.db.Dispose();
            });

            var processContext = new ProcessContext(this.inputDirectoryInfo, this.outputDirectoryInfo);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return Task.WhenAll(
                databaseDisposed, 
                finish.Completion);
        }
        
        private void ScanTarget()
        {
            // gets all files:
            var allFiles = this.outputDirectoryInfo
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(BlockCreator.MediaFileFilter)
                .ToList();

            var allItems = this.mediaItemCollection.FindAll().ToList();

            var crossOuterJoinResult = allFiles.FullOuterJoin(
                allItems,
                info => info.FullName,
                info => info.FullName,
                (info, scanInfo) => new { Key = info?.FullName ?? scanInfo?.FullName, File = info, Item = scanInfo }).ToList();
            
            foreach (var element in crossOuterJoinResult)
            {
                if (element.File == null && !element.Item.Deleted)
                {
                    // has been removed, mark meta as deleted:
                    element.Item.Deleted = true;
                    this.mediaItemCollection.Update(element.Item);
                }
                else if (element.Item == null)
                {
                    // new file!
                    var context = new PhotoContext(element.File, null);
                    context.TryOpen();
                    if (context.RgbaImage != null)
                    {
                        context.Hash = this.imageHasher.Hash(context.RgbaImage);
                        var newItem = this.ConvertToMediaItemQuickScanInfoBeforeMove(context);

                        this.mediaItemCollection.Insert(newItem);
                        context.Dispose();
                    }
                } 
                else if (element.File != null)
                {
                    if (!element.File.LastWriteTime.Equals(element.Item.LastWriteTimeUtc))
                    {
                        // existing file, has changed
                        // update hash and meta data:
                        var context = new PhotoContext(element.File, null);
                        context.TryOpen();
                        context.Hash = this.imageHasher.Hash(context.RgbaImage);

                        var newItem = this.ConvertToMediaItemQuickScanInfoBeforeMove(context);

                        element.Item.Hash = newItem.Hash;
                        element.Item.MetaData = newItem.MetaData;

                        this.mediaItemCollection.Update(element.Item);
                        context.Dispose();
                    }
                }
            }
        }

        //private ITargetBlock<PhotoContext> EnsureMetaData(IPropagatorBlock<PhotoContext, PhotoContext> hasherBlock, IPropagatorBlock<PhotoContext, PhotoContext> analysisBlock, ITargetBlock<PhotoContext> continueWith)
        //{
        //    var input = new ActionBlock<PhotoContext>(context =>
        //    {
        //        var existing = this.mediaItemCollection.FindOne(item  => item.FullName == context.Source.FullName);
        //        if (existing != null)
        //        {
        //            // if we did not yet calculate a hash on the context, and there is none saved OR the last modify date changed,
        //            // we need to calculate the hash and update the meta data (second run)
        //            if (!context.Hash.HasValue && (existing.Hash == null || existing.LastWriteTimeUtc != context.Source.LastWriteTimeUtc))
        //            {
        //                Log.Information($"Updating hash for {context.Source.FullName}...");
        //                hasherBlock.Post(context);
        //            } 
        //            else if (context.Hash.HasValue)
        //            {
        //                existing.Hash = context.Hash;
        //                this.mediaItemCollection.Update(existing);
        //            }
        //            else if (context.MetaData == null && (existing.MetaData == null ||
        //                                                  existing.LastWriteTimeUtc != context.Source.LastWriteTimeUtc))
        //            {
        //                Log.Information($"Updating meta data for {context.Source.FullName}...");
        //                analysisBlock.Post(context);
        //            }
        //            else if (context.MetaData != null)
        //            {
        //                existing.MetaData = this.ConvertMetaData(context.MetaData);
        //                this.mediaItemCollection.Update(existing);
        //            }
        //            else
        //            {
        //                continueWith.Post(context);
        //            }
        //        }
        //    });

        //    input.Completion.ContinueWith(task =>
        //    {
        //        hasherBlock.Complete();
        //        analysisBlock.Complete();
        //        continueWith.Complete();
        //    });

        //    return input;
        //}

        //private IPropagatorBlock<DirectoryInfo, PhotoContext> CreateScannerBlock()
        //{
        //    var output = new BufferBlock<PhotoContext>();

        //    var input = new ActionBlock<DirectoryInfo>(info =>
        //    {
        //        var allFiles = info
        //            .EnumerateFiles("*", SearchOption.AllDirectories)
        //            .Where(BlockCreator.MediaFileFilter);

        //        foreach (var fileInfo in allFiles)
        //        {
        //            output.Post(new PhotoContext(fileInfo, null));
        //        }
        //    });

        //    input.Completion.ContinueWith(task => output.Complete());

        //    return DataflowBlock.Encapsulate(input, output);
        //}

        private ITargetBlock<PhotoContext[]> CreateRegisterMetadataBlock()
        {
            return new ActionBlock<PhotoContext[]>(contexts =>
            {
                var converted = contexts.Where(c => !c.HasError).Select(this.ConvertToMediaItemQuickScanInfoAfterMove);

                var insertBulk = this.mediaItemCollection.InsertBulk(converted);
                Log.Information($"Inserted {insertBulk}# image media into db");
            });
        }

        private MediaItemQuickScanInfo ConvertToMediaItemQuickScanInfoAfterMove(PhotoContext context)
        {
            return new MediaItemQuickScanInfo
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
            };
        }

        private MediaItemQuickScanInfo ConvertToMediaItemQuickScanInfoBeforeMove(PhotoContext context)
        {
            return new MediaItemQuickScanInfo
            {
                FullName = context.Source.FullName,
                LastWriteTimeUtc = context.Source.LastWriteTimeUtc,
                CreationTimeUtc = context.Source.CreationTimeUtc,
                Hash = context.Hash,
                Length = context.Source.Length,
                Name = context.Source.Name,
                MetaData = this.ConvertMetaData(context.MetaData)
                //ExifValues = context.ExifValues,
                //ImageProperties = context.ImageProperties
            };
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
}