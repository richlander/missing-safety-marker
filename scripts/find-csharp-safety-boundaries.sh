#!/usr/bin/env bash
#
# find-csharp-safety-boundaries.sh
#
# Find safe methods that contain unsafe {} blocks — C#'s implicit safety
# boundary. This is what D marks explicitly with @trusted.
# This script is for current C# or other absence-based variants where the
# boundary is still implicit. If the language has an explicit `safe` keyword,
# prefer plain grep: `rg -w "safe" --type cs`.
#
# A safety boundary is a method NOT marked unsafe, NOT inside an unsafe
# type, that contains unsafe { } blocks in its body.
#
# No LSP. Just grep + awk.
#
# Output: TSV to stdout (file, line, method, signature)
# Summary: to stderr
#
# Limitations (unfixable without a real parser):
#   - String literals containing braces or "unsafe" cause false positives
#   - Preprocessor directives (#if etc.) may hide or reveal code
#   - Partial classes may split an unsafe class across files
#   - This is a triage tool, not an authoritative audit tool
#
# Usage:
#   ./find-csharp-safety-boundaries.sh [dir]
#   ./find-csharp-safety-boundaries.sh ~/git/runtime/src/libraries

set -euo pipefail

DIR="${1:-$(pwd)}"

if [[ ! -d "$DIR" ]]; then
    echo "Error: $DIR is not a directory" >&2
    exit 1
fi

echo "Scanning $DIR for safe methods wrapping unsafe blocks ..." >&2

printf "file\tline\tmethod\tsignature\n"

find "$DIR" -name '*.cs' -print0 | sort -z | while IFS= read -r -d '' file; do
    awk '
    { lines[NR] = $0 }
    END {
        # First pass: find unsafe type ranges (unsafe class/struct)
        # Store as start_line -> end_line pairs
        unsafe_type_count = 0
        for (i = 1; i <= NR; i++) {
            line = lines[i]
            if (line ~ /unsafe[[:space:]]+(class|struct|interface)/) {
                # Find the opening brace
                depth = 0; started = 0
                for (j = i; j <= NR; j++) {
                    s = lines[j]
                    sub(/\/\/.*$/, "", s)
                    for (k = 1; k <= length(s); k++) {
                        c = substr(s, k, 1)
                        if (c == "{") { depth++; started = 1 }
                        if (c == "}") depth--
                    }
                    if (started && depth <= 0) {
                        unsafe_type_count++
                        ut_start[unsafe_type_count] = i
                        ut_end[unsafe_type_count] = j
                        break
                    }
                }
            }
        }

        # Second pass: find methods
        for (n = 1; n <= NR; n++) {
            raw = lines[n]
            stripped = raw
            sub(/\/\/.*$/, "", stripped)

            # Skip lines inside unsafe types
            in_unsafe_type = 0
            for (u = 1; u <= unsafe_type_count; u++) {
                if (n > ut_start[u] && n < ut_end[u]) {
                    in_unsafe_type = 1
                    break
                }
            }
            if (in_unsafe_type) continue

            # Match method declarations (not marked unsafe)
            is_method = 0
            if (stripped ~ /(public|private|protected|internal)/ && stripped ~ /[a-zA-Z_]+[[:space:]]+[a-zA-Z_][a-zA-Z0-9_]*[[:space:]]*\(/) is_method = 1
            if (!is_method) continue
            if (stripped ~ /[[:space:]]unsafe[[:space:]]/) continue
            if (stripped ~ /(^|[[:space:]])(class|struct|interface|enum|delegate)[[:space:]]/) continue

            fn_line = n
            fn_sig = raw
            sub(/^[[:space:]]+/, "", fn_sig)

            # Extract method name: last identifier before (
            fn_name = stripped
            sub(/[[:space:]]*\(.*/, "", fn_name)
            sub(/.*[[:space:]]/, "", fn_name)
            sub(/[<>].*/, "", fn_name)

            # Walk the body tracking brace depth
            depth = 0; started = 0; has_unsafe_block = 0
            prev_was_unsafe = 0

            for (j = n; j <= NR; j++) {
                line = lines[j]
                sub(/\/\/.*$/, "", line)

                # Allman style: "unsafe" alone on one line, "{" on the next
                if (prev_was_unsafe && line ~ /^[[:space:]]*\{/) has_unsafe_block = 1
                prev_was_unsafe = 0
                if (line ~ /[[:space:]]unsafe[[:space:]]*$/ || line ~ /^[[:space:]]*unsafe[[:space:]]*$/) prev_was_unsafe = 1

                for (k = 1; k <= length(line); k++) {
                    c = substr(line, k, 1)
                    if (c == "{") { depth++; started = 1 }
                    if (c == "}") depth--
                }
                # Same-line style: unsafe { ... }
                if (line ~ /unsafe[[:space:]]*\{/) has_unsafe_block = 1
                if (started && depth <= 0) break
            }

            if (has_unsafe_block) {
                printf "%s\t%d\t%s\t%s\n", RELFILE, fn_line, fn_name, fn_sig
            }

            # Skip past the body
            n = j
        }
    }
    ' RELFILE="${file#"$DIR"/}" "$file"
done
