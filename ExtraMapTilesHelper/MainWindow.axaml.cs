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
            _defaultTilesManager.SetVisible(menuItem.IsChecked == true);
        }
    }

    private void OnToggleTileSnappingClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            _snappingEngine.IsSnappingEnabled = menuItem.IsChecked == true;
        }
    }

    private void ResetView() => _cameraController.ResetView();

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
        double zoomFactor = e.Delta.Y > 0 ? 1.15 : 0.85;
        _cameraController.ZoomAtViewportPoint(zoomFactor, e.GetPosition(MapScrollViewer));
        e.Handled = true;
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e) => _cameraController.ZoomAtViewportCenter(1.15);
    private void OnZoomOutClicked(object? sender, RoutedEventArgs e) => _cameraController.ZoomAtViewportCenter(0.85);

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (point.Properties.IsLeftButtonPressed)
        {
            PlacedTilesList.SelectedItem = null;
        }

        _cameraController.BeginPan(sender, e);
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e) => _cameraController.Pan(e);
    private void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e) => _cameraController.EndPan(e);

    public ObservableCollection<DictionaryItem> Dictionaries { get; } = new();
    public ObservableCollection<PlacedTile> PlacedTiles { get; } = new();

    private Image? _currentSelectedImage;
    private PlacedTile? _currentSelectedTile;
    private bool _isUpdatingBoxes = false;

    // --- NEW: Grid Tile Dragging Trackers ---
    private Point? _tileDragStartPoint;
    private bool _isDraggingTile = false;

    private readonly MapCameraController _cameraController;
    private readonly TileSnappingEngine _snappingEngine = new();
    private readonly DefaultTilesManager _defaultTilesManager = new();

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

        _cameraController = new MapCameraController(MapScrollViewer, MapZoomTransform);
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
        _snappingEngine.PrepareDrag(MapCanvas);

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
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("DraggedTexture") && !e.Data.Contains("MovePlacedTile"))
        {
            e.DragEffects = DragDropEffects.None;
            DragHighlight.IsVisible = false;
            return;
        }

        e.DragEffects = e.Data.Contains("MovePlacedTile") ? DragDropEffects.Move : DragDropEffects.Copy;
        var position = e.GetPosition(MapCanvas);

        if (!_snappingEngine.ShouldRecalculate(position))
        {
            e.Handled = true;
            return;
        }

        var snappedPosition = _snappingEngine.GetSnappedPosition(position, MapCanvas);

        if (_snappingEngine.IsSameSnappedPosition(snappedPosition))
        {
            e.Handled = true;
            return;
        }

        _snappingEngine.SetLastSnappedPosition(snappedPosition);

        Canvas.SetLeft(DragHighlight, snappedPosition.X);
        Canvas.SetTop(DragHighlight, snappedPosition.Y);
        if (!DragHighlight.IsVisible) DragHighlight.IsVisible = true;

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
            var finalPosition = _snappingEngine.GetSnappedPosition(dropPosition, MapCanvas);

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
            var finalPosition = _snappingEngine.GetSnappedPosition(dropPosition, MapCanvas);

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

    private void SetTilePositionFromCoordinates(PlacedTile tile, double x, double y)
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

    private void SetTilePositionFromOffsets(PlacedTile tile, double offsetX, double offsetY)
    {
        tile.OffsetX = offsetX;
        tile.OffsetY = offsetY;

        var coords = CoordinateMapper.OffsetsToCoordinates(offsetX, offsetY);
        tile.X = coords.X;
        tile.Y = coords.Y;

        var game = CoordinateMapper.CoordinatesToGame(coords.X, coords.Y);
        tile.GameX = game.X;
        tile.GameY = game.Y;
    }

    private void SetTilePositionFromGame(PlacedTile tile, double gameX, double gameY)
    {
        tile.GameX = gameX;
        tile.GameY = gameY;

        var coords = CoordinateMapper.GameToCoordinates(gameX, gameY);
        tile.X = coords.X;
        tile.Y = coords.Y;

        var offsets = CoordinateMapper.CoordinatesToOffsets(coords.X, coords.Y);
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

    private void OnResetViewClicked(object? sender, RoutedEventArgs e) => _cameraController.ResetView();
}