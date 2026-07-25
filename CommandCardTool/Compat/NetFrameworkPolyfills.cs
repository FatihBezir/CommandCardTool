// Types the C# compiler needs for records (init accessors) and for the
// index/range operators (s[1..], list[^1]). .NET Framework 4.8 does not ship
// them, but the compiler is happy with any definition in the right namespace.

#if NETFRAMEWORK

using System.Diagnostics;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace System
{
    /// <summary>Position in a collection, either from the start or from the end (<c>^1</c>).</summary>
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _value = fromEnd ? ~value : value;
        }

        public static Index Start => new Index(0);
        public static Index End   => new Index(~0);

        public static Index FromStart(int value) => new Index(value);
        public static Index FromEnd(int value)   => new Index(value, fromEnd: true);

        public int  Value     => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;

        public int GetOffset(int length) => IsFromEnd ? length + _value + 1 : _value;

        public static implicit operator Index(int value) => new Index(value);

        public bool Equals(Index other)   => _value == other._value;
        public override bool Equals(object? obj) => obj is Index other && Equals(other);
        public override int GetHashCode() => _value;
        public override string ToString() => IsFromEnd ? "^" + Value : Value.ToString();
    }

    /// <summary>Half-open range between two <see cref="Index"/> values (<c>1..^2</c>).</summary>
    internal readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End   { get; }

        public Range(Index start, Index end) { Start = start; End = end; }

        public static Range All => new Range(Index.Start, Index.End);
        public static Range StartAt(Index start) => new Range(start, Index.End);
        public static Range EndAt(Index end)     => new Range(Index.Start, end);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end   = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? obj) => obj is Range other && Equals(other);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
        public override string ToString() => Start + ".." + End;
    }
}

namespace LauncherWinUI.Compat
{
    using System.Collections.Generic;

    /// <summary>
    /// BCL methods added after .NET Framework 4.8. Extension methods are only
    /// picked when no instance method matches, so these vanish on newer targets.
    /// </summary>
    internal static class BclShims
    {
        public static bool Contains(this string s, char value)
            => s.IndexOf(value) >= 0;

        public static bool Contains(this string s, string value, StringComparison comparison)
            => s.IndexOf(value, comparison) >= 0;

        public static bool StartsWith(this string s, char value)
            => s.Length > 0 && s[0] == value;

        public static bool EndsWith(this string s, char value)
            => s.Length > 0 && s[s.Length - 1] == value;

        public static int GetHashCode(this string s, StringComparison comparison)
            => comparison is StringComparison.OrdinalIgnoreCase or StringComparison.InvariantCultureIgnoreCase
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(s)
                : s.GetHashCode();

        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
            where TKey : notnull
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }

        public static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, out TValue value)
            where TKey : notnull
        {
            if (!dict.TryGetValue(key, out value!)) return false;
            dict.Remove(key);
            return true;
        }

        public static void Deconstruct<TKey, TValue>(
            this KeyValuePair<TKey, TValue> kv, out TKey key, out TValue value)
        {
            key = kv.Key;
            value = kv.Value;
        }

        public static TValue? GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dict, TKey key)
            where TKey : notnull
            => dict.TryGetValue(key, out var v) ? v : default;

        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
            where TKey : notnull
            => dict.TryGetValue(key, out var v) ? v : defaultValue;

        // net48 only has the char[]/string[] forms of Split.
        public static string[] Split(this string s, char separator, int count)
            => s.Split(new[] { separator }, count);

        public static string[] Split(this string s, char separator, StringSplitOptions options)
            => s.Split(new[] { separator }, options);

        public static string[] Split(this string s, string separator)
            => s.Split(new[] { separator }, StringSplitOptions.None);

        public static string[] Split(this string s, string separator, int count)
            => s.Split(new[] { separator }, count, StringSplitOptions.None);

        public static string[] Split(this string s, string separator, StringSplitOptions options)
            => s.Split(new[] { separator }, options);
    }

    
    }

#endif
