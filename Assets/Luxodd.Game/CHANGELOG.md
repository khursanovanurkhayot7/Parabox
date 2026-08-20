# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.1.1] - Unreleased

### Changed
- Updated WebGL validation guidance for throttled startup, reconnect, and replacement-socket behavior.

### Fixed
- Fixed a WebGL WebSocket race where messages could be sent while the browser socket was still in CONNECTING state, causing `InvalidStateError`.
- Messages sent during CONNECTING are queued and flushed in FIFO order when the socket opens.
- Stale WebSocket callbacks can no longer clear or interfere with a newer replacement socket.

## [1.1.0] - 2026-08-13

### Added
- Added asmdef-based modular architecture:
  - `Luxodd.Game.Runtime`
  - `Luxodd.Game.InputSystem` (optional)
  - `Luxodd.Game.Editor`
  - `Luxodd.Game.InputSystem.Editor`
- Added optional Input System backend contract and implementation:
  - `IArcadeControlsBackend`
  - `ArcadeControlsInputSystemBackend`
- Added optional Input System compile gating through asmdef `versionDefines` + `defineConstraints` (`LUXODD_INPUT_SYSTEM`).
- Added arcade button mapping diagnostic example aligned with the physical arcade button layout.
- Added joystick visualization example with shader-based visual feedback.
- Added explicit `Unity.TextMeshPro` and `Unity.ugui` runtime asmdef references.
- Added export-contained example font and TMP resource references under `Assets/Luxodd.Game`.

### Changed
- Split `ArcadeControls` into:
  - Core facade with legacy-safe fallback behavior
  - Optional Input System backend in separate assembly
- Moved runtime code into `Assets/Luxodd.Game/Runtime/Core/**`.
- Moved Input System specific runtime code into `Assets/Luxodd.Game/Runtime/InputSystem/**`.
- Moved editor tooling into `Assets/Luxodd.Game/Editor/Core/**`.
- Moved Input System diagnostic example to `Assets/Luxodd.Game/Example/Scripts/InputSystem/**`.
- Updated assembly references:
  - `Luxodd.Game.Runtime` now references `Unity.TextMeshPro`.
  - `Luxodd.Game.InputSystem` now references `Unity.InputSystem`.
- Updated `EventSystemInputModuleSwitcher` to detect Input System UI modules at runtime without a compile-time dependency on `LUXODD_INPUT_SYSTEM` in Core.
- Current arcade joystick hardware behavior is digital-direction oriented (typically 0/1 states), while the public API remains `Vector2`-compatible for future analog hardware.
- `Assets/WebGLTemplates/LuxoddTemplate/**` is part of required release content for WebGL/browser integration.
- Projects upgrading from previous plugin structure should remove old plugin files before importing `1.1.0` to avoid stale files from legacy paths.
- Legacy Input fallback returns neutral values when Legacy Input Manager is unavailable.
- Every non-empty `prize_tickets` response is forwarded as `prize_won`, including repeated identical payloads.
- Selected backend command handlers log safe metadata instead of full payload contents.
- Package export guidance uses `Assets/Luxodd.Game/**` and `Assets/WebGLTemplates/LuxoddTemplate/**`.

### Fixed
- Fixed Core Legacy Input exceptions in Input System Package-only projects.
- Fixed `EventSystemInputModuleSwitcher` calling `DestroyImmediate` from `OnValidate`.
- Fixed the missing `Unity.ugui` runtime assembly reference.
- Fixed repeated identical prize ticket responses being dropped before frontend forwarding.
- Removed full prize ticket JSON and selected full backend payload logs.
- Fixed example prefab and scene dependencies on root `Assets/Fonts` and `Assets/TextMesh Pro` folders.

### Removed
- Removed duplicate `prize_won` payload suppression.
- Removed plugin example dependencies on root `Assets/Fonts` and `Assets/TextMesh Pro`.

### Known Limitations
- Score signing/encryption is not included in 1.1.0.
- Final Unity 2022 LTS, Unity 6+, clean-project, and WebGL browser validation remains required before future release publication.
- Broader transport, token, URL, and session logging hardening remains future work.

## [1.0.12] - 2026-02-16

### Added
- Added mobile detection demo example.
- Added screen resize detection with event propagation.
- Added orientation handling demo (portrait/landscape example panel).
- Added WebGL Safe Area applier for mobile browsers.
- Added additional debug information for mobile runtime validation.
- Added UI layout example scene updates for mobile testing.

### Changed
- Updated mobile detection logic and orientation handling flow.
- Improved Unity logo resizing behavior.
- Updated example scene structure and related UI handlers.
- Updated LuxoddRuntimeContext to support VisualViewport integration.
- Updated WebGL template integration (LuxoddRuntimeContext.jslib adjustments).

### Fixed
- Fixed UI layout issues on mobile devices.
- Fixed orientation-related UI inconsistencies.
- Minor runtime fixes and cleanup.

