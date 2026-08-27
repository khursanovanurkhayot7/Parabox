using UnityEngine;

namespace Parabox
{
    // Persistent, non-farmable puzzle scoring. Each level keeps only its highest result, and the
    // displayed total is the sum of those personal bests rather than a currency that can be farmed.
    public static class ScoreSystem
    {
        const string ScorePrefix = "Parabox.Score.";
        const string ScoreVersionKey = "Parabox.Score.Version";
        // Increment whenever the stored score scale changes so existing totals migrate once.
        public const int CurrentVersion = 3;

        public struct Breakdown
        {
            public int maxScore;
            public int completionPoints;
            public int movePoints;
            public int speedPoints;
            public int noUndoPoints;
            public int runScore;
        }

        public struct Award
        {
            public int runScore;
            public int gained;
            public int levelBest;
            public int total;
            public bool newBest;
        }

        public static string ScoreKey(int level) => ScorePrefix + level;

        // Level 1 = 100, Level 2 = 120, and every following level is worth twenty more.
        public static int MaxPoints(int level) => 100 + Mathf.Max(0, level) * 20;

        public static int TotalMaximum(int levelCount)
        {
            int count = Mathf.Max(0, levelCount);
            long total = (long)count * 100L + (long)count * (count - 1) * 10L;
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        // Every point has a visible reason: 40% completion, 30% move efficiency, 20% speed and
        // 10% for a clean solve without Undo.
        public static Breakdown Calculate(int level, int par, int moves, int moveLimit,
            float secondsLeft, float timeLimit, int undoCount)
        {
            int maximum = MaxPoints(level);
            int completionPool = Mathf.RoundToInt(maximum * 0.40f);
            int movePool = Mathf.RoundToInt(maximum * 0.30f);
            int speedPool = Mathf.RoundToInt(maximum * 0.20f);
            int noUndoPool = maximum - completionPool - movePool - speedPool;

            float moveEfficiency;
            if (par > 0 && moveLimit > par)
                moveEfficiency = 1f - Mathf.Clamp01((moves - par) / (float)(moveLimit - par));
            else if (par > 0)
                moveEfficiency = moves <= par ? 1f : 0f;
            else
                moveEfficiency = Mathf.Clamp01((moveLimit - moves) / (float)Mathf.Max(1, moveLimit));

            float speedEfficiency = timeLimit > 0f
                ? Mathf.Clamp01(secondsLeft / timeLimit)
                : 0f;
            int movePoints = Mathf.RoundToInt(movePool * moveEfficiency);
            int speedPoints = Mathf.RoundToInt(speedPool * speedEfficiency);
            int noUndoPoints = undoCount <= 0 ? noUndoPool : 0;
            int score = Mathf.Clamp(completionPool + movePoints + speedPoints + noUndoPoints,
                0, maximum);

            return new Breakdown
            {
                maxScore = maximum,
                completionPoints = completionPool,
                movePoints = movePoints,
                speedPoints = speedPoints,
                noUndoPoints = noUndoPoints,
                runScore = score
            };
        }

        public static string Rating(int score, int maximum)
        {
            float ratio = maximum > 0 ? score / (float)maximum : 0f;
            if (ratio >= 0.999f) return "PERFECT CLEAR";
            if (ratio >= 0.85f) return "AMAZING";
            if (ratio >= 0.70f) return "GREAT JOB";
            if (ratio >= 0.50f) return "LEVEL COMPLETE";
            return "CLEARED — TRY FOR MORE";
        }

        public static Award RecordBest(int level, int runScore, int levelCount)
        {
            EnsureCurrentVersion(levelCount);
            int maximum = MaxPoints(level);
            int previous = Mathf.Clamp(PlayerPrefs.GetInt(ScoreKey(level), 0), 0, maximum);
            int best = Mathf.Max(previous, Mathf.Clamp(runScore, 0, maximum));
            if (best > previous) PlayerPrefs.SetInt(ScoreKey(level), best);

            return new Award
            {
                runScore = runScore,
                gained = best - previous,
                levelBest = best,
                total = Total(levelCount),
                newBest = best > previous
            };
        }

        public static int Total(int levelCount)
        {
            EnsureCurrentVersion(levelCount);
            long total = 0;
            for (int i = 0; i < Mathf.Max(0, levelCount); i++)
                total += Mathf.Clamp(PlayerPrefs.GetInt(ScoreKey(i), 0), 0, MaxPoints(i));
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        public static int Best(int level)
            => Mathf.Clamp(PlayerPrefs.GetInt(ScoreKey(level), 0), 0, MaxPoints(level));

        public static void Reset(int levelCount)
        {
            for (int i = 0; i < Mathf.Max(0, levelCount); i++)
                PlayerPrefs.DeleteKey(ScoreKey(i));
            PlayerPrefs.SetInt(ScoreVersionKey, CurrentVersion);
        }

        // Old thousands-based results cannot be compared directly with the new visible cap.
        // Preserve every positive old result as a completed maximum instead of deleting progress.
        public static int ConvertToCurrentVersion(int score, int sourceVersion, int level = -1)
        {
            score = Mathf.Max(0, score);
            if (sourceVersion < CurrentVersion)
                return score > 0 && level >= 0 ? MaxPoints(level) : score;
            return level >= 0 ? Mathf.Min(score, MaxPoints(level)) : score;
        }

        public static void EnsureCurrentVersion(int levelCount)
        {
            int storedVersion = PlayerPrefs.GetInt(ScoreVersionKey, 1);
            if (storedVersion >= CurrentVersion) return;

            for (int i = 0; i < Mathf.Max(0, levelCount); i++)
            {
                string key = ScoreKey(i);
                if (!PlayerPrefs.HasKey(key)) continue;
                int migrated = ConvertToCurrentVersion(PlayerPrefs.GetInt(key, 0), storedVersion, i);
                PlayerPrefs.SetInt(key, migrated);
            }

            PlayerPrefs.SetInt(ScoreVersionKey, CurrentVersion);
            PlayerPrefs.Save();
        }
    }
}
