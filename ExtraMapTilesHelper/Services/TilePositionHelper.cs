using ExtraMapTilesHelper.Models;

namespace ExtraMapTilesHelper.Services;

public sealed class TilePositionHelper
{
    public void UpdateFromCoordinates(PlacedTile tile, double x, double y)
    {
        tile.X = x;
        tile.Y = y;

        var offsets = CoordinateMapper.CoordinatesToOffsets(x, y);
        tile.OffsetX = offsets.X;
        tile.OffsetY = offsets.Y;

        var game = CoordinateMapper.CoordinatesToGame(x, y);
        tile.GameX = game.X;
        tile.GameY = game.Y;
    }

    public void UpdateFromOffsets(PlacedTile tile, double ox, double oy)
    {
        tile.OffsetX = ox;
        tile.OffsetY = oy;

        var coords = CoordinateMapper.OffsetsToCoordinates(ox, oy);
        tile.X = coords.X;
        tile.Y = coords.Y;

        var game = CoordinateMapper.CoordinatesToGame(coords.X, coords.Y);
        tile.GameX = game.X;
        tile.GameY = game.Y;
    }

    public void UpdateFromGame(PlacedTile tile, double gx, double gy)
    {
        tile.GameX = gx;
        tile.GameY = gy;

        var coords = CoordinateMapper.GameToCoordinates(gx, gy);
        tile.X = coords.X;
        tile.Y = coords.Y;

        var offsets = CoordinateMapper.CoordinatesToOffsets(coords.X, coords.Y);
        tile.OffsetX = offsets.X;
        tile.OffsetY = offsets.Y;
    }
}