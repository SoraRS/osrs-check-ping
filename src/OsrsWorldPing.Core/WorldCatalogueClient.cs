using System.Buffers.Binary;
using System.Text;

namespace OsrsWorldPing.Core;

public sealed class WorldCatalogueClient(HttpClient httpClient) : IWorldCatalogueClient
{
    public static readonly Uri CatalogueUri = new("https://oldschool.runescape.com/slr.ws?order=LPWM");
    private const int MaximumBytes = 1024 * 1024;

    public async Task<IReadOnlyList<WorldInfo>> FetchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await httpClient.GetAsync(CatalogueUri, HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > MaximumBytes)
                throw new InvalidDataException("The world catalogue is too large.");
            buffer.Write(chunk, 0, read);
        }
        return Decode(buffer.ToArray());
    }

    public static IReadOnlyList<WorldInfo> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            throw new InvalidDataException("The world catalogue header is incomplete.");
        var length = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (length < 2 || length > MaximumBytes || length > data.Length - 4)
            throw new InvalidDataException("The world catalogue length is invalid.");

        // The transport can contain padding after the length-prefixed payload.
        var reader = new CatalogueReader(data.Slice(4, (int)length));
        int count = reader.ReadUInt16();
        if (count == 0)
            throw new InvalidDataException("The world catalogue contains no worlds.");
        var worlds = new List<WorldInfo>(count);
        var ids = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            int id = reader.ReadUInt16();
            uint flags = reader.ReadUInt32();
            string host = reader.ReadString(253);
            string activity = reader.ReadString(4096);
            int location = reader.ReadByte();
            int population = reader.ReadInt16();
            if (id == 0 || !ids.Add(id) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
                throw new InvalidDataException("The world catalogue contains an invalid world.");
            worlds.Add(new WorldInfo(id, flags, host, activity, location, population));
        }
        if (reader.Remaining != 0)
            throw new InvalidDataException("The world catalogue payload has unexpected fields.");
        return worlds;
    }

    private ref struct CatalogueReader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _remaining = data;
        private static readonly Encoding TextEncoding = CreateEncoding();
        public int Remaining => _remaining.Length;

        private static Encoding CreateEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }

        private ReadOnlySpan<byte> Take(int length)
        {
            if (_remaining.Length < length)
                throw new InvalidDataException("A world catalogue record is incomplete.");
            var value = _remaining[..length];
            _remaining = _remaining[length..];
            return value;
        }

        public byte ReadByte() => Take(1)[0];
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Take(2));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        public string ReadString(int maximumLength)
        {
            int length = _remaining.IndexOf((byte)0);
            if (length < 0 || length > maximumLength)
                throw new InvalidDataException("A world catalogue string is invalid.");
            string result = TextEncoding.GetString(Take(length));
            Take(1);
            return result;
        }
    }
}
