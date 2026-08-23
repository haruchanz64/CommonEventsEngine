using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using UnityEngine;

namespace CommonEventsEngine.State
{
    [Serializable] public sealed class SwitchEntry { public string key; public bool value; }
    [Serializable] public sealed class VariableEntry { public string key; public EventValue value; }
    [Serializable] public sealed class SelfSwitchEntry { public string compositeKey; public bool value; }
    [Serializable] public sealed class InventoryItemEntry { public string itemId; public int count; }

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
    /// and a built-in Gold + Inventory ledger with JSON save/load snapshots (SaveEncryptionToolkit compatible).
    /// </summary>
    public sealed class WorldStateStore : IEventInventoryStore
    {
        private readonly Dictionary<string, bool> _switches = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, EventValue> _variables = new Dictionary<string, EventValue>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _selfSwitches = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Gold { get; private set; }

        public IReadOnlyDictionary<string, bool> Switches => _switches;
        public IReadOnlyDictionary<string, EventValue> Variables => _variables;
        public IReadOnlyDictionary<string, bool> SelfSwitches => _selfSwitches;

        public event Action<string, bool> OnSwitchChanged;
        public event Action<string, EventValue> OnVariableChanged;
        public event Action<string, SelfSwitchSlot, bool> OnSelfSwitchChanged;
        public event Action<int> OnGoldChanged;
        public event Action<string, int> OnItemCountChanged;

        public bool GetSwitch(string key) =>
            !string.IsNullOrEmpty(key) && _switches.TryGetValue(key, out bool v) && v;

        public void SetSwitch(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _switches[key] = value;
            OnSwitchChanged?.Invoke(key, value);
        }

        public void ToggleSwitch(string key) => SetSwitch(key, !GetSwitch(key));

        public EventValue GetVariable(string key) =>
            !string.IsNullOrEmpty(key) && _variables.TryGetValue(key, out EventValue v) ? v : EventValue.FromInt(0);

        public void SetVariable(string key, EventValue value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _variables[key] = value;
            OnVariableChanged?.Invoke(key, value);
        }

        public bool GetSelfSwitch(string ownerKey, SelfSwitchSlot slot)
        {
            if (string.IsNullOrWhiteSpace(ownerKey)) return false;
            return _selfSwitches.TryGetValue(MakeSelfSwitchKey(ownerKey, slot), out bool v) && v;
        }

        public void SetSelfSwitch(string ownerKey, SelfSwitchSlot slot, bool value)
        {
            if (string.IsNullOrWhiteSpace(ownerKey)) return;
            _selfSwitches[MakeSelfSwitchKey(ownerKey, slot)] = value;
            OnSelfSwitchChanged?.Invoke(ownerKey, slot, value);
        }

        public void ModifyGold(int delta) => SetGold(Math.Max(0, Gold + delta));

        public void SetGold(int amount)
        {
            Gold = Math.Max(0, amount);
            OnGoldChanged?.Invoke(Gold);
        }

        public int GetItemCount(string itemId) =>
            !string.IsNullOrEmpty(itemId) && _items.TryGetValue(itemId, out int c) ? c : 0;

        public void ModifyItem(string itemId, int delta)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return;
            SetItemCount(itemId, GetItemCount(itemId) + delta);
        }

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

        public IReadOnlyDictionary<string, int> GetAllItems() => _items;

        public void Clear()
        {
            _switches.Clear();
            _variables.Clear();
            _selfSwitches.Clear();
            _items.Clear();
            Gold = 0;
        }

        public string ExportJson(bool prettyPrint = true)
        {
            var snap = new WorldStateSnapshot { gold = Gold };
            foreach (var kvp in _switches) snap.switches.Add(new SwitchEntry { key = kvp.Key, value = kvp.Value });
            foreach (var kvp in _variables) snap.variables.Add(new VariableEntry { key = kvp.Key, value = kvp.Value });
            foreach (var kvp in _selfSwitches) snap.selfSwitches.Add(new SelfSwitchEntry { compositeKey = kvp.Key, value = kvp.Value });
            foreach (var kvp in _items) snap.items.Add(new InventoryItemEntry { itemId = kvp.Key, count = kvp.Value });
            return JsonUtility.ToJson(snap, prettyPrint);
        }

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

        public static string MakeSelfSwitchKey(string ownerKey, SelfSwitchSlot slot) => $"{ownerKey}::{slot}";
    }

    /// <summary>
    /// Key-based registry allowing ScriptableObject CommonEventAssets to locate scene GameObjects
    /// cleanly without storing illegal scene references in assets.
    /// </summary>
    public sealed class SceneBindingRegistry
    {
        private readonly Dictionary<string, GameObject> _bindings = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, GameObject> Bindings => _bindings;

        public void Register(string key, GameObject go)
        {
            if (!string.IsNullOrWhiteSpace(key) && go != null)
            {
                _bindings[key] = go;
            }
        }

        public void Unregister(string key, GameObject go)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (_bindings.TryGetValue(key, out GameObject current) && current == go)
            {
                _bindings.Remove(key);
            }
        }

        public bool TryGet(string key, out GameObject go)
        {
            if (!string.IsNullOrWhiteSpace(key) && _bindings.TryGetValue(key, out go) && go != null)
            {
                return true;
            }
            go = null;
            return false;
        }

        public void Clear() => _bindings.Clear();
    }
}
