use std::io::Error;
use crate::tcp::tcp_server::TcpServer;

mod tcp;
mod client;
mod packet;
mod utils;

#[tokio::main]
async fn main() -> Result<(), Error> {
    let tcp_server = TcpServer::new("127.0.0.1".to_string(), 8080);
    tcp_server.start().await?;

    Ok(())
}
