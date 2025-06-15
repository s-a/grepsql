using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using PatternMatch = PgQuery.NET.AST.Pattern.Match;

namespace PgQuery.NET.SQL
{
    /// <summary>
    /// SQL Pattern Matcher that provides a high-level interface for pattern matching in SQL ASTs.
    /// This class wraps the lower-level AST pattern matching functionality.
    /// </summary>
    public static class PatternMatcher
    {
        private static bool _debugEnabled = false;
        private static Dictionary<string, List<IMessage>> _globalCaptures = new();

        /// <summary>
        /// Enable or disable debug output.
        /// </summary>
        /// <param name="enable">True to enable debug output</param>
        public static void SetDebug(bool enable)
        {
            _debugEnabled = enable;
            // Debug is now handled per-call in the new pattern matcher
            Postgres.SetDebug(enable);
        }

        /// <summary>
        /// Check if a SQL string matches the given pattern.
        /// </summary>
        /// <param name="pattern">Pattern to match</param>
        /// <param name="sql">SQL string to check</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>True if the SQL matches the pattern</returns>
        public static bool Match(string pattern, string sql, bool debug = false)
        {
            return Postgres.SearchInSql(pattern, sql, debug).Any();
        }

        /// <summary>
        /// Search for all nodes matching the pattern in the SQL.
        /// </summary>
        /// <param name="pattern">Pattern to search for</param>
        /// <param name="sql">SQL string to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public static List<IMessage> Search(string pattern, string sql, bool debug = false)
        {
            return Postgres.SearchInSql(pattern, sql, debug);
        }

        /// <summary>
        /// Search for nodes and capture named groups.
        /// </summary>
        /// <param name="pattern">Pattern with capture groups</param>
        /// <param name="sql">SQL string to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>Dictionary of captured nodes by name</returns>
        public static Dictionary<string, List<IMessage>> SearchWithCaptures(string pattern, string sql, bool debug = false)
        {
            // For captures, we need to use the PatternMatch.SearchWithCaptures method
            // but we need to iterate through statements like SearchInSql does
            var parseResult = Postgres.ParseSql(sql);
            if (parseResult?.ParseTree?.Stmts == null)
                return new Dictionary<string, List<IMessage>>();

            var allCaptures = new Dictionary<string, List<IMessage>>();
            
            foreach (var stmt in parseResult.ParseTree.Stmts)
            {
                if (stmt?.Stmt != null)
                {
                    var stmtCaptures = PatternMatch.SearchWithCaptures(stmt.Stmt, pattern, debug);
                    foreach (var capture in stmtCaptures)
                    {
                        if (!allCaptures.ContainsKey(capture.Key))
                            allCaptures[capture.Key] = new List<IMessage>();
                        allCaptures[capture.Key].AddRange(capture.Value);
                    }
                }
            }
            
            return allCaptures;
        }

        /// <summary>
        /// Search for patterns across multiple SQL strings.
        /// </summary>
        /// <param name="pattern">Pattern to search for</param>
        /// <param name="sqlStrings">SQL strings to search</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public static List<IMessage> SearchInSqlStrings(string pattern, IEnumerable<string> sqlStrings, bool debug = false)
        {
            var results = new List<IMessage>();
            
            foreach (var sql in sqlStrings)
            {
                try
                {
                    var matches = Postgres.SearchInSql(pattern, sql, debug);
                    results.AddRange(matches);
                }
                catch (Exception ex)
                {
                    if (debug)
                    {
                        Console.WriteLine($"Error parsing SQL '{sql}': {ex.Message}");
                    }
                    // Continue with other SQL strings
                }
            }
            
            return results;
        }

        /// <summary>
        /// Get a detailed breakdown of pattern matching results.
        /// </summary>
        /// <param name="pattern">Pattern to analyze</param>
        /// <param name="sql">SQL string to analyze</param>
        /// <returns>Analysis results</returns>
        public static PatternAnalysisResult Analyze(string pattern, string sql)
        {
            var parseResult = Postgres.ParseSql(sql);
            var matches = Postgres.SearchInSql(pattern, sql, true);
            var captures = SearchWithCaptures(pattern, sql, true);
            
            return new PatternAnalysisResult
            {
                Pattern = pattern,
                Sql = sql,
                MatchCount = matches.Count,
                Matches = matches,
                Captures = captures,
                ParseTree = parseResult?.ParseTree
            };
        }

