using SFM.DataImport.Models;
using SFM.Importer.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SFM.Importer.Models
{
    public enum ImportStatus
    {
        [Display(Name = "Ikke begynnt")]
        NotStarted,
        [Display(Name = "Opplasting pågår")]
        Ongoing,
        [Display(Name = "Opplasting ferdig")]
        Completed,
        [Display(Name = "Oplasting feilet")]
        Failed,
    }

    public class ImportFile
    {
        /// <summary>
        /// The name of the file from the FM export with relative path
        /// </summary>
        public string FileNameWithRelativePath { get; set; }

        /// <summary>
        /// The name of the file from the FM export
        /// </summary>
        public string FileName => FileNameWithRelativePath?.Contains("\\") == true
            ? FileNameWithRelativePath.Substring(FileNameWithRelativePath.LastIndexOf('\\') + 1)
            : FileNameWithRelativePath;

        /// <summary>
        /// What type of file is this. The type decides the priority
        /// Used by the GUI.
        /// </summary>
        public FileType FileType { get; set; }

        // Used by the GUI.
        [JsonIgnore]
        public string FileTypeAsString { get => EnumHelper<FileType>.GetDescriptionValue(FileType); }

        /// <summary>
        /// The current status of the upload.
        /// </summary>
        public ImportStatus Status { get; set; }

        // Used by the GUI.
        [JsonIgnore]
        public string StatusAsString { get => EnumHelper<ImportStatus>.GetDisplayValue(Status); }

        /// <summary>
        /// The URL of the temporary file in SFM. This is used to resume interrupted uploads 
        /// </summary>
        public string RemoteUrl { get; set; }

        /// <summary>
        /// When the file was added to the ledger
        /// </summary>
        public DateTime WhenAddedToLedger { get; set; }

        /// <summary>
        /// When the entry was last changed 
        /// </summary>
        public DateTime WhenModified { get; set; }

        /// <summary>
        /// Add a file. This also determines the type of file from the name
        /// </summary>
        /// <param name="file"></param>
        /// <returns>The current instance</returns>
        public ImportFile AddFile(string file)
        {
            FileNameWithRelativePath = file;

            WhenAddedToLedger = DateTime.Now;

            if (file == Constants.OrganizationFilename)
            {
                FileType = FileType.OrganizationFile;
            }
            else if (file.StartsWith(Constants.PatientFolder))
            {
                FileType = FileType.PatientFile;
            }
            else if (file.StartsWith(Constants.ReportFolder))
            {
                FileType = FileType.ReportFile;
            }
            else
            {
                FileType = FileType.Unknown;
            }

            WasModified();
            return this;
        }

        /// <summary>
        /// Updates the when modified timestamp.
        /// </summary>
        public void WasModified()
        {
            WhenModified = DateTime.Now;
        }
    }

    // TODO: Use INotifyPropertyChanged to update the GridView
    public class ImportLedger
    {
        /// <summary>
        /// The ledger is a list of import files and their status
        /// </summary>
        public ObservableCollection<ImportFile> importFiles { get; set; }

        /// <summary>
        /// The import session
        /// </summary>
        public Guid ImportSession { get; private set; }
        public ImportLedger()
        {
            importFiles = new ObservableCollection<ImportFile>();
            ImportSession = Guid.NewGuid();
        }
    }
}
