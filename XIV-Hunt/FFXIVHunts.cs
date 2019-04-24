using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using FFXIV_GameSense.Properties;
using System.Threading.Tasks;
using XIVDB;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http;
using System.Windows.Documents;
using Splat;
using XIVAPI;
using System.Threading;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Collections;

namespace FFXIV_GameSense
{
    class FFXIVHunts : IDisposable
    {
        internal static readonly Dictionary<ushort, List<ushort>> MapHunts = new Dictionary<ushort, List<ushort>>
        {
            { 134, new List<ushort>{ 2928,2945,2962 } },
            { 135, new List<ushort>{ 2929,2946,2963 } },
            { 137, new List<ushort>{ 2930,2947,2964 } },
            { 138, new List<ushort>{ 2931,2948,2965 } },
            { 139, new List<ushort>{ 2932,2949,2966 } },
            { 140, new List<ushort>{ 2923,2940,2957 } },
            { 141, new List<ushort>{ 2924,2941,2958 } },
            { 145, new List<ushort>{ 2925,2942,2959 } },
            { 146, new List<ushort>{ 2926,2943,2960 } },
            { 147, new List<ushort>{ 2927,2944,2961 } },//Northern Thanalan
            { 148, new List<ushort>{ 2919,2936,2953 } },
            { 152, new List<ushort>{ 2920,2937,2954 } },//East Shroud
            { 153, new List<ushort>{ 2921,2938,2955 } },
            { 154, new List<ushort>{ 2922,2939,2956 } },
            { 155, new List<ushort>{ 2934,2951,2968 } },//Coerthas Central Highlands
            { 156, new List<ushort>{ 2935,2952,2969 } },
            { 180, new List<ushort>{ 2933,2950,2967 } },
            { 397, new List<ushort>{ 4350,4351,4362,4363,4374 } },
            { 398, new List<ushort>{ 4352,4353,4364,4365,4375 } },//The Dravanian Forelands
            { 399, new List<ushort>{ 4354,4355,4366,4367,4376 } },
            { 400, new List<ushort>{ 4356,4357,4368,4369,4377 } },
            { 401, new List<ushort>{ 4358,4359,4370,4371,4378 } },
            { 402, new List<ushort>{ 4360,4361,4372,4373,4380 } },//Azyz Lla
            { 612, new List<ushort>{ 6008,6009,5990,5991,5987 } },//The Fringes
            { 620, new List<ushort>{ 6010,6011,5992,5993,5988 } },//The Peaks
            { 621, new List<ushort>{ 6012,6013,5994,5995,5989 } },//The Peaks
            { 613, new List<ushort>{ 6002,6003,5996,5997,5984 } },//Ruby Sea
            { 614, new List<ushort>{ 6004,6005,5998,5999,5985 } },//Yanxia
            { 622, new List<ushort>{ 6006,6007,6000,6001,5986 } },//Azim Steppe
        };
        internal List<Hunt> Hunts = new List<Hunt>();
        private static List<FATEReport> FATEs = GameResources.GetFates().Select(x => new FATEReport(x)).ToList();
        private static HashSet<uint> HuntsPutInChat = new HashSet<uint>();
        private static readonly uint[] DCZones = new uint[] { 630, 656, 732, 763, 795, 827 };
        private static HuntsHubConnection hubConnection;
        internal static HttpClient Http { get; private set; } = new HttpClient();
        internal static bool Joined { get; private set; }
        private static bool Joining;
        private static ushort lastJoined, lastZone;
        internal const string baseUrl = "https://xivhunt.net/";
        //internal const string baseUrl = "http://localhost:5000/";
        internal const string VerifiedCharactersUrl = baseUrl + "Manage/VerifiedCharacters";
        private static DateTime ServerTimeUtc;
        private static DateTime LastShoutChatSync;
        private static DataCenterInstanceMatchInfo DCInstance;
        private readonly Window1 w1;

        internal async Task LeaveGroup()
        {
            if (!Joined)
                return;
            await LeaveDCZone();
            await hubConnection.Connection.InvokeAsync(nameof(LeaveGroup), lastJoined);
            LogHost.Default.Info("Left " + GameResources.GetWorldName(lastJoined));
            Joined = false;
        }

        internal FFXIVHunts(Window1 pw1)
        {
            w1 = pw1;
            foreach (KeyValuePair<ushort, HuntRank> kvp in Hunt.RankMap)
                Hunts.Add(new Hunt(kvp.Key));

            CreateConnection();
        }

        private void CreateConnection()
        {
            if (hubConnection == null)
            {
                hubConnection = new HuntsHubConnection();
                RegisterHubMethods();
            }
        }

