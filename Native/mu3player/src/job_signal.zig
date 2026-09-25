//! Job-semaphore batching, initialized by the shared player lifecycle.
//!
//! Windows-only treatment; other hosts return `ERR_UNSUPPORTED` from init.
//! No shutdown export: the installed hook stays process-owned through teardown.

const builtin = @import("builtin");
const status = @import("status.zig");

pub const ERR_OK: i32 = 0;
/// Non-Windows host.
pub const ERR_UNSUPPORTED: i32 = status.JOB_SIGNAL | 1;
/// Init already attempted.
pub const ERR_ALREADY: i32 = status.JOB_SIGNAL | 2;
/// Invalid x64 player PE image.
pub const ERR_PLAYER: i32 = status.JOB_SIGNAL | 3;
/// Signal body missing/ambiguous, or its import-slot reference invalid.
pub const ERR_PROLOGUE: i32 = status.JOB_SIGNAL | 4;
/// MinHook initialize failed.
pub const ERR_MINHOOK: i32 = status.JOB_SIGNAL | 5;
/// Hook create/enable failed. Unactivated hooks are removed.
pub const ERR_HOOK: i32 = status.JOB_SIGNAL | 6;

const impl = if (builtin.os.tag == .windows)
    @import("job_signal/hook.zig")
else
    struct {
        fn init() i32 {
            return ERR_UNSUPPORTED;
        }
    };

/// Enable the Signal hook. Failure leaves the original implementation installed.
pub fn init() i32 {
    return impl.init();
}
