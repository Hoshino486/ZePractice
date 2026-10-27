namespace ZePractice;

public sealed class WorkshopVoteLedger
{
    private readonly List<string> _candidates = new();
    private readonly Dictionary<uint, string> _votes = new();
    private readonly HashSet<uint> _rtv = new();
    private HashSet<uint> _voters = new();
    public IReadOnlyList<string> Candidates => _candidates.AsReadOnly();
    public int PlayerCount => _voters.Count;
    public int RtvCount => _rtv.Count;
    public int RequiredRtv => RequiredVotes(PlayerCount);
    public bool Quorum => PlayerCount > 0 && RtvCount >= RequiredRtv;
    public static int RequiredVotes(int players) => players <= 0 ? 0 : checked((int)((long)players * 4 / 5 + 1));

    public void SyncVoters(IEnumerable<uint> voters)
    {
        _voters = voters.ToHashSet();
        _rtv.IntersectWith(_voters);
        foreach (uint identity in _votes.Keys.Where(identity => !_voters.Contains(identity)).ToArray()) _votes.Remove(identity);
    }

    public bool AddCandidate(string identifier, int limit)
    {
        if (string.IsNullOrEmpty(identifier) || !PublicGameplayLogic.TryWorkshopId(identifier, out string canonical) || canonical != identifier ||
            _candidates.Count >= limit || _candidates.Contains(identifier, StringComparer.Ordinal)) return false;
        _candidates.Add(identifier);
        return true;
    }

    public bool RemoveCandidate(string identifier)
    {
        if (!_candidates.Remove(identifier)) return false;
        foreach (uint identity in _votes.Where(pair => pair.Value == identifier).Select(pair => pair.Key).ToArray()) _votes.Remove(identity);
        return true;
    }

    public bool Vote(uint identity, string identifier)
    {
        if (!_voters.Contains(identity) || !_candidates.Contains(identifier, StringComparer.Ordinal)) return false;
        _votes[identity] = identifier;
        return true;
    }

    public string? Choice(uint identity) => _votes.GetValueOrDefault(identity);
    public int VotesFor(string identifier) => _votes.Values.Count(value => value == identifier);
    public bool Support(uint identity) => _voters.Contains(identity) && _rtv.Add(identity);
    public bool Withdraw(uint identity) => _rtv.Remove(identity);
    public void ClearRtv() => _rtv.Clear();
    public void ResetBallots() { _votes.Clear(); _rtv.Clear(); _voters.Clear(); }
    public void ResetForMap() { _candidates.Clear(); ResetBallots(); }
    public bool CanSelect => _candidates.Count == 1 || _candidates.Count > 1 && _votes.Count > 0;

    public string? SelectWinner(Func<int, int> choose)
    {
        if (!CanSelect) return null;
        if (_candidates.Count == 1) return _candidates[0];
        int maximum = _candidates.Max(VotesFor);
        var tied = _candidates.Where(identifier => VotesFor(identifier) == maximum).ToArray();
        return tied.Length == 1 ? tied[0] : tied[choose(tied.Length)];
    }

    public static bool IsPublicChat(string text)
    {
        text = (text ?? "").Trim();
        return text.Equals("rtv", StringComparison.OrdinalIgnoreCase) || text.Equals("unrtv", StringComparison.OrdinalIgnoreCase) ||
            PublicGameplayLogic.IsWorkshopChat(text);
    }
}
