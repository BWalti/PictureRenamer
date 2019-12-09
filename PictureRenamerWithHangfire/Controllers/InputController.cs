namespace PictureRenamerWithHangfire.Controllers
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;

    using PictureRenamerWithHangfire.Import;

    [Route("api/[controller]")]
    [ApiController]
    public class InputController : ControllerBase
    {
        private readonly IOptions<ScanOptions> options;

        public InputController(IOptions<ScanOptions> options)
        {
            this.options = options;
        }

        [HttpGet]
        public IEnumerable<FastScanResult> Get()
        {
            var di = new DirectoryInfo(this.options.Value.Input);

            return di.EnumerateFiles("*.*", SearchOption.AllDirectories)
                .Select(fi => new FastScanResult
                                  {
                                      RelativePath = Path.GetRelativePath(di.FullName, fi.FullName),
                                      LastWriteTimeUtc = fi.LastWriteTimeUtc,
                                      CreationTimeUtc = fi.CreationTimeUtc,
                                      Size = fi.Length
                                  });
        }
    }
}