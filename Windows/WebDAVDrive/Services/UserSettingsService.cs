using ITHit.FileSystem.Windows.ShellExtension;

namespace WebDAVDrive.Services
{
    public class UserSettingsService
    {
        //Save basic settings for engine - to secure storage and to engine
        public void SaveSettings(VirtualEngine engine, DriveSettings model)
        {
            string normalized = $"{engine.RemoteStorageRootPath.TrimEnd('/')}/";
            SecureStorageService secureStorage = ServiceProvider.GetService<SecureStorageService>();
            secureStorage.SaveSensitiveData(normalized + "UserSettings", model);
            IncomingSyncModeSetting oldSyncMode = engine.SyncModeSetting;

            //save to engine
            engine.AutoLockTimeoutMs = model.AutoLockTimeoutMs;
            engine.ManualLockTimeoutMs = model.ManualLockTimeoutMs;
            engine.TrayMaxHistoryItems = model.TrayMaxHistoryItems;
            engine.AutoLock = model.AutoLock;
            engine.SetLockReadOnly = model.SetLockReadOnly;
            engine.SyncModeSetting = model.IncomingSyncMode;
            engine.DriveSettings.DriveName = model.DriveName;
            engine.RefreshExplorerOnFolderNavigation = model.RefreshExplorerOnFolderNavigation;

            //in case sync mode setting has just changed - we should stop, recreate and start monitor
            if (engine.SyncModeSetting != oldSyncMode)
            {
                engine.RestartMonitor();
            }
        }

        //Get basic settings for engine from secure storage
        public DriveSettings? GetSettings(string engineRemotePath)
        {
            string normalized = $"{engineRemotePath.TrimEnd('/')}/";
            DriveSettings settingsData;
            SecureStorageService secureStorage = ServiceProvider.GetService<SecureStorageService>();
            //try to get by normalized URL, if no success - get by original URL (as existing settings can be stored at original URL)
            return secureStorage.TryGetSensitiveData(normalized + "UserSettings", out settingsData) ? settingsData :
                secureStorage.TryGetSensitiveData(engineRemotePath + "UserSettings", out settingsData) ? settingsData : null;
        }
    }
}
