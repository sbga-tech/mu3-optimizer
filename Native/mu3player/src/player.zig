//! Unity 5.6.4f1 x64 player targets, resolved from executable-section patterns.
//! File inspection and live resolution use the same parser and matching rules.
const std = @import("std");
const builtin = @import("builtin");
const win32 = @import("win32");
const coff = std.coff;
const services = win32.system.system_services;
const memory = win32.system.memory;
const kernel32 = win32.kernel32;

pub const Error = error{
    UnsupportedPlatform,
    InvalidImage,
    UnreadableMemory,
    MissingPattern,
    AmbiguousPattern,
};

pub const Build = enum { nondevelopment, development };
pub const Layout = enum { file, mapped };

pub const GpuTargets = struct {
    build: Build,
    push: u32,
    pop: u32,
    cleanup: u32,
    context: u32,
};

pub const SignalTargets = struct {
    signal: u32,
};

/// A wildcard covers a documented relative-address operand, never an opcode.
const Pattern = struct {
    bytes: []const u8,
    significant: []const bool,
    anchor_offset: usize,
    anchor_len: usize,

    fn matches(self: Pattern, bytes: []const u8) bool {
        if (bytes.len < self.bytes.len) return false;
        for (self.bytes, self.significant, bytes[0..self.bytes.len]) |expected, significant, actual| {
            if (significant and expected != actual) return false;
        }
        return true;
    }
};

fn pattern(comptime text: []const u8) Pattern {
    return comptime blk: {
        @setEvalBranchQuota(20_000);
        var tokens = std.mem.tokenizeAny(u8, text, " \t\r\n");
        var count: usize = 0;
        while (tokens.next() != null) count += 1;
        var bytes: [count]u8 = undefined;
        var significant: [count]bool = undefined;
        tokens.reset();
        var i: usize = 0;
        var run_start: usize = 0;
        var run_len: usize = 0;
        var anchor_offset: usize = 0;
        var anchor_len: usize = 0;
        while (tokens.next()) |token| : (i += 1) {
            if (std.mem.eql(u8, token, "??")) {
                bytes[i] = 0;
                significant[i] = false;
                run_len = 0;
            } else {
                if (token.len != 2) @compileError("pattern tokens must be hex bytes or ??");
                bytes[i] = std.fmt.parseInt(u8, token, 16) catch @compileError("invalid pattern byte");
                significant[i] = true;
                if (run_len == 0) run_start = i;
                run_len += 1;
                if (run_len > anchor_len) {
                    anchor_offset = run_start;
                    anchor_len = run_len;
                }
            }
        }
        if (anchor_len == 0) @compileError("pattern must contain literal bytes");
        const result_bytes = bytes;
        const result_significant = significant;
        break :blk .{
            .bytes = &result_bytes,
            .significant = &result_significant,
            .anchor_offset = anchor_offset,
            .anchor_len = anchor_len,
        };
    };
}

const ObjectLayout = struct {
    max_queued_frames: u32,
    queue_head: u32,
    queue_size: u32,
};

const Profile = struct {
    build: Build,
    layout: ObjectLayout,
};

// Unity 5.6.4f1 ac7086b8d112, Windows x64 Mono player variations.
const profiles = [_]Profile{
    .{
        .build = .nondevelopment,
        .layout = .{ .max_queued_frames = 0x110c, .queue_head = 0x3a08, .queue_size = 0x3a10 },
    },
    .{
        .build = .development,
        .layout = .{ .max_queued_frames = 0x1164, .queue_head = 0x3aa8, .queue_size = 0x3ab0 },
    },
};

fn hex32(comptime n: u32) []const u8 {
    return std.fmt.comptimePrint("{x:0>2} {x:0>2} {x:0>2} {x:0>2} ", .{
        @as(u8, @truncate(n)),       @as(u8, @truncate(n >> 8)),
        @as(u8, @truncate(n >> 16)), @as(u8, @truncate(n >> 24)),
    });
}

const GpuPatterns = struct { push: Pattern, pop: Pattern, cleanup: Pattern };

