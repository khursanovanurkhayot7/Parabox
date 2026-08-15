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
        public const int CurrentVersion = 2;

        public struct Award
        {
            public int runScore;
            public int gained;
            public int levelBest;
            public int total;
            public bool newBest;
        }

        public static string ScoreKey(int level) => ScorePrefix + level;

        // Faster clears and fewer moves score higher. The chapter multiplier makes later puzzles
        // worth more without making early levels irrelevant. Version 2 halves the original award
        // scale so full-campaign totals stay readable while preserving the exact same ranking.
        public static int Calculate(int level, int par, int moves, int moveLimit, float secondsLeft)
        {
            int chapter = Mathf.Clamp(level / 10 + 1, 1, 5);
            int movesRemaining = Mathf.Max(0, moveLimit - moves);
            int secondsRemaining = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));

            int baseScore = 250 * chapter;
            int moveBonus = movesRemaining * 50 * chapter;
            int timeBonus = secondsRemaining * 5 * chapter;
            int perfectBonus = par > 0 && moves <= par ? 250 * chapter : 0;
            return baseScore + moveBonus + timeBonus + perfectBonus;
        }

        public static Award RecordBest(int level, int runScore, int levelCount)
        {
            EnsureCurrentVersion(levelCount);
            int previous = PlayerPrefs.GetInt(ScoreKey(level), 0);
            int best = Mathf.Max(previous, Mathf.Max(0, runScore));
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
                total += Mathf.Max(0, PlayerPrefs.GetInt(ScoreKey(i), 0));
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        public static void Reset(int levelCount)
        {
            for (int i = 0; i < Mathf.Max(0, levelCount); i++)
                PlayerPrefs.DeleteKey(ScoreKey(i));
            PlayerPrefs.SetInt(ScoreVersionKey, CurrentVersion);
        }

        // Converts scores restored from cloud data created before the lower scoring scale.
        public static int ConvertToCurrentVersion(int score, int sourceVersion)
        {
            score = Mathf.Max(0, score);
            if (sourceVersion >= CurrentVersion) return score;
            return Mathf.RoundToInt(score * 0.5f);
        }

        public static void EnsureCurrentVersion(int levelCount)
        {
            int storedVersion = PlayerPrefs.GetInt(ScoreVersionKey, 1);
            if (storedVersion >= CurrentVersion) return;

            for (int i = 0; i < Mathf.Max(0, levelCount); i++)
            {
                string key = ScoreKey(i);
                if (!PlayerPrefs.HasKey(key)) continue;
                int migrated = ConvertToCurrentVersion(PlayerPrefs.GetInt(key, 0), storedVersion);
                PlayerPrefs.SetInt(key, migrated);
            }

            PlayerPrefs.SetInt(ScoreVersionKey, CurrentVersion);
            PlayerPrefs.Save();
        }
    }
}
