//! CappedSemaphore counter transition and kernel batch release.
const std = @import("std");

pub const HANDLE = ?std.os.windows.HANDLE;

/// Binary layout of Unity `CappedSemaphore` (x64).
pub const CappedSemaphore = extern struct {
    current: i32,
    pad: u32 = 0,
    maximum: u64,
    handle: HANDLE,
};

const LONG_MAX: u64 = 0x7fff_ffff;

comptime {
    if (@offsetOf(CappedSemaphore, "current") != 0) @compileError("current must be at 0");
    if (@offsetOf(CappedSemaphore, "maximum") != 8) @compileError("maximum must be at 8");
    if (@offsetOf(CappedSemaphore, "handle") != 16) @compileError("handle must be at 16");
    if (@sizeOf(CappedSemaphore) != 24) @compileError("CappedSemaphore must be 24 bytes");
    if (@alignOf(CappedSemaphore) != 8) @compileError("CappedSemaphore must be 8-aligned");
}

/// SeqCst fence matching `lock or dword ptr [rsp], 0`. Zig 0.16 has no `@fence`.
fn seqCstFence() void {
    var cell: u32 align(4) = 0;
    _ = @atomicRmw(u32, &cell, .Or, 0, .seq_cst);
}

/// Keep negative waiter debt signed; only surplus permits have an upper cap.
fn nextValue(old: i32, count: i32, maximum: u64) i32 {
    const bits = @as(u32, @bitCast(old)) +% @as(u32, @bitCast(count));
    var next: i32 = @bitCast(bits);
    if (next >= 0 and @as(u64, @intCast(next)) > maximum) {
        next = @bitCast(@as(u32, @truncate(maximum)));
    }
    return next;
}

fn canBatch(count: u64) bool {
    return count > 1 and count <= LONG_MAX;
}

/// SeqCst fence once, then Relaxed loads and SeqCst CAS, matching `lock or`
/// plus `lock cmpxchg`. Release only the waiter debt repaid by this transition.
pub fn commit(current: *i32, maximum: u64, count: i32) u64 {
    seqCstFence();
    while (true) {
        const old = @atomicLoad(i32, current, .monotonic);
        const next = nextValue(old, count, maximum);
        if (next == old) return 0;
        if (@cmpxchgStrong(i32, current, old, next, .seq_cst, .monotonic) != null) continue;
        if (old >= 0 or next <= old) return 0;
        const end: i64 = if (next < 0) next else 0;
        return @bitCast(end - @as(i64, old));
    }
}

/// Batch only when `1 < count <= LONG_MAX`. A failed multi-release does not
/// change the kernel count; replay singles without repeating the committed CAS.
/// Failed single releases cannot be repaired after the counter update.
/// `ctx` is monomorphized so the production hook has no vtable or heap traffic.
pub fn release(handle: HANDLE, count: u64, ctx: anytype) void {
    if (canBatch(count) and ctx.tryRelease(handle, @intCast(count))) return;
    var left = count;
    while (left != 0) : (left -= 1) {
        _ = ctx.tryRelease(handle, 1);
    }
}

test "failed batch replays each wake once without repeating the counter update" {
    const Kernel = struct {
        attempts: u32 = 0,
        wakes: u32 = 0,

        fn tryRelease(self: *@This(), _: HANDLE, count: i32) bool {
            self.attempts += 1;
            if (count > 1) return false;
            self.wakes += @intCast(count);
            return true;
        }
    };
    var current: i32 = -3;
    var kernel: Kernel = .{};
    release(null, commit(&current, 31, 3), &kernel);
    try std.testing.expectEqual(@as(i32, 0), current);
    try std.testing.expectEqual(@as(u32, 3), kernel.wakes);
    try std.testing.expectEqual(@as(u32, 4), kernel.attempts);
}

test "partial signals preserve outstanding waiters without surplus permits" {
    var current: i32 = -30;
    try std.testing.expectEqual(@as(u64, 0), commit(&current, 31, 0));
    try std.testing.expectEqual(@as(i32, -30), current);
    try std.testing.expectEqual(@as(u64, 1), commit(&current, 31, 1));
    try std.testing.expectEqual(@as(i32, -29), current);
    try std.testing.expectEqual(@as(u64, 8), commit(&current, 31, 8));
    try std.testing.expectEqual(@as(i32, -21), current);
    try std.testing.expectEqual(@as(u64, 21), commit(&current, 31, 25));
    try std.testing.expectEqual(@as(i32, 4), current);
    try std.testing.expectEqual(@as(u64, 0), commit(&current, 31, 31));
    try std.testing.expectEqual(@as(i32, 31), current);
}

