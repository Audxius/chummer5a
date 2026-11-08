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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Chummer
{
    /// <summary>
    /// WinForms implementation of <see cref="IDialogService"/>: shows the legacy form whose class name matches the
    /// request, replaying the request's recorded settings and setup calls on it and copying its results back.
    /// Request members are named after the form members by construction, so the mapping is by name.
    /// </summary>
    public sealed class WinFormsDialogService : IDialogService
    {
        public DialogOutcome Show(DialogRequest objRequest, object owner = null, CancellationToken token = default)
        {
            return Utils.SafelyRunSynchronously(t => ShowAsync(objRequest, owner, t), token);
        }

        public async Task<DialogOutcome> ShowAsync(DialogRequest objRequest, object owner = null, CancellationToken token = default)
        {
            if (objRequest == null)
                throw new ArgumentNullException(nameof(objRequest));
            Type typForm = typeof(WinFormsDialogService).Assembly.GetType("Chummer." + objRequest.DialogName, true);
            object[] aobjArguments = ConvertArguments(typForm, objRequest.ConstructorArguments);
            using (ThreadSafeForm<Form> frmDialog = await ThreadSafeForm<Form>.GetAsync(
                       () => (Form)Activator.CreateInstance(typForm, aobjArguments), token).ConfigureAwait(false))
            {
                Form frmForm = frmDialog.MyForm;
                // Replay the engine's setup in its original order (settings and setup calls interleaved).
                foreach (DialogOperation objOperation in objRequest.Operations)
                {
                    if (!objOperation.IsCall)
                    {
                        PropertyInfo objProperty = typForm.GetProperty(objOperation.Name, BindingFlags.Public | BindingFlags.Instance)
                                                   ?? throw new MissingMemberException(typForm.Name, objOperation.Name);
                        object objValue = ConvertValue(objOperation.Arguments[0], objProperty.PropertyType);
                        await frmForm.DoThreadSafeAsync((Form x) => objProperty.SetValue(x, objValue), token).ConfigureAwait(false);
                        continue;
                    }
                    MethodInfo objMethod = FindMethod(typForm, objOperation.Name, objOperation.Arguments);
                    object[] aobjCallArguments = objMethod.GetParameters()
                                                          .Select((x, i) => i < objOperation.Arguments.Length ? ConvertValue(objOperation.Arguments[i], x.ParameterType) : x.DefaultValue)
                                                          .ToArray();
                    object objReturn = await frmForm.DoThreadSafeFuncAsync((Form x) => objMethod.Invoke(x, aobjCallArguments), token).ConfigureAwait(false);
                    if (objReturn is Task tskReturn)
                        await tskReturn.ConfigureAwait(false);
                }

                DialogResult eResult = owner is Character objCharacter
                    ? await frmDialog.ShowDialogSafeAsync(objCharacter, token).ConfigureAwait(false)
                    : await frmDialog.ShowDialogSafeAsync(owner as IWin32Window, token).ConfigureAwait(false);

                foreach (string strResult in objRequest.ResultNames)
                {
                    PropertyInfo objProperty = typForm.GetProperty(strResult, BindingFlags.Public | BindingFlags.Instance)
                                               ?? throw new MissingMemberException(typForm.Name, strResult);
                    object objValue = await frmForm.DoThreadSafeFuncAsync((Form x) => objProperty.GetValue(x), token).ConfigureAwait(false);
                    objRequest.SetResult(strResult, objValue);
                }

                return (DialogOutcome)eResult;
            }
        }

        private static MethodInfo FindMethod(Type typForm, string strName, object[] aobjArguments)
        {
            foreach (MethodInfo objMethod in typForm.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Name == strName))
            {
                ParameterInfo[] aobjParameters = objMethod.GetParameters();
                if (aobjParameters.Length < aobjArguments.Length)
                    continue;
                if (aobjParameters.Skip(aobjArguments.Length).Any(x => !x.HasDefaultValue))
                    continue;
                bool blnMatch = true;
                for (int i = 0; i < aobjArguments.Length; ++i)
                {
                    if (aobjArguments[i] != null && !IsConvertible(aobjArguments[i], aobjParameters[i].ParameterType))
                    {
                        blnMatch = false;
                        break;
                    }
                }
                if (blnMatch)
                    return objMethod;
            }
            throw new MissingMethodException(typForm.Name, strName);
        }

        private static object[] ConvertArguments(Type typForm, IReadOnlyList<object> lstArguments)
        {
            foreach (ConstructorInfo objConstructor in typForm.GetConstructors())
            {
                ParameterInfo[] aobjParameters = objConstructor.GetParameters();
                if (aobjParameters.Length != lstArguments.Count)
                    continue;
                bool blnMatch = true;
                for (int i = 0; i < aobjParameters.Length; ++i)
                {
                    if (lstArguments[i] != null && !IsConvertible(lstArguments[i], aobjParameters[i].ParameterType))
                    {
                        blnMatch = false;
                        break;
                    }
                }
                if (blnMatch)
                    return aobjParameters.Select((x, i) => ConvertValue(lstArguments[i], x.ParameterType)).ToArray();
            }
            throw new MissingMethodException(typForm.Name, ".ctor");
        }

        private static bool IsConvertible(object objValue, Type typTarget)
        {
            return typTarget.IsInstanceOfType(objValue) || typTarget.IsEnum && objValue is Enum;
        }

        /// <summary>
        /// Engine-side mirror enums (e.g. SelectArtMode) are converted to the form's nested enum by name.
        /// </summary>
        private static object ConvertValue(object objValue, Type typTarget)
        {
            if (objValue == null || typTarget.IsInstanceOfType(objValue))
                return objValue;
            if (typTarget.IsEnum && objValue is Enum)
                return Enum.Parse(typTarget, objValue.ToString());
            return Convert.ChangeType(objValue, typTarget, GlobalSettings.InvariantCultureInfo);
        }
    }
}
