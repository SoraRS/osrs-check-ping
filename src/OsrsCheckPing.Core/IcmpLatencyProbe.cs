using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OsrsCheckPing.Core;

public sealed class IcmpLatencyProbe : ILatencyProbe
{
    public async Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token).ConfigureAwait(false);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
            if (address is null)
                return new ProbeResult(ProbeStatus.DnsError);
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(2),
                buffer: Array.Empty<byte>(), cancellationToken: timeout.Token).ConfigureAwait(false);
            return reply.Status switch
            {
                IPStatus.Success => new ProbeResult(ProbeStatus.Success, reply.RoundtripTime),
                IPStatus.TimedOut => new ProbeResult(ProbeStatus.TimedOut),
                _ => new ProbeResult(ProbeStatus.Unreachable)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(ProbeStatus.TimedOut);
        }
        catch (SocketException)
        {
            return new ProbeResult(ProbeStatus.DnsError);
        }
        catch (PingException)
        {
            return new ProbeResult(ProbeStatus.Unreachable);
        }
        catch (PlatformNotSupportedException)
        {
            return new ProbeResult(ProbeStatus.Error);
        }
    }
}
