using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using PgQuery.NET.AST;

namespace PgQuery.NET.AST.Pattern
{
    /// <summary>
    /// Strategy interface for different node matching approaches.
    /// </summary>
    public interface IMatchingStrategy
    {
        bool Match(IMessage node, string? nodeType);
    }

    /// <summary>
    /// Matches nodes by their type name.
    /// </summary>
    public class NodeTypeMatchingStrategy : IMatchingStrategy
    {
        public bool Match(IMessage node, string? nodeType)
        {
            return string.IsNullOrEmpty(nodeType) || node?.GetType().Name == nodeType;
        }
    }

    /// <summary>
    /// Matches nodes by checking if they have a specific field.
    /// </summary>
    public class FieldMatchingStrategy : IMatchingStrategy
    {
        public bool Match(IMessage node, string? fieldName)
        {
            return !string.IsNullOrEmpty(fieldName) && FieldAccessor.HasField(node, fieldName);
        }
    }

    /// <summary>
    /// Factory for creating appropriate matching strategies.
    /// </summary>
    public static class MatchingStrategyFactory
    {
        private static readonly IMatchingStrategy NodeTypeStrategy = new NodeTypeMatchingStrategy();
        private static readonly IMatchingStrategy FieldStrategy = new FieldMatchingStrategy();

        public static IMatchingStrategy GetStrategy(string? nodeType)
        {
            if (string.IsNullOrEmpty(nodeType))
                return NodeTypeStrategy;
                
            return SQL.Postgres.IsKnownAttribute(nodeType) 
                ? FieldStrategy 
                : NodeTypeStrategy;
        }
    }

    /// <summary>
    /// Utility class for accessing protobuf message fields in a consistent way.
    /// </summary>
    public static class FieldAccessor
    {
        public static bool HasField(IMessage node, string fieldName)
        {
            if (node == null) return false;
            
            // FieldValueWrapper doesn't have protobuf descriptor
            if (node is FieldValueWrapper)
                return false;
            
            var descriptor = node.Descriptor;
            var field = descriptor.Fields.InDeclarationOrder()
                .FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            
            if (field != null)
            {
                var value = field.Accessor.GetValue(node);
                return value != null && !IsEmptyValue(value);
            }
            
            return false;
        }

        public static object? GetFieldValue(IMessage node, string fieldName)
        {
            if (node == null) return null;
            
            var descriptor = node.Descriptor;
            var field = descriptor.Fields.InDeclarationOrder()
                .FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            
            return field?.Accessor.GetValue(node);
        }

        private static bool IsEmptyValue(object value)
        {
            if (value is string str) return string.IsNullOrEmpty(str);
            if (value is System.Collections.ICollection collection) return collection.Count == 0;
            return false;
        }
    }

    /// <summary>
    /// Iterator for protobuf message fields that handles both single and repeated fields.
    /// </summary>
    public static class ProtobufNodeIterator
    {
        public delegate void NodeAction(IMessage node);
        public delegate bool NodePredicate(IMessage node);

        /// <summary>
        /// Iterate through all child nodes of a protobuf message.
        /// </summary>
        public static void ForEachChild(IMessage node, NodeAction action)
        {
            if (node == null) return;

            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                
                if (value == null) continue;
                
                // Handle single IMessage fields
                if (value is IMessage childMessage)
                {
                    action(childMessage);
                }
                // Handle repeated fields (collections)
                else if (value is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is IMessage itemMessage)
                        {
                            action(itemMessage);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Find first child node that matches the predicate.
        /// </summary>
        public static IMessage? FindChild(IMessage node, NodePredicate predicate)
        {
            if (node == null) return null;

            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                
                if (value == null) continue;
                
                // Handle single IMessage fields
                if (value is IMessage childMessage)
                {
                    if (predicate(childMessage))
                        return childMessage;
                }
                // Handle repeated fields (collections)
                else if (value is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is IMessage itemMessage && predicate(itemMessage))
                        {
                            return itemMessage;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Check if any child node matches the predicate.
        /// </summary>
        public static bool AnyChild(IMessage node, NodePredicate predicate)
        {
            return FindChild(node, predicate) != null;
        }

        /// <summary>
        /// Get all child nodes that match the predicate.
        /// </summary>
        public static List<IMessage> GetChildren(IMessage node, NodePredicate predicate)
        {
            var results = new List<IMessage>();
            if (node == null) return results;

            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                
                if (value == null) continue;
                
                // Handle single IMessage fields
                if (value is IMessage childMessage)
                {
                    if (predicate(childMessage))
                        results.Add(childMessage);
                }
                // Handle repeated fields (collections)
                else if (value is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is IMessage itemMessage && predicate(itemMessage))
                        {
                            results.Add(itemMessage);
                        }
                    }
                }
            }
            return results;
        }
    }

