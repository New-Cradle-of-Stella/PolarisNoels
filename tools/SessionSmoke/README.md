# SessionSmoke：managed 会话集成测试

独立的 net8.0 控制台项目，用来验证原生网络迁移后的 **C# 会话层与延迟分发**。
它 **源链接真实实现**，用 `FakeTransport` 触发真实事件，不加载 `polaris_net.dll`，不引用
Unity/游戏程序集，也不触发任何游戏行为。它 **不是** 业务代码，只用于验证。

## 1. 源链接的真实文件（不是等价假逻辑）

| 文件 | 验证内容 |
|---|---|
| `Networking/NetworkRuntime.cs` | 事件路由、握手识别、Bulk 存档路由、加载期延迟队列 / FIFO / 每帧上限 / 断开清理 |
| `Networking/Session/HostSession.cs` | peer 连上后发 `HandshakeCodec` 主机配置 + `BulkCodec` 存档；记录入房者回包 |
| `Networking/Session/ClientSession.cs` | `Ready = JoinResult(0) + HostMessage + Bulk 存档`；peer 过滤；坏存档诊断与断开 |
| `Networking/ClientServer/HandshakeCodec.cs` | 握手信封 `PNJ1` + JSON 编解码（往返断言） |
| `Networking/BulkCodec.cs` | Bulk 载荷首字节类型（存档 / 游戏大包） |
| `Networking/SaveTransfer.cs` | gzip 存档 `Pack/Unpack`（大存档往返 + 压缩断言） |
| `Networking/INetTransport.cs`、`NativeMethods.cs`、`NativeTransport.cs`、`ReliableSendQueue.cs`、`SafeEvents.cs` | `NetworkRuntime` 的编译依赖（`NativeTransport` 仅编译，不初始化，不需要 DLL） |
| `Networking/ClientServer/PolarisNoelsHostMessage.cs`、`PolarisNoelsClientMessage.cs` | 真实握手 DTO |

## 2. 测试替身（stub，仅依赖表面）

`Plugin`（日志）、`DB`、`PolarisNoelsTools` / `NetworkConfig` / `NetWorkType`、
`SVD`、`LocalNickname`、`PartyManager`、`nel`（`PRNoel` / `NelM2DBase` / `MpBase`）、
`PolarisNoels.DataStruct`（`ClientConfig` / `NoelType` / `ColorNoelColor`）。
这些都是游戏/Unity/BepInEx/DB 依赖，stub 只提供字段与空实现，**不包含任何会话逻辑**。

`FakeTransport` 实现 `INetTransport`：记录 `Send/Disconnect/StartHost/Join`，并提供
`RaiseJoinResult / RaiseMessage / RaisePeerConnected / RaisePeerDisconnected / RaiseBulkProgress`
让测试主动触发真实事件。

因为 `NetworkRuntime` 是静态单例，测试用反射把 `FakeTransport` 注入 `NetworkRuntime.Transport`
（私有 setter），再把 `NetworkRuntime` 的私有静态处理器（`OnMessage` / `OnPeerConnected` /
`OnPeerDisconnected`）挂到 fake 的事件上——这正是生产里 `EnsureTransport` 做的事。

## 3. 覆盖的场景（184 条断言）

1. **客户端就绪顺序无关**：`JoinResult(0)` + 来自 peer0 的 `HostMessage` + 有效 Bulk 存档
   三种到达顺序（`0-1-2`、`2-1-0`、`1-2-0`）都必须最终 `Ready`，且在第三个条件到达前保持 `false`；
   校验 `LocalId`、`SimBattle*` 传播、`PendingConfigs`、存档落盘字节、以及回给主机的真实
   `HandshakeCodec` ClientMessage。
2. **非主机 peer 不生效**：来自 peer3 的 HostMessage / Bulk 存档 / BulkProgress 一律忽略
   （`Ready`、`DB.SyncSaveContentBuffer`、`ReceivedArchiveBytes` 不变），peer0 仍能成功。
