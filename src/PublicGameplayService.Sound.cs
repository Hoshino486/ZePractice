using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private sealed class MapSound
    {
        public int Guid, Source;
        public uint Hash;
        public float BaseVolume = 1, StartedAt;
        public (int Slot, int? UserId)[] Recipients = [];
    }
    private readonly Dictionary<int, MapSound> _sounds = new();
    private bool _sendingVolume;

    private void InitializeSounds()
    {
        _plugin.HookUserMessage(208, SoundStarted, HookMode.Post);
        _plugin.HookUserMessage(209, SoundStopped, HookMode.Pre);
        _plugin.HookUserMessage(210, SoundParameters, HookMode.Post);
        _plugin.HookUserMessage(212, SoundHashStopped, HookMode.Pre);
    }

    private HookResult SoundStarted(UserMessage message)
    {
        if (!_active || _disposed) return HookResult.Continue;
        try
        {
            int index = message.ReadInt("source_entity_index");
            if (index < 1 || index >= 16384) return HookResult.Continue;
            var entity = Utilities.GetEntityFromIndex<CBaseEntity>(index);
            if (entity is not { IsValid: true } || !PublicGameplayLogic.IsMapSound(entity.DesignerName)) return HookResult.Continue;
            var sound = new MapSound
            {
                Guid = message.ReadInt("soundevent_guid"), Source = index, Hash = message.ReadUInt("soundevent_hash"), StartedAt = Server.CurrentTime,
                Recipients = message.Recipients.Where(Player).Select(player => (player.Slot, player.UserId)).ToArray()
            };
            if (PublicGameplayLogic.TryReadSoundVolume(message.ReadBytes("packed_params"), out float volume)) sound.BaseVolume = volume;
            if (_sounds.Count >= 256) _sounds.Remove(_sounds.MinBy(entry => entry.Value.StartedAt).Key);
            _sounds[sound.Guid] = sound;
            Later(() => { if (_sounds.TryGetValue(sound.Guid, out var current) && ReferenceEquals(current, sound)) SendSoundVolumes(sound); });
        }
        catch (Exception error) { Report("sound-start", error); }
        return HookResult.Continue;
    }

    private HookResult SoundStopped(UserMessage message)
    {
        if (!_disposed) _sounds.Remove(message.ReadInt("soundevent_guid"));
        return HookResult.Continue;
    }

    private HookResult SoundHashStopped(UserMessage message)
    {
        if (_disposed) return HookResult.Continue;
        uint hash = message.ReadUInt("soundevent_hash"); int source = message.ReadInt("source_entity_index");
        foreach (var entry in _sounds.Where(entry => entry.Value.Hash == hash && (source < 0 || entry.Value.Source == source)).ToArray()) _sounds.Remove(entry.Key);
        return HookResult.Continue;
    }

    private HookResult SoundParameters(UserMessage message)
    {
        if (_sendingVolume || !_active || _disposed) return HookResult.Continue;
        try
        {
            if (!_sounds.TryGetValue(message.ReadInt("soundevent_guid"), out var sound)) return HookResult.Continue;
            if (PublicGameplayLogic.TryReadSoundVolume(message.ReadBytes("packed_params"), out float volume)) sound.BaseVolume = volume;
            Later(() => { if (_sounds.TryGetValue(sound.Guid, out var current) && ReferenceEquals(current, sound)) SendSoundVolumes(sound); });
        }
        catch (Exception error) { Report("sound-params", error); }
        return HookResult.Continue;
    }

    private void SendSoundVolumes(MapSound sound)
    {
        foreach (var recipient in sound.Recipients)
        {
            var player = Utilities.GetPlayerFromSlot(recipient.Slot);
            if (!Player(player) || player!.UserId != recipient.UserId || player.IsBot) continue;
            var state = State(player);
            if (state.MusicVolume < 1) SendVolume(player, sound, state.MusicVolume);
        }
    }

    private void SendVolume(CCSPlayerController player, MapSound sound, float fraction)
    {
        try
        {
            _sendingVolume = true;
            using var message = UserMessage.FromId(210);
            message.SetInt("soundevent_guid", sound.Guid);
            message.SetBytes("packed_params", PublicGameplayLogic.VolumeParameters(sound.BaseVolume * fraction));
            var recipients = new RecipientFilter(); recipients.Add(player); message.Send(recipients);
        }
        catch (Exception error) { Report("sound-volume", error); }
        finally { _sendingVolume = false; }
    }

    private void ApplyMusicVolume(CCSPlayerController player)
    {
        foreach (var sound in _sounds.Values)
            if (sound.Recipients.Any(recipient => recipient.Slot == player.Slot && recipient.UserId == player.UserId)) SendVolume(player, sound, State(player).MusicVolume);
    }

    private void DisposeSounds()
    {
        PluginCleanup.ForPlayers(_active, _players.Count, () => Utilities.GetPlayers().Where(player => Player(player) && !player.IsBot), player =>
        {
            if (!_players.TryGetValue(player.Slot, out var state) || state.UserId != player.UserId || state.MusicVolume >= 1) return;
            foreach (var sound in _sounds.Values)
                if (sound.Recipients.Any(recipient => recipient.Slot == player.Slot && recipient.UserId == player.UserId)) SendVolume(player, sound, 1);
        }, "sound-client-reset", Report);
        PluginCleanup.Run("sound-unhook-start", () => _plugin.UnhookUserMessage(208, SoundStarted, HookMode.Post), Report);
        PluginCleanup.Run("sound-unhook-stop", () => _plugin.UnhookUserMessage(209, SoundStopped, HookMode.Pre), Report);
        PluginCleanup.Run("sound-unhook-params", () => _plugin.UnhookUserMessage(210, SoundParameters, HookMode.Post), Report);
        PluginCleanup.Run("sound-unhook-hash", () => _plugin.UnhookUserMessage(212, SoundHashStopped, HookMode.Pre), Report);
        _sounds.Clear();
    }
}