    /// <summary>
    /// Base class for all pattern matching expressions.
    /// Implements Ruby Fast-style pattern matching with unified Find-based hierarchy.
    /// Contains the core search and matching logic that all patterns inherit.
    /// </summary>
    public class Find
    {
        public List<Find> Conditions { get; } = new List<Find>();
        public List<object> Captures { get; } = new List<object>();

        protected readonly string? _nodeType;
        
        protected Find() { }
        
        public Find(params Find[] conditions)
        {
            Conditions.AddRange(conditions);
        }
        
        public Find(string nodeType)
        {
            _nodeType = nodeType;
        }

        public Find(string nodeType, Find condition)
        {
            _nodeType = nodeType;
            Conditions.Add(condition);
        }

        public Find(string nodeType, List<Find> conditions)
        {
            _nodeType = nodeType;
            Conditions.AddRange(conditions);
        }

        /// <summary>
        /// Search for nodes matching this pattern in the AST.
        /// </summary>
        /// <param name="rootNode">Root node to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public List<IMessage> Search(IMessage rootNode, bool debug = false)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            var results = new List<IMessage>();
            SearchRecursive(rootNode, results, debug);
            
            Pattern.DebugLogger.Instance.Log($"Found {results.Count} matches for pattern: {this}");
            
            return results;
        }

        /// <summary>
        /// Search for nodes and return captures.
        /// </summary>
        /// <param name="rootNode">Root node to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of captured objects</returns>
        public List<object> SearchWithCaptures(IMessage rootNode, bool debug = false)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            var results = new List<IMessage>();
            SearchRecursive(rootNode, results, debug);
            
            // Extract captures from this expression
            var captures = GetCaptures();
            
            Pattern.DebugLogger.Instance.Log($"Found {results.Count} matches with {captures.Count} captures for pattern: {this}");
            
            return captures;
        }

        protected void SearchRecursive(IMessage node, List<IMessage> results, bool debug)
        {
            if (node == null) return;

            // Check if current node matches - this will trigger captures
            if (Match(node))
            {
                results.Add(node);
                Pattern.DebugLogger.Instance.Log($"Match found: {node.GetType().Name}");
            }

            // Search children using the iterator utility
            ProtobufNodeIterator.ForEachChild(node, child => SearchRecursive(child, results, debug));
        }

        public virtual bool Match(IMessage node)
        {
            // First check the base condition (nodeType or default)
            if (!MatchCondition(node))
                return false;
            
            // Then check all additional conditions (AND logic for compound patterns)
            if (Conditions.Count > 0)
            {
                return MatchConditions(node);
            }
            
            return true;
        }

        public virtual bool MatchCondition(IMessage node)
        {
            var strategy = MatchingStrategyFactory.GetStrategy(_nodeType);
            return strategy.Match(node, _nodeType);
        }

        /// <summary>
        /// Get the match result for capture purposes. For field patterns, returns the field value.
        /// For other patterns, returns the node itself.
        /// </summary>
        public virtual object GetMatchResult(IMessage node)
        {
            if (!string.IsNullOrEmpty(_nodeType) && SQL.Postgres.IsKnownAttribute(_nodeType))
            {
                // For field patterns, return the field value
                return FieldAccessor.GetFieldValue(node, _nodeType) ?? node;
            }
            // For other patterns, return the node itself
            return node;
        }

        public bool IsFieldPattern(out string fieldName)
        {
            fieldName = "";
            if (!string.IsNullOrEmpty(_nodeType) && SQL.Postgres.IsKnownAttribute(_nodeType))
            {
                fieldName = _nodeType;
                return true;
            }
            return false;
        }

