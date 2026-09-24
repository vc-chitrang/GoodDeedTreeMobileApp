package com.viitorcloud.gooddeedtree.kiosk;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

/** Relaunches the kiosk after a reboot, power cut or app update. */
public class KioskBootReceiver extends BroadcastReceiver {
    private static final String TAG = "GoodDeedKiosk";

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null || intent.getAction() == null) {
            return;
        }
        Intent launch = context.getPackageManager().getLaunchIntentForPackage(context.getPackageName());
        if (launch == null) {
            Log.e(TAG, "No launch intent found for " + context.getPackageName());
            return;
        }
        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        try {
            context.startActivity(launch);
            Log.i(TAG, "Kiosk started after " + intent.getAction());
        } catch (RuntimeException e) {
            Log.e(TAG, "Could not start kiosk after boot: " + e.getMessage());
        }
    }
}
