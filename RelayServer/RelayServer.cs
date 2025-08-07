using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// UDP Relay Server with online ID system for direct peer connections
/// </summary>
public class RelayServer
{
    private readonly string _host;
    private readonly int _port;
    private UdpClient _udpClient;
    private CancellationTokenSource _cancellationToken;
    
    // Dictionary to store client connections with their online IDs and numeric IDs
    private readonly ConcurrentDictionary<string, (IPEndPoint ip, DateTime lastSeen, bool isHost, int numericId)> _clients = new();
    // Dictionary to store host connections - maps host online ID to list of connected peer online IDs
    private readonly ConcurrentDictionary<string, List<string>> _hostConnections = new();
    // Track next available numeric ID
    private int _nextNumericId = 2; // Start at 2, host gets 1
    
    private readonly object _connectionLock = new();

    // Packet types for the online ID system
    private enum PacketType
    {
        Connect = 1,
        ConnectResponse = 2,
        Host = 3,
        HostResponse = 4,
        Join = 5,
        JoinResponse = 6,
        RelayData = 7,
        Heartbeat = 8,
        PeerDisconnected = 9
    }

    public RelayServer(string host = "0.0.0.0", int port = 9999)
    {
        _host = host;
        _port = port;
        Console.WriteLine($"NodeTunnel initializing on {host}:{port}");
    }
    
    public async Task StartAsync()
    {
        try
        {
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(_host), _port));
            _cancellationToken = new CancellationTokenSource();

            Console.WriteLine($"NodeTunnel listening on {_host}:{_port}");
            Console.WriteLine("Waiting for connections...");

            // Start cleanup task
            _ = Task.Run(CleanupDisconnectedClientsAsync);
            
