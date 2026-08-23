using System;
using System.Collections.Generic;
using CommonEventsEngine.Data;
using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Core
{
    public enum CommonEventTriggerMode
    {
        Manual = 0,
        Autorun = 1,
        Parallel = 2
    }

    public enum SelfSwitchSlot
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3
    }

    public enum EventVariableType
    {
        Int = 0,
        Float = 1,
        String = 2
    }

    public enum CommandStatus
    {
        Running = 0,
        Completed = 1
    }

    public enum CommandBlockRole
    {
        Normal = 0,
        OpenBlock = 1,
        MidBlock = 2,
        CloseBlock = 3
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EventCommandAttribute : Attribute
    {
        public string MenuPath { get; }
        public string Description { get; }

        public EventCommandAttribute(string menuPath, string description = "")
        {
            MenuPath = menuPath;
            Description = description ?? string.Empty;
        }
    }

    [Serializable]
    public struct EventValue
    {
        public EventVariableType type;
        public int intValue;
        public float floatValue;
        public string stringValue;

        public static EventValue FromInt(int v) => new EventValue { type = EventVariableType.Int, intValue = v, floatValue = v, stringValue = v.ToString() };
        public static EventValue FromFloat(float v) => new EventValue { type = EventVariableType.Float, intValue = Mathf.RoundToInt(v), floatValue = v, stringValue = v.ToString("G") };
        public static EventValue FromString(string v) => new EventValue { type = EventVariableType.String, stringValue = v ?? string.Empty };

        public float AsFloat() => type == EventVariableType.Float ? floatValue : (type == EventVariableType.Int ? intValue : (float.TryParse(stringValue, out float f) ? f : 0f));
        public int AsInt() => type == EventVariableType.Int ? intValue : Mathf.RoundToInt(AsFloat());
        public string AsString() => type == EventVariableType.String ? (stringValue ?? string.Empty) : (type == EventVariableType.Float ? floatValue.ToString("G") : intValue.ToString());

        public override string ToString() => AsString();
    }

    /// <summary>
    /// Swappable dialogue and choice presentation contract.
    /// BuiltInDialogueBox provides a turnkey uGUI implementation; games can supply their own dialogue system.
    /// </summary>
    public interface IEventDialogueHandler
    {
        void ShowMessage(string speakerName, string message, Sprite portrait, Action onCompleted);
        void ShowChoices(string prompt, IReadOnlyList<string> choices, int cancelChoiceIndex, Action<int> onChoiceSelected);
        void CancelActiveDialogue();
    }

    /// <summary>
    /// Swappable screen transition, flash, and camera shake presentation contract.
    /// BuiltInScreenFader provides a turnkey uGUI + Camera implementation.
    /// </summary>
    public interface IScreenTransitionHandler
    {
        void StartFade(Color targetColor, float targetAlpha, float durationSeconds, Action onCompleted);
        void StartFlash(Color flashColor, float durationSeconds, Action onCompleted);
        void StartCameraShake(float intensity, float durationSeconds, Action onCompleted);
        void CancelTransitions();
    }

    /// <summary>
    /// Swappable audio playback contract for SFX and BGM commands.
    /// </summary>
    public interface IEventAudioHandler
    {
        void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f);
        void PlayBgm(AudioClip clip, float volume = 0.8f, bool loop = true);
        void StopBgm();
    }

    /// <summary>
    /// Swappable RPG inventory and gold ledger contract.
    /// </summary>
    public interface IEventInventoryStore
    {
        int Gold { get; }
        void ModifyGold(int delta);
        void SetGold(int amount);
        int GetItemCount(string itemId);
        void ModifyItem(string itemId, int delta);
        void SetItemCount(string itemId, int count);
        IReadOnlyDictionary<string, int> GetAllItems();
    }

    /// <summary>
    /// Runtime context passed into every command during Start, Tick, and Cancel.
    /// </summary>
    public interface IEventExecutionContext
    {
        WorldStateStore WorldState { get; }
        IEventInventoryStore Inventory { get; }
        IEventDialogueHandler Dialogue { get; }
        IScreenTransitionHandler Screen { get; }
        IEventAudioHandler Audio { get; }
        SceneBindingRegistry SceneBindings { get; }
        string OwnerKey { get; }
        int CallDepth { get; }

        bool TryGetLocalVariable(string key, out EventValue value);
        void SetLocalVariable(string key, EventValue value);
        void EmitSignal(string signalName, string payload = "");
        bool ConsumeSignalSince(string signalName, long afterSequence, out string payload);
        long CurrentSignalSequence { get; }
        void RequestJumpToIndex(int nextCommandIndex);
        void RequestExitEvent();
        void RequestSubEventCall(CommonEventAsset subEvent, Dictionary<string, EventValue> args = null);
        void Log(string message);
    }

    /// <summary>
    /// Base class for all serializable commands inside a CommonEventAsset ([SerializeReference]).
    /// Subclass this and decorate with [EventCommand("Category/Name")] to appear automatically in the Editor.
    /// </summary>
    [Serializable]
    public abstract class EventCommandBase
    {
        public bool enabled = true;
        public string comment = string.Empty;

        public virtual CommandBlockRole BlockRole => CommandBlockRole.Normal;

        public virtual string GetDisplaySummary() => GetType().Name;

        public abstract CommandStatus OnStart(IEventExecutionContext ctx);

        public virtual CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime) => CommandStatus.Completed;

        public virtual void OnCancel(IEventExecutionContext ctx) { }
    }
}
