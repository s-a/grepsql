using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Google.Protobuf;

namespace PgQuery.NET.Analysis
{
    /// <summary>
    /// Extension methods for IMessage (PostgreSQL AST nodes)
    /// </summary>
    public static class IMessageExtensions
    {
        /// <summary>
        /// Gets the source text for this node from the original SQL
        /// </summary>
        /// <param name="node">The AST node</param>
        /// <param name="sourceText">The original SQL text</param>
        /// <param name="estimateLength">Whether to estimate the end position if not available</param>
        /// <returns>The source text for this node, or null if location information is not available</returns>
        public static string? GetSource(this IMessage node, string sourceText, bool estimateLength = true)
        {
            if (node == null || string.IsNullOrEmpty(sourceText))
                return null;

            var location = LocationExtractor.GetLocation(node);
            if (!location.HasValue)
                return null;

            var start = location.Value;
            if (start < 0 || start >= sourceText.Length)
                return null;

            if (!estimateLength)
            {
                // Just return from start position to end of current token/word
                var end = FindTokenEnd(sourceText, start);
                return sourceText.Substring(start, Math.Min(end - start, sourceText.Length - start));
            }

            // Try to estimate the end position based on node type and content
            var estimatedEnd = EstimateNodeEnd(node, sourceText, start);
            var length = Math.Min(estimatedEnd - start, sourceText.Length - start);
            
            return length > 0 ? sourceText.Substring(start, length) : null;
        }

        /// <summary>
        /// Gets the source range (start, end) for this node
        /// </summary>
        /// <param name="node">The AST node</param>
        /// <param name="sourceText">The original SQL text</param>
        /// <param name="estimateLength">Whether to estimate the end position if not available</param>
        /// <returns>The source range for this node, or null if location information is not available</returns>
        public static SourceRange? GetSourceRange(this IMessage node, string sourceText, bool estimateLength = true)
        {
            if (node == null || string.IsNullOrEmpty(sourceText))
                return null;

            var location = LocationExtractor.GetLocation(node);
            if (!location.HasValue)
                return null;

            var start = location.Value;
            if (start < 0 || start >= sourceText.Length)
                return null;

            int end;
            if (!estimateLength)
            {
                end = FindTokenEnd(sourceText, start);
            }
            else
            {
                end = EstimateNodeEnd(node, sourceText, start);
            }

            return new SourceRange(start, end);
        }

        /// <summary>
        /// Gets a detailed source information object for this node
        /// </summary>
        /// <param name="node">The AST node</param>
        /// <param name="sourceText">The original SQL text</param>
        /// <returns>Detailed source information including line/column positions</returns>
        public static SourceInfo? GetSourceInfo(this IMessage node, string sourceText)
        {
            var range = node.GetSourceRange(sourceText);
            if (!range.HasValue)
                return null;

            var sourceInfo = SourceInfo.FromRange(sourceText, range.Value);
            sourceInfo.NodeType = node.GetType().Name;
            sourceInfo.Source = node.GetSource(sourceText);

            return sourceInfo;
        }

        private static int FindTokenEnd(string sourceText, int start)
        {
            var end = start;
            
            // Skip whitespace at the beginning
            while (end < sourceText.Length && char.IsWhiteSpace(sourceText[end]))
                end++;
            
            if (end >= sourceText.Length)
                return Math.Min(start + 1, sourceText.Length);

            // Find end of current token
            var c = sourceText[end];
            
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                // Identifier or number
                while (end < sourceText.Length && (char.IsLetterOrDigit(sourceText[end]) || sourceText[end] == '_'))
                    end++;
            }
            else if (c == '"' || c == '\'')
            {
                // Quoted string
                var quote = c;
                end++; // Skip opening quote
                while (end < sourceText.Length && sourceText[end] != quote)
                {
                    if (sourceText[end] == '\\')
                        end++; // Skip escaped character
                    end++;
                }
                if (end < sourceText.Length)
                    end++; // Include closing quote
            }
            else
            {
                // Single character token (operator, punctuation)
                end++;
            }

            return end;
        }

        private static int EstimateNodeEnd(IMessage node, string sourceText, int start)
        {
            var nodeType = node.GetType().Name;
            
            // Try to get a more accurate estimate based on node type
            switch (nodeType)
            {
                case "A_Const":
                    return EstimateConstantEnd(sourceText, start);
                
                case "ColumnRef":
                    return EstimateColumnRefEnd(sourceText, start);
                
                case "FuncCall":
                    return EstimateFunctionCallEnd(sourceText, start);
                
                case "A_Expr":
                    return EstimateExpressionEnd(sourceText, start);
                
                case "SelectStmt":
                    return EstimateSelectStatementEnd(sourceText, start);
                
                default:
                    // Fallback: find end of current token
                    return FindTokenEnd(sourceText, start);
            }
        }

