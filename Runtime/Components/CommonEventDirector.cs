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
    [Serializable] public sealed class BoolUnityEvent : UnityEvent<bool> { }
    [Serializable] public sealed class SignalUnityEvent : UnityEvent<string, string> { }
    [Serializable] public sealed class CommonEventUnityEvent : UnityEvent<CommonEventAsset> { }
    [Serializable] public sealed class IntUnityEvent : UnityEvent<int> { }

    /// <summary>
    /// Scene MonoBehaviour hosting CommonEventRunner, WorldStateDatabase initialization,
    /// built-in or custom Dialogue/Screen/Audio handlers, and Inspector-bindable UnityEvents.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CommonEventDirector : MonoBehaviour
    {
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

        public CommonEventRunner Runner
        {
            get
            {
                EnsureInitialized();
                return _runner;
            }
        }

        public WorldStateStore WorldState => Runner.WorldState;
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

        public void TriggerEvent(CommonEventAsset asset) => Runner.StartEvent(asset);
        public void TriggerEventById(string eventId) => Runner.StartEventById(eventId);
        public void SetSwitchOn(string switchKey) => Runner.WorldState.SetSwitch(switchKey, true);
        public void SetSwitchOff(string switchKey) => Runner.WorldState.SetSwitch(switchKey, false);
        public void EmitSignal(string signalName) => Runner.EmitSignal(signalName);

        public string ExportSaveJson() => Runner.WorldState.ExportJson(prettyPrint: true);
        public void ImportSaveJson(string json) => Runner.WorldState.ImportJson(json);

        private static T ResolveHandler<T>(MonoBehaviour candidate) where T : class => candidate as T;
    }
}
