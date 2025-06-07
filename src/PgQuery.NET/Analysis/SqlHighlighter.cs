using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Google.Protobuf;

namespace PgQuery.NET.Analysis
{
    /// <summary>
    /// Highlighting styles for SQL output
    /// </summary>
    public enum HighlightStyle
    {
        None,
        AnsiColors,
        Html,
        Markdown
    }

    /// <summary>
    /// Configuration for SQL highlighting
    /// </summary>
    public class HighlightOptions
    {
        public HighlightStyle Style { get; set; } = HighlightStyle.AnsiColors;
        public bool ShowLineNumbers { get; set; } = true;
        public bool ShowMatchInfo { get; set; } = true;
        public string MatchColor { get; set; } = "\u001b[32m"; // Green
        public string ResetColor { get; set; } = "\u001b[0m";
        public string LineNumberColor { get; set; } = "\u001b[90m"; // Gray
        public string InfoColor { get; set; } = "\u001b[36m"; // Cyan
    }

    /// <summary>
    /// Utility for highlighting SQL text based on AST node matches
    /// </summary>
    public static class SqlHighlighter
    {
        /// <summary>
        /// Highlight matching nodes in SQL text
        /// </summary>
        public static string HighlightMatches(string sqlText, IEnumerable<IMessage> matchingNodes, HighlightOptions? options = null)
        {
            options ??= new HighlightOptions();
            
            if (string.IsNullOrEmpty(sqlText) || !matchingNodes.Any())
            {
                return sqlText;
            }

            // Extract source ranges for all matching nodes
            var ranges = new List<SourceRange>();
            foreach (var node in matchingNodes)
            {
                var range = LocationExtractor.GetSourceRange(node, sqlText);
                if (range.HasValue)
                {
                    ranges.Add(range.Value);
                }
            }

            if (!ranges.Any())
            {
                return sqlText;
            }

            // Merge overlapping ranges and sort by start position
            var mergedRanges = MergeOverlappingRanges(ranges);
            
            return options.Style switch
            {
                HighlightStyle.AnsiColors => HighlightWithAnsi(sqlText, mergedRanges, options),
                HighlightStyle.Html => HighlightWithHtml(sqlText, mergedRanges, options),
                HighlightStyle.Markdown => HighlightWithMarkdown(sqlText, mergedRanges, options),
                _ => sqlText
            };
        }

        /// <summary>
        /// Highlight specific character ranges in SQL text
        /// </summary>
        public static string HighlightRanges(string sqlText, IEnumerable<SourceRange> ranges, HighlightOptions? options = null)
        {
            options ??= new HighlightOptions();
            
            if (string.IsNullOrEmpty(sqlText) || !ranges.Any())
            {
                return sqlText;
            }

            var mergedRanges = MergeOverlappingRanges(ranges);
            
            return options.Style switch
            {
                HighlightStyle.AnsiColors => HighlightWithAnsi(sqlText, mergedRanges, options),
                HighlightStyle.Html => HighlightWithHtml(sqlText, mergedRanges, options),
                HighlightStyle.Markdown => HighlightWithMarkdown(sqlText, mergedRanges, options),
                _ => sqlText
            };
        }

        /// <summary>
        /// Show detailed match information for debugging
        /// </summary>
        public static string ShowMatchDetails(string sqlText, IEnumerable<IMessage> matchingNodes, HighlightOptions? options = null)
        {
            options ??= new HighlightOptions();
            var sb = new StringBuilder();
            
            if (options.ShowMatchInfo)
            {
                sb.AppendLine($"{options.InfoColor}=== SQL Match Details ==={options.ResetColor}");
            }

            // Create source buffer for position mapping
            var sourceBuffer = LocationExtractor.CreateSourceBuffer(sqlText);
            
            // Collect all located nodes
            var locatedMatches = new List<(IMessage Node, SourceRange Range, SourcePosition Position)>();
            
            foreach (var node in matchingNodes)
            {
                var range = LocationExtractor.GetSourceRange(node, sqlText);
                if (range.HasValue)
                {
                    var position = sourceBuffer.GetPosition(range.Value.Start);
                    locatedMatches.Add((node, range.Value, position));
                }
            }

            // Sort by position
            locatedMatches.Sort((a, b) => a.Range.Start.CompareTo(b.Range.Start));

            // Show match details
            if (options.ShowMatchInfo && locatedMatches.Any())
            {
                sb.AppendLine($"{options.InfoColor}Matches found: {locatedMatches.Count}{options.ResetColor}");
                
                foreach (var (node, range, position) in locatedMatches)
                {
                    var nodeType = node.GetType().Name;
                    var text = sqlText.Substring(range.Start, Math.Min(range.Length, sqlText.Length - range.Start));
                    sb.AppendLine($"{options.InfoColor}  {nodeType} at {position}: \"{text}\"{options.ResetColor}");
                }
                
                sb.AppendLine();
            }

            // Add highlighted SQL
            var highlighted = HighlightMatches(sqlText, matchingNodes, options);
            sb.Append(highlighted);

            return sb.ToString();
        }

