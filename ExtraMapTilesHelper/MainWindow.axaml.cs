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

    private const double OriginTileX = 49920.0;
    private const double OriginTileY = 49920.0;

    // Add near your existing origin constants
    private const double GameOriginX = -4140.0;
    private const double GameOriginY = 8400.0;
    private const double GameTileSize = 4500.0;

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

        Dictionaries.CollectionChanged += (s, e) => UpdateDictionaryCount();
        UpdateDictionaryCount();
    }

    private void UpdateDictionaryCount()
    {
        int dictCount = Dictionaries.Count;
        int texCount = Dictionaries.Sum(d => d.Textures.Count);

        Dispatcher.UIThread.Post(() =>
        {
            DictionaryCountText.Text = $"Texture Dictionaries ({dictCount})";
        });
    }

    private void OnCanvasDragEnter(object? sender, DragEventArgs e)
    {
        _lastSnappedPosition = new Point(-1000, -1000);
        _spatialHash.Clear();

        // --- NEW: Assign the visual preview source! ---
        if (e.Data.Contains("DragImage") && e.Data.Get("DragImage") is Avalonia.Media.Imaging.Bitmap bmp)
        {
            DragHighlightImage.Source = bmp;
        }
        else if (e.Data.Contains("SourceImage") && e.Data.Get("SourceImage") is Image sourceImg)
        {
            DragHighlightImage.Source = sourceImg.Source;
        }
        else
        {
            DragHighlightImage.Source = null;
        }

        foreach (var child in MapCanvas.Children)
        {
            if (child is Control ctrl && (ctrl.Tag is TextureItem || ctrl.Tag is PlacedTile))
            {
                double tileX = Canvas.GetLeft(ctrl);
                double tileY = Canvas.GetTop(ctrl);

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

        double maxX = (double.IsNaN(MapCanvas.Width) ? 100000 : MapCanvas.Width) - GridCellSize;
        double maxY = (double.IsNaN(MapCanvas.Height) ? 100000 : MapCanvas.Height) - GridCellSize;

        targetX = Math.Clamp(targetX, 0, maxX);
        targetY = Math.Clamp(targetY, 0, maxY);

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

        // Clamp to Canvas Map Bounds
        gridX = Math.Clamp(gridX, 0, maxX);
        gridY = Math.Clamp(gridY, 0, maxY);

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
                            if (sp.X < 0 || sp.Y < 0 || sp.X > maxX || sp.Y > maxY) continue; // Check bounds

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
                                {
                                    return bestSnap;
                                }
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
            DragHighlightImage.Source = null; // Clear memory reference
        }
    }

    // --- NEW: Status update helper ---
    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = message;
        });
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

        int total = files.Count;
        int current = 0;

        // Show status before work starts
        SetStatus($"Loading dictionaries ({current}/{total})");

        try
        {
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

                    current++;
                    SetStatus($"Loading dictionaries ({current}/{total})");
                }
            });

            // Delay setting the final status slightly to ensure it lands after the queued loading status updates
            Dispatcher.UIThread.Post(() =>
            {
                SetStatus($"Loaded {total} Dictionaries");
            });
        }
        catch (Exception ex)
        {
            SetStatus("Error loading dictionaries");
            // Optionally log ex
        }
        finally
        {
            ImportMenuItem.IsEnabled = true;
        }
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

        // --- NEW: Decode lower res for drag visual ---
        try
        {
            using var stream = System.IO.File.OpenRead(draggedItem.HighResFilePath);
            var dragPreview = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 256);
            dragData.Set("DragImage", dragPreview);
        }
        catch { }

        await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy);
    }

    // 2. Drop the tile on the grid
    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        // Hide the highlight immediately
        DragHighlight.IsVisible = false;
        DragHighlightImage.Source = null;

        // A) Handle repositioning an already placed tile
        if (e.Data.Contains("MovePlacedTile") && e.Data.Get("MovePlacedTile") is PlacedTile moveTile && e.Data.Get("SourceImage") is Image sourceImage)
        {
            var dropPosition = e.GetPosition(MapCanvas);
            var finalPosition = CalculateDropPosition(dropPosition);

            // Update both coordinate systems in one place
            SetTilePositionFromCoordinates(moveTile, finalPosition.X, finalPosition.Y);

            // Move the existing Canvas Image
            Canvas.SetLeft(sourceImage, moveTile.X);
            Canvas.SetTop(sourceImage, moveTile.Y);

            // Keep selection visuals + editor in sync
            if (_currentSelectedTile == moveTile)
            {
                Canvas.SetLeft(SelectionHighlight, moveTile.X);
                Canvas.SetTop(SelectionHighlight, moveTile.Y);
                UpdateCoordinateEditorUi(); // respects XY/Offset mode
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
                Texture = item
            };

            SetTilePositionFromCoordinates(placedTile, finalPosition.X, finalPosition.Y);

            var mapImage = new Image
            {
                Source = canvasBitmap,
                Width = GridCellSize,
                Height = GridCellSize,
                Stretch = Stretch.Fill,
                Tag = placedTile,
                ZIndex = 6
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
        // Cancel the potential drag if they successfully let go of the mouse button
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

        // Keep offsets in sync if tile came from older data
        SetTilePositionFromCoordinates(tile, tile.X, tile.Y);

        _isUpdatingBoxes = true;

        TileEditorPanel.IsVisible = true;
        SelectedTileName.Text = tile.TxdName;
        SelectedTileYtd.Text = $"YTD: {tile.YtdName}";
        SelectedTilePreview.Source = tile.Texture.Preview;

        SelectionHighlight.Width = mapImage.Width;
        SelectionHighlight.Height = mapImage.Height;
        Canvas.SetLeft(SelectionHighlight, tile.X);
        Canvas.SetTop(SelectionHighlight, tile.Y);
        SelectionHighlight.IsVisible = true;

        _isUpdatingBoxes = false;

        UpdateCoordinateEditorUi();
    }

    private void OnEditBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || _currentSelectedTile == null || _currentSelectedImage == null || !e.NewValue.HasValue) return;

        double newValue = (double)e.NewValue.Value;

        if (IsOffsetMode)
        {
            double offsetX = sender == EditXBox ? newValue : _currentSelectedTile.OffsetX;
            double offsetY = sender == EditYBox ? newValue : _currentSelectedTile.OffsetY;
            SetTilePositionFromOffsets(_currentSelectedTile, offsetX, offsetY);
        }
        else
        {
            double gameX = sender == EditXBox ? newValue : _currentSelectedTile.GameX;
            double gameY = sender == EditYBox ? newValue : _currentSelectedTile.GameY;
            SetTilePositionFromGame(_currentSelectedTile, gameX, gameY);
        }

        Canvas.SetLeft(_currentSelectedImage, _currentSelectedTile.X);
        Canvas.SetTop(_currentSelectedImage, _currentSelectedTile.Y);
        Canvas.SetLeft(SelectionHighlight, _currentSelectedTile.X);
        Canvas.SetTop(SelectionHighlight, _currentSelectedTile.Y);
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

    private void OnRemoveDictionaryClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not DictionaryItem dictionary)
        {
            return;
        }

        var placedTilesToRemove = PlacedTiles
            .Where(t => string.Equals(t.YtdName, dictionary.Name, StringComparison.Ordinal))
            .ToList();

        var imageTilesToRemove = MapCanvas.Children
            .OfType<Image>()
            .Where(img => img.Tag is PlacedTile tile &&
                          string.Equals(tile.YtdName, dictionary.Name, StringComparison.Ordinal))
            .ToList();

        foreach (var image in imageTilesToRemove)
        {
            MapCanvas.Children.Remove(image);
        }

        foreach (var placedTile in placedTilesToRemove)
        {
            PlacedTiles.Remove(placedTile);
        }

        if (_currentSelectedTile != null &&
            string.Equals(_currentSelectedTile.YtdName, dictionary.Name, StringComparison.Ordinal))
        {
            PlacedTilesList.SelectedItem = null;
            _currentSelectedImage = null;
            _currentSelectedTile = null;
            TileEditorPanel.IsVisible = false;
            SelectionHighlight.IsVisible = false;
        }

        Dictionaries.Remove(dictionary);
    }

    private bool IsOffsetMode => CoordinateModeToggle.IsChecked == true;

    private Point CoordinatesToOffsets(double x, double y)
    {
        return new Point(
            (x - OriginTileX) / GridCellSize,
            (y - OriginTileY) / GridCellSize);
    }

    private Point OffsetsToCoordinates(double offsetX, double offsetY)
    {
        return new Point(
            OriginTileX + (offsetX * GridCellSize),
            OriginTileY + (offsetY * GridCellSize));
    }

    private Point CoordinatesToGame(double x, double y)
    {
        var offsets = CoordinatesToOffsets(x, y);
        return new Point(
            GameOriginX + (offsets.X * GameTileSize),
            GameOriginY + (offsets.Y * GameTileSize));
    }

    private Point GameToCoordinates(double gameX, double gameY)
    {
        double offsetX = (gameX - GameOriginX) / GameTileSize;
        double offsetY = (gameY - GameOriginY) / GameTileSize;
        return OffsetsToCoordinates(offsetX, offsetY);
    }

    private void SetTilePositionFromCoordinates(PlacedTile tile, double x, double y)
    {
        tile.X = x;
        tile.Y = y;

        var offsets = CoordinatesToOffsets(x, y);
        tile.OffsetX = offsets.X;
        tile.OffsetY = offsets.Y;

        var game = CoordinatesToGame(x, y);
        tile.GameX = game.X;
        tile.GameY = game.Y;
    }

    private void SetTilePositionFromOffsets(PlacedTile tile, double offsetX, double offsetY)
    {
        tile.OffsetX = offsetX;
        tile.OffsetY = offsetY;

        var coords = OffsetsToCoordinates(offsetX, offsetY);
        tile.X = coords.X;
        tile.Y = coords.Y;

        var game = CoordinatesToGame(coords.X, coords.Y);
        tile.GameX = game.X;
        tile.GameY = game.Y;
    }

    private void SetTilePositionFromGame(PlacedTile tile, double gameX, double gameY)
    {
        tile.GameX = gameX;
        tile.GameY = gameY;

        var coords = GameToCoordinates(gameX, gameY);
        tile.X = coords.X;
        tile.Y = coords.Y;

        var offsets = CoordinatesToOffsets(coords.X, coords.Y);
        tile.OffsetX = offsets.X;
        tile.OffsetY = offsets.Y;
    }

    private void UpdateCoordinateEditorUi()
    {
        EditXLabel.Text = IsOffsetMode ? "Offset X:" : "Game X:";
        EditYLabel.Text = IsOffsetMode ? "Offset Y:" : "Game Y:";

        if (_currentSelectedTile == null) return;

        _isUpdatingBoxes = true;
        if (IsOffsetMode)
        {
            EditXBox.Value = (decimal)_currentSelectedTile.OffsetX;
            EditYBox.Value = (decimal)_currentSelectedTile.OffsetY;
            EditXBox.Increment = 1.0m;
            EditYBox.Increment = 1.0m;
        }
        else
        {
            EditXBox.Value = (decimal)_currentSelectedTile.GameX;
            EditYBox.Value = (decimal)_currentSelectedTile.GameY;
            EditXBox.Increment = 4500.0m;
            EditYBox.Increment = 4500.0m;
        }
        _isUpdatingBoxes = false;
    }

    private void OnCoordinateModeToggled(object? sender, RoutedEventArgs e)
    {
        UpdateCoordinateEditorUi();
    }
}