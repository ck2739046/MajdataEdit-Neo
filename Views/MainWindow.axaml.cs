using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Data;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using AvaloniaEdit.Utils;
using MajdataEdit_Neo.Controls;
using MajdataEdit_Neo.Extensions;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Types.Plugin;
using MajdataEdit_Neo.Models.SimaiAnalyzer;
using MajdataEdit_Neo.Types.MajSetting;
using MajdataEdit_Neo.Types.SimaiAnalyzer;
using MajdataEdit_Neo.ViewModels;
using MsBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MajdataEdit_Neo.Base;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using static MajdataEdit_Neo.Base.MajEnv;
using static MajdataEdit_Neo.Utils.FFmpegChecker;
using MajdataEdit_Neo.ViewModels.SubModels;

namespace MajdataEdit_Neo.Views;

public partial class MainWindow : Window
{
    MainWindowViewModel viewModel => (MainWindowViewModel)DataContext!;

    //window elements
    readonly TextEditor textEditor;
    readonly TextMarkerService markerService;

    readonly SimaiVisualizerControl simaiVisual;

    readonly Button zoomIn, zoomOut;

    readonly NumericUpDown first;
    readonly NumericUpDown speed;


    //behind elements
    readonly DispatcherTimer _debounceTimer;


    string? _currentTooltipMessage;
    private readonly HashSet<Key> _pressedKeys = new();
    bool IsCtrlKeyDown => _pressedKeys.Contains(Key.LeftCtrl) || _pressedKeys.Contains(Key.RightCtrl);

    public MainWindow()
    {
        Console.WriteLine(MajBase);

        var isMac = OperatingSystem.IsMacOS();
        var isLinux = OperatingSystem.IsLinux();

        //pull up MajdataView
        var viewPath = GetPath(isMac || isLinux ? "MajdataView" : "MajdataView.exe");
        if (File.Exists(viewPath) &&
            Process.GetProcessesByName("MajdataView").Length <= 0 &&
            Process.GetProcessesByName("Unity").Length <= 0)
        {
            Process.Start(viewPath);
        }

        // 补齐mac环境变量
        if (isMac)
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH");
            var extraPath = "/usr/local/bin:/opt/homebrew/bin:/opt/homebrew/sbin";
            Environment.SetEnvironmentVariable("PATH", $"{currentPath}:{extraPath}");
        }

        InitializeComponent();

