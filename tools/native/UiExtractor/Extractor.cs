using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

internal static class Extractor
{
    private static string s_strBackendDir;
    private static string s_strServicesDir;
    private static string s_strFormsDir;
    private static string s_strControlsDir;
    private static string s_strOutputDir;

    public static async Task<int> Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: UiExtractor <repo root> [--dry-run]");
            return 2;
        }
        string strRepo = Path.GetFullPath(args[0]);
        bool blnDryRun = args.Contains("--dry-run");
        string strChummer = Path.Combine(strRepo, "Chummer");
        s_strBackendDir = Path.Combine(strChummer, "Backend") + Path.DirectorySeparatorChar;
        s_strServicesDir = Path.Combine(s_strBackendDir, "Services") + Path.DirectorySeparatorChar;
        s_strFormsDir = Path.Combine(strChummer, "Forms") + Path.DirectorySeparatorChar;
        s_strControlsDir = Path.Combine(strChummer, "Controls") + Path.DirectorySeparatorChar;
        s_strOutputDir = Path.Combine(s_strFormsDir, "UiExtensions");

        Dictionary<string, string> dicProps = new Dictionary<string, string>
        {
            ["Configuration"] = "Release",
            ["EnableWindowsTargeting"] = "true",
            ["SolutionDir"] = strRepo + Path.DirectorySeparatorChar,
            ["GenerateResourceUsePreserializedResources"] = "true",
            ["PreBuildEvent"] = string.Empty,
            ["PostBuildEvent"] = string.Empty,
        };
        using MSBuildWorkspace objWorkspace = MSBuildWorkspace.Create(dicProps);
        objWorkspace.WorkspaceFailed += (s, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine("workspace: " + e.Diagnostic.Message);
        };
        Project objProject = await objWorkspace.OpenProjectAsync(Path.Combine(strChummer, "Chummer.csproj")).ConfigureAwait(false);
        Compilation objCompilation = await objProject.GetCompilationAsync().ConfigureAwait(false);
        List<Diagnostic> lstErrors = objCompilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToList();
        if (lstErrors.Count > 0)
        {
            Console.Error.WriteLine(lstErrors.Count + " compile errors; refusing to refactor an inconsistent semantic model:");
            foreach (Diagnostic objError in lstErrors.Take(20))
                Console.Error.WriteLine("  " + objError);
            return 1;
        }

        if (args.Contains("--analyze-dialogs"))
        {
            DialogAnalyzer.Run(objCompilation, s_strBackendDir, s_strFormsDir);
            return 0;
        }

        // ---------- Phase A: analysis ----------
        Dictionary<IMethodSymbol, MethodDeclarationSyntax> dicCandidates = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
        List<string> lstSkipped = new List<string>();
        foreach (SyntaxTree objTree in objCompilation.SyntaxTrees)
        {
            string strPath = objTree.FilePath;
            if (!strPath.StartsWith(s_strBackendDir, StringComparison.Ordinal) || strPath.StartsWith(s_strServicesDir, StringComparison.Ordinal))
                continue;
            SemanticModel objModel = objCompilation.GetSemanticModel(objTree);
            foreach (MethodDeclarationSyntax objDecl in objTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!(objModel.GetDeclaredSymbol(objDecl) is IMethodSymbol objMethod))
                    continue;
                if (!HasUiSignature(objMethod))
                    continue;
                string strSkipReason = GetSkipReason(objMethod, objDecl, objModel);
                if (strSkipReason != null)
                {
                    lstSkipped.Add(Describe(objMethod) + ": " + strSkipReason);
                    continue;
                }
                dicCandidates[objMethod] = objDecl;
            }
        }

        // Name collisions with members that stay behind would silently change overload resolution: iterate to a fixpoint.
        bool blnChanged = true;
        while (blnChanged)
        {
            blnChanged = false;
            foreach (IMethodSymbol objMethod in dicCandidates.Keys.ToList())
            {
                if (objMethod.ContainingType.IsStatic)
                    continue; // calls into static classes are re-pointed explicitly, no overload resolution hazard
                bool blnCollision = SelfAndBaseTypes(objMethod.ContainingType)
                                    .SelectMany(x => x.GetMembers(objMethod.Name))
                                    .Any(x => !(x is IMethodSymbol m && dicCandidates.ContainsKey(m.OriginalDefinition)) && !SymbolEqualityComparer.Default.Equals(x, objMethod));
                if (blnCollision)
                {
                    dicCandidates.Remove(objMethod);
                    lstSkipped.Add(Describe(objMethod) + ": name collides with a member that stays in the engine");
                    blnChanged = true;
                }
            }
        }

        // Members the moved bodies touch that are not accessible from another class.
        HashSet<ISymbol> setToWiden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (KeyValuePair<IMethodSymbol, MethodDeclarationSyntax> kvp in dicCandidates)
        {
            SemanticModel objModel = objCompilation.GetSemanticModel(kvp.Value.SyntaxTree);
            foreach (SimpleNameSyntax objName in kvp.Value.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                ISymbol objSymbol = GetSymbol(objModel, objName);
                CollectWidening(objSymbol, objName, setToWiden);
            }
        }

        Console.WriteLine($"Moving {dicCandidates.Count} methods out of the engine; widening {setToWiden.Count} members; skipped {lstSkipped.Count}.");
        foreach (string strSkip in lstSkipped.OrderBy(x => x, StringComparer.Ordinal))
            Console.WriteLine("  skip " + strSkip);

        // ---------- Phase B: rewrite ----------
        Dictionary<INamedTypeSymbol, List<MethodDeclarationSyntax>> dicExtracted = new Dictionary<INamedTypeSymbol, List<MethodDeclarationSyntax>>(SymbolEqualityComparer.Default);
        Dictionary<INamedTypeSymbol, CompilationUnitSyntax> dicSourceUnits = new Dictionary<INamedTypeSymbol, CompilationUnitSyntax>(SymbolEqualityComparer.Default);
        Dictionary<INamedTypeSymbol, string> dicNewlines = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        int intFilesChanged = 0;
        foreach (SyntaxTree objTree in objCompilation.SyntaxTrees)
        {
            if (string.IsNullOrEmpty(objTree.FilePath) || !File.Exists(objTree.FilePath))
                continue;
            SemanticModel objModel = objCompilation.GetSemanticModel(objTree);
            SyntaxNode objOldRoot = objTree.GetRoot();
            MoveRewriter objRewriter = new MoveRewriter(objModel, dicCandidates, setToWiden);
            SyntaxNode objNewRoot = objRewriter.Visit(objOldRoot);
            if (objNewRoot == objOldRoot)
                continue;

            // Pull out the methods being moved (annotated by the rewriter).
            List<MethodDeclarationSyntax> lstMoved = objNewRoot.GetAnnotatedNodes(MoveRewriter.MovedAnnotation).OfType<MethodDeclarationSyntax>().ToList();
            string strNewline = objOldRoot.ToFullString().Contains("\r\n") ? "\r\n" : "\n";
            foreach (MethodDeclarationSyntax objMoved in lstMoved)
            {
                INamedTypeSymbol objType = objRewriter.MovedOwners[objMoved.GetAnnotations(MoveRewriter.MovedAnnotation).First().Data];
                if (!dicExtracted.TryGetValue(objType, out List<MethodDeclarationSyntax> lstForType))
                {
                    lstForType = new List<MethodDeclarationSyntax>();
                    dicExtracted.Add(objType, lstForType);
                    dicSourceUnits[objType] = (CompilationUnitSyntax)objOldRoot;
                    dicNewlines[objType] = strNewline;
                }
                lstForType.Add(objMoved.WithLeadingTrivia(MoveRewriter.StripDirectives(objMoved.GetLeadingTrivia())));
            }
            if (lstMoved.Count > 0)
                objNewRoot = objNewRoot.RemoveNodes(lstMoved, SyntaxRemoveOptions.KeepDirectives);

            intFilesChanged++;
            if (!blnDryRun)
                File.WriteAllText(objTree.FilePath, objNewRoot.ToFullString(), new UTF8Encoding(HasBom(objTree.FilePath)));
        }

        foreach (KeyValuePair<INamedTypeSymbol, List<MethodDeclarationSyntax>> kvp in dicExtracted)
        {
            string strFile = WriteExtensionClass(kvp.Key, kvp.Value, dicSourceUnits[kvp.Key], dicNewlines[kvp.Key], blnDryRun);
            Console.WriteLine($"  {kvp.Value.Count,3} -> {Path.GetRelativePath(strRepo, strFile)}");
        }
        Console.WriteLine($"Rewrote {intFilesChanged} files{(blnDryRun ? " (dry run, nothing written)" : string.Empty)}.");
        return 0;
    }

    private static bool HasBom(string strPath)
    {
        byte[] abytStart = new byte[3];
        using (FileStream objStream = File.OpenRead(strPath))
        {
            if (objStream.Read(abytStart, 0, 3) < 3)
                return false;
        }
        return abytStart[0] == 0xEF && abytStart[1] == 0xBB && abytStart[2] == 0xBF;
    }

    internal static string ExtensionClassName(INamedTypeSymbol objType)
    {
        StringBuilder sbdName = new StringBuilder();
        for (INamedTypeSymbol objLoop = objType; objLoop != null; objLoop = objLoop.ContainingType)
            sbdName.Insert(0, objLoop.Name);
        return sbdName + "UiExtensions";
    }

    private static string WriteExtensionClass(INamedTypeSymbol objType, List<MethodDeclarationSyntax> lstMethods, CompilationUnitSyntax objSourceUnit,
        string strNewline, bool blnDryRun)
    {
        string strSourcePath = objType.Locations.First(x => x.IsInSource && x.SourceTree.FilePath.StartsWith(s_strBackendDir, StringComparison.Ordinal)).SourceTree.FilePath;
        string strRelativeDir = Path.GetDirectoryName(Path.GetRelativePath(s_strBackendDir, strSourcePath)) ?? string.Empty;
        string strClassName = ExtensionClassName(objType);
        string strOutPath = Path.Combine(s_strOutputDir, strRelativeDir, strClassName + ".cs");

        StringBuilder sbdFile = new StringBuilder();
        // Keep the source file's leading trivia (license header) and usings.
        string strHeader = objSourceUnit.GetLeadingTrivia().ToFullString();
        int intUsingsStart = strHeader.Length;
        sbdFile.Append(strHeader.TrimEnd()).Append(strNewline).Append(strNewline);
        foreach (UsingDirectiveSyntax objUsing in objSourceUnit.Usings)
            sbdFile.Append(objUsing.WithoutTrivia().ToFullString()).Append(strNewline);
        if (!objSourceUnit.Usings.Any(x => x.Name?.ToString() == "System.Windows.Forms"))
            sbdFile.Append("using System.Windows.Forms;").Append(strNewline);
        sbdFile.Append(strNewline);
        string strNamespace = objType.ContainingNamespace.IsGlobalNamespace ? null : objType.ContainingNamespace.ToDisplayString();
        string strIndent = strNamespace == null ? string.Empty : "    ";
        if (strNamespace != null)
            sbdFile.Append("namespace ").Append(strNamespace).Append(strNewline).Append('{').Append(strNewline);
        sbdFile.Append(strIndent).Append("/// <summary>").Append(strNewline)
               .Append(strIndent).Append("/// UI (WinForms) members of <see cref=\"").Append(objType.ToDisplayString().Replace('<', '{').Replace('>', '}'))
               .Append("\"/>, moved out of the engine for the native port.").Append(strNewline)
               .Append(strIndent).Append("/// </summary>").Append(strNewline)
               .Append(strIndent).Append("public static class ").Append(strClassName).Append(strNewline)
               .Append(strIndent).Append('{').Append(strNewline);
        bool blnFirst = true;
        foreach (MethodDeclarationSyntax objMethod in lstMethods)
        {
            if (!blnFirst)
                sbdFile.Append(strNewline);
            blnFirst = false;
            string strMethod = objMethod.ToFullString();
            // Original members sit at depth 2 (namespace + class); nested types are deeper. Re-indent to depth 2.
            sbdFile.Append(Reindent(strMethod, strIndent + "    ", strNewline).TrimEnd()).Append(strNewline);
        }
        sbdFile.Append(strIndent).Append('}').Append(strNewline);
        if (strNamespace != null)
            sbdFile.Append('}').Append(strNewline);

        if (!blnDryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(strOutPath));
            if (File.Exists(strOutPath))
                throw new InvalidOperationException(strOutPath + " already exists; run the tool on a clean tree.");
            File.WriteAllText(strOutPath, sbdFile.ToString(), new UTF8Encoding(false));
        }
        return strOutPath;
    }

    private static string Reindent(string strMethod, string strTargetIndent, string strNewline)
    {
        string[] astrLines = strMethod.Replace("\r\n", "\n").Split('\n');
        // Find the indentation of the declaration line (first non-blank, non-directive line).
        int intCurrent = -1;
        foreach (string strLine in astrLines)
        {
            if (strLine.Trim().Length == 0)
                continue;
            intCurrent = strLine.Length - strLine.TrimStart(' ').Length;
            break;
        }
        if (intCurrent < 0)
            return strMethod;
        int intDelta = strTargetIndent.Length - intCurrent;
        StringBuilder sbdReturn = new StringBuilder();
        foreach (string strLine in astrLines)
        {
            string strOut = strLine;
            if (strLine.Trim().Length > 0)
            {
                int intLead = strLine.Length - strLine.TrimStart(' ').Length;
                int intNew = Math.Max(0, intLead + intDelta);
                strOut = new string(' ', intNew) + strLine.Substring(intLead);
            }
            sbdReturn.Append(strOut).Append(strNewline);
        }
        return sbdReturn.ToString();
    }

    private static string Describe(IMethodSymbol objMethod) => objMethod.ContainingType.ToDisplayString() + "." + objMethod.Name;

    internal static IEnumerable<INamedTypeSymbol> SelfAndBaseTypes(INamedTypeSymbol objType)
    {
        for (INamedTypeSymbol objLoop = objType; objLoop != null; objLoop = objLoop.BaseType)
            yield return objLoop;
    }

    internal static ISymbol GetSymbol(SemanticModel objModel, SyntaxNode objNode)
    {
        SymbolInfo objInfo = objModel.GetSymbolInfo(objNode);
        return objInfo.Symbol ?? (objInfo.CandidateSymbols.Length == 1 ? objInfo.CandidateSymbols[0] : null);
    }

    private static bool IsSourceUnder(ISymbol objSymbol, string strDir)
    {
        return objSymbol.Locations.Any(x => x.IsInSource && x.SourceTree.FilePath.StartsWith(strDir, StringComparison.Ordinal));
    }

    internal static bool IsUiType(ITypeSymbol objType, HashSet<ITypeSymbol> setSeen = null)
    {
        if (objType == null)
            return false;
        setSeen ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        if (!setSeen.Add(objType))
            return false;
        switch (objType)
        {
            case IArrayTypeSymbol objArray:
                return IsUiType(objArray.ElementType, setSeen);
            case IPointerTypeSymbol objPointer:
                return IsUiType(objPointer.PointedAtType, setSeen);
            case ITypeParameterSymbol objParam:
                return objParam.ConstraintTypes.Any(x => IsUiType(x, setSeen));
            case INamedTypeSymbol objNamed:
            {
                string strNamespace = objNamed.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (strNamespace == "System.Windows" || strNamespace.StartsWith("System.Windows.", StringComparison.Ordinal))
                    return true;
                if (IsSourceUnder(objNamed, s_strFormsDir) || IsSourceUnder(objNamed, s_strControlsDir))
                    return true;
                if (objNamed.TypeArguments.Any(x => IsUiType(x, setSeen)))
                    return true;
                // Engine-side wrappers around UI types (e.g. ThreadSafeForm<T> where T : Form) count as UI too.
                if (objNamed.TypeParameters.Any(x => x.ConstraintTypes.Any(y => IsUiType(y, setSeen))))
                    return true;
                return objNamed.BaseType != null && objNamed.BaseType.SpecialType != SpecialType.System_Object && IsUiType(objNamed.BaseType, setSeen);
            }
        }
        return false;
    }

    private static bool HasUiSignature(IMethodSymbol objMethod)
    {
        if (IsUiType(objMethod.ReturnType))
            return true;
        if (objMethod.Parameters.Any(x => IsUiType(x.Type)))
            return true;
        return objMethod.TypeParameters.Any(x => x.ConstraintTypes.Any(y => IsUiType(y)));
    }

    private static string GetSkipReason(IMethodSymbol objMethod, MethodDeclarationSyntax objDecl, SemanticModel objModel)
    {
        INamedTypeSymbol objType = objMethod.ContainingType;
        if (objType.TypeKind != TypeKind.Class)
            return "containing type is not a class";
        if (objType.IsGenericType)
            return "containing type is generic";
        if (IsUiType(objType))
            return "containing type is itself a UI type (move the whole file instead)";
        if (objMethod.IsAbstract || objMethod.IsVirtual || objMethod.IsOverride || objMethod.IsExtern || objMethod.IsPartialDefinition || objMethod.PartialImplementationPart != null)
            return "abstract/virtual/override/extern/partial";
        if (objMethod.ExplicitInterfaceImplementations.Length > 0)
            return "explicit interface implementation";
        if (objType.AllInterfaces.SelectMany(x => x.GetMembers()).Any(x => SymbolEqualityComparer.Default.Equals(objType.FindImplementationForInterfaceMember(x), objMethod)))
            return "implements an interface member";
        if (objDecl.DescendantNodes().OfType<BaseExpressionSyntax>().Any())
            return "uses base.";
        if (objDecl.Body == null && objDecl.ExpressionBody == null)
            return "has no body";
        if (objType.IsStatic && !objMethod.IsStatic)
            return "instance member of static class";
        return null;
    }

    private static void CollectWidening(ISymbol objSymbol, SimpleNameSyntax objName, HashSet<ISymbol> setToWiden)
    {
        if (objSymbol == null)
            return;
        if (objSymbol is IMethodSymbol objMethodSymbol && objMethodSymbol.ReducedFrom != null)
            objSymbol = objMethodSymbol.ReducedFrom;
        objSymbol = objSymbol.OriginalDefinition;
        if (!(objSymbol is IFieldSymbol || objSymbol is IPropertySymbol || objSymbol is IMethodSymbol || objSymbol is IEventSymbol || objSymbol is INamedTypeSymbol))
            return;
        if (!objSymbol.Locations.Any(x => x.IsInSource))
            return;
        if (objSymbol is IMethodSymbol objMethod && objMethod.MethodKind != MethodKind.Ordinary)
        {
            // Property accessors (via IPropertySymbol below), constructors etc.
            if (objMethod.MethodKind == MethodKind.Constructor && IsRestricted(objMethod.DeclaredAccessibility))
                setToWiden.Add(objMethod);
            return;
        }
        if (IsRestricted(objSymbol.DeclaredAccessibility))
            setToWiden.Add(objSymbol);
        if (objSymbol is IPropertySymbol objProperty)
        {
            if (objProperty.GetMethod != null && IsRestricted(objProperty.GetMethod.DeclaredAccessibility))
                setToWiden.Add(objProperty.GetMethod);
            if (objProperty.SetMethod != null && IsRestricted(objProperty.SetMethod.DeclaredAccessibility))
                setToWiden.Add(objProperty.SetMethod);
        }
        // Containing types must be reachable too (private nested classes).
        for (INamedTypeSymbol objOuter = objSymbol.ContainingType; objOuter != null; objOuter = objOuter.ContainingType)
        {
            if (IsRestricted(objOuter.DeclaredAccessibility))
                setToWiden.Add(objOuter);
        }
    }

    private static bool IsRestricted(Accessibility eAccessibility)
    {
        return eAccessibility == Accessibility.Private || eAccessibility == Accessibility.Protected || eAccessibility == Accessibility.ProtectedAndInternal;
    }
}
