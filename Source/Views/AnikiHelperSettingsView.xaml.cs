using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using Forms = System.Windows.Forms;

namespace AnikiHelper
{
    public partial class AnikiHelperSettingsView : UserControl
    {
        public AnikiHelperSettingsView()
        {
            InitializeComponent();

            LoadLocaleFromCurrentUICulture();

            Loaded += AnikiHelperSettingsView_Loaded;
        }

        private void AnikiHelperSettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var vm = DataContext as AnikiHelperSettingsViewModel;
                    vm?.RefreshHomeDashboard();
                    vm?.Settings?.LoadOverlayApps();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch
            {
            }
        }


        private void ConfigureThemeFeatures_Click(object sender, RoutedEventArgs e)
        {
            if (MainSettingsTabs != null)
            {
                MainSettingsTabs.SelectedIndex = 1;
            }
        }

        private void ConfigureSteam_Click(object sender, RoutedEventArgs e)
        {
            if (MainSettingsTabs != null)
            {
                MainSettingsTabs.SelectedIndex = 2;
            }
        }

        private void ConfigureFullscreenTools_Click(object sender, RoutedEventArgs e)
        {
            if (MainSettingsTabs != null)
            {
                MainSettingsTabs.SelectedIndex = 3;
            }
        }

        private void InstallPlayniteAchievements_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.InstallPlayniteAchievements();
        }

        private void InstallUniPlaySong_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.InstallUniPlaySong();
        }

        private void ChooseScreenshotProvider_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.ChooseAndInstallScreenshotProvider();
        }

        private void InstallScreenshotUtilitiesLocalProvider_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.InstallScreenshotUtilitiesLocalProvider();
        }

        private void HubAppsToolComboBox_DropDownOpened(object sender, EventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                vm?.Settings?.LoadOverlayApps();
            }
            catch
            {
            }
        }

        private void LoadLocaleFromCurrentUICulture()
        {
            try
            {
                CultureInfo cul = CultureInfo.CurrentUICulture;

                string asmName = Assembly.GetExecutingAssembly().GetName().Name; // "AnikiHelper"
                string basePack = $"pack://application:,,,/{asmName};component/";

                string dash = cul.Name;                     // ex: "fr-FR"
                string underscore = dash.Replace('-', '_'); // ex: "fr_FR"
                string neutral = cul.TwoLetterISOLanguageName; // ex: "fr"

                string[] candidates =
                {
                    basePack + $"Localization/{dash}.xaml",
                    basePack + $"Localization/{underscore}.xaml",
                    basePack + $"Localization/{neutral}.xaml"
                };

                foreach (var uri in candidates)
                {
                    try
                    {
                        var dict = (ResourceDictionary)Application.LoadComponent(new Uri(uri, UriKind.Absolute));
                        Application.Current.Resources.MergedDictionaries.Insert(0, dict);
                        return;
                    }
                    catch
                    {

                    }
                }
            }
            catch
            {
                // fallback EN
            }
        }

        private void DeleteAllMonthlyStats_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var confirmText = GetResourceText(
                    "MonthlyDeleteAll_Confirm",
                    "Are you sure you want to permanently delete all Monthly Stats data? Tracking for the current month will restart from now. This action cannot be undone unless you exported a backup.");

                var result = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    : MessageBox.Show(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.DeleteAllMonthlyStats();

                ShowInformation(GetResourceText(
                    "MonthlyDeleteAll_Success",
                    "All Monthly Stats data has been deleted. Tracking restarts from now."));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("MonthlyDeleteAll_Error", "Error while deleting Monthly Stats data:") +
                    "\n" + ex.Message);
            }
        }

        private void ExportMonthlyBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Title = "Export Monthly Backup",
                    Filter = "JSON file (*.json)|*.json",
                    FileName = $"AnikiHelper_MonthlyBackup_{DateTime.Now:yyyy-MM-dd}.json",
                    DefaultExt = ".json",
                    AddExtension = true
                };

                if (dlg.ShowDialog() == true)
                {
                    vm.ExportMonthlyBackup(dlg.FileName);
                }
            }
            catch (Exception ex)
            {
                var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;
                if (api != null)
                {
                    api.Dialogs.ShowErrorMessage("Error while exporting monthly backup:\n" + ex.Message, "Aniki Helper");
                }
                else
                {
                    MessageBox.Show("Error while exporting monthly backup:\n" + ex.Message, "Aniki Helper");
                }
            }
        }

        private void ImportMonthlyBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var api = vm?.Api;
                if (vm == null)
                {
                    return;
                }

                var confirmText = "Importing a monthly backup will rebuild monthly snapshot files for the current library. Continue?";
                var res = api != null
                    ? api.Dialogs.ShowMessage(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes)
                {
                    return;
                }

                var dlg = new OpenFileDialog
                {
                    Title = "Import Monthly Backup",
                    Filter = "JSON file (*.json)|*.json",
                    DefaultExt = ".json",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() == true)
                {
                    vm.ImportMonthlyBackup(dlg.FileName);
                }
            }
            catch (Exception ex)
            {
                var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;
                if (api != null)
                {
                    api.Dialogs.ShowErrorMessage("Error while importing monthly backup:\n" + ex.Message, "Aniki Helper");
                }
                else
                {
                    MessageBox.Show("Error while importing monthly backup:\n" + ex.Message, "Aniki Helper");
                }
            }
        }

        private void ExportThemeConfiguration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Title = GetResourceText("ThemeConfiguration_ExportDialogTitle", "Export theme configuration"),
                    Filter = "JSON file (*.json)|*.json",
                    FileName = $"AnikiHelper_ThemeConfiguration_{DateTime.Now:yyyy-MM-dd}.json",
                    DefaultExt = ".json",
                    AddExtension = true
                };

                if (dlg.ShowDialog() != true)
                {
                    return;
                }

                vm.ExportThemeConfiguration(dlg.FileName);

                ShowInformation(
                    GetResourceText("ThemeConfiguration_ExportSuccess", "Theme configuration exported successfully."));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("ThemeConfiguration_ExportError", "Error while exporting the theme configuration:") +
                    "\n" + ex.Message);
            }
        }

        private void ImportThemeConfiguration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dlg = new OpenFileDialog
                {
                    Title = GetResourceText("ThemeConfiguration_ImportDialogTitle", "Import theme configuration"),
                    Filter = "JSON file (*.json)|*.json",
                    DefaultExt = ".json",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() != true)
                {
                    return;
                }

                var confirmText = GetResourceText(
                    "ThemeConfiguration_ImportConfirm",
                    "Importing this file will replace the current theme customization options and presets. Continue?");

                var result = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.ImportThemeConfiguration(dlg.FileName);

                ShowInformation(
                    GetResourceText("ThemeConfiguration_ImportSuccess", "Theme configuration imported successfully."));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("ThemeConfiguration_ImportError", "Error while importing the theme configuration:") +
                    "\n" + ex.Message);
            }
        }

        private string GetResourceText(string key, string fallback)
        {
            try
            {
                return Application.Current?.TryFindResource(key) as string ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private void ShowInformation(string message)
        {
            var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;

            if (api != null)
            {
                api.Dialogs.ShowMessage(message, "Aniki Helper", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(message, "Aniki Helper", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ShowError(string message)
        {
            var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;

            if (api != null)
            {
                api.Dialogs.ShowErrorMessage(message, "Aniki Helper");
            }
            else
            {
                MessageBox.Show(message, "Aniki Helper", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearColorCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var api = vm?.Api;
                if (vm == null || api == null)
                {
                    return;
                }

                var confirmText = (string)Application.Current.TryFindResource("ConfirmClearCache")
                                  ?? "Clear dynamic color cache? The palette file will be deleted and rebuilt automatically.";

                var res = api.Dialogs.ShowMessage(
                    confirmText,
                    "Aniki Helper",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.ClearColorCache();

                var doneText = (string)Application.Current.TryFindResource("CacheClearedMsg")
                               ?? "Color cache cleared. It will rebuild automatically as you browse your games.";

                api.Dialogs.ShowMessage(
                    doneText,
                    "Aniki Helper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;
                if (api != null)
                {
                    api.Dialogs.ShowErrorMessage("Error while clearing cache:\n" + ex.Message, "Aniki Helper");
                }
                else
                {
                    MessageBox.Show("Error while clearing cache:\n" + ex.Message, "Aniki Helper");
                }
            }
        }

        private void ResetSplashMinDuration_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnikiHelperSettingsViewModel vm)
            {
                vm.Settings.GameLaunchSplashMinimumDurationMs = 2400;
            }
        }

        private void ManageSourceSplash_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;
            vm?.OpenSourceSplashScreenManager();
        }

        private void ManagePlatformSplash_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;
            vm?.OpenPlatformSplashScreenManager();
        }

        private void ClearLogFile_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.ClearLogFile();
        }

        private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.OpenLogsFolder();
        }

        private void ManageGlobalSplash_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                vm.OpenGlobalSplashScreenManager();
            }
            catch (Exception ex)
            {
                var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;
                if (api != null)
                {
                    api.Dialogs.ShowErrorMessage("Error while opening global splash manager:\n" + ex.Message, "Aniki Helper");
                }
                else
                {
                    MessageBox.Show("Error while opening global splash manager:\n" + ex.Message, "Aniki Helper");
                }
            }
        }

        private void ClearNewsCacheA_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var api = vm?.Api;
                if (vm == null || api == null)
                {
                    return;
                }

                var confirmText = (string)Application.Current.TryFindResource("AnikiNews_SourceA_ClearCache_Confirm")
                                  ?? "Clear source A cache?";

                var res = api.Dialogs.ShowMessage(
                    confirmText,
                    "Aniki Helper",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.ClearNewsCacheA();

                var doneText = (string)Application.Current.TryFindResource("AnikiNews_SourceA_ClearCache_Done")
                               ?? "Source A cache cleared.";

                api.Dialogs.ShowMessage(
                    doneText,
                    "Aniki Helper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;
                if (api != null)
                {
                    api.Dialogs.ShowErrorMessage("Error while clearing source A cache:\n" + ex.Message, "Aniki Helper");
                }
                else
                {
                    MessageBox.Show("Error while clearing source A cache:\n" + ex.Message, "Aniki Helper");
                }
            }
        }

        private void ClearNewsCacheB_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var api = vm?.Api;
                if (vm == null || api == null)
                {
                    return;
                }

                var confirmText = (string)Application.Current.TryFindResource("AnikiNews_SourceB_ClearCache_Confirm")
                                  ?? "Clear source B cache?";

                var res = api.Dialogs.ShowMessage(
                    confirmText,
                    "Aniki Helper",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.ClearNewsCacheB();

                var doneText = (string)Application.Current.TryFindResource("AnikiNews_SourceB_ClearCache_Done")
                               ?? "Source B cache cleared.";

                api.Dialogs.ShowMessage(
                    doneText,
                    "Aniki Helper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;
                if (api != null)
                {
                    api.Dialogs.ShowErrorMessage("Error while clearing source B cache:\n" + ex.Message, "Aniki Helper");
                }
                else
                {
                    MessageBox.Show("Error while clearing source B cache:\n" + ex.Message, "Aniki Helper");
                }
            }
        }

        private string SelectFolder(string title, string currentPath)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = title;
                dialog.ShowNewFolderButton = true;

                var normalizedCurrentPath = string.IsNullOrWhiteSpace(currentPath)
                    ? string.Empty
                    : currentPath.Replace("/", "\\");

                if (!string.IsNullOrWhiteSpace(normalizedCurrentPath) && Directory.Exists(normalizedCurrentPath))
                {
                    dialog.SelectedPath = normalizedCurrentPath;
                }

                var result = dialog.ShowDialog();

                if (result == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    return dialog.SelectedPath.Replace("\\", "/").TrimEnd('/');
                }

                return null;
            }
        }

        private string SelectImageFile(string title, string currentPath)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                CheckFileExists = true
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    var folder = Path.GetDirectoryName(currentPath.Replace("/", "\\"));
                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                    {
                        dialog.InitialDirectory = folder;
                    }
                }
            }
            catch
            {
            }

            return dialog.ShowDialog() == true
                ? dialog.FileName.Replace("\\", "/")
                : null;
        }

        private void BrowseHubAppBackground(int slot)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;

            if (vm == null)
            {
                return;
            }

            var title = FindResource("HubApps_SelectBackgroundDialog") as string ?? "Select a background image for this Hub app card.";
            string currentPath;

            switch (slot)
            {
                case 1:
                    currentPath = vm.Settings.HubAppSlot1BackgroundPath;
                    break;
                case 2:
                    currentPath = vm.Settings.HubAppSlot2BackgroundPath;
                    break;
                case 3:
                    currentPath = vm.Settings.HubAppSlot3BackgroundPath;
                    break;
                case 4:
                    currentPath = vm.Settings.HubAppSlot4BackgroundPath;
                    break;
                default:
                    return;
            }

            var selectedPath = SelectImageFile(title, currentPath);

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            switch (slot)
            {
                case 1:
                    vm.Settings.HubAppSlot1BackgroundPath = selectedPath;
                    break;
                case 2:
                    vm.Settings.HubAppSlot2BackgroundPath = selectedPath;
                    break;
                case 3:
                    vm.Settings.HubAppSlot3BackgroundPath = selectedPath;
                    break;
                case 4:
                    vm.Settings.HubAppSlot4BackgroundPath = selectedPath;
                    break;
            }
        }

        private void BrowseHubAppSlot1Background_Click(object sender, RoutedEventArgs e)
        {
            BrowseHubAppBackground(1);
        }

        private void BrowseHubAppSlot2Background_Click(object sender, RoutedEventArgs e)
        {
            BrowseHubAppBackground(2);
        }

        private void BrowseHubAppSlot3Background_Click(object sender, RoutedEventArgs e)
        {
            BrowseHubAppBackground(3);
        }

        private void BrowseHubAppSlot4Background_Click(object sender, RoutedEventArgs e)
        {
            BrowseHubAppBackground(4);
        }

        private void BrowseFilterIconsFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;

            if (vm == null)
            {
                return;
            }

            var selectedPath = SelectFolder(
                FindResource("CustomIcons_SelectFilterFolderDialog") as string ?? "Select the folder containing your filter PNG icons.",
                vm.Settings.CustomFilterIconsFolder
            );

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                vm.Settings.CustomFilterIconsFolder = selectedPath;
            }
        }

        private void BrowseSourceIconsFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;

            if (vm == null)
            {
                return;
            }

            var selectedPath = SelectFolder(
                FindResource("CustomIcons_SelectSourceFolderDialog") as string ?? "Select the folder containing your source PNG icons.",
                vm.Settings.CustomSourceIconsFolder
            );

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                vm.Settings.CustomSourceIconsFolder = selectedPath;
            }
        }

        private void BrowseBannerAboveCoverFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;

            if (vm == null)
            {
                return;
            }

            var selectedPath = SelectFolder(
                FindResource("CustomIcons_SelectBannerAboveCoverFolderDialog") as string ?? "Select the folder containing your above-cover banner PNG images.",
                vm.Settings.CustomBannerAboveCoverFolder
            );

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                vm.Settings.CustomBannerAboveCoverFolder = selectedPath;
            }
        }

        private void BrowseBannerOnCoverFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;

            if (vm == null)
            {
                return;
            }

            var selectedPath = SelectFolder(
                FindResource("CustomIcons_SelectBannerOnCoverFolderDialog") as string ?? "Select the folder containing your on-cover banner PNG images.",
                vm.Settings.CustomBannerOnCoverFolder
            );

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                vm.Settings.CustomBannerOnCoverFolder = selectedPath;
            }
        }

        private async void InitializeSteamCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var api = vm?.Api;
                if (vm == null || api == null)
                {
                    return;
                }

                var confirmText = (string)Application.Current.TryFindResource("ConfirmInitSteamCache")
                                  ?? "This will scan your library and initialize the Steam update cache for all Steam games. Continue?";

                var res = api.Dialogs.ShowMessage(
                    confirmText,
                    "Aniki Helper",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes)
                {
                    return;
                }

                await vm.InitializeSteamUpdatesCacheAsync();

                var doneText = (string)Application.Current.TryFindResource("InitSteamCacheDoneMsg")
                               ?? "Done! Steam update cache has been initialized.";

                api.Dialogs.ShowMessage(
                    doneText,
                    "Aniki Helper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var api = (DataContext as AnikiHelperSettingsViewModel)?.Api;
                if (api != null)
                {
                    api.Dialogs.ShowErrorMessage("Error while initializing Steam cache:\n" + ex.Message, "Aniki Helper");
                }
                else
                {
                    MessageBox.Show("Error while initializing Steam cache:\n" + ex.Message, "Aniki Helper");
                }
            }
        }
    }
}
