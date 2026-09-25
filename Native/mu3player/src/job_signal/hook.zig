//! Win32 `CappedSemaphore::Signal` hook; failed batch releases replay as singles.
const std = @import("std");
const w32 = @import("win32");

const player = @import("../player.zig");
const minhook = @import("../minhook.zig");
const job = @import("../job_signal.zig");
const semaphore = @import("semaphore.zig");

const ReleaseSemaphore = w32.kernel32.ReleaseSemaphore;
const SignalFn = *const fn (*anyopaque, i32) callconv(.c) void;

var started: std.atomic.Value(bool) = .init(false);

const Kernel = struct {
    pub fn tryRelease(_: *const @This(), handle: semaphore.HANDLE, count: i32) bool {
        return ReleaseSemaphore(handle, count, null) != 0;
    }
};
fn hookedSignal(raw: *anyopaque, count: i32) callconv(.c) void {
    const sem: *align(1) const semaphore.CappedSemaphore = @ptrCast(raw);
    const current: *i32 = @alignCast(@constCast(&sem.current));
    const wake = semaphore.commit(current, sem.maximum, count);
    if (wake == 0) return;
    const kernel: Kernel = .{};
    semaphore.release(sem.handle, wake, &kernel);
}

fn initInner() i32 {
    const image = player.current() catch return job.ERR_PLAYER;
    const targets = image.resolveSignal() catch return job.ERR_PROLOGUE;
    minhook.initialize() catch return job.ERR_MINHOOK;

    const target = image.address(targets.signal);
    var trampoline: ?SignalFn = null;
    minhook.create(target, &hookedSignal, &trampoline) catch return job.ERR_HOOK;
    minhook.enable(target) catch {
        minhook.remove(target) catch {};
        return job.ERR_HOOK;
    };
    return job.ERR_OK;
}

pub fn init() i32 {
    if (started.swap(true, .acq_rel)) return job.ERR_ALREADY;
    return initInner();
}