## [1.0.10] — 2026-01-12
### Fixed
- Fixed an issue with **WebSocket connection in local WebGL builds**:
  - The server address was resolved incorrectly when running WebGL builds locally.
  - Updated logic now uses the correct server address, preventing connection errors during local testing.

### Changed
- Replaced the default Unity splash logo with the **Luxodd logo** displayed on WebGL game startup.

## [1.0.9] — 2026-01-01
### Added
- Added a **full gameplay-based Controlling Test scene**:
  - Demonstrates real-world arcade input usage in a 2D action scenario.
  - Includes player movement, jumping, ladder interaction, shooting, and item usage.
  - Uses adapter-based input architecture to keep gameplay logic independent from hardware.
- Added a **Full Working Example** documentation section with a step-by-step explanation of the gameplay test scene.
- Extended documentation to clearly distinguish between:
  - Input visualization/debug scenes
  - Gameplay-driven input examples

### Improved
- Documentation clarity for arcade input testing and learning workflows.
- Onboarding experience for junior and junior+ developers working with arcade controls.

## [1.0.8] — 2025-12-24
### Added
- Introduced a clear and unified mapping between real arcade buttons and Unity joystick inputs based on button colors.
- Added `ArcadeControls` API for simplified input handling:
  - `ArcadeControls.GetStick()` — returns joystick input in a convenient format (Vector2) for horizontal and vertical movement.
  - `GetButton`, `GetButtonDown`, and `GetButtonUp` methods to query button state by `ArcadeButtonColor`, enabling color-based input logic in game code.
- Added a dedicated Editor window that visually represents the arcade control panel with example button mappings, helping developers understand the physical layout and intended usage.
- Added a new **Controlling Test** scene to the plugin examples:
  - Includes a controllable square driven by joystick input.
  - Displays approximate button placement by color, matching the real arcade layout.
  - Highlights buttons on screen when the corresponding physical joystick buttons are pressed, making input mapping clear and intuitive.

### Changed
- Renamed the `Network` prefab to `UnityPluginPrefab` to better reflect its expanded responsibilities, including both networking and input handling.
- Unified the plugin menu naming in the Unity Editor — all entries are now grouped under **Luxodd Unity Plugin**.

### Fixed
- Minor usability and consistency fixes across editor menus and example content.

## [1.0.7] — 2025-12-10
### Fixed
- Fixed JavaScript bridge issue causing the error `ReferenceError: allocateUTF8 is not defined` when calling `GetParentHost` in WebGL builds.
- Updated `.jslib` integration to properly expose UTF8 allocation utilities and ensure compatibility with the latest Unity WebGL template.
- Improved stability of the WebGL initialization sequence.

## [1.0.6] — 2025-12-09
### Added
- Plugin that provides access to the Developer Token and WebSocket server URL.
- Delay (5–7 seconds) at the beginning of the game launch to wait for the event with the token and updated server address.  
  If no new address is received within this time, the plugin connects using the previous method.
- Updated `Network` prefab — added `LuxoddSessionBridge` class, which retrieves token and WebSocket URL data from the new plugin and notifies the system.
- Introduced `Task`/`async`/`await` wrappers for commands to allow usage without callbacks.
- Added the ability to retrieve the WebSocket server URL based on the parent window where the game is running, eliminating the need to pass the server address through settings.
- Implemented backward compatibility with previous plugin versions.

### Changed
- Updated example scenes and scripts to align with the new plugin logic.

## [1.0.5] — 2025-09-23
### Fixed
- WebGL template naming.

## [1.0.4] — 2025-09-22
### Added
- Ability to create mission descriptions and export them to the admin panel (first iteration, improvements planned for future versions).
- Commands for working with the Strategic Betting mode:
  - `GetGameSessionInfoRequest` — retrieves information about the current game session type (Strategic Betting or Pay to Play).
  - `GetBettingSessionMissionsRequest` — retrieves information about SB missions, including description, bet amount, difficulty, and bet coefficient.
  - `SendStrategicBettingResultRequest` — sends the result of an SB game session, including mission ID and result.
- Commands for handling in-game Transactions — a mechanism that processes requests to continue playing after Game Over or to restart, handled directly by the system instead of the game client.

### Fixed
- Minor bug fixes.

## [1.0.3] — 2025-07-17
### Added
- Plugin version display in the WebGL Template.
- Automatic script for version injection during WebGL build.
- Example EditorWindow to display plugin information.
- Added logic for command dispatch when reconnecting to the server.

### Changed
- Documentation updated.

### Fixed
- Minor build-related bug fixes.

## [1.0.2] — 2025-06-23
### Fixed
- Fixed credits variable issue — switched from integer to float.
- Fixed reconnection issue.

## [1.0.1] — 2025-05-29
### Added
- Added Developer Token Setup.
- Added Newtonsoft Dependency Installer.

## [1.0.0] — 2025-05-15
### Added
- First stable release of the plugin.