fn gpuPatterns(comptime layout: ObjectLayout) GpuPatterns {
    @setEvalBranchQuota(100_000);
    return .{
        .push = pattern(
            "40 53 " ++ // push rbx
                "48 83 EC 20 " ++ // sub rsp, 20h
                "83 B9 " ++ hex32(layout.max_queued_frames) ++ "00 " ++ // cmp [rcx+maxQueuedFrames], 0
                "48 8B D9 " ++ // mov rbx, rcx
                "0F 8C E4 00 00 00 " ++ // jl epilogue
                "48 8B 0D ?? ?? ?? ?? " ++ // mov rcx, [rip+device]
                "33 C0 " ++ // xor eax, eax
                "4C 8D 44 24 30 " ++ // lea r8, [rsp+30h]
                "48 89 44 24 30 " ++ // zero query descriptor/output
                "48 89 44 24 38 " ++
                "48 8B 01 " ++ // mov rax, [rcx]
                "48 8D 54 24 38 " ++ // lea rdx, [rsp+38h]
                "48 89 74 24 40 " ++ // save rsi/rdi
                "48 89 7C 24 48 " ++
                "FF 90 C0 00 00 00 " ++ // ID3D11Device::CreateQuery
                "48 8B 54 24 30 " ++ // mov rdx, [rsp+30h]
                "48 85 D2 " ++ // test query
                "74 6E " ++ // skip insertion if null
                "48 8B 0D ?? ?? ?? ?? " ++ // mov rcx, [rip+context]
                "48 8B 01 " ++ // mov rax, [rcx]
                "FF 90 E0 00 00 00 " ++ // ID3D11DeviceContext::End
                "48 8B B3 " ++ hex32(layout.queue_head), // mov rsi, [rbx+queueHead]
        ),
        .pop = pattern(
            "48 89 5C 24 08 " ++ // save rbx/rbp/rsi/rdi
                "48 89 6C 24 10 " ++
                "48 89 74 24 18 " ++
                "48 89 7C 24 20 " ++
                "41 54 " ++ // push r12
                "48 83 EC 40 " ++ // sub rsp, 40h
                "48 8B 81 " ++ hex32(layout.queue_head) ++ // mov rax, [rcx+queueHead]
                "48 8B 2D ?? ?? ?? ?? " ++ // mov rbp, [rip+context]
                "45 33 E4 " ++ // xor r12d, r12d
                "0F 29 74 24 30 " ++ // save xmm6
                "F2 0F 10 35 ?? ?? ?? ?? " ++ // movsd xmm6, [rip+sleepInterval]
                "48 8B 10 " ++ // mov rdx, [rax]
                "48 8B F1 " ++ // mov rsi, rcx
                "41 8B DC " ++ // mov ebx, r12d
                "48 8B 7A 10 " ++ // mov rdi, [rdx+10h] (query)
                "66 66 66 0F 1F 84 00 00 00 00 00 " ++ // alignment nop
                "48 8B 45 00 " ++ // mov rax, [rbp]
                "45 33 C9 " ++ // zero GetData output/size/flags
                "45 33 C0 " ++
                "48 8B D7 " ++ // mov rdx, rdi
                "48 8B CD " ++ // mov rcx, rbp
                "44 89 64 24 20 " ++
                "FF 90 E8 00 00 00", // ID3D11DeviceContext::GetData
        ),
        .cleanup = pattern(
            "48 89 5C 24 08 " ++ // save rbx/rsi
                "48 89 74 24 10 " ++
                "57 " ++ // push rdi
                "48 83 EC 20 " ++
                "48 8B B9 " ++ hex32(layout.queue_head) ++ // mov rdi, [rcx+queueHead]
                "48 8B F1 " ++ // mov rsi, rcx
                "48 8B 1F " ++ // mov rbx, [rdi]
                "48 3B DF " ++ // cmp rbx, rdi
                "74 17 " ++ // empty list
                "48 8B 4B 10 " ++ // mov rcx, [rbx+10h] (query)
                "48 85 C9 " ++
                "74 06 " ++
                "48 8B 01 " ++
                "FF 50 10 " ++ // IUnknown::Release
                "48 8B 1B " ++ // next list entry
                "48 3B DF " ++
                "75 E9 " ++
                "48 8B 86 " ++ hex32(layout.queue_head) ++
                "48 8B 08 " ++
                "48 89 00 " ++ // reset head links
                "48 8B 86 " ++ hex32(layout.queue_head) ++
                "48 89 40 08 " ++
                "48 C7 86 " ++ hex32(layout.queue_size) ++ "00 00 00 00", // zero queue count
        ),
    };
}

