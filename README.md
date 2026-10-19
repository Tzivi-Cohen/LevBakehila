# LevBakehila – לב בקהילה

מערכת לניהול ושיבוץ מתנדבות לפעילויות עם ילדים עם צרכים מיוחדים.
הרכזת מפרסמת פעילויות התנדבות: מועד, מיקום, מספר מתנדבות נדרש ומיומנויות מומלצות. המתנדבות נרשמות למערכת, מעדכנות שכונה ומיומנויות, מסננות פעילויות לפי שכונה וסוג (עם אפשרות "קודם בשכונה שלי"), ונרשמות לפעילויות פנויות.
המערכת מונעת הרשמה כפולה, הרשמה לפעילויות חופפות וחריגה מהמכסה, גם כשכמה בקשות מגיעות באותו רגע.

## טכנולוגיות

| תחום | טכנולוגיה |
|---|---|
| שרת | C#, .NET 8, ASP.NET Core Web API |
| בסיס נתונים | PostgreSQL 17, Entity Framework Core 8 עם Npgsql (Code First), LINQ |
| אבטחה | JWT Bearer, הרשאות לפי Roles, PBKDF2 Password Hash, User Secrets |
| מיפוי | AutoMapper 14.0.0 עם Profiles (ראו "AutoMapper: הבחירה והסיכון") |
| נוספים | Swagger (עם JWT), NLog, Dependency Injection |
| בדיקות | xUnit, Moq, בדיקות אינטגרציה מול PostgreSQL אמיתי |
| לקוח | React 18 + Vite, ב־Repository נפרד (`LevBakehila.Client`) |

## מבנה השכבות

```
LevBakehila.Core      Entities, Enums, DTOs, Interfaces, Exceptions, Settings  (ללא EF / ASP.NET)
LevBakehila.Data      AppDbContext, Fluent API, Repositories, UnitOfWork, Migrations, Seed   -> Core
LevBakehila.Service   AuthService, UserService, ActivityService, RegistrationService, SkillService, MappingProfile -> Core
LevBakehila.API       Controllers, Middleware, Program.cs (DI, JWT, Swagger, NLog, AutoMapper)  -> Service, Core, Data (רישום DI בלבד)
LevBakehila.Tests     Unit (xUnit + Moq) + Integration (PostgreSQL)
```

* Controllers דקים וקוראים רק ל־Services. אין DbContext ב־Controller.
* כל החוקים העסקיים נמצאים בשכבת Service.
* Service לא מכיר את EF Core. הוא עובד מול `IUnitOfWork` ומול Repositories, דרך Interfaces שמוגדרים ב־Core.
* **Lifetimes:**
  * `DbContext`, ה־Repositories וה־Services הם Scoped: מופע אחד לכל בקשת HTTP. Singleton שמחזיק DbContext היה משתף אותו בין בקשות ("captive dependency"), ו־DbContext אינו Thread-safe.
  * `IPasswordHasher` הוא Singleton, כי הוא חסר מצב.
  * את AutoMapper רושמים עם `AddAutoMapper`: הקונפיגורציה היא Singleton, ו־`IMapper` הוא Transient.

## המשאב המוגבל

המשאב המוגבל הוא **מספר המקומות הפנויים (`AvailableSpots`) בכל פעילות**.
בכל הרשמה הוא יורד ב־1, ובכל ביטול של הרשמה פעילה הוא עולה ב־1. תמיד מתקיים `0 <= AvailableSpots <= MaxVolunteers`, וזה נאכף גם ב־Check Constraint בבסיס הנתונים.

המבחן: "שתי מתנדבות לחצו 'הרשמה' על המקום האחרון באותו רגע. שתיהן קיבלו אישור, ואסור היה ששתיהן יקבלו". המערכת מבטיחה שרק אחת תצליח.

## Optimistic Concurrency

