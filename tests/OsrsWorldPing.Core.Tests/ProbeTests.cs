using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OsrsWorldPing.Core;

namespace OsrsWorldPing.Core.Tests;

public sealed class ProbeTests
{
    private sealed class Resolver : IDnsResolver
    {
        public Func<string, CancellationToken, Task<IPAddress[]>> Handler = (_, _) => Task.FromResult(new[] { IPAddress.Loopback });
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken token) => Handler(host, token);
    }
    private sealed class Sender : IIcmpSender
    {
        public Func<IPAddress, CancellationToken, Task<IcmpResponse>> Handler = (_, _) => Task.FromResult(new IcmpResponse(IPStatus.Success, 12));
        public Task<IcmpResponse> SendAsync(IPAddress address, CancellationToken token) => Handler(address, token);
    }
    [Theory]
    [InlineData(IPStatus.TimedOut, ProbeStatus.TimedOut)]
    [InlineData(IPStatus.DestinationHostUnreachable, ProbeStatus.Unreachable)]
    [InlineData(IPStatus.DestinationNetworkUnreachable, ProbeStatus.Unreachable)]
    [InlineData(IPStatus.TtlExpired, ProbeStatus.NetworkError)]
    public async Task NativeStatusIsPreservedWithoutInventingOfflineOrBlocked(IPStatus native, ProbeStatus expected)
    {
        var result = await new IcmpLatencyProbe(new Resolver(), new Sender { Handler = (_, _) => Task.FromResult(new IcmpResponse(native, 0)) }).ProbeAsync("supplied-host", default);
        Assert.Equal(expected, result.Status); Assert.Equal(native.ToString(), result.Diagnostic!.NativeCode);
        Assert.Equal("ICMP", result.Diagnostic.Stage); Assert.Equal("127.0.0.1", result.Diagnostic.Address);
    }
    [Fact]
    public async Task SuppliedHostAndResolvedAddressAreUsed()
    {
        string? host = null; IPAddress? sent = null;
        var dns = new Resolver { Handler = (value, _) => { host = value; return Task.FromResult(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }); } };
        var ping = new Sender { Handler = (value, _) => { sent = value; return Task.FromResult(new IcmpResponse(IPStatus.Success, 30)); } };
        var result = await new IcmpLatencyProbe(dns, ping).ProbeAsync("jagex-supplied.example", default);
        Assert.Equal("jagex-supplied.example", host); Assert.Equal(IPAddress.Loopback, sent); Assert.True(result.IsGood(30));
    }
    [Fact]
    public async Task DnsFailureDoesNotSendPing()
    {
        var dns = new Resolver { Handler = (_, _) => throw new SocketException((int)SocketError.HostNotFound) };
        var ping = new Sender { Handler = (_, _) => throw new InvalidOperationException("Should not probe") };
        var result = await new IcmpLatencyProbe(dns, ping).ProbeAsync("missing", default);
        Assert.Equal(ProbeStatus.DnsError, result.Status); Assert.Equal("DNS", result.Diagnostic!.Stage);
    }
    [Fact]
    public async Task LocalPermissionFailureIsNotAWorldOutage()
    {
        var ping = new Sender { Handler = (_, _) => throw new PingException("Denied", new Win32Exception(5)) };
        var result = await new IcmpLatencyProbe(new Resolver(), ping).ProbeAsync("host", default);
        Assert.Equal(ProbeStatus.PermissionDenied, result.Status);
    }
    [Fact]
    public async Task UnexpectedPingFailureIsNotMisreportedAsUnreachable()
    {
        var result = await new IcmpLatencyProbe(new Resolver(), new Sender { Handler = (_, _) => throw new PingException("Unspecified failure") }).ProbeAsync("host", default);
        Assert.Equal(ProbeStatus.Error, result.Status);
    }
    [Fact]
    public async Task CallerCancellationPropagatesThroughDns()
    {
        using var cancel = new CancellationTokenSource();
        var dns = new Resolver { Handler = async (_, token) => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); return []; } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IcmpLatencyProbe(dns, new Sender()).ProbeAsync("host", cancel.Token));
    }
    [Theory]
    [InlineData(30, 3)] [InlineData(29, 3)] [InlineData(2, 3)] [InlineData(5, 5)] [InlineData(17, 2)]
    public void ColumnOrderIsBalancedAndUnique(int count, int columns)
    {
        var positions = Enumerable.Range(0, count).Select(i => WorldColumnLayout.Position(i, count, columns)).ToArray();
        Assert.Equal(count, positions.Distinct().Count());
        Assert.Equal(positions, positions.OrderBy(p => p.Column).ThenBy(p => p.Row));
        Assert.All(positions, p => Assert.InRange(p.Column, 0, columns - 1));
        Assert.All(positions.GroupBy(p => p.Column), group => Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(p => p.Row)));
    }
}
