package com.ub.client.features;

import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;

public class ZoomFeature {
    public static KeyBinding zoomKey;
    public static boolean isZooming = false;
    public static double currentZoom = 1.0;

    public static void register() {
        zoomKey = KeyBindingHelper.registerKeyBinding(new KeyBinding(
                "key.ubclient.zoom", 
                InputUtil.Type.KEYSYM, 
                GLFW.GLFW_KEY_C, 
                "category.ubclient.features"
        ));
    }

    public static boolean isZoomPressed() {
        if (zoomKey != null) {
            isZooming = zoomKey.isPressed();
            return isZooming;
        }
        return false;
    }

    public static double manageZoom(double fov) {
        if (isZoomPressed()) {
            currentZoom = Math.max(0.2, currentZoom - 0.05);
        } else {
            currentZoom = Math.min(1.0, currentZoom + 0.05);
        }
        return fov * currentZoom;
    }
}
