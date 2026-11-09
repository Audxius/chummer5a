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
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop
{
    /// <summary>
    /// Maps FooViewModel to Views.FooView by convention.
    /// </summary>
    public sealed class ViewLocator : IDataTemplate
    {
        public Control Build(object param)
        {
            if (param == null)
                return null;
            string strName = param.GetType().FullName?.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
            Type typView = strName == null ? null : Type.GetType(strName);
            if (typView != null)
                return (Control)Activator.CreateInstance(typView);
            return new TextBlock { Text = "No view for " + param.GetType().Name };
        }

        public bool Match(object data) => data is ViewModelBase;
    }
}
