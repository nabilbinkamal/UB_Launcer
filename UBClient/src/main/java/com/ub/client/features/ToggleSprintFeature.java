package com.ub.client.features;

import com.ub.client.UBClientSettings;

import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;

public class ToggleSprintFeature {
    public static KeyBinding toggleSprintKey;
    public static boolean isSprintToggled = false;

    public static void register() {
        toggleSprintKey = KeyBindingHelper.registerKeyBinding(new KeyBinding(
                "key.ubclient.togglesprint", 
                InputUtil.Type.KEYSYM, 
                GLFW.GLFW_KEY_R, 
                "category.ubclient.features"
        ));

        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            if (!UBClientSettings.toggleSprintEnabled) {
                isSprintToggled = false;
                return;
            }
            while (toggleSprintKey.wasPressed()) {
                isSprintToggled = !isSprintToggled;
            }

            if (isSprintToggled && client.player != null) {
                if (client.options.forwardKey.isPressed() && !client.player.isSneaking() && !client.player.horizontalCollision) {
                    client.player.setSprinting(true);
                }
            }
        });
    }
}
