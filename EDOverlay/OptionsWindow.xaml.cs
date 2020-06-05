using System;
using System.Windows;
using System.Windows.Input;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Configuration;
using System.Collections.Specialized;

namespace EDOverlay
{
    /// <summary>
    /// Interaction logic for OptionsWindow.xaml
    /// </summary>
    public partial class OptionsWindow
    { 
        public OptionsWindow()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            cmdrName.Text = ConfigurationManager.AppSettings.Get("edsmCmdrName");
            apiKey.Text = ConfigurationManager.AppSettings.Get("edsmApiKey");
        }

        private void InterfaceItem_MakeDraggable(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ExitButton_Click(object sender, EventArgs e)
        {
            Close();
        }        

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            ConfigData con = new ConfigData
            {
                CmdrNameSave = cmdrName.Text,
                ApiKeySave = apiKey.Text
            };
            UpdateSetting("edsmCmdrName", con.CmdrNameSave);
            UpdateSetting("edsmApiKey", con.ApiKeySave);
            Close();
        }

        private static void UpdateSetting(string key, string value)
        {
            Configuration configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            configuration.AppSettings.Settings[key].Value = value;
            configuration.Save();

            ConfigurationManager.RefreshSection("appSettings");
        }

        public class ConfigData
        {
            private string cmdrNameSave;
            private string apiKeySave;
            //private string journalPathSave;

            public string CmdrNameSave
            {
                get { return cmdrNameSave; }
                set { cmdrNameSave = value; }
            }

            public string ApiKeySave
            {
                get { return apiKeySave; }
                set { apiKeySave = value; }
            }

            //public string JournalPathSave
            //{
            //    get { return journalPathSave; }
            //    set { journalPathSave = value; }
            //}
        }
    }
}
