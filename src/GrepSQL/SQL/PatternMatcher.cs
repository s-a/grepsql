using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using PatternMatch = GrepSQL.Patterns.Match;

namespace GrepSQL.SQL
{
    /// <summary>
    /// SQL Pattern Matcher that provides a high-level interface for pattern matching in SQL ASTs.
    /// This class wraps the lower-level AST pattern matching functionality.
    /// </summary>
    public static class PatternMatcher
    {
        private static bool _debugEnabled = false;

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
        /// Search for nodes and capture groups.
        /// </summary>
        /// <param name="pattern">Pattern with capture groups</param>
        /// <param name="sql">SQL string to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of captured objects</returns>
        public static List<object> SearchWithCaptures(string pattern, string sql, bool debug = false)
        {
            // For captures, we need to use the PatternMatch.SearchWithCaptures method
            // but we need to iterate through statements like SearchInSql does
            var parseResult = Postgres.ParseSql(sql);
            if (parseResult?.ParseTree?.Stmts == null)
                return new List<object>();

            var allCaptures = new List<object>();
            
            foreach (var stmt in parseResult.ParseTree.Stmts)
            {
                if (stmt?.Stmt != null)
                {
                    var stmtCaptures = PatternMatch.SearchWithCaptures(stmt.Stmt, pattern, debug);
                    allCaptures.AddRange(stmtCaptures);
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
        /// <returns>List of captured objects</returns>
        public static List<object> SearchWithCaptures(IMessage node, string pattern, bool debug = false)
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
        public List<object> Captures { get; set; } = new();
        public IMessage? ParseTree { get; set; }
    }
} 