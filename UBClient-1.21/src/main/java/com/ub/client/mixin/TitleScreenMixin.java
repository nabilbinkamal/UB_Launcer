package com.ub.client.mixin;

import com.ub.client.UB21Settings;
import com.ub.client.gui.UBMenuScreen;
import com.ub.client.gui.UBButtonRenderer;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Renderable;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TitleScreen.class)
public final class TitleScreenMixin {
    @Inject(method = "init", at = @At("TAIL"))
    private void ubclient$addMenuButton(CallbackInfo callbackInfo) {
        TitleScreen screen = (TitleScreen) (Object) this;
        for (Renderable renderable : ((TitleScreenAccessor) screen).ubclient$getRenderables()) {
            if (renderable instanceof Button button) positionMinecraftButton(button, screen.width, screen.height);
        }
        ((TitleScreenAccessor) screen).ubclient$addRenderableWidget(Button.builder(Component.literal("UB MENU"), button ->
                Minecraft.getInstance().setScreen(new UBMenuScreen(screen)))
            .bounds(10, 10, 90, 20)
            .build());
    }

    private static void positionMinecraftButton(Button button, int width, int height) {
        String label = button.getMessage().getString().toLowerCase(java.util.Locale.ROOT);
        int center = width / 2;
        int y = Math.max(150, height / 2 - 90);
        if (label.contains("single")) button.setPosition(center - 112, y);
        else if (label.contains("multi")) button.setPosition(center - 112, y + 34);
        else if (label.contains("realm")) button.setPosition(center - 112, y + 68);
        else if (label.equals("mods")) button.setPosition(center + 5, y + 68);
        else if (label.contains("option")) button.setPosition(center - 112, y + 108);
        else if (label.contains("quit")) button.setPosition(center + 5, y + 108);
        else if (label.contains("language") || label.contains("accessibility")) button.setPosition(-100, -100);
    }

    @Inject(method = "render", at = @At("TAIL"))
    private void ubclient$renderBranding(GuiGraphics context, int mouseX, int mouseY, float delta, CallbackInfo callbackInfo) {
        if (!UB21Settings.customUi) return;
        TitleScreen screen = (TitleScreen) (Object) this;
        int width = screen.width;
        int center = width / 2;
        // Full-screen dark veil keeps the selected background while giving the
        // screen the compact launcher composition shown in the reference.
        context.fill(0, 0, width, screen.height, 0x72030810);
        context.fill(0, 0, width, 62, 0xE607121D);
        context.fill(0, 60, width, 63, 0xFF65C7FF);
        var font = Minecraft.getInstance().font;
        context.drawString(font, "UB CLIENT", 22, 18, 0xFFB9E9FF, true);
        context.drawString(font, "MODERN EDITION  |  1.21+", 22, 36, 0xFFA9DFFF, true);
        context.drawString(font, "CUSTOM UI", width - 78, 18, 0xFF65C7FF, true);
        context.drawString(font, "READY", width - 48, 36, 0xFF55E0B6, true);
        context.drawString(font, "UB CLIENT", center - font.width("UB CLIENT") / 2, 108, 0xFFE5F8FF, true);
        context.drawString(font, "CHOOSE YOUR EXPERIENCE", center - font.width("CHOOSE YOUR EXPERIENCE") / 2, 124, 0xFF79BFE0, false);
        for (Renderable renderable : ((TitleScreenAccessor) (Object) this).ubclient$getRenderables()) {
            if (renderable instanceof Button button) UBButtonRenderer.render(context, button, font);
        }
    }
}