        /// <summary>
        /// Create a visual representation showing matches in context
        /// </summary>
        public static string ShowMatchesInContext(string sqlText, IEnumerable<IMessage> matchingNodes, int contextLines = 2, HighlightOptions? options = null)
        {
            options ??= new HighlightOptions();
            var sourceBuffer = LocationExtractor.CreateSourceBuffer(sqlText);
            var sb = new StringBuilder();

            // Get all matching ranges
            var ranges = new List<SourceRange>();
            foreach (var node in matchingNodes)
            {
                var range = LocationExtractor.GetSourceRange(node, sqlText);
                if (range.HasValue)
                {
                    ranges.Add(range.Value);
                }
            }

            if (!ranges.Any())
            {
                return sqlText;
            }

            // Find all affected lines
            var affectedLines = new HashSet<int>();
            foreach (var range in ranges)
            {
                var startPos = sourceBuffer.GetPosition(range.Start);
                var endPos = sourceBuffer.GetPosition(Math.Min(range.End - 1, sqlText.Length - 1));
                
                for (var line = startPos.Line; line <= endPos.Line; line++)
                {
                    // Add context lines
                    for (var contextLine = Math.Max(1, line - contextLines); 
                         contextLine <= Math.Min(sourceBuffer.LineCount, line + contextLines); 
                         contextLine++)
                    {
                        affectedLines.Add(contextLine);
                    }
                }
            }

            // Sort lines and create output
            var sortedLines = affectedLines.OrderBy(x => x).ToList();
            var lastShownLine = 0;

            foreach (var lineNumber in sortedLines)
            {
                // Show ellipsis for gaps
                if (lineNumber > lastShownLine + 1)
                {
                    sb.AppendLine($"{options.LineNumberColor}...{options.ResetColor}");
                }

                var lineText = sourceBuffer.GetLineText(lineNumber);
                var lineStart = sourceBuffer.GetCharacterPosition(lineNumber, 1);
                
                // Highlight matches within this line
                var lineRanges = ranges
                    .Where(r => r.Start < lineStart + lineText.Length && r.End > lineStart)
                    .Select(r => new SourceRange(
                        Math.Max(0, r.Start - lineStart),
                        Math.Min(lineText.Length, r.End - lineStart)))
                    .ToList();

                var highlightedLine = lineRanges.Any() 
                    ? HighlightRanges(lineText, lineRanges, options)
                    : lineText;

                if (options.ShowLineNumbers)
                {
                    sb.AppendLine($"{options.LineNumberColor}{lineNumber,4}:{options.ResetColor} {highlightedLine}");
                }
                else
                {
                    sb.AppendLine(highlightedLine);
                }

                lastShownLine = lineNumber;
            }

            return sb.ToString();
        }

        private static List<SourceRange> MergeOverlappingRanges(IEnumerable<SourceRange> ranges)
        {
            var sortedRanges = ranges.OrderBy(r => r.Start).ToList();
            if (!sortedRanges.Any()) return new List<SourceRange>();

            var merged = new List<SourceRange>();
            var current = sortedRanges[0];

            for (var i = 1; i < sortedRanges.Count; i++)
            {
                var next = sortedRanges[i];
                
                if (current.Overlaps(next) || current.End == next.Start)
                {
                    // Merge ranges
                    current = new SourceRange(current.Start, Math.Max(current.End, next.End));
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            
            merged.Add(current);
            return merged;
        }

        private static string HighlightWithAnsi(string text, List<SourceRange> ranges, HighlightOptions options)
        {
            if (!ranges.Any()) return text;

            var sb = new StringBuilder();
            var lastPos = 0;

            foreach (var range in ranges)
            {
                // Add text before the match
                if (range.Start > lastPos)
                {
                    sb.Append(text.Substring(lastPos, range.Start - lastPos));
                }

                // Add highlighted match
                var matchText = text.Substring(range.Start, Math.Min(range.Length, text.Length - range.Start));
                sb.Append($"{options.MatchColor}{matchText}{options.ResetColor}");

                lastPos = range.End;
            }

            // Add remaining text
            if (lastPos < text.Length)
            {
                sb.Append(text.Substring(lastPos));
            }

            return sb.ToString();
        }

        private static string HighlightWithHtml(string text, List<SourceRange> ranges, HighlightOptions options)
        {
            if (!ranges.Any()) return System.Web.HttpUtility.HtmlEncode(text);

            var sb = new StringBuilder();
            var lastPos = 0;

            foreach (var range in ranges)
            {
                // Add text before the match
                if (range.Start > lastPos)
                {
                    var beforeText = text.Substring(lastPos, range.Start - lastPos);
                    sb.Append(System.Web.HttpUtility.HtmlEncode(beforeText));
                }

                // Add highlighted match
                var matchText = text.Substring(range.Start, Math.Min(range.Length, text.Length - range.Start));
                sb.Append($"<mark>{System.Web.HttpUtility.HtmlEncode(matchText)}</mark>");

                lastPos = range.End;
            }

            // Add remaining text
            if (lastPos < text.Length)
            {
                var remainingText = text.Substring(lastPos);
                sb.Append(System.Web.HttpUtility.HtmlEncode(remainingText));
            }

            return sb.ToString();
        }

        private static string HighlightWithMarkdown(string text, List<SourceRange> ranges, HighlightOptions options)
        {
            if (!ranges.Any()) return text;

            var sb = new StringBuilder();
            var lastPos = 0;

            foreach (var range in ranges)
            {
                // Add text before the match
                if (range.Start > lastPos)
                {
                    sb.Append(text.Substring(lastPos, range.Start - lastPos));
                }

                // Add highlighted match
                var matchText = text.Substring(range.Start, Math.Min(range.Length, text.Length - range.Start));
                sb.Append($"**{matchText}**");

                lastPos = range.End;
            }

            // Add remaining text
            if (lastPos < text.Length)
            {
                sb.Append(text.Substring(lastPos));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Check if the terminal supports ANSI colors
        /// </summary>
        public static bool SupportsAnsiColors()
        {
            return TreePrinter.SupportsColors();
        }
    }
} 