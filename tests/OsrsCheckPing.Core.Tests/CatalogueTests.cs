using System.Buffers.Binary;
using System.Net;
using System.Text;
using OsrsCheckPing.Core;

namespace OsrsCheckPing.Core.Tests;

public sealed class CatalogueTests
{
    internal static byte[] Encode(params WorldInfo[] worlds)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var payload = new MemoryStream();
        void Write16(int value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, unchecked((ushort)value));
            payload.Write(bytes);
        }
        void Write32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            payload.Write(bytes);
        }
        void WriteString(string value)
        {
            payload.Write(Encoding.GetEncoding(1252).GetBytes(value));
            payload.WriteByte(0);
        }
        Write16(worlds.Length);
        foreach (var world in worlds)
        {
            Write16(world.Id);
            Write32(world.Flags);
            WriteString(world.Host);
            WriteString(world.Activity);
            payload.WriteByte((byte)world.Location);
            Write16(world.Population);
        }
        byte[] data = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)payload.Length);
        payload.ToArray().CopyTo(data, 4);
        return data;
    }

    [Fact]
    public void DecodesBigEndianFieldsCp1252AndTrailingTransportPadding()
    {
        var world = new WorldInfo(552, 0x80000001, "oldschool252a.runescape.com", "Café – PvP", 12, -1);
        var bytes = Encode(world).Concat(new byte[100]).ToArray();
        Assert.Equal(world, Assert.Single(WorldCatalogueClient.Decode(bytes)));
        Assert.Equal("Unknown region (12)", world.Region);
        Assert.True(world.Members);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void RejectsTruncatedResponses(int bytes)
    {
        var encoded = Encode(new WorldInfo(301, 0, "oldschool1.runescape.com", "-", 0, 100));
        Assert.Throws<InvalidDataException>(() => WorldCatalogueClient.Decode(encoded[..bytes]));
    }

    [Fact]
    public void RejectsDuplicateWorldsInvalidHostAndEmptyCatalogue()
    {
        var world = new WorldInfo(301, 0, "oldschool1.runescape.com", "-", 0, 100);
        Assert.Throws<InvalidDataException>(() => WorldCatalogueClient.Decode(Encode(world, world)));
        Assert.Throws<InvalidDataException>(() => WorldCatalogueClient.Decode(Encode(world with { Host = "not a hostname" })));
        Assert.Throws<InvalidDataException>(() => WorldCatalogueClient.Decode(Encode()));
    }

    [Fact]
    public void RejectsOverstatedCountAndUnterminatedStrings()
    {
        var data = Encode(new WorldInfo(301, 0, "oldschool1.runescape.com", "-", 0, 100));
        data[5] = 2;
        Assert.Throws<InvalidDataException>(() => WorldCatalogueClient.Decode(data));
        data = Encode(new WorldInfo(301, 0, "oldschool1.runescape.com", "-", 0, 100));
        Array.Fill(data, (byte)'x', 12, data.Length - 12);
        Assert.Throws<InvalidDataException>(() => WorldCatalogueClient.Decode(data));
    }

    [Fact]
    public async Task ClientFetchesOfficialEndpointAndChecksHttpStatus()
    {
        var world = new WorldInfo(301, 1, "supplied-host.runescape.com", "-", 0, 100);
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            Assert.Equal(WorldCatalogueClient.CatalogueUri, request.RequestUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encode(world)) };
        }));
        Assert.Equal(world, Assert.Single(await new WorldCatalogueClient(http).FetchAsync(CancellationToken.None)));
        using var failing = new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new WorldCatalogueClient(failing).FetchAsync(CancellationToken.None));
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
