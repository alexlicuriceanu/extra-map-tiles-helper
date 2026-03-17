using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExtraMapTilesHelper.Models;

public class PlacedTile : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private TextureItem _texture = null!;

    public required TextureItem Texture 
    { 
        get => _texture; 
        set 
        { 
            _texture = value; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(YtdName));
            OnPropertyChanged(nameof(TxdName));
        } 
    }
    
    // Coordinates
    public double X 
    { 
        get => _x; 
        set { if (_x != value) { _x = value; OnPropertyChanged(); } } 
    }
    
    public double Y 
    { 
        get => _y; 
        set { if (_y != value) { _y = value; OnPropertyChanged(); } } 
    }

    // Helpers to easily access these bindings for UI or export
    public string YtdName => Texture?.DictionaryName ?? string.Empty;
    public string TxdName => Texture?.Name ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}