using System;
using Xunit;
using Xunit.Abstractions;

namespace PgQuery.NET.Tests
{
    public class MinimalLoadTest
    {
        private readonly ITestOutputHelper _output;

        public MinimalLoadTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void LoadSimpleQuery()
        {
            var query = "SELECT 1";
            _output.WriteLine($"Attempting to parse query: {query}");

            try
            {
                var result = PgQuery.Parse(query);
                _output.WriteLine("Successfully parsed query!");
                _output.WriteLine($"Number of statements: {result.ParseTree.Stmts.Count}");
                
                var firstStmt = result.ParseTree.Stmts[0].Stmt;
                _output.WriteLine($"First statement type: {firstStmt.NodeCase}");
                
                if (firstStmt.SelectStmt != null)
                {
                    var selectStmt = firstStmt.SelectStmt;
                    _output.WriteLine($"Number of target list items: {selectStmt.TargetList.Count}");
                    
                    foreach (var target in selectStmt.TargetList)
                    {
                        _output.WriteLine($"Target value type: {target.ResTarget.Val.NodeCase}");
                        if (target.ResTarget.Val.AConst != null)
                        {
                            _output.WriteLine($"Constant value case: {target.ResTarget.Val.AConst.ValCase}");
                            if (target.ResTarget.Val.AConst.ValCase == AST.A_Const.ValOneofCase.Ival)
                            {
                                _output.WriteLine($"Integer value: {target.ResTarget.Val.AConst.Ival.Ival}");
                            }
                        }
                    }
                }
            }
            catch (PgQueryException ex)
            {
                _output.WriteLine($"Parse error: {ex.Message}");
                if (ex.CursorPosition > 0)
                {
                    _output.WriteLine($"Error at position: {ex.CursorPosition}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Unexpected error: {ex}");
            }
        }
    }
} 