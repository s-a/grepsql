// PgQuery.NET - A comprehensive C# wrapper for libpg_query
// Provides easy, safe access to PostgreSQL's query parser from .NET

using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace PgQuery.NET
{
    // ==================== NATIVE INTEROP ====================
    
    /// <summary>
    /// Native function imports from libpg_query
    /// </summary>
    internal static class Native
    {
        private const string LibraryName = "libpgquery_wrapper";

        static Native()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern PgQueryProtobufParseResult pg_query_parse_protobuf_wrapper(string query);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void pg_query_free_protobuf_parse_result(PgQueryProtobufParseResult result);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr get_fingerprint(string query);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void free_string(IntPtr ptr);
    }
    
    // ==================== NATIVE STRUCTURES ====================
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct PgQueryProtobuf
    {
        public nuint len;
        public IntPtr data;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct PgQueryProtobufParseResult
    {
        public PgQueryProtobuf parse_tree;
        public IntPtr stderr_buffer;
        public IntPtr error;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct PgQueryError
    {
        public IntPtr message;
        public IntPtr filename;
        public int lineno;
        public int cursorpos;
    }
    
    // ==================== MANAGED TYPES ====================
    
    /// <summary>
    /// Exception thrown when a PostgreSQL query parsing error occurs
    /// </summary>
    public class PgQueryException : Exception
    {
        public string? Filename { get; }
        public int LineNumber { get; }
        public int CursorPosition { get; }
        
        internal PgQueryException(string message, string? filename = null, int lineNumber = 0, int cursorPosition = 0) 
            : base(message)
        {
            Filename = filename;
            LineNumber = lineNumber;
            CursorPosition = cursorPosition;
        }
    }
    
    /// <summary>
    /// Result of parsing a PostgreSQL query
    /// </summary>
    public class ParseResult
    {
        public string Query { get; }
        public AST.ParseResult ParseTree { get; }
        public string? StderrBuffer { get; }
        
        internal ParseResult(string query, AST.ParseResult parseTree, string? stderrBuffer = null)
        {
            Query = query;
            ParseTree = parseTree;
            StderrBuffer = stderrBuffer;
        }
    }
    
    /// <summary>
    /// Result of fingerprinting a PostgreSQL query
    /// </summary>
    public class FingerprintResult
    {
        public string Query { get; }
        public string Fingerprint { get; }
        
        internal FingerprintResult(string query, string fingerprint)
        {
            Query = query;
            Fingerprint = fingerprint;
        }
    }
    
    // ==================== MAIN API CLASS ====================
    
    /// <summary>
    /// Main entry point for PostgreSQL query parsing and analysis
    /// </summary>
    public static class PgQuery
    {
        private const int MaxProtobufSize = 50 * 1024 * 1024; // 50MB
        private const int MinProtobufSize = 8; // Minimum size for a valid protobuf message

        /// <summary>
        /// Parse a PostgreSQL query into an AST
        /// </summary>
        /// <param name="query">The SQL query to parse</param>
        /// <returns>ParseResult containing the AST</returns>
        /// <exception cref="PgQueryException">Thrown when parsing fails</exception>
        public static ParseResult Parse(string query)
        {
            if (string.IsNullOrEmpty(query))
                throw new ArgumentException("Query cannot be null or empty", nameof(query));
            
#if DEBUG
            Console.WriteLine($"Attempting to parse query: {query}");
#endif
            var result = Native.pg_query_parse_protobuf_wrapper(query);
            try
            {
#if DEBUG
                Console.WriteLine($"Got result from native library");
                Console.WriteLine($"Result error pointer: {result.error}");
                Console.WriteLine($"Result stderr buffer pointer: {result.stderr_buffer}");
                Console.WriteLine($"Result parse tree data pointer: {result.parse_tree.data}");
                Console.WriteLine($"Result parse tree length: {result.parse_tree.len}");
#endif
                
                CheckForError(result.error);
                
                var stderrBuffer = result.stderr_buffer != IntPtr.Zero 
                    ? Marshal.PtrToStringUTF8(result.stderr_buffer) 
                    : null;
                
                if (stderrBuffer != null)
                {
#if DEBUG
                    Console.WriteLine($"Stderr buffer: {stderrBuffer}");
#endif
                }
                
                // Check if the data pointer is valid
                if (result.parse_tree.data == IntPtr.Zero)
                {
                    throw new PgQueryException("No protobuf data returned");
                }

                // Safely handle the size - nuint to long conversion to avoid overflow
                var dataSizeLong = (long)result.parse_tree.len;
#if DEBUG
                Console.WriteLine($"Raw protobuf size (long): {dataSizeLong}");
#endif
                
                // Validate size bounds
                if (dataSizeLong <= 0)
                {
                    throw new PgQueryException($"Invalid protobuf data size: {dataSizeLong}");
                }
                
                if (dataSizeLong > MaxProtobufSize)
                {
                    throw new PgQueryException($"Protobuf data too large: {dataSizeLong} bytes (max: {MaxProtobufSize})");
                }
                
                if (dataSizeLong < MinProtobufSize)
                {
                    throw new PgQueryException($"Protobuf data too small: {dataSizeLong} bytes (min: {MinProtobufSize})");
                }
                
                // Safe cast to int after validation
                var dataSize = (int)dataSizeLong;
#if DEBUG
                Console.WriteLine($"Attempting to copy {dataSize} bytes of protobuf data");
#endif
                
                // Copy protobuf data to managed memory
                var data = new byte[dataSize];
                try
                {
                    // Use Marshal.Copy for safe memory copying
                    Marshal.Copy(result.parse_tree.data, data, 0, dataSize);
#if DEBUG
                    Console.WriteLine("Successfully copied protobuf data");
#endif
                }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine($"Failed to copy protobuf data: {ex}");
#endif
                    throw new PgQueryException($"Failed to copy protobuf data: {ex.Message}");
                }
                
                // Parse protobuf data
                try
                {
                    using var stream = new MemoryStream(data);
                    var parseTree = AST.ParseResult.Parser.ParseFrom(stream);
                    if (parseTree == null)
                    {
                        throw new PgQueryException("Failed to parse protobuf data: result is null");
                    }
#if DEBUG
                    Console.WriteLine("Successfully parsed protobuf data");
#endif
                    return new ParseResult(query, parseTree, stderrBuffer);
                }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine($"Failed to parse protobuf data: {ex}");
#endif
                    throw new PgQueryException($"Failed to parse protobuf data: {ex.Message}");
                }
            }
            finally
            {
                Native.pg_query_free_protobuf_parse_result(result);
            }
        }
        
        /// <summary>
        /// Generate a fingerprint for a PostgreSQL query
        /// </summary>
        /// <param name="query">The SQL query to fingerprint</param>
        /// <returns>FingerprintResult containing the fingerprint</returns>
        /// <exception cref="PgQueryException">Thrown when fingerprinting fails</exception>
        public static FingerprintResult Fingerprint(string query)
        {
            if (string.IsNullOrEmpty(query))
                throw new ArgumentException("Query cannot be null or empty", nameof(query));
            
            var fingerprintPtr = Native.get_fingerprint(query);
            if (fingerprintPtr == IntPtr.Zero)
                throw new PgQueryException("Failed to generate fingerprint");
            
            try
            {
                var fingerprint = Marshal.PtrToStringUTF8(fingerprintPtr);
                return new FingerprintResult(query, fingerprint ?? throw new PgQueryException("Failed to read fingerprint"));
            }
            finally
            {
                Native.free_string(fingerprintPtr);
            }
        }
        
        private static void CheckForError(IntPtr errorPtr)
        {
            if (errorPtr == IntPtr.Zero) return;
            
            var error = Marshal.PtrToStructure<PgQueryError>(errorPtr);
            var message = error.message != IntPtr.Zero ? Marshal.PtrToStringUTF8(error.message) : "Unknown error";
            var filename = error.filename != IntPtr.Zero ? Marshal.PtrToStringUTF8(error.filename) : null;
            
            throw new PgQueryException(message ?? "Unknown error", filename, error.lineno, error.cursorpos);
        }
    }
    
    // ==================== EXTENSION METHODS ====================
    
    /// <summary>
    /// Extension methods for working with PostgreSQL ASTs
    /// </summary>
    public static class PgQueryExtensions
    {
        /// <summary>
        /// Find all table names referenced in the query
        /// </summary>
        public static List<string> GetTableNames(this ParseResult parseResult)
        {
            var tables = new List<string>();
            
            // Navigate the AST to find table references
            foreach (var stmt in parseResult.ParseTree.Stmts)
            {
                FindTableNames(stmt.Stmt, tables);
            }
            
            return tables.Distinct().ToList();
        }
        
        private static void FindTableNames(AST.Node node, List<string> tables)
        {
            // Look for RangeVar nodes (table references)
            if (node.RangeVar != null)
            {
                if (!string.IsNullOrEmpty(node.RangeVar.Relname))
                {
                    tables.Add(node.RangeVar.Relname);
                }
            }
            
            // Recursively search all child nodes
            foreach (var child in node.GetChildren())
            {
                FindTableNames(child, tables);
            }
        }
        
        /// <summary>
        /// Check if the query is a SELECT statement
        /// </summary>
        public static bool IsSelectQuery(this ParseResult parseResult)
        {
            foreach (var stmt in parseResult.ParseTree.Stmts)
            {
                if (stmt.Stmt.SelectStmt != null)
                {
                    return true;
                }
            }
            
            return false;
        }
    }
}

