use std::collections::HashMap;

pub struct Room {
    host_online_id: String,
    connected_peers: HashMap<String, u32>,
    next_numeric_id: u32,
}

impl Room {
    pub fn new(host_online_id: String) -> Self {
        Self {
            host_online_id,
            connected_peers: HashMap::new(),
            next_numeric_id: 1,
        }
    }

    pub fn add_peer(&mut self, online_id: String) -> u32 {
        let numeric_id = self.next_numeric_id;
        self.connected_peers.insert(online_id, numeric_id);

        self.next_numeric_id += 1;
        numeric_id
    }
}
