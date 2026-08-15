using UnityEngine;

namespace Parabox
{
    // The single source of truth for the complete 50-level learning curve. Generation, gameplay,
    // tutorials and campaign QA all read this profile, so a board cannot say "Teach" while the
    // timer, move allowance or physical layout treats it as a mastery test.
    public static class CampaignProgression
    {
        public enum LevelRole
        {
            Teach,
            Practice,
            Experiment,
            Combine,
            Twist,
            Mastery,
            Finale,
        }

        public struct Profile
        {
            public int level;
            public int chapter;
            public string chapterName;
            public string philosophy;
            public LevelRole role;
            public string mechanicFocus;
            public bool introducesMechanic;
            public float rating;
            public int wallBudget;
            public int directionalBudget;
            public int hazardBudget;
            public int moveSlack;
        }

        static readonly int[] RatingLevels = { 1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50 };
        static readonly float[] RatingValues = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 8.5f, 9f, 10f };

        static readonly string[] ChapterNames =
        {
            "Foundations", "Systems", "Synergy", "Mastery", "Recursion"
        };

        static readonly string[] ChapterPhilosophies =
        {
            "Learn one readable rule at a time", "Control state and coupled pieces",
            "Combine familiar rules", "Solve multi-step dependencies", "Plan across nested spaces"
        };

        // One concise design contract for every board in final campaign order. These are not UI
        // flavour strings: the campaign validator serializes and checks them on every prefab.
        // The first forty boards establish the complete movement vocabulary. Chapter V then uses
        // all ten slots for a dedicated recursion curriculum, from one readable chamber to the
        // five-room extraction finale.
        static readonly string[] MechanicFocus =
        {
            "Read a one-way route around an ivory pillar", "Reuse the arrow and approach cargo from the useful side",
            "Swim through deep water while keeping cargo on dry ground", "Park cargo on a button before crossing its gate",
            "Line up cargo and sacrifice it to break the cracked rock", "Use an updraft without losing the required route",
            "Stop sliding cargo on its intended cell", "Bridge the trench, cross the ice and preserve the return route",
            "Use sticky-floor momentum without overshooting", "Commit cargo to the cage only after its route is ready",

            "Move opposite the mirror and finish both targets", "Collect a pearl before crossing its lock",
            "Plan the magnetic pull before every move", "Coordinate the diver and echo through asymmetric wall banks",
            "Push from stable ground because sand gives no purchase", "Place locking cargo only when it can remain solved",
            "Break, toggle and cross the latch in the correct order", "Coordinate the diver and mirror around a cage",
            "Sequence a light gate and a heavy cargo plate", "Assign both coloured crates without blocking their shared sorting space",

            "Coordinate the diver and echo around a cage", "Turn cargo onto the button, cross its gate, then toggle the latch",
            "Launch over the trench, then complete two cargo deliveries", "Reorient one crate around a pillar while sequencing two deliveries",
            "Break the wall, toggle the latch and charge the boulder onto its target", "Turn cargo onto the button, collect the pearl and ride the current through both locks",
            "Combine echo, cage, sand and sticky momentum in a mastery route", "Route cargo around the kelp bank to hold the exit gate open",
            "Assign the coloured delivery while the other crate holds the heavy plate", "Spend two crates to break two rocks in the only safe order",

            "Use the cracked bridge for delivery, then take the geyser-assisted one-way return", "Turn cargo around the wall, hold the button and take the arrow detour",
            "Drop one crate onto the button, then turn and deliver the second", "Send cargo through the narrow gap, collect the pearl and return through its held gate",
            "Collect both pearls, hold the button and cross the lock-and-gate stack", "Build separate horizontal and vertical run-ups before opening the pearl lock",
            "Collect the pearl, unlock the dry cargo channel and finish from the swimming side", "Hold the gate, toggle the latch and lock the lower delivery",
            "Send slick cargo through ice and a narrow gap into the gravity chute", "Hold the gate, then use the side loop to control two pulse barriers",

            "Enter a pinned room and exit through a different edge", "Push a room into its socket before entering it",
            "Extract cargo outward through two room boundaries", "Send cargo inward, finish deep, then backtrack outside",
            "Reposition a child room and re-enter it from another side", "Turn deep cargo while relaying it across three boundaries",
            "Move cargo between sibling rooms through the outer sorting bay", "Choose the child room's exit position before extracting cargo",
            "Re-enter from a new side and carry cargo through four nested boundaries", "Side-dock a deep room, extract an ordered convoy and relay it across sibling branches"
        };

        // A new rule is demonstrated before it becomes part of later combination boards. Keep this
        // table in lockstep with MechanicCatalog's prefab audit: it is also serialized onto each
        // level so editor tooling can detect accidental curriculum drift.
        static readonly bool[] IntroducesMechanic =
        {
            true, true, true, true, true, true, true, true, true, true,
            true, true, true, true, true, true, true, false, true, true,
            false, false, true, false, true, true, false, true, false, false,
            true, false, true, true, false, false, false, false, false, true,
            true, false, true, false, false, true, false, false, false, false
        };

        public static Profile ForLevel(int levelIndex)
        {
            int index = Mathf.Clamp(levelIndex, 0, 49);
            int chapter = index / 10;
            int step = index % 10;
            return new Profile
            {
                level = index + 1,
                chapter = chapter + 1,
                chapterName = ChapterNames[chapter],
                philosophy = ChapterPhilosophies[chapter],
                role = RoleFor(index),
                mechanicFocus = MechanicFocus[index],
                introducesMechanic = IntroducesMechanic[index],
                rating = RatingFor(index),

                // The authored mechanic relationships are the difficulty. Do not procedurally
                // paint extra walls, arrows or hazards onto a solved board merely because its
                // number is higher; that creates clutter rather than better decisions.
                wallBudget = 0,
                directionalBudget = 0,
                hazardBudget = 0,

                // Assistance is removed in short, measured bands instead of once per chapter.
                // Undo refunds moves, so from Level 23 onward the exact proven budget rewards
                // planning without making experimentation irreversible or unfair.
                moveSlack = index >= 40 ? 3
                    : index < 3 ? 4
                    : index < 8 ? 3
                    : index < 14 ? 2
                    : index < 22 ? 1
                    : 0,
            };
        }

