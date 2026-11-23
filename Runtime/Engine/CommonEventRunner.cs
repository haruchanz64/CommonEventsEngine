using System;
using System.Collections.Generic;
using CommonEventsEngine.Commands;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Engine
{
    /// <summary>
    /// Immutable-style record of a single signal broadcast stored in <see cref="CommonEventRunner.SignalHistory"/>.
    /// </summary>
    public sealed class SignalRecord
    {
        /// <summary>
        /// Monotonically increasing sequence number assigned when the signal was emitted.
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        /// Exact signal identifier passed to <see cref="CommonEventRunner.EmitSignal"/>.
        /// </summary>
        public string SignalName { get; set; }

        /// <summary>
        /// Optional non-null string payload attached to the signal.
        /// </summary>
        public string Payload { get; set; }
    }

    /// <summary>
    /// Represents one active subroutine call frame on an <see cref="ActiveEventProcess.CallStack"/>.
    /// </summary>
    public sealed class EventExecutionFrame
    {
        /// <summary>
        /// Event asset currently executing in this frame.
        /// </summary>
        public CommonEventAsset EventAsset { get; }

        /// <summary>
        /// Zero-based index of the current or next command in <see cref="CommonEventAsset.commands"/>.
        /// </summary>
        public int InstructionPointer { get; set; }

        /// <summary>
        /// Whether <see cref="EventCommandBase.OnStart"/> has already been invoked for the command at <see cref="InstructionPointer"/>.
        /// </summary>
        public bool CurrentCommandStarted { get; set; }

        /// <summary>
        /// Owner key propagated to Self-Switch evaluations inside this frame.
        /// </summary>
        public string OwnerKey { get; }

        /// <summary>
        /// Frame-scoped local variables initialized from subroutine call arguments.
        /// </summary>
        public Dictionary<string, EventValue> LocalVariables { get; }

        /// <summary>
        /// Tracks completed iteration counts keyed by each <see cref="LoopCommand"/> instruction index.
        /// </summary>
        public Dictionary<int, int> LoopIterationCounts { get; } = new Dictionary<int, int>();

        /// <summary>
        /// Initializes a new execution frame at instruction index 0.
        /// </summary>
        /// <param name="asset">Event asset executed by this frame.</param>
        /// <param name="ownerKey">Optional owner key for Self-Switch lookups; null becomes an empty string.</param>
        /// <param name="args">Optional initial local variables copied into <see cref="LocalVariables"/>.</param>
        public EventExecutionFrame(CommonEventAsset asset, string ownerKey, Dictionary<string, EventValue> args)
        {
            EventAsset = asset;
            OwnerKey = ownerKey ?? string.Empty;
            LocalVariables = args != null
                ? new Dictionary<string, EventValue>(args, StringComparer.Ordinal)
                : new Dictionary<string, EventValue>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Tracks a running instance of a <see cref="CommonEventAsset"/> and its nested subroutine call stack.
    /// </summary>
    public sealed class ActiveEventProcess
    {
        /// <summary>
        /// Short 8-character unique identifier assigned to this process instance.
        /// </summary>
        public string ProcessId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>
        /// Root event asset that initiated this process.
        /// </summary>
        public CommonEventAsset RootEvent { get; }

        /// <summary>
        /// Whether this process runs in the exclusive Autorun queue and may lock player control.
        /// </summary>
        public bool IsAutorun { get; }

        /// <summary>
        /// Whether this process runs as a concurrent Parallel background event.
        /// </summary>
        public bool IsParallel { get; }

        /// <summary>
        /// Active LIFO stack of <see cref="EventExecutionFrame"/> entries; empty once the process completes.
        /// </summary>
        public Stack<EventExecutionFrame> CallStack { get; } = new Stack<EventExecutionFrame>();

        /// <summary>
        /// <c>true</c> when all frames have popped from <see cref="CallStack"/>.
        /// </summary>
        public bool IsFinished => CallStack.Count == 0;

        /// <summary>
        /// Top-most frame on <see cref="CallStack"/>, or null if <see cref="IsFinished"/> is <c>true</c>.
        /// </summary>
        public EventExecutionFrame CurrentFrame => CallStack.Count > 0 ? CallStack.Peek() : null;

        /// <summary>
        /// Creates a new active process and pushes the initial frame for <paramref name="rootEvent"/>.
        /// </summary>
        /// <param name="rootEvent">Entry-point event asset.</param>
        /// <param name="ownerKey">Optional Self-Switch owner key for the root frame.</param>
        /// <param name="args">Optional initial local variables for the root frame.</param>
        /// <param name="isAutorun">Marks the process as an exclusive Autorun process.</param>
        /// <param name="isParallel">Marks the process as a concurrent Parallel process.</param>
        public ActiveEventProcess(CommonEventAsset rootEvent, string ownerKey, Dictionary<string, EventValue> args, bool isAutorun, bool isParallel)
        {
            RootEvent = rootEvent;
            IsAutorun = isAutorun;
            IsParallel = isParallel;
            CallStack.Push(new EventExecutionFrame(rootEvent, ownerKey, args));
        }
    }

    /// <summary>
    /// Deterministic, tick-driven RPG Maker-style Common Events execution engine.
    /// Runs Manual calls, exclusive Autorun events (with player control lock events), and concurrent Parallel loops.
    /// </summary>
    public sealed class CommonEventRunner
    {
        private sealed class FrameContext : IEventExecutionContext
        {
            private readonly CommonEventRunner _runner;
            private readonly ActiveEventProcess _process;
            private readonly EventExecutionFrame _frame;

            public int? PendingJumpIndex { get; private set; }
            public bool PendingExitEvent { get; private set; }
            public (CommonEventAsset sub, Dictionary<string, EventValue> args)? PendingSubCall { get; private set; }

            public WorldStateStore WorldState => _runner.WorldState;
            public IEventInventoryStore Inventory => _runner.Inventory;
            public IEventDialogueHandler Dialogue => _runner.DialogueHandler;
            public IScreenTransitionHandler Screen => _runner.ScreenHandler;
            public IEventAudioHandler Audio => _runner.AudioHandler;
            public SceneBindingRegistry SceneBindings => _runner.SceneBindings;
            public string OwnerKey => _frame.OwnerKey;
            public int CallDepth => _process.CallStack.Count;
            public long CurrentSignalSequence => _runner.SignalSequence;

            public FrameContext(CommonEventRunner runner, ActiveEventProcess process, EventExecutionFrame frame)
            {
                _runner = runner;
                _process = process;
                _frame = frame;
            }

            public bool TryGetLocalVariable(string key, out EventValue value) =>
                _frame.LocalVariables.TryGetValue(key, out value);

            public void SetLocalVariable(string key, EventValue value)
            {
                if (!string.IsNullOrWhiteSpace(key)) _frame.LocalVariables[key] = value;
            }

            public void EmitSignal(string signalName, string payload = "") => _runner.EmitSignal(signalName, payload);

            public bool ConsumeSignalSince(string signalName, long afterSequence, out string payload) =>
                _runner.TryFindSignalSince(signalName, afterSequence, out payload);

            public void RequestJumpToIndex(int nextCommandIndex) => PendingJumpIndex = nextCommandIndex;
            public void RequestExitEvent() => PendingExitEvent = true;
            public void RequestSubEventCall(CommonEventAsset subEvent, Dictionary<string, EventValue> args = null) =>
                PendingSubCall = (subEvent, args);
            public void Log(string message) => _runner.AddLog(message);
        }

        private readonly List<CommonEventAsset> _registeredEvents = new List<CommonEventAsset>();
        private readonly List<ActiveEventProcess> _activeProcesses = new List<ActiveEventProcess>();
        private readonly List<SignalRecord> _signalHistory = new List<SignalRecord>(64);
        private readonly List<string> _logs = new List<string>(128);
        private bool _isControlLocked;

        /// <summary>
        /// Central store holding Global Switches, Variables, Self-Switches, and default Inventory/Gold state.
        /// </summary>
        public WorldStateStore WorldState { get; }

        /// <summary>
        /// Inventory and gold store exposed to conditions and commands; defaults to <see cref="WorldState"/>.
        /// </summary>
        public IEventInventoryStore Inventory { get; set; }

        /// <summary>
        /// Optional presentation handler for dialogue messages and choices.
        /// </summary>
        public IEventDialogueHandler DialogueHandler { get; set; }

        /// <summary>
        /// Optional presentation handler for screen fades, flashes, and camera shakes.
        /// </summary>
        public IScreenTransitionHandler ScreenHandler { get; set; }

        /// <summary>
        /// Optional handler for SFX and BGM playback commands.
        /// </summary>
        public IEventAudioHandler AudioHandler { get; set; }

        /// <summary>
        /// Registry resolving string binding keys to scene <see cref="GameObject"/> instances.
        /// </summary>
        public SceneBindingRegistry SceneBindings { get; }

        /// <summary>
        /// Maximum nested <see cref="CallCommonEventCommand"/> call stack depth before subroutine calls are blocked with a warning log. Defaults to <c>16</c>.
        /// </summary>
        public int MaxCallDepth { get; set; } = 16;

        /// <summary>
        /// Maximum instant commands executed per process during a single <see cref="Tick"/> call to guard against runaway loops. Defaults to <c>128</c>.
        /// </summary>
        public int MaxInstantCommandsPerTick { get; set; } = 128;

        /// <summary>
        /// Sequence counter incremented on every <see cref="EmitSignal"/> call.
        /// </summary>
        public long SignalSequence { get; private set; }

        /// <summary>
        /// <c>true</c> while any active Autorun process with <see cref="CommonEventAsset.lockPlayerControlOnAutorun"/> enabled is running.
        /// </summary>
        public bool IsControlLocked => _isControlLocked;

        /// <summary>
        /// When <c>true</c>, <see cref="Tick"/> returns immediately without evaluating triggers or advancing processes.
        /// </summary>
        public bool IsPaused { get; set; }

        /// <summary>
        /// Events registered for automatic Autorun/Parallel evaluation and <see cref="StartEventById"/> lookup.
        /// </summary>
        public IReadOnlyList<CommonEventAsset> RegisteredEvents => _registeredEvents;

        /// <summary>
        /// Currently running processes across Manual, Autorun, and Parallel trigger modes.
        /// </summary>
        public IReadOnlyList<ActiveEventProcess> ActiveProcesses => _activeProcesses;

        /// <summary>
        /// Rolling history of up to the 200 most recent emitted signals.
        /// </summary>
        public IReadOnlyList<SignalRecord> SignalHistory => _signalHistory;

        /// <summary>
        /// Rolling buffer of up to the 256 most recent diagnostic log messages.
        /// </summary>
        public IReadOnlyList<string> Logs => _logs;

        /// <summary>
        /// Raised whenever <see cref="IsControlLocked"/> transitions between <c>false</c> and <c>true</c>.
        /// </summary>
        public event Action<bool> OnControlLockChanged;

        /// <summary>
        /// Raised with the signal name and payload whenever <see cref="EmitSignal"/> broadcasts a signal.
        /// </summary>
        public event Action<string, string> OnSignalEmitted;

        /// <summary>
        /// Raised whenever a new process is started via <see cref="StartEvent"/> or automatic trigger evaluation.
        /// </summary>
        public event Action<CommonEventAsset> OnEventStarted;

        /// <summary>
        /// Raised after a process finishes all frames and is removed from <see cref="ActiveProcesses"/>.
        /// </summary>
        public event Action<CommonEventAsset> OnEventCompleted;

        /// <summary>
        /// Raised whenever a command or runner warning appends a message to <see cref="Logs"/>.
        /// </summary>
        public event Action<string> OnLogMessage;

        /// <summary>
        /// Initializes a new runner with optional pre-existing state and scene binding registries.
        /// </summary>
        /// <param name="worldState">Optional state store; a fresh <see cref="WorldStateStore"/> is created if null.</param>
        /// <param name="sceneBindings">Optional scene binding registry; a fresh <see cref="SceneBindingRegistry"/> is created if null.</param>
        public CommonEventRunner(WorldStateStore worldState = null, SceneBindingRegistry sceneBindings = null)
        {
            WorldState = worldState ?? new WorldStateStore();
            Inventory = WorldState;
            SceneBindings = sceneBindings ?? new SceneBindingRegistry();
        }

        /// <summary>
        /// Registers an event asset for automatic Autorun/Parallel trigger evaluation and lookup by <see cref="CommonEventAsset.eventId"/>.
        /// Duplicate or null assets are ignored.
        /// </summary>
        public void RegisterEvent(CommonEventAsset asset)
        {
            if (asset != null && !_registeredEvents.Contains(asset))
            {
                _registeredEvents.Add(asset);
            }
        }

        /// <summary>
        /// Replaces all registered events with the non-null unique entries from <paramref name="assets"/>.
        /// </summary>
        public void SetRegisteredEvents(IEnumerable<CommonEventAsset> assets)
        {
            _registeredEvents.Clear();
            if (assets == null) return;
            foreach (var a in assets) RegisterEvent(a);
        }

        /// <summary>
        /// Starts a new <see cref="ActiveEventProcess"/> for <paramref name="asset"/> and updates the control-lock state.
        /// </summary>
        /// <param name="asset">Event asset to run; returns null if null or if its command list is empty.</param>
        /// <param name="ownerKey">Optional owner key used for Self-Switch lookups.</param>
        /// <param name="args">Optional initial local variables passed into the root frame.</param>
        /// <param name="forceAutorun">When <c>true</c>, schedules the process in the exclusive Autorun queue regardless of the asset's trigger mode.</param>
        /// <returns>The created process handle, or null if <paramref name="asset"/> has no commands.</returns>
        public ActiveEventProcess StartEvent(
            CommonEventAsset asset,
            string ownerKey = null,
            Dictionary<string, EventValue> args = null,
            bool forceAutorun = false)
        {
            if (asset == null || asset.commands == null || asset.commands.Count == 0) return null;

            bool isAutorun = forceAutorun || asset.triggerMode == CommonEventTriggerMode.Autorun;
            bool isParallel = !isAutorun && asset.triggerMode == CommonEventTriggerMode.Parallel;

            var process = new ActiveEventProcess(asset, ownerKey, args, isAutorun, isParallel);
            _activeProcesses.Add(process);
            OnEventStarted?.Invoke(asset);
            UpdateControlLockState();
            return process;
        }

        /// <summary>
        /// Finds the first registered event matching <paramref name="eventId"/> (ordinal comparison) and starts it.
        /// </summary>
        /// <returns>The started <see cref="ActiveEventProcess"/>, or null if no matching registered event exists.</returns>
        public ActiveEventProcess StartEventById(string eventId, string ownerKey = null, Dictionary<string, EventValue> args = null)
        {
            for (int i = 0; i < _registeredEvents.Count; i++)
            {
                if (string.Equals(_registeredEvents[i].eventId, eventId, StringComparison.Ordinal))
                {
                    return StartEvent(_registeredEvents[i], ownerKey, args);
                }
            }
            return null;
        }

        /// <summary>
        /// Records a named signal in <see cref="SignalHistory"/> (capped at 200 entries), increments <see cref="SignalSequence"/>,
        /// and raises <see cref="OnSignalEmitted"/>. Ignores null or whitespace signal names.
        /// </summary>
        public void EmitSignal(string signalName, string payload = "")
        {
            if (string.IsNullOrWhiteSpace(signalName)) return;
            SignalSequence++;
            _signalHistory.Add(new SignalRecord
            {
                Sequence = SignalSequence,
                SignalName = signalName,
                Payload = payload ?? string.Empty
            });
            if (_signalHistory.Count > 200) _signalHistory.RemoveAt(0);
            OnSignalEmitted?.Invoke(signalName, payload ?? string.Empty);
        }

        /// <summary>
        /// Searches <see cref="SignalHistory"/> from newest to oldest for <paramref name="signalName"/> with a sequence strictly greater than <paramref name="afterSequence"/>.
        /// </summary>
        /// <param name="signalName">Exact ordinal signal name to match.</param>
        /// <param name="afterSequence">Exclusive lower bound sequence number.</param>
        /// <param name="payload">Receives the matched payload, or an empty string if no matching signal is found.</param>
        /// <returns><c>true</c> if a matching signal was emitted after <paramref name="afterSequence"/>; otherwise <c>false</c>.</returns>
        public bool TryFindSignalSince(string signalName, long afterSequence, out string payload)
        {
            for (int i = _signalHistory.Count - 1; i >= 0; i--)
            {
                var rec = _signalHistory[i];
                if (rec.Sequence <= afterSequence) break;
                if (string.Equals(rec.SignalName, signalName, StringComparison.Ordinal))
                {
                    payload = rec.Payload;
                    return true;
                }
            }
            payload = string.Empty;
            return false;
        }

        /// <summary>
        /// Cancels every active process (invoking <see cref="EventCommandBase.OnCancel"/> on active commands),
        /// clears <see cref="ActiveProcesses"/>, and releases any active control lock.
        /// </summary>
        public void CancelAllProcesses()
        {
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                CancelProcess(_activeProcesses[i]);
            }
            _activeProcesses.Clear();
            UpdateControlLockState();
        }

        /// <summary>
        /// Clears all entries from <see cref="Logs"/>.
        /// </summary>
        public void ClearLogs() => _logs.Clear();

        /// <summary>
        /// Evaluates automatic Autorun/Parallel triggers and advances all active processes by <paramref name="deltaTime"/> seconds.
        /// Does nothing when <see cref="IsPaused"/> is <c>true</c>.
        /// </summary>
        /// <param name="deltaTime">Elapsed time in seconds for this tick.</param>
        public void Tick(float deltaTime)
        {
            if (IsPaused) return;

            EvaluateAutomaticTriggers();

            bool autorunRanThisTick = false;
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                var proc = _activeProcesses[i];
                if (proc.IsFinished) continue;

                // Only the first active Autorun runs at a time (classic RPG Maker exclusive Autorun queue)
                if (proc.IsAutorun)
                {
                    if (autorunRanThisTick) continue;
                    autorunRanThisTick = true;
                }

                AdvanceProcess(proc, deltaTime, maxCommands: MaxInstantCommandsPerTick);
            }

            CleanupFinishedProcesses();
        }

        /// <summary>
        /// Executes at most one command step on <paramref name="process"/> (used by the UI Toolkit Live Step Debugger).
        /// </summary>
        /// <param name="process">Target active process to step.</param>
        /// <param name="deltaTime">Elapsed time in seconds passed if the active command is ticked.</param>
        /// <returns><c>true</c> if <paramref name="process"/> still has remaining work; <c>false</c> if it is null or finished.</returns>
        public bool StepSingleCommand(ActiveEventProcess process, float deltaTime = 0.016f)
        {
            if (process == null || process.IsFinished) return false;
            AdvanceProcess(process, deltaTime, maxCommands: 1);
            CleanupFinishedProcesses();
            return !process.IsFinished;
        }

        /// <summary>
        /// Scans registered Autorun and Parallel events, starting eligible events whose conditions are met
        /// and cancelling running Parallel events whose conditions have become false.
        /// </summary>
        private void EvaluateAutomaticTriggers()
        {
            bool hasRunningAutorun = HasRunningAutorun();

            for (int i = 0; i < _registeredEvents.Count; i++)
            {
                var evt = _registeredEvents[i];
                if (evt == null || evt.triggerMode == CommonEventTriggerMode.Manual) continue;
                if (evt.triggerConditions == null || evt.triggerConditions.Count == 0) continue;

                bool condMet = EventCondition.EvaluateAll(evt.triggerConditions, evt.conditionMode, WorldState, Inventory);

                if (evt.triggerMode == CommonEventTriggerMode.Autorun)
                {
                    if (condMet && !hasRunningAutorun && !IsEventCurrentlyRunning(evt))
                    {
                        StartEvent(evt);
                        hasRunningAutorun = true;
                    }
                }
                else if (evt.triggerMode == CommonEventTriggerMode.Parallel)
                {
                    if (condMet && !IsEventCurrentlyRunning(evt))
                    {
                        StartEvent(evt);
                    }
                    else if (!condMet)
                    {
                        CancelRunningParallel(evt);
                    }
                }
            }
        }

        /// <summary>
        /// Steps <paramref name="proc"/> through up to <paramref name="maxCommands"/> commands, handling structural
        /// If/Else/Loop control flow directly and delegating other commands to <see cref="EventCommandBase.OnStart"/> and <see cref="EventCommandBase.OnTick"/>.
        /// </summary>
        private void AdvanceProcess(ActiveEventProcess proc, float deltaTime, int maxCommands)
        {
            int executed = 0;
            bool consumedTickDelta = false;
            while (!proc.IsFinished && executed < maxCommands)
            {
                var frame = proc.CurrentFrame;
                var list = frame.EventAsset.commands;

                if (frame.InstructionPointer < 0 || frame.InstructionPointer >= list.Count)
                {
                    proc.CallStack.Pop();
                    continue;
                }

                var cmd = list[frame.InstructionPointer];
                if (cmd == null || !cmd.enabled)
                {
                    frame.InstructionPointer++;
                    frame.CurrentCommandStarted = false;
                    continue;
                }

                // Handle structural control flow directly in the runner
                if (cmd is IfCommand ifCmd)
                {
                    executed++;
                    var ctx = new FrameContext(this, proc, frame);
                    bool passed = ifCmd.condition == null || ifCmd.condition.Evaluate(ctx);
                    if (passed)
                    {
                        frame.InstructionPointer++;
                    }
                    else
                    {
                        int elseOrEnd = FindMatchingElseOrEndIf(list, frame.InstructionPointer);
                        frame.InstructionPointer = elseOrEnd + 1;
                    }
                    frame.CurrentCommandStarted = false;
                    continue;
                }

                if (cmd is ElseCommand)
                {
                    executed++;
                    int endIf = FindMatchingEndIf(list, frame.InstructionPointer);
                    frame.InstructionPointer = endIf + 1;
                    frame.CurrentCommandStarted = false;
                    continue;
                }

                if (cmd is EndIfCommand)
                {
                    executed++;
                    frame.InstructionPointer++;
                    frame.CurrentCommandStarted = false;
                    continue;
                }

                if (cmd is LoopCommand loopCmd)
                {
                    executed++;
                    int loopIdx = frame.InstructionPointer;
                    frame.LoopIterationCounts.TryGetValue(loopIdx, out int count);
                    if (loopCmd.maxIterations > 0 && count >= loopCmd.maxIterations)
                    {
                        frame.LoopIterationCounts.Remove(loopIdx);
                        int endLoop = FindMatchingEndLoop(list, loopIdx);
                        frame.InstructionPointer = endLoop + 1;
                    }
                    else
                    {
                        frame.LoopIterationCounts[loopIdx] = count + 1;
                        frame.InstructionPointer++;
                    }
                    frame.CurrentCommandStarted = false;
                    continue;
                }

                if (cmd is BreakLoopCommand)
                {
                    executed++;
                    int enclosingEndLoop = FindEnclosingEndLoop(list, frame.InstructionPointer);
                    int enclosingStart = FindEnclosingLoopStart(list, frame.InstructionPointer);
                    if (enclosingStart >= 0) frame.LoopIterationCounts.Remove(enclosingStart);
                    frame.InstructionPointer = enclosingEndLoop + 1;
                    frame.CurrentCommandStarted = false;
                    continue;
                }

                if (cmd is EndLoopCommand)
                {
                    executed++;
                    int loopStart = FindEnclosingLoopStart(list, frame.InstructionPointer);
                    if (loopStart >= 0)
                    {
                        frame.InstructionPointer = loopStart;
                        // Yield frame after an infinite loop cycle to prevent freezing the main thread
                        if (list[loopStart] is LoopCommand lc && lc.maxIterations <= 0)
                        {
                            frame.CurrentCommandStarted = false;
                            break;
                        }
                    }
                    else
                    {
                        frame.InstructionPointer++;
                    }
                    frame.CurrentCommandStarted = false;
                    continue;
                }

                // Regular or custom EventCommandBase execution
                var execCtx = new FrameContext(this, proc, frame);
                CommandStatus status;

                if (!frame.CurrentCommandStarted)
                {
                    frame.CurrentCommandStarted = true;
                    status = cmd.OnStart(execCtx);
                    executed++;

                    // Apply this tick's deltaTime once so timed waits advance during the frame they start
                    if (status == CommandStatus.Running && !consumedTickDelta && deltaTime > 0f && maxCommands > 1)
                    {
                        consumedTickDelta = true;
                        status = cmd.OnTick(execCtx, deltaTime);
                    }
                }
                else
                {
                    consumedTickDelta = true;
                    status = cmd.OnTick(execCtx, deltaTime);
                    if (status == CommandStatus.Running) break;
                    executed++;
                }

                if (execCtx.PendingExitEvent)
                {
                    proc.CallStack.Pop();
                    continue;
                }

                if (status == CommandStatus.Running)
                {
                    break;
                }

                frame.CurrentCommandStarted = false;
                frame.InstructionPointer = execCtx.PendingJumpIndex ?? (frame.InstructionPointer + 1);

                if (execCtx.PendingSubCall.HasValue)
                {
                    var sub = execCtx.PendingSubCall.Value;
                    if (proc.CallStack.Count < MaxCallDepth)
                    {
                        proc.CallStack.Push(new EventExecutionFrame(sub.sub, frame.OwnerKey, sub.args));
                    }
                    else
                    {
                        AddLog($"[Warning] Max CommonEvent call depth ({MaxCallDepth}) exceeded while calling '{sub.sub.displayName}'.");
                    }
                }
            }

            // Pop any completed frames immediately so StepSingleCommand reflects completion on the last command
            while (!proc.IsFinished)
            {
                var top = proc.CurrentFrame;
                if (top.InstructionPointer < 0 || top.InstructionPointer >= top.EventAsset.commands.Count)
                {
                    proc.CallStack.Pop();
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Removes completed processes from <see cref="ActiveProcesses"/>, raises <see cref="OnEventCompleted"/>,
        /// and refreshes the player control lock state.
        /// </summary>
        private void CleanupFinishedProcesses()
        {
            for (int i = _activeProcesses.Count - 1; i >= 0; i--)
            {
                if (_activeProcesses[i].IsFinished)
                {
                    var finished = _activeProcesses[i];
                    _activeProcesses.RemoveAt(i);
                    OnEventCompleted?.Invoke(finished.RootEvent);
                }
            }
            UpdateControlLockState();
        }

        /// <summary>
        /// Unwinds <paramref name="proc"/>'s call stack and invokes <see cref="EventCommandBase.OnCancel"/> on any started command.
        /// </summary>
        private void CancelProcess(ActiveEventProcess proc)
        {
            if (proc == null) return;
            while (proc.CallStack.Count > 0)
            {
                var frame = proc.CallStack.Pop();
                if (frame.CurrentCommandStarted &&
                    frame.InstructionPointer >= 0 &&
                    frame.InstructionPointer < frame.EventAsset.commands.Count)
                {
                    var cmd = frame.EventAsset.commands[frame.InstructionPointer];
                    cmd?.OnCancel(new FrameContext(this, proc, frame));
                }
            }
        }

        /// <summary>
        /// Cancels and removes all running Parallel processes backed by <paramref name="evt"/>.
        /// </summary>
        private void CancelRunningParallel(CommonEventAsset evt)
        {
            for (int i = _activeProcesses.Count - 1; i >= 0; i--)
            {
                if (_activeProcesses[i].IsParallel && _activeProcesses[i].RootEvent == evt)
                {
                    CancelProcess(_activeProcesses[i]);
                    _activeProcesses.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> if any unfinished Autorun process is currently active.
        /// </summary>
        private bool HasRunningAutorun()
        {
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                if (_activeProcesses[i].IsAutorun && !_activeProcesses[i].IsFinished) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="evt"/> already has an unfinished active process.
        /// </summary>
        private bool IsEventCurrentlyRunning(CommonEventAsset evt)
        {
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                if (_activeProcesses[i].RootEvent == evt && !_activeProcesses[i].IsFinished) return true;
            }
            return false;
        }

        /// <summary>
        /// Recomputes <see cref="IsControlLocked"/> from active Autorun processes and raises <see cref="OnControlLockChanged"/> on transitions.
        /// </summary>
        private void UpdateControlLockState()
        {
            bool shouldLock = false;
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                var p = _activeProcesses[i];
                if (p.IsAutorun && !p.IsFinished && p.RootEvent != null && p.RootEvent.lockPlayerControlOnAutorun)
                {
                    shouldLock = true;
                    break;
                }
            }

            if (shouldLock != _isControlLocked)
            {
                _isControlLocked = shouldLock;
                OnControlLockChanged?.Invoke(_isControlLocked);
            }
        }

        /// <summary>
        /// Appends a message to the 256-entry rolling log buffer and notifies <see cref="OnLogMessage"/> subscribers.
        /// </summary>
        private void AddLog(string msg)
        {
            _logs.Add(msg);
            if (_logs.Count > 256) _logs.RemoveAt(0);
            OnLogMessage?.Invoke(msg);
        }

        /// <summary>
        /// Scans forward from <paramref name="ifIndex"/> to locate the matching <see cref="ElseCommand"/> or <see cref="EndIfCommand"/> at nesting depth 0.
        /// </summary>
        private static int FindMatchingElseOrEndIf(List<EventCommandBase> list, int ifIndex)
        {
            int depth = 0;
            for (int i = ifIndex + 1; i < list.Count; i++)
            {
                if (list[i] is IfCommand) depth++;
                else if (list[i] is ElseCommand && depth == 0) return i;
                else if (list[i] is EndIfCommand)
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return list.Count - 1;
        }

        /// <summary>
        /// Scans forward from <paramref name="startIndex"/> to locate the closing <see cref="EndIfCommand"/> at nesting depth 0.
        /// </summary>
        private static int FindMatchingEndIf(List<EventCommandBase> list, int startIndex)
        {
            int depth = 0;
            for (int i = startIndex + 1; i < list.Count; i++)
            {
                if (list[i] is IfCommand) depth++;
                else if (list[i] is EndIfCommand)
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return list.Count - 1;
        }

        /// <summary>
        /// Scans forward from <paramref name="loopIndex"/> to locate the matching <see cref="EndLoopCommand"/> at nesting depth 0.
        /// </summary>
        private static int FindMatchingEndLoop(List<EventCommandBase> list, int loopIndex)
        {
            int depth = 0;
            for (int i = loopIndex + 1; i < list.Count; i++)
            {
                if (list[i] is LoopCommand) depth++;
                else if (list[i] is EndLoopCommand)
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return list.Count - 1;
        }

        /// <summary>
        /// Scans forward from <paramref name="fromIndex"/> (typically a <see cref="BreakLoopCommand"/>) to find the enclosing <see cref="EndLoopCommand"/>.
        /// </summary>
        private static int FindEnclosingEndLoop(List<EventCommandBase> list, int fromIndex)
        {
            int depth = 0;
            for (int i = fromIndex + 1; i < list.Count; i++)
            {
                if (list[i] is LoopCommand) depth++;
                else if (list[i] is EndLoopCommand)
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return list.Count - 1;
        }

        /// <summary>
        /// Scans backward from <paramref name="fromIndex"/> to find the opening <see cref="LoopCommand"/> of the innermost enclosing loop, or <c>-1</c> if none exists.
        /// </summary>
        private static int FindEnclosingLoopStart(List<EventCommandBase> list, int fromIndex)
        {
            int depth = 0;
            for (int i = fromIndex - 1; i >= 0; i--)
            {
                if (list[i] is EndLoopCommand) depth++;
                else if (list[i] is LoopCommand)
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return -1;
        }
    }
}
