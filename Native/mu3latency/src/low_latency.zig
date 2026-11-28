const std = @import("std");
const builtin = @import("builtin");
const w32 = @import("win32");

const LOW_LATENCY: i32 = 0x00050000;

const d3d11 = w32.graphics.direct3d11;
const dxgi = w32.graphics.dxgi;
const k32 = w32.kernel32;

pub const ERR_OK: i32 = 0;
pub const ERR_UNSUPPORTED: i32 = LOW_LATENCY | 1;
pub const ERR_ALREADY: i32 = LOW_LATENCY | 2;
pub const ERR_DEVICE: i32 = LOW_LATENCY | 3;
pub const ERR_VENDOR: i32 = LOW_LATENCY | 4;
pub const ERR_REFLEX_LOAD: i32 = LOW_LATENCY | 5;
pub const ERR_ANTI_LAG: i32 = LOW_LATENCY | 6;
pub const ERR_INACTIVE: i32 = LOW_LATENCY | 7;
pub const ERR_FRAME: i32 = LOW_LATENCY | 8;
pub const ERR_REFLEX_INIT: i32 = LOW_LATENCY | 9;
pub const ERR_REFLEX_MODE: i32 = LOW_LATENCY | 10;
pub const ERR_REFLEX_STATUS: i32 = LOW_LATENCY | 11;
pub const ERR_REFLEX_OFF: i32 = LOW_LATENCY | 12;

pub const Backend = enum(u32) { none = 0, reflex = 1, anti_lag_2 = 2 };

const VENDOR_NVIDIA: u32 = 0x10DE;
const VENDOR_AMD: u32 = 0x1002;

const NvQueryInterface = *const fn (u32) callconv(.c) ?*anyopaque;
const NvInitialize = *const fn () callconv(.c) i32;
const NvSetSleepMode = *const fn (*anyopaque, *SleepModeParams) callconv(.c) i32;
const NvGetSleepStatus = *const fn (*anyopaque, *SleepStatusParams) callconv(.c) i32;
const NvSleep = *const fn (*anyopaque) callconv(.c) i32;

const NV_INITIALIZE: u32 = 0x0150E828;
const NV_D3D_SET_SLEEP_MODE: u32 = 0xAC1CA9E0;
const NV_D3D_GET_SLEEP_STATUS: u32 = 0xAEF96CA1;
const NV_D3D_SLEEP: u32 = 0x852CD1D2;

const SleepModeParams = extern struct {
    version: u32,
    low_latency_mode: u8,
    low_latency_boost: u8,
    minimum_interval_us: u32,
    use_markers_to_optimize: u8,
    use_min_queue_time: u8,
    reserved: [30]u8,
};

const SleepStatusParams = extern struct {
    version: u32,
    low_latency_mode: u8,
    fullscreen_vrr: u8,
    control_panel_vsync: u8,
    sleep_interval_us: u32,
    use_game_sleep: u8,
    fullscreen_iflip: u8,
    frame_generation_multiplier: u8,
    dynamic_frame_generation: u8,
    dynamic_frame_generation_target_us: u32,
    reserved: [114]u8,
};

comptime {
    std.debug.assert(@sizeOf(SleepModeParams) == 44);
    std.debug.assert(@sizeOf(SleepStatusParams) == 136);
    std.debug.assert(@sizeOf(AntiLagData) == 32);
}

fn nvVersion(comptime T: type) u32 {
    return @sizeOf(T) | (1 << 16);
}

const AntiLagData = extern struct {
    size: u32,
    version: u32,
    mode: u32,
    control: ?[*]const u8,
    control_length: u32,
    max_fps: u32,
};

const AmdDxExtCreate11 = *const fn (?*anyopaque, *?*anyopaque) callconv(.c) i32;
const AntiLagUpdate = *const fn (*anyopaque, ?*AntiLagData) callconv(.c) i32;
const ANTI_LAG_REQUEST: u64 = 0xbf380ebc5ab4d0a6;
const ANTI_LAG_UPDATE_SLOT: usize = 3;
const ANTI_LAG_ENABLED: u32 = 1;
const ANTI_LAG_DISABLED: u32 = 2;
const anti_lag_control = "delag_next_osd_supported_in_dxxp = 1";

const State = struct {
    backend: Backend = .none,
    device: ?*anyopaque = null,
    nv_sleep: ?NvSleep = null,
    nv_sleep_status: ?NvGetSleepStatus = null,
    anti_lag: ?*anyopaque = null,
    detail: i32 = 0,
};

var started: std.atomic.Value(bool) = .init(false);
var state: State = .{};

fn deviceOf(texture: *anyopaque) ?*anyopaque {
    const child: *const d3d11.ID3D11DeviceChild = @ptrCast(@alignCast(texture));
    var device: ?*d3d11.ID3D11Device = null;
    child.GetDevice(&device);
    return @ptrCast(device);
}

fn vendorOf(device: *anyopaque) ?u32 {
    const unknown: *const w32.system.com.IUnknown = @ptrCast(@alignCast(device));
    var raw: ?*anyopaque = null;
    if (unknown.QueryInterface(dxgi.IID_IDXGIDevice, @ptrCast(&raw)).failed) return null;
    const dxgi_device: *const dxgi.IDXGIDevice = @ptrCast(@alignCast(raw orelse return null));
    defer _ = dxgi_device.IUnknown.Release();
    var adapter: *dxgi.IDXGIAdapter = undefined;
    if (dxgi_device.GetAdapter(&adapter).failed) return null;
    defer _ = adapter.IUnknown.Release();
    var description: dxgi.DXGI_ADAPTER_DESC = undefined;
    if (adapter.GetDesc(&description).failed) return null;
    return description.VendorId;
}

