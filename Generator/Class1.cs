using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator]
public sealed class SandboxGenerator : IIncrementalGenerator
{
    private sealed class ItemInfo : IEquatable<ItemInfo>
    {
        public ItemInfo(ClassDeclarationSyntax syntax, ITypeSymbol symbol)
        {
            Syntax = syntax;
            Symbol = symbol;
        }

        public ClassDeclarationSyntax Syntax { get; }
        public ITypeSymbol Symbol { get; }

        public override int GetHashCode() => HashCode.Combine(Syntax.GetHashCode(), SymbolEqualityComparer.Default.GetHashCode(Symbol));
        public override bool Equals(object? obj) => obj is ItemInfo other && Equals(other);
        public bool Equals(ItemInfo? other)
        {
            return other is not null
                && Syntax == other.Syntax
                && SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol);
        }
        public static bool operator ==(ItemInfo? lhs, ItemInfo? rhs) => lhs is null ? rhs is null : lhs.Equals(rhs);
        public static bool operator !=(ItemInfo? lhs, ItemInfo? rhs) => !(lhs == rhs);
    }

    public void Initialize(IncrementalGeneratorInitializationContext builder)
    {
        builder.RegisterPostInitializationOutput(GenerateAttribute);

        var typesToProcess = builder.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SandboxAttribute",
                static (node, ct) => node is ClassDeclarationSyntax,
                static (info, ct) => info.TargetSymbol is ITypeSymbol typeSymbol
                    ? new ItemInfo((ClassDeclarationSyntax)info.TargetNode, typeSymbol)
                    : null!)
            .Where(static x => x is not null)
            .Collect();

        builder.RegisterSourceOutput(typesToProcess, GenerateImplementation);
    }

    private void GenerateAttribute(IncrementalGeneratorPostInitializationContext builder)
    {
        builder.AddSource("Attribute.cs", """
            [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
            internal sealed class SandboxAttribute : Attribute { }
            """);
    }

    private void GenerateImplementation(SourceProductionContext builder, ImmutableArray<ItemInfo> infoList)
    {
        var code = new StringBuilder();

        foreach (var group in infoList.GroupBy(static x => x.Symbol.ContainingNamespace?.ToDisplayString() ?? "").OrderBy(x => x.Key))
        {
            if (!string.IsNullOrEmpty(group.Key))
                code.AppendLine($"namespace {group.Key}\n{{");

            foreach (var info in group)
            {
                var members = new List<string>();

                foreach (var member in info.Symbol.GetTypeMembers("Reference").Single().GetMembers())
                {
                    if (member is IMethodSymbol method)
                    {
                        if (!method.IsStatic && method.MethodKind == MethodKind.Ordinary && method.CanBeReferencedByName)
                        {
                            var visibility = method.DeclaredAccessibility switch
                            {
                                Accessibility.Public => "public",
                                Accessibility.Private => "private",
                                _ => throw new NotImplementedException()
                            };
                            var resultType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            var args = new List<string>();

                            foreach (var arg in method.Parameters)
                                args.Add($"{arg.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {arg.Name}");

                            members.Add($$"""
                                {{visibility}} partial {{resultType}} {{method.Name}}({{string.Join(", ", args)}})
                                {
                                    throw new global::System.NotImplementedException();
                                }
                                """);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(group.Key))
                    code.Append("    ");
                code.AppendLine($$"""
                    partial class {{info.Symbol.Name}}
                    {
                        new public partial struct Reference
                        {
                            {{string.Join("\n", members).Replace("\n", "\n        ")}}
                        }
                    }
                    """.Replace("\n", string.IsNullOrEmpty(group.Key) ? "\n" : "\n    "));
            }

            if (!string.IsNullOrEmpty(group.Key))
                code.AppendLine("}");
        }

        code.Replace("\r\n", "\n");

        if (code.Length > 0)
            builder.AddSource("Implementation.cs", code.ToString());
    }
}
