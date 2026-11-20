// ATLAS 3D (Unity) - smoother on phones:
//   · automatic resolution: when the view drops under ~24 FPS it is drawn a
//     little smaller (and back up when there is room). Renders are not
//     affected: they always use full quality.
//   · rest: with nothing moving and no finger on the screen, the scene is
//     drawn a third as often (the touch still answers at once). Less heat
//     means the phone does not slow itself down later.
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas
{
    public partial class AtlasApp
    {
        float autoScale = 1f, perfTimer, lastActive, appliedScale = -1f;
        int appliedMsaa = -1;
        int perfFrames;
        Vector3 lastMouse;

        void AdaptPerformance()
        {
            if (capturing || rendering)
            {
                OnDemandRendering.renderFrameInterval = 1;
                appliedScale = -1f;
                appliedMsaa = -1;
                return;
            }
            // Something happening? (finger, mouse, keys, playback, work)
            bool active = playing || Input.touchCount > 0 || Input.anyKey || Input.mouseScrollDelta.sqrMagnitude > 0f
                          || (Input.mousePosition - lastMouse).sqrMagnitude > 1f || draggingGizmo || sculpting || reducing;
            lastMouse = Input.mousePosition;
            if (active) lastActive = Time.unscaledTime;
            bool resting = Prefs.saveBattery && Time.unscaledTime - lastActive > 2f;
            OnDemandRendering.renderFrameInterval = resting ? 3 : 1;

            // Frame rate while in use → the view's size.
            if (!resting && Prefs.autoRes && Application.isMobilePlatform)
            {
                perfFrames++;
                perfTimer += Time.unscaledDeltaTime;
                if (perfTimer >= 1.5f)
                {
                    float fpsNow = perfFrames / perfTimer;
                    perfFrames = 0;
                    perfTimer = 0f;
                    if (fpsNow < 24f && autoScale > 0.55f) autoScale = Mathf.Max(0.55f, autoScale - 0.1f);
                    else if (fpsNow > 45f && autoScale < 1f) autoScale = Mathf.Min(1f, autoScale + 0.05f);
                }
            }
            else if (!Prefs.autoRes) autoScale = 1f;
            float want = Mathf.Clamp(Prefs.viewRes * autoScale, 0.4f, 1f);
            if (Mathf.Abs(want - appliedScale) > 0.001f)
            {
                var urp = Urp;
                if (urp != null) urp.renderScale = want;
                appliedScale = want;
            }
            // A phone that struggles also smooths edges less (4x MSAA costs
            // as much as a quarter more of the screen); back when it recovers.
            int wantMsaa = Prefs.msaa;
            if (Application.isMobilePlatform && Prefs.autoRes)
            {
                if (autoScale < 0.66f) wantMsaa = 1;
                else if (autoScale < 0.86f) wantMsaa = Mathf.Min(wantMsaa, 2);
            }
            if (wantMsaa != appliedMsaa)
            {
                var urp = Urp;
                if (urp != null) urp.msaaSampleCount = wantMsaa;
                appliedMsaa = wantMsaa;
            }
        }
    }
}
