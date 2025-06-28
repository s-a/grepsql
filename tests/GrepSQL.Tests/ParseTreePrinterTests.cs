using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using GrepSQL.AST;

namespace GrepSQL.Tests
{
    public class ParseTreePrinterTests
    {
        private readonly ITestOutputHelper _output;

        public ParseTreePrinterTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void PrintSimpleSelectTree()
        {
            var query = "SELECT id, name FROM users WHERE age > 25";
            var result = PgQuery.Parse(query);
            
            _output.WriteLine($"Query: {query}");
            _output.WriteLine("Parse Tree:");
            
            foreach (var stmt in result.ParseTree.Stmts)
            {
                PrintNode(stmt.Stmt);
            }
        }

        private void PrintNode(Google.Protobuf.IMessage node, int depth = 0)
        {
            var indent = new string(' ', depth * 2);
            _output.WriteLine($"{indent}Node: {node.GetType().Name}");

            switch (node)
            {
                case SelectStmt selectStmt:
                    PrintSelectStmt(selectStmt, depth + 1);
                    break;
                case RangeVar rangeVar:
                    PrintRangeVar(rangeVar, depth + 1);
                    break;
                case A_Expr aExpr:
                    PrintAExpr(aExpr, depth + 1);
                    break;
                case ColumnRef columnRef:
                    PrintColumnRef(columnRef, depth + 1);
                    break;
                case A_Const aConst:
                    PrintAConst(aConst, depth + 1);
                    break;
            }
        }

        private void PrintSelectStmt(SelectStmt stmt, int depth)
        {
            var indent = new string(' ', depth * 2);
            
            _output.WriteLine($"{indent}Target List:");
            foreach (var target in stmt.TargetList)
            {
                PrintNode(target.ResTarget.Val, depth + 1);
            }

            _output.WriteLine($"{indent}From Clause:");
            foreach (var from in stmt.FromClause)
            {
                PrintNode(from, depth + 1);
            }

            if (stmt.WhereClause != null)
            {
                _output.WriteLine($"{indent}Where Clause:");
                PrintNode(stmt.WhereClause, depth + 1);
            }
        }

        private void PrintRangeVar(RangeVar rangeVar, int depth)
        {
            var indent = new string(' ', depth * 2);
            _output.WriteLine($"{indent}Table: {rangeVar.Relname}");
        }

        private void PrintAExpr(A_Expr aexpr, int depth)
        {
            var indent = new string(' ', depth * 2);
            _output.WriteLine($"{indent}Operator: {string.Join(".", aexpr.Name)}");
            
            _output.WriteLine($"{indent}Left:");
            PrintNode(aexpr.Lexpr, depth + 1);
            
            _output.WriteLine($"{indent}Right:");
            PrintNode(aexpr.Rexpr, depth + 1);
        }

        private void PrintColumnRef(ColumnRef columnRef, int depth)
        {
            var indent = new string(' ', depth * 2);
            _output.WriteLine($"{indent}Column: {string.Join(".", columnRef.Fields)}");
        }

        private void PrintAConst(A_Const aconst, int depth)
        {
            var indent = new string(' ', depth * 2);
            var value = aconst.ValCase switch
            {
                A_Const.ValOneofCase.Ival => aconst.Ival.Ival.ToString(),
                A_Const.ValOneofCase.Fval => aconst.Fval.Fval,
                A_Const.ValOneofCase.Sval => aconst.Sval.Sval,
                A_Const.ValOneofCase.Boolval => aconst.Boolval.Boolval.ToString(),
                A_Const.ValOneofCase.Bsval => aconst.Bsval.Bsval,
                _ => "null"
            };
            _output.WriteLine($"{indent}Value: {value}");
        }
    }
} 