* `VolunteerActivity.RowVersion` ו־`User.Version` (מסוג `uint`) ממופים לעמודת המערכת `xmin` של PostgreSQL, שמשתנה אוטומטית בכל UPDATE של השורה. שניהם מוגדרים כ־Concurrency Tokens (`IsRowVersion()` ב־Fluent API).
* EF שולח `UPDATE ... WHERE "Id" = @id AND xmin = @original`. אם בקשה אחרת הספיקה לשנות את השורה, אף שורה לא מתעדכנת ונזרק `DbUpdateConcurrencyException`.
* `UnitOfWork` מתרגם את החריגה (וגם הפרת אינדקס ייחודי או Deadlock) ל־`ConcurrencyConflictException`. ה־Service מבצע Rollback. `ExceptionHandlingMiddleware` כותב Warning אחד ללוג (עם CorrelationId) ומחזיר **HTTP 409** עם הודעה ברורה, ולא 500.
* הבדיקה העסקית והשמירה רצות באותה טרנזקציה. הבדיקות לבדן (`HasAvailableSpotsAsync`, בדיקת חפיפה) הן בדיקה מוקדמת בלבד, והן לא מספיקות כדי למנוע תחרות.

איפה בדיוק נמצאת התחרות, ואיך היא נעצרת:

| מצב | השורה המשותפת שמכריעה |
|---|---|
| שתי מתנדבות על המקום האחרון | שורת הפעילות (`RowVersion`). רק העדכון הראשון של `AvailableSpots` מצליח |
| אותה מתנדבת, שתי בקשות במקביל לשתי פעילויות חופפות | שורת המשתמשת (`Version`). כל הרשמה מבצעת עליה UPDATE אמיתי (`MarkAsModified`) |
| הרכזת משנה שעות של פעילות, ובאותו רגע מתנדבת שרשומה אליה נרשמת לפעילות אחרת שחופפת לשעות החדשות (חוק 6) | שורת המשתמשת (`Version`). כשהשעות משתנות, `ActivityService.UpdateAsync` מבצע UPDATE לשורה של כל מתנדבת רשומה, באותה טרנזקציה |
| ביטול הרשמה או ביטול פעילות | Status, המכסה ו־Version של המשתמשת נשמרים יחד בטרנזקציה אחת |

כל המצבים האלה נבדקים מול PostgreSQL אמיתי (`LevBakehila.Tests/Integration`). בבדיקות יש גם שני DbContext שקוראים את אותה פעילות: הראשון שומר, והשני מקבל `DbUpdateConcurrencyException`.

## דרישות מוקדמות

* .NET SDK 8 ומעלה (נבדק עם SDK 9, שבונה net8.0).
* PostgreSQL 16 ומעלה, מותקן מקומית (נבדק על PostgreSQL 17).
* Node.js 18 ומעלה (ללקוח React).
* כלי EF, לפקודות Migrations ידניות בלבד: `dotnet tool install --global dotnet-ef --version 8.*`

## התקנת PostgreSQL (Windows)

1. מורידים את ה־Installer של PostgreSQL 16 או 17 מ־https://www.postgresql.org/download/windows (EDB). בהתקנה בוחרים לפחות **PostgreSQL Server** ו־**Command Line Tools**. Port: 5432.
2. בהתקנה מגדירים סיסמה למשתמש־העל `postgres`. שומרים אותה מחוץ לפרויקט.
3. בודקים שהשרת פועל (מתאימים את מספר הגרסה בנתיב):
   ```powershell
   & "C:\Program Files\PostgreSQL\17\bin\psql.exe" -U postgres -c "SELECT version();"
   ```
4. אין צורך ליצור טבלאות או בסיס נתונים ידנית. בעלייה הראשונה השרת יוצר את `LevBakehilaDb`, מחיל את ה־Migrations ומוסיף נתוני דמו.

המלצה לסביבה שאינה פיתוח מקומי: לא לעבוד עם משתמש־העל `postgres`, אלא עם משתמש ייעודי שהוא בעל בסיס הנתונים.

## התקנה והרצה מקומית

מבנה התיקיות: שני Repositories זה לצד זה.
```
Downloads\
  LevBakehila\          השרת (Solution, הקובץ הזה)
  LevBakehila.Client\   לקוח React
```

