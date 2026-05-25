using ITHit.FileSystem;
using ITHit.FileSystem.Samples.Common.Windows;
using ITHit.FileSystem.Windows.Explorer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualDrive
{
    /// <inheritdoc />
    public class VirtualEngine : VirtualEngineBase
    {
        /// <summary>
        /// Monitors changes in the remote storage, notifies the client and updates the user file system.
        /// </summary>
        public readonly RemoteStorageMonitor RemoteStorageMonitor;

        /// <summary>
        /// Creates a vitual file system Engine.
        /// </summary>
        /// <param name="license">A license string.</param>
        /// <param name="userFileSystemRootPath">
        /// A root folder of your user file system. 
        /// Your file system tree will be located under this folder.
        /// </param>
        /// <param name="remoteStorageRootPath">Path to the remote storage root.</param>
        /// <param name="iconsFolderPath">Path to the icons folder.</param>
        /// <param name="logFormatter">Logger.</param>
        public VirtualEngine(
            string license,
            string userFileSystemRootPath,
            string remoteStorageRootPath,
            string iconsFolderPath,
            LogFormatter logFormatter)
            : base(license, userFileSystemRootPath, remoteStorageRootPath, iconsFolderPath, false, logFormatter)
        {
            RemoteStorageMonitor = new RemoteStorageMonitor(remoteStorageRootPath, this, this.Logger);
        }

        /// <inheritdoc/>
        public override async Task<IFileSystemItem> GetFileSystemItemAsync(byte[] remoteStorageItemId, FileSystemItemType itemType, IContext context, ILogger logger = null)
        {
            string userFileSystemPath = context.FileNameHint;
            if (itemType == FileSystemItemType.File)
            {
                return new VirtualFile(userFileSystemPath, remoteStorageItemId, this, logger);
            }
            else
            {
                return new VirtualFolder(userFileSystemPath, remoteStorageItemId, this, logger);
            }
        }

        private ConcurrentDictionary<string, DateTime> FoldersWithColumns = new ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// Updates Windows Explorer columns. 
        /// This method is called when user navigates to a new folder or listing occures in Windows Explorer.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="path">Path for which navigation or listing occured.</param>
        private void UpdateExplorerColumns(object sender, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            
            // Add columns only one time for each folder.
            if (!FoldersWithColumns.TryAdd(path, DateTime.UtcNow))
                return;

            try
            {
                var allWindows = WindowsExplorer.GetExplorerWindows(path);

                foreach (var window in allWindows)
                {
                    if (window.TryAddColumns(new[] { new ColumnListItem("System.FileAttributes") }).IsSuccess &&
                        window.TrySetViewMode(FolderViewMode.Details).IsSuccess)
                    {
                        window.TryRefresh();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"Failed to update Explorer columns for path: {path}", path, null, ex);
            }
        }


        /// <inheritdoc/>
        public override async Task StartAsync(bool processModified = true, CancellationToken cancellationToken = default)
        {
            WindowsExplorer.FolderNavigation += UpdateExplorerColumns;
            WindowsExplorer.FolderListing += UpdateExplorerColumns;
            await base.StartAsync(processModified, cancellationToken);
            await RemoteStorageMonitor.StartAsync();
        }

        /// <inheritdoc/>
        public override async Task StopAsync()
        {
            await base.StopAsync();
            await RemoteStorageMonitor.StopAsync();
            WindowsExplorer.FolderNavigation -= UpdateExplorerColumns;
            WindowsExplorer.FolderListing -= UpdateExplorerColumns;
        }

        private bool disposedValue;

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    RemoteStorageMonitor.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
            base.Dispose(disposing);
        }
    }
}
