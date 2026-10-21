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
        private const string RecentPath = "/api/v1/recent";

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
        private int healthState = -1;   // -1 unknown, 0 down, 1 up: health is logged only when it changes

        private const string Tag = "[KioskNet] ";

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
            string cached = PlayerPrefs.GetString(UrlPrefKey, string.Empty);
            Debug.Log(Tag + "Start. configured=" + config.ServerBaseUrl
                + " cached=" + (cached.Length > 0 ? cached : "(none)")
                + " using=" + BaseUrl
                + " timeout=" + config.RequestTimeoutSeconds + "s");
            Debug.Log(Tag + "Device IPs=" + LocalAddresses() + " reachability=" + Application.internetReachability);
        }

        /// <summary>Forget the cached discovered URL and go back to the configured one.</summary>
        public void ResetToConfiguredUrl() {
            PlayerPrefs.DeleteKey(UrlPrefKey);
            PlayerPrefs.Save();
            baseUrl = null;
            Debug.Log(Tag + "Cached URL cleared, using " + BaseUrl);
        }

        private static string LocalAddresses() {
            try {
                StringBuilder sb = new StringBuilder();
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName())) {
                    if (address.AddressFamily == AddressFamily.InterNetwork) {
                        sb.Append(address).Append(' ');
                    }
                }
                return sb.Length > 0 ? sb.ToString().Trim() : "(none found)";
            } catch (Exception exception) {
                return "(lookup failed: " + exception.GetType().Name + " " + exception.Message + ")";
            }
        }

        private static string Describe(UnityWebRequest request, long ms) {
            return "result=" + request.result + " code=" + request.responseCode
                + " error=\"" + request.error + "\" url=" + request.url + " after " + ms + "ms";
        }

        private void Update() {
            string found = discoveredUrl;
            if (found != null) {
                discoveredUrl = null;
                if (found != baseUrl) {
                    Debug.Log(Tag + "Discovery: switching from " + baseUrl + " to " + found);
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
            Debug.Log(Tag + "Discovery: broadcasting " + DiscoverRequest + " to 255.255.255.255:" + DiscoveryPort
                + " (device IPs " + LocalAddresses() + ")");
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    using (UdpClient udp = new UdpClient()) {
                        udp.EnableBroadcast = true;
                        udp.Client.ReceiveTimeout = 1500;
                        byte[] request = Encoding.UTF8.GetBytes(DiscoverRequest);
                        udp.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        string reply = Encoding.UTF8.GetString(udp.Receive(ref from)).Trim();
                        Debug.Log(Tag + "Discovery: reply \"" + reply + "\" from " + from);
                        string[] parts = reply.Split(' ');
                        int wallPort;
                        if (parts.Length == 2 && parts[0] == DiscoverReply && int.TryParse(parts[1], out wallPort)) {
                            discoveredUrl = "http://" + from.Address + ":" + wallPort;
                        } else {
                            Debug.LogWarning(Tag + "Discovery: unexpected reply, ignored.");
                        }
                    }
                } catch (SocketException exception) {
                    Debug.LogWarning(Tag + "Discovery failed: socket " + exception.SocketErrorCode
                        + (exception.SocketErrorCode == SocketError.TimedOut ? " (no wall answered within 1.5 s)" : "")
                        + " - " + exception.Message);
                } catch (Exception exception) {
                    Debug.LogError(Tag + "Discovery error: " + exception.GetType().Name + " - " + exception.Message);
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

        /// <summary> Latest planted deeds, newest first, for the idle cards. </summary>
        public void FetchRecent(Action<bool, List<RecentDeed>> onDone) {
            StartCoroutine(GetJson(RecentPath, (bool ok, string body) => {
                List<RecentDeed> list = new List<RecentDeed>();
                if (ok) {
                    RecentResponse response = Parse<RecentResponse>(body);
                    if (response != null && response.deeds != null) {
                        foreach (RecentDeed deed in response.deeds) {
                            if (deed != null && !string.IsNullOrWhiteSpace(deed.promise)) {
                                list.Add(deed);
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
                int state = ok ? 1 : 0;
                if (state != healthState) {
                    healthState = state;
                    if (ok) {
                        Debug.Log(Tag + "Wall reachable at " + BaseUrl + " (" + watch.ElapsedMilliseconds + "ms)");
                    } else {
                        Debug.LogWarning(Tag + "Wall NOT reachable at " + BaseUrl);
                    }
                }
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
                bool quiet = path == HealthPath;   // polled every few seconds
                if (!quiet) {
                    Debug.Log(Tag + "GET " + request.url);
                }
                Stopwatch watch = Stopwatch.StartNew();
                yield return request.SendWebRequest();
                watch.Stop();
                bool ok = request.result == UnityWebRequest.Result.Success;
                if (request.result == UnityWebRequest.Result.ConnectionError) {
                    StartDiscovery();
                }
                if (!ok) {
                    // Health failures log their details only on the first failure after being up.
                    if (!quiet || healthState != 0) {
                        Debug.LogWarning(Tag + "GET " + path + " FAILED " + Describe(request, watch.ElapsedMilliseconds));
                    }
                } else if (!quiet) {
                    Debug.Log(Tag + "GET " + path + " ok code=" + request.responseCode + " " + watch.ElapsedMilliseconds + "ms");
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
                string shortId = submission.requestId != null && submission.requestId.Length > 8 ? submission.requestId.Substring(0, 8) : submission.requestId;
                Debug.Log(Tag + "POST " + request.url + " request " + shortId + " (" + body.Length + " bytes)");
                Stopwatch watch = Stopwatch.StartNew();
                yield return request.SendWebRequest();
                watch.Stop();

                long code = request.responseCode;
                if (request.result == UnityWebRequest.Result.Success || code == 409) {
                    // 409 = the server already has this requestId: the leaf is safe, use the stored result.
                    SubmitResponse response = Parse<SubmitResponse>(request.downloadHandler.text);
                    if (response != null && !string.IsNullOrEmpty(response.status)) {
                        Debug.Log(Tag + "POST " + shortId + " delivered: status=" + response.status + " leaf=" + response.leafNumber
                            + " queue=" + response.queuePosition + " code=" + code + " " + watch.ElapsedMilliseconds + "ms");
                        onDone(DeliveryStatus.Delivered, response);
                    } else {
                        Debug.LogWarning(Tag + "POST " + shortId + " unreadable response, " + Describe(request, watch.ElapsedMilliseconds));
                        onDone(DeliveryStatus.Unreachable, null);
                    }
                    yield break;
                }

                if (request.result == UnityWebRequest.Result.ConnectionError) {
                    StartDiscovery();
                }
                bool retryable = request.result == UnityWebRequest.Result.ConnectionError || code == 0 || code == 408 || code == 429 || code >= 500;
                Debug.LogWarning(Tag + "POST " + shortId + " FAILED " + Describe(request, watch.ElapsedMilliseconds)
                    + (retryable ? " -> queued, will retry" : " -> not retryable"));
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
                Debug.LogWarning(Tag + "Could not parse " + typeof(T).Name + ": " + exception.Message);
                return null;
            }
        }
    }
}