test "concurrent producers release each registered waiter once" {
    const Producers = struct {
        current: i32 = -31,
        released: std.atomic.Value(u64) = .init(0),
        start: std.atomic.Value(bool) = .init(false),

        fn run(self: *@This(), count: i32) void {
            while (!self.start.load(.acquire)) std.atomic.spinLoopHint();
            _ = self.released.fetchAdd(commit(&self.current, 31, count), .monotonic);
        }
    };
    var producers: Producers = .{};
    var threads: [4]std.Thread = undefined;
    var started: usize = 0;
    defer {
        producers.start.store(true, .release);
        for (threads[0..started]) |thread| thread.join();
    }
    for ([_]i32{ 1, 3, 7, 20 }) |count| {
        threads[started] = try std.Thread.spawn(.{}, Producers.run, .{ &producers, count });
        started += 1;
    }
    producers.start.store(true, .release);
    for (threads) |thread| thread.join();
    started = 0;
    try std.testing.expectEqual(@as(u64, 31), producers.released.load(.monotonic));
    try std.testing.expectEqual(@as(i32, 0), producers.current);
}

test "Windows partial signals leave other waiters parked and drain without lost wakes" {
    if (@import("builtin").os.tag != .windows) return error.SkipZigTest;
    const Kernel = struct {
        extern "kernel32" fn CreateSemaphoreW(?*anyopaque, i32, i32, ?[*:0]const u16) callconv(.c) HANDLE;
        extern "kernel32" fn ReleaseSemaphore(HANDLE, i32, ?*i32) callconv(.c) i32;
        extern "kernel32" fn WaitForSingleObject(HANDLE, u32) callconv(.c) u32;
        extern "kernel32" fn CloseHandle(HANDLE) callconv(.c) i32;
        extern "kernel32" fn GetTickCount64() callconv(.c) u64;
        extern "kernel32" fn Sleep(u32) callconv(.c) void;

        current: i32 = 0,
        handle: HANDLE,
        completed: HANDLE,
        failures: std.atomic.Value(u32) = .init(0),

        fn run(self: *@This()) void {
            const old = @atomicRmw(i32, &self.current, .Sub, 1, .seq_cst);
            if (old <= 0 and WaitForSingleObject(self.handle, 10000) != 0) {
                _ = self.failures.fetchAdd(1, .monotonic);
            }
            if (ReleaseSemaphore(self.completed, 1, null) == 0) {
                _ = self.failures.fetchAdd(1, .monotonic);
            }
        }

        fn tryRelease(self: *@This(), handle: HANDLE, count: i32) bool {
            const released = ReleaseSemaphore(handle, count, null) != 0;
            if (!released) _ = self.failures.fetchAdd(1, .monotonic);
            return released;
        }
    };
    const handle = Kernel.CreateSemaphoreW(null, 0, 31, null) orelse return error.CreateSemaphoreFailed;
    defer _ = Kernel.CloseHandle(handle);
    const completed = Kernel.CreateSemaphoreW(null, 0, 31, null) orelse return error.CreateSemaphoreFailed;
    defer _ = Kernel.CloseHandle(completed);
    var kernel: Kernel = .{ .handle = handle, .completed = completed };
    var threads: [4]std.Thread = undefined;
    var started: usize = 0;
    defer {
        if (started != 0) _ = Kernel.ReleaseSemaphore(handle, 4, null);
        for (threads[0..started]) |thread| thread.join();
    }
    for (&threads) |*thread| {
        thread.* = try std.Thread.spawn(.{}, Kernel.run, .{&kernel});
        started += 1;
    }
    const deadline = Kernel.GetTickCount64() + 10000;
    while (@atomicLoad(i32, &kernel.current, .seq_cst) != -4) {
        if (Kernel.GetTickCount64() >= deadline) return error.WaitersDidNotRegister;
        Kernel.Sleep(0);
    }
    release(handle, commit(&kernel.current, 31, 1), &kernel);
    try std.testing.expectEqual(@as(u32, 0), Kernel.WaitForSingleObject(completed, 10000));
    try std.testing.expectEqual(@as(i32, -3), @atomicLoad(i32, &kernel.current, .seq_cst));
    release(handle, commit(&kernel.current, 31, 3), &kernel);
    for (0..3) |_| try std.testing.expectEqual(@as(u32, 0), Kernel.WaitForSingleObject(completed, 10000));
    for (threads) |thread| thread.join();
    started = 0;
    try std.testing.expectEqual(@as(u32, 0), kernel.failures.load(.monotonic));
    try std.testing.expectEqual(@as(i32, 0), kernel.current);
    try std.testing.expectEqual(@as(u32, 258), Kernel.WaitForSingleObject(handle, 0));
    try std.testing.expectEqual(@as(u32, 258), Kernel.WaitForSingleObject(completed, 0));
}
