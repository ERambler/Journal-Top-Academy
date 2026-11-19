using Android.App;
using Android.Content;
using Android.OS;
using Android.Graphics;

namespace TopJournal;

/// <summary>
/// Каналы уведомлений и показ событий «что нового».
/// Только платформенные API (minSdk 26 → Notification.Builder с channelId).
/// </summary>
public static class Notifier
{
    public const string ChannelGrades = "grades";
    public const string ChannelHomework = "homework";

    public static void EnsureChannels(Context ctx)
    {
        var nm = (NotificationManager)ctx.GetSystemService(Context.NotificationService)!;
        nm.CreateNotificationChannel(new NotificationChannel(
            ChannelGrades, "Оценки", NotificationImportance.High));
        nm.CreateNotificationChannel(new NotificationChannel(
            ChannelHomework, "Домашние задания", NotificationImportance.Default));
    }

    /// <summary>Одно сводное уведомление на канал: оценки отдельно, ДЗ отдельно.</summary>
    public static void ShowEvents(Context ctx, IReadOnlyList<string> events)
    {
        if (events.Count == 0) return;
        var nm = (NotificationManager)ctx.GetSystemService(Context.NotificationService)!;
        if (!nm.AreNotificationsEnabled()) return;

        var grades = events.Where(e => e.StartsWith("Новая оценка")).ToList();
        var hw = events.Where(e => !e.StartsWith("Новая оценка")).ToList();

        int id = 100;
        if (grades.Count > 0) nm.Notify(id++, Build(ctx, ChannelGrades, "Оценки", string.Join("\n", grades)));
        if (hw.Count > 0) nm.Notify(id++, Build(ctx, ChannelHomework, "Домашние задания", string.Join("\n", hw)));
    }

    static Notification Build(Context ctx, string channel, string title, string text)
    {
        var intent = new Intent(ctx, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        var pi = PendingIntent.GetActivity(ctx, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new Notification.Builder(ctx, channel)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetColor(Color.ParseColor("#2E5BFF"))
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetAutoCancel(true)
            .SetContentIntent(pi)
            .Build();
    }
}
