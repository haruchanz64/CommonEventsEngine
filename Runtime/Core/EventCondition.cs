using System;
using System.Collections.Generic;
using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Core
{
    /// <summary>
    /// Selects which state source an <see cref="EventCondition"/> inspects during evaluation.
    /// </summary>
    public enum ConditionKind
    {
        GlobalSwitch = 0,
        SelfSwitch = 1,
        VariableCompare = 2,
        LocalVariableCompare = 3,
        InventoryItemCount = 4,
        GoldAmount = 5
    }

    /// <summary>
    /// Relational comparison operator applied to numeric or ordinal string values.
    /// </summary>
    public enum CompareOperator
    {
        Equal = 0,
        NotEqual = 1,
        Greater = 2,
        GreaterOrEqual = 3,
        Less = 4,
        LessOrEqual = 5
    }

    /// <summary>
    /// Logical aggregation mode used by <see cref="EventCondition.EvaluateAll"/>.
    /// </summary>
    public enum ConditionCombineMode
    {
        AllTrueAnd = 0,
        AnyTrueOr = 1
    }

    /// <summary>
    /// Serializable predicate evaluated against global switches, self-switches, variables, inventory counts, or gold.
    /// </summary>
    [Serializable]
    public sealed class EventCondition
    {
        public ConditionKind kind = ConditionKind.GlobalSwitch;
        public string key = "Switch_01";
        public SelfSwitchSlot selfSwitchSlot = SelfSwitchSlot.A;
        public bool expectedBool = true;
        public CompareOperator compareOperator = CompareOperator.GreaterOrEqual;
        public EventValue compareValue = EventValue.FromInt(1);

        /// <summary>
        /// Evaluates this condition using the stores, owner key, and local variables from <paramref name="ctx"/>.
        /// </summary>
        /// <returns><c>true</c> if the predicate matches; <c>false</c> if <paramref name="ctx"/> is null or the check fails.</returns>
        public bool Evaluate(IEventExecutionContext ctx)
        {
            if (ctx == null) return false;
            return Evaluate(ctx.WorldState, ctx.Inventory, ctx.OwnerKey, ctx.TryGetLocalVariable);
        }

        /// <summary>
        /// Evaluates this condition against explicit state and inventory stores.
        /// </summary>
        /// <param name="state">World state store used for switches and global variables; may be null.</param>
        /// <param name="inventory">Inventory store used for item counts and gold; null treats counts and gold as <c>0</c>.</param>
        /// <param name="ownerKey">Optional owner key used when <see cref="kind"/> is <see cref="ConditionKind.SelfSwitch"/>.</param>
        /// <param name="localLookup">Optional resolver used when <see cref="kind"/> is <see cref="ConditionKind.LocalVariableCompare"/>.</param>
        /// <returns><c>true</c> if the condition holds; otherwise <c>false</c>.</returns>
        public bool Evaluate(
            WorldStateStore state,
            IEventInventoryStore inventory,
            string ownerKey = null,
            Func<string, (bool found, EventValue val)> localLookup = null)
        {
            switch (kind)
            {
                case ConditionKind.GlobalSwitch:
                    return state != null && state.GetSwitch(key) == expectedBool;

                case ConditionKind.SelfSwitch:
                    return state != null && state.GetSelfSwitch(ownerKey, selfSwitchSlot) == expectedBool;

                case ConditionKind.VariableCompare:
                    if (state == null) return false;
                    return Compare(state.GetVariable(key), compareValue, compareOperator);

                case ConditionKind.LocalVariableCompare:
                    if (localLookup == null) return false;
                    var res = localLookup(key);
                    return res.found && Compare(res.val, compareValue, compareOperator);

                case ConditionKind.InventoryItemCount:
                    int count = inventory != null ? inventory.GetItemCount(key) : 0;
                    return CompareNumbers(count, compareValue.AsFloat(), compareOperator);

                case ConditionKind.GoldAmount:
                    int gold = inventory != null ? inventory.Gold : 0;
                    return CompareNumbers(gold, compareValue.AsFloat(), compareOperator);

                default:
                    return false;
            }
        }

        private delegate bool TryLocalLookupDelegate(string k, out EventValue v);

        /// <summary>
        /// Adapts an <c>out</c>-parameter local variable lookup delegate to the tuple-returning <see cref="Evaluate(WorldStateStore, IEventInventoryStore, string, Func{string, ValueTuple{bool, EventValue}})"/> overload.
        /// </summary>
        private bool Evaluate(WorldStateStore state, IEventInventoryStore inventory, string ownerKey, TryLocalLookupDelegate lookup)
        {
            return Evaluate(state, inventory, ownerKey, k =>
            {
                EventValue v = default;
                bool found = lookup != null && lookup(k, out v);
                return (found, v);
            });
        }

        /// <summary>
        /// Evaluates a list of conditions combined with <paramref name="mode"/>.
        /// </summary>
        /// <param name="conditions">Conditions to check; null or empty lists evaluate to <c>true</c>.</param>
        /// <param name="mode">Whether all conditions must match (AND) or any single condition suffices (OR).</param>
        /// <param name="state">World state store used for switch and variable checks.</param>
        /// <param name="inventory">Inventory store used for item and gold checks.</param>
        /// <param name="ownerKey">Optional owner key for self-switch checks.</param>
        /// <param name="localLookup">Optional resolver for local variable checks.</param>
        /// <returns><c>true</c> if the combined predicate passes; otherwise <c>false</c>.</returns>
        public static bool EvaluateAll(
            IReadOnlyList<EventCondition> conditions,
            ConditionCombineMode mode,
            WorldStateStore state,
            IEventInventoryStore inventory,
            string ownerKey = null,
            Func<string, (bool found, EventValue val)> localLookup = null)
        {
            if (conditions == null || conditions.Count == 0) return true;

            if (mode == ConditionCombineMode.AnyTrueOr)
            {
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (conditions[i] != null && conditions[i].Evaluate(state, inventory, ownerKey, localLookup))
                    {
                        return true;
                    }
                }
                return false;
            }

            for (int i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] != null && !conditions[i].Evaluate(state, inventory, ownerKey, localLookup))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Compares two <see cref="EventValue"/> instances using ordinal string comparison if either side is a string, or floating-point comparison otherwise.
        /// </summary>
        public static bool Compare(EventValue left, EventValue right, CompareOperator op)
        {
            if (left.type == EventVariableType.String || right.type == EventVariableType.String)
            {
                int cmp = string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal);
                return ApplyCmp(cmp, op);
            }
            return CompareNumbers(left.AsFloat(), right.AsFloat(), op);
        }

        /// <summary>
        /// Compares two floats using <see cref="Mathf.Approximately"/> for equality tolerances.
        /// </summary>
        private static bool CompareNumbers(float a, float b, CompareOperator op)
        {
            switch (op)
            {
                case CompareOperator.Equal: return Mathf.Approximately(a, b);
                case CompareOperator.NotEqual: return !Mathf.Approximately(a, b);
                case CompareOperator.Greater: return a > b;
                case CompareOperator.GreaterOrEqual: return a >= b || Mathf.Approximately(a, b);
                case CompareOperator.Less: return a < b;
                case CompareOperator.LessOrEqual: return a <= b || Mathf.Approximately(a, b);
                default: return false;
            }
        }

        /// <summary>
        /// Maps a three-way comparison result (<c>cmp</c>) to the requested <see cref="CompareOperator"/>.
        /// </summary>
        private static bool ApplyCmp(int cmp, CompareOperator op)
        {
            switch (op)
            {
                case CompareOperator.Equal: return cmp == 0;
                case CompareOperator.NotEqual: return cmp != 0;
                case CompareOperator.Greater: return cmp > 0;
                case CompareOperator.GreaterOrEqual: return cmp >= 0;
                case CompareOperator.Less: return cmp < 0;
                case CompareOperator.LessOrEqual: return cmp <= 0;
                default: return false;
            }
        }

        /// <summary>
        /// Formats a compact human-readable representation of this condition for command summaries.
        /// </summary>
        public string GetSummary()
        {
            switch (kind)
            {
                case ConditionKind.GlobalSwitch: return $"Switch[{key}] == {(expectedBool ? "ON" : "OFF")}";
                case ConditionKind.SelfSwitch: return $"SelfSwitch[{selfSwitchSlot}] == {(expectedBool ? "ON" : "OFF")}";
                case ConditionKind.VariableCompare: return $"Var[{key}] {OpSymbol(compareOperator)} {compareValue}";
                case ConditionKind.LocalVariableCompare: return $"Local[{key}] {OpSymbol(compareOperator)} {compareValue}";
                case ConditionKind.InventoryItemCount: return $"Item[{key}] {OpSymbol(compareOperator)} {compareValue.AsInt()}";
                case ConditionKind.GoldAmount: return $"Gold {OpSymbol(compareOperator)} {compareValue.AsInt()}";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// Returns the symbolic operator string (such as <c>"=="</c> or <c>"&gt;="</c>) for display summaries.
        /// </summary>
        private static string OpSymbol(CompareOperator op)
        {
            switch (op)
            {
                case CompareOperator.Equal: return "==";
                case CompareOperator.NotEqual: return "!=";
                case CompareOperator.Greater: return ">";
                case CompareOperator.GreaterOrEqual: return ">=";
                case CompareOperator.Less: return "<";
                case CompareOperator.LessOrEqual: return "<=";
                default: return "==";
            }
        }
    }
}
