//! Batched PrimitiveMesh quad emission for MU3 (Unity 5.6.4, x64 Windows).
//!
//! The managed caller owns and pins every buffer for exactly one call. This
//! module validates the complete batch before writing, then emits commands in
//! source order with the same f32 values and triangle ordering as JointUtil.

use core::{mem, ptr, slice};

const COMMAND_PRIM: u32 = 0;
const COMMAND_WALL: u32 = 1;
const COMMAND_QUAD_RANGE: u32 = 2;

const STATUS_OK: i32 = 0;
const STATUS_NULL: i32 = -1;
const STATUS_COUNT: i32 = -2;
const STATUS_KIND: i32 = -3;
const STATUS_CAPACITY: i32 = -4;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Vec2 {
    pub x: f32,
    pub y: f32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Vec3 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Color {
    pub r: f32,
    pub g: f32,
    pub b: f32,
    pub a: f32,
}

/// Fixed 92-byte command shared with NativeGeometryEmitter.Command.
/// Fields are interpreted by kind; unused values remain zero.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct Command {
    pub kind: u32,
    pub flags: u32,
    pub values: [f32; 13],
    pub color0: Color,
    pub color1: Color,
}

#[no_mangle]
pub extern "C" fn mu3_geometry_command_size() -> u32 {
    mem::size_of::<Command>() as u32
}

/// Emits all commands into caller-owned buffers. Counts are capacities on
/// entry and written element counts on success. No buffer is changed unless
/// the entire batch validates.
#[no_mangle]
pub unsafe extern "C" fn mu3_geometry_emit(
    commands: *const Command,
    command_count: i32,
    vertices: *mut Vec3,
    vertex_capacity: i32,
    colors: *mut Color,
    color_capacity: i32,
    uvs: *mut Vec2,
    uv_capacity: i32,
    triangles: *mut i32,
    triangle_capacity: i32,
    vertex_count: *mut i32,
    uv_count: *mut i32,
    triangle_count: *mut i32,
) -> i32 {
    if command_count < 0 || vertex_capacity < 0 || color_capacity < 0
        || uv_capacity < 0 || triangle_capacity < 0
    {
        return STATUS_COUNT;
    }
    if vertex_count.is_null() || uv_count.is_null() || triangle_count.is_null() {
        return STATUS_NULL;
    }
    if command_count == 0 {
        *vertex_count = 0;
        *uv_count = 0;
        *triangle_count = 0;
        return STATUS_OK;
    }
    if commands.is_null() || vertices.is_null() || colors.is_null() || triangles.is_null() {
        return STATUS_NULL;
    }

    let commands = slice::from_raw_parts(commands, command_count as usize);
    let mut needed_vertices: usize = 0;
    let mut needed_uvs: usize = 0;
    let mut needed_triangles: usize = 0;
    for command in commands {
        match command.kind {
            COMMAND_PRIM | COMMAND_QUAD_RANGE => {
                needed_vertices += 4;
                needed_uvs += 4;
                needed_triangles += 6;
            }
            COMMAND_WALL => {
                needed_vertices += 4;
                needed_triangles += 6;
            }
            _ => return STATUS_KIND,
        }
    }
    if needed_vertices > vertex_capacity as usize
        || needed_vertices > color_capacity as usize
        || needed_uvs > uv_capacity as usize
        || needed_triangles > triangle_capacity as usize
        || (needed_uvs != 0 && uvs.is_null())
    {
        return STATUS_CAPACITY;
    }

    let mut v = 0usize;
    let mut u = 0usize;
    let mut t = 0usize;
    for command in commands {
        match command.kind {
            COMMAND_PRIM => emit_prim(command, vertices, colors, uvs, triangles, &mut v, &mut u, &mut t),
            COMMAND_WALL => emit_wall(command, vertices, colors, triangles, &mut v, &mut t),
            COMMAND_QUAD_RANGE => emit_quad_range(command, vertices, colors, uvs, triangles, &mut v, &mut u, &mut t),
            _ => return STATUS_KIND,
        }
    }

    *vertex_count = v as i32;
    *uv_count = u as i32;
    *triangle_count = t as i32;
    STATUS_OK
}

#[inline(always)]
unsafe fn write_quad_indices(triangles: *mut i32, t: usize, v: usize) {
    ptr::write(triangles.add(t), v as i32);
    ptr::write(triangles.add(t + 1), (v + 1) as i32);
    ptr::write(triangles.add(t + 2), (v + 2) as i32);
    ptr::write(triangles.add(t + 3), (v + 2) as i32);
    ptr::write(triangles.add(t + 4), (v + 1) as i32);
    ptr::write(triangles.add(t + 5), (v + 3) as i32);
}

