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
        public const int CurrentVersion = 5;
        public const int StartingScore = 100;
        public const int ScoreIncreasePerLevel = 20;
        public const int TimeGraceSeconds = 10;
        public const int TimePenaltyPerSecond = 3;
        public const int MovePenaltyPerExtraMove = 10;

        public struct Breakdown
        {
            public int maxScore;
            public int completionPoints;
            public int movePoints;
            public int speedPoints;
            public int noUndoPoints;
            public int timePenalty;
            public int movePenalty;
            public int targetMoves;
            public int usedMoves;
            public int elapsedSeconds;
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

        // Harder levels carry a larger visible score budget: Level 1 starts at 100 and every
        // following level adds 20. The premium HUD and the awarded result share this value.
        public static int MaxPoints(int level)
            => StartingScore + Mathf.Max(0, level) * ScoreIncreasePerLevel;

        public static int TotalMaximum(int levelCount)
        {
            int count = Mathf.Max(0, levelCount);
            long total = (long)count * StartingScore
                + (long)count * (count - 1) / 2L * ScoreIncreasePerLevel;
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        // The live HUD and the final award call this same method, so the player never sees one
        // score during play and a different score on the win/leaderboard flow.
        public static Breakdown Calculate(int level, int par, int moves, int moveLimit,
            float secondsLeft, float timeLimit, int undoCount)
        {
            int maximum = MaxPoints(level);
            int targetMoves = par > 0 ? par : Mathf.Max(1, moveLimit);
            // An undone step was still played. Including one spent move per Undo prevents score
            // farming while keeping the visible current board move counter unchanged.
            int usedMoves = Mathf.Max(0, moves) + Mathf.Max(0, undoCount);
            int extraMoves = Mathf.Max(0, usedMoves - targetMoves);
            int movePenalty = extraMoves * MovePenaltyPerExtraMove;

            float elapsed = timeLimit > 0f
                ? Mathf.Max(0f, timeLimit - Mathf.Max(0f, secondsLeft)) : 0f;
            int elapsedSeconds = Mathf.Max(0, Mathf.FloorToInt(elapsed));
            int chargedSeconds = Mathf.Max(0, elapsedSeconds - TimeGraceSeconds);
            int timePenalty = chargedSeconds * TimePenaltyPerSecond;
            int score = Mathf.Clamp(maximum - timePenalty - movePenalty, 0, maximum);

            return new Breakdown
            {
                maxScore = maximum,
                completionPoints = maximum,
                movePoints = -movePenalty,
                speedPoints = -timePenalty,
                noUndoPoints = 0,
                timePenalty = timePenalty,
                movePenalty = movePenalty,
                targetMoves = targetMoves,
                usedMoves = usedMoves,
                elapsedSeconds = elapsedSeconds,
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
            if (sourceVersion == 4 && level >= 0)
            {
                // Version 4 compressed every level to a 100-point scale. Restore the same earned
                // percentage on the level-specific scale instead of gifting or deleting progress.
                return Mathf.Clamp(Mathf.RoundToInt(score / (float)StartingScore
                    * MaxPoints(level)), 0, MaxPoints(level));
            }
            if (sourceVersion == 3 && level >= 0)
                return Mathf.Clamp(score, 0, MaxPoints(level));
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