// ==================== EXAMPLE USAGE ====================

namespace PgQuery.NET.Examples
{
    public static class Examples
    {
        public static void RunExamples()
        {
            // Basic parsing
            var query = "SELECT id, name FROM users WHERE age > 25 ORDER BY name";
            
            try
            {
                // Parse the query
                var parseResult = PgQuery.Parse(query);
                Console.WriteLine($"Parsed query: {parseResult.Query}");
                Console.WriteLine($"Is SELECT: {parseResult.IsSelectQuery()}");
                Console.WriteLine($"Tables: {string.Join(", ", parseResult.GetTableNames())}");
                
                // Generate fingerprint
                var fingerprintResult = PgQuery.Fingerprint(query);
                Console.WriteLine($"Fingerprint: {fingerprintResult.Fingerprint}");
                
                // Advanced AST navigation
                foreach (var stmt in parseResult.ParseTree.Stmts)
                {
                    NavigateAst(stmt.Stmt);
                }
            }
            catch (PgQueryException ex)
            {
                Console.WriteLine($"Parse error: {ex.Message}");
                if (ex.CursorPosition > 0)
                {
                    Console.WriteLine($"Error at position: {ex.CursorPosition}");
                }
            }
        }
        
        private static void NavigateAst(AST.Node node, int depth = 0)
        {
            var indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}Node type: {node.NodeCase}");
            
            foreach (var child in node.GetChildren())
            {
                NavigateAst(child, depth + 1);
            }
        }
    }
}
