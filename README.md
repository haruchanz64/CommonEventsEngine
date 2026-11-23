# CommonEventsEngine

RPG Maker-inspired Common Events, global Switches/Variables, Self-Switches, Inventory/Gold ledger, and tick-driven command scripting framework for Unity 6. It brings RPG Maker's top-to-bottom indented event scripting workflow to Unity 6 with a deterministic state-machine runner, swappable uGUI presentation components, and a UI Toolkit Event Workbench with live step debugging.

## Features

- **Deterministic tick-driven state machine (`CommonEventRunner`):** Commands advance through `OnStart`, `OnTick(deltaTime)`, and `OnCancel` with zero per-frame coroutine allocations on the runner, pause/resume support (`IsPaused`), single-command step debugging (`StepSingleCommand`), and EditMode unit testability.
- **RPG Maker trigger modes (`CommonEventTriggerMode`):**
  - **Manual:** Triggered from code (`StartEvent` / `StartEventById`), uGUI `Button.onClick()`, 2D/3D trigger colliders (`CommonEventTrigger`), or subroutines (`CallCommonEventCommand` with local call arguments and `MaxCallDepth` recursion guard).
  - **Autorun:** Runs automatically in an exclusive one-at-a-time queue while its `triggerConditions` evaluate true and raises `OnControlLockChanged(bool)` when `lockPlayerControlOnAutorun` is enabled so character controllers or HUDs can pause input during cutscenes.
  - **Parallel:** Loops concurrently in the background while its `triggerConditions` evaluate true and cancels active commands (`OnCancel`) when conditions turn false.
- **Global Switches, Variables, Self-Switches, and Inventory (`WorldStateStore`):**
  - Stores named boolean Switches, typed Variables (`Int`, `Float`, `String` via `EventValue`), per-object Self-Switches (`A`, `B`, `C`, `D`), non-negative Gold, and itemized Inventory counts.
  - Initializes default starting state from a `WorldStateDatabase` ScriptableObject and exports/restores full JSON snapshots via `ExportJson` and `ImportJson`.
- **29 built-in commands plus automatic custom discovery:**
  - **Flow (11):** `IfCommand`, `ElseCommand`, `EndIfCommand`, `LoopCommand`, `BreakLoopCommand`, `EndLoopCommand`, `WaitSecondsCommand`, `WaitUntilCommand`, `WaitForSignalCommand`, `CallCommonEventCommand`, `ExitEventCommand`.
  - **State & Inventory (6):** `SetSwitchCommand`, `SetSelfSwitchCommand`, `SetVariableCommand` (`Set`, `Add`, `Subtract`, `Multiply`, `Divide`, `RandomIntRange`), `CopyVariableCommand`, `ModifyGoldCommand`, `ModifyItemCommand`.
  - **Dialogue, Screen & Audio (6):** `ShowMessageCommand`, `ShowChoicesCommand`, `FadeScreenCommand`, `FlashScreenCommand`, `ShakeCameraCommand`, `PlayAudioCommand` (`PlaySfx`, `PlayBgm`, `StopBgm`).
  - **Scene & Debug (6):** `EmitSignalCommand`, `SetObjectActiveCommand`, `TeleportObjectCommand`, `SpawnPrefabCommand`, `LoadSceneCommand`, `LogCommand`.
  - **Custom commands:** Subclass `EventCommandBase` and apply `[EventCommand("Category/Name")]`; `TypeCache` registers it in the workbench `+ Add Command` menu automatically.
- **Swappable built-in presentation components:**
  - `BuiltInDialogueBox` (`IEventDialogueHandler`) auto-builds a ScreenSpaceOverlay uGUI Dialogue and Choice Canvas using `UnityEngine.UI` (`LegacyRuntime.ttf`, no TextMeshPro required) with configurable typewriter speed, or binds to custom uGUI references.
  - `BuiltInScreenFader` (`IScreenTransitionHandler`) provides full-screen Fade Out/In, color Flash, and damped Camera Shake using unscaled time.
  - `BuiltInEventAudio` (`IEventAudioHandler`) manages SFX one-shots and looping BGM through auto-created or assigned `AudioSource` components.
- **UI Toolkit Event Workbench and scene setup menus:**
  - Open **Tools > HaruChanZ64 > Gameplay > CommonEventsEngine > Event Workbench** for an indented command editor, structural block linter, live WorldState/Inventory inspector, step-by-step debugger with an inline dialogue sandbox, and signal/binding monitor.
  - Create scene rigs in one click under **GameObject > CommonEventsEngine** (`Create Full CommonEvents Rig`, `Create Dialogue Box Canvas`, `Create Screen Fader Canvas`, `Create Common Event Trigger`).

