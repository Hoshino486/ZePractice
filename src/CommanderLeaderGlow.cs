using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

/// <summary>Player pawn glow is not a reliable player outline. Follow the same two-prop
/// skeleton relay used by CS2Fixes ZEPlayer::StartGlow, with this server's unlimited range.</summary>
public sealed class CommanderLeaderGlow
{
    private CDynamicProp? _relay, _outline;
    private uint _pawn;
    private string _model = "";
    public CDynamicProp? Relay => _relay is { IsValid: true } ? _relay : null;
    public CDynamicProp? Outline => _outline is { IsValid: true } ? _outline : null;
    public static string Model(CBaseModelEntity e) => e.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName ?? "";
    private static uint Parent(CBaseEntity e) => e.CBodyComponent?.SceneNode?.PParent?.Owner?.EntityHandle.Raw ?? uint.MaxValue;
    public bool Linked(CCSPlayerPawn pawn) => Relay != null && Outline != null &&
        Parent(_relay!) == pawn.EntityHandle.Raw && Parent(_outline!) == _relay!.EntityHandle.Raw;

    public void Update(CCSPlayerPawn pawn, Color color)
    {
        string model = Model(pawn);
        if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("Leader model is not ready");
        if (_pawn != pawn.EntityHandle.Raw || _model != model || !Linked(pawn))
        {
            Clear(true);
            try
            {
                _relay = Spawn(model, "ze_commander_leader_relay", color, false);
                _outline = Spawn(model, "ze_commander_leader_outline", color, true);
                _relay.AcceptInput("FollowEntity", pawn, pawn, "!activator");
                _outline.AcceptInput("FollowEntity", _relay, _relay, "!activator");
                _pawn = pawn.EntityHandle.Raw; _model = model;
            }
            catch { Clear(true); throw; }
        }
        if (!CommanderVisualStyle.Matches(_outline!, color)) CommanderVisualStyle.Apply(_outline!, color);
    }

    private static CDynamicProp Spawn(string model, string name, Color color, bool outline)
    {
        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic") ?? throw new InvalidOperationException("Leader glow allocation failed");
        try
        {
            using var kv = new CEntityKeyValues();
            kv.SetString("model", model); kv.SetString("targetname", name);
            kv.SetInt("spawnflags", 256); kv.SetInt("solid", 0);
            kv.SetInt("renderamt", outline ? 1 : 0);
            if (!outline) kv.SetInt("rendermode", 10); // kRenderNone; only the relay is invisible.
            else
            {
                kv.SetInt("glowstate", 3); kv.SetInt("glowrange", CommanderVisualStyle.UnlimitedRange);
                kv.SetInt("glowrangemin", 0); kv.SetInt("glowteam", -1); kv.SetColor("glowcolor", color);
            }
            kv.SetFloat("fademindist", -1); kv.SetFloat("fademaxdist", 0); kv.SetInt("object_culling", 3);
            prop.DispatchSpawn(kv);
            prop.Collision.SolidType = SolidType_t.SOLID_NONE; prop.Collision.SolidFlags = 4; prop.Collision.CollisionGroup = 1;
            prop.AcceptInput("DisableCollision");
            if (outline) { prop.AcceptInput("StartGlowing"); CommanderVisualStyle.Apply(prop, color); }
            return prop;
        }
        catch { if (prop.IsValid) prop.Remove(); throw; }
    }
    public void Transmit(CCheckTransmitInfo info)
    {
        if (Relay is { } relay) info.TransmitEntities.Add(relay);
        if (Outline is { } outline) info.TransmitEntities.Add(outline);
    }
    public void Clear(bool remove)
    {
        if (remove)
        {
            if (_outline is { IsValid: true }) _outline.Remove();
            if (_relay is { IsValid: true }) _relay.Remove();
        }
        _outline = _relay = null; _pawn = 0; _model = "";
    }
    public object Diagnostics() => new { relay = Relay?.Index, outline = Outline?.Index, pawn = _pawn, model = _model };
}