        public string? GetNodeType()
        {
            return _nodeType;
        }

        protected virtual bool MatchConditions(IMessage node)
        {
            // For compound Find patterns, use AND logic - all conditions must match
            return Conditions.All(condition => condition.Match(node));
        }

        /// <summary>
        /// Search for any matching condition in children. Used by HasChildren and similar patterns.
        /// </summary>
        protected bool FindInChildren(IMessage node, Find condition)
        {
            return SearchChildrenRecursive(node, condition);
        }

        /// <summary>
        /// Recursively search children nodes for matches.
        /// </summary>
        protected bool SearchChildrenRecursive(IMessage node, Find condition)
        {
            if (node == null) return false;

            // Use the iterator utility to search children
            return ProtobufNodeIterator.AnyChild(node, child =>
            {
                // Check if condition matches this child
                if (condition.Match(child))
                    return true;
                
                // Recursively search deeper children
                return SearchChildrenRecursive(child, condition);
            });
        }

        /// <summary>
        /// Propagate captures from matching conditions to this expression.
        /// </summary>
        protected void PropagateCaptures(IEnumerable<Find> matchingConditions)
        {
            foreach (var condition in matchingConditions)
            {
                var conditionCaptures = condition.GetCaptures();
                Captures.AddRange(conditionCaptures);
            }
        }

        public virtual List<object> GetCaptures()
        {
            var result = new List<object>(Captures);
            
            // Merge captures from conditions
            foreach (var condition in Conditions)
            {
                var conditionCaptures = condition.GetCaptures();
                result.AddRange(conditionCaptures);
            }
            
            return result;
        }

        public override string ToString()
        {
            return FormatAsString();
        }

