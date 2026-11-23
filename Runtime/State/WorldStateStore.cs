using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using UnityEngine;

namespace CommonEventsEngine.State
{
    /// <summary>
    /// Serializable key/value pair representing a Global Switch in a <see cref="WorldStateSnapshot"/>.
    /// </summary>
    [Serializable] public sealed class SwitchEntry { public string key; public bool value; }

    /// <summary>
    /// Serializable key/value pair representing a typed Global Variable in a <see cref="WorldStateSnapshot"/>.
    /// </summary>
    [Serializable] public sealed class VariableEntry { public string key; public EventValue value; }

    /// <summary>
    /// Serializable composite-key/value pair representing a per-owner Self-Switch in a <see cref="WorldStateSnapshot"/>.
    /// </summary>
    [Serializable] public sealed class SelfSwitchEntry { public string compositeKey; public bool value; }

    /// <summary>
    /// Serializable item ID and quantity pair in a <see cref="WorldStateSnapshot"/>.
    /// </summary>
    [Serializable] public sealed class InventoryItemEntry { public string itemId; public int count; }

    /// <summary>
    /// Flat JSON-serializable snapshot of a <see cref="WorldStateStore"/> compatible with <see cref="JsonUtility"/>.
    /// </summary>
    [Serializable]
    public sealed class WorldStateSnapshot
    {
        public List<SwitchEntry> switches = new List<SwitchEntry>();
        public List<VariableEntry> variables = new List<VariableEntry>();
        public List<SelfSwitchEntry> selfSwitches = new List<SelfSwitchEntry>();
        public int gold;
        public List<InventoryItemEntry> items = new List<InventoryItemEntry>();
    }

    /// <summary>
    /// Central RPG Maker-style blackboard storing Global Switches, typed Variables, Self-Switches (A/B/C/D),
    /// and a built-in Gold and Inventory ledger with JSON save/load snapshots.
    /// </summary>
    public sealed class WorldStateStore : IEventInventoryStore
    {
        private readonly Dictionary<string, bool> _switches = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, EventValue> _variables = new Dictionary<string, EventValue>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _selfSwitches = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public int Gold { get; private set; }

        /// <summary>
        /// Read-only view of all recorded Global Switches.
        /// </summary>
        public IReadOnlyDictionary<string, bool> Switches => _switches;

        /// <summary>
        /// Read-only view of all recorded Global Variables.
        /// </summary>
        public IReadOnlyDictionary<string, EventValue> Variables => _variables;

        /// <summary>
        /// Read-only view of all recorded Self-Switches keyed by <see cref="MakeSelfSwitchKey"/>.
        /// </summary>
        public IReadOnlyDictionary<string, bool> SelfSwitches => _selfSwitches;

        /// <summary>
        /// Raised whenever a Global Switch is written via <see cref="SetSwitch"/> or <see cref="ToggleSwitch"/>.
        /// </summary>
        public event Action<string, bool> OnSwitchChanged;

        /// <summary>
        /// Raised whenever a Global Variable is written via <see cref="SetVariable"/>.
        /// </summary>
        public event Action<string, EventValue> OnVariableChanged;

        /// <summary>
        /// Raised whenever a per-owner Self-Switch is written via <see cref="SetSelfSwitch"/>.
        /// </summary>
        public event Action<string, SelfSwitchSlot, bool> OnSelfSwitchChanged;

        /// <summary>
        /// Raised with the clamped Gold balance whenever <see cref="SetGold"/> or <see cref="ModifyGold"/> is called.
        /// </summary>
        public event Action<int> OnGoldChanged;

        /// <summary>
        /// Raised with the item ID and clamped count whenever an item quantity is updated.
        /// </summary>
        public event Action<string, int> OnItemCountChanged;

        /// <summary>
        /// Returns the boolean state of the named Global Switch, defaulting to <c>false</c> if unset.
        /// </summary>
        public bool GetSwitch(string key) =>
            !string.IsNullOrEmpty(key) && _switches.TryGetValue(key, out bool v) && v;

