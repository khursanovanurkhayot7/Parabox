#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    /// <summary>
    /// Repairs proof metadata for the 50 existing campaign prefabs. It never regenerates a board,
    /// adds/removes a GameObject, or changes a marker. A replacement proof is committed only after
    /// the exact current LevelModel wins, and the old runtime-layout proof is pinned so route-safe
    /// rebalancing cannot make the board look or play differently after the metadata repair.
    /// </summary>
    public static class ParaboxSolvabilityRepair
    {
        const int LevelCount = 50;
        const int MaximumSearchDepth = 320;
        const int SearchNodeLimit = 3000000;
        const string LevelFolder = "Assets/Parabox/Prefabs/Levels";
        const string MenuPath =
            "Tools/Parabox/Validation/Repair All 50 Solutions (Keep Levels Unchanged)";

        sealed class LevelRepair
        {
            public int number;
            public string path;
            public string name;
            public string layoutSignature;
            public string oldSolution;
            public int oldPar;
            public bool oldPreserveRuntimeLayoutProof;
            public string oldRuntimeLayoutProof;
            public string solution;

            public bool NeedsWrite => oldSolution != solution || oldPar != solution.Length;
        }

        [MenuItem(MenuPath, priority = 240)]
        public static void RepairAllSolutions()
        {
            if (!EditorUtility.DisplayDialog(
                    "Repair solvability without changing levels",
                    "This searches the exact current Level_1 through Level_50 prefabs.\n\n"
                    + "It will NOT rebuild levels or change rooms, objects, walls, mechanics, "
                    + "difficulty, timers, transforms, or visuals. Only a stale solution/par proof "
                    + "may be repaired. Runtime layout generation remains locked to its current "
                    + "pre-repair proof.\n\n"
                    + "Every proven repair is verified and saved independently. A board that "
                    + "cannot be proven is left completely unchanged and listed in the report.",
                    "Run safe repair", "Cancel"))
                return;

            var repairs = new List<LevelRepair>(LevelCount);
            var committed = new List<LevelRepair>(LevelCount);
            var failures = new List<string>();
            try
            {
                for (int number = 1; number <= LevelCount; number++)
                {
                    string path = $"{LevelFolder}/Level_{number}.prefab";
                    EditorUtility.DisplayProgressBar(
                        "Parabox solvability repair",
                        $"Proving the existing Level {number}/{LevelCount} board...",
                        (number - 1f) / LevelCount);

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
                    if (prefab == null || info == null)
                    {
                        failures.Add($"L{number:00}: missing prefab or ParaboxLevel metadata at {path}");
                        continue;
                    }

                    string existingError;
                    bool existingWins = RouteWins(prefab, info.solution, out existingError);
                    string solution = info.solution;
                    if (!existingWins)
                    {
                        int maximumDepth = Mathf.Max(MaximumSearchDepth, info.par + 96);
                        solution = ParaboxCampaignSolver.FindShortest(
                            prefab, Mathf.Max(1, info.par), maximumDepth, SearchNodeLimit,
                            () => EditorUtility.DisplayCancelableProgressBar(
                                "Parabox solvability repair",
                                $"Searching Level {number}/{LevelCount}... "
                                + "Click Cancel to stop safely.",
                                (number - 1f) / LevelCount));
                        if (string.IsNullOrEmpty(solution))
                        {
                            failures.Add(
                                $"L{number:00} {info.levelName}: {existingError}; no winning route "
                                + $"was proven within {maximumDepth} moves/{SearchNodeLimit:N0} nodes");
                            continue;
                        }

                        if (!RouteWins(prefab, solution, out string solverError))
                        {
                            failures.Add(
                                $"L{number:00} {info.levelName}: solver candidate failed replay: "
                                + solverError);
                            continue;
                        }
                    }

                    repairs.Add(new LevelRepair
                    {
                        number = number,
                        path = path,
                        name = info.levelName,
                        layoutSignature = AuthoredLayoutSignature(prefab),
                        oldSolution = info.solution ?? string.Empty,
                        oldPar = info.par,
                        oldPreserveRuntimeLayoutProof = info.preserveRuntimeLayoutProof,
                        oldRuntimeLayoutProof = info.runtimeLayoutProof ?? string.Empty,
                        solution = solution,
                    });
                }

                var unresolvedFailures = new List<string>(failures);
                if (repairs.Count == 0)
                {
                    string failedReport = BuildFailureReport(failures, repairs.Count);
                    WriteReport(failedReport);
                    Debug.LogWarning(failedReport);
                    EditorUtility.DisplayDialog(
                        "No levels were changed",
                        "No current board could be proven, so nothing was saved. Open "
                        + "Library/ParaboxSolvabilityRepair.txt for details.",
                        "OK");
                    return;
                }

                int changed = 0;
                foreach (LevelRepair repair in repairs)
                {
                    if (!repair.NeedsWrite) continue;
                    committed.Add(repair);
                    SaveRepair(repair);
                    changed++;
                }
                AssetDatabase.SaveAssets();

                failures.Clear();
                for (int i = 0; i < repairs.Count; i++)
                {
                    LevelRepair repair = repairs[i];
                    EditorUtility.DisplayProgressBar(
                        "Parabox solvability repair",
                        $"Verifying Level {repair.number}/{LevelCount} after metadata save...",
                        0.8f + i / (LevelCount * 5f));

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(repair.path);
                    ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
                    if (prefab == null || info == null)
                    {
                        failures.Add($"L{repair.number:00}: prefab disappeared after save");
                        continue;
                    }
                    if (!string.Equals(
                            repair.layoutSignature, AuthoredLayoutSignature(prefab),
                            StringComparison.Ordinal))
                        failures.Add($"L{repair.number:00}: authored layout signature changed");
                    if (info.solution != repair.solution || info.par != repair.solution.Length)
                        failures.Add($"L{repair.number:00}: repaired proof metadata was not saved");
                    if (!RouteWins(prefab, info.solution, out string replayError))
                        failures.Add($"L{repair.number:00}: final proof failed replay: {replayError}");
                }

                if (failures.Count > 0)
                {
                    RollBack(committed);
                    string rollbackReport = BuildFailureReport(failures, repairs.Count)
                        + "\nROLLBACK: every solution/par/layout-proof field was restored.\n";
                    WriteReport(rollbackReport);
                    Debug.LogError(rollbackReport);
                    EditorUtility.DisplayDialog(
                        "Repair rolled back safely",
                        "A post-save check failed, so all proof metadata was restored. No level "
                        + "layout was changed. Open the Console for the exact level.",
                        "OK");
                    return;
                }

                if (unresolvedFailures.Count > 0)
                {
                    string partialReport = BuildPartialReport(
                        repairs, changed, unresolvedFailures);
                    WriteReport(partialReport);
                    Debug.LogWarning(partialReport);
                    EditorUtility.DisplayDialog(
                        "Safe proof repair completed",
                        $"{repairs.Count}/{LevelCount} boards now have verified winning proofs. "
                        + $"{unresolvedFailures.Count} board(s) could not be proven and were left "
                        + "unchanged. No layouts, objects, mechanics, timers or visuals changed.\n\n"
                        + "Report: Library/ParaboxSolvabilityRepair.txt",
                        "OK");
                    return;
                }

                string successReport = BuildSuccessReport(repairs, changed);
                WriteReport(successReport);
                Debug.Log(successReport);
                EditorUtility.DisplayDialog(
                    "All 50 levels are solvable",
                    $"50/50 existing boards have winning proofs. {changed} stale proof(s) were "
                    + "repaired. No level layouts, objects, mechanics, difficulty, timers, or "
                    + "visuals were changed.\n\nFull report: "
                    + "Library/ParaboxSolvabilityRepair.txt",
                    "OK");
            }
            catch (OperationCanceledException)
            {
                if (committed.Count > 0)
                {
                    try { RollBack(committed); }
                    catch (Exception rollbackEx) { Debug.LogException(rollbackEx); }
                }
                string cancelledReport =
                    "PARABOX SOLVABILITY REPAIR CANCELLED\n"
                    + "No level layout was changed. Any proof metadata touched by this run "
                    + "was restored.\n";
                WriteReport(cancelledReport);
                Debug.LogWarning(cancelledReport);
                EditorUtility.DisplayDialog(
                    "Repair cancelled safely",
                    "The search was cancelled. No level layout was changed and any proof "
                    + "metadata touched by this run was restored.",
                    "OK");
            }
            catch (Exception ex)
            {
                if (committed.Count > 0)
                {
                    try { RollBack(committed); }
                    catch (Exception rollbackEx) { Debug.LogException(rollbackEx); }
                }
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "Repair stopped",
                    "The operation failed and any touched proof metadata was restored. No level "
                    + "layout was intentionally changed. See the Console for details.",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(MenuPath, true)]
        static bool CanRepairAllSolutions()
            => !Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode
               && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        static void SaveRepair(LevelRepair repair)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(repair.path);
            try
            {
                ParaboxLevel info = root.GetComponent<ParaboxLevel>();
                if (info == null)
                    throw new InvalidOperationException(
                        $"Level {repair.number} lost its ParaboxLevel component.");

                if (info.solution != repair.solution)
                {
                    // Preserve the exact proof that currently controls runtime decoration. A stale
                    // or blank proof deliberately continues to produce the same unmodified model.
                    string currentLayoutProof = info.preserveRuntimeLayoutProof
                        ? info.runtimeLayoutProof ?? string.Empty
                        : info.solution ?? string.Empty;
                    info.preserveRuntimeLayoutProof = true;
                    info.runtimeLayoutProof = currentLayoutProof;
                    info.solution = repair.solution;
                }
                info.par = repair.solution.Length;
                PrefabUtility.SaveAsPrefabAsset(root, repair.path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void RollBack(List<LevelRepair> repairs)
        {
            foreach (LevelRepair repair in repairs)
            {
                if (!repair.NeedsWrite) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(repair.path);
                try
                {
                    ParaboxLevel info = root.GetComponent<ParaboxLevel>();
                    if (info == null) continue;
                    info.solution = repair.oldSolution;
                    info.par = repair.oldPar;
                    info.preserveRuntimeLayoutProof = repair.oldPreserveRuntimeLayoutProof;
                    info.runtimeLayoutProof = repair.oldRuntimeLayoutProof;
                    PrefabUtility.SaveAsPrefabAsset(root, repair.path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
        }

        static bool RouteWins(GameObject prefab, string route, out string error)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                error = "stored solution is empty";
                return false;
            }

            LevelModel model;
            try { model = LevelParser.Parse(prefab); }
            catch (Exception ex)
            {
                error = "parse failed: " + ex.Message;
                return false;
            }
            if (model == null || model.player == null)
            {
                error = "parsed model has no player";
                return false;
            }

            for (int step = 0; step < route.Length; step++)
            {
                if (!TryDirection(route[step], out Vector2Int direction))
                {
                    error = $"invalid command '{route[step]}' at move {step + 1}";
                    return false;
                }
                if (!model.TryMovePlayer(direction))
                {
                    error = $"route is blocked at move {step + 1} ({route[step]})";
                    return false;
                }
            }

            if (!model.IsWon())
            {
                error = $"route ends after {model.MoveCount} moves without completing every target";
                return false;
            }
            error = string.Empty;
            return true;
        }

        static bool TryDirection(char code, out Vector2Int direction)
        {
            switch (code)
            {
                case 'U': direction = Vector2Int.up; return true;
                case 'R': direction = Vector2Int.right; return true;
                case 'D': direction = Vector2Int.down; return true;
                case 'L': direction = Vector2Int.left; return true;
                default: direction = default; return false;
            }
        }

        // Hash every authored object/component except the four ParaboxLevel proof fields. This is
        // checked before and after saving so the command cannot silently mutate level content.
        static string AuthoredLayoutSignature(GameObject prefab)
        {
            var source = new StringBuilder(32768);
            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            Array.Sort(transforms, (a, b) => string.CompareOrdinal(TransformPath(a), TransformPath(b)));
            foreach (Transform transform in transforms)
            {
                GameObject go = transform.gameObject;
                source.Append(TransformPath(transform)).Append('|')
                    .Append(go.activeSelf ? '1' : '0').Append('|')
                    .Append(go.layer).Append('|').Append(go.tag).Append('|')
                    .Append(go.isStatic ? '1' : '0').Append('|');
                AppendVector(source, transform.localPosition);
                AppendQuaternion(source, transform.localRotation);
                AppendVector(source, transform.localScale);

                Component[] components = go.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (component == null || component is Transform)
                        continue;
                    if (component is ParaboxLevel level)
                    {
                        AppendProtectedLevelMetadata(source, level);
                        continue;
                    }
                    source.Append(component.GetType().AssemblyQualifiedName).Append(':')
                        .Append(EditorJsonUtility.ToJson(component, false)).Append(';');
                }
            }

            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(source.ToString()));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        static void AppendProtectedLevelMetadata(StringBuilder target, ParaboxLevel level)
        {
            // solution/par and the two runtime-proof preservation fields are the only values the
            // tool is allowed to touch. Every designer-facing field remains part of the hash.
            target.Append(typeof(ParaboxLevel).AssemblyQualifiedName).Append(':')
                .Append(level.levelName).Append('|')
                .Append(level.difficultyRating.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(level.designComplexity).Append('|')
                .Append(level.chapter).Append('|')
                .Append(level.chapterName).Append('|')
                .Append(level.chapterPhilosophy).Append('|')
                .Append((int)level.progressionRole).Append('|')
                .Append(level.mechanicFocus).Append('|')
                .Append(level.introducesMechanic ? '1' : '0').Append(';');
        }

        static string TransformPath(Transform transform)
        {
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.GetSiblingIndex().ToString("D4") + ":" + current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        static void AppendVector(StringBuilder target, Vector3 value)
        {
            target.Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        static void AppendQuaternion(StringBuilder target, Quaternion value)
        {
            target.Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.w.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        static string BuildFailureReport(List<string> failures, int proven)
        {
            var report = new StringBuilder(4096);
            report.AppendLine("PARABOX SOLVABILITY REPAIR - NOT SAVED");
            report.AppendLine("=======================================");
            report.AppendLine($"Proven before save: {proven}/{LevelCount}");
            report.AppendLine("No level prefab metadata or layout was changed.");
            report.AppendLine();
            foreach (string failure in failures) report.AppendLine("FAIL " + failure);
            return report.ToString();
        }

        static string BuildSuccessReport(List<LevelRepair> repairs, int changed)
        {
            var report = new StringBuilder(8192);
            report.AppendLine("PARABOX SOLVABILITY REPAIR - PASS");
            report.AppendLine("=================================");
            report.AppendLine($"RESULT: {repairs.Count}/{LevelCount} current boards are solvable.");
            report.AppendLine($"Metadata repairs: {changed}");
            report.AppendLine("Authored layout changes: 0");
            report.AppendLine("Objects/mechanics/difficulty/timers/visual changes: 0");
            report.AppendLine();
            foreach (LevelRepair repair in repairs)
            {
                string status = repair.NeedsWrite ? "REPAIRED" : "KEPT";
                report.AppendLine(
                    $"{status,-8} L{repair.number:00} {repair.name}: {repair.solution.Length} moves");
            }
            return report.ToString();
        }

        static string BuildPartialReport(List<LevelRepair> repairs, int changed,
                                         List<string> unresolvedFailures)
        {
            var report = new StringBuilder(8192);
            report.AppendLine("PARABOX SOLVABILITY REPAIR - PARTIAL PASS");
            report.AppendLine("=========================================");
            report.AppendLine($"RESULT: {repairs.Count}/{LevelCount} current boards are proven.");
            report.AppendLine($"Metadata repairs safely saved: {changed}");
            report.AppendLine($"Unresolved boards left unchanged: {unresolvedFailures.Count}");
            report.AppendLine("Authored layout changes: 0");
            report.AppendLine("Objects/mechanics/difficulty/timers/visual changes: 0");
            report.AppendLine();
            foreach (LevelRepair repair in repairs)
            {
                string status = repair.NeedsWrite ? "REPAIRED" : "KEPT";
                report.AppendLine(
                    $"{status,-8} L{repair.number:00} {repair.name}: "
                    + $"{repair.solution.Length} moves");
            }
            report.AppendLine();
            foreach (string failure in unresolvedFailures)
                report.AppendLine("UNRESOLVED " + failure);
            return report.ToString();
        }

        static void WriteReport(string report)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return;
            File.WriteAllText(
                Path.Combine(projectRoot, "Library", "ParaboxSolvabilityRepair.txt"), report);
        }
    }
}
#endif
