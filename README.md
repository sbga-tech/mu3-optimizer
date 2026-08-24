# MU3 Optimizer

MU3 Optimizer is a set of MonoMod patches for MU3 sddt160. It reduces camera work, note and lane CPU work, material churn, transient allocations, and login wait time.

## Compatibility

- Game assembly: MU3 sddt160
- Runtime: .NET Framework 3.5
- Patch loader: MonoMod through BepInEx
- GPU text path: Windows Direct3D 11

## Installation

1. Copy these files to `package/BepInEx/monomod/`:

   ```text
   Assembly-CSharp.Steroid.mm.dll
   AMDaemon.NET.Steroid.mm.dll
   ```

2. Put `mu3.ini` in the game working directory, normally `package/`. You can instead set `MU3_MODS_CONFIG_PATH` to an explicit path.
3. Start the game.

Configuration is read while MonoMod generates the patched assemblies. Restart the game after changing `mu3.ini`.

## Configuration

Under `[Optimization]`, `1` enables a patch and `0` leaves that part of the game unchanged. Patch-specific settings use `[Optimization.<PatchName>]`.

```ini
[Optimization]
NoImageBloom=1
RenderLayers=1
SwingJointPhysics=1
InactiveMirrorIndicator=1
NoUICameraDuringPlay=1
GpuTextScroll=1
LoginRequestsBatching=1
AsyncLoginRequests=1
NoteBatching=1
ActiveNoteTraversal=1
LaneGeometryCulling=1
CachedNoteVisibility=1
PrimitiveMeshEmission=1
UVAnimation=1
CollabSocketCaching=1
CollabHeartbeatCaching=1
CollabMemberListCompaction=1
InlinedAMDaemonCalls=1

[Optimization.RenderLayers]
StageFPS=0
BGMergeFPS=0
FXFPS=30
DisableShadows=1

[Optimization.SwingJointPhysics]
SwingJointFPS=60
SwingJointNative=0
```

Set layer rates from highest to lowest: `FXFPS`, then `BGMergeFPS`, then `StageFPS`. With the example above, FX can update at 30 FPS while the other layers update only after a change.

- A negative layer FPS renders every frame.
- `0` updates only after a forced or upstream change.
- A positive value sets the maximum update rate.
- Setting `SwingJointFPS` to `0` or below solves swing joints every rendered frame.

## Rendering and UI patches

### `NoImageBloom`

Removes bloom extraction and blur while keeping the half-resolution copy and optional write-back used by the camera pipeline. With four bloom iterations, each camera run avoids one `RenderWithShader` pass, eight bloom blits, and four temporary render-texture acquire/release pairs.

Bright objects no longer bloom.

### `RenderLayers`

Retains the stage, background-merge, and background-FX results instead of rendering every layer every frame. Known cutscene and FX events still refresh the layers when needed.

With `StageFPS` and `BGMergeFPS` set to `0` and `FXFPS` set to `30`, a gameplay frame with no refresh skips the stage, stage-merge, and post-stage scene renders. The compositor still performs one final blit. A 60 FPS game skips 30 FX renders each second, while a 120 FPS game skips 90. `DisableShadows=1` also removes shadow-map, shadow-caster, and shadow-receiver work after the start cutscene.

Low layer rates can make animated backgrounds look less smooth. Disabling shadows changes scene lighting.

### `SwingJointPhysics`

Caps costume and accessory physics to `SwingJointFPS`, skips inactive joints, and avoids repeated managed property access. With `SwingJointFPS=60`, a game running at 120 FPS solves joints 60 times per second instead of 120. At 240 FPS, it still solves them 60 times instead of 240.

`SwingJointNative=1` replaces the managed per-joint solver with one native solve call per manager on each scheduled update, but only after a live transform check succeeds. If validation or solving fails, the managed solver remains active.

### `InactiveMirrorIndicator`

Turns off the mirror indicator object when mirror mode is disabled instead of keeping a blank sprite active. This removes one `Image` and its `BaseMeshEffect` from canvas traversal without changing the visible result.

### `NoUICameraDuringPlay`

Moves eligible SystemUI and BattleUI canvases to screen-space overlay during play. During normal gameplay, this disables one UI camera render per frame and skips the stock `SystemUI.execute` body. Dialogs restore the camera path.

This patch is experimental because unusual dialog or failure ordering can expose sorting problems.

### `GpuTextScroll`

Moves scrolling-text displacement and clipping to a Direct3D 11 shader. Once the scroll wait ends, eligible text no longer calls `SetVerticesDirty` or rebuilds its CPU mesh every rendered frame. The CPU still advances the timer and scroll offset, then sends three material updates to the shader.

Unsupported text uses the normal CPU path. This includes non-D3D11 graphics, dynamic fonts, incompatible materials, emission, `RectMask2D`, unsupported mesh modifiers, and missing shader support.

## Login patches

### `LoginRequestsBatching`

Raises the six user-data page limits from 50 to 1,073,741,823. For example, a dataset with 2,500 records normally needs 50 requests of 50 records. The patch asks for all 2,500 records at once, removing 49 round trips when the server accepts the larger count.

