package com.viitorcloud.gooddeedtree.kiosk;

import android.app.Activity;
import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.os.SystemClock;

/** Full process restart for the operator panel: schedules a relaunch, then ends the process. */
public final class KioskRestarter {
    private KioskRestarter() {
    }

    public static void restart(Activity activity) {
        Context context = activity.getApplicationContext();
        Intent launch = context.getPackageManager().getLaunchIntentForPackage(context.getPackageName());
        if (launch == null) {
            throw new IllegalStateException("No launch intent");
        }
        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        PendingIntent pending = PendingIntent.getActivity(context, 4313, launch,
                PendingIntent.FLAG_CANCEL_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        AlarmManager alarms = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        alarms.set(AlarmManager.ELAPSED_REALTIME, SystemClock.elapsedRealtime() + 600, pending);
        try {
            activity.stopLockTask();
        } catch (RuntimeException ignored) {
            // not in lock task
        }
        activity.finishAffinity();
        android.os.Process.killProcess(android.os.Process.myPid());
    }
}
