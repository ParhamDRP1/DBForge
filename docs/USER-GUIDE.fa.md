> نسخهٔ ۱٫۲: برای رابط جدید، پروفایل‌ها و ویزاردها [این راهنما](WIZARDS.fa.md) را بخوانید. مطالب زیر مرجع موتور Utility نسخهٔ قبلی است.

# راهنمای فارسی DSNForge

[English](USER-GUIDE.md) · [معرفی پروژه](../README.fa.md)

DSNForge 1.1 — DB2 z/OS Utility Studio
====================================

خروجی‌ها
  release\DSNForge.Desktop.exe    برنامه ویندوزی
  release\DSNForge.Cli.exe        خط فرمان
  release\DSNForge.Core.dll       کتابخانه مشترک
  DSNForge.sln                    سورس Solution

پیش‌نیاز
  Windows x64، .NET Framework 4.8 و Microsoft Host Integration Server
  با Microsoft.HostIntegration.MsDb2Client نصب‌شده. این درایور اختصاصی در بسته کپی
  نمی‌شود. نسخه نصب‌شده روی همین سیستم در آزمایش واقعی استفاده شد.
  برای build به .NET SDK و targeting pack مربوط به Framework 4.8 نیاز است.
  powershell -File .\build.ps1

شروع در GUI
  ۱. یک پروژه بسازید یا فایل JSON پروژه را Open کنید.
  ۲. در Connections مشخصات DB2 و FTP را تنظیم کنید. رمزها فقط برای همان جلسه‌اند؛
     در فایل پروژه ذخیره نمی‌شوند. هر دو رمز باید وارد شوند، مگر کاری که FTP ندارد.
  ۳. با دکمه‌های LOAD / UNLOAD / TRANSFER مرحله اضافه و ترتیب مراحل را تنظیم کنید.
  ۴. مشخصات Task settings و Dataset را تعیین کنید. خواص Dataset بازشدنی هستند.
  ۵. برای CSV، ترتیب و حداکثر بایت UTF-8 ستون‌ها را در CSV layout وارد کنید یا
     Read table layout را بزنید. این تبدیل برای ستون‌های CHAR/VARCHAR است.
     انواع عددی، تاریخ، باینری و nullable را با کارت‌های native مناسب اجرا کنید.
  ۶. Validate بررسی محلی انجام می‌دهد. DB2 Preview از DSNUTILU با RESTART=PREVIEW
     استفاده می‌کند و syntax را بررسی می‌کند؛ این آزمون تضمین صحت داده یا فضای
     دیسک/مجوز FTP نیست. Run پس از اعتبارسنجی محلی شروع می‌شود.
  ۷. Live events، نوار پیشرفت و Report نتیجه را نشان می‌دهند. Export report
     فایل‌های JSON/CSV/HTML را در پوشه دلخواه ذخیره می‌کند.

نوع کار
  Load: CSV، فایل native، Dataset موجود یا کارت‌های تولیدشده توسط UNLOAD.
  Unload: اجرای UNLOAD، دریافت فایل داده و ذخیره کارت LOAD تولیدشده در پوشه گزارش.
  Transfer: UNLOAD و سپس LOAD روی یک سرور/اتصال، با جدول مقصد از پیش ساخته‌شده.
     فایل داده روی مین‌فریم می‌ماند؛ فقط کارت‌های LOAD از FTP خوانده می‌شوند.
     برای پروژه چندجدولی، چند مرحله اضافه کنید. مراحل به ترتیب اجرا می‌شوند.
     این نسخه یک پروفایل اتصال برای هر پروژه دارد؛ برای سرورهای مختلف پروژه‌های
     جدا و فایل‌های میانی استفاده کنید. Transfer همه انواع native سازگار را حفظ می‌کند.

