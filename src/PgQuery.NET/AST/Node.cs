using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;

namespace PgQuery.NET.AST
{
    public partial class Node
    {
        private List<IMessage>? _children;
        private Node? _parent;

        /// <summary>
        /// The parent node, if any.
        /// </summary>
        public Node? Parent => _parent;

        /// <summary>
        /// Whether this node has no children.
        /// </summary>
        public bool IsLeaf => !HasChildren;

        /// <summary>
        /// Whether this node has children (Ruby Fast equivalent of "...").
        /// </summary>
        public bool HasChildren => GetSmartChildren().Any();

        /// <summary>
        /// Check if this node is not null (Ruby Fast equivalent of "_").
        /// </summary>
        public bool IsSomething => true;

        /// <summary>
        /// Check if this node is null (Ruby Fast equivalent of "nil").
        /// </summary>
        public bool IsNil => false;

        /// <summary>
        /// Get the node type as a symbol-like string for pattern matching.
        /// </summary>
        public string TypeSymbol => GetType().Name.ToLowerInvariant();

        /// <summary>
        /// Gets all child nodes of this node using Protocol Buffers reflection.
        /// This includes both regular child nodes AND attributes as "virtual children".
        /// </summary>
        public IEnumerable<Node> GetChildren()
        {
            return GetSmartChildren().OfType<Node>();
        }

        /// <summary>
        /// Smart children method that exposes both child nodes and attributes as "Node children".
        /// This replaces the need for AttributeFinder by making attributes accessible as children.
        /// </summary>
        public IEnumerable<IMessage> GetSmartChildren()
        {
            if (_children == null)
            {
                _children = ExtractSmartChildren().ToList();
            }
            return _children;
        }

        /// <summary>
        /// All child nodes of this node (cached).
        /// </summary>
        public IEnumerable<IMessage> Children => GetSmartChildren();

