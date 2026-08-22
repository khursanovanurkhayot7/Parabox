#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    /// <summary>
    /// Read-only Chapter 2 proof replay. It uses the exact runtime LevelModel and never edits a
    /// prefab. The focused three-task boards keep their authored fallbacks here because their
    /// serialized solutions are blank to prevent the automatic rebalancer from adding objectives.
    /// </summary>
    [InitializeOnLoad]
    public static class ParaboxChapterTwoSolvabilityVerifier
    {
        const int FirstLevel = 11;
        const int LastLevel = 20;
        const string LevelFolder = "Assets/Parabox/Prefabs/Levels";

        static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName;
        static string RequestPath => Path.Combine(ProjectRoot, "Library", "ParaboxVerifyChapterTwo.request");
        static string ResultPath => Path.Combine(ProjectRoot, "Library", "ParaboxVerifyChapterTwo.result");

        static ParaboxChapterTwoSolvabilityVerifier()
        {
            EditorApplication.update += ProcessRequest;
        }

        static void ProcessRequest()
        {
            if (string.IsNullOrEmpty(ProjectRoot) || !File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            try
            {
                string report = Verify();
                File.WriteAllText(ResultPath, "PASS\n" + report);
                Debug.Log(report);
            }
            catch (Exception ex)
            {
                File.WriteAllText(ResultPath, "FAIL\n" + ex);
                Debug.LogException(ex);
            }
        }

        [MenuItem("Tools/Parabox/Validate Chapter 2 Solvability (Levels 11-20)")]
        public static void VerifyFromMenu()
        {
            string report = Verify();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Chapter 2 Solvability", report, "OK");
        }

        public static void VerifyFromCommandLine()
        {
            string report = Verify();
            Debug.Log(report);
        }

        public static string Verify()
        {
            var report = new StringBuilder(2048);
            int passed = 0;

            for (int levelNumber = FirstLevel; levelNumber <= LastLevel; levelNumber++)
            {
                string path = $"{LevelFolder}/Level_{levelNumber}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    throw new InvalidOperationException($"Level {levelNumber}: missing prefab at {path}.");

                ParaboxLevel info = prefab.GetComponent<ParaboxLevel>();
                if (info == null)
                    throw new InvalidOperationException($"Level {levelNumber}: missing ParaboxLevel metadata.");

                string route = string.IsNullOrWhiteSpace(info.solution)
                    ? FocusedThreeTaskRoute(levelNumber)
                    : info.solution;
                if (string.IsNullOrWhiteSpace(route))
                    throw new InvalidOperationException($"Level {levelNumber}: no route is available to prove solvability.");

                LevelModel model = LevelParser.Parse(prefab);
                if (model.player == null)
                    throw new InvalidOperationException($"Level {levelNumber}: parsed model has no player.");

                for (int step = 0; step < route.Length; step++)
                {
                    if (!TryDirection(route[step], out Vector2Int direction))
                        throw new InvalidOperationException(
                            $"Level {levelNumber}: invalid route character '{route[step]}' at move {step + 1}.");
                    if (!model.TryMovePlayer(direction))
                        throw new InvalidOperationException(
                            $"Level {levelNumber}: route is blocked at move {step + 1} ({route[step]}).");
                }

                if (!model.IsWon())
                    throw new InvalidOperationException(
                        $"Level {levelNumber}: route ends after {model.MoveCount} moves without completing every target.");

                int moveLimit = GameManager.MoveLimitForLevel(levelNumber - 1, info.par);
                if (model.MoveCount > moveLimit)
                    throw new InvalidOperationException(
                        $"Level {levelNumber}: winning route uses {model.MoveCount} moves, above limit {moveLimit}.");

                passed++;
                report.AppendLine(
                    $"PASS L{levelNumber:00} {info.levelName}: {model.MoveCount}/{moveLimit} moves");
            }

            report.AppendLine($"RESULT: {passed}/{LastLevel - FirstLevel + 1} Chapter 2 levels are solvable.");
            return report.ToString();
        }

        static string FocusedThreeTaskRoute(int levelNumber)
        {
            switch (levelNumber)
            {
                case 13: return "LLLLLLUUUUDDDDDDLLLLDLL";
                case 16: return "RRRURDDDLDRRRRUULLLL";
                case 17: return "URRRRDDDDRDLLLLLLUURRRDDLUUR";
                case 18: return "RRRRRURDDDDLDRRRRRUULLLLLLLLLLUL";
                case 19: return "LLLLLLLULDDDDDDDRDLLLLLLLLUURRRRRRRRRU";
                case 20: return "LLLLLLDDDDDDRDLLLLLLLLLLURRRRDLUURRRRRRRRRRRRU";
                default: return string.Empty;
            }
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
    }
}
#endif
