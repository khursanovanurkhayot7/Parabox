using System;
using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Reads mechanics from the level prefabs themselves. Chapter tutorials stay at Levels
    // 1/11/21/31/41. Each chapter may schedule only one additional NEW MECHANIC video, at the
    // first level in that chapter which genuinely adds a supported rule. This keeps the chapter
    // contract at one video when there is no new rule and at two videos maximum otherwise.
    public static class MechanicCatalog
    {
        public enum Id
        {
            Navigation,
            Crate,
            OneWay,
            Current,
            Geyser,
            Trench,
            NarrowGap,
            BreakableRock,
            ButtonGate,
            SlidingCargo,
            HeavyPlateGate,
            NestedBoard,
            Ice,
            ToggleLatch,
            Pulse,
            Boulder,
            DeepWater,
            GravityWell,
            Updraft,
            StickyFloor,
            Cage,
            KeyLock,
            Mirror,
            Kelp,
            CrackedFloor,
            Magnet,
            Echo,
            Sand,
            LockingCargo,
            ColourCargo,
            Portal,
            Deflector,
            Swap,
            MultiStageRecursion,
            ChamberChain,
            PlayerBodies,
        }

        public static List<Id> MechanicsIn(GameObject prefab, int levelIndex)
        {
            var result = new List<Id> { Id.Navigation };
            if (prefab == null) return result;

            BoxMarker[] boxes = prefab.GetComponentsInChildren<BoxMarker>(true);
            bool hasCrate = false, hasNested = false;
            bool boulder = false, locking = false, colour = false;
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxMarker box = boxes[i];
                if (box == null) continue;
                if (box.containsRoomId >= 0) hasNested = true;
                else hasCrate = true;
                boulder |= box.boulder;
                locking |= box.locking;
                colour |= box.colour > 0;
            }
            AddIf(result, hasCrate, Id.Crate);
            AddIf(result, boulder, Id.Boulder);
            AddIf(result, locking, Id.LockingCargo);
            AddIf(result, colour, Id.ColourCargo);
            AddIf(result, hasNested, Id.NestedBoard);

            AddIf(result, prefab.GetComponentInChildren<OneWayMarker>(true) != null, Id.OneWay);
            AddIf(result, prefab.GetComponentInChildren<CurrentMarker>(true) != null, Id.Current);
            AddIf(result, prefab.GetComponentInChildren<TrenchMarker>(true) != null, Id.Trench);
            AddIf(result, prefab.GetComponentInChildren<IceMarker>(true) != null, Id.Ice);
            AddIf(result, prefab.GetComponentInChildren<CrackMarker>(true) != null, Id.CrackedFloor);
            AddIf(result, prefab.GetComponentInChildren<PortalMarker>(true) != null, Id.Portal);

            bool lightSwitch = false, heavySwitch = false;
            foreach (SwitchMarker marker in prefab.GetComponentsInChildren<SwitchMarker>(true))
            {
                if (marker == null) continue;
                if (marker.heavy) heavySwitch = true;
                else lightSwitch = true;
            }
            bool lightGate = false, heavyGate = false;
            foreach (GateMarker marker in prefab.GetComponentsInChildren<GateMarker>(true))
            {
                if (marker == null) continue;
                if (marker.heavy) heavyGate = true;
                else lightGate = true;
            }
            AddIf(result, lightSwitch || lightGate, Id.ButtonGate);
            AddIf(result, heavySwitch || heavyGate, Id.HeavyPlateGate);

            bool mirror = false, echo = false;
            int regularPlayers = 0;
            foreach (PlayerMarker marker in prefab.GetComponentsInChildren<PlayerMarker>(true))
            {
                if (marker == null) continue;
                mirror |= marker.isMirror;
                echo |= marker.isEcho;
                if (!marker.isMirror && !marker.isEcho) regularPlayers++;
            }
            AddIf(result, mirror, Id.Mirror);
            AddIf(result, echo, Id.Echo);
            AddIf(result, regularPlayers > 1, Id.PlayerBodies);

            foreach (TerrainMarker marker in prefab.GetComponentsInChildren<TerrainMarker>(true))
            {
                if (marker == null) continue;
                switch (marker.kind)
                {
                    case TerrainKind.Kelp: Add(result, Id.Kelp); break;
                    case TerrainKind.Deep: Add(result, Id.DeepWater); break;
                    case TerrainKind.Key:
                    case TerrainKind.Lock: Add(result, Id.KeyLock); break;
                    case TerrainKind.Geyser: Add(result, Id.Geyser); break;
                    case TerrainKind.Gap: Add(result, Id.NarrowGap); break;
                    case TerrainKind.Gravity: Add(result, Id.GravityWell); break;
                    case TerrainKind.Rock: Add(result, Id.BreakableRock); break;
                    case TerrainKind.Toggle:
                    case TerrainKind.Latch: Add(result, Id.ToggleLatch); break;
                    case TerrainKind.Pulse: Add(result, Id.Pulse); break;
                    case TerrainKind.Updraft: Add(result, Id.Updraft); break;
                    case TerrainKind.Cage: Add(result, Id.Cage); break;
                    case TerrainKind.Deflector: Add(result, Id.Deflector); break;
                    case TerrainKind.Sand: Add(result, Id.Sand); break;
                    case TerrainKind.Magnet: Add(result, Id.Magnet); break;
                    case TerrainKind.Sticky: Add(result, Id.StickyFloor); break;
                    case TerrainKind.Swap: Add(result, Id.Swap); break;
                }
            }

            int rooms = prefab.GetComponentsInChildren<RoomMarker>(true).Length;
            // One inner room teaches entering and exiting. A third room introduces a multi-stage
            // transfer; a fourth is the first real chamber chain, where state must be carried
            // across three boundaries. Keeping these thresholds distinct spaces the lessons out.
            AddIf(result, rooms >= 3, Id.MultiStageRecursion);
            AddIf(result, rooms >= 4, Id.ChamberChain);
            return result;
        }

        // Explicit later checkpoints for mechanics whose authored teaching board used to be their
        // only appearance. Values are zero-based level indices. Each sequence follows
        // Practice -> Combine -> Master; the first appearance remains discovered from the prefab.
        // LevelLayoutRebalancer installs these as real board rules and replays the stored solution
        // before retaining them.
        public static Id[] RehearsalsAt(int levelIndex)
        {
            // Chapter II carries the learned one-way rule into recursive-room puzzles.
            // LevelLayoutRebalancer retains it only after the exact stored route wins with it
            // active, so it is a real route commitment and does not trigger another tutorial.
            if (levelIndex >= 10 && levelIndex <= 19)
                return new[] { Id.OneWay };

            // The last six boards are separate mastery exams, not rotations of one finale. Each
            // receives one different route-validated rule family on top of its five-or-more cargo
            // jobs, recursive rooms, gate hold, one-way commitments and sealed portal exit.
            switch (levelIndex)
            {
                case 44: return new[] { Id.StickyFloor };
                case 45: return new[] { Id.LockingCargo };
                case 46: return new[] { Id.KeyLock };
                case 47: return new[] { Id.CrackedFloor };
                case 48: return new[] { Id.Sand };
                case 49: return new[] { Id.Magnet };
            }

            return Array.Empty<Id>();
        }

        public static List<Id> IntroductionsAt(GameObject[] prefabs, int levelIndex)
        {
            var introduced = new List<Id>();
            if (prefabs == null || levelIndex < 0 || levelIndex >= prefabs.Length) return introduced;

            var seen = new HashSet<Id>();
            for (int i = 0; i < levelIndex; i++)
                foreach (Id id in MechanicsIn(prefabs[i], i)) seen.Add(id);

            foreach (Id id in MechanicsIn(prefabs[levelIndex], levelIndex))
                if (!seen.Contains(id)) introduced.Add(id);
            return introduced;
        }

        // A chapter normally shows one focused NEW MECHANIC example, followed by its chapter
        // tutorial when the first eligible rule is introduced at the opener. Chapter I stages
        // button/gate later as one focused lesson.
        // Only concrete player-facing rules qualify.
        // MultiStageRecursion and ChamberChain are difficulty labels for deeper uses of the same
        // room-box rule, not new controls, so they must not create extra tutorial interruptions.
        // Colour matching is taught by the chapter board itself and likewise is not a separate
        // control tutorial. This keeps Chapter II at its one chapter video.
        // Reverse order lets the most specific qualifying rule represent a chapter's mechanic
        // lesson when one board adds several. Outside Chapter I, later rules in the same chapter
        // do not add more tutorial interruptions.
        public static bool TryGetNewMechanicTutorial(GameObject[] prefabs, int levelIndex,
                                                      out Id tutorial)
        {
            tutorial = default;
            if (prefabs == null || levelIndex < 0 || levelIndex >= prefabs.Length)
                return false;

            // Chapter I is deliberately staged instead of front-loading every rule. The chapter
            // opener teaches movement/pushing and Level 7 introduces the button/gate dependency.
            if (levelIndex < 10)
            {
                List<Id> chapterOneIntroductions = IntroductionsAt(prefabs, levelIndex);
                if (levelIndex == 6 && chapterOneIntroductions.Contains(Id.ButtonGate))
                {
                    tutorial = Id.ButtonGate;
                    return true;
                }
                return false;
            }

            int chapterStart = (levelIndex / 10) * 10;
            for (int earlier = chapterStart; earlier < levelIndex; earlier++)
                if (TryGetEligibleIntroductionAt(prefabs, earlier, chapterStart, out _))
                    return false;

            return TryGetEligibleIntroductionAt(prefabs, levelIndex, chapterStart, out tutorial);
        }

        static bool TryGetEligibleIntroductionAt(GameObject[] prefabs, int levelIndex,
                                                  int chapterStart, out Id tutorial)
        {
            List<Id> introductions = IntroductionsAt(prefabs, levelIndex);
            TryGetChapterTutorial(chapterStart, out Id chapterLesson);
            for (int i = introductions.Count - 1; i >= 0; i--)
            {
                Id candidate = introductions[i];
                if (candidate == Id.Navigation || candidate == chapterLesson)
                    continue;
                if (!HasDedicatedMechanicTutorial(candidate)) continue;
                tutorial = candidate;
                return true;
            }

            tutorial = default;
            return false;
        }

        public static bool HasDedicatedMechanicTutorial(Id mechanic)
        {
            switch (mechanic)
            {
                case Id.Crate:
                case Id.OneWay:
                case Id.DeepWater:
                case Id.ButtonGate:
                case Id.BreakableRock:
                case Id.Updraft:
                case Id.Trench:
                case Id.Ice:
                case Id.StickyFloor:
                case Id.Cage:
                case Id.KeyLock:
                case Id.Magnet:
                case Id.Echo:
                case Id.Sand:
                case Id.LockingCargo:
                case Id.ToggleLatch:
                case Id.HeavyPlateGate:
                case Id.NestedBoard:
                case Id.Portal:
                    return true;
                default:
                    return false;
            }
        }

        public static List<Id> TutorialsAt(GameObject[] prefabs, int levelIndex)
        {
            var lessons = new List<Id>();
            if (TryGetNewMechanicTutorial(prefabs, levelIndex, out Id newMechanic))
                lessons.Add(newMechanic);
            if (TryGetChapterTutorial(levelIndex, out Id chapterTutorial)
                && !lessons.Contains(chapterTutorial))
                lessons.Add(chapterTutorial);
            return lessons;
        }

        public static bool TryGetChapterTutorial(int levelIndex, out Id tutorial)
        {
            switch (levelIndex)
            {
                case 0: tutorial = Id.Navigation; return true;
                case 10: tutorial = Id.NestedBoard; return true;
                case 20: tutorial = Id.NestedBoard; return true;
                case 30: tutorial = Id.NestedBoard; return true;
                case 40: tutorial = Id.ColourCargo; return true;
                default: tutorial = default; return false;
            }
        }

        public static bool IsChapterTutorial(int levelIndex, Id tutorial)
            => TryGetChapterTutorial(levelIndex, out Id expected) && expected == tutorial;

        public static bool IsChapterTutorialCheckpoint(int levelIndex)
            => levelIndex >= 0 && levelIndex < 50 && levelIndex % 10 == 0;

        // Player-facing copy for the clean tutorial at each chapter opener. These describe the
        // purpose-built tutorial board, never the campaign level behind it.
        public static string TutorialBundleName(int levelIndex)
            => TutorialBundleName(levelIndex, Id.Navigation);

        public static string TutorialTitle(int levelIndex, Id tutorial)
        {
            if (levelIndex == 0 && tutorial == Id.Navigation) return "CHAPTER 1 TUTORIAL";
            if (levelIndex == 10 && tutorial == Id.NestedBoard) return "CHAPTER 2 TUTORIAL";
            if (levelIndex == 20 && tutorial == Id.NestedBoard) return "CHAPTER 3  •  CARGO RELAY";
            if (levelIndex == 30 && tutorial == Id.NestedBoard) return "CHAPTER 4  •  ROOM DOCKING";
            if (levelIndex == 40 && tutorial == Id.ColourCargo)
                return "CHAPTER 5 TUTORIAL";
            return "NEW MECHANIC  •  " + DisplayName(tutorial);
        }

        public static string TutorialBundleName(int levelIndex, Id tutorial)
        {
            switch (levelIndex)
            {
                case 0 when tutorial == Id.Navigation:
                    return "MOVE  •  PUSH  •  TWO TARGETS";
                case 10 when tutorial == Id.NestedBoard:
                    return "ROOM INSIDE A BOX  •  ENTER + EXIT  •  CARGO RELAY";
                case 20 when tutorial == Id.NestedBoard:
                    return "CARGO RELAY  •  MOVE + PIN ROOM  •  DEEP TRANSFER";
                case 30 when tutorial == Id.NestedBoard:
                    return "ROOM MODULE  •  DOCK SOCKET  •  SIDE EXIT";
                case 40 when tutorial == Id.ColourCargo:
                    return "ROOMS INSIDE ROOMS  •  CARGO BUTTON  •  ONE-WAY GATE";
                default: return DisplayName(tutorial);
            }
        }

        public static string TutorialBundleLesson(int levelIndex)
            => TutorialBundleLesson(levelIndex, Id.Navigation);

        public static string TutorialBundleLesson(int levelIndex, Id tutorial)
        {
            switch (levelIndex)
            {
                case 0 when tutorial == Id.Navigation:
                    return "Push the crate onto its target, then move the player to the bright target.";
                case 10 when tutorial == Id.NestedBoard:
                    return "Enter the smaller rooms, then carry their cargo back across both room boundaries.";
                case 20 when tutorial == Id.NestedBoard:
                    return "Pin the smaller room, enter it, then carry its coral cargo back across both room boundaries.";
                case 30 when tutorial == Id.NestedBoard:
                    return "Push the room onto its glowing dock. When pinned, enter it and leave through the useful side.";
                case 40 when tutorial == Id.ColourCargo:
                    return "Enter both nested rooms, extract the coral cargo across both boundaries, leave it on the button, then follow the arrow through the opened gate.";
                default: return Lesson(tutorial);
            }
        }

        public static string Signature(IReadOnlyList<Id> ids)
        {
            if (ids == null || ids.Count == 0) return string.Empty;
            var values = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) values[i] = ((int)ids[i]).ToString();
            return string.Join("-", values);
        }

        public static string Lesson(IReadOnlyList<Id> ids)
        {
            if (ids == null || ids.Count == 0) return string.Empty;
            var lines = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string line = Lesson(ids[i]);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            return string.Join("  •  ", lines);
        }

        public static string[] VisualTokens(Id id)
        {
            switch (id)
            {
                case Id.Navigation: return new[] { "Player", "PlayerGoal", "OneWay" };
                case Id.Crate: return new[] { "Box", "BoxGoal" };
                case Id.OneWay: return new[] { "OneWay" };
                case Id.Current: return new[] { "Current" };
                case Id.Geyser: return new[] { "Geyser" };
                case Id.Trench: return new[] { "Pit", "PitFill" };
                case Id.NarrowGap: return new[] { "Gap" };
                case Id.BreakableRock: return new[] { "Rock" };
                case Id.ButtonGate: return new[] { "Button", "Gate" };
                case Id.HeavyPlateGate: return new[] { "Plate", "HeavyGate" };
                case Id.NestedBoard:
                case Id.MultiStageRecursion:
                case Id.ChamberChain: return new[] { "Frame", "Backing" };
                case Id.PlayerBodies: return new[] { "Player", "PlayerGoal" };
                case Id.Ice: return new[] { "Ice", "IceSheen" };
                case Id.ToggleLatch: return new[] { "Toggle", "Latch" };
                case Id.Pulse: return new[] { "Pulse" };
                case Id.Boulder: return new[] { "Boulder" };
                case Id.DeepWater: return new[] { "Deep", "Depth" };
                case Id.GravityWell: return new[] { "Gravity" };
                case Id.Updraft: return new[] { "Updraft" };
                case Id.StickyFloor: return new[] { "Sticky", "AdhesiveRing" };
                case Id.Cage: return new[] { "Cage" };
                case Id.KeyLock: return new[] { "Pearl", "Lock" };
                case Id.Mirror: return new[] { "Mirror" };
                case Id.Kelp: return new[] { "Kelp" };
                case Id.CrackedFloor: return new[] { "Coral", "Rubble" };
                case Id.Magnet: return new[] { "Magnet", "Field" };
                case Id.Echo: return new[] { "Echo" };
                case Id.Sand: return new[] { "Sand" };
                case Id.LockingCargo: return new[] { "Locking" };
                case Id.ColourCargo: return new[] { "Colour", "Color" };
                case Id.Portal: return new[] { "Portal", "PortalGlow" };
                case Id.Deflector: return new[] { "Deflector" };
                case Id.Swap: return new[] { "Swap" };
                default: return Array.Empty<string>();
            }
        }

        public static string DisplayName(Id id)
        {
            switch (id)
            {
                case Id.OneWay: return "ONE-WAY ARROW";
                case Id.ButtonGate: return "BUTTON + GATE";
                case Id.HeavyPlateGate: return "HEAVY PLATE + GATE";
                case Id.NestedBoard: return "ROOM INSIDE A BOX";
                case Id.KeyLock: return "PEARL + LOCK";
                case Id.ToggleLatch: return "TOGGLE + LATCH";
                case Id.ColourCargo: return "COLOURED CARGO";
                case Id.MultiStageRecursion: return "MULTI-STAGE RECURSION";
                case Id.ChamberChain: return "CHAMBER CHAIN";
                case Id.PlayerBodies: return "PLAYER BODIES";
                default: return id.ToString().ToUpperInvariant();
            }
        }

        public static string Lesson(Id id)
        {
            switch (id)
            {
                case Id.Navigation: return "MOVE one step at a time and reach the bright target";
                case Id.Crate: return "PUSH from behind; crates only move away from you";
                case Id.OneWay: return "ARROW cells accept movement only in their shown direction";
                case Id.Current: return "CURRENTS carry anything that stops on them";
                case Id.Geyser: return "GEYSERS launch you over the next cell";
                case Id.Trench: return "TRENCHES need cargo to become a safe bridge";
                case Id.NarrowGap: return "NARROW GAPS accept cargo, but not the diver";
                case Id.BreakableRock: return "BREAK ROCK by pushing cargo into it";
                case Id.ButtonGate: return "BUTTONS hold their matching gates open";
                case Id.HeavyPlateGate: return "HEAVY PLATES open only while cargo holds them";
                case Id.NestedBoard: return "ENTER the smaller board; actions inside affect the outer puzzle";
                case Id.Ice: return "ICE keeps movement sliding until a solid cell stops it";
                case Id.ToggleLatch: return "TOGGLES permanently change their matching latches";
                case Id.Pulse: return "PULSE barriers alternate after every move";
                case Id.Boulder: return "BOULDERS need a run-up in the same direction before the push";
                case Id.DeepWater: return "DEEP WATER carries the diver, but sinks cargo";
                case Id.GravityWell: return "GRAVITY WELLS pull aligned cargo downward";
                case Id.Updraft: return "UPDRAFTS lift cargo until another cell stops it";
                case Id.StickyFloor: return "STICKY FLOOR repeats your last direction on the next move";
                case Id.Cage: return "CAGES accept a crate, but never release it";
                case Id.KeyLock: return "COLLECT the pearl before crossing its gold lock";
                case Id.Mirror: return "MIRRORS move opposite to you; both targets must finish";
                case Id.Kelp: return "KELP lets the diver pass, but blocks cargo";
                case Id.CrackedFloor: return "CRACKED FLOOR collapses after you leave";
                case Id.Magnet: return "MAGNETS pull aligned cargo one cell after every move";
                case Id.Echo: return "ECHOES copy every move; solve both positions together";
                case Id.Sand: return "SAND gives no purchase, so you cannot push while standing on it";
                case Id.LockingCargo: return "LOCKING CARGO cannot move after reaching its target";
                case Id.ColourCargo: return "COLOURED CARGO belongs on the target with the same colour and mark";
                case Id.Portal: return "PORTALS connect matching spaces instantly";
                case Id.Deflector: return "DEFLECTORS turn movement clockwise";
                case Id.Swap: return "SWAP cells trade the objects standing on their pair";
                case Id.MultiStageRecursion: return "RECURSION asks you to plan the inner result before returning outside";
                case Id.ChamberChain: return "CHAMBER CHAINS require an inner solve before the outer route can finish";
                case Id.PlayerBodies: return "PLAYER BODIES can be pushed; place every pink body on a bright player target";
                default: return string.Empty;
            }
        }

        static void AddIf(List<Id> values, bool condition, Id id)
        {
            if (condition) Add(values, id);
        }

        static void Add(List<Id> values, Id id)
        {
            if (!values.Contains(id)) values.Add(id);
        }
    }
}
