using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace EDOverlay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // not sure if this path can be changed by config/install.  mine is here
        private readonly string EDJournalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Saved Games\Frontier Developments\Elite Dangerous");
        private readonly Dictionary<string, HighestConcentationLocation> HighestConcentrations = new Dictionary<string, HighestConcentationLocation>();
        private readonly MediaPlayer Player = new MediaPlayer();
        private string SystemName;
        private long SystemAddress;
        private float[] SystemCoordinates;
        private int ShipId;
        private string ShipName;
        private string CmdrName;
        private EdsmApiProvider EdsmProvider;
        private bool IsEdsmApiReady;

        private IConfiguration _config;
        public int TotalSystemBodies { get; set; }
        public int TotalSystemNonBodies { get; set; }
        public ItemChangingObservableCollection<SystemPoi> SystemPoiList = new ItemChangingObservableCollection<SystemPoi>();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            FindTopMaterialsInLogs();
            WatchJournalForChanges();
            InitializeData();

            DiscoveryOutputListView.DataContext = HighestConcentrations.OrderBy(entry => entry.Key);
            POIListBox.DataContext = SystemPoiList;
            //Dispatcher.Invoke( () => MakeProgress() )
        }

        private void InitializeData()
        {
            RemainingJumps.Text = "Awaiting Plotted Route";
        }

        private void FindTopMaterialsInLogs()
        {
            foreach (var file in Directory.GetFiles(EDJournalPath).Where(filename => System.IO.Path.GetExtension(filename) == ".log"))
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
                catch (Exception)
                {
                    // couldn't get the file. must have been busy.  move on
                }
            }
        }

        private async void LoadConfig()
        {
            var builder = new ConfigurationBuilder()
              .SetBasePath(AppContext.BaseDirectory)
              .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
              .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true);

            _config = builder.Build();

            string edsmApiKey = _config["edsmApiKey"];
            string pilotName = _config["edsmCmdrName"];

            if (string.IsNullOrWhiteSpace(edsmApiKey) || string.IsNullOrWhiteSpace(pilotName))
            {
                System.Diagnostics.Debug.WriteLine("Warning: EDSM configuration not found");
                return;
            }

            EdsmProvider = new EdsmApiProvider(pilotName, edsmApiKey);
            IsEdsmApiReady = await EdsmProvider.Initialize();
        }

        private async void WatchJournalForChanges()
        {
            // grab the latest log file
            string logCacheFileName = Directory.GetFiles(EDJournalPath)
                .Where(file => System.IO.Path.GetExtension(file) == ".log")
                .OrderByDescending(file => File.GetLastWriteTime(file))
                .FirstOrDefault();

            System.Diagnostics.Debug.WriteLine(logCacheFileName);

            await Task.Run(() =>
            {
                using StreamReader reader = new StreamReader(new FileStream(logCacheFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                // grab any important info from most recent file up to latest log (EOF)
                // we can gather global information from the initial records in each file
                string entry;
                while ((entry = reader.ReadLine()) != null)
                {
                    Dispatcher.Invoke(() => ProcessLiveEntry(entry));
                }

                // initialize position at EOF
                long lastMaxOffset = reader.BaseStream.Length;

                while (true)
                {
                    System.Threading.Thread.Sleep(100);

                    //if the file size has not changed, idle
                    if (reader.BaseStream.Length == lastMaxOffset)
                    {
                        continue;
                    }

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
            });
        }

        private async void ProcessLiveEntry(string journalEntry)
        {
            JsonDocument jsonDoc;
            try
            {
                jsonDoc = JsonDocument.Parse(journalEntry);
            }
            catch
            {
                // The log line wasn't valid JSON; just return or handle it
                return;
            }

            // what event is this?
            JsonElement eventJson = jsonDoc.RootElement;

            // Safely check for "event" key:
            if (!eventJson.TryGetProperty("event", out JsonElement eventElement))
            {
                // If there's no "event" property, just return (or handle it however you prefer)
                return;
            }

            string eventName = eventElement.GetString();

            // Update EDSM, if useful
            if (IsEdsmApiReady && !EdsmProvider.DiscardedEvents.Contains(eventName))
            {
                await EdsmProvider.PostEventIfUseful(journalEntry, new EdsmApiProvider.TransientState(ShipId, SystemName, SystemAddress, SystemCoordinates));
            }

            // Jumping to new system
            if (eventName == "StartJump" && journalEntry.Contains("Hyperspace"))
            {
                SystemName = eventJson.GetProperty("StarSystem").GetString();
                var starClass = eventJson.GetProperty("StarClass").GetString();
                SystemPoiList.Clear();

                string upcomingStar;

                if (starClass == "N")
                    upcomingStar = "CAUTION: Non-Sequence star ahead!";
                else
                    upcomingStar = $"a class {starClass} star...";

                TrafficText = $"Standby for system {SystemName}\n" + upcomingStar;
            }

            // Jumped to new system
            if (eventName == "FSDJump")
            {
                TotalBodies.Text = "Awaiting Scan";

                SystemName = eventJson.GetProperty("StarSystem").GetString();
                SystemAddress = eventJson.GetProperty("SystemAddress").GetInt64();
                SystemCoordinates = eventJson.GetProperty("StarPos").EnumerateArray()
                    .Select(coords => coords.GetSingle()).ToArray();

                SystemPoiList.Clear();
                CurrentSystemText = SystemName;

                if (IsEdsmApiReady)
                {
                    await Task.Delay(1000); // wait a second to let EDSM be updated
                    var systemTraffic = await EdsmProvider.GetSystemTrafficAsync(SystemName);
                    if (systemTraffic == null || systemTraffic.id == 0)
                    {
                        TrafficText = "EDSM had no data";
                    }
                    else
                    {
                        TrafficText = $"Discovered by CMDR {systemTraffic?.discovery?.commander} on {systemTraffic?.discovery?.date}" +
                            $"\nTotal Traffic: {systemTraffic.traffic.total} ships ({systemTraffic.traffic.week} this week)";
                    }
                }
            }

            // Honked
            if (eventName == "FSSDiscoveryScan")
            {
                TotalSystemBodies = eventJson.GetProperty("BodyCount").GetInt32();
                TotalSystemNonBodies = eventJson.GetProperty("NonBodyCount").GetInt32();

                // Print total bodies to Textblock
                TotalBodies.Text = TotalSystemBodies.ToString();
            }

            // Scanned a planet
            else if (eventName == "Scan")
            {
                ProcessScannedBody(journalEntry);
            }

            // All bodies scanned
            else if (eventName == "FSSAllBodiesFound")
            {
                TotalBodies.Text = "System Scan Complete";
            }

            // Landable (materials) found
            else if (eventName == "Scan" && journalEntry.Contains("\"Landable\":true"))
            {
                ProcessMaterials(journalEntry, false);
            }

            // Surface Scan Complete
            else if (eventName == "SAAScanComplete")
            {
                int scannedBodyId = eventJson.GetProperty("BodyID").GetInt32();
                var scannedBody = SystemPoiList.FirstOrDefault(poi => poi.BodyID == scannedBodyId);

                if (scannedBody != null)
                {
                    scannedBody.SurfaceScanned = true;
                }
            }

            // FSD Target to calculate remaining jumps
            else if (eventName == "FSDTarget")
            {
                if (journalEntry.Contains("RemainingJumpsInRoute"))
                    RemainingJumps.Text = eventJson.GetProperty("RemainingJumpsInRoute").GetInt32().ToString();
            }

            // Destination Reached
            else if (journalEntry.Contains("DestinationFromHyperspace"))
            {
                RemainingJumps.Text = "Destination Reached!";
                await Task.Delay(10000);
                RemainingJumps.Text = "Awaiting Plotted Route";
            }

            // set the shipID
            // else if (new[] { "SetUserShipName", "ShipyardSwap", "Loadout", "LoadGame" }.Contains(eventName))
            // Removed "ShipyardSwap" because it does not output ShipName anymore. "Loadout" automatically logs after the swap and decalres ShipName and ShipID.
            else if (new[] { "SetUserShipName", "Loadout", "LoadGame" }.Contains(eventName))
            {
                // ShipName
                if (eventJson.TryGetProperty("ShipName", out JsonElement shipNameElement))
                {
                    ShipName = shipNameElement.GetString();
                }

                // ShipID
                if (eventJson.TryGetProperty("ShipID", out JsonElement shipIdElement))
                {
                    ShipId = shipIdElement.GetInt32();
                }

                // Commander
                if (eventJson.TryGetProperty("Commander", out JsonElement commanderElement))
                {
                    CmdrName = commanderElement.GetString();
                }
            }

            // location info
            else if (eventName == "Location")
            {
                SystemName = eventJson.GetProperty("StarSystem").GetString();
                SystemAddress = eventJson.GetProperty("SystemAddress").GetInt64();
                SystemCoordinates = eventJson.GetProperty("StarPos").EnumerateArray()
                    .Select(coords => coords.GetSingle()).ToArray();

                CurrentSystemText = SystemName;
            }

            // ED closed
            else if (eventName == "Shutdown")
            {
                Application.Current.Shutdown();
            }
            else
            {
                CurrentEventText.Text = $"Currently In: {SystemName} \nShip: {ShipName} Cmdr: {CmdrName} \n{journalEntry}";
            }
        }

        private void ProcessScannedBody(string journalEntry)
        {
            JsonElement eventJson = JsonDocument.Parse(journalEntry).RootElement;

            int bodyId = eventJson.GetProperty("BodyID").GetInt32();
            float distance = eventJson.GetProperty("DistanceFromArrivalLS").GetSingle();
            PlanetClass bodyClass = PlanetClass.Icy;

            string bodyName = eventJson.GetProperty("BodyName").GetString()?.Replace(SystemName ?? "NOSYSTEM", string.Empty);
            if (eventJson.TryGetProperty("PlanetClass", out JsonElement jsonPlanetClass))
            {
                bodyClass = MapPlanetClass(jsonPlanetClass.GetString());
            }

            bool isTerraformable = false;
            if (eventJson.TryGetProperty("TerraformState", out JsonElement terraformOptional))
            {
                isTerraformable = terraformOptional.GetString() == "Terraformable";
            }

            if (isTerraformable || bodyClass == PlanetClass.EarthLike || bodyClass == PlanetClass.AmmoniaWorld || bodyClass == PlanetClass.WaterWorld)
            {
                AddSystemPoi(bodyId, bodyName, isTerraformable, bodyClass, (int)distance);
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

        private List<string> ProcessMaterials(string journalEntry, bool isSilent = true)
        {
            var json = JsonDocument.Parse(journalEntry);
            var materials = JsonSerializer.Deserialize<MaterialConcentration[]>(json.RootElement.GetProperty("Materials").GetRawText());
            List<string> newFindings = new List<string>();

            // browse through the materials found and update the dictionary if we have a new record high
            foreach (var material in materials)
            {
                string element = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(material.Name);
                string bodyName = json.RootElement.GetProperty("BodyName").GetString();

                if (!HighestConcentrations.ContainsKey(element))
                {
                    HighestConcentrations.Add(element, new HighestConcentationLocation(decimal.Round(material.Percent, 2), bodyName));
                }
                else if (material.Percent > HighestConcentrations[element].Percent)
                {
                    HighestConcentrations[element] = new HighestConcentationLocation(decimal.Round(material.Percent, 2), bodyName);
                    newFindings.Add($"High contentration ({material.Name}): {material.Percent}");
                    if (!isSilent)
                    {
                        PlayNewHighConcentationFound();
                    }
                }
            }

            return newFindings;
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
            if (Application.Current.Windows.OfType<OptionsWindow>().FirstOrDefault() == null)
            {
                
                OptionsWindow options = new OptionsWindow();
                options.Show();
            }
        }

        private void CopySystem_Click(object sender, EventArgs e)
        {
            var textblock = (sender as TextBlock).Text;

            Clipboard.SetText(textblock.ToString());
        }

        private void PlaySystemPoiFound(PlanetClass planetClass)
        {
            switch (planetClass)
            {
                case PlanetClass.AmmoniaWorld:
                    Player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
                case PlanetClass.WaterWorld:
                    Player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
                case PlanetClass.EarthLike:
                    Player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
                default:
                    Player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/terraform.wav"));
                    break;
            }

            Player.Play();
        }

        private void PlayNewHighConcentationFound()
        {
            Player.Open(new Uri($"{Environment.CurrentDirectory}/sounds/256543__debsound__r2d2-astro-droid.wav"));
            Player.Play();
        }

        private static PlanetClass MapPlanetClass(string edPlanetClass)
        {
            return edPlanetClass switch
            {
                "Icy body" => PlanetClass.Icy,
                "Rocky body" => PlanetClass.Rocky,
                "Rocky ice body" => PlanetClass.RockyIce,
                "Metal rich body" => PlanetClass.MetalRich,
                "Sudarsky class II gas giant" => PlanetClass.ClassIIGasGiant,
                "High metal content body" => PlanetClass.HMC,
                "Water world" => PlanetClass.WaterWorld,
                "Ammonia world" => PlanetClass.AmmoniaWorld,
                "Earthlike body" => PlanetClass.EarthLike,
                _ => PlanetClass.Icy,
            };
        }

        public record MaterialInfo(string Abbreviation, Rarity Rarity);
        public readonly static Dictionary<string, MaterialInfo> MaterialLookup = new Dictionary<string, MaterialInfo>
        {
            {"Carbon", new MaterialInfo("C", Rarity.VeryCommon) },
            {"Iron", new MaterialInfo("Fe", Rarity.VeryCommon) },
            {"Nickel", new MaterialInfo("Ni", Rarity.VeryCommon) },
            {"Phosphorus", new MaterialInfo("P", Rarity.VeryCommon) },
            {"Sulphur", new MaterialInfo("S", Rarity.VeryCommon) },
            {"Arsenic", new MaterialInfo("As", Rarity.Common) },
            {"Chromium", new MaterialInfo("Cr", Rarity.Common) },
            {"Germanium", new MaterialInfo("Ge", Rarity.Common) },
            {"Manganese", new MaterialInfo("Mn", Rarity.Common) },
            {"Vanadium", new MaterialInfo("V", Rarity.Common) },
            {"Zinc", new MaterialInfo("Zn", Rarity.Common) },
            {"Zirconium", new MaterialInfo("Zr", Rarity.Common) },
            {"Cadmium", new MaterialInfo("Cd", Rarity.Uncommon) },
            {"Mercury", new MaterialInfo("Hg", Rarity.Uncommon) },
            {"Molybdenum", new MaterialInfo("Mo", Rarity.Uncommon) },
            {"Niobium", new MaterialInfo("Nb", Rarity.Uncommon) },
            {"Tin", new MaterialInfo("Sn", Rarity.Uncommon) },
            {"Tungsten", new MaterialInfo("W", Rarity.Uncommon) },
            {"Antimony", new MaterialInfo("Sb", Rarity.Rare) },
            {"Polonium", new MaterialInfo("Po", Rarity.Rare) },
            {"Ruthenium", new MaterialInfo("Ru", Rarity.Rare) },
            {"Selenium", new MaterialInfo("Se", Rarity.Rare) },
            {"Technetium", new MaterialInfo("Tc", Rarity.Rare) },
            {"Tellurium", new MaterialInfo("Te", Rarity.Rare) },
            {"Yttrium", new MaterialInfo("Y", Rarity.Rare) }
        };

        #region "For future use"
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

        //private async void MakeProgress()
        //{
        //    IProgress<int> progress = new Progress<int>(percentCompleted =>
        //    {
        //        eventProgressBar.Value = percentCompleted;
        //    });

        //    await Task.Run(async () =>
        //    {
        //        progress.Report(0);
        //        foreach (var i in Enumerable.Range(1, 20))
        //        {
        //            await Task.Delay(1000);
        //            progress.Report(i * 5);
        //        }
        //    });
        //}
        #endregion

        #region Dependency Properties
        private string _trafficText = "Awaiting new system";
        public string TrafficText
        {
            get => _trafficText;

            set
            {
                _trafficText = value;
                NotifyPropertyChanged();
            }
        }

        public string CurrentSystemText
        {
            get => SystemName;
            set
            {
                SystemName = value;
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
        public HighestConcentationLocation(decimal percent, string body) { Percent = percent; BodyName = body; }
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
            get => _surfaceScanned;

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
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(string))
            {
                throw new InvalidOperationException("The target must be a String");
            }

            return String.Join(Environment.NewLine, ((ObservableCollection<string>)value).ToArray());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(bool), typeof(string))]
    public class BoolToMappedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? "mapped" : "not mapped";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(string), typeof(string))]
    public class ElementAbbreviationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Format($"({MainWindow.MaterialLookup[(string)value].Abbreviation})");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion
}
