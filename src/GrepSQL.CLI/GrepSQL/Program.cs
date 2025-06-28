using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
using Google.Protobuf;
using GrepSQL;
using GrepSQL.SQL;
using GrepSQL.AST;

namespace GrepSQL
{
    // Simple TreePrinter implementation
    public static class TreePrinter
    {
        public enum TreeMode
        {
            Clean,
            Full
        }

        public enum NodeStatus
        {
            Normal,
            Matched
        }

        public static bool SupportsColors()
        {
            return !Console.IsOutputRedirected;
        }

        public static void PrintTree(IMessage node, bool useColors = true, int maxDepth = 8, NodeStatus status = NodeStatus.Normal, TreeMode mode = TreeMode.Clean, HashSet<IMessage>? matchingPath = null)
        {
            PrintNode(node, 0, maxDepth, useColors, status, mode, matchingPath);
        }

        private static void PrintNode(IMessage node, int depth, int maxDepth, bool useColors, NodeStatus status, TreeMode mode, HashSet<IMessage>? matchingPath)
        {
            if (depth > maxDepth || node == null) return;

            var indent = new string(' ', depth * 2);
            var isMatched = matchingPath?.Contains(node) == true || status == NodeStatus.Matched;
            var nodeType = node.Descriptor?.Name ?? "Unknown";

            if (useColors && isMatched)
            {
                Console.WriteLine($"{indent}\u001b[32m{nodeType}\u001b[0m"); // Green for matched
            }
            else
            {
                Console.WriteLine($"{indent}{nodeType}");
            }

            // Print fields based on mode
            if (mode == TreeMode.Full || depth < 3)
            {
                foreach (var field in node.Descriptor?.Fields.InFieldNumberOrder() ?? Enumerable.Empty<Google.Protobuf.Reflection.FieldDescriptor>())
                {
                    var value = field.Accessor.GetValue(node);
                    if (value != null)
                    {
                        PrintField(field.Name, value, depth + 1, maxDepth, useColors, mode, matchingPath);
                    }
                }
            }
        }

        private static void PrintField(string fieldName, object value, int depth, int maxDepth, bool useColors, TreeMode mode, HashSet<IMessage>? matchingPath)
        {
            if (depth > maxDepth) return;

            var indent = new string(' ', depth * 2);

            if (value is IMessage message)
            {
                Console.WriteLine($"{indent}{fieldName}:");
                PrintNode(message, depth + 1, maxDepth, useColors, NodeStatus.Normal, mode, matchingPath);
            }
            else if (value is System.Collections.IList list && list.Count > 0)
            {
                Console.WriteLine($"{indent}{fieldName}: [{list.Count} items]");
                for (int i = 0; i < Math.Min(list.Count, 5); i++)
                {
                    Console.WriteLine($"{indent}  [{i}]:");
                    if (list[i] is IMessage listMessage)
                    {
                        PrintNode(listMessage, depth + 2, maxDepth, useColors, NodeStatus.Normal, mode, matchingPath);
                    }
                    else
                    {
                        Console.WriteLine($"{indent}    {list[i]}");
                    }
                }
                if (list.Count > 5)
                {
                    Console.WriteLine($"{indent}  ... and {list.Count - 5} more");
                }
            }
            else if (value is string str && !string.IsNullOrEmpty(str))
            {
                Console.WriteLine($"{indent}{fieldName}: {str}");
            }
            else if (value is bool || value is int || value is long || value is double)
            {
                Console.WriteLine($"{indent}{fieldName}: {value}");
            }
        }
    }

    // Simple HighlightOptions implementation
    public class HighlightOptions
    {
        public string Style { get; set; } = "ansi";
        public int ContextLines { get; set; } = 0;
        public bool ShowLineNumbers { get; set; } = false;
        public bool ShowMatchInfo { get; set; } = false;
    }

    public class Options
    {
        [Value(0, MetaName = "pattern", Required = false, HelpText = "SQL pattern expression to match against")]
        public string? PositionalPattern { get; set; }

        [Value(1, MetaName = "files", HelpText = "SQL files to search through")]
        public IEnumerable<string> PositionalFiles { get; set; } = new List<string>();

        [Option('p', "pattern", Required = false, HelpText = "SQL pattern expression to match against (alternative to positional)")]
        public string? Pattern { get; set; }

