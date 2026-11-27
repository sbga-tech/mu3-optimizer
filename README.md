# MU3 Optimizer

MU3 Optimizer is a set of MonoMod patches for MU3 sddt160. The patches cut down the rendering and CPU work the game does during play, avoid a lot of throwaway memory allocations, and shorten the wait at login. Each patch has its own switch in `mu3.ini`, so you can turn off anything you don't want.

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

2. Put `mu3.ini` in the game's working directory, which is normally `package/`. If you keep it somewhere else, point `MU3_CONFIG_PATH` at it. The older `MU3_MODS_CONFIG_PATH` still works when `MU3_CONFIG_PATH` isn't set.
3. Start the game.

MonoMod reads `mu3.ini` when it patches the game at startup, so restart the game after you change a setting.

When at least one patch is enabled, the game log shows `[Steroid] loaded.` shortly after startup. Messages from individual patches start with `[Steroid][<Patch>]`. A warning means the patch hit something it couldn't handle and fell back to the game's normal behavior. An error means something failed outright. With every patch disabled, the mod doesn't change how the game behaves and doesn't log anything. It never hides messages from the game or BepInEx.

## Configuration

Each switch under `[Optimization]` turns one patch on (`1`) or off (`0`). Every switch is on by default. Some patches have extra settings in their own `[Optimization.<PatchName>]` section. The example below matches the defaults.

```ini
[Optimization]
ImageBloom=1
RenderLayers=1
SwingJointPhysics=1
InactiveMirrorIndicator=1
NoUICameraDuringPlay=1
GpuTextScroll=1
LoginRequestsBatching=1
AsyncLoginRequests=1
NoteBatching=1
PrimitiveMeshEmission=1
GameplayPrewarm=1
UVAnimation=1
CollabSocketCaching=1
CollabHeartbeatCaching=1
CollabMemberListCompaction=1
InlinedAMDaemonCalls=1
UnityPlayerHooks=1

[Optimization.ImageBloom]
; Most glow redraws per second; must be above 0.
BloomFPS=60
; Removes the glow completely; BloomFPS then has no effect.
NoGlow=0

[Optimization.RenderLayers]
; How often each layer redraws during normal play:
;   negative = every frame
;   0        = only when forced, or when an earlier layer changes
;   positive = at most this many times per second
; The layers feed into each other (stage, then background merge, then FX), and
; redrawing one layer also redraws the layers after it. Give FXFPS the highest
; rate, then BGMergeFPS, then StageFPS. At 60, the cabinet's frame rate, nothing
; changes at 60 FPS, and faster displays skip the extra layer redraws. Lower
; values such as StageFPS=0, BGMergeFPS=0, FXFPS=30 save more work but make the
; background less smooth.
StageFPS=60
BGMergeFPS=60
FXFPS=60
; Turns off shadows after the opening cutscene.
DisableShadows=1
; Draws the FX gate texture only when the FX layer redraws; off by default.
ScheduleGateCamera=0
; During transition effects, redraws only the FX layer every frame while the
; camera stays still; off by default.
EventFXOnly=0

[Optimization.SwingJointPhysics]
; Most physics updates per second. 0 or below updates every rendered frame.
SwingJointFPS=60

[Optimization.UnityPlayerHooks]
GpuFenceWait=1
JobSignalBatching=1
; Turns on Unity graphics jobs after startup, with a fix for their crash.
GraphicsJobs=1
; Turns on Unity's GPU skinning for characters after startup; off by default.
GpuSkinning=0
; Lets the CPU start the next frame while the GPU finishes the last one, only
; while that adds no delay; needs GpuFenceWait. Off by default.
AdaptiveFrameQueue=0
```

## Rendering and UI patches

### `ImageBloom`

Bloom is the soft glow around bright objects. To make it, the game renders the battle scene a second time every frame, keeping only the bright parts, then blurs that image over several extra passes on the next frame.

