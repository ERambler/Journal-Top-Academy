using System.Text.Json.Serialization;

namespace TopJournal;

/// <summary>
/// Источник JSON-сериализации без рефлексии (Source Generator).
/// Обязателен при PublishTrimmed: рефлексионный System.Text.Json вырезается линкером.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Tokens))]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(List<VisitDto>))]
[JsonSerializable(typeof(List<HomeworkDto>))]
[JsonSerializable(typeof(List<CounterDto>))]
[JsonSerializable(typeof(List<ScheduleDto>))]
[JsonSerializable(typeof(Snapshot))]
internal partial class AppJson : JsonSerializerContext;