fn startReflex(device: *anyopaque) i32 {
    const module = k32.LoadLibraryW(std.unicode.utf8ToUtf16LeStringLiteral("nvapi64.dll")) orelse return ERR_REFLEX_LOAD;
    const query: NvQueryInterface = @ptrCast(k32.GetProcAddress(module, "nvapi_QueryInterface") orelse return ERR_REFLEX_LOAD);
    const initialize: NvInitialize = @ptrCast(query(NV_INITIALIZE) orelse return ERR_REFLEX_LOAD);
    const set_sleep_mode: NvSetSleepMode = @ptrCast(query(NV_D3D_SET_SLEEP_MODE) orelse return ERR_REFLEX_LOAD);
    const get_sleep_status: NvGetSleepStatus = @ptrCast(query(NV_D3D_GET_SLEEP_STATUS) orelse return ERR_REFLEX_LOAD);
    const sleep: NvSleep = @ptrCast(query(NV_D3D_SLEEP) orelse return ERR_REFLEX_LOAD);
    state.detail = initialize();
    if (state.detail != 0) return ERR_REFLEX_INIT;
    var mode = std.mem.zeroes(SleepModeParams);
    mode.version = nvVersion(SleepModeParams);
    mode.low_latency_mode = 1;
    state.detail = set_sleep_mode(device, &mode);
    if (state.detail != 0) return ERR_REFLEX_MODE;
    state.nv_sleep = sleep;
    state.nv_sleep_status = get_sleep_status;
    state.backend = .reflex;
    return ERR_OK;
}

fn antiLagUpdate(api: *anyopaque) AntiLagUpdate {
    const vtable: *const [ANTI_LAG_UPDATE_SLOT + 1]*const anyopaque = @ptrCast(@alignCast(@as(*const *const anyopaque, @ptrCast(@alignCast(api))).*));
    return @ptrCast(vtable[ANTI_LAG_UPDATE_SLOT]);
}

pub fn verify(sleep_interval_us: *u32) i32 {
    sleep_interval_us.* = 0;
    if (state.backend != .reflex) return ERR_OK;
    var current = std.mem.zeroes(SleepStatusParams);
    current.version = nvVersion(SleepStatusParams);
    state.detail = state.nv_sleep_status.?(state.device.?, &current);
    if (state.detail != 0) return ERR_REFLEX_STATUS;
    sleep_interval_us.* = current.sleep_interval_us;
    return if (current.low_latency_mode != 0 and current.use_game_sleep != 0) ERR_OK else ERR_REFLEX_OFF;
}

fn startAntiLag() i32 {
    const module = k32.GetModuleHandleA("amdxx64.dll") orelse return ERR_ANTI_LAG;
    const create: AmdDxExtCreate11 = @ptrCast(k32.GetProcAddress(module, "AmdDxExtCreate11") orelse return ERR_ANTI_LAG);
    var api: ?*anyopaque = @ptrFromInt(ANTI_LAG_REQUEST);
    state.detail = create(null, &api);
    if (state.detail != 0) return ERR_ANTI_LAG;
    const interface = api orelse return ERR_ANTI_LAG;
    const update = antiLagUpdate(interface);
    var initial = AntiLagData{
        .size = @sizeOf(AntiLagData),
        .version = 1,
        .mode = ANTI_LAG_DISABLED,
        .control = null,
        .control_length = 0,
        .max_fps = 0,
    };
    var enabled = AntiLagData{
        .size = @sizeOf(AntiLagData),
        .version = 1,
        .mode = ANTI_LAG_ENABLED,
        .control = anti_lag_control,
        .control_length = anti_lag_control.len,
        .max_fps = 0,
    };
    state.detail = update(interface, &initial);
    if (state.detail == 0) state.detail = update(interface, &enabled);
    if (state.detail != 0) return ERR_ANTI_LAG;
    state.anti_lag = interface;
    state.backend = .anti_lag_2;
    return ERR_OK;
}

pub fn init(texture: ?*anyopaque, backend: *u32) i32 {
    backend.* = @intFromEnum(Backend.none);
    if (builtin.os.tag != .windows) return ERR_UNSUPPORTED;
    if (started.swap(true, .acq_rel)) return ERR_ALREADY;
    const device = deviceOf(texture orelse return ERR_DEVICE) orelse return ERR_DEVICE;
    state.device = device;
    const result = switch (vendorOf(device) orelse return ERR_DEVICE) {
        VENDOR_NVIDIA => startReflex(device),
        VENDOR_AMD => startAntiLag(),
        else => ERR_VENDOR,
    };
    backend.* = @intFromEnum(state.backend);
    return result;
}

pub fn frame() i32 {
    return switch (state.backend) {
        .none => ERR_INACTIVE,
        .reflex => blk: {
            state.detail = state.nv_sleep.?(state.device.?);
            break :blk if (state.detail == 0) ERR_OK else ERR_FRAME;
        },
        .anti_lag_2 => blk: {
            state.detail = antiLagUpdate(state.anti_lag.?)(state.anti_lag.?, null);
            break :blk switch (state.detail) {
                0, 1 => ERR_OK,
                else => ERR_FRAME,
            };
        },
    };
}

pub fn detail() i32 {
    return state.detail;
}
