//! Fixed-slot query-to-fence-value sidecar for GPU fence waits.

const SIDECAR_CAP: usize = 16;

const SidecarError = error{ Overflow, Duplicate };

pub const Marker = struct {
    owner: usize,
    generation: u32,
    query: usize,
    value: u64,
};

const SidecarSlot = struct {
    used: bool = false,
    marker: Marker = .{ .owner = 0, .generation = 0, .query = 0, .value = 0 },
};

pub const Sidecar = struct {
    slots: [SIDECAR_CAP]SidecarSlot = [_]SidecarSlot{.{}} ** SIDECAR_CAP,
    used: usize = 0,

    pub fn insert(self: *Sidecar, marker: Marker) SidecarError!void {
        if (marker.query == 0) return error.Duplicate;
        for (self.slots) |s| {
            if (s.used and s.marker.query == marker.query) return error.Duplicate;
        }
        for (&self.slots) |*s| {
            if (!s.used) {
                s.used = true;
                s.marker = marker;
                self.used += 1;
                return;
            }
        }
        return error.Overflow;
    }

    pub fn find(self: *const Sidecar, query: usize) ?Marker {
        if (query == 0) return null;
        for (self.slots) |s| {
            if (s.used and s.marker.query == query) return s.marker;
        }
        return null;
    }

    pub fn removeQuery(self: *Sidecar, query: usize) bool {
        if (query == 0) return false;
        for (&self.slots) |*s| {
            if (s.used and s.marker.query == query) {
                s.used = false;
                s.marker = .{ .owner = 0, .generation = 0, .query = 0, .value = 0 };
                self.used -|= 1;
                return true;
            }
        }
        return false;
    }

    pub fn clearOwner(self: *Sidecar, owner: usize) void {
        var remain: usize = 0;
        for (&self.slots) |*s| {
            if (s.used and s.marker.owner == owner) {
                s.used = false;
                s.marker = .{ .owner = 0, .generation = 0, .query = 0, .value = 0 };
            } else if (s.used) {
                remain += 1;
            }
        }
        self.used = remain;
    }

    pub fn clearAll(self: *Sidecar) void {
        self.* = .{};
    }
};