### 1. הגדרת סודות (User Secrets), פעם אחת

ב־`appsettings.Development.json` יש Connection String **ללא סיסמה**. ב־`appsettings.json` אין Connection String בכלל. הערך המלא, עם הסיסמה, נשמר רק ב־User Secrets ודורס אותו. גם מפתח ה־JWT וסיסמאות משתמשי הדמו נשמרים רק שם, ולא בקוד או ב־Git.

מריצים מתיקיית `LevBakehila` ב־PowerShell. הסיסמה של PostgreSQL מוקלדת בקלט מוסתר:
```powershell
# Connection String כולל סיסמת PostgreSQL
$pw = Read-Host "PostgreSQL password (postgres)" -AsSecureString
$plain = [System.Net.NetworkCredential]::new('', $pw).Password
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=LevBakehilaDb;Username=postgres;Password=$plain" --project LevBakehila.API

# מפתח JWT אקראי (לא מוצג)
$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "Jwt:Key" ([Convert]::ToBase64String($bytes)) --project LevBakehila.API

# סיסמאות משתמשי הדמו (8 תווים לפחות), שאתם בוחרים - לסביבת הדגמה מקומית בלבד (ראו "משתמשי דמו")
$a = Read-Host "Demo Admin password" -AsSecureString
dotnet user-secrets set "Seed:AdminPassword" ([System.Net.NetworkCredential]::new('', $a).Password) --project LevBakehila.API
$v = Read-Host "Demo Volunteer password" -AsSecureString
dotnet user-secrets set "Seed:VolunteerPassword" ([System.Net.NetworkCredential]::new('', $v).Password) --project LevBakehila.API

Remove-Variable pw, plain, bytes, a, v
```
בדיקה, שמציגה רק את שמות המפתחות: `dotnet user-secrets list --project LevBakehila.API | ForEach-Object { ($_ -split ' = ')[0] }`

אם חסרה הגדרה חיונית, השרת לא עולה ומציג הודעה שמסבירה בדיוק מה להגדיר.

### 2. בסיס הנתונים ו־Migrations

**בעליית השרת, ה־Migrations מוחלות אוטומטית וה־Seed Data נוצר.** ה־Seed לא יוצר כפילויות בהרצות חוזרות.
להחלה ידנית, או כדי לראות מה ממתין:
```bash
dotnet ef migrations list  -p LevBakehila.Data -s LevBakehila.API     # קריאה בלבד: מה הוחל ומה ממתין
dotnet ef database update  -p LevBakehila.Data -s LevBakehila.API
```
Migrations קיימות:
1. `InitialCreate`: טבלאות וקשרים.
2. `AddConcurrencyAndIndexes`: Concurrency Tokens על xmin, ואינדקסים ייחודיים על Email ועל (UserId, ActivityId).
3. `AddActivitySkills`: מיומנויות נדרשות לפעילות, בטבלת `ActivitySkills`.
4. `ConvertDateTimesToUtc`: מעבר ל־UTC. ראו "זמנים ואזור זמן".

**Migration 4 על בסיס נתונים קיים:** היא ממירה את עמודות התאריך ל־`timestamptz`, ומשאירה כל ערך באותו רגע בזמן. זה נבדק בבדיקת אינטגרציה, ובהרצה על עותק משוחזר של נתונים אמיתיים.
בהתקנה חדשה אין מה לעשות: בסיס הנתונים נוצר ישר עם 4 ה־Migrations.
אם יש לכם בסיס נתונים קיים מגרסה קודמת, מומלץ לגבות אותו לפני ההרצה הראשונה של השרת. שמרו את הגיבוי מחוץ לתיקיית הפרויקט:
```powershell
& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" -U postgres -d LevBakehilaDb -Fc -f "<backup-folder>\LevBakehilaDb.dump"
# שחזור, אם יידרש, לבסיס נתונים חדש:
& "C:\Program Files\PostgreSQL\17\bin\createdb.exe" -U postgres LevBakehilaDb_restored
& "C:\Program Files\PostgreSQL\17\bin\pg_restore.exe" -U postgres -d LevBakehilaDb_restored "<backup-folder>\LevBakehilaDb.dump"
```

