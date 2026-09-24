using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

using DG.Tweening;

using UnityEngine;
using UnityEngine.UIElements;

namespace GoodDeedTreeMobileApp {
    public enum KioskScreen {
        Attract = 0,
        Input = 1,
        Release = 2,
        Sending = 3,
        Result = 4,
        ThankYou = 5
    }

    /// <summary>
    /// Tree kiosk flow: Attract -> Input -> Release -> Sending -> Result -> Thank you -> Attract.
    /// Also owns the always-on behaviours: idle reset, promise count, suggestion list.
    /// Visitor names and promises are only held in memory for the current visitor and are never logged.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(KioskApiClient))]
    [RequireComponent(typeof(SubmissionQueue))]
    public class KioskFlow : MonoBehaviour {
        private const string VisibleClass = "screen--visible";
        private const string CountCacheKey = "GoodDeedKiosk.PromiseCount";
        private const float SwipeDistance = 170f;
        private const float SwipeMaxSeconds = 1.2f;
        private const float LiftSeconds = 1.3f;
        private const float ScreenFadeSeconds = 0.35f;

        private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-US");

        [SerializeField] private KioskConfig config;

        private readonly Dictionary<KioskScreen, VisualElement> screens = new Dictionary<KioskScreen, VisualElement>();
        private readonly List<Button> chipButtons = new List<Button>();
        private readonly List<string> suggestions = new List<string>();

        private KioskApiClient api;
        private SubmissionQueue queue;
        private OperatorPanel operatorPanel;
        private LeafView leaf;

        private VisualElement root;
        private Label countLabel;
        private Label attractHint;
        private TextField nameField;
        private TextField promiseField;
        private Label nameCounter;
        private Label promiseCounter;
        private VisualElement chipsContainer;
        private Button continueButton;
        private Label swipeArrow;
        private Label resultLabel;
        private VisualElement resultIcon;
        private Label thanksLabel;

        private KioskScreen current = KioskScreen.Attract;
        private float lastInteraction;
        private float nextCountRefresh;
        private int promiseCount;
        private string selectedSuggestion = string.Empty;
        private Coroutine flowRoutine;

        private bool swipeTracking;
        private int swipePointerId;
        private float swipeStartY;
        private float swipeStartTime;

        private void Awake() {
            if (config == null) {
                Debug.LogError("[KioskFlow] No KioskConfig assigned - the kiosk cannot start.", this);
                enabled = false;
                return;
            }
            KioskDevice.Apply();
            api = GetComponent<KioskApiClient>();
            queue = GetComponent<SubmissionQueue>();
            api.Initialize(config);
            queue.Initialize(config, api);
            queue.OfflineSubmissionDelivered += OnOfflineSubmissionDelivered;
            promiseCount = PlayerPrefs.GetInt(CountCacheKey, 0);
            for (int i = 0; i < config.DefaultSuggestions.Count; i++) {
                suggestions.Add(config.DefaultSuggestions[i]);
            }
        }

        private ConnectionStatusView connectionStatus;

        private void OnEnable() {
            if (config == null) {
                return;
            }
            root = GetComponent<UIDocument>().rootVisualElement;
            BindUi();
            operatorPanel = new OperatorPanel(root, config, api, queue);
            leaf = new LeafView(root);
            connectionStatus = new ConnectionStatusView(root, api, queue);
            BuildChips();
            UpdateCountLabel();
            ShowScreenImmediate(KioskScreen.Attract);
            EnterAttract();
            FetchStartupData();
        }

        private void OnDestroy() {
            if (queue != null) {
                queue.OfflineSubmissionDelivered -= OnOfflineSubmissionDelivered;
            }
            DOTween.KillAll();
        }

        private void Update() {
            if (connectionStatus != null) {
                connectionStatus.Tick();
            }
            float now = Time.realtimeSinceStartup;
            bool inProgress = current != KioskScreen.Attract || operatorPanel.IsOpen;
            if (inProgress && now - lastInteraction > config.IdleResetSeconds) {
                ResetToAttract();
                return;
            }
            if (current == KioskScreen.Attract && now >= nextCountRefresh) {
                nextCountRefresh = now + config.CountRefreshSeconds;
                RefreshCount();
            }
        }

