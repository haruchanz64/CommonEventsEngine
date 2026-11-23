using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using UnityEngine;

namespace CommonEventsEngine.Commands
{
    /// <summary>
    /// Opens a conditional branch evaluated directly by <see cref="Engine.CommonEventRunner"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/If Condition", "Branches execution based on a Switch, Self-Switch, Variable, Item, or Gold check.")]
    public sealed class IfCommand : EventCommandBase
    {
        public EventCondition condition = new EventCondition();

        /// <inheritdoc/>
        public override CommandBlockRole BlockRole => CommandBlockRole.OpenBlock;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"If ({(condition != null ? condition.GetSummary() : "True")})";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    /// <summary>
    /// Marks the fallback branch inside an enclosing <see cref="IfCommand"/> block.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Else", "Alternative branch for the enclosing If block.")]
    public sealed class ElseCommand : EventCommandBase
    {
        /// <inheritdoc/>
        public override CommandBlockRole BlockRole => CommandBlockRole.MidBlock;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => "Else";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    /// <summary>
    /// Closes the nearest enclosing <see cref="IfCommand"/> or <see cref="ElseCommand"/> block.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/End If", "Closes the enclosing If/Else block.")]
    public sealed class EndIfCommand : EventCommandBase
    {
        /// <inheritdoc/>
        public override CommandBlockRole BlockRole => CommandBlockRole.CloseBlock;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => "End If";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    /// <summary>
    /// Opens a loop block that repeats until <see cref="BreakLoopCommand"/> executes or <see cref="maxIterations"/> is reached.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Loop", "Repeats enclosed commands until BreakLoop or maxIterations is reached.")]
    public sealed class LoopCommand : EventCommandBase
    {
        [Tooltip("0 = infinite until BreakLoop (guarded by runner safety cap per tick).")]
        [Min(0)] public int maxIterations = 0;

        /// <inheritdoc/>
        public override CommandBlockRole BlockRole => CommandBlockRole.OpenBlock;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => maxIterations > 0 ? $"Loop (max {maxIterations}x)" : "Loop";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    /// <summary>
    /// Exits the innermost enclosing <see cref="LoopCommand"/> block and clears its iteration counter.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Break Loop", "Exits the innermost enclosing Loop block.")]
    public sealed class BreakLoopCommand : EventCommandBase
    {
        /// <inheritdoc/>
        public override string GetDisplaySummary() => "Break Loop";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    /// <summary>
    /// Closes a <see cref="LoopCommand"/> block and jumps back to its start; infinite loops (<c>maxIterations &lt;= 0</c>) yield one tick per cycle.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/End Loop", "Jumps back to the matching Loop start.")]
    public sealed class EndLoopCommand : EventCommandBase
    {
        /// <inheritdoc/>
        public override CommandBlockRole BlockRole => CommandBlockRole.CloseBlock;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => "End Loop";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    /// <summary>
    /// Pauses the current event process until <see cref="durationSeconds"/> of runner tick time has elapsed.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Wait Seconds", "Pauses this event for the specified duration in seconds.")]
    public sealed class WaitSecondsCommand : EventCommandBase
    {
        [Min(0f)] public float durationSeconds = 1f;
        [NonSerialized] private float _elapsed;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Wait {durationSeconds:F2}s";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            _elapsed = 0f;
            return durationSeconds <= 0f ? CommandStatus.Completed : CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime)
        {
            _elapsed += Math.Max(0f, deltaTime);
            return _elapsed >= durationSeconds ? CommandStatus.Completed : CommandStatus.Running;
        }
    }

    /// <summary>
    /// Pauses the current event process until <see cref="condition"/> evaluates to <c>true</c>.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Wait Until Condition", "Pauses this event until the specified condition evaluates true.")]
    public sealed class WaitUntilCommand : EventCommandBase
    {
        public EventCondition condition = new EventCondition();

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Wait Until ({(condition != null ? condition.GetSummary() : "True")})";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            return condition == null || condition.Evaluate(ctx) ? CommandStatus.Completed : CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime)
        {
            return condition == null || condition.Evaluate(ctx) ? CommandStatus.Completed : CommandStatus.Running;
        }
    }

    /// <summary>
    /// Pauses the current event process until a matching <see cref="signalName"/> is emitted after this command starts,
    /// optionally storing the signal payload into a Global Variable.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Wait For Signal", "Pauses until another script or event emits the named signal.")]
    public sealed class WaitForSignalCommand : EventCommandBase
    {
        public string signalName = "cutscene_continue";
        public string storePayloadInVariable = string.Empty;
        [NonSerialized] private long _startSeq;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Wait For Signal '{signalName}'";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            _startSeq = ctx.CurrentSignalSequence;
            return CommandStatus.Running;
        }

        /// <inheritdoc/>
        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime)
        {
            if (ctx.ConsumeSignalSince(signalName, _startSeq, out string payload))
            {
                if (!string.IsNullOrWhiteSpace(storePayloadInVariable))
                {
                    ctx.WorldState.SetVariable(storePayloadInVariable, EventValue.FromString(payload));
                }
                return CommandStatus.Completed;
            }
            return CommandStatus.Running;
        }
    }

    /// <summary>
    /// Serializable key/value argument passed as a local variable when invoking a Common Event.
    /// </summary>
    [Serializable]
    public sealed class CallArgumentEntry
    {
        public string key = "arg0";
        public EventValue value = EventValue.FromInt(0);
    }

    /// <summary>
    /// Pushes <see cref="targetEvent"/> onto the active process call stack as a subroutine with optional local <see cref="arguments"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Call Common Event", "Invokes another CommonEventAsset as a subroutine with optional arguments.")]
    public sealed class CallCommonEventCommand : EventCommandBase
    {
        public CommonEventAsset targetEvent;
        public List<CallArgumentEntry> arguments = new List<CallArgumentEntry>();

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            $"Call Common Event '{(targetEvent != null ? targetEvent.displayName : "(None)")}'";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (targetEvent != null)
            {
                Dictionary<string, EventValue> args = null;
                if (arguments != null && arguments.Count > 0)
                {
                    args = new Dictionary<string, EventValue>(StringComparer.Ordinal);
                    for (int i = 0; i < arguments.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(arguments[i].key))
                        {
                            args[arguments[i].key] = arguments[i].value;
                        }
                    }
                }
                ctx.RequestSubEventCall(targetEvent, args);
            }
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Immediately pops the current execution frame from the process call stack.
    /// </summary>
    [Serializable]
    [EventCommand("Flow/Exit Event", "Terminates the currently executing event immediately.")]
    public sealed class ExitEventCommand : EventCommandBase
    {
        /// <inheritdoc/>
        public override string GetDisplaySummary() => "Exit Event";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            ctx.RequestExitEvent();
            return CommandStatus.Completed;
        }
    }
}
