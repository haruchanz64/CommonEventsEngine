using System;
using CommonEventsEngine.Core;
using UnityEngine;

namespace CommonEventsEngine.Commands
{
    /// <summary>
    /// Mutation applied by <see cref="SetSwitchCommand"/> to a Global Switch.
    /// </summary>
    public enum SwitchOperation { SetOn = 0, SetOff = 1, Toggle = 2 }

    /// <summary>
    /// Arithmetic or assignment operation applied by <see cref="SetVariableCommand"/>.
    /// </summary>
    public enum VariableOperation
    {
        Set = 0,
        Add = 1,
        Subtract = 2,
        Multiply = 3,
        Divide = 4,
        RandomIntRange = 5
    }

    /// <summary>
    /// Sets, clears, or toggles a Global Switch on <see cref="IEventExecutionContext.WorldState"/>.
    /// </summary>
    [Serializable]
    [EventCommand("State/Set Switch", "Sets or toggles a Global Switch on WorldStateStore.")]
    public sealed class SetSwitchCommand : EventCommandBase
    {
        public string switchKey = "Switch_01";
        public SwitchOperation operation = SwitchOperation.SetOn;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Switch[{switchKey}] = {operation}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            switch (operation)
            {
                case SwitchOperation.SetOn: ctx.WorldState.SetSwitch(switchKey, true); break;
                case SwitchOperation.SetOff: ctx.WorldState.SetSwitch(switchKey, false); break;
                case SwitchOperation.Toggle: ctx.WorldState.ToggleSwitch(switchKey); break;
            }
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Writes a per-owner Self-Switch slot (A through D) using <see cref="ownerKeyOverride"/> or the active frame's <see cref="IEventExecutionContext.OwnerKey"/>.
    /// </summary>
    [Serializable]
    [EventCommand("State/Set Self-Switch", "Sets Self-Switch A/B/C/D for the owning scene trigger object.")]
    public sealed class SetSelfSwitchCommand : EventCommandBase
    {
        public SelfSwitchSlot slot = SelfSwitchSlot.A;
        public bool value = true;
        [Tooltip("Optional override; leave empty to use the triggering CommonEventTrigger's ownerKey.")]
        public string ownerKeyOverride = string.Empty;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"SelfSwitch[{slot}] = {(value ? "ON" : "OFF")}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            string owner = string.IsNullOrWhiteSpace(ownerKeyOverride) ? ctx.OwnerKey : ownerKeyOverride;
            ctx.WorldState.SetSelfSwitch(owner, slot, value);
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Assigns, modifies arithmetically, concatenates strings on, or randomizes a Global or Local Variable.
    /// </summary>
    [Serializable]
    [EventCommand("State/Set Variable", "Assigns or modifies a Global or Local Variable.")]
    public sealed class SetVariableCommand : EventCommandBase
    {
        public string variableKey = "Var_01";
        public bool isLocalVariable;
        public VariableOperation operation = VariableOperation.Set;
        public EventValue operand = EventValue.FromInt(1);
        public int randomMinInclusive = 1;
        public int randomMaxInclusive = 10;

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            $"{(isLocalVariable ? "Local" : "Var")}[{variableKey}] {operation} {(operation == VariableOperation.RandomIntRange ? $"({randomMinInclusive}..{randomMaxInclusive})" : operand.ToString())}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            EventValue current = isLocalVariable
                ? (ctx.TryGetLocalVariable(variableKey, out EventValue lv) ? lv : EventValue.FromInt(0))
                : ctx.WorldState.GetVariable(variableKey);

            EventValue next = Compute(current);

            if (isLocalVariable)
            {
                ctx.SetLocalVariable(variableKey, next);
            }
            else
            {
                ctx.WorldState.SetVariable(variableKey, next);
            }

            return CommandStatus.Completed;
        }

