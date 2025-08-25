use std::collections::HashMap;
use std::sync::Arc;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::sync::RwLock;
use crate::client::id_generator::IdGenerator;
use crate::packet::packet_builder::PacketBuilder;
use crate::packet::packet_type::PacketType;
use crate::room::room::Room;
use crate::utils::byte_utils::ByteUtils;

pub struct ClientConnection {
    tcp_stream: TcpStream,
    online_id: Option<String>,
    numeric_id: Option<u32>,
    rooms: Arc<RwLock<HashMap<String, Room>>>
}

impl ClientConnection {
    pub fn new(tcp_stream: TcpStream, rooms: Arc<RwLock<HashMap<String, Room>>>) -> Self {
        Self {
            tcp_stream,
            rooms,
            online_id: None,
            numeric_id: None,
        }
    }
    
    pub async fn handle_client(&mut self, id_gen: Arc<IdGenerator>) {
        loop {
            match self.read_packet().await { 
                Ok(packet) => {
                    if let Err(e) = self.handle_packet(packet, &id_gen).await {
                        println!("Error handling packet: {}", e);
                        break;
                    }
                }
                Err(e) => {
                    println!("Error reading packet: {}", e);
                    break;
                }
            }
        }
    }
    
    pub async fn read_packet(&mut self) -> Result<Vec<u8>, String> {
        let mut len_bytes = [0u8; 4];
        self.tcp_stream.read_exact(&mut len_bytes).await
            .map_err(|e| format!("Failed to read length: {}", e))?;
        
        let len = ByteUtils::unpack_u32(&len_bytes, 0)
            .ok_or("Invalid length header")? as usize;
        
        let mut packet = vec![0u8; len];
        self.tcp_stream.read_exact(&mut packet).await
            .map_err(|e| format!("Failed to read packet: {}", e))?;
        
        Ok(packet)
    }
    
    pub async fn handle_packet(&mut self, data: Vec<u8>, id_gen: &IdGenerator) -> Result<(), String> {
        let packet_type_u32 = ByteUtils::unpack_u32(&data, 0)
            .ok_or("Missing packet type")?;
        
        let packet_type = PacketType::from_u32(packet_type_u32)
            .ok_or("Unknown packet type")?;
        
        match packet_type {
            PacketType::Connect => self.handle_connect(id_gen).await,
            PacketType::Host => self.handle_host().await,
            PacketType::Join => self.handle_join(&data).await?,
            _ => {}
        }
        
        Ok(())
    }

    pub async fn handle_connect(&mut self, id_gen: &IdGenerator) {
        let online_id = id_gen.generate().await;
        let packet = PacketBuilder::build_connect(&online_id);
        self.online_id = Some(online_id);

        match self.send_packet(packet).await {
            Ok(_) => println!("Sent packet!"),
            Err(e) => println!("Failed to send packet: {}", e)
        }
    }

    pub async fn handle_host(&mut self) {
        if let Some(online_id) = &self.online_id {
            let numeric_id = {
                let mut rooms = self.rooms.write().await;

                if rooms.contains_key(online_id) {
                    println!("Already hosting a room!");
                    return;
                }

                let mut room = Room::new(online_id.clone());
                let numeric_id = room.add_peer(online_id.clone());
                rooms.insert(online_id.clone(), room);

                numeric_id
            };

            self.numeric_id = Some(numeric_id);

            let packet = PacketBuilder::build_connected_to_room(numeric_id);

            match self.send_packet(packet).await {
                Ok(_) => println!("Peer connected to room!"),
                Err(e) => println!("Failed to send packet: {}", e)
            }
        } else {
            println!("Attempted to host without an online ID!")
        }
    }

    pub async fn handle_join(&mut self, data: &[u8]) -> Result<(), String> {
        let offset = 4;
        
        let (host_id, _) = ByteUtils::unpack_str(data, offset)
            .ok_or("Failed to parse host ID")?;
        
        let joiner_id = self.online_id.as_ref()
            .ok_or("No online ID set")?
            .clone();

        {
            let mut rooms = self.rooms.write().await;

            if let Some(room) = rooms.get_mut(&host_id) {
                self.numeric_id = Some(room.add_peer(joiner_id));
            } else {
                return Err("Room not found".to_string());
            }
        }

        let packet = PacketBuilder::build_connected_to_room(
            self.numeric_id.ok_or("No numeric ID set")?
        );

        match self.send_packet(packet).await {
            Ok(_) => println!("Peer connected to room!"),
            Err(e) => println!("Failed to send packet: {}", e)
        }
        
        Ok(())
    }

    async fn send_packet(&mut self, packet: Vec<u8>) -> Result<(), String> {
        let packet_len = ByteUtils::pack_u32(packet.len() as u32);

        self.tcp_stream.write_all(&packet_len).await
            .map_err(|e| format!("Failed to send length: {}", e))?;
        self.tcp_stream.write_all(&packet).await
            .map_err(|e| format!("Failed to send packet: {}", e))?;

        Ok(())
    }
}
