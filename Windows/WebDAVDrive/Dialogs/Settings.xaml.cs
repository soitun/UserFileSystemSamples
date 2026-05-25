using ITHit.FileSystem.Samples.Common.Windows;
using ITHit.FileSystem.Windows;
using ITHit.FileSystem.Windows.WinUI;
using ITHit.FileSystem.Windows.WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Reflection;
using WebDAVDrive.Services;
using Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using System.Threading.Tasks;
using System;

namespace WebDAVDrive.Dialogs
{
    /// <summary>
    /// New drive mounting dialog.
    /// </summary>
    public sealed partial class Settings : DialogWindow
    {
        private readonly ResourceLoader resourceLoader = ResourceLoader.GetForViewIndependentUse();

        private readonly VirtualEngine engine;
        private readonly Tray parentTrayWindow;

        private bool copyPathInProgress;
        private object? originalCopyButtonContent;
        private ToolTip? copyPathToolTip;
        private Registrar registrar;

        public Settings(Tray trayWindow, Registrar registrar) : base()
        {
            InitializeComponent();
            Resize(880, 750);
            engine = (trayWindow.Engine as VirtualEngine)!;
            parentTrayWindow = trayWindow;
            this.registrar = registrar;
            Title = $"{ServiceProvider.GetService<AppSettings>().ProductName} - {resourceLoader.GetString("SettingsWindow/Title")}";

            // Resize and center the window.
            SetDefaultPosition();

            //Set values from engine
            EnginePath.Text = engine.Path;

            originalCopyButtonContent = CopyEnginePath.Content;
            copyPathToolTip = new ToolTip
            {
                Content = resourceLoader.GetString("CopyTooltip/Content")
            };
            ToolTipService.SetToolTip(CopyEnginePath, copyPathToolTip);

            AutomaticLockTimeout.Text = (engine.AutoLockTimeoutMs / 1000).ToString();
            ManualLockTimeout.Text = engine.ManualLockTimeoutMs == -1 ? string.Empty : (engine.ManualLockTimeoutMs / 1000).ToString();
            TrayMaxHistoryItems.Text = engine.TrayMaxHistoryItems.ToString();
            AutoLockEnable.IsOn = engine.AutoLock;
            ReadOnlyOnLockedFiles.IsOn = engine.SetLockReadOnly;
            RefreshExplorerOnFolderNavigation.IsOn = engine.RefreshExplorerOnFolderNavigation;
            DriveName.Text = engine.DriveSettings.DriveName;

            List<IncomingSyncModeSettingModel> comboBoxItems = new List<IncomingSyncModeSettingModel>
            {
                new IncomingSyncModeSettingModel { Setting = IncomingSyncModeSetting.Auto, FriendlyName = resourceLoader.GetString("IncomingSyncAuto") },
                new IncomingSyncModeSettingModel { Setting = IncomingSyncModeSetting.SyncId, FriendlyName = resourceLoader.GetString("IncomingSyncSyncId") },
                new IncomingSyncModeSettingModel { Setting = IncomingSyncModeSetting.CRUD, FriendlyName = resourceLoader.GetString("IncomingSyncCRUD") },
                new IncomingSyncModeSettingModel { Setting = IncomingSyncModeSetting.TimerPooling, FriendlyName = resourceLoader.GetString("IncomingSyncTimerPooling") },
                new IncomingSyncModeSettingModel { Setting = IncomingSyncModeSetting.Off, FriendlyName = resourceLoader.GetString("IncomingSyncOff") }
            };
            PushSynchronizationModeComboBox.ItemsSource = comboBoxItems;
            PushSynchronizationModeComboBox.SelectedIndex = comboBoxItems.FindIndex(0, s => s.Setting == engine.SyncModeSetting);

            NavView.SelectedItem = NavView.MenuItems[0];
            //show version of ITHit.FileSystem.Windows assembly (EngineWindows type is located there)
            VersionLabel.Text = $"{ServiceProvider.GetService<AppSettings>().ProductName} - {resourceLoader.GetString("VersionLabel")} " +
                Assembly.GetAssembly(typeof(EngineWindows))!.GetName().Version!.ToString();

            //Assign events manually, after initial assignments - to avoid firing from code, and also to get rid of separate handler methods per each control.
            //Textual settings saving after each text change (key press)
            AutomaticLockTimeout.TextChanged += (sender, e) => ValidateAndSaveSettings();
            ManualLockTimeout.TextChanged += (sender, e) => ValidateAndSaveSettings();
            TrayMaxHistoryItems.TextChanged += (sender, e) => ValidateAndSaveSettings();
            AutoLockEnable.Toggled += (sender, e) => ValidateAndSaveSettings();
            ReadOnlyOnLockedFiles.Toggled += (sender, e) => ValidateAndSaveSettings();
            RefreshExplorerOnFolderNavigation.Toggled += (sender, e) => ValidateAndSaveSettings();
            PushSynchronizationModeComboBox.SelectionChanged += (sender, e) => ValidateAndSaveSettings();
            DriveName.TextChanged += (sender, e) => ValidateAndSaveSettings();
        }

