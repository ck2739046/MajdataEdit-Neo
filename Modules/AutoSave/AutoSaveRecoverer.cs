/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

using MajdataEdit_Neo.Modules.AutoSave.Contexts;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MajdataEdit_Neo.Modules.AutoSave;

internal class AutoSaveRecoverer : IAutoSaveRecoverer
{
    readonly IAutoSaveContext _globalContext;
    readonly IAutoSaveIndexManager _globalIndex;
    readonly IAutoSaveContext _localContext;
    readonly IAutoSaveIndexManager _localIndex;

    readonly static IReadOnlyCollection<AutoSaveFileInfo> EMPTY_COLLECTION = new List<AutoSaveFileInfo>(0);
    public AutoSaveRecoverer(IAutoSaveContext localContext, IAutoSaveContext globalContext)
    {
        _localContext = localContext;
        _globalContext = globalContext;
        _localIndex = new AutoSaveIndexManager(localContext, AutoSaveManager.LOCAL_AUTOSAVE_MAX_COUNT);
        if (!string.IsNullOrWhiteSpace(_localContext.WorkingPath))
            _localIndex.ChangePath(_localContext.WorkingPath);

        _globalIndex = new AutoSaveIndexManager(globalContext, AutoSaveManager.GLOBAL_AUTOSAVE_MAX_COUNT);
        _globalIndex.ChangePath(_globalContext.WorkingPath);
    }

    public IReadOnlyCollection<AutoSaveFileInfo> GetLocalAutoSaves()
    {
        if (string.IsNullOrWhiteSpace(_localContext.WorkingPath))
            return EMPTY_COLLECTION;

        _localIndex.ChangePath(_localContext.WorkingPath);
        var result = new List<AutoSaveFileInfo>();

        result.AddRange(_localIndex.GetFileInfos());
        result.Sort(delegate (AutoSaveFileInfo f1, AutoSaveFileInfo f2)
        {
            return f2.SavedTime.CompareTo(f1.SavedTime);
        });

        return result;
    }

    public IReadOnlyCollection<AutoSaveFileInfo> GetGlobalAutoSaves()
    {
        var result = new List<AutoSaveFileInfo>();
        result.AddRange(_globalIndex.GetFileInfos());
        result.Sort(delegate (AutoSaveFileInfo f1, AutoSaveFileInfo f2)
        {
            return f2.SavedTime.CompareTo(f1.SavedTime);
        });

        return result;
    }

    public IReadOnlyCollection<AutoSaveFileInfo> GetAllAutoSaves()
    {
        var result = new List<AutoSaveFileInfo>();

        result.AddRange(GetLocalAutoSaves());
        result.AddRange(GetGlobalAutoSaves());

        return result;
    }
    public bool RecoverFile(AutoSaveFileInfo recoveredFileInfo)
    {
        // 原始的maidata路径
        var rawMaidataPath = recoveredFileInfo.RawPath + "/maidata.txt";
        // 原始maidata恢复前备份路径
        var backupMaidataPath = recoveredFileInfo.RawPath + "/maidata.before_recovery.txt";
        // 自动保存maidata路径
        var autosaveMaidataPath = recoveredFileInfo.FileName;
        var recoveryTempPath = Path.Combine(
            recoveredFileInfo.RawPath!,
            $".maidata.recovery.{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(autosaveMaidataPath!, recoveryTempPath);
            using (var stream = new FileStream(recoveryTempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);

            if (File.Exists(rawMaidataPath))
            {
                if (File.Exists(backupMaidataPath))
                    File.Delete(backupMaidataPath);

                File.Replace(recoveryTempPath, rawMaidataPath, backupMaidataPath);
            }
            else
            {
                File.Move(recoveryTempPath, rawMaidataPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to recover auto-save file: {ex}");
            return false;
        }
        finally
        {
            if (File.Exists(recoveryTempPath))
                File.Delete(recoveryTempPath);
        }

        return true;
    }
}
