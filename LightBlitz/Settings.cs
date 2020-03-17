using System;
using System.IO;
using System.Xml.Serialization;

namespace LightBlitz
{
    public class Settings
    {
        private static readonly XmlSerializer xmlSerializer = new XmlSerializer(typeof(Settings));

        public static Settings Current = new Settings();

        public bool ApplySpells = true;
        public bool ApplyRunes = true;
        public bool ApplyItemBuilds = true;
        public bool BlinkToRight = false;
        public bool MapSummonersLift = true;
        public bool MapHowlingAbyss = true;

        public static Settings LoadDefault()
        {
            var settingPath = GetAppDataPath("Settings.xml");

            if (File.Exists(settingPath))
            {
                try
                {
                    using (FileStream stream = new FileStream(settingPath, FileMode.Open, FileAccess.Read))
                        return (Settings)xmlSerializer.Deserialize(stream);
                }
                catch (InvalidOperationException)
                {
                }
            }

            return new Settings();
        }

        public void SaveToDefault()
        {
            var settingPath = GetAppDataPath("Settings.xml");

            using (FileStream stream = File.Create(settingPath))
                xmlSerializer.Serialize(stream, this);
        }

        private static string GetAppDataPath(string appendPath)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appPath = Path.Combine(appDataPath, "LightBlitz");

            if (!Directory.Exists(appPath))
                Directory.CreateDirectory(appPath);

            return Path.Combine(appPath, appendPath);
        }
    }
}
