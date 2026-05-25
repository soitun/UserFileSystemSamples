using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebDAVDrive.Services
{
    /// <summary>
    /// Represents per-drive configuration within a deployment configuration file.
    /// Each drive entry defines a WebDAV server to mount and optional per-drive settings overrides.
    /// All properties are nullable — only non-null values override the defaults.
    /// </summary>
    public class DriveConfiguration
    {
        public string? WebDAVServerURL { get; set; }
        public string? UserFileSystemRootPath { get; set; }
        public bool? DriveVisible { get; set; }
        public double? AutoLockTimeoutMs { get; set; }
        public double? ManualLockTimeoutMs { get; set; }
        public double? SyncIntervalMs { get; set; }
        public int? TrayMaxHistoryItems { get; set; }
        public double? FolderInvalidationIntervalMs { get; set; }
        public string? IncomingSyncMode { get; set; }
        public int? MaxTransferConcurrentRequests { get; set; }
        public int? MaxOperationsConcurrentRequests { get; set; }
        public bool? AutoLock { get; set; }
        public bool? SetLockReadOnly { get; set; }
        public string? ThumbnailGeneratorUrl { get; set; }
        public string? RequestThumbnailsFor { get; set; }
        public Dictionary<string, string>? Compare { get; set; }
        public Dictionary<string, string>? CustomColumns { get; set; }

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrWhiteSpace(WebDAVServerURL);
    }
}
