use crate::{
    channels::*,
    event::{self, Event, Events},
    nat::{punch, socket::PunchSocket, stun},
    quic::{self, Gate, Identity},
    room_code::Room,
};
use bytes::Bytes;
use hmac::{Hmac, Mac};
use parking_lot::{Mutex, RwLock};
use serde::{Deserialize, Serialize};
use sha2::Sha256;
use std::{
    collections::{HashMap, VecDeque},
    future::Future,
    net::SocketAddr,
    pin::Pin,
    sync::{
        atomic::{AtomicBool, AtomicI32, AtomicU32, Ordering},
        Arc,
    },
    time::{Duration, Instant},
};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    sync::{Notify, Semaphore},
    time::timeout,
};

pub const LOCAL: i32 = 0;
pub const REMOTE: i32 = 1;
pub const KICKED: i32 = 2;
pub const TIMEOUT: i32 = 3;
pub const PROTOCOL: i32 = 4;
pub const ROOM_FULL: i32 = 5;
pub const HOST_CLOSED: i32 = 6;
pub const ROOM_INFO: u8 = 0x25;
pub const DEFAULT_STUN: &str =
    "stun.l.google.com:19302,stun1.l.google.com:19302,stun.cloudflare.com:3478";
type DialFuture = Pin<Box<dyn Future<Output = Result<(), (i32, String)>> + Send>>;

