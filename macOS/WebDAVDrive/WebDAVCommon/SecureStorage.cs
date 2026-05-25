using System;
using System.IO;
using System.Text.Json;
using Common.Core;
using CoreWlan;
using Security;

namespace WebDAVCommon
{
	public class SecureStorage: SecureStorageBase
    {
        public SecureStorage(string domainIdentifier) : base("group.com.webdav.vfs", domainIdentifier)
        {

        }

        public SecureStorage() : base("group.com.webdav.vfs")
        {

        }
    }
}

