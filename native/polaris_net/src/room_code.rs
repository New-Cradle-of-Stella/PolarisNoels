use crc::{Crc, CRC_16_IBM_3740};
use rand::RngCore;
use serde::{Deserialize, Serialize};
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddr};

const ALPHABET: &[u8; 32] = b"0123456789ABCDEFGHJKMNPQRSTVWXYZ";

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Room {
    pub secret: [u8; 16],
    pub fingerprint: [u8; 32],
    pub candidates: Vec<SocketAddr>,
    pub symmetric: bool,
}

impl Room {
    pub fn new(fingerprint: [u8; 32], candidates: Vec<SocketAddr>, symmetric: bool) -> Self {
        let mut secret = [0; 16];
        rand::thread_rng().fill_bytes(&mut secret);
        Self {
            secret,
            fingerprint,
            candidates,
            symmetric,
        }
    }
    pub fn encode(&self) -> String {
        let mut bytes = vec![1];
        bytes.extend(self.secret);
        bytes.extend(self.fingerprint);
        bytes.push(self.candidates.len().min(8) as u8);
        for addr in self.candidates.iter().take(8) {
            match addr.ip() {
                IpAddr::V4(ip) => {
                    bytes.push(4);
                    bytes.extend(ip.octets());
                }
                IpAddr::V6(ip) => {
                    bytes.push(6);
                    bytes.extend(ip.octets());
                }
            }
            bytes.extend(addr.port().to_le_bytes());
        }
        bytes.push(u8::from(self.symmetric));
        bytes.extend(
            Crc::<u16>::new(&CRC_16_IBM_3740)
                .checksum(&bytes)
                .to_le_bytes(),
        );
        let mut out = String::from("PN1-");
        let mut bits = 0u32;
        let mut count = 0;
        for byte in bytes {
            bits = (bits << 8) | u32::from(byte);
            count += 8;
            while count >= 5 {
                count -= 5;
                out.push(ALPHABET[((bits >> count) & 31) as usize] as char);
            }
        }
        if count > 0 {
            out.push(ALPHABET[((bits << (5 - count)) & 31) as usize] as char);
        }
        out
    }
    pub fn decode(code: &str) -> Result<Self, String> {
        let clean: String = code.chars().filter(|c| !c.is_whitespace()).collect();
        let text = clean.strip_prefix("PN1-").ok_or("room code version")?;
        if text.len() > 400 {
            return Err("room code too long".into());
        }
        let mut data = Vec::new();
        let mut bits = 0u32;
        let mut count = 0;
        for c in text.bytes() {
            let v = ALPHABET
                .iter()
                .position(|b| *b == c.to_ascii_uppercase())
                .ok_or("invalid room code character")?;
            bits = (bits << 5) | v as u32;
            count += 5;
            if count >= 8 {
                count -= 8;
                data.push((bits >> count) as u8);
            }
        }
        if count != 0 && (bits & ((1 << count) - 1)) != 0 {
            return Err("room code padding".into());
        }
        if data.len() < 53 || text.len() != (data.len() * 8).div_ceil(5) {
            return Err("room code truncated or noncanonical".into());
        }
        let n = data.len();
        let checksum = u16::from_le_bytes([data[n - 2], data[n - 1]]);
        if Crc::<u16>::new(&CRC_16_IBM_3740).checksum(&data[..n - 2]) != checksum {
            return Err("room code checksum".into());
        }
        if data[0] != 1 {
            return Err("room code version".into());
        }
        let secret = data[1..17].try_into().unwrap();
        let fingerprint = data[17..49].try_into().unwrap();
        let len = usize::from(data[49]);
        if len == 0 || len > 8 {
            return Err("candidate count".into());
        }
        let mut pos = 50;
        let mut candidates = Vec::new();
        for _ in 0..len {
            let family = *data.get(pos).ok_or("candidate truncated")?;
            pos += 1;
            let size = match family {
                4 => 4,
                6 => 16,
                _ => return Err("candidate family".into()),
            };
            if pos + size + 2 > n - 3 {
                return Err("candidate truncated".into());
            }
            let ip = if family == 4 {
                IpAddr::V4(Ipv4Addr::from(
                    <[u8; 4]>::try_from(&data[pos..pos + 4]).unwrap(),
                ))
            } else {
                IpAddr::V6(Ipv6Addr::from(
                    <[u8; 16]>::try_from(&data[pos..pos + 16]).unwrap(),
                ))
            };
            pos += size;
            let port = u16::from_le_bytes([data[pos], data[pos + 1]]);
            pos += 2;
            if port == 0 || ip.is_unspecified() || ip.is_multicast() {
                return Err("invalid candidate".into());
            }
            candidates.push(SocketAddr::new(ip, port));
        }
        if pos != n - 3 || data[pos] > 1 {
            return Err("room code trailing fields".into());
        }
        Ok(Self {
            secret,
            fingerprint,
            candidates,
            symmetric: data[pos] != 0,
        })
    }
}
