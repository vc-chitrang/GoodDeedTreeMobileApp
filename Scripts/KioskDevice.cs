using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// Device-level kiosk behaviour: portrait, full screen, never sleep, Android lock task
    /// (screen pinning) so visitors cannot leave the app. Auto-start after reboot is handled by
    /// the KioskBootReceiver in Plugins/Android.
    /// </summary>
    public static class KioskDevice {
        public static void Apply() {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.fullScreen = true;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            TouchScreenKeyboard.hideInput = true;
            SetLockTask(true);
        }

        public static void ExitKiosk() {
            SetLockTask(false);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public static void RestartApp() {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (RestartAndroidProcess()) {
                return;
            }
#endif
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private static void SetLockTask(bool locked) {
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                // Not disposed here: the runnable uses it later on the UI thread.
                AndroidJavaObject activity = CurrentActivity();
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() => {
                    activity.Call(locked ? "startLockTask" : "stopLockTask");
                }));
            } catch (AndroidJavaException exception) {
                Debug.LogError("[KioskDevice] Lock task " + (locked ? "start" : "stop") + " failed: " + exception.Message);
            }
#else
            Debug.Log("[KioskDevice] Lock task " + (locked ? "on" : "off") + " (only active on Android devices).");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject CurrentActivity() {
            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) {
                return player.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }

        private static bool RestartAndroidProcess() {
            try {
                using (AndroidJavaClass restarter = new AndroidJavaClass("com.viitorcloud.gooddeedtree.kiosk.KioskRestarter")) {
                    using (AndroidJavaObject activity = CurrentActivity()) {
                        restarter.CallStatic("restart", activity);
                    }
                }
                return true;
            } catch (AndroidJavaException exception) {
                Debug.LogError("[KioskDevice] Restart failed, reloading scene instead: " + exception.Message);
                return false;
            }
        }
#endif
    }
}
