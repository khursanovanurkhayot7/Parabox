PARABOX — a Patrick's Parabox inspired puzzle game
===================================================

This project uses URP so it can be built for WebGL (browser).

HOW TO SET UP (one time):
1. Open the project in Unity and wait for scripts to compile.
2. In the top menu click:  Tools > Parabox > Create Everything (Run Once)
3. Done. It sets up the URP render pipeline and creates:
   - Assets/Parabox/Sprites          (procedurally generated rounded sprites)
   - Assets/Parabox/Prefabs          (Floor, Border, Wall, Box, MetaBox, Player, goals)
   - Assets/Parabox/Prefabs/Levels   (Level_1 ... Level_50 prefabs)
   - Assets/Parabox/Scenes           (MainMenu.unity + Game.unity, added to Build Settings)
4. After running it you may DELETE the folder:  Assets/Parabox/Editor
   !!! DO NOT delete Assets/Parabox/Scripts — the scenes and prefabs need it !!!

NOTE: running the wizard is safe to repeat — it regenerates all sprites,
prefabs and scenes each time. If you edited it, just run it again.

HOW TO PLAY:
- Luxodd joystick ...... move in levels / navigate menus
- Black button ......... confirm / select / continue
- Red button ........... undo
- Green button ......... restart level
- Yellow button ........ open level select
- Blue button .......... mute / unmute sound
- Orange button ........ Luxodd system help / overlay (reserved)
- White button ......... back / cancel; exits to Luxodd from the title

DESKTOP TEST CONTROLS:
- WASD / Arrow keys ... move & push boxes
- Z ................... undo
- R ................... restart level
- M ................... mute / unmute sound
- Esc ................. back to menu
- Push boxes onto the orange outline targets.
- Stand on the pink outline target yourself.
- Boxes with a frame contain a whole room: push boxes INTO them,
  or walk into them yourself. Walk past the inside edge to get out again.

PROGRESS:
- Beaten levels show a green check on the menu, and your best (fewest)
  move count is saved per level and shown in the HUD and win screen.
- The level map opens any unlocked level directly.
- Beat all 50 for the "You Win!" ending. Press Del on the menu to reset.

LEVELS: 50 progressively ordered levels. Every board has a stored solver-proven
winning route and a par derived from that route. Campaign order uses a composite
design score: solution depth, direction changes, interacting objects, targets,
mechanic variety, puzzle dependencies and nested rooms all contribute. This keeps
consecutive levels distinct even when two optimal routes have similar lengths.

The player-facing rating follows a continuous authored curve with no plateaus:
  L1  1.0   L5  2.0   L10 3.0   L15 4.0   L20 5.0   L25 6.0
  L30 7.0   L35 8.0   L40 8.5   L45 9.0   L50 10.0

PHYSICAL REBALANCE: Scripts/LevelLayoutRebalancer.cs deterministically adds
chapter-scaled wall formations and directional choke points when a level is
parsed. It protects the stored winning route, so gameplay and level-map
previews stay synchronized and regenerating the prefabs does not erase the
rebalance. The campaign validator also requires every level to receive at
least one physical addition before it can pass.

  Early game    short foundation boards and first terrain rules
  Mid game      gates, cargo behavior and multi-entity puzzles
  Late game     expert movement contracts, nested rooms and dense choke networks

NOTE ON "INFINITY": the famous late-game trick of a box that contains
itself is intentionally NOT included. This engine renders a nested room as
a real child object, so a self-containing box would create an impossible
parenting loop. True infinity needs a different rendering approach and
special movement rules — a separate, much larger project.

BUILDING FOR WEBGL:
1. File > Build Profiles > select Web > Switch Platform (if not already).
2. Click Build (or Build And Run). Upload the output folder to itch.io
   (zip it, mark "This file will be played in the browser") or any web host.

EDITING / ADDING LEVELS:
Level prefabs are plain data: every room is a child object with a RoomMarker,
and walls/boxes/goals/player carry marker components (grid x,y).
Duplicate any Level_N.prefab, edit markers, then add the new prefab to the
GameManager "Level Prefabs" array in the Game scene. (Easier: edit the
LevelDef list in the wizard's Levels() method — plain ASCII grids — and
re-run "Create Everything" to regenerate all level prefabs.)

VALIDATING THE CAMPAIGN:
Before a release—or after changing a level, mechanic, timer or move rule—run:
  Tools > Parabox > Validate Entire Campaign (50 Levels)
The validator parses every shipped prefab, replays its stored solution through
the real gameplay model, checks the move budget, confirms that the final board
wins, verifies the exact 1-10 target curve, and rejects rating or design-score
plateaus between consecutive levels.
