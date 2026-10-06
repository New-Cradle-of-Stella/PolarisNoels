//! Real UDP mappings and filtering. Private sockets tunnel through a gateway;
//! QUIC sees the original external source, never the private tunnel address.
use quinn::{
    udp::{RecvMeta, Transmit},
    AsyncUdpSocket, Runtime as _, UdpPoller,
};
use std::{
    collections::{HashMap, HashSet},
    io::{self, IoSliceMut},
    net::{IpAddr, Ipv4Addr, SocketAddr, UdpSocket},
    pin::Pin,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc,
    },
    task::{Context, Poll},
    thread::{self, JoinHandle},
    time::Duration,
};
#[derive(Clone, Copy, PartialEq)]
pub enum Mode {
    FullCone,
    Restricted,
    Symmetric,
}
pub struct Nat {
    gateway: SocketAddr,
    done: Arc<AtomicBool>,
    worker: Option<JoinHandle<()>>,
}
fn udp(ip: &str) -> UdpSocket {
    let s = UdpSocket::bind(format!("{ip}:0")).unwrap();
    s.set_nonblocking(true).unwrap();
    socket2::SockRef::from(&s)
        .set_recv_buffer_size(4 * 1024 * 1024)
        .unwrap();
    s
}
fn envelope(addr: SocketAddr, data: &[u8]) -> io::Result<Vec<u8>> {
    let addr = polaris_net::nat::socket::normalized(addr);
    let IpAddr::V4(ip) = addr.ip() else {
        return Err(io::ErrorKind::AddrNotAvailable.into());
    };
    let mut out = b"NT".to_vec();
    out.extend(ip.octets());
    out.extend(addr.port().to_be_bytes());
    out.extend(data);
    Ok(out)
}
fn unpack(data: &[u8]) -> Option<(SocketAddr, &[u8])> {
    if data.len() < 8 || data[..2] != *b"NT" {
        return None;
    }
    Some((
        SocketAddr::new(
            Ipv4Addr::new(data[2], data[3], data[4], data[5]).into(),
            u16::from_be_bytes([data[6], data[7]]),
        ),
        &data[8..],
    ))
}
impl Nat {
    pub fn new(mode: Mode) -> Self {
        let gateway = udp("127.0.0.1");
        let address = gateway.local_addr().unwrap();
        let done = Arc::new(AtomicBool::new(false));
        let stop = done.clone();
        let worker = thread::spawn(move || {
            let mut private = None;
            let mut maps: HashMap<SocketAddr, (UdpSocket, HashSet<SocketAddr>)> = HashMap::new();
            let mut buf = vec![0; 65536];
            while !stop.load(Ordering::Relaxed) {
                while let Ok((n, src)) = gateway.recv_from(&mut buf) {
                    if let Some((dst, data)) = unpack(&buf[..n]) {
                        private = Some(src);
                        let key = if mode == Mode::Symmetric {
                            dst
                        } else {
                            "0.0.0.0:0".parse().unwrap()
                        };
                        let entry = maps
                            .entry(key)
                            .or_insert_with(|| (udp("127.0.0.1"), HashSet::new()));
                        entry.1.insert(dst);
                        let _ = entry.0.send_to(data, dst);
                    }
                }
                for (socket, allowed) in maps.values() {
                    while let Ok((n, src)) = socket.recv_from(&mut buf) {
                        if mode != Mode::FullCone && !allowed.contains(&src) {
                            continue;
                        }
                        if let Some(private) = private {
                            if let Ok(data) = envelope(src, &buf[..n]) {
                                let _ = gateway.send_to(&data, private);
                            }
                        }
                    }
                }
                thread::sleep(Duration::from_millis(1));
            }
        });
        Self {
            gateway: address,
            done,
            worker: Some(worker),
        }
    }
    pub fn wrap(&self, socket: UdpSocket) -> Result<Arc<dyn AsyncUdpSocket>, String> {
        let inner = quinn::TokioRuntime
            .wrap_udp_socket(socket)
            .map_err(|e| e.to_string())?;
        Ok(Arc::new(Tunnel {
            inner,
            gateway: self.gateway,
        }))
    }
}
impl Drop for Nat {
    fn drop(&mut self) {
        self.done.store(true, Ordering::Relaxed);
        if let Some(worker) = self.worker.take() {
            worker.join().unwrap();
        }
    }
}
#[derive(Debug)]
struct Tunnel {
    inner: Arc<dyn AsyncUdpSocket>,
    gateway: SocketAddr,
}
impl AsyncUdpSocket for Tunnel {
    fn create_io_poller(self: Arc<Self>) -> Pin<Box<dyn UdpPoller>> {
        self.inner.clone().create_io_poller()
    }
    fn local_addr(&self) -> io::Result<SocketAddr> {
        self.inner.local_addr()
    }
    fn try_send(&self, t: &Transmit) -> io::Result<()> {
        let bytes = envelope(t.destination, t.contents)?;
        let IpAddr::V4(ip) = self.gateway.ip() else {
            unreachable!()
        };
        let dest = SocketAddr::new(ip.to_ipv6_mapped().into(), self.gateway.port());
        self.inner.try_send(&Transmit {
            destination: dest,
            ecn: None,
            contents: &bytes,
            segment_size: None,
            src_ip: None,
        })
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
            let mut kept = 0;
            for i in 0..n {
                if let Some((src, data)) = unpack(&bufs[i][..meta[i].len]) {
                    let len = data.len();
                    bufs[i].copy_within(8..8 + len, 0);
                    if kept != i {
                        let (l, r) = bufs.split_at_mut(i);
                        l[kept][..len].copy_from_slice(&r[0][..len]);
                    }
                    meta[kept] = meta[i];
                    let IpAddr::V4(ip) = src.ip() else {
                        unreachable!()
                    };
                    meta[kept].addr = SocketAddr::new(ip.to_ipv6_mapped().into(), src.port());
                    meta[kept].len = len;
                    meta[kept].stride = len;
                    kept += 1;
                }
            }
            if kept > 0 {
                return Poll::Ready(Ok(kept));
            }
        }
        cx.waker().wake_by_ref();
        Poll::Pending
    }
}
pub struct Stun {
    pub servers: String,
    done: Arc<AtomicBool>,
    worker: Option<JoinHandle<()>>,
}
impl Stun {
    pub fn new() -> Self {
        let sockets = [udp("127.0.0.2"), udp("127.0.0.3")];
        let servers = sockets
            .iter()
            .map(|s| s.local_addr().unwrap().to_string())
            .collect::<Vec<_>>()
            .join(",");
        let done = Arc::new(AtomicBool::new(false));
        let stop = done.clone();
        let worker = thread::spawn(move || {
            let mut buf = [0; 65536];
            while !stop.load(Ordering::Relaxed) {
                for socket in &sockets {
                    while let Ok((n, src)) = socket.recv_from(&mut buf) {
                        if n != 20 || buf[..2] != [0, 1] {
                            continue;
                        }
                        let IpAddr::V4(ip) = src.ip() else { continue };
                        let mut out = vec![1, 1, 0, 12, 0x21, 0x12, 0xa4, 0x42];
                        out.extend(&buf[8..20]);
                        out.extend([0, 0x20, 0, 8, 0, 1]);
                        out.extend((src.port() ^ 0x2112).to_be_bytes());
                        for (v, m) in ip.octets().iter().zip([0x21, 0x12, 0xa4, 0x42]) {
                            out.push(v ^ m);
                        }
                        let _ = socket.send_to(&out, src);
                    }
                }
                thread::sleep(Duration::from_millis(1));
            }
        });
        Self {
            servers,
            done,
            worker: Some(worker),
        }
    }
}
impl Drop for Stun {
    fn drop(&mut self) {
        self.done.store(true, Ordering::Relaxed);
        if let Some(worker) = self.worker.take() {
            worker.join().unwrap();
        }
    }
}
