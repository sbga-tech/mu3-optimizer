//! Unity graphics jobs switched on at run time, with job buffer deletes deferred to the main
//! thread; initialized by the shared player lifecycle.
//!
//! On D3D11, `PlayerSettings.graphicsJobs` and the root pass context's
//! `isUsingJobifiedRendering` are the only state that decides graphics-job use after startup;
//! the launch argument sets the first before device creation and the second is copied from
//! it once. The `EndGraphicsJobsLate` hook sets both after the first forward following init,
//! so the end-of-frame job work runs exactly once per frame.
//!
//! With graphics jobs, the job that finishes last runs render-node cleanup and records its
//! canvas buffer deletes into its own command stream. The render thread replays job streams
//! in scheduling order, so a later-scheduled job can replay draws of the freed buffers.
//! When the destroying thread's device is not the main client (a graphics job on a worker, or
//! on the main thread while a fence wait executes the job), the destructor's buffer is handed
//! to the main thread, which deletes it after `PreLateUpdate.EndGraphicsJobsLate`: the
//! main-queue delete then follows every job stream that can still reference the buffer.
//! The switch is queued only after both hooks are enabled.
//!
//! Windows-only treatment; other hosts return `ERR_UNSUPPORTED` from init.
//! No shutdown export: the hooks stay process-owned through teardown, and graphics jobs stay
//! on until the process exits.

const builtin = @import("builtin");
const status = @import("status.zig");

pub const ERR_OK: i32 = 0;
/// Non-Windows host.
pub const ERR_UNSUPPORTED: i32 = status.GRAPHICS_JOBS | 1;
/// Init already attempted.
pub const ERR_ALREADY: i32 = status.GRAPHICS_JOBS | 2;
/// Invalid x64 player PE image.
pub const ERR_PLAYER: i32 = status.GRAPHICS_JOBS | 3;
/// Destructor, TLS device getter, player-loop forward, pass-context initializer, settings
/// getter or per-frame readers missing, ambiguous or inconsistent.
pub const ERR_PROLOGUE: i32 = status.GRAPHICS_JOBS | 4;
/// MinHook initialize failed.
pub const ERR_MINHOOK: i32 = status.GRAPHICS_JOBS | 5;
/// Hook create/enable failed. Unactivated hooks are removed or disabled.
pub const ERR_HOOK: i32 = status.GRAPHICS_JOBS | 6;
/// The calling (main) thread had no graphics device at init.
pub const ERR_DEVICE: i32 = status.GRAPHICS_JOBS | 7;

const impl = if (builtin.os.tag == .windows)
    @import("graphics_jobs/hook.zig")
else
    struct {
        fn init() i32 {
            return ERR_UNSUPPORTED;
        }
    };

/// Must be called on Unity's main thread, whose device identifies non-job deletes.
/// Failure leaves Unity's code and graphics-jobs state unchanged.
pub fn init() i32 {
    return impl.init();
}

test {
    _ = @import("graphics_jobs/pending.zig");
}
