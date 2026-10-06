use super::socket::{PunchSocket, StunWait};
use quinn::AsyncUdpSocket;
use rand::RngCore;
use std::{
    collections::HashSet,
    net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddr},
    sync::Arc,
    time::{Duration, Instant},
};
use tokio::{net::lookup_host, sync::oneshot, time::timeout};

pub fn parse_response(data: &[u8], transaction: [u8; 12]) -> Result<SocketAddr, String> {
    if data.len() < 20
        || data[..2] != [1, 1]
        || data[4..8] != [0x21, 0x12, 0xa4, 0x42]
        || data[8..20] != transaction
    {
        return Err("STUN header".into());
    }
    let end = 20 + u16::from_be_bytes([data[2], data[3]]) as usize;
    if end > data.len() || !(end - 20).is_multiple_of(4) {
        return Err("STUN length".into());
    }
    let mut pos = 20;
    while pos + 4 <= end {
        let kind = u16::from_be_bytes([data[pos], data[pos + 1]]);
        let len = u16::from_be_bytes([data[pos + 2], data[pos + 3]]) as usize;
        pos += 4;
        if pos + len > end {
            return Err("STUN attribute".into());
        }
        if (kind == 0x20 || kind == 1) && len >= 4 {
            let xor = kind == 0x20;
            let port =
                u16::from_be_bytes([data[pos + 2], data[pos + 3]]) ^ if xor { 0x2112 } else { 0 };
            let ip = match data[pos + 1] {
                1 if len == 8 => {
                    let mut octets: [u8; 4] = data[pos + 4..pos + 8].try_into().unwrap();
                    if xor {
                        for (b, m) in octets.iter_mut().zip([0x21, 0x12, 0xa4, 0x42]) {
                            *b ^= m;
                        }
                    }
                    IpAddr::V4(Ipv4Addr::from(octets))
                }
                2 if len == 20 => {
                    let mut octets: [u8; 16] = data[pos + 4..pos + 20].try_into().unwrap();
                    let mut mask = vec![0x21, 0x12, 0xa4, 0x42];
                    mask.extend(transaction);
                    if xor {
                        for (b, m) in octets.iter_mut().zip(mask) {
                            *b ^= m;
                        }
                    }
                    IpAddr::V6(Ipv6Addr::from(octets))
                }
                _ => return Err("STUN address family".into()),
            };
            if port == 0 || ip.is_unspecified() || ip.is_multicast() {
                return Err("STUN invalid mapped address".into());
            }
            return Ok(SocketAddr::new(ip, port));
        }
        pos += (len + 3) & !3;
    }
    Err("STUN mapped address missing".into())
}

pub async fn binding(socket: Arc<PunchSocket>, server: SocketAddr) -> Result<SocketAddr, String> {
    let mut transaction = [0; 12];
    rand::thread_rng().fill_bytes(&mut transaction);
    let mut request = vec![0, 1, 0, 0, 0x21, 0x12, 0xa4, 0x42];
    request.extend(transaction);
    let (sender, mut receiver) = oneshot::channel();
    socket.stuns.lock().insert(
        transaction,
        StunWait {
            target: server,
            sender,
        },
    );
    let mut result = Err("STUN timeout".into());
    for _ in 0..3 {
        let _ = socket.send_raw(server, &request);
        if let Ok(Ok(mapped)) = timeout(Duration::from_millis(200), &mut receiver).await {
            result = Ok(mapped);
            break;
        }
    }
    socket.stuns.lock().remove(&transaction);
    result
}
#[derive(Clone, Debug)]
pub struct Candidates {
    pub addresses: Vec<SocketAddr>,
    pub nat: String,
    pub rtt: u32,
}
pub async fn collect(socket: Arc<PunchSocket>, lan: bool, stun: bool, servers: &str) -> Candidates {
    let port = socket.local_addr().unwrap().port();
    let mut addresses = Vec::new();
    if lan {
        if let Ok(interfaces) = if_addrs::get_if_addrs() {
            for interface in interfaces {
                let name = interface.name.to_ascii_lowercase();
                let ip = interface.ip();
                if interface.is_loopback()
                    || name.contains("vethernet")
                    || name.contains("vmware")
                    || ip.is_unspecified()
                    || matches!(ip,IpAddr::V4(v) if v.is_link_local())
                    || matches!(ip,IpAddr::V6(v) if v.is_unicast_link_local())
                {
                    continue;
                }
                let addr = SocketAddr::new(ip, port);
                if !addresses.contains(&addr) && addresses.len() < 5 {
                    addresses.push(addr);
                }
            }
        }
    }
    // 单机测试及无活动网卡时仍允许显式本机入房；不冒充公网候选。
    addresses.push(SocketAddr::new(Ipv4Addr::LOCALHOST.into(), port));
    let started = Instant::now();
    let mut mapped = Vec::new();
    let mut ips = HashSet::new();
    if stun {
        let mut tasks = Vec::new();
        for server in servers.split(',').take(8) {
            let server = server.trim().to_owned();
            let socket = socket.clone();
            tasks.push(tokio::spawn(async move {
                let found = timeout(Duration::from_secs(1), lookup_host(&server))
                    .await
                    .ok()?
                    .ok()?;
                let targets: Vec<_> = found.collect();
                let target = *targets.iter().find(|a| a.is_ipv4()).or(targets.first())?;
                binding(socket, target)
                    .await
                    .ok()
                    .map(|addr| (target.ip(), addr))
            }));
        }
        for task in tasks {
            if let Ok(Some((ip, addr))) = task.await {
                if ips.insert(ip) {
                    mapped.push(addr);
                }
                if !addresses.contains(&addr) && addresses.len() < 8 {
                    addresses.push(addr);
                }
            }
        }
    }
    let nat = if mapped.len() >= 2 {
        if mapped.windows(2).any(|pair| pair[0] != pair[1]) {
            "symmetric"
        } else {
            "cone"
        }
    } else if mapped.len() == 1 {
        "unknown"
    } else if stun {
        "blocked"
    } else {
        "lan"
    };
    Candidates {
        addresses,
        nat: nat.into(),
        rtt: started.elapsed().as_millis() as u32,
    }
}
