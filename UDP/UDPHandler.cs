using System.Net;
using System.Net.Sockets;
using System.Text;
using NodeTunnel.TCP;
using NodeTunnel.Utils;

namespace NodeTunnel.UDP;

public class UDPHandler {
    private UdpClient _udp;
    private TCPHandler _tcp;
    private CancellationTokenSource _ct;
    private readonly Dictionary<string, IPEndPoint> _oidToEndpoint = new();

    public UDPHandler(TCPHandler tcpHandler) {
        _tcp = tcpHandler;
    }
    
    public async Task StartUdpAsync(string host = "0.0.0.0", int port = 9999) {
        _udp = new UdpClient(port);
        _ct = new CancellationTokenSource();
        
        Console.WriteLine("UDP Listening on 9999");

        await HandleUdpPackets();
    }

    public async Task HandleUdpPackets() {
        try {
            while (!_ct.Token.IsCancellationRequested) {
                var res = await _udp.ReceiveAsync();
                var data = res.Buffer;
                var endpoint = res.RemoteEndPoint;

                if (data.Length < 8) continue;

                var senderOidLen = ByteUtils.UnpackU32(data, 0);
                if (data.Length < senderOidLen + 8) continue;

                var senderOid = Encoding.UTF8.GetString(data, 4, (int)senderOidLen);
                _oidToEndpoint[senderOid] = endpoint;

                var targetOidLen = ByteUtils.UnpackU32(data, 4 + (int)senderOidLen);
                if (data.Length < 8 + senderOidLen + targetOidLen) continue;

                var targetOid = Encoding.UTF8.GetString(data, 8 + (int)senderOidLen, (int)targetOidLen);
                var gameData = data.Skip(8 + (int)senderOidLen + (int)targetOidLen).ToArray();

                if (targetOid == "SERVER") {
                    var msg = Encoding.UTF8.GetString(gameData);

                    if (msg == "UDP_CONNECT") {
                        Console.WriteLine("Sending back UDP connect");
                        var response = CreateRelayPacket("SERVER", senderOid, Encoding.UTF8.GetBytes("UDP_CONNECT_RES"));
                        await _udp.SendAsync(response, endpoint);
                    }
                }
                
                var room = _tcp.GetRoomForPeer(senderOid);
                if (room == null) continue;

                try {
                    Console.WriteLine($"Received UDP from {senderOid} to {targetOid}, room has {room.GetPeers().Count} peers");

                    if (targetOid == "0") {
                        // Broadcast to all peers except sender
                        foreach (var peer in room.GetPeers()) {
                            if (peer.oid != senderOid && _oidToEndpoint.ContainsKey(peer.oid)) {
                                Console.WriteLine($"Broadcasting to {peer.oid} at {_oidToEndpoint[peer.oid]}");
                                
                                // Create a new relay packet with correct format
                                var relayPacket = CreateRelayPacket(senderOid, peer.oid, gameData);
                                await _udp.SendAsync(relayPacket, _oidToEndpoint[peer.oid]);
                            } else if (peer.oid != senderOid) {
                                Console.WriteLine($"No endpoint for {peer.oid}");
                            }
                        }
                    }
                    else {
                        // Send to specific peer
                        Console.WriteLine($"Trying to relay to specific peer {targetOid}");
                        if (room.HasPeer(targetOid) && _oidToEndpoint.ContainsKey(targetOid)) {
                            Console.WriteLine($"Relaying to {targetOid} at {_oidToEndpoint[targetOid]}");
                            
                            // Create a new relay packet with correct format
                            var relayPacket = CreateRelayPacket(senderOid, targetOid, gameData);
                            await _udp.SendAsync(relayPacket, _oidToEndpoint[targetOid]);
                        } else {
                            Console.WriteLine($"Cannot relay to {targetOid} - HasPeer: {room.HasPeer(targetOid)}, HasEndpoint: {_oidToEndpoint.ContainsKey(targetOid)}");
                        }
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error relaying UDP packet: {ex.Message}");
                }
            }
        }
        catch (ObjectDisposedException) {
            Console.WriteLine("Goodbye!");
        }
        catch (Exception ex) {
            Console.WriteLine($"UDP Handler Error: {ex.Message}");
        }
    }

    // Helper method to create properly formatted relay packets
    private byte[] CreateRelayPacket(string fromOid, string toOid, byte[] gameData) {
        var packet = new List<byte>();
        
        // Add sender OID length and data
        packet.AddRange(ByteUtils.PackU32((uint)fromOid.Length));
        packet.AddRange(Encoding.UTF8.GetBytes(fromOid));
        
        // Add target OID length and data
        packet.AddRange(ByteUtils.PackU32((uint)toOid.Length));
        packet.AddRange(Encoding.UTF8.GetBytes(toOid));
        
        // Add game data
        packet.AddRange(gameData);
        
        return packet.ToArray();
    }
}