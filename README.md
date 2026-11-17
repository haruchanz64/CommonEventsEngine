# CommonEventsEngine

RPG Maker-inspired Common Events, global Switches/Variables, Self-Switches, Inventory/Gold ledger, and tick-driven command scripting framework for Unity 6.

CommonEventsEngine brings RPG Maker's top-to-bottom indented event scripting workflow to Unity 6, with a deterministic state-machine runner, built-in uGUI Dialogue Box and Screen Fader components, and a UI Toolkit workbench with an EditMode step debugger.

## Features

- **Deterministic tick-driven state machine (`CommonEventRunner`):** Commands run through `OnStart`, `OnTick(deltaTime)`, and `OnCancel` with zero per-frame coroutine allocations, pause/step debugging, and full EditMode unit testability.
- **RPG Maker trigger modes:**
  - **Manual:** Triggered from code, Canvas `Button.onClick()`, 2D/3D colliders (`CommonEventTrigger`), or subroutines (`CallCommonEvent` with local call arguments and recursion depth guard).
  - **Autorun:** Automatically executes in an exclusive queue when its trigger conditions evaluate true and raises `OnControlLockChanged(bool)` so movement controllers or UI can pause during cutscenes.
  - **Parallel:** Loops concurrently in the background while its trigger conditions remain true and cancels cleanly when the switch turns off.
- **Global Switches, Variables, Self-Switches, and Inventory (`WorldStateStore`):**
  - Stores named boolean Switches, typed Variables (`Int`, `Float`, `String`), per-object Self-Switches (`A`, `B`, `C`, `D`), Gold, and itemized Inventory counts.
  - Exports and restores complete JSON snapshots (`ExportJson` / `ImportJson`) compatible with `SaveEncryptionToolkit`.
- **29 built-in RPG commands plus automatic custom discovery:**
  - **Flow:** `If`, `Else`, `EndIf`, `Loop`, `BreakLoop`, `EndLoop`, `WaitSeconds`, `WaitUntil`, `WaitForSignal`, `CallCommonEvent`, `ExitEvent`.
  - **State & Inventory:** `SetSwitch`, `SetSelfSwitch`, `SetVariable` (`Set`, `Add`, `Subtract`, `Multiply`, `Divide`, `RandomIntRange`), `CopyVariable`, `ModifyGold`, `ModifyItem`.
  - **Dialogue, Screen & Audio:** `ShowMessage`, `ShowChoices`, `FadeScreen`, `FlashScreen`, `ShakeCamera`, `PlayAudio` (SFX, BGM, Stop BGM).
  - **Scene & Signals:** `EmitSignal`, `SetObjectActive`, `TeleportObject`, `SpawnPrefab`, `LoadScene`, `Log`.
  - **Custom commands:** Subclass `EventCommandBase`, add `[EventCommand("Category/Name")]`, and `TypeCache` registers it in the workbench automatically.
- **Built-in, swappable uGUI presentation components:**
  - `BuiltInDialogueBox` (`IEventDialogueHandler`) auto-builds a styled uGUI Dialogue and Choice Canvas using `UnityEngine.UI` (no TextMeshPro import required) or binds to your own uGUI references.
  - `BuiltInScreenFader` (`IScreenTransitionHandler`) provides full-screen Fade Out/In, color Flash, and Camera Shake.
  - `BuiltInEventAudio` (`IEventAudioHandler`) manages SFX one-shots and looping BGM.
  - Instantiate an editable scene rig in one click from **GameObject > CommonEventsEngine > Create Full CommonEvents Rig**.
- **UI Toolkit Event Workbench (`Tools > HaruChanZ64 > Gameplay > CommonEventsEngine > Event Workbench`):**
  - **Event Command Editor:** Indented command list, `TypeCache`-backed **+ Add Command** menu, inline property editing, reorder/delete, and live block structure validation (`If`/`EndIf`, `Loop`/`EndLoop`).
  - **Switches, Vars & Inventory:** Inspect and mutate Global Switches, Variables, Gold, and Inventory live in PlayMode or EditMode sandbox.
  - **Live Step Debugger:** Step command-by-command (`Step 1 Command`, `Tick +0.25s`) with an inline sandbox Dialogue and Choice panel right inside the Editor window.
  - **Signals & Bindings:** Emit signals manually and inspect active `SceneBindingRegistry` keys.

