using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace EDOverlay
{
    public class EdsmApiProvider
    {
        private static readonly HttpClient client = new HttpClient();
        private string CmdrName { get; set; }
        private string ApiKey { get; set; }

        public EdsmApiProvider(string cmdrName, string apiKey)
        {
            CmdrName = cmdrName;
            ApiKey = apiKey;

            client.BaseAddress = new Uri("https://www.edsm.net/");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
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

        public async Task<EdsmLogs> GetSystemLogAsync(string systemName)
        {
            EdsmLogs logs = null;
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
            EdsmTraffic traffic = null;
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

    }
}
