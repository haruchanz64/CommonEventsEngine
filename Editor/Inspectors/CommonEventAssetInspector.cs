using CommonEventsEngine.Data;
using CommonEventsEngine.Editor.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace CommonEventsEngine.Editor.Inspectors
{
    [CustomEditor(typeof(CommonEventAsset))]
    public sealed class CommonEventAssetInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var asset = (CommonEventAsset)target;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8 } };

            var openBtn = new Button(() => CommonEventsWorkbenchWindow.OpenWithAsset(asset))
            {
                text = "Open in Event Workbench"
            };
            openBtn.style.flexGrow = 1;
            openBtn.style.height = 28;
            row.Add(openBtn);

            var presetBtn = new Button(() =>
            {
                Undo.RecordObject(asset, "Apply Sample Merchant Preset");
                asset.ApplySampleMerchantPreset();
                EditorUtility.SetDirty(asset);
            })
            {
                text = "Seed Sample Merchant"
            };
            presetBtn.style.height = 28;
            row.Add(presetBtn);

            root.Add(row);

            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }
    }
}