        private static int EstimateConstantEnd(string sourceText, int start)
        {
            var end = start;
            
            // Skip whitespace
            while (end < sourceText.Length && char.IsWhiteSpace(sourceText[end]))
                end++;
            
            if (end >= sourceText.Length)
                return sourceText.Length;

            var c = sourceText[end];
            
            if (c == '\'' || c == '"')
            {
                // String literal
                var quote = c;
                end++; // Skip opening quote
                while (end < sourceText.Length && sourceText[end] != quote)
                {
                    if (sourceText[end] == '\\')
                        end++; // Skip escaped character
                    if (end < sourceText.Length)
                        end++;
                }
                if (end < sourceText.Length)
                    end++; // Include closing quote
            }
            else if (char.IsDigit(c) || c == '-' || c == '+')
            {
                // Numeric literal
                if (c == '-' || c == '+')
                    end++; // Skip sign
                
                while (end < sourceText.Length && (char.IsDigit(sourceText[end]) || sourceText[end] == '.'))
                    end++;
            }
            else
            {
                // Other constant (e.g., NULL, TRUE, FALSE)
                while (end < sourceText.Length && char.IsLetter(sourceText[end]))
                    end++;
            }

            return end;
        }

        private static int EstimateColumnRefEnd(string sourceText, int start)
        {
            var end = start;
            
            // Skip whitespace
            while (end < sourceText.Length && char.IsWhiteSpace(sourceText[end]))
                end++;
            
            // Handle qualified names (schema.table.column)
            while (end < sourceText.Length)
            {
                // Read identifier
                if (sourceText[end] == '"')
                {
                    // Quoted identifier
                    end++; // Skip opening quote
                    while (end < sourceText.Length && sourceText[end] != '"')
                        end++;
                    if (end < sourceText.Length)
                        end++; // Skip closing quote
                }
                else
                {
                    // Unquoted identifier
                    while (end < sourceText.Length && (char.IsLetterOrDigit(sourceText[end]) || sourceText[end] == '_'))
                        end++;
                }
                
                // Check if there's a dot for qualification
                if (end < sourceText.Length && sourceText[end] == '.')
                {
                    end++; // Skip dot
                    continue;
                }
                
                break;
            }

            return end;
        }

        private static int EstimateFunctionCallEnd(string sourceText, int start)
        {
            var end = start;
            
            // Skip whitespace
            while (end < sourceText.Length && char.IsWhiteSpace(sourceText[end]))
                end++;
            
            // Read function name
            while (end < sourceText.Length && (char.IsLetterOrDigit(sourceText[end]) || sourceText[end] == '_'))
                end++;
            
            // Skip whitespace
            while (end < sourceText.Length && char.IsWhiteSpace(sourceText[end]))
                end++;
            
            // Look for opening parenthesis
            if (end < sourceText.Length && sourceText[end] == '(')
            {
                var parenCount = 1;
                end++; // Skip opening paren
                
                while (end < sourceText.Length && parenCount > 0)
                {
                    if (sourceText[end] == '(')
                        parenCount++;
                    else if (sourceText[end] == ')')
                        parenCount--;
                    end++;
                }
            }

            return end;
        }

        private static int EstimateExpressionEnd(string sourceText, int start)
        {
            // For expressions, we'll be conservative and just find the next token
            return FindTokenEnd(sourceText, start);
        }

        private static int EstimateSelectStatementEnd(string sourceText, int start)
        {
            // For SELECT statements, we'll find the end of the SELECT keyword
            // More sophisticated logic would be needed for full statement parsing
            var end = start;
            
            // Skip whitespace
            while (end < sourceText.Length && char.IsWhiteSpace(sourceText[end]))
                end++;
            
            // Read SELECT keyword
            var selectKeyword = "SELECT";
            for (int i = 0; i < selectKeyword.Length && end < sourceText.Length; i++)
            {
                if (char.ToUpperInvariant(sourceText[end]) == selectKeyword[i])
                    end++;
                else
                    break;
            }

            return end;
        }
    }

    /// <summary>
    /// Detailed source information for a node
    /// </summary>
    public class SourceInfo
    {
        public string? NodeType { get; set; }
        public string? Source { get; set; }
        public SourceRange Range { get; set; }
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }

        public static SourceInfo FromRange(string sourceText, SourceRange range)
        {
            var info = new SourceInfo { Range = range };
            
            // Calculate line and column positions
            var lines = sourceText.Substring(0, Math.Min(range.Start, sourceText.Length)).Split('\n');
            info.StartLine = lines.Length;
            info.StartColumn = lines.LastOrDefault()?.Length + 1 ?? 1;
            
            if (range.End <= sourceText.Length)
            {
                var endLines = sourceText.Substring(0, range.End).Split('\n');
                info.EndLine = endLines.Length;
                info.EndColumn = endLines.LastOrDefault()?.Length + 1 ?? 1;
            }
            else
            {
                info.EndLine = info.StartLine;
                info.EndColumn = info.StartColumn;
            }

            return info;
        }
    }
} 