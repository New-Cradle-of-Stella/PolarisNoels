#![allow(dead_code)]
pub mod netem;
use polaris_net::{
    event::{Event, JOIN_RESULT, LOG},
    runtime::Node,
    session::Config,
};
use std::{
    thread,
    time::{Duration, Instant},
};
pub fn node(max: u16) -> Node {
    Node::new(Config {
        max_peers: max,
        enable_stun: false,
        ..Config::default()
    })
    .unwrap()
}
pub fn drain(node: &Node) -> Vec<Event> {
    let mut out = vec![];
    while let Some(event) = node.state.events.poll(64 * 1024 * 1024).unwrap() {
        if event.header.kind == LOG {
            eprintln!("native: {}", String::from_utf8_lossy(&event.data));
        }
        out.push(event);
    }
    out
}
pub fn until(mut action: impl FnMut() -> bool, seconds: u64) {
    let deadline = Instant::now() + Duration::from_secs(seconds);
    loop {
        if action() {
            return;
        }
        assert!(Instant::now() < deadline, "deadline exceeded ({seconds}s)");
        thread::sleep(Duration::from_millis(5));
    }
}
pub fn host(max: u16) -> Node {
    let n = node(max);
    n.host().unwrap();
    until(|| n.state.code.lock().is_some(), 5);
    drain(&n);
    n
}
pub fn code(host: &Node) -> String {
    host.state.code.lock().clone().unwrap()
}
pub fn join(host: &Node, client: &Node) {
    client.join(&code(host)).unwrap();
    let mut joined = false;
    until(
        || {
            for ev in drain(client) {
                if ev.header.kind == JOIN_RESULT {
                    assert_eq!(ev.header.code, 0);
                    joined = true;
                }
            }
            drain(host);
            joined
        },
        20,
    );
}
