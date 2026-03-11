using Avalonia.Media.Imaging;

namespace ExtraMapTilesHelper.Models;

public class TextureItem
{
    public string Name { get; set; } = string.Empty;
    public string DictionaryName { get; set; } = string.Empty;
    public Bitmap Preview { get; set; } = null!;

    // NEW: The path to the 8K image on disk!
    public string HighResFilePath { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }
}