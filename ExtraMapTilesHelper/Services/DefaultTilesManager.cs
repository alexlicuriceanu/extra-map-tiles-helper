using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace ExtraMapTilesHelper.Services;

public sealed class DefaultTilesManager
{
    private readonly List<Image> _defaultTiles = new();

    public void LoadTiles(Canvas mapCanvas)
    {
        if (_defaultTiles.Count > 0) return;

        double grid = CoordinateMapper.CanvasTileSize;
        double anchorX = CoordinateMapper.OriginTileX;
        double anchorY = CoordinateMapper.OriginTileY;

        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                string assetUri = $"avares://ExtraMapTilesHelper/Assets/minimap_sea_{x}_{y}.png";
                try
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(assetUri));
                    var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                    var mapImage = new Image
                    {
                        Source = bitmap,
                        Width = grid,
                        Height = grid,
                        Stretch = Stretch.Fill,
                        IsHitTestVisible = false,
                        ZIndex = 5
                    };

                    Canvas.SetLeft(mapImage, anchorX + (y * grid));
                    Canvas.SetTop(mapImage, anchorY + (x * grid));

                    mapCanvas.Children.Add(mapImage);
                    _defaultTiles.Add(mapImage);
                }
                catch
                {
                }
            }
        }
    }

    public void SetVisible(bool isVisible)
    {
        foreach (var tile in _defaultTiles)
            tile.IsVisible = isVisible;
    }
}