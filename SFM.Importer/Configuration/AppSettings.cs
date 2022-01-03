namespace SFM.Importer.Configuration
{
    public class AppSettings
    {
        public string Token { get; internal set; }
        public string Folder { get; set; }
        public string UploadAPI { get; set; }
        public int UploadThreads { get; set; }
    }
}