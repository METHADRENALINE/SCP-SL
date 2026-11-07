namespace SecretAPI.SourceGenerators.Builders;

internal abstract class Builder<TBuilder>
    where TBuilder : Builder<TBuilder>
{
    protected readonly List<SyntaxToken> _modifiers = new();

    internal TBuilder AddModifiers(params SyntaxKind[] modifiers)
    {
        foreach (SyntaxKind token in modifiers)
            _modifiers.Add(Token(token));

        return (TBuilder)this;
    }
}
