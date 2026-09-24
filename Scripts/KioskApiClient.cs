using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

using UnityEngine;
using UnityEngine.Networking;

using Debug = UnityEngine.Debug;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// HTTP client for the wall server. Deliberately does not log request or response bodies:
    /// they carry visitors' names and promises.
    /// </summary>
    public class KioskApiClient : MonoBehaviour {
        private const string StatsPath = "/api/v1/stats";
        private const string SuggestionsPath = "/api/v1/suggestions";
        private const string PromisesPath = "/api/v1/promises";
        private const string HealthPath = "/api/v1/health";

        private const string DiscoverRequest = "GOODDEEDTREE_DISCOVER";
        private const string DiscoverReply = "GOODDEEDTREE_WALL";
        private const int DiscoveryPort = 47777;
        private const float DiscoveryCooldownSeconds = 10f;
        private const string UrlPrefKey = "gdt.wallBaseUrl";

        [SerializeField] private KioskConfig config;

        private string baseUrl;
        private float nextDiscoveryTime;
        private volatile string discoveredUrl;
        private volatile bool discovering;

        /// <summary> URL in use: last one found by discovery, else the configured one. </summary>
        public string BaseUrl {
            get {
                if (string.IsNullOrEmpty(baseUrl)) {
                    baseUrl = PlayerPrefs.GetString(UrlPrefKey, config.ServerBaseUrl);
                }
                return baseUrl;
            }
        }

        public void Initialize(KioskConfig kioskConfig) {
            config = kioskConfig;
            baseUrl = null;
        }

        private void Update() {
            string found = discoveredUrl;
            if (found != null) {
                discoveredUrl = null;
                if (found != baseUrl) {
                    Debug.Log("[KioskApi] Wall found by discovery at " + found);
                    baseUrl = found;
                    PlayerPrefs.SetString(UrlPrefKey, found);
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>
        /// Broadcasts a discovery request on the LAN; the wall answers "GOODDEEDTREE_WALL port".
        /// Runs on a worker thread; the result is applied on the main thread in Update.
        /// </summary>
        private void StartDiscovery() {
            if (discovering || Time.unscaledTime < nextDiscoveryTime) {
                return;
            }
            discovering = true;
            nextDiscoveryTime = Time.unscaledTime + DiscoveryCooldownSeconds;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    using (UdpClient udp = new UdpClient()) {
                        udp.EnableBroadcast = true;
                        udp.Client.ReceiveTimeout = 1500;
                        byte[] request = Encoding.UTF8.GetBytes(DiscoverRequest);
                        udp.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        string[] parts = Encoding.UTF8.GetString(udp.Receive(ref from)).Trim().Split(' ');
                        int wallPort;
                        if (parts.Length == 2 && parts[0] == DiscoverReply && int.TryParse(parts[1], out wallPort)) {
                            discoveredUrl = "http://" + from.Address + ":" + wallPort;
                        }
                    }
                } catch (Exception exception) {
                    Debug.LogWarning("[KioskApi] Discovery got no answer: " + exception.GetType().Name);
                } finally {
                    discovering = false;
                }
            });
        }

        public void FetchPromiseCount(Action<bool, int> onDone) {
            StartCoroutine(GetJson(StatsPath, (bool ok, string body) => {
                if (!ok) {
                    onDone(false, 0);
                    return;
                }
                StatsResponse stats = Parse<StatsResponse>(body);
                onDone(stats != null, stats != null ? stats.promiseCount : 0);
            }));
        }

        public void FetchSuggestions(Action<bool, List<string>> onDone) {
            StartCoroutine(GetJson(SuggestionsPath, (bool ok, string body) => {
                List<string> list = new List<string>();
                if (ok) {
                    SuggestionsResponse response = Parse<SuggestionsResponse>(body);
                    if (response != null && response.suggestions != null) {
                        for (int i = 0; i < response.suggestions.Length; i++) {
                            if (!string.IsNullOrWhiteSpace(response.suggestions[i])) {
                                list.Add(response.suggestions[i].Trim());
                            }
                        }
                    }
                }
                onDone(ok && list.Count > 0, list);
            }));
        }

        public void CheckHealth(Action<bool, long> onDone) {
            Stopwatch watch = Stopwatch.StartNew();
            StartCoroutine(GetJson(HealthPath, (bool ok, string body) => {
                watch.Stop();
                onDone(ok, watch.ElapsedMilliseconds);
            }));
        }

        public void Submit(PromiseSubmission submission, Action<DeliveryStatus, SubmitResponse> onDone) {
            StartCoroutine(PostSubmission(submission, onDone));
        }

        private IEnumerator GetJson(string path, Action<bool, string> onDone) {
            using (UnityWebRequest request = UnityWebRequest.Get(BaseUrl + path)) {
                request.timeout = config.RequestTimeoutSeconds;
                request.SetRequestHeader("Accept", "application/json");
                yield return request.SendWebRequest();
                bool ok = request.result == UnityWebRequest.Result.Success;
                if (request.result == UnityWebRequest.Result.ConnectionError) {
                    StartDiscovery();
                }
                if (!ok) {
                    Debug.LogWarning("[KioskApi] GET " + path + " failed: " + request.responseCode + " " + request.error);
                }
                onDone(ok, ok ? request.downloadHandler.text : string.Empty);
            }
        }

        private IEnumerator PostSubmission(PromiseSubmission submission, Action<DeliveryStatus, SubmitResponse> onDone) {
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(submission));
            using (UnityWebRequest request = new UnityWebRequest(BaseUrl + PromisesPath, UnityWebRequest.kHttpVerbPOST)) {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = config.RequestTimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Idempotency-Key", submission.requestId);
                yield return request.SendWebRequest();

                long code = request.responseCode;
                if (request.result == UnityWebRequest.Result.Success || code == 409) {
                    // 409 = the server already has this requestId: the leaf is safe, use the stored result.
                    SubmitResponse response = Parse<SubmitResponse>(request.downloadHandler.text);
                    if (response != null && !string.IsNullOrEmpty(response.status)) {
                        onDone(DeliveryStatus.Delivered, response);
                    } else {
                        Debug.LogWarning("[KioskApi] Submit returned an unreadable response (" + code + ").");
                        onDone(DeliveryStatus.Unreachable, null);
                    }
                    yield break;
                }

                if (request.result == UnityWebRequest.Result.ConnectionError) {
                    StartDiscovery();
                }
                bool retryable = request.result == UnityWebRequest.Result.ConnectionError || code == 0 || code == 408 || code == 429 || code >= 500;
                Debug.LogWarning("[KioskApi] Submit failed: " + code + " " + request.error + (retryable ? " (will retry)" : " (not retryable)"));
                onDone(retryable ? DeliveryStatus.Unreachable : DeliveryStatus.PermanentFailure, null);
            }
        }

        private static T Parse<T>(string json) where T : class {
            if (string.IsNullOrEmpty(json)) {
                return null;
            }
            try {
                return JsonUtility.FromJson<T>(json);
            } catch (ArgumentException exception) {
                Debug.LogWarning("[KioskApi] Could not parse " + typeof(T).Name + ": " + exception.Message);
                return null;
            }
        }
    }
}
