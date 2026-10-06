use super::{punch, stun};
use parking_lot::Mutex;
use quinn::{
    udp::{RecvMeta, Transmit},
    AsyncUdpSocket, UdpPoller,
};
use std::{
    collections::{HashMap, HashSet},
    io::{self, IoSliceMut},
    net::SocketAddr,
    pin::Pin,
    sync::{
        atomic::{AtomicI32, Ordering},
        Arc,
    },
    task::{Context, Poll},
};
use tokio::sync::oneshot;

#[derive(Debug)]
pub struct ProbeWait {
    pub secret: [u8; 16],
    pub targets: Vec<SocketAddr>,
    pub sender: oneshot::Sender<SocketAddr>,
}
#[derive(Debug)]
pub struct StunWait {
    pub target: SocketAddr,
    pub sender: oneshot::Sender<SocketAddr>,
}
#[derive(Debug)]
pub struct PunchSocket {
    pub inner: Arc<dyn AsyncUdpSocket>,
    pub secret: Mutex<Option<[u8; 16]>>,
    pub local_id: Arc<AtomicI32>,
    pub probes: Mutex<HashMap<u32, ProbeWait>>,
    pub stuns: Mutex<HashMap<[u8; 12], StunWait>>,
    pub trusted_peers: Mutex<HashSet<i32>>,
}
pub fn normalized(addr: SocketAddr) -> SocketAddr {
    match addr {
        SocketAddr::V6(a) => a
            .ip()
            .to_ipv4_mapped()
            .map(|ip| SocketAddr::new(ip.into(), a.port()))
            .unwrap_or(addr),
        _ => addr,
    }
}
impl PunchSocket {
    pub fn new(inner: Arc<dyn AsyncUdpSocket>, local_id: Arc<AtomicI32>) -> Self {
        Self {
            inner,
            secret: Mutex::new(None),
            local_id,
            probes: Mutex::new(HashMap::new()),
            stuns: Mutex::new(HashMap::new()),
            trusted_peers: Mutex::new(HashSet::new()),
        }
    }
    pub fn send_raw(&self, destination: SocketAddr, contents: &[u8]) -> io::Result<()> {
        // Quinn maps IPv4 destinations for its own dual-stack traffic. STUN/probes
        // bypass Endpoint and must apply the same mapping (WSASendMsg on Windows).
        let destination = if self.inner.local_addr()?.is_ipv6() {
            match destination {
                SocketAddr::V4(v) => SocketAddr::new(v.ip().to_ipv6_mapped().into(), v.port()),
                other => other,
            }
        } else {
            destination
        };
        self.inner.try_send(&Transmit {
            destination,
            ecn: None,
            contents,
            segment_size: None,
            src_ip: None,
        })
    }
    fn intercept(&self, data: &[u8], source: SocketAddr) -> bool {
        let source = normalized(source);
        if data.first() == Some(&0x20) {
            if let Some(secret) = *self.secret.lock() {
                if let Some((kind, nonce, peer, target)) = punch::decode(data, &secret) {
                    if kind == 1 {
                        // Mesh probes may arrive before the host's membership announcement.
                        // ACK only after its certificate is trusted, so TLS cannot race PeerJoined.
                        if self.local_id.load(Ordering::Acquire) != 0
                            && !self.trusted_peers.lock().contains(&peer)
                        {
                            return true;
                        }
                        let ack = punch::encode(
                            2,
                            nonce,
                            self.local_id.load(Ordering::Acquire),
                            target,
                            &secret,
                        );
                        let _ = self.send_raw(source, &ack);
                    } else if kind == 2 {
                        let mut waits = self.probes.lock();
                        if waits
                            .get(&nonce)
                            .is_some_and(|w| w.secret == secret && w.targets.contains(&target))
                        {
                            if let Some(w) = waits.remove(&nonce) {
                                let _ = w.sender.send(source);
                            }
                        }
                    }
                }
            }
            return true;
        }
        if data.len() >= 20 && data[0] & 0xc0 == 0 && data[4..8] == [0x21, 0x12, 0xa4, 0x42] {
            let transaction: [u8; 12] = data[8..20].try_into().unwrap();
            let mut waits = self.stuns.lock();
            if waits
                .get(&transaction)
                .is_some_and(|w| normalized(w.target) == source)
            {
                if let Ok(mapped) = stun::parse_response(data, transaction) {
                    if let Some(wait) = waits.remove(&transaction) {
                        let _ = wait.sender.send(mapped);
                    }
                }
            }
            return true;
        }
        false
    }
}
impl AsyncUdpSocket for PunchSocket {
    fn create_io_poller(self: Arc<Self>) -> Pin<Box<dyn UdpPoller>> {
        self.inner.clone().create_io_poller()
    }
    fn try_send(&self, t: &Transmit) -> io::Result<()> {
        self.inner.try_send(t)
    }
    fn local_addr(&self) -> io::Result<SocketAddr> {
        self.inner.local_addr()
    }
    fn max_transmit_segments(&self) -> usize {
        self.inner.max_transmit_segments()
    }
    fn max_receive_segments(&self) -> usize {
        self.inner.max_receive_segments()
    }
    fn may_fragment(&self) -> bool {
        self.inner.may_fragment()
    }
    fn poll_recv(
        &self,
        cx: &mut Context<'_>,
        bufs: &mut [IoSliceMut<'_>],
        meta: &mut [RecvMeta],
    ) -> Poll<io::Result<usize>> {
        for _ in 0..32 {
            let n = match self.inner.poll_recv(cx, bufs, meta) {
                Poll::Ready(Ok(n)) => n,
                other => return other,
            };
            if n == 0 {
                return Poll::Ready(Ok(0));
            }
            let mut kept = 0;
            for i in 0..n {
                let stride = meta[i].stride.max(1);
                let len = meta[i].len;
                let source = meta[i].addr;
                let mut written = 0;
                for start in (0..len).step_by(stride) {
                    let end = (start + stride).min(len);
                    if !self.intercept(&bufs[i][start..end], source) {
                        bufs[i].copy_within(start..end, written);
                        written += end - start;
                    }
                }
                if written != 0 {
                    if kept != i {
                        let (left, right) = bufs.split_at_mut(i);
                        left[kept][..written].copy_from_slice(&right[0][..written]);
                    }
                    meta[kept] = meta[i];
                    meta[kept].len = written;
                    kept += 1;
                }
            }
            if kept != 0 {
                return Poll::Ready(Ok(kept));
            }
        }
        cx.waker().wake_by_ref();
        Poll::Pending
    }
}
