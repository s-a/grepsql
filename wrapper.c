#include "libpg_query/pg_query.h"

// Simple wrapper to export pg_query functions
PgQueryParseResult pg_query_parse_wrapper(const char* input) {
    return pg_query_parse(input);
}

void pg_query_free_parse_result_wrapper(PgQueryParseResult result) {
    pg_query_free_parse_result(result);
}

PgQueryNormalizeResult pg_query_normalize_wrapper(const char* input) {
    return pg_query_normalize(input);
}

void pg_query_free_normalize_result_wrapper(PgQueryNormalizeResult result) {
    pg_query_free_normalize_result(result);
}

// Protobuf wrapper functions
PgQueryProtobufParseResult pg_query_parse_protobuf_wrapper(const char* input) {
    return pg_query_parse_protobuf(input);
}

void pg_query_free_protobuf_parse_result_wrapper(PgQueryProtobufParseResult result) {
    pg_query_free_protobuf_parse_result(result);
}

PgQueryFingerprintResult pg_query_fingerprint_wrapper(const char* input) {
    return pg_query_fingerprint(input);
}

void pg_query_free_fingerprint_result_wrapper(PgQueryFingerprintResult result) {
    pg_query_free_fingerprint_result(result);
}

PgQueryScanResult pg_query_scan_wrapper(const char* input) {
    return pg_query_scan(input);
}

void pg_query_free_scan_result_wrapper(PgQueryScanResult result) {
    pg_query_free_scan_result(result);
}