        // ------------------------------------------------------------------ binding

        private void BindUi() {
            screens.Clear();
            screens.Add(KioskScreen.Attract, root.Q<VisualElement>("screen-attract"));
            screens.Add(KioskScreen.Input, root.Q<VisualElement>("screen-input"));
            screens.Add(KioskScreen.Release, root.Q<VisualElement>("screen-release"));
            screens.Add(KioskScreen.Sending, root.Q<VisualElement>("screen-sending"));
            screens.Add(KioskScreen.Result, root.Q<VisualElement>("screen-result"));
            screens.Add(KioskScreen.ThankYou, root.Q<VisualElement>("screen-thanks"));

            countLabel = root.Q<Label>("count-label");
            attractHint = root.Q<Label>("attract-hint");
            nameField = root.Q<TextField>("name-field");
            promiseField = root.Q<TextField>("promise-field");
            nameCounter = root.Q<Label>("name-counter");
            promiseCounter = root.Q<Label>("promise-counter");
            chipsContainer = root.Q<VisualElement>("chips");
            continueButton = root.Q<Button>("continue-button");
            swipeArrow = root.Q<Label>("swipe-arrow");
            resultLabel = root.Q<Label>("result-label");
            resultIcon = root.Q<VisualElement>("result-icon");
            thanksLabel = root.Q<Label>("thanks-label");

            root.RegisterCallback<PointerDownEvent>(OnAnyPointer, TrickleDown.TrickleDown);
            screens[KioskScreen.Attract].RegisterCallback<PointerDownEvent>(OnAttractTapped);

            nameField.maxLength = config.NameMaxLength;
            promiseField.maxLength = config.PromiseMaxLength;
            nameField.RegisterValueChangedCallback(OnNameChanged);
            promiseField.RegisterValueChangedCallback(OnPromiseChanged);
            continueButton.clicked += OnContinue;

            VisualElement release = screens[KioskScreen.Release];
            release.RegisterCallback<PointerDownEvent>(OnReleasePointerDown);
            release.RegisterCallback<PointerMoveEvent>(OnReleasePointerMove);
            release.RegisterCallback<PointerUpEvent>(OnReleasePointerUp);
            release.RegisterCallback<PointerCancelEvent>(OnReleasePointerCancel);
            root.Q<Button>("release-button").clicked += ReleaseLeaf;
            root.Q<Button>("edit-button").clicked += OnEditPromise;
        }

        private void BuildChips() {
            chipsContainer.Clear();
            chipButtons.Clear();
            for (int i = 0; i < suggestions.Count; i++) {
                string suggestion = suggestions[i];
                if (suggestion.Length > config.PromiseMaxLength) {
                    continue;
                }
                Button chip = new Button(() => OnChipTapped(suggestion));
                chip.text = suggestion;
                chip.AddToClassList("chip");
                chipsContainer.Add(chip);
                chipButtons.Add(chip);
            }
            RefreshChipSelection();
        }

        // ------------------------------------------------------------------ startup data

        private void FetchStartupData() {
            RefreshCount();
            nextCountRefresh = Time.realtimeSinceStartup + config.CountRefreshSeconds;
            api.FetchSuggestions((bool ok, List<string> list) => {
                if (!ok) {
                    return;
                }
                suggestions.Clear();
                suggestions.AddRange(list);
                BuildChips();
            });
        }

        private void RefreshCount() {
            api.FetchPromiseCount((bool ok, int count) => {
                if (ok) {
                    SetPromiseCount(Mathf.Max(count, 0));
                }
            });
        }

        private void SetPromiseCount(int count) {
            promiseCount = count;
            PlayerPrefs.SetInt(CountCacheKey, promiseCount);
            UpdateCountLabel();
        }

