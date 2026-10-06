mod common;
use common::*;
use polaris_net::{
    event::{Header, JOIN_RESULT, MESSAGE, NAT_STATE},
    ffi::*,
};
use std::{ptr, thread, time::Duration};
unsafe fn poll() -> Option<(Header, Vec<u8>)> {
    let mut ev = Header::default();
    let mut buf = vec![0; 8 * 1024 * 1024];
    let rc = pn_poll(&mut ev, buf.as_mut_ptr(), buf.len() as u32);
    assert!(rc >= 0, "pn_poll: {rc}");
    if rc == 0 {
        None
    } else {
        buf.truncate(ev.len as usize);
        Some((ev, buf))
    }
}
#[test]
fn abi_roundtrip_buffers_and_hundred_sessions() {
    unsafe {
        pn_shutdown();
        let config = PnConfig {
            abi_version: 1,
            bind_port: 0,
            max_peers: 2,
            enable_stun: 0,
            enable_lan: 1,
            enable_upnp: 0,
            reserved0: 0,
            stun_servers: ptr::null(),
            idle_timeout_ms: 20000,
            reserved1: 0,
        };
        assert_eq!(pn_init(ptr::null()), -1);
        assert_eq!(pn_abi_version(), 1);
        assert_eq!(pn_send(0, 0, ptr::null(), 1), -1);
        // Warm provider/IO caches before taking the lifecycle baseline.
        assert_eq!(pn_init(&config), 0);
        pn_shutdown();
        thread::sleep(Duration::from_millis(100));
        let cold = counts();
        let mut baseline = cold;
        for cycle in 0..100 {
            assert_eq!(pn_init(&config), 0);
            assert_eq!(pn_init(&config), -2);
            assert_eq!(pn_host_start(), 0);
            let mut ev = Header::default();
            until(
                || {
                    let rc = pn_poll(&mut ev, ptr::null_mut(), 0);
                    if rc == -4 {
                        assert_eq!(ev.kind, NAT_STATE);
                        assert!(ev.len > 0);
                        true
                    } else {
                        assert!(rc >= 0);
                        false
                    }
                },
                5,
            );
            let (nat, _) = poll().unwrap();
            assert_eq!(nat.kind, NAT_STATE);
            let mut len = 0;
            assert_eq!(pn_room_code(ptr::null_mut(), 0, &mut len), -4);
            let mut code = vec![0; len as usize];
            assert_eq!(pn_room_code(code.as_mut_ptr(), len, &mut len), 0);
            let client = node(2);
            client.join(std::str::from_utf8(&code).unwrap()).unwrap();
            until(
                || {
                    while poll().is_some() {}
                    drain(&client)
                        .iter()
                        .any(|e| e.header.kind == JOIN_RESULT && e.header.code == 0)
                },
                10,
            );
            assert_eq!(pn_local_peer_id(), 0);
            assert_eq!(pn_peer_count(), 1);
            let id = client
                .state
                .local_id
                .load(std::sync::atomic::Ordering::Acquire);
            let mut stats = PeerStats::default();
            assert_eq!(pn_peer_stats(id, &mut stats), 0);
            assert_eq!(stats.path_kind, 1);
            assert_eq!(pn_peer_stats(999, &mut stats), -3);
            let payload = (cycle as u32).to_le_bytes();
            assert_eq!(pn_send(id, 0, payload.as_ptr(), 4), 0);
            until(
                || {
                    drain(&client)
                        .iter()
                        .any(|e| e.header.kind == MESSAGE && e.data == payload)
                },
                5,
            );
            client.state.send(0, 0, &payload).unwrap();
            until(
                || {
                    let rc = pn_poll(&mut ev, ptr::null_mut(), 0);
                    if rc == -4 {
                        assert_eq!(ev.kind, MESSAGE);
                        true
                    } else {
                        assert!(rc >= 0);
                        false
                    }
                },
                5,
            );
            let (header, data) = poll().unwrap();
            assert_eq!(header.peer, id);
            assert_eq!(data, payload);
            if cycle == 0 {
                let bad = b"PN1-invalid";
                assert_eq!(pn_join(bad.as_ptr(), bad.len() as u32), -2);
            }
            pn_shutdown();
            assert_eq!(pn_peer_count(), 0);
            assert_eq!(pn_local_peer_id(), -1);
            drop(client);
            if cycle == 9 {
                thread::sleep(Duration::from_millis(100));
                baseline = counts();
            }
            if cycle % 10 == 9 {
                eprintln!("cycle{} handles/threads={:?}", cycle + 1, counts());
            }
        }
        thread::sleep(Duration::from_millis(500));
        let after = counts();
        eprintln!("100 lifecycle cold={cold:?}, warm baseline handles/threads={baseline:?}, after={after:?}");
        assert!(after.0 <= baseline.0 + 3);
        assert!(after.1 <= baseline.1);
        assert_eq!(pn_init(&config), 0);
        let bad = b"PN1-invalid";
        assert_eq!(pn_join(bad.as_ptr(), bad.len() as u32), 0);
        until(
            || {
                while let Some((ev, _)) = poll() {
                    if ev.kind == JOIN_RESULT {
                        assert_eq!(ev.code, 1);
                        return true;
                    }
                }
                false
            },
            3,
        );
        pn_shutdown();
    }
}
#[cfg(windows)]
fn counts() -> (u32, u32) {
    #[repr(C)]
    struct ThreadEntry {
        size: u32,
        usage: u32,
        id: u32,
        owner: u32,
        base: i32,
        delta: i32,
        flags: u32,
    }
    unsafe extern "system" {
        fn GetCurrentProcess() -> *mut std::ffi::c_void;
        fn GetCurrentProcessId() -> u32;
        fn GetProcessHandleCount(h: *mut std::ffi::c_void, n: *mut u32) -> i32;
        fn CreateToolhelp32Snapshot(flags: u32, id: u32) -> *mut std::ffi::c_void;
        fn Thread32First(h: *mut std::ffi::c_void, e: *mut ThreadEntry) -> i32;
        fn Thread32Next(h: *mut std::ffi::c_void, e: *mut ThreadEntry) -> i32;
        fn CloseHandle(h: *mut std::ffi::c_void) -> i32;
    }
    unsafe {
        let mut handles = 0;
        assert_ne!(GetProcessHandleCount(GetCurrentProcess(), &mut handles), 0);
        let snapshot = CreateToolhelp32Snapshot(4, 0);
        assert_ne!(snapshot as isize, -1);
        let pid = GetCurrentProcessId();
        let mut e = ThreadEntry {
            size: 28,
            usage: 0,
            id: 0,
            owner: 0,
            base: 0,
            delta: 0,
            flags: 0,
        };
        let mut threads = 0;
        let mut found = Thread32First(snapshot, &mut e);
        while found != 0 {
            if e.owner == pid {
                threads += 1;
            }
            found = Thread32Next(snapshot, &mut e);
        }
        CloseHandle(snapshot);
        (handles, threads)
    }
}
#[cfg(not(windows))]
fn counts() -> (u32, u32) {
    (0, 0)
}
