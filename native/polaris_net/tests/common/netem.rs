//! A single-client UDP proxy; packets in both directions experience seeded impairments.
use rand::{rngs::StdRng, Rng, SeedableRng};
use std::{
    collections::VecDeque,
    net::{SocketAddr, UdpSocket},
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc,
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant},
};
#[derive(Clone, Copy, Default)]
pub struct Impairment {
    pub delay_ms: u64,
    pub jitter_ms: u64,
    pub loss_percent: u32,
    pub reorder_percent: u32,
    pub bytes_per_second: u64,
}
pub struct Proxy {
    pub address: SocketAddr,
    stop: Arc<AtomicBool>,
    worker: Option<JoinHandle<()>>,
}
impl Proxy {
    pub fn new(server: SocketAddr, config: Impairment) -> Self {
        let sock = UdpSocket::bind("127.0.0.1:0").unwrap();
        socket2::SockRef::from(&sock)
            .set_recv_buffer_size(4 * 1024 * 1024)
            .unwrap();
        sock.set_nonblocking(true).unwrap();
        let address = sock.local_addr().unwrap();
        let stop = Arc::new(AtomicBool::new(false));
        let done = stop.clone();
        let worker = thread::spawn(move || {
            let mut client = None;
            let mut rng = StdRng::seed_from_u64(0x504e31);
            let mut queue: VecDeque<(Instant, SocketAddr, Vec<u8>)> = VecDeque::new();
            let mut buf = vec![0; 65536];
            let mut pacing = [Instant::now(); 2];
            while !done.load(Ordering::Relaxed) {
                while let Ok((n, src)) = sock.recv_from(&mut buf) {
                    let direction = usize::from(src == server);
                    let dst = if direction == 1 {
                        if let Some(c) = client {
                            c
                        } else {
                            continue;
                        }
                    } else {
                        client = Some(src);
                        server
                    };
                    if std::env::var_os("PN_PROXY_DEBUG").is_some() {
                        eprintln!("proxy {src} -> {dst} n={n} first={:x}", buf[0]);
                    }
                    if rng.gen_range(0..100) < config.loss_percent {
                        continue;
                    }
                    let jitter = if config.jitter_ms == 0 {
                        0
                    } else {
                        rng.gen_range(0..=config.jitter_ms * 2) as i64 - config.jitter_ms as i64
                    };
                    let reorder = if rng.gen_range(0..100) < config.reorder_percent {
                        50
                    } else {
                        0
                    };
                    let mut due = Instant::now()
                        + Duration::from_millis(
                            (config.delay_ms as i64 + jitter).max(0) as u64 + reorder,
                        );
                    if config.bytes_per_second > 0 {
                        due = due.max(pacing[direction]);
                        pacing[direction] = due
                            + Duration::from_secs_f64(n as f64 / config.bytes_per_second as f64);
                    }
                    let at = queue
                        .iter()
                        .position(|(t, _, _)| *t > due)
                        .unwrap_or(queue.len());
                    queue.insert(at, (due, dst, buf[..n].to_vec()));
                }
                let now = Instant::now();
                while queue.front().is_some_and(|(t, _, _)| *t <= now) {
                    let (_, dst, data) = queue.pop_front().unwrap();
                    let _ = sock.send_to(&data, dst);
                }
                thread::sleep(Duration::from_millis(1));
            }
        });
        Self {
            address,
            stop,
            worker: Some(worker),
        }
    }
}
impl Drop for Proxy {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
        if let Some(worker) = self.worker.take() {
            worker.join().unwrap();
        }
    }
}
