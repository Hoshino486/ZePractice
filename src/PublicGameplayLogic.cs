using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ZePractice;

public static class PublicGameplayLogic
{
    public static IReadOnlyDictionary<string, string> ServerPlayerProtectionRules { get; } = new Dictionary<string, string>
    {
        ["mp_autokick"] = "0", ["mp_suicide_penalty"] = "0"
    }.AsReadOnly();

    public static IReadOnlyDictionary<string, string> ServerMovementRules { get; } = new Dictionary<string, string>
    {
        ["sv_autobunnyhopping"] = "0", ["sv_enablebunnyhopping"] = "1",
        ["sv_airaccelerate"] = "12", ["sv_air_max_wishspeed"] = "30", ["sv_maxvelocity"] = "3500",
        ["sv_staminamax"] = "0", ["sv_staminajumpcost"] = "0", ["sv_staminalandcost"] = "0",
        ["sv_jump_spam_penalty_time"] = "0"
    }.AsReadOnly();

    private static readonly byte[] VolumeHeader = [0xe9, 0x54, 0x60, 0xbd, 0x08, 0x04, 0x00];

    public static bool TryInteger(string text, int minimum, int maximum, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= minimum && value <= maximum;

    public static bool TryVolume(string text, out float value) =>
        float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) &&
        float.IsFinite(value) && value is >= 0 and <= 1;

    public static int HumanSpawnArmor(int currentArmor, int freeArmor, bool fromZombie, int serverLimit) =>
        Math.Clamp(fromZombie ? (freeArmor > 0 ? 100 : 0) : currentArmor, 0, Math.Min(100, serverLimit));

    public static bool TryToggle(string text, bool current, out bool enabled)
    {
        enabled = current;
        switch (text.Trim().ToLowerInvariant())
        {
            case "": case "toggle": enabled = !current; return true;
            case "1": case "on": enabled = true; return true;
            case "0": case "off": enabled = false; return true;
            default: return false;
        }
    }

    public static bool TryWorkshopId(string text, out string identifier)
    {
        identifier = "";
        if (!Regex.IsMatch(text, "^[+]?[1-9][0-9]{0,19}$", RegexOptions.CultureInvariant)) return false;
        string digits = text.TrimStart('+');
        if (!ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out ulong numeric) || numeric == 0) return false;
        identifier = numeric.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public static bool IsWorkshopChat(string text) =>
        Regex.IsMatch(text.Trim(), "^[!/！]?host_workshop_map(?:\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryWorkshopChat(string text, out string identifier)
    {
        identifier = "";
        var match = Regex.Match(text.Trim(), "^[!/！]?host_workshop_map[ \\t]+([+]?[1-9][0-9]{0,19})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && TryWorkshopId(match.Groups[1].Value, out identifier);
    }

    public static bool HideTeammate(int viewerSlot, int targetSlot, int viewerTeam, int targetTeam, bool targetAlive, int range, float distanceSquared) =>
        viewerSlot != targetSlot && viewerTeam is 2 or 3 && viewerTeam == targetTeam && targetAlive &&
        range is > 0 and <= 999 && float.IsFinite(distanceSquared) && distanceSquared >= 0 && distanceSquared <= (float)range * range;

    public static Vector3 CapHorizontal(Vector3 velocity, float maximum)
    {
        float length = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        if (!float.IsFinite(length) || !float.IsFinite(maximum) || maximum <= 0) return new(0, 0, velocity.Z);
        return length > maximum ? new(velocity.X * maximum / length, velocity.Y * maximum / length, velocity.Z) : velocity;
    }

    public static bool IsMapSound(string designerName) => designerName is "ambient_generic" or "point_soundevent" ||
        designerName.StartsWith("snd_event_", StringComparison.Ordinal) || designerName.StartsWith("snd_soundscape", StringComparison.Ordinal);

    public static byte[] VolumeParameters(float volume)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(volume));
        byte[] packet = new byte[11];
        VolumeHeader.CopyTo(packet, 0);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7), volume);
        return packet;
    }

    public static bool TryReadSoundVolume(ReadOnlySpan<byte> parameters, out float volume)
    {
        volume = 1;
        int offset = parameters.IndexOf(VolumeHeader);
        if (offset < 0 || parameters.Length - offset < 11) return false;
        float candidate = BinaryPrimitives.ReadSingleLittleEndian(parameters.Slice(offset + 7, 4));
        if (!float.IsFinite(candidate) || candidate is < 0 or > 16) return false;
        volume = candidate;
        return true;
    }
}

public sealed class ZombieRegenerationState
{
    public float LastDamageAt { get; private set; }
    public float NextRegenerationAt { get; private set; }
    private int _health, _armor;

    public void Reset(int health, int armor, float now, float delay)
    { _health = health; _armor = armor; Damage(now, delay); }

    public void Damage(float now, float delay)
    { LastDamageAt = now; NextRegenerationAt = now + delay; }

    public (int Health, int Armor) Update(int team, int health, int armor, int maximumHealth, int maximumArmor,
        float now, float delay, int healthPerSecond, int armorPerSecond)
    {
        if (health < _health || armor < _armor) Damage(now, delay);
        if (team == 2 && health > 0 && now >= NextRegenerationAt && now - LastDamageAt >= delay)
        {
            health = (int)Math.Min(maximumHealth, (long)health + healthPerSecond);
            armor = (int)Math.Min(maximumArmor, (long)armor + armorPerSecond);
            NextRegenerationAt = now + 1;
        }
        _health = health; _armor = armor;
        return (health, armor);
    }
}

public sealed class BunnyHopSequence
{
    public int ConsecutiveJumps { get; private set; }
    public bool SlowCurrentJump { get; private set; }
    public float LastJumpAt { get; private set; } = float.NegativeInfinity;
    private float _groundedSince = -1;
    private bool _wasAirborne;

    public void Reset()
    { ConsecutiveJumps = 0; SlowCurrentJump = false; LastJumpAt = float.NegativeInfinity; _groundedSince = -1; _wasAirborne = false; }

    public bool Jump(float now)
    {
        if (now - LastJumpAt < 0.1f) return false;
        LastJumpAt = now;
        SlowCurrentJump = ++ConsecutiveJumps >= 5;
        if (SlowCurrentJump) ConsecutiveJumps = 0;
        _groundedSince = -1;
        return true;
    }

    public void ObserveGround(bool grounded, float now)
    {
        if (!grounded) { _wasAirborne = true; _groundedSince = -1; return; }
        if (_wasAirborne) { SlowCurrentJump = false; _wasAirborne = false; }
        if (_groundedSince < 0) _groundedSince = now;
        if (now - _groundedSince >= 0.25f) { ConsecutiveJumps = 0; SlowCurrentJump = false; }
    }
}

public sealed class DroppedGunClock
{
    public bool Protected { get; set; }
    public bool WasOwned { get; private set; }
    public float DroppedAt { get; private set; } = -1;
    public bool Update(bool owned, bool firearm, float now, float lifetime)
    {
        if (owned) { WasOwned = true; DroppedAt = -1; return false; }
        if (Protected || !firearm || !WasOwned) return false;
        if (DroppedAt < 0) DroppedAt = now;
        return now - DroppedAt >= lifetime;
    }
}

public sealed class ManualRoundGate
{
    public bool Started { get; private set; }
    public float AllowedUntil { get; private set; } = -1;
    public void MapStart() { Started = false; AllowedUntil = -1; }
    public void RoundStarted() { Started = true; AllowedUntil = -1; }
    public void Permit(float now, float duration) => AllowedUntil = now + duration;
    public bool Blocks(float now) => Started && now > AllowedUntil;
}