Batch و حفظ داده
  BatchRecords تعداد رکورد در هر اجرای LOAD است. صفر یعنی یک فایل/اجرا.
  CSV به رکورد ثابت باینری UTF-8، همراه طول دو‌بایتی هر VARCHAR تبدیل می‌شود.
  VARCHAR MIXED در کارت LOAD لازم است؛ این مسیر برای متن فارسی آزمایش شده است.
  مقادیر متن، فاصله‌ها، صفرهای ابتدایی و رشته خالی حفظ می‌شوند؛ رشته خالی NULL نیست.
  CSV عنوان اختیاری، delimiter قابل تنظیم، کوتیشن و فیلد چندخطی دارد.
  NativeRecordBytes عرض payload فایل native است. برای batch کردن فایل native باید
  عرض ثابت مشخص باشد. NativeInputHasRdw چهار بایت RDW هر رکورد را حذف می‌کند؛
  رکوردهای متغیر با طول متفاوت یا spanned رد می‌شوند و باید روی Dataset اصلی بمانند.
  در Replace تنها batch اول REPLACE است؛ بقیه RESUME YES هستند.
  ControlStatements سفارشی باید BatchRecords=0 داشته باشد؛ برای native batching
  از GeneratedCards استفاده کنید. UNLOAD در هر مرحله یک بار برای جدول اجرا می‌شود؛
  تنظیمات دقیق UNLOAD از UtilityOptions/ControlStatements قابل تعیین است.

Dataset و FTP
  Name نام کامل MVS بدون کوتیشن است؛ هر qualifier حداکثر ۸ کاراکتر، کل نام حداکثر ۴۴.
  نمونه: MYUSER.DSNFORGE.{run}.{batch}
  توکن run برای هر اجرا یکتا و batch مثل B000001 است.
  AutoAllocate، DeleteAfterSuccess، RECFM، LRECL، BLKSIZE، فضا و UNIT قابل تنظیم‌اند.
  Datasetهای UNLOAD شامل فایل داده و فایل PUNCH هستند. حذف تنها پس از موفقیت مراحل
  وابسته انجام می‌شود. در خطا، فایل‌ها برای بررسی باقی می‌مانند.
  UNLOAD بدون AutoAllocate نیازمند هر دو Dataset از پیش ساخته‌شده و AllowOverwrite است.
  FTP: Binary (TYPE I)، Ascii (TYPE A)، Ebcdic (TYPE E)، passive/active، explicit TLS،
  timeout، اندازه بافر و retry. گواهی TLS باید معتبر باشد؛ گزینه نادیده گرفتن آن نداریم.
  SiteOptions برای هر گزینه SITE سرور، از جمله RDW و SBDATACONN، قابل ویرایش است.
  Binary هیچ تبدیل متنی انجام نمی‌دهد. ASCII/EBCDIC به تنظیم تبدیل سرور وابسته‌اند.
  برای UNLOAD باینری با رکورد VB، SITE RDW را برای حفظ مرز رکوردها فعال کنید؛
  برای بارگذاری مجدد آن فایل NativeInputHasRdw و عرض payload صحیح را تعیین کنید.
  FTP روی برخی سرورها SIZE را پشتیبانی نمی‌کند: در این حالت دانلود تعداد بایت دارد
  ولی درصد کل/ETA نمایش داده نمی‌شود. Upload درصد، سرعت و ETA همان مرحله را دارد.
  این نسخه SFTP نیست. FTPS و FTP active پیاده شده‌اند اما در این محیط زنده تست نشده‌اند.

سوئیچ‌های تخصصی
  UtilityOptions: گزینه‌های اضافی قبل از INTO TABLE / FROM TABLE.
  ControlStatements: متن کامل TEMPLATE/LOAD/UNLOAD؛ جایگزین کارت تولیدی برنامه می‌شود.
  توکن‌ها: {dataset}، {punch}، {table}، {target}.
  تب Utility cards امکان پیش‌نمایش، ویرایش و اعمال متن کامل را دارد؛ کارت‌های سفارشی
  محدود به فهرست گزینه‌های GUI نیستند. نحو دقیق تابع نسخه DB2 میزبان است.
  این برنامه از DSNUTILU با چهار پارامتر و حد 32704 بایت UTF-8 استفاده می‌کند.
  کاراکترهای پایان خط پیش از ارسال به فاصله تبدیل می‌شوند؛ از کارت‌های بدون comment
  وابسته به انتهای خط استفاده کنید. قالب native را از SYSPUNCH تولیدشده بگیرید.

