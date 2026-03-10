#if ENABLE_PROFILER
using System.Collections.Generic;
using System.IO;
using System.Text;
using MU3.Battle;
using MU3.Util;
using Debug = UnityEngine.Debug;
using UnityEngine;

namespace MU3.Notes;

/// <summary>
/// Runtime batch diagnostics for note renderers.
/// Press F6 to dump a report to batch_debug_report.txt.
///
/// Iterates NotesManager.noteCacheList to inspect every pooled note
/// GameObject's renderer state.  Detects issues affecting:
///
///   Dynamic Batching:
///   - Material instance cloning (renderer.material vs sharedMaterial)
///   - Per-renderer render-queue divergence
///   - Distinct meshes / vertex counts exceeding dynamic-batch limits (>300 verts)
///   - Non-uniform or negative scale
///   - MaterialPropertyBlock overrides
///   - Multi-pass shaders
///
///   GPU Instancing:
///   - Material.enableInstancing not set
///   - Distinct mesh variations within the same pool
///   - MaterialPropertyBlock with non-instanced properties
///   - SkinnedMeshRenderers (not supported by instancing)
///   - Lightmap or light-probe differences
///
/// Compiled only when ENABLE_PROFILER is defined (Debug and Release builds).
/// </summary>
public class BatchDebug : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6))
            DumpBatchReport();
    }

    private static void DumpBatchReport()
    {
        GameEngine gameEngine = SingletonMonoBehaviour<GameEngine>.instance;
        if (gameEngine == null || gameEngine.notesManager == null)
        {
            Debug.Log("[BatchDebug] NotesManager not available.");
            return;
        }

        NotesCacheList cacheList = gameEngine.notesManager.noteCacheList;
        if (cacheList == null || cacheList.Count == 0)
        {
            Debug.Log("[BatchDebug] noteCacheList is empty or null.");
            return;
        }

        var sb = new StringBuilder(16384);
        sb.AppendLine("╔══════════════════════════════════════════════════════════╗");
        sb.AppendLine("║              BatchDebug Report                          ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        // fingerprint (shader|texId) → list of description strings.
        var globalFingerprints = new Dictionary<string, List<string>>();
        // mesh instanceID → list of (noteType, meshName) for cross-pool mesh sharing check.
        var globalMeshUsers = new Dictionary<int, List<string>>();
        var mpb = new MaterialPropertyBlock();

        int totalPools = 0;
        int totalDynBatchIssues = 0;
        int totalInstancingIssues = 0;

        for (int t = 0; t < cacheList.Count; t++)
        {
            NotesCache cache = cacheList[t];
            if (cache.Count == 0) continue;
            totalPools++;

            sb.AppendLine("┌──────────────────────────────────────────────────────────");
            sb.Append("│ Pool: ").AppendLine(cache.noteType.ToString());
            sb.Append("│ Size: ").Append(cache.Count)
              .Append("  InUse: ").Append(cache.use)
              .Append("  PeakUse: ").AppendLine(cache.usemax.ToString());
            sb.AppendLine("└──────────────────────────────────────────────────────────");

            // ── Collect per-pool renderer data ──────────────────────────
            var matIds = new Dictionary<int, int>();       // matInstanceID → count
            var meshIds = new Dictionary<int, string>();   // meshInstanceID → meshName
            int totalRenderers = 0;
            int skinnedCount = 0;
            bool hasLightProbes = false;
            bool hasLightmaps = false;

            for (int i = 0; i < cache.Count; i++)
            {
                NotesCacheItem item = cache[i];
                if (item == null || item.go == null) continue;

                var renderers = item.go.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer rend = renderers[r];
                    if (rend == null) continue;
                    totalRenderers++;

                    if (rend is SkinnedMeshRenderer)
                        skinnedCount++;

                    if (rend.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off)
                        hasLightProbes = true;
                    if (rend.lightmapIndex >= 0 && rend.lightmapIndex != 0xFFFE)
                        hasLightmaps = true;

                    Material mat = rend.sharedMaterial;
                    if (mat != null)
                    {
                        int matId = mat.GetInstanceID();
                        if (!matIds.ContainsKey(matId))
                            matIds[matId] = 0;
                        matIds[matId]++;
                    }

                    MeshFilter mf = rend.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        int meshId = mf.sharedMesh.GetInstanceID();
                        if (!meshIds.ContainsKey(meshId))
                        {
                            meshIds[meshId] = mf.sharedMesh.name;
                            if (!globalMeshUsers.ContainsKey(meshId))
                                globalMeshUsers[meshId] = new List<string>();
                            globalMeshUsers[meshId].Add(cache.noteType.ToString());
                        }
                    }
                }
            }

            sb.Append("  Renderers: ").Append(totalRenderers)
              .Append("  Materials: ").Append(matIds.Count)
              .Append("  Meshes: ").AppendLine(meshIds.Count.ToString());
            sb.AppendLine();

            // ═══════════════════════════════════════════════════════════
            // SECTION A: Dynamic Batching Analysis
            // ═══════════════════════════════════════════════════════════
            sb.AppendLine("  [Dynamic Batching]");
            var dynIssues = new List<string>();

            if (matIds.Count > 1)
                dynIssues.Add("Multiple material instances (" + matIds.Count + ") in pool — must share ONE material");

            // Scale check.
            NotesCacheItem firstItem = cache.Count > 0 ? cache[0] : null;
            if (firstItem != null && firstItem.go != null)
            {
                Vector3 s = firstItem.go.transform.lossyScale;
                bool neg = s.x < 0f || s.y < 0f || s.z < 0f;
                bool nonUni = !Mathf.Approximately(Mathf.Abs(s.x), Mathf.Abs(s.y))
                           || !Mathf.Approximately(Mathf.Abs(s.x), Mathf.Abs(s.z));
                if (neg)
                    dynIssues.Add("Negative scale (" + s.x.ToString("F2") + ", "
                        + s.y.ToString("F2") + ", " + s.z.ToString("F2") + ")");
                if (nonUni)
                    dynIssues.Add("Non-uniform scale (" + s.x.ToString("F2") + ", "
                        + s.y.ToString("F2") + ", " + s.z.ToString("F2") + ")");
            }

            // Per-item dynamic batch checks.
            int prevQueue = -1;
            for (int i = 0; i < cache.Count; i++)
            {
                NotesCacheItem item = cache[i];
                if (item == null || item.go == null) continue;

                var renderers = item.go.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer rend = renderers[r];
                    if (rend == null || rend.sharedMaterial == null) continue;
                    Material mat = rend.sharedMaterial;

                    if (prevQueue >= 0 && mat.renderQueue != prevQueue)
                    {
                        string msg = "Render queue divergence: item[" + i + "] queue="
                            + mat.renderQueue + " vs previous=" + prevQueue;
                        if (!dynIssues.Contains(msg)) dynIssues.Add(msg);
                    }
                    prevQueue = mat.renderQueue;

                    rend.GetPropertyBlock(mpb);
                    if (!mpb.isEmpty)
                    {
                        string msg = "item[" + i + "] \"" + item.go.name + "\" has MaterialPropertyBlock";
                        dynIssues.Add(msg);
                    }

                    MeshFilter mf = rend.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 300)
                    {
                        string msg = "item[" + i + "] mesh \"" + mf.sharedMesh.name
                            + "\" has " + mf.sharedMesh.vertexCount + " verts (limit: 300)";
                        dynIssues.Add(msg);
                    }

                    if (mat.passCount > 1)
                    {
                        string msg = "Multi-pass shader (" + mat.passCount + " passes)";
                        if (!dynIssues.Contains(msg)) dynIssues.Add(msg);
                    }
                }
            }

            if (dynIssues.Count == 0)
            {
                sb.AppendLine("    OK — no issues detected");
            }
            else
            {
                totalDynBatchIssues += dynIssues.Count;
                for (int i = 0; i < dynIssues.Count; i++)
                    sb.Append("    WARN: ").AppendLine(dynIssues[i]);
            }
            sb.AppendLine();

            // ═══════════════════════════════════════════════════════════
            // SECTION B: GPU Instancing Analysis
            // ═══════════════════════════════════════════════════════════
            sb.AppendLine("  [GPU Instancing]");
            var instIssues = new List<string>();

            if (skinnedCount > 0)
                instIssues.Add("Pool has " + skinnedCount + " SkinnedMeshRenderer(s) — GPU instancing not supported");

            if (meshIds.Count > 1)
            {
                sb.Append("    INFO: ").Append(meshIds.Count).AppendLine(" distinct meshes in pool:");
                foreach (var kv in meshIds)
                    sb.Append("      - \"").Append(kv.Value).Append("\" id=").AppendLine(kv.Key.ToString());
                instIssues.Add("Multiple distinct meshes — instances must share the same mesh");
            }

            if (hasLightProbes)
                instIssues.Add("Light probes enabled — different probe data can break instancing batches");

            if (hasLightmaps)
                instIssues.Add("Lightmapped renderers — different lightmap indices break instancing");

            // Check each material for instancing readiness.
            foreach (var kv in matIds)
            {
                Material mat = FindMatById(cache, kv.Key);
                if (mat == null) continue;

                if (!mat.enableInstancing)
                    instIssues.Add("Material \"" + mat.name + "\" has enableInstancing=OFF");

                if (mat.shader != null && mat.shader.name.Contains("Particle"))
                    instIssues.Add("Material \"" + mat.name + "\" uses particle shader — may not support instancing");
            }

            // Check for non-instanced MaterialPropertyBlock usage.
            for (int i = 0; i < cache.Count; i++)
            {
                NotesCacheItem item = cache[i];
                if (item == null || item.go == null) continue;

                var renderers = item.go.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer rend = renderers[r];
                    if (rend == null) continue;

                    rend.GetPropertyBlock(mpb);
                    if (!mpb.isEmpty)
                    {
                        instIssues.Add("item[" + i + "] \"" + item.go.name
                            + "\" has MaterialPropertyBlock — only instanced properties (_Color etc.) are batch-safe");
                        break; // one warning per item is enough
                    }
                }
            }

            if (instIssues.Count == 0)
            {
                sb.AppendLine("    OK — no issues detected");
            }
            else
            {
                totalInstancingIssues += instIssues.Count;
                for (int i = 0; i < instIssues.Count; i++)
                    sb.Append("    WARN: ").AppendLine(instIssues[i]);
            }
            sb.AppendLine();

            // ═══════════════════════════════════════════════════════════
            // SECTION C: Material Details
            // ═══════════════════════════════════════════════════════════
            sb.AppendLine("  [Materials]");
            foreach (var kv in matIds)
            {
                Material mat = FindMatById(cache, kv.Key);
                if (mat == null) continue;

                string shaderName = mat.shader != null ? mat.shader.name : "(null)";
                sb.Append("    \"").Append(mat.name).Append("\"  (id=").Append(kv.Key)
                  .Append(", users=").Append(kv.Value).AppendLine(")");
                sb.Append("      Shader      : ").AppendLine(shaderName);
                sb.Append("      Passes      : ").AppendLine(mat.passCount.ToString());
                sb.Append("      RenderQueue : ").AppendLine(mat.renderQueue.ToString());
                sb.Append("      Instancing  : ").AppendLine(mat.enableInstancing ? "ON" : "OFF");

                int texId = 0;
                if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
                {
                    texId = mat.mainTexture.GetInstanceID();
                    sb.Append("      MainTex     : \"").Append(mat.mainTexture.name)
                      .Append("\" (id=").Append(texId).AppendLine(")");
                }
                else
                {
                    sb.AppendLine("      MainTex     : (none)");
                }

                if (mat.name.Contains("(Instance)"))
                    sb.AppendLine("      WARN: CLONED material — name contains \"(Instance)\"");

                // Register for cross-type check.
                string fp = shaderName + "|" + texId;
                string entry = cache.noteType + " mat=\"" + mat.name
                    + "\" id=" + kv.Key + " queue=" + mat.renderQueue
                    + " instancing=" + (mat.enableInstancing ? "ON" : "OFF");
                if (!globalFingerprints.ContainsKey(fp))
                    globalFingerprints[fp] = new List<string>();
                globalFingerprints[fp].Add(entry);

                sb.AppendLine();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PHASE 2: Cross-Pool Analysis
        // ═══════════════════════════════════════════════════════════════
        sb.AppendLine("╔══════════════════════════════════════════════════════════╗");
        sb.AppendLine("║              Cross-Pool Analysis                        ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        // Same shader+texture but different Material objects.
        sb.AppendLine("  [Duplicate Materials — same shader+texture, different object]");
        bool anyDupes = false;
        foreach (var kv in globalFingerprints)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < kv.Value.Count; i++)
                seen.Add(kv.Value[i]);

            if (seen.Count <= 1) continue;
            anyDupes = true;
            sb.Append("    Fingerprint: ").AppendLine(kv.Key);
            foreach (string e in seen)
                sb.Append("      - ").AppendLine(e);
            sb.AppendLine("      ^ Cannot batch across these pools — consolidate into one Material.");
            sb.AppendLine();
        }

        if (!anyDupes)
            sb.AppendLine("    OK — no cross-pool material duplicates found.");
        sb.AppendLine();

        // Shared meshes across pools (good for instancing).
        sb.AppendLine("  [Mesh Sharing Across Pools]");
        bool anyShared = false;
        foreach (var kv in globalMeshUsers)
        {
            if (kv.Value.Count <= 1) continue;
            anyShared = true;
            sb.Append("    Mesh id=").Append(kv.Key).AppendLine(" shared by:");
            for (int i = 0; i < kv.Value.Count; i++)
                sb.Append("      - ").AppendLine(kv.Value[i]);
            sb.AppendLine("      ^ Good — shared mesh enables cross-pool instancing if material matches.");
            sb.AppendLine();
        }

        if (!anyShared)
            sb.AppendLine("    INFO: No meshes shared across pools.");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════════
        // SUMMARY
        // ═══════════════════════════════════════════════════════════════
        sb.AppendLine("╔══════════════════════════════════════════════════════════╗");
        sb.AppendLine("║              Summary                                    ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════╝");
        sb.Append("  Pools analyzed          : ").AppendLine(totalPools.ToString());
        sb.Append("  Dynamic batch warnings  : ").AppendLine(totalDynBatchIssues.ToString());
        sb.Append("  GPU instancing warnings : ").AppendLine(totalInstancingIssues.ToString());
        if (totalDynBatchIssues == 0 && totalInstancingIssues == 0)
            sb.AppendLine("  All pools look batch-friendly!");
        sb.AppendLine();

        string report = sb.ToString();

        try
        {
            File.WriteAllText("batch_debug_report.txt", report);
            Debug.Log("[BatchDebug] Report written to "
                + System.IO.Path.GetFullPath("batch_debug_report.txt"));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[BatchDebug] Failed to write report: " + ex.Message);
        }
    }

    private static Material FindMatById(NotesCache cache, int matId)
    {
        for (int i = 0; i < cache.Count; i++)
        {
            NotesCacheItem item = cache[i];
            if (item == null || item.go == null) continue;
            var renderers = item.go.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r].sharedMaterial != null
                    && renderers[r].sharedMaterial.GetInstanceID() == matId)
                    return renderers[r].sharedMaterial;
            }
        }

        return null;
    }
}
#endif