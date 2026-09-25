//! One C ABI for independent process-owned Unity player hooks.
const gpu_wait = @import("gpu_wait/runtime.zig");
const job_signal = @import("job_signal.zig");
const status = @import("status.zig");
comptime {
    if (@sizeOf(*anyopaque) != 8) @compileError("Player hooks require 64-bit pointers");
}

pub const GPU_WAIT: u32 = 1;
pub const JOB_SIGNAL: u32 = 2;

/// Initialize one hook per call so each requested hook returns its own status.
/// A combined/unknown selector is an error; it must not silently lose a result.
pub export fn mu3_player_init(feature: u32) i32 {
    return switch (feature) {
        GPU_WAIT => gpu_wait.init(),
        JOB_SIGNAL => job_signal.init(),
        else => status.ERR_INVALID_FEATURE,
    };
}

/// Only GPU waits need explicit teardown; the job hook stays installed.
export fn mu3_player_shutdown() i32 {
    return gpu_wait.shutdown();
}
