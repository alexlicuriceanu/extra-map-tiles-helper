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
    private readonly System.Collections.Generic.Dictionary<(int X, int Y), System.Collections.Generic.List<Avalonia.Rect>> _spatialHash = new();
    private Point _lastRawMousePosition = new Point(-1000, -1000);
    private readonly System.Collections.Generic.List<Image> _defaultTiles = new();
    private bool _isSnappingEnabled = true;

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
        LoadDefaultTiles();
    }

    private void LoadDefaultTiles()
    {
        // Shift anchors to the new exact grid center
        double anchorX = 49920;
        double anchorY = 49920;

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
                        Width = GridCellSize,
                        Height = GridCellSize,
                        Stretch = Stretch.Fill,
                        IsHitTestVisible = false,
                        ZIndex = 5
                    };

                    Canvas.SetLeft(mapImage, anchorX + (y * GridCellSize));
                    Canvas.SetTop(mapImage, anchorY + (x * GridCellSize));

                    MapCanvas.Children.Add(mapImage);
                    _defaultTiles.Add(mapImage);
                }
                catch
                {

                }
            }
        }
    }

    private void OnToggleDefaultTilesClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            bool isVisible = menuItem.IsChecked == true;
            foreach (var tile in _defaultTiles)
            {
                tile.IsVisible = isVisible;
            }
        }
    }

    private void OnToggleTileSnappingClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            _isSnappingEnabled = menuItem.IsChecked == true;
        }
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
        // The logical center point where the red dot is
        double mapCenterX = 49920 * _zoomLevel; 
        double mapCenterY = 49920 * _zoomLevel;

        double viewportHalfWidth = MapScrollViewer.Viewport.Width / 2;
        double viewportHalfHeight = MapScrollViewer.Viewport.Height / 2;

        MapScrollViewer.Offset = new Avalonia.Vector(mapCenterX - viewportHalfWidth, mapCenterY - viewportHalfHeight);
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

        // Allow clicking on the empty canvas to deselect any currently selected tile
        if (point.Properties.IsLeftButtonPressed)
        {
            PlacedTilesList.SelectedItem = null;
        }

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
    public ObservableCollection<PlacedTile> PlacedTiles { get; } = new();

    private Image? _currentSelectedImage;
    private PlacedTile? _currentSelectedTile;
    private bool _isUpdatingBoxes = false;
    
    // --- NEW: Grid Tile Dragging Trackers ---
    private Point? _tileDragStartPoint;
    private bool _isDraggingTile = false;

    public MainWindow()
    {
        InitializeComponent();
        TextureTree.ItemsSource = Dictionaries;

        // Let the future right-side table know where to find the objects
        PlacedTilesList.ItemsSource = PlacedTiles;

        AddHandler(DragDrop.DropEvent, OnCanvasDrop);
        AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnCanvasDragLeave);

        // NEW: Listen for when the mouse first enters the canvas
        AddHandler(DragDrop.DragEnterEvent, OnCanvasDragEnter);
    }

    private void OnCanvasDragEnter(object? sender, DragEventArgs e)
    {
        _lastSnappedPosition = new Point(-1000, -1000);
        _spatialHash.Clear();

        foreach (var child in MapCanvas.Children)
        {
            // THE FIX: Look for anything with a TextureItem tag, instead of strictly a Grid!
            if (child is Control ctrl && (ctrl.Tag is TextureItem || ctrl.Tag is PlacedTile))
            {
                double tileX = Canvas.GetLeft(ctrl);
                double tileY = Canvas.GetTop(ctrl);

                // Fallback to GridCellSize if Width/Height aren't explicitly set
                double tileW = double.IsNaN(ctrl.Width) ? GridCellSize : ctrl.Width;
                double tileH = double.IsNaN(ctrl.Height) ? GridCellSize : ctrl.Height;

                int bucketX = (int)(tileX / GridCellSize);
                int bucketY = (int)(tileY / GridCellSize);
                var bucketKey = (bucketX, bucketY);

                if (!_spatialHash.ContainsKey(bucketKey))
                {
                    _spatialHash[bucketKey] = new System.Collections.Generic.List<Avalonia.Rect>();
                }

                _spatialHash[bucketKey].Add(new Avalonia.Rect(tileX, tileY, tileW, tileH));
            }
        }
    }

    private Point CalculateDropPosition(Point mousePosition)
    {
        double targetX = mousePosition.X - (GridCellSize / 2);
        double targetY = mousePosition.Y - (GridCellSize / 2);

        if (!_isSnappingEnabled)
        {
            return new Point(targetX, targetY);
        }

        // --- NEW: DISTANCE SQUARED MATH ---
        double snapThreshold = 48.0;
        double bestDistSq = snapThreshold * snapThreshold; // 2304

        Point bestSnap = new Point(targetX, targetY);
        bool foundSnap = false;

        // 1. Calculate the closest Grid Snap Point
        double gridX = Math.Round(targetX / GridCellSize) * GridCellSize;
        double gridY = Math.Round(targetY / GridCellSize) * GridCellSize;

        // REmoved the maxGrid clamping here to support infinite map space
        gridX = Math.Max(0, gridX);
        gridY = Math.Max(0, gridY);

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

        // 2. Calculate the closest Tile Snap Point using the true bounding boxes
        for (int bx = mouseBucketX - 2; bx <= mouseBucketX + 2; bx++)
        {
            for (int by = mouseBucketY - 2; by <= mouseBucketY + 2; by++)
            {
                if (_spatialHash.TryGetValue((bx, by), out var tilesInBucket))
                {
                    foreach (var cachedRect in tilesInBucket)
                    {
                        double imgX = cachedRect.X;
                        double imgY = cachedRect.Y;
                        double imgW = cachedRect.Width;
                        double imgH = cachedRect.Height;

                        // Broad-phase using true sizes
                        if (Math.Abs(imgX - targetX) > (Math.Max(GridCellSize, imgW) + snapThreshold) ||
                            Math.Abs(imgY - targetY) > (Math.Max(GridCellSize, imgH) + snapThreshold))
                        {
                            continue;
                        }

                        // 9 Magnetic Points: The 8 edges + the exact overlap center!
                        Point[] snapPoints = new Point[]
                        {
                            new Point(imgX - GridCellSize, imgY),
                            new Point(imgX + imgW, imgY),
                            new Point(imgX, imgY - GridCellSize),
                            new Point(imgX, imgY + imgH),
                            new Point(imgX - GridCellSize, imgY - GridCellSize),
                            new Point(imgX + imgW, imgY - GridCellSize),
                            new Point(imgX - GridCellSize, imgY + imgH),
                            new Point(imgX + imgW, imgY + imgH),
                            new Point(imgX, imgY) // NEW: Snap directly on top of the tile!
                        };

                        foreach (var sp in snapPoints)
                        {
                            if (sp.X < 0 || sp.Y < 0) continue; // Only check negative bounds now

                            double distSq = Math.Pow(targetX - sp.X, 2) + Math.Pow(targetY - sp.Y, 2);

                            // THE FIX: We add a +25 buffer to bestDistSq. 
                            // This means if a tile edge is even *slightly* further away than an empty grid line, 
                            // the tile still wins the tug-of-war!
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
        }

        return foundSnap ? bestSnap : new Point(targetX, targetY);
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        // 1. Differentiate between a new texture from the sidebar and a PlacedTile being moved
        if (!e.Data.Contains("DraggedTexture") && !e.Data.Contains("MovePlacedTile"))
        {
            e.DragEffects = DragDropEffects.None;
            DragHighlight.IsVisible = false;
            return;
        }

        e.DragEffects = e.Data.Contains("MovePlacedTile") ? DragDropEffects.Move : DragDropEffects.Copy;
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

        // A) Handle repositioning an already placed tile
        if (e.Data.Contains("MovePlacedTile") && e.Data.Get("MovePlacedTile") is PlacedTile moveTile && e.Data.Get("SourceImage") is Image sourceImage)
        {
            var dropPosition = e.GetPosition(MapCanvas);
            var finalPosition = CalculateDropPosition(dropPosition);

            // Update the underlying data
            moveTile.X = finalPosition.X;
            moveTile.Y = finalPosition.Y;

            // Move the existing Canvas Image
            Canvas.SetLeft(sourceImage, finalPosition.X);
            Canvas.SetTop(sourceImage, finalPosition.Y);

            // Keep the properties panel in sync if it's currently selected
            if (_currentSelectedTile == moveTile)
            {
                Canvas.SetLeft(SelectionHighlight, finalPosition.X);
                Canvas.SetTop(SelectionHighlight, finalPosition.Y);

                _isUpdatingBoxes = true;
                EditXBox.Value = (decimal)finalPosition.X;
                EditYBox.Value = (decimal)finalPosition.Y;
                _isUpdatingBoxes = false;
            }
        }
        // B) Handle placing a completely new tile from the sidebar
        else if (e.Data.Contains("DraggedTexture") && e.Data.Get("DraggedTexture") is TextureItem item)
        {
            var dropPosition = e.GetPosition(MapCanvas);
            var finalPosition = CalculateDropPosition(dropPosition);

            // Decode at cell size (lower memory than 1024 for this use-case)
            Avalonia.Media.Imaging.Bitmap canvasBitmap;
            using (var stream = System.IO.File.OpenRead(item.HighResFilePath))
            {
                canvasBitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 1024);
            }

            // Create and store the data structure representation
            var placedTile = new PlacedTile
            {
                Texture = item,
                X = finalPosition.X,
                Y = finalPosition.Y
            };

            var mapImage = new Image
            {
                Source = canvasBitmap,
                Width = GridCellSize,
                Height = GridCellSize,
                Stretch = Stretch.Fill,
                Tag = placedTile
            };

            mapImage.PointerPressed += OnPlacedTilePointerPressed;
            mapImage.PointerMoved += OnPlacedTilePointerMoved;
            mapImage.PointerReleased += OnPlacedTilePointerReleased;

            Canvas.SetLeft(mapImage, finalPosition.X);
            Canvas.SetTop(mapImage, finalPosition.Y);

            MapCanvas.Children.Add(mapImage);
            PlacedTiles.Add(placedTile);
        }
    }

    private async void OnPlacedTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (sender is Image mapImage && mapImage.Tag is PlacedTile tile)
        {
            SelectTile(tile, mapImage);
            PlacedTilesList.SelectedItem = tile; // Sync with listbox selection
            
            // Record exactly where the mouse started the click instead of dragging immediately
            _tileDragStartPoint = e.GetPosition(MapCanvas);
            e.Handled = true;
        }
    }

    private async void OnPlacedTilePointerMoved(object? sender, PointerEventArgs e)
    {
        // If we haven't clicked a tile, or we are already dragging, do nothing
        if (_tileDragStartPoint == null || _isDraggingTile) return;

        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _tileDragStartPoint = null;
            return;
        }

        var currentPosition = e.GetPosition(MapCanvas);
        var distanceSq = Math.Pow(currentPosition.X - _tileDragStartPoint.Value.X, 2) + Math.Pow(currentPosition.Y - _tileDragStartPoint.Value.Y, 2);

        // Require at least a 3-pixel movement boundary (9 squared) before starting a drag
        if (distanceSq > 9)
        {
            if (sender is Image mapImage && mapImage.Tag is PlacedTile tile)
            {
                _isDraggingTile = true;

                var dragData = new DataObject();
                dragData.Set("MovePlacedTile", tile);
                dragData.Set("SourceImage", mapImage);

                await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);

                // Reset states after drag completes
                _isDraggingTile = false;
                _tileDragStartPoint = null;
            }
        }
    }

    private void OnPlacedTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Cancel the potential drag if they successfully let go without moving past the threshold
        _tileDragStartPoint = null;
    }

    private void OnPlacedTilesListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PlacedTilesList.SelectedItem is PlacedTile tile)
        {
            var mapImage = MapCanvas.Children.OfType<Image>().FirstOrDefault(img => img.Tag == tile);
            if (mapImage != null)
            {
                SelectTile(tile, mapImage);
            }
        }
        else
        {
            TileEditorPanel.IsVisible = false;
            _currentSelectedImage = null;
            _currentSelectedTile = null;
            SelectionHighlight.IsVisible = false;
        }
    }

    private void SelectTile(PlacedTile tile, Image mapImage)
    {
        _currentSelectedImage = mapImage;
        _currentSelectedTile = tile;

        _isUpdatingBoxes = true;

        TileEditorPanel.IsVisible = true;
        SelectedTileName.Text = tile.TxdName;
        SelectedTileYtd.Text = $"YTD: {tile.YtdName}";
        EditXBox.Value = (decimal)tile.X;
        EditYBox.Value = (decimal)tile.Y;
        SelectedTilePreview.Source = tile.Texture.Preview;

        // Update selection highlight position and size
        SelectionHighlight.Width = mapImage.Width;
        SelectionHighlight.Height = mapImage.Height;
        Canvas.SetLeft(SelectionHighlight, tile.X);
        Canvas.SetTop(SelectionHighlight, tile.Y);
        SelectionHighlight.IsVisible = true;

        _isUpdatingBoxes = false;
    }

    private void OnEditBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || _currentSelectedTile == null || _currentSelectedImage == null || !e.NewValue.HasValue) return;

        double newValue = (double)e.NewValue.Value;

        if (sender == EditXBox)
        {
            _currentSelectedTile.X = newValue;
            Canvas.SetLeft(_currentSelectedImage, newValue);
            Canvas.SetLeft(SelectionHighlight, newValue);
        }
        else if (sender == EditYBox)
        {
            _currentSelectedTile.Y = newValue;
            Canvas.SetTop(_currentSelectedImage, newValue);
            Canvas.SetTop(SelectionHighlight, newValue);
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