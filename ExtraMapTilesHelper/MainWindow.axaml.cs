using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ExtraMapTilesHelper.Models;
using ExtraMapTilesHelper.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ExtraMapTilesHelper;

public partial class MainWindow : Window
{
    public ObservableCollection<TextureItem> Textures { get; } = new();
    private readonly YtdService _ytdService = new();

    private const double DefaultZoom = 0.5;
    private const double MinZoom = 0.15;
    private const double MaxZoom = 5.0;
    private const double ZoomInSpeed = 1.15;
    private const double ZoomOutSpeed = 0.85;
    private const double GridCellSize = 256.0;

    private double _zoomLevel = DefaultZoom;
    private bool _isPanning = false;
    private Avalonia.Point _lastPanPoint;

    // --- NEW: DRAG AND DROP CACHE ---
    private Point _lastSnappedPosition = new Point(-1000, -1000);
    private readonly System.Collections.Generic.Dictionary<(int X, int Y), System.Collections.Generic.List<Point>> _spatialHash = new();
    private Point _lastRawMousePosition = new Point(-1000, -1000);

    private void OnMainWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            double scaling = screen.Scaling;
            Width = screen.WorkingArea.Width / scaling * 0.8;
            Height = screen.WorkingArea.Height / scaling * 0.8;
            // RootTransform.LayoutTransform = new ScaleTransform(1 / scaling, 1 / scaling);
        }

        ResetView();
    }

    private void ResetView()
    {
        _zoomLevel = DefaultZoom;
        MapZoomTransform.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        MapScrollViewer.UpdateLayout();
        CenterViewOnMap();
    }

    private void CenterViewOnMap()
    {
        double scaledWidth = MapCanvas.Width * _zoomLevel;
        double scaledHeight = MapCanvas.Height * _zoomLevel;

        double centerX = (scaledWidth - MapScrollViewer.Viewport.Width) / 2;
        double centerY = (scaledHeight - MapScrollViewer.Viewport.Height) / 2;

        MapScrollViewer.Offset = new Avalonia.Vector(centerX, centerY);
    }

    private void ZoomAtViewportPoint(double zoomFactor, Avalonia.Point viewportPoint)
    {
        double newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);
        if (newZoom == _zoomLevel) return;

        var scrollOffset = MapScrollViewer.Offset;

        double absoluteX = (scrollOffset.X + viewportPoint.X) / _zoomLevel;
        double absoluteY = (scrollOffset.Y + viewportPoint.Y) / _zoomLevel;

        _zoomLevel = newZoom;
        MapZoomTransform.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        MapScrollViewer.UpdateLayout();

        double newOffsetX = (absoluteX * _zoomLevel) - viewportPoint.X;
        double newOffsetY = (absoluteY * _zoomLevel) - viewportPoint.Y;

        MapScrollViewer.Offset = new Avalonia.Vector(newOffsetX, newOffsetY);
    }

    private void ZoomAtViewportCenter(double zoomFactor)
    {
        var center = new Avalonia.Point(
            MapScrollViewer.Viewport.Width / 2.0,
            MapScrollViewer.Viewport.Height / 2.0);

        ZoomAtViewportPoint(zoomFactor, center);
    }

    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double zoomFactor = e.Delta.Y > 0 ? ZoomInSpeed : ZoomOutSpeed;
        ZoomAtViewportPoint(zoomFactor, e.GetPosition(MapScrollViewer));
        e.Handled = true;
    }

    private void OnResetViewClicked(object? sender, RoutedEventArgs e)
    {
        ResetView();
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e)
    {
        ZoomAtViewportCenter(ZoomInSpeed);
    }

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e)
    {
        ZoomAtViewportCenter(ZoomOutSpeed);
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);

        // 1. Only trigger if they clicked the Middle Mouse Button (Scroll Wheel)
        if (point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;

            // Record exactly where the mouse was when they clicked
            _lastPanPoint = e.GetPosition(MapScrollViewer);

            // Change the cursor to indicate we are grabbing the map
            MapScrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);

            // "Capture" the pointer so if they drag outside the window, it doesn't break the pan
            e.Pointer.Capture(sender as Avalonia.Input.InputElement);
            e.Handled = true;
        }
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        // 2. If we aren't currently panning, do nothing
        if (!_isPanning) return;

        var currentPoint = e.GetPosition(MapScrollViewer);

        // Calculate the physical distance the mouse moved since the last frame
        double deltaX = _lastPanPoint.X - currentPoint.X;
        double deltaY = _lastPanPoint.Y - currentPoint.Y;

        // Apply that exact movement to the ScrollViewer's offset
        MapScrollViewer.Offset = new Avalonia.Vector(
            MapScrollViewer.Offset.X + deltaX,
            MapScrollViewer.Offset.Y + deltaY
        );

        // Update the last point for the next frame
        _lastPanPoint = currentPoint;
    }

    private void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // 3. Stop panning when they let go of the middle mouse button
        if (_isPanning && e.InitialPressMouseButton == MouseButton.Middle)
        {
            _isPanning = false;

            // Reset the cursor back to normal
            MapScrollViewer.Cursor = Cursor.Default;

            // Release the captured mouse
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    public ObservableCollection<DictionaryItem> Dictionaries { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        TextureTree.ItemsSource = Dictionaries;

        AddHandler(DragDrop.DropEvent, OnCanvasDrop);
        AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnCanvasDragLeave);

        // NEW: Listen for when the mouse first enters the canvas
        AddHandler(DragDrop.DragEnterEvent, OnCanvasDragEnter);
    }

    private void OnCanvasDragEnter(object? sender, DragEventArgs e)
    {
        _lastSnappedPosition = new Point(-1000, -1000);

        // 1. Clear the spatial hash
        _spatialHash.Clear();

        // 2. Sort every tile into its mathematical bucket
        foreach (var child in MapCanvas.Children)
        {
            if (child is Grid grid && grid.Name != "DragHighlight")
            {
                double tileX = Canvas.GetLeft(grid);
                double tileY = Canvas.GetTop(grid);

                // Calculate which bucket this tile belongs in
                int bucketX = (int)(tileX / GridCellSize);
                int bucketY = (int)(tileY / GridCellSize);
                var bucketKey = (bucketX, bucketY);

                // If this bucket doesn't exist yet, create it
                if (!_spatialHash.ContainsKey(bucketKey))
                {
                    _spatialHash[bucketKey] = new System.Collections.Generic.List<Point>();
                }

                // Add the tile to its designated bucket
                _spatialHash[bucketKey].Add(new Point(tileX, tileY));
            }
        }
    }

    private Point CalculateDropPosition(Point mousePosition)
    {
        double targetX = mousePosition.X - (GridCellSize / 2);
        double targetY = mousePosition.Y - (GridCellSize / 2);

        // --- NEW: DISTANCE SQUARED MATH ---
        double snapThreshold = 48.0;
        double bestDistSq = snapThreshold * snapThreshold; // 2304

        Point bestSnap = new Point(targetX, targetY);
        bool foundSnap = false;

        // 1. Calculate the closest Grid Snap Point
        double gridX = Math.Round(targetX / GridCellSize) * GridCellSize;
        double gridY = Math.Round(targetY / GridCellSize) * GridCellSize;

        double maxGridX = MapCanvas.Width - GridCellSize;
        double maxGridY = MapCanvas.Height - GridCellSize;
        gridX = Math.Clamp(gridX, 0, maxGridX);
        gridY = Math.Clamp(gridY, 0, maxGridY);

        double distToGridSq = Math.Pow(targetX - gridX, 2) + Math.Pow(targetY - gridY, 2);
        if (distToGridSq < bestDistSq)
        {
            bestDistSq = distToGridSq;
            bestSnap = new Point(gridX, gridY);
            foundSnap = true;
        }

        // 2. Calculate the closest Tile Snap Point
        // 1. What bucket is the mouse currently in?
        int mouseBucketX = (int)(targetX / GridCellSize);
        int mouseBucketY = (int)(targetY / GridCellSize);

        // 2. Only check the mouse's bucket and the immediate neighbors (a 5x5 chunk area)
        for (int bx = mouseBucketX - 2; bx <= mouseBucketX + 2; bx++)
        {
            for (int by = mouseBucketY - 2; by <= mouseBucketY + 2; by++)
            {
                // Instantly grab the tiles in this chunk. If the chunk is empty, skip instantly!
                if (_spatialHash.TryGetValue((bx, by), out var tilesInBucket))
                {
                    foreach (var cachedPos in tilesInBucket)
                    {
                        double imgX = cachedPos.X;
                        double imgY = cachedPos.Y;

                        if (Math.Abs(imgX - targetX) > (GridCellSize + snapThreshold) ||
                            Math.Abs(imgY - targetY) > (GridCellSize + snapThreshold))
                        {
                            continue;
                        }

                        Point[] snapPoints = new Point[]
                        {
                            new Point(imgX - GridCellSize, imgY),
                            new Point(imgX + GridCellSize, imgY),
                            new Point(imgX, imgY - GridCellSize),
                            new Point(imgX, imgY + GridCellSize),
                            new Point(imgX - GridCellSize, imgY - GridCellSize),
                            new Point(imgX + GridCellSize, imgY - GridCellSize),
                            new Point(imgX - GridCellSize, imgY + GridCellSize),
                            new Point(imgX + GridCellSize, imgY + GridCellSize)
                        };

                        foreach (var sp in snapPoints)
                        {
                            if (sp.X < 0 || sp.Y < 0 || sp.X > maxGridX || sp.Y > maxGridY) continue;

                            double distSq = Math.Pow(targetX - sp.X, 2) + Math.Pow(targetY - sp.Y, 2);

                            if (distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestSnap = sp;
                                foundSnap = true;

                                if (bestDistSq < 1.0) return bestSnap;
                            }
                        }
                    }
                }
            }
        }

        return foundSnap ? bestSnap : new Point(targetX, targetY);
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("DraggedTexture"))
        {
            e.DragEffects = DragDropEffects.None;
            DragHighlight.IsVisible = false;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        var position = e.GetPosition(MapCanvas);

        // --- NEW: THE THROTTLE ---
        // If the mouse hasn't moved at least 5 pixels since the last calculation, skip the heavy math!
        double rawDistSq = Math.Pow(position.X - _lastRawMousePosition.X, 2) + Math.Pow(position.Y - _lastRawMousePosition.Y, 2);
        if (rawDistSq < 25) // 5 pixels squared
        {
            e.Handled = true;
            return;
        }
        _lastRawMousePosition = position; // Update the tracker

        var snappedPosition = CalculateDropPosition(position);

        if (snappedPosition == _lastSnappedPosition)
        {
            e.Handled = true;
            return;
        }

        _lastSnappedPosition = snappedPosition;

        // THE FIX: Use Canvas positioning so Avalonia cleans up the old pixels!
        Canvas.SetLeft(DragHighlight, snappedPosition.X);
        Canvas.SetTop(DragHighlight, snappedPosition.Y);

        if (!DragHighlight.IsVisible)
        {
            DragHighlight.IsVisible = true;
        }

        e.Handled = true;
    }

    private void OnCanvasDragLeave(object? sender, RoutedEventArgs e)
    {
        // Only hide if the e is actually related to the canvas boundary
        if (e.Source == MapCanvas)
        {
            DragHighlight.IsVisible = false;
        }
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select YTD Files",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Texture Dictionary") { Patterns = new[] { "*.ytd" } } }
        });

        if (files.Count == 0) return;

        ImportMenuItem.IsEnabled = false;

        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                string dictName = System.IO.Path.GetFileNameWithoutExtension(file.Path.LocalPath);

                // 1. DUPLICATE CHECK: Remove the whole dictionary if it already exists
                Dispatcher.UIThread.Invoke(() =>
                {
                    var existingDict = Dictionaries.FirstOrDefault(d => d.Name == dictName);
                    if (existingDict != null) Dictionaries.Remove(existingDict);
                });

                // 2. Create the new parent dictionary
                var newDict = new DictionaryItem { Name = dictName };

                // 3. Extract textures and add them TO THE DICTIONARY, not the main UI yet
                var extractedTextures = _ytdService.ExtractTextures(file.Path.LocalPath);
                foreach (var tex in extractedTextures)
                {
                    newDict.Textures.Add(tex);
                }

                // 4. Safely push the fully loaded dictionary to the UI
                Dispatcher.UIThread.Post(() => Dictionaries.Add(newDict));
            }
        });

        ImportMenuItem.IsEnabled = true;
    }

    // --- NEW DRAG AND DROP LOGIC ---

    // 1. Pick up the tile
    private async void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only start dragging if they clicked the LEFT mouse button
        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed) return;

        var control = sender as Control;
        var draggedItem = control?.DataContext as TextureItem;

        if (draggedItem == null) return;

        var dragData = new DataObject();
        dragData.Set("DraggedTexture", draggedItem);

        await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy);
    }

    // 2. Drop the tile on the grid
    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        // Hide the highlight immediately
        DragHighlight.IsVisible = false;

        if (e.Data.Contains("DraggedTexture") && e.Data.Get("DraggedTexture") is TextureItem item)
        {
            var dropPosition = e.GetPosition(MapCanvas);
            var finalPosition = CalculateDropPosition(dropPosition);

            // Decode at cell size (lower memory than 1024 for this use-case)
            Avalonia.Media.Imaging.Bitmap canvasBitmap;
            using (var stream = System.IO.File.OpenRead(item.HighResFilePath))
            {
                canvasBitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 1024);
            }

            var mapImage = new Image
            {
                Source = canvasBitmap,
                Width = GridCellSize,
                Height = GridCellSize,
                Stretch = Stretch.Fill,
                Tag = item
            };

            Canvas.SetLeft(mapImage, finalPosition.X);
            Canvas.SetTop(mapImage, finalPosition.Y);

            MapCanvas.Children.Add(mapImage);
        }
    }

    private void OnDictionaryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only react to Left Click
        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed) return;

        // Find the dictionary we clicked and toggle its expanded state
        if (sender is Control control && control.DataContext is DictionaryItem dict)
        {
            dict.IsExpanded = !dict.IsExpanded;

            // Mark as handled so the click doesn't accidentally trigger other background events
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.R)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.OemPlus or Key.Add)
        {
            ZoomAtViewportCenter(ZoomInSpeed);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.OemMinus or Key.Subtract)
        {
            ZoomAtViewportCenter(ZoomOutSpeed);
            e.Handled = true;
        }
    }
}