## Requirements

- Unity `6000.0` or newer
- `com.unity.ugui` (`2.0.0` or newer, included by default in Unity 6)

## Installation

### Unity Package Manager (Git URL)

1. Open **Window > Package Manager** in Unity 6 (`6000.0+`).
2. Click **+** (top-left) and choose **Install package from git URL...**.
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

1. Create a scene rig via **GameObject > CommonEventsEngine > Create Full CommonEvents Rig**. This creates a `CommonEventsRig` GameObject with `CommonEventDirector`, `BuiltInDialogueBox`, `BuiltInScreenFader`, and `BuiltInEventAudio` pre-wired.
2. *(Optional)* Create a starting state catalog via **Assets > Create > CommonEventsEngine > World State Database** and assign it to `Initial Database` on `CommonEventDirector`.
3. Create an event asset via **Assets > Create > CommonEventsEngine > Common Event Asset**.
4. In the Inspector, click **Open in Event Workbench** (or open **Tools > HaruChanZ64 > Gameplay > CommonEventsEngine > Event Workbench**), then click **Seed Merchant Preset** to populate a complete Dialogue, Choices, Gold, Inventory, and Flash sequence.
5. Switch to the **3. Live Step Debugger** tab and click **Start Event** or **Step 1 Command** to run the encounter inside EditMode using the inline dialogue sandbox.
6. To trigger the event in a scene, add `CommonEventTrigger` (or **GameObject > CommonEventsEngine > Create Common Event Trigger**), assign the `CommonEventAsset`, and either enable `triggerOnColliderEnter`, enable `triggerOnStart`, or bind a uGUI `Button.onClick()` to `CommonEventTrigger.Trigger()`.
7. Add `CommonEventSceneBinding` to scene objects (such as `"Player"`, `"BossDoor"`, or `"TownMarker"`) so `SetObjectActiveCommand`, `TeleportObjectCommand`, and `SpawnPrefabCommand` can reference them by key without asset-to-scene links.

## Core concepts

### Tick-driven execution pipeline

Each call to `CommonEventRunner.Tick(deltaTime)` (driven from `CommonEventDirector.Update()`) runs three stages unless `IsPaused` is `true`:

1. **Automatic trigger evaluation (`EvaluateAutomaticTriggers`):** Scans `RegisteredEvents` with non-empty `triggerConditions`. Eligible `Autorun` events start only when no other Autorun is active; `Parallel` events start when conditions become true and cancel cleanly (`OnCancel`) when conditions turn false.
2. **Process advancement (`AdvanceProcess`):** Only the first active Autorun process advances per tick (exclusive Autorun queue), while Manual and Parallel processes run concurrently. Each process executes up to `MaxInstantCommandsPerTick` (default `128`) instant commands per tick. Structural flow commands (`IfCommand`, `ElseCommand`, `EndIfCommand`, `LoopCommand`, `BreakLoopCommand`, `EndLoopCommand`) are resolved directly by the runner; infinite loops (`maxIterations <= 0`) yield one tick at `EndLoopCommand` to prevent main-thread freezes.
3. **Completion and control lock (`CleanupFinishedProcesses`):** Removes finished processes, raises `OnEventCompleted`, and updates `IsControlLocked` / `OnControlLockChanged` whenever an Autorun process with `lockPlayerControlOnAutorun` starts or finishes.

### Call stacks, local scope, and Self-Switches

- `CallCommonEventCommand` pushes a new `EventExecutionFrame` onto `ActiveEventProcess.CallStack` with copied local arguments (`Dictionary<string, EventValue>`) and inherits the caller's `OwnerKey`. Recursive calls beyond `MaxCallDepth` (default `16`) are skipped with a warning entry in `runner.Logs`.
- `CommonEventTrigger` resolves `EffectiveOwnerKey` from its `ownerKey` field or `"{sceneName}:{gameObjectName}"`, allowing reusable `CommonEventAsset` scripts (such as a treasure chest or NPC dialogue) to track independent Self-Switches (`A`, `B`, `C`, `D`) per scene object.

### Signal bus and scene bindings

- `EmitSignal(signalName, payload)` increments `SignalSequence`, appends to a 200-entry rolling `SignalHistory`, and raises `OnSignalEmitted`. `WaitForSignalCommand` captures `CurrentSignalSequence` in `OnStart` and resumes once a matching signal arrives with a higher sequence number, optionally storing its payload in a Global Variable.
- `SceneBindingRegistry` decouples ScriptableObject event assets from scene hierarchy references. `CommonEventSceneBinding` registers on `OnEnable` and unregisters on `OnDisable`.

