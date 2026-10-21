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
        private float drag;          // vertical finger offset applied now (panel px)
        private float dragX;         // horizontal finger offset applied now
        private float targetDrag;    // where the finger is; drag/dragX ease towards it
        private float targetDragX;
        private bool following;
        private const float FollowSharpness = 18f;   // higher = tighter to the finger
        private const float TouchScale = 1.08f;      // leaf grows this much while held
        private float touch = 1f;                    // current touch scale multiplier
        private readonly object touchId = new object();

        public LeafView(VisualElement root) {
            stage = root.Q<VisualElement>("leaf-stage");
            body = root.Q<VisualElement>("leaf-body");
            glow = root.Q<VisualElement>("leaf-glow");
            nameLabel = root.Q<Label>("leaf-name");
            promiseLabel = root.Q<Label>("leaf-promise");
            top = 640f;
            Apply();
        }

        /// <summary> One sentence on the leaf: "<b>Name</b> chose to ...". </summary>
        public void SetText(string visitorName, string promise) {
            nameLabel.style.display = DisplayStyle.None;
            promiseLabel.enableRichText = true;
            promiseLabel.text = DeedSentence.Rich(visitorName, promise);
            FitText();
        }

        private const float MaxFontSize = 72f;   // a couple of words: big and bold on the leaf
        private const float MinFontSize = 24f;   // the longest name + promise still fits
        private const float FontStep = 2f;

        /// <summary>
        /// Picks the largest font size at which the sentence fits inside the leaf's text box
        /// (leaf-text). Short sentences get big text, long ones shrink, nothing spills off the leaf.
        /// </summary>
        private void FitText() {
            VisualElement box = promiseLabel.parent;
            float width = box.layout.width;
            float height = box.layout.height;
            if (float.IsNaN(width) || width <= 0f || float.IsNaN(height) || height <= 0f) {
                // Not laid out yet: fit once the box has a size.
                box.RegisterCallback<GeometryChangedEvent>(OnTextBoxLaidOut);
                return;
            }
            float size = MaxFontSize;
            while (size > MinFontSize) {
                promiseLabel.style.fontSize = size;
                Vector2 needed = promiseLabel.MeasureTextSize(promiseLabel.text, width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined);
                if (needed.y <= height) {
                    return;
                }
                size -= FontStep;
            }
            promiseLabel.style.fontSize = MinFontSize;
        }

        private void OnTextBoxLaidOut(GeometryChangedEvent evt) {
            promiseLabel.parent.UnregisterCallback<GeometryChangedEvent>(OnTextBoxLaidOut);
            FitText();
        }

        /// <summary> Finger is on the leaf: start following it and scale up a little. </summary>
        public void BeginFollow() {
            DOTween.Kill(tweenId);
            following = true;
            targetDrag = drag;
            targetDragX = dragX;
            SetTouch(TouchScale, 0.2f, Ease.OutBack);
        }

        /// <summary> Where the finger is now, relative to where it touched down (panel px). </summary>
        public void FollowTo(float offsetX, float offsetY) {
            targetDragX = offsetX;
            targetDrag = offsetY;
        }

        /// <summary> Called every frame: eases the leaf towards the finger. </summary>
        public void Tick(float deltaTime) {
            if (!following) {
                return;
            }
            float k = 1f - Mathf.Exp(-FollowSharpness * deltaTime);
            drag = Mathf.Lerp(drag, targetDrag, k);
            dragX = Mathf.Lerp(dragX, targetDragX, k);
            Apply();
        }

        /// <summary> Finger lifted without a release: glide back home at normal size. </summary>
        public Tween SpringBack() {
            following = false;
            SetTouch(1f, 0.3f, Ease.OutSine);
            Sequence back = DOTween.Sequence().SetId(tweenId);
            back.Join(DOTween.To(() => drag, v => { drag = v; Apply(); }, 0f, 0.45f).SetEase(Ease.OutBack));
            back.Join(DOTween.To(() => dragX, v => { dragX = v; Apply(); }, 0f, 0.45f).SetEase(Ease.OutBack));
            return back;
        }

        private void SetTouch(float target, float seconds, Ease ease) {
            DOTween.Kill(touchId);
            DOTween.To(() => touch, v => { touch = v; Apply(); }, target, seconds).SetEase(ease).SetId(touchId);
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
            // Carry on from wherever the finger let go - no jump back to the rest spot.
            following = false;
            top += drag;
            drag = 0f;
            targetDrag = 0f;
            SetTouch(1f, 0.25f, Ease.OutSine);   // back to normal size as it lets go
            Sequence sequence = DOTween.Sequence().SetId(tweenId);
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
            dragX = 0f;
            targetDrag = 0f;
            targetDragX = 0f;
            following = false;
            DOTween.Kill(touchId);
            touch = 1f;
            Apply();
        }

        private void Apply() {
            stage.style.top = top + drag;
            stage.style.translate = new Translate(dragX, bob, 0f);
            stage.style.scale = new Scale(new Vector3(scale * touch, scale * touch, 1f));
            stage.style.opacity = alpha;
            // Fully qualified: the Unity-Logs-Viewer plugin declares a global Rotate class.
            body.style.rotate = new UnityEngine.UIElements.Rotate(new Angle(BaseRotation + sway, AngleUnit.Degree));
            glow.style.opacity = Mathf.Clamp01(glowAlpha * glowPulse);
        }
    }
}
