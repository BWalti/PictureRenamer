namespace PictureRenamer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using MetadataExtractor;
    using MetadataExtractor.Formats.Exif;
    using Serilog;

    public class FileRenamerPipeline
    {
        private readonly IPropagatorBlock<ProcessContext, PhotoContext> fileScannerBlock;
        private readonly ActionBlock<PhotoContext> mover;

        private static readonly ExecutionDataflowBlockOptions SingleExecution = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = 1
        };

        private static readonly DataflowLinkOptions DataflowLinkOptions = new DataflowLinkOptions
        {
            PropagateCompletion = true
        };

        public FileRenamerPipeline()
        {
            this.fileScannerBlock = CreateFileScannerBlock();
            var analysis = CreateAnalysisBlock();
            var filter = CreateFilterBlock();
            var suggestion = CreateSuggestionBlock();
            this.mover = CreateMoverAction();

            this.fileScannerBlock.LinkTo(analysis, DataflowLinkOptions);
            analysis.LinkTo(filter, DataflowLinkOptions);
            filter.LinkTo(suggestion, DataflowLinkOptions);
            suggestion.LinkTo(this.mover, DataflowLinkOptions);
        }

        public Task Run(DirectoryInfo input, DirectoryInfo output)
        {
            var processContext = new ProcessContext(input, output);

            this.fileScannerBlock.Post(processContext);
            this.fileScannerBlock.Complete();

            return this.mover.Completion;
        }

        private static ActionBlock<PhotoContext> CreateMoverAction()
        {
            return new ActionBlock<PhotoContext>(context =>
                {
                    var targetFullPath = Path.Combine(context.PossibleTargetPath, context.PossibleTargetFileName);

                    var counter = 1;
                    var extension = Path.GetExtension(context.PossibleTargetFileName);
                    while (File.Exists(targetFullPath))
                    {
                        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(context.PossibleTargetFileName);
                        var formattedCounter = counter.ToString().PadLeft(3, '0');

                        targetFullPath = Path.Combine(
                            context.PossibleTargetPath,
                            $"{fileNameWithoutExtension}-{formattedCounter}{extension}");

                        counter++;
                    }

                    System.IO.Directory.CreateDirectory(context.PossibleTargetPath);

                    Log.Information($"Moving: {context.Source.FullName} to {targetFullPath}");
                    File.Move(context.Source.FullName, targetFullPath);
                },
                SingleExecution);
        }

        private static IPropagatorBlock<PhotoContext, PhotoContext> CreateFilterBlock()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(context =>
            {
                if (context.HasError)
                {
                    var inputFile = context.Source;
                    if ((inputFile.Extension == ".db") || (inputFile.Extension == ".modd") ||
                        (inputFile.Name == "desktop.ini"))
                    {
                        Log.Debug($"Skipping: {context.Source.FullName}");
                        return;
                    }
                }

                output.Post(context);
            });

            input.Completion.ContinueWith(task => { output.Complete(); });

            return DataflowBlock.Encapsulate(input, output);
        }

        private static IPropagatorBlock<ProcessContext, PhotoContext> CreateFileScannerBlock()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<ProcessContext>(context =>
                {
                    var allFiles = context.SourceRoot.EnumerateFiles("*", SearchOption.AllDirectories);

                    foreach (var fileInfo in allFiles)
                    {
                        output.Post(new PhotoContext(fileInfo, context));
                    }
                },
                SingleExecution);

            input.Completion.ContinueWith(task => { output.Complete(); });

            return DataflowBlock.Encapsulate(input, output);
        }

        private static IPropagatorBlock<PhotoContext, PhotoContext> CreateAnalysisBlock()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(context =>
                {
                    using (Stream imageStream = File.OpenRead(context.Source.FullName))
                    {
                        try
                        {
                            var metaDataDirectories = ImageMetadataReader.ReadMetadata(imageStream).ToList();

                            context.ExifIfd0 = metaDataDirectories.OfType<ExifIfd0Directory>().FirstOrDefault();
                            context.ExifSubIfs = metaDataDirectories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                            context.Gps = metaDataDirectories.OfType<GpsDirectory>().FirstOrDefault();

                            output.Post(context);
                        }
                        catch (Exception e)
                        {
                            Log.Error($"Failed extracting metadata from {context.Source.FullName}. Error: {e.Message}");

                            context.Error = e;
                            output.Post(context);
                        }
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 10
                });

            input.Completion.ContinueWith(task => { output.Complete(); });

            return DataflowBlock.Encapsulate(input, output);
        }

        private static IPropagatorBlock<PhotoContext, PhotoContext> CreateSuggestionBlock()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(photoContext =>
            {
                if (photoContext.HasError)
                {
                    if (photoContext.Error is ImageProcessingException)
                    {
                        return;
                    }

                    var sourceCreationTime = photoContext.Source.CreationTime;
                    var targetPath = CreateTargetPath(photoContext, sourceCreationTime);
                    var targetFileName = CreateFileName(sourceCreationTime, "GENERIC", photoContext.Source);

                    photoContext.SetPossibleSolution(targetPath, targetFileName);
                }
                else
                {
                    var model = GetModel(photoContext).FirstOrDefault(s => !string.IsNullOrEmpty(s));
                    var dateTime = GetDateTime(photoContext).FirstOrDefault(s => !string.IsNullOrEmpty(s));

                    var parsedDateTime = DateTime.ParseExact(dateTime, "yyyy:MM:dd HH:mm:ss",
                        Thread.CurrentThread.CurrentCulture);

                    var targetPath = CreateTargetPath(photoContext, parsedDateTime);
                    var targetFilePath = CreateFileName(parsedDateTime, model, photoContext.Source);

                    photoContext.SetPossibleSolution(targetPath, targetFilePath);
                }

                output.Post(photoContext);
            });

            input.Completion.ContinueWith(task => { output.Complete(); });

            return DataflowBlock.Encapsulate(input, output);
        }

        private static IEnumerable<string> GetModel(PhotoContext photoContext)
        {
            yield return photoContext.ExifIfd0?.GetDescription(ExifDirectoryBase.TagModel)?.Trim();

            yield return "GENERIC";
        }

        private static IEnumerable<string> GetDateTime(PhotoContext photoContext)
        {
            yield return photoContext.ExifIfd0?.GetDescription(ExifDirectoryBase.TagDateTime);
            yield return photoContext.ExifIfd0?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);
            yield return photoContext.ExifIfd0?.GetDescription(ExifDirectoryBase.TagDateTimeDigitized);

            yield return photoContext.ExifSubIfs?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);

            if (photoContext.Gps != null)
            {
                yield return photoContext.Gps.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);

                var timeStamp = photoContext.Gps.GetDescription(GpsDirectory.TagTimeStamp);
                var dateStamp = photoContext.Gps.GetDescription(GpsDirectory.TagDateStamp);
                yield return $"{dateStamp} {timeStamp.Substring(0, 8)}";
            }

            yield return photoContext.Source.CreationTime.ToString("yyyy:MM:dd HH:mm:ss");
        }

        private static string CreateTargetPath(PhotoContext photoContext, DateTime dateTime)
        {
            return Path.Combine(
                photoContext.Context.TargetRoot.FullName,
                dateTime.Year.ToString(),
                dateTime.Month.ToString().PadLeft(2, '0'));
        }

        private static string CreateFileName(DateTime parsedDateTime, string model, FileSystemInfo inputFile)
        {
            return $"{parsedDateTime:yyyy-MM-dd HHmmss}-{model}{inputFile.Extension}";
        }
    }
}