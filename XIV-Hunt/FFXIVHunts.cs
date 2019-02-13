﻿using Newtonsoft.Json;
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
        internal List<Hunt> hunts = new List<Hunt>();
        private static List<FATEReport> fates = GameResources.GetFates().Select(x => new FATEReport(x)).ToList();
        private static List<uint> HuntsPutInChat = new List<uint>();
        private static readonly uint[] DCZones = new uint[] { 630, 656, 732, 763, 795, 827 };
        private static HuntsHubConnection hubConnection;
        internal static HttpClient Http { get; private set; } = new HttpClient();
        internal static bool Joined { get; private set; }
        private static bool joining;
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
                hunts.Add(new Hunt(kvp.Key));

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
            hubConnection.Connection.On<Hunt>("ReceiveHunt", hunt =>
            {
                LogHost.Default.Debug(string.Format("[{0}] Report received: {1}", GameResources.GetWorldName(hunt.WorldId), hunt.Name));
                if (PutInChat(hunt) && Settings.Default.FlashTaskbarIconOnHuntAndFATEs)
                    if (hunt.LastAlive)
                        NativeMethods.FlashTaskbarIcon(Program.mem.Process, 45, true);
                    else
                        NativeMethods.StopFlashWindowEx(Program.mem.Process);
            });
            hubConnection.Connection.On<FATEReport>("ReceiveFATE", fate =>
            {
                //LogHost.Default.Debug(string.Format("[{0}] Report received: {1} - {2}%", GameResources.GetWorldName(fate.WorldId), fate.Name(true), fate.Progress));
                if (PutInChat(fate) && Settings.Default.FlashTaskbarIconOnHuntAndFATEs)
                    NativeMethods.FlashTaskbarIcon(Program.mem.Process);
            });
            hubConnection.Connection.On<DataCenterInstanceMatchInfo>("DCInstanceMatch", instance =>
            {
                string s = string.Format(Resources.DCInstanceMatch, Program.AssemblyName.Name, (ServerTimeUtc - instance.StartTime).TotalMinutes.ToString("F0"), $"{baseUrl}DCInstance/{instance.ID}");
                LogHost.Default.Info("DCInstanceMatch: " + s);
                DCInstance = instance;
                ChatMessage cm = new ChatMessage { MessageString = s };
                _ = Program.mem.WriteChatMessage(cm);
            });
            hubConnection.Connection.On<int>("ConnectedCount", connectedCount =>
            {
                w1.HuntConnectionTextBlock.Dispatcher.Invoke(() => w1.HuntConnectionTextBlock.Text = string.Format(Resources.FormConnectedToCount, GameResources.GetWorldName(Program.mem.GetWorldId()), connectedCount - 1));
            });
            hubConnection.Connection.Closed += Connection_Closed;
        }

        private Task Connection_Closed(Exception arg)
        {
            Joined = joining = false;
            return Task.CompletedTask;
        }

        internal HuntRank HuntRankFor(ushort HuntID)
        {
            if (hunts.Any(x => x.Id == HuntID))
                return hunts.Single(x => x.Id == HuntID).Rank;
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
            int idx = fates.IndexOf(fate);
            if (idx == -1)
                return false;
            fates[idx].State = fate.State;
            fates[idx].StartTimeEpoch = fate.StartTimeEpoch;
            fates[idx].Duration = fate.Duration;
            fates[idx].Progress = fate.Progress;
            bool skipAnnounce = Settings.Default.NoAnnouncementsInContent && Program.mem.GetCurrentContentFinderCondition() > 0
                || (Math.Abs(fate.TimeRemaining.TotalHours) < 3 && fate.TimeRemaining.TotalMinutes < Settings.Default.FATEMinimumMinutesRemaining)
                || ((fate.State == FATEState.Preparation) ? fates[idx].lastPutInChat > Program.mem.GetServerUtcTime().AddMinutes(-10) : Math.Abs(fate.Progress - fates[idx].LastReportedProgress) < Settings.Default.FATEMinimumPercentInterval && Settings.Default.FATEMinimumPercentInterval > 0);
            if (FateNotifyCheck(fates[idx].ID) && fates[idx].lastPutInChat < Program.mem.GetServerUtcTime().AddMinutes(-Settings.Default.FATEInterval) && !fate.HasEnded && !skipAnnounce)
            {
                ChatMessage cm = new ChatMessage();
                string postpend;
                if (fate.State == FATEState.Preparation)
                    postpend = Resources.PreparationState;
                else if (Math.Abs(fate.TimeRemaining.TotalHours) > 3)//StartTimeEpoch isn't set during the first few seconds
                    postpend = fate.Progress + "%";
                else
                    postpend = string.Format(Resources.FATEPrcTimeRemaining, fate.Progress, (int)fate.TimeRemaining.TotalMinutes, fate.TimeRemaining.Seconds.ToString("D2"));
                cm = ChatMessage.MakePosChatMessage(string.Format(Resources.FATEMsg, fates[idx].Name()), fate.ZoneID, fate.PosX, fate.PosY, " " + postpend);
                _ = Program.mem.WriteChatMessage(cm);
                CheckAndPlaySound(HuntRank.FATE);
                fates[idx].lastPutInChat = Program.mem.GetServerUtcTime();
                fates[idx].LastReportedProgress = fate.Progress;
                //if (fate.Progress > 99)
                //    fates[idx].lastReportedDead = DateTime.UtcNow;
                return true;
            }
            return false;
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
            if (lastJoined != mem.GetWorldId() && Joined && !joining || !Joined)
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
            foreach (Monster c in mem.Combatants.OfType<Monster>().Where(c => hunts.Exists(h => h.Id == c.BNpcNameID && GetZoneId(c.BNpcNameID) == thisZone)))
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
                    await hubConnection.Connection.InvokeAsync(nameof(LeaveDCZone));
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
            int idx = fates.FindIndex(h => h.ID == f.ID);
            if (idx < 0 || (fates[idx].LastReported > ServerTimeUtc.AddSeconds(-5) && !(fates[idx].Progress != 100 && f.Progress > 99)))
                return;

            fates[idx].LastReported = ServerTimeUtc;
            //too pussy to use copy constructor
            fates[idx].Progress = f.Progress;
            fates[idx].PosX = f.PosX;
            fates[idx].PosZ = f.PosZ;
            fates[idx].PosY = f.PosY;
            fates[idx].ZoneID = f.ZoneID;
            fates[idx].Duration = f.Duration;
            fates[idx].StartTimeEpoch = f.StartTimeEpoch;
            fates[idx].State = f.State;
            fates[idx].ZoneID = f.ZoneID;

            try
            {
                if (hubConnection.Connected && Joined)
                    await hubConnection.Connection.InvokeAsync(nameof(ReportFate), fates[idx]);
            }
            catch (Exception e) { LogHost.Default.WarnException(nameof(ReportFate), e); }
        }

        private static bool FateNotifyCheck(ushort id)
        {
            //Get first ID for the FATE with this name
            id = GameResources.GetFateId(GameResources.GetFATEInfo(id)?.Name);
            return Settings.Default.FATEs.Contains(id.ToString());
        }

        private async Task JoinServerGroup()
        {
            if (Joined && hubConnection.Connected || joining)
                return;
            joining = true;
            w1.HuntConnectionTextBlock.Dispatcher.Invoke(() => w1.HuntConnectionTextBlock.Text = Resources.FormReadingSID);
            ushort sid = Program.mem.GetWorldId();
            Reporter r = new Reporter { WorldID = sid, Name = Program.mem.GetSelfCombatant().Name, Version = Program.AssemblyName.Version };
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
            else if(result == JoinGroupResult.Locked)
                w1.HuntConnectionTextBlock.Dispatcher.Invoke(() => w1.HuntConnectionTextBlock.Text = string.Format(Resources.FormJoinLocked, Program.AssemblyName.Name));
            joining = false;
            Joined = true;
            lastJoined = sid;
            foreach (Hunt h in hunts)
                h.WorldId = sid;
            foreach (FATEReport f in fates)
                f.WorldId = sid;
            ushort zid = Program.mem.GetZoneId();
            if (Array.IndexOf(DCZones, zid) > -1)
                await JoinDCZone(zid);
        }

        private bool PutInChat(Hunt hunt)
        {
            if (Settings.Default.NoAnnouncementsInContent && Program.mem.GetCurrentContentFinderCondition() > 0)
                return false;
            if ((hunt.IsARR() && hunt.Rank == HuntRank.B && Settings.Default.BARR && Settings.Default.notifyB)
                || (hunt.IsARR() && hunt.Rank == HuntRank.A && Settings.Default.AARR && Settings.Default.notifyA)
                || (hunt.IsARR() && hunt.Rank == HuntRank.S && Settings.Default.SARR && Settings.Default.notifyS)
                || (hunt.IsHW() && hunt.Rank == HuntRank.B && Settings.Default.BHW && Settings.Default.notifyB)
                || (hunt.IsHW() && hunt.Rank == HuntRank.A && Settings.Default.AHW && Settings.Default.notifyA)
                || (hunt.IsHW() && hunt.Rank == HuntRank.S && Settings.Default.SHW && Settings.Default.notifyS)
                || (hunt.IsSB() && hunt.Rank == HuntRank.B && Settings.Default.BSB && Settings.Default.notifyB)
                || (hunt.IsSB() && hunt.Rank == HuntRank.A && Settings.Default.ASB && Settings.Default.notifyA)
                || (hunt.IsSB() && hunt.Rank == HuntRank.S && Settings.Default.SSB && Settings.Default.notifyS)
                )
            {
                int idx = hunts.IndexOf(hunt);
                ChatMessage cm = new ChatMessage();
                if (Settings.Default.OncePerHunt ? !HuntsPutInChat.Contains(hunt.OccurrenceID) : hunts[idx].lastPutInChat < Program.mem.GetServerUtcTime().AddMinutes(-Settings.Default.HuntInterval) && hunt.LastAlive /*&& hunts[idx].lastReportedDead < Program.mem.GetServerUtcTime().AddSeconds(-15)*/)
                {
                    cm = ChatMessage.MakePosChatMessage(string.Format(Resources.HuntMsg, hunt.Rank.ToString(), hunt.Name), GetZoneId(hunt.Id), hunt.LastX, hunt.LastY);
                    if (cm != null)
                    {
                        _ = Program.mem.WriteChatMessage(cm);
                        CheckAndPlaySound(hunt.Rank);
                        hunts[idx] = hunt;
                        HuntsPutInChat.Add(hunt.OccurrenceID);
                        hunts[idx].lastPutInChat = Program.mem.GetServerUtcTime();
                        return true;
                    }
                }
                else if (hunts[idx].lastReportedDead < ServerTimeUtc.AddSeconds(-12) && !hunt.LastAlive)
                {
                    cm.MessageString = string.Format(Resources.HuntMsgKilled, hunt.Rank.ToString(), hunt.Name);
                    if (cm != null)
                    {
                        _ = Program.mem.WriteChatMessage(cm);
                        hunts[idx] = hunt;
                        hunts[idx].lastReportedDead = Program.mem.GetServerUtcTime();
                        return true;
                    }
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

        private async Task ReportHunt(Monster c)
        {
            int idx = hunts.FindIndex(h => h.Id == c.BNpcNameID);
            if (hunts[idx].LastReported > ServerTimeUtc.AddSeconds(-5) && c.CurrentHP > 0)
                return;//no need to report this often
            //else if (!hunts[idx].LastAlive && hunts[idx].LastReported > DateTime.UtcNow.AddSeconds(-5))
            //    return;

            hunts[idx].LastReported = ServerTimeUtc;
            hunts[idx].LastX = c.PosX;
            hunts[idx].LastY = c.PosY;
            hunts[idx].OccurrenceID = c.ID;
            hunts[idx].LastAlive = (c.CurrentHP > 0) ? true : false;

            try
            {
                if (Joined)
                    await hubConnection.Connection.InvokeAsync(nameof(ReportHunt), hunts[idx]);
            }
            catch (Exception e) { LogHost.Default.WarnException(nameof(ReportHunt), e); }
        }

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

        internal bool IsARR()
        {
            return Id < 3000;
        }

        internal bool IsHW()
        {
            return Id > 3000 && Id < 5000;
        }

        internal bool IsSB()
        {
            return Id > 5000 && Id < 7000;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Hunt item))
            {
                return false;
            }
            return Id.Equals(item.Id) && WorldId.Equals(item.WorldId);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        internal static bool TryGetHuntRank(ushort HuntID, out HuntRank hr) => (RankMap.TryGetValue(HuntID, out hr)) ? true : false;
    }

    class Reporter
    {
        public ushort WorldID { get; set; }
        public string Name { get; set; }
        public Version Version { get; set; }
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
}