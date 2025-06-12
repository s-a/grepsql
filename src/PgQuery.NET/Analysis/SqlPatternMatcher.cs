using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Google.Protobuf;

namespace PgQuery.NET.Analysis
{
    /// <summary>
    /// High-performance SQL pattern matcher with Ruby Fast-inspired syntax.
    /// Includes expression compilation caching and memory optimizations.
    /// 
    /// Usage:
    ///   bool match = SqlPatternMatcher.Match("SelectStmt", sql);
    ///   var nodes = SqlPatternMatcher.Search("_", sql);
    /// </summary>
    public static class SqlPatternMatcher
    {
        // Performance: Expression compilation cache to avoid re-parsing patterns
        private static readonly ConcurrentDictionary<string, IExpression> _expressionCache = new();
        private static readonly object _cacheLock = new object();
        private const int MAX_CACHE_SIZE = 1000; // Prevent memory leaks
        
        // Performance: Thread-local storage for captures to avoid locking
        private static readonly ThreadLocal<List<IMessage>> _captures = new(() => new List<IMessage>());
        
        // Debug flag for controlling debug output
        private static bool _debugEnabled = false;
        
        // Debug helper method
        private static void DebugLog(string message)
        {
            if (_debugEnabled) Console.WriteLine(message);
        }
        
        // Performance: Reusable string arrays for common patterns
        private static readonly string[] _commonWildcards = { "_", "...", "nil" };
        