        private void RegisterHubMethods()
        {
            hubConnection.Connection.On(nameof(ReceiveHunt), (Action<Hunt>)(hunt => { ReceiveHunt(hunt); }));
            hubConnection.Connection.On(nameof(ReceiveFATE), (Action<FATEReport>)(fate => { ReceiveFATE(fate); }));
            hubConnection.Connection.On(nameof(DCInstanceMatch), (Action<DataCenterInstanceMatchInfo>)(instance => { DCInstanceMatch(instance); }));
            hubConnection.Connection.On<int>("ConnectedCount", connectedCount =>
            {
                w1.HuntConnectionTextBlock.Dispatcher.Invoke(() => w1.HuntConnectionTextBlock.Text = string.Format(Resources.FormConnectedToCount, GameResources.GetWorldName(Program.mem.GetWorldId()), connectedCount - 1));
            });
            hubConnection.Connection.Closed += Connection_Closed;
        }

        private static void DCInstanceMatch(DataCenterInstanceMatchInfo instance)
        {
            string s = string.Format(Resources.DCInstanceMatch, Program.AssemblyName.Name, (ServerTimeUtc - instance.StartTime).TotalMinutes.ToString("F0"), $"{baseUrl}DCInstance/{instance.ID}");
            LogHost.Default.Info("DCInstanceMatch: " + s);
            DCInstance = instance;
            ChatMessage cm = new ChatMessage { MessageString = s };
            _ = Program.mem.WriteChatMessage(cm);
        }

        private void ReceiveFATE(FATEReport fate)
        {
            //LogHost.Default.Debug(string.Format("[{0}] Report received: {1} - {2}%", GameResources.GetWorldName(fate.WorldId), fate.Name(true), fate.Progress));
            if (PutInChat(fate) && Settings.Default.FlashTaskbarIconOnHuntAndFATEs)
                NativeMethods.FlashTaskbarIcon(Program.mem.Process);
        }

        private void ReceiveHunt(Hunt hunt)
        {
            LogHost.Default.Debug(string.Format("[{0}] Report received: {1}", GameResources.GetWorldName(hunt.WorldId), hunt.Name));
            if (PutInChat(hunt) && Settings.Default.FlashTaskbarIconOnHuntAndFATEs)
                if (hunt.LastAlive)
                    NativeMethods.FlashTaskbarIcon(Program.mem.Process, 45, true);
                else
                    NativeMethods.StopFlashWindowEx(Program.mem.Process);
        }

        private Task Connection_Closed(Exception arg)
        {
            Joined = Joining = false;
            return Task.CompletedTask;
        }

        internal HuntRank HuntRankFor(ushort HuntID)
        {
            if (Hunts.Any(x => x.Id == HuntID))
                return Hunts.Single(x => x.Id == HuntID).Rank;
            throw new ArgumentException("Unknown hunt", nameof(HuntID));
        }

        internal async Task Connect()
        {
            CreateConnection();
            if (await hubConnection.Connect(w1))
                RegisterHubMethods();
            if (!Joined && hubConnection.Connected)
                await JoinServerGroup();
        }

