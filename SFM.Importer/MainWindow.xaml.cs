using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFM.Importer.Configuration;
using SFM.Importer.Models;
using SFM.Importer.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SFM.Importer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public CancellationTokenSource cancellationTokenSource;
        private readonly ImportLedgerHandler importLedgerHandler;
        private readonly UploadScheduler uploadScheduler;

        private readonly AppSettings appsettings;
        private readonly ILogger<MainWindow> logger;


        public MainWindow(IOptions<AppSettings> appsettings, ILogger<MainWindow> logger,
            ImportLedgerHandler importLedgerHandler, UploadScheduler uploadScheduler)
        {
            this.importLedgerHandler = importLedgerHandler;
            this.uploadScheduler = uploadScheduler;

            this.logger = logger;
            this.appsettings = appsettings.Value;
            InitializeComponent();
            this.Visibility = Visibility.Visible; // Make the window visible before we show a possible error message
            if (!string.IsNullOrEmpty(this.appsettings.Folder))
            {
                // A folder was on the command line, automatically load the ledger
                ChangeFolder(this.appsettings.Folder, true);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    ChangeFolder(dialog.SelectedPath, false);
                }
            }
        }

        private void ChangeFolder(string folder, bool fromCommandLine)
        {
            if (File.Exists(Path.Combine(folder, DataImport.Models.Constants.OrganizationFilename)))
            {
                // Chosen directory is ok
                lblSelectedFolder.Content = folder;
                btnStartUpload.IsEnabled = true;
                gviewImportFiles.IsEnabled = true;
                btnSelectFolder.IsDefault = false;
                // The ImportLedgerHandler manages everything to do with keeping track of the files to be uploaded
                importLedgerHandler.Initialize(folder);
                // Set the item source for the grid view showing the status for each file
                gviewImportFiles.ItemsSource = importLedgerHandler.LedgerView;
                SetStatus("Klar til opplastning eller endring af FM export mappe", ActionImpact.Normal);
            }
            else
            {
                // Chosen directory does not contain the required files, revert.
                lblSelectedFolder.Content = "";
                btnStartUpload.IsEnabled = false;
                gviewImportFiles.IsEnabled = false;
                btnSelectFolder.IsDefault = true;
                importLedgerHandler.Clear();
                gviewImportFiles.ItemsSource = null;
                SetStatus("Venligst velg en mappe som inneholder FM export data", ActionImpact.NeedAttention);
                if (fromCommandLine)
                {
                    MessageBox.Show($"Valgt mappe \"{folder}\" mangler filen \"{DataImport.Models.Constants.OrganizationFilename}\".",
                        "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Valgt mappe \"{folder}\" mangler filen \"{DataImport.Models.Constants.OrganizationFilename}\". Vennligst velg en annen mappe.",
                        "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        public enum ActionImpact
        {
            Normal,
            ShowWorking,
            NeedAttention,
        }

        public void SetStatus(string statusText, ActionImpact actionImpact)
        {
            switch (actionImpact)
            {
                case ActionImpact.NeedAttention:
                    sbMain.Background = new LinearGradientBrush(Colors.Coral, Colors.Bisque, new Point(0, 0), new Point(5, 1));
                    lblStatus.FontWeight = FontWeights.UltraBold;
                    lblStatus.FontStretch = FontStretches.UltraExpanded;
                    lblStatus.Foreground = new SolidColorBrush(Colors.Beige);
                    break;
                case ActionImpact.Normal:
                    sbMain.Background = new LinearGradientBrush(Colors.Beige, Colors.CadetBlue, new Point(0, 0), new Point(5, 1));
                    lblStatus.FontWeight = FontWeights.Normal;
                    lblStatus.FontStretch = FontStretches.Normal;
                    lblStatus.Foreground = new SolidColorBrush(Colors.Black);
                    break;
                case ActionImpact.ShowWorking:
                    sbMain.Background = new LinearGradientBrush(Colors.Moccasin, Colors.LightBlue, new Point(0, 0), new Point(5, 1));
                    lblStatus.FontWeight = FontWeights.Normal;
                    lblStatus.FontStretch = FontStretches.Normal;
                    lblStatus.Foreground = new SolidColorBrush(Colors.Black);
                    break;
            }
            lblStatus.Text = statusText;
        }

        // Blog on how to use cancellation tokens and progress classes:
        // https://devblogs.microsoft.com/dotnet/async-in-4-5-enabling-progress-and-cancellation-in-async-apis/
        private async void StartUploadBtn_Click(object sender, RoutedEventArgs e)
        {
            logger.LogInformation("Upload started");

            // Switch state of buttons
            btnCancelUpload.IsEnabled = true;
            btnSelectFolder.IsEnabled = btnStartUpload.IsEnabled = false;
            SetStatus("Opplastning pågår...", ActionImpact.ShowWorking);

            // Create a cancellation token source that will be used to stop to worker threads if the user
            // clicks the cancel upload button
            cancellationTokenSource = new CancellationTokenSource();

            // Start the upload scheduler which will handle everything until complete or cancelled
            // Has to be done in a task or the UI will be blocked
            var progress = new Progress<UploadReport>(ReportUploadProgress);

            Task.Run(async () =>
            {
                await uploadScheduler.StartAsync(cancellationTokenSource.Token, progress);
            });
        }

        void ReportUploadProgress(UploadReport uploadReport)
        {
            switch (uploadReport.Status)
            {
                case UploadStatus.Finished:
                    SetStatus("Opplastning er ferdig", ActionImpact.Normal);
                    btnCancelUpload.IsEnabled = false;
                    btnSelectFolder.IsEnabled = btnStartUpload.IsEnabled = true;
                    break;
                case UploadStatus.Cancelled:
                    SetStatus("Opplastning ble avbrutt. Du kan starte igjen.", ActionImpact.Normal);
                    btnCancelUpload.IsEnabled = false;
                    btnSelectFolder.IsEnabled = btnStartUpload.IsEnabled = true;
                    break;
                case UploadStatus.FileStatusChanged:
                    //we just want the item source to be updated
                    break;
            }
            // Updates the Grid
            var itemSource = gviewImportFiles.ItemsSource;
            gviewImportFiles.ItemsSource = null;
            gviewImportFiles.ItemsSource = itemSource;
        }

        private void btnCancelUpload_Click(object sender, RoutedEventArgs e)
        {
            btnCancelUpload.IsEnabled = false;
            SetStatus("Opplastning blir afbrutt. Vent litt.", ActionImpact.NeedAttention);
            logger.LogInformation("Import is being cancelled!");
            // Tell the scheduler to stop working
            cancellationTokenSource.Cancel();
        }

    }
}