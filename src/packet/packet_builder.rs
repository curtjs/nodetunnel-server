use crate::packet::packet_type::PacketType;
use crate::utils::byte_utils::ByteUtils;

pub struct PacketBuilder;

impl PacketBuilder {
    pub fn build_connect(online_id: &String) -> Vec<u8> {
        let mut packet = ByteUtils::pack_u32(PacketType::Connect as u32);
        packet.extend(ByteUtils::pack_str(online_id.as_str()));
        packet
    }
    
    pub fn build_connected_to_room(numeric_id: u32) -> Vec<u8> {
        let mut packet = ByteUtils::pack_u32(PacketType::ConnectedToRoom as u32);
        packet.extend(ByteUtils::pack_u32(numeric_id));
        packet
    }
}
