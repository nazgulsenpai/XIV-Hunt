using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FFXIV_GameSense
{
    public class Coord
    {
        public int id { get; set; }
        public int baseId { get; set; }
        public double x { get; set; }
        public double y { get; set; }
        public double x_G { get; set; }
        public double y_G { get; set; }
        public bool possibleSpawn { get; set; }
        public double dateEpoch { get; set; }
        public string updatedBy { get; set; }
        public bool SRank { get; set; }
    }
    public static class HorusReporter
    {
        private static readonly HttpClient Client = new HttpClient();
        private static string url = "https://horus-hunts.net/Radar/";
        public static string TokenURL = "https://horus-hunts.net/Radar/Download";
        //private static string url = "http://localhost:1122/Radar/";
        public static async Task ReportKill(int id, string world, uint iD)
        {
            var values = new Dictionary<string, string>
            {
               { "Id", id.ToString() },
               { "World", world },
               {"Ocurrence", iD.ToString() },
               {"Token", Properties.Settings.Default.HorusToken }
            };

            var content = new FormUrlEncodedContent(values);
            if (!String.IsNullOrEmpty(Properties.Settings.Default.HorusToken))
            {
                try
                {
                    await Client.PostAsync(url + "/ReportKill/", content);
                }
                catch (Exception ex)
                {

                    throw;
                }
            }
        }

        public static async Task ReportPoint(int id, string world, double x, double y, bool idle, uint iD)
        {
            var values = new Dictionary<string, string>
            {
               { "Id", id.ToString() },
               { "World", world},
                {"X", x.ToString() },
                {"Y", y.ToString() },
                {"Idle", idle.ToString() },
                {"Ocurrence", iD.ToString() },
                {"Token", Properties.Settings.Default.HorusToken }
            };

            var content = new FormUrlEncodedContent(values);

            if (!String.IsNullOrEmpty(Properties.Settings.Default.HorusToken))
            {

                try
                {
                    var res = await Client.PostAsync(url + "/ReportPoint/", content);
#if DEBUG
                    Splat.LogHost.Default.Debug(String.Format("POINT: [Id: {0}] [Idle: {1}] [MemID: {2}] / [{3}]", id, idle, iD, await res.Content.ReadAsStringAsync()));
#endif
                }
                catch (Exception ex)
                {

                }
            }
        }
    }
}
     
 

// [HttpPost]
//public async Task<ActionResult> UpdateGID2(int GID2, int HuntId, string Token)