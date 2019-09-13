namespace PictureRenamer
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using CoenM.ImageHash.HashAlgorithms;
    using Microsoft.Extensions.Configuration;
    using PictureRenamer.Pipelines;
    using Serilog;

    internal class Program
    {
        private static void Main(string[] args)
        {
            // Read the application settings file containing the Serilog configuration.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", false, false)
                .Build();

            // Setting up the static Serilog logger.
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            Run().Wait();

            Log.CloseAndFlush();
        }

        private static Task Run()
        {
            var input = @"Y:\Import-Queue";
            var output = @"Y:\Processed";
            var recycleBin = @"Y:\Duplicates";

            //var input = @"D:\PicRenameSpielwiese\Input";
            //var output = @"D:\PicRenameSpielwiese\Processed";
            //var recycleBin = @"D:\PicRenameSpielwiese\Recycle";

            var inputDirectoryInfo = new DirectoryInfo(input);
            var outputDirectoryInfo = new DirectoryInfo(output);
            var recycleBinDirectoryInfo = new DirectoryInfo(recycleBin);

            if (!inputDirectoryInfo.Exists)
            {
                throw new ArgumentException("Input directory does not exist!", nameof(input));
            }

            if (!outputDirectoryInfo.Exists)
            {
                throw new ArgumentException("Output directory does not exist!", nameof(output));
            }

            if (!recycleBinDirectoryInfo.Exists)
            {
                throw new ArgumentException("RecycleBin directory does not exist!", nameof(output));
            }

            // Step 1: Scan target directory for changes, update meta & hashes (mark data of non-existent files as "deleted", so that duplicates do not come in again?)
            // Step 2: Scan input directory for new files, calc meta & hash
            // Step 3: if collision, move to duplicates, if no collision, move to target
            var pipeline = CreatePipeline(PipelineKind.Full, inputDirectoryInfo, outputDirectoryInfo, recycleBinDirectoryInfo);
            return pipeline.Run();
        }
        
        private static IRunnablePipeline CreatePipeline(PipelineKind kind, DirectoryInfo inputDirectoryInfo, DirectoryInfo outputDirectoryInfo, DirectoryInfo recycleBin)
        {
            switch(kind)
            {
                case PipelineKind.Rename:
                    return new FileRenamerPipeline(inputDirectoryInfo, outputDirectoryInfo);

                case PipelineKind.Duplicate:
                    return new DuplicateMediaItemPipeline(inputDirectoryInfo, outputDirectoryInfo, recycleBin, new AverageHash());

                case PipelineKind.Full:
                    return new FullMediaItemPipeline(inputDirectoryInfo, outputDirectoryInfo, recycleBin);

                case PipelineKind.TimeStampMissmatch:
                    return new ScanForTimeStampMissmatchPipeline(outputDirectoryInfo);
                    
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public enum PipelineKind
    {
        Rename,
        Duplicate,
        Full,
        TimeStampMissmatch
    }
}