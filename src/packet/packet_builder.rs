use crate::packet::packet_type::PacketType;
use crate::utils::byte_utils::ByteUtils;

pub struct PacketBuilder;

impl PacketBuilder {
    pub fn build_online_id(online_id: &String) -> Vec<u8> {
        let mut packet = ByteUtils::pack_u32(PacketType::OnlineId as u32);
        packet.extend(ByteUtils::pack_str(online_id.as_str()));
        packet
    }
}
