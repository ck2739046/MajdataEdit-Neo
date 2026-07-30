/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

using MajdataEdit_Neo.Modules.AutoSave.Contexts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MajdataEdit_Neo.Modules.AutoSave;

internal class AutoSaveIndexManager : IAutoSaveIndexManager
{
    string? _curPath;
    AutoSaveIndex? _index;
    bool _isReady;
    int _maxAutoSaveCount;

    readonly IAutoSaveContext _context;

    public AutoSaveIndexManager(IAutoSaveContext context)
    {
        _maxAutoSaveCount = 5;
        _context = context;
    }

    public AutoSaveIndexManager(IAutoSaveContext context, int maxAutoSaveCount) : this(context)
    {
        this._maxAutoSaveCount = maxAutoSaveCount;
    }

    public void ChangePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new LocalDirNotOpenYetException("Auto-save directory has not been configured.");

        var normalizedPath = Path.GetFullPath(path);
        if (normalizedPath == _curPath)
        {
            _index = LoadOrCreateIndexFile(normalizedPath);
            _isReady = true;
            return;
        }

        // 路径创建和索引读取都成功后，才提交新的当前路径。
        var index = LoadOrCreateIndexFile(normalizedPath);
        _curPath = normalizedPath;
        _index = index;
        _isReady = true;
    }

    public int GetFileCount()
    {
        if (!IsReady()) throw new AutoSaveIndexNotReadyException("AutoSaveIndexManager is not ready yet.");

        return _index!.Count;
    }

    public List<AutoSaveFileInfo> GetFileInfos()
    {
        if (!IsReady()) throw new AutoSaveIndexNotReadyException("AutoSaveIndexManager is not ready yet.");

        return _index!.FilesInfo;
    }

    public int GetMaxAutoSaveCount()
    {
        return _maxAutoSaveCount;
    }

    public string GetNewAutoSaveFileName()
    {
        var path = _curPath + "/autosave." + GetCurrentTimeString() + ".txt";

        var fileInfo = new AutoSaveFileInfo
        {
            FileName = path,
            SavedTime = DateTimeOffset.Now.ToUnixTimeSeconds(),
            RawPath = _context.RawFilePath
        };
        _index!.FilesInfo.Add(fileInfo);

        _index.Count++;

        // 将变更存储到index文件中
        UpdateIndexFile();

        return path;
    }

    public bool IsReady()
    {
        return _isReady;
    }

    public void RefreshIndex()
    {
        // 先扫描一遍，如果有文件已经被删了就先移除掉
        for (var i = _index!.Count - 1; i >= 0; i--)
        {
            var fileInfo = _index.FilesInfo[i];
            if (!File.Exists(fileInfo.FileName))
            {
                _index.FilesInfo.RemoveAt(i);
                _index.Count--;
            }
        }

        // 然后从this.index.FileInfo的表头开始删除 直到保证自动保存文件的数量符合maxAutoSaveCount的要求
        while (_index.Count > _maxAutoSaveCount)
        {
            var fileInfo = _index.FilesInfo[0];
            File.Delete(fileInfo.FileName!);
            _index.FilesInfo.RemoveAt(0);
            _index.Count--;
        }

        // 将变更存储到index文件中
        UpdateIndexFile();
    }

    public void SetMaxAutoSaveCount(int maxAutoSaveCount)
    {
        this._maxAutoSaveCount = maxAutoSaveCount;
        Console.WriteLine("maxAutoSaveCount:" + maxAutoSaveCount);
    }


    private AutoSaveIndex LoadOrCreateIndexFile(string path)
    {
        CreateDirectoryIfNotExists(path);
        KeepDirectoryHidden(path);

        var indexFilePath = Path.Combine(path, ".index.json");
        if (!File.Exists(indexFilePath))
        {
            var index = new AutoSaveIndex();
            UpdateIndexFile(path, index);
            return index;
        }

        return LoadIndexFromFile(indexFilePath);
    }


    /// <summary>
    ///     若文件夹不存在则创建
    /// </summary>
    /// <param name="dirPath"></param>
    private void CreateDirectoryIfNotExists(string dirPath)
    {
        if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
    }

    /// <summary>
    ///     保证文件夹处于隐藏状态
    /// </summary>
    /// <param name="dirPath"></param>
    private void KeepDirectoryHidden(string dirPath)
    {
        var dirInfo = new DirectoryInfo(dirPath);

        if ((dirInfo.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
            dirInfo.Attributes = FileAttributes.Hidden;
    }

    /// <summary>
    ///     将saveIndex存储到index文件中
    /// </summary>
    private void UpdateIndexFile()
    {
        UpdateIndexFile(_curPath!, _index!);
    }

    private static void UpdateIndexFile(string directory, AutoSaveIndex index)
    {
        var indexPath = Path.Combine(directory, ".index.json");

        var jsonText = JsonSerializer.Serialize(index);
        File.WriteAllText(indexPath, jsonText);
    }

    /// <summary>
    ///     从index文件读取saveIndex
    /// </summary>
    private static AutoSaveIndex LoadIndexFromFile(string indexPath)
    {
        var jsonText = File.ReadAllText(indexPath);
        return JsonSerializer.Deserialize(
                   jsonText,
                   AutoSaveIndexJsonContext.Default.AutoSaveIndex)
               ?? throw new InvalidDataException($"Invalid auto-save index: {indexPath}");
    }

    /// <summary>
    ///     获取当前时间字符串
    /// </summary>
    /// <returns></returns>
    private string GetCurrentTimeString()
    {
        var now = DateTime.Now;

        return now.Year + "-" +
               now.Month + "-" +
               now.Day + "_" +
               now.Hour + "-" +
               now.Minute + "-" +
               now.Second;
    }
}
