namespace SecretAPI.SourceGenerators.Utils;

internal readonly struct MethodParameter
{
    private readonly SyntaxList<AttributeListSyntax> _attributeLists;
    private readonly SyntaxTokenList _modifiers;
    private readonly TypeSyntax? _type;
    private readonly SyntaxToken _identifier;
    private readonly EqualsValueClauseSyntax? _default;

    internal MethodParameter(
        string identifier,
        TypeSyntax? type = null,
        SyntaxTokenList modifiers = default,
        SyntaxList<AttributeListSyntax> attributeLists = default,
        EqualsValueClauseSyntax? @default = null)
    {
        _identifier = IsValidIdentifier(identifier)
            ? Identifier(identifier)
            : throw new ArgumentException("Identifier is not valid.", nameof(identifier));

        _type = type;
        _modifiers = modifiers;
        _attributeLists = attributeLists;
        _default = @default;
    }

    public ParameterSyntax Syntax => Parameter(_attributeLists, _modifiers, _type, _identifier, _default);
}
