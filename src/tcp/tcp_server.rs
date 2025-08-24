use std::collections::HashMap;
use std::io::Error;
use std::sync::Arc;
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::RwLock;
use crate::client::connection;
use crate::client::connection::ClientConnection;
use crate::client::id_generator::IdGenerator;
use crate::room::room::Room;

pub struct TcpServer {
    address: String,
    port: u16,
    id_gen: Arc<IdGenerator>,
    rooms: Arc<RwLock<HashMap<String, Room>>>,
}

impl TcpServer {
    pub fn new(address: String, port: u16) -> Self {
        TcpServer {
            address,
            port,
            id_gen: Arc::new(IdGenerator::new()),
            rooms: Arc::new(RwLock::new(HashMap::new())),
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
            let rooms = Arc::clone(&self.rooms);
            
            tokio::spawn(async move {
                let mut client_conn = ClientConnection::new(socket, rooms);
                client_conn.handle_client(id_gen).await;
            });
        }
    }
}