        //setup editor
        textEditor = this.FindControl<TextEditor>("Editor")!;
        textEditor.TextChanged += TextEditor_TextChanged;
        textEditor.TextArea.TextEntered += TextEditor_TextArea_TextEntered;
        textEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        textEditor.TextArea.AddHandler(InputElement.KeyDownEvent, TextEditor_PreviewKeyDown, RoutingStrategies.Tunnel);
        textEditor.Options.HighlightCurrentLine = true;
        textEditor.Options.EnableTextDragDrop = true;
        var _registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        var _install = TextMate.InstallTextMate(textEditor, _registryOptions);
        var registry = new Registry(_install.RegistryOptions);
        _install.SetGrammarFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "simai.tmLanguage.json"));
        markerService = new TextMarkerService(textEditor.Document, textEditor.TextArea.TextView);
        textEditor.TextArea.TextView.BackgroundRenderers.Add(markerService);
        textEditor.PointerMoved += TextEditor_PointerMoved;
        InputMethod.SetIsInputMethodEnabled(textEditor.TextArea, false);
        //setup visualizer
        simaiVisual = this.FindControl<SimaiVisualizerControl>("SimaiVisual")!;
        simaiVisual.PointerWheelChanged += SimaiVisual_PointerWheelChanged;
        simaiVisual.PointerMoved += SimaiVisual_PointerMoved;
        //setup zoom buttons
        zoomIn = this.FindControl<Button>("ZoomIn")!;
        zoomIn.Click += ZoomIn_Click;
        zoomOut = this.FindControl<Button>("ZoomOut")!;
        zoomOut.Click += ZoomOut_Click;
        //setup control panel
        first = this.FindControl<NumericUpDown>("First")!;
        first.PointerWheelChanged += First_PointerWheelChanged;
        speed = this.FindControl<NumericUpDown>("Speed")!;
        speed.PointerWheelChanged += Speed_PointerWheelChanged;
        //this window
        this.KeyDown += MainWindow_KeyDown;
        this.KeyUp += MainWindow_KeyUp;
        this.LostFocus += MainWindow_LostFocus;
        this.Closing += MainWindow_Closing;
        this.Loaded += MainWindow_Loaded;


        //setup debounce timer
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(114.514) };
        _debounceTimer.Tick += _debounceTimer_Tick;
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var setting = viewModel.Settings.Settings.WindowSetting;
        this.Position = new PixelPoint(setting.PosX, setting.PosY);
        this.Width = setting.Width;
        this.Height = setting.Height;

        LoadPluginsToMenu();
        viewModel.RequestPluginActionExecution += ViewModel_RequestPluginActionExecution;

        if (viewModel.Settings.Settings.EditSetting.AutoCheckUpdatesOnStartup)
        {
            await viewModel.Update.CheckUpdateAsync(true);
        }
        await viewModel.Session.Playback.ConnectToPlayerAsync();
    }

    private void LoadPluginsToMenu()
    {
        MenuItem editMenu = this.FindControl<MenuItem>("EditMenu")!;
        MenuFlyout editorFlyout = (MenuFlyout)this.FindControl<TextEditor>("Editor")!.ContextFlyout!;

        foreach (var item in viewModel.Session.Plugins.PluginItems)
        {
            if (item is PluginAction action)
            {
                var geometry = string.IsNullOrEmpty(action.IconKey) ? null
                    : Converters.IconKeyToStreamGeometryConverter.Instance.Convert(
                        action.IconKey,
                        typeof(Avalonia.Media.StreamGeometry),
                        null,
                        System.Globalization.CultureInfo.CurrentCulture)
                    as Avalonia.Media.StreamGeometry;

                var editMenuItem = new MenuItem
                {
                    Header = action.Name,
                    Command = viewModel.ExecutePluginActionCommand,
                    CommandParameter = action,
                    Icon = geometry != null ? new PathIcon { Data = geometry } : null
                };
                var flyoutMenuItem = new MenuItem
                {
                    Header = action.Name,
                    Command = viewModel.ExecutePluginActionCommand,
                    CommandParameter = action,
                    Icon = geometry != null ? new PathIcon { Data = geometry } : null
                };

                editMenu.Items.Add(editMenuItem);
                editorFlyout.Items.Add(flyoutMenuItem);
            }
            else if (item is PluginMenuSeparator)
            {
                editMenu!.Items.Add(new Separator());
                editorFlyout!.Items.Add(new Separator());
            }
        }
    }

    private void ViewModel_RequestPluginActionExecution(PluginAction action)
    {
        if (action.Transform == null) return;

        var selectedText = textEditor.SelectedText;
        if (!string.IsNullOrEmpty(selectedText))
        {
            var newText = action.Transform(selectedText);
            if (newText != selectedText)
            {
                textEditor.Document.Replace(textEditor.SelectionStart, textEditor.SelectionLength, newText);
            }
        }
    }

    bool haveAsked = false;
    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (haveAsked) return;
        e.Cancel = true;
        haveAsked = true;
        viewModel.Settings.SetWindowLastState(this);
        viewModel.OnWindowClosing();
        if (!await viewModel.Session.AskSave())
        {
            Process.GetProcessesByName("MajdataView").FirstOrDefault()?.Kill();
            this.Close();
        }
        else haveAsked = false;
    }

    private void MainWindow_LostFocus(object? sender, RoutedEventArgs e)
    {
        _pressedKeys.Clear();
    }

    private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key);
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Add(e.Key);
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        var seek = textEditor.SelectionStart;
        viewModel.Session.Playback.SetCaretTime(seek, IsCtrlKeyDown);
        viewModel.Session.Playback.CaretLine = textEditor.TextArea.Caret.Line;
    }

    static double? lastX = null;
    private void SimaiVisual_PointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as SimaiVisualizerControl);
        var x = point.Position.X;
        viewModel.IsPointerPressedSimaiVisual = point.Properties.IsLeftButtonPressed;
        if (lastX is null) lastX = x;
        var delta = x - lastX;
        if (point.Properties.IsLeftButtonPressed)
        {
            var docseek = viewModel.Session.Playback.SlideTrackTime((float)delta * 10f / Width, viewModel.Session.SongTrackInfo, viewModel.Session.Doc.CurrentChartData, viewModel.Session.Doc.CurrentSimaiFile?.Offset ?? 0);
            SeekToDocPos(docseek, textEditor);
        }
        lastX = x;
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        viewModel.Session.Playback.SlideZoomLevel(-0.3f);
    }
    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        viewModel.Session.Playback.SlideZoomLevel(0.3f);
    }

    private void First_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        first.Value += (decimal)(e.Delta.Y / 100d);
        e.Handled = true;
    }

    private void Speed_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var value = speed.Value + (decimal)(e.Delta.Y / 10d);
        if (value < (decimal)0.1)
        {
            e.Handled = true;
            return;
        }
        else
        {
            speed.Value = value;
            e.Handled = true;
        }
    }

    private void SimaiVisual_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsCtrlKeyDown)
        {
            viewModel.Session.Playback.SlideZoomLevel(-0.3f * (float)e.Delta.Y);
        }
        else
        {
            var docseek = viewModel.Session.Playback.SlideTrackTime(e.Delta.Y, viewModel.Session.SongTrackInfo, viewModel.Session.Doc.CurrentChartData, (viewModel.Session.Doc.CurrentSimaiFile?.Offset ?? 0));
            SeekToDocPos(docseek, textEditor);
        }
    }

    private void TextEditor_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var area = textEditor.TextArea;

        bool hasShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool hasCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        //fix: when selection is not empty, left/right key will move caret to start/end of selection,
        //instead of moving caret from the start by one char.
        if (!area.Selection.IsEmpty && !hasShift)
        {
            if (e.Key == Key.Right)
            {
                int endOffset = area.Selection.SurroundingSegment.EndOffset;
                area.Caret.Offset = endOffset;
                area.ClearSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                int startOffset = area.Selection.SurroundingSegment.Offset;
                area.Caret.Offset = startOffset;
                area.ClearSelection();
                e.Handled = true;
            }
        }

        //fix: SB AvaloniaEdit ate my ctrl+up/down
        if (hasCtrl && !hasShift)
        {
            if (e.Key == Key.Up)
            {
                EditingCommands.MoveUpByLine.Execute(null, area);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                EditingCommands.MoveDownByLine.Execute(null, area);
                e.Handled = true;
            }
        }

        //fix: ctrl+left/right/up/down jumps something, we dont need this
        if (hasCtrl)
        {
            if (hasShift)
            {
                switch (e.Key)
                {
                    case Key.Left:
                        EditingCommands.SelectLeftByCharacter.Execute(null, area);
                        e.Handled = true;
                        break;
                    case Key.Right:
                        EditingCommands.SelectRightByCharacter.Execute(null, area);
                        e.Handled = true;
                        break;
                    case Key.Up:
                        EditingCommands.SelectUpByLine.Execute(null, area);
                        e.Handled = true;
                        break;
                    case Key.Down:
                        EditingCommands.SelectDownByLine.Execute(null, area);
                        e.Handled = true;
                        break;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case Key.Left:
                        EditingCommands.MoveLeftByCharacter.Execute(null, area);
                        e.Handled = true;
                        break;
                    case Key.Right:
                        EditingCommands.MoveRightByCharacter.Execute(null, area);
                        e.Handled = true;
                        break;
                    // up/down is normal
                }
            }
        }
    }

    private async void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
        await viewModel.Session.Doc.SetFumenContent(((TextEditor)sender!).Text);
        var seek = textEditor.SelectionStart;
        viewModel.Session.Playback.SetCaretTime(seek, IsCtrlKeyDown);
    }
    private void _debounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        TextEditor_DebouncedTextChanged();
    }
    private async void TextEditor_DebouncedTextChanged()
    {
        var fumen = viewModel.Session.Doc.CurrentChartMetadata[viewModel.Session.Doc.SelectedDifficulty].Fumen;

        var diags = await Task.Run(() => SimaiChecker.Check(fumen));
        viewModel.Session.Doc.SimaiDiagnostics = diags;
        markerService.UpdateDiags(diags);

        viewModel.Session.Doc.Signatures.Clear();
        if (viewModel.Session.Doc.CurrentChartData != null)
        {
            var timingList = viewModel.Session.Doc.CurrentChartData.CommaTimings;
            var first = timingList.FirstOrDefault();
            if (first != default)
            {
                var lastNum = first.SignatureNumerator;
                var lastDeno = first.SignatureDenominator;
                foreach (var timing in timingList)
                {
                    if (timing.SignatureNumerator != lastNum || timing.SignatureDenominator != lastDeno)
                    {
                        viewModel.Session.Doc.Signatures.Add((timing.Timing, timing.SignatureNumerator, timing.SignatureDenominator));
                    }
                }
            }
        }
    }
    private void TextEditor_PointerMoved(object? sender, PointerEventArgs e)
    {
        var textView = textEditor.TextArea.TextView;
        var pos = e.GetPosition(textView);
        var visualPos = textView.GetPosition(pos + textView.ScrollOffset);

        string? newMessage = null;
        if (visualPos != null)
        {
            int offset = textEditor.Document.GetOffset(visualPos.Value.Line, visualPos.Value.Column);
            var marker = markerService.GetMarkerAtOffset(offset);
            newMessage = marker?.Message;
        }

        if (_currentTooltipMessage != newMessage)
        {
            _currentTooltipMessage = newMessage;
            if (!string.IsNullOrEmpty(newMessage))
            {
                ToolTip.SetTip(textEditor.TextArea, newMessage);
                ToolTip.SetIsOpen(textEditor.TextArea, true);
            }
            else
            {
                ToolTip.SetIsOpen(textEditor.TextArea, false);
            }
        }
    }

    private void TextEditor_TextArea_TextEntered(object? sender, TextInputEventArgs e)
    {
        if (SimaiCompletionData.SIMAI_COMPLETIONS.ContainsKey(e.Text?[0] ?? '\0'))
        {
            var completionWindow = new CompletionWindow(textEditor.TextArea);
            completionWindow.Closed += (o, args) => completionWindow = null;

            var data = completionWindow.CompletionList.CompletionData;
            data.AddRange(SimaiCompletionData.SIMAI_COMPLETIONS[e.Text![0]]);

            completionWindow.Show();
        }
    }

    private async void FindReplace_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (textEditor.SearchPanel.IsOpened)
            textEditor.SearchPanel.Close();
        else
        {
            textEditor.TextArea.Focus();
            await Task.Delay(100); // focus will cost time, or the searchpanel buttons wont work.
            textEditor.SearchPanel.Open();
        }
    }
    private void SeekToDocPos(Point position, TextEditor editor)
    {
        if (position.Y + 1 > editor.Document.LineCount) return;
        var offset = editor.Document.GetOffset((int)position.Y + 1, (int)position.X);
        editor.Select(offset, 0);
        editor.ScrollTo((int)position.Y + 1, (int)position.X);
        editor.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Session.Playback.RequestSeekToDocPos -= Playback_RequestSeekToDocPos;
            vm.Session.Playback.RequestSeekToDocPos += Playback_RequestSeekToDocPos;
        }
    }

    private void Playback_RequestSeekToDocPos(Point point)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            SeekToDocPos(point, textEditor);
        });
    }
}










