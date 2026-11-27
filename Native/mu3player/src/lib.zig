//! One C ABI for independent process-owned Unity player hooks.
const gpu_wait = @import("gpu_wait/runtime.zig");
const gpu_skinning = @import("gpu_skinning.zig");
const graphics_jobs = @import("graphics_jobs.zig");
const job_signal = @import("job_signal.zig");
const status = @import("status.zig");
comptime {
    if (@sizeOf(*anyopaque) != 8) @compileError("Player hooks require 64-bit pointers");
}

pub const GPU_WAIT: u32 = 1;
pub const JOB_SIGNAL: u32 = 2;
pub const GRAPHICS_JOBS: u32 = 4;
pub const GPU_SKINNING: u32 = 8;

/// Initialize one hook per call so each requested hook returns its own status.
/// A combined/unknown selector is an error; it must not silently lose a result.
pub export fn mu3_player_init(feature: u32) i32 {
    return switch (feature) {
        GPU_WAIT => gpu_wait.init(),
        JOB_SIGNAL => job_signal.init(),
        GRAPHICS_JOBS => graphics_jobs.init(),
        GPU_SKINNING => gpu_skinning.init(),
        else => status.ERR_INVALID_FEATURE,
    };
}

/// Only GPU waits need explicit teardown; the job hooks stay installed.
export fn mu3_player_shutdown() i32 {
    return gpu_wait.shutdown();
}

export fn mu3_gpu_wait_ns(nanoseconds: *u64) i32 {
    return gpu_wait.waited(nanoseconds);
}
