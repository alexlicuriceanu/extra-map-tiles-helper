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
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ExtraMapTilesHelper.Services;

public class YtdService
{
    public IEnumerable<TextureItem> ExtractTextures(string filePath)
    {
        var dictName = Path.GetFileNameWithoutExtension(filePath);
        var dictFolder = TempWorkspace.GetDictionaryFolder(dictName);

        byte[] fileData = File.ReadAllBytes(filePath);
        var ytd = new YtdFile();

        try 
        { 
            RpfFile.LoadResourceFile(ytd, fileData, 13); 
        }
        catch (Exception ex)
        { 
            System.Diagnostics.Debug.WriteLine($"Error in ExtractTextures: {ex.Message}");
            return Array.Empty<TextureItem>(); 
        }

        var items = ytd.TextureDict?.Textures?.data_items;
        if (items == null) return Array.Empty<TextureItem>();

        // 1. A thread-safe collection to hold the textures as they finish
        var results = new ConcurrentBag<TextureItem>();

        // 2. PARALLEL PROCESSING: Max out the CPU cores!
        Parallel.ForEach(items, tex =>
        {
            byte[]? ddsBytes = GetDdsWithHeader(tex);
            if (ddsBytes == null) return; // 'continue' becomes 'return' in a Parallel lambda

            string highResPath = Path.Combine(dictFolder, $"{tex.Name}.png");

            // ConvertAndSaveDds is completely thread-safe because every thread 
            // gets its own MemoryStream and file path!
            var bitmap = ConvertAndSaveDds(ddsBytes, highResPath);

            if (bitmap != null)
            {
                results.Add(new TextureItem
                {
                    Name = tex.Name,
                    DictionaryName = dictName,
                    Preview = bitmap,
                    HighResFilePath = highResPath,
                    Width = tex.Width,
                    Height = tex.Height
                });
            }
        });

        // 3. Return the populated bag
        return results;
    }

    private Bitmap? ConvertAndSaveDds(byte[] ddsData, string outputPath)
    {
        using var stream = new MemoryStream(ddsData);
        using var pfimImage = Pfimage.FromStream(stream);

        var info = new SKImageInfo(pfimImage.Width, pfimImage.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var skBitmap = new SKBitmap(info);
        IntPtr ptr = skBitmap.GetPixels();

        if (pfimImage.Format == Pfim.ImageFormat.Rgba32)
        {
            for (int y = 0; y < pfimImage.Height; y++)
                Marshal.Copy(pfimImage.Data, y * pfimImage.Stride, ptr + (y * skBitmap.RowBytes), pfimImage.Width * 4);
        }
        else if (pfimImage.Format == Pfim.ImageFormat.Rgb24)
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
        else
        {
            // Fallback for other formats (like R5G6B5 potentially) - Try direct copy if strides match, 
            // but usually Pfim converts to 32/24 bit. If strictly needed, we can implement more converters here.
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

    private static SKImage CreateThumbnail(SKBitmap original, int maxSize)
    {
        float ratio = Math.Min((float)maxSize / original.Width, (float)maxSize / original.Height);
        var resizeInfo = new SKImageInfo((int)(original.Width * ratio), (int)(original.Height * ratio), SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var resizedBitmap = original.Resize(resizeInfo, new SKSamplingOptions(SKFilterMode.Linear));
        return SKImage.FromBitmap(resizedBitmap);
    }

    private static byte[]? GetDdsWithHeader(Texture tex)
    {
        if (tex == null) return null;
        byte[]? textureData = tex.Data?.FullData;
        if (textureData == null || textureData.Length == 0) return null;

        int width = tex.Width, height = tex.Height, mips = tex.Levels;
        string format = tex.Format.ToString().ToUpper(); // Ensure case-insensitive check

        bool isDx10 = false;
        uint dxgiFormat = 0;
        int blockSize = 16;


        string fourCC;
        // Determine Format, FourCC, and BlockSize
        if (format.Contains("DXT1") || format.Contains("BC1"))
        {
            fourCC = "DXT1";
            blockSize = 8;
        }
        else if (format.Contains("DXT3") || format.Contains("BC2"))
        {
            fourCC = "DXT3";
        }
        else if (format.Contains("DXT5") || format.Contains("BC3"))
        {
            fourCC = "DXT5";
        }
        else if (format.Contains("ATI1") || format.Contains("BC4"))
        {
            fourCC = "ATI1";
            blockSize = 8;
        }
        else if (format.Contains("ATI2") || format.Contains("BC5"))
        {
            fourCC = "ATI2";
        }
        else if (format.Contains("BC7"))
        {
            fourCC = "DX10";
            isDx10 = true;
            dxgiFormat = 98; // DXGI_FORMAT_BC7_UNORM
            blockSize = 16;
        }
        else
        {
            // Default to DXT1 if unknown, though this might fail decoding
            fourCC = "DXT1";
            blockSize = 8;
        }

        // Header size: 128 standard + 20 for DX10 extension if needed
        int headerSize = 128 + (isDx10 ? 20 : 0);
        
        byte[] header = new byte[headerSize];
        using (MemoryStream ms = new MemoryStream(header))
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(0x20534444); // Magic "DDS "
            bw.Write(124);        // dwSize

            // dwFlags (Caps | Height | Width | PixelFormat | MipMapCount | LinearSize)
            bw.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000); 

            bw.Write(height);
            bw.Write(width);

            // Pitch/LinearSize
            bw.Write(Math.Max(1, ((width + 3) / 4)) * blockSize * height);
            
            bw.Write(0); // Depth
            bw.Write(mips); // MipMapCount
            for (int i = 0; i < 11; i++) bw.Write(0); // Reserved

            // DDPIXELFORMAT
            bw.Write(32); // Size
            bw.Write(0x4); // DDPF_FOURCC
            bw.Write(Encoding.ASCII.GetBytes(fourCC));
            bw.Write(0); // RGBBitCount
            bw.Write(0); // RBitMask
            bw.Write(0); // GBitMask
            bw.Write(0); // BBitMask
            bw.Write(0); // ABitMask

            // Caps
            bw.Write(0x1000 | 0x400000); // dwCaps (Texture | MipMap)
            bw.Write(0); // dwCaps2
            bw.Write(0); // dwCaps3
            bw.Write(0); // dwCaps4
            bw.Write(0); // Reserved2

            // Write DX10 Header Extension
            if (isDx10)
            {
                bw.Write(dxgiFormat); // dxgiFormat (BC7_UNORM = 98)
                bw.Write(3);          // resourceDimension (Texture2D = 3)
                bw.Write(0);          // miscFlag
                bw.Write(1);          // arraySize
                bw.Write(0);          // miscFlags2
            }
        }

        byte[] combined = new byte[header.Length + textureData.Length];
        Array.Copy(header, 0, combined, 0, header.Length);
        Array.Copy(textureData, 0, combined, header.Length, textureData.Length);

        return combined;
    }
}