package com.ub.client.mixin;

import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.TitleScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TitleScreen.class)
public class TitleScreenMixin {
    @Inject(method = "render", at = @At("TAIL"))
    private void onRender(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        TitleScreen screen = (TitleScreen) (Object) this;
        int width = screen.width;
        
        // Draw a sleek dark header to cover the vanilla logo
        context.fill(0, 0, width, 80, 0xDD05090C); // Very dark, mostly opaque
        context.fill(0, 80, width, 82, 0xFF68DDA8); // Cyan/Green border strip
        
        // Draw UB Client Custom Logo Text
        context.getMatrices().push();
        context.getMatrices().scale(3.0f, 3.0f, 3.0f);
        int textWidth = net.minecraft.client.MinecraftClient.getInstance().textRenderer.getWidth("UB Client");
        context.drawTextWithShadow(
                net.minecraft.client.MinecraftClient.getInstance().textRenderer, 
                "UB Client", 
                (int)((width / 2f) / 3.0f) - (textWidth / 2), 
                8, 
                0x68DDA8
        );
        context.getMatrices().pop();

        // Draw Subtitle
        int subTextWidth = net.minecraft.client.MinecraftClient.getInstance().textRenderer.getWidth("Premium Edition");
        context.drawTextWithShadow(
                net.minecraft.client.MinecraftClient.getInstance().textRenderer, 
                "Premium Edition", 
                (width / 2) - (subTextWidth / 2), 
                60, 
                0x82C7FF
        );
    }
}
