using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Media.Imaging;
using CodeWalker.GameFiles;
using ExtraMapTilesHelper.Models;
using Pfim;
using SkiaSharp;

namespace ExtraMapTilesHelper.Services;

public class YtdService
{
    public IEnumerable<TextureItem> ExtractTextures(string filePath)
    {
        var dictName = Path.GetFileNameWithoutExtension(filePath);
        var dictFolder = TempWorkspace.GetDictionaryFolder(dictName);

        byte[] fileData = File.ReadAllBytes(filePath);
        var ytd = new YtdFile();

        try { RpfFile.LoadResourceFile(ytd, fileData, 13); }
        catch { yield break; }

        var items = ytd.TextureDict?.Textures?.data_items;
        if (items == null) yield break;

        foreach (var tex in items)
        {
            byte[] ddsBytes = GetDdsWithHeader(tex);
            if (ddsBytes == null) continue;

            string highResPath = Path.Combine(dictFolder, $"{tex.Name}.png");

            var bitmap = ConvertAndSaveDds(ddsBytes, highResPath);

            if (bitmap != null)
            {
                yield return new TextureItem
                {
                    Name = tex.Name,
                    DictionaryName = dictName,
                    Preview = bitmap,
                    HighResFilePath = highResPath,
                    Width = tex.Width,
                    Height = tex.Height
                };
            }
        }
    }

    private Bitmap? ConvertAndSaveDds(byte[] ddsData, string outputPath)
    {
        using var stream = new MemoryStream(ddsData);
        using var pfimImage = Pfimage.FromStream(stream);

        var info = new SKImageInfo(pfimImage.Width, pfimImage.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var skBitmap = new SKBitmap(info);
        IntPtr ptr = skBitmap.GetPixels();

        if (pfimImage.Format == ImageFormat.Rgba32)
        {
            for (int y = 0; y < pfimImage.Height; y++)
                Marshal.Copy(pfimImage.Data, y * pfimImage.Stride, ptr + (y * skBitmap.RowBytes), pfimImage.Width * 4);
        }
        else if (pfimImage.Format == ImageFormat.Rgb24)
        {
            int w = pfimImage.Width, h = pfimImage.Height;
            byte[] bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int src = y * pfimImage.Stride + x * 3, dst = (y * w + x) * 4;
                    bgra[dst] = pfimImage.Data[src];
                    bgra[dst + 1] = pfimImage.Data[src + 1];
                    bgra[dst + 2] = pfimImage.Data[src + 2];
                    bgra[dst + 3] = 255;
                }
            }
            Marshal.Copy(bgra, 0, ptr, info.BytesSize);
        }

        // 1. SAVE THE FULL-RES IMAGE TO DISK FIRST
        using (var fullResImage = SKImage.FromBitmap(skBitmap))
        using (var fullResData = fullResImage.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.OpenWrite(outputPath))
        {
            fullResData.SaveTo(fs);
        }

        // 2. GENERATE THE LIGHTWEIGHT THUMBNAIL FOR THE UI
        int maxPreviewSize = 128;
        using SKImage thumbImage = skBitmap.Width > maxPreviewSize || skBitmap.Height > maxPreviewSize
            ? CreateThumbnail(skBitmap, maxPreviewSize)
            : SKImage.FromBitmap(skBitmap);

        using var thumbData = thumbImage.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        thumbData.SaveTo(ms);
        ms.Seek(0, SeekOrigin.Begin);

        return new Bitmap(ms);
    }

    private SKImage CreateThumbnail(SKBitmap original, int maxSize)
    {
        float ratio = Math.Min((float)maxSize / original.Width, (float)maxSize / original.Height);
        var resizeInfo = new SKImageInfo((int)(original.Width * ratio), (int)(original.Height * ratio), SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var resizedBitmap = original.Resize(resizeInfo, SKFilterQuality.Low);
        return SKImage.FromBitmap(resizedBitmap);
    }

    private byte[] GetDdsWithHeader(Texture tex)
    {
        if (tex == null) return null;
        byte[] textureData = tex.Data?.FullData;
        if (textureData == null || textureData.Length == 0) return null;

        int width = tex.Width, height = tex.Height, mips = tex.Levels;
        string format = tex.Format.ToString();

        byte[] header = new byte[128];
        using (MemoryStream ms = new MemoryStream(header))
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(0x20534444); bw.Write(124);
            bw.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000);
            bw.Write(height); bw.Write(width);

            int blockSize = (format.Contains("DXT1") || format.Contains("BC1")) ? 8 : 16;
            bw.Write(Math.Max(1, ((width + 3) / 4)) * blockSize * height);
            bw.Write(0); bw.Write(mips);
            for (int i = 0; i < 11; i++) bw.Write(0);

            bw.Write(32); bw.Write(0x4);
            string fourCC = format.Contains("DXT3") || format.Contains("BC2") ? "DXT3" :
                            format.Contains("DXT5") || format.Contains("BC3") ? "DXT5" :
                            format.Contains("ATI1") || format.Contains("BC4") ? "ATI1" :
                            format.Contains("ATI2") || format.Contains("BC5") ? "ATI2" : "DXT1";

            bw.Write(Encoding.ASCII.GetBytes(fourCC));
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            bw.Write(0x1000 | 0x400000); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        }

        byte[] combined = new byte[header.Length + textureData.Length];
        Array.Copy(header, 0, combined, 0, header.Length);
        Array.Copy(textureData, 0, combined, header.Length, textureData.Length);

        return combined;
    }
}