        // ==================== NODE-BASED METHODS (delegate to AstPatternMatcher) ====================

        /// <summary>
        /// Match a pattern against a single AST node.
        /// </summary>
        /// <param name="node">AST node to match against</param>
        /// <param name="pattern">Pattern to match</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>True if pattern matches</returns>
        public static bool Match(IMessage node, string pattern, bool debug = false)
        {
            return PatternMatch.Search(node, pattern, debug).Any();
        }

        /// <summary>
        /// Search for all nodes matching a pattern within a single AST node.
        /// </summary>
        /// <param name="node">Root AST node to search within</param>
        /// <param name="pattern">Pattern to search for</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public static List<IMessage> Search(IMessage node, string pattern, bool debug = false)
        {
            return PatternMatch.Search(node, pattern, debug);
        }

        /// <summary>
        /// Search for nodes matching a pattern and return captured groups.
        /// </summary>
        /// <param name="node">Root AST node to search within</param>
        /// <param name="pattern">Pattern with capture groups</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>Dictionary of captured nodes by name</returns>
        public static Dictionary<string, List<IMessage>> SearchWithCaptures(IMessage node, string pattern, bool debug = false)
        {
            return PatternMatch.SearchWithCaptures(node, pattern, debug);
        }

        /// <summary>
        /// Get expression tree representation for debugging.
        /// </summary>
        /// <param name="pattern">Pattern to parse</param>
        /// <returns>Expression tree as string</returns>
        public static string GetExpressionTree(string pattern)
        {
            return PatternMatch.GetExpressionTree(pattern);
        }

        // ==================== UTILITY METHODS ====================

        /// <summary>
        /// Search for patterns across multiple SQL parse results.
        /// </summary>
        /// <param name="pattern">Pattern to search for</param>
        /// <param name="asts">Parse results to search within</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes from all ASTs</returns>
        public static List<IMessage> SearchInAsts(string pattern, IEnumerable<ParseResult> asts, bool debug = false)
        {
            return Postgres.SearchInAsts(pattern, asts, debug);
        }

        // ==================== UTILITY METHODS ====================

        /// <summary>
        /// Parse SQL into an AST.
        /// </summary>
        /// <param name="sql">SQL to parse</param>
        /// <returns>Parse result or null if parsing failed</returns>
        public static ParseResult? ParseSql(string sql)
        {
            return Postgres.ParseSql(sql);
        }

        /// <summary>
        /// Get parse tree with PL/pgSQL content included.
        /// </summary>
        /// <param name="sql">SQL to parse</param>
        /// <param name="includeDoStmt">Whether to include parsed DoStmt content</param>
        /// <returns>Parse result with PL/pgSQL content</returns>
        public static ParseResult? GetParseTreeWithPlPgSql(string sql, bool includeDoStmt = true)
        {
            if (!includeDoStmt)
            {
                return ParseSql(sql);
            }

            // For now, just return the basic parse result
            // The enhanced DoStmt handling is done automatically in Postgres.SearchInSql
            return ParseSql(sql);
        }

        // ==================== BACKWARD COMPATIBILITY METHODS ====================

