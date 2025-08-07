using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NodeTunnel.UDP;
using NodeTunnel.Utils;

namespace NodeTunnel.TCP;

public class TCPHandler {
    public event Action<string> PeerDisconnected;
    public event Action<IEnumerable<string>> PeersDisconnected; 
    
    private TcpListener _tcp;
    private CancellationTokenSource _ct;
    
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _oidToRid = new();
    private readonly ConcurrentDictionary<TcpClient, string> _tcpToOid = new();
    
    public async Task StartTcpAsync(string host = "0.0.0.0", int port = 9998) {
        _tcp = new TcpListener(IPAddress.Parse(host), port);
        _tcp.Start();
        _ct = new CancellationTokenSource();
        
        Console.WriteLine($"TCP server listening on {host}:{port}");

        while (!_ct.Token.IsCancellationRequested) {
            try {
                var tcpClient = await _tcp.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleTcpClient(tcpClient));
            }
            catch (ObjectDisposedException) {
                break;
            }
        }
    }

    private async Task HandleTcpClient(TcpClient client) {
        var buff = new byte[4096];
        var msgBuff = new List<byte>();

        try {
            var stream = client.GetStream();

            while (client.Connected && !_ct.Token.IsCancellationRequested) {
                var bytes = await stream.ReadAsync(buff, 0, buff.Length);
                if (bytes == 0) {
                    DisconnectClient(client);
                    break;
                }

                msgBuff.AddRange(buff.Take(bytes));

                while (msgBuff.Count >= 4) {
                    var msgLen = ByteUtils.UnpackU32(msgBuff.ToArray(), 0);

                    if (msgBuff.Count >= 4 + msgLen) {
                        var msgData = msgBuff.Skip(4).Take((int)msgLen).ToArray();
                        msgBuff.RemoveRange(0, 4 + (int)msgLen);

                        await HandleTcpMessage(msgData, client);
                    }
                    else {
                        break; // wait for more data
                    }
                }
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"TCP Client Error: {ex.Message}");
        }
        finally {
            client.Close();
        }
    }

    private void DisconnectClient(TcpClient client) {
        if (!_tcpToOid.TryGetValue(client, out var oid)) return;
        
        var room = GetRoomForPeer(oid);
        if (room == null) return;

        if (room.Id == oid) {
            Console.WriteLine($"Host {oid} disconnecting, closing room");

            var allOids = room.Clients.Keys.ToList();
            var clientsToClose = room.Clients.Values.Where(c => c != client).ToList();
            _rooms.TryRemove(room.Id, out _);

            foreach (var clientToClose in clientsToClose) {
                try {
                    clientToClose.Close();
                    _tcpToOid.TryRemove(clientToClose, out _);
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error closing client: {ex.Message}");
                }
            }
            
            PeersDisconnected.Invoke(allOids);
        }
        else {
            // disconnect myself
            room.RemovePeer(oid);
            _ = Task.Run(() => SendPeerList(room));
            PeerDisconnected?.Invoke(oid);
        }

        _tcpToOid.TryRemove(client, out _);
    }

    private async Task SendTcpMessage(TcpClient client, byte[] data) {
        try {
            var stream = client.GetStream();

            var lenBytes = ByteUtils.PackU32((uint)data.Length);
            await stream.WriteAsync(lenBytes, 0, lenBytes.Length);

            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }
        catch (Exception ex) {
            Console.WriteLine($"Error Sending TCP Message: {ex.Message}");
        }
    }
    
    private async Task HandleTcpMessage(byte[] data, TcpClient client) {
        Console.WriteLine("Received Message!");
        var pktType = (PacketType)ByteUtils.UnpackU32(data, 0);
        var payload = data[4..];

        switch (pktType) {
            case PacketType.Connect:
                Console.WriteLine("Received Connect Request");
                await HandleConnect(client);
                break;
            case PacketType.Host:
                Console.WriteLine("Received Host Request");
                await HandleHost(payload, client);
                break;
            case PacketType.Join:
                Console.WriteLine("Received Join Request");
                await HandleJoin(payload, client);
                break;
            case PacketType.PeerList:
                Console.WriteLine("Received Peer List Request");
                break;
            default:
                Console.WriteLine($"Unknown Packet Type: {pktType}");
                break;
        }
    }

    /**
     * Gets called whenever the client connects to the relay server
     * Sends the client their OID
     */
    private async Task HandleConnect(TcpClient client) {
        var oid = GenerateOid();
        _tcpToOid[client] = oid;
        
        Console.WriteLine($"OID Generated: {oid}");

        var msg = new List<byte>();
        msg.AddRange(ByteUtils.PackU32((uint)PacketType.Connect));
        msg.AddRange(ByteUtils.PackU32((uint)oid.Length));
        msg.AddRange(Encoding.UTF8.GetBytes(oid));
        
        await SendTcpMessage(client, msg.ToArray());
    }
    
    /**
     * Gets called whenever the client requests to host a room
     * Sends the client their RID and NID
     */
    private async Task HandleHost(byte[] data, TcpClient client) {
        var oidLen = ByteUtils.UnpackU32(data, 0);
        var oid = Encoding.UTF8.GetString(data, 4, (int)oidLen);
        var room = new Room(oid, client);
        _rooms[oid] = room;
        
        Console.WriteLine($"Created Room For Peer: {oid}");

        var msg = new List<byte>();
        msg.AddRange(ByteUtils.PackU32((uint)PacketType.Host));

        await SendTcpMessage(client, msg.ToArray());
        await SendPeerList(room);
    }
    
    /**
     * Gets called whenever the client requests to join a room
     * Sends the client their RID and NID
     */
    private async Task HandleJoin(byte[] data, TcpClient client) {
        var oidLen = (int)ByteUtils.UnpackU32(data, 0);
        var oid = Encoding.UTF8.GetString(data, 4, oidLen);

        var hostOidLen = (int)ByteUtils.UnpackU32(data, 4 + oidLen);
        var hostOid = Encoding.UTF8.GetString(data, 8 + oidLen, hostOidLen);

        if (_rooms.TryGetValue(hostOid, out var room)) {
            room.AddPeer(oid, client);
        }
        else {
            return;
        }

        var msg = new List<byte>();
        msg.AddRange(ByteUtils.PackU32((uint)PacketType.Join));

        await SendTcpMessage(client, msg.ToArray());
        await SendPeerList(room);
    }
    
    /**
     * Sends an updated peer list to all clients in room
     */
    private async Task SendPeerList(Room room) {
        var peers = room.GetPeers();

        var msg = new List<byte>();
        msg.AddRange(ByteUtils.PackU32((uint)PacketType.PeerList));
        msg.AddRange(ByteUtils.PackU32((uint)peers.Count));

        foreach (var (oid, nid) in peers) {
            msg.AddRange(ByteUtils.PackU32((uint)oid.Length));
            msg.AddRange(Encoding.UTF8.GetBytes(oid));
            msg.AddRange(ByteUtils.PackU32((uint)nid));
        }

        foreach (var tcp in room.Clients.Values) {
            await SendTcpMessage(tcp, msg.ToArray());
        }
    }

    /**
     * Gets the room that the given peer is in
     */
    public Room? GetRoomForPeer(string oid) {
        return _rooms.Values.FirstOrDefault(room => room.HasPeer(oid));
    }

    private string GenerateOid() {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rand = new Random();
        var oid = new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[rand.Next(s.Length)]).ToArray());

        while (_oidToRid.ContainsKey(oid) || _rooms.ContainsKey(oid)) {
            oid = new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[rand.Next(s.Length)]).ToArray());
        }

        return oid;
    }

    public int GetTotalRooms() => _rooms.Count;

    public int GetTotalPeers() => _rooms.Values.Sum(room => room.GetPeers().Count);
}