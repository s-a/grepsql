using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommandLine;
using Google.Protobuf;
using PgQuery.NET;
using PgQuery.NET.Analysis;

namespace GrepSQL
{
    public class Options
    {
        [Option('p', "pattern", Required = true, HelpText = "SQL pattern expression to match against")]
        public string Pattern { get; set; } = "";

        [Option('f', "files", HelpText = "SQL files to search through")]
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
    }

    public class SqlMatch
    {
        public string FileName { get; set; } = "";
        public string Sql { get; set; } = "";
        public object? Ast { get; set; }
        public int LineNumber { get; set; }
        public string MatchDetails { get; set; } = "";
        public HashSet<IMessage>? MatchingPath { get; set; }
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
                var matches = new List<SqlMatch>();

                if (!string.IsNullOrEmpty(options.FromSql))
                {
                    // Process inline SQL
                    ProcessSql("(inline)", options.FromSql, options.Pattern, options.Debug, options.Verbose, matches);
                }
                else if (options.Files.Any())
                {
                    // Process files
                    foreach (var file in options.Files)
                    {
                        if (!File.Exists(file))
                        {
                            Console.Error.WriteLine($"Error: File '{file}' not found.");
                            continue;
                        }

                        try
                        {
                            var content = File.ReadAllText(file);
                            ProcessSql(file, content, options.Pattern, options.Debug, options.Verbose, matches);
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
                    ProcessSql("(stdin)", content, options.Pattern, options.Debug, options.Verbose, matches);
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
                    // Use MatchWithDetails for better debugging information
                    var (success, details) = SqlPatternMatcher.MatchWithDetails(pattern, sql, debug, verbose);
                    
                    // Show debug details even for failed matches when debug is enabled
                    if (debug && !success)
                    {
                        Console.Error.WriteLine($"[DEBUG] Pattern match failed for {fileName} at line {sqlStatements[i].LineNumber}:");
                        Console.Error.WriteLine(details);
                        Console.Error.WriteLine();
                    }
                    
                    if (success)
                    {
                        var ast = SqlPatternMatcher.ParseSql(sql);
                        var matchingPath = SqlPatternMatcher.GetMatchingPath();
                        
                        matches.Add(new SqlMatch
                        {
                            FileName = fileName,
                            Sql = sql,
                            Ast = ast?.ParseTree?.Stmts?.FirstOrDefault()?.Stmt,
                            LineNumber = sqlStatements[i].LineNumber,
                            MatchDetails = debug ? details : "",
                            MatchingPath = matchingPath
                        });
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
            var lines = content.Split('\n');
            var currentStatement = new List<string>();
            var startLine = 1;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                // Skip empty lines and comments at the start
                if (currentStatement.Count == 0 && (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("--")))
                {
                    startLine = i + 2; // Next line (1-based)
                    continue;
                }

                currentStatement.Add(line);

                // Simple statement detection: ends with semicolon (not in quotes/comments)
                if (EndsWithSemicolon(line))
                {
                    var sql = string.Join("\n", currentStatement);
                    statements.Add((sql, startLine));
                    currentStatement.Clear();
                    startLine = i + 2; // Next line (1-based)
                }
            }

            // Add remaining statement if any
            if (currentStatement.Count > 0)
            {
                var sql = string.Join("\n", currentStatement);
                statements.Add((sql, startLine));
            }

            return statements;
        }

        static bool EndsWithSemicolon(string line)
        {
            // Simple check - could be improved to handle quotes and comments
            var trimmed = line.Trim();
            return trimmed.EndsWith(";") && !trimmed.StartsWith("--");
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
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                Console.WriteLine($"{prefix}[AST]");
                Console.WriteLine(JsonSerializer.Serialize(match.Ast, jsonOptions));
            }
            else
            {
                Console.WriteLine($"{prefix}{match.Sql}");
            }
            
            if (!options.CountOnly)
            {
                Console.WriteLine(); // Empty line between matches
            }
        }
    }
}