### 3. הרצת השרת
```bash
dotnet run --project LevBakehila.API --launch-profile https
```
Swagger: https://localhost:7257/swagger
1. מריצים `POST /api/auth/login` עם אחד ממשתמשי הדמו ומעתיקים את ה־`token`.
2. לוחצים **Authorize** ומדביקים את ה־token, בלי המילה Bearer.

### 4. הרצת React
```bash
cd ..\LevBakehila.Client
npm install
npm run dev
```
הלקוח רץ על http://localhost:5173 ופונה ל־`https://localhost:7257`. אפשר לשנות את הכתובת עם `VITE_API_URL` בקובץ `.env` (דוגמה ב־`.env.example`).
אם הדפדפן חוסם את התעודה של localhost, מריצים פעם אחת `dotnet dev-certs https --trust`.

### 5. הרצת הבדיקות
```bash
dotnet test                                              # הכל
dotnet test --filter "Category!=Integration"             # Unit בלבד (ללא בסיס נתונים)
dotnet test --filter "Category=Integration"              # Concurrency, Migrations וחוקים מול PostgreSQL אמיתי
```
בדיקות האינטגרציה רצות מול שרת PostgreSQL אמיתי, כי EF Core InMemory לא תומך ב־xmin. פרטי השרת נלקחים מה־User Secrets של ה־API (`ConnectionStrings:DefaultConnection`).
* ה־`Database=` מוחלף תמיד בבסיס נתונים זמני בשם `levbakehila_tests_<guid>`, שנוצר דרך ה־Migrations ונמחק בסיום. `LevBakehilaDb` לא נפגע.
* למשתמש נדרשת הרשאת `CREATEDB` (למשתמש `postgres` היא קיימת).
* אפשר להשתמש בשרת אחר עם משתנה הסביבה `LEVBAKEHILA_TEST_POSTGRES` (Connection String), שגובר על ה־User Secrets.

## משתמשי דמו (לסביבת הדגמה מקומית בלבד)

המשתמשים נוצרים ב־Seed בעלייה הראשונה של השרת. הסיסמה של כל אחד היא הסיסמה שבחרתם בסעיף 1, ב־User Secrets:

| תפקיד | אימייל | שכונה | סיסמה |
|---|---|---|---|
| Admin (רכזת) | admin@levbakehila.local | מרכז העיר | הערך שהוגדר ב־`Seed:AdminPassword` |
| Volunteer | noa@levbakehila.local | רמות | הערך שהוגדר ב־`Seed:VolunteerPassword` |
| Volunteer | shira@levbakehila.local | גילה | הערך שהוגדר ב־`Seed:VolunteerPassword` |
| Volunteer | tamar@levbakehila.local | רמות | הערך שהוגדר ב־`Seed:VolunteerPassword` |

הסיסמאות לא מופיעות בקוד, ב־README או ב־Git. בבסיס הנתונים נשמר רק Hash שלהן (PBKDF2).
**חשוב:** ה־Seed יוצר משתמש רק אם הוא עדיין לא קיים. אם בסיס הנתונים כבר נוצר עם סיסמאות אחרות, שינוי ה־User Secrets לא משנה אותן.
משתמשת חדשה שנרשמת דרך `POST /api/auth/register` או דרך מסך ההרשמה מקבלת תמיד את התפקיד Volunteer.

### תרחיש הדגמה למורה (כ־10 דקות)
1. **Swagger:** פותחים את https://localhost:7257/swagger, מתחברים כ־Admin ולוחצים Authorize. מראים את כל ה־Endpoints, ש־`GET /api/users` מחזיר 200 ל־Admin, ו־403 עם token של noa.
2. **רכזת (React):** מתחברים כ־Admin. במסך "ניהול":
   * יוצרים פעילות עם מכסה של מתנדבת אחת ובוחרים מיומנויות נדרשות. השעות מוזנות בשעון ישראל.
   * מוסיפים מיומנות ב"ניהול מיומנויות".
