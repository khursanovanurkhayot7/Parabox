PARABOX — a Patrick's Parabox inspired puzzle game
===================================================

This project uses URP so it can be built for WebGL (browser).

HOW TO SET UP (one time):
1. Open the project in Unity and wait for scripts to compile.
2. In the top menu click:  Tools > Parabox > Create Everything (Run Once)
3. Done. It sets up the URP render pipeline and creates:
   - Assets/Parabox/Sprites          (procedurally generated rounded sprites)
   - Assets/Parabox/Prefabs          (Floor, Border, Wall, Box, MetaBox, Player, goals)
   - Assets/Parabox/Prefabs/Levels   (Level_1 ... Level_30 prefabs)
   - Assets/Parabox/Scenes           (MainMenu.unity + Game.unity, added to Build Settings)
4. After running it you may DELETE the folder:  Assets/Parabox/Editor
   !!! DO NOT delete Assets/Parabox/Scripts — the scenes and prefabs need it !!!

NOTE: running the wizard is safe to repeat — it regenerates all sprites,
prefabs and scenes each time. If you edited it, just run it again.

HOW TO PLAY:
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
- "PLAY" continues at your first unbeaten level; the number buttons jump
  to any level directly.
- Beat all 30 for the "You Win!" ending. Press Del on the menu to reset.

LEVELS: 30 levels in 5 worlds of 6, on a gentle difficulty curve (each world
is one color on the menu). Every level was PROCEDURALLY generated and then
verified by a BFS solver that plays by the exact same rules as the game, so
all 30 are guaranteed solvable; the solver's optimal move count is each
level's "par" (shown as a // comment next to it in the wizard).
  World 1  Basics            - straight pushes, learn to move
  World 2  Crowded           - more boxes, a wall or two in the way
  World 3  Inside the Box    - push a box into a meta-box
  World 4  Housemates        - box and/or you belong inside a room
  World 5  Deep & Twin       - two rooms deep, or one box into each of two

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
