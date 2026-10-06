# 原生网络迁移与验收记录

2026-10-07，Windows x64，Rust 1.99.0 / MSVC 14.51，quinn 0.11.12、rustls 0.23.45，依赖固定在 `Cargo.lock`。游戏插件目标仍是 netstandard2.1，独立测试是 net8.0。

M0–M4 的实现已落地：LiteNetLib 已移除，单个双栈 UDP socket 共用 QUIC、STUN 与打洞；房间码、证书指纹、成员网格、三通道、标题入房与存档进度已接入。M5 主机转发、M6 UPnP、公网信令和中继不在本次实现中。这里记录自动测试与游戏实测的边界。

## 自动验收

最终原生回归 19 项通过，独立高延迟 Bulk 短测 1 项通过，另行运行的 30 分钟长测 1 项通过；clippy 全目标零警告、rustfmt 检查通过。C# 初版与会话测试由 DeepSeek 协助，父代理审查修正并独立执行验证。

| 项目 | 当前结果与覆盖范围 |
|---|---|
| 原生 DLL | Release 构建通过，14 个 `pn_*` 导出；依赖仅为 Windows 系统 DLL，未依赖 VCRUNTIME/MSVCP |
| C ABI | Rust 与 C# 布局断言通过；MSVC C11 编译 `include/abi_check.c` 通过，配置/事件/统计均 32B |
| 编码、帧、序号、队列 | 未知 kind、超长帧、回绕、CRC、探针 MAC、STUN、小缓冲保留事件、优先淘汰数据报、可靠背压唤醒、FFI panic 捕获通过 |
| 回环 | Reliable / Sequenced / Bulk 双向，0、1B、1MiB、5MiB、64MiB 边界，过大载荷拒绝通过 |
| 五人网格 | 同时入房后两两互通，成员退出在 1 秒内通知，其余成员继续联机；踢人 reason=2，房主退出 reason=6；满房拒绝、ID 不复用通过 |
| 公共 STUN 探测 | 真实 UDP 探测取得公网反射候选，7 个候选、约 637ms；类型 unknown，未据此宣称跨网打洞通过 |
| NAT 模拟 | full-cone 房主入房通过；可达房主下两个受限锥形成员建立网格通过；对称 NAT 网格失败给出类型与 PunchTimeout；受限房主首次入房按设计失败并有诊断 |
| 生命周期 | 100 次 init→host→join→shutdown 无崩溃；Windows 首次网络连接缓存预热后，线程/句柄基线 5/114，100 次结束仍 5/114，无持续增长 |
| 托管传输 | 真 DLL、64B 小缓冲扩容、订阅者抛异常隔离、回调内 Shutdown 后停止 Poll；发送 QueueFull FIFO、广播与单播顺序屏障、有界重试通过 |
| 三进程 C# | host + 两个独立成员，房间码、网格、可靠/状态消息、主机→成员 5MiB 与成员→主机 1MiB Bulk、踢出/关闭，全部进程 exit 0 |
| 会话集成 | 源链接真实 NetworkRuntime / HostSession / ClientSession，游戏依赖用 stub、传输用 FakeTransport；6 场景 184 断言通过：三条件乱序就绪、拒绝非主机配置/存档、坏存档诊断、主机握手、标题/加载延期 FIFO、256/帧预算、断线清队列、关闭再启动 |
| 插件构建 | 真实游戏程序集与 Publicizer 的 MSBuild Release 构建通过，0 错误；有既有 Publicizer/MonoMod 重名、不可达代码、BepInEx 分析器警告 |
| 30 分钟压测 | 通过：400ms/5% 与 800ms/15%，RTT 抖动 ±50ms、5% 重排；各 90000 条 Reliable 全部按序收到，0 断线；Seq 413323/864000 与 383264/864000，收到的序号严格递增；测试进程 CPU 15.32% 单核，后半程约 13.4–13.6MiB 内存 |

短测的 400ms RTT / 5% 丢包场景，5MiB Bulk 到应用事件实测约 9.8 秒，达到 <15 秒目标；同时持续发 60Hz×8×150B Sequenced 与 50 条/s Reliable。800ms / 15% 丢包场景 Reliable 无丢失、无乱序。另用修正计时后的 20 秒诊断测量加入 ±50ms RTT 抖动与 5% 重排：400ms/5% 的 Bulk 为 15.322s，800ms/15% 为 27.859s。该额外条件下未达到 15s，不能与无抖动的达标结果混用。Sequenced 可丢失，接收序号严格递增。

CPU 与内存数据来自独立 Rust 测试进程（包含两个代理和四个 Node）。它们不代表 Unity 主线程 Poll 0.3ms/帧或五人原生 CPU 5% 的验收；这两个目标仍需游戏实测。

长测启动后补充了无效房间码状态、Bulk 全局内存预算、STUN 候选校验和断线原因保留。上述补丁由最终代码的回归测试覆盖；30 分钟记录对应启动时构建，未对每次后续补丁重新运行长测。长测 Bulk 的早期计时包含第二组连接的准备耗时，因此 19.676s/24.806s 不作为准确传输耗时；修正计时的独立短测见上文。

