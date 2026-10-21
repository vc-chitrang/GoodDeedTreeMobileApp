using System.Collections.Generic;

using DG.Tweening;

using UnityEngine;
using UnityEngine.UIElements;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// Attract-screen testimonials: the four deed cards authored in Kiosk.uxml drift slowly upward
    /// like leaves on a breeze, each showing an already-planted deed. Cards are staggered so one
    /// fades in as another fades out. Deeds come from the wall (GET /api/v1/recent), with a small
    /// built-in set until the wall has any. Cards ignore touches, so a tap anywhere still starts.
    /// </summary>
    public class IdleDeedCards {
        private const float RiseSeconds = 11f;      // one card's trip from bottom to top
        private const float StaggerSeconds = 2.9f;  // gap between card launches
        private const float RefreshSeconds = 60f;   // how often new deeds are fetched

        private static readonly string[,] Fallback = {
            { "Aarav", "Call my grandmother every Sunday" },
            { "Meera", "Plant ten saplings this monsoon" },
            { "Kabir", "Help a classmate with maths" },
            { "Ananya", "Carry a cloth bag, not plastic" },
            { "Rohan", "Read to the children next door" },
            { "Isha", "Save water while brushing" },
        };

        private readonly VisualElement area;
        private readonly List<VisualElement> cards = new List<VisualElement>();
        private readonly List<Label> labels = new List<Label>();
        private readonly KioskApiClient api;
        private readonly List<string> sentences = new List<string>();
        private readonly object tweenId = new object();

        private bool running;
        private int nextCard;
        private int nextSentence;
        private float nextLaunch;
        private float nextRefresh;

        public IdleDeedCards(VisualElement root, KioskApiClient apiClient, KioskConfig config) {
            api = apiClient;
            area = root.Q<VisualElement>("deed-cards");
            for (int i = 0; i < 4; i++) {
                VisualElement card = root.Q<VisualElement>("deed-card-" + i);
                Label label = root.Q<Label>("deed-card-" + i + "-text");
                if (card == null || label == null) {
                    continue;
                }
                label.enableRichText = true;
                cards.Add(card);
                labels.Add(label);
            }
            for (int i = 0; i < Fallback.GetLength(0); i++) {
                sentences.Add(DeedSentence.Rich(Fallback[i, 0], Fallback[i, 1]));
            }
        }

        /// <summary> Call every frame; <paramref name="visible"/> is true while the attract screen shows. </summary>
        public void Tick(bool visible) {
            if (area == null || cards.Count == 0) {
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (visible && !running) {
                running = true;
                nextLaunch = now;
                if (now >= nextRefresh) {
                    Refresh();
                }
            } else if (!visible && running) {
                running = false;
                DOTween.Kill(tweenId);
                foreach (VisualElement card in cards) {
                    card.style.opacity = 0f;
                }
            }
            if (!running) {
                return;
            }
            if (now >= nextRefresh) {
                Refresh();
            }
            if (now >= nextLaunch) {
                nextLaunch = now + StaggerSeconds;
                Launch(nextCard);
                nextCard = (nextCard + 1) % cards.Count;
            }
        }

        private void Refresh() {
            nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;
            api.FetchRecent((bool ok, List<RecentDeed> deeds) => {
                if (!ok) {
                    return;   // keep what we have (fallback or last fetch)
                }
                sentences.Clear();
                foreach (RecentDeed deed in deeds) {
                    sentences.Add(DeedSentence.Rich(deed.name, deed.promise));
                }
                nextSentence = 0;
            });
        }

        private void Launch(int index) {
            VisualElement card = cards[index];
            Label label = labels[index];
            label.text = sentences[nextSentence % sentences.Count];
            nextSentence++;

            float areaWidth = area.layout.width > 0f ? area.layout.width : 1080f;
            float areaHeight = area.layout.height > 0f ? area.layout.height : 1920f;
            const float cardWidth = 560f;
            // Alternate sides so neighbouring cards don't overlap, with a little randomness.
            float lane = index % 2 == 0 ? 0.08f : 0.40f;
            float x = areaWidth * (lane + Random.Range(0f, 0.08f));
            x = Mathf.Min(x, areaWidth - cardWidth - 20f);
            float startY = areaHeight * 0.92f;
            float endY = areaHeight * 0.12f;
            float drift = Random.Range(-60f, 60f);
            float tilt = Random.Range(-4f, 4f);

            DOTween.Kill(card);
            card.style.left = x;
            card.style.top = startY;
            card.style.opacity = 0f;
            card.style.rotate = new UnityEngine.UIElements.Rotate(new Angle(tilt, AngleUnit.Degree));

            Sequence s = DOTween.Sequence().SetId(tweenId).SetTarget(card);
            s.Join(DOTween.To(() => startY, v => card.style.top = v, endY, RiseSeconds).SetEase(Ease.Linear));
            s.Join(DOTween.To(() => x, v => card.style.left = v, x + drift, RiseSeconds).SetEase(Ease.InOutSine));
            s.Join(DOTween.To(() => tilt, v => card.style.rotate = new UnityEngine.UIElements.Rotate(new Angle(v, AngleUnit.Degree)), -tilt, RiseSeconds).SetEase(Ease.InOutSine));
            // Fade in over the first fifth, hold, fade out over the last third.
            s.Insert(0f, DOTween.To(() => 0f, v => card.style.opacity = v, 1f, RiseSeconds * 0.2f).SetEase(Ease.OutSine));
            s.Insert(RiseSeconds * 0.66f, DOTween.To(() => 1f, v => card.style.opacity = v, 0f, RiseSeconds * 0.34f).SetEase(Ease.InSine));
        }
    }
}
