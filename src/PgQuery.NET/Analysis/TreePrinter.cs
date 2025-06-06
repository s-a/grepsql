using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace PgQuery.NET.Analysis
{
    /// <summary>
    /// AST tree printing functionality with color support for debugging
    /// </summary>
    public static class TreePrinter
    {
        // ANSI color codes for terminal output
        private static class Colors
        {
            public const string Reset = "\u001b[0m";
            public const string Green = "\u001b[32m";      // Matched nodes
            public const string Red = "\u001b[31m";        // Unmatched nodes
            public const string Gray = "\u001b[90m";       // Skipped nodes
            public const string Yellow = "\u001b[33m";     // Field names
            public const string Cyan = "\u001b[36m";       // Values
            public const string Blue = "\u001b[34m";       // Node types
        }

        /// <summary>
        /// Node status for coloring in debug mode
        /// </summary>
        public enum NodeStatus
        {
            Normal,
            Matched,
            Unmatched,
            Skipped
        }

        /// <summary>
        /// Tree display mode
        /// </summary>
        public enum TreeMode
        {
            Clean,  // Hide empty arrays, default values, locations
            Full    // Show everything
        }

        /// <summary>
        /// Print an AST tree with optional coloring for debug mode
        /// </summary>
        public static void PrintTree(IMessage node, bool useColors = false, int maxDepth = 10, NodeStatus status = NodeStatus.Normal, TreeMode mode = TreeMode.Clean, HashSet<IMessage>? matchingPath = null)
        {
            var output = FormatTree(node, useColors, maxDepth, status, mode, matchingPath);
            Console.WriteLine(output);
        }

        /// <summary>
        /// Format an AST tree as a string with optional coloring
        /// </summary>
        public static string FormatTree(IMessage node, bool useColors = false, int maxDepth = 10, NodeStatus status = NodeStatus.Normal, TreeMode mode = TreeMode.Clean, HashSet<IMessage>? matchingPath = null)
        {
            var sb = new StringBuilder();
            FormatNode(node, sb, 0, maxDepth, useColors, status, new HashSet<IMessage>(), mode, matchingPath);
            return sb.ToString();
        }

        /// <summary>
        /// Print a node with match status information during pattern matching
        /// </summary>
        public static void PrintNodeWithStatus(IMessage node, NodeStatus status, int depth, bool useColors = true)
        {
            var indent = new string(' ', depth * 2);
            var nodeType = node.GetType().Name;
            
            if (useColors)
            {
                var color = status switch
                {
                    NodeStatus.Matched => Colors.Green,
                    NodeStatus.Unmatched => Colors.Red,
                    NodeStatus.Skipped => Colors.Gray,
                    _ => Colors.Blue
                };
                
                var statusIcon = status switch
                {
                    NodeStatus.Matched => "✓",
                    NodeStatus.Unmatched => "✗",
                    NodeStatus.Skipped => "•",
                    _ => "→"
                };
                
                Console.WriteLine($"{indent}{color}{statusIcon} {nodeType}{Colors.Reset}");
            }
            else
            {
                var statusText = status switch
                {
                    NodeStatus.Matched => "[MATCH]",
                    NodeStatus.Unmatched => "[NO MATCH]",
                    NodeStatus.Skipped => "[SKIP]",
                    _ => ""
                };
                
                Console.WriteLine($"{indent}{statusText} {nodeType}");
            }
        }

        private static void FormatNode(IMessage node, StringBuilder sb, int depth, int maxDepth, 
            bool useColors, NodeStatus status, HashSet<IMessage> visited, TreeMode mode = TreeMode.Clean, HashSet<IMessage>? matchingPath = null)
        {
            if (node == null || depth > maxDepth || visited.Contains(node))
            {
                return;
            }

            visited.Add(node);
            var indent = new string(' ', depth * 2);
            var nodeType = node.GetType().Name;

            // Determine node status based on matching path
            var nodeStatus = status;
            if (matchingPath != null && matchingPath.Contains(node))
            {
                nodeStatus = NodeStatus.Matched;
            }

            // Apply color based on status
            if (useColors)
            {
                var color = nodeStatus switch
                {
                    NodeStatus.Matched => Colors.Green,
                    NodeStatus.Unmatched => Colors.Red,
                    NodeStatus.Skipped => Colors.Gray,
                    _ => Colors.Blue
                };
                
                var statusIcon = nodeStatus switch
                {
                    NodeStatus.Matched => "✓ ",
                    NodeStatus.Unmatched => "✗ ",
                    NodeStatus.Skipped => "• ",
                    _ => ""
                };
                
                sb.AppendLine($"{indent}{color}{statusIcon}{nodeType}{Colors.Reset}");
            }
            else
            {
                sb.AppendLine($"{indent}{nodeType}");
            }

            // Get all fields of the message
            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                if (value == null) continue;

                var fieldName = ConvertToCamelCase(field.Name);
                
                // In clean mode, skip certain fields and values
                if (mode == TreeMode.Clean && ShouldSkipField(fieldName, value))
                    continue;

                // In clean mode, skip empty lists
                if (mode == TreeMode.Clean && value is IList list && list.Count == 0)
                    continue;

                var fieldIndent = new string(' ', (depth + 1) * 2);
                
                // Skip showing field name for single IMessage values to avoid redundancy
                if (value is IMessage)
                {
                    // Don't show the field name, just format the message directly with proper indentation
                    FormatNode((IMessage)value, sb, depth + 1, maxDepth, useColors, NodeStatus.Normal, visited, mode, matchingPath);
                }
                else
                {
                    // Show field name for primitive values and lists
                    if (useColors)
                    {
                        sb.Append($"{fieldIndent}{Colors.Yellow}{fieldName}{Colors.Reset}: ");
                    }
                    else
                    {
                        sb.Append($"{fieldIndent}{fieldName}: ");
                    }

                    FormatValue(value, sb, depth + 1, maxDepth, useColors, visited, mode, matchingPath);
                }
            }

            visited.Remove(node);
        }

        private static void FormatValue(object value, StringBuilder sb, int depth, int maxDepth, 
            bool useColors, HashSet<IMessage> visited, TreeMode mode = TreeMode.Clean, HashSet<IMessage>? matchingPath = null)
        {
            if (value == null)
            {
                if (useColors)
                    sb.AppendLine($"{Colors.Gray}null{Colors.Reset}");
                else
                    sb.AppendLine("null");
                return;
            }

            if (value is IMessage childMessage)
            {
                FormatNode(childMessage, sb, depth, maxDepth, useColors, NodeStatus.Normal, visited, mode, matchingPath);
            }
            else if (value is IList list)
            {
                if (list.Count == 0)
                {
                    // In clean mode, skip empty arrays completely
                    if (mode == TreeMode.Clean)
                        return;
                        
                    if (useColors)
                        sb.AppendLine($"{Colors.Gray}[]{Colors.Reset}");
                    else
                        sb.AppendLine("[]");
                }
                else
                {
                    sb.AppendLine($"[{list.Count} items]");
                    var listIndent = new string(' ', (depth + 1) * 2);
                    
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (depth > maxDepth) break;
                        
                        sb.Append($"{listIndent}[{i}]: ");
                        if (list[i] is IMessage itemMessage)
                        {
                            sb.AppendLine();
                            FormatNode(itemMessage, sb, depth + 1, maxDepth, useColors, NodeStatus.Normal, visited, mode, matchingPath);
                        }
                        else
                        {
                            FormatPrimitiveValue(list[i]!, sb, useColors);
                        }
                    }
                }
            }
            else
            {
                FormatPrimitiveValue(value, sb, useColors);
            }
        }

        private static void FormatPrimitiveValue(object value, StringBuilder sb, bool useColors)
        {
            if (value == null)
            {
                if (useColors)
                    sb.AppendLine($"{Colors.Gray}null{Colors.Reset}");
                else
                    sb.AppendLine("null");
                return;
            }

            var valueStr = value.ToString();
            
            if (useColors)
            {
                // Different colors for different types of values
                var color = value switch
                {
                    string => Colors.Cyan,
                    bool => Colors.Yellow,
                    int or long or double or float => Colors.Cyan,
                    _ => Colors.Reset
                };
                
                sb.AppendLine($"{color}{valueStr}{Colors.Reset}");
            }
            else
            {
                sb.AppendLine(valueStr);
            }
        }

        /// <summary>
        /// Determines if a field should be skipped in clean mode
        /// </summary>
        private static bool ShouldSkipField(string fieldName, object value)
        {
            // Skip location fields
            if (fieldName == "location")
                return true;

            // Skip default enum values
            if (value is string stringValue)
            {
                if (stringValue == "Default" || stringValue == "SetopNone" || 
                    stringValue == "LIMIT_OPTION_DEFAULT" || stringValue == "SETOP_NONE" ||
                    stringValue == "CoerceExplicitCall" || stringValue == "COERCE_EXPLICIT_CALL" ||
                    stringValue == "AexprOp" || stringValue == "AEXPR_OP")
                    return true;
            }

            // Skip enum values that are default/none
            if (value != null)
            {
                var valueStr = value.ToString();
                if (valueStr == "Default" || valueStr == "SetopNone" || valueStr == "LimitOptionDefault")
                    return true;
            }

            // Skip false boolean values for certain fields
            if (value is bool boolValue && !boolValue)
            {
                if (fieldName == "isnull" || fieldName == "all" || fieldName == "groupDistinct" ||
                    fieldName == "aggStar" || fieldName == "aggDistinct" || fieldName == "funcVariadic")
                    return true;
            }

            // Skip True boolean values for certain default fields
            if (value is bool trueBoolValue && trueBoolValue)
            {
                if (fieldName == "inh") // Default inheritance value
                    return true;
            }

            // Skip empty string values for certain fields
            if (value is string emptyStr && string.IsNullOrEmpty(emptyStr))
            {
                if (fieldName == "catalogname" || fieldName == "schemaname" || fieldName == "name")
                    return true;
            }

            // Skip single character persistence values
            if (value is string singleChar && singleChar.Length == 1)
            {
                if (fieldName == "relpersistence" && singleChar == "p") // Default persistence
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Convert snake_case to camelCase
        /// </summary>
        private static string ConvertToCamelCase(string snakeCase)
        {
            if (string.IsNullOrEmpty(snakeCase))
                return snakeCase;

            var parts = snakeCase.Split('_');
            if (parts.Length == 1)
                return snakeCase; // Already camelCase or single word

            var result = new StringBuilder();
            result.Append(parts[0]); // First part stays lowercase

            for (int i = 1; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    result.Append(char.ToUpper(parts[i][0]));
                    if (parts[i].Length > 1)
                        result.Append(parts[i].Substring(1));
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Check if the terminal supports ANSI colors
        /// </summary>
        public static bool SupportsColors()
        {
            // Basic check for color support
            var term = Environment.GetEnvironmentVariable("TERM");
            var colorTerm = Environment.GetEnvironmentVariable("COLORTERM");
            
            return !string.IsNullOrEmpty(term) && 
                   (term.Contains("color") || term.Contains("xterm") || !string.IsNullOrEmpty(colorTerm));
        }
    }
} 