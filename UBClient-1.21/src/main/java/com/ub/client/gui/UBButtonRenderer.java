package com.ub.client.gui;

import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;

public final class UBButtonRenderer {
    private UBButtonRenderer() {
    }

    public static void render(GuiGraphics context, Button button, Font font) {
        String label = button.getMessage().getString();
        int color = buttonColor(label);
        if (button.isHovered()) color = lighten(color, 34);
        int x = button.getX();
        int y = button.getY();
        int right = x + button.getWidth();
        int bottom = y + button.getHeight();
        context.fill(x - 1, y - 1, right + 1, bottom + 1, 0xAA02080F);
        context.fill(x, y, right, bottom, 0xF20A1C2A);
        context.fill(x, y, right, y + 2, color | 0xFF000000);
        context.fill(x, bottom - 2, right, bottom, color | 0xFF000000);
        context.fill(x, y + 2, x + 2, bottom - 2, color | 0xFF000000);
        context.fill(right - 2, y + 2, right, bottom - 2, color | 0xFF000000);
        int textColor = button.isHovered() ? 0xFFFFFFFF : 0xFFE7F7FF;
        int textX = x + (button.getWidth() - font.width(button.getMessage())) / 2;
        int textY = y + (button.getHeight() - 8) / 2;
        context.drawString(font, button.getMessage(), textX, textY, textColor, true);
    }

    private static int buttonColor(String label) {
        String value = label.toLowerCase(java.util.Locale.ROOT);
        if (value.contains("single")) return 0xFF35C6F4;
        if (value.contains("multi")) return 0xFF3D82FF;
        if (value.contains("realm")) return 0xFF9A6CFF;
        if (value.contains("mod")) return 0xFFE36BFF;
        if (value.contains("option")) return 0xFF4DD9C0;
        if (value.contains("quit")) return 0xFFFF647C;
        if (value.contains("save") || value.contains("back")) return 0xFF35C6F4;
        if (value.contains("performance")) return 0xFFFFB84D;
        if (value.contains("keystroke")) return 0xFFC77DFF;
        if (value.contains("hud")) return 0xFF45E0B5;
        if (value.contains("custom")) return 0xFF5EA7FF;
        return 0xFF8BD8FF;
    }

    private static int lighten(int color, int amount) {
        int red = Math.min(255, ((color >> 16) & 255) + amount);
        int green = Math.min(255, ((color >> 8) & 255) + amount);
        int blue = Math.min(255, (color & 255) + amount);
        return (red << 16) | (green << 8) | blue;
    }
}
