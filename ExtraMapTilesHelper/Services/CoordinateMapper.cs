using Avalonia;

namespace ExtraMapTilesHelper.Services;

public static class CoordinateMapper
{
    public const double CanvasTileSize = 256.0;

    public const double OriginTileX = 49920.0;
    public const double OriginTileY = 49920.0;

    public const double GameOriginX = -4140.0;
    public const double GameOriginY = 8400.0;
    public const double GameTileSize = 4500.0;

    public static Point CoordinatesToOffsets(double x, double y)
    {
        return new Point(
            (x - OriginTileX) / CanvasTileSize,
            (y - OriginTileY) / CanvasTileSize);
    }

    public static Point OffsetsToCoordinates(double offsetX, double offsetY)
    {
        return new Point(
            OriginTileX + (offsetX * CanvasTileSize),
            OriginTileY + (offsetY * CanvasTileSize));
    }

    public static Point CoordinatesToGame(double x, double y)
    {
        var offsets = CoordinatesToOffsets(x, y);
        return new Point(
            GameOriginX + (offsets.X * GameTileSize),
            GameOriginY - (offsets.Y * GameTileSize)); // Y-axis is inverted
    }

    public static Point GameToCoordinates(double gameX, double gameY)
    {
        double offsetX = (gameX - GameOriginX) / GameTileSize;
        double offsetY = -(gameY - GameOriginY) / GameTileSize; // Y-axis is inverted
        return OffsetsToCoordinates(offsetX, offsetY);
    }
}