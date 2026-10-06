# C# 侧原生网络层接入说明（M0 / M4）

> 当前实现：ABI 1、原生 QUIC 传输、C# 会话与标题入房接入。实现与验证说明取代已完成的计划。
> 本文描述 C# 接入；原生 DLL 已构建并完成真实三进程联调。完整验证范围与实测结果见 [验收记录](native-network-validation.md)。

## 1. 文件清单

| 文件 | 作用 |
|---|---|
| `Networking/NativeMethods.cs` | 手写 P/Invoke：`PnConfig`/`PnEvent`/`PnPeerStats`、`PnAbi` 常量、`PnNativeLoader`（仅绝对路径 `LoadLibraryW` + ABI 校验，**不调用 `SetDllDirectoryW`**）。**无 Unity 依赖** |
| `Networking/NativeTransport.cs` | `INetTransport` 的原生实现：轮询、逐订阅者异常隔离、`PN_ERR_BUFFER` 扩容重试（clamp 64MB）、可靠/大块发送交给托管队列。**无 Unity 依赖** |
| `Networking/ReliableSendQueue.cs` | 可靠/大块发送队列：每 `(peer,channel)` FIFO、QueueFull 停止该目标、有界字节、溢出/超时明确断开、数据拷贝。**无 Unity 依赖** |
| `Networking/SafeEvents.cs` | 事件分发异常隔离：逐个订阅者 `try/catch`，一个回调抛异常不影响其它订阅者与后续事件。**无 Unity 依赖** |
| `Networking/INetTransport.cs` | 窄接口、`NetChannel`/`DisconnectReason`/`NetJoinError`、`NetTransportConfig`。**无 Unity 依赖** |
| `Networking/NetworkRuntime.cs` | 进程级单例：唯一 transport、事件路由、加载期间延迟业务分发（有界 + 背压）、握手信封识别 |
| `Networking/BulkCodec.cs` | BULK 载荷首字节类型：`1=存档`、`2=游戏大包(protobuf)` |
| `Networking/SaveTransfer.cs` | 旧的 gzip 存档打包（从已删除的 `NetTuning.cs` 移植；`NetTuning` 类随 LiteNetLib 一起删除） |
| `Networking/Session/HostSession.cs` | 主机业务握手：peer 连上 → HostMessage(Reliable) + 存档(Bulk)；记录 sender id 的昵称/NoelColor/Party；静音 |
| `Networking/Session/ClientSession.cs` | 入房者业务握手：JoinResult/HostMessage/Bulk 三就绪、写存档、回 ClientMessage、进游戏后生成影子 |
| `Networking/ClientServer/HandshakeCodec.cs` | 握手信封 magic `PNJ1` + 1 字节类型 + JSON |
| `Networking/PolarisNoelsPeer.cs` | 游戏内外壳：业务分发、地图过滤、实体补发、建立后对已有 peer 补宣告、销毁时退房 |
| `tools/NetSmoke/` | 独立 net8.0 冒烟：ABI 布局自检 + 托管队列/事件隔离自检 + 三进程 mesh |
| 删除 | `Networking/ClientServer/PolarisNoelsHost.cs`、`PolarisNoelsClient.cs`、`Networking/NetTuning.cs`、`DB` 里三个 `*_ACCESS_KEY`、`PolarisNoelsTools.PeerDic/ConnectOtherPeer` |

## 2. 通道与投递语义

| 旧 | 新 |
|---|---|
| `DeliveryMethod.ReliableOrdered` | `NetChannel.Reliable` (0) |
| `DeliveryMethod.Sequenced` | `NetChannel.Sequenced` (1) |
| （存档单条大消息 / 模拟战斗地图） | `NetChannel.Bulk` (2) |

`PolarisNoelsTools.Broadcast(message, channel = Reliable, mapOnly)`；`peer = -1` 为广播。
保存协议字段（protobuf-net `PolarisNoelsPeerMessage` 等）**未改动**。

## 3. 连接生命周期（关键行为）

1. `Plugin.Awake`：`NetworkRuntime.PreloadNative()` 按插件目录绝对路径 `LoadLibraryW` 预加载 `polaris_net.dll` 并比对 `pn_abi_version()`；
   失败只写日志并置 `NativeUnavailable`（联机禁用），不抛异常。配置项：`Network/BindPort`、`Network/MaxPlayers`、`Network/EnableStun`（游戏内设置里另有 STUN 开关 `mpconfig_enable_stun`）。
2. `Plugin.Update`：每帧 `NetworkRuntime.Poll()` —— **标题、游戏、加载三种状态都 poll**，场景切换不 `pn_shutdown`。
3. 主机标题流程：选完存档（`DB.InitConfig` + `DB.SyncSaveContentBuffer` 均已就绪）→ `NetworkRuntime.StartHost` →
   `HostSession.Start` 再次校验二者就绪 → `pn_host_start` → 候选收集完成后 `PN_EV_NAT_STATE` 触发 `pn_room_code` 读取，界面显示房间码并提供「复制房间码」。
