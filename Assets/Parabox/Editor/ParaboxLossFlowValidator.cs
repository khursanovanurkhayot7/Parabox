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
            var report = new StringBuilder();
            int failures = 0;

            ValidateOverlay(ref failures, report);
            ValidateExactStateContinue(ref failures, report);
            ValidateSessionTransactionContract(ref failures, report);

            string result = failures == 0
                ? "LOSS FLOW VALIDATION PASSED — CONTINUE preserves state and RESTART begins a clean run."
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

        static void ValidateExactStateContinue(ref int failures, StringBuilder report)
        {
            const int levelIndex = 44; // nested Switch Chamber exercises the richer state space
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/Parabox/Prefabs/Levels/Level_{levelIndex + 1}.prefab");
            if (prefab == null)
            {
                Check(false, "Level 45 prefab loaded", ref failures, report);
                return;
            }

            ParaboxLevel info = prefab.GetComponent<ParaboxLevel>();
            LevelModel model = LevelParser.Parse(prefab);
            int movesToReplay = Mathf.Min(12, info.solution.Length);
            for (int i = 0; i < movesToReplay; i++)
            {
                Vector2Int direction = Direction(info.solution[i]);
                if (!model.TryMovePlayer(direction))
                    throw new InvalidOperationException($"Validation setup route blocked at move {i + 1}.");
            }

            string before = MutableState(model);
            var root = new GameObject("ContinueValidation");
            var manager = root.AddComponent<GameManager>();
            manager.levelLabel = TextChild(root.transform, "LevelLabel");
            manager.movesLabel = TextChild(root.transform, "MovesLabel");

            Set(manager, "model", model);
            Set(manager, "levelIndex", levelIndex);
            Set(manager, "par", info.par);
            Set(manager, "timeLimit", 1f);
            Set(manager, "timeLeft", 0f);
            Set(manager, "moveLimit", model.MoveCount);
            Set(manager, "timedUp", true);
            Set(manager, "outOfMoves", true);
            Set(manager, "lossTransactionRequested", true);

            Invoke(manager, "ContinueCurrentSession");

            float expectedTime = GameManager.TimeLimitForLevel(levelIndex, info.par);
            float actualTime = Get<float>(manager, "timeLeft");
            int actualMoveLimit = Get<int>(manager, "moveLimit");
            Check(ReferenceEquals(model, Get<LevelModel>(manager, "model"))
                  && before == MutableState(model),
                "Continue preserved the exact live puzzle model and undo history", ref failures, report);
            Check(Mathf.Approximately(actualTime, expectedTime)
                  && Mathf.Approximately(Get<float>(manager, "timeLimit"), expectedTime),
                $"timer refilled to the full {expectedTime:0.0}s", ref failures, report);
            Check(actualMoveLimit - model.MoveCount == GameManager.MoveLimitForLevel(levelIndex, info.par),
                $"moves refilled to the full {GameManager.MoveLimitForLevel(levelIndex, info.par)}",
                ref failures, report);
            Check(!Get<bool>(manager, "timedUp") && !Get<bool>(manager, "outOfMoves")
                  && !Get<bool>(manager, "lossTransactionRequested"),
                "terminal flags cleared only after Continue", ref failures, report);
            Check(Get<bool>(manager, "countdownArmed"),
                "gameplay timer resumes immediately after Continue", ref failures, report);

            UnityEngine.Object.DestroyImmediate(root);
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

        static void Invoke(object target, string method)
            => target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
    }
}