## Code examples

### 1. Write a custom `EventCommandBase`

Decorate any serializable subclass of `EventCommandBase` with `[EventCommand("Category/Name", "Description")]` so `TypeCache` lists it automatically in the Event Workbench:

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

### 2. Trigger events, lock player control, and save/load world state

Wire gameplay systems to `CommonEventDirector` (or directly to `CommonEventRunner`) to react to Autorun control locks, pass local call arguments, and persist state as JSON:

```csharp
using System.Collections.Generic;
using CommonEventsEngine.Components;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using UnityEngine;

public sealed class CutsceneBridge : MonoBehaviour
{
    [SerializeField] private CommonEventDirector director;
    [SerializeField] private CommonEventAsset rewardEvent;

    private string _savedSnapshot;

    private void OnEnable()
    {
        director.Runner.OnControlLockChanged += HandleControlLock;
        director.Runner.OnSignalEmitted += HandleSignal;
    }

    private void OnDisable()
    {
        if (director == null) return;
        director.Runner.OnControlLockChanged -= HandleControlLock;
        director.Runner.OnSignalEmitted -= HandleSignal;
    }

    public void GrantBonusReward(int bonusGold)
    {
        var args = new Dictionary<string, EventValue>
        {
            ["BonusGold"] = EventValue.FromInt(bonusGold)
        };
        director.Runner.StartEvent(rewardEvent, ownerKey: "ArenaMaster", args: args);
    }

    public void SaveProgress() => _savedSnapshot = director.ExportSaveJson();
    public void LoadProgress() => director.ImportSaveJson(_savedSnapshot);

    private void HandleControlLock(bool locked) => Debug.Log($"Player control locked: {locked}");
    private void HandleSignal(string signal, string payload) => Debug.Log($"Signal '{signal}': {payload}");
}
```

### 3. Supply a custom `IEventDialogueHandler`

Implement `IEventDialogueHandler` on a `MonoBehaviour` and assign it to `Dialogue Handler Behaviour` on `CommonEventDirector` (or assign `runner.DialogueHandler` in code) to replace the built-in uGUI dialogue box:

```csharp
using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using UnityEngine;

public sealed class CustomDialogueAdapter : MonoBehaviour, IEventDialogueHandler
{
    public void ShowMessage(string speakerName, string message, Sprite portrait, Action onCompleted)
    {
        Debug.Log($"{speakerName}: {message}");
        onCompleted?.Invoke();
    }

    public void ShowChoices(string prompt, IReadOnlyList<string> choices, int cancelChoiceIndex, Action<int> onChoiceSelected)
    {
        onChoiceSelected?.Invoke(0);
    }

    public void CancelActiveDialogue()
    {
        // Hide your custom dialogue UI here without invoking callbacks.
    }
}
```

## Extension contracts and core API

| Type | Namespace | Purpose | Built-in implementation |
| :--- | :--- | :--- | :--- |
| `EventCommandBase` | `CommonEventsEngine.Core` | Base class for `[SerializeReference]` commands discovered by `TypeCache` via `[EventCommand]` | 29 built-in commands in `CommonEventsEngine.Commands` |
| `IEventDialogueHandler` | `CommonEventsEngine.Core` | Displays dialogue lines and selectable choice prompts | `BuiltInDialogueBox` (`CommonEventsEngine.Presentation`) |
| `IScreenTransitionHandler` | `CommonEventsEngine.Core` | Drives screen fades, color flashes, and camera shake | `BuiltInScreenFader` (`CommonEventsEngine.Presentation`) |
| `IEventAudioHandler` | `CommonEventsEngine.Core` | Plays one-shot SFX and looping BGM | `BuiltInEventAudio` (`CommonEventsEngine.Presentation`) |
| `IEventInventoryStore` | `CommonEventsEngine.Core` | Stores Gold balance and item counts checked by conditions and commands | `WorldStateStore` (`CommonEventsEngine.State`) |
| `IEventExecutionContext` | `CommonEventsEngine.Core` | Per-command runtime context exposing state, handlers, local variables, signals, and control-flow requests | Internal `FrameContext` inside `CommonEventRunner` |
| `CommonEventRunner` | `CommonEventsEngine.Engine` | Pure C# tick-driven runner usable directly in headless simulations or EditMode tests | `CommonEventRunner` (hosted in scenes by `CommonEventDirector`) |
| `CommonEventAsset` | `CommonEventsEngine.Data` | ScriptableObject holding trigger mode, conditions, `[SerializeReference]` command list, indentation, and structural validation | `CommonEventAsset` |
| `WorldStateDatabase` | `CommonEventsEngine.Data` | ScriptableObject catalog of initial Switches, Variables, starting Gold, and starting Inventory items | `WorldStateDatabase` |
| `SceneBindingRegistry` | `CommonEventsEngine.State` | Maps string keys to live scene `GameObject` instances | `SceneBindingRegistry` (populated by `CommonEventSceneBinding`) |

