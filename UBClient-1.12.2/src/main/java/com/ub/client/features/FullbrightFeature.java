package com.ub.client.features;

import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;

public class FullbrightFeature {
    public static KeyBinding fullbrightKey;
    public static boolean isFullbright = false;

    public static void register() {
        fullbrightKey = KeyBindingHelper.registerKeyBinding(new KeyBinding(
                "key.ubclient.fullbright", 
                InputUtil.Type.KEYSYM, 
                GLFW.GLFW_KEY_G, 
                "category.ubclient.features"
        ));

        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            while (fullbrightKey.wasPressed()) {
                isFullbright = !isFullbright;
                
                // Force a lightmap update so the screen brightens instantly
                if (client.worldRenderer != null) {
                    client.worldRenderer.reload();
                }
            }
        });
    }
}
