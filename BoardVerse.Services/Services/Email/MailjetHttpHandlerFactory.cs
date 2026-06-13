using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace BoardVerse.Services.Services.Email
{
    internal static class MailjetHttpHandlerFactory
    {
        public static SocketsHttpHandler Create() => new()
        {
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            },
            ConnectCallback = ConnectIPv4Async
        };

        private static async ValueTask<Stream> ConnectIPv4Async(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                var endpoint = await ResolveIPv4Async(context.DnsEndPoint, cancellationToken);
                await socket.ConnectAsync(endpoint, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task<EndPoint> ResolveIPv4Async(
            DnsEndPoint dnsEndPoint,
            CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(dnsEndPoint.Host, AddressFamily.InterNetwork, cancellationToken);
            if (addresses.Length == 0)
            {
                throw new IOException($"No IPv4 address resolved for {dnsEndPoint.Host}");
            }

            return new IPEndPoint(addresses[0], dnsEndPoint.Port);
        }
    }
}
