using MajdataEdit_Neo.Modules.AutoSave;
using MajdataEdit_Neo.Modules.AutoSave.Contexts;
using MajSimai;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public class AutoSaveModel
{
    readonly InternalAutoSaveContext _localContext;
    readonly InternalAutoSaveContext _globalContext;
    readonly InternalAutoSaveContentProvider _contentProvider = new();
    readonly AutoSaveManager _manager;
    readonly Lock _syncLock = new();

    bool _isUpdating = false;
    DateTime _lastUpdateTime = DateTime.UnixEpoch;

    const int UPDATE_INTERVAL_MS = 5000;

    public bool IsFileChanged
    {
        get => _manager.IsFileChanged;
        set => _manager.IsFileChanged = value;
    }

    public bool Enabled
    {
        get => _manager.Enabled;
        set => _manager.Enabled = value;
    }

    public AutoSaveModel()
    {
        _localContext = new InternalAutoSaveContext(_contentProvider);
        _globalContext = new InternalAutoSaveContext(_contentProvider);
        AutoSaveManager.Initialize(_localContext, _globalContext);
        _manager = AutoSaveManager.Instance;
    }

    public void UpdateContext(string maidataDir)
    {
        _localContext.RawFilePath = Path.Combine(maidataDir, "maidata.txt");
        _localContext.WorkingPath = Path.Combine(maidataDir, ".autosave");
        _globalContext.RawFilePath = Path.Combine(maidataDir, "maidata.txt");
    }

    public void SetContent(string content)
    {
        _contentProvider.Content = content;
    }

    public async Task OnSimaiFileChangedAsync(SimaiFile? simaiFile)
    {
        lock (_syncLock)
        {
            if ((DateTime.Now - _lastUpdateTime).TotalMilliseconds < UPDATE_INTERVAL_MS)
                return;
            if (_isUpdating)
                return;
            _isUpdating = true;
            _lastUpdateTime = DateTime.Now;
        }

        try
        {
            if (simaiFile is null) return;
            var maidata = await SimaiParser.DeparseAsync(simaiFile);
            _contentProvider.Content = maidata;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    internal class InternalAutoSaveContext : IAutoSaveContext, IAutoSaveContentProvider<string>
    {
        public string WorkingPath { get; set; } = Path.Combine(Environment.CurrentDirectory, ".autosave");
        public string RawFilePath { get; set; } = string.Empty;
        public string Content => _contentProvider?.Content ?? string.Empty;

        readonly IAutoSaveContentProvider<string>? _contentProvider;

        public InternalAutoSaveContext(IAutoSaveContentProvider<string>? contentProvider)
        {
            _contentProvider = contentProvider;
        }
        public InternalAutoSaveContext()
        {
        }
    }

    internal class InternalAutoSaveContentProvider : IAutoSaveContentProvider<string>
    {
        public string Content { get; set; } = string.Empty;
    }
}
