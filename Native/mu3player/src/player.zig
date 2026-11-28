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

pub const GraphicsJobsTargets = struct {
    /// `SharedGfxBuffer` deleting destructor.
    destructor: u32,
    /// `PreLateUpdate.EndGraphicsJobsLate` player-loop forward.
    end_graphics_jobs: u32,
    /// `GetThreadedGfxDevice`.
    threaded_device: u32,
    /// `GetPlayerSettingsPtr`.
    settings_getter: u32,
    /// `g_SharedPassContext` pointer slot.
    pass_context_slot: u32,
};

/// `GfxDevice::DeleteBuffer` vtable byte offset called by the matched destructor.
pub const device_delete_buffer_slot: usize = 0x218;

/// `PlayerSettings::graphicsJobs`, the byte `-force-gfx-jobs` writes.
pub const settings_graphics_jobs_offset: usize = 0x26B;
/// `ShaderPassContext::isUsingJobifiedRendering`, copied once from `graphicsJobs` at startup.
pub const pass_context_jobified_offset: usize = 0xC1;

pub const GpuSkinningTargets = struct {
    /// `GetPlayerSettingsPtr`, as called by `SkinnedMeshRenderer::DoesQualifyForGPUSkinning`.
    settings_getter: u32,
};

/// `PlayerSettings::gpuSkinning`, read only by `DoesQualifyForGPUSkinning` at each skin preparation.
pub const settings_gpu_skinning_offset: usize = 0x26A;

pub const SwapChainTargets = struct {
    primary_slot: u32,
};

pub const swap_chain_offset: usize = 0x240;
pub const swap_chain1_offset: usize = 0x248;
pub const swap_chain2_offset: usize = 0x250;
pub const exclusive_fullscreen_offset: usize = 0x269;

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

// Non-development build. If `m_GfxBuffer` (+0x10) is set, the destructor deletes it through
// the calling thread's device before freeing itself; a graphics job therefore records the
// delete into its own command stream.
const shared_buffer_destructor = pattern(
    "48 89 5C 24 08 " ++ // save rbx
        "57 " ++ // push rdi
        "48 83 EC 20 " ++
        "48 83 79 10 00 " ++ // cmp [rcx+10h] (m_GfxBuffer), 0
        "48 8D 05 ?? ?? ?? ?? " ++ // lea rax, [rip+SharedGfxBuffer vtable]
        "8B FA " ++ // mov edi, edx (delete flags)
        "48 89 01 " ++
        "48 8B D9 " ++
        "74 16 " ++ // no buffer
        "E8 ?? ?? ?? ?? " ++ // call GetThreadedGfxDevice
        "48 8B 53 10 " ++ // mov rdx, [rbx+10h]
        "4C 8B 00 " ++
        "48 8B C8 " ++
        "41 FF 90 18 02 00 00 " ++ // call [r8+218h] (GfxDevice::DeleteBuffer)
        "48 8D 05 ?? ?? ?? ?? " ++ // lea rax, [rip+base vtable]
        "48 89 03 " ++
        "40 F6 C7 01 " ++ // test dil, 1
        "74 08 " ++
        "48 8B CB " ++
        "E8 ?? ?? ?? ?? " ++ // call operator delete
        "48 8B C3 " ++
        "48 8B 5C 24 30 " ++
        "48 83 C4 20 " ++
        "5F " ++
        "C3",
);

// `GetThreadedGfxDevice`: TlsGetValue(gfxDeviceThreaded key) as a tail call.
const threaded_device_getter = pattern("8B 0D ?? ?? ?? ?? 48 FF 25 ?? ?? ?? ??");

// Non-development build. Unless jobs sync at end of frame, completes the frame's async job
// fences (EndAsyncJobFrame), advances DynamicVBO and swaps its buffers.
const end_graphics_jobs_late = pattern(
    "40 53 " ++ // push rbx
        "48 83 EC 20 " ++
        "8B 0D ?? ?? ?? ?? " ++ // mov ecx, [rip+gfxDeviceThreaded key]
        "FF 15 ?? ?? ?? ?? " ++ // call TlsGetValue
        "48 8B C8 " ++
        "48 8B D8 " ++
        "48 8B 10 " ++
        "FF 92 20 07 00 00 " ++ // call [rdx+720h] (ShouldJobsSyncAtEndOfFrame)
        "84 C0 " ++
        "75 2E " ++
        "48 8B 13 " ++
        "48 8B CB " ++
        "FF 92 58 02 00 00 " ++ // call [rdx+258h] (EndAsyncJobFrame)
        "E8 ?? ?? ?? ?? " ++ // call DynamicVBO::NextFrame
        "8B 0D ?? ?? ?? ?? " ++ // mov ecx, [rip+gfxDeviceThreaded key]
        "8B D8 " ++
        "FF 15 ?? ?? ?? ?? " ++ // call TlsGetValue
        "8B D3 " ++
        "4C 8B 00 " ++
        "48 8B C8 " ++
        "41 FF 90 50 04 00 00 " ++ // call [r8+450h] (SwapDynamicVBOBuffers)
        "48 83 C4 20 " ++
        "5B " ++
        "C3",
);

