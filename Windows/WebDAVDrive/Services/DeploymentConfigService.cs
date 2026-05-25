using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebDAVDrive.Services
{
    /// <summary>
    /// Service responsible for applying deployment configuration to the application.
    /// Handles reading from JSON config file, license registration, and drive mounting.
    /// Works cross-platform (Windows and macOS).
    /// </summary>
    public class DeploymentConfigService
    {
        private readonly IDrivesService _drivesService;
        private readonly JsonConfigProvider _configProvider;
        private readonly ILog _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeploymentConfigService"/> class.
        /// </summary>
        /// <param name="drivesService">The drives service for mounting operations.</param>
        /// <param name="configProvider">The JSON configuration provider.</param>
        /// <param name="log">Logger instance.</param>
        public DeploymentConfigService(IDrivesService drivesService, JsonConfigProvider configProvider, ILog log)
        {
            _drivesService = drivesService ?? throw new ArgumentNullException(nameof(drivesService));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Tries to apply deployment configuration if it exists and hasn't been applied yet.
        /// This method is called during normal application startup.
        /// </summary>
        /// <returns>
        /// An <see cref="ApplyConfigResult"/> indicating the outcome of the operation.
        /// </returns>
        public async Task<ApplyConfigResult> TryApplyConfigurationAsync()
        {
            _log.Debug($"Deployment config path: {_configProvider.GetConfigFilePath()}");

            try
            {
                if (!_configProvider.ConfigurationExists())
                {
                    return new ApplyConfigResult
                    {
                        Success = true,
                        ExitCode = ExitCodes.Success,
                        Message = "No deployment configuration found. Normal startup."
                    };
                }

                if (_configProvider.IsAlreadyApplied())
                {
                    return new ApplyConfigResult
                    {
                        Success = true,
                        ExitCode = ExitCodes.Success,
                        Message = "Configuration was already applied previously."
                    };
                }

                DeploymentConfiguration? config = _configProvider.ReadConfiguration();

                if (config == null)
                {
                    _log.Warn("Deployment configuration file could not be read.");
                    return new ApplyConfigResult
                    {
                        Success = true,
                        ExitCode = ExitCodes.Success,
                        Message = "Configuration file could not be read. Normal startup."
                    };
                }

                if (!config.IsValid)
                {
                    _log.Warn("Deployment configuration is invalid (no drives with valid URL).");
                    return new ApplyConfigResult
                    {
                        Success = true,
                        ExitCode = ExitCodes.Success,
                        Message = "Configuration file invalid. Normal startup."
                    };
                }

                return await ApplyConfigurationInternalAsync(config);
            }
            catch (Exception ex)
            {
                _log.Error($"Unexpected error reading deployment configuration: {ex.Message}", ex);
                return new ApplyConfigResult
                {
                    Success = true,
                    ExitCode = ExitCodes.Success,
                    Message = "Error reading configuration. Normal startup."
                };
            }
        }

        /// <summary>
        /// Applies the deployment configuration (internal implementation).
        /// </summary>
        private Task<ApplyConfigResult> ApplyConfigurationInternalAsync(DeploymentConfiguration config)
        {
            _log.Info("Applying deployment configuration...");
            _log.Debug($"  SettingsVersion: {config.SettingsVersion ?? "(not set)"}");
            _log.Debug($"  License: {DeploymentConfiguration.GetMaskedLicense(config.License)}");
            _log.Debug($"  Drives: {config.Drives!.Count} drive(s)");

            for (int i = 0; i < config.Drives!.Count; i++)
            {
                DriveConfiguration drive = config.Drives[i];
                _log.Debug($"  Drive[{i}]: URL={drive.WebDAVServerURL}, Path={drive.UserFileSystemRootPath ?? "(auto)"}");
            }

            try
            {
                // Apply settings overrides before mounting
                ApplySettingsOverrides(config);

                // Mark as applied in LocalSettings
                MarkConfigurationAsApplied();

                _log.Info("Deployment configuration applied successfully. Drives will be mounted during initialization.");

                return Task.FromResult(new ApplyConfigResult
                {
                    Success = true,
                    ExitCode = ExitCodes.Success,
                    Message = "Configuration applied successfully."
                });
            }
            catch (Exception ex)
            {
                _log.Error($"Unexpected error applying deployment configuration: {ex.Message}", ex);
                return Task.FromResult(new ApplyConfigResult
                {
                    Success = false,
                    ExitCode = ExitCodes.UnexpectedError,
                    ErrorMessage = $"Unexpected error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Applies non-null settings from the deployment configuration to the application settings.
        /// Each deployment drive is converted to a DriveSettings and added to Settings.Drives.
        /// </summary>
        private void ApplySettingsOverrides(DeploymentConfiguration config)
        {
            AppSettings settings = _drivesService.Settings;
            int overrideCount = 0;

            if (!string.IsNullOrEmpty(config.License))
            {
                settings.License = config.License;
                overrideCount++;
            }
            else
            {
                _log.Warn("No License found in deployment config. Using license from appsettings.json.");
            }

            // Use the first drive from appsettings.json as a baseline so optional per-drive
            // fields omitted by the deployment config (e.g. RequestThumbnailsFor, CustomColumns)
            // still fall back to the bundled defaults instead of null.
            DriveSettings? baseline = settings.Drives.Count > 0 ? settings.Drives[0] : null;

            // Build the new list first so a broken deployment config can't wipe existing drives.
            List<DriveSettings> newDrives = new();

            if (config.Drives != null)
            {
                foreach (DriveConfiguration deployDrive in config.Drives)
                {
                    if (string.IsNullOrEmpty(deployDrive.WebDAVServerURL))
                    {
                        _log.Warn("Skipping deployment drive with empty WebDAVServerURL.");
                        continue;
                    }

                    DriveSettings driveSettings = baseline != null ? CloneDriveSettings(baseline) : new DriveSettings();
                    driveSettings.WebDAVServerURL = $"{deployDrive.WebDAVServerURL.TrimEnd('/')}/";

                    if (!string.IsNullOrEmpty(deployDrive.UserFileSystemRootPath))
                        driveSettings.UserFileSystemRootPath = Environment.ExpandEnvironmentVariables(deployDrive.UserFileSystemRootPath);

                    ApplyDriveOverrides(deployDrive, driveSettings);

                    driveSettings.MaxTransferConcurrentRequests ??= 6;
                    driveSettings.MaxOperationsConcurrentRequests ??= int.MaxValue;

                    newDrives.Add(driveSettings);
                }
            }
            else
            {
                _log.Warn("No Drives section found in deployment config.");
            }

            if (newDrives.Count == 0)
            {
                throw new InvalidOperationException(
                    "Deployment configuration produced no valid drives. " +
                    "Ensure the 'Drives' array contains at least one entry with a non-empty 'WebDAVServerURL'.");
            }

            // Swap atomically once the new set is validated.
            settings.Drives.Clear();
            settings.Drives.AddRange(newDrives);
            overrideCount += newDrives.Count;

            _log.Info($"Deployment configuration applied. {settings.Drives.Count} drive(s) configured.");
        }

        /// <summary>
        /// Returns a shallow copy of the given drive settings used as a baseline for overrides.
        /// </summary>
        private static DriveSettings CloneDriveSettings(DriveSettings source) => new DriveSettings
        {
            WebDAVServerURL = source.WebDAVServerURL,
            UserFileSystemRootPath = source.UserFileSystemRootPath,
            AutoLockTimeoutMs = source.AutoLockTimeoutMs,
            ManualLockTimeoutMs = source.ManualLockTimeoutMs,
            TrayMaxHistoryItems = source.TrayMaxHistoryItems,
            SyncIntervalMs = source.SyncIntervalMs,
            MaxTransferConcurrentRequests = source.MaxTransferConcurrentRequests,
            MaxOperationsConcurrentRequests = source.MaxOperationsConcurrentRequests,
            ThumbnailGeneratorUrl = source.ThumbnailGeneratorUrl,
            RequestThumbnailsFor = source.RequestThumbnailsFor,
            AutoLock = source.AutoLock,
            SetLockReadOnly = source.SetLockReadOnly,
            IncomingSyncMode = source.IncomingSyncMode,
            Compare = new Dictionary<string, string>(source.Compare),
            CustomColumns = new Dictionary<int, string>(source.CustomColumns),
            FolderInvalidationIntervalMs = source.FolderInvalidationIntervalMs,
        };

        /// <summary>
        /// Applies non-null per-drive overrides from deployment configuration to drive settings.
        /// </summary>
        private void ApplyDriveOverrides(DriveConfiguration source, DriveSettings target)
        {
            if (source.AutoLockTimeoutMs.HasValue)
                target.AutoLockTimeoutMs = source.AutoLockTimeoutMs.Value;

            if (source.ManualLockTimeoutMs.HasValue)
                target.ManualLockTimeoutMs = source.ManualLockTimeoutMs.Value;

            if (source.SyncIntervalMs.HasValue)
                target.SyncIntervalMs = source.SyncIntervalMs.Value;

            if (source.TrayMaxHistoryItems.HasValue)
                target.TrayMaxHistoryItems = source.TrayMaxHistoryItems.Value;

            if (source.FolderInvalidationIntervalMs.HasValue)
                target.FolderInvalidationIntervalMs = source.FolderInvalidationIntervalMs.Value;

            if (source.IncomingSyncMode != null && Enum.TryParse<IncomingSyncModeSetting>(source.IncomingSyncMode, true, out var syncMode))
                target.IncomingSyncMode = syncMode;

            if (source.MaxTransferConcurrentRequests.HasValue)
                target.MaxTransferConcurrentRequests = source.MaxTransferConcurrentRequests.Value;

            if (source.MaxOperationsConcurrentRequests.HasValue)
                target.MaxOperationsConcurrentRequests = source.MaxOperationsConcurrentRequests.Value;

            if (source.AutoLock.HasValue)
                target.AutoLock = source.AutoLock.Value;

            if (source.SetLockReadOnly.HasValue)
                target.SetLockReadOnly = source.SetLockReadOnly.Value;

            if (source.ThumbnailGeneratorUrl != null)
                target.ThumbnailGeneratorUrl = source.ThumbnailGeneratorUrl;

            if (source.RequestThumbnailsFor != null)
                target.RequestThumbnailsFor = source.RequestThumbnailsFor;

            if (source.Compare != null)
                target.Compare = source.Compare;

            if (source.CustomColumns != null)
            {
                var columns = new Dictionary<int, string>();
                foreach (var kvp in source.CustomColumns)
                {
                    if (int.TryParse(kvp.Key, out int key))
                        columns[key] = kvp.Value;
                    else
                        _log.Warn($"Invalid CustomColumns key: '{kvp.Key}'. Ignoring.");
                }
                target.CustomColumns = columns;
            }
        }

        /// <summary>
        /// Marks the configuration as applied so it won't be applied again.
        /// </summary>
        private void MarkConfigurationAsApplied()
        {
            try
            {
                bool marked = _configProvider.MarkAsApplied();
                if (marked)
                {
                    _log.Info("Configuration marked as applied in LocalSettings.");
                }
                else
                {
                    _log.Warn("Failed to mark configuration as applied. It may be applied again on next startup.");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to mark configuration as applied: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Represents the result of applying deployment configuration.
    /// </summary>
    public class ApplyConfigResult
    {
        /// <summary>
        /// Indicates whether the configuration was applied successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The exit code to return from the application.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Success message when operation completed successfully.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Error message when operation failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Exit codes for the apply-config operation.
    /// </summary>
    public static class ExitCodes
    {
        /// <summary>
        /// Configuration applied successfully.
        /// </summary>
        public const int Success = 0;

        /// <summary>
        /// No configuration found.
        /// </summary>
        public const int ConfigurationNotFound = 1;

        /// <summary>
        /// Configuration is invalid (missing required values).
        /// </summary>
        public const int InvalidConfiguration = 2;

        /// <summary>
        /// License validation failed.
        /// </summary>
        public const int LicenseValidationFailed = 3;

        /// <summary>
        /// Failed to mount the drive.
        /// </summary>
        public const int MountFailed = 4;

        /// <summary>
        /// Engine failed to start.
        /// </summary>
        public const int EngineFailed = 5;

        /// <summary>
        /// An unexpected error occurred.
        /// </summary>
        public const int UnexpectedError = 99;
    }
}
