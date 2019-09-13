namespace PictureRenamer.Pipelines
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using CoenM.ImageHash;
    using Serilog;

    public class DuplicateMediaItemPipeline : IRunnablePipeline
    {
        private static readonly DataflowLinkOptions DataflowLinkOptions =
            new DataflowLinkOptions {PropagateCompletion = true};

        private static readonly ExecutionDataflowBlockOptions SingleExecution =
            new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 1};

        private readonly IImageHash imageHasher;
        private readonly DirectoryInfo inputDirectoryInfo;
        private readonly DirectoryInfo outputDirectoryInfo;
        private readonly DirectoryInfo recycleBin;

        public DuplicateMediaItemPipeline(DirectoryInfo inputDirectoryInfo, DirectoryInfo outputDirectoryInfo,
            DirectoryInfo recycleBin, IImageHash imageHasher)
        {
            this.inputDirectoryInfo = inputDirectoryInfo;
            this.outputDirectoryInfo = outputDirectoryInfo;
            this.recycleBin = recycleBin;
            this.imageHasher = imageHasher;
        }

        public Task Run()
        {
            var fileScannerBlock = BlockCreator.CreateFileScannerBlock(false);

            //var analysis = ReadImageMetadataBlock();
            var hashCalculator = BlockCreator.CreateHashCalculator(this.imageHasher);
            var collectedHashes = BlockCreator.CollectAll<PhotoContext>();
            var findDuplicates = BlockCreator.FindExactMatches();
            var processDuplicates = new ActionBlock<Dictionary<ulong?, List<PhotoContext>>>(
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
                            var target = Path.Combine(this.recycleBin.FullName, Path.GetFileName(fn));
                            File.Move(fn, target);
                        }
                    }
                });

            fileScannerBlock.LinkTo(hashCalculator, DataflowLinkOptions);

            hashCalculator.LinkTo(collectedHashes, DataflowLinkOptions);
            collectedHashes.LinkTo(findDuplicates, DataflowLinkOptions);
            findDuplicates.LinkTo(processDuplicates, DataflowLinkOptions);

            var processContext = new ProcessContext(this.inputDirectoryInfo, this.outputDirectoryInfo);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return processDuplicates.Completion;
        }
    }
}