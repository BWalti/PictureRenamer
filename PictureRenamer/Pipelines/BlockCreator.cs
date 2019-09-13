namespace PictureRenamer.Pipelines
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks.Dataflow;
    using CoenM.ImageHash;
    using Serilog;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.MetaData.Profiles.Exif;
    using Directory = System.IO.Directory;

    public class BlockCreator
    {
        private static readonly DataflowLinkOptions DataflowLinkOptions =
            new DataflowLinkOptions {PropagateCompletion = true};

        private static readonly ExecutionDataflowBlockOptions SingleExecution =
            new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 1};

        public static IPropagatorBlock<T, IEnumerable<T>> CollectAll<T>()
        {
            var all = new List<T>();

            var output = new BufferBlock<IEnumerable<T>>();

            var input = new ActionBlock<T>(context => { all.Add(context); }, SingleExecution);

            input.Completion.ContinueWith(
                task =>
                {
                    output.Post(all);
                    output.Complete();
                });

            return DataflowBlock.Encapsulate(input, output);
        }

        public static IPropagatorBlock<PhotoContext, PhotoContext> ReadImageMetadataBlock()
        {
            var transformed = new TransformBlock<PhotoContext, PhotoContext>(context =>
            {
                if (!context.TryOpen())
                {
                    context.Error = new Exception("Could not open image...");
                }

                return context;
            });

            return transformed;
        }

        public static IPropagatorBlock<PhotoContext, PhotoContext> CreateHashCalculator(IImageHash imageHasher)
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(
                context =>
                {
                    try
                    {
                        context.Hash = CalculateHash(imageHasher, context.Source.FullName);
                        Log.Information($"Hash: {context.Source.Name} = {context.Hash}");
                        output.Post(context);
                    }
                    catch (Exception)
                    {
                        // ignore for the moment: 80/20
                    }
                },
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 8});

            input.Completion.ContinueWith(task => output.Complete());

            return DataflowBlock.Encapsulate(input, output);
        }

        public static ulong CalculateHash(IImageHash imageHasher, string fullName)
        {
            using (var stream = File.OpenRead(fullName))
            using (var image = Image.Load(stream))
            {
                return imageHasher.Hash(image);
            }
        }

        public static IPropagatorBlock<IEnumerable<PhotoContext>, Dictionary<ulong?, List<PhotoContext>>> FindExactMatches()
        {
            var output = new BufferBlock<Dictionary<ulong?, List<PhotoContext>>>();

            var input = new ActionBlock<IEnumerable<PhotoContext>>(
                context =>
                {
                    var grouped = from entry in context
                        group entry by entry.Hash
                        into g
                        select new {Hash = g.Key, Items = g.ToList()};

                    var resultingDictionary = grouped.Where(g => g.Items.Count > 1)
                        .ToDictionary(x => x.Hash, x => x.Items);

                    output.Post(resultingDictionary);
                });

            input.Completion.ContinueWith(task => output.Complete());

            return DataflowBlock.Encapsulate(input, output);
        }

        public static IPropagatorBlock<PhotoContext, PhotoContext> CreateFilterBlock()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(
                context =>
                {
                    if (context.HasError)
                    {
                        var inputFile = context.Source;
                        if ((inputFile.Extension == ".db") || (inputFile.Extension == ".modd")
                                                           || (inputFile.Name == "desktop.ini"))
                        {
                            Log.Debug($"Skipping: {context.Source.FullName}");
                            return;
                        }

                        Log.Warning($"context HasError, but is not a skipped type: {context.Source.FullName}");
                        return;
                    }

                    output.Post(context);
                });

            input.Completion.ContinueWith(task => { output.Complete(); });

            return DataflowBlock.Encapsulate(input, output);
        }

        private static readonly Regex MediaFileNamePattern = new Regex(@"\.(jpg|png|jpeg|thm|orf|psd|arw|cr2|mov|mp4)", RegexOptions.IgnoreCase);

        public static IPropagatorBlock<ProcessContext, PhotoContext> CreateFileScannerBlock(bool useInput = true)
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<ProcessContext>(
                context =>
                {
                    var contextDirectory = useInput ? context.SourceRoot : context.TargetRoot;

                    var allFiles = contextDirectory
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Where(MediaFileFilter);

                    foreach (var fileInfo in allFiles)
                    {
                        output.Post(new PhotoContext(fileInfo, context));
                    }
                },
                SingleExecution);

            input.Completion.ContinueWith(task => { output.Complete(); });

            return DataflowBlock.Encapsulate(input, output);
        }

        public static bool MediaFileFilter(FileInfo info)
        {
            return MediaFileNamePattern.IsMatch(info.Extension);
        }

        public static IPropagatorBlock<PhotoContext, PhotoContext> CreateMoverAction(string alternativeTargetPath = null)
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(
                context =>
                {
                    var targetPath = alternativeTargetPath ?? context.PossibleTargetPath;

                    var targetFullPath = Path.Combine(targetPath, context.PossibleTargetFileName ?? context.Source.Name);

                    var counter = 1;
                    var extension = Path.GetExtension(context.PossibleTargetFileName);
                    while (File.Exists(targetFullPath))
                    {
                        var fileNameWithoutExtension =
                            Path.GetFileNameWithoutExtension(context.PossibleTargetFileName);
                        var formattedCounter = counter.ToString().PadLeft(3, '0');

                        targetFullPath = Path.Combine(
                            targetPath,
                            $"{fileNameWithoutExtension}-{formattedCounter}{extension}");

                        counter++;
                    }

                    Directory.CreateDirectory(targetPath);

                    Log.Information($"Moving: {context.Source.FullName} to {targetFullPath}");
                    context.EnsureClosed();
                    File.Move(context.Source.FullName, targetFullPath);

                    context.Target = new FileInfo(targetFullPath);
                    output.Post(context);
                },
                SingleExecution);

            input.Completion.ContinueWith(task => output.Complete());

            return DataflowBlock.Encapsulate(input, output);
        }

        public static IPropagatorBlock<PhotoContext, PhotoContext> CreateSuggestionBlock()
        {
            var output = new BufferBlock<PhotoContext>();

            var input = new ActionBlock<PhotoContext>(
                photoContext =>
                {
                    if (photoContext.HasError)
                    {
                        if (photoContext.Error is ImageProcessingException)
                        {
                            return;
                        }

                        var dateTime = photoContext.Source.CreationTime <= photoContext.Source.LastWriteTime 
                            ? photoContext.Source.CreationTime 
                            : photoContext.Source.LastWriteTime;

                        var targetPath = CreateTargetPath(photoContext, dateTime);
                        var targetFileName = CreateFileName(dateTime, "GENERIC", photoContext.Source);

                        photoContext.SetPossibleSolution(targetPath, targetFileName);
                    }
                    else
                    {
                        var camera = GetCameraDescription(photoContext);
                        var dateTime = GetDateTime(photoContext).FirstOrDefault(s => !string.IsNullOrEmpty(s) && s != "0000:00:00 00:00:00");

                        var parsedDateTime = DateTime.ParseExact(
                            dateTime,
                            "yyyy:MM:dd HH:mm:ss",
                            Thread.CurrentThread.CurrentCulture);

                        var targetPath = CreateTargetPath(photoContext, parsedDateTime);
                        var targetFilePath = CreateFileName(parsedDateTime, camera, photoContext.Source);

                        photoContext.SetPossibleSolution(targetPath, targetFilePath);
                    }

                    output.Post(photoContext);
                });

            input.Completion.ContinueWith(task => { output.Complete(); });

            return DataflowBlock.Encapsulate(input, output);
        }

        public static IEnumerable<string> GetDateTime(PhotoContext photoContext)
        {
            yield return photoContext.RgbaImage?.MetaData?.ExifProfile?.GetValue(ExifTag.DateTimeOriginal)?.Value?.ToString();
            yield return photoContext.RgbaImage?.MetaData?.ExifProfile?.GetValue(ExifTag.DateTimeDigitized)?.Value?.ToString();
            yield return photoContext.RgbaImage?.MetaData?.ExifProfile?.GetValue(ExifTag.DateTime)?.Value?.ToString();

            var dateStamp = photoContext.RgbaImage?.MetaData?.ExifProfile?.GetValue(ExifTag.GPSDateStamp)?.Value?.ToString();
            var timeStamp = photoContext.RgbaImage?.MetaData?.ExifProfile?.GetValue(ExifTag.GPSTimestamp)?.Value?.ToString();
            if (!string.IsNullOrEmpty(dateStamp) && !string.IsNullOrEmpty(timeStamp))
            {
                yield return $"{dateStamp} {timeStamp.Substring(0, 8)}";
            }

            yield return photoContext.Source.LastWriteTimeUtc.ToString("yyyy:MM:dd HH:mm:ss");
        }

        private static string CreateFileName(DateTime parsedDateTime, string model, FileSystemInfo inputFile)
        {
            return $"{parsedDateTime:yyyy-MM-dd HHmmss}-{model}{inputFile.Extension}";
        }

        private static string CreateTargetPath(PhotoContext photoContext, DateTime dateTime)
        {
            return Path.Combine(
                photoContext.Context.TargetRoot.FullName,
                dateTime.Year.ToString(),
                dateTime.Month.ToString().PadLeft(2, '0'));
        }

        private static string GetCameraDescription(PhotoContext photoContext)
        {
            var make = GetMake(photoContext).FirstOrDefault();
            var model = GetModel(photoContext).FirstOrDefault();

            var parts = new []{make, model}.Where(i => i != null).Select(s => s.Trim());
            return string.Join("-", parts);
        }

        private static IEnumerable<string> GetMake(PhotoContext photoContext)
        {
            yield return photoContext.RgbaImage?.MetaData?.ExifProfile?.GetValue(ExifTag.Make)?.Value?.ToString();
        }

        private static IEnumerable<string> GetModel(PhotoContext photoContext)
        {
            yield return photoContext.RgbaImage?.MetaData?.ExifProfile?.GetValue(ExifTag.Model)?.Value?.ToString();

            yield return "GENERIC";
        }
    }
}