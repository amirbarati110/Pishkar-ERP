---
title: "Retail ERP — Master Product Design FINAL v4.0"
status: "LOCKED"
language: "fa-IR"
platform: "Windows Desktop"
primary_stack: "C# / .NET 10 LTS / WinUI 3 / Firebird 5"
license_model: "Perpetual / Full Feature"
source_of_truth: true
---

# Retail ERP — Master Product Design FINAL v4.0

> **این فایل تنها Source of Truth پروژه است.**
>
> Scope این نسخه شامل تمام تصمیم‌های نهایی درباره UX مینیمال، میز کار، Shortcut System، گزارش‌ساز چندوجهی، AI Local، نصب یک‌کلیکی و Incremental Update است.
>
> تمام طراحی UI، دیتابیس، API، تست، Installer، Update، AI، Help و کدنویسی باید با این سند سازگار باشد.
> هیچ قابلیت نباید در چند بخش با تعریف مستقل و متفاوت تکرار شود. هر قابلیت فقط یک Owner Section دارد و سایر بخش‌ها فقط به آن ارجاع می‌دهند.
> هر تغییر آینده باید به‌صورت Change Request ثبت شود؛ ویرایش پراکنده، Patch دستی روی Scope و Feature Creep ممنوع است.

---

# فصل 1 — هویت محصول و قوانین غیرقابل تغییر

## 1.1 تعریف محصول

محصول یک **Retail ERP / Business Operating System** برای کسب‌وکارهای کوچک و متوسط است؛ نه یک نرم‌افزار حسابداری سنتی.

حوزه‌های اصلی:

- فروش و POS
- کالا و قیمت‌گذاری
- انبار و WMS
- خرید و تأمین
- تولید و MRP
- CRM و باشگاه مشتریان
- پیام‌رسانی و Automation
- مالی، خزانه و حسابداری
- HRM و حقوق
- لجستیک
- فروش آنلاین و Integration
- BI و گزارش
- Workflow و Tasks
- Local AI
- Help و آموزش داخل نرم‌افزار
- مدیریت تجهیزات، چاپ و اسناد

## 1.2 بازار هدف

- سوپرمارکت و فروشگاه مواد غذایی
- خشکبار و عطاری
- عمده‌فروشی
- پوشاک
- لوازم یدکی
- آرایشی
- تولیدی سبک
- فروشگاه زنجیره‌ای
- سایر SMBها با Template مناسب

## 1.3 اصول محصول

1. **Simple by Default:** قدرت زیاد در Backend، سادگی زیاد در UI.
2. **Local-first:** عملیات اصلی بدون اینترنت کار می‌کنند.
3. **Full Feature:** نسخه اصلی امکانات Core را کامل دارد.
4. **Perpetual License:** استفاده از نسخه خریداری‌شده دائمی است.
5. **Iran-first:** فارسی، RTL، شمسی، تومان/ریال و منطق ایران پیش‌فرض است.
6. **Weak-PC Friendly:** سیستم ضعیف فروشگاهی نیز باید قابل استفاده باشد.
7. **One-click Installation:** نصب و تنظیم پیش‌نیازها خودکار است.
8. **Incremental Update:** تغییر کوچک نباید باعث دانلود مجدد کل نرم‌افزار شود.
9. **AI Local:** راهنما و تحلیل AI باید بدون اینترنت قابل استفاده باشند.
10. **Clean Engineering:** کد باید برای هر برنامه‌نویس حرفه‌ای قابل فهم باشد.

## 1.4 مدل تجاری

- لایسنس دائمی
- تمدید سالانه اجباری: ندارد
- پشتیبانی اجباری: ندارد
- Cloud اجباری: ندارد
- اتصال دائمی به License Server: ندارد
- هزینه SMS/Cloud/Provider فقط در صورت استفاده و بابت هزینه سرویس خارجی است

---

# فصل 2 — معماری فنی، کیفیت کد و Performance

## 2.1 Stack قفل‌شده

- Language: **C#**
- Runtime: **.NET 10 LTS**
- Desktop UI: **WinUI 3 + Windows App SDK**
- UI Markup: **XAML**
- Pattern: **MVVM**
- Architecture: **Clean Architecture + Modular Monolith**
- Local Database: **Firebird 5**
- Cloud/API آینده: **ASP.NET Core**

## 2.2 دیتابیس Local

### تک‌سیستم
Firebird Embedded.

### چند سیستم / چند صندوق
Firebird Server روی سیستم اصلی یا Server داخل LAN.

کاربر نباید هیچ‌کدام از موارد زیر را دستی انجام دهد:

- ساخت Instance
- تعریف Connection String
- بازکردن Port
- نصب دستی Database Runtime
- تنظیم سرویس دیتابیس

Installer مسئول همه موارد است.

## 2.3 Process Architecture

حداقل Processهای مستقل:

### ERP Desktop
UI و Business Operations.

### ERP Local Agent
برای:

- PC-POS
- Printer
- Label Printer
- Scale
- Sync
- Backup
- Device Monitoring
- Background Jobs

### AI Engine
On-Demand؛ مدل دائماً RAM مصرف نمی‌کند.

### Database
Firebird Embedded یا Server.

## 2.4 ساختار Solution

```text
ERP.Domain
ERP.Application
ERP.Infrastructure
ERP.Persistence
ERP.Desktop
ERP.LocalAgent
ERP.AI
ERP.Sync
ERP.Integrations
ERP.Reporting
ERP.Tests
```

## 2.5 قوانین Clean Code

- نام کلاس، متد و متغیر باید معنی‌دار و استاندارد باشد.
- کد Clever، مبهم و وابسته به دانش یک نفر ممنوع است.
- Hidden Coupling ممنوع است.
- Module Boundaryها باید صریح باشند.
- Business Rule نباید داخل Code-behind UI مخفی شود.
- Methodها کوتاه و Single-purpose باشند.
- Public API داخلی ماژول‌ها مستند باشد.
- Exception swallowing ممنوع است.
- Magic Number/String ممنوع است.
- Configuration در فایل/Storage مناسب، نه Hard-code.
- Dependency جدید فقط با دلیل فنی مشخص اضافه شود.
- Secrets هیچ‌وقت داخل Source Code ذخیره نشوند.
- Migrationها Versioned و قابل Rollback/Recovery باشند.

## 2.6 Documentation برای برنامه‌نویس

هر Module باید دارای:

- README کوتاه
- مسئولیت Module
- ورودی/خروجی
- Public Contracts
- Business Events
- Dependencyها
- تست‌های Critical
- Decisionهای معماری خاص

باشد.

## 2.7 سخت‌افزار هدف

- Windows 10 64-bit به بالا
- 4GB RAM: حداقل عملیاتی
- 8GB RAM: پیشنهادشده
- CPU: Core i3 قدیمی / Pentium جدید قابل قبول
- HDD: قابل استفاده
- SSD: پیشنهادشده
- GPU: اجباری نیست

## 2.8 Performance Budget

- Startup روی SSD: هدف حدود 3 ثانیه یا کمتر
- Query معمول Local: هدف کمتر از 200ms
- ثبت فاکتور بدون Integration خارجی: هدف کمتر از 500ms
- Gridهای بزرگ: Pagination + Virtualization
- عکس‌ها: Thumbnail و Lazy Load
- AI Indexing هنگام فعالیت POS: Pause/Throttle
- عملیات سنگین Historical/BI: On-demand
- هیچ Background Job حق ایجاد Lag محسوس در POS ندارد

## 2.9 Transaction و Reliability

عملیات مالی/انبار Critical باید Transactional باشند.

مثال ثبت فروش:

```text
Invoice
Payment
Stock
Accounting
```

یا همه Commit می‌شوند یا عملیات Rollback می‌شود.

Integrationهای خارجی از Outbox Pattern استفاده می‌کنند.

## 2.10 Idempotency

برای موارد زیر اجباری:

- PC-POS
- Online Orders
- Sync
- Tax Submission
- Retryهای Background

تا ثبت یا برداشت تکراری رخ ندهد.

---

# فصل 3 — Iran-first، Design System و UX

## 3.1 زبان و جهت

- زبان پیش‌فرض: فارسی
- Direction: RTL واقعی
- UI Copy: فارسی روان و ساده
- انگلیسی فقط برای موارد فنی یا آینده
- هیچ صفحه اصلی نباید با Layout خارجی/LTR طراحی شود

## 3.2 تقویم و تاریخ

نمایش اصلی:

- تقویم جلالی/شمسی
- ماه‌های فارسی
- هفته مطابق عرف ایران
- فیلترهای «امروز»، «این هفته»، «این ماه»، «سال جاری» بر اساس تقویم ایرانی
- داشبوردها و گزارش‌ها بر اساس تاریخ شمسی

در Data Layer تاریخ استاندارد قابل پردازش نیز نگهداری می‌شود.

## 3.3 تعطیلات و مناسبت‌ها

Calendar Engine باید امکان:

- تعطیلات رسمی ایران
- تعطیلی اختصاصی کسب‌وکار
- مناسبت‌های قابل تنظیم

را داشته باشد.

## 3.4 واحد پول

- تومان / ریال قابل انتخاب
- هزارگان استاندارد
- نمایش فارسی خوانا
- نمونه: `۱۲,۵۰۰,۰۰۰ تومان`

## 3.5 نگارش فارسی

قبل از Release هر UI باید بازبینی:

- املایی
- فاصله‌گذاری
- نیم‌فاصله
- عنوان دکمه
- پیام خطا
- چاپ
- Help

داشته باشد.

**غلط املایی در UI قابل قبول نیست.**

## 3.6 Design System

- Light Mode
- Dark Mode
- High Contrast
- Font Scaling
- Density قابل تنظیم
- فونت فارسی خوانا و Embeddable
- کارت و Border محدود
- Shadow حداقلی
- رنگ فقط برای معنی: Success / Warning / Error / Info

## 3.7 قانون «هر صفحه یک مأموریت»

هر صفحه فقط یک هدف اصلی دارد.

قابلیت‌های زیاد با Progressive Disclosure نمایش داده می‌شوند.

استفاده از صفحه شلوغ با تعداد زیادی Card، KPI و CTA برای عملیات روزانه ممنوع است.

## 3.8 عملیات 2 تا 3 مرحله‌ای

عملیات‌های مهم در 2 تا 3 صفحه واضح انجام می‌شوند.

مثال فروش:

1. مشتری و کالا
2. پرداخت
3. تأیید

Popup فقط برای:

- هشدار کوتاه
- Confirmation
- Notification
- انتخاب ساده

استفاده می‌شود.

## 3.9 Actionها

هر صفحه حداکثر یک CTA اصلی واضح دارد.

Actionهای ثانویه مرتب و کم‌تعداد باشند.

## 3.10 Edit و Delete

در Entityهای قابل تغییر:

- Edit واضح و در دسترس
- Delete همیشه دو مرحله‌ای

### حذف مرحله 1
«از حذف این مورد مطمئن هستید؟»

### حذف مرحله 2
«این عملیات روی سوابق مرتبط اثر دارد. حذف نهایی انجام شود؟»

اسناد مالی Posted حذف فیزیکی نمی‌شوند؛ از Void/Reverse/Cancel استفاده می‌شود.

## 3.11 Undo

برای عملیات کم‌ریسک مانند Archive:

> کالا آرشیو شد — [بازگردانی]

## 3.12 Accessibility

- Keyboard-first
- POS Touch-friendly
- High Contrast
- Font Scaling
- Tab Navigation
- Shortcut قابل تنظیم
- عملکرد مناسب در رزولوشن 1366×768 و Scaleهای رایج ویندوز

---


## 3.13 معماری منوی اصلی و «میز کار»

Navigation سطح اول باید محدود، روشن و Domain-based باشد. قابلیت‌ها نباید به‌صورت Flat و پراکنده در Sidebar ریخته شوند.

ساختار پیشنهادی سطح اول:

- میز کار
- فروش و POS
- مشتریان و CRM
- کالا و قیمت‌گذاری
- انبار و WMS
- خرید و تأمین
- تولید
- مالی و حسابداری
- کارکنان و حقوق
- لجستیک
- گزارش و تحلیل
- یکپارچه‌سازی‌ها
- تنظیمات

### میز کار

«میز کار» صفحه عملیات روزانه و Role-based است، نه Dashboard شلوغ مدیریتی.

