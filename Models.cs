using System.Text.Json.Serialization;

namespace TopJournal;

/// <summary>DTO ответа /auth/login и /auth/refresh.</summary>
public class Tokens
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_in_access")] public long ExpiresInAccess { get; set; }
    [JsonPropertyName("expires_in_refresh")] public long ExpiresInRefresh { get; set; }
    [JsonPropertyName("user_role")] public string UserRole { get; set; } = "";
}

/// <summary>GET /settings/user-info (фрагмент).</summary>
public class UserInfo
{
    [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
    [JsonPropertyName("group_name")] public string GroupName { get; set; } = "";
    [JsonPropertyName("stream_name")] public string StreamName { get; set; } = "";
    [JsonPropertyName("student_id")] public int StudentId { get; set; }
}

/// <summary>GET /progress/operations/student-visits.</summary>
public class VisitDto
{
    [JsonPropertyName("date_visit")] public string DateVisit { get; set; } = "";
    [JsonPropertyName("lesson_number")] public int LessonNumber { get; set; }
    /// <summary>0 = прогул, 1 = был, 2 = опоздание.</summary>
    [JsonPropertyName("status_was")] public int StatusWas { get; set; }
    [JsonPropertyName("spec_id")] public int SpecId { get; set; }
    [JsonPropertyName("spec_name")] public string SpecName { get; set; } = "";
    [JsonPropertyName("teacher_name")] public string TeacherName { get; set; } = "";
    [JsonPropertyName("lesson_theme")] public string LessonTheme { get; set; } = "";

    [JsonPropertyName("class_work_mark")] public int? ClassWork { get; set; }
    [JsonPropertyName("home_work_mark")] public int? HomeWork { get; set; }
    [JsonPropertyName("control_work_mark")] public int? ControlWork { get; set; }
    [JsonPropertyName("lab_work_mark")] public int? LabWork { get; set; }
    [JsonPropertyName("practical_work_mark")] public int? PracticalWork { get; set; }
    [JsonPropertyName("final_work_mark")] public int? FinalWork { get; set; }
    [JsonPropertyName("final_work_mark_type")] public string? FinalWorkType { get; set; }

    /// <summary>(значение, поле) всех ненулевых оценок записи.</summary>
    [JsonIgnore]
    public IEnumerable<(int Mark, string Field)> Marks
    {
        get
        {
            if (ClassWork is int cw) yield return (cw, "работа на уроке");
            if (HomeWork is int hw) yield return (hw, "домашняя работа");
            if (ControlWork is int kw) yield return (kw, "контрольная");
            if (LabWork is int lw) yield return (lw, "лабораторная");
            if (PracticalWork is int pw) yield return (pw, "практическая");
            if (FinalWork is int fw) yield return (fw, FinalWorkType is { Length: > 0 } ? FinalWorkType : "итоговая");
        }
    }
}

/// <summary>GET /homework/operations/list.</summary>
public class HomeworkDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name_spec")] public string NameSpec { get; set; } = "";
    [JsonPropertyName("theme")] public string Theme { get; set; } = "";
    [JsonPropertyName("fio_teach")] public string FioTeach { get; set; } = "";
    [JsonPropertyName("creation_time")] public string CreationTime { get; set; } = "";
    [JsonPropertyName("completion_time")] public string CompletionTime { get; set; } = "";
    [JsonPropertyName("overdue_time")] public string OverdueTime { get; set; } = "";
    [JsonPropertyName("comment")] public string? Comment { get; set; }
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }
    /// <summary>0 просрочено · 1 проверено · 2 на проверке · 3 текущее.</summary>
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("homework_stud")] public HomeworkStud? Stud { get; set; }
}

public class HomeworkStud
{
    [JsonPropertyName("mark")] public int? Mark { get; set; }
    [JsonPropertyName("auto_mark")] public bool AutoMark { get; set; }
    [JsonPropertyName("creation_time")] public string CreationTime { get; set; } = "";
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }
}

public class CounterDto
{
    [JsonPropertyName("counter_type")] public int CounterType { get; set; }
    [JsonPropertyName("counter")] public int Counter { get; set; }
}

/// <summary>GET /schedule/operations/get-by-date-range.</summary>
public class ScheduleDto
{
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("lesson")] public int Lesson { get; set; }
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("finished_at")] public string FinishedAt { get; set; } = "";
    [JsonPropertyName("subject_name")] public string SubjectName { get; set; } = "";
    [JsonPropertyName("teacher_name")] public string TeacherName { get; set; } = "";
    [JsonPropertyName("room_name")] public string RoomName { get; set; } = "";
}

/// <summary>
/// Период отпуск/больничный: занятия в нём исключаются из статистики посещаемости
/// и аналитики (прогулы не считаются), оценки сохраняются.
/// </summary>
public class VacationPeriod
{
    [JsonPropertyName("start")] public string Start { get; set; } = "";
    [JsonPropertyName("end")] public string End { get; set; } = "";
}

/// <summary>Локальный снимок всего, что нужно UI и виджету.</summary>
public class Snapshot
{
    public string SavedAt { get; set; } = "";
    public string FullName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public List<VisitDto> Visits { get; set; } = new();
    public List<HomeworkDto> Homework { get; set; } = new();
    public List<ScheduleDto> Schedule { get; set; } = new();
    /// <summary>counter_type → значение (0 просрочено, 1 проверено, 2 на проверке, 3 текущее, 4 всего).</summary>
    public Dictionary<int, int> Counters { get; set; } = new();

    [JsonIgnore]
    public double Average => AllMarks().DefaultIfEmpty(0).Average();
    [JsonIgnore]
    public double AttendancePct
    {
        get
        {
            if (Visits.Count == 0) return 0;
            int here = Visits.Count(v => v.StatusWas is 1 or 2);
            return 100.0 * here / Visits.Count;
        }
    }

    public IEnumerable<int> AllMarks() => Visits.SelectMany(v => v.Marks.Select(m => m.Mark));
}
