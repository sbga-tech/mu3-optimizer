//! Native SwingJoint batch solver for MU3 (Unity 5.6.4, x64 Windows).
//!
//! Replicates `MU3.SwingJoint.update()` math exactly as decompiled from
//! sddt160 Assembly-CSharp, including:
//! - UnityEngine managed `Quaternion*Quaternion` / `Quaternion*Vector3`
//!   operator numerics (operation order preserved for f32 bit parity),
//! - `MU3.CustomMath.normalize`,
//! - `MU3.SwingJointColliderInformation.getCollisionPointWithSphere`,
//! - `MU3.SimpleKDTree` plane partition + candidate selection,
//! - Unity C++ `Quaternion.FromToRotation` (Moller-Hughes matrix +
//!   MatrixToQuaternion) and `Quaternion.Lerp` (clamp01, dot-negate,
//!   normalize) reimplemented from the Unity 5.x reference algorithms.
//! The caller passes MonoMethod* handles for the Transform icalls and the
//! managed objects' MonoObject* pointers; `mu3_swing_solve_direct` walks the
//! joints in list order, reading and writing the transforms through the
//! engine's own registered icall implementations, resolved at runtime via
//! mono_lookup_internal_call (see the direct-mode section below).

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Vec3 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Quat {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub w: f32,
}

/// Matches MU3.SwingJointColliderInformation (Pack = 4): Vector3 + float.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct ColliderInfo {
    pub position: Vec3,
    pub radius: f32,
}

/// Per-joint solve input, all fields f32. Internal to the solver: filled
/// from native transform reads by `mu3_swing_solve_direct`.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct JointIn {
    /// _transform.rotation before the localRotation reset.
    pub pre_rot: Quat,
    /// _transform.position (unaffected by the reset).
    pub pos: Vec3,
    /// _transform.rotation after localRotation = _initialLocalRotation.
    pub rot2: Quat,
    /// _childNode.lossyScale.x
    pub child_lossy_x: f32,
    /// collisionRadius property result: base radius * joint lossyScale.x
    pub eff_radius: f32,
    pub node_axis: Vec3,
    pub node_length: f32,
    pub child_pull: f32,
    pub inertial: f32,
    pub static_force: Vec3,
    pub physics_ratio: f32,
    pub now_child: Vec3,
    pub old_child: Vec3,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct JointOut {
    /// Final world rotation to write back.
    pub rot: Quat,
    /// New _nowChildPos (the previous one becomes _oldChildPos).
    pub now_child: Vec3,
}

// ---------------------------------------------------------------------------
// Vector helpers (componentwise, same op order as the C# source)
// ---------------------------------------------------------------------------

#[inline(always)]
fn v_add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3 { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z }
}

#[inline(always)]
fn v_sub(a: Vec3, b: Vec3) -> Vec3 {
    Vec3 { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z }
}

#[inline(always)]
fn v_scale(a: Vec3, s: f32) -> Vec3 {
    Vec3 { x: a.x * s, y: a.y * s, z: a.z * s }
}

#[inline(always)]
fn v_dot(a: Vec3, b: Vec3) -> f32 {
    a.x * b.x + a.y * b.y + a.z * b.z
}

#[inline(always)]
fn v_cross(a: Vec3, b: Vec3) -> Vec3 {
    Vec3 {
        x: a.y * b.z - a.z * b.y,
        y: a.z * b.x - a.x * b.z,
        z: a.x * b.y - a.y * b.x,
    }
}

/// Vector3.sqrMagnitude: x*x + y*y + z*z (left-associated adds).
#[inline(always)]
fn v_sqr_magnitude(a: Vec3) -> f32 {
    a.x * a.x + a.y * a.y + a.z * a.z
}

/// MU3.CustomMath.normalize: zero vector below 1e-6 magnitude.
#[inline(always)]
fn custom_normalize(v: Vec3) -> Vec3 {
    let num = (v.x * v.x + v.y * v.y + v.z * v.z).sqrt();
    if num <= 1e-6f32 {
        return Vec3 { x: 0.0, y: 0.0, z: 0.0 };
    }
    let inv = 1.0f32 / num;
    v_scale(v, inv)
}

// ---------------------------------------------------------------------------
// Quaternion helpers
// ---------------------------------------------------------------------------