        // Performance: Pre-compiled regex for faster tokenization
        private static readonly Regex TOKENIZER = new Regex(@"
            [\+\-\/\*\\!]         # operators or negation
            |
            ===?                  # == or ===  
            |
            \d+\.\d*              # decimals and floats
            |
            ""[^""]+""            # strings
            |
            _                     # something not nil: match
            |
            \.{3}                 # a node with children: ...
            |
            \[|\]                 # square brackets for All
            |
            \^                    # node has children with
            |
            \?                    # maybe expression
            |
            [\d\w_]+[=\!\?]?     # method names or numbers
            |
            \(|\)                 # parens for tuples
            |
            \{|\}                 # curly brackets for Any
            |
            \$                    # capture
            |
            \#\w[\d\w_]+[\\!\?]?  # custom method call
            |
            \.\w[\d\w_]+\?        # instance method call
            |
            \\\d                  # find using captured expression
            |
            %\d                   # bind extra arguments to the expression
        ", RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

        // Performance: Literal cache for common patterns
        private static readonly Dictionary<string, IExpression> LITERALS = new Dictionary<string, IExpression>
        {
            ["_"] = new Find("_"),      // Match any node
            ["nil"] = new Find("nil"),  // Match null/empty nodes
            ["..."] = new Find("...")   // Match nodes with children
        };

        /// <summary>
        /// Match a pattern against SQL, with performance optimizations.
        /// Uses expression compilation caching for repeated patterns.
        /// </summary>
        /// <param name="pattern">Pattern to match (Ruby Fast syntax)</param>
        /// <param name="sql">SQL string to parse and match against</param>
        /// <returns>True if pattern matches</returns>
        public static bool Match(string pattern, string sql)
        {
            try
            {
                // Simply use the Search method and check if any results are found
                var results = Search(pattern, sql);
                return results.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlPatternMatcher] Match failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Search for all nodes matching a pattern, with performance optimizations.
        /// </summary>
        /// <param name="pattern">Pattern to search for</param>
        /// <param name="sql">SQL string to parse and search</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public static List<IMessage> Search(string pattern, string sql, bool debug = false)
        {
            try
            {
                _debugEnabled = debug;
                
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Searching for pattern: {pattern}");
                }
                
                ClearCaptures();
                
                var expression = GetCachedExpression(pattern);
                var ast = PgQuery.Parse(sql);
                var results = new List<IMessage>();
                
                // Search across all statements in the parse tree
                SearchInAsts(expression, new[] { ast }, results);
                
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Found {results.Count} matches");
                }
                
                return results;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Search failed: {ex.Message}";
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] {errorMessage}");
                }
                Console.WriteLine($"[SqlPatternMatcher] {errorMessage}");
                return new List<IMessage>();
            }
        }

        /// <summary>
        /// Search for all nodes matching a pattern across multiple ASTs.
        /// </summary>
        /// <param name="pattern">Pattern to search for</param>
        /// <param name="asts">List of ASTs to search</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public static List<IMessage> SearchInAsts(string pattern, IEnumerable<ParseResult> asts, bool debug = false)
        {
            try
            {
                _debugEnabled = debug;
                
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Searching for pattern: {pattern} across multiple ASTs");
                }
                
                ClearCaptures();
                
                var expression = GetCachedExpression(pattern);
                var results = new List<IMessage>();
                
                SearchInAsts(expression, asts, results);
                
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Found {results.Count} matches across all ASTs");
                }
                
                return results;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Search across ASTs failed: {ex.Message}";
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] {errorMessage}");
                }
                Console.WriteLine($"[SqlPatternMatcher] {errorMessage}");
                return new List<IMessage>();
            }
        }

        /// <summary>
        /// Core search method that works across multiple ASTs.
        /// </summary>
        private static void SearchInAsts(IExpression expression, IEnumerable<ParseResult> asts, List<IMessage> results)
        {
            foreach (var ast in asts)
            {
                if (ast?.ParseTree?.Stmts == null) continue;

                foreach (var stmt in ast.ParseTree.Stmts)
                {
                    if (stmt.Stmt == null) continue;

                    // For simple wildcard patterns like "_", only check the root node
                    if (expression is Find find && find.IsWildcard())
                    {
                        if (expression.Match(stmt.Stmt))
                        {
                            results.Add(stmt.Stmt);
                        }
                    }
                    else if (expression is Find findNil && findNil.IsNil())
                    {
                        // nil should not match anything in a valid parse tree
                        continue;
                    }
                    else if (expression is Find findEllipsis && findEllipsis.IsEllipsis())
                    {
                        // For ellipsis patterns, check if the root node has children
                        if (expression.Match(stmt.Stmt))
                        {
                            results.Add(stmt.Stmt);
                        }
                    }
                    else
                    {
                        // For all other patterns, search recursively
                        SearchRecursive(expression, stmt.Stmt, results);
                    }
                }
            }
        }

        /// <summary>
        /// Recursive search with performance optimizations and enhanced DoStmt handling.
        /// </summary>
        private static void SearchRecursive(IExpression expression, IMessage node, List<IMessage> results)
        {
            if (node == null) return;

            // Check if current node matches
            if (expression.Match(node))
            {
                results.Add(node);
            }

            // Enhanced DoStmt handling - detect and parse PL/pgSQL content
            if (node.Descriptor?.Name == "DoStmt")
            {
                DebugLog($"[DoStmt] Found DoStmt node, processing PL/pgSQL content");
                var plpgsqlContent = ExtractSqlFromDoStmt(node);
                
                if (!string.IsNullOrEmpty(plpgsqlContent))
                {
                    try
                    {
                        DebugLog($"[DoStmt] Extracted PL/pgSQL content: {plpgsqlContent}");
                        
                        // First try to extract SQL statements manually from the PL/pgSQL block
                        var extractedSqlStatements = ExtractSqlStatementsFromPlPgSqlBlock(plpgsqlContent);
                        
                        if (extractedSqlStatements.Count > 0)
                        {
                            DebugLog($"[DoStmt] Found {extractedSqlStatements.Count} SQL statements via manual extraction");
                            var embeddedAsts = new List<ParseResult>();
                            
                            foreach (var sqlStatement in extractedSqlStatements)
                            {
                                if (!string.IsNullOrEmpty(sqlStatement))
                                {
                                    try
                                    {
                                        DebugLog($"[DoStmt] Parsing SQL: {sqlStatement.Substring(0, Math.Min(50, sqlStatement.Length))}...");
                                        var embeddedAst = ParseSql(sqlStatement);
                                        if (embeddedAst != null)
                                        {
                                            embeddedAsts.Add(embeddedAst);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLog($"[DoStmt] Failed to parse extracted SQL: {ex.Message}");
                                    }
                                }
                            }
                            
                            // Search within embedded SQL ASTs
                            if (embeddedAsts.Count > 0)
                            {
                                var embeddedResults = new List<IMessage>();
                                SearchInAsts(expression, embeddedAsts, embeddedResults);
                                
                                // Wrap results to indicate they came from DoStmt
                                foreach (var result in embeddedResults)
                                {
                                    results.Add(new DoStmtWrapper(result, plpgsqlContent));
                                }
                            }
                        }
                        
                        // Also try structured PL/pgSQL parsing if available
                        try
                        {
                            var plpgsqlJson = PgQuery.ParsePlpgsql(plpgsqlContent);
                            DebugLog($"[DoStmt] PL/pgSQL structured parsing succeeded");
                            
                            // Create a PL/pgSQL AST wrapper that can be searched
                            var plpgsqlWrapper = new PlPgSqlWrapper(plpgsqlJson, plpgsqlContent);
                            
                            // Search within the PL/pgSQL content
                            SearchInPlPgSql(expression, plpgsqlWrapper, results);
                            
                            // Also extract and parse any additional embedded SQL statements from JSON
                            var jsonSqlStatements = ExtractSqlFromPlpgsqlJson(plpgsqlJson);
                            
                            if (jsonSqlStatements.Count > 0)
                            {
                                DebugLog($"[DoStmt] Found {jsonSqlStatements.Count} additional SQL statements from JSON");
                                var additionalAsts = new List<ParseResult>();
                                
                                foreach (var sqlStatement in jsonSqlStatements)
                                {
                                    if (!string.IsNullOrEmpty(sqlStatement))
                                    {
                                        try
                                        {
                                            var embeddedAst = ParseSql(sqlStatement);
                                            if (embeddedAst != null)
                                            {
                                                additionalAsts.Add(embeddedAst);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            DebugLog($"[DoStmt] Failed to parse JSON-extracted SQL: {ex.Message}");
                                        }
                                    }
                                }
                                
                                // Search within additional embedded SQL ASTs
                                if (additionalAsts.Count > 0)
                                {
                                    var additionalResults = new List<IMessage>();
                                    SearchInAsts(expression, additionalAsts, additionalResults);
                                    
                                    // Wrap results to indicate they came from DoStmt
                                    foreach (var result in additionalResults)
                                    {
                                        results.Add(new DoStmtWrapper(result, plpgsqlContent));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"[DoStmt] PL/pgSQL structured parsing failed (using manual extraction): {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"[DoStmt] PL/pgSQL processing failed: {ex.Message}");
                    }
                }
            }

            // Performance: Use descriptor to efficiently iterate children
            var descriptor = node.Descriptor;
            if (descriptor != null)
            {
                foreach (var field in descriptor.Fields.InFieldNumberOrder())
                {
                    if (field.IsRepeated)
                    {
                        var list = (System.Collections.IList)field.Accessor.GetValue(node);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                if (item is IMessage child)
                                {
                                    SearchRecursive(expression, child, results);
                                }
                            }
                        }
                    }
                    else
                    {
                        var value = field.Accessor.GetValue(node);
                        if (value is IMessage child)
                        {
                            SearchRecursive(expression, child, results);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Extract SQL statements from PL/pgSQL block using pattern matching.
        /// </summary>
        private static List<string> ExtractSqlStatementsFromPlPgSqlBlock(string plpgsqlContent)
        {
            var sqlStatements = new List<string>();
            
            if (string.IsNullOrEmpty(plpgsqlContent))
                return sqlStatements;
            
            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Processing content");
            
            // Remove the outer BEGIN/END block and clean up
            var content = plpgsqlContent.Trim();
            
            // Remove BEGIN and END keywords if present
            if (content.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Substring(5).Trim();
            }
            if (content.EndsWith("END", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Substring(0, content.Length - 3).Trim();
            }
            
            // Split by semicolons and extract SQL statements
            var lines = content.Split('\n');
            var currentStatement = new System.Text.StringBuilder();
            bool inStatement = false;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("--"))
                    continue;
                
                // Skip PL/pgSQL specific statements
                if (trimmedLine.StartsWith("RAISE ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("EXCEPTION", StringComparison.OrdinalIgnoreCase))
                {
                    // If we're in a statement, end it before skipping
                    if (inStatement && currentStatement.Length > 0)
                    {
                        var stmt = currentStatement.ToString().Trim();
                        if (IsParsableSqlStatement(stmt))
                        {
                            sqlStatements.Add(stmt);
                            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Found SQL: {stmt.Substring(0, Math.Min(50, stmt.Length))}...");
                        }
                        currentStatement.Clear();
                        inStatement = false;
                    }
                    continue;
                }
                
                // Check if this line starts a SQL statement
                if (IsStartOfSqlStatement(trimmedLine))
                {
                    // If we were already building a statement, save it first
                    if (inStatement && currentStatement.Length > 0)
                    {
                        var stmt = currentStatement.ToString().Trim();
                        if (IsParsableSqlStatement(stmt))
                        {
                            sqlStatements.Add(stmt);
                            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Found SQL: {stmt.Substring(0, Math.Min(50, stmt.Length))}...");
                        }
                        currentStatement.Clear();
                    }
                    
                    currentStatement.AppendLine(line);
                    inStatement = true;
                }
                else if (inStatement)
                {
                    currentStatement.AppendLine(line);
                }
                
                // Check if this line ends a statement
                if (inStatement && trimmedLine.EndsWith(";"))
                {
                    var stmt = currentStatement.ToString().Trim();
                    if (IsParsableSqlStatement(stmt))
                    {
                        sqlStatements.Add(stmt);
                        DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Found SQL: {stmt.Substring(0, Math.Min(50, stmt.Length))}...");
                    }
                    currentStatement.Clear();
                    inStatement = false;
                }
            }
            
            // Handle any remaining statement
            if (inStatement && currentStatement.Length > 0)
            {
                var stmt = currentStatement.ToString().Trim();
                if (IsParsableSqlStatement(stmt))
                {
                    sqlStatements.Add(stmt);
                    DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Found final SQL: {stmt.Substring(0, Math.Min(50, stmt.Length))}...");
                }
            }
            
            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Extracted {sqlStatements.Count} SQL statements");
            return sqlStatements;
        }

        /// <summary>
        /// Check if a line starts a SQL statement.
        /// </summary>
        private static bool IsStartOfSqlStatement(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            
            var upperLine = line.ToUpperInvariant();
            return upperLine.StartsWith("SELECT ") ||
                   upperLine.StartsWith("INSERT ") ||
                   upperLine.StartsWith("UPDATE ") ||
                   upperLine.StartsWith("DELETE ") ||
                   upperLine.StartsWith("CREATE ") ||
                   upperLine.StartsWith("DROP ") ||
                   upperLine.StartsWith("ALTER ") ||
                   upperLine.StartsWith("WITH ") ||
                   upperLine.StartsWith("TRUNCATE ") ||
                   upperLine.StartsWith("GRANT ") ||
                   upperLine.StartsWith("REVOKE ");
        }

        /// <summary>
        /// Check if a statement is a parsable SQL statement.
        /// </summary>
        private static bool IsParsableSqlStatement(string statement)
        {
            if (string.IsNullOrWhiteSpace(statement)) return false;
            
            var upperStatement = statement.Trim().ToUpperInvariant();
            
            // Must start with a SQL keyword
            if (!IsStartOfSqlStatement(upperStatement)) return false;
            
            // Must not be a PL/pgSQL specific statement
            if (upperStatement.Contains("RAISE NOTICE") ||
                upperStatement.Contains("DECLARE ") ||
                upperStatement.Contains("EXCEPTION"))
                return false;
            
            // Should have reasonable length
            if (statement.Length < 10) return false;
            
            return true;
        }

        /// <summary>
        /// Wrapper for PL/pgSQL JSON nodes to make them searchable.
        /// </summary>
        public class PlPgSqlJsonNode : IMessage
        {
            public System.Text.Json.JsonElement JsonElement { get; }
            private readonly Google.Protobuf.Reflection.MessageDescriptor _descriptor;
            
            public PlPgSqlJsonNode(System.Text.Json.JsonElement jsonElement)
            {
                JsonElement = jsonElement;
                _descriptor = CreatePlPgSqlJsonDescriptor();
            }
            
            public Google.Protobuf.Reflection.MessageDescriptor Descriptor => _descriptor;
            public int CalculateSize() => JsonElement.ToString().Length;
            public void MergeFrom(Google.Protobuf.CodedInputStream input) { }
            public void WriteTo(Google.Protobuf.CodedOutputStream output) { }
            public IMessage Clone() => new PlPgSqlJsonNode(JsonElement);
            public bool Equals(IMessage other) => other is PlPgSqlJsonNode node && node.JsonElement.Equals(JsonElement);
            
            private static Google.Protobuf.Reflection.MessageDescriptor CreatePlPgSqlJsonDescriptor()
            {
                // Create a descriptor that represents a PL/pgSQL JSON node
                return Google.Protobuf.WellKnownTypes.Struct.Descriptor;
            }
        }

        /// <summary>
        /// Wrapper for PL/pgSQL content to make it searchable.
        /// </summary>
        public class PlPgSqlWrapper : IMessage
        {
            public string PlPgSqlJson { get; }
            public string PlPgSqlContent { get; }
            private readonly Google.Protobuf.Reflection.MessageDescriptor _descriptor;
            
            public PlPgSqlWrapper(string plpgsqlJson, string plpgsqlContent)
            {
                PlPgSqlJson = plpgsqlJson;
                PlPgSqlContent = plpgsqlContent;
                
                // Create a fake descriptor for this wrapper
                _descriptor = CreatePlPgSqlDescriptor();
            }
            
            public Google.Protobuf.Reflection.MessageDescriptor Descriptor => _descriptor;
            public int CalculateSize() => PlPgSqlContent.Length;
            public void MergeFrom(Google.Protobuf.CodedInputStream input) { }
            public void WriteTo(Google.Protobuf.CodedOutputStream output) { }
            public IMessage Clone() => new PlPgSqlWrapper(PlPgSqlJson, PlPgSqlContent);
            public bool Equals(IMessage other) => other is PlPgSqlWrapper wrapper && wrapper.PlPgSqlContent == PlPgSqlContent;
            
            private static Google.Protobuf.Reflection.MessageDescriptor CreatePlPgSqlDescriptor()
            {
                // Create a descriptor that represents a PL/pgSQL block
                return Google.Protobuf.WellKnownTypes.Any.Descriptor;
            }

            /// <summary>
            /// Get the node type name for pattern matching.
            /// </summary>
            public string GetNodeTypeName()
            {
                return "PlPgSqlBlock";
            }
        }

        /// <summary>
        /// Wrapper class to track nodes that came from inside DoStmt parsing
        /// </summary>
        public class DoStmtWrapper : IMessage
        {
            public IMessage InnerNode { get; }
            public string ExtractedSql { get; }
            
            public DoStmtWrapper(IMessage innerNode, string extractedSql)
            {
                InnerNode = innerNode;
                ExtractedSql = extractedSql;
            }
            
            public Google.Protobuf.Reflection.MessageDescriptor Descriptor => InnerNode.Descriptor;
            public int CalculateSize() => InnerNode.CalculateSize();
            public void MergeFrom(Google.Protobuf.CodedInputStream input) => InnerNode.MergeFrom(input);
            public void WriteTo(Google.Protobuf.CodedOutputStream output) => InnerNode.WriteTo(output);
            public IMessage Clone() => new DoStmtWrapper(InnerNode, ExtractedSql);
            public bool Equals(IMessage other) => InnerNode.Equals(other);
        }

        /// <summary>
        /// Match a pattern against SQL with detailed results, compatible with SqlPatternMatcher API.
        /// </summary>
        /// <param name="pattern">Pattern to match</param>
        /// <param name="sql">SQL string to match against</param>
        /// <param name="debug">Enable debug output</param>
        /// <param name="verbose">Enable verbose debug output</param>
        /// <returns>Tuple of success and details</returns>
        public static (bool Success, string Details) MatchWithDetails(string pattern, string sql, bool debug = false, bool verbose = false)
        {
            try
            {
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Matching pattern: {pattern}");
                }
                
                var success = Match(pattern, sql);
                var details = success ? "Pattern matched successfully" : "Pattern did not match";
                
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Result: {success} - {details}");
                }
                
                return (success, details);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Match failed: {ex.Message}";
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Error: {errorMessage}");
                }
                return (false, errorMessage);
            }
        }

        /// <summary>
        /// Multiple pattern matching method, compatible with SqlPatternMatcher API.
        /// </summary>
        /// <param name="pattern">Pattern to match</param>
        /// <param name="sql">SQL string to match against</param>
        /// <param name="debug">Enable debug output</param>
        /// <param name="verbose">Enable verbose debug output</param>
        /// <returns>True if pattern matches</returns>
        public static bool Matches(string pattern, string sql, bool debug = false, bool verbose = false)
        {
            return Match(pattern, sql);
        }

        /// <summary>
        /// Parse SQL and return parse result, compatible with SqlPatternMatcher API.
        /// </summary>
        /// <param name="sql">SQL string to parse</param>
        /// <returns>Parse result or null if parsing fails</returns>
        public static ParseResult? ParseSql(string sql)
        {
            try
            {
                return PgQuery.Parse(sql);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Get matching path (for backward compatibility - returns empty set).
        /// </summary>
        /// <returns>Empty hash set</returns>
        public static HashSet<IMessage> GetMatchingPath()
        {
            return new HashSet<IMessage>();
        }

        /// <summary>
        /// Set debug mode (for backward compatibility - no-op in current implementation).
        /// </summary>
        /// <param name="enable">Enable debug mode</param>
        public static void SetDebug(bool enable)
        {
            // No-op for backward compatibility
            // Debug is controlled per-call in the new implementation
        }

        /// <summary>
        /// Get cached expression or compile and cache new one.
        /// Thread-safe with bounded cache size for memory management.
        /// </summary>
        private static IExpression GetCachedExpression(string pattern)
        {
            // Performance: Check cache first (lock-free read in most cases)
            if (_expressionCache.TryGetValue(pattern, out var cached))
            {
                return cached;
            }

            // Performance: Double-checked locking pattern for compilation
            lock (_cacheLock)
            {
                if (_expressionCache.TryGetValue(pattern, out cached))
                {
                    return cached;
                }

                // Performance: Bounded cache to prevent memory leaks
                if (_expressionCache.Count >= MAX_CACHE_SIZE)
                {
                    // Remove oldest entries (simple FIFO eviction)
                    var toRemove = _expressionCache.Keys.Take(_expressionCache.Count / 4).ToList();
                    foreach (var key in toRemove)
                    {
                        _expressionCache.TryRemove(key, out _);
                    }
                }

                // Compile and cache the expression
                var expression = CompileExpression(pattern);
                _expressionCache[pattern] = expression;
                return expression;
            }
        }

        /// <summary>
        /// Performance optimized expression compilation with smart caching.
        /// </summary>
        private static IExpression CompileExpression(string pattern)
        {
            try
            {
                // Check for attribute patterns first before general parsing
                if (IsAttributePattern(pattern))
                {
                    DebugLog($"[CompileExpression] Detected attribute pattern: {pattern}");
                    return new Find(pattern);
                }
                
                // Check for simple patterns that can use cached literals
                if (IsSimplePattern(pattern))
                {
                    if (LITERALS.TryGetValue(pattern, out var literal))
                    {
                        return literal;
                    }
                }

                // Use the expression parser for complex patterns
                var parser = new ExpressionParser(pattern);
                return parser.Parse();
            }
            catch (Exception ex)
            {
                DebugLog($"[CompileExpression] Failed to compile pattern '{pattern}': {ex.Message}");
                // Fallback to a simple Find expression
                return new Find(pattern);
            }
        }
        
        private static bool IsAttributePattern(string pattern)
        {
            // Check if pattern looks like (attributeName value_expression)
            if (!pattern.StartsWith("(") || !pattern.EndsWith(")")) return false;
            
            var inner = pattern.Substring(1, pattern.Length - 2).Trim();
            var parts = inner.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length != 2) return false;
            
            var attributeName = parts[0].Trim();
            var valueExpression = parts[1].Trim();
            
            // Check if the first part looks like an attribute name (no special characters)
            if (string.IsNullOrEmpty(attributeName) || 
                attributeName.Contains('(') || attributeName.Contains(')') ||
                attributeName.Contains('{') || attributeName.Contains('}') ||
                attributeName.Contains('[') || attributeName.Contains(']'))
            {
                return false;
            }
            
            // Comprehensive list of PostgreSQL AST attribute names
            var commonAttributes = new[] { 
                // Table and relation names
                "relname", "schemaname", "aliasname", "tablename", "catalogname",
                
                // Column and field names  
                "colname", "fieldname", "attname", "resname",
                
                // Function and procedure names
                "funcname", "proname", "oprname", "aggname",
                
                // Type names
                "typename", "typname", "typnamespace",
                
                // Index and constraint names
                "indexname", "idxname", "constraintname", "conname",
                
                // General names and identifiers
                "name", "defname", "label", "alias", "objname",
                
                // String values
                "str", "sval", "val", "value", "strval",
                
                // Numeric values
                "ival", "fval", "dval", "location", "typemod",
                
                // Boolean values
                "boolval", "isnull", "islocal", "isnotnull", "unique", "primary",
                "deferrable", "initdeferred", "replace", "ifnotexists", "missingok",
                "concurrent", "temporary", "unlogged", "setof", "pcttype",
                
                // Access methods and storage
                "accessmethod", "tablespacename", "indexspace", "storage",
                
                // Constraint types and actions
                "contype", "fkmatchtype", "fkupdaction", "fkdelaction",
                
                // Expression and operator types
                "kind", "opno", "opfuncid", "opresulttype", "opcollid",
                
                // Language and format specifiers
                "language", "funcformat", "defaction",
                
                // Ordering and sorting
                "ordering", "nullsfirst", "nullslast",
                
                // Inheritance and OID references
                "inhcount", "typeoid", "colloid", "oldpktableoid",
                
                // Subquery and CTE names
                "ctename", "subquery", "withname",
                
                // Window function attributes
                "winname", "framestart", "frameend",
                
                // Trigger attributes
                "tgname", "tgfoid", "tgtype", "tgenabled",
                
                // Role and permission attributes
                "rolname", "grantor", "grantee", "privilege",
                
                // Database and schema attributes
                "datname", "nspname", "encoding", "collate", "ctype",
                
                // Sequence attributes
                "seqname", "increment", "minvalue", "maxvalue", "start", "cache",
                
                // View attributes
                "viewname", "viewquery", "materialized",
                
                // Extension and foreign data wrapper attributes
                "extname", "fdwname", "srvname", "usename",
                
                // Partition attributes
                "partitionkey", "partitionbound", "partitionstrategy",
                
                // Publication and subscription attributes
                "pubname", "subname", "publication", "subscription"
            };
            
            if (commonAttributes.Contains(attributeName.ToLowerInvariant()))
            {
                DebugLog($"[IsAttributePattern] Recognized attribute pattern: {attributeName} = {valueExpression}");
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Performance optimization: detect simple patterns that don't need full parsing.
        /// </summary>
        private static bool IsSimplePattern(string pattern)
        {
            return !string.IsNullOrEmpty(pattern) && 
                   !pattern.Contains('(') && !pattern.Contains('[') && !pattern.Contains('{') &&
                   !pattern.Contains('$') && !pattern.Contains('!') && !pattern.Contains('?') &&
                   !pattern.Contains('^') && !pattern.Contains('\\') && !pattern.Contains('#');
        }

        /// <summary>
        /// Extract SQL content from a DoStmt node's dollar-quoted string.
        /// </summary>
        private static string? ExtractSqlFromDoStmt(IMessage doStmtNode)
        {
            try
            {
                DebugLog($"[ExtractSqlFromDoStmt] Starting extraction from DoStmt");
                // Use reflection to access the Args field and look for the "as" argument
                var argsField = FindField(doStmtNode.Descriptor, "args");
                if (argsField != null)
                {
                    DebugLog($"[ExtractSqlFromDoStmt] Found args field");
                    var args = (System.Collections.IList?)argsField.Accessor.GetValue(doStmtNode);
                    if (args != null)
                    {
                        DebugLog($"[ExtractSqlFromDoStmt] Found {args.Count} args");
                        foreach (var arg in args)
                        {
                            if (arg is IMessage argMessage)
                            {
                                DebugLog($"[ExtractSqlFromDoStmt] Processing arg: {argMessage.Descriptor?.Name}");
                                var defElemField = FindField(argMessage.Descriptor, "def_elem");
                                if (defElemField != null)
                                {
                                    DebugLog($"[ExtractSqlFromDoStmt] Found def_elem field");
                                    var defElem = defElemField.Accessor.GetValue(argMessage) as IMessage;
                                    if (defElem != null && IsDefElemWithName(defElem, "as"))
                                    {
                                        DebugLog($"[ExtractSqlFromDoStmt] Found 'as' DefElem");
                                        var sval = GetStringValueFromDefElem(defElem);
                                        if (!string.IsNullOrEmpty(sval))
                                        {
                                            DebugLog($"[ExtractSqlFromDoStmt] Extracted sval: {sval}");
                                            // Return the raw PL/pgSQL content for proper parsing
                                            return sval;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        DebugLog($"[ExtractSqlFromDoStmt] args is null");
                    }
                }
                else
                {
                    DebugLog($"[ExtractSqlFromDoStmt] args field not found");
                }
            }
            catch (Exception)
            {
                // If extraction fails, return null
            }
            
            return null;
        }

        /// <summary>
        /// Find a field by name in a message descriptor.
        /// </summary>
        private static Google.Protobuf.Reflection.FieldDescriptor? FindField(Google.Protobuf.Reflection.MessageDescriptor? descriptor, string fieldName)
        {
            if (descriptor?.Fields == null) return null;
            
            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                if (field.Name == fieldName)
                {
                    return field;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Check if a DefElem has the specified name.
        /// </summary>
        private static bool IsDefElemWithName(IMessage defElem, string expectedName)
        {
            try
            {
                var defnameField = FindField(defElem.Descriptor, "defname");
                if (defnameField != null)
                {
                    var defname = defnameField.Accessor.GetValue(defElem) as string;
                    return defname == expectedName;
                }
            }
            catch (Exception)
            {
                // If access fails, return false
            }
            
            return false;
        }

        /// <summary>
        /// Get the string value from a DefElem's arg field.
        /// </summary>
        private static string? GetStringValueFromDefElem(IMessage defElem)
        {
            try
            {
                var argField = FindField(defElem.Descriptor, "arg");
                if (argField != null)
                {
                    var arg = argField.Accessor.GetValue(defElem) as IMessage;
                    if (arg != null)
                    {
                        var stringField = FindField(arg.Descriptor, "string");
                        if (stringField != null)
                        {
                            var stringValue = stringField.Accessor.GetValue(arg) as IMessage;
                            if (stringValue != null)
                            {
                                var svalField = FindField(stringValue.Descriptor, "sval");
                                if (svalField != null)
                                {
                                    return svalField.Accessor.GetValue(stringValue) as string;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // If access fails, return null
            }
            
            return null;
        }

        /// <summary>
        /// Extract SQL statements from PL/pgSQL JSON result using structured parsing.
        /// </summary>
        private static List<string> ExtractSqlFromPlpgsqlJson(string plpgsqlJson)
        {
            var sqlStatements = new List<string>();
            
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(plpgsqlJson);
                
                // Navigate the PL/pgSQL AST structure to find SQL statements
                ExtractSqlFromJsonElement(doc.RootElement, sqlStatements);
            }
            catch (Exception ex)
            {
                DebugLog($"[ExtractSqlFromPlpgsqlJson] Failed to parse JSON: {ex.Message}");
            }
            
            return sqlStatements;
        }

        /// <summary>
        /// Recursively extract SQL statements from PL/pgSQL JSON elements.
        /// </summary>
        private static void ExtractSqlFromJsonElement(System.Text.Json.JsonElement element, List<string> sqlStatements)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        // Look for SQL statement nodes in the PL/pgSQL AST
                        if (IsSqlStatementProperty(property.Name))
                        {
                            var sqlText = ExtractSqlTextFromProperty(property.Value);
                            if (!string.IsNullOrEmpty(sqlText))
                            {
                                sqlStatements.Add(sqlText);
                            }
                        }
                        
                        // Recursively search nested objects
                        ExtractSqlFromJsonElement(property.Value, sqlStatements);
                    }
                    break;
                    
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        ExtractSqlFromJsonElement(item, sqlStatements);
                    }
                    break;
            }
        }

        /// <summary>
        /// Check if a property name indicates it contains SQL statement information.
        /// </summary>
        private static bool IsSqlStatementProperty(string propertyName)
        {
            // Based on libpgquery PL/pgSQL AST structure, look for properties that contain SQL
            return propertyName.Contains("stmt", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.Contains("query", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.Contains("sql", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.Equals("PLpgSQL_stmt_execsql", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.Equals("PLpgSQL_stmt_dynexecute", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extract SQL text from a JSON property value.
        /// </summary>
        private static string? ExtractSqlTextFromProperty(System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return element.GetString();
            }
            
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                // Look for common SQL text fields in PL/pgSQL AST nodes
                if (element.TryGetProperty("sqlstmt", out var sqlstmt) && sqlstmt.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return sqlstmt.GetString();
                }
                
                if (element.TryGetProperty("query", out var query) && query.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return query.GetString();
                }
                
                if (element.TryGetProperty("sql", out var sql) && sql.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return sql.GetString();
                }
            }
            
            return null;
        }

        /// <summary>
        /// Get captured nodes from pattern matching, compatible with SqlPatternMatcher API.
        /// </summary>
        /// <returns>Dictionary of captured nodes by name</returns>
        public static IReadOnlyDictionary<string, List<IMessage>> GetCaptures()
        {
            // For backward compatibility, return empty dictionary since new implementation
            // doesn't support named captures yet
            return new Dictionary<string, List<IMessage>>();
        }

        /// <summary>
        /// Clear captures for current thread.
        /// </summary>
        public static void ClearCaptures()
        {
            _captures.Value?.Clear();
        }

        /// <summary>
        /// Add node to captures for current thread.
        /// </summary>
        internal static void AddCapture(IMessage node)
        {
            _captures.Value?.Add(node);
        }

        /// <summary>
        /// Clear expression cache (for testing or memory management).
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _expressionCache.Clear();
            }
        }

        /// <summary>
        /// Get cache statistics for monitoring.
        /// </summary>
        public static (int count, int maxSize) GetCacheStats()
        {
            return (_expressionCache.Count, MAX_CACHE_SIZE);
        }

        // Shared case conversion utilities
        private static bool HandleCaseConversions(string? value, string? target)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(target)) return false;
            
            // Handle boolean values: true/false vs True/False
            if ((value == "True" && target == "true") || 
                (value == "true" && target == "True") ||
                (value == "False" && target == "false") || 
                (value == "false" && target == "False"))
            {
                return true;
            }
            
            // Handle camelCase to underscore conversion for node types
            // e.g., SelectStmt vs select_stmt, A_Const vs a_const
            if (ConvertCamelToUnderscore(value) == target?.ToLowerInvariant() ||
                ConvertCamelToUnderscore(target) == value?.ToLowerInvariant())
            {
                return true;
            }
            
            // Handle underscore to camelCase conversion
            if (ConvertUnderscoreToCamel(value) == target ||
                ConvertUnderscoreToCamel(target) == value)
            {
                return true;
            }
            
            return false;
        }
        
        // Convert camelCase/PascalCase to underscore_case
        private static string ConvertCamelToUnderscore(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? "";
            
            var result = new System.Text.StringBuilder();
            
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                
                // Add underscore before uppercase letters (except first character)
                if (i > 0 && char.IsUpper(c))
                {
                    result.Append('_');
                }
                
                result.Append(char.ToLowerInvariant(c));
            }
            
            return result.ToString();
        }
        
        // Convert underscore_case to camelCase
        private static string ConvertUnderscoreToCamel(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? "";
            
            var result = new System.Text.StringBuilder();
            bool capitalizeNext = false;
            
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                
                if (c == '_')
                {
                    capitalizeNext = true;
                }
                else
                {
                    if (capitalizeNext)
                    {
                        result.Append(char.ToUpperInvariant(c));
                        capitalizeNext = false;
                    }
                    else
                    {
                        result.Append(c);
                    }
                }
            }
            
            return result.ToString();
        }

        // Core Expression Interface
        private interface IExpression
        {
            bool Match(IMessage node);
        }

        // Find Expression - matches node types, field values, and primitives
        private class Find : IExpression
        {
            private readonly object _target;
            protected readonly string _targetString;
            private readonly bool _isWildcard;
            private readonly bool _isNil;
            private readonly bool _isAttributePattern;
            private readonly string _attributeName;
            private readonly object _attributeValue;

            public Find(object target)
            {
                _target = target;
                _targetString = target?.ToString() ?? "";
                _isWildcard = _targetString == "_";
                _isNil = _targetString == "nil";
                
                // Check if this is an s-expression attribute pattern: (relname "users")
                _isAttributePattern = ParseAttributePattern(_targetString, out _attributeName, out _attributeValue);
            }

            public bool IsEllipsis() => _targetString == "...";
            public bool IsWildcard() => _isWildcard;
            public bool IsNil() => _isNil;

            public bool MatchesDirectValue(object value)
            {
                if (_isWildcard) return true;
                if (_isNil) return value == null;
                if (_isAttributePattern) return false; // Attribute patterns don't match direct values
                return MatchesValue(value, _target);
            }

            // Core matching logic - this is the heart of all pattern matching
            protected virtual bool MatchCore(IMessage node)
            {
                if (node == null) return _isWildcard || _isNil;
                if (_isWildcard) return true;
                if (_isNil) return false; // nil doesn't match existing nodes
                
                // Handle ellipsis pattern - matches nodes with children
                if (IsEllipsis())
                {
                    DebugLog($"[Find] Checking ellipsis pattern against node: {node?.Descriptor?.Name}");
                    return HasChildren(node);
                }
                
                DebugLog($"[Find] Matching '{_targetString}' against node: {node?.Descriptor?.Name}");

                // Handle attribute patterns like (relname "users")
                if (_isAttributePattern)
                {
                    DebugLog($"[Find] Checking attribute pattern: {_attributeName} = {_attributeValue}");
                    return MatchesAttribute(node, _attributeName, _attributeValue);
                }

                // Handle node type matching
                var nodeType = node?.Descriptor?.Name ?? "";
                if (MatchesNodeType(nodeType, _targetString))
                {
                    DebugLog($"[Find] Node type match: {nodeType} matches {_targetString}");
                    return true;
                }

                // Handle nested value matching
                if (MatchesNestedValue(node, _target))
                {
                    DebugLog($"[Find] Nested value match found");
                    return true;
                }

                DebugLog($"[Find] No match found for '{_targetString}' against {nodeType}");
                return false;
            }

            // Public interface - can be overridden by specialized classes
            public virtual bool Match(IMessage node) => MatchCore(node);

            // Helper methods for pattern matching
            private bool MatchesNodeType(string nodeType, string pattern)
            {
                if (string.IsNullOrEmpty(nodeType) || string.IsNullOrEmpty(pattern)) return false;
                
                // Direct match
                if (string.Equals(nodeType, pattern, StringComparison.OrdinalIgnoreCase)) return true;
                
                // Handle case conversions
                if (HandleCaseConversions(nodeType, pattern)) return true;
                
                return false;
            }

            // Performance: Optimized value matching with case-insensitive handling
            private bool MatchesValue(object value, object target)
            {
                if (value == null || target == null) return value == target;
                
                // Direct equality check
                if (value.Equals(target)) return true;
                
                // String comparison (case-sensitive for performance)
                var valueStr = value.ToString();
                var targetStr = target.ToString();
                
                if (valueStr == targetStr) return true;
                
                // Case-insensitive comparison for common patterns
                if (string.Equals(valueStr, targetStr, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // Handle common case conversions (camelCase vs lowercase)
                if (SqlPatternMatcher.HandleCaseConversions(valueStr, targetStr))
                {
                    return true;
                }
                
                // Numeric comparison for performance
                if (IsNumeric(value) && IsNumeric(target))
                {
                    return value.Equals(target);
                }
                
                return false;
            }

            private bool MatchesNestedValue(IMessage message, object target)
            {
                if (message?.Descriptor == null) return false;

                foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
                {
                    try
                    {
                        var fieldValue = field.Accessor.GetValue(message);
                        
                        if (field.IsRepeated)
                        {
                            var list = (System.Collections.IList?)fieldValue;
                            if (list != null)
                            {
                                foreach (var item in list)
                                {
                                    if (MatchesValue(item, target)) return true;
                                }
                            }
                        }
                        else if (fieldValue != null)
                        {
                            if (MatchesValue(fieldValue, target)) return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"[Find] Error accessing field {field.Name}: {ex.Message}");
                    }
                }
                return false;
            }

            private bool IsNumeric(object value) =>
                value is int || value is long || value is float || value is double || value is decimal;

            private bool HasChildren(IMessage node)
            {
                if (node?.Descriptor == null) return false;
                
                foreach (var field in node.Descriptor.Fields.InFieldNumberOrder())
                {
                    try
                    {
                        var fieldValue = field.Accessor.GetValue(node);
                        
                        if (field.IsRepeated)
                        {
                            var list = (System.Collections.IList?)fieldValue;
                            if (list != null && list.Count > 0) return true;
                        }
                        else if (fieldValue is IMessage) return true;
                    }
                    catch
                    {
                        // Continue checking other fields
                    }
                }
                return false;
            }

            private bool ParseAttributePattern(string pattern, out string attributeName, out object attributeValue)
            {
                attributeName = "";
                attributeValue = "";
                
                // Check for s-expression pattern: (attribute value_expression)
                if (!pattern.StartsWith("(") || !pattern.EndsWith(")")) return false;
                
                var inner = pattern.Substring(1, pattern.Length - 2).Trim();
                var parts = inner.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length != 2) return false;
                
                attributeName = parts[0].Trim();
                var valueExpressionStr = parts[1].Trim();
                
                // Parse the value expression using the same expression parser
                // This allows full expression syntax like _, !value, {value1 value2 !value3}, etc.
                try
                {
                    var expressionParser = new ExpressionParser(valueExpressionStr);
                    var valueExpression = expressionParser.Parse();
                    attributeValue = valueExpression;
                    return true;
                }
                catch (Exception ex)
                {
                    DebugLog($"[Find] Failed to parse attribute value expression '{valueExpressionStr}': {ex.Message}");
                    
                    // Fallback to simple string parsing for basic quoted strings
                    if (valueExpressionStr.StartsWith("\"") && valueExpressionStr.EndsWith("\"") && valueExpressionStr.Length > 1)
                    {
                        attributeValue = valueExpressionStr.Substring(1, valueExpressionStr.Length - 2);
                    }
                    else if (valueExpressionStr.StartsWith("'") && valueExpressionStr.EndsWith("'") && valueExpressionStr.Length > 1)
                    {
                        attributeValue = valueExpressionStr.Substring(1, valueExpressionStr.Length - 2);
                    }
                    else
                    {
                        attributeValue = valueExpressionStr;
                    }
                    return true;
                }
            }

            private bool MatchesAttribute(IMessage node, string attributeName, object expectedValue)
            {
                if (node?.Descriptor == null || string.IsNullOrEmpty(attributeName)) return false;

                DebugLog($"[Find] Checking attribute {attributeName} against node {node.Descriptor.Name}");
                
                // Find the field with the given name
                var field = node.Descriptor.Fields.InFieldNumberOrder()
                    .FirstOrDefault(f => string.Equals(f.Name, attributeName, StringComparison.OrdinalIgnoreCase));
                
                if (field == null)
                {
                    DebugLog($"[Find] Field {attributeName} not found in {node.Descriptor.Name}");
                    return false;
                }

                try
                {
                    var fieldValue = field.Accessor.GetValue(node);
                    DebugLog($"[Find] Field {attributeName} has value: {fieldValue}");
                    
                    // If expectedValue is an IExpression, evaluate it against the field value
                    if (expectedValue is IExpression expression)
                    {
                        var result = MatchExpressionAgainstValue(expression, fieldValue);
                        DebugLog($"[Find] Expression match result for attribute {attributeName}: {result}");
                        return result;
                    }
                    
                    // Fallback to simple value matching
                    return MatchesValue(fieldValue, expectedValue);
                }
                catch (Exception ex)
                {
                    DebugLog($"[Find] Error accessing field {attributeName}: {ex.Message}");
                    return false;
                }
            }
            
            private bool MatchExpressionAgainstValue(IExpression expression, object fieldValue)
            {
                // Handle different expression types when matching against a field value
                if (expression is Find findExpr)
                {
                    // For Find expressions, match the pattern directly against the field value
                    if (findExpr.IsWildcard())
                    {
                        DebugLog($"[Find] Wildcard matches any field value: {fieldValue}");
                        return true;
                    }
                    
                    if (findExpr.IsNil())
                    {
                        DebugLog($"[Find] Nil pattern matches null field value: {fieldValue == null}");
                        return fieldValue == null;
                    }
                    
                    // For regular Find, match against the target value using string comparison
                    var targetString = findExpr._targetString;
                    var fieldString = fieldValue?.ToString() ?? "";
                    
                    DebugLog($"[Find] Comparing field value '{fieldString}' with target '{targetString}'");
                    
                    // Direct string match
                    if (string.Equals(fieldString, targetString, StringComparison.OrdinalIgnoreCase))
                    {
                        DebugLog($"[Find] Direct string match found");
                        return true;
                    }
                    
                    // Use the existing MatchesValue method for more complex matching
                    return MatchesValue(fieldValue, targetString);
                }
                
                if (expression is Not notExpr)
                {
                    // Negation: !value means NOT value
                    var innerExpr = GetNotInnerExpression(notExpr);
                    if (innerExpr != null)
                    {
                        var innerMatch = MatchExpressionAgainstValue(innerExpr, fieldValue);
                        DebugLog($"[Find] Negation result: !({innerMatch}) = {!innerMatch}");
                        return !innerMatch;
                    }
                }
                
                if (expression is Any anyExpr)
                {
                    // Set matching: {value1 value2 !value3} - any of the expressions can match
                    var innerExpressions = GetAnyInnerExpressions(anyExpr);
                    foreach (var innerExpr in innerExpressions)
                    {
                        if (MatchExpressionAgainstValue(innerExpr, fieldValue))
                        {
                            DebugLog($"[Find] Set pattern matched for field value: {fieldValue}");
                            return true;
                        }
                    }
                    DebugLog($"[Find] Set pattern did not match field value: {fieldValue}");
                    return false;
                }
                
                // For other expression types, fallback to string comparison
                DebugLog($"[Find] Fallback string comparison for expression type: {expression.GetType().Name}");
                return false;
            }
            
            private IExpression GetNotInnerExpression(Not notExpr)
            {
                // Use reflection to access the private _expression field
                var field = typeof(Not).GetField("_expression", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (IExpression)field?.GetValue(notExpr);
            }
            
            private IExpression[] GetAnyInnerExpressions(Any anyExpr)
            {
                // Use reflection to access the private _expressions field
                var field = typeof(Any).GetField("_expressions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (IExpression[])field?.GetValue(anyExpr) ?? new IExpression[0];
            }
        }

        // Not Expression - negation with performance optimizations
        private class Not : IExpression
        {
            private readonly IExpression _expression;

            public Not(IExpression expression) => _expression = expression;

            public bool Match(IMessage node) => !_expression.Match(node);
        }

        // Any Expression - OR operation with short-circuit evaluation
        private class Any : IExpression
        {
            private readonly IExpression[] _expressions;

            public Any(IExpression[] expressions) => _expressions = expressions;

            public bool Match(IMessage node)
            {
                // Performance: Short-circuit on first match
                foreach (var expr in _expressions)
                {
                    if (expr.Match(node)) return true;
                }
                return false;
            }
        }

        // All Expression - AND operation with short-circuit evaluation
        private class All : Find
        {
            private readonly IExpression[] _expressions;

            public All(IExpression[] expressions) : base("_") => _expressions = expressions;

            public override bool Match(IMessage node)
            {
                Console.WriteLine($"[All] Matching All expression with {_expressions.Length} parts against node: {node?.Descriptor?.Name}");
                
                // First try base Find logic for attribute patterns and simple matching
                if (base.Match(node)) return true;
                
                // Handle patterns that start with Until expressions (ellipsis patterns)
                // For patterns like (... something) or (SelectStmt ... something)
                for (int i = 0; i < _expressions.Length; i++)
                {
                    if (_expressions[i] is Until untilExpr)
                    {
                        Console.WriteLine($"[All] Detected Until expression at index {i}");
                        
                        // If we have expressions before the Until, they must match first
                        for (int j = 0; j < i; j++)
                        {
                            if (!_expressions[j].Match(node))
                            {
                                Console.WriteLine($"[All] Expression {j} failed to match before Until");
                                return false;
                            }
                        }
                        
                        Console.WriteLine($"[All] All expressions before Until matched, now applying Until");
                        // The Until expression searches in the subtree for its targets
                        return untilExpr.Match(node);
                    }
                }
                
                // Handle 2-part ellipsis patterns like (SelectStmt ...)
                if (_expressions.Length == 2 && 
                    _expressions[1] is Find ellipsisFind && ellipsisFind.IsEllipsis())
                {
                    // First expression should match the current node
                    if (!_expressions[0].Match(node)) return false;
                    
                    // Second expression (...) should confirm node has children
                    return _expressions[1].Match(node);
                }
                
                // Standard All behavior: all expressions must match the same node
                foreach (var expr in _expressions)
                {
                    if (!expr.Match(node)) return false;
                }
                return true;
            }
        }

        // Capture Expression - with thread-safe capture storage
        private class Capture : IExpression
        {
            private readonly IExpression _expression;

            public Capture(IExpression expression) => _expression = expression;

            public bool Match(IMessage node)
            {
                if (_expression.Match(node))
                {
                    AddCapture(node);
                    return true;
                }
                return false;
            }
        }

        // Maybe Expression - optional matching
        private class Maybe : IExpression
        {
            private readonly IExpression _expression;

            public Maybe(IExpression expression) => _expression = expression;

            public bool Match(IMessage node) => node == null || _expression.Match(node);
        }

        // Parent Expression - check children efficiently
        private class Parent : IExpression
        {
            private readonly IExpression _expression;

            public Parent(IExpression expression) => _expression = expression;

            public bool Match(IMessage node)
            {
                if (node == null) return false;

                var descriptor = node.Descriptor;
                if (descriptor == null) return false;

                // Performance: Early termination on first match
                foreach (var field in descriptor.Fields.InFieldNumberOrder())
                {
                    if (field.IsRepeated)
                    {
                        var list = (System.Collections.IList)field.Accessor.GetValue(node);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                if (item is IMessage child && _expression.Match(child))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    else
                    {
                        var value = field.Accessor.GetValue(node);
                        if (value is IMessage child && _expression.Match(child))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        // Until Expression - ellipsis pattern matching with recursive traversal
        private class Until : IExpression
        {
            private readonly IExpression[] _expressions;

            public Until(IExpression[] expressions) 
            {
                _expressions = expressions;
                Console.WriteLine($"[Until] Created Until expression with {expressions.Length} target expressions");
            }

            public Until(IExpression expression) 
            {
                _expressions = new[] { expression };
                Console.WriteLine($"[Until] Created Until expression with single target: {expression?.GetType().Name}");
            }

            public bool Match(IMessage node)
            {
                Console.WriteLine($"[Until] Matching {_expressions.Length} expressions against node: {node?.Descriptor?.Name}");
                
                // All target expressions must be found somewhere in the subtree
                foreach (var expr in _expressions)
                {
                    var result = SearchInSubtree(node, expr);
                    Console.WriteLine($"[Until] Expression {expr.GetType().Name} search result: {result}");
                    if (!result)
                    {
                        Console.WriteLine($"[Until] Failed to find expression {expr.GetType().Name} in subtree");
                        return false;
                    }
                }
                
                Console.WriteLine($"[Until] All expressions found in subtree");
                return true;
            }

            private bool SearchInSubtree(IMessage node, IExpression targetExpression)
            {
                if (node == null) 
                {
                    Console.WriteLine($"[Until] Node is null, returning false");
                    return false;
                }

                Console.WriteLine($"[Until] Checking node: {node.Descriptor?.Name}");
                
                // First check if the target expression matches the current node
                if (targetExpression.Match(node)) 
                {
                    Console.WriteLine($"[Until] Target expression matched current node!");
                    return true;
                }

                var descriptor = node.Descriptor;
                if (descriptor == null) 
                {
                    Console.WriteLine($"[Until] Node descriptor is null");
                    return false;
                }

                // Recursively search all children - improved traversal
                foreach (var field in descriptor.Fields.InFieldNumberOrder())
                {
                    Console.WriteLine($"[Until] Checking field: {field.Name} (repeated: {field.IsRepeated})");
                    
                    try
                    {
                        var fieldValue = field.Accessor.GetValue(node);
                        
                        if (field.IsRepeated)
                        {
                            var list = (System.Collections.IList?)fieldValue;
                            if (list != null)
                            {
                                Console.WriteLine($"[Until] Field {field.Name} has {list.Count} items");
                                foreach (var item in list)
                                {
                                    if (item is IMessage child)
                                    {
                                        if (SearchInSubtree(child, targetExpression))
                                        {
                                            Console.WriteLine($"[Until] Found match in repeated field {field.Name}");
                                            return true;
                                        }
                                    }
                                    else if (item != null)
                                    {
                                        Console.WriteLine($"[Until] Non-message item in {field.Name}: {item} ({item.GetType().Name})");
                                        // For attribute patterns, also check non-message values
                                        if (targetExpression is Find findExpr && findExpr.MatchesDirectValue(item))
                                        {
                                            Console.WriteLine($"[Until] Direct value match found!");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (fieldValue is IMessage child)
                            {
                                if (SearchInSubtree(child, targetExpression))
                                {
                                    Console.WriteLine($"[Until] Found match in field {field.Name}");
                                    return true;
                                }
                            }
                            else if (fieldValue != null)
                            {
                                Console.WriteLine($"[Until] Non-message field {field.Name}: {fieldValue} ({fieldValue.GetType().Name})");
                                // For attribute patterns, also check non-message values
                                if (targetExpression is Find findExpr && findExpr.MatchesDirectValue(fieldValue))
                                {
                                    Console.WriteLine($"[Until] Direct value match found in field {field.Name}!");
                                    return true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Until] Error accessing field {field.Name}: {ex.Message}");
                        // Continue with other fields
                    }
                }
                
                Console.WriteLine($"[Until] No match found in subtree of {node.Descriptor?.Name}");
                return false;
            }
        }

        // Expression Parser - with performance optimizations
        private class ExpressionParser
        {
            private readonly Queue<string> _tokens;

            public ExpressionParser(string expression)
            {
                // Performance: Pre-filter and cache token matches
                var matches = TOKENIZER.Matches(expression);
                _tokens = new Queue<string>(matches.Count);
                
                foreach (Match match in matches)
                {
                    _tokens.Enqueue(match.Value);
                }
            }

            public IExpression Parse()
            {
                if (_tokens.Count == 0) return LITERALS["_"];

                var token = _tokens.Dequeue();
                DebugLog($"[Parser] Parsing token: '{token}'");
                
                var result = token switch
                {
                    "(" => new All(ParseUntil(")")),
                    "[" => new All(ParseUntil("]")),
                    "{" => new Any(ParseUntil("}")),
                    "$" => new Capture(Parse()),
                    "!" => new Not(Parse()),
                    "?" => new Maybe(Parse()),
                    "^" => new Parent(Parse()),
                    "..." => new Until(ParseUntil("")),
                    var str when str.StartsWith("\"") && str.EndsWith("\"") => new Find(str[1..^1]),
                    var str when LITERALS.ContainsKey(str) => LITERALS[str],
                    _ => new Find(TypecastValue(token))
                };
                
                DebugLog($"[Parser] Created expression: {result.GetType().Name}");
                return result;
            }

            private IExpression[] ParseUntil(string endToken)
            {
                var expressions = new List<IExpression>();
                while (_tokens.Count > 0 && _tokens.Peek() != endToken)
                {
                    expressions.Add(Parse());
                }
                if (_tokens.Count > 0) _tokens.Dequeue(); // Remove end token
                return expressions.ToArray();
            }
                
            // Performance: Optimized value type casting
            private object TypecastValue(string token)
            {
                if (string.IsNullOrEmpty(token)) return token;
                
                // Performance: Check common cases first
                if (token.Length == 1 && char.IsDigit(token[0]))
                {
                    return token[0] - '0'; // Fast single digit conversion
                }
                
                if (int.TryParse(token, out var intValue)) return intValue;
                if (double.TryParse(token, out var doubleValue)) return doubleValue;
                
                return token;
            }
        }

        /// <summary>
        /// Get parse tree with PL/pgSQL support for visualization.
        /// </summary>
        /// <param name="sql">SQL string to parse</param>
        /// <param name="includeDoStmt">Whether to include parsed DoStmt content</param>
        /// <returns>Parse result with embedded PL/pgSQL trees</returns>
        public static ParseResult? GetParseTreeWithPlPgSql(string sql, bool includeDoStmt = true)
        {
            try
            {
                var parseResult = PgQuery.Parse(sql);
                
                if (includeDoStmt && parseResult?.ParseTree?.Stmts != null)
                {
                    // Process DoStmt nodes to include their PL/pgSQL content
                    foreach (var stmt in parseResult.ParseTree.Stmts)
                    {
                        if (stmt?.Stmt != null)
                        {
                            ProcessDoStmtForTree(stmt.Stmt);
                        }
                    }
                }
                
                return parseResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlPatternMatcher] Parse tree generation failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Process DoStmt nodes recursively to include PL/pgSQL content.
        /// </summary>
        private static void ProcessDoStmtForTree(IMessage node)
        {
            if (node == null) return;

            // Check if this is a DoStmt
            if (node.Descriptor?.Name == "DoStmt")
            {
                var plpgsqlContent = ExtractSqlFromDoStmt(node);
                if (!string.IsNullOrEmpty(plpgsqlContent))
                {
                    try
                    {
                        // Parse the PL/pgSQL content
                        var plpgsqlJson = PgQuery.ParsePlpgsql(plpgsqlContent);
                        
                        // Store the parsed content for tree visualization
                        // This would typically be stored as metadata or in a wrapper
                        DebugLog($"[ProcessDoStmtForTree] Parsed PL/pgSQL content for tree: {plpgsqlJson.Length} chars");
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"[ProcessDoStmtForTree] Failed to parse PL/pgSQL: {ex.Message}");
                    }
                }
            }

            // Recursively process children
            var descriptor = node.Descriptor;
            if (descriptor != null)
            {
                foreach (var field in descriptor.Fields.InFieldNumberOrder())
                {
                    if (field.IsRepeated)
                    {
                        var list = (System.Collections.IList)field.Accessor.GetValue(node);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                if (item is IMessage child)
                                {
                                    ProcessDoStmtForTree(child);
                                }
                            }
                        }
                    }
                    else
                    {
                        var value = field.Accessor.GetValue(node);
                        if (value is IMessage child)
                        {
                            ProcessDoStmtForTree(child);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Search within PL/pgSQL content.
        /// </summary>
        private static void SearchInPlPgSql(IExpression expression, PlPgSqlWrapper plpgsqlWrapper, List<IMessage> results)
        {
            // Check if the expression matches the PL/pgSQL wrapper itself
            if (expression.Match(plpgsqlWrapper))
            {
                results.Add(plpgsqlWrapper);
            }
            
            // Parse the JSON and create searchable nodes from the PL/pgSQL AST
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(plpgsqlWrapper.PlPgSqlJson);
                
                // Create virtual nodes from the PL/pgSQL AST for searching
                var plpgsqlNodes = CreateSearchableNodesFromPlPgSqlJson(doc.RootElement);
                
                foreach (var node in plpgsqlNodes)
                {
                    if (expression.Match(node))
                    {
                        results.Add(node);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[SearchInPlPgSql] Failed to parse PL/pgSQL JSON for searching: {ex.Message}");
            }
        }

        /// <summary>
        /// Create searchable nodes from PL/pgSQL JSON AST.
        /// </summary>
        private static List<IMessage> CreateSearchableNodesFromPlPgSqlJson(System.Text.Json.JsonElement rootElement)
        {
            var nodes = new List<IMessage>();
            
            CreateSearchableNodesRecursive(rootElement, nodes);
            
            return nodes;
        }

        /// <summary>
        /// Recursively create searchable nodes from PL/pgSQL JSON elements.
        /// </summary>
        private static void CreateSearchableNodesRecursive(System.Text.Json.JsonElement element, List<IMessage> nodes)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    // Create a wrapper node for this JSON object
                    var wrapperNode = new PlPgSqlJsonNode(element);
                    nodes.Add(wrapperNode);
                    
                    // Recursively process nested objects
                    foreach (var property in element.EnumerateObject())
                    {
                        CreateSearchableNodesRecursive(property.Value, nodes);
                    }
                    break;
                    
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        CreateSearchableNodesRecursive(item, nodes);
                    }
                    break;
            }
        }
    }
} 