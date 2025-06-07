using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf;

namespace PgQuery.NET.Analysis
{
    /// <summary>
    /// Represents a source location range in SQL text
    /// </summary>
    public struct SourceRange
    {
        public int Start { get; }
        public int End { get; }
        public int Length => End - Start;

        public SourceRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        public SourceRange(int start, int length, bool isLength)
        {
            Start = start;
            End = isLength ? start + length : length;
        }

        public bool Contains(int position) => position >= Start && position < End;
        public bool Overlaps(SourceRange other) => Start < other.End && End > other.Start;

        public override string ToString() => $"[{Start}..{End})";
    }

    /// <summary>
    /// Represents a source location with line and column information
    /// </summary>
    public struct SourcePosition
    {
        public int CharacterPosition { get; }
        public int Line { get; }
        public int Column { get; }

        public SourcePosition(int characterPosition, int line, int column)
        {
            CharacterPosition = characterPosition;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"Line {Line}, Column {Column} (char {CharacterPosition})";
    }

    /// <summary>
    /// Utility for extracting location information from PostgreSQL AST nodes
    /// </summary>
    public static class LocationExtractor
    {
        private static readonly Dictionary<Type, PropertyInfo?> LocationPropertyCache = new();

        /// <summary>
        /// Extract the start location from any PostgreSQL AST node that has a location property
        /// </summary>
        public static int? GetLocation(IMessage node)
        {
            if (node == null) return null;

            var nodeType = node.GetType();
            
            // Check cache first
            if (LocationPropertyCache.TryGetValue(nodeType, out var cachedProperty))
            {
                if (cachedProperty == null) return null;
                var value = cachedProperty.GetValue(node);
                return value is int intValue && intValue >= 0 ? intValue : null;
            }

            // Find and cache the location property
            var locationProperty = nodeType.GetProperty("Location", BindingFlags.Public | BindingFlags.Instance);
            LocationPropertyCache[nodeType] = locationProperty;

            if (locationProperty?.PropertyType == typeof(int))
            {
                var value = locationProperty.GetValue(node);
                return value is int intValue && intValue >= 0 ? intValue : null;
            }

            return null;
        }

        /// <summary>
        /// Estimate the end position of a node based on its type and content
        /// </summary>
        public static int? GetEstimatedEndLocation(IMessage node, string sourceText)
        {
            var start = GetLocation(node);
            if (!start.HasValue) return null;

            var estimatedLength = EstimateNodeLength(node, sourceText, start.Value);
            return start.Value + estimatedLength;
        }

        /// <summary>
        /// Get a source range for a node, combining start location with estimated length
        /// </summary>
        public static SourceRange? GetSourceRange(IMessage node, string sourceText)
        {
            var start = GetLocation(node);
            if (!start.HasValue) return null;

            var end = GetEstimatedEndLocation(node, sourceText);
            if (!end.HasValue) return null;

            return new SourceRange(start.Value, end.Value);
        }

        /// <summary>
        /// Extract all nodes with location information from an AST tree
        /// </summary>
        public static List<(IMessage Node, SourceRange Range)> GetAllLocatedNodes(IMessage rootNode, string sourceText)
        {
            var result = new List<(IMessage, SourceRange)>();
            CollectLocatedNodes(rootNode, sourceText, result, new HashSet<IMessage>());
            
            // Sort by start position for easier processing
            result.Sort((a, b) => a.Item2.Start.CompareTo(b.Item2.Start));
            
            return result;
        }

        /// <summary>
        /// Find the most specific (deepest) node that contains the given position
        /// </summary>
        public static IMessage? FindNodeAtPosition(IMessage rootNode, string sourceText, int position)
        {
            var locatedNodes = GetAllLocatedNodes(rootNode, sourceText);
            
            IMessage? mostSpecific = null;
            var smallestRange = int.MaxValue;

            foreach (var (node, range) in locatedNodes)
            {
                if (range.Contains(position) && range.Length < smallestRange)
                {
                    mostSpecific = node;
                    smallestRange = range.Length;
                }
            }

            return mostSpecific;
        }

