package com.ub.client;

import net.fabricmc.api.ClientModInitializer;

public class UBClientMod implements ClientModInitializer {
    public static final String MOD_ID = "ubclient";
    @Override
    public void onInitializeClient() {
        System.out.println("Initializing UB Custom Client for Minecraft 1.8.9...");
        
        // Security Verification (Anti-Leak DRM)
        // This token is dynamically injected by the UB Launcher C# backend as an environment variable
        String authToken = System.getenv("UB_LAUNCHER_AUTH_TOKEN");
        
        if (authToken == null || authToken.trim().isEmpty()) {
            System.err.println("[UB Client Security] Authentication failed!");
            return; // Exit early, preventing HUD and features from loading
        }
        
        System.out.println("[UB Client Security] Authentication successful.");
        
        System.out.println("UB Client is active for Minecraft 1.8.9.");
    }
}
