using System.Text.Json;

namespace Callu.Infrastructure.Utilities;

/// <summary>
/// Simple JSONPath extractor for webhook payload mapping
/// Supports basic JSONPath expressions: $.field, $.field.nested, $.array[0].field
/// </summary>
public static class JsonPathExtractor
{
    /// <summary>
    /// Extracts a value from a JSON document using a JSONPath expression
    /// </summary>
    /// <param name="json">JSON string to extract from</param>
    /// <param name="jsonPath">JSONPath expression (e.g., $.alerts[0].labels.alertname)</param>
    /// <returns>Extracted string value or null if not found</returns>
    public static string? ExtractValue(string json, string jsonPath)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(jsonPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ExtractValue(doc.RootElement, jsonPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a value from a JsonElement using a JSONPath expression
    /// </summary>
    public static string? ExtractValue(JsonElement root, string jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath))
            return null;

        try
        {
            var path = jsonPath.TrimStart('$').TrimStart('.');
            var current = root;
            var segments = ParsePathSegments(path);

            foreach (var segment in segments)
            {
                if (segment.IsArrayIndex)
                {
                    if (current.ValueKind == JsonValueKind.Array && segment.Index < current.GetArrayLength())
                    {
                        current = current[segment.Index];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment.PropertyName, out var prop))
                    {
                        current = prop;
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => current.GetRawText()
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts multiple values based on a field mapping dictionary
    /// </summary>
    /// <param name="json">JSON string to extract from</param>
    /// <param name="fieldMappings">Dictionary of field name to JSONPath</param>
    /// <returns>Dictionary of extracted values</returns>
    public static Dictionary<string, string?> ExtractMultiple(string json, Dictionary<string, string> fieldMappings)
    {
        var result = new Dictionary<string, string?>();

        if (string.IsNullOrEmpty(json))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            
            foreach (var (fieldName, jsonPath) in fieldMappings)
            {
                if (!string.IsNullOrEmpty(jsonPath))
                {
                    result[fieldName] = ExtractValue(doc.RootElement, jsonPath);
                }
            }
        }
        catch
        {
        }

        return result;
    }

    /// <summary>
    /// Parses a JSONPath into segments for traversal
    /// </summary>
    private static List<PathSegment> ParsePathSegments(string path)
    {
        var segments = new List<PathSegment>();
        var current = "";
        var i = 0;

        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                if (!string.IsNullOrEmpty(current))
                {
                    segments.Add(new PathSegment { PropertyName = current });
                    current = "";
                }
                i++;
            }
            else if (path[i] == '[')
            {
                if (!string.IsNullOrEmpty(current))
                {
                    segments.Add(new PathSegment { PropertyName = current });
                    current = "";
                }

                var endBracket = path.IndexOf(']', i);
                if (endBracket > i)
                {
                    var indexStr = path.Substring(i + 1, endBracket - i - 1);

                    if (int.TryParse(indexStr, out var index))
                    {
                        segments.Add(new PathSegment { IsArrayIndex = true, Index = index });
                    }
                    else if (indexStr.StartsWith("'") && indexStr.EndsWith("'"))
                    {
                        segments.Add(new PathSegment { PropertyName = indexStr.Trim('\'') });
                    }
                    else if (indexStr.StartsWith("\"") && indexStr.EndsWith("\""))
                    {
                        segments.Add(new PathSegment { PropertyName = indexStr.Trim('"') });
                    }
                    
                    i = endBracket + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                current += path[i];
                i++;
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            segments.Add(new PathSegment { PropertyName = current });
        }

        return segments;
    }

    private class PathSegment
    {
        public bool IsArrayIndex { get; set; }
        public int Index { get; set; }
        public string PropertyName { get; set; } = "";
    }
}
