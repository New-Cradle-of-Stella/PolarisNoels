//! C ABI的有效指针/长度由调用方保证；详见include/polaris_net.h。
#![allow(clippy::missing_safety_doc)]
use crate::{
    event::{Event, Header},
    runtime::Node,
    session::Config,
};
use parking_lot::Mutex;
use std::{
    cell::RefCell,
    ffi::{c_char, CStr},
    panic::{catch_unwind, AssertUnwindSafe},
    ptr,
    sync::{atomic::Ordering, Arc},
};

static INSTANCE: Mutex<Option<Node>> = Mutex::new(None);
thread_local! {static LAST_ERROR:RefCell<String>=const{RefCell::new(String::new())};}
#[repr(C)]
pub struct PnConfig {
    pub abi_version: u32,
    pub bind_port: u16,
    pub max_peers: u16,
    pub enable_stun: u8,
    pub enable_lan: u8,
    pub enable_upnp: u8,
    pub reserved0: u8,
    pub stun_servers: *const c_char,
    pub idle_timeout_ms: u32,
    pub reserved1: u32,
}
#[repr(C)]
#[derive(Default)]
pub struct PeerStats {
    pub rtt_ms: u32,
    pub loss_permille: u32,
    pub bytes_sent: u64,
    pub bytes_recv: u64,
    pub send_queue_bytes: u32,
    pub path_kind: u8,
    pub reserved: [u8; 3],
}
fn error(code: i32, text: impl Into<String>) -> i32 {
    LAST_ERROR.with(|slot| *slot.borrow_mut() = text.into());
    code
}
fn guarded(action: impl FnOnce() -> i32) -> i32 {
    match catch_unwind(AssertUnwindSafe(action)) {
        Ok(value) => value,
        Err(_) => error(-99, "native panic contained at C ABI boundary"),
    }
}
fn state() -> Result<Arc<crate::session::State>, i32> {
    INSTANCE
        .lock()
        .as_ref()
        .map(|n| n.state.clone())
        .ok_or_else(|| error(-2, "transport not initialized"))
}
unsafe fn bytes<'a>(data: *const u8, len: u32) -> Result<&'a [u8], i32> {
    if len == 0 {
        Ok(&[])
    } else if data.is_null() || len as usize > crate::channels::MAX_BULK {
        Err(error(-1, "invalid data pointer/length"))
    } else {
        Ok(std::slice::from_raw_parts(data, len as usize))
    }
}
unsafe fn output(data: &[u8], out: *mut u8, cap: u32, out_len: *mut u32) -> i32 {
    if out_len.is_null() {
        return error(-1, "out_len is null");
    }
    ptr::write(out_len, data.len() as u32);
    if data.len() > cap as usize {
        return -4;
    }
    if !data.is_empty() {
        if out.is_null() {
            return error(-1, "output buffer is null");
        }
        ptr::copy_nonoverlapping(data.as_ptr(), out, data.len());
    }
    0
}

