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
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chummer.UI.Skills;
using Chummer.UI.Table;
using Microsoft.Win32;
using BorderStyle = System.Windows.Forms.BorderStyle;
using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;
using ListBox = System.Windows.Forms.ListBox;
using ListView = System.Windows.Forms.ListView;
using SystemColors = System.Drawing.SystemColors;
using TableCell = Chummer.UI.Table.TableCell;
using TextBox = System.Windows.Forms.TextBox;
using Timer = System.Timers.Timer;
using TreeNode = System.Windows.Forms.TreeNode;
using TreeView = System.Windows.Forms.TreeView;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.ColorManager"/>, moved out of the engine for the native port.
    /// </summary>
    public static class ColorManagerUiExtensions
    {
        public static void UpdateLightDarkMode(this Control objControl, CancellationToken token = default)
        {
            ApplyColorsRecursively(objControl, ColorManager.IsLightMode, token);
        }

        public static void UpdateLightDarkMode(this Control objControl, bool blnLightMode, CancellationToken token = default)
        {
            ApplyColorsRecursively(objControl, blnLightMode, token);
        }

        public static void UpdateLightDarkMode(this ToolStripItem tssItem, CancellationToken token = default)
        {
            ApplyColorsRecursively(tssItem, ColorManager.IsLightMode, token);
        }

        public static void UpdateLightDarkMode(this ToolStripItem tssItem, bool blnLightMode, CancellationToken token = default)
        {
            ApplyColorsRecursively(tssItem, blnLightMode, token);
        }

        public static Task UpdateLightDarkModeAsync(this Control objControl, CancellationToken token = default)
        {
            return ApplyColorsRecursivelyAsync(objControl, ColorManager.IsLightMode, token);
        }

        public static Task UpdateLightDarkModeAsync(this Control objControl, bool blnLightMode, CancellationToken token = default)
        {
            return ApplyColorsRecursivelyAsync(objControl, blnLightMode, token);
        }

        internal static void ApplyColorsRecursively(Control objControl, bool blnLightMode, CancellationToken token = default)
        {
            void ApplyButtonStyle(CancellationToken innerToken)
            {
                // Buttons look weird if colored based on anything other than the default color scheme in dark mode
                objControl.DoThreadSafe(x =>
                {
                    x.ForeColor = SystemColors.ControlText;
                    if (x is ButtonBase z)
                        z.UseVisualStyleBackColor = true;
                }, innerToken);
            }
            switch (objControl)
            {
                case DataGridView objDataGridView:
                    {
                        Color objBackgroundColor;
                        Color objForeColor;
                        Color objBackColor;
                        Color objAlternateBackColor;
                        if (blnLightMode)
                        {
                            objBackgroundColor = ColorManager.AppWorkspaceLight;
                            objForeColor = ColorManager.ControlTextLight;
                            objBackColor = ColorManager.ControlLight;
                            objAlternateBackColor = ColorManager.ControlLighterLight;
                        }
                        else
                        {
                            objBackgroundColor = ColorManager.AppWorkspaceDark;
                            objForeColor = ColorManager.ControlTextDark;
                            objBackColor = ColorManager.ControlDark;
                            objAlternateBackColor = ColorManager.ControlLighterDark;
                        }
                        objDataGridView.DoThreadSafe((x, t) =>
                        {
                            x.BackgroundColor = objBackgroundColor;
                            x.GridColor = objForeColor;
                            x.DefaultCellStyle.ForeColor = objForeColor;
                            x.DefaultCellStyle.BackColor = objBackColor;
                            x.ColumnHeadersDefaultCellStyle.ForeColor = objForeColor;
                            x.ColumnHeadersDefaultCellStyle.BackColor = objBackColor;
                            x.AlternatingRowsDefaultCellStyle.ForeColor = objForeColor;
                            x.AlternatingRowsDefaultCellStyle.BackColor = objAlternateBackColor;
                            x.RowTemplate.DefaultCellStyle.ForeColor = objForeColor;
                            x.RowTemplate.DefaultCellStyle.BackColor = objBackColor;
                            foreach (DataGridViewTextBoxColumn objColumn in x.Columns)
                            {
                                t.ThrowIfCancellationRequested();
                                objColumn.DefaultCellStyle.ForeColor = objForeColor;
                                objColumn.DefaultCellStyle.BackColor = objBackColor;
                            }
                        }, token);
                    }
                    break;

                case SplitContainer objSplitControl:
                    objSplitControl.DoThreadSafe(x =>
                    {
                        if (blnLightMode)
                        {
                            x.ForeColor = ColorManager.SplitterColorLight;
                            x.BackColor = ColorManager.SplitterColorLight;
                        }
                        else
                        {
                            x.ForeColor = ColorManager.SplitterColorDark;
                            x.BackColor = ColorManager.SplitterColorDark;
                        }
                    }, token);
                    ApplyColorsRecursively(objSplitControl.DoThreadSafeFunc(x => x.Panel1, token), blnLightMode, token);
                    ApplyColorsRecursively(objSplitControl.DoThreadSafeFunc(x => x.Panel2, token), blnLightMode, token);
                    break;

                case TreeView treControl:
                    treControl.DoThreadSafe(x =>
                    {
                        if (blnLightMode)
                        {
                            x.ForeColor = ColorManager.WindowTextLight;
                            x.BackColor = ColorManager.WindowLight;
                            x.LineColor = ColorManager.WindowTextLight;
                        }
                        else
                        {
                            x.ForeColor = ColorManager.WindowTextDark;
                            x.BackColor = ColorManager.WindowDark;
                            x.LineColor = ColorManager.WindowTextDark;
                        }
                    }, token);
                    foreach (TreeNode objNode in treControl.DoThreadSafeFunc(x => x.Nodes, token))
                        ApplyColorsRecursively(objNode, blnLightMode, token);
                    break;

                case TextBox txtControl:
                    txtControl.DoThreadSafe(x =>
                    {
                        if (x.ForeColor != ColorManager.ErrorColor)
                        {
                            if (blnLightMode)
                            {
                                x.ForeColor = ColorManager.WindowTextLight;
                                if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                                    x.BackColor = ColorManager.ControlLight;
                                else
                                    x.BackColor = ColorManager.WindowLight;
                            }
                            else
                            {
                                x.ForeColor = ColorManager.WindowTextDark;
                                if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                                    x.BackColor = ColorManager.ControlDark;
                                else
                                    x.BackColor = ColorManager.WindowDark;
                            }
                        }
                        else if (blnLightMode)
                        {
                            if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                                x.BackColor = ColorManager.ControlLight;
                            else
                                x.BackColor = ColorManager.WindowLight;
                        }
                        else if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                            x.BackColor = ColorManager.ControlDark;
                        else
                            x.BackColor = ColorManager.WindowDark;
                    }, token);
                    break;

                case ListView objListView:
                    objListView.DoThreadSafe(x =>
                    {
                        if (blnLightMode)
                        {
                            x.ForeColor = ColorManager.WindowTextLight;
                            x.BackColor = ColorManager.WindowLight;
                            foreach (DiceRollerListViewItem objItem in x.Items.OfType<DiceRollerListViewItem>())
                            {
                                if (objItem.IsHit)
                                {
                                    if (objItem.IsGlitch)
                                    {
                                        objItem.ForeColor = ColorManager.DieGlitchHitForeLight;
                                        objItem.BackColor = ColorManager.DieGlitchHitBackgroundLight;
                                    }
                                    else
                                    {
                                        objItem.ForeColor = ColorManager.DieHitForeLight;
                                        objItem.BackColor = ColorManager.DieHitBackgroundLight;
                                    }
                                }
                                else if (objItem.IsGlitch)
                                {
                                    objItem.ForeColor = ColorManager.DieGlitchForeLight;
                                    objItem.BackColor = ColorManager.DieGlitchBackgroundLight;
                                }
                                else
                                {
                                    objItem.ForeColor = ColorManager.WindowTextLight;
                                    objItem.BackColor = ColorManager.WindowLight;
                                }
                            }
                        }
                        else
                        {
                            x.ForeColor = ColorManager.WindowTextDark;
                            x.BackColor = ColorManager.WindowDark;
                            foreach (DiceRollerListViewItem objItem in x.Items.OfType<DiceRollerListViewItem>())
                            {
                                if (objItem.IsHit)
                                {
                                    if (objItem.IsGlitch)
                                    {
                                        objItem.ForeColor = ColorManager.DieGlitchHitForeDark;
                                        objItem.BackColor = ColorManager.DieGlitchHitBackgroundDark;
                                    }
                                    else
                                    {
                                        objItem.ForeColor = ColorManager.DieHitForeDark;
                                        objItem.BackColor = ColorManager.DieHitBackgroundDark;
                                    }
                                }
                                else if (objItem.IsGlitch)
                                {
                                    objItem.ForeColor = ColorManager.DieGlitchForeDark;
                                    objItem.BackColor = ColorManager.DieGlitchBackgroundDark;
                                }
                                else
                                {
                                    objItem.ForeColor = ColorManager.WindowTextDark;
                                    objItem.BackColor = ColorManager.WindowDark;
                                }
                            }
                        }
                    }, token);
                    break;

                case ListBox _:
                case ComboBox _:
                case TableCell _:
                    objControl.DoThreadSafe(x =>
                    {
                        if (blnLightMode)
                        {
                            x.ForeColor = ColorManager.WindowTextLight;
                            x.BackColor = ColorManager.WindowLight;
                        }
                        else
                        {
                            x.ForeColor = ColorManager.WindowTextDark;
                            x.BackColor = ColorManager.WindowDark;
                        }
                    }, token);
                    break;

                case GroupBox _:
                    objControl.DoThreadSafe(x =>
                    {
                        if (blnLightMode)
                        {
                            x.ForeColor = ColorManager.ControlTextLight;
                            x.BackColor = ColorManager.ControlLight;
                        }
                        else
                        {
                            x.ForeColor = ColorManager.ControlTextDark;
                            x.BackColor = ColorManager.ControlDark;
                        }
                    }, token);
                    break;

                case ContactControl _:
                case PetControl _:
                case SkillControl _:
                case KnowledgeSkillControl _:
                    // These controls have colors that are always data-bound
                    break;

                case RichTextBox _:
                    // Rtf TextBox is special because we don't want any color changes, otherwise it will mess up the saved Rtf text
                    return;

                case CheckBox chkControl:
                    if (chkControl.DoThreadSafeFunc(x => x.Appearance == Appearance.Button, token) || chkControl is DpiFriendlyCheckBoxDisguisedAsButton)
                    {
                        ApplyButtonStyle(token);
                        break;
                    }

                    if (chkControl is ColorableCheckBox chkControlColored)
                    {
                        chkControlColored.DoThreadSafe(x =>
                        {
                            x.DefaultColorScheme = blnLightMode;
                            if (blnLightMode) // Disabled case for Light mode already handled by the switch above
                                x.ForeColor = ColorManager.ControlTextLight;
                            else
                                x.ForeColor = x.Enabled ? ColorManager.ControlTextDark : ColorManager.GrayText;
                        }, token);
                        break;
                    }
                    goto default;

                case Button cmdControl:
                    if (cmdControl.DoThreadSafeFunc(x => x.FlatStyle, token) == FlatStyle.Flat)
                        goto default;
                    ApplyButtonStyle(token);
                    break;

                case HeaderCell _:
                    // Header cells should use inverted colors
                    objControl.DoThreadSafe(x =>
                    {
                        if (blnLightMode)
                        {
                            x.ForeColor = ColorManager.ControlLightestLight;
                            x.BackColor = ColorManager.ControlTextLight;
                        }
                        else
                        {
                            x.ForeColor = ColorManager.ControlLightestDark;
                            x.BackColor = ColorManager.ControlTextDark;
                        }
                    }, token);
                    return;

                case TableLayoutPanel tlpControl:
                    tlpControl.DoThreadSafe(x =>
                    {
                        if (x.BorderStyle != BorderStyle.None)
                            x.BorderStyle = blnLightMode ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    }, token);
                    goto default;
                case Form frmControl:
                    MenuStrip objMainMenuStrip = frmControl.DoThreadSafeFunc(x => x.MainMenuStrip, token);
                    if (objMainMenuStrip != null)
                    {
                        foreach (ToolStripMenuItem tssItem in objMainMenuStrip.DoThreadSafeFunc(x => x.Items, token))
                            ApplyColorsRecursively(tssItem, blnLightMode, token);
                    }
                    goto default;
                case TabControl objTabControl:
                    foreach (TabPage tabPage in objTabControl.DoThreadSafeFunc(x => x.TabPages, token))
                        ApplyColorsRecursively(tabPage, blnLightMode, token);
                    goto default;
                case ToolStrip tssStrip:
                    foreach (ToolStripItem tssItem in tssStrip.DoThreadSafeFunc(x => x.Items, token))
                        ApplyColorsRecursively(tssItem, blnLightMode, token);
                    goto default;
                default:
                    objControl.DoThreadSafe(x =>
                    {
                        if (blnLightMode)
                        {
                            if (x.ForeColor == ColorManager.ControlDark)
                                x.ForeColor = ColorManager.ControlLight;
                            else if (x.ForeColor == ColorManager.ControlDarkerDark)
                                x.ForeColor = ColorManager.ControlDarkerLight;
                            else if (x.ForeColor == ColorManager.ControlDarkestDark)
                                x.ForeColor = ColorManager.ControlDarkestLight;
                            else if (x.ForeColor == ColorManager.WindowTextDark)
                                x.ForeColor = ColorManager.WindowTextLight;
                            else if (x.ForeColor == ColorManager.ControlTextLight || x.ForeColor == ColorManager.ControlTextDark)
                                x.ForeColor = ColorManager.ControlTextLight;
                        }
                        else if (x.ForeColor == ColorManager.ControlLight)
                            x.ForeColor = ColorManager.ControlDark;
                        else if (x.ForeColor == ColorManager.ControlDarkerLight)
                            x.ForeColor = ColorManager.ControlDarkerDark;
                        else if (x.ForeColor == ColorManager.ControlDarkestLight)
                            x.ForeColor = ColorManager.ControlDarkestDark;
                        else if (x.ForeColor == ColorManager.WindowTextLight)
                            x.ForeColor = ColorManager.WindowTextDark;
                        else if (x.ForeColor == ColorManager.ControlTextLight || x.ForeColor == ColorManager.ControlTextDark)
                            x.ForeColor = ColorManager.ControlTextDark;
                    }, token);
                    // These controls never have backgrounds set explicitly, so shouldn't have their backgrounds overwritten
                    if (!(objControl is Label || objControl is CheckBox || objControl is PictureBox
                          || objControl is Button || (objControl is Panel && !(objControl is SplitterPanel
                                                                               || objControl
                                                                                   .DoThreadSafeFunc(
                                                                                       x => x.BackColor, token).A
                                                                               == byte.MaxValue))))
                    {
                        objControl.DoThreadSafe(x =>
                        {
                            if (blnLightMode)
                            {
                                if (x.BackColor == ColorManager.ControlLighterDark)
                                    x.BackColor = ColorManager.ControlLighterLight;
                                else if (x.BackColor == ColorManager.ControlLightestDark)
                                    x.BackColor = ColorManager.ControlLightestLight;
                                else if (x.BackColor == ColorManager.WindowDark)
                                    x.BackColor = ColorManager.WindowLight;
                                else if (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark)
                                    x.BackColor = ColorManager.ControlLight;
                            }
                            else if (x.BackColor == ColorManager.ControlLighterLight)
                                x.BackColor = ColorManager.ControlLighterDark;
                            else if (x.BackColor == ColorManager.ControlLightestLight)
                                x.BackColor = ColorManager.ControlLightestDark;
                            else if (x.BackColor == ColorManager.WindowLight)
                                x.BackColor = ColorManager.WindowDark;
                            else if (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark)
                                x.BackColor = ColorManager.ControlDark;
                        }, token);
                    }

                    break;
            }

            foreach (Control objChild in objControl.DoThreadSafeFunc(x => x.Controls, token))
                ApplyColorsRecursively(objChild, blnLightMode, token);
        }

        internal static void ApplyColorsRecursively(ToolStripItem tssItem, bool blnLightMode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            ToolStrip objParent = tssItem.GetCurrentParent();
            if (objParent != null)
                objParent.DoThreadSafe(DoColor, token: token);
            else
                DoColor(token);

            void DoColor(CancellationToken innerToken)
            {
                if (blnLightMode)
                {
                    if (tssItem.ForeColor == ColorManager.ControlDark)
                        tssItem.ForeColor = ColorManager.ControlLight;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkerDark)
                        tssItem.ForeColor = ColorManager.ControlDarkerLight;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkestDark)
                        tssItem.ForeColor = ColorManager.ControlDarkestLight;
                    else
                        tssItem.ForeColor = ColorManager.ControlTextLight;
                    innerToken.ThrowIfCancellationRequested();
                    if (tssItem.BackColor == ColorManager.ControlLighterDark)
                        tssItem.BackColor = ColorManager.ControlLighterLight;
                    else if (tssItem.BackColor == ColorManager.ControlLightestDark)
                        tssItem.BackColor = ColorManager.ControlLightestLight;
                    else
                        tssItem.BackColor = ColorManager.ControlLight;
                }
                else
                {
                    if (tssItem.ForeColor == ColorManager.ControlLight)
                        tssItem.ForeColor = ColorManager.ControlDark;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkerLight)
                        tssItem.ForeColor = ColorManager.ControlDarkerDark;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkestLight)
                        tssItem.ForeColor = ColorManager.ControlDarkestDark;
                    else
                        tssItem.ForeColor = ColorManager.ControlTextDark;
                    innerToken.ThrowIfCancellationRequested();
                    if (tssItem.BackColor == ColorManager.ControlLighterLight)
                        tssItem.BackColor = ColorManager.ControlLighterDark;
                    else if (tssItem.BackColor == ColorManager.ControlLightestLight)
                        tssItem.BackColor = ColorManager.ControlLightestDark;
                    else
                        tssItem.BackColor = ColorManager.ControlDark;
                }
            }

            token.ThrowIfCancellationRequested();
            switch (tssItem)
            {
                case ToolStripDropDownItem tssDropDownItem:
                    foreach (ToolStripItem tssDropDownChild in tssDropDownItem.DropDownItems)
                        ApplyColorsRecursively(tssDropDownChild, blnLightMode, token);
                    break;
                case ColorableToolStripSeparator tssSeparator when objParent != null:
                    objParent.DoThreadSafe(() => tssSeparator.DefaultColorScheme = blnLightMode, token);
                    break;
                case ColorableToolStripSeparator tssSeparator:
                    tssSeparator.DefaultColorScheme = blnLightMode;
                    break;
            }
        }

        internal static void ApplyColorsRecursively(TreeNode nodNode, bool blnLightMode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            TreeView treView = nodNode.TreeView;
            if (treView != null)
                treView.DoThreadSafe(DoColor, token: token);
            else
                DoColor();

            void DoColor()
            {
                if (blnLightMode)
                {
                    if (nodNode.ForeColor == ColorManager.HasNotesColorDark)
                        nodNode.ForeColor = ColorManager.HasNotesColorLight;
                    else if (nodNode.ForeColor == ColorManager.GrayHasNotesColorDark)
                        nodNode.ForeColor = ColorManager.GrayHasNotesColorLight;
                    else if (nodNode.ForeColor == ColorManager.WindowTextDark)
                        nodNode.ForeColor = ColorManager.WindowTextLight;
                    nodNode.BackColor = ColorManager.WindowLight;
                }
                else
                {
                    if (nodNode.ForeColor == ColorManager.HasNotesColorLight)
                        nodNode.ForeColor = ColorManager.HasNotesColorDark;
                    else if (nodNode.ForeColor == ColorManager.GrayHasNotesColorLight)
                        nodNode.ForeColor = ColorManager.GrayHasNotesColorDark;
                    else if (nodNode.ForeColor == ColorManager.WindowTextLight)
                        nodNode.ForeColor = ColorManager.WindowTextDark;
                    nodNode.BackColor = ColorManager.WindowDark;
                }
            }
            token.ThrowIfCancellationRequested();
            foreach (TreeNode nodNodeChild in nodNode.Nodes)
                ApplyColorsRecursively(nodNodeChild, blnLightMode, token);
        }

        internal static async Task ApplyColorsRecursivelyAsync(Control objControl, bool blnLightMode, CancellationToken token = default)
        {
            Task ApplyButtonStyle(CancellationToken innerToken)
            {
                // Buttons look weird if colored based on anything other than the default color scheme in dark mode
                return objControl.DoThreadSafeAsync(x =>
                {
                    x.ForeColor = SystemColors.ControlText;
                    if (x is ButtonBase z)
                        z.UseVisualStyleBackColor = true;
                }, innerToken);
            }
            switch (objControl)
            {
                case DataGridView objDataGridView:
                    {
                        Color objBackgroundColor;
                        Color objForeColor;
                        Color objBackColor;
                        Color objAlternateBackColor;
                        if (blnLightMode)
                        {
                            objBackgroundColor = ColorManager.AppWorkspaceLight;
                            objForeColor = ColorManager.ControlTextLight;
                            objBackColor = ColorManager.ControlLight;
                            objAlternateBackColor = ColorManager.ControlLighterLight;
                        }
                        else
                        {
                            objBackgroundColor = ColorManager.AppWorkspaceDark;
                            objForeColor = ColorManager.ControlTextDark;
                            objBackColor = ColorManager.ControlDark;
                            objAlternateBackColor = ColorManager.ControlLighterDark;
                        }
                        await objDataGridView.DoThreadSafeAsync((x, t) =>
                        {
                            x.BackgroundColor = objBackgroundColor;
                            x.GridColor = objForeColor;
                            x.DefaultCellStyle.ForeColor = objForeColor;
                            x.DefaultCellStyle.BackColor = objBackColor;
                            x.ColumnHeadersDefaultCellStyle.ForeColor = objForeColor;
                            x.ColumnHeadersDefaultCellStyle.BackColor = objBackColor;
                            x.AlternatingRowsDefaultCellStyle.ForeColor = objForeColor;
                            x.AlternatingRowsDefaultCellStyle.BackColor = objAlternateBackColor;
                            x.RowTemplate.DefaultCellStyle.ForeColor = objForeColor;
                            x.RowTemplate.DefaultCellStyle.BackColor = objBackColor;
                            foreach (DataGridViewTextBoxColumn objColumn in x.Columns)
                            {
                                t.ThrowIfCancellationRequested();
                                objColumn.DefaultCellStyle.ForeColor = objForeColor;
                                objColumn.DefaultCellStyle.BackColor = objBackColor;
                            }
                        }, token).ConfigureAwait(false);
                        break;
                    }
                case SplitContainer objSplitControl:
                    {
                        Color objColor = blnLightMode
                            ? ColorManager.SplitterColorLight
                            : ColorManager.SplitterColorDark;
                        await objSplitControl.DoThreadSafeAsync(x =>
                        {
                            x.ForeColor = objColor;
                            x.BackColor = objColor;
                        }, token).ConfigureAwait(false);
                        await ApplyColorsRecursivelyAsync(
                            await objSplitControl.DoThreadSafeFuncAsync(x => x.Panel1, token: token).ConfigureAwait(false), blnLightMode, token).ConfigureAwait(false);
                        await ApplyColorsRecursivelyAsync(
                            await objSplitControl.DoThreadSafeFuncAsync(x => x.Panel2, token: token).ConfigureAwait(false), blnLightMode, token).ConfigureAwait(false);
                        break;
                    }
                case TreeView treControl:
                    {
                        Color objForeColor;
                        Color objBackColor;
                        if (blnLightMode)
                        {
                            objForeColor = ColorManager.WindowTextLight;
                            objBackColor = ColorManager.WindowLight;
                        }
                        else
                        {
                            objForeColor = ColorManager.WindowTextDark;
                            objBackColor = ColorManager.WindowDark;
                        }
                        await treControl.DoThreadSafeAsync(x =>
                        {
                            x.ForeColor = objForeColor;
                            x.BackColor = objBackColor;
                            x.LineColor = objForeColor;
                        }, token).ConfigureAwait(false);
                        foreach (TreeNode objNode in await treControl.DoThreadSafeFuncAsync(x => x.Nodes, token).ConfigureAwait(false))
                            await ApplyColorsRecursivelyAsync(objNode, blnLightMode, token).ConfigureAwait(false);
                        break;
                    }
                case TextBox txtControl:
                    {
                        await txtControl.DoThreadSafeAsync(x =>
                        {
                            if (x.ForeColor != ColorManager.ErrorColor)
                            {
                                if (blnLightMode)
                                {
                                    x.ForeColor = ColorManager.WindowTextLight;
                                    if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                                        x.BackColor = ColorManager.ControlLight;
                                    else
                                        x.BackColor = ColorManager.WindowLight;
                                }
                                else
                                {
                                    x.ForeColor = ColorManager.WindowTextDark;
                                    if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                                        x.BackColor = ColorManager.ControlDark;
                                    else
                                        x.BackColor = ColorManager.WindowDark;
                                }
                            }
                            else if (blnLightMode)
                            {
                                if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                                    x.BackColor = ColorManager.ControlLight;
                                else
                                    x.BackColor = ColorManager.WindowLight;
                            }
                            else if (x.ReadOnly && (x.BackColor == ColorManager.ControlLight || x.BackColor == ColorManager.ControlDark))
                                x.BackColor = ColorManager.ControlDark;
                            else
                                x.BackColor = ColorManager.WindowDark;
                        }, token).ConfigureAwait(false);
                        break;
                    }
                case ListView objListView:
                    {
                        Color objForeColor;
                        Color objBackColor;
                        if (blnLightMode)
                        {
                            objForeColor = ColorManager.WindowTextLight;
                            objBackColor = ColorManager.WindowLight;
                        }
                        else
                        {
                            objForeColor = ColorManager.WindowTextDark;
                            objBackColor = ColorManager.WindowDark;
                        }

                        await objListView.DoThreadSafeAsync(x =>
                        {
                            x.ForeColor = objForeColor;
                            x.BackColor = objBackColor;
                            if (blnLightMode)
                            {
                                foreach (DiceRollerListViewItem objItem in x.Items.OfType<DiceRollerListViewItem>())
                                {
                                    if (objItem.IsHit)
                                    {
                                        if (objItem.IsGlitch)
                                        {
                                            objItem.ForeColor = ColorManager.DieGlitchHitForeLight;
                                            objItem.BackColor = ColorManager.DieGlitchHitBackgroundLight;
                                        }
                                        else
                                        {
                                            objItem.ForeColor = ColorManager.DieHitForeLight;
                                            objItem.BackColor = ColorManager.DieHitBackgroundLight;
                                        }
                                    }
                                    else if (objItem.IsGlitch)
                                    {
                                        objItem.ForeColor = ColorManager.DieGlitchForeLight;
                                        objItem.BackColor = ColorManager.DieGlitchBackgroundLight;
                                    }
                                    else
                                    {
                                        objItem.ForeColor = objForeColor;
                                        objItem.BackColor = objBackColor;
                                    }
                                }
                            }
                            else
                            {
                                foreach (DiceRollerListViewItem objItem in x.Items.OfType<DiceRollerListViewItem>())
                                {
                                    if (objItem.IsHit)
                                    {
                                        if (objItem.IsGlitch)
                                        {
                                            objItem.ForeColor = ColorManager.DieGlitchHitForeDark;
                                            objItem.BackColor = ColorManager.DieGlitchHitBackgroundDark;
                                        }
                                        else
                                        {
                                            objItem.ForeColor = ColorManager.DieHitForeDark;
                                            objItem.BackColor = ColorManager.DieHitBackgroundDark;
                                        }
                                    }
                                    else if (objItem.IsGlitch)
                                    {
                                        objItem.ForeColor = ColorManager.DieGlitchForeDark;
                                        objItem.BackColor = ColorManager.DieGlitchBackgroundDark;
                                    }
                                    else
                                    {
                                        objItem.ForeColor = objForeColor;
                                        objItem.BackColor = objBackColor;
                                    }
                                }
                            }
                        }, token).ConfigureAwait(false);
                        break;
                    }
                case ListBox _:
                case ComboBox _:
                case TableCell _:
                    {
                        Color objForeColor;
                        Color objBackColor;
                        if (blnLightMode)
                        {
                            objForeColor = ColorManager.WindowTextLight;
                            objBackColor = ColorManager.WindowLight;
                        }
                        else
                        {
                            objForeColor = ColorManager.WindowTextDark;
                            objBackColor = ColorManager.WindowDark;
                        }
                        await objControl.DoThreadSafeAsync(x =>
                        {
                            x.ForeColor = objForeColor;
                            x.BackColor = objBackColor;
                        }, token).ConfigureAwait(false);
                        break;
                    }
                case GroupBox _:
                    {
                        Color objForeColor;
                        Color objBackColor;
                        if (blnLightMode)
                        {
                            objForeColor = ColorManager.ControlTextLight;
                            objBackColor = ColorManager.ControlLight;
                        }
                        else
                        {
                            objForeColor = ColorManager.ControlTextDark;
                            objBackColor = ColorManager.ControlDark;
                        }
                        await objControl.DoThreadSafeAsync(x =>
                        {
                            x.ForeColor = objForeColor;
                            x.BackColor = objBackColor;
                        }, token).ConfigureAwait(false);
                        break;
                    }
                case ContactControl _:
                case PetControl _:
                case SkillControl _:
                case KnowledgeSkillControl _:
                    // These controls have colors that are always data-bound
                    break;

                case RichTextBox _:
                    // Rtf TextBox is special because we don't want any color changes, otherwise it will mess up the saved Rtf text
                    return;

                case CheckBox chkControl:
                    if (await chkControl.DoThreadSafeFuncAsync(x => x.Appearance == Appearance.Button, token).ConfigureAwait(false) || chkControl is DpiFriendlyCheckBoxDisguisedAsButton)
                    {
                        await ApplyButtonStyle(token).ConfigureAwait(false);
                        break;
                    }

                    if (chkControl is ColorableCheckBox chkControlColored)
                    {
                        Color objForeColor;
                        if (blnLightMode)
                        {
                            objForeColor = ColorManager.ControlTextLight; // Disabled case for Light mode already handled by the DefaultColorScheme = true property
                        }
                        else
                        {
                            objForeColor = await chkControlColored.DoThreadSafeFuncAsync(x => x.Enabled, token).ConfigureAwait(false)
                                ? ColorManager.ControlTextDark
                                : ColorManager.GrayTextDark;
                        }
                        await chkControlColored.DoThreadSafeAsync(x =>
                        {
                            x.DefaultColorScheme = blnLightMode;
                            x.ForeColor = objForeColor;
                        }, token).ConfigureAwait(false);
                        break;
                    }
                    goto default;

                case Button cmdControl:
                    if (await cmdControl.DoThreadSafeFuncAsync(x => x.FlatStyle, token).ConfigureAwait(false) == FlatStyle.Flat)
                        goto default;
                    await ApplyButtonStyle(token).ConfigureAwait(false);
                    break;

                case HeaderCell _:
                    // Header cells should use inverted colors
                    {
                        Color objForeColor;
                        Color objBackColor;
                        if (blnLightMode)
                        {
                            objForeColor = ColorManager.ControlLightestLight;
                            objBackColor = ColorManager.ControlDark;
                        }
                        else
                        {
                            objForeColor = ColorManager.ControlLightestDark;
                            objBackColor = ColorManager.ControlLight;
                        }

                        await objControl.DoThreadSafeAsync(x =>
                        {
                            x.ForeColor = objForeColor;
                            x.BackColor = objBackColor;
                        }, token).ConfigureAwait(false);
                        return;
                    }
                case TableLayoutPanel tlpControl:
                    await tlpControl.DoThreadSafeAsync(x =>
                    {
                        if (x.BorderStyle != BorderStyle.None)
                            x.BorderStyle = blnLightMode ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    }, token).ConfigureAwait(false);
                    goto default;
                case Form frmControl:
                    MenuStrip objMainMenuStrip = await frmControl.DoThreadSafeFuncAsync(x => x.MainMenuStrip, token).ConfigureAwait(false);
                    if (objMainMenuStrip != null)
                    {
                        foreach (ToolStripMenuItem tssItem in await objMainMenuStrip.DoThreadSafeFuncAsync(x => x.Items, token).ConfigureAwait(false))
                            await ApplyColorsRecursivelyAsync(tssItem, blnLightMode, token).ConfigureAwait(false);
                    }
                    goto default;
                case TabControl objTabControl:
                    foreach (TabPage tabPage in await objTabControl.DoThreadSafeFuncAsync(x => x.TabPages, token).ConfigureAwait(false))
                        await ApplyColorsRecursivelyAsync(tabPage, blnLightMode, token).ConfigureAwait(false);
                    goto default;
                case ToolStrip tssStrip:
                    foreach (ToolStripItem tssItem in tssStrip.Items)
                        await ApplyColorsRecursivelyAsync(tssItem, blnLightMode, token).ConfigureAwait(false);
                    goto default;
                default:
                    {
                        Color objControlLightColor = ColorManager.ControlLight;
                        Color objControlDarkColor = ColorManager.ControlDark;
                        Color objControlDarkerLightColor = ColorManager.ControlDarkerLight;
                        Color objControlDarkestLightColor = ColorManager.ControlDarkestLight;
                        Color objControlDarkerDarkColor = ColorManager.ControlDarkerDark;
                        Color objControlDarkestDarkColor = ColorManager.ControlDarkestDark;
                        Color objControlTextLightColor = ColorManager.ControlTextLight;
                        Color objControlTextDarkColor = ColorManager.ControlTextDark;
                        await objControl.DoThreadSafeAsync(x =>
                        {
                            if (blnLightMode)
                            {
                                if (x.ForeColor == objControlDarkColor)
                                    x.ForeColor = objControlLightColor;
                                else if (x.ForeColor == objControlDarkerDarkColor)
                                    x.ForeColor = objControlDarkerLightColor;
                                else if (x.ForeColor == objControlDarkestDarkColor)
                                    x.ForeColor = objControlDarkestLightColor;
                                else if (x.ForeColor == objControlTextLightColor || x.ForeColor == objControlTextDarkColor)
                                    x.ForeColor = objControlTextLightColor;
                            }
                            else if (x.ForeColor == objControlLightColor)
                                x.ForeColor = objControlDarkColor;
                            else if (x.ForeColor == objControlDarkerLightColor)
                                x.ForeColor = objControlDarkerDarkColor;
                            else if (x.ForeColor == objControlDarkestLightColor)
                                x.ForeColor = objControlDarkestDarkColor;
                            else if (x.ForeColor == objControlTextLightColor || x.ForeColor == objControlTextDarkColor)
                                x.ForeColor = objControlTextDarkColor;
                        }, token).ConfigureAwait(false);
                        // These controls never have backgrounds set explicitly, so shouldn't have their backgrounds overwritten
                        if (!(objControl is Label || objControl is CheckBox || objControl is PictureBox
                              || objControl is Button || (objControl is Panel && !(objControl is SplitterPanel
                                                                                   || (await objControl
                                                                                       .DoThreadSafeFuncAsync(
                                                                                           x => x.BackColor, token).ConfigureAwait(false)).A
                                                                                   == byte.MaxValue))))
                        {
                            Color objControlLighterLightColor
                                = ColorManager.ControlLighterLight;
                            Color objControlLightestLightColor
                                = ColorManager.ControlLightestLight;
                            Color objControlLighterDarkColor
                                = ColorManager.ControlLighterDark;
                            Color objControlLightestDarkColor
                                = ColorManager.ControlLightestDark;
                            await objControl.DoThreadSafeAsync(x =>
                            {
                                if (blnLightMode)
                                {
                                    if (x.BackColor == objControlLighterDarkColor)
                                        x.BackColor = objControlLighterLightColor;
                                    else if (x.BackColor == objControlLightestDarkColor)
                                        x.BackColor = objControlLightestLightColor;
                                    else
                                        x.BackColor = objControlLightColor;
                                }
                                else if (x.BackColor == objControlLighterLightColor)
                                    x.BackColor = objControlLighterDarkColor;
                                else if (x.BackColor == objControlLightestLightColor)
                                    x.BackColor = objControlLightestDarkColor;
                                else
                                    x.BackColor = objControlDarkColor;
                            }, token).ConfigureAwait(false);
                        }

                        break;
                    }
            }

            foreach (Control objChild in await objControl.DoThreadSafeFuncAsync(x => x.Controls, token).ConfigureAwait(false))
                await ApplyColorsRecursivelyAsync(objChild, blnLightMode, token).ConfigureAwait(false);
        }

        internal static async Task ApplyColorsRecursivelyAsync(ToolStripItem tssItem, bool blnLightMode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            Color objControlLightColor = ColorManager.ControlLight;
            Color objControlDarkColor = ColorManager.ControlDark;

            ToolStrip objParent = tssItem.GetCurrentParent();
            if (objParent != null)
                await objParent.DoThreadSafeAsync(DoColor, token).ConfigureAwait(false);
            else
                DoColor(token);

            void DoColor(CancellationToken innerToken)
            {
                if (blnLightMode)
                {
                    if (tssItem.ForeColor == objControlDarkColor)
                        tssItem.ForeColor = objControlLightColor;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkerDark)
                        tssItem.ForeColor = ColorManager.ControlDarkerLight;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkestDark)
                        tssItem.ForeColor = ColorManager.ControlDarkestLight;
                    else
                        tssItem.ForeColor = ColorManager.ControlTextLight;
                    innerToken.ThrowIfCancellationRequested();
                    if (tssItem.BackColor == ColorManager.ControlLighterDark)
                        tssItem.BackColor = ColorManager.ControlLighterLight;
                    else if (tssItem.BackColor == ColorManager.ControlLightestDark)
                        tssItem.BackColor = ColorManager.ControlLightestLight;
                    else
                        tssItem.BackColor = objControlLightColor;
                }
                else
                {
                    if (tssItem.ForeColor == objControlLightColor)
                        tssItem.ForeColor = objControlDarkColor;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkerLight)
                        tssItem.ForeColor = ColorManager.ControlDarkerDark;
                    else if (tssItem.ForeColor == ColorManager.ControlDarkestLight)
                        tssItem.ForeColor = ColorManager.ControlDarkestDark;
                    else
                        tssItem.ForeColor = ColorManager.ControlTextDark;
                    innerToken.ThrowIfCancellationRequested();
                    if (tssItem.BackColor == ColorManager.ControlLighterLight)
                        tssItem.BackColor = ColorManager.ControlLighterDark;
                    else if (tssItem.BackColor == ColorManager.ControlLightestLight)
                        tssItem.BackColor = ColorManager.ControlLightestDark;
                    else
                        tssItem.BackColor = objControlDarkColor;
                }
            }

            token.ThrowIfCancellationRequested();
            switch (tssItem)
            {
                case ToolStripDropDownItem tssDropDownItem:
                    foreach (ToolStripItem tssDropDownChild in tssDropDownItem.DropDownItems)
                        await ApplyColorsRecursivelyAsync(tssDropDownChild, blnLightMode, token).ConfigureAwait(false);
                    break;
                case ColorableToolStripSeparator tssSeparator when objParent != null:
                    await objParent.DoThreadSafeAsync(() => tssSeparator.DefaultColorScheme = blnLightMode, token).ConfigureAwait(false);
                    break;
                case ColorableToolStripSeparator tssSeparator:
                    tssSeparator.DefaultColorScheme = blnLightMode;
                    break;
            }
        }

        internal static async Task ApplyColorsRecursivelyAsync(TreeNode nodNode, bool blnLightMode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            Color objBackColor = blnLightMode ? ColorManager.WindowLight : ColorManager.WindowDark;

            TreeView treView = nodNode.TreeView;
            if (treView != null)
                await treView.DoThreadSafeAsync(DoColor, token).ConfigureAwait(false);
            else
                DoColor();

            void DoColor()
            {
                if (blnLightMode)
                {
                    if (nodNode.ForeColor == ColorManager.HasNotesColorDark)
                        nodNode.ForeColor = ColorManager.HasNotesColorLight;
                    else if (nodNode.ForeColor == ColorManager.GrayHasNotesColorDark)
                        nodNode.ForeColor = ColorManager.GrayHasNotesColorLight;
                    else if (nodNode.ForeColor == ColorManager.WindowTextDark)
                        nodNode.ForeColor = ColorManager.WindowTextLight;
                }
                else
                {
                    if (nodNode.ForeColor == ColorManager.HasNotesColorLight)
                        nodNode.ForeColor = ColorManager.HasNotesColorDark;
                    else if (nodNode.ForeColor == ColorManager.GrayHasNotesColorLight)
                        nodNode.ForeColor = ColorManager.GrayHasNotesColorDark;
                    else if (nodNode.ForeColor == ColorManager.WindowTextLight)
                        nodNode.ForeColor = ColorManager.WindowTextDark;
                }
                nodNode.BackColor = objBackColor;
            }

            token.ThrowIfCancellationRequested();
            foreach (TreeNode nodNodeChild in nodNode.Nodes)
                await ApplyColorsRecursivelyAsync(nodNodeChild, blnLightMode, token).ConfigureAwait(false);
        }
    }
}
