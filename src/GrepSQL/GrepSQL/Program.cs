using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
using Google.Protobuf;
using PgQuery.NET;
using PgQuery.NET.Analysis;
using PgQuery.NET.AST;

namespace GrepSQL
{
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
    }

    public class SqlMatch
    {
        public string FileName { get; set; } = "";
        public string Sql { get; set; } = "";
        public object? Ast { get; set; }
        public int LineNumber { get; set; }
        public string MatchDetails { get; set; } = "";
        public HashSet<IMessage>? MatchingPath { get; set; }
        public List<IMessage>? MatchingNodes { get; set; }
        public IReadOnlyDictionary<string, List<IMessage>>? Captures { get; set; }
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
            // Split content into individual SQL statements (basic approach)
            var sqlStatements = SplitSqlStatements(content);
            
            for (int i = 0; i < sqlStatements.Count; i++)
            {
                var sql = sqlStatements[i].Sql.Trim();
                if (string.IsNullOrEmpty(sql)) continue;

                                try
                {
                    // Use Search for recursive pattern matching instead of MatchWithDetails
                    var searchResults = SqlPatternMatcher.Search(pattern, sql, debug);
                    bool success = searchResults.Count > 0;
                    
                    // Show debug details even for failed matches when debug is enabled
                    if (debug && !success)
                    {
                        Console.Error.WriteLine($"[DEBUG] Pattern match failed for {fileName} at line {sqlStatements[i].LineNumber}:");
                        Console.Error.WriteLine("Pattern did not match");
                        Console.Error.WriteLine();
                    }
                    
                    if (success)
                    {
                        // Collect captures after successful pattern matching
                        var captures = SqlPatternMatcher.GetCaptures();
                        
                        // Check if any results are from DoStmt extraction
                        bool hasDoStmtResults = searchResults.Any(r => r.GetType().Name == "DoStmtWrapper");
                        
                        if (hasDoStmtResults)
                        {
                            // Handle DoStmt matches separately
                            foreach (var result in searchResults)
                            {
                                if (result.GetType().Name == "DoStmtWrapper")
                                {
                                    // Use reflection to get ExtractedSql property
                                    var extractedSqlProperty = result.GetType().GetProperty("ExtractedSql");
                                    var innerNodeProperty = result.GetType().GetProperty("InnerNode");
                                    
                                    if (extractedSqlProperty != null && innerNodeProperty != null)
                                    {
                                        var extractedSql = extractedSqlProperty.GetValue(result) as string ?? sql;
                                        var innerNode = innerNodeProperty.GetValue(result) as IMessage;
                                        
                                        if (innerNode != null)
                                        {
                                            var ast = SqlPatternMatcher.ParseSql(extractedSql);
                                            var matchingPath = new HashSet<IMessage> { innerNode };
                                            var matchingNodes = new List<IMessage> { innerNode };
                                        
                                            string details = debug ? "Found match inside DO statement" : "";
                                            
                                            matches.Add(new SqlMatch
                                            {
                                                FileName = fileName,
                                                Sql = extractedSql,
                                                Ast = innerNode,
                                                LineNumber = sqlStatements[i].LineNumber,
                                                MatchDetails = details,
                                                MatchingPath = matchingPath,
                                                MatchingNodes = matchingNodes,
                                                Captures = captures
                                            });
                                        }
                                    }
                                }
                                else
                                {
                                    // Regular match
                                    var ast = SqlPatternMatcher.ParseSql(sql);
                                    var matchingPath = new HashSet<IMessage> { result };
                                    var matchingNodes = new List<IMessage> { result };
                                    
                                    string details = debug ? "Found regular match" : "";
                                    
                                    matches.Add(new SqlMatch
                                    {
                                        FileName = fileName,
                                        Sql = sql,
                                        Ast = result,
                                        LineNumber = sqlStatements[i].LineNumber,
                                        MatchDetails = details,
                                        MatchingPath = matchingPath,
                                        MatchingNodes = matchingNodes,
                                        Captures = captures
                                    });
                                }
                            }
                        }
                        else
                        {
                            // Regular processing for non-DoStmt matches
                            var ast = SqlPatternMatcher.ParseSql(sql);
                            var matchingPath = new HashSet<IMessage>(searchResults);
                            var matchingNodes = new List<IMessage>(searchResults);
                            
                            string details = debug ? $"Found {searchResults.Count} matches" : "";
                            
                            matches.Add(new SqlMatch
                            {
                                FileName = fileName,
                                Sql = sql,
                                Ast = ast?.ParseTree?.Stmts?.FirstOrDefault()?.Stmt,
                                LineNumber = sqlStatements[i].LineNumber,
                                MatchDetails = details,
                                MatchingPath = matchingPath,
                                MatchingNodes = matchingNodes,
                                Captures = captures
                            });
                        }
                    }
                }
                catch (PgQueryException ex)
                {
                    if (debug)
                    {
                        Console.Error.WriteLine($"Parse error in {fileName} at line {sqlStatements[i].LineNumber}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    if (debug)
                    {
                        Console.Error.WriteLine($"Error processing SQL in {fileName} at line {sqlStatements[i].LineNumber}: {ex.Message}");
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
                    // Check if we only have the default capture group
                    bool onlyDefaultCaptures = match.Captures.Count == 1 && match.Captures.ContainsKey("default");
                    
                    foreach (var captureGroup in match.Captures)
                    {
                        var captureName = captureGroup.Key;
                        var capturedNodes = captureGroup.Value;
                        
                        foreach (var capturedNode in capturedNodes)
                        {
                            var captureValue = ExtractNodeValue(capturedNode);
                            if (!string.IsNullOrEmpty(captureValue))
                            {
                                if (onlyDefaultCaptures)
                                {
                                    // Don't show [default]: prefix when there's only default captures
                                    Console.WriteLine($"{prefix}{captureValue}");
                                }
                                else
                                {
                                    // Show capture group name when there are multiple named captures
                                    Console.WriteLine($"{prefix}[{captureName}]: {captureValue}");
                                }
                            }
                            else
                            {
                                // If we can't extract a simple value, show the node type
                                if (onlyDefaultCaptures)
                                {
                                    Console.WriteLine($"{prefix}{capturedNode.Descriptor?.Name ?? "Unknown"}");
                                }
                                else
                                {
                                    Console.WriteLine($"{prefix}[{captureName}]: {capturedNode.Descriptor?.Name ?? "Unknown"}");
                                }
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
                TreePrinter.PrintTree((IMessage)match.Ast, useColors, maxDepth: 8, TreePrinter.NodeStatus.Normal, treeMode, match.MatchingPath);
            }
            else if (options.PrintAst)
            {
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                
                Console.WriteLine($"{prefix}[AST]");
                Console.WriteLine(JsonSerializer.Serialize(match.Ast, jsonOptions));
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
                        outputSql = SqlHighlighter.ShowMatchesInContext(
                            match.Sql, 
                            match.MatchingNodes, 
                            options.ContextLines.Value, 
                            highlightOptions);
                        
                        // For context view, don't add prefix to each line since it's already formatted
                        Console.WriteLine($"{prefix}[CONTEXT]");
                        Console.WriteLine(outputSql);
                    }
                    else if (options.Debug || options.Verbose)
                    {
                        outputSql = SqlHighlighter.ShowMatchDetails(match.Sql, match.MatchingNodes, highlightOptions);
                        Console.WriteLine($"{prefix}{outputSql}");
                    }
                    else
                    {
                        outputSql = SqlHighlighter.HighlightMatches(match.Sql, match.MatchingNodes, highlightOptions);
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
                "html" => HighlightStyle.Html,
                "markdown" => HighlightStyle.Markdown,
                "ansi" => HighlightStyle.AnsiColors,
                null => HighlightStyle.AnsiColors,
                _ => HighlightStyle.AnsiColors
            };

            return new HighlightOptions
            {
                Style = highlightStyle,
                ShowLineNumbers = options.ShowLineNumbers,
                ShowMatchInfo = options.Debug || options.Verbose
            };
        }

        /// <summary>
        /// Extract a meaningful string value from a captured AST node
        /// </summary>
        static string? ExtractNodeValue(IMessage node)
        {
            if (node?.Descriptor == null) return null;

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
