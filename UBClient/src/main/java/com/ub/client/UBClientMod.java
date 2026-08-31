package com.ub.client;

import com.ub.client.hud.UBHudOverlay;
import com.ub.client.gui.UBSettingsScreen;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;
import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class UBClientMod implements ClientModInitializer {
    public static final String MOD_ID = "ubclient";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);
    private static KeyBinding settingsKey;

    @Override
    public void onInitializeClient() {
        LOGGER.info("Initializing UB Custom Client...");
        
        // Security Verification (Anti-Leak DRM)
        // This token is dynamically injected by the UB Launcher C# backend as an environment variable
        String authToken = System.getenv("UB_LAUNCHER_AUTH_TOKEN");
        
        if (authToken == null || authToken.trim().isEmpty()) {
            LOGGER.error("[UB Client Security] Authentication failed! The mod was launched outside of the official UB Launcher.");
            LOGGER.error("[UB Client Security] Disabling all custom features.");
            return; // Exit early, preventing HUD and features from loading
        }
        
        LOGGER.info("[UB Client Security] Authentication successful.");
        UBClientSettings.load(MinecraftClient.getInstance().runDirectory.toPath());
        settingsKey = KeyBindingHelper.registerKeyBinding(new KeyBinding(
                "key.ubclient.settings", InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_H,
                "category.ubclient.features"));
        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            while (settingsKey.wasPressed()) client.setScreen(new UBSettingsScreen(client.currentScreen));
        });
        
        // Register Features
        com.ub.client.features.ZoomFeature.register();
        com.ub.client.features.ToggleSprintFeature.register();
        com.ub.client.features.FullbrightFeature.register();
        
        // Register HUD Overlay (Only if authenticated)
        HudRenderCallback.EVENT.register(new com.ub.client.hud.UBHudOverlay());
    }
}
