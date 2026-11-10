/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NLog;
namespace Chummer.DataEditing
{
    // The legacy EditXmlData change summary, kept alongside the same amendment engine.
    internal sealed partial class XmlAmendmentWorker
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly XmlDocument _objDiffBaseXmlDocument = new XmlDocument { XmlResolver = null };
        private readonly XmlDocument _objDiffResultXmlDocument = new XmlDocument { XmlResolver = null };
        private async Task<string> GenerateCleanDiffAsync(string strBaseXml, string strResultXml, CancellationToken token = default)
        {
            try
            {
                // Parse both XML documents
                await _objDiffBaseXmlDocument.LoadXmlStandardAsync(strBaseXml, token: token).ConfigureAwait(false);
                await _objDiffResultXmlDocument.LoadXmlStandardAsync(strResultXml, token: token).ConfigureAwait(false);

                using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool, out StringBuilder sbdOutput))
                {
                    sbdOutput.AppendLine(await LanguageManager.GetStringAsync("XmlEditor_AmendmentChanges", token: token).ConfigureAwait(false));
                    sbdOutput.AppendLine();

                    // Compare the documents and generate a clean diff
                    bool blnAnyChanges = await CompareXmlDocumentsAsync(_objDiffBaseXmlDocument, _objDiffResultXmlDocument, sbdOutput, string.Empty, token).ConfigureAwait(false);

                    if (!blnAnyChanges)
                    {
                        sbdOutput.AppendLine(await LanguageManager.GetStringAsync("XmlEditor_NoChanges", token: token).ConfigureAwait(false));
                        sbdOutput.AppendLine(await LanguageManager.GetStringAsync("XmlEditor_NoChangesDescription", token: token).ConfigureAwait(false));
                    }

                    return sbdOutput.ToString();
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                ex = ex.Demystify();
                Log.Error(ex, "Error generating clean diff");
                return StringExtensions.FastFormat(await LanguageManager.GetStringAsync("XmlEditor_ErrorGeneratingCleanDiff", token: token).ConfigureAwait(false), ex.Message);
            }
        }

        private static async Task<bool> CompareXmlDocumentsAsync(XmlDocument objBaseDoc, XmlDocument objResultDoc, StringBuilder sbdOutput, string strCurrentPath, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                // Compare root elements
                return await CompareXmlNodesAsync(objBaseDoc.DocumentElement, objResultDoc.DocumentElement, sbdOutput, strCurrentPath, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                ex = ex.Demystify();
                Log.Error(ex, "Error comparing XML documents");
                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_ErrorComparingDocuments", token: token).ConfigureAwait(false), ex.Message).AppendLine();
            }
            return false;
        }

        private static async Task<bool> CompareXmlNodesAsync(XmlNode objBaseNode, XmlNode objResultNode, StringBuilder sbdOutput, string strCurrentPath, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            bool blnReturn = false;
            try
            {
                if (objBaseNode == null && objResultNode == null)
                    return false;

                string strNodePath = GetNodePath(objBaseNode ?? objResultNode, strCurrentPath);

                // Node was added
                if (objBaseNode == null)
                {
                    blnReturn = true;
                    sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_Added", token: token).ConfigureAwait(false), strNodePath).AppendLine();
                    sbdOutput.AppendLine("  ", await FormatXmlNode(objResultNode, token).ConfigureAwait(false));
                    return true;
                }

                // Node was removed
                if (objResultNode == null)
                {
                    blnReturn = true;
                    sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_Removed", token: token).ConfigureAwait(false), strNodePath).AppendLine();
                    sbdOutput.AppendLine("  ", await FormatXmlNode(objBaseNode, token).ConfigureAwait(false));
                    return true;
                }

                // Compare text nodes
                if (objBaseNode.NodeType == XmlNodeType.Text && objResultNode.NodeType == XmlNodeType.Text)
                {
                    if (!string.Equals(objBaseNode.Value, objResultNode.Value, StringComparison.Ordinal))
                    {
                        blnReturn = true;
                        sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_Modified", token: token).ConfigureAwait(false), strNodePath).AppendLine();
                        sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_OldValue", token: token).ConfigureAwait(false), await FormatXmlNode(objBaseNode, token).ConfigureAwait(false)).AppendLine();
                        sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_NewValue", token: token).ConfigureAwait(false), await FormatXmlNode(objResultNode, token).ConfigureAwait(false)).AppendLine();
                        return true;
                    }
                    return false;
                }

                // Compare element nodes
                if (objBaseNode.NodeType == XmlNodeType.Element && objResultNode.NodeType == XmlNodeType.Element)
                {
                    // Compare attributes
                    if (objBaseNode.Attributes != null || objResultNode.Attributes != null)
                    {
                        Dictionary<string, string> baseAttribs;
                        if (objBaseNode.Attributes != null)
                        {
                            baseAttribs = new Dictionary<string, string>(objBaseNode.Attributes.Count);
                            foreach (XmlAttribute objAttribute in objBaseNode.Attributes)
                            {
                                baseAttribs.Add(objAttribute.Name, objAttribute.Value);
                            }
                        }
                        else
                            baseAttribs = new Dictionary<string, string>(0);

                        Dictionary<string, string> resultAttribs;
                        if (objResultNode.Attributes != null)
                        {
                            resultAttribs = new Dictionary<string, string>(objResultNode.Attributes.Count);
                            foreach (XmlAttribute objAttribute in objResultNode.Attributes)
                            {
                                resultAttribs.Add(objAttribute.Name, objAttribute.Value);
                            }
                        }
                        else
                            resultAttribs = new Dictionary<string, string>(0);

                        foreach (KeyValuePair<string, string> kvp in baseAttribs)
                        {
                            if (!resultAttribs.TryGetValue(kvp.Key, out string value) || !resultAttribs.Remove(kvp.Key))
                            {
                                blnReturn = true;
                                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_RemovedAttribute", token: token).ConfigureAwait(false), strNodePath, kvp.Key, kvp.Value).AppendLine();
                            }
                            else if (!string.Equals(value, kvp.Value, StringComparison.Ordinal))
                            {
                                blnReturn = true;
                                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_ModifiedAttribute", token: token).ConfigureAwait(false), strNodePath, kvp.Key).AppendLine();
                                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_OldValue", token: token).ConfigureAwait(false), "\"" + kvp.Value + "\"").AppendLine();
                                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_NewValue", token: token).ConfigureAwait(false), "\"" + value + "\"").AppendLine();
                            }
                        }

                        blnReturn = blnReturn || resultAttribs.Count > 0;
                        foreach (KeyValuePair<string, string> kvp in resultAttribs)
                        {
                            sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_AddedAttribute", token: token).ConfigureAwait(false), strNodePath, kvp.Key, kvp.Value).AppendLine();
                        }
                    }

                    // Compare text content first (for elements with only text content)
                    using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool, out StringBuilder sbdBaseText))
                    using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool, out StringBuilder sbdResultText))
                    {
                        foreach (XmlNode objNode in objBaseNode.ChildNodes)
                        {
                            if (objNode.NodeType == XmlNodeType.Text)
                                sbdBaseText.Append(objNode.Value);
                        }
                        foreach (XmlNode objNode in objResultNode.ChildNodes)
                        {
                            if (objNode.NodeType == XmlNodeType.Text)
                                sbdResultText.Append(objNode.Value);
                        }

                        if (sbdBaseText.Length > 0 || sbdResultText.Length > 0)
                        {
                            string strBaseText = sbdBaseText.ToString();
                            string strResultText = sbdResultText.ToString();

                            if (!string.Equals(strBaseText, strResultText, StringComparison.Ordinal))
                            {
                                blnReturn = true;
                                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_Modified", token: token).ConfigureAwait(false), strNodePath).AppendLine();
                                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_OldValue", token: token).ConfigureAwait(false), "\"" + strBaseText + "\"").AppendLine();
                                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_NewValue", token: token).ConfigureAwait(false), "\"" + strResultText + "\"").AppendLine();
                            }
                        }
                    }

                    // Compare child element nodes
                    // Group children by their identifying attributes or content
                    Dictionary<string, XmlNode> baseGroups = GroupChildNodes(objBaseNode.ChildNodes);
                    Dictionary<string, XmlNode> resultGroups = GroupChildNodes(objResultNode.ChildNodes);

                    // Find added, removed, and modified nodes
                    foreach (KeyValuePair<string, XmlNode> kvp in resultGroups)
                    {
                        if (!baseGroups.TryGetValue(kvp.Key, out XmlNode value) || !baseGroups.Remove(kvp.Key))
                        {
                            blnReturn = true;
                            string strChildPath = GetNodePath(kvp.Value, strNodePath);
                            sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_Added", token: token).ConfigureAwait(false), strChildPath).AppendLine();
                            sbdOutput.AppendLine("  ", await FormatXmlNode(kvp.Value, token).ConfigureAwait(false));
                        }
                        else
                        {
                            blnReturn = await CompareXmlNodesAsync(value, kvp.Value, sbdOutput, strNodePath, token).ConfigureAwait(false) || blnReturn;
                        }
                    }

                    blnReturn = blnReturn || baseGroups.Count > 0;
                    foreach (XmlNode xmlLoopNode in baseGroups.Values)
                    {
                        string strChildPath = GetNodePath(xmlLoopNode, strNodePath);
                        sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_Removed", token: token).ConfigureAwait(false), strChildPath).AppendLine();
                        sbdOutput.AppendLine("  ", await FormatXmlNode(xmlLoopNode, token).ConfigureAwait(false));
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                ex = ex.Demystify();
                Log.Error(ex, "Error comparing XML nodes");
                sbdOutput.AppendFastFormat(await LanguageManager.GetStringAsync("XmlEditor_ErrorComparingNodes", token: token).ConfigureAwait(false ), strCurrentPath, ex.Message).AppendLine();
            }
            return blnReturn;
        }

        private static Dictionary<string, XmlNode> GroupChildNodes(XmlNodeList childNodes)
        {
            if (childNodes == null)
                return new Dictionary<string, XmlNode>(0);
            Dictionary<string, XmlNode> groups = new Dictionary<string, XmlNode>(childNodes.Count);
            foreach (XmlNode node in childNodes)
            {
                if (node.NodeType != XmlNodeType.Element)
                    continue;
                // Try to find an ID attribute first
                string strKey = node.Attributes?["id"]?.Value ?? node["id"]?.InnerText;
                if (!string.IsNullOrEmpty(strKey)) strKey = node.Name + "|id:" + strKey;

                // If no ID, try to use name attribute
                if (string.IsNullOrEmpty(strKey))
                {
                    strKey = node.Attributes?["name"]?.Value ?? node["name"]?.InnerText;
                    if (!string.IsNullOrEmpty(strKey)) strKey = node.Name + "|name:" + strKey;
                    // If still no key, use the node name with index
                    if (string.IsNullOrEmpty(strKey))
                    {
                        strKey = node.Name;
                    }
                }

                string uniqueKey = strKey;
                for (int duplicate = 1; groups.ContainsKey(uniqueKey); ++duplicate)
                    uniqueKey = strKey + "#" + duplicate.ToString(GlobalSettings.InvariantCultureInfo);
                groups[uniqueKey] = node;
            }

            return groups;
        }

        private static string GetNodePath(XmlNode node, string strCurrentPath)
        {
            if (node == null)
                return strCurrentPath;

            string strNodeName = node.Name;

            // For element nodes, try to include ID or name attribute
            if (node.NodeType == XmlNodeType.Element)
            {
                string strId = node.Attributes?["id"]?.Value ?? node["id"]?.InnerText;

                if (!string.IsNullOrEmpty(strId))
                {
                    strNodeName = strNodeName + "[id=" + strId + "]";
                }
                else
                {
                    string strName = node.Attributes?["name"]?.Value ?? node["name"]?.InnerText;
                    if (!string.IsNullOrEmpty(strName))
                    {
                        strNodeName = strNodeName + "[name=" + strName + "]";
                    }
                }
            }

            return string.IsNullOrEmpty(strCurrentPath) ? strNodeName : strCurrentPath + "/" + strNodeName;
        }

        private static async Task<string> FormatXmlNode(XmlNode xmlNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (xmlNode.NodeType == XmlNodeType.Text)
                {
                    return "\"" + xmlNode.Value + "\"";
                }

                if (xmlNode.NodeType == XmlNodeType.Element)
                {
                    // Create a temporary document to format just this node
                    using (new FetchSafelyFromSafeObjectPool<XmlDocument>(Utils.XmlDocumentPool, out XmlDocument objTempDoc))
                    {
                        XmlNode xmlImportedNode = objTempDoc.ImportNode(xmlNode, true);
                        objTempDoc.AppendChild(xmlImportedNode);

                        // Format the XML and get the inner content
                        using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool, out StringBuilder sbdReturn))
                        {
                            using (StringWriter objStringWriter = new StringWriter(sbdReturn, GlobalSettings.InvariantCultureInfo))
                            {
                                using (XmlTextWriter objXmlWriter = new XmlTextWriter(objStringWriter))
                                {
                                    objXmlWriter.Formatting = Formatting.Indented;
                                    objXmlWriter.Indentation = 2;
                                    xmlImportedNode.WriteTo(objXmlWriter);
                                }
                            }
                            return sbdReturn.ToTrimmedString();
                        }
                    }
                }

                return xmlNode.ToString();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                ex = ex.Demystify();
                Log.Error(ex, await LanguageManager.GetStringAsync("XmlEditor_ErrorFormattingNode", token: token).ConfigureAwait(false));
                return xmlNode.ToString();
            }
        }

    }
}
