using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Threading.Tasks;
//using Newtonsoft.Json;

namespace EDOverlay.Tests
{

    [TestClass()]
    public class EdsmApiProviderTests
    {
        static readonly IConfiguration config = InitConfiguration();
        static readonly string _edsmApiKey = config["edsmApiKey"];
        static readonly string _edsmCmdrName = config["edsmCmdrName"];

        readonly EdsmApiProvider edsm = new EdsmApiProvider(_edsmCmdrName, _edsmApiKey);

        [TestMethod()]
        public void EdsmApiProviderTest()
        {
            Assert.Fail();
        }

        [TestMethod()]
        public void InitializeTest()
        {
            Assert.Fail();
        }

        [TestMethod()]
        public void GetSystemLogAsyncTest()
        {
            Assert.Fail();
        }

        [TestMethod()]
        public void GetSystemTrafficAsyncTest()
        {
            Assert.Fail();
        }

        [TestMethod()]
        public async Task GetDiscardedEventsAsyncTest()
        {
            // assert
            Assert.IsTrue(await edsm.GetDiscardedEventsAsync());
        }

        [TestMethod()]
        public async Task PostEventIfUsefulTest()
        {
            // arrange
            var journalEntry = @"{""timestamp"":""2020-10-13T06:34:43Z"", ""event"":""FSDJump"", ""StarSystem"":""Plio Chroa FP-W b29-3"", ""SystemAddress"":7330301571329, ""StarPos"":[4635.65625,91.62500,48224.96875], ""SystemAllegiance"":"""", ""SystemEconomy"":""$economy_None;"", ""SystemEconomy_Localised"":""None"", ""SystemSecondEconomy"":""$economy_None;"", ""SystemSecondEconomy_Localised"":""None"", ""SystemGovernment"":""$government_None;"", ""SystemGovernment_Localised"":""None"", ""SystemSecurity"":""$GAlAXY_MAP_INFO_state_anarchy;"", ""SystemSecurity_Localised"":""Anarchy"", ""Population"":0, ""Body"":""Plio Chroa FP-W b29-3"", ""BodyID"":0, ""BodyType"":""Star"", ""JumpDist"":253.698, ""FuelUsed"":7.424023, ""FuelLevel"":12.297239, ""BoostUsed"":4 }";
            int shipId = 13;

            // act
            var returnValue = await edsm.PostEventIfUseful(journalEntry, new EdsmApiProvider.TransientState(shipId));

            // assert
            Assert.IsTrue(returnValue.Contains("\"msgnum\":100"));
        }

        /// <summary>
        /// Import configuration from appsettings for testing
        /// </summary>
        /// <returns></returns>
        public static IConfiguration InitConfiguration()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            return config;
        }
    }
}