        private void UpdateCountLabel() {
            countLabel.style.display = promiseCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            countLabel.text = promiseCount.ToString("N0", NumberCulture) + (promiseCount == 1 ? " promise planted" : " promises planted");
        }

        // ------------------------------------------------------------------ screens

        private void ShowScreenImmediate(KioskScreen screen) {
            foreach (KeyValuePair<KioskScreen, VisualElement> pair in screens) {
                bool visible = pair.Key == screen;
                pair.Value.EnableInClassList(VisibleClass, visible);
                pair.Value.style.opacity = visible ? 1f : 0f;
            }
            current = screen;
        }

        private void ShowScreen(KioskScreen screen) {
            if (screen == current) {
                return;
            }
            VisualElement from = screens[current];
            VisualElement to = screens[screen];
            DOTween.Kill(from);
            DOTween.Kill(to);
            DOTween.To(() => from.resolvedStyle.opacity, v => from.style.opacity = v, 0f, ScreenFadeSeconds)
                .SetId(from)
                .OnComplete(() => from.RemoveFromClassList(VisibleClass));
            to.AddToClassList(VisibleClass);
            to.style.opacity = 0f;
            DOTween.To(() => 0f, v => to.style.opacity = v, 1f, ScreenFadeSeconds).SetId(to).SetDelay(ScreenFadeSeconds * 0.5f);
            current = screen;
        }

