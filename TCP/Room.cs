using System.Net.Sockets;
using System.Text;

namespace NodeTunnel.TCP;

/**
 * Contains data about different rooms
 * Connected peers, host peer, etc.
 */
public class Room {
    public string Id { get; }
    public string HostOid { get; private set; }

    private readonly Dictionary<string, int> _oidToNid = new();
    public readonly Dictionary<string, TcpClient> Clients = new();
    private int _nextNid = 2; 

    public Room(string id, TcpClient hostClient) {
        Id = id;
        _oidToNid[id] = 1;
        
        Clients[id] = hostClient;
    }

    public int AddPeer(string oid, TcpClient client) {
        Clients[oid] = client;
        
        var nid = _nextNid++;
        _oidToNid[oid] = nid;
        
        Console.WriteLine($"Added Peer: {oid}({nid})");
        
        return nid;
    }

    public void RemovePeer(string oid) {
        _oidToNid.Remove(oid);
        Clients.Remove(oid);
    }

    public bool HasPeer(string oid) {
        return Clients.ContainsKey(oid);
    }

    public List<(string oid, int nid)> GetPeers() {
        var peers = new List<(string oid, int nid)>();
        
        foreach (var kvp in _oidToNid) {
            peers.Add((kvp.Key, kvp.Value));
        }

        return peers;
    }
}