// A getter alone is ambiguous. Its RIP operand must reference the same context
// global as the matched Push/Pop functions (not an adjacent D3D device getter).
const context_getter = pattern("48 8B 05 ?? ?? ?? ?? C3");

// Both builds have the same 109-byte counter transition/release loop. Only the
// ReleaseSemaphore import-slot displacement differs; fields and branches stay exact.
const signal_body = pattern(
    "48 89 5C 24 08 " ++ // save rbx/rsi
        "48 89 74 24 10 " ++
        "57 " ++ // push rdi
        "48 83 EC 20 " ++
        "48 8B F1 " ++ // mov rsi, rcx
        "F0 83 0C 24 00 " ++ // lock or [rsp], 0 (full barrier)
        "66 0F 1F 84 00 00 00 00 00 " ++ // nop
        "8B 19 " ++ // mov ebx, [rcx] (current)
        "8D 04 13 " ++ // lea eax, [rbx+rdx] (wrapping add)
        "48 63 F8 " ++ // movsxd rdi, eax
        "48 8B 41 08 " ++ // mov rax, [rcx+8] (maximum)
        "48 3B F8 " ++
        "0F 47 F8 " ++ // unsigned cap
        "3B FB " ++
        "74 27 " ++ // unchanged counter
        "8B C3 " ++
        "F0 0F B1 39 " ++ // lock cmpxchg [rcx], edi
        "75 E2 " ++ // retry
        "3B DF " ++
        "7D 1B " ++
        "85 DB " ++
        "79 11 " ++ // only negative old counts release a waiter
        "48 8B 4E 10 " ++ // mov rcx, [rsi+10h] (semaphore handle)
        "45 33 C0 " ++
        "41 8D 50 01 " ++ // release one
        "FF 15 ?? ?? ?? ?? " ++ // ReleaseSemaphore via RIP-relative IAT slot
        "FF C3 " ++
        "3B DF " ++
        "7C E5 " ++ // repeat for the negative span
        "48 8B 5C 24 30 " ++ // restore nonvolatile registers
        "48 8B 74 24 38 " ++
        "48 83 C4 20 " ++
        "5F " ++
        "C3",
);

const RelativeOperand = struct { displacement: usize, instruction_end: usize };
const pop_context_operand: RelativeOperand = .{ .displacement = 0x24, .instruction_end = 0x28 };
const push_context_operand: RelativeOperand = .{ .displacement = 0x53, .instruction_end = 0x57 };
const getter_context_operand: RelativeOperand = .{ .displacement = 3, .instruction_end = 7 };
const signal_import_operand: RelativeOperand = .{ .displacement = 83, .instruction_end = 87 };

const Headers = struct {
    image_size: u32,
    sections: []align(1) const coff.SectionHeader,

    fn parse(bytes: []const u8) Error!Headers {
        const dos = try value(services.IMAGE_DOS_HEADER, bytes, 0);
        if (dos.e_magic != services.IMAGE_DOS_SIGNATURE or dos.e_lfanew < 0) return error.InvalidImage;
        const nt_offset: usize = @intCast(dos.e_lfanew);
        if (try value(u32, bytes, nt_offset) != services.IMAGE_NT_SIGNATURE) return error.InvalidImage;
        const file_offset = nt_offset + @sizeOf(u32);
        const file = try value(coff.Header, bytes, file_offset);
        if (file.machine != .AMD64 or file.number_of_sections == 0) return error.InvalidImage;
        const Optional = coff.OptionalHeader.@"PE32+";
        if (file.size_of_optional_header < @sizeOf(Optional)) return error.InvalidImage;
        const optional_offset = file_offset + @sizeOf(coff.Header);
        const optional = try value(Optional, bytes, optional_offset);
        if (optional.standard.magic != .@"PE32+" or optional.size_of_image == 0) return error.InvalidImage;
        const table_offset = optional_offset + file.size_of_optional_header;
        const table_size = @as(usize, file.number_of_sections) * @sizeOf(coff.SectionHeader);
        const table = try range(bytes, table_offset, table_size);
        if (table_offset + table_size > optional.size_of_headers or
            optional.size_of_headers > optional.size_of_image) return error.InvalidImage;
        return .{
            .image_size = optional.size_of_image,
            .sections = std.mem.bytesAsSlice(coff.SectionHeader, table),
        };
    }
};

