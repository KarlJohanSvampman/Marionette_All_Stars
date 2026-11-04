using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    public class InputSelectionTests
    {
        [UnityTest]
        public IEnumerator FallsBackToKeyboardWithoutAHeadset()
        {
            var go = new GameObject("Puppeteer");
            var auto = go.AddComponent<AutoControlInput>();
            auto.hands = go.AddComponent<XRHandsControlInput>();
            auto.keyboard = go.AddComponent<KeyboardControlInput>();
            yield return null;

            var input = new CrossInput { Channels = new float[3] };
            auto.Read(ControlSide.Left, ref input);
            Assert.IsFalse(auto.hands.IsAvailable, "no hand tracking subsystem should run in a headless test");
            Assert.AreSame(auto.keyboard, auto.Active, "without a headset the puppet should be driven by the keyboard");
            Object.Destroy(go);
        }
    }
}
