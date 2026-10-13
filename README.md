# Oxira API

الخادم الخلفي لموقع [oxira.sa](https://oxira.sa)، ويحل محل n8n بالكامل. وهو أيضًا **مزوّد حساب أوكسيرا الموحّد**: كل مواقع أوكسيرا (oxira.sa و design.oxira.sa و events.oxira.sa) تسجّل الدخول به وتتحقق من رموزه.

- **التقنية:** ASP.NET Core 10 و EF Core 10 على SQL Server (قاعدة بيانات خاصة بهذا الـ API).
- **البنية:** Clean Architecture، مع MediatR 12.5 (CQRS: `ICommand` و`IQuery` و`LoggingBehavior`) و Controllers رفيعة.
- **الخدمات:** JWT، Swagger، SignalR، IIS.
- **التكاملات:** Claude و Zoho SMTP و Moyasar و Cloudflare KV و Meta و WordPress.

## البنية

| المشروع | المحتوى |
|---|---|
| `Oxira.Web.Domain` | الكيانات والقواعد: الحساب والجلسات، الطلبات والمدفوعات والاشتراكات، البوابة، SEO والتواصل الاجتماعي، توقيت الرياض |
| `Oxira.Web.Application` | أوامر واستعلامات MediatR لكل ميزة في `Features/*`، والمنافذ (`IAppDbContext`، `IEmailSender`، `IAnthropic`، `IPaymentGateway`، `ISitePublisher`، `IMetaClient`، `IWordPressClient`، `ISafeFetcher` …)، وقوالب الإيميل |
| `Oxira.Web.Infrastructure` | EF Core / SQL Server، SMTP في الخلفية، Claude، Moyasar، Cloudflare KV، Meta Graph، WordPress REST، جلب المواقع بأمان (SSRF)، DNS TXT، Data Protection |
| `Oxira.Web.Api` | Controllers، JWT، Swagger، SignalR (`/hubs/account` و`/hubs/orders`)، حدود الطلبات، المهام المجدولة |
| `tests/Oxira.Web.Tests` | اختبارات تكامل على SQL Server حقيقي (في CI)، مع بدائل وهمية للبريد والذكاء الاصطناعي وميسر و Cloudflare و Meta و WordPress |

## حساب أوكسيرا الموحّد (الأساس الذي تعتمد عليه APIs أوكسيرا الأخرى)

- **الرمز:** JWT بخوارزمية HS256.
  - Issuer: `https://api.oxira.sa`، و Audience: `oxira`.
  - المفتاح من `Jwt__Key`: 32 حرفًا على الأقل، **ونفس المفتاح في كل APIs أوكسيرا**.
- **الـ Claims:**
  - `sub`: معرّف المستخدم.
  - `email` و`name` و`stamp` و`jti` و`iat`.
  - `role`: `customer` أو `staff` أو `admin`. الدوران الأخيران لفريق أوكسيرا.
  - إضافات يتجاهلها غيره: `sid` (الجلسة)، و`act` (صاحب الحساب عند التبديل لحسابه).
- **المدد:**
  - رمز الدخول: 15 دقيقة.
  - رمز التجديد: 30 يومًا، ويتغيّر مع كل تجديد. يُحفظ مشفّرًا (SHA-256 فقط). إعادة استخدام رمز قديم تُنهي كل جلسات ذلك الدخول.
- **إنهاء الجلسات فورًا:** تغيير كلمة المرور، أو تغيير الدور، أو إيقاف الحساب، أو «الخروج من كل الأجهزة» يغيّر `SecurityStamp`، فتتوقف كل الرموز القديمة.
- **ردود الدخول:** كلها بالشكل `{success, user, token, expiresAt, refreshToken, refreshExpiresAt}`.

| الطريقة | المسار | الوظيفة |
|---|---|---|
| POST | `/api/auth/signup` | حساب جديد، ويُرسل رمز تحقق من 6 أرقام. شرط كلمة المرور: `^(?=.*[A-Za-z؀-ۿ])(?=.*\d).{8,128}$` |
| POST | `/api/auth/verify` | `{email, code, purpose: verify\|login}` ← جلسة |
| POST | `/api/auth/login` | جلسة، أو `success:false` مع `reason: unverified\|twofa` (يُرسل رمز) |
| POST | `/api/auth/resend` · `/forgot` · `/reset` | إعادة الرمز، ونسيت كلمة المرور |
| POST | `/api/auth/refresh` · `/signout` | `{refreshToken}` · `{refreshToken, everywhere}` |
| POST | `/api/auth/magic-link` | `{email, lang, returnUrl}` (التفاصيل تحت الجدول). الرد دائمًا `{success:true}` |
| POST | `/api/auth/magic-link/complete` | `{code}` ← جلسة |
| GET/PUT/POST | `/api/auth/me` · `/profile` · `/password` · `/twofa` | الملف، والصورة، وكلمة المرور، والتحقق بخطوتين |
| POST | `/api/auth/switch` | `{owner}`: العمل على حساب صاحب فريق (`full` أو `viewer`)، أو الرجوع للحساب الشخصي |
| GET/POST | `/api/auth/sessions` · `/sessions/{id}/revoke` · `/sessions/revoke-all` | الأجهزة |
| GET/PUT | `/api/admin/users` · `/{id}/role` · `/{id}/suspend` | فريق أوكسيرا (`staff`/`admin`) |

**تفاصيل الرابط السحري:**

- `returnUrl` يجب أن يبدأ بأصل مسموح من `Auth:MagicLinkOrigins`.
- الرابط المرسل: `returnUrl?k=<40 hex>`، أو `&k=` إن كان في `returnUrl` استعلام.
- الرمز صالح 30 دقيقة ولمرة واحدة.
- الحد: رابط كل دقيقتين، و5 روابط في الساعة لكل بريد.
- البريد الجديد يحصل على حساب مؤكَّد بلا كلمة مرور.

**أسباب الأخطاء التي يعرفها الموقع:** `invalid` و`exists` و`weak` و`email` و`name` و`code` و`expired` و`tries` و`locked` (مع `minutes`) و`wait` (مع `seconds`) و`session` و`viewer` و`forbidden` و`bad_request`.

**قفل الحساب:** بعد 5 محاولات خاطئة يُقفل الدخول 15 دقيقة.

## الواجهات (REST)

**شكل الأخطاء:** `{success:false, reason, error, message}` مع حالة HTTP المناسبة. في بيئة التطوير، أخطاء 500 تحمل `detail`.

**شكل الطلبات:** كل المسارات تقبل JSON، وJSON بنوع `text/plain`، و`application/x-www-form-urlencoded`. حقل `data` الذي يحمل JSON يُدمج في الطلب.

**مصيدة البوتات:** إذا امتلأ الحقل المخفي `company_website`، يرد الـ API بنجاح ولا يحفظ شيئًا.

| المسار | الوظيفة |
|---|---|
| `POST /api/contact` · `/api/partners` · `/api/testimonials` · `GET /api/proof` | التواصل، والشركاء (رابط `?ref=`)، والآراء (لا تظهر قبل موافقة الفريق)، وإثبات الثقة |
| `POST /api/chat` · `GET /api/chat/{sessionId}` | مساعد oxira.sa (Claude + أداة `save_lead`، والسيرفر يرسل الإيميل للفريق) |
| `POST /api/audit` | فحص موقع: 14 فحصًا وأربع درجات. يجلب الموقع بأمان (عناوين عامة فقط، تحقق عند الاتصال، حدود للحجم والوقت والتحويلات) |
| `POST /api/agent/demos` · `GET /api/agent/demos/{id}` · `POST /api/agent/chat` · `POST /api/agent/subscriptions` | تجربة وكيل واتساب، ومنها 4 أمثلة ثابتة |
| `POST /api/studio/builder` · `/api/studio/uploads` · `/api/studio/share` · `GET /api/studio/drafts/{id}` · `GET /api/images/{id}` | المصمم الذكي، ورفع الصور (`i_…_j\|p`)، وروابط المعاينة (`d_…`) |
| `POST /api/orders/site` · `/api/orders/bundle` · `/api/orders/checkout` · `GET /api/orders/{id}/payment` | الطلبات والدفع. الأسعار تُحسب في السيرفر دائمًا |
| `POST /api/payments/moyasar/callback` | إشعار ميسر. لا يُعتمد الدفع إلا بعد قراءة الفاتورة من ميسر بالحالة `paid` والمبلغ الصحيح |
| `GET /api/portal` · `POST /api/portal/{subscribe\|cancel\|booking\|menu\|settings\|checkin}` | بوابة العميل |
| `/api/account/{notifications\|invoices\|tickets\|team\|partner}` | الإشعارات، والفواتير الضريبية، وتذاكر الدعم، والفريق، والشريك |
| `POST /api/sites/external` · `/api/sites/external/{id}/check` | المواقع المستضافة خارج أوكسيرا، والتحقق من ملكيتها بسجل DNS TXT |
| `/api/seo/*` · `/api/social/*` | إضافات SEO والتواصل الاجتماعي، وربط WordPress و Meta |
| `GET /api/status` | حالة الخدمات: فحص كل 15 دقيقة، ونسبة تشغيل 24 ساعة، و96 شريطًا |
| `POST /api/sites/{contact\|booking\|ticket\|review\|chat}` | نماذج مواقع العملاء (من sites worker). الرد تحويل أو صفحة صغيرة |
| `POST /api/admin/jobs/{renewals\|seo\|social\|content\|publish\|status}` | تشغيل مهمة مجدولة الآن (للفريق) |

### SignalR

- **`/hubs/account`:** يحتاج تسجيل الدخول، والرمز في `?access_token=`. الأحداث: `notification` و`ticket` و`payment`. فريق أوكسيرا يستلم أيضًا `ticket` و`order`.
- **`/hubs/orders`:** بدون دخول. نادِ `WatchOrder(orderId)` ← يصل الحدث `payment` بعد تأكد الدفع.

## القواعد المهمة

- **الأسعار:** من قسم `Prices`. الصفحة ترسل الإجمالي، لكن لا يُعتمد أبدًا.
  - الباقات: Starter 1499، و Pro 2499، و Business 4999. الأخيرتان تشملان الدومين، والدومين الإضافي 99.
  - ضريبة القيمة المضافة: 15%.
  - الباقة الجاهزة: 3999.
  - الإضافات الشهرية بالريال: booking 99، و menu 49، و reviews 79، و chat 199، و content 499، و tickets 0 (مع رسوم 5%)، و seo 299، و seogrowth 799، و social 699، و socialpro 1499، و ads 999.
- **الدومين:** لا يُطلب تسجيل دومين لعميل إلا بعد أن **يدفع العميل قيمته ويتأكد الدفع**. إيميل الفريق «سجّل الدومين» يُرسل من `OrderPaidHandler` فقط، وفقط إذا كان الدومين ضمن المدفوع.
- **بعد الدفع:**
  1. يُنشأ حساب للبريد إن لم يكن موجودًا (يكمل العميل بـ«نسيت كلمة المرور» أو بالتسجيل).
  2. يُضاف اشتراك الاستضافة (سنوي)، واشتراك الدومين إن كان مدفوعًا.
  3. يُنشر الموقع على Cloudflare KV.
  4. تُرسل فاتورة ضريبية (`OX-YYYY-000123`) وإيميلات.
  5. يُدفع الحدث `payment` عبر SignalR.
- **التجديد:** مهمة كل ساعة.
  - فاتورة ميسر قبل موعد التجديد بـ 3 أيام.
  - عدم الدفع حتى الموعد ← `past_due` (والخدمة تستمر 7 أيام).
  - بعدها ← `cancelled`، وإلغاء الاستضافة يوقف الموقع.
  - `cancelling` ينتهي في موعده.
- **النشر على Cloudflare:**
  - المفاتيح: `slug:` و`host:` و`draft:` و`img:` و`feat:o<id>` و`seo:o<id>` و`post:o<id>:<slug>` و`ticket:<code>`.
  - العدّاد `hits:o<id>:<yyyy-mm>` يكتبه الـ worker.
  - بدون إعدادات Cloudflare تُسجَّل المفاتيح في السجل فقط (`ISitePublisher`).
- **الدومين الخاص بالعميل (own):** لا يُربط `host:<domain>` إلا بعد إثبات الملكية بسجل TXT (`verify_txt` في البوابة، ثم `POST /api/sites/external/{id}/check`). والدومين المستخدم لموقع مدفوع أو لموقع خارجي مؤكَّد لا يقبله حساب آخر (`409 domain`).
- **التحقق من البريد:** إذا أُكّد البريد أول مرة برابط سحري، تُحذف أي كلمة مرور وُضعت قبل التأكيد وتنتهي كل الجلسات. وعناوين `Auth:AdminEmails` تصبح admin بعد التأكيد فقط.
- **الأسرار المخزّنة:** كلمة مرور تطبيق WordPress ورموز Meta تُشفَّر بـ ASP.NET Data Protection، ومفاتيح التشفير محفوظة في قاعدة البيانات.
- **المساحة المشتركة:** المشاهد (`viewer`) يقرأ فقط، وكل تعديل منه يرجع `403` مع `reason: viewer`. إزالة العضو تُنهي جلساته على ذلك الحساب فورًا.

## ما هو جزئي أو يدوي (عمدًا)

- **تقارير SEO:** Search Console والترتيب و PageSpeed لا تُجلب تلقائيًا بعد. الحقول `report` و`ranks` و`health` تُعرض عند وجود بيانات في `SeoProfiles`، وعدّادات العملاء المحتملين (`leadsNow`) تُقرأ من KV.
- **إعلانات Meta:** تُعتمد في البوابة، ثم يُرسل للفريق إيميل ليطلقها من مدير الإعلانات. أما نشر المنشورات على Facebook و Instagram فتلقائي.
- **تقرير التواصل الاجتماعي الشهري** (`report`): يظهر عند تعبئته، ولا يُجلب من Meta Insights بعد.
- **ميسر:** الحساب الحي لم يُفعّل بعد. في التطوير `Payments__Simulate=true` يفتح صفحة دفع تجريبية (`/api/payments/simulate/{id}`) بدل ميسر.
- **n8n:** لم يعد مطلوبًا. `Site:NotifyWebhook` اختياري لإرسال نسخة من الإشعارات.

## الإعدادات (متغيرات البيئة على السيرفر — لا تضع أي سر في المستودع)

| المتغير | الوصف |
|---|---|
| `ConnectionStrings__Web` | قاعدة SQL Server لهذا الـ API |
| `Jwt__Key` | مفتاح الرموز، نفسه في كل APIs أوكسيرا (32 حرفًا على الأقل) |
| `Smtp__Password` | كلمة مرور تطبيق Zoho لبريد info@oxira.sa |
| `Anthropic__ApiKey` | Claude: المحادثة، المصمم، وكيل واتساب، مسودات SEO والتواصل |
| `Payments__MoyasarSecretKey` · `Payments__WebhookSecret` | ميسر (`sk_live_…`). اضبط `callback_url` في ميسر على `https://api.oxira.sa/api/payments/moyasar/callback` |
| `Cloudflare__AccountId` · `Cloudflare__NamespaceId` · `Cloudflare__ApiToken` | KV الخاص بـ sites worker (صلاحية Workers KV Storage: Edit) |
| `Meta__AppId` · `Meta__AppSecret` | تطبيق Meta. اضبط Redirect URI على `https://api.oxira.sa/api/social/meta/callback` |
| `Site__ApiUrl` · `Site__SiteUrl` · `Site__TeamEmail` | العناوين العامة وبريد الفريق |
| `Auth__AdminEmails__0` | بريد يصبح `admin` من أول تسجيل |
| `Proxy__TrustCloudflare` | `true` خلف Cloudflare: عنوان الزائر من `CF-Connecting-IP`، ويُقبل فقط إذا جاء الاتصال من نطاقات Cloudflare (`Proxy__CloudflareRanges__0…`، والقائمة الرسمية افتراضيًا) |
| `Status__Services__<key>` | روابط صفحة الحالة: `web` و`events` و`design` و`api` و`automation` |

## التشغيل محليًا

```bash
dotnet run --project src/Oxira.Web.Api   # https://localhost:7201/swagger (Payments:Simulate مفعّل في Development)
dotnet test                              # يحتاج SQL Server (OXIRA_TEST_DB)
```

## النشر على IIS

1. **السيرفر:** ثبّت .NET 10 Hosting Bundle، وفعّل **WebSocket Protocol** من Windows Features.
2. **الملفات:** نزّل artifact باسم `oxira-web-api-iis` من آخر تشغيل لـ CI، أو نفّذ `dotnet publish -c Release`.
3. **الموقع في IIS:** أنشئ Site، واجعل Application Pool على **No Managed Code**. ملف `web.config` جاهز ويتضمن WebSockets وحد 20 MB للطلب.
4. **متغيرات البيئة:** ضعها كما في الجدول أعلاه.
5. **قاعدة البيانات:** تُنشأ وتُحدَّث تلقائيًا عند التشغيل. لو تفضّل التطبيق يدويًا، اضبط `Database__MigrateOnStartup=false` واستخدم `db/oxira-web.sql`.
6. **العنوان `api.oxira.sa`:** إما أن توجّهه إلى السيرفر عبر Cloudflare، أو تبقي worker الحالي (`public/cloudflare/api-worker.js` في مستودع الموقع) وتضع `API_ORIGIN`. في الحالة الثانية تذهب `/api/*` و`/hubs/*` إلى IIS، وتبقى روابط n8n القديمة تعمل.
7. **تفعيل الموقع:** أضف المتغير `PUBLIC_API_URL=https://api.oxira.sa` في مستودع oxira-web، وأضف المتغير `OXIRA_API` في sites worker.

## Migrations

**Actions → Add EF Core migration → Run workflow** مع اسم الـ migration: يولّدها ويضيف السكربت `db/oxira-web.sql`، ثم يحفظهما في المستودع. بعدها شغّل CI من Actions، لأن الـ push من الـ workflow لا يشغّله تلقائيًا.
