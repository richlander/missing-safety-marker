using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

return UnsafeMethodScanner.Run(args);

internal static partial class UnsafeMethodScanner
{
    private static readonly Regex WhitespaceRegex = CollapseWhitespace();

    public static int Run(string[] args)
    {
        if (!TryParseOptions(args, out var options, out var errorMessage))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine();
            }

            PrintUsage();
            return string.IsNullOrWhiteSpace(errorMessage) ? 0 : 1;
        }

        var inputPath = ExpandHome(options.InputPath);
        if (!Directory.Exists(inputPath) && !File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: '{options.InputPath}' does not exist.");
            return 1;
        }

        var fullInputPath = Path.GetFullPath(inputPath);
        var displayRoot = Directory.Exists(fullInputPath)
            ? fullInputPath
            : Path.GetDirectoryName(fullInputPath) ?? Directory.GetCurrentDirectory();

        var files = EnumerateCsFiles(fullInputPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.Error.WriteLine($"Scanning {fullInputPath} with Roslyn...");

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, documentationMode: DocumentationMode.None);
        switch (options.Mode)
        {
            case ScanMode.UnsafeContainingMembers:
                RunUnsafeContainingMembersQuery(files, displayRoot, parseOptions, options.IncludeLocalFunctions);
                break;
            case ScanMode.DebugAssertReview:
                RunDebugAssertReviewQuery(files, displayRoot, parseOptions, options.IncludeLocalFunctions);
                break;
            default:
                throw new InvalidOperationException($"Unhandled scan mode '{options.Mode}'.");
        }

        return 0;
    }

    private static void RunUnsafeContainingMembersQuery(
        IReadOnlyList<string> files,
        string displayRoot,
        CSharpParseOptions parseOptions,
        bool includeLocalFunctions)
    {
        Console.WriteLine("file\tline\tkind\tmember\tsignature\treason");

        var results = new List<UnsafeMemberRecord>();
        foreach (var file in files)
        {
            if (!TryParseFile(file, parseOptions, out var tree))
            {
                continue;
            }

            var walker = new UnsafeMemberWalker(displayRoot, tree, includeLocalFunctions);
            walker.Visit(tree.GetRoot());
            results.AddRange(walker.Results);
        }

        foreach (var result in results
            .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Line)
            .ThenBy(record => record.Member, StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"{EscapeTsv(result.RelativePath)}\t{result.Line}\t{EscapeTsv(result.Kind)}\t{EscapeTsv(result.Member)}\t{EscapeTsv(result.Signature)}\t{EscapeTsv(result.Reason)}");
        }

        Console.Error.WriteLine(
            $"Found {results.Count:N0} unsafe-containing {(includeLocalFunctions ? "declarations" : "methods")} across {files.Count:N0} C# files.");
    }

    private static void RunDebugAssertReviewQuery(
        IReadOnlyList<string> files,
        string displayRoot,
        CSharpParseOptions parseOptions,
        bool includeLocalFunctions)
    {
        Console.WriteLine("file\tline\tassert_line\tkind\tmember\tassert_condition\tsignature\tunsafe_reason");

        var results = new List<DebugAssertReviewRecord>();
        foreach (var file in files)
        {
            if (!TryParseFile(file, parseOptions, out var tree))
            {
                continue;
            }

            var walker = new DebugAssertReviewWalker(displayRoot, tree, includeLocalFunctions);
            walker.Visit(tree.GetRoot());
            results.AddRange(walker.Results);
        }

        foreach (var result in results
            .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Line)
            .ThenBy(record => record.AssertLine)
            .ThenBy(record => record.Member, StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"{EscapeTsv(result.RelativePath)}\t{result.Line}\t{result.AssertLine}\t{EscapeTsv(result.Kind)}\t{EscapeTsv(result.Member)}\t{EscapeTsv(result.AssertCondition)}\t{EscapeTsv(result.Signature)}\t{EscapeTsv(result.UnsafeReason)}");
        }

        Console.Error.WriteLine(
            $"Found {results.Count:N0} Debug.Assert review candidates in unsafe-containing {(includeLocalFunctions ? "declarations" : "methods")} across {files.Count:N0} C# files.");
    }

    private static bool TryParseFile(string file, CSharpParseOptions parseOptions, out SyntaxTree tree)
    {
        try
        {
            var sourceText = SourceText.From(File.ReadAllText(file));
            tree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, path: file);
            return true;
        }
        catch (IOException ioException)
        {
            Console.Error.WriteLine($"Warning: could not read '{file}': {ioException.Message}");
        }
        catch (UnauthorizedAccessException unauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: access denied for '{file}': {unauthorizedAccessException.Message}");
        }

        tree = null!;
        return false;
    }

    private static bool TryParseOptions(string[] args, out ScanOptions options, out string? errorMessage)
    {
        var inputPath = Directory.GetCurrentDirectory();
        var includeLocalFunctions = false;
        var mode = ScanMode.UnsafeContainingMembers;
        var pathProvided = false;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-h":
                case "--help":
                    options = new ScanOptions(inputPath, includeLocalFunctions, mode);
                    errorMessage = null;
                    return false;
                case "--include-local-functions":
                    includeLocalFunctions = true;
                    break;
                case "--debug-assert-review":
                    mode = ScanMode.DebugAssertReview;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        options = default;
                        errorMessage = $"Error: unrecognized option '{arg}'.";
                        return false;
                    }

                    if (pathProvided)
                    {
                        options = default;
                        errorMessage = "Error: supply at most one path.";
                        return false;
                    }

                    inputPath = arg;
                    pathProvided = true;
                    break;
            }
        }

        options = new ScanOptions(inputPath, includeLocalFunctions, mode);
        errorMessage = null;
        return true;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project scripts/FindCSharpUnsafeMethods -- [path]");
        Console.Error.WriteLine("  dotnet run --project scripts/FindCSharpUnsafeMethods -- [path] --debug-assert-review");
        Console.Error.WriteLine("  dotnet run --project scripts/FindCSharpUnsafeMethods -- [path] --include-local-functions");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Default query:");
        Console.Error.WriteLine("  Lists unsafe-containing members under current C# syntax:");
        Console.Error.WriteLine("    1. a member marked with the 'unsafe' modifier");
        Console.Error.WriteLine("    2. a member declared inside an unsafe type");
        Console.Error.WriteLine("    3. a safe member that contains an 'unsafe { }' block");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Review query:");
        Console.Error.WriteLine("  --debug-assert-review");
        Console.Error.WriteLine("    Restricts results to unsafe-containing members that call Debug.Assert");
        Console.Error.WriteLine("    without any matching if-condition for that same assertion.");
    }

    private static IEnumerable<string> EnumerateCsFiles(string path)
    {
        if (File.Exists(path))
        {
            if (string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }

            yield break;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
        };

        foreach (var file in Directory.EnumerateFiles(path, "*.cs", options))
        {
            if (ShouldSkip(file))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool ShouldSkip(string filePath)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = filePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }

        return path;
    }

    private static string EscapeTsv(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string GetSignatureText(SyntaxNode declaration)
    {
        var sourceText = declaration.SyntaxTree.GetText();
        var headerSpan = declaration switch
        {
            BaseMethodDeclarationSyntax method when method.Body is not null =>
                TextSpan.FromBounds(declaration.SpanStart, method.Body.SpanStart),
            BaseMethodDeclarationSyntax method when method.ExpressionBody is not null =>
                TextSpan.FromBounds(declaration.SpanStart, method.ExpressionBody.SpanStart),
            LocalFunctionStatementSyntax localFunction when localFunction.Body is not null =>
                TextSpan.FromBounds(declaration.SpanStart, localFunction.Body.SpanStart),
            LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody is not null =>
                TextSpan.FromBounds(declaration.SpanStart, localFunction.ExpressionBody.SpanStart),
            _ => declaration.Span,
        };

        return WhitespaceRegex.Replace(sourceText.GetSubText(headerSpan).ToString(), " ").Trim();
    }

    private static string GetMemberName(SyntaxNode declaration)
    {
        var containers = declaration.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(GetTypeName)
            .Reverse();

        var namespaces = declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(namespaceDeclaration => namespaceDeclaration.Name.ToString())
            .Reverse();

        var memberName = declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText + FormatTypeParameters(method.TypeParameterList),
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => "~" + destructor.Identifier.ValueText,
            OperatorDeclarationSyntax operatorDeclaration => $"operator {operatorDeclaration.OperatorToken.Text}",
            ConversionOperatorDeclarationSyntax conversion =>
                $"{conversion.ImplicitOrExplicitKeyword.Text} operator {conversion.Type}",
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText + FormatTypeParameters(localFunction.TypeParameterList),
            _ => declaration.GetType().Name,
        };

        return string.Join(".", namespaces.Concat(containers).Append(memberName));
    }

    private static string GetTypeName(BaseTypeDeclarationSyntax declaration)
    {
        var typeParameters = declaration switch
        {
            TypeDeclarationSyntax typeDeclaration => typeDeclaration.TypeParameterList,
            _ => null,
        };

        return declaration.Identifier.ValueText + FormatTypeParameters(typeParameters);
    }

    private static string FormatTypeParameters(TypeParameterListSyntax? typeParameterList) =>
        typeParameterList is null
            ? string.Empty
            : $"<{string.Join(", ", typeParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText))}>";

    private static string? GetUnsafeContainingReason(SyntaxNode declaration)
    {
        if (HasUnsafeModifier(declaration))
        {
            return "explicit";
        }

        var unsafeType = declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(HasUnsafeModifier);
        if (unsafeType is not null)
        {
            return $"implicit (inside unsafe {GetTypeKind(unsafeType)} {GetTypeName(unsafeType)})";
        }

        if (declaration is LocalFunctionStatementSyntax)
        {
            foreach (var ancestor in declaration.Ancestors())
            {
                switch (ancestor)
                {
                    case UnsafeStatementSyntax:
                        return "implicit (inside unsafe block)";
                    case LocalFunctionStatementSyntax localFunction when HasUnsafeModifier(localFunction):
                        return $"implicit (inside unsafe local function {localFunction.Identifier.ValueText})";
                    case BaseMethodDeclarationSyntax methodDeclaration when HasUnsafeModifier(methodDeclaration):
                        return $"implicit (inside unsafe {GetKind(methodDeclaration)})";
                }
            }
        }

        return ContainsUnsafeBlock(declaration) ? "contains unsafe block" : null;
    }

    private static bool ContainsUnsafeBlock(SyntaxNode declaration)
    {
        var searchRoot = GetSearchRoot(declaration);
        return searchRoot is not null
            && searchRoot.DescendantNodes(descendIntoChildren: ShouldDescendIntoChildren).OfType<UnsafeStatementSyntax>().Any();
    }

    private static SyntaxNode? GetSearchRoot(SyntaxNode declaration) =>
        declaration switch
        {
            BaseMethodDeclarationSyntax method when method.Body is not null => method.Body,
            BaseMethodDeclarationSyntax method when method.ExpressionBody is not null => method.ExpressionBody.Expression,
            LocalFunctionStatementSyntax localFunction when localFunction.Body is not null => localFunction.Body,
            LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody is not null => localFunction.ExpressionBody.Expression,
            _ => null,
        };

    private static bool ShouldDescendIntoChildren(SyntaxNode node) =>
        node is not AnonymousFunctionExpressionSyntax
        && node is not LocalFunctionStatementSyntax;

    private static bool HasUnsafeModifier(SyntaxNode declaration) =>
        declaration switch
        {
            BaseMethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            BaseTypeDeclarationSyntax type => type.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            LocalFunctionStatementSyntax localFunction => localFunction.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            _ => false,
        };

    private static string GetKind(SyntaxNode declaration) =>
        declaration switch
        {
            MethodDeclarationSyntax => "method",
            ConstructorDeclarationSyntax => "constructor",
            DestructorDeclarationSyntax => "destructor",
            OperatorDeclarationSyntax => "operator",
            ConversionOperatorDeclarationSyntax => "conversion",
            LocalFunctionStatementSyntax => "local function",
            _ => "member",
        };

    private static string GetTypeKind(BaseTypeDeclarationSyntax declaration) =>
        declaration switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record struct",
            RecordDeclarationSyntax => "record",
            _ => "type",
        };

    private static bool TryGetDebugAssertCondition(InvocationExpressionSyntax invocation, out ExpressionSyntax condition)
    {
        condition = null!;
        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "Assert", StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsDebugReceiver(memberAccess.Expression))
            {
                return false;
            }

            condition = StripParentheses(invocation.ArgumentList.Arguments[0].Expression);
            return true;
        }

        if (invocation.Expression is IdentifierNameSyntax identifierName
            && string.Equals(identifierName.Identifier.ValueText, "Assert", StringComparison.Ordinal)
            && HasUsingStaticDebug(invocation))
        {
            condition = StripParentheses(invocation.ArgumentList.Arguments[0].Expression);
            return true;
        }

        return false;
    }

    private static bool HasUsingStaticDebug(SyntaxNode node)
    {
        var compilationUnit = node.SyntaxTree.GetRoot() as CompilationUnitSyntax;
        if (compilationUnit is null)
        {
            return false;
        }

        static bool IsDebugUsing(UsingDirectiveSyntax usingDirective) =>
            usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
            && string.Equals(
                usingDirective.Name?.ToString().Replace("global::", "", StringComparison.Ordinal),
                "System.Diagnostics.Debug",
                StringComparison.Ordinal);

        if (compilationUnit.Usings.Any(IsDebugUsing))
        {
            return true;
        }

        foreach (var namespaceDeclaration in node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
        {
            if (namespaceDeclaration.Usings.Any(IsDebugUsing))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDebugReceiver(ExpressionSyntax expression)
    {
        var receiverText = WhitespaceRegex.Replace(expression.WithoutTrivia().ToString(), string.Empty)
            .Replace("global::", string.Empty, StringComparison.Ordinal);

        return string.Equals(receiverText, "Debug", StringComparison.Ordinal)
            || receiverText.EndsWith(".Debug", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetComparableConditions(ExpressionSyntax expression)
    {
        yield return NormalizeCondition(expression);
        yield return NegateCondition(expression);
    }

    private static string NormalizeCondition(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);

        if (expression is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return $"!({NormalizeCondition(prefix.Operand)})";
        }

        if (expression is BinaryExpressionSyntax binary)
        {
            var left = NormalizeCondition(binary.Left);
            var right = NormalizeCondition(binary.Right);
            var operatorText = binary.OperatorToken.ValueText;

            if (IsCommutative(binary.Kind()) && string.CompareOrdinal(left, right) > 0)
            {
                (left, right) = (right, left);
            }

            return $"({left}{operatorText}{right})";
        }

        return WhitespaceRegex.Replace(expression.WithoutTrivia().ToString(), string.Empty);
    }

    private static string NegateCondition(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);

        if (expression is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return NormalizeCondition(prefix.Operand);
        }

        if (expression is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(SyntaxKind.TrueLiteralExpression))
            {
                return "false";
            }

            if (literal.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                return "true";
            }
        }

        if (expression is BinaryExpressionSyntax binary && TryGetNegatedBinaryKind(binary.Kind(), out var negatedKind))
        {
            var negated = SyntaxFactory.BinaryExpression(
                negatedKind,
                StripParentheses(binary.Left),
                StripParentheses(binary.Right));

            return NormalizeCondition(negated);
        }

        return $"!({NormalizeCondition(expression)})";
    }

    private static bool IsCommutative(SyntaxKind kind) =>
        kind is SyntaxKind.EqualsExpression
            or SyntaxKind.NotEqualsExpression
            or SyntaxKind.LogicalAndExpression
            or SyntaxKind.LogicalOrExpression;

    private static bool TryGetNegatedBinaryKind(SyntaxKind kind, out SyntaxKind negatedKind)
    {
        switch (kind)
        {
            case SyntaxKind.EqualsExpression:
                negatedKind = SyntaxKind.NotEqualsExpression;
                return true;
            case SyntaxKind.NotEqualsExpression:
                negatedKind = SyntaxKind.EqualsExpression;
                return true;
            case SyntaxKind.LessThanExpression:
                negatedKind = SyntaxKind.GreaterThanOrEqualExpression;
                return true;
            case SyntaxKind.LessThanOrEqualExpression:
                negatedKind = SyntaxKind.GreaterThanExpression;
                return true;
            case SyntaxKind.GreaterThanExpression:
                negatedKind = SyntaxKind.LessThanOrEqualExpression;
                return true;
            case SyntaxKind.GreaterThanOrEqualExpression:
                negatedKind = SyntaxKind.LessThanExpression;
                return true;
            default:
                negatedKind = SyntaxKind.None;
                return false;
        }
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespace();

    private enum ScanMode
    {
        UnsafeContainingMembers,
        DebugAssertReview,
    }

    private readonly record struct ScanOptions(
        string InputPath,
        bool IncludeLocalFunctions,
        ScanMode Mode);

    private readonly record struct UnsafeMemberRecord(
        string RelativePath,
        int Line,
        string Kind,
        string Member,
        string Signature,
        string Reason);

    private readonly record struct DebugAssertReviewRecord(
        string RelativePath,
        int Line,
        int AssertLine,
        string Kind,
        string Member,
        string AssertCondition,
        string Signature,
        string UnsafeReason);

    private readonly record struct AssertCandidate(
        int Line,
        string ConditionText,
        ExpressionSyntax ConditionSyntax);

    private sealed class UnsafeMemberWalker : CSharpSyntaxWalker
    {
        private readonly string _rootPath;
        private readonly SyntaxTree _tree;
        private readonly bool _includeLocalFunctions;

        public UnsafeMemberWalker(string rootPath, SyntaxTree tree, bool includeLocalFunctions)
            : base(SyntaxWalkerDepth.Node)
        {
            _rootPath = rootPath;
            _tree = tree;
            _includeLocalFunctions = includeLocalFunctions;
        }

        public List<UnsafeMemberRecord> Results { get; } = new();

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            RecordIfUnsafeContaining(node);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            RecordIfUnsafeContaining(node);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            RecordIfUnsafeContaining(node);
            base.VisitDestructorDeclaration(node);
        }

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            RecordIfUnsafeContaining(node);
            base.VisitOperatorDeclaration(node);
        }

        public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            RecordIfUnsafeContaining(node);
            base.VisitConversionOperatorDeclaration(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (_includeLocalFunctions)
            {
                RecordIfUnsafeContaining(node);
            }

            base.VisitLocalFunctionStatement(node);
        }

        private void RecordIfUnsafeContaining(SyntaxNode declaration)
        {
            var reason = GetUnsafeContainingReason(declaration);
            if (reason is null)
            {
                return;
            }

            Results.Add(new UnsafeMemberRecord(
                Path.GetRelativePath(_rootPath, _tree.FilePath),
                _tree.GetLineSpan(declaration.Span).StartLinePosition.Line + 1,
                GetKind(declaration),
                GetMemberName(declaration),
                GetSignatureText(declaration),
                reason));
        }
    }

    private sealed class DebugAssertReviewWalker : CSharpSyntaxWalker
    {
        private readonly string _rootPath;
        private readonly SyntaxTree _tree;
        private readonly bool _includeLocalFunctions;

        public DebugAssertReviewWalker(string rootPath, SyntaxTree tree, bool includeLocalFunctions)
            : base(SyntaxWalkerDepth.Node)
        {
            _rootPath = rootPath;
            _tree = tree;
            _includeLocalFunctions = includeLocalFunctions;
        }

        public List<DebugAssertReviewRecord> Results { get; } = new();

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            RecordReviewCandidates(node);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            RecordReviewCandidates(node);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            RecordReviewCandidates(node);
            base.VisitDestructorDeclaration(node);
        }

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            RecordReviewCandidates(node);
            base.VisitOperatorDeclaration(node);
        }

        public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            RecordReviewCandidates(node);
            base.VisitConversionOperatorDeclaration(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (_includeLocalFunctions)
            {
                RecordReviewCandidates(node);
            }

            base.VisitLocalFunctionStatement(node);
        }

        private void RecordReviewCandidates(SyntaxNode declaration)
        {
            var unsafeReason = GetUnsafeContainingReason(declaration);
            if (unsafeReason is null)
            {
                return;
            }

            var searchRoot = GetSearchRoot(declaration);
            if (searchRoot is null)
            {
                return;
            }

            var ifConditions = searchRoot
                .DescendantNodes(descendIntoChildren: ShouldDescendIntoChildren)
                .OfType<IfStatementSyntax>()
                .Select(ifStatement => NormalizeCondition(ifStatement.Condition))
                .Where(condition => !string.IsNullOrEmpty(condition))
                .ToHashSet(StringComparer.Ordinal);

            var assertCandidates = searchRoot
                .DescendantNodes(descendIntoChildren: ShouldDescendIntoChildren)
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => TryGetAssertCandidate(invocation))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!.Value)
                .ToList();

            if (assertCandidates.Count == 0)
            {
                return;
            }

            foreach (var assertCandidate in assertCandidates)
            {
                var hasMatchingIf = GetComparableConditions(assertCandidate.ConditionSyntax)
                    .Any(condition => ifConditions.Contains(condition));

                if (hasMatchingIf)
                {
                    continue;
                }

                Results.Add(new DebugAssertReviewRecord(
                    Path.GetRelativePath(_rootPath, _tree.FilePath),
                    _tree.GetLineSpan(declaration.Span).StartLinePosition.Line + 1,
                    assertCandidate.Line,
                    GetKind(declaration),
                    GetMemberName(declaration),
                    assertCandidate.ConditionText,
                    GetSignatureText(declaration),
                    unsafeReason));
            }
        }

        private AssertCandidate? TryGetAssertCandidate(InvocationExpressionSyntax invocation)
        {
            if (!TryGetDebugAssertCondition(invocation, out var condition))
            {
                return null;
            }

            return new AssertCandidate(
                _tree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1,
                WhitespaceRegex.Replace(condition.WithoutTrivia().ToString(), " ").Trim(),
                condition);
        }
    }
}
