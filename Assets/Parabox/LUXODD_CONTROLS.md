# Luxodd and Standard Gamepad Controls

Parabox reads controller input through `LuxoddArcadeAdapter`, which polls the Luxodd
`ArcadeControls` API. The Luxodd Binding Editor supplies overlay labels only; it does not bind
gameplay actions.

## Physical and fallback mapping

| Luxodd color | Cabinet input | Parabox action | Standard gamepad |
|---|---:|---|---|
| Black | `JoystickButton0` | Confirm / activate selected button | A / Cross |
| Red | `JoystickButton1` | Undo | X / Square |
| Green | `JoystickButton2` | Restart level | Y / Triangle |
| Yellow | `JoystickButton3` | Open/close Level Select | Start / Options |
| Blue | `JoystickButton4` | Mute | Left shoulder |
| Purple | `JoystickButton5` | Skip the active walkthrough | Right shoulder |
| Orange | `JoystickButton8` | Luxodd system help/overlay | Select / View |
| White | `JoystickButton9` | Back / cancel | B / Circle |

The arcade stick, standard-gamepad left stick, and standard-gamepad D-pad all produce the same
direction signal. If an idle cabinet joystick and an active standard gamepad are connected at the
same time, the input with the strongest direction wins.

Gameplay uses one-gesture/one-move input: tilting or holding a direction spends exactly one move,
and the stick/D-pad must return to neutral before another puzzle move can fire. Menu focus may repeat
while held because it cannot alter the puzzle or consume the move allowance.

## Screen behavior

- Main menu: stick/D-pad changes focus; Black/A activates; Yellow/Start toggles Level Select;
  White/B returns; Blue/LB mutes.
- Level Select: stick/D-pad follows the authored level route; Black/A opens the selected level;
  Yellow/Start or White/B returns.
- Every walkthrough: the prebuilt purple `SKIP` button is visible for the entire demonstration;
  Purple/RB skips immediately and restores the untouched board with full time and moves. Mouse/touch
  can click the same button, and Tab is the keyboard shortcut.
- Tutorial choice: stick/D-pad selects `REPEAT` or `TRY IT YOURSELF`; Black/A activates the
  selected choice. Purple/RB remains available as Skip.
- First mechanic appearances: a prebuilt `NEW MECHANIC` briefing appears before the first playable
  frame. Full solve demonstrations run only when that exact chapter opener introduces a new rule;
  familiar-mechanic chapter recaps are not presented as late tutorials.
- Gameplay: stick/D-pad moves; Red/X undoes; Green/Y restarts; Yellow/Start or White/B opens the
  menu; Blue/LB mutes.
- Level complete: Black/A continues.
- Loss: the game intentionally blocks local controller actions while the Luxodd transaction owns
  the screen. Luxodd Continue restores the exact failed board state with full time and moves.
- Campaign finale: stick/D-pad selects `PLAY AGAIN` or `LEVEL SELECT`; Black/A activates;
  Yellow/Start or White/B chooses Level Select.

## Input ownership

Controller navigation has one owner: `LuxoddArcadeAdapter`. Both scene EventSystems have
`sendNavigationEvents` disabled so Unity's UI module cannot invoke a button a second time from the
same gamepad press. Mouse and touch pointer events still use `InputSystemUIInputModule` normally.

Keyboard navigation is handled explicitly by the menu/game scripts, preserving arrows/WASD,
Enter/Space and Escape without re-enabling duplicate controller submission.

## Validation

Run either command after changing controls:

- `Tools > Parabox > Generate Prebuilt UI (Run This)`
- `Tools > Parabox > Validate Luxodd Controls`

The validator checks all eight cabinet indices, all eight labels, the standard-gamepad fallback,
one EventSystem per scene, and single-path controller ownership. A WebGL build is blocked when the
audit fails. The machine-readable result is written to
`Library/ParaboxLuxoddControlValidation.txt`.

Official references:

- https://docs.luxodd.com/docs/arcade-launch/unity-plugin/arcade-control/arcade-unity-plugin
- https://docs.luxodd.com/docs/arcade-launch/unity-plugin/arcade-control/arcade-unity-plugin-full-example
- https://docs.luxodd.com/docs/arcade-launch/unity-plugin/arcade-control/api-reference-arcade-controls