/// Borrows a raw PE file or a mapped image; all results are RVAs, not file offsets.
pub const Image = struct {
    bytes: []const u8,
    layout: Layout,
    headers: Headers,
    live: bool = false,

    pub fn init(bytes: []const u8, layout: Layout) Error!Image {
        const headers = try Headers.parse(bytes);
        if (layout == .mapped and headers.image_size > bytes.len) return error.InvalidImage;
        const image: Image = .{ .bytes = bytes, .layout = layout, .headers = headers };
        for (headers.sections) |section| {
            const size = sectionSize(section);
            if (section.virtual_address > headers.image_size or
                size > headers.image_size - section.virtual_address) return error.InvalidImage;
            // Validate file/virtual extents before any scanner can read them.
            _ = try image.sectionBytes(section);
        }
        return image;
    }

    fn sectionBytes(self: Image, section: coff.SectionHeader) Error![]const u8 {
        const offset = if (self.layout == .mapped) section.virtual_address else section.pointer_to_raw_data;
        const size = if (self.layout == .mapped) sectionSize(section) else @min(sectionSize(section), section.size_of_raw_data);
        const bytes = try range(self.bytes, offset, size);
        if (self.live and size != 0) {
            if (builtin.os.tag != .windows) return error.UnsupportedPlatform;
            try readableSpan(bytes, self.bytes.ptr);
        }
        return bytes;
    }

    fn find(self: Image, signature: Pattern) Error!u32 {
        return self.findReferencing(signature, null);
    }

    const Reference = struct { operand: RelativeOperand, target: u32 };

    fn findReferencing(self: Image, signature: Pattern, reference: ?Reference) Error!u32 {
        var found: ?u32 = null;
        for (self.headers.sections) |section| {
            // Execute-only sections are outside the target domain in both layouts.
            if (!section.flags.MEM_EXECUTE or !section.flags.MEM_READ) continue;
            const bytes = try self.sectionBytes(section);
            if (bytes.len < signature.bytes.len) continue;
            const anchor = signature.bytes[signature.anchor_offset..][0..signature.anchor_len];
            var cursor: usize = signature.anchor_offset;
            while (std.mem.indexOfPos(u8, bytes, cursor, anchor)) |hit| {
                cursor = hit + 1;
                const start = hit - signature.anchor_offset;
                if (!signature.matches(bytes[start..])) continue;
                const rva = section.virtual_address + @as(u32, @intCast(start));
                if (reference) |ref| {
                    const target = self.relativeTarget(bytes[start..], rva, ref.operand) catch continue;
                    if (target != ref.target) continue;
                }
                if (found != null) return error.AmbiguousPattern;
                found = rva;
            }
        }
        return found orelse error.MissingPattern;
    }

    fn relativeTarget(self: Image, bytes: []const u8, rva: u32, operand: RelativeOperand) Error!u32 {
        const displacement = try value(i32, bytes, operand.displacement);
        const target = @as(i64, rva) + @as(i64, @intCast(operand.instruction_end)) + displacement;
        if (target < 0 or target >= self.headers.image_size) return error.MissingPattern;
        return @intCast(target);
    }

    fn codeAt(self: Image, rva: u32, len: usize) Error![]const u8 {
        for (self.headers.sections) |section| {
            if (!section.flags.MEM_EXECUTE or !section.flags.MEM_READ or rva < section.virtual_address) continue;
            const bytes = try self.sectionBytes(section);
            const offset = rva - section.virtual_address;
            if (offset <= bytes.len and len <= bytes.len - offset) return bytes[offset..][0..len];
        }
        return error.MissingPattern;
    }

    fn pointerSlot(self: Image, rva: u32) bool {
        for (self.headers.sections) |section| {
            if (section.flags.MEM_EXECUTE or !section.flags.MEM_READ or rva < section.virtual_address) continue;
            const offset = rva - section.virtual_address;
            const size = sectionSize(section);
            if (offset <= size and @sizeOf(u64) <= size - offset) return true;
        }
        return false;
    }

    fn resolveProfile(self: Image, comptime profile: Profile) Error!GpuTargets {
        const signatures = comptime gpuPatterns(profile.layout);
        const push = try self.find(signatures.push);
        const pop = try self.find(signatures.pop);
        const cleanup = try self.find(signatures.cleanup);
        const slot = try self.relativeTarget(try self.codeAt(pop, pop_context_operand.instruction_end), pop, pop_context_operand);
        const push_slot = try self.relativeTarget(try self.codeAt(push, push_context_operand.instruction_end), push, push_context_operand);
        if (slot != push_slot or !self.pointerSlot(slot)) return error.MissingPattern;
        const context = try self.findReferencing(context_getter, .{ .operand = getter_context_operand, .target = slot });
        // Push's over-budget loop must call the Pop we just resolved.
        const pop_call_offset = 0xdf;
        const call_rva = std.math.add(u32, push, pop_call_offset) catch return error.InvalidImage;
        const call = try self.codeAt(call_rva, 5);
        if (call[0] != 0xe8 or try self.relativeTarget(call, call_rva, .{ .displacement = 1, .instruction_end = 5 }) != pop)
            return error.MissingPattern;
        return .{
            .build = profile.build,
            .push = push,
            .pop = pop,
            .cleanup = cleanup,
            .context = context,
        };
    }

    /// Resolve one coherent build, rejecting duplicate matches or mixed layouts.
    /// Deliberately does not inspect Signal, which may already be hooked.
    pub fn resolveGpu(self: Image) Error!GpuTargets {
        var found: ?GpuTargets = null;
        inline for (profiles) |profile| {
            const candidate: ?GpuTargets = self.resolveProfile(profile) catch |err| switch (err) {
                error.MissingPattern => null,
                else => return err,
            };
            if (candidate) |targets| {
                if (found != null) return error.AmbiguousPattern;
                found = targets;
            }
        }
        return found orelse error.MissingPattern;
    }

    /// Signal's verified body/layout is shared by both builds; GPU hooks are irrelevant.
    pub fn resolveSignal(self: Image) Error!SignalTargets {
        const rva = try self.find(signal_body);
        const slot = try self.relativeTarget(try self.codeAt(rva, signal_body.bytes.len), rva, signal_import_operand);
        if (!self.pointerSlot(slot)) return error.MissingPattern;
        return .{ .signal = rva };
    }

    /// Only resolved RVAs from this mapped image may be converted to addresses.
    pub fn address(self: Image, rva: u32) *anyopaque {
        std.debug.assert(self.layout == .mapped and rva < self.headers.image_size);
        return @ptrCast(@constCast(self.bytes.ptr + rva));
    }
};

