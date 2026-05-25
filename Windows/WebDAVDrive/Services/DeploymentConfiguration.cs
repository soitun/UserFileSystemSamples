using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace WebDAVDrive.Services
{
    /// <summary>
    /// Represents the top-level deployment configuration read from JSON file.
    /// Used for Intune, GPO, SCCM, Jamf, or unattended installation scenarios.
    /// </summary>
    public class DeploymentConfiguration
    {
        /// <summary>
        /// Configuration format version for future compatibility.
        /// </summary>
        public string? SettingsVersion { get; set; }

        /// <summary>
        /// License to activate IT Hit User File System Engine and WebDAV Client Library.
        /// Both components accept the same license string.
        /// </summary>
        public string? License { get; set; }

        /// <summary>
        /// Array of drive configurations.
        /// </summary>
        public List<DriveConfiguration>? Drives { get; set; }

        /// <summary>
        /// Indicates whether the configuration is valid and can be applied.
        /// At least one drive with a valid URL is required.
        /// </summary>
        [JsonIgnore]
        public bool IsValid => Drives != null && Drives.Count > 0 && Drives.Any(d => d.IsValid);

        /// <summary>
        /// Returns a masked version of a license string for logging purposes.
        /// </summary>
        public static string GetMaskedLicense(string? license)
        {
            if (string.IsNullOrEmpty(license))
            {
                return "(not set)";
            }

            if (license.Length <= 8)
            {
                return new string('*', license.Length);
            }

            return $"{license[..4]}...{license[^4..]}";
        }
    }
}
