using System;

namespace GoodDeedTreeMobileApp {
    /// <summary> Payload sent to POST /api/v1/promises. requestId makes retries idempotent. </summary>
    [Serializable]
    public class PromiseSubmission {
        public string requestId;
        public string name;
        public string promise;
        public string suggestion;
        public string createdAtUtc;
    }

    /// <summary> status is "accepted" or "rejected". queuePosition 0 means the leaf plays now. </summary>
    [Serializable]
    public class SubmitResponse {
        public string status;
        public int leafNumber;
        public int queuePosition;
    }

    [Serializable]
    public class StatsResponse {
        public int promiseCount;
    }

    [Serializable]
    public class SuggestionsResponse {
        public string[] suggestions;
    }

    public enum DeliveryStatus {
        Delivered = 0,
        Unreachable = 1,
        PermanentFailure = 2
    }

    public enum SubmitOutcome {
        PlaysNow = 0,
        Queued = 1,
        Rejected = 2,
        SavedOffline = 3
    }

    public class SubmitResult {
        public SubmitOutcome Outcome { get; private set; }
        public int LeafNumber { get; private set; }
        public int QueuePosition { get; private set; }

        public SubmitResult(SubmitOutcome outcome, int leafNumber, int queuePosition) {
            Outcome = outcome;
            LeafNumber = leafNumber;
            QueuePosition = queuePosition;
        }
    }
}
