namespace PictureRenamer
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Configuration;
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
            var input = @"Z:\Import-Queue";
            var output = @"Z:\Processed";

            var inputDirectoryInfo = new DirectoryInfo(input);
            var outputDirectoryInfo = new DirectoryInfo(output);

            if (!inputDirectoryInfo.Exists)
            {
                throw new ArgumentException("Input directory does not exist!", nameof(input));
            }

            if (!outputDirectoryInfo.Exists)
            {
                throw new ArgumentException("Output directory does not exist!", nameof(output));
            }

            var pipeline = new FileRenamerPipeline();
            //return pipeline.RunMover(inputDirectoryInfo, outputDirectoryInfo);
            return pipeline.RunDuplicateScanner(inputDirectoryInfo, outputDirectoryInfo);
        }
    }
}