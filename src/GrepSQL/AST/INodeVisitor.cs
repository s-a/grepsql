using System;

namespace GrepSQL.AST
{
    /// <summary>
    /// Interface for AST node visitors
    /// </summary>
    public interface INodeVisitor
    {
        void Visit(Node node);
        void Visit(SelectStmt node);
        void Visit(FromExpr node);
        void Visit(RangeVar node);
        void Visit(A_Expr node);
        void Visit(ColumnRef node);
        void Visit(ResTarget node);
        void Visit(A_Const node);
        void Visit(TypeCast node);
        void Visit(FuncCall node);
        void Visit(JoinExpr node);
    }

    /// <summary>
    /// Interface for AST node visitors that return a value
    /// </summary>
    public interface INodeVisitor<T>
    {
        T Visit(Node node);
        T Visit(SelectStmt node);
        T Visit(FromExpr node);
        T Visit(RangeVar node);
        T Visit(A_Expr node);
        T Visit(ColumnRef node);
        T Visit(ResTarget node);
        T Visit(A_Const node);
        T Visit(TypeCast node);
        T Visit(FuncCall node);
        T Visit(JoinExpr node);
    }
} 