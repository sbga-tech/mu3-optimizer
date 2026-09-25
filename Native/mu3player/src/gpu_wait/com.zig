//! ID3D11 COM calls for GPU fence waits, via generated zigwin32 interfaces.
const std = @import("std");
const w32 = @import("win32");

const d3d11 = w32.graphics.direct3d11;
const IUnknown = w32.system.com.IUnknown;

pub const HRESULT = w32.zig.HRESULT;
pub const S_OK = w32.foundation.S_OK;
pub const S_FALSE = w32.foundation.S_FALSE;
pub const HANDLE = w32.foundation.HANDLE;

pub const ID3D11Device = d3d11.ID3D11Device;
pub const ID3D11Device5 = d3d11.ID3D11Device5;
pub const ID3D11DeviceChild = d3d11.ID3D11DeviceChild;
pub const ID3D11DeviceContext = d3d11.ID3D11DeviceContext;
pub const ID3D11DeviceContext4 = d3d11.ID3D11DeviceContext4;
pub const ID3D11Fence = d3d11.ID3D11Fence;
pub const ID3D11Asynchronous = d3d11.ID3D11Asynchronous;

pub const EndFn = @FieldType(ID3D11DeviceContext.VTable, "End");
pub const GetDataFn = @FieldType(ID3D11DeviceContext.VTable, "GetData");

pub fn failed(hr: HRESULT) bool {
    return hr.failed;
}

fn asUnknown(this: *anyopaque) *const IUnknown {
    return @ptrCast(@alignCast(this));
}

pub fn addRef(this: ?*anyopaque) void {
    const obj = this orelse return;
    _ = asUnknown(obj).AddRef();
}

pub fn release(this: ?*anyopaque) void {
    const obj = this orelse return;
    _ = asUnknown(obj).Release();
}

pub fn getDevice(context: ?*anyopaque) ?*anyopaque {
    const obj: *const ID3D11DeviceChild = @ptrCast(@alignCast(context orelse return null));
    var device: ?*ID3D11Device = null;
    obj.GetDevice(&device);
    return @ptrCast(device);
}

pub fn flush(context: ?*anyopaque) void {
    const obj: *const ID3D11DeviceContext = @ptrCast(@alignCast(context orelse return));
    obj.Flush();
}

pub fn getCompletedValue(fence: ?*anyopaque) u64 {
    const obj: *const ID3D11Fence = @ptrCast(@alignCast(fence orelse return std.math.maxInt(u64)));
    return obj.GetCompletedValue();
}

pub fn setEventOnCompletion(fence: ?*anyopaque, value: u64, event: ?HANDLE) HRESULT {
    const obj: *const ID3D11Fence = @ptrCast(@alignCast(fence orelse return .fromInt(0xFFFF_FFFF)));
    return obj.SetEventOnCompletion(value, event);
}

pub fn signal(context4: ?*anyopaque, fence: ?*anyopaque, value: u64) HRESULT {
    const ctx: *const ID3D11DeviceContext4 = @ptrCast(@alignCast(context4 orelse return .fromInt(0xFFFF_FFFF)));
    const fen: *ID3D11Fence = @ptrCast(@alignCast(fence orelse return .fromInt(0xFFFF_FFFF)));
    return ctx.Signal(fen, value);
}

pub fn createFence(device5: ?*anyopaque, out: *?*anyopaque) HRESULT {
    out.* = null;
    const obj: *const ID3D11Device5 = @ptrCast(@alignCast(device5 orelse return .fromInt(0xFFFF_FFFF)));
    var raw: ?*anyopaque = null;
    const hr = obj.CreateFence(0, d3d11.D3D11_FENCE_FLAG_NONE, d3d11.IID_ID3D11Fence, @ptrCast(&raw));
    out.* = raw;
    return hr;
}

fn queryInterface(this: ?*anyopaque, iid: *const w32.zig.Guid, out: *?*anyopaque) HRESULT {
    out.* = null;
    const unk = asUnknown(this orelse return .fromInt(0xFFFF_FFFF));
    var raw: ?*anyopaque = null;
    const hr = unk.QueryInterface(iid, @ptrCast(&raw));
    out.* = raw;
    return hr;
}

pub fn queryDevice5(device: ?*anyopaque, out: *?*anyopaque) HRESULT {
    return queryInterface(device, d3d11.IID_ID3D11Device5, out);
}

pub fn queryContext4(context: ?*anyopaque, out: *?*anyopaque) HRESULT {
    return queryInterface(context, d3d11.IID_ID3D11DeviceContext4, out);
}

pub fn vtblEnd(this: ?*anyopaque) ?*anyopaque {
    const obj: *const ID3D11DeviceContext = @ptrCast(@alignCast(this orelse return null));
    return @ptrFromInt(@intFromPtr(obj.vtable.End));
}

pub fn vtblGetData(this: ?*anyopaque) ?*anyopaque {
    const obj: *const ID3D11DeviceContext = @ptrCast(@alignCast(this orelse return null));
    return @ptrFromInt(@intFromPtr(obj.vtable.GetData));
}

pub fn releaseCom(
    device5: ?*anyopaque,
    context4: ?*anyopaque,
    fence: ?*anyopaque,
    device: ?*anyopaque,
    context: ?*anyopaque,
) void {
    release(fence);
    release(context4);
    release(device5);
    release(device);
    release(context);
}
