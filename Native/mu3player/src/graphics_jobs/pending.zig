//! Fixed-capacity handoff of buffer pointers from graphics jobs to the main thread.
//! A full list rejects the push so the caller can keep Unity's immediate delete.
const std = @import("std");

pub fn Pending(comptime capacity: usize) type {
    return struct {
        const Self = @This();

        locked: std.atomic.Value(bool) = .init(false),
        items: [capacity]usize = undefined,
        len: usize = 0,

        fn lock(self: *Self) void {
            while (self.locked.cmpxchgWeak(false, true, .acquire, .monotonic) != null)
                std.atomic.spinLoopHint();
        }

        fn unlock(self: *Self) void {
            self.locked.store(false, .release);
        }

        /// Returns false when the list is full; the item is then not retained.
        pub fn push(self: *Self, item: usize) bool {
            self.lock();
            defer self.unlock();
            if (self.len == capacity) return false;
            self.items[self.len] = item;
            self.len += 1;
            return true;
        }

        /// Moves every accepted item, oldest first, into `out` and empties the list.
        pub fn take(self: *Self, out: *[capacity]usize) []usize {
            self.lock();
            defer self.unlock();
            const n = self.len;
            @memcpy(out[0..n], self.items[0..n]);
            self.len = 0;
            return out[0..n];
        }
    };
}

test "a full list rejects the push, and every accepted item is taken exactly once in order" {
    var list: Pending(3) = .{};
    var out: [3]usize = undefined;
    try std.testing.expect(list.push(10));
    try std.testing.expect(list.push(11));
    try std.testing.expect(list.push(12));
    try std.testing.expect(!list.push(13));
    try std.testing.expectEqualSlices(usize, &.{ 10, 11, 12 }, list.take(&out));
    try std.testing.expectEqual(@as(usize, 0), list.take(&out).len);
    try std.testing.expect(list.push(14));
    try std.testing.expectEqualSlices(usize, &.{14}, list.take(&out));
}

test "concurrent producers never lose or duplicate an accepted item" {
    const per_thread = 2000;
    const Shared = struct {
        list: Pending(64) = .{},
        accepted: [4]std.atomic.Value(u64) = @splat(.init(0)),
        done: std.atomic.Value(u32) = .init(0),

        fn produce(self: *@This(), id: usize) void {
            var i: usize = 0;
            while (i < per_thread) : (i += 1) {
                // Encode producer and sequence so each value is unique.
                while (!self.list.push(id * per_thread + i + 1)) std.atomic.spinLoopHint();
                _ = self.accepted[id].fetchAdd(1, .monotonic);
            }
            _ = self.done.fetchAdd(1, .release);
        }
    };
    var shared: Shared = .{};
    var seen: [4 * per_thread + 1]bool = @splat(false);
    var threads: [4]std.Thread = undefined;
    for (&threads, 0..) |*thread, id| thread.* = try std.Thread.spawn(.{}, Shared.produce, .{ &shared, id });
    var out: [64]usize = undefined;
    var taken: usize = 0;
    while (true) {
        const finished = shared.done.load(.acquire) == threads.len;
        for (shared.list.take(&out)) |item| {
            try std.testing.expect(!seen[item]);
            seen[item] = true;
            taken += 1;
        }
        if (finished and shared.list.take(&out).len == 0) break;
    }
    for (threads) |thread| thread.join();
    try std.testing.expectEqual(@as(usize, 4 * per_thread), taken);
}
