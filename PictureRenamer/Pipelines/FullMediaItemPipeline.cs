namespace PictureRenamer.Pipelines
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using CoenM.ImageHash;
    using CoenM.ImageHash.HashAlgorithms;
    using LiteDB;
    using Serilog;

    public class FullMediaItemPipeline : IRunnablePipeline
    {
        private static readonly DataflowLinkOptions DataflowLinkOptions =
            new DataflowLinkOptions {PropagateCompletion = true};

        private readonly IImageHash imageHasher;
        private readonly DirectoryInfo inputDirectoryInfo;
        private readonly LiteCollection<MediaItemQuickScanInfo> mediaItemCollection;
        private readonly DirectoryInfo outputDirectoryInfo;
        private readonly DirectoryInfo recycleBin;

        public FullMediaItemPipeline(LiteCollection<MediaItemQuickScanInfo> mediaItemCollection,
            DirectoryInfo inputDirectoryInfo, DirectoryInfo outputDirectoryInfo, DirectoryInfo recycleBin)
        {
            this.mediaItemCollection = mediaItemCollection;
            this.inputDirectoryInfo = inputDirectoryInfo;
            this.outputDirectoryInfo = outputDirectoryInfo;
            this.recycleBin = recycleBin;
            this.imageHasher = new AverageHash();
        }

        /// <summary>
        ///     Move all from Input-Directory to Output-Directory, well named.
        ///     Then for each new item, invoke "duplicate" check. For that, hash needs to be calculated first
        /// </summary>
        /// <returns></returns>
        public Task Run()
        {
            var fileScannerBlock = BlockCreator.CreateFileScannerBlock();
            var analysis = BlockCreator.CreateAnalysisBlock();

            var filter = BlockCreator.CreateFilterBlock();
            var suggestion = BlockCreator.CreateSuggestionBlock();
            var mover = BlockCreator.CreateMoverAction();

            fileScannerBlock.LinkTo(analysis, DataflowLinkOptions);
            analysis.LinkTo(filter, DataflowLinkOptions);
            filter.LinkTo(suggestion, DataflowLinkOptions);
            suggestion.LinkTo(mover, DataflowLinkOptions);

            var processContext = new ProcessContext(this.inputDirectoryInfo, this.outputDirectoryInfo);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return mover.Completion;
        }

        public Task RunDuplicateScanner(DirectoryInfo input, DirectoryInfo output, string recycleBin, string fileFilter)
        {
            var fileScannerBlock = BlockCreator.CreateFileScannerBlock(false, fileFilter);

            //var analysis = CreateAnalysisBlock();
            var hashCalculator = BlockCreator.CreateHashCalculator(this.imageHasher);
            var collectedHashes = BlockCreator.CollectAll<PhotoContext>();
            var findDuplicates = BlockCreator.FindExactMatches();
            var processDuplicates = new ActionBlock<Dictionary<ulong, List<PhotoContext>>>(
                dict =>
                {
                    Log.Warning($"Found {dict.Count}# images with duplicates:");
                    foreach (var entry in dict)
                    {
                        var fullNames = entry.Value.Select(pc => pc.Source.FullName).OrderBy(fn => fn).ToList();

                        var allImages = string.Join(", ", fullNames);
                        Log.Warning($"- {entry.Key}: {allImages}");

                        foreach (var fn in fullNames.Skip(1))
                        {
                            var target = Path.Combine(recycleBin, Path.GetFileName(fn));
                            File.Move(fn, target);
                        }
                    }
                });

            fileScannerBlock.LinkTo(hashCalculator, DataflowLinkOptions);

            //analysis.LinkTo(hashCalculator, DataflowLinkOptions);
            hashCalculator.LinkTo(collectedHashes, DataflowLinkOptions);
            collectedHashes.LinkTo(findDuplicates, DataflowLinkOptions);
            findDuplicates.LinkTo(processDuplicates, DataflowLinkOptions);

            var processContext = new ProcessContext(input, output);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return processDuplicates.Completion;
        }
    }
}