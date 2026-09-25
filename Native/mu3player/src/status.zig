//! Packed native/managed i32 status: 0 is success.
//! Upper 16 bits identify player (0) or hook; lower 16 bits are a sequential reason.

pub const GPU: i32 = 0x00010000;
pub const JOB_SIGNAL: i32 = 0x00020000;
pub const PLAYER: i32 = 0x00000000;
pub const ERR_INVALID_FEATURE: i32 = PLAYER | 1;
