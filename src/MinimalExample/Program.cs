using System;
using GrepSQL;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Starting minimal GrepSQL example...");
        
        try
        {
            var query = "SELECT 1";
            Console.WriteLine($"Attempting to parse query: {query}");
            
            var result = GrepSQL.PgQuery.Parse(query);
            Console.WriteLine("Successfully parsed query!");
            Console.WriteLine($"Number of statements: {result.ParseTree.Stmts.Count}");
            
            var firstStmt = result.ParseTree.Stmts[0].Stmt;
            Console.WriteLine($"First statement type: {firstStmt.SelectStmt}");
            
            Console.WriteLine("Parse tree details:");
            Console.WriteLine(result.ParseTree.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
} 