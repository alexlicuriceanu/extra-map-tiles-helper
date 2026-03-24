using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ExtraMapTilesHelper.Models;
using ExtraMapTilesHelper.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ExtraMapTilesHelper.Controllers;

public sealed class ProjectController
{
    private readonly YtdService _ytdService;

    public ProjectController(YtdService ytdService)
    {
        _ytdService = ytdService;
    }

    public async Task ImportYtdsAsync(
        IStorageProvider storageProvider,
        ObservableCollection<DictionaryItem> dictionaries,
        Action<string> setStatus,
        Action<bool> setImportEnabled)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select YTD Files",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Texture Dictionary") { Patterns = ["*.ytd"] }]
        });

        if (files.Count == 0) return;

        setImportEnabled(false);

        int total = files.Count;
        int current = 0;
        setStatus($"Loading dictionaries ({current}/{total})");

        try
        {
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    string dictName = System.IO.Path.GetFileNameWithoutExtension(file.Path.LocalPath);

                    Dispatcher.UIThread.Invoke(() =>
                    {
                        var existingDict = dictionaries.FirstOrDefault(d => d.Name == dictName);
                        if (existingDict != null) dictionaries.Remove(existingDict);
                    });

                    var newDict = new DictionaryItem { Name = dictName };
                    var extractedTextures = _ytdService.ExtractTextures(file.Path.LocalPath);

                    foreach (var tex in extractedTextures)
                    {
                        newDict.Textures.Add(tex);
                    }

                    // FIX: was Dispatcher.UIThread.Post(...)
                    Dispatcher.UIThread.Invoke(() => dictionaries.Add(newDict));

                    current++;
                    setStatus($"Loading {dictName}.ytd ({current}/{total})");
                }
            });

            setStatus($"Loaded {total} dictionaries");
        }
        catch
        {
            setStatus("Error loading dictionaries");
        }
        finally
        {
            setImportEnabled(true);
        }
    }

    public void RemoveDictionary(
        DictionaryItem dictionary,
        ObservableCollection<DictionaryItem> dictionaries,
        ObservableCollection<PlacedTileItem> placedTiles,
        Canvas mapCanvas,
        PlacedTileItem? currentSelectedTile,
        Action clearSelection)
    {
        var placedTilesToRemove = placedTiles
            .Where(t => string.Equals(t.YtdName, dictionary.Name, StringComparison.Ordinal))
            .ToList();

        var imageTilesToRemove = mapCanvas.Children
            .OfType<Image>()
            .Where(img => img.Tag is PlacedTileItem tile &&
                          string.Equals(tile.YtdName, dictionary.Name, StringComparison.Ordinal))
            .ToList();

        foreach (var image in imageTilesToRemove)
        {
            mapCanvas.Children.Remove(image);
        }

        foreach (var tile in placedTilesToRemove)
        {
            placedTiles.Remove(tile);
        }

        if (currentSelectedTile != null &&
            string.Equals(currentSelectedTile.YtdName, dictionary.Name, StringComparison.Ordinal))
        {
            clearSelection();
        }

        dictionaries.Remove(dictionary);
    }

    public void RemoveAllDictionaries(
        ObservableCollection<DictionaryItem> dictionaries,
        ObservableCollection<PlacedTileItem> placedTiles,
        Canvas mapCanvas,
        Action clearSelection)
    {
        var dictionaryNames = dictionaries
            .Select(d => d.Name)
            .ToHashSet(StringComparer.Ordinal);

        var imageTilesToRemove = mapCanvas.Children
            .OfType<Image>()
            .Where(img => img.Tag is PlacedTileItem tile && dictionaryNames.Contains(tile.YtdName))
            .ToList();

        foreach (var image in imageTilesToRemove)
        {
            mapCanvas.Children.Remove(image);
        }

        var placedTilesToRemove = placedTiles
            .Where(t => dictionaryNames.Contains(t.YtdName))
            .ToList();

        foreach (var tile in placedTilesToRemove)
        {
            placedTiles.Remove(tile);
        }

        clearSelection();
        dictionaries.Clear();
    }
}