using System.Collections.Generic;
using CommonEventsEngine.Commands;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using UnityEngine;

namespace CommonEventsEngine.Components
{
    /// <summary>
    /// Drop-in scene or Canvas trigger component with RPG Maker-style Self-Switches (A/B/C/D).
    /// Supports OnStart, 2D/3D trigger colliders, and zero-argument Trigger() binding for Canvas Button.onClick().
    /// </summary>
    public sealed class CommonEventTrigger : MonoBehaviour
    {
        [Header("Target Event & Owner Key")]
        [SerializeField] private CommonEventDirector explicitDirector;
        [SerializeField] private CommonEventAsset eventAsset;
        [Tooltip("Unique owner ID used for Self-Switches A/B/C/D. Defaults to scene/object name if empty.")]
        [SerializeField] private string ownerKey = string.Empty;
        [SerializeField] private List<CallArgumentEntry> callArguments = new List<CallArgumentEntry>();

        [Header("Activation Conditions")]
        [SerializeField] private ConditionCombineMode conditionMode = ConditionCombineMode.AllTrueAnd;
        [SerializeField] private List<EventCondition> requiredConditions = new List<EventCondition>();

        [Header("Auto Trigger Options")]
        [SerializeField] private bool triggerOnStart = false;
        [SerializeField] private bool triggerOnColliderEnter = false;
        [SerializeField] private string requiredColliderTag = "Player";
        [SerializeField] private bool triggerOncePerLoad = false;

        private bool _triggered;

        /// <summary>
        /// Resolved owner key used for Self-Switch lookups: returns <c>ownerKey</c> when set,
        /// or <c>"{sceneName}:{gameObjectName}"</c> when left blank.
        /// </summary>
        public string EffectiveOwnerKey => string.IsNullOrWhiteSpace(ownerKey) ? $"{gameObject.scene.name}:{gameObject.name}" : ownerKey;

        private void Start()
        {
            if (triggerOnStart) Trigger();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!triggerOnColliderEnter) return;
            if (!string.IsNullOrEmpty(requiredColliderTag) && !other.CompareTag(requiredColliderTag)) return;
            Trigger();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!triggerOnColliderEnter) return;
            if (!string.IsNullOrEmpty(requiredColliderTag) && !other.CompareTag(requiredColliderTag)) return;
            Trigger();
        }

        /// <summary>
        /// Zero-argument entry point bindable directly to a uGUI Canvas <c>Button.onClick</c> or interaction script.
        /// Evaluates <c>requiredConditions</c> against <see cref="EffectiveOwnerKey"/> and starts <c>eventAsset</c> if all checks pass.
        /// </summary>
        public void Trigger()
        {
            if (triggerOncePerLoad && _triggered) return;
            if (eventAsset == null) return;

            var dir = explicitDirector != null ? explicitDirector : CommonEventDirector.ActiveInstance;
            if (dir == null) return;

            string owner = EffectiveOwnerKey;
            if (!EventCondition.EvaluateAll(requiredConditions, conditionMode, dir.WorldState, dir.Runner.Inventory, owner))
            {
                return;
            }

            Dictionary<string, EventValue> args = null;
            if (callArguments != null && callArguments.Count > 0)
            {
                args = new Dictionary<string, EventValue>(System.StringComparer.Ordinal);
                for (int i = 0; i < callArguments.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(callArguments[i].key))
                    {
                        args[callArguments[i].key] = callArguments[i].value;
                    }
                }
            }

            _triggered = true;
            dir.Runner.StartEvent(eventAsset, ownerKey: owner, args: args);
        }
    }
}
