using CommonEventsEngine.State;
using UnityEngine;

namespace CommonEventsEngine.Components
{
    /// <summary>
    /// Registers this GameObject under a string key in CommonEventDirector.SceneBindings
    /// so CommonEventAssets can toggle, teleport, or spawn at this object without asset-to-scene references.
    /// </summary>
    public sealed class CommonEventSceneBinding : MonoBehaviour
    {
        [SerializeField] private string bindingKey = "Player";
        [SerializeField] private CommonEventDirector explicitDirector;

        /// <summary>
        /// String key under which this <see cref="GameObject"/> is registered while enabled.
        /// </summary>
        public string BindingKey => bindingKey;

        private void OnEnable()
        {
            var registry = ResolveRegistry();
            registry?.Register(bindingKey, gameObject);
        }

        private void OnDisable()
        {
            var registry = ResolveRegistry();
            registry?.Unregister(bindingKey, gameObject);
        }

        /// <summary>
        /// Resolves the target <see cref="SceneBindingRegistry"/> from <c>explicitDirector</c> or <see cref="CommonEventDirector.ActiveInstance"/>.
        /// </summary>
        private SceneBindingRegistry ResolveRegistry()
        {
            var dir = explicitDirector != null ? explicitDirector : CommonEventDirector.ActiveInstance;
            return dir != null ? dir.SceneBindings : null;
        }
    }
}