#[inline(always)]
unsafe fn write_colors(colors: *mut Color, v: usize, low: Color, high: Color) {
    ptr::write(colors.add(v), low);
    ptr::write(colors.add(v + 1), low);
    ptr::write(colors.add(v + 2), high);
    ptr::write(colors.add(v + 3), high);
}

#[inline(always)]
unsafe fn emit_prim(
    command: &Command,
    vertices: *mut Vec3,
    colors: *mut Color,
    uvs: *mut Vec2,
    triangles: *mut i32,
    v: &mut usize,
    u: &mut usize,
    t: &mut usize,
) {
    let p = &command.values;
    let (x0a, x0b, x1a, x1b, u0a, u0b, u1a, u1b) = if p[2] < p[5] {
        (p[0], p[1], p[3], p[4], p[6], p[7], p[8], p[9])
    } else {
        (p[1], p[0], p[4], p[3], p[7], p[6], p[9], p[8])
    };
    ptr::write(vertices.add(*v), Vec3 { x: x0a, y: p[12], z: p[2] });
    ptr::write(vertices.add(*v + 1), Vec3 { x: x0b, y: p[12], z: p[2] });
    ptr::write(vertices.add(*v + 2), Vec3 { x: x1a, y: p[12], z: p[5] });
    ptr::write(vertices.add(*v + 3), Vec3 { x: x1b, y: p[12], z: p[5] });
    ptr::write(uvs.add(*u), Vec2 { x: u0a, y: p[10] });
    ptr::write(uvs.add(*u + 1), Vec2 { x: u0b, y: p[10] });
    ptr::write(uvs.add(*u + 2), Vec2 { x: u1a, y: p[11] });
    ptr::write(uvs.add(*u + 3), Vec2 { x: u1b, y: p[11] });
    write_colors(colors, *v, command.color0, command.color1);
    write_quad_indices(triangles, *t, *v);
    *v += 4;
    *u += 4;
    *t += 6;
}

#[inline(always)]
unsafe fn emit_wall(
    command: &Command,
    vertices: *mut Vec3,
    colors: *mut Color,
    triangles: *mut i32,
    v: &mut usize,
    t: &mut usize,
) {
    let p = &command.values;
    let (xa, za, xb, zb) = if p[1] < p[3] {
        (p[0], p[1], p[2], p[3])
    } else {
        (p[2], p[3], p[0], p[1])
    };
    ptr::write(vertices.add(*v), Vec3 { x: xa, y: p[4], z: za });
    ptr::write(vertices.add(*v + 1), Vec3 { x: xb, y: p[4], z: zb });
    ptr::write(vertices.add(*v + 2), Vec3 { x: xa, y: p[5], z: za });
    ptr::write(vertices.add(*v + 3), Vec3 { x: xb, y: p[5], z: zb });
    write_colors(colors, *v, command.color0, command.color1);
    write_quad_indices(triangles, *t, *v);
    *v += 4;
    *t += 6;
}

#[inline(always)]
unsafe fn emit_quad_range(
    command: &Command,
    vertices: *mut Vec3,
    colors: *mut Color,
    uvs: *mut Vec2,
    triangles: *mut i32,
    v: &mut usize,
    u: &mut usize,
    t: &mut usize,
) {
    let p = &command.values;
    let right = command.flags & 1 != 0;
    let (d0x, d0z, d1x, d1z, u0x, u1x, t0x, t0z, t1x, t1z) = if !right {
        (p[0], p[1], p[2], p[3], 0.0f32, 1.0f32, p[4], p[5], p[6], p[7])
    } else {
        (p[2], p[3], p[0], p[1], 1.0f32, 0.0f32, p[6], p[7], p[4], p[5])
    };
    ptr::write(vertices.add(*v), Vec3 { x: d0x, y: p[10], z: d0z });
    ptr::write(vertices.add(*v + 1), Vec3 { x: d1x, y: p[10], z: d1z });
    ptr::write(vertices.add(*v + 2), Vec3 { x: t0x, y: p[10], z: t0z });
    ptr::write(vertices.add(*v + 3), Vec3 { x: t1x, y: p[10], z: t1z });
    ptr::write(uvs.add(*u), Vec2 { x: u0x, y: p[8] });
    ptr::write(uvs.add(*u + 1), Vec2 { x: u1x, y: p[8] });
    ptr::write(uvs.add(*u + 2), Vec2 { x: u0x, y: p[9] });
    ptr::write(uvs.add(*u + 3), Vec2 { x: u1x, y: p[9] });
    write_colors(colors, *v, command.color0, command.color1);
    write_quad_indices(triangles, *t, *v);
    *v += 4;
    *u += 4;
    *t += 6;
}

#[cfg(test)]
mod tests {
    use super::*;

    fn color(value: f32) -> Color {
        Color { r: value, g: value + 1.0, b: value + 2.0, a: value + 3.0 }
    }