3. **מתנדבת:** מתחברים כ־noa.
   * ב"הפרופיל שלי" מעדכנים שכונה ומיומנויות.
   * ב"פעילויות" מסננים לפי שכונה או סוג, מדפדפים, ומסמנים "קודם בשכונה שלי".
   * פותחים את הפעילות: רואים מקומות פנויים והתאמת מיומנויות (✓/✗), ונרשמים.
4. **409:**
   * הרשמה נוספת לאותה פעילות, או לפעילות חופפת, מציגה הודעת 409.
   * מתחברים כ־shira באותה פעילות: הכפתור "אין מקומות פנויים" מושבת.
   * תחרות אמיתית מוכחת בבדיקות: `dotnet test --filter "Category=Integration"`. כששתי בקשות מגיעות במקביל, אחת מקבלת 409: "הנתונים עודכנו על ידי משתמשת אחרת באותו רגע".
5. **ביטולים:** ב"ההרשמות שלי" מבטלים הרשמה, והמקום חוזר. הרכזת לוחצת "נרשמות", ואז "ביטול" על הפעילות (`DELETE`, Soft Delete): הפעילות מסומנת "בוטלה", וכל ההרשמות אליה מבוטלות.
6. **לוג:** ב־`LevBakehila.API\bin\Debug\net8.0\logs\levbakehila-<date>.log`, כל שורה כוללת CorrelationId. כל 409 מופיע בשורת Warning אחת.

## Endpoints

| Method | Route | הרשאה | תשובה |
|---|---|---|---|
| POST | /api/auth/register | ציבורי | 201 + Location / 409 (אימייל קיים) |
| POST | /api/auth/login | ציבורי | 200 (JWT) / 401 |
| GET | /api/users (דפדוף ומיון), /api/users/{id} | Admin | 200 / 403 |
| GET / PUT | /api/users/me (שם, שכונה, מיומנויות. לא Role ולא Email) | מחוברת | 200 |
| DELETE | /api/users/{id} | Admin | 204 / 409 (יש הרשמות פעילות) |
| GET | /api/activities: סינון (`neighborhood`, `category`, `onlyAvailable`), "קרוב לבית" (`myNeighborhoodFirst`), מיון ודפדוף | מחוברת | 200 |
| GET | /api/activities/{id} | מחוברת | 200 / 404 |
| GET | /api/activities/{id}/registrations | Admin | 200 / 403 |
| POST | /api/activities | Admin | 201 + Location |
| PUT | /api/activities/{id} (כולל `requiredSkillIds`) | Admin | 200 / 409 |
| DELETE | /api/activities/{id}: **Soft Delete**. ביטול הפעילות וכל ההרשמות הפעילות אליה, בטרנזקציה אחת. השורה נשארת, ו־GET מחזיר `isActive: false` | Admin | 204 |
| POST | /api/registrations | Volunteer | 201 + Location → `/api/registrations/{id}` / 409 |
| GET | /api/registrations/{id} | בעלת ההרשמה או Admin | 200 / 403 / 404 |
| GET | /api/registrations/mine | מחוברת | 200 |
| DELETE | /api/registrations/{id} (ביטול: Status = Cancelled, ללא מחיקה) | בעלת ההרשמה או Admin | 204 / 403 |
| GET | /api/skills, /api/skills/{id} | מחוברת | 200 |
| POST / PUT / DELETE | /api/skills, /api/skills/{id} | Admin | 201 / 200 / 204 |

פרמטרי דפדוף: `pageNumber` (ברירת מחדל 1), `pageSize` (ברירת מחדל 10, מקסימום 50), `sortBy`, `sortDirection` (`asc` / `desc`).
`/api/auth/register` ו־`/api/auth/login` הם המוסכמה המקובלת לאימות, ולכן הם נשארו לפי PLAN 11.1.

## מבנה תשובת שגיאה

