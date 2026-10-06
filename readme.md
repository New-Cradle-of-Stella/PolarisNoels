# PolarisNoels

AliceInCradle 的 BepInEx 联机插件，原名 WNMN / WeNeedMoreNoels。

后续计划：提取 Polaris 基础库，提供二次开发教程。

A BepInEx multiplayer plugin for AliceInCradle, formerly WNMN / WeNeedMoreNoels. Planned: a reusable Polaris library and a mod development tutorial.

联机传输已迁移为 Windows x64 Rust / QUIC 原生层，使用单个 UDP 端口、房间码和独立存档通道。
构建前先在 `native/polaris_net` 执行 `cargo build --release --locked`，再构建 `PolarisNoels.csproj`；插件输出应同时包含 `PolarisNoels.dll` 与 `polaris_net.dll`。
默认绑定随机端口；高级 IP 直连可在 BepInEx 的 `Network/BindPort` 配置固定端口。
房间码包含入房凭据，请只分享给一起联机的玩家。STUN 不提供中继；房主受限 NAT、对称 NAT 或部分 CGNAT 仍可能需要端口映射或可达网络。

构建、自动测试结果与游戏内验收清单见 [原生网络迁移记录](docs/native-network-validation.md)。
