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
    });
    module.addImport("win32", win32);
    const player = b.createModule(.{
        .root_source_file = b.path("../mu3player/src/player.zig"),
        .target = target,
        .optimize = optimize,
    });
    player.addImport("win32", win32);
    module.addImport("player", player);
    module.linkSystemLibrary("kernel32", .{});

    const library = b.addLibrary(.{
        .name = "mu3latency",
        .linkage = .dynamic,
        .root_module = module,
    });
    b.getInstallStep().dependOn(&b.addInstallArtifact(library, .{
        .implib_dir = .disabled,
    }).step);
}
