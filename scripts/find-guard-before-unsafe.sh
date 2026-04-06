#!/usr/bin/env bash
#
# find-guard-before-unsafe.sh
#
# Triage C# methods that pair guard logic with unsafe-ish operations.
#
# This is intended for current C# and proposed variants *without* an explicit
# `safe` keyword. It helps surface likely safety-boundary methods when the
# boundary is still inferred from source patterns rather than marked directly.
#
# If the language uses an explicit `safe` marker, prefer plain grep:
#   rg -w "safe" --type cs <dir>
#
# Output: TSV to stdout (file, line, method, signature, indicators)
# Summary: to stderr
#
# Limitations:
#   - This is a heuristic triage tool, not an authoritative parser.
#   - Method matching is approximate and will miss unusual formatting.
#   - It looks for common unsafe primitives and common guard idioms only.
#
# Usage:
#   ./find-guard-before-unsafe.sh [dir]
#   ./find-guard-before-unsafe.sh ~/git/runtime/src/libraries/System.Private.CoreLib/src/System/Collections

set -euo pipefail

DIR="${1:-$(pwd)}"

if [[ ! -d "$DIR" ]]; then
    echo "Error: $DIR is not a directory" >&2
    exit 1
fi

echo "Scanning $DIR for methods that guard unsafe-ish operations ..." >&2

printf "file\tline\tmethod\tsignature\tindicators\n"

find "$DIR" -name '*.cs' -print0 | sort -z | while IFS= read -r -d '' file; do
    awk '
    function add_indicator(tag) {
        if (index(indicators, tag) == 0) {
            indicators = (indicators == "" ? tag : indicators "," tag)
        }
    }

    { lines[NR] = $0 }

    END {
        for (n = 1; n <= NR; n++) {
            raw = lines[n]
            stripped = raw
            sub(/\/\/.*$/, "", stripped)

            is_method = 0
            if (stripped ~ /(public|private|protected|internal)/ &&
                stripped ~ /[a-zA-Z_]+[[:space:]]+[a-zA-Z_][a-zA-Z0-9_]*[[:space:]]*\(/) {
                is_method = 1
            }
            if (!is_method) continue
            if (stripped ~ /(^|[[:space:]])(class|struct|interface|enum|delegate)[[:space:]]/) continue

            fn_line = n
            fn_sig = raw
            sub(/^[[:space:]]+/, "", fn_sig)

            fn_name = stripped
            sub(/[[:space:]]*\(.*/, "", fn_name)
            sub(/.*[[:space:]]/, "", fn_name)
            sub(/[<>].*/, "", fn_name)

            depth = 0
            started = 0
            has_unsafe = 0
            has_guard = 0
            indicators = ""

            for (j = n; j <= NR; j++) {
                line = lines[j]
                sub(/\/\/.*$/, "", line)

                if (line ~ /Unsafe\./) {
                    has_unsafe = 1
                    add_indicator("Unsafe.*")
                }
                if (line ~ /MemoryMarshal\./) {
                    has_unsafe = 1
                    add_indicator("MemoryMarshal")
                }
                if (line ~ /Buffer\.Memmove/) {
                    has_unsafe = 1
                    add_indicator("Buffer.Memmove")
                }
                if (line ~ /NativeMemory\./) {
                    has_unsafe = 1
                    add_indicator("NativeMemory")
                }
                if (line ~ /Marshal\./) {
                    has_unsafe = 1
                    add_indicator("Marshal")
                }
                if (line ~ /unsafe[[:space:]]*\{/) {
                    has_unsafe = 1
                    add_indicator("unsafe-block")
                }
                if (line ~ /stackalloc/) {
                    has_unsafe = 1
                    add_indicator("stackalloc")
                }
                if (line ~ /fixed[[:space:]]*\(/) {
                    has_unsafe = 1
                    add_indicator("fixed")
                }

                if (line ~ /ThrowIf/) {
                    has_guard = 1
                    add_indicator("ThrowIf")
                }
                if (line ~ /ThrowHelper\.Throw/) {
                    has_guard = 1
                    add_indicator("ThrowHelper")
                }
                if (line ~ /ArgumentOutOfRangeException|ArgumentNullException|ArgumentException/) {
                    has_guard = 1
                    add_indicator("ArgumentCheck")
                }
                if (line ~ /Debug\.Assert/) {
                    has_guard = 1
                    add_indicator("Debug.Assert")
                }

                for (k = 1; k <= length(line); k++) {
                    c = substr(line, k, 1)
                    if (c == "{") { depth++; started = 1 }
                    if (c == "}") depth--
                }

                if (started && depth <= 0) break
            }

            if (has_unsafe && has_guard) {
                printf "%s\t%d\t%s\t%s\t%s\n", RELFILE, fn_line, fn_name, fn_sig, indicators
            }

            n = j
        }
    }
    ' RELFILE="${file#"$DIR"/}" "$file"
done
