using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input; // Required for DragDrop
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using ExtraMapTilesHelper.Models;
using ExtraMapTilesHelper.Services;

namespace ExtraMapTilesHelper;

public partial class MainWindow : Window
{
    public ObservableCollection<TextureItem> Textures { get; } = new();
    private readonly YtdService _ytdService = new();

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