        /// <summary>
        /// Computes the next <see cref="EventValue"/> from <paramref name="cur"/> according to <see cref="operation"/>.
        /// Division by zero leaves <paramref name="cur"/> unchanged; string addition concatenates operands.
        /// </summary>
        private EventValue Compute(EventValue cur)
        {
            switch (operation)
            {
                case VariableOperation.Set:
                    return operand;
                case VariableOperation.Add:
                    return operand.type == EventVariableType.String
                        ? EventValue.FromString(cur.AsString() + operand.AsString())
                        : (cur.type == EventVariableType.Float || operand.type == EventVariableType.Float
                            ? EventValue.FromFloat(cur.AsFloat() + operand.AsFloat())
                            : EventValue.FromInt(cur.AsInt() + operand.AsInt()));
                case VariableOperation.Subtract:
                    return cur.type == EventVariableType.Float || operand.type == EventVariableType.Float
                        ? EventValue.FromFloat(cur.AsFloat() - operand.AsFloat())
                        : EventValue.FromInt(cur.AsInt() - operand.AsInt());
                case VariableOperation.Multiply:
                    return cur.type == EventVariableType.Float || operand.type == EventVariableType.Float
                        ? EventValue.FromFloat(cur.AsFloat() * operand.AsFloat())
                        : EventValue.FromInt(cur.AsInt() * operand.AsInt());
                case VariableOperation.Divide:
                    float denom = operand.AsFloat();
                    if (Mathf.Approximately(denom, 0f)) return cur;
                    return cur.type == EventVariableType.Float || operand.type == EventVariableType.Float
                        ? EventValue.FromFloat(cur.AsFloat() / denom)
                        : EventValue.FromInt(cur.AsInt() / Math.Max(1, operand.AsInt()));
                case VariableOperation.RandomIntRange:
                    int min = Math.Min(randomMinInclusive, randomMaxInclusive);
                    int max = Math.Max(randomMinInclusive, randomMaxInclusive);
                    return EventValue.FromInt(UnityEngine.Random.Range(min, max + 1));
                default:
                    return operand;
            }
        }
    }

    /// <summary>
    /// Copies a value between Global and Local variable scopes.
    /// </summary>
    [Serializable]
    [EventCommand("State/Copy Variable", "Copies a Global or Local variable into another variable key.")]
    public sealed class CopyVariableCommand : EventCommandBase
    {
        public string sourceKey = "arg0";
        public bool sourceIsLocal;
        public string destinationKey = "Var_01";
        public bool destinationIsLocal;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Copy {sourceKey} -> {destinationKey}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            EventValue val = sourceIsLocal
                ? (ctx.TryGetLocalVariable(sourceKey, out EventValue lv) ? lv : EventValue.FromInt(0))
                : ctx.WorldState.GetVariable(sourceKey);

            if (destinationIsLocal) ctx.SetLocalVariable(destinationKey, val);
            else ctx.WorldState.SetVariable(destinationKey, val);
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Adds to, subtracts from, or sets the Gold balance on <see cref="IEventExecutionContext.Inventory"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Inventory/Modify Gold", "Adds or subtracts Gold (currency) on the Inventory store.")]
    public sealed class ModifyGoldCommand : EventCommandBase
    {
        public int delta = 50;
        public bool setAbsolute;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => setAbsolute ? $"Set Gold = {delta}" : $"Gold {(delta >= 0 ? "+" : "")}{delta}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.Inventory != null)
            {
                if (setAbsolute) ctx.Inventory.SetGold(delta);
                else ctx.Inventory.ModifyGold(delta);
            }
            return CommandStatus.Completed;
        }
    }

    /// <summary>
    /// Adds to, removes from, or sets an item's quantity on <see cref="IEventExecutionContext.Inventory"/>.
    /// </summary>
    [Serializable]
    [EventCommand("Inventory/Modify Item", "Adds or removes item quantity by Item ID on the Inventory store.")]
    public sealed class ModifyItemCommand : EventCommandBase
    {
        public string itemId = "potion_hp";
        public int delta = 1;
        public bool setAbsolute;

        /// <inheritdoc/>
        public override string GetDisplaySummary() =>
            setAbsolute ? $"Set Item[{itemId}] = {delta}" : $"Item[{itemId}] {(delta >= 0 ? "+" : "")}{delta}";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            if (ctx.Inventory != null)
            {
                if (setAbsolute) ctx.Inventory.SetItemCount(itemId, delta);
                else ctx.Inventory.ModifyItem(itemId, delta);
            }
            return CommandStatus.Completed;
        }
    }
}
