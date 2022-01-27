using System.ComponentModel;

namespace SFM.DataImport.Models
{
    /// <summary>
    /// The file types accepted by the migration process
    /// <remarks>This needs to be kept the same as SFM.Server.Core.DataImport.FileType</remarks>
    /// </summary>
    public enum FileType
    {
        [Description("Virksomhets fil")]
        OrganizationFile,
        [Description("Pasients fil")]
        PatientFile,
        [Description("Raport fil")]
        ReportFile,
        [Description("Ukjænt fil")]
        Unknown,
    }
}