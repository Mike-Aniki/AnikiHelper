using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

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
                // 1) Récupère la culture effective (celle de Playnite)
                CultureInfo cul = CultureInfo.CurrentUICulture;

                string asmName = Assembly.GetExecutingAssembly().GetName().Name; // "AnikiHelper"
                string basePack = $"pack://application:,,,/{asmName};component/";

                // 2) Variantes compatibles avec ta nomenclature de fichiers
                string dash = cul.Name;                     // ex: "fr-FR"
                string underscore = dash.Replace('-', '_'); // ex: "fr_FR"
                string neutral = cul.TwoLetterISOLanguageName; // ex: "fr"

                // 3) Liste ordonnée : on charge le premier qui existe
                string[] candidates =
                {
                    basePack + $"Localization/{dash}.xaml",
                    basePack + $"Localization/{underscore}.xaml",
                    basePack + $"Localization/{neutral}.xaml"
                    // pas besoin d'ajouter en_US ici : déjà chargé dans le XAML comme fallback
                };

                // 4) On insère la langue trouvée en tête des MergedDictionaries
                foreach (var uri in candidates)
                {
                    try
                    {
                        var dict = (ResourceDictionary)Application.LoadComponent(new Uri(uri, UriKind.Absolute));
                        // Insert(0) → il prend la priorité sur le fallback EN
                        Application.Current.Resources.MergedDictionaries.Insert(0, dict);
                        return; // dès qu’on a chargé une variante, on s’arrête
                    }
                    catch
                    {
                        // introuvable → on tente le suivant
                    }
                }

                // Si rien trouvé : on garde l’anglais chargé en XAML (pas de crash).
            }
            catch
            {
                // En cas d’erreur, ne rien faire : fallback EN reste actif.
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
                if (MessageBox.Show(
                    (string)Application.Current.TryFindResource("ConfirmClearCache"),
                    "Aniki Helper",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    (DataContext as AnikiHelperSettingsViewModel)?.ClearColorCache();

                    MessageBox.Show(
                        (string)Application.Current.TryFindResource("CacheClearedMsg"),
                        "Aniki Helper",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while clearing cache:\n" + ex.Message, "Aniki Helper");
            }
        }
    }
}
