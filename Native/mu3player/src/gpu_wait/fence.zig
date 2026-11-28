//! Device bind, COM End/GetData detours, and fence wait.
const std = @import("std");
const com = @import("com.zig");
const sidecar_mod = @import("sidecar.zig");
const state = @import("state.zig");
const errors = @import("errors.zig");
const w32 = @import("win32.zig");

const Runtime = state.Runtime;
const TlsScope = state.TlsScope;
const Marker = sidecar_mod.Marker;
const HRESULT = com.HRESULT;

const FENCE_WAIT_MS: u64 = 1000;
const WaitOut = enum { complete, skip, fail };
const WaitObject = enum { signaled, timeout, failed };

fn hookedEnd(ctx: *const com.ID3D11DeviceContext, query: ?*com.ID3D11Asynchronous) callconv(.winapi) void {
    const r = state.rt() orelse return;
    state.enter(r);
    defer state.leave(r);
    const ctx_p: ?*anyopaque = @ptrCast(@constCast(ctx));
    const query_p: ?*anyopaque = @ptrCast(query);
    const scope = state.findKind(state.currentScope(r), state.KIND_PUSH);
    const ours = scope != null and ctx_p == r.context.load(.acquire);
    state.origEnd(r, ctx, query);
    if (!ours or query_p == null or r.stopping.load(.acquire)) return;
    if (!r.fence_wait.load(.acquire)) return;
    if (state.anyWaiting(state.currentScope(r))) return;
    const owner = scope.?.owner;
    const captured = r.owner.load(.acquire);
    if (captured != null and captured != owner) {
        state.latch(r);
        return;
    }
    const context4 = r.context4.load(.acquire);
    const fence_obj = r.fence.load(.acquire);
    const next = r.next_value.load(.monotonic);
    if (next == 0 or next == std.math.maxInt(u64)) {
        state.latch(r);
        return;
    }
    const hr = com.signal(context4, fence_obj, next);
    if (com.failed(hr)) {
        state.latch(r);
        return;
    }
    r.next_value.store(next + 1, .monotonic);
    const marker = Marker{
        .owner = state.ptrInt(owner),
        .generation = r.generation.load(.monotonic),
        .query = state.ptrInt(query_p),
        .value = next,
    };
    r.sidecar.insert(marker) catch state.latch(r);
}

fn hookedGetData(
    ctx: *const com.ID3D11DeviceContext,
    query: ?*com.ID3D11Asynchronous,
    data: ?*anyopaque,
    size: u32,
    flags: u32,
) callconv(.winapi) HRESULT {
    const r = state.rt() orelse return .fromInt(0xFFFF_FFFF);
    state.enter(r);
    defer state.leave(r);
    const ctx_p: ?*anyopaque = @ptrCast(@constCast(ctx));
    const query_p: ?*anyopaque = @ptrCast(query);
    const scope_opt = state.findKind(state.currentScope(r), state.KIND_POP);
    const ours = scope_opt != null and
        !state.anyWaiting(state.currentScope(r)) and
        !r.stopping.load(.acquire) and
        ctx_p == r.context.load(.acquire);
    const hr = state.origGetData(r, ctx, query, data, size, flags);
    if (!ours) return hr;
    const scope = scope_opt.?;
    if (scope.query == null) {
        scope.query = query_p;
    } else if (scope.query != query_p) {
        state.latch(r);
        return hr;
    }
    if (std.meta.eql(hr, com.S_OK)) return hr;
    if (!std.meta.eql(hr, com.S_FALSE)) return hr;
    if (r.stopping.load(.acquire)) return hr;
    return treatPending(r, scope, ctx, query, data, size, flags);
}

fn treatPending(
    r: *Runtime,
    scope: *TlsScope,
    ctx: *const com.ID3D11DeviceContext,
    query: ?*com.ID3D11Asynchronous,
    data: ?*anyopaque,
    size: u32,
    flags: u32,
) HRESULT {
    if (!r.fence_wait.load(.acquire)) return com.S_FALSE;
    const query_p: ?*anyopaque = @ptrCast(query);
    const marker = r.sidecar.find(state.ptrInt(query_p)) orelse return com.S_FALSE;
    if (marker.generation != r.generation.load(.monotonic)) {
        state.latch(r);
        return com.S_FALSE;
    }
    if (scope.treated) return com.S_FALSE;
    scope.treated = true;
    const fence_obj = r.fence.load(.acquire);
    const completed = com.getCompletedValue(fence_obj);
    if (state.suspiciousCompleted(r, completed)) {
        state.latch(r);
        return com.S_FALSE;
    }
    r.last_completed.store(completed, .monotonic);
    if (completed >= marker.value) {
        return finishQuery(r, state.origGetData(r, ctx, query, data, size, flags));
    }
    return switch (waitFence(r, scope, marker.value)) {
        .complete => finishQuery(r, state.origGetData(r, ctx, query, data, size, flags)),
        .skip, .fail => com.S_FALSE,
    };
}

fn finishQuery(r: *Runtime, hr: HRESULT) HRESULT {
    if (std.meta.eql(hr, com.S_FALSE)) {
        state.latch(r);
    }
    return hr;
}

