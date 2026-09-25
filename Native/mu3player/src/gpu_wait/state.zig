//! Shared GPU fence-wait runtime record. One process-owned instance; never freed.
const std = @import("std");
const com = @import("com.zig");
const minhook = @import("../minhook.zig");
const sidecar_mod = @import("sidecar.zig");
const errors = @import("errors.zig");
const win32 = @import("win32.zig");

const AtomicBool = std.atomic.Value(bool);
const AtomicPtr = std.atomic.Value(?*anyopaque);
const AtomicU32 = std.atomic.Value(u32);
const AtomicU64 = std.atomic.Value(u64);

const Sidecar = sidecar_mod.Sidecar;
const ERR_HOOK = errors.ERR_HOOK;
const ERR_OK = errors.ERR_OK;
const TlsGetValue = win32.TlsGetValue;
const TlsSetValue = win32.TlsSetValue;

pub const KIND_PUSH: u8 = 1;
pub const KIND_POP: u8 = 2;
pub const BIND_NONE: u32 = 0;
pub const BIND_BUSY: u32 = 1;
pub const BIND_DONE: u32 = 2;

pub const PushFn = *const fn (?*anyopaque) callconv(.c) void;
pub const EndFn = com.EndFn;
pub const GetDataFn = com.GetDataFn;
pub const GetCtxFn = *const fn (i32) callconv(.c) ?*anyopaque;

pub const TlsScope = struct {
    prev: ?*TlsScope,
    kind: u8,
    waiting: bool,
    treated: bool,
    owner: ?*anyopaque,
    query: ?*anyopaque,
};

pub const Runtime = struct {
    stopped: AtomicBool,
    render_thread: AtomicU32,
    quarantined: AtomicBool,
    in_flight: AtomicU32,
    stopping: AtomicBool,
    fence_wait: AtomicBool,
    event_armed: AtomicBool,
    event_quarantined: AtomicBool,
    bind_state: AtomicU32,
    lifecycle_busy: AtomicBool,
    generation: AtomicU32,
    next_value: AtomicU64,
    last_completed: AtomicU64,
    tls_slot: u32,
    qpc_freq: u64,
    get_context: GetCtxFn,
    orig_push: ?PushFn,
    orig_pop: ?PushFn,
    orig_cleanup: ?PushFn,
    orig_end: ?EndFn,
    orig_get_data: ?GetDataFn,
    owner: AtomicPtr,
    context: AtomicPtr,
    device: AtomicPtr,
    device5: AtomicPtr,
    context4: AtomicPtr,
    fence: AtomicPtr,
    event: ?*anyopaque,
    sidecar: Sidecar,
    hooks: minhook.HookSet,
};

var runtime_storage: Runtime = undefined;
var runtime_published: std.atomic.Value(?*Runtime) = .init(null);

pub fn rt() ?*Runtime {
    return runtime_published.load(.acquire);
}

pub fn publish(r: *Runtime) void {
    runtime_published.store(r, .release);
}

pub fn ptrInt(p: ?*anyopaque) usize {
    return if (p) |q| @intFromPtr(q) else 0;
}

pub fn enter(r: *Runtime) void {
    _ = r.in_flight.fetchAdd(1, .acquire);
}

pub fn leave(r: *Runtime) void {
    _ = r.in_flight.fetchSub(1, .release);
}

pub fn currentScope(r: *Runtime) ?*TlsScope {
    return @ptrCast(@alignCast(TlsGetValue(r.tls_slot)));
}

pub fn anyWaiting(start: ?*TlsScope) bool {
    var p = start;
    while (p) |scope| {
        if (scope.waiting) return true;
        p = scope.prev;
    }
    return false;
}

pub fn findKind(start: ?*TlsScope, kind: u8) ?*TlsScope {
    var p = start;
    while (p) |scope| {
        if (scope.kind == kind) return scope;
        p = scope.prev;
    }
    return null;
}

pub fn pushScope(r: *Runtime, scope: *TlsScope) ?*anyopaque {
    const prev = TlsGetValue(r.tls_slot);
    scope.prev = @ptrCast(@alignCast(prev));
    _ = TlsSetValue(r.tls_slot, scope);
    return prev;
}

pub fn latch(r: *Runtime) void {
    r.fence_wait.store(false, .release);
}

pub fn suspiciousCompleted(r: *Runtime, completed: u64) bool {
    if (completed == std.math.maxInt(u64)) return true;
    return completed < r.last_completed.load(.monotonic);
}

pub fn origPush(r: *Runtime, this: ?*anyopaque) void {
    if (r.orig_push) |f| f(this);
}

pub fn origPop(r: *Runtime, this: ?*anyopaque) void {
    if (r.orig_pop) |f| f(this);
}

pub fn origCleanup(r: *Runtime, this: ?*anyopaque) void {
    if (r.orig_cleanup) |f| f(this);
}

pub fn origEnd(
    r: *Runtime,
    ctx: *const com.ID3D11DeviceContext,
    query: ?*com.ID3D11Asynchronous,
) void {
    if (r.orig_end) |f| f(ctx, query);
}

pub fn origGetData(
    r: *Runtime,
    ctx: *const com.ID3D11DeviceContext,
    query: ?*com.ID3D11Asynchronous,
    data: ?*anyopaque,
    size: u32,
    flags: u32,
) com.HRESULT {
    return if (r.orig_get_data) |f| f(ctx, query, data, size, flags) else .fromInt(0xFFFF_FFFF);
}

pub fn addHook(r: *Runtime, target: ?*anyopaque, hook: anytype, orig: *?@TypeOf(hook)) i32 {
    const t = target orelse return ERR_HOOK;
    r.hooks.add(t, hook, orig) catch return ERR_HOOK;
    return ERR_OK;
}

pub fn enableFrom(r: *Runtime, from: usize) i32 {
    r.hooks.enableFrom(from) catch return ERR_HOOK;
    return ERR_OK;
}

pub fn rollbackFrom(r: *Runtime, from: usize) void {
    r.hooks.rollbackFrom(from);
}

pub fn disableOurHooks(r: *Runtime) bool {
    return r.hooks.disableAll();
}

pub fn initRuntime(slot: u32, get_context: GetCtxFn) *Runtime {
    runtime_storage = .{
        .stopped = .init(false),
        .render_thread = .init(0),
        .quarantined = .init(false),
        .in_flight = .init(0),
        .stopping = .init(false),
        .fence_wait = .init(false),
        .event_armed = .init(false),
        .event_quarantined = .init(false),
        .bind_state = .init(BIND_NONE),
        .lifecycle_busy = .init(false),
        .generation = .init(0),
        .next_value = .init(1),
        .last_completed = .init(0),
        .tls_slot = slot,
        .qpc_freq = win32.qpf(),
        .get_context = get_context,
        .orig_push = null,
        .orig_pop = null,
        .orig_cleanup = null,
        .orig_end = null,
        .orig_get_data = null,
        .owner = .init(null),
        .context = .init(null),
        .device = .init(null),
        .device5 = .init(null),
        .context4 = .init(null),
        .fence = .init(null),
        .event = null,
        .sidecar = .{},
        .hooks = .{},
    };
    return &runtime_storage;
}