باید فقط موارد پرتکرار و Actionable را نشان دهد:

- میانبر عملیات روزانه
- کارهای اخیر
- فاکتورهای نیمه‌تمام / معلق
- تأییدهای در انتظار
- هشدارهای مهم
- Taskهای امروز
- اعلان‌های Critical
- Shortcutهای شخصی
- چند KPI محدود و مرتبط با نقش

نمونه برای صندوق‌دار:
- فروش جدید
- فاکتورهای معلق
- مشتری اخیر
- مرجوعی
- وضعیت صندوق

نمونه برای انباردار:
- رسید
- حواله
- انتقال
- موجودی بحرانی
- شمارش امروز

نمونه برای مدیر:
- فروش امروز
- سود
- هشدارها
- تأییدها
- سفارش‌های در انتظار
- گزارش‌های Pin‌شده

میز کار:
- قابل شخصی‌سازی است.
- Drag & Drop کنترل‌شده دارد.
- Widgetهای پیش‌فرض کم هستند.
- حق ندارد به صفحه‌ای شلوغ با ده‌ها نمودار تبدیل شود.

## 3.14 Keyboard & Shortcut Manager

Shortcut System یک زیرسیستم Core UX است و نباید به چند کلید پراکنده در فرم‌ها تقلیل پیدا کند.

### اهداف

- افزایش سرعت عملیات پرتکرار
- کاهش وابستگی به Mouse
- کاهش حرکت دست
- ثبات رفتاری بین Moduleها
- جلوگیری از Shortcutهای خطرناک
- شخصی‌سازی برای User حرفه‌ای

### معیار طراحی Default Shortcut

برای هر Action باید این موارد بررسی شود:

1. Frequency — چند بار در روز استفاده می‌شود؟
2. Risk — اجرای اشتباه آن چقدر خطرناک است؟
3. Reach — دسترسی فیزیکی روی Keyboard چقدر راحت است؟
4. Convention — با استانداردهای Windows تداخل دارد یا نه؟
5. Context — در چه صفحه/Controlی فعال است؟
6. Permission — آیا User اجازه Action را دارد؟
7. Recovery — در صورت اشتباه امکان Undo/Recovery وجود دارد؟

### طبقه‌بندی Risk

#### High-frequency / Low-risk
کلیدهای ساده و سریع.

مثال:
- جستجو
- عملیات جدید
- مرحله بعد
- ثبت و مورد بعدی

#### Medium-risk
ترکیب یا F-key مشخص.

مثال:
- چاپ
- ویرایش
- کپی
- تعلیق

#### High-risk / Destructive
Shortcut تک‌کلیدی آسان ممنوع است.

مثال:
- حذف
- ابطال سند
- فروش زیر Cost
- Override مالی

این عملیات علاوه بر Shortcut امن، Confirmation/Approval لازم خود را حفظ می‌کنند.

### Actionهای مستقل

این Actionها هرگز نباید با یک Shortcut مبهم ترکیب شوند:

- Save
- Save & New
- Save & Next
- Save & Print
- Save & Close
- Print
- Edit
- Copy
- Search
- Back
- Next Step
- Delete/Cancel

### Fast-entry Workflow

در صفحات پرتکرار مانند:

- فاکتور خرید
- فروش
- دریافت
- پرداخت
- حواله انبار
- ثبت کالا
- ثبت مشتری
- چک
- تولید

کاربر باید بتواند رکورد را ثبت کند و با یک Shortcut وارد رکورد بعدی شود؛ بدون برگشت به List یا Menu.

اگر اطلاعات Unsaved وجود دارد، Reset/New نباید Silent انجام شود.

### Preserve Defaults

در «ثبت و مورد بعدی»، براساس تنظیمات می‌توان موارد کم‌خطر را حفظ کرد:

- شعبه
- انبار
- تاریخ
- ارز
- نوع سند
- Tax Profile

اما Party و Line Items پیش‌فرض پاک شوند مگر Workflow صریحاً خلاف آن را تعیین کند.

### Full Keyboard Navigation

- Tab / Shift+Tab
- Arrow Navigation در Gridها
- Enter برای Action ایمن و Contextual
- Esc برای Back/Close
- Focus Order استاندارد
- Scanner Flow بدون Mouse

Delete داخل Text Input فقط Character را حذف می‌کند و هرگز نباید رکورد را حذف کند.

### User Shortcut Profile

هر User می‌تواند Shortcut Profile شخصی داشته باشد.

قابلیت‌ها:

- Change Binding
- Conflict Detection
- Reserved-key Validation
- Reset to Default
- Import Profile
- Export Profile
- Role Presets
- Search Shortcuts
- نمایش Shortcut کنار Button/Menu

### Permission-aware

اگر Action برای User مجاز نیست:
- Shortcut اجرا نمی‌شود.
- Shortcut Hint برای Action غیرمجاز نمایش داده نمی‌شود یا Disabled است.
- تلاش غیرمجاز در Security Log قابل ثبت است.

### Discoverability

- Shortcut روی Button/Menu نمایش داده شود.
- «میانبرهای این صفحه» وجود داشته باشد.
- Overlay اختیاری برای نمایش Shortcutهای Current Context.
- F1 همچنان راهنمای صفحه را باز می‌کند مگر User آن را در چارچوب مجاز شخصی‌سازی کند.

### Shortcut Telemetry Local

در Experience Layer می‌توان به‌صورت Local و Privacy-aware ثبت کرد:
- چه Shortcutهایی زیاد استفاده می‌شوند
- کجا User از Mouse به Keyboard برمی‌گردد
- چه Shortcutی باعث Cancel/Error شده

این داده فقط برای بهبود Local Experience و پیشنهاد شخصی‌سازی استفاده می‌شود؛ نه ارسال اجباری به Cloud.


---

# فصل 4 — Help، AI Tutor و Experience Layer

## 4.1 Help اجباری

هیچ صفحه‌ای بدون **راهنمای این صفحه** Release نمی‌شود.

Help شامل:

- هدف صفحه
- توضیح اجزا
- مثال
- Tour
- Highlight
- FAQ
- خطاهای رایج
- مراحل عملیات

## 4.2 Machine-readable Help Contract

هر Feature باید Workflow Definition داشته باشد.

نمونه:

```yaml
workflow: expense_payment
steps:
  - page: PaymentCreate
    control: Counterparty
    instruction_fa: "طرف حساب را انتخاب کنید."
  - control: ExpenseCategory
    instruction_fa: "نوع هزینه را مشخص کنید."
  - control: Amount
    instruction_fa: "مبلغ را وارد کنید."
  - action: Continue
  - page: PaymentReview
    action: Confirm
```

## 4.3 AI Tutor

کاربر می‌تواند بپرسد:

> «سند پرداخت اجاره را چطور ثبت کنم؟»

AI باید:

1. منظور را بفهمد.
2. نقش و Permission کاربر را بررسی کند.
3. وضعیت صفحه فعلی را بداند.
4. مسیر دقیق را فارسی توضیح دهد.
5. در صورت درخواست صفحه مربوط را باز کند.
6. Control موردنظر را Highlight/Focus کند.
7. مرحله‌به‌مرحله جلو برود.

## 4.4 سه حالت راهنما

### پاسخ سریع
مسیر و توضیح کوتاه.

### راهنمای مرحله‌ای
Step-by-step.

### راهنمای تعاملی
Navigate + Highlight + Wait for Completion.

## 4.5 سطح پاسخ براساس نقش

کاربر عادی:
> نیازی به سند دستی ندارید؛ عملیات را ثبت کنید تا سند خودکار ساخته شود.

حسابدار:
جزئیات Accounting Mapping را نیز می‌بیند.

## 4.6 Local AI Runtime

سه سطح:

- AI Lite: سیستم 4GB
- AI Standard: 8–16GB
- AI Advanced: 16GB+ / GPU

LLM On-demand Load می‌شود.

## 4.7 AI Data Architecture

ترکیب:

- Structured Query Engine
- Calculation Engine
- Semantic Index
- Event Index
- RAG
- Business Rules
- Local LLM

## 4.8 قانون عدد مالی

AI حق حدس ندارد.

Flow:

```text
Database Query
→ Calculation Engine
→ Verified Result
→ AI Explanation
```

## 4.9 AI Permissions

AI فقط داده‌هایی را می‌بیند که User فعلی اجازه مشاهده آنها را دارد.

## 4.10 Experience Layer

از Phase 0 Eventهای معنادار ثبت می‌شوند:

- Workflow Start/Complete
- Search
- Selection
- Correction
- Warning Shown
- Approval
- Rejection
- Error
- Outcome

اما:

- داده غیرضروری ثبت نمی‌شود
- Privacy رعایت می‌شود
- Retention قابل تنظیم است
- Experience Log جای Audit Log را نمی‌گیرد

هدف: Context بهتر برای AI و تحلیل UX.

---

# فصل 5 — کالا، دسته‌بندی، قیمت‌گذاری و سودآوری

## 5.1 Product Master

حداقل اطلاعات:

- نام
- SKU
- کد داخلی
- Alias
- Barcode
- تصویر
- Brand
- Category
- Units
- Weight/Dimensions
- Tax
- Supplier
- Expiry Settings
- Serial/Batch Tracking
- Price Levels
- Reorder Settings
- Custom Fields

## 5.2 Category

- چندسطحی
- Drag & Drop
- Sort
- Merge
- Move
- Archive
- Hide
- Branch Visibility

نمونه:

```text
مواد غذایی
└── خشکبار
    └── گردو
        └── گردو ایرانی
```

## 5.3 Visibility

کالا/دسته می‌تواند:

- Active
- Inactive
- Archived

باشد و جداگانه از:

- POS
- Online Store
- Purchasing
- Branch
- User Group

مخفی شود.

## 5.4 Units و Conversion

مثال:

- کیلو
- گرم
- عدد
- بسته
- کارتن

و تبدیل:

`1 Carton = 12 Pack`

Bulk Break پشتیبانی شود.

## 5.5 Variants

برای صنف‌هایی مانند پوشاک:

Parent Product + Variantها براساس:

- رنگ
- سایز
- ویژگی سفارشی

## 5.6 Serial / Warranty

- Serial Tracking
- Warranty
- Service History

## 5.7 Price Levels

- Retail
- Wholesale
- VIP
- Partner
- Level N

Customer Default Price Level پشتیبانی شود.

## 5.8 Customer-specific Price

Contract Price برای مشتری خاص.

## 5.9 Pricing Priority

ترتیب قابل تعریف و deterministic:

```text
Customer Contract
→ Promotion
→ Customer Level
→ Branch Price
→ Default Price
```

## 5.10 Inventory Layers و قیمت قدیم/جدید

هر Receipt یک Layer مستقل می‌سازد.

Layer شامل:

- Product
- Warehouse
- Lot/Batch
- Supplier
- Purchase Date
- Quantity
- Purchase Price
- Landed Cost
- Sale Price Version
- Expiry

مثال:

```text
گردو ایرانی

Layer A
7kg
Cost: 600,000
Sale: 750,000

Layer B
30kg
Cost: 720,000
Sale: 890,000
```

Product در لیست تکرار نمی‌شود؛ Layerها در جزئیات قابل مشاهده‌اند.

## 5.11 Costing

پشتیبانی:

- FIFO
- FEFO
- Moving Weighted Average

حتی اگر حسابداری Average باشد، Layer واقعی موجودی حذف نمی‌شود.

## 5.12 Price Change Wizard

هنگام تغییر قیمت:

- کل موجودی
- فقط موجودی جدید
- Batch/Lot انتخابی
- از تاریخ مشخص
- پس از اتمام موجودی قدیم

## 5.13 تحلیل قیمت و سودآوری

این قابلیت Owner Section اصلی تحلیل قیمت است.

برای هر کالا:

- آخرین قیمت خرید
- میانگین قیمت خرید
- کمترین/بیشترین خرید
- Landed Cost
- بهای تمام‌شده
- قیمت فروش فعلی
- تاریخچه قیمت فروش
- سود ریالی
- Gross Margin
- Markup
- ارزش موجودی
- سود کل
- سرمایه خوابیده

## 5.14 Margin و Markup

### Gross Margin

`(Sale Price - Cost) / Sale Price × 100`

### Markup

`(Sale Price - Cost) / Cost × 100`

در UI با دو عنوان متفاوت نمایش داده شوند:

