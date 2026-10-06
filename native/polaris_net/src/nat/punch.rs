use super::socket::{ProbeWait, PunchSocket};
use hmac::{Hmac, Mac};
use sha2::Sha256;
use std::{
    net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddr},
    sync::Arc,
    time::Duration,
};
use tokio::{
    sync::oneshot,
    time::{interval, timeout},
};

pub fn encode(kind: u8, nonce: u32, peer: i32, target: SocketAddr, secret: &[u8; 16]) -> Vec<u8> {
    let mut out = vec![0x20];
    out.extend(b"PNPN");
    out.push(kind);
    out.extend(nonce.to_le_bytes());
    out.extend(peer.to_le_bytes());
    // 接收方看不到自身的外部NAT端口，显式携带目标地址使双方能校验同一MAC。
    match target.ip() {
        IpAddr::V4(ip) => {
            out.push(4);
            out.extend(ip.octets());
        }
        IpAddr::V6(ip) => {
            out.push(6);
            out.extend(ip.octets());
        }
    }
    out.extend(target.port().to_le_bytes());
    let mut mac = Hmac::<Sha256>::new_from_slice(secret).unwrap();
    mac.update(&out);
    out.extend(&mac.finalize().into_bytes()[..16]);
    out
}
pub fn decode(data: &[u8], secret: &[u8; 16]) -> Option<(u8, u32, i32, SocketAddr)> {
    if data.len() < 37 || data[..5] != *b"\x20PNPN" || ![1, 2].contains(&data[5]) {
        return None;
    }
    let size = match data[14] {
        4 => 4,
        6 => 16,
        _ => return None,
    };
    let end = 15 + size + 2;
    if data.len() != end + 16 {
        return None;
    }
    let mut mac = Hmac::<Sha256>::new_from_slice(secret).ok()?;
    mac.update(&data[..end]);
    // verify_slice要求完整SHA256标签，使用恒定时间的前16字节比较。
    let tag = mac.finalize().into_bytes();
    let mut difference = 0u8;
    for (a, b) in tag[..16].iter().zip(&data[end..]) {
        difference |= a ^ b;
    }
    if difference != 0 {
        return None;
    }
    let ip = if size == 4 {
        IpAddr::V4(Ipv4Addr::from(<[u8; 4]>::try_from(&data[15..19]).ok()?))
    } else {
        IpAddr::V6(Ipv6Addr::from(<[u8; 16]>::try_from(&data[15..31]).ok()?))
    };
    Some((
        data[5],
        u32::from_le_bytes(data[6..10].try_into().ok()?),
        i32::from_le_bytes(data[10..14].try_into().ok()?),
        SocketAddr::new(ip, u16::from_le_bytes(data[end - 2..end].try_into().ok()?)),
    ))
}
pub async fn punch(
    socket: Arc<PunchSocket>,
    candidates: Vec<SocketAddr>,
    secret: [u8; 16],
) -> Result<SocketAddr, String> {
    let nonce = rand::random();
    let (sender, receiver) = oneshot::channel();
    socket.probes.lock().insert(
        nonce,
        ProbeWait {
            secret,
            targets: candidates.clone(),
            sender,
        },
    );
    let run = async {
        tokio::pin!(receiver);
        let mut tick = interval(Duration::from_millis(50));
        loop {
            tokio::select! {
                result=&mut receiver=>return result.map_err(|_|"punch cancelled".to_string()),
                _=tick.tick()=>for &addr in &candidates {
                    let bytes=encode(1,nonce,socket.local_id.load(std::sync::atomic::Ordering::Acquire),addr,&secret);
                    if let Err(e)=socket.send_raw(addr,&bytes){if std::env::var_os("PN_PROXY_DEBUG").is_some(){eprintln!("probe send {addr}: {e}");}}
                }
            }
        }
    };
    let result = timeout(Duration::from_secs(6), run)
        .await
        .map_err(|_| "punch timeout".to_string())
        .and_then(|r| r);
    socket.probes.lock().remove(&nonce);
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn authenticated_probe() {
        for target in ["127.0.0.1:1234", "[::1]:2345"] {
            let target = target.parse().unwrap();
            let mut data = encode(1, 7, -1, target, &[3; 16]);
            assert_eq!(decode(&data, &[3; 16]), Some((1, 7, -1, target)));
            data[8] ^= 1;
            assert!(decode(&data, &[3; 16]).is_none());
            assert!(decode(&data, &[4; 16]).is_none());
        }
    }
}
