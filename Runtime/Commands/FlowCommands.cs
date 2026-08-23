using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using UnityEngine;

namespace CommonEventsEngine.Commands
{
    [Serializable]
    [EventCommand("Flow/If Condition", "Branches execution based on a Switch, Self-Switch, Variable, Item, or Gold check.")]
    public sealed class IfCommand : EventCommandBase
    {
        public EventCondition condition = new EventCondition();

        public override CommandBlockRole BlockRole => CommandBlockRole.OpenBlock;
        public override string GetDisplaySummary() => $"If ({(condition != null ? condition.GetSummary() : "True")})";

        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    [Serializable]
    [EventCommand("Flow/Else", "Alternative branch for the enclosing If block.")]
    public sealed class ElseCommand : EventCommandBase
    {
        public override CommandBlockRole BlockRole => CommandBlockRole.MidBlock;
        public override string GetDisplaySummary() => "Else";
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    [Serializable]
    [EventCommand("Flow/End If", "Closes the enclosing If/Else block.")]
    public sealed class EndIfCommand : EventCommandBase
    {
        public override CommandBlockRole BlockRole => CommandBlockRole.CloseBlock;
        public override string GetDisplaySummary() => "End If";
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    [Serializable]
    [EventCommand("Flow/Loop", "Repeats enclosed commands until BreakLoop or maxIterations is reached.")]
    public sealed class LoopCommand : EventCommandBase
    {
        [Tooltip("0 = infinite until BreakLoop (guarded by runner safety cap per tick).")]
        [Min(0)] public int maxIterations = 0;

        public override CommandBlockRole BlockRole => CommandBlockRole.OpenBlock;
        public override string GetDisplaySummary() => maxIterations > 0 ? $"Loop (max {maxIterations}x)" : "Loop";
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    [Serializable]
    [EventCommand("Flow/Break Loop", "Exits the innermost enclosing Loop block.")]
    public sealed class BreakLoopCommand : EventCommandBase
    {
        public override string GetDisplaySummary() => "Break Loop";
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    [Serializable]
    [EventCommand("Flow/End Loop", "Jumps back to the matching Loop start.")]
    public sealed class EndLoopCommand : EventCommandBase
    {
        public override CommandBlockRole BlockRole => CommandBlockRole.CloseBlock;
        public override string GetDisplaySummary() => "End Loop";
        public override CommandStatus OnStart(IEventExecutionContext ctx) => CommandStatus.Completed;
    }

    [Serializable]
    [EventCommand("Flow/Wait Seconds", "Pauses this event for the specified duration in seconds.")]
    public sealed class WaitSecondsCommand : EventCommandBase
    {
        [Min(0f)] public float durationSeconds = 1f;
        [NonSerialized] private float _elapsed;

        public override string GetDisplaySummary() => $"Wait {durationSeconds:F2}s";

        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            _elapsed = 0f;
            return durationSeconds <= 0f ? CommandStatus.Completed : CommandStatus.Running;
        }

        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime)
        {
            _elapsed += Math.Max(0f, deltaTime);
            return _elapsed >= durationSeconds ? CommandStatus.Completed : CommandStatus.Running;
        }
    }

    [Serializable]
    [EventCommand("Flow/Wait Until Condition", "Pauses this event until the specified condition evaluates true.")]
    public sealed class WaitUntilCommand : EventCommandBase
    {
        public EventCondition condition = new EventCondition();

        public override string GetDisplaySummary() => $"Wait Until ({(condition != null ? condition.GetSummary() : "True")})";

        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            return condition == null || condition.Evaluate(ctx) ? CommandStatus.Completed : CommandStatus.Running;
        }

        public override CommandStatus OnTick(IEventExecutionContext ctx, float deltaTime)
        {
            return condition == null || condition.Evaluate(ctx) ? CommandStatus.Completed : CommandStatus.Running;
        }
    }

    [Serializable]
    [EventCommand("Flow/Wait For Signal", "Pauses until another script or event emits the named signal.")]
    public sealed class WaitForSignalCommand : EventCommandBase
    {
        public string signalName = "cutscene_continue";
        public string storePayloadInVariable = string.Empty;
        [NonSerialized] private long _startSeq;

        public override string GetDisplaySummary() => $"Wait For Signal '{signalName}'";

        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            _startSeq = ctx.CurrentSignalSequence;
            return CommandStatus.Running;
        }

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

    [Serializable]
    public sealed class CallArgumentEntry
    {
        public string key = "arg0";
        public EventValue value = EventValue.FromInt(0);
    }

    [Serializable]
    [EventCommand("Flow/Call Common Event", "Invokes another CommonEventAsset as a subroutine with optional arguments.")]
    public sealed class CallCommonEventCommand : EventCommandBase
    {
        public CommonEventAsset targetEvent;
        public List<CallArgumentEntry> arguments = new List<CallArgumentEntry>();

        public override string GetDisplaySummary() =>
            $"Call Common Event '{(targetEvent != null ? targetEvent.displayName : "(None)")}'";

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

    [Serializable]
    [EventCommand("Flow/Exit Event", "Terminates the currently executing event immediately.")]
    public sealed class ExitEventCommand : EventCommandBase
    {
        public override string GetDisplaySummary() => "Exit Event";

        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            ctx.RequestExitEvent();
            return CommandStatus.Completed;
        }
    }
}
