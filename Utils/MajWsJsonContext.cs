using MajdataEdit_Neo.Types.MajWs;
using System.Text.Json.Serialization;

namespace MajdataEdit_Neo.Utils;

[JsonSerializable(typeof(MajWsRequestBase))]
[JsonSerializable(typeof(MajWsRequestLoad))]
[JsonSerializable(typeof(MajWsRequestPlay))]
[JsonSerializable(typeof(MajWsRequestSetting))]
[JsonSerializable(typeof(MajWsResponseBase))]
[JsonSerializable(typeof(ViewSummary))]
internal partial class MajWsJsonContext : JsonSerializerContext
{
}