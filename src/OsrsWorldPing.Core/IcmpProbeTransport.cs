using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OsrsWorldPing.Core;

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed record IcmpResponse(IPStatus Status, long RoundTripMilliseconds);

public interface IIcmpSender
{
    Task<IcmpResponse> SendAsync(IPAddress address, CancellationToken cancellationToken);
}

public sealed class IcmpLatencyProbe(IDnsResolver? resolver = null, IIcmpSender? sender = null) : ILatencyProbe
{
    private readonly IDnsResolver _resolver = resolver ?? new SystemDnsResolver();
    private readonly IIcmpSender _sender = sender ?? new SystemIcmpSender();
    public async Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        string stage = "DNS";
        IPAddress? address = null;
        ProbeResult Failure(ProbeStatus status, string code) => new(status, Diagnostic: new(stage, address?.ToString(), code));
        try
        {
            var addresses = await _resolver.ResolveAsync(host, timeout.Token).ConfigureAwait(false);
            address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
            if (address is null) return Failure(ProbeStatus.DnsError, "NoAddress");
            stage = "ICMP";
            var reply = await _sender.SendAsync(address, timeout.Token).ConfigureAwait(false);
            return reply.Status switch
            {
                IPStatus.Success => new ProbeResult(ProbeStatus.Success, reply.RoundTripMilliseconds, new(stage, address.ToString(), "Success")),
                IPStatus.TimedOut => Failure(ProbeStatus.TimedOut, reply.Status.ToString()),
                IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable or
                    IPStatus.DestinationProtocolUnreachable or IPStatus.DestinationPortUnreachable or IPStatus.BadRoute
                    => Failure(ProbeStatus.Unreachable, reply.Status.ToString()),
                _ => Failure(ProbeStatus.NetworkError, reply.Status.ToString())
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(stage == "DNS" ? ProbeStatus.DnsError : ProbeStatus.TimedOut, "DeadlineExceeded");
        }
        catch (SocketException error)
        {
            return Failure(error.SocketErrorCode == SocketError.AccessDenied ? ProbeStatus.PermissionDenied :
                stage == "DNS" ? ProbeStatus.DnsError : ProbeStatus.NetworkError, error.SocketErrorCode.ToString());
        }
        catch (PingException error)
        {
            var native = error.InnerException as Win32Exception;
            return Failure(native?.NativeErrorCode is 5 or 13 or 10013 ? ProbeStatus.PermissionDenied : ProbeStatus.Error,
                native is null ? nameof(PingException) : $"NativeError:{native.NativeErrorCode}");
        }
        catch (UnauthorizedAccessException) { return Failure(ProbeStatus.PermissionDenied, "AccessDenied"); }
        catch (PlatformNotSupportedException) { return Failure(ProbeStatus.Unsupported, "PlatformNotSupported"); }
    }

    private sealed class SystemDnsResolver : IDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken);
    }
    private sealed class SystemIcmpSender : IIcmpSender
    {
        public async Task<IcmpResponse> SendAsync(IPAddress address, CancellationToken cancellationToken)
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(2), buffer: Array.Empty<byte>(), cancellationToken: cancellationToken).ConfigureAwait(false);
            return new(reply.Status, reply.RoundtripTime);
        }
    }
}
