# mvc-inspect

> **A .NET global CLI tool that generates a full text structure tree for ASP.NET Core MVC projects — powered by Roslyn.**

[![NuGet](https://img.shields.io/badge/dotnet%20tool-mvc--inspect-blue?logo=nuget)](https://github.com/sbay-dev/mvc-inspect)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)](https://dotnet.microsoft.com)
[![Version](https://img.shields.io/badge/version-2.2.1-orange)](CHANGELOG.md)

---

## ما هي الأداة؟ / What is it?

**بالعربية:**  
`mvc-inspect` أداة سطر أوامر (.NET Global Tool) تحلّل مشاريع ASP.NET Core MVC وتنتج تقريراً نصياً شجرياً كاملاً يشمل كل عنصر في الكود — مفيدة لـ:
- ✅ مراجعة تصميم المشروع (Design Review)
- ✅ مقارنة مشروعين (Gap Analysis)
- ✅ Checklist مطابقة للمطورين
- ✅ توثيق هيكل الكود

**In English:**  
`mvc-inspect` is a .NET global CLI tool that analyzes ASP.NET Core MVC projects and produces a complete text structure tree covering every code element — useful for design review, gap analysis, developer checklists, and code documentation.

---

## التثبيت / Installation

```bash
dotnet tool install --global mvc-inspect
```

> متطلبات: .NET 8 SDK أو أحدث

---

## الاستخدام / Usage

### 1. فحص مشروع واحد / Inspect a single project

```bash
mvc-inspect <path>
```

**مثال:**
```bash
mvc-inspect "C:\source\MyMvcApp"
```

يحفظ التقرير تلقائياً داخل المجلد بطابع زمني:
```
[OK] Report saved to:
     C:\source\MyMvcApp\mvc-structure_20260306_064429.txt
```

---

### 2. تحليل الفجوات بين مشروعين / Gap analysis between two projects

```bash
mvc-inspect --compare <pathA> <pathB>
```

**مثال:**
```bash
mvc-inspect --compare "C:\source\ReferenceApp" "C:\source\DevApp"
```

يحفظ تقرير المقارنة داخل `pathB`:
```
[OK] Report saved to:
     C:\source\DevApp\mvc-gap-report_20260306_064430.txt
```

---

### 3. خيارات إضافية / Options

| الخيار | الوصف |
|--------|-------|
| `--out <file>` | تحديد مسار ملف الإخراج يدوياً |
| `--no-views` | استثناء ملفات `.cshtml` |
| `--cs-only` | ملفات C# فقط |
| `--no-migrations` | استثناء مجلد Migrations |

**أمثلة:**
```bash
# حفظ في مسار مخصص
mvc-inspect "C:\source\MyApp" --out "C:\reports\structure.txt"

# C# فقط بدون Razor وبدون Migrations
mvc-inspect "C:\source\MyApp" --cs-only --no-migrations

# مقارنة مع حفظ في مسار مخصص
mvc-inspect --compare "C:\Ref" "C:\Dev" --out "C:\reports\gap.txt"
```

---

## محتويات التقرير / Report Contents

### شجرة المشروع الواحد (`mvc-structure_*.txt`)

```
MyMvcApp/
├── Controllers/
│   └── HomeController.cs
│       └── namespace MyMvcApp.Controllers
│           └── [class] HomeController : Controller
│               ├── [field]  - _context : ApplicationDbContext
│               ├── [ctor]   + HomeController(ApplicationDbContext context)
│               ├── [method] + Index() : IActionResult
│               │               [GET /]
│               ├── [method] + About() : IActionResult
│               └── [method] + Error() : IActionResult
├── Models/
│   └── User.cs
│       └── namespace MyMvcApp.Models
│           └── [class] User
│               ├── [prop]   + Id : int  { get; set; }
│               ├── [prop]   + Name : string  { get; set; }
│               └── [prop]   + Email : string  { get; set; }
└── Views/
    └── Home/
        └── Index.cshtml
            ├── @model   MyMvcApp.Models.HomeViewModel
            ├── Layout   _Layout
            └── asp-for  Name, Email
```

**العناصر المستخرجة:**

| الرمز | النوع |
|-------|-------|
| `[class]` | كلاس |
| `[interface]` | واجهة |
| `[enum]` | تعداد |
| `[struct]` | هيكل |
| `[record]` | سجل |
| `[delegate]` | مفوّض |
| `[snippet]` | مقتطف (local function / lambda) |
| `[field]` | حقل |
| `[prop]` | خاصية |
| `[ctor]` | منشئ |
| `[method]` | دالة |
| `+` | public |
| `-` | private |
| `#` | protected |
| `~` | internal |

---

### تقرير الفجوات (`mvc-gap-report_*.txt`)

يشمل أربعة أقسام:

1. **ملخص تنفيذي** — جدول بعدد الملفات، الكلاسات، الدوال في كل مشروع
2. **فجوات C#** — ملفات مفقودة أو مختلفة على مستوى الكلاسات والأعضاء
3. **فجوات Razor** — ملفات `.cshtml` المفقودة أو المختلفة
4. **قائمة مهام المطور** — Checklist جاهزة للتنفيذ

---

## السلوك عند الحفظ / Save Behavior

| الحالة | الملف المولَّد |
|--------|----------------|
| `mvc-inspect <path>` (تلقائي) | `mvc-structure_yyyyMMdd_HHmmss.txt` |
| `mvc-inspect --compare <A> <B>` (تلقائي) | `mvc-gap-report_yyyyMMdd_HHmmss.txt` |
| مع `--out <file>` | المسار المحدد يدوياً |

> **ملاحظة:** التقارير التلقائية تحمل طابعاً زمنياً دائماً لمنع الكتابة فوق السجلات السابقة وتمكين المقارنة عبر الزمن.

---

## الامتدادات المحمية / Protected Extensions

لا تقبل الأداة كتابة الناتج (`--out`) على الملفات الحساسة:
`.sln` `.csproj` `.cs` `.cshtml` `.json` `.config` `.xml` `.dll` `.exe`

---

## المتطلبات / Requirements

- .NET 8.0 SDK or later
- Windows / Linux / macOS

---

## البناء من المصدر / Build from Source

```bash
git clone https://github.com/sbay-dev/mvc-inspect
cd mvc-inspect/src
dotnet build -c Release
dotnet pack -c Release
dotnet tool install --global --add-source . mvc-inspect
```

---

## سجل التغييرات / Changelog

### v2.2.1 (2026-03-06)
- 🔒 التقارير التلقائية تحمل طابعاً زمنياً (`yyyyMMdd_HHmmss`) لمنع الكتابة فوق السجلات السابقة

### v2.2.0
- 💾 حفظ تلقائي دائماً داخل مجلد المشروع (لا يطبع على الشاشة)
- 🚫 حماية الملفات الحساسة من الكتابة عليها عبر `--out`

### v2.0.0
- ✅ دعم `.cshtml` و `.cshtml.cs` كعنصر MVC أساسي
- ✅ تقرير فجوات Razor مع مقارنة كاملة
- ✅ استخرج: `@model`, `@inject`, `Layout`, `@section`, `asp-for`, `<partial>`, ViewBag/ViewData

### v1.0.0
- 🎉 الإصدار الأول — فحص C# بـ Roslyn، مقارنة المشاريع، تقرير الفجوات

---

## الترخيص / License

MIT © 2025-2026 [SBAY-SDK](https://github.com/sbay-dev)
