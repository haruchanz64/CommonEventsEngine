using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Data
{
    /// <summary>
    /// Initial Global Switch entry authored in a <see cref="WorldStateDatabase"/> asset.
    /// </summary>
    [Serializable]
    public sealed class SwitchDefinition
    {
        public string key = "Switch_01";
        public bool initialValue;
        public string description = string.Empty;
    }

    /// <summary>
    /// Initial typed Global Variable entry authored in a <see cref="WorldStateDatabase"/> asset.
    /// </summary>
    [Serializable]
    public sealed class VariableDefinition
    {
        public string key = "Var_01";
        public EventValue initialValue = EventValue.FromInt(0);
        public string description = string.Empty;
    }

    /// <summary>
    /// Starting inventory item definition authored in a <see cref="WorldStateDatabase"/> asset.
    /// </summary>
    [Serializable]
    public sealed class ItemDefinition
    {
        public string itemId = "potion_hp";
        public string displayName = "Health Potion";
        [Min(0)] public int startingCount;
    }

    /// <summary>
    /// Project-wide catalog of initial Global Switches, Variables, Starting Gold, and Starting Inventory Items.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldStateDatabase", menuName = "CommonEventsEngine/World State Database", order = 101)]
    public class WorldStateDatabase : ScriptableObject
    {
        [Header("Initial Global Switches")]
        public List<SwitchDefinition> switches = new List<SwitchDefinition>();

        [Header("Initial Global Variables")]
        public List<VariableDefinition> variables = new List<VariableDefinition>();

        [Header("Initial Currency & Inventory")]
        [Min(0)] public int startingGold = 100;
        public List<ItemDefinition> startingItems = new List<ItemDefinition>();

        /// <summary>
        /// Clears <paramref name="store"/> and populates it with this database's starting gold, switches, variables, and items.
        /// </summary>
        /// <param name="store">Target store to reset and seed; null stores are ignored.</param>
        public void ApplyTo(WorldStateStore store)
        {
            if (store == null) return;
            store.Clear();
            store.SetGold(startingGold);

            for (int i = 0; i < switches.Count; i++)
            {
                if (switches[i] != null && !string.IsNullOrWhiteSpace(switches[i].key))
                {
                    store.SetSwitch(switches[i].key, switches[i].initialValue);
                }
            }

            for (int i = 0; i < variables.Count; i++)
            {
                if (variables[i] != null && !string.IsNullOrWhiteSpace(variables[i].key))
                {
                    store.SetVariable(variables[i].key, variables[i].initialValue);
                }
            }

            for (int i = 0; i < startingItems.Count; i++)
            {
                if (startingItems[i] != null && !string.IsNullOrWhiteSpace(startingItems[i].itemId) && startingItems[i].startingCount > 0)
                {
                    store.SetItemCount(startingItems[i].itemId, startingItems[i].startingCount);
                }
            }
        }
    }
}
