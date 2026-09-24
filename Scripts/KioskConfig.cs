using System.Collections.Generic;

using UnityEngine;

namespace GoodDeedTreeMobileApp {
    [CreateAssetMenu(menuName = "GoodDeedTree/Kiosk Config", fileName = "KioskConfig")]
    public class KioskConfig : ScriptableObject {
        [Header("Server")]
        [Tooltip("Base URL of the wall server on the local network, without a trailing slash.")]
        [SerializeField] private string serverBaseUrl = "http://192.168.1.50:8080";
        [SerializeField] private int requestTimeoutSeconds = 6;

        [Header("Input")]
        [SerializeField] private int nameMaxLength = 15;
        [SerializeField] private int promiseMaxLength = 60;
        [SerializeField] private List<string> defaultSuggestions = new List<string> {
            "Call my parents",
            "Help a stranger",
            "Feed an animal",
            "Thank someone today"
        };

        [Header("Timing (seconds)")]
        [SerializeField] private float idleResetSeconds = 60f;
        [SerializeField] private float resultHoldSeconds = 4.5f;
        [SerializeField] private float rejectedHoldSeconds = 3.5f;
        [SerializeField] private float thankYouSeconds = 8f;
        [SerializeField] private float countRefreshSeconds = 60f;

        [Header("Offline retry (seconds)")]
        [SerializeField] private float retryMinSeconds = 5f;
        [SerializeField] private float retryMaxSeconds = 120f;

        [Header("Operator")]
        [SerializeField] private int operatorTapCount = 5;
        [SerializeField] private float operatorTapWindowSeconds = 3f;
        [Tooltip("SHA-256 (hex, lowercase) of the operator PIN. Default is the hash of 2468 - change it before deploying.")]
        [SerializeField] private string operatorPinSha256 = "a1fb4e703a9ef1fa4936801721ff285a97ac85330856674412e054892afe6972";
        [SerializeField] private int operatorMaxAttempts = 5;
        [SerializeField] private float operatorLockoutSeconds = 30f;

        public string ServerBaseUrl {
            get { return serverBaseUrl.TrimEnd('/'); }
        }

        public int RequestTimeoutSeconds {
            get { return requestTimeoutSeconds; }
        }

        public int NameMaxLength {
            get { return nameMaxLength; }
        }

        public int PromiseMaxLength {
            get { return promiseMaxLength; }
        }

        public IReadOnlyList<string> DefaultSuggestions {
            get { return defaultSuggestions; }
        }

        public float IdleResetSeconds {
            get { return idleResetSeconds; }
        }

        public float ResultHoldSeconds {
            get { return resultHoldSeconds; }
        }

        public float RejectedHoldSeconds {
            get { return rejectedHoldSeconds; }
        }

        public float ThankYouSeconds {
            get { return thankYouSeconds; }
        }

        public float CountRefreshSeconds {
            get { return countRefreshSeconds; }
        }

        public float RetryMinSeconds {
            get { return retryMinSeconds; }
        }

        public float RetryMaxSeconds {
            get { return retryMaxSeconds; }
        }

        public int OperatorTapCount {
            get { return operatorTapCount; }
        }

        public float OperatorTapWindowSeconds {
            get { return operatorTapWindowSeconds; }
        }

        public string OperatorPinSha256 {
            get { return operatorPinSha256; }
        }

        public int OperatorMaxAttempts {
            get { return operatorMaxAttempts; }
        }

        public float OperatorLockoutSeconds {
            get { return operatorLockoutSeconds; }
        }
    }
}