## Requirements

- Unity `6000.0` or newer
- `com.unity.ugui` (`2.0.0` or newer, included by default in Unity 6)

## Installation

### Unity Package Manager (Git URL)

1. Open **Window > Package Manager**.
2. Click **+** and choose **Install package from git URL...**.
3. Enter:

```text
https://github.com/haruchanz64/CommonEventsEngine.git
```

### Local package

Add the package path to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.haruchanz64.common-events-engine": "file:../Plugins/CommonEventsEngine"
  }
}
```

## Quick start

### 1. Create a scene rig and event asset

1. Select **GameObject > CommonEventsEngine > Create Full CommonEvents Rig** to add a `CommonEventDirector`, editable uGUI `BuiltInDialogueBox`, `BuiltInScreenFader`, and `BuiltInEventAudio`.
2. Right-click in the Project window and choose **Create > CommonEventsEngine > Common Event Asset**.
3. Click **Open in Event Workbench** in the Inspector (or **Tools > HaruChanZ64 > Gameplay > CommonEventsEngine > Event Workbench**) and click **Seed Merchant Preset** to inspect a complete Dialogue, Choices, Gold, Inventory, and Flash sequence.
4. Switch to the **Live Step Debugger** tab and click **Start Event** to play through the encounter immediately inside the Editor.

### 2. Trigger from a scene object or Canvas button

Add `CommonEventTrigger` to any GameObject or Canvas button:
- For a 2D/3D trigger zone, enable `triggerOnColliderEnter` and assign your `CommonEventAsset`.
- For a uGUI button, wire `Button.onClick()` directly to `CommonEventTrigger.Trigger()`.
- Add `CommonEventSceneBinding` to scene objects (such as `"Player"`, `"BossDoor"`, or `"TownSpawnMarker"`) so Common Events can toggle or teleport them by key.

### 3. Write a custom command

```csharp
using System;
using CommonEventsEngine.Core;

[Serializable]
[EventCommand("Quest/Complete Objective", "Marks a quest objective switch ON and emits a signal.")]
public sealed class CompleteObjectiveCommand : EventCommandBase
{
    public string objectiveSwitch = "Quest_01_Done";

    public override string GetDisplaySummary() => $"Complete Objective [{objectiveSwitch}]";

    public override CommandStatus OnStart(IEventExecutionContext ctx)
    {
        ctx.WorldState.SetSwitch(objectiveSwitch, true);
        ctx.EmitSignal("quest_objective_completed", objectiveSwitch);
        return CommandStatus.Completed;
    }
}
```

## Extension contracts

| Interface | Purpose | Default implementation |
| :--- | :--- | :--- |
| `EventCommandBase` | Base class for serializable commands discovered by `TypeCache` | 29 built-in commands under `Runtime/Commands/` |
| `IEventDialogueHandler` | Displays dialogue messages and choice prompts | `BuiltInDialogueBox` |
| `IScreenTransitionHandler` | Drives screen fades, flashes, and camera shake | `BuiltInScreenFader` |
| `IEventAudioHandler` | Plays SFX one-shots and looping BGM | `BuiltInEventAudio` |
| `IEventInventoryStore` | Stores Gold and item quantities checked by conditions/commands | `WorldStateStore` |

## Testing

The package includes EditMode NUnit tests in `Tests/Editor/CommonEventsEngineTests.cs` covering:

- Nested `If / Else / EndIf` branching and structural block linter validation
- `Loop / BreakLoop / EndLoop` and `maxIterations` limits
- `WaitSeconds`, `WaitUntil`, and `WaitForSignal` with payload capture
- Exclusive `Autorun` queue with `OnControlLockChanged` and `Parallel` lifecycle
- `CallCommonEvent` local call arguments and recursion depth guard
- Per-object Self-Switches (`A/B/C/D`), `SceneBindingRegistry`, and `TeleportObject`
- Full RPG Merchant preset (Dialogue, Choices, Gold, Inventory, Flash) and `StepSingleCommand`
- `WorldStateStore` JSON save/load snapshot round-trip

## License

MIT. See [LICENSE.md](LICENSE.md).
