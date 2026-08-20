# Luxodd Unity Plugin

Unity plugin for WebGL arcade games on the Luxodd platform. It integrates browser/iframe-embedded WebGL builds with Luxodd backend services through WebSocket commands and browser bridge code.

## Compatibility

- Unity 2022 LTS minimum.
- Compatible with Unity 6+.
- WebGL arcade game target.
- Examples are compatible with the Built-in Render Pipeline and do not require URP.

## Dependencies

Required Unity packages:

- UGUI (`com.unity.ugui`)
- TextMeshPro (`com.unity.textmeshpro`)
- Newtonsoft.Json (`com.unity.nuget.newtonsoft-json`)

Optional Unity package:

- Input System (`com.unity.inputsystem`)

The plugin includes editor checks for missing TextMeshPro and Newtonsoft.Json packages. Input System is optional and is not required for Core runtime support.

## Architecture

- `Assets/Luxodd.Game/Runtime/Core/**` — always-available runtime API.
- `Assets/Luxodd.Game/Runtime/InputSystem/**` — optional Input System backend.
- `Assets/Luxodd.Game/Editor/Core/**` — editor tooling without Input System dependency.
- `Assets/Luxodd.Game/Editor/InputSystem/**` — optional Input System editor tooling.

Core does not directly depend on `UnityEngine.InputSystem`. Optional assemblies use asmdef package-presence gating through `versionDefines` and `LUXODD_INPUT_SYSTEM`.

## Input Compatibility

`ArcadeControls` is the recommended arcade input API:

```csharp
Vector2 stick = ArcadeControls.GetJoystick();
bool isPressed = ArcadeControls.GetButton(ArcadeButtonColor.Red);
bool pressedThisFrame = ArcadeControls.GetButtonDown(ArcadeButtonColor.Black);
bool releasedThisFrame = ArcadeControls.GetButtonUp(ArcadeButtonColor.Green);
```

- With Legacy Input Manager available, Core uses guarded Legacy Input fallback paths.
- With Active Input Handling set to **Input System Package** only, Legacy fallback paths return neutral values (`Vector2.zero`, `0f`, or `false`) instead of throwing.
- Active Input Handling = **Both** is not required merely to avoid Legacy Input exceptions.
- When `com.unity.inputsystem` is installed, the optional backend registers automatically and provides Input System device support. Configure `ArcadeInputMappingConfig` through the supplied setup/config components for hardware-specific mappings.

Current arcade joystick hardware reports digital directions (typically `0` or `1`), while the public API remains `Vector2`-compatible for future analog hardware.

## EventSystem Input Module

`EventSystemInputModuleSwitcher` corrects the EventSystem input module in Editor and at runtime:

- Uses `InputSystemUIInputModule` when the Input System UI module is available.
- Uses `StandaloneInputModule` otherwise.
- Editor correction is delayed outside `OnValidate`; it does not destroy components directly during `OnValidate`.

## prize_won Forwarding and Logging

For WebGL `level_end` responses:

- Every non-empty `prize_tickets` response is forwarded to the frontend as `prize_won`.
- Repeated identical non-empty payloads are forwarded and are not suppressed.
- Null or empty prize ticket responses are ignored.
- Full prize ticket JSON is not logged.
- Selected backend command handlers log safe parsing/forwarding metadata instead of full payload contents.

This does not imply that all transport, token, URL, or session logging has been hardened in this release.

## Examples and Local Resources

The plugin includes input, arcade button, joystick, mobile, and gameplay examples. Example font and TMP resources are contained under `Assets/Luxodd.Game`; package export does not require root `Assets/Fonts` or root `Assets/TextMesh Pro` folders.

## WebGL Template

`Assets/WebGLTemplates/LuxoddTemplate` is required release content for browser, iframe, and platform bridge integration.

`Assets/WebGLTemplates` is a reserved Unity folder name. Do not rename it, move it under `Assets/Luxodd.Game`, or nest/restructure the template.

Select `LuxoddTemplate` in Unity WebGL Player Settings before building.

The WebGL bridge buffers messages while the browser WebSocket is CONNECTING and flushes them after the socket opens. This prevents early-session WebSocket send race errors.

## Exporting the Plugin

Export exactly these roots:

- `Assets/Luxodd.Game/**`
- `Assets/WebGLTemplates/LuxoddTemplate/**`

Exclude project-specific content, including:

- `Assets/Fonts/**`
- `Assets/TextMesh Pro/**`
- `Assets/Scenes/**`
- `Assets/Settings/**`
- `Packages/**`
- `ProjectSettings/**`
- `UserSettings/**`
- `.agent/**`, `.codex/**`, and `docs/**`
- IDE folders and root zip/export artifacts

In Unity's Export Package dialog, select only the approved roots. Disable **Include Dependencies** if Unity attempts to include project-local assets outside those roots, then review the selected asset list before exporting.

## Upgrading from Previous Versions

For 1.1.0, use a clean reinstall instead of importing over an older version:

1. Back up the Unity project.
2. Remove the old `Assets/Luxodd.Game` folder.
3. Back up customized `Assets/WebGLTemplates/LuxoddTemplate` if applicable.
4. Import the 1.1.0 package.
5. Replace or merge `Assets/WebGLTemplates/LuxoddTemplate` carefully.
6. Let Unity recompile and check scenes/prefabs for missing script warnings.

This avoids stale files from earlier layouts causing duplicate classes, duplicate assemblies, or mixed old/new plugin structure. Do not bulk-delete `Assets/Luxodd.Game/Scripts/**` after import; valid 1.1.0 files may remain there.

## Known Limitations

- Score signing/encryption is not included in 1.1.0. This release does not provide ev1/ev2 signing, BouncyCastle integration, or secure score session support.
- Final clean-project validation in Unity 2022 LTS and Unity 6+, plus final WebGL browser validation, is required before release publication.
