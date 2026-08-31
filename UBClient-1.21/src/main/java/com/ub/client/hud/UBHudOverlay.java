package com.ub.client.hud;

import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;

public class UBHudOverlay implements HudRenderCallback {
    @Override
    public void onHudRender(DrawContext drawContext, float tickDelta) {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client.options.hudHidden) {
            return;
        }

        int x = 10;
        int y = 10;
        
        // Draw UB Client Branding
        drawContext.drawTextWithShadow(client.textRenderer, "UB Client", x, y, 0x68DDA8);
        
        // Draw FPS
        int fps = client.getCurrentFps();
        drawContext.drawTextWithShadow(client.textRenderer, "FPS: " + fps, x, y + 12, 0xFFFFFF);
        
        // Draw Coordinates if in game
        if (client.player != null) {
            int blockX = client.player.getBlockX();
            int blockY = client.player.getBlockY();
            int blockZ = client.player.getBlockZ();
            drawContext.drawTextWithShadow(client.textRenderer, String.format("XYZ: %d / %d / %d", blockX, blockY, blockZ), x, y + 24, 0xAAAAAA);
        }

        // Draw Feature Status
        int statusY = y + 38;
        if (com.ub.client.features.ToggleSprintFeature.isSprintToggled) {
            drawContext.drawTextWithShadow(client.textRenderer, "[Sprinting (Toggled)]", x, statusY, 0x68DDA8);
            statusY += 12;
        }
        if (com.ub.client.features.FullbrightFeature.isFullbright) {
            drawContext.drawTextWithShadow(client.textRenderer, "[Fullbright ON]", x, statusY, 0xF3C969);
        }

        // Draw Keystrokes
        drawKeystrokes(drawContext, client);
    }

    private void drawKeystrokes(DrawContext drawContext, MinecraftClient client) {
        int width = client.getWindow().getScaledWidth();
        int height = client.getWindow().getScaledHeight();
        
        int startX = width - 80;
        int startY = height - 80;

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
        int bgColor = pressed ? 0x88FFFFFF : 0x44000000;
        int textColor = pressed ? 0x000000 : 0xFFFFFF;

        context.fill(x, y, x + w, y + h, bgColor);
        
        int textWidth = client.textRenderer.getWidth(label);
        int textX = x + (w - textWidth) / 2;
        int textY = y + (h - client.textRenderer.fontHeight) / 2 + 1;
        context.drawText(client.textRenderer, label, textX, textY, textColor, false);
    }
}
