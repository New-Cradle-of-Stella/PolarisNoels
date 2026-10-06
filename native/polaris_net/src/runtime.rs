use crate::{
    nat::socket::PunchSocket,
    quic::{Gate, Identity},
    room_code::Room,
    session::{Config, State},
};
use quinn::Runtime as _;
use std::{
    net::{SocketAddr, UdpSocket},
    sync::{
        atomic::{AtomicI32, Ordering},
        Arc,
    },
    time::Duration,
};

pub struct Node {
    pub state: Arc<State>,
    runtime: Option<tokio::runtime::Runtime>,
}
impl Node {
    pub fn new(config: Config) -> Result<Self, String> {
        if !(2..=8).contains(&config.max_peers) || config.idle_timeout_ms < 1000 {
            return Err("invalid network configuration".into());
        }
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()
            .map_err(|e| e.to_string())?;
        let state = runtime.block_on(async {
            let identity = Identity::new()?;
            let gate = Arc::new(Gate::default());
            let local_id = Arc::new(AtomicI32::new(-1));
            let udp = bind_socket(config.bind_port).map_err(|e| e.to_string())?;
            udp.set_nonblocking(true).map_err(|e| e.to_string())?;
            let inner = quinn::TokioRuntime
                .wrap_udp_socket(udp)
                .map_err(|e| e.to_string())?;
            let socket = Arc::new(PunchSocket::new(inner, local_id.clone()));
            let endpoint = quinn::Endpoint::new_with_abstract_socket(
                quinn::EndpointConfig::default(),
                Some(identity.server_config(gate.clone(), config.idle_timeout_ms)?),
                socket.clone(),
                Arc::new(quinn::TokioRuntime),
            )
            .map_err(|e| e.to_string())?;
            Ok::<_, String>(State::new(
                config, endpoint, identity, gate, socket, local_id,
            ))
        })?;
        runtime.spawn(state.clone().accept_loop());
        Ok(Self {
            state,
            runtime: Some(runtime),
        })
    }
    pub fn host(&self) -> Result<(), String> {
        self.state
            .phase
            .compare_exchange(0, 1, Ordering::AcqRel, Ordering::Acquire)
            .map_err(|_| "session already started")?;
        self.runtime
            .as_ref()
            .ok_or("shutdown")?
            .spawn(self.state.clone().start_host());
        Ok(())
    }
    pub fn join(&self, code: &str) -> Result<(), String> {
        let room = Room::decode(code)?;
        self.state
            .phase
            .compare_exchange(0, 2, Ordering::AcqRel, Ordering::Acquire)
            .map_err(|_| "session already started")?;
        self.runtime
            .as_ref()
            .ok_or("shutdown")?
            .spawn(self.state.clone().join_room(room));
        Ok(())
    }
    pub fn join_direct(&self, address: SocketAddr, fp: Option<[u8; 32]>) -> Result<(), String> {
        self.state
            .phase
            .compare_exchange(0, 2, Ordering::AcqRel, Ordering::Acquire)
            .map_err(|_| "session already started")?;
        self.runtime
            .as_ref()
            .ok_or("shutdown")?
            .spawn(self.state.clone().join_direct(address, fp));
        Ok(())
    }
    pub fn spawn_direct_host_lookup(
        &self,
        host: String,
        port: u16,
        fp: Option<[u8; 32]>,
    ) -> Result<(), String> {
        self.state
            .phase
            .compare_exchange(0, 2, Ordering::AcqRel, Ordering::Acquire)
            .map_err(|_| "session already started")?;
        let state = self.state.clone();
        self.runtime.as_ref().ok_or("shutdown")?.spawn(async move {
            let result = tokio::time::timeout(
                Duration::from_secs(3),
                tokio::net::lookup_host((host.as_str(), port)),
            )
            .await;
            if let Ok(Ok(mut found)) = result {
                if let Some(address) = found.next() {
                    state.join_direct(address, fp).await;
                    return;
                }
            }
            state.events.log(1, "direct host resolution failed".into());
            state
                .events
                .push(crate::event::Event::new(
                    crate::event::JOIN_RESULT,
                    0,
                    0,
                    3,
                    vec![],
                ))
                .await;
        });
        Ok(())
    }
    pub fn shutdown(&mut self) {
        if let Some(runtime) = self.runtime.take() {
            runtime.block_on(self.state.shutdown());
            runtime.shutdown_timeout(Duration::from_millis(500));
        }
    }
    pub fn emit(&self, event: crate::event::Event) {
        if let Some(runtime) = &self.runtime {
            let events = self.state.events.clone();
            runtime.spawn(async move {
                events.push(event).await;
            });
        }
    }
}
impl Drop for Node {
    fn drop(&mut self) {
        self.shutdown();
    }
}

fn bind_socket(port: u16) -> std::io::Result<UdpSocket> {
    let socket = socket2::Socket::new(
        socket2::Domain::IPV6,
        socket2::Type::DGRAM,
        Some(socket2::Protocol::UDP),
    )?;
    socket.set_only_v6(false)?;
    socket.set_recv_buffer_size(4 * 1024 * 1024)?;
    socket.set_send_buffer_size(1024 * 1024)?;
    socket.bind(&SocketAddr::from(([0u16; 8], port)).into())?;
    Ok(socket.into())
}
