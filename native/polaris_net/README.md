# polaris_net

Windows x64 MSVC `cdylib`，ABI 1。一个进程只能通过 C ABI 持有一个会话；独立 Rust 测试使用多个 `Node`，C# 联调使用多个进程。

```powershell
cd native/polaris_net
cargo build --release --locked
cargo test --release --locked
cargo clippy --all-targets --locked -- -D warnings
cargo fmt --check
# 独立的 30 分钟延迟/丢包验收，默认测试不启动它
cargo test --release --locked --test latency thirty_minute_soak -- --ignored --nocapture
```

须从这个目录执行 Cargo，使 `.cargo/config.toml` 的静态 CRT 配置生效。工具链与依赖实测版本在 `Cargo.lock` 与仓库验收记录中。产物为 `target/release/polaris_net.dll`，随游戏插件 DLL 一起发布。

`include/polaris_net.h` 是 C ABI。事件、配置和统计均为 Windows x64 下 32 字节；调用方负责保证指针及长度有效。参数错误/状态错误不会跨 FFI 展开 Rust panic，空指针允许用于长度为零的载荷和查询所需缓冲大小。`pn_poll` 缓冲不足时保留当前事件。

Reliable：单个有序双向流，用户载荷最多 1MiB。Sequenced：DATAGRAM、每 peer 全局序号，载荷由协商后的 MTU 限制。Bulk：独立单向流，载荷最多 64MiB，低流优先级；各 Bulk 之间不保证完成顺序。控制帧不交给 C#。

发送预算 4096 条/peer、8MiB；接收事件预算 8192 条、32MiB。为兼容合法的 64MiB Bulk，单个大包允许独占空队列预算，其余可靠消息等待空间。接收 Bulk 的分配预算是整个 Node 共用 64MiB。Sequenced 可被丢弃，可靠事件不会静默淘汰。QUIC 本身的发送/流控缓冲另有界限，因此队列预算并非整个进程总内存。

全连接网格由主机分配不复用的 peer ID；较大 ID 拨号。证书私钥证明、证书指纹与房间 MAC 均校验。Probe ACK 要等到主机成员公告的指纹被信任才发出，避免多人同时入房的 TLS 时序竞争。

首次入房需要可达主机。STUN 只发现映射，不是信令/中继。主机可达后，受限锥形 NAT 成员可经主机协调打洞；对称 NAT 网格失败返回可诊断超时。高级直连未提供指纹时使用 TOFU；无房间 MAC 的直连只允许局域网/回环来源或显式固定端口主机。固定端口主机允许该高级入口，因此其准入凭据弱于房间码。

ABI 的 `enable_upnp` 仍是预留字段，当前忽略；本期没有中继、自动重连、账号或公网信令服务。
