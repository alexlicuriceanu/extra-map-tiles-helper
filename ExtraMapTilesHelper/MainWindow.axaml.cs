using Avalonia;
using Avalonia.Controls;
using Avalonia.Input; // Required for DragDrop
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
    private double _zoomLevel = 0.25;

    private void OnMainWindowLoaded(object? sender, RoutedEventArgs e)
    {
        // Apply the initial zoom right when the app starts
        MapZoomTransform.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        MapScrollViewer.UpdateLayout();

        // Calculate the center based on the NEW scaled size of the canvas
        double scaledWidth = MapCanvas.Width * _zoomLevel;
        double scaledHeight = MapCanvas.Height * _zoomLevel;

        double centerX = (scaledWidth - MapScrollViewer.Viewport.Width) / 2;
        double centerY = (scaledHeight - MapScrollViewer.Viewport.Height) / 2;

        MapScrollViewer.Offset = new Avalonia.Vector(centerX, centerY);
    }

    // 2. Zoom to Mouse Cursor
    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // 3. SET ZOOM SPEED
        // 1.10 = 10% zoom per tick (slower/smoother)
        // 1.25 = 25% zoom per tick (faster/snappier)
        double zoomInSpeed = 1.15;
        double zoomOutSpeed = 0.85;

        double zoomFactor = e.Delta.Y > 0 ? zoomInSpeed : zoomOutSpeed;

        // 2. CLAMP THE ZOOM (Prevents the grid from disappearing)
        // First number is minimum zoom out (0.2 = 20%). 
        // Second number is maximum zoom in (5.0 = 500%).
        double minZoom = 0.13; // Don't go below 0.15 or the grid renderer crashes!
        double maxZoom = 5.0;

        double newZoom = Math.Clamp(_zoomLevel * zoomFactor, minZoom, maxZoom);

        if (newZoom == _zoomLevel) return;

        // Get the exact mouse position relative to the scroll viewer
        var mousePos = e.GetPosition(MapScrollViewer);
        var scrollOffset = MapScrollViewer.Offset;

        // Calculate where the mouse is looking on the absolute map before the zoom
        double absoluteX = (scrollOffset.X + mousePos.X) / _zoomLevel;
        double absoluteY = (scrollOffset.Y + mousePos.Y) / _zoomLevel;

        // Apply the new zoom level
        _zoomLevel = newZoom;
        MapZoomTransform.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);

        // Force Avalonia to recalculate the canvas size with the new zoom
        MapScrollViewer.UpdateLayout();

        // Adjust the scrollbars so the absolute coordinate stays exactly under the mouse cursor
        double newOffsetX = (absoluteX * _zoomLevel) - mousePos.X;
        double newOffsetY = (absoluteY * _zoomLevel) - mousePos.Y;

        MapScrollViewer.Offset = new Avalonia.Vector(newOffsetX, newOffsetY);

        e.Handled = true;
    }

    public MainWindow()
    {
        InitializeComponent();
        TextureList.ItemsSource = Textures;

        // Listen for items being dropped on the canvas
        AddHandler(DragDrop.DropEvent, OnCanvasDrop);
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

        ImportButton.IsEnabled = false;
        ImportButton.Content = "Extracting...";

        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                string dictName = System.IO.Path.GetFileNameWithoutExtension(file.Path.LocalPath);

                Dispatcher.UIThread.Invoke(() =>
                {
                    var oldItems = Textures.Where(t => t.DictionaryName == dictName).ToList();
                    foreach (var item in oldItems) Textures.Remove(item);
                });

                var extractedTextures = _ytdService.ExtractTextures(file.Path.LocalPath);
                foreach (var tex in extractedTextures)
                {
                    Dispatcher.UIThread.Post(() => Textures.Add(tex));
                }
            }
        });

        ImportButton.IsEnabled = true;
        ImportButton.Content = "Import YTDs";
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
        if (e.Data.Contains("DraggedTexture") && e.Data.Get("DraggedTexture") is TextureItem item)
        {
            var dropPosition = e.GetPosition(MapCanvas);

            // Load the full 8K image from disk
            var highResBitmap = new Avalonia.Media.Imaging.Bitmap(item.HighResFilePath);

            var mapImage = new Image
            {
                Source = highResBitmap,
                Width = item.Width,
                Height = item.Height,
                Tag = item // Store the data so we can click on it later!
            };

            // Place it exactly where the mouse was released
            Canvas.SetLeft(mapImage, dropPosition.X);
            Canvas.SetTop(mapImage, dropPosition.Y);

            MapCanvas.Children.Add(mapImage);
        }
    }
}