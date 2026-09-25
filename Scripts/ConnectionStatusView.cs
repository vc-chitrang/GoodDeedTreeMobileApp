using UnityEngine;
using UnityEngine.UIElements;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// Small wall-connection badge in the bottom-right corner. Polls /health on a timer and
    /// shows connected / offline plus any promises still waiting to be delivered.
    /// Shows no address, so nothing about the network is exposed to visitors.
    /// </summary>
    public class ConnectionStatusView {
        private const float PollSeconds = 5f;

        private readonly KioskApiClient api;
        private readonly SubmissionQueue queue;
        private readonly Label label;
        private float nextPoll;
        private bool waiting;
        private bool known;
        private bool online;
        private long latencyMs;

        public ConnectionStatusView(VisualElement root, KioskApiClient apiClient, SubmissionQueue submissionQueue) {
            api = apiClient;
            queue = submissionQueue;
            label = root.Q<Label>("connection-status");   // authored in Kiosk.uxml
            Render();
        }

        public void Tick() {
            if (waiting || Time.realtimeSinceStartup < nextPoll) {
                return;
            }
            waiting = true;
            api.CheckHealth((bool ok, long milliseconds) => {
                waiting = false;
                known = true;
                online = ok;
                latencyMs = milliseconds;
                nextPoll = Time.realtimeSinceStartup + PollSeconds;
                Render();
            });
        }

        private void Render() {
            string pending = queue != null && queue.PendingCount > 0 ? " \u00B7 " + queue.PendingCount + " pending" : string.Empty;
            label.RemoveFromClassList("connection-status--online");
            label.RemoveFromClassList("connection-status--offline");
            if (!known) {
                label.text = "\u25CF Connecting to wall\u2026" + pending;
                return;
            }
            label.AddToClassList(online ? "connection-status--online" : "connection-status--offline");
            label.text = online
                ? "\u25CF Wall connected \u00B7 " + latencyMs + " ms" + pending
                : "\u25CF Wall offline \u00B7 retrying" + pending;
        }
    }
}
