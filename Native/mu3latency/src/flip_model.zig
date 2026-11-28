const std = @import("std");
const builtin = @import("builtin");
const w32 = @import("win32");
const player = @import("player");

const dxgi = w32.graphics.dxgi;
const d3d11 = w32.graphics.direct3d11;
const memory = w32.system.memory;
const kernel32 = w32.kernel32;

const FLIP_MODEL: i32 = 0x00060000;
pub const ERR_OK: i32 = 0;
pub const ERR_UNSUPPORTED: i32 = FLIP_MODEL | 1;
pub const ERR_ALREADY: i32 = FLIP_MODEL | 2;
pub const ERR_PLAYER: i32 = FLIP_MODEL | 3;
pub const ERR_PROLOGUE: i32 = FLIP_MODEL | 4;
pub const ERR_NO_SWAP_CHAIN: i32 = FLIP_MODEL | 5;
pub const ERR_PROTECT: i32 = FLIP_MODEL | 6;
pub const ERR_PENDING: i32 = FLIP_MODEL | 7;
pub const ERR_DESCRIBE: i32 = FLIP_MODEL | 8;
pub const ERR_CREATE: i32 = FLIP_MODEL | 9;
pub const ERR_RECREATE: i32 = FLIP_MODEL | 10;

const BUFFER_COUNT: u32 = 2;
const flip_flags: u32 = @intCast(@intFromEnum(dxgi.DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH) | @intFromEnum(dxgi.DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING));
const blt_flags: u32 = @intCast(@intFromEnum(dxgi.DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH));

const Chain = dxgi.IDXGISwapChain;
const PresentFn = @FieldType(Chain.VTable, "Present");
const ResizeBuffersFn = @FieldType(Chain.VTable, "ResizeBuffers");

var started: std.atomic.Value(bool) = .init(false);
var result: std.atomic.Value(i32) = .init(ERR_PENDING);
var flipped: std.atomic.Value(bool) = .init(false);
var unity: usize = 0;
var original_present: ?PresentFn = null;
var original_resize: ?ResizeBuffersFn = null;

fn slot(offset: usize) *?*Chain {
    return @ptrFromInt(unity + offset);
}

fn exclusiveFullscreen() bool {
    return @as(*const u8, @ptrFromInt(unity + player.exclusive_fullscreen_offset)).* != 0;
}

fn patch(chain: *Chain) bool {
    const table: *Chain.VTable = @constCast(chain.vtable);
    if (table.Present == &hookedPresent) return true;
    const first: [*]u8 = @ptrCast(&table.Present);
    const last: [*]u8 = @ptrCast(&table.ResizeBuffers);
    const size = @intFromPtr(last) + @sizeOf(ResizeBuffersFn) - @intFromPtr(first);
    var old: memory.PAGE_PROTECTION_FLAGS = undefined;
    if (kernel32.VirtualProtect(first, size, memory.PAGE_READWRITE, &old) == 0) return false;
    if (original_present == null) original_present = table.Present;
    if (original_resize == null) original_resize = table.ResizeBuffers;
    table.Present = &hookedPresent;
    table.ResizeBuffers = &hookedResizeBuffers;
    _ = kernel32.VirtualProtect(first, size, old, &old);
    return true;
}

fn hookedPresent(chain: *const Chain, sync_interval: u32, flags: u32) callconv(.winapi) w32.zig.HRESULT {
    const tear = sync_interval == 0 and flipped.load(.acquire) and !exclusiveFullscreen();
    return original_present.?(chain, sync_interval, if (tear) flags | dxgi.DXGI_PRESENT_ALLOW_TEARING else flags);
}

fn hookedResizeBuffers(chain: *const Chain, count: u32, width: u32, height: u32, format: dxgi.common.DXGI_FORMAT, flags: u32) callconv(.winapi) w32.zig.HRESULT {
    if (slot(player.swap_chain_offset).* == chain) {
        if (flipped.load(.acquire)) return original_resize.?(chain, 0, width, height, format, flip_flags);
        if (result.load(.acquire) == ERR_PENDING) {
            result.store(swap(@constCast(chain), width, height, format), .release);
            return w32.foundation.S_OK;
        }
    }
    return original_resize.?(chain, count, width, height, format, flags);
}

