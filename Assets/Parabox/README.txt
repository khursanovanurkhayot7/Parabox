PARABOX — a Patrick's Parabox inspired puzzle game
===================================================

This project uses URP so it can be built for WebGL (browser).

HOW TO SET UP (one time):
1. Open the project in Unity and wait for scripts to compile.
2. In the top menu click:  Tools > Parabox > Create Everything (Run Once)
3. Done. It sets up the URP render pipeline and creates:
   - Assets/Parabox/Sprites          (procedurally generated rounded sprites)
   - Assets/Parabox/Prefabs          (Floor, Border, Wall, Box, MetaBox, Player, goals)
   - Assets/Parabox/Prefabs/Levels   (Level_1 ... Level_5 prefabs)
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
- Beat all 10 for the "You Win!" ending. Press Del on the menu to reset.

LEVELS (all verified solvable by an automated solver):
1  First Steps            - basic pushing
2  Chain Reaction         - push a row of boxes
3  Think Inside the Box   - push a box inside a box
4  Breaking Out           - you start INSIDE a box, push your way out
5  Home Inside            - the goal is inside a box
6  Detour                 - walls block the direct path
7  Housemates             - box AND you both belong inside a box
8  Box in a Box           - push a box two boxes deep
9  Deep Dive              - travel two rooms deep to your goal
10 Twin Rooms             - one box into each of two rooms

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
Duplicate Level_5.prefab, edit markers, then add the new prefab to the
GameManager "Level Prefabs" array in the Game scene.
