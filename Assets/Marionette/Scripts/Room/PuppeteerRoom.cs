using System;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Marionette.Room
{
    /// <summary>
    /// The puppeteer's room. You are the puppeteer, in first person. Walk about by teleporting (point at
    /// the floor grid and pinch), pinch the chair in the middle of the room to sit down, and from the
    /// chair's armrest panel take control of the puppet, call up the globe and beam the puppet away.
    /// While seated you can't teleport.
    /// </summary>
    public class PuppeteerRoom : MonoBehaviour
    {
        public XROrigin origin;
        public RoomChair chair;
        public TeleportFloor floor;
        public PuppetLink link;
        public RoomPointer pointer;

        public bool Seated { get; private set; }
        public event Action<bool> SeatedChanged;

        Camera Cam => origin.Camera;

        void Awake()
        {
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
        }

        /// <summary>Move so the head is above floorPoint, keeping the way you face.</summary>
        public bool TeleportTo(Vector3 floorPoint)
        {
            if (Seated || (link != null && link.IsConnected)) return false;
            var t = origin.transform;
            Vector3 head = Cam.transform.position;
            t.position += new Vector3(floorPoint.x - head.x, floorPoint.y - t.position.y, floorPoint.z - head.z);
            return true;
        }

        public void Sit()
        {
            if (Seated || chair == null) return;
            origin.transform.localScale = Vector3.one;
            origin.MatchOriginUpCameraForward(Vector3.up, chair.Facing);
            origin.MoveCameraToWorldLocation(chair.eyePoint.position);
            Seated = true;
            SeatedChanged?.Invoke(true);
        }

        public void Stand()
        {
            if (!Seated) return;
            if (link != null && link.IsConnected) link.Disconnect("left the chair");
            Seated = false;
            var t = origin.transform;
            t.position = new Vector3(t.position.x, floor != null ? floor.transform.position.y : 0f, t.position.z);
            TeleportTo(chair.standPoint.position);
            SeatedChanged?.Invoke(false);
        }

        /// <summary>Put the head back where it sits in the chair (after the view was away with the puppet).</summary>
        public void ReturnToSeat()
        {
            if (!Seated || chair == null) return;
            origin.transform.localScale = Vector3.one;
            origin.MatchOriginUpCameraForward(Vector3.up, chair.Facing);
            origin.MoveCameraToWorldLocation(chair.eyePoint.position);
        }
    }

    /// <summary>The floor: a grid of cells to teleport onto. Point and pinch, let go to go there.</summary>
    public class TeleportFloor : MonoBehaviour, IRoomTarget
    {
        public PuppeteerRoom room;
        [Tooltip("Grid cell size, metres.")]
        public float cell = 0.5f;
        [Tooltip("Keep this far from the room's edge, metres.")]
        public float margin = 0.4f;
        Transform m_Highlight;
        float m_Shown;

        void Awake()
        {
            var r = GetComponent<Renderer>();
            if (r != null)
            {
                var mat = new Material(r.sharedMaterial);
                mat.SetTexture("_BaseMap", GridTexture());
                Vector3 size = transform.lossyScale;
                mat.SetTextureScale("_BaseMap", new Vector2(size.x / cell, size.z / cell));
                r.material = mat;
            }
            var hl = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hl.name = "TeleportCell";
            Destroy(hl.GetComponent<Collider>());
            hl.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            hl.transform.localScale = Vector3.one * (cell * 0.92f);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null)
            {
                var m = new Material(shader);
                m.SetColor("_BaseColor", new Color(0.4f, 0.85f, 1f, 1f));
                hl.GetComponent<Renderer>().sharedMaterial = m;
            }
            hl.SetActive(false);
            m_Highlight = hl.transform;
        }

        static Texture2D GridTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool line = x < 2 || y < 2 || x >= n - 1 || y >= n - 1;
                    px[y * n + x] = line ? new Color(0.55f, 0.6f, 0.65f) : new Color(0.3f, 0.32f, 0.35f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public Vector3 Snap(Vector3 p)
        {
            Vector3 size = transform.lossyScale;
            float hx = size.x * 0.5f - margin, hz = size.z * 0.5f - margin;
            Vector3 world = transform.position;
            float x = Mathf.Clamp(Mathf.Floor((p.x - world.x) / cell) * cell + cell * 0.5f, -hx, hx);
            float z = Mathf.Clamp(Mathf.Floor((p.z - world.z) / cell) * cell + cell * 0.5f, -hz, hz);
            return new Vector3(world.x + x, world.y + size.y * 0.5f, world.z + z);
        }

        bool Usable => room != null && !room.Seated && (room.link == null || !room.link.IsConnected);

        public void OnPointerHover(Vector3 point)
        {
            if (!Usable) return;
            m_Highlight.gameObject.SetActive(true);
            m_Highlight.position = Snap(point) + Vector3.up * 0.005f;
            m_Shown = 0.1f;
        }

        public void OnPointerPress(Vector3 point) { }

        public void OnPointerRelease(Vector3 point)
        {
            if (Usable) room.TeleportTo(Snap(point));
        }

        void Update()
        {
            if ((m_Shown -= Time.deltaTime) <= 0f && m_Highlight != null) m_Highlight.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The chair: pinch it for Enter Chair / Cancel, or Exit Chair / Cancel when sitting in it.
    /// Uses a local chair model if there is one (Resources/ChairModel, never committed): the stand-in parts are
    /// hidden but keep their colliders, and the armrest panel moves onto the model's right arm.
    /// </summary>
    public class RoomChair : MonoBehaviour, IRoomTarget
    {
        public PuppeteerRoom room;
        public Transform eyePoint, standPoint;
        public ChoiceMenu menu;
        [Tooltip("The armrest panel, moved onto the model's arm when a model is used.")]
        public Transform panel;
        [Tooltip("The stand-in chair's visible parts, hidden when a model is used.")]
        public Renderer[] standIn;

        [Header("Model (optional, kept local: never committed)")]
        public GameObject model;
        public string modelResource = "ChairModel";
        [Tooltip("Model size multiplier (1 = the model's own metres).")]
        public float modelScale = 1f;
        [Tooltip("Turn the model about the vertical so its seat faces forward, degrees.")]
        public float modelYaw = 0f;
        [Tooltip("Where the armrest panel goes on the model, in the chair's space (front of the right arm, just above it).")]
        public Vector3 modelPanelPosition = new Vector3(0.37f, 0.62f, 0.2f);

        public GameObject ModelInstance { get; private set; }

        void Start() => UseModel();

        void UseModel()
        {
            var source = model != null ? model : (string.IsNullOrEmpty(modelResource) ? null : Resources.Load<GameObject>(modelResource));
            if (source == null) return;
            var instance = Instantiate(source, transform, false);
            instance.name = source.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
            instance.transform.localScale = Vector3.one * modelScale;
            foreach (var c in instance.GetComponentsInChildren<Collider>()) Destroy(c);
            if (standIn != null) foreach (var r in standIn) if (r != null) r.enabled = false;
            if (panel != null && eyePoint != null)
            {
                panel.localPosition = modelPanelPosition;
                panel.rotation = Quaternion.LookRotation(panel.position - eyePoint.position, transform.up); // +Z away from the viewer
            }
            ModelInstance = instance;
        }

        public Vector3 Facing => Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        public void OnPointerHover(Vector3 point) { }
        public void OnPointerPress(Vector3 point) => OpenMenu();
        public void OnPointerRelease(Vector3 point) { }

        public void OpenMenu()
        {
            if (room == null || menu == null) return;
            if (room.Seated) menu.Show("Exit Chair", room.Stand);
            else menu.Show("Enter Chair", room.Sit);
        }
    }

    /// <summary>A small floating menu: one action and Cancel. Appears in front of the player.</summary>
    public class ChoiceMenu : MonoBehaviour
    {
        public RoomButton action, cancel;
        public float distance = 0.55f, closeAfter = 10f;
        Action m_Do;
        float m_Open;

        public bool IsOpen => gameObject.activeSelf;
        public string ActionLabel => action != null ? action.Label : "";

        public void Wire()
        {
            action.onPress = () => { var d = m_Do; Close(); d?.Invoke(); };
            cancel.onPress = Close;
        }

        public void Show(string label, Action onChoose)
        {
            action.Label = label;
            m_Do = onChoose;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                float scale = cam.transform.lossyScale.x;
                transform.position = cam.transform.position + fwd * (distance * scale) + Vector3.down * (0.12f * scale);
                transform.rotation = Quaternion.LookRotation(fwd, Vector3.up); // +Z away from the viewer
            }
            gameObject.SetActive(true);
            m_Open = closeAfter;
        }

        public void Close()
        {
            m_Do = null;
            gameObject.SetActive(false);
        }

        void Update()
        {
            if ((m_Open -= Time.deltaTime) <= 0f) Close();
        }
    }
}
