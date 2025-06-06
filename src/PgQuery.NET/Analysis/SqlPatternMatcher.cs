using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using PgQuery.NET.AST;

namespace PgQuery.NET.Analysis
{
    /// <summary>
    /// SQL pattern matching functionality for Protocol Buffer AST nodes
    /// </summary>
    public static class SqlPatternMatcher
    {
        private static readonly Dictionary<string, List<IMessage>> _captures = new();
        private static bool _debug = false;
        private static bool _verbose = false;
        private static int _depth = 0;
        private static readonly HashSet<IMessage> _matchingPath = new();
        private static bool _inSubtreeSearch = false;

        // Pattern matching literals
        private static readonly Dictionary<string, Func<IMessage, bool>> Literals = new()
        {
            ["..."] = node => true,
            ["_"] = node => node != null
        };

        // Tokenizer pattern for SQL pattern expressions
        private static readonly Regex TokenPattern = new(
            @"
            \\\\d+              # find using captured expression (MUST come before operators!)
            |
            ===?                  # == or ===
            |
            true|false           # boolean literals
            |
            \d+\.\d*             # decimals and floats
            |
            \d+                  # integers
            |
            ""[^""]*""           # double-quoted strings
            |
            '[^']*'              # single-quoted strings
            |
            _                    # something not nil: match
            |
            \.{3}               # a node with children: ...
            |
            \[|\]               # square brackets `[` and `]` for all
            |
            \^                  # node has children with
            |
            \?                  # maybe expression
            |
            [\w_]+[=\!\?]?      # method names (without leading digits to avoid conflict with numbers)
            |
            \(|\)               # parens `(` and `)` for tuples
            |
            \{|\}               # curly brackets `{` and `}` for any
            |
            \$\w[\d\w_]*        # capture variable: $name
            |
            \#\w[\d\w_]+[\\!\?]? # custom method call
            |
            \.\w[\d\w_]+\?      # instance method call
            |
            [\+\-\/\*!]         # operators or negation (MUST come after backreferences!)
            |
            %\d                 # bind extra arguments to the expression",
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled
        );

        public static bool Matches(string pattern, string sql, bool debug = false, bool verbose = false)
        {
            try
            {
                _captures.Clear();
                _matchingPath.Clear();
                _debug = debug;
                _verbose = verbose;
                _depth = 0;

                var sqlAst = PgQuery.Parse(sql);
                var expression = ExpressionParser.Parse(pattern);

                if (_debug)
                {
                    Console.WriteLine("Pattern Expression:");
                    Console.WriteLine(pattern);
                    Console.WriteLine("\nSQL AST:");
                    
                    // Use TreePrinter to show a nicely formatted AST
                    var useColors = TreePrinter.SupportsColors();
                    TreePrinter.PrintTree(sqlAst.ParseTree.Stmts[0].Stmt, useColors, maxDepth: 5, TreePrinter.NodeStatus.Normal, TreePrinter.TreeMode.Full);
                    
                    Console.WriteLine("\nMatching...\n");
                }

                // Start matching from the actual statement, not the Node wrapper
                var startNode = sqlAst.ParseTree.Stmts[0].Stmt;
                
                // Handle (stmt (SelectStmt ...)) patterns by skipping the stmt wrapper
                if (expression is All all && all.GetExpressions().Length >= 2 && 
                    all.GetExpressions()[0] is Find stmtFind && stmtFind.Token == "stmt" &&
                    all.GetExpressions()[1] is All innerAll && innerAll.GetExpressions().Length > 0 &&
                    innerAll.GetExpressions()[0] is Find stmtTypeFind &&
                    (stmtTypeFind.Token == "SelectStmt" || stmtTypeFind.Token == "InsertStmt" || stmtTypeFind.Token == "UpdateStmt" || stmtTypeFind.Token == "DeleteStmt"))
                {
                    // Skip the stmt wrapper and match directly with the inner pattern
                    switch (startNode.NodeCase)
                    {
                        case AST.Node.NodeOneofCase.SelectStmt when stmtTypeFind.Token == "SelectStmt":
                            return MatchRecursive(startNode.SelectStmt, all.GetExpressions()[1]);
                        case AST.Node.NodeOneofCase.InsertStmt when stmtTypeFind.Token == "InsertStmt":
                            return MatchRecursive(startNode.InsertStmt, all.GetExpressions()[1]);
                        case AST.Node.NodeOneofCase.UpdateStmt when stmtTypeFind.Token == "UpdateStmt":
                            return MatchRecursive(startNode.UpdateStmt, all.GetExpressions()[1]);
                        case AST.Node.NodeOneofCase.DeleteStmt when stmtTypeFind.Token == "DeleteStmt":
                            return MatchRecursive(startNode.DeleteStmt, all.GetExpressions()[1]);
                    }
                }
                // For simple statement type patterns, match against the unwrapped statement
                else if (expression is Find find && 
                    (find.Token == "SelectStmt" || find.Token == "InsertStmt" || find.Token == "UpdateStmt" || find.Token == "DeleteStmt"))
                {
                    switch (startNode.NodeCase)
                    {
                        case AST.Node.NodeOneofCase.SelectStmt when find.Token == "SelectStmt":
                            return MatchRecursive(startNode.SelectStmt, expression);
                        case AST.Node.NodeOneofCase.InsertStmt when find.Token == "InsertStmt":
                            return MatchRecursive(startNode.InsertStmt, expression);
                        case AST.Node.NodeOneofCase.UpdateStmt when find.Token == "UpdateStmt":
                            return MatchRecursive(startNode.UpdateStmt, expression);
                        case AST.Node.NodeOneofCase.DeleteStmt when find.Token == "DeleteStmt":
                            return MatchRecursive(startNode.DeleteStmt, expression);
                    }
                }
                // For All expressions that start with statement types, match against the unwrapped statement
                else if (expression is All allExpr && allExpr.GetExpressions().Length > 0 && 
                         allExpr.GetExpressions()[0] is Find firstFind &&
                         (firstFind.Token == "SelectStmt" || firstFind.Token == "InsertStmt" || firstFind.Token == "UpdateStmt" || firstFind.Token == "DeleteStmt"))
                {
                    switch (startNode.NodeCase)
                    {
                        case AST.Node.NodeOneofCase.SelectStmt when firstFind.Token == "SelectStmt":
                            return MatchRecursive(startNode.SelectStmt, allExpr);
                        case AST.Node.NodeOneofCase.InsertStmt when firstFind.Token == "InsertStmt":
                            return MatchRecursive(startNode.InsertStmt, allExpr);
                        case AST.Node.NodeOneofCase.UpdateStmt when firstFind.Token == "UpdateStmt":
                            return MatchRecursive(startNode.UpdateStmt, allExpr);
                        case AST.Node.NodeOneofCase.DeleteStmt when firstFind.Token == "DeleteStmt":
                            return MatchRecursive(startNode.DeleteStmt, allExpr);
                    }
                }

                return MatchRecursive(startNode, expression);
            }
            catch (Exception ex)
            {
                if (_debug)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                return false;
            }
        }

        private static bool MatchRecursive(IMessage node, IExpression pattern)
        {
            _depth++;
            var indent = new string(' ', _depth * 2);
            
            if (_debug && _verbose)
            {
                Log($"Trying to match pattern {pattern.GetType().Name} at depth {_depth}", true);
                Log($"Current node: {node}", true);
                
                // Use TreePrinter to show node structure in debug mode
                if (_depth <= 2) // Only show detailed tree for shallow depths to avoid spam
                {
                    TreePrinter.PrintNodeWithStatus(node, TreePrinter.NodeStatus.Normal, _depth, TreePrinter.SupportsColors());
                }
            }

            if (pattern.Match(node))
            {
                if (_debug && _verbose)
                {
                    Log($"✓ Direct match found for {pattern.GetType().Name}", true);
                    if (_depth <= 2)
                    {
                        TreePrinter.PrintNodeWithStatus(node, TreePrinter.NodeStatus.Matched, _depth, TreePrinter.SupportsColors());
                    }
                }
                
                // Add this node to the matching path
                _matchingPath.Add(node);
                
                _depth--;
                return true;
            }

            if (_debug && _verbose)
            {
                Log($"× No direct match, checking children...", true);
                if (_depth <= 2)
                {
                    TreePrinter.PrintNodeWithStatus(node, TreePrinter.NodeStatus.Unmatched, _depth, TreePrinter.SupportsColors());
                }
            }

            // Get all fields of the message
            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                
                if (value == null) continue;

                if (value is IMessage childMessage)
                {
                    if (_debug && _verbose) Log($"Checking message field '{field.Name}'...", true);
                    if (MatchRecursive(childMessage, pattern))
                    {
                        // Add this node to the matching path since it contains a match
                        _matchingPath.Add(node);
                        _depth--;
                        return true;
                    }
                }
                else if (value is IList list)
                {
                    if (_debug && _verbose) Log($"Checking list field '{field.Name}'...", true);
                    foreach (var item in list)
                    {
                        if (item is IMessage itemMessage && MatchRecursive(itemMessage, pattern))
                        {
                            // Add this node to the matching path since it contains a match
                            _matchingPath.Add(node);
                            _depth--;
                            return true;
                        }
                    }
                }
            }

