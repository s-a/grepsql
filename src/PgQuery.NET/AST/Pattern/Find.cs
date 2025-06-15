using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using PgQuery.NET.AST;

namespace PgQuery.NET.AST.Pattern
{
    /// <summary>
    /// Base class for all pattern matching expressions.
    /// Implements Ruby Fast-style pattern matching with unified Find-based hierarchy.
    /// Contains the core search and matching logic that all patterns inherit.
    /// </summary>
    public class Find
    {
        public List<Find> Conditions { get; } = new List<Find>();
        public Dictionary<string, List<IMessage>> Captures { get; } = new Dictionary<string, List<IMessage>>();

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
            
            if (debug)
            {
                Console.WriteLine($"Found {results.Count} matches for pattern: {this}");
            }
            
            return results;
        }

        /// <summary>
        /// Search for nodes and return captures.
        /// </summary>
        /// <param name="rootNode">Root node to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>Dictionary of captured nodes by name</returns>
        public Dictionary<string, List<IMessage>> SearchWithCaptures(IMessage rootNode, bool debug = false)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            var results = new List<IMessage>();
            SearchRecursive(rootNode, results, debug);
            
            // Extract captures from this expression
            var captures = GetCaptures();
            
            if (debug)
            {
                Console.WriteLine($"Found {results.Count} matches with {captures.Count} capture groups for pattern: {this}");
            }
            