        /// <summary>
        /// Match with detailed results (backward compatibility).
        /// </summary>
        /// <param name="pattern">Pattern to match</param>
        /// <param name="sql">SQL to match against</param>
        /// <param name="debug">Enable debug output</param>
        /// <param name="verbose">Enable verbose output</param>
        /// <returns>Match result with details</returns>
        public static (bool Success, string Details) MatchWithDetails(string pattern, string sql, bool debug = false, bool verbose = false)
        {
            try
            {
                var results = Search(pattern, sql, debug);
                var success = results.Count > 0;
                var details = success 
                    ? $"Found {results.Count} matches" 
                    : "No matches found";
                
                if (verbose && success)
                {
                    details += $"\nMatching nodes: {string.Join(", ", results.Take(5).Select(n => n.Descriptor?.Name ?? "Unknown"))}";
                    if (results.Count > 5)
                    {
                        details += $" and {results.Count - 5} more...";
                    }
                }
                
                return (success, details);
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Simple match check (backward compatibility).
        /// </summary>
        /// <param name="pattern">Pattern to match</param>
        /// <param name="sql">SQL to match against</param>
        /// <param name="debug">Enable debug output</param>
        /// <param name="verbose">Enable verbose output</param>
        /// <returns>True if pattern matches</returns>
        public static bool Matches(string pattern, string sql, bool debug = false, bool verbose = false)
        {
            return Match(pattern, sql, debug);
        }

        // ==================== LEGACY CAPTURE SUPPORT ====================
        // These methods provide backward compatibility for the old capture system

        /// <summary>
        /// Get captured nodes (legacy support).
        /// </summary>
        /// <returns>Empty dictionary (captures are now handled differently)</returns>
        public static IReadOnlyDictionary<string, List<IMessage>> GetCaptures()
        {
            return new Dictionary<string, List<IMessage>>();
        }

        /// <summary>
        /// Clear captures (legacy support - no-op).
        /// </summary>
        public static void ClearCaptures()
        {
            // No-op - captures are now handled per-operation
        }

        /// <summary>
        /// Get matching path (legacy support).
        /// </summary>
        /// <returns>Empty set</returns>
        public static HashSet<IMessage> GetMatchingPath()
        {
            return new HashSet<IMessage>();
        }

        // ==================== INTERNAL HELPER METHODS ====================

        /// <summary>
        /// Add capture (legacy support - no-op).
        /// </summary>
        /// <param name="node">Node to capture</param>
        internal static void AddCapture(IMessage node)
        {
            // No-op - captures are now handled differently
        }

        /// <summary>
        /// Add named capture (legacy support - no-op).
        /// </summary>
        /// <param name="name">Capture name</param>
        /// <param name="node">Node to capture</param>
        internal static void AddNamedCapture(string name, IMessage node)
        {
            // No-op - captures are now handled differently
        }

        // ==================== LEGACY CLASSES FOR BACKWARD COMPATIBILITY ====================

        /// <summary>
        /// DoStmt wrapper for backward compatibility.
        /// </summary>
        public class DoStmtWrapper : IMessage
        {
            public IMessage InnerNode { get; }
            public string ExtractedSql { get; }

            public DoStmtWrapper(IMessage innerNode, string extractedSql)
            {
                InnerNode = innerNode ?? throw new ArgumentNullException(nameof(innerNode));
                ExtractedSql = extractedSql ?? string.Empty;
            }

            public Google.Protobuf.Reflection.MessageDescriptor Descriptor => InnerNode.Descriptor;
            public int CalculateSize() => InnerNode.CalculateSize();
            public void MergeFrom(Google.Protobuf.CodedInputStream input) => InnerNode.MergeFrom(input);
            public void WriteTo(Google.Protobuf.CodedOutputStream output) => InnerNode.WriteTo(output);
            public IMessage Clone() => new DoStmtWrapper(InnerNode, ExtractedSql);
            public bool Equals(IMessage other) => InnerNode.Equals(other);
        }

        /// <summary>
        /// Something class for backward compatibility.
        /// </summary>
        public class Something : IMessage
        {
            private static readonly Google.Protobuf.Reflection.MessageDescriptor _descriptor = 
                Google.Protobuf.WellKnownTypes.Any.Descriptor;

            public Google.Protobuf.Reflection.MessageDescriptor Descriptor => _descriptor;
            public int CalculateSize() => 0;
            public void MergeFrom(Google.Protobuf.CodedInputStream input) { }
            public void WriteTo(Google.Protobuf.CodedOutputStream output) { }
            public IMessage Clone() => new Something();
            public bool Equals(IMessage other) => other is Something;
        }
    }

    /// <summary>
    /// Results of pattern analysis.
    /// </summary>
    public class PatternAnalysisResult
    {
        public string Pattern { get; set; } = "";
        public string Sql { get; set; } = "";
        public int MatchCount { get; set; }
        public List<IMessage> Matches { get; set; } = new();
        public Dictionary<string, List<IMessage>> Captures { get; set; } = new();
        public IMessage? ParseTree { get; set; }
    }
} 