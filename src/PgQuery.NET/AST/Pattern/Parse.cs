using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using PgQuery.NET.AST;

namespace PgQuery.NET.AST.Pattern
{
    /// <summary>
    /// Global debug logger singleton for pattern matching operations.
    /// </summary>
    public sealed class DebugLogger
    {
        private static readonly Lazy<DebugLogger> _instance = new Lazy<DebugLogger>(() => new DebugLogger());
        private bool _isEnabled = false;

        private DebugLogger() { }

        public static DebugLogger Instance => _instance.Value;

        public void Enable() => _isEnabled = true;
        public void Disable() => _isEnabled = false;
        public bool IsEnabled => _isEnabled;

        public void Log(string message)
        {
            if (_isEnabled)
            {
                Console.WriteLine(message);
            }
        }

        public void Log(string format, params object[] args)
        {
            if (_isEnabled)
            {
                Console.WriteLine(format, args);
            }
        }
    }

    /// <summary>
    /// AST Pattern Matcher with Ruby Fast-style pattern matching capabilities.
    /// Supports tokenizer-based parsing with unified Find-based class hierarchy.
    /// </summary>
    public static class Match
    {
        public static readonly Regex TokenizerRegex = new Regex(@"
            ""[^""]*""       # double quoted strings
            |
            '[^']*'         # single quoted strings
            |
            \.{3}           # ellipsis ...
            |
            [\w_]+          # identifiers
            |
            [\(\)\{\}\[\]]  # brackets and parens
            |
            [\^\!\?\$]      # special characters
            |
            _               # wildcard
        ", RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

        /// <summary>
        /// Search for nodes matching the given pattern in the AST.
        /// </summary>
        /// <param name="rootNode">Root node to search in</param>
        /// <param name="pattern">Pattern to match (Ruby Fast syntax)</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public static List<IMessage> Search(IMessage rootNode, string pattern, bool debug = false)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));
            if (string.IsNullOrEmpty(pattern)) throw new ArgumentException("Pattern cannot be null or empty", nameof(pattern));

            var originalDebugState = DebugLogger.Instance.IsEnabled;
            if (debug) DebugLogger.Instance.Enable();

            try
            {
                var expression = ParsePattern(pattern);
                return expression.Search(rootNode, debug);
            }
            catch (Exception ex)
            {
                if (debug)
                {
                    Console.WriteLine($"Error searching pattern '{pattern}': {ex.Message}");
                }
                throw;
            }
            finally
            {
                if (!originalDebugState) DebugLogger.Instance.Disable();
            }
        }

