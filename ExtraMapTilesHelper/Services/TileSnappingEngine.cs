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

    private static Point RotatePoint(double x, double y, double ox, double oy, double cos, double sin)
    {
        double dx = x - ox;
        double dy = y - oy;
        return new Point(ox + dx * cos - dy * sin, oy + dx * sin + dy * cos);
    }

    private static Rect GetBoundingBox(double x, double y, double w, double h, int rotation, RelativePoint origin)
    {
        rotation = (rotation % 360 + 360) % 360;

        if (rotation % 90 != 0 || rotation == 0)
            return new Rect(x, y, w, h);

        double originX = origin.Unit == RelativeUnit.Relative ? origin.Point.X * w : origin.Point.X;
        double originY = origin.Unit == RelativeUnit.Relative ? origin.Point.Y * h : origin.Point.Y;

        double absOriginX = x + originX;
        double absOriginY = y + originY;

        var corners = new Point[]
        {
            new Point(0, 0),
            new Point(w, 0),
            new Point(w, h),
            new Point(0, h)
        };

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        double rad = rotation * Math.PI / 180.0;
        double cos = Math.Round(Math.Cos(rad));
        double sin = Math.Round(Math.Sin(rad));

        foreach (var corner in corners)
        {
            double relX = corner.X - originX;
            double relY = corner.Y - originY;

            double rotX = relX * cos - relY * sin;
            double rotY = relX * sin + relY * cos;

            double absX = absOriginX + rotX;
            double absY = absOriginY + rotY;

            if (absX < minX) minX = absX;
            if (absY < minY) minY = absY;
            if (absX > maxX) maxX = absX;
            if (absY > maxY) maxY = absY;
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

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

            int rotation = 0;
            RelativePoint origin = RelativePoint.TopLeft;

            if (ctrl.Tag is PlacedTileItem placedTile)
                rotation = placedTile.RotationDegrees;

            if (ctrl is Avalonia.Controls.Image img)
                origin = img.RenderTransformOrigin;

            // We only skip arbitrarily rotated tiles. For multiples of 90, compute visual true bounds to be snapped against.
            if (rotation % 90 != 0) continue; 

            Rect bbox = GetBoundingBox(tileX, tileY, tileW, tileH, rotation, origin);
            tileX = bbox.X;
            tileY = bbox.Y;
            tileW = bbox.Width;
            tileH = bbox.Height;

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

    public Point GetSnappedPosition(Point mousePosition, Canvas mapCanvas, double dragWidth, double dragHeight, Point? dragOffset = null, int dragRotation = 0, RelativePoint? dragOrigin = null)
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

        RelativePoint origin = dragOrigin ?? RelativePoint.TopLeft;

        Rect dragBBox = GetBoundingBox(targetX, targetY, dragWidth, dragHeight, dragRotation, origin);
        double diffX = dragBBox.X - targetX;
        double diffY = dragBBox.Y - targetY;

        double snapThreshold = 48.0;
        double bestDistSq = snapThreshold * snapThreshold;

        Point bestSnapBBox = new Point(dragBBox.X, dragBBox.Y);
        bool foundSnap = false;

        // Snapping Priority 1: Snap the anchor mathematically, placing the visual center exactly on a grid point.
        double originX = origin.Unit == RelativeUnit.Relative ? origin.Point.X * dragWidth : origin.Point.X;
        double originY = origin.Unit == RelativeUnit.Relative ? origin.Point.Y * dragHeight : origin.Point.Y;

        double anchorAbsX = targetX + originX;
        double anchorAbsY = targetY + originY;

        double gridX = Math.Round(anchorAbsX / grid) * grid;
        double gridY = Math.Round(anchorAbsY / grid) * grid;

        double distToGridSq = Math.Pow(anchorAbsX - gridX, 2) + Math.Pow(anchorAbsY - gridY, 2);
        if (distToGridSq < bestDistSq)
        {
            bestDistSq = distToGridSq;
            bestSnapBBox = new Point(gridX - originX + diffX, gridY - originY + diffY);
            foundSnap = true;
        }

        if (dragRotation % 90 == 0) // We can snap to other tiles if our dragged tile is perfectly orthogonal
        {
            double snapTargetX = dragBBox.X;
            double snapTargetY = dragBBox.Y;

            int mouseBucketX = (int)(snapTargetX / grid);
            int mouseBucketY = (int)(snapTargetY / grid);

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

                        if (Math.Abs(imgX - snapTargetX) > (Math.Max(dragBBox.Width, imgW) + snapThreshold) ||
                            Math.Abs(imgY - snapTargetY) > (Math.Max(dragBBox.Height, imgH) + snapThreshold))
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

                        var targetXAnchors = new List<double>(xAnchors.Count * 2);
                        foreach (var ax in xAnchors)
                        {
                            targetXAnchors.Add(ax);
                            targetXAnchors.Add(ax - dragBBox.Width);
                        }

                        var targetYAnchors = new List<double>(yAnchors.Count * 2);
                        foreach (var ay in yAnchors)
                        {
                            targetYAnchors.Add(ay);
                            targetYAnchors.Add(ay - dragBBox.Height);
                        }

                        foreach (var tx in targetXAnchors)
                        {
                            foreach (var ty in targetYAnchors)
                            {
                                double distSq = Math.Pow(snapTargetX - tx, 2) + Math.Pow(snapTargetY - ty, 2);
                                if (distSq <= bestDistSq + 25)
                                {
                                    bestDistSq = distSq;
                                    bestSnapBBox = new Point(tx, ty);
                                    foundSnap = true;

                                    if (bestDistSq < 1.0)
                                        goto EndSnapping;
                                }
                            }
                        }
                    }
                }
            }
        }

    EndSnapping:
        if (foundSnap)
        {
            return new Point(bestSnapBBox.X - diffX, bestSnapBBox.Y - diffY);
        }

        return new Point(targetX, targetY);
    }

    public Point GetSnappedPosition(Point mousePosition, Canvas mapCanvas)
        => GetSnappedPosition(mousePosition, mapCanvas, CoordinateMapper.CanvasTileSize, CoordinateMapper.CanvasTileSize);
}