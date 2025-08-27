use std::collections::HashMap;
// src/room/room.rs
use tokio::sync::mpsc;
use crate::packet::packet_builder::PacketBuilder;

pub struct Room {
    host_online_id: String,
    connected_peers: HashMap<String, u32>, // online_id -> numeric_id
    client_senders: HashMap<String, mpsc::UnboundedSender<Vec<u8>>>, // online_id -> sender
    next_numeric_id: u32,
}

impl Room {
    pub fn new(host_online_id: String) -> Self {
        Self {
            host_online_id: host_online_id.clone(),
            connected_peers: HashMap::new(),
            client_senders: HashMap::new(),
            next_numeric_id: 1,
        }
    }

    pub fn add_peer(&mut self, online_id: String, sender: mpsc::UnboundedSender<Vec<u8>>) -> u32 {
        let numeric_id = if online_id == self.host_online_id {
            1 // Host always gets ID 1
        } else {
            self.next_numeric_id += 1;
            self.next_numeric_id - 1
        };

        self.connected_peers.insert(online_id.clone(), numeric_id);
        self.client_senders.insert(online_id, sender);
        numeric_id
    }

    pub fn remove_peer(&mut self, online_id: &str) {
        self.connected_peers.remove(online_id);
        self.client_senders.remove(online_id);
    }

    pub fn has_peer(&self, online_id: &str) -> bool {
        self.connected_peers.contains_key(online_id)
    }

    pub fn get_peers(&self) -> &HashMap<String, u32> {
        &self.connected_peers
    }

    pub async fn broadcast_peer_list(&self) {
        let peer_list_packet = PacketBuilder::build_peer_list(&self.connected_peers);

        for sender in self.client_senders.values() {
            // If send fails, the client connection is probably dead
            // We could collect failed sends and clean them up
            let _ = sender.send(peer_list_packet.clone());
        }

        println!("Broadcasted peer list to {} clients", self.client_senders.len());
    }

    pub fn is_empty(&self) -> bool {
        self.connected_peers.is_empty()
    }
}