        [Option('f', "files", HelpText = "SQL files to search through (alternative to positional)")]
        public IEnumerable<string> Files { get; set; } = new List<string>();

        [Option("from-sql", HelpText = "Inline SQL to search instead of files")]
        public string? FromSql { get; set; }

        [Option("ast", HelpText = "Print AST instead of SQL")]
        public bool PrintAst { get; set; }

        [Option("debug", HelpText = "Print matching details for debugging")]
        public bool Debug { get; set; }

        [Option("no-color", HelpText = "Disable colored output")]
        public bool NoColor { get; set; }

        [Option("tree", HelpText = "Print AST as a formatted tree")]
        public bool PrintTree { get; set; }

        [Option("tree-mode", HelpText = "Tree display mode: clean (default) or full")]
        public string? TreeMode { get; set; }

        [Option("verbose", HelpText = "Enable verbose debug output")]
        public bool Verbose { get; set; }

        [Option('c', "count", HelpText = "Only print count of matches")]
        public bool CountOnly { get; set; }

        [Option('n', "line-numbers", HelpText = "Show line numbers in output")]
        public bool ShowLineNumbers { get; set; }

        [Option("no-filename", HelpText = "Don't show filename in output")]
        public bool NoFilename { get; set; }

        [Option("highlight", HelpText = "Highlight matching SQL parts")]
        public bool HighlightMatches { get; set; }

        [Option("highlight-style", HelpText = "Highlighting style: ansi, html, markdown")]
        public string? HighlightStyle { get; set; }

        [Option("context", HelpText = "Show context lines around matches (requires --highlight)")]
        public int? ContextLines { get; set; }

        [Option("captures-only", HelpText = "Print only captured nodes/values from patterns")]
        public bool CapturesOnly { get; set; }

        [Option('X', "only-exp", HelpText = "Show only the expression tree generated by the parser")]
        public bool OnlyExpression { get; set; }
    }

    public class SqlMatch
    {
        public string FileName { get; set; } = "";
        public string Sql { get; set; } = "";
        public object? Ast { get; set; }
        public IMessage? MatchedNode { get; set; }
        public int LineNumber { get; set; }
        public string MatchDetails { get; set; } = "";
        public HashSet<IMessage>? MatchingPath { get; set; }
        public List<IMessage>? MatchingNodes { get; set; }
        public List<object>? Captures { get; set; }
    }

    // Simple SqlHighlighter implementation
    public static class SqlHighlighter
    {
        public static string HighlightSql(string sql, List<IMessage> matchingNodes, HighlightOptions options)
        {
            // Simple implementation - just return the SQL with basic highlighting
            if (options.Style == "ansi")
            {
                return $"\u001b[32m{sql}\u001b[0m"; // Green highlighting
            }
            else if (options.Style == "html")
            {
                return $"<mark>{sql}</mark>";
            }
            else if (options.Style == "markdown")
            {
                return $"**{sql}**";
            }
            return sql;
        }
    }

