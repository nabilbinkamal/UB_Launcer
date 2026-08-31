package com.ub.client;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

public final class UBClientSettings {
    public static boolean hudEnabled = true;
    public static boolean keystrokesEnabled = true;
    public static boolean coordinatesEnabled = true;
    public static boolean zoomEnabled = true;
    public static boolean toggleSprintEnabled = true;
    public static boolean fullbrightEnabled = true;

    private static Path configPath;

    private UBClientSettings() {}

    public static void load(Path runDirectory) {
        configPath = runDirectory.resolve("config").resolve("ubclient.properties");
        if (!Files.exists(configPath)) return;
        var properties = new Properties();
        try (InputStream input = Files.newInputStream(configPath)) {
            properties.load(input);
            hudEnabled = read(properties, "hud", hudEnabled);
            keystrokesEnabled = read(properties, "keystrokes", keystrokesEnabled);
            coordinatesEnabled = read(properties, "coordinates", coordinatesEnabled);
            zoomEnabled = read(properties, "zoom", zoomEnabled);
            toggleSprintEnabled = read(properties, "toggle_sprint", toggleSprintEnabled);
            fullbrightEnabled = read(properties, "fullbright", fullbrightEnabled);
        } catch (IOException ignored) {
        }
    }

    public static void save() {
        if (configPath == null) return;
        var properties = new Properties();
        properties.setProperty("hud", Boolean.toString(hudEnabled));
        properties.setProperty("keystrokes", Boolean.toString(keystrokesEnabled));
        properties.setProperty("coordinates", Boolean.toString(coordinatesEnabled));
        properties.setProperty("zoom", Boolean.toString(zoomEnabled));
        properties.setProperty("toggle_sprint", Boolean.toString(toggleSprintEnabled));
        properties.setProperty("fullbright", Boolean.toString(fullbrightEnabled));
        try {
            Files.createDirectories(configPath.getParent());
            try (OutputStream output = Files.newOutputStream(configPath)) {
                properties.store(output, "UB Client settings");
            }
        } catch (IOException ignored) {
        }
    }

    private static boolean read(Properties properties, String key, boolean fallback) {
        return Boolean.parseBoolean(properties.getProperty(key, Boolean.toString(fallback)));
    }
}