fn create(factory: *dxgi.IDXGIFactory2, device: *w32.system.com.IUnknown, window: ?w32.foundation.HWND, width: u32, height: u32, format: dxgi.common.DXGI_FORMAT, flip: bool) ?*dxgi.IDXGISwapChain1 {
    const desc = dxgi.DXGI_SWAP_CHAIN_DESC1{
        .Width = width,
        .Height = height,
        .Format = format,
        .Stereo = 0,
        .SampleDesc = .{ .Count = 1, .Quality = 0 },
        .BufferUsage = .{ .SHADER_INPUT = 1, .RENDER_TARGET_OUTPUT = 1 },
        .BufferCount = if (flip) BUFFER_COUNT else 1,
        .Scaling = .STRETCH,
        .SwapEffect = if (flip) .FLIP_DISCARD else .DISCARD,
        .AlphaMode = .UNSPECIFIED,
        .Flags = if (flip) flip_flags else blt_flags,
    };
    const fullscreen = dxgi.DXGI_SWAP_CHAIN_FULLSCREEN_DESC{
        .RefreshRate = .{ .Numerator = 0, .Denominator = 1 },
        .ScanlineOrdering = .UNSPECIFIED,
        .Scaling = .UNSPECIFIED,
        .Windowed = 1,
    };
    var out: *dxgi.IDXGISwapChain1 = undefined;
    if (factory.CreateSwapChainForHwnd(device, window, &desc, &fullscreen, null, &out).failed) return null;
    return out;
}

fn swap(chain: *Chain, width: u32, height: u32, format: dxgi.common.DXGI_FORMAT) i32 {
    var desc: dxgi.DXGI_SWAP_CHAIN_DESC = undefined;
    if (chain.GetDesc(&desc).failed) return ERR_DESCRIBE;
    var device_raw: ?*anyopaque = null;
    if (chain.IDXGIDeviceSubObject.GetDevice(d3d11.IID_ID3D11Device, @ptrCast(&device_raw)).failed) return ERR_DESCRIBE;
    const device: *d3d11.ID3D11Device = @ptrCast(@alignCast(device_raw.?));
    defer _ = device.IUnknown.Release();
    var factory_raw: ?*anyopaque = null;
    if (chain.IDXGIDeviceSubObject.IDXGIObject.GetParent(dxgi.IID_IDXGIFactory2, @ptrCast(&factory_raw)).failed) return ERR_DESCRIBE;
    const factory: *dxgi.IDXGIFactory2 = @ptrCast(@alignCast(factory_raw.?));
    defer _ = factory.IUnknown.Release();
    var context_raw: ?*d3d11.ID3D11DeviceContext = null;
    device.GetImmediateContext(&context_raw);
    const context = context_raw.?;
    defer _ = context.IUnknown.Release();

    const new_width = if (width == 0) desc.BufferDesc.Width else width;
    const new_height = if (height == 0) desc.BufferDesc.Height else height;
    const new_format = if (format == .UNKNOWN) desc.BufferDesc.Format else format;

    context.OMSetRenderTargets(0, null, null);
    for ([_]usize{ player.swap_chain2_offset, player.swap_chain1_offset, player.swap_chain_offset }) |offset| {
        if (slot(offset).*) |held| _ = held.IUnknown.Release();
        slot(offset).* = null;
    }
    context.Flush();

    const unknown: *w32.system.com.IUnknown = &device.IUnknown;
    var outcome = ERR_OK;
    const replacement = create(factory, unknown, desc.OutputWindow, new_width, new_height, new_format, true) orelse blk: {
        outcome = ERR_CREATE;
        break :blk create(factory, unknown, desc.OutputWindow, new_width, new_height, new_format, false) orelse return ERR_RECREATE;
    };
    var base_raw: ?*anyopaque = null;
    _ = replacement.IUnknown.QueryInterface(dxgi.IID_IDXGISwapChain, @ptrCast(&base_raw));
    const base: *Chain = @ptrCast(@alignCast(base_raw.?));
    slot(player.swap_chain1_offset).* = @ptrCast(replacement);
    slot(player.swap_chain_offset).* = base;
    _ = factory.IDXGIFactory1.IDXGIFactory.MakeWindowAssociation(desc.OutputWindow, dxgi.DXGI_MWA_NO_WINDOW_CHANGES | dxgi.DXGI_MWA_NO_ALT_ENTER);
    if (!patch(base)) return ERR_PROTECT;
    flipped.store(outcome == ERR_OK, .release);
    return outcome;
}

pub fn init() i32 {
    if (builtin.os.tag != .windows) return ERR_UNSUPPORTED;
    if (started.swap(true, .acq_rel)) return ERR_ALREADY;
    const image = player.current() catch return ERR_PLAYER;
    const targets = image.resolveSwapChain() catch return ERR_PROLOGUE;
    unity = @as(*const usize, @ptrCast(@alignCast(image.address(targets.primary_slot)))).*;
    if (unity == 0) return ERR_NO_SWAP_CHAIN;
    const chain = slot(player.swap_chain_offset).* orelse return ERR_NO_SWAP_CHAIN;
    if (!patch(chain)) return ERR_PROTECT;
    return ERR_OK;
}

pub fn state() i32 {
    return result.load(.acquire);
}
