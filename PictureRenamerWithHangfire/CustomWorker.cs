namespace PictureRenamerWithHangfire
{
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    public class CustomWorker : IHostedService
    {
        private readonly PathConfig config;

        public CustomWorker(IOptions<PathConfig> config)
        {
            this.config = config.Value;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}