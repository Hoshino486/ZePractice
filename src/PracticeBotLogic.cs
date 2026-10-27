using System.Numerics;

namespace ZePractice;

public readonly record struct PracticeIdentity(int Slot, uint Controller, int? UserId, long Connection = 0);
public sealed record PracticePlacement(Vector3 Position, Vector3 Angles, int Team, int Health, int Armor);

/// <summary>Connections, not names/slots alone, own requests. Pending bots consume the same quota as live ones.</summary>
public sealed class PracticeBotRoster
{
    public sealed class Entry(long id, string name, PracticeIdentity owner, PracticePlacement placement, double now)
    {
        public long Id { get; } = id;
        public string Name { get; } = name;
        public PracticeIdentity Owner { get; } = owner;
        public PracticePlacement Placement { get; set; } = placement;
        public double RequestedAt { get; } = now;
        public PracticeIdentity? Bot { get; set; }
        public bool Retired { get; set; }
        public double RetiredAt { get; set; }
    }
    private readonly Dictionary<long, Entry> _entries = new();
    private readonly string _prefix = "ZEPB_" + Guid.NewGuid().ToString("N")[..8] + "_";
    public IEnumerable<Entry> Entries => _entries.Values.Where(e => !e.Retired);
    public static int Opponent(int team) => team == 3 ? 2 : team == 2 ? 3 : 0;
    public static bool TryResolveTeam(int ownerTeam, string? choice, out int team)
    {
        team = 0;
        if (ownerTeam is not (2 or 3)) return false;
        team = (choice ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "enemy" => Opponent(ownerTeam),
            "ally" => ownerTeam,
            "t" => 2,
            "ct" => 3,
            _ => 0
        };
        return team != 0;
    }
    public IEnumerable<Entry> Owned(PracticeIdentity owner) => Entries.Where(e => e.Owner == owner);
    public Entry? Reserve(PracticeIdentity owner, PracticePlacement placement, bool serverOwner, int limit, double now)
    {
        if (placement.Team is not (2 or 3) || placement.Health < 1 || !double.IsFinite(now) ||
            !Finite(placement.Position) || !Finite(placement.Angles) || (!serverOwner && Owned(owner).Count() >= limit)) return null;
        // Reuse the lowest free number so deleting/killing a bot frees its number immediately.
        long id = 1;
        while (_entries.ContainsKey(id)) id++;
        return _entries[id] = new(id, _prefix + id, owner, placement, now);
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    public Entry? ByName(string name) => _entries.Values.FirstOrDefault(e => e.Name == name);
    public Entry? ByBot(PracticeIdentity bot) => _entries.Values.FirstOrDefault(e => e.Bot == bot);
    public bool Bind(Entry entry, PracticeIdentity bot) => !entry.Retired && entry.Bot == null &&
        _entries.TryGetValue(entry.Id, out var found) && ReferenceEquals(entry, found) && BindOnce(entry, bot);
    private static bool BindOnce(Entry entry, PracticeIdentity bot) { entry.Bot = bot; return true; }
    public void Retire(Entry entry, double now = 0)
    { if (!entry.Retired) { entry.Retired = true; entry.RetiredAt = now; } }
    /// <summary>Drop a retired entry so its number becomes available again on the next reserve.</summary>
    public void Drop(Entry entry) => _entries.Remove(entry.Id);
    public void PruneRetired(double requestedBefore)
    {
        foreach (var id in _entries.Where(pair => pair.Value.Retired && pair.Value.RetiredAt < requestedBefore).Select(pair => pair.Key).ToArray())
            _entries.Remove(id);
    }
    public void Clear() => _entries.Clear();
}

/// <summary>Damage events and effective HP loss are separate (overkill is not extra HP lost).
/// Slow time integrates VelocityModifier after damage; zero world speed is never used as a slow signal.</summary>
public sealed class PracticeBotStatistics(int health, int armor, double now)
{
    public int InitialHealth { get; } = health;
    public int RemainingHealth { get; private set; } = health;
    public int RemainingArmor { get; private set; } = armor;
    public long EffectiveHealthLoss { get; private set; }
    public long EventDamage { get; private set; }
    public long ArmorLoss { get; private set; }
    public int Hits { get; private set; }
    public int Headshots { get; private set; }
    public double SlowSeconds { get; private set; }
    public float LowestVelocityModifier { get; private set; } = 1;
    public double StartedAt { get; } = now;
    public bool HasDamage { get; private set; }
    private double _sampleAt = now;
    private bool _slow;
    public void Hurt(int actualHealth, int actualArmor, int eventDamage, bool headshot, double time, float modifier)
    {
        Sample(time, modifier);
        int hp = Math.Max(0, actualHealth), ap = Math.Max(0, actualArmor);
        long loss = Math.Max(0, (long)RemainingHealth - hp);
        EffectiveHealthLoss += loss;
        ArmorLoss += Math.Max(0, (long)RemainingArmor - ap);
        EventDamage += Math.Max(0, eventDamage);
        RemainingHealth = hp; RemainingArmor = ap;
        Hits++; if (headshot) Headshots++;
        HasDamage |= loss > 0 || eventDamage > 0;
        Sample(time, modifier);
    }
    public void Observe(int actualHealth, int actualArmor)
    {
        // Refresh the baseline without counting a loss: used when a bot regenerates.
        RemainingHealth = Math.Max(0, actualHealth);
        RemainingArmor = Math.Max(0, actualArmor);
    }
    public void Sample(double time, float modifier)
    {
        if (!double.IsFinite(time)) return;
        double dt = time - _sampleAt;
        // Never invent seconds over a disconnected server, map load or paused sampling gap.
        if (_slow && dt is >= 0 and <= .25) SlowSeconds += dt;
        _sampleAt = time;
        _slow = HasDamage && float.IsFinite(modifier) && modifier is >= 0 and < .999f;
        if (_slow) LowestVelocityModifier = Math.Min(LowestVelocityModifier, modifier);
    }
}
