package com.ub.client.gui;

import com.ub.client.UBClientSettings;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;

public final class UBSettingsScreen extends Screen {
    private final Screen parent;
    private int left;
    private int top;

    public UBSettingsScreen(Screen parent) {
        super(Text.literal("UB Client Settings"));
        this.parent = parent;
    }

    @Override
    protected void init() {
        left = this.width / 2 - 115;
        top = this.height / 2 - 92;
        addDrawableChild(toggleButton("HUD", () -> UBClientSettings.hudEnabled,
                value -> UBClientSettings.hudEnabled = value, 0));
        addDrawableChild(toggleButton("Keystrokes", () -> UBClientSettings.keystrokesEnabled,
                value -> UBClientSettings.keystrokesEnabled = value, 1));
        addDrawableChild(toggleButton("Coordinates", () -> UBClientSettings.coordinatesEnabled,
                value -> UBClientSettings.coordinatesEnabled = value, 2));
        addDrawableChild(toggleButton("Zoom (C)", () -> UBClientSettings.zoomEnabled,
                value -> UBClientSettings.zoomEnabled = value, 3));
        addDrawableChild(toggleButton("Toggle Sprint (R)", () -> UBClientSettings.toggleSprintEnabled,
                value -> UBClientSettings.toggleSprintEnabled = value, 4));
        addDrawableChild(toggleButton("Fullbright (G)", () -> UBClientSettings.fullbrightEnabled,
                value -> UBClientSettings.fullbrightEnabled = value, 5));
        addDrawableChild(ButtonWidget.builder(Text.literal("Save & Back"), button -> {
            UBClientSettings.save();
            if (client != null) client.setScreen(parent);
        }).dimensions(left, top + 178, 230, 20).build());
    }

    private ButtonWidget toggleButton(String name, java.util.function.BooleanSupplier getter,
                                      java.util.function.Consumer<Boolean> setter, int row) {
        var button = ButtonWidget.builder(Text.literal(label(name, getter.getAsBoolean())), ignored -> {
            setter.accept(!getter.getAsBoolean());
            ignored.setMessage(Text.literal(label(name, getter.getAsBoolean())));
        }).dimensions(left, top + row * 28, 230, 20).build();
        return button;
    }

    private static String label(String name, boolean enabled) {
        return name + ": " + (enabled ? "ON" : "OFF");
    }

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float delta) {
        renderBackground(context, mouseX, mouseY, delta);
        context.fill(left - 18, top - 28, left + 248, top + 218, 0xE6091B2A);
        context.fill(left - 18, top - 28, left + 248, top - 26, 0xFF65C7FF);
        var titleWidth = textRenderer.getWidth(title);
        context.drawTextWithShadow(textRenderer, title, width / 2 - titleWidth / 2, top - 18, 0xFFB9E9FF);
        super.render(context, mouseX, mouseY, delta);
    }

    @Override
    public boolean shouldPause() {
        return false;
    }
}
