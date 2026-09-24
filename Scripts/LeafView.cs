using DG.Tweening;

using UnityEngine;
using UnityEngine.UIElements;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// The single leaf that travels through every screen: idle float on Attract, live preview on
    /// Input, glowing on Release, lifting off the top on Sending. All motion is DOTween.
    /// </summary>
    public class LeafView {
        private const float BaseRotation = -20f;

        private readonly VisualElement stage;
        private readonly VisualElement body;
        private readonly VisualElement glow;
        private readonly Label nameLabel;
        private readonly Label promiseLabel;
        private readonly object tweenId = new object();
        private readonly object idleId = new object();

        private float top;
        private float scale = 1f;
        private float alpha = 1f;
        private float glowAlpha = 0.5f;
        private float glowPulse = 1f;
        private float bob;
        private float sway;
        private float drag;

        public LeafView(VisualElement root) {
            stage = root.Q<VisualElement>("leaf-stage");
            body = root.Q<VisualElement>("leaf-body");
            glow = root.Q<VisualElement>("leaf-glow");
            nameLabel = root.Q<Label>("leaf-name");
            promiseLabel = root.Q<Label>("leaf-promise");
            top = 640f;
            Apply();
        }

        public void SetText(string visitorName, string promise) {
            nameLabel.text = visitorName;
            promiseLabel.text = promise;
        }

        /// <summary> Finger drag on the Release screen, in panel pixels (negative = up). </summary>
        public void SetDrag(float offset) {
            drag = offset;
            Apply();
        }

        public Tween SpringBack() {
            return DOTween.To(() => drag, v => { drag = v; Apply(); }, 0f, 0.35f).SetEase(Ease.OutBack).SetId(tweenId);
        }

        public Sequence MoveTo(float targetTop, float targetScale, float targetAlpha, float targetGlow, float seconds, Ease ease) {
            DOTween.Kill(tweenId);
            Sequence sequence = DOTween.Sequence().SetId(tweenId);
            sequence.Join(DOTween.To(() => top, v => { top = v; Apply(); }, targetTop, seconds).SetEase(ease));
            sequence.Join(DOTween.To(() => scale, v => { scale = v; Apply(); }, targetScale, seconds).SetEase(ease));
            sequence.Join(DOTween.To(() => alpha, v => { alpha = v; Apply(); }, targetAlpha, seconds).SetEase(ease));
            sequence.Join(DOTween.To(() => glowAlpha, v => { glowAlpha = v; Apply(); }, targetGlow, seconds).SetEase(ease));
            return sequence;
        }

        /// <summary> Lift off the top of the screen, toward the wall. </summary>
        public Sequence LiftOff(float seconds) {
            StopIdle();
            DOTween.Kill(tweenId);
            drag = 0f;
            Sequence sequence = DOTween.Sequence().SetId(tweenId);
            sequence.Append(DOTween.To(() => top, v => { top = v; Apply(); }, top + 40f, 0.25f).SetEase(Ease.OutQuad));
            sequence.Append(DOTween.To(() => top, v => { top = v; Apply(); }, -1100f, seconds).SetEase(Ease.InCubic));
            sequence.Join(DOTween.To(() => scale, v => { scale = v; Apply(); }, 0.3f, seconds).SetEase(Ease.InQuad));
            sequence.Join(DOTween.To(() => sway, v => { sway = v; Apply(); }, 14f, seconds).SetEase(Ease.InOutSine));
            sequence.Insert(0.25f + (seconds * 0.6f), DOTween.To(() => alpha, v => { alpha = v; Apply(); }, 0f, seconds * 0.4f));
            return sequence;
        }

        /// <summary> Gentle float + glow breathing, looping until stopped. </summary>
        public void StartIdle(float glowStrength) {
            StopIdle();
            bob = 0f;
            sway = 0f;
            glowPulse = 1f;
            DOTween.To(() => bob, v => { bob = v; Apply(); }, -26f, 2.8f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetId(idleId);
            DOTween.To(() => sway, v => { sway = v; Apply(); }, 4f, 3.7f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetId(idleId);
            DOTween.To(() => glowPulse, v => { glowPulse = v; Apply(); }, glowStrength, 2.2f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetId(idleId);
        }

        public void StopIdle() {
            DOTween.Kill(idleId);
            bob = 0f;
            sway = 0f;
            glowPulse = 1f;
            Apply();
        }

        public void Snap(float targetTop, float targetScale, float targetAlpha, float targetGlow) {
            DOTween.Kill(tweenId);
            top = targetTop;
            scale = targetScale;
            alpha = targetAlpha;
            glowAlpha = targetGlow;
            drag = 0f;
            Apply();
        }

        private void Apply() {
            stage.style.top = top + drag;
            stage.style.translate = new Translate(0f, bob, 0f);
            stage.style.scale = new Scale(new Vector3(scale, scale, 1f));
            stage.style.opacity = alpha;
            body.style.rotate = new Rotate(BaseRotation + sway);
            glow.style.opacity = Mathf.Clamp01(glowAlpha * glowPulse);
        }
    }
}
