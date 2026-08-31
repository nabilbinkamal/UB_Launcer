package com.ub.client;

import net.fabricmc.api.ClientModInitializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class UBClientMod implements ClientModInitializer {
    public static final String MOD_ID = "ubclient";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);

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
        
        LOGGER.info("UB Client is active for Minecraft 1.19.4.");
    }
}
