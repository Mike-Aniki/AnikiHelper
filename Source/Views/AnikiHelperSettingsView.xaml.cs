using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Forms = System.Windows.Forms;

namespace AnikiHelper
{
    public partial class AnikiHelperSettingsView : UserControl
    {
        // Public Aniki Pack Creator release page.
        private const string PackCreatorDownloadUrl = "https://github.com/Mike-Aniki/AnikiPackCreator/releases/latest";
        private const string PackCreatorExecutableName = "AnikiPackCreator.exe";

        private bool isUpdatingVideoTmdbTokenPasswordBox;
        private CancellationTokenSource videoArtworkScanCts;

        public AnikiHelperSettingsView()
        {
            InitializeComponent();

            LoadLocaleFromCurrentUICulture();

            Loaded += AnikiHelperSettingsView_Loaded;
            DataContextChanged += AnikiHelperSettingsView_DataContextChanged;
        }

        private void AnikiHelperSettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var vm = DataContext as AnikiHelperSettingsViewModel;
                    vm?.RefreshHomeDashboard();
                    vm?.RefreshCustomVisualPackLibrary();
                    vm?.RefreshCustomColorPackLibrary();
                    vm?.RefreshLoginPackLibrary();
                    vm?.RefreshSoundPackLibrary();
                    vm?.RefreshCompletePackLibrary();
                    vm?.RefreshLoginBackgroundMediaState();
                    vm?.Settings?.LoadOverlayApps();
                    if (vm?.Settings != null)
                    {
                        TryAutoFillVideoFfprobePath(vm.Settings);
                        SyncVideoTmdbTokenPasswordBox();
                        vm.Settings.VideoPlayer?.RefreshThumbnailDiagnostics();
                    }
                    vm?.Settings?.VideoPlayer?.RefreshThumbnailDiagnostics();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch
            {
            }
        }


