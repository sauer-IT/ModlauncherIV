using System.IO;
using System.Text.Json;
using ModlauncherIV.Core.Backup;

namespace ModlauncherIV.App;

/// <summary>A server somebody wants to keep.</summary>
/// <param name="Name">What to call it. Empty means: the address is the name.</param>
/// <param name="Address">host:port, as it goes on the command line.</param>
public sealed record SavedServer(string Name, string Address);

/// <summary>
/// The servers kept on this machine, beside the ones the multiplayer client
/// remembers by itself.
///
/// The client keeps a history: where it was last, newest first, and only that.
/// A history is not a choice - it forgets, it is ordered by accident, and a
/// server you have not been on yet is never in it. So the launcher keeps a
/// second, deliberate list, and shows both together.
///
/// It does not try to be a server browser. There is no list of live servers
/// that can be fetched without guessing at somebody's undocumented endpoint,
/// and a browser that goes stale in the launcher while the client has a real
/// one would be worse than not having it.
/// </summary>
public static class ServerBook
{
    /// <summary>
    /// Longest address accepted. Long enough for a host name with a port, short
    /// enough that nothing surprising ends up on a command line.
    /// </summary>
    public const int MaxAddressLength = 64;

    public static string DefaultFile { get; } = Path.Combine(AppPaths.Root, "servers.json");

    /// <summary>
    /// Whether this can be handed to the client as an address.
    ///
    /// It ends up on a command line, so the rule is what may appear in a host
    /// name and a port and nothing else - no spaces, no quotes, no switches.
    /// The same rule the client's own history is read with.
    /// </summary>
    public static bool IsAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var trimmed = address.Trim();

        return trimmed.Length <= MaxAddressLength
               && trimmed.All(c => char.IsLetterOrDigit(c) || c is '.' or ':' or '-' or '_')
               && trimmed.Any(char.IsLetterOrDigit);
    }

    public static IReadOnlyList<SavedServer> Load(string? file = null)
    {
        var path = file ?? DefaultFile;

        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var saved = JsonSerializer.Deserialize<SavedServer[]>(File.ReadAllText(path));

            // Foreign data, even when we wrote it: a hand-edited file must not
            // be able to put something on a command line.
            return saved is null
                ? []
                : saved.Where(s => IsAddress(s.Address))
                    .Select(s => new SavedServer(Clean(s.Name), s.Address.Trim()))
                    .ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Adds one, or renames the one already on that address.</summary>
    public static IReadOnlyList<SavedServer> Add(string name, string address, string? file = null)
    {
        if (!IsAddress(address))
        {
            return Load(file);
        }

        var entry = new SavedServer(Clean(name), address.Trim());

        var servers = Load(file)
            .Where(s => !Same(s.Address, entry.Address))
            .Append(entry)
            .ToArray();

        Save(servers, file);
        return servers;
    }

    public static IReadOnlyList<SavedServer> Remove(string address, string? file = null)
    {
        var servers = Load(file).Where(s => !Same(s.Address, address)).ToArray();

        Save(servers, file);
        return servers;
    }

    private static void Save(IReadOnlyList<SavedServer> servers, string? file)
    {
        var path = file ?? DefaultFile;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(servers, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A list of servers is a convenience. Losing it must not take the
            // home page down with it.
        }
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A name is shown, never executed - but it should stay one line.</summary>
    private static string Clean(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : new string(name.Trim().Where(c => !char.IsControl(c)).Take(40).ToArray());

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
