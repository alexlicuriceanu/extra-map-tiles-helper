using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ExtraMapTilesHelper.Models;
using ExtraMapTilesHelper.Services;
using System.Linq;

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

        ImportButton.IsEnabled = false;
        ImportButton.Content = "Extracting...";

        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                // 1. Get the name of the dictionary we are about to import
                string dictName = System.IO.Path.GetFileNameWithoutExtension(file.Path.LocalPath);

                // 2. CHECK FOR DUPLICATES: Remove any existing textures with this dictionary name
                Dispatcher.UIThread.Invoke(() =>
                {
                    // Find all items that match the name
                    var oldItems = Textures.Where(t => t.DictionaryName == dictName).ToList();

                    // Remove them from the UI list
                    foreach (var item in oldItems)
                    {
                        Textures.Remove(item);
                    }
                });

                // 3. Extract and add the fresh ones
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
}