3. **坏存档可诊断**：格式垃圾与空载荷两种坏存档都不置 `SaveReceived`，`LastJoinError` 变为
   `HandshakeFailed`、以 `Protocol` 断开主机、并写入含 `invalid save archive` 的错误日志。
4. **主机握手与存档**：`PeerConnected` 后先发 Reliable 的 `HandshakeCodec` 主机配置
   （`InitID`、`SyncHost`、`SyncConnectedList`、`PeerConfigs`、`PeerParties`），再发 Bulk 的
   `BulkCodec.KindSaveArchive`（200000 字节 gzip 往返，且确实被压缩）；并验证主机记录入房者回包。
5. **延迟分发**：标题期 `gameHandler == null` 时消息进队列不丢；加载期继续排队；恢复后按 FIFO
   分发且每帧 ≤ 256 条（300 条 → 第一帧 256、剩 44）；`PeerDisconnected` 清除该 peer 积压、
   其它 peer 不受影响；回调抛异常不阻断同批后续消息（立即分发与排空两条路径）。
6. **Shutdown 与重启**：`Shutdown` 后 `Transport/Client/Host` 置空、队列与 peer 集合清空、
   旧 transport 收到 `Shutdown+Dispose`；旧会话已解绑（旧 transport 再触发事件不改变旧会话状态）；
   重新注入 fake 后可再次 `StartClient` 并重新 `Ready`。

## 4. 构建与运行

沙箱里的 `dotnet build` / `dotnet run` 会在 MSBuild 的 `Csc` 任务处失败
（`MSB3883 ... Access is denied`，编译器子进程的重定向 stdio 管道被沙箱拒绝，且无审批通道），
所以本仓库提供与 `tools/NetSmoke/build-netsmoke.ps1` 相同的本地 Roslyn 直编路径。

```powershell
# 首选（正常环境）：
dotnet build tools/SessionSmoke/SessionSmoke.csproj -c Release --configfile ../../.build/NuGet.config
dotnet tools/SessionSmoke/bin/Release/net8.0/SessionSmoke.dll

# dotnet run 形式（同样走构建）：
dotnet run --project tools/SessionSmoke/SessionSmoke.csproj -c Release --configfile .build/NuGet.config

# 沙箱/无 dotnet build 权限时的本地 Roslyn 直编（本环境实际使用）：
& tools/SessionSmoke/build-sessionsmoke.ps1
dotnet tools/SessionSmoke/bin/Release/net8.0/SessionSmoke.dll
```

退出码：`0` = 全部断言通过；`1` = 存在断言失败；`4` = 未处理异常。
运行结束时 stdout 会打印断言数与失败数；完整输出同时镜像到
`tools/SessionSmoke/bin/Release/net8.0/session-smoke.log`（不依赖 shell 重定向）。

## 5. 依赖：Newtonsoft.Json

`HandshakeCodec` 与 `NetworkRuntime` 依赖 Newtonsoft.Json。`SessionSmoke.csproj` 用
`$(USERPROFILE)\.nuget\packages\newtonsoft.json\13.0.4\lib\net6.0\Newtonsoft.Json.dll`
作为明确 `HintPath`（本机缓存已有 13.0.4，不联网安装依赖）；若该路径不存在，回退到仓库已有的
`.build\plugin\Newtonsoft.Json.dll`。`build-sessionsmoke.ps1` 采用同样的查找顺序，并把该 DLL 复制到输出目录。

## 6. 未验证范围

- **无游戏、无 Unity、无 BepInEx、无原生 DLL**：真实 `pn_*` 调用、真实 QUIC/BULK 传输、
  Unity 主线程时序、房间码 UI、原生层背压均未覆盖。
- 延迟队列的 4096 条 / 32MB 硬上限与 `deferredPaused` 背压路径未在本测试中触发（只覆盖
  常规排队、FIFO、每帧 256 与断开清理）。
- `DB`/`Plugin`/`PolarisNoelsTools` 是 stub，因此这里验证的是会话层与 `NetworkRuntime` 的
  逻辑契约，不代表游戏内端到端行为。
