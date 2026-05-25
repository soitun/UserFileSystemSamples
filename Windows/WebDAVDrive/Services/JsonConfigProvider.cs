using log4net;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebDAVDrive.Services
{
    /// <summary>
    /// Provides functionality to read configuration from a JSON file for automated deployments.
    /// Works cross-platform (Windows and macOS).
    /// </summary>
    public class JsonConfigProvider
    {
        /// <summary>
        /// The JSON configuration file name.
        /// </summary>
        private const string ConfigFileName = "webdavdrive-config.json";

        /// <summary>
        /// Key for storing deployment applied status in LocalSettings.
        /// </summary>
        private const string DeploymentAppliedKey = "DeploymentConfigApplied";

        /// <summary>
        /// Key for storing deployment applied timestamp in LocalSettings.
        /// </summary>
        private const string DeploymentAppliedAtKey = "DeploymentConfigAppliedAt";

        /// <summary>
        /// Key for storing the config file hash to detect changes.
        /// </summary>
        private const string DeploymentConfigHashKey = "DeploymentConfigHash";

        private readonly ILog _log;
        private readonly string _appId;

        /// <summary>
        /// JSON serializer options for reading configuration.
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonConfigProvider"/> class.
        /// </summary>
        /// <param name="log">Logger instance for logging operations.</param>
        /// <param name="appId">Application ID for determining config folder path.</param>
        public JsonConfigProvider(ILog log, string appId)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _appId = appId ?? throw new ArgumentNullException(nameof(appId));
        }

        /// <summary>
        /// Gets the cross-platform configuration directory path accessible to all users.
        /// </summary>
        /// <returns>Path to the configuration directory.</returns>
        public string GetConfigDirectoryPath()
        {
            // Use CommonApplicationData which is:
            // - Windows: C:\ProgramData
            // - macOS: /usr/share (or /Library/Application Support for per-app)
            // - Linux: /usr/share
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(basePath, _appId);
        }

        /// <summary>
        /// Gets the full path to the configuration file.
        /// </summary>
        /// <returns>Full path to the JSON configuration file.</returns>
        public string GetConfigFilePath()
        {
            return Path.Combine(GetConfigDirectoryPath(), ConfigFileName);
        }

        /// <summary>
        /// Reads the deployment configuration from the JSON file.
        /// </summary>
        /// <returns>
        /// A <see cref="DeploymentConfiguration"/> object containing the configuration values,
        /// or null if no valid configuration was found.
        /// </returns>
        public DeploymentConfiguration? ReadConfiguration()
        {
            string configPath = GetConfigFilePath();
            _log.Info($"Reading deployment configuration from: {configPath}");

            try
            {
                if (!File.Exists(configPath))
                {
                    _log.Debug($"Configuration file does not exist: {configPath}");
                    return null;
                }

                string jsonContent = File.ReadAllText(configPath);
                
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    _log.Warn("Configuration file is empty.");
                    return null;
                }

                DeploymentConfiguration? config = JsonSerializer.Deserialize<DeploymentConfiguration>(jsonContent, JsonOptions);

                if (config == null)
                {
                    _log.Warn("Failed to deserialize configuration file.");
                    return null;
                }

                if (config.IsValid)
                {
                    _log.Info($"Configuration loaded successfully. {config.Drives!.Count} drive(s), License: {DeploymentConfiguration.GetMaskedLicense(config.License)}");
                }
                else
                {
                    _log.Warn("Configuration file exists but is invalid (no valid drives found).");
                }

                return config;
            }
            catch (JsonException ex)
            {
                _log.Error($"Failed to parse configuration file. Invalid JSON format. Error: {ex.Message}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.Error($"Access denied reading configuration file: {configPath}. Error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _log.Error($"Error reading configuration file: {configPath}. Error: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Marks the configuration as applied by saving status to LocalSettings.
        /// This avoids writing to ProgramData which may be read-only.
        /// </summary>
        /// <returns>True if successfully marked as applied, false otherwise.</returns>
        public bool MarkAsApplied()
        {
            _log.Debug("Marking configuration as applied in LocalSettings...");

            try
            {
                string configHash = GetConfigFileHash();

                Windows.Storage.ApplicationData.Current.LocalSettings.Values[DeploymentAppliedKey] = true;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[DeploymentAppliedAtKey] = DateTime.UtcNow.ToString("O");
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[DeploymentConfigHashKey] = configHash;

                _log.Debug("Configuration marked as applied successfully.");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Error marking configuration as applied: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Checks if deployment configuration file exists.
        /// </summary>
        /// <returns>True if configuration file exists, false otherwise.</returns>
        public bool ConfigurationExists()
        {
            string configPath = GetConfigFilePath();
            bool exists = File.Exists(configPath);
            _log.Debug($"Configuration file exists check: {configPath} = {exists}");
            return exists;
        }

        /// <summary>
        /// Checks if configuration was already applied.
        /// Uses LocalSettings to track applied status, and compares config file hash
        /// to detect if config has changed since last application.
        /// </summary>
        /// <returns>True if configuration was already applied and hasn't changed, false otherwise.</returns>
        public bool IsAlreadyApplied()
        {
            try
            {
                // Check if marked as applied in LocalSettings
                if (!Windows.Storage.ApplicationData.Current.LocalSettings.Values.ContainsKey(DeploymentAppliedKey))
                {
                    return false;
                }

                bool applied = (bool)Windows.Storage.ApplicationData.Current.LocalSettings.Values[DeploymentAppliedKey];
                if (!applied)
                {
                    return false;
                }

                // Check if config file has changed since last application
                string? savedHash = Windows.Storage.ApplicationData.Current.LocalSettings.Values[DeploymentConfigHashKey] as string;
                string currentHash = GetConfigFileHash();

                if (savedHash != currentHash)
                {
                    _log.Info("Configuration file has changed since last application. Will re-apply.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.Debug($"Error checking if already applied: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets a hash of the configuration file content to detect changes.
        /// </summary>
        private string GetConfigFileHash()
        {
            try
            {
                string configPath = GetConfigFilePath();
                if (!File.Exists(configPath))
                {
                    return string.Empty;
                }

                string content = File.ReadAllText(configPath);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
                return Convert.ToBase64String(hashBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets information about when the configuration was applied.
        /// </summary>
        /// <returns>DateTime when config was applied, or null if not applied.</returns>
        public DateTime? GetAppliedAt()
        {
            try
            {
                if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.ContainsKey(DeploymentAppliedAtKey))
                {
                    string? dateStr = Windows.Storage.ApplicationData.Current.LocalSettings.Values[DeploymentAppliedAtKey] as string;
                    if (DateTime.TryParse(dateStr, out DateTime result))
                    {
                        return result;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Resets the applied status, allowing the configuration to be re-applied.
        /// </summary>
        public void ResetAppliedStatus()
        {
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove(DeploymentAppliedKey);
                Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove(DeploymentAppliedAtKey);
                Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove(DeploymentConfigHashKey);
                _log.Debug("Deployment applied status reset.");
            }
            catch (Exception ex)
            {
                _log.Warn($"Error resetting applied status: {ex.Message}");
            }
        }
    }
}