        /// <summary>
        /// Get all descendant nodes (depth-first traversal).
        /// </summary>
        /// <returns>All descendant nodes</returns>
        public IEnumerable<Node> Descendants()
        {
            foreach (var child in Children.OfType<Node>())
            {
                yield return child;
                foreach (var descendant in child.Descendants())
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// Get all ancestor nodes up to the root.
        /// </summary>
        /// <returns>All ancestor nodes</returns>
        public IEnumerable<Node> Ancestors()
        {
            var current = _parent;
            while (current != null)
            {
                yield return current;
                current = current._parent;
            }
        }

        /// <summary>
        /// Check if this node matches a specific type.
        /// </summary>
        /// <param name="nodeType">The node type to check</param>
        /// <returns>True if the node type matches</returns>
        public bool IsType(string nodeType)
        {
            return GetType().Name.Equals(nodeType, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get the value of a specific field if it exists.
        /// </summary>
        /// <param name="fieldName">Name of the field</param>
        /// <returns>Field value or null</returns>
        public object? GetFieldValue(string fieldName)
        {
            var descriptor = Descriptor;
            if (descriptor == null) return null;

            var field = descriptor.Fields.InFieldNumberOrder()
                .FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            return field?.Accessor.GetValue(this);
        }

        /// <summary>
        /// Get a detailed string representation including field values.
        /// </summary>
        /// <returns>Detailed string representation</returns>
        public string ToDetailedString()
        {
            var descriptor = Descriptor;
            if (descriptor == null) return GetType().Name;

            var fields = new List<string>();
            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                var value = field.Accessor.GetValue(this);
                if (value != null && !IsChildMessage(value))
                {
                    fields.Add($"{field.Name}={value}");
                }
            }

            return fields.Any() ? $"{GetType().Name}({string.Join(", ", fields)})" : GetType().Name;
        }

        /// <summary>
        /// Create a tree representation of this node and its descendants.
        /// </summary>
        /// <param name="maxDepth">Maximum depth to traverse</param>
        /// <returns>Tree representation as string</returns>
        public string ToTreeString(int maxDepth = 10)
        {
            return ToTreeStringInternal(0, maxDepth, "");
        }

        private string ToTreeStringInternal(int currentDepth, int maxDepth, string prefix)
        {
            if (currentDepth > maxDepth) return prefix + "...\n";

            var result = prefix + ToDetailedString() + "\n";
            
            var children = Children.ToList();
            for (int i = 0; i < children.Count; i++)
            {
                var isLast = i == children.Count - 1;
                var childPrefix = prefix + (isLast ? "└── " : "├── ");
                var nextPrefix = prefix + (isLast ? "    " : "│   ");
                
                if (children[i] is Node childNode)
                {
                    result += childNode.ToTreeStringInternal(currentDepth + 1, maxDepth, childPrefix);
                }
                else if (children[i] is AttributeNode attrNode)
                {
                    result += childPrefix + attrNode.ToString() + "\n";
                }
            }

            return result;
        }

        /// <summary>
        /// Extract smart children that include both child nodes and attributes as virtual children.
        /// This replaces AttributeFinder functionality by making attributes accessible as children.
        /// </summary>
        private IEnumerable<IMessage> ExtractSmartChildren()
        {
            var descriptor = Descriptor;
            if (descriptor == null) yield break;

            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                var value = field.Accessor.GetValue(this);
                
                if (value == null) continue;

                // Handle repeated fields (lists)
                if (field.IsRepeated && value is System.Collections.IList list)
                {
                    foreach (var item in list)
                    {
                        if (item is IMessage childMessage)
                        {
                            // Set parent if it's our custom Node type
                            if (item is Node childNode)
                            {
                                childNode._parent = this;
                            }
                            yield return childMessage;
                        }
                    }
                }
                // Handle single child messages (including protobuf-generated classes)
                else if (value is IMessage childMessage)
                {
                    // Set parent if it's our custom Node type
                    if (value is Node childNode)
                    {
                        childNode._parent = this;
                    }
                    yield return childMessage;
                }
                // Handle attributes as virtual children - create AttributeNode wrapper
                else if (!IsChildMessage(value))
                {
                    // Create a virtual attribute node
                    var attributeNode = new AttributeNode(field.Name, value, this);
                    yield return attributeNode;
                }
            }
        }

        /// <summary>
        /// Check if a value represents a child message (vs a primitive value).
        /// </summary>
        private static bool IsChildMessage(object value)
        {
            return value is IMessage || 
                   (value is System.Collections.IList list && 
                    list.Count > 0 && 
                    list[0] is IMessage);
        }
    }

    /// <summary>
    /// Virtual node that represents an attribute as a child node.
    /// This allows attributes to be treated as children in pattern matching.
    /// </summary>
    public class AttributeNode : IMessage
    {
        private readonly string _attributeName;
        private readonly object _attributeValue;
        private readonly Node _parentNode;

        public string AttributeName => _attributeName;
        public object AttributeValue => _attributeValue;

        public AttributeNode(string attributeName, object attributeValue, Node parent)
        {
            _attributeName = attributeName;
            _attributeValue = attributeValue;
            _parentNode = parent;
        }

        public override string ToString()
        {
            return $"{_attributeName}={_attributeValue}";
        }

        // IMessage implementation
        public MessageDescriptor Descriptor => null!; // Virtual node has no descriptor
        public int CalculateSize() => 0;
        public void MergeFrom(IMessage other) => throw new NotImplementedException("AttributeNode is a virtual node");
        public void MergeFrom(CodedInputStream input) => throw new NotImplementedException("AttributeNode is a virtual node");
        public void WriteTo(CodedOutputStream output) => throw new NotImplementedException("AttributeNode is a virtual node");
        public IMessage Clone() => new AttributeNode(_attributeName, _attributeValue, _parentNode);
        public bool Equals(IMessage other) => other is AttributeNode attr && attr._attributeName == _attributeName && Equals(attr._attributeValue, _attributeValue);
    }
} 