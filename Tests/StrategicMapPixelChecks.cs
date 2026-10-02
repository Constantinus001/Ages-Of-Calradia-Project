using System;
using System.Drawing;
using System.Text.RegularExpressions;

// Test-only acceleration of the original exhaustive GetPixel assertions.
// No sampling, hashes, resampling, image conversion or changed alpha semantics.
public static class StrategicMapPixelChecks
{
    public static void Composer(string prefab)
    {
        if (Regex.Matches(prefab, "TextureProviderName=\"CalendarStrategicCampaignAtlasTextureProvider\"").Count != 1)
            throw new InvalidOperationException("Expected exactly one Strategic Map composer; unrelated page/banner providers are not map composers");
    }

    public static void Atlas(Bitmap atlas, Bitmap source, int offsetX, int offsetY, string name)
    {
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                if (atlas.GetPixel(offsetX + x, offsetY + y).ToArgb() != source.GetPixel(x, y).ToArgb())
                    throw new InvalidOperationException("Atlas pixel mismatch for " + name + " at " + x + "," + y);
    }

    public static long[] Coverage(Bitmap basis, Bitmap coverage)
    {
        long land = 0, covered = 0;
        for (int y = 0; y < basis.Height; y++)
            for (int x = 0; x < basis.Width; x++)
                if (basis.GetPixel(x, y).A == 0)
                {
                    land++;
                    if (coverage.GetPixel(x, y).A > 0) covered++;
                }
        return new[] { land, covered };
    }

    public static int Index(Bitmap basis, Bitmap index, bool settlement)
    {
        if (basis.Size != index.Size) throw new InvalidOperationException("Index dimensions differ from base map");
        int borders = 0;
        for (int y = 0; y < basis.Height; y++)
            for (int x = 0; x < basis.Width; x++)
            {
                Color b = basis.GetPixel(x, y), i = index.GetPixel(x, y);
                if (b.A == 0)
                {
                    if (settlement && i.A == 0) throw new InvalidOperationException("Transparent settlement interior at " + x + "," + y);
                    if (!(i.R >= 1 && i.R <= 133) && !(settlement && i.R == 255))
                        throw new InvalidOperationException("Invalid land id at " + x + "," + y);
                    if (settlement && i.R == 255) borders++;
                }
                else if (i.A != 0) throw new InvalidOperationException("Index overwrites opaque border/water at " + x + "," + y);
            }
        return borders;
    }
}
