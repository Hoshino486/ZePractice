using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CounterStrikeSharp.API.Modules.Utils
{
    public sealed class CEntityHandle(uint raw)
    {
        public uint Raw { get; } = raw;
        public uint Index => Raw & 0x7FFF;
    }
    public sealed class CHandle<T>(uint raw) where T : CEntityInstance
    {
        public T? Value => Utilities.Entities.GetValueOrDefault(raw) is T { IsValid: true } entity ? entity : null;
    }
}

namespace CounterStrikeSharp.API.Core
{
    public enum HookMode { Pre, Post }
    public enum HookResult { Continue, Stop }
    public sealed class GameEventInfo;
    public class CEntityInstance(uint raw, string designerName)
    {
        public bool IsValid { get; set; } = true;
        public string DesignerName { get; } = designerName;
        public CEntityHandle EntityHandle { get; } = new(raw);
        public uint Index => EntityHandle.Index;
    }
    public class CBaseEntity(uint raw, string designerName) : CEntityInstance(raw, designerName)
    {
        public int RemoveCalls { get; private set; }
        public bool FailNextRemove { get; set; }
        public void Remove()
        {
            if (FailNextRemove) { FailNextRemove = false; throw new InvalidOperationException("test removal failure"); }
            RemoveCalls++;
            IsValid = false;
            Utilities.Entities.Remove(EntityHandle.Raw);
        }
    }
    public sealed class CSmokeGrenadeProjectile(uint raw) : CBaseEntity(raw, "smokegrenade_projectile")
    {
        public int SmokeEffectTickBegin { get; set; }
        public bool DidSmokeEffect { get; set; }
    }
    public sealed class CCSPlayerPawn(uint raw) : CBaseEntity(raw, "player")
    {
        public float FlashDuration { get; set; }
        public float FlashMaxAlpha { get; set; }
        public float BlindUntilTime { get; set; }
        public float BlindStartTime { get; set; }
    }
    public sealed class CCSPlayerController(uint raw) : CEntityInstance(raw, "cs_player_controller")
    {
        public bool IsHLTV { get; set; }
        public bool IsBot { get; set; }
        public int TeamNum { get; set; }
        public CHandle<CCSPlayerPawn> PlayerPawn { get; set; } = new(0);
    }
    public sealed class EventPlayerBlind
    {
        public float BlindDuration { get; set; }
        public CCSPlayerController? Userid { get; set; }
    }
    public sealed class EventPlayerSpawn { public CCSPlayerController? Userid { get; set; } }
    public sealed class EventSmokegrenadeDetonate { public int Entityid { get; set; } }
    public sealed class CCheckTransmitInfo { public HashSet<int> TransmitEntities { get; } = new(); }
    public sealed class CCheckTransmitInfoList : List<(CCheckTransmitInfo Information, CCSPlayerController? Viewer)>;
    public static class Listeners
    {
        public delegate void OnServerPostEntityThink();
        public delegate void OnEntitySpawned(CEntityInstance entity);
        public delegate void OnEntityDeleted(CEntityInstance entity);
        public delegate void CheckTransmit(CCheckTransmitInfoList list);
    }
}

namespace CounterStrikeSharp.API
{
    public static class Utilities
    {
        public static readonly Dictionary<uint, CEntityInstance> Entities = new();
        public static readonly List<CCSPlayerController> Players = new();
        public static readonly List<(uint Handle, string Class, string Field)> Changes = new();
        public static List<CCSPlayerController> GetPlayers() => Players;
        public static IEnumerable<T> FindAllEntitiesByDesignerName<T>(string name) where T : CEntityInstance =>
            Entities.Values.OfType<T>().Where(entity => entity.IsValid && entity.DesignerName == name).ToArray();
        public static T? GetEntityFromIndex<T>(int index) where T : CEntityInstance =>
            Entities.Values.OfType<T>().FirstOrDefault(entity => entity.IsValid && entity.Index == index);
        public static void SetStateChanged(CEntityInstance entity, string className, string field) =>
            Changes.Add((entity.EntityHandle.Raw, className, field));
        public static T Add<T>(T entity) where T : CEntityInstance { Entities[entity.EntityHandle.Raw] = entity; return entity; }
        public static void Reset() { Entities.Clear(); Players.Clear(); Changes.Clear(); }
    }
    public static class Server
    {
        public static readonly Queue<Action> Updates = new();
        public static void NextWorldUpdate(Action action) => Updates.Enqueue(action);
        public static void Step()
        {
            int count = Updates.Count;
            for (int index = 0; index < count; index++) Updates.Dequeue()();
        }
    }
}

namespace Microsoft.Extensions.Logging
{
    public sealed class TestLogger
    {
        public List<Exception> Errors { get; } = new();
        public void LogError(Exception error, string message, params object[] arguments) => Errors.Add(error);
    }
}

namespace ZePractice
{
    public sealed class ZePracticePlugin
    {
        public Microsoft.Extensions.Logging.TestLogger Logger { get; } = new();
        public readonly Dictionary<Type, Delegate> Listeners = new();
        public readonly Dictionary<(Type Type, HookMode Mode), Delegate> Events = new();
        public void RegisterListener<T>(T listener) where T : Delegate => Listeners.Add(typeof(T), listener);
        public void RemoveListener<T>(T listener) where T : Delegate => Listeners.Remove(typeof(T));
        public void RegisterEventHandler<T>(Func<T, GameEventInfo, HookResult> handler, HookMode mode) => Events.Add((typeof(T), mode), handler);
        public void DeregisterEventHandler<T>(Func<T, GameEventInfo, HookResult> handler, HookMode mode) => Events.Remove((typeof(T), mode));
        public HookResult Fire<T>(T gameEvent, HookMode mode = HookMode.Pre) =>
            ((Func<T, GameEventInfo, HookResult>)Events[(typeof(T), mode)])(gameEvent, new());
        public void Think() => ((CounterStrikeSharp.API.Core.Listeners.OnServerPostEntityThink)Listeners[typeof(CounterStrikeSharp.API.Core.Listeners.OnServerPostEntityThink)])();
        public void Spawn(CEntityInstance entity) => ((CounterStrikeSharp.API.Core.Listeners.OnEntitySpawned)Listeners[typeof(CounterStrikeSharp.API.Core.Listeners.OnEntitySpawned)])(entity);
        public void Delete(CEntityInstance entity) => ((CounterStrikeSharp.API.Core.Listeners.OnEntityDeleted)Listeners[typeof(CounterStrikeSharp.API.Core.Listeners.OnEntityDeleted)])(entity);
        public void Transmit(CCheckTransmitInfoList list) => ((CounterStrikeSharp.API.Core.Listeners.CheckTransmit)Listeners[typeof(CounterStrikeSharp.API.Core.Listeners.CheckTransmit)])(list);
    }
}
