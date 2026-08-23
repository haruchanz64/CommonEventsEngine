using System;
using System.Collections.Generic;
using CommonEventsEngine.Commands;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Engine
{
    public sealed class SignalRecord
    {
        public long Sequence { get; set; }
        public string SignalName { get; set; }
        public string Payload { get; set; }
    }

    public sealed class EventExecutionFrame
    {
        public CommonEventAsset EventAsset { get; }
        public int InstructionPointer { get; set; }
        public bool CurrentCommandStarted { get; set; }
        public string OwnerKey { get; }
        public Dictionary<string, EventValue> LocalVariables { get; }
        public Dictionary<int, int> LoopIterationCounts { get; } = new Dictionary<int, int>();

        public EventExecutionFrame(CommonEventAsset asset, string ownerKey, Dictionary<string, EventValue> args)
        {
            EventAsset = asset;
            OwnerKey = ownerKey ?? string.Empty;
            LocalVariables = args != null
                ? new Dictionary<string, EventValue>(args, StringComparer.Ordinal)
                : new Dictionary<string, EventValue>(StringComparer.Ordinal);
        }
    }

    public sealed class ActiveEventProcess
    {
        public string ProcessId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public CommonEventAsset RootEvent { get; }
        public bool IsAutorun { get; }
        public bool IsParallel { get; }
        public Stack<EventExecutionFrame> CallStack { get; } = new Stack<EventExecutionFrame>();
        public bool IsFinished => CallStack.Count == 0;

        public EventExecutionFrame CurrentFrame => CallStack.Count > 0 ? CallStack.Peek() : null;

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

        public WorldStateStore WorldState { get; }
        public IEventInventoryStore Inventory { get; set; }
        public IEventDialogueHandler DialogueHandler { get; set; }
        public IScreenTransitionHandler ScreenHandler { get; set; }
        public IEventAudioHandler AudioHandler { get; set; }
        public SceneBindingRegistry SceneBindings { get; }

        public int MaxCallDepth { get; set; } = 16;
        public int MaxInstantCommandsPerTick { get; set; } = 128;
        public long SignalSequence { get; private set; }
        public bool IsControlLocked => _isControlLocked;
        public bool IsPaused { get; set; }

        public IReadOnlyList<CommonEventAsset> RegisteredEvents => _registeredEvents;
        public IReadOnlyList<ActiveEventProcess> ActiveProcesses => _activeProcesses;
        public IReadOnlyList<SignalRecord> SignalHistory => _signalHistory;
        public IReadOnlyList<string> Logs => _logs;

        public event Action<bool> OnControlLockChanged;
        public event Action<string, string> OnSignalEmitted;
        public event Action<CommonEventAsset> OnEventStarted;
        public event Action<CommonEventAsset> OnEventCompleted;
        public event Action<string> OnLogMessage;

        public CommonEventRunner(WorldStateStore worldState = null, SceneBindingRegistry sceneBindings = null)
        {
            WorldState = worldState ?? new WorldStateStore();
            Inventory = WorldState;
            SceneBindings = sceneBindings ?? new SceneBindingRegistry();
        }

        public void RegisterEvent(CommonEventAsset asset)
        {
            if (asset != null && !_registeredEvents.Contains(asset))
            {
                _registeredEvents.Add(asset);
            }
        }

        public void SetRegisteredEvents(IEnumerable<CommonEventAsset> assets)
        {
            _registeredEvents.Clear();
            if (assets == null) return;
            foreach (var a in assets) RegisterEvent(a);
        }

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

        public void CancelAllProcesses()
        {
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                CancelProcess(_activeProcesses[i]);
            }
            _activeProcesses.Clear();
            UpdateControlLockState();
        }

        public void ClearLogs() => _logs.Clear();

        /// <summary>
        /// Advances all scheduled Autorun, Manual, and Parallel events by deltaTime.
        /// </summary>
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
        /// Executes at most one command step on the target process (used by the UI Toolkit Live Step Debugger).
        /// </summary>
        public bool StepSingleCommand(ActiveEventProcess process, float deltaTime = 0.016f)
        {
            if (process == null || process.IsFinished) return false;
            AdvanceProcess(process, deltaTime, maxCommands: 1);
            CleanupFinishedProcesses();
            return !process.IsFinished;
        }

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

        private bool HasRunningAutorun()
        {
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                if (_activeProcesses[i].IsAutorun && !_activeProcesses[i].IsFinished) return true;
            }
            return false;
        }

        private bool IsEventCurrentlyRunning(CommonEventAsset evt)
        {
            for (int i = 0; i < _activeProcesses.Count; i++)
            {
                if (_activeProcesses[i].RootEvent == evt && !_activeProcesses[i].IsFinished) return true;
            }
            return false;
        }

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

        private void AddLog(string msg)
        {
            _logs.Add(msg);
            if (_logs.Count > 256) _logs.RemoveAt(0);
            OnLogMessage?.Invoke(msg);
        }

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
