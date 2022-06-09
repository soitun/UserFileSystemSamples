using System.Runtime.InteropServices;
using ITHit.FileSystem.Windows.ShellExtension;

namespace WebDAVDrive.ShellExtension
{
    /// <summary>
    /// Implements Windows Explorer context menu.
    /// </summary>
    [ComVisible(true)]
    [ProgId("WebDAVDrive.ContextMenusProvider")]
    [Guid("A22EBD03-343E-433C-98DF-372C6B3A1538")]
    public class ContextMenusProvider : CloudFilesContextMenuVerbRpcBase
    {

    }
}