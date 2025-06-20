using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

using ITHit.FileSystem;
using ITHit.FileSystem.Windows;
using ITHit.FileSystem.Samples.Common;
using ITHit.FileSystem.Samples.Common.Windows;
using ITHit.WebDAV.Client;
using ITHit.WebDAV.Client.Exceptions;

namespace WebDAVDrive
{
    ///<inheritdoc>
    public abstract class VirtualFileSystemItem : IFileSystemItemWindows, ILock
    {
        /// <summary>
        /// Application settings.
        /// </summary>
        protected AppSettings Settings;

        /// <summary>
        /// File or folder path in the user file system.
        /// </summary>
        protected readonly string UserFileSystemPath;

        /// <summary>
        /// File or folder item ID in the remote storage.
        /// </summary>
        protected readonly byte[] RemoteStorageItemId;

        /// <summary>
        /// Path of this file or folder in the remote storage.
        /// </summary>
        protected readonly string RemoteStoragePath;

        /// <summary>
        /// Logger.
        /// </summary>
        protected readonly ILogger Logger;

        /// <summary>
        /// Engine.
        /// </summary>
        protected readonly VirtualEngine Engine;

        protected readonly WebDavSession Dav;

        /// <summary>
        /// Automatic lock timeout in milliseconds.
        /// </summary>
        private readonly double autoLockTimeoutMs;

        /// <summary>
        /// Manual lock timeout in milliseconds.
        /// </summary>
        private readonly double manualLockTimeoutMs;

        /// <summary>
        /// Creates instance of this class.
        /// </summary>
        /// <param name="remoteStorageId">Remote storage item ID.</param>
        /// <param name="userFileSystemPath">User file system path. This paramater is available on Windows platform only. On macOS and iOS this parameter is always null.</param>
        /// <param name="engine">Engine instance.</param>
        /// <param name="autoLockTimeoutMs">Automatic lock timeout in milliseconds.</param>
        /// <param name="manualLockTimeoutMs">Manual lock timeout in milliseconds.</param>
        /// <param name="logger">Logger.</param>
        public VirtualFileSystemItem(byte[] remoteStorageId, string userFileSystemPath, VirtualEngine engine, double autoLockTimeoutMs, double manualLockTimeoutMs, AppSettings appSettings, ILogger logger)
        {
            if (string.IsNullOrEmpty(userFileSystemPath))
            {
                throw new ArgumentNullException(nameof(userFileSystemPath));
            }
            Settings = appSettings;
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Dav = engine.DavClient;

            UserFileSystemPath = userFileSystemPath;
            RemoteStorageItemId = remoteStorageId;
            RemoteStoragePath = Engine.Mapping.MapPath(userFileSystemPath);

            this.autoLockTimeoutMs = autoLockTimeoutMs;
            this.manualLockTimeoutMs = manualLockTimeoutMs;
        }

        ///<inheritdoc>
        public async Task MoveToAsync(string targetUserFileSystemPath, byte[] targetParentItemId, IOperationContext operationContext, IConfirmationResultContext resultContext, CancellationToken cancellationToken = default)
        {
            string userFileSystemNewPath = targetUserFileSystemPath;
            string userFileSystemOldPath = UserFileSystemPath;
            Logger.LogDebug($"{nameof(IFileSystemItem)}.{nameof(MoveToAsync)}()", userFileSystemOldPath, userFileSystemNewPath, operationContext);
        }

        /// <inheritdoc/>
        public async Task MoveToCompletionAsync(string targetUserFileSystemPath, byte[] targetFolderRemoteStorageItemId, IWindowsMoveContext operationContext, IInSyncStatusResultContext resultContext, CancellationToken cancellationToken = default)
        {
            string userFileSystemNewPath = targetUserFileSystemPath;
            string userFileSystemOldPath = UserFileSystemPath;
            Logger.LogMessage($"{nameof(IFileSystemItem)}.{nameof(MoveToCompletionAsync)}()", userFileSystemOldPath, userFileSystemNewPath, operationContext);

            string remoteStorageOldPath = RemoteStoragePath;
            string remoteStorageNewPath = Engine.Mapping.MapPath(userFileSystemNewPath);

            await Dav.MoveToAsync(new Uri(remoteStorageOldPath), new Uri(remoteStorageNewPath), true, null, null, cancellationToken);
        }

