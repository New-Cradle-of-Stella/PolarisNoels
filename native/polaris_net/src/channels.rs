use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};

pub const RELIABLE: i32 = 0;
pub const SEQUENCED: i32 = 1;
pub const BULK: i32 = 2;
pub const MAX_RELIABLE: usize = 1024 * 1024;
pub const MAX_CONTROL: usize = 64 * 1024;
pub const MAX_BULK: usize = 64 * 1024 * 1024;
pub const HELLO: u8 = 1;
pub const HELLO_ACK: u8 = 2;
pub const USER: u8 = 0x10;
pub const PEER_LIST: u8 = 0x20;
pub const PEER_JOINED: u8 = 0x21;
pub const PEER_LEFT: u8 = 0x22;
pub const PUNCH: u8 = 0x23;
pub const MEMBER_INFO: u8 = 0x24;
pub const DISCONNECT: u8 = 0x30;
pub const FORWARD: u8 = 0x31;

pub fn is_newer(seq: u32, previous: u32) -> bool {
    seq != previous && seq.wrapping_sub(previous) < (1 << 31)
}

pub async fn write_frame<W: AsyncWrite + Unpin>(
    writer: &mut W,
    kind: u8,
    data: &[u8],
) -> Result<(), String> {
    let max = if kind == USER {
        MAX_RELIABLE
    } else {
        MAX_CONTROL
    };
    if data.len() > max {
        return Err("frame too large".into());
    }
    writer.write_u8(kind).await.map_err(|e| e.to_string())?;
    writer
        .write_u32_le(data.len() as u32)
        .await
        .map_err(|e| e.to_string())?;
    writer.write_all(data).await.map_err(|e| e.to_string())
}

pub async fn read_frame<R: AsyncRead + Unpin>(reader: &mut R) -> Result<(u8, Vec<u8>), String> {
    let kind = reader.read_u8().await.map_err(|e| e.to_string())?;
    let len = reader.read_u32_le().await.map_err(|e| e.to_string())? as usize;
    let max = if kind == USER {
        MAX_RELIABLE
    } else {
        MAX_CONTROL
    };
    if len > max {
        return Err("frame too large".into());
    }
    let mut data = vec![0; len];
    reader
        .read_exact(&mut data)
        .await
        .map_err(|e| e.to_string())?;
    Ok((kind, data))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn sequence_wrap() {
        assert!(is_newer(0, u32::MAX));
        assert!(!is_newer(u32::MAX, 0));
        assert!(!is_newer(17, 17));
    }
    #[tokio::test]
    async fn frames_and_limits() {
        let (mut a, mut b) = tokio::io::duplex(64);
        let sender = tokio::spawn(async move {
            write_frame(&mut a, 0xfe, b"unknown").await.unwrap();
            write_frame(&mut a, USER, b"").await.unwrap();
            write_frame(&mut a, USER, b"next").await.unwrap();
        });
        assert_eq!(
            read_frame(&mut b).await.unwrap(),
            (0xfe, b"unknown".to_vec())
        );
        assert_eq!(read_frame(&mut b).await.unwrap(), (USER, vec![]));
        assert_eq!(read_frame(&mut b).await.unwrap().1, b"next");
        sender.await.unwrap();
        let mut bad = &b"\x10\xff\xff\xff\x7f"[..];
        assert!(read_frame(&mut bad).await.is_err());
    }
}
