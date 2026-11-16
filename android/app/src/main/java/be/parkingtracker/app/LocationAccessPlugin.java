package be.parkingtracker.app;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/**
 * Location permission helper for the web app:
 *  - status():            is location allowed "while using" (fine) and "all the time" (background)?
 *  - requestForeground(): the normal "While using the app" request
 *  - requestBackground(): asks for "Allow all the time" (Android 11+ shows the app's location settings page)
 *  - openSettings():      opens this app's settings page
 */
@CapacitorPlugin(name = "LocationAccess")
public class LocationAccessPlugin extends Plugin {

    private static final int REQ_FOREGROUND = 4241;
    private static final int REQ_BACKGROUND = 4242;

    private boolean granted(String permission) {
        return ContextCompat.checkSelfPermission(getContext(), permission) == PackageManager.PERMISSION_GRANTED;
    }

    private boolean backgroundGranted() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return granted(Manifest.permission.ACCESS_FINE_LOCATION);
        return granted(Manifest.permission.ACCESS_BACKGROUND_LOCATION);
    }

    @PluginMethod
    public void status(PluginCall call) {
        JSObject r = new JSObject();
        r.put("foreground", granted(Manifest.permission.ACCESS_FINE_LOCATION) || granted(Manifest.permission.ACCESS_COARSE_LOCATION));
        r.put("precise", granted(Manifest.permission.ACCESS_FINE_LOCATION));
        r.put("background", backgroundGranted());
        call.resolve(r);
    }

    @PluginMethod
    public void requestForeground(PluginCall call) {
        ActivityCompat.requestPermissions(getActivity(),
            new String[] { Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION }, REQ_FOREGROUND);
        call.resolve();
    }

    @PluginMethod
    public void requestBackground(PluginCall call) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && !backgroundGranted()) {
            if (!granted(Manifest.permission.ACCESS_FINE_LOCATION)) {
                // Android only offers "all the time" once "while using the app" is granted.
                openAppSettings();
            } else {
                ActivityCompat.requestPermissions(getActivity(),
                    new String[] { Manifest.permission.ACCESS_BACKGROUND_LOCATION }, REQ_BACKGROUND);
            }
        }
        call.resolve();
    }

    @PluginMethod
    public void openSettings(PluginCall call) {
        openAppSettings();
        call.resolve();
    }

    private void openAppSettings() {
        Intent i = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", getContext().getPackageName(), null));
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        getContext().startActivity(i);
    }
}
