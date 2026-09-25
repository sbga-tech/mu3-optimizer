//! Kernel32 APIs and QPC deadline math for GPU fence waits.
const std = @import("std");
const w32 = @import("win32");

const foundation = w32.foundation;
const k32 = w32.kernel32;
const threading = w32.system.threading;

pub const HANDLE = foundation.HANDLE;
pub const TRUE = foundation.TRUE;
pub const FALSE = foundation.FALSE;
pub const WAIT_OBJECT_0 = foundation.WAIT_OBJECT_0;
pub const WAIT_TIMEOUT = foundation.WAIT_TIMEOUT;
pub const WAIT_IO_COMPLETION = foundation.WAIT_IO_COMPLETION;
pub const TLS_OUT_OF_INDEXES = threading.TLS_OUT_OF_INDEXES;
pub const INVALID_HANDLE_VALUE = foundation.INVALID_HANDLE_VALUE;

pub const GetCurrentThreadId = k32.GetCurrentThreadId;
pub const TlsAlloc = k32.TlsAlloc;
pub const TlsGetValue = k32.TlsGetValue;
pub const TlsSetValue = k32.TlsSetValue;
pub const TlsFree = k32.TlsFree;
pub const CreateEventW = k32.CreateEventW;
pub const ResetEvent = k32.ResetEvent;
pub const CloseHandle = k32.CloseHandle;
pub const WaitForSingleObjectEx = k32.WaitForSingleObjectEx;
pub const QueryPerformanceCounter = k32.QueryPerformanceCounter;
pub const QueryPerformanceFrequency = k32.QueryPerformanceFrequency;
pub const SleepEx = k32.SleepEx;

pub fn qpc() u64 {
    var v: foundation.LARGE_INTEGER = .{ .QuadPart = 0 };
    _ = QueryPerformanceCounter(&v);
    return @bitCast(v.QuadPart);
}

pub fn qpf() u64 {
    var v: foundation.LARGE_INTEGER = .{ .QuadPart = 0 };
    _ = QueryPerformanceFrequency(&v);
    return @bitCast(v.QuadPart);
}

pub fn remainingMillis(now_qpc: u64, deadline_qpc: u64, freq: u64) u32 {
    if (freq == 0 or now_qpc >= deadline_qpc) return 0;
    const ticks = deadline_qpc - now_qpc;
    const ms = (ticks *| 1000) / freq;
    return @intCast(@min(ms, std.math.maxInt(u32)));
}

pub fn deadlineFrom(start_qpc: u64, freq: u64, budget_ms: u64) u64 {
    return start_qpc +| ((freq *| budget_ms) / 1000);
}
