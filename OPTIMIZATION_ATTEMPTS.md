# MU3 Optimization

## Known Performance Issue

1. There are 5 camera in the PlayMusic scene. Bringing huge overhead on Camera.Render (nearly 5ms)

   * Main Camera
   * Stage Camera
   * Effect Camera
   * FX Gate Camera
   * SystemUI Camera
2. HUGE tris count

   * ~50k per character
   * every notes consists of 3-5 capsule models
   * ~150k in complex background
3. Culling overhead. Probably caused by complex shadows and large number of renderers. The game rarely combines meshes.
4. UpdateRendererBoundingVolumes overhead, caused by large number of deeply nested moving object. Now discovered:

   * fd_field/kiban
   * character animations
5. No batching. Every notes in the game have no batching.

   * Notes: The game actively copies materials across all instances. The hold notes uses runtime generate meshes making GPU instancing unavailable.
   * Characters: Components on character doesn't do batching at all, making each character 50+ drawcalls.
   * UVAnimations: Every UVScroll and UVSheetAnimElement create a new material instance per object. And changes of props making dynamic batching impossible.

6. SkinnedMeshRenderer generates huge amounts of drawcalls.
7. Note Effect: observed frame drops but haven't dive into it.