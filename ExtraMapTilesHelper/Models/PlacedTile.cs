namespace ExtraMapTilesHelper.Models;

public class PlacedTile
{
    public required TextureItem Texture { get; set; }
    
    // Coordinates
    public double X { get; set; }
    public double Y { get; set; }

    // Helpers to easily access these bindings for UI or export
    public string YtdName => Texture.DictionaryName;
    public string TxdName => Texture.Name;
}