fn sectionSize(section: coff.SectionHeader) u32 {
    return if (section.virtual_size == 0) section.size_of_raw_data else section.virtual_size;
}

fn range(bytes: []const u8, offset: usize, len: usize) Error![]const u8 {
    if (offset > bytes.len or len > bytes.len - offset) return error.InvalidImage;
    return bytes[offset..][0..len];
}

fn value(comptime T: type, bytes: []const u8, offset: usize) Error!T {
    return std.mem.bytesAsValue(T, try range(bytes, offset, @sizeOf(T))).*;
}

fn readableRegion(address: [*]const u8, allocation: [*]const u8) Error!memory.MEMORY_BASIC_INFORMATION {
    var info: memory.MEMORY_BASIC_INFORMATION = undefined;
    if (kernel32.VirtualQuery(address, &info, @sizeOf(@TypeOf(info))) == 0) return error.UnreadableMemory;
    const p = info.Protect;
    if (info.AllocationBase != @as(*const anyopaque, @ptrCast(allocation)) or
        info.State.COMMIT == 0 or p.PAGE_GUARD != 0 or p.PAGE_NOACCESS != 0 or
        (p.PAGE_READONLY == 0 and p.PAGE_READWRITE == 0 and p.PAGE_WRITECOPY == 0 and
            p.PAGE_EXECUTE_READ == 0 and p.PAGE_EXECUTE_READWRITE == 0 and p.PAGE_EXECUTE_WRITECOPY == 0))
        return error.UnreadableMemory;
    return info;
}

