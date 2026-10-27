using System.Drawing;
using System.Numerics;

namespace CounterStrikeSharp.API.Core
{
    public class BasePluginConfig { public virtual int Version { get; set; } }
    public enum HookResult { Continue }
    public enum HookMode { Post }
    public enum gear_slot_t { GEAR_SLOT_RIFLE, GEAR_SLOT_PISTOL, GEAR_SLOT_KNIFE }
    public enum SolidType_t { SOLID_NONE }
    public enum MoveType_t { MOVETYPE_NONE }
    [Flags] public enum TakeDamageFlags_t { DFLAG_SUPPRESS_HEALTH_CHANGES = 1, DFLAG_SUPPRESS_PHYSICS_FORCE = 2 }
    public sealed class Handle { public uint Raw; public bool IsValid; }
    public sealed class Identity { public string Name = ""; }
    public sealed class Position { public float X, Y, Z; }
    public class CEntityInstance
    {
        public bool IsValid = true;
        public Handle EntityHandle = new();
        public string DesignerName = "";
        public Identity? Entity = new();
        public T As<T>() where T : CEntityInstance => (T)this;
        public void Remove() => IsValid = false;
    }
    public class CBaseEntity : CEntityInstance
    {
        public Position? AbsOrigin = new(), AbsRotation = new();
        public MoveType_t MoveType, ActualMoveType;
        public TakeDamageFlags_t TakeDamageFlags;
        public void Teleport(Vector3 origin, Vector3 angles) { AbsOrigin = new() { X = origin.X, Y = origin.Y, Z = origin.Z }; AbsRotation = new() { X = angles.X, Y = angles.Y, Z = angles.Z }; }
        public void AcceptInput(string input) { }
    }
    public sealed class Collision
    {
        public Position Mins = new(), Maxs = new();
        public SolidType_t SolidType; public int SolidFlags, CollisionGroup;
    }
    public sealed class CGlowProperty
    {
        public int GlowType, GlowTeam, GlowRange, GlowRangeMin;
        public Color GlowColorOverride; public Position GlowColor = new();
        public bool Flashing, EligibleForScreenHighlight, Glowing;
        public float GlowTime, GlowStartTime;
    }
    public class CBaseModelEntity : CBaseEntity
    {
        public CGlowProperty Glow = new(); public Collision Collision = new(); public Color Render;
    }
    public sealed class CDynamicProp : CBaseModelEntity
    {
        public void SetModel(string model) { }
        public void DispatchSpawn() { }
    }
    public sealed class WeaponData { public gear_slot_t GearSlot = gear_slot_t.GEAR_SLOT_PISTOL; }
    public sealed class CCSWeaponBase : CBaseModelEntity
    {
        public WeaponData? VData = new(); public string UniqueHammerID = ""; public Handle OwnerEntity = new();
    }
    public sealed class CCSPlayerController : CBaseEntity;
    public static class Listeners
    {
        public delegate void OnServerPrecacheResources(CounterStrikeSharp.API.Modules.Entities.ResourceManifest manifest);
        public delegate void OnEntitySpawned(CEntityInstance entity);
        public delegate void OnTick();
    }
}
namespace CounterStrikeSharp.API.Modules.Commands { public sealed class CommandInfo; }
namespace CounterStrikeSharp.API.Modules.Timers { public enum TimerFlags { STOP_ON_MAPCHANGE } }
namespace CounterStrikeSharp.API.Modules.Entities
{
    public sealed class ResourceManifest { public void AddResource(string path) { } }
    public static class EntityIO
    {
        public delegate Core.HookResult EntityOutputHandler(object output, string name, Core.CEntityInstance activator, Core.CEntityInstance caller, object value, float delay);
    }
}
namespace CounterStrikeSharp.API.Modules.Utils { }
namespace CounterStrikeSharp.API.Modules.Memory
{
    public static class Schema
    {
        public static readonly Dictionary<string, int> GlowOffsets = new()
        {
            ["m_fGlowColor"] = 8, ["m_iGlowType"] = 48, ["m_iGlowTeam"] = 52,
            ["m_nGlowRange"] = 56, ["m_nGlowRangeMin"] = 60, ["m_glowColorOverride"] = 64,
            ["m_bFlashing"] = 68, ["m_flGlowTime"] = 72, ["m_flGlowStartTime"] = 76,
            ["m_bEligibleForScreenHighlight"] = 80, ["m_bGlowing"] = 81
        };
        public static bool IsSchemaFieldNetworked(string schema, string property) => schema == "CGlowProperty" && GlowOffsets.ContainsKey(property) && property is not ("m_fGlowColor" or "m_bGlowing");
        public static short GetSchemaOffset(string schema, string property) => schema == "CGlowProperty" ? checked((short)GlowOffsets[property]) : throw new Exception("Unexpected schema");
    }
}
namespace CounterStrikeSharp.API
{
    public static class Server { public static float CurrentTime; public static void NextFrame(Action callback) => callback(); }
    public static class Utilities
    {
        public static readonly List<Core.CEntityInstance> Entities = new();
        public static int NetworkWrites;
        public static readonly List<(uint Entity, int Offset)> GlowWrites = new();
        public static IEnumerable<T> FindAllEntitiesByDesignerName<T>(string name) where T : Core.CEntityInstance => Entities.OfType<T>().Where(entity => entity.IsValid && (entity.DesignerName == name || name == "weapon_" && entity.DesignerName.StartsWith(name)));
        public static T? GetEntityFromIndex<T>(int index) where T : Core.CEntityInstance => Entities.OfType<T>().FirstOrDefault(entity => (entity.EntityHandle.Raw & 0x7FFF) == index && entity.IsValid);
        public static T CreateEntityByName<T>(string name) where T : Core.CEntityInstance, new() { var entity = new T { DesignerName = name, EntityHandle = new() { Raw = (uint)(Entities.Count + 1000) } }; Entities.Add(entity); return entity; }
        public static void SetStateChanged(Core.CBaseModelEntity entity, string schema, string property, int extraOffset = 0)
        {
            NetworkWrites++;
            if (property != "m_Glow") return;
            if (schema != "CBaseModelEntity" || !new[] { 48, 52, 56, 60, 64, 68, 72, 76, 80 }.Contains(extraOffset))
                throw new Exception("Glow replication requires a networked member offset, not the struct or server-only flags.");
            GlowWrites.Add((entity.EntityHandle.Raw, extraOffset));
        }
    }
}
namespace Microsoft.Extensions.Logging { public static class Extensions { public static void LogError(this object logger, Exception error, string message, string area) => throw new Exception(area, error); } }
namespace ZePractice
{
    using CounterStrikeSharp.API.Core;
    using CounterStrikeSharp.API.Modules.Commands;
    using CounterStrikeSharp.API.Modules.Entities;
    using CounterStrikeSharp.API.Modules.Timers;
    public sealed class ZePracticePlugin
    {
        public ZePracticeConfig Config = new(); public object Logger = new();
        public readonly Dictionary<Type, Delegate> Listeners = new();
        public void RegisterListener<T>(T callback) where T : Delegate => Listeners.Add(typeof(T), callback);
        public void RemoveListener<T>(T callback) where T : Delegate => Listeners.Remove(typeof(T));
        public void HookEntityOutput(string name, string output, EntityIO.EntityOutputHandler callback, HookMode mode) { }
        public void UnhookEntityOutput(string name, string output, EntityIO.EntityOutputHandler callback, HookMode mode) { }
        public void AddZeCommand(string name, string help, Action<CCSPlayerController?, CommandInfo> callback) { }
        public void RemoveZeCommand(string name, Action<CCSPlayerController?, CommandInfo> callback) { }
        public void AddTimer(float seconds, Action callback, TimerFlags flags) => callback();
        public void Reply(CCSPlayerController? player, string message) { }
        public void Advance(float seconds) { CounterStrikeSharp.API.Server.CurrentTime += seconds; ((Listeners.OnTick)Listeners[typeof(Listeners.OnTick)])(); }
        public void Spawn(CEntityInstance entity) => ((Listeners.OnEntitySpawned)Listeners[typeof(Listeners.OnEntitySpawned)])(entity);
    }
}
