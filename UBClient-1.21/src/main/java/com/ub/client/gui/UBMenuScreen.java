package com.ub.client.gui;

import com.ub.client.UB21Settings;
import com.ub.client.mixin.TitleScreenAccessor;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Renderable;
import net.minecraft.network.chat.Component;

public final class UBMenuScreen extends Screen {
    private final Screen parent;
    private int left;
    private int top;

    public UBMenuScreen(Screen parent) {
        super(Component.literal("UB Client Menu"));
        this.parent = parent;
    }

    @Override
    protected void init() {
        left = width / 2 - 115;
        top = height / 2 - 70;
        addRenderableWidget(toggle("Custom UI", () -> UB21Settings.customUi, value -> UB21Settings.customUi = value, 0));
        addRenderableWidget(toggle("Modern HUD", () -> UB21Settings.modernHud, value -> UB21Settings.modernHud = value, 1));
        addRenderableWidget(toggle("Keystrokes", () -> UB21Settings.keystrokes, value -> UB21Settings.keystrokes = value, 2));
        addRenderableWidget(toggle("Performance Mode", () -> UB21Settings.performanceMode, value -> UB21Settings.performanceMode = value, 3));
        addRenderableWidget(Button.builder(Component.literal("Save & Back"), button -> {
            UB21Settings.save();
            if (minecraft != null) minecraft.setScreen(parent);
        }).bounds(left, top + 128, 230, 20).build());
    }

    private Button toggle(String name, java.util.function.BooleanSupplier getter,
                                java.util.function.Consumer<Boolean> setter, int row) {
        return Button.builder(Component.literal(label(name, getter.getAsBoolean())), button -> {
            setter.accept(!getter.getAsBoolean());
            button.setMessage(Component.literal(label(name, getter.getAsBoolean())));
        }).bounds(left, top + row * 27, 230, 20).build();
    }

    private static String label(String name, boolean enabled) {
        return name + ": " + (enabled ? "ON" : "OFF");
    }

    @Override
    public void render(GuiGraphics context, int mouseX, int mouseY, float delta) {
        // Do not call Screen.renderBackground here: shader/UI mods may already
        // blur the frame and Minecraft allows only one blur pass per frame.
        context.fill(0, 0, width, height, 0xE607121D);
        context.fill(left - 18, top - 30, left + 248, top + 168, 0xE6091B2A);
        context.fill(left - 18, top - 30, left + 248, top - 28, 0xFF65C7FF);
        context.drawString(font, "PERSONALIZE YOUR CLIENT", left - 2, top - 47, 0xFF79BFE0, true);
        context.drawString(font, "Toggle features safely at any time", left - 2, top - 34, 0xFF7098AA, false);
        int titleWidth = font.width(title);
        context.drawString(font, title, width / 2 - titleWidth / 2, top - 19, 0xFFB9E9FF, true);
        super.render(context, mouseX, mouseY, delta);
        for (Renderable renderable : ((TitleScreenAccessor) (Object) this).ubclient$getRenderables()) {
            if (renderable instanceof Button button) UBButtonRenderer.render(context, button, font);
        }
    }

    public boolean shouldPause() {
        return false;
    }
}
