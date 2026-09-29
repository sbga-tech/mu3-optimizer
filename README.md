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

2. Put `mu3.ini` in the game's working directory, which is normally `package/`. If you keep it somewhere else, point `MU3_MODS_CONFIG_PATH` at it.
3. Start the game.

MonoMod reads `mu3.ini` when it patches the game at startup, so restart the game after you change a setting.

When at least one patch is enabled, the game log shows `[Steroid] loaded.` shortly after startup. Messages from individual patches start with `[Steroid][<Patch>]`. A warning means the patch hit something it couldn't handle and fell back to the game's normal behavior. An error means something failed outright. With every patch disabled, the mod doesn't change how the game behaves and doesn't log anything. It never hides messages from the game or BepInEx.

## Configuration

Each switch under `[Optimization]` turns one patch on (`1`) or off (`0`). A switch you leave out uses its default: `NoImageBloom`, `NoUICameraDuringPlay`, and `PrimitiveMeshEmission` are off by default, and everything else is on. Some patches have extra settings in their own `[Optimization.<PatchName>]` section. The example below matches the defaults.

```ini
[Optimization]
NoImageBloom=0
RenderLayers=1
SwingJointPhysics=1
InactiveMirrorIndicator=1
NoUICameraDuringPlay=0
GpuTextScroll=1
LoginRequestsBatching=1
AsyncLoginRequests=1
NoteBatching=1
PrimitiveMeshEmission=0
GameplayPrewarm=1
UVAnimation=1
CollabSocketCaching=1
CollabHeartbeatCaching=1
CollabMemberListCompaction=1
InlinedAMDaemonCalls=1
UnityPlayerHooks=1

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

[Optimization.SwingJointPhysics]
; Most physics updates per second. 0 or below updates every rendered frame.
SwingJointFPS=60
SwingJointNative=0

[Optimization.UnityPlayerHooks]
GpuFenceWait=1
JobSignalBatching=1
```

## Rendering and UI patches

### `NoImageBloom`

Bloom, the soft glow around bright objects, takes several extra rendering passes and temporary textures every time the camera renders. This patch removes the glow and keeps the rest of the image effect the camera needs. The trade-off is visible: bright objects no longer glow.

It is off by default because it changes how the game looks. It is most likely to help when the GPU is the bottleneck.

### `RenderLayers`

The game draws the stage, a merged background, and the background effects with separate cameras on every frame. This patch keeps the last image of each layer and redraws a layer only as often as its rate setting allows. The final picture on screen is still drawn every frame. By default each layer redraws at most 60 times a second, the cabinet's frame rate, so the picture doesn't change at 60 FPS and faster displays skip the extra redraws.

Every layer redraws each frame whenever the game needs it: at the start of a battle, during the opening cutscene, around damage, wave-change, and overkill effects, and whenever the game isn't in normal play. `DisableShadows=1`, which is on by default, also turns off shadows after the opening cutscene.

Rates below your frame rate can make animated backgrounds look less smooth, and turning off shadows changes the scene's lighting.

### `SwingJointPhysics`

Hair, costumes, and accessories sway using physics that runs once per rendered frame and doesn't account for frame time. On a display faster than the cabinet's 60 FPS, the sway moves too fast, and the game does more physics work than it needs to. This patch runs the physics at most `SwingJointFPS` times a second (60 by default), which brings back the intended motion and saves the extra work. It also skips joints on hidden objects and does less repeated work per joint.

`SwingJointNative=1` moves the physics into a native helper bundled with the mod. The helper is only used after a live check confirms that it reads and moves the game's objects exactly like the normal code. If that check or a later update fails, the patch keeps using the normal physics code.

### `InactiveMirrorIndicator`

With mirror mode off, the game still keeps the mirror indicator active and just shows a blank image, so the UI system keeps processing it. This patch switches the indicator off instead. Nothing on screen changes.

### `NoUICameraDuringPlay`

The game draws its menus and play HUD with a separate UI camera. During a song, this patch draws those canvases directly on top of the screen, so the UI camera can be switched off, and it skips the UI system's per-frame update. It stays active from the moment a song is about to start until play ends. With `GameplayPrewarm`, that includes the time the music waits for preparation to finish. When a dialog appears, the normal UI camera comes back.

This patch is off by default and experimental. Unusual dialog or error sequences can make UI elements draw in the wrong order.

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

The game rebuilds the lane and hold-note meshes every frame by adding points one at a time. This patch collects the drawing commands for a frame and writes the whole mesh at once through a native helper bundled with the mod. If the helper can't be used, it writes the mesh directly from managed code, and if that isn't possible either, it keeps the game's original method.

It is off by default because testing didn't show a meaningful difference. Leave it off unless it clearly helps on your setup.

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

`UnityPlayerHooks` patches two places inside Unity's own engine code, which a normal MonoMod patch can't reach. The hooks come from a native library embedded in the mod and are installed right after the game starts. Set `UnityPlayerHooks=0` to turn both off, or turn each one off separately under `[Optimization.UnityPlayerHooks]`.

The hooks find their targets by scanning for known code patterns, so they support both the regular and the development build of the Unity 5.6.4f1 x64 player. If a hook can't find its target exactly, it is skipped with a warning in the log and Unity's original code runs.

### `GpuFenceWait`

When Unity waits for the GPU to finish a frame, it waits on a GPU query. This hook waits for a fence event from the graphics driver instead and then checks the original query, so Unity still gets the same results and handles errors the same way. It needs a GPU and driver with Direct3D 11 fence support. Without that, or if anything fails, Unity's normal wait is used.

In testing, the fence wait performed the same as waiting with a precise timer, and the precise timer was clearly faster than Unity's normal wait. The fence has not been compared directly with Unity's normal wait.

### `JobSignalBatching`

Unity wakes its worker threads when there is work for them. That code has a counting bug: when many workers are waiting, handing out a little work can wake all of them at once. This hook fixes the check so only as many workers wake as there is work for, and wakes several workers with one system call instead of one call each. The number of workers and the way jobs are handed out don't change.

If a combined wake-up fails, the hook falls back to waking the workers one at a time. It stays installed until the game exits, because Unity can still wake workers while it shuts down.

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
```

`build-native` builds every project under `Native/` that has a `build.zig` or `Cargo.toml` and collects the Windows DLLs into `Native/Build`. `build-main` builds the reference projects and then the optimizer. The container needs write access to the reference projects' generated `obj/` and `artifacts/` directories. `patch` applies both optimizer modules to those build outputs and writes portable PDBs next to the patched assemblies.

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

