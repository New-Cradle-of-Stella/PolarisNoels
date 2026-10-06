#pragma once
#ifdef __cplusplus
extern "C" {
#endif
#include <stdint.h>

#define PN_ABI_VERSION 1

/* ---- 返回码 ---- */
#define PN_OK              0
#define PN_ERR_INVALID    -1   /* 参数非法 */
#define PN_ERR_STATE      -2   /* 状态不允许（未 init / 已 init 等） */
#define PN_ERR_NOT_FOUND  -3   /* peer 不存在 */
#define PN_ERR_BUFFER     -4   /* 调用方缓冲区不足，*out_len 里是需要的大小 */
#define PN_ERR_QUEUE_FULL -5   /* 发送队列已满（调用方须处理可靠发送的重试或断开） */
#define PN_ERR_INTERNAL  -99   /* 内部错误/panic，详见 pn_last_error */

/* ---- 通道 ---- */
#define PN_CH_RELIABLE    0    /* 可靠有序（QUIC 双向流，长度前缀分帧） */
#define PN_CH_SEQUENCED   1    /* 不可靠按序：QUIC DATAGRAM + 4 字节序号，旧包丢弃 */
#define PN_CH_BULK        2    /* 大块可靠传输：每条消息一个独立单向流，带进度事件 */

/* ---- 配置 ---- */
typedef struct pn_config {
    uint32_t abi_version;       /* 必须 = PN_ABI_VERSION */
    uint16_t bind_port;         /* 0 = 随机 */
    uint16_t max_peers;         /* 含自己，2~8 */
    uint8_t  enable_stun;       /* 1 = 收集公网候选 */
    uint8_t  enable_lan;        /* 1 = 收集内网候选（同局域网直连） */
    uint8_t  enable_upnp;       /* 预留，当前忽略 */
    uint8_t  reserved0;
    const char* stun_servers;   /* 逗号分隔 "host:port,host:port"，NULL = 内置默认 */
    uint32_t idle_timeout_ms;   /* 对端无响应判定断线，0 = 默认 20000 */
    uint32_t reserved1;
} pn_config;

/* ---- 事件 ---- */
typedef enum pn_event_type {
    PN_EV_NONE            = 0,
    PN_EV_PEER_CONNECTED  = 1,  /* peer 已完成 QUIC 握手，可收发 */
    PN_EV_PEER_DISCONNECTED = 2,/* reason 见 pn_disc_reason */
    PN_EV_MESSAGE         = 3,  /* 数据在 buf 里：channel / peer / len */
    PN_EV_BULK_PROGRESS   = 4,  /* peer / bulk_id / done / total */
    PN_EV_NAT_STATE       = 5,  /* 候选收集与 NAT 类型结果，见 pn_nat_info（JSON 文本在 buf） */
    PN_EV_JOIN_RESULT     = 6,  /* pn_join 的最终结果：code = 0 成功，否则 pn_join_error */
    PN_EV_LOG             = 7   /* 日志行（UTF-8）在 buf，level 放在 code */
} pn_event_type;

typedef enum pn_disc_reason {
    PN_DISC_LOCAL     = 0,      /* 本地主动断开 */
    PN_DISC_REMOTE    = 1,      /* 对端主动断开 */
    PN_DISC_KICKED    = 2,      /* 被主机踢出 */
    PN_DISC_TIMEOUT   = 3,
    PN_DISC_PROTOCOL  = 4,      /* 版本/房间密钥不匹配等 */
    PN_DISC_ROOM_FULL = 5,
    PN_DISC_HOST_CLOSED = 6     /* 主机关闭了房间 */
} pn_disc_reason;

typedef enum pn_join_error {
    PN_JOIN_OK = 0,
    PN_JOIN_BAD_CODE = 1,       /* 房间码无法解析/版本不符 */
    PN_JOIN_PUNCH_TIMEOUT = 2,  /* 所有候选都打洞失败（多半是对称 NAT） */
    PN_JOIN_HANDSHAKE_FAILED = 3, /* QUIC/证书指纹/房间密钥校验失败 */
    PN_JOIN_ROOM_FULL = 4
} pn_join_error;

typedef struct pn_event {
    int32_t  type;              /* pn_event_type */
    int32_t  peer;              /* peer id；与事件无关时为 -1 */
    int32_t  channel;           /* PN_CH_* ；仅 MESSAGE 有效 */
    int32_t  code;              /* 含义随 type：reason / join_error / log level */
    uint32_t len;               /* 本次写入 buf 的字节数 */
    uint32_t aux0;              /* BULK_PROGRESS: done；其他: 0 */
    uint32_t aux1;              /* BULK_PROGRESS: total */
    uint32_t aux2;              /* BULK_PROGRESS: bulk_id */
} pn_event;

typedef struct pn_peer_stats {
    uint32_t rtt_ms;            /* 平滑 RTT */
    uint32_t loss_permille;     /* 估算丢包千分比 */
    uint64_t bytes_sent;
    uint64_t bytes_recv;
    uint32_t send_queue_bytes;  /* 尚未发出的排队字节 */
    uint8_t  path_kind;         /* 0 = 未知, 1 = 直连(LAN), 2 = 直连(公网/打洞), 3 = 经主机转发 */
    uint8_t  reserved[3];
} pn_peer_stats_t;

/* ---- 生命周期 ---- */
int32_t pn_abi_version(void);                    /* 返回库的 ABI 版本，C# 启动时必须与 PN_ABI_VERSION 比对 */
int32_t pn_init(const pn_config* cfg);           /* 启动运行时与 UDP 套接字；重复调用返回 PN_ERR_STATE */
void    pn_shutdown(void);                       /* 优雅关闭：通知所有对端、停止运行时、释放套接字；可重复调用 */

/* ---- 建房 / 入房 ---- */
/* 主机：开始监听。收集候选地址（异步），完成后生成房间码并通过 PN_EV_NAT_STATE 事件给出。
   同步返回只表示已受理。房间码也可通过 pn_room_code 取回。 */
int32_t pn_host_start(void);
int32_t pn_room_code(uint8_t* out, uint32_t cap, uint32_t* out_len);  /* UTF-8，无结尾 \0 */

/* 客户端：用房间码入房。异步；结果通过 PN_EV_JOIN_RESULT 给出。
   成功后会自动与房间内所有现有玩家建立连接，每个连接各产生一次 PN_EV_PEER_CONNECTED。 */
int32_t pn_join(const uint8_t* room_code, uint32_t len);

/* 直连模式（局域网/已端口转发）：不做 STUN/打洞，直接 QUIC 连 host:port。
   fingerprint 可为 NULL（此时只信任一次，日志警告）。 */
int32_t pn_join_direct(const char* host, uint16_t port, const uint8_t* fingerprint32);

/* ---- 数据 ---- */
/* peer = -1 表示广播给所有已连接对端。数据会被拷贝，返回后调用方可立即释放。 */
int32_t pn_send(int32_t peer, int32_t channel, const uint8_t* data, uint32_t len);
int32_t pn_disconnect(int32_t peer, int32_t reason);   /* reason 传 PN_DISC_KICKED 表示踢出（仅主机有效） */

/* ---- 轮询 ---- */
/* 取出一个事件。有事件返回 1，无事件返回 0，缓冲不足返回 PN_ERR_BUFFER（此时事件不丢，ev->len 是所需大小）。
   MESSAGE/NAT_STATE/LOG 的载荷写入 buf（最多 cap 字节）。 */
int32_t pn_poll(pn_event* ev, uint8_t* buf, uint32_t cap);

/* ---- 查询 ---- */
int32_t pn_peer_stats(int32_t peer, pn_peer_stats_t* out);
int32_t pn_local_peer_id(void);                        /* 主机 = 0；入房成功前返回 -1 */
int32_t pn_peer_count(void);                           /* 已连接对端数（不含自己） */
int32_t pn_last_error(uint8_t* out, uint32_t cap, uint32_t* out_len);  /* 当前线程最近一次错误的 UTF-8 文本 */

#ifdef __cplusplus
}
#endif
