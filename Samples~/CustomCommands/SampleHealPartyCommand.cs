using System;
using CommonEventsEngine.Core;
using UnityEngine;

namespace CommonEventsEngine.Samples
{
    /// <summary>
    /// Example custom EventCommandBase that automatically appears under "RPG/Heal Party"
    /// in the CommonEvents Workbench "+ Add Command" menu via TypeCache.
    /// </summary>
    [Serializable]
    [EventCommand("RPG/Heal Party", "Restores party HP variable up to MaxHP and emits a heal signal.")]
    public sealed class SampleHealPartyCommand : EventCommandBase
    {
        public string hpVariableKey = "PartyHP";
        public string maxHpVariableKey = "PartyMaxHP";
        public int healAmount = 50;

        /// <inheritdoc/>
        public override string GetDisplaySummary() => $"Heal Party +{healAmount} ({hpVariableKey})";

        /// <inheritdoc/>
        public override CommandStatus OnStart(IEventExecutionContext ctx)
        {
            int cur = ctx.WorldState.GetVariable(hpVariableKey).AsInt();
            int max = Math.Max(1, ctx.WorldState.GetVariable(maxHpVariableKey).AsInt());
            int next = Mathf.Min(max, cur + healAmount);

            ctx.WorldState.SetVariable(hpVariableKey, EventValue.FromInt(next));
            ctx.EmitSignal("party_healed", next.ToString());
            return CommandStatus.Completed;
        }
    }
}
