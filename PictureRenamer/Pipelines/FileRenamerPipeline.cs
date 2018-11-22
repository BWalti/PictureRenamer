namespace PictureRenamer.Pipelines
{
    using System.IO;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using CoenM.ImageHash;
    using CoenM.ImageHash.HashAlgorithms;

    public class FileRenamerPipeline : IRunnablePipeline
    {
        private static readonly DataflowLinkOptions DataflowLinkOptions =
            new DataflowLinkOptions {PropagateCompletion = true};

        private readonly IImageHash imageHasher;
        private readonly DirectoryInfo inputDirectoryInfo;
        private readonly DirectoryInfo outputDirectoryInfo;

        public FileRenamerPipeline(DirectoryInfo inputDirectoryInfo, DirectoryInfo outputDirectoryInfo)
        {
            this.inputDirectoryInfo = inputDirectoryInfo;
            this.outputDirectoryInfo = outputDirectoryInfo;
            this.imageHasher = new AverageHash();
        }

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
    }
}