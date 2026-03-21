using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExtraMapTilesHelper.Models;

public class PlacedTileItem : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private double _offsetX;
    private double _offsetY;
    private TextureItem _texture = null!;
    private double _gameX;
    private double _gameY;
    private double _alpha = 100.0;
    private double _scaleX = 1.0;
    private double _scaleY = 1.0;
    private int _rotationDegrees;
    private bool _centered;
    private bool _isOffsetMode = true;

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

    // World-space coordinates (canvas pixels)
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

    // Tile offsets from origin (0,0), with inverted Y axis
    public double OffsetX
    {
        get => _offsetX;
        set { if (_offsetX != value) { _offsetX = value; OnPropertyChanged(); } }
    }

    public double OffsetY
    {
        get => _offsetY;
        set { if (_offsetY != value) { _offsetY = value; OnPropertyChanged(); } }
    }


    // In-game coordinates
    public double GameX
    {
        get => _gameX;
        set { if (_gameX != value) { _gameX = value; OnPropertyChanged(); } }
    }

    public double GameY
    {
        get => _gameY;
        set { if (_gameY != value) { _gameY = value; OnPropertyChanged(); } }
    }

    public double Alpha
    {
        get => _alpha;
        set { if (_alpha != value) { _alpha = value; OnPropertyChanged(); } }
    }

    public double ScaleX
    {
        get => _scaleX;
        set { if (_scaleX != value) { _scaleX = value; OnPropertyChanged(); } }
    }

    public double ScaleY
    {
        get => _scaleY;
        set { if (_scaleY != value) { _scaleY = value; OnPropertyChanged(); } }
    }

    public int RotationDegrees
    {
        get => _rotationDegrees;
        set { if (_rotationDegrees != value) { _rotationDegrees = value; OnPropertyChanged(); } }
    }

    public bool Centered
    {
        get => _centered;
        set { if (_centered != value) { _centered = value; OnPropertyChanged(); } }
    }

    public bool IsOffsetMode
    {
        get => _isOffsetMode;
        set { if (_isOffsetMode != value) { _isOffsetMode = value; OnPropertyChanged(); } }
    }

    public string YtdName => Texture?.DictionaryName ?? string.Empty;
    public string TxdName => Texture?.Name ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}