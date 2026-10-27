namespace ZePractice;

public static class MapIdentityLogic
{
    public const ulong IndividualBase = 76561197960265728;
    public static ulong? Verified(ulong reported, ulong? authorized, bool bot) =>
        !bot && authorized == reported && reported > IndividualBase && reported <= IndividualBase + uint.MaxValue ? reported : null;
}

public sealed class MapIdentityBinding
{
    public int? UserId { get; private set; }
    public uint Pawn { get; private set; }
    public ulong Identity { get; private set; }
    public bool Matches(int? userId, uint pawn, ulong identity) => identity != 0 && UserId == userId && Pawn == pawn && Identity == identity;
    public void Published(int? userId, uint pawn, ulong identity) { UserId = userId; Pawn = pawn; Identity = identity; }
}
