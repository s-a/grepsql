using System;
using System.Text.Json;
using PgQuery.NET;
using PgQuery.NET.Analysis;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("PgQuery.NET API Examples");
        Console.WriteLine("=======================\n");

        if (args.Length > 0 && args[0] == "--debug-ast")
        {
            var query = args[1];
            var result = PgQuery.NET.PgQuery.Parse(query);
            Console.WriteLine($"AST for query: {query}");
            Console.WriteLine(JsonSerializer.Serialize(result.ParseTree, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        // Basic Query Parsing
        ShowBasicParsing();

        // Query Normalization
        ShowNormalization();

        // Query Fingerprinting
        ShowFingerprinting();

        // Error Handling
        ShowErrorHandling();

        // Table Name Extraction
        ShowTableExtraction();

        // Query Type Detection
        ShowQueryTypeDetection();

        // Advanced Analysis
        ShowAdvancedAnalysis();

        // Pattern Matching
        ShowPatternMatching();
    }

    static void ShowBasicParsing()
    {
        Console.WriteLine("1. Basic Query Parsing");
        Console.WriteLine("---------------------");

        var query = "SELECT id, name FROM users WHERE age > 25";
        var result = PgQuery.NET.PgQuery.Parse(query);
        
        Console.WriteLine($"Query: {query}");
        Console.WriteLine($"AST (first node type): {result.ParseTree.RootElement.EnumerateObject().First().Name}");
        Console.WriteLine();
    }

    static void ShowNormalization()
    {
        Console.WriteLine("2. Query Normalization");
        Console.WriteLine("---------------------");

        var queries = new[]
        {
            "SELECT * FROM users WHERE age = 25",
            "INSERT INTO logs (message) VALUES ('User logged in')",
            "UPDATE users SET last_login = '2024-03-20 10:30:00' WHERE id = 123"
        };

        foreach (var query in queries)
        {
            var result = PgQuery.NET.PgQuery.Normalize(query);
            Console.WriteLine($"Original:   {query}");
            Console.WriteLine($"Normalized: {result.NormalizedQuery}");
            Console.WriteLine();
        }
    }

    static void ShowFingerprinting()
    {
        Console.WriteLine("3. Query Fingerprinting");
        Console.WriteLine("----------------------");

        // Example 1: Same structure, different values
        var query1 = "SELECT * FROM users WHERE age > 25";
        var query2 = "SELECT * FROM users WHERE age > 30";

        var fp1 = PgQuery.NET.PgQuery.Fingerprint(query1);
        var fp2 = PgQuery.NET.PgQuery.Fingerprint(query2);

        Console.WriteLine("Comparing structurally similar queries:");
        Console.WriteLine($"Query 1: {query1}");
        Console.WriteLine($"Query 2: {query2}");
        Console.WriteLine($"Same fingerprint: {fp1.Fingerprint == fp2.Fingerprint}");
        Console.WriteLine();

        // Example 2: Different structure
        var query3 = "SELECT name FROM users";
        var fp3 = PgQuery.NET.PgQuery.Fingerprint(query3);

        Console.WriteLine("Comparing structurally different queries:");
        Console.WriteLine($"Query 1: {query1}");
        Console.WriteLine($"Query 3: {query3}");
        Console.WriteLine($"Same fingerprint: {fp1.Fingerprint == fp3.Fingerprint}");
        Console.WriteLine();
    }

    static void ShowErrorHandling()
    {
        Console.WriteLine("4. Error Handling");
        Console.WriteLine("----------------");

        var invalidQueries = new[]
        {
            "SELECT * FROM",  // Incomplete
            "SELCT * FROM users",  // Typo
            "SELECT * FROM users WHERE"  // Incomplete WHERE clause
        };

        foreach (var query in invalidQueries)
        {
            try
            {
                var result = PgQuery.NET.PgQuery.Parse(query);
                Console.WriteLine($"Unexpected success parsing: {query}");
            }
            catch (PgQueryException ex)
            {
                Console.WriteLine($"Query: {query}");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Position: {ex.CursorPosition}");
                Console.WriteLine();
            }
        }
    }

    static void ShowTableExtraction()
    {
        Console.WriteLine("5. Table Name Extraction");
        Console.WriteLine("----------------------");

        var queries = new[]
        {
            "SELECT * FROM users JOIN orders ON users.id = orders.user_id",
            "WITH temp AS (SELECT * FROM logs) SELECT * FROM temp JOIN users ON temp.user_id = users.id",
            "INSERT INTO audit_log SELECT * FROM users WHERE deleted = true"
        };

        foreach (var query in queries)
        {
            var result = PgQuery.NET.PgQuery.Parse(query);
            var tables = result.GetTableNames();

            Console.WriteLine($"Query: {query}");
            Console.WriteLine($"Tables: [{string.Join(", ", tables)}]");
            Console.WriteLine();
        }
    }

    static void ShowQueryTypeDetection()
    {
        Console.WriteLine("6. Query Type Detection");
        Console.WriteLine("---------------------");

        var queries = new[]
        {
            "SELECT * FROM users",
            "INSERT INTO users (name) VALUES ('John')",
            "UPDATE users SET active = false",
            "DELETE FROM users WHERE id = 1"
        };

        foreach (var query in queries)
        {
            var result = PgQuery.NET.PgQuery.Parse(query);
            Console.WriteLine($"Query: {query}");
            Console.WriteLine($"Is SELECT: {result.IsSelectQuery()}");
            Console.WriteLine();
        }
    }

    static void ShowAdvancedAnalysis()
    {
        Console.WriteLine("7. Advanced Analysis");
        Console.WriteLine("------------------");

        var complexQuery = @"
            WITH regional_sales AS (
                SELECT region, SUM(amount) as total
                FROM orders
                GROUP BY region
            )
            SELECT r.region, 
                   r.total,
                   ROW_NUMBER() OVER (ORDER BY r.total DESC) as rank
            FROM regional_sales r
            WHERE r.total > 1000";

        // Analyze complexity
        var complexity = SqlAnalyzer.AnalyzeComplexity(complexQuery);
        Console.WriteLine("Query Complexity Analysis:");
        Console.WriteLine($"- Selects: {complexity.Selects}");
        Console.WriteLine($"- Joins: {complexity.Joins}");
        Console.WriteLine($"- Subqueries: {complexity.Subqueries}");
        Console.WriteLine($"- Window Functions: {complexity.WindowFunctions}");
        Console.WriteLine($"- Function Calls: {complexity.FunctionCalls}");
        Console.WriteLine($"- Complexity Level: {complexity.ComplexityLevel}");
        Console.WriteLine();

        // Find column references
        var columns = SqlAnalyzer.FindColumnReferences(complexQuery);
        Console.WriteLine("Column References:");
        Console.WriteLine($"[{string.Join(", ", columns)}]");
        Console.WriteLine();

        // Check for write operations
        var hasWrites = SqlAnalyzer.ContainsWriteOperations(complexQuery);
        Console.WriteLine($"Contains write operations: {hasWrites}");
        Console.WriteLine();
    }

    static void ShowPatternMatching()
    {
        Console.WriteLine("8. SQL Pattern Matching");
        Console.WriteLine("---------------------");

        // Example 1: Basic wildcard matching
        var example1 = new[]
        {
            // Match any SELECT statement
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"targetList\": _, \"fromClause\": _}}}]}", 
             "SELECT id FROM users"),
            
            // Match SELECT with specific target list
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"targetList\": [{\"ResTarget\": {\"val\": {\"ColumnRef\": {\"fields\": [{\"String\": {\"sval\": \"id\"}}]}}}}]}}}]}", 
             "SELECT id FROM users"),
            
            // Match SELECT with specific FROM clause
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"fromClause\": [{\"RangeVar\": {\"relname\": \"users\"}}]}}}]}", 
             "SELECT * FROM users")
        };

        Console.WriteLine("Basic Pattern Matching (with debug):");
        foreach (var (pattern, query) in example1)
        {
            Console.WriteLine($"\nTesting pattern against query:");
            Console.WriteLine($"Pattern: {pattern}");
            Console.WriteLine($"Query:   {query}");
            var matches = SqlPatternMatcher.Matches(pattern, query, debug: true);
            Console.WriteLine($"\nFinal result: {matches}\n");
            Console.WriteLine(new string('-', 80));
        }

        // Example 2: Capturing and backreferences
        var example2 = new[]
        {
            // Capture table name and reference it in column ref
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {" +
             "\"fromClause\": [{\"RangeVar\": {\"relname\": $name}}], " +
             "\"targetList\": [{\"ResTarget\": {\"val\": {\"ColumnRef\": {\"fields\": [{\"String\": {\"sval\": \\1}}]}}}}]" +
             "}}}]}", 
             "SELECT users FROM users"),
            
            // Capture multiple values
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {" +
             "\"targetList\": [{\"ResTarget\": {\"val\": {\"ColumnRef\": {\"fields\": [{\"String\": {\"sval\": $col}}]}}}}], " +
             "\"fromClause\": [{\"RangeVar\": {\"relname\": $table}}]" +
             "}}}]}", 
             "SELECT id FROM users")
        };

        Console.WriteLine("\nCapturing and Backreferences (with debug):");
        foreach (var (pattern, query) in example2)
        {
            Console.WriteLine($"\nTesting pattern against query:");
            Console.WriteLine($"Pattern: {pattern}");
            Console.WriteLine($"Query:   {query}");
            var matches = SqlPatternMatcher.Matches(pattern, query, debug: true);
            Console.WriteLine($"\nFinal result: {matches}\n");
            Console.WriteLine(new string('-', 80));
        }

        // Example 3: Union and intersection conditions
        var example3 = new[]
        {
            // Match either SELECT or UPDATE
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"UpdateStmt\": {\"relation\": {\"relname\": \"users\"}, \"targetList\": _}}}]}", 
             "UPDATE users SET active = true"),
            
            // Match SELECT with WHERE clause
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"targetList\": _, \"fromClause\": _, \"whereClause\": _}}}]}", 
             "SELECT * FROM users WHERE age > 25"),
            
            // Complex pattern combining features
            ("{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {" +
             "\"targetList\": [{\"ResTarget\": {\"val\": {\"FuncCall\": {\"funcname\": [{\"String\": {\"sval\": \"COUNT\"}}]}}}}], " +
             "\"fromClause\": [{\"RangeVar\": {\"relname\": \"users\"}}], " +
             "\"groupClause\": _" +
             "}}}]}", 
             "SELECT COUNT(*) FROM users GROUP BY id")
        };

        Console.WriteLine("\nUnion and Intersection Conditions (with debug):");
        foreach (var (pattern, query) in example3)
        {
            Console.WriteLine($"\nTesting pattern against query:");
            Console.WriteLine($"Pattern: {pattern}");
            Console.WriteLine($"Query:   {query}");
            var matches = SqlPatternMatcher.Matches(pattern, query, debug: true);
            Console.WriteLine($"\nFinal result: {matches}\n");
            Console.WriteLine(new string('-', 80));
        }

        // Example 4: Search for patterns
        Console.WriteLine("\nPattern Search (with debug):");
        var searchQuery = @"
            WITH regional_sales AS (
                SELECT region, SUM(amount) as total
                FROM orders
                GROUP BY region
            )
            SELECT r.region, 
                   r.total,
                   ROW_NUMBER() OVER (ORDER BY r.total DESC) as rank
            FROM regional_sales r
            WHERE r.total > 1000";

        var searchPatterns = new[]
        {
            // Find CTE
            "{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"withClause\": {\"ctes\": _}}}}]}", 
            
            // Find function calls
            "{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"targetList\": [{\"ResTarget\": {\"val\": {\"FuncCall\": {\"funcname\": [{\"String\": {\"sval\": $name}}]}}}}]}}}]}", 
            
            // Find window functions
            "{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"targetList\": [{\"ResTarget\": {\"val\": {\"WindowFunc\": _}}}]}}}]}", 
            
            // Find SELECT with WHERE
            "{\"version\": _, \"stmts\": [{\"stmt\": {\"SelectStmt\": {\"targetList\": _, \"fromClause\": _, \"whereClause\": _}}}]}"
        };

        foreach (var pattern in searchPatterns)
        {
            Console.WriteLine($"\nSearching with pattern:");
            Console.WriteLine($"Pattern: {pattern}");
            var results = SqlPatternMatcher.Search(pattern, searchQuery, debug: true);
            Console.WriteLine($"\nFound {results.Count} matches\n");
            Console.WriteLine(new string('-', 80));
        }
    }
} 