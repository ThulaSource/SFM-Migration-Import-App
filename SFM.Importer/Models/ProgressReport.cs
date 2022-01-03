namespace SFM.Importer.Models
{
    public enum UploadStatus
    {
        Ongoing,
        FileStatusChanged,
        Finished,
        Cancelled,
    }
    
    public class UploadReport
    {
        public UploadStatus Status { get; set; }
    }
}