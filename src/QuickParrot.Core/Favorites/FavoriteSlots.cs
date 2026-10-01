using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickParrot.Core.Favorites;

/// <summary>
/// The clips on F1-F12, as library-relative "/"-separated paths (null = empty). Immutable, compared by value, and
/// always exactly <see cref="Count"/> slots of normalized paths, however it was built.
/// </summary>
[JsonConverter(typeof(FavoriteSlotsJsonConverter))]
public sealed class FavoriteSlots : IEquatable<FavoriteSlots>
{
    public const int Count = 12;

    public static readonly FavoriteSlots Empty = new([]);

    private readonly string?[] _paths;

    /// <summary>Extra entries are dropped, missing ones are empty, and unusable paths become empty.</summary>
    public FavoriteSlots(IEnumerable<string?> paths)
    {
        _paths = new string?[Count];
        var i = 0;
        foreach (var path in paths)
        {
            if (i == Count)
                break;

            _paths[i++] = NormalizePath(path);
        }
    }

    /// <summary>Slot 1-12's path, or null if it's empty.</summary>
    public string? this[int slot] => _paths[IndexOf(slot)];

    public IReadOnlyList<string?> Paths => Array.AsReadOnly(_paths);

    /// <summary>Bit (slot - 1) is set for each slot holding a clip.</summary>
    public int AssignedMask
    {
        get
        {
            var mask = 0;
            for (var i = 0; i < Count; i++)
            {
                if (_paths[i] is not null)
                    mask |= 1 << i;
            }

            return mask;
        }
    }

    public static bool IsValidSlot(int slot) => slot is >= 1 and <= Count;

    public static string KeyName(int slot) => $"F{slot}";

    /// <summary>A clip's name as shown to the user: its file name without the extension.</summary>
    public static string DisplayName(string relativePath)
    {
        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        var withoutExtension = Path.GetFileNameWithoutExtension(name);
        return withoutExtension.Length > 0 ? withoutExtension : name;
    }

    /// <summary>"/"-separated with no empty segments, or null if empty, rooted or able to escape the library.</summary>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path[0] is '/' or '\\' || path.Contains(':'))
            return null;

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.Trim('.', ' ').Length == 0) // ".", ".." and names Windows would trim away
                return null;
        }

        return string.Join('/', segments);
    }

    public FavoriteSlots With(int slot, string? path)
    {
        var paths = (string?[])_paths.Clone();
        paths[IndexOf(slot)] = path;
        return new FavoriteSlots(paths);
    }

    public bool Equals(FavoriteSlots? other) =>
        other is not null && _paths.AsSpan().SequenceEqual(other._paths);

    public override bool Equals(object? obj) => Equals(obj as FavoriteSlots);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var path in _paths)
            hash.Add(path);

        return hash.ToHashCode();
    }

    private static int IndexOf(int slot) =>
        IsValidSlot(slot) ? slot - 1 : throw new ArgumentOutOfRangeException(nameof(slot), slot, "Favorites are F1-F12.");
}

/// <summary>Reads and writes <see cref="FavoriteSlots"/> as an array of paths, tolerating hand-edited junk.</summary>
public sealed class FavoriteSlotsJsonConverter : JsonConverter<FavoriteSlots>
{
    public override FavoriteSlots Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return FavoriteSlots.Empty;
        }

        var paths = new List<string?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            paths.Add(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);
            reader.Skip(); // steps over a nested object or array; a no-op for scalars
        }

        return new FavoriteSlots(paths);
    }

    public override void Write(Utf8JsonWriter writer, FavoriteSlots value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var path in value.Paths)
            writer.WriteStringValue(path);

        writer.WriteEndArray();
    }
}
