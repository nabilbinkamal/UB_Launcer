package com.ub.client.hud;

import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import com.ub.client.UBClientSettings;

public class UBHudOverlay implements HudRenderCallback {
    @Override
    public void onHudRender(DrawContext drawContext, float tickDelta) {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client.options.hudHidden || !UBClientSettings.hudEnabled) {
            return;
        }

        int x = 10;
        int y = 10;
        int panelWidth = 132;
        int panelHeight = client.player == null ? 42 : 58;

        // Modern light-blue UB Client panel.
        drawContext.fill(x - 6, y - 6, x + panelWidth, y + panelHeight, 0xCC071521);
        drawContext.fill(x - 6, y - 6, x + panelWidth, y - 4, 0xFF65C7FF);
        drawContext.drawTextWithShadow(client.textRenderer, "UB CLIENT", x, y, 0xFFB9E9FF);

        int fps = client.getCurrentFps();
        drawContext.drawTextWithShadow(client.textRenderer, "FPS  " + fps, x, y + 12, 0xFFE8F8FF);
        
        // Draw Coordinates if in game
        if (client.player != null && UBClientSettings.coordinatesEnabled) {
            int blockX = client.player.getBlockX();
            int blockY = client.player.getBlockY();
            int blockZ = client.player.getBlockZ();
            drawContext.drawTextWithShadow(client.textRenderer, String.format("XYZ  %d  %d  %d", blockX, blockY, blockZ), x, y + 24, 0xFFA9DFFF);
        }

        // Draw Feature Status
        int statusY = y + 38;
        if (com.ub.client.features.ToggleSprintFeature.isSprintToggled) {
            drawContext.drawTextWithShadow(client.textRenderer, "SPRINT  ON", x, statusY, 0xFF72D5FF);
            statusY += 12;
        }
        if (com.ub.client.features.FullbrightFeature.isFullbright) {
            drawContext.drawTextWithShadow(client.textRenderer, "FULLBRIGHT  ON", x, statusY, 0xFFBDEBFF);
        }

        // Draw Keystrokes
        if (UBClientSettings.keystrokesEnabled) drawKeystrokes(drawContext, client);
    }

    private void drawKeystrokes(DrawContext drawContext, MinecraftClient client) {
        int width = client.getWindow().getScaledWidth();
        int height = client.getWindow().getScaledHeight();
        
        int startX = width - 86;
        int startY = height - 88;

        // W
        drawKey(drawContext, client, client.options.forwardKey, startX + 22, startY, 20, 20, "W");
        // A
        drawKey(drawContext, client, client.options.leftKey, startX, startY + 22, 20, 20, "A");
        // S
        drawKey(drawContext, client, client.options.backKey, startX + 22, startY + 22, 20, 20, "S");
        // D
        drawKey(drawContext, client, client.options.rightKey, startX + 44, startY + 22, 20, 20, "D");
        // Space
        drawKey(drawContext, client, client.options.jumpKey, startX, startY + 44, 64, 16, "SPACE");
    }

    private void drawKey(DrawContext context, MinecraftClient client, net.minecraft.client.option.KeyBinding key, int x, int y, int w, int h, String label) {
        boolean pressed = key.isPressed();
        int bgColor = pressed ? 0xFF55B9EA : 0xCC0B2030;
        int borderColor = pressed ? 0xFFB9EEFF : 0xFF397B9D;
        int textColor = pressed ? 0xFF04131D : 0xFFE5F8FF;
        
        context.fill(x, y, x + w, y + h, bgColor);
        context.fill(x, y, x + w, y + 1, borderColor);
        context.fill(x, y + h - 1, x + w, y + h, borderColor);
        
        int textWidth = client.textRenderer.getWidth(label);
        int textX = x + (w - textWidth) / 2;
        int textY = y + (h - client.textRenderer.fontHeight) / 2 + 1;
        context.drawText(client.textRenderer, label, textX, textY, textColor, false);
    }
}