fn readableSpan(bytes: []const u8, allocation: [*]const u8) Error!void {
    var cursor = @intFromPtr(bytes.ptr);
    const end = std.math.add(usize, cursor, bytes.len) catch return error.InvalidImage;
    while (cursor < end) {
        const info = try readableRegion(@ptrFromInt(cursor), allocation);
        const next = std.math.add(usize, @intFromPtr(info.BaseAddress), info.RegionSize) catch return error.InvalidImage;
        if (next <= cursor) return error.UnreadableMemory;
        cursor = next;
    }
}

/// Scan only the loaded executable; never loads a player or allocates a copy.
pub fn current() Error!Image {
    if (builtin.os.tag != .windows) return error.UnsupportedPlatform;
    const module = kernel32.GetModuleHandleW(null) orelse return error.InvalidImage;
    const base: [*]const u8 = @ptrCast(module);
    const first_region = try readableRegion(base, base);
    if (first_region.BaseAddress != @as(*anyopaque, @ptrCast(module))) return error.InvalidImage;
    const headers = try Headers.parse(base[0..first_region.RegionSize]);
    _ = std.math.add(usize, @intFromPtr(base), headers.image_size) catch return error.InvalidImage;
    var image = try Image.init(base[0..headers.image_size], .mapped);
    image.live = true;
    return image;
}

test "signal resolution excludes execute-only code but rejects readable duplicates" {
    var bytes: [512]u8 = @splat(0);
    var sections: [3]coff.SectionHeader = undefined;
    for (&sections, 0..) |*section, i| {
        section.* = std.mem.zeroes(coff.SectionHeader);
        section.virtual_address = @intCast(64 + i * 128);
        section.pointer_to_raw_data = section.virtual_address;
        section.virtual_size = 128;
        section.size_of_raw_data = 128;
        section.flags.MEM_EXECUTE = i < 2;
        section.flags.MEM_READ = i != 1;
    }
    // An identical target in execute-only code must not create ambiguity.
    for ([_]usize{ 64, 192 }) |offset| {
        @memcpy(bytes[offset..][0..signal_body.bytes.len], signal_body.bytes);
        std.mem.writeInt(i32, bytes[offset + signal_import_operand.displacement ..][0..4], 384 - @as(i32, @intCast(offset + signal_import_operand.instruction_end)), .little);
    }
    for ([_]Layout{ .file, .mapped }) |layout| {
        const image: Image = .{
            .bytes = &bytes,
            .layout = layout,
            .headers = .{ .image_size = bytes.len, .sections = &sections },
        };
        try std.testing.expectEqual(@as(u32, 64), (try image.resolveSignal()).signal);
        try std.testing.expectError(error.MissingPattern, image.codeAt(192, 1));
        std.mem.swap(coff.SectionHeader, &sections[0], &sections[1]);
        try std.testing.expectEqual(@as(u32, 64), (try image.resolveSignal()).signal);
        std.mem.swap(coff.SectionHeader, &sections[0], &sections[1]);

        sections[1].flags.MEM_READ = true;
        try std.testing.expectError(error.AmbiguousPattern, image.resolveSignal());
        sections[1].flags.MEM_READ = false;
        sections[0].flags.MEM_READ = false;
        try std.testing.expectError(error.MissingPattern, image.resolveSignal());
        sections[0].flags.MEM_READ = true;
    }
}
