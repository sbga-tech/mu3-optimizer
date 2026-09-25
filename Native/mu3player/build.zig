const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{
        .default_target = .{ .cpu_arch = .x86_64, .os_tag = .windows, .abi = .gnu },
    });
    const optimize = b.standardOptimizeOption(.{});
    const win32 = b.dependency("win32", .{}).module("win32");

    const module = b.createModule(.{
        .root_source_file = b.path("src/lib.zig"),
        .target = target,
        .optimize = optimize,
        .link_libc = true,
    });
    module.addImport("win32", win32);
    module.addIncludePath(b.path("vendor/minhook/include"));
    module.addIncludePath(b.path("vendor/minhook/src"));
    module.addCMacro("_WIN32_WINNT", "0x0601");
    module.addCMacro("WIN32_LEAN_AND_MEAN", "1");
    module.addCSourceFiles(.{
        .files = &.{
            "vendor/minhook/src/buffer.c",
            "vendor/minhook/src/hook.c",
            "vendor/minhook/src/trampoline.c",
            "vendor/minhook/src/hde/hde64.c",
        },
    });
    module.linkSystemLibrary("kernel32", .{});

    const library = b.addLibrary(.{
        .name = "mu3player",
        .linkage = .dynamic,
        .root_module = module,
    });
    b.getInstallStep().dependOn(&b.addInstallArtifact(library, .{
        .implib_dir = .disabled,
    }).step);
}
