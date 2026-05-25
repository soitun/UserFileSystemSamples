using System;
using System.Web;

namespace WebDAVMacApp
{
    /// <summary>
    /// Represents the extracted protocol parameters including Item URLs, Mount URL, and Command.
    /// </summary>
    public class ProtocolParameters
    {
        /// <summary>
        /// Gets or sets the list of item URLs.
        /// </summary>
        public List<string> ItemUrls { get; set; } = new();

        /// <summary>
        /// Gets or sets the WebDAV mount URL.
        /// </summary>
        public required Uri MountUrl { get; set; }

        /// <summary>
        /// Gets or sets the command to be executed (e.g., lock, unlock, etc.).
        /// </summary>
        public CommandType Command { get; set; }

        /// <summary>
        /// Gets or sets the license Id.
        /// </summary>
        public string? LicenseId { get; set; }

        /// <summary>
        /// Gets or sets the cookies to be used for authentication or session management.
        /// </summary>
        public string? Cookies { get; set; }

        /// <summary>
        /// Gets or sets the login URL for authentication purposes.
        /// </summary>
        public string? LoginUrl { get; set; }

        /// <summary>
        /// Parses the given URI and extracts the Item URLs, Mount URL, and Command.
        /// </summary>
        /// <param name="uri">The input URI to parse.</param>
        /// <returns>An instance of <see cref="ProtocolParameters"/> containing parsed data.</returns>
        /// <exception cref="ArgumentException">Thrown when the provided URI is invalid.</exception>
        public static ProtocolParameters Parse(Uri uri)
        {
            if (uri == null || string.IsNullOrEmpty(uri.AbsoluteUri))
                throw new ArgumentException("Invalid URI");

            // Extract parameters from the URI.
            Dictionary<string, string> parameters = ParseUriParameters(uri);

            return new ProtocolParameters
            {
                ItemUrls = GetItemUrls(parameters),
                MountUrl = new Uri(HttpUtility.UrlDecode(parameters["MountUrl"])),
                Command = parameters.ContainsKey("Command") && Enum.TryParse(HttpUtility.UrlDecode(parameters["Command"]), true, out CommandType command)
                    ? command
                    : CommandType.Open, // Default if parsing fails
                LicenseId = parameters.ContainsKey("LicenseId") ? HttpUtility.UrlDecode(parameters["LicenseId"]) : null,
                Cookies = parameters.ContainsKey("cookies") ? HttpUtility.UrlDecode(parameters["cookies"]) : null,
                LoginUrl = parameters.ContainsKey("LoginUrl") ? HttpUtility.UrlDecode(parameters["LoginUrl"]) : null
            };
        }

        /// <summary>
        /// Extracts key-value parameters from the provided URI.
        /// </summary>
        /// <param name="uri">The input URI containing parameters.</param>
        /// <returns>A dictionary of parameter names and values.</returns>
        private static Dictionary<string, string> ParseUriParameters(Uri uri)
        {
            // Extract the protocol prefix dynamically.
            int colonIndex = uri.AbsoluteUri.IndexOf(':');
            string uriWithoutProtocol = colonIndex >= 0
                ? uri.AbsoluteUri.Substring(colonIndex + 1)
                : uri.AbsoluteUri;

            // Split and parse key-value pairs.
            return uriWithoutProtocol
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(
                    pair => pair[0],
                    pair => pair.Length > 1 ? pair[1] : string.Empty
                );
        }

        /// <summary>
        /// Extracts the list of item URLs from the parsed parameters.
        /// </summary>
        /// <param name="parameters">A dictionary of key-value pairs extracted from the URI.</param>
        /// <returns>A list of item URLs.</returns>
        private static List<string> GetItemUrls(Dictionary<string, string> parameters)
        {
            if (!parameters.ContainsKey("ItemUrl"))
                return new List<string>();

            string itemUrlRaw = HttpUtility.UrlDecode(parameters["ItemUrl"]);
            string trimmedUrls = itemUrlRaw.Trim('[', '\"', ']');
            return trimmedUrls.Split(new[] { "\",\"" }, StringSplitOptions.None).ToList();
        }
    }

    /// <summary>
    /// Represents the types of commands that can be executed.
    /// </summary>
    public enum CommandType
    {
        /// <summary>
        /// Represents a lock command.
        /// </summary>
        Lock,

        /// <summary>
        /// Represents an unlock command.
        /// </summary>
        Unlock,

        /// <summary>
        /// Represents an open with command.
        /// </summary>
        OpenWith,

        /// <summary>
        /// Represents a print command.
        /// </summary>
        Print,

        /// <summary>
        /// Represents an open command.
        /// </summary>
        Open,

        /// <summary>
        /// Represents an edit command.
        /// </summary>
        Edit
    }
}

