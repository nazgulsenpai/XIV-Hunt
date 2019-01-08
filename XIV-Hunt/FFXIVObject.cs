using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using XIVDB;

namespace FFXIV_GameSense
{
    public enum EObjType : ushort
    {
        Unknown,
        //Exit = 1629,
        //Entrance = 2632,
        CairnOfPassage = 11292,
        CairnOfReturn = 11297,
        BeaconOfReturn = 18648,
        BeaconOfPassage = 18660,
        Hoard = 12353,
        Banded = 12347,
        Silver = 5479,
        Gold = 11500,
        BronzeTrap = 5478
    }

    public class Entity
    {
        public uint ID { get; set; }
        public uint OwnerID { get; set; }
        public int Order { get; set; }
        public uint TargetID { get; set; }
        public string Name { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float Heading { get; set; }
        public float HeadingDegree => (float)((Heading + Math.PI) * (180.0 / Math.PI));
        public byte EffectiveDistance { get; set; }

        public float GetDistanceTo(Entity target)
        {
            var distanceX = Math.Abs(PosX - target.PosX);
            var distanceY = Math.Abs(PosY - target.PosY);
            var distanceZ = Math.Abs(PosZ - target.PosZ);
            return (float)Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY) + (distanceZ * distanceZ));
        }

        public float GetDistanceTo(float x, float y, float z)
        {
            if (z.Equals(0.0f))
                return GetHorizontalDistanceTo(x, y);
            var distanceX = Math.Abs(PosX - x);
            var distanceY = Math.Abs(PosY - y);
            var distanceZ = Math.Abs(PosZ - z);
            return (float)Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY) + (distanceZ * distanceZ));
        }

        private float GetHorizontalDistanceTo(float x, float y)
        {
            var distanceX = Math.Abs(PosX - x);
            var distanceY = Math.Abs(PosY - y);
            return (float)Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
        }

        public float GetHorizontalDistanceTo(Entity target)
        {
            var distanceX = Math.Abs(PosX - target.PosX);
            var distanceY = Math.Abs(PosY - target.PosY);
            return (float)Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
        }

        public string GetPosReadable(ushort zoneId)
        {
            if (GameResources.GetSizeFactor(zoneId) == 0)
                return "Unknown size_factor: " + GameResources.GetSizeFactor(zoneId).ToString();

            return "( " + GetXReadable(zoneId).ToString("0.0").Replace(',', '.') + " , " + GetYReadable(zoneId).ToString("0.0").Replace(',', '.') + " )";
        }

        internal float GetXReadable(ushort zoneId, ushort mapId = 0) => GetXReadable(PosX, zoneId, mapId);

        internal static float GetXReadable(float PosX, ushort zoneId, ushort mapId = 0) => GetCoordReadable(PosX, zoneId, mapId);

        internal float GetYReadable(ushort zoneId, ushort mapId = 0) => GetYReadable(PosY, zoneId, mapId);

        internal static float GetYReadable(float PosY, ushort zoneId, ushort mapId = 0) => GetCoordReadable(PosY, zoneId, mapId);

        private static float GetCoordReadable(float c, ushort zoneId, ushort mapId = 0)
        {
            c *= 0.02f;
            switch (GameResources.GetSizeFactor(zoneId, mapId))
            {
                case 95:
                    return c + 22.5f;
                case 100:
                    return c + 21.5f;
                case 300:
                    return c + 7.5f;
                case 400:
                    return c + 6;
                case 200:
                case 0:
                    return c + 11.25f;
                case 800:
                    return c + 3.5f;
                default:
                    return 0;
            }
        }

        internal static float GetCoordFromReadable(float r, ushort zoneId, ushort mapId = 0)
        {
            switch (GameResources.GetSizeFactor(zoneId, mapId))
            {
                case 95:
                    r -= 22.5f;
                    break;
                case 100:
                    r -= 21.5f;
                    break;
                case 300:
                    r -= 7.5f;
                    break;
                case 400:
                    r -= 6;
                    break;
                case 200:
                case 0:
                    r -= 11.25f;
                    break;
                case 800:
                    r -= 3.5f;
                    break;
                default:
                    r = 0;
                    break;
            }
            return r /= 0.02f;
        }
    }

    public abstract class Combatant : Entity
    {
        public JobEnum Job { get; set; }
        public string JobName => Enum.GetName(typeof(JobEnum), Job);
        public byte Level { get; set; }
        public uint CurrentHP { get; set; }
        public uint MaxHP { get; set; }
        public uint CurrentMP { get; set; }
        public uint MaxMP { get; set; }
        public ushort MaxTP { get; set; }
        public ushort CurrentTP { get; set; }
        public ushort MaxGP { get; set; }
        public ushort CurrentGP { get; set; }
        public ushort MaxCP { get; set; }
        public ushort CurrentCP { get; set; }
        public List<Status> StatusList = new List<Status>();

        public bool IsBattleClass() => !(Job >= JobEnum.CRP && Job <= JobEnum.FSH);

        public bool IsGatherer() => IsGatherer(Job);

        public static bool IsGatherer(JobEnum j) => j >= JobEnum.MIN && j <= JobEnum.FSH;

        public bool IsCrafter() => IsCrafter(Job);

        public static bool IsCrafter(JobEnum j) => j >= JobEnum.CRP && j <= JobEnum.CUL;
    }

    public class PC : Combatant { }

    public class Monster : Combatant
    {
        public ushort BNpcNameID { get; set; }
        public uint FateID { get; set; }
    }

    public class EObject : Entity
    {
        public EObjType SubType { get; set; }
        public bool CairnIsUnlocked = false;
    }

    public class Treasure : Entity  { }

    public class NPC : Entity  { }

    public class Aetheryte : Entity  { }

    public class GatheringPoint : Entity { }
    
    public class Mount : Entity { }

    public class Minion : Entity { }

    public class Retainer : Entity { }

    public class Housing : Entity { }

    public class Area : Entity { }

    public class Cutscene : Entity { }

    public class CardStand : Entity { }

    public class FATE
    {
        [JsonProperty("id")]
        public ushort ID { get; set; }
        [JsonIgnore]
        public string ReadName { get; set; }
        public string Name(bool stripTags = false) => stripTags ? FATEInfo.Name : FATEInfo.NameWithTags;
        public byte Progress { get; set; }
        [JsonProperty("x")]
        public float PosX { get; set; }
        [JsonIgnore]
        public float PosZ { get; set; }
        [JsonProperty("y")]
        public float PosY { get; set; }
        public ushort ZoneID { get; set; }
        public FATEState State { get; set; }
        public uint StartTimeEpoch { get; set; }
        public ushort Duration { get; set; }
        public DateTime LastReported { get; set; }
        [JsonIgnore]
        public DateTime EndTime => DateTimeOffset.FromUnixTimeSeconds(StartTimeEpoch + Duration).UtcDateTime;
        [JsonIgnore]
        public TimeSpan TimeRemaining => EndTime - Program.mem.GetServerUtcTime();//TODO: fix
        [JsonIgnore]
        public bool HasEnded => State == FATEState.Ended || State == FATEState.Failed;
        [JsonIgnore]
        public FATEInfo FATEInfo => GameResources.GetFATEInfo(ID);

        public FATE() { }

        public FATE(FATE f)//copy constructor
        {
            ID = f.ID;
            ReadName = f.ReadName;
            Progress = f.Progress;
            PosX = f.PosX;
            PosZ = f.PosZ;
            PosY = f.PosY;
            ZoneID = f.ZoneID;
            State = f.State;
            StartTimeEpoch = f.StartTimeEpoch;
            Duration = f.Duration;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is FATE f) || obj == null)
                return false;
            return (ID == f.ID);
        }

        public override int GetHashCode() => ID.GetHashCode();

        public bool IsDataCenterShared() => (ID > 962 && ID < 1101) || FATEInfo?.EurekaFate == true;
    }

    public enum FATEState : byte
    {
        Running = 0x02,
        Ended = 0x04,
        Failed = 0x05,
        Preparation = 0x07,
        WaitingForEnd = 0x08
    }

    ///
    /// Job enum
    ///
    public enum JobEnum : byte
    {
        ADV,
        GLA,
        PGL,
        MRD,
        LNC,
        ARC,
        CNJ,
        THM,
        CRP,
        BSM,
        ARM,
        GSM,
        LTW,
        WVR,
        ALC,
        CUL,
        MIN,
        BTN,
        FSH,
        PLD,
        MNK,
        WAR,
        DRG,
        BRD,
        WHM,
        BLM,
        ACN,
        SMN,
        SCH,
        ROG,
        NIN,
        MCH,
        DRK,
        AST,
        SAM,
        RDM,
        BLU
    }

    public class Status
    {
        public short ID { get; set; }
        public short Value { get; set; }
        public float Timer { get; set; }
        public uint CasterId { get; set; }

        public bool Equals(Status p)
        {
            return ID.Equals(p.ID);
        }

    }

    class ContentFinder
    {
        public ContentFinderState State { get; set; }
        public byte RouletteID { get; set; }
        public ushort ContentFinderConditionID { get; set; }
        public string InstanceContentName => GameResources.GetContentFinderName(ContentFinderConditionID);

        public bool IsDutyRouletteQueued()
        {
            return (RouletteID > 0 && RouletteID < 10) || RouletteID == 15 || RouletteID == 17; //10-14 is The Feast and 2 empty entries
        }
    }

    enum ContentFinderState : byte
    {
        NotQueued = 0,
        Queued = 1,
        Popped = 2,
        Entering = 3,
        In = 4
    }
}
