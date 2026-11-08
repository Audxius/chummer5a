using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Single-pass rewrite of one document:
///  - bodies of methods being moved: implicit member access becomes objThis.X / C.X, `this` becomes objThis, and the
///    declaration is turned into an extension method and annotated for extraction;
///  - everywhere else: unqualified calls to moved instance methods become this.M(...), references to moved static
///    methods are re-pointed at the new extension class;
///  - declarations of members that moved code needs are widened to internal.
/// All semantic queries use the original (pre-rewrite) nodes, which CSharpSyntaxRewriter passes to each Visit method.
/// </summary>
internal sealed class MoveRewriter : CSharpSyntaxRewriter
{
    public const string MovedAnnotation = "UiExtractor.Moved";

    private readonly SemanticModel _objModel;
    private readonly Dictionary<IMethodSymbol, MethodDeclarationSyntax> _dicMoved;
    private readonly HashSet<ISymbol> _setToWiden;
    private IMethodSymbol _objCurrentMoved;
    private int _intAnnotationId;

    public Dictionary<string, INamedTypeSymbol> MovedOwners { get; } = new Dictionary<string, INamedTypeSymbol>();

    public MoveRewriter(SemanticModel objModel, Dictionary<IMethodSymbol, MethodDeclarationSyntax> dicMoved, HashSet<ISymbol> setToWiden)
        : base(visitIntoStructuredTrivia: false)
    {
        _objModel = objModel;
        _dicMoved = dicMoved;
        _setToWiden = setToWiden;
    }

    private bool IsMoved(ISymbol objSymbol, out IMethodSymbol objMethod)
    {
        objMethod = objSymbol as IMethodSymbol;
        if (objMethod == null)
            return false;
        if (objMethod.ReducedFrom != null)
            objMethod = objMethod.ReducedFrom;
        objMethod = objMethod.OriginalDefinition;
        return _dicMoved.ContainsKey(objMethod);
    }

    // ---------------- declarations ----------------

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        IMethodSymbol objSymbol = _objModel.GetDeclaredSymbol(node);
        bool blnMoving = objSymbol != null && _dicMoved.ContainsKey(objSymbol.OriginalDefinition);
        IMethodSymbol objPrevious = _objCurrentMoved;
        if (blnMoving)
            _objCurrentMoved = objSymbol;
        MethodDeclarationSyntax objNew = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
        _objCurrentMoved = objPrevious;

        if (!blnMoving)
            return WidenIfNeeded(objSymbol, objNew, objNew.Modifiers, (n, m) => ((MethodDeclarationSyntax)n).WithModifiers(m));

