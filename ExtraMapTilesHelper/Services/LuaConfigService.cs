using ExtraMapTilesHelper.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExtraMapTilesHelper.Services;

public class LuaConfigService
{
    public static string GenerateLuaConfig(IEnumerable<PlacedTileItem> tiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("config = {}");
        sb.AppendLine("config.scaleform_minimap_main_map = \"minimap_main_map\"");
        sb.AppendLine("config.scaleform_minimap = \"minimap\"");
        sb.AppendLine("config.offset = 0.1\n");
        sb.AppendLine("config.remove_blur = true");
        sb.AppendLine("config.radar_masks = \"radar_masks\"\n");
        sb.AppendLine("config.tiles = {");

        int index = 1;
        foreach (var tile in tiles)
        {
            sb.AppendLine($"    ['{index}'] = {{");
            sb.AppendLine($"        txd = \"{tile.YtdName}\",");
            sb.AppendLine($"        txn = \"{tile.TxdName}\",");

            // Match exactly what the numeric editor shows (anchor-based when centered)
            double width = CoordinateMapper.CanvasTileSize * tile.ScaleX;
            double height = CoordinateMapper.CanvasTileSize * tile.ScaleY;
            double anchorX = tile.X + (tile.Centered ? width / 2.0 : 0.0);
            double anchorY = tile.Y + (tile.Centered ? height / 2.0 : 0.0);

            if (tile.IsOffsetMode)
            {
                var offsets = CoordinateMapper.CoordinatesToOffsets(anchorX, anchorY);
                sb.AppendLine($"        x_offset = {offsets.X.ToString("0.0###", CultureInfo.InvariantCulture)},");
                sb.AppendLine($"        y_offset = {offsets.Y.ToString("0.0###", CultureInfo.InvariantCulture)},");
            }
            else
            {
                var game = CoordinateMapper.CoordinatesToGame(anchorX, anchorY);
                sb.AppendLine($"        x = {game.X.ToString("0.0###", CultureInfo.InvariantCulture)},");
                sb.AppendLine($"        y = {game.Y.ToString("0.0###", CultureInfo.InvariantCulture)},");
            }

            sb.AppendLine($"        x_scale = {tile.ScaleX.ToString("0.0###", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"        y_scale = {tile.ScaleY.ToString("0.0###", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"        rotation = {tile.RotationDegrees.ToString("0.0###", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"        alpha = {(int)tile.Alpha},");
            sb.AppendLine($"        centered = {(tile.Centered ? "true" : "false")},");
            sb.AppendLine($"        visible = {(tile.IsVisible ? "true" : "false")}");
            sb.AppendLine("    },"); // Removed the trailing comma from here
            index++;
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public IEnumerable<ParsedTileData> ParseLuaConfig(string luaContent)
    {
        var tiles = new List<ParsedTileData>();

        var tilesBody = ExtractTilesTableBody(luaContent);
        if (string.IsNullOrWhiteSpace(tilesBody))
            return tiles;

        var blockRegex = new Regex(@"\[(?:(?:'(?<idSingle>[^']+)')|(?:\""(?<idDouble>[^""]+)\"")|(?<idNumber>\d+))\]\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
        var fieldRegex = new Regex(@"([a-zA-Z_]+)\s*=\s*(?:(?:\""([^""]*)\"")|(?:'([^']*)')|([^,\s}]+))");

        int fallbackId = 1;

        foreach (Match blockMatch in blockRegex.Matches(tilesBody))
        {
            var idRaw = blockMatch.Groups["idSingle"].Success
                ? blockMatch.Groups["idSingle"].Value
                : blockMatch.Groups["idDouble"].Success
                    ? blockMatch.Groups["idDouble"].Value
                    : blockMatch.Groups["idNumber"].Value;

            int parsedId = int.TryParse(idRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? id
                : fallbackId;

            fallbackId++;

            var blockContent = blockMatch.Groups["body"].Value;
            var tileData = new ParsedTileData
            {
                ConfigId = parsedId
            };

            foreach (Match fieldMatch in fieldRegex.Matches(blockContent))
            {
                string key = fieldMatch.Groups[1].Value.Trim().ToLowerInvariant();
                string val = fieldMatch.Groups[2].Success
                    ? fieldMatch.Groups[2].Value
                    : fieldMatch.Groups[3].Success
                        ? fieldMatch.Groups[3].Value
                        : fieldMatch.Groups[4].Value.Trim();

                switch (key)
                {
                    case "txd":
                        tileData.DictionaryName = val;
                        break;
                    case "txn":
                        tileData.TextureName = val;
                        break;
                    case "x_offset":
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double xo))
                            tileData.OffsetX = xo;
                        break;
                    case "y_offset":
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double yo))
                            tileData.OffsetY = yo;
                        break;
                    case "x":
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double gx))
                            tileData.GameX = gx;
                        break;
                    case "y":
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double gy))
                            tileData.GameY = gy;
                        break;
                    case "x_scale":
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double xs))
                            tileData.ScaleX = xs;
                        break;
                    case "y_scale":
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double ys))
                            tileData.ScaleY = ys;
                        break;
                    case "rotation":
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double rot))
                            tileData.Rotation = rot;
                        break;
                    case "alpha":
                        if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out int alpha))
                            tileData.Alpha = alpha;
                        break;
                    case "centered":
                        tileData.Centered = val.Equals("true", System.StringComparison.OrdinalIgnoreCase);
                        break;
                    case "visible":
                        tileData.Visible = val.Equals("true", System.StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            tiles.Add(tileData);
        }

        return tiles;
    }

    private string ExtractTilesTableBody(string luaContent)
    {
        var markerMatch = Regex.Match(luaContent, @"config\.tiles\s*=\s*\{", RegexOptions.IgnoreCase);
        if (!markerMatch.Success)
            return string.Empty;

        int openBraceIndex = luaContent.IndexOf('{', markerMatch.Index);
        if (openBraceIndex < 0)
            return string.Empty;

        int depth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool escaped = false;

        for (int i = openBraceIndex; i < luaContent.Length; i++)
        {
            char c = luaContent[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
                continue;

            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    int bodyStart = openBraceIndex + 1;
                    int bodyLength = i - bodyStart;
                    return bodyLength > 0
                        ? luaContent.Substring(bodyStart, bodyLength)
                        : string.Empty;
                }
            }
        }

        return string.Empty;
    }
}

/// <summary>
/// A completely UI-decoupled intermediate structure generated by the Lua parsing logic that your controller/I-O orchestrator can apply mapping against.
/// </summary>
public class ParsedTileData
{
    public int ConfigId { get; set; }

    public string DictionaryName { get; set; } = string.Empty;
    public string TextureName { get; set; } = string.Empty;

    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }

    public double? GameX { get; set; }
    public double? GameY { get; set; }

    public double ScaleX { get; set; } = 1.0;
    public double ScaleY { get; set; } = 1.0;
    public double Rotation { get; set; }
    public int Alpha { get; set; } = 100;
    public bool Centered { get; set; }
    public bool Visible { get; set; } = true;

    public bool HasOffsetCoordinates => OffsetX.HasValue && OffsetY.HasValue;
    public bool HasGameCoordinates => GameX.HasValue && GameY.HasValue;
}