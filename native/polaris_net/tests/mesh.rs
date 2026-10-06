mod common;
use common::*;
use polaris_net::{channels::RELIABLE, event::*, session::*};
use std::{collections::HashSet, sync::atomic::Ordering, time::Instant};
#[test]
fn five_members_leave_kick_and_host_close() {
    let mut host = host(5);
    let clients: Vec<_> = (0..4).map(|_| node(5)).collect();
    for client in &clients {
        client.join(&code(&host)).unwrap();
    }
    let mut joined = HashSet::new();
    until(
        || {
            for (i, c) in clients.iter().enumerate() {
                for ev in drain(c) {
                    if ev.header.kind == JOIN_RESULT {
                        assert_eq!(ev.header.code, 0);
                        joined.insert(i);
                    }
                }
            }
            drain(&host);
            joined.len() == 4
        },
        25,
    );
    let mut ids = HashSet::new();
    ids.insert(0);
    for c in &clients {
        assert_eq!(c.state.peers.read().len(), 4);
        ids.insert(c.state.local_id.load(Ordering::Acquire));
    }
    assert_eq!(ids.len(), 5);
    assert_eq!(host.state.peers.read().len(), 4);
    for c in clients.iter().chain(std::iter::once(&host)) {
        let id = c.state.local_id.load(Ordering::Acquire);
        c.state.send(-1, RELIABLE, &id.to_le_bytes()).unwrap();
    }
    let mut received: Vec<HashSet<i32>> = (0..5).map(|_| HashSet::new()).collect();
    until(
        || {
            for (i, c) in clients.iter().chain(std::iter::once(&host)).enumerate() {
                for ev in drain(c) {
                    if ev.header.kind == MESSAGE {
                        let sent = i32::from_le_bytes(ev.data.try_into().unwrap());
                        assert_eq!(sent, ev.header.peer);
                        received[i].insert(sent);
                    }
                }
            }
            received.iter().all(|r| r.len() == 4)
        },
        5,
    );
    let departed = clients[0].state.local_id.load(Ordering::Acquire);
    let started = Instant::now();
    clients[0].state.disconnect(0, LOCAL).unwrap();
    until(
        || {
            for c in clients[1..].iter().chain(std::iter::once(&host)) {
                drain(c);
            }
            clients[1..]
                .iter()
                .chain(std::iter::once(&host))
                .all(|c| c.state.peers.read().len() == 3)
        },
        2,
    );
    assert!(started.elapsed().as_secs_f64() < 1.0);
    assert!(!host.state.peers.read().contains_key(&departed));
    let id = clients[1].state.local_id.load(Ordering::Acquire);
    host.state.disconnect(id, KICKED).unwrap();
    until(
        || {
            drain(&clients[1]).iter().any(|e| {
                if e.header.kind == DISCONNECTED {
                    eprintln!(
                        "kick observed peer={} reason={}",
                        e.header.peer, e.header.code
                    );
                }
                e.header.kind == DISCONNECTED && e.header.peer == 0 && e.header.code == KICKED
            })
        },
        3,
    );
    host.shutdown();
    for c in &clients[2..] {
        until(
            || {
                drain(c).iter().any(|e| {
                    if e.header.kind == DISCONNECTED {
                        eprintln!(
                            "close observed peer={} reason={}",
                            e.header.peer, e.header.code
                        );
                    }
                    e.header.kind == DISCONNECTED
                        && e.header.peer == 0
                        && e.header.code == HOST_CLOSED
                })
            },
            3,
        );
    }
}
#[test]
fn room_full_and_ids_are_not_reused() {
    let host = host(2);
    let first = node(2);
    join(&host, &first);
    let old = first.state.local_id.load(Ordering::Acquire);
    let extra = node(2);
    extra.join(&code(&host)).unwrap();
    until(
        || {
            drain(&extra)
                .iter()
                .any(|e| e.header.kind == JOIN_RESULT && e.header.code == 4)
        },
        15,
    );
    first.state.disconnect(0, LOCAL).unwrap();
    until(|| host.state.peers.read().is_empty(), 3);
    let next = node(2);
    join(&host, &next);
    assert!(next.state.local_id.load(Ordering::Acquire) > old);
}