With this patch the second render happens at most `BloomFPS` times a second (60 by default, the cabinet's frame rate). At 60 FPS or below nothing changes. On faster displays the glow is redrawn 60 times a second, as it is on the cabinet, while everything else still draws every frame. At the end of a battle, when the game reuses the finished frame, the scene is rendered every frame as before.

`NoGlow=1` removes the glow and its passes completely and keeps the rest of the image effect the camera needs. The trade-off is visible: bright objects no longer glow. It is off by default.

This switch replaces the old `NoImageBloom` setting, which no longer does anything. To keep removing the glow, use `ImageBloom=1` with `NoGlow=1`.

### `RenderLayers`

The game draws the stage, a merged background, and the background effects with separate cameras on every frame. This patch keeps the last image of each layer and redraws a layer only as often as its rate setting allows. The final picture on screen is still drawn every frame. By default each layer redraws at most 60 times a second, the cabinet's frame rate, so the picture doesn't change at 60 FPS and faster displays skip the extra redraws.

Every layer redraws each frame whenever the game needs it: at the start of a battle, during the opening cutscene, around damage, wave-change, and overkill effects, and whenever the game isn't in normal play. `DisableShadows=1`, which is on by default, also turns off shadows after the opening cutscene.

Rates below your frame rate can make animated backgrounds look less smooth, and turning off shadows changes the scene's lighting.

`ScheduleGateCamera=1` also limits a fourth camera. The battle stage has an extra camera that draws the gate particles into a small texture on every frame, and only the FX layer uses that texture. With this setting, that camera draws only on the frames where the FX layer redraws. When the FX layer redraws every frame, the picture is exactly the same. At lower FX rates, the gate texture the FX layer shows can be up to one FX redraw older than before. It helps most with `FXFPS=0` or other low FX rates.

`EventFXOnly=1` changes what happens during those transition effects. The wave-change, overkill, damage, and battle-start effects are drawn on the FX layer, and the camera doesn't move during them in normal play. With this setting, only the FX layer redraws every frame during an effect. The stage and merged background keep their normal rates, unless the camera moves, and every layer still redraws on the first and last frame of the effect. With `StageFPS=0`, the stage then stays as it was until the effect ends, instead of animating during it.

### `SwingJointPhysics`

Hair, costumes, and accessories sway using physics that runs once per rendered frame and doesn't account for frame time. On a display faster than the cabinet's 60 FPS, the sway moves too fast, and the game does more physics work than it needs to. This patch runs the physics at most `SwingJointFPS` times a second (60 by default), which brings back the intended motion and saves the extra work. It also skips joints on hidden objects and does less repeated work per joint.

### `InactiveMirrorIndicator`

With mirror mode off, the game still keeps the mirror indicator active and just shows a blank image, so the UI system keeps processing it. This patch switches the indicator off instead. Nothing on screen changes.

### `NoUICameraDuringPlay`

The game draws its menus and play HUD with a separate UI camera. During a song, this patch draws those canvases directly on top of the screen, so the UI camera can be switched off, and it skips the UI system's per-frame update. It stays active from the moment a song is about to start until play ends. With `GameplayPrewarm`, that includes the time the music waits for preparation to finish. When a dialog appears, the normal UI camera comes back.

Unusual dialog or error sequences can make UI elements draw in the wrong order. Set `NoUICameraDuringPlay=0` if that happens.

### `GpuTextScroll`

Text that is too long for its box scrolls sideways, and the game rebuilds that text's shape on the CPU every frame to move it. This patch moves the scrolling and clipping into a Direct3D 11 shader, so the CPU only updates the scroll position.

Text the shader can't handle keeps using the normal path. That includes systems without Direct3D 11, dynamic fonts, glowing (emission) text, masked text, and uncommon materials or text effects.

## Login patches

### `LoginRequestsBatching`

At login the game downloads your characters, items, music, cards, music items, and rival music data in pages of 50, with one request per page. This patch asks for each list in a single request, which removes a lot of round trips for large accounts when the server allows it.

The server can still enforce its own page size, and bigger responses use more memory while they load.

### `AsyncLoginRequests`

After your basic user record arrives, the game fetches the rest of your data one request at a time and waits for each response before sending the next. This patch sends the requests that don't depend on each other at the same time and keeps the ones that do in their original order.

Everything still runs on the game's main thread. The patch only overlaps the waiting. A network error stops the group it happens in, and how much time you save depends on how many parallel requests the server accepts.

## Note and lane patches

### `NoteBatching`

Every note object normally ends up with its own private copy of its material, which stops the GPU from drawing similar notes together. This patch looks at each pooled note once, groups notes that use the same material and mesh, and gives each group one shared material. Notes in a group can then be drawn together with GPU instancing, and reusing a note from the pool no longer repeats the setup.

Notes with different shaders, meshes, or drawing order still need separate groups.

### `PrimitiveMeshEmission`

The game rebuilds the lane and hold-note meshes every frame by adding points one at a time, and then wipes its point lists. This patch collects the drawing commands for a frame and writes the whole mesh at once through a native helper bundled with the mod, and empties the lists without wiping memory that the next frame overwrites anyway. If the helper can't be used, it writes the mesh directly from managed code, and if that isn't possible either, it keeps the game's original method.

### `GameplayPrewarm`

The first time a song shows a judgement, updates the score, or plays a wave-change or overkill effect, Unity has to load and prepare everything involved, which can cause a stutter in the middle of play. This patch does that work before the song starts, and the music waits until it is finished. It renders throwaway copies of the judgement and score displays off screen, prepares the wave-change and overkill effects ahead of time and keeps them paused out of sight until the game plays them, and has the runtime compile the code those moments use.

The copies are separate from the real game objects, so the actual score and effects aren't touched. If the prepared effects run out, the game creates new ones as usual. If preparation is cancelled or fails, the patch cleans up after itself and the song doesn't start.

It replaces the old `JudgementPrewarm` setting, which no longer does anything.

### `UVAnimation`

Scrolling textures and sprite-sheet animations normally give every animated object its own copy of its material, which stops those objects from being drawn together. This patch animates them with per-object overrides instead, so they keep sharing the original material.

## Collaboration patches

These patches cover the collaboration network code, which keeps running during normal play, not only in party play. They reuse network objects instead of creating new ones for every packet and heartbeat. With them turned off, normal play has shown serious garbage-collection problems. The network protocol doesn't change.

### `CollabSocketCaching`

Reuses network sockets and addresses when the address and port haven't changed, instead of rebuilding them or creating new address objects for every packet. Reopening a connection that hasn't changed does nothing.

### `CollabHeartbeatCaching`

Reuses one heartbeat request and one heartbeat response instead of creating new ones for every heartbeat.

### `CollabMemberListCompaction`

Removes disconnected or expired party members from the member lists in place, in fewer passes and without temporary lists. Notifications about members leaving still happen as before.

## AMDaemon patch

### `InlinedAMDaemonCalls`

Every call the game makes into AMDaemon goes through a small wrapper that creates a new delegate object and adds an extra indirect call. This patch rewrites those calls to go straight to the native function. The same error check still runs after every call.

## Unity player patches

`UnityPlayerHooks` patches places inside Unity's own engine code, which a normal MonoMod patch can't reach. The hooks come from a native library embedded in the mod and are installed right after the game starts. Set `UnityPlayerHooks=0` to turn them all off, or turn each one off separately under `[Optimization.UnityPlayerHooks]`.

The hooks find their targets by scanning for known code patterns. `GpuFenceWait` and `JobSignalBatching` support both the regular and the development build of the Unity 5.6.4f1 x64 player; `GraphicsJobs` and `GpuSkinning` recognise only the regular build. If a hook can't find its target exactly, it is skipped with a warning in the log and Unity's original code runs.

### `GpuFenceWait`

When Unity waits for the GPU to finish a frame, it waits on a GPU query. This hook waits for a fence event from the graphics driver instead and then checks the original query, so Unity still gets the same results and handles errors the same way. It needs a GPU and driver with Direct3D 11 fence support. Without that, or if anything fails, Unity's normal wait is used.

In testing, the fence wait performed the same as waiting with a precise timer, and the precise timer was clearly faster than Unity's normal wait. The fence has not been compared directly with Unity's normal wait.

### `JobSignalBatching`

Unity wakes its worker threads when there is work for them. That code has a counting bug: when many workers are waiting, handing out a little work can wake all of them at once. This hook fixes the check so only as many workers wake as there is work for, and wakes several workers with one system call instead of one call each. The number of workers and the way jobs are handed out don't change.

If a combined wake-up fails, the hook falls back to waking the workers one at a time. It stays installed until the game exits, because Unity can still wake workers while it shuts down.

### `GraphicsJobs`

Graphics jobs build each camera pass's draw commands on worker threads instead of the main thread, and the render thread plays them back in the order they were scheduled. This turns them on without the `-force-gfx-jobs` launch option.

Unity only reads the launch option while it starts up, before the mod runs. So the hook instead changes the two engine flags that decide, every frame, whether camera passes are built by worker threads. It switches them at the point in the frame where Unity would have finished the previous frame's graphics jobs, so no frame is left half in one mode. The frames before the mod starts, during boot, render without graphics jobs. Once on, graphics jobs stay on until the game exits.

It also fixes a crash that Unity's graphics jobs have on their own. Whichever job finishes last deletes UI buffers that are no longer needed, and records those deletes in its own command list. When that job was scheduled earlier than another job that still draws the same buffers, the deletes are played back first and the later draws use freed memory. The game sometimes crashes when that happens. The hook moves those deletes to the main thread, which performs them after Unity has finished the previous frame's graphics jobs, so every draw that uses the buffers has already been queued. Graphics jobs are only switched on once this fix is installed; if it can't be, they stay off.

If you start the game with `-force-gfx-jobs`, keep `GraphicsJobs=1`, because it carries the crash fix.

### `GpuSkinning`

Characters bend their meshes to follow their skeletons every frame. The game ships with Unity's GPU skinning turned off, so worker threads do this on the CPU and upload the results. This turns GPU skinning on right after startup, so the graphics card does the bending instead.

Unity checks the setting every time it prepares a character mesh, so the change takes effect from the first battle. Meshes with active blend shapes, and graphics cards without the Direct3D 11 feature it needs, keep CPU skinning. In testing, characters, costumes, hair, and weapons looked the same, and scores were identical.

It is off by default.

### `AdaptiveFrameQueue`

The game tells Unity to finish every frame on the GPU before it starts preparing the next one. On a fast PC the GPU finishes its work early and then waits for the next frame, while the CPU waits for the GPU. With this setting Unity may prepare one frame ahead, so the two overlap and frames come out faster.

A frame prepared ahead only adds delay when the GPU can't keep up and frames pile up waiting for it. The setting watches how long Unity waits for the GPU. When the wait grows, it goes back to the game's behavior, and tries again a little later. With VSync on, it always keeps the game's behavior.

It needs `GpuFenceWait`. It is off by default.

## Building

Requirements:

- Docker
- `mu3-reference/` next to this repository, with its reference source projects and the Unity/.NET DLLs under `lib/`
- Unity credentials in `UNITY_EMAIL` and `UNITY_PASSWORD`
- Zig 0.16.0 for the `x86_64-windows-gnu` player hook, and Rust/Cargo with the `x86_64-pc-windows-gnu` target for the geometry and swing helpers
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
./build.sh check
```

`build-native` builds every project under `Native/` that has a `build.zig` or `Cargo.toml` and collects the Windows DLLs into `Native/Build`. `build-main` builds the reference projects and then the optimizer. The container needs write access to the reference projects' generated `obj/` and `artifacts/` directories. `patch` applies both optimizer modules to those build outputs and writes portable PDBs next to the patched assemblies. `check` runs the native unit tests and `build-main`, then patches the game once with every switch off, once per switch, and once with every switch on, and fails if a switch changes game code outside its own patches.

Outputs:

```text
bin/Release/net35/Assembly-CSharp.Steroid.mm.dll
bin/Release/net35/AMDaemon.NET.Steroid.mm.dll
bin/Release/net35/MONOMODDED_Assembly-CSharp.dll
bin/Release/net35/MONOMODDED_AMDaemon.NET.dll
```

Install only the two `.mm.dll` files if you are using BepInEx MonoMod loader. The `MONOMODDED_*` assemblies can be used on cab environment where MonoMod loader isn't applicable.

### Source layout

- `Assembly-CSharp/Startup.cs`: the `[Steroid] loaded.` startup hook shared by every switch.
- `Assembly-CSharp/MonoMod/StateHooks.cs`: `[OnStateEnter("<State>")]` and `[OnStateLeave("<State>")]` attach a switch-gated `static void (THolder)` method to a game state-machine transition. Patching inserts the call into the holder's final `Enter_<State>` or `Leave_<State>` method, and creates the method if it doesn't exist, so no patch has to own it.
- `Assembly-CSharp/Features/<Switch>/` and `AMDaemon.NET/Features/<Switch>/`: everything that belongs to one `[Optimization]` switch, in namespace `MU3.Mod.<Switch>`. MonoMod rules stay in the `MonoMod` namespace.
- `Assembly-CSharp/Runtime/`: embedded native-module and AssetBundle loading, and the frame-rate limiter, shared by several switches.
- `*/MonoMod/Rules.cs`: the MonoMod rules entry points. `Common/` holds the configuration and patch lifecycle code shared by both projects.