        ///<inheritdoc>
        public async Task DeleteAsync(IOperationContext operationContext, IConfirmationResultContext resultContext, CancellationToken cancellationToken = default)
        {
            Logger.LogDebug($"{nameof(IFileSystemItem)}.{nameof(DeleteAsync)}()", UserFileSystemPath, default, operationContext);

            // To cancel the operation and prevent the file from being deleted, 
            // call the resultContext.ReturnErrorResult() method or throw any exception inside this method.

            // IMPOTRTANT! See Windows Cloud API delete prevention bug description here: 
            // https://stackoverflow.com/questions/68887190/delete-in-cloud-files-api-stopped-working-on-windows-21h1
            // https://docs.microsoft.com/en-us/answers/questions/75240/bug-report-cfapi-ackdelete-borken-on-win10-2004.html

            // Note that some applications, such as Windows Explorer may call delete more than one time on the same file/folder.
        }

        /// <inheritdoc/>
        public async Task DeleteCompletionAsync(IOperationContext operationContext, IInSyncStatusResultContext resultContext, CancellationToken cancellationToken = default)
        {
            // On Windows, for move with overwrite on folders to function correctly, 
            // the deletion of the folder in the remote storage must be done in DeleteCompletionAsync()
            // Otherwise the folder will be deleted before files in it can be moved.

            Logger.LogMessage($"{nameof(IFileSystemItem)}.{nameof(DeleteCompletionAsync)}()", UserFileSystemPath, default, operationContext);

            try
            {
                await Dav.DeleteAsync(new Uri(RemoteStoragePath), null, null, cancellationToken);
            }
            catch (NotFoundException ex)
            {
                // The item is not found. We do not want the Engine to repeat the delete operation.
                Logger.LogDebug("Item already deleted", UserFileSystemPath, default, operationContext);
            }
            catch (WebDavHttpException ex)
            {
                // We want the Engine to try deleting this item again later.
                resultContext.SetInSync = false;
                Logger.LogMessage(ex.Message, UserFileSystemPath, default, operationContext);
            }
        }

        
        public async Task<byte[]> GetThumbnailAsync(uint size, IOperationContext operationContext)
        {
            byte[] thumbnail = null;

            string[] exts = Settings.RequestThumbnailsFor.Trim().Split("|");
            string ext = System.IO.Path.GetExtension(UserFileSystemPath).TrimStart('.');

            if (exts.Any(ext.Equals) || exts.Any("*".Equals))
            {
                string ThumbnailGeneratorUrl = Settings.ThumbnailGeneratorUrl.Replace("{thumbnail width}", size.ToString()).Replace("{thumbnail height}", size.ToString());
                string filePathRemote = ThumbnailGeneratorUrl.Replace("{path to file}", Engine.Mapping.MapPath(UserFileSystemPath));

                try
                {
                    using (IDownloadResponse response = await Dav.DownloadAsync(new Uri(filePathRemote)))
                    {
                        using (Stream stream = await response.GetResponseStreamAsync())
                        {
                            thumbnail = await StreamToByteArrayAsync(stream);
                        }
                    }
                }
                catch (System.Net.WebException we)
                {
                    Logger.LogMessage(we.Message, UserFileSystemPath);
                }
                catch (Exception e)
                {
                    Logger.LogError($"Failed to load thumbnail {size}px", UserFileSystemPath, null, e, operationContext);
                }
            }

            string thumbnailResult = thumbnail != null ? "Success" : "Not Impl";
            Logger.LogDebug($"{nameof(IFileSystemItem)}.{nameof(GetThumbnailAsync)}() - {thumbnailResult}", UserFileSystemPath, default, operationContext);

            return thumbnail;
        }

