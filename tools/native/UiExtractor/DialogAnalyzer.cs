using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// --analyze-dialogs: lists, per WinForms dialog type, every member that engine (Backend) code touches,
/// with how it is used (read / write / call). Input for designing toolkit-agnostic dialog request types.
/// </summary>
internal static class DialogAnalyzer
{
    public static void Run(Compilation objCompilation, string strBackendDir, string strFormsDir)
    {
        Dictionary<string, SortedDictionary<string, SortedSet<string>>> dicUsage = new Dictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
        foreach (SyntaxTree objTree in objCompilation.SyntaxTrees)
        {
            if (!objTree.FilePath.StartsWith(strBackendDir, StringComparison.Ordinal))
                continue;
            SemanticModel objModel = objCompilation.GetSemanticModel(objTree);
            foreach (SimpleNameSyntax objName in objTree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                ISymbol objSymbol = Extractor.GetSymbol(objModel, objName);
                if (objSymbol == null || !(objSymbol is IPropertySymbol || objSymbol is IMethodSymbol || objSymbol is IFieldSymbol || objSymbol is IEventSymbol))
                    continue;
                if (objSymbol is IMethodSymbol objMethod && objMethod.MethodKind == MethodKind.Constructor)
                    continue;
                INamedTypeSymbol objType = objSymbol.ContainingType;
                if (objType == null || !objType.Locations.Any(x => x.IsInSource && x.SourceTree.FilePath.StartsWith(strFormsDir, StringComparison.Ordinal)))
                    continue;
                if (!objType.AllInterfaces.Any() && objType.BaseType?.ToDisplayString() != "System.Windows.Forms.Form" && !InheritsForm(objType))
                    continue;
                if (!InheritsForm(objType))
                    continue;
                string strUse = GetUse(objName, objSymbol);
                string strMember = objSymbol is IMethodSymbol m
                    ? m.Name + "(" + string.Join(", ", m.Parameters.Select(p => p.Type.ToDisplayString() + " " + p.Name + (p.HasExplicitDefaultValue ? " = " + (p.ExplicitDefaultValue?.ToString() ?? "null") : string.Empty))) + ") : " + m.ReturnType.ToDisplayString()
                    : objSymbol.Name + " : " + (objSymbol is IPropertySymbol p2 ? p2.Type.ToDisplayString() : objSymbol is IFieldSymbol f ? f.Type.ToDisplayString() : ((IEventSymbol)objSymbol).Type.ToDisplayString());
                if (!dicUsage.TryGetValue(objType.Name, out SortedDictionary<string, SortedSet<string>> dicMembers))
                    dicUsage[objType.Name] = dicMembers = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                if (!dicMembers.TryGetValue(strMember, out SortedSet<string> setUses))
                    dicMembers[strMember] = setUses = new SortedSet<string>(StringComparer.Ordinal);
                setUses.Add(strUse);
            }
        }
        foreach (KeyValuePair<string, SortedDictionary<string, SortedSet<string>>> kvp in dicUsage.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(kvp.Key);
            foreach (KeyValuePair<string, SortedSet<string>> kvpMember in kvp.Value)
                Console.WriteLine("    " + kvpMember.Key + "   [" + string.Join(",", kvpMember.Value) + "]");
        }
    }

    private static bool InheritsForm(INamedTypeSymbol objType)
    {
        for (INamedTypeSymbol objLoop = objType.BaseType; objLoop != null; objLoop = objLoop.BaseType)
        {
            if (objLoop.ToDisplayString() == "System.Windows.Forms.Form")
                return true;
        }
        return false;
    }

    private static string GetUse(SimpleNameSyntax objName, ISymbol objSymbol)
    {
        if (objSymbol is IMethodSymbol)
            return "call";
        SyntaxNode objNode = objName.Parent is MemberAccessExpressionSyntax objAccess && objAccess.Name == objName ? objAccess : (SyntaxNode)objName;
        if (objNode.Parent is AssignmentExpressionSyntax objAssignment && objAssignment.Left == objNode)
            return objAssignment.Parent is InitializerExpressionSyntax ? "init" : "write";
        return "read";
    }
}