    // Simple HighlightStyle enum
    public enum HighlightStyle
    {
        Ansi,
        Html,
        Markdown
    }

    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    options => RunGrepSql(options),
                    errors => 1);
        }

        static int RunGrepSql(Options options)
        {
            try
            {
                // Determine pattern (positional takes precedence)
                var pattern = !string.IsNullOrEmpty(options.PositionalPattern) 
                    ? options.PositionalPattern 
                    : options.Pattern;

                if (string.IsNullOrEmpty(pattern))
                {
                    Console.Error.WriteLine("Error: Pattern is required. Provide it as first argument or use -p/--pattern option.");
                    return 1;
                }

                // Handle --only-exp flag
                if (options.OnlyExpression)
                {
                    try
                    {
                        var expressionTree = PatternMatcher.GetExpressionTree(pattern);
                        Console.WriteLine(expressionTree);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error parsing pattern: {ex.Message}");
                        return 1;
                    }
                }

                // Determine files (positional takes precedence, combine if both provided)
                var allFiles = new List<string>();
                if (options.PositionalFiles.Any())
                {
                    allFiles.AddRange(options.PositionalFiles);
                }
                if (options.Files.Any())
                {
                    allFiles.AddRange(options.Files);
                }

                // Expand glob patterns in file paths
                var expandedFiles = ExpandGlobPatterns(allFiles);

                var matches = new List<SqlMatch>();

                if (!string.IsNullOrEmpty(options.FromSql))
                {
                    // Process inline SQL
                    ProcessSql("(inline)", options.FromSql, pattern, options.Debug, options.Verbose, matches);
                }
                else if (expandedFiles.Any())
                {
                    // Process files
                    foreach (var file in expandedFiles)
                    {
                        if (!File.Exists(file))
                        {
                            Console.Error.WriteLine($"Error: File '{file}' not found.");
                            continue;
                        }

                        try
                        {
                            var content = File.ReadAllText(file);
                            ProcessSql(file, content, pattern, options.Debug, options.Verbose, matches);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Error reading file '{file}': {ex.Message}");
                        }
                    }
                }
                else
                {
                    // Read from stdin
                    var content = Console.In.ReadToEnd();
                    ProcessSql("(stdin)", content, pattern, options.Debug, options.Verbose, matches);
                }

                // Output results
                if (options.CountOnly)
                {
                    Console.WriteLine(matches.Count);
                }
                else
                {
                    foreach (var match in matches)
                    {
                        PrintMatch(match, options);
                    }
                }

                return matches.Any() ? 0 : 1; // Exit code 0 if matches found, 1 if none
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (options.Debug)
                {
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return 2;
            }
        }

        static List<string> ExpandGlobPatterns(IEnumerable<string> patterns)
        {
            var expandedFiles = new List<string>();
            
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                
                // Check if the pattern contains glob characters
                if (pattern.Contains('*') || pattern.Contains('?'))
                {
                    try
                    {
                        // Handle different glob patterns
                        if (pattern.StartsWith("**"))
                        {
                            // Recursive glob pattern like **/*.sql
                            var searchPattern = pattern.Substring(pattern.IndexOf('/') + 1);
                            var files = Directory.GetFiles(".", searchPattern, SearchOption.AllDirectories);
                            expandedFiles.AddRange(files.Select(f => Path.GetRelativePath(".", f)));
                        }
                        else if (pattern.Contains('/'))
                        {
                            // Pattern with directory like dir/*.sql
                            var directory = Path.GetDirectoryName(pattern) ?? ".";
                            var searchPattern = Path.GetFileName(pattern);
                            
                            if (Directory.Exists(directory))
                            {
                                var files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
                                expandedFiles.AddRange(files.Select(f => Path.GetRelativePath(".", f)));
                            }
                        }
                        else
                        {
                            // Simple pattern like *.sql
                            var files = Directory.GetFiles(".", pattern, SearchOption.TopDirectoryOnly);
                            expandedFiles.AddRange(files.Select(f => Path.GetRelativePath(".", f)));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Warning: Could not expand glob pattern '{pattern}': {ex.Message}");
                        // If glob expansion fails, treat it as a literal filename
                        expandedFiles.Add(pattern);
                    }
                }
                else
                {
                    // Not a glob pattern, add as-is
                    expandedFiles.Add(pattern);
                }
            }
            
            return expandedFiles.Distinct().ToList();
        }

        static void ProcessSql(string fileName, string content, string pattern, bool debug, bool verbose, List<SqlMatch> matches)
        {
            // No caching mechanism - expressions are compiled fresh each time
            
            // Split content into individual SQL statements (basic approach)
            var sqlStatements = SplitSqlStatements(content);
            
            for (int i = 0; i < sqlStatements.Count; i++)
            {
                var sql = sqlStatements[i].Sql.Trim();
                if (string.IsNullOrEmpty(sql)) continue;

                try
                {
                    // Parse the SQL to get the AST
                    var parseResult = Postgres.ParseSql(sql);
                    if (parseResult?.ParseTree == null)
                    {
                        if (debug)
                        {
                            Console.Error.WriteLine($"[DEBUG] Failed to parse SQL: {sql}");
                        }
                        continue;
                    }

                    // Show AST tree when debugging to help understand structure
                    if (debug)
                    {
                        Console.Error.WriteLine($"[DEBUG] ======== SQL: {sql} ========");
                        Console.Error.WriteLine($"[DEBUG] ======== AST Tree Structure ========");
                        TreePrinter.PrintTree(parseResult.ParseTree, TreePrinter.SupportsColors(), 15, TreePrinter.NodeStatus.Normal, TreePrinter.TreeMode.Full);
                        Console.Error.WriteLine($"[DEBUG] ======== Pattern Matching: {pattern} ========");
                    }

                    // Use PatternMatcher.Search which delegates to Postgres.SearchInSql
                    var results = PatternMatcher.Search(pattern, sql, debug);
                    
                    if (debug && verbose)
                    {
                        Console.Error.WriteLine($"[DEBUG] SQL: {sql}");
                        Console.Error.WriteLine($"[DEBUG] Pattern: {pattern}");
                        Console.Error.WriteLine($"[DEBUG] Results: {results.Count}");
                    }

                    // Get captures using the new capture system
                    var captures = PatternMatcher.SearchWithCaptures(pattern, sql, debug);
                    
                    if (results.Count > 0 || captures.Any())
                    {
                        // Create a single match for this SQL statement with all matching nodes
                        var match = new SqlMatch
                        {
                            FileName = fileName,
                            LineNumber = sqlStatements[i].LineNumber,
                            Sql = sql,
                            Ast = parseResult.ParseTree, // Store the full parse tree
                            MatchedNode = results.FirstOrDefault(), // Store the first matched node
                            MatchingNodes = results.ToList(), // Store all matching nodes
                            MatchingPath = new HashSet<IMessage>(results), // Store matching path for highlighting
                            Captures = captures.Any() ? captures : null // Store captures
                        };

                        matches.Add(match);
                    }

                    if (captures.Any() && debug)
                    {
                        Console.Error.WriteLine($"[DEBUG] Captures: {captures.Count}");
                        for (int j = 0; j < captures.Count; j++)
                        {
                            Console.Error.WriteLine($"[DEBUG] Capture [{j}]: {captures[j]}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (debug)
                    {
                        Console.Error.WriteLine($"[ERROR] Failed to process SQL: {ex.Message}");
                        Console.Error.WriteLine($"[ERROR] SQL: {sql}");
                    }
                }
            }
        }

        static List<(string Sql, int LineNumber)> SplitSqlStatements(string content)
        {
            var statements = new List<(string, int)>();
            var chars = content.ToCharArray();
            var currentStatement = new StringBuilder();
            var startLine = 1;
            var currentLine = 1;
            var i = 0;

            while (i < chars.Length)
            {
                var ch = chars[i];

                // Track line numbers
                if (ch == '\n')
                {
                    currentLine++;
                }

                // Skip whitespace and comments at the start of statements
                if (currentStatement.Length == 0)
                {
                    if (char.IsWhiteSpace(ch))
                    {
                        if (ch == '\n')
                        {
                            startLine = currentLine;
                        }
                        i++;
                        continue;
                    }
                    
                    // Skip SQL comments at statement start
                    if (ch == '-' && i + 1 < chars.Length && chars[i + 1] == '-')
                    {
                        // Skip to end of line
                        while (i < chars.Length && chars[i] != '\n')
                        {
                            i++;
                        }
                        continue;
                    }
                    
                    // Set start line for this statement
                    startLine = currentLine;
                }

                currentStatement.Append(ch);

                // Handle different quote types
                if (ch == '\'')
                {
                    // Single quote - skip until matching quote
                    i++;
                    while (i < chars.Length)
                    {
                        currentStatement.Append(chars[i]);
                        if (chars[i] == '\'')
                        {
                            // Check for escaped quote
                            if (i + 1 < chars.Length && chars[i + 1] == '\'')
                            {
                                i++; // Skip the escaped quote
                                currentStatement.Append(chars[i]);
                            }
                            else
                            {
                                break; // End of quoted string
                            }
                        }
                        else if (chars[i] == '\n')
                        {
                            currentLine++;
                        }
                        i++;
                    }
                }
                else if (ch == '$')
                {
                    // Potential dollar quote - check for dollar-quoted string
                    var dollarTag = ExtractDollarTag(chars, i);
                    if (dollarTag != null)
                    {
                        // Add the opening tag
                        for (int j = 1; j < dollarTag.Length; j++)
                        {
                            i++;
                            currentStatement.Append(chars[i]);
                            if (chars[i] == '\n') currentLine++;
                        }
                        
                        // Now find the closing tag
                        i++;
                        while (i < chars.Length)
                        {
                            currentStatement.Append(chars[i]);
                            if (chars[i] == '\n') currentLine++;
                            
                            // Check if we found the closing tag
                            if (chars[i] == '$' && MatchesDollarTag(chars, i, dollarTag))
                            {
                                // Add the rest of the closing tag
                                for (int j = 1; j < dollarTag.Length; j++)
                                {
                                    i++;
                                    currentStatement.Append(chars[i]);
                                    if (chars[i] == '\n') currentLine++;
                                }
                                break;
                            }
                            i++;
                        }
                    }
                }
                else if (ch == '-' && i + 1 < chars.Length && chars[i + 1] == '-')
                {
                    // SQL comment - skip to end of line
                    while (i < chars.Length && chars[i] != '\n')
                    {
                        currentStatement.Append(chars[i]);
                        i++;
                    }
                    if (i < chars.Length)
                    {
                        currentStatement.Append(chars[i]); // Add the newline
                        currentLine++;
                    }
                }
                else if (ch == ';')
                {
                    // End of statement
                    var sql = currentStatement.ToString().Trim();
                    if (!string.IsNullOrEmpty(sql))
                    {
                        statements.Add((sql, startLine));
                    }
                    currentStatement.Clear();
                }

                i++;
            }

            // Add remaining statement if any
            if (currentStatement.Length > 0)
            {
                var sql = currentStatement.ToString().Trim();
                if (!string.IsNullOrEmpty(sql))
                {
                    statements.Add((sql, startLine));
                }
            }

            return statements;
        }

        static string? ExtractDollarTag(char[] chars, int startPos)
        {
            if (startPos >= chars.Length || chars[startPos] != '$')
                return null;

            var tag = new StringBuilder("$");
            var i = startPos + 1;

            // Extract the tag (alphanumeric characters between $...$)
            while (i < chars.Length && chars[i] != '$')
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    return null; // Invalid dollar tag
                tag.Append(chars[i]);
                i++;
            }

            if (i >= chars.Length || chars[i] != '$')
                return null; // No closing $

            tag.Append('$');
            return tag.ToString();
        }

        static bool MatchesDollarTag(char[] chars, int pos, string tag)
        {
            if (pos + tag.Length > chars.Length)
                return false;

            for (int i = 0; i < tag.Length; i++)
            {
                if (chars[pos + i] != tag[i])
                    return false;
            }
            return true;
        }

        static TreePrinter.TreeMode ParseTreeMode(string? treeModeStr)
        {
            return treeModeStr?.ToLowerInvariant() switch
            {
                "full" => TreePrinter.TreeMode.Full,
                "clean" or "" or null => TreePrinter.TreeMode.Clean,
                _ => TreePrinter.TreeMode.Clean // Default to clean for invalid values
            };
        }

        static void PrintMatch(SqlMatch match, Options options)
        {
            var prefix = "";
            
            if (!options.NoFilename && match.FileName != "(stdin)" && match.FileName != "(inline)")
            {
                prefix += $"{match.FileName}:";
            }
            
            if (options.ShowLineNumbers)
            {
                prefix += $"{match.LineNumber}:";
            }

            // Handle captures-only output
            if (options.CapturesOnly)
            {
                if (match.Captures != null && match.Captures.Count > 0)
                {
                    for (int i = 0; i < match.Captures.Count; i++)
                    {
                        var capturedValue = match.Captures[i];
                        
                        // If --tree is specified with --captures-only, show tree structure of captured nodes
                        if (options.PrintTree && capturedValue is IMessage capturedNode)
                        {
                            Console.WriteLine($"{prefix}[CAPTURED NODE {i}]");
                            
                            var useColors = !options.NoColor && TreePrinter.SupportsColors();
                            var treeMode = ParseTreeMode(options.TreeMode);
                            TreePrinter.PrintTree(capturedNode, useColors, maxDepth: 8, TreePrinter.NodeStatus.Matched, treeMode);
                        }
                        else
                        {
                            // Default behavior: extract simple values or show object string representation
                            string? displayValue = null;
                            
                            if (capturedValue is IMessage node)
                            {
                                // Only call ExtractNodeValue if the node has a proper descriptor
                                try
                                {
                                    displayValue = ExtractNodeValue(node);
                                    if (string.IsNullOrEmpty(displayValue))
                                    {
                                        displayValue = node.Descriptor?.Name ?? "Unknown";
                                    }
                                }
                                catch (NotSupportedException)
                                {
                                    // Handle FieldValueWrapper and similar cases
                                    displayValue = node.ToString();
                                }
                            }
                            else
                            {
                                displayValue = capturedValue?.ToString();
                            }
                            
                            if (!string.IsNullOrEmpty(displayValue))
                            {
                                Console.WriteLine($"{prefix}{displayValue}");
                            }
                            else
                            {
                                Console.WriteLine($"{prefix}[EMPTY CAPTURE {i}]");
                            }
                        }
                    }
                }
                else
                {
                    // No captures found
                    Console.WriteLine($"{prefix}[NO CAPTURES]");
                }
                
                if (!options.CountOnly)
                {
                    Console.WriteLine(); // Empty line between matches
                }
                return;
            }

            if (options.Debug)
            {
                Console.WriteLine($"{prefix}[DEBUG] Match Details:");
                Console.WriteLine(match.MatchDetails);
                Console.WriteLine($"{prefix}[DEBUG] End Details");
                Console.WriteLine();
            }

            if (options.PrintTree && match.Ast != null)
            {
                Console.WriteLine($"{prefix}[TREE]");
                var useColors = !options.NoColor && TreePrinter.SupportsColors();
                var treeMode = ParseTreeMode(options.TreeMode);
                
                // Print the tree for the parse tree root
                if (match.Ast is IMessage astMessage)
                {
                    TreePrinter.PrintTree(astMessage, useColors, maxDepth: 8, TreePrinter.NodeStatus.Normal, treeMode, match.MatchingPath);
                }
                else
                {
                    Console.WriteLine("Unable to display tree - AST is not in expected format");
                }
            }
            else if (options.PrintAst)
            {
                Console.WriteLine($"{prefix}[AST]");
                
                if (match.Ast != null)
                {
                    try
                    {
                        // Convert protobuf message to JSON
                        var jsonFormatter = new Google.Protobuf.JsonFormatter(Google.Protobuf.JsonFormatter.Settings.Default.WithFormatDefaultValues(true));
                        var json = jsonFormatter.Format((IMessage)match.Ast);
                        
                        // Pretty print the JSON
                        var jsonDoc = JsonDocument.Parse(json);
                        var prettyJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions 
                        { 
                            WriteIndented = true
                        });
                        
                        Console.WriteLine(prettyJson);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error serializing AST: {ex.Message}");
                        Console.WriteLine($"AST Type: {match.Ast.GetType().Name}");
                    }
                }
                else
                {
                    Console.WriteLine("null");
                }
            }
            else
            {
                // Handle highlighting if requested
                var outputSql = match.Sql;
                
                if (options.HighlightMatches && match.MatchingNodes?.Any() == true)
                {
                    var highlightOptions = CreateHighlightOptions(options);
                    
                    if (options.ContextLines.HasValue)
                    {
                        outputSql = SqlHighlighter.HighlightSql(
                            match.Sql, 
                            match.MatchingNodes, 
                            highlightOptions);
                        
                        // For context view, don't add prefix to each line since it's already formatted
                        Console.WriteLine($"{prefix}[CONTEXT]");
                        Console.WriteLine(outputSql);
                    }
                    else if (options.Debug || options.Verbose)
                    {
                        outputSql = SqlHighlighter.HighlightSql(match.Sql, match.MatchingNodes, highlightOptions);
                        Console.WriteLine($"{prefix}{outputSql}");
                    }
                    else
                    {
                        outputSql = SqlHighlighter.HighlightSql(match.Sql, match.MatchingNodes, highlightOptions);
                        Console.WriteLine($"{prefix}{outputSql}");
                    }
                }
                else
                {
                    Console.WriteLine($"{prefix}{outputSql}");
                }
            }
            
            if (!options.CountOnly)
            {
                Console.WriteLine(); // Empty line between matches
            }
        }

        static HighlightOptions CreateHighlightOptions(Options options)
        {
            var highlightStyle = options.HighlightStyle?.ToLowerInvariant() switch
            {
                "html" => "html",
                "markdown" => "markdown", 
                "ansi" => "ansi",
                null => "ansi",
                _ => "ansi"
            };

            return new HighlightOptions
            {
                Style = highlightStyle,
                ContextLines = options.ContextLines ?? 0,
                ShowLineNumbers = options.ShowLineNumbers,
                ShowMatchInfo = options.Debug || options.Verbose
            };
        }

        /// <summary>
        /// Extract a meaningful string value from a captured AST node
        /// </summary>
        static string? ExtractNodeValue(IMessage node)
        {
            if (node == null) return null;
            
            try
            {
                if (node.Descriptor == null) return null;
                
                // Handle common node types that contain useful values
                var nodeTypeName = node.Descriptor.Name;

            switch (nodeTypeName)
            {
                case "A_Const":
                    // Extract constant values (strings, numbers, etc.)
                    return ExtractConstantValue(node);
                    
                case "RangeVar":
                    // Extract table name from relname field
                    return ExtractFieldValue(node, "relname");
                    
                case "ColumnRef":
                    // Extract column name (might be in nested structure)
                    return ExtractColumnName(node);
                    
                case "FuncCall":
                    // Extract function name
                    return ExtractFunctionName(node);
                    
                case "String":
                    // Extract string value
                    return ExtractFieldValue(node, "sval");
                    
                default:
                    // For other node types, try to extract common field names
                    var commonFields = new[] { "sval", "ival", "fval", "bval", "relname", "colname", "funcname", "name" };
                    foreach (var fieldName in commonFields)
                    {
                        var value = ExtractFieldValue(node, fieldName);
                        if (!string.IsNullOrEmpty(value))
                            return value;
                    }
                    return null;
            }
            }
            catch (NotSupportedException)
            {
                // Handle cases like FieldValueWrapper that don't have a proper descriptor
                return node.ToString();
            }
            catch (Exception)
            {
                // Handle any other exceptions gracefully
                return node.ToString();
            }
        }

        /// <summary>
        /// Extract constant value from A_Const node
        /// </summary>
        static string? ExtractConstantValue(IMessage node)
        {
            var descriptor = node.Descriptor;
            if (descriptor == null) return null;

            // A_Const typically has nested values like String, Integer, Float, etc.
            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                var value = field.Accessor.GetValue(node);
                if (value is IMessage nestedMessage)
                {
                    var nestedValue = ExtractNodeValue(nestedMessage);
                    if (!string.IsNullOrEmpty(nestedValue))
                        return nestedValue;
                }
            }
            return null;
        }

        /// <summary>
        /// Extract column name from ColumnRef which might have nested fields
        /// </summary>
        static string? ExtractColumnName(IMessage node)
        {
            // ColumnRef typically has a "fields" array containing String nodes
            var fieldsValue = ExtractFieldValue(node, "fields");
            if (!string.IsNullOrEmpty(fieldsValue))
                return fieldsValue;

            // Try to extract from nested structure
            var descriptor = node.Descriptor;
            if (descriptor == null) return null;

            var fieldsField = descriptor.Fields.InFieldNumberOrder()
                .FirstOrDefault(f => f.Name == "fields");
                
            if (fieldsField?.IsRepeated == true)
            {
                var fieldsList = fieldsField.Accessor.GetValue(node) as System.Collections.IList;
                if (fieldsList != null && fieldsList.Count > 0)
                {
                    if (fieldsList[0] is IMessage firstField)
                    {
                        return ExtractNodeValue(firstField);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract function name from FuncCall node
        /// </summary>
        static string? ExtractFunctionName(IMessage node)
        {
            // FuncCall typically has a "funcname" field with an array of String nodes
            var funcNameField = ExtractFieldValue(node, "funcname");
            if (!string.IsNullOrEmpty(funcNameField))
                return funcNameField;

            // Try to extract from nested funcname structure
            var descriptor = node.Descriptor;
            if (descriptor == null) return null;

            var funcnameField = descriptor.Fields.InFieldNumberOrder()
                .FirstOrDefault(f => f.Name == "funcname");
                
            if (funcnameField?.IsRepeated == true)
            {
                var funcnameList = funcnameField.Accessor.GetValue(node) as System.Collections.IList;
                if (funcnameList != null && funcnameList.Count > 0)
                {
                    if (funcnameList[0] is IMessage firstNamePart)
                    {
                        return ExtractNodeValue(firstNamePart);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract field value by field name
        /// </summary>
        static string? ExtractFieldValue(IMessage node, string fieldName)
        {
            if (node?.Descriptor == null) return null;

            var field = node.Descriptor.Fields.InFieldNumberOrder()
                .FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));

            if (field == null) return null;

            try
            {
                var value = field.Accessor.GetValue(node);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
