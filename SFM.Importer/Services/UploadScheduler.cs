using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFM.DataImport.Models;
using SFM.Importer.Configuration;
using SFM.Importer.Models;
using TusDotNetClient;

namespace SFM.Importer.Services
{
    public class UploadScheduler
    {
        private readonly ImportLedgerHandler importLedgerHandler;
        private readonly ILogger logger;
        private readonly AppSettings appSettings;

        private List<Task> workerThreads = new List<Task>();

        public UploadScheduler(ImportLedgerHandler importLedgerHandler, ILogger<UploadScheduler> logger,
            IOptions<AppSettings> appSettings)
        {
            this.importLedgerHandler = importLedgerHandler;
            this.logger = logger;
            this.appSettings = appSettings.Value;
        }

        /// <summary>
        /// Start uploading any files to SFM that are waiting in the ledger
        /// </summary>
        /// <param name="cancellationToken"></param>
        public async Task StartAsync(CancellationToken cancellationToken, IProgress<UploadReport> progress)
        {
            progress.Report(new UploadReport { Status = UploadStatus.Ongoing });
            logger.LogInformation("Upload scheduler started");

            try
            {
                // Create a thread safe Queue
                var importQueue = new ConcurrentQueue<ImportFile>();

                //We need to make sure a certain order is maintained so we'll hard code these validations
                //1. Organization file
                //2. All patient / Report files

                // Check if the organization file has been sent. If not then send it before the threads start to
                // ensure that it is first to be processed
                var organizationFile = importLedgerHandler.Ledger.importFiles
                    .FirstOrDefault(x => x.FileType == FileType.OrganizationFile);

                if (organizationFile == null)
                {
                    logger.LogInformation("Could not find Organization file");
                    return;
                }

                if (organizationFile?.Status != ImportStatus.Completed)
                {
                    await UploadAFileAsync(Guid.NewGuid(), organizationFile, progress, cancellationToken);                   
                    
                    if (cancellationToken.IsCancellationRequested)
                    {
                        logger.LogInformation("User cancelled, terminating");
                        // If the caller has requested the the operation be cancelled then throw an exception 
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                if (organizationFile.Status == ImportStatus.Completed)
                {
                    // Only process items that are not completed
                    foreach (var item in importLedgerHandler.Ledger.importFiles.Where(x =>
                        x.Status != ImportStatus.Completed))
                    {
                        importQueue.Enqueue(item);
                    }
                    // Start worker threads that read from the queue until everything is done or the import is cancelled
                    for (int i = 0; i < appSettings.UploadThreads; i++)
                    {
                        var task = Task.Run(() =>
                        {
                            var workerId = Guid.NewGuid();
                            logger.LogInformation("[Worker {WorkerId}] Task created", workerId);
                            while (importQueue.TryDequeue(out var importFile))
                            {
                                logger.LogInformation("[Worker {WorkerId}] Item taken from queue", workerId);
                                
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    logger.LogInformation("[Worker {WorkerId}] User cancelled, terminating", workerId);
                                    return;
                                }
                                UploadAFileAsync(workerId, importFile, progress, cancellationToken).GetAwaiter().GetResult();
                            }

                            logger.LogInformation("[Worker {WorkerId}] No work left for worker, terminating", workerId);
                        });
                        workerThreads.Add(task);
                    }

                    Task.WaitAll(workerThreads.ToArray());
                }
            }
            catch (OperationCanceledException){}

            if (cancellationToken.IsCancellationRequested)
            {
                progress.Report(new UploadReport { Status = UploadStatus.Cancelled });
                logger.LogInformation("User cancelled, Scheduler closing");
            }
            else
            {
                progress.Report(new UploadReport { Status = UploadStatus.Finished });
                logger.LogInformation("All work completed, Scheduler closing");
            }
        }

        private async Task UploadAFileAsync(Guid workerId, ImportFile importFile, IProgress<UploadReport> progress, CancellationToken cancellationToken)
        {
            try
            {
                var client = new TusClient();
                client.AdditionalHeaders.Add("Authorization", "Bearer " + appSettings.Token);

                var address = appSettings.UploadAPI; // Connect to this service

                // Proxy changes based on: https://stackoverflow.com/questions/59343807/get-system-default-web-proxy-in-net-core
                var targetUri = new Uri(address);
                
                // get the system default web proxy ...
                var proxyUri = WebRequest.DefaultWebProxy.GetProxy(targetUri);

                // ... and check whether it should be used or not
                var proxyAuthorityEqualsTargetAuthority = proxyUri?.Authority?.Equals(targetUri.Authority) == true;
                var proxyRequired = !proxyAuthorityEqualsTargetAuthority;

                if (proxyRequired)
                {
                    var proxy = new WebProxy
                    {
                        Address = proxyUri,
                        BypassProxyOnLocal = false,
                        UseDefaultCredentials = true
                    };
                    client.Proxy = proxy;
                }

                var fileToUpload = importLedgerHandler.FileToImport(importFile);

                logger.LogInformation("[Worker {WorkerId}] Uploading file {FileName}", workerId, importFile.FileNameWithRelativePath);

                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("User cancelled, terminating");
                    return;
                }

                // If we do not have a remote file name, create one
                if (string.IsNullOrEmpty(importFile.RemoteUrl))
                {
                    // Need to start by creating a file on SFM
                    (string, string)[] metadata =
                    {
                        ("name", importFile.FileName),
                        ("contentType", "application/octet-stream"),
                        ("FileType", importFile.FileType.ToString()),
                        ("SessionId", importLedgerHandler.Ledger.ImportSession.ToString())
                    };
                    try
                    {
                        var fileUrl = await client.CreateAsync(address, fileToUpload.Length, metadata);
                        logger.LogInformation("[Worker {WorkerId}] Remote created {FileUrl}", workerId, fileUrl);
                        importLedgerHandler.SetRemoteUrl(importFile, fileUrl);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Worker {WorkerId}] Error when creating file at {Address}", workerId,
                            address);
                        importLedgerHandler.SetStatus(importFile, ImportStatus.Failed);
                        // Do not continue on the file
                        return;
                    }
                    finally
                    {
                        progress.Report(new UploadReport { Status = UploadStatus.FileStatusChanged });
                    }
                }

                try
                {
                    // Start the upload or resume if it previously failed midway
                    await client.UploadAsync(importFile.RemoteUrl, fileToUpload, 
                        // Chunk size in megabytes
                        chunkSize: 0.5D );
                    logger.LogInformation("[Worker {WorkerId}] File uploaded to {RemoteUrl}", workerId,
                        importFile.RemoteUrl);
                    importLedgerHandler.SetStatus(importFile, ImportStatus.Completed);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Worker {WorkerId}] Error when uploading file to {RemoteUrl}", workerId,
                        importFile.RemoteUrl);
                    importLedgerHandler.SetStatus(importFile, ImportStatus.Failed);
                }
                finally
                {
                    progress.Report(new UploadReport { Status = UploadStatus.FileStatusChanged });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Worker {WorkerId}] Error when uploading file", workerId);
                importLedgerHandler.SetStatus(importFile, ImportStatus.Failed);
            }
            finally
            {
                progress.Report(new UploadReport { Status = UploadStatus.FileStatusChanged });
            }
        }
    }
}