using UnityEngine;

namespace Marionette.Room
{
    /// <summary>Something in the room you can point at and pinch.</summary>
    public interface IRoomTarget
    {
        /// <summary>Pointed at this frame (the ray hits it at point).</summary>
        void OnPointerHover(Vector3 point);
        /// <summary>Pinched while pointing at it.</summary>
        void OnPointerPress(Vector3 point);
        /// <summary>Pinch released while still pointing at it.</summary>
        void OnPointerRelease(Vector3 point);
    }

    /// <summary>Something you can press by touching it with a fingertip.</summary>
    public interface IPokeable
    {
        void Poke();
    }

    /// <summary>
    /// Room interaction with bare hands:
    ///   - Pinch-ray: point with either hand and pinch to press what the ray hits (the chair, menu buttons,
    ///     globe pins); for the floor, the teleport happens when you let go.
    ///   - Poke: an index fingertip pressing into a button presses it.
    ///   - Grab: pinching close to the hologram globe takes hold of it, to spin it.
    /// Switched off while the puppeteer is connected to the puppet (PuppetLink).
    /// </summary>
    [RequireComponent(typeof(RoomHands))]
    public class RoomPointer : MonoBehaviour
    {
        [Tooltip("Longest pointing ray, metres (before the player's scale).")]
        public float maxDistance = 10f;
        [Tooltip("Fingertip radius for poking buttons, metres.")]
        public float pokeRadius = 0.012f;
        public Color rayColor = new Color(0.6f, 0.9f, 1f, 0.8f);

        RoomHands m_Hands;
        readonly IRoomTarget[] m_Pressed = new IRoomTarget[2];
        readonly IPokeable[] m_Poking = new IPokeable[2];
        readonly HoloGlobe[] m_Grabbing = new HoloGlobe[2];
        readonly LineRenderer[] m_Rays = new LineRenderer[2];
        readonly Transform[] m_Dots = new Transform[2];

