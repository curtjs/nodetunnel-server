use std::collections::HashSet;
use rand::distr::Alphanumeric;
use rand::Rng;
use tokio::sync::Mutex;

pub struct IdGenerator {
    used_ids: Mutex<HashSet<String>>,
}

impl IdGenerator {
    pub fn new() -> Self {
        Self {
            used_ids: Mutex::new(HashSet::new()),
        }
    }

    pub async fn generate(&self) -> String {
        loop {
            let mut id: String = rand::rng()
                .sample_iter(&Alphanumeric)
                .take(5)
                .map(char::from)
                .collect();
            id.make_ascii_uppercase();
            
            let mut used_ids = self.used_ids.lock().await;
            if !used_ids.contains(&id) {
                used_ids.insert(id.clone());
                return id;
            }
        }
    }
}
