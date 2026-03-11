using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ExtraMapTilesHelper.Models;
using ExtraMapTilesHelper.Services;

namespace ExtraMapTilesHelper;

public partial class MainWindow : Window
{
    // This collection automatically tells the UI to update when items are added
    public ObservableCollection<TextureItem> Textures { get; } = new();
    private readonly YtdService _ytdService = new();

    public MainWindow()
    {
        InitializeComponent();

        // Bind the ListBox in the XAML to our ObservableCollection
        TextureList.ItemsSource = Textures;
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        // 1. Open Native File Picker (100% thread-safe in Avalonia)
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select YTD Files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Texture Dictionary") { Patterns = new[] { "*.ytd" } }
            }
        });

        if (files.Count == 0) return;

        // Disable button to prevent double clicks
        ImportButton.IsEnabled = false;
        ImportButton.Content = "Working...";

        // 2. Push the heavy lifting to a background thread
        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                // .LocalPath converts the file URI into a standard C:\... path
                var extractedTextures = _ytdService.ExtractTextures(file.Path.LocalPath);

                foreach (var tex in extractedTextures)
                {
                    // 3. Safely pass the texture back to the UI thread!
                    Dispatcher.UIThread.Post(() => Textures.Add(tex));
                }
            }
        });

        // Re-enable the button when done
        ImportButton.IsEnabled = true;
        ImportButton.Content = "Import YTDs";
    }
}