گزارش و خطا
  هر اجرا یک پوشه شامل report.json، report.csv، report.html، events.jsonl، کارت‌های
  اجراشده و checkpoint هر Utility دارد. گزارش به‌تدریج پس از batchها ذخیره می‌شود.
  زمان FTP و Utility جداست؛ سرعت و تعداد رکورد هر مرحله در گزارش قابل بررسی است.
  شمار رکورد خروجی از SYSPRINT خوانده می‌شود. مجموع رکورد مراحل Transfer شمار عملیات
  است، نه تعداد یکتای رکورد؛ هر batch جدا گزارش می‌شود.
  RC=0 موفق، RC=4 هشدار قابل تنظیم، RC>4 خطا. برخی خطاهای سرور همزمان با بازگشت
  SYSPRINT قابل دریافت‌اند؛ DSNUTILU پیشرفت ردیفی زنده را روی این رابط ارائه نمی‌کند.
  برنامه هر ۵ ثانیه heartbeat می‌دهد و درصد جعلی برای Utility نمایش نمی‌دهد.
  Stop انتقال FTP را قطع و لغو فرمان DB2 را درخواست می‌کند. خاتمه کلاینت اثبات
  rollback یا خاتمه Utility روی سرور نیست. در قطع ارتباط بعد از شروع Utility،
  OutcomeUnknown ثبت می‌شود و LOAD هرگز خودکار تکرار نمی‌شود؛ UtilityId را بررسی کنید.
  retry خودکار مربوط به FTP پیش از Utility است. پروژه ذخیره‌شده، تنظیمات را بازیابی
  می‌کند؛ ادامه خودکار یک اجرای نیمه‌تمام پیاده نشده است. پروژه را کورکورانه دوباره
  اجرا نکنید، چون batchهای موفق ممکن است قبلاً در مقصد ثبت شده باشند.

CLI
  DSNForge.Cli.exe new project.json
  DSNForge.Cli.exe validate project.json
  DSNForge.Cli.exe preview project.json
  DSNForge.Cli.exe run project.json
  رمزها از DSNFORGE_DB2_PASSWORD و DSNFORGE_FTP_PASSWORD خوانده می‌شوند.
  نام متغیرها در پروژه قابل تغییر است. رمز را در آرگومان خط فرمان نگذارید.
  Ctrl+C درخواست لغو می‌دهد. exit code صفر یعنی Completed/CompletedWithWarnings،
  کد ۱ خطای اجرا و کد ۲ خطای ورودی/اعتبارسنجی است.
  مسیرهای نسبی پروژه نسبت به پوشه فایل JSON تفسیر می‌شوند.

Library
  نمونه استفاده در LIBRARY-USAGE.cs.txt است. IUtilityExecutor قابل جایگزینی برای تست
  یا درایور دیگر است. TransferEngine.RunAsync و ValidateRemoteAsync و رویداد Progress
  API مشترک GUI و CLI هستند. کتابخانه فعلاً هدف net48/x64 دارد.

آزمایش انجام‌شده در 2026-10-20
  Build Release بدون خطا/هشدار؛ آزمون‌های خودکار تبدیل، batch، گزارش و کنترل خطا.
  PREVIEW واقعی روی Db2 10.
  UNLOAD واقعی 10000 ردیف و دانلود داده و SYSPUNCH.
  Transfer واقعی 10000 ردیف بین دو جدول آزمایشی؛ حذف Dataset بعد از موفقیت؛
  مقایسه همه ۲۱ فیلد تمام رکوردها با منبع، شامل فارسی، موفق.
  LOAD CSV در دو batch با اندازه 5000؛ REPLACE سپس RESUME YES؛ RC=0؛
  مقایسه کامل 10000 ردیف و ۲۱ ستون موفق.
  GUI ساخته و تصویر آن در artifacts\desktop.png بررسی شده است.
  نمونه‌های examples به جدول‌ها و آدرس‌های فرضی اشاره دارند؛
  قبل از استفاده عملی، جدول‌ها و مسیرها را تنظیم کنید. هیچ رمز در پروژه‌ها نیست.
  فایل داده آزمایشی و گزارش‌های واقعی داخل artifacts هستند و در بسته release نمی‌آیند.

منابع
  https://www.ibm.com/docs/en/db2-for-zos/12.0.0?topic=db2-dsnutilu
  https://www.ibm.com/docs/en/db2-for-zos/12.0.0?topic=unload-load-statements-that-are-generated-by
  https://www.ibm.com/docs/en/db2-for-zos/12.0.0?topic=unload-sample-control-statements
  https://www.ibm.com/support/pages/using-ftp-transfer-variable-blocked-file-binary-data
