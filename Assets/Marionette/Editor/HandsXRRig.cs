using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace Marionette.Editor
{
    /// <summary>
    /// Builds a minimal XR rig for finger-tracked puppeteering: XR Origin, a head-tracked camera and
    /// the XR Hands sample hand meshes so the player sees their own hands. No controller objects.
    /// The same camera also works on desktop, where PuppetCamera drives it instead of the headset.
    /// </summary>
    public static class HandsXRRig
    {
        const string k_HandsSample = "Assets/Samples/XR Hands/1.8.1/HandVisualizer/Prefabs/";

        [MenuItem("Marionette/Hand Tracking/Add Hands-Only XR Rig To Scene")]
        static void AddToScene()
        {
            var origin = Create(Vector3.zero, Quaternion.identity);
            foreach (var hands in Object.FindObjectsByType<XRHandsControlInput>())
                hands.trackingSpace = origin.CameraFloorOffsetObject.transform;
            Selection.activeGameObject = origin.gameObject;
            EditorSceneManager.MarkSceneDirty(origin.gameObject.scene);
        }

        /// <summary>Creates the rig standing on the floor at position, reusing the scene's Main Camera if there is one.</summary>
        public static XROrigin Create(Vector3 position, Quaternion rotation)
        {
            var existing = Object.FindAnyObjectByType<XROrigin>();
            if (existing != null) return existing;

            var root = new GameObject("XR Origin (Hands)");
            Undo.RegisterCreatedObjectUndo(root, "Create Hands XR Rig");
            root.transform.SetPositionAndRotation(position, rotation);

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(root.transform, false);

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.gameObject.AddComponent<AudioListener>();
            }
            cam.transform.SetParent(offset.transform, false);
            cam.transform.localPosition = new Vector3(0f, 1.6f, 0f); // desktop starting point; the headset overrides it
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.01f;

            var pose = cam.gameObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(new InputAction("Head Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            pose.rotationInput = new InputActionProperty(new InputAction("Head Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            pose.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking State", binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));

            var origin = root.AddComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            AddHand(offset.transform, "Left Hand Tracking.prefab");
            AddHand(offset.transform, "Right Hand Tracking.prefab");
            return origin;
        }

        static void AddHand(Transform parent, string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_HandsSample + prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"[Marionette] Hand mesh prefab not found at {k_HandsSample + prefabName}. " +
                                 "Import it from Package Manager > XR Hands > Samples > HandVisualizer to see your hands in VR.");
                return;
            }
            var hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            hand.transform.localPosition = Vector3.zero;
            hand.transform.localRotation = Quaternion.identity;
        }
    }
}
