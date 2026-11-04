using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace Marionette.Editor
{
    /// <summary>
    /// Configures OpenXR so the game is driven by finger tracking, not controllers.
    /// For Quest (Android) and PC VR / Quest Link (Standalone):
    ///   on  - Hand Tracking Subsystem (XR Hands joint data), Meta Hand Tracking Aim (system pinch
    ///         gestures), Hand Interaction Profile (hands as an OpenXR input device)
    ///   off - every controller interaction profile
    /// On Android builds, <see cref="HandsOnlyManifest"/> also marks hand tracking as required in the
    /// manifest, so Quest launches the app in hands mode, and asks for high tracking frequency.
    /// </summary>
    public static class HandTrackingSetup
    {
        static readonly string[] k_Enable = { "HandTracking", "MetaHandTrackingAim", "HandInteractionProfile" };
        static readonly string[] k_Disable =
        {
            "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile", "MetaQuestTouchProControllerProfile",
            "ValveIndexControllerProfile", "HTCViveControllerProfile", "MicrosoftMotionControllerProfile",
            "KHRSimpleControllerProfile", "PICO4ControllerProfile", "PICONeo3ControllerProfile"
        };

        [MenuItem("Marionette/Hand Tracking/Use Hands Only (disable controllers)")]
        public static void Apply() => Run(apply: true);

        [MenuItem("Marionette/Hand Tracking/Check Setup")]
        public static void Check() => Run(apply: false);

        static void Run(bool apply)
        {
            var report = new StringBuilder(apply ? "[Marionette] Hands-only setup applied:\n" : "[Marionette] Hand tracking setup:\n");
            bool ok = true;
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null) { report.AppendLine($"  {group}: OpenXR not set up for this platform"); ok = false; continue; }
                foreach (var f in settings.GetFeatures())
                {
                    string type = f.GetType().Name;
                    bool shouldBeOn = System.Array.IndexOf(k_Enable, type) >= 0;
                    bool shouldBeOff = System.Array.IndexOf(k_Disable, type) >= 0;
                    if (!shouldBeOn && !shouldBeOff) continue;
                    if (apply && f.enabled != shouldBeOn)
                    {
                        f.enabled = shouldBeOn;
                        EditorUtility.SetDirty(f);
                    }
                    bool right = f.enabled == shouldBeOn;
                    ok &= right;
                    report.AppendLine($"  {group,-10} {(f.enabled ? "ON " : "off")} {type}{(right ? "" : "   <-- should be " + (shouldBeOn ? "ON" : "off"))}");
                }
            }
            if (apply) AssetDatabase.SaveAssets();
            report.AppendLine(ok ? "  All good: the game reads finger tracking only." : "  Run Marionette > Hand Tracking > Use Hands Only to fix.");
            report.AppendLine("  On the headset: Settings > Movement tracking > Hand tracking must be on. " +
                              "For Quest Link, enable hand tracking in the Meta Horizon Link app's developer settings.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                report.AppendLine("  Note: Android Build Support is not installed, so standalone Quest builds are unavailable. " +
                                  "Add it in Unity Hub (Installs > this version > Add modules). Quest Link works without it.");
            Debug.Log(report.ToString());
        }
    }

#if UNITY_ANDROID
    /// <summary>
    /// Runs after the XR Hands build hook, which declares hand tracking as optional. This marks it
    /// required (Quest starts the app in hands mode and doesn't ask for controllers) and requests
    /// high-frequency tracking, which matters for fine finger control.
    /// </summary>
    class HandsOnlyManifest : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;

        const string k_Android = "http://schemas.android.com/apk/res/android";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = System.IO.Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!System.IO.File.Exists(manifestPath)) return;

            var doc = new System.Xml.XmlDocument();
            doc.Load(manifestPath);
            var ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("android", k_Android);
            var manifest = doc.SelectSingleNode("/manifest");
            var application = doc.SelectSingleNode("/manifest/application");
            if (manifest == null || application == null) return;

            var feature = (System.Xml.XmlElement)doc.SelectSingleNode("/manifest/uses-feature[@android:name='oculus.software.handtracking']", ns);
            if (feature == null)
            {
                feature = doc.CreateElement("uses-feature");
                feature.SetAttribute("name", k_Android, "oculus.software.handtracking");
                manifest.AppendChild(feature);
            }
            feature.SetAttribute("required", k_Android, "true");

            if (doc.SelectSingleNode("/manifest/uses-permission[@android:name='com.oculus.permission.HAND_TRACKING']", ns) == null)
            {
                var permission = doc.CreateElement("uses-permission");
                permission.SetAttribute("name", k_Android, "com.oculus.permission.HAND_TRACKING");
                manifest.AppendChild(permission);
            }

            SetMetaData(doc, application, ns, "com.oculus.handtracking.frequency", "HIGH");
            SetMetaData(doc, application, ns, "com.oculus.handtracking.version", "V2.0");
            doc.Save(manifestPath);
        }

        static void SetMetaData(System.Xml.XmlDocument doc, System.Xml.XmlNode application, System.Xml.XmlNamespaceManager ns, string name, string value)
        {
            var node = (System.Xml.XmlElement)application.SelectSingleNode($"meta-data[@android:name='{name}']", ns);
            if (node == null)
            {
                node = doc.CreateElement("meta-data");
                node.SetAttribute("name", k_Android, name);
                application.AppendChild(node);
            }
            node.SetAttribute("value", k_Android, value);
        }
    }
#endif
}
