using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;

namespace ModlauncherIV.App;

/// <summary>A server as the master list describes it, right now.</summary>
public sealed record LiveServer(
    string Address,
    string Name,
    string GameMode,
    int Players,
    int MaxPlayers,
    bool Locked,
    bool Official,
    IReadOnlyList<string> Games);

/// <summary>
/// The list of servers that are up, from GTA Connected's own master list.
///
/// The client's launcher asks serverlisting.gtaconnected.com over a WebSocket
/// with the sub-protocol "ws_masterlist1" and reads binary frames; the page
/// that host serves does the same thing in JavaScript, which is where the shape
/// below was read from rather than guessed. Both ends use .NET's own
/// conventions - a 7-bit encoded length in front of every string and integer -
/// so the reader here is a few lines.
///
/// This is somebody else's protocol and nobody has promised it will stay put.
/// So every failure is the same failure: an empty list and a reason. What the
/// launcher does with that is offer the client's own browser, which is where
/// this list would have come from anyway.
/// </summary>
public static class ServerListing
{
    public const string Endpoint = "wss://serverlisting.gtaconnected.com/";

    private const string SubProtocol = "ws_masterlist1";

    /// <summary>The games we care about: GTA IV, and the episodes.</summary>
    private static readonly string[] Games = ["IVC", "EFLCC"];

    private const byte Join = 0;
    private const byte ServerAdd = 0;
    private const byte Joined = 4;

    /// <summary>
    /// Asks the list, and gives up quietly.
    /// </summary>
    /// <param name="timeout">
    /// The whole exchange, connection included. Short on purpose: this sits in
    /// front of somebody who wants to play, and a list that takes ten seconds
    /// is worse than a button that opens the client's own.
    /// </param>
    public static async Task<(IReadOnlyList<LiveServer> Servers, string? Error)> FetchAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var cutoff = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cutoff.CancelAfter(timeout);

        var servers = new List<LiveServer>();

        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol(SubProtocol);

            await socket.ConnectAsync(new Uri(Endpoint), cutoff.Token).ConfigureAwait(false);
            await socket.SendAsync(JoinMessage(), WebSocketMessageType.Binary, true, cutoff.Token)
                .ConfigureAwait(false);

            var buffer = new byte[16 * 1024];

            while (socket.State == WebSocketState.Open)
            {
                var message = await ReadMessageAsync(socket, buffer, cutoff.Token).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                // "Joined" closes the opening batch: everything that was up has
                // been sent by then. Staying on the socket after it would mean
                // waiting for a server to change something.
                if (Read(message, servers) == Joined)
                {
                    break;
                }
            }

            await CloseQuietly(socket).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Ran into the cutoff. Whatever arrived before it still counts.
            return (Sorted(servers), servers.Count > 0 ? null : "The server list did not answer in time.");
        }
        catch (Exception e) when (e is WebSocketException or HttpRequestException or IOException
                                      or InvalidOperationException or UriFormatException)
        {
            return (Sorted(servers), $"The server list could not be reached: {e.Message}");
        }

        return (Sorted(servers), null);
    }

    /// <summary>Busiest first - that is what somebody looking for a game wants.</summary>
    private static IReadOnlyList<LiveServer> Sorted(IEnumerable<LiveServer> servers) => servers
        .OrderByDescending(s => s.Players)
        .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    /// <summary>The "join" message, as the client sends it. Public so it can be read back in a test.</summary>
    public static ArraySegment<byte> JoinMessage()
    {
        var body = new List<byte> { Join };

        Write7Bit(body, 0); // flags, none of which we ask for
        Write7Bit(body, Games.Length);

        foreach (var game in Games)
        {
            WriteString(body, game);
        }

        return new ArraySegment<byte>(body.ToArray());
    }

    /// <summary>
    /// One whole message, however many frames it arrives in. Returns null when
    /// the far end closed.
    /// </summary>
    private static async Task<byte[]?> ReadMessageAsync(
        ClientWebSocket socket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return message.ToArray();
            }

            // A single message larger than this is not a server list.
            if (message.Length > 4 * 1024 * 1024)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Reads one message and adds what it describes. Returns its type, so the
    /// caller can tell the opening batch from what follows.
    /// </summary>
    public static int Read(byte[] message, List<LiveServer> servers)
    {
        var offset = 0;

        try
        {
            var type = Read7Bit(message, ref offset);

            if (type != ServerAdd)
            {
                return type;
            }

            var flags = Read7Bit(message, ref offset);
            var name = ReadString(message, ref offset);
            var mode = ReadString(message, ref offset);
            var max = Read7Bit(message, ref offset);
            var current = Read7Bit(message, ref offset);
            var address = ReadString(message, ref offset);

            var games = new List<string>();
            var count = Read7Bit(message, ref offset);

            for (var i = 0; i < count && i < 32; i++)
            {
                games.Add(ReadString(message, ref offset));
            }

            // The address is handed to another program on a command line, so it
            // passes the same check as one typed in by hand - this one comes
            // from a stranger's server entry, which makes it matter more.
            if (!ServerBook.IsAddress(address))
            {
                return type;
            }

            servers.Add(new LiveServer(
                address.Trim(),
                Clean(name),
                Clean(mode),
                current,
                max,
                (flags & 1) != 0,
                (flags & 2) != 0,
                games));

            return type;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException
                                      or OverflowException or DecoderFallbackException)
        {
            // One unreadable entry costs that entry. The protocol belongs to
            // somebody else and may grow a field without telling us.
            return -1;
        }
    }

    /// <summary>
    /// A name comes from whoever runs the server. It is only ever shown, but it
    /// should stay one line and not paint the page with control characters.
    /// </summary>
    private static string Clean(string text) =>
        new(text.Where(c => !char.IsControl(c)).Take(60).ToArray());

    private static void Write7Bit(List<byte> target, int value)
    {
        var remaining = (uint)value;

        while (remaining >= 0x80)
        {
            target.Add((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        target.Add((byte)remaining);
    }

    private static void WriteString(List<byte> target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);

        Write7Bit(target, bytes.Length);
        target.AddRange(bytes);
    }

    private static int Read7Bit(byte[] source, ref int offset)
    {
        var result = 0;

        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = source[offset++];
            result |= (b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new OverflowException("7-bit encoded integer without an end.");
    }

    private static string ReadString(byte[] source, ref int offset)
    {
        var length = Read7Bit(source, ref offset);

        if (length < 0 || offset + length > source.Length)
        {
            throw new IndexOutOfRangeException();
        }

        var text = Encoding.UTF8.GetString(source, offset, length);
        offset += length;

        return text;
    }

    private static async Task CloseQuietly(ClientWebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));

                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, closing.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
        {
            // Nothing left to save at this point - the list is already read.
        }
    }
}
