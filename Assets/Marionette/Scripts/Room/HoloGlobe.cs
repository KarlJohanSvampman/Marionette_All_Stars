using System;
using System.Collections.Generic;
using UnityEngine;

namespace Marionette.Room
{
    /// <summary>
    /// A hologram Earth globe above the chair. Pinch close to it to take hold and spin it; it keeps
    /// turning a little when let go. Pins on it are the places the puppet can be beamed to: point at a pin
    /// and pinch, or touch it, to choose it.
    /// </summary>
    public class HoloGlobe : MonoBehaviour
    {
        public float radius = 0.16f;
        [Tooltip("How far from the surface a pinch still takes hold, as a fraction of the radius.")]
        public float grabReach = 0.35f;
        [Tooltip("How quickly a spin dies down after letting go, per second.")]
        public float spinDamping = 1.5f;
        public Transform spinner; // the rotating part: sphere and pins
        public TextMesh label;

        public static readonly List<HoloGlobe> Active = new List<HoloGlobe>();

        readonly List<GlobePin> m_Pins = new List<GlobePin>();
        int m_Hand = -1;
        Vector3 m_LastDir, m_Spin; // spin as an angular velocity, degrees per second
        public BeamDestination Selected { get; private set; }
        public event Action<BeamDestination> SelectionChanged;
        public IReadOnlyList<GlobePin> Pins => m_Pins;

        void OnEnable() => Active.Add(this);
        void OnDisable() { Active.Remove(this); m_Hand = -1; }

        void Awake()
        {
            var r = spinner != null ? spinner.GetComponentInChildren<Renderer>() : null;
            if (r != null && r.sharedMaterial != null)
            {
                var mat = new Material(r.sharedMaterial);
                mat.SetTexture("_BaseMap", EarthTexture());
                r.material = mat;
            }
        }

        public void AddPin(BeamDestination dest, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Pin_" + dest.displayName;
            go.transform.SetParent(spinner, false);
            go.transform.localPosition = LatLong(dest.latitude, dest.longitude) * (radius * 1.04f / spinner.lossyScale.x);
            go.transform.localScale = Vector3.one * (0.022f / spinner.lossyScale.x);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<RoomNoInterrupt>();
            var pin = go.AddComponent<GlobePin>();
            pin.globe = this;
            pin.destination = dest;
            m_Pins.Add(pin);
        }

        /// <summary>Unit vector for a latitude/longitude, in the spinner's space (+Y north, longitude 0 toward -Z).</summary>
        public static Vector3 LatLong(float lat, float lon)
        {
            float la = lat * Mathf.Deg2Rad, lo = lon * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(la) * Mathf.Sin(lo), Mathf.Sin(la), -Mathf.Cos(la) * Mathf.Cos(lo));
        }

        public void Select(BeamDestination dest)
        {
            Selected = dest;
            foreach (var p in m_Pins) p.SetSelected(p.destination == dest);
            if (label != null) label.text = dest != null ? dest.displayName : "Pick a place";
            SelectionChanged?.Invoke(dest);
        }

        public bool CanGrab(Vector3 p) => isActiveAndEnabled && Vector3.Distance(p, transform.position) < radius * (1f + grabReach);

        public void Grab(int hand, Vector3 p)
        {
            m_Hand = hand;
            m_LastDir = (p - transform.position).normalized;
            m_Spin = Vector3.zero;
        }

        public void Drag(int hand, Vector3 p)
        {
            if (hand != m_Hand) return;
            Vector3 dir = (p - transform.position).normalized;
            var turn = Quaternion.FromToRotation(m_LastDir, dir);
            spinner.rotation = turn * spinner.rotation;
            turn.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Time.deltaTime > 0f && !float.IsNaN(axis.x)) m_Spin = Vector3.Lerp(m_Spin, axis * (angle / Time.deltaTime), 0.5f);
            m_LastDir = dir;
        }

        public void Release(int hand)
        {
            if (hand == m_Hand) m_Hand = -1;
        }

        void Update()
        {
            if (m_Hand >= 0 || m_Spin.sqrMagnitude < 0.01f) return;
            spinner.rotation = Quaternion.AngleAxis(m_Spin.magnitude * Time.deltaTime, m_Spin.normalized) * spinner.rotation;
            m_Spin *= Mathf.Exp(-spinDamping * Time.deltaTime);
        }

        /// <summary>A made-up Earth: noise continents and a latitude/longitude grid, in hologram blue.</summary>
        static Texture2D EarthTexture()
        {
            const int w = 512, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float lat = (y / (float)(h - 1) - 0.5f) * 180f;
                for (int x = 0; x < w; x++)
                {
                    float lon = (x / (float)w) * 360f - 180f;
                    Vector3 p = LatLong(lat, lon) * 2.2f;
                    float n = Noise3(p) * 0.65f + Noise3(p * 2.3f + Vector3.one * 7f) * 0.35f;
                    bool land = n > 0.52f;
                    bool grid = Mathf.Abs(Mathf.Repeat(lat, 30f)) < 0.7f || Mathf.Abs(Mathf.Repeat(lon, 30f)) < 0.7f;
                    float a = land ? 0.75f : 0.12f;
                    if (grid) a = Mathf.Max(a, 0.35f);
                    px[y * w + x] = new Color(land ? 0.55f : 0.2f, land ? 0.95f : 0.6f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static float Noise3(Vector3 p) =>
            (Mathf.PerlinNoise(p.x + 11f, p.y + 3f) + Mathf.PerlinNoise(p.y + 5f, p.z + 17f) + Mathf.PerlinNoise(p.z + 23f, p.x + 13f)) / 3f;
    }

    /// <summary>A place on the globe. Point and pinch it, or touch it, to choose it as the beam destination.</summary>
    public class GlobePin : MonoBehaviour, IRoomTarget, IPokeable
    {
        public HoloGlobe globe;
        public BeamDestination destination;
        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;
        bool m_Selected;
        float m_Hover;

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_Block = new MaterialPropertyBlock();
        }

        public void SetSelected(bool on) => m_Selected = on;
        public void OnPointerHover(Vector3 point) => m_Hover = 0.1f;
        public void OnPointerPress(Vector3 point) => globe.Select(destination);
        public void OnPointerRelease(Vector3 point) { }
        public void Poke() => globe.Select(destination);

        void Update()
        {
            m_Hover -= Time.deltaTime;
            if (m_Renderer == null) return;
            float pulse = m_Selected ? 0.75f + 0.25f * Mathf.Sin(Time.time * 6f) : 1f;
            Color c = m_Selected ? new Color(1f, 0.85f, 0.3f) * pulse : m_Hover > 0f ? new Color(1f, 1f, 1f) : new Color(1f, 0.45f, 0.3f);
            m_Block.SetColor("_BaseColor", c);
            m_Renderer.SetPropertyBlock(m_Block);
        }
    }
}
