using System;
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
using System.Windows.Markup;
using System.Collections.ObjectModel;

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
        private MediaPlayer _player = new MediaPlayer();
        private string _systemName;

        public ObservableCollection<SystemPoi> SystemPoiList = new ObservableCollection<SystemPoi>();

        public MainWindow()
        {
            FindTopMaterialsInLogs();

            InitializeComponent();

            DiscoveryOutputListView.DataContext = _highestConcentrations.OrderBy(entry => entry.Key);
            POIListBox.DataContext = SystemPoiList;
            //Dispatcher.Invoke( () => MakeProgress() )

            WatchJournalForChanges();
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
                        ProcessMaterials(line);
                    }
                }
                catch(Exception ex)
                {
                    // couldn't get the file. must have been busy.  move on
                }
            }
        }


        private async void WatchJournalForChanges()
        {
            // grab the latest log file
            string logCacheFileName = Directory.GetFiles(_edJournalPath)
                .Where(file => System.IO.Path.GetExtension(file) == ".log")
                .OrderByDescending(file => File.GetLastWriteTime(file))
                .FirstOrDefault();

            System.Diagnostics.Debug.WriteLine(logCacheFileName);

            await Task.Run(() =>
            {
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
                            // a new event came in
                            Dispatcher.Invoke(() => ProcessLiveEntry(line));
                        }

                        //update the last max offset
                        lastMaxOffset = reader.BaseStream.Position;
                    }
                }
            });
        }

        private void ProcessLiveEntry(string journalEntry)
        {
            // Jump to new system
            if (journalEntry.Contains("\"event\":\"FSDJump\""))
            {
                _systemName = JObject.Parse(journalEntry)["StarSystem"].ToString();
                SystemPoiList.Clear();
            }

            // Terraformable found
            else if (journalEntry.Contains("\"event\":\"Scan\"") && journalEntry.Contains("Terraformable"))
            {
                AddTerraformablePoi(journalEntry);
            }

            // Landable (materials) found
            else if (journalEntry.Contains("\"event\":\"Scan\"") && journalEntry.Contains("\"Landable\":true"))
            {
                foreach (var find in ProcessMaterials(journalEntry))
                {
                    PlayNewHighConcentationFound();
                    //CurrentEventText.Text = $"New high concentration found for {material.Name} on {bodyName}!";
                    POIText.Text = find;
                }
            }

            // Surface Scan Complete
            else if (journalEntry.Contains("\"event\":\"SAAScanComplete\""))
            {
                int scannedBodyId = (int)JObject.Parse(journalEntry)["BodyID"];
                var scannedBody = SystemPoiList.FirstOrDefault(poi => poi.BodyID == scannedBodyId);

                if (scannedBody != null)
                    scannedBody.SurfaceScanned = true;
            }

            // ED closed
            else if (journalEntry.Contains("\"event\":\"Shutdown\""))
            {
                Application.Current.Shutdown();
            }
            else
            {
                CurrentEventText.Text = journalEntry;
            }
        }

        private void AddTerraformablePoi(string journalEntry)
        {
            int scannedBodyId = (int)JObject.Parse(journalEntry)["BodyID"];
            string bodyName = JObject.Parse(journalEntry)["BodyName"].ToString().Replace(_systemName ?? "NOSYSTEM", string.Empty);
            int distance = Convert.ToInt32(JObject.Parse(journalEntry)["DistanceFromArrivalLS"]);

            if (!SystemPoiList.Any(item => item.BodyID == scannedBodyId))
            {
                PlayTerraformFound();

                SystemPoiList.Add(new SystemPoi() {
                    BodyID=scannedBodyId,
                    PlanetClass = PlanetClass.AmmoniaWorld,
                    BodyName = bodyName,
                    IsTerraformable = true,
                    SurfaceScanned = false,
                    DistanceFromEntry = distance });

                CurrentEventText.Text = $"Terraformable found: {JObject.Parse(journalEntry)["BodyName"]}";
            }
        }

        private List<string> ProcessMaterials(string journalEntry)
        {
            var json = JObject.Parse(journalEntry);
            var materials = json.SelectToken("Materials");
            List<string> newFindings = new List<string>();

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
                    newFindings.Add($"High contentration ({material.Name}): {material.Percent}");
                }
            }

            return newFindings;
        }

        private async void MakeProgress()
        {
            IProgress<int> progress = new Progress<int>(percentCompleted =>
            {
                eventProgressBar.Value = percentCompleted;
            });

            await Task.Run(async () =>
            {
                progress.Report(0);
                foreach (var i in Enumerable.Range(1, 20))
                {
                    await Task.Delay(1000);
                    progress.Report(i * 5);
                }
            });
        }

        private void InterfaceItem_MakeDraggable(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void ExitButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void VeryCommonButton_Click(object sender, EventArgs e)
        {
            

        }
        
        private void CommonButton_Click(object sender, EventArgs e)
        {
            
        }

        private void UncommonButton_Click(object sender, EventArgs e)
        {
            
        }

        private void RareButton_Click(object sender, EventArgs e)
        {
            
        }

        // TODO: Refactor all these audio methods into one method
        private void PlayTerraformFound()
        {
            _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
            _player.Play();
        }

        private void PlayEarthLikeFound()
        {
            _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/earthlikeworld.wav"));
            _player.Play();
        }

        private void PlayWaterWorldFound()
        {
            _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/waterworld.wav"));
            _player.Play();
        }

        private void PlayAmmoniaWorldFound()
        {
            _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/ammoniaworld.wav"));
            _player.Play();
        }


        private void PlayNewHighConcentationFound()
        {
            _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/256543__debsound__r2d2-astro-droid.wav"));
            _player.Play();
        }

        private PlanetClass MapPlanetClass(string edPlanetClass)
        {
            switch (edPlanetClass)
            {
                case "Icy body":
                    return PlanetClass.Icy;
                case "High metal content body":
                    return PlanetClass.HighMetalContent;
                case "Water world":
                    return PlanetClass.WaterWorld;
                case "Ammonia world":
                    return PlanetClass.AmmoniaWorld;
                case "Earthlike world":
                    return PlanetClass.EarthLike;
                default:
                    return PlanetClass.Icy;
            }
        }

        public Dictionary<string, Rarity> MaterialRarity = new Dictionary<string, Rarity>
        {
            {"Carbon", Rarity.VeryCommon },
            {"Iron", Rarity.VeryCommon },
            {"Nickel", Rarity.VeryCommon },
            {"Phosphorus", Rarity.VeryCommon },
            {"Sulphur", Rarity.VeryCommon },
            {"Arsenic", Rarity.Common },
            {"Chromium", Rarity.Common },
            {"Germanium", Rarity.Common },
            {"Manganese", Rarity.Common },
            {"Vanadium", Rarity.Common },
            {"Zinc", Rarity.Common },
            {"Zirconium", Rarity.Common },
            {"Cadmium", Rarity.Uncommon },
            {"Mercury", Rarity.Uncommon },
            {"Molybdenum", Rarity.Uncommon },
            {"Niobium", Rarity.Uncommon },
            {"Tin", Rarity.Uncommon },
            {"Tungsten", Rarity.Uncommon },
            {"Antimony", Rarity.Rare },
            {"Polonium", Rarity.Rare },
            {"Ruthenium", Rarity.Rare },
            {"Selenium", Rarity.Rare },
            {"Technetium", Rarity.Rare },
            {"Tellurium", Rarity.Rare },
            {"Yttrium", Rarity.Rare }
        };
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

    public class SystemPoi
    {
        public int BodyID { get; set; }
        public PlanetClass PlanetClass { get; set; }
        public string BodyName { get; set; }
        public bool IsTerraformable { get; set; }
        public int DistanceFromEntry { get; set; }
        public bool SurfaceScanned { get; set; }
    }

    public enum PlanetClass
    {
        Icy,
        HighMetalContent,
        EarthLike,
        WaterWorld,
        AmmoniaWorld
    };

    public enum Rarity
    {
        VeryCommon,
        Common,
        Uncommon,
        Rare,
        VeryRare
    };


    #region UI Concerns
    public abstract class BaseConverter : MarkupExtension
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }

    [ValueConversion(typeof(ObservableCollection<string>), typeof(string))]
    public class ListToStringConverter : BaseConverter, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (targetType != typeof(string))
                throw new InvalidOperationException("The target must be a String");

            return String.Join(Environment.NewLine, ((ObservableCollection<string>)value).ToArray());
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion
}
