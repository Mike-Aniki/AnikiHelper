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
                string asm = Assembly.GetExecutingAssembly().GetName().Name;
                string lang = CultureInfo.CurrentUICulture.Name; // ex: fr-FR, en-US

                string[] uris =
                {
                    $"pack://application:,,,/{asm};component/Localization/{lang}.xaml",
                    $"pack://application:,,,/{asm};component/Localization/en_US.xaml" // fallback
                };

                foreach (var uri in uris)
                {
                    try
                    {
                        var dict = (ResourceDictionary)Application.LoadComponent(new Uri(uri, UriKind.Absolute));
                        Resources.MergedDictionaries.Insert(0, dict);
                        return; // dès qu’un dictionnaire est chargé, on s’arrête
                    }
                    catch
                    {
                        // ignore si non trouvé
                    }
                }
            }
            catch
            {
                // silencieux
            }
        }

        private void ResetSnapshot_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as AnikiHelperSettingsViewModel)?.ResetMonthlySnapshot();
        }
    }
}
