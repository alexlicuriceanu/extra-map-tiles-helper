using Avalonia.Media.Imaging;

namespace ExtraMapTilesHelper.Models;

public class TextureItem
{
    public string Name { get; set; } = string.Empty;
    public string DictionaryName { get; set; } = string.Empty;

    // This is Avalonia's native image format. No Base64 needed!
    public Bitmap Preview { get; set; } = null!;

    public int Width { get; set; }
    public int Height { get; set; }
}