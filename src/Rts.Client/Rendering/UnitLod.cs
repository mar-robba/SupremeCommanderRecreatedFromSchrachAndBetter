namespace Rts.Client.Rendering;

/// <summary>(9b-2) Which units use the low-poly mesh: projected bounding-sphere radius in pixels, with hysteresis
/// (pure; <c>UnitLodTests</c>).</summary>
public static class UnitLod
{
    /// <summary>Units whose bounding sphere projects below this radius (pixels) use the LOD mesh.</summary>
    public static float PixelRadius = 7f;
    /// <summary>Hysteresis band (fraction of <see cref="PixelRadius"/>): a unit switches to LOD below
    /// r·(1 − band) and back to the full mesh above r·(1 + band), so units at the boundary do not flicker.</summary>
    public const float Band = 0.12f;

    /// <summary>Pixels per world unit at distance 1 for a screen of height <paramref name="screenH"/> and a
    /// projection whose [1][1] element is <paramref name="projYScale"/> (= 1 / tan(fovy / 2)).</summary>
    public static float PixelsAtUnitDistance(float screenH, float projYScale) => screenH * 0.5f * projYScale;

    /// <summary>Projected radius (pixels) of a sphere of radius <paramref name="radius"/> at <paramref name="distance"/>.</summary>
    public static float ProjectedRadius(float radius, float distance, float pxAtUnitDistance) =>
        radius * pxAtUnitDistance / MathF.Max(distance, 0.01f);

    /// <summary>LOD decision with hysteresis: <paramref name="wasLod"/> = the unit's state last frame.</summary>
    public static bool UseLod(bool wasLod, float projectedRadius, float threshold, float band = Band) =>
        projectedRadius < threshold * (wasLod ? 1f + band : 1f - band);

    /// <summary>Camera distance beyond which a model of <paramref name="radius"/> starts using its LOD mesh.</summary>
    public static float SwitchDistance(float radius, float pxAtUnitDistance, float threshold, float band = Band) =>
        radius * pxAtUnitDistance / (threshold * (1f - band));
}
