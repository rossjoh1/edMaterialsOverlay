using Microsoft.VisualStudio.TestTools.UnitTesting;
using EDOverlay;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
//using Newtonsoft.Json;

namespace EDOverlay.Tests
{

    [TestClass()]
    public class EdsmApiProviderTests
    {
        static string _edsmCmdrName = "WolfHeart";
        static string _edsmApiKey = "ebb4ccccb06221f6e2eeeb2cd17d6c1c9f270f40";
        EdsmApiProvider edsm = new EdsmApiProvider(_edsmCmdrName, _edsmApiKey);

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

            // act
            var returnValue = await edsm.PostEventIfUseful(journalEntry);

            // assert
            Assert.IsTrue(returnValue.Contains("\"msgnum\":100"));
        }
    }
}