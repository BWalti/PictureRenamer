namespace PictureRenamer.Pipelines
{
    using SixLabors.ImageSharp.MetaData;

    public class CustomMetaData
    {
        public double HorizontalResolution { get; set; }
        public string WhiteBalance { get; set; }
        public string Software { get; set; }
        public string SensingMethod2 { get; set; }
        public string SensingMethod { get; set; }
        public string SceneCaptureType { get; set; }
        public string SceneType { get; set; }
        public string SelfTimerMode { get; set; }
        public string Saturation { get; set; }
        public string PixelScale { get; set; }
        public string PixelYDimension { get; set; }
        public string PixelXDimension { get; set; }
        public string Orientation { get; set; }
        public string ISOSpeed { get; set; }
        public string GPSSatellites { get; set; }
        public string GPSDateStamp { get; set; }
        public string GPSTimestamp { get; set; }
        public string GPSLongitude { get; set; }
        public string GPSLatitude { get; set; }
        public string DigitalZoomRatio { get; set; }
        public string FocalLength { get; set; }
        public string Model { get; set; }
        public string Make { get; set; }
        public string LensSerialNumber { get; set; }
        public string LensInfo { get; set; }
        public string LensModel { get; set; }
        public string LensMake { get; set; }
        public string FNumber { get; set; }
        public string ExposureTime { get; set; }
        public string ExposureProgram { get; set; }
        public string ExposureMode { get; set; }
        public string RecommendedExposureIndex { get; set; }
        public string ExposureIndex2 { get; set; }
        public string ExposureIndex { get; set; }
        public string DateTime { get; set; }
        public string DateTimeOriginal { get; set; }
        public string DateTimeDigitized { get; set; }
        public string BrightnessValue { get; set; }
        public string BatteryLevel { get; set; }
        public string AmbientTemperature { get; set; }
        public string ShutterSpeed { get; set; }
        public string Aperture { get; set; }
        public PixelResolutionUnit ResolutionUnits { get; set; }
        public double VerticalResolution { get; set; }
    }
}