using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace ExtraMapTilesHelper.Services;

public sealed class Camera
{
    private readonly ScrollViewer _mapScrollViewer;
    private readonly LayoutTransformControl _mapZoomTransform;

    private const double DefaultZoom = 0.5;
    private const double MinZoom = 0.15;
    private const double MaxZoom = 5.0;

    private double _zoomLevel = DefaultZoom;
    private bool _isPanning;
    private Point _lastPanPoint;

    public Camera(ScrollViewer mapScrollViewer, LayoutTransformControl mapZoomTransform)
    {
        _mapScrollViewer = mapScrollViewer;
        _mapZoomTransform = mapZoomTransform;
    }

    public void ResetView()
    {
        _zoomLevel = DefaultZoom;
        _mapZoomTransform.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        _mapScrollViewer.UpdateLayout();
        CenterViewOnMap();
    }

    public void ZoomAtViewportPoint(double zoomFactor, Point viewportPoint)
    {
        double newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);
        if (newZoom == _zoomLevel) return;

        var scrollOffset = _mapScrollViewer.Offset;

        double absoluteX = (scrollOffset.X + viewportPoint.X) / _zoomLevel;
        double absoluteY = (scrollOffset.Y + viewportPoint.Y) / _zoomLevel;

        _zoomLevel = newZoom;
        _mapZoomTransform.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        _mapScrollViewer.UpdateLayout();

        double newOffsetX = (absoluteX * _zoomLevel) - viewportPoint.X;
        double newOffsetY = (absoluteY * _zoomLevel) - viewportPoint.Y;

        _mapScrollViewer.Offset = new Vector(newOffsetX, newOffsetY);
    }

    public void ZoomAtViewportCenter(double zoomFactor)
    {
        var center = new Point(
            _mapScrollViewer.Viewport.Width / 2.0,
            _mapScrollViewer.Viewport.Height / 2.0);

        ZoomAtViewportPoint(zoomFactor, center);
    }

    public void BeginPan(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsMiddleButtonPressed && !point.Properties.IsLeftButtonPressed) return;

        _isPanning = true;
        _lastPanPoint = e.GetPosition(_mapScrollViewer);
        _mapScrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(sender as InputElement);
        e.Handled = true;
    }

    public void Pan(PointerEventArgs e)
    {
        if (!_isPanning) return;

        var currentPoint = e.GetPosition(_mapScrollViewer);
        double deltaX = _lastPanPoint.X - currentPoint.X;
        double deltaY = _lastPanPoint.Y - currentPoint.Y;

        _mapScrollViewer.Offset = new Vector(
            _mapScrollViewer.Offset.X + deltaX,
            _mapScrollViewer.Offset.Y + deltaY);

        _lastPanPoint = currentPoint;
    }

    public void EndPan(PointerReleasedEventArgs e)
    {
        if (!_isPanning || (e.InitialPressMouseButton != MouseButton.Middle && e.InitialPressMouseButton != MouseButton.Left)) return;

        _isPanning = false;
        _mapScrollViewer.Cursor = Cursor.Default;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CenterViewOnMap()
    {
        double mapCenterX = CoordinateMapper.OriginTileX * _zoomLevel;
        double mapCenterY = CoordinateMapper.OriginTileY * _zoomLevel;

        double viewportHalfWidth = _mapScrollViewer.Viewport.Width / 2;
        double viewportHalfHeight = _mapScrollViewer.Viewport.Height / 2;

        _mapScrollViewer.Offset = new Vector(
            mapCenterX - viewportHalfWidth,
            mapCenterY - viewportHalfHeight);
    }
}