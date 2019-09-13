namespace PictureRenamer.Models
{
    using System;

    public class MediaItemMetaTag
    {
        public Guid Id { get; set; }

        public Guid MeidaItemQuickScanInfoId { get; set; }
        public MediaItemQuickScanInfo MediaItemQuickScanInfo { get; set; }

        public int? TagId { get; set; }

        public string TagValue { get; set; }
    }
}