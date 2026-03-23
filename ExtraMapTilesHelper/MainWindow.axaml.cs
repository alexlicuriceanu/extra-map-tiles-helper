using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ExtraMapTilesHelper.Controllers;
using ExtraMapTilesHelper.Models;
using ExtraMapTilesHelper.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ExtraMapTilesHelper;

public partial class MainWindow : Window
{
    public ObservableCollection<TextureItem> Textures { get; } = new();
    public ObservableCollection<DictionaryItem> Dictionaries { get; } = new();
    public ObservableCollection<PlacedTileItem> PlacedTiles { get; } = new();

    private readonly TileSnappingEngine _snappingEngine = new();
    private readonly DefaultTiles _defaultTiles = new();
    private readonly Camera _camera;
    private readonly TilePositionHelper _tilePositionHelper = new();
    private readonly SelectionController _selectionController;
    private readonly ProjectController _projectController;

    private bool _isUpdatingBoxes;
    private Point? _tileDragStartPoint;
    private bool _isDraggingTile;

    public MainWindow()
    {
        InitializeComponent();

        _camera = new Camera(MapScrollViewer, MapZoomTransform);
        _selectionController = new SelectionController(_tilePositionHelper);
        _projectController = new ProjectController(new YtdService());

        _selectionController.SelectionChanged += OnSelectionChanged;
        _selectionController.TilePositionUpdated += OnTilePositionUpdated;

        TextureTree.ItemsSource = Dictionaries;
        PlacedTilesList.ItemsSource = PlacedTiles;

        AddHandler(DragDrop.DropEvent, OnCanvasDrop);
        AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnCanvasDragLeave);
        AddHandler(DragDrop.DragEnterEvent, OnCanvasDragEnter);

