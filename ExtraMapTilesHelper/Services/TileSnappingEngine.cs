using Avalonia;
using Avalonia.Controls;
using ExtraMapTilesHelper.Models;
using System.Collections.Generic;
using System;

namespace ExtraMapTilesHelper.Services;

public sealed class TileSnappingEngine
{
    private readonly Dictionary<(int X, int Y), List<Rect>> _spatialHash = new();
    private Point _lastRawMousePosition = new(-1000, -1000);
    private Point _lastSnappedPosition = new(-1000, -1000);

    public bool IsSnappingEnabled { get; set; } = true;

    public void PrepareDrag(Canvas mapCanvas)
    {
        _lastRawMousePosition = new Point(-1000, -1000);
        _lastSnappedPosition = new Point(-1000, -1000);
        _spatialHash.Clear();

        foreach (var child in mapCanvas.Children)
        {
            if (child is not Control ctrl) continue;
            if (ctrl.Tag is not TextureItem && ctrl.Tag is not PlacedTile) continue;

            double tileX = Canvas.GetLeft(ctrl);
            double tileY = Canvas.GetTop(ctrl);

            double tileW = double.IsNaN(ctrl.Width) ? CoordinateMapper.CanvasTileSize : ctrl.Width;
            double tileH = double.IsNaN(ctrl.Height) ? CoordinateMapper.CanvasTileSize : ctrl.Height;

            int bucketX = (int)(tileX / CoordinateMapper.CanvasTileSize);
            int bucketY = (int)(tileY / CoordinateMapper.CanvasTileSize);
            var bucketKey = (bucketX, bucketY);

            if (!_spatialHash.ContainsKey(bucketKey))
                _spatialHash[bucketKey] = new List<Rect>();

            _spatialHash[bucketKey].Add(new Rect(tileX, tileY, tileW, tileH));
        }
    }

    public bool ShouldRecalculate(Point rawPosition)
    {
        double rawDistSq = Math.Pow(rawPosition.X - _lastRawMousePosition.X, 2)
                         + Math.Pow(rawPosition.Y - _lastRawMousePosition.Y, 2);

        if (rawDistSq < 25) return false;

        _lastRawMousePosition = rawPosition;
        return true;
    }

    public bool IsSameSnappedPosition(Point position) => position == _lastSnappedPosition;

    public void SetLastSnappedPosition(Point position) => _lastSnappedPosition = position;

    public Point GetSnappedPosition(Point mousePosition, Canvas mapCanvas)
    {
        double grid = CoordinateMapper.CanvasTileSize;

        double targetX = mousePosition.X - (grid / 2);
        double targetY = mousePosition.Y - (grid / 2);

        double maxX = (double.IsNaN(mapCanvas.Width) ? 100000 : mapCanvas.Width) - grid;
        double maxY = (double.IsNaN(mapCanvas.Height) ? 100000 : mapCanvas.Height) - grid;

        targetX = Math.Clamp(targetX, 0, maxX);
        targetY = Math.Clamp(targetY, 0, maxY);

        if (!IsSnappingEnabled)
            return new Point(targetX, targetY);

        double snapThreshold = 48.0;
        double bestDistSq = snapThreshold * snapThreshold;

        Point bestSnap = new(targetX, targetY);
        bool foundSnap = false;

        double gridX = Math.Round(targetX / grid) * grid;
        double gridY = Math.Round(targetY / grid) * grid;

        gridX = Math.Clamp(gridX, 0, maxX);
        gridY = Math.Clamp(gridY, 0, maxY);

        double distToGridSq = Math.Pow(targetX - gridX, 2) + Math.Pow(targetY - gridY, 2);
        if (distToGridSq < bestDistSq)
        {
            bestDistSq = distToGridSq;
            bestSnap = new Point(gridX, gridY);
            foundSnap = true;
        }

        int mouseBucketX = (int)(targetX / grid);
        int mouseBucketY = (int)(targetY / grid);

        for (int bx = mouseBucketX - 2; bx <= mouseBucketX + 2; bx++)
        {
            for (int by = mouseBucketY - 2; by <= mouseBucketY + 2; by++)
            {
                if (!_spatialHash.TryGetValue((bx, by), out var tilesInBucket)) continue;

                foreach (var cachedRect in tilesInBucket)
                {
                    double imgX = cachedRect.X;
                    double imgY = cachedRect.Y;
                    double imgW = cachedRect.Width;
                    double imgH = cachedRect.Height;

                    if (Math.Abs(imgX - targetX) > (Math.Max(grid, imgW) + snapThreshold) ||
                        Math.Abs(imgY - targetY) > (Math.Max(grid, imgH) + snapThreshold))
                    {
                        continue;
                    }

                    Point[] snapPoints =
                    {
                        new(imgX - grid, imgY),
                        new(imgX + imgW, imgY),
                        new(imgX, imgY - grid),
                        new(imgX, imgY + imgH),
                        new(imgX - grid, imgY - grid),
                        new(imgX + imgW, imgY - grid),
                        new(imgX - grid, imgY + imgH),
                        new(imgX + imgW, imgY + imgH),
                        new(imgX, imgY)
                    };

                    foreach (var sp in snapPoints)
                    {
                        if (sp.X < 0 || sp.Y < 0 || sp.X > maxX || sp.Y > maxY) continue;

                        double distSq = Math.Pow(targetX - sp.X, 2) + Math.Pow(targetY - sp.Y, 2);
                        if (distSq <= bestDistSq + 25)
                        {
                            bestDistSq = distSq;
                            bestSnap = sp;
                            foundSnap = true;

                            if (bestDistSq < 1.0)
                                return bestSnap;
                        }
                    }
                }
            }
        }

        return foundSnap ? bestSnap : new Point(targetX, targetY);
    }
}