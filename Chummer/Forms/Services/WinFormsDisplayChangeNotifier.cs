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

using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Chummer
{
    /// <summary>
    /// Tree nodes that display a contact or spirit and must follow its changes (previously stored on the model).
    /// </summary>
    public static class WinFormsTrackedNodes
    {
        private static readonly ConditionalWeakTable<object, ConcurrentHashSet<TreeNode>> s_dicNodes
            = new ConditionalWeakTable<object, ConcurrentHashSet<TreeNode>>();

        public static ConcurrentHashSet<TreeNode> Get(object objSource)
        {
            return s_dicNodes.GetValue(objSource, x => new ConcurrentHashSet<TreeNode>());
        }
    }

    /// <summary>
    /// WinForms implementation of <see cref="IDisplayChangeNotifier"/>: refreshes tracked tree nodes.
    /// </summary>
    public sealed class WinFormsDisplayChangeNotifier : IDisplayChangeNotifier
    {
        public void OnDisplayChanged(object objSource, DisplayAspect eAspect, CancellationToken token = default)
        {
            switch (objSource)
            {
                case Contact objContact:
                    switch (eAspect)
                    {
                        case DisplayAspect.Text: objContact.RefreshTrackedTreeNodes_Text(token); break;
                        case DisplayAspect.Color: objContact.RefreshTrackedTreeNodes_Color(token); break;
                        case DisplayAspect.ToolTipText: objContact.RefreshTrackedTreeNodes_ToolTipText(token); break;
                        case DisplayAspect.TypeData: objContact.RefreshTrackedTreeNodes_TypeData(token); break;
                        case DisplayAspect.Location: objContact.RefreshTrackedTreeNodes_Location(token); break;
                    }
                    break;
                case Spirit objSpirit:
                    switch (eAspect)
                    {
                        case DisplayAspect.Text: objSpirit.RefreshTrackedTreeNodes_Text(token); break;
                        case DisplayAspect.Color: objSpirit.RefreshTrackedTreeNodes_Color(token); break;
                        case DisplayAspect.ToolTipText: objSpirit.RefreshTrackedTreeNodes_ToolTipText(token); break;
                        case DisplayAspect.TypeData: objSpirit.RefreshTrackedTreeNodes_TypeData(token); break;
                        case DisplayAspect.ServicesOwed: objSpirit.RefreshTrackedTreeNodes_ServicesOwed(token); break;
                    }
                    break;
            }
        }

        public Task OnDisplayChangedAsync(object objSource, DisplayAspect eAspect, CancellationToken token = default)
        {
            switch (objSource)
            {
                case Contact objContact:
                    switch (eAspect)
                    {
                        case DisplayAspect.Text: return objContact.RefreshTrackedTreeNodes_TextAsync(token);
                        case DisplayAspect.Color: return objContact.RefreshTrackedTreeNodes_ColorAsync(token);
                        case DisplayAspect.ToolTipText: return objContact.RefreshTrackedTreeNodes_ToolTipTextAsync(token);
                        case DisplayAspect.TypeData: return objContact.RefreshTrackedTreeNodes_TypeDataAsync(token);
                        case DisplayAspect.Location: return objContact.RefreshTrackedTreeNodes_LocationAsync(token);
                    }
                    break;
                case Spirit objSpirit:
                    switch (eAspect)
                    {
                        case DisplayAspect.Text: return objSpirit.RefreshTrackedTreeNodes_TextAsync(token);
                        case DisplayAspect.Color: return objSpirit.RefreshTrackedTreeNodes_ColorAsync(token);
                        case DisplayAspect.ToolTipText: return objSpirit.RefreshTrackedTreeNodes_ToolTipTextAsync(token);
                        case DisplayAspect.TypeData: return objSpirit.RefreshTrackedTreeNodes_TypeDataAsync(token);
                        case DisplayAspect.ServicesOwed: return objSpirit.RefreshTrackedTreeNodes_ServicesOwedAsync(token);
                    }
                    break;
            }
            return Task.CompletedTask;
        }
    }
}