        public static float RatingFor(int levelIndex)
        {
            int level = Mathf.Clamp(levelIndex + 1, 1, 50);
            for (int i = 1; i < RatingLevels.Length; i++)
            {
                if (level > RatingLevels[i]) continue;
                float t = Mathf.InverseLerp(RatingLevels[i - 1], RatingLevels[i], level);
                return Mathf.Lerp(RatingValues[i - 1], RatingValues[i], t);
            }
            return RatingValues[RatingValues.Length - 1];
        }

        public static float TimeLimit(int levelIndex, int par)
        {
            int level = Mathf.Clamp(levelIndex + 1, 1, 50);

            // A predictable arcade curve: Level 2 starts at exactly 20 seconds and Chapters I-IV
            // gain one second per board. Recursive boards need time to read each newly entered
            // coordinate space, so Chapter V also scales from the solver-proven route length. This
            // prevents a deep but valid solve from timing out while the player is still planning.
            if (level <= 2) return 20f;
            if (level <= 40) return 20f + (level - 2);
            int chapterStep = level - 41;
            return Mathf.Max(70f + chapterStep * 2f, par * 1.5f + 22f);
        }

        public static string TutorialLine(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex, 0, 49) / 10)
            {
                case 1: return "Terrain changes what a move does. Read the whole route before committing.";
                case 2: return "Rules now interact. Plan their order before making the first move.";
                case 3: return "One move may affect several pieces. Track the full board state.";
                case 4: return "Rooms are movable objects. Pin one to enter it, then plan how cargo crosses every boundary.";
                default: return "Move one tile at a time and reach the bright player target.";
            }
        }

        // A one-line, non-spoiling briefing for the first appearance of a rule. Chapter openers
        // still receive the full cinematic demonstration; these lines cover mechanics introduced
        // between chapter boundaries. They explain cause and effect, never the puzzle's solution.
        public static string MechanicTutorialLine(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex, 0, 49))
            {
                case 0:  return "ONE-WAY  Follow the arrow and route around the ivory pillar.";
                case 1:  return "PUSH  The arrow remains. Get behind the amber cargo and push it away from you.";
                case 2:  return "DEEP WATER  It carries the diver, but cargo will sink.";
                case 3:  return "BUTTON + GATE  Leave cargo on the button to hold its matching gate open.";
                case 4:  return "BREAKABLE ROCK  Push cargo into the cracked rock to open the route.";
                case 5:  return "UPDRAFT  Cargo rises until another cell stops it.";
                case 6:  return "SLIDING CARGO  Icy cargo keeps moving until a solid cell stops it.";
                case 7:  return "BRIDGE + ICE  Fill the trench with cargo, then use the frozen crossing.";
                case 8:  return "STICKY FLOOR  Your next move repeats the previous direction.";
                case 9:  return "CAGE  A crate can enter, but it cannot leave.";
                case 10: return "MIRROR  The second diver moves opposite to you.";
                case 11: return "PEARL + LOCK  Collect the pearl before crossing the gold lock.";
                case 12: return "MAGNET  Aligned cargo is pulled after every move.";
                case 13: return "ECHO  The second diver copies every move; finish both positions together.";
                case 14: return "SAND  You cannot push cargo while standing on it.";
                case 15: return "LOCKING CARGO  Once delivered, this crate cannot move again.";
                case 16: return "TOGGLE + LATCH  Activate the switch before crossing its matching latch.";
                case 18: return "HEAVY PLATE  The striped heavy gate opens only while cargo holds its plate.";
                case 19: return "COLOUR CARGO  Match each coloured crate to its own target.";
                case 22: return "GEYSER  Ride the launch across the trench, then finish the route.";
                case 24: return "BOULDER  Move in the same direction first to build a pushing run-up.";
                case 25: return "CURRENT  Anything that stops on the current is carried in its direction.";
                case 27: return "KELP  The diver can pass through it, but cargo cannot.";
                case 30: return "CRACKED FLOOR  It collapses after you leave, so plan the return first.";
                case 32: return "GRAVITY WELL  Aligned cargo is pulled one cell after every move.";
                case 33: return "NARROW GAP  Cargo fits through; the diver must find another route.";
                case 39: return "PULSE  The barrier advances through a three-beat open-and-closed cycle.";
                case 40: return "NESTED BOARD  A room is also a box. When it cannot be pushed, move into it.";
                case 42: return "MULTI-STAGE RECURSION  Cargo can cross room boundaries in either direction.";
                case 45: return "CHAMBER CHAIN  Track the same cargo across three connected room boundaries.";
                default: return string.Empty;
            }
        }

        static LevelRole RoleFor(int index)
        {
            int safe = Mathf.Clamp(index, 0, 49);
            int step = safe % 10;
            if (safe == 49) return LevelRole.Finale;
            if (step == 0) return LevelRole.Teach;
            if (step <= 2) return LevelRole.Practice;
            if (step <= 4) return LevelRole.Experiment;
            if (step <= 6) return LevelRole.Combine;
            if (step == 7) return LevelRole.Twist;
            return LevelRole.Mastery;
        }
    }
}
