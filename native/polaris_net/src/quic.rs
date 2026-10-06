use parking_lot::RwLock;
use quinn::crypto::rustls::{QuicClientConfig, QuicServerConfig};
use rustls::client::danger::{HandshakeSignatureValid, ServerCertVerified, ServerCertVerifier};
use rustls::pki_types::{CertificateDer, PrivatePkcs8KeyDer, ServerName, UnixTime};
use rustls::server::danger::{ClientCertVerified, ClientCertVerifier};
use rustls::{DigitallySignedStruct, DistinguishedName, SignatureScheme};
use sha2::{Digest, Sha256};
use std::{
    collections::HashSet,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc,
    },
    time::Duration,
};

pub const ALPN: &[u8] = b"polaris-noels/1";
pub fn fingerprint(cert: &[u8]) -> [u8; 32] {
    Sha256::digest(cert).into()
}

#[derive(Debug, Default)]
pub struct Gate {
    pub hosting: AtomicBool,
    pub known: RwLock<HashSet<[u8; 32]>>,
}
#[derive(Clone)]
pub struct Identity {
    pub cert: CertificateDer<'static>,
    pub key: Vec<u8>,
    pub fingerprint: [u8; 32],
}
impl Identity {
    pub fn new() -> Result<Self, String> {
        let generated = rcgen::generate_simple_self_signed(vec!["polaris.local".into()])
            .map_err(|e| e.to_string())?;
        let cert = generated.cert.der().clone();
        let fingerprint = fingerprint(cert.as_ref());
        Ok(Self {
            cert,
            key: generated.key_pair.serialize_der(),
            fingerprint,
        })
    }
    pub fn server_config(&self, gate: Arc<Gate>, idle: u32) -> Result<quinn::ServerConfig, String> {
        let mut tls = rustls::ServerConfig::builder_with_provider(Arc::new(
            rustls::crypto::ring::default_provider(),
        ))
        .with_protocol_versions(&[&rustls::version::TLS13])
        .map_err(|e| e.to_string())?
        .with_client_cert_verifier(Arc::new(ClientVerifier(gate)))
        .with_single_cert(
            vec![self.cert.clone()],
            PrivatePkcs8KeyDer::from(self.key.clone()).into(),
        )
        .map_err(|e| e.to_string())?;
        tls.alpn_protocols = vec![ALPN.to_vec()];
        let mut config = quinn::ServerConfig::with_crypto(Arc::new(
            QuicServerConfig::try_from(tls).map_err(|e| e.to_string())?,
        ));
        config.transport_config(transport(idle));
        Ok(config)
    }
    pub fn client_config(
        &self,
        expected: Option<[u8; 32]>,
        idle: u32,
    ) -> Result<quinn::ClientConfig, String> {
        let mut tls = rustls::ClientConfig::builder_with_provider(Arc::new(
            rustls::crypto::ring::default_provider(),
        ))
        .with_protocol_versions(&[&rustls::version::TLS13])
        .map_err(|e| e.to_string())?
        .dangerous()
        .with_custom_certificate_verifier(Arc::new(ServerVerifier(expected)))
        .with_client_auth_cert(
            vec![self.cert.clone()],
            PrivatePkcs8KeyDer::from(self.key.clone()).into(),
        )
        .map_err(|e| e.to_string())?;
        tls.alpn_protocols = vec![ALPN.to_vec()];
        let mut config = quinn::ClientConfig::new(Arc::new(
            QuicClientConfig::try_from(tls).map_err(|e| e.to_string())?,
        ));
        config.transport_config(transport(idle));
        Ok(config)
    }
}

