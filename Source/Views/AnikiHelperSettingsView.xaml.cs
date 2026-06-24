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

        private void ResetSnapshot_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.ResetMonthlySnapshot();
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
