using CodeWalker.GameFiles;
using CodeWalker.Utils; // Or your local namespace if you copied DDSIO.cs
using Pfim;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ExtraMapTilesHelper.backend
{
    public class CodeWalkerService
    {
        public record TextureInfo(string Name, int Width, int Height, string Preview);
        public record YtdResult(string DictionaryName, List<TextureInfo> Textures);

        // Notice we no longer return a YtdResult. We use a callback Action instead.
        public void ExtractYtd(string filePath, Action<TextureInfo> onTextureReady)
        {
            var dictName = Path.GetFileNameWithoutExtension(filePath);
            byte[] fileData = File.ReadAllBytes(filePath);
            var ytd = new YtdFile();

            try
            {
                RpfFile.LoadResourceFile(ytd, fileData, 13);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CodeWalker YTD Load Error: {ex.Message}");
                return;
            }

            var items = ytd.TextureDict?.Textures?.data_items;
            if (items == null) return;

            foreach (var tex in items)
            {
                try
                {
                    byte[] ddsBytes = GetDDS(tex);
                    if (ddsBytes != null)
                    {
                        string preview = ConvertDdsToPngBase64(ddsBytes);

                        // Immediately send this single texture back to the router!
                        onTextureReady(new TextureInfo(tex.Name, tex.Width, tex.Height, preview));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Skipping {tex.Name}: {ex.Message}");
                }
            }
        }

        // --- YOUR CUSTOM DDS EXTRACTOR ---
        private static byte[] GetDDS(Texture tex)
        {
            if (tex == null) return null;

            // Get the raw bytes of the first (biggest) mipmap level
            byte[] textureData = tex.Data?.FullData;
            if (textureData == null || textureData.Length == 0) return null;

            int width = tex.Width;
            int height = tex.Height;
            int mips = tex.Levels;
            string format = tex.Format.ToString();

            byte[] header = new byte[128];
            using (MemoryStream ms = new MemoryStream(header))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(0x20534444); // Magic "DDS "
                bw.Write(124); // Size of header structure
                bw.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000); // Flags
                bw.Write(height);
                bw.Write(width);

                int blockSize = (format.Contains("DXT1") || format.Contains("BC1")) ? 8 : 16;
                int pitch = Math.Max(1, ((width + 3) / 4)) * blockSize;
                bw.Write(pitch * height);

                bw.Write(0); // Depth
                bw.Write(mips); // Mipmap count
                for (int i = 0; i < 11; i++) bw.Write(0); // Reserved

                // PIXEL FORMAT STRUCTURE
                bw.Write(32);
                bw.Write(0x4);

                string fourCC = "DXT1";
                if (format.Contains("DXT3") || format.Contains("BC2")) fourCC = "DXT3";
                if (format.Contains("DXT5") || format.Contains("BC3")) fourCC = "DXT5";
                if (format.Contains("ATI1") || format.Contains("BC4")) fourCC = "ATI1";
                if (format.Contains("ATI2") || format.Contains("BC5")) fourCC = "ATI2";

                bw.Write(Encoding.ASCII.GetBytes(fourCC));

                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                // CAPS
                bw.Write(0x1000 | 0x400000);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            }

            byte[] combined = new byte[header.Length + textureData.Length];
            Array.Copy(header, 0, combined, 0, header.Length);
            Array.Copy(textureData, 0, combined, header.Length, textureData.Length);

            return combined;
        }

        // --- PFIM TO PNG CONVERTER ---
        private static string ConvertDdsToPngBase64(byte[] ddsData)
        {
            using var stream = new MemoryStream(ddsData);
            using var pfimImage = Pfimage.FromStream(stream);

            var info = new SKImageInfo(pfimImage.Width, pfimImage.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);

            // Get a direct pointer to SkiaSharp's image buffer
            IntPtr ptr = bitmap.GetPixels();

            if (pfimImage.Format == ImageFormat.Rgba32)
            {
                // RAM CRASH FIX: pfimImage.Data contains MipMaps at the end. 
                // We must only copy the main image by reading exactly 'Height' number of rows.
                for (int y = 0; y < pfimImage.Height; y++)
                {
                    // Marshal.Copy(sourceArray, sourceIndex, destinationPointer, lengthInBytes)
                    Marshal.Copy(pfimImage.Data, y * pfimImage.Stride, ptr + (y * info.RowBytes), info.Width * 4);
                }
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

                // Safe copy restricted to the exact byte size Skia expects
                Marshal.Copy(bgra, 0, ptr, info.BytesSize);
            }

            using var skImage = SKImage.FromBitmap(bitmap);
            using var pngData = skImage.Encode(SKEncodedImageFormat.Png, 80);

            return $"data:image/png;base64,{Convert.ToBase64String(pngData.AsSpan())}";
        }
    }
}
