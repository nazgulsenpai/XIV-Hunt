using Splat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FFXIV_GameSense
{
    public static class Aswang
    {
        public static string updateFilename = "";
        public static DateTime reportSUncheckTime;
        public static string versionNumber = "";
        public static string baseURL = "https://raw.githubusercontent.com/nazgulsenpai/Aswang-XIV-Hunt/master/xivhunt/";
        public static bool updateAvailable = false;
        public static bool reportS = false;
        public static bool CheckUser(Entity c)
        {
            LogHost.Default.Debug("Checking if character " + c.Name + " is allowed.");
            string url = "https://raw.githubusercontent.com/nazgulsenpai/Aswang-XIV-Hunt/master/xivhuntallowed/allowed.dat";
            string contents;
            using (var wc = new System.Net.WebClient())
                contents = wc.DownloadString(url);
            int findindex = 0;
            foreach (string name in contents.Split('|'))
            { if (name == c.Name) { findindex = 1; } }
            if (findindex == 0) { return false; }
            else { return true; }
        }
    }

}
