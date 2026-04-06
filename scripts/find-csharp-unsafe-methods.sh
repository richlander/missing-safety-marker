#!/usr/bin/env bash
#
# find-csharp-unsafe-methods.sh
#
# Find all methods that are unsafe — both explicitly marked and
# implicitly unsafe (inside an unsafe class/struct).
# This is most useful for current C# and proposed variants that still rely on
# `unsafe` as the primary discoverable marker. In an explicit-`safe` world,
# pair this with `rg -w "safe"` rather than treating it as the only inventory.
#
# This is what D splits into @system (explicitly unsafe) and what
# falls outside @safe/@trusted. In C# the two categories are:
#   1. Methods with the `unsafe` modifier on the method itself
#   2. Methods inside an `unsafe class` or `unsafe struct`
#
# No LSP. Just grep + awk.
#
# Output: TSV to stdout (file, line, method, signature, reason)
# Summary: to stderr
#
# Limitations (unfixable without a real parser):
#   - String literals containing "unsafe" may cause false positives
#   - Partial classes may split an unsafe type across files
#   - Nested types (unsafe class inside a safe class) tracked by depth
#   - This is a triage tool, not an authoritative audit tool
#
# Usage:
#   ./find-csharp-unsafe-methods.sh [dir]
#   ./find-csharp-unsafe-methods.sh ~/git/runtime/src/libraries

set -euo pipefail

DIR="${1:-$(pwd)}"

if [[ ! -d "$DIR" ]]; then
    echo "Error: $DIR is not a directory" >&2
    exit 1
fi

echo "Scanning $DIR for unsafe methods (explicit + implicit) ..." >&2

printf "file\tline\tmethod\tsignature\treason\n"

find "$DIR" -name '*.cs' -print0 | sort -z | while IFS= read -r -d '' file; do
    awk '
    { lines[NR] = $0 }
    END {
        # First pass: find unsafe type ranges
        unsafe_type_count = 0
        for (i = 1; i <= NR; i++) {
            line = lines[i]
            if (line ~ /unsafe[[:space:]]+(class|struct|interface)/) {
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
                        # Also store the type name for reporting
                        ut_line_text[unsafe_type_count] = lines[i]
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

            # Is this a method declaration?
            is_method = 0
            if (stripped ~ /(public|private|protected|internal)/ && stripped ~ /[a-zA-Z_]+[[:space:]]+[a-zA-Z_][a-zA-Z0-9_]*[[:space:]]*\(/) is_method = 1
            if (!is_method) continue
            if (stripped ~ /(^|[[:space:]])(class|struct|interface|enum|delegate)[[:space:]]/) continue

            fn_line = n
            fn_sig = raw
            sub(/^[[:space:]]+/, "", fn_sig)

            # Extract method name
            fn_name = stripped
            sub(/[[:space:]]*\(.*/, "", fn_name)
            sub(/.*[[:space:]]/, "", fn_name)
            sub(/[<>].*/, "", fn_name)

            reason = ""

            # Check: explicitly marked unsafe
            if (stripped ~ /[[:space:]]unsafe[[:space:]]/) {
                reason = "explicit"
            }

            # Check: inside an unsafe type
            if (reason == "") {
                for (u = 1; u <= unsafe_type_count; u++) {
                    if (n > ut_start[u] && n < ut_end[u]) {
                        reason = "implicit (unsafe type)"
                        break
                    }
                }
            }

            if (reason != "") {
                printf "%s\t%d\t%s\t%s\t%s\n", RELFILE, fn_line, fn_name, fn_sig, reason

                # Skip past method body
                depth = 0; started = 0
                for (j = n; j <= NR; j++) {
                    line = lines[j]
                    sub(/\/\/.*$/, "", line)
                    for (k = 1; k <= length(line); k++) {
                        c = substr(line, k, 1)
                        if (c == "{") { depth++; started = 1 }
                        if (c == "}") depth--
                    }
                    if (started && depth <= 0) break
                }
                n = j
            }
        }
    }
    ' RELFILE="${file#"$DIR"/}" "$file"
done