        /// <summary>
        /// Sets the named Global Switch and raises <see cref="OnSwitchChanged"/>. Ignores null or whitespace keys.
        /// </summary>
        public void SetSwitch(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _switches[key] = value;
            OnSwitchChanged?.Invoke(key, value);
        }

        /// <summary>
        /// Inverts the current boolean state of the named Global Switch.
        /// </summary>
        public void ToggleSwitch(string key) => SetSwitch(key, !GetSwitch(key));

        /// <summary>
        /// Returns the stored <see cref="EventValue"/> for <paramref name="key"/>, defaulting to integer <c>0</c> if unset.
        /// </summary>
        public EventValue GetVariable(string key) =>
            !string.IsNullOrEmpty(key) && _variables.TryGetValue(key, out EventValue v) ? v : EventValue.FromInt(0);

        /// <summary>
        /// Stores <paramref name="value"/> under <paramref name="key"/> and raises <see cref="OnVariableChanged"/>. Ignores null or whitespace keys.
        /// </summary>
        public void SetVariable(string key, EventValue value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _variables[key] = value;
            OnVariableChanged?.Invoke(key, value);
        }

        /// <summary>
        /// Returns the state of a per-owner Self-Switch slot, defaulting to <c>false</c> when unset or when <paramref name="ownerKey"/> is empty.
        /// </summary>
        public bool GetSelfSwitch(string ownerKey, SelfSwitchSlot slot)
        {
            if (string.IsNullOrWhiteSpace(ownerKey)) return false;
            return _selfSwitches.TryGetValue(MakeSelfSwitchKey(ownerKey, slot), out bool v) && v;
        }

        /// <summary>
        /// Sets a per-owner Self-Switch slot and raises <see cref="OnSelfSwitchChanged"/>. Ignores null or whitespace owner keys.
        /// </summary>
        public void SetSelfSwitch(string ownerKey, SelfSwitchSlot slot, bool value)
        {
            if (string.IsNullOrWhiteSpace(ownerKey)) return;
            _selfSwitches[MakeSelfSwitchKey(ownerKey, slot)] = value;
            OnSelfSwitchChanged?.Invoke(ownerKey, slot, value);
        }

        /// <inheritdoc/>
        public void ModifyGold(int delta) => SetGold(Math.Max(0, Gold + delta));

        /// <inheritdoc/>
        public void SetGold(int amount)
        {
            Gold = Math.Max(0, amount);
            OnGoldChanged?.Invoke(Gold);
        }

        /// <inheritdoc/>
        public int GetItemCount(string itemId) =>
            !string.IsNullOrEmpty(itemId) && _items.TryGetValue(itemId, out int c) ? c : 0;

        /// <inheritdoc/>
        public void ModifyItem(string itemId, int delta)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return;
            SetItemCount(itemId, GetItemCount(itemId) + delta);
        }

