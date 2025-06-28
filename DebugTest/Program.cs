using System;
using GrepSQL.Analysis;

Console.WriteLine("Testing basic pattern:");
Console.WriteLine($"Result: {SqlPatternMatcher.Match("(relname \"users\")", "SELECT id FROM users")}");