// Non-development build. Creates the root pass context and copies
// `PlayerSettings.graphicsJobs` (+0x26B) into its `isUsingJobifiedRendering` (+0xC1).
const pass_context_initialize = pattern(
    "48 83 EC 38 " ++
        "BA 14 00 00 00 " ++ // mov edx, kMemShaderId
        "4C 8D 0D ?? ?? ?? ?? " ++ // lea r9, [rip+file]
        "B9 C8 00 00 00 " ++ // mov ecx, sizeof(ShaderPassContext)
        "44 8D 42 FC " ++
        "C7 44 24 20 0B 00 00 00 " ++
        "E8 ?? ?? ?? ?? " ++ // call operator new
        "48 85 C0 " ++
        "74 08 " ++
        "48 8B C8 " ++
        "E8 ?? ?? ?? ?? " ++ // call ShaderPassContext::ShaderPassContext
        "48 89 05 ?? ?? ?? ?? " ++ // mov [rip+g_SharedPassContext], rax
        "C6 80 C1 00 00 00 00 " ++ // mov byte [rax+0C1h], 0
        "E8 ?? ?? ?? ?? " ++ // call GetPlayerSettingsPtr
        "0F B6 88 6B 02 00 00 " ++ // movzx ecx, byte [rax+26Bh]
        "48 8B 05 ?? ?? ?? ?? " ++ // mov rax, [rip+g_SharedPassContext]
        "88 88 C1 00 00 00 " ++ // mov [rax+0C1h], cl
        "48 83 C4 38 " ++
        "C3",
);

// `GetPlayerSettingsPtr`: GetGameManagerIfExists(0) as a tail call.
const settings_getter = pattern("33 C9 E9 ?? ?? ?? ??");
// `GetGameManagerIfExists`: managers[index] from a RIP-relative array.
const game_manager_lookup = pattern("48 63 C1 48 8D 0D ?? ?? ?? ?? 48 8B 04 C1 C3");
// Per-frame readers that must use the same getter/offset and slot/offset.
const should_jobs_sync = pattern("48 83 EC 28 E8 ?? ?? ?? ?? 33 C9 38 88 6B 02 00 00 0F 94 C0 48 83 C4 28 C3");
const num_graphics_jobs = pattern(
    "48 83 EC 28 48 8B 05 ?? ?? ?? ?? 80 B8 C1 00 00 00 00 74 0C E8 ?? ?? ?? ?? 48 98 48 83 C4 28 C3",
);
// `SkinnedMeshRenderer::DoesQualifyForGPUSkinning`: no active blend shapes, a mesh, then
// `PlayerSettings.gpuSkinning` (+0x26A) and the device capability (+0x291).
const gpu_skinning_qualifier = pattern(
    "48 83 EC 28 " ++
        "48 83 B9 98 02 00 00 00 75 2D " ++
        "48 83 B9 A0 02 00 00 00 74 23 " ++
        "E8 ?? ?? ?? ?? " ++ // call GetPlayerSettingsPtr
        "80 B8 6A 02 00 00 00 74 15 " ++
        "E8 ?? ?? ?? ?? " ++ // call GetGraphicsCaps
        "80 B8 91 02 00 00 00 74 07 " ++
        "B0 01 48 83 C4 28 C3",
);

const present_frame = pattern(
    "32 DB " ++
        "48 8B 05 ?? ?? ?? ?? " ++
        "48 8B 88 40 02 00 00 " ++
        "48 85 C9 " ++
        "74 ?? " ++
        "45 33 C0 " ++
        "41 8B D0 " ++
        "84 DB " ++
        "75 06 " ++
        "8B 90 60 02 00 00 " ++
        "44 39 80 60 02 00 00",
);

