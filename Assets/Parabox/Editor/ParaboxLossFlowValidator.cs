using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // Focused release check for the post-loss contract. Production loss is terminal and must show
    // GAME OVER plus a visible five-second return-to-arcade countdown with no local actions.
    public static class ParaboxLossFlowValidator
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [InitializeOnLoadMethod]
        static void RunRequestedValidation()
        {
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string request = System.IO.Path.Combine(
                projectRoot, "Library", "ParaboxValidateLossFlow.request");
            if (!System.IO.File.Exists(request)) return;
            System.IO.File.Delete(request);
            EditorApplication.delayCall += ValidateFromCommandLine;
        }

        [MenuItem("Tools/Parabox/Validate Luxodd Loss / Continue Flow")]
        public static void ValidateFromMenu()
        {
            string report = Validate();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Parabox Loss Flow", report, "OK");
        }

        public static void ValidateFromCommandLine()
        {
            string report = Validate();
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(projectRoot, "Library", "ParaboxLossFlowValidation.txt"), report);
            Debug.Log(report);
        }

        static string Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run loss-flow validation in Edit Mode.");
            var report = new StringBuilder();
            int failures = 0;

            ValidateOverlay(ref failures, report);
            ValidateFreshLevelContinue(ref failures, report);
            ValidateSessionTransactionContract(ref failures, report);

            string result = failures == 0
                ? "LOSS FLOW VALIDATION PASSED — CONTINUE retries the current level; RESTART begins a new campaign."
                : $"LOSS FLOW VALIDATION FAILED — {failures} error(s).";
            report.AppendLine(result);
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            return report.ToString();
        }

        static void ValidateOverlay(ref int failures, StringBuilder report)
        {
            var root = new GameObject("LossFlowValidation", typeof(RectTransform));
            var retry = ChildRect(root.transform, "RestartHolder");
            var levels = ChildRect(root.transform, "LevelsHolder");
            var buttonObject = new GameObject("StyleSource", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(retry, false);

            var fx = root.AddComponent<LoseFx>();
            fx.titleText = TextChild(root.transform, "GameOverTitle");
            fx.subText = TextChild(root.transform, "GameOverReturnCountdown");
            fx.restartRT = retry;
            fx.levelsRT = levels;
            Invoke(fx, "Awake");

            Check(!retry.gameObject.activeSelf && !levels.gameObject.activeSelf,
                "legacy Continue and Levels holders are inactive", ref failures, report);
            Check(fx.transactionDelay >= 4.9f && fx.transactionDelay <= 5.1f,
                $"arcade return countdown is {fx.transactionDelay:0.0}s", ref failures, report);
            MethodInfo formatter = typeof(LoseFx).GetMethod("ReturnMessage",
                BindingFlags.Public | BindingFlags.Static);
            string copy = formatter != null
                ? formatter.Invoke(null, new object[] { "OUT OF MOVES", 5 }) as string
                : string.Empty;
            Check(copy != null && copy.Contains("CONTINUE / RESTART IN 5"),
                "loss copy explicitly announces the five-second session options countdown",
                ref failures, report);
            Check(typeof(LossLeaderboard).GetMethod("SetPlayerScoreSummary",
                      BindingFlags.Public | BindingFlags.Instance) != null
                  && typeof(LoseFx).GetMethod("SetScoreSummary",
                      BindingFlags.Public | BindingFlags.Instance) != null,
                "loss UI exposes level-score and submitted-total summary",
                ref failures, report);
            UnityEngine.Object.DestroyImmediate(root);
        }

        static void ValidateSessionTransactionContract(ref int failures, StringBuilder report)
        {
            MethodInfo request = typeof(LuxoddGameService).GetMethod("RequestLossTransaction",
                BindingFlags.Public | BindingFlags.Static);
            Check(request != null && request.GetParameters().Length == 4,
                "Luxodd loss transaction receives separate Continue and Restart callbacks",
                ref failures, report);

            Check(typeof(GameManager).GetMethod("OpenLossSessionOptions", PrivateInstance) != null,
                "GAME OVER opens the Luxodd session options",
                ref failures, report);
            Check(typeof(GameManager).GetMethod("RestartCampaignSession", PrivateInstance) != null,
                "accepted Restart has a clean Level 1 callback",
                ref failures, report);
        }

        static void ValidateFreshLevelContinue(ref int failures, StringBuilder report)
        {
            // Cover the first level, nested/one-way puzzles, mutable terrain and the finale.
            foreach (int levelIndex in new[] { 0, 13, 14, 44, 49 })
                ValidateFreshLevelContinue(levelIndex, ref failures, report);
        }

        static void ValidateFreshLevelContinue(int levelIndex, ref int failures, StringBuilder report)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/Parabox/Prefabs/Levels/Level_{levelIndex + 1}.prefab");
            if (prefab == null)
            {
                Check(false, $"Level {levelIndex + 1} prefab loaded", ref failures, report);
                return;
            }

            ParaboxLevel info = prefab.GetComponent<ParaboxLevel>();
            LevelModel model = LevelParser.Parse(prefab);
            string initialState = MutableState(model);
            int movesToReplay = Mathf.Min(12, info.solution.Length);
            for (int i = 0; i < movesToReplay; i++)
            {
                Vector2Int direction = Direction(info.solution[i]);
                if (!model.TryMovePlayer(direction))
                    throw new InvalidOperationException($"Validation setup route blocked at move {i + 1}.");
            }

            var root = new GameObject("ContinueValidation");
            const string levelKey = "Parabox.Level";
            bool hadLevel = PlayerPrefs.HasKey(levelKey);
            int savedLevel = PlayerPrefs.GetInt(levelKey);
            bool hadPreview = PlayerPrefs.HasKey(GameManager.EditorPreviewLevelKey);
            int savedPreview = PlayerPrefs.GetInt(GameManager.EditorPreviewLevelKey);
            string[] transferNames = { "restartTimerPending", "restartTimerLevel",
                "restartTimerRemaining", "restartTimerCapturedAt", "restartTimerWasArmed",
                "continuedLevelReload", "restartTutorialSuppressionPending",
                "restartTutorialSuppressionLevel", "firstLevelSecondChanceUsed" };
            FieldInfo[] transferFields = transferNames.Select(name => typeof(GameManager).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static)).ToArray();
            object[] savedTransfers = transferFields.Select(field => field.GetValue(null)).ToArray();
            int[] savedScores = Enumerable.Range(0, 50)
                .Select(i => PlayerPrefs.GetInt(ScoreSystem.ScoreKey(i), 0)).ToArray();

            try
            {
                var manager = root.AddComponent<GameManager>();
                Set(manager, "model", model);
                Set(manager, "levelIndex", levelIndex);
                Set(manager, "par", info.par);
                Set(manager, "timedUp", true);
                Set(manager, "outOfMoves", true);
                Set(manager, "lossTransactionRequested", true);
                SetStatic("restartTimerPending", true);
                SetStatic("restartTimerLevel", levelIndex);
                SetStatic("restartTimerRemaining", 0.1f);
                SetStatic("restartTimerCapturedAt", Time.realtimeSinceStartup - 10f);
                SetStatic("restartTimerWasArmed", true);
                SetStatic("firstLevelSecondChanceUsed", true);
                PlayerPrefs.SetInt(levelKey, (levelIndex + 1) % 50);

                // Test the hand-off without loading a scene or calling the payment service.
                // ContinueCurrentSession invokes this preparation, then ReloadCurrentLevel.
                Invoke(manager, "PrepareCurrentLevelContinue");
                Check(PlayerPrefs.GetInt(levelKey, -1) == levelIndex,
                    $"Continue reloads Level {levelIndex + 1}, not Level 1 or the next level",
                    ref failures, report);
                Check(!GetStatic<bool>("restartTimerPending")
                      && GetStatic<int>("restartTimerLevel") == -1
                      && GetStatic<float>("restartTimerRemaining") == 0f
                      && GetStatic<float>("restartTimerCapturedAt") == 0f
                      && !GetStatic<bool>("restartTimerWasArmed"),
                    "expired clock transfer is discarded", ref failures, report);
                Check(GetStatic<bool>("restartTutorialSuppressionPending")
                      && GetStatic<int>("restartTutorialSuppressionLevel") == levelIndex,
                    "the retry skips only this level's tutorial", ref failures, report);
                Check(GetStatic<int>("continuedLevelReload") == levelIndex,
                    "scene reload keeps the existing Luxodd level/session", ref failures, report);
                Check(GetStatic<bool>("firstLevelSecondChanceUsed") && savedScores.SequenceEqual(
                        Enumerable.Range(0, 50).Select(i => PlayerPrefs.GetInt(ScoreSystem.ScoreKey(i), 0))),
                    "Continue preserves campaign score and the consumed Level-1 teaching retry",
                    ref failures, report);

                // The scene's Start uses this exact prefab parser and allowance setup.
                LevelModel fresh = LevelParser.Parse(prefab);
                Check(!ReferenceEquals(model, fresh) && MutableState(fresh) == initialState
                      && fresh.MoveCount == 0 && !fresh.Undo(),
                    "fresh scene restores the original board and clears all moves/undo history",
                    ref failures, report);
                float expectedTime = GameManager.TimeLimitForLevel(levelIndex, info.par);
                Set(manager, "timeLimit", expectedTime);
                Set(manager, "timeLeft", expectedTime);
                Invoke(manager, "RestoreTimerAfterRestart");
                Check(Mathf.Approximately(Get<float>(manager, "timeLeft"), expectedTime),
                    $"fresh timer keeps the full {expectedTime:0.0}s allowance", ref failures, report);
                Check(GameManager.MoveLimitForLevel(levelIndex, info.par) - fresh.MoveCount
                      == GameManager.MoveLimitForLevel(levelIndex, info.par),
                    "fresh move allowance starts at zero moves used", ref failures, report);
            }
            finally
            {
                for (int i = 0; i < transferFields.Length; i++)
                    transferFields[i].SetValue(null, savedTransfers[i]);
                if (hadLevel) PlayerPrefs.SetInt(levelKey, savedLevel);
                else PlayerPrefs.DeleteKey(levelKey);
                if (hadPreview) PlayerPrefs.SetInt(GameManager.EditorPreviewLevelKey, savedPreview);
                else PlayerPrefs.DeleteKey(GameManager.EditorPreviewLevelKey);
                PlayerPrefs.Save();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static string MutableState(LevelModel model)
        {
            var state = new StringBuilder();
            state.Append(model.MoveCount).Append('|').Append(model.latched).Append('|')
                .Append(model.beat).Append('|').Append(model.hasLast).Append('|')
                .Append(model.lastDir).Append('|').Append(model.hasForced).Append('|')
                .Append(model.forcedDir);
            foreach (PEntity entity in model.entities)
                state.Append(";E:").Append(entity.roomId).Append(':').Append(entity.pos)
                    .Append(':').Append(entity.sunk);
            foreach (var key in model.collected.OrderBy(k => k.Item1)
                         .ThenBy(k => k.Item2.x).ThenBy(k => k.Item2.y))
                state.Append(";K:").Append(key.Item1).Append(':').Append(key.Item2);
            foreach (PRoom room in model.rooms.Values.OrderBy(r => r.id))
            {
                foreach (Vector2Int cell in room.filled.OrderBy(CellKey))
                    state.Append(";F:").Append(room.id).Append(':').Append(cell);
                foreach (Vector2Int cell in room.broken.OrderBy(CellKey))
                    state.Append(";B:").Append(room.id).Append(':').Append(cell);
                foreach (Vector2Int cell in room.smashed.OrderBy(CellKey))
                    state.Append(";S:").Append(room.id).Append(':').Append(cell);
            }
            return state.ToString();
        }

        static int CellKey(Vector2Int cell) => cell.y * 1000 + cell.x;

        static Vector2Int Direction(char move)
        {
            switch (move)
            {
                case 'U': return Vector2Int.up;
                case 'D': return Vector2Int.down;
                case 'L': return Vector2Int.left;
                case 'R': return Vector2Int.right;
                default: throw new ArgumentOutOfRangeException(nameof(move), move, "Unknown route move.");
            }
        }

        static RectTransform ChildRect(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return (RectTransform)child.transform;
        }

        static Text TextChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            child.transform.SetParent(parent, false);
            return child.GetComponent<Text>();
        }

        static void Check(bool condition, string message, ref int failures, StringBuilder report)
        {
            if (condition) report.AppendLine("PASS  " + message);
            else
            {
                failures++;
                report.AppendLine("FAIL  " + message);
            }
        }

        static void Set(object target, string field, object value)
            => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);

        static T Get<T>(object target, string field)
            => (T)target.GetType().GetField(field, PrivateInstance).GetValue(target);

        static void SetStatic(string field, object value)
            => typeof(GameManager).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, value);

        static T GetStatic<T>(string field)
            => (T)typeof(GameManager).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        static void Invoke(object target, string method)
            => target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
    }
}