## 原方案需要明确的补充

1. **STUN 不是信令。** 第一次连接房主时，只有客户端拿到房主候选，受限房主尚不知道客户端候选。因此单向房间码无法保证两台受限 NAT 机器首次互通。房主须公网可达、端口映射、同局域网可达或 full-cone；建立房主连接后，可通过它交换成员候选并同时打洞。不能仅由“反射端口相同”判断 NAT 入站过滤一定宽松。
2. Probe MAC 显式携带目标地址（含地址族/地址/端口），否则接收方看不到自身 NAT 的外部端口，无法复算目标地址参与的 MAC。ACK nonce/目标/MAC 校验后才使用观察到的来源地址。Windows 双栈 raw-send 先映射 IPv4 地址。
3. `MEMBER_INFO=0x24` 上报成员 ID/指纹/候选，`ROOM_INFO=0x25` 在认证 TLS 内传房间描述；`PEER_LEFT` 是 8B 的 ID+reason，以保留踢人原因。内部成员 JSON 使用 Rust serde 的指纹字节数组。
4. C 的统计类型叫 `pn_peer_stats_t`，函数仍叫 `pn_peer_stats`，避免原计划 typedef 与函数同名导致头文件不能编译；二进制 ABI 数值、布局与导出不变。
5. 高级直连未指定指纹时为 TOFU；无房间 MAC 的入口仅接受局域网/回环或显式固定端口主机。固定端口配置开放该高级入口，不能把它的准入强度等同房间码。
6. 单个合法 Bulk 可大于 8MiB 发送预算或 32MiB 事件预算；仅允许它独占空队列，其余可靠数据背压。Bulk 分配预算整个 Node 共用 64MiB。发送用 BBR 与 8KiB DATAGRAM 待发预算，避免随机丢包时可靠流被持续状态包饿死。序号仍是每 peer 全局序号，保持原业务语义。
7. 存档与 JSON 握手都有独立信封；只接受 peer 0 的主机配置/存档。解压上限 64MiB，非法存档不会标记 Ready。标题/加载期只延期业务分发；达到可靠延期预算时暂停读取，恢复后每帧最多 256 条。日志只记录 NAT 类型，不记录包含密钥的完整房间码或 NAT JSON。

## 构建与复现

须有 Windows x64 Rust MSVC 工具链、Visual Studio C++ 工具与适用 Windows SDK；.NET SDK 和游戏程序集用于完整插件构建。普通环境可用：

```powershell
Push-Location native/polaris_net
cargo build --release --locked
cargo test --release --locked
cargo clippy --all-targets --locked -- -D warnings
cargo fmt --check
Pop-Location
dotnet build PolarisNoels.csproj -c Release -p:RequireNativeTransport=true
powershell -NoProfile -ExecutionPolicy Bypass -File tools/NetSmoke/run-smoke.ps1
dotnet run --project tools/SessionSmoke/SessionSmoke.csproj -c Release
```

不指定 `GameRootPath` 时构建输出在 `.build/plugin`，不会安装进游戏。测试脚本以隐藏子进程执行；各进程日志在 NetSmoke 的 `bin/Release/net8.0/smoke-run`，临时房间码文件在结束后移除。没有原生 DLL 时脚本返回 10，属于阻塞，不能当作联调通过。

30 分钟长测单独运行：

```powershell
cd native/polaris_net
cargo test --release --locked --test latency thirty_minute_soak -- --ignored --nocapture
```

代理种子固定，支持单向延迟、抖动、丢包、重排、带宽限制。NAT 测试走真实 UDP 映射代理；STUN 测试用两个不同回环 IP 的模拟服务。结果证明模拟条件中的行为，不能代替实际路由器测试。`PN_SOAK_SECONDS` 只用于诊断缩短时间，不属于 30 分钟验收。

## 游戏内验收清单（尚未执行）

- [ ] 同机双开，房间码复制/粘贴、主机房间码布局、标题存档进度正常；取消后可再次入房。
- [ ] Ready 三条件完成后才进入游戏；标题→游戏→地图加载共用连接，期间不出现重复 peer 或重连。
- [ ] 主机与成员都可看到角色；后加入、成员退出、踢出、房主退出时界面提示与清理正常。
- [ ] 两台不同网络机器测试：可达房主与家庭 NAT 成员、受限 NAT 成员网格、对称 NAT 失败提示；不宣称所有家宽或 CGNAT 都可打通。
- [ ] 游戏中叠加 400/800ms 延迟与 5% 丢包运行至少 5 分钟，测试存档、切图、可靠事件与退出。
- [ ] 五人 60Hz 下测原生 CPU、Unity 主线程 Poll/分发耗时与内存；核对 5% 单核和 0.3ms/帧目标。
- [ ] DLL 缺失、ABI 不匹配时只禁用联机，单机仍能启动。

本次没有进行游戏内实测或真实跨网联机，也没有安装到游戏目录。高延迟与丢包代理验收运行在 Rust；C# 三进程联调目前使用无损回环，尚未在外部 Clumsy/netem 环境复测。此前审计发现的业务层联机问题留待下一轮处理。