        void Awake()
        {
            m_Hands = GetComponent<RoomHands>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "LeftPointerRay" : "RightPointerRay");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = 0.004f;
                if (shader != null) line.sharedMaterial = new Material(shader);
                line.startColor = rayColor;
                line.endColor = new Color(rayColor.r, rayColor.g, rayColor.b, 0.1f);
                line.enabled = false;
                m_Rays[i] = line;
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "PointerDot";
                Destroy(dot.GetComponent<Collider>());
                dot.transform.SetParent(go.transform, false);
                dot.transform.localScale = Vector3.one * 0.015f;
                if (shader != null) dot.GetComponent<Renderer>().sharedMaterial = line.sharedMaterial;
                dot.SetActive(false);
                m_Dots[i] = dot.transform;
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < 2; i++)
            {
                if (m_Rays[i] != null) m_Rays[i].enabled = false;
                if (m_Dots[i] != null) m_Dots[i].gameObject.SetActive(false);
                if (m_Grabbing[i] != null) { m_Grabbing[i].Release(i); m_Grabbing[i] = null; }
                m_Pressed[i] = null;
                m_Poking[i] = null;
            }
        }

        void Update()
        {
            for (int i = 0; i < 2; i++) UpdateHand(i, m_Hands[i]);
        }

        void UpdateHand(int i, RoomHands.Hand hand)
        {
            var ray = m_Rays[i];
            var dot = m_Dots[i];
            ray.enabled = false;
            dot.gameObject.SetActive(false);
            if (!hand.tracked)
            {
                if (m_Grabbing[i] != null) { m_Grabbing[i].Release(i); m_Grabbing[i] = null; }
                m_Pressed[i] = null;
                return;
            }
            float scale = transform.lossyScale.x;

            // Grab the globe: pinch near it, spin while pinched.
            if (m_Grabbing[i] != null)
            {
                if (hand.pinching) { m_Grabbing[i].Drag(i, hand.pinchPoint); return; }
                m_Grabbing[i].Release(i);
                m_Grabbing[i] = null;
            }
            if (hand.pinchStarted && !m_Hands.UsingMouse)
                foreach (var globe in HoloGlobe.Active)
                    if (globe.CanGrab(hand.pinchPoint)) { m_Grabbing[i] = globe; globe.Grab(i, hand.pinchPoint); return; }

            // Poke buttons with the index fingertip.
            if (!m_Hands.UsingMouse)
            {
                IPokeable poked = null;
                foreach (var c in Physics.OverlapSphere(hand.indexTip, pokeRadius * scale, ~0, QueryTriggerInteraction.Collide))
                    if ((poked = c.GetComponentInParent<IPokeable>()) != null) break;
                if (poked != null && poked != m_Poking[i]) poked.Poke();
                m_Poking[i] = poked;
            }

            // Point and pinch.
            IRoomTarget target = null;
            Vector3 point = hand.aim.origin + hand.aim.direction * (maxDistance * scale);
            if (Physics.Raycast(hand.aim, out RaycastHit hit, maxDistance * scale, ~0, QueryTriggerInteraction.Collide))
            {
                target = hit.collider.GetComponentInParent<IRoomTarget>();
                point = hit.point;
            }
            if (target != null)
            {
                target.OnPointerHover(point);
                ray.enabled = true;
                ray.widthMultiplier = 0.004f * scale;
                ray.SetPosition(0, hand.aim.origin);
                ray.SetPosition(1, point);
                dot.gameObject.SetActive(true);
                dot.position = point;
                dot.localScale = Vector3.one * (0.015f * scale / Mathf.Max(1e-4f, dot.parent.lossyScale.x));
            }
            if (hand.pinchStarted && target != null)
            {
                m_Pressed[i] = target;
                target.OnPointerPress(point);
            }
            if (hand.pinchEnded)
            {
                if (target != null && target == m_Pressed[i]) target.OnPointerRelease(point);
                m_Pressed[i] = null;
            }
        }
    }

    /// <summary>A 3D button: point and pinch it, or poke it with a fingertip.</summary>
    public class RoomButton : MonoBehaviour, IRoomTarget, IPokeable
    {
        public System.Action onPress;
        public Color idle = new Color(0.18f, 0.22f, 0.3f), hover = new Color(0.25f, 0.4f, 0.6f), pressed = new Color(0.4f, 0.8f, 1f);
        TextMesh m_Label;
        Renderer m_Face;
        MaterialPropertyBlock m_Block;
        float m_Hover, m_Flash;

        public string Label
        {
            get => m_Label != null ? m_Label.text : "";
            set { if (m_Label != null) m_Label.text = value; }
        }

        /// <summary>
        /// A flat button on a panel. Panels are built with +Z pointing away from the viewer, so the button's
        /// face and its label (TextMesh reads from -Z) face whoever is in front. parent should be unscaled.
        /// </summary>
        public static RoomButton Create(Transform parent, string name, string label, Vector3 localPos, Vector2 size, Material face, System.Action onPress)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 0.012f);
            if (face != null) go.GetComponent<Renderer>().sharedMaterial = face;
            var b = go.AddComponent<RoomButton>();
            b.onPress = onPress;
            b.m_Label = RoomText.Create(go.transform.parent, name + "Label", label, localPos + new Vector3(0f, 0f, -0.008f), size.y * 0.4f);
            return b;
        }

        void Awake()
        {
            m_Face = GetComponent<Renderer>();
            m_Block = new MaterialPropertyBlock();
        }

        public void Press()
        {
            m_Flash = 0.25f;
            onPress?.Invoke();
        }

        public void Poke() => Press();
        public void OnPointerHover(Vector3 point) => m_Hover = 0.1f;
        public void OnPointerPress(Vector3 point) => Press();
        public void OnPointerRelease(Vector3 point) { }

        void Update()
        {
            m_Hover -= Time.deltaTime;
            m_Flash -= Time.deltaTime;
            if (m_Face == null) return;
            m_Block.SetColor("_BaseColor", m_Flash > 0f ? pressed : m_Hover > 0f ? hover : idle);
            m_Face.SetPropertyBlock(m_Block);
        }

        void OnEnable() { if (m_Label != null) m_Label.gameObject.SetActive(true); }
        void OnDisable() { if (m_Label != null) m_Label.gameObject.SetActive(false); }
    }

    /// <summary>World-space text, using Unity's built-in font (renders in URP without extra packages).</summary>
    public static class RoomText
    {
        public static TextMesh Create(Transform parent, string name, string text, Vector3 localPos, float height, Color? color = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = height / 6.4f; // fontSize 64 -> about one line of the given height
            tm.color = color ?? Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            // TextMesh reads correctly from its -Z side: panels point +Z away from the viewer.
            go.transform.localScale = Vector3.one;
            return tm;
        }
    }
}
