using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace PgQuery.NET.AST.Visitors
{
    /// <summary>
    /// Visitor that implements pattern matching functionality
    /// </summary>
    public class PatternMatchVisitor : NodeVisitor<bool>
    {
        private readonly IPattern _pattern;
        private readonly Dictionary<string, List<IMessage>> _captures;
        private readonly bool _debug;

        public PatternMatchVisitor(IPattern pattern, Dictionary<string, List<IMessage>> captures, bool debug = false)
        {
            _pattern = pattern;
            _captures = captures;
            _debug = debug;
        }

        private void Log(string message, bool isRoot = false)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }

        public override bool Visit(SelectStmt node)
        {
            if (_pattern is SelectPattern selectPattern)
            {
                return selectPattern.Match(node);
            }
            return TryMatchPattern(node);
        }

        public override bool Visit(FromExpr node)
        {
            return TryMatchPattern(node);
        }

        public override bool Visit(RangeVar node)
        {
            return TryMatchPattern(node);
        }

        public override bool Visit(A_Expr node)
        {
            return TryMatchPattern(node);
        }

        public override bool Visit(ColumnRef node)
        {
            if (_pattern is ColumnPattern columnPattern)
            {
                return columnPattern.Match(node);
            }
            return TryMatchPattern(node);
        }

        public override bool Visit(ResTarget node)
        {
            return TryMatchPattern(node);
        }

        public override bool Visit(A_Const node)
        {
            return TryMatchPattern(node);
        }

        public override bool Visit(TypeCast node)
        {
            return TryMatchPattern(node);
        }

        public override bool Visit(FuncCall node)
        {
            return TryMatchPattern(node);
        }

        public override bool Visit(JoinExpr node)
        {
            if (_pattern is JoinPattern joinPattern)
            {
                return joinPattern.Match(node);
            }
            return TryMatchPattern(node);
        }

        private bool TryMatchPattern(IMessage node)
        {
            // First try to match at current level
            if (_pattern.Match(node))
            {
                if (_debug) Log($"✓ Found match at {node.GetType().Name}");
                return true;
            }

            // Then try to match in children
            if (node is Node astNode)
            {
                // Handle the oneof field based on NodeCase
                switch (astNode.NodeCase)
                {
                    case Node.NodeOneofCase.SelectStmt:
                        if (Visit(astNode.SelectStmt)) return true;
                        break;
                    case Node.NodeOneofCase.FromExpr:
                        if (Visit(astNode.FromExpr)) return true;
                        break;
                    case Node.NodeOneofCase.RangeVar:
                        if (Visit(astNode.RangeVar)) return true;
                        break;
                    case Node.NodeOneofCase.AExpr:
                        if (Visit(astNode.AExpr)) return true;
                        break;
                    case Node.NodeOneofCase.ColumnRef:
                        if (Visit(astNode.ColumnRef)) return true;
                        break;
                    case Node.NodeOneofCase.ResTarget:
                        if (Visit(astNode.ResTarget)) return true;
                        break;
                    case Node.NodeOneofCase.AConst:
                        if (Visit(astNode.AConst)) return true;
                        break;
                    case Node.NodeOneofCase.TypeCast:
                        if (Visit(astNode.TypeCast)) return true;
                        break;
                    case Node.NodeOneofCase.FuncCall:
                        if (Visit(astNode.FuncCall)) return true;
                        break;
                    case Node.NodeOneofCase.JoinExpr:
                        if (Visit(astNode.JoinExpr)) return true;
                        break;
                }
            }
            else
            {
                // Handle other Protocol Buffers message types
                var descriptor = node.Descriptor;
                foreach (var field in descriptor.Fields.InDeclarationOrder())
                {
                    var value = field.Accessor.GetValue(node);
                    
                    if (value == null) continue;

                    if (value is Node childNode)
                    {
                        if (_debug) Log($"Checking node field '{field.Name}'");
                        if (Visit(childNode))
                        {
                            return true;
                        }
                    }
                    else if (value is RepeatedField<Node> nodeList)
                    {
                        if (_debug) Log($"Checking node list field '{field.Name}'");
                        if (_pattern.Match(nodeList))
                        {
                            return true;
                        }
                        foreach (var item in nodeList)
                        {
                            if (Visit(item))
                            {
                                return true;
                            }
                        }
                    }
                    else if (value is IMessage childMessage)
                    {
                        if (_debug) Log($"Checking message field '{field.Name}'");
                        if (_pattern.Match(childMessage))
                        {
                            return true;
                        }
                    }
                    else if (value is IList list)
                    {
                        if (_debug) Log($"Checking list field '{field.Name}'");
                        foreach (var item in list)
                        {
                            if (item is Node nodeItem)
                            {
                                if (Visit(nodeItem))
                                {
                                    return true;
                                }
                            }
                            else if (item is IMessage messageItem)
                            {
                                if (_pattern.Match(messageItem))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Base interface for pattern matching
    /// </summary>
    public interface IPattern
    {
        bool Match(IMessage node);
        bool Match(RepeatedField<Node> nodes);
    }

    /// <summary>
    /// Base pattern class with default implementations
    /// </summary>
    public abstract class BasePattern : IPattern
    {
        public virtual bool Match(IMessage node) => false;
        public virtual bool Match(RepeatedField<Node> nodes) => false;
    }

    /// <summary>
    /// Pattern for matching repeated fields
    /// </summary>
    public class RepeatedPattern : BasePattern
    {
        private readonly IPattern _itemPattern;
        private readonly bool _matchAll;

        public RepeatedPattern(IPattern itemPattern, bool matchAll = false)
        {
            _itemPattern = itemPattern;
            _matchAll = matchAll;
        }

        public override bool Match(RepeatedField<Node> nodes)
        {
            if (_matchAll)
            {
                return nodes.Count > 0 && nodes.ToList().All(node => _itemPattern.Match(node));
            }
            return nodes.ToList().Any(node => _itemPattern.Match(node));
        }
    }

    /// <summary>
    /// Pattern for matching SELECT statements
    /// </summary>
    public class SelectPattern : BasePattern
    {
        private readonly IPattern? _targetList;
        private readonly IPattern? _fromClause;
        private readonly IPattern? _whereClause;

        public SelectPattern(IPattern? targetList = null, IPattern? fromClause = null, IPattern? whereClause = null)
        {
            _targetList = targetList;
            _fromClause = fromClause;
            _whereClause = whereClause;
        }

        public override bool Match(IMessage node)
        {
            if (node is not SelectStmt select) return false;

            if (_targetList != null && !_targetList.Match(select.TargetList))
                return false;

            if (_fromClause != null && !_fromClause.Match(select.FromClause))
                return false;

            if (_whereClause != null && !_whereClause.Match(select.WhereClause))
                return false;

            return true;
        }
    }

    /// <summary>
    /// Pattern for matching column references
    /// </summary>
    public class ColumnPattern : BasePattern
    {
        private readonly string? _name;

        public ColumnPattern(string? name = null)
        {
            _name = name;
        }

        public override bool Match(IMessage node)
        {
            if (node is not ColumnRef column) return false;

            if (_name != null)
            {
                foreach (var field in column.Fields)
                {
                    if (field.NodeCase == Node.NodeOneofCase.String && field.String.Sval == _name)
                    {
                        return true;
                    }
                }
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Pattern for matching JOIN expressions
    /// </summary>
    public class JoinPattern : BasePattern
    {
        private readonly IPattern? _left;
        private readonly IPattern? _right;
        private readonly JoinType? _type;

        public JoinPattern(IPattern? left = null, IPattern? right = null, JoinType? type = null)
        {
            _left = left;
            _right = right;
            _type = type;
        }

        public override bool Match(IMessage node)
        {
            if (node is not JoinExpr join) return false;

            if (_left != null && !_left.Match(join.Larg))
                return false;

            if (_right != null && !_right.Match(join.Rarg))
                return false;

            if (_type.HasValue && join.Jointype != _type.Value)
                return false;

            return true;
        }
    }
} 