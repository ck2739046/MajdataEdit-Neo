/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

using System;

namespace MajdataEdit_Neo.Modules.AutoSave;

internal class AutoSaveIndexNotReadyException : Exception
{
    public AutoSaveIndexNotReadyException()
    {
    }

    public AutoSaveIndexNotReadyException(string message) : base(message)
    {
    }

    public AutoSaveIndexNotReadyException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public override string Message => base.Message;
}

internal class LocalDirNotOpenYetException : Exception
{
    public LocalDirNotOpenYetException()
    {
    }

    public LocalDirNotOpenYetException(string message) : base(message)
    {
    }

    public LocalDirNotOpenYetException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public override string Message => base.Message;
}
