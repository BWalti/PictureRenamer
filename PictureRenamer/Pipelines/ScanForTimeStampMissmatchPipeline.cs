namespace PictureRenamer.Pipelines
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using Nest;
    using Serilog;

    public class ScanForTimeStampMissmatchPipeline : IRunnablePipeline
    {
        private static readonly DataflowLinkOptions DataflowLinkOptions =
            new DataflowLinkOptions {PropagateCompletion = true};

        private readonly DirectoryInfo scanDirectoryInfo;
        private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
        private ElasticClient elasticClient;

        public ScanForTimeStampMissmatchPipeline(DirectoryInfo scanDirectoryInfo)
        {
            this.scanDirectoryInfo = scanDirectoryInfo;

            var node = new Uri("http://192.168.0.11:30489/");
            var settings = new ConnectionSettings(node)
                .DefaultIndex("image-container")
                .DefaultMappingFor<ImageInformationContainer>(m =>
                    m.IdProperty(nameof(ImageInformationContainer.Id)));
            this.elasticClient = new ElasticClient(settings);
            
        }

        public Task Run()
        {
            var fileScannerBlock = BlockCreator.CreateFileScannerBlock();
            var analysis = BlockCreator.ReadImageMetadataBlock();
            var filter = BlockCreator.CreateFilterBlock();
            var missmatch = new ActionBlock<PhotoContext>(context =>
            {
                var imageContainer = new ImageInformationContainer
                {
                    Id = context.Source.FullName,
                    ImageMetaData = context.MetaData,
                    FileSystemProperties = new FileSystemProperties
                    {
                        LastWriteTimeUtc = context.Source.LastWriteTimeUtc,
                        CreationTimeUtc = context.Source.CreationTimeUtc,
                        LastAccessTimeUtc = context.Source.LastAccessTimeUtc,
                        Length = context.Source.Length
                    }
                };
                var response = this.elasticClient.Index(imageContainer, idx => idx.Index("image-container"));

                var lastWriteTime = context.Source.LastWriteTime;
                var metaDateTime = BlockCreator.GetDateTime(context)
                    .FirstOrDefault(s => !string.IsNullOrEmpty(s));

                var parsedDateTime = DateTime.ParseExact(
                    metaDateTime,
                    "yyyy:MM:dd HH:mm:ss",
                    Thread.CurrentThread.CurrentCulture);

                if (TimeSpan.FromTicks(Math.Abs(lastWriteTime.Ticks - parsedDateTime.Ticks)) > FourHours)
                {
                    Log.Information($"${context.Source.FullName}: {lastWriteTime} / {parsedDateTime}");
                }
            });

            fileScannerBlock.LinkTo(analysis, DataflowLinkOptions);
            analysis.LinkTo(filter, DataflowLinkOptions);
            filter.LinkTo(missmatch, DataflowLinkOptions);

            var processContext = new ProcessContext(this.scanDirectoryInfo, null);
            fileScannerBlock.Post(processContext);
            fileScannerBlock.Complete();

            return missmatch.Completion;
        }
    }
}