        private void AnikiHelperSettingsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(SyncVideoTmdbTokenPasswordBox),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch
            {
            }
        }

        private void SyncVideoTmdbTokenPasswordBox()
        {
            try
            {
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                if (settings == null || VideoTmdbTokenPasswordBox == null)
                {
                    return;
                }

                var token = settings.VideoTmdbReadAccessToken ?? string.Empty;
                if (string.Equals(VideoTmdbTokenPasswordBox.Password, token, StringComparison.Ordinal))
                {
                    return;
                }

                isUpdatingVideoTmdbTokenPasswordBox = true;
                try
                {
                    VideoTmdbTokenPasswordBox.Password = token;
                }
                finally
                {
                    isUpdatingVideoTmdbTokenPasswordBox = false;
                }
            }
            catch
            {
                isUpdatingVideoTmdbTokenPasswordBox = false;
            }
        }

        private void VideoTmdbTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (isUpdatingVideoTmdbTokenPasswordBox)
            {
                return;
            }

            try
            {
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                if (settings != null)
                {
                    settings.VideoTmdbReadAccessToken = VideoTmdbTokenPasswordBox?.Password ?? string.Empty;
                }
            }
            catch
            {
            }
        }


        private void ImportLoginPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("LoginPack_ImportDialogTitle", "Import a Login Pack"),
                    Filter = "Login Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        GetResourceText("LoginPack_ImportConfirm", "Add this ZIP to the Login Pack library?"),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question)
                    : MessageBox.Show(
                        GetResourceText("LoginPack_ImportConfirm", "Add this ZIP to the Login Pack library?"),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = vm.ImportLoginPack(dialog.FileName);
                var packName = string.IsNullOrWhiteSpace(result?.PackName)
                    ? Path.GetFileNameWithoutExtension(dialog.FileName)
                    : result.PackName;
                var authorSuffix = string.IsNullOrWhiteSpace(result?.Author)
                    ? string.Empty
                    : " — " + result.Author;

                var key = result?.WasUpdated == true
                    ? "LoginPack_UpdateSuccess"
                    : result?.WasAlreadyInLibrary == true
                        ? "LoginPack_AlreadyInstalled"
                        : "LoginPack_ImportSuccess";
                var fallback = result?.WasUpdated == true
                    ? "Login Pack '{0}' was updated to version {1}."
                    : result?.WasAlreadyInLibrary == true
                        ? "Login Pack '{0}' is already installed."
                        : "Login Pack '{0}' was added to the library.";

                ShowInformation(string.Format(
                    GetResourceText(key, fallback),
                    packName + authorSuffix,
                    result?.Version ?? string.Empty));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("LoginPack_ImportError", "The Login Pack could not be imported:") +
                    "\n" + ex.Message);
            }
        }

        private void ExportLoginPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as LoginPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = GetResourceText("LoginPack_ExportDialogTitle", "Export Login Pack"),
                    Filter = "Login Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    AddExtension = true,
                    FileName = GetSafeLoginPackFileName(pack.Name) + ".zip"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                vm.ExportLoginPack(pack.Id, dialog.FileName);
                ShowInformation(string.Format(
                    GetResourceText("LoginPack_ExportSuccess", "Login Pack '{0}' exported successfully."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("LoginPack_ExportError", "The Login Pack could not be exported:") +
                    "\n" + ex.Message);
            }
        }

        private void DeleteLoginPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as LoginPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var confirmationText = string.Format(
                    GetResourceText("LoginPack_DeleteConfirm", "Delete Login Pack '{0}' from the library?"),
                    pack.Name);
                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning)
                    : MessageBox.Show(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.DeleteLoginPack(pack.Id);
                ShowInformation(string.Format(
                    GetResourceText("LoginPack_DeleteSuccess", "Login Pack '{0}' was deleted."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("LoginPack_DeleteError", "The Login Pack could not be deleted:") +
                    "\n" + ex.Message);
            }
        }

        private static string GetSafeLoginPackFileName(string name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "LoginPack" : name.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "LoginPack" : value;
        }

        private void ImportSoundPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("SoundPack_ImportDialogTitle", "Import a Sound Pack"),
                    Filter = "Sound Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        GetResourceText("SoundPack_ImportConfirm", "Add this ZIP to the Sound Pack library?"),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question)
                    : MessageBox.Show(
                        GetResourceText("SoundPack_ImportConfirm", "Add this ZIP to the Sound Pack library?"),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = vm.ImportSoundPack(dialog.FileName);
                var packName = string.IsNullOrWhiteSpace(result?.PackName)
                    ? Path.GetFileNameWithoutExtension(dialog.FileName)
                    : result.PackName;
                var authorSuffix = string.IsNullOrWhiteSpace(result?.Author)
                    ? string.Empty
                    : " — " + result.Author;

                var key = result?.WasUpdated == true
                    ? "SoundPack_UpdateSuccess"
                    : result?.WasAlreadyInLibrary == true
                        ? "SoundPack_AlreadyInstalled"
                        : "SoundPack_ImportSuccess";
                var fallback = result?.WasUpdated == true
                    ? "Sound Pack '{0}' was updated to version {1}."
                    : result?.WasAlreadyInLibrary == true
                        ? "Sound Pack '{0}' is already installed."
                        : "Sound Pack '{0}' was added to the library.";

                ShowInformation(string.Format(
                    GetResourceText(key, fallback),
                    packName + authorSuffix,
                    result?.Version ?? string.Empty));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("SoundPack_ImportError", "The Sound Pack could not be imported:") +
                    "\n" + ex.Message);
            }
        }

        private void ExportSoundPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as SoundPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = GetResourceText("SoundPack_ExportDialogTitle", "Export Sound Pack"),
                    Filter = "Sound Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    AddExtension = true,
                    FileName = GetSafeSoundPackFileName(pack.Name) + ".zip"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                vm.ExportSoundPack(pack.Id, dialog.FileName);
                ShowInformation(string.Format(
                    GetResourceText("SoundPack_ExportSuccess", "Sound Pack '{0}' exported successfully."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("SoundPack_ExportError", "The Sound Pack could not be exported:") +
                    "\n" + ex.Message);
            }
        }

        private void DeleteSoundPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as SoundPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var confirmationText = string.Format(
                    GetResourceText("SoundPack_DeleteConfirm", "Delete Sound Pack '{0}' from the library?"),
                    pack.Name);
                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning)
                    : MessageBox.Show(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.DeleteSoundPack(pack.Id);
                ShowInformation(string.Format(
                    GetResourceText("SoundPack_DeleteSuccess", "Sound Pack '{0}' was deleted."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("SoundPack_DeleteError", "The Sound Pack could not be deleted:") +
                    "\n" + ex.Message);
            }
        }

        private static string GetSafeSoundPackFileName(string name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "SoundPack" : name.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "SoundPack" : value;
        }

        private void ImportCompletePack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("CompletePack_ImportDialogTitle", "Import a Complete Pack"),
                    Filter = "Complete Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        GetResourceText("CompletePack_ImportConfirm", "Add this ZIP to the Complete Pack library? Nothing is applied until you choose Apply."),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question)
                    : MessageBox.Show(
                        GetResourceText("CompletePack_ImportConfirm", "Add this ZIP to the Complete Pack library? Nothing is applied until you choose Apply."),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = vm.ImportCompletePack(dialog.FileName);
                var packName = string.IsNullOrWhiteSpace(result?.PackName)
                    ? Path.GetFileNameWithoutExtension(dialog.FileName)
                    : result.PackName;
                var authorSuffix = string.IsNullOrWhiteSpace(result?.Author)
                    ? string.Empty
                    : " — " + result.Author;

                var key = result?.WasUpdated == true
                    ? "CompletePack_UpdateSuccess"
                    : result?.WasAlreadyInLibrary == true
                        ? "CompletePack_AlreadyInstalled"
                        : "CompletePack_ImportSuccess";
                var fallback = result?.WasUpdated == true
                    ? "Complete Pack '{0}' was updated to version {1}."
                    : result?.WasAlreadyInLibrary == true
                        ? "Complete Pack '{0}' is already installed."
                        : "Complete Pack '{0}' was added to the library.";

                ShowInformation(string.Format(
                    GetResourceText(key, fallback),
                    packName + authorSuffix,
                    result?.Version ?? string.Empty));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("CompletePack_ImportError", "The Complete Pack could not be imported:") +
                    "\n" + ex.Message);
            }
        }

        private void ApplyCompletePack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as CompletePackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var confirmationText = string.Format(
                    GetResourceText(
                        "CompletePack_ApplyConfirm",
                        "Apply Complete Pack '{0}'? Included components replace their current selections. Components not included are left unchanged."),
                    pack.Name);
                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question)
                    : MessageBox.Show(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.ApplyCompletePack(pack.Id);
                global::AnikiHelper.AnikiHelper.Instance?.ShowAnikiThemeSettingsRestartPromptIfNeeded();
                ShowInformation(string.Format(
                    GetResourceText("CompletePack_ApplySuccess", "Complete Pack '{0}' was applied."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("CompletePack_ApplyError", "The Complete Pack could not be applied:") +
                    "\n" + ex.Message);
            }
        }

        private void ExportCompletePack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as CompletePackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = GetResourceText("CompletePack_ExportDialogTitle", "Export Complete Pack"),
                    Filter = "Complete Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    AddExtension = true,
                    FileName = GetSafeCompletePackFileName(pack.Name) + ".zip"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                vm.ExportCompletePack(pack.Id, dialog.FileName);
                ShowInformation(string.Format(
                    GetResourceText("CompletePack_ExportSuccess", "Complete Pack '{0}' exported successfully."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("CompletePack_ExportError", "The Complete Pack could not be exported:") +
                    "\n" + ex.Message);
            }
        }

        private void DeleteCompletePack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as CompletePackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var choiceText = string.Format(
                    GetResourceText(
                        "CompletePack_DeleteChoice",
                        "Delete Complete Pack '{0}'?\n\nYES = Delete the Complete Pack and all of its installed Visual, Color, Login and Sound Packs.\n\nNO = Delete the Complete Pack only and keep its included packs installed.\n\nCANCEL = Keep everything."),
                    pack.Name);

                var choice = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        choiceText,
                        "Aniki Helper",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning)
                    : MessageBox.Show(
                        choiceText,
                        "Aniki Helper",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.None)
                {
                    return;
                }

                var deleteIncludedPacks = choice == MessageBoxResult.Yes;
                if (deleteIncludedPacks)
                {
                    var analysis = vm.AnalyzeCompletePackDelete(pack.Id);
                    if (analysis?.HasSharedComponents == true)
                    {
                        var sharedPackNames = string.Join(
                            Environment.NewLine,
                            analysis.SharedWithCompletePackNames.Select(x => "• " + x));
                        var sharedWarning = string.Format(
                            GetResourceText(
                                "CompletePack_DeleteSharedWarning",
                                "Some included packs are also used by these installed Complete Packs:\n\n{0}\n\nDeleting the included packs will remove those installed component copies too. The other Complete Packs remain in your library and can reinstall their components if you apply them again.\n\nContinue?"),
                            sharedPackNames);

                        var sharedConfirmation = vm.Api != null
                            ? vm.Api.Dialogs.ShowMessage(
                                sharedWarning,
                                "Aniki Helper",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning)
                            : MessageBox.Show(
                                sharedWarning,
                                "Aniki Helper",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                        if (sharedConfirmation != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }
                }

                vm.DeleteCompletePack(pack.Id, deleteIncludedPacks);
                ShowInformation(string.Format(
                    deleteIncludedPacks
                        ? GetResourceText("CompletePack_DeleteSuccessWithComponents", "Complete Pack '{0}' and its included packs were deleted.")
                        : GetResourceText("CompletePack_DeleteSuccess", "Complete Pack '{0}' was deleted."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("CompletePack_DeleteError", "The Complete Pack could not be deleted:") +
                    "\n" + ex.Message);
            }
        }

        private static string GetSafeCompletePackFileName(string name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "CompletePack" : name.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "CompletePack" : value;
        }

        private void ClearDownloadedLoginVideos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null || !vm.HasDownloadedLoginVideos)
                {
                    return;
                }

                var message = GetResourceText(
                    "DownloadedLoginVideos_ClearConfirm",
                    "Delete all downloaded login backgrounds? The default background is kept.");

                var result = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(message, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    : MessageBox.Show(message, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.ClearDownloadedLoginBackgroundVideos();
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("DownloadedLoginVideos_ClearError", "Downloaded login backgrounds could not be deleted:") +
                    "\n" + ex.Message);
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
                MainSettingsTabs.SelectedIndex = 3;
            }
        }

        private void ConfigureFullscreenTools_Click(object sender, RoutedEventArgs e)
        {
            if (MainSettingsTabs != null)
            {
                MainSettingsTabs.SelectedIndex = 4;
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

        private void OpenVisualPackCreator_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var path = ResolvePackCreatorPath(vm?.Settings);

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    vm?.RefreshVisualPackCreatorState();
                    ShowError(GetResourceText(
                        "VisualPackCreator_OpenMissing",
                        "The Aniki Pack Creator executable could not be found. Locate it again."));
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("VisualPackCreator_OpenError", "The Aniki Pack Creator could not be opened:") +
                    "\n" + ex.Message);
            }
        }

        private string ResolvePackCreatorPath(AnikiHelperSettings settings)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            var configuredPath = settings.VisualPackCreatorPath;
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            // Migration from the former AnikiVisualPackCreator.exe name.
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                try
                {
                    var directory = Path.GetDirectoryName(configuredPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        var renamedExecutable = Path.Combine(directory, PackCreatorExecutableName);
                        if (File.Exists(renamedExecutable))
                        {
                            settings.VisualPackCreatorPath = renamedExecutable;
                            (DataContext as AnikiHelperSettingsViewModel)?.RefreshVisualPackCreatorState();
                            return renamedExecutable;
                        }
                    }
                }
                catch
                {
                }
            }

            return configuredPath ?? string.Empty;
        }

        private void LocateVisualPackCreator_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm?.Settings == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("VisualPackCreator_LocateDialogTitle", "Locate Aniki Pack Creator"),
                    Filter = "Aniki Pack Creator (AnikiPackCreator.exe)|AnikiPackCreator.exe|Executable files (*.exe)|*.exe",
                    CheckFileExists = true,
                    Multiselect = false
                };

                var currentPath = vm.Settings.VisualPackCreatorPath;
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    try
                    {
                        var currentDirectory = Path.GetDirectoryName(currentPath);
                        if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
                        {
                            dialog.InitialDirectory = currentDirectory;
                        }
                    }
                    catch
                    {
                    }
                }

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                vm.Settings.VisualPackCreatorPath = dialog.FileName;
                vm.RefreshVisualPackCreatorState();
                vm.EndEdit();
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("VisualPackCreator_LocateError", "The Aniki Pack Creator location could not be saved:") +
                    "\n" + ex.Message);
            }
        }

        private void DownloadVisualPackCreator_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PackCreatorDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("VisualPackCreator_DownloadError", "The Aniki Pack Creator download page could not be opened:") +
                    "\n" + ex.Message);
            }
        }

        private void OpenCommunityPacks_Click(object sender, RoutedEventArgs e)
        {
            var packType = (sender as FrameworkElement)?.Tag as string ?? "visual";
            try
            {
                (DataContext as AnikiHelperSettingsViewModel)?.OpenCommunityPacksBrowser(packType);
            }
            catch (Exception ex)
            {
                (DataContext as AnikiHelperSettingsViewModel)?.Api?.Dialogs?.ShowErrorMessage(
                    GetResourceText("CommunityPack_OpenError", "The Community Packs browser could not be opened:") +
                    Environment.NewLine + ex.Message,
                    GetResourceText("CommunityPack_GenericTitle", "Community Packs"));
            }
        }

        private void ImportCustomVisualPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("VisualPackCustom_ImportDialogTitle", "Import a Custom Visual Pack"),
                    Filter = "Visual Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var confirmText = GetResourceText(
                    "VisualPackCustom_ImportConfirm",
                    "Add this ZIP to the Visual Pack library?");

                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = vm.ImportCustomVisualPack(dialog.FileName);
                var packName = string.IsNullOrWhiteSpace(result?.PackName)
                    ? Path.GetFileNameWithoutExtension(dialog.FileName)
                    : result.PackName;

                var authorSuffix = string.IsNullOrWhiteSpace(result?.Author)
                    ? string.Empty
                    : " — " + result.Author;

                var messageKey = result?.WasUpdated == true
                    ? "VisualPackLibrary_UpdateSuccess"
                    : result?.WasAlreadyInLibrary == true
                        ? "VisualPackLibrary_DuplicateApplied"
                        : "VisualPackCustom_ImportSuccess";
                var fallback = result?.WasUpdated == true
                    ? "Visual Pack '{0}' was updated. The installed version is now {1}."
                    : result?.WasAlreadyInLibrary == true
                        ? "Visual Pack '{0}' was already in the library. Select it in Fullscreen under Custom > Selected Visual Pack to use it."
                        : "Visual Pack '{0}' was added to the library. Select it in Fullscreen under Custom > Selected Visual Pack to use it.";

                ShowInformation(string.Format(
                    GetResourceText(messageKey, fallback),
                    packName + authorSuffix,
                    result?.Version ?? string.Empty));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("VisualPackCustom_ImportError", "The Custom Visual Pack could not be imported:") +
                    "\n" + ex.Message);
            }
        }

        private void ApplyCustomVisualPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as VisualPackLibraryViewItem;
                if (vm == null || pack == null || pack.IsActive)
                {
                    return;
                }

                vm.ApplyCustomVisualPack(pack.Id);

                ShowInformation(string.Format(
                    GetResourceText(
                        "VisualPackLibrary_ApplySuccess",
                        "Visual Pack '{0}' is now active. It will be used when Custom is selected."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("VisualPackLibrary_ApplyError", "The Visual Pack could not be applied:") +
                    "\n" + ex.Message);
            }
        }

        private void ExportCustomVisualPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as VisualPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = GetResourceText("VisualPackLibrary_ExportDialogTitle", "Export Visual Pack"),
                    Filter = "Visual Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    AddExtension = true,
                    FileName = GetSafeVisualPackFileName(pack.Name) + ".zip"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                vm.ExportCustomVisualPack(pack.Id, dialog.FileName);

                ShowInformation(string.Format(
                    GetResourceText("VisualPackLibrary_ExportSuccess", "Visual Pack '{0}' exported successfully."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("VisualPackLibrary_ExportError", "The Visual Pack could not be exported:") +
                    "\n" + ex.Message);
            }
        }

        private void DeleteCustomVisualPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as VisualPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var confirmText = string.Format(
                    GetResourceText("VisualPackLibrary_DeleteConfirm", "Delete Visual Pack '{0}' from the library?"),
                    pack.Name);

                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    : MessageBox.Show(confirmText, "Aniki Helper", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.DeleteCustomVisualPack(pack.Id);

                ShowInformation(string.Format(
                    GetResourceText("VisualPackLibrary_DeleteSuccess", "Visual Pack '{0}' was deleted."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("VisualPackLibrary_DeleteError", "The Visual Pack could not be deleted:") +
                    "\n" + ex.Message);
            }
        }

        private void ImportCustomColorPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                if (vm == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("ColorPack_ImportDialogTitle", "Import a Color Pack"),
                    Filter = "Color Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        GetResourceText("ColorPack_ImportConfirm", "Add this ZIP to the Color Pack library?"),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question)
                    : MessageBox.Show(
                        GetResourceText("ColorPack_ImportConfirm", "Add this ZIP to the Color Pack library?"),
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = vm.ImportCustomColorPack(dialog.FileName);
                var packName = string.IsNullOrWhiteSpace(result?.PackName)
                    ? Path.GetFileNameWithoutExtension(dialog.FileName)
                    : result.PackName;
                var authorSuffix = string.IsNullOrWhiteSpace(result?.Author)
                    ? string.Empty
                    : " — " + result.Author;

                var key = result?.WasUpdated == true
                    ? "ColorPack_UpdateSuccess"
                    : result?.WasAlreadyInLibrary == true
                        ? "ColorPack_AlreadyInstalled"
                        : "ColorPack_ImportSuccess";
                var fallback = result?.WasUpdated == true
                    ? "Color Pack '{0}' was updated to version {1}."
                    : result?.WasAlreadyInLibrary == true
                        ? "Color Pack '{0}' is already installed."
                        : "Color Pack '{0}' was added to the library.";

                ShowInformation(string.Format(
                    GetResourceText(key, fallback),
                    packName + authorSuffix,
                    result?.Version ?? string.Empty));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("ColorPack_ImportError", "The Color Pack could not be imported:") +
                    "\n" + ex.Message);
            }
        }

        private void ExportCustomColorPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as ColorPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = GetResourceText("ColorPack_ExportDialogTitle", "Export Color Pack"),
                    Filter = "Color Pack ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    AddExtension = true,
                    FileName = GetSafeColorPackFileName(pack.Name) + ".zip"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                vm.ExportCustomColorPack(pack.Id, dialog.FileName);
                ShowInformation(string.Format(
                    GetResourceText("ColorPack_ExportSuccess", "Color Pack '{0}' exported successfully."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("ColorPack_ExportError", "The Color Pack could not be exported:") +
                    "\n" + ex.Message);
            }
        }

        private void DeleteCustomColorPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var pack = (sender as FrameworkElement)?.DataContext as ColorPackLibraryViewItem;
                if (vm == null || pack == null)
                {
                    return;
                }

                var confirmationText = string.Format(
                    GetResourceText("ColorPack_DeleteConfirm", "Delete Color Pack '{0}' from the library?"),
                    pack.Name);
                var confirmation = vm.Api != null
                    ? vm.Api.Dialogs.ShowMessage(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning)
                    : MessageBox.Show(
                        confirmationText,
                        "Aniki Helper",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.DeleteCustomColorPack(pack.Id);
                ShowInformation(string.Format(
                    GetResourceText("ColorPack_DeleteSuccess", "Color Pack '{0}' was deleted."),
                    pack.Name));
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText("ColorPack_DeleteError", "The Color Pack could not be deleted:") +
                    "\n" + ex.Message);
            }
        }

        private static string GetSafeColorPackFileName(string name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "ColorPack" : name.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "ColorPack" : value;
        }

        private static string GetSafeVisualPackFileName(string name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "VisualPack" : name.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "VisualPack" : value;
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

        private void ClearPlayniteWebCache_Click(object sender, RoutedEventArgs e)
        {
            ExitEventHandler restartHandler = null;
            Application application = null;

            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var api = vm?.Api;
                if (vm == null || api == null)
                {
                    return;
                }

                var title = GetResourceText(
                    "SteamAccount_ClearWebCacheTitle",
                    "Steam login keeps refreshing?");

                var confirmText = GetResourceText(
                    "SteamAccount_ClearWebCacheConfirm",
                    "Playnite must restart to clear its shared web cache. Browser-based integrations may ask you to sign in again. Continue?");

                var restartErrorText = GetResourceText(
                    "SteamAccount_ClearWebCacheError",
                    "Unable to restart Playnite and clear the web cache. Use Playnite Settings → Advanced → Clear web cache.");

                var result = api.Dialogs.ShowMessage(
                    confirmText,
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                // Save pending Aniki Helper changes before Playnite closes.
                vm.EndEdit();

                string playniteExecutable = null;
                using (var currentProcess = Process.GetCurrentProcess())
                {
                    try
                    {
                        playniteExecutable = currentProcess.MainModule?.FileName;
                    }
                    catch
                    {
                    }
                }

                if (string.IsNullOrWhiteSpace(playniteExecutable))
                {
                    var applicationPath = api.Paths?.ApplicationPath;
                    if (!string.IsNullOrWhiteSpace(applicationPath))
                    {
                        playniteExecutable = Path.Combine(applicationPath, "Playnite.DesktopApp.exe");
                    }
                }

                if (string.IsNullOrWhiteSpace(playniteExecutable) || !File.Exists(playniteExecutable))
                {
                    throw new FileNotFoundException(
                        "Could not locate the current Playnite executable.",
                        playniteExecutable);
                }

                var playniteArguments = "--clearwebcache --nolibupdate --masterinstance";
                var configurationPath = api.Paths?.ConfigurationPath;
                if (!string.IsNullOrWhiteSpace(configurationPath))
                {
                    playniteArguments += " --userdatadir \"" + configurationPath + "\"";
                }

                var executableToRestart = playniteExecutable;
                var argumentsToRestart = playniteArguments;
                var workingDirectory = Path.GetDirectoryName(playniteExecutable);

                application = Application.Current;
                if (application == null)
                {
                    throw new InvalidOperationException("The Playnite application instance is unavailable.");
                }

                // Playnite releases CEF and its single-instance resources in its Exit handler.
                // This handler was registered later, so the new instance starts immediately after
                // those resources have been released.
                restartHandler = (exitSender, exitArgs) =>
                {
                    try
                    {
                        application.Exit -= restartHandler;

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = executableToRestart,
                            Arguments = argumentsToRestart,
                            WorkingDirectory = workingDirectory,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception launchException)
                    {
                        MessageBox.Show(
                            restartErrorText + "\n\n" + launchException.Message,
                            "Aniki Helper",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };

                application.Exit += restartHandler;
                application.Shutdown();
            }
            catch (Exception ex)
            {
                if (application != null && restartHandler != null)
                {
                    try
                    {
                        application.Exit -= restartHandler;
                    }
                    catch
                    {
                    }
                }

                ShowError(
                    GetResourceText(
                        "SteamAccount_ClearWebCacheError",
                        "Unable to restart Playnite and clear the web cache. Use Playnite Settings → Advanced → Clear web cache.") +
                    "\n\n" + ex.Message);
            }
        }

        private void RestoreDefaultSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as AnikiHelperSettingsViewModel;
                var api = vm?.Api;
                if (vm == null || api == null)
                {
                    return;
                }

                var result = api.Dialogs.ShowMessage(
                    GetResourceText(
                        "SettingsReset_Confirm",
                        "Restore all Aniki Helper options to their default values? Your connected accounts and personal data will be kept."),
                    GetResourceText("SettingsReset_SectionTitle", "Restore default settings"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                vm.RestoreDefaultPluginOptions();

                api.Dialogs.ShowMessage(
                    GetResourceText(
                        "SettingsReset_Success",
                        "Aniki Helper options have been restored to their default values."),
                    "Aniki Helper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError(
                    GetResourceText(
                        "SettingsReset_Error",
                        "Unable to restore the default settings.") +
                    "\n\n" + ex.Message);
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

        private void ResetSplashMaxDuration_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnikiHelperSettingsViewModel vm)
            {
                vm.Settings.GameLaunchSplashMaximumWaitMs = AnikiHelperSettings.DefaultGameLaunchSplashMaximumWaitMs;
            }
        }

        private void ResetSplashBackgroundDimming_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnikiHelperSettingsViewModel vm)
            {
                vm.Settings.GameLaunchSplashBackgroundDimming = AnikiHelperSettings.DefaultGameLaunchSplashBackgroundDimming;
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

        private void AddVideoLibraryPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var key = (sender as FrameworkElement)?.Tag?.ToString() ?? string.Empty;
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                settings?.AddVideoLibraryPath(key);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ToggleVideoLibraryOptions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var entry = (sender as FrameworkElement)?.DataContext as AnikiVideoLibraryPathEntry;
                if (entry != null)
                {
                    entry.ShowOptions = !entry.ShowOptions;
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void BrowseVideoLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var element = sender as FrameworkElement;
                var key = element?.Tag?.ToString() ?? string.Empty;
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                var entry = element?.DataContext as AnikiVideoLibraryPathEntry;
                if (settings == null || entry == null)
                {
                    return;
                }

                string title;
                switch (key)
                {
                    case "movies":
                        title = GetResourceText("VideoLibraries_BrowseMovies", "Choose your Movies folder");
                        break;
                    case "series":
                        title = GetResourceText("VideoLibraries_BrowseSeries", "Choose your TV Shows folder");
                        break;
                    case "anime":
                        title = GetResourceText("VideoLibraries_BrowseAnime", "Choose your Anime folder");
                        break;
                    case "custom":
                        title = GetResourceText("VideoLibraries_BrowseCustom", "Choose your Custom library folder");
                        break;
                    default:
                        return;
                }

                var selected = SelectNetworkFolder(title, entry.Path ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    entry.Path = selected;
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void RemoveVideoLibraryPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var element = sender as FrameworkElement;
                var key = element?.Tag?.ToString() ?? string.Empty;
                var entry = element?.DataContext as AnikiVideoLibraryPathEntry;
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                settings?.RemoveVideoLibraryPath(key, entry);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OpenVideoCenterLibraryManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                (DataContext as AnikiHelperSettingsViewModel)?.OpenVideoCenterLibraryManager();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OpenVideoCenterIntroEndingManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                (DataContext as AnikiHelperSettingsViewModel)?.OpenVideoCenterIntroEndingManager();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void VideoArtworkScan_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;
            var settings = vm?.Settings;
            var player = settings?.VideoPlayer;
            var api = vm?.Api;
            if (player == null || api == null)
            {
                ShowError(GetResourceText("VideoArtworkScan_Error", "Artwork scan could not be started."));
                return;
            }

            if (!settings.GetVideoLibraryPaths("movies").Any() &&
                !settings.GetVideoLibraryPaths("series").Any() &&
                !settings.GetVideoLibraryPaths("anime").Any())
            {
                ShowInformation(GetResourceText("VideoArtworkScan_NoLibraries", "Configure at least one Video Center library before starting a scan."));
                return;
            }

            Services.VideoPlayer.AnikiVideoArtworkScanResult scanResult = null;
            Exception scanError = null;
            var cancelled = false;

            api.Dialogs.ActivateGlobalProgress(async args =>
            {
                args.IsIndeterminate = true;
                args.Text = GetResourceText("VideoArtworkScan_Preparing", "Scanning libraries...");

                var progress = new Progress<Services.VideoPlayer.AnikiVideoArtworkScanProgress>(p =>
                {
                    args.IsIndeterminate = p.TotalItems <= 0;
                    args.ProgressMaxValue = Math.Max(1, p.TotalItems);
                    args.CurrentProgressValue = Math.Min(Math.Max(0, p.ProcessedItems), Math.Max(1, p.TotalItems));
                    var current = string.IsNullOrWhiteSpace(p.CurrentItem) ? string.Empty : "  •  " + p.CurrentItem;
                    args.Text = string.Format(
                        GetResourceText("VideoArtworkScan_ProgressWindow", "Scanning artwork... {0}/{1}  •  Cover +{2}  •  Landscape +{3}  •  Wallpaper +{4}  •  Logo +{5}{6}"),
                        p.ProcessedItems,
                        p.TotalItems,
                        p.CoversFound,
                        p.LandscapesFound,
                        p.HeroesFound,
                        p.LogosFound,
                        current);
                });

                try
                {
                    scanResult = await player.ScanMissingLibraryArtworkAsync(progress, args.CancelToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    scanError = ex;
                }
            }, new Playnite.SDK.GlobalProgressOptions(
                GetResourceText("VideoArtworkScan_Preparing", "Scanning libraries..."),
                true)
            {
                IsIndeterminate = true
            });

            if (scanError != null)
            {
                ShowError(GetResourceText("VideoArtworkScan_Error", "Artwork scan could not be completed.") + "\n" + scanError.Message);
                return;
            }
            if (cancelled || scanResult == null)
            {
                ShowInformation(GetResourceText("VideoArtworkScan_Cancelled", "Artwork scan cancelled."));
                return;
            }

            var summary = string.Format(
                GetResourceText("VideoArtworkScan_ResultAssets", "{0} media scanned. +{1} covers, +{2} landscapes, +{3} wallpapers, +{4} logos. {5} complete, {6} incomplete, {7} failed."),
                scanResult.TotalItems,
                scanResult.CoversFound,
                scanResult.LandscapesFound,
                scanResult.HeroesFound,
                scanResult.LogosFound,
                scanResult.CompleteItems,
                scanResult.IncompleteItems,
                scanResult.FailedItems);
            if (scanResult.UnavailableLibraries > 0)
            {
                summary += "\n" + string.Format(
                    GetResourceText("VideoArtworkScan_UnavailableLibraries", "Unavailable libraries: {0}."),
                    scanResult.UnavailableLibraries);
            }
            ShowInformation(GetResourceText("VideoArtworkScan_Completed", "Artwork scan completed.") + "\n\n" + summary);
        }

        private void GenerateMissingVideoThumbnails_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;
            var settings = vm?.Settings;
            var player = settings?.VideoPlayer;
            var api = vm?.Api;
            if (player == null || api == null)
            {
                ShowError(GetResourceText("VideoThumbnail_GenerateError", "Thumbnail generation could not be started."));
                return;
            }

            var ffmpeg = (settings.VideoThumbnailFfmpegPath ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
            {
                ShowInformation(GetResourceText("VideoThumbnail_GenerateNeedsFfmpeg", "Configure a valid FFmpeg path before generating thumbnails."));
                return;
            }

            if (!settings.GetVideoLibraryPaths("movies").Any() &&
                !settings.GetVideoLibraryPaths("series").Any() &&
                !settings.GetVideoLibraryPaths("anime").Any())
            {
                ShowInformation(GetResourceText("VideoArtworkScan_NoLibraries", "Configure at least one Video Center library before starting a scan."));
                return;
            }

            Services.VideoPlayer.AnikiVideoThumbnailGenerationResult generationResult = null;
            Exception generationError = null;
            var cancelled = false;

            api.Dialogs.ActivateGlobalProgress(async args =>
            {
                args.IsIndeterminate = true;
                args.Text = GetResourceText("VideoThumbnail_GeneratePreparing", "Finding videos that need thumbnails...");

                var progress = new Progress<Services.VideoPlayer.AnikiVideoThumbnailGenerationProgress>(p =>
                {
                    args.IsIndeterminate = p.TotalItems <= 0;
                    args.ProgressMaxValue = Math.Max(1, p.TotalItems);
                    args.CurrentProgressValue = Math.Min(Math.Max(0, p.ProcessedItems), Math.Max(1, p.TotalItems));
                    var current = string.IsNullOrWhiteSpace(p.CurrentItem) ? string.Empty : "  •  " + p.CurrentItem;
                    args.Text = string.Format(
                        GetResourceText("VideoThumbnail_GenerateProgress", "Generating thumbnails... {0}/{1}  •  +{2} created  •  {3} existing  •  {4} failed{5}"),
                        p.ProcessedItems,
                        p.TotalItems,
                        p.GeneratedItems,
                        p.ExistingItems,
                        p.FailedItems,
                        current);
                });

                try
                {
                    generationResult = await player.GenerateMissingLibraryThumbnailsAsync(progress, args.CancelToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    generationError = ex;
                }
            }, new Playnite.SDK.GlobalProgressOptions(
                GetResourceText("VideoThumbnail_GeneratePreparing", "Finding videos that need thumbnails..."),
                true)
            {
                IsIndeterminate = true
            });

            if (generationError != null)
            {
                ShowError(GetResourceText("VideoThumbnail_GenerateError", "Thumbnail generation failed.") + "\n" + generationError.Message);
                return;
            }
            if (cancelled || generationResult == null)
            {
                ShowInformation(GetResourceText("VideoThumbnail_GenerateCancelled", "Thumbnail generation cancelled."));
                return;
            }

            ShowInformation(GetResourceText("VideoThumbnail_GenerateCompleted", "Thumbnail generation completed.") + "\n\n" +
                string.Format(
                    GetResourceText("VideoThumbnail_GenerateResult", "{0} videos scanned. {1} thumbnails created, {2} already available, {3} failed."),
                    generationResult.TotalItems,
                    generationResult.GeneratedItems,
                    generationResult.ExistingItems,
                    generationResult.FailedItems));
        }

        private void BrowseVideoNetworkLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var slot = GetTaggedSlot(sender);
                if (slot < 1 || slot > 4)
                {
                    return;
                }

                var vm = DataContext as AnikiHelperSettingsViewModel;
                var settings = vm?.Settings;
                if (settings == null)
                {
                    return;
                }

                var selected = SelectNetworkFolder(
                    GetResourceText("VideoNetwork_BrowseTitle", "Choose a network folder"),
                    GetVideoNetworkLocationPath(settings, slot));

                if (string.IsNullOrWhiteSpace(selected))
                {
                    return;
                }

                SetVideoNetworkLocationPath(settings, slot, selected);
                if (string.IsNullOrWhiteSpace(GetVideoNetworkLocationName(settings, slot)))
                {
                    SetVideoNetworkLocationName(settings, slot, GetNetworkLocationDefaultName(selected));
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ClearVideoNetworkLocation_Click(object sender, RoutedEventArgs e)
        {
            var slot = GetTaggedSlot(sender);
            if (slot < 1 || slot > 4)
            {
                return;
            }

            var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
            if (settings == null)
            {
                return;
            }

            SetVideoNetworkLocationName(settings, slot, string.Empty);
            SetVideoNetworkLocationPath(settings, slot, string.Empty);
        }

        private static int GetTaggedSlot(object sender)
        {
            var element = sender as FrameworkElement;
            return element != null && int.TryParse(element.Tag?.ToString(), out var slot) ? slot : 0;
        }

        private static string GetVideoNetworkLocationName(AnikiHelperSettings settings, int slot)
        {
            switch (slot)
            {
                case 1: return settings.VideoNetworkLocation1Name;
                case 2: return settings.VideoNetworkLocation2Name;
                case 3: return settings.VideoNetworkLocation3Name;
                case 4: return settings.VideoNetworkLocation4Name;
                default: return string.Empty;
            }
        }

        private static string GetVideoNetworkLocationPath(AnikiHelperSettings settings, int slot)
        {
            switch (slot)
            {
                case 1: return settings.VideoNetworkLocation1Path;
                case 2: return settings.VideoNetworkLocation2Path;
                case 3: return settings.VideoNetworkLocation3Path;
                case 4: return settings.VideoNetworkLocation4Path;
                default: return string.Empty;
            }
        }

        private static void SetVideoNetworkLocationName(AnikiHelperSettings settings, int slot, string value)
        {
            switch (slot)
            {
                case 1: settings.VideoNetworkLocation1Name = value; break;
                case 2: settings.VideoNetworkLocation2Name = value; break;
                case 3: settings.VideoNetworkLocation3Name = value; break;
                case 4: settings.VideoNetworkLocation4Name = value; break;
            }
        }

        private static void SetVideoNetworkLocationPath(AnikiHelperSettings settings, int slot, string value)
        {
            switch (slot)
            {
                case 1: settings.VideoNetworkLocation1Path = value; break;
                case 2: settings.VideoNetworkLocation2Path = value; break;
                case 3: settings.VideoNetworkLocation3Path = value; break;
                case 4: settings.VideoNetworkLocation4Path = value; break;
            }
        }

        private string SelectNetworkFolder(string title, string currentPath)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = title;
                dialog.ShowNewFolderButton = false;

                var current = (currentPath ?? string.Empty).Trim().Replace('/', '\\');
                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }

                var result = dialog.ShowDialog();
                if (result == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    return dialog.SelectedPath.Trim();
                }

                return null;
            }
        }

        private static string GetNetworkLocationDefaultName(string path)
        {
            try
            {
                var value = (path ?? string.Empty).Trim().TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                var info = new DirectoryInfo(value);
                if (!string.IsNullOrWhiteSpace(info.Name))
                {
                    return info.Name;
                }

                var parts = value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[parts.Length - 1] : value;
            }
            catch
            {
                return path ?? string.Empty;
            }
        }


        private void BrowseVideoThumbnailFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                if (settings == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("VideoThumbnail_BrowseTitle", "Choose ffmpeg.exe"),
                    Filter = "ffmpeg.exe|ffmpeg.exe|Executable files|*.exe|All files|*.*",
                    FileName = string.IsNullOrWhiteSpace(settings.VideoThumbnailFfmpegPath) ? "ffmpeg.exe" : Path.GetFileName(settings.VideoThumbnailFfmpegPath),
                    CheckFileExists = true,
                    Multiselect = false
                };

                var currentPath = (settings.VideoThumbnailFfmpegPath ?? string.Empty).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    try
                    {
                        var directory = Directory.Exists(currentPath) ? currentPath : Path.GetDirectoryName(currentPath);
                        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                        {
                            dialog.InitialDirectory = directory;
                        }
                    }
                    catch { }
                }

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    settings.VideoThumbnailFfmpegPath = dialog.FileName;
                    TryAutoFillVideoFfprobePath(settings);
                    settings.VideoPlayer?.RefreshThumbnailDiagnostics();
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ClearVideoThumbnailFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
            if (settings != null)
            {
                settings.VideoThumbnailFfmpegPath = string.Empty;
                settings.VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        private void VideoThumbnailFfmpegPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                    if (settings != null)
                    {
                        TryAutoFillVideoFfprobePath(settings);
                        settings.VideoPlayer?.RefreshThumbnailDiagnostics();
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void BrowseVideoFfprobe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                if (settings == null)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = GetResourceText("VideoFfprobe_BrowseTitle", "Choose ffprobe.exe"),
                    Filter = "ffprobe.exe|ffprobe.exe|Executable files|*.exe|All files|*.*",
                    FileName = string.IsNullOrWhiteSpace(settings.VideoFfprobePath) ? "ffprobe.exe" : Path.GetFileName(settings.VideoFfprobePath),
                    CheckFileExists = true,
                    Multiselect = false
                };

                var currentPath = (settings.VideoFfprobePath ?? string.Empty).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(currentPath))
                {
                    currentPath = (settings.VideoThumbnailFfmpegPath ?? string.Empty).Trim().Trim('"');
                }

                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    try
                    {
                        var directory = Directory.Exists(currentPath) ? currentPath : Path.GetDirectoryName(currentPath);
                        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                        {
                            dialog.InitialDirectory = directory;
                        }
                    }
                    catch { }
                }

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    settings.VideoFfprobePath = dialog.FileName;
                    settings.VideoPlayer?.RefreshThumbnailDiagnostics();
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ClearVideoFfprobe_Click(object sender, RoutedEventArgs e)
        {
            var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
            if (settings != null)
            {
                settings.VideoFfprobePath = string.Empty;
                settings.VideoPlayer?.RefreshThumbnailDiagnostics();
            }
        }

        private void VideoFfprobePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                    settings?.VideoPlayer?.RefreshThumbnailDiagnostics();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private static void TryAutoFillVideoFfprobePath(AnikiHelperSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            try
            {
                var currentProbe = (settings.VideoFfprobePath ?? string.Empty).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(currentProbe) && File.Exists(currentProbe))
                {
                    // Preserve a valid manually selected ffprobe executable.
                    return;
                }

                var ffmpeg = (settings.VideoThumbnailFfmpegPath ?? string.Empty).Trim().Trim('"');
                var folder = string.IsNullOrWhiteSpace(ffmpeg) ? string.Empty : Path.GetDirectoryName(ffmpeg);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                var ffprobe = Path.Combine(folder, "ffprobe.exe");
                if (File.Exists(ffprobe))
                {
                    settings.VideoFfprobePath = ffprobe;
                }
            }
            catch
            {
            }
        }

        private void ClearVideoThumbnailCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = (DataContext as AnikiHelperSettingsViewModel)?.Settings;
                settings?.VideoPlayer?.ClearThumbnailCache();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
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

        private void BrowseFilterBackgroundsFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as AnikiHelperSettingsViewModel;

            if (vm == null)
            {
                return;
            }

            var selectedPath = SelectFolder(
                FindResource("CustomIcons_SelectFilterBackgroundFolderDialog") as string ?? "Select the folder containing your filter background images.",
                vm.Settings.CustomFilterBackgroundsFolder
            );

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                vm.Settings.CustomFilterBackgroundsFolder = selectedPath;
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
