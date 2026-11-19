using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace TopJournal;

/// <summary>Первый запуск: ввод логина/пароля журнала, первая синхронизация.</summary>
[Activity(Label = "Журнал ТОП", Exported = true, MainLauncher = true)]
public class LoginActivity : Activity
{
    EditText? _login;
    EditText? _password;
    Button? _btn;
    ProgressBar? _progress;
    TextView? _error;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // тема системы: светлая/тёмная (до SetContentView)
        bool night = (Resources!.Configuration.UiMode & Android.Content.Res.UiMode.NightMask)
                     == Android.Content.Res.UiMode.NightYes;
        SetTheme(night ? Resource.Style.AppTheme_Dark : Resource.Style.AppTheme);

        base.OnCreate(savedInstanceState);
        Notifier.EnsureChannels(this);

        // креды уже сохранены → сразу в дневник (он синхронизируется сам)
        if (Store.HasCreds(this))
        {
            SyncService.Schedule(this);
            StartActivity(typeof(MainActivity));
            Finish();
            return;
        }

        SetContentView(Resource.Layout.login);
        _login = FindViewById<EditText>(Resource.Id.loginEdit)!;
        _password = FindViewById<EditText>(Resource.Id.passwordEdit)!;
        _btn = FindViewById<Button>(Resource.Id.loginBtn)!;
        _progress = FindViewById<ProgressBar>(Resource.Id.loginProgress)!;
        _error = FindViewById<TextView>(Resource.Id.loginError)!;

        _btn.Click += async (_, _) => await DoLoginAsync();
        _password.EditorAction += async (_, _) => await DoLoginAsync();
    }

    async Task DoLoginAsync()
    {
        var login = _login!.Text!.Trim();
        var password = _password!.Text!;
        if (login.Length == 0 || password.Length == 0)
        {
            ShowError("Введите логин и пароль");
            return;
        }

        _btn!.Enabled = false;
        _progress!.Visibility = ViewStates.Visible;
        _error!.Visibility = ViewStates.Gone;

        try
        {
            var api = new MystatApi();
            await api.LoginAsync(login, password);
            Store.SaveCreds(this, login, password);
            Store.SaveTokens(this, api.CurrentTokens!);
            await SyncService.SyncNowAsync(this); // первая выгрузка данных
            SyncService.Schedule(this);
            StartActivity(typeof(MainActivity));
            Finish();
        }
        catch (Exception ex)
        {
            ShowError($"Не удалось войти: {ex.Message}");
            _btn.Enabled = true;
            _progress.Visibility = ViewStates.Gone;
        }
    }

    void ShowError(string msg)
    {
        _error!.Text = msg;
        _error.Visibility = ViewStates.Visible;
    }
}
