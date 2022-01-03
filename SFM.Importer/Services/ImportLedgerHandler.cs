using System.Collections.Generic;
using SFM.Importer.Models;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SFM.DataImport.Models;

namespace SFM.Importer.Services
{
    public class ImportLedgerHandler
    {
        private string folder;
        private string LedgerFilename { get => Path.Combine(folder, Constants.LedgerFilename); }
        
        private readonly List<string> ValidExtensions = new List<string> {".dat", ".pat", ".rpt"}; 

        public ImportLedger Ledger { get; internal set; }

        public IEnumerable<ImportFile> LedgerView { get => Ledger.importFiles; }

        /// <summary>
        /// Load the class from file if it exists and verify its contents against the index file.
        /// If a file from the index file is missing then add it
        /// </summary>
        /// <param name="folder">The folder being worked on</param>
        public void Initialize(string folder)
        {
            this.folder = folder;

            // If there exists a ledger file then load it, otherwise create a new one
            if (File.Exists(LedgerFilename))
            {
                var options = new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                };
                var ledgerJson = File.ReadAllText(LedgerFilename);
                Ledger = JsonSerializer.Deserialize<ImportLedger>(ledgerJson, options);
            }
            else
            {
                Ledger = new ImportLedger();
            }


            // Check the index file and add any files missing or recreate from scratch in ledger is empty
            var changes = false;
            var files = Directory.EnumerateFiles(folder,"*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // Remove the folder prefix
                changes |= CheckImportFile(file.Substring(folder.Length+1));
            }

            // If any files have been added then update the ledger file
            if (changes)
            {
                SaveLedger();
            }
        }

        /// <summary>
        /// Check to see if there are any new files in the index file that were not there before, eg. on create
        /// </summary>
        /// <param name="file"></param>
        private bool CheckImportFile(string file)
        {
            // Only accept files that match the list of valid extensions
            if (ValidExtensions.Contains(Path.GetExtension(file)))
            {
                // There is a new file that did not already exist. Add it.
                if (Ledger.importFiles.All(x => x.FileNameWithRelativePath != file))
                {
                    var newItem = new ImportFile()
                        .AddFile(file);
                    Ledger.importFiles.Add(newItem);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// If the ledger is no longer used.
        /// </summary>
        public void Clear()
        {
            // Let the garbage collector clean up the contents
            Ledger = null;
        }

        /// <summary>
        /// Save the ledger to disk
        /// </summary>
        public void SaveLedger()
        {
            // Make sure that only one thread is saving the ledger at once
            lock (Ledger)
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                    {
                        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                    },
                };
                var ledgerJson = JsonSerializer.Serialize<ImportLedger>(Ledger, options);
                File.WriteAllText(LedgerFilename, ledgerJson);
            }
        }

        /// <summary>
        /// Store the remote URL of the file being imported
        /// </summary>
        /// <param name="importFile">The import file being changed</param>
        /// <param name="remoteUrl">The remote URL to store</param>
        public void SetRemoteUrl(ImportFile importFile, string remoteUrl)
        {
            // Only one thread is working on each importFile at once so no need to lock
            importFile.RemoteUrl = remoteUrl;
            SetStatus(importFile, ImportStatus.Ongoing);
        }

        /// <summary>
        /// Set a new status in the ledger for the file to be imported
        /// </summary>
        /// <param name="importFile">The import file being changed</param>
        /// <param name="importStatus">The new status</param>
        public void SetStatus(ImportFile importFile, ImportStatus importStatus)
        {
            // Only one thread is working on each importFile at once so no need to lock
            importFile.Status = importStatus;
            importFile.WasModified();
            // Save the ledger after every change 
            SaveLedger();
        }
        
        /// <summary>
        /// Create a FileInfo object for the import file
        /// </summary>
        /// <param name="importFile">The import file that requires the FileInfo</param>
        /// <returns>The FileInfo object</returns>
        public FileInfo FileToImport(ImportFile importFile)
        {
            return new FileInfo(Path.Combine(folder, importFile.FileNameWithRelativePath));
        }
    }
}
