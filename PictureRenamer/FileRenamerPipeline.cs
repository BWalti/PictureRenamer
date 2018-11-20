namespace PictureRenamer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using DupImageLib;
    using MetadataExtractor;
    using MetadataExtractor.Formats.Exif;
    using Serilog;

    public class FileRenamerPipeline
    {
        private static readonly ExecutionDataflowBlockOptions SingleExecution = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = 1
        };

        private static readonly DataflowLinkOptions DataflowLinkOptions = new DataflowLinkOptions
        {
            PropagateCompletion = true
        };

        private readonly ImageHashes imageHasher;

        public FileRenamerPipeline()
        {
            // Create new ImageHashes instance using ImageMackick as image manipulation library
            this.imageHasher = new ImageHashes(new ImageMagickTransformer());
        }

        public Task RunMover(DirectoryInfo input, DirectoryInfo output)
        {
            var fileScannerBlock = CreateFileScannerBlock();
            var analysis = CreateAnalysisBlock();

            var filter = CreateFilterBlock();
            var suggestion = CreateSuggestionBlock();
            var mover = CreateMoverAction();

            fileScannerBlock.LinkTo(analysis, DataflowLinkOptions);
            analysis.LinkTo(filter, DataflowLinkOptions);
            filter.LinkTo(suggestion, DataflowLinkOptions);
            suggestion.LinkTo(mover, DataflowLinkOptions);

            var processContext = new ProcessContext(input, output);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return mover.Completion;
        }

        public Task RunDuplicateScanner(DirectoryInfo input, DirectoryInfo output)
        {
            var fileScannerBlock = CreateFileScannerBlock(false, "*.jpg");
            var analysis = CreateAnalysisBlock();
            var hashCalculator = this.CreateHashCalculator();
            var collectedHashes = this.CollectAll<PhotoContext>();
            var findDuplicates = this.FindExactMatches();
            var processDuplicates = new ActionBlock<Dictionary<ulong, List<PhotoContext>>>(dict =>
            {
                Log.Warning($"Found {dict.Count}# images with duplicates:");
                foreach (var entry in dict)
                {
                    var allImages = string.Join(", ", entry.Value.Select(pc => pc.Source.FullName));
                    Log.Warning($"- {entry.Key}: {allImages}");
                }
            });

            fileScannerBlock.LinkTo(analysis, DataflowLinkOptions);
            analysis.LinkTo(hashCalculator, DataflowLinkOptions);
            hashCalculator.LinkTo(collectedHashes, DataflowLinkOptions);
            collectedHashes.LinkTo(findDuplicates, DataflowLinkOptions);
            findDuplicates.LinkTo(processDuplicates, DataflowLinkOptions);

            var processContext = new ProcessContext(input, output);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return processDuplicates.Completion;
        }

        private IPropagatorBlock<IEnumerable<PhotoContext>, Dictionary<ulong, List<PhotoContext>>> FindExactMatches()
        {
            var output = new BufferBlock<Dictionary<ulong, List<PhotoContext>>>();

            var input = new ActionBlock<IEnumerable<PhotoContext>>(context =>
            {
                var grouped = from entry in context
                    group entry by entry.Hash
                    into g
                    select new {Hash = g.Key, Items = g.ToList()};

                var resultingDictionary = grouped
                    .Where(g => g.Items.Count > 1)
                    .ToDictionary(x => x.Hash, x => x.Items);

                output.Post(resultingDictionary);
            });

            input.Completion.ContinueWith(task => output.Complete());

            return DataflowBlock.Encapsulate(input, output);
        }

        private IPropagatorBlock<T, IEnumerable<T>> CollectAll<T>()
        {
            var all = new List<T>();

            var output = new BufferBlock<IEnumerable<T>>();

            var input = new ActionBlock<T>(context =>
            {
                all.Add(context);
            }, SingleExecution);

            input.Completion.ContinueWith(task =>
            {
                output.Post(all);
                output.Complete();
            });

            return DataflowBlock.Encapsulate(input, output);
        }

        private IPropagatorBlock<PhotoContext, PhotoContext> CreateHashCalculator()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(context =>
                {
                    var hash = this.imageHasher.CalculateAverageHash64(context.Source.FullName);
                    context.Hash = hash;
                    Log.Information($"Hash: {context.Source.Name} = {hash}");
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 8
                });

            input.Completion.ContinueWith(task => output.Complete());

            return DataflowBlock.Encapsulate(input, output);
        }

        private static IPropagatorBlock<PhotoContext, PhotoContext> CreateMoverAction()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(context =>
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

                    context.Target = targetFullPath;
                },
                SingleExecution);

            input.Completion.ContinueWith(task => output.Complete());

            return DataflowBlock.Encapsulate(input, output);
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

        private static IPropagatorBlock<ProcessContext, PhotoContext> CreateFileScannerBlock(bool useInput = true, string filter="*")
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<ProcessContext>(context =>
                {
                    var contextDirectory = useInput 
                        ? context.SourceRoot 
                        : context.TargetRoot;

                    var allFiles = contextDirectory.EnumerateFiles(filter, SearchOption.AllDirectories);

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