        private bool PutInChat(FATEReport fate)
        {
            int idx = FATEs.IndexOf(fate);
            if (idx == -1)
                return false;
            FATEs[idx].State = fate.State;
            FATEs[idx].StartTimeEpoch = fate.StartTimeEpoch;
            FATEs[idx].Duration = fate.Duration;
            FATEs[idx].Progress = fate.Progress;
            bool skipAnnounce = Settings.Default.NoAnnouncementsInContent && Program.mem.GetCurrentContentFinderCondition() > 0
                || (Math.Abs(fate.TimeRemaining.TotalHours) < 3 && fate.TimeRemaining.TotalMinutes < Settings.Default.FATEMinimumMinutesRemaining)
                || ((fate.State == FATEState.Preparation) ? FATEs[idx].lastPutInChat > Program.mem.GetServerUtcTime().AddMinutes(-10) : Math.Abs(fate.Progress - FATEs[idx].LastReportedProgress) < Settings.Default.FATEMinimumPercentInterval && Settings.Default.FATEMinimumPercentInterval > 0);
            if (FateNotifyCheck(FATEs[idx].ID) && FATEs[idx].lastPutInChat < Program.mem.GetServerUtcTime().AddMinutes(-Settings.Default.FATEInterval) && !fate.HasEnded && !skipAnnounce)
            {
                ChatMessage cm = new ChatMessage();
                string postpend;
                if (fate.State == FATEState.Preparation)
                    postpend = Resources.PreparationState;
                else if (Math.Abs(fate.TimeRemaining.TotalHours) > 3)//StartTimeEpoch isn't set during the first few seconds
                    postpend = fate.Progress + "%";
                else
                    postpend = string.Format(Resources.FATEPrcTimeRemaining, fate.Progress, (int)fate.TimeRemaining.TotalMinutes, fate.TimeRemaining.Seconds.ToString("D2"));
                cm = ChatMessage.MakePosChatMessage(string.Format(GetWorldPrepend(fate.WorldId) + Resources.FATEMsg, FATEs[idx].Name()), fate.ZoneID, fate.PosX, fate.PosY, " " + postpend);
                _ = Program.mem.WriteChatMessage(cm);
                CheckAndPlaySound(HuntRank.FATE);
                FATEs[idx].lastPutInChat = Program.mem.GetServerUtcTime();
                FATEs[idx].LastReportedProgress = fate.Progress;
                //if (fate.Progress > 99)
                //    fates[idx].lastReportedDead = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        private string GetWorldPrepend(ushort wid)
        {
            if (Settings.Default.NotificationsFromOtherWorlds)
                return $"[{GameResources.GetWorldName(wid)}] ";
            return string.Empty;
        }

        internal async Task LastKnownInfoForHunt(ushort id)
        {
            World world;
            string e;
            HttpResponseMessage r = await Http.GetAsync(baseUrl + "api/worlds/" + Program.mem.GetWorldId().ToString());
            if (r.IsSuccessStatusCode)
                e = await r.Content.ReadAsStringAsync();
            else
                return;
            world = JsonConvert.DeserializeObject<World>(e);
            Hunt result = world.Hunts.First(x => x.Id == id);
            if (result == null)
                return;
            TimeSpan timeSinceLastReport = ServerTimeUtc.Subtract(result.LastReported);
            if (timeSinceLastReport < TimeSpan.Zero)
                timeSinceLastReport = TimeSpan.Zero;
            ChatMessage cm = new ChatMessage();
            double TotalHours = Math.Floor(timeSinceLastReport.TotalHours);
            if (!result.LastAlive)
            {
                cm.MessageString = string.Format(Resources.LKIHuntKilled, result.Name);
                if (Resources.LKIHuntKilled.Contains("<time>"))//japanese case
                    cm.MessageString += cm.MessageString.Replace("<time>", string.Format(Resources.LKIHoursMinutes, TotalHours, timeSinceLastReport.Minutes));
                else if (timeSinceLastReport.TotalDays > 90)
                    cm.MessageString = string.Format(Resources.LKIHuntNotReported, result.Name);
                else if (timeSinceLastReport.TotalHours > 72)
                    cm.MessageString += string.Format(Resources.LKIHours, TotalHours);
                else if (timeSinceLastReport.TotalHours < 1)
                    cm.MessageString += string.Format(Resources.LKIMinutes, Math.Floor(timeSinceLastReport.TotalMinutes));
                else
                    cm.MessageString += string.Format(Resources.LKIHoursMinutes, TotalHours, timeSinceLastReport.Minutes);
            }
            else
            {
                ushort zid = GetZoneId(result.Id);
                cm = ChatMessage.MakePosChatMessage(string.Format(Resources.LKILastSeenAt, result.Name), zid, result.LastX, result.LastY, string.Format(Resources.LKIHoursMinutes, TotalHours, timeSinceLastReport.Minutes));
            }
            await Program.mem.WriteChatMessage(cm);
        }

        internal async Task LastKnownInfoForFATE(ushort id)
        {
            if (!hubConnection.Connected || !Joined)
                return;
            FATE result = await hubConnection.Connection.InvokeAsync<FATEReport>("QueryFATE", id);
            if (result == null)
                return;
            TimeSpan timeSinceLastReport = ServerTimeUtc.Subtract(result.LastReported);
            if (timeSinceLastReport < TimeSpan.Zero)
                timeSinceLastReport = TimeSpan.Zero;
            ChatMessage cm = new ChatMessage();
            if (timeSinceLastReport.TotalDays > 90)
                cm.MessageString = string.Format(Resources.LKIHuntNotReported, result.Name());
            else if (timeSinceLastReport.TotalHours > 100)
                cm.MessageString = string.Format(Resources.LKIFATEDays, result.Name(), Convert.ToUInt32(timeSinceLastReport.TotalDays));
            else
                cm = ChatMessage.MakePosChatMessage(string.Format(Resources.LKIFATE, result.Name(), Math.Floor(timeSinceLastReport.TotalHours), timeSinceLastReport.Minutes), result.ZoneID, result.PosX, result.PosY);
            await Program.mem.WriteChatMessage(cm);
        }

        internal async Task<Item> QueryItem(string itemsearch) => await hubConnection.Connection.InvokeAsync<Item>(nameof(QueryItem), itemsearch, Thread.CurrentThread.CurrentUICulture.Name);

        internal async void Check(FFXIVMemory mem)
        {
            if (!hubConnection.Connected)
                await Connect();
            if (!hubConnection.Connected)
                return;
            if (lastJoined != mem.GetWorldId() && Joined && !Joining || !Joined)
            {
                await LeaveGroup();
                await JoinServerGroup();
            }
            ServerTimeUtc = mem.GetServerUtcTime();
            ushort thisZone = mem.GetZoneId();
            if (thisZone != lastZone && Settings.Default.OncePerHunt && Settings.Default.ForgetOnZoneChange)
            {
                HuntsPutInChat.Clear();
            }

            if (Array.IndexOf(DCZones, thisZone) > -1 && Array.IndexOf(DCZones, lastZone) == -1 && Joined)
            {
                LastShoutChatSync = await JoinDCZone(thisZone);
            }
            else if (Array.IndexOf(DCZones, lastZone) > -1 && Array.IndexOf(DCZones, thisZone) == -1)
            {
                await LeaveDCZone();
            }
            lastZone = thisZone;
            foreach (Monster c in mem.Combatants.OfType<Monster>().Where(c => Hunts.Exists(h => h.Id == c.BNpcNameID && GetZoneId(c.BNpcNameID) == thisZone)))
            {
                _ = ReportHunt(c);
            }
            if (Array.IndexOf(DCZones, thisZone) > -1 && LastShoutChatSync != null)
            {
                await ReportDCShoutChat(mem.ReadChatLogBackwards(filter: x => x.Channel == ChatChannel.Shout, stopOn: x => x.Timestamp <= LastShoutChatSync).OrderByDescending(x => x.Timestamp).Take(10));
            }
            foreach (FATE f in mem.GetFateList().Where(f => f.ZoneID == thisZone))
            {
                _ = ReportFate(f);
                if (f.IsDataCenterShared() && PutInChat(new FATEReport(f) { WorldId = mem.GetWorldId() }) && Settings.Default.FlashTaskbarIconOnHuntAndFATEs)
                    NativeMethods.FlashTaskbarIcon(mem.Process);
            }
        }

        private async Task LeaveDCZone()
        {
            Debug.WriteLine(nameof(LeaveDCZone));
            try
            {
                if (hubConnection.Connected && Joined)
                    await hubConnection.Connection.InvokeAsync(nameof(LeaveDCZone));//TODO: fix as it may have been disposed
            }
            catch (Exception e) { LogHost.Default.WarnException(nameof(LeaveDCZone), e); }
        }

        private async Task<DateTime> JoinDCZone(ushort zoneid)
        {
            Debug.WriteLine(nameof(JoinDCZone) + " " + zoneid);
            try
            {
                if (hubConnection.Connected && Joined)
                    return await hubConnection.Connection.InvokeAsync<DateTime>(nameof(JoinDCZone), zoneid, DCInstance?.ID > 0 ? DCInstance?.ID : 0);
            }
            catch (Exception e) { LogHost.Default.WarnException(nameof(JoinDCZone), e); }
            return DateTime.MaxValue;
        }

        private async Task ReportDCShoutChat(IEnumerable<ChatMessage> recentShoutChat)
        {
            if (recentShoutChat.Any() && hubConnection.Connected && Joined)
            {
                try
                {
                    await hubConnection.Connection.InvokeAsync(nameof(ReportDCShoutChat), recentShoutChat);
                    LastShoutChatSync = recentShoutChat.Max(x => x.Timestamp);
                }
                catch (Exception e) { LogHost.Default.WarnException(nameof(ReportDCShoutChat), e); }
            }
        }

        internal async Task RandomPositionForBNpc(ushort bnpcid)
        {
            EnemyObject Enemy;
            string e;
            HttpResponseMessage r = await Http.GetAsync("https://api.xivdb.com/enemy/" + bnpcid);
            if (r.IsSuccessStatusCode)
                e = await r.Content.ReadAsStringAsync();
            else
                return;
            Enemy = JsonConvert.DeserializeObject<EnemyObject>(e);
            if (Enemy == null)
                return;
            //if (Enemy.Map_data.Points.All(x => x.App_data.Fate.Is_fate))
            //    return;//TODO: Redirect to FATE
            else if (Enemy.Map_data.Points.Any(x => x.App_data.Fate.Is_fate) && !Enemy.Map_data.Points.All(x => x.App_data.Fate.Is_fate))
            {   //Don't output FATE spawn points
                var temppoints = Enemy.Map_data.Points.ToList();
                temppoints.RemoveAll(x => x.App_data.Fate.Is_fate);
                Enemy.Map_data.Points = temppoints.ToArray();
            }
            int n = new Random().Next(0, Enemy.Map_data.Points.Length);
            ChatMessage cm = ChatMessage.MakePosChatMessage(string.Format(Resources.LKICanBeFoundAt, GameResources.GetEnemyName(bnpcid, true)), GameResources.MapIdToZoneId(Enemy.Map_data.Points[n].Map_id), Enemy.Map_data.Points[n].App_position.Position.X, Enemy.Map_data.Points[n].App_position.Position.Y, mapId: Enemy.Map_data.Points[n].Map_id);
            await Program.mem.WriteChatMessage(cm);
        }


        private async Task ReportFate(FATE f)
        {
            int idx = FATEs.FindIndex(h => h.ID == f.ID);
            if (idx < 0 || (FATEs[idx].LastReported > ServerTimeUtc.AddSeconds(-5) && !(FATEs[idx].Progress != 100 && f.Progress > 99)))
                return;

            FATEs[idx].LastReported = ServerTimeUtc;
            //too pussy to use copy constructor
            FATEs[idx].Progress = f.Progress;
            FATEs[idx].PosX = f.PosX;
            FATEs[idx].PosZ = f.PosZ;
            FATEs[idx].PosY = f.PosY;
            FATEs[idx].ZoneID = f.ZoneID;
            FATEs[idx].Duration = f.Duration;
            FATEs[idx].StartTimeEpoch = f.StartTimeEpoch;
            FATEs[idx].State = f.State;
            FATEs[idx].ZoneID = f.ZoneID;

            try
            {
                if (hubConnection.Connected && Joined)
                    await hubConnection.Connection.InvokeAsync(nameof(ReportFate), FATEs[idx]);
            }
            catch (Exception e) { LogHost.Default.WarnException(nameof(ReportFate), e); }
        }

        private static bool FateNotifyCheck(ushort id)
        {
            //Get first ID for the FATE with this name
            id = GameResources.GetFateId(GameResources.GetFATEInfo(id)?.Name);
            return Settings.Default.FATEs.Contains(id);
        }

        private async Task JoinServerGroup()
        {
            if (Joined && hubConnection.Connected || Joining)
                return;
            Joining = true;
            w1.HuntConnectionTextBlock.Dispatcher.Invoke(() => w1.HuntConnectionTextBlock.Text = Resources.FormReadingSID);
            ushort sid = Program.mem.GetWorldId();
            Reporter r = new Reporter(sid, sid, Program.mem.GetSelfCombatant().Name, Hunts.AsReadOnly(), hubConnection.Connection);
            LogHost.Default.Info("Joining " + GameResources.GetWorldName(sid));
            JoinGroupResult result = await hubConnection.Connection.InvokeAsync<JoinGroupResult>("JoinGroup", r);
            if (result == JoinGroupResult.Denied)
                w1.HuntConnectionTextBlock.Dispatcher.Invoke(() =>
                {
                    w1.HuntConnectionTextBlock.Inlines.Clear();
                    w1.HuntConnectionTextBlock.Inlines.Add(string.Format(Resources.FormFailedToJoin, $"{r.Name} ({GameResources.GetWorldName(sid)})").Replace(UI.LogInForm.XIVHuntNet, string.Empty));
                    Hyperlink link = new Hyperlink(new Run(UI.LogInForm.XIVHuntNet)) { NavigateUri = new Uri(VerifiedCharactersUrl) };
                    link.RequestNavigate += UI.LogInForm.Link_RequestNavigate;
                    w1.HuntConnectionTextBlock.Inlines.Add(link);
                });
            else if (result == JoinGroupResult.Locked)
                w1.HuntConnectionTextBlock.Dispatcher.Invoke(() => w1.HuntConnectionTextBlock.Text = string.Format(Resources.FormJoinLocked, Program.AssemblyName.Name));
            Joining = false;
            Joined = true;//result != JoinGroupResult.Denied
            lastJoined = sid;
            foreach (Hunt h in Hunts)
                h.WorldId = sid;
            foreach (FATEReport f in FATEs)
                f.WorldId = sid;
            ushort zid = Program.mem.GetZoneId();
            if (Array.IndexOf(DCZones, zid) > -1)
                await JoinDCZone(zid);
        }

        private bool PutInChat(Hunt hunt)
        {
            if (Settings.Default.NoAnnouncementsInContent && Program.mem.GetCurrentContentFinderCondition() > 0)
                return false;
            int idx = Hunts.IndexOf(hunt);
            ChatMessage cm = new ChatMessage();
            if (Settings.Default.OncePerHunt ? !HuntsPutInChat.Contains(hunt.OccurrenceID) : Hunts[idx].lastPutInChat < Program.mem.GetServerUtcTime().AddMinutes(-Settings.Default.HuntInterval) && hunt.LastAlive /*&& hunts[idx].lastReportedDead < Program.mem.GetServerUtcTime().AddSeconds(-15)*/)
            {
                cm = ChatMessage.MakePosChatMessage(GetWorldPrepend(hunt.WorldId) + string.Format(Resources.HuntMsg, hunt.Rank.ToString(), hunt.Name), GetZoneId(hunt.Id), hunt.LastX, hunt.LastY);
                if (cm != null)
                {
                    _ = Program.mem.WriteChatMessage(cm);
                    CheckAndPlaySound(hunt.Rank);
                    Hunts[idx] = hunt;
                    HuntsPutInChat.Add(hunt.OccurrenceID);
                    Hunts[idx].lastPutInChat = Program.mem.GetServerUtcTime();
                    return true;
                }
            }
            else if (Hunts[idx].lastReportedDead < ServerTimeUtc.AddSeconds(-12) && !hunt.LastAlive)
            {
                cm.MessageString = string.Format(Resources.HuntMsgKilled, hunt.Rank.ToString(), hunt.Name);
                if (cm != null)
                {
                    _ = Program.mem.WriteChatMessage(cm);
                    Hunts[idx] = hunt;
                    Hunts[idx].lastReportedDead = Program.mem.GetServerUtcTime();
                    return true;
                }
            }
            return false;
        }

        private static ushort GetZoneId(ushort huntId)
        {
            foreach (KeyValuePair<ushort, List<ushort>> m in MapHunts)
            {
                if (m.Value.Contains(huntId))
                    return m.Key;
            }
            return 0;
        }

        private void CheckAndPlaySound(HuntRank r)
        {
            try
            {
                if (w1.sounds.TryGetValue(r, out var soundPlayer))
                    _ = SoundPlayer.Play(soundPlayer);
            }
            catch (Exception ex) { LogHost.Default.ErrorException(nameof(CheckAndPlaySound), ex); }
        }
        // ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- -------
        private async Task ReportHunt(Monster c)
        {

            int idx = Hunts.FindIndex(h => h.Id == c.BNpcNameID);
            LogHost.Default.Debug(c.CurrentHP.ToString() + "/" + c.MaxHP.ToString());
            if (Hunts[idx].Rank != HuntRank.S || (Hunts[idx].Rank == HuntRank.S && Aswang.reportS))
            {
                LogHost.Default.Debug(Hunts[idx].Rank + " " + Hunts[idx].Name + " - Reporting ");
                if (Hunts[idx].LastReported > ServerTimeUtc.AddSeconds(-5) && c.CurrentHP > 0)
                    return;//no need to report this often
                           //else if (!hunts[idx].LastAlive && hunts[idx].LastReported > DateTime.UtcNow.AddSeconds(-5))
                           //    return;

                Hunts[idx].LastReported = ServerTimeUtc;
                Hunts[idx].LastX = c.PosX;
                Hunts[idx].LastY = c.PosY;
                Hunts[idx].OccurrenceID = c.ID;
                Hunts[idx].LastAlive = (c.CurrentHP > 0) ? true : false;

                try
                {
                    if (Joined)
                        await hubConnection.Connection.InvokeAsync(nameof(ReportHunt), Hunts[idx]);
                }
                catch (Exception e) { LogHost.Default.WarnException(nameof(ReportHunt), e); }
            }
            else
            {
//---------------------------------------Horus Reports
                if (Hunts[idx].LastAlive && Hunts[idx].Rank != HuntRank.S)
                {
                    await HorusReporter.ReportPoint(Hunts[idx].Id, GameResources.GetWorldName(Hunts[idx].WorldId), Hunts[idx].LastX, Hunts[idx].LastY, Hunts[idx].LastAlive, Hunts[idx].OccurrenceID);
                }
                if (!Hunts[idx].LastAlive && Hunts[idx].Rank == HuntRank.S)
                {
                    await HorusReporter.ReportPoint(Hunts[idx].Id, GameResources.GetWorldName(Hunts[idx].WorldId), Hunts[idx].LastX, Hunts[idx].LastY, Hunts[idx].LastAlive, Hunts[idx].OccurrenceID);
                    await HorusReporter.ReportKill(Hunts[idx].Id, GameResources.GetWorldName(Hunts[idx].WorldId), Hunts[idx].OccurrenceID);
                }
//---------------------------------------Horus Reports
                if (Hunts[idx].LastReported < ServerTimeUtc.AddMinutes(-Settings.Default.HuntInterval)) //if (Hunts[idx].LastReported = ServerTimeUtc; >= DateTime.Now)
                {
                    LogHost.Default.Debug(Hunts[idx].Rank + " " + Hunts[idx].Name + " - Not Reported");
                    ChatMessage cm = new ChatMessage();
                    cm = ChatMessage.MakePosChatMessage(string.Format(Resources.HuntMsg, "[Not Reported] " + Hunts[idx].Rank.ToString(), c.Name), GetZoneId(Hunts[idx].Id), c.PosX, c.PosY);
                    await Program.mem.WriteChatMessage(cm);
                    if ((Hunts[idx].IsARR && Hunts[idx].Rank == HuntRank.B && Settings.Default.BARR && Settings.Default.notifyB)
                        || (Hunts[idx].IsARR && Hunts[idx].Rank == HuntRank.A && Settings.Default.AARR && Settings.Default.notifyA)
                        || (Hunts[idx].IsARR && Hunts[idx].Rank == HuntRank.S && Settings.Default.SARR && Settings.Default.notifyS)
                        || (Hunts[idx].IsHW && Hunts[idx].Rank == HuntRank.B && Settings.Default.BHW && Settings.Default.notifyB)
                        || (Hunts[idx].IsHW && Hunts[idx].Rank == HuntRank.A && Settings.Default.AHW && Settings.Default.notifyA)
                        || (Hunts[idx].IsHW && Hunts[idx].Rank == HuntRank.S && Settings.Default.SHW && Settings.Default.notifyS)
                        || (Hunts[idx].IsSB && Hunts[idx].Rank == HuntRank.B && Settings.Default.BSB && Settings.Default.notifyB)
                        || (Hunts[idx].IsSB && Hunts[idx].Rank == HuntRank.A && Settings.Default.ASB && Settings.Default.notifyA)
                        || (Hunts[idx].IsSB && Hunts[idx].Rank == HuntRank.S && Settings.Default.SSB && Settings.Default.notifyS)
                        ) { CheckAndPlaySound(Hunts[idx].Rank); }
                    Hunts[idx].LastReported = ServerTimeUtc;
                }
            }
        }
         //------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- ------- -------
        public void Dispose()
        {
            _ = LeaveGroup();
            hubConnection.Connection.DisposeAsync();
        }
    }

    public class DataCenterInstanceMatchInfo
    {
        public uint ID { get; set; }
        public DateTime StartTime { get; set; }
    }

    class Hunt
    {
        [JsonProperty("wId")]
        internal ushort WorldId { get; set; }
        [JsonProperty]
        internal ushort Id { get; set; }
        [JsonProperty("r")]
        internal HuntRank Rank => RankMap[Id];
        [JsonProperty]
        internal DateTime LastReported { get; set; }
        //[JsonProperty("i")]
        //internal byte instance { get; set; }
        internal DateTime lastPutInChat = DateTime.MinValue;
        internal DateTime lastReportedDead = DateTime.MinValue;
        [JsonProperty("x")]
        internal float LastX { get; set; }
        [JsonProperty("y")]
        internal float LastY { get; set; }
        [JsonProperty]
        internal bool LastAlive { get; set; }
        [JsonProperty]
        internal uint OccurrenceID { get; set; }
        [JsonIgnore]
        internal string WorldName => GameResources.GetWorldName(WorldId);
        [JsonIgnore]
        internal string Name => GameResources.GetEnemyName(Id);
        [JsonIgnore]
        internal static readonly Dictionary<ushort, HuntRank> RankMap = IdexRanks();

        private static Dictionary<ushort, HuntRank> IdexRanks()
        {
            Dictionary<ushort, HuntRank> r = new Dictionary<ushort, HuntRank>();
            //4.0
            for (ushort i = 6002; i < 6014; i++)
                r.Add(i, HuntRank.B);
            for (ushort i = 5990; i < 6002; i++)
                r.Add(i, HuntRank.A);
            for (ushort i = 5984; i < 5990; i++)
                r.Add(i, HuntRank.S);

            //3.0
            for (ushort i = 4350; i < 4362; i++)
                r.Add(i, HuntRank.B);
            for (ushort i = 4362; i < 4374; i++)
                r.Add(i, HuntRank.A);
            for (ushort i = 4374; i < 4381; i++)
                r.Add(i, HuntRank.S);
            r.Remove(4379);

            //2.0
            for (ushort i = 2919; i < 2936; i++)
                r.Add(i, HuntRank.B);
            for (ushort i = 2936; i < 2953; i++)
                r.Add(i, HuntRank.A);
            for (ushort i = 2953; i < 2970; i++)
                r.Add(i, HuntRank.S);
            return r;
        }

        public Hunt() { }//necessary for SignalR receive

        internal Hunt(ushort _id)
        {
            WorldId = Program.mem.GetWorldId();
            Id = _id;
            LastReported = DateTime.MinValue;
        }

        internal bool IsARR => Id < 3000;

        internal bool IsHW => Id > 3000 && Id < 5000;

        internal bool IsSB => Id > 5000 && Id < 7000;

        public override bool Equals(object obj)
        {
            if (!(obj is Hunt item))
            {
                return false;
            }
            return Id.Equals(item.Id) && WorldId.Equals(item.WorldId);
        }

        public override int GetHashCode() => Id.GetHashCode();

        internal static bool TryGetHuntRank(ushort HuntID, out HuntRank hr) => (RankMap.TryGetValue(HuntID, out hr)) ? true : false;
    }

    class Reporter
    {
        public ushort HomeWorldID { get; private set; }
        public ushort CurrentWorldID { get; private set; }
        public string Name { get; private set; }
        public Version Version = Program.AssemblyName.Version;
        public ObservableHashSet<ushort> SubscribedHunts => Settings.Default.Hunts;
        public ObservableHashSet<ushort> SubscribedFATEs => Settings.Default.FATEs;
        public bool SubscribedToOtherWorlds => Settings.Default.NotificationsFromOtherWorlds;
        private readonly IReadOnlyList<Hunt> Hunts;
        private readonly HubConnection hubConnection;

        public Reporter(ushort hwid, ushort cwid, string name, IReadOnlyList<Hunt> hunts, in HubConnection connection)
        {
            HomeWorldID = hwid;
            CurrentWorldID = cwid;
            Name = name;
            Hunts = hunts;
            hubConnection = connection;
            RefreshSubscribedHuntIDs();
            Settings.Default.PropertyChanged += ReporterSettingsPropertyChanged;
            SubscribedHunts.CollectionChanged += SubscribedHunt_CollectionChanged;
            SubscribedFATEs.CollectionChanged += SubscribedFATEs_CollectionChanged;
        }

        private void ReporterSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Debug.WriteLine($"[{nameof(Reporter)}] {nameof(ReporterSettingsPropertyChanged)}:{e.PropertyName}");
            if (e.PropertyName == nameof(Settings.Default.NotificationsFromOtherWorlds))
            {
                hubConnection.SendAsync("SetSubscribedToOtherWorlds", Settings.Default.NotificationsFromOtherWorlds);
                if (Settings.Default.NotificationsFromOtherWorlds)
                    LogHost.Default.Info($"[{nameof(Reporter)}] Subscribed to other worlds on this datacenter");
                else
                    LogHost.Default.Info($"[{nameof(Reporter)}] Unsubscribed from other worlds");
            }
            else if (e.PropertyName.StartsWith("notify") && e.PropertyName.Length == 7 || (e.PropertyName.All(x => char.IsUpper(x)) && e.PropertyName.Length < 5))
                RefreshSubscribedHuntIDs();
        }

        private void SubscribedHunt_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Debug.WriteLine($"[{nameof(Reporter)}] {nameof(SubscribedHunt_CollectionChanged)}:{e.Action.ToString()}");
            hubConnection.SendAsync("SetSubscription", new SubscriptionUpdate(nameof(Hunt), e.Action, e.NewItems?.Count > 0 ? e.NewItems : e.OldItems));
            LogInfo(nameof(SubscribedHunt_CollectionChanged), e);
        }