        private static async Task<byte[]> StreamToByteArrayAsync(Stream stream)
        {
            using (MemoryStream memoryStream = new())
            {
                await stream.CopyToAsync(memoryStream);
                return memoryStream.ToArray();
            }
        }
        

        /// <inheritdoc/>
        public async Task<IEnumerable<FileSystemItemPropertyData>> GetPropertiesAsync(IOperationContext operationContext)
        {
            // For this method to be called you need to register a properties handler.
            // See method description for more details.

            Logger.LogDebug($"{nameof(IFileSystemItem)}.{nameof(GetPropertiesAsync)}()", UserFileSystemPath, default, operationContext);

            IList<FileSystemItemPropertyData> props = new List<FileSystemItemPropertyData>();

            // Read LockInfo and choose the lock icon.
            if (operationContext.Properties.TryGetActiveLockInfo(out ServerLockInfo lockInfo))
            {
                // Determine if the item is locked by this user or thirt-party user.
                bool thisUser = Engine.IsCurrentUser(lockInfo.Owner);
                string lockIconName = thisUser ? "Locked" : "LockedByAnotherUser";

                // Get Lock Mode.
                if (thisUser && (lockInfo.Mode == LockMode.Auto))
                {
                    lockIconName += "Auto";
                }

                //If it's icon for current user (Locked or LockedAuto) and current theme is dark - use white icon
                if (thisUser && ServiceProvider.IsDarkTheme)
                {
                    lockIconName += "White";
                }

                // Set Lock Owner.
                FileSystemItemPropertyData propertyLockOwner = new FileSystemItemPropertyData()
                {
                    Id = (int)CustomColumnIds.LockOwnerIcon,
                    Value = lockInfo.Owner,
                    IconResource = Path.Combine(Engine.IconsFolderPath, lockIconName + ".ico")
                };
                props.Add(propertyLockOwner);

                // Set Lock Expires.
                FileSystemItemPropertyData propertyLockExpires = new FileSystemItemPropertyData()
                {
                    Id = (int)CustomColumnIds.LockExpirationDate,
                    Value = lockInfo.LockExpirationDateUtc.ToString(),
                    IconResource = Path.Combine(Engine.IconsFolderPath, "Empty.ico")
                };
                props.Add(propertyLockExpires);

                // Set Lock Scope
                FileSystemItemPropertyData propertyLockScope = new FileSystemItemPropertyData()
                {
                    Id = (int)CustomColumnIds.LockScope,
                    Value = lockInfo.Exclusive ? "Exclusive" : "Shared",
                    IconResource = Path.Combine(Engine.IconsFolderPath, "Empty.ico")
                };
                props.Add(propertyLockScope);
            }


            // Read content ETag.
            if (operationContext.Properties.TryGetContentETag(out string contentETag))
            {
                FileSystemItemPropertyData propertyContentETag = new FileSystemItemPropertyData()
                {
                    Id = (int)CustomColumnIds.ContentETag,
                    Value = contentETag,
                    IconResource = Path.Combine(Engine.IconsFolderPath, "Empty.ico")
                };
                props.Add(propertyContentETag);
            }

            // Read metadata ETag.
            if (operationContext.Properties.TryGetMetadataETag(out string metadataETag))
            {
                FileSystemItemPropertyData propertyMetadataETag = new FileSystemItemPropertyData()
                {
                    Id = (int)CustomColumnIds.MetadataETag,
                    Value = metadataETag,
                    IconResource = Path.Combine(Engine.IconsFolderPath, "Empty.ico")
                };
                props.Add(propertyMetadataETag);
            }

            // Remote storage item ID.
            if (RemoteStorageItemId != null)
            {
                FileSystemItemPropertyData propertyId = new FileSystemItemPropertyData()
                {
                    Id = (int)CustomColumnIds.Id,
                    Value = LogFormatter.IdToSting(RemoteStorageItemId),
                    IconResource = Path.Combine(Engine.IconsFolderPath, "Empty.ico")
                };
                props.Add(propertyId);
            }

            return props;
        }

        
        ///<inheritdoc>
        public async Task LockAsync(LockMode lockMode, IOperationContext operationContext, CancellationToken cancellationToken)
        {
            Logger.LogMessage($"{nameof(ILock)}.{nameof(LockAsync)}({lockMode})", UserFileSystemPath, default, operationContext);

            // Call your remote storage here to lock the item.
            // Save the lock token and other lock info received from the remote storage on the client.
            // Supply the lock-token as part of each remote storage update in IFile.WriteAsync() method.
            // Note that the actual lock timeout applied by the server may be different from the one requested.

            string lockOwner = Engine.CurrentUserPrincipal;
            double timeoutMs = lockMode == LockMode.Auto ? autoLockTimeoutMs : manualLockTimeoutMs;
            TimeSpan timeout = timeoutMs == -1 ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(timeoutMs);

            LockInfo lockInfo = (await Dav.LockAsync(new Uri(RemoteStoragePath), LockScope.Exclusive, false, lockOwner, timeout, null, cancellationToken)).WebDavResponse;

            // Save lock-token and lock-mode. Start the timer to refresh the lock.
            await SaveLockAsync(lockInfo, lockMode, operationContext, cancellationToken);
        }

