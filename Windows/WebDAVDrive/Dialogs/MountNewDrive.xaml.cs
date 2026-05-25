using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.System;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

using WebDAVDrive.Services;
using ITHit.FileSystem.Windows.WinUI.Dialogs;
using ITHit.FileSystem.Extensions;

namespace WebDAVDrive.Dialogs
{
    /// <summary>
    /// New drive mounting dialog.
    /// </summary>
    public sealed partial class MountNewDrive : DialogWindow
    {
        private readonly ResourceLoader resourceLoader = ResourceLoader.GetForViewIndependentUse();

        private bool displayNameEdited = false;

        public MountNewDrive() : base()
        {
            InitializeComponent();
            Resize(800, 400);
            Title = $"{ServiceProvider.GetService<AppSettings>().ProductName} - {resourceLoader.GetString("MountNewDriveWindow/Title")}";

            IDrivesService drivesService = ServiceProvider.GetService<IDrivesService>();
            RootPathEntry.Text = drivesService.GenerateRootPathForProtocolMounting();

            // Resize and center the window.
            SetDefaultPosition();
        }

        private async void OnBrowseClicked(object sender, RoutedEventArgs e)
        {
            FolderPicker folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            nint hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
                RootPathEntry.Text = folder.Path;
        }

        private void OnValidateClicked(object sender, RoutedEventArgs e)
        {
            string url = UrlEntry.Text;
            string userFileSystemRootPath = RootPathEntry.Text;
            string displayNameText = DisplayNameEntry.Text;

            // Check if the URL entry is empty
            if (string.IsNullOrWhiteSpace(url))
            {
                // Show required validation message
                RequiredMessage.Visibility = Visibility.Visible;
                ValidationMessage.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Hide the required validation message
                RequiredMessage.Visibility = Visibility.Collapsed;

                // Validate the URL format
                if (IsValidUrl(url))
                {
                    IDrivesService drivesService = ServiceProvider.GetService<IDrivesService>();
                    ValidationMessage.Visibility = Visibility.Collapsed;
                    btnAddDrive.IsEnabled = false;

                    // Mount new domain.
                    _ = Task.Run(async () =>
                    {
                        //TODO: pass display name here
                        (bool success, Exception? exception) result = await drivesService.MountNewAsync(url, userFileSystemRootPath, true, displayNameText);

                        if (result.success)
                        {
                            ServiceProvider.DispatcherQueue.TryEnqueue(() =>
                            {
                                Close();
                            });
                        }
                        else
                        {
                            // Unmount engine if mounting failed.
                            KeyValuePair<Guid, VirtualEngine>? engine = drivesService.Engines.Where(p => p.Value.RemoteStorageRootPath == url).FirstOrDefault();
                            if (engine != null)
                            {
                                await drivesService.UnMountAsync(engine.Value.Value.InstanceId);
                            }

                            ServiceProvider.DispatcherQueue.TryEnqueue(async () =>
                            {
                                ContentDialog dialog = new ContentDialog
                                {
                                    Title = resourceLoader.GetString("ErrorContentDialog/Title"),
                                    Content = result.exception?.Message,
                                    CloseButtonText = resourceLoader.GetString("ErrorContentDialog/CloseButtonText"),
                                    XamlRoot = Content.XamlRoot
                                };

                                await dialog.ShowAsync();
                                btnAddDrive.IsEnabled = true;
                            });
                        }
                    });
                }
                else
                {
                    ResourceLoader resourceLoader = ResourceLoader.GetForViewIndependentUse();
                    ValidationMessage.Text = resourceLoader.GetString("InvalidUrl");
                    ValidationMessage.Visibility = Visibility.Visible;
                }
            }
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool IsValidUrl(string url)
        {
            // Simple URL validation regex
            string pattern = @"^https?://([\w-]+(\.[\w-]+)+)(:[0-9]+)?(/[\w- ./?%&=]*)?$";
            return Regex.IsMatch(url, pattern);
        }

        //focus UrlEntry textbox, make it trigger click by Enter press
        private void UrlEntryLoaded(object sender, RoutedEventArgs e)
        {
            UrlEntry.Focus(FocusState.Programmatic);
        }

        //"click" Add button on Enter (being inside textbox focused)
        private void UrlEntryKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                OnValidateClicked(btnAddDrive, null);
            }
        }

        //in case user did not edited Display Name field - make default name from updated URL and fill Display Name field by it
        private void UrlEntryTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!displayNameEdited)
            {
                DisplayNameEntry.Text = PathExtensions.ConvertToDisplayName(UrlEntry.Text);
            }
        }

        //once Display Name field is edited by user - do not more generate default name from URL
        private void DisplayNameKeyDown(object sender, KeyRoutedEventArgs e)
        {
            displayNameEdited = true;
        }
    }
}
