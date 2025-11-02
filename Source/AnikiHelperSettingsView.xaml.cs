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
            LoadLocale(); // ← charge fr_FR.xaml ou en_US.xaml
        }

        private void LoadLocale()
        {
            try
            {
                string asm = Assembly.GetExecutingAssembly().GetName().Name; // "AnikiHelper"
                var cul = CultureInfo.CurrentUICulture;

                string dash = cul.Name;                    // fr-FR
                string underscore = dash.Replace('-', '_'); // fr_FR
                string neutral = cul.TwoLetterISOLanguageName; // fr

                string basePath = $"pack://application:,,,/{asm};component/";

                var candidates = new[]
                {
            basePath + $"Localization/{dash}.xaml",
            basePath + $"Localization/{underscore}.xaml",
            basePath + $"Localization/{neutral}.xaml",
            // fallback (on a déjà en_US.xaml mergé en XAML, mais on tente quand même en priorité)
            basePath + $"Localization/en_US.xaml"
        };

                foreach (var uri in candidates)
                {
                    try
                    {
                        var dict = (ResourceDictionary)Application.LoadComponent(new Uri(uri, UriKind.Absolute));
                        // On insère devant le fallback pour le surcharger
                        Application.Current.Resources.MergedDictionaries.Insert(0, dict);
                        return;
                    }
                    catch { /* ignore si introuvable */ }
                }
            }
            catch { }
        }


        private void ResetSnapshot_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.ResetMonthlySnapshot();
        }
    }
}
