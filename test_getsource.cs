using System;
using PgQuery.NET;
using PgQuery.NET.Analysis;

public class TestGetSource
{
    public static void Main()
    {
        var sql = "SELECT id, name FROM users WHERE status = 'active'";
        
        Console.WriteLine("Original SQL:");
        Console.WriteLine(sql);
        Console.WriteLine();
        
        var parseResult = PgQuery.Parse(sql);
        var stmt = parseResult.ParseTree.Stmts[0].Stmt;
        
        // Find all nodes with location information
        var locatedNodes = LocationExtractor.GetAllLocatedNodes(stmt, sql);
        
        Console.WriteLine("Located nodes and their source text:");
        foreach (var (node, range) in locatedNodes)
        {
            var source = node.GetSource(sql);
            var sourceInfo = node.GetSourceInfo(sql);
            
            Console.WriteLine($"- {node.GetType().Name}: \"{source}\" at {range} (Line {sourceInfo?.StartLine}, Col {sourceInfo?.StartColumn})");
        }
        
        Console.WriteLine();
        Console.WriteLine("Testing getSource() on specific nodes:");
        
        // Test the SelectStmt node
        var selectStmt = stmt;
        var selectSource = selectStmt.GetSource(sql);
        Console.WriteLine($"SelectStmt source: \"{selectSource}\"");
        
        // Find a specific column reference
        var columnRefs = SqlPatternMatcher.Search("ColumnRef", sql);
        if (columnRefs.Count > 0)
        {
            var firstColumnRef = columnRefs[0];
            var columnSource = firstColumnRef.GetSource(sql);
            var columnInfo = firstColumnRef.GetSourceInfo(sql);
            
            Console.WriteLine($"First ColumnRef: \"{columnSource}\" at Line {columnInfo?.StartLine}, Column {columnInfo?.StartColumn}");
        }
    }
} 