/// UnityEngine managed operator*(Quaternion, Quaternion), exact op order.
#[inline(always)]
fn q_mul(lhs: Quat, rhs: Quat) -> Quat {
    Quat {
        x: lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
        y: lhs.w * rhs.y + lhs.y * rhs.w + lhs.z * rhs.x - lhs.x * rhs.z,
        z: lhs.w * rhs.z + lhs.z * rhs.w + lhs.x * rhs.y - lhs.y * rhs.x,
        w: lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z,
    }
}

/// UnityEngine managed operator*(Quaternion, Vector3), exact op order.
#[inline(always)]
fn q_mul_vec(rotation: Quat, point: Vec3) -> Vec3 {
    let num = rotation.x * 2.0f32;
    let num2 = rotation.y * 2.0f32;
    let num3 = rotation.z * 2.0f32;
    let num4 = rotation.x * num;
    let num5 = rotation.y * num2;
    let num6 = rotation.z * num3;
    let num7 = rotation.x * num2;
    let num8 = rotation.x * num3;
    let num9 = rotation.y * num3;
    let num10 = rotation.w * num;
    let num11 = rotation.w * num2;
    let num12 = rotation.w * num3;
    Vec3 {
        x: (1.0f32 - (num5 + num6)) * point.x + (num7 - num12) * point.y + (num8 + num11) * point.z,
        y: (num7 + num12) * point.x + (1.0f32 - (num4 + num6)) * point.y + (num9 - num10) * point.z,
        z: (num8 - num11) * point.x + (num9 + num10) * point.y + (1.0f32 - (num4 + num5)) * point.z,
    }
}

#[inline(always)]
fn q_dot(a: Quat, b: Quat) -> f32 {
    a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w
}

#[inline(always)]
fn q_normalize(q: Quat) -> Quat {
    let mag = q_dot(q, q).sqrt();
    if mag < 1e-6f32 {
        return Quat { x: 0.0, y: 0.0, z: 0.0, w: 1.0 };
    }
    let inv = 1.0f32 / mag;
    Quat { x: q.x * inv, y: q.y * inv, z: q.z * inv, w: q.w * inv }
}

/// Unity C++ Quaternion Lerp: clamp01(t), dot-negate, componentwise, normalize.
#[inline(always)]
fn q_lerp(q1: Quat, q2: Quat, t: f32) -> Quat {
    let t = if t < 0.0f32 { 0.0f32 } else if t > 1.0f32 { 1.0f32 } else { t };
    let tmp = if q_dot(q1, q2) < 0.0f32 {
        Quat {
            x: q1.x + t * (-q2.x - q1.x),
            y: q1.y + t * (-q2.y - q1.y),
            z: q1.z + t * (-q2.z - q1.z),
            w: q1.w + t * (-q2.w - q1.w),
        }
    } else {
        Quat {
            x: q1.x + t * (q2.x - q1.x),
            y: q1.y + t * (q2.y - q1.y),
            z: q1.z + t * (q2.z - q1.z),
            w: q1.w + t * (q2.w - q1.w),
        }
    };
    q_normalize(tmp)
}

/// Unity Matrix3x3 -> Quaternion conversion (Ken Shoemake / Unity order).
#[inline]
fn matrix_to_quaternion(m: &[[f32; 3]; 3]) -> Quat {
    let trace = m[0][0] + m[1][1] + m[2][2];
    let mut q = Quat { x: 0.0, y: 0.0, z: 0.0, w: 1.0 };
    if trace > 0.0f32 {
        let mut root = (trace + 1.0f32).sqrt();
        q.w = 0.5f32 * root;
        root = 0.5f32 / root;
        q.x = (m[2][1] - m[1][2]) * root;
        q.y = (m[0][2] - m[2][0]) * root;
        q.z = (m[1][0] - m[0][1]) * root;
    } else {
        const NEXT: [usize; 3] = [1, 2, 0];
        let mut i = 0usize;
        if m[1][1] > m[0][0] {
            i = 1;
        }
        if m[2][2] > m[i][i] {
            i = 2;
        }
        let j = NEXT[i];
        let k = NEXT[j];
        let mut root = (m[i][i] - m[j][j] - m[k][k] + 1.0f32).sqrt();
        let mut xyz = [0.0f32; 3];
        xyz[i] = 0.5f32 * root;
        root = 0.5f32 / root;
        q.w = (m[k][j] - m[j][k]) * root;
        xyz[j] = (m[j][i] + m[i][j]) * root;
        xyz[k] = (m[k][i] + m[i][k]) * root;
        q.x = xyz[0];
        q.y = xyz[1];
        q.z = xyz[2];
    }
    q
}

