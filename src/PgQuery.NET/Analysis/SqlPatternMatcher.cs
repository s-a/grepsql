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
            ["..."] = new Find("..."), // Match any node with children
            ["nil"] = new Find("nil")   // Match null/empty nodes
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
                ClearCaptures();
                
                // Performance: Use cached expression if available
                var expression = GetCachedExpression(pattern);
                var ast = PgQuery.Parse(sql);
                
                return expression.Match(ast.ParseTree.Stmts[0].Stmt);
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
                if (debug)
                {
                    Console.WriteLine($"[SqlPatternMatcher] Searching for pattern: {pattern}");
                }
                
                ClearCaptures();
                
                var expression = GetCachedExpression(pattern);
                var ast = PgQuery.Parse(sql);
                var results = new List<IMessage>();
                
                SearchRecursive(expression, ast.ParseTree.Stmts[0].Stmt, results);
                
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
        /// Compile pattern into optimized expression tree.
        /// </summary>
        private static IExpression CompileExpression(string pattern)
        {
            // Performance: Handle common literals directly
            if (LITERALS.TryGetValue(pattern, out var literal))
            {
                return literal;
            }

            // Performance: Fast path for simple patterns
            if (IsSimplePattern(pattern))
            {
                return new Find(pattern);
            }

            // Full parsing for complex patterns
            return new ExpressionParser(pattern).Parse();
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
        /// Recursive search with performance optimizations.
        /// </summary>
        private static void SearchRecursive(IExpression expression, IMessage node, List<IMessage> results)
        {
            if (node == null) return;

            // Check if current node matches
            if (expression.Match(node))
            {
                results.Add(node);
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
            _captures.Value.Clear();
        }

        /// <summary>
        /// Add node to captures for current thread.
        /// </summary>
        internal static void AddCapture(IMessage node)
        {
            _captures.Value.Add(node);
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

        // Core Expression Interface
        private interface IExpression
        {
            bool Match(IMessage node);
        }

        // Find Expression - matches node types, field values, and primitives
        private class Find : IExpression
        {
            private readonly object _target;
            private readonly string _targetString;
            private readonly bool _isWildcard;

            public Find(object target)
            {
                _target = target;
                _targetString = target?.ToString() ?? "";
                _isWildcard = _targetString == "_";
            }

            public bool Match(IMessage node)
            {
                if (node == null) return _targetString == "nil";
                
                // Performance: Fast path for wildcards
                if (_isWildcard) return true;

                // Handle common patterns efficiently
                if (_targetString == "...")
                {
                    return HasChildren(node);
                }

                // Check node type
                if (node.Descriptor?.Name == _targetString)
                {
                    return true;
                }

                // Check field values - optimized iteration
                var descriptor = node.Descriptor;
                if (descriptor != null)
                {
                    foreach (var field in descriptor.Fields.InFieldNumberOrder())
                    {
                        var value = field.Accessor.GetValue(node);
                        if (value != null && MatchesValue(value, _target))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            // Performance: Optimized value matching
            private bool MatchesValue(object value, object target)
            {
                if (value == null || target == null) return value == target;
                
                // Direct equality check
                if (value.Equals(target)) return true;
                
                // String comparison (case-sensitive for performance)
                if (value.ToString() == target.ToString()) return true;
                
                // Numeric comparison for different types
                if (IsNumeric(value) && IsNumeric(target))
                {
                    try
                    {
                        return Convert.ToDouble(value) == Convert.ToDouble(target);
                    }
                    catch
                    {
                        return false;
                    }
                }

                return false;
            }

            // Performance: Fast numeric type checking
            private bool IsNumeric(object value) =>
                value is int || value is long || value is float || value is double || value is decimal;

            // Performance: Optimized children checking
            private bool HasChildren(IMessage node)
            {
                var descriptor = node.Descriptor;
                if (descriptor == null) return false;

                foreach (var field in descriptor.Fields.InFieldNumberOrder())
                {
                    if (field.IsRepeated)
                    {
                        var list = (System.Collections.IList)field.Accessor.GetValue(node);
                        if (list != null && list.Count > 0) return true;
                    }
                    else
                    {
                        var value = field.Accessor.GetValue(node);
                        if (value is IMessage) return true;
                    }
                }
                return false;
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
        private class All : IExpression
        {
            private readonly IExpression[] _expressions;

            public All(IExpression[] expressions) => _expressions = expressions;

            public bool Match(IMessage node)
            {
                // Performance: Short-circuit on first failure
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
                return token switch
                {
                    "(" => new All(ParseUntil(")")),
                    "[" => new All(ParseUntil("]")),
                    "{" => new Any(ParseUntil("}")),
                    "$" => new Capture(Parse()),
                    "!" => new Not(Parse()),
                    "?" => new Maybe(Parse()),
                    "^" => new Parent(Parse()),
                    var str when str.StartsWith("\"") && str.EndsWith("\"") => new Find(str[1..^1]),
                    var str when LITERALS.ContainsKey(str) => LITERALS[str],
                    _ => new Find(TypecastValue(token))
                };
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
    }
} 