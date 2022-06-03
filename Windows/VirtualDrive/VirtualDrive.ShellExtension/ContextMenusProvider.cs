using System.Runtime.InteropServices;
using ITHit.FileSystem.Windows.ShellExtension;


namespace VirtualDrive.ShellExtension
{
    
    /// <summary>
    /// Implements Windows Explorer context menu.
    /// </summary>
    [ComVisible(true)]
    [ProgId("VirtualDrive.ContextMenusProvider")]
    [Guid("9C923BF3-3A4B-487B-AB4E-B4CF87FD1C25")]
    public class ContextMenusProvider : CloudFilesContextMenuVerbRpcBase
    {

    }
    
}