/// Moller-Hughes "from-to" rotation matrix, Unity 5.x variant, for unit vectors.
#[inline]
fn from_to_matrix(from: Vec3, to: Vec3) -> [[f32; 3]; 3] {
    const EPSILON: f32 = 0.000001f32;
    let v = v_cross(from, to);
    let e = v_dot(from, to);
    if e > 1.0f32 - EPSILON {
        // Identity.
        [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
    } else if e < -1.0f32 + EPSILON {
        // from == -to: build any orthonormal frame around `from`.
        let mut left = Vec3 { x: 0.0, y: from.z, z: -from.y };
        if v_dot(left, left) < EPSILON {
            left = Vec3 { x: -from.z, y: 0.0, z: from.x };
        }
        let invlen = 1.0f32 / v_dot(left, left).sqrt();
        left = v_scale(left, invlen);
        let up = v_cross(left, from);
        let fxx = -from.x * from.x;
        let fyy = -from.y * from.y;
        let fzz = -from.z * from.z;
        let fxy = -from.x * from.y;
        let fxz = -from.x * from.z;
        let fyz = -from.y * from.z;
        let uxx = up.x * up.x;
        let uyy = up.y * up.y;
        let uzz = up.z * up.z;
        let uxy = up.x * up.y;
        let uxz = up.x * up.z;
        let uyz = up.y * up.z;
        let lxx = -left.x * left.x;
        let lyy = -left.y * left.y;
        let lzz = -left.z * left.z;
        let lxy = -left.x * left.y;
        let lxz = -left.x * left.z;
        let lyz = -left.y * left.z;
        [
            [fxx + uxx + lxx, fxy + uxy + lxy, fxz + uxz + lxz],
            [fxy + uxy + lxy, fyy + uyy + lyy, fyz + uyz + lyz],
            [fxz + uxz + lxz, fyz + uyz + lyz, fzz + uzz + lzz],
        ]
    } else {
        let h = 1.0f32 / (1.0f32 + e);
        let hvx = h * v.x;
        let hvz = h * v.z;
        let hvxy = hvx * v.y;
        let hvxz = hvx * v.z;
        let hvyz = hvz * v.y;
        [
            [e + hvx * v.x, hvxy - v.z, hvxz + v.y],
            [hvxy + v.z, e + h * v.y * v.y, hvyz - v.x],
            [hvxz - v.y, hvyz + v.x, e + hvz * v.z],
        ]
    }
}

/// Unity Quaternion.FromToRotation with the "safe" magnitude guards
/// (Vector3 epsilon = 1e-5): identity for degenerate inputs, otherwise
/// normalize both vectors and convert the from-to matrix to a quaternion.
#[inline]
fn q_from_to_rotation(from: Vec3, to: Vec3) -> Quat {
    const VECTOR3_EPSILON: f32 = 0.00001f32;
    let from_mag = v_dot(from, from).sqrt();
    let to_mag = v_dot(to, to).sqrt();
    if from_mag < VECTOR3_EPSILON || to_mag < VECTOR3_EPSILON {
        return Quat { x: 0.0, y: 0.0, z: 0.0, w: 1.0 };
    }
    let from_n = v_scale(from, 1.0f32 / from_mag);
    let to_n = v_scale(to, 1.0f32 / to_mag);
    let m = from_to_matrix(from_n, to_n);
    matrix_to_quaternion(&m)
}

// ---------------------------------------------------------------------------
// KD partition + collision (MU3.SimpleKDTree / SwingJointColliderInformation)
// ---------------------------------------------------------------------------

/// Plane.dot: nx*x + ny*y + nz*z + d (left-associated adds).
#[inline(always)]
fn plane_dot(plane: &[f32; 4], p: Vec3) -> f32 {
    plane[0] * p.x + plane[1] * p.y + plane[2] * p.z + plane[3]
}

/// SwingJointColliderInformation.getSideOfPlane.
#[inline(always)]
fn side_of_plane(plane: &[f32; 4], c: &ColliderInfo) -> i32 {
    let num = plane_dot(plane, c.position);
    if 0.0f32 <= num {
        if num <= c.radius { 1 } else { 2 }
    } else if -c.radius <= num {
        1
    } else {
        0
    }
}

/// getCollisionPointWithSphere; collision and center alias in the caller,
/// so read the center first and return the new position on hit.
#[inline(always)]
fn collide_sphere(c: &ColliderInfo, center: Vec3, radius: f32) -> Option<Vec3> {
    let mut vector = v_sub(center, c.position);
    let num = c.radius + radius;
    let sqr = v_sqr_magnitude(vector);
    if sqr >= num * num {
        return None;
    }
    if 1e-5f32 < sqr {
        vector = v_scale(vector, num / sqr.sqrt());
    } else {
        vector = v_scale(vector, num);
    }
    Some(v_add(c.position, vector))
}

// ---------------------------------------------------------------------------
// Solver
// ---------------------------------------------------------------------------

/// calcNormalizedChildPosition: position + normalize(childPos - position)
/// * nodeLength * childLossyScale (left-to-right scalar multiplies).
#[inline(always)]
fn calc_normalized_child(child_pos: Vec3, position: Vec3, node_length: f32, child_lossy: f32) -> Vec3 {
    let v = custom_normalize(v_sub(child_pos, position));
    v_add(position, v_scale(v_scale(v, node_length), child_lossy))
}

#[inline]
fn solve_one(joint: &JointIn, colliders: &[ColliderInfo], plane: &[f32; 4], right: &[u32], left: &[u32]) -> JointOut {
    // vector = rotation2 * nodeAxis * childPullForce + staticForce
    let mut vector = v_add(
        v_scale(q_mul_vec(joint.rot2, joint.node_axis), joint.child_pull),
        joint.static_force,
    );
    // vector *= childNode.lossyScale.x
    vector = v_scale(vector, joint.child_lossy_x);
    // vector += (old - now) * inertialForce
    vector = v_add(vector, v_scale(v_sub(joint.old_child, joint.now_child), joint.inertial));
    // childPos = 2*now - old + vector
    let mut child_pos = v_add(v_sub(v_scale(joint.now_child, 2.0f32), joint.old_child), vector);
    child_pos = calc_normalized_child(child_pos, joint.pos, joint.node_length, joint.child_lossy_x);

    // Candidate selection identical to SimpleKDTree.getCollideCandidates,
    // evaluated once on the pre-collision childPos.
    let num = plane_dot(plane, child_pos);
    let side = if !(0.0f32 <= num) {
        if -joint.eff_radius <= num { 1 } else { 0 }
    } else if num <= joint.eff_radius {
        1
    } else {
        2
    };
    let run_candidates = |indices: &[u32], child_pos: &mut Vec3| {
        for &i in indices {
            let c = &colliders[i as usize];
            if let Some(hit) = collide_sphere(c, *child_pos, joint.eff_radius) {
                *child_pos = calc_normalized_child(hit, joint.pos, joint.node_length, joint.child_lossy_x);
            }
        }
    };
    match side {
        0 => run_candidates(left, &mut child_pos),
        2 => run_candidates(right, &mut child_pos),
        _ => {
            // Whole array in original order.
            for c in colliders {
                if let Some(hit) = collide_sphere(c, child_pos, joint.eff_radius) {
                    child_pos = calc_normalized_child(hit, joint.pos, joint.node_length, joint.child_lossy_x);
                }
            }
        }
    }

    // fromDirection = TransformDirection(nodeAxis) == rot2 * nodeAxis.
    let from_direction = q_mul_vec(joint.rot2, joint.node_axis);
    let q = q_from_to_rotation(from_direction, v_sub(child_pos, joint.pos));
    let rot = q_lerp(joint.pre_rot, q_mul(q, joint.rot2), joint.physics_ratio);

    JointOut { rot, now_child: child_pos }
}

/// Replicate SimpleKDTree.update partition: stable traversal order.
fn partition(colliders: &[ColliderInfo], plane: &[f32; 4]) -> (Vec<u32>, Vec<u32>) {
    let mut right: Vec<u32> = Vec::with_capacity(colliders.len());
    let mut left: Vec<u32> = Vec::with_capacity(colliders.len());
    for (i, c) in colliders.iter().enumerate() {
        match side_of_plane(plane, c) {
            2 => right.push(i as u32),
            0 => left.push(i as u32),
            _ => {}
        }
    }
    (right, left)
}

// ---------------------------------------------------------------------------
// Direct native-transform mode
// ---------------------------------------------------------------------------
//
// The managed side passes MonoMethod* handles (MethodInfo.MethodHandle) for
// the five UnityEngine.Transform icalls; mu3_swing_native_init resolves the
// registered icall implementations through mono_lookup_internal_call from
// the in-process mono.dll. Each implementation receives the managed this
// (MonoObject*), null-checks its m_CachedPtr field and runs the engine
// member function - byte-for-byte what the managed Transform properties do,
// including dirty marking and change dispatch inside the engine setters.
// No PDB, RVA, or player-build pinning: the lookup works on any x64
// Mono-based Unity player registering these icalls.
//
//   INTERNAL_get_position       (this, &Vector3f out)
//   INTERNAL_get_rotation       (this, &Quaternionf out)
//   INTERNAL_get_lossyScale     (this, &Vector3f out)
//   INTERNAL_set_localRotation  (this, &Quaternionf)
//   INTERNAL_set_rotation       (this, &Quaternionf)
//
// A destroyed UnityEngine.Object keeps its MonoObject alive but zeroes
// m_CachedPtr; the icall would then raise a managed NullReferenceException,
// which must never unwind through Rust frames. Every entry point therefore
// re-checks the field before calling.

use core::ffi::c_void;

/// MonoObject prefix on x64: vtable* + synchronisation* = 0x10, followed by
/// UnityEngine.Object.m_CachedPtr as the first managed field.
/// `mu3_swing_verify_transform` checks this offset against a managed-read
/// m_CachedPtr value before the direct mode is enabled.
const MONO_OBJECT_CACHED_PTR_OFFSET: usize = 0x10;

#[inline(always)]
unsafe fn cached_native_ptr(obj: *mut c_void) -> usize {
    core::ptr::read_unaligned((obj as *const u8).add(MONO_OBJECT_CACHED_PTR_OFFSET) as *const usize)
}

type GetVec3Fn = unsafe extern "C" fn(*mut c_void, *mut Vec3) -> *mut Vec3;
type GetQuatFn = unsafe extern "C" fn(*mut c_void, *mut Quat) -> *mut Quat;
type SetQuatFn = unsafe extern "C" fn(*mut c_void, *const Quat);

#[derive(Clone, Copy)]
struct NativeFns {
    get_position: GetVec3Fn,
    get_rotation: GetQuatFn,
    get_lossy_scale: GetVec3Fn,
    set_local_rotation: SetQuatFn,
    set_rotation: SetQuatFn,
}

static NATIVE_FNS: std::sync::OnceLock<NativeFns> = std::sync::OnceLock::new();

/// Per-joint input for the direct mode: MonoObject* of the joint's
/// _transform / _childNode plus the managed-field parameters (manager
/// overwrites already applied by C#). Layout mirrored by the C# JointRef
/// struct (Pack = 8, pointers first).
#[repr(C)]
pub struct JointRef {
    pub transform: *mut c_void,
    pub child: *mut c_void,
    pub init_local_rot: Quat,
    pub node_axis: Vec3,
    pub node_length: f32,
    /// collisionRadius after manager overwrite, before the lossyScale.x mult.
    pub radius_base: f32,
    pub child_pull: f32,
    pub inertial: f32,
    pub static_force: Vec3,
    pub physics_ratio: f32,
    /// In-out: _nowChildPos / _oldChildPos verlet state.
    pub now_child: Vec3,
    pub old_child: Vec3,
}

/// Resolve the five Transform icall implementations by passing the given
/// MonoMethod* handles to mono_lookup_internal_call from the in-process
/// mono.dll. 0 ok; -10 mono.dll not loaded; -11 export missing;
/// -12 an icall resolved to null; -14 null MonoMethod* argument;
/// -30 non-Windows build.
#[no_mangle]
pub unsafe extern "C" fn mu3_swing_native_init(
    get_position: *mut c_void,
    get_rotation: *mut c_void,
    get_lossy_scale: *mut c_void,
    set_local_rotation: *mut c_void,
    set_rotation: *mut c_void,
) -> i32 {
    if NATIVE_FNS.get().is_some() {
        return 0;
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (get_position, get_rotation, get_lossy_scale, set_local_rotation, set_rotation);
        return -30;
    }
    #[cfg(target_os = "windows")]
    {
        if get_position.is_null()
            || get_rotation.is_null()
            || get_lossy_scale.is_null()
            || set_local_rotation.is_null()
            || set_rotation.is_null()
        {
            return -14;
        }
        #[link(name = "kernel32")]
        extern "system" {
            fn GetModuleHandleW(name: *const u16) -> *mut c_void;
            fn GetProcAddress(module: *mut c_void, name: *const u8) -> *mut c_void;
        }
        // "mono.dll", NUL-terminated UTF-16.
        let mono_name: [u16; 9] =
            [0x6d, 0x6f, 0x6e, 0x6f, 0x2e, 0x64, 0x6c, 0x6c, 0];
        let mono = GetModuleHandleW(mono_name.as_ptr());
        if mono.is_null() {
            return -10;
        }
        let lookup = GetProcAddress(mono, b"mono_lookup_internal_call\0".as_ptr());
        if lookup.is_null() {
            return -11;
        }
        let lookup: unsafe extern "C" fn(*mut c_void) -> *mut c_void =
            core::mem::transmute(lookup);
        let get_position = lookup(get_position);
        let get_rotation = lookup(get_rotation);
        let get_lossy_scale = lookup(get_lossy_scale);
        let set_local_rotation = lookup(set_local_rotation);
        let set_rotation = lookup(set_rotation);
        if get_position.is_null()
            || get_rotation.is_null()
            || get_lossy_scale.is_null()
            || set_local_rotation.is_null()
            || set_rotation.is_null()
        {
            return -12;
        }
        let _ = NATIVE_FNS.set(NativeFns {
            get_position: core::mem::transmute(get_position),
            get_rotation: core::mem::transmute(get_rotation),
            get_lossy_scale: core::mem::transmute(get_lossy_scale),
            set_local_rotation: core::mem::transmute(set_local_rotation),
            set_rotation: core::mem::transmute(set_rotation),
        });
        0
    }
}

/// Bitwise read/write verification against values the caller read through the
/// managed properties in the same frame. 0 ok; -20 not initialized;
/// -21 null argument; -26 the MonoObject's m_CachedPtr field does not
/// match the managed-read value (layout assumption failed);
/// -22/-23/-24 position/rotation/lossyScale mismatch.
/// On success `post_rot` holds the native re-read after SetRotation with
/// the just-read rotation; the caller compares it with a managed re-read.
#[no_mangle]
pub unsafe extern "C" fn mu3_swing_verify_transform(
    transform: *mut c_void,
    expect_cached_ptr: usize,
    expect_pos: *const Vec3,
    expect_rot: *const Quat,
    expect_lossy: *const Vec3,
    post_rot: *mut Quat,
) -> i32 {
    let fns = match NATIVE_FNS.get() {
        Some(f) => *f,
        None => return -20,
    };
    if transform.is_null()
        || expect_cached_ptr == 0
        || expect_pos.is_null()
        || expect_rot.is_null()
        || expect_lossy.is_null()
        || post_rot.is_null()
    {
        return -21;
    }
    if cached_native_ptr(transform) != expect_cached_ptr {
        return -26;
    }
    let bits3 = |v: Vec3| [v.x.to_bits(), v.y.to_bits(), v.z.to_bits()];
    let bits4 = |q: Quat| [q.x.to_bits(), q.y.to_bits(), q.z.to_bits(), q.w.to_bits()];

    let mut pos = Vec3 { x: 0.0, y: 0.0, z: 0.0 };
    (fns.get_position)(transform, &mut pos);
    if bits3(pos) != bits3(*expect_pos) {
        return -22;
    }
    let mut rot = Quat { x: 0.0, y: 0.0, z: 0.0, w: 0.0 };
    (fns.get_rotation)(transform, &mut rot);
    if bits4(rot) != bits4(*expect_rot) {
        return -23;
    }
    let mut lossy = Vec3 { x: 0.0, y: 0.0, z: 0.0 };
    (fns.get_lossy_scale)(transform, &mut lossy);
    if bits3(lossy) != bits3(*expect_lossy) {
        return -24;
    }
    (fns.set_rotation)(transform, &rot);
    (fns.get_rotation)(transform, post_rot);
    0
}

/// Solve every joint sequentially in list order, reading and writing the
/// transforms through the engine's registered icall implementations. This
/// matches the original per-joint `update()` loop exactly: a joint's reads
/// see every earlier joint's writes, like the managed code.
///
/// Returns 0 on success, -1/-2/-3 on invalid arguments,
/// -20 when mu3_swing_native_init has not succeeded.
#[no_mangle]
pub unsafe extern "C" fn mu3_swing_solve_direct(
    refs: *mut JointRef,
    njoints: i32,
    colliders: *const ColliderInfo,
    ncolliders: i32,
    plane: *const f32,
) -> i32 {
    let fns = match NATIVE_FNS.get() {
        Some(f) => *f,
        None => return -20,
    };
    if njoints < 0 || ncolliders < 0 {
        return -1;
    }
    if (njoints > 0 && refs.is_null()) || plane.is_null() {
        return -2;
    }
    if ncolliders > 0 && colliders.is_null() {
        return -3;
    }
    let refs = core::slice::from_raw_parts_mut(refs, njoints as usize);
    let colliders = if ncolliders > 0 {
        core::slice::from_raw_parts(colliders, ncolliders as usize)
    } else {
        &[]
    };
    let plane = &*(plane as *const [f32; 4]);

    let (right, left) = partition(colliders, plane);

    for r in refs {
        if r.transform.is_null() || r.child.is_null() {
            continue;
        }
        // Destroyed native side (m_CachedPtr zeroed): the icall would raise
        // a managed exception; skip like the managed null check.
        if cached_native_ptr(r.transform) == 0 || cached_native_ptr(r.child) == 0 {
            continue;
        }
        // Exact transform access order of the original update():
        // rotation, localRotation reset, position, rotation, child
        // lossyScale, own lossyScale.
        let mut pre_rot = Quat { x: 0.0, y: 0.0, z: 0.0, w: 0.0 };
        (fns.get_rotation)(r.transform, &mut pre_rot);
        (fns.set_local_rotation)(r.transform, &r.init_local_rot);
        let mut pos = Vec3 { x: 0.0, y: 0.0, z: 0.0 };
        (fns.get_position)(r.transform, &mut pos);
        let mut rot2 = Quat { x: 0.0, y: 0.0, z: 0.0, w: 0.0 };
        (fns.get_rotation)(r.transform, &mut rot2);
        let mut child_lossy = Vec3 { x: 0.0, y: 0.0, z: 0.0 };
        (fns.get_lossy_scale)(r.child, &mut child_lossy);
        let mut own_lossy = Vec3 { x: 0.0, y: 0.0, z: 0.0 };
        (fns.get_lossy_scale)(r.transform, &mut own_lossy);

        let joint = JointIn {
            pre_rot,
            pos,
            rot2,
            child_lossy_x: child_lossy.x,
            eff_radius: r.radius_base * own_lossy.x,
            node_axis: r.node_axis,
            node_length: r.node_length,
            child_pull: r.child_pull,
            inertial: r.inertial,
            static_force: r.static_force,
            physics_ratio: r.physics_ratio,
            now_child: r.now_child,
            old_child: r.old_child,
        };
        let o = solve_one(&joint, colliders, plane, &right, &left);
        (fns.set_rotation)(r.transform, &o.rot);
        r.old_child = r.now_child;
        r.now_child = o.now_child;
    }
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unchanged_direction_preserves_the_joint_orientation() {
        let v = Vec3 { x: 0.0, y: 1.0, z: 0.0 };
        let q = q_from_to_rotation(v, v);
        let probe = Vec3 { x: 1.0, y: 2.0, z: 3.0 };
        let rotated = q_mul_vec(q, probe);
        assert!((rotated.x - probe.x).abs() < 1e-6);
        assert!((rotated.y - probe.y).abs() < 1e-6);
        assert!((rotated.z - probe.z).abs() < 1e-6);
    }

    #[test]
    fn perpendicular_from_to_rotates() {
        let from = Vec3 { x: 1.0, y: 0.0, z: 0.0 };
        let to = Vec3 { x: 0.0, y: 1.0, z: 0.0 };
        let q = q_from_to_rotation(from, to);
        let rotated = q_mul_vec(q, from);
        assert!((rotated.x - to.x).abs() < 1e-5);
        assert!((rotated.y - to.y).abs() < 1e-5);
        assert!((rotated.z - to.z).abs() < 1e-5);
    }

    #[test]
    fn collision_pushes_out() {
        let c = ColliderInfo { position: Vec3 { x: 0.0, y: 0.0, z: 0.0 }, radius: 1.0 };
        let hit = collide_sphere(&c, Vec3 { x: 0.5, y: 0.0, z: 0.0 }, 0.1).unwrap();
        assert!((hit.x - 1.1).abs() < 1e-6);
        assert!(hit.y.abs() < 1e-6);
        assert!(hit.z.abs() < 1e-6);
    }
}
