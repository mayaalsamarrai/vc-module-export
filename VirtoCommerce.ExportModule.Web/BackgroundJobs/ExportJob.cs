using System;
using System.IO;
using System.Web.Hosting;
using Hangfire;
using Hangfire.Server;
using VirtoCommerce.ExportModule.Core.Model;
using VirtoCommerce.ExportModule.Core.Services;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.Platform.Core.PushNotifications;
using VirtoCommerce.Platform.Data.Common;

namespace VirtoCommerce.ExportModule.Web.BackgroundJobs
{
    public class ExportJob
    {
        private readonly IPushNotificationManager _pushNotificationManager;
        private readonly IDataExporter _dataExporter;
        private readonly IExportProviderFactory _exportProviderFactory;


        private readonly string _defaultExportFolder;
        private const string DefaultExportFileName = "exported_data.zip";

        public ExportJob(IDataExporter dataExporter,
            IPushNotificationManager pushNotificationManager,
            IExportProviderFactory exportProviderFactory,
            IModuleInitializerOptions moduleInitializerOptions)
        {
            _dataExporter = dataExporter;
            _pushNotificationManager = pushNotificationManager;
            _exportProviderFactory = exportProviderFactory;
            _defaultExportFolder = moduleInitializerOptions.VirtualRoot + "/App_Data/Export/";
        }

        public void ExportBackground(ExportDataRequest request, ExportPushNotification notification, IJobCancellationToken cancellationToken, PerformContext context)
        {
            void progressCallback(ExportProgressInfo x)
            {
                notification.Patch(x);
                notification.JobId = context.BackgroundJob.Id;
                _pushNotificationManager.Upsert(notification);
            }

            try
            {

                var localTmpFolder = HostingEnvironment.MapPath(_defaultExportFolder);
                var fileName = Path.Combine(localTmpFolder, Path.GetFileName(DefaultExportFileName));


                // Do not like provider creation here to get file extension, maybe need to pass created provider to Exporter.
                // Create stream inside Exporter is not good as it is not Exporter resposibility to decide where to write.
                var provider = _exportProviderFactory.CreateProvider(request);

                if (!string.IsNullOrEmpty(provider.ExportedFileExtension))
                {
                    fileName = Path.ChangeExtension(Path.GetFileName(DefaultExportFileName), provider.ExportedFileExtension);
                }

                var localTmpPath = Path.Combine(localTmpFolder, fileName);

                if (!Directory.Exists(localTmpFolder))
                {
                    Directory.CreateDirectory(localTmpFolder);
                }

                if (File.Exists(localTmpPath))
                {
                    File.Delete(localTmpPath);
                }

                //Import first to local tmp folder because Azure blob storage doesn't support some special file access mode 
                using (var stream = File.OpenWrite(localTmpPath))
                {
                    _dataExporter.Export(stream, request, progressCallback, new JobCancellationTokenWrapper(cancellationToken));
                    notification.DownloadUrl = $"api/export/download/{fileName}";
                }
            }
            catch (JobAbortedException)
            {
                //do nothing
            }
            catch (Exception ex)
            {
                notification.Errors.Add(ex.ExpandExceptionMessage());
            }
            finally
            {
                notification.Description = "Export finished";
                notification.Finished = DateTime.UtcNow;
                _pushNotificationManager.Upsert(notification);
            }
        }
    }
}
