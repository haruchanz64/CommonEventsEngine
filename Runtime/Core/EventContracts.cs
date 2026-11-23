using System;
using System.Collections.Generic;
using CommonEventsEngine.Data;
using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Core
{
    /// <summary>
    /// Determines how a <see cref="CommonEventAsset"/> is scheduled by <see cref="Engine.CommonEventRunner"/>.
    /// </summary>
    public enum CommonEventTriggerMode
    {
        Manual = 0,
        Autorun = 1,
        Parallel = 2
    }

    /// <summary>
    /// Identifies one of four per-owner boolean Self-Switch slots (A through D).
    /// </summary>
    public enum SelfSwitchSlot
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3
    }

    /// <summary>
    /// Discriminator tag indicating which typed payload is active inside an <see cref="EventValue"/>.
    /// </summary>
    public enum EventVariableType
    {
        Int = 0,
        Float = 1,
        String = 2
    }

    /// <summary>
    /// Execution status returned by <see cref="EventCommandBase.OnStart"/> and <see cref="EventCommandBase.OnTick"/>.
    /// </summary>
    public enum CommandStatus
    {
        Running = 0,
        Completed = 1
    }

    /// <summary>
    /// Structural indentation role used by flow-control commands when computing editor nesting depth.
    /// </summary>
    public enum CommandBlockRole
    {
        Normal = 0,
        OpenBlock = 1,
        MidBlock = 2,
        CloseBlock = 3
    }

    /// <summary>
    /// Marks a concrete <see cref="EventCommandBase"/> subclass with its workbench menu path and description for <c>TypeCache</c> discovery.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EventCommandAttribute : Attribute
    {
        /// <summary>
        /// Slash-delimited menu path shown in the Event Workbench "+ Add Command" menu.
        /// </summary>
        public string MenuPath { get; }

        /// <summary>
        /// Short summary describing what the command does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Initializes the command metadata attribute.
        /// </summary>
        /// <param name="menuPath">Slash-delimited category and display name, such as <c>"Flow/Wait Seconds"</c>.</param>
        /// <param name="description">Optional short description; null values default to an empty string.</param>
        public EventCommandAttribute(string menuPath, string description = "")
        {
            MenuPath = menuPath;
            Description = description ?? string.Empty;
        }
    }

    /// <summary>
    /// Serializable tagged union storing an integer, float, or string value for event variables and call arguments.
    /// </summary>
    [Serializable]
    public struct EventValue
    {
        /// <summary>
        /// Active value type tag for this instance.
        /// </summary>
        public EventVariableType type;

        /// <summary>
        /// Integer representation populated when created as an integer or float.
        /// </summary>
        public int intValue;

        /// <summary>
        /// Floating-point representation populated when created as a float or integer.
        /// </summary>
        public float floatValue;

        /// <summary>
        /// String representation populated for all value types.
        /// </summary>
        public string stringValue;

        /// <summary>
        /// Creates an <see cref="EventValue"/> tagged as <see cref="EventVariableType.Int"/>.
        /// </summary>
        public static EventValue FromInt(int v) => new EventValue { type = EventVariableType.Int, intValue = v, floatValue = v, stringValue = v.ToString() };

        /// <summary>
        /// Creates an <see cref="EventValue"/> tagged as <see cref="EventVariableType.Float"/>, rounding <see cref="intValue"/> to the nearest integer.
        /// </summary>
        public static EventValue FromFloat(float v) => new EventValue { type = EventVariableType.Float, intValue = Mathf.RoundToInt(v), floatValue = v, stringValue = v.ToString("G") };

        /// <summary>
        /// Creates an <see cref="EventValue"/> tagged as <see cref="EventVariableType.String"/>, coercing null to an empty string.
        /// </summary>
        public static EventValue FromString(string v) => new EventValue { type = EventVariableType.String, stringValue = v ?? string.Empty };

        /// <summary>
        /// Returns the value coerced to a <see cref="float"/>, parsing <see cref="stringValue"/> or returning <c>0</c> if unparseable.
        /// </summary>
        public float AsFloat() => type == EventVariableType.Float ? floatValue : (type == EventVariableType.Int ? intValue : (float.TryParse(stringValue, out float f) ? f : 0f));

        /// <summary>
        /// Returns the value coerced to an <see cref="int"/>, rounding float or parsed string values to the nearest integer.
        /// </summary>
        public int AsInt() => type == EventVariableType.Int ? intValue : Mathf.RoundToInt(AsFloat());

        /// <summary>
        /// Returns the value formatted as a non-null string.
        /// </summary>
        public string AsString() => type == EventVariableType.String ? (stringValue ?? string.Empty) : (type == EventVariableType.Float ? floatValue.ToString("G") : intValue.ToString());

        /// <inheritdoc/>
        public override string ToString() => AsString();
    }

    /// <summary>
    /// Swappable dialogue and choice presentation contract.
    /// <see cref="Presentation.BuiltInDialogueBox"/> provides a built-in uGUI implementation; games can supply their own dialogue system.
    /// </summary>
    public interface IEventDialogueHandler
    {
        /// <summary>
        /// Displays a dialogue line and invokes <paramref name="onCompleted"/> once the player advances past it.
        /// </summary>
        /// <param name="speakerName">Display name of the speaking character.</param>
        /// <param name="message">Dialogue body text to render.</param>
        /// <param name="portrait">Optional character portrait sprite, or null to hide the portrait slot.</param>
        /// <param name="onCompleted">Callback invoked when the message completes and execution may resume.</param>
        void ShowMessage(string speakerName, string message, Sprite portrait, Action onCompleted);

        /// <summary>
        /// Presents a list of choices and invokes <paramref name="onChoiceSelected"/> with the zero-based selected index.
        /// </summary>
        /// <param name="prompt">Prompt text shown alongside the choice options.</param>
        /// <param name="choices">Ordered list of choice labels to display.</param>
        /// <param name="cancelChoiceIndex">Zero-based fallback index associated with a cancel action.</param>
        /// <param name="onChoiceSelected">Callback receiving the zero-based index chosen by the player.</param>
        void ShowChoices(string prompt, IReadOnlyList<string> choices, int cancelChoiceIndex, Action<int> onChoiceSelected);

        /// <summary>
        /// Immediately dismisses any active message or choice prompt without invoking pending completion callbacks.
        /// </summary>
        void CancelActiveDialogue();
    }

    /// <summary>
    /// Swappable screen transition, flash, and camera shake presentation contract.
    /// <see cref="Presentation.BuiltInScreenFader"/> provides a built-in uGUI and camera implementation.
    /// </summary>
    public interface IScreenTransitionHandler
    {
        /// <summary>
        /// Fades a full-screen overlay toward <paramref name="targetColor"/> and <paramref name="targetAlpha"/> over <paramref name="durationSeconds"/>.
        /// </summary>
        /// <param name="targetColor">RGB color of the overlay.</param>
        /// <param name="targetAlpha">Target alpha in the <c>[0, 1]</c> range (<c>1</c> is opaque, <c>0</c> is clear).</param>
        /// <param name="durationSeconds">Transition duration in seconds; zero or negative completes immediately.</param>
        /// <param name="onCompleted">Optional callback invoked when the fade finishes.</param>
        void StartFade(Color targetColor, float targetAlpha, float durationSeconds, Action onCompleted);

        /// <summary>
        /// Plays a full-screen color flash that decays to transparent over <paramref name="durationSeconds"/>.
        /// </summary>
        /// <param name="flashColor">Peak flash color; alpha controls peak opacity.</param>
        /// <param name="durationSeconds">Flash duration in seconds.</param>
        /// <param name="onCompleted">Optional callback invoked when the flash finishes.</param>
        void StartFlash(Color flashColor, float durationSeconds, Action onCompleted);

        /// <summary>
        /// Applies a damped camera shake over <paramref name="durationSeconds"/>.
        /// </summary>
        /// <param name="intensity">Maximum positional shake amplitude in world units.</param>
        /// <param name="durationSeconds">Shake duration in seconds.</param>
        /// <param name="onCompleted">Optional callback invoked after the camera returns to rest.</param>
        void StartCameraShake(float intensity, float durationSeconds, Action onCompleted);

        /// <summary>
        /// Stops all active fades, flashes, and camera shakes and restores any modified camera transform.
        /// </summary>
        void CancelTransitions();
    }

    /// <summary>
    /// Swappable audio playback contract for SFX and BGM commands.
    /// </summary>
    public interface IEventAudioHandler
    {
        /// <summary>
        /// Plays a one-shot sound effect clip.
        /// </summary>
        /// <param name="clip">Audio clip to play; null clips are ignored.</param>
        /// <param name="volume">Playback volume multiplier in the <c>[0, 1]</c> range.</param>
        /// <param name="pitch">Playback pitch multiplier.</param>
        void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f);

        /// <summary>
        /// Starts background music playback, replacing any currently playing BGM clip.
        /// </summary>
        /// <param name="clip">Audio clip to play; null clips are ignored.</param>
        /// <param name="volume">Playback volume in the <c>[0, 1]</c> range.</param>
        /// <param name="loop">Whether the clip loops continuously.</param>
        void PlayBgm(AudioClip clip, float volume = 0.8f, bool loop = true);

        /// <summary>
        /// Stops any currently playing background music.
        /// </summary>
        void StopBgm();
    }

    /// <summary>
    /// Swappable RPG inventory and gold ledger contract.
    /// </summary>
    public interface IEventInventoryStore
    {
        /// <summary>
        /// Current non-negative Gold balance.
        /// </summary>
        int Gold { get; }

        /// <summary>
        /// Adjusts <see cref="Gold"/> by <paramref name="delta"/>, clamping the resulting balance at zero.
        /// </summary>
        void ModifyGold(int delta);

        /// <summary>
        /// Sets <see cref="Gold"/> to <paramref name="amount"/>, clamping negative values to zero.
        /// </summary>
        void SetGold(int amount);

        /// <summary>
        /// Returns the owned quantity for <paramref name="itemId"/>, or <c>0</c> if not present.
        /// </summary>
        int GetItemCount(string itemId);

        /// <summary>
        /// Adds <paramref name="delta"/> to the owned count of <paramref name="itemId"/>, clamping at zero.
        /// </summary>
        void ModifyItem(string itemId, int delta);

        /// <summary>
        /// Sets the owned count of <paramref name="itemId"/> to <paramref name="count"/>, removing the entry when zero or negative.
        /// </summary>
        void SetItemCount(string itemId, int count);

        /// <summary>
        /// Returns a read-only view of all item IDs with positive counts.
        /// </summary>
        IReadOnlyDictionary<string, int> GetAllItems();
    }

    /// <summary>
    /// Runtime execution context passed into every command during <see cref="EventCommandBase.OnStart"/>,
    /// <see cref="EventCommandBase.OnTick"/>, and <see cref="EventCommandBase.OnCancel"/>.
    /// </summary>
    public interface IEventExecutionContext
    {
        /// <summary>
        /// Active global state store for switches, variables, and self-switches.
        /// </summary>
        WorldStateStore WorldState { get; }

        /// <summary>
        /// Active inventory and gold store; defaults to <see cref="WorldState"/> unless overridden on the runner.
        /// </summary>
        IEventInventoryStore Inventory { get; }

        /// <summary>
        /// Optional dialogue handler; may be null in headless or test runs.
        /// </summary>
        IEventDialogueHandler Dialogue { get; }

        /// <summary>
        /// Optional screen transition handler; may be null in headless or test runs.
        /// </summary>
        IScreenTransitionHandler Screen { get; }

        /// <summary>
        /// Optional audio handler; may be null in headless or test runs.
        /// </summary>
        IEventAudioHandler Audio { get; }

        /// <summary>
        /// Registry mapping string keys to scene <see cref="GameObject"/> instances.
        /// </summary>
        SceneBindingRegistry SceneBindings { get; }

        /// <summary>
        /// Owner key of the active execution frame, used to scope Self-Switch lookups.
        /// </summary>
        string OwnerKey { get; }

        /// <summary>
        /// Current subroutine call stack depth for this process (1 for the root event).
        /// </summary>
        int CallDepth { get; }

        /// <summary>
        /// Attempts to read a frame-local variable or call argument by <paramref name="key"/>.
        /// </summary>
        /// <returns><c>true</c> if the key exists in the current frame; otherwise <c>false</c>.</returns>
        bool TryGetLocalVariable(string key, out EventValue value);

        /// <summary>
        /// Writes a variable scoped to the current call stack frame.
        /// </summary>
        void SetLocalVariable(string key, EventValue value);

        /// <summary>
        /// Broadcasts a named signal with an optional string payload through the runner signal bus.
        /// </summary>
        void EmitSignal(string signalName, string payload = "");

        /// <summary>
        /// Checks whether <paramref name="signalName"/> has been emitted after <paramref name="afterSequence"/>.
        /// </summary>
        /// <param name="signalName">Exact ordinal signal name to match.</param>
        /// <param name="afterSequence">Exclusive lower-bound sequence number captured before waiting.</param>
        /// <param name="payload">Receives the most recent matching signal payload, or an empty string if not found.</param>
        /// <returns><c>true</c> if a matching signal occurred after <paramref name="afterSequence"/>; otherwise <c>false</c>.</returns>
        bool ConsumeSignalSince(string signalName, long afterSequence, out string payload);

        /// <summary>
        /// Monotonically increasing sequence number of the latest emitted signal.
        /// </summary>
        long CurrentSignalSequence { get; }

        /// <summary>
        /// Requests that the runner set the current frame's instruction pointer to <paramref name="nextCommandIndex"/> once this command completes.
        /// </summary>
        void RequestJumpToIndex(int nextCommandIndex);

        /// <summary>
        /// Requests immediate termination of the current event call frame.
        /// </summary>
        void RequestExitEvent();

        /// <summary>
        /// Queues a subroutine call to <paramref name="subEvent"/> with optional local arguments once this command completes.
        /// </summary>
        void RequestSubEventCall(CommonEventAsset subEvent, Dictionary<string, EventValue> args = null);

        /// <summary>
        /// Appends a diagnostic message to the runner's log buffer and notifies listeners.
        /// </summary>
        void Log(string message);
    }

    /// <summary>
    /// Base class for all serializable commands inside a <see cref="CommonEventAsset"/> (<c>[SerializeReference]</c>).
    /// Subclass this type and decorate it with <see cref="EventCommandAttribute"/> so the Event Workbench discovers it automatically.
    /// </summary>
    [Serializable]
    public abstract class EventCommandBase
    {
        /// <summary>
        /// When <c>false</c>, the runner skips this command without calling <see cref="OnStart"/> or <see cref="OnTick"/>.
        /// </summary>
        public bool enabled = true;

        /// <summary>
        /// Optional designer note stored alongside the command.
        /// </summary>
        public string comment = string.Empty;

        /// <summary>
        /// Structural block role used to indent nested flow commands in the Event Workbench.
        /// </summary>
        public virtual CommandBlockRole BlockRole => CommandBlockRole.Normal;

        /// <summary>
        /// Returns a single-line human-readable summary shown in the Event Workbench command list.
        /// </summary>
        public virtual string GetDisplaySummary() => GetType().Name;

        /// <summary>
        /// Runs once when the instruction pointer enters this command.
        /// </summary>
        /// <param name="ctx">Execution context for the active event frame.</param>
        /// <returns><see cref="CommandStatus.Completed"/> to advance immediately, or <see cref="CommandStatus.Running"/> to continue ticking.</returns>
        public abstract CommandStatus OnStart(IEventExecutionContext ctx);

        /// <summary>
        /// Advances an active command while it remains in <see cref="CommandStatus.Running"/>.
        /// </summary>
        /// <param name="ctx">Execution context for the active event frame.</param>
        /// <param name="deltaTime">Elapsed tick time in seconds.</param>
        /// <returns><see cref="CommandStatus.Completed"/> when finished, or <see cref="CommandStatus.Running"/> to keep waiting.</returns>
        public virtual CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime) => CommandStatus.Completed;

        /// <summary>
        /// Invoked if the owning process is cancelled while this command is actively running.
        /// </summary>
        /// <param name="ctx">Execution context for the cancelled frame.</param>
        public virtual void OnCancel(IEventExecutionContext ctx) { }
    }
}
