
using Cairo;
using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace EternalStorm;

public class StormMapLayer : MapLayer
{
    private readonly ICoreClientAPI capi;
    private double cx, cz;

    // offscreen texture for the ring overlay
    private LoadedTexture ringTex;

    public override string Title => "Storm Wall";
    public override string LayerGroupCode => "stormwall";
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
    public override bool RequireChunkLoaded => false;

    public StormMapLayer(ICoreClientAPI capi, IWorldMapManager mapSink) : base(capi, mapSink)
    {
        this.capi = capi;
        ringTex = new LoadedTexture(capi);
    }

    public override void OnLoaded()
    {
        var spawn = capi.World.DefaultSpawnPosition.AsBlockPos;
        cx = spawn.X + 0.5;
        cz = spawn.Z + 0.5;
        ZIndex = 999;
    }

    public override void Render(GuiElementMap mapElem, float dt)
    {
        if (!Active) return;

        int w = Math.Max(1, (int)mapElem.Bounds.InnerWidth);
        int h = Math.Max(1, (int)mapElem.Bounds.InnerHeight);

        using (var surface = new ImageSurface(Format.Argb32, w, h))
        using (var ctx = new Context(surface))
        {
            // clear fully transparent
            ctx.Operator = Operator.Source;
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            ctx.Operator = Operator.Over;

            // draw the ring onto the offscreen surface
            DrawRing(ctx, mapElem, EternalStormModSystem.Instance.config.BorderStart);

            // upload/update the texture from the Cairo surface
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: false, ref ringTex);
        }

        capi.Render.Render2DTexture(
            ringTex.TextureId,
            (float)mapElem.Bounds.renderX,
            (float)mapElem.Bounds.renderY,
            (float)mapElem.Bounds.InnerWidth,
            (float)mapElem.Bounds.InnerHeight,
            50f,
            new Vec4f(1, 1, 1, 1));
    }

    private void DrawRing(Context ctx, GuiElementMap mapElem, double r)
    {
        // Outer glow
        ctx.SetSourceRGBA(1, 0, 0, 0.22);
        ctx.LineWidth = GuiElement.scaled(4f);
        PathCircle(ctx, mapElem, r);
        ctx.StrokePreserve();

        // Core ring
        ctx.SetSourceRGBA(1, 0, 0, 0.85);
        ctx.LineWidth = GuiElement.scaled(2f);
        ctx.Stroke();
    }

    private void PathCircle(Context ctx, GuiElementMap mapElem, double r)
    {
        const int Segs = 256;
        bool first = true;

        for (int i = 0; i <= Segs; i++)
        {
            double t = (i / (double)Segs) * GameMath.TWOPI;
            double wx = cx + r * Math.Cos(t);
            double wz = cz + r * Math.Sin(t);

            var wpos = new Vec3d(wx, 0, wz);
            var view = new Vec2f();
            mapElem.TranslateWorldPosToViewPos(wpos, ref view);

            // Convert absolute screen coords -> element-local coords
            double lx = view.X;
            double ly = view.Y;

            if (first) { ctx.MoveTo(lx, ly); first = false; }
            else ctx.LineTo(lx, ly);
        }

        ctx.ClosePath();
    }

    public override void OnMapClosedClient()
    {
        ringTex?.Dispose();
        ringTex = new LoadedTexture(capi);
    }

    public override void Dispose()
    {
        ringTex?.Dispose();
        ringTex = null;
    }
}