כל שגיאה מוחזרת באותו מבנה JSON: ולידציה (400), 401, 403, 404, 409 ו־500.
```json
{ "statusCode": 409, "message": "אין מקומות פנויים בפעילות הזו.", "correlationId": "5aadf36f-..." }
```
בשגיאת ולידציה נוסף השדה `errors`, שממפה כל שדה להודעות שלו. ב־500 לא נחשפים Stack Trace או פרטים פנימיים. ה־`correlationId` מופיע גם בכותרת `X-Correlation-ID` ובכל שורות הלוג של הבקשה.

## Logging

NLog (`nlog.config`) כותב ל־Console ולקובץ `logs/levbakehila-<date>.log`. הקוד עצמו משתמש ב־`ILogger<T>` הרגיל.

| רמה | מתי |
|---|---|
| Information | כל בקשה נכנסת (Method ו־Path בלבד), פעולות משמעותיות (הרשמה, ביטול, יצירה), ובקשה שהלקוח ביטל |
| Warning | חריג שטופל, פעם אחת, ב־`ExceptionHandlingMiddleware`: התנגשות Concurrency, חוק עסקי, 404, 403 או התחברות שנכשלה |
| Error | חריגה לא צפויה (500) |
| Debug | פרטי פיתוח (`appsettings.Development.json`) |

לא נרשמים: סיסמאות, טוקנים, מפתחות, אימיילים או גוף בקשות.

## זמנים ואזור זמן

* **שרת ובסיס נתונים:** הכל ב־UTC. `DateTime.UtcNow`, ועמודות `timestamp with time zone`. Npgsql דוחה DateTime שאינו UTC, ולכן שמירה של שעון מקומי בטעות נכשלת, ולא נשמרת בשקט עם ערך שגוי.
* **API:** תאריכים נכנסים ויוצאים בפורמט ISO 8601. ביציאה תמיד עם `Z`, למשל `2026-10-21T13:00:00Z`. בכניסה מתקבל `Z` או היסט (`+03:00`), וערך בלי היסט נקרא כ־UTC (`UtcDateTimeJsonConverter`).
* **לקוח:** מציג ומקבל שעות בשעון ישראל (`Asia/Jerusalem`), בלי תלות באזור הזמן של הדפדפן, כולל מעבר בין שעון קיץ לשעון חורף. נבדק בדפדפן שמוגדר לניו יורק.

## AutoMapper: הבחירה והסיכון

* **מה בשימוש:** AutoMapper **14.0.0** (רישיון MIT) עם `MappingProfile` אחד בשכבת Service. הרישום ב־`Program.cs` (`AddAutoMapper`), ו־`IMapper` מוזרק ל־Services.
* **למה:** המורה דורשת "מיפוי דרך AutoMapper Profiles".
* **הסיכון, שידוע ומתועד:** לגרסה 14.0.0 יש חולשה ידועה, [GHSA-rvv3-g6hj-g44x / CVE-2026-32933](https://github.com/advisories/GHSA-rvv3-g6hj-g44x). זו חולשת High (CVSS 7.5) מסוג DoS: מיפוי של גרף אובייקטים מקונן מאוד עלול להפיל את התהליך. לגרסאות 14.x ומטה אין תיקון.
* **למה לא גרסה מתוקנת:** הגרסאות המתוקנות (15.1.1 ומעלה, ו־16.1.1 ומעלה) מופצות ברישיון RPL 1.5, או ברישיון המסחרי של Lucky Penny Software, שדורש הרשמה ומפתח. הבחירה בגרסה 14.0.0 נעשתה ביודעין על ידי הסטודנטית.
* **האזהרה לא מוסתרת:** `dotnet build` ו־`dotnet list package --vulnerable` מציגים את NU1903.
* **הקטנת הסיכון בפרויקט:**
  * כל הטיפוסים שממופים שטוחים, או בעומק רמה אחת (רשימת מיומנויות).
  * ל־Request DTOs אין אובייקטים מקוננים בכלל, כך שלקוח לא יכול לשלוח גרף מקונן למיפוי.
  * גודל הבקשה מוגבל ב־Kestrel.
* **בסביבת ייצור:** יש לעבור לגרסה מתוקנת עם רישיון מתאים.
