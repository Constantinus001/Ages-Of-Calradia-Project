using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using TaleWorlds.GauntletUI;
using TaleWorlds.TwoDimension;
using EngineTexture = TaleWorlds.Engine.Texture;
using EngineTextureWrapper = TaleWorlds.Engine.GauntletUI.EngineTexture;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    /// <summary>Small marker rotated by the live season-progress widget.</summary>
    public sealed class AocMapBarSeasonArrowTextureProvider : AocMapBarTextureProviderBase
    {
        protected override string AssetFileName { get { return "aoc_mapbar_season_arrow.png"; } }
        protected override string TextureName { get { return "aoc_mapbar_season_arrow"; } }
        protected override int TargetWidth { get { return 14; } }
        protected override int TargetHeight { get { return 14; } }
    }

    public abstract class AocMapBarTextureProviderBase : TextureProvider
    {
        private EngineTexture _engineTexture;
        private TaleWorlds.TwoDimension.Texture _renderTexture;

        protected override TaleWorlds.TwoDimension.Texture OnGetTextureForRender(
            TwoDimensionContext context, string name)
        {
            if (_renderTexture != null && _renderTexture.IsValid) return _renderTexture;
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(
                    GetType().Assembly.Location);
                DirectoryInfo binDirectory = string.IsNullOrEmpty(assemblyDirectory)
                    ? null : Directory.GetParent(assemblyDirectory);
                DirectoryInfo moduleDirectory = binDirectory == null
                    ? null : binDirectory.Parent;
                if (moduleDirectory == null) return null;
                string assetDirectory = Path.Combine(moduleDirectory.FullName,
                    "GUI", "SpriteParts", "aoc_mapbar");
                string assetPath = Path.Combine(assetDirectory, AssetFileName);
                if (!File.Exists(assetPath)) return null;

                int width;
                int height;
                byte[] pixels = ReadRgba(assetPath, out width, out height);
                _engineTexture = EngineTexture.CreateFromByteArray(pixels, width, height);
                if (_engineTexture == null || _engineTexture.IsReleased) return null;
                _engineTexture.Name = TextureName;
                _renderTexture = new TaleWorlds.TwoDimension.Texture(
                    new EngineTextureWrapper(_engineTexture));
                return _renderTexture;
            }
            catch (Exception exception)
            {
                Trace.WriteLine("AOC MapBar texture failed safely: " + exception);
                return null;
            }
        }

        public override void Clear(bool clearNextFrame)
        {
            base.Clear(clearNextFrame);
            _renderTexture = null;
            if (_engineTexture != null && !_engineTexture.IsReleased)
            {
                if (clearNextFrame) _engineTexture.ReleaseAfterNumberOfFrames(1);
                else _engineTexture.Release();
            }
            _engineTexture = null;
        }

        protected abstract string AssetFileName { get; }
        protected abstract string TextureName { get; }
        protected abstract int TargetWidth { get; }
        protected abstract int TargetHeight { get; }

        private byte[] ReadRgba(string assetPath, out int width, out int height)
        {
            using (Bitmap source = new Bitmap(assetPath))
            using (Bitmap bitmap = new Bitmap(TargetWidth, TargetHeight,
                PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(0, 0, TargetWidth, TargetHeight));
                }
                width = bitmap.Width;
                height = bitmap.Height;
                Rectangle bounds = new Rectangle(0, 0, width, height);
                BitmapData data = bitmap.LockBits(bounds,
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] sourcePixels = new byte[stride * height];
                    byte[] pixels = new byte[width * height * 4];
                    Marshal.Copy(data.Scan0, sourcePixels, 0, sourcePixels.Length);
                    for (int y = 0; y < height; y++)
                    {
                        int sourceRow = data.Stride >= 0
                            ? y * stride : (height - 1 - y) * stride;
                        int targetRow = y * width * 4;
                        Buffer.BlockCopy(sourcePixels, sourceRow, pixels,
                            targetRow, width * 4);
                        for (int x = 0; x < width; x++)
                        {
                            int offset = targetRow + x * 4;
                            byte blue = pixels[offset];
                            pixels[offset] = pixels[offset + 2];
                            pixels[offset + 2] = blue;
                        }
                    }
                    return pixels;
                }
                finally { bitmap.UnlockBits(data); }
            }
        }
    }

    /// <summary>Reference halo above the live vanilla dial; preserves authored color and alpha.</summary>
    public sealed class AocMapBarSeasonCrownTextureProvider : AocMapBarTextureProviderBase
    {
        protected override string AssetFileName { get { return "aoc_mapbar_season_crown_compact.png"; } }
        protected override string TextureName { get { return "aoc_mapbar_season_crown"; } }
        protected override int TargetWidth { get { return 162; } }
        protected override int TargetHeight { get { return 93; } }
    }

    /// <summary>Vanilla center-frame stone revealed only through the old dial notch.</summary>
    public sealed class AocMapBarNotchBackingTextureProvider : AocMapBarTextureProviderBase
    {
        protected override string AssetFileName { get { return "aoc_mapbar_notch_backing.png"; } }
        protected override string TextureName { get { return "aoc_mapbar_notch_backing"; } }
        protected override int TargetWidth { get { return 60; } }
        protected override int TargetHeight { get { return 66; } }
    }

    /// <summary>Silver circular rim around the live vanilla day/night face.</summary>
    public sealed class AocMapBarMedallionRimTextureProvider : AocMapBarTextureProviderBase
    {
        protected override string AssetFileName { get { return "aoc_mapbar_medallion_ring_oval.png"; } }
        protected override string TextureName { get { return "aoc_mapbar_medallion_rim"; } }
        protected override int TargetWidth { get { return 108; } }
        protected override int TargetHeight { get { return 108; } }
    }
}

