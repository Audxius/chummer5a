/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.LanguageManager"/>, moved out of the engine for the native port.
    /// </summary>
    public static class LanguageManagerUiExtensions
    {
        /// <summary>
        /// Translate an object int a specified language.
        /// </summary>
        /// <param name="objObject">Object to translate.</param>
        /// <param name="strIntoLanguage">Language to which to translate the object.</param>
        /// <param name="blnDoResumeLayout">Whether to suspend and then resume the control being translated.</param>
        /// <param name="token">Cancellation token to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void TranslateWinForm(this Control objObject, string strIntoLanguage = "",
                                            bool blnDoResumeLayout = true, CancellationToken token = default)
        {
            // Use RunOnMainThread here because we don't want redraws while we translate a form
            Utils.RunOnMainThread(t => TranslateWinFormCoreAsync(true, objObject, strIntoLanguage, blnDoResumeLayout, t), token: token);
        }

        /// <summary>
        /// Translate an object int a specified language.
        /// </summary>
        /// <param name="objObject">Object to translate.</param>
        /// <param name="strIntoLanguage">Language to which to translate the object.</param>
        /// <param name="blnDoResumeLayout">Whether to suspend and then resume the control being translated.</param>
        /// <param name="token">Cancellation token to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Task TranslateWinFormAsync(this Control objObject, string strIntoLanguage = "",
                                                 bool blnDoResumeLayout = true, CancellationToken token = default)
        {
            return TranslateWinFormCoreAsync(false, objObject, strIntoLanguage, blnDoResumeLayout, token);
        }

        /// <summary>
        /// Translate an object int a specified language.
        /// Uses flag hack method design outlined here to avoid locking:
        /// https://docs.microsoft.com/en-us/archive/msdn-magazine/2015/july/async-programming-brownfield-async-development
        /// </summary>
        /// <param name="blnSync">Flag for whether method should always use synchronous code or not.</param>
        /// <param name="objObject">Object to translate.</param>
        /// <param name="strIntoLanguage">Language to which to translate the object.</param>
        /// <param name="blnDoResumeLayout">Whether to suspend and then resume the control being translated.</param>
        /// <param name="token">Cancellation token to use.</param>
        internal static async Task TranslateWinFormCoreAsync(bool blnSync, Control objObject, string strIntoLanguage,
                                                            bool blnDoResumeLayout, CancellationToken token = default)
        {
            if (Utils.IsDesignerMode || Utils.IsRunningInVisualStudio)
                return;
            if (blnDoResumeLayout)
            {
                if (blnSync)
                    // ReSharper disable once MethodHasAsyncOverload
                    // ReSharper disable once MethodHasAsyncOverloadWithCancellation
                    objObject.DoThreadSafe(x => x.SuspendLayout(), token);
                else
                    await objObject.DoThreadSafeAsync(x => x.SuspendLayout(), token).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(strIntoLanguage))
                strIntoLanguage = GlobalSettings.Language;
            bool blnLanguageLoaded = blnSync
                // ReSharper disable once MethodHasAsyncOverload
                ? LanguageManager.LoadLanguage(strIntoLanguage, token)
                : await LanguageManager.LoadLanguageAsync(strIntoLanguage, token).ConfigureAwait(false);
            if (blnLanguageLoaded)
            {
                RightToLeft eIntoRightToLeft = RightToLeft.No;
                string strKey = strIntoLanguage.ToUpperInvariant();
                if (LanguageManager.LoadedLanguageData.TryGetValue(strKey, out LanguageData objLanguageData))
                    eIntoRightToLeft = objLanguageData.IsRightToLeftScript ? RightToLeft.Yes : RightToLeft.No;

                if (blnSync)
                    // ReSharper disable once MethodHasAsyncOverload
                    UpdateControls(objObject, strIntoLanguage, eIntoRightToLeft, token);
                else
                    await UpdateControlsAsync(objObject, strIntoLanguage, eIntoRightToLeft, token).ConfigureAwait(false);
            }
            else if (!strIntoLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                if (blnSync)
                    // ReSharper disable once MethodHasAsyncOverload
                    UpdateControls(objObject, GlobalSettings.DefaultLanguage, RightToLeft.No, token);
                else
                    await UpdateControlsAsync(objObject, GlobalSettings.DefaultLanguage, RightToLeft.No, token).ConfigureAwait(false);
            }

            if (blnDoResumeLayout)
            {
                if (blnSync)
                    // ReSharper disable once MethodHasAsyncOverload
                    // ReSharper disable once MethodHasAsyncOverloadWithCancellation
                    objObject.DoThreadSafe(x => x.ResumeLayout(), CancellationToken.None);
                else
                    await objObject.DoThreadSafeAsync(x => x.ResumeLayout(), CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Recursive method to translate all of the controls in a Form or UserControl.
        /// </summary>
        /// <param name="objParent">Control container to translate.</param>
        /// <param name="strIntoLanguage">Language into which the control should be translated</param>
        /// <param name="eIntoRightToLeft">Whether <paramref name="strIntoLanguage" /> is a right-to-left language</param>
        /// <param name="token">CancellationToken to listen to.</param>
        internal static void UpdateControls(Control objParent, string strIntoLanguage, RightToLeft eIntoRightToLeft,
                                           CancellationToken token = default)
        {
            if (objParent == null)
                return;

            objParent.DoThreadSafe(x =>
            {
                try
                {
                    x.RightToLeft = eIntoRightToLeft;
                }
                catch (NotSupportedException)
                {
                    if (x.GetType() != typeof(WebBrowser))
                        Utils.BreakIfDebug();
                }
            }, token);

            if (objParent is Form frmForm)
            {
                frmForm.DoThreadSafe((x, y) =>
                {
                    // Translatable items are identified by having a value in their Tag attribute. The contents of Tag is the string to lookup in the language list.
                    // Update the Form itself.
                    string strControlTag = x.Tag?.ToString();
                    if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                             && !strControlTag.IsGuid() && !File.Exists(strControlTag))
                        x.Text = LanguageManager.GetString(strControlTag, strIntoLanguage, token: y);
                    else if (x.Text.StartsWith('['))
                        x.Text = string.Empty;

                    // update any menu strip items that have tags
                    if (x.MainMenuStrip != null)
                        foreach (ToolStripMenuItem tssItem in x.MainMenuStrip.Items)
                            TranslateToolStripItemsRecursively(tssItem, strIntoLanguage, eIntoRightToLeft, y);
                }, token);
            }

            // Translatable items are identified by having a value in their Tag attribute. The contents of Tag is the string to lookup in the language list.
            foreach (Control objChild in objParent.DoThreadSafeFunc(x => x.Controls, token))
            {
                objChild.DoThreadSafe(x =>
                {
                    try
                    {
                        x.RightToLeft = eIntoRightToLeft;
                    }
                    catch (NotSupportedException)
                    {
                        if (x.GetType() != typeof(WebBrowser))
                            Utils.BreakIfDebug();
                    }
                }, token);

                switch (objChild)
                {
                    case Label _:
                    case Button _:
                    case CheckBox _:
                    {
                        objChild.DoThreadSafe((x, y) =>
                        {
                            string strControlTag = x.Tag?.ToString();
                            if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                     && !strControlTag.IsGuid()
                                                                     && !File.Exists(strControlTag))
                                x.Text = LanguageManager.GetString(strControlTag, strIntoLanguage, token: y);
                            else if (x.Text.StartsWith('['))
                                x.Text = string.Empty;
                        }, token);
                        break;
                    }
                    case ToolStrip tssStrip:
                    {
                        tssStrip.DoThreadSafe((x, y) =>
                        {
                            foreach (ToolStripItem tssItem in x.Items)
                                TranslateToolStripItemsRecursively(tssItem, strIntoLanguage, eIntoRightToLeft, y);
                        }, token);

                        break;
                    }
                    case ListView lstList:
                    {
                        lstList.DoThreadSafe((x, y) =>
                        {
                            foreach (ColumnHeader objHeader in x.Columns)
                            {
                                string strControlTag = objHeader.Tag?.ToString();
                                if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                         && !strControlTag.IsGuid()
                                                                         && !File.Exists(strControlTag))
                                    objHeader.Text = LanguageManager.GetString(strControlTag, strIntoLanguage, token: y);
                                else if (objHeader.Text.StartsWith('['))
                                    objHeader.Text = string.Empty;
                            }
                        }, token);

                        break;
                    }
                    case TabControl objTabControl:
                    {
                        foreach (TabPage tabPage in objTabControl.DoThreadSafeFunc(x => x.TabPages, token))
                        {
                            tabPage.DoThreadSafe((x, y) =>
                            {
                                string strControlTag = x.Tag?.ToString();
                                if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                         && !strControlTag.IsGuid()
                                                                         && !File.Exists(strControlTag))
                                    x.Text = LanguageManager.GetString(strControlTag, strIntoLanguage, token: y);
                                else if (x.Text.StartsWith('['))
                                    x.Text = string.Empty;
                            }, token);
                            UpdateControls(tabPage, strIntoLanguage, eIntoRightToLeft, token);
                        }

                        break;
                    }
                    case SplitContainer objSplitControl:
                        UpdateControls(objSplitControl.DoThreadSafeFunc(x => x.Panel1, token), strIntoLanguage,
                                       eIntoRightToLeft, token);
                        UpdateControls(objSplitControl.DoThreadSafeFunc(x => x.Panel2, token), strIntoLanguage,
                                       eIntoRightToLeft, token);
                        break;

                    case GroupBox _:
                    {
                        objChild.DoThreadSafe((x, t) =>
                        {
                            string strControlTag = x.Tag?.ToString();
                            if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                     && !strControlTag.IsGuid()
                                                                     && !File.Exists(strControlTag))
                                x.Text = LanguageManager.GetString(strControlTag, strIntoLanguage, token: t);
                            else if (x.Text.StartsWith('['))
                                x.Text = string.Empty;
                        }, token);
                        UpdateControls(objChild, strIntoLanguage, eIntoRightToLeft, token);
                        break;
                    }
                    case Panel _:
                        UpdateControls(objChild, strIntoLanguage, eIntoRightToLeft, token);
                        break;

                    case TreeView treTree:
                    {
                        treTree.DoThreadSafe((x, y) =>
                        {
                            foreach (TreeNode objNode in x.Nodes)
                            {
                                if (objNode.Level == 0)
                                {
                                    string strControlTag = objNode.Tag?.ToString();
                                    if (!string.IsNullOrEmpty(strControlTag)
                                        && strControlTag.StartsWith("Node_", StringComparison.Ordinal))
                                    {
                                        objNode.Text = LanguageManager.GetString(strControlTag, strIntoLanguage, token: y);
                                    }
                                    else if (objNode.Text.StartsWith('['))
                                        objNode.Text = string.Empty;
                                }
                                else if (objNode.Text.StartsWith('['))
                                    objNode.Text = string.Empty;
                            }
                        }, token);

                        break;
                    }
                    case DataGridView objDataGridView:
                    {
                        objDataGridView.DoThreadSafe((x, y) =>
                        {
                            foreach (DataGridViewTextBoxColumn objColumn in x.Columns)
                            {
                                if (objColumn is DataGridViewTextBoxColumnTranslated objTranslatedColumn
                                    && !string.IsNullOrWhiteSpace(objTranslatedColumn.TranslationTag))
                                {
                                    objColumn.HeaderText
                                        = LanguageManager.GetString(objTranslatedColumn.TranslationTag, strIntoLanguage, token: y);
                                }
                            }
                        }, token);

                        break;
                    }
                    case ITranslatable translatable:
                        // let custom nodes determine how they want to be translated
                        translatable.Translate(token);
                        break;
                }
            }
        }

        /// <summary>
        /// Recursive method to translate all of the controls in a Form or UserControl.
        /// </summary>
        /// <param name="objParent">Control container to translate.</param>
        /// <param name="strIntoLanguage">Language into which the control should be translated</param>
        /// <param name="eIntoRightToLeft">Whether <paramref name="strIntoLanguage" /> is a right-to-left language</param>
        /// <param name="token">CancellationToken to listen to.</param>
        internal static async Task UpdateControlsAsync(Control objParent, string strIntoLanguage, RightToLeft eIntoRightToLeft,
                                           CancellationToken token = default)
        {
            if (objParent == null)
                return;

            await objParent.DoThreadSafeAsync(x =>
            {
                try
                {
                    x.RightToLeft = eIntoRightToLeft;
                }
                catch (NotSupportedException)
                {
                    if (x.GetType() != typeof(WebBrowser))
                        Utils.BreakIfDebug();
                }
            }, token).ConfigureAwait(false);

            if (objParent is Form frmForm)
            {
                string strTagToUse = await frmForm.DoThreadSafeFuncAsync(x =>
                {
                    // Translatable items are identified by having a value in their Tag attribute. The contents of Tag is the string to lookup in the language list.
                    // Update the Form itself.
                    string strControlTag = x.Tag?.ToString();
                    if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                             && !strControlTag.IsGuid() && !File.Exists(strControlTag))
                        return strControlTag;
                    if (x.Text.StartsWith('['))
                        x.Text = string.Empty;
                    return string.Empty;
                }, token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(strTagToUse))
                {
                    string strText = await LanguageManager.GetStringAsync(strTagToUse, strIntoLanguage, false, token).ConfigureAwait(false);
                    await frmForm.DoThreadSafeAsync(x => x.Text = strText, token).ConfigureAwait(false);
                }
                // update any menu strip items that have tags
                MenuStrip objMenuStrip = await frmForm.DoThreadSafeFuncAsync(x => x.MainMenuStrip, token).ConfigureAwait(false);
                if (objMenuStrip != null)
                {
                    ToolStripItemCollection lstItems = await objMenuStrip.DoThreadSafeFuncAsync(x => x.Items, token).ConfigureAwait(false);
                    List<ValueTuple<ToolStripItem, string>> lstTagsToUse = new List<ValueTuple<ToolStripItem, string>>(lstItems.Count);
                    foreach (ToolStripItem tssItem in lstItems)
                        lstTagsToUse.AddRange(await TranslateToolStripItemsRecursivelyPrepAsync(objMenuStrip, tssItem, strIntoLanguage, eIntoRightToLeft, token).ConfigureAwait(false));
                    foreach ((ToolStripItem objControl, string strTag) in lstTagsToUse)
                    {
                        string strText = await LanguageManager.GetStringAsync(strTag, strIntoLanguage, false, token).ConfigureAwait(false);
                        await objMenuStrip.DoThreadSafeAsync(() => objControl.Text = strText, token).ConfigureAwait(false);
                    }
                }
            }

            // Translatable items are identified by having a value in their Tag attribute. The contents of Tag is the string to lookup in the language list.
            foreach (Control objChild in await objParent.DoThreadSafeFuncAsync(x => x.Controls, token).ConfigureAwait(false))
            {
                await objChild.DoThreadSafeAsync(x =>
                {
                    try
                    {
                        x.RightToLeft = eIntoRightToLeft;
                    }
                    catch (NotSupportedException)
                    {
                        if (x.GetType() != typeof(WebBrowser))
                            Utils.BreakIfDebug();
                    }
                }, token).ConfigureAwait(false);

                switch (objChild)
                {
                    case Label _:
                    case Button _:
                    case CheckBox _:
                        {
                            string strTagToUse = await objChild.DoThreadSafeFuncAsync(x =>
                            {
                                string strControlTag = x.Tag?.ToString();
                                if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                         && !strControlTag.IsGuid()
                                                                         && !File.Exists(strControlTag))
                                    return strControlTag;
                                if (x.Text.StartsWith('['))
                                    x.Text = string.Empty;
                                return string.Empty;
                            }, token).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(strTagToUse))
                            {
                                string strText = await LanguageManager.GetStringAsync(strTagToUse, strIntoLanguage, false, token).ConfigureAwait(false);
                                await objChild.DoThreadSafeAsync(x => x.Text = strText, token).ConfigureAwait(false);
                            }
                            break;
                        }
                    case ToolStrip tssStrip:
                        {
                            ToolStripItemCollection lstItems = await tssStrip.DoThreadSafeFuncAsync(x => x.Items, token).ConfigureAwait(false);
                            List<ValueTuple<ToolStripItem, string>> lstTagsToUse = new List<ValueTuple<ToolStripItem, string>>(lstItems.Count);
                            foreach (ToolStripItem tssItem in lstItems)
                                lstTagsToUse.AddRange(await TranslateToolStripItemsRecursivelyPrepAsync(tssStrip, tssItem, strIntoLanguage, eIntoRightToLeft, token).ConfigureAwait(false));
                            foreach ((ToolStripItem objControl, string strTag) in lstTagsToUse)
                            {
                                string strText = await LanguageManager.GetStringAsync(strTag, strIntoLanguage, false, token).ConfigureAwait(false);
                                await tssStrip.DoThreadSafeAsync(() => objControl.Text = strText, token).ConfigureAwait(false);
                            }
                            break;
                        }
                    case ListView lstList:
                        {
                            List<ValueTuple<ColumnHeader, string>> lstTagsToUse = new List<ValueTuple<ColumnHeader, string>>(await lstList.DoThreadSafeFuncAsync(x => x.Columns.Count, token).ConfigureAwait(false));
                            await lstList.DoThreadSafeAsync((x, t) =>
                            {
                                foreach (ColumnHeader objHeader in x.Columns)
                                {
                                    t.ThrowIfCancellationRequested();
                                    string strControlTag = objHeader.Tag?.ToString();
                                    if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                             && !strControlTag.IsGuid()
                                                                             && !File.Exists(strControlTag))
                                        lstTagsToUse.Add(new ValueTuple<ColumnHeader, string>(objHeader, strControlTag));
                                    else if (objHeader.Text.StartsWith('['))
                                        objHeader.Text = string.Empty;
                                }
                            }, token).ConfigureAwait(false);
                            foreach ((ColumnHeader objControl, string strTag) in lstTagsToUse)
                            {
                                string strText = await LanguageManager.GetStringAsync(strTag, strIntoLanguage, false, token).ConfigureAwait(false);
                                await lstList.DoThreadSafeAsync(() => objControl.Text = strText, token).ConfigureAwait(false);
                            }

                            break;
                        }
                    case TabControl objTabControl:
                        {
                            List<ValueTuple<TabPage, string>> lstTagsToUse = new List<ValueTuple<TabPage, string>>(await objTabControl.DoThreadSafeFuncAsync(x => x.TabCount, token).ConfigureAwait(false));
                            foreach (TabPage tabPage in await objTabControl.DoThreadSafeFuncAsync(x => x.TabPages, token).ConfigureAwait(false))
                            {
                                await tabPage.DoThreadSafeAsync(x =>
                                {
                                    string strControlTag = x.Tag?.ToString();
                                    if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                             && !strControlTag.IsGuid()
                                                                             && !File.Exists(strControlTag))
                                    {
                                        lstTagsToUse.Add(new ValueTuple<TabPage, string>(x, strControlTag));
                                    }
                                    else if (x.Text.StartsWith('['))
                                        x.Text = string.Empty;
                                }, token).ConfigureAwait(false);
                                await UpdateControlsAsync(tabPage, strIntoLanguage, eIntoRightToLeft, token).ConfigureAwait(false);
                            }
                            foreach ((TabPage objControl, string strTag) in lstTagsToUse)
                            {
                                string strText = await LanguageManager.GetStringAsync(strTag, strIntoLanguage, false, token).ConfigureAwait(false);
                                await objControl.DoThreadSafeAsync(x => x.Text = strText, token).ConfigureAwait(false);
                            }

                            break;
                        }
                    case SplitContainer objSplitControl:
                        await UpdateControlsAsync(await objSplitControl.DoThreadSafeFuncAsync(x => x.Panel1, token).ConfigureAwait(false), strIntoLanguage,
                                                  eIntoRightToLeft, token).ConfigureAwait(false);
                        await UpdateControlsAsync(await objSplitControl.DoThreadSafeFuncAsync(x => x.Panel2, token).ConfigureAwait(false), strIntoLanguage,
                                                  eIntoRightToLeft, token).ConfigureAwait(false);
                        break;

                    case GroupBox _:
                        {
                            string strTagToUse = await objChild.DoThreadSafeFuncAsync(x =>
                            {
                                string strControlTag = x.Tag?.ToString();
                                if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                                         && !strControlTag.IsGuid()
                                                                         && !File.Exists(strControlTag))
                                    return strControlTag;
                                if (x.Text.StartsWith('['))
                                    x.Text = string.Empty;
                                return string.Empty;
                            }, token).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(strTagToUse))
                            {
                                string strText = await LanguageManager.GetStringAsync(strTagToUse, strIntoLanguage, false, token).ConfigureAwait(false);
                                await objChild.DoThreadSafeAsync(x => x.Text = strText, token).ConfigureAwait(false);
                            }
                            await UpdateControlsAsync(objChild, strIntoLanguage, eIntoRightToLeft, token).ConfigureAwait(false);
                            break;
                        }
                    case Panel _:
                        await UpdateControlsAsync(objChild, strIntoLanguage, eIntoRightToLeft, token).ConfigureAwait(false);
                        break;

                    case TreeView treTree:
                        {
                            List<ValueTuple<TreeNode, string>> lstTagsToUse = new List<ValueTuple<TreeNode, string>>(await treTree.DoThreadSafeFuncAsync(x => x.GetNodeCount(false), token).ConfigureAwait(false));
                            await treTree.DoThreadSafeAsync((x, t) =>
                            {
                                foreach (TreeNode objNode in x.Nodes)
                                {
                                    t.ThrowIfCancellationRequested();
                                    if (objNode.Level == 0)
                                    {
                                        string strControlTag = objNode.Tag?.ToString();
                                        if (!string.IsNullOrEmpty(strControlTag)
                                            && strControlTag.StartsWith("Node_", StringComparison.Ordinal))
                                        {
                                            lstTagsToUse.Add(new ValueTuple<TreeNode, string>(objNode, strControlTag));
                                        }
                                        else if (objNode.Text.StartsWith('['))
                                            objNode.Text = string.Empty;
                                    }
                                    else if (objNode.Text.StartsWith('['))
                                        objNode.Text = string.Empty;
                                }
                            }, token).ConfigureAwait(false);
                            foreach ((TreeNode objControl, string strTag) in lstTagsToUse)
                            {
                                string strText = await LanguageManager.GetStringAsync(strTag, strIntoLanguage, false, token).ConfigureAwait(false);
                                await treTree.DoThreadSafeAsync(() => objControl.Text = strText, token).ConfigureAwait(false);
                            }

                            break;
                        }
                    case DataGridView objDataGridView:
                        {
                            List<ValueTuple<DataGridViewTextBoxColumn, string>> lstTagsToUse = new List<ValueTuple<DataGridViewTextBoxColumn, string>>(await objDataGridView.DoThreadSafeFuncAsync(x => x.ColumnCount, token).ConfigureAwait(false));
                            await objDataGridView.DoThreadSafeAsync((x, t) =>
                            {
                                foreach (DataGridViewTextBoxColumn objColumn in x.Columns)
                                {
                                    t.ThrowIfCancellationRequested();
                                    if (objColumn is DataGridViewTextBoxColumnTranslated objTranslatedColumn
                                        && !string.IsNullOrWhiteSpace(objTranslatedColumn.TranslationTag))
                                    {
                                        lstTagsToUse.Add(new ValueTuple<DataGridViewTextBoxColumn, string>(objColumn, objTranslatedColumn.TranslationTag));
                                    }
                                }
                            }, token).ConfigureAwait(false);
                            foreach ((DataGridViewTextBoxColumn objControl, string strTag) in lstTagsToUse)
                            {
                                string strText = await LanguageManager.GetStringAsync(strTag, strIntoLanguage, false, token).ConfigureAwait(false);
                                await objDataGridView.DoThreadSafeAsync(() => objControl.HeaderText = strText, token).ConfigureAwait(false);
                            }

                            break;
                        }
                    case ITranslatable translatable:
                        // let custom nodes determine how they want to be translated
                        translatable.Translate(token);
                        break;
                }
            }
        }

        /// <summary>
        /// Loads the proper language from the language file for every menu item recursively
        /// </summary>
        /// <param name="tssItem">Given ToolStripItem to translate.</param>
        /// <param name="strIntoLanguage">Language into which the ToolStripItem and all dropdown items should be translated.</param>
        /// <param name="eIntoRightToLeft">Whether <paramref name="strIntoLanguage"/> uses right-to-left script or left-to-right. If left at Inherit, then a loading function will be used to set the value.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void TranslateToolStripItemsRecursively(this ToolStripItem tssItem, string strIntoLanguage = "",
                                                              RightToLeft eIntoRightToLeft = RightToLeft.Inherit,
                                                              CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (tssItem == null)
                return;
            if (string.IsNullOrEmpty(strIntoLanguage))
                strIntoLanguage = GlobalSettings.Language;
            if (eIntoRightToLeft == RightToLeft.Inherit && LanguageManager.LoadLanguage(strIntoLanguage, token))
            {
                string strKey = strIntoLanguage.ToUpperInvariant();
                if (LanguageManager.LoadedLanguageData.TryGetValue(strKey, out LanguageData objLanguageData))
                {
                    eIntoRightToLeft = objLanguageData.IsRightToLeftScript ? RightToLeft.Yes : RightToLeft.No;
                }
            }

            tssItem.RightToLeft = eIntoRightToLeft;

            string strControlTag = tssItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                     && !strControlTag.IsGuid() && !File.Exists(strControlTag))
                tssItem.Text = LanguageManager.GetString(strControlTag, strIntoLanguage, token: token);
            else if (tssItem.Text.StartsWith('['))
                tssItem.Text = string.Empty;

            if (tssItem is ToolStripDropDownItem tssDropDownItem)
                foreach (ToolStripItem tssDropDownChild in tssDropDownItem.DropDownItems)
                    TranslateToolStripItemsRecursively(tssDropDownChild, strIntoLanguage, eIntoRightToLeft, token);
        }

        /// <summary>
        /// Loads the proper language from the language file for every menu item recursively
        /// </summary>
        /// <param name="tssBase">Base toolstrip to whose items are to be translated / actively being translated.</param>
        /// <param name="tssItem">Given ToolStripItem to translate.</param>
        /// <param name="strIntoLanguage">Language into which the ToolStripItem and all dropdown items should be translated.</param>
        /// <param name="eIntoRightToLeft">Whether <paramref name="strIntoLanguage"/> uses right-to-left script or left-to-right. If left at Inherit, then a loading function will be used to set the value.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task<List<ValueTuple<ToolStripItem, string>>> TranslateToolStripItemsRecursivelyPrepAsync(this ToolStrip tssBase, ToolStripItem tssItem, string strIntoLanguage = "",
                                                                                                                 RightToLeft eIntoRightToLeft = RightToLeft.Inherit,
                                                                                                                 CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            List<ValueTuple<ToolStripItem, string>> lstReturn = new List<ValueTuple<ToolStripItem, string>>(byte.MaxValue);
            if (tssItem == null)
                return lstReturn;
            if (string.IsNullOrEmpty(strIntoLanguage))
                strIntoLanguage = GlobalSettings.Language;
            if (eIntoRightToLeft == RightToLeft.Inherit && await LanguageManager.LoadLanguageAsync(strIntoLanguage, token).ConfigureAwait(false))
            {
                string strKey = strIntoLanguage.ToUpperInvariant();
                if (LanguageManager.LoadedLanguageData.TryGetValue(strKey, out LanguageData objLanguageData))
                {
                    eIntoRightToLeft = objLanguageData.IsRightToLeftScript ? RightToLeft.Yes : RightToLeft.No;
                }
            }

            await tssBase.DoThreadSafeAsync(() =>
            {
                tssItem.RightToLeft = eIntoRightToLeft;

                string strControlTag = tssItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(strControlTag) && !int.TryParse(strControlTag, out int _)
                                                         && !strControlTag.IsGuid() && !File.Exists(strControlTag))
                    lstReturn.Add(new ValueTuple<ToolStripItem, string>(tssItem, strControlTag));
                else if (tssItem.Text.StartsWith('['))
                    tssItem.Text = string.Empty;
            }, token).ConfigureAwait(false);

            if (tssItem is ToolStripDropDownItem tssDropDownItem)
                foreach (ToolStripItem tssDropDownChild in tssDropDownItem.DropDownItems)
                    lstReturn.AddRange(await TranslateToolStripItemsRecursivelyPrepAsync(tssBase, tssDropDownChild, strIntoLanguage, eIntoRightToLeft, token).ConfigureAwait(false));
            return lstReturn;
        }

        public static void PopulateSheetLanguageList(ElasticComboBox cboLanguage, string strSelectedSheet,
                                                     Character objCharacter,
                                                     CultureInfo defaultCulture = null,
                                                     CancellationToken token = default)
        {
            if (cboLanguage == null)
                throw new ArgumentNullException(nameof(cboLanguage));
            string strDefaultSheetLanguage = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.Language;
            int? intLastIndexDirectorySeparator = strSelectedSheet?.LastIndexOf(Path.DirectorySeparatorChar);
            if (intLastIndexDirectorySeparator.HasValue && intLastIndexDirectorySeparator != -1)
            {
                string strSheetLanguage = strSelectedSheet.Substring(0, intLastIndexDirectorySeparator.Value);
                if (strSheetLanguage.Length == 5)
                    strDefaultSheetLanguage = strSheetLanguage;
            }

            List<ListItem> lstSheetLanguageList = LanguageManager.GetSheetLanguageList(objCharacter, true, token);
            try
            {
                cboLanguage.PopulateWithListItems(lstSheetLanguageList, token: token);
                cboLanguage.DoThreadSafe(x =>
                {
                    if (!string.IsNullOrEmpty(strDefaultSheetLanguage))
                        x.SelectedValue = strDefaultSheetLanguage;
                    if (x.SelectedIndex == -1)
                        x.SelectedValue
                            = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.DefaultLanguage;
                }, token);
            }
            finally
            {
                Utils.ListItemListPool.Return(ref lstSheetLanguageList);
            }
        }

        public static void PopulateSheetLanguageList(ElasticComboBox cboLanguage, string strSelectedSheet,
                                                     IEnumerable<Character> lstCharacters,
                                                     CultureInfo defaultCulture = null,
                                                     CancellationToken token = default)
        {
            if (cboLanguage == null)
                throw new ArgumentNullException(nameof(cboLanguage));
            string strDefaultSheetLanguage = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.Language;
            int? intLastIndexDirectorySeparator = strSelectedSheet?.LastIndexOf(Path.DirectorySeparatorChar);
            if (intLastIndexDirectorySeparator.HasValue && intLastIndexDirectorySeparator != -1)
            {
                string strSheetLanguage = strSelectedSheet.Substring(0, intLastIndexDirectorySeparator.Value);
                if (strSheetLanguage.Length == 5)
                    strDefaultSheetLanguage = strSheetLanguage;
            }

            List<ListItem> lstSheetLanguageList = LanguageManager.GetSheetLanguageList(lstCharacters, true, token);
            try
            {
                cboLanguage.PopulateWithListItems(lstSheetLanguageList, token: token);
                cboLanguage.DoThreadSafe(x =>
                {
                    if (!string.IsNullOrEmpty(strDefaultSheetLanguage))
                        x.SelectedValue = strDefaultSheetLanguage;
                    if (x.SelectedIndex == -1)
                        x.SelectedValue
                            = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.DefaultLanguage;
                }, token);
            }
            finally
            {
                Utils.ListItemListPool.Return(ref lstSheetLanguageList);
            }
        }

        public static Task PopulateSheetLanguageListAsync(ElasticComboBox cboLanguage, string strSelectedSheet,
                                                          Character objCharacter = null,
                                                          CultureInfo defaultCulture = null,
                                                          CancellationToken token = default)
        {
            return cboLanguage == null
                ? Task.FromException(new ArgumentNullException(nameof(cboLanguage)))
                : PopulateSheetLanguageListAsyncInner(token);

            async Task PopulateSheetLanguageListAsyncInner(CancellationToken innerToken)
            {
                string strDefaultSheetLanguage = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.Language;
                int? intLastIndexDirectorySeparator = strSelectedSheet?.LastIndexOf(Path.DirectorySeparatorChar);
                if (intLastIndexDirectorySeparator.HasValue && intLastIndexDirectorySeparator != -1)
                {
                    string strSheetLanguage = strSelectedSheet.Substring(0, intLastIndexDirectorySeparator.Value);
                    if (strSheetLanguage.Length == 5)
                        strDefaultSheetLanguage = strSheetLanguage;
                }

                List<ListItem> lstSheetLanguageList
                    = await LanguageManager.GetSheetLanguageListAsync(objCharacter, true, innerToken).ConfigureAwait(false);
                try
                {
                    await cboLanguage.PopulateWithListItemsAsync(lstSheetLanguageList, token: innerToken)
                                     .ConfigureAwait(false);
                    await cboLanguage.DoThreadSafeAsync(x =>
                    {
                        if (!string.IsNullOrEmpty(strDefaultSheetLanguage))
                            x.SelectedValue = strDefaultSheetLanguage;
                        if (x.SelectedIndex == -1)
                            x.SelectedValue
                                = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.DefaultLanguage;
                    }, token: innerToken).ConfigureAwait(false);
                }
                finally
                {
                    Utils.ListItemListPool.Return(ref lstSheetLanguageList);
                }
            }
        }

        public static Task PopulateSheetLanguageListAsync(ElasticComboBox cboLanguage, string strSelectedSheet,
                                                          IEnumerable<Character> lstCharacters,
                                                          CultureInfo defaultCulture = null,
                                                          CancellationToken token = default)
        {
            return cboLanguage == null
                ? Task.FromException(new ArgumentNullException(nameof(cboLanguage)))
                : PopulateSheetLanguageListAsyncInner(token);

            async Task PopulateSheetLanguageListAsyncInner(CancellationToken innerToken)
            {
                string strDefaultSheetLanguage = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.Language;
                int? intLastIndexDirectorySeparator = strSelectedSheet?.LastIndexOf(Path.DirectorySeparatorChar);
                if (intLastIndexDirectorySeparator.HasValue && intLastIndexDirectorySeparator != -1)
                {
                    string strSheetLanguage = strSelectedSheet.Substring(0, intLastIndexDirectorySeparator.Value);
                    if (strSheetLanguage.Length == 5)
                        strDefaultSheetLanguage = strSheetLanguage;
                }

                List<ListItem> lstSheetLanguageList
                    = await LanguageManager.GetSheetLanguageListAsync(lstCharacters, true, innerToken).ConfigureAwait(false);
                try
                {
                    await cboLanguage.PopulateWithListItemsAsync(lstSheetLanguageList, token: innerToken)
                                     .ConfigureAwait(false);
                    await cboLanguage.DoThreadSafeAsync(x =>
                    {
                        if (!string.IsNullOrEmpty(strDefaultSheetLanguage))
                            x.SelectedValue = strDefaultSheetLanguage;
                        if (x.SelectedIndex == -1)
                            x.SelectedValue
                                = defaultCulture?.Name.ToLowerInvariant() ?? GlobalSettings.DefaultLanguage;
                    }, token: innerToken).ConfigureAwait(false);
                }
                finally
                {
                    Utils.ListItemListPool.Return(ref lstSheetLanguageList);
                }
            }
        }
    }
}