        /// <summary>
        /// Saves lock token, lock mode, refreshes Windows Explorer UI and starts a timer to extend the lock.
        /// </summary>
        /// <param name="lockInfo">Lock info.</param>
        /// <param name="lockMode">Lock mode.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task object that can be awaited.</returns>
        private async Task SaveLockAsync(LockInfo lockInfo, LockMode lockMode, IOperationContext operationContext, CancellationToken cancellationToken)
        {
            if (Engine.Placeholders.TryGetItem(UserFileSystemPath, out PlaceholderItem placeholder))
            {
                // Save lock-token and lock-mode.
                ServerLockInfo serverLockInfo = new ServerLockInfo
                {
                    LockToken = lockInfo.LockToken.LockToken,
                    Exclusive = lockInfo.LockScope == LockScope.Exclusive,
                    Owner = lockInfo.Owner,
                    LockExpirationDateUtc = DateTimeOffset.Now.Add(lockInfo.TimeOut),
                    Mode = lockMode
                };
                operationContext.Properties.SetLockInfo(serverLockInfo);

                // Update Windows Explorer.
                placeholder.UpdateUI();

                Logger.LogDebug($"Locked/Refreshed by {lockInfo.Owner}, timeout: {lockInfo.TimeOut:hh\\:mm\\:ss\\.ff}", UserFileSystemPath, default, operationContext);

                // Start the timer to extend (refresh) the automatic lock when it is about to expire.
                if (lockInfo.TimeOut < TimeSpan.MaxValue && lockMode == LockMode.Auto)
                {
                    // We want to refresh the lock some time before the lock expires.
                    // Either 1 minute before, for release config, or 1/5 of a timeout time, for dev config.
                    double refreshTimeOut = lockInfo.TimeOut.TotalMilliseconds;
                    refreshTimeOut -= refreshTimeOut > 120000 ? 60000 : refreshTimeOut / 5;

                    System.Timers.Timer timer = new System.Timers.Timer(refreshTimeOut);
                    timer.AutoReset = false;
                    timer.Elapsed += (object sender, System.Timers.ElapsedEventArgs e) => {
                        LockRefreshAsync(
                            lockInfo.LockToken.LockToken,
                            lockInfo.TimeOut,
                            lockMode,
                            operationContext,
                            cancellationToken,
                            timer);
                    };
                    timer.Start();
                }
            }
        }