## Included samples

Import **Custom Event Command Sample** (`Samples~/CustomCommands`) from the Package Manager **Samples** tab:

- `SampleHealPartyCommand`: custom `EventCommandBase` in `CommonEventsEngine.Samples` decorated with `[EventCommand("RPG/Heal Party", ...)]` that reads `PartyHP` and `PartyMaxHP` variables, clamps healed HP to `PartyMaxHP`, and emits the `"party_healed"` signal with the new HP value.

## Testing

The package includes EditMode NUnit tests under `Tests/Editor/` runnable from **Window > General > Test Runner** (EditMode tab):

- `Tests/Editor/CommonEventsEngineTests.cs`: verifies the sample Potion Merchant preset (dialogue, choices, gold deduction, inventory grant, screen flash, and insufficient-gold branch), structural block validation (`ValidateStructure`), `Loop`/`BreakLoop` accumulation, `WaitSeconds` and `WaitForSignal` payload capture, exclusive `Autorun` control lock (`OnControlLockChanged`) and `Parallel` lifecycle cancellation, `CallCommonEvent` local arguments and `MaxCallDepth` recursion guard, per-object Self-Switches with `SceneBindingRegistry` and `WorldStateStore` JSON snapshot round-trip, and `StepSingleCommand` line-by-line execution.

## Troubleshooting and limitations

- **Autorun or Parallel events never start automatically:** `CommonEventRunner.EvaluateAutomaticTriggers()` requires at least one entry in `triggerConditions` and requires the asset to be registered in `CommonEventDirector`'s `Common Events` list (or via `runner.RegisterEvent`). Assets with empty `triggerConditions` must be started manually via `TriggerEvent`, `StartEvent`, or `CommonEventTrigger`.
- **Autorun event loops infinitely after finishing:** Automatic triggers are re-evaluated on every tick. If an Autorun event's trigger Switch stays `ON` after the event completes, it starts again on the next tick. Turn off the trigger Switch or update its gating Variable/Self-Switch before the event exits.
- **Dialogue, fade, or audio commands skip immediately:** When `DialogueHandler`, `ScreenHandler`, or `AudioHandler` is null, `ShowMessageCommand` logs the message to `runner.Logs`, `ShowChoicesCommand` picks choice index `0`, and screen/audio commands complete immediately. Attach the built-in presentation components to the `CommonEventDirector` hierarchy or assign custom handlers in the Inspector.
- **Deactivated objects cannot be re-enabled by `SetObjectActiveCommand`:** `CommonEventSceneBinding` registers in `OnEnable` and unregisters in `OnDisable`, so disabling a bound `GameObject` removes its key from `SceneBindingRegistry`. Keep the binding on an always-active parent object or register the reference directly via `SceneBindings.Register` if you need to re-enable it later.
- **UI does not update after `ImportJson` / `ImportSaveJson`:** `WorldStateStore.ImportJson` clears and repopulates state silently without invoking `OnSwitchChanged`, `OnVariableChanged`, `OnSelfSwitchChanged`, `OnGoldChanged`, or `OnItemCountChanged`. Refresh bound HUD widgets explicitly after loading a save snapshot.
- **Clicking Next or choice buttons on `BuiltInDialogueBox` does nothing:** `BuiltInDialogueBox` builds a Canvas with a `GraphicRaycaster`, but does not create a scene `EventSystem`. Add a Unity `EventSystem` matching your project's Active Input Handling, or drive dialogue from code via `AdvanceOrCompleteMessage()` and `SelectChoice(index)`.
- **Running the same `CommonEventAsset` concurrently:** Timed and interactive commands (`WaitSecondsCommand`, `WaitForSignalCommand`, `ShowMessageCommand`, `ShowChoicesCommand`, screen transition commands) hold transient `[NonSerialized]` fields on the serialized command instance. Avoid launching multiple overlapping processes on the same `CommonEventAsset` instance at the same time.

## License

[MIT](LICENSE.md)
