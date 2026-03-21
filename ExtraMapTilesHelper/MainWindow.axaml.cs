using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ExtraMapTilesHelper.Controllers;
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
    private void OnCoordinateModeToggled(object? sender, RoutedEventArgs e) => UpdateCoordinateEditorUi();

    private void OnRemoveTileClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectionController.CurrentTile is { } tile && _selectionController.CurrentImage is { } image)
        {
            MapCanvas.Children.Remove(image);
            PlacedTiles.Remove(tile);
            _selectionController.ClearSelection();
        }
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        await _projectController.ImportYtdsAsync(
            StorageProvider,
            Dictionaries,
            SetStatus,
            enabled => ImportMenuItem.IsEnabled = enabled);
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

        if (e.Data.Contains("SourceImage") && e.Data.Get("SourceImage") is Image sourceImage)
        {
            dragW = sourceImage.Width;
            dragH = sourceImage.Height;
        }

        var snappedPosition = _snappingEngine.GetSnappedPosition(position, MapCanvas, dragW, dragH);

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
            var finalPosition = _snappingEngine.GetSnappedPosition(dropPosition, MapCanvas, sourceImage.Width, sourceImage.Height);

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

            var placedTile = new PlacedTileItem { Texture = item };
            _tilePositionHelper.UpdateFromCoordinates(placedTile, finalPosition.X, finalPosition.Y);

            var mapImage = new Image
            {
                Source = canvasBitmap,
                Width = CoordinateMapper.CanvasTileSize * placedTile.ScaleX,
                Height = CoordinateMapper.CanvasTileSize * placedTile.ScaleY,
                Stretch = Stretch.Fill,
                Tag = placedTile,
                ZIndex = 6,
                Opacity = placedTile.Alpha / 100.0
            };

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
        SelectedTileName.Text = tile.TxdName;
        SelectedTileYtd.Text = $"YTD: {tile.YtdName}";
        SelectedTilePreview.Source = tile.Texture.Preview;

        SelectionHighlight.Width = image.Width;
        SelectionHighlight.Height = image.Height;
        Canvas.SetLeft(SelectionHighlight, tile.X);
        Canvas.SetTop(SelectionHighlight, tile.Y);
        SelectionHighlight.IsVisible = true;

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

        UpdateCoordinateEditorUi();
    }

    private void OnEditBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || !e.NewValue.HasValue) return;

        _selectionController.UpdateTilePosition(
            (double)e.NewValue.Value,
            isX: sender == EditXBox,
            isOffsetMode: IsOffsetMode);
    }

    private void OnEditAlphaBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || !e.NewValue.HasValue) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;

        if (tile != null && image != null)
        {
            tile.Alpha = (double)e.NewValue.Value;
            image.Opacity = tile.Alpha / 100.0;
        }
    }

    private void OnEditScaleBoxValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingBoxes || !e.NewValue.HasValue) return;

        var tile = _selectionController.CurrentTile;
        var image = _selectionController.CurrentImage;
        if (tile == null || image == null) return;

        var value = Math.Clamp((double)e.NewValue.Value, 0.1, 10.0);

        if (sender == EditScaleXBox)
            tile.ScaleX = value;
        else
            tile.ScaleY = value;

        image.Width = CoordinateMapper.CanvasTileSize * tile.ScaleX;
        image.Height = CoordinateMapper.CanvasTileSize * tile.ScaleY;

        SelectionHighlight.Width = image.Width;
        SelectionHighlight.Height = image.Height;
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

    private bool IsOffsetMode => CoordinateModeToggle.IsChecked == true;

    private void UpdateCoordinateEditorUi()
    {
        EditXLabel.Text = IsOffsetMode ? "Offset X:" : "Game X:";
        EditYLabel.Text = IsOffsetMode ? "Offset Y:" : "Game Y:";

        var tile = _selectionController.CurrentTile;
        if (tile == null) return;

        _isUpdatingBoxes = true;

        if (IsOffsetMode)
        {
            EditXBox.Value = (decimal)tile.OffsetX;
            EditYBox.Value = (decimal)tile.OffsetY;
            EditXBox.Increment = 1.0m;
            EditYBox.Increment = 1.0m;
        }
        else
        {
            EditXBox.Value = (decimal)tile.GameX;
            EditYBox.Value = (decimal)tile.GameY;
            EditXBox.Increment = 4500.0m;
            EditYBox.Increment = 4500.0m;
        }

        EditAlphaBox.Value = (decimal)tile.Alpha;
        EditScaleXBox.Value = (decimal)tile.ScaleX;
        EditScaleYBox.Value = (decimal)tile.ScaleY;

        _isUpdatingBoxes = false;
    }
}