using Android.App;
using Android.App.Job;
using Android.Content;

namespace TopJournal;

/// <summary>
/// Периодическая синхронизация (каждые 30 мин): API → снимок → дифф → уведомления → виджет.
/// JobScheduler с SetPersisted(true) переживает перезагрузку (нужен RECEIVE_BOOT_COMPLETED — есть).
/// </summary>
[Service(Exported = true, Permission = "android.permission.BIND_JOB_SERVICE")]
public class SyncService : JobService
{
    const int JobId = 1;

    public override bool OnStartJob(JobParameters? args)
    {
        Task.Run(async () =>
        {
            bool ok;
            try { ok = await SyncNowAsync(this); }
            catch { ok = false; }
            JobFinished(args, !ok); // при неудаче попросим повторный прогон
        });
        return true; // работа идёт в фоне
    }

    public override bool OnStopJob(JobParameters? args) => true; // пересинхронизировать позже

    /// <summary>Полный цикл: авторизация → данные → дифф → уведомления → виджет.</summary>
    public static async Task<bool> SyncNowAsync(Context ctx)
    {
        var (login, password) = Store.GetCreds(ctx);
        if (login is null) return false;

        var api = new MystatApi(login, password);
        await api.EnsureAuthAsync(Store.GetRefreshToken(ctx));
        Store.SaveTokens(ctx, api.CurrentTokens!);

        var info = await api.GetUserInfoAsync();
        var visits = await api.GetVisitsAsync() ?? new List<VisitDto>();
        var hw = new List<HomeworkDto>();
        // несданные (0 просрочено, 3 текущее, 2 на проверке) + проверенные (1) для диффа оценок
        foreach (var st in new[] { 0, 3, 2, 1 })
            hw.AddRange(await api.GetHomeworkAsync(st) ?? new List<HomeworkDto>());
        var counters = await api.GetCountersAsync();
        var schedule = await api.GetScheduleRangeAsync(
            DateTime.Today.AddDays(-7), DateTime.Today.AddDays(21)) ?? new List<ScheduleDto>();

        var old = Store.LoadSnapshot(ctx);
        var snap = new Snapshot
        {
            FullName = info?.FullName ?? "",
            GroupName = info?.GroupName ?? "",
            Visits = visits,
            Homework = hw,
            Schedule = schedule,
            Counters = counters,
        };

        var events = Store.Diff(old, snap);
        Store.SaveSnapshot(ctx, snap);

        Notifier.EnsureChannels(ctx);
        Notifier.ShowEvents(ctx, events);
        JournalWidget.RenderAll(ctx);
        return true;
    }

    public static void Schedule(Context ctx)
    {
        var js = (JobScheduler)ctx.GetSystemService(Context.JobSchedulerService)!;
        // не плодим дубли: перепланируем под тем же id
        var builder = new JobInfo.Builder(JobId,
                new ComponentName(ctx, Java.Lang.Class.FromType(typeof(SyncService))))
            .SetPeriodic(30 * 60 * 1000L, 10 * 60 * 1000L) // 30 мин, окно 10 мин
            .SetPersisted(true)
            .SetRequiredNetworkType(NetworkType.Any);
        try { js.Schedule(builder.Build()); }
        catch (Exception) { /* не ронять приложение из-за планировщика */ }
    }
}
