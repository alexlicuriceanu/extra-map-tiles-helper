using Avalonia.Controls;
using ExtraMapTilesHelper.Models;
using ExtraMapTilesHelper.Services;
using System;

namespace ExtraMapTilesHelper.Controllers;

public sealed class SelectionController
{
    private readonly TilePositionHelper _tilePositionHelper;

    public SelectionController(TilePositionHelper tilePositionHelper)
    {
        _tilePositionHelper = tilePositionHelper;
    }

    public PlacedTileItem? CurrentTile { get; private set; }
    public Image? CurrentImage { get; private set; }

    public event Action<PlacedTileItem?, Image?>? SelectionChanged;
    public event Action<PlacedTileItem>? TilePositionUpdated;

    public void SelectTile(PlacedTileItem tile, Image image)
    {
        CurrentTile = tile;
        CurrentImage = image;

        _tilePositionHelper.UpdateFromCoordinates(tile, tile.X, tile.Y);
        SelectionChanged?.Invoke(CurrentTile, CurrentImage);
    }

    public void ClearSelection()
    {
        CurrentTile = null;
        CurrentImage = null;
        SelectionChanged?.Invoke(null, null);
    }

    public void UpdateTilePosition(double newValue, bool isX, bool isOffsetMode)
    {
        if (CurrentTile == null) return;

        if (isOffsetMode)
        {
            double ox = isX ? newValue : CurrentTile.OffsetX;
            double oy = isX ? CurrentTile.OffsetY : newValue;
            _tilePositionHelper.UpdateFromOffsets(CurrentTile, ox, oy);
        }
        else
        {
            double gx = isX ? newValue : CurrentTile.GameX;
            double gy = isX ? CurrentTile.GameY : newValue;
            _tilePositionHelper.UpdateFromGame(CurrentTile, gx, gy);
        }

        TilePositionUpdated?.Invoke(CurrentTile);
    }
}