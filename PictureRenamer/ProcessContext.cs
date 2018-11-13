namespace PictureRenamer
{
    using System.IO;

    public class ProcessContext
    {
        public ProcessContext(DirectoryInfo sourceRoot, DirectoryInfo targetRoot)
        {
            this.SourceRoot = sourceRoot;
            this.TargetRoot = targetRoot;
        }

        public DirectoryInfo SourceRoot { get; }

        public DirectoryInfo TargetRoot { get; }
    }
}