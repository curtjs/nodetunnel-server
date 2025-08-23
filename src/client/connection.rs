use std::sync::Arc;
use tokio::io::AsyncWriteExt;
use tokio::net::TcpStream;
use crate::client::id_generator::IdGenerator;
use crate::packet::packet_builder::PacketBuilder;
use crate::utils::byte_utils::ByteUtils;

pub struct ClientConnection {
    tcp_stream: TcpStream,
    online_id: Option<String>,
    hosting: bool
}

impl ClientConnection {
    pub fn new(tcp_stream: TcpStream) -> Self {
        Self {
            tcp_stream,
            online_id: None,
            hosting: false
        }
    }

    pub async fn gen_id(&mut self, id_gen: Arc<IdGenerator>) {
        let online_id = id_gen.generate().await;
        let packet = PacketBuilder::build_online_id(&online_id);
        self.online_id = Some(online_id);
        
        match self.send_packet(packet).await { 
            Ok(_) => println!("Sent packet!"),
            Err(e) => println!("Failed to send packet: {}", e)
        }
    }
    
    pub fn host(&mut self) {
        self.hosting = true;
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
