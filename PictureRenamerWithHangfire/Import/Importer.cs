namespace PictureRenamerWithHangfire.Import
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    using Hangfire;

    using Humanizer;

    using LiteDB;

    using Microsoft.Extensions.Options;

    using Serilog;

    public class Importer
    {
        private const string OutputFastScanResultsName = nameof(ScanOptions.Output) + nameof(FastScanResult);

        private readonly IOptions<ScanOptions> options;

        private readonly IBackgroundJobClient backgroundJobClient;

        private readonly LiteDatabase database;

        private LiteCollection<FastScanResult> fastScanResults;

        public Importer(IOptions<ScanOptions> options, IBackgroundJobClient backgroundJobClient, LiteDatabase database)
        {
            this.options = options;
            this.backgroundJobClient = backgroundJobClient;
            this.database = database;
        }

        public void DoImport()
        {
            var input = new DirectoryInfo(this.options.Value.Input);
            var output = new DirectoryInfo(this.options.Value.Output);

            // step 1: ensure that scan database exists:
            this.EnsureScanDatabase(output);

            // step 2: scan...
        }

        private void EnsureScanDatabase(DirectoryInfo output)
        {
            if (this.database.CollectionExists(OutputFastScanResultsName))
            {
                return;
            }

            // if not, create it:
            this.fastScanResults = this.database.GetCollection<FastScanResult>(OutputFastScanResultsName);
            this.fastScanResults.EnsureIndex(info => info.RelativePath);
            this.fastScanResults.EnsureIndex(info => info.LastWriteTimeUtc);
            this.fastScanResults.EnsureIndex(info => info.CreationTimeUtc);
            this.fastScanResults.EnsureIndex(info => info.Size);
            
            // and scan output first
            var initialScan = GetFastScanResults(output).ToList();

            var stopwatch = Stopwatch.StartNew();
            this.fastScanResults.Insert(initialScan);
            stopwatch.Stop();

            Log.Information($"Found {initialScan.Count}# files in output directory. Added them into the collection within {stopwatch.ElapsedMilliseconds.Milliseconds().Humanize(2)}");
            
            // for each scan result, create a Job to Calculate Hashes:
            foreach (var scanResult in initialScan)
            {
                this.backgroundJobClient.Enqueue<CalculateHashes>(
                    hasher => hasher.CreateDeepScanResult(Location.Output, scanResult));
            }
        }

        private static IEnumerable<FastScanResult> GetFastScanResults(DirectoryInfo directory)
        {
            return directory.EnumerateFiles("*.*", SearchOption.AllDirectories)
                .Select(
                    fi => new FastScanResult
                              {
                                  RelativePath = Path.GetRelativePath(directory.FullName, fi.FullName),
                                  LastWriteTimeUtc = fi.LastWriteTimeUtc,
                                  CreationTimeUtc = fi.CreationTimeUtc,
                                  Size = fi.Length
                              });
        }
    }
}