        private async void CopyPathClicked(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(engine.Path);
            Clipboard.SetContent(dataPackage);

            if (copyPathInProgress)
                return;

            copyPathInProgress = true;
            try
            {
                string copiedText = resourceLoader.GetString("CopyPathButtonCopied/Content");
                if (copyPathToolTip != null)
                {
                    copyPathToolTip.Content = copiedText;
                    copyPathToolTip.IsOpen = true;
                }

                CopyEnginePath.Content = new FontIcon
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                    Glyph = "\uE001"
                };

                await Task.Delay(1200);

                if (copyPathToolTip != null)
                {
                    copyPathToolTip.IsOpen = false;
                    await Task.Delay(300);
                    //return old tooltip content
                    copyPathToolTip.Content = resourceLoader.GetString("CopyTooltip/Content");
                }

                CopyEnginePath.Content = originalCopyButtonContent;
            }
            finally
            {
                copyPathInProgress = false;
            }
        }

        /// <summary>
        /// Validates settings form. In case it's valid - saves updated settings.
        /// </summary>
        private void ValidateAndSaveSettings()
        {
            bool isValidationError = false;
            AutomaticRequiredMessage.Visibility = AutomaticValidationMessage.Visibility = ManualValidationMessage.Visibility =
                TrayMaxHistoryItemsRequiredMessage.Visibility = TrayMaxHistoryItemsValidationMessage.Visibility = Visibility.Collapsed;

            //"Automatic lock timeout" field is required and should be a number
            if (string.IsNullOrWhiteSpace(AutomaticLockTimeout.Text))
            {
                isValidationError = true;
                AutomaticRequiredMessage.Visibility = Visibility.Visible;
                AutomaticValidationMessage.Visibility = Visibility.Collapsed;
            }
            else if (!double.TryParse(AutomaticLockTimeout.Text, out double automaticLockTimeout))
            {
                isValidationError = true;
                AutomaticRequiredMessage.Visibility = Visibility.Collapsed;
                AutomaticValidationMessage.Visibility = Visibility.Visible;
            }

            //"Manual lock timeout" field is NOT required, but if provided - it should be a number
            if (!string.IsNullOrWhiteSpace(ManualLockTimeout.Text) && !double.TryParse(ManualLockTimeout.Text, out double manualLockTimeout))
            {
                isValidationError = true;
                ManualValidationMessage.Visibility = Visibility.Visible;
            }

            //"Tray max history items" field is required and should be a 10+ integer number
            if (string.IsNullOrWhiteSpace(TrayMaxHistoryItems.Text))
            {
                isValidationError = true;
                TrayMaxHistoryItemsRequiredMessage.Visibility = Visibility.Visible;
                TrayMaxHistoryItemsValidationMessage.Visibility = Visibility.Collapsed;
            }
            else if (!int.TryParse(TrayMaxHistoryItems.Text, out int trayMaxHistoryItems) || trayMaxHistoryItems < 10)
            {
                isValidationError = true;
                TrayMaxHistoryItemsRequiredMessage.Visibility = Visibility.Collapsed;
                TrayMaxHistoryItemsValidationMessage.Visibility = Visibility.Visible;
            }

            if (string.IsNullOrWhiteSpace(DriveName.Text))
            {
                isValidationError = true;
                DriveNameRequiredMessage.Visibility = Visibility.Visible;
            }

            if (!isValidationError)
            {
                UserSettingsService userSettingsService = ServiceProvider.GetService<UserSettingsService>();
                int trayMaxHistoryItemsShowing = int.Parse(TrayMaxHistoryItems.Text);
                bool driveNameChanged = DriveName.Text != engine.DriveSettings.DriveName;
                userSettingsService.SaveSettings(engine, new DriveSettings
                {
                    AutoLockTimeoutMs = double.Parse(AutomaticLockTimeout.Text) * 1000,
                    ManualLockTimeoutMs = (string.IsNullOrWhiteSpace(ManualLockTimeout.Text) || ManualLockTimeout.Text == "-1") ? -1 : (double.Parse(ManualLockTimeout.Text) * 1000),
                    TrayMaxHistoryItems = trayMaxHistoryItemsShowing,
                    SetLockReadOnly = ReadOnlyOnLockedFiles.IsOn,
                    AutoLock = AutoLockEnable.IsOn,
                    IncomingSyncMode = (PushSynchronizationModeComboBox.SelectedItem as IncomingSyncModeSettingModel)!.Setting,
                    DriveName = DriveName.Text,
                    RefreshExplorerOnFolderNavigation = RefreshExplorerOnFolderNavigation.IsOn
                });
                
                //update property of parent Tray window and clear history on the fly (if user provided smaller setting value)
                int? oldValue = parentTrayWindow.TrayMaxHistoryItems;
                parentTrayWindow.TrayMaxHistoryItems = trayMaxHistoryItemsShowing;
                if (oldValue.HasValue && oldValue.Value > trayMaxHistoryItemsShowing)
                {
                    parentTrayWindow.CleanupHistoryItems();
                }

                if (driveNameChanged)
                {
                    registrar.SetDriveName(engine.SyncRootId, DriveName.Text);
                }
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer is NavigationViewItem item)
            {
                string? tag = item.Tag?.ToString();

                SettingsPage.Visibility = Visibility.Collapsed;
                AboutPage.Visibility = Visibility.Collapsed;

                switch (tag)
                {
                    case "settings":
                        SettingsPage.Visibility = Visibility.Visible;
                        break;
                    case "about":
                        AboutPage.Visibility = Visibility.Visible;
                        break;
                }
            }
        }
    }

    public class IncomingSyncModeSettingModel
    {
        public IncomingSyncModeSetting Setting { get; set; }
        public string FriendlyName { get; set; }
    }
}