        /// <inheritdoc/>
        public void SetItemCount(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return;
            int clamped = Math.Max(0, count);
            if (clamped == 0)
            {
                _items.Remove(itemId);
            }
            else
            {
                _items[itemId] = clamped;
            }
            OnItemCountChanged?.Invoke(itemId, clamped);
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, int> GetAllItems() => _items;

        /// <summary>
        /// Resets all switches, variables, self-switches, items, and gold to empty/zero without raising change events.
        /// </summary>
        public void Clear()
        {
            _switches.Clear();
            _variables.Clear();
            _selfSwitches.Clear();
            _items.Clear();
            Gold = 0;
        }

        /// <summary>
        /// Serializes all switches, variables, self-switches, gold, and items to a JSON snapshot string.
        /// </summary>
        /// <param name="prettyPrint">When <c>true</c>, formats the JSON with indentation and line breaks.</param>
        public string ExportJson(bool prettyPrint = true)
        {
            var snap = new WorldStateSnapshot { gold = Gold };
            foreach (var kvp in _switches) snap.switches.Add(new SwitchEntry { key = kvp.Key, value = kvp.Value });
            foreach (var kvp in _variables) snap.variables.Add(new VariableEntry { key = kvp.Key, value = kvp.Value });
            foreach (var kvp in _selfSwitches) snap.selfSwitches.Add(new SelfSwitchEntry { compositeKey = kvp.Key, value = kvp.Value });
            foreach (var kvp in _items) snap.items.Add(new InventoryItemEntry { itemId = kvp.Key, count = kvp.Value });
            return JsonUtility.ToJson(snap, prettyPrint);
        }

        /// <summary>
        /// Clears existing state and restores all entries from a JSON string produced by <see cref="ExportJson"/>.
        /// Does not invoke per-entry change events.
        /// </summary>
        /// <param name="json">JSON snapshot string; null or whitespace leaves the store cleared.</param>
        public void ImportJson(string json)
        {
            Clear();
            if (string.IsNullOrWhiteSpace(json)) return;
            var snap = JsonUtility.FromJson<WorldStateSnapshot>(json);
            if (snap == null) return;

            Gold = Math.Max(0, snap.gold);
            if (snap.switches != null)
            {
                for (int i = 0; i < snap.switches.Count; i++)
                {
                    if (!string.IsNullOrEmpty(snap.switches[i].key))
                        _switches[snap.switches[i].key] = snap.switches[i].value;
                }
            }
            if (snap.variables != null)
            {
                for (int i = 0; i < snap.variables.Count; i++)
                {
                    if (!string.IsNullOrEmpty(snap.variables[i].key))
                        _variables[snap.variables[i].key] = snap.variables[i].value;
                }
            }
            if (snap.selfSwitches != null)
            {
                for (int i = 0; i < snap.selfSwitches.Count; i++)
                {
                    if (!string.IsNullOrEmpty(snap.selfSwitches[i].compositeKey))
                        _selfSwitches[snap.selfSwitches[i].compositeKey] = snap.selfSwitches[i].value;
                }
            }
            if (snap.items != null)
            {
                for (int i = 0; i < snap.items.Count; i++)
                {
                    if (!string.IsNullOrEmpty(snap.items[i].itemId) && snap.items[i].count > 0)
                        _items[snap.items[i].itemId] = snap.items[i].count;
                }
            }
        }

        /// <summary>
        /// Builds the canonical composite dictionary key (<c>"{ownerKey}::{slot}"</c>) for a Self-Switch entry.
        /// </summary>
        public static string MakeSelfSwitchKey(string ownerKey, SelfSwitchSlot slot) => $"{ownerKey}::{slot}";
    }

    /// <summary>
    /// Key-based registry allowing <see cref="Data.CommonEventAsset"/> commands to locate scene <see cref="GameObject"/>
    /// instances without storing scene references in ScriptableObject assets.
    /// </summary>
    public sealed class SceneBindingRegistry
    {
        private readonly Dictionary<string, GameObject> _bindings = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        /// <summary>
        /// Read-only map of currently registered binding keys to their scene objects.
        /// </summary>
        public IReadOnlyDictionary<string, GameObject> Bindings => _bindings;

        /// <summary>
        /// Binds <paramref name="go"/> under <paramref name="key"/>, replacing any previous binding for that key.
        /// </summary>
        public void Register(string key, GameObject go)
        {
            if (!string.IsNullOrWhiteSpace(key) && go != null)
            {
                _bindings[key] = go;
            }
        }

        /// <summary>
        /// Removes the binding for <paramref name="key"/> only if it still points to <paramref name="go"/>.
        /// </summary>
        public void Unregister(string key, GameObject go)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (_bindings.TryGetValue(key, out GameObject current) && current == go)
            {
                _bindings.Remove(key);
            }
        }

        /// <summary>
        /// Looks up a live (non-destroyed) scene object by <paramref name="key"/>.
        /// </summary>
        /// <returns><c>true</c> if a valid <see cref="GameObject"/> is bound to <paramref name="key"/>; otherwise <c>false</c>.</returns>
        public bool TryGet(string key, out GameObject go)
        {
            if (!string.IsNullOrWhiteSpace(key) && _bindings.TryGetValue(key, out go) && go != null)
            {
                return true;
            }
            go = null;
            return false;
        }

        /// <summary>
        /// Removes all scene bindings from the registry.
        /// </summary>
        public void Clear() => _bindings.Clear();
    }
}
