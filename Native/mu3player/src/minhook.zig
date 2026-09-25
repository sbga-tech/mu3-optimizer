//! Typed MinHook boundary and fixed-capacity, process-owned hook groups.
//! Disabling retains trampolines: a detour may still be returning through one.

const Status = enum(i32) {
    unknown = -1,
    ok = 0,
    already_initialized,
    not_initialized,
    already_created,
    not_created,
    enabled,
    disabled,
    not_executable,
    unsupported_function,
    memory_alloc,
    memory_protect,
    module_not_found,
    function_not_found,
    _,
};

const Error = error{
    Unknown,
    NotInitialized,
    AlreadyCreated,
    NotCreated,
    Enabled,
    Disabled,
    NotExecutable,
    UnsupportedFunction,
    MemoryAlloc,
    MemoryProtect,
    ModuleNotFound,
    FunctionNotFound,
    Capacity,
};

const Native = struct {
    extern fn MH_Initialize() callconv(.winapi) Status;
    extern fn MH_CreateHook(target: *anyopaque, hook: *anyopaque, original: *?*anyopaque) callconv(.winapi) Status;
    extern fn MH_EnableHook(target: *anyopaque) callconv(.winapi) Status;
    extern fn MH_DisableHook(target: *anyopaque) callconv(.winapi) Status;
    extern fn MH_RemoveHook(target: *anyopaque) callconv(.winapi) Status;
    extern fn MH_QueueEnableHook(target: *anyopaque) callconv(.winapi) Status;
    extern fn MH_QueueDisableHook(target: *anyopaque) callconv(.winapi) Status;
    extern fn MH_ApplyQueued() callconv(.winapi) Status;
};

pub fn initialize() Error!void {
    const status = Native.MH_Initialize();
    if (status != .already_initialized) try check(status);
}

pub fn create(target: *anyopaque, hook: anytype, original: *?@TypeOf(hook)) Error!void {
    comptime {
        const info = @typeInfo(@TypeOf(hook));
        if (info != .pointer or @typeInfo(info.pointer.child) != .@"fn")
            @compileError("detour must be a function pointer");
    }
    var trampoline: ?*anyopaque = null;
    try check(Native.MH_CreateHook(target, @ptrFromInt(@intFromPtr(hook)), &trampoline));
    original.* = @ptrCast(@alignCast(trampoline orelse return error.Unknown));
}

pub fn enable(target: *anyopaque) Error!void {
    try check(Native.MH_EnableHook(target));
}

/// Only for unpublished hooks whose activation failed.
pub fn remove(target: *anyopaque) Error!void {
    try check(Native.MH_RemoveHook(target));
}

pub const HookSet = struct {
    const capacity = 8;
    const Record = struct { target: *anyopaque, live: bool };
    records: [capacity]Record = undefined,
    n: usize = 0,

    pub fn add(self: *@This(), target: *anyopaque, hook: anytype, original: *?@TypeOf(hook)) Error!void {
        if (self.n == capacity) return error.Capacity;
        try create(target, hook, original);
        self.records[self.n] = .{ .target = target, .live = true };
        self.n += 1;
    }

    pub fn enableFrom(self: *const @This(), from: usize) Error!void {
        for (self.records[from..self.n]) |record|
            try check(Native.MH_QueueEnableHook(record.target));
        try check(Native.MH_ApplyQueued());
    }

    pub fn rollbackFrom(self: *@This(), from: usize) void {
        var i = self.n;
        while (i > from) {
            i -= 1;
            const record = &self.records[i];
            if (!record.live) continue;
            // Cancel any queued enable as well as disabling immediately.
            // Uncertain outcomes keep the record live for quarantine/retry.
            const queued = Native.MH_QueueDisableHook(record.target);
            const disabled = Native.MH_DisableHook(record.target);
            record.live = queued != .ok or (disabled != .ok and disabled != .disabled);
        }
        _ = Native.MH_ApplyQueued();
    }

    pub fn disableAll(self: *@This()) bool {
        self.rollbackFrom(0);
        for (self.records[0..self.n]) |record|
            if (record.live) return false;
        return true;
    }
};

fn check(status: Status) Error!void {
    return switch (status) {
        .ok => {},
        .not_initialized => error.NotInitialized,
        .already_created => error.AlreadyCreated,
        .not_created => error.NotCreated,
        .enabled => error.Enabled,
        .disabled => error.Disabled,
        .not_executable => error.NotExecutable,
        .unsupported_function => error.UnsupportedFunction,
        .memory_alloc => error.MemoryAlloc,
        .memory_protect => error.MemoryProtect,
        .module_not_found => error.ModuleNotFound,
        .function_not_found => error.FunctionNotFound,
        else => error.Unknown,
    };
}