- حاشیه سود
- درصد افزایش نسبت به بهای تمام‌شده

## 5.15 Price Analysis Screen

سه Tab/نمای مرتب:

### خلاصه
خرید آخر، Cost، فروش، سود، Margin، موجودی.

### تاریخچه
نمودار خرید، فروش، Margin، Supplier و Layerها.

### اقدام
- تغییر قیمت
- Margin هدف
- Batch Pricing
- Promotion
- Supplier Comparison
- Purchase Suggestion

## 5.16 Target Margin

برای Product/Category/Branch/Price Level:

- Minimum Margin
- Target Margin
- Target Markup
- Minimum Profit Amount

سیستم قیمت پیشنهادی می‌دهد.

## 5.17 Break-even & Loss Guard

- Break-even Price
- Minimum Allowed Price
- Below-cost Detection
- Low-margin Detection

Policy:

- Warning
- Manager Approval
- Block

## 5.18 Supplier Price Intelligence

- آخرین قیمت هر تأمین‌کننده
- میانگین
- کمترین قیمت
- Lead Time
- شرایط پرداخت
- نرخ مرجوعی/کیفیت

## 5.19 Price & Label Coordination

بعد از تغییر قیمت:

- تشخیص کالاهای نیازمند چاپ لیبل جدید
- انتخاب Layer/Batch
- چاپ گروهی لیبل قیمت جدید

## 5.20 Price AI

AI می‌تواند پاسخ دهد:

- «چرا سود این کالا کم شده؟»
- «قیمت مناسب فروش چقدر است؟»
- «کدام کالا زیر Margin هدف است؟»
- «کدام تأمین‌کننده مناسب‌تر است؟»

فقط با Calculation Engine واقعی.

---

# فصل 6 — فروش، POS و پرداخت

## 6.1 اصل POS

POS باید سریع‌ترین و ساده‌ترین بخش سیستم باشد.

Flow:

1. مشتری + کالا
2. پرداخت
3. تأیید نهایی

## 6.2 مرحله 1 — مشتری و کالا

نمایش فقط:

- Customer
- Product Search / Barcode
- Cart
- Total
- Continue

## 6.3 مشتری نقدی

Default Customer: «مشتری نقدی».

## 6.4 جستجوی مشتری

با:

- موبایل
- نام
- کد
- کد ملی
- کارت باشگاه

## 6.5 ثبت سریع مشتری

در همان POS:

**+ مشتری جدید**

Quick Create:

- موبایل
- نام

Duplicate Check قبل از ثبت.

## 6.6 Customer Context Notification

پس از انتخاب مشتری، Background Check:

- مانده
- فاکتور باز
- بدهی سررسید
- قسط
- چک برگشتی
- سقف اعتبار
- Blacklist
- Wallet
- Loyalty
- Coupon
- Birthday

فقط موارد مهم نمایش داده شوند.

Notification غیرمسدودکننده باشد مگر Policy نیاز به Block داشته باشد.

## 6.7 Credit Policy

- Warning
- Manager Approval
- Block

براساس:

- Credit Limit
- Overdue
- Returned Cheque
- Custom Rules

## 6.8 Product Search

- Barcode
- SKU
- Name
- Alias
- Category
- Scale Barcode
- Serial
- Favorite

Scanner نیاز به Focus دستی نداشته باشد.

## 6.9 Cart Row

نمایش:

- نام
- تعداد
- واحد
- قیمت
- جمع
- `-`
- `+`
- Edit
- More

## 6.10 Edit Line

کاربر مجاز می‌تواند:

- قیمت همین فاکتور را تغییر دهد
- تخفیف مبلغی
- تخفیف درصدی

و در صورت Permission ببیند:

- خرید آخر
- بهای تمام‌شده
- Margin
- آخرین قیمت فروش به همین مشتری
- Minimum Price
- Stock

## 6.11 Invoice Price vs Master Price

دو Action کاملاً جدا:

### ویرایش قیمت همین فاکتور
فقط Invoice Line.

### ویرایش قیمت اصلی کالا
Navigate به Product Master.

هیچ تغییر ضمنی بین این دو مجاز نیست.

## 6.12 Discount Guard

براساس Permission:

- Discount Limit
- Minimum Margin
- Below-cost Rule

نیاز به Manager Approval در صورت عبور.

## 6.13 Multi Active Sales

چند Sale Tab همزمان.

Hold/Resume.

## 6.14 Promotion Engine

- Buy X Get Y
- Percentage
- Fixed
- Bundle
- Category
- Customer/VIP
- Date/Hour
- Branch
- Quantity
- Cart Total
- Coupon

خودکار اعمال شود و دلیل تخفیف قابل مشاهده باشد.

## 6.15 مرحله 2 — پرداخت

روش‌ها:

- PC-POS
- نقد
- Card Manual
- Wallet
- Store Credit
- Gift Card
- نسیه
- اقساط
- چک
- Split Payment

## 6.16 Split Payment

چند روش همزمان تا Balance = 0 یا بدهی مجاز.

## 6.17 PC-POS

مبلغ مستقیم ارسال می‌شود.

Flow:

```text
ERP
→ Local Payment Agent
→ PSP Adapter
→ Terminal
→ Result
→ Verification
→ Invoice Payment
```

ذخیره:

- Trace
- Reference
- Terminal
- Merchant
- Amount
- Status
- Time

## 6.18 Payment State Machine

```text
Pending
Sent
Processing
Approved
Declined
Timeout
Unknown
Reversed
Reconciled
```

Duplicate Charge ممنوع.

## 6.19 PC-POS Setup/Diagnostics

- Provider
- Terminal
- IP/COM
- Device Assignment
- Test
- Connection Status
- Last Success
- Retry/Reversal/Reconciliation

## 6.20 Installment

- مبلغ
- پیش‌پرداخت
- تعداد قسط
- فاصله
- تاریخ شروع
- سود
- جریمه تأخیر

Schedule خودکار.

## 6.21 مرحله 3 — تأیید

خلاصه:

- مشتری
- اقلام
- تخفیف
- مبلغ
- Payment Breakdown
- Loyalty

CTA:

**ثبت و چاپ**

## 6.22 After Sale

Toast:

> فروش با موفقیت ثبت شد.

Action:

- چاپ
- ارسال
- مشاهده
- فروش جدید

Backend:

- Stock
- Accounting
- Customer Balance
- Loyalty
- Commission
- Tax Queue
- Sync Queue
- Messaging Event
- Audit

## 6.23 Accounting Document

در POS نمایش داده نمی‌شود.

مدیر/حسابدار در صورت نیاز:

**مشاهده سند حسابداری**

## 6.24 Cash Register Shift

Start Shift:

- Cash Float

End Shift:

- Expected Cash
- Actual Cash
- Difference
- Reason

## 6.25 Return

- پیدا کردن Invoice
- انتخاب Line
- Qty
- Reason
- Refund Method
- Return to Stock / Damaged / Waste

## 6.26 Exchange

Return + New Sale در یک Workflow با محاسبه مابه‌التفاوت.

## 6.27 Reservation / Deposit

- Reserve Stock
- Deposit
- Expiry
- Release Reservation

## 6.28 Gift Card و Store Credit

- Issue
- Recharge
- Redeem
- Expiry Optional
- History

Store Credit به Customer متصل است.

---

# فصل 7 — مشتری، CRM، باشگاه مشتریان و پیام‌رسانی

## 7.1 Customer 360

- مشخصات
- تماس
- آدرس
- خریدها
- مانده
- پرداخت
- مرجوعی
- Ticket
- Note
- Loyalty
- Preference
- Communication History

## 7.2 Loyalty

- Points
- Tier
- Wallet
- Cashback
- Coupon
- Referral
- Birthday
- Gift Card
- Campaign

## 7.3 Segmentation

- New
- Active
- Loyal
- VIP
- At Risk
- Lost
- High Value
- Debtor
- Product-based

## 7.4 RFM / LRFM

- Recency
- Frequency
- Monetary
- Length

## 7.5 Survey

- CSAT
- NPS
- Rating
- Comment

متصل به:

- Invoice
- Branch
- Cashier
- Online Order

## 7.6 Communication Engine

Provider-independent.

Adapters:

- Kavenegar
- SMS.ir
- Melipayamak
- IPPanel
- Custom REST

در آینده:

- Email
- Push
- WhatsApp
- Telegram/Channels مطابق امکان قانونی و فنی

## 7.7 Triggerها

- Sale
- Birthday
- Inactive Customer
- Installment
- Cheque
- Order Ready
- Stock Available
- Loyalty Level
- Payroll
- Promotion

## 7.8 Automation Builder

```text
Trigger
→ Condition
→ Action
```

مثال:

```text
CustomerInactive30Days
→ LifetimeValue > X
→ GenerateCoupon
→ SendSMS
```

## 7.9 Anti-Spam

- Consent
- Opt-out
- Frequency Cap
- Quiet Hours
- Transactional vs Marketing

---

# فصل 8 — انبار، WMS، تأمین و خرید

## 8.1 Warehouse Structure

- Multi-Warehouse
- Zone
- Location
- Bin

## 8.2 Warehouse Operations

- Receipt
- Issue
- Transfer
- Adjustment
- Stocktake
- Cycle Count
- Reservation
- Release
- Waste
- Internal Consumption
- Sample
- Damaged
- Return

## 8.3 Stock States

- Physical
- Available
- Reserved
- In Transit
- Blocked
- Damaged
- Expired

## 8.4 Batch / Lot / Expiry

- Batch No
- Receipt Date
- Production Date
- Expiry
- Supplier
- Quantity
- Cost

FEFO برای کالاهای تاریخ‌دار.

## 8.5 Reorder Settings

برای Product/Warehouse:

- Min
- Max
- Reorder Point
- Safety Stock
- Preferred Order Qty
- Lead Time
- Preferred Supplier

## 8.6 Smart Replenishment

تحلیل:

- 7/30/90-day Sales
- Trend
- Seasonality
- Available
- Reserved
- In Transit
- Lead Time
- Safety Stock
- Target Sales

خروجی باید همراه دلیل باشد.

## 8.7 Transfer Before Purchase

ابتدا کمبود از سایر انبارها بررسی شود.

در صورت وجود موجودی اضافه:

> پیشنهاد انتقال داخلی

قبل از Purchase Recommendation.

## 8.8 Target-based Planning

کاربر هدف تعیین می‌کند:

- مبلغ فروش
- تعداد
- Category
- Product
- Branch
- Week/Month/Season

سیستم محاسبه می‌کند:

- Forecast
- Required Stock
- Purchase Requirement
- Capital Required
- Expected Margin
- Bottleneck

## 8.9 Purchasing Flow

```text
Purchase Request
→ RFQ
→ Supplier Offers
→ Comparison
→ Purchase Order
→ In Transit
→ Receipt
→ QC
→ Supplier Invoice
→ Payment
```

## 8.10 Supplier Comparison

- Price
- Lead Time
- Quality
- Return Rate
- Discount
- Payment Terms

## 8.11 Partial Receipt

Open Quantity حفظ شود.

مثال:

PO=100  
Received=70  
Open=30

## 8.12 Landed Cost

- Freight
- Insurance
- Packaging
- Handling
- Customs/Other

Allocation روی Cost کالا.

## 8.13 Dead Stock

Definition configurable.

مثال:

No Sale > 60 days.

Action Suggestions:

- Discount
- Transfer
- Bundle
- Return Supplier

## 8.14 Near-expiry

Alert + پیشنهاد Promotion/Transfer.

---

# فصل 9 — تولید، بارکد، لیبل و تجهیزات وزن

## 9.1 BOM / Recipe

مواد اولیه، بسته‌بندی و Output.

## 9.2 Production Order

```text
Plan
→ Material Check
→ Issue Material
→ Produce
→ QC
→ Finished Goods Receipt
```

## 9.3 Production Cost

- Material
- Packaging
- Labor
- Overhead
- Waste

## 9.4 Production Batch

- Batch No
- Manufacturing Date
- Expiry
- Operator
- Qty
- Cost
- QC

## 9.5 Barcode

- EAN-13
- Code128
- QR
- Internal Barcode
- Weighted Barcode

## 9.6 Label Designer

Drag & Drop:

- Logo
- Product
- Price
- Weight
- Barcode
- QR
- Batch
- Production
- Expiry

## 9.7 Scale

