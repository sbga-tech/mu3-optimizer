//! Win32 `SharedGfxBuffer` destructor and `EndGraphicsJobsLate` hooks: deferred job buffer
//! deletes, and the queued graphics-jobs switch applied after the forward.
const std = @import("std");

const player = @import("../player.zig");
const minhook = @import("../minhook.zig");
const feature = @import("../graphics_jobs.zig");
const Pending = @import("pending.zig").Pending;

const DestructorFn = *const fn (*anyopaque, u32) callconv(.c) ?*anyopaque;
const ForwardFn = *const fn () callconv(.c) void;
const DeviceFn = *const fn () callconv(.c) ?*anyopaque;
const DeleteBufferFn = *const fn (*anyopaque, *anyopaque) callconv(.c) void;
const SettingsFn = *const fn () callconv(.c) ?*anyopaque;

/// `SharedGfxBuffer::m_GfxBuffer`, tested and cleared by the matched destructor.
const buffer_offset = 0x10;
/// A frame releases a few canvas buffers; a full list keeps Unity's immediate delete.
const capacity = 4096;

var started: std.atomic.Value(bool) = .init(false);
var main_device: usize = 0;
var threaded_device: DeviceFn = undefined;
var settings_getter: SettingsFn = undefined;
var pass_context_slot: *const ?*anyopaque = undefined;
var original_destructor: ?DestructorFn = null;
var original_forward: ?ForwardFn = null;
var pending: Pending(capacity) = .{};
/// Main-thread scratch for one drain.
var drained: [capacity]usize = undefined;
/// Main thread only, like the forward. Set once both hooks are enabled, so graphics jobs
/// never run without their deletes deferred.
var switch_pending = false;

fn hookedDestructor(self: *anyopaque, flags: u32) callconv(.c) ?*anyopaque {
    // A graphics job runs with its own client as the thread's device, on a worker or on the
    // main thread when a fence wait executes the job itself; only main-device deletes stay.
    if (@intFromPtr(threaded_device()) != main_device) {
        const field: *usize = @ptrFromInt(@intFromPtr(self) + buffer_offset);
        const buffer = field.*;
        // The original skips its delete for a null buffer and still frees the object.
        if (buffer != 0 and pending.push(buffer)) field.* = 0;
    }
    return original_destructor.?(self, flags);
}

fn hookedForward() callconv(.c) void {
    // With jobs off the original only checks the sync mode; with jobs on it completes the
    // previous frame's jobs, commits vertex declarations and advances the dynamic buffers.
    original_forward.?();
    drainDeletes();
    if (switch_pending) switchOnGraphicsJobs();
}

fn drainDeletes() void {
    const buffers = pending.take(&drained);
    if (buffers.len == 0) return;
    // The main thread's device is the main GfxDeviceClient; its delete joins the main queue.
    const device = threaded_device() orelse return;
    const vtable: *const [*]const usize = @ptrCast(@alignCast(device));
    const delete: DeleteBufferFn = @ptrFromInt(vtable.*[player.device_delete_buffer_slot / @sizeOf(usize)]);
    for (buffers) |buffer| delete(device, @ptrFromInt(buffer));
}

/// Runs after this frame's forward saw jobs off, so `PlayerEndFrame` skips its end-of-frame
/// job work for this frame and the next forward does it once, exactly as with jobs on from
/// startup. Both bytes are what the launch argument and the startup copy would have set.
fn switchOnGraphicsJobs() void {
    const settings = settings_getter() orelse return;
    const context = pass_context_slot.* orelse return;
    const graphics_jobs: *u8 = @ptrFromInt(@intFromPtr(settings) + player.settings_graphics_jobs_offset);
    const jobified: *u8 = @ptrFromInt(@intFromPtr(context) + player.pass_context_jobified_offset);
    graphics_jobs.* = 1;
    jobified.* = 1;
    switch_pending = false;
}

fn initInner() i32 {
    const image = player.current() catch return feature.ERR_PLAYER;
    const targets = image.resolveGraphicsJobs() catch return feature.ERR_PROLOGUE;
    minhook.initialize() catch return feature.ERR_MINHOOK;
    threaded_device = @ptrCast(image.address(targets.threaded_device));
    main_device = @intFromPtr(threaded_device() orelse return feature.ERR_DEVICE);
    settings_getter = @ptrCast(image.address(targets.settings_getter));
    pass_context_slot = @ptrCast(@alignCast(image.address(targets.pass_context_slot)));
    var hooks: minhook.HookSet = .{};
    hooks.add(image.address(targets.destructor), &hookedDestructor, &original_destructor) catch
        return feature.ERR_HOOK;
    hooks.add(image.address(targets.end_graphics_jobs), &hookedForward, &original_forward) catch {
        minhook.remove(image.address(targets.destructor)) catch {};
        return feature.ERR_HOOK;
    };
    hooks.enableFrom(0) catch {
        hooks.rollbackFrom(0);
        return feature.ERR_HOOK;
    };
    switch_pending = true;
    return feature.ERR_OK;
}

pub fn init() i32 {
    if (started.swap(true, .acq_rel)) return feature.ERR_ALREADY;
    return initInner();
}