            if (_debug && _verbose)
            {
                Log($"× No match found in children", true);
            }

            _depth--;
            return false;
        }

        private static void Log(string message, bool isRoot = false)
        {
            if (_debug && _verbose && (isRoot || _depth <= 3))
            {
                var indent = new string(' ', _depth * 2);
                Console.WriteLine($"{indent}{message}");
            }
        }

        public static List<IMessage> Search(string pattern, string sql, bool debug = false)
        {
            try
            {
                _captures.Clear();
                _debug = debug;
                _depth = 0;

                var sqlAst = PgQuery.Parse(sql);
                var expression = ExpressionParser.Parse(pattern);
                var results = new List<IMessage>();

                if (_debug)
                {
                    Console.WriteLine("Pattern Expression:");
                    Console.WriteLine(pattern);
                    Console.WriteLine("\nSQL AST:");
                    
                    // Use TreePrinter to show a nicely formatted AST
                    var useColors = TreePrinter.SupportsColors();
                    TreePrinter.PrintTree(sqlAst.ParseTree.Stmts[0].Stmt, useColors, maxDepth: 5, TreePrinter.NodeStatus.Normal, TreePrinter.TreeMode.Full);
                    
                    Console.WriteLine("\nSearching...\n");
                }

                SearchInNode(sqlAst.ParseTree.Stmts[0].Stmt, expression, results);

                if (_debug)
                {
                    Console.WriteLine($"\nFound {results.Count} matches");
                }

                return results;
            }
            catch (Exception ex)
            {
                if (_debug)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                return new List<IMessage>();
            }
        }

        private static void SearchInNode(IMessage node, IExpression pattern, List<IMessage> results)
        {
            _depth++;
            var indent = new string(' ', _depth * 2);

            if (_debug)
            {
                Log($"Searching in node of type {node.GetType().Name}", true);
            }

            // For Node wrappers, check if we should prefer the contained object over the wrapper
            if (node is AST.Node astNode)
            {
                var containedObject = ExtractActualNodeFromWrapper(node);
                if (containedObject != node && pattern.Match(containedObject))
                {
                    // The contained object matches, prefer it over the wrapper
                    if (_debug)
                    {
                        Log($"✓ Found match in contained object {containedObject.GetType().Name} at depth {_depth}", true);
                    }
                    if (!results.Contains(containedObject))
                    {
                        results.Add(containedObject);
                    }
                    // Don't add the wrapper node, and don't recurse into children since we already handled the contained object
                    _depth--;
                    return;
                }
            }

            // Try to match at current level (for non-Node objects or when contained object doesn't match)
            if (pattern.Match(node))
            {
                if (_debug)
                {
                    Log($"✓ Found match at depth {_depth}", true);
                }
                if (!results.Contains(node))
                {
                    results.Add(node);
                }
            }

            // Get all fields of the message
            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                
                if (value == null) continue;

                if (value is IMessage childMessage)
                {
                    if (_debug) Log($"Checking message field '{field.Name}'", true);
                    SearchInNode(childMessage, pattern, results);
                }
                else if (value is IList list)
                {
                    if (_debug) Log($"Checking list field '{field.Name}'", true);
                    foreach (var item in list)
                    {
                        if (item is IMessage itemMessage)
                        {
                            SearchInNode(itemMessage, pattern, results);
                        }
                    }
                }
            }

            _depth--;
        }

        // Expression Parser and AST classes
        private interface IExpression
        {
            bool Match(IMessage node);
        }

        private class Find : IExpression
        {
            private readonly string _token;

            public Find(string token)
            {
                _token = token;
            }

            public string Token => _token;

            public bool Match(IMessage node)
            {
                if (_debug)
                {
                    Log($"Find: Matching token '{_token}' against node type {node.GetType().Name}", true);
                }

                if (_token == "_" || _token == "...")
                {
                    if (_debug) Log($"✓ Wildcard match: {_token}", true);
                    return true;
                }

                // Handle quoted strings in the pattern (both single and double quotes)
                if ((_token.StartsWith("\"") && _token.EndsWith("\"")) || 
                    (_token.StartsWith("'") && _token.EndsWith("'")))
                {
                    var stringValue = _token.Substring(1, _token.Length - 2);
                    if (_debug) Log($"Checking quoted string literal: '{stringValue}'", true);

                    // Check if this is a String node
                    if (node is AST.String stringNode && stringNode.Sval == stringValue)
                    {
                        if (_debug) Log($"✓ String literal match found in String node", true);
                        return true;
                    }
                    
                    // For field patterns, we need to check if this node represents a field value
                    // that should match the quoted string. This is typically used for enum values
                    // and other primitive field values where the pattern has quotes but the 
                    // actual node type represents the value itself.
                    var nodeTypeName = node.GetType().Name;
                    if (nodeTypeName == stringValue)
                    {
                        if (_debug) Log($"✓ Quoted string matches node type: {nodeTypeName}", true);
                        return true;
                    }
                    
                    // Check if the node's ToString() matches the quoted value
                    var nodeStringValue = node.ToString();
                    if (nodeStringValue == stringValue)
                    {
                        if (_debug) Log($"✓ Quoted string matches node string value: {nodeStringValue}", true);
                        return true;
                    }
                }

                // Handle boolean values
                if (_token == "true" || _token == "false")
                {
                    if (_debug) Log($"Checking boolean value: {_token}", true);
                    if (node is AST.Boolean boolNode && boolNode.Boolval.ToString().ToLower() == _token)
                    {
                        if (_debug) Log($"✓ Boolean value match found", true);
                        return true;
                    }
                }

                // Handle integer values
                if (int.TryParse(_token, out var intValue))
                {
                    if (_debug) Log($"Checking integer value: {intValue}", true);
                    if (node is AST.Integer intNode && intNode.Ival == intValue)
                    {
                        if (_debug) Log($"✓ Integer value match found", true);
                        return true;
                    }
                }

                // Handle node type matching
                var nodeType = node.GetType().Name;
                if (nodeType == _token)
                {
                    if (_debug) Log($"✓ Node type match found: {_token}", true);
                    // Add this node to the matching path since it matched
                    _matchingPath.Add(node);
                    return true;
                }

                // Handle field name matching (convert camelCase to snake_case)
                var snakeCaseToken = ConvertToSnakeCase(_token);
                if (nodeType == snakeCaseToken)
                {
                    if (_debug) Log($"✓ Node type match found (snake_case): {snakeCaseToken}", true);
                    // Add this node to the matching path since it matched
                    _matchingPath.Add(node);
                    return true;
                }

                // No special case handling needed - the main pattern matching handles this

                if (_debug) Log($"× No match found for token: {_token}", true);
                return false;
            }
        }

        private class Not : IExpression
        {
            private readonly IExpression _expression;

            public Not(IExpression expression)
            {
                _expression = expression;
            }

            public bool Match(IMessage node)
            {
                if (_debug) Log($"Not: Checking negation of {_expression.GetType().Name}", true);
                var result = !_expression.Match(node);
                if (_debug) Log(result ? "✓ Negation matched" : "× Negation did not match", true);
                if (result)
                {
                    // Add this node to the matching path since it matched
                    _matchingPath.Add(node);
                }
                return result;
            }
        }

        private class All : IExpression
        {
            private readonly IExpression[] _expressions;

            public All(IExpression[] expressions)
            {
                _expressions = expressions;
            }

            public IExpression[] GetExpressions() => _expressions;

            public bool Match(IMessage node)
            {
                if (_debug) Log($"All: Matching {_expressions.Length} expressions", true);
                
                if (_expressions.Length == 0)
                {
                    if (_debug) Log("✓ Empty All pattern matches everything", true);
                    return true;
                }

                // Handle the case where we start with ...
                if (_expressions[0] is Find find && find.Token == "...")
                {
                    if (_debug) Log("Found leading ellipsis, searching globally for remaining patterns", true);
                    // Skip the leading ellipsis and search for the remaining patterns anywhere in the tree
                    var remainingPatterns = _expressions.Skip(1).ToArray();
                    return SearchForPatternsInSubtree(node, remainingPatterns);
                }

                // NEW: Detect field patterns that should search recursively
                // Only apply field pattern detection if we're not already in a subtree search
                if (!_inSubtreeSearch && _expressions.Length >= 1 && _expressions[0] is Find firstFind)
                {
                    if (_debug) Log($"Checking if pattern starting with '{firstFind.Token}' should be treated as field pattern", true);
                    if (IsFieldName(firstFind.Token))
                    {
                        if (_debug) Log("Detected field pattern, searching globally through tree", true);
                        
                        var fieldName = firstFind.Token;
                        var snakeCaseFieldName = ConvertToSnakeCase(fieldName);
                        var fieldPattern = new All(_expressions);
                        
                        // Use the same field-aware search logic as the ellipsis version
                        return SearchForFieldInSubtree(node, fieldName, snakeCaseFieldName, fieldPattern);
                    }
                }

                // Special case: if we have multiple expressions and the first matches the current node type
                // then the remaining expressions might be trying to match field structure
                if (_expressions.Length >= 2 && 
                    _expressions[0] is Find nodeTypeFind)
                {
                    // Check if the first expression matches the current node type
                    if (nodeTypeFind.Match(node))
                    {
                        if (_debug) Log($"✓ Node type '{nodeTypeFind.Token}' matched, now checking field patterns", true);
                        
                        // Now check if we have ellipsis as the second expression
                        if (_expressions.Length >= 3 && 
                            _expressions[1] is Find ellipsisFind && ellipsisFind.Token == "...")
                        {
                            if (_debug) Log("Found ellipsis after node type match, searching for remaining patterns in current node only", true);
                            // Use ellipsis logic to find the remaining patterns in the current node only (not subtree)
                            var remainingPatterns = _expressions.Skip(2).ToArray();
                            return SearchForPatternsInCurrentNode(node, remainingPatterns);
                        }
                        
                        // Now try to match the remaining field patterns against the node's structure
                        var fieldPatterns = _expressions.Skip(1).ToArray();
                        if (MatchFieldPatterns(node, fieldPatterns))
                        {
                            if (_debug) Log("✓ Field patterns matched", true);
                            // Add this node to the matching path since it matched
                            _matchingPath.Add(node);
                            return true;
                        }
                    }
                }

                // Try to match all expressions in sequence (original logic)
                var remainingExpressions = _expressions.ToList();
                var currentNode = node;

                while (remainingExpressions.Any())
                {
                    var expr = remainingExpressions[0];
                    bool found = false;

                    if (_debug) Log($"Trying to match expression {expr.GetType().Name}", true);

                    // Try to match at current level
                    if (expr.Match(currentNode))
                    {
                        remainingExpressions.RemoveAt(0);
                        found = true;
                        if (_debug) Log("✓ Found match at current level", true);
                        continue;
                    }

                    // Try to match in children
                    var descriptor = currentNode.Descriptor;
                    foreach (var field in descriptor.Fields.InDeclarationOrder())
                    {
                        var value = field.Accessor.GetValue(currentNode);
                        
                        if (value == null) continue;

                        if (value is IMessage childMessage)
                        {
                            if (_debug) Log($"Checking message field '{field.Name}'", true);
                            if (expr.Match(childMessage))
                            {
                                remainingExpressions.RemoveAt(0);
                                currentNode = childMessage;
                                found = true;
                                if (_debug) Log($"✓ Found match in field '{field.Name}'", true);
                                break;
                            }
                        }
                        else if (value is IList list)
                        {
                            if (_debug) Log($"Checking list field '{field.Name}'", true);
                            foreach (var item in list)
                            {
                                if (item is IMessage itemMessage && expr.Match(itemMessage))
                                {
                                    remainingExpressions.RemoveAt(0);
                                    currentNode = itemMessage;
                                    found = true;
                                    if (_debug) Log("✓ Found match in list item", true);
                                    break;
                                }
                            }
                            if (found) break;
                        }
                    }

                    if (!found)
                    {
                        if (_debug) Log("× Failed to find match for current expression", true);
                        break;
                    }
                }

                var success = !remainingExpressions.Any();
                if (_debug) Log(success ? "✓ All expressions matched" : "× Not all expressions matched", true);
                return success;
            }

            private bool SearchForPatternsInCurrentNode(IMessage node, IExpression[] patterns)
            {
                if (_debug) Log($"SearchForPatternsInCurrentNode: Looking for {patterns.Length} patterns in current node only", true);
                
                if (patterns.Length == 0)
                {
                    if (_debug) Log("✓ No patterns to search for", true);
                    return true;
                }

                // Check if the first pattern is a field pattern (like (whereClause ...))
                if (patterns[0] is All firstPattern && firstPattern.GetExpressions().Length >= 1 &&
                    firstPattern.GetExpressions()[0] is Find fieldFind)
                {
                    var fieldName = fieldFind.Token;
                    var snakeCaseFieldName = ConvertToSnakeCase(fieldName);
                    
                    if (_debug) Log($"Looking for field pattern '{fieldName}' (snake_case: '{snakeCaseFieldName}') in current node only", true);
                    
                    // Search for this field ONLY in the current node (not children)
                    var descriptor = node.Descriptor;
                    foreach (var field in descriptor.Fields.InDeclarationOrder())
                    {
                        if (field.Name == fieldName || field.Name == snakeCaseFieldName)
                        {
                            if (_debug) Log($"✓ Found field '{field.Name}' in current node", true);
                            var fieldValue = field.Accessor.GetValue(node);
                            
                            // Get the remaining patterns after the field name
                            var remainingPatterns = firstPattern.GetExpressions().Skip(1).ToArray();
                            
                            if (remainingPatterns.Length == 0)
                            {
                                // Just matching field existence - but field must have a value
                                if (fieldValue == null)
                                {
                                    if (_debug) Log($"× Field '{field.Name}' exists but is null, pattern requires value", true);
                                    return false;
                                }
                                
                                if (_debug) Log($"✓ Field '{field.Name}' exists with value, no additional patterns", true);
                                
                                // If there are more patterns after this one, we need to continue searching
                                if (patterns.Length > 1)
                                {
                                    var nextPatterns = patterns.Skip(1).ToArray();
                                    return SearchForPatternsInCurrentNode(node, nextPatterns);
                                }
                                return true;
                            }
                            
                            // Check if field value is null when we have patterns to match
                            if (fieldValue == null)
                            {
                                if (_debug) Log($"× Field '{field.Name}' is null but pattern requires matching against value", true);
                                return false;
                            }
                            
                            // Match the remaining patterns against the field value
                            if (fieldValue is IMessage fieldMessage)
                            {
                                var remainingPattern = new All(remainingPatterns);
                                if (remainingPattern.Match(fieldMessage))
                                {
                                    if (_debug) Log($"✓ Field pattern matched", true);
                                    
                                    // If there are more patterns after this one, we need to continue searching
                                    if (patterns.Length > 1)
                                    {
                                        var nextPatterns = patterns.Skip(1).ToArray();
                                        return SearchForPatternsInCurrentNode(node, nextPatterns);
                                    }
                                    return true;
                                }
                            }
                            else if (fieldValue is IList fieldList)
                            {
                                if (MatchListPatterns(fieldList, remainingPatterns))
                                {
                                    if (_debug) Log($"✓ Field list pattern matched", true);
                                    
                                    // If there are more patterns after this one, we need to continue searching
                                    if (patterns.Length > 1)
                                    {
                                        var nextPatterns = patterns.Skip(1).ToArray();
                                        return SearchForPatternsInCurrentNode(node, nextPatterns);
                                    }
                                    return true;
                                }
                            }
                            else if (remainingPatterns.Length == 1 && remainingPatterns[0] is Find primitiveFind)
                            {
                                // Match primitive value
                                if (MatchPrimitiveValue(fieldValue, primitiveFind.Token))
                                {
                                    if (_debug) Log($"✓ Field primitive pattern matched", true);
                                    
                                    // If there are more patterns after this one, we need to continue searching
                                    if (patterns.Length > 1)
                                    {
                                        var nextPatterns = patterns.Skip(1).ToArray();
                                        return SearchForPatternsInCurrentNode(node, nextPatterns);
                                    }
                                    return true;
                                }
                            }
                        }
                    }
                    
                    // Field not found in current node
                    if (_debug) Log($"× Field '{fieldName}' not found in current node", true);
                    return false;
                }
                else
                {
                    // Try to match all patterns starting from current node using the existing logic
                    if (MatchFieldPatterns(node, patterns))
                    {
                        if (_debug) Log("✓ All patterns matched starting from current node", true);
                        return true;
                    }
                }

                if (_debug) Log("× Patterns not found in current node", true);
                return false;
            }

            private bool IsFieldPattern(IExpression expression)
            {
                if (_debug) Log($"IsFieldPattern: Checking expression of type {expression.GetType().Name}", true);
                
                // Check if this is an All pattern that starts with a field name
                if (expression is All all && all.GetExpressions().Length >= 1 && 
                    all.GetExpressions()[0] is Find find)
                {
                    var token = find.Token;
                    if (_debug) Log($"IsFieldPattern: Found All pattern with token '{token}'", true);
                    
                    return IsFieldName(token);
                }
                else
                {
                    if (_debug) Log("IsFieldPattern: Expression is not an All pattern or doesn't start with Find", true);
                }
                
                return false;
            }

            private bool IsFieldName(string token)
            {
                // Check if this looks like a field name rather than a node type
                // Field names are typically camelCase or snake_case and don't match common node types
                var commonNodeTypes = new HashSet<string>
                {
                    "SelectStmt", "InsertStmt", "UpdateStmt", "DeleteStmt", "CreateStmt",
                    "AlterStmt", "DropStmt", "A_Expr", "ColumnRef", "A_Const", "FuncCall",
                    "JoinExpr", "BoolExpr", "CaseExpr", "ResTarget", "RangeVar", "SubLink",
                    "WindowFunc", "Aggref", "GroupingFunc", "WindowDef", "SortBy", "Node",
                    "List", "String", "Integer", "Float", "BitString", "Null", "Boolean"
                };
                
                // Known field names from PostgreSQL AST
                var knownFieldNames = new HashSet<string>
                {
                    "relname", "schemaname", "whereClause", "targetList", "fromClause", 
                    "groupClause", "havingClause", "sortClause", "limitOffset", "limitCount",
                    "distinctClause", "intoClause", "windowClause", "withClause", "valuesLists",
                    "lockingClause", "op", "all", "larg", "rarg", "relation", "cols", "selectStmt",
                    "returningList", "onConflictClause", "override", "values", "location",
                    "fields", "sval", "ival", "fval", "boolval", "val", "name", "args", "funcname",
                    "agg_order", "agg_distinct", "agg_within_group", "agg_star", "agg_filter",
                    "over", "lexpr", "rexpr", "kind", "quals", "alias", "colnames", "jointype",
                    "isNatural", "lqualname", "rqualname", "using", "boolop", "booltesttype",
                    "nulltesttype", "rowcompare", "opname", "opfamilies", "opcintype", "location"
                };
                
                // If it's a known field name, definitely treat it as a field pattern
                if (knownFieldNames.Contains(token))
                {
                    if (_debug) Log($"'{token}' identified as known field name", true);
                    return true;
                }
                
                // If it's not a common node type and looks like a field name, treat it as a field pattern
                if (!commonNodeTypes.Contains(token) && 
                    (token.Contains("_") || // snake_case
                     (char.IsLower(token[0]) && token.Any(char.IsUpper)) || // camelCase
                     token.All(char.IsLower))) // lowercase
                {
                    if (_debug) Log($"'{token}' identified as field pattern by naming convention", true);
                    return true;
                }
                
                if (_debug) Log($"'{token}' NOT identified as field pattern (is common node type: {commonNodeTypes.Contains(token)})", true);
                return false;
            }

            private bool SearchForPatternsInSubtree(IMessage node, IExpression[] patterns)
            {
                if (_debug) Log($"SearchForPatternsInSubtree: Looking for {patterns.Length} patterns in subtree", true);
                
                // Set the flag to prevent infinite recursion from field pattern detection
                var wasInSubtreeSearch = _inSubtreeSearch;
                _inSubtreeSearch = true;
                
                try
                {
                    if (patterns.Length == 0)
                    {
                        if (_debug) Log("✓ No patterns to search for", true);
                        return true;
                    }

                // Check if the first pattern is a field pattern (like (whereClause ...)) vs a node type pattern (like (CommonTableExpr ...))
                if (patterns[0] is All firstPattern && firstPattern.GetExpressions().Length >= 1 &&
                    firstPattern.GetExpressions()[0] is Find fieldFind)
                {
                    var tokenName = fieldFind.Token;
                    
                    // Check if this looks like a node type (PascalCase) or a field name (camelCase)
                    // Node types typically start with uppercase, field names with lowercase
                    bool looksLikeNodeType = char.IsUpper(tokenName[0]);
                    
                    if (looksLikeNodeType)
                    {
                        if (_debug) Log($"Token '{tokenName}' looks like a node type, treating as node pattern", true);
                        // This is likely a node type pattern like (CommonTableExpr ...), not a field pattern
                        // Try to match the pattern starting from current node using the existing logic
                        if (MatchFieldPatterns(node, patterns))
                        {
                            if (_debug) Log("✓ Node type pattern matched starting from current node", true);
                            return true;
                        }
                        // Also check if we can find a node of this type in the subtree
                        if (FindNodeTypeInSubtree(node, tokenName, firstPattern))
                        {
                            if (_debug) Log($"✓ Found node type '{tokenName}' in subtree", true);
                            return true;
                        }
                    }
                    else
                    {
                        var fieldName = tokenName;
                        var snakeCaseFieldName = ConvertToSnakeCase(fieldName);
                        
                        if (_debug) Log($"Looking for field pattern '{fieldName}' (snake_case: '{snakeCaseFieldName}') in subtree", true);
                        
                        // Search for this field in the current node and all children
                        if (SearchForFieldInSubtree(node, fieldName, snakeCaseFieldName, firstPattern))
                        {
                            if (_debug) Log($"✓ Found field pattern '{fieldName}'", true);
                            
                            // If there are more patterns after this one, we need to continue searching
                            if (patterns.Length > 1)
                            {
                                var remainingPatterns = patterns.Skip(1).ToArray();
                                return SearchForPatternsInSubtree(node, remainingPatterns);
                            }
                            return true;
                        }
                    }
                }
                else
                {
                    // Try to match all patterns starting from current node using the existing logic
                    if (MatchFieldPatterns(node, patterns))
                    {
                        if (_debug) Log("✓ All patterns matched starting from current node", true);
                        return true;
                    }
                }

                // Recursively search in all child nodes
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Searching in message field '{field.Name}'", true);
                        if (SearchForPatternsInSubtree(childMessage, patterns))
                        {
                            if (_debug) Log($"✓ Found patterns in field '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Searching in list field '{field.Name}'", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && SearchForPatternsInSubtree(itemMessage, patterns))
                            {
                                if (_debug) Log($"✓ Found patterns in list item in field '{field.Name}'", true);
                                return true;
                            }
                        }
                    }
                }

                    if (_debug) Log("× Patterns not found in subtree", true);
                    return false;
                }
                finally
                {
                    // Restore the original flag state
                    _inSubtreeSearch = wasInSubtreeSearch;
                }
            }

            private bool SearchForFieldInSubtree(IMessage node, string fieldName, string snakeCaseFieldName, All fieldPattern)
            {
                if (_debug) Log($"SearchForFieldInSubtree: Looking for field '{fieldName}' in {node.GetType().Name}", true);
                
                // Check if this node has the field we're looking for
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    if (field.Name == fieldName || field.Name == snakeCaseFieldName)
                    {
                        if (_debug) Log($"✓ Found field '{field.Name}' in current node", true);
                        var fieldValue = field.Accessor.GetValue(node);
                        
                        // Get the remaining patterns after the field name
                        var remainingPatterns = fieldPattern.GetExpressions().Skip(1).ToArray();
                        
                        if (remainingPatterns.Length == 0)
                        {
                            // Just matching field existence
                            if (_debug) Log($"✓ Field '{field.Name}' exists, no additional patterns", true);
                            return true;
                        }
                        
                        // Match the remaining patterns against the field value
                        if (fieldValue is IMessage fieldMessage)
                        {
                            var remainingPattern = new All(remainingPatterns);
                            if (remainingPattern.Match(fieldMessage))
                            {
                                if (_debug) Log($"✓ Field pattern matched", true);
                                return true;
                            }
                        }
                        else if (fieldValue is IList fieldList)
                        {
                            if (MatchListPatterns(fieldList, remainingPatterns))
                            {
                                if (_debug) Log($"✓ Field list pattern matched", true);
                                return true;
                            }
                        }
                        else if (remainingPatterns.Length == 1 && remainingPatterns[0] is Find primitiveFind)
                        {
                            // Match primitive value
                            if (MatchPrimitiveValue(fieldValue, primitiveFind.Token))
                            {
                                if (_debug) Log($"✓ Field primitive pattern matched", true);
                                return true;
                            }
                        }
                    }
                }

                // Recursively search in child nodes
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (SearchForFieldInSubtree(childMessage, fieldName, snakeCaseFieldName, fieldPattern))
                        {
                            if (_debug) Log($"✓ Found field in child message '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && SearchForFieldInSubtree(itemMessage, fieldName, snakeCaseFieldName, fieldPattern))
                            {
                                if (_debug) Log($"✓ Found field in list item in field '{field.Name}'", true);
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            private bool FindNodeTypeInSubtree(IMessage node, string nodeTypeName, All nodePattern)
            {
                if (_debug) Log($"FindNodeTypeInSubtree: Looking for node type '{nodeTypeName}' in subtree", true);
                
                // Check if current node matches the node type
                if (node.GetType().Name == nodeTypeName)
                {
                    if (_debug) Log($"✓ Found node type '{nodeTypeName}' at current node", true);
                    // Try to match the full pattern against this node
                    if (nodePattern.Match(node))
                    {
                        if (_debug) Log($"✓ Node pattern matched", true);
                        return true;
                    }
                }
                
                // Recursively search in child nodes
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (FindNodeTypeInSubtree(childMessage, nodeTypeName, nodePattern))
                        {
                            if (_debug) Log($"✓ Found node type in child message '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && FindNodeTypeInSubtree(itemMessage, nodeTypeName, nodePattern))
                            {
                                if (_debug) Log($"✓ Found node type in list item in field '{field.Name}'", true);
                                return true;
                            }
                        }
                    }
                }
                
                return false;
            }

            private bool TryMatchAllPatterns(IMessage node, IExpression[] patterns)
            {
                if (_debug) Log($"TryMatchAllPatterns: Trying to match {patterns.Length} patterns at current node", true);
                
                if (patterns.Length == 0)
                {
                    return true;
                }

                // Use the existing field pattern matching logic
                return MatchFieldPatterns(node, patterns);
            }

            private bool MatchFieldPatterns(IMessage node, IExpression[] patterns)
            {
                if (_debug) Log($"MatchFieldPatterns: Matching {patterns.Length} patterns against node type {node.GetType().Name}", true);
                
                if (patterns.Length == 0)
                {
                    if (_debug) Log("✓ No patterns to match", true);
                    return true;
                }

                // Handle wildcards that can match anything
                if (patterns[0] is Find wildcard && (wildcard.Token == "..." || wildcard.Token == "_"))
                {
                    if (_debug) Log($"✓ Wildcard '{wildcard.Token}' matches anything", true);
                    // Continue with remaining patterns
                    if (patterns.Length > 1)
                    {
                        return MatchFieldPatterns(node, patterns.Skip(1).ToArray());
                    }
                    return true;
                }

                // Check if the first pattern is a field name pattern like (targetList ...) or (whereClause ?(A_Expr ...))
                if (patterns[0] is All fieldPattern && fieldPattern.GetExpressions().Length >= 1 && 
                    fieldPattern.GetExpressions()[0] is Find fieldNameFind)
                {
                    var fieldName = fieldNameFind.Token;
                    var snakeCaseFieldName = ConvertToSnakeCase(fieldName);
                    
                    if (_debug) Log($"Looking for field '{fieldName}' (snake_case: '{snakeCaseFieldName}')", true);
                    
                    // Look for the field in the node
                    var descriptor = node.Descriptor;
                    foreach (var field in descriptor.Fields.InDeclarationOrder())
                    {
                        if (field.Name == fieldName || field.Name == snakeCaseFieldName)
                        {
                            if (_debug) Log($"✓ Found field '{field.Name}'", true);
                            var fieldValue = field.Accessor.GetValue(node);
                            
                            // Get the remaining patterns after the field name
                            var remainingPatterns = fieldPattern.GetExpressions().Skip(1).ToArray();
                            
                            if (fieldValue == null)
                            {
                                if (_debug) Log("Field value is null", true);
                                
                                // Check if the pattern allows null (maybe pattern)
                                if (remainingPatterns.Length == 1 && remainingPatterns[0] is Maybe)
                                {
                                    if (_debug) Log("✓ Maybe pattern allows null field", true);
                                    return true;
                                }
                                
                                if (remainingPatterns.Length == 0)
                                {
                                    return true; // Just matching field name
                                }
                                
                                if (_debug) Log("× Field is null but pattern doesn't allow it", true);
                                return false;
                            }
                            
                            if (remainingPatterns.Length == 0)
                            {
                                return true; // Just matching field name
                            }
                            
                            // Check if we have a Maybe pattern
                            if (remainingPatterns.Length == 1 && remainingPatterns[0] is Maybe maybePattern)
                            {
                                if (_debug) Log("Found Maybe pattern, matching inner expression", true);
                                if (fieldValue is IMessage fieldMessage)
                                {
                                    return maybePattern.Match(fieldMessage);
                                }
                                else if (fieldValue is IList fieldList)
                                {
                                    // For lists, we need to check if the maybe pattern matches any item
                                    foreach (var item in fieldList)
                                    {
                                        if (item is IMessage itemMessage && maybePattern.Match(itemMessage))
                                        {
                                            return true;
                                        }
                                    }
                                    return false;
                                }
                                else
                                {
                                    // For primitive values, we can't really apply Maybe pattern
                                    return true;
                                }
                            }
                            
                            // Normal field matching
                            if (fieldValue is IMessage fieldMessage2)
                            {
                                var remainingPattern = new All(remainingPatterns);
                                if (remainingPattern.Match(fieldMessage2))
                                {
                                    // Add the field message to the matching path
                                    _matchingPath.Add(fieldMessage2);
                                    return true;
                                }
                                return false;
                            }
                            else if (fieldValue is IList fieldList2)
                            {
                                if (MatchListPatterns(fieldList2, remainingPatterns))
                                {
                                    return true;
                                }
                                return false;
                            }
                            else
                            {
                                // Primitive field value
                                if (remainingPatterns.Length == 1 && remainingPatterns[0] is Find primitiveFind)
                                {
                                    return MatchPrimitiveValue(fieldValue, primitiveFind.Token);
                                }
                                // Handle Any pattern for primitive values (e.g., {users orders})
                                else if (remainingPatterns.Length == 1 && remainingPatterns[0] is Any anyPattern)
                                {
                                    return MatchPrimitiveValueAny(fieldValue, anyPattern);
                                }
                            }
                        }
                    }
                    
                    // Field not found - check if this is a Maybe pattern
                    var remainingPatterns2 = fieldPattern.GetExpressions().Skip(1).ToArray();
                    if (remainingPatterns2.Length == 1 && remainingPatterns2[0] is Maybe)
                    {
                        if (_debug) Log($"✓ Field '{fieldName}' not found but Maybe pattern allows it", true);
                        return true;
                    }
                    
                    if (_debug) Log($"× Field '{fieldName}' not found", true);
                    return false;
                }

                // Fallback to original logic for non-field patterns
                if (node is IMessage childMessage)
                {
                    // Try to match the patterns against the child message
                    var allPattern = new All(patterns);
                    return allPattern.Match(childMessage);
                }
                else if (node is IList list)
                {
                    // For list fields, check if any of the patterns can match the list structure
                    return MatchListPatterns(list, patterns);
                }
                else
                {
                    // For primitive values, try to match the patterns directly
                    if (patterns.Length == 1 && patterns[0] is Find find)
                    {
                        // Try to match primitive values
                        return MatchPrimitiveValue(node, find.Token);
                    }
                }

                if (_debug) Log("× No match found for value", true);
                return false;
            }

            private bool MatchListPatterns(IList list, IExpression[] patterns)
            {
                if (_debug) Log($"MatchListPatterns: Matching {patterns.Length} patterns against list with {list.Count} items", true);
                
                // For lists, we can either match the wildcard or try to match the pattern against items
                if (patterns.Length > 0 && patterns[0] is Find wildcard && (wildcard.Token == "..." || wildcard.Token == "_"))
                {
                    if (_debug) Log($"✓ Wildcard '{wildcard.Token}' matches list", true);
                    return true;
                }

                // Try to match the pattern against list items
                var allPattern = new All(patterns);
                foreach (var item in list)
                {
                    if (item is IMessage itemMessage && allPattern.Match(itemMessage))
                    {
                        if (_debug) Log("✓ Pattern matched list item", true);
                        // Add the matched list item to the matching path
                        _matchingPath.Add(itemMessage);
                        return true;
                    }
                }
                
                if (_debug) Log("× Pattern did not match any list item", true);
                return false;
            }

            private bool MatchPrimitiveValueAny(object value, Any anyPattern)
            {
                if (_debug) Log($"MatchPrimitiveValueAny: Checking if value {value} matches any of {anyPattern.GetExpressions().Length} expressions", true);
                
                // For each expression in the Any pattern, try to match it against the primitive value
                foreach (var expression in anyPattern.GetExpressions())
                {
                    if (_debug) Log($"Trying expression {expression.GetType().Name}", true);
                    
                    // If it's a Find expression, extract the token and compare it as a primitive
                    if (expression is Find find)
                    {
                        if (MatchPrimitiveValue(value, find.Token))
                        {
                            if (_debug) Log($"✓ Primitive value matched with token '{find.Token}'", true);
                            return true;
                        }
                    }
                    // For other complex expressions, we'd need to handle them differently
                    // For now, we'll focus on Find expressions which are the most common case
                    else
                    {
                        if (_debug) Log($"× Complex expression {expression.GetType().Name} not supported in primitive matching yet", true);
                    }
                }
                
                if (_debug) Log("× No expressions in Any pattern matched the primitive value", true);
                return false;
            }

            private bool MatchPrimitiveValue(object value, string token)
            {
                if (_debug) Log($"MatchPrimitiveValue: Matching '{token}' against {value} (type: {value?.GetType().Name})", true);
                
                if (token == "..." || token == "_")
                {
                    if (_debug) Log($"✓ Wildcard '{token}' matches primitive", true);
                    return true;
                }

                // Handle boolean literals
                if (token == "true" || token == "false")
                {
                    if (value is bool boolValue)
                    {
                        var expectedBool = token == "true";
                        if (boolValue == expectedBool)
                        {
                            if (_debug) Log($"✓ Boolean literal match: {token}", true);
                            return true;
                        }
                    }
                    // Also check string representation for JSON boolean values
                    var boolValueStr = value?.ToString()?.ToLower();
                    if (boolValueStr == token)
                    {
                        if (_debug) Log($"✓ Boolean string match: {token}", true);
                        return true;  
                    }
                }
                
                // Handle integer literals
                if (int.TryParse(token, out var expectedInt))
                {
                    // Check if value is directly an integer
                    if (value is int intValue && intValue == expectedInt)
                    {
                        if (_debug) Log($"✓ Integer match: {token}", true);
                        return true;
                    }
                    // Check if value is a long that matches
                    if (value is long longValue && longValue == expectedInt)
                    {
                        if (_debug) Log($"✓ Long to integer match: {token}", true);
                        return true;
                    }
                    // Check string representation
                    if (int.TryParse(value?.ToString(), out var parsedInt) && parsedInt == expectedInt)
                    {
                        if (_debug) Log($"✓ Integer string match: {token}", true);
                        return true;
                    }
                }
                
                // Handle float/double literals
                if (double.TryParse(token, out var expectedDouble))
                {
                    // Check if value is directly a double
                    if (value is double doubleValue && Math.Abs(doubleValue - expectedDouble) < 0.0001)
                    {
                        if (_debug) Log($"✓ Double match: {token}", true);
                        return true;
                    }
                    // Check if value is a float
                    if (value is float floatValue && Math.Abs(floatValue - expectedDouble) < 0.0001)
                    {
                        if (_debug) Log($"✓ Float match: {token}", true);
                        return true;
                    }
                    // Check string representation
                    if (double.TryParse(value?.ToString(), out var parsedDouble) && Math.Abs(parsedDouble - expectedDouble) < 0.0001)
                    {
                        if (_debug) Log($"✓ Float string match: {token}", true);
                        return true;
                    }
                }

                // Try to match the string representation
                var valueStr = value?.ToString();
                
                // Handle quoted strings in patterns - remove quotes for comparison (both single and double quotes)
                var compareToken = token;
                if ((token.StartsWith("\"") && token.EndsWith("\"")) || 
                    (token.StartsWith("'") && token.EndsWith("'")))
                {
                    compareToken = token.Substring(1, token.Length - 2);
                    if (_debug) Log($"Removed quotes from token: '{compareToken}'", true);
                }
                
                // Direct string match
                if (valueStr == compareToken)
                {
                    if (_debug) Log($"✓ String match: '{compareToken}'", true);
                    return true;
                }
                
                // Handle protobuf enum conversion: SCREAMING_SNAKE_CASE to PascalCase
                // e.g., "AND_EXPR" should match "AndExpr"
                if (compareToken.Contains("_"))
                {
                    var pascalCaseToken = ConvertToPascalCase(compareToken);
                    if (valueStr == pascalCaseToken)
                    {
                        if (_debug) Log($"✓ Enum conversion match: '{compareToken}' -> '{pascalCaseToken}'", true);
                        return true;
                    }
                }
                
                // Handle reverse conversion: PascalCase to SCREAMING_SNAKE_CASE
                // e.g., "AndExpr" should match "AND_EXPR"
                if (token.Length > 0 && char.IsUpper(token[0]) && !token.Contains("_"))
                {
                    var snakeCaseToken = ConvertToSnakeCase(token).ToUpper();
                    if (valueStr == snakeCaseToken)
                    {
                        if (_debug) Log($"✓ Reverse enum conversion match: '{token}' -> '{snakeCaseToken}'", true);
                        return true;
                    }
                }

                if (_debug) Log($"× No match for primitive value", true);
                return false;
            }
            
            private string ConvertToPascalCase(string snakeCase)
            {
                var parts = snakeCase.Split('_');
                var result = "";
                foreach (var part in parts)
                {
                    if (part.Length > 0)
                    {
                        result += char.ToUpper(part[0]) + part.Substring(1).ToLower();
                    }
                }
                return result;
            }

            private bool MatchWithEllipsis(IMessage node, IExpression[] expressions, int currentIndex)
            {
                if (_debug) Log($"MatchWithEllipsis: At index {currentIndex} of {expressions.Length}", true);
                
                if (currentIndex >= expressions.Length)
                {
                    if (_debug) Log("✓ Reached end of expressions", true);
                    return true;
                }
                
                if (expressions[currentIndex] is Find find && find.Token == "...")
                {
                    if (_debug) Log("Found ellipsis, skipping to next expression", true);
                    // Skip ... and try to match the next expression
                    if (currentIndex + 1 >= expressions.Length)
                    {
                        if (_debug) Log("✓ No more expressions after ellipsis", true);
                        return true;
                    }
                    return MatchRecursive(node, expressions[currentIndex + 1], expressions, currentIndex + 1);
                }

                // Try to match the current expression
                if (_debug) Log($"Trying to match expression at index {currentIndex}", true);
                if (expressions[currentIndex].Match(node))
                {
                    if (_debug) Log("✓ Expression matched", true);
                    if (currentIndex + 1 >= expressions.Length)
                    {
                        if (_debug) Log("✓ No more expressions to match", true);
                        return true;
                    }
                    return MatchWithEllipsis(node, expressions, currentIndex + 1);
                }

                // Try to match in children
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Checking message field '{field.Name}'", true);
                        if (MatchWithEllipsis(childMessage, expressions, currentIndex))
                        {
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking list field '{field.Name}'", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && MatchWithEllipsis(itemMessage, expressions, currentIndex))
                            {
                                return true;
                            }
                        }
                    }
                }

                if (_debug) Log("× No match found for ellipsis pattern", true);
                return false;
            }

            private bool MatchRecursive(IMessage node, IExpression expression, IExpression[] expressions, int currentIndex)
            {
                if (_debug) Log($"MatchRecursive: Trying to match expression at depth {_depth}", true);
                
                // Try to match at current level
                if (expression.Match(node))
                {
                    if (_debug) Log("✓ Found match at current level", true);
                    if (currentIndex + 1 >= expressions.Length)
                    {
                        if (_debug) Log("✓ No more expressions to match", true);
                        return true;
                    }
                    return MatchWithEllipsis(node, expressions, currentIndex + 1);
                }

                // Recursively try to match in children
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Checking message field '{field.Name}'", true);
                        if (MatchRecursive(childMessage, expression, expressions, currentIndex))
                        {
                            if (_debug) Log($"✓ Found match in field '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking list field '{field.Name}'", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && MatchRecursive(itemMessage, expression, expressions, currentIndex))
                            {
                                if (_debug) Log("✓ Found match in list item", true);
                                return true;
                            }
                        }
                    }
                }

                if (_debug) Log("× No match found in children", true);
                return false;
            }
        }

        private class Any : IExpression
        {
            private readonly IExpression[] _expressions;

            public Any(IExpression[] expressions)
            {
                _expressions = expressions;
            }

            public IExpression[] GetExpressions() => _expressions;

            public bool Match(IMessage node)
            {
                if (_debug) Log($"Any: Trying to match any of {_expressions.Length} expressions", true);

                if (_expressions.Length == 0)
                {
                    if (_debug) Log("✓ Empty Any pattern matches everything", true);
                    return true;
                }

                // Handle the case where we start with ...
                if (_expressions[0] is Find find && find.Token == "...")
                {
                    if (_debug) Log("Found leading ellipsis, using special matching", true);
                    return MatchWithEllipsis(node, _expressions, 0);
                }

                // Try to match any expression against the current node
                if (_expressions.Any(expr => expr.Match(node)))
                {
                    if (_debug) Log("✓ Found direct match", true);
                    return true;
                }

                // Try to match in children
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Checking message field '{field.Name}'", true);
                        if (_expressions.Any(expr => expr.Match(childMessage)))
                        {
                            if (_debug) Log($"✓ Found match in field '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking list field '{field.Name}'", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && _expressions.Any(expr => expr.Match(itemMessage)))
                            {
                                if (_debug) Log("✓ Found match in list item", true);
                                return true;
                            }
                        }
                    }
                }

                if (_debug) Log("× No matches found for Any pattern", true);
                return false;
            }

            private bool MatchWithEllipsis(IMessage node, IExpression[] expressions, int currentIndex)
            {
                if (_debug) Log($"MatchWithEllipsis: At index {currentIndex} of {expressions.Length}", true);
                
                if (currentIndex >= expressions.Length)
                {
                    if (_debug) Log("✓ Reached end of expressions", true);
                    return true;
                }
                
                if (expressions[currentIndex] is Find find && find.Token == "...")
                {
                    if (_debug) Log("Found ellipsis, skipping to next expression", true);
                    // Skip ... and try to match the next expression
                    if (currentIndex + 1 >= expressions.Length)
                    {
                        if (_debug) Log("✓ No more expressions after ellipsis", true);
                        return true;
                    }
                    return MatchRecursive(node, expressions[currentIndex + 1], expressions, currentIndex + 1);
                }

                // Try to match the current expression
                if (_debug) Log($"Trying to match expression at index {currentIndex}", true);
                if (expressions[currentIndex].Match(node))
                {
                    if (_debug) Log("✓ Expression matched", true);
                    if (currentIndex + 1 >= expressions.Length)
                    {
                        if (_debug) Log("✓ No more expressions to match", true);
                        return true;
                    }
                    return MatchWithEllipsis(node, expressions, currentIndex + 1);
                }

                if (_debug) Log("× Expression did not match", true);
                return false;
            }

            private bool MatchRecursive(IMessage node, IExpression expression, IExpression[] expressions, int currentIndex)
            {
                if (_debug) Log($"MatchRecursive: Trying to match expression at depth {_depth}", true);
                
                // Try to match at current level
                if (expression.Match(node))
                {
                    if (_debug) Log("✓ Found match at current level", true);
                    if (currentIndex + 1 >= expressions.Length)
                    {
                        if (_debug) Log("✓ No more expressions to match", true);
                        return true;
                    }
                    return MatchWithEllipsis(node, expressions, currentIndex + 1);
                }

                // Recursively try to match in children
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Checking message field '{field.Name}'", true);
                        if (MatchRecursive(childMessage, expression, expressions, currentIndex))
                        {
                            if (_debug) Log($"✓ Found match in field '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking list field '{field.Name}'", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && MatchRecursive(itemMessage, expression, expressions, currentIndex))
                            {
                                if (_debug) Log("✓ Found match in list item", true);
                                return true;
                            }
                        }
                    }
                }

                if (_debug) Log("× No match found in children", true);
                return false;
            }
        }

        private class Capture : IExpression
        {
            private readonly string _name;
            private readonly IExpression _expression;

            public Capture(string name, IExpression expression)
            {
                _name = name;
                _expression = expression;
            }

            public bool Match(IMessage node)
            {
                if (_debug) Log($"Capture: Trying to capture '{_name}'", true);
                
                // First try to match the expression
                if (_expression.Match(node))
                {
                    // Store the capture
                    if (!_captures.ContainsKey(_name))
                    {
                        _captures[_name] = new List<IMessage>();
                    }
                    
                    // Extract the actual typed object from Node wrapper if needed
                    var actualNode = ExtractActualNodeFromWrapper(node);
                    
                    // Check if we already have this exact node captured
                    bool alreadyCaptured = _captures[_name].Any(captured => MessageEquals(captured, actualNode));
                    if (!alreadyCaptured)
                    {
                        if (_debug) Log($"Adding new capture for '{_name}' (type: {actualNode.GetType().Name})", true);
                        _captures[_name].Add(actualNode);
                    }
                    else if (_debug)
                    {
                        Log($"Node already captured for '{_name}'", true);
                    }

                    if (_debug) Log($"✓ Successfully captured node for '{_name}'", true);
                    return true;
                }

                // If the expression didn't match, try to match in children
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Checking message field '{field.Name}' for capture", true);
                        if (Match(childMessage))
                        {
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking list field '{field.Name}' for capture", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && Match(itemMessage))
                            {
                                return true;
                            }
                        }
                    }
                }

                if (_debug) Log($"× Failed to capture node for '{_name}'", true);
                return false;
            }
        }

        private class FindWithCapture : IExpression
        {
            private readonly int _index;

            public FindWithCapture(int index)
            {
                _index = index;
            }

            public bool Match(IMessage node)
            {
                if (_debug) Log($"FindWithCapture: Looking for backreference \\{_index}", true);
                
                var indexStr = _index.ToString();
                if (!_captures.ContainsKey(indexStr))
                {
                    if (_debug) Log($"× No capture found for index {_index}", true);
                    return false;
                }

                var captures = _captures[indexStr];
                var result = captures.Any(capture => MessageEquals(capture, node));
                if (_debug) Log(result ? $"✓ Found matching capture for \\{_index}" : $"× No matching capture for \\{_index}", true);
                return result;
            }
        }

        private class Maybe : IExpression
        {
            private readonly IExpression _expression;

            public Maybe(IExpression expression)
            {
                _expression = expression;
            }

            public bool Match(IMessage node)
            {
                if (_debug) Log("Maybe: Checking optional expression", true);
                
                if (node == null)
                {
                    if (_debug) Log("✓ Node is null, Maybe pattern matches", true);
                    return true;
                }

                // For Maybe patterns, if the node exists, try to match the inner expression
                // If it matches, great. If it doesn't match, that's also okay (because it's optional)
                var result = _expression.Match(node);
                if (_debug) Log(result ? "✓ Expression matched" : "✓ Expression didn't match but Maybe pattern allows it", true);
                return true; // Always return true - Maybe means optional
            }
        }



        private class Parent : IExpression
        {
            private readonly IExpression _expression;

            public Parent(IExpression expression)
            {
                _expression = expression;
            }

            public bool Match(IMessage node)
            {
                if (_debug) Log("Parent: Checking for parent match", true);

                // First check if any direct property matches
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Checking message field '{field.Name}'", true);
                        if (_expression.Match(childMessage))
                        {
                            if (_debug) Log($"✓ Found match in field '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking list field '{field.Name}'", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && _expression.Match(itemMessage))
                            {
                                if (_debug) Log("✓ Found match in list item", true);
                                return true;
                            }
                        }
                    }
                }

                // Then recursively check all properties
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Recursively checking object in '{field.Name}'", true);
                        if (Match(childMessage))
                        {
                            if (_debug) Log($"✓ Found match in nested object '{field.Name}'", true);
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking array in '{field.Name}'", true);
                        foreach (var item in list)
                        {
                            if (item is IMessage itemMessage && Match(itemMessage))
                            {
                                if (_debug) Log("✓ Found match in array item", true);
                                return true;
                            }
                        }
                    }
                }

                if (_debug) Log("× No parent match found", true);
                return false;
            }
        }

        private static class ExpressionParser
        {
            public static IExpression Parse(string pattern)
            {
                var tokens = TokenPattern.Matches(pattern)
                    .Cast<Match>()
                    .Select(m => m.Value)
                    .ToList();

                // Debug tokenization for backreferences
                if (_debug && pattern.Contains("\\"))
                {
                    Console.WriteLine($"Pattern: {pattern}");
                    Console.WriteLine($"Tokens: [{string.Join(", ", tokens.Select(t => $"\"{t}\""))}]");
                }

                return ParseExpression(tokens, 0, out _) ?? new Find("_");
            }

            private static IExpression? ParseExpression(List<string> tokens, int startIndex, out int nextIndex)
            {
                if (startIndex >= tokens.Count)
                {
                    nextIndex = startIndex;
                    return null;
                }

                var token = tokens[startIndex];
                nextIndex = startIndex + 1;

                switch (token)
                {
                    case "(":
                        var expressions = new List<IExpression>();
                        while (nextIndex < tokens.Count && tokens[nextIndex] != ")")
                        {
                            var expr = ParseExpression(tokens, nextIndex, out nextIndex);
                            if (expr != null)
                            {
                                expressions.Add(expr);
                            }
                        }
                        nextIndex++; // Skip closing parenthesis
                        return new All(expressions.ToArray());

                    case "[":
                        var allExpressions = new List<IExpression>();
                        while (nextIndex < tokens.Count && tokens[nextIndex] != "]")
                        {
                            var expr = ParseExpression(tokens, nextIndex, out nextIndex);
                            if (expr != null)
                            {
                                allExpressions.Add(expr);
                            }
                        }
                        nextIndex++; // Skip closing bracket
                        return new All(allExpressions.ToArray());

                    case "{":
                        var anyExpressions = new List<IExpression>();
                        while (nextIndex < tokens.Count && tokens[nextIndex] != "}")
                        {
                            var expr = ParseExpression(tokens, nextIndex, out nextIndex);
                            if (expr != null)
                            {
                                anyExpressions.Add(expr);
                            }
                        }
                        nextIndex++; // Skip closing brace
                        return new Any(anyExpressions.ToArray());

                    case "^":
                        var parentExpr = ParseExpression(tokens, nextIndex, out nextIndex);
                        return parentExpr != null ? new Parent(parentExpr) : new Find("_");

                    case "?":
                        var maybeExpr = ParseExpression(tokens, nextIndex, out nextIndex);
                        return maybeExpr != null ? new Maybe(maybeExpr) : new Find("_");

                    case "!":
                        var notExpr = ParseExpression(tokens, nextIndex, out nextIndex);
                        return notExpr != null ? new Not(notExpr) : new Find("_");

                    default:
                        if (token.StartsWith("$"))
                        {
                            var name = token.Substring(1);
                            var nextExpr = ParseExpression(tokens, nextIndex, out nextIndex);
                            return new Capture(name, nextExpr ?? new Find("_"));
                        }
                        else if (token.StartsWith("\\"))
                        {
                            var indexStr = token.Substring(1);
                            if (int.TryParse(indexStr, out var index))
                            {
                                // Validate that this backreference index makes sense
                                if (index < 1)
                                {
                                    throw new FormatException($"Backreference index must be positive: {index}");
                                }
                                return new FindWithCapture(index);
                            }
                            throw new FormatException($"Invalid backreference index: {indexStr}");
                        }
                        else if (token == "...")
                        {
                            return new Find("...");
                        }
                        else if (token == "_")
                        {
                            return new Find("_");
                        }
                        else
                        {
                            return new Find(token);
                        }
                }
            }
        }

        private static bool MessageEquals(IMessage a, IMessage b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.GetType() != b.GetType()) return false;

            var descriptor = a.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var valueA = field.Accessor.GetValue(a);
                var valueB = field.Accessor.GetValue(b);

                if (valueA == null && valueB == null) continue;
                if (valueA == null || valueB == null) return false;

                if (valueA is IMessage messageA && valueB is IMessage messageB)
                {
                    if (!MessageEquals(messageA, messageB)) return false;
                }
                else if (valueA is IList listA && valueB is IList listB)
                {
                    if (listA.Count != listB.Count) return false;
                    for (int i = 0; i < listA.Count; i++)
                    {
                        if (listA[i] is IMessage itemA && listB[i] is IMessage itemB)
                        {
                            if (!MessageEquals(itemA, itemB)) return false;
                        }
                        else if (!Equals(listA[i], listB[i]))
                        {
                            return false;
                        }
                    }
                }
                else if (!Equals(valueA, valueB))
                {
                    return false;
                }
            }

            return true;
        }

        // Helper method for testing and debugging
        public static (bool Success, string Details) MatchWithDetails(string pattern, string sql, bool debug = false, bool verbose = false)
        {
            try
            {
                _captures.Clear();
                _debug = debug;
                _verbose = verbose;
                _depth = 0;

                var sqlAst = PgQuery.Parse(sql);
                var expression = ExpressionParser.Parse(pattern);
                
                var details = new System.Text.StringBuilder();
                details.AppendLine("Pattern:");
                details.AppendLine(pattern);
                details.AppendLine("\nSQL Query:");
                details.AppendLine(sql);
                
                var success = MatchRecursive(sqlAst.ParseTree.Stmts[0].Stmt, expression);
                
                if (success)
                {
                    details.AppendLine("\nCaptures:");
                    foreach (var capture in _captures)
                    {
                        details.AppendLine($"{capture.Key}:");
                        foreach (var value in capture.Value)
                        {
                            details.AppendLine(value.ToString());
                        }
                    }
                }
                
                return (success, details.ToString());
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Helper method to get captured nodes
        public static IReadOnlyDictionary<string, List<IMessage>> GetCaptures()
        {
            return _captures;
        }

        // Helper method to get matching path
        public static HashSet<IMessage> GetMatchingPath()
        {
            return new HashSet<IMessage>(_matchingPath);
        }

        // Helper method to clear captures
        public static void ClearCaptures()
        {
            _captures.Clear();
        }

        // Helper method to enable/disable debug logging
        public static void SetDebug(bool enable)
        {
            _debug = enable;
        }

        private static IMessage ExtractActualNodeFromWrapper(IMessage node)
        {
            // If this is a Node wrapper, extract the actual typed object
            if (node is AST.Node astNode)
            {
                switch (astNode.NodeCase)
                {
                    case AST.Node.NodeOneofCase.AExpr:
                        return astNode.AExpr;
                    case AST.Node.NodeOneofCase.SelectStmt:
                        return astNode.SelectStmt;
                    case AST.Node.NodeOneofCase.InsertStmt:
                        return astNode.InsertStmt;
                    case AST.Node.NodeOneofCase.UpdateStmt:
                        return astNode.UpdateStmt;
                    case AST.Node.NodeOneofCase.DeleteStmt:
                        return astNode.DeleteStmt;
                    case AST.Node.NodeOneofCase.RangeVar:
                        return astNode.RangeVar;
                    case AST.Node.NodeOneofCase.ColumnRef:
                        return astNode.ColumnRef;
                    case AST.Node.NodeOneofCase.ResTarget:
                        return astNode.ResTarget;
                    case AST.Node.NodeOneofCase.AConst:
                        return astNode.AConst;
                    case AST.Node.NodeOneofCase.FuncCall:
                        return astNode.FuncCall;
                    case AST.Node.NodeOneofCase.JoinExpr:
                        return astNode.JoinExpr;
                    case AST.Node.NodeOneofCase.BoolExpr:
                        return astNode.BoolExpr;
                    case AST.Node.NodeOneofCase.CaseExpr:
                        return astNode.CaseExpr;
                    case AST.Node.NodeOneofCase.SubLink:
                        return astNode.SubLink;
                    case AST.Node.NodeOneofCase.TypeCast:
                        return astNode.TypeCast;
                    case AST.Node.NodeOneofCase.FromExpr:
                        return astNode.FromExpr;
                    // Add more cases as needed
                    default:
                        // If we don't have a specific case, return the wrapper
                        return node;
                }
            }
            
            // If it's not a Node wrapper, return as-is
            return node;
        }

        private static string ConvertToSnakeCase(string camelCase)
        {
            if (string.IsNullOrEmpty(camelCase))
                return camelCase;

            var result = new System.Text.StringBuilder();
            result.Append(char.ToLower(camelCase[0]));

            for (int i = 1; i < camelCase.Length; i++)
            {
                if (char.IsUpper(camelCase[i]))
                {
                    result.Append('_');
                    result.Append(char.ToLower(camelCase[i]));
                }
                else
                {
                    result.Append(camelCase[i]);
                }
            }

            return result.ToString();
        }

        public static ParseResult? ParseSql(string sql)
        {
            try
            {
                return PgQuery.Parse(sql);
            }
            catch
            {
                return null;
            }
        }
    }
} 