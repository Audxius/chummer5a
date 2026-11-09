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

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Chummer.Desktop.Localization;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>The choices legacy shows next to the Initiate / Submerge button (group, ordeal, schooling).</summary>
    public sealed class InitiationPick
    {
        public bool Group { get; set; }
        public bool Ordeal { get; set; }
        public bool Schooling { get; set; }
        public bool ChooseMetamagic { get; set; } = true;
    }

    /// <summary>
    /// Initiation / submersion: group, ordeal and schooling reductions with the resulting karma (and the 10,000¥ schooling fee
    /// for magicians in career mode), then whether to pick the grade's metamagic or echo right away.
    /// </summary>
    public sealed class InitiationWindow : HudWindow
    {
        private InitiationPick _objResult;

        public InitiationWindow(Character objCharacter) : base(objCharacter.MAGEnabled ? "Initiate" : "Submerge", 520)
        {
            bool blnMagic = objCharacter.MAGEnabled;
            bool blnCareer = objCharacter.Created;
            int intGrade = blnMagic ? objCharacter.InitiateGrade : objCharacter.SubmersionGrade;
            CheckBox chkGroup = new CheckBox { Content = blnMagic ? "Group initiation" : "Network submersion" };
            CheckBox chkOrdeal = new CheckBox { Content = blnMagic ? "Ordeal" : "Task" };
            CheckBox chkSchooling = new CheckBox { Content = blnMagic ? "Schooling" + (blnCareer ? " (costs 10,000¥)" : string.Empty) : "Schooling" };
            CheckBox chkMetamagic = new CheckBox { Content = blnMagic ? "Choose the grade's metamagic now" : "Choose the grade's echo now", IsChecked = true };
            TextBlock txtCost = new TextBlock { FontSize = 30, FontWeight = FontWeight.Black };
            TextBlock txtKarma = new TextBlock { Classes = { "muted" } };
            void Update()
            {
                int intCost = MagicRules.GetInitiationCostAsync(objCharacter, chkGroup.IsChecked == true, chkOrdeal.IsChecked == true, chkSchooling.IsChecked == true)
                                        .GetAwaiter().GetResult();
                txtCost.Text = blnCareer ? intCost.ToString(GlobalSettings.CultureInfo) + " karma" : "free at creation";
                txtKarma.Text = blnCareer ? "You have " + objCharacter.Karma.ToString(GlobalSettings.CultureInfo) + " karma" : string.Empty;
            }
            chkGroup.IsCheckedChanged += (sender, args) => Update();
            chkOrdeal.IsCheckedChanged += (sender, args) => Update();
            chkSchooling.IsCheckedChanged += (sender, args) => Update();
            Update();
            Button cmdOk = new Button { Content = blnMagic ? "INITIATE" : "SUBMERGE", IsDefault = true, Classes = { "primary" } };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };
            cmdOk.Click += (sender, args) =>
            {
                _objResult = new InitiationPick
                {
                    Group = chkGroup.IsChecked == true,
                    Ordeal = chkOrdeal.IsChecked == true,
                    Schooling = chkSchooling.IsChecked == true,
                    ChooseMetamagic = chkMetamagic.IsChecked == true,
                };
                Close();
            };
            cmdCancel.Click += (sender, args) => Close();
            Content = Frame(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Heading((blnMagic ? "Initiate to grade " : "Submerge to grade ") + (intGrade + 1).ToString(GlobalSettings.CultureInfo)),
                    chkGroup, chkOrdeal, chkSchooling,
                    new StackPanel { Margin = new Thickness(0, 6, 0, 0), Children = { new TextBlock { Text = "COST", Classes = { "label" } }, txtCost, txtKarma } },
                    chkMetamagic,
                    ButtonRow(cmdOk, cmdCancel)
                }
            }, "hot");
        }

        public static async Task<InitiationPick> ShowAsync(object owner, Character objCharacter)
        {
            InitiationWindow objWindow = new InitiationWindow(objCharacter);
            await DialogHost.ShowModalAsync(objWindow, owner).ConfigureAwait(true);
            return objWindow._objResult;
        }
    }
}
