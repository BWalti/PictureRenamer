namespace PictureRenamer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using CoenM.ImageHash.HashAlgorithms;
    using LiteDB;
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
            // var input = @"C:\Users\bwalt\Pictures\DiGa Möbel";
            // var output = @"C:\Users\bwalt\Pictures\DiGa Möbel";
            var input = @"Z:\Import-Queue";
            var output = @"Z:\Processed";
            var recycleBin = @"Z:\Duplicates";

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

            var scanned = outputDirectoryInfo.EnumerateFiles("*.jpg|*.png|*.jpeg|*.mp4").ToList();

            using (var db = new LiteDatabase(@"MyData.db"))
            {
                var mediaItemCollection = db.GetCollection<MediaItemQuickScanInfo>();
                mediaItemCollection.EnsureIndex(info => info.FullName);

                var pipeline = CreatePipeline(PipelineKind.Full, inputDirectoryInfo, outputDirectoryInfo, recycleBinDirectoryInfo, mediaItemCollection);
                return pipeline.Run();
            }
        }

        private static IRunnablePipeline CreatePipeline(PipelineKind kind, DirectoryInfo inputDirectoryInfo, DirectoryInfo outputDirectoryInfo, DirectoryInfo recycleBin, LiteCollection<MediaItemQuickScanInfo> mediaItemCollection)
        {
            switch(kind)
            {
                case PipelineKind.Rename:
                    return new FileRenamerPipeline(inputDirectoryInfo, outputDirectoryInfo);

                case PipelineKind.Duplicate:
                    return new DuplicateMediaItemPipeline(inputDirectoryInfo, outputDirectoryInfo, recycleBin, "*.jpg", new AverageHash());

                case PipelineKind.Full:
                    return new FullMediaItemPipeline(mediaItemCollection, inputDirectoryInfo, outputDirectoryInfo, recycleBin);

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public enum PipelineKind
    {
        Rename,
        Duplicate,
        Full
    }
}