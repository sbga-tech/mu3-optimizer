//! Unity GPU (stream-out) skinning switched on at run time; initialized by the shared player
//! lifecycle.
//!
//! `SkinnedMeshRenderer::DoesQualifyForGPUSkinning` is the only reader of
//! `PlayerSettings.gpuSkinning`. It runs at every skin preparation and also requires no active
//! blend shapes and the D3D11 stream-out capability, so setting the byte after startup moves
//! qualifying renderers to GPU skinning from their next preparation. Renderers with active blend
//! shapes and devices without the capability keep CPU skinning.
//!
//! Windows-only treatment; other hosts return `ERR_UNSUPPORTED` from init.
//! One-way: GPU skinning stays on until the process exits.

const std = @import("std");
const builtin = @import("builtin");
const player = @import("player.zig");
const status = @import("status.zig");

pub const ERR_OK: i32 = 0;
/// Non-Windows host.
pub const ERR_UNSUPPORTED: i32 = status.GPU_SKINNING | 1;
/// Init already attempted.
pub const ERR_ALREADY: i32 = status.GPU_SKINNING | 2;
/// Invalid x64 player PE image.
pub const ERR_PLAYER: i32 = status.GPU_SKINNING | 3;
/// Qualifier, settings getter or game-manager lookup missing, ambiguous or inconsistent.
pub const ERR_PROLOGUE: i32 = status.GPU_SKINNING | 4;
/// The player settings manager does not exist yet.
pub const ERR_SETTINGS: i32 = status.GPU_SKINNING | 5;

const SettingsFn = *const fn () callconv(.c) ?*anyopaque;

var started: std.atomic.Value(bool) = .init(false);

/// Must be called on Unity's main thread, the thread that prepares skins.
/// Failure leaves the setting unchanged.
pub fn init() i32 {
    if (builtin.os.tag != .windows) return ERR_UNSUPPORTED;
    if (started.swap(true, .acq_rel)) return ERR_ALREADY;
    const image = player.current() catch return ERR_PLAYER;
    const targets = image.resolveGpuSkinning() catch return ERR_PROLOGUE;
    const getter: SettingsFn = @ptrCast(image.address(targets.settings_getter));
    const settings = getter() orelse return ERR_SETTINGS;
    const enabled: *u8 = @ptrFromInt(@intFromPtr(settings) + player.settings_gpu_skinning_offset);
    enabled.* = 1;
    return ERR_OK;
}
