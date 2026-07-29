using UnityEngine;

namespace Parabox
{
    // The batch-B terrain types. These are plain per-cell facts with no extra data, so they share
    // one marker with a kind rather than five near-identical components.
    public enum TerrainKind
    {
        Kelp,     // a thicket only the diver slips through
        Deep,     // the diver swims over; a crate pushed in is lost for good
        Key,      // a pearl the diver collects by coming to rest on it
        Lock,     // solid until every pearl on the board has been collected
        Geyser,   // landing here launches you two cells on, over whatever is in between
        Gap,      // too narrow for a diver; a crate slides straight through it
        Gravity,  // a crate left standing here sinks until something holds it up
        Rock,     // a crate shoved into it shatters both; a diver just bounces off
        Toggle,   // step on one and every latch gate flips, and stays flipped
        Latch,    // solid until a toggle has been stepped on
        Pulse,    // open on every other move
        Updraft,  // cargo left standing here floats upward
        Cage,     // a crate can be pushed in, but never back out
        Deflector,// entering one turns you a quarter turn clockwise and sends you on
        Sand,     // no purchase: you may walk it, but you may not push from it
        Magnet,   // drags crates on its row/column one cell closer after every move
        Sticky,   // leaving one forces your next move to repeat that direction
        Swap,     // step on one and you trade places with whatever stands on its twin
    }

    public class TerrainMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public TerrainKind kind;
    }
}