        /// <summary>
        /// Search for nodes and return captures.
        /// </summary>
        /// <param name="rootNode">Root node to search in</param>
        /// <param name="pattern">Pattern with capture groups</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>Dictionary of captured nodes by name</returns>
        public static Dictionary<string, List<IMessage>> SearchWithCaptures(IMessage rootNode, string pattern, bool debug = false)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));
            if (string.IsNullOrEmpty(pattern)) throw new ArgumentException("Pattern cannot be null or empty", nameof(pattern));

            var originalDebugState = DebugLogger.Instance.IsEnabled;
            if (debug) DebugLogger.Instance.Enable();

            try
            {
                var expression = ParsePattern(pattern);
                return expression.SearchWithCaptures(rootNode, debug);
            }
            catch (Exception ex)
            {
                if (debug)
                {
                    Console.WriteLine($"Error searching pattern '{pattern}': {ex.Message}");
                }
                throw;
            }
            finally
            {
                if (!originalDebugState) DebugLogger.Instance.Disable();
            }
        }

        /// <summary>
        /// Parse a pattern string into an expression tree.
        /// </summary>
        /// <param name="pattern">Pattern to parse</param>
        /// <returns>Root expression</returns>
        public static Find ParsePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Pattern cannot be null or empty", nameof(pattern));

            var tokens = TokenizerRegex.Matches(pattern).Select(m => m.Value).ToList();
            DebugLogger.Instance.Log($"Tokens: {string.Join(", ", tokens)}");
            return ParseExpression(tokens);
        }

        /// <summary>
        /// Get expression tree as string for debugging.
        /// </summary>
        /// <param name="pattern">Pattern to parse</param>
        /// <returns>Expression tree representation</returns>
        public static string GetExpressionTree(string pattern)
        {
            try
            {
                var expression = ParsePattern(pattern);
                return expression.ToString();
            }
            catch (Exception ex)
            {
                return $"Error parsing pattern: {ex.Message}";
            }
        }

        private static Find ParseExpression(List<string> tokens)
        {
            if (tokens.Count == 0)
                throw new ArgumentException("Unexpected end of pattern");
                
            var token = tokens[0];
            tokens.RemoveAt(0); // consume the token
            
            switch (token)
            {
                case "(":
                    return ParseUntil(tokens, ")", false);
                case "{":
                    return new Any(ParseMultipleUntil(tokens, "}"));
                case "[":
                    return new All(ParseMultipleUntil(tokens, "]"));
                case "^":
                    return new Parent(ParseExpression(tokens));
                case "!":
                    return new Not(ParseExpression(tokens));
                case "?":
                    return new Maybe(ParseExpression(tokens));
                case "$":
                    return new Capture("default", ParseExpression(tokens));
                case "_":
                    return new Something();
                case "...":
                    // Check if there are more tokens to parse as condition
                    if (tokens.Count > 0)
                    {
                        return new HasChildren(ParseExpression(tokens));
                    }
                    else
                    {
                        // Ellipsis without condition means "has any children"
                        return new HasChildren();
                    }
                default:
                    return ParseIdentifierOrLiteral(token, tokens);
            }
        }

        /// <summary>
        /// Parse an identifier token which could be a quoted literal, attribute, node type, or unknown identifier.
        /// </summary>
        /// <param name="token">The token to parse</param>
        /// <param name="tokens">Remaining tokens for lookahead</param>
        /// <returns>Appropriate Find expression</returns>
        private static Find ParseIdentifierOrLiteral(string token, List<string> tokens)
        {
            // Handle quoted literals
            if (token.StartsWith("\"") && token.EndsWith("\""))
                return new Literal(token.Substring(1, token.Length - 2));
            if (token.StartsWith("'") && token.EndsWith("'"))
                return new Literal(token.Substring(1, token.Length - 2));
            
            // Check if this is a known attribute name
            if (IsKnownAttribute(token))
            {
                return ParseAttributeExpression(token, tokens);
            }
            
            // Check if this is a known node type
            if (SQL.Postgres.NodeTypeNames.Contains(token)) 
            {
                return new Find(token);
            }
            
            // Default to literal for unknown identifiers
            return new Literal(token);
        }

        /// <summary>
        /// Parse an attribute expression with optional value pattern.
        /// </summary>
        /// <param name="attributeName">The attribute name</param>
        /// <param name="tokens">Remaining tokens for lookahead</param>
        /// <returns>MatchAttribute expression</returns>
        private static Find ParseAttributeExpression(string attributeName, List<string> tokens)
        {
            // Look ahead to see if there's a value pattern following
            if (tokens.Count > 0)
            {
                var nextToken = tokens[0];
                // Check for complex patterns, quoted strings, or simple identifiers
                if (ShouldParseAsValuePattern(nextToken))
                {
                    var valuePattern = ParseExpression(tokens);
                    return new MatchAttribute(attributeName, valuePattern);
                }
            }
            
            // No value pattern following, just return the attribute matcher with a wildcard
            return new MatchAttribute(attributeName, new Something());
        }

        /// <summary>
        /// Determine if the next token should be parsed as a value pattern for an attribute.
        /// </summary>
        /// <param name="nextToken">The next token to examine</param>
        /// <returns>True if it should be parsed as a value pattern</returns>
        private static bool ShouldParseAsValuePattern(string nextToken)
        {
            // Complex patterns
            if (nextToken == "{" || nextToken == "(") return true;
            
            // Quoted strings
            if (nextToken.StartsWith("\"") || nextToken.StartsWith("'")) return true;
            
            // Not a closing delimiter (which would end the current expression)
            return !nextToken.Equals(")", StringComparison.Ordinal) && 
                   !nextToken.Equals("}", StringComparison.Ordinal) && 
                   !nextToken.Equals("]", StringComparison.Ordinal);
        }

        private static Find ParseUntil(List<string> tokens, string endToken, bool createAnyPattern = false)
        {
            var expressions = new List<Find>();
            
            while (tokens.Count > 0 && tokens[0] != endToken)
            {
                expressions.Add(ParseExpression(tokens));
            }
            
            if (tokens.Count == 0)
                throw new ArgumentException($"Expected '{endToken}' but reached end of pattern");
                
            tokens.RemoveAt(0); // consume end token
            
            DebugLogger.Instance.Log($"Expressions: {string.Join(", ", expressions.Select(e => e.ToString()))}");
            // Return single expression or compound
            if (expressions.Count == 0)
                return new Something(); // empty group
            if (expressions.Count == 1)
                return expressions[0];
            
            return new Find(expressions.ToArray());
        }

        private static List<Find> ParseMultipleUntil(List<string> tokens, string endToken)
        {
            var expressions = new List<Find>();
            
            while (tokens.Count > 0 && tokens[0] != endToken)
            {
                var token = tokens[0];
                
                // For known attributes in Any/All patterns, treat each as standalone
                if (IsKnownAttribute(token))
                {
                    tokens.RemoveAt(0); // consume the token
                    expressions.Add(new MatchAttribute(token, new Something()));
                }
                else
                {
                    expressions.Add(ParseExpression(tokens));
                }
            }
            
            if (tokens.Count == 0)
                throw new ArgumentException($"Expected '{endToken}' but reached end of pattern");
                
            tokens.RemoveAt(0); // consume end token
            
            DebugLogger.Instance.Log($"Expressions: {string.Join(", ", expressions.Select(e => e.ToString()))}");
            return expressions;
        }

        private static bool IsKnownAttribute(string name)
        {
            return !string.IsNullOrEmpty(name) && SQL.Postgres.AttributeNames.Contains(name);
        }
    }
}