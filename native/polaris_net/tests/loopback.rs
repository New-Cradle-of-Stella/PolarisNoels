mod common;
use common::*;
use polaris_net::{channels::*, event::*};
use std::collections::HashMap;
#[test]
fn channels_and_boundaries() {
    let host = host(2);
    let client = node(2);
    join(&host, &client);
    drain(&client);
    let id = client
        .state
        .local_id
        .load(std::sync::atomic::Ordering::Acquire);
    assert_eq!(
        host.state.send(id, RELIABLE, &vec![0; MAX_RELIABLE + 1]),
        Err(-1)
    );
    assert_eq!(host.state.send(id, BULK, &vec![0; MAX_BULK + 1]), Err(-1));
    assert_eq!(host.state.send(id, SEQUENCED, &vec![0; 1500]), Err(-1));
    assert_eq!(host.state.send(999, RELIABLE, b""), Err(-3));
    assert_eq!(host.state.send(id, 3, b""), Err(-1));
    let expected = vec![
        (RELIABLE, vec![]),
        (RELIABLE, vec![9]),
        (RELIABLE, vec![3; MAX_RELIABLE]),
        (BULK, vec![]),
        (BULK, vec![7; 5 * 1024 * 1024]),
        (SEQUENCED, vec![5; 150]),
    ];
    for (channel, data) in &expected {
        host.state.send(id, *channel, data).unwrap();
    }
    let mut received = vec![];
    let mut progress = false;
    until(
        || {
            for ev in drain(&client) {
                if ev.header.kind == MESSAGE {
                    received.push((ev.header.channel, ev.data));
                }
                if ev.header.kind == BULK_PROGRESS {
                    assert!(ev.header.aux0 <= ev.header.aux1);
                    progress = true;
                }
            }
            received.len() == expected.len()
        },
        10,
    );
    for item in expected {
        assert!(
            received.contains(&item),
            "missing channel {} len {}",
            item.0,
            item.1.len()
        );
    }
    assert!(progress);
    let reliable: Vec<_> = received
        .iter()
        .filter(|(c, _)| *c == RELIABLE)
        .map(|(_, d)| d.len())
        .collect();
    assert_eq!(reliable, vec![0, 1, MAX_RELIABLE]);
    client.state.send(0, RELIABLE, b"return").unwrap();
    until(
        || {
            drain(&host)
                .iter()
                .any(|ev| ev.header.kind == MESSAGE && ev.data == b"return")
        },
        5,
    );
    host.state.send(id, BULK, &vec![42; MAX_BULK]).unwrap();
    until(
        || {
            drain(&client).iter().any(|e| {
                e.header.kind == MESSAGE
                    && e.header.channel == BULK
                    && e.data.len() == MAX_BULK
                    && e.data.iter().all(|b| *b == 42)
            })
        },
        20,
    );
    host.state.send(id, SEQUENCED, &[]).unwrap();
    until(
        || {
            drain(&client).iter().any(|e| {
                e.header.kind == MESSAGE && e.header.channel == SEQUENCED && e.data.is_empty()
            })
        },
        5,
    );
}
#[test]
fn bad_certificate_and_secret() {
    let host = host(3);
    let wrong = node(3);
    let addr = polaris_net::room_code::Room::decode(&code(&host))
        .unwrap()
        .candidates
        .into_iter()
        .find(|a| a.ip().is_loopback())
        .unwrap();
    wrong.join_direct(addr, Some([99; 32])).unwrap();
    until(
        || {
            drain(&wrong)
                .iter()
                .any(|e| e.header.kind == JOIN_RESULT && e.header.code == 3)
        },
        15,
    );
    let mut room = polaris_net::room_code::Room::decode(&code(&host)).unwrap();
    room.secret = [99; 16];
    let wrong = node(3);
    wrong.join(&room.encode()).unwrap();
    // Probes are authenticated: a wrong secret fails before QUIC, with a punch timeout.
    until(
        || {
            drain(&wrong)
                .iter()
                .any(|e| e.header.kind == JOIN_RESULT && e.header.code == 2)
        },
        10,
    );
}
#[test]
fn reliable_order_under_loss() {
    let host = host(2);
    let mut room = polaris_net::room_code::Room::decode(&code(&host)).unwrap();
    let actual = room
        .candidates
        .iter()
        .find(|a| a.ip().is_loopback())
        .copied()
        .unwrap();
    let proxy = netem::Proxy::new(
        actual,
        netem::Impairment {
            delay_ms: 30,
            jitter_ms: 10,
            loss_percent: 15,
            reorder_percent: 20,
            ..Default::default()
        },
    );
    room.candidates = vec![proxy.address];
    let client = node(2);
    client.join(&room.encode()).unwrap();
    until(
        || {
            drain(&client)
                .iter()
                .any(|e| e.header.kind == JOIN_RESULT && e.header.code == 0)
        },
        20,
    );
    drain(&host);
    let id = client
        .state
        .local_id
        .load(std::sync::atomic::Ordering::Acquire);
    for seq in 0u32..200 {
        host.state.send(id, RELIABLE, &seq.to_le_bytes()).unwrap();
    }
    let mut last = None;
    let mut counts = HashMap::new();
    until(
        || {
            for ev in drain(&client) {
                if ev.header.kind == MESSAGE {
                    let seq = u32::from_le_bytes(ev.data.try_into().unwrap());
                    assert_eq!(seq, last.map_or(0, |n| n + 1));
                    last = Some(seq);
                    *counts.entry(seq).or_insert(0) += 1;
                }
            }
            counts.len() == 200
        },
        20,
    );
    assert!(counts.values().all(|n| *n == 1));
}
