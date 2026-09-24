using System;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;
using UnityEngine.UIElements;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// Hidden operator access: N taps on the top-right corner open a PIN pad; the right PIN opens
    /// the operator panel (server check, pending offline leaves, restart, exit kiosk).
    /// </summary>
    public class OperatorPanel {
        private const string VisibleClass = "overlay--visible";
        private const int PinLength = 4;

        private readonly KioskConfig config;
        private readonly KioskApiClient api;
        private readonly SubmissionQueue queue;
        private readonly VisualElement pinOverlay;
        private readonly VisualElement operatorOverlay;
        private readonly Label pinDots;
        private readonly Label pinError;
        private readonly Label serverLabel;
        private readonly Label pendingLabel;
        private readonly StringBuilder pinBuffer = new StringBuilder();

        private int cornerTaps;
        private float firstTapTime;
        private int failedAttempts;
        private float lockedUntil;

        public bool IsOpen {
            get { return pinOverlay.ClassListContains(VisibleClass) || operatorOverlay.ClassListContains(VisibleClass); }
        }

        public OperatorPanel(VisualElement root, KioskConfig kioskConfig, KioskApiClient apiClient, SubmissionQueue submissionQueue) {
            config = kioskConfig;
            api = apiClient;
            queue = submissionQueue;

            pinOverlay = root.Q<VisualElement>("pin-overlay");
            operatorOverlay = root.Q<VisualElement>("operator-overlay");
            pinDots = root.Q<Label>("pin-dots");
            pinError = root.Q<Label>("pin-error");
            serverLabel = root.Q<Label>("op-server");
            pendingLabel = root.Q<Label>("op-pending");
            root.Q<Label>("op-url").text = api.BaseUrl;

            root.Q<VisualElement>("operator-corner").RegisterCallback<PointerDownEvent>(OnCornerTapped);

            VisualElement pad = root.Q<VisualElement>("pin-pad");
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "<" };
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i];
                Button button = new Button(() => OnPinKey(key));
                button.text = key == "<" ? "⌫" : key;
                button.AddToClassList("pin-key");
                pad.Add(button);
            }

            root.Q<Button>("pin-cancel").clicked += CloseAll;
            root.Q<Button>("pin-enter").clicked += SubmitPin;
            root.Q<Button>("op-check").clicked += CheckServer;
            root.Q<Button>("op-retry").clicked += RetryPending;
            root.Q<Button>("op-restart").clicked += KioskDevice.RestartApp;
            root.Q<Button>("op-exit").clicked += KioskDevice.ExitKiosk;
            root.Q<Button>("op-close").clicked += CloseAll;
        }

        public void CloseAll() {
            pinBuffer.Clear();
            pinOverlay.RemoveFromClassList(VisibleClass);
            operatorOverlay.RemoveFromClassList(VisibleClass);
        }

        private void OnCornerTapped(PointerDownEvent evt) {
            evt.StopPropagation();
            float now = Time.realtimeSinceStartup;
            if (cornerTaps == 0 || now - firstTapTime > config.OperatorTapWindowSeconds) {
                cornerTaps = 0;
                firstTapTime = now;
            }
            cornerTaps++;
            if (cornerTaps >= config.OperatorTapCount) {
                cornerTaps = 0;
                OpenPinPad();
            }
        }

        private void OpenPinPad() {
            pinBuffer.Clear();
            RefreshPinDots();
            pinError.text = IsLockedOut() ? LockoutMessage() : " ";
            pinOverlay.AddToClassList(VisibleClass);
        }

        private void OnPinKey(string key) {
            if (key == "C") {
                pinBuffer.Clear();
            } else if (key == "<") {
                if (pinBuffer.Length > 0) {
                    pinBuffer.Length--;
                }
            } else if (pinBuffer.Length < PinLength) {
                pinBuffer.Append(key);
            }
            RefreshPinDots();
            if (pinBuffer.Length == PinLength) {
                SubmitPin();
            }
        }

        private void SubmitPin() {
            if (IsLockedOut()) {
                pinError.text = LockoutMessage();
                pinBuffer.Clear();
                RefreshPinDots();
                return;
            }
            bool valid = string.Equals(Sha256Hex(pinBuffer.ToString()), config.OperatorPinSha256, StringComparison.OrdinalIgnoreCase);
            pinBuffer.Clear();
            RefreshPinDots();
            if (!valid) {
                failedAttempts++;
                if (failedAttempts >= config.OperatorMaxAttempts) {
                    failedAttempts = 0;
                    lockedUntil = Time.realtimeSinceStartup + config.OperatorLockoutSeconds;
                    pinError.text = LockoutMessage();
                    Debug.LogWarning("[Operator] Too many wrong PIN attempts, locked out.");
                } else {
                    pinError.text = "Wrong PIN";
                }
                return;
            }
            failedAttempts = 0;
            pinOverlay.RemoveFromClassList(VisibleClass);
            OpenOperator();
        }

        private void OpenOperator() {
            RefreshPending();
            serverLabel.text = "Server: checking…";
            operatorOverlay.AddToClassList(VisibleClass);
            CheckServer();
        }

        private void CheckServer() {
            serverLabel.text = "Server: checking…";
            api.CheckHealth((bool ok, long milliseconds) => {
                serverLabel.text = ok ? "Server: connected (" + milliseconds + " ms)" : "Server: NOT reachable";
                RefreshPending();
            });
        }

        private void RetryPending() {
            queue.RetryAllNow();
            RefreshPending();
        }

        private void RefreshPending() {
            pendingLabel.text = "Pending offline leaves: " + queue.PendingCount;
        }

        private void RefreshPinDots() {
            StringBuilder dots = new StringBuilder();
            for (int i = 0; i < PinLength; i++) {
                dots.Append(i < pinBuffer.Length ? "●" : "○");
            }
            pinDots.text = dots.ToString();
        }

        private bool IsLockedOut() {
            return Time.realtimeSinceStartup < lockedUntil;
        }

        private string LockoutMessage() {
            int seconds = Mathf.CeilToInt(lockedUntil - Time.realtimeSinceStartup);
            return "Locked. Try again in " + seconds + " s";
        }

        private static string Sha256Hex(string value) {
            using (SHA256 sha = SHA256.Create()) {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) {
                    hex.Append(hash[i].ToString("x2"));
                }
                return hex.ToString();
            }
        }
    }
}
