using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;

namespace AnikiHelper
{
    public partial class AnikiHelperSettingsView : UserControl
    {
        public AnikiHelperSettingsView()
        {
            InitializeComponent();

            // IMPORTANT : on charge la locale Playnite ici, AVANT l’affichage
            LoadLocaleFromCurrentUICulture();
        }

        /// <summary>
        /// Injecte le dictionnaire de ressources correspondant à la langue de Playnite
        /// (Playnite règle CurrentUICulture selon la langue choisie).
        /// Ordre de recherche : fr-FR → fr_FR → fr  (puis fallback EN déjà chargé en XAML).
        /// </summary>
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
                        // on tente le suivant
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