        Dictionaries.CollectionChanged += (s, e) => UpdateDictionaryCount();
        PlacedTiles.CollectionChanged += OnPlacedTilesCollectionChanged;
        UpdateDictionaryCount();
    }

    private void OnMainWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            double scaling = screen.Scaling;
            Width = screen.WorkingArea.Width / scaling * 0.8;
            Height = screen.WorkingArea.Height / scaling * 0.8;
        }

        _camera.ResetView();
        _defaultTiles.LoadTiles(MapCanvas);
    }

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = message);
    }

    private void UpdateDictionaryCount()
    {
        Dispatcher.UIThread.Post(() =>
        {
            DictionaryCountText.Text = $"Texture Dictionaries ({Dictionaries.Count})";
        });
    }

    private void UpdatePlacedTilesIds()
    {
        for (int i = 0; i < PlacedTiles.Count; i++)
        {
            PlacedTiles[i].ConfigId = i + 1; // +1 matching Lua output index
        }

        if (_selectionController?.CurrentTile != null)
        {
            SelectedTileName.Text = $"{_selectionController.CurrentTile.TxdName} (ID: {_selectionController.CurrentTile.ConfigId})";
        }
    }

    private void OnToggleDefaultTilesClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
            _defaultTiles.SetVisible(menuItem.IsChecked == true);
    }

    private void OnToggleTileSnappingClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
            _snappingEngine.IsSnappingEnabled = menuItem.IsChecked == true;
    }

    private void OnResetViewClicked(object? sender, RoutedEventArgs e) => _camera.ResetView();
    private void OnCoordinateModeToggled(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingBoxes) return;

        if (_selectionController.CurrentTile is { } tile)
            tile.IsOffsetMode = CoordinateModeToggle.IsChecked == true;

        UpdateCoordinateEditorUi();
    }

    private void OnVisibleToggled(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingBoxes) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        tile.IsVisible = VisibleToggle.IsChecked == true;
        UpdateTileVisualState(tile, image);
    }

    private void UpdateTileOpacity(PlacedTileItem tile, Image image)
    {
        double baseOpacity = tile.Alpha / 100.0;
        image.Opacity = tile.IsVisible ? baseOpacity : baseOpacity * 0.1;
    }

    private void OnCenteredToggled(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingBoxes) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        double halfW = image.Width / 2.0;
        double halfH = image.Height / 2.0;

        bool centered = IsCenteredMode;
        tile.Centered = centered;

        double newX = centered ? tile.X - halfW : tile.X + halfW;
        double newY = centered ? tile.Y - halfH : tile.Y + halfH;

        _tilePositionHelper.UpdateFromCoordinates(tile, newX, newY);
        ApplyTileRotation(image, tile);
        OnTilePositionUpdated(tile);
    }

    private void OnRemoveTileClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectionController.CurrentTile is { } tile && _selectionController.CurrentImage is { } image)
        {
            MapCanvas.Children.Remove(image);
            PlacedTiles.Remove(tile);
            _selectionController.ClearSelection();
        }
    }

    private void OnResetTileClicked(object? sender, RoutedEventArgs e)
    {
        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        // Reset Alpha and Visibility
        tile.Alpha = 100.0;
        tile.IsVisible = true;
        UpdateTileVisualState(tile, image);

        // Reset Rotation
        tile.RotationDegrees = 0;
        ApplyTileRotation(image, tile);

        // Reset Scale
        tile.ScaleX = 1.0;
        tile.ScaleY = 1.0;

        double newWidth = CoordinateMapper.CanvasTileSize;
        double newHeight = CoordinateMapper.CanvasTileSize;

        image.Width = newWidth;
        image.Height = newHeight;

        // Reset position to offsets (0, 0)
        var resetAnchor = CoordinateMapper.OffsetsToCoordinates(0, 0);
        double newX = tile.Centered ? resetAnchor.X - (newWidth / 2.0) : resetAnchor.X;
        double newY = tile.Centered ? resetAnchor.Y - (newHeight / 2.0) : resetAnchor.Y;

        _tilePositionHelper.UpdateFromCoordinates(tile, newX, newY);

        Canvas.SetLeft(image, tile.X);
        Canvas.SetTop(image, tile.Y);

        SelectionHighlight.Width = image.Width;
        SelectionHighlight.Height = image.Height;
        Canvas.SetLeft(SelectionHighlight, tile.X);
        Canvas.SetTop(SelectionHighlight, tile.Y);

        UpdateCoordinateEditorUi();
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        await _projectController.ImportYtdsAsync(
            StorageProvider,
            Dictionaries,
            SetStatus,
            enabled => ImportMenuItem.IsEnabled = enabled);
    }

    private async void OnExportConfigClicked(object? sender, RoutedEventArgs e)
    {
        if (PlacedTiles.Count == 0)
        {
            SetStatus("No tiles to export");
            return;
        }

        var saveFileOptions = new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export Lua Config",
            DefaultExtension = "lua",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Lua files")
                {
                    Patterns = new[] { "*.lua" }
                }
            }
        };

        var selectedFile = await StorageProvider.SaveFilePickerAsync(saveFileOptions);

        if (selectedFile != null)
        {
            try
            {
                var configService = new LuaConfigService();
                string luaContent = configService.GenerateLuaConfig(PlacedTiles);

                using (var stream = await selectedFile.OpenWriteAsync())
                using (var writer = new System.IO.StreamWriter(stream))
                {
                    await writer.WriteAsync(luaContent);
                }

                SetStatus($"Successfully exported config to '{selectedFile.Path.LocalPath}'"); // NEW: using LocalPath strips off file://
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to export config: {ex.Message}");
            }
        }
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnRemoveDictionaryClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not DictionaryItem dictionary) return;

        _projectController.RemoveDictionary(
            dictionary,
            Dictionaries,
            PlacedTiles,
            MapCanvas,
            _selectionController.CurrentTile,
            _selectionController.ClearSelection);

        PlacedTilesList.SelectedItem = null;
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (point.Properties.IsLeftButtonPressed)
        {
            PlacedTilesList.SelectedItem = null;
            _selectionController.ClearSelection();
        }

        _camera.BeginPan(sender, e);
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e) => _camera.Pan(e);
    private void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e) => _camera.EndPan(e);

    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double zoomFactor = e.Delta.Y > 0 ? 1.15 : 0.85;
        _camera.ZoomAtViewportPoint(zoomFactor, e.GetPosition(MapScrollViewer));
        e.Handled = true;
    }

    private async void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed) return;

        if ((sender as Control)?.DataContext is not TextureItem draggedItem) return;

        var dragData = new DataObject();
        dragData.Set("DraggedTexture", draggedItem);

        try
        {
            using var stream = System.IO.File.OpenRead(draggedItem.HighResFilePath);
            var dragPreview = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 256);
            dragData.Set("DragImage", dragPreview);
        }
        catch { }

        await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy);
    }

    private void OnCanvasDragEnter(object? sender, DragEventArgs e)
    {
        _snappingEngine.PrepareDrag(MapCanvas);

        if (e.Data.Contains("DragImage") && e.Data.Get("DragImage") is Avalonia.Media.Imaging.Bitmap bmp)
            DragHighlightImage.Source = bmp;
        else if (e.Data.Contains("SourceImage") && e.Data.Get("SourceImage") is Image sourceImg)
            DragHighlightImage.Source = sourceImg.Source;
        else
            DragHighlightImage.Source = null;

        if (e.Data.Contains("SourceImage") && e.Data.Get("SourceImage") is Image draggedSourceImage)
        {
            DragHighlight.Width = draggedSourceImage.Width;
            DragHighlight.Height = draggedSourceImage.Height;
        }
        else
        {
            DragHighlight.Width = CoordinateMapper.CanvasTileSize;
            DragHighlight.Height = CoordinateMapper.CanvasTileSize;
        }

        if (e.Data.Contains("MovePlacedTile") && e.Data.Get("MovePlacedTile") is PlacedTileItem moveTile)
        {
            var origin = moveTile.Centered
                ? new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
                : new RelativePoint(0.0, 0.0, RelativeUnit.Relative);

            DragHighlight.RenderTransformOrigin = origin;
            DragHighlight.RenderTransform = new RotateTransform(moveTile.RotationDegrees);
        }
        else
        {
            DragHighlight.RenderTransform = null;
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

        double dragW = CoordinateMapper.CanvasTileSize;
        double dragH = CoordinateMapper.CanvasTileSize;
        int dragRotation = 0;
        RelativePoint? dragOrigin = null;

        if (e.Data.Contains("SourceImage") && e.Data.Get("SourceImage") is Image sourceImage)
        {
            dragW = sourceImage.Width;
            dragH = sourceImage.Height;
            dragOrigin = sourceImage.RenderTransformOrigin;
        }

        if (e.Data.Contains("MovePlacedTile") && e.Data.Get("MovePlacedTile") is PlacedTileItem mt)
        {
            dragRotation = mt.RotationDegrees;
        }

        Point? dragOffset = e.Data.Contains("DragOffset") ? (Point?)e.Data.Get("DragOffset") : null;
        var snappedPosition = _snappingEngine.GetSnappedPosition(position, MapCanvas, dragW, dragH, dragOffset, dragRotation, dragOrigin);

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
        if (e.Source == MapCanvas)
        {
            DragHighlight.IsVisible = false;
            DragHighlightImage.Source = null;
        }
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        DragHighlight.IsVisible = false;
        DragHighlightImage.Source = null;

        if (e.Data.Contains("MovePlacedTile") && e.Data.Get("MovePlacedTile") is PlacedTileItem moveTile && e.Data.Get("SourceImage") is Image sourceImage)
        {
            var dropPosition = e.GetPosition(MapCanvas);
            Point? dragOffset = e.Data.Contains("DragOffset") ? (Point?)e.Data.Get("DragOffset") : null;
            var finalPosition = _snappingEngine.GetSnappedPosition(dropPosition, MapCanvas, sourceImage.Width, sourceImage.Height, dragOffset, moveTile.RotationDegrees, sourceImage.RenderTransformOrigin);

            _tilePositionHelper.UpdateFromCoordinates(moveTile, finalPosition.X, finalPosition.Y);
            Canvas.SetLeft(sourceImage, moveTile.X);
            Canvas.SetTop(sourceImage, moveTile.Y);

            if (_selectionController.CurrentTile == moveTile)
                OnTilePositionUpdated(moveTile);
        }
        else if (e.Data.Contains("DraggedTexture") && e.Data.Get("DraggedTexture") is TextureItem item)
        {
            var dropPosition = e.GetPosition(MapCanvas);
            var finalPosition = _snappingEngine.GetSnappedPosition(
                dropPosition,
                MapCanvas,
                CoordinateMapper.CanvasTileSize,
                CoordinateMapper.CanvasTileSize);

            Avalonia.Media.Imaging.Bitmap canvasBitmap;
            using (var stream = System.IO.File.OpenRead(item.HighResFilePath))
            {
                canvasBitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 1024);
            }

            var placedTile = new PlacedTileItem { Texture = item, Centered = IsCenteredMode, IsOffsetMode = IsOffsetMode };
            _tilePositionHelper.UpdateFromCoordinates(placedTile, finalPosition.X, finalPosition.Y);

            var mapImage = new Image
            {
                Source = canvasBitmap,
                Width = CoordinateMapper.CanvasTileSize * placedTile.ScaleX,
                Height = CoordinateMapper.CanvasTileSize * placedTile.ScaleY,
                Stretch = Stretch.Fill,
                Tag = placedTile,
                ZIndex = 6
            };

            UpdateTileVisualState(placedTile, mapImage);

            mapImage.PointerPressed += OnPlacedTilePointerPressed;
            mapImage.PointerMoved += OnPlacedTilePointerMoved;
            mapImage.PointerReleased += OnPlacedTilePointerReleased;

            Canvas.SetLeft(mapImage, finalPosition.X);
            Canvas.SetTop(mapImage, finalPosition.Y);

            MapCanvas.Children.Add(mapImage);
            PlacedTiles.Add(placedTile);

            // NEW: Automatically select the newly placed tile
            _selectionController.SelectTile(placedTile, mapImage);
            PlacedTilesList.SelectedItem = placedTile;
        }
    }

    private async void OnPlacedTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (sender is Image mapImage && mapImage.Tag is PlacedTileItem tile)
        {
            _selectionController.SelectTile(tile, mapImage);
            PlacedTilesList.SelectedItem = tile;
            _tileDragStartPoint = e.GetPosition(MapCanvas);
            e.Handled = true;
        }

        await Task.CompletedTask;
    }

    private async void OnPlacedTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tileDragStartPoint == null || _isDraggingTile) return;

        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _tileDragStartPoint = null;
            return;
        }

        var currentPosition = e.GetPosition(MapCanvas);
        var distanceSq = Math.Pow(currentPosition.X - _tileDragStartPoint.Value.X, 2) +
                         Math.Pow(currentPosition.Y - _tileDragStartPoint.Value.Y, 2);

        if (distanceSq > 9 && sender is Image mapImage && mapImage.Tag is PlacedTileItem tile)
        {
            _isDraggingTile = true;

            var dragData = new DataObject();
            dragData.Set("MovePlacedTile", tile);
            dragData.Set("SourceImage", mapImage);

            var dragOffset = new Point(_tileDragStartPoint.Value.X - tile.X, _tileDragStartPoint.Value.Y - tile.Y);
            dragData.Set("DragOffset", dragOffset);

            await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);

            _isDraggingTile = false;
            _tileDragStartPoint = null;
        }
    }

    private void OnPlacedTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _tileDragStartPoint = null;
    }

    private void OnPlacedTilesListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PlacedTilesList.SelectedItem is not PlacedTileItem tile)
        {
            _selectionController.ClearSelection();
            return;
        }

        var mapImage = MapCanvas.Children.OfType<Image>().FirstOrDefault(img => img.Tag == tile);
        if (mapImage != null)
            _selectionController.SelectTile(tile, mapImage);
    }

    private void OnSelectionChanged(PlacedTileItem? tile, Image? image)
    {
        if (tile == null || image == null)
        {
            TileEditorPanel.IsVisible = false;
            SelectionHighlight.IsVisible = false;
            return;
        }

        _isUpdatingBoxes = true;

        TileEditorPanel.IsVisible = true;
        SelectedTileName.Text = $"{tile.TxdName} (ID: {tile.ConfigId})"; // NEW
        SelectedTileYtd.Text = $"YTD: {tile.YtdName}";
        SelectedTilePreview.Source = tile.Texture.Preview;

        SelectionHighlight.Width = image.Width;
        SelectionHighlight.Height = image.Height;
        Canvas.SetLeft(SelectionHighlight, tile.X);
        Canvas.SetTop(SelectionHighlight, tile.Y);
        SelectionHighlight.IsVisible = true;

        ApplyTileRotation(image, tile);

        _isUpdatingBoxes = false;

        UpdateCoordinateEditorUi();
    }

    private void OnTilePositionUpdated(PlacedTileItem tile)
    {
        var selectedImage = _selectionController.CurrentImage;
        if (selectedImage == null) return;

        Canvas.SetLeft(selectedImage, tile.X);
        Canvas.SetTop(selectedImage, tile.Y);

        Canvas.SetLeft(SelectionHighlight, tile.X);
        Canvas.SetTop(SelectionHighlight, tile.Y);

        ApplyTileRotation(selectedImage, tile);
        UpdateCoordinateEditorUi();
    }

    private void OnEditBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || !e.NewValue.HasValue) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        if (!tile.Centered)
        {
            _selectionController.UpdateTilePosition(
                (double)e.NewValue.Value,
                isX: sender == EditXBox,
                isOffsetMode: tile.IsOffsetMode);
            return;
        }

        double halfW = image.Width / 2.0;
        double halfH = image.Height / 2.0;

        double anchorX = tile.X + halfW;
        double anchorY = tile.Y + halfH;

        if (tile.IsOffsetMode)
        {
            var currentAnchorOffsets = CoordinateMapper.CoordinatesToOffsets(anchorX, anchorY);

            double newOx = sender == EditXBox ? (double)e.NewValue.Value : currentAnchorOffsets.X;
            double newOy = sender == EditYBox ? (double)e.NewValue.Value : currentAnchorOffsets.Y;

            var newAnchor = CoordinateMapper.OffsetsToCoordinates(newOx, newOy);
            _tilePositionHelper.UpdateFromCoordinates(tile, newAnchor.X - halfW, newAnchor.Y - halfH);
        }
        else
        {
            var currentAnchorGame = CoordinateMapper.CoordinatesToGame(anchorX, anchorY);

            double newGx = sender == EditXBox ? (double)e.NewValue.Value : currentAnchorGame.X;
            double newGy = sender == EditYBox ? (double)e.NewValue.Value : currentAnchorGame.Y;

            var newAnchor = CoordinateMapper.GameToCoordinates(newGx, newGy);
            _tilePositionHelper.UpdateFromCoordinates(tile, newAnchor.X - halfW, newAnchor.Y - halfH);
        }

        OnTilePositionUpdated(tile);
    }

    private void OnEditAlphaBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || !e.NewValue.HasValue) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;

        if (tile != null && image != null)
        {
            tile.Alpha = (double)e.NewValue.Value;
            UpdateTileVisualState(tile, image);
        }
    }

    private void OnEditScaleBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || !e.NewValue.HasValue) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        var value = Math.Clamp((double)e.NewValue.Value, 0.1, 10.0);

        double oldWidth = image.Width;
        double oldHeight = image.Height;

        // Anchor point before scaling
        double anchorX = tile.Centered ? tile.X + (oldWidth / 2.0) : tile.X;
        double anchorY = tile.Centered ? tile.Y + (oldHeight / 2.0) : tile.Y;

        if (sender == EditScaleXBox)
            tile.ScaleX = value;
        else
            tile.ScaleY = value;

        double newWidth = CoordinateMapper.CanvasTileSize * tile.ScaleX;
        double newHeight = CoordinateMapper.CanvasTileSize * tile.ScaleY;

        image.Width = newWidth;
        image.Height = newHeight;

        // Recompute top-left from anchor mode
        double newX = tile.Centered ? anchorX - (newWidth / 2.0) : anchorX;
        double newY = tile.Centered ? anchorY - (newHeight / 2.0) : anchorY;

        _tilePositionHelper.UpdateFromCoordinates(tile, newX, newY);

        Canvas.SetLeft(image, tile.X);
        Canvas.SetTop(image, tile.Y);

        SelectionHighlight.Width = image.Width;
        SelectionHighlight.Height = image.Height;
        Canvas.SetLeft(SelectionHighlight, tile.X);
        Canvas.SetTop(SelectionHighlight, tile.Y);

        UpdateCoordinateEditorUi();
    }

    private void OnDictionaryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (sender is Control control && control.DataContext is DictionaryItem dict)
        {
            dict.IsExpanded = !dict.IsExpanded;
            e.Handled = true;
        }
    }

    private static int NormalizeDegrees(int value)
    {
        int normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private void ApplyTileRotation(Image image, PlacedTileItem tile)
    {
        var origin = tile.Centered
            ? new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
            : new RelativePoint(0.0, 0.0, RelativeUnit.Relative);

        image.RenderTransformOrigin = origin;
        image.RenderTransform = new RotateTransform(tile.RotationDegrees);

        if (_selectionController?.CurrentTile == tile)
        {
            SelectionHighlight.RenderTransformOrigin = origin;
            SelectionHighlight.RenderTransform = new RotateTransform(tile.RotationDegrees);
        }
    }

    private bool IsOffsetMode => CoordinateModeToggle.IsChecked == true;
    private bool IsCenteredMode => CenteredToggle.IsChecked == true;

    private void UpdateCoordinateEditorUi()
    {
        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        _isUpdatingBoxes = true;

        VisibleToggle.IsChecked = tile.IsVisible;
        CenteredToggle.IsChecked = tile.Centered;
        CoordinateModeToggle.IsChecked = tile.IsOffsetMode;

        EditXLabel.Text = tile.IsOffsetMode ? "Offset X:" : "Game X:";
        EditYLabel.Text = tile.IsOffsetMode ? "Offset Y:" : "Game Y:";

        double anchorX = tile.X;
        double anchorY = tile.Y;

        if (tile.Centered)
        {
            anchorX += image.Width / 2.0;
            anchorY += image.Height / 2.0;
        }

        if (tile.IsOffsetMode)
        {
            var offsets = CoordinateMapper.CoordinatesToOffsets(anchorX, anchorY);
            EditXBox.Value = (decimal)offsets.X;
            EditYBox.Value = (decimal)offsets.Y;
            EditXBox.Increment = 1.0m;
            EditYBox.Increment = 1.0m;
        }
        else
        {
            var game = CoordinateMapper.CoordinatesToGame(anchorX, anchorY);
            EditXBox.Value = (decimal)game.X;
            EditYBox.Value = (decimal)game.Y;
            EditXBox.Increment = 4500.0m;
            EditYBox.Increment = 4500.0m;
        }

        EditAlphaBox.Value = (decimal)tile.Alpha;
        EditScaleXBox.Value = (decimal)tile.ScaleX;
        EditScaleYBox.Value = (decimal)tile.ScaleY;
        EditRotationBox.Value = tile.RotationDegrees;

        _isUpdatingBoxes = false;
    }

    private void OnEditRotationBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || !e.NewValue.HasValue) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        int raw = (int)Math.Round((double)e.NewValue.Value);
        int normalized = NormalizeDegrees(raw);

        tile.RotationDegrees = normalized;
        ApplyTileRotation(image, tile);

        _isUpdatingBoxes = true;
        EditRotationBox.Value = normalized;
        _isUpdatingBoxes = false;
    }

    private void OnPlacedTilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdatePlacedTilesIds();
    }

    private void UpdateTileVisualState(PlacedTileItem tile, Image image)
    {
        UpdateTileOpacity(tile, image);
    }
}