        /// <summary>
        /// Template method for formatting pattern as string. Can be overridden for custom formatting.
        /// </summary>
        protected virtual string FormatAsString()
        {
            // If this is a base Find with a nodeType, show the nodeType
            if (!string.IsNullOrEmpty(_nodeType))
            {
                if (Conditions.Count > 0)
                {
                    return $"Find({_nodeType}, {string.Join(", ", Conditions.Select(c => c.ToString()))})";
                }
                return $"Find({_nodeType})";
            }
            
            // For base Find class with conditions but no nodeType, show as compound
            if (GetType() == typeof(Find) && Conditions.Count > 0)
            {
                return $"Find({string.Join(", ", Conditions.Select(c => c.ToString()))})";
            }
            
            return "Find";
        }
    }

    /// <summary>
    /// Matches any node (wildcard pattern "_").
    /// </summary>
    public class Something : Find
    {
        public Something() : base() { }
        
        public Something(Find condition) : base()
        {
            if (condition != null)
                throw new ArgumentException("Something cannot have a condition");
        }
        
        public override bool MatchCondition(IMessage node)
        {
            return node != null;
        }
        
        protected override string FormatAsString()
        {
            return "_";
        }
    }

    /// <summary>
    /// Matches nodes that have children (ellipsis pattern "...").
    /// </summary>
    public class HasChildren : Find
    {
        public HasChildren() : base() { }
        
        public HasChildren(Find condition) : base()
        {
            Conditions.Add(condition);
        }

        public override bool MatchCondition(IMessage node)
        {
            if (node == null) return false;
            
            // If no conditions, just check if node has any children
            if (Conditions.Count == 0)
            {
                if (node is Node astNode)
                {
                    return astNode.GetSmartChildren().Any();
                }
                // Use the iterator utility to check for children
                return ProtobufNodeIterator.AnyChild(node, _ => true);
            }
            
            // Search through children to find matches for any condition (OR logic for ellipsis)
            return Conditions.Any(condition => FindInChildren(node, condition));
        }
        
        public override bool Match(IMessage node)
        {
            // For HasChildren, all matching logic is handled in MatchCondition
            // which searches through children. Don't call MatchConditions.
            return MatchCondition(node);
        }

        protected override string FormatAsString()
        {
            return "HasChildren(" + string.Join(", ", Conditions.Select(c => c.ToString())) + ")";
        }
    }

    /// <summary>
    /// Captures matching nodes.
    /// </summary>
    public class Capture : Find
    {
        private readonly Find _innerExpression;

        public Capture(Find innerExpression)
        {
            _innerExpression = innerExpression ?? throw new ArgumentNullException(nameof(innerExpression));
        }

        public override bool Match(IMessage node)
        {
            if (_innerExpression.Match(node))
            {
                // Capture the actual match result, following Ruby Fast approach
                var capturedValue = GetCaptureValue(node);
                Captures.Add(capturedValue);
                return true;
            }
            return false;
        }

        private object GetCaptureValue(IMessage node)
        {
            // Recursively search for field patterns that match this node
            var fieldValue = FindMatchingFieldValue(_innerExpression, node);
            if (fieldValue != null && fieldValue != node)
            {
                // Found a field value, return it directly
                return fieldValue;
            }
            
            // Default: capture the node itself
            return node;
        }

        private object FindMatchingFieldValue(Find expression, IMessage node)
        {
            // Check if this expression is a field pattern that matches
            if (expression.Match(node))
            {
                var result = expression.GetMatchResult(node);
                if (result != node && result != null)
                {
                    return result;
                }
            }

            // Recursively check conditions
            foreach (var condition in expression.Conditions)
            {
                var fieldValue = FindMatchingFieldValue(condition, node);
                if (fieldValue != null && fieldValue != node)
                {
                    return fieldValue;
                }
            }

            return node; // No field value found
        }

        public override bool MatchCondition(IMessage node)
        {
            return _innerExpression.MatchCondition(node);
        }

        protected override string FormatAsString()
        {
            var count = Captures.Count;
            return $"Capture({_innerExpression.ToString()}, {count})";
        }
    }

    /// <summary>
    /// Matches parent nodes (Ruby Fast "^" pattern).
    /// </summary>
    public class Parent : Find
    {
        public Parent() : base() { }
        
        public Parent(Find condition) : base()
        {
            Conditions.Add(condition);
        }

        public override bool MatchCondition(IMessage node)
        {
            // For now, just return true as a placeholder
            // In a full implementation, this would check if the node is a parent
            return true;
        }

        protected override string FormatAsString()
        {
            return "Parent(" + base.FormatAsString() + ")";
        }
    }

    /// <summary>
    /// Matches any condition (Ruby Fast "{}" pattern).
    /// </summary>
    public class Any : Find
    {
        public Any() : base() { }
        
        public Any(params Find[] conditions) : base()
        {
            Conditions.AddRange(conditions);
        }

        public Any(List<Find> conditions) : base()
        {
            Conditions.AddRange(conditions);
        }

        public override bool MatchCondition(IMessage node)
        {
            return true; // Always matches
        }

        protected override bool MatchConditions(IMessage node)
        {
            // OR logic: any condition can match (this is the key difference from base Find)
            var matchingConditions = Conditions.Where(condition => condition.Match(node)).ToList();
            
            if (matchingConditions.Any())
            {
                // Propagate captures from all matching conditions
                PropagateCaptures(matchingConditions);
                return true;
            }
            
            return false;
        }

        protected override string FormatAsString()
        {
            if (Conditions.Count > 0)
            {
                return $"Any({string.Join(", ", Conditions.Select(c => c.ToString()))})";
            }
            return "Any";
        }
    }

    /// <summary>
    /// Matches when all conditions are met (Ruby Fast "All" pattern).
    /// </summary>
    public class All : Find
    {
        public All() : base() { }
        
        public All(List<Find> conditions) : base()
        {
            Conditions.AddRange(conditions);
        }
        
        public All(params Find[] conditions) : base()
        {
            Conditions.AddRange(conditions);
        }

        protected override bool MatchConditions(IMessage node)
        {
            // AND logic: all conditions must match
            if (Conditions.Count == 0) return true;
            
            var matchingConditions = new List<Find>();
            var allMatched = true;
            
            foreach (var condition in Conditions)
            {
                if (condition.Match(node))
                {
                    matchingConditions.Add(condition);
                }
                else
                {
                    allMatched = false;
                }
            }
            
            // Only propagate captures if all conditions matched
            if (allMatched)
            {
                PropagateCaptures(matchingConditions);
            }
            
            return allMatched;
        }

        protected override string FormatAsString()
        {
            return "All";
        }
    }

    /// <summary>
    /// Matches when condition is NOT met (Ruby Fast "!" pattern).
    /// </summary>
    public class Not : Find
    {
        public Not() : base() { }
        
        public Not(Find condition) : base()
        {
            Conditions.Add(condition);
        }

        public override bool MatchCondition(IMessage node)
        {
            return !base.MatchCondition(node);
        }

        protected override string FormatAsString()
        {
            return "Not(" + base.FormatAsString() + ")";
        }
    }

    /// <summary>
    /// Optional match (Ruby Fast "?" pattern).
    /// If the node is null, it will match.
    /// If the node is not null, it will match if the base condition matches.
    /// </summary>
    public class Maybe : Find
    {
        public Maybe() : base() { }
        
        public Maybe(Find condition) : base()
        {
            Conditions.Add(condition);
        }

        public override bool MatchCondition(IMessage node)
        {
            return node != null && base.MatchCondition(node);
        }

        protected override string FormatAsString()
        {
            return "Maybe(" + base.FormatAsString() + ")";
        }
    }
    class Literal : Find
    {
        private readonly string _value;
        public Literal(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }   
        public override bool MatchCondition(IMessage node)
        {
            if (node is FieldValueWrapper wrapper)
            {
                return string.Equals(wrapper.ToString(), _value, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        protected override string FormatAsString()
        {
            return $"\"{_value}\"";
        }
    }

    class MatchAttribute : Find
    {
        private readonly string _fieldName;
        private readonly Find _innerExpression;
        
        public MatchAttribute(string fieldName, Find innerExpression)
        {
            _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            _innerExpression = innerExpression ?? throw new ArgumentNullException(nameof(innerExpression));
        }
        
        public override bool MatchCondition(IMessage node)
        {
            if (node == null) return false;
            
            // Check if the node has this field
            if (!FieldAccessor.HasField(node, _fieldName)) return false;
            
            // Get field value and check if inner expression matches
            var fieldValue = FieldAccessor.GetFieldValue(node, _fieldName);
            if (fieldValue == null) return false;
            
            // For string values, create a wrapper to match against
            IMessage valueToMatch = fieldValue is IMessage fieldMessage 
                ? fieldMessage 
                : new FieldValueWrapper(fieldValue);
            
            // Clear inner expression captures to avoid accumulation
            _innerExpression.Captures.Clear();
            
            // Call match and propagate captures
            bool matches = _innerExpression.Match(valueToMatch);
            
            // Propagate captures using the utility method
            if (matches)
            {
                PropagateCaptures(new[] { _innerExpression });
            }
            
            return matches;
        }
        
        public override object GetMatchResult(IMessage node)
        {
            // For MatchAttribute, return the field value itself for captures
            var fieldValue = FieldAccessor.GetFieldValue(node, _fieldName);
            if (fieldValue != null)
            {
                return fieldValue;
            }
            return node;
        }
        
        protected override string FormatAsString()
        {
            return $"MatchAttribute({_fieldName}, {_innerExpression.ToString()})";
        }
    }

    /// <summary>
    /// Wrapper for non-IMessage field values to make them compatible with capture system.
    /// </summary>
    public class FieldValueWrapper : IMessage
    {
        private readonly object _value;

        public FieldValueWrapper(object value)
        {
            _value = value;
        }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => 
            throw new NotSupportedException("FieldValueWrapper does not have a descriptor");

        public int CalculateSize() {
            if (_value is string str) {
                return str.Length;
            }
            return 0;
        }
        public void MergeFrom(CodedInputStream input) { }
        public void WriteTo(CodedOutputStream output) { }
        public IMessage Clone() => new FieldValueWrapper(_value);
        public bool Equals(IMessage other) => other is FieldValueWrapper wrapper && Equals(_value, wrapper._value);

        public override string ToString() => _value?.ToString() ?? "";
        public override bool Equals(object? obj) => obj is FieldValueWrapper wrapper && Equals(_value, wrapper._value);
        public override int GetHashCode() => _value?.GetHashCode() ?? 0;
    }
} 