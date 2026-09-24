using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// Write-ahead store for submissions. Every submission is written to disk before the first
    /// send, so a crash, power cut or dropped network never loses a leaf. The file is deleted the
    /// moment the server confirms delivery, so names and promises never outlive a successful send.
    /// Retries reuse the same requestId, which the server uses to discard duplicates.
    /// </summary>
    public class SubmissionQueue : MonoBehaviour {
        private const string FolderName = "pending_promises";
        private const string FileExtension = ".json";

        [SerializeField] private KioskConfig config;
        [SerializeField] private KioskApiClient api;

        private readonly HashSet<string> inFlight = new HashSet<string>();
        private string folderPath;
        private float nextRetryTime;
        private float currentBackoff;

        public event Action<SubmitResponse> OfflineSubmissionDelivered;

        public int PendingCount {
            get { return ListPendingFiles().Length; }
        }

        public void Initialize(KioskConfig kioskConfig, KioskApiClient apiClient) {
            config = kioskConfig;
            api = apiClient;
            folderPath = Path.Combine(Application.persistentDataPath, FolderName);
            Directory.CreateDirectory(folderPath);
            currentBackoff = config.RetryMinSeconds;
            nextRetryTime = Time.realtimeSinceStartup + 2f;
        }

        /// <summary> Persists the submission, then tries to deliver it once right away. </summary>
        public void SubmitNow(PromiseSubmission submission, Action<DeliveryStatus, SubmitResponse> onDone) {
            Persist(submission);
            Send(submission, onDone);
        }

        /// <summary> Retries everything waiting on disk immediately (operator action). </summary>
        public void RetryAllNow() {
            currentBackoff = config.RetryMinSeconds;
            nextRetryTime = 0f;
        }

        private void Update() {
            if (folderPath == null || Time.realtimeSinceStartup < nextRetryTime) {
                return;
            }
            nextRetryTime = Time.realtimeSinceStartup + currentBackoff;

            string[] files = ListPendingFiles();
            if (files.Length == 0) {
                currentBackoff = config.RetryMinSeconds;
                return;
            }

            // Oldest first, one at a time: keeps the wall's arrival order and the network quiet.
            Array.Sort(files, CompareByWriteTime);
            PromiseSubmission submission = Load(files[0]);
            if (submission == null) {
                return;
            }
            Send(submission, (DeliveryStatus status, SubmitResponse response) => {
                if (status == DeliveryStatus.Unreachable) {
                    currentBackoff = Mathf.Min(currentBackoff * 2f, config.RetryMaxSeconds);
                    return;
                }
                currentBackoff = config.RetryMinSeconds;
                nextRetryTime = Time.realtimeSinceStartup + 0.5f;
                if (status == DeliveryStatus.Delivered && OfflineSubmissionDelivered != null) {
                    OfflineSubmissionDelivered(response);
                }
            });
        }

        private void Send(PromiseSubmission submission, Action<DeliveryStatus, SubmitResponse> onDone) {
            if (!inFlight.Add(submission.requestId)) {
                onDone(DeliveryStatus.Unreachable, null);
                return;
            }
            api.Submit(submission, (DeliveryStatus status, SubmitResponse response) => {
                inFlight.Remove(submission.requestId);
                if (status == DeliveryStatus.Delivered) {
                    Delete(submission.requestId);
                } else if (status == DeliveryStatus.PermanentFailure) {
                    // The server refuses this payload outright; retrying would loop forever.
                    Debug.LogError("[SubmissionQueue] A submission was refused permanently by the server and has been discarded.");
                    Delete(submission.requestId);
                }
                onDone(status, response);
            });
        }

        private void Persist(PromiseSubmission submission) {
            string target = PathFor(submission.requestId);
            string temp = target + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(submission));
            if (File.Exists(target)) {
                File.Delete(target);
            }
            File.Move(temp, target);
        }

        private PromiseSubmission Load(string path) {
            try {
                PromiseSubmission submission = JsonUtility.FromJson<PromiseSubmission>(File.ReadAllText(path));
                if (submission != null && !string.IsNullOrEmpty(submission.requestId)) {
                    return submission;
                }
            } catch (Exception exception) {
                Debug.LogError("[SubmissionQueue] Unreadable pending file, discarding: " + exception.GetType().Name);
            }
            File.Delete(path);
            return null;
        }

        private void Delete(string requestId) {
            string path = PathFor(requestId);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }

        private string[] ListPendingFiles() {
            if (folderPath == null || !Directory.Exists(folderPath)) {
                return new string[0];
            }
            return Directory.GetFiles(folderPath, "*" + FileExtension);
        }

        private string PathFor(string requestId) {
            return Path.Combine(folderPath, requestId + FileExtension);
        }

        private static int CompareByWriteTime(string a, string b) {
            return File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b));
        }
    }
}
