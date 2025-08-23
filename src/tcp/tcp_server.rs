use std::io::Error;
use std::sync::Arc;
use tokio::net::{TcpListener, TcpStream};
use crate::client::connection;
use crate::client::connection::ClientConnection;
use crate::client::id_generator::IdGenerator;

pub struct TcpServer {
    address: String,
    port: u16,
    id_gen: Arc<IdGenerator>
}

impl TcpServer {
    pub fn new(address: String, port: u16) -> Self {
        TcpServer {
            address,
            port,
            id_gen: Arc::new(IdGenerator::new())
        }
    }

    pub async fn start(&self) -> Result<(), Error> {
        let addr = format!("{}:{}", self.address, self.port);
        let listener = TcpListener::bind(&addr).await?;

        println!("TCP Started on {}", addr);

        loop {
            let (socket, addr) = listener.accept().await?;
            println!("New connection from: {}", addr);
            
            let id_gen = Arc::clone(&self.id_gen);
            
            tokio::spawn(async move {
                let mut client_conn = ClientConnection::new(socket);
                client_conn.gen_id(id_gen).await;
            });
        }
    }
}
