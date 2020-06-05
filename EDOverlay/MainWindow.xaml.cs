using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Windows.Markup;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Configuration;

namespace EDOverlay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // not sure if this path can be changed by config/install.  mine is here
        private string _edJournalPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Saved Games\Frontier Developments\Elite Dangerous");
        private Dictionary<string, HighestConcentationLocation> _highestConcentrations = new Dictionary<string, HighestConcentationLocation>();
        private MediaPlayer _player = new MediaPlayer();
        private string _systemName;
        private string _abbreviation;
        private EdsmApiProvider _edsmProvider;
        private bool _isEdsmApiReady;
        private float _fuelMaxCapacity;
        private float _fuelCurrent;
        private bool _lowFuel = false;

        public int TotalSystemBodies { get; set; }
        public int TotalSystemNonBodies { get; set; }
        public ItemChangingObservableCollection<SystemPoi> SystemPoiList = new ItemChangingObservableCollection<SystemPoi>();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            FindTopMaterialsInLogs();
            WatchJournalForChanges();

            DiscoveryOutputListView.DataContext = _highestConcentrations.OrderBy(entry => entry.Key);
            POIListBox.DataContext = SystemPoiList;
            //Dispatcher.Invoke( () => MakeProgress() )
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
                catch(Exception)
                {
                    // couldn't get the file. must have been busy.  move on
                }
            }
        }

        private void LoadConfig()
        {
            string edsmApiKey = ConfigurationManager.AppSettings["edsmApiKey"] ?? null;
            string pilotName = ConfigurationManager.AppSettings["edsmCmdrName"] ?? null;

            if (edsmApiKey != null && pilotName != null)
            {
                _isEdsmApiReady = true;
                _edsmProvider = new EdsmApiProvider(pilotName, edsmApiKey);
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

        private async void ProcessLiveEntry(string journalEntry)
        {
            // Calculate ships fuel max compacity
            if (journalEntry.Contains("\"event\":\"Loadout\""))
            {
                var fuelCapacity = JObject.Parse(journalEntry)["FuelCapacity"];
                _fuelMaxCapacity = (float)fuelCapacity["Main"];
                FuelLevelMax.Text = _fuelMaxCapacity.ToString();
                LowFuel(_fuelCurrent, _fuelMaxCapacity);
            }

            // Get ships current fuel level when loading a game
            if (journalEntry.Contains("\"event\":\"LoadGame\""))
            {
                _fuelCurrent = (float)JObject.Parse(journalEntry)["FuelLevel"];
                _fuelMaxCapacity = (float)JObject.Parse(journalEntry)["FuelCapacity"];
                FuelLevelCurrent.Text = _fuelCurrent.ToString();
                FuelLevelMax.Text = _fuelMaxCapacity.ToString();
                LowFuel(_fuelCurrent, _fuelMaxCapacity);
            }

            // Jumping to new system
            if (journalEntry.Contains("\"event\":\"StartJump\"") && journalEntry.Contains("Hyperspace"))
            {
                _systemName = JObject.Parse(journalEntry)["StarSystem"].ToString();
                var starClass = JObject.Parse(journalEntry)["StarClass"].ToString();
                SystemPoiList.Clear();

                // Check to see if we are low on fuel (<= 1/4 of max capacity)
                LowFuel(_fuelCurrent, _fuelMaxCapacity);

                string upcomingStar;

                if (starClass == "K" || starClass == "G" || starClass == "B" || starClass == "F" || starClass == "O" || starClass == "A" || starClass == "M" && _lowFuel)
                    upcomingStar = $"a class {starClass} star...\nWARNING: Fuel Levels below 25%, please refuel here.";
                if (starClass == "K" || starClass == "G" || starClass == "B" || starClass == "F" || starClass == "O" || starClass == "A" || starClass == "M" && !_lowFuel)
                    upcomingStar = $"a class {starClass} star...";
                if (starClass == "N" && _lowFuel)
                    upcomingStar = $"CAUTION: Non-Sequence star ahead!\nWARNING: Fuel Levels below 25%. Reroute to KGBFOAM start to refuel.";
                if (starClass == "N" && !_lowFuel)
                    upcomingStar = $"CAUTION: Non-Sequence star ahead!";
                if (_lowFuel)
                    upcomingStar = $"a class {starClass} star...\nWARNING: Fuel Levels below 25%. Reroute to KGBFOAM start to refuel.";
                else
                    upcomingStar = $"a class {starClass} star...";

                TrafficText = $"Standby for system {_systemName}\n" + upcomingStar;

                // TODO Make fuel warning it's own textbox. Make the text red and then refactor this whole if statement to use that
                // TODO Make a class for all star types then we can just call starClass.fuelable
                // TODO Figure out what other event can alter the fuel tank of your ship
            }

            // Set fuel levels after fuel scooping
            if (journalEntry.Contains("\"event\":\"FuelScoop\""))
            {
                _fuelCurrent = (float)JObject.Parse(journalEntry)["Total"];
                FuelLevelCurrent.Text = _fuelCurrent.ToString();
            }

            // Jumped to new system
            if (journalEntry.Contains("\"event\":\"FSDJump\""))
            {
                _systemName = JObject.Parse(journalEntry)["StarSystem"].ToString();
                _fuelCurrent = (float)JObject.Parse(journalEntry)["FuelLevel"];
                FuelLevelCurrent.Text = _fuelCurrent.ToString();
                SystemPoiList.Clear();

                if (_isEdsmApiReady)
                {
                    await Task.Delay(1000); // wait a second to let EDSM be updated
                    var systemTraffic = await _edsmProvider.GetSystemTrafficAsync(_systemName);
                    if (systemTraffic == null || systemTraffic.id == 0)
                        TrafficText = "EDSM had no data";
                    else
                        TrafficText = $"Discovered by CMDR {systemTraffic?.discovery?.commander} on {systemTraffic?.discovery?.date}" +
                            $"\nTotal Traffic: {systemTraffic.traffic.total} ships ({systemTraffic.traffic.week} this week)";
                }
            }

            // Honked
            if (journalEntry.Contains("FSSDiscoveryScan"))
            {
                TotalSystemBodies = (int)JObject.Parse(journalEntry)["BodyCount"];
                TotalSystemNonBodies = (int)JObject.Parse(journalEntry)["NonBodyCount"];

                // Print total bodies to Textblock
                TotalBodies.Text = TotalSystemBodies.ToString();
            }

            // Scanned a planet
            else if (journalEntry.Contains("\"event\":\"Scan\""))
            {
                ProcessScannedBody(journalEntry);
            }

            // All bodies scanned
            else if (journalEntry.Contains("\"event\":\"FSSAllBodiesFound\""))
            {
                TotalBodies.Text = "System Scan Complete";
            }

            // Landable (materials) found
            else if (journalEntry.Contains("\"event\":\"Scan\"") && journalEntry.Contains("\"Landable\":true"))
            {  
                foreach (var find in ProcessMaterials(journalEntry))
                {
                    //POIText.Text = find;
                    Console.WriteLine(find);
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

            // FSD Target to calculate remaining jumps
            else if (journalEntry.Contains("\"event\":\"FSDTarget\"") || journalEntry.Contains("\"event\":\"Music\", \"MusicTrack\":\"DestinationFromHyperspace\""))
            {
                TotalBodies.Text = "Awaiting Scan";
                
                if (journalEntry.Contains("\"event\":\"FSDTarget\""))
                {
                    int _jumpsRemaining = (int)JObject.Parse(journalEntry)["RemainingJumpsInRoute"];

                    // Print Remaining Jumps to Textblock
                    RemainingJumps.Text = _jumpsRemaining.ToString();
                }
                if (journalEntry.Contains("\"event\":\"Music\", \"MusicTrack\":\"DestinationFromHyperspace\""))
                {
                    // Destination Reached
                    RemainingJumps.Text = "Destination Reached!";
                }
            }

            //// Received a chat message
            //else if (journalEntry.Contains("\"event\":\"ReceiveText\""))
            //{
            //    ProcessReceivedMessage(journalEntry);
            //}

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

        private void LowFuel(float fuelCurrent, float fuelMax)
        {
            if (fuelCurrent <= fuelMax / 4f)
                _lowFuel = true;
            else
                _lowFuel = false;
        }

        //private void ProcessReceivedMessage(string journalEntry)
        //{
        //    string msgFrom = JObject.Parse(journalEntry)["From"]?.ToString();
        //    string msgBody = JObject.Parse(journalEntry)["Message"]?.ToString();
        //    bool isPlayer = JObject.Parse(journalEntry)["Channel"]?.ToString() == "player";

        //    if (isPlayer)
        //    {
        //        AddNewMessage(msgFrom, msgBody);
        //    }
        //}

        //private void AddNewMessage(string msgFrom, string msgBody)
        //{
        //    // add new message to list
        //    chatText.Text = "From: " + msgFrom + " -- " + msgBody;
        //}

        private void ProcessScannedBody(string journalEntry)
        {
            int bodyId = (int)JObject.Parse(journalEntry)["BodyID"];
            int distance = Convert.ToInt32(JObject.Parse(journalEntry)["DistanceFromArrivalLS"]);

            string bodyName = JObject.Parse(journalEntry)["BodyName"].ToString()?.Replace(_systemName ?? "NOSYSTEM", string.Empty);
            bool isTerraformable = JObject.Parse(journalEntry)["TerraformState"]?.ToString() == "Terraformable";
            PlanetClass bodyClass = MapPlanetClass(JObject.Parse(journalEntry)["PlanetClass"]?.ToString());

            if (isTerraformable || bodyClass == PlanetClass.EarthLike || bodyClass == PlanetClass.AmmoniaWorld || bodyClass == PlanetClass.WaterWorld)
            {
                AddSystemPoi(bodyId, bodyName, isTerraformable, bodyClass, distance);
            }
        }

        private void AddSystemPoi(int bodyId, string bodyName, bool isTerraformble, PlanetClass planetClass, int distance)
        {
            if (!SystemPoiList.Any(item => item.BodyID == bodyId))
            {
                PlaySystemPoiFound(planetClass);

                SystemPoiList.Add(new SystemPoi()
                {
                    BodyID = bodyId,
                    PlanetClass = planetClass,
                    BodyName = bodyName,
                    IsTerraformable = isTerraformble,
                    SurfaceScanned = false,
                    DistanceFromEntry = distance
                });
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
                AddAbbreviation(element);

                if (!_highestConcentrations.ContainsKey(element))
                {
                    _highestConcentrations.Add(element, new HighestConcentationLocation(_abbreviation ,decimal.Round(material.Percent, 2), bodyName));
                }
                else if (material.Percent > _highestConcentrations[element].Percent)
                {
                    _highestConcentrations[element] = new HighestConcentationLocation(_abbreviation, decimal.Round(material.Percent, 2), bodyName);
                    newFindings.Add($"High contentration ({material.Name}): {material.Percent}");
                    PlayNewHighConcentationFound();
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
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ExitButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void SettingsButton_Click(object sender, EventArgs e)
        {
            OptionsWindow options = new OptionsWindow();
            options.Show();
        }

        //private void VeryCommonButton_Click(object sender, EventArgs e)
        //{            

        //}
        
        //private void CommonButton_Click(object sender, EventArgs e)
        //{
            
        //}

        //private void UncommonButton_Click(object sender, EventArgs e)
        //{
            
        //}

        //private void RareButton_Click(object sender, EventArgs e)
        //{
            
        //}

        private void CopySystem_Click(object sender, EventArgs e)
        {
            var textblock = (sender as TextBlock).Text;

            Clipboard.SetText(textblock.ToString());
        }

        private void PlaySystemPoiFound(PlanetClass planetClass)
        {
            switch(planetClass)
            {
                case PlanetClass.AmmoniaWorld:
                    _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
                case PlanetClass.WaterWorld:
                    _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
                case PlanetClass.EarthLike:
                    _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
                default:
                    _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
            }
            
            _player.Play();
        }

        private void PlayNewHighConcentationFound()
        {
            _player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/256543__debsound__r2d2-astro-droid.wav"));
            _player.Play();
        }

        private void AddAbbreviation(string material)
        {
            
            if (material == "Carbon")
                _abbreviation = "(C)";
            if (material == "Iron")
                _abbreviation = "(Fe)";
            if (material == "Nickel")
                _abbreviation = "(Ni)";
            if (material == "Phosphorus")
                _abbreviation = "(P)";
            if (material == "Sulphur")
                _abbreviation = "(S)";
            if (material == "Arsenic")
                _abbreviation = "(As)";
            if (material == "Chromium")
                _abbreviation = "(Cr)";
            if (material == "Germanium")
                _abbreviation = "(Ge)";
            if (material == "Manganese")
                _abbreviation = "(Mn)";
            if (material == "Vanadium")
                _abbreviation = "(V)";
            if (material == "Zinc")
                _abbreviation = "(Zn)";
            if (material == "Zirconium")
                _abbreviation = "(Zr)";
            if (material == "Cadmium")
                _abbreviation = "(Cd)";
            if (material == "Mercury")
                _abbreviation = "(Hg)";
            if (material == "Molybdenum")
                _abbreviation = "(Mo)";
            if (material == "Niobium")
                _abbreviation = "(Nb)";
            if (material == "Tin")
                _abbreviation = "(Sn)";
            if (material == "Tungsten")
                _abbreviation = "(W)";
            if (material == "Antimony")
                _abbreviation = "(Sb)";
            if (material == "Polonium")
                _abbreviation = "(Po)";
            if (material == "Ruthenium")
                _abbreviation = "(Ru)";
            if (material == "Selenium")
                _abbreviation = "(Se)";
            if (material == "Technetium")
                _abbreviation = "(Tc)";
            if (material == "Tellurium")
                _abbreviation = "(Te)";
            if (material == "Yttrium")
                _abbreviation = "(Y)";
        }

        private PlanetClass MapPlanetClass(string edPlanetClass)
        {
            switch (edPlanetClass)
            {
                case "Icy body":
                    return PlanetClass.Icy;
                case "Rocky body":
                    return PlanetClass.Rocky;
                case "Rocky ice body":
                    return PlanetClass.RockyIce;
                case "Metal rich body":
                    return PlanetClass.MetalRich;
                case "Sudarsky class II gas giant":
                    return PlanetClass.ClassIIGasGiant;
                case "High metal content body":
                    return PlanetClass.HMC;
                case "Water world":
                    return PlanetClass.WaterWorld;
                case "Ammonia world":
                    return PlanetClass.AmmoniaWorld;
                case "Earthlike body":
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

        #region Dependency Properties
        private string _trafficText = "Awaiting new system";
        public string TrafficText
        {
            get { return _trafficText; }

            set
            {
                _trafficText = value;
                NotifyPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    public class MaterialConcentration
    {
        public string Name { get; set; }

        public decimal Percent { get; set; }
    }

    public class HighestConcentationLocation
    {
        public HighestConcentationLocation(string abbreviation, decimal percent, string body) { Abbreviation = abbreviation;  Percent = percent; BodyName = body; }
        public string Abbreviation { get; set; }
        public decimal Percent { get; set; }
        public string BodyName { get; set; }
    }

    public class SystemPoi : INotifyPropertyChanged
    {
        public int BodyID { get; set; }
        public PlanetClass PlanetClass { get; set; }
        public string BodyName { get; set; }
        public bool IsTerraformable { get; set; }
        public int DistanceFromEntry { get; set; }

        private bool _surfaceScanned;
        public bool SurfaceScanned
        {
            get { return _surfaceScanned; }

            set
            {
                _surfaceScanned = value;
                NotifyPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum PlanetClass
    {
        Icy,
        Rocky,
        RockyIce,
        HMC,
        MetalRich,
        ClassIIGasGiant,
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

    [ValueConversion(typeof(bool), typeof(string))]
    public class BoolToMappedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool)value ? "mapped" : "not mapped";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion
}
