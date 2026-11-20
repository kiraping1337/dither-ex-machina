using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace dither_ex_machina
{
    public partial class App : Application
    {
        private static readonly string LanguageFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dither Ex Machina", "language.txt");

        private bool _russian;

        internal bool IsRussian => _russian;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool russian = false;
            try { russian = File.ReadAllText(LanguageFile).Trim() == "ru"; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            ApplyLanguage(russian);
            base.OnStartup(e);
        }

        internal void SetLanguage(bool russian)
        {
            if (_russian == russian) return;
            ApplyLanguage(russian);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LanguageFile)!);
                File.WriteAllText(LanguageFile, _russian ? "ru" : "en");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void ApplyLanguage(bool russian)
        {
            var culture = CultureInfo.GetCultureInfo(russian ? "ru-RU" : "en-US");
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
            Resources["UiLanguage"] = XmlLanguage.GetLanguage(culture.Name);
            if (russian != _russian)
            {
                string dictionary = russian ? "Russian" : "English";
                Resources.MergedDictionaries[0] = new ResourceDictionary
                {
                    Source = new Uri($"/dither-ex-machina;component/Localization/{dictionary}.xaml", UriKind.Relative)
                };
                _russian = russian;
            }
        }
    }
}
