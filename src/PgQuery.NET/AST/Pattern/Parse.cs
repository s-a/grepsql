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
                    return ParseUntil(tokens, ")");
                case "{":
                    return new Any(ParseUntil(tokens, "}"));
                case "[":
                    return new All(ParseUntil(tokens, "]"));
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
                    return new HasChildren();
                default:
                    if (token.StartsWith("\"") && token.EndsWith("\""))
                        return new Find(token.Substring(1, token.Length - 2));
                    if (token.StartsWith("'") && token.EndsWith("'"))
                        return new Find(token.Substring(1, token.Length - 2)); // remove quotes
                    return new Find(token);
            }
        }

        private static Find ParseUntil(List<string> tokens, string endToken)
        {
            var expressions = new List<Find>();
            
            while (tokens.Count > 0 && tokens[0] != endToken)
            {
                expressions.Add(ParseExpression(tokens));
            }
            
            if (tokens.Count == 0)
                throw new ArgumentException($"Expected '{endToken}' but reached end of pattern");
                
            tokens.RemoveAt(0); // consume end token
            
            // Return single expression or compound
            if (expressions.Count == 0)
                return new Something(); // empty group
            if (expressions.Count == 1)
                return expressions[0];
            
            // Special case: (fieldname $_) should be treated as a field capture
            if (expressions.Count == 2 && 
                expressions[0] is Find fieldFind && 
                expressions[1] is Capture capture &&
                fieldFind.GetNodeType() != null &&
                IsKnownAttribute(fieldFind.GetNodeType()))
            {
                // Create a special field capture that captures the field value
                return new FieldCapture(capture._name, fieldFind.GetNodeType());
            }
            
            // Create a compound expression with multiple conditions
            var compound = new Something();
            compound.Conditions.AddRange(expressions);
            return compound;
        }

        private static bool IsKnownAttribute(string name)
        {
            return !string.IsNullOrEmpty(name) && SQL.Postgres.AttributeNames.Contains(name);
        }
    }
}