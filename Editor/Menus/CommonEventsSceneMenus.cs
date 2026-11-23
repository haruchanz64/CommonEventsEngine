using CommonEventsEngine.Components;
using CommonEventsEngine.Presentation;
using UnityEditor;
using UnityEngine;

namespace CommonEventsEngine.Editor.Menus
{
    /// <summary>
    /// One-click GameObject menus that instantiate ready-to-edit CommonEventsEngine scene rigs,
    /// uGUI Dialogue Boxes, Screen Faders, and Event Triggers.
    /// </summary>
    public static class CommonEventsSceneMenus
    {
        /// <summary>
        /// Creates a pre-wired <c>CommonEventsRig</c> GameObject containing <see cref="CommonEventDirector"/>,
        /// <see cref="BuiltInDialogueBox"/>, <see cref="BuiltInScreenFader"/>, and <see cref="BuiltInEventAudio"/>.
        /// </summary>
        [MenuItem("GameObject/CommonEventsEngine/Create Full CommonEvents Rig", false, 10)]
        public static void CreateFullRig(MenuCommand command)
        {
            var rigGo = new GameObject("CommonEventsRig");
            GameObjectUtility.SetParentAndAlign(rigGo, command.context as GameObject);

            rigGo.AddComponent<CommonEventDirector>();

            var dialogue = rigGo.AddComponent<BuiltInDialogueBox>();
            dialogue.EnsureUiBuilt();

            var fader = rigGo.AddComponent<BuiltInScreenFader>();
            fader.EnsureOverlayBuilt();

            rigGo.AddComponent<BuiltInEventAudio>();

            Undo.RegisterCreatedObjectUndo(rigGo, "Create CommonEvents Rig");
            Selection.activeGameObject = rigGo;
        }

        /// <summary>
        /// Creates a standalone <see cref="BuiltInDialogueBox"/> GameObject with its default uGUI Canvas pre-built.
        /// </summary>
        [MenuItem("GameObject/CommonEventsEngine/Create Dialogue Box Canvas", false, 11)]
        public static void CreateDialogueBox(MenuCommand command)
        {
            var go = new GameObject("CommonEventsDialogueBox");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            var box = go.AddComponent<BuiltInDialogueBox>();
            box.EnsureUiBuilt();
            Undo.RegisterCreatedObjectUndo(go, "Create Dialogue Box");
            Selection.activeGameObject = go;
        }

        /// <summary>
        /// Creates a standalone <see cref="BuiltInScreenFader"/> GameObject with its screen-space overlay Canvas pre-built.
        /// </summary>
        [MenuItem("GameObject/CommonEventsEngine/Create Screen Fader Canvas", false, 12)]
        public static void CreateScreenFader(MenuCommand command)
        {
            var go = new GameObject("CommonEventsScreenFader");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            var fader = go.AddComponent<BuiltInScreenFader>();
            fader.EnsureOverlayBuilt();
            Undo.RegisterCreatedObjectUndo(go, "Create Screen Fader");
            Selection.activeGameObject = go;
        }

        /// <summary>
        /// Creates a GameObject with a <see cref="CommonEventTrigger"/> component ready to bind to an asset or collider.
        /// </summary>
        [MenuItem("GameObject/CommonEventsEngine/Create Common Event Trigger", false, 13)]
        public static void CreateEventTrigger(MenuCommand command)
        {
            var go = new GameObject("CommonEventTrigger");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            go.AddComponent<CommonEventTrigger>();
            Undo.RegisterCreatedObjectUndo(go, "Create Common Event Trigger");
            Selection.activeGameObject = go;
        }
    }
}
