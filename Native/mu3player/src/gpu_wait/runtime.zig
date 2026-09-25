//! GPU fence-wait init and shutdown.
//!
//! - `init` verifies the mapped player, allocates one process TLS slot, and
//!   initializes MinHook. MinHook is process-owned: already-initialized is OK,
//!   and this module never uninitializes or removes hooks.
//! - Player push/pop/cleanup hooks are created before the runtime is published,
//!   then enabled. Create failure rolls back, frees TLS, and does not publish
//!   (`ERR_HOOK`). Enable failure publishes, disables, latches fallback, and
//!   keeps TLS/trampolines (`ERR_HOOK`, later `init` is `ERR_ALREADY`).
//! - Device bind happens once on the first eligible push: COM vtable End/GetData
//!   hooks are installed even when ID3D11Device5/Context4/fence QI fails; fence
//!   waits arm only after a manual-reset event is created.
//! - Win32 TLS holds a stack-linked `TlsScope` chain (no thread-local objects).
//!   Scope records are not heap-allocated. Sidecar is 16 fixed slots. Hook
//!   records are 8 fixed slots. Runtime is a single static, never freed.
//! - `shutdown` disables our hooks, waits for in-flight detours, and releases
//!   COM/event handles only when nothing is armed or incomplete.
//!   Trampolines, TLS, and MinHook stay mapped. The published runtime stays.
const builtin = @import("builtin");
const com = @import("com.zig");
const hooks = @import("hooks.zig");
const player = @import("../player.zig");
const minhook = @import("../minhook.zig");
const state = @import("state.zig");
const errors = @import("errors.zig");
const win32 = @import("win32.zig");

const PushFn = state.PushFn;
const ERR_OK = errors.ERR_OK;
const ERR_UNSUPPORTED = errors.ERR_UNSUPPORTED;
const ERR_ALREADY = errors.ERR_ALREADY;
const ERR_PE_IDENTITY = errors.ERR_PE_IDENTITY;
const ERR_PROLOGUE = errors.ERR_PROLOGUE;
const ERR_MINHOOK = errors.ERR_MINHOOK;
const ERR_HOOK = errors.ERR_HOOK;
const ERR_TLS = errors.ERR_TLS;
const ERR_SHUTDOWN = errors.ERR_SHUTDOWN;
const FALSE = win32.FALSE;
const TLS_OUT_OF_INDEXES = win32.TLS_OUT_OF_INDEXES;
const INVALID_HANDLE_VALUE = win32.INVALID_HANDLE_VALUE;
const TlsAlloc = win32.TlsAlloc;
const TlsFree = win32.TlsFree;
const CloseHandle = win32.CloseHandle;
const SleepEx = win32.SleepEx;
const releaseCom = com.releaseCom;
const rt = state.rt;
const latch = state.latch;
const addHook = state.addHook;
const enableFrom = state.enableFrom;
const rollbackFrom = state.rollbackFrom;
const disableOurHooks = state.disableOurHooks;
const initRuntime = state.initRuntime;
const publish = state.publish;

const SHUTDOWN_WAIT_MS: u32 = 2000;

fn isInvalidHandle(h: ?*anyopaque) bool {
    return h == null or h == INVALID_HANDLE_VALUE;
}

fn closeHandle(h: *?*anyopaque) void {
    if (isInvalidHandle(h.*)) return;
    _ = CloseHandle(h.*);
    h.* = null;
}

pub fn init() i32 {
    if (builtin.os.tag != .windows) return ERR_UNSUPPORTED;
    if (rt() != null) return ERR_ALREADY;
    const image = player.current() catch return ERR_PE_IDENTITY;
    const targets = image.resolveGpu() catch return ERR_PROLOGUE;
    const slot = TlsAlloc();
    if (slot == TLS_OUT_OF_INDEXES) return ERR_TLS;
    minhook.initialize() catch {
        _ = TlsFree(slot);
        return ERR_MINHOOK;
    };
    const raw = initRuntime(slot, @ptrCast(image.address(targets.context)));
    var orig_push: ?PushFn = null;
    var orig_pop: ?PushFn = null;
    var orig_cleanup: ?PushFn = null;
    const e1 = addHook(raw, image.address(targets.push), &hooks.hookedPush, &orig_push);
    const e2 = addHook(raw, image.address(targets.pop), &hooks.hookedPop, &orig_pop);
    const e3 = addHook(raw, image.address(targets.cleanup), &hooks.hookedCleanup, &orig_cleanup);
    if (e1 != ERR_OK or e2 != ERR_OK or e3 != ERR_OK) {
        rollbackFrom(raw, 0);
        _ = TlsFree(slot);
        return ERR_HOOK;
    }
    raw.orig_push = orig_push;
    raw.orig_pop = orig_pop;
    raw.orig_cleanup = orig_cleanup;
    raw.lifecycle_busy.store(true, .release);
    publish(raw);
    if (enableFrom(raw, 0) != ERR_OK) {
        raw.stopping.store(true, .release);
        _ = disableOurHooks(raw);
        latch(raw);
        raw.lifecycle_busy.store(false, .release);
        return ERR_HOOK;
    }
    raw.lifecycle_busy.store(false, .release);
    return ERR_OK;
}

pub fn shutdown() i32 {
    if (builtin.os.tag != .windows) return ERR_OK;
    const r = rt() orelse return ERR_OK;
    if (r.stopped.load(.acquire)) {
        return if (r.quarantined.load(.acquire)) ERR_SHUTDOWN else ERR_OK;
    }
    r.stopping.store(true, .release);
    var locked = false;
    var spin: u32 = 0;
    while (spin < SHUTDOWN_WAIT_MS) : (spin += 1) {
        if (r.lifecycle_busy.cmpxchgStrong(false, true, .acquire, .monotonic) == null) {
            locked = true;
            break;
        }
        _ = SleepEx(1, FALSE);
    }
    if (!locked) {
        r.quarantined.store(true, .release);
        return ERR_SHUTDOWN;
    }
    defer r.lifecycle_busy.store(false, .release);
    r.fence_wait.store(false, .release);
    const hooks_disabled = disableOurHooks(r);
    spin = 0;
    while (spin < SHUTDOWN_WAIT_MS) : (spin += 1) {
        if (r.in_flight.load(.acquire) == 0) break;
        _ = SleepEx(1, FALSE);
    }
    const incomplete = !hooks_disabled or r.in_flight.load(.acquire) != 0;
    const event_held = r.event_quarantined.load(.acquire) or r.event_armed.load(.acquire);
    if (!event_held and !incomplete) {
        const fence = r.fence.swap(null, .acq_rel);
        const context4 = r.context4.swap(null, .acq_rel);
        const device5 = r.device5.swap(null, .acq_rel);
        const device = r.device.swap(null, .acq_rel);
        const context = r.context.swap(null, .acq_rel);
        releaseCom(device5, context4, fence, device, context);
        closeHandle(&r.event);
        r.sidecar.clearAll();
    } else {
        r.quarantined.store(true, .release);
    }
    r.stopped.store(true, .release);
    return if (event_held or incomplete) ERR_SHUTDOWN else ERR_OK;
}