        private async void LockRefreshAsync(string lockToken, TimeSpan timeout, LockMode lockMode, IOperationContext operationContext, CancellationToken cancellationToken, System.Timers.Timer timer)
        {
            try
            {
                timer.Dispose();

                if (cancellationToken.IsCancellationRequested) return;

                // Check that the item is still locked.
                if (operationContext.Properties.TryGetActiveLockInfo(out ServerLockInfo serverLockInfo))
                {
                    // The item may be unlocked and than locked again. Check that the stored lock is the same as passed to this method.
                    bool sameToken = lockToken.Equals(serverLockInfo.LockToken, StringComparison.InvariantCultureIgnoreCase);
                    if (sameToken)
                    {
                        // If this is auto-lock and the there is no lock file (owner file) - do not refresh lock, unlock the file instead.
                        if (lockMode == LockMode.Auto
                            && FilterHelper.IsLockedWithOwnerFile(UserFileSystemPath) && !FilterHelper.IsOwnerFileExists(UserFileSystemPath))
                        {
                            await Engine.ClientNotifications(UserFileSystemPath).UnlockAsync(true, cancellationToken);
                            PlaceholderItem.UpdateUI(UserFileSystemPath);
                            return;
                        }

                        // Extend (refresh) the lock.
                        //Program.DavClient.RefreshLockAsync(new Uri(RemoteStoragePath), lockToken, timeout, cancellationToken);
                        IHierarchyItem item = (await Dav.GetItemAsync(new Uri(RemoteStoragePath), null, cancellationToken)).WebDavResponse;
                        LockInfo lockInfo = (await item.RefreshLockAsync(lockToken, timeout, null, cancellationToken)).WebDavResponse;

                        Logger.LogMessage($"Lock extended, new timeout: {lockInfo.TimeOut:hh\\:mm\\:ss\\.ff}", UserFileSystemPath, default, operationContext);

                        // Save the new lock. Start the timer to refresh the lock.
                        await SaveLockAsync(lockInfo, lockMode, operationContext, cancellationToken);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Logger.LogDebug("Lock refresh canceled", UserFileSystemPath, default, operationContext);
            }
            catch (Exception ex)
            {
                Logger.LogError("Lock refresh failed", UserFileSystemPath, default, ex, operationContext);
            }
        }
        

        
        ///<inheritdoc>
        public async Task<LockMode> GetLockModeAsync(IOperationContext operationContext, CancellationToken cancellationToken)
        {
            if (operationContext.Properties.TryGetActiveLockInfo(out ServerLockInfo lockInfo))
            {
                return lockInfo.Mode;
            }
            return LockMode.None;
        }
        

        
        ///<inheritdoc>
        public async Task UnlockAsync(IOperationContext operationContext, CancellationToken cancellationToken)
        {
            Logger.LogMessage($"{nameof(ILock)}.{nameof(UnlockAsync)}()", UserFileSystemPath, default, operationContext);

            // Read the lock-token.
            if (operationContext.Properties.TryGetActiveLockInfo(out ServerLockInfo lockInfo))
            {
                LockUriTokenPair[] lockTokens = new LockUriTokenPair[] { new LockUriTokenPair(new Uri(RemoteStoragePath), lockInfo.LockToken) };

                // Unlock the item in the remote storage.
                try
                {
                    await Dav.UnlockAsync(new Uri(RemoteStoragePath), lockTokens, null, cancellationToken);
                }
                catch (ITHit.WebDAV.Client.Exceptions.ConflictException)
                {
                    Logger.LogDebug("The item is already unlocked.", UserFileSystemPath, default, operationContext);
                }
            }

            // Delete lock-mode and lock-token info.
            operationContext.Properties.TryDeleteLockInfo();
        }
        

        
        ///<inheritdoc>
        public async Task<IMetadata> GetMetadataAsync(IOperationContext operationContext, IResultContextBase resultContext, CancellationToken cancellationToken = default)
        {
            Logger.LogMessage($"{nameof(ILock)}.{nameof(GetMetadataAsync)}()", UserFileSystemPath, default, operationContext);

            IWebDavResponse<ITHit.WebDAV.Client.IFile> resp = await Dav.GetFileAsync(new Uri(RemoteStoragePath), Mapping.GetDavProperties());
            IMetadata metadata = Mapping.GetMetadata(resp.WebDavResponse);

            return metadata;
        }
        
    }
}
