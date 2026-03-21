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

        double grid = CoordinateMapper.CanvasTileSize;

        foreach (var child in mapCanvas.Children)
        {
            if (child is not Control ctrl) continue;
            if (ctrl.Tag is not TextureItem && ctrl.Tag is not PlacedTileItem) continue;

            double tileX = Canvas.GetLeft(ctrl);
            double tileY = Canvas.GetTop(ctrl);

            double tileW = double.IsNaN(ctrl.Width) ? grid : ctrl.Width;
            double tileH = double.IsNaN(ctrl.Height) ? grid : ctrl.Height;

            int startBucketX = (int)Math.Floor(tileX / grid);
            int startBucketY = (int)Math.Floor(tileY / grid);
            int endBucketX = (int)Math.Floor((tileX + tileW - 0.0001) / grid);
            int endBucketY = (int)Math.Floor((tileY + tileH - 0.0001) / grid);

            for (int bx = startBucketX; bx <= endBucketX; bx++)
            {
                for (int by = startBucketY; by <= endBucketY; by++)
                {
                    var bucketKey = (bx, by);
                    if (!_spatialHash.TryGetValue(bucketKey, out var list))
                    {
                        list = new List<Rect>();
                        _spatialHash[bucketKey] = list;
                    }

                    list.Add(new Rect(tileX, tileY, tileW, tileH));
                }
            }
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

    public Point GetSnappedPosition(Point mousePosition, Canvas mapCanvas, double dragWidth, double dragHeight, Point? dragOffset = null)
    {
        double grid = CoordinateMapper.CanvasTileSize;

        double targetX = dragOffset.HasValue 
            ? mousePosition.X - dragOffset.Value.X 
            : mousePosition.X - (dragWidth / 2);
            
        double targetY = dragOffset.HasValue 
            ? mousePosition.Y - dragOffset.Value.Y 
            : mousePosition.Y - (dragHeight / 2);

        double maxX = (double.IsNaN(mapCanvas.Width) ? 100000 : mapCanvas.Width) - dragWidth;
        double maxY = (double.IsNaN(mapCanvas.Height) ? 100000 : mapCanvas.Height) - dragHeight;

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

                    if (Math.Abs(imgX - targetX) > (Math.Max(dragWidth, imgW) + snapThreshold) ||
                        Math.Abs(imgY - targetY) > (Math.Max(dragHeight, imgH) + snapThreshold))
                    {
                        continue;
                    }

                    int xSegments = Math.Max(0, (int)Math.Floor(imgW / grid));
                    int ySegments = Math.Max(0, (int)Math.Floor(imgH / grid));

                    var xAnchors = new List<double>(xSegments + 3) { imgX };
                    for (int i = 1; i <= xSegments; i++)
                        xAnchors.Add(imgX + (i * grid));
                    xAnchors.Add(imgX + imgW);

                    var yAnchors = new List<double>(ySegments + 3) { imgY };
                    for (int i = 1; i <= ySegments; i++)
                        yAnchors.Add(imgY + (i * grid));
                    yAnchors.Add(imgY + imgH);

                    // NEW: allow snapping when dragged tile approaches from right/bottom OR left/top
                    // targetX is dragged tile top-left X, so include both "anchor" and "anchor - dragWidth"
                    var targetXAnchors = new List<double>(xAnchors.Count * 2);
                    foreach (var ax in xAnchors)
                    {
                        targetXAnchors.Add(ax);
                        targetXAnchors.Add(ax - dragWidth);
                    }

                    // targetY is dragged tile top-left Y, so include both "anchor" and "anchor - dragHeight"
                    var targetYAnchors = new List<double>(yAnchors.Count * 2);
                    foreach (var ay in yAnchors)
                    {
                        targetYAnchors.Add(ay);
                        targetYAnchors.Add(ay - dragHeight);
                    }

                    foreach (var tx in targetXAnchors)
                    {
                        foreach (var ty in targetYAnchors)
                        {
                            if (tx < 0 || ty < 0 || tx > maxX || ty > maxY) continue;

                            double distSq = Math.Pow(targetX - tx, 2) + Math.Pow(targetY - ty, 2);
                            if (distSq <= bestDistSq + 25)
                            {
                                bestDistSq = distSq;
                                bestSnap = new Point(tx, ty);
                                foundSnap = true;

                                if (bestDistSq < 1.0)
                                    return bestSnap;
                            }
                        }
                    }
                }
            }
        }

        return foundSnap ? bestSnap : new Point(targetX, targetY);
    }

    public Point GetSnappedPosition(Point mousePosition, Canvas mapCanvas)
        => GetSnappedPosition(mousePosition, mapCanvas, CoordinateMapper.CanvasTileSize, CoordinateMapper.CanvasTileSize);
}