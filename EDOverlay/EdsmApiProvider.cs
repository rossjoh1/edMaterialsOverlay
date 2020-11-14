using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace EDOverlay
{
    public class EdsmApiProvider
    {
        private static readonly HttpClient client = new HttpClient();
        private string CmdrName { get; set; }
        private string ApiKey { get; set; }
        private readonly string AppName = "ED Explorers Companion";
        private readonly string AppVersion = "0.1.1";   // for now
        private List<string> discardedEvents { get; set; }

        public List<string> DiscardedEvents { get => discardedEvents; }

        public EdsmApiProvider(string cmdrName, string apiKey)
        {
            CmdrName = cmdrName;
            ApiKey = apiKey;

            client.BaseAddress = new Uri("https://www.edsm.net/");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<bool> Initialize()
        {
            return await GetDiscardedEventsAsync();
        }

        public async Task<EdsmLogs> GetSystemLogAsync(string systemName)
        {
            EdsmLogs logs;
            HttpResponseMessage response = await client.GetAsync($"api-logs-v1/get-logs?commanderName={CmdrName}&apiKey={ApiKey}&systemName={systemName}");
            if (response.IsSuccessStatusCode)
            {
                logs = await response.Content.ReadAsAsync<EdsmLogs>();
                System.Diagnostics.Debug.WriteLine($"Current System (EDSM): {logs?.logs[0]?.system} First: {logs?.logs[0]?.firstDiscoverer}");
                return logs;
            }
            else
            {
                System.Diagnostics.Debug.Write($"Status: {response.IsSuccessStatusCode}");
                return null;
            }
        }

        public async Task<EdsmTraffic> GetSystemTrafficAsync(string systemName)
        {
            EdsmTraffic traffic;
            HttpResponseMessage response = await client.GetAsync($"api-system-v1/traffic?commanderName={CmdrName}&apiKey={ApiKey}&systemName={systemName}");
            if (response.IsSuccessStatusCode)
            {
                traffic = await response.Content.ReadAsAsync<EdsmTraffic>();
                System.Diagnostics.Debug.WriteLine($"Current System (EDSM): {traffic?.name} First: {traffic?.discovery?.commander} on {traffic?.discovery?.date}");
                return traffic;
            }
            else
            {
                System.Diagnostics.Debug.Write($"Status: {response.IsSuccessStatusCode}");
                return null;
            }
        }

        public async Task<bool> GetDiscardedEventsAsync()
        {
            HttpResponseMessage response = await client.GetAsync($"api-journal-v1/discard");

            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine("Connected to EDSM");
                discardedEvents = await response.Content.ReadAsAsync<List<string>>();
                return true;
            }
            else
            {
                System.Diagnostics.Debug.Write("Unable to retrieve discarded events list from EDSM");
                return false;
            }
        }

        public async Task<string> PostEventIfUseful(string journalEntry, TransientState transientState = null)
        {
            // inject transient properties into event
            var eventBody = JsonSerializer.Deserialize<Dictionary<string, object>>(journalEntry);

            if (transientState != null)
            {
                eventBody.Add("_shipId", transientState._shipId);
                eventBody.Add("_systemName", transientState._systemName);
                eventBody.Add("_systemAddress", transientState._systemAddress);
                eventBody.Add("_systemCoordinates", transientState._systemCoordinates);
            }

            var entryWithTransientState = JsonSerializer.Serialize(eventBody);

            System.Diagnostics.Debug.WriteLine($"Event with transients: {entryWithTransientState}");

            // compose body as formparts and post
            var content = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["commanderName"] = CmdrName,
                ["apiKey"] = ApiKey,
                ["fromSoftware"] = AppName,
                ["fromSoftwareVersion"] = AppVersion,
                ["message"] = entryWithTransientState
            });

            HttpResponseMessage response = await client.PostAsync("api-journal-v1", content);

            if (response.IsSuccessStatusCode)
            {
                string r = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Posted event to EDSM - StatusCode: {response.StatusCode}\nResponse: {r}");
                return r;
            }
            else
            {
                System.Diagnostics.Debug.Write($"Error posting event - StatusCode: {response.StatusCode}");
                return String.Empty;
            }
        }

        public class EdsmLogs
        {
            public int msgnum { get; set; }
            public string msg { get; set; }
            public string startDateTime { get; set; }
            public string endDateTime { get; set; }
            public log[] logs { get; set; }

            public class log
            {
                public int shipId { get; set; }
                public string system { get; set; }
                public long systemId { get; set; }
                public bool firstDiscoverer { get; set; }
                public string date { get; set; }
            }
        }

        public class EdsmTraffic
        {
            public int id { get; set; }
            public long id64 { get; set; }
            public string name { get; set; }
            public string url { get; set; }
            public discoveryNode discovery { get; set; }
            public trafficNode traffic { get; set; }

            public class discoveryNode
            {
                public string commander { get; set; }

                private string _date;
                public string date
                {
                    get { return DateTime.Parse(_date).AddYears(1286).ToShortDateString(); }
                    set { _date = value; }
                }
            }

            public class trafficNode
            {
                public int total { get; set; }
                public int week { get; set; }
                public int day { get; set; }
            }
        }

        public class TransientState
        {
            public int _shipId { get; set; }
            public string _systemName { get; set; }
            public long _systemAddress { get; set; }
            public float[] _systemCoordinates { get; set; }
        }
    }
}
