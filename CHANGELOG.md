# Changelog

All notable changes to this package are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-11-23

### Changed

- Expanded `README.md` into a standardized developer guide covering the tick-driven pipeline, trigger modes, code examples, extension contracts, included samples, tests, and troubleshooting caveats.
- Added XML documentation comments across public types, members, and non-trivial internal/private helpers in Runtime, Editor, and Samples.

## [1.0.0] - 2026-08-23

### Added

- Deterministic tick-driven `CommonEventRunner` supporting Manual calls (with local call arguments and recursion guard), exclusive Autorun queue with `OnControlLockChanged`, Parallel loops, Signal Bus, and single-command step execution.
- `CommonEventAsset` ScriptableObject with flat `[SerializeReference]` command lists, RPG Maker indentation calculation, structural block validator, and sample Potion Merchant preset.
- `WorldStateDatabase` and `WorldStateStore` storing Global Switches, typed Variables (`Int`, `Float`, `String`), Self-Switches (`A/B/C/D`), Gold, and Inventory items with JSON save/load snapshots.
- 29 built-in commands across Flow, State, Inventory, Dialogue/Choices, Screen/Camera, Audio, and Scene/Signals plus `[EventCommand]` `TypeCache` custom command auto-discovery.
- Interface-backed built-in uGUI and Audio components: `BuiltInDialogueBox` (`IEventDialogueHandler`), `BuiltInScreenFader` (`IScreenTransitionHandler`), and `BuiltInEventAudio` (`IEventAudioHandler`).
- Scene integration components: `CommonEventDirector`, `CommonEventSceneBinding`, `CommonEventTrigger`, and one-click `GameObject > CommonEventsEngine` scene rig menus.
- UI Toolkit `CommonEventsWorkbenchWindow` (`Tools > CommonEventsEngine > Event Workbench`) with Event Command Editor, Switches/Variables/Inventory inspector, Live Step Debugger with inline Dialogue sandbox, and Signals/Bindings tab, plus custom `CommonEventAssetInspector`.
- Custom command sample in `Samples~/CustomCommands/SampleHealPartyCommand.cs` and EditMode NUnit tests in `Tests/Editor/CommonEventsEngineTests.cs`.
