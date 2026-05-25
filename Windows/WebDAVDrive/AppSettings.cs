using Microsoft.Extensions.Configuration;
using System.Reflection;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using ITHit.FileSystem.Synchronization;
using System.Text.Json.Serialization;


namespace WebDAVDrive
{
    /// <summary>
    /// Per-drive settings. Each drive has its own URL, mount path, and engine configuration.
    /// </summary>
    public class DriveSettings
    {
        /// <summary>
        /// WebDAV server URL for this drive.
        /// </summary>
        public string WebDAVServerURL { get; set; }

        /// <summary>
        /// Local folder path where the drive will be mounted.
        /// Supports environment variables like %USERPROFILE%.
        /// </summary>
        public string UserFileSystemRootPath { get; set; }

        /// <summary>
        /// Automatic lock timeout in milliseconds.
        /// </summary>
        [JsonPropertyName("AutomaticLockTimeout")]
        public double AutoLockTimeoutMs { get; set; }

        /// <summary>
        /// Manual lock timeout in milliseconds.
        /// </summary>
        [JsonPropertyName("ManualLockTimeout")]
        public double ManualLockTimeoutMs { get; set; }

        /// <summary>
        /// Controls the number of events in the tray window.
        /// </summary>
        public int TrayMaxHistoryItems { get; set; }

        /// <summary>
        /// Full outgoing synchronization and hydration/dehydration interval in milliseconds.
        /// </summary>
        public double SyncIntervalMs { get; set; }

        /// <summary>
        /// Throttling max of create/update/read concurrent requests.
        /// </summary>
        public int? MaxTransferConcurrentRequests { get; set; }

        /// <summary>
        /// Throttling max of listing/move/delete concurrent requests.
        /// </summary>
        public int? MaxOperationsConcurrentRequests { get; set; }

        /// <summary>
        /// URL to get a thumbnail for Windows Explorer thumbnails mode.
        /// Your server must return 404 Not Found if the thumbnail can not be generated.
        /// If incorrect size is returned, the image will be resized by the platform automatically.
        /// </summary>
        public string ThumbnailGeneratorUrl { get; set; }

        /// <summary>
        /// File types to request thumbnails for.
        /// To request thumbnails for specific file types, list file types using '|' separator.
        /// To request thumbnails for all file types set the value to "*".
        /// </summary>
        public string RequestThumbnailsFor { get; set; }

        /// <summary>
        /// Automatically lock the file in remote storage when opened for writing, unlock on close.
        /// </summary>
        public bool AutoLock { get; set; }

        /// <summary>
        /// Mark documents locked by other users as read-only.
        /// </summary>
        public bool SetLockReadOnly { get; set; }

        /// <summary>
        /// Incoming synchronization mode.
        /// </summary>
        public IncomingSyncModeSetting IncomingSyncMode { get; set; }

        /// <summary>
        /// Compare command settings.
        /// </summary>
        public Dictionary<string, string> Compare { get; set; } = new();

        /// <summary>
        /// Custom columns to display in Windows Explorer.
        /// </summary>
        public Dictionary<int, string> CustomColumns { get; set; } = new();

        /// <summary>
        /// Folder content invalidation period in milliseconds.
        /// </summary>
        public double FolderInvalidationIntervalMs { get; set; }

        /// <summary>
        /// Drive name.
        /// </summary>
        public string DriveName { get; set; }

        /// <summary>
        /// Refresh Windows Explorer when navigating to a folder. Default is true.
        /// </summary>
        public bool RefreshExplorerOnFolderNavigation { get; set; } = true;
    }

    /// <summary>
    /// Application settings.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Unique ID of this application.
        /// </summary>
        public string AppID { get; set; }

        /// <summary>
        /// Integrated Bundle License activating IT Hit User File System Engine and IT Hit WebDAV Client Library for .NET.
        /// </summary>
        public string License { get; set; }

        /// <summary>
        /// Path to the icons folder.
        /// </summary>
        public string IconsFolderPath { get; set; }

        /// <summary>
        /// Product name. Displayed in every location where product name is required.
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// Per-drive settings. Each drive is mounted as a separate sync root.
        /// </summary>
        public List<DriveSettings> Drives { get; set; } = new();
    }

    /// <summary>
    /// Binds, validates and normalizes Settings configuration.
    /// </summary>
    public static class SettingsConfigValidator
    {
        /// <summary>
        /// Binds, validates and normalizes WebDAV Context configuration.
        /// </summary>
        /// <param name="configuration">Instance of <see cref="IConfiguration"/>.</param>
        public static AppSettings ReadSettings(this IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            AppSettings settings = new AppSettings();

            configuration.Bind(settings);

            settings.Drives.Clear();

            foreach (var driveSection in configuration.GetSection("Drives").GetChildren())
            {
                DriveSettings drive = new DriveSettings();
                driveSection.Bind(drive);

                if (string.IsNullOrEmpty(drive.WebDAVServerURL))
                    continue;

                drive.WebDAVServerURL = $"{drive.WebDAVServerURL.TrimEnd('/')}/";

                if (!string.IsNullOrEmpty(drive.UserFileSystemRootPath))
                {
                    drive.UserFileSystemRootPath = Environment.ExpandEnvironmentVariables(drive.UserFileSystemRootPath);
                }

                drive.MaxTransferConcurrentRequests ??= 6;
                drive.MaxOperationsConcurrentRequests ??= int.MaxValue;

                settings.Drives.Add(drive);
            }

            if (settings.Drives.Count == 0)
            {
                throw new ArgumentException("At least one drive with a WebDAVServerURL is required.", "Drives");
            }

            // Icons folder.
            settings.IconsFolderPath = Path.Combine(AppContext.BaseDirectory, @"Images");

            // Load product name from entry exe file.
            object[] attributes = Assembly.GetEntryAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
            if (attributes.Length > 0)
            {
                settings.ProductName = (attributes[0] as AssemblyProductAttribute).Product;
            }

            return settings;
        }
    }

    /// <summary>
    /// Incoming synchronization mode settings value.
    /// </summary>
    public enum IncomingSyncModeSetting
    {
        /// <summary>
        /// No pulling or pushing from server will be used.
        /// </summary>
        Off = IncomingSyncMode.Disabled,

        /// <summary>
        /// Synchronization using on Sync ID algorithm.
        /// </summary>
        SyncId = IncomingSyncMode.SyncId,

        /// <summary>
        /// Recive Create, update, delete and move notifications via Web Sockets.
        /// </summary>
        CRUD = 2,

        /// <summary>
        /// Synchronization using remote storage pooling.
        /// </summary>
        TimerPooling = IncomingSyncMode.TimerPooling,

        /// <summary>
        /// Select mode automatically. Tries SyncID, than CRUD, than TimerPooling.
        /// </summary>
        Auto = 256
    }
}
