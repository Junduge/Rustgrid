#nullable enable
namespace Rustgrid.Shared.Sim
{
    /// <summary>
    /// Stable 64-bit hash (FNV-1a over little-endian bytes plus a final avalanche).
    /// Unlike GetHashCode it never changes between processes, runtimes or platforms,
    /// so it can identify levels, rules and game states in replays and metrics.
    /// Callers add fields in a fixed canonical order; collections are length-prefixed.
    /// </summary>
    public struct StableHasher
    {
        private const ulong OffsetBasis = 0xCBF29CE484222325UL;
        private const ulong Prime = 0x100000001B3UL;
        private ulong _hash;
        private bool _started;

        public void Add(int value) => Add((ulong)(uint)value, 4);

        public void Add(long value) => Add((ulong)value, 8);

        public void Add(ulong value) => Add(value, 8);

        public void Add(bool value) => Add(value ? 1UL : 0UL, 1);

        /// <summary>Length-prefixed UTF-16 code units: "ab"+"c" never collides with "a"+"bc".</summary>
        public void Add(string value)
        {
            Add(value.Length);
            for (int i = 0; i < value.Length; i++) Add(value[i], 2);
        }

        public ulong Value
        {
            get
            {
                ulong z = _started ? _hash : OffsetBasis;
                z = (z ^ (z >> 33)) * 0xFF51AFD7ED558CCDUL;
                z = (z ^ (z >> 33)) * 0xC4CEB9FE1A85EC53UL;
                return z ^ (z >> 33);
            }
        }

        public static string ToHex(ulong hash) => hash.ToString("x16");

        private void Add(ulong value, int bytes)
        {
            if (!_started)
            {
                _hash = OffsetBasis;
                _started = true;
            }
            for (int i = 0; i < bytes; i++)
            {
                _hash ^= (value >> (8 * i)) & 0xFF;
                _hash *= Prime;
            }
        }
    }
}
