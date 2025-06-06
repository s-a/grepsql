using System;

namespace PgQuery.NET.AST
{
    /// <summary>
    /// Base implementation of AST node visitor
    /// </summary>
    public abstract class NodeVisitor : INodeVisitor
    {
        public virtual void Visit(Node node)
        {
            switch (node.NodeCase)
            {
                case Node.NodeOneofCase.SelectStmt:
                    Visit(node.SelectStmt);
                    break;
                case Node.NodeOneofCase.FromExpr:
                    Visit(node.FromExpr);
                    break;
                case Node.NodeOneofCase.RangeVar:
                    Visit(node.RangeVar);
                    break;
                case Node.NodeOneofCase.AExpr:
                    Visit(node.AExpr);
                    break;
                case Node.NodeOneofCase.ColumnRef:
                    Visit(node.ColumnRef);
                    break;
                case Node.NodeOneofCase.ResTarget:
                    Visit(node.ResTarget);
                    break;
                case Node.NodeOneofCase.AConst:
                    Visit(node.AConst);
                    break;
                case Node.NodeOneofCase.TypeCast:
                    Visit(node.TypeCast);
                    break;
                case Node.NodeOneofCase.FuncCall:
                    Visit(node.FuncCall);
                    break;
                case Node.NodeOneofCase.JoinExpr:
                    Visit(node.JoinExpr);
                    break;
            }
        }

        public virtual void Visit(SelectStmt node) { }
        public virtual void Visit(FromExpr node) { }
        public virtual void Visit(RangeVar node) { }
        public virtual void Visit(A_Expr node) { }
        public virtual void Visit(ColumnRef node) { }
        public virtual void Visit(ResTarget node) { }
        public virtual void Visit(A_Const node) { }
        public virtual void Visit(TypeCast node) { }
        public virtual void Visit(FuncCall node) { }
        public virtual void Visit(JoinExpr node) { }
    }

    /// <summary>
    /// Base implementation of AST node visitor that returns a value
    /// </summary>
    public abstract class NodeVisitor<T> : INodeVisitor<T>
    {
        public virtual T Visit(Node node)
        {
            return node.NodeCase switch
            {
                Node.NodeOneofCase.SelectStmt => Visit(node.SelectStmt),
                Node.NodeOneofCase.FromExpr => Visit(node.FromExpr),
                Node.NodeOneofCase.RangeVar => Visit(node.RangeVar),
                Node.NodeOneofCase.AExpr => Visit(node.AExpr),
                Node.NodeOneofCase.ColumnRef => Visit(node.ColumnRef),
                Node.NodeOneofCase.ResTarget => Visit(node.ResTarget),
                Node.NodeOneofCase.AConst => Visit(node.AConst),
                Node.NodeOneofCase.TypeCast => Visit(node.TypeCast),
                Node.NodeOneofCase.FuncCall => Visit(node.FuncCall),
                Node.NodeOneofCase.JoinExpr => Visit(node.JoinExpr),
                _ => default
            };
        }

        public abstract T Visit(SelectStmt node);
        public abstract T Visit(FromExpr node);
        public abstract T Visit(RangeVar node);
        public abstract T Visit(A_Expr node);
        public abstract T Visit(ColumnRef node);
        public abstract T Visit(ResTarget node);
        public abstract T Visit(A_Const node);
        public abstract T Visit(TypeCast node);
        public abstract T Visit(FuncCall node);
        public abstract T Visit(JoinExpr node);
    }
} 