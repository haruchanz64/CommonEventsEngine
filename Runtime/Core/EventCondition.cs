using System;
using System.Collections.Generic;
using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Core
{
    public enum ConditionKind
    {
        GlobalSwitch = 0,
        SelfSwitch = 1,
        VariableCompare = 2,
        LocalVariableCompare = 3,
        InventoryItemCount = 4,
        GoldAmount = 5
    }

    public enum CompareOperator
    {
        Equal = 0,
        NotEqual = 1,
        Greater = 2,
        GreaterOrEqual = 3,
        Less = 4,
        LessOrEqual = 5
    }

    public enum ConditionCombineMode
    {
        AllTrueAnd = 0,
        AnyTrueOr = 1
    }

    [Serializable]
    public sealed class EventCondition
    {
        public ConditionKind kind = ConditionKind.GlobalSwitch;
        public string key = "Switch_01";
        public SelfSwitchSlot selfSwitchSlot = SelfSwitchSlot.A;
        public bool expectedBool = true;
        public CompareOperator compareOperator = CompareOperator.GreaterOrEqual;
        public EventValue compareValue = EventValue.FromInt(1);

        public bool Evaluate(IEventExecutionContext ctx)
        {
            if (ctx == null) return false;
            return Evaluate(ctx.WorldState, ctx.Inventory, ctx.OwnerKey, ctx.TryGetLocalVariable);
        }

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

        private bool Evaluate(WorldStateStore state, IEventInventoryStore inventory, string ownerKey, TryLocalLookupDelegate lookup)
        {
            return Evaluate(state, inventory, ownerKey, k =>
            {
                EventValue v = default;
                bool found = lookup != null && lookup(k, out v);
                return (found, v);
            });
        }

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

        public static bool Compare(EventValue left, EventValue right, CompareOperator op)
        {
            if (left.type == EventVariableType.String || right.type == EventVariableType.String)
            {
                int cmp = string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal);
                return ApplyCmp(cmp, op);
            }
            return CompareNumbers(left.AsFloat(), right.AsFloat(), op);
        }

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
