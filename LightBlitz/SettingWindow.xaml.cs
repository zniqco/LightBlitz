using Microsoft.Win32;
using System;
using System.Reflection;
using System.Windows;

namespace LightBlitz
{
    public partial class SettingWindow : Window
    {
        private const string autorunRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string autorunRegistryName = "LightBlitz";

        private string applicationPath
        {
            get
            {
                return Assembly.GetExecutingAssembly().Location;
            }
        }

        public SettingWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            NativeWrapper.RemoveWindowIcon(this);

            base.OnSourceInitialized(e);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(autorunRegistryPath, true))
                LaunchAppAtLoginCheckBox.IsChecked = ("\"" + applicationPath + "\"").Equals(key.GetValue(autorunRegistryName));

            ApplySpellsCheckBox.IsChecked = Settings.Current.ApplySpells;
            ApplyRunesCheckBox.IsChecked = Settings.Current.ApplyRunes;
            // ApplyItemBuildsCheckBox.IsChecked = Settings.Current.ApplyItemBuilds;
            BlinkToRightCheckBox.IsChecked = Settings.Current.BlinkToRight;

            MapSummonersRiftCheckBox.IsChecked = Settings.Current.MapSummonersLift;
            MapHowlingAbyssCheckBox.IsChecked = Settings.Current.MapHowlingAbyss;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(autorunRegistryPath, true))
            {
                if (LaunchAppAtLoginCheckBox.IsChecked == true)
                    key.SetValue(autorunRegistryName, ("\"" + applicationPath + "\""));
                else if (key.GetValue(autorunRegistryName) != null)
                    key.DeleteValue(autorunRegistryName);
            }

            Settings.Current.ApplySpells = (ApplySpellsCheckBox.IsChecked == true);
            Settings.Current.ApplyRunes = (ApplyRunesCheckBox.IsChecked == true);
            // Settings.Current.ApplyItemBuilds = (ApplyItemBuildsCheckBox.IsChecked == true);
            Settings.Current.BlinkToRight = (BlinkToRightCheckBox.IsChecked == true);

            Settings.Current.MapSummonersLift = (MapSummonersRiftCheckBox.IsChecked == true);
            Settings.Current.MapHowlingAbyss = (MapHowlingAbyssCheckBox.IsChecked == true);

            Settings.Current.SaveToDefault();

            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