        private void EnterAttract() {
            leaf.SetText(string.Empty, string.Empty);
            leaf.MoveTo(640f, 1f, 1f, 0.75f, 0.9f, Ease.InOutSine);
            leaf.StartIdle(1.35f);
            DOTween.Kill(attractHint);
            attractHint.style.opacity = 1f;
            DOTween.To(() => 1f, v => attractHint.style.opacity = v, 0.35f, 1.6f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetId(attractHint);
        }

        private void ResetToAttract() {
            StopFlowRoutine();
            operatorPanel.CloseAll();
            ClearForm(true);
            ShowScreen(KioskScreen.Attract);
            EnterAttract();
            RefreshCount();
        }

        private void ClearForm(bool clearName) {
            if (clearName) {
                nameField.SetValueWithoutNotify(string.Empty);
            }
            promiseField.SetValueWithoutNotify(string.Empty);
            selectedSuggestion = string.Empty;
            root.focusController.focusedElement?.Blur();
            UpdateInputState();
        }

        // ------------------------------------------------------------------ attract

        private void OnAnyPointer(PointerDownEvent evt) {
            lastInteraction = Time.realtimeSinceStartup;
        }

        private void OnAttractTapped(PointerDownEvent evt) {
            if (current != KioskScreen.Attract || operatorPanel.IsOpen) {
                return;
            }
            GoToInput(false);
        }

        // ------------------------------------------------------------------ input

        private void GoToInput(bool keepName) {
            DOTween.Kill(attractHint);
            ClearForm(!keepName);
            leaf.StopIdle();
            leaf.MoveTo(40f, 0.72f, 1f, 0.35f, 0.7f, Ease.OutCubic);
            ShowScreen(KioskScreen.Input);
            UpdateInputState();
        }

        private void OnNameChanged(ChangeEvent<string> evt) {
            string cleaned = StripLineBreaks(evt.newValue);
            if (cleaned != evt.newValue) {
                nameField.SetValueWithoutNotify(cleaned);
            }
            lastInteraction = Time.realtimeSinceStartup;
            UpdateInputState();
        }

        private void OnPromiseChanged(ChangeEvent<string> evt) {
            string cleaned = StripLineBreaks(evt.newValue);
            if (cleaned != evt.newValue) {
                promiseField.SetValueWithoutNotify(cleaned);
            }
            if (!string.Equals(cleaned, selectedSuggestion, StringComparison.Ordinal)) {
                selectedSuggestion = string.Empty;
            }
            lastInteraction = Time.realtimeSinceStartup;
            UpdateInputState();
        }

        private void OnChipTapped(string suggestion) {
            selectedSuggestion = suggestion;
            promiseField.value = suggestion;
            selectedSuggestion = suggestion;
            UpdateInputState();
        }

        private void UpdateInputState() {
            string visitorName = CurrentName();
            string promise = CurrentPromise();
            nameCounter.text = nameField.value.Length + " / " + config.NameMaxLength;
            promiseCounter.text = promiseField.value.Length + " / " + config.PromiseMaxLength;
            nameCounter.EnableInClassList("field-counter--full", nameField.value.Length >= config.NameMaxLength);
            promiseCounter.EnableInClassList("field-counter--full", promiseField.value.Length >= config.PromiseMaxLength);
            leaf.SetText(visitorName, promise);
            continueButton.SetEnabled(IsValid(visitorName, config.NameMaxLength) && IsValid(promise, config.PromiseMaxLength));
            RefreshChipSelection();
        }

        private void RefreshChipSelection() {
            for (int i = 0; i < chipButtons.Count; i++) {
                chipButtons[i].EnableInClassList("chip--selected", chipButtons[i].text == selectedSuggestion && selectedSuggestion.Length > 0);
            }
        }

        private void OnContinue() {
            if (!IsValid(CurrentName(), config.NameMaxLength) || !IsValid(CurrentPromise(), config.PromiseMaxLength)) {
                return;
            }
            root.focusController.focusedElement?.Blur();
            leaf.MoveTo(600f, 1.08f, 1f, 1f, 0.8f, Ease.OutBack);
            leaf.StartIdle(1.5f);
            ShowScreen(KioskScreen.Release);
            DOTween.Kill(swipeArrow);
            swipeArrow.style.translate = new Translate(0f, 0f, 0f);
            DOTween.To(() => 0f, v => swipeArrow.style.translate = new Translate(0f, v, 0f), -34f, 0.9f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetId(swipeArrow);
        }

        private void OnEditPromise() {
            DOTween.Kill(swipeArrow);
            leaf.StopIdle();
            leaf.MoveTo(40f, 0.72f, 1f, 0.35f, 0.6f, Ease.OutCubic);
            ShowScreen(KioskScreen.Input);
        }

        // ------------------------------------------------------------------ release

        private void OnReleasePointerDown(PointerDownEvent evt) {
            if (current != KioskScreen.Release || evt.target is Button) {
                return;
            }
            swipeTracking = true;
            swipePointerId = evt.pointerId;
            swipeStartY = evt.position.y;
            swipeStartTime = Time.realtimeSinceStartup;
            screens[KioskScreen.Release].CapturePointer(evt.pointerId);
        }

        private void OnReleasePointerMove(PointerMoveEvent evt) {
            if (!swipeTracking || evt.pointerId != swipePointerId) {
                return;
            }
            float up = Mathf.Min(0f, evt.position.y - swipeStartY);
            leaf.SetDrag(up * 0.6f);
        }

        private void OnReleasePointerUp(PointerUpEvent evt) {
            if (!swipeTracking || evt.pointerId != swipePointerId) {
                return;
            }
            EndSwipeTracking();
            float travelled = swipeStartY - evt.position.y;
            float seconds = Time.realtimeSinceStartup - swipeStartTime;
            if (travelled >= SwipeDistance && seconds <= SwipeMaxSeconds) {
                ReleaseLeaf();
            } else {
                leaf.SpringBack();
            }
        }

        private void OnReleasePointerCancel(PointerCancelEvent evt) {
            if (!swipeTracking || evt.pointerId != swipePointerId) {
                return;
            }
            EndSwipeTracking();
            leaf.SpringBack();
        }

        private void EndSwipeTracking() {
            swipeTracking = false;
            VisualElement release = screens[KioskScreen.Release];
            if (release.HasPointerCapture(swipePointerId)) {
                release.ReleasePointer(swipePointerId);
            }
        }

        private void ReleaseLeaf() {
            if (current != KioskScreen.Release) {
                return;
            }
            DOTween.Kill(swipeArrow);
            PromiseSubmission submission = new PromiseSubmission();
            submission.requestId = Guid.NewGuid().ToString("N");
            submission.name = CurrentName();
            submission.promise = CurrentPromise();
            submission.suggestion = string.Equals(submission.promise, selectedSuggestion, StringComparison.Ordinal) ? selectedSuggestion : string.Empty;
            submission.createdAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            StopFlowRoutine();
            flowRoutine = StartCoroutine(SendRoutine(submission));
        }

        // ------------------------------------------------------------------ sending / result / thanks

        private IEnumerator SendRoutine(PromiseSubmission submission) {
            ShowScreen(KioskScreen.Sending);
            Sequence lift = leaf.LiftOff(LiftSeconds);

            bool done = false;
            DeliveryStatus status = DeliveryStatus.Unreachable;
            SubmitResponse response = null;
            queue.SubmitNow(submission, (DeliveryStatus s, SubmitResponse r) => {
                status = s;
                response = r;
                done = true;
            });
            yield return lift.WaitForCompletion();
            while (!done) {
                yield return null;
            }

            SubmitResult result = ToResult(status, response);
            if (result.Outcome == SubmitOutcome.Rejected) {
                yield return ShowRejected();
                yield break;
            }

            // Optimistic +1; the next stats refresh replaces it with the server's real count.
            SetPromiseCount(promiseCount + 1);

            resultLabel.text = ResultMessage(result);
            ShowScreen(KioskScreen.Result);
            yield return new WaitForSecondsRealtime(config.ResultHoldSeconds);

            thanksLabel.text = result.LeafNumber > 0
                ? "Your leaf is now part of the Tree — leaf #" + result.LeafNumber.ToString("N0", NumberCulture)
                : "Your leaf is now part of the Tree";
            ShowScreen(KioskScreen.ThankYou);
            yield return new WaitForSecondsRealtime(config.ThankYouSeconds);

            flowRoutine = null;
            ResetToAttract();
        }

        private IEnumerator ShowRejected() {
            resultLabel.text = "Let's try a different promise";
            ShowScreen(KioskScreen.Result);
            yield return new WaitForSecondsRealtime(config.RejectedHoldSeconds);
            flowRoutine = null;
            leaf.Snap(-300f, 0.72f, 0f, 0.35f);
            GoToInput(true);
        }

        private static SubmitResult ToResult(DeliveryStatus status, SubmitResponse response) {
            if (status == DeliveryStatus.Unreachable) {
                return new SubmitResult(SubmitOutcome.SavedOffline, 0, 0);
            }
            if (status == DeliveryStatus.PermanentFailure || response == null ||
                string.Equals(response.status, "rejected", StringComparison.OrdinalIgnoreCase)) {
                return new SubmitResult(SubmitOutcome.Rejected, 0, 0);
            }
            SubmitOutcome outcome = response.queuePosition > 0 ? SubmitOutcome.Queued : SubmitOutcome.PlaysNow;
            return new SubmitResult(outcome, response.leafNumber, response.queuePosition);
        }

        private static string ResultMessage(SubmitResult result) {
            if (result.Outcome == SubmitOutcome.Queued) {
                return "Your leaf is #" + result.QueuePosition.ToString("N0", NumberCulture) + " in line — keep watching the tree.";
            }
            if (result.Outcome == SubmitOutcome.SavedOffline) {
                return "Your leaf is on its way — keep watching the tree.";
            }
            return "Look up at the tree — your leaf is arriving.";
        }

        private void OnOfflineSubmissionDelivered(SubmitResponse response) {
            RefreshCount();
        }

        private void StopFlowRoutine() {
            if (flowRoutine != null) {
                StopCoroutine(flowRoutine);
                flowRoutine = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        private string CurrentName() {
            return nameField.value.Trim();
        }

        private string CurrentPromise() {
            return promiseField.value.Trim();
        }

        private static bool IsValid(string value, int maxLength) {
            return value.Length > 0 && value.Length <= maxLength;
        }

        private static string StripLineBreaks(string value) {
            if (string.IsNullOrEmpty(value)) {
                return string.Empty;
            }
            return value.Replace("\r", string.Empty).Replace("\n", " ");
        }
    }
}
