using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using MajdataEdit_Neo.Assets.Langs;
using MajdataEdit_Neo.Utils;
using MajdataEdit_Neo.ViewModels;
using MsBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Themes;
using TextMateSharp.Themes;

namespace MajdataEdit_Neo.Views;

public partial class RecoverWindow : Window
{
    private readonly TextEditor _editor;

    public RecoverWindow()
    {
        InitializeComponent();

        _editor = this.FindControl<TextEditor>("RecoverEditor")!;
        _editor.Options.HighlightCurrentLine = true;
        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        var installation = TextMate.InstallTextMate(_editor, registryOptions);
        installation.SetGrammarFile(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "simai.tmLanguage.json"));
        installation.SetTheme(CreateClassicTheme());
    }

    // Extends the default DarkPlus theme in place with the classic MajdataEdit
    // colors. Scopes the old highlighter did not cover keep DarkPlus defaults.
    private static IRawTheme CreateClassicTheme() => new ThemeRaw
    {
        ["name"] = "MajdataEdit Classic",
        ["include"] = "dark_plus.json",
        ["tokenColors"] = new List<IRawThemeSetting>
        {
            Rule("comment.line.double-pipe.simai", "#6A9955"),
            Rule("meta.bpm.simai", "#FFF036"),
            Rule("constant.numeric.bpm-value.simai", "#FFF036"),
            Rule("punctuation.definition.bpm.begin.simai", "#FFFFFF"),
            Rule("punctuation.definition.bpm.end.simai", "#FFFFFF"),
            Rule("meta.measure.simai", "#DA70D6"),
            Rule("support.function.measure-value.simai", "#DA70D6"),
            Rule("punctuation.definition.measure.begin.simai", "#FFFFFF"),
            Rule("punctuation.definition.measure.end.simai", "#FFFFFF"),
            Rule("entity.name.variable.tap.simai", "#9CDCFE"),
            Rule("entity.name.variable.tap-double.simai", "#9CDCFE"),
            Rule("entity.name.class.hold.simai", "#4EC9B0"),
            Rule("entity.name.type.touch.simai", "#B5CEA8"),
            Rule("entity.name.type.touch-hold.simai", "#B5CEA8"),
            Rule("entity.name.function.slidecode.simai", "#DCDCAA"),
            Rule("entity.name.function.slidecode-star.simai", "#DCDCAA"),
            Rule("entity.name.function.slidecode-break.simai", "#DCDCAA"),
            Rule("entity.name.function.slide-star.simai", "#DCDCAA"),
            Rule("entity.name.function.slide.simai", "#DCDCAA"),
            Rule("keyword.operator.each.simai", "#FFFFFF"),
            Rule("keyword.operator.pseudo-each.simai", "#D88164"),
            Rule("keyword.operator.modifier.simai", "#D88164"),
        }
    };

    private static IRawThemeSetting Rule(string scope, string foreground) => new ThemeRaw
    {
        ["scope"] = scope,
        ["settings"] = new ThemeRaw { ["foreground"] = foreground }
    };

    public RecoverWindow(RecoverViewModel viewModel) : this()
    {
        DataContext = viewModel;
        _editor.Text = viewModel.Content;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Opened += (_, _) =>
        {
            var autoSaveList = this.FindControl<ListBox>("AutoSaveList");
            if (autoSaveList is not null && autoSaveList.ItemCount > 0)
                autoSaveList.SelectedIndex = 0;

            var list = this.FindControl<ListBox>("DifficultyList");
            if (list is not null && list.ItemCount > 0)
                list.SelectedIndex = 0;
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RecoverViewModel viewModel)
            return;

        if (e.PropertyName == nameof(RecoverViewModel.Content))
            _editor.Text = viewModel.Content;

        if (e.PropertyName == nameof(RecoverViewModel.Difficulties))
        {
            var list = this.FindControl<ListBox>("DifficultyList");
            if (list is not null)
                list.SelectedIndex = list.ItemCount > 0 ? 0 : -1;
        }
    }

    private void DifficultyList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is not RecoverDifficultyItem difficulty ||
            difficulty.LineNumber < 1 ||
            difficulty.LineNumber > _editor.Document.LineCount)
            return;

        var line = _editor.Document.GetLineByNumber(difficulty.LineNumber);
        _editor.Select(line.Offset, line.Length);
        _editor.ScrollTo(difficulty.LineNumber, 1);
    }

    private async void RecoverButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecoverViewModel viewModel || !viewModel.HasAutoSave)
            return;

        if (!viewModel.Recover())
        {
            await MessageBox.ShowWindowDialogAsync(
                Langs.Msg_RecoverFailed,
                Langs.Gui_Error,
                ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error);
            return;
        }

        Close(new RecoverDialogResult(
            RecoverDialogAction.Recover,
            viewModel.RecoveredMaidataPath!));
    }

    private void LoadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecoverViewModel viewModel ||
            !viewModel.CanLoadSelectedChart ||
            string.IsNullOrWhiteSpace(viewModel.RecoveredMaidataPath))
            return;

        Close(new RecoverDialogResult(
            RecoverDialogAction.Load,
            viewModel.RecoveredMaidataPath));
    }
}
