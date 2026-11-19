# Journal-Top-Academy
Журнал "колледж академия топ" для родителей и студентов

## Возможности
Дневник (оценки/расписание/ДЗ) + виджет рабочего стола + уведомления о новых оценках.
Тёмная/светлая тема, самообновление из GitHub Releases, фоновая синхронизация раз в 30 мин
(JobScheduler — в простое 0% CPU). Чистый C# (`net9.0-android`), ноль NuGet-зависимостей,
APK ~3.6 МБ.

## Сборка
.NET SDK 9 + JDK 17 + Android SDK (platform 35), без Android Studio:
```
dotnet build -c Release
```
APK: `bin/Release/net9.0-android/ru.erambler.topjournal-Signed.apk` (Release — обязательно:
Debug-APK использует Fast Deployment и падает при установке файлом).

## Установка
[Последний релиз](https://github.com/ERambler/Journal-Top-Academy/releases/latest) →
`ru.erambler.topjournal-Signed.apk` → открыть на телефоне. Далее приложение обновляется само.

## Подробнее
- [CHANGELOG.md](CHANGELOG.md) — история версий (её же показывает модалка обновления)
- [docs/API-DISCOVERY.md](docs/API-DISCOVERY.md) — как найден API (законный реверс-инжиниринг)
- [docs/API.md](docs/API.md) — справочник API
- Лицензия: [GPL-3.0](LICENSE)