            return captures;
        }

        private void SearchRecursive(IMessage node, List<IMessage> results, bool debug)
        {
            if (node == null) return;

            // Check if current node matches
            if (Match(node))
            {
                results.Add(node);
                if (debug)
                {
                    Console.WriteLine($"Match found: {node.GetType().Name}");
                }
            }

            // Search children using protobuf reflection
            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                
                if (value == null) continue;
                
                // Handle single IMessage fields
                if (value is IMessage childMessage)
                {
                    SearchRecursive(childMessage, results, debug);
                }
                // Handle repeated fields (collections)
                else if (value is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is IMessage itemMessage)
                        {
                            SearchRecursive(itemMessage, results, debug);
                        }
                    }
                }
            }
        }

        public virtual bool Match(IMessage node)
        {
            return MatchCondition(node) && MatchConditions(node);
        }

        public virtual bool MatchCondition(IMessage node)
        {
            if (!string.IsNullOrEmpty(_nodeType))
            {
                // Check if this is a field name (attribute) rather than a node type
                if (IsKnownAttribute(_nodeType))
                {
                    return HasField(node, _nodeType);
                }
                else
                {
                    return node?.GetType().Name == _nodeType;
                }
            }
            return true; // Base implementation always matches
        }

        /// <summary>
        /// Get the match result for capture purposes. For field patterns, returns the field value.
        /// For other patterns, returns the node itself.
        /// </summary>
        public virtual object GetMatchResult(IMessage node)
        {
            if (!string.IsNullOrEmpty(_nodeType) && IsKnownAttribute(_nodeType))
            {
                // For field patterns, return the field value
                return GetFieldValue(node, _nodeType);
            }
            // For other patterns, return the node itself
            return node;
        }

        private bool IsKnownAttribute(string name)
        {
            return SQL.Postgres.AttributeNames.Contains(name);
        }

        protected bool HasField(IMessage node, string fieldName)
        {
            if (node == null) return false;
            
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

        private bool IsEmptyValue(object value)
        {
            if (value is string str) return string.IsNullOrEmpty(str);
            if (value is System.Collections.ICollection collection) return collection.Count == 0;
            return false;
        }

        public bool IsFieldPattern(out string fieldName)
        {
            fieldName = "";
            if (!string.IsNullOrEmpty(_nodeType) && IsKnownAttribute(_nodeType))
            {
                fieldName = _nodeType;
                return true;
            }
            return false;
        }

        public string GetNodeType()
        {
            return _nodeType;
        }

        protected object GetFieldValue(IMessage node, string fieldName)
        {
            if (node == null) return null;
            
            var descriptor = node.Descriptor;
            var field = descriptor.Fields.InDeclarationOrder()
                .FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            
            return field?.Accessor.GetValue(node);
        }

        protected virtual bool MatchConditions(IMessage node)
        {
            // OR logic: any condition can match
            return Conditions.Count == 0 || Conditions.Any(condition => condition.Match(node));
        }

        public virtual Dictionary<string, List<IMessage>> GetCaptures()
        {
            var result = new Dictionary<string, List<IMessage>>(Captures);
            
            // Merge captures from conditions
            foreach (var condition in Conditions)
            {
                var conditionCaptures = condition.GetCaptures();
                foreach (var kvp in conditionCaptures)
                {
                    if (result.ContainsKey(kvp.Key))
                    {
                        result[kvp.Key].AddRange(kvp.Value);
                    }
                    else
                    {
                        result[kvp.Key] = new List<IMessage>(kvp.Value);
                    }
                }
            }
            
            return result;
        }

        public override string ToString()
        {
            var result = GetType().Name.Replace("Finder", "").Replace("Find", "");
            
            // If this is a base Find with a nodeType, show the nodeType
            if (string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(_nodeType))
            {
                result = _nodeType;
            }
            
            // For base Find class with conditions, show as compound
            if (GetType() == typeof(Find) && Conditions.Count > 0)
            {
                return $"Find({string.Join(", ", Conditions.Select(c => c.ToString()))})";
            }
            
            // For other classes, only add conditions if they have them
            if (Conditions.Count > 0 && GetType() != typeof(Something))
            {
                result += $"({string.Join(", ", Conditions.Select(c => c.ToString()))})";
            }
            return result;
        }
    }


    /// <summary>
    /// Matches any node (wildcard pattern "_").
    /// </summary>
    public class Something : Find
    {
        public override bool MatchCondition(IMessage node)
        {
            return node != null;
        }

        public override string ToString()
        {
            // For Something with conditions, show as compound
            if (Conditions.Count > 0)
            {
                return $"Find({string.Join(", ", Conditions.Select(c => c.ToString()))})";
            }
            return "Something";
        }
    }

    /// <summary>
    /// Matches nodes that have children (ellipsis pattern "...").
    /// </summary>
    public class HasChildren : Find
    {
        public override bool MatchCondition(IMessage node)
        {
            if (node == null) return false;
            if (node is Node astNode)
            {
                return astNode.GetSmartChildren().Any();
            }
            var descriptor = node.Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(node);
                if (value != null)
                    return true;
            }
            return false;
        }

        public override string ToString()
        {
            return "HasChildren(" + base.ToString() + ")";
        }
    }

    /// <summary>
    /// Captures field values from nodes that have a specific field.
    /// </summary>
    public class FieldCapture : Find
    {
        private readonly string _captureName;
        private readonly string _fieldName;

        public FieldCapture(string captureName, string fieldName)
        {
            _captureName = captureName ?? throw new ArgumentNullException(nameof(captureName));
            _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        }

        public override bool Match(IMessage node)
        {
            if (HasField(node, _fieldName))
            {
                if (!Captures.ContainsKey(_captureName))
                    Captures[_captureName] = new List<IMessage>();
                
                // Capture the field value directly
                var fieldValue = GetFieldValue(node, _fieldName);
                
                if (fieldValue != null)
                {
                    if (fieldValue is IMessage fieldMessage)
                    {
                        Captures[_captureName].Add(fieldMessage);
                    }
                    else
                    {
                        // For non-IMessage values (like strings), wrap them
                        Captures[_captureName].Add(new FieldValueWrapper(fieldValue));
                    }
                }
                return true;
            }
            return false;
        }

        public override bool MatchCondition(IMessage node)
        {
            return HasField(node, _fieldName);
        }

        public override string ToString()
        {
            var count = Captures.ContainsKey(_captureName) ? Captures[_captureName].Count : 0;
            return $"FieldCapture({_captureName}, {_fieldName}, {count})";
        }
    }

    /// <summary>
    /// Captures matching nodes with a given name.
    /// </summary>
    public class Capture : Find
    {
        public readonly string _name;
        private readonly Find _innerExpression;

        public Capture(string name, Find innerExpression)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _innerExpression = innerExpression ?? throw new ArgumentNullException(nameof(innerExpression));
        }

        public override bool Match(IMessage node)
        {
            if (_innerExpression.Match(node))
            {
                if (!Captures.ContainsKey(_name))
                    Captures[_name] = new List<IMessage>();
                
                // Capture the actual match result, following Ruby Fast approach
                var capturedValue = GetCaptureValue(node);
                Captures[_name].Add(capturedValue);
                return true;
            }
            return false;
        }

        private IMessage GetCaptureValue(IMessage node)
        {
            // Recursively search for field patterns that match this node
            var fieldValue = FindMatchingFieldValue(_innerExpression, node);
            if (fieldValue != null && fieldValue != node)
            {
                // Found a field value, wrap it if needed
                if (fieldValue is IMessage fieldMessage)
                {
                    return fieldMessage;
                }
                else
                {
                    return new FieldValueWrapper(fieldValue);
                }
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

        public override string ToString()
        {
            var count = Captures.ContainsKey(_name) ? Captures[_name].Count : 0;
            return $"Capture({_name}, {_innerExpression.ToString()}, {count})";
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

        public override string ToString()
        {
            return "Parent(" + base.ToString() + ")";
        }
    }

    /// <summary>
    /// Matches any condition (Ruby Fast "{}" pattern).
    /// </summary>
    public class Any : Find
    {
        public Any() : base() { }
        
        public Any(Find condition) : base()
        {
            Conditions.Add(condition);
        }

        public override bool MatchCondition(IMessage node)
        {
            return true; // Always matches
        }

        public override string ToString()
        {
            return "Any(" + string.Join(", ", Conditions.Select(c => c.ToString())) + ")";
        }
    }

    /// <summary>
    /// Matches when all conditions are met (Ruby Fast "All" pattern).
    /// </summary>
    public class All : Find
    {
        public All() : base() { }
        
        public All(Find condition) : base()
        {
            Conditions.Add(condition);
        }

        protected override bool MatchConditions(IMessage node)
        {
            // AND logic: all conditions must match
            return Conditions.Count == 0 || Conditions.All(condition => condition.Match(node));
        }

        public override string ToString()
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

        public override string ToString()
        {
            return "Not(" + base.ToString() + ")";
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

        public override string ToString()
        {
            return "Maybe(" + base.ToString() + ")";
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

        public int CalculateSize() => 0;
        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }
        public void WriteTo(Google.Protobuf.CodedOutputStream output) { }
        public IMessage Clone() => new FieldValueWrapper(_value);
        public bool Equals(IMessage other) => other is FieldValueWrapper wrapper && Equals(_value, wrapper._value);

        public override string ToString() => _value?.ToString() ?? "";
        public override bool Equals(object obj) => obj is FieldValueWrapper wrapper && Equals(_value, wrapper._value);
        public override int GetHashCode() => _value?.GetHashCode() ?? 0;
    }
} 