#[derive(Clone, Debug)]
pub struct Config {
    pub bind_port: u16,
    pub max_peers: u16,
    pub enable_stun: bool,
    pub enable_lan: bool,
    pub stun_servers: String,
    pub idle_timeout_ms: u32,
}
impl Default for Config {
    fn default() -> Self {
        Self {
            bind_port: 0,
            max_peers: 5,
            enable_stun: true,
            enable_lan: true,
            stun_servers: DEFAULT_STUN.into(),
            idle_timeout_ms: 20000,
        }
    }
}
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Member {
    pub id: i32,
    pub fingerprint: [u8; 32],
    pub candidates: Vec<SocketAddr>,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
struct PunchRequest {
    from: i32,
    to: i32,
    candidates: Vec<SocketAddr>,
}

enum Outgoing {
    Frame(u8, Bytes),
    Bulk(u32, Bytes),
}
impl Outgoing {
    fn size(&self) -> usize {
        match self {
            Self::Frame(_, b) | Self::Bulk(_, b) => b.len(),
        }
    }
}
#[derive(Default)]
struct SendQueue {
    items: VecDeque<Outgoing>,
    bytes: usize,
    count: usize,
}
pub struct Peer {
    id: i32,
    pub connection: quinn::Connection,
    queue: Mutex<SendQueue>,
    wake: Notify,
    seq: AtomicU32,
    bulk_id: AtomicU32,
    pub forced_reason: AtomicI32,
}
impl Peer {
    pub fn queued_bytes(&self) -> u32 {
        self.queue.lock().bytes as u32
    }
    fn enqueue(&self, item: Outgoing) -> Result<(), i32> {
        let mut q = self.queue.lock();
        let size = item.size();
        if q.count >= 4096 || (q.count > 0 && q.bytes + size > 8 * 1024 * 1024) {
            return Err(-5);
        }
        q.bytes += size;
        q.count += 1;
        q.items.push_back(item);
        self.wake.notify_one();
        Ok(())
    }
    fn control(&self, kind: u8, data: Vec<u8>) -> Result<(), i32> {
        self.enqueue(Outgoing::Frame(kind, Bytes::from(data)))
    }
    fn completed(&self, size: usize) {
        let mut q = self.queue.lock();
        q.bytes -= size;
        q.count -= 1;
    }
}

pub struct State {
    pub config: Config,
    pub endpoint: quinn::Endpoint,
    pub identity: Identity,
    pub gate: Arc<Gate>,
    pub socket: Arc<PunchSocket>,
    pub local_id: Arc<AtomicI32>,
    pub phase: AtomicU32,
    pub closing: AtomicBool,
    pub events: Arc<Events>,
    pub peers: RwLock<HashMap<i32, Arc<Peer>>>,
    members: RwLock<HashMap<i32, Member>>,
    pub room: Mutex<Option<Room>>,
    pub code: Mutex<Option<String>>,
    candidates: Mutex<Vec<SocketAddr>>,
    admission: tokio::sync::Mutex<()>,
    next_id: AtomicI32,
    connecting: Mutex<HashMap<i32, bool>>,
    bulk_memory: Arc<Semaphore>,
}
impl State {
    pub fn new(
        config: Config,
        endpoint: quinn::Endpoint,
        identity: Identity,
        gate: Arc<Gate>,
        socket: Arc<PunchSocket>,
        local_id: Arc<AtomicI32>,
    ) -> Arc<Self> {
        Arc::new(Self {
            config,
            endpoint,
            identity,
            gate,
            socket,
            local_id,
            phase: AtomicU32::new(0),
            closing: AtomicBool::new(false),
            events: Arc::new(Events::default()),
            peers: RwLock::new(HashMap::new()),
            members: RwLock::new(HashMap::new()),
            room: Mutex::new(None),
            code: Mutex::new(None),
            candidates: Mutex::new(vec![]),
            admission: tokio::sync::Mutex::new(()),
            next_id: AtomicI32::new(1),
            connecting: Mutex::new(HashMap::new()),
            bulk_memory: Arc::new(Semaphore::new(MAX_BULK)),
        })
    }
    pub async fn accept_loop(self: Arc<Self>) {
        while let Some(incoming) = self.endpoint.accept().await {
            let state = self.clone();
            tokio::spawn(async move {
                let result = timeout(Duration::from_secs(10), async {
                    let conn = incoming.await.map_err(|e| e.to_string())?;
                    let guard = conn.clone();
                    let result = state.clone().accept_peer(conn).await;
                    if result.is_err() {
                        guard.close((PROTOCOL as u32).into(), b"admission failed");
                    }
                    result
                })
                .await;
                if let Err(e) = result.unwrap_or_else(|_| Err("admission timeout".into())) {
                    state
                        .events
                        .log(2, format!("incoming connection rejected: {e}"));
                }
            });
        }
    }
    async fn collect(&self) -> stun::Candidates {
        let found = stun::collect(
            self.socket.clone(),
            self.config.enable_lan,
            self.config.enable_stun,
            &self.config.stun_servers,
        )
        .await;
        *self.candidates.lock() = found.addresses.clone();
        found
    }
    pub async fn start_host(self: Arc<Self>) {
        self.local_id.store(0, Ordering::Release);
        self.gate.hosting.store(true, Ordering::Release);
        let found = self.collect().await;
        let room = Room::new(
            self.identity.fingerprint,
            found.addresses.clone(),
            found.nat == "symmetric",
        );
        *self.socket.secret.lock() = Some(room.secret);
        *self.code.lock() = Some(room.encode());
        *self.room.lock() = Some(room);
        self.members.write().insert(0, self.my_member());
        self.nat_event(found, true).await;
    }
    async fn nat_event(&self, found: stun::Candidates, host: bool) {
        let payload = serde_json::json!({"nat":found.nat,"candidates":found.addresses,"stun_rtt_ms":found.rtt,"room_code":if host {self.code.lock().clone()}else{None}});
        self.events
            .push(Event::new(
                event::NAT_STATE,
                -1,
                0,
                0,
                payload.to_string().into_bytes(),
            ))
            .await;
    }
    fn my_member(&self) -> Member {
        Member {
            id: self.local_id.load(Ordering::Acquire),
            fingerprint: self.identity.fingerprint,
            candidates: self.candidates.lock().clone(),
        }
    }
    fn install(&self, member: Member) -> Result<(), String> {
        if member.id < 0 || member.candidates.is_empty() || member.candidates.len() > 8 {
            return Err("invalid membership descriptor".into());
        }
        if let Some(old) = self.members.read().get(&member.id) {
            if old.fingerprint != member.fingerprint {
                return Err("member identity changed".into());
            }
        }
        self.gate.known.write().insert(member.fingerprint);
        self.socket.trusted_peers.lock().insert(member.id);
        self.members.write().insert(member.id, member);
        Ok(())
    }
    fn secret(&self) -> Result<[u8; 16], String> {
        self.room
            .lock()
            .as_ref()
            .map(|r| r.secret)
            .ok_or_else(|| "room not ready".into())
    }

    pub async fn join_room(self: Arc<Self>, room: Room) {
        let found = self.collect().await;
        self.nat_event(found, false).await;
        *self.socket.secret.lock() = Some(room.secret);
        *self.room.lock() = Some(room.clone());
        let member = Member {
            id: 0,
            fingerprint: room.fingerprint,
            candidates: room.candidates,
        };
        if let Err(error) = self.install(member.clone()) {
            self.join_failed(1, error).await;
            return;
        }
        match self.clone().dial(member, true, true).await {
            Ok(()) => self.complete_join().await,
            Err((code, error)) => self.join_failed(code, error).await,
        }
    }
    pub async fn join_direct(self: Arc<Self>, address: SocketAddr, expected: Option<[u8; 32]>) {
        let found = self.collect().await;
        self.nat_event(found, false).await;
        if expected.is_none() {
            self.events.log(
                2,
                "direct join: trust-on-first-use certificate; use a room code to pin identity"
                    .into(),
            );
        }
        let member = Member {
            id: 0,
            fingerprint: expected.unwrap_or([0; 32]),
            candidates: vec![address],
        };
        match self.clone().dial(member, true, false).await {
            Ok(()) => self.complete_join().await,
            Err((code, error)) => self.join_failed(code, error).await,
        }
    }
    async fn complete_join(self: Arc<Self>) {
        let id = self.local_id.load(Ordering::Acquire);
        let targets: Vec<Member> = self
            .members
            .read()
            .values()
            .filter(|m| m.id != id && m.id != 0)
            .cloned()
            .collect();
        let mut tasks = Vec::new();
        for member in targets {
            let state = self.clone();
            tasks.push(tokio::spawn(async move {
                if id > member.id {
                    state.connect_member(member).await
                } else {
                    state.wait_peer(member.id).await
                }
            }));
        }
        for task in tasks {
            match task.await {
                Ok(Ok(())) => {}
                Ok(Err(error)) => {
                    self.join_failed(2, error).await;
                    return;
                }
                Err(error) => {
                    self.join_failed(3, error.to_string()).await;
                    return;
                }
            }
        }
        if self.closing.load(Ordering::Acquire) || !self.peers.read().contains_key(&0) {
            self.join_failed(3, "host disconnected during join".into())
                .await;
            return;
        }
        self.phase.store(3, Ordering::Release);
        self.events
            .push(Event::new(event::JOIN_RESULT, 0, 0, 0, vec![]))
            .await;
    }
    async fn join_failed(&self, code: i32, error: String) {
        self.events.log(2, format!("join failed ({code}): {error}"));
        self.events
            .push(Event::new(event::JOIN_RESULT, 0, 0, code, vec![]))
            .await;
        let peers: Vec<_> = self.peers.read().values().cloned().collect();
        for peer in peers {
            peer.connection
                .close((PROTOCOL as u32).into(), b"join failed");
        }
    }
    async fn wait_peer(&self, id: i32) -> Result<(), String> {
        timeout(Duration::from_secs(15), async {
            loop {
                if self.peers.read().contains_key(&id) {
                    return Ok(());
                }
                if self.closing.load(Ordering::Acquire) {
                    return Err("closing".into());
                }
                tokio::time::sleep(Duration::from_millis(20)).await;
            }
        })
        .await
        .map_err(|_| format!("mesh peer {id} timed out"))?
    }
    async fn connect_member(self: Arc<Self>, member: Member) -> Result<(), String> {
        if self.peers.read().contains_key(&member.id) {
            return Ok(());
        }
        let started = {
            let mut pending = self.connecting.lock();
            if let std::collections::hash_map::Entry::Vacant(entry) = pending.entry(member.id) {
                entry.insert(true);
                true
            } else {
                false
            }
        };
        if !started {
            return self.wait_peer(member.id).await;
        }
        let result = self
            .clone()
            .dial(member.clone(), false, true)
            .await
            .map_err(|(_, e)| e);
        self.connecting.lock().remove(&member.id);
        result
    }
    // 显式装箱切断入房、控制帧和接收任务之间的异步类型递归。
    fn dial(self: Arc<Self>, member: Member, host: bool, use_punch: bool) -> DialFuture {
        Box::pin(async move {
            let address = if use_punch {
                if !host {
                    let request = PunchRequest {
                        from: self.local_id.load(Ordering::Acquire),
                        to: member.id,
                        candidates: self.candidates.lock().clone(),
                    };
                    let master = self.peers.read().get(&0).cloned();
                    if let Some(master) = master {
                        master
                            .control(PUNCH, serde_json::to_vec(&request).unwrap())
                            .map_err(|_| (3, "control send queue full".into()))?;
                    }
                }
                punch::punch(
                    self.socket.clone(),
                    member.candidates.clone(),
                    self.secret().map_err(|e| (3, e))?,
                )
                .await
                .map_err(|e| (2, e))?
            } else {
                member.candidates[0]
            };
            let pin = if member.fingerprint == [0; 32] {
                None
            } else {
                Some(member.fingerprint)
            };
            let cfg = self
                .identity
                .client_config(pin, self.config.idle_timeout_ms)
                .map_err(|e| (3, e))?;
            let conn = timeout(
                Duration::from_secs(10),
                self.endpoint
                    .connect_with(cfg, address, "polaris.local")
                    .map_err(|e| (3, e.to_string()))?,
            )
            .await
            .map_err(|_| (3, "QUIC connect timeout".into()))?
            .map_err(|e| (3, e.to_string()))?;
            let guard = conn.clone();
            let result = timeout(
                Duration::from_secs(10),
                self.clone().outgoing_handshake(conn, member, host),
            )
            .await
            .map_err(|_| (3, "hello timeout".into()))
            .and_then(|r| r);
            if result.is_err() {
                guard.close((PROTOCOL as u32).into(), b"hello failed");
            }
            result
        })
    }
    async fn outgoing_handshake(
        self: Arc<Self>,
        conn: quinn::Connection,
        member: Member,
        host: bool,
    ) -> Result<(), (i32, String)> {
        let fp = quic::remote_fingerprint(&conn).map_err(|e| (3, e))?;
        let (mut send, mut recv) = conn.open_bi().await.map_err(|e| (3, e.to_string()))?;
        send.set_priority(10).map_err(|e| (3, e.to_string()))?;
        let secret = self.room.lock().as_ref().map(|r| r.secret);
        let mut hello = 1u16.to_le_bytes().to_vec();
        hello.extend(self.identity.fingerprint);
        hello.extend(
            secret
                .map(|s| hello_mac(s, self.identity.fingerprint, fp))
                .unwrap_or([0; 32]),
        );
        write_frame(&mut send, HELLO, &hello)
            .await
            .map_err(|e| (3, e))?;
        let (kind, ack) = read_frame(&mut recv).await.map_err(|e| (3, e))?;
        if kind != HELLO_ACK || ack.len() != 5 {
            return Err((3, "invalid hello acknowledgement".into()));
        }
        let assigned = i32::from_le_bytes(ack[..4].try_into().unwrap());
        if assigned < 0 {
            return Err((4, "room full".into()));
        }
        if host {
            self.local_id.store(assigned, Ordering::Release);
            let (kind, payload) = read_frame(&mut recv).await.map_err(|e| (3, e))?;
            if kind != ROOM_INFO {
                return Err((3, "room descriptor missing".into()));
            }
            let room: Room = serde_json::from_slice(&payload).map_err(|e| (3, e.to_string()))?;
            if room.fingerprint != fp || secret.is_some_and(|s| s != room.secret) {
                return Err((3, "room identity mismatch".into()));
            }
            *self.socket.secret.lock() = Some(room.secret);
            *self.room.lock() = Some(room.clone());
            self.install(Member {
                id: 0,
                fingerprint: fp,
                candidates: room.candidates,
            })
            .map_err(|e| (3, e))?;
        } else if assigned != self.local_id.load(Ordering::Acquire) {
            return Err((3, "mesh identity mismatch".into()));
        }
        write_frame(
            &mut send,
            MEMBER_INFO,
            &serde_json::to_vec(&self.my_member()).unwrap(),
        )
        .await
        .map_err(|e| (3, e))?;
        if host {
            let (kind, payload) = read_frame(&mut recv).await.map_err(|e| (3, e))?;
            if kind != PEER_LIST {
                return Err((3, "peer list missing".into()));
            }
            let members: Vec<Member> =
                serde_json::from_slice(&payload).map_err(|e| (3, e.to_string()))?;
            if members.len() > self.config.max_peers as usize {
                return Err((3, "peer list too large".into()));
            }
            for peer in members {
                self.install(peer).map_err(|e| (3, e))?;
            }
        }
        self.register(member.id, conn, send, recv)
            .await
            .map_err(|e| (3, e))
    }
    async fn accept_peer(self: Arc<Self>, conn: quinn::Connection) -> Result<(), String> {
        let (mut send, mut recv) = conn.accept_bi().await.map_err(|e| e.to_string())?;
        send.set_priority(10).map_err(|e| e.to_string())?;
        let fp = quic::remote_fingerprint(&conn)?;
        let (kind, hello) = read_frame(&mut recv).await?;
        if kind != HELLO || hello.len() != 66 || hello[..2] != [1, 0] || hello[2..34] != fp {
            return Err("invalid hello".into());
        }
        let secret = self.secret()?;
        let host = self.local_id.load(Ordering::Acquire) == 0;
        let source = crate::nat::socket::normalized(conn.remote_address());
        let direct = host
            && hello[34..] == [0; 32]
            && (self.config.bind_port != 0
                || source.ip().is_loopback()
                || matches!(source.ip(),std::net::IpAddr::V4(v) if v.is_private()));
        if !direct {
            let mut mac = Hmac::<Sha256>::new_from_slice(&secret).unwrap();
            mac.update(&fp);
            mac.update(&self.identity.fingerprint);
            mac.verify_slice(&hello[34..])
                .map_err(|_| "hello room MAC mismatch")?;
        }
        let _admission = if host {
            Some(self.admission.lock().await)
        } else {
            None
        };
        let remote_id = if host {
            if self.peers.read().len() >= usize::from(self.config.max_peers - 1) {
                let mut ack = (-1i32).to_le_bytes().to_vec();
                ack.push(4);
                write_frame(&mut send, HELLO_ACK, &ack).await?;
                send.finish().map_err(|e| e.to_string())?;
                let _ = timeout(Duration::from_secs(1), send.stopped()).await;
                return Err("room full".into());
            }
            self.next_id.fetch_add(1, Ordering::AcqRel)
        } else {
            let member = self
                .members
                .read()
                .values()
                .find(|m| m.fingerprint == fp)
                .cloned()
                .ok_or("unknown mesh identity")?;
            if member.id <= self.local_id.load(Ordering::Acquire) {
                return Err("wrong mesh dialer".into());
            }
            member.id
        };
        let mut ack = remote_id.to_le_bytes().to_vec();
        ack.push(u8::from(host));
        write_frame(&mut send, HELLO_ACK, &ack).await?;
        if host {
            let room = self.room.lock().clone().ok_or("room missing")?;
            write_frame(&mut send, ROOM_INFO, &serde_json::to_vec(&room).unwrap()).await?;
        }
        let (kind, payload) = read_frame(&mut recv).await?;
        if kind != MEMBER_INFO {
            return Err("member descriptor missing".into());
        }
        let member: Member = serde_json::from_slice(&payload).map_err(|e| e.to_string())?;
        if member.id != remote_id || member.fingerprint != fp {
            return Err("member descriptor identity mismatch".into());
        }
        if host {
            let list: Vec<_> = self.members.read().values().cloned().collect();
            write_frame(&mut send, PEER_LIST, &serde_json::to_vec(&list).unwrap()).await?;
        }
        self.install(member.clone())?;
        self.clone().register(remote_id, conn, send, recv).await?;
        if host {
            let bytes = serde_json::to_vec(&member).unwrap();
            let peers: Vec<_> = self
                .peers
                .read()
                .iter()
                .filter(|(id, _)| **id != remote_id)
                .map(|(_, p)| p.clone())
                .collect();
            for peer in peers {
                if peer.control(PEER_JOINED, bytes.clone()).is_err() {
                    peer.connection
                        .close((PROTOCOL as u32).into(), b"control backpressure");
                }
            }
        }
        Ok(())
    }
    async fn register(
        self: Arc<Self>,
        id: i32,
        connection: quinn::Connection,
        send: quinn::SendStream,
        recv: quinn::RecvStream,
    ) -> Result<(), String> {
        if self.closing.load(Ordering::Acquire) {
            return Err("closing".into());
        }
        let peer = Arc::new(Peer {
            id,
            connection,
            queue: Mutex::new(SendQueue::default()),
            wake: Notify::new(),
            seq: AtomicU32::new(0),
            bulk_id: AtomicU32::new(0),
            forced_reason: AtomicI32::new(-1),
        });
        {
            let mut peers = self.peers.write();
            if peers.contains_key(&id) {
                peer.connection
                    .close((PROTOCOL as u32).into(), b"duplicate connection");
                return Err("duplicate connection".into());
            }
            peers.insert(id, peer.clone());
        }
        self.events
            .push(Event::new(event::CONNECTED, id, 0, 0, vec![]))
            .await;
        let state = self.clone();
        let p = peer.clone();
        tokio::spawn(async move {
            state.writer(id, p, send).await;
        });
        let state = self.clone();
        let p = peer.clone();
        tokio::spawn(async move {
            state.reliable_reader(id, p, recv).await;
        });
        let state = self.clone();
        let p = peer.clone();
        tokio::spawn(async move {
            state.datagram_reader(id, p).await;
        });
        let state = self.clone();
        let p = peer.clone();
        tokio::spawn(async move {
            state.bulk_reader(id, p).await;
        });
        let state = self.clone();
        let p = peer.clone();
        tokio::spawn(async move {
            let error = p.connection.closed().await;
            let forced = p.forced_reason.load(Ordering::Acquire);
            let reason = if forced >= 0 {
                forced
            } else {
                match error {
                    quinn::ConnectionError::ApplicationClosed(close) => {
                        let code = close.error_code.into_inner() as i32;
                        if code == LOCAL {
                            REMOTE
                        } else if (1..=6).contains(&code) {
                            code
                        } else {
                            PROTOCOL
                        }
                    }
                    quinn::ConnectionError::TimedOut => TIMEOUT,
                    _ => REMOTE,
                }
            };
            state.disconnected(id, &p, reason).await;
        });
        Ok(())
    }
    async fn writer(self: Arc<Self>, _id: i32, peer: Arc<Peer>, mut send: quinn::SendStream) {
        let bulk_slots = Arc::new(Semaphore::new(2));
        loop {
            let notified = peer.wake.notified();
            let next = { peer.queue.lock().items.pop_front() };
            let Some(item) = next else {
                tokio::select! {_=notified=>continue,_=peer.connection.closed()=>return}
            };
            let size = item.size();
            match item {
                Outgoing::Frame(kind, data) => {
                    let result = write_frame(&mut send, kind, &data).await;
                    peer.completed(size);
                    if result.is_err() {
                        peer.connection
                            .close((PROTOCOL as u32).into(), b"reliable stream failed");
                        return;
                    }
                }
                Outgoing::Bulk(bulk_id, data) => {
                    let p = peer.clone();
                    let slots = bulk_slots.clone();
                    tokio::spawn(async move {
                        let _permit = slots.acquire_owned().await.unwrap();
                        let result = async {
                            let mut stream =
                                p.connection.open_uni().await.map_err(|e| e.to_string())?;
                            stream.set_priority(-10).map_err(|e| e.to_string())?;
                            stream
                                .write_u32_le(bulk_id)
                                .await
                                .map_err(|e| e.to_string())?;
                            stream
                                .write_u32_le(data.len() as u32)
                                .await
                                .map_err(|e| e.to_string())?;
                            for chunk in data.chunks(64 * 1024) {
                                stream.write_all(chunk).await.map_err(|e| e.to_string())?;
                                tokio::task::yield_now().await;
                            }
                            stream.finish().map_err(|e| e.to_string())?;
                            Ok::<_, String>(())
                        }
                        .await;
                        p.completed(size);
                        if result.is_err() {
                            p.connection
                                .close((PROTOCOL as u32).into(), b"bulk stream failed");
                        }
                    });
                }
            }
        }
    }
    async fn reliable_reader(
        self: Arc<Self>,
        id: i32,
        peer: Arc<Peer>,
        mut recv: quinn::RecvStream,
    ) {
        while let Ok((kind, data)) = read_frame(&mut recv).await {
            match kind {
                USER => {
                    self.events
                        .push(Event::new(event::MESSAGE, id, RELIABLE, 0, data))
                        .await
                }
                DISCONNECT if data.len() == 1 => {
                    let reason = i32::from(data[0]);
                    if (0..=6).contains(&reason) {
                        peer.forced_reason.store(
                            if reason == LOCAL { REMOTE } else { reason },
                            Ordering::Release,
                        );
                        peer.connection.close((reason as u32).into(), b"disconnect");
                    }
                    return;
                }
                PEER_LIST | PEER_JOINED | PEER_LEFT | PUNCH => {
                    if let Err(error) = self.clone().control(id, kind, data).await {
                        self.events.log(2, error);
                        peer.connection
                            .close((PROTOCOL as u32).into(), b"invalid control");
                        return;
                    }
                }
                _ => {} // 未知帧已按长度完整读取，可安全跳过。
            }
        }
        // A remote application close also wakes the stream reader. Calling close
        // again would replace its Kicked/HostClosed code with LocallyClosed.
        if peer.connection.close_reason().is_none() {
            peer.forced_reason.store(PROTOCOL, Ordering::Release);
            peer.connection
                .close((PROTOCOL as u32).into(), b"reliable stream closed");
        }
    }
    async fn control(self: Arc<Self>, sender: i32, kind: u8, data: Vec<u8>) -> Result<(), String> {
        match kind {
            PEER_JOINED if sender == 0 && self.local_id.load(Ordering::Acquire) != 0 => {
                let member: Member = serde_json::from_slice(&data).map_err(|e| e.to_string())?;
                self.install(member.clone())?;
                let state = self.clone();
                tokio::spawn(async move {
                    if state.local_id.load(Ordering::Acquire) > member.id {
                        if let Err(e) = state.clone().connect_member(member).await {
                            state.events.log(2, e);
                        }
                    } else if let Ok(secret) = state.secret() {
                        let _ = punch::punch(state.socket.clone(), member.candidates, secret).await;
                    }
                });
            }
            PEER_LEFT if sender == 0 && self.local_id.load(Ordering::Acquire) != 0 => {
                if data.len() != 8 {
                    return Err("invalid peer left".into());
                }
                let id = i32::from_le_bytes(data[..4].try_into().unwrap());
                let reason = i32::from_le_bytes(data[4..].try_into().unwrap());
                if id <= 0 || id == self.local_id.load(Ordering::Acquire) {
                    return Err("invalid departed member".into());
                }
                self.members.write().remove(&id);
                let p = self.peers.read().get(&id).cloned();
                if let Some(p) = p {
                    p.forced_reason.store(reason, Ordering::Release);
                    p.connection.close((reason as u32).into(), b"member left");
                }
            }
            PUNCH => {
                let request: PunchRequest =
                    serde_json::from_slice(&data).map_err(|e| e.to_string())?;
                if self.local_id.load(Ordering::Acquire) == 0 {
                    if request.from != sender || request.to <= 0 || request.to == sender {
                        return Err("invalid punch route".into());
                    }
                    let target = self
                        .peers
                        .read()
                        .get(&request.to)
                        .cloned()
                        .ok_or("punch target missing")?;
                    target
                        .control(PUNCH, data)
                        .map_err(|_| "control queue full")?;
                } else {
                    if sender != 0 || request.to != self.local_id.load(Ordering::Acquire) {
                        return Err("unauthorized punch".into());
                    }
                    let member = self
                        .members
                        .read()
                        .get(&request.from)
                        .cloned()
                        .ok_or("punch source missing")?;
                    let state = self.clone();
                    tokio::spawn(async move {
                        if let Ok(secret) = state.secret() {
                            let _ =
                                punch::punch(state.socket.clone(), member.candidates, secret).await;
                        }
                    });
                }
            }
            _ => return Err("unauthorized control frame".into()),
        }
        Ok(())
    }
    async fn datagram_reader(&self, id: i32, peer: Arc<Peer>) {
        let mut previous = None;
        while let Ok(data) = peer.connection.read_datagram().await {
            if data.len() < 4 {
                continue;
            }
            let seq = u32::from_le_bytes(data[..4].try_into().unwrap());
            if previous.is_some_and(|last| !is_newer(seq, last)) {
                continue;
            }
            previous = Some(seq);
            self.events
                .push(Event::new(
                    event::MESSAGE,
                    id,
                    SEQUENCED,
                    0,
                    data[4..].to_vec(),
                ))
                .await;
        }
    }
    async fn bulk_reader(self: Arc<Self>, id: i32, peer: Arc<Peer>) {
        let memory = self.bulk_memory.clone();
        while let Ok(mut stream) = peer.connection.accept_uni().await {
            let state = self.clone();
            let p = peer.clone();
            let memory = memory.clone();
            tokio::spawn(async move {
                let result = async {
                    let bulk_id = stream.read_u32_le().await.map_err(|e| e.to_string())?;
                    let len = stream.read_u32_le().await.map_err(|e| e.to_string())? as usize;
                    if len > MAX_BULK {
                        let _ = stream.stop((PROTOCOL as u32).into());
                        return Err("bulk exceeds 64MB".into());
                    }
                    let _permit = memory
                        .acquire_many_owned(len as u32)
                        .await
                        .map_err(|e| e.to_string())?;
                    let mut data = vec![0; len];
                    let mut read = 0;
                    let mut last = Instant::now() - Duration::from_secs(1);
                    while read < len {
                        let end = (read + 64 * 1024).min(len);
                        let n = stream
                            .read(&mut data[read..end])
                            .await
                            .map_err(|e| e.to_string())?
                            .ok_or("bulk truncated")?;
                        if n == 0 {
                            return Err("bulk truncated".into());
                        }
                        read += n;
                        if read == len || last.elapsed() >= Duration::from_millis(100) {
                            last = Instant::now();
                            let mut ev = Event::new(event::BULK_PROGRESS, id, BULK, 0, vec![]);
                            ev.header.aux0 = read as u32;
                            ev.header.aux1 = len as u32;
                            ev.header.aux2 = bulk_id;
                            state.events.push(ev).await;
                        }
                    }
                    if stream
                        .read(&mut [0u8; 1])
                        .await
                        .map_err(|e| e.to_string())?
                        .is_some()
                    {
                        return Err("bulk trailing bytes".into());
                    }
                    state
                        .events
                        .push(Event::new(event::MESSAGE, id, BULK, 0, data))
                        .await;
                    Ok::<_, String>(())
                }
                .await;
                if let Err(error) = result {
                    state.events.log(1, format!("bulk receive: {error}"));
                    if !error.contains("64MB") {
                        p.connection
                            .close((PROTOCOL as u32).into(), b"bulk corrupted");
                    }
                }
            });
        }
    }
    async fn disconnected(self: Arc<Self>, id: i32, peer: &Arc<Peer>, reason: i32) {
        let removed = {
            let mut peers = self.peers.write();
            if peers.get(&id).is_some_and(|p| Arc::ptr_eq(p, peer)) {
                peers.remove(&id);
                true
            } else {
                false
            }
        };
        if !removed {
            return;
        }
        if id == 0 && self.local_id.load(Ordering::Acquire) != 0 {
            let mesh_reason = match reason {
                KICKED => KICKED,
                HOST_CLOSED => HOST_CLOSED,
                _ => LOCAL,
            };
            let others: Vec<_> = self.peers.read().values().cloned().collect();
            for p in others {
                p.forced_reason.store(mesh_reason, Ordering::Release);
                p.connection
                    .close((mesh_reason as u32).into(), b"room connection ended");
            }
        }
        if self.local_id.load(Ordering::Acquire) == 0 {
            self.members.write().remove(&id);
            let mut data = id.to_le_bytes().to_vec();
            data.extend(reason.to_le_bytes());
            let others: Vec<_> = self.peers.read().values().cloned().collect();
            for p in others {
                if p.control(PEER_LEFT, data.clone()).is_err() {
                    p.connection
                        .close((PROTOCOL as u32).into(), b"membership backpressure");
                }
            }
        }
        self.events
            .push(Event::new(event::DISCONNECTED, id, 0, reason, vec![]))
            .await;
    }
    pub fn send(&self, target: i32, channel: i32, data: &[u8]) -> Result<(), i32> {
        if self.closing.load(Ordering::Acquire) {
            return Err(-2);
        }
        if !(0..=2).contains(&channel)
            || (channel == RELIABLE && data.len() > MAX_RELIABLE)
            || data.len() > MAX_BULK
        {
            return Err(-1);
        }
        let mut peers: Vec<_> = if target == -1 {
            self.peers.read().values().cloned().collect()
        } else {
            vec![self.peers.read().get(&target).cloned().ok_or(-3)?]
        };
        peers.sort_by_key(|p| p.id);
        if channel == SEQUENCED {
            if peers.iter().any(|p| {
                p.connection
                    .max_datagram_size()
                    .is_none_or(|max| data.len() + 4 > max)
            }) {
                return Err(-1);
            }
            for peer in peers {
                let mut bytes = peer
                    .seq
                    .fetch_add(1, Ordering::Relaxed)
                    .to_le_bytes()
                    .to_vec();
                bytes.extend(data);
                peer.connection
                    .send_datagram(Bytes::from(bytes))
                    .map_err(|_| -2)?;
            }
        } else {
            // 同一次广播先预留所有目标预算；失败时不对部分目标发送，避免上层重试产生重复事件。
            let mut guards: Vec<_> = peers.iter().map(|p| p.queue.lock()).collect();
            if guards
                .iter()
                .any(|q| q.count >= 4096 || (q.count > 0 && q.bytes + data.len() > 8 * 1024 * 1024))
            {
                return Err(-5);
            }
            let bytes = Bytes::copy_from_slice(data);
            for (peer, q) in peers.iter().zip(guards.iter_mut()) {
                let item = if channel == RELIABLE {
                    Outgoing::Frame(USER, bytes.clone())
                } else {
                    Outgoing::Bulk(peer.bulk_id.fetch_add(1, Ordering::Relaxed), bytes.clone())
                };
                q.bytes += data.len();
                q.count += 1;
                q.items.push_back(item);
                peer.wake.notify_one();
            }
        }
        Ok(())
    }
    pub fn disconnect(&self, id: i32, reason: i32) -> Result<(), i32> {
        if !(0..=6).contains(&reason)
            || (reason == KICKED && self.local_id.load(Ordering::Acquire) != 0)
        {
            return Err(-1);
        }
        let peer = self.peers.read().get(&id).cloned().ok_or(-3)?;
        peer.forced_reason.store(reason, Ordering::Release);
        peer.connection.close((reason as u32).into(), b"disconnect");
        Ok(())
    }
    pub async fn shutdown(&self) {
        self.closing.store(true, Ordering::Release);
        let reason = if self.local_id.load(Ordering::Acquire) == 0 {
            HOST_CLOSED
        } else {
            LOCAL
        };
        let peers: Vec<_> = self.peers.read().values().cloned().collect();
        for peer in peers {
            let _ = peer.control(DISCONNECT, vec![reason as u8]);
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
        self.endpoint.close((reason as u32).into(), b"shutdown");
        let _ = timeout(Duration::from_millis(200), self.endpoint.wait_idle()).await;
    }
}

fn hello_mac(secret: [u8; 16], client: [u8; 32], server: [u8; 32]) -> [u8; 32] {
    let mut mac = Hmac::<Sha256>::new_from_slice(&secret).unwrap();
    mac.update(&client);
    mac.update(&server);
    mac.finalize().into_bytes().into()
}
