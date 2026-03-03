using Photino.NET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ExtraMapTilesHelper.backend
{
    public class MessageRouter
    {
        private readonly PhotinoWindow _window;

        // Configured to match JavaScript's camelCase naming
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public MessageRouter(PhotinoWindow window)
        {
            _window = window;
        }

        private string _lastDirectory = string.Empty;

        public void HandleMessage(object? sender, string rawMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawMessage);
                var type = doc.RootElement.GetProperty("type").GetString();

                // The Traffic Cop
                switch (type)
                {
                    case "ping":
                        Send("pong", new { message = "Backend is alive and ready!" });
                        break;

                    case "open_file_dialog":
                        // 1. Enable multiple file selection
                        var selectedFiles = _window.ShowOpenFile(
                            title: "Select YTD files",
                            defaultPath: string.IsNullOrEmpty(_lastDirectory) ? null : _lastDirectory,
                            multiSelect: true,

                            filters: new[] { ("Texture Dictionary", new[] { "ytd" }) }
                        );

                        if (selectedFiles != null && selectedFiles.Length > 0)
                        {

                            _lastDirectory = Path.GetDirectoryName(selectedFiles[0]) ?? string.Empty;

                            Task.Run(() =>
                            {
                                var service = new CodeWalkerService();

                                foreach (var filePath in selectedFiles)
                                {
                                    try
                                    {
                                        string dictName = Path.GetFileNameWithoutExtension(filePath);

                                        // 1. Tell the UI to prepare a section for this dictionary
                                        Send("ytd_started", new { dictionaryName = dictName });

                                        // 2. Extract and stream textures one by one
                                        service.ExtractYtd(filePath, (textureInfo) =>
                                        {
                                            // Because this payload is just ONE image, WebView2 handles it flawlessly
                                            Send("texture_loaded", new { dictionaryName = dictName, texture = textureInfo });
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Send("error", new { message = $"Failed to load {Path.GetFileName(filePath)}: {ex.Message}" });
                                    }
                                }

                                // Clean up memory after everything is done
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            });
                        }
                        break;

                    default:
                        Console.WriteLine($"Unknown message type: {type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Router Error: {ex.Message}");
            }
        }

        // Helper to format and send JSON back to the UI
        public void Send(string type, object payload)
        {
            var message = JsonSerializer.Serialize(new { type, payload }, JsonOptions);

            // Safely delegates the message sending back to the main UI thread
            _window.Invoke(() => _window.SendWebMessage(message));
        }
    }
}
