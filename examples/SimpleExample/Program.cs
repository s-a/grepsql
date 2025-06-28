using GrepSQL;

class Program
{
    static void Main()
    {
        // Parse a SQL query
        var query = "SELECT id, name FROM users WHERE age > 25 ORDER BY name";
        
        try
        {
            // Generate fingerprint
            var fingerprintResult = GrepSQL.PgQuery.Fingerprint(query);
            Console.WriteLine($"Query: {query}");
            Console.WriteLine($"Fingerprint: {fingerprintResult.Fingerprint}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (ex is PgQueryException pgEx)
            {
                Console.WriteLine($"Query error at position: {pgEx.CursorPosition}");
            }
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
