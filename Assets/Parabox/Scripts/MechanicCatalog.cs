using System;
using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Reads mechanics from the level prefabs themselves. This keeps onboarding tied to the real
    // campaign: moving a mechanic to another level also moves its first-appearance lesson, and a
    // later board cannot accidentally be labelled as an introduction to an already-known rule.
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
            bool slick = false, boulder = false, locking = false, colour = false;
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxMarker box = boxes[i];
                if (box == null) continue;
                if (box.containsRoomId >= 0) hasNested = true;
                else hasCrate = true;
                slick |= box.slick;
                boulder |= box.boulder;
                locking |= box.locking;
                colour |= box.colour > 0;
            }
            AddIf(result, hasCrate, Id.Crate);
            AddIf(result, slick, Id.SlidingCargo);
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
            AddIf(result, levelIndex >= 40 && rooms >= 4, Id.ChamberChain);
            return result;
        }

        // Explicit later checkpoints for mechanics whose authored teaching board used to be their
        // only appearance. Values are zero-based level indices. Each sequence follows
        // Practice -> Combine -> Master; the first appearance remains discovered from the prefab.
        // LevelLayoutRebalancer installs these as real board rules and replays the stored solution
        // before retaining them.
        public static Id[] RehearsalsAt(int levelIndex)
        {
            // The campaign is authored and solver-proven as a whole. Injecting a one-off tile into
            // a route after the fact would turn a designed puzzle into an unexplained rule test.
            // Rehearsals must be added to authored definitions and proven there.
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

        // The campaign still teaches every rule at its first real appearance. In addition, each
        // ten-level chapter opens with one short gameplay-style refresher so a returning player is
        // never dropped into a new world without a readable demonstration. These are presentation
        // mini-boards only; they do not load or replay the campaign level.
        public static List<Id> TutorialsAt(GameObject[] prefabs, int levelIndex)
        {
            var lessons = new List<Id>();
            switch (levelIndex)
            {
                case 0:  Add(lessons, Id.Navigation); break;   // Level 1
                case 10: Add(lessons, Id.Mirror); break;       // Level 11
                case 20: Add(lessons, Id.Echo); break;         // Level 21
                case 30: Add(lessons, Id.CrackedFloor); break; // Level 31
                case 40: Add(lessons, Id.NestedBoard); break;  // Level 41
            }

            foreach (Id introduced in IntroductionsAt(prefabs, levelIndex))
                Add(lessons, introduced);
            return lessons;
        }

        public static bool IsChapterTutorialCheckpoint(int levelIndex)
            => levelIndex >= 0 && levelIndex < 50 && levelIndex % 10 == 0;

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
                case Id.SlidingCargo: return new[] { "Slick" };
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
                case Id.SlidingCargo: return "SLIDING CARGO continues until something stops it";
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
                case Id.ColourCargo: return "COLOURED CARGO belongs on the matching target";
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
