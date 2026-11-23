using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommonEventsEngine.Commands
{
    /// <summary>
    /// Playback action performed by <see cref="PlayAudioCommand"/>.
    /// </summary>
    public enum AudioCommandKind { PlaySfx = 0, PlayBgm = 1, StopBgm = 2 }

    /// <summary>
    /// Target opacity direction for <see cref="FadeScreenCommand"/>.
    /// </summary>
    public enum ScreenFadeDirection { FadeOutToOpaque = 0, FadeInToClear = 1 }

    /// <summary>
    /// Displays a dialogue line through <see cref="IEventExecutionContext.Dialogue"/> and optionally waits for player advance.
    /// Logs the line and completes immediately if no dialogue handler is bound.
    /// </summary>
    [Serializable]
    [EventCommand("Dialogue/Show Message", "Displays a dialogue message via IEventDialogueHandler and waits for player advance.")]
    public sealed class ShowMessageCommand : EventCommandBase
    {
        public string speakerName = "Narrator";
        [TextArea(2, 4)] public string message = "Hello, adventurer!";
        public Sprite portrait;
        public bool waitForCompletion = true;

        [NonSerialized] private bool _done;

        /// <inheritdoc/>
        public override string GetDisplaySummary()
        {
            string preview = string.IsNullOrEmpty(message) ? string.Empty : (message.Length > 36 ? message.Substring(0, 36) + "..." : message);
            return $"Show Message [{speakerName}]: \"{preview}\"";
        }

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.Dialogue == null)
            {
                ctx.Log($"[Dialogue] {speakerName}: {message}");
                return CommandStatus.Completed;
            }

            _done = false;
            ctx.Dialogue.ShowMessage(speakerName, message, portrait, () => _done = true);
            return !waitForCompletion || _done ? CommandStatus.Completed : CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime) =>
            _done ? CommandStatus.Completed : CommandStatus.Running;

        /// <inheritdoc/>
        public override void OnCancel(IEventExecutionContext ctx) => ctx.Dialogue?.CancelActiveDialogue();
    }

    /// <summary>
    /// Presents selectable choices through <see cref="IEventExecutionContext.Dialogue"/> and writes the chosen zero-based index into <see cref="resultVariableKey"/>.
    /// Defaults to index <c>0</c> if no dialogue handler is bound.
    /// </summary>
    [Serializable]
    [EventCommand("Dialogue/Show Choices", "Displays selectable choices via IEventDialogueHandler and stores the chosen index in a Variable.")]
    public sealed class ShowChoicesCommand : EventCommandBase
    {
        public string prompt = "Choose an option:";
        public List<string> choices = new List<string> { "Yes", "No" };
        public string resultVariableKey = "LastChoice";
        public int cancelChoiceIndex = 1;

        [NonSerialized] private bool _done;

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            $"Show Choices ({(choices != null ? choices.Count : 0)} opts) -> Var[{resultVariableKey}]";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (choices == null || choices.Count == 0) return CommandStatus.Completed;

            if (ctx.Dialogue == null)
            {
                ctx.WorldState.SetVariable(resultVariableKey, EventValue.FromInt(0));
                return CommandStatus.Completed;
            }

            _done = false;
            ctx.Dialogue.ShowChoices(prompt, choices, cancelChoiceIndex, idx =>
            {
                ctx.WorldState.SetVariable(resultVariableKey, EventValue.FromInt(idx));
                _done = true;
            });

            return _done ? CommandStatus.Completed : CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime) =>
            _done ? CommandStatus.Completed : CommandStatus.Running;

        /// <inheritdoc/>
        public override void OnCancel(IEventExecutionContext ctx) => ctx.Dialogue?.CancelActiveDialogue();
    }

    /// <summary>
    /// Drives a full-screen fade to opaque or clear through <see cref="IEventExecutionContext.Screen"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Screen/Fade Screen", "Fades the screen out or in via IScreenTransitionHandler.")]
    public sealed class FadeScreenCommand : EventCommandBase
    {
        public ScreenFadeDirection direction = ScreenFadeDirection.FadeOutToOpaque;
        public Color fadeColor = Color.black;
        [Min(0f)] public float durationSeconds = 0.5f;
        public bool waitForCompletion = true;

        [NonSerialized] private bool _done;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Fade Screen ({direction}, {durationSeconds:F2}s)";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.Screen == null) return CommandStatus.Completed;
            _done = false;
            float targetAlpha = direction == ScreenFadeDirection.FadeOutToOpaque ? 1f : 0f;
            ctx.Screen.StartFade(fadeColor, targetAlpha, durationSeconds, () => _done = true);
            return !waitForCompletion || _done ? CommandStatus.Completed : CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime) =>
            _done ? CommandStatus.Completed : CommandStatus.Running;
    }

    /// <summary>
    /// Plays a brief full-screen color flash pulse through <see cref="IEventExecutionContext.Screen"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Screen/Flash Screen", "Flashes the screen with a color pulse via IScreenTransitionHandler.")]
    public sealed class FlashScreenCommand : EventCommandBase
    {
        public Color flashColor = Color.white;
        [Min(0.05f)] public float durationSeconds = 0.3f;
        public bool waitForCompletion = false;

        [NonSerialized] private bool _done;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Flash Screen ({durationSeconds:F2}s)";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.Screen == null) return CommandStatus.Completed;
            _done = false;
            ctx.Screen.StartFlash(flashColor, durationSeconds, () => _done = true);
            return !waitForCompletion || _done ? CommandStatus.Completed : CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime) =>
            _done ? CommandStatus.Completed : CommandStatus.Running;
    }

    /// <summary>
    /// Shakes the target camera through <see cref="IEventExecutionContext.Screen"/> for <see cref="durationSeconds"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Screen/Shake Camera", "Shakes the active camera via IScreenTransitionHandler.")]
    public sealed class ShakeCameraCommand : EventCommandBase
    {
        [Min(0.01f)] public float intensity = 0.25f;
        [Min(0.05f)] public float durationSeconds = 0.4f;
        public bool waitForCompletion = true;

        [NonSerialized] private bool _done;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Shake Camera (intensity {intensity:F2}, {durationSeconds:F2}s)";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.Screen == null) return CommandStatus.Completed;
            _done = false;
            ctx.Screen.StartCameraShake(intensity, durationSeconds, () => _done = true);
            return !waitForCompletion || _done ? CommandStatus.Completed : CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime) =>
            _done ? CommandStatus.Completed : CommandStatus.Running;
    }

    /// <summary>
    /// Plays a one-shot SFX clip, starts BGM playback, or stops BGM through <see cref="IEventExecutionContext.Audio"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Audio/Play or Stop Audio", "Plays SFX, starts looping BGM, or stops BGM via IEventAudioHandler.")]
    public sealed class PlayAudioCommand : EventCommandBase
    {
        public AudioCommandKind kind = AudioCommandKind.PlaySfx;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.5f, 2f)] public float pitch = 1f;
        public bool loopBgm = true;

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            kind == AudioCommandKind.StopBgm ? "Stop BGM" : $"{kind}: {(clip != null ? clip.name : "(None)")}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.Audio == null) return CommandStatus.Completed;
            switch (kind)
            {
                case AudioCommandKind.PlaySfx: ctx.Audio.PlaySfx(clip, volume, pitch); break;
                case AudioCommandKind.PlayBgm: ctx.Audio.PlayBgm(clip, volume, loopBgm); break;
                case AudioCommandKind.StopBgm: ctx.Audio.StopBgm(); break;
            }
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Emits a named signal with an optional string payload onto the runner signal bus.
    /// </summary>
    [Serializable]
    [EventCommand("Scene/Emit Signal", "Broadcasts a decoupled signal with an optional string payload.")]
    public sealed class EmitSignalCommand : EventCommandBase
    {
        public string signalName = "quest_updated";
        public string payload = string.Empty;

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            string.IsNullOrEmpty(payload) ? $"Emit Signal '{signalName}'" : $"Emit Signal '{signalName}' ({payload})";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            ctx.EmitSignal(signalName, payload);
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Calls <see cref="GameObject.SetActive"/> on a scene object registered under <see cref="bindingKey"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Scene/Set Object Active", "Activates or deactivates a scene object registered in SceneBindingRegistry.")]
    public sealed class SetObjectActiveCommand : EventCommandBase
    {
        public string bindingKey = "Door_01";
        public bool active = true;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Set Active '{bindingKey}' = {active}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.SceneBindings != null && ctx.SceneBindings.TryGet(bindingKey, out GameObject go))
            {
                go.SetActive(active);
            }
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Moves a bound scene object to a bound destination marker's position and rotation, or falls back to <see cref="worldPosition"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Scene/Teleport Object", "Moves a registered scene object to another marker binding or world position.")]
    public sealed class TeleportObjectCommand : EventCommandBase
    {
        public string targetBindingKey = "Player";
        public string destinationMarkerKey = string.Empty;
        public Vector3 worldPosition;

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            string.IsNullOrEmpty(destinationMarkerKey)
                ? $"Teleport '{targetBindingKey}' -> {worldPosition}"
                : $"Teleport '{targetBindingKey}' -> Marker '{destinationMarkerKey}'";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.SceneBindings != null && ctx.SceneBindings.TryGet(targetBindingKey, out GameObject target))
            {
                if (!string.IsNullOrEmpty(destinationMarkerKey) && ctx.SceneBindings.TryGet(destinationMarkerKey, out GameObject marker))
                {
                    target.transform.SetPositionAndRotation(marker.transform.position, marker.transform.rotation);
                }
                else
                {
                    target.transform.position = worldPosition;
                }
            }
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Instantiates <see cref="prefab"/> at a bound spawn marker's transform or at <see cref="fallbackPosition"/> with identity rotation.
    /// </summary>
    [Serializable]
    [EventCommand("Scene/Spawn Prefab", "Instantiates a prefab at a registered spawn marker or world position.")]
    public sealed class SpawnPrefabCommand : EventCommandBase
    {
        public GameObject prefab;
        public string spawnPointBindingKey = string.Empty;
        public Vector3 fallbackPosition;

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            $"Spawn '{(prefab != null ? prefab.name : "(None)")}'";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (prefab == null) return CommandStatus.Completed;
            Vector3 pos = fallbackPosition;
            Quaternion rot = Quaternion.identity;
            if (!string.IsNullOrEmpty(spawnPointBindingKey) &&
                ctx.SceneBindings != null &&
                ctx.SceneBindings.TryGet(spawnPointBindingKey, out GameObject marker))
            {
                pos = marker.transform.position;
                rot = marker.transform.rotation;
            }
            UnityEngine.Object.Instantiate(prefab, pos, rot);
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Loads a Unity scene by <see cref="sceneName"/> via <see cref="SceneManager.LoadScene(string, LoadSceneMode)"/> during Play Mode.
    /// No-ops safely in EditMode.
    /// </summary>
    [Serializable]
    [EventCommand("Scene/Load Scene", "Loads a Unity scene by name (in PlayMode).")]
    public sealed class LoadSceneCommand : EventCommandBase
    {
        public string sceneName = "TownScene";
        public LoadSceneMode loadMode = LoadSceneMode.Single;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Load Scene '{sceneName}' ({loadMode})";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (Application.isPlaying && !string.IsNullOrWhiteSpace(sceneName))
            {
                SceneManager.LoadScene(sceneName, loadMode);
            }
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Appends <see cref="message"/> to the runner's diagnostic log buffer via <see cref="IEventExecutionContext.Log"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Debug/Log Message", "Writes a diagnostic message to the runner log and Unity Console.")]
    public sealed class LogCommand : EventCommandBase
    {
        public string message = "Event checkpoint reached.";

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Log \"{message}\"";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            ctx.Log(message);
            return CommandStatus.Completed;
        }
    }
}
