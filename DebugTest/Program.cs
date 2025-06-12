using System;
using PgQuery.NET.Analysis;

Console.WriteLine("Testing basic pattern:");
Console.WriteLine($"Result: {SqlPatternMatcher.Match("(relname \"users\")", "SELECT id FROM users")}");