        /// <summary>
        /// Convert character positions to line/column information
        /// </summary>
        public static SourcePosition GetSourcePosition(string sourceText, int characterPosition)
        {
            if (characterPosition < 0 || characterPosition > sourceText.Length)
                throw new ArgumentOutOfRangeException(nameof(characterPosition));

            var line = 1;
            var column = 1;

            for (var i = 0; i < characterPosition && i < sourceText.Length; i++)
            {
                if (sourceText[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return new SourcePosition(characterPosition, line, column);
        }

        /// <summary>
        /// Create a source buffer that can efficiently map between character positions and line/column
        /// </summary>
        public static SourceBuffer CreateSourceBuffer(string sourceText)
        {
            return new SourceBuffer(sourceText);
        }

        private static void CollectLocatedNodes(IMessage node, string sourceText, 
            List<(IMessage, SourceRange)> result, HashSet<IMessage> visited)
        {
            if (node == null || visited.Contains(node)) return;
            visited.Add(node);

            // Check if this node has location information
            var range = GetSourceRange(node, sourceText);
            if (range.HasValue)
            {
                result.Add((node, range.Value));
            }

            // Recursively process child nodes
            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                if (value == null) continue;

                if (value is IMessage childMessage)
                {
                    CollectLocatedNodes(childMessage, sourceText, result, visited);
                }
                else if (value is IEnumerable<IMessage> messageList)
                {
                    foreach (var item in messageList)
                    {
                        CollectLocatedNodes(item, sourceText, result, visited);
                    }
                }
            }
        }

        private static int EstimateNodeLength(IMessage node, string sourceText, int startPos)
        {
            if (startPos >= sourceText.Length) return 0;

            var nodeType = node.GetType().Name;
            
            // Handle specific node types with known patterns
            switch (nodeType)
            {
                case "String":
                case "A_Const" when IsStringConstant(node):
                    return EstimateStringConstantLength(sourceText, startPos);
                    
                case "Integer":
                case "A_Const" when IsIntegerConstant(node):
                    return EstimateIntegerLength(sourceText, startPos);
                    
                case "ColumnRef":
                    return EstimateIdentifierLength(sourceText, startPos);
                    
                case "RangeVar":
                    return EstimateIdentifierLength(sourceText, startPos);
                    
                case "FuncCall":
                    return EstimateFunctionCallLength(sourceText, startPos);
                    
                case "TypeName":
                    return EstimateTypeNameLength(sourceText, startPos);
                    
                // Keywords
                case "SelectStmt":
                    return EstimateKeywordLength(sourceText, startPos, "SELECT");
                case "InsertStmt":
                    return EstimateKeywordLength(sourceText, startPos, "INSERT");
                case "UpdateStmt":
                    return EstimateKeywordLength(sourceText, startPos, "UPDATE");
                case "DeleteStmt":
                    return EstimateKeywordLength(sourceText, startPos, "DELETE");
                    
                default:
                    // Generic estimation: look for word boundaries
                    return EstimateGenericTokenLength(sourceText, startPos);
            }
        }

        private static bool IsStringConstant(IMessage node)
        {
            // Check if the A_Const contains a string value
            var descriptor = node.Descriptor;
            var valField = descriptor.Fields.InDeclarationOrder().FirstOrDefault(f => f.Name == "val");
            if (valField != null)
            {
                var val = valField.Accessor.GetValue(node);
                if (val is IMessage valMessage)
                {
                    return valMessage.GetType().Name == "String";
                }
            }
            return false;
        }

        private static bool IsIntegerConstant(IMessage node)
        {
            var descriptor = node.Descriptor;
            var valField = descriptor.Fields.InDeclarationOrder().FirstOrDefault(f => f.Name == "val");
            if (valField != null)
            {
                var val = valField.Accessor.GetValue(node);
                if (val is IMessage valMessage)
                {
                    return valMessage.GetType().Name == "Integer";
                }
            }
            return false;
        }

        private static int EstimateStringConstantLength(string sourceText, int startPos)
        {
            if (startPos >= sourceText.Length) return 0;
            
            var quote = sourceText[startPos];
            if (quote == '\'' || quote == '"')
            {
                for (var i = startPos + 1; i < sourceText.Length; i++)
                {
                    if (sourceText[i] == quote)
                    {
                        // Check for escaped quotes
                        if (i + 1 < sourceText.Length && sourceText[i + 1] == quote)
                        {
                            i++; // Skip escaped quote
                            continue;
                        }
                        return i - startPos + 1;
                    }
                }
            }
            
            // Fallback to generic estimation
            return EstimateGenericTokenLength(sourceText, startPos);
        }

        private static int EstimateIntegerLength(string sourceText, int startPos)
        {
            var length = 0;
            for (var i = startPos; i < sourceText.Length && char.IsDigit(sourceText[i]); i++)
            {
                length++;
            }
            return Math.Max(1, length);
        }

        private static int EstimateIdentifierLength(string sourceText, int startPos)
        {
            if (startPos >= sourceText.Length) return 0;
            
            // Handle quoted identifiers
            if (sourceText[startPos] == '"')
            {
                for (var i = startPos + 1; i < sourceText.Length; i++)
                {
                    if (sourceText[i] == '"')
                    {
                        return i - startPos + 1;
                    }
                }
            }
            
            // Handle regular identifiers
            var length = 0;
            for (var i = startPos; i < sourceText.Length; i++)
            {
                var c = sourceText[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    length++;
                }
                else
                {
                    break;
                }
            }
            return Math.Max(1, length);
        }

        private static int EstimateFunctionCallLength(string sourceText, int startPos)
        {
            // Function calls are complex, so we'll use a simple heuristic
            // Find the function name, then look for the opening parenthesis
            var identifierLength = EstimateIdentifierLength(sourceText, startPos);
            
            // Look for opening parenthesis after potential whitespace
            for (var i = startPos + identifierLength; i < sourceText.Length; i++)
            {
                if (char.IsWhiteSpace(sourceText[i])) continue;
                if (sourceText[i] == '(')
                {
                    // For now, just return the identifier part
                    // A more sophisticated implementation would parse to the matching ')'
                    return identifierLength;
                }
                break;
            }
            
            return identifierLength;
        }

        private static int EstimateTypeNameLength(string sourceText, int startPos)
        {
            // Type names can be qualified (schema.type) or have parameters
            // For now, treat them like identifiers
            return EstimateIdentifierLength(sourceText, startPos);
        }

        private static int EstimateKeywordLength(string sourceText, int startPos, string keyword)
        {
            if (startPos + keyword.Length <= sourceText.Length)
            {
                var actualText = sourceText.Substring(startPos, keyword.Length);
                if (string.Equals(actualText, keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return keyword.Length;
                }
            }
            
            // Fallback
            return EstimateGenericTokenLength(sourceText, startPos);
        }

        private static int EstimateGenericTokenLength(string sourceText, int startPos)
        {
            if (startPos >= sourceText.Length) return 0;
            
            var length = 0;
            for (var i = startPos; i < sourceText.Length; i++)
            {
                var c = sourceText[i];
                if (char.IsWhiteSpace(c) || ".,;()[]{}=<>+-*/%".Contains(c))
                {
                    break;
                }
                length++;
            }
            return Math.Max(1, length);
        }
    }

    /// <summary>
    /// Efficient source buffer for mapping between character positions and line/column information
    /// </summary>
    public class SourceBuffer
    {
        private readonly string _text;
        private readonly List<int> _lineStarts;

        public string Text => _text;
        public int Length => _text.Length;
        public int LineCount => _lineStarts.Count;

        public SourceBuffer(string text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _lineStarts = BuildLineIndex(text);
        }

        public SourcePosition GetPosition(int characterPosition)
        {
            if (characterPosition < 0 || characterPosition > _text.Length)
                throw new ArgumentOutOfRangeException(nameof(characterPosition));

            // Binary search to find the line
            var line = BinarySearchLineIndex(characterPosition);
            var lineStart = _lineStarts[line];
            var column = characterPosition - lineStart + 1;

            return new SourcePosition(characterPosition, line + 1, column);
        }

        public int GetCharacterPosition(int line, int column)
        {
            if (line < 1 || line > _lineStarts.Count)
                throw new ArgumentOutOfRangeException(nameof(line));

            var lineStart = _lineStarts[line - 1];
            var position = lineStart + column - 1;

            if (position > _text.Length)
                throw new ArgumentOutOfRangeException(nameof(column));

            return position;
        }

        public string GetLineText(int line)
        {
            if (line < 1 || line > _lineStarts.Count)
                throw new ArgumentOutOfRangeException(nameof(line));

            var start = _lineStarts[line - 1];
            var end = line < _lineStarts.Count ? _lineStarts[line] - 1 : _text.Length;

            // Remove trailing newline if present
            if (end > start && end <= _text.Length && _text[end - 1] == '\n')
                end--;
            if (end > start && end <= _text.Length && _text[end - 1] == '\r')
                end--;

            return _text.Substring(start, end - start);
        }

        private static List<int> BuildLineIndex(string text)
        {
            var lineStarts = new List<int> { 0 }; // First line starts at position 0

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineStarts.Add(i + 1);
                }
            }

            return lineStarts;
        }

        private int BinarySearchLineIndex(int position)
        {
            var left = 0;
            var right = _lineStarts.Count - 1;

            while (left < right)
            {
                var mid = (left + right + 1) / 2;
                if (_lineStarts[mid] <= position)
                {
                    left = mid;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return left;
        }
    }
} 