const low_latency = @import("low_latency.zig");

comptime {
    if (@sizeOf(*anyopaque) != 8) @compileError("mu3latency requires 64-bit pointers");
}

export fn mu3_low_latency_init(texture: ?*anyopaque, backend: *u32) i32 {
    return low_latency.init(texture, backend);
}

export fn mu3_low_latency_frame() i32 {
    return low_latency.frame();
}

export fn mu3_low_latency_detail() i32 {
    return low_latency.detail();
}

export fn mu3_low_latency_verify(sleep_interval_us: *u32) i32 {
    return low_latency.verify(sleep_interval_us);
}
