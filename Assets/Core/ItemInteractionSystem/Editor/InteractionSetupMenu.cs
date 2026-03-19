#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ItemInteraction.Editor
{
    public class InteractionSetupMenu : EditorWindow
    {
        private GameObject targetObject;
        private string itemDisplayName = "New Item";

        [MenuItem("Tools/Life Is Strange/Interaction Setup Wizard")]
        [MenuItem("GameObject/Life Is Strange Interaction/Open Setup Wizard", false, 10)]
        public static void ShowWindow()
        {
            GetWindow<InteractionSetupMenu>("Interaction Setup");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            GUILayout.Label("System Check", EditorStyles.boldLabel);
            
            var director = FindFirstObjectByType<InteractionDirector>();
            if (director == null)
            {
                EditorGUILayout.HelpBox("Scene is missing an Interaction Director.", MessageType.Warning);
                if (GUILayout.Button("Create Interaction Director", GUILayout.Height(30)))
                {
                    var go = new GameObject("ItemInteractionDirector");
                    Undo.RegisterCreatedObjectUndo(go, "Create Interaction Director");
                    director = go.AddComponent<InteractionDirector>();
                    go.AddComponent<DefaultInteractionInputSource>();
                    Selection.activeGameObject = go;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Interaction Director is present.", MessageType.Info);
            }

            EditorGUILayout.Space(20);
            GUILayout.Label("Item Setup", EditorStyles.boldLabel);

            targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);
            
            // Auto-detect from selection if empty
            if (targetObject == null && Selection.activeGameObject != null)
            {
                targetObject = Selection.activeGameObject;
            }

            if (targetObject != null)
            {
                itemDisplayName = EditorGUILayout.TextField("Item Name", itemDisplayName);

                if (GUILayout.Button("Setup Interaction Item", GUILayout.Height(40)))
                {
                    SetupTargetObject();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Select or assign a GameObject to setup.", MessageType.Info);
            }
        }

        private void SetupTargetObject()
        {
            if (targetObject == null) return;
            
            Undo.RecordObject(targetObject, "Setup Interaction Item");

            var interactable = targetObject.GetComponent<InteractableItem>();
            if (interactable == null)
            {
                interactable = Undo.AddComponent<InteractableItem>(targetObject);
            }
            
            interactable.displayName = string.IsNullOrWhiteSpace(itemDisplayName) ? targetObject.name : itemDisplayName;

            if (targetObject.GetComponent<SelectableOutline>() == null)
            {
                Undo.AddComponent<SelectableOutline>(targetObject);
            }

            if (targetObject.GetComponent<Collider>() == null)
            {
                Undo.AddComponent<BoxCollider>(targetObject);
                Debug.Log("Added BoxCollider as default. Adjust size as needed.");
            }

            EditorUtility.SetDirty(targetObject);
            Debug.Log($"Setup completed for {targetObject.name}");
        }
    }
}
#endif