const RelativeOperand = struct { displacement: usize, instruction_end: usize };
const pop_context_operand: RelativeOperand = .{ .displacement = 0x24, .instruction_end = 0x28 };
const push_context_operand: RelativeOperand = .{ .displacement = 0x53, .instruction_end = 0x57 };
const getter_context_operand: RelativeOperand = .{ .displacement = 3, .instruction_end = 7 };
const signal_import_operand: RelativeOperand = .{ .displacement = 83, .instruction_end = 87 };
const destructor_getter_call: RelativeOperand = .{ .displacement = 0x21, .instruction_end = 0x25 };
const getter_key_operand: RelativeOperand = .{ .displacement = 2, .instruction_end = 6 };
const forward_key_operand: RelativeOperand = .{ .displacement = 8, .instruction_end = 12 };
const forward_second_key_operand: RelativeOperand = .{ .displacement = 56, .instruction_end = 60 };
const pass_context_store_operand: RelativeOperand = .{ .displacement = 0x36, .instruction_end = 0x3A };
const pass_context_getter_call: RelativeOperand = .{ .displacement = 0x42, .instruction_end = 0x46 };
const pass_context_load_operand: RelativeOperand = .{ .displacement = 0x50, .instruction_end = 0x54 };
const settings_getter_jump: RelativeOperand = .{ .displacement = 3, .instruction_end = 7 };
const game_manager_array_operand: RelativeOperand = .{ .displacement = 6, .instruction_end = 10 };
const should_jobs_sync_call: RelativeOperand = .{ .displacement = 5, .instruction_end = 9 };
const num_graphics_jobs_slot: RelativeOperand = .{ .displacement = 7, .instruction_end = 11 };
const qualifier_settings_call: RelativeOperand = .{ .displacement = 0x19, .instruction_end = 0x1D };
const present_swap_chain_operand: RelativeOperand = .{ .displacement = 5, .instruction_end = 9 };

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

    /// The destructor must call the TLS device getter, and the forward must read the same
    /// TLS key twice. The root pass-context initializer names the flag bytes; its settings
    /// getter must be the game-manager lookup, and both per-frame readers must use the same
    /// getter and slot. Any other shape fails closed.
    pub fn resolveGraphicsJobs(self: Image) Error!GraphicsJobsTargets {
        const destructor = try self.find(shared_buffer_destructor);
        const destructor_code = try self.codeAt(destructor, shared_buffer_destructor.bytes.len);
        const device = try self.relativeTarget(destructor_code, destructor, destructor_getter_call);
        const device_code = try self.codeAt(device, threaded_device_getter.bytes.len);
        if (!threaded_device_getter.matches(device_code)) return error.MissingPattern;
        const key = try self.relativeTarget(device_code, device, getter_key_operand);
        if (!self.pointerSlot(key)) return error.MissingPattern;
        const forward = try self.findReferencing(end_graphics_jobs_late, .{ .operand = forward_key_operand, .target = key });
        const forward_code = try self.codeAt(forward, end_graphics_jobs_late.bytes.len);
        if (try self.relativeTarget(forward_code, forward, forward_second_key_operand) != key) return error.MissingPattern;

        const initializer = try self.find(pass_context_initialize);
        const code = try self.codeAt(initializer, pass_context_initialize.bytes.len);
        const slot = try self.relativeTarget(code, initializer, pass_context_store_operand);
        if (try self.relativeTarget(code, initializer, pass_context_load_operand) != slot or !self.pointerSlot(slot))
            return error.MissingPattern;
        const settings = try self.relativeTarget(code, initializer, pass_context_getter_call);
        try self.settingsGetter(settings);
        _ = try self.findReferencing(should_jobs_sync, .{ .operand = should_jobs_sync_call, .target = settings });
        _ = try self.findReferencing(num_graphics_jobs, .{ .operand = num_graphics_jobs_slot, .target = slot });
        return .{
            .destructor = destructor,
            .end_graphics_jobs = forward,
            .threaded_device = device,
            .settings_getter = settings,
            .pass_context_slot = slot,
        };
    }

    /// The only reader of the setting names both the getter and the byte; the getter must be
    /// the game-manager lookup that the graphics-jobs resolution also requires.
    pub fn resolveGpuSkinning(self: Image) Error!GpuSkinningTargets {
        const qualifier = try self.find(gpu_skinning_qualifier);
        const code = try self.codeAt(qualifier, gpu_skinning_qualifier.bytes.len);
        const settings = try self.relativeTarget(code, qualifier, qualifier_settings_call);
        try self.settingsGetter(settings);
        return .{ .settings_getter = settings };
    }

    pub fn resolveSwapChain(self: Image) Error!SwapChainTargets {
        const rva = try self.find(present_frame);
        const slot = try self.relativeTarget(try self.codeAt(rva, present_frame.bytes.len), rva, present_swap_chain_operand);
        if (!self.pointerSlot(slot)) return error.MissingPattern;
        return .{ .primary_slot = slot };
    }

    /// `GetPlayerSettingsPtr` tail-calls `GetGameManagerIfExists(0)`, which indexes a data array.
    fn settingsGetter(self: Image, getter: u32) Error!void {
        const getter_code = try self.codeAt(getter, settings_getter.bytes.len);
        if (!settings_getter.matches(getter_code)) return error.MissingPattern;
        const lookup = try self.relativeTarget(getter_code, getter, settings_getter_jump);
        const lookup_code = try self.codeAt(lookup, game_manager_lookup.bytes.len);
        if (!game_manager_lookup.matches(lookup_code)) return error.MissingPattern;
        if (!self.pointerSlot(try self.relativeTarget(lookup_code, lookup, game_manager_array_operand)))
            return error.MissingPattern;
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
