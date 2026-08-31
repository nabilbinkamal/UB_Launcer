package com.ub.client;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

public final class UB21Settings {
    public static boolean customUi = true;
    public static boolean modernHud = true;
    public static boolean keystrokes = true;
    public static boolean performanceMode = true;
    private static Path path;

    private UB21Settings() {}

    public static void load(Path runDirectory) {
        path = runDirectory.resolve("config").resolve("ubclient.properties");
        if (!Files.exists(path)) return;
        var properties = new Properties();
        try (InputStream input = Files.newInputStream(path)) {
            properties.load(input);
            customUi = read(properties, "custom_ui", customUi);
            modernHud = read(properties, "modern_hud", modernHud);
            keystrokes = read(properties, "keystrokes", keystrokes);
            performanceMode = read(properties, "performance_mode", performanceMode);
        } catch (IOException ignored) {}
    }

    public static void save() {
        if (path == null) return;
        var properties = new Properties();
        properties.setProperty("custom_ui", Boolean.toString(customUi));
        properties.setProperty("modern_hud", Boolean.toString(modernHud));
        properties.setProperty("keystrokes", Boolean.toString(keystrokes));
        properties.setProperty("performance_mode", Boolean.toString(performanceMode));
        try {
            Files.createDirectories(path.getParent());
            try (OutputStream output = Files.newOutputStream(path)) {
                properties.store(output, "UB Client 1.21 settings");
            }
        } catch (IOException ignored) {}
    }

    private static boolean read(Properties properties, String key, boolean fallback) {
        return Boolean.parseBoolean(properties.getProperty(key, Boolean.toString(fallback)));
    }
}
