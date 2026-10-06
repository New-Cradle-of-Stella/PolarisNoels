#include "polaris_net.h"
#include <stddef.h>
_Static_assert(sizeof(pn_config) == 32, "pn_config size");
_Static_assert(offsetof(pn_config, stun_servers) == 16, "STUN pointer offset");
_Static_assert(sizeof(pn_event) == 32, "pn_event size");
_Static_assert(sizeof(pn_peer_stats_t) == 32, "pn_peer_stats size");
_Static_assert(offsetof(pn_peer_stats_t, path_kind) == 28, "path_kind offset");
