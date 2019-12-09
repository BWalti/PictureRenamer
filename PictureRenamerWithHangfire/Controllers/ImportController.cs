namespace PictureRenamerWithHangfire.Controllers
{
    using Hangfire;

    using Microsoft.AspNetCore.Mvc;

    using PictureRenamerWithHangfire.Import;

    [Route("api/[controller]")]
    [ApiController]
    public class ImportController : ControllerBase
    {
        private readonly IBackgroundJobClient backgroundJobClient;

        public ImportController(IBackgroundJobClient backgroundJobClient)
        {
            this.backgroundJobClient = backgroundJobClient;
        }

        [HttpPost]
        public void Post()
        {
            this.backgroundJobClient.Enqueue<Importer>(importer => importer.DoImport());
        }
    }
}