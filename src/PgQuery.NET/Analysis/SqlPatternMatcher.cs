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
                
                // For simple wildcard patterns like "_", only check the root node
                if (pattern == "_" || pattern == "..." || pattern == "nil")
                {
                    var rootNode = ast.ParseTree.Stmts[0].Stmt;
                    if (expression.Match(rootNode))
                    {
                        results.Add(rootNode);
                    }
                }
                else
                {
                    // For specific node types or complex patterns, search recursively
                    SearchRecursive(expression, ast.ParseTree.Stmts[0].Stmt, results);
                }
                
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

            // Check for s-expression attribute pattern BEFORE complex parsing
            // because ExpressionParser doesn't handle s-expressions
            if (pattern.StartsWith("(") && pattern.EndsWith(")"))
            {
                return new Find(pattern);
            }

            // Performance: Fast path for simple patterns
            if (IsSimplePattern(pattern))
            {
                return new Find(pattern);
            }
            
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
            if (ConvertCamelToUnderscore(value) == target.ToLowerInvariant() ||
                ConvertCamelToUnderscore(target) == value.ToLowerInvariant())
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
        private static string ConvertCamelToUnderscore(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
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
        private static string ConvertUnderscoreToCamel(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
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
            private readonly string _targetString;
            private readonly bool _isWildcard;
            private readonly bool _isAttributePattern;
            private readonly string _attributeName;
            private readonly object _attributeValue;

            public Find(object target)
            {
                _target = target;
                _targetString = target?.ToString() ?? "";
                _isWildcard = _targetString == "_";
                
                // Check if this is an s-expression attribute pattern: (relname "users")
                _isAttributePattern = ParseAttributePattern(_targetString, out _attributeName, out _attributeValue);
            }
            
            public bool IsEllipsis() => _targetString == "...";

            public bool Match(IMessage node)
            {
                if (node == null) return _targetString == "nil";
                
                // Performance: Fast path for wildcards
                if (_isWildcard) return true;

                // Handle s-expression attribute patterns: (relname "users")
                if (_isAttributePattern)
                {
                    return MatchesAttribute(node, _attributeName, _attributeValue);
                }

                // Handle common patterns efficiently
                if (_targetString == "...")
                {
                    return HasChildren(node);
                }

                // Check node type with case-insensitive and case conversion support
                var nodeTypeName = node.Descriptor?.Name;
                if (nodeTypeName != null && MatchesNodeType(nodeTypeName, _targetString))
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
            
            // Enhanced node type matching with case conversion support
            private bool MatchesNodeType(string nodeType, string pattern)
            {
                // Direct match
                if (nodeType == pattern) return true;
                
                // Case-insensitive match
                if (string.Equals(nodeType, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // Handle case conversions
                return SqlPatternMatcher.HandleCaseConversions(nodeType, pattern);
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
                
                // For protobuf nested structures, recursively check nested values
                if (value is IMessage nestedMessage)
                {
                    return MatchesNestedValue(nestedMessage, target);
                }

                return false;
            }

            
            private bool MatchesNestedValue(IMessage message, object target)
            {
                var descriptor = message.Descriptor;
                if (descriptor == null) return false;

                foreach (var field in descriptor.Fields.InFieldNumberOrder())
                {
                    var fieldValue = field.Accessor.GetValue(message);
                    
                    if (fieldValue != null)
                    {
                        // Direct match
                        if (MatchesValue(fieldValue, target)) return true;
                        
                        // For repeated fields
                        if (field.IsRepeated && fieldValue is System.Collections.IList list)
                        {
                            foreach (var item in list)
                            {
                                if (item != null && MatchesValue(item, target)) return true;
                            }
                        }
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

            // Parse s-expression attribute pattern: (relname "users")
            private bool ParseAttributePattern(string pattern, out string attributeName, out object attributeValue)
            {
                attributeName = "";
                attributeValue = "";

                if (string.IsNullOrEmpty(pattern) || !pattern.StartsWith("(") || !pattern.EndsWith(")"))
                    return false;

                // Remove parentheses
                var inner = pattern.Substring(1, pattern.Length - 2).Trim();
                
                // Split on whitespace - simple approach
                var parts = inner.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length != 2)
                    return false;

                attributeName = parts[0];
                var valueStr = parts[1];

                // Parse the value (remove quotes if present)
                if (valueStr.StartsWith("\"") && valueStr.EndsWith("\"") && valueStr.Length > 1)
                {
                    attributeValue = valueStr.Substring(1, valueStr.Length - 2);
                }
                else if (valueStr.StartsWith("'") && valueStr.EndsWith("'") && valueStr.Length > 1)
                {
                    attributeValue = valueStr.Substring(1, valueStr.Length - 2);
                }
                else if (int.TryParse(valueStr, out var intValue))
                {
                    attributeValue = intValue;
                }
                else if (double.TryParse(valueStr, out var doubleValue))
                {
                    attributeValue = doubleValue;
                }
                else if (bool.TryParse(valueStr, out var boolValue))
                {
                    attributeValue = boolValue;
                }
                else
                {
                    attributeValue = valueStr; // Keep as string
                }


                return true;
            }

            // Match node by specific attribute value
            private bool MatchesAttribute(IMessage node, string attributeName, object expectedValue)
            {
                if (node?.Descriptor == null) return false;

                var field = node.Descriptor.Fields.InDeclarationOrder()
                    .FirstOrDefault(f => f.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                
                if (field == null) return false;

                var actualValue = field.Accessor.GetValue(node);
                if (actualValue == null) return false;

                return MatchesValue(actualValue, expectedValue);
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
                // Handle special case for recursive child validation patterns like (A_Const ... (_ {true 1}))
                // Check if pattern has form: nodeType + "..." + childPattern
                if (_expressions.Length >= 3 && 
                    _expressions[1] is Find ellipsisFind && ellipsisFind.IsEllipsis())
                {
                    // First expression should match the current node
                    if (!_expressions[0].Match(node)) return false;
                    
                    // Second expression (...) should confirm node has children
                    if (!_expressions[1].Match(node)) return false;
                    
                    // Remaining expressions should match against children
                    var childExpressions = _expressions.Skip(2).ToArray();
                    if (childExpressions.Length > 0)
                    {
                        return MatchChildren(node, childExpressions);
                    }
                    return true;
                }
                
                // Standard All behavior: all expressions must match the same node
                foreach (var expr in _expressions)
                {
                    if (!expr.Match(node)) return false;
                }
                return true;
            }
            
            private bool MatchChildren(IMessage node, IExpression[] childExpressions)
            {
                if (node == null) return false;

                var descriptor = node.Descriptor;
                if (descriptor == null) return false;

                // Check each child field
                foreach (var field in descriptor.Fields.InFieldNumberOrder())
                {
                    if (field.IsRepeated)
                    {
                        var list = (System.Collections.IList)field.Accessor.GetValue(node);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                if (item is IMessage child && MatchAllChildExpressions(child, childExpressions))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    else
                    {
                        var value = field.Accessor.GetValue(node);
                        if (value is IMessage child && MatchAllChildExpressions(child, childExpressions))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            
            private bool MatchAllChildExpressions(IMessage child, IExpression[] expressions)
            {
                foreach (var expr in expressions)
                {
                    if (!expr.Match(child)) return false;
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