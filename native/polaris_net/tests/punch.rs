mod common;
#[path = "common/nat.rs"]
mod nat;
use common::*;
use nat::*;
use polaris_net::{event::*, runtime::Node, session::Config};
fn behind(nat: &Nat, stun: &Stun) -> Node {
    Node::with_socket(
        Config {
            max_peers: 3,
            enable_stun: true,
            enable_lan: false,
            stun_servers: stun.servers.clone(),
            ..Config::default()
        },
        |udp| nat.wrap(udp),
    )
    .unwrap()
}
#[test]
fn restricted_cones_mesh_via_reachable_host() {
    let stun = Stun::new();
    let host = host(3);
    let a_nat = Nat::new(Mode::Restricted);
    let b_nat = Nat::new(Mode::Restricted);
    let a = behind(&a_nat, &stun);
    let b = behind(&b_nat, &stun);
    join(&host, &a);
    join(&host, &b);
    // The remote listener may consume MemberInfo one tick after the dialer joins.
    until(
        || a.state.peers.read().len() == 2 && b.state.peers.read().len() == 2,
        2,
    );
    a.state.send(-1, 0, b"NAT mesh").unwrap();
    until(
        || {
            drain(&b)
                .iter()
                .any(|e| e.header.kind == MESSAGE && e.data == b"NAT mesh")
        },
        5,
    );
}
#[test]
fn full_cone_host_room_code() {
    let stun = Stun::new();
    let h_nat = Nat::new(Mode::FullCone);
    let c_nat = Nat::new(Mode::Restricted);
    let host = behind(&h_nat, &stun);
    host.host().unwrap();
    until(|| host.state.code.lock().is_some(), 5);
    let client = behind(&c_nat, &stun);
    join(&host, &client);
    assert_eq!(client.state.peers.read().len(), 1);
}
#[test]
fn symmetric_nat_reports_type_and_mesh_timeout() {
    let stun = Stun::new();
    let host = host(3);
    let a_nat = Nat::new(Mode::Symmetric);
    let b_nat = Nat::new(Mode::Symmetric);
    let a = behind(&a_nat, &stun);
    let b = behind(&b_nat, &stun);
    join(&host, &a);
    b.join(&code(&host)).unwrap();
    let mut symmetric = false;
    let mut failed = false;
    until(
        || {
            for ev in drain(&b) {
                if ev.header.kind == NAT_STATE {
                    symmetric = String::from_utf8(ev.data).unwrap().contains("symmetric");
                }
                if ev.header.kind == JOIN_RESULT {
                    assert_eq!(ev.header.code, 2);
                    failed = true;
                }
            }
            failed
        },
        25,
    );
    assert!(symmetric);
}
#[test]
fn restricted_host_requires_external_signalling() {
    let stun = Stun::new();
    let h_nat = Nat::new(Mode::Restricted);
    let c_nat = Nat::new(Mode::Restricted);
    let host = behind(&h_nat, &stun);
    host.host().unwrap();
    until(|| host.state.code.lock().is_some(), 5);
    let client = behind(&c_nat, &stun);
    client.join(&code(&host)).unwrap();
    until(
        || {
            drain(&client)
                .iter()
                .any(|e| e.header.kind == JOIN_RESULT && e.header.code == 2)
        },
        10,
    );
}
