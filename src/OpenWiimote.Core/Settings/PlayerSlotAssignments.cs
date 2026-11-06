using OpenWiimote.Core.Protocol;

namespace OpenWiimote.Core.Settings;

/// <summary>Remembers up to four player identities, independently of connection order.</summary>
public sealed class PlayerSlotAssignments
{
    private readonly Dictionary<string, int> _preferred = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _active = new(StringComparer.OrdinalIgnoreCase);

    public PlayerSlotAssignments(IReadOnlyDictionary<string, int>? saved = null)
    {
        if (saved is null) return;
        foreach (var (key, slot) in saved)
        {
            if (string.IsNullOrWhiteSpace(key) || slot is < 1 or > 4) continue;
            var identity = Normalize(key);
            if (!_preferred.ContainsKey(identity) && !_preferred.ContainsValue(slot)) _preferred[identity] = slot;
        }
    }

    // The owning service serializes assignment and release with its session lock.
    public int Assign(string deviceKey)
    {
        var key = Normalize(deviceKey);
        if (_active.TryGetValue(key, out var current)) return current;
        var used = _active.Values.ToHashSet();
        var slot = _preferred.GetValueOrDefault(key);
        if (slot == 0 || used.Contains(slot))
        {
            slot = Enumerable.Range(1, 4).FirstOrDefault(candidate => !used.Contains(candidate) && !_preferred.ContainsValue(candidate));
            if (slot == 0) slot = Enumerable.Range(1, 4).FirstOrDefault(candidate => !used.Contains(candidate));
        }
        if (slot == 0) return 0; // More remotes than player lights: never claim a duplicate slot.
        foreach (var previous in _preferred.Where(pair => pair.Value == slot).Select(pair => pair.Key).ToArray())
            _preferred.Remove(previous);
        _preferred[key] = slot;
        _active[key] = slot;
        return slot;
    }

    public void Release(string deviceKey) => _active.Remove(Normalize(deviceKey));
    public Dictionary<string, int> Save() => new(_preferred, StringComparer.OrdinalIgnoreCase);
    public static byte LedMask(int player) => player is >= 1 and <= 4 ? (byte)(0x10 << (player - 1)) : (byte)0;
    private static string Normalize(string key) => HidDeviceIdentity.TryFormatBluetoothAddress(key) ?? key.Trim();
}
