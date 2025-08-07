using NodeTunnel.HTTP;
using NodeTunnel.TCP;
using NodeTunnel.UDP;

namespace NodeTunnel;

public class NodeTunnel {
    public static async Task Main() {
        var tcpHandler = new TCPHandler();
        var udpHandler = new UDPHandler(tcpHandler);
        var statusServer = new StatusServer(tcpHandler);

        try {
            var tcpTask = tcpHandler.StartTcpAsync();
            var udpTask = udpHandler.StartUdpAsync();
            var statusTask = statusServer.StartAsync();

            await Task.WhenAny(tcpTask, udpTask, statusTask);
        }
        catch (Exception ex) {
            Console.WriteLine($"Server error: {ex.Message}");
        }
        
        Console.WriteLine("Server stopped.");
    }
}
