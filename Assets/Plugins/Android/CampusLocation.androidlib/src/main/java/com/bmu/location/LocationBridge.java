package com.bmu.location;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.os.Bundle;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;

/**
 * Live device location for Unity, polled from C#.
 * Listens to GPS and network providers and keeps the best recent fix.
 */
public class LocationBridge {

    private static final String TAG = "LocationBridge";
    private static final long UPDATE_INTERVAL_MS = 1000;
    private static final long STALE_FIX_MS = 2 * 60 * 1000;

    private static LocationManager manager;
    private static Location best;

    private static final LocationListener listener = new LocationListener() {
        @Override
        public void onLocationChanged(Location location) {
            consider(location);
        }

        // Required on older Android versions.
        @Override public void onStatusChanged(String provider, int status, Bundle extras) { }
        @Override public void onProviderEnabled(String provider) { }
        @Override public void onProviderDisabled(String provider) { }
    };

    /** Starts listening. Returns false if permission is missing or no provider is available. */
    public static synchronized boolean start(final Activity activity) {
        if (activity == null) return false;
        if (activity.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED &&
            activity.checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
            return false;
        }

        manager = (LocationManager) activity.getSystemService(Context.LOCATION_SERVICE);
        if (manager == null) return false;

        boolean started = false;
        for (String provider : new String[] { LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER }) {
            try {
                if (!manager.isProviderEnabled(provider)) continue;
                consider(manager.getLastKnownLocation(provider));
                manager.requestLocationUpdates(provider, UPDATE_INTERVAL_MS, 0f, listener, Looper.getMainLooper());
                started = true;
            } catch (SecurityException | IllegalArgumentException e) {
                Log.w(TAG, "Provider " + provider + " unavailable: " + e.getMessage());
            }
        }
        return started;
    }

    public static synchronized void stop() {
        if (manager != null) {
            try {
                manager.removeUpdates(listener);
            } catch (SecurityException e) {
                Log.w(TAG, "removeUpdates failed: " + e.getMessage());
            }
        }
    }

    /** True when location services (GPS or network) are switched on in system settings. */
    public static boolean isEnabled(Activity activity) {
        LocationManager lm = (LocationManager) activity.getSystemService(Context.LOCATION_SERVICE);
        return lm != null && (lm.isProviderEnabled(LocationManager.GPS_PROVIDER) || lm.isProviderEnabled(LocationManager.NETWORK_PROVIDER));
    }

    public static synchronized boolean hasFix() {
        return best != null && ageMillis(best) < STALE_FIX_MS;
    }

    public static synchronized double getLatitude() { return best != null ? best.getLatitude() : 0; }

    public static synchronized double getLongitude() { return best != null ? best.getLongitude() : 0; }

    /** Horizontal accuracy in metres, or -1 when unknown. */
    public static synchronized float getAccuracy() { return best != null && best.hasAccuracy() ? best.getAccuracy() : -1f; }

    private static synchronized void consider(Location candidate) {
        if (candidate == null) return;
        if (best == null || isBetter(candidate, best)) best = candidate;
    }

    private static boolean isBetter(Location candidate, Location current) {
        long newer = ageMillis(current) - ageMillis(candidate);
        if (newer > 30_000) return true;    // Much newer wins.
        if (newer < -30_000) return false;  // Much older loses.
        float a = candidate.hasAccuracy() ? candidate.getAccuracy() : Float.MAX_VALUE;
        float b = current.hasAccuracy() ? current.getAccuracy() : Float.MAX_VALUE;
        return a <= b || newer > 0;
    }

    private static long ageMillis(Location location) {
        return (SystemClock.elapsedRealtimeNanos() - location.getElapsedRealtimeNanos()) / 1_000_000L;
    }
}