fn waitFence(r: *Runtime, scope: *TlsScope, value: u64) WaitOut {
    if (state.anyWaiting(scope)) return .skip;
    if (r.event_quarantined.load(.acquire)) {
        state.latch(r);
        return .fail;
    }
    if (r.event_armed.load(.acquire)) {
        state.latch(r);
        return .fail;
    }
    const event = r.event;
    if (event == null) {
        state.latch(r);
        return .fail;
    }
    if (w32.ResetEvent(event) == w32.FALSE) {
        state.latch(r);
        return .fail;
    }
    const fence_obj = r.fence.load(.acquire);
    const hr = com.setEventOnCompletion(fence_obj, value, event);
    if (com.failed(hr)) {
        quarantine(r);
        state.latch(r);
        return .fail;
    }
    r.event_armed.store(true, .release);
    com.flush(r.context.load(.acquire));
    scope.waiting = true;
    const outcome = alertableDeadline(r.qpc_freq, event, FENCE_WAIT_MS);
    scope.waiting = false;
    switch (outcome) {
        .signaled => {
            r.event_armed.store(false, .release);
            const completed = com.getCompletedValue(fence_obj);
            if (state.suspiciousCompleted(r, completed) or completed < value) {
                state.latch(r);
                return .fail;
            }
            r.last_completed.store(completed, .monotonic);
            return .complete;
        },
        .timeout => {
            quarantine(r);
            state.latch(r);
            return .fail;
        },
        .failed => {
            quarantine(r);
            state.latch(r);
            return .fail;
        },
    }
}

fn alertableDeadline(freq: u64, handle: ?w32.HANDLE, budget_ms: u64) WaitObject {
    const start = w32.qpc();
    const deadline = w32.deadlineFrom(start, freq, budget_ms);
    while (true) {
        const now = w32.qpc();
        const rem = w32.remainingMillis(now, deadline, freq);
        if (rem == 0) return .timeout;
        const st = w32.WaitForSingleObjectEx(handle, rem, w32.TRUE);
        if (st == w32.WAIT_OBJECT_0) return .signaled;
        if (st == w32.WAIT_IO_COMPLETION) continue;
        if (st == w32.WAIT_TIMEOUT) return .timeout;
        return .failed;
    }
}

fn quarantine(r: *Runtime) void {
    r.event_quarantined.store(true, .release);
    r.quarantined.store(true, .release);
}

pub fn bindIfNeeded(r: *Runtime, owner: ?*anyopaque) void {
    if (r.bind_state.load(.acquire) != state.BIND_NONE) return;
    if (r.lifecycle_busy.cmpxchgStrong(false, true, .acquire, .monotonic) != null) return;
    defer r.lifecycle_busy.store(false, .release);
    if (r.stopping.load(.acquire)) return;
    if (r.bind_state.cmpxchgStrong(state.BIND_NONE, state.BIND_BUSY, .acq_rel, .acquire) != null) return;
    bindDevice(r, owner);
    r.bind_state.store(state.BIND_DONE, .release);
}

fn bindDevice(r: *Runtime, owner: ?*anyopaque) void {
    const context = r.get_context(0);
    if (context == null) {
        state.latch(r);
        return;
    }
    com.addRef(context);
    const device = com.getDevice(context);
    if (device == null) {
        com.release(context);
        state.latch(r);
        return;
    }
    var device5: ?*anyopaque = null;
    var context4: ?*anyopaque = null;
    var fence_obj: ?*anyopaque = null;
    const hr5 = com.queryDevice5(device, &device5);
    const hr4 = com.queryContext4(context, &context4);
    var can_wait = true;
    if (com.failed(hr5) or device5 == null) {
        can_wait = false;
    } else if (com.failed(hr4) or context4 == null) {
        can_wait = false;
    } else {
        const hr_f = com.createFence(device5, &fence_obj);
        if (com.failed(hr_f) or fence_obj == null) can_wait = false;
    }
    const end_fn = com.vtblEnd(context);
    const get_fn = com.vtblGetData(context);
    const phase = r.hooks.n;
    var orig_end: ?com.EndFn = null;
    var orig_get: ?com.GetDataFn = null;
    if (state.addHook(r, end_fn, &hookedEnd, &orig_end) != errors.ERR_OK or
        state.addHook(r, get_fn, &hookedGetData, &orig_get) != errors.ERR_OK)
    {
        state.rollbackFrom(r, phase);
        com.releaseCom(device5, context4, fence_obj, device, context);
        state.latch(r);
        return;
    }
    r.orig_end = orig_end;
    r.orig_get_data = orig_get;
    if (state.enableFrom(r, phase) != errors.ERR_OK) {
        state.rollbackFrom(r, phase);
        com.releaseCom(device5, context4, fence_obj, device, context);
        state.latch(r);
        return;
    }
    r.owner.store(owner, .release);
    r.context.store(context, .release);
    r.device.store(device, .release);
    r.device5.store(device5, .release);
    r.context4.store(context4, .release);
    r.fence.store(fence_obj, .release);
    r.generation.store(1, .release);
    r.next_value.store(1, .release);
    r.render_thread.store(w32.GetCurrentThreadId(), .release);
    if (!can_wait) {
        state.latch(r);
        return;
    }
    r.event = w32.CreateEventW(null, w32.TRUE, w32.FALSE, null);
    if (r.event == null) {
        state.latch(r);
        return;
    }
    r.fence_wait.store(true, .release);
}
