﻿using ITHit.FileSystem;
using ITHit.FileSystem.Windows;
using ITHit.WebDAV.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace VirtualFileSystem
{
    /// <summary>
    /// Maps a user file system path to the remote storage path and back. 
    /// </summary>
    /// <remarks>You will change methods of this class to map the user file system path to your remote storage path.</remarks>
    internal static class Mapping
    {
        /// <summary>
        /// Returns a remote storage URI that corresponds to the user file system path.
        /// </summary>
        /// <param name="userFileSystemPath">Full path in the user file system.</param>
        /// <returns>Remote storage URI that corresponds to the <paramref name="userFileSystemPath"/>.</returns>
        public static string MapPath(string userFileSystemPath)
        {
            // Get path relative to the virtual root.
            string relativePath = Path.TrimEndingDirectorySeparator(userFileSystemPath).Substring(
                Path.TrimEndingDirectorySeparator(Program.Settings.UserFileSystemRootPath).Length);
            relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar);

            string[] segments = relativePath.Split('\\');

            IEnumerable<string> encodedSegments = segments.Select(x => Uri.EscapeDataString(x));
            relativePath = string.Join('/', encodedSegments);      

            string path = $"{Program.Settings.WebDAVServerUrl}{relativePath}";


            // Add trailing slash to folder URLs so Uri class concatenation works correctly.
            if(!path.EndsWith('/') && Directory.Exists(userFileSystemPath))
            {
                path = $"{path}/";
            }

            return path;
        }

        /// <summary>
        /// Returns a user file system path that corresponds to the remote storage URI.
        /// </summary>
        /// <param name="remoteStorageUri">Remote storage URI.</param>
        /// <returns>Path in the user file system that corresponds to the <paramref name="remoteStorageUri"/>.</returns>
        public static string ReverseMapPath(string remoteStorageUri)
        {
            // Remove the 'https://server:8080/' part.
            string rsPath = new UriBuilder(remoteStorageUri).Path;
            string webDAVServerUrlPath = new UriBuilder(Program.Settings.WebDAVServerUrl).Path;

            // Get path relative to the virtual root.
            string relativePath = rsPath.Substring(webDAVServerUrlPath.TrimEnd('/').Length);
            relativePath = relativePath.TrimStart('/');

            string[] segments = relativePath.Split('/');

            IEnumerable<string> decodedSegments = segments.Select(x => Uri.UnescapeDataString(x));
            relativePath = string.Join(Path.DirectorySeparatorChar, decodedSegments);

            string path = $"{Program.Settings.UserFileSystemRootPath}{relativePath}";
            return path;
        }

        /// <summary>
        /// Gets a user file system item info from the remote storage data.
        /// </summary>
        /// <param name="remoteStorageItem">Remote storage item info.</param>
        /// <returns>User file system item info.</returns>
        public static FileSystemItemBasicInfo GetUserFileSysteItemBasicInfo(IHierarchyItemAsync remoteStorageItem)
        {
            FileSystemItemBasicInfo userFileSystemItem;

            if (remoteStorageItem is IFileAsync)
            {
                userFileSystemItem = new FileBasicInfo();
            }
            else
            {
                userFileSystemItem = new FolderBasicInfo();
            }

            userFileSystemItem.Name = remoteStorageItem.DisplayName;
            userFileSystemItem.Attributes = FileAttributes.Normal;
            userFileSystemItem.CreationTime = remoteStorageItem.CreationDate;
            userFileSystemItem.LastWriteTime = remoteStorageItem.LastModified;
            userFileSystemItem.LastAccessTime = remoteStorageItem.LastModified;
            userFileSystemItem.ChangeTime = remoteStorageItem.LastModified;

            // If the item is locked by another user, set the LockedByAnotherUser to true.
            userFileSystemItem.LockedByAnotherUser = remoteStorageItem.ActiveLocks.Length > 0;

            // If the file is moved/renamed and the app is not running this will help us 
            // to sync the file/folder to remote storage after app starts.
            userFileSystemItem.CustomData = new CustomData
            {
                OriginalPath = Mapping.ReverseMapPath(remoteStorageItem.Href.ToString())
            }.Serialize();

            if (remoteStorageItem is IFileAsync)
            {
                // We send the ETag to 
                // the server inside If-Match header togeter with updated content from client.
                // This will make sure the changes on the server is not overwritten.
                ((FileBasicInfo)userFileSystemItem).ETag = ((IFileAsync)remoteStorageItem).Etag;

                ((FileBasicInfo)userFileSystemItem).Length = ((IFileAsync)remoteStorageItem).ContentLength;
            };

            return userFileSystemItem;
        }
    }
}