        private void SubscribedFATEs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Debug.WriteLine($"[{nameof(Reporter)}] {nameof(SubscribedFATEs_CollectionChanged)}:{e.Action.ToString()}");
            hubConnection.SendAsync("SetSubscription", new SubscriptionUpdate(nameof(FATEReport), e.Action, e.NewItems?.Count > 0 ? e.NewItems : e.OldItems));
            LogInfo(nameof(SubscribedFATEs_CollectionChanged), e);
        }

        private void LogInfo(string methodName, NotifyCollectionChangedEventArgs e)
        {
            string info = $"{methodName}:{e.Action.ToString()}{Environment.NewLine}";
            foreach (object i in e.NewItems ?? new List<object>())
                info += "+" + i + ((e.NewItems.IndexOf(i) + 1) % 3 == 0 ? Environment.NewLine : " ");
            foreach (object i in e.OldItems ?? new List<object>())
                info += "-" + i + ((e.OldItems.IndexOf(i) + 1) % 3 == 0 ? Environment.NewLine : " ");
            LogHost.Default.Info(info);
        }

        //I threw up in my mouth a little bit
        private void RefreshSubscribedHuntIDs()
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            HashSet<ushort> newSubscribedHuntIDsHS = new HashSet<ushort>();
            if (Settings.Default.notifyS)
                newSubscribedHuntIDsHS.AddRange(Hunts.Where(x => x.Rank == HuntRank.S && ((x.IsARR && Settings.Default.SARR) || x.IsHW && Settings.Default.SHW || x.IsSB && Settings.Default.SSB)).Select(x => x.Id));
            if (Settings.Default.notifyA)
                newSubscribedHuntIDsHS.AddRange(Hunts.Where(x => x.Rank == HuntRank.A && ((x.IsARR && Settings.Default.AARR) || x.IsHW && Settings.Default.AHW || x.IsSB && Settings.Default.ASB)).Select(x => x.Id));
            if (Settings.Default.notifyB)
                newSubscribedHuntIDsHS.AddRange(Hunts.Where(x => x.Rank == HuntRank.B && ((x.IsARR && Settings.Default.BARR) || x.IsHW && Settings.Default.BHW || x.IsSB && Settings.Default.BSB)).Select(x => x.Id));
            SubscribedHunts.RemoveRange(SubscribedHunts.Except(newSubscribedHuntIDsHS));
            SubscribedHunts.AddRange(newSubscribedHuntIDsHS);
            s.Stop();
            Debug.WriteLine(nameof(RefreshSubscribedHuntIDs) + ": " + s.ElapsedTicks);
        }
    }

    class FATEReport : FATE
    {
        [JsonProperty("wId")]
        public ushort WorldId { get; set; }
        //public byte Instance { get; set; }
        [JsonIgnore]
        public DateTime lastPutInChat = DateTime.MinValue;
        [JsonIgnore]
        public DateTime lastReportedDead = DateTime.MinValue;
        [JsonIgnore]
        public byte LastReportedProgress = byte.MaxValue;
        public FATEReport() : base()
        { }

        public FATEReport(FATE fate) : base(fate)
        { }


        public override bool Equals(object obj)
        {
            if (!(obj is FATEReport item))
            {
                return false;
            }
            return ID.Equals(item.ID) && WorldId.Equals(item.WorldId);
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }
    }

    [JsonObject]
    class World
    {
        [JsonProperty("id")]
        internal ushort Id { get; set; }
        [JsonProperty("hunts")]
        internal List<Hunt> Hunts { get; set; }
    }

    enum HuntRank
    {
        B,
        A,
        S,
        FATE
    }

    public enum JoinGroupResult
    {
        Denied,
        Joined,
        Locked
    }

    class SubscriptionUpdate
    {
        public string Type { get; private set; }
        public NotifyCollectionChangedAction ChangeAction { get; private set; }
        public IEnumerable Items { get; private set; }
        
        public SubscriptionUpdate(string t, NotifyCollectionChangedAction changedAction, IEnumerable items)
        {
            Type = t;
            ChangeAction = changedAction;
            Items = items;
        }
    }
}