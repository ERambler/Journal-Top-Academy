# API журнала Академии ТОП (journal.top-academy.ru)

## Что это

У сайта `journal.top-academy.ru` **есть полноценный JSON API** — то же, что использует
официальное мобильное приложение **MyStat** (App Store id1404383586). Парсить HTML не нужно.

Найдено реверсом публичного JS-бандла `https://journal.top-academy.ru/app.*.js`
(класс `AuthService`, поле `apiUrl`).

- База: `https://msapi.top-academy.ru/api/v2`
- Файлы: `https://fs.top-academy.ru/api/v1/files/<token>`
- Новое SPA-приложение журнала: `https://journalka.top-academy.ru`

## Доступ (важно!)

WAF (DDoS-Guard) пускает только запросы с заголовком:

```
Referer: https://journalka.top-academy.ru/      # или https://journal.top-academy.ru/
```

- **User-Agent не важен** (проверено: пустой, curl/8.0, произвольный — всё проходит).
- Cookies не нужны, TLS-отпечаток не проверяется.
- Без Referer — `403 Forbidden` от nginx ещё до приложения.
- CORS для браузерных запросов с чужих доменов закрыт → клиент должен быть не-браузерным
  (приложение, curl, Tasker). Это ограничение только браузеров.

## Авторизация

### POST /auth/login
```json
{
  "username": "<логин от журнала>",
  "password": "<пароль>",
  "application_key": "6a56a5df2667e65aab73ce76d1dd737f7d1faef9c52e8b8c55ac75f565d8e8a6"
}
```
`application_key` — публичный ключ приложения, зашит в JS-бандл журнала
(`new AuthModel("6a56a...e8a6")`). Поле `id_city` опционально.

Ответ 200:
```json
{
  "access_token": "JWT...",
  "refresh_token": "JWT...",
  "expires_in_refresh": 1795129985,
  "expires_in_access": 1795129985,
  "user_type": 1,
  "user_role": "student",
  "city_data": {"id_city": 444, "name": "Геленджик Колледж", ...}
}
```

### POST /auth/refresh
```json
{"refresh_token": "<refresh_token>"}
```
Возвращает новую пару токенов (тот же формат, что у login). Authorization не нужен.

**Жизненный цикл токенов:** оба JWT живут ~6 часов (поле `exp` внутри). Refresh продлевает
цепочку. Если устройство было офлайн дольше срока жизни refresh-токена — только полный
re-login, поэтому клиент должен хранить логин/пароль (в зашифрованном виде).

Все данные — с заголовком `Authorization: Bearer <access_token>`.

## Эндпоинты данных (проверены на реальном аккаунте, 2026-11-19)

| Метод и путь | Назначение | Параметры |
|---|---|---|
| `GET /settings/user-info` | Профиль: ФИО, группа, поток, баллы coin/gem, фото, флаги видимости фич | — |
| `GET /progress/operations/student-visits` | **Посещаемость + все оценки за уроки**, вся история (~200 записей) | опционально `subject_source`, `subject_id` |
| `GET /progress/operations/student-exams` | Экзамены/формы контроля | — |
| `GET /progress/operations/school-quarterly-grades` | Четвертные оценки (школьная ветка; в колледже пусто) | — |
| `GET /progress/operations/plan-url` | Ссылка на учебный план | — |
| `GET /homework/operations/list` | Список ДЗ | `status` (см. enum), `page` |
| `GET /count/homework` | Счётчики ДЗ по статусам | — |
| `GET /schedule/operations/get-by-date` | Расписание на день | `date_filter=YYYY-MM-DD` |
| `GET /schedule/operations/get-by-date-range` | Расписание за период | `date_start`, `date_end` |
| `GET /schedule/operations/get-month` | Расписание на месяц | `date_filter` |
| `POST /auth/handoff` | Код хендоффа для перехода в journalka-SPA | — |

Ещё в бандле найдены (не проверялись): `/library/operations/list` (учебные материалы),
`/market/customer/*` (маркет), `/homework/operations/create|delete` (сдача ДЗ),
`/homework/evaluation/operations/*` (комментарии проверяющих).

### Enum: статусы ДЗ (`status` и `counter_type`)

| Значение | Смысл |
|---|---|
| 0 | просрочено (EXPIRED) |
| 1 | проверено (DONE) |
| 2 | на проверке (INSPECTION) |
| 3 | текущее/открытое (OPENED) |
| 4 | все (ALL) / в item'ах — «проверено с оценкой» |
| 5 | удалено (DELETED) |

### Формат записи student-visits

```json
{
  "date_visit": "2026-11-19",
  "lesson_number": 1,
  "status_was": 1,                     // 0=пропуск 1=был 2=опоздание
  "spec_id": 52,
  "subject_id": 52,
  "subject_source": 1,
  "teacher_name": "Добрынский Андрей Александрович",
  "spec_name": "Основы информационных технологий КБ",
  "lesson_theme": "Ноутбуки и мобильные устройства",
  "class_work_mark": 5,                // оценка за работу на уроке
  "home_work_mark": null,              // за домашнюю работу
  "control_work_mark": null,           // контрольная
  "lab_work_mark": null,               // лабораторная
  "practical_work_mark": null,         // практическая
  "final_work_mark": null,             // итоговая
  "final_work_mark_type": null
}
```

**Именно этот эндпоинт отвечает на вопрос «какая оценка и за что»: тип работы — имя поля.**

### Формат записи homework/operations/list

```json
{
  "id": 3486,
  "id_spec": 54, "subject_source": 1, "subject_id": 54,
  "id_teach": 140, "id_group": 105,
  "fio_teach": "Песик Валерия Юрьевна",
  "theme": "Урок 7. Импульс тела...",
  "creation_time": "2026-11-02",
  "completion_time": "2026-11-07",     // срок сдачи
  "overdue_time": "2026-11-08",
  "file_path": "https://fs.top-academy.ru/api/v1/files/<token>",
  "comment": "",
  "name_spec": "Физика КБ",
  "status": 4,
  "homework_stud": {                    // null, пока не сдано
    "id": 51648,
    "mark": 1,                          // оценка; auto_mark=true → 1=зачёт автопроверки
    "creation_time": "2026-11-15",
    "auto_mark": true
  },
  "homework_comment": null,
  "cover_image": null
}
```

### settings/user-info (фрагмент)

```json
{
  "full_name": "Журавлёв Светослав Евгеньевич",
  "student_id": 171,
  "group_name": "9/1-КБ-26/1",
  "current_group_id": 105,
  "stream_name": "Колледж 2026",
  "gaming_points": [{"new_gaming_point_types__id": 1, "points": 573},   // coin
                    {"new_gaming_point_types__id": 2, "points": 281}],  // gem
  "photo": "https://fs.top-academy.ru/api/v1/files/<token>",
  "birthday": "2010-10-20",
  "last_date_visit": "2026-11-19 19:53:43"
}
```

## Референс-клиент

`mystat_client.py` в этой папке — рабочий клиент (Python 3.10+, stdlib only):

```
python mystat_client.py          # полный отчёт: посещаемость, средний балл по предметам, ДЗ
python mystat_client.py diff     # детект новых оценок/ДЗ (ядро логики уведомлений)
```

Креды читаются из `config.json` рядом со скриптом.

## Правовая/этическая сторона

API не документировано публично, но это официальный API мобильного приложения MyStat,
вызывается теми же запросами, что и приложение, с тем же публичным application_key.
Логин/пароль передаются только на `msapi.top-academy.ru` (инфраструктура Академии ТОП).
Нижний край вежливости: не чаще одного опроса в ~15–30 минут, retries с backoff.
