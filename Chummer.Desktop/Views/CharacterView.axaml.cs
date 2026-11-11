/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Chummer.Desktop.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Chummer.Desktop.Views
{
    public partial class CharacterView : UserControl
    {
        private readonly Func<Task> _commitEditor;
        private CharacterViewModel _editorOwner;
        public CharacterView()
        {
            AvaloniaXamlLoader.Load(this);
            _commitEditor = CommitFocusedInfoAsync;
            DataContextChanged += (_, _) => BindEditor();
            AttachedToVisualTree += (_, _) => BindEditor();
            DetachedFromVisualTree += (_, _) => UnbindEditor();
        }

        private void BindEditor()
        {
            UnbindEditor(); _editorOwner = DataContext as CharacterViewModel;
            if (_editorOwner != null) _editorOwner.CommitInfoEditor = _commitEditor;
        }
        private void UnbindEditor()
        {
            if (_editorOwner != null && ReferenceEquals(_editorOwner.CommitInfoEditor, _commitEditor)) _editorOwner.CommitInfoEditor = null;
            _editorOwner = null;
        }
        private async Task CommitFocusedInfoAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox editor
                    && editor.DataContext is CharacterInfoViewModel && editor.GetVisualAncestors().Contains(this))
                    BindingOperations.GetBindingExpressionBase(editor, TextBox.TextProperty)?.UpdateSource();
            });
        }
    }
}
