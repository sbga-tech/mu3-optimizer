//! Unity player push/pop/cleanup detours for GPU fence waits.
const fence = @import("fence.zig");
const state = @import("state.zig");
const w32 = @import("win32.zig");

pub fn hookedPush(this: ?*anyopaque) callconv(.c) void {
    const r = state.rt() orelse return;
    state.enter(r);
    defer state.leave(r);
    if (r.stopping.load(.acquire) or state.anyWaiting(state.currentScope(r))) {
        state.origPush(r, this);
        return;
    }
    fence.bindIfNeeded(r, this);
    if (r.render_thread.load(.acquire) != w32.GetCurrentThreadId()) {
        state.origPush(r, this);
        return;
    }
    if (r.get_context(0) != r.context.load(.acquire)) {
        state.latch(r);
        state.origPush(r, this);
        return;
    }
    const owner = r.owner.load(.acquire);
    if (owner != null and owner != this) {
        state.latch(r);
        state.origPush(r, this);
        return;
    }
    var scope = state.TlsScope{
        .prev = null,
        .kind = state.KIND_PUSH,
        .waiting = false,
        .treated = false,
        .owner = this,
        .query = null,
    };
    const prev = state.pushScope(r, &scope);
    defer _ = w32.TlsSetValue(r.tls_slot, prev);
    state.origPush(r, this);
}

pub fn hookedPop(this: ?*anyopaque) callconv(.c) void {
    const r = state.rt() orelse return;
    state.enter(r);
    defer state.leave(r);
    if (r.stopping.load(.acquire) or
        state.anyWaiting(state.currentScope(r)) or
        r.render_thread.load(.acquire) != w32.GetCurrentThreadId() or
        r.owner.load(.acquire) != this)
    {
        state.origPop(r, this);
        return;
    }
    var scope = state.TlsScope{
        .prev = null,
        .kind = state.KIND_POP,
        .waiting = false,
        .treated = false,
        .owner = this,
        .query = null,
    };
    {
        const prev = state.pushScope(r, &scope);
        defer _ = w32.TlsSetValue(r.tls_slot, prev);
        state.origPop(r, this);
    }
    if (scope.query != null) {
        _ = r.sidecar.removeQuery(state.ptrInt(scope.query));
    }
}

pub fn hookedCleanup(this: ?*anyopaque) callconv(.c) void {
    const r = state.rt() orelse return;
    state.enter(r);
    defer state.leave(r);
    if (r.stopping.load(.acquire) or state.anyWaiting(state.currentScope(r))) {
        state.origCleanup(r, this);
        return;
    }
    if (r.render_thread.load(.acquire) != w32.GetCurrentThreadId()) {
        if (r.owner.load(.acquire) != null) state.latch(r);
        state.origCleanup(r, this);
        return;
    }
    r.sidecar.clearOwner(state.ptrInt(this));
    state.origCleanup(r, this);
}