#[no_mangle]
pub extern "C" fn pn_abi_version() -> i32 {
    guarded(|| 1)
}
#[no_mangle]
pub unsafe extern "C" fn pn_init(cfg: *const PnConfig) -> i32 {
    guarded(|| {
        if cfg.is_null() {
            return error(-1, "configuration is null");
        }
        let cfg = &*cfg;
        if cfg.abi_version != 1
            || !(2..=8).contains(&cfg.max_peers)
            || cfg.enable_stun > 1
            || cfg.enable_lan > 1
            || cfg.enable_upnp > 1
            || cfg.reserved0 != 0
            || cfg.reserved1 != 0
        {
            return error(-1, "unsupported ABI or invalid configuration");
        }
        let mut slot = INSTANCE.lock();
        if slot.is_some() {
            return error(-2, "transport already initialized");
        }
        let servers = if cfg.stun_servers.is_null() {
            crate::session::DEFAULT_STUN.to_string()
        } else {
            match CStr::from_ptr(cfg.stun_servers).to_str() {
                Ok(s) if s.len() <= 4096 => s.to_owned(),
                _ => return error(-1, "invalid STUN server list"),
            }
        };
        let config = Config {
            bind_port: cfg.bind_port,
            max_peers: cfg.max_peers,
            enable_stun: cfg.enable_stun != 0,
            enable_lan: cfg.enable_lan != 0,
            stun_servers: servers,
            idle_timeout_ms: if cfg.idle_timeout_ms == 0 {
                20000
            } else {
                cfg.idle_timeout_ms
            },
        };
        match Node::new(config) {
            Ok(node) => {
                *slot = Some(node);
                0
            }
            Err(e) => error(-99, e),
        }
    })
}
#[no_mangle]
pub extern "C" fn pn_shutdown() {
    let _ = guarded(|| {
        let node = INSTANCE.lock().take();
        drop(node);
        0
    });
}
#[no_mangle]
pub extern "C" fn pn_host_start() -> i32 {
    guarded(|| {
        let slot = INSTANCE.lock();
        let Some(node) = slot.as_ref() else {
            return error(-2, "transport not initialized");
        };
        match node.host() {
            Ok(()) => 0,
            Err(e) => error(-2, e),
        }
    })
}
#[no_mangle]
pub unsafe extern "C" fn pn_room_code(out: *mut u8, cap: u32, out_len: *mut u32) -> i32 {
    guarded(|| {
        let s = match state() {
            Ok(s) => s,
            Err(e) => return e,
        };
        let code = s.code.lock().clone();
        match code {
            Some(code) => output(code.as_bytes(), out, cap, out_len),
            None => error(-2, "room code not ready"),
        }
    })
}
#[no_mangle]
pub unsafe extern "C" fn pn_join(code: *const u8, len: u32) -> i32 {
    guarded(|| {
        let raw = match bytes(code, len) {
            Ok(b) => b,
            Err(e) => return e,
        };
        let code = match std::str::from_utf8(raw) {
            Ok(s) if s.len() <= 8192 => s,
            _ => return error(-1, "invalid UTF-8 room code"),
        };
        let slot = INSTANCE.lock();
        let Some(node) = slot.as_ref() else {
            return error(-2, "transport not initialized");
        };
        match node.join(code) {
            Ok(()) => 0,
            Err(e) => {
                if node.state.phase.load(Ordering::Acquire) != 0 {
                    error(-2, e)
                } else {
                    node.state.events.log(2, format!("invalid room code: {e}"));
                    node.state.phase.store(2, Ordering::Release);
                    // 错误入房码仍由异步结果事件统一通知UI，调用本身已被受理。
                    node.emit(Event::new(crate::event::JOIN_RESULT, 0, 0, 1, vec![]));
                    0
                }
            }
        }
    })
}
#[no_mangle]
pub unsafe extern "C" fn pn_join_direct(
    host: *const c_char,
    port: u16,
    fingerprint32: *const u8,
) -> i32 {
    guarded(|| {
        if host.is_null() || port == 0 {
            return error(-1, "invalid direct address");
        }
        let host = match CStr::from_ptr(host).to_str() {
            Ok(s) if !s.is_empty() && s.len() <= 4096 => s.to_string(),
            _ => return error(-1, "invalid direct hostname"),
        };
        let fp = if fingerprint32.is_null() {
            None
        } else {
            Some(
                std::slice::from_raw_parts(fingerprint32, 32)
                    .try_into()
                    .unwrap(),
            )
        };
        let slot = INSTANCE.lock();
        let Some(node) = slot.as_ref() else {
            return error(-2, "transport not initialized");
        };
        match node.spawn_direct_host_lookup(host, port, fp) {
            Ok(()) => 0,
            Err(e) => error(-2, e),
        }
    })
}
#[no_mangle]
pub unsafe extern "C" fn pn_send(peer: i32, channel: i32, data: *const u8, len: u32) -> i32 {
    guarded(|| {
        let data = match bytes(data, len) {
            Ok(b) => b,
            Err(e) => return e,
        };
        let state = match state() {
            Ok(s) => s,
            Err(e) => return e,
        };
        match state.send(peer, channel, data) {
            Ok(()) => 0,
            Err(code) => error(
                code,
                match code {
                    -5 => "send queue full",
                    -3 => "peer not connected",
                    -1 => "invalid channel or packet length",
                    _ => "transport closing or datagram send failed",
                },
            ),
        }
    })
}
#[no_mangle]
pub extern "C" fn pn_disconnect(peer: i32, reason: i32) -> i32 {
    guarded(|| {
        let state = match state() {
            Ok(s) => s,
            Err(e) => return e,
        };
        match state.disconnect(peer, reason) {
            Ok(()) => 0,
            Err(e) => error(e, "disconnect rejected"),
        }
    })
}
#[no_mangle]
pub unsafe extern "C" fn pn_poll(ev: *mut Header, buf: *mut u8, cap: u32) -> i32 {
    guarded(|| {
        if ev.is_null() || (cap != 0 && buf.is_null()) {
            return error(-1, "invalid event buffer");
        }
        let state = match state() {
            Ok(s) => s,
            Err(e) => return e,
        };
        match state.events.poll(cap as usize) {
            Ok(Some(event)) => {
                ptr::write(ev, event.header);
                if !event.data.is_empty() {
                    ptr::copy_nonoverlapping(event.data.as_ptr(), buf, event.data.len());
                }
                1
            }
            Ok(None) => {
                ptr::write(ev, Header::default());
                0
            }
            Err(header) => {
                ptr::write(ev, header);
                -4
            }
        }
    })
}
#[no_mangle]
pub unsafe extern "C" fn pn_peer_stats(peer: i32, out: *mut PeerStats) -> i32 {
    guarded(|| {
        if out.is_null() {
            return error(-1, "stats buffer is null");
        }
        let state = match state() {
            Ok(s) => s,
            Err(e) => return e,
        };
        let p = match state.peers.read().get(&peer).cloned() {
            Some(p) => p,
            None => return error(-3, "peer not found"),
        };
        let stats = p.connection.stats();
        let address = crate::nat::socket::normalized(p.connection.remote_address());
        let lan = address.ip().is_loopback()
            || matches!(address.ip(),std::net::IpAddr::V4(v) if v.is_private())
            || matches!(address.ip(),std::net::IpAddr::V6(v) if v.is_unique_local());
        let output = PeerStats {
            rtt_ms: stats.path.rtt.as_millis().min(u32::MAX as u128) as u32,
            loss_permille: (stats.path.lost_packets.saturating_mul(1000)
                / stats.path.sent_packets.max(1))
            .min(1000) as u32,
            bytes_sent: stats.udp_tx.bytes,
            bytes_recv: stats.udp_rx.bytes,
            send_queue_bytes: p.queued_bytes(),
            path_kind: if lan { 1 } else { 2 },
            reserved: [0; 3],
        };
        ptr::write(out, output);
        0
    })
}
#[no_mangle]
pub extern "C" fn pn_local_peer_id() -> i32 {
    guarded(|| {
        INSTANCE
            .lock()
            .as_ref()
            .map_or(-1, |n| n.state.local_id.load(Ordering::Acquire))
    })
}
#[no_mangle]
pub extern "C" fn pn_peer_count() -> i32 {
    guarded(|| {
        INSTANCE
            .lock()
            .as_ref()
            .map_or(0, |n| n.state.peers.read().len() as i32)
    })
}
#[no_mangle]
pub unsafe extern "C" fn pn_last_error(out: *mut u8, cap: u32, out_len: *mut u32) -> i32 {
    guarded(|| {
        let message = LAST_ERROR.with(|s| s.borrow().clone());
        output(message.as_bytes(), out, cap, out_len)
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn panic_is_contained() {
        assert_eq!(guarded(|| panic!("FFI test hook")), -99);
        assert_eq!(pn_abi_version(), 1);
        LAST_ERROR.with(|e| assert!(e.borrow().contains("panic contained")));
    }
    #[test]
    fn abi_layout() {
        assert_eq!(std::mem::size_of::<PnConfig>(), 32);
        assert_eq!(std::mem::offset_of!(PnConfig, stun_servers), 16);
        assert_eq!(std::mem::size_of::<Header>(), 32);
        assert_eq!(std::mem::size_of::<PeerStats>(), 32);
        assert_eq!(std::mem::offset_of!(PeerStats, path_kind), 28);
    }
}
