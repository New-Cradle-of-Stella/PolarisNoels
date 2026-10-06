use crate::channels::SEQUENCED;
use parking_lot::Mutex;
use std::collections::VecDeque;
use tokio::sync::Notify;

pub const CONNECTED: i32 = 1;
pub const DISCONNECTED: i32 = 2;
pub const MESSAGE: i32 = 3;
pub const BULK_PROGRESS: i32 = 4;
pub const NAT_STATE: i32 = 5;
pub const JOIN_RESULT: i32 = 6;
pub const LOG: i32 = 7;

#[repr(C)]
#[derive(Clone, Copy, Default, Debug)]
pub struct Header {
    pub kind: i32,
    pub peer: i32,
    pub channel: i32,
    pub code: i32,
    pub len: u32,
    pub aux0: u32,
    pub aux1: u32,
    pub aux2: u32,
}
#[derive(Debug)]
pub struct Event {
    pub header: Header,
    pub data: Vec<u8>,
}
impl Event {
    pub fn new(kind: i32, peer: i32, channel: i32, code: i32, data: Vec<u8>) -> Self {
        Self {
            header: Header {
                kind,
                peer,
                channel,
                code,
                len: data.len() as u32,
                ..Header::default()
            },
            data,
        }
    }
}
#[derive(Default)]
struct Queue {
    entries: VecDeque<Event>,
    bytes: usize,
}
#[derive(Default)]
pub struct Events {
    queue: Mutex<Queue>,
    space: Notify,
}
impl Events {
    pub async fn push(&self, event: Event) {
        loop {
            let wake = self.space.notified();
            tokio::pin!(wake);
            wake.as_mut().enable();
            {
                let mut q = self.queue.lock();
                // 单个合法 BULK 可达64MB；仅在空队列时允许它独占预算，其余事件保持背压。
                if q.entries.len() < 8192
                    && (q.entries.is_empty() || q.bytes + event.data.len() <= 32 * 1024 * 1024)
                {
                    q.bytes += event.data.len();
                    q.entries.push_back(event);
                    return;
                }
                if let Some(i) = q
                    .entries
                    .iter()
                    .position(|e| e.header.kind == MESSAGE && e.header.channel == SEQUENCED)
                {
                    let old = q.entries.remove(i).unwrap();
                    q.bytes -= old.data.len();
                    continue;
                }
                if event.header.kind == MESSAGE && event.header.channel == SEQUENCED {
                    return;
                }
            }
            wake.await;
        }
    }
    pub fn poll(&self, cap: usize) -> Result<Option<Event>, Header> {
        let mut q = self.queue.lock();
        match q.entries.front() {
            None => Ok(None),
            Some(e) if e.data.len() > cap => Err(e.header),
            Some(_) => {
                let e = q.entries.pop_front().unwrap();
                q.bytes -= e.data.len();
                self.space.notify_waiters();
                Ok(Some(e))
            }
        }
    }
    pub fn log(&self, level: i32, text: String) {
        let mut q = self.queue.lock();
        if q.entries.len() < 8192 && q.bytes + text.len() <= 32 * 1024 * 1024 {
            q.bytes += text.len();
            q.entries
                .push_back(Event::new(LOG, -1, 0, level, text.into_bytes()));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[tokio::test]
    async fn small_buffer_does_not_consume() {
        let q = Events::default();
        q.push(Event::new(MESSAGE, 2, 0, 0, vec![1; 100])).await;
        assert_eq!(q.poll(1).unwrap_err().len, 100);
        assert_eq!(q.poll(100).unwrap().unwrap().data.len(), 100);
        assert!(q.poll(100).unwrap().is_none());
    }
    #[tokio::test]
    async fn pressure_evicts_datagrams_and_wakes_reliable_waiters() {
        let q = std::sync::Arc::new(Events::default());
        for _ in 0..8191 {
            q.push(Event::new(MESSAGE, 1, 0, 0, vec![])).await;
        }
        q.push(Event::new(MESSAGE, 1, SEQUENCED, 0, vec![9])).await;
        q.push(Event::new(MESSAGE, 1, 0, 0, vec![8])).await;
        let next = q.clone();
        let waiter = tokio::spawn(async move {
            next.push(Event::new(MESSAGE, 1, 0, 0, vec![7])).await;
        });
        tokio::time::sleep(std::time::Duration::from_millis(5)).await;
        assert!(!waiter.is_finished());
        q.poll(1).unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(1), waiter)
            .await
            .unwrap()
            .unwrap();
        let mut tail = vec![];
        while let Some(ev) = q.poll(1).unwrap() {
            assert_ne!(ev.header.channel, SEQUENCED);
            if !ev.data.is_empty() {
                tail.extend(ev.data);
            }
        }
        assert_eq!(tail, vec![8, 7]);
    }
}