fn transport(idle: u32) -> Arc<quinn::TransportConfig> {
    let mut cfg = quinn::TransportConfig::default();
    cfg.max_idle_timeout(Some(
        Duration::from_millis(u64::from(idle)).try_into().unwrap(),
    ));
    cfg.keep_alive_interval(Some(Duration::from_secs(1)));
    cfg.datagram_receive_buffer_size(Some(1024 * 1024));
    cfg.datagram_send_buffer_size(8 * 1024);
    // Loss on game links is often unrelated to congestion. Cubic collapses the
    // window under random loss, letting continuous datagrams starve streams.
    let mut bbr = quinn::congestion::BbrConfig::default();
    bbr.initial_window(256 * 1024);
    cfg.congestion_controller_factory(Arc::new(bbr));
    cfg.max_concurrent_bidi_streams(1u32.into());
    cfg.max_concurrent_uni_streams(4u32.into());
    cfg.stream_receive_window((8u32 * 1024 * 1024).into());
    cfg.receive_window((32u32 * 1024 * 1024).into());
    Arc::new(cfg)
}

#[derive(Debug)]
struct ServerVerifier(Option<[u8; 32]>);
impl ServerCertVerifier for ServerVerifier {
    fn verify_server_cert(
        &self,
        cert: &CertificateDer<'_>,
        _: &[CertificateDer<'_>],
        _: &ServerName<'_>,
        _: &[u8],
        _: UnixTime,
    ) -> Result<ServerCertVerified, rustls::Error> {
        if self.0.is_some_and(|fp| fp != fingerprint(cert.as_ref())) {
            return Err(rustls::Error::General(
                "server certificate fingerprint mismatch".into(),
            ));
        }
        Ok(ServerCertVerified::assertion())
    }
    fn verify_tls12_signature(
        &self,
        m: &[u8],
        c: &CertificateDer<'_>,
        s: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, rustls::Error> {
        rustls::crypto::verify_tls12_signature(
            m,
            c,
            s,
            &rustls::crypto::ring::default_provider().signature_verification_algorithms,
        )
    }
    fn verify_tls13_signature(
        &self,
        m: &[u8],
        c: &CertificateDer<'_>,
        s: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, rustls::Error> {
        rustls::crypto::verify_tls13_signature(
            m,
            c,
            s,
            &rustls::crypto::ring::default_provider().signature_verification_algorithms,
        )
    }
    fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
        rustls::crypto::ring::default_provider()
            .signature_verification_algorithms
            .supported_schemes()
    }
}
#[derive(Debug)]
struct ClientVerifier(Arc<Gate>);
impl ClientCertVerifier for ClientVerifier {
    fn root_hint_subjects(&self) -> &[DistinguishedName] {
        &[]
    }
    fn verify_client_cert(
        &self,
        cert: &CertificateDer<'_>,
        _: &[CertificateDer<'_>],
        _: UnixTime,
    ) -> Result<ClientCertVerified, rustls::Error> {
        if !self.0.hosting.load(Ordering::Acquire)
            && !self.0.known.read().contains(&fingerprint(cert.as_ref()))
        {
            return Err(rustls::Error::General(
                "unknown mesh member certificate".into(),
            ));
        }
        Ok(ClientCertVerified::assertion())
    }
    fn verify_tls12_signature(
        &self,
        m: &[u8],
        c: &CertificateDer<'_>,
        s: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, rustls::Error> {
        rustls::crypto::verify_tls12_signature(
            m,
            c,
            s,
            &rustls::crypto::ring::default_provider().signature_verification_algorithms,
        )
    }
    fn verify_tls13_signature(
        &self,
        m: &[u8],
        c: &CertificateDer<'_>,
        s: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, rustls::Error> {
        rustls::crypto::verify_tls13_signature(
            m,
            c,
            s,
            &rustls::crypto::ring::default_provider().signature_verification_algorithms,
        )
    }
    fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
        rustls::crypto::ring::default_provider()
            .signature_verification_algorithms
            .supported_schemes()
    }
}

pub fn remote_fingerprint(conn: &quinn::Connection) -> Result<[u8; 32], String> {
    let identity = conn.peer_identity().ok_or("peer certificate missing")?;
    let chain = identity
        .downcast::<Vec<CertificateDer<'static>>>()
        .map_err(|_| "peer certificate type")?;
    Ok(fingerprint(
        chain
            .first()
            .ok_or("empty peer certificate chain")?
            .as_ref(),
    ))
}