            await HandlePacketsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server error: {ex.Message}");
        }
        finally
        {
            Stop();
        }
    }
    
    private async Task HandlePacketsAsync()
    {
        while (!_cancellationToken.Token.IsCancellationRequested)
        {
            try
            {
                var res = await _udpClient.ReceiveAsync();
                _ = Task.Run(() => HandlePacket(res.Buffer, res.RemoteEndPoint));
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving packet: {ex.Message}");
            }
        }
    }
    
    private void HandlePacket(byte[] data, IPEndPoint remoteIp)
    {
        try
        {
            if (data.Length < 4) return;

            var packetType = (PacketType)ReadUInt32BE(data, 0);
            var payload = data[4..];

            switch (packetType)
            {
                case PacketType.Connect:
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client connecting from {remoteIp}...");
                    HandleConnect(remoteIp);
                    break;
                case PacketType.Host:
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client hosting...");
                    HandleHost(payload, remoteIp);
                    break;
                case PacketType.Join:
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client joining...");
                    HandleJoin(payload, remoteIp);
                    break;
                case PacketType.RelayData:
                    HandleRelayData(payload, remoteIp);
                    break;
                case PacketType.Heartbeat:
                    HandleHeartbeat(payload, remoteIp);
                    break;
                default:
                    Console.WriteLine($"Unknown packet of type: {packetType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling packet from {remoteIp}: {ex.Message}");
        }
    }

    private void HandleConnect(IPEndPoint remoteIp) {
        // Generate a unique online ID
        var onlineId = GenerateOnlineId();
        var numericId = Interlocked.Increment(ref _nextNumericId) - 1;
        _clients[onlineId] = (remoteIp, DateTime.Now, false, numericId);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client connected with online ID: {onlineId}, numeric ID: {numericId} from {remoteIp}");
        
        // Send response with both online ID and numeric ID
        var res = new List<byte>();
        res.AddRange(WriteUInt32BE((uint)PacketType.ConnectResponse));
        res.AddRange(WriteUInt32BE((uint)onlineId.Length));
        res.AddRange(Encoding.UTF8.GetBytes(onlineId));
        res.AddRange(WriteUInt32BE((uint)numericId));
        
        SendToClient(res.ToArray(), remoteIp);
    }

    private void HandleHost(byte[] data, IPEndPoint remoteIp)
    {
        var onlineId = Encoding.UTF8.GetString(data);
        
        if (!_clients.ContainsKey(onlineId)) return;

        var (ip, lastSeen, _, currentNumericId) = _clients[onlineId];
        // Host always gets numeric ID 1
        _clients[onlineId] = (remoteIp, DateTime.Now, true, 1);

        lock (_connectionLock)
        {
            if (!_hostConnections.ContainsKey(onlineId))
            {
                _hostConnections[onlineId] = new List<string>();
            }
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {onlineId} is now hosting with numeric ID 1");
        
        // Send host response with numeric ID 1
        var res = new List<byte>();
        res.AddRange(WriteUInt32BE((uint)PacketType.HostResponse));
        res.AddRange(WriteUInt32BE(1)); // Success
        res.AddRange(WriteUInt32BE(1)); // Numeric ID (host is always 1)
        
        SendToClient(res.ToArray(), remoteIp);
    }

    private void HandleJoin(byte[] data, IPEndPoint remoteIp)
    {
        if (data.Length < 4) return;

        var joinerIdLength = (int)ReadUInt32BE(data, 0);
        if (data.Length < 4 + joinerIdLength + 4) return;

        var joinerId = Encoding.UTF8.GetString(data, 4, joinerIdLength);
        var hostIdLength = (int)ReadUInt32BE(data, 4 + joinerIdLength);
        if (data.Length < 4 + joinerIdLength + 4 + hostIdLength) return;

        var hostId = Encoding.UTF8.GetString(data, 4 + joinerIdLength + 4, hostIdLength);

        if (!_clients.ContainsKey(joinerId) || !_clients.ContainsKey(hostId)) 
        {
            SendJoinResponse(remoteIp, 0, 0); // Failed
            return;
        }

        var (joinerIp, joinerLastSeen, joinerIsHost, joinerNumericId) = _clients[joinerId];
        _clients[joinerId] = (remoteIp, DateTime.Now, joinerIsHost, joinerNumericId);

        var (hostIp, hostLastSeen, hostIsHost, hostNumericId) = _clients[hostId];
        if (!hostIsHost)
        {
            SendJoinResponse(remoteIp, 0, 0); // Host is not hosting
            return;
        }

        List<string> existingPeers;
        lock (_connectionLock)
        {
            if (!_hostConnections.ContainsKey(hostId))
            {
                _hostConnections[hostId] = new List<string>();
            }

            // Get existing peers before adding the new joiner
            existingPeers = new List<string>(_hostConnections[hostId]);

            if (!_hostConnections[hostId].Contains(joinerId))
            {
                _hostConnections[hostId].Add(joinerId);
            }
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {joinerId} (numeric: {joinerNumericId}) joined host {hostId} (numeric: {hostNumericId})");
        
        // Send success response to joiner with their numeric ID
        SendJoinResponse(remoteIp, 1, joinerNumericId);
        
        // Small delay to ensure joiner processes the response
        Task.Delay(50).ContinueWith(_ => {
            // Notify joiner about host first (host is always numeric ID 1)
            NotifyPeerConnection(joinerId, hostId, true);
            
            // Then notify joiner about all existing peers
            foreach (var existingPeer in existingPeers)
            {
                NotifyPeerConnection(joinerId, existingPeer, true);
            }
            
            // Finally notify host and existing peers about the new joiner
            NotifyPeerConnection(hostId, joinerId, true);
            foreach (var existingPeer in existingPeers)
            {
                NotifyPeerConnection(existingPeer, joinerId, true);
            }
        });
    }

    private void SendJoinResponse(IPEndPoint remoteIp, uint success, int numericId)
    {
        var res = new List<byte>();
        res.AddRange(WriteUInt32BE((uint)PacketType.JoinResponse));
        res.AddRange(WriteUInt32BE(success));
        res.AddRange(WriteUInt32BE((uint)numericId));
        
        SendToClient(res.ToArray(), remoteIp);
    }

    private void NotifyPeerConnection(string targetId, string peerId, bool connected)
    {
        if (!_clients.TryGetValue(targetId, out var target)) return;
        if (!_clients.TryGetValue(peerId, out var peer)) return;

        var notification = new List<byte>();
        notification.AddRange(WriteUInt32BE((uint)PacketType.PeerDisconnected));
        notification.AddRange(WriteUInt32BE((uint)peerId.Length));
        notification.AddRange(Encoding.UTF8.GetBytes(peerId));
        notification.AddRange(WriteUInt32BE((uint)peer.numericId)); // Include numeric ID
        notification.AddRange(WriteUInt32BE(connected ? 1u : 0u));

        SendToClient(notification.ToArray(), target.ip);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Notified {targetId} about {peerId} (numeric: {peer.numericId}) {(connected ? "connected" : "disconnected")}");
    }

    private void HandleRelayData(byte[] data, IPEndPoint remoteIp)
    {
        if (data.Length < 8) return;

        var fromIdLength = (int)ReadUInt32BE(data, 0);
        if (data.Length < 4 + fromIdLength + 4) return;

        var fromId = Encoding.UTF8.GetString(data, 4, fromIdLength);
        var toIdLength = (int)ReadUInt32BE(data, 4 + fromIdLength);
        
        if (data.Length < 4 + fromIdLength + 4 + toIdLength) return;

        var toId = Encoding.UTF8.GetString(data, 4 + fromIdLength + 4, toIdLength);
        var payload = data[(4 + fromIdLength + 4 + toIdLength)..];

        if (!_clients.ContainsKey(fromId)) return;

        var (_, _, isHost, numericId) = _clients[fromId];
        _clients[fromId] = (remoteIp, DateTime.Now, isHost, numericId);
        
        if (toId == "0") // Broadcast
        {
            // Find all peers connected to this client (either as host or as peers of the same host)
            List<string> targetPeers = new();
            
            lock (_connectionLock)
            {
                // If this client is a host, broadcast to all connected peers
                if (_hostConnections.ContainsKey(fromId))
                {
                    targetPeers.AddRange(_hostConnections[fromId]);
                }
                else
                {
                    // If this client is a peer, find the host and broadcast to host + all other peers
                    foreach (var kvp in _hostConnections)
                    {
                        if (kvp.Value.Contains(fromId))
                        {
                            targetPeers.Add(kvp.Key); // Add host
                            targetPeers.AddRange(kvp.Value.Where(p => p != fromId)); // Add other peers
                            break;
                        }
                    }
                }
            }

            // Send to all target peers
            foreach (var targetId in targetPeers)
            {
                if (_clients.TryGetValue(targetId, out var target))
                {
                    SendRelayPacket(target.ip, fromId, targetId, payload);
                }
            }
        }
        else
        {
            // Send to specific client
            if (_clients.TryGetValue(toId, out var target))
            {
                SendRelayPacket(target.ip, fromId, toId, payload);
            }
        }
    }

    private void SendRelayPacket(IPEndPoint targetIp, string fromId, string toId, byte[] payload)
    {
        var relayPacket = new List<byte>();
        relayPacket.AddRange(WriteUInt32BE((uint)PacketType.RelayData));
        relayPacket.AddRange(WriteUInt32BE((uint)fromId.Length));
        relayPacket.AddRange(Encoding.UTF8.GetBytes(fromId));
        relayPacket.AddRange(WriteUInt32BE((uint)toId.Length));
        relayPacket.AddRange(Encoding.UTF8.GetBytes(toId));
        relayPacket.AddRange(payload);
        
        SendToClient(relayPacket.ToArray(), targetIp);
    }

    private void HandleHeartbeat(byte[] data, IPEndPoint remoteIp)
    {
        var onlineId = Encoding.UTF8.GetString(data);
        
        if (!_clients.ContainsKey(onlineId)) return;
        
        var (ip, lastSeen, isHost, numericId) = _clients[onlineId];
        _clients[onlineId] = (remoteIp, DateTime.Now, isHost, numericId);
        
        var res = new List<byte>();
        res.AddRange(WriteUInt32BE((uint)PacketType.Heartbeat));
        res.AddRange(Encoding.UTF8.GetBytes(onlineId));
        
        SendToClient(res.ToArray(), remoteIp);
    }

    private async Task CleanupDisconnectedClientsAsync()
    {
        while (!_cancellationToken.Token.IsCancellationRequested)
        {
            try
            {
                var currentTime = DateTime.Now;
                var disconnectedClients = new List<string>();
                
                foreach (var kvp in _clients)
                {
                    if ((currentTime - kvp.Value.lastSeen).TotalSeconds > 30)
                    {
                        disconnectedClients.Add(kvp.Key);
                    }
                }
                
                foreach (var clientId in disconnectedClients)
                {
                    RemoveClient(clientId);
                }
                
                await Task.Delay(10000, _cancellationToken.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in cleanup task: {ex.Message}");
            }
        }
    }
    
    private void RemoveClient(string onlineId)
    {
        if (!_clients.TryRemove(onlineId, out var client)) return;
        
        Console.WriteLine($"Client {onlineId} disconnected");
        
        lock (_connectionLock)
        {
            // If this client was a host, notify all connected peers and remove the host connection
            if (_hostConnections.ContainsKey(onlineId))
            {
                var connectedPeers = _hostConnections[onlineId];
                foreach (var peerId in connectedPeers)
                {
                    NotifyPeerConnection(peerId, onlineId, false);
                }
                _hostConnections.TryRemove(onlineId, out _);
            }
            
            // If this client was connected to a host, remove from that host's connection list
            foreach (var kvp in _hostConnections)
            {
                if (kvp.Value.Contains(onlineId))
                {
                    kvp.Value.Remove(onlineId);
                    
                    // Notify host and all other peers about disconnection
                    NotifyPeerConnection(kvp.Key, onlineId, false);
                    foreach (var peerId in kvp.Value)
                    {
                        NotifyPeerConnection(peerId, onlineId, false);
                    }
                    break;
                }
            }
        }
    }

    private void SendToClient(byte[] data, IPEndPoint remoteIp)
    {
        try
        {
            _udpClient.Send(data, data.Length, remoteIp);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending data to {remoteIp}: {ex.Message}");
        }
    }
    
    public void Stop()
    {
        Console.WriteLine("Shutting down NodeTunnel...");
        _cancellationToken?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
    }

    private string GenerateOnlineId()
    {
        // Generate a 8-character alphanumeric ID
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        var id = new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        
        // Ensure uniqueness
        while (_clients.ContainsKey(id))
        {
            id = new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
        
        return id;
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }
    
    private static byte[] WriteUInt32BE(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }
}