- Online Scale
- Label Scale
- Tare
- Weight Barcode

Gross - Tare = Net.

---

# فصل 10 — مالی، خزانه، حسابداری و مالیات

## 10.1 Treasury

- Cash
- Bank
- POS Account
- Petty Cash
- Receivable
- Payable
- Transfer
- Cheque
- Installment
- Loan

## 10.2 Bank Reconciliation

- Import Statement
- Auto Match
- Manual Match
- Difference Detection

## 10.3 Cheque

- Receive
- Issue
- Deposit
- Transfer
- Clear
- Bounce
- Return
- Due Alert
- Print

## 10.4 Accounting

- Double Entry
- Chart of Accounts
- General Ledger
- Subsidiary
- Trial Balance
- P&L
- Balance Sheet
- Cost Center
- Project

## 10.5 Auto Accounting

Business Operation سند تولید می‌کند.

کاربر عملیاتی Debit/Credit نمی‌بیند.

## 10.6 Accounting Mapping

Rules قابل تنظیم برای:

- Sale
- Return
- Purchase
- Cost
- Payment
- Stock
- Production

## 10.7 Financial Period

- Fiscal Year
- Opening
- Closing
- Period Lock

تغییر بعد از Lock فقط با Permission.

## 10.8 Multi-Currency

- Base Currency
- Transaction Currency
- Exchange Rate
- FX Gain/Loss

## 10.9 Multi-Company

چند Company مستقل در یک نصب.

Data Isolation اجباری.

Consolidated Reporting برای آینده.

## 10.10 Tax / Iranian Compliance

Architecture مستقل و Updatable برای:

- قوانین مالیاتی
- شناسه کالا/خدمت
- صورتحساب الکترونیکی
- وضعیت ارسال
- خطا
- اصلاح/ابطال
- Connector سامانه مؤدیان

---

# فصل 11 — HRM، حقوق، دارایی و تعمیرات

## 11.1 Employee Master

- مشخصات
- قرارداد
- Branch
- Department
- Position
- Shift
- Bank
- Documents
- Assets
- Training

## 11.2 Attendance

- Clock In/Out
- Shift
- Leave
- Mission
- Overtime
- Undertime
- Absence

Device Adapter آماده باشد.

## 11.3 Payroll

- Base Salary
- Allowance
- Overtime
- Commission
- Bonus
- Tax
- Insurance
- Loan
- Advance
- Deduction

## 11.4 Payroll Flow

```text
Draft
→ Review
→ Approval
→ Payment
→ Payslip
```

## 11.5 Assets

- Equipment
- PC
- Vehicle
- Refrigerator
- Furniture
- Other

اطلاعات:

- Purchase
- Value
- Location
- Responsible Person
- Depreciation
- Service

## 11.6 Maintenance

- Preventive Maintenance
- Repair Ticket
- Service Date
- Cost
- Technician/Supplier
- Downtime

---

# فصل 12 — لجستیک، سفارش و فروش آنلاین

## 12.1 Inbound Logistics

```text
Shipment
→ Receipt
→ Verification
→ Discrepancy
→ Warehouse
```

## 12.2 Outbound Logistics

```text
Order
→ Picking
→ Packing
→ Driver
→ Delivery
→ Settlement
```

## 12.3 Driver Settlement

- Delivered
- Returned
- Cash
- POS
- Difference
- Final Settlement

## 12.4 Online Store Integration

ERP مرجع اصلی موجودی است.

Sync:

- Product
- Price
- Stock
- Category
- Customer
- Order
- Status

## 12.5 Connector Architecture

Connector مستقل برای:

- WooCommerce
- Shopify
- Custom Store
- Marketplace
- Delivery Platform
- Payment Gateway

## 12.6 Sync Architecture

Outbox/Inbox Pattern.

- Retry
- Offline Queue
- Conflict Detection
- Idempotency

## 12.7 Conflict Rules

Owner مشخص برای هر داده.

مثال:

- Stock: ERP Authority
- Online Order: Source Ownership
- Price: Rule قابل تنظیم

---

# فصل 13 — Dashboard، BI، گزارش، Workflow و Notifications

## 13.1 Dashboard

Drag & Drop و قابل شخصی‌سازی.

Widgetهای ممکن:

- فروش امروز
- سود
- تعداد فاکتور
- میانگین سبد
- نقدینگی
- سفارش آنلاین
- مشتری جدید
- بدهکار
- موجودی کم
- انقضا
- کارکنان حاضر
- چک
- Task
- نمودارها

Dashboard نباید شلوغ باشد؛ Widgetهای پیش‌فرض محدود و قابل حذف باشند.

## 13.2 Global Search

`Ctrl + K`

جستجو در:

- Product
- Customer
- Invoice
- Document
- Supplier
- Employee
- Order
- Cheque
- Setting

نتیجه دارای Quick Action.

## 13.3 Reports

- Sales
- Profit
- Inventory
- Purchase
- Customer
- Supplier
- Cash Flow
- HR
- Payroll
- Production
- Marketing

## 13.4 BI

- Sales Trend
- Margin
- Category Mix
- Hourly Sales
- Day of Week
- Inventory Turnover
- Dead Stock
- Customer Cohort
- RFM/LRFM
- Employee Performance

## 13.5 Report & Analytics Studio

گزارش‌ساز یکی از Flagship Capabilityهای ERP است و باید از تمام Moduleها داده مجاز دریافت کند.

دو سطح UX دارد:

### سطح 1 — گزارش‌های آماده

برای User عادی:

- فروش امروز
- سود این ماه
- موجودی
- بدهکاران
- پرفروش‌ها
- گردش کالا
- خرید
- چک‌ها
- حقوق
- تولید

با Filter ساده و خروجی سریع.

### سطح 2 — Analytics Studio

برای مدیر و User حرفه‌ای، بدون نیاز به SQL.

قابلیت‌ها:

- Data Source
- Metrics
- Dimensions
- Fields
- Filter
- Advanced Filter
- Group
- Sort
- Calculated Measureهای کنترل‌شده
- Pivot / Cross-tab
- Drill-down
- Drill-through
- Period Comparison
- Branch Comparison
- Scenario/Target Comparison
- Top/Bottom N
- Conditional Highlight
- Table
- KPI
- Line / Bar / Pie / Scatter در موارد مناسب
- Narrative/Textual Interpretation
- Saved Report Definition
- Favorite / Pin to Workbench
- Shared Definition براساس Permission
- Print
- Excel
- PDF
- CSV

هر گزارش باید بتواند بین Table، Pivot، Chart و Narrative جابه‌جا شود؛ «گزارش نموداری» نباید Module جدا و پراکنده باشد.

### گزارش چندوجهی

نمونه درخواست:

> فروش سه ماه اخیر گردو را به تفکیک شعبه، تعداد مشتری، قیمت خرید، سود و حاشیه سود با سه ماه قبل مقایسه کن.

یک Report Definition می‌تواند همزمان:
- چند Dimension
- چند Metric
- Comparison Period
- Drill-down
- Chart
- Narrative

داشته باشد.

### Report Permissions

Report Engine باید Row/Field/Company/Branch Permissionهای User را رعایت کند.

وجود Report Builder مجوز دورزدن Permission نیست.

### Query Safety

- Queryهای سنگین محدود/بهینه شوند.
- Dataset بزرگ Paginated/Aggregated شود.
- Weak-PC Constraint رعایت شود.
- Report Query نباید POS را Block کند.

## 13.6 AI Report Builder

Local AI باید بتواند درخواست فارسی مدیر را به Report Definition تبدیل کند.

نمونه:

> «کالاهایی که فروششان خوب است ولی سودشان کم است نشان بده.»

AI باید:

1. Intent را استخراج کند.
2. Data Sourceهای مجاز را تعیین کند.
3. Metrics/Dimensions را پیشنهاد دهد.
4. Filter و Period را مشخص کند.
5. Query/Calculation را از Engine واقعی بگیرد.
6. نوع Table/Pivot/Chart مناسب را انتخاب کند.
7. Narrative فارسی بسازد.
8. Report Definition را قبل از ذخیره قابل بازبینی کند.

AI حق ندارد برای پرکردن Gap عدد بسازد.

عددها فقط از:
- Verified Query
- Calculation Engine
- Aggregation Engine

می‌آیند.

کاربر می‌تواند:

- ذخیره گزارش
- ویرایش Report Definition
- افزودن به میز کار
- Export
- چاپ
- اجرای دوباره
- Duplicate Template

را انجام دهد.

## 13.7 Narrative Analytics

سیستم می‌تواند کنار گزارش توضیح فارسی قابل فهم ارائه کند.

مثال:

> فروش این گروه نسبت به دوره قبل افزایش داشته، اما حاشیه سود کاهش یافته است. بیشترین اثر از افزایش بهای تمام‌شده دو Batch اخیر آمده است.

هر جمله تحلیلی که عدد دارد باید Traceable به Metric واقعی باشد.


## 13.8 Workflow Engine

مثال:

- Purchase > X → Manager Approval
- Discount > X → Supervisor
- Leave > N days → HR
- Inventory Adjustment → Warehouse Manager

## 13.9 Task Engine

Manual + Automatic.

## 13.10 Notification Center

Types:

- Info
- Success
- Warning
- Critical

Action:

- Snooze
- Mark Read
- Assign
- Open Source

## 13.11 Central Attention Center

فقط موارد Actionable:

- Stock Out
- Expiry
- Cheque Due
- Customer Risk
- Order Waiting
- Backup Problem
- Payment Unknown
- Sync Error

---

# فصل 14 — Import/Export، چاپ، Device Center و اسناد

## 14.1 Import Center

Core Importها:

- Product
- Customer
- Supplier
- Opening Stock
- Price
- Other Master Data در صورت نیاز

## 14.2 Excel Template

برای هر Import:

- فایل نمونه آماده
- Header فارسی
- نمونه یک یا دو Row
- توضیح ستون‌های اجباری/اختیاری

## 14.3 Import Wizard

```text
Select File
→ Detect Columns
→ Map Columns
→ Preview
→ Validate
→ Import
→ Result
```

## 14.4 Duplicate Policy

برای Product:

- Barcode
- SKU
- Name + Rule

برای Customer:

- Mobile
- National ID
- Customer Code

رفتار:

- Skip
- Update
- Ask
- Merge در موارد مجاز

## 14.5 Validation

پیام خطای فارسی واضح:

- شماره موبایل نامعتبر
- بارکد تکراری
- قیمت نامعتبر
- دسته‌بندی ناموجود
- Required Field Missing

## 14.6 Import Rollback

Import Batch باید قابل Rollback امن باشد تا اشتباه فایل باعث خرابی Master Data نشود.

## 14.7 Competitor Migration

Adapter برای خروجی‌های قانونی/قابل‌دسترسی:

- Holo
- Mahak
- Dasht
- Baran

## 14.8 Export

- Excel
- CSV
- PDF
- Print

در Gridهای مناسب.

## 14.9 Document Management

Attachment برای:

- Product
- Customer
- Supplier
- Invoice
- Employee
- Asset
- Other relevant entities

Formats:

- PDF
- Image
- Contract
- Receipt
- Warranty

## 14.10 Print & Device Center

یک مرکز واحد، نه تنظیمات پراکنده.

Device Profiles:

- Receipt Printer
- A4 Printer
- Label Printer
- Customer Display
- Scale
- POS Terminal

## 14.11 Sales Receipt Setup

- 58mm / 80mm / Custom
- Logo
- Header
- Footer
- Customer Info
- Tax
- Discount
- QR
- Barcode
- Copy Count
- Margin
- Density

## 14.12 Label Printer Setup

- Width/Height
- Gap
- Margin
- Density
- Speed
- Orientation
- Copies
- Template
- Test Print

## 14.13 Per-workstation Profile

هر Station تنظیم چاپ و Device مستقل داشته باشد.

مثال:

```text
Cashier-1:
Sales Receipt → 80mm Printer

Warehouse-1:
Product Label → 50x30 Label Printer
```

## 14.14 Print Preview

قبل از چاپ Designer/Template:

- Preview
- Test Print
- Save Profile

---

# فصل 15 — امنیت، نصب، Backup، Update، تست و Delivery

## 15.1 User / Role / Permission

Scope:

- Company
- Branch
- Module
- Action
- Sensitive Field

## 15.2 Permission Granularity

مثال Cashier:

- View Cost: No
- Edit Master Price: No
- Discount <= X: Yes
- Delete Posted Invoice: No

## 15.3 Manager Approval

PIN شخصی یا روش امن مجاز.

رمز مشترک ممنوع.

Audit:

- درخواست‌کننده
- تأییدکننده
- زمان
- دلیل
- Old/New Value

## 15.4 Audit Log

جدا از Technical Log.

برای:

- Price Change
- Discount Override
- Delete/Archive
- Accounting Change
- Inventory Adjustment
- Approval
- Security Action

## 15.5 Logging

سه نوع:

- Technical Log
- Business Audit
- Security Log

Rotation و Retention مشخص.

## 15.6 Privacy

- Data اصلی Local
- Cloud فقط داده لازم
- AI Local اطلاعات را برای پاسخ Local به اینترنت ارسال نمی‌کند
- Experience Data حداقلی و Permission-aware

## 15.7 One-click Installer

کاربر فقط Setup را اجرا می‌کند.

Installer مسئول:

- Runtime
- Firebird
- Local Agent
- App Files
- Service Registration
- Shortcut
- Initial Database
- Required Folder/Permission
- First-run Initialization
- Hardware Setup Wizard در صورت نیاز

کاربر نباید پیش‌نیاز فنی دستی نصب کند.

## 15.8 Installation Flow

```text
Run Setup
→ Install
→ Business Name
→ Business Type
→ Admin User
→ Start
```

## 15.9 Business Templates

- Grocery
- Food
- Dry Fruits
- Clothing
- Spare Parts
- Cosmetics
- Production
- Wholesale
- Other

Template فقط Default UX/Fields را تنظیم می‌کند؛ Core مشترک است.

## 15.10 Backup

Automatic:

- Daily
- Weekly

Destination:

- Local Disk
- External Disk
- NAS
- Optional Cloud

## 15.11 Backup Security

- Encryption
- Verification
- Health Check

سیستم باید فقط وجود فایل را چک نکند؛ Backup باید قابل اعتبارسنجی باشد.

## 15.12 Migration PC

Export Package:

`Company.erpbackup`

شامل:

- Database
- Attachments
- Settings
- Templates
- AI Index قابل بازسازی/انتقال
- Users
- Workflows

## 15.13 Incremental / Delta Update

**اصل قفل‌شده:** برای تغییر کوچک، دانلود کامل Installer ممنوع است.

Update Package باید Componentized باشد.

Componentهای مستقل نمونه:

- Core App
- UI Resources
- Help Content
- AI Prompt/Knowledge Package
- Connector
- Local Agent
- Report Templates
- Tax Connector

## 15.14 Update Manifest

هر Release:

- Version
- Component Version
- File Hash
- Dependency
- Migration Requirement
- Minimum Compatible Version
- Signature

## 15.15 Delta Strategy

Updater فقط فایل/Blockهای تغییرکرده را دانلود می‌کند.

Fallback به Full Package فقط وقتی:

- Delta ناممکن است
- Version خیلی قدیمی است
- Integrity Check شکست خورده

## 15.16 Update Process

```text
Check
→ Show Size & Changes
→ Download Delta
→ Verify Signature/Hash
→ Backup if Needed
→ Apply
→ Migrate
→ Health Check
→ Complete
```

## 15.17 Update Rollback

در صورت Failure:

- Restore Previous Binary
- Database Migration Recovery طبق Strategy
- App قابل اجرا باقی بماند

## 15.18 Update Channels

- Stable
- Optional Preview/Internal

مشتری عادی پیش‌فرض Stable.

## 15.19 Diagnostics

Health Center:

- Database
- Backup
- Printer
- Label Printer
- PC-POS
- Scale
- Network
- Sync
- Disk
- AI

CTA:

**بررسی سلامت سیستم**

## 15.20 Error Handling

خطای Technical مستقیماً به کاربر نمایش داده نشود.

مثال درست:

> اتصال به کارتخوان برقرار نیست.

Action:

**بررسی اتصال**

Technical Details فقط برای Diagnostic/Support.

## 15.21 Test Strategy

- Unit
- Integration
- Database
- UI Critical Flow
- POS
- Payment
- Hardware Mock
- Sync
- Migration
- Backup/Restore
- Update/Rollback
- Performance

## 15.22 Critical Automated Scenarios

حداقل:

- Cash Sale
- PC-POS Sale
- Split Payment
- Return
- Exchange
- Customer Credit
- Purchase Receipt
- Warehouse Transfer
- Stocktake
- Old/New Price Layers
- Price/Margin Calculation
- Production
- Payroll
- Backup Restore
- Offline/Online Sync
- Incremental Update

## 15.23 Definition of Done

هیچ Feature ای Done نیست مگر اینکه:

1. Domain Logic کامل باشد.
2. UI ساده و RTL باشد.
3. Iran-first Rule رعایت شده باشد.
4. Validation وجود داشته باشد.
5. Permission وجود داشته باشد.
6. Audit در صورت نیاز وجود داشته باشد.
7. Help فارسی آماده باشد.
8. AI Tutor Metadata آماده باشد.
9. Error/Empty/Loading State کامل باشد.
10. Tests وجود داشته باشد.
11. Performance بررسی شده باشد.
12. Weak-PC Constraint بررسی شده باشد.
13. متن فارسی بازبینی املایی شده باشد.
14. Backup/Update Compatibility در صورت ارتباط بررسی شده باشد.
15. Shortcut/Keyboard Flow برای Featureهای پرتکرار طراحی و تست شده باشد.
16. اگر Feature گزارش‌پذیر است، Data Contract آن برای Report Engine تعریف شده باشد.

## 15.24 Development Phases

### Phase 0 — Foundation
Architecture, Design System, Database, Installer, User, Permission, Audit, Settings, Backup, Update Skeleton, Help/AI Contracts, Navigation/Workbench Skeleton, Keyboard & Shortcut Manager Skeleton.

### Phase 1 — Product & Pricing
Product, Category, Unit, Variant, Barcode, Custom Field, Price Engine, Profit Analysis, Inventory Layers.

### Phase 2 — Warehouse & Purchasing
Warehouse, Stock Ledger, Reorder, Smart Replenishment, Supplier, Purchase.

### Phase 3 — POS & Payment
Customer Quick Create, Cart, Pricing, Promotion, PC-POS, Cash Shift, Return/Exchange, Printing.

### Phase 4 — Finance
Treasury, Cheque, Bank, Accounting, Fiscal Period, Tax Infrastructure.

### Phase 5 — Production
BOM, Batch, Production Cost, Label/Scale Integration.

### Phase 6 — CRM & Loyalty
Customer 360, Loyalty, Wallet, Segment, Survey.

### Phase 7 — Communication & Automation
SMS, Automation Builder, Notification Rules.

### Phase 8 — HR
Employee, Attendance, Payroll, Assets.

### Phase 9 — Logistics
Picking, Packing, Driver, Delivery, Settlement.

### Phase 10 — Online Integration
API, Sync, Store Connectors.

### Phase 11 — BI
Workbench Widgets, Dashboard, Report & Analytics Studio, AI Report Builder Contracts, Analytics.

### Phase 12 — AI Expansion
AI Query, Tutor Enhancements, Analysis, Safe Actions.

## 15.25 اولین Milestone اجرایی

اولین Build:

```text
Skeleton
→ Design System
→ Installer
→ Database
→ Users/Permissions
→ Settings
→ Product/Category
→ Warehouse
→ Inventory Layer
→ Customer
→ POS
→ Payment
→ Printing
```

---

# قرارداد نهایی اجرای Codex

Codex و هر برنامه‌نویس باید این قواعد را رعایت کند:

1. این فایل Source of Truth است.
2. Feature جدید بدون Change Request وارد پروژه نشود.
3. تعریف یک قابلیت در چند فایل/ماژول با منطق متفاوت ممنوع است.
4. UI باید مینیمال و خلوت بماند.
5. هر عملیات اصلی 2 تا 3 مرحله‌ای باشد مگر دلیل UX مستند وجود داشته باشد.
6. هیچ صفحه عملیاتی نباید به Dashboard شلوغ تبدیل شود.
7. فارسی و Iran-first پیش‌فرض است.
8. غلط املایی در UI پذیرفته نیست.
9. Help و AI Tutor Contract از اولین Commit Feature لازم است.
10. Database و Business Rule باید قبل از UI Hack طراحی شوند.
11. کد باید برای برنامه‌نویس بعدی قابل فهم باشد.
12. One-click Installer Requirement قابل حذف نیست.
13. Incremental Update Requirement قابل حذف نیست.
14. Weak-PC Performance قابل قربانی‌کردن برای AI یا Animation نیست.
15. اسناد مالی Posted حذف فیزیکی نمی‌شوند.
16. حذف‌های مجاز دو تأیید دارند.
17. AI برای عدد مالی از Query/Calculation واقعی استفاده می‌کند.
18. هر Feature باید Owner Module مشخص داشته باشد.
19. Cross-module interaction از Contract/Event مشخص استفاده می‌کند.
20. اگر کد با این سند تعارض دارد و Change Request رسمی وجود ندارد، **این سند مرجع است**.
21. Shortcutها نباید Ad-hoc تعریف شوند؛ تمام Bindingها از Keyboard & Shortcut Manager عبور می‌کنند.
22. هر Module باید Data Contract لازم برای Report & Analytics Studio را تعریف کند.
23. AI Report Builder فقط از Query/Calculation واقعی استفاده می‌کند و حق تولید عدد حدسی ندارد.
24. «میز کار» تنها محل تجمع عملیات روزانه است؛ Featureهای Domain در Owner Module خود باقی می‌مانند.

---

# معیار نهایی موفقیت محصول

کاربر مبتدی باید بتواند بدون آموزش حضوری:

- برنامه را با یک Setup نصب کند
- فروش را سریع شروع کند
- در هر صفحه راهنمای فارسی داشته باشد
- از AI بپرسد «این کار را چطور انجام دهم؟»
- بدون اینترنت عملیات اصلی را انجام دهد

و در همان زمان مدیر حرفه‌ای باید به:

- حسابداری
- سود
- قیمت
- انبار
- HR
- تولید
- CRM
- Automation
- BI
- Audit

دسترسی عمیق داشته باشد، بدون اینکه این پیچیدگی روی صفحه کاربر عادی ریخته شود.

---

# پیوست الف — ممیزی وضعیت پیاده‌سازی و نقاط ضعف شناسایی‌شده

> این پیوست، برخلاف بقیه سند، «قانون» نیست — **گزارش وضعیت واقعی کد نسبت به این سند** است، به‌روزشده در هر بازبینی. هدفش جلوگیری از این است که فاصله‌ی بین سند و کد پنهان بماند. هر آیتم پس از رفع باید به «برطرف‌شده» تغییر وضعیت بدهد، نه حذف شود، تا سابقه باقی بماند.

**آخرین بازبینی:** 2026-09-14 — روی برنچ `feature/foundation-catalog-inventory`. آیتم‌های 🟢 همین بازبینی رفع شدند؛ سابقه برای مرجع نگه داشته شده، نه حذف.

## الف.۱ باگ‌های تاییدشده (Correctness)

| # | مورد | محل | وضعیت |
|---|------|-----|-------|
| ۱ | `ProductImportPreparer.Prepare` وقتی نام کالا در یک ردیف خالیه، به‌جای گزارش Issue تک‌ردیفی (`import.product.name-required`)، کل Import رو با `InvalidDataException` کرش می‌کنه — چون `ReadRequired` روی ستون نام مستقیم صدا زده میشه، نه از مسیر Issue-collection مثل بقیه ستون‌ها. بررسی ریشه‌ای نشون داد همین باگ روی ستون‌های دسته‌بندی/واحد/قیمت هم بود (نه فقط نام) — هر چهارتا با یک Root Cause رفع و هرکدوم با تست پوشش داده شد. | `src/ERP.Application/Importing/ProductImportPreparer.cs` | 🟢 برطرف‌شده (۱۵/۱۵ تست سبز) |

## الف.۲ نقض قوانین قفل‌شده‌ی فصل ۲ (Clean Code / Secrets / Config)

| # | مورد | محل | نقض کدام قانون | وضعیت |
|---|------|-----|------------------|-------|
| ۲ | مسیر دیتابیس Firebird و مسیر `fbclient.dll` به‌صورت Hard-code با درایو مشخص (`D:\RetailERPData\...`, `D:\RetailERPTools\...`) نوشته شده. روی هر سیستم دیگه یا حتی همین سیستم با درایو متفاوت، برنامه در همون لحظه راه‌اندازی کرش می‌کنه. | `src/ERP.Desktop/AppServices.cs` | بند ۲.۵: «Configuration در فایل/Storage مناسب، نه Hard-code» + فصل ۱۵.۷ | 🟢 برطرف‌شده — مسیرها حالا از `AppSettings` (فایل JSON زیر LocalAppData، خودکار ساخته میشه) خونده می‌شن، نه Hard-code. نصب یک‌کلیکی واقعی هنوز باقیه (نگاه کن الف.۶) |
| ۳ | یوزرنیم/پسورد پیش‌فرض Firebird (`SYSDBA` / `masterkey`) مستقیم در سورس‌کد نوشته شده. | `src/ERP.Desktop/AppServices.cs` | بند ۲.۵: «Secrets هیچ‌وقت داخل Source Code ذخیره نشوند» | 🟡 بخشی — دیگه در سورس هارد-کد نیست (از `AppSettings.json` خونده میشه)، ولی مقدار پیش‌فرضی که در اولین اجرا نوشته میشه هنوز همون Factory Default فایربرده؛ Credential واقعیِ per-installation نیاز به Installer داره (الف.۶) |
| ۴ | `App.OnLaunched` بدون `try/catch` منتظر `AppServices.InitializeAsync` می‌مونه؛ اگر دیتابیس یا Client Library پیدا نشه، برنامه با یک Exception خام و بدون هیچ پیام یا صفحه‌ی خطا کرش می‌کنه. | `src/ERP.Desktop/App.xaml.cs` | Definition of Done #9 + فصل ۱۵.۲۰ | 🟢 برطرف‌شده — `try/catch` دور Init اضافه شد؛ روی خطا `StartupErrorWindow` (پنجره‌ی مستقل بدون وابستگی به DesignSystem) پیام فارسی + جزئیات فنی نشون می‌ده |
| ۵ | هیچ Global Exception Handler (`Application.UnhandledException`) ثبت نشده — یک Exception مدیریت‌نشده در هر نقطه از برنامه، کل اپ رو بدون Log و بدون گزارش می‌بنده. | `src/ERP.Desktop/App.xaml.cs` | فصل ۱۵.۱۹ + ۱۵.۲۰ | 🟢 برطرف‌شده — `UnhandledException` ثبت شده و Crash رو به `%LocalAppData%\PishkarERP\logs\` می‌نویسه. (هنوز صرفاً Log می‌کنه، Recovery/Restart خودکار نداره) |

## الف.۳ ناهماهنگی برند/Design System بین Figma و کد WinUI

| # | مورد | محل | جزئیات | وضعیت |
|---|------|-----|--------|-------|
| ۶ | رنگ‌های اصلی کد WinUI با برند «پیشکار» یکی نبود: `AppPrimaryBrush = #087A5B` به‌جای سبز برند `#059669`، و `AppAccentBrush = #175CD3` **آبی** به‌جای کهربایی برند `#D97706`. | `src/ERP.Desktop/DesignSystem/Colors.xaml` | طبق فصل ۳۰ فایل Figma و `design-system/retail-erp/MASTER.md` | 🟢 برطرف‌شده — رنگ‌ها با برند یکی شدن؛ توکن‌های جدید `AppAccentSoftBrush`/`AppOnAccentBrush`/`AppNavSelectedBrush` هم اضافه شد |
| ۷ | فونت کل اپ `Tahoma` بود — نه Embeddable، نه فونت فارسی اختصاصی (بند ۳.۶). | `src/ERP.Desktop/DesignSystem/Typography.xaml` | بند ۳.۶ «فونت فارسی خوانا و Embeddable» | 🟢 برطرف‌شده — فونت **Vazirmatn** (۴ وزن Regular/Medium/SemiBold/Bold، مجوز SIL OFL) واقعاً دانلود و زیر `Assets/Fonts` Embed شد، در csproj به‌عنوان Content ثبت شد (دقیقاً هم‌الگو با Assetهای موجود مثل AppIcon.ico) و در Typography.xaml با سینتکس `/Assets/Fonts/...#Vazirmatn` رفرنس شد. فایل مجوز هم کنارش هست |
| ۸ | عنوان پنجره و Sidebar «راهکار فروش» بود، نه «پیشکار ERP» طبق برند قفل‌شده در Figma. | `src/ERP.Desktop/MainWindow.xaml`, `MainPage.xaml` | یکپارچگی برند | 🟢 برطرف‌شده |
| ۹ | چند رنگ در `MainPage.xaml` مستقیم Hex نوشته شده بود (`#1D2939`, `#98A2B3`, ...) به‌جای ThemeResource. | `src/ERP.Desktop/MainPage.xaml` | نقض ۲.۵ (Hidden Coupling) + ۳.۶ | 🟢 برطرف‌شده — همه به ThemeResource وصل شدن |
| ۱۰ | Sidebar با `StackPanel` + `Button` دستی ساخته شده بود، بدون Visual State برای آیتم فعال — کاربر نمی‌فهمید توی کدوم صفحه‌ست. | `src/ERP.Desktop/MainPage.xaml(.cs)` | راهنمای WinUI (`NavigationView`) | 🟢 برطرف‌شده — Sidebar به کنترل بومی `NavigationView` مهاجرت کرد (رنگ‌ها با ThemeResource alias به برند وصل شدن، Selected/Hover/Pressed بومی، AccessKey و AutomationProperties روی هر آیتم). نکته‌ی صداقت: اسم دقیق چند تا از ThemeResource Key‌ها (مثل `NavigationViewItemBackgroundSelected`) بدون اجرای واقعی روی ویندوز تاییدنشده — اگه اسمی اشتباه باشه، فقط همون یک افکت به رنگ پیش‌فرض ویندوز برمی‌گرده، نه Crash |

## الف.۴ فاصله با رفرنس UI (مقایسه با اسکرین‌شات‌های POS ارائه‌شده توسط کاربر)

صفحات فعلی (`فروش سریع` و مشابه در Figma) در حد Wireframe هستن، نه UI نهایی. موارد غایب نسبت به رفرنس:

- جدول واقعی سبد خرید (ردیف کالا/تعداد/قیمت/تخفیف/وضعیت موجودی)
- گرید «دسترسی سریع کالا» (پرفروش‌ها/دسته‌ها/فروش اخیر)
- بنر وضعیت مشتری (مانده بدهکار، فاکتور باز)
- گرید روش پرداخت با آیکون
- نوار پایین با میانبرهای کیبورد همیشه‌نمایان (F3/F4/F8/Esc) — با اینکه فصل ۳.۱۴ کل سند به همین اختصاص داره، در هیچ‌کدام از موکاپ‌ها یا کد فعلی پیاده نشده
- تب چند فاکتور فعال همزمان (بند ۶.۱۳ Multi Active Sales)
- کپی‌پیست محتوای یکسان بین صفحات نامرتبط در Figma (میزکار مدیر و شیفت صندوق دقیقاً یک بلوک هشدار دارند؛ تسویه‌وپرداخت دقیقاً کارت‌های فاکتورهای‌فروش رو تکرار کرده)

## الف.۵ خلأی خود سند (نه فقط کد) — نبود مشخصات Login/Authentication

فصل ۱۵.۱ («User / Role / Permission») فقط **دامنه‌ی مدل دسترسی** رو تعریف می‌کنه (Company/Branch/Module/Action/Sensitive Field) — سند هیچ‌جا مشخص نکرده:

- صفحه/فرم ورود چطور به‌نظر می‌رسه و چند مرحله‌ست
- روش احراز هویت چیه (Username+Password؟ PIN سریع برای صندوق؟ کارت پرسنلی؟ ترکیبی؟)
- سیاست رمز عبور (حداقل طول، انقضا، قفل بعد از تلاش ناموفق)
- مدیریت Session (چند کاربر همزمان روی یک صندوق؟ Auto-lock بعد از بی‌کاری؟)
- ارتباط لاگین با فصل ۳.۱۳ (میز کار Role-based) — یعنی بعد از لاگین دقیقاً چی به کاربر نشون داده میشه

این یعنی حتی اگر کد Phase 0 (پایین) رو کامل کنیم، بدون یک **Change Request برای طراحی Login** طبق قانون خود سند (بند ۲۰ در «قرارداد نهایی اجرای Codex»)، پیاده‌سازی‌ش صرفاً حدس مهندس خواهد بود.

## الف.۶ خلأهای Phase 0 (Foundation) طبق فصل ۱۵.۲۴

طبق نقشه‌راه خود سند، Phase 0 باید شامل این‌ها باشه؛ در کد فعلی **هیچ‌کدام پیاده نشده**:

- User / Role / Permission (فقط یک `PilotUserContext` با GUID ثابت جای Login واقعی نشسته)
- One-click Installer
- Backup / Update Skeleton
- Help / AI Tutor Contract
- Keyboard & Shortcut Manager (به‌صورت زیرسیستم مرکزی؛ فعلاً فقط `Ctrl+S` پراکنده روی یک دکمه هست)
- Audit Log کامل (نوشتن هست، UI نمایش/بازبینی نیست)

## الف.۷ جمع‌بندی اولویت رفع (برای پیگیری بعدی)

1. ~~باگ Import (الف.۱ #۱)~~ 🟢 برطرف‌شده
2. ~~مسیر/Secret هاردکد + کرش بی‌صدا (الف.۲ #۲-۵)~~ 🟢 برطرف‌شده (Credential واقعی هنوز منتظر Installer — آیتم ۶)
3. ~~هماهنگی رنگ/فونت/برند با Figma (الف.۳ #۶-۹)~~ 🟢 برطرف‌شده (فونت هم واقعاً Embed شد)
4. ~~مهاجرت Sidebar به `NavigationView` بومی (الف.۳ #۱۰)~~ 🟢 برطرف‌شده
5. 🟡 تکمیل صفحه‌ی POS واقعی در کد، مطابق رفرنسی که کاربر داد (الف.۴ + پیوست ب) — جدول سبد خرید، گرید کالا، نوار شورتکات پایین، بنر مشتری. **مهم:** این کار نباید از Layout صفحات Figma کپی بشه؛ مرجع طراحی، رفرنس‌های `design-references/pishkar-pos/` + پژوهش پیوست ب است.
   - ✅ بک‌اند جست‌وجوی سریع کالا (`SearchProductsHandler`/`FirebirdProductSearchReader`، Rank + تست واقعی روی Firebird)
   - ✅ دامنه و Backend کامل «فروش سریع»: `Sale` (سبد خرید با افزودن/حذف/ادغام کالا، تخفیف، مالیات)، اتصال به انبار (کسر FIFO واقعی هنگام تکمیل فاکتور)، تراکنش اتمیک (اگر موجودی کافی نباشد نه فاکتور نه کسر موجودی ثبت می‌شود — با تست واقعی Rollback روی Firebird تایید شد). لایه‌ها: `ERP.Domain/Sales`, `ERP.Application/Sales`, `ERP.Persistence/Sales`
   - ✅ طراحی صفحه‌ی فروش تایید شد (نسخه اولیه): فضای مستقل فروش با دکمه خروج، تب فاکتور موقت برای چند مشتری هم‌زمان، لیست فاکتورها، دسته‌بندی سمت راست، جدول کالا وسط، فاکتور سمت چپ، نوار عملیات تمام‌عرض. بهینه‌شده برای مانیتور ۱۲۸۰×۱۰۲۴ صندوق فروشگاهی + Responsive
   - ✅ تخفیف سطر (مبلغی/درصدی)، ویرایش قیمت سطر با گزینه‌ی «قیمت اصلی کالا هم به‌روز شود» (§۶.۱۰/§۶.۱۱)، خدمات/هزینه فاکتور، و روش‌های پرداخت نسیه و چک — با Migration V003 و تست Round-trip روی Firebird واقعی
   - ✅ **شماره فاکتور** — یک عدد ساده و صعودی (مثل ۱۲۵۸)، **بدون** پیشوند سال. طرح قبلی «۱۴۰۵-۱۲۰۱» کنار گذاشته شد؛ دلیل و منبع در پیوست ج
   - ✅ رفع خطای کامپایل `Customer` (تابع نرمال‌سازی ارقام موبایل وجود نداشت)
   - ✅ **مشتری** (Backend): ثبت سریع با چک موبایل تکراری، جست‌وجو با اسم/موبایل، انتخاب مشتری روی فاکتور، مانده حساب و فاکتورهای باز، «دریافت از مشتری»، و کنترل سقف اعتبار نسیه — جزئیات و دلایل در پیوست د
   - ✅ **لیست فاکتورهای یک روز** (روز بر اساس نیمه‌شب تهران، نه UTC؛ با جمع روز و جمع هر روش پرداخت)، **لیست فاکتورهای معلق** (پیش‌نویسِ دارای کالا — وضعیت جدا ندارد تا با بسته‌شدن برنامه چیزی گم نشود)، و **جزئیات فاکتور** با نام کالا/کد/واحد/موجودی. محدودیت: هنوز جلوی باز کردن هم‌زمان یک فاکتور معلق در دو صندوق گرفته نمی‌شود (نیازمند هویت صندوق/کاربر در Phase 0)
   - ❌ باقی‌مانده: خودِ صفحه‌ی WinUI فروش، چاپ فاکتور، مرجوعی، Cash Shift
   - ❌ درخواست کاربر (۱۴۰۵/۰۶/۲۴): **تولید بارکد برای کالاهای بدون بارکد** (§۹.۵) — باید از محدوده‌ی GS1 مخصوص مصرف داخلی (Restricted Circulation، پیشوند ۲) با رقم کنترل صحیح ساخته شود و با محدوده‌ی بارکد ترازو (§۹.۷، کالای وزنی) تداخل نداشته باشد؛ قبل از اجرا تحقیق دقیق شود. همراه چاپ برچسب (§۹.۶)
   - ❌ درخواست کاربر (۱۴۰۵/۰۶/۲۴): **چاپ لیبل و اتیکت کالا** (§۹.۶) — هم از لیست کالاها، هم **داخل فاکتور خرید با کلیک راست روی ردیف** (فاکتور خرید §۸.۹ هنوز ساخته نشده؛ این قابلیت باید از روز اول در آن دیده شود). وابسته به تولید بارکد (بالا)
   - ❌ درخواست کاربر (۱۴۰۵/۰۶/۲۴): **اتصال PC-POS** با DLLهای شرکت‌های PSP (ارتباط شبکه‌ای با IP) — طبق §۶.۱۷ از طریق Local Payment Agent و آداپتر هر PSP؛ DLLها و مستنداتشان اول بررسی شوند (ممکن است .NET Framework باشند و داخل برنامه‌ی اصلی لود نشوند)
6. 🔴 نوشتن مشخصات Login/Authentication به‌عنوان Change Request رسمی (الف.۵) — پیش‌نیاز هر پیاده‌سازی واقعی User/Permission
7. 🔴 شروع Phase 0 Foundation واقعی (User/Permission/Installer/Backup) — بدون آیتم ۶، این‌ها صرفاً حدس مهندس می‌مونن

---

# پیوست ب — تجربه صندوق‌دار و جست‌وجوی سریع کالا (Change Request)

> این بخش، مکمل فصل ۳ و ۶ سند است؛ چون خود سند برای «سرعت جست‌وجو» عدد/معیار مشخص نکرده بود، طبق قانون سند («کد قبل از UI باید طراحی بشه» و «Change Request رسمی») این‌جا رسماً ثبت می‌شه. مرجع: کاربر اصلی و پرتکرارترین نقش برنامه **صندوق‌دار** است؛ حجم کار بالا، زمان کم، تمرکز کم — هر اصطکاک کوچیک ضرب در صدها بار در روز می‌شه.

## ب.۱ یافته‌های پژوهش رقبا (منبع‌دار)

از بررسی مستقیم مقالات UX سیستم‌های POS معتبر (نه حدس):

- در بنچمارک ۲۰۲۶ روی Square/Toast/Lightspeed، **جست‌وجوی کالا «Queue-forming Function»** شناخته شده — یعنی صف عملیات پشت سرش می‌ایسته، پس باید **زیر یک ثانیه** جواب بده. کاربران واقعی از تاخیر ۲ تا ۳ ثانیه‌ای جست‌وجو به‌قدری ناراضی بودن که گفتن باعث رفتنشون به سراغ رقیب می‌شه. ([creative.navy](https://creative.navy/blog/pos-software-ux-benchmarking-2026-the-coherence-gap/))
- «Taxonomy باید با زبان صندوق‌دار یکی باشه، نه ساختار دیتابیس» — یعنی جست‌وجو باید همون‌جوری که صندوق‌دار اسم کالا رو صدا می‌زنه جواب بده، نه فقط تطبیق دقیق فیلد.
- **حفظ حافظه‌ی عضلانی (Muscle Memory):** توالی حرکات دست/چشم صندوق‌دار نباید بین آپدیت‌ها به‌هم بریزه؛ عناصر اصلی صفحه باید مکان ثابت داشته باشن.
- طراحی مسیر خطا (Error Recovery) به‌اندازه‌ی مسیر اصلی مهمه، نه فرع بر آن.
- از شلوغی تراکمی («Sense Decay» — لایه‌لایه اضافه‌شدن قابلیت روی صفحه‌ی اصلی فروش) باید پرهیز بشه. ([agentestudio.com](https://agentestudio.com/blog/design-principles-pos-interface))
- جست‌وجو باید چندکاناله باشه: نام، دسته، بارکد، کد کالا — هر کدوم با اولویت متفاوت.
- Confirmation/Dialog فقط برای عملیات پرریسک؛ برای کار روتین (مثل افزودن کالا به سبد) هیچ تاییدی نباید سد راه بشه — دقیقاً هم‌راستا با فصل ۳.۸ سند اصلی.

## ب.۲ قانون قفل‌شده‌ی جست‌وجوی کالا

1. با نیم‌کلمه (Prefix) هم باید نتیجه بیاد؛ نتیجه‌ای که کالا **با همون حروف شروع می‌شه** همیشه بالای نتایجی میاد که فقط **وسط اسمش** اون حروف رو داره.
2. جست‌وجو باید هم‌زمان نام، کد کالا (SKU) و بارکد رو پوشش بده.
3. حداکثر ۸ نتیجه پیش‌فرض نشون داده بشه (نه یک لیست بلند که صندوق‌دار باید اسکرول کنه) — قابل افزایش تا ۵۰ در موارد خاص.
4. Query باید در دیتابیس Rank و Limit بشه (نه این‌که همه‌چیز لود بشه و توی حافظه فیلتر بشه) تا با رشد کاتالوگ کند نشه.
5. این پیاده‌سازی شد: `SearchProductsHandler` (لایه Application) + `FirebirdProductSearchReader` (لایه Persistence)، با تست واقعی روی Firebird — شامل تست Rank، تست جست‌وجوی بارکد، و تست محدودیت تعداد نتیجه.

## ب.۳ برای بقیه‌ی صفحات (نه فقط جست‌وجو)

- **حس‌وحال ۲۰۲۶، نه سیستم‌های قدیمی ایرانی:** رقبای داخلی (هلو/سپیدار/پارسیان) روی «سادگی» تبلیغ می‌کنن ولی از نظر بصری قدیمی‌ان؛ تمایز ما باید در تراکم اطلاعات هوشمند + مینیمال بودن واقعی باشه، نه فقط شعار سادگی.
- مرجع طراحی از این به بعد **رفرنس‌های واقعی کاربر** (`design-references/pishkar-pos/`) + این پژوهش است — **نه** چیدمان صفحات فیگما (کاربر صراحتاً گفته چیدمان فیگما رو دوست نداره، فقط نماد/لوگوش تایید شده).
- هر صفحه‌ی پرتکرار (Sales & POS) باید طوری طراحی بشه که با چشم بسته هم صندوق‌دار حرفه‌ای بتونه مسیرش رو حدس بزنه — دکمه‌ها/فیلدها جای ثابت دارن، هیچ‌وقت بین نسخه‌ها جابه‌جا نمی‌شن بدون دلیل مستند.

> **اصل نهایی:** پیچیدگی برای سیستم؛ سادگی برای کاربر.

---

# پیوست ج — شماره فاکتور فروش (Change Request)

> سند اصلی قالب شماره فاکتور را مشخص نکرده بود و پیوست الف.۷ به‌اشتباه «۱۴۰۵-۱۲۰۱» را پیشنهاد داده بود. این پیوست تصمیم نهایی را ثبت می‌کند.

## ج.۱ قانون قفل‌شده

1. شماره فاکتور یک **عدد ساده و صعودی** است (نمونه: ۱۲۵۸) — بدون سال، بدون کد شعبه.
2. شماره **هیچ‌وقت تکرار نمی‌شود، هیچ‌وقت عقب نمی‌رود و اول سال مالی از ۱ شروع نمی‌شود.**
3. شماره فقط لحظه‌ی **ثبت نهایی** فاکتور گرفته می‌شود. فاکتور پیش‌نویس، معلق یا رهاشده شماره ندارد.
4. «جای خالی» در شماره‌ها مجاز است (مثلاً اگر ثبت به‌خاطر قطعی برق وسط کار شکست بخورد)، ولی تکرار مجاز نیست.
5. روی صفحه و چاپ با ارقام فارسی نمایش داده می‌شود.

## ج.۲ چرا (منبع‌دار)

- در **سامانه مودیان**، سریال داخلی فاکتور بخشی از «شماره منحصربه‌فرد مالیاتی» ۲۲ رقمی است و باید **یکتا و صعودی** باشد؛ اگر تکراری باشد یا به عدد قبلی برگردد، فاکتور در سامانه ثبت نمی‌شود. ([حسابداران برتر](https://hesabdaranebartar.com/blog/1203), [داریک سافت](https://dariksoft.com/))
- طبق **اطلاعیه ۳۱ سازمان امور مالیاتی**، سریال نباید اول سال ریست شود (آخرین فاکتور ۱۴۰۳ = ۲۰۰۰ ← اولین فاکتور ۱۴۰۴ = ۲۰۰۱). ([حسابداران برتر](https://hesabdaranebartar.com/blog/1203))
- نرم‌افزارهای قدیمی‌تر مثل **هلو** شماره فاکتور را اول هر سال مالی از ۱ شروع می‌کنند — همین عادت با قانون جدید تعارض دارد و ما تکرارش نمی‌کنیم. ([ilyaresahra.blogfa](https://ilyaresahra.blogfa.com/post/36))
- رفرنس طراحی تاییدشده‌ی کاربر (`design-references/pishkar-pos/`) هم شماره را ساده نشان می‌دهد: «۱۲۵۸».

## ج.۳ اجرا

- `ERP.Domain/Sales/SaleNumber.cs` + `Sale.Number`
- Sequence دیتابیس `SEQ_SALE_NUMBER` (Migration V004). Sequence در Firebird مستقل از تراکنش است؛ پس دو صندوق هم‌زمان هرگز شماره‌ی یکسان نمی‌گیرند و شماره‌ی یک ثبتِ شکست‌خورده دوباره داده نمی‌شود. ([Firebird Generator Guide](https://www.firebirdsql.org/file/documentation/html/en/firebirddocs/generatorguide/firebird-generator-guide.html))
- تست روی Firebird واقعی: دو فاکتور پشت‌سرهم شماره‌ی n و n+1 می‌گیرند، فاکتورِ ردشده (موجودی ناکافی) شماره نمی‌گیرد و جای خالی هم ایجاد نمی‌کند.
- **نکته برای صفحه‌ی فروش:** چون شماره فقط هنگام ثبت گرفته می‌شود، سربرگ فاکتور در حال ساخت به‌جای عدد «پیش‌نویس» نشان می‌دهد و شماره بعد از ثبت ظاهر می‌شود. نمایش «شماره‌ی بعدی» از قبل ممنوع است، چون با چند صندوق هم‌زمان ممکن است غلط از آب دربیاید.

---

# پیوست د — مشتری روی فاکتور، مانده حساب و سقف اعتبار (Change Request)

> فصل ۶.۴ تا ۶.۷ «چه چیزی» را گفته بود؛ این پیوست «چطور» و تصمیم‌هایی را که سند باز گذاشته بود ثبت می‌کند.

## د.۱ قوانین قفل‌شده

1. **موبایل = هویت مشتری و یکتاست.** دو مشتری با یک موبایل ثبت نمی‌شوند؛ مقایسه روی ارقام نرمال‌شده است («۰۹۱۲ ۳۴۵ ۶۷۸۹» = «09123456789»). دلیل: دو پرونده برای یک نفر، مانده و سابقه‌ی خرید و امتیاز باشگاه (فصل ۷) را بی‌صدا دو تکه می‌کند و صندوق‌دار متوجه نمی‌شود.
2. **فروش نسیه و چکی بدون مشتری ممنوع است.** «مشتری نقدی» فقط برای نقد و کارت.
3. فاکتور همیشه با «مشتری نقدی» شروع می‌شود و مشتری هر لحظه قبل از ثبت انتخاب/عوض/حذف می‌شود (F4). مشتری بایگانی‌شده قابل انتخاب نیست.
4. **مانده مشتری = مانده اول دوره + فاکتورهای نسیه − دریافتی‌ها.**
5. **فروش چکی جزو بدهی مشتری حساب نمی‌شود.** در حسابداری ایران با دریافت چک، حساب مشتری بستانکار می‌شود و طلب به «چک‌های دریافتنی» منتقل می‌شود؛ فقط اگر چک برگشت بخورد بدهی دوباره فعال می‌شود. ([سپیدار سیستم](https://www.sepidarsystem.com/blog/registration-of-receivables-and-payables-in-accounting/)) پیگیری وصول/برگشت چک مال خزانه‌داری (فصل ۱۰.۳) است.
6. **فاکتور باز:** دریافتی‌ها به ترتیب قدیمی‌ترین تسویه می‌شوند (اول مانده اول دوره، بعد فاکتورها به ترتیب تاریخ). فاکتوری که حتی بخشی از آن مانده، باز است. پرداخت بیشتر از بدهی = «بستانکار» (پیش‌پرداخت)، نه بدهی منفی.
7. **سقف اعتبار (فقط نسیه):** تا سقف → مجاز. بیش از سقف → نیاز به تأیید (در Audit ثبت می‌شود). بیش از ۱.۵ برابر سقف یا مشتری بایگانی → مسدود، حتی با تأیید. سقف صفر یعنی «سقف تعیین نشده».
8. مبلغ مالیات و مبلغ نهایی هر فاکتور **لحظه‌ی ثبت ذخیره می‌شود** و بعداً دوباره محاسبه نمی‌شود، چون نرخ مالیات در طول زمان عوض می‌شود.
9. «دریافت از مشتری» (نقد/کارت) فقط ثبت می‌شود و ویرایش/حذف ندارد (قانون ۱۵).

## د.۲ محدودیت‌های آگاهانه (باید بعداً تکمیل شوند)

- **تأیید مدیر نقش را چک نمی‌کند**، چون سیستم کاربر/دسترسی هنوز وجود ندارد (الف.۶). فعلاً فقط کاربری که تأیید کرده در Audit ثبت می‌شود. با آمدن Permission باید به نقش مدیر محدود شود.
- جست‌وجوی مشتری فقط با **اسم و موبایل** است؛ کد مشتری، کد ملی و کارت باشگاه (§۶.۴) وقتی اضافه می‌شوند که این فیلدها به مشتری اضافه شوند.
- دریافت با **چک** از مشتری و برگشت چک → فاز ۴ خزانه‌داری.
- دو صندوق که هم‌زمان به یک مشتری نسیه بفروشند، ممکن است هر دو از کنترل سقف رد شوند (بازه‌ی زمانی بسیار کوتاه). برای یک فروشگاه قابل قبول است؛ در صورت نیاز با قفل ردیف مشتری بسته می‌شود.
- **«دریافت از مشتری» (۱۴۰۵/۰۶/۲۵) صفحه گرفت** ولی جای دکمه‌اش تصمیمی عمدی است، نه پیش‌فرض بی‌فکر: داخل همان بنر کهربایی مانده‌ی مشتری روی صفحه‌ی فروش (§۶.۶)، نه دکمه‌ای همیشه‌روشن در ردیف جست‌وجوی مشتری. یعنی وقتی بنر (به‌خاطر مانده‌ی صفر) دیده نمی‌شود، دکمه هم دیده نمی‌شود — برای «مشتری نقدی» درست است، ولی یک مشتری بدون بدهی که بخواهد پیش‌پرداخت کند فعلاً راهی از این مسیر ندارد. اگر لازم شد، جای دکمه باید عوض شود، نه اینکه دوباره از صفر طراحی شود.

## د.۳ اجرا و تست

- Domain: `Customer`, `CustomerAccount`, `CustomerPayment`, `Sale.AssignCustomer`
- Application: `QuickCreateCustomer`, `SearchCustomers`, `GetCustomerAccount`, `RecordCustomerPayment`, `SetSaleCustomer`، و کنترل اعتبار داخل `CompleteSale`
- دیتابیس: Migration V005 (مشتری) و V006 (مبلغ فاکتور + دریافتی‌ها + کلید خارجی فاکتور→مشتری). V006 روی **کپی** دیتابیس واقعی `data/RETAIL-ERP.FDB` امتحان شد و بدون خطا اجرا شد.
- تست روی Firebird واقعی: فروش نسیه → بدهی ۱٬۷۰۰٬۰۰۰ و یک فاکتور باز → دریافت کامل → بدهی صفر و فاکتور باز صفر.

# پیوست و — اصلاح فاکتور ثبت‌شده (Change Request)

> §۱۰.۱۰ گفته بود سند Posted حذف/ویرایش فیزیکی نمی‌شود و باید «اصلاح» یا «ابطال» شود؛ این پیوست تصمیم‌های اجرایی را ثبت می‌کند.

## و.۱ قوانین قفل‌شده

1. **اصلاح = فاکتور تازه، نه ویرایش.** یک `Sale` جدید با `CorrectsSaleId` رو به فاکتور اصلی ساخته می‌شود؛ ردیف فاکتور اصلی در دیتابیس هرگز نوشته نمی‌شود — نه مبلغش، نه وضعیتش. شماره‌ی جدید از همان دنباله‌ی صعودی کشیده می‌شود (پیوست ج).
2. **این نسخه فقط ارقام مالی را باز می‌کند: تخفیف، هزینه/خدمات، درصد مالیات، روش پرداخت.** طبق [malitor.ir](https://malitor.ir/p/1515/) تغییر تعداد کالا هم قانوناً جزو «اصلاحی» است، ولی نیاز به برگشت/برداشت موجودی دارد که همان «مرجوعی» (§۶.۲۵) است و هنوز ساخته نشده؛ تغییر کالا/تاریخ/خریدار هم قانوناً «ابطال» می‌خواهد که ساخته نشده. تلاش برای این‌ها رد می‌شود، با پیام روشن، نه سکوت یا خرابی موجودی.
3. **دلیل اصلاح الزامی است** و روی همان فیلد `Sale.Note` (پیوست الف-۲ #۴) ذخیره می‌شود — یک اصلاحیه‌ی بی‌دلیل ثبت نمی‌شود (قانون ۱۵: کار روی سند Posted باید پاسخ‌گو باشد).
4. **هر فاکتور فقط یک‌بار قابل اصلاح است.** زنجیره‌ی اصلاحیه‌پشت‌اصلاحیه یا اصلاحِ خودِ یک اصلاحیه رد می‌شود — دنبال کردن یک زنجیره‌ی بلند برای صندوق‌دار/حسابدار سخت می‌شود؛ اگر لازم شد، نسخه‌ی بعدی این محدودیت را با یک UI برای دیدن کل زنجیره باز می‌کند.
5. **اصلاحیه‌ی نسیه دوباره از سقف اعتبار عبور می‌کند** (§۶.۷) — دقیقاً همان قانونی که فاکتور تازه رعایت می‌کند؛ کد این قانون بین دو مسیر مشترک است (`SaleCreditPolicy`) که رفتارشان جدا نشود.
6. **موجودی دست نمی‌خورد.** کالاهای همان اصلاحیه از قبل، یک‌بار، هنگام فاکتور اصلی از انبار کم شده‌اند؛ کامل‌کردن اصلاحیه دوباره FIFO مصرف نمی‌کند.
7. **هر جایی که فاکتورهای تکمیل‌شده جمع زده می‌شوند** — لیست/جمع امروز، بدهی مشتری — فاکتور اصلاحی‌شده کنار گذاشته می‌شود و فقط آخرین نسخه شمرده می‌شود؛ وگرنه مبلغ دو بار حساب می‌شود.

## و.۲ محدودیت‌های آگاهانه (باید بعداً تکمیل شوند)

- تغییر تعداد/کالا: منتظر «مرجوعی» (§۶.۲۵).
- تغییر خریدار/تاریخ/شناسه کالا: منتظر «ابطال» واقعی (که خودش یک قابلیت جدا و بزرگ است، نه فقط یک Enum جدید).
- تأیید مدیر برای عبور از سقف اعتبار هنوز نقش را چک نمی‌کند (همان محدودیت پیوست د.۲).
- بازکردن دوباره‌ی یک اصلاحیه‌ی معلق (بسته و ذخیره‌شده) در یک نشست بعدی، شماره‌ی فاکتور اصلی را روی صفحه نشان نمی‌دهد (فقط از حافظه‌ی همان نشست خوانده می‌شود، نه دوباره از دیتابیس) — خودِ محافظت (اجازه‌ی تغییر فقط ارقام مالی) دست‌نخورده می‌ماند.
- اصلاح فقط از تب «فاکتورهای امروز» در دسترس است؛ فاکتور روزهای قبل (که مهلت قانونی اصلاح هنوز دارد) از این صفحه قابل‌دسترسی نیست.

## و.۳ اجرا و تست

- Domain: `Sale.CorrectsSaleId`, `Sale.OpenCorrection`
- Application: `StartSaleCorrection`, `CompleteSaleCorrection`, `SaleCreditPolicy` (مشترک با `CompleteSale`)
- دیتابیس: Migration V008 (`CORRECTS_SALE_ID` + FK خودارجاع + ایندکس)؛ `ListCompletedAsync` و `ICustomerLedgerReader` هر دو فاکتور اصلاح‌شده را با `NOT EXISTS` کنار می‌گذارند.
- تست روی Firebird واقعی: فاکتور نقدی → اصلاحیه با روش پرداخت دیگر → فاکتور اصلی دست‌نخورده، لیست/جمع امروز فقط اصلاحیه را می‌شمارد؛ فاکتور نسیه → اصلاحیه با تخفیف تازه → بدهی مشتری برابر مبلغ اصلاح‌شده است، نه جمع دو فاکتور.

# پیوست ه — ردیاب زنده‌ی صفحه‌ی فروش و طرح پایه (نه اینجا، جای دیگر)

> این سند خیلی بزرگ است برای این‌که هر تصمیم ریز صفحه‌ی فروش و صفحات پایه در آن جا بگیرد و هم‌زمان به‌روز بماند. آن ردیابی جای جدا دارد:
>
> **`docs/checklists/sales-and-base-design-vs-source-of-truth.md`** — بند به بند این سند (فصل‌های ۳، ۴، ۵، ۶، ۱۰.۱۰، ۱۵) در برابر کد واقعی، با وضعیت ✅/⚠️/❌/⏸ و تاریخ هر تغییر. **قبل از دست‌بردن به صفحه‌ی فروش، منوی اصلی، میز کار، یا صفحات دسته‌بندی/کالا/موجودی اولیه، آن فایل را بخوان** — چیزی که آنجا ✅ یا با توضیح ⏸/⚠️ علامت خورده، تصمیم عمدی بوده، نه جا افتادن.
>
> نمونه‌ی تصمیم‌هایی که فقط آنجا ثبت شده‌اند، نه اینجا: نرخ پیش‌فرض مالیات ۱۰٪ (نه ۹٪ قدیمی) هنوز یک نرخ برای کل فاکتور است، نه هر کالا — چون فیلد «مالیات ٪» تکی در فوتر همان طرح تأییدشده‌ی کاربر است و تغییرش نیاز به تأیید صریح کاربر دارد؛ Minimum Price در پنجره‌ی ویرایش سطر عمداً حذف شد چون `Product.MinimumPrice` در دامنه وجود ندارد؛ راهنمای F1 از فایل‌های `src/ERP.Desktop/Help/Workflows/*.yaml` خوانده می‌شود.
