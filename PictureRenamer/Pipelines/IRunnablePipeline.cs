namespace PictureRenamer.Pipelines
{
    using System.Threading.Tasks;

    public interface IRunnablePipeline
    {
        Task Run();
    }
}