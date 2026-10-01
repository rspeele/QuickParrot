namespace QuickParrot.Core.Devices;

/// <summary>What every listed audio endpoint has, render or capture.</summary>
public interface IAudioEndpoint
{
    string Id { get; }

    string Name { get; }

    bool IsActive { get; }
}

/// <summary>Endpoint IDs and names compare case-insensitively.</summary>
public static class Endpoints
{
    public static StringComparer IdComparer => StringComparer.OrdinalIgnoreCase;

    public static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The active endpoint with this ID, or null (always null for a null ID).</summary>
    public static T? FindActive<T>(IEnumerable<T> endpoints, string? id) where T : class, IAudioEndpoint =>
        id is null ? null : endpoints.FirstOrDefault(d => d.IsActive && SameId(d.Id, id));
}