4. 客户端标题流程：输入房间码（或高级 `IP:端口` 走 `pn_join_direct`）→ `pn_join` → 等到 `JoinResult(0)` + `HostMessage` + BULK 存档三者齐备后进入游戏（超时/各类 `pn_join_error` 有独立提示）。
   取消/超时/失败都会 `NetworkRuntime.Shutdown()`，彻底 `pn_shutdown` 并释放 transport，重试时重建。
5. 进入游戏：`NetworkBootstrap.OnNoelAppear` → `PolarisNoelsTools.InitNetworking` 只挂业务外壳，**不重连**；客户端影子在此时才生成（标题阶段没有 Mover）。
   外壳 `Start` 会对标题期就连上的 peer 补发一次玩家宣告。
6. 加载期间：`NetworkRuntime.DeferGameDispatch` 为真、`gameHandler==null`（标题期）或已有积压时，游戏消息进托管队列；poll 不暂停。
   队列有 4096 条 / 32MB 双上限：溢出优先丢 `Sequenced`（含淘汰最旧的 Sequenced），可靠消息不丢；
   可靠消息仍放不下时暂停 `pn_poll` 触发 QUIC 背压，业务侧排空后自动恢复。恢复分发每帧最多 256 条。
7. 断线：`PN_DISC_KICKED → DB.PolarisNoelsHostKicked`，`HOST_CLOSED/REMOTE → DB.PolarisNoelsHostClosed`，沿用原有回标题提示。
   游戏内外壳销毁（回到标题/退出）时主动退房：远端关闭或本地仍连接都执行 `NetworkRuntime.Shutdown()`。

## 4. 可靠发送队列（PN_ERR_QUEUE_FULL=-5）

`NativeTransport` 把 Reliable/Bulk 交给 `ReliableSendQueue`，不静默丢弃：

- **顺序**：每个 `(peer,channel)` 一条 FIFO。该目标有积压时新消息一律排队，不会越过旧消息；同通道的广播与单播设有顺序屏障，避免后发单播越过待发送广播，或反向越过；不同单播目标可独立推进。Reliable 与 Bulk 互不影响。
- **QueueFull**：本轮不再尝试该目标/通道（同目标的后续消息保持顺序等待），其它目标照常；下一帧重试。
- **有界**：默认 8192 条 / 32MB。溢出或等待超过 5s：记 error 并 `pn_disconnect(peer, Timeout)`；
  广播（`peer=-1`）失败则断开全部已连接 peer，绝不部分重发（原生 `pn_send` 广播是原子预留）。
- Sequenced 满时只记日志丢弃当前包（原生层已丢最旧）。数据入队时拷贝，调用方之后可复用原数组。

## 5. 异常隔离与缓冲

- 所有 `pn_event` 回调经 `SafeEvents` 逐个订阅者 `try/catch`，一个回调抛异常只记日志，不中断同事件其它订阅者，也不中断后续事件。
- 订阅者在回调里 `Shutdown()` 后，`Poll` 立即返回，绝不再调 `pn_poll`。
- `pn_poll` 小缓冲返回 `PN_ERR_BUFFER` 时按 `ev.Len` 扩容重试；扩容目标 clamp 到 64MB，只要 `needed<=64MB` 就一定能满足。

## 6. 验证命令

```powershell
# 1) ABI 布局自检（不需要 polaris_net.dll）
dotnet tools/NetSmoke/bin/Release/net8.0/NetSmoke.dll abi

# 2) 托管队列 / 事件隔离自检（不需要 polaris_net.dll）
dotnet tools/NetSmoke/bin/Release/net8.0/NetSmoke.dll selfcheck

# 3) 一键：构建 + ABI + 自检 +（有 DLL 时）三进程 mesh
powershell -NoProfile -ExecutionPolicy Bypass -File tools/NetSmoke/run-smoke.ps1
#   退出码：0 通过；1 构建失败；2 ABI 失败；3 mesh 失败；4 自检失败；10 无 DLL（联调被阻塞，不能视为通过）

# 4) 真实公共 STUN 探测（需要允许 UDP 出站，不输出房间密钥）
dotnet tools/NetSmoke/bin/Release/net8.0/NetSmoke.dll nat

# 5) Roslyn csc 直编备用路径
powershell -NoProfile -ExecutionPolicy Bypass -File tools/NetSmoke/build-netsmoke.ps1
```

`selfcheck` 断言：QueueFull 后 A/B/C FIFO、QueueFull 只阻塞该目标、广播失败不部分重发、32MB 上限明确失败、
数据拷贝、Reliable/Bulk 独立、抛异常订阅者不影响其它订阅者与后续消息、缓冲扩容 clamp。
mesh 断言：每条 ack 的 `ok` 属性、Scalar 化 Sequenced 严格单调、双向 BULK（host→client 5MB、client→host 1MB）完整性、
mesh 直连、踢出（`Kicked`）、主机关闭（`HostClosed`）。

## 7. 验证边界

原生 ABI、小缓冲重试、回调异常与回调内 Shutdown、三进程 mesh、双向 BULK、踢出/主机关闭已通过自动验证。完整插件通过 dotnet build（含 Publicizer），产物同时包含托管 DLL 与原生 DLL。

房间码 UI 排版、游戏场景切换、影子生成及实际不同网络 NAT 仍需按 [游戏验收清单](native-network-validation.md) 实测。构建不等于游戏验收。
