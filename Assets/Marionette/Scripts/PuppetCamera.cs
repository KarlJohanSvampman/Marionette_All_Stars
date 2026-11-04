using UnityEngine;
using UnityEngine.XR;

namespace Marionette
{
    /// <summary>
    /// Desktop third-person camera that follows the puppet at a distance proportional to its
    /// height, so the framing stays the same at mouse and gnome size. Does nothing in VR,
    /// where the player's head is the camera.
    /// </summary>
    public class PuppetCamera : MonoBehaviour
    {
        public PuppetSpawner spawner;
        [Tooltip("Camera offset from the puppet, in puppet heights.")]
        public Vector3 offset = new Vector3(0f, 1.6f, -2.6f);
        public float followSharpness = 4f;

        Camera m_Camera;

        void Awake() => m_Camera = GetComponent<Camera>();

        void LateUpdate()
        {
            if (XRSettings.isDeviceActive || spawner == null || spawner.rig == null) return;
            var rig = spawner.rig;
            Vector3 target = rig.Node(PuppetNodeId.Center).Body.position;
            Vector3 desired = target + offset * rig.height;
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
            if (m_Camera != null) m_Camera.nearClipPlane = Mathf.Max(0.002f, rig.height * 0.05f);
        }
    }
}