Card and character dictionaries keep normal initial capacities instead of reserving space for the request limit. A server may still enforce its own page size, and larger responses use more peak memory.

### `AsyncLoginRequests`

Starts independent user-data states as coroutines after the base user record is available. The 46 top-level states run as nine concurrent groups: 34 independent states and 12 states in five ordered dependency chains. The longest chain contains three states instead of sending all 46 states one after another.

This overlaps network waits on Unity's main thread; it does not create CPU worker threads. Any network error stops the group, and server concurrency limits determine the wall-clock gain.

## Note and lane patches

### `NoteBatching`

Scans each pooled note object once, groups compatible renderers by render queue, material, and mesh, then shares one material within each group. Later checkouts do not rescan child renderers or access `renderer.material`. This replaces up to one private material per pooled renderer with one material for each compatible group and allows those notes to use GPU instancing.

Separate shaders, meshes, and ordering requirements still need separate groups.

### `ActiveNoteTraversal`

Sorts note controls once by creation frame and keeps a forward spawn cursor, so spawn and end checks only visit controls in the current chart window. For example, with 10,000 chart controls and 200 in the current window, those checks visit about 400 controls per frame instead of about 20,000, avoiding roughly 19,600 visits. Active note updates and counts still process the active list.

The saving grows with chart length and note count.

### `LaneGeometryCulling`

Skips lanes outside the visible chart window, starts visible-lane shape searches at a forward cursor instead of index zero, and clips emitted geometry to the visible range. An invisible lane now enters `NotesLane.draw` zero times per frame instead of once, and each lane no longer runs `LaneSetParam.clear` once per frame.

### `CachedNoteVisibility`

Caches the active state of tap, foot, hold-end, and hold-effect objects so `GameObject.SetActive` runs only when visibility changes. When visibility stays the same, a tap goes from up to three calls per frame to zero, and a hold goes from up to four calls to zero.

### `PrimitiveMeshEmission`

Writes lane and hold geometry directly into preallocated `List<T>` backing arrays, avoiding 18 `List.Add` calls for each textured quad and 14 for each wall quad. It publishes the final list sizes once before mesh upload, skips all four uploads on empty frames, and clears an empty mesh only when it previously held vertices. Degenerate quads are discarded before emission.

If the runtime list layout is incompatible, the patch uses the normal `List.Add` path automatically.

### `UVAnimation`

Uses one reusable `MaterialPropertyBlock` for continuous UV scrolling and sprite-sheet offsets. Each component creates zero private materials instead of one per renderer, which keeps the base material shared and preserves batching.

## Collaboration patches

### `CollabSocketCaching`

Reuses sockets and endpoints when their address and port have not changed. Each UDP receive that reaches `ReceiveFrom` avoids one `IPEndPoint` allocation. Each broadcast send avoids one `IPEndPoint` and one four-byte address-array allocation, while connect setup reuses its endpoint and avoids the same array allocation. Reopening an unchanged UDP, broadcast, or listen endpoint becomes a no-op instead of rebuilding the socket.

IPv4 byte-order conversion also uses integer operations instead of temporary byte arrays. The network protocol is unchanged.

### `CollabHeartbeatCaching`

Reuses one heartbeat request and one heartbeat response object, removing one allocation for every heartbeat send and one for every reply.

### `CollabMemberListCompaction`

Removes expired or disconnected members in place. Recruit cleanup uses one reverse pass with no temporary collection instead of allocating an index list and making two passes. Setting-host cleanup uses one update-and-mark pass plus one `RemoveAll` pass instead of an update pass followed by two cleanup passes.

Disposal and state-change notifications are preserved.

## AMDaemon patch

### `InlinedAMDaemonCalls`

Rewrites 269 `Api.Call` and `Api.CallAction` sites into 267 shared helpers, with zero skipped sites. Each rewritten call avoids one delegate allocation and one indirect `Delegate.Invoke` dispatch while still calling the native entry point and running `Api.CheckException` in a `finally` block.

## Building

Requirements:

- Docker
- `mu3-reference/` beside this repository
- Unity credentials in `UNITY_EMAIL` and `UNITY_PASSWORD`
- Rust/Cargo with the `x86_64-pc-windows-gnu` target
- MinGW-w64 with `x86_64-w64-mingw32-gcc`

Build everything:

```sh
UNITY_EMAIL=... UNITY_PASSWORD=... ./build.sh all
```

Build individual stages:

```sh
UNITY_EMAIL=... UNITY_PASSWORD=... ./build.sh build-assets
./build.sh build-native
./build.sh build-main
./build.sh patch
```

Outputs:

```text
bin/Release/net35/Assembly-CSharp.Steroid.mm.dll
bin/Release/net35/AMDaemon.NET.Steroid.mm.dll
bin/Release/net35/MONOMODDED_Assembly-CSharp.dll
bin/Release/net35/MONOMODDED_AMDaemon.NET.dll
```
