﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace EDOverlay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // not sure if this path can be changed by config/install.  mine is here
        private string _edJournalPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Saved Games\Frontier Developments\Elite Dangerous");

        private Dictionary<string, HighestConcentationLocation> _highestConcentrations = new Dictionary<string, HighestConcentationLocation>();

        public MainWindow()
        {
            FindTopMaterialsInLogs();

            InitializeComponent();

            DiscoveryOutputListView.DataContext = _highestConcentrations.OrderBy(entry => entry.Key);

            var logWatcherTask = Task.Run( () => WatchJournalForChanges() );
        }

        private void FindTopMaterialsInLogs()
        {
            foreach (var file in Directory.GetFiles(_edJournalPath).Where(filename => System.IO.Path.GetExtension(filename) == ".log"))
            {
                // we only care about the landable planet scans for this
                try
                {
                    var lines = File.ReadAllLines(file).Where(line => line.Contains("\"event\":\"Scan\"") && line.Contains("\"Landable\":true"));
                    foreach (var line in lines)
                    {
                        var json = JObject.Parse(line);
                        var materials = json.SelectToken("Materials");

                        // browse through the materials found and update the dictionary if we have a new record high
                        foreach (var mat in materials)
                        {
                            var material = mat.ToObject<MaterialConcentration>();

                            string element = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(material.Name);
                            string bodyName = json["BodyName"].ToString();

                            if (!_highestConcentrations.ContainsKey(element))
                            {
                                _highestConcentrations.Add(element, new HighestConcentationLocation(decimal.Round(material.Percent, 2), bodyName));
                            }
                            else if (material.Percent > _highestConcentrations[element].Percent)
                            {
                                _highestConcentrations[element] = new HighestConcentationLocation(decimal.Round(material.Percent, 2), bodyName);
                            }

                        }
                    }
                }
                catch(Exception ex)
                {
                    // couldn't get the file. must have been busy.  move on
                    //edEventReceived?.Invoke(this, new MyEventArgs(ex.Message));
                }
            }
        }


        private void WatchJournalForChanges()
        {
            // grab the latest log file
            string logCacheFileName = Directory.GetFiles(_edJournalPath)
                .Where(file => System.IO.Path.GetExtension(file) == ".log")
                .OrderByDescending(file => File.GetLastWriteTime(file))
                .FirstOrDefault();


            using (StreamReader reader = new StreamReader(new FileStream(logCacheFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                //start at the end of the file
                long lastMaxOffset = reader.BaseStream.Length;

                //seek to the last max offset
                reader.BaseStream.Seek(lastMaxOffset, SeekOrigin.Begin);

                while (true)
                {
                    System.Threading.Thread.Sleep(100);

                    //if the file size has not changed, idle
                    if (reader.BaseStream.Length == lastMaxOffset)
                        continue;

                    //read out of the file until the EOF
                    string line = "";
                    while ((line = reader.ReadLine()) != null)
                    {
                        //event that a new event came in
                        //MessageBox.Show("New event: " + line);
                    }


                    //update the last max offset
                    lastMaxOffset = reader.BaseStream.Position;
                }
            }
        }

        private void DiscoveryOutputListView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void ExitButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }

    public class MaterialConcentration
    {
        public string Name { get; set; }
        public decimal Percent { get; set; }
    }

    public class HighestConcentationLocation
    {
        public HighestConcentationLocation(decimal percent, string body) { Percent = percent; BodyName = body; }
        public decimal Percent { get; set; }
        public string BodyName { get; set; }
    }
}