        // Convert to extension/static method in the UI class.
        bool blnWasPublic = objSymbol.DeclaredAccessibility == Accessibility.Public;
        List<SyntaxToken> lstModifiers = new List<SyntaxToken>
        {
            SyntaxFactory.Token(blnWasPublic ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(SyntaxFactory.Space),
        };
        foreach (SyntaxToken objToken in objNew.Modifiers)
        {
            if (objToken.IsKind(SyntaxKind.AsyncKeyword) || objToken.IsKind(SyntaxKind.UnsafeKeyword))
                lstModifiers.Add(objToken.WithLeadingTrivia().WithTrailingTrivia(SyntaxFactory.Space));
        }
        SyntaxTriviaList objOriginalLeading = objNew.GetLeadingTrivia();
        objNew = objNew.WithModifiers(SyntaxFactory.TokenList(lstModifiers));
        objNew = objNew.WithLeadingTrivia(objOriginalLeading);
        if (!objSymbol.IsStatic)
        {
            ParameterSyntax objThisParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("objThis"))
                                                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ThisKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
                                                        .WithType(SyntaxFactory.ParseTypeName(objSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty))
                                                                               .WithTrailingTrivia(SyntaxFactory.Space));
            SeparatedSyntaxList<ParameterSyntax> lstParams = objNew.ParameterList.Parameters.Insert(0, objThisParam);
            objNew = objNew.WithParameterList(objNew.ParameterList.WithParameters(lstParams));
        }
        string strId = (++_intAnnotationId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        MovedOwners[strId] = objSymbol.ContainingType;
        return objNew.WithAdditionalAnnotations(new SyntaxAnnotation(MovedAnnotation, strId));
    }

    internal static SyntaxTriviaList StripDirectives(SyntaxTriviaList objTrivia)
    {
        // Drop #region/#endregion etc. (they stay behind in the source file) and any blank lines before the doc comment.
        List<SyntaxTrivia> lstKept = objTrivia.Where(x => !x.IsDirective && !x.IsKind(SyntaxKind.DisabledTextTrivia)).ToList();
        while (lstKept.Count > 0 && (lstKept[0].IsKind(SyntaxKind.EndOfLineTrivia) || lstKept[0].IsKind(SyntaxKind.WhitespaceTrivia) && lstKept.Count > 1 && lstKept[1].IsKind(SyntaxKind.EndOfLineTrivia)))
            lstKept.RemoveAt(0);
        return SyntaxFactory.TriviaList(lstKept);
    }

    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        SyntaxNode objNew = base.VisitFieldDeclaration(node);
        ISymbol objSymbol = node.Declaration.Variables.Select(x => _objModel.GetDeclaredSymbol(x)).FirstOrDefault(x => x != null && _setToWiden.Contains(x));
        return WidenIfNeeded(objSymbol, objNew, ((FieldDeclarationSyntax)objNew).Modifiers, (n, m) => ((FieldDeclarationSyntax)n).WithModifiers(m));
    }

    public override SyntaxNode VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        SyntaxNode objNew = base.VisitEventFieldDeclaration(node);
        ISymbol objSymbol = node.Declaration.Variables.Select(x => _objModel.GetDeclaredSymbol(x)).FirstOrDefault(x => x != null && _setToWiden.Contains(x));
        return WidenIfNeeded(objSymbol, objNew, ((EventFieldDeclarationSyntax)objNew).Modifiers, (n, m) => ((EventFieldDeclarationSyntax)n).WithModifiers(m));
    }

    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        IPropertySymbol objSymbol = _objModel.GetDeclaredSymbol(node);
        PropertyDeclarationSyntax objNew = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
        bool blnPropertyWidened = objSymbol != null && _setToWiden.Contains(objSymbol);
        objNew = (PropertyDeclarationSyntax)WidenIfNeeded(objSymbol, objNew, objNew.Modifiers, (n, m) => ((PropertyDeclarationSyntax)n).WithModifiers(m));
        if (objNew.AccessorList != null && objSymbol != null)
        {
            Accessibility eProperty = blnPropertyWidened ? Widened(objSymbol.DeclaredAccessibility) : objSymbol.DeclaredAccessibility;
            SyntaxList<AccessorDeclarationSyntax> lstAccessors = objNew.AccessorList.Accessors;
            for (int i = 0; i < lstAccessors.Count; ++i)
            {
                AccessorDeclarationSyntax objAccessor = lstAccessors[i];
                if (objAccessor.Modifiers.Count == 0)
                    continue;
                IMethodSymbol objAccessorSymbol = objAccessor.Keyword.IsKind(SyntaxKind.GetKeyword) ? objSymbol.GetMethod : objSymbol.SetMethod;
                Accessibility eAccessor = objAccessorSymbol?.DeclaredAccessibility ?? Accessibility.NotApplicable;
                if (objAccessorSymbol != null && _setToWiden.Contains(objAccessorSymbol))
                    eAccessor = Widened(eAccessor);
                // An accessor modifier must be strictly more restrictive than the property.
                if (Rank(eAccessor) >= Rank(eProperty))
                    lstAccessors = lstAccessors.Replace(objAccessor, objAccessor.WithModifiers(default).WithKeyword(objAccessor.Keyword.WithLeadingTrivia(objAccessor.Modifiers.First().LeadingTrivia)));
                else if (objAccessorSymbol != null && _setToWiden.Contains(objAccessorSymbol))
                    lstAccessors = lstAccessors.Replace(objAccessor, objAccessor.WithModifiers(ModifiersFor(eAccessor, objAccessor.Modifiers)));
            }
            objNew = objNew.WithAccessorList(objNew.AccessorList.WithAccessors(lstAccessors));
        }
        return objNew;
    }

    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        INamedTypeSymbol objSymbol = _objModel.GetDeclaredSymbol(node);
        SyntaxNode objNew = base.VisitClassDeclaration(node);
        return WidenIfNeeded(objSymbol, objNew, ((ClassDeclarationSyntax)objNew).Modifiers, (n, m) => ((ClassDeclarationSyntax)n).WithModifiers(m));
    }

    public override SyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        INamedTypeSymbol objSymbol = _objModel.GetDeclaredSymbol(node);
        SyntaxNode objNew = base.VisitEnumDeclaration(node);
        return WidenIfNeeded(objSymbol, objNew, ((EnumDeclarationSyntax)objNew).Modifiers, (n, m) => ((EnumDeclarationSyntax)n).WithModifiers(m));
    }

    public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
    {
        INamedTypeSymbol objSymbol = _objModel.GetDeclaredSymbol(node);
        SyntaxNode objNew = base.VisitStructDeclaration(node);
        return WidenIfNeeded(objSymbol, objNew, ((StructDeclarationSyntax)objNew).Modifiers, (n, m) => ((StructDeclarationSyntax)n).WithModifiers(m));
    }

    public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        IMethodSymbol objSymbol = _objModel.GetDeclaredSymbol(node);
        SyntaxNode objNew = base.VisitConstructorDeclaration(node);
        return WidenIfNeeded(objSymbol, objNew, ((ConstructorDeclarationSyntax)objNew).Modifiers, (n, m) => ((ConstructorDeclarationSyntax)n).WithModifiers(m));
    }

    private SyntaxNode WidenIfNeeded(ISymbol objSymbol, SyntaxNode objNode, SyntaxTokenList lstModifiers, Func<SyntaxNode, SyntaxTokenList, SyntaxNode> funcWith)
    {
        if (objSymbol == null || !_setToWiden.Contains(objSymbol.OriginalDefinition))
            return objNode;
        Accessibility eNew = Widened(objSymbol.DeclaredAccessibility);
        SyntaxTokenList lstNew = ModifiersFor(eNew, lstModifiers);
        if (lstModifiers.Count == 0)
        {
            // Implicitly private: the new modifier takes over the node's leading trivia (indentation, doc comments).
            SyntaxTriviaList objLeading = objNode.GetLeadingTrivia();
            objNode = objNode.WithoutLeadingTrivia();
            lstNew = SyntaxFactory.TokenList(lstNew.Select((x, i) => i == 0 ? x.WithLeadingTrivia(objLeading) : x));
        }
        return funcWith(objNode, lstNew);
    }

    private static Accessibility Widened(Accessibility eOld)
    {
        switch (eOld)
        {
            case Accessibility.Private:
            case Accessibility.ProtectedAndInternal:
                return Accessibility.Internal;
            case Accessibility.Protected:
                return Accessibility.ProtectedOrInternal;
            default:
                return eOld;
        }
    }

    private static int Rank(Accessibility eAccessibility)
    {
        switch (eAccessibility)
        {
            case Accessibility.Private: return 0;
            case Accessibility.ProtectedAndInternal: return 1;
            case Accessibility.Protected: return 2;
            case Accessibility.Internal: return 2;
            case Accessibility.ProtectedOrInternal: return 3;
            case Accessibility.Public: return 4;
            default: return 4;
        }
    }

    private static SyntaxTokenList ModifiersFor(Accessibility eAccessibility, SyntaxTokenList lstOld)
    {
        SyntaxKind[] aeAccessKinds = { SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.InternalKeyword };
        List<SyntaxToken> lstAccess = new List<SyntaxToken>();
        switch (eAccessibility)
        {
            case Accessibility.Internal:
                lstAccess.Add(SyntaxFactory.Token(SyntaxKind.InternalKeyword));
                break;
            case Accessibility.ProtectedOrInternal:
                lstAccess.Add(SyntaxFactory.Token(SyntaxKind.ProtectedKeyword));
                lstAccess.Add(SyntaxFactory.Token(SyntaxKind.InternalKeyword));
                break;
            case Accessibility.Public:
                lstAccess.Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                break;
            default:
                return lstOld;
        }
        SyntaxTriviaList objLeading = lstOld.Count > 0 ? lstOld[0].LeadingTrivia : default;
        List<SyntaxToken> lstRest = lstOld.Where(x => !aeAccessKinds.Contains(x.Kind())).ToList();
        List<SyntaxToken> lstAll = new List<SyntaxToken>();
        foreach (SyntaxToken objToken in lstAccess.Concat(lstRest))
            lstAll.Add(objToken.WithLeadingTrivia().WithTrailingTrivia(SyntaxFactory.Space));
        if (lstAll.Count > 0)
            lstAll[0] = lstAll[0].WithLeadingTrivia(objLeading);
        return SyntaxFactory.TokenList(lstAll);
    }

    // ---------------- expressions ----------------

    public override SyntaxNode VisitThisExpression(ThisExpressionSyntax node)
    {
        if (_objCurrentMoved != null && !_objCurrentMoved.IsStatic)
            return SyntaxFactory.IdentifierName("objThis").WithTriviaFrom(node);
        return base.VisitThisExpression(node);
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // C.M(...) where M (static) moved: re-point at the new class.
        ISymbol objSymbol = Extractor.GetSymbol(_objModel, node.Name);
        if (IsMoved(objSymbol, out IMethodSymbol objMoved) && objMoved.IsStatic && !(objSymbol is IMethodSymbol m && m.ReducedFrom != null))
        {
            ISymbol objReceiver = Extractor.GetSymbol(_objModel, node.Expression);
            if (objReceiver is INamedTypeSymbol)
            {
                MemberAccessExpressionSyntax objNew = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node);
                return objNew.WithExpression(SyntaxFactory.IdentifierName(Extractor.ExtensionClassName(objMoved.ContainingType)).WithTriviaFrom(objNew.Expression));
            }
        }
        return base.VisitMemberAccessExpression(node);
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node) => RewriteSimpleName(node) ?? base.VisitIdentifierName(node);

    public override SyntaxNode VisitGenericName(GenericNameSyntax node)
    {
        SyntaxNode objNew = base.VisitGenericName(node);
        return RewriteSimpleName(node, (SimpleNameSyntax)objNew) ?? objNew;
    }

    private SyntaxNode RewriteSimpleName(SimpleNameSyntax node, SimpleNameSyntax objRewritten = null)
    {
        objRewritten = objRewritten ?? node;
        if (!IsImplicitReceiver(node))
            return null;
        ISymbol objSymbol = Extractor.GetSymbol(_objModel, node);
        if (objSymbol == null)
            return null;
        if (!(objSymbol is IFieldSymbol || objSymbol is IPropertySymbol || objSymbol is IMethodSymbol || objSymbol is IEventSymbol))
            return null;
        if (objSymbol is IMethodSymbol objMethodSymbol && objMethodSymbol.MethodKind == MethodKind.LocalFunction)
            return null;
        INamedTypeSymbol objOwner = objSymbol.ContainingType;
        if (objOwner == null)
            return null;
        bool blnMoved = IsMoved(objSymbol, out IMethodSymbol objMovedMethod);

        if (_objCurrentMoved != null)
        {
            INamedTypeSymbol objCurrentType = _objCurrentMoved.ContainingType;
            if (!Extractor.SelfAndBaseTypes(objCurrentType).Any(x => SymbolEqualityComparer.Default.Equals(x, objOwner.OriginalDefinition) || SymbolEqualityComparer.Default.Equals(x, objOwner)))
                return null;
            if (!objSymbol.IsStatic)
            {
                if (_objCurrentMoved.IsStatic)
                    return null; // cannot happen in valid code
                return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                            SyntaxFactory.IdentifierName("objThis"), objRewritten.WithoutTrivia())
                                    .WithTriviaFrom(node);
            }
            if (blnMoved)
            {
                // Static method moved along: same new class if same owner, otherwise qualify with the other new class.
                if (SymbolEqualityComparer.Default.Equals(objMovedMethod.ContainingType, objCurrentType))
                    return null;
                return Qualify(Extractor.ExtensionClassName(objMovedMethod.ContainingType), objRewritten, node);
            }
            return Qualify(objOwner.ToMinimalDisplayString(_objModel, node.SpanStart), objRewritten, node);
        }

        // Outside moved code: only references to moved methods need fixing.
        if (!blnMoved)
            return null;
        if (objMovedMethod.IsStatic)
            return Qualify(Extractor.ExtensionClassName(objMovedMethod.ContainingType), objRewritten, node);
        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.ThisExpression(), objRewritten.WithoutTrivia())
                            .WithTriviaFrom(node);
    }

    private static SyntaxNode Qualify(string strQualifier, SimpleNameSyntax objName, SyntaxNode objOriginal)
    {
        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.ParseExpression(strQualifier), objName.WithoutTrivia())
                            .WithTriviaFrom(objOriginal);
    }

    /// <summary>
    /// True if the name is used without an explicit receiver (so `this.`/static qualification is implied).
    /// </summary>
    private static bool IsImplicitReceiver(SimpleNameSyntax node)
    {
        SyntaxNode objParent = node.Parent;
        switch (objParent)
        {
            case MemberAccessExpressionSyntax objAccess when objAccess.Name == node:
            case MemberBindingExpressionSyntax _:
            case QualifiedNameSyntax objQualified when objQualified.Right == node:
            case NameColonSyntax _:
            case NameEqualsSyntax _:
            case AliasQualifiedNameSyntax _:
            case TypeArgumentListSyntax _:
                return false;
            case AssignmentExpressionSyntax objAssignment when objAssignment.Left == node && objAssignment.Parent is InitializerExpressionSyntax:
                return false; // object initializer member
        }
        // Names used as types are never members.
        return !(objParent is VariableDeclarationSyntax || objParent is ParameterSyntax || objParent is ObjectCreationExpressionSyntax
                 || objParent is TypeOfExpressionSyntax || objParent is CastExpressionSyntax c && c.Type == node || objParent is BaseTypeSyntax);
    }
}
