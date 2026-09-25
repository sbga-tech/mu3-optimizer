//! GPU fence-wait initialization and shutdown return codes.
const status = @import("../status.zig");

pub const ERR_OK: i32 = 0;
pub const ERR_UNSUPPORTED: i32 = status.GPU | 1;
pub const ERR_ALREADY: i32 = status.GPU | 2;
pub const ERR_PE_IDENTITY: i32 = status.GPU | 3;
pub const ERR_PROLOGUE: i32 = status.GPU | 4;
pub const ERR_MINHOOK: i32 = status.GPU | 5;
pub const ERR_HOOK: i32 = status.GPU | 6;
pub const ERR_TLS: i32 = status.GPU | 7;
pub const ERR_SHUTDOWN: i32 = status.GPU | 8;
