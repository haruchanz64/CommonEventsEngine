using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using CommonEventsEngine.Engine;
using CommonEventsEngine.Presentation;
using CommonEventsEngine.State;
using UnityEngine;
using UnityEngine.Events;

namespace CommonEventsEngine.Components
{
    /// <summary>
    /// Serializable <see cref="UnityEvent{T0}"/> carrying a boolean payload (used for control-lock notifications).
    /// </summary>
    [Serializable] public sealed class BoolUnityEvent : UnityEvent<bool> { }

    /// <summary>
    /// Serializable <see cref="UnityEvent{T0, T1}"/> carrying a signal name and string payload.
    /// </summary>
    [Serializable] public sealed class SignalUnityEvent : UnityEvent<string, string> { }

    /// <summary>
    /// Serializable <see cref="UnityEvent{T0}"/> carrying a <see cref="CommonEventAsset"/> reference.
    /// </summary>
    [Serializable] public sealed class CommonEventUnityEvent : UnityEvent<CommonEventAsset> { }

    /// <summary>
    /// Serializable <see cref="UnityEvent{T0}"/> carrying an integer payload (used for Gold balance updates).
    /// </summary>
    [Serializable] public sealed class IntUnityEvent : UnityEvent<int> { }

    /// <summary>
    /// Scene MonoBehaviour hosting <see cref="CommonEventRunner"/>, <see cref="WorldStateDatabase"/> initialization,
    /// built-in or custom Dialogue/Screen/Audio handlers, and Inspector-bindable UnityEvents.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CommonEventDirector : MonoBehaviour
    {
        /// <summary>
        /// Currently active scene director instance, or null if no director has awakened.
        /// </summary>
        public static CommonEventDirector ActiveInstance { get; private set; }

        [Header("Database & Common Events")]
        [SerializeField] private WorldStateDatabase initialDatabase;
        [SerializeField] private List<CommonEventAsset> commonEvents = new List<CommonEventAsset>();
        [SerializeField] private bool useUnscaledDeltaTime = false;
        [SerializeField] private bool persistAcrossScenes = false;

        [Header("Optional Custom Handlers (Auto-discovered on this GameObject if empty)")]
        [SerializeField] private MonoBehaviour dialogueHandlerBehaviour;
        [SerializeField] private MonoBehaviour screenHandlerBehaviour;
        [SerializeField] private MonoBehaviour audioHandlerBehaviour;

        [Header("Inspector Events (Wire to CharacterMotor, Canvas UI, Quests, etc.)")]
        public BoolUnityEvent onControlLockChanged = new BoolUnityEvent();
        public SignalUnityEvent onSignalEmitted = new SignalUnityEvent();
        public CommonEventUnityEvent onEventStarted = new CommonEventUnityEvent();
        public CommonEventUnityEvent onEventCompleted = new CommonEventUnityEvent();
        public IntUnityEvent onGoldChanged = new IntUnityEvent();

        private CommonEventRunner _runner;

        /// <summary>
        /// Underlying <see cref="CommonEventRunner"/>, lazily initialized on first access via <see cref="EnsureInitialized"/>.
        /// </summary>
        public CommonEventRunner Runner
        {
            get
            {
                EnsureInitialized();
                return _runner;
            }
        }

        /// <summary>
        /// Active <see cref="WorldStateStore"/> owned by <see cref="Runner"/>.
        /// </summary>
        public WorldStateStore WorldState => Runner.WorldState;

        /// <summary>
        /// Active <see cref="SceneBindingRegistry"/> owned by <see cref="Runner"/>.
        /// </summary>
        public SceneBindingRegistry SceneBindings => Runner.SceneBindings;

        private void Awake()
        {
            if (ActiveInstance != null && ActiveInstance != this && persistAcrossScenes)
            {
                Destroy(gameObject);
                return;
            }

            ActiveInstance = this;
            if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (ActiveInstance == this) ActiveInstance = null;
        }

        private void Update()
        {
            if (_runner == null) return;
            float dt = useUnscaledDeltaTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _runner.Tick(dt);
        }

        /// <summary>
        /// Creates and configures the <see cref="CommonEventRunner"/> if not yet initialized, applying <c>initialDatabase</c>,
        /// registering <c>commonEvents</c>, resolving presentation handlers, and forwarding runner events to UnityEvents.
        /// </summary>
        public void EnsureInitialized()
        {
            if (_runner != null) return;

            var store = new WorldStateStore();
            if (initialDatabase != null)
            {
                initialDatabase.ApplyTo(store);
            }

            _runner = new CommonEventRunner(store);
            _runner.SetRegisteredEvents(commonEvents);

            _runner.DialogueHandler = ResolveHandler<IEventDialogueHandler>(dialogueHandlerBehaviour) ?? GetComponentInChildren<BuiltInDialogueBox>();
            _runner.ScreenHandler = ResolveHandler<IScreenTransitionHandler>(screenHandlerBehaviour) ?? GetComponentInChildren<BuiltInScreenFader>();
            _runner.AudioHandler = ResolveHandler<IEventAudioHandler>(audioHandlerBehaviour) ?? GetComponentInChildren<BuiltInEventAudio>();

            _runner.OnControlLockChanged += locked => onControlLockChanged?.Invoke(locked);
            _runner.OnSignalEmitted += (sig, payload) => onSignalEmitted?.Invoke(sig, payload);
            _runner.OnEventStarted += evt => onEventStarted?.Invoke(evt);
            _runner.OnEventCompleted += evt => onEventCompleted?.Invoke(evt);
            _runner.WorldState.OnGoldChanged += g => onGoldChanged?.Invoke(g);
        }

        /// <summary>
        /// Starts <paramref name="asset"/> immediately on <see cref="Runner"/>.
        /// </summary>
        public void TriggerEvent(CommonEventAsset asset) => Runner.StartEvent(asset);

        /// <summary>
        /// Starts the first registered event matching <paramref name="eventId"/> on <see cref="Runner"/>.
        /// </summary>
        public void TriggerEventById(string eventId) => Runner.StartEventById(eventId);

        /// <summary>
        /// Sets the named Global Switch to <c>true</c>. Bindable from UnityEvents.
        /// </summary>
        public void SetSwitchOn(string switchKey) => Runner.WorldState.SetSwitch(switchKey, true);

        /// <summary>
        /// Sets the named Global Switch to <c>false</c>. Bindable from UnityEvents.
        /// </summary>
        public void SetSwitchOff(string switchKey) => Runner.WorldState.SetSwitch(switchKey, false);

        /// <summary>
        /// Broadcasts <paramref name="signalName"/> with an empty payload on <see cref="Runner"/>. Bindable from UnityEvents.
        /// </summary>
        public void EmitSignal(string signalName) => Runner.EmitSignal(signalName);

        /// <summary>
        /// Exports the current <see cref="WorldState"/> to a pretty-printed JSON snapshot string.
        /// </summary>
        public string ExportSaveJson() => Runner.WorldState.ExportJson(prettyPrint: true);

        /// <summary>
        /// Replaces <see cref="WorldState"/> contents from a JSON snapshot previously produced by <see cref="ExportSaveJson"/>.
        /// </summary>
        public void ImportSaveJson(string json) => Runner.WorldState.ImportJson(json);

        /// <summary>
        /// Safely casts an Inspector-assigned <see cref="MonoBehaviour"/> to the requested handler interface.
        /// </summary>
        private static T ResolveHandler<T>(MonoBehaviour candidate) where T : class => candidate as T;
    }
}
