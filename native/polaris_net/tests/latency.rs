mod common;
use common::*;
use polaris_net::{channels::*, event::*, room_code::Room, runtime::Node};
use std::{
    sync::atomic::Ordering,
    thread,
    time::{Duration, Instant},
};
struct Scenario {
    host: Node,
    client: Node,
    _proxy: netem::Proxy,
    id: i32,
    sent: u32,
    reliable: u32,
    seq_sent: u32,
    seq_last: Option<u32>,
    seq_recv: u64,
    bulk: bool,
    bulk_started: Instant,
    bulk_seconds: Option<f64>,
    rtt: u64,
}
fn scenario(rtt: u64, loss: u32, jitter: bool) -> Scenario {
    let host = host(2);
    let mut room = Room::decode(&code(&host)).unwrap();
    let actual = room
        .candidates
        .iter()
        .find(|a| a.ip().is_loopback())
        .copied()
        .unwrap();
    let proxy = netem::Proxy::new(
        actual,
        netem::Impairment {
            delay_ms: rtt / 2,
            jitter_ms: if jitter { 25 } else { 0 },
            loss_percent: loss,
            reorder_percent: if jitter { 5 } else { 0 },
            ..Default::default()
        },
    );
    room.candidates = vec![proxy.address];
    let client = node(2);
    client.join(&room.encode()).unwrap();
    let mut joined = false;
    until(
        || {
            for ev in drain(&client) {
                if ev.header.kind == JOIN_RESULT {
                    assert_eq!(ev.header.code, 0);
                    joined = true;
                }
            }
            joined
        },
        25,
    );
    drain(&host);
    let id = client.state.local_id.load(Ordering::Acquire);
    let now = Instant::now();
    Scenario {
        host,
        client,
        _proxy: proxy,
        id,
        sent: 0,
        reliable: 0,
        seq_sent: 0,
        seq_last: None,
        seq_recv: 0,
        bulk: false,
        bulk_started: now,
        bulk_seconds: None,
        rtt,
    }
}
fn receive(s: &mut Scenario) {
    for ev in drain(&s.client) {
        match ev.header.kind {
            DISCONNECTED => panic!("RTT{} disconnected {}", s.rtt, ev.header.code),
            MESSAGE => match ev.header.channel {
                RELIABLE => {
                    let n = u32::from_le_bytes(ev.data[..4].try_into().unwrap());
                    assert_eq!(n, s.reliable, "reliable loss/reorder");
                    s.reliable += 1;
                }
                SEQUENCED => {
                    let n = u32::from_le_bytes(ev.data[..4].try_into().unwrap());
                    if let Some(last) = s.seq_last {
                        assert!(is_newer(n, last), "datagram reordered");
                    }
                    s.seq_last = Some(n);
                    s.seq_recv += 1;
                }
                BULK => {
                    assert_eq!(ev.data, vec![0x5a; 5 * 1024 * 1024]);
                    s.bulk = true;
                    s.bulk_seconds = Some(s.bulk_started.elapsed().as_secs_f64());
                }
                _ => panic!("channel"),
            },
            _ => {}
        }
    }
    for ev in drain(&s.host) {
        assert_ne!(ev.header.kind, DISCONNECTED);
    }
}
fn run(duration: u64, jitter: bool) {
    let mut scenarios = vec![scenario(400, 5, jitter), scenario(800, 15, jitter)];
    // Start both transfers after setup. Otherwise the second handshake would
    // inflate the first transfer's measured application-delivery time.
    for s in &mut scenarios {
        s.bulk_started = Instant::now();
        s.host
            .state
            .send(s.id, BULK, &vec![0x5a; 5 * 1024 * 1024])
            .unwrap();
    }
    let start = Instant::now();
    let mut seq_tick = Instant::now();
    let mut reliable_tick = Instant::now();
    let cpu_start = metrics::cpu();
    let mut memory = Vec::new();
    let mut sampled = Instant::now();
    while start.elapsed() < Duration::from_secs(duration) {
        let now = Instant::now();
        if now >= seq_tick {
            seq_tick += Duration::from_nanos(1_000_000_000 / 60);
            for s in &mut scenarios {
                for _ in 0..8 {
                    let mut data = [0; 150];
                    data[..4].copy_from_slice(&s.seq_sent.to_le_bytes());
                    s.host.state.send(s.id, SEQUENCED, &data).unwrap();
                    s.seq_sent += 1;
                }
            }
        }
        if now >= reliable_tick {
            reliable_tick += Duration::from_millis(20);
            for s in &mut scenarios {
                s.host
                    .state
                    .send(s.id, RELIABLE, &s.sent.to_le_bytes())
                    .unwrap();
                s.sent += 1;
            }
        }
        for s in &mut scenarios {
            receive(s);
        }
        if sampled.elapsed() >= Duration::from_secs(10) {
            memory.push(metrics::memory());
            sampled = Instant::now();
            eprintln!(
                "elapsed={}s memory={}KiB reliable={}/{}",
                start.elapsed().as_secs(),
                memory.last().unwrap() / 1024,
                scenarios[0].reliable,
                scenarios[0].sent
            );
        }
        thread::sleep(Duration::from_millis(2));
    }
    until(
        || {
            for s in &mut scenarios {
                receive(s);
            }
            scenarios.iter().all(|s| s.sent == s.reliable && s.bulk)
        },
        60,
    );
    let cpu = (metrics::cpu() - cpu_start) / start.elapsed().as_secs_f64() * 100.;
    eprintln!("CPU single-core percent={cpu:.2}, memory samples bytes={memory:?}");
    for s in &scenarios {
        eprintln!(
            "RTT{} sent={} reliable={} seq={}/{} bulk5MB={:.3}s measured_rtt={}ms",
            s.rtt,
            s.sent,
            s.reliable,
            s.seq_recv,
            s.seq_sent,
            s.bulk_seconds.unwrap(),
            s.host.state.peers.read()[&s.id]
                .connection
                .stats()
                .path
                .rtt
                .as_millis()
        );
    }
    if !jitter {
        assert!(
            scenarios[0].bulk_seconds.unwrap() < 15.0,
            "5MB/400ms/5% bulk exceeds 15s target"
        );
    }
    if duration >= 1800 {
        assert!(
            cpu < 20.,
            "whole two-scenario test exceeds 20% single-core CPU target"
        );
        let tail = &memory[memory.len() / 2..];
        assert!(
            tail.last().unwrap() < &(tail.iter().min().unwrap() + 16 * 1024 * 1024),
            "steady memory grew more than 16MiB"
        );
    }
}
#[test]
fn delayed_bulk_and_order() {
    run(20, false);
}
#[test]
#[ignore = "30 minute acceptance; PN_SOAK_SECONDS can shorten for diagnosis"]
fn thirty_minute_soak() {
    run(
        std::env::var("PN_SOAK_SECONDS")
            .ok()
            .and_then(|v| v.parse().ok())
            .unwrap_or(1800),
        true,
    );
}
#[cfg(windows)]
mod metrics {
    #[repr(C)]
    #[derive(Default)]
    struct Memory {
        cb: u32,
        faults: u32,
        peak: usize,
        working: usize,
        quota_peak_paged: usize,
        quota_paged: usize,
        quota_peak_nonpaged: usize,
        quota_nonpaged: usize,
        pagefile: usize,
        peak_pagefile: usize,
    }
    unsafe extern "system" {
        fn GetCurrentProcess() -> *mut std::ffi::c_void;
        fn GetProcessTimes(
            h: *mut std::ffi::c_void,
            c: *mut u64,
            e: *mut u64,
            k: *mut u64,
            u: *mut u64,
        ) -> i32;
    }
    #[link(name = "psapi")]
    unsafe extern "system" {
        fn GetProcessMemoryInfo(h: *mut std::ffi::c_void, m: *mut Memory, cb: u32) -> i32;
    }
    pub fn cpu() -> f64 {
        unsafe {
            let (mut c, mut e, mut k, mut u) = (0, 0, 0, 0);
            assert_ne!(
                GetProcessTimes(GetCurrentProcess(), &mut c, &mut e, &mut k, &mut u),
                0
            );
            (k + u) as f64 / 10_000_000.
        }
    }
    pub fn memory() -> usize {
        unsafe {
            let mut m = Memory {
                cb: std::mem::size_of::<Memory>() as u32,
                ..Default::default()
            };
            assert_ne!(GetProcessMemoryInfo(GetCurrentProcess(), &mut m, m.cb), 0);
            m.working
        }
    }
}
#[cfg(not(windows))]
mod metrics {
    pub fn cpu() -> f64 {
        0.
    }
    pub fn memory() -> usize {
        0
    }
}