    fn command(kind: u32, flags: u32, values: [f32; 13], low: f32, high: f32) -> Command {
        Command { kind, flags, values, color0: color(low), color1: color(high) }
    }

    #[test]
    fn command_layout_is_stable() {
        assert_eq!(mem::size_of::<Command>(), 92);
        assert_eq!(mem::align_of::<Command>(), 4);
    }

    #[test]
    fn mixed_batch_preserves_order_and_winding() {
        let commands = [
            command(
                COMMAND_PRIM,
                0,
                [1.0, 2.0, 8.0, 3.0, 4.0, 5.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 7.0],
                10.0,
                20.0,
            ),
            command(
                COMMAND_WALL,
                0,
                [11.0, 1.0, 12.0, 2.0, 3.0, 4.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
                30.0,
                40.0,
            ),
            command(
                COMMAND_QUAD_RANGE,
                1,
                [21.0, 22.0, 23.0, 24.0, 25.0, 26.0, 27.0, 28.0, 0.7, 0.8, 9.0, 0.0, 0.0],
                50.0,
                60.0,
            ),
        ];
        let mut vertices = [Vec3 { x: 0.0, y: 0.0, z: 0.0 }; 12];
        let mut colors = [color(0.0); 12];
        let mut uvs = [Vec2 { x: 0.0, y: 0.0 }; 8];
        let mut triangles = [0; 18];
        let mut vc = -1;
        let mut uc = -1;
        let mut tc = -1;
        let status = unsafe {
            mu3_geometry_emit(
                commands.as_ptr(),
                commands.len() as i32,
                vertices.as_mut_ptr(),
                vertices.len() as i32,
                colors.as_mut_ptr(),
                colors.len() as i32,
                uvs.as_mut_ptr(),
                uvs.len() as i32,
                triangles.as_mut_ptr(),
                triangles.len() as i32,
                &mut vc,
                &mut uc,
                &mut tc,
            )
        };
        assert_eq!(status, STATUS_OK);
        assert_eq!((vc, uc, tc), (12, 8, 18));
        assert_eq!((vertices[0].x, vertices[0].y, vertices[0].z), (2.0, 7.0, 8.0));
        assert_eq!((vertices[4].x, vertices[4].y, vertices[4].z), (11.0, 3.0, 1.0));
        assert_eq!((vertices[8].x, vertices[8].y, vertices[8].z), (23.0, 9.0, 24.0));
        assert_eq!((uvs[0].x, uvs[0].y), (0.2, 0.5));
        assert_eq!((uvs[4].x, uvs[4].y), (1.0, 0.7));
        assert_eq!(triangles, [0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7, 8, 9, 10, 10, 9, 11]);
        assert_eq!(colors[2].r, 20.0);
        assert_eq!(colors[10].r, 60.0);
    }

    #[test]
    fn invalid_batch_does_not_write() {
        let command = command(99, 0, [0.0; 13], 0.0, 0.0);
        let mut vertices = [Vec3 { x: 7.0, y: 8.0, z: 9.0 }; 4];
        let mut colors = [color(1.0); 4];
        let mut uvs = [Vec2 { x: 5.0, y: 6.0 }; 4];
        let mut triangles = [17; 6];
        let mut vc = -1;
        let mut uc = -1;
        let mut tc = -1;
        let status = unsafe {
            mu3_geometry_emit(
                &command, 1, vertices.as_mut_ptr(), 4, colors.as_mut_ptr(), 4,
                uvs.as_mut_ptr(), 4, triangles.as_mut_ptr(), 6, &mut vc, &mut uc, &mut tc,
            )
        };
        assert_eq!(status, STATUS_KIND);
        assert_eq!(vertices[0].x, 7.0);
        assert_eq!(triangles[0], 17);
    }

    #[test]
    fn insufficient_capacity_does_not_write() {
        let command = command(COMMAND_PRIM, 0, [0.0; 13], 0.0, 0.0);
        let mut vertices = [Vec3 { x: 7.0, y: 8.0, z: 9.0 }; 4];
        let mut colors = [color(1.0); 4];
        let mut uvs = [Vec2 { x: 5.0, y: 6.0 }; 3];
        let mut triangles = [17; 6];
        let mut vc = -1;
        let mut uc = -1;
        let mut tc = -1;
        let status = unsafe {
            mu3_geometry_emit(
                &command, 1, vertices.as_mut_ptr(), 4, colors.as_mut_ptr(), 4,
                uvs.as_mut_ptr(), 3, triangles.as_mut_ptr(), 6, &mut vc, &mut uc, &mut tc,
            )
        };
        assert_eq!(status, STATUS_CAPACITY);
        assert_eq!(vertices[0].x, 7.0);
        assert_eq!(triangles[0], 17);
    }
}
