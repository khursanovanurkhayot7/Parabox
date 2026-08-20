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
            "Foundations", "Inside the Box", "Room Transfers", "Room Maneuvers", "Recursive Mastery"
        };

        static readonly string[] ChapterPhilosophies =
        {
            "Master four readable foundation rules", "Enter rooms that are also boxes",
            "Transfer cargo through rooms and choose useful exits",
            "Dock, enter and re-enter rooms as puzzle pieces",
            "Master deep room chains, sibling transfers and multi-stage extraction"
        };

        // One concise design contract for every board in final campaign order. These are not UI
        // flavour strings: the campaign validator serializes and checks them on every prefab.
        // Chapter II introduces room-box traversal gently. Chapter III transfers cargo across
        // room boundaries, Chapter IV makes rooms themselves the objects to reposition, and
        // Chapter V combines both ideas across the deepest recursive chains in the campaign.
        static readonly string[] MechanicFocus =
        {
            "Read a bent one-way route around an ivory pillar",
            "Approach cargo from below, turn it onto its goal and return separately",
            "Stop sliding cargo at the far wall, then take the lower return path",
            "Solve two opposed deliveries before returning to the player target",
            "Keep one crate on the button while delivering another beyond its gate",
            "Complete an ordinary delivery, slide a second crate onto the button, then cross its opened gate",
            "Hold the button with one crate, send the other through the gate, then recover and deliver both",
            "Complete three deliveries from three approach sides around the central wall and one-way crossing",
            "Hold the gate with ordinary cargo while ordinary and sliding cargo cross the divider",
            "Open the circuit with one crate, route sliding cargo through it, then finish three deliveries and exit",

            "Enter one fixed room, solve its bent inner route and leave through a different edge",
            "Exit a fixed room, circle outside and re-enter it from another side before finishing",
            "Pin one movable room, enter it and leave through the opposite boundary",
            "Push a movable room twice, pin it against a wall, then enter from its useful side",
            "Dock a movable room on its socket, enter it and use its far exit to reach the target",
            "Enter from one edge, extract through another and return by a new approach",
            "Turn a movable room before its useful opening can line up with the route",
            "Dock the room, enter it, leave, then re-enter from its new position",
            "Carry the same object across an inner boundary and finish outside",
            "Dock the room, leave through its far side, then re-enter from the new position",

            "Transfer cargo through the room before placing the room itself",
            "Move the room first, then bring its cargo to the outer target",
            "Cross between differently shaped rooms without losing the return route",
            "Choose the useful exit side before extracting the cargo",
            "Move a room around a corner, pin it and use its new entry side",
            "Reverse the normal flow by sending cargo into the inner room",
            "Relay one object through two connected rooms in the correct order",
            "Enter a room inside another room and return through both boundaries",
            "Turn the inner delivery before the surrounding room can be moved",
            "Reverse the transfer by sending ordinary cargo into the inner room",

            "Choose the only exit side that preserves the later docking move",
            "Extract cargo outward, then reposition its former room for the exit",
            "Solve one nested branch before crossing into its sibling branch",
            "Route cargo around an inner pillar before aligning the parent room",
            "Travel through two connected rooms without losing the outer route",
            "Send cargo inward first, then bring it back through a different side",
            "Relay one cargo piece outward across two boundaries, then finish outside",
            "Carry one object through three connected room scales",
            "Dock three room pieces without blocking the next approach",
            "Push the child room to its stop, enter it and extract from the new side",

            "Carry cargo through two nested rooms, then dock the parent to finish",
            "Plan a long inner relay whose return path changes after each transfer",
            "Turn cargo around the deepest corner before rebuilding the outer route",
            "Relay cargo across four boundaries while preserving the exit path",
            "Extract from one branch and deliver into a different sibling branch",
            "Choose the room position that creates the correct final exit side",
            "Move back through a nested pocket and recover cargo from a new approach",
            "Push, pin and re-enter across five spaces before extracting the cargo",
            "Coordinate a branching five-room chain with ordered cargo transfers",
            "Complete the final four-room recursion where every boundary crossing matters"
        };

        // This serialized flag marks the five guaranteed chapter-tutorial checkpoints. Focused
        // NEW MECHANIC videos are derived from the actual prefab by MechanicCatalog and therefore
        // do not need a fragile second hard-coded table here.
        static readonly bool[] IntroducesMechanic =
        {
            true, false, false, false, false, false, false, false, false, false,
            true, false, false, false, false, false, false, false, false, false,
            true, false, false, false, false, false, false, false, false, false,
            true, false, false, false, false, false, false, false, false, false,
            true, false, false, false, false, false, false, false, false, false
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

                // Every campaign puzzle gives the player exactly three recovery moves beyond its
                // authored route. This keeps the rule predictable: a 14-move solution always has
                // a 17-move allowance, independent of chapter or level number.
                moveSlack = 3,
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

            // A predictable arcade curve: Level 2 starts at exactly 20 seconds. Multi-delivery and
            // recursive boards also scale from their authored route length so the player has time
            // to read the board and plan instead of losing to the clock while learning.
            if (level <= 2) return 20f;
            if (level <= 10)
                return Mathf.Max(20f + (level - 2), par * 1.35f + 12f);
            if (level <= 20) return Mathf.Max(20f + (level - 2), par * 1.5f + 22f);
            if (level <= 30) return Mathf.Max(20f + (level - 2), par * 1.5f + 20f);
            if (level <= 40) return Mathf.Max(20f + (level - 2), par * 1.5f + 20f);
            int chapterStep = level - 41;
            return Mathf.Max(76f + chapterStep * 3f, par * 1.65f + 30f);
        }

        public static string TutorialLine(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex, 0, 49) / 10)
            {
                case 1: return "A room can also be a box. If it cannot move, enter it and leave through another edge.";
                case 2: return "Carry cargo through room boundaries. Choose the exit side before you start pushing.";
                case 3: return "A room is also a movable piece. Pin it to enter, leave from another side, then dock it.";
                case 4: return "Combine everything: move, pin and enter rooms, then relay cargo through several connected spaces.";
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
                case 2:  return "SLIDING CARGO  A pushed sliding crate continues until a wall stops it.";
                case 4:  return "BUTTON + GATE  Leave cargo on the button to hold its matching gate open.";
                case 10: return "NESTED BOARD  A room is also a box. When it cannot move, the player enters it.";
                case 11: return "PEARL + LOCK  Collect the pearl before crossing the gold lock.";
                case 12: return "MAGNET  Aligned cargo is pulled after every move.";
                case 13: return "ECHO  The second diver copies every move; finish both positions together.";
                case 14: return "SAND  You cannot push cargo while standing on it.";
                case 15: return "LOCKING CARGO  Once delivered, this crate cannot move again.";
                case 16: return "TOGGLE + LATCH  Activate the switch before crossing its matching latch.";
                case 22: return "GEYSER  Ride the launch across the trench, then finish the route.";
                case 24: return "BOULDER  Move in the same direction first to build a pushing run-up.";
                case 27: return "KELP  The diver can pass through it, but cargo cannot.";
                case 30: return "MOVABLE ROOM  If the room cannot be pushed, you enter it. Exit from another side to reposition it.";
                case 32: return "GRAVITY WELL  Aligned cargo is pulled one cell after every move.";
                case 33: return "NARROW GAP  Cargo fits through; the diver must find another route.";
                case 39: return "PULSE  The barrier advances through a three-beat open-and-closed cycle.";
                case 18: return "MULTI-STAGE RECURSION  One move can carry the player or cargo between connected rooms.";
                case 29: return "CHAMBER CHAIN  Track the same cargo through every connected coordinate space.";
                case 40: return "COLOUR CARGO  Match coral, sky and green crates to targets with the same colour and mark.";
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
