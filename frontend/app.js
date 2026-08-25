const configuredApiBase = window.LENSEE_CONFIG?.apiBaseUrl?.trim();
const isLocalHost = ["localhost", "127.0.0.1", "::1"].includes(location.hostname);
const defaultApiBase = isLocalHost ? "http://localhost:5000" : "";
let apiBase = configuredApiBase || localStorage.getItem("lensee.apiBase") || defaultApiBase;
const authKey = "lensee.auth";
let activeAuth = null;
const apiCandidates = [
  configuredApiBase,
  localStorage.getItem("lensee.apiBase"),
  ...(isLocalHost ? ["http://localhost:5000", "https://localhost:7237"] : [])
].filter(Boolean);
const ngrokSkipHeader = "ngrok-skip-browser-warning";
const mutationEventName = "lensee:data-mutated";
const authEventName = "lensee:auth-changed";

let catalogCategories = [];
let categoryTree = [];
let catalogBrands = [];
let selectedProductId = null;
let inventoryLocations = [];
let inventorySkuOptions = [];
let selectedInventoryLocationId = "";
let operationLocations = [];
let operationSkuOptions = [];
let operationProductOptions = [];
let operationAvailableSkuIds = null;
let operationMerchantOptions = [];
let operationRepresentativeOptions = [];
let selectedSupplyShipmentId = null;
let supplyShipments = [];
let supplyCurrentDetail = null;
let supplySkuLoadPromise = null;
let supplySkuSearchIndex = [];
let operationsUiState = {
  mode: "create",
  operationId: null,
  operationType: "WarehouseTransfer",
  revisionReason: "",
  revisionFingerprint: null,
  openDetailIds: []
};
let paymentMerchants = [];
let paymentAccountants = [];
let paymentHistoryRows = [];
let reportOperationRows = [];
let reportPaymentRows = [];
let reportMerchantRows = [];
let reportStocktakeRows = [];
let reportSupplyRows = [];
let selectedMerchantId = null;
let selectedRepresentativeId = null;
let auditPageState = { page: 1, pageSize: 50 };
let notificationPageState = { page: 1, pageSize: 10 };
let shopifyIntegrationPageState = { page: 1, pageSize: 25 };
let shopifySkuPageState = { page: 1, pageSize: 50 };
let activeRefreshTimer = null;
let activeRefreshController = null;
let activeRefreshInFlight = false;
let notificationBadgeInFlight = false;
let refreshSessionPromise = null;
let noticeSequence = 0;
const mutationLocks = new Set();
const displayReferenceCache = new Map();
let visibleIdentifierObserver = null;

const syncChannel = "BroadcastChannel" in window ? new BroadcastChannel("lensee-sync") : null;
const syncStorageKey = "lensee.sync";
const refreshLockName = "lensee-auth-refresh";
const refreshLockStorageKey = "lensee.refresh.lock";
const refreshLockLeaseMs = 30000;
const refreshLockWaitMs = 35000;

const languageKey = "lensee.language";
let currentLanguage = localStorage.getItem(languageKey) === "en" ? "en" : "ar";
let applyingLanguage = false;
let languageApplyTimer = null;
let languageObserver = null;

const arabicTranslations = Object.freeze({
  "Sign In": "تسجيل الدخول",
  "Identity": "الهوية وتسجيل الدخول",
  "Overview": "نظرة عامة",
  "Dashboard": "لوحة التحكم",
  "Catalog": "الكتالوج",
  "Inventory": "المخزون",
  "Supply": "التوريد",
  "CRM": "إدارة العلاقات التجارية",
  "Operations": "العمليات",
  "Payments": "المدفوعات",
  "Notifications": "التنبيهات",
  "Reports": "التقارير",
  "Stocktake": "الجرد",
  "Admin": "مدير النظام",
  "Administration": "إدارة النظام",
  "Operations Console": "منصة تشغيل Lensee",
  "Sign in": "تسجيل الدخول",
  "Sign out": "تسجيل الخروج",
  "API healthy": "الخادم متصل ويعمل",
  "API degraded": "الخادم متصل جزئيًا",
  "API offline": "الخادم غير متصل",
  "Checking API": "جارٍ التحقق من اتصال الخادم",
  "Not signed in": "غير مسجّل الدخول",
  "Location scoped": "مقيّد بالموقع المعيّن",
  "Access denied": "غير مصرح بالدخول",
  "This session cannot open that workspace.": "لا تملك هذه الجلسة صلاحية فتح مساحة العمل المطلوبة.",
  "Continue": "متابعة",
  "Cancel": "إلغاء",
  "Confirm": "تأكيد",
  "Create": "إنشاء",
  "Clear": "مسح",
  "Reset": "إعادة ضبط",
  "Refresh": "تحديث",
  "Save": "حفظ",
  "Save draft": "حفظ المسودة",
  "New": "جديد",
  "Edit": "تعديل",
  "Detail": "التفاصيل",
  "Details": "التفاصيل",
  "Actions": "الإجراءات",
  "Action": "الإجراء",
  "Loading": "جارٍ التحميل",
  "Loading...": "جارٍ التحميل...",
  "No results": "لا توجد نتائج",
  "Name": "الاسم",
  "Business name": "اسم النشاط",
  "Contact person": "مسؤول التواصل",
  "Phone": "رقم الهاتف",
  "Business type": "نوع النشاط",
  "Merchant": "تاجر",
  "Merchants": "التجار",
  "Representative": "مندوب",
  "Representatives": "المندوبون",
  "Add merchant": "إضافة تاجر",
  "Create merchant": "إضافة تاجر",
  "Update merchant": "تعديل بيانات التاجر",
  "Add note": "إضافة ملاحظة",
  "Deactivate": "إيقاف",
  "Reactivate": "إعادة تفعيل",
  "Active": "نشط",
  "Inactive": "غير نشط",
  "Locations": "المواقع",
  "Location": "الموقع",
  "Stock balances": "أرصدة المخزون",
  "Batches": "دفعات المخزون",
  "Expired batches": "دفعات منتهية الصلاحية",
  "Transactions": "حركات المخزون",
  "Set target": "تحديد المستهدف",
  "Target": "الجهة المستهدفة",
  "Low stock": "مخزون منخفض",
  "No target": "بدون مستهدف",
  "Show zero-stock SKUs": "إظهار الأصناف ذات الرصيد الصفري",
  "Show empty batches": "إظهار الدفعات الخالية",
  "Operations control": "لوحة العمليات",
  "Create draft": "إنشاء مسودة",
  "Type": "النوع",
  "Source location": "موقع الصرف",
  "Destination location": "موقع الاستلام",
  "Buyer name": "اسم العميل",
  "Buyer phone": "رقم هاتف العميل",
  "Payment method": "طريقة الدفع",
  "Supplier": "المورد",
  "Invoice": "رقم الفاتورة",
  "Notes": "ملاحظات",
  "Revision reason": "سبب المراجعة",
  "Operation lines": "بنود العملية",
  "Add line": "إضافة بند",
  "Find stock": "البحث في المخزون",
  "Product": "المنتج",
  "Power": "درجة العدسة",
  "Color": "اللون",
  "Package": "نوع العبوة",
  "Side": "الطرف",
  "Returned": "مرتجع",
  "Replacement": "بديل",
  "Mode": "وحدة الإدخال",
  "Packs": "عبوات",
  "Pieces": "قطع",
  "Quantity": "الكمية",
  "Unit price": "سعر الوحدة",
  "Bonus": "مجاني",
  "Batch / expiry": "الدفعة / تاريخ الصلاحية",
  "Lot": "رقم الدفعة",
  "Batch expiry": "تاريخ انتهاء الدفعة",
  "Resolved SKU": "رمز الصنف المحدد",
  "Select merchant": "اختر التاجر",
  "Select representative": "اختر المندوب",
  "Inventory receipt": "استلام مخزون",
  "Warehouse transfer": "تحويل مخزون",
  "Wholesale sale": "بيع جملة",
  "Retail/online sale": "بيع قطاعي / أونلاين",
  "Representative reserve": "حجز للمندوب",
  "Return": "مرتجع",
  "Change": "استبدال",
  "Write-off": "إعدام / تسوية مخزون",
  "Draft": "مسودة",
  "Reserved": "محجوز",
  "Shipped": "تم الشحن",
  "Received": "تم الاستلام",
  "Completed": "مكتمل",
  "Confirmed": "مؤكد",
  "Cancelled": "ملغي",
  "PendingAdminReview": "بانتظار مراجعة الإدارة",
  "Rejected": "مرفوض",
  "Approved": "معتمد",
  "Cash hand to hand": "نقدي مباشر",
  "Cash transaction": "تحويل أو إيداع نقدي",
  "Installment": "تقسيط",
  "Payments and remaining": "المدفوعات والمتبقي",
  "Amount": "المبلغ",
  "Assign": "إسناد",
  "Approve": "اعتماد",
  "Reject": "رفض",
  "Load remaining": "تحميل المتبقي",
  "Reports and exports": "التقارير والتصدير",
  "Download": "تنزيل",
  "Export log": "سجل التصدير",
  "Stock": "المخزون",
  "Payment": "المدفوعات",
  "Merchant remaining": "المتبقي على التجار",
  "Stocktake sessions": "جلسات الجرد",
  "No users found.": "لم يتم العثور على مستخدمين.",
  "No merchants yet.": "لا يوجد تجار بعد.",
  "No representatives yet.": "لا يوجد مندوبون بعد.",
  "Show completed/received/cancelled history": "إظهار سجل العمليات المكتملة والمستلمة والملغاة",
  "Username": "اسم المستخدم",
  "Password": "كلمة المرور",
  "Show password": "إظهار كلمة المرور",
  "Show": "إظهار",
  "Hide": "إخفاء",
  "Sign in to Lensee": "تسجيل الدخول إلى Lensee",
  "Operations ERP": "نظام تشغيل موارد المؤسسة",
  "Lensee access control": "دخول نظام Lensee",
  "Secure ERP access for daily operations.": "دخول آمن لنظام التشغيل اليومي.",
  "Secure entry for catalog, stock, operations, payments, and reporting workspaces.": "دخول آمن لمساحات الكتالوج والمخزون والعمليات والمدفوعات والتقارير.",
  "Authorized session": "جلسة مصرح بها",
  "Role and location permissions are applied after login.": "يتم تطبيق صلاحيات الدور والموقع بعد تسجيل الدخول.",
  "Sign in with your account to continue to the workspace.": "سجّل الدخول بحسابك للمتابعة إلى مساحة العمل.",
  "Required": "مطلوب",
  "No matches": "لا توجد نتائج مطابقة",
  "Select product": "اختر المنتج",
  "Select source and SKU": "اختر موقع الصرف ورمز الصنف",
  "Select batch / expiry": "اختر الدفعة وتاريخ الصلاحية",
  "Not required": "غير مطلوب",
  "Loading stock...": "جارٍ تحميل المخزون...",
  "No non-expired stock": "لا يوجد مخزون صالح",
  "Failed to load stock": "تعذر تحميل المخزون",
  "No export logs yet.": "لا توجد سجلات تصدير حتى الآن.",
  "All available": "الكل",
  "All available locations": "جميع المواقع المتاحة",
  "All locations": "جميع المواقع",
  "All SKUs": "جميع رموز الأصناف",
  "All types": "جميع الأنواع",
  "Loading users...": "جارٍ تحميل المستخدمين...",
  "Loading catalog": "جارٍ تحميل الكتالوج",
  "Loading product": "جارٍ تحميل المنتج",
  "Loading merchant detail...": "جارٍ تحميل تفاصيل التاجر...",
  "Loading operation details...": "جارٍ تحميل تفاصيل العملية...",
  "Loading payment details...": "جارٍ تحميل تفاصيل المدفوعة...",
  "Loading stocktakes...": "جارٍ تحميل جلسات الجرد...",
  "No active operations.": "لا توجد عمليات نشطة.",
  "No stock rows.": "لا توجد أرصدة مخزون.",
  "No operations.": "لا توجد عمليات.",
  "No payment logs.": "لا توجد سجلات دفع.",
  "No merchant remaining.": "لا يوجد متبقٍ على التجار.",
  "Users and access": "المستخدمون والصلاحيات",
  "User": "المستخدم",
  "Role": "الدور",
  "Status": "الحالة",
  "New password": "كلمة المرور الجديدة",
  "Warehouse Clerk": "أمين المخزن",
  "C-Level": "الإدارة التنفيذية",
  "Accountant": "محاسب",
  "Open session": "فتح جلسة",
  "Sessions": "الجلسات",
  "Session detail": "تفاصيل الجلسة",
  "Counted": "الكمية المعدودة",
  "Discrepancy": "فرق الجرد",
  "Run replenishment": "تشغيل إعادة التوريد",
  "Daily replenishment": "إعادة التوريد اليومية",
  "Mark read": "تحديد كمقروء",
  "Mark all read": "تحديد الكل كمقروء",
  "Unread": "غير مقروء",
  "Read": "مقروء",
  "Document downloads": "تنزيل المستندات",
  "Operation bill": "فاتورة العملية",
  "Payment receipt": "إيصال الدفع",
  "Merchant statement": "كشف حساب التاجر",
  "Stocktake summary": "ملخص الجرد",
  "Download bill": "تنزيل الفاتورة",
  "Download receipt": "تنزيل الإيصال",
  "Download statement": "تنزيل كشف الحساب",
  "Download summary": "تنزيل ملخص الجرد",
  "PDF": "PDF",
  "Create category": "إنشاء تصنيف",
  "Create brand": "إنشاء علامة تجارية",
  "Create product": "إنشاء منتج",
  "Save SKU": "حفظ كود الصنف",
  "Barcode": "الباركود",
  "Size": "المقاس",
  "Unknown SKU": "رمز صنف غير معروف",
  "SKU conflict": "تعارض في رمز الصنف",
  "No matching SKU": "لا يوجد رمز صنف مطابق",
  "Cash received": "تحصيل نقدي",
  "Cash refund": "استرداد نقدي",
  "Financial adjustment": "تسوية مالية",
  "Merchant credit": "رصيد دائن للتاجر",
  "Remaining reduction": "تخفيض المتبقي",
  "Draft sub-log": "حفظ كسجل فرعي مسودة",
  "Record cash": "تسجيل حركة نقدية",
  "Save adjustment": "حفظ التسوية",
  "Date received": "تاريخ التحصيل",
  "Operation reference": "مرجع العملية",
  "Payment log reference": "مرجع سجل الدفع",
  "Payment history": "سجل حركة المدفوعات",
  "When": "التوقيت",
  "Event": "الحدث",
  "Buyer / merchant": "العميل / التاجر",
  "Actor": "المسؤول",
  "Payment log opened": "تم فتح سجل دفع",
  "Installment drafted": "تم تسجيل قسط كمسودة",
  "Installment approved": "تم اعتماد القسط",
  "Installment rejected": "تم رفض القسط",
  "Cash receipt recorded": "تم تسجيل تحصيل نقدي",
  "Cash receipt approved": "تم اعتماد التحصيل النقدي",
  "Cash refund recorded": "تم تسجيل الاسترداد النقدي",
  "Financial cash refund": "استرداد نقدي مالي",
  "Assigned to accountant": "تم الإسناد إلى المحاسب",
  "No stage history yet.": "لا يوجد سجل للمراحل حتى الآن.",
  "No cash records.": "لا توجد حركات نقدية.",
  "No financial adjustments.": "لا توجد تسويات مالية.",
  "No payment history yet.": "لا يوجد سجل لحركة المدفوعات بعد.",
  "No installment or cash confirmations are waiting.": "لا توجد أقساط أو حركات نقدية في انتظار الاعتماد.",
  "open confirmations": "اعتمادات معلقة",
  "Daily work": "العمل اليومي",
  "Money": "الماليات",
  "Stock": "المخزون",
  "Oversight": "المتابعة والإدارة",
  "Open navigation": "فتح التنقل",
  "Close navigation": "إغلاق التنقل",
  "Open work": "عمل مفتوح",
  "Open confirmations": "تأكيدات مفتوحة",
  "Stock attention": "مخزون يحتاج متابعة",
  "Unread alerts": "تنبيهات غير مقروءة",
  "Operator command center": "مركز قيادة التشغيل",
  "Queue": "قائمة الانتظار",
  "Ledger": "السجل",
  "Tools": "الأدوات",
  "Confirmations queue": "قائمة التأكيدات",
  "Payment ledger": "سجل المدفوعات",
  "Workflow rail": "مسار العمل",
  "Active queue": "قائمة نشطة",
  "Create and revise": "إنشاء ومراجعة",
  "record": "سجل",
  "product": "منتج",
  "Export intent logged.": "تم تسجيل طلب التصدير.",
  "Report downloaded.": "تم تنزيل التقرير.",
  "PDF downloaded.": "تم تنزيل ملف PDF.",
  "Select a document row before downloading.": "اختر سجل المستند المطلوب قبل التنزيل.",
  "Cannot reach the API. Check the API base URL and whether the host is running.": "لا يمكن الوصول إلى النظام. راجع عنوان الخادم وتأكد من تشغيله.",
  "English": "الإنجليزية",
  "Switch to English": "التبديل إلى الإنجليزية",
  "Switch to Arabic": "التبديل إلى العربية",
  "Dismiss notice": "إغلاق الإشعار",
  "Authorization": "التفويض",
  "Forbidden": "غير مسموح",
  "Signing in": "جارٍ تسجيل الدخول",
  "Username or password is incorrect.": "اسم المستخدم أو كلمة المرور غير صحيحة.",
  "The API cannot connect to PostgreSQL. Check the database connection and restart the backend if needed.": "يتعذر على الخادم الاتصال بقاعدة بيانات PostgreSQL. راجع إعدادات الاتصال ثم أعد تشغيل الخادم.",
  "Sign in failed. Check the account credentials and try again.": "فشل تسجيل الدخول. راجع بيانات الحساب ثم حاول مرة أخرى.",
  "Session expired. Sign in again.": "انتهت صلاحية الجلسة. سجّل الدخول مرة أخرى.",
  "This account does not have permission for that action.": "هذا الحساب لا يملك صلاحية تنفيذ هذا الإجراء.",
  "Check the request values.": "راجع القيم المُدخلة في الطلب.",
  "The workspace request failed.": "فشل تنفيذ الطلب داخل مساحة العمل.",
  "Could not load users.": "تعذر تحميل المستخدمين.",
  "No rows are available for this workspace yet.": "لا توجد بيانات متاحة في مساحة العمل هذه حتى الآن.",
  "Lensee operations control center": "مركز التحكم في عمليات Lensee",
  "Products, SKUs, categories, and brands.": "المنتجات ورموز الأصناف والتصنيفات والعلامات التجارية.",
  "Stock balances, batches, replenishment, and targets.": "أرصدة المخزون والدفعات وإعادة التوريد والمستهدفات.",
  "Receipts, transfers, sales, returns, changes, and write-offs.": "الاستلامات والتحويلات والمبيعات والمرتجعات والاستبدالات والتسويات.",
  "Payment logs, approvals, cash records, and live remaining.": "سجلات الدفع والاعتمادات والحركات النقدية والمتبقي الحالي.",
  "Workflow alerts, stock alerts, and operational updates.": "تنبيهات سير العمل والمخزون والتحديثات التشغيلية.",
  "CSV exports, PDF documents, and export history.": "تصدير ملفات CSV ومستندات PDF وسجل عمليات التصدير.",
  "Batch-aware counts and reconciliations.": "جرد وتسويات مع تتبع دفعات المخزون.",
  "Users, passwords, and access maintenance.": "إدارة المستخدمين وكلمات المرور والصلاحيات.",
  "Open workspace": "فتح مساحة العمل",
  "Current role": "الدور الحالي",
  "Workspace access": "صلاحيات مساحات العمل",
  "Scope": "النطاق",
  "Assigned location access": "الوصول إلى الموقع المعيّن",
  "Cross-location access": "الوصول إلى جميع المواقع",
  "Workspace map": "خريطة مساحات العمل",
  "Cross-module administration": "إدارة شاملة لكل الوحدات",
  "Cross-module administration. Start with open work, then move to money, stock, and reports without losing operational context.": "إدارة شاملة لكل الوحدات. ابدأ بالأعمال المفتوحة، ثم انتقل إلى الماليات والمخزون والتقارير دون فقدان سياق التشغيل.",
  "Executive oversight": "إشراف تنفيذي",
  "Payments and remaining control": "إدارة المدفوعات والمتبقي",
  "Inventory and operational execution": "تنفيذ أعمال المخزون والعمليات",
  "modules": "وحدات",
  "Catalog master data": "البيانات الأساسية للكتالوج",
  "Manage products, SKUs, categories, and brands with clear active states and reusable product structure.": "إدارة المنتجات ورموز الأصناف والتصنيفات والعلامات التجارية مع حالات تفعيل واضحة وبنية منتجات قابلة لإعادة الاستخدام.",
  "Filters": "عوامل التصفية",
  "Search": "بحث",
  "Product, brand, category": "المنتج أو العلامة التجارية أو التصنيف",
  "Show inactive products": "إظهار المنتجات غير النشطة",
  "Products": "المنتجات",
  "Brand": "العلامة التجارية",
  "Category": "التصنيف",
  "Pack": "العبوة",
  "Writable": "قابل للتعديل",
  "Read only": "للقراءة فقط",
  "Product detail": "تفاصيل المنتج",
  "Select a product to review its configuration, SKU set, and lifecycle state.": "اختر منتجًا لعرض إعداداته ورموز أصنافه وحالة تفعيله.",
  "Access": "الصلاحيات",
  "This role can review catalog data but cannot change it.": "يمكن لهذا الدور عرض بيانات الكتالوج فقط دون تعديلها.",
  "Product editor": "محرر المنتج",
  "Sell mode": "طريقة البيع",
  "Single piece": "قطعة منفردة",
  "Sealed pack only": "عبوة مغلقة فقط",
  "Both": "كلاهما",
  "Pieces per pack": "عدد القطع في العبوة",
  "Expiry source": "مصدر الصلاحية",
  "Batch expiry date": "تاريخ انتهاء دفعة المخزون",
  "No batch expiry": "دون صلاحية على مستوى الدفعة",
  "Valid for": "مدة الصلاحية بعد الفتح",
  "Duration unit": "وحدة المدة",
  "Days": "أيام",
  "Months": "أشهر",
  "Years": "سنوات",
  "New product": "منتج جديد",
  "Categories": "التصنيفات",
  "Parent": "التصنيف الأب",
  "None": "لا يوجد",
  "New category": "تصنيف جديد",
  "Brands": "العلامات التجارية",
  "New brand": "علامة تجارية جديدة",
  "No categories": "لا توجد تصنيفات",
  "No products found": "لم يتم العثور على منتجات",
  "Expiry": "الصلاحية",
  "Opening validity": "صلاحية الاستخدام بعد الفتح",
  "Unused in MVP": "غير مستخدم في النسخة الحالية",
  "Batch expiry dates on inventory batches control FEFO, sales, transfers, and opened-piece expiry.": "تتحكم تواريخ انتهاء دفعات المخزون في الصرف حسب الأقرب انتهاءً والمبيعات والتحويلات وصلاحية القطع بعد الفتح.",
  "SKUs": "رموز الأصناف",
  "Generated SKU": "رمز الصنف المُنشأ",
  "Derived after save": "يتم إنشاؤه بعد الحفظ",
  "Power sign": "إشارة الدرجة",
  "Power value": "قيمة الدرجة",
  "SKU": "رمز الصنف",
  "No SKUs": "لا توجد رموز أصناف",
  "Update category": "تحديث التصنيف",
  "Update brand": "تحديث العلامة التجارية",
  "Update product": "تحديث المنتج",
  "Product status updated.": "تم تحديث حالة المنتج.",
  "SKU saved.": "تم حفظ رمز الصنف.",
  "SKU status updated.": "تم تحديث حالة رمز الصنف.",
  "Category saved.": "تم حفظ التصنيف.",
  "Brand saved.": "تم حفظ العلامة التجارية.",
  "Product saved.": "تم حفظ المنتج.",
  "Product name, category, and brand are required.": "اسم المنتج والتصنيف والعلامة التجارية حقول مطلوبة.",
  "Pieces per pack must be greater than zero.": "يجب أن يكون عدد القطع في العبوة أكبر من صفر.",
  "Clinical params": "الخصائص الطبية",
  "Color is required for lens SKUs.": "اللون مطلوب عند إنشاء رمز صنف لعدسة.",
  "Size is required for solution SKUs.": "الحجم مطلوب عند إنشاء رمز صنف للمحلول.",
  "Check the catalog form values.": "راجع القيم المُدخلة في نموذج الكتالوج.",
  "That SKU code already exists.": "رمز الصنف هذا مستخدم بالفعل.",
  "You do not have permission to change catalog data.": "لا تملك صلاحية تعديل بيانات الكتالوج.",
  "Catalog change failed.": "فشل حفظ التعديل في الكتالوج.",
  "You do not have access to this catalog action.": "لا تملك صلاحية تنفيذ هذا الإجراء في الكتالوج.",
  "Could not load catalog data.": "تعذر تحميل بيانات الكتالوج.",
  "Stock, batches, and replenishment": "المخزون والدفعات وإعادة التوريد",
  "Monitor available stock, reserved stock, replenishment gaps, blocked expiry batches, and the immutable stock ledger.": "تابع المخزون المتاح والمحجوز ونواقص إعادة التوريد والدفعات المحظورة بسبب انتهاء الصلاحية وسجل حركات المخزون.",
  "Available": "المتاح",
  "Meant to be": "المستهدف",
  "Needed": "المطلوب",
  "Updated": "آخر تحديث",
  "Loading stock": "جارٍ تحميل المخزون",
  "Destination": "الوجهة",
  "Incoming": "الوارد",
  "Main available": "المتاح في المخزن الرئيسي",
  "Loading replenishment": "جارٍ تحميل احتياجات إعادة التوريد",
  "Expired batches are blocked from FEFO sale, transfer, reserve, and write-off allocation.": "تُحظر الدفعات منتهية الصلاحية من البيع والتحويل والحجز والتسوية عند الصرف حسب الأقرب انتهاءً.",
  "Reason": "السبب",
  "Loading expired batches": "جارٍ تحميل الدفعات منتهية الصلاحية",
  "Expiry date": "تاريخ الانتهاء",
  "Loading batches": "جارٍ تحميل دفعات المخزون",
  "Created": "تاريخ الإنشاء",
  "Loading transactions": "جارٍ تحميل حركات المخزون",
  "No locations": "لا توجد مواقع",
  "Catalog unavailable": "الكتالوج غير متاح",
  "No stock balances yet.": "لا توجد أرصدة مخزون حتى الآن.",
  "No target-stock rows yet.": "لا توجد مستهدفات مخزون حتى الآن.",
  "Covered": "مغطى",
  "No batches yet.": "لا توجد دفعات مخزون حتى الآن.",
  "No expired batches.": "لا توجد دفعات منتهية الصلاحية.",
  "No transactions yet.": "لا توجد حركات مخزون حتى الآن.",
  "Healthy": "المخزون مناسب",
  "Inactive SKU": "رمز صنف غير نشط",
  "pieces not set": "عدد القطع غير محدد",
  "No expiry": "بدون تاريخ انتهاء",
  "expired": "منتهي الصلاحية",
  "Set Target Packs": "تحديد مستهدف العبوات",
  "Target stock is measured in packs.": "يُقاس مستهدف المخزون بعدد العبوات.",
  "Target packs must be a non-negative whole number.": "يجب أن يكون مستهدف العبوات عددًا صحيحًا لا يقل عن صفر.",
  "Target packs updated.": "تم تحديث مستهدف العبوات.",
  "Check the inventory filters or target packs.": "راجع عوامل تصفية المخزون أو قيمة مستهدف العبوات.",
  "You do not have access to this inventory action.": "لا تملك صلاحية تنفيذ هذا الإجراء في المخزون.",
  "Could not load inventory data.": "تعذر تحميل بيانات المخزون.",
  "MainWarehouse": "المخزن الرئيسي",
  "SubWarehouse": "مخزن فرعي",
  "Online": "الأونلاين",
  "Retail": "نقطة بيع",
  "Target packs": "مستهدف العبوات",
  "Available packs": "العبوات المتاحة",
  "Available pieces": "القطع المتاحة",
  "Reserved packs": "العبوات المحجوزة",
  "Reserved pieces": "القطع المحجوزة",
  "Shortage": "العجز",
  "Location type": "نوع الموقع",
  "Stock ledger": "سجل حركات المخزون",
  "Transaction type": "نوع الحركة",
  "Reference": "المرجع",
  "Occurred at": "وقت الحركة",
  "Merchant and representative records": "بيانات التجار والمندوبين",
  "Maintain commercial relationships, operational notes, and merchant context used across sales, returns, payments, and reporting.": "إدارة العلاقات التجارية والملاحظات التشغيلية وبيانات التجار المستخدمة في المبيعات والمرتجعات والمدفوعات والتقارير.",
  "Profiles, commercial contacts, remaining context, and operational history.": "الملفات التجارية وبيانات التواصل والمتبقي والسجل التشغيلي.",
  "Pharmacy": "صيدلية",
  "Oculist": "طبيب عيون",
  "BeautyCenter": "مركز تجميل",
  "Other": "أخرى",
  "Business": "النشاط",
  "Contact": "جهة الاتصال",
  "External": "خارجي",
  "Internal": "داخلي",
  "Create representative": "إضافة مندوب",
  "Business name and contact person are required.": "اسم النشاط ومسؤول التواصل حقول مطلوبة.",
  "Merchant updated.": "تم تحديث بيانات التاجر.",
  "Merchant created.": "تم إنشاء التاجر.",
  "Representative name is required.": "اسم المندوب مطلوب.",
  "Representative updated.": "تم تحديث بيانات المندوب.",
  "Representative created.": "تم إنشاء المندوب.",
  "Representative not found.": "لم يتم العثور على المندوب.",
  "Merchant deactivated.": "تم إيقاف التاجر.",
  "Merchant reactivated.": "تمت إعادة تفعيل التاجر.",
  "Representative deactivated.": "تم إيقاف المندوب.",
  "Representative reactivated.": "تمت إعادة تفعيل المندوب.",
  "Add Merchant Note": "إضافة ملاحظة للتاجر",
  "Write a short note for this merchant profile.": "اكتب ملاحظة قصيرة في ملف التاجر.",
  "Note added.": "تمت إضافة الملاحظة.",
  "Sold packs": "العبوات المباعة",
  "Sold pieces": "القطع المباعة",
  "Remaining": "المتبقي",
  "Operation": "العملية",
  "Qty": "الكمية",
  "Total": "الإجمالي",
  "No operations for this merchant yet.": "لا توجد عمليات لهذا التاجر حتى الآن.",
  "Sold minus returned by SKU, lot, and batch expiry": "المباع مطروحًا منه المرتجع حسب رمز الصنف ورقم الدفعة وتاريخ الانتهاء",
  "Latest notes": "أحدث الملاحظات",
  "No notes yet.": "لا توجد ملاحظات حتى الآن.",
  "Merchant return/change reference": "مرجع مرتجع أو استبدال التاجر",
  "Sold": "المباع",
  "Alert": "تنبيه",
  "No confirmed merchant sales or returns yet.": "لا توجد مبيعات أو مرتجعات مؤكدة لهذا التاجر حتى الآن.",
  "OK": "سليم",
  "Select a merchant": "اختر تاجرًا",
  "Add representative": "إضافة مندوب",
  "Update representative": "تحديث بيانات المندوب",
  "Representative type": "نوع المندوب",
  "Merchant detail": "تفاصيل التاجر",
  "Representative detail": "تفاصيل المندوب",
  "Commercial profile": "الملف التجاري",
  "Start a new operation draft.": "ابدأ مسودة عملية جديدة.",
  "This role can inspect operations but cannot create or revise drafts.": "يمكن لهذا الدور عرض العمليات فقط، ولا يمكنه إنشاء المسودات أو تعديلها.",
  "No.": "الرقم",
  "Route": "المسار",
  "Receipt only": "خاص بعمليات الاستلام",
  "Used for receipt flows": "يُستخدم في مسارات الاستلام",
  "Required for revisions": "مطلوب عند مراجعة عملية",
  "Select product attributes to resolve SKU.": "اختر خصائص المنتج لتحديد رمز الصنف.",
  "Select product, power, and color to resolve SKU.": "اختر المنتج ودرجة العدسة واللون لتحديد رمز الصنف.",
  "Try another color, power, package, or source location.": "جرّب لونًا أو درجة أو عبوة أو موقع صرف آخر.",
  "SKUs match these attributes. Refine package/size.": "يوجد أكثر من رمز صنف مطابق لهذه الخصائص. حدّد العبوة أو الحجم بدقة أكبر.",
  "Not used": "غير مستخدم",
  "External supplier": "مورد خارجي",
  "MainWarehouse unavailable": "المخزن الرئيسي غير متاح",
  "Select destination": "اختر موقع الاستلام",
  "Select source": "اختر موقع الصرف",
  "No destination": "بدون وجهة",
  "Select receiving/issuing location": "اختر موقع الاستلام أو الصرف",
  "Route is fixed for this operation once chosen.": "لا يمكن تغيير مسار العملية بعد اختياره.",
  "Edit draft": "تعديل المسودة",
  "Update the existing draft without changing its operation type.": "حدّث المسودة الحالية دون تغيير نوع العملية.",
  "Draft edit": "تعديل مسودة",
  "Save draft changes": "حفظ تعديلات المسودة",
  "Revise operation": "مراجعة العملية",
  "Reapply this operation with a required reason. Stock and payment effects are recalculated by the API.": "أعد تطبيق العملية مع إدخال سبب إلزامي. سيعيد الخادم حساب تأثيراتها على المخزون والمدفوعات.",
  "Revision": "مراجعة",
  "Submit revision": "إرسال المراجعة",
  "Draft loaded into the editor.": "تم تحميل المسودة في المحرر.",
  "Operation loaded for revision.": "تم تحميل العملية للمراجعة.",
  "Draft updated.": "تم تحديث المسودة.",
  "Revision reason is required.": "سبب المراجعة مطلوب.",
  "Operation revised.": "تمت مراجعة العملية.",
  "Draft saved.": "تم حفظ المسودة.",
  "Add at least one operation line.": "أضف بندًا واحدًا على الأقل إلى العملية.",
  "Select a SKU for every line.": "اختر رمز صنف لكل بند.",
  "Each SKU can appear once per side. Sales may use one paid line and one bonus line for the same SKU.": "يمكن أن يظهر رمز الصنف مرة واحدة في كل جانب. في المبيعات يمكن إضافة بند مدفوع وبند مجاني للرمز نفسه.",
  "Every pack quantity must be a whole number greater than zero.": "يجب أن تكون كمية كل بند عددًا صحيحًا أكبر من صفر.",
  "MainWarehouse must exist before operations can be created.": "يجب إنشاء المخزن الرئيسي قبل إنشاء العمليات.",
  "Inventory receipt destination must be MainWarehouse.": "يجب أن يكون موقع استلام المخزون هو المخزن الرئيسي.",
  "Warehouse transfer must move packs from MainWarehouse to a non-main destination.": "يجب أن ينقل تحويل المخزون العبوات من المخزن الرئيسي إلى موقع آخر.",
  "Select a source location before choosing stock.": "اختر موقع الصرف قبل اختيار المخزون.",
  "Wholesale sale requires a merchant.": "بيع الجملة يتطلب اختيار تاجر.",
  "Sale line unit price must be greater than zero unless the line is marked as bonus.": "يجب أن يكون سعر الوحدة أكبر من صفر، إلا إذا كان البند مجانيًا.",
  "Select a batch / expiry for every stock-consuming line.": "اختر دفعة وتاريخ صلاحية لكل بند يخصم من المخزون.",
  "Retail installment sales require a registered merchant.": "المبيعات القطاعي بالتقسيط تتطلب اختيار تاجر مسجل.",
  "Reserve requires a representative.": "الحجز للمندوب يتطلب اختيار مندوب.",
  "Return requires a merchant.": "المرتجع يتطلب اختيار تاجر.",
  "Return lines must include batch expiry.": "يجب إدخال تاريخ انتهاء الدفعة في بنود المرتجع.",
  "Change requires a merchant.": "الاستبدال يتطلب اختيار تاجر.",
  "Change needs at least one returned line and one replacement line.": "يجب أن يحتوي الاستبدال على بند مرتجع واحد وبند بديل واحد على الأقل.",
  "Returned change lines must include batch expiry.": "يجب إدخال تاريخ انتهاء الدفعة في البنود المرتجعة ضمن الاستبدال.",
  "Standard": "عادي",
  "Paid": "مدفوع",
  "Revise": "مراجعة",
  "Ship": "شحن",
  "Receive": "استلام",
  "Complete": "إكمال",
  "Working": "جارٍ التنفيذ",
  "Confirmation cancelled. The operation is still a draft.": "تم إلغاء التأكيد، وما زالت العملية مسودة.",
  "Confirm anyway": "تأكيد رغم التحذير",
  "Keep as draft": "الإبقاء كمسودة",
  "This exception will be recorded as a business decision. Check the SKU, lot, and batch expiry before continuing.": "سيُسجل هذا الاستثناء كقرار إداري. راجع رمز الصنف ورقم الدفعة وتاريخ الانتهاء قبل المتابعة.",
  "Requested": "المطلوب",
  "Operation code": "رمز العملية",
  "Created by": "أنشأها",
  "Confirmed by": "اعتمدها",
  "Last edited by": "آخر تعديل بواسطة",
  "Merchant / buyer": "التاجر / العميل",
  "Current version": "الإصدار الحالي",
  "No lines.": "لا توجد بنود.",
  "Allocated SKU": "رمز الصنف المخصّص",
  "No batch allocation snapshot.": "لا توجد لقطة لتخصيص دفعات المخزون.",
  "No versions.": "لا توجد إصدارات سابقة.",
  "pack(s)": "عبوة",
  "ChangeIn": "البديل",
  "ChangeOut": "المرتجع",
  "InventoryReceipt": "استلام مخزون",
  "WarehouseTransfer": "تحويل مخزون",
  "WholesaleSale": "بيع جملة",
  "RetailSale": "بيع قطاعي / أونلاين",
  "Reserve": "حجز للمندوب",
  "WriteOff": "إعدام / تسوية مخزون",
  "CashHandToHand": "نقدي مباشر",
  "CashTransaction": "تحويل أو إيداع نقدي",
  "Remove": "حذف",
  "Draft edit mode": "وضع تعديل المسودة",
  "Revision mode": "وضع مراجعة العملية",
  "Control installment and cash confirmation queues, then review the full payment history without losing the operational trail.": "إدارة قوائم اعتماد الأقساط والحركات النقدية، ثم مراجعة سجل المدفوعات الكامل مع الحفاظ على أثر كل عملية.",
  "Assign to accountant...": "إسناد إلى محاسب...",
  "Buyer": "العميل",
  "Method": "الطريقة",
  "Remaining": "المتبقي",
  "Loading payments": "جارٍ تحميل المدفوعات",
  "Review every payment-related record created across the system, including opening logs, installment actions, cash records, approvals, refunds, and financial adjustments.": "راجع جميع سجلات المدفوعات في النظام، بما يشمل فتح السجلات والأقساط والحركات النقدية والاعتمادات والاستردادات والتسويات المالية.",
  "Loading history": "جارٍ تحميل السجل",
  "Draft payment entry": "إضافة حركة دفع كمسودة",
  "Cash / refund record": "حركة نقدية / استرداد",
  "Use for return/change outcomes that become merchant credit, remaining reduction, or cash refund.": "استخدم هذا القسم لنتائج المرتجع أو الاستبدال التي تتحول إلى رصيد دائن للتاجر أو تخفيض للمتبقي أو استرداد نقدي.",
  "Operation ID": "معرّف العملية",
  "Merchant remaining": "المتبقي على التاجر",
  "Use": "الاستخدام",
  "By": "بواسطة",
  "Approve cash": "اعتماد النقدية",
  "Initialized by": "بدأه",
  "Assigned to": "مسند إلى",
  "Last modified by": "آخر تعديل بواسطة",
  "Stage": "المرحلة",
  "Date": "التاريخ",
  "Drafted": "أُنشئت كمسودة",
  "Decision": "القرار",
  "No sub-logs yet.": "لا توجد سجلات فرعية حتى الآن.",
  "Cash record": "السجل النقدي",
  "Adjustment": "التسوية",
  "Payment sub-log drafted.": "تم حفظ حركة الدفع كمسودة.",
  "Payment approved.": "تم اعتماد الدفع.",
  "Cash receipt approved.": "تم اعتماد التحصيل النقدي.",
  "Reject Payment Entry": "رفض حركة الدفع",
  "Record the reason. Rejected entries remain visible in the log.": "سجّل سبب الرفض. ستظل الحركات المرفوضة ظاهرة في السجل.",
  "Payment rejected.": "تم رفض حركة الدفع.",
  "Select an accountant before assigning the payment log.": "اختر محاسبًا قبل إسناد سجل الدفع.",
  "Payment log moved to accountant queue.": "تم نقل سجل الدفع إلى قائمة المحاسب.",
  "Merchant and positive amount are required.": "يجب اختيار تاجر وإدخال مبلغ أكبر من صفر.",
  "Cash refund adjustments must reference an operation ID.": "يجب ربط تسوية الاسترداد النقدي بمعرّف عملية.",
  "Financial adjustment saved.": "تم حفظ التسوية المالية.",
  "Financial adjustment requested.": "\u062a\u0645 \u0625\u0631\u0633\u0627\u0644 \u0637\u0644\u0628 \u0627\u0644\u062a\u0633\u0648\u064a\u0629 \u0627\u0644\u0645\u0627\u0644\u064a\u0629.",
  "Financial adjustment approved.": "\u062a\u0645 \u0627\u0639\u062a\u0645\u0627\u062f \u0627\u0644\u062a\u0633\u0648\u064a\u0629 \u0627\u0644\u0645\u0627\u0644\u064a\u0629.",
  "Financial adjustment rejected.": "\u062a\u0645 \u0631\u0641\u0636 \u0627\u0644\u062a\u0633\u0648\u064a\u0629 \u0627\u0644\u0645\u0627\u0644\u064a\u0629.",
  "Reject Financial Adjustment": "\u0631\u0641\u0636 \u0627\u0644\u062a\u0633\u0648\u064a\u0629 \u0627\u0644\u0645\u0627\u0644\u064a\u0629",
  "Record the reason. Rejected adjustments remain visible in the log.": "\u0633\u062c\u0644 \u0633\u0628\u0628 \u0627\u0644\u0631\u0641\u0636. \u062a\u0628\u0642\u0649 \u0627\u0644\u062a\u0633\u0648\u064a\u0627\u062a \u0627\u0644\u0645\u0631\u0641\u0648\u0636\u0629 \u0638\u0627\u0647\u0631\u0629 \u0641\u064a \u0627\u0644\u0633\u062c\u0644.",
  "Cash record saved.": "تم حفظ الحركة النقدية.",
  "Loaded": "تم التحميل",
  "PendingAdmin": "بانتظار الإدارة",
  "PendingAccountant": "بانتظار المحاسب",
  "CashReceived": "تحصيل نقدي",
  "CashRefund": "استرداد نقدي",
  "MerchantCredit": "رصيد دائن للتاجر",
  "BalanceReduction": "تخفيض المتبقي على التاجر",
  "Required for cash refund": "مطلوب عند الاسترداد النقدي",
  "Download operational, inventory, payment, and statement outputs in CSV and PDF formats.": "نزّل تقارير العمليات والمخزون والمدفوعات وكشوف الحساب بصيغ CSV وPDF.",
  "CSV": "CSV",
  "Sales": "المبيعات",
  "Net collected": "صافي التحصيل",
  "Returns / adjustments": "المرتجعات / التسويات",
  "Loading operations": "جارٍ تحميل العمليات",
  "Cash receive receipt": "إيصال تحصيل نقدي",
  "Loading cash payments": "جارٍ تحميل المدفوعات النقدية",
  "Download cash receipt": "تنزيل إيصال التحصيل النقدي",
  "Loading merchants": "جارٍ تحميل التجار",
  "Loading stocktakes": "جارٍ تحميل جلسات الجرد",
  "No rows available": "لا توجد سجلات متاحة",
  "Select...": "اختر...",
  "Report": "التقرير",
  "Requested by": "طلبه",
  "Count physical stock by SKU, lot, and expiry, then confirm reconciliations through the ledger.": "سجّل الجرد الفعلي حسب رمز الصنف ورقم الدفعة وتاريخ الانتهاء، ثم اعتمد فروق الجرد في سجل المخزون.",
  "Read-only stocktake review.": "عرض جلسات الجرد دون تعديل.",
  "Session": "الجلسة",
  "Select a session to enter counts or review discrepancies.": "اختر جلسة لإدخال الكميات الفعلية أو مراجعة فروق الجرد.",
  "No stocktake sessions yet.": "لا توجد جلسات جرد حتى الآن.",
  "Confirm adjustments": "اعتماد فروق الجرد",
  "System": "النظام",
  "Physical": "الفعلي",
  "Delta": "الفرق",
  "Note": "ملاحظة",
  "No counted lines yet.": "لا توجد بنود معدودة حتى الآن.",
  "Save counts": "حفظ الكميات",
  "Lot number": "رقم الدفعة",
  "Physical count": "الكمية الفعلية",
  "Location is required.": "الموقع مطلوب.",
  "Stocktake session opened.": "تم فتح جلسة الجرد.",
  "Every stocktake line needs a SKU and non-negative whole-number count.": "يجب اختيار رمز صنف لكل بند وإدخال كمية فعلية صحيحة لا تقل عن صفر.",
  "Stocktake counts saved.": "تم حفظ كميات الجرد.",
  "Stocktake confirmed and ledger adjustments posted.": "تم اعتماد الجرد وتسجيل التسويات في سجل المخزون.",
  "Review alerts, workflow updates, targets, and linked records without losing context.": "راجع التنبيهات وتحديثات سير العمل والمستهدفات والسجلات المرتبطة دون فقدان سياقها.",
  "Visible": "الظاهرة",
  "Unread only": "غير المقروء فقط",
  "Previous": "السابق",
  "Next": "التالي",
  "Page 1 of 1": "الصفحة 1 من 1",
  "Manual alert triggers": "تشغيل التنبيهات يدويًا",
  "Run alert scans on demand when you want to refresh operational warnings immediately.": "شغّل فحص التنبيهات يدويًا لتحديث التحذيرات التشغيلية فورًا.",
  "Unresolved reserves": "حجوزات غير محسومة",
  "No notifications match the current filters.": "لا توجد تنبيهات تطابق عوامل التصفية الحالية.",
  "Broadcast": "إرسال عام",
  "Open inventory": "فتح المخزون",
  "Open payments": "فتح المدفوعات",
  "Open operations": "فتح العمليات",
  "Open stocktakes": "فتح الجرد",
  "Open CRM": "فتح إدارة العلاقات التجارية",
  "Open reports": "فتح التقارير",
  "Open related page": "فتح الصفحة المرتبطة",
  "Payment workflow": "سير عمل المدفوعات",
  "Operation status": "حالة العملية",
  "Stocktake confirmed": "تم اعتماد الجرد",
  "Notification": "تنبيه",
  "Channel": "القناة",
  "Event location": "موقع الحدث",
  "Review active accounts, assigned locations, and password resets from one controlled admin surface.": "راجع الحسابات النشطة والمواقع المعيّنة وعمليات إعادة تعيين كلمات المرور من شاشة إدارية واحدة.",
  "Review employee accounts, assigned locations, and controlled access from one admin surface.": "راجع حسابات الموظفين والمواقع المعيّنة والتحكم في الوصول من شاشة إدارية واحدة.",
  "Create employee account": "إنشاء حساب موظف",
  "Set the employee's sign-in name, temporary password, role, and warehouse scope.": "حدّد اسم دخول الموظف وكلمة المرور المؤقتة والدور ونطاق المخزن.",
  "Full name": "الاسم الكامل",
  "Administrator": "مدير النظام",
  "ERP administrator": "مدير ERP",
  "Warehouse clerk": "موظف المخزن",
  "Warehouse location": "موقع المخزن",
  "Loading locations...": "جارٍ تحميل المواقع...",
  "Select warehouse location": "اختر موقع المخزن",
  "Temporary password": "كلمة المرور المؤقتة",
  "Full name and username are required.": "الاسم الكامل واسم المستخدم مطلوبان.",
  "Warehouse clerks must be assigned to a warehouse location.": "يجب تعيين موظفي المخزن إلى موقع مخزن.",
  "Confirm password": "تأكيد كلمة المرور",
  "Reset password": "إعادة تعيين كلمة المرور",
  "Password must be at least 8 characters.": "يجب ألا تقل كلمة المرور عن 8 أحرف.",
  "Password confirmation does not match.": "تأكيد كلمة المرور غير مطابق.",
  "Active sessions were revoked.": "تم إنهاء الجلسات النشطة.",
  "Failed": "فشل التحميل",
  "Update": "تحديث",
  "Lens": "عدسات",
  "Solution": "محلول",
  "SinglePiece": "قطعة منفردة",
  "SealedPackOnly": "عبوة مغلقة فقط",
  "Batch": "دفعة مخزون",
  "Daily": "يومي",
  "Monthly": "شهري",
  "Annually": "سنوي",
  "Unknown location": "موقع غير معروف",
  "8+ characters": "8 أحرف على الأقل",
  "Repeat": "أعد إدخال كلمة المرور",
  "Alert run": "تشغيل التنبيه",
  "Batch history": "سجل الدفعات",
  "Merchant Batch History": "سجل دفعات التاجر",
  "Batch history and notes": "سجل الدفعات والملاحظات",
  "Recorded sales and confirmed returns": "المبيعات المسجلة والمرتجعات المؤكدة",
  "Recorded sales and confirmed returns by SKU, lot, and expiry": "المبيعات المسجلة والمرتجعات المؤكدة حسب رمز الصنف والدفعة والانتهاء",
  "No merchant batch history yet.": "لا يوجد سجل دفعات لهذا التاجر حتى الآن.",
  "Merchant batch history loaded.": "تم تحميل سجل دفعات التاجر.",
  "Expiry status": "حالة الصلاحية",
  "Merchant expiry recalls": "استدعاءات دفعات التجار لقرب الانتهاء",
  "Start Return": "بدء مرتجع",
  "No Stock at Merchant": "لا يوجد مخزون لدى التاجر",
  "Physical quantity": "الكمية الفعلية",
  "Receiving location": "موقع الاستلام",
  "Approaching expiry": "يقترب من الانتهاء",
  "Read only": "للقراءة فقط",
  "Daily scan active": "الفحص اليومي مفعّل",
  "Sold merchant batches inside the configured expiry window, ordered by earliest expiry.": "دفعات مباعة للتجار داخل نافذة قرب الانتهاء المحددة، مرتبة حسب الأقرب انتهاءً.",
  "Global expiry window (months)": "نافذة قرب الانتهاء العامة (بالأشهر)",
  "Save recall settings": "حفظ إعدادات الاستدعاء",
  "No active merchant expiry recalls.": "لا توجد استدعاءات نشطة لدفعات التجار.",
  "Start merchant return": "بدء مرتجع من التاجر",
  "Select a location": "اختر موقعًا",
  "Create return draft": "إنشاء مسودة مرتجع",
  "Recorded sales warning": "تحذير بخصوص المبيعات المسجلة",
  "One or more returned batch quantities are above the recorded sales balance. Review the batch facts before continuing.": "كمية مرتجع تشغيلة واحدة أو أكثر أكبر من رصيد المبيعات المسجل. راجع بيانات التشغيلات قبل المتابعة.",
  "Sold to merchant": "المباع للتاجر",
  "Already returned": "المرتجع سابقًا",
  "Requested now": "المطلوب الآن",
  "Above recorded balance": "الزيادة عن الرصيد المسجل",
  "Exception reason": "سبب الاستثناء",
  "Explain why this return should continue": "وضّح سبب متابعة هذا المرتجع",
  "Confirm with exception": "تأكيد مع استثناء",
  "Close": "إغلاق",
  "This account can review the warning but cannot bypass it.": "يمكن لهذا الحساب مراجعة التحذير، لكن لا يمكنه تجاوزه.",
  "Exception reason is required.": "سبب الاستثناء مطلوب.",
  "Explain how the physical stock was checked.": "اشرح كيفية التحقق من المخزون الفعلي لدى التاجر.",
  "Merchant recall closed as no stock.": "تم إغلاق الاستدعاء لعدم وجود مخزون لدى التاجر.",
  "Merchant recall settings saved.": "تم حفظ إعدادات استدعاء دفعات التجار.",
  "Merchant expiry recall": "استدعاء دفعة تاجر لقرب الانتهاء",
  "LowStock": "مخزون منخفض",
  "UnresolvedReserves": "حجوزات غير محسومة",
  "PaymentWorkflow": "سير عمل المدفوعات",
  "OperationStatus": "حالة العملية",
  "StocktakeConfirmed": "تم اعتماد الجرد",
  "PaymentLogOpened": "تم فتح سجل دفع",
  "PaymentAssigned": "تم الإسناد إلى المحاسب",
  "InstallmentDrafted": "تم تسجيل قسط كمسودة",
  "InstallmentApproved": "تم اعتماد القسط",
  "InstallmentRejected": "تم رفض القسط",
  "CashReceiptRecorded": "تم تسجيل تحصيل نقدي",
  "CashReceiptApproved": "تم اعتماد التحصيل النقدي",
  "CashRefundRecorded": "تم تسجيل الاسترداد النقدي",
  "Primary": "التنقل الرئيسي",
  "Lensee dashboard": "لوحة تحكم Lensee",
  "Blank if none": "اتركه فارغًا إذا لم يوجد",
  "Notification type": "نوع التنبيه",
  "Product, color, power, SKU": "المنتج أو اللون أو الدرجة أو رمز الصنف",
  "Remove line": "حذف البند",
  "Can edit catalog": "يمكنه تعديل الكتالوج",
  "View only": "عرض فقط",
  "Product scope": "نطاق المنتجات",
  "Products and SKUs": "المنتجات ورموز الأصناف",
  "Reference data": "البيانات المرجعية",
  "Categories and brands": "التصنيفات والعلامات التجارية",
  "Not set": "غير محدد",
  "Can adjust targets": "يمكنه تعديل المستهدفات",
  "Assigned location": "الموقع المعيّن",
  "Ledger model": "نظام السجل",
  "Append-only stock history": "سجل مخزون غير قابل للحذف أو التعديل",
  "Blocked": "محظور",
  "Can edit CRM records": "يمكنه تعديل بيانات العلاقات التجارية",
  "Merchant context": "بيانات التاجر",
  "Operations link": "الربط بالعمليات",
  "Shared across workflows": "مشترك بين مسارات العمل",
  "No lot": "بدون رقم دفعة",
  "Unknown buyer": "عميل غير معروف",
  "Can create operations": "يمكنه إنشاء العمليات",
  "Can revise operations": "يمكنه مراجعة العمليات",
  "Operation scope": "نطاق العمليات",
  "Draft and confirmed lifecycle": "دورة المسودة والتأكيد",
  "Payment controls": "ضوابط المدفوعات",
  "Admin approval workflow": "مسار اعتماد الإدارة",
  "Reporting scope": "نطاق التقارير",
  "CSV and PDF outputs": "مخرجات CSV وPDF",
  "Can manage stocktakes": "يمكنه إدارة الجرد",
  "Can review stocktakes": "يمكنه مراجعة الجرد",
  "Alert scope": "نطاق التنبيهات",
  "Role and location aware": "بحسب الدور والموقع",
  "Open confirmations": "الاعتمادات المعلقة",
  "Main warehouse": "المخزن الرئيسي",
  "Operations queue": "قائمة العمليات",
  "Create drafts, confirm movement, and inspect history.": "أنشئ المسودات، وأكد حركة المخزون، وراجع السجل.",
  "Create the operational draft on the rail, resolve stock lines in the workspace, then move the queue through confirmation and fulfillment.": "أنشئ مسودة العملية من المسار الجانبي، ثم حدد بنود المخزون داخل مساحة العمل، وبعدها حرّك قائمة الانتظار عبر التأكيد والتنفيذ.",
  "Check balances, batches, targets, and replenishment.": "راجع الأرصدة والدفعات والمستهدفات وإعادة التوريد.",
  "Download operational evidence and review totals.": "نزّل مستندات التشغيل وراجع الإجماليات.",
  "Product totals": "إجماليات المنتجات",
  "Loading product totals": "جارٍ تحميل إجماليات المنتجات",
  "SKU count": "عدد رموز الأصناف",
  "Total packs": "إجمالي العبوات",
  "Total pieces": "إجمالي القطع",
  "Breakdown": "التفصيل",
  "Rate": "المعدل",
  "No validity breakdown": "لا يوجد تفصيل للصلاحية",
  "No available stock for this location.": "لا يوجد مخزون متاح لهذا الموقع.",
  "Search stock first or choose product attributes to resolve the SKU.": "ابحث في المخزون أولًا أو اختر خصائص المنتج لتحديد رمز الصنف.",
  "Active operations stay compact here. Use Details to inspect versions, stock movement, and documents.": "تبقى العمليات النشطة مختصرة هنا. استخدم التفاصيل لمراجعة الإصدارات وحركة المخزون والمستندات.",
  "Assign, use, approve, and audit payment records.": "أسند واستخدم واعتمد وراجع سجلات الدفع.",
  "Open installment and cash confirmations that still need assignment, accountant action, or admin approval.": "الأقساط والحركات النقدية المفتوحة التي تحتاج إلى إسناد أو إجراء محاسب أو اعتماد إدارة.",
  "One row per payment with stages, sub-logs, cash records, refunds, and adjustments inside expanded detail.": "صف واحد لكل مدفوعة مع المراحل والسجلات الفرعية والحركات النقدية والاستردادات والتسويات داخل التفاصيل الموسعة.",
  "Stages": "المراحل",
  "No payment confirmations are waiting.": "لا توجد تأكيدات دفع في الانتظار.",
  "Use": "استخدام",
  "By": "بواسطة",
  "Healthy": "سليم",
  "OK": "سليم",
  "Inactive SKU": "رمز صنف غير نشط",
  "No expiry": "بدون تاريخ انتهاء",
  "Try another color, power, package, or source location.": "جرّب لونًا أو درجة أو عبوة أو موقع صرف آخر.",
  "SKUs match these attributes. Refine package/size.": "توجد رموز أصناف مطابقة لهذه الخصائص. حدّد العبوة أو المقاس بدقة.",
  "Actual total I have": "الإجمالي الفعلي لدي",
  "Batch expiry is required for products with batch expiry tracking.": "تاريخ انتهاء الدفعة مطلوب للمنتجات التي تعتمد تتبع انتهاء الدفعات.",
  "External": "خارجي",
  "Cash receive receipt": "إيصال تحصيل نقدي",
  "Download cash receipt": "تنزيل إيصال التحصيل النقدي",
  "Loading operations": "جارٍ تحميل العمليات",
  "Loading payments": "جارٍ تحميل المدفوعات",
  "Loading cash payments": "جارٍ تحميل التحصيلات النقدية",
  "Loading merchants": "جارٍ تحميل التجار",
  "Loading replenishment": "جارٍ تحميل إعادة التوريد",
  "Loading expired batches": "جارٍ تحميل الدفعات المنتهية",
  "Loading batches": "جارٍ تحميل الدفعات",
  "Loading transactions": "جارٍ تحميل الحركات",
  "No products found": "لم يتم العثور على منتجات",
  "No stock balances yet.": "لا توجد أرصدة مخزون بعد.",
  "No transactions yet.": "لا توجد حركات بعد.",
  "No batches yet.": "لا توجد دفعات بعد.",
  "No expired batches.": "لا توجد دفعات منتهية.",
  "No target-stock rows yet.": "لا توجد مستهدفات مخزون بعد.",
  "No stocktake sessions yet.": "لا توجد جلسات جرد بعد.",
  "Select product attributes to resolve SKU.": "اختر خصائص المنتج لتحديد رمز الصنف.",
  "Select product, power, and color to resolve SKU.": "اختر المنتج والدرجة واللون لتحديد رمز الصنف.",
  "No locations": "لا توجد مواقع",
  "Stock": "المخزون",
  "Imported shipments, landed costs, and receipts.": "الشحنات المستوردة، تكلفة الوصول، وإيصالات المخزون.",
  "Imported shipments": "الشحنات المستوردة",
  "Shipments": "الشحنات",
  "Shipment": "الشحنة",
  "Draft value": "قيمة المسودات",
  "Ready to confirm": "جاهزة للتأكيد",
  "Access": "الصلاحية",
  "Read only": "قراءة فقط",
  "Register imported shipments, allocate customs and import costs, then post controlled inventory receipts.": "سجل الشحنات المستوردة، وزع الجمارك ومصاريف الاستيراد، ثم أنشئ إيصالات مخزون مضبوطة.",
  "Search by shipment, supplier, or invoice.": "ابحث برقم الشحنة أو المورد أو الفاتورة.",
  "Search shipments": "بحث في الشحنات",
  "All statuses": "كل الحالات",
  "Draft": "مسودة",
  "Received": "تم الاستلام",
  "Cancelled": "ملغاة",
  "Supplier": "المورد",
  "Status": "الحالة",
  "Total": "الإجمالي",
  "No invoice": "بدون فاتورة",
  "costs": "تكاليف",
  "Shipment detail": "تفاصيل الشحنة",
  "Select a shipment to review lines, cost allocation, receipt operation, and history.": "اختر شحنة لمراجعة البنود، توزيع التكلفة، عملية إيصال المخزون، وسجل الحركة.",
  "Supply receipt": "إيصال توريد",
  "Register incoming shipment": "تسجيل شحنة واردة",
  "Enter supplier, SKU lines, and costs in one document, then save the draft before confirming receipt.": "أدخل بيانات المورد والبنود والتكاليف في نموذج واحد، ثم احفظ المسودة قبل تأكيد الاستلام.",
  "New shipment": "شحنة جديدة",
  "Shipment data": "بيانات الشحنة",
  "Receipt draft": "مسودة استلام",
  "Invoice number": "رقم الفاتورة",
  "Shipment date": "تاريخ الشحنة",
  "Destination warehouse": "مخزن الوصول",
  "Notes": "ملاحظات",
  "SKU lines": "بنود SKU",
  "Prices can stay blank while drafting and must be completed before confirmation.": "يمكن ترك السعر فارغا في المسودة، ويجب إكماله قبل التأكيد.",
  "Add line": "إضافة بند",
  "Import cost breakdown": "تفصيل تكاليف الاستيراد",
  "Add cost": "إضافة تكلفة",
  "Product subtotal": "إجمالي المنتجات",
  "Import costs": "تكاليف الاستيراد",
  "Landed total": "الإجمالي بعد التكلفة",
  "Confirmation readiness": "جاهزية التأكيد",
  "Incomplete prices": "أسعار ناقصة",
  "Find SKU": "بحث SKU",
  "Product, color, power, SKU code": "المنتج، اللون، القوة، كود SKU",
  "Search and select a SKU.": "ابحث واختر SKU.",
  "Quantity": "الكمية",
  "Unit price": "سعر الوحدة",
  "Draft blank": "فارغ في المسودة",
  "Required before confirmation.": "مطلوب قبل التأكيد.",
  "Lot": "التشغيلة",
  "Expiry": "الصلاحية",
  "Line notes": "ملاحظات البند",
  "Remove line": "حذف البند",
  "Price must be greater than zero.": "السعر يجب أن يكون أكبر من صفر.",
  "Selected SKU": "SKU محدد",
  "Unknown SKU": "SKU غير معروف",
  "Cost type": "نوع التكلفة",
  "Customs": "جمارك",
  "Freight": "شحن",
  "Clearance": "تخليص",
  "Handling": "مناولة",
  "Insurance": "تأمين",
  "Other": "أخرى",
  "Description": "الوصف",
  "Amount": "المبلغ",
  "Remove cost": "حذف التكلفة",
  "Loading shipments...": "جار تحميل الشحنات...",
  "No supply shipments match the current filters.": "لا توجد شحنات مطابقة للفلاتر الحالية.",
  "Failed": "فشل التحميل",
  "Loading shipment...": "جار تحميل الشحنة...",
  "Confirm receipt": "تأكيد الاستلام",
  "Print receipt": "طباعة الإيصال",
  "Products": "المنتجات",
  "Readiness": "جاهزية التأكيد",
  "Inventory receipt operation": "عملية إيصال المخزون",
  "Lines": "البنود",
  "Qty": "الكمية",
  "Unit": "الوحدة",
  "Line": "البند",
  "Allocated": "الموزع",
  "Landed unit": "تكلفة الوحدة النهائية",
  "Batch": "التشغيلة",
  "Blank": "فارغ",
  "Cost breakdown": "تفصيل التكاليف",
  "Type": "النوع",
  "No costs.": "لا توجد تكاليف.",
  "History": "السجل",
  "Time": "الوقت",
  "Summary": "الملخص",
  "No history.": "لا يوجد سجل حتى الآن.",
  "Supply shipment saved.": "تم حفظ شحنة التوريد.",
  "Supply shipment received into inventory.": "تم استلام الشحنة في المخزون.",
  "Supply shipment cancelled.": "تم إلغاء شحنة التوريد.",
  "Review these values": "راجع هذه القيم",
  "Supplier is required.": "اسم المورد مطلوب.",
  "Destination warehouse is required.": "مخزن الوصول مطلوب.",
  "At least one SKU line is required.": "يجب إضافة بند SKU واحد على الأقل.",
  "Invalid prices": "أسعار غير صحيحة",
  "Ready": "جاهزة",
  "No lines": "لا توجد بنود",
  "Only draft shipments can be confirmed.": "يمكن تأكيد الشحنات المسودة فقط.",
  "Every SKU price must be greater than zero before confirmation.": "كل أسعار SKU يجب أن تكون أكبر من صفر قبل التأكيد.",
  "Every SKU line needs a unit price before confirmation.": "كل بند SKU يحتاج سعر وحدة قبل التأكيد.",
  "Ready to confirm.": "جاهزة للتأكيد.",
  "pieces not set": "عدد القطع غير محدد",
  "Account": "الحساب",
  "Activity": "النشاط",
  "Add warehouse": "إضافة مخزن",
  "All active SKUs": "كل رموز الأصناف النشطة",
  "All areas": "كل الأقسام",
  "All catalog products": "كل منتجات الكتالوج",
  "All states": "كل الحالات",
  "All wear cycles": "كل دورات الاستخدام",
  "Allocation": "التخصيص",
  "Allocation pending": "التخصيص معلّق",
  "Annual": "سنوي",
  "Arabic": "العربية",
  "Area": "القسم",
  "Attempts": "المحاولات",
  "Audit history": "سجل التدقيق",
  "Batch allocation pending": "تخصيص الدفعات معلّق",
  "Buyer contact": "بيانات تواصل العميل",
  "Check catalog": "فحص الكتالوج",
  "Copy SKU": "نسخ رمز الصنف",
  "Copy the ERP SKU into each Shopify variant. Orders match SKU only; each quantity is an individual lens piece.": "انسخ رمز صنف ERP إلى حقل SKU لكل متغير في Shopify. تتم مطابقة الطلبات باستخدام SKU فقط، وتمثل كل كمية قطعة عدسة منفردة.",
  "Could not copy the SKU. Copy it manually from the table.": "تعذر نسخ رمز الصنف. انسخه يدويًا من الجدول.",
  "Could not load audit history.": "تعذر تحميل سجل التدقيق.",
  "CSV / PDF": "CSV / PDF",
  "Delete": "حذف",
  "Delivery queue": "قائمة استلام الطلبات",
  "Employee accounts": "حسابات الموظفين",
  "Enable piece sales": "تفعيل البيع بالقطعة",
  "ERP SKU": "رمز صنف ERP",
  "ERP SKU copied. Paste it into the Shopify variant SKU field.": "تم نسخ رمز صنف ERP. الصقه في حقل SKU لمتغير Shopify.",
  "ERP SKUs for Shopify": "رموز أصناف ERP لـ Shopify",
  "ERP SKUs per page": "رموز أصناف ERP في الصفحة",
  "Event detail": "تفاصيل الحدث",
  "Export language": "لغة التصدير",
  "Find activity": "البحث في النشاط",
  "Find SKU or product": "ابحث عن رمز صنف أو منتج",
  "From": "من",
  "Full name & role": "الاسم الكامل والدور",
  "Generate weekly open-payment summary": "إنشاء ملخص أسبوعي للمدفوعات المفتوحة",
  "Imported": "تم الاستيراد",
  "Integration events": "أحداث التكامل",
  "Landed": "تكلفة الوصول",
  "Lens products only": "منتجات العدسات فقط",
  "Loading audit history": "جار تحميل سجل التدقيق",
  "Loading ERP SKU readiness…": "جار تحميل جاهزية رموز أصناف ERP…",
  "Loading event": "جار تحميل الحدث",
  "Loading integration events…": "جار تحميل أحداث التكامل…",
  "Loading recalls": "جار تحميل طلبات الاسترجاع",
  "Make primary": "تعيين كمسؤول رئيسي",
  "Needs review": "يحتاج مراجعة",
  "Needs setup": "يحتاج إعدادًا",
  "No active ERP SKUs match this view.": "لا توجد رموز أصناف ERP نشطة تطابق هذا العرض.",
  "No audit events match these filters.": "لا توجد أحداث تدقيق تطابق عوامل التصفية.",
  "No changes detected; operation was not revised.": "لم تُكتشف تغييرات؛ لم يتم تعديل العملية.",
  "No individual field values were saved for this event.": "لم تُحفظ قيم حقول منفردة لهذا الحدث.",
  "No matching records.": "لا توجد سجلات مطابقة.",
  "No Shopify events match this view.": "لا توجد أحداث Shopify تطابق هذا العرض.",
  "No supply shipments.": "لا توجد شحنات توريد.",
  "Not applicable": "غير منطبق",
  "Online and retail targets are topped up from MainWarehouse through Draft warehouse transfers awaiting confirmation.": "تُستكمل أرصدة قنوات البيع الإلكتروني والتجزئة من المخزن الرئيسي عبر تحويلات مخزنية مسودة تنتظر التأكيد.",
  "Online intake": "استلام الطلبات الإلكترونية",
  "Only the primary Administrator can add an active warehouse location.": "يمكن للمسؤول الرئيسي فقط إضافة موقع مخزن نشط.",
  "Open record": "فتح السجل",
  "Open related record": "فتح السجل المرتبط",
  "Order": "الطلب",
  "Payload": "بيانات الحدث",
  "Performed by": "نفّذه",
  "Person, record name, action, or saved value": "شخص أو اسم سجل أو إجراء أو قيمة محفوظة",
  "Piece sale disabled": "البيع بالقطعة معطّل",
  "Primary Admin": "المسؤول الرئيسي",
  "Print": "طباعة",
  "Processing": "قيد المعالجة",
  "Product / attributes": "المنتج / الخصائص",
  "Product list unavailable": "قائمة المنتجات غير متاحة",
  "Protect the commercial record. Allocate stock only after review.": "احمِ السجل التجاري. لا تخصّص المخزون إلا بعد المراجعة.",
  "Protected": "محمي",
  "Queued": "في قائمة الانتظار",
  "Queued events process automatically. Exceptions require a deliberate retry or resolution note.": "تُعالج الأحداث المنتظرة تلقائيًا. تتطلب الحالات الاستثنائية إعادة محاولة مقصودة أو ملاحظة تسوية.",
  "Ready to publish": "جاهز للنشر",
  "Receipt": "إيصال الاستلام",
  "Record": "السجل",
  "Recorded activity": "النشاط المسجّل",
  "Refresh intake": "تحديث قائمة الاستلام",
  "Resolution": "التسوية",
  "Resolution note": "ملاحظة التسوية",
  "Resolve": "تسوية",
  "Resolved": "تمت التسوية",
  "Retry": "إعادة المحاولة",
  "Retrying": "تجري إعادة المحاولة",
  "Rows per page": "صفوف الصفحة",
  "Saved values": "القيم المحفوظة",
  "Search product, color, power, or SKU": "ابحث بالمنتج أو اللون أو القوة أو رمز الصنف",
  "Select a batch and expiry for every Shopify line.": "اختر دفعة وتاريخ صلاحية لكل بند Shopify.",
  "Select a receiving location and enter a positive whole quantity.": "اختر موقع الاستلام وأدخل كمية صحيحة موجبة.",
  "Select an event to inspect the recorded details.": "اختر حدثًا لمراجعة التفاصيل المسجّلة.",
  "Set Lens cycle": "تحديد دورة العدسة",
  "Shipping address": "عنوان الشحن",
  "Shopify batch allocation saved.": "تم حفظ تخصيص دفعات Shopify.",
  "Shopify event queued for retry.": "تمت إضافة حدث Shopify إلى قائمة إعادة المحاولة.",
  "Shopify event resolved.": "تمت تسوية حدث Shopify.",
  "Shopify intake desk": "مكتب استلام Shopify",
  "Shopify line": "بند Shopify",
  "Showing 0 ERP SKUs": "عرض 0 من رموز أصناف ERP",
  "SKU / product": "رمز الصنف / المنتج",
  "SKU or product name": "رمز الصنف أو اسم المنتج",
  "Store": "المتجر",
  "Sub-warehouse": "مخزن فرعي",
  "Succeeded": "نجح",
  "Supply landed cost": "تكلفة التوريد عند الوصول",
  "System activity": "نشاط النظام",
  "The related record is unavailable or no longer permitted.": "السجل المرتبط غير متاح أو لم يعد مسموحًا بعرضه.",
  "The trail remains available even when the original account or record has been removed.": "يبقى السجل متاحًا حتى عند حذف الحساب أو السجل الأصلي.",
  "There can be only one active Main warehouse.": "لا يمكن أن يوجد سوى مخزن رئيسي نشط واحد.",
  "These are the values recorded when the activity was completed.": "هذه هي القيم المسجّلة عند اكتمال النشاط.",
  "This secure link could not be created.": "تعذر إنشاء هذا الرابط الآمن.",
  "This secure link is unavailable or has expired.": "هذا الرابط الآمن غير متاح أو انتهت صلاحيته.",
  "To": "إلى",
  "Trust": "الموثوقية",
  "Unavailable": "غير متاح",
  "Unsupported product": "منتج غير مدعوم",
  "Username / full name": "اسم المستخدم / الاسم الكامل",
  "View details": "عرض التفاصيل",
  "Warehouse name": "اسم المخزن",
  "Warehouse name is required.": "اسم المخزن مطلوب.",
  "Wear cycle": "دورة الاستخدام",
  "Webhook content is never shown here. Temporary legacy-path deliveries are explicitly marked until you upgrade to signed webhooks.": "لا يُعرض محتوى Webhook هنا مطلقًا. تُميّز عمليات الاستلام المؤقتة عبر المسار القديم بوضوح حتى الترقية إلى Webhooks موقّعة.",
  "Was": "كان",
  "Now": "أصبح",
  "Saved": "محفوظ",
  "Cleared": "تم المسح",
  "Role not recorded": "الدور غير مسجّل",
  "Signed receiver ready": "المستقبِل الموقّع جاهز",
  "Temporary legacy receiver": "مستقبِل مؤقت عبر المسار القديم",
  "Configuration required": "الإعداد مطلوب",
  "Signed HMAC": "توقيع HMAC صالح",
  "Temporary legacy path": "مسار قديم مؤقت",
  "Delivery accepted for processing.": "تم قبول الطلب للمعالجة.",
  "Not parsed": "لم تتم القراءة",
  "Retained securely": "محفوظ بأمان",
  "Retention expired": "انتهت مدة الاحتفاظ",
  "Events": "الأحداث",
  "Receiver": "المستقبِل",
  "Payload access": "الوصول إلى بيانات الحدث",
  "Checking": "جار التحقق",
  "Review successful system activity by person, time, section, and related record.": "راجع نشاط النظام الناجح حسب الشخص والوقت والقسم والسجل المرتبط.",
  "Review online orders, repair mappings, and resolve exceptions before they reach warehouse fulfillment.": "راجع الطلبات الإلكترونية وأصلح الربط وعالج الاستثناءات قبل وصولها إلى تنفيذ المخزن.",
  "No variant attributes": "لا توجد خصائص للمتغير",
  "RequiresAttention": "يحتاج مراجعة",
  "Handle open confirmations first, then use the ledger and tools for audit, entries, cash records, adjustments, and merchant remaining.": "عالج التأكيدات المفتوحة أولًا، ثم استخدم دفتر الحسابات والأدوات للتدقيق والقيود والسجلات النقدية والتسويات والمتبقي على التجار.",
  "Total sales": "إجمالي المبيعات",
  "Was:": "كان:",
  "Now:": "أصبح:",
  "Saved:": "محفوظ:",
  "Updated value": "القيمة المحدّثة",
  "Supplier Name": "اسم المورد",
  "Operation Number": "رقم العملية",
  "Location Id": "معرّف الموقع",
  "Product Id": "معرّف المنتج",
  "Sku Id": "معرّف رمز الصنف",
  "User Id": "معرّف المستخدم",
  "Merchant Id": "معرّف التاجر",
  "Representative Id": "معرّف المندوب",
  "Source Location Id": "معرّف موقع المصدر",
  "Destination Location Id": "معرّف موقع الوجهة",
  "Is Active": "نشط"
});

const translatedTextSources = new WeakMap();
const translatedAttributeSources = new WeakMap();

function translateEnglishText(value, contextElement = null) {
  const leadingWhitespace = value.match(/^\s*/)?.[0] || "";
  const trailingWhitespace = value.match(/\s*$/)?.[0] || "";
  const text = value.slice(leadingWhitespace.length, value.length - trailingWhitespace.length);
  if (!text) return value;

  if (text === "Change" && currentPath() === "/operations") {
    return `${leadingWhitespace}استبدال${trailingWhitespace}`;
  }
  if (text === "Target" && currentPath() === "/notifications") {
    return `${leadingWhitespace}الجهة المستهدفة${trailingWhitespace}`;
  }

  const exact = arabicTranslations[text];
  if (exact) return `${leadingWhitespace}${exact}${trailingWhitespace}`;

  let match = text.match(/^(.+?)\s+-\s+Location scoped$/i);
  if (match) {
    const translatedRole = arabicTranslations[match[1]] || match[1];
    return `${leadingWhitespace}${translatedRole} — مقيّد بالموقع المعيّن${trailingWhitespace}`;
  }

  match = text.match(/^(.+?)\s*->\s*(.+)$/);
  if (match) {
    const source = arabicTranslations[match[1]] || match[1];
    const destination = arabicTranslations[match[2]] || match[2];
    return `${leadingWhitespace}${source} <- ${destination}${trailingWhitespace}`;
  }

  match = text.match(/^Operation\s+(confirm|ship|receive|complete|cancel)\s+completed\.$/i);
  if (match) {
    const actions = {
      confirm: "تأكيد",
      ship: "شحن",
      receive: "استلام",
      complete: "إكمال",
      cancel: "إلغاء"
    };
    return `${leadingWhitespace}تم ${actions[match[1].toLowerCase()]} العملية بنجاح.${trailingWhitespace}`;
  }

  match = text.match(/^Reserved\s+(\d+)\s+replenishment transfer\(s\)\.\s+(\d+)\s+pack\(s\)\s+still uncovered\.(.*)$/i);
  if (match) {
    const alert = match[3] ? ` ${match[3].replace(/^\s*Alert:\s*/i, "تنبيه: ")}` : "";
    return `${leadingWhitespace}تم حجز ${match[1]} تحويل لإعادة التوريد، وما زال ${match[2]} عبوة غير مغطاة.${alert}${trailingWhitespace}`;
  }

  match = text.match(/^Page\s+(\d+)\s+of\s+(\d+)$/i);
  if (match) return `${leadingWhitespace}الصفحة ${match[1]} من ${match[2]}${trailingWhitespace}`;

  match = text.match(/^Showing\s+(\d+)[–-](\d+)\s+of\s+(\d+)\s+ERP SKUs\s+·\s+Page\s+(\d+)\s+of\s+(\d+)$/i);
  if (match) return `${leadingWhitespace}عرض ${match[1]}–${match[2]} من ${match[3]} رمز صنف ERP · الصفحة ${match[4]} من ${match[5]}${trailingWhitespace}`;

  match = text.match(/^Operation\s+(.+)$/i);
  if (match) return `${leadingWhitespace}العملية ${match[1]}${trailingWhitespace}`;

  match = text.match(/^Session\s+(.+)$/i);
  if (match) return `${leadingWhitespace}الجلسة ${match[1]}${trailingWhitespace}`;

  match = text.match(/^(.+?)\s+by\s+(.+)$/i);
  if (match) return `${leadingWhitespace}${match[1]} بواسطة ${match[2]}${trailingWhitespace}`;

  match = text.match(/^Editing\s+(.+)$/i);
  if (match) return `${leadingWhitespace}تعديل ${match[1]}${trailingWhitespace}`;

  match = text.match(/^Edit\s+(.+)$/i);
  if (match) return `${leadingWhitespace}تعديل ${match[1]}${trailingWhitespace}`;

  match = text.match(/^Update\s+(.+)$/i);
  if (match) return `${leadingWhitespace}تحديث ${match[1]}${trailingWhitespace}`;

  match = text.match(/^No\.\s*(.+)$/i);
  if (match) return `${leadingWhitespace}المستخدم ${match[1]}${trailingWhitespace}`;

  match = text.match(/^User\s+(.+)$/i);
  if (match) return `${leadingWhitespace}المستخدم ${match[1]}${trailingWhitespace}`;

  match = text.match(/^By\s+(.+)$/i);
  if (match) return `${leadingWhitespace}بواسطة ${match[1]}${trailingWhitespace}`;

  match = text.match(/^External\s*->\s*(.+)$/i);
  if (match) return `${leadingWhitespace}خارجي ← ${match[1]}${trailingWhitespace}`;

  match = text.match(/^(.+?)\s+remaining$/i);
  if (match) return `${leadingWhitespace}${match[1]} متبقي${trailingWhitespace}`;

  match = text.match(/^(.+?)\s+shortage$/i);
  if (match) return `${leadingWhitespace}${match[1]} عجز${trailingWhitespace}`;

  match = text.match(/^Password changed for (.+)\. Active sessions were revoked\.$/);
  if (match) return `${leadingWhitespace}تم تغيير كلمة المرور للمستخدم ${match[1]} وإنهاء جلساته النشطة.${trailingWhitespace}`;

  match = text.match(/^Employee account created for (.+)\.$/);
  if (match) return `${leadingWhitespace}تم إنشاء حساب الموظف ${match[1]}.${trailingWhitespace}`;

  match = text.match(/^(.+?) was added as an active warehouse\.$/);
  if (match) return `${leadingWhitespace}تمت إضافة ${match[1]} كمخزن نشط.${trailingWhitespace}`;

  match = text.match(/^Delete account (.+)\? This cannot be undone\.$/);
  if (match) return `${leadingWhitespace}حذف حساب ${match[1]}؟ لا يمكن التراجع عن هذا الإجراء.${trailingWhitespace}`;

  match = text.match(/^Account (.+) deleted\.$/);
  if (match) return `${leadingWhitespace}تم حذف حساب ${match[1]}.${trailingWhitespace}`;

  match = text.match(/^(Reactivate|Deactivate) account (.+)\?$/i);
  if (match) {
    const action = match[1].toLowerCase() === "reactivate" ? "إعادة تنشيط" : "إلغاء تنشيط";
    return `${leadingWhitespace}${action} حساب ${match[2]}؟${trailingWhitespace}`;
  }

  match = text.match(/^Created\s+(\d+)\s+Draft replenishment transfer\(s\)\.\s+(\d+)\s+pack\(s\)\s+still uncovered\.(.*)$/i);
  if (match) {
    const alert = match[3] ? ` ${match[3].replace(/^\s*Alert:\s*/i, "تنبيه: ")}` : "";
    return `${leadingWhitespace}تم إنشاء ${match[1]} تحويل مسودة لإعادة التوريد، وما زال ${match[2]} عبوة غير مغطاة.${alert}${trailingWhitespace}`;
  }

  match = text.match(/^(.+?) is now (active|inactive)\.$/i);
  if (match) {
    const status = match[2].toLowerCase() === "active" ? "نشطًا" : "غير نشط";
    return `${leadingWhitespace}أصبح حساب ${match[1]} ${status}.${trailingWhitespace}`;
  }

  match = text.match(/^Make (.+) the primary Administrator\? You will no longer be able to delete Administrator accounts\.$/);
  if (match) return `${leadingWhitespace}تعيين ${match[1]} كمسؤول رئيسي؟ لن تتمكن بعد ذلك من حذف حسابات المسؤولين.${trailingWhitespace}`;

  match = text.match(/^(.+?) is now the primary Administrator\.$/);
  if (match) return `${leadingWhitespace}أصبح ${match[1]} المسؤول الرئيسي الآن.${trailingWhitespace}`;

  match = text.match(/^Alert run matched (\d+) item\(s\)\.$/);
  if (match) return `${leadingWhitespace}اكتمل فحص التنبيه وطابق ${match[1]} عنصر.${trailingWhitespace}`;

  match = text.match(/^Return draft (.+) created\.$/);
  if (match) return `${leadingWhitespace}تم إنشاء مسودة المرتجع ${match[1]}.${trailingWhitespace}`;

  match = text.match(/^(POST|CREATE|CREATED|PUT|PATCH|UPDATE|UPDATED|DELETE|DELETED|CONFIRM|CONFIRMED|CANCEL|CANCELLED|APPROVE|APPROVED|REJECT|REJECTED|DEACTIVATE|DEACTIVATED|REACTIVATE|REACTIVATED|CHANGED)\s+(.+)\.$/i);
  if (match) {
    const actions = {
      post: "تم إنشاء", create: "تم إنشاء", created: "تم إنشاء",
      put: "تم تحديث", patch: "تم تحديث", update: "تم تحديث", updated: "تم تحديث",
      delete: "تم حذف", deleted: "تم حذف",
      confirm: "تم تأكيد", confirmed: "تم تأكيد",
      cancel: "تم إلغاء", cancelled: "تم إلغاء",
      approve: "تم اعتماد", approved: "تم اعتماد",
      reject: "تم رفض", rejected: "تم رفض",
      deactivate: "تم إلغاء تنشيط", deactivated: "تم إلغاء تنشيط",
      reactivate: "تمت إعادة تنشيط", reactivated: "تمت إعادة تنشيط",
      changed: "تم تغيير"
    };
    const subjects = {
      "employee account": "حساب الموظف",
      "inventory receipt": "إيصال المخزون",
      "Shopify event": "حدث Shopify",
      representative: "المندوب",
      notification: "التنبيه",
      stocktake: "الجرد",
      shipment: "الشحنة",
      operation: "العملية",
      merchant: "التاجر",
      payment: "الدفعة"
    };
    let subject = match[2];
    for (const [english, arabic] of Object.entries(subjects)) {
      if (subject.toLowerCase().startsWith(`${english.toLowerCase()} `)) {
        subject = `${arabic} ${subject.slice(english.length + 1)}`;
        break;
      }
    }
    return `${leadingWhitespace}${actions[match[1].toLowerCase()]} ${subject}.${trailingWhitespace}`;
  }

  match = text.match(/^(-?\d+(?:\.\d+)?)\s+open confirmations?$/i);
  if (match) return `${leadingWhitespace}${match[1]} تأكيد مفتوح${trailingWhitespace}`;

  match = text.match(/^(.+?)\s*\((\d+)(?:,\s*(\d+)\s+unread)?\)$/i);
  if (match) {
    const translatedLabel = arabicTranslations[match[1]] || match[1];
    const unreadPart = match[3] ? `، ${match[3]} غير مقروء` : "";
    return `${leadingWhitespace}${translatedLabel} (${match[2]}${unreadPart})${trailingWhitespace}`;
  }

  match = text.match(/^(-?\d+(?:\.\d+)?)\s+(products?|users?|modules?|merchants?|representatives?|reps?|unread|active|operations?|visible|shortages?|items?|packs?|pieces?|sessions?|events?|categories?|balances?|batches?|transactions?|recalls?|records?|expired|logged)$/i);
  if (match) {
    const labels = {
      product: "منتج", products: "منتج",
      user: "مستخدم", users: "مستخدم",
      module: "وحدة", modules: "وحدة",
      merchant: "تاجر", merchants: "تاجر",
      representative: "مندوب", representatives: "مندوب",
      rep: "مندوب", reps: "مندوب",
      unread: "غير مقروء",
      active: "عملية نشطة",
      operation: "عملية", operations: "عملية",
      visible: "ظاهر",
      shortage: "حالة عجز", shortages: "حالة عجز",
      item: "عنصر", items: "عنصر",
      pack: "عبوة", packs: "عبوة",
      piece: "قطعة", pieces: "قطعة",
      session: "جلسة", sessions: "جلسة",
      event: "حدث", events: "حدث",
      category: "فئة", categories: "فئة",
      balance: "رصيد", balances: "رصيد",
      batch: "دفعة", batches: "دفعة",
      transaction: "معاملة", transactions: "معاملة",
      recall: "طلب استرجاع", recalls: "طلب استرجاع",
      record: "سجل", records: "سجل",
      expired: "دفعة منتهية الصلاحية",
      logged: "مسجّل"
    };
    return `${leadingWhitespace}${match[1]} ${labels[match[2].toLowerCase()]}${trailingWhitespace}`;
  }

  match = text.match(/^(-?\d+(?:\.\d+)?)\s+pack\(s\)$/i);
  if (match) return `${leadingWhitespace}${match[1]} عبوة${trailingWhitespace}`;

  match = text.match(/^(-?\d+(?:\.\d+)?)\s+packs?(?:\s*\/\s*(-?\d+(?:\.\d+)?)\s+pieces?)?$/i);
  if (match) {
    const pieces = match[2] !== undefined ? ` / ${match[2]} قطعة` : "";
    return `${leadingWhitespace}${match[1]} عبوة${pieces}${trailingWhitespace}`;
  }

  match = text.match(/^(.+?)\s*\/\s*(\d+)\s+pcs$/i);
  if (match) return `${leadingWhitespace}${match[1]} / ${match[2]} قطعة${trailingWhitespace}`;

  match = text.match(/^(.+?)\s+expired$/i);
  if (match) return `${leadingWhitespace}${match[1]} — منتهي الصلاحية${trailingWhitespace}`;

  const colonMatch = text.match(/^([^:]+):\s*(.+)$/);
  if (colonMatch && arabicTranslations[colonMatch[1]]) {
    return `${leadingWhitespace}${arabicTranslations[colonMatch[1]]}: ${colonMatch[2]}${trailingWhitespace}`;
  }

  return value;
}

function uiText(english) {
  return currentLanguage === "ar" ? translateEnglishText(english).trim() : english;
}

function getOriginalText(node) {
  const current = node.nodeValue;
  let original = translatedTextSources.get(node);
  if (original === undefined) {
    original = current;
    translatedTextSources.set(node, original);
    return original;
  }

  const expectedArabic = translateEnglishText(original, node.parentElement);
  if (current !== original && current !== expectedArabic) {
    original = current;
    translatedTextSources.set(node, original);
  }
  return original;
}

function getOriginalAttribute(element, attribute) {
  let sources = translatedAttributeSources.get(element);
  if (!sources) {
    sources = new Map();
    translatedAttributeSources.set(element, sources);
  }

  const current = element.getAttribute(attribute) || "";
  let original = sources.get(attribute);
  if (original === undefined) {
    original = current;
    sources.set(attribute, original);
    return original;
  }

  const expectedArabic = translateEnglishText(original);
  if (current !== original && current !== expectedArabic) {
    original = current;
    sources.set(attribute, original);
  }
  return original;
}

function applyLanguage() {
  if (applyingLanguage) return;
  applyingLanguage = true;

  const isArabic = currentLanguage === "ar";
  document.documentElement.lang = isArabic ? "ar-EG" : "en";
  document.documentElement.dir = isArabic ? "rtl" : "ltr";
  document.body.classList.toggle("lang-ar", isArabic);

  const route = routes[currentPath()];
  document.title = route
    ? `Lensee - ${isArabic ? translateEnglishText(route.title).trim() : route.title}`
    : "Lensee";

  document.querySelectorAll("#language-toggle, #login-language-toggle").forEach((toggle) => {
    toggle.setAttribute("data-no-translate", "");
    toggle.textContent = isArabic ? "English" : "العربية";
    toggle.setAttribute("aria-label", isArabic ? "التبديل إلى الإنجليزية" : "Switch to Arabic");
    toggle.title = isArabic ? "التبديل إلى الإنجليزية" : "Switch to Arabic";
  });

  const root = document.body;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);

  for (const node of nodes) {
    if (node.parentElement?.closest("[data-no-translate], script, style")) continue;
    const original = getOriginalText(node);
    const translated = isArabic ? translateEnglishText(original, node.parentElement) : original;
    if (node.nodeValue !== translated) node.nodeValue = translated;
  }

  root.querySelectorAll("[placeholder], [title], [aria-label]").forEach((element) => {
    if (element.closest("[data-no-translate]")) return;
    for (const attribute of ["placeholder", "title", "aria-label"]) {
      if (!element.hasAttribute(attribute)) continue;
      const original = getOriginalAttribute(element, attribute);
      const translated = isArabic ? translateEnglishText(original) : original;
      if (element.getAttribute(attribute) !== translated) element.setAttribute(attribute, translated);
    }
  });

  applyingLanguage = false;
}

function queueLanguageApply() {
  if (applyingLanguage || currentLanguage !== "ar") return;
  window.clearTimeout(languageApplyTimer);
  languageApplyTimer = window.setTimeout(() => {
    if (!applyingLanguage && currentLanguage === "ar") {
      applyLanguage();
    }
  }, 0);
}

function startLanguageObserver() {
  if (languageObserver || !document.body) return;
  languageObserver = new MutationObserver((mutations) => {
    if (applyingLanguage || currentLanguage !== "ar") return;
    const hasTranslatableChange = mutations.some((mutation) => {
      if (mutation.type === "childList" || mutation.type === "characterData") return true;
      return mutation.type === "attributes" && ["placeholder", "title", "aria-label"].includes(mutation.attributeName);
    });
    if (hasTranslatableChange) {
      queueLanguageApply();
    }
  });
  languageObserver.observe(document.body, {
    childList: true,
    subtree: true,
    characterData: true,
    attributes: true,
    attributeFilter: ["placeholder", "title", "aria-label"]
  });
}

function setLanguage(language) {
  currentLanguage = language === "ar" ? "ar" : "en";
  localStorage.setItem(languageKey, currentLanguage);
  applyLanguage();
  if (currentPath() === "/supply" && getAuth()) {
    routes["/supply"].render().then(applyLanguage).catch((exception) => notice(getFriendlyWorkspaceError(exception), "error"));
  }
}

const systemValueAliases = Object.freeze({
  "استلام مخزون": "InventoryReceipt",
  "تحويل مخزون": "WarehouseTransfer",
  "بيع جملة": "WholesaleSale",
  "بيع قطاعي / أونلاين": "RetailSale",
  "حجز للمندوب": "Reserve",
  "مرتجع": "Return",
  "استبدال": "Change",
  "إعدام / تسوية مخزون": "WriteOff",
  "نقدي مباشر": "CashHandToHand",
  "تحويل أو إيداع نقدي": "CashTransaction",
  "تقسيط": "Installment",
  "عبوات": "Packs",
  "قطع": "Pieces",
  "بديل": "ChangeIn",
  "راجع": "ChangeOut",
  "استلام نقدي": "CashReceived",
  "استرداد نقدي": "CashRefund",
  "رصيد للتاجر": "MerchantCredit",
  "تخفيض الرصيد": "BalanceReduction"
});

function canonicalSystemValue(value) {
  const text = String(value || "").trim();
  return systemValueAliases[text] || text;
}

function canonicalSelectValue(id) {
  const element = document.getElementById(id);
  return canonicalSystemValue(element?.value || element?.selectedOptions?.[0]?.textContent || "");
}

const routes = {
  "/login": { title: "Sign In", label: "Identity", roles: [], render: renderLogin },
  "/dashboard": { title: "Overview", label: "Dashboard", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderDashboard },
  "/catalog": { title: "Catalog", label: "Catalog", roles: ["CLevel", "Admin", "ERPAdmin", "WarehouseClerk"], render: renderCatalog },
  "/inventory": { title: "Inventory", label: "Inventory", roles: ["CLevel", "Admin", "ERPAdmin", "WarehouseClerk"], render: renderInventory },
  "/supply": { title: "Supply", label: "Supply", roles: ["CLevel", "Admin"], render: renderSupply },
  "/crm": { title: "CRM", label: "CRM", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderCrm },
  "/operations": { title: "Operations", label: "Operations", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderOperations },
  "/payments": { title: "Payments", label: "Payments", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant"], render: renderPayments },
  "/notifications": { title: "Notifications", label: "Notifications", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderNotifications },
  "/integrations": { title: "Online intake", label: "Online intake", roles: ["CLevel", "Admin", "ERPAdmin", "WarehouseClerk"], render: renderShopifyIntegration },
  "/reports": { title: "Reports", label: "Reports", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant"], render: renderReports },
  "/stocktakes": { title: "Stocktake", label: "Stocktake", roles: ["CLevel", "Admin", "ERPAdmin"], render: renderStocktakes },
  "/audit": { title: "Audit history", label: "Audit history", roles: ["Admin", "ERPAdmin"], render: renderAudit },
  "/admin": { title: "Administration", label: "Admin", roles: ["Admin", "ERPAdmin"], render: renderAdmin }
};

const navItems = [
  ["/dashboard", "Dashboard"],
  ["/catalog", "Catalog"],
  ["/inventory", "Inventory"],
  ["/supply", "Supply"],
  ["/crm", "CRM"],
  ["/operations", "Operations"],
  ["/payments", "Payments"],
  ["/notifications", "Notifications"],
  ["/integrations", "Online intake"],
  ["/reports", "Reports"],
  ["/stocktakes", "Stocktake"],
  ["/audit", "Audit history"],
  ["/admin", "Admin"]
];

const navGroups = [
  { label: "Daily work", items: ["/dashboard", "/operations", "/notifications"] },
  { label: "Money", items: ["/payments", "/reports"] },
  { label: "Stock", items: ["/inventory", "/supply", "/catalog", "/stocktakes"] },
  { label: "Oversight", items: ["/crm", "/integrations", "/audit", "/admin"] }
];

if (!sessionStorage.getItem("lensee.tabId")) {
  sessionStorage.setItem("lensee.tabId", crypto.randomUUID?.() || `${Date.now()}-${Math.random()}`);
}
const tabId = sessionStorage.getItem("lensee.tabId");

document.getElementById("logout-button").addEventListener("click", logout);
document.getElementById("language-toggle").addEventListener("click", () => setLanguage(currentLanguage === "ar" ? "en" : "ar"));
document.getElementById("sidebar-toggle")?.addEventListener("click", () => setSidebarOpen(!document.body.classList.contains("sidebar-open")));
document.addEventListener("click", (event) => {
  const segment = event.target.closest("[data-scroll-target]");
  if (segment) {
    const target = document.getElementById(segment.dataset.scrollTarget);
    if (target) {
      segment.closest(".segmented-control")?.querySelectorAll("[role='tab']").forEach((tab) => {
        tab.setAttribute("aria-selected", tab === segment ? "true" : "false");
      });
      target.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  }

  if (event.target.closest("#login-language-toggle")) {
    setLanguage(currentLanguage === "ar" ? "en" : "ar");
  }
  if (event.target.closest("#nav a") && window.matchMedia("(max-width: 820px)").matches) {
    setSidebarOpen(false);
  }
  if (document.body.classList.contains("sidebar-open") &&
      !event.target.closest(".sidebar") &&
      !event.target.closest("#sidebar-toggle")) {
    setSidebarOpen(false);
  }
});

document.addEventListener("submit", (event) => {
  const form = event.target;
  if (!(form instanceof HTMLFormElement)) return;
  form.querySelectorAll("input, textarea").forEach((input) => {
    if (!(input instanceof HTMLInputElement || input instanceof HTMLTextAreaElement) || input.type === "password") return;
    const name = `${input.name} ${input.id}`.toLowerCase();
    if (/(token|secret|payload|password)/.test(name)) return;
    if (input instanceof HTMLTextAreaElement) {
      input.value = input.value.replace(/\r\n/g, "\n").trim();
    } else if (/username|sku/.test(name)) {
      input.value = input.value.trim();
    } else if (["text", "search", "email", "tel", "url"].includes(input.type)) {
      input.value = input.value.trim().replace(/\s+/g, " ");
    }
  });
}, true);
window.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    setSidebarOpen(false);
  }
});
window.addEventListener("hashchange", renderRoute);
window.addEventListener("focus", () => {
  checkHealth();
  refreshActiveView({ reason: "focus" });
});
window.addEventListener(mutationEventName, () => {
  refreshActiveView({ reason: "local-mutation" });
  updateNotificationBadge();
});
window.addEventListener("storage", (event) => {
  if (!syncChannel && event.key === syncStorageKey && event.newValue) {
    try { handleExternalSync(JSON.parse(event.newValue)); } catch { /* Ignore malformed sync payloads. */ }
  }
  if (event.key === authKey) {
    window.dispatchEvent(new CustomEvent(authEventName));
  }
});
window.addEventListener(authEventName, renderRoute);
syncChannel?.addEventListener("message", (event) => handleExternalSync(event.data));
checkHealth();
startLanguageObserver();
restoreSessionFromCookie().finally(() => {
  renderRoute();
  applyLanguage();
});

function getAuth() {
  return activeAuth;
}
window.__lenseeGetAuth = getAuth;

function setAuth(auth, { broadcast = true } = {}) {
  activeAuth = auth ? { accessToken: auth.accessToken, user: auth.user } : null;
  localStorage.removeItem(authKey);
  if (broadcast) publishSync({ type: "auth-signed-in", source: tabId });
}

function clearAuth({ broadcast = true } = {}) {
  activeAuth = null;
  localStorage.removeItem(authKey);
  if (broadcast) publishSync({ type: "auth-signed-out", source: tabId });
}

function publishSync(payload) {
  const message = { ...payload, id: crypto.randomUUID?.() || `${Date.now()}-${Math.random()}`, at: Date.now() };
  if (syncChannel) {
    syncChannel.postMessage(message);
  } else {
    localStorage.setItem(syncStorageKey, JSON.stringify(message));
  }
}

function handleExternalSync(payload) {
  if (!payload || payload.source === tabId) return;
  if (payload.type === "auth-signed-in") {
    restoreSessionFromCookie().finally(renderRoute);
    return;
  }
  if (payload.type === "auth-signed-out") {
    clearAuth({ broadcast: false });
    renderRoute();
    return;
  }
  if (payload.type === "mutation") {
    refreshActiveView({ reason: "external-mutation" });
    updateNotificationBadge();
  }
}

function buildRequestHeaders(options = {}, auth = getAuth()) {
  const headers = new Headers(options.headers || {});
  applyApiHeaders(headers);
  if (options.body !== undefined && !(options.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  if (auth?.accessToken) {
    headers.set("Authorization", `Bearer ${auth.accessToken}`);
  }
  return headers;
}

function createUuid() {
  const cryptoSource = globalThis.crypto;
  if (cryptoSource?.randomUUID) {
    return cryptoSource.randomUUID();
  }
  const bytes = new Uint8Array(16);
  cryptoSource.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0"));
  return `${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex.slice(6, 8).join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
}

function withPaymentIdempotency(path, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  if (method === "GET" || !path.startsWith("/api/v1/payments")) {
    return options;
  }

  const headers = new Headers(options.headers || {});
  if (!headers.has("Idempotency-Key")) {
    headers.set("Idempotency-Key", createUuid());
  }
  return { ...options, headers };
}

async function fetchWithAuth(path, options = {}) {
  let auth = getAuth();
  let headers = buildRequestHeaders(options, auth);
  let response = await fetch(`${apiBase}${path}`, { ...options, headers, credentials: "include" });
  if (response.status !== 401 || !auth?.accessToken) {
    return response;
  }

  const refreshed = await refreshSession();
  if (!refreshed) {
    return response;
  }

  auth = refreshed;
  headers = buildRequestHeaders(options, auth);
  return fetch(`${apiBase}${path}`, { ...options, headers, credentials: "include" });
}

async function request(path, options = {}) {
  const requestOptions = withPaymentIdempotency(path, options);
  const response = await fetchWithAuth(path, requestOptions);

  if (!response.ok) {
    const body = await response.text();
    const error = new Error(body || response.statusText);
    error.status = response.status;
    throw error;
  }

  const payload = response.status === 204 ? null : await response.json();
  const method = (requestOptions.method || "GET").toUpperCase();
  if (method !== "GET") {
    window.dispatchEvent(new CustomEvent(mutationEventName, { detail: { path, method } }));
    publishSync({ type: "mutation", source: sessionStorage.getItem("lensee.tabId"), path, method });
  }

  return payload;
}

async function downloadFile(path, fileName) {
  const response = await fetchWithAuth(path);

  if (!response.ok) {
    const body = await response.text();
    const error = new Error(body || response.statusText);
    error.status = response.status;
    throw error;
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function delay(milliseconds) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}

function readRefreshLockLease() {
  try {
    const value = localStorage.getItem(refreshLockStorageKey);
    return value ? JSON.parse(value) : null;
  } catch {
    return null;
  }
}

function releaseRefreshLockLease(owner) {
  const lease = readRefreshLockLease();
  if (lease?.owner === owner) {
    localStorage.removeItem(refreshLockStorageKey);
  }
}

async function withRefreshLockFallback(action) {
  const owner = `${tabId}:${crypto.randomUUID?.() || `${Date.now()}-${Math.random()}`}`;
  const deadline = Date.now() + refreshLockWaitMs;

  while (Date.now() < deadline) {
    try {
      const currentLease = readRefreshLockLease();
      if (!currentLease || currentLease.expiresAt <= Date.now()) {
        localStorage.setItem(refreshLockStorageKey, JSON.stringify({ owner, expiresAt: Date.now() + refreshLockLeaseMs }));
        if (readRefreshLockLease()?.owner === owner) {
          try {
            return await action();
          } finally {
            releaseRefreshLockLease(owner);
          }
        }
      }
    } catch {
      return null;
    }

    await delay(75 + Math.floor(Math.random() * 75));
  }

  return null;
}

async function withRefreshLock(action) {
  if (navigator.locks?.request) {
    return navigator.locks.request(refreshLockName, { mode: "exclusive" }, action);
  }

  return withRefreshLockFallback(action);
}

async function refreshSession({ broadcastFailure = true } = {}) {
  if (refreshSessionPromise) {
    return refreshSessionPromise;
  }

  refreshSessionPromise = (async () => {
    try {
      const result = await withRefreshLock(async () => {
        const headers = new Headers({ "Content-Type": "application/json" });
        applyApiHeaders(headers);
        const response = await fetch(`${apiBase}/api/v1/auth/refresh`, {
          method: "POST",
          headers,
          credentials: "include",
          body: JSON.stringify({})
        });
        if (!response.ok) {
          return { auth: null, terminal: response.status < 500 };
        }
        return { auth: await response.json(), terminal: false };
      });
      if (!result) {
        clearAuth({ broadcast: false });
        return null;
      }
      if (!result.auth) {
        clearAuth({ broadcast: broadcastFailure && result.terminal });
        return null;
      }
      setAuth(result.auth, { broadcast: false });
      return result.auth;
    } catch {
      clearAuth({ broadcast: false });
      return null;
    } finally {
      refreshSessionPromise = null;
    }
  })();

  return refreshSessionPromise;
}

async function restoreSessionFromCookie() {
  try {
    return await refreshSession({ broadcastFailure: false });
  } catch {
    clearAuth({ broadcast: false });
    return null;
  }
}

async function checkHealth() {
  const pill = document.getElementById("health-pill");
  try {
    const healthBase = await resolveApiBase();
    const health = await fetchHealth(healthBase).then((response) => response.json());
    pill.textContent = health.status === "Healthy" ? "API healthy" : "API degraded";
    pill.className = `status-pill ${health.status === "Healthy" ? "status-ok" : "status-warn"}`;
  } catch {
    pill.textContent = "API offline";
    pill.className = "status-pill status-warn";
  }
}

function fetchHealth(baseUrl) {
  return fetch(`${baseUrl}/health`, { headers: apiHeaders(), cache: "no-store" });
}

async function resolveApiBase(preferred = apiBase) {
  const candidates = [preferred, ...apiCandidates].filter((value, index, values) => value && values.indexOf(value) === index);
  for (const candidate of candidates) {
    try {
      const normalized = candidate.replace(/\/$/, "");
      const response = await fetchHealth(normalized);
      if (response.ok) {
        apiBase = normalized;
        localStorage.setItem("lensee.apiBase", apiBase);
        return apiBase;
      }
    } catch {
      // Try the next local development URL.
    }
  }
  return preferred.replace(/\/$/, "");
}

function currentPath() {
  const hash = location.hash.replace(/^#/, "");
  return (hash.split("?")[0] || "/dashboard").replace(/\/$/, "") || "/dashboard";
}

function currentRouteQuery() {
  const queryIndex = location.hash.indexOf("?");
  return new URLSearchParams(queryIndex >= 0 ? location.hash.slice(queryIndex + 1) : "");
}

async function renderRoute() {
  const auth = getAuth();
  const path = currentPath();
  const route = routes[path];
  if (!route) {
    location.hash = auth ? "/dashboard" : "/login";
    return;
  }
  document.body.classList.toggle("auth-page", path === "/login" && !auth);

  if (path !== "/login" && !auth) {
    location.hash = "/login";
    return;
  }
  if (auth && path === "/login") {
    location.hash = "/dashboard";
    return;
  }
  if (route.roles.length > 0 && auth && !route.roles.includes(auth.user.role)) {
    renderForbidden();
    return;
  }
  if (auth && path === "/integrations" && auth.user.role === "WarehouseClerk" && auth.user.locationType !== "Online") {
    renderForbidden();
    return;
  }

  document.getElementById("page-title").textContent = route.title;
  document.getElementById("route-label").textContent = route.label;
  renderNav(auth);
  renderSession(auth);
  updateNotificationBadge();
  await route.render();
  applyLanguage();
  startVisibleIdentifierMasking();
  sanitizeVisibleIdentifiers(document.getElementById("view"));
  scheduleRouteRefresh(path);
  await applyNotificationFocus();
  sanitizeVisibleIdentifiers(document.getElementById("view"));
}

async function applyNotificationFocus() {
  const query = currentRouteQuery();
  const reference = query.get("ref");
  if (reference) {
    try {
      const destination = await request(`/api/v1/navigation-references/${encodeURIComponent(reference)}/resolve`);
      const destinationPath = String(destination.route || "").replace(/^#/, "");
      if (!destinationPath || destinationPath !== currentPath()) {
        location.hash = `${destination.route}?ref=${encodeURIComponent(reference)}`;
        return;
      }
      await applyResolvedFocus(destination.focus, destination.recordId);
    } catch {
      notice("This secure link is unavailable or has expired.", "warning");
    }
    return;
  }

  // Legacy links are stripped once opened. New links contain opaque, server-issued references only.
  const id = query.get("id");
  const focus = query.get("focus");
  if (!id || !focus) return;

  try {
    await applyResolvedFocus(focus, id);
    history.replaceState(null, "", `#${currentPath()}`);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function applyResolvedFocus(focus, id) {
  if (focus === "merchant") await showMerchantDetail(id);
  if (focus === "supply-shipment") await showSupplyDetail(id);
  if (focus === "stocktake") await showStocktakeDetail(id);
  if (focus === "operation") {
    const button = document.querySelector(`[data-op-toggle][data-op-id="${CSS.escape(id)}"]`);
    if (button) await toggleOperationDetails(id, button, true);
  }
  if (focus === "payment") {
    const button = document.querySelector(`[data-payment-detail="${CSS.escape(id)}"]`);
    if (button) await togglePaymentDetails(id, button);
  }
  if (focus === "merchant-expiry-recall") {
    const row = document.querySelector(`[data-merchant-recall-row="${CSS.escape(id)}"]`);
    row?.scrollIntoView({ behavior: "smooth", block: "center" });
    row?.classList.add("focus-highlight");
  }
}

function renderNav(auth) {
  const nav = document.getElementById("nav");
  const path = currentPath();
  nav.innerHTML = "";
  if (!auth) {
    nav.innerHTML = `<a href="#/login" aria-current="page">Sign in</a>`;
    return;
  }

  const itemLabels = new Map(navItems);
  for (const group of navGroups) {
    const visibleItems = group.items.filter((href) => routes[href]?.roles.includes(auth.user.role)
      && (href !== "/integrations" || auth.user.role !== "WarehouseClerk" || auth.user.locationType === "Online"));
    if (visibleItems.length === 0) {
      continue;
    }

    const groupNode = document.createElement("section");
    groupNode.className = "nav-group";
    groupNode.innerHTML = `<p class="nav-group-label">${escapeHtml(group.label)}</p>`;

    for (const href of visibleItems) {
      const link = document.createElement("a");
      link.href = `#${href}`;
      link.textContent = itemLabels.get(href) || routes[href].label;
      if (href === "/notifications") {
        link.id = "notifications-nav-link";
      }
      if (path === href) {
        link.setAttribute("aria-current", "page");
      }
      groupNode.appendChild(link);
    }

    nav.appendChild(groupNode);
  }
}

function renderSession(auth) {
  const session = document.getElementById("session");
  session.textContent = auth ? `${roleLabel(auth.user.role)}${auth.user.locationId ? " - Location scoped" : ""}` : "Not signed in";
  document.getElementById("logout-button").hidden = !auth;
  document.getElementById("sidebar-toggle").hidden = !auth;
}

function setSidebarOpen(open) {
  document.body.classList.toggle("sidebar-open", open);
  const toggle = document.getElementById("sidebar-toggle");
  if (!toggle) return;
  toggle.setAttribute("aria-expanded", String(open));
  const label = open ? "Close navigation" : "Open navigation";
  toggle.setAttribute("aria-label", label);
  toggle.title = label;
}

function pageIntro({ eyebrow, title, body = "", metrics = "" }) {
  return `
    <section class="page-intro">
      <div>
        <p class="eyebrow">${escapeHtml(eyebrow)}</p>
        <h2>${escapeHtml(title)}</h2>
        ${body ? `<p>${escapeHtml(body)}</p>` : ""}
      </div>
      ${metrics ? `<div class="rail-metrics">${metrics}</div>` : ""}
    </section>`;
}

function statusChip(label, tone = "muted", id = null) {
  const idAttribute = id ? ` id="${escapeHtml(id)}"` : "";
  return `<span${idAttribute} class="status-pill status-${escapeHtml(tone)}">${escapeHtml(label)}</span>`;
}

function emptyState(message, actionHtml = "") {
  return `<div class="empty-state"><span>${escapeHtml(message)}</span>${actionHtml}</div>`;
}

function segmentedControl(items) {
  return `<div class="segmented-control" role="tablist">${items.map((item, index) => `<button type="button" data-scroll-target="${escapeHtml(item.target)}" role="tab" aria-selected="${index === 0 ? "true" : "false"}">${escapeHtml(item.label)}</button>`).join("")}</div>`;
}

function notice(message, tone = "info") {
  const area = document.getElementById("notification-area");
  const id = `notice-${++noticeSequence}`;
  const node = document.createElement("div");
  node.className = `notice notice-${tone}`;
  node.id = id;
  node.setAttribute("role", tone === "error" ? "alert" : "status");
  node.innerHTML = `<span>${escapeHtml(displaySafeText(message))}</span><button class="notice-close" type="button" aria-label="Dismiss notice">x</button>`;
  node.querySelector("button").addEventListener("click", () => node.remove());
  area.appendChild(node);
  window.setTimeout(() => {
    const current = document.getElementById(id);
    if (current) current.remove();
  }, tone === "error" ? 12000 : 7000);
}

function promptDialog({ title, label, defaultValue = "", inputType = "text", required = false, multiline = false }) {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.className = "dialog-overlay";
    overlay.innerHTML = `
      <form class="dialog-card">
        <div class="section-head tight-head">
          <div><h2>${escapeHtml(title)}</h2><p class="muted-text">${escapeHtml(label)}</p></div>
        </div>
        <div class="field">
          ${multiline
            ? `<textarea class="input dialog-input" rows="4">${escapeHtml(defaultValue)}</textarea>`
            : `<input class="input dialog-input" type="${escapeHtml(inputType)}" value="${escapeHtml(defaultValue)}">`}
        </div>
        <div class="form-actions">
          <button class="button primary" type="submit">Continue</button>
          <button class="button secondary" type="button" data-dialog-cancel>Cancel</button>
        </div>
      </form>`;
    document.body.appendChild(overlay);
    const input = overlay.querySelector(".dialog-input");
    input.focus();
    input.select?.();
    const close = (value) => {
      overlay.remove();
      resolve(value);
    };
    overlay.addEventListener("click", (event) => {
      if (event.target === overlay) {
        close(null);
      }
    });
    overlay.querySelector("[data-dialog-cancel]").addEventListener("click", () => close(null));
    overlay.querySelector("form").addEventListener("submit", (event) => {
      event.preventDefault();
      const value = input.value.trim();
      if (required && !value) {
        input.setAttribute("aria-invalid", "true");
        input.focus();
        return;
      }
      close(value);
    });
  });
}


async function withMutationGuard(key, control, action) {
  if (mutationLocks.has(key)) {
    return null;
  }

  mutationLocks.add(key);
  const previousDisabled = control?.disabled;
  const previousBusy = control?.getAttribute?.("aria-busy");
  if (control) {
    control.disabled = true;
    control.setAttribute("aria-busy", "true");
  }

  try {
    return await action();
  } finally {
    mutationLocks.delete(key);
    if (control) {
      control.disabled = Boolean(previousDisabled);
      if (previousBusy === null || previousBusy === undefined) {
        control.removeAttribute("aria-busy");
      } else {
        control.setAttribute("aria-busy", previousBusy);
      }
    }
  }
}
function apiHeaders() {
  const headers = new Headers();
  applyApiHeaders(headers);
  return headers;
}

function applyApiHeaders(headers) {
  headers.set(ngrokSkipHeader, "true");
}

function confirmDialog({ title, message, confirmLabel = "Confirm", cancelLabel = "Cancel", tone = "default", bodyHtml = "" }) {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.className = "dialog-overlay";
    overlay.innerHTML = `
      <section class="dialog-card confirm-dialog ${tone === "warning" ? "confirm-dialog-warning" : ""}" role="dialog" aria-modal="true" aria-labelledby="confirm-dialog-title">
        <div class="section-head tight-head">
          <div>
            <h2 id="confirm-dialog-title">${escapeHtml(title)}</h2>
            <p class="muted-text">${escapeHtml(message)}</p>
          </div>
        </div>
        ${bodyHtml ? `<div class="confirm-dialog-body">${bodyHtml}</div>` : ""}
        <div class="form-actions">
          <button class="button primary" type="button" data-dialog-confirm>${escapeHtml(confirmLabel)}</button>
          <button class="button secondary" type="button" data-dialog-cancel>${escapeHtml(cancelLabel)}</button>
        </div>
      </section>`;
    document.body.appendChild(overlay);
    const close = (value) => {
      overlay.remove();
      resolve(value);
    };
    overlay.addEventListener("click", (event) => {
      if (event.target === overlay) {
        close(false);
      }
    });
    overlay.querySelector("[data-dialog-cancel]").addEventListener("click", () => close(false));
    overlay.querySelector("[data-dialog-confirm]").addEventListener("click", () => close(true));
    overlay.querySelector("[data-dialog-confirm]").focus();
  });
}

function scheduleRouteRefresh(path) {
  window.clearInterval(activeRefreshTimer);
  activeRefreshTimer = null;
  activeRefreshController?.abort();
  activeRefreshController = null;
  if (path === "/login") {
    return;
  }

  activeRefreshTimer = window.setInterval(() => {
    refreshActiveView({ reason: "timer" });
    updateNotificationBadge();
  }, 30000);
}

async function refreshActiveView({ reason = "manual" } = {}) {
  if (!getAuth() || activeRefreshInFlight) {
    return;
  }

  activeRefreshInFlight = true;
  activeRefreshController?.abort();
  activeRefreshController = new AbortController();
  const startedPath = currentPath();

  try {
    switch (startedPath) {
      case "/catalog":
        await loadCatalogProducts();
        break;
      case "/inventory":
        await refreshInventoryTables();
        break;
      case "/supply":
        await loadSupplyShipments();
        break;
      case "/crm":
        await Promise.all([loadMerchants(), loadRepresentatives()]);
        break;
      case "/operations":
        await loadOperations();
        break;
      case "/payments":
        await Promise.all([loadPayments(), loadPaymentHistory()]);
        break;
      case "/notifications":
        await Promise.all([
          loadNotifications(),
          ["Admin", "ERPAdmin", "CLevel"].includes(getAuth()?.user.role) ? loadMerchantExpiryRecalls() : Promise.resolve()
        ]);
        break;
      case "/reports":
        await loadReports();
        break;
      case "/stocktakes":
        await loadStocktakes();
        break;
      case "/admin":
        await loadAdminUsers();
        break;
    }
  } catch (error) {
    if (error?.name !== "AbortError") {
      console.warn(`Refresh failed for ${startedPath} (${reason})`, error);
    }
  } finally {
    activeRefreshInFlight = false;
  }
}

async function updateNotificationBadge() {
  const link = document.getElementById("notifications-nav-link");
  if (!link || !getAuth() || notificationBadgeInFlight) {
    return;
  }

  notificationBadgeInFlight = true;
  try {
    const result = await request("/api/v1/notifications/unread-count");
    const count = Number(result.count || 0);
    link.textContent = count > 0 ? `Notifications (${count})` : "Notifications";
  } catch (error) {
    link.textContent = "Notifications";
    if (error?.status === 401) {
      clearAuth();
    }
  } finally {
    notificationBadgeInFlight = false;
  }
}

function renderLogin() {
  document.getElementById("notification-area").innerHTML = "";
  document.getElementById("view").innerHTML = `
    <section class="auth-layout">
      <div class="auth-copy">
        <div class="auth-copy-top">
          <span class="auth-wordmark">Lensee</span>
          <button class="button secondary auth-language-toggle" id="login-language-toggle" type="button">${currentLanguage === "ar" ? "English" : "العربية"}</button>
        </div>
        <div class="auth-kicker">Operations ERP</div>
        <h2>Lensee</h2>
        <p>Secure ERP access for daily operations.</p>
        <div class="auth-status"><span id="login-health-dot" class="health-dot"></span><span id="login-health-text">Checking API</span></div>
      </div>
      <form class="auth-panel" id="login-form">
        <div class="auth-panel-head">
          <span>Authorized session</span>
          <strong>Sign in</strong>
        </div>
        <div class="field"><label for="username">Username</label><input class="input" id="username" name="username" autocomplete="username" required autofocus></div>
        <div class="field"><label for="password">Password</label><div class="password-field"><input class="input" id="password" name="password" type="password" autocomplete="current-password" required><button class="button secondary inline-icon password-toggle" id="toggle-password" type="button" aria-label="Show password" title="Show password">Show</button></div></div>
        <div class="login-error" id="login-error" role="alert" hidden></div>
        <button class="button auth-submit" id="login-submit" type="submit">Sign in</button>
      </form>
    </section>`;

  checkLoginHealth();
  document.getElementById("toggle-password").addEventListener("click", () => {
    const password = document.getElementById("password");
    const isHidden = password.type === "password";
    password.type = isHidden ? "text" : "password";
    document.getElementById("toggle-password").textContent = isHidden ? "Hide" : "Show";
  });
  document.getElementById("login-form").addEventListener("submit", login);
}

async function login(event) {
  event.preventDefault();
  const submit = document.getElementById("login-submit");
  const error = document.getElementById("login-error");
  const form = new FormData(event.currentTarget);
  const nextApiBase = (await resolveApiBase(apiBase)).replace(/\/$/, "");

  localStorage.setItem("lensee.apiBase", nextApiBase);
  apiBase = nextApiBase;
  error.hidden = true;
  submit.disabled = true;
  submit.textContent = "Signing in";
  try {
    const auth = await loginRequest(nextApiBase, {
      method: "POST",
      body: JSON.stringify({ username: form.get("username"), password: form.get("password") })
    });
    setAuth(auth);
    location.hash = "/dashboard";
    location.reload();
  } catch (exception) {
    error.textContent = getFriendlyLoginError(exception);
    error.hidden = false;
    submit.disabled = false;
    submit.textContent = "Sign in";
  }
}

async function loginRequest(baseUrl, options) {
  const headers = new Headers({ "Content-Type": "application/json" });
  applyApiHeaders(headers);
  const response = await fetch(`${baseUrl}/api/v1/auth/login`, { ...options, headers, credentials: "include" });
  if (!response.ok) {
    throw new Error(await response.text() || response.statusText);
  }
  return response.json();
}

async function checkLoginHealth() {
  const dot = document.getElementById("login-health-dot");
  const text = document.getElementById("login-health-text");
  try {
    const healthBase = await resolveApiBase(apiBase);
    const health = await fetchHealth(healthBase).then((response) => response.json());
    dot.className = `health-dot ${health.status === "Healthy" ? "health-ok" : "health-warn"}`;
    text.textContent = health.status === "Healthy" ? "API healthy" : "API degraded";
  } catch {
    dot.className = "health-dot health-warn";
    text.textContent = "API offline";
  }
}

function renderDashboard() {
  const auth = getAuth();
  const currentRole = auth?.user?.role || "";
  const visibleWorkspaces = navItems
    .filter(([href]) => routes[href].roles.length === 0 || routes[href].roles.includes(currentRole))
    .filter(([href]) => href !== "/dashboard")
    .map(([href, label]) => {
      const descriptions = {
        "/catalog": "Products, SKUs, categories, and brands.",
        "/inventory": "Stock balances, batches, replenishment, and targets.",
        "/supply": "Imported shipments, landed costs, and receipts.",
        "/crm": "Merchants, representatives, notes, and batch history.",
        "/operations": "Receipts, transfers, sales, returns, changes, and write-offs.",
        "/payments": "Payment logs, approvals, cash records, and live remaining.",
        "/notifications": "Workflow alerts, stock alerts, and operational updates.",
        "/reports": "CSV exports, PDF documents, and export history.",
        "/stocktakes": "Batch-aware counts and reconciliations.",
        "/admin": "Users, passwords, and access maintenance."
      };
      return workspaceCard(href, label, descriptions[href] || "Open workspace", workspaceTone(href));
    })
    .join("");

  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: "Overview",
      title: "Operator command center",
      body: `${dashboardPrimaryResponsibility(currentRole)}. Start with open work, then move to money, stock, and reports without losing operational context.`,
      metrics: `
        ${scenarioCard("Open work", "Loading", "status-muted", "dashboard-open-work")}
        ${scenarioCard("Open confirmations", "Loading", "status-muted", "dashboard-open-confirmations")}
        ${scenarioCard("Unread alerts", "Loading", "status-muted", "dashboard-unread-alerts")}
        ${scenarioCard("Total sales", "Loading", "status-muted", "dashboard-total-sales")}
        ${scenarioCard("Actual total I have", "Loading", "status-muted", "dashboard-actual-collected")}
        ${scenarioCard("Remaining", "Loading", "status-muted", "dashboard-remaining-receivable")}
      `
    })}
    
    <section class="command-grid">
      <a class="command-tile command-tile-daily" href="#/operations">
        <span>Daily work</span>
        <strong>Operations queue</strong>
        <small>Create drafts, confirm movement, and inspect history.</small>
      </a>
      ${routes["/payments"].roles.includes(currentRole) ? `<a class="command-tile command-tile-money" href="#/payments"><span>Money</span><strong>Confirmations queue</strong><small>Assign, use, approve, and audit payment records.</small></a>` : ""}
      <a class="command-tile command-tile-stock" href="#/inventory">
        <span>Stock</span>
        <strong>Stock attention</strong>
        <small>Check balances, batches, targets, and replenishment.</small>
      </a>
      <a class="command-tile command-tile-oversight" href="#/reports">
        <span>Oversight</span>
        <strong>Reports and exports</strong>
        <small>Download operational evidence and review totals.</small>
      </a>
    </section>

    <section class="band rail-band">
      <div class="section-head">
        <div>
          <h2>Workspace map</h2>
        </div>
      </div>
      <div class="workspace-card-grid">${visibleWorkspaces}</div>
    </section>`;

  loadDashboardFinancialSummary();
  loadDashboardOperationalSummary();
}

async function loadDashboardFinancialSummary() {
  const sales = document.getElementById("dashboard-total-sales");
  const actual = document.getElementById("dashboard-actual-collected");
  const remaining = document.getElementById("dashboard-remaining-receivable");
  if (!sales || !actual || !remaining) return;
  try {
    const summary = await request("/api/v1/reports/financial-summary");
    sales.textContent = formatMoney(summary.totalSales);
    actual.textContent = formatMoney(summary.actualCollected);
    remaining.textContent = formatMoney(summary.remainingReceivable);
    sales.className = "status-ok";
    actual.className = "status-ok";
    remaining.className = Number(summary.remainingReceivable || 0) > 0 ? "status-warn" : "status-ok";
  } catch {
    sales.textContent = "Unavailable";
    actual.textContent = "Unavailable";
    remaining.textContent = "Unavailable";
  }
}

function renderCatalog() {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  document.getElementById("view").innerHTML = `
    <section class="catalog-hero">
      <div>
        <p class="eyebrow">Catalog</p>
        <h2>Catalog master data</h2>
        <p>Manage products, SKUs, categories, and brands with clear active states and reusable product structure.</p>
      </div>
      <div class="scenario-grid">
        ${scenarioCard("Role", canWrite ? "Can edit catalog" : "View only", canWrite ? "status-ok" : "status-muted")}
        ${scenarioCard("Product scope", "Products and SKUs", "status-muted")}
        ${scenarioCard("Reference data", "Categories and brands", "status-muted")}
      </div>
    </section>

    <section class="catalog-layout">
      <aside class="catalog-side">
        <section class="band compact-band">
          <div class="section-head"><h2>Filters</h2><button id="catalog-refresh" class="button secondary" type="button">Refresh</button></div>
          <div class="field"><label for="catalog-search">Search</label><input id="catalog-search" class="input" placeholder="Product, brand, category"></div>
          <label class="check-field"><input id="catalog-include-inactive" type="checkbox" checked><span>Show inactive products</span></label>
          <div class="muted-text" id="catalog-count">Loading</div>
        </section>
      </aside>

      <section class="catalog-main">
        <section class="band">
          <div class="section-head"><h2>Products</h2><span class="status-pill ${canWrite ? "status-ok" : "status-muted"}">${canWrite ? "Writable" : "Read only"}</span></div>
          <div class="table-wrap"><table><thead><tr><th>Name</th><th>Type</th><th>Brand</th><th>Category</th><th>Pack</th><th>Status</th>${canWrite ? "<th>Actions</th>" : ""}</tr></thead><tbody id="catalog-products"><tr><td colspan="${canWrite ? 7 : 6}">Loading catalog</td></tr></tbody></table></div>
        </section>
        <section class="catalog-detail-grid">
          <section class="band" id="catalog-detail"><h2>Product detail</h2><p class="muted-text">Select a product to review its configuration, SKU set, and lifecycle state.</p></section>
          ${canWrite ? renderCatalogWritePanel() : `<section class="band"><h2>Access</h2><p class="muted-text">This role can review catalog data but cannot change it.</p></section>`}
        </section>
      </section>
    </section>`;

  document.getElementById("catalog-refresh").addEventListener("click", refreshCatalogWorkspace);
  document.getElementById("catalog-search").addEventListener("input", debounce(loadCatalogProducts, 250));
  document.getElementById("catalog-include-inactive").addEventListener("change", loadCatalogProducts);

  if (canWrite) {
    wireCatalogWritePanel();
  }
  refreshCatalogWorkspace();
}

async function loadDashboardOperationalSummary() {
  const openWork = document.getElementById("dashboard-open-work");
  const openConfirmations = document.getElementById("dashboard-open-confirmations");
  const unreadAlerts = document.getElementById("dashboard-unread-alerts");
  if (!openWork || !openConfirmations || !unreadAlerts) return;

  const setUnavailable = () => {
    openWork.textContent = "Unavailable";
    openConfirmations.textContent = "Unavailable";
    unreadAlerts.textContent = "Unavailable";
  };

  try {
    const [operations, payments, notifications] = await Promise.all([
      request("/api/v1/operations?pageSize=50").catch(() => null),
      request("/api/v1/payments?pageSize=50").catch(() => null),
      request("/api/v1/notifications?page=1&pageSize=50").catch(() => null)
    ]);

    const operationRows = operations?.items || [];
    const paymentRows = payments?.items || [];
    const notificationRows = notifications?.items || notifications || [];
    const activeOperations = operationRows.filter((operation) => !["Completed", "Received", "Cancelled"].includes(operation.status)).length;
    const queuePayments = paymentRows.filter((log) =>
      ["Installment", "CashHandToHand", "CashTransaction"].includes(log.paymentMethod) &&
      ["PendingAdmin", "PendingAccountant", "PendingAdminReview"].includes(log.status)).length;
    const unreadCount = notificationRows.filter((notification) => notification.isRead === false || notification.readAt == null).length;

    openWork.textContent = String(activeOperations);
    openConfirmations.textContent = String(queuePayments);
    unreadAlerts.textContent = String(unreadCount);
    openWork.className = activeOperations > 0 ? "status-warn" : "status-ok";
    openConfirmations.className = queuePayments > 0 ? "status-warn" : "status-ok";
    unreadAlerts.className = unreadCount > 0 ? "status-warn" : "status-ok";
  } catch {
    setUnavailable();
  }
}

function scenarioCard(title, value, tone, valueId = null) {
  const idAttribute = valueId ? ` id="${escapeHtml(valueId)}"` : "";
  return `<div class="scenario-card"><span>${escapeHtml(title)}</span><strong${idAttribute} class="${escapeHtml(tone)}">${escapeHtml(value)}</strong></div>`;
}

function workspaceCard(href, title, description, tone = "neutral") {
  return `<a class="workspace-card workspace-card-${escapeHtml(tone)}" href="#${escapeHtml(href)}"><strong>${escapeHtml(title)}</strong><span>${escapeHtml(description)}</span></a>`;
}

function workspaceTone(href) {
  if (["/operations", "/notifications"].includes(href)) return "daily";
  if (["/payments", "/reports"].includes(href)) return "money";
  if (["/inventory", "/catalog", "/stocktakes"].includes(href)) return "stock";
  return "oversight";
}

function dashboardPrimaryResponsibility(role) {
  return {
    Admin: "Cross-module administration",
    ERPAdmin: "Cross-module administration",
    CLevel: "Executive oversight",
    Accountant: "Payments and remaining control",
    WarehouseClerk: "Inventory and operational execution"
  }[role] || "Workspace access";
}

function isSystemAdminRole(role) {
  return role === "Admin" || role === "ERPAdmin";
}

function renderCatalogWritePanel() {
  return `
    <section class="write-stack">
      <section class="band">
        <div class="section-head"><h2>Product editor</h2><button class="button secondary" id="product-reset" type="button">New</button></div>
        <form class="form wide-form" id="product-form">
          <input type="hidden" id="product-id">
          <div class="form-error" id="product-error" hidden></div>
          <div class="form-grid">
            <div class="field"><label for="product-name">Name</label><input id="product-name" class="input" required></div>
            <div class="field"><label for="product-type">Type</label><select id="product-type" class="select"><option value="Lens">Lens</option><option value="Solution">Solution</option></select></div>
            <div class="field"><label for="product-category">Category</label><select id="product-category" class="select" required></select></div>
            <div class="field"><label for="product-brand">Brand</label><select id="product-brand" class="select" required></select></div>
            <div class="field"><label for="product-sell-mode">Sell mode</label><select id="product-sell-mode" class="select"><option value="SinglePiece">Single piece</option><option value="SealedPackOnly">Sealed pack only</option><option value="Both">Both</option></select></div>
            <div class="field"><label for="product-pieces">Pieces per pack</label><input id="product-pieces" class="input" type="number" min="1" value="1"></div>
            <div class="field"><label for="product-expiry">Expiry source</label><select id="product-expiry" class="select"><option value="Batch">Batch expiry date</option><option value="None">No batch expiry</option></select></div>
            <div class="field"><label for="product-duration-value">Valid for</label><input id="product-duration-value" class="input" type="number" min="1" step="1" value="6"></div>
            <div class="field"><label for="product-duration-unit">Duration unit</label><select id="product-duration-unit" class="select"><option value="Daily">Days</option><option value="Monthly" selected>Months</option><option value="Annual">Years</option></select></div>
            <input type="hidden" id="product-clinical">
          </div>
          
          
            <div class="form-actions"><button class="button" id="product-submit" type="submit">Create product</button><span class="muted-text" id="product-mode">New product</span></div>
        </form>
      </section>

      <section class="catalog-admin-grid">
        <section class="band compact-band">
          <div class="section-head"><h2>Categories</h2><button class="button secondary" id="category-reset" type="button">New</button></div>
          <form class="form" id="category-form">
            <input type="hidden" id="category-id">
            <div class="form-error" id="category-error" hidden></div>
            <div class="field"><label for="category-name">Name</label><input id="category-name" class="input" required></div>
            <div class="field"><label for="category-parent">Parent</label><select id="category-parent" class="select"><option value="">None</option></select></div>
            <div class="form-actions"><button class="button" id="category-submit" type="submit">Create category</button><span class="muted-text" id="category-mode">New category</span></div>
          </form>
          <div class="tree-list" id="category-list"></div>
        </section>
        <section class="band compact-band">
          <div class="section-head"><h2>Brands</h2><button class="button secondary" id="brand-reset" type="button">New</button></div>
          <form class="form" id="brand-form">
            <input type="hidden" id="brand-id">
            <div class="form-error" id="brand-error" hidden></div>
            <div class="field"><label for="brand-name">Name</label><input id="brand-name" class="input" required></div>
            <div class="form-actions"><button class="button" id="brand-submit" type="submit">Create brand</button><span class="muted-text" id="brand-mode">New brand</span></div>
          </form>
          <div class="chip-list" id="brand-list"></div>
        </section>
      </section>
    </section>`;
}

function wireCatalogWritePanel() {
  document.getElementById("product-type").addEventListener("change", syncProductTypeFields);
  document.getElementById("product-reset").addEventListener("click", resetProductForm);
  document.getElementById("category-reset").addEventListener("click", resetCategoryForm);
  document.getElementById("brand-reset").addEventListener("click", resetBrandForm);
  document.getElementById("category-form").addEventListener("submit", saveCategory);
  document.getElementById("brand-form").addEventListener("submit", saveBrand);
  document.getElementById("product-form").addEventListener("submit", saveProduct);
  syncProductTypeFields();
}

async function refreshCatalogWorkspace() {
  await loadCatalogLookups();
  await loadCatalogProducts();
  if (selectedProductId) {
    await loadCatalogDetail(selectedProductId);
  }
}

async function loadCatalogLookups() {
  try {
    const [categories, tree, brands] = await Promise.all([
      request("/api/v1/catalog/categories"),
      request("/api/v1/catalog/categories/tree"),
      request("/api/v1/catalog/brands")
    ]);
    catalogCategories = categories;
    categoryTree = tree;
    catalogBrands = brands;
    refreshLookupControls();
  } catch (exception) {
    notice(getFriendlyApiError(exception), "error");
  }
}

function refreshLookupControls() {
  const canWrite = isSystemAdminRole(getAuth()?.user.role);
  if (!canWrite) {
    return;
  }
  fillCategorySelect(document.getElementById("category-parent"), true);
  fillCategorySelect(document.getElementById("product-category"), false);
  fillSelect(document.getElementById("product-brand"), catalogBrands);
  renderCatalogReferenceLists();
}

function renderCatalogReferenceLists() {
  const categoryList = document.getElementById("category-list");
  const brandList = document.getElementById("brand-list");
  if (categoryList) {
    categoryList.innerHTML = renderCategoryTree(categoryTree);
    categoryList.querySelectorAll("[data-category-id]").forEach((button) => {
      button.addEventListener("click", () => {
        const category = catalogCategories.find((value) => value.id === button.dataset.categoryId);
        if (category) {
          document.getElementById("category-id").value = category.id;
          document.getElementById("category-name").value = category.name;
          document.getElementById("category-parent").value = category.parentId || "";
          document.getElementById("category-submit").textContent = "Update category";
          document.getElementById("category-mode").textContent = `Editing ${category.name}`;
          clearFormError("category-error");
          document.getElementById("category-name").focus();
        }
      });
    });
  }
  if (brandList) {
    brandList.innerHTML = catalogBrands.map((brand) => `<button class="chip" type="button" data-brand-id="${escapeHtml(brand.id)}">Edit ${escapeHtml(brand.name)}</button>`).join("");
    brandList.querySelectorAll("[data-brand-id]").forEach((button) => {
      button.addEventListener("click", () => {
        const brand = catalogBrands.find((value) => value.id === button.dataset.brandId);
        if (brand) {
          document.getElementById("brand-id").value = brand.id;
          document.getElementById("brand-name").value = brand.name;
          document.getElementById("brand-submit").textContent = "Update brand";
          document.getElementById("brand-mode").textContent = `Editing ${brand.name}`;
          clearFormError("brand-error");
          document.getElementById("brand-name").focus();
        }
      });
    });
  }
}

function renderCategoryTree(nodes, depth = 0) {
  if (nodes.length === 0) {
    return depth === 0 ? `<p class="muted-text">No categories</p>` : "";
  }
  return nodes.map((node) => `
    <div class="tree-row" style="--depth:${depth}">
      <button class="chip" type="button" data-category-id="${escapeHtml(node.id)}">Edit ${escapeHtml(node.name)}</button>
    </div>
    ${renderCategoryTree(node.children || [], depth + 1)}
  `).join("");
}

function fillCategorySelect(select, includeEmpty) {
  const current = select.value;
  const options = [];
  flattenCategoryOptions(categoryTree, options);
  select.innerHTML = includeEmpty ? `<option value="">None</option>` : "";
  select.innerHTML += options.map((item) => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.label)}</option>`).join("");
  if ([...select.options].some((option) => option.value === current)) {
    select.value = current;
  }
}

function flattenCategoryOptions(nodes, output, depth = 0) {
  for (const node of nodes) {
    output.push({ id: node.id, label: `${"  ".repeat(depth)}${node.name}` });
    flattenCategoryOptions(node.children || [], output, depth + 1);
  }
}

function fillSelect(select, items) {
  const current = select.value;
  select.innerHTML = items.map((item) => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.name)}</option>`).join("");
  if ([...select.options].some((option) => option.value === current)) {
    select.value = current;
  }
}

async function loadCatalogProducts() {
  const tbody = document.getElementById("catalog-products");
  const count = document.getElementById("catalog-count");
  const searchInput = document.getElementById("catalog-search");
  const includeInactiveInput = document.getElementById("catalog-include-inactive");
  if (!tbody || !count || !searchInput || !includeInactiveInput) {
    return;
  }
  const canWrite = isSystemAdminRole(getAuth()?.user.role);
  const search = searchInput.value.trim();
  const includeInactive = includeInactiveInput.checked;
  const params = new URLSearchParams({ page: "1", pageSize: "50", includeInactive: String(includeInactive) });
  if (search) {
    params.set("search", search);
  }

  tbody.innerHTML = `<tr><td colspan="${canWrite ? 7 : 6}">Loading catalog</td></tr>`;
  try {
    const result = await request(`/api/v1/catalog/products?${params}`);
    count.textContent = `${result.totalCount} product${result.totalCount === 1 ? "" : "s"}`;
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="${canWrite ? 7 : 6}">No products found</td></tr>`
      : result.items.map((product) => `
        <tr class="click-row ${product.id === selectedProductId ? "selected-row" : ""}" data-product-id="${escapeHtml(product.id)}">
          <td>${escapeHtml(product.name)}</td><td>${escapeHtml(product.productType)}</td><td>${escapeHtml(product.brandName)}</td>
          <td>${escapeHtml(product.categoryName)}</td><td>${formatPackHint(product)}</td>
          <td><span class="status-pill ${product.isActive ? "status-ok" : "status-muted"}">${product.isActive ? "Active" : "Inactive"}</span></td>
          ${canWrite ? `<td><button class="button secondary table-action" type="button" data-product-edit="${escapeHtml(product.id)}">Edit</button></td>` : ""}
        </tr>`).join("");
    tbody.querySelectorAll("[data-product-id]").forEach((row) => row.addEventListener("click", () => loadCatalogDetail(row.dataset.productId)));
    tbody.querySelectorAll("[data-product-edit]").forEach((button) => button.addEventListener("click", (event) => {
      event.stopPropagation();
      editProductFromList(button.dataset.productEdit);
    }));
  } catch (exception) {
    tbody.innerHTML = `<tr><td colspan="${canWrite ? 7 : 6}">${escapeHtml(getFriendlyApiError(exception))}</td></tr>`;
    count.textContent = "";
  }
}

async function editProductFromList(productId) {
  try {
    const product = await request(`/api/v1/catalog/products/${productId}`);
    selectedProductId = productId;
    fillProductForm(product);
    await loadCatalogDetail(productId);
  } catch (exception) {
    notice(getFriendlyApiError(exception), "error");
  }
}

async function loadCatalogDetail(productId) {
  selectedProductId = productId;
  const detail = document.getElementById("catalog-detail");
  const canWrite = isSystemAdminRole(getAuth()?.user.role);
  detail.innerHTML = `<h2>Product detail</h2><p>Loading product</p>`;
  try {
    const product = await request(`/api/v1/catalog/products/${productId}`);
    detail.innerHTML = `
      <div class="section-head">
        <div><h2>${escapeHtml(product.name)}</h2><p class="muted-text">${escapeHtml(product.brandName)} - ${escapeHtml(product.categoryName)}</p></div>
        <div class="inline-actions">
          <span class="status-pill ${product.isActive ? "status-ok" : "status-muted"}">${product.isActive ? "Active" : "Inactive"}</span>
          ${canWrite ? `<button class="button secondary" id="edit-product" type="button">Edit</button><button class="button secondary" id="toggle-product" type="button">${product.isActive ? "Deactivate" : "Reactivate"}</button>` : ""}
        </div>
      </div>
      <div class="detail-grid">
        <div><span>Type</span><strong>${escapeHtml(product.productType)}</strong></div>
        <div><span>Sell mode</span><strong>${escapeHtml(product.sellMode || "Not set")}</strong></div>
        <div><span>Pieces per pack</span><strong>${escapeHtml(product.piecesPerPack || "Not set")}</strong></div>
        <div><span>Expiry</span><strong>${escapeHtml(product.expiryType || "Not set")}</strong></div>
        <div><span>Opening validity</span><strong>${escapeHtml(formatOpeningValidity(product))}</strong></div>
      </div>
      <p class="muted-text">Batch expiry dates on inventory batches control FEFO, sales, transfers, and opened-piece expiry.</p>
      ${renderSkuSection(product, canWrite)}`;
    if (canWrite) {
      wireProductAdminActions(product);
    }
    await loadCatalogProducts();
  } catch (exception) {
    detail.innerHTML = `<h2>Product detail</h2><p>${escapeHtml(getFriendlyApiError(exception))}</p>`;
  }
}

function formatOpeningValidity(product) {
  const opened = product.openedExpiryDuration || product.sealedExpiryDuration;
  const rate = product.openedExpiryRate;
  if (opened && rate) return `${opened} (${rate})`;
  return opened || rate || "Not set";
}

function renderSkuSection(product, canWrite) {
  return `
    <h3>SKUs</h3>
    ${canWrite ? `
      <form class="form wide-form compact-form" id="sku-form">
        <input type="hidden" id="sku-id"><div class="form-error" id="sku-error" hidden></div>
        <div class="form-grid">
          <div class="sku-preview"><span>Generated SKU</span><strong id="sku-code-preview">Derived after save</strong></div>
          <div class="field"><label for="sku-power-sign">Power sign</label><select id="sku-power-sign" class="select"><option value="">None</option><option value="+">+</option><option value="-">-</option></select></div>
          <div class="field"><label for="sku-power-value">Power value</label><input id="sku-power-value" class="input" type="number" step="0.25" min="0"></div>
          <div class="field"><label for="sku-color">Color</label><input id="sku-color" class="input"></div>
          <div class="field"><label for="sku-size">Size</label><input id="sku-size" class="input"></div>
          <div class="field"><label for="sku-barcode">Barcode</label><input id="sku-barcode" class="input"></div>
        </div>
        <div class="form-actions"><button class="button" type="submit">Save SKU</button><button class="button secondary" id="sku-reset" type="button">Clear</button></div>
      </form>` : ""}
    <div class="table-wrap"><table><thead><tr><th>SKU</th><th>Power</th><th>Color</th><th>Size</th><th>Barcode</th><th>Status</th>${canWrite ? "<th>Actions</th>" : ""}</tr></thead><tbody>
      ${product.skus.length === 0 ? `<tr><td colspan="${canWrite ? 7 : 6}">No SKUs</td></tr>` : product.skus.map((sku) => `
        <tr><td>${escapeHtml(sku.skuCode)}</td><td>${escapeHtml(formatPower(sku))}</td><td>${escapeHtml(sku.colorName || "-")}</td><td>${escapeHtml(sku.size || "-")}</td><td>${escapeHtml(sku.barcode || "-")}</td>
        <td><span class="status-pill ${sku.isActive ? "status-ok" : "status-muted"}">${sku.isActive ? "Active" : "Inactive"}</span></td>
        ${canWrite ? `<td><button class="button secondary table-action" type="button" data-edit-sku="${escapeHtml(sku.id)}">Edit</button><button class="button secondary table-action" type="button" data-toggle-sku="${escapeHtml(sku.id)}">${sku.isActive ? "Deactivate" : "Reactivate"}</button></td>` : ""}</tr>`).join("")}
    </tbody></table></div>`;
}

function wireProductAdminActions(product) {
  document.getElementById("edit-product").addEventListener("click", () => fillProductForm(product));
  document.getElementById("toggle-product").addEventListener("click", async () => {
    if (await saveCatalogEntity(`/api/v1/catalog/products/${product.id}/${product.isActive ? "deactivate" : "reactivate"}`, "PATCH", null, "Product status updated.")) {
      await loadCatalogDetail(product.id);
    }
  });
  document.getElementById("sku-reset").addEventListener("click", resetSkuForm);
  ["sku-power-sign", "sku-power-value", "sku-color", "sku-size"].forEach((id) => {
    document.getElementById(id).addEventListener("input", () => updateSkuPreview(product));
    document.getElementById(id).addEventListener("change", () => updateSkuPreview(product));
  });
  updateSkuPreview(product);
  document.getElementById("sku-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const validation = validateSkuForm(product.productType);
    if (validation) {
      showFormError("sku-error", validation);
      return;
    }
    const skuId = document.getElementById("sku-id").value;
    const saved = await saveCatalogEntity(skuId ? `/api/v1/catalog/skus/${skuId}` : `/api/v1/catalog/products/${product.id}/skus`, skuId ? "PUT" : "POST", readSkuForm(), "SKU saved.", "sku-error");
    if (saved) {
      resetSkuForm();
      await loadCatalogDetail(product.id);
    }
  });
  document.querySelectorAll("[data-edit-sku]").forEach((button) => button.addEventListener("click", () => {
    const sku = product.skus.find((value) => value.id === button.dataset.editSku);
    if (sku) {
      fillSkuForm(sku);
    }
  }));
  document.querySelectorAll("[data-toggle-sku]").forEach((button) => button.addEventListener("click", async () => {
    const sku = product.skus.find((value) => value.id === button.dataset.toggleSku);
    if (sku && await saveCatalogEntity(`/api/v1/catalog/skus/${sku.id}/${sku.isActive ? "deactivate" : "reactivate"}`, "PATCH", null, "SKU status updated.")) {
      await loadCatalogDetail(product.id);
    }
  }));
}

async function saveCategory(event) {
  event.preventDefault();
  const id = document.getElementById("category-id").value;
  const saved = await saveCatalogEntity(id ? `/api/v1/catalog/categories/${id}` : "/api/v1/catalog/categories", id ? "PUT" : "POST", {
    name: document.getElementById("category-name").value,
    parentId: document.getElementById("category-parent").value || null
  }, "Category saved.", "category-error");
  if (saved) {
    resetCategoryForm();
    await loadCatalogLookups();
  }
}

async function saveBrand(event) {
  event.preventDefault();
  const id = document.getElementById("brand-id").value;
  const saved = await saveCatalogEntity(id ? `/api/v1/catalog/brands/${id}` : "/api/v1/catalog/brands", id ? "PUT" : "POST", {
    name: document.getElementById("brand-name").value
  }, "Brand saved.", "brand-error");
  if (saved) {
    resetBrandForm();
    await loadCatalogLookups();
  }
}

async function saveProduct(event) {
  event.preventDefault();
  const validation = validateProductForm();
  if (validation) {
    showFormError("product-error", validation);
    return;
  }
  const id = document.getElementById("product-id").value;
  const saved = await saveCatalogEntity(id ? `/api/v1/catalog/products/${id}` : "/api/v1/catalog/products", id ? "PUT" : "POST", readProductForm(), "Product saved.", "product-error");
  if (saved) {
    resetProductForm();
    await loadCatalogProducts();
    if (id) {
      await loadCatalogDetail(id);
    }
  }
}

async function saveCatalogEntity(path, method, payload, successMessage, errorId) {
  clearFormError(errorId);
  try {
    await request(path, { method, body: payload === null ? undefined : JSON.stringify(payload) });
    notice(successMessage, "success");
    return true;
  } catch (exception) {
    const message = getFriendlyCatalogWriteError(exception);
    if (errorId) {
      showFormError(errorId, message);
    }
    notice(message, "error");
    return false;
  }
}

function validateProductForm() {
  const name = document.getElementById("product-name").value.trim();
  const type = document.getElementById("product-type").value;
  const category = document.getElementById("product-category").value;
  const brand = document.getElementById("product-brand").value;
  const pieces = Number(document.getElementById("product-pieces").value || 0);
  if (!name || !category || !brand) {
    return "Product name, category, and brand are required.";
  }
  if (pieces <= 0) {
    return "Pieces per pack must be greater than zero.";
  }
  return validateJson(buildClinicalParamsFromForm(), "Clinical params");
}

function validateSkuForm(productType) {
  const color = document.getElementById("sku-color").value.trim();
  const size = document.getElementById("sku-size").value.trim();
  if (productType !== "Solution" && !color) {
    return "Color is required for lens SKUs.";
  }
  if (productType === "Solution" && !size) {
    return "Size is required for solution SKUs.";
  }
  return null;
}

function validateJson(value, label) {
  if (!value) {
    return null;
  }
  try {
    JSON.parse(value);
    return null;
  } catch {
    return `${label} must be valid JSON.`;
  }
}

function readProductForm() {
  const type = document.getElementById("product-type").value;
  const pieces = document.getElementById("product-pieces").value;
  const clinicalParams = buildClinicalParamsFromForm();
  const durationValue = document.getElementById("product-duration-value")?.value;
  const openedExpiryRate = document.getElementById("product-duration-unit")?.value || null;
  return {
    categoryId: document.getElementById("product-category").value,
    brandId: document.getElementById("product-brand").value,
    name: document.getElementById("product-name").value,
    productType: type,
    expiryType: document.getElementById("product-expiry").value,
    sealedExpiryDuration: null,
    openedExpiryRate: type === "Solution" ? null : openedExpiryRate,
    openedExpiryDuration: type === "Solution" || !durationValue ? null : buildDuration(durationValue, openedExpiryRate),
    piecesPerPack: pieces ? Number(pieces) : null,
    sellMode: document.getElementById("product-sell-mode").value,
    clinicalParams,
    extendedAttributes: null
  };
}

function parseDurationAmount(duration) {
  const match = String(duration || "").trim().match(/^([1-9][0-9]*)\s+(day|days|month|months|year|years)$/i);
  return match ? match[1] : "";
}

function parseDurationRate(duration) {
  const match = String(duration || "").trim().match(/^[1-9][0-9]*\s+(day|days|month|months|year|years)$/i);
  const unit = match ? match[1].toLowerCase() : "";
  if (unit.startsWith("day")) {
    return "Daily";
  }
  if (unit.startsWith("year")) {
    return "Annual";
  }
  return "Monthly";
}

function extractClinicalDurationUnit(clinicalParams) {
  if (!clinicalParams) {
    return "";
  }

  try {
    const parsed = JSON.parse(clinicalParams);
    const duration = String(parsed.duration || "").toLowerCase();
    if (duration.startsWith("day")) {
      return "Daily";
    }
    if (duration.startsWith("year")) {
      return "Annual";
    }
    if (duration.startsWith("month")) {
      return "Monthly";
    }
  } catch {
    return "";
  }

  return "";
}

function buildDuration(amount, rate) {
  const value = Number(amount);
  const unit = rate === "Daily"
    ? value === 1 ? "day" : "days"
    : rate === "Annual"
      ? value === 1 ? "year" : "years"
      : value === 1 ? "month" : "months";
  return `${value} ${unit}`;
}

function fillProductForm(product) {
  document.getElementById("product-id").value = product.id;
  document.getElementById("product-name").value = product.name;
  document.getElementById("product-type").value = product.productType;
  document.getElementById("product-category").value = product.categoryId;
  document.getElementById("product-brand").value = product.brandId;
  document.getElementById("product-sell-mode").value = product.sellMode || "SinglePiece";
  document.getElementById("product-pieces").value = product.piecesPerPack || "";
  document.getElementById("product-expiry").value = product.expiryType || "Batch";
  const durationValue = document.getElementById("product-duration-value");
  const durationUnit = document.getElementById("product-duration-unit");
  const clinical = document.getElementById("product-clinical");
  if (durationValue) {
    durationValue.value = extractClinicalDurationAmount(product.clinicalParams) || "6";
  }
  if (durationUnit) {
    durationUnit.value = extractClinicalDurationUnit(product.clinicalParams) || "Monthly";
  }
  if (clinical) {
    clinical.value = product.clinicalParams || "";
  }
  document.getElementById("product-submit").textContent = "Update product";
  document.getElementById("product-mode").textContent = `Editing ${product.name}`;
  syncProductTypeFields();
  document.getElementById("product-name").focus();
}

function resetProductForm() {
  document.getElementById("product-id").value = "";
  document.getElementById("product-name").value = "";
  document.getElementById("product-type").value = "Lens";
  document.getElementById("product-sell-mode").value = "SinglePiece";
  document.getElementById("product-pieces").value = "1";
  document.getElementById("product-expiry").value = "Batch";
  const durationValue = document.getElementById("product-duration-value");
  const durationUnit = document.getElementById("product-duration-unit");
  const clinical = document.getElementById("product-clinical");
  if (durationValue) {
    durationValue.value = "6";
  }
  if (durationUnit) {
    durationUnit.value = "Monthly";
  }
  if (clinical) {
    clinical.value = buildClinicalParamsFromForm();
  }
  document.getElementById("product-submit").textContent = "Create product";
  document.getElementById("product-mode").textContent = "New product";
  clearFormError("product-error");
  syncProductTypeFields();
}

function readSkuForm() {
  const powerValue = document.getElementById("sku-power-value").value;
  return {
    powerSign: document.getElementById("sku-power-sign").value || null,
    powerValue: powerValue ? Number(powerValue) : null,
    colorName: document.getElementById("sku-color").value || null,
    size: document.getElementById("sku-size").value || null,
    barcode: document.getElementById("sku-barcode").value || null
  };
}

function fillSkuForm(sku) {
  document.getElementById("sku-id").value = sku.id;
  document.getElementById("sku-code-preview").textContent = sku.skuCode;
  document.getElementById("sku-power-sign").value = sku.powerSign || "";
  document.getElementById("sku-power-value").value = sku.powerValue ?? "";
  document.getElementById("sku-color").value = sku.colorName || "";
  document.getElementById("sku-size").value = sku.size || "";
  document.getElementById("sku-barcode").value = sku.barcode || "";
  document.getElementById("sku-power-sign").focus();
}

function resetSkuForm() {
  ["sku-id", "sku-power-sign", "sku-power-value", "sku-color", "sku-size", "sku-barcode"].forEach((id) => {
    document.getElementById(id).value = "";
  });
  const preview = document.getElementById("sku-code-preview");
  if (preview) {
    preview.textContent = "Derived after save";
  }
  clearFormError("sku-error");
}

function updateSkuPreview(product) {
  const preview = document.getElementById("sku-code-preview");
  if (!preview) {
    return;
  }

  preview.textContent = generateSkuPreview(product, readSkuForm());
}

function generateSkuPreview(product, sku) {
  const brand = toBrandCode(product.brandName);
  const category = toCategoryCode(product.categoryName);
  if (product.productType === "Solution") {
    return joinSkuParts(brand, category, toCode(sku.size, 8));
  }

  return joinSkuParts(
    brand,
    category,
    formatSkuPower(sku.powerSign, sku.powerValue),
    toCode(sku.colorName, 12),
    toOptionalCode(sku.size, 8),
    toOpenedExpiryDurationCode(product.openedExpiryDuration),
    toOptionalCode(product.openedExpiryRate, 12));
}

function toBrandCode(value) {
  const parts = String(value || "")
    .split(/[ \/_-]+/)
    .filter((part) => part && !["and", "of"].includes(part.toLowerCase()));
  if (parts.length > 1) {
    return toCode(parts.map((part) => part[0]).join(""), 3);
  }

  return toCode(value, 3);
}

function toCategoryCode(value) {
  const parts = String(value || "")
    .split(/[ \/_-]+/)
    .filter((part) => part && !["and", "of"].includes(part.toLowerCase()));
  if (parts.length > 1) {
    return toCode(parts.map((part) => part[0]).join(""), 3);
  }

  return toCode(value, 3);
}

function toCode(value, maxLength) {
  const code = String(value || "NA")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-z0-9]/gi, "")
    .toUpperCase();

  return (code || "NA").slice(0, maxLength);
}

function toOptionalCode(value, maxLength) {
  return String(value || "").trim() ? toCode(value, maxLength) : "";
}

function toOpenedExpiryDurationCode(value) {
  const parts = String(value || "").trim().split(/\s+/);
  const amount = Number.parseInt(parts[0], 10);
  if (!Number.isFinite(amount) || parts.length < 2) {
    return toOptionalCode(value, 8);
  }

  const unit = parts[1].toLowerCase();
  const prefix = unit === "day" || unit === "days" ? "D" : unit === "month" || unit === "months" ? "M" : unit === "year" || unit === "years" ? "Y" : "";
  return prefix ? `${prefix}${String(amount).padStart(2, "0")}` : toOptionalCode(value, 8);
}

function formatSkuPower(sign, value) {
  if (value === null || value === undefined || value === "") {
    return "P0";
  }

  return `${sign === "-" ? "M" : "P"}${String(Number(value).toFixed(2)).replace(/\.?0+$/, "").replace(".", "")}`;
}

function joinSkuParts(...parts) {
  return parts.filter(Boolean).join("-");
}

function resetCategoryForm() {
  document.getElementById("category-id").value = "";
  document.getElementById("category-name").value = "";
  document.getElementById("category-parent").value = "";
  document.getElementById("category-submit").textContent = "Create category";
  document.getElementById("category-mode").textContent = "New category";
  clearFormError("category-error");
}

function resetBrandForm() {
  document.getElementById("brand-id").value = "";
  document.getElementById("brand-name").value = "";
  document.getElementById("brand-submit").textContent = "Create brand";
  document.getElementById("brand-mode").textContent = "New brand";
  clearFormError("brand-error");
}

function syncProductTypeFields() {
  const type = document.getElementById("product-type")?.value;
  const durationValue = document.getElementById("product-duration-value");
  const durationUnit = document.getElementById("product-duration-unit");
  const clinical = document.getElementById("product-clinical");
  if (!clinical || !durationUnit || !durationValue) {
    return;
  }
  clinical.value = buildClinicalParamsFromForm();
  durationUnit.disabled = type === "Solution";
  durationValue.disabled = type === "Solution";
}

function buildClinicalParamsFromForm() {
  const type = document.getElementById("product-type")?.value;
  const durationValue = Number(document.getElementById("product-duration-value")?.value || 0);
  const durationUnit = document.getElementById("product-duration-unit")?.value || "Monthly";
  if (type === "Solution") {
    return null;
  }
  if (durationValue <= 0) {
    return null;
  }
  const amount = durationValue === 1 ? "1" : String(durationValue);
  const duration = durationUnit === "Daily" ? "daily" : durationUnit === "Annual" ? "annually" : "monthly";
  return JSON.stringify({ duration: `${amount} ${duration}` });
}

function extractClinicalDurationAmount(clinicalParams) {
  if (!clinicalParams) {
    return "";
  }

  try {
    const parsed = JSON.parse(clinicalParams);
    const duration = String(parsed.duration || "").trim();
    const match = duration.match(/^([1-9][0-9]*)\s+(day|days|month|months|year|years)$/i);
    return match ? match[1] : "";
  } catch {
    return "";
  }
}

function showFormError(id, message) {
  const element = document.getElementById(id);
  if (!element) {
    return;
  }
  element.textContent = message;
  element.hidden = false;
}

function clearFormError(id) {
  if (!id) {
    return;
  }
  const element = document.getElementById(id);
  if (element) {
    element.hidden = true;
    element.textContent = "";
  }
}

function renderInventory() {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  document.getElementById("view").innerHTML = `
    <section class="catalog-hero">
      <div>
        <p class="eyebrow">Inventory</p>
        <h2>Stock, batches, and replenishment</h2>
        <p>Monitor available stock, reserved stock, replenishment gaps, blocked expiry batches, and the immutable stock ledger.</p>
      </div>
      <div class="scenario-grid">
        ${scenarioCard("Role", canWrite ? "Can adjust targets" : "Read only", canWrite ? "status-ok" : "status-muted")}
        ${scenarioCard("Scope", auth?.user.locationId ? "Assigned location" : "All available locations", "status-muted")}
        ${scenarioCard("Ledger model", "Append-only stock history", "status-ok")}
      </div>
    </section>

    <section class="catalog-layout">
      <aside class="catalog-side">
        <section class="band compact-band">
          <div class="section-head"><h2>Filters</h2><button id="inventory-refresh" class="button secondary" type="button">Refresh</button></div>
          <div class="field"><label for="inventory-location">Location</label><select id="inventory-location" class="select"><option value="">All available</option></select></div>
          <div class="field inventory-sku-picker"><label for="inventory-sku-search">SKU</label>
            <input id="inventory-sku" type="hidden" value="">
            <input id="inventory-sku-search" class="input" type="search" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="inventory-sku-results" placeholder="Search product, color, power, or SKU">
            <div id="inventory-sku-results" class="op-line-search-results" role="listbox" hidden></div>
          </div>
          <label class="check-field"><input id="inventory-include-zero-stock" type="checkbox"><span>Show zero-stock SKUs</span></label>
          <label class="check-field"><input id="inventory-include-empty" type="checkbox"><span>Show empty batches</span></label>
        </section>
        <section class="band compact-band">
          <h2>Locations</h2>
          <div id="inventory-locations" class="reference-list"><span class="muted-text">Loading</span></div>
        </section>
      </aside>

      <section class="catalog-main">
        <section class="band">
          <div class="section-head"><h2>Product totals</h2><span id="inventory-product-total-count" class="muted-text">Loading</span></div>
          <div class="table-wrap"><table><thead><tr><th>Product</th><th>SKU count</th><th>Total packs</th><th>Total pieces</th><th>Breakdown</th></tr></thead><tbody id="inventory-product-totals"><tr><td colspan="5">Loading product totals</td></tr></tbody></table></div>
        </section>
        <section class="band">
          <div class="section-head"><h2>Stock balances</h2><span id="inventory-balance-count" class="muted-text">Loading</span></div>
          <div class="table-wrap"><table><thead><tr><th>Location</th><th>SKU</th><th>Available</th><th>Reserved</th><th>Meant to be</th><th>Needed</th><th>Status</th><th>Updated</th>${canWrite ? "<th>Actions</th>" : ""}</tr></thead><tbody id="inventory-balances"><tr><td colspan="${canWrite ? 9 : 8}">Loading stock</td></tr></tbody></table></div>
        </section>
        <section class="band">
          <div class="section-head">
            <div><h2>Daily replenishment</h2><p>Online and retail targets are topped up from MainWarehouse through Draft warehouse transfers awaiting confirmation.</p></div>
            <div class="inline-actions">
              <span id="inventory-replenishment-count" class="muted-text">Loading</span>
              ${canWrite ? `<button id="reserve-replenishment" class="button secondary" type="button">Run replenishment</button>` : ""}
            </div>
          </div>
          <div class="table-wrap"><table><thead><tr><th>Destination</th><th>SKU</th><th>Available</th><th>Incoming</th><th>Meant to be</th><th>Needed</th><th>Main available</th></tr></thead><tbody id="inventory-replenishment"><tr><td colspan="7">Loading replenishment</td></tr></tbody></table></div>
        </section>
        <section class="band">
          <div class="section-head">
            <div><h2>Expired batches</h2><p>Expired batches are blocked from FEFO sale, transfer, reserve, and write-off allocation.</p></div>
            <span id="inventory-blocked-count" class="muted-text">Loading</span>
          </div>
          <div class="table-wrap"><table><thead><tr><th>Location</th><th>SKU</th><th>Lot</th><th>Quantity</th><th>Expiry</th><th>Reason</th></tr></thead><tbody id="inventory-blocked-batches"><tr><td colspan="6">Loading expired batches</td></tr></tbody></table></div>
        </section>
        <section class="catalog-detail-grid">
          <section class="band">
            <div class="section-head"><h2>Batches</h2><span id="inventory-batch-count" class="muted-text">Loading</span></div>
            <div class="table-wrap"><table><thead><tr><th>Lot</th><th>Location</th><th>SKU</th><th>Quantity</th><th>Expiry date</th><th>Notes</th></tr></thead><tbody id="inventory-batches"><tr><td colspan="6">Loading batches</td></tr></tbody></table></div>
          </section>
          <section class="band">
            <div class="section-head"><h2>Transactions</h2><span id="inventory-transaction-count" class="muted-text">Loading</span></div>
            <div class="table-wrap"><table><thead><tr><th>Type</th><th>Location</th><th>SKU</th><th>Change</th><th>Created</th></tr></thead><tbody id="inventory-transactions"><tr><td colspan="5">Loading transactions</td></tr></tbody></table></div>
          </section>
        </section>
      </section>
    </section>`;

  document.getElementById("inventory-refresh").addEventListener("click", refreshInventoryWorkspace);
  document.getElementById("inventory-location").addEventListener("change", () => {
    selectedInventoryLocationId = document.getElementById("inventory-location").value;
    refreshInventoryTables();
  });
  document.getElementById("inventory-sku-search").addEventListener("input", () => {
    const search = document.getElementById("inventory-sku-search");
    const filter = document.getElementById("inventory-sku");
    const selected = inventorySkuOptions.find((sku) => sku.id === filter.value);
    if (!search.value.trim() && filter.value) {
      clearInventorySkuFilter();
      return;
    }
    if (selected && search.value !== selected.label) {
      filter.value = "";
      refreshInventoryTables();
    }
    renderInventorySkuSearchResults();
  });
  document.getElementById("inventory-sku-search").addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      hideInventorySkuSearchResults();
      event.currentTarget.blur();
    }
  });
  document.getElementById("inventory-include-zero-stock").addEventListener("change", loadInventoryBalances);
  document.getElementById("inventory-include-empty").addEventListener("change", loadInventoryBatches);
  document.getElementById("reserve-replenishment")?.addEventListener("click", reserveInventoryReplenishment);
  refreshInventoryWorkspace();
}

async function refreshInventoryWorkspace() {
  if (!document.getElementById("inventory-balances")) {
    return;
  }
  await Promise.all([loadInventoryLocations(), loadInventorySkuOptions()]);
  await refreshInventoryTables();
}

async function refreshInventoryTables() {
  if (!document.getElementById("inventory-balances")) {
    return;
  }
  await Promise.all([
    loadInventoryProductTotals(),
    loadInventoryBalances(),
    loadInventoryReplenishment(),
    loadTransferBlockedBatches(),
    loadInventoryBatches(),
    loadInventoryTransactions()
  ]);
}

async function loadInventoryLocations() {
  const select = document.getElementById("inventory-location");
  const list = document.getElementById("inventory-locations");
  try {
    inventoryLocations = await request("/api/v1/inventory/locations");
    select.innerHTML = `<option value="">All available</option>${inventoryLocations.map((location) => `<option value="${escapeHtml(location.id)}">${escapeHtml(location.name)}</option>`).join("")}`;
    if (getAuth()?.user.locationId && !selectedInventoryLocationId) {
      selectedInventoryLocationId = getAuth().user.locationId;
    }
    select.value = selectedInventoryLocationId;
    select.disabled = Boolean(getAuth()?.user.locationId);
    list.innerHTML = inventoryLocations.length === 0
      ? `<span class="muted-text">No locations</span>`
      : inventoryLocations.map((location) => `<button class="reference-item" type="button" data-location-id="${escapeHtml(location.id)}"><strong>${escapeHtml(location.name)}</strong><span>${escapeHtml(location.locationType)} ${location.isActive ? "Active" : "Inactive"}</span></button>`).join("");
    list.querySelectorAll("[data-location-id]").forEach((button) => button.addEventListener("click", () => {
      selectedInventoryLocationId = button.dataset.locationId;
      select.value = selectedInventoryLocationId;
      refreshInventoryTables();
    }));
  } catch (exception) {
    list.innerHTML = `<span class="muted-text">${escapeHtml(getFriendlyInventoryError(exception))}</span>`;
  }
}

async function loadInventorySkuOptions() {
  const filter = document.getElementById("inventory-sku");
  const search = document.getElementById("inventory-sku-search");
  try {
    const products = [];
    let page = 1;
    let totalCount = 0;
    do {
      const result = await request(`/api/v1/catalog/products?includeInactive=false&page=${page}&pageSize=100`);
      products.push(...(result.items || []));
      totalCount = result.totalCount || products.length;
      page += 1;
    } while (products.length < totalCount);

    const options = [];
    for (const product of products) {
      const detail = await request(`/api/v1/catalog/products/${product.id}`);
      for (const sku of detail.skus || []) {
        if (sku.isActive) {
          options.push({
            id: sku.id,
            productName: detail.name,
            brandName: detail.brandName,
            categoryName: detail.categoryName,
            skuCode: sku.skuCode,
            powerSign: sku.powerSign,
            powerValue: sku.powerValue,
            colorName: sku.colorName,
            size: sku.size,
            label: `${sku.skuCode} - ${detail.name}`
          });
        }
      }
    }
    inventorySkuOptions = options.sort((left, right) => left.label.localeCompare(right.label));
    if (filter) {
      const current = filter.value;
      filter.value = inventorySkuOptions.some((sku) => sku.id === current) ? current : "";
      updateInventorySkuSearchLabel();
    }
  } catch (exception) {
    if (filter) {
      filter.value = "";
    }
    if (search) {
      search.value = "";
      search.placeholder = "Catalog unavailable";
    }
  }
}

function merchantRecallReturnDialog(locations, recall) {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.className = "dialog-overlay";
    overlay.innerHTML = `
      <form class="dialog-card" novalidate>
        <div class="section-head tight-head"><div><h2>Start merchant return</h2><p class="muted-text">${escapeHtml(recall.merchantName)} / ${escapeHtml(recall.skuCode || recall.productName || "SKU")}</p></div></div>
        <div class="field"><label>Receiving location</label><select class="select" data-recall-location required><option value="">Select a location</option>${locations.filter((location) => location.isActive !== false).map((location) => `<option value="${escapeHtml(location.id)}">${escapeHtml(location.name)}</option>`).join("")}</select></div>
        <div class="field"><label>Physical quantity</label><input class="input" data-recall-quantity type="number" min="1" step="1" required></div>
        <div class="field"><label>Notes</label><textarea class="input" data-recall-notes rows="3"></textarea></div>
        <div class="form-error" data-recall-error hidden></div>
        <div class="form-actions"><button class="button primary" type="submit">Create return draft</button><button class="button secondary" type="button" data-dialog-cancel>Cancel</button></div>
      </form>`;
    document.body.appendChild(overlay);
    const close = (value) => { overlay.remove(); resolve(value); };
    overlay.querySelector("[data-dialog-cancel]").addEventListener("click", () => close(null));
    overlay.addEventListener("click", (event) => { if (event.target === overlay) close(null); });
    overlay.querySelector("form").addEventListener("submit", (event) => {
      event.preventDefault();
      const receivingLocationId = overlay.querySelector("[data-recall-location]").value;
      const quantity = Number(overlay.querySelector("[data-recall-quantity]").value);
      const notes = overlay.querySelector("[data-recall-notes]").value.trim();
      const error = overlay.querySelector("[data-recall-error]");
      if (!receivingLocationId || !Number.isInteger(quantity) || quantity <= 0) {
        error.textContent = "Select a receiving location and enter a positive whole quantity.";
        error.hidden = false;
        return;
      }
      close({ receivingLocationId, quantity, notes: notes || null });
    });
    overlay.querySelector("[data-recall-location]").focus();
  });
}

function parseMerchantSalesVarianceGate(exception) {
  if (exception?.status !== 409 || !(exception instanceof Error)) return null;
  try {
    const body = JSON.parse(exception.message || "");
    return body?.code === "MerchantSalesVariance" && Array.isArray(body.warnings) ? body : null;
  } catch {
    return null;
  }
}

function merchantSalesVarianceDialog(gate) {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.className = "dialog-overlay";
    const warningRows = (gate.warnings || []).map((warning) => `
      <tr>
        <td><strong>${escapeHtml(warning.skuCode || "SKU")}</strong><div class="muted-cell">${escapeHtml(warning.productName || "-")}</div></td>
        <td>${escapeHtml(warning.lotNumber || "-")}</td>
        <td>${escapeHtml(warning.expiryDate || "-")}</td>
        <td>${escapeHtml(String(warning.soldQuantity ?? 0))}</td>
        <td>${escapeHtml(String(warning.returnedQuantity ?? 0))}</td>
        <td>${escapeHtml(String(warning.requestedQuantity ?? 0))}</td>
        <td><strong>${escapeHtml(String(warning.excessQuantity ?? 0))}</strong></td>
      </tr>`).join("");
    overlay.innerHTML = `
      <form class="dialog-card confirm-dialog confirm-dialog-warning sales-variance-dialog" role="dialog" aria-modal="true" aria-labelledby="merchant-sales-variance-title">
        <div class="section-head tight-head"><div><h2 id="merchant-sales-variance-title">${escapeHtml(gate.title || "Recorded sales warning")}</h2><p class="muted-text">${escapeHtml(gate.detail || "Review the recorded merchant sales before continuing.")}</p></div></div>
        <div class="table-wrap sales-variance-dialog-table"><table><thead><tr><th>SKU</th><th>Lot</th><th>Expiry</th><th>Sold to merchant</th><th>Already returned</th><th>Requested now</th><th>Above recorded balance</th></tr></thead><tbody>${warningRows}</tbody></table></div>
        ${gate.canBypass ? `
          <div class="field"><label for="merchant-sales-variance-reason">Exception reason</label><textarea id="merchant-sales-variance-reason" class="input" rows="3" maxlength="500" placeholder="Explain why this return should continue" required></textarea></div>
          <div class="form-error" data-variance-error hidden></div>` : `<p class="form-error">This account can review the warning but cannot bypass it.</p>`}
        <div class="form-actions">
          ${gate.canBypass ? `<button class="button primary" type="submit" data-dialog-confirm>Confirm with exception</button>` : ""}
          <button class="button secondary" type="button" data-dialog-cancel>${gate.canBypass ? "Cancel" : "Close"}</button>
        </div>
      </form>`;
    document.body.appendChild(overlay);
    applyLanguage();
    const close = (value) => { overlay.remove(); resolve(value); };
    overlay.querySelector("[data-dialog-cancel]").addEventListener("click", () => close(null));
    overlay.addEventListener("click", (event) => { if (event.target === overlay) close(null); });
    if (gate.canBypass) {
      const reason = overlay.querySelector("#merchant-sales-variance-reason");
      reason.focus();
      overlay.querySelector("form").addEventListener("submit", (event) => {
        event.preventDefault();
        const value = reason.value.trim();
        const error = overlay.querySelector("[data-variance-error]");
        if (!value) {
          error.textContent = "Exception reason is required.";
          error.hidden = false;
          applyLanguage();
          return;
        }
        close({ acknowledgeSalesVariance: true, salesVarianceReason: value });
      });
    }
  });
}

function inventorySkuSearchHaystack(sku) {
  return `${sku.productName || ""} ${sku.brandName || ""} ${sku.categoryName || ""} ${sku.skuCode || ""} ${formatOperationPowerKey(operationPowerKey(sku))} ${sku.colorName || ""} ${sku.size || ""}`.toLowerCase();
}

function updateInventorySkuSearchLabel() {
  const filter = document.getElementById("inventory-sku");
  const search = document.getElementById("inventory-sku-search");
  if (!filter || !search) {
    return;
  }

  const selected = inventorySkuOptions.find((sku) => sku.id === filter.value);
  search.value = selected ? selected.label : "";
}

function hideInventorySkuSearchResults() {
  const results = document.getElementById("inventory-sku-results");
  const search = document.getElementById("inventory-sku-search");
  if (results) {
    results.hidden = true;
    results.innerHTML = "";
  }
  search?.setAttribute("aria-expanded", "false");
}

function clearInventorySkuFilter() {
  const filter = document.getElementById("inventory-sku");
  const search = document.getElementById("inventory-sku-search");
  if (filter) filter.value = "";
  if (search) search.value = "";
  hideInventorySkuSearchResults();
  refreshInventoryTables();
}

function renderInventorySkuSearchResults() {
  const filter = document.getElementById("inventory-sku");
  const search = document.getElementById("inventory-sku-search");
  const results = document.getElementById("inventory-sku-results");
  if (!filter || !search || !results) {
    return;
  }

  const query = search.value.trim().toLowerCase();
  if (!query) {
    hideInventorySkuSearchResults();
    return;
  }

  const terms = query.split(/\s+/).filter(Boolean);
  const matches = inventorySkuOptions
    .filter((sku) => terms.every((term) => inventorySkuSearchHaystack(sku).includes(term)))
    .slice(0, 8);
  setupAdaptiveSearchResultDismissal();
  collapseAdaptiveSearchResults(results);
  results.hidden = false;
  search.setAttribute("aria-expanded", "true");
  results.innerHTML = matches.length === 0
    ? `<button type="button" class="op-line-search-result" disabled>No matching SKU</button>`
    : matches.map((sku) => `
        <button type="button" class="op-line-search-result" role="option" data-inventory-sku-id="${escapeHtml(sku.id)}">
          <strong>${escapeHtml(sku.productName)}</strong>
          <span>${escapeHtml(formatOperationPowerKey(operationPowerKey(sku)))} / ${escapeHtml(sku.colorName || "-")} / ${escapeHtml(sku.size || "-")}</span>
          <small>${escapeHtml(sku.skuCode)}</small>
        </button>`).join("");
  results.querySelectorAll("[data-inventory-sku-id]").forEach((button) => button.addEventListener("click", () => {
    filter.value = button.dataset.inventorySkuId || "";
    updateInventorySkuSearchLabel();
    hideInventorySkuSearchResults();
    refreshInventoryTables();
  }));
}

async function loadInventoryProductTotals() {
  const tbody = document.getElementById("inventory-product-totals");
  const count = document.getElementById("inventory-product-total-count");
  if (!tbody || !count) {
    return;
  }

  const params = new URLSearchParams();
  if (selectedInventoryLocationId) {
    params.set("locationId", selectedInventoryLocationId);
  }

  try {
    const rows = await request(`/api/v1/inventory/product-totals?${params.toString()}`);
    count.textContent = `${rows.length} categor${rows.length === 1 ? "y" : "ies"}`;
    tbody.innerHTML = rows.length === 0
      ? `<tr><td colspan="5">No available stock for this location.</td></tr>`
      : rows.map((row, index) => {
        const detailId = `inventory-product-category-${index}`;
        const products = Array.isArray(row.products) ? row.products : [row];
        return `
        <tr class="product-total-row">
          <td><strong>${escapeHtml(row.categoryName || row.productName || shortId(row.categoryId || row.productId, row.categoryId ? "CAT" : "PRD"))}</strong><span class="muted-cell">${escapeHtml(row.productCount ?? products.length)} product${(row.productCount ?? products.length) === 1 ? "" : "s"}</span></td>
          <td>${escapeHtml(row.skuCount)}</td>
          <td>${escapeHtml(row.totalPacks)}</td>
          <td>${row.totalPieces == null ? "-" : escapeHtml(row.totalPieces)}</td>
          <td><button class="button secondary table-action" type="button" data-product-total-toggle="${escapeHtml(detailId)}" aria-expanded="false">Details</button></td>
        </tr>
        <tr class="product-rate-row" id="${escapeHtml(detailId)}" hidden>
          <td colspan="5">${renderCategoryProductTotals(products)}</td>
        </tr>`;
      }).join("");
    tbody.querySelectorAll("[data-product-total-toggle]").forEach((button) => {
      button.addEventListener("click", () => toggleProductTotalDetails(button));
    });
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="5">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

function renderCategoryProductTotals(products) {
  return `
    <div class="table-wrap compact-table product-rate-table">
      <table>
        <thead><tr><th>Product</th><th>Opening validity</th><th>Rate</th><th>SKU count</th><th>Total packs</th><th>Total pieces</th></tr></thead>
        <tbody>${products.length === 0
          ? `<tr><td colspan="6">No validity breakdown</td></tr>`
          : products.map((product) => {
            const rate = Array.isArray(product.rateTotals) && product.rateTotals.length === 1
              ? product.rateTotals[0]
              : null;
            return `
            <tr>
              <td><strong>${escapeHtml(product.productName || shortId(product.productId, "PRD"))}</strong></td>
              <td>${escapeHtml(rate?.openedExpiryDuration || rate?.sealedExpiryDuration || "Not set")}</td>
              <td>${escapeHtml(rate?.openedExpiryRate || "Not set")}</td>
              <td>${escapeHtml(product.skuCount)}</td>
              <td>${escapeHtml(product.totalPacks)}</td>
              <td>${product.totalPieces == null ? "-" : escapeHtml(product.totalPieces)}</td>
            </tr>`;
          }).join("")}</tbody>
      </table>
    </div>`;
}

function toggleProductTotalDetails(button) {
  const row = document.getElementById(button.dataset.productTotalToggle);
  if (!row) return;
  const expanded = button.getAttribute("aria-expanded") === "true";
  button.setAttribute("aria-expanded", String(!expanded));
  button.closest(".product-total-row")?.setAttribute("aria-expanded", String(!expanded));
  button.textContent = expanded ? "Details" : "Hide";
  row.hidden = expanded;
}

async function loadInventoryBalances() {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  const tbody = document.getElementById("inventory-balances");
  const count = document.getElementById("inventory-balance-count");
  const includeZeroStock = document.getElementById("inventory-include-zero-stock");
  if (!tbody || !count || !includeZeroStock) {
    return;
  }
  const params = inventoryParams();
  params.set("pageSize", "50");
  params.set("includeZeroStock", String(includeZeroStock.checked));
  try {
    const result = await request(`/api/v1/inventory/stock-balances?${params.toString()}`);
    count.textContent = `${result.totalCount} balance${result.totalCount === 1 ? "" : "s"}`;
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="${canWrite ? 9 : 8}">No stock balances yet.</td></tr>`
      : result.items.map((balance) => `
        <tr>
          <td>${escapeHtml(balance.locationName)}</td>
          <td><strong>${escapeHtml(balance.skuCode || "Unknown SKU")}</strong>${skuStatusBadge(balance.skuIsActive)}<span class="muted-cell">${escapeHtml(balance.productName || shortId(balance.skuId, "SKU"))}</span></td>
          <td>${quantityStack(balance.availablePacks, balance.availablePieces, balance.locationType)}</td>
          <td>${quantityStack(balance.reservedInWarehousePacks + balance.reservedWithRepPacks, addNullable(balance.reservedInWarehousePieces, balance.reservedWithRepPieces), balance.locationType)}</td>
          <td>${quantityStack(balance.targetPacks, balance.targetPieces, balance.locationType)}</td>
          <td>${quantityStack(inventoryShortagePacks(balance), inventoryShortagePieces(balance), balance.locationType)}</td>
          <td>${inventoryStockStatus(balance)}</td>
          <td>${escapeHtml(formatDateTime(balance.lastUpdated))}</td>
          ${canWrite ? `<td><button class="button secondary table-action" type="button" data-target-location="${escapeHtml(balance.locationId)}" data-target-sku="${escapeHtml(balance.skuId)}" data-target-current="${escapeHtml(balance.targetPacks ?? "")}">Set target</button></td>` : ""}
        </tr>`).join("");
    tbody.querySelectorAll("[data-target-location]").forEach((button) => button.addEventListener("click", () => setInventoryTarget(button)));
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="${canWrite ? 9 : 8}">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

async function loadInventoryReplenishment() {
  const tbody = document.getElementById("inventory-replenishment");
  const count = document.getElementById("inventory-replenishment-count");
  if (!tbody || !count) {
    return;
  }

  const params = inventoryParams();
  try {
    const rows = await request(`/api/v1/operations/replenishment?${params.toString()}`);
    const shortages = rows.filter((row) => row.shortagePacks > 0);
    count.textContent = `${shortages.length} shortage${shortages.length === 1 ? "" : "s"}`;
    tbody.innerHTML = rows.length === 0
      ? `<tr><td colspan="7">No target-stock rows yet.</td></tr>`
      : rows.map((row) => `
        <tr>
          <td>${escapeHtml(row.destinationLocationName)}</td>
          <td><strong>${escapeHtml(row.skuCode || "Unknown SKU")}</strong><span class="muted-cell">${escapeHtml(row.productName || shortId(row.skuId, "SKU"))}</span></td>
          <td>${quantityStack(row.availablePacks, row.availablePieces, row.destinationLocationType)}</td>
          <td>${quantityStack(row.incomingPacks, row.incomingPieces, row.destinationLocationType)}</td>
          <td>${quantityStack(row.targetPacks, row.targetPieces, row.destinationLocationType)}</td>
          <td>${row.shortagePacks > 0 ? `<span class="status-pill status-warn">${quantityText(row.shortagePacks, row.shortagePieces, row.destinationLocationType)}</span>` : `<span class="status-pill status-ok">Covered</span>`}</td>
          <td>${quantityStack(row.mainAvailablePacks, null, "MainWarehouse")}</td>
        </tr>`).join("");
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="7">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function loadInventoryBatches() {
  const tbody = document.getElementById("inventory-batches");
  const count = document.getElementById("inventory-batch-count");
  const params = inventoryParams();
  params.set("pageSize", "50");
  params.set("includeEmpty", String(document.getElementById("inventory-include-empty").checked));
  try {
    const result = await request(`/api/v1/inventory/batches?${params.toString()}`);
    count.textContent = `${result.totalCount} batch${result.totalCount === 1 ? "" : "es"}`;
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="6">No batches yet.</td></tr>`
      : result.items.map((batch) => `
        <tr>
          <td>${escapeHtml(batch.lotNumber || "-")}</td>
          <td>${escapeHtml(batch.locationName)}</td>
          <td><strong>${escapeHtml(batch.skuCode || "Unknown SKU")}</strong>${skuStatusBadge(batch.skuIsActive)}<span class="muted-cell">${escapeHtml(batch.productName || shortId(batch.skuId, "SKU"))}</span></td>
          <td>${quantityStack(batch.packQuantity, batch.pieceQuantity, batch.locationType)}</td>
          <td>${expiryBadge(batch.expiryDate)}</td>
          <td>${escapeHtml(batch.notes || "-")}</td>
        </tr>`).join("");
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

async function loadTransferBlockedBatches() {
  const tbody = document.getElementById("inventory-blocked-batches");
  const count = document.getElementById("inventory-blocked-count");
  if (!tbody || !count) {
    return;
  }

  const params = inventoryParams();
  try {
    const rows = await request(`/api/v1/inventory/transfer-blocked-batches?${params.toString()}`);
    count.textContent = `${rows.length} expired`;
    tbody.innerHTML = rows.length === 0
      ? `<tr><td colspan="6">No expired batches.</td></tr>`
      : rows.map((batch) => `
        <tr>
          <td>${escapeHtml(batch.locationName)}</td>
          <td><strong>${escapeHtml(batch.skuCode || "Unknown SKU")}</strong><span class="muted-cell">${escapeHtml(batch.productName || shortId(batch.skuId, "SKU"))}</span></td>
          <td>${escapeHtml(batch.lotNumber || "-")}</td>
          <td>${quantityStack(batch.packQuantity, batch.pieceQuantity, batch.locationType)}</td>
          <td>${expiryBadge(batch.expiryDate)}</td>
          <td><span class="status-pill status-warn">${escapeHtml(batch.reason || "Blocked")}</span></td>
        </tr>`).join("");
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

async function loadInventoryTransactions() {
  const tbody = document.getElementById("inventory-transactions");
  const count = document.getElementById("inventory-transaction-count");
  const params = inventoryParams();
  params.set("pageSize", "50");
  try {
    const result = await request(`/api/v1/inventory/transactions?${params.toString()}`);
    count.textContent = `${result.totalCount} transaction${result.totalCount === 1 ? "" : "s"}`;
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="5">No transactions yet.</td></tr>`
      : result.items.map((transaction) => `
        <tr>
          <td>${escapeHtml(transaction.transactionType)}</td>
          <td>${escapeHtml(transaction.locationName)}</td>
          <td><strong>${escapeHtml(transaction.skuCode || "Unknown SKU")}</strong>${skuStatusBadge(transaction.skuIsActive)}<span class="muted-cell">${escapeHtml(transaction.productName || shortId(transaction.skuId, "SKU"))}</span></td>
          <td>${quantityStack(transaction.packChange, transaction.pieceChange, transaction.locationType)}</td>
          <td>${escapeHtml(formatDateTime(transaction.createdAt))}</td>
        </tr>`).join("");
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="5">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

function inventoryStockStatus(balance) {
  if (balance.targetPacks === null || balance.targetPacks === undefined) {
    return `<span class="status-pill status-muted">No target</span>`;
  }
  if (balance.availablePacks < balance.targetPacks) {
    return `<span class="status-pill status-warn">Low stock</span>`;
  }
  return `<span class="status-pill status-ok">Healthy</span>`;
}

function skuStatusBadge(isActive) {
  if (isActive === false) {
    return ` <span class="status-pill status-muted">Inactive SKU</span>`;
  }
  return "";
}

function inventoryShortagePacks(balance) {
  if (balance.targetPacks === null || balance.targetPacks === undefined) {
    return null;
  }
  return Math.max(Number(balance.targetPacks) - Number(balance.availablePacks), 0);
}

function inventoryShortagePieces(balance) {
  const shortage = inventoryShortagePacks(balance);
  if (shortage === null || balance.availablePieces === null || balance.availablePieces === undefined || !balance.piecesPerPack) {
    return null;
  }
  return shortage * Number(balance.piecesPerPack);
}

function quantityStack(packs, pieces, locationType) {
  const packText = packs === null || packs === undefined ? "-" : `${packs} pack${Math.abs(Number(packs)) === 1 ? "" : "s"}`;
  if (locationType === "MainWarehouse") {
    return `<strong>${escapeHtml(packText)}</strong>`;
  }
  const pieceText = pieces === null || pieces === undefined ? "pieces not set" : `${pieces} piece${Math.abs(Number(pieces)) === 1 ? "" : "s"}`;
  return `<strong>${escapeHtml(packText)}</strong><span class="muted-cell">${escapeHtml(pieceText)}</span>`;
}

function quantityText(packs, pieces, locationType) {
  const packText = `${packs} pack${Math.abs(Number(packs)) === 1 ? "" : "s"}`;
  if (locationType === "MainWarehouse" || pieces === null || pieces === undefined) {
    return packText;
  }

  return `${packText} / ${pieces} piece${Math.abs(Number(pieces)) === 1 ? "" : "s"}`;
}

function addNullable(left, right) {
  if (left === null || left === undefined || right === null || right === undefined) {
    return null;
  }
  return left + right;
}

function expiryBadge(expiryDate) {
  if (!expiryDate) {
    return `<span class="status-pill status-muted">No expiry</span>`;
  }
  const today = new Date();
  const expiry = new Date(`${expiryDate}T00:00:00`);
  const days = Math.ceil((expiry - today) / 86400000);
  if (days < 0) {
    return `<span class="status-pill status-warn">${escapeHtml(expiryDate)} expired</span>`;
  }
  if (days <= 90) {
    return `<span class="status-pill status-warn">${escapeHtml(expiryDate)}</span>`;
  }
  return `<span class="status-pill status-ok">${escapeHtml(expiryDate)}</span>`;
}

function inventoryParams() {
  const params = new URLSearchParams();
  const locationId = document.getElementById("inventory-location")?.value;
  const skuId = document.getElementById("inventory-sku")?.value.trim();
  if (locationId) {
    params.set("locationId", locationId);
  }
  if (skuId) {
    params.set("skuId", skuId);
  }
  return params;
}

async function setInventoryTarget(button) {
  const current = button.dataset.targetCurrent || "";
  const raw = await promptDialog({
    title: "Set Target Packs",
    label: "Target stock is measured in packs.",
    defaultValue: current,
    inputType: "number",
    required: true
  });
  if (raw === null) {
    return;
  }
  const value = raw.trim();
  if (value && (!Number.isInteger(Number(value)) || Number(value) < 0)) {
    notice("Target packs must be a non-negative whole number.", "error");
    return;
  }

  try {
    await request(`/api/v1/inventory/stock-balances/${button.dataset.targetLocation}/${button.dataset.targetSku}/target`, {
      method: "PUT",
      body: JSON.stringify({ targetPacks: value ? Number(value) : null })
    });
    notice("Target packs updated.", "success");
    await loadInventoryBalances();
  } catch (exception) {
    notice(getFriendlyInventoryError(exception), "error");
  }
}

async function reserveInventoryReplenishment() {
  try {
    const locationId = document.getElementById("inventory-location")?.value || null;
    const skuId = document.getElementById("inventory-sku")?.value || null;
    const result = await request("/api/v1/operations/replenishment/daily-reset", {
      method: "POST",
      body: JSON.stringify({ locationId, skuId })
    });
    const alertText = result.alerts?.length
      ? ` Alert: ${result.alerts.map((alert) => `${alert.skuCode || alert.skuId} at ${alert.destinationLocationName}: ${alert.message}`).join(" | ")}`
      : "";
    notice(`Created ${result.createdOperations} Draft replenishment transfer(s). ${result.unfilledPacks} pack(s) still uncovered.${alertText}`, result.unfilledPacks > 0 ? "info" : "success");
    await refreshInventoryTables();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function renderCrm() {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  selectedMerchantId = null;
  selectedRepresentativeId = null;
  document.getElementById("view").innerHTML = `
    <section class="catalog-hero">
      <div>
        <p class="eyebrow">CRM</p>
        <h2>Merchant and representative records</h2>
        <p>Maintain commercial relationships, operational notes, and merchant context used across sales, returns, payments, and reporting.</p>
      </div>
      <div class="scenario-grid">
        ${scenarioCard("Role", canWrite ? "Can edit CRM records" : "Read only", canWrite ? "status-ok" : "status-muted")}
        ${scenarioCard("Merchant context", "Batch history and notes", "status-muted")}
        ${scenarioCard("Operations link", "Shared across workflows", "status-muted")}
      </div>
    </section>
    <section class="band">
      <div class="section-head">
        <div>
          <h2>Merchants</h2>
          <p>Profiles, commercial contacts, remaining context, and operational history.</p>
        </div>
        <span id="crm-count" class="status-pill status-muted">Loading</span>
      </div>
      ${canWrite ? `
        <form id="merchant-form" class="form grid-form">
          <input id="merchant-id" type="hidden">
          <div class="field"><label for="merchant-name">Business name</label><input id="merchant-name" class="input" required></div>
          <div class="field"><label for="merchant-contact">Contact person</label><input id="merchant-contact" class="input" required></div>
          <div class="field"><label for="merchant-phone">Phone</label><input id="merchant-phone" class="input"></div>
          <div class="field"><label for="merchant-type">Business type</label><select id="merchant-type" class="select"><option>Merchant</option><option>Pharmacy</option><option>Oculist</option><option>BeautyCenter</option><option>Other</option></select></div>
          <div class="toolbar full-span">
            <button id="merchant-save-button" class="button primary" type="submit">Create merchant</button>
            <button id="merchant-reset-button" class="button secondary" type="button">Clear</button>
          </div>
        </form>` : ""}
      <div class="table-wrap">
        <table><thead><tr><th>Business</th><th>Contact</th><th>Phone</th><th>Type</th><th>Status</th><th>Actions</th></tr></thead><tbody id="merchant-rows"></tbody></table>
      </div>
      <div id="merchant-detail-panel" class="detail-panel" hidden></div>
    </section>
    <section class="band">
      <div class="section-head"><h2>Representatives</h2><span id="rep-count" class="status-pill status-muted">Loading</span></div>
      ${canWrite ? `
        <form id="rep-form" class="form grid-form">
          <input id="rep-id" type="hidden">
          <div class="field"><label for="rep-name">Name</label><input id="rep-name" class="input" required></div>
          <div class="field"><label for="rep-phone">Phone</label><input id="rep-phone" class="input"></div>
          <div class="field"><label for="rep-type">Type</label><select id="rep-type" class="select"><option>External</option><option>Internal</option></select></div>
          <div class="toolbar full-span">
            <button id="rep-save-button" class="button primary" type="submit">Create representative</button>
            <button id="rep-reset-button" class="button secondary" type="button">Clear</button>
          </div>
        </form>` : ""}
      <div class="table-wrap">
        <table><thead><tr><th>Name</th><th>Phone</th><th>Type</th><th>Status</th><th>Actions</th></tr></thead><tbody id="rep-rows"></tbody></table>
      </div>
    </section>`;

  if (canWrite) {
    document.getElementById("merchant-form").addEventListener("submit", saveMerchant);
    document.getElementById("merchant-reset-button").addEventListener("click", resetMerchantForm);
    document.getElementById("rep-form").addEventListener("submit", saveRepresentative);
    document.getElementById("rep-reset-button").addEventListener("click", resetRepresentativeForm);
  }
  await Promise.all([loadMerchants(), loadRepresentatives()]);
}

async function loadMerchants(search = "") {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  const canReadBatchHistory = ["Admin", "ERPAdmin", "CLevel"].includes(auth?.user.role);
  const tbody = document.getElementById("merchant-rows");
  const count = document.getElementById("crm-count");
  try {
    const result = await fetchMerchantList(search);
    count.textContent = `${result.totalCount} merchants`;
    tbody.innerHTML = result.items.length === 0 ? `<tr><td colspan="6">No merchants yet.</td></tr>` : result.items.map((merchant) => `
      <tr>
        <td>${escapeHtml(merchant.businessName)}</td>
        <td>${escapeHtml(merchant.contactPersonName)}</td>
        <td>${escapeHtml((merchant.phoneNumbers || []).join(", ") || "-")}</td>
        <td>${escapeHtml(merchant.businessType)}</td>
        <td><span class="status-pill ${merchant.status === "Active" ? "status-ok" : "status-muted"}">${escapeHtml(merchant.status)}</span></td>
        <td>
          <button class="button secondary table-action" type="button" data-view-merchant="${escapeHtml(merchant.id)}">Detail</button>
          <button class="button secondary table-action" type="button" data-print-report="merchant-statement" data-print-id="${escapeHtml(merchant.id)}" data-print-code="${escapeHtml(merchant.businessName)}">Print</button>
          ${canWrite ? `<button class="button secondary table-action" type="button" data-edit-merchant="${escapeHtml(merchant.id)}">Edit</button>` : ""}
          ${canWrite ? `<button class="button secondary table-action" type="button" data-status-merchant="${escapeHtml(merchant.id)}" data-next-status="${merchant.status === "Active" ? "deactivate" : "reactivate"}">${merchant.status === "Active" ? "Deactivate" : "Reactivate"}</button>` : ""}
          ${canWrite ? `<button class="button secondary table-action" type="button" data-note-merchant="${escapeHtml(merchant.id)}">Add note</button>` : ""}
          ${canReadBatchHistory ? `<button class="button secondary table-action" type="button" data-batch-history-merchant="${escapeHtml(merchant.id)}">Batch history</button>` : ""}
        </td>
      </tr>`).join("");
    tbody.querySelectorAll("[data-view-merchant]").forEach((button) => button.addEventListener("click", () => showMerchantDetail(button.dataset.viewMerchant)));
    tbody.querySelectorAll("[data-edit-merchant]").forEach((button) => button.addEventListener("click", async () => {
      const merchant = result.items.find((value) => value.id === button.dataset.editMerchant);
      if (merchant) {
        fillMerchantForm(merchant);
        await loadMerchants();
        return;
      }
      await editMerchant(button.dataset.editMerchant);
    }));
    tbody.querySelectorAll("[data-status-merchant]").forEach((button) => button.addEventListener("click", () => changeMerchantStatus(button.dataset.statusMerchant, button.dataset.nextStatus)));
    tbody.querySelectorAll("[data-note-merchant]").forEach((button) => button.addEventListener("click", () => addMerchantNote(button.dataset.noteMerchant)));
    tbody.querySelectorAll("[data-batch-history-merchant]").forEach((button) => button.addEventListener("click", () => showMerchantBatchHistory(button.dataset.batchHistoryMerchant)));
    bindPrintReportButtons(tbody);
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function loadRepresentatives() {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  const tbody = document.getElementById("rep-rows");
  const count = document.getElementById("rep-count");
  try {
    const reps = await request("/api/v1/crm/representatives?includeInactive=true");
    count.textContent = `${reps.length} reps`;
    tbody.innerHTML = reps.length === 0 ? `<tr><td colspan="5">No representatives yet.</td></tr>` : reps.map((rep) => `
      <tr>
        <td>${escapeHtml(rep.name)}</td>
        <td>${escapeHtml((rep.phoneNumbers || []).join(", ") || "-")}</td>
        <td>${escapeHtml(rep.type)}</td>
        <td><span class="status-pill ${rep.status === "Active" ? "status-ok" : "status-muted"}">${escapeHtml(rep.status)}</span></td>
        <td>
          ${canWrite ? `<button class="button secondary table-action" type="button" data-edit-rep="${escapeHtml(rep.id)}">Edit</button>` : ""}
          ${canWrite ? `<button class="button secondary table-action" type="button" data-status-rep="${escapeHtml(rep.id)}" data-next-status="${rep.status === "Active" ? "deactivate" : "reactivate"}">${rep.status === "Active" ? "Deactivate" : "Reactivate"}</button>` : ""}
        </td>
      </tr>`).join("");
    tbody.querySelectorAll("[data-edit-rep]").forEach((button) => button.addEventListener("click", () => editRepresentative(button.dataset.editRep)));
    tbody.querySelectorAll("[data-status-rep]").forEach((button) => button.addEventListener("click", () => changeRepresentativeStatus(button.dataset.statusRep, button.dataset.nextStatus)));
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="5">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function saveMerchant(event) {
  event.preventDefault();
  const businessName = document.getElementById("merchant-name").value.trim();
  const contactPersonName = document.getElementById("merchant-contact").value.trim();
  const merchantId = document.getElementById("merchant-id").value;
  if (!businessName || !contactPersonName) {
    notice("Business name and contact person are required.", "error");
    return;
  }

  try {
    await request(merchantId ? `/api/v1/crm/merchants/${merchantId}` : "/api/v1/crm/merchants", {
      method: merchantId ? "PUT" : "POST",
      body: JSON.stringify({
        businessName,
        contactPersonName,
        phoneNumbers: document.getElementById("merchant-phone").value.trim() ? [document.getElementById("merchant-phone").value.trim()] : [],
        businessType: document.getElementById("merchant-type").value
      })
    });
    resetMerchantForm();
    notice(merchantId ? "Merchant updated." : "Merchant created.", "success");
    await loadMerchants();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function fetchMerchantList(search = "") {
  if (search) {
    const params = new URLSearchParams({ includeInactive: "true", pageSize: "100", search });
    return await request(`/api/v1/crm/merchants?${params.toString()}`);
  }

  const items = [];
  let page = 1;
  let totalCount = 0;
  do {
    const params = new URLSearchParams({ includeInactive: "true", pageSize: "100", page: String(page) });
    const result = await request(`/api/v1/crm/merchants?${params.toString()}`);
    items.push(...(result.items || []));
    totalCount = result.totalCount || items.length;
    page += 1;
  } while (items.length < totalCount);

  return { items, totalCount };
}

async function saveRepresentative(event) {
  event.preventDefault();
  const name = document.getElementById("rep-name").value.trim();
  const repId = document.getElementById("rep-id").value;
  if (!name) {
    notice("Representative name is required.", "error");
    return;
  }

  try {
    await request(repId ? `/api/v1/crm/representatives/${repId}` : "/api/v1/crm/representatives", {
      method: repId ? "PUT" : "POST",
      body: JSON.stringify({
        name,
        phoneNumbers: document.getElementById("rep-phone").value.trim() ? [document.getElementById("rep-phone").value.trim()] : [],
        type: document.getElementById("rep-type").value
      })
    });
    resetRepresentativeForm();
    notice(repId ? "Representative updated." : "Representative created.", "success");
    await loadRepresentatives();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function resetMerchantForm() {
  const form = document.getElementById("merchant-form");
  if (!form) {
    return;
  }
  form.reset();
  document.getElementById("merchant-id").value = "";
  document.getElementById("merchant-save-button").textContent = "Create merchant";
  selectedMerchantId = null;
}

function resetRepresentativeForm() {
  const form = document.getElementById("rep-form");
  if (!form) {
    return;
  }
  form.reset();
  document.getElementById("rep-id").value = "";
  document.getElementById("rep-save-button").textContent = "Create representative";
  selectedRepresentativeId = null;
}

async function editMerchant(merchantId) {
  try {
    const detail = await request(`/api/v1/crm/merchants/${merchantId}`);
    const merchant = detail.merchant;
    fillMerchantForm(merchant);
    await showMerchantDetail(merchantId, detail);
    await loadMerchants();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function fillMerchantForm(merchant) {
  document.getElementById("merchant-id").value = merchant.id;
  document.getElementById("merchant-name").value = merchant.businessName || "";
  document.getElementById("merchant-contact").value = merchant.contactPersonName || "";
  document.getElementById("merchant-phone").value = (merchant.phoneNumbers || [])[0] || "";
  document.getElementById("merchant-type").value = merchant.businessType || "Merchant";
  document.getElementById("merchant-save-button").textContent = "Update merchant";
  selectedMerchantId = merchant.id;
}

async function editRepresentative(repId) {
  const rep = operationRepresentativeOptions.find((value) => value.id === repId) || (await request("/api/v1/crm/representatives?includeInactive=true")).find((value) => value.id === repId);
  if (!rep) {
    notice("Representative not found.", "error");
    return;
  }
  document.getElementById("rep-id").value = rep.id;
  document.getElementById("rep-name").value = rep.name || "";
  document.getElementById("rep-phone").value = (rep.phoneNumbers || [])[0] || "";
  document.getElementById("rep-type").value = rep.type || "External";
  document.getElementById("rep-save-button").textContent = "Update representative";
  selectedRepresentativeId = rep.id;
}

async function changeMerchantStatus(merchantId, action) {
  try {
    await request(`/api/v1/crm/merchants/${merchantId}/${action}`, { method: "PATCH" });
    notice(action === "deactivate" ? "Merchant deactivated." : "Merchant reactivated.", "success");
    await loadMerchants();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function changeRepresentativeStatus(repId, action) {
  try {
    await request(`/api/v1/crm/representatives/${repId}/${action}`, { method: "PATCH" });
    notice(action === "deactivate" ? "Representative deactivated." : "Representative reactivated.", "success");
    await loadRepresentatives();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function showMerchantDetail(merchantId, existingDetail = null) {
  const panel = document.getElementById("merchant-detail-panel");
  if (!panel) {
    return;
  }
  panel.hidden = false;
  panel.innerHTML = `<span class="muted-text">Loading merchant detail...</span>`;
  try {
    const detail = existingDetail || await request(`/api/v1/crm/merchants/${merchantId}`);
    const merchant = detail.merchant;
    const summary = detail.summary || {};
    const notes = detail.notes || [];
    const operations = detail.recentOperations || [];
    const balance = summary.balance ?? summary.balancePlaceholder ?? 0;
    const canReadBatchHistory = ["Admin", "ERPAdmin", "CLevel"].includes(getAuth()?.user.role);
    const batchRows = canReadBatchHistory ? await request(`/api/v1/crm/merchants/${merchantId}/batch-history`) : [];
    panel.innerHTML = `
      <div class="section-head tight-head">
        <div><h3>${escapeHtml(merchant.businessName)}</h3><p>${escapeHtml(merchant.contactPersonName)} ${merchant.phoneNumbers?.length ? `- ${escapeHtml(merchant.phoneNumbers.join(", "))}` : ""}</p></div>
        <span class="status-pill ${merchant.status === "Active" ? "status-ok" : "status-muted"}">${escapeHtml(merchant.status)}</span>
      </div>
      <div class="operation-detail-grid">
        <div class="metric"><span>Operations</span><strong>${escapeHtml(summary.operationCount || 0)}</strong></div>
        <div class="metric"><span>Sold packs</span><strong>${escapeHtml(summary.soldPacks || 0)}</strong></div>
        <div class="metric"><span>Sold pieces</span><strong>${escapeHtml(summary.soldPieces || 0)}</strong></div>
        <div class="metric"><span>Remaining</span><strong>${escapeHtml(formatMoney(balance))}</strong></div>
      </div>
      <div class="table-wrap compact-table"><table><thead><tr><th>Operation</th><th>Type</th><th>Status</th><th>Payment</th><th>Qty</th><th>Bonus</th><th>Total</th><th>Created</th></tr></thead><tbody>${operations.length === 0
        ? `<tr><td colspan="8">No operations for this merchant yet.</td></tr>`
        : operations.map((operation) => `<tr>
            <td><strong>${escapeHtml(operation.operationNumber)}</strong></td>
            <td>${escapeHtml(operation.operationType)}</td>
            <td><span class="status-pill ${operationStatusClass(operation.status)}">${escapeHtml(operation.status)}</span></td>
            <td>${escapeHtml(operation.paymentMethod || "-")}</td>
            <td>${escapeHtml(operation.quantity || 0)}</td>
            <td>${escapeHtml(operation.bonusQuantity || 0)}</td>
            <td>${escapeHtml(formatMoney(operation.total || 0))}</td>
            <td>${escapeHtml(formatDateTime(operation.createdAt))}</td>
          </tr>`).join("")}</tbody></table></div>
      ${canReadBatchHistory ? `<div class="section-head tight-head"><h3>Merchant Batch History</h3><span class="muted-text">Recorded sales and confirmed returns by SKU, lot, and expiry</span></div>
      ${renderMerchantBatchHistoryTable(batchRows)}` : ""}
      <div class="table-wrap compact-table"><table><thead><tr><th>Latest notes</th><th>Created</th></tr></thead><tbody>${notes.length === 0
        ? `<tr><td colspan="2">No notes yet.</td></tr>`
        : notes.map((note) => `<tr><td>${escapeHtml(note.note)}</td><td>${escapeHtml(formatDateTime(note.createdAt))}</td></tr>`).join("")}</tbody></table></div>`;
    await loadMerchants();
  } catch (exception) {
    panel.innerHTML = `<span class="muted-text">${escapeHtml(getFriendlyWorkspaceError(exception))}</span>`;
  }
}

async function showMerchantBatchHistory(merchantId) {
  try {
    const panel = document.getElementById("merchant-detail-panel");
    const rows = await request(`/api/v1/crm/merchants/${merchantId}/batch-history`);
    if (panel) {
      panel.hidden = false;
      panel.innerHTML = `
        <div class="section-head tight-head"><h3>Merchant Batch History</h3><span class="muted-text">Recorded sales and confirmed returns</span></div>
        ${renderMerchantBatchHistoryTable(rows)}`;
    }
    notice(rows.length === 0 ? "No merchant batch history yet." : "Merchant batch history loaded.", "info");
    await loadMerchants();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function renderMerchantBatchHistoryTable(rows) {
  return `<div class="table-wrap compact-table"><table><thead><tr><th>SKU</th><th>Product</th><th>Lot</th><th>Batch expiry</th><th>Sold</th><th>Returned</th><th>Expiry status</th></tr></thead><tbody>${rows.length === 0
    ? `<tr><td colspan="7">No confirmed merchant sales or returns yet.</td></tr>`
    : rows.map((row) => `<tr>
          <td><strong>${escapeHtml(row.skuCode || shortId(row.skuId, "SKU"))}</strong></td>
          <td>${escapeHtml(row.productName || "-")}</td>
          <td>${escapeHtml(row.lotNumber || "-")}</td>
          <td>${row.expiryDate ? expiryBadge(row.expiryDate) : `<span class="status-pill status-muted">-</span>`}</td>
          <td>${escapeHtml(row.soldQuantity || 0)}</td>
          <td>${escapeHtml(row.returnedQuantity || 0)}</td>
          <td><span class="status-pill ${row.expiryStatus === "Expired" ? "status-warn" : "status-muted"}">${escapeHtml(row.expiryStatus || "-")}</span></td>
        </tr>`).join("")}</tbody></table></div>`;
}

async function addMerchantNote(merchantId) {
  const note = await promptDialog({
    title: "Add Merchant Note",
    label: "Write a short note for this merchant profile.",
    multiline: true,
    required: true
  });
  if (!note?.trim()) {
    return;
  }
  try {
    await request(`/api/v1/crm/merchants/${merchantId}/notes`, { method: "POST", body: JSON.stringify({ note }) });
    notice("Note added.", "success");
    await loadMerchants();
    await showMerchantDetail(merchantId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function renderOperations() {
  const auth = getAuth();
  const canWrite = ["Admin", "ERPAdmin", "WarehouseClerk"].includes(auth?.user.role);
  operationsUiState.operationType = operationsUiState.operationType === "InventoryReceipt" ? "WarehouseTransfer" : (operationsUiState.operationType || "WarehouseTransfer");
  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: "Operations",
      title: "Operations control",
      body: "Create the operational draft on the rail, resolve stock lines in the workspace, then move the queue through confirmation and fulfillment.",
      metrics: `
        ${scenarioCard("Workflow rail", canWrite ? "Create and revise" : "Read only", canWrite ? "status-ok" : "status-muted")}
        ${scenarioCard("Active queue", "Loading", "status-muted", "operation-count")}
        ${scenarioCard("Scope", auth?.user.locationId ? "Assigned location access" : "Cross-location access", "status-muted")}
      `
    })}
    <section class="operations-workspace">
      ${canWrite ? `
        <form id="operation-form" class="form operation-form-layout">
          <aside class="workflow-rail">
            <div class="operation-editor-banner">
              <div>
                <strong id="operation-editor-title">Create draft</strong>
                <p id="operation-editor-hint" class="muted-text">Start a new operation draft.</p>
              </div>
              <span id="operation-editor-mode" class="status-pill status-muted">Create</span>
            </div>
            <div class="field"><label for="op-type">Type</label><select id="op-type" class="select"><option value="WarehouseTransfer">Warehouse transfer</option><option value="WholesaleSale">Wholesale sale</option><option value="RetailSale">Retail/online sale</option><option value="Reserve">Representative reserve</option><option value="Return">Return</option><option value="Change">Change</option><option value="WriteOff">Write-off</option></select></div>
            <div class="field"><label for="op-source">Source location</label><select id="op-source" class="select"></select></div>
            <div class="field"><label for="op-destination">Destination location</label><select id="op-destination" class="select"></select></div>
            <div class="field op-merchant-field"><label for="op-merchant">Merchant</label><select id="op-merchant" class="select"></select></div>
            <div class="field op-rep-field"><label for="op-representative">Representative</label><select id="op-representative" class="select"></select></div>
            <div class="field op-buyer-field"><label for="op-buyer">Buyer name</label><input id="op-buyer" class="input" autocomplete="off"></div>
            <div class="field op-buyer-field"><label for="op-buyer-phone">Buyer phone</label><input id="op-buyer-phone" class="input" autocomplete="off"></div>
            <div class="field op-payment-field"><label for="op-payment">Payment method</label><select id="op-payment" class="select"><option value="">-</option><option value="CashHandToHand">Cash hand to hand</option><option value="CashTransaction">Cash transaction</option><option value="Installment">Installment</option></select></div>
            <div class="field"><label for="op-supplier">Supplier</label><input id="op-supplier" class="input" autocomplete="off" placeholder="Receipt only"></div>
            <div class="field"><label for="op-invoice">Invoice</label><input id="op-invoice" class="input" autocomplete="off" placeholder="Used for receipt flows"></div>
            <div class="field"><label for="op-notes">Notes</label><input id="op-notes" class="input" autocomplete="off"></div>
            <div class="field" id="op-revision-reason-field" hidden><label for="op-revision-reason">Revision reason</label><input id="op-revision-reason" class="input" autocomplete="off" placeholder="Required for revisions"></div>
            <div class="rail-actions">
              <button class="button primary" id="operation-submit-button" type="submit">Save draft</button>
              <button id="operation-editor-reset" class="button secondary" type="button">Reset</button>
            </div>
          </aside>
          <section class="band operation-line-panel">
            <div class="section-head tight-head"><div><h2>Operation lines</h2><p>Search stock first or choose product attributes to resolve the SKU.</p></div><button id="op-add-line" class="button secondary" type="button">Add line</button></div>
            <div id="op-lines" class="line-editor"></div>
          </section>
        </form>` : `<p class="muted-text">This role can inspect operations but cannot create or revise drafts.</p>`}
    </section>
    <section class="band rail-band">
      <div class="section-head">
        <div><h2>Queue</h2><p>Active operations stay compact here. Use Details to inspect versions, stock movement, and documents.</p></div>
      </div>
      <div class="toolbar">
        <label class="check-field"><input id="operations-show-completed" type="checkbox"><span>Show completed/received/cancelled history</span></label>
      </div>
      <div class="table-wrap">
        <table><thead><tr><th>No.</th><th>Type</th><th>Status</th><th>Route</th><th>Created</th><th>Action</th></tr></thead><tbody id="operation-rows"></tbody></table>
      </div>
    </section>`;

  if (canWrite) {
    await Promise.all([hydrateOperationLocations(), hydrateOperationSkus(), hydrateOperationCrmOptions()]);
    const typeControl = document.getElementById("op-type");
    if (!typeControl) {
      return;
    }
    typeControl.value = operationsUiState.operationType;
    typeControl.addEventListener("change", syncOperationTypeControls);
    document.getElementById("op-source").addEventListener("change", () => {
      lockOperationRouteIfSelected();
      void refreshOperationSkuAvailability();
    });
    document.getElementById("op-destination").addEventListener("change", lockOperationRouteIfSelected);
    document.getElementById("op-add-line").addEventListener("click", () => addOperationLine());
    document.getElementById("operation-editor-reset").addEventListener("click", resetOperationEditorMode);
    addOperationLine();
    syncOperationTypeControls();
    applyOperationEditorMode();
    document.getElementById("operation-form").addEventListener("submit", submitOperationEditor);
  }
  document.getElementById("operations-show-completed").addEventListener("change", loadOperations);
  await loadOperations();
}

async function hydrateOperationLocations() {
  operationLocations = await request("/api/v1/inventory/locations");
  for (const id of ["op-source", "op-destination"]) {
    const select = document.getElementById(id);
    select.innerHTML = `<option value="">-</option>${operationLocations.map((location) => `<option value="${escapeHtml(location.id)}">${escapeHtml(location.name)}</option>`).join("")}`;
  }
}

async function hydrateOperationSkus() {
  const products = [];
  let page = 1;
  let totalCount = 0;
  do {
    const result = await request(`/api/v1/catalog/products?includeInactive=false&page=${page}&pageSize=100`);
    products.push(...(result.items || []));
    totalCount = result.totalCount || products.length;
    page += 1;
  } while (products.length < totalCount);

  const skus = [];
  const productOptions = [];
  for (const product of products) {
    const detail = await request(`/api/v1/catalog/products/${product.id}`);
    productOptions.push({
      id: detail.id,
      name: detail.name,
      brandName: detail.brandName,
      categoryName: detail.categoryName,
      productType: detail.productType,
      expiryType: detail.expiryType,
      piecesPerPack: detail.piecesPerPack,
      sellMode: detail.sellMode,
      label: `${detail.brandName} / ${detail.name}`
    });
    for (const sku of detail.skus.filter((value) => value.isActive)) {
      skus.push({
        id: sku.id,
        productId: detail.id,
        productName: detail.name,
        brandName: detail.brandName,
        categoryName: detail.categoryName,
        productType: detail.productType,
        piecesPerPack: detail.piecesPerPack,
        sellMode: detail.sellMode,
        skuCode: sku.skuCode,
        powerSign: sku.powerSign,
        powerValue: sku.powerValue,
        colorName: sku.colorName,
        size: sku.size,
        label: `${detail.name} / ${sku.skuCode}`
      });
    }
  }
  operationSkuOptions = skus;
  operationProductOptions = productOptions.sort((a, b) => a.label.localeCompare(b.label));
  document.querySelectorAll(".line-editor-row").forEach((row) => {
    const skuId = row.querySelector(".op-line-sku")?.value;
    populateOperationProductOptions(row);
    if (skuId) {
      seedOperationLineSkuSelection(row, skuId);
    }
  });
}

async function hydrateOperationCrmOptions() {
  try {
    const [merchants, representatives] = await Promise.all([
      fetchMerchantList(""),
      request("/api/v1/crm/representatives?includeInactive=false")
    ]);
    operationMerchantOptions = (merchants.items || []).filter((merchant) => merchant.status === "Active");
    operationRepresentativeOptions = representatives || [];
    const merchantSelect = document.getElementById("op-merchant");
    const repSelect = document.getElementById("op-representative");
    if (merchantSelect) {
      merchantSelect.innerHTML = `<option value="">Select merchant</option>${operationMerchantOptions.map((merchant) => `<option value="${escapeHtml(merchant.id)}">${escapeHtml(merchant.businessName)}</option>`).join("")}`;
    }
    if (repSelect) {
      repSelect.innerHTML = `<option value="">Select representative</option>${operationRepresentativeOptions.map((rep) => `<option value="${escapeHtml(rep.id)}">${escapeHtml(rep.name)}</option>`).join("")}`;
    }
  } catch {
    operationMerchantOptions = [];
    operationRepresentativeOptions = [];
  }
}

function addOperationLine(line = {}) {
  const container = document.getElementById("op-lines");
  if (!container) {
    return;
  }

  const row = document.createElement("div");
  row.className = "line-editor-row";
  row.dataset.operationLineId = line.operationLineId || "";
  row.innerHTML = `
    <input class="op-line-sku" type="hidden" value="">
    <div class="field op-line-finder"><label>Find stock</label><input class="input op-line-search" autocomplete="off" placeholder="Product, color, power, SKU"><div class="op-line-search-results" hidden></div></div>
    <div class="field"><label>Product</label><select class="select op-line-product" required></select></div>
    <div class="field"><label>Power</label><select class="select op-line-power" required><option value="">Power</option></select></div>
    <div class="field"><label>Color</label><select class="select op-line-color" required><option value="">Color</option></select></div>
    <div class="field"><label>Package</label><select class="select op-line-size"><option value="">Package</option></select></div>
    <div class="op-line-resolved full-span"><span class="muted-text">Select product attributes to resolve SKU.</span></div>
    <div class="field op-line-section-field"><label>Side</label><select class="select op-line-section"><option value="ChangeOut">Returned</option><option value="ChangeIn">Replacement</option></select></div>
    <div class="field"><label>Mode</label><select class="select op-line-entry-mode"><option value="Packs">Packs</option><option value="Pieces">Pieces</option></select></div>
    <div class="field"><label>Quantity</label><input class="input op-line-qty" type="number" min="1" step="1" value="${escapeHtml(line.packQuantity || line.pieceQuantity || 1)}" required></div>
    <div class="field op-line-sale-field"><label>Unit price</label><input class="input op-line-price" type="number" min="0" step="0.01" value="${escapeHtml(line.unitPrice || 0)}"></div>
    <label class="check-field op-line-sale-field"><input class="op-line-bonus" type="checkbox" ${line.isBonus ? "checked" : ""}><span>Bonus</span></label>
    <div class="field op-line-stock-field"><label>Batch / expiry</label><select class="select op-line-stock-option"><option value="">Select source and SKU</option></select></div>
    <div class="field op-line-receipt-field"><label>Lot</label><input class="input op-line-lot" maxlength="100" value="${escapeHtml(line.lotNumber || "")}"></div>
    <div class="field op-line-receipt-field"><label>Batch expiry</label><input class="input op-line-expiry" type="date" value="${escapeHtml(line.expiryDate || "")}"></div>
    <button class="icon-button op-remove-line" type="button" title="Remove line">x</button>`;
  populateOperationProductOptions(row);
  row.querySelector(".op-line-section").value = line.section || "ChangeOut";
  row.querySelector(".op-line-entry-mode").value = line.entryMode || "Packs";
  const syncLineOnly = () => syncOperationLineControls(document.getElementById("op-type").value);
  row.querySelector(".op-line-bonus").addEventListener("change", syncLineOnly);
  row.querySelector(".op-line-section").addEventListener("change", syncLineOnly);
  row.querySelector(".op-line-search").addEventListener("input", () => renderOperationSkuSearchResults(row));
  row.querySelector(".op-line-product").addEventListener("change", () => {
    populateOperationAttributeOptions(row);
    resolveOperationLineSku(row);
  });
  row.querySelector(".op-line-power").addEventListener("change", () => resolveOperationLineSku(row));
  row.querySelector(".op-line-color").addEventListener("change", () => resolveOperationLineSku(row));
  row.querySelector(".op-line-size").addEventListener("change", () => resolveOperationLineSku(row));
  row.querySelector(".op-line-entry-mode").addEventListener("change", () => {
    syncLineOnly();
    refreshOperationStockOptions(row);
  });
  row.querySelector(".op-line-stock-option").addEventListener("change", () => applySelectedStockOption(row));
  row.querySelector(".op-remove-line").addEventListener("click", () => {
    if (container.querySelectorAll(".line-editor-row").length > 1) {
      row.remove();
    }
  });
  container.appendChild(row);
  if (line.skuId) {
    seedOperationLineSkuSelection(row, line.skuId);
  } else {
    populateOperationAttributeOptions(row);
    resolveOperationLineSku(row);
  }
  syncOperationLineControls(document.getElementById("op-type")?.value || operationsUiState.operationType || "WarehouseTransfer");
  if (line.lotNumber !== undefined || line.expiryDate !== undefined) {
    row.querySelector(".op-line-lot").value = line.lotNumber || "";
    row.querySelector(".op-line-expiry").value = line.expiryDate || "";
    void refreshOperationStockOptions(row);
  }
}

function isOperationStockConsumingType(type) {
  return ["WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve", "WriteOff"].includes(type);
}

function isOperationBatchSelectionType(type) {
  return ["WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve", "WriteOff"].includes(type);
}

function availableOperationSkusForType(type) {
  if (!isOperationStockConsumingType(type) || operationAvailableSkuIds === null) {
    return operationSkuOptions;
  }

  return operationSkuOptions.filter((sku) => operationAvailableSkuIds.has(sku.id));
}

function populateOperationProductOptions(row) {
  const select = row.querySelector(".op-line-product");
  if (!select) {
    return;
  }

  const current = select.value;
  const type = document.getElementById("op-type")?.value || operationsUiState.operationType;
  const skuPool = availableOperationSkusForType(type);
  const availableProductIds = new Set(skuPool.map((sku) => sku.productId));
  const products = operationProductOptions.filter((product) =>
    !isOperationStockConsumingType(type) ||
    operationAvailableSkuIds === null ||
    availableProductIds.has(product.id));

  select.innerHTML = `<option value="">Select product</option>${products.map((product) =>
    `<option value="${escapeHtml(product.id)}">${escapeHtml(product.label)}</option>`).join("")}`;
  select.value = products.some((product) => product.id === current) ? current : "";
}

function populateOperationAttributeOptions(row, preferred = {}) {
  const productId = row.querySelector(".op-line-product")?.value;
  const powerSelect = row.querySelector(".op-line-power");
  const colorSelect = row.querySelector(".op-line-color");
  const sizeSelect = row.querySelector(".op-line-size");
  if (!powerSelect || !colorSelect || !sizeSelect) {
    return;
  }

  const type = document.getElementById("op-type")?.value || operationsUiState.operationType;
  const product = operationProductOptions.find((value) => value.id === productId);
  const skus = availableOperationSkusForType(type).filter((sku) => !productId || sku.productId === productId);
  const powerValues = uniqueSortedValues(skus.map((sku) => operationPowerKey(sku)).filter(Boolean), comparePowerKeys);
  const colorValues = uniqueSortedValues(skus.map((sku) => sku.colorName || "").filter(Boolean));
  const sizeValues = uniqueSortedValues(skus.map((sku) => sku.size || "").filter(Boolean));

  const isSolution = product?.productType === "Solution";
  setSelectOptionsPreservingValue(
    powerSelect,
    isSolution ? [{ value: "", label: "Not used" }] : [{ value: "", label: "Power" }, ...powerValues.map((value) => ({ value, label: formatOperationPowerKey(value) }))],
    isSolution ? "" : preferred.powerKey ?? powerSelect.value);
  setSelectOptionsPreservingValue(
    colorSelect,
    isSolution ? [{ value: "", label: "Not used" }] : [{ value: "", label: "Color" }, ...colorValues.map((value) => ({ value, label: value }))],
    isSolution ? "" : preferred.colorName ?? colorSelect.value);
  setSelectOptionsPreservingValue(
    sizeSelect,
    [{ value: "", label: "Package" }, ...sizeValues.map((value) => ({ value, label: value }))],
    preferred.size ?? sizeSelect.value);
  powerSelect.disabled = isSolution;
  colorSelect.disabled = isSolution;
}

function uniqueSortedValues(values, comparer = (a, b) => a.localeCompare(b)) {
  return Array.from(new Set(values)).sort(comparer);
}

function operationPowerKey(sku) {
  if (sku.powerValue === null || sku.powerValue === undefined || sku.powerValue === "") {
    return "";
  }

  return `${sku.powerSign === "-" ? "-" : "+"}${Number(sku.powerValue).toFixed(2)}`;
}

function formatOperationPowerKey(value) {
  if (!value) {
    return "Power";
  }

  const sign = value.startsWith("-") ? "-" : "+";
  const number = Math.abs(Number(value)).toFixed(2);
  return `${sign}${number}`;
}

function comparePowerKeys(a, b) {
  return Number(a) - Number(b);
}

function resolveOperationLineSku(row, options = {}) {
  const hidden = row.querySelector(".op-line-sku");
  const resolved = row.querySelector(".op-line-resolved");
  const productId = row.querySelector(".op-line-product")?.value;
  const product = operationProductOptions.find((value) => value.id === productId);
  const powerKey = row.querySelector(".op-line-power")?.value;
  const colorName = row.querySelector(".op-line-color")?.value;
  const size = row.querySelector(".op-line-size")?.value;

  hidden.value = "";
  if (!options.preserveStock) {
    clearOperationLineStockFields(row);
  }

  if (!productId) {
    resolved.innerHTML = `<span class="muted-text">Select product attributes to resolve SKU.</span>`;
    return null;
  }

  const isSolution = product?.productType === "Solution";
  if (!isSolution && (!powerKey || !colorName)) {
    resolved.innerHTML = `<span class="muted-text">Select product, power, and color to resolve SKU.</span>`;
    return null;
  }

  const type = document.getElementById("op-type")?.value || operationsUiState.operationType;
  const matches = availableOperationSkusForType(type).filter((sku) =>
    sku.productId === productId &&
    (isSolution || operationPowerKey(sku) === powerKey) &&
    (isSolution || (sku.colorName || "") === colorName) &&
    ((sku.size || "") === (size || "") || (!sku.size && !size)));

  if (matches.length === 0) {
    resolved.innerHTML = `<span class="status-pill status-warn">No matching SKU</span><span class="muted-cell">Try another color, power, package, or source location.</span>`;
    return null;
  }

  if (matches.length > 1) {
    resolved.innerHTML = `<span class="status-pill status-warn">SKU conflict</span><span class="muted-cell">${matches.length} SKUs match these attributes. Refine package/size.</span>`;
    return null;
  }

  const sku = matches[0];
  hidden.value = sku.id;
  resolved.innerHTML = `<span class="status-pill status-ok">Resolved SKU</span><strong>${escapeHtml(sku.skuCode)}</strong><span class="muted-cell">${escapeHtml(sku.productName)}</span>`;
  void refreshOperationStockOptions(row);
  return sku;
}

function seedOperationLineSkuSelection(row, skuId) {
  const sku = operationSkuOptions.find((value) => value.id === skuId);
  if (!sku) {
    row.querySelector(".op-line-sku").value = skuId || "";
    row.querySelector(".op-line-resolved").innerHTML = `<span class="status-pill status-warn">Unknown SKU</span><span class="muted-cell">${escapeHtml(shortId(skuId, "SKU"))}</span>`;
    return;
  }

  row.querySelector(".op-line-product").value = sku.productId;
  populateOperationAttributeOptions(row, {
    powerKey: operationPowerKey(sku),
    colorName: sku.colorName || "",
    size: sku.size || ""
  });
  row.querySelector(".op-line-sku").value = sku.id;
  row.querySelector(".op-line-resolved").innerHTML = `<span class="status-pill status-ok">Resolved SKU</span><strong>${escapeHtml(sku.skuCode)}</strong><span class="muted-cell">${escapeHtml(sku.productName)}</span>`;
}

function renderOperationSkuSearchResults(row) {
  const input = row.querySelector(".op-line-search");
  const results = row.querySelector(".op-line-search-results");
  const query = input.value.trim().toLowerCase();
  if (!query) {
    results.hidden = true;
    results.innerHTML = "";
    return;
  }

  const type = document.getElementById("op-type")?.value || operationsUiState.operationType;
  const terms = query.split(/\s+/).filter(Boolean);
  const matches = availableOperationSkusForType(type)
    .filter((sku) => {
      const haystack = `${sku.productName} ${sku.brandName} ${sku.categoryName} ${sku.skuCode} ${formatOperationPowerKey(operationPowerKey(sku))} ${sku.colorName || ""} ${sku.size || ""}`.toLowerCase();
      return terms.every((term) => haystack.includes(term));
    })
    .slice(0, 8);

  setupAdaptiveSearchResultDismissal();
  collapseAdaptiveSearchResults(results);
  results.hidden = false;
  results.innerHTML = matches.length === 0
    ? `<button type="button" class="op-line-search-result" disabled>No matches</button>`
    : matches.map((sku) => `
        <button type="button" class="op-line-search-result" data-sku-id="${escapeHtml(sku.id)}">
          <strong>${escapeHtml(sku.productName)}</strong>
          <span>${escapeHtml(formatOperationPowerKey(operationPowerKey(sku)))} / ${escapeHtml(sku.colorName || "-")} / ${escapeHtml(sku.size || "-")}</span>
          <small>${escapeHtml(sku.skuCode)}</small>
        </button>`).join("");
  results.querySelectorAll("[data-sku-id]").forEach((button) => {
    button.addEventListener("click", () => {
      seedOperationLineSkuSelection(row, button.dataset.skuId);
      input.value = "";
      results.hidden = true;
      results.innerHTML = "";
      clearOperationLineStockFields(row);
      void refreshOperationStockOptions(row);
    });
  });
}

function clearOperationLineStockFields(row) {
  const stockSelect = row.querySelector(".op-line-stock-option");
  if (stockSelect) {
    stockSelect.innerHTML = `<option value="">Select batch / expiry</option>`;
    stockSelect.value = "";
  }
  row.querySelector(".op-line-lot").value = "";
  row.querySelector(".op-line-expiry").value = "";
}

function syncOperationTypeControls() {
  const type = canonicalSelectValue("op-type");
  operationsUiState.operationType = type;
  const source = document.getElementById("op-source");
  const destination = document.getElementById("op-destination");
  const previousSource = source.value;
  const previousDestination = destination.value;
  const main = operationLocations.find((location) => location.locationType === "MainWarehouse");
  const nonMain = operationLocations.filter((location) => location.locationType !== "MainWarehouse");

  if (type === "InventoryReceipt") {
    setSelectOptionsPreservingValue(source, [{ value: "", label: "External supplier" }], previousSource);
    setSelectOptionsPreservingValue(destination, main ? [{ value: main.id, label: main.name }] : [{ value: "", label: "MainWarehouse unavailable" }], previousDestination);
    source.disabled = true;
    destination.disabled = true;
    document.getElementById("op-supplier").disabled = false;
    document.getElementById("op-invoice").disabled = false;
    setOperationFieldGroupVisibility({ merchant: false, rep: false, buyer: false, payment: false, receipt: true });
    syncOperationLineControls(type);
    applyOperationEditorMode();
    return;
  }

  setSelectOptionsPreservingValue(source, main ? [{ value: main.id, label: main.name }] : [{ value: "", label: "MainWarehouse unavailable" }], previousSource);
  setSelectOptionsPreservingValue(destination, [{ value: "", label: "Select destination" }, ...nonMain.map((location) => ({ value: location.id, label: location.name }))], previousDestination);
  source.disabled = true;
  destination.disabled = false;
  document.getElementById("op-supplier").disabled = true;
  document.getElementById("op-invoice").disabled = true;
  syncOperationLineControls(type);

  if (type === "WarehouseTransfer") {
    setOperationFieldGroupVisibility({ merchant: false, rep: false, buyer: false, payment: false, receipt: false });
    applyOperationEditorMode();
    return;
  }
  if (type === "WholesaleSale") {
    setSelectOptionsPreservingValue(source, [{ value: "", label: "Select source" }, ...operationLocations.map((location) => ({ value: location.id, label: location.name }))], previousSource);
    setSelectOptionsPreservingValue(destination, [{ value: "", label: "No destination" }], previousDestination);
    source.disabled = false;
    destination.disabled = true;
    setOperationFieldGroupVisibility({ merchant: true, rep: false, buyer: false, payment: true, receipt: false });
    void refreshAllOperationStockOptions();
    applyOperationEditorMode();
    return;
  }
  if (type === "RetailSale") {
    const retailLocations = operationLocations.filter((location) => ["SubWarehouse", "Online", "Retail"].includes(location.locationType) || /retail|online/i.test(location.name));
    setSelectOptionsPreservingValue(source, [{ value: "", label: "Select source" }, ...retailLocations.map((location) => ({ value: location.id, label: location.name }))], previousSource);
    setSelectOptionsPreservingValue(destination, [{ value: "", label: "No destination" }], previousDestination);
    source.disabled = false;
    destination.disabled = true;
    setOperationFieldGroupVisibility({ merchant: true, rep: false, buyer: true, payment: true, receipt: false });
    void refreshAllOperationStockOptions();
    applyOperationEditorMode();
    return;
  }
  if (type === "Reserve") {
    setSelectOptionsPreservingValue(source, [{ value: "", label: "Select source" }, ...operationLocations.map((location) => ({ value: location.id, label: location.name }))], previousSource);
    setSelectOptionsPreservingValue(destination, [{ value: "", label: "No destination" }], previousDestination);
    source.disabled = false;
    destination.disabled = true;
    setOperationFieldGroupVisibility({ merchant: false, rep: true, buyer: false, payment: false, receipt: false });
    applyOperationEditorMode();
    return;
  }
  if (type === "Return" || type === "Change") {
    setSelectOptionsPreservingValue(source, [{ value: "", label: "Select receiving/issuing location" }, ...operationLocations.map((location) => ({ value: location.id, label: location.name }))], previousSource);
    setSelectOptionsPreservingValue(destination, [{ value: "", label: "No destination" }], previousDestination);
    source.disabled = false;
    destination.disabled = true;
    setOperationFieldGroupVisibility({ merchant: true, rep: false, buyer: false, payment: true, receipt: false });
    applyOperationEditorMode();
    return;
  }
  if (type === "WriteOff") {
    setSelectOptionsPreservingValue(source, [{ value: "", label: "Select source" }, ...operationLocations.map((location) => ({ value: location.id, label: location.name }))], previousSource);
    setSelectOptionsPreservingValue(destination, [{ value: "", label: "No destination" }], previousDestination);
    source.disabled = false;
    destination.disabled = true;
    setOperationFieldGroupVisibility({ merchant: false, rep: false, buyer: false, payment: false, receipt: false });
  }
  applyOperationEditorMode();
}

function setSelectOptionsPreservingValue(select, options, preferredValue) {
  const fallback = options.some((option) => option.value === preferredValue)
    ? preferredValue
    : (options[0]?.value ?? "");
  select.innerHTML = options.map((option) => `<option value="${escapeHtml(option.value)}">${escapeHtml(option.label)}</option>`).join("");
  select.value = fallback;
}

function lockOperationRouteIfSelected() {
  const source = document.getElementById("op-source");
  const destination = document.getElementById("op-destination");
  const type = document.getElementById("op-type")?.value;
  if (!source || !destination) {
    return;
  }

  if (source.value) {
    source.disabled = true;
  }
  if (destination.value) {
    destination.disabled = true;
  }

  if (type === "InventoryReceipt" || type === "WarehouseTransfer" || type === "WriteOff") {
    if (source.value) {
      source.title = "Route is fixed for this operation once chosen.";
    }
    if (destination.value) {
      destination.title = "Route is fixed for this operation once chosen.";
    }
  }
}

function setOperationFieldGroupVisibility({ merchant, rep, buyer, payment, receipt }) {
  setFieldGroupState(".op-merchant-field", merchant);
  setFieldGroupState(".op-rep-field", rep);
  setFieldGroupState(".op-buyer-field", buyer);
  setFieldGroupState(".op-payment-field", payment);
  setSingleFieldState(document.getElementById("op-supplier"), receipt);
  setSingleFieldState(document.getElementById("op-invoice"), receipt);
}

function setFieldGroupState(selector, visible) {
  document.querySelectorAll(selector).forEach((field) => {
    field.hidden = !visible;
    field.querySelectorAll("input, select, textarea").forEach((control) => {
      control.disabled = !visible;
      if (!visible) {
        control.value = "";
      }
    });
  });
}

function setSingleFieldState(control, visible) {
  const field = control.closest(".field");
  field.hidden = !visible;
  control.disabled = !visible;
  if (!visible) {
    control.value = "";
  }
}

function syncOperationLineControls(type) {
  const isSale = ["WholesaleSale", "RetailSale"].includes(type);
  const isBatchSelectedFlow = isOperationBatchSelectionType(type);
  const isFinancialShell = ["Return", "Change"].includes(type);
  document.querySelectorAll(".line-editor-row").forEach((row) => {
    const entryMode = row.querySelector(".op-line-entry-mode");
    const price = row.querySelector(".op-line-price");
    const bonus = row.querySelector(".op-line-bonus");
    const bonusField = bonus.closest(".op-line-sale-field");
    const priceField = price.closest(".op-line-sale-field");
    const sectionField = row.querySelector(".op-line-section-field");
    const section = row.querySelector(".op-line-section");
    const stockField = row.querySelector(".op-line-stock-field");
    const stockSelect = row.querySelector(".op-line-stock-option");

    entryMode.disabled = type !== "RetailSale";
    if (type !== "RetailSale") {
      entryMode.value = "Packs";
    }
    sectionField.hidden = type !== "Change";
    section.disabled = type !== "Change";
    if (type !== "Change") {
      section.value = "ChangeOut";
    }

    row.querySelectorAll(".op-line-receipt-field").forEach((field) => {
      const visible = type === "InventoryReceipt" || type === "Return" || (type === "Change" && section.value === "ChangeOut");
      field.hidden = !visible;
      field.querySelectorAll("input").forEach((input) => {
        input.disabled = !visible && !isSale && !isBatchSelectedFlow;
        if (!visible && !isSale && !isBatchSelectedFlow) {
          input.value = "";
        }
      });
    });
    stockField.hidden = !isBatchSelectedFlow;
    stockSelect.disabled = !isBatchSelectedFlow;
    if (!isBatchSelectedFlow) {
      stockSelect.innerHTML = `<option value="">Not required</option>`;
    }

    priceField.hidden = !isSale && !isFinancialShell;
    bonusField.hidden = !isSale;
    price.disabled = (!isSale && !isFinancialShell) || bonus.checked;
    bonus.disabled = !isSale;
    if (!isSale && !isFinancialShell) {
      price.value = 0;
      bonus.checked = false;
    } else if (bonus.checked) {
      price.value = 0;
    }
  });
  if (isBatchSelectedFlow) {
    void refreshOperationSkuAvailability();
  } else {
    operationAvailableSkuIds = null;
    document.querySelectorAll(".line-editor-row").forEach((row) => {
      populateOperationProductOptions(row);
      populateOperationAttributeOptions(row);
      resolveOperationLineSku(row, { preserveStock: true });
    });
  }
}

function refreshAllOperationStockOptions() {
  document.querySelectorAll(".line-editor-row").forEach((row) => {
    void refreshOperationStockOptions(row);
  });
}

async function refreshOperationSkuAvailability() {
  const type = document.getElementById("op-type")?.value || operationsUiState.operationType;
  const sourceId = document.getElementById("op-source")?.value;
  if (!isOperationStockConsumingType(type) || !sourceId) {
    operationAvailableSkuIds = null;
    document.querySelectorAll(".line-editor-row").forEach((row) => {
      populateOperationProductOptions(row);
      populateOperationAttributeOptions(row);
      resolveOperationLineSku(row);
    });
    refreshAllOperationStockOptions();
    return;
  }

  try {
    const result = await request(`/api/v1/inventory/stock-balances?locationId=${encodeURIComponent(sourceId)}&pageSize=1000`);
    operationAvailableSkuIds = new Set((result.items || [])
      .filter((balance) => (balance.availablePacks || 0) > 0 || (balance.availablePieces || 0) > 0)
      .map((balance) => balance.skuId));
  } catch {
    operationAvailableSkuIds = null;
  }

  document.querySelectorAll(".line-editor-row").forEach((row) => {
    const currentSkuId = row.querySelector(".op-line-sku")?.value;
    populateOperationProductOptions(row);
    if (currentSkuId && (!operationAvailableSkuIds || operationAvailableSkuIds.has(currentSkuId))) {
      seedOperationLineSkuSelection(row, currentSkuId);
    } else {
      populateOperationAttributeOptions(row);
      resolveOperationLineSku(row);
    }
  });
  refreshAllOperationStockOptions();
}

function applyOperationEditorMode() {
  const title = document.getElementById("operation-editor-title");
  const hint = document.getElementById("operation-editor-hint");
  const mode = document.getElementById("operation-editor-mode");
  const submit = document.getElementById("operation-submit-button");
  const revisionField = document.getElementById("op-revision-reason-field");
  const revisionInput = document.getElementById("op-revision-reason");
  const typeControl = document.getElementById("op-type");
  if (!title || !hint || !mode || !submit || !typeControl) {
    return;
  }

  if (operationsUiState.mode === "edit") {
    title.textContent = "Edit draft";
    hint.textContent = document.getElementById("operation-form")?.dataset.shopifyDraft === "true"
      ? "Shopify commercial data is read-only. Select the required batch and expiry, then fulfill the draft."
      : "Update the existing draft without changing its operation type.";
    mode.textContent = "Draft edit";
    submit.textContent = "Save draft changes";
    typeControl.disabled = true;
    if (revisionField) {
      revisionField.hidden = true;
    }
    if (revisionInput) {
      revisionInput.value = "";
    }
    applyShopifyCommercialLocks();
    return;
  }

  if (operationsUiState.mode === "revise") {
    title.textContent = "Revise operation";
    hint.textContent = "Reapply this operation with a required reason. Stock and payment effects are recalculated by the API.";
    mode.textContent = "Revision";
    submit.textContent = "Submit revision";
    typeControl.disabled = true;
    if (revisionField) {
      revisionField.hidden = false;
    }
    return;
  }

  title.textContent = "Create draft";
  hint.textContent = "Start a new operation draft.";
  mode.textContent = "Create";
  submit.textContent = "Save draft";
  typeControl.disabled = false;
  if (revisionField) {
    revisionField.hidden = true;
  }
  if (revisionInput) {
    revisionInput.value = "";
  }
  applyShopifyCommercialLocks();
}

function applyShopifyCommercialLocks() {
  const form = document.getElementById("operation-form");
  const locked = operationsUiState.mode === "edit" && form?.dataset.shopifyDraft === "true";
  ["op-source", "op-destination", "op-merchant", "op-representative", "op-buyer", "op-buyer-phone", "op-payment", "op-supplier", "op-invoice", "op-notes", "op-add-line"].forEach((id) => {
    const control = document.getElementById(id);
    if (control) control.disabled = locked;
  });
  document.querySelectorAll(".line-editor-row").forEach((row) => {
    row.querySelectorAll(".op-line-search, .op-line-product, .op-line-power, .op-line-color, .op-line-size, .op-line-section, .op-line-entry-mode, .op-line-qty, .op-line-price, .op-line-bonus, .op-remove-line").forEach((control) => {
      control.disabled = locked;
    });
  });
}

function resetOperationEditorMode() {
  operationsUiState.mode = "create";
  operationsUiState.operationId = null;
  operationsUiState.revisionFingerprint = null;
  operationsUiState.revisionReason = "";
  const form = document.getElementById("operation-form");
  if (form) {
    form.reset();
    delete form.dataset.shopifyDraft;
  }
  const lines = document.getElementById("op-lines");
  if (lines) {
    lines.innerHTML = "";
    addOperationLine();
  }
  const typeControl = document.getElementById("op-type");
  if (typeControl) {
    typeControl.value = operationsUiState.operationType || "WarehouseTransfer";
  }
  syncOperationTypeControls();
  applyOperationEditorMode();
}

function seedOperationEditor(detail, mode) {
  operationsUiState.mode = mode;
  operationsUiState.operationId = detail.id;
  operationsUiState.operationType = detail.operationType;
  operationsUiState.revisionFingerprint = mode === "revise" ? canonicalOperationPayload({
    operationType: detail.operationType,
    sourceLocationId: detail.sourceLocationId,
    destinationLocationId: detail.destinationLocationId,
    merchantId: detail.clientId,
    representativeId: detail.representativeId,
    buyerName: detail.clientId ? null : detail.clientName,
    buyerPhone: detail.buyerPhone,
    paymentMethod: detail.paymentMethod,
    notes: detail.notes,
    receipt: detail.receipt,
    lines: (detail.lines || []).map((line) => ({ skuId: line.skuId, packQuantity: line.entryMode === "Pieces" ? 0 : line.quantity, pieceQuantity: line.entryMode === "Pieces" ? line.quantity : null, entryMode: line.entryMode, section: line.section, unitPrice: line.unitPrice, isBonus: (line.bonusQuantity || 0) > 0, lotNumber: line.lotNumber, expiryDate: line.expiryDate, notes: line.notes }))
  }) : null;
  const form = document.getElementById("operation-form");
  if (!form) {
    return;
  }

  form.reset();
  form.dataset.shopifyDraft = detail.salesChannel === "Shopify" ? "true" : "false";
  document.getElementById("op-type").value = detail.operationType;
  syncOperationTypeControls();
  document.getElementById("op-source").value = detail.sourceLocationId || "";
  document.getElementById("op-destination").value = detail.destinationLocationId || "";
  const merchant = document.getElementById("op-merchant");
  const rep = document.getElementById("op-representative");
  const buyer = document.getElementById("op-buyer");
  const buyerPhone = document.getElementById("op-buyer-phone");
  const payment = document.getElementById("op-payment");
  const notes = document.getElementById("op-notes");
  if (merchant) {
    merchant.value = detail.clientId || "";
  }
  if (rep) {
    rep.value = detail.representativeId || "";
  }
  if (buyer && detail.operationType === "RetailSale" && !detail.clientId) {
    buyer.value = detail.clientName || "";
  }
  if (buyerPhone) {
    buyerPhone.value = detail.buyerPhone || "";
  }
  if (payment) {
    payment.value = detail.paymentMethod || "";
  }
  if (notes) {
    notes.value = detail.notes || "";
  }
  const supplier = document.getElementById("op-supplier");
  const invoice = document.getElementById("op-invoice");
  if (supplier && detail.receipt?.supplierName) {
    supplier.value = detail.receipt.supplierName;
  }
  if (invoice && detail.receipt?.invoiceNumber) {
    invoice.value = detail.receipt.invoiceNumber || "";
  }

  const lines = document.getElementById("op-lines");
  lines.innerHTML = "";
  (detail.lines || []).forEach((line) => addOperationLine({
    operationLineId: line.id,
    skuId: line.skuId,
    entryMode: line.entryMode,
    pieceQuantity: line.entryMode === "Pieces" ? getOperationLinePrefillQuantity(line) : null,
    packQuantity: line.entryMode === "Pieces" ? null : getOperationLinePrefillQuantity(line),
    unitPrice: line.unitPrice,
    isBonus: (line.bonusQuantity || 0) > 0,
    lotNumber: line.lotNumber,
    expiryDate: line.expiryDate,
    section: line.section,
    notes: line.notes
  }));
  if ((detail.lines || []).length === 0) {
    addOperationLine();
  }
  refreshAllOperationStockOptions();
  applyOperationEditorMode();
}

function getOperationLinePrefillQuantity(line) {
  if (line.entryMode === "Pieces") {
    return line.pieceQuantity ?? line.quantity ?? 1;
  }

  return line.packQuantity ?? line.quantity ?? 1;
}

async function startOperationEditorMode(operationId, mode) {
  try {
    const detail = await request(`/api/v1/operations/${operationId}`);
    seedOperationEditor(detail, mode);
    notice(mode === "edit" ? "Draft loaded into the editor." : "Operation loaded for revision.", "success");
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function refreshOperationStockOptions(row) {
  const type = document.getElementById("op-type")?.value;
  const select = row.querySelector(".op-line-stock-option");
  if (!select || !isOperationBatchSelectionType(type)) {
    return;
  }

  const sourceId = document.getElementById("op-source")?.value;
  const skuId = row.querySelector(".op-line-sku")?.value;
  const entryMode = type === "RetailSale" ? row.querySelector(".op-line-entry-mode").value : "Packs";
  const current = encodeStockOption({
    lotNumber: row.querySelector(".op-line-lot").value || null,
    expiryDate: row.querySelector(".op-line-expiry").value || null
  });

  if (!sourceId || !skuId) {
    select.innerHTML = `<option value="">Select source and SKU</option>`;
    return;
  }

  select.innerHTML = `<option value="">Loading stock...</option>`;
  try {
    const options = await request(`/api/v1/inventory/stock-options?locationId=${encodeURIComponent(sourceId)}&skuId=${encodeURIComponent(skuId)}&entryMode=${encodeURIComponent(entryMode)}`);
    if (!options.length) {
      select.innerHTML = `<option value="">No non-expired stock</option>`;
      row.querySelector(".op-line-lot").value = "";
      row.querySelector(".op-line-expiry").value = "";
      return;
    }

    select.innerHTML = `<option value="">Select batch / expiry</option>${options.map((option) => {
      const value = encodeStockOption(option);
      const quantity = entryMode === "Pieces" && option.pieceQuantity != null
        ? `${option.pieceQuantity} pieces`
        : `${option.packQuantity} packs`;
      const loose = entryMode === "Pieces" && option.loosePieceQuantity > 0 ? `, ${option.loosePieceQuantity} loose` : "";
      return `<option value="${escapeHtml(value)}">${escapeHtml(`${option.expiryDate || "No expiry"} / ${option.lotNumber || "No lot"} / ${quantity}${loose}`)}</option>`;
    }).join("")}`;
    if (current && Array.from(select.options).some((option) => option.value === current)) {
      select.value = current;
    } else {
      row.querySelector(".op-line-lot").value = "";
      row.querySelector(".op-line-expiry").value = "";
    }
  } catch (exception) {
    select.innerHTML = `<option value="">Failed to load stock</option>`;
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function applySelectedStockOption(row) {
  const select = row.querySelector(".op-line-stock-option");
  if (!select?.value) {
    row.querySelector(".op-line-lot").value = "";
    row.querySelector(".op-line-expiry").value = "";
    return;
  }

  try {
    const option = JSON.parse(decodeURIComponent(select.value));
    row.querySelector(".op-line-lot").value = option.lotNumber || "";
    row.querySelector(".op-line-expiry").value = option.expiryDate || "";
  } catch {
    row.querySelector(".op-line-lot").value = "";
    row.querySelector(".op-line-expiry").value = "";
  }
}

function encodeStockOption(option) {
  if (!option || (!option.lotNumber && !option.expiryDate)) {
    return "";
  }

  return encodeURIComponent(JSON.stringify({
    lotNumber: option.lotNumber || null,
    expiryDate: option.expiryDate || null
  }));
}

async function loadOperations() {
  const tbody = document.getElementById("operation-rows");
  const count = document.getElementById("operation-count");
  const auth = getAuth();
  const canWrite = ["Admin", "ERPAdmin", "WarehouseClerk"].includes(auth?.user.role);
  try {
    const result = await request("/api/v1/operations?pageSize=50");
    const showCompleted = document.getElementById("operations-show-completed")?.checked;
    const items = showCompleted
      ? result.items
      : result.items.filter((operation) => !["Received", "Completed", "Confirmed", "Cancelled"].includes(operation.status));
    count.textContent = showCompleted ? `${result.totalCount} operations` : `${items.length} active`;
    tbody.innerHTML = items.length === 0 ? `<tr><td colspan="6">No active operations.</td></tr>` : items.map((operation) => `
      <tr>
        <td><strong>${escapeHtml(operation.operationNumber)}</strong>${operation.salesChannel === "Shopify" ? `<span class="status-pill status-warn">Shopify${operation.shopifyOrderNumber ? ` ${escapeHtml(operation.shopifyOrderNumber)}` : ""}</span>` : ""}${operation.allocationPending ? `<span class="status-pill status-muted">Allocation pending</span>` : ""}</td>
        <td>${escapeHtml(operation.operationType)}</td>
        <td><span class="status-pill ${operationStatusClass(operation.status)}">${escapeHtml(operation.status)}</span></td>
        <td>${escapeHtml(formatOperationRoute(operation))}</td>
        <td>${escapeHtml(formatDateTime(operation.createdAt))}</td>
        <td>${renderOperationActions(operation, canWrite)}</td>
      </tr>
      <tr class="operation-detail-row" id="operation-detail-${escapeHtml(operation.id)}" hidden><td colspan="6"><div class="operation-detail">Loading</div></td></tr>`).join("");
    tbody.querySelectorAll("[data-op-toggle]").forEach((button) => button.addEventListener("click", () => toggleOperationDetails(button.dataset.opId, button)));
    tbody.querySelectorAll("[data-op-action]").forEach((button) => button.addEventListener("click", () => runOperationAction(button.dataset.opAction, button.dataset.opId, button)));
    tbody.querySelectorAll("[data-op-edit]").forEach((button) => button.addEventListener("click", () => startOperationEditorMode(button.dataset.opEdit, "edit")));
    tbody.querySelectorAll("[data-op-revise]").forEach((button) => button.addEventListener("click", () => startOperationEditorMode(button.dataset.opRevise, "revise")));
    bindPrintReportButtons(tbody);
    for (const operationId of operationsUiState.openDetailIds) {
      const toggle = tbody.querySelector(`[data-op-toggle][data-op-id="${operationId}"]`);
      if (toggle) {
        // eslint-disable-next-line no-await-in-loop
        await toggleOperationDetails(operationId, toggle, true);
      }
    }
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function submitOperationEditor(event) {
  event.preventDefault();
  const type = document.getElementById("op-type").value;
  const lines = readOperationLines(type);
  const isShopifyDraft = operationsUiState.mode === "edit" && document.getElementById("operation-form")?.dataset.shopifyDraft === "true";
  if (isShopifyDraft && operationsUiState.operationId) {
    if (lines.some((line) => !line.operationLineId || !line.expiryDate || !line.stockOptionSelected)) {
      notice("Select a batch and expiry for every Shopify line.", "error");
      return;
    }
    try {
      await request(`/api/v1/operations/${operationsUiState.operationId}/shopify-allocation`, {
        method: "PUT",
        body: JSON.stringify({ lines: lines.map((line) => ({ operationLineId: line.operationLineId, lotNumber: line.lotNumber, expiryDate: line.expiryDate })) })
      });
      notice("Shopify batch allocation saved.", "success");
      resetOperationEditorMode();
      await loadOperations();
    } catch (exception) {
      notice(getFriendlyWorkspaceError(exception), "error");
    }
    return;
  }
  const validationMessage = validateOperationForm(type, lines);
  if (validationMessage) {
    notice(validationMessage, "error");
    return;
  }
  const payloadLines = lines.map(({ stockOptionSelected, ...line }) => line);

  const body = {
    operationType: type,
    sourceLocationId: document.getElementById("op-source").value || null,
    destinationLocationId: document.getElementById("op-destination").value || null,
    merchantId: ["WholesaleSale", "RetailSale", "Return", "Change"].includes(type) ? document.getElementById("op-merchant").value || null : null,
    representativeId: type === "Reserve" ? document.getElementById("op-representative").value || null : null,
    buyerName: type === "RetailSale" ? document.getElementById("op-buyer").value || null : null,
    buyerPhone: type === "RetailSale" ? document.getElementById("op-buyer-phone").value || null : null,
    paymentMethod: ["WholesaleSale", "RetailSale", "Return", "Change"].includes(type) ? canonicalSelectValue("op-payment") || null : null,
    notes: document.getElementById("op-notes").value || null,
    receipt: type === "InventoryReceipt" ? { supplierName: document.getElementById("op-supplier").value || "Supplier", invoiceNumber: document.getElementById("op-invoice").value || null } : null,
    lines: payloadLines
  };

  try {
    if (operationsUiState.mode === "edit" && operationsUiState.operationId) {
      await request(`/api/v1/operations/${operationsUiState.operationId}`, { method: "PUT", body: JSON.stringify(body) });
      notice("Draft updated.", "success");
    } else if (operationsUiState.mode === "revise" && operationsUiState.operationId) {
      const reason = document.getElementById("op-revision-reason").value?.trim();
      if (!reason) {
        notice("Revision reason is required.", "error");
        return;
      }
      await request(`/api/v1/operations/${operationsUiState.operationId}/revise`, {
        method: "POST",
        body: JSON.stringify({ operation: body, reason })
      });
      notice("Operation revised.", "success");
    } else {
      await request("/api/v1/operations", { method: "POST", body: JSON.stringify(body) });
      notice("Draft saved.", "success");
    }
    resetOperationEditorMode();
    await loadOperations();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function readOperationLines(type) {
  return Array.from(document.querySelectorAll(".line-editor-row")).map((row) => ({
    operationLineId: row.dataset.operationLineId || null,
    skuId: row.querySelector(".op-line-sku").value,
    packQuantity: row.querySelector(".op-line-entry-mode").value === "Pieces" ? 0 : Number(row.querySelector(".op-line-qty").value),
    pieceQuantity: row.querySelector(".op-line-entry-mode").value === "Pieces" ? Number(row.querySelector(".op-line-qty").value) : null,
    entryMode: type === "RetailSale" ? canonicalSystemValue(row.querySelector(".op-line-entry-mode").value) : "Packs",
    section: type === "Change" ? canonicalSystemValue(row.querySelector(".op-line-section").value) : null,
    unitPrice: row.querySelector(".op-line-bonus").checked ? 0 : Number(row.querySelector(".op-line-price").value || 0),
    isBonus: ["WholesaleSale", "RetailSale"].includes(type) ? row.querySelector(".op-line-bonus").checked : false,
    stockOptionSelected: isOperationBatchSelectionType(type) ? Boolean(row.querySelector(".op-line-stock-option").value) : false,
    expiryDate: isOperationBatchSelectionType(type) || type === "InventoryReceipt" || type === "Return" || (type === "Change" && row.querySelector(".op-line-section").value === "ChangeOut") ? row.querySelector(".op-line-expiry").value || null : null,
    lotNumber: isOperationBatchSelectionType(type) || type === "InventoryReceipt" || type === "Return" || (type === "Change" && row.querySelector(".op-line-section").value === "ChangeOut") ? row.querySelector(".op-line-lot").value || null : null,
    notes: null
  }));
}

function validateOperationForm(type, lines) {
  const source = document.getElementById("op-source").value;
  const destination = document.getElementById("op-destination").value;
  const main = operationLocations.find((location) => location.locationType === "MainWarehouse");

  if (lines.length === 0) {
    return "Add at least one operation line.";
  }
  if (lines.some((line) => !line.skuId)) {
    return "Select a SKU for every line.";
  }
  if (new Set(lines.map((line) => operationLineUniquenessKey(type, line))).size !== lines.length) {
    return "Each SKU can appear once per side. Sales may use one paid line and one bonus line for the same SKU.";
  }
  if (lines.some((line) => {
    const quantity = line.entryMode === "Pieces" ? line.pieceQuantity : line.packQuantity;
    return !Number.isInteger(quantity) || quantity < 1;
  })) {
    return "Every pack quantity must be a whole number greater than zero.";
  }
  if (!main) {
    return "MainWarehouse must exist before operations can be created.";
  }
  if (type === "InventoryReceipt" && destination !== main.id) {
    return "Inventory receipt destination must be MainWarehouse.";
  }
  if (type === "InventoryReceipt" && lines.some((line) => {
    const sku = operationSkuOptions.find((value) => value.id === line.skuId);
    const product = operationProductOptions.find((value) => value.id === sku?.productId);
    return product?.expiryType === "Batch" && !line.expiryDate;
  })) {
    return "Batch expiry is required for products with batch expiry tracking.";
  }
  if (type === "WarehouseTransfer" && (source !== main.id || !destination || destination === main.id)) {
    return "Warehouse transfer must move packs from MainWarehouse to a non-main destination.";
  }
  if (isOperationStockConsumingType(type) && !source) {
    return "Select a source location before choosing stock.";
  }
  if (type === "WholesaleSale" && !document.getElementById("op-merchant").value) {
    return "Wholesale sale requires a merchant.";
  }
  if (["WholesaleSale", "RetailSale"].includes(type) && lines.some((line) => !line.isBonus && (!Number.isFinite(line.unitPrice) || line.unitPrice <= 0))) {
    return "Sale line unit price must be greater than zero unless the line is marked as bonus.";
  }
  if (isOperationBatchSelectionType(type) && lines.some((line) => !line.stockOptionSelected || !line.expiryDate)) {
    return "Select a batch / expiry for every stock-consuming line.";
  }
  if (type === "RetailSale" && ["Installment"].includes(canonicalSelectValue("op-payment")) && !document.getElementById("op-merchant").value) {
    return "Retail installment sales require a registered merchant.";
  }
  if (type === "Reserve" && !document.getElementById("op-representative").value) {
    return "Reserve requires a representative.";
  }
  if (type === "Return" && !document.getElementById("op-merchant").value) {
    return "Return requires a merchant.";
  }
  if (type === "Return" && lines.some((line) => !line.expiryDate)) {
    return "Return lines must include batch expiry.";
  }
  if (type === "Change") {
    if (!document.getElementById("op-merchant").value) {
      return "Change requires a merchant.";
    }
    if (!lines.some((line) => line.section === "ChangeOut") || !lines.some((line) => line.section === "ChangeIn")) {
      return "Change needs at least one returned line and one replacement line.";
    }
    if (lines.some((line) => line.section === "ChangeOut" && !line.expiryDate)) {
      return "Returned change lines must include batch expiry.";
    }
  }

  return "";
}

function operationLineUniquenessKey(type, line) {
  const section = type === "Change" ? line.section || "ChangeOut" : "Standard";
  const bonus = ["WholesaleSale", "RetailSale"].includes(type) && line.isBonus === true ? "Bonus" : "Paid";
  return `${line.skuId}:${section}:${bonus}:${line.entryMode}:${line.lotNumber || ""}:${line.expiryDate || ""}`;
}

function renderOperationActions(operation, canWrite) {
  const detailButton = `<button class="button secondary table-action" type="button" data-op-toggle="details" data-op-id="${escapeHtml(operation.id)}">Details</button>`;
  const printButton = `<button class="button secondary table-action" type="button" data-print-report="operation-bill" data-print-id="${escapeHtml(operation.id)}" data-print-code="${escapeHtml(operation.operationNumber)}">Print</button>`;
  if (!canWrite) {
    return `${detailButton} ${printButton}`;
  }
  const actions = [];
  if (operation.status === "Draft") {
    actions.push(["edit-draft", "Edit"]);
  } else if (getAuth()?.user?.role === "Admin" && operation.status !== "Cancelled" && operation.salesChannel !== "Shopify") {
    actions.push(["revise", "Revise"]);
  }
  const shippingOperationTypes = ["WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve"];
  if (operation.status === "Draft") {
    actions.push(["confirm", "Confirm"], ["cancel", "Cancel"]);
  } else if (shippingOperationTypes.includes(operation.operationType) && operation.status === "Reserved") {
    actions.push(["ship", "Ship"], [operation.operationType === "WholesaleSale" || operation.operationType === "RetailSale" ? "complete" : "receive", operation.operationType === "WarehouseTransfer" ? "Receive" : "Complete"], ["cancel", "Cancel"]);
  } else if (shippingOperationTypes.includes(operation.operationType) && operation.status === "Shipped") {
    actions.push([operation.operationType === "WholesaleSale" || operation.operationType === "RetailSale" ? "complete" : "receive", operation.operationType === "WarehouseTransfer" ? "Receive" : "Complete"]);
  }

  return actions.length === 0
    ? `${detailButton} ${printButton}`
    : `${detailButton} ${printButton} ${actions.map(([action, label]) => {
      if (action === "edit-draft") {
        return `<button class="button secondary table-action" type="button" data-op-edit="${escapeHtml(operation.id)}">${label}</button>`;
      }
      if (action === "revise") {
        return `<button class="button secondary table-action" type="button" data-op-revise="${escapeHtml(operation.id)}">${label}</button>`;
      }
      return `<button class="button secondary table-action" type="button" data-op-action="${action}" data-op-id="${escapeHtml(operation.id)}">${label}</button>`;
    }).join(" ")}`;
}

async function toggleOperationDetails(operationId, button, forceOpen = false) {
  const row = document.getElementById(`operation-detail-${operationId}`);
  if (!row) {
    return;
  }

  if (!row.hidden && !forceOpen) {
    row.hidden = true;
    button.textContent = "Details";
    operationsUiState.openDetailIds = operationsUiState.openDetailIds.filter((value) => value !== operationId);
    return;
  }

  row.hidden = false;
  button.textContent = "Hide";
  if (!operationsUiState.openDetailIds.includes(operationId)) {
    operationsUiState.openDetailIds.push(operationId);
  }
  const target = row.querySelector(".operation-detail");
  if (target.dataset.loaded === "true") {
    return;
  }

  target.innerHTML = `<span class="muted-text">Loading operation details...</span>`;
  try {
    const detail = await request(`/api/v1/operations/${operationId}`);
    target.innerHTML = renderOperationDetail(detail);
    target.dataset.loaded = "true";
  } catch (exception) {
    target.innerHTML = `<span class="muted-text">${escapeHtml(getFriendlyWorkspaceError(exception))}</span>`;
  }
}

function renderOperationDetail(detail) {
  const lines = detail.lines || [];
  const allocations = dedupeOperationAllocations(detail.allocations || []);
  const versions = detail.versions || [];
  return `
    <div class="operation-detail-grid">
      <div class="metric"><span>Operation code</span><strong>${escapeHtml(detail.operationNumber)}</strong></div>
      <div class="metric"><span>Status</span><strong>${escapeHtml(detail.status)}</strong></div>
      <div class="metric"><span>Type</span><strong>${escapeHtml(detail.operationType)}</strong></div>
      <div class="metric"><span>Created</span><strong>${escapeHtml(formatDateTime(detail.createdAt))}</strong></div>
      <div class="metric"><span>Confirmed</span><strong>${escapeHtml(formatDateTime(detail.confirmedAt) || "-")}</strong></div>
      <div class="metric"><span>Created by</span><strong>${escapeHtml(detail.createdByName || detail.createdBy || "-")}</strong></div>
      <div class="metric"><span>Confirmed by</span><strong>${escapeHtml(detail.confirmedByName || detail.confirmedBy || "-")}</strong></div>
      <div class="metric"><span>Last edited by</span><strong>${escapeHtml(detail.lastEditedByName || "-")}</strong></div>
      <div class="metric"><span>Route</span><strong>${escapeHtml(formatOperationRoute(detail))}</strong></div>
      <div class="metric"><span>Merchant / buyer</span><strong>${escapeHtml(detail.clientName || "-")}</strong></div>
      <div class="metric"><span>Representative</span><strong>${escapeHtml(detail.representativeName || "-")}</strong></div>
      <div class="metric"><span>Payment</span><strong>${escapeHtml(detail.paymentMethod || "-")}</strong></div>
      <div class="metric"><span>Channel</span><strong>${escapeHtml(detail.salesChannel || "Manual")}${detail.shopifyOrderNumber ? ` / ${escapeHtml(detail.shopifyOrderNumber)}` : ""}</strong></div>
      <div class="metric"><span>Buyer contact</span><strong>${escapeHtml([detail.buyerPhone, detail.buyerEmail].filter(Boolean).join(" / ") || "-")}</strong></div>
      ${detail.shippingAddress ? `<div class="metric"><span>Shipping address</span><strong>${escapeHtml(detail.shippingAddress)}</strong></div>` : ""}
      ${detail.allocationPending ? `<div class="metric"><span>Allocation</span><strong><span class="status-pill status-warn">Batch allocation pending</span></strong></div>` : ""}
      <div class="metric"><span>Current version</span><strong>${escapeHtml(detail.currentVersionNumber || "-")}</strong></div>
    </div>
    ${detail.notes ? `<p class="muted-text">${escapeHtml(detail.notes)}</p>` : ""}
    <div class="table-wrap compact-table"><table><thead><tr><th>SKU</th><th>Product</th><th>Shopify line</th><th>Wear cycle</th><th>Side</th><th>Quantity</th><th>Bonus</th><th>Unit price</th><th>Total</th><th>Lot</th><th>Batch expiry</th><th>Notes</th></tr></thead><tbody>${lines.length === 0
      ? `<tr><td colspan="12">No lines.</td></tr>`
      : lines.map((line) => `<tr>
          <td><strong>${escapeHtml(line.skuCode)}</strong></td>
          <td>${escapeHtml(line.productName)}</td>
          <td>${renderShopifyLineMetadata(line)}</td>
          <td>${renderWearCycle(line.wearCycle, line.wearDuration)}</td>
          <td>${escapeHtml(formatOperationLineSection(line.section))}</td>
          <td>${escapeHtml(line.quantity)} ${escapeHtml(line.entryMode || "Packs")}</td>
          <td>${line.bonusQuantity ? `<span class="status-pill status-warn">${escapeHtml(line.bonusQuantity)}</span>` : "-"}</td>
          <td>${escapeHtml(formatMoney(line.unitPrice || 0))}</td>
          <td>${escapeHtml(formatMoney(line.lineTotal || 0))}</td>
          <td>${escapeHtml(line.lotNumber || "-")}</td>
          <td>${line.expiryDate ? expiryBadge(line.expiryDate) : `<span class="status-pill status-muted">-</span>`}</td>
          <td>${escapeHtml(line.notes || "-")}</td>
        </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>Allocated SKU</th><th>Quantity</th><th>Lot</th><th>Batch expiry</th></tr></thead><tbody>${allocations.length === 0
      ? `<tr><td colspan="4">No batch allocation snapshot.</td></tr>`
      : allocations.map((allocation) => `<tr>
          <td><strong>${escapeHtml(allocation.skuCode || shortId(allocation.skuId, "SKU"))}</strong>${allocation.productName ? `<span class="muted-cell"> / ${escapeHtml(allocation.productName)}</span>` : ""}</td>
          <td>${escapeHtml(allocation.quantity)} pack(s)</td>
          <td>${escapeHtml(allocation.lotNumber || "-")}</td>
          <td>${allocation.expiryDate ? expiryBadge(allocation.expiryDate) : `<span class="status-pill status-muted">No expiry</span>`}</td>
        </tr>`).join("")}</tbody></table></div>
    <div class="operation-version-list">${versions.length === 0
      ? `<span class="muted-text">No versions.</span>`
      : versions.map((version) => `<span class="status-pill status-muted">v${escapeHtml(version.versionNumber)} ${escapeHtml(version.reason)} - ${escapeHtml(formatDateTime(version.editedAt))} - ${escapeHtml(version.editedByName || "-")}</span>`).join("")}</div>`;
}

function renderShopifyLineMetadata(line) {
  if (!line.shopifyLineItemId) return "-";
  let properties = [];
  try {
    properties = JSON.parse(line.shopifyProperties || "[]");
  } catch {
    properties = [];
  }
  const label = [line.shopifyTitle, line.shopifyVariantTitle].filter(Boolean).join(" / ");
  const propertyText = properties.map((property) => `${property.name}: ${property.value}`).join(" / ");
  return `<strong>${escapeHtml(line.shopifySku || "-")}</strong><div class="muted-cell">${escapeHtml(label || `Line ${line.shopifyLineItemId}`)}</div>${propertyText ? `<div class="muted-cell">${escapeHtml(propertyText)}</div>` : ""}`;
}

function formatOperationLineSection(section) {
  if (section === "ChangeOut") {
    return "Returned";
  }
  if (section === "ChangeIn") {
    return "Replacement";
  }
  return "-";
}

function dedupeOperationAllocations(allocations) {
  const grouped = new Map();
  for (const allocation of allocations) {
    const key = `${allocation.skuId || ""}:${allocation.batchId || ""}:${allocation.lotNumber || ""}:${allocation.expiryDate || ""}`;
    const current = grouped.get(key);
    if (current) {
      current.quantity += Number(allocation.quantity || 0);
      continue;
    }
    grouped.set(key, { ...allocation, quantity: Number(allocation.quantity || 0) });
  }
  return Array.from(grouped.values()).filter((allocation) => allocation.quantity > 0);
}

function operationStatusClass(status) {
  if (status === "Received" || status === "Completed" || status === "Confirmed") {
    return "status-ok";
  }
  if (status === "Cancelled") {
    return "status-muted";
  }
  if (status === "Reserved" || status === "Shipped") {
    return "status-warn";
  }
  return "status-muted";
}

function formatOperationRoute(operation) {
  if (operation.operationType === "InventoryReceipt") {
    return `External -> ${operation.destinationLocationName || "MainWarehouse"}`;
  }
  return `${operation.sourceLocationName || "MainWarehouse"} -> ${operation.destinationLocationName || "-"}`;
}

async function runOperationAction(action, operationId, button, options = {}) {
  return withMutationGuard(`operation:${operationId}:${action}`, button, async () => {
    const previousLabel = button?.textContent;
    if (button) {
      button.textContent = "Working";
    }

    try {
      const path = `/api/v1/operations/${operationId}/${action}`;
      try {
        await request(path, {
          method: "POST",
          body: options.body ? JSON.stringify(options.body) : undefined
        });
      } catch (exception) {
        const gate = action === "confirm" ? parseMerchantSalesVarianceGate(exception) : null;
        if (!gate) throw exception;
        const bypass = await merchantSalesVarianceDialog(gate);
        if (!bypass) {
          await loadOperations();
          return;
        }
        await request(path, { method: "POST", body: JSON.stringify(bypass) });
      }
      notice(`Operation ${action} completed.`, "success");
      await Promise.all([
        loadOperations(),
        currentPath() === "/inventory" ? refreshInventoryTables() : Promise.resolve()
      ]);
    } catch (exception) {
      notice(getFriendlyWorkspaceError(exception), "error");
      await loadOperations();
    } finally {
      if (button && previousLabel) {
        button.textContent = previousLabel;
      }
    }
  });
}

async function renderPayments() {
  const auth = getAuth();
  const isAdmin = isSystemAdminRole(auth?.user.role);
  const canDraft = ["Admin", "ERPAdmin", "Accountant"].includes(auth?.user.role);
  const merchants = await loadPaymentMerchants();
  paymentMerchants = merchants;
  paymentAccountants = isAdmin ? await loadPaymentAccountants() : [];
  const accountantOptions = paymentAccountants.map((user) => `<option value="${escapeHtml(user.id)}">${escapeHtml(user.fullName || user.username)} (${escapeHtml(user.username)})</option>`).join("");
  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: "Payments",
      title: "Payments and remaining",
      body: "Handle open confirmations first, then use the ledger and tools for audit, entries, cash records, adjustments, and merchant remaining.",
      metrics: `
        ${scenarioCard("Queue", "Loading", "status-muted", "payment-count")}
        ${scenarioCard("Ledger", "Loading", "status-muted", "payment-history-count")}
        ${scenarioCard("Tools", canDraft || isAdmin ? "Available" : "Read only", canDraft || isAdmin ? "status-ok" : "status-muted")}
      `
    })}
    ${segmentedControl([
      { target: "payment-queue-section", label: "Queue" },
      { target: "payment-ledger-section", label: "Ledger" },
      { target: "payment-tools-section", label: "Tools" }
    ])}
    <section id="payment-queue-section" class="band payment-queue-band">
      <div class="section-head">
        <div><h2>Confirmations queue</h2><p>Open installment and cash confirmations that still need assignment, accountant action, or admin approval.</p></div>
      </div>
      <div class="toolbar">
        <button id="payments-refresh" class="button secondary" type="button">Refresh</button>
        ${isAdmin ? `<select id="payment-accountant" class="select"><option value="">Assign to accountant...</option>${accountantOptions}</select>` : ""}
      </div>
      <div class="table-wrap"><table><thead><tr><th>Payment</th><th>Buyer</th><th>Operation</th><th>Method</th><th>Total</th><th>Paid</th><th>Remaining</th><th>Status</th><th>Actions</th></tr></thead><tbody id="payment-rows"><tr><td colspan="9">Loading payments</td></tr></tbody></table></div>
    </section>
    <section id="payment-ledger-section" class="band payment-ledger-band">
      <div class="section-head">
        <div><h2>Payment ledger</h2><p>One row per payment with stages, sub-logs, cash records, refunds, and adjustments inside expanded detail.</p></div>
      </div>
      <div class="table-wrap"><table><thead><tr><th>Updated</th><th>Payment</th><th>Buyer / merchant</th><th>Operation</th><th>Method</th><th>Total</th><th>Status</th><th>Actor</th><th>Stages</th></tr></thead><tbody id="payment-history-rows"><tr><td colspan="9">Loading history</td></tr></tbody></table></div>
    </section>
    <section id="payment-tools-section" class="payment-tools-grid">
    ${canDraft ? `
      <section class="band compact-band payment-tool-card">
        <h2>Draft payment entry</h2>
        <form id="payment-sublog-form" class="form grid-form">
          <div class="field"><label for="payment-log-id">Payment log reference</label><input id="payment-log-id" class="input" required></div>
          <div class="field"><label for="payment-amount">Amount</label><input id="payment-amount" class="input" type="number" min="0.00" step="0.01" value="0"></div>
          <div class="field"><label for="payment-method">Method</label><select id="payment-method" class="select"><option value="CashTransaction">Cash transaction</option><option value="Installment">Installment</option><option value="CashHandToHand">Cash hand to hand</option></select></div>
          <div class="field"><label for="payment-date">Date received</label><input id="payment-date" class="input" type="date"></div>
          <div class="field full-span"><label for="payment-notes">Notes</label><input id="payment-notes" class="input"></div>
          <button class="button" type="submit">Draft sub-log</button>
        </form>
      </section>` : ""}
    ${isAdmin ? `
      <section class="band compact-band payment-tool-card">
        <h2>Cash receipt record</h2>
        <form id="cash-record-form" class="form grid-form">
          <div class="field"><label for="cash-operation-id">Operation reference</label><input id="cash-operation-id" class="input" required></div>
          <div class="field"><label for="cash-type">Type</label><select id="cash-type" class="select"><option value="CashReceived">Cash received</option></select></div>
          <div class="field"><label for="cash-amount">Amount</label><input id="cash-amount" class="input" type="number" min="0.01" step="0.01" required></div>
          <div class="field full-span"><label for="cash-notes">Notes</label><input id="cash-notes" class="input"></div>
          <button class="button" type="submit">Record cash</button>
        </form>
      </section>` : ""}
      ${canDraft ? `<section class="band compact-band payment-tool-card">
        <h2>Financial adjustment</h2>
        <p class="muted-text">Submit a source-linked request. Admin or ERPAdmin approval posts the financial effect.</p>
        <form id="financial-adjustment-form" class="form grid-form">
          <div class="form-error full-span" id="financial-adjustment-error" hidden></div>
          <div class="field"><label for="adjustment-merchant">Merchant</label><select id="adjustment-merchant" class="select" required>${merchants.map((merchant) => `<option value="${escapeHtml(merchant.id)}">${escapeHtml(merchant.businessName)}</option>`).join("")}</select></div>
          <div class="field"><label for="adjustment-type">Type</label><select id="adjustment-type" class="select"><option value="MerchantCredit">Merchant credit</option><option value="BalanceReduction">Remaining reduction</option><option value="CashRefund">Cash refund</option></select></div>
          <div class="field"><label for="adjustment-operation-id">Operation ID</label><input id="adjustment-operation-id" class="input" placeholder="Required source"></div>
          <div class="field"><label for="adjustment-amount">Amount</label><input id="adjustment-amount" class="input" type="number" min="0.01" step="0.01" required></div>
          <div class="field full-span"><label for="adjustment-notes">Notes</label><input id="adjustment-notes" class="input"></div>
          <button class="button" type="submit">Request adjustment</button>
        </form>
      </section>` : ""}
    <section class="band compact-band payment-tool-card merchant-tool-card">
      <div class="section-head"><h2>Merchant remaining</h2><span id="merchant-balance-status" class="muted-text">Select a merchant</span></div>
      <div class="toolbar"><select id="payment-merchant" class="select">${merchants.map((merchant) => `<option value="${escapeHtml(merchant.id)}">${escapeHtml(merchant.businessName)}</option>`).join("")}</select><button id="load-merchant-balance" class="button secondary" type="button">Load remaining</button></div>
      <div id="merchant-balance-panel" class="detail-grid"></div>
    </section>
    </section>`;

  document.getElementById("payments-refresh").addEventListener("click", () => Promise.all([loadPayments(), loadPaymentHistory()]));
  document.getElementById("payment-sublog-form")?.addEventListener("submit", draftPaymentSubLog);
  document.getElementById("cash-record-form")?.addEventListener("submit", createCashRecord);
  document.getElementById("financial-adjustment-form")?.addEventListener("submit", createFinancialAdjustment);
  document.getElementById("load-merchant-balance").addEventListener("click", loadMerchantBalance);
  await Promise.all([loadPayments(), loadPaymentHistory()]);
}

async function loadPaymentMerchants() {
  try {
    const merchants = await request("/api/v1/crm/merchants?includeInactive=true&pageSize=200");
    return merchants.items || [];
  } catch {
    return [];
  }
}

async function loadPaymentAccountants() {
  try {
    const users = await request("/api/v1/users");
    return users.filter((user) => user.role === "Accountant" && user.isActive);
  } catch {
    return [];
  }
}

async function loadPayments() {
  const tbody = document.getElementById("payment-rows");
  const count = document.getElementById("payment-count");
  if (!tbody || !count) {
    return;
  }
  const auth = getAuth();
  const isAdmin = isSystemAdminRole(auth?.user.role);
  const isAccountant = auth?.user.role === "Accountant";
  const canDraft = ["Admin", "ERPAdmin", "Accountant"].includes(auth?.user.role);
  try {
    let result;
    try {
      result = await request("/api/v1/payments?pageSize=50");
    } catch (firstError) {
      await new Promise((resolve) => setTimeout(resolve, 500));
      result = await request("/api/v1/payments?pageSize=50");
    }
    const queueItems = (result.items || []).filter((log) =>
      ["Installment", "CashHandToHand", "CashTransaction"].includes(log.paymentMethod) &&
      ["PendingAdmin", "PendingAccountant", "PendingAdminReview"].includes(log.status));
    count.textContent = `${queueItems.length} open confirmation${queueItems.length === 1 ? "" : "s"}`;
    tbody.innerHTML = queueItems.length === 0
      ? `<tr><td colspan="9">No payment confirmations are waiting.</td></tr>`
      : queueItems.map((log) => `
        <tr>
          <td>${canDraft ? `<button class="button secondary table-action" type="button" data-payment-use="${escapeHtml(log.id)}">Use</button>` : ""}<strong>${escapeHtml(shortId(log.id, "PAY"))}</strong></td>
          <td><strong>${escapeHtml(log.buyerName || "Unknown buyer")}</strong><div class="muted-cell">${escapeHtml(shortId(log.merchantId, "MER"))}</div></td>
          <td><strong>${escapeHtml(log.operationNumber || shortId(log.operationId, "OP"))}</strong><div class="muted-cell">${escapeHtml(log.operationType || "-")}</div></td>
          <td>${escapeHtml(log.paymentMethod)}</td>
          <td>${escapeHtml(formatMoney(log.totalAmount))}</td>
          <td>${escapeHtml(formatMoney(log.amountPaid))}</td>
          <td>${escapeHtml(formatMoney(log.remainingAmount))}</td>
          <td><span class="status-pill ${log.status === "Completed" ? "status-ok" : "status-warn"}">${escapeHtml(log.status)}</span><div class="muted-cell">By ${escapeHtml(log.initializedByName || log.lastModifiedByName || "-")}</div></td>
          <td><button class="button secondary table-action" type="button" data-payment-detail="${escapeHtml(log.id)}">Details</button><button class="button secondary table-action" type="button" data-print-report="${log.paymentMethod === "CashHandToHand" ? "cash-receipt" : "payment-receipt"}" data-print-id="${escapeHtml(log.id)}" data-print-code="${escapeHtml(shortId(log.id, "PAY"))}">Print</button>${isAdmin && log.status !== "Completed" ? `<button class="button secondary table-action" type="button" data-payment-assign="${escapeHtml(log.id)}">Assign</button>` : ""}${isAdmin && log.paymentMethod === "CashHandToHand" && log.status === "PendingAccountant" ? `<button class="button secondary table-action" type="button" data-cash-approve="${escapeHtml(log.id)}">Approve cash</button>` : ""}</td>
        </tr>
        <tr class="operation-detail-row" id="payment-detail-${escapeHtml(log.id)}" hidden><td colspan="9"><div class="operation-detail">Loading</div></td></tr>`).join("");
    tbody.querySelectorAll("[data-payment-use]").forEach((button) => button.addEventListener("click", () => {
      const logInput = document.getElementById("payment-log-id");
      if (logInput) {
        logInput.value = button.dataset.paymentUse;
      }
    }));
    tbody.querySelectorAll("[data-payment-detail]").forEach((button) => button.addEventListener("click", () => togglePaymentDetails(button.dataset.paymentDetail, button)));
    tbody.querySelectorAll("[data-payment-assign]").forEach((button) => button.addEventListener("click", () => assignPaymentLog(button.dataset.paymentAssign)));
    tbody.querySelectorAll("[data-cash-approve]").forEach((button) => button.addEventListener("click", () => approveCashReceipt(button.dataset.cashApprove)));
    bindPrintReportButtons(tbody);
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="9">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function loadPaymentHistory() {
  const tbody = document.getElementById("payment-history-rows");
  const count = document.getElementById("payment-history-count");
  if (!tbody || !count) {
    return;
  }

  try {
    const result = await request("/api/v1/payments?pageSize=200");
    paymentHistoryRows = result.items || [];
    count.textContent = `${paymentHistoryRows.length} record${paymentHistoryRows.length === 1 ? "" : "s"}`;
    tbody.innerHTML = paymentHistoryRows.length === 0
      ? `<tr><td colspan="9">No payment history yet.</td></tr>`
      : paymentHistoryRows.map((row) => `
        <tr>
          <td>${escapeHtml(formatDateTime(row.lastModifiedAt))}</td>
          <td><strong>${escapeHtml(shortId(row.id, "PAY"))}</strong><div class="muted-cell">${escapeHtml(row.status || "-")}</div></td>
          <td><strong>${escapeHtml(row.buyerName || "Unknown buyer")}</strong><div class="muted-cell">${escapeHtml(shortId(row.merchantId, "MER"))}</div></td>
          <td><strong>${escapeHtml(row.operationNumber || shortId(row.operationId, "OP"))}</strong><div class="muted-cell">${escapeHtml(row.operationType || "-")}</div></td>
          <td>${escapeHtml(row.paymentMethod || "-")}</td>
          <td>${escapeHtml(formatMoney(row.totalAmount))}</td>
          <td><span class="status-pill ${paymentHistoryStatusClass(row.status)}">${escapeHtml(row.status || "-")}</span></td>
          <td>${escapeHtml(row.lastModifiedByName || row.initializedByName || "-")}</td>
          <td><button class="button secondary table-action" type="button" data-payment-history-detail="${escapeHtml(row.id)}">Details</button><button class="button secondary table-action" type="button" data-print-report="${row.paymentMethod === "CashHandToHand" ? "cash-receipt" : "payment-receipt"}" data-print-id="${escapeHtml(row.id)}" data-print-code="${escapeHtml(shortId(row.id, "PAY"))}">Print</button></td>
        </tr>
        <tr class="operation-detail-row" id="payment-history-detail-${escapeHtml(row.id)}" hidden><td colspan="9"><div class="operation-detail">Loading</div></td></tr>`).join("");
    tbody.querySelectorAll("[data-payment-history-detail]").forEach((button) => button.addEventListener("click", () => togglePaymentHistoryDetails(button.dataset.paymentHistoryDetail, button)));
    bindPrintReportButtons(tbody);
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="9">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

function paymentHistoryStatusClass(status) {
  if (status === "Completed" || status === "Confirmed") {
    return "status-ok";
  }
  if (status === "Rejected" || status === "Cancelled") {
    return "status-muted";
  }
  return "status-warn";
}

async function togglePaymentDetails(id, button) {
  const row = document.getElementById(`payment-detail-${id}`);
  if (!row) {
    return;
  }
  if (!row.hidden) {
    row.hidden = true;
    button.textContent = "Details";
    return;
  }
  row.hidden = false;
  button.textContent = "Hide";
  const target = row.querySelector(".operation-detail");
  target.innerHTML = `<span class="muted-text">Loading payment details...</span>`;
  try {
    const detail = await request(`/api/v1/payments/${id}`);
    target.innerHTML = renderPaymentDetail(detail);
    target.querySelectorAll("[data-sublog-approve]").forEach((approve) => approve.addEventListener("click", () => approveSubLog(approve.dataset.sublogApprove, approve.dataset.paymentLogId)));
    target.querySelectorAll("[data-sublog-reject]").forEach((reject) => reject.addEventListener("click", () => rejectSubLog(reject.dataset.sublogReject, reject.dataset.paymentLogId)));
    target.querySelectorAll("[data-adjustment-approve]").forEach((approve) => approve.addEventListener("click", () => approveAdjustment(approve.dataset.adjustmentApprove, id)));
    target.querySelectorAll("[data-adjustment-reject]").forEach((reject) => reject.addEventListener("click", () => rejectAdjustment(reject.dataset.adjustmentReject, id)));
  } catch (exception) {
    target.innerHTML = `<span class="muted-text">${escapeHtml(getFriendlyWorkspaceError(exception))}</span>`;
  }
}

async function togglePaymentHistoryDetails(id, button) {
  const row = document.getElementById(`payment-history-detail-${id}`);
  if (!row) {
    return;
  }
  if (!row.hidden) {
    row.hidden = true;
    button.textContent = "Details";
    return;
  }
  row.hidden = false;
  button.textContent = "Hide";
  const target = row.querySelector(".operation-detail");
  target.innerHTML = `<span class="muted-text">Loading payment details...</span>`;
  try {
    const detail = await request(`/api/v1/payments/${id}`);
    target.innerHTML = renderPaymentDetail(detail);
    target.querySelectorAll("[data-sublog-approve]").forEach((approve) => approve.addEventListener("click", () => approveSubLog(approve.dataset.sublogApprove, approve.dataset.paymentLogId)));
    target.querySelectorAll("[data-sublog-reject]").forEach((reject) => reject.addEventListener("click", () => rejectSubLog(reject.dataset.sublogReject, reject.dataset.paymentLogId)));
    target.querySelectorAll("[data-adjustment-approve]").forEach((approve) => approve.addEventListener("click", () => approveAdjustment(approve.dataset.adjustmentApprove, id)));
    target.querySelectorAll("[data-adjustment-reject]").forEach((reject) => reject.addEventListener("click", () => rejectAdjustment(reject.dataset.adjustmentReject, id)));
  } catch (exception) {
    target.innerHTML = `<span class="muted-text">${escapeHtml(getFriendlyWorkspaceError(exception))}</span>`;
  }
}

function renderPaymentDetail(detail) {
  const isAdmin = isSystemAdminRole(getAuth()?.user.role);
  const subLogs = detail.subLogs || [];
  const cashRecords = detail.cashRecords || [];
  const adjustments = detail.adjustments || [];
  const stages = detail.stages || [];
  const log = detail.log || {};
  return `<div class="detail-stack">
    <div class="operation-detail-grid">
      <div class="metric"><span>Initialized by</span><strong>${escapeHtml(log.initializedByName || "-")}</strong></div>
      <div class="metric"><span>Assigned to</span><strong>${escapeHtml(log.assignedToName || "-")}</strong></div>
      <div class="metric"><span>Last modified by</span><strong>${escapeHtml(log.lastModifiedByName || "-")}</strong></div>
      <div class="metric"><span>Status</span><strong>${escapeHtml(log.status || "-")}</strong></div>
    </div>
    <div class="table-wrap compact-table"><table><thead><tr><th>Stage</th><th>When</th><th>Actor</th><th>Method</th><th>Amount</th><th>Status</th><th>Notes</th></tr></thead><tbody>${stages.length === 0
    ? `<tr><td colspan="7">No stage history yet.</td></tr>`
    : stages.map((stage) => `<tr>
        <td>${escapeHtml(paymentStageLabel(stage.stageType))}</td>
        <td>${escapeHtml(formatDateTime(stage.happenedAt))}</td>
        <td>${escapeHtml(stage.actorName || "-")}</td>
        <td>${escapeHtml(stage.paymentMethod || "-")}</td>
        <td>${escapeHtml(formatMoney(stage.amount))}</td>
        <td><span class="status-pill ${paymentHistoryStatusClass(stage.status)}">${escapeHtml(stage.status || "-")}</span></td>
        <td>${escapeHtml(stage.notes || "-")}</td>
      </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>Amount</th><th>Method</th><th>Date</th><th>Status</th><th>Drafted</th><th>Decision</th><th>Actions</th></tr></thead><tbody>${subLogs.length === 0
    ? `<tr><td colspan="7">No sub-logs yet.</td></tr>`
    : subLogs.map((sub) => `<tr>
        <td>${escapeHtml(formatMoney(sub.amount))}</td>
        <td>${escapeHtml(sub.paymentMethod || "-")}</td>
        <td>${escapeHtml(sub.dateReceived || "-")}</td>
        <td><span class="status-pill ${sub.status === "Confirmed" ? "status-ok" : sub.status === "Rejected" ? "status-muted" : "status-warn"}">${escapeHtml(sub.status)}</span></td>
        <td>${escapeHtml(formatDateTime(sub.draftedAt))}<div class="muted-cell">${escapeHtml(sub.draftedByName || "-")}</div></td>
        <td>${escapeHtml(sub.rejectionReason || formatDateTime(sub.confirmedAt) || "-")}<div class="muted-cell">${escapeHtml(sub.confirmedByName || "-")}</div></td>
        <td>${isAdmin && sub.status === "Draft" ? `<button class="button secondary table-action" type="button" data-payment-log-id="${escapeHtml(log.id)}" data-sublog-approve="${escapeHtml(sub.id)}">Approve</button><button class="button secondary table-action" type="button" data-payment-log-id="${escapeHtml(log.id)}" data-sublog-reject="${escapeHtml(sub.id)}">Reject</button>` : "-"}</td>
      </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>Cash record</th><th>Amount</th><th>Date</th><th>Status</th><th>Created by</th><th>Notes</th></tr></thead><tbody>${cashRecords.length === 0
    ? `<tr><td colspan="6">No cash records.</td></tr>`
    : cashRecords.map((record) => `<tr>
        <td>${escapeHtml(record.paymentType || "-")}<div class="muted-cell">${escapeHtml(record.subType || "-")}</div></td>
        <td>${escapeHtml(formatMoney(record.amount))}</td>
        <td>${escapeHtml(formatDateTime(record.paymentDate))}</td>
        <td><span class="status-pill ${paymentHistoryStatusClass(record.status)}">${escapeHtml(record.status || "-")}</span></td>
        <td>${escapeHtml(record.createdByName || "-")}</td>
        <td>${escapeHtml(record.notes || "-")}</td>
      </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>Adjustment</th><th>Amount</th><th>Date</th><th>Status</th><th>Created by</th><th>Notes</th><th>Actions</th></tr></thead><tbody>${adjustments.length === 0
    ? `<tr><td colspan="7">No financial adjustments.</td></tr>`
    : adjustments.map((adjustment) => `<tr>
        <td>${escapeHtml(paymentStageLabel(adjustment.adjustmentType))}</td>
        <td>${escapeHtml(formatMoney(adjustment.amount))}</td>
        <td>${escapeHtml(formatDateTime(adjustment.createdAt))}</td>
        <td><span class="status-pill ${paymentHistoryStatusClass(adjustment.status)}">${escapeHtml(adjustment.status || "-")}</span></td>
        <td>${escapeHtml(adjustment.createdByName || "-")}</td>
        <td>${escapeHtml(adjustment.notes || "-")}</td>
        <td>${isAdmin && adjustment.status === "PendingApproval" ? `<button class="button secondary table-action" type="button" data-adjustment-approve="${escapeHtml(adjustment.id)}">Approve</button><button class="button secondary table-action" type="button" data-adjustment-reject="${escapeHtml(adjustment.id)}">Reject</button>` : "-"}</td>
      </tr>`).join("")}</tbody></table></div>
  </div>`;
}

function paymentStageLabel(stageType) {
  const labels = {
    PaymentLogOpened: "Payment log opened",
    PaymentAssigned: "Assigned to accountant",
    InstallmentDrafted: "Installment drafted",
    InstallmentApproved: "Installment approved",
    InstallmentRejected: "Installment rejected",
    CashReceiptRecorded: "Cash receipt recorded",
    CashReceiptApproved: "Cash receipt approved",
    CashRefundRecorded: "Cash refund recorded",
    MerchantCredit: "Merchant credit",
    BalanceReduction: "Remaining reduction",
    CashRefund: "Financial cash refund"
  };
  return labels[stageType] || stageType || "-";
}

async function draftPaymentSubLog(event) {
  event.preventDefault();
  const id = document.getElementById("payment-log-id").value.trim();
  const amountValue = document.getElementById("payment-amount").value;
  const notes = document.getElementById("payment-notes").value.trim();
  try {
    await request(`/api/v1/payments/${id}/sub-logs`, {
      method: "POST",
      body: JSON.stringify({
        amount: amountValue === "" ? 0 : Number(amountValue),
        paymentMethod: canonicalSelectValue("payment-method"),
        dateReceived: document.getElementById("payment-date").value || null,
        notes: notes || "0"
      })
    });
    notice("Payment sub-log drafted.", "success");
    event.target.reset();
    await Promise.all([loadPayments(), loadPaymentHistory()]);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function approveSubLog(id, paymentLogId = null) {
  try {
    await request(`/api/v1/payments/sub-logs/${id}/approve`, { method: "POST" });
    notice("Payment approved.", "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function approveCashReceipt(id) {
  try {
    await request(`/api/v1/payments/cash-receipts/${encodeURIComponent(id)}/approve`, { method: "POST" });
    notice("Cash receipt approved.", "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function rejectSubLog(id, paymentLogId = null) {
  const reason = await promptDialog({
    title: "Reject Payment Entry",
    label: "Record the reason. Rejected entries remain visible in the log.",
    multiline: true,
    required: true
  });
  if (!reason) {
    return;
  }
  try {
    await request(`/api/v1/payments/sub-logs/${id}/reject`, { method: "POST", body: JSON.stringify({ reason }) });
    notice("Payment rejected.", "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function approveAdjustment(id, paymentLogId = null) {
  try {
    await request(`/api/v1/payments/adjustments/${encodeURIComponent(id)}/approve`, { method: "POST" });
    notice("Financial adjustment approved.", "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function rejectAdjustment(id, paymentLogId = null) {
  const reason = await promptDialog({
    title: "Reject Financial Adjustment",
    label: "Record the reason. Rejected adjustments remain visible in the log.",
    multiline: true,
    required: true
  });
  if (!reason) {
    return;
  }

  try {
    await request(`/api/v1/payments/adjustments/${encodeURIComponent(id)}/reject`, { method: "POST", body: JSON.stringify({ reason }) });
    notice("Financial adjustment rejected.", "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function reopenPaymentDetail(paymentLogId) {
  if (!paymentLogId) {
    return;
  }

  const button = document.querySelector(`[data-payment-detail="${CSS.escape(paymentLogId)}"]`);
  if (button) {
    await togglePaymentDetails(paymentLogId, button);
  }
}

async function assignPaymentLog(id) {
  const accountantId = document.getElementById("payment-accountant")?.value || "";
  if (!accountantId) {
    notice("Select an accountant before assigning the payment log.", "error");
    return;
  }
  try {
    await request(`/api/v1/payments/${id}/assign`, { method: "POST", body: JSON.stringify({ accountantUserId: accountantId }) });
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    notice("Payment log moved to accountant queue.", "success");
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function createFinancialAdjustment(event) {
  event.preventDefault();
  clearFormError("financial-adjustment-error");
  const adjustmentType = canonicalSelectValue("adjustment-type");
  const operationId = document.getElementById("adjustment-operation-id").value.trim();
  const amount = Number(document.getElementById("adjustment-amount").value);
  if (!document.getElementById("adjustment-merchant").value || !Number.isFinite(amount) || amount <= 0) {
    showFormError("financial-adjustment-error", "Merchant and positive amount are required.");
    return;
  }
  if (adjustmentType === "CashRefund" && !operationId) {
    showFormError("financial-adjustment-error", "Cash refund adjustments must reference an operation ID.");
    return;
  }

  try {
    await request("/api/v1/payments/adjustments", {
      method: "POST",
      body: JSON.stringify({
        merchantId: document.getElementById("adjustment-merchant").value,
        operationId: operationId || null,
        adjustmentType,
        amount,
        notes: document.getElementById("adjustment-notes").value || null
      })
    });
    notice("Financial adjustment requested.", "success");
    event.target.reset();
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await loadMerchantBalance();
  } catch (exception) {
    showFormError("financial-adjustment-error", getFriendlyWorkspaceError(exception));
  }
}

async function createCashRecord(event) {
  event.preventDefault();
  try {
    await request("/api/v1/payments/cash-records", {
      method: "POST",
      body: JSON.stringify({
        operationId: document.getElementById("cash-operation-id").value.trim(),
        paymentType: canonicalSelectValue("cash-type"),
        amount: Number(document.getElementById("cash-amount").value),
        notes: document.getElementById("cash-notes").value || null
      })
    });
    notice("Cash record saved.", "success");
    event.target.reset();
    await Promise.all([loadPayments(), loadPaymentHistory()]);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function loadMerchantBalance() {
  const merchantId = document.getElementById("payment-merchant").value;
  const status = document.getElementById("merchant-balance-status");
  const panel = document.getElementById("merchant-balance-panel");
  if (!merchantId) {
    status.textContent = "Select merchant";
    return;
  }
  try {
    const balance = await request(`/api/v1/payments/merchants/${merchantId}/balance`);
    const paymentsReceived = Number(balance.paymentsReceived || 0);
    const cashRefunded = Number(balance.cashRefunded || 0);
    const netCollected = paymentsReceived - cashRefunded;
    const corrections = Number(balance.returnTotal || 0) +
      Number(balance.merchantCredits || 0) +
      Number(balance.balanceReductions || 0) -
      Number(balance.changeNet || 0);
    status.textContent = "Loaded";
    panel.innerHTML = `
      <div><span>Remaining</span><strong>${escapeHtml(formatMoney(balance.balance))}</strong></div>
      <div><span>Sales</span><strong>${escapeHtml(formatMoney(balance.saleTotal))}</strong></div>
      <div><span>Net collected</span><strong>${escapeHtml(formatMoney(netCollected))}</strong></div>
      <div><span>Returns / adjustments</span><strong>${escapeHtml(formatMoney(corrections))}</strong></div>`;
  } catch (exception) {
    status.textContent = getFriendlyWorkspaceError(exception);
  }
}

function shortId(value, prefix = "REF") {
  const raw = String(value || "").trim();
  if (!raw) return "";
  if (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(raw)) {
    return raw;
  }

  const safePrefix = /^[A-Z]{2,4}$/.test(String(prefix || "").toUpperCase()) ? String(prefix).toUpperCase() : "REF";
  const cacheKey = `${safePrefix}:${raw.toLowerCase()}`;
  const cached = displayReferenceCache.get(cacheKey);
  if (cached) return cached;

  const alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
  let remaining = BigInt(`0x${raw.replaceAll("-", "").slice(-20)}`);
  let encoded = "";
  for (let index = 0; index < 16; index += 1) {
    encoded = `${alphabet[Number(remaining & 31n)]}${encoded}`;
    remaining >>= 5n;
  }
  const reference = `${safePrefix}-${encoded.slice(0, 4)}-${encoded.slice(4, 8)}-${encoded.slice(8, 12)}-${encoded.slice(12)}`;
  displayReferenceCache.set(cacheKey, reference);
  return reference;
}

function displaySafeText(value, prefix = "REF") {
  return String(value ?? "").replace(/\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/gi, (id) => shortId(id, prefix));
}

function sanitizeVisibleIdentifiers(root) {
  if (!root || !document.createTreeWalker) return;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);
  nodes.forEach((node) => {
    const safeText = displaySafeText(node.nodeValue);
    if (safeText !== node.nodeValue) node.nodeValue = safeText;
  });
}

function startVisibleIdentifierMasking() {
  if (visibleIdentifierObserver) return;
  const root = document.getElementById("view");
  if (!root || !window.MutationObserver) return;
  visibleIdentifierObserver = new MutationObserver((mutations) => {
    mutations.forEach((mutation) => mutation.addedNodes.forEach((node) => {
      if (node.nodeType === Node.TEXT_NODE) {
        const safeText = displaySafeText(node.nodeValue);
        if (safeText !== node.nodeValue) node.nodeValue = safeText;
      } else if (node.nodeType === Node.ELEMENT_NODE) {
        sanitizeVisibleIdentifiers(node);
      }
    }));
  });
  visibleIdentifierObserver.observe(root, { childList: true, subtree: true });
}

async function renderReports() {
  const role = getAuth()?.user.role;
  const canSeeStock = role !== "Accountant";
  document.getElementById("view").innerHTML = `
    <section class="band">
      <div class="section-head"><div><h2>Reports and exports</h2><p class="muted-text">Download operational, inventory, payment, and statement outputs in CSV and PDF formats.</p></div><button id="reports-refresh" class="button secondary" type="button">Refresh</button></div>
      <div class="report-grid">
        <section class="report-panel">
          <div class="section-head tight-head"><h3>Export language</h3><span class="muted-text">CSV / PDF</span></div>
          <div class="segmented-control" role="radiogroup" aria-label="Export language">
            <label><input type="radio" name="report-export-language" value="ar" ${currentLanguage === "ar" ? "checked" : ""}>Arabic</label>
            <label><input type="radio" name="report-export-language" value="en" ${currentLanguage === "en" ? "checked" : ""}>English</label>
          </div>
        </section>
        ${canSeeStock ? `<section class="report-panel"><div class="section-head tight-head"><h3>Stock</h3><button class="button secondary table-action" type="button" data-download-report="stock.csv">CSV</button></div><div id="report-stock" class="table-wrap compact-table">Loading</div></section>` : ""}
        <section class="report-panel"><div class="section-head tight-head"><h3>Operations</h3><button class="button secondary table-action" type="button" data-download-report="operations.csv">CSV</button></div><div id="report-operations" class="table-wrap compact-table">Loading</div></section>
        <section class="report-panel"><div class="section-head tight-head"><h3>Payments</h3><button class="button secondary table-action" type="button" data-download-report="payments.csv">CSV</button></div><div id="report-payments" class="table-wrap compact-table">Loading</div></section>
        <section class="report-panel"><div class="section-head tight-head"><h3>Supply landed cost</h3><button class="button secondary table-action" type="button" data-download-report="supply.csv">CSV</button></div><div id="report-supply" class="table-wrap compact-table">Loading</div></section>
        <section class="report-panel"><div class="section-head tight-head"><h3>Merchant remaining</h3><button class="button secondary table-action" type="button" data-download-report="merchant-balances.csv">CSV</button></div><div id="report-balances" class="table-wrap compact-table">Loading</div></section>
        <section class="report-panel report-download-panel">
          <div class="section-head tight-head"><h3>Document downloads</h3><span class="muted-text">PDF</span></div>
          <div class="download-grid">
            ${renderReportSearchPicker("operation-bill", "Operation bill", "Search operation code, client, type", "Download bill")}
            ${renderReportSearchPicker("payment-receipt", "Payment receipt", "Search payment code, operation, merchant", "Download receipt")}
            ${renderReportSearchPicker("cash-receipt", "Cash receive receipt", "Search cash payment code, operation", "Download cash receipt")}
            ${renderReportSearchPicker("supply-landed-cost", "Supply landed cost", "Search shipment, supplier, invoice", "Download landed cost")}
            ${renderReportSearchPicker("merchant-statement", "Merchant statement", "Search merchant", "Download statement")}
            ${renderReportSearchPicker("stocktake-summary", "Stocktake summary", "Search stocktake, location, status", "Download summary")}
          </div>
        </section>
        <section class="report-panel"><div class="section-head tight-head"><h3>Export log</h3><span id="report-export-count" class="muted-text">Loading</span></div><div id="report-exports" class="table-wrap compact-table">Loading</div></section>
      </div>
    </section>`;

  document.getElementById("reports-refresh").addEventListener("click", loadReports);
  document.querySelectorAll("[data-download-report]").forEach((button) => button.addEventListener("click", () => downloadReport(button.dataset.downloadReport)));
  document.querySelectorAll("[data-pdf-report]").forEach((button) => button.addEventListener("click", () => downloadReportPdf(button.dataset.pdfReport)));
  await loadReports();
}

async function loadReports() {
  const role = getAuth()?.user.role;
  const canSeeStock = role !== "Accountant";
  await Promise.all([
    canSeeStock ? loadStockReport() : Promise.resolve(),
    loadOperationsReport(),
    loadPaymentsReport(),
    loadSupplyReport(),
    loadMerchantBalancesReport(),
    loadStocktakeReportOptions(),
    loadExportLogs()
  ]);
  renderReportDownloadSelectors();
}

async function loadStockReport() {
  const target = document.getElementById("report-stock");
  if (!target) {
    return;
  }

  try {
    const rows = await request("/api/v1/reports/stock");
    target.innerHTML = `<table><thead><tr><th>Location</th><th>SKU</th><th>Available</th><th>Reserved</th><th>Target</th><th>Updated</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="6">No stock rows.</td></tr>`
      : rows.map((row) => `<tr>
          <td>${escapeHtml(row.locationName)}</td>
          <td><strong>${escapeHtml(row.skuCode || "Unknown SKU")}</strong><span class="muted-cell">${escapeHtml(row.productName || "")}</span></td>
          <td>${escapeHtml(row.availableQty)}</td>
          <td>${escapeHtml(Number(row.reservedInWarehouseQty || 0) + Number(row.reservedWithRepQty || 0))}</td>
          <td>${escapeHtml(row.targetQty ?? "-")}</td>
          <td>${escapeHtml(formatDateTime(row.lastUpdated))}</td>
        </tr>`).join("")}</tbody></table>`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadOperationsReport() {
  const target = document.getElementById("report-operations");
  try {
    const rows = await request("/api/v1/reports/operations");
    reportOperationRows = rows;
    target.innerHTML = `<table><thead><tr><th>Operation</th><th>Type</th><th>Status</th><th>Qty</th><th>Total</th><th>Created</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="6">No operations.</td></tr>`
      : rows.slice(0, 12).map((row) => `<tr><td>${escapeHtml(row.operationNumber)}</td><td>${escapeHtml(row.operationType)}</td><td>${escapeHtml(row.status)}</td><td>${escapeHtml(row.quantity)}</td><td>${escapeHtml(formatMoney(row.total))}</td><td>${escapeHtml(formatDateTime(row.createdAt))}</td></tr>`).join("")}</tbody></table>`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadPaymentsReport() {
  const target = document.getElementById("report-payments");
  try {
    const rows = await request("/api/v1/reports/payments");
    reportPaymentRows = rows;
    target.innerHTML = `<table><thead><tr><th>Payment</th><th>Operation</th><th>Method</th><th>Total</th><th>Paid</th><th>Remaining</th><th>Status</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="7">No payment logs.</td></tr>`
      : rows.slice(0, 12).map((row) => `<tr><td>${escapeHtml(shortId(row.id, "PAY"))}</td><td>${escapeHtml(row.operationNumber || shortId(row.operationId, "OP"))}</td><td>${escapeHtml(row.paymentMethod)}</td><td>${escapeHtml(formatMoney(row.totalAmount))}</td><td>${escapeHtml(formatMoney(row.amountPaid))}</td><td>${escapeHtml(formatMoney(row.remainingAmount))}</td><td>${escapeHtml(row.status)}</td></tr>`).join("")}</tbody></table>`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

function renderWearCycle(cycle, duration) {
  if (!cycle) return `<span class="status-pill status-warn">Needs setup</span>`;
  if (cycle === "NotApplicable") return `<span class="status-pill status-muted">Not applicable</span>`;
  const label = cycle === "Annual" ? "Yearly" : cycle;
  return `<span class="status-pill status-ok">${escapeHtml(label)}</span>${duration ? `<span class="muted-cell"> ${escapeHtml(duration)}</span>` : ""}`;
}

async function loadSupplyReport() {
  const target = document.getElementById("report-supply");
  try {
    const rows = await request("/api/v1/reports/supply");
    reportSupplyRows = rows;
    target.innerHTML = `<table><thead><tr><th>Shipment</th><th>Supplier</th><th>Status</th><th>Qty</th><th>Landed</th><th>Receipt</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="6">No supply shipments.</td></tr>`
      : rows.slice(0, 12).map((row) => `<tr><td>${escapeHtml(row.shipmentNumber)}<span class="muted-cell">${escapeHtml(row.invoiceNumber || "-")}</span></td><td>${escapeHtml(row.supplierName)}</td><td>${escapeHtml(row.status)}</td><td>${escapeHtml(row.quantity)}</td><td>${escapeHtml(formatMoney(row.landedTotal))}</td><td>${escapeHtml(row.inventoryReceiptOperationId ? shortId(row.inventoryReceiptOperationId, "OP") : "-")}</td></tr>`).join("")}</tbody></table>`;
  } catch (exception) {
    reportSupplyRows = [];
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadMerchantBalancesReport() {
  const target = document.getElementById("report-balances");
  try {
    const rows = await request("/api/v1/reports/merchant-balances");
    reportMerchantRows = rows;
    target.innerHTML = `<table><thead><tr><th>Merchant</th><th>Remaining</th><th>Sales</th><th>Net collected</th><th>Returns / adjustments</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="5">No merchant remaining.</td></tr>`
      : rows.slice(0, 12).map((row) => {
        const paymentsReceived = Number(row.paymentsReceived || 0);
        const cashRefunded = Number(row.cashRefunded || 0);
        const corrections = Number(row.returnTotal || 0) +
          Number(row.merchantCredits || 0) +
          Number(row.balanceReductions || 0) -
          Number(row.changeNet || 0);
        return `<tr><td>${escapeHtml(row.businessName)}</td><td>${escapeHtml(formatMoney(row.balance))}</td><td>${escapeHtml(formatMoney(row.saleTotal))}</td><td>${escapeHtml(formatMoney(paymentsReceived - cashRefunded))}</td><td>${escapeHtml(formatMoney(corrections))}</td></tr>`;
      }).join("")}</tbody></table>`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadStocktakeReportOptions() {
  try {
    const result = await request("/api/v1/stocktakes?pageSize=100");
    reportStocktakeRows = result.items || [];
  } catch {
    reportStocktakeRows = [];
  }
}

function renderReportDownloadSelectors() {
  setupReportSearchPicker("operation-bill", reportOperationRows, (row) => row.id, (row) => `${row.operationNumber} / ${row.operationType} / ${row.status} / ${row.clientName || "-"}`);
  setupReportSearchPicker("payment-receipt", reportPaymentRows, (row) => row.id, (row) => `${shortId(row.id, "PAY")} / ${row.operationNumber || shortId(row.operationId, "OP")} / ${row.paymentMethod} / ${formatMoney(row.remainingAmount)} remaining`);
  setupReportSearchPicker("cash-receipt", reportPaymentRows.filter((row) => row.paymentMethod === "CashHandToHand"), (row) => row.id, (row) => `${shortId(row.id, "PAY")} / ${row.operationNumber || shortId(row.operationId, "OP")} / ${row.status} / ${formatMoney(row.totalAmount)}`);
  setupReportSearchPicker("supply-landed-cost", reportSupplyRows, (row) => row.id, (row) => `${row.shipmentNumber} / ${row.supplierName} / ${row.invoiceNumber || "-"} / ${formatMoney(row.landedTotal)}`);
  setupReportSearchPicker("merchant-statement", reportMerchantRows, (row) => row.merchantId, (row) => `${row.businessName} / ${formatMoney(row.balance)}`);
  setupReportSearchPicker("stocktake-summary", reportStocktakeRows, (row) => row.id, (row) => `${shortId(row.id, "STK")} / ${row.status} / ${formatDateTime(row.createdAt)}`);
}

function setReportSelect(id, rows, valueSelector, labelSelector) {
  const select = document.getElementById(id);
  if (!select) {
    return;
  }
  select.innerHTML = rows.length === 0
    ? `<option value="">No rows available</option>`
    : `<option value="">Select...</option>${rows.map((row) => `<option value="${escapeHtml(valueSelector(row))}">${escapeHtml(labelSelector(row))}</option>`).join("")}`;
}

function renderReportSearchPicker(reportType, label, placeholder, buttonLabel) {
  const id = `report-picker-${reportType}`;
  return `<div class="field report-search-field">
    <label for="${id}-search">${escapeHtml(label)}</label>
    <input id="${id}-value" type="hidden">
    <input id="${id}-search" class="input report-picker-search" data-report-picker="${escapeHtml(reportType)}" type="search" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="${id}-results" placeholder="${escapeHtml(placeholder)}">
    <div id="${id}-results" class="op-line-search-results report-picker-results" role="listbox" hidden></div>
    <button class="button secondary" type="button" data-pdf-report="${escapeHtml(reportType)}">${escapeHtml(buttonLabel)}</button>
  </div>`;
}

function setupReportSearchPicker(reportType, rows, valueSelector, labelSelector) {
  const search = document.getElementById(`report-picker-${reportType}-search`);
  const value = document.getElementById(`report-picker-${reportType}-value`);
  const results = document.getElementById(`report-picker-${reportType}-results`);
  if (!search || !value || !results) {
    return;
  }

  setupAdaptiveSearchResultDismissal();

  const render = () => {
    const term = search.value.trim().toLowerCase();
    if (term.length < 2) {
      hideAdaptiveSearchResults(results, search);
      return;
    }

    const matches = rows
      .map((row) => ({ row, value: String(valueSelector(row) || ""), label: String(labelSelector(row) || "") }))
      .filter((item) => !term || item.label.toLowerCase().includes(term) || item.value.toLowerCase().includes(term))
      .slice(0, 12);

    collapseAdaptiveSearchResults(results);
    results.hidden = false;
    search.setAttribute("aria-expanded", "true");
    results.innerHTML = matches.length === 0
      ? `<button class="op-line-search-result" type="button" disabled>No matching records.</button>`
      : matches.map((item) => `<button class="op-line-search-result" type="button" role="option" data-report-picker-value="${escapeHtml(item.value)}" data-report-picker-label="${escapeHtml(item.label)}"><strong>${escapeHtml(item.label)}</strong><span class="muted-cell">${escapeHtml(item.value)}</span></button>`).join("");
    results.querySelectorAll("[data-report-picker-value]").forEach((button) => button.addEventListener("click", () => {
      value.value = button.dataset.reportPickerValue || "";
      search.value = button.dataset.reportPickerLabel || "";
      hideAdaptiveSearchResults(results, search);
    }));
  };

  search.addEventListener("input", () => {
    value.value = "";
    render();
  });
  search.addEventListener("focus", () => {
    collapseAdaptiveSearchResults(results);
    if (search.value.trim().length >= 2) render();
  });
  search.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      hideAdaptiveSearchResults(results, search);
      search.blur();
    }
  });
  search.addEventListener("blur", () => window.setTimeout(() => {
    hideAdaptiveSearchResults(results, search);
  }, 150));
}

let adaptiveSearchResultDismissalReady = false;

function setupAdaptiveSearchResultDismissal() {
  if (adaptiveSearchResultDismissalReady) return;
  adaptiveSearchResultDismissalReady = true;

  document.addEventListener("focusin", (event) => {
    const owner = event.target.closest?.(".op-line-finder, .inventory-sku-picker, .report-search-field");
    collapseAdaptiveSearchResults(owner?.querySelector(".op-line-search-results") || null);
  });
  document.addEventListener("pointerdown", (event) => {
    if (!event.target.closest?.(".op-line-finder, .inventory-sku-picker, .report-search-field")) {
      collapseAdaptiveSearchResults();
    }
  });
}

function hideAdaptiveSearchResults(results, control = null) {
  if (!results) return;
  results.hidden = true;
  results.innerHTML = "";
  control?.setAttribute("aria-expanded", "false");
}

function collapseAdaptiveSearchResults(exceptResults = null) {
  document.querySelectorAll(".op-line-search-results").forEach((results) => {
    if (results !== exceptResults) {
      hideAdaptiveSearchResults(results, results.parentElement?.querySelector('[role="combobox"]'));
    }
  });
}

async function loadExportLogs() {
  const target = document.getElementById("report-exports");
  const count = document.getElementById("report-export-count");
  try {
    const result = await request("/api/v1/reports/exports?pageSize=20");
    count.textContent = `${result.totalCount} logged`;
    target.innerHTML = `<table><thead><tr><th>Report</th><th>Requested by</th><th>Created</th></tr></thead><tbody>${result.items.length === 0
      ? `<tr><td colspan="3">No export logs yet.</td></tr>`
      : result.items.map((row) => `<tr><td>${escapeHtml(row.reportType)}</td><td>${escapeHtml(row.requestedByRole ? roleLabel(row.requestedByRole) : "System")}</td><td>${escapeHtml(formatDateTime(row.createdAt))}</td></tr>`).join("")}</tbody></table>`;
  } catch (exception) {
    count.textContent = "Failed";
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function logReportExport(reportType) {
  try {
    await request("/api/v1/reports/exports", { method: "POST", body: JSON.stringify({ reportType }) });
    notice("Export intent logged.", "success");
    await loadExportLogs();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function downloadReport(reportName) {
  try {
    const language = getReportExportLanguage();
    await downloadFile(`/api/v1/reports/${reportName}?language=${encodeURIComponent(language)}`, `lensee-${language}-${reportName}`);
    notice("Report downloaded.", "success");
    await loadExportLogs();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function downloadReportPdf(reportType) {
  const selectors = {
    "operation-bill": "report-picker-operation-bill-value",
    "payment-receipt": "report-picker-payment-receipt-value",
    "cash-receipt": "report-picker-cash-receipt-value",
    "supply-landed-cost": "report-picker-supply-landed-cost-value",
    "merchant-statement": "report-picker-merchant-statement-value",
    "stocktake-summary": "report-picker-stocktake-summary-value"
  };
  const id = document.getElementById(selectors[reportType])?.value || "";
  if (!id) {
    notice("Select a document row before downloading.", "error");
    return;
  }

  const paths = {
    "operation-bill": `/api/v1/reports/operations/${encodeURIComponent(id.trim())}/bill.pdf`,
    "payment-receipt": `/api/v1/reports/payments/${encodeURIComponent(id.trim())}/receipt.pdf`,
    "cash-receipt": `/api/v1/reports/payments/${encodeURIComponent(id.trim())}/cash-receipt.pdf`,
    "supply-landed-cost": `/api/v1/reports/supply/${encodeURIComponent(id.trim())}/landed-cost.pdf`,
    "merchant-statement": `/api/v1/reports/merchants/${encodeURIComponent(id.trim())}/statement.pdf`,
    "stocktake-summary": `/api/v1/reports/stocktakes/${encodeURIComponent(id.trim())}/summary.pdf`
  };

  try {
    await printReportPdf(reportType, id.trim());
    notice("PDF downloaded.", "success");
    await loadExportLogs();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function getReportExportLanguage() {
  return document.querySelector('input[name="report-export-language"]:checked')?.value === "en" ? "en" : "ar";
}

async function printReportPdf(reportType, id, codeOverride = null) {
  const paths = {
    "operation-bill": `/api/v1/reports/operations/${encodeURIComponent(id.trim())}/bill.pdf`,
    "payment-receipt": `/api/v1/reports/payments/${encodeURIComponent(id.trim())}/receipt.pdf`,
    "cash-receipt": `/api/v1/reports/payments/${encodeURIComponent(id.trim())}/cash-receipt.pdf`,
    "supply-landed-cost": `/api/v1/reports/supply/${encodeURIComponent(id.trim())}/landed-cost.pdf`,
    "merchant-statement": `/api/v1/reports/merchants/${encodeURIComponent(id.trim())}/statement.pdf`,
    "stocktake-summary": `/api/v1/reports/stocktakes/${encodeURIComponent(id.trim())}/summary.pdf`
  };
  const language = getReportExportLanguage();
  const code = sanitizeFileCode(codeOverride || getReportFileCode(reportType, id));
  await downloadFile(`${paths[reportType]}?language=${encodeURIComponent(language)}`, `lensee-${language}-${reportType}-${code}.pdf`);
}

function getReportFileCode(reportType, id) {
  const cleanId = String(id || "").trim();
  if (reportType === "operation-bill") {
    return sanitizeFileCode(reportOperationRows.find((row) => row.id === cleanId)?.operationNumber || cleanId);
  }
  if (reportType === "payment-receipt" || reportType === "cash-receipt") {
    return sanitizeFileCode(shortId(reportPaymentRows.find((row) => row.id === cleanId)?.id || cleanId, "PAY"));
  }
  if (reportType === "merchant-statement") {
    return sanitizeFileCode(reportMerchantRows.find((row) => row.merchantId === cleanId)?.businessName || cleanId);
  }
  if (reportType === "supply-landed-cost") {
    return sanitizeFileCode(reportSupplyRows.find((row) => row.id === cleanId)?.shipmentNumber || cleanId);
  }
  if (reportType === "stocktake-summary") {
    return sanitizeFileCode(shortId(cleanId, "STK"));
  }
  return sanitizeFileCode(cleanId);
}

function sanitizeFileCode(value) {
  return String(value || "report").trim().replace(/[^a-z0-9._-]+/gi, "-").replace(/^-+|-+$/g, "") || "report";
}

function bindPrintReportButtons(root = document) {
  root.querySelectorAll("[data-print-report]").forEach((button) => button.addEventListener("click", async () => {
    try {
      await printReportPdf(button.dataset.printReport, button.dataset.printId, button.dataset.printCode);
      notice("PDF downloaded.", "success");
      if (document.getElementById("report-exports")) {
        await loadExportLogs();
      }
    } catch (exception) {
      notice(getFriendlyWorkspaceError(exception), "error");
    }
  }));
}

function supplyStatusLabel(status) {
  const labels = {
    Draft: supplyText("Draft", "مسودة"),
    Received: supplyText("Received", "تم الاستلام"),
    Cancelled: supplyText("Cancelled", "ملغاة")
  };
  return labels[status] || status || "-";
}

function supplyCostTypeLabel(type) {
  const labels = {
    Customs: supplyText("Customs", "جمارك"),
    Freight: supplyText("Freight", "شحن"),
    Clearance: supplyText("Clearance", "تخليص"),
    Handling: supplyText("Handling", "مناولة"),
    Insurance: supplyText("Insurance", "تأمين"),
    Other: supplyText("Other", "أخرى")
  };
  return labels[type] || type || "-";
}

function supplyText(english, arabic) {
  return currentLanguage === "ar" ? arabic : english;
}

async function hydrateSupplySkus() {
  if (operationSkuOptions.length > 0) {
    buildSupplySkuSearchIndex();
    return;
  }
  if (!supplySkuLoadPromise) {
    supplySkuLoadPromise = hydrateOperationSkus()
      .then(buildSupplySkuSearchIndex)
      .finally(() => {
        supplySkuLoadPromise = null;
      });
  }
  await supplySkuLoadPromise;
}

function buildSupplySkuSearchIndex() {
  supplySkuSearchIndex = operationSkuOptions.map((sku) => ({
    sku,
    searchText: `${sku.productName} ${sku.brandName} ${sku.categoryName} ${sku.skuCode} ${formatOperationPowerKey(operationPowerKey(sku))} ${sku.colorName || ""} ${sku.size || ""}`.toLowerCase()
  }));
}

async function renderSupply() {
  const auth = getAuth();
  const canWrite = auth?.user.role === "Admin";
  selectedSupplyShipmentId = null;
  supplyCurrentDetail = null;
  await Promise.all([
    loadSupplyLocations(),
    hydrateSupplySkus()
  ]);

  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: supplyText("Stock", "المخزون"),
      title: supplyText("Supply", "التوريد"),
      body: supplyText("Register imported shipments, allocate customs and import costs, then post controlled inventory receipts.", "سجل الشحنات المستوردة، وزع الجمارك ومصاريف الاستيراد، ثم أنشئ إيصالات مخزون مضبوطة."),
      metrics: `
        ${scenarioCard(supplyText("Shipments", "الشحنات"), supplyText("Loading", "جار التحميل"), "status-muted", "supply-count")}
        ${scenarioCard(supplyText("Draft value", "قيمة المسودات"), supplyText("Loading", "جار التحميل"), "status-muted", "supply-draft-total")}
        ${scenarioCard(supplyText("Ready to confirm", "جاهزة للتأكيد"), supplyText("Loading", "جار التحميل"), "status-muted", "supply-ready-count")}
        ${scenarioCard(supplyText("Access", "الصلاحية"), canWrite ? supplyText("Admin", "مدير") : supplyText("Read only", "قراءة فقط"), canWrite ? "status-ok" : "status-muted")}
      `
    })}
    <section class="supply-workspace">
      <main class="supply-main-pane">
        ${canWrite ? renderSupplyForm() : ""}
        <section class="supply-list-pane">
        <section class="band compact-band">
          <div class="section-head tight-head">
            <div><h2>${supplyText("Shipments", "الشحنات")}</h2><p class="muted-text">${supplyText("Search by shipment, supplier, or invoice.", "ابحث برقم الشحنة أو المورد أو الفاتورة.")}</p></div>
            <button id="supply-refresh" class="button secondary" type="button">${supplyText("Refresh", "تحديث")}</button>
          </div>
          <div class="toolbar">
            <input id="supply-search" class="input" autocomplete="off" placeholder="${supplyText("Search shipments", "بحث في الشحنات")}">
            <select id="supply-status" class="select compact-select">
              <option value="">${supplyText("All statuses", "كل الحالات")}</option>
              <option value="Draft">${supplyText("Draft", "مسودة")}</option>
              <option value="Received">${supplyText("Received", "تم الاستلام")}</option>
              <option value="Cancelled">${supplyText("Cancelled", "ملغاة")}</option>
            </select>
          </div>
          <div class="table-wrap compact-table">
            <table><thead><tr><th>${supplyText("Shipment", "الشحنة")}</th><th>${supplyText("Supplier", "المورد")}</th><th>${supplyText("Status", "الحالة")}</th><th>${supplyText("Total", "الإجمالي")}</th><th></th></tr></thead><tbody id="supply-rows"></tbody></table>
          </div>
        </section>
        </section>
        <section class="band supply-detail-pane" id="supply-detail">
          <h2>${supplyText("Shipment detail", "تفاصيل الشحنة")}</h2>
          <p class="muted-text">${supplyText("Select a shipment to review lines, cost allocation, receipt operation, and history.", "اختر شحنة لمراجعة البنود، توزيع التكلفة، عملية إيصال المخزون، وسجل الحركة.")}</p>
        </section>
      </main>
    </section>`;

  document.getElementById("supply-refresh").addEventListener("click", loadSupplyShipments);
  document.getElementById("supply-search").addEventListener("input", debounce(loadSupplyShipments, 250));
  document.getElementById("supply-status").addEventListener("change", loadSupplyShipments);
  if (canWrite) {
    wireSupplyForm();
  }
  await loadSupplyShipments();
}

function renderSupplyForm() {
  const mainWarehouse = inventoryLocations.find((location) => location.locationType === "MainWarehouse") || inventoryLocations[0];
  return `
    <section class="supply-form-card" id="supply-form-panel">
      <div class="supply-form-heading">
        <div>
          <span class="supply-form-badge">${supplyText("Supply receipt", "إيصال توريد")}</span>
          <h2>${supplyText("Register incoming shipment", "تسجيل شحنة واردة")}</h2>
          <p class="muted-text">${supplyText("Enter supplier, SKU lines, and costs in one document, then save the draft before confirming receipt.", "أدخل بيانات المورد والبنود والتكاليف في نموذج واحد، ثم احفظ المسودة قبل تأكيد الاستلام.")}</p>
        </div>
        <button id="supply-reset" class="button secondary" type="button">${supplyText("New shipment", "شحنة جديدة")}</button>
      </div>
      <form id="supply-form" class="form wide-form compact-form supply-receipt-form">
        <div class="form-error" id="supply-form-error" hidden></div>
        <div class="supply-validation-list full-span" id="supply-validation-list" hidden></div>
        <input id="supply-id" type="hidden">
        <section class="supply-document-block supply-header-block full-span">
          <div class="supply-block-title"><span>${supplyText("Shipment data", "بيانات الشحنة")}</span><strong>${supplyText("Receipt draft", "مسودة استلام")}</strong></div>
          <div class="supply-header-grid">
            <div class="field"><label for="supply-supplier">${supplyText("Supplier", "المورد")}</label><input id="supply-supplier" class="input" maxlength="255" required></div>
            <div class="field"><label for="supply-invoice">${supplyText("Invoice number", "رقم الفاتورة")}</label><input id="supply-invoice" class="input" maxlength="100"></div>
            <div class="field"><label for="supply-date">${supplyText("Shipment date", "تاريخ الشحنة")}</label><input id="supply-date" class="input" type="datetime-local"></div>
            <div class="field"><label for="supply-location">${supplyText("Destination warehouse", "مخزن الوصول")}</label><select id="supply-location" class="select">${inventoryLocations.filter((location) => location.isActive).map((location) => `<option value="${escapeHtml(location.id)}" ${location.id === mainWarehouse?.id ? "selected" : ""}>${escapeHtml(location.name)}</option>`).join("")}</select></div>
            <div class="field full-span"><label for="supply-notes">${supplyText("Notes", "ملاحظات")}</label><textarea id="supply-notes" class="input" rows="2" maxlength="4000"></textarea></div>
          </div>
        </section>
        <section class="operation-line-panel supply-document-block full-span">
          <div class="section-head tight-head"><div><h2>${supplyText("SKU lines", "بنود SKU")}</h2><p class="muted-text">${supplyText("Prices can stay blank while drafting and must be completed before confirmation.", "يمكن ترك السعر فارغا في المسودة، ويجب إكماله قبل التأكيد.")}</p></div><button id="supply-add-line" class="button secondary" type="button">${supplyText("Add line", "إضافة بند")}</button></div>
          <div id="supply-lines" class="line-editor"></div>
        </section>
        <section class="operation-line-panel supply-document-block full-span">
          <div class="section-head tight-head"><div><h2>${supplyText("Import cost breakdown", "تفصيل تكاليف الاستيراد")}</h2></div><button id="supply-add-cost" class="button secondary" type="button">${supplyText("Add cost", "إضافة تكلفة")}</button></div>
          <div id="supply-costs" class="line-editor"></div>
        </section>
        <section class="supply-summary-panel full-span" id="supply-summary-panel">
          <div><span>${supplyText("Product subtotal", "إجمالي المنتجات")}</span><strong id="supply-form-product-total">0.00</strong></div>
          <div><span>${supplyText("Import costs", "تكاليف الاستيراد")}</span><strong id="supply-form-cost-total">0.00</strong></div>
          <div><span>${supplyText("Landed total", "الإجمالي بعد التكلفة")}</span><strong id="supply-form-landed-total">0.00</strong></div>
          <div><span>${supplyText("Confirmation readiness", "جاهزية التأكيد")}</span><strong id="supply-form-readiness" class="status-warn">${supplyText("Incomplete prices", "أسعار ناقصة")}</strong></div>
        </section>
        <div class="form-actions full-span">
          <button class="button primary" type="submit">${supplyText("Save draft", "حفظ المسودة")}</button>
        </div>
      </form>
    </section>`;
}

function wireSupplyForm() {
  document.getElementById("supply-form").addEventListener("submit", saveSupplyShipment);
  document.getElementById("supply-reset").addEventListener("click", resetSupplyForm);
  document.getElementById("supply-add-line").addEventListener("click", () => addSupplyLine());
  document.getElementById("supply-add-cost").addEventListener("click", () => addSupplyCost());
  document.getElementById("supply-form").addEventListener("input", updateSupplyFormSummary);
  document.getElementById("supply-form").addEventListener("change", updateSupplyFormSummary);
  resetSupplyForm();
}

function resetSupplyForm() {
  const form = document.getElementById("supply-form");
  if (!form) {
    return;
  }
  form.reset();
  document.getElementById("supply-id").value = "";
  document.getElementById("supply-lines").innerHTML = "";
  document.getElementById("supply-costs").innerHTML = "";
  document.getElementById("supply-validation-list").hidden = true;
  document.getElementById("supply-form-error").hidden = true;
  addSupplyLine();
  addSupplyCost({ costType: "Customs" });
  updateSupplyFormSummary();
}

function addSupplyLine(line = {}) {
  const container = document.getElementById("supply-lines");
  if (!container) {
    return;
  }
  const unitPriceValue = line.unitPrice ?? "";
  const row = document.createElement("div");
  row.className = "line-editor-row supply-line-row";
  row.innerHTML = `
    <input class="supply-line-sku" type="hidden" value="${escapeHtml(line.skuId || "")}">
    <div class="field op-line-finder"><label>${supplyText("Find SKU", "بحث SKU")}</label><input class="input supply-line-search" autocomplete="off" placeholder="${supplyText("Product, color, power, SKU code", "المنتج، اللون، القوة، كود SKU")}"><div class="op-line-search-results" hidden></div></div>
    <div class="op-line-resolved full-span"><span class="muted-text">${supplyText("Search and select a SKU.", "ابحث واختر SKU.")}</span></div>
    <div class="field"><label>${supplyText("Quantity", "الكمية")}</label><input class="input supply-line-qty" type="number" min="1" step="1" value="${escapeHtml(line.quantity || 1)}" required></div>
    <div class="field"><label>${supplyText("Unit price", "سعر الوحدة")}</label><input class="input supply-line-price" type="number" min="0.01" step="0.01" value="${escapeHtml(unitPriceValue)}" placeholder="${supplyText("Draft blank", "فارغ في المسودة")}"><span class="field-hint supply-price-hint" hidden>${supplyText("Required before confirmation.", "مطلوب قبل التأكيد.")}</span></div>
    <div class="field"><label>${supplyText("Lot", "التشغيلة")}</label><input class="input supply-line-lot" maxlength="100" value="${escapeHtml(line.lotNumber || "")}"></div>
    <div class="field"><label>${supplyText("Expiry", "الصلاحية")}</label><input class="input supply-line-expiry" type="date" value="${escapeHtml(line.expiryDate || "")}"></div>
    <div class="field full-span"><label>${supplyText("Line notes", "ملاحظات البند")}</label><input class="input supply-line-notes" maxlength="1000" value="${escapeHtml(line.notes || "")}"></div>
    <button class="icon-button supply-remove-line" type="button" title="${supplyText("Remove line", "حذف البند")}">x</button>`;
  row.querySelector(".supply-line-search").addEventListener("input", () => renderSupplySkuSearchResults(row));
  row.querySelector(".supply-line-price").addEventListener("input", () => updateSupplyLinePriceState(row));
  row.querySelector(".supply-remove-line").addEventListener("click", () => {
    if (container.querySelectorAll(".supply-line-row").length > 1) {
      row.remove();
      updateSupplyFormSummary();
    }
  });
  container.appendChild(row);
  if (line.skuId) {
    seedSupplyLineSkuSelection(row, line.skuId);
  }
  updateSupplyLinePriceState(row);
  updateSupplyFormSummary();
}

function updateSupplyLinePriceState(row) {
  const priceInput = row.querySelector(".supply-line-price");
  const hint = row.querySelector(".supply-price-hint");
  const isBlank = priceInput.value.trim() === "";
  const value = Number(priceInput.value);
  const isInvalid = !isBlank && (!Number.isFinite(value) || value <= 0);
  row.classList.toggle("supply-line-incomplete", isBlank);
  row.classList.toggle("supply-line-invalid", isInvalid);
  if (hint) {
    hint.hidden = !isBlank && !isInvalid;
    hint.textContent = isInvalid ? supplyText("Price must be greater than zero.", "السعر يجب أن يكون أكبر من صفر.") : supplyText("Required before confirmation.", "مطلوب قبل التأكيد.");
  }
  updateSupplyFormSummary();
}

function renderSupplySkuSearchResults(row) {
  const input = row.querySelector(".supply-line-search");
  const results = row.querySelector(".op-line-search-results");
  const terms = input.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) {
    results.hidden = true;
    results.innerHTML = "";
    return;
  }

  if (supplySkuSearchIndex.length !== operationSkuOptions.length) {
    buildSupplySkuSearchIndex();
  }
  const matches = supplySkuSearchIndex
    .filter((entry) => terms.every((term) => entry.searchText.includes(term)))
    .map((entry) => entry.sku)
    .slice(0, 8);

  setupAdaptiveSearchResultDismissal();
  collapseAdaptiveSearchResults(results);
  results.hidden = false;
  results.innerHTML = matches.length === 0
    ? `<button type="button" class="op-line-search-result" disabled>${supplyText("No results", "لا توجد نتائج")}</button>`
    : matches.map((sku) => `
        <button type="button" class="op-line-search-result" data-supply-sku-id="${escapeHtml(sku.id)}">
          <strong>${escapeHtml(sku.productName)}</strong>
          <span>${escapeHtml(formatOperationPowerKey(operationPowerKey(sku)))} / ${escapeHtml(sku.colorName || "-")} / ${escapeHtml(sku.size || "-")}</span>
          <small>${escapeHtml(sku.skuCode)}</small>
        </button>`).join("");
  results.querySelectorAll("[data-supply-sku-id]").forEach((button) => {
    button.addEventListener("click", () => {
      seedSupplyLineSkuSelection(row, button.dataset.supplySkuId);
      input.value = "";
      results.hidden = true;
      results.innerHTML = "";
      updateSupplyFormSummary();
    });
  });
}

function seedSupplyLineSkuSelection(row, skuId) {
  const sku = operationSkuOptions.find((value) => value.id === skuId);
  row.querySelector(".supply-line-sku").value = skuId || "";
  row.querySelector(".op-line-resolved").innerHTML = sku
    ? `<span class="status-pill status-ok">${supplyText("Selected SKU", "SKU محدد")}</span><strong>${escapeHtml(sku.skuCode)}</strong><span class="muted-cell">${escapeHtml(sku.productName)}</span>`
    : `<span class="status-pill status-warn">${supplyText("Unknown SKU", "SKU غير معروف")}</span><span class="muted-cell">${escapeHtml(shortId(skuId, "SKU"))}</span>`;
}

function addSupplyCost(cost = {}) {
  const container = document.getElementById("supply-costs");
  if (!container) {
    return;
  }
  const row = document.createElement("div");
  row.className = "line-editor-row supply-cost-row";
  row.innerHTML = `
    <div class="field"><label>${supplyText("Cost type", "نوع التكلفة")}</label><select class="select supply-cost-type">
      ${["Customs", "Freight", "Clearance", "Handling", "Insurance", "Other"].map((item) => `<option value="${escapeHtml(item)}">${escapeHtml(supplyCostTypeLabel(item))}</option>`).join("")}
    </select></div>
    <div class="field"><label>${supplyText("Description", "الوصف")}</label><input class="input supply-cost-description" maxlength="255" value="${escapeHtml(cost.description || "")}"></div>
    <div class="field"><label>${supplyText("Amount", "المبلغ")}</label><input class="input supply-cost-amount" type="number" min="0" step="0.01" value="${escapeHtml(cost.amount || 0)}"></div>
    <button class="icon-button supply-remove-cost" type="button" title="${supplyText("Remove cost", "حذف التكلفة")}">x</button>`;
  row.querySelector(".supply-cost-type").value = cost.costType || "Other";
  row.querySelector(".supply-remove-cost").addEventListener("click", () => {
    row.remove();
    updateSupplyFormSummary();
  });
  container.appendChild(row);
  updateSupplyFormSummary();
}

async function loadSupplyShipments() {
  const tbody = document.getElementById("supply-rows");
  const count = document.getElementById("supply-count");
  if (!tbody) {
    return;
  }
  tbody.innerHTML = `<tr><td colspan="5">${supplyText("Loading shipments...", "جار تحميل الشحنات...")}</td></tr>`;
  const params = new URLSearchParams();
  const search = document.getElementById("supply-search")?.value.trim();
  const status = document.getElementById("supply-status")?.value;
  if (search) params.set("search", search);
  if (status) params.set("status", status);
  try {
    const rows = await request(`/api/v1/supply/shipments${params.toString() ? `?${params}` : ""}`);
    supplyShipments = rows;
    updateSupplyPageMetrics(rows);
    if (count) {
      count.textContent = currentLanguage === "ar" ? `${rows.length} شحنة` : `${rows.length} shipment${rows.length === 1 ? "" : "s"}`;
    }
    tbody.innerHTML = rows.length === 0 ? `<tr><td colspan="5">${supplyText("No supply shipments match the current filters.", "لا توجد شحنات مطابقة للفلاتر الحالية.")}</td></tr>` : rows.map((row) => `
      <tr class="click-row ${row.id === selectedSupplyShipmentId ? "selected-row" : ""}" data-supply-id="${escapeHtml(row.id)}">
        <td><strong>${escapeHtml(row.shipmentNumber)}</strong><span class="muted-cell">${escapeHtml(row.invoiceNumber || supplyText("No invoice", "بدون فاتورة"))}</span></td>
        <td>${escapeHtml(row.supplierName)}<span class="muted-cell">${escapeHtml(row.destinationLocationName || "-")} / ${escapeHtml(row.quantity || 0)} ${supplyText("packs", "عبوة")}</span></td>
        <td><span class="status-pill ${row.status === "Received" ? "status-ok" : row.status === "Cancelled" ? "status-muted" : "status-warn"}">${escapeHtml(supplyStatusLabel(row.status))}</span></td>
        <td><strong>${escapeHtml(formatMoney(row.landedTotal))}</strong><span class="muted-cell">${escapeHtml(formatMoney(row.costSubtotal))} ${supplyText("costs", "تكاليف")}</span></td>
        <td><button class="button secondary table-action" type="button" data-supply-detail="${escapeHtml(row.id)}">${supplyText("Details", "التفاصيل")}</button></td>
      </tr>`).join("");
    tbody.querySelectorAll("[data-supply-detail], [data-supply-id]").forEach((element) => {
      element.addEventListener("click", () => showSupplyDetail(element.dataset.supplyDetail || element.dataset.supplyId));
    });
  } catch (exception) {
    if (count) count.textContent = supplyText("Failed", "فشل التحميل");
    updateSupplyPageMetrics([]);
    tbody.innerHTML = `<tr><td colspan="5">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function showSupplyDetail(id) {
  selectedSupplyShipmentId = id;
  const target = document.getElementById("supply-detail");
  const canWrite = getAuth()?.user.role === "Admin";
  target.innerHTML = `<h2>${supplyText("Shipment detail", "تفاصيل الشحنة")}</h2><p>${supplyText("Loading shipment...", "جار تحميل الشحنة...")}</p>`;
  try {
    const shipment = await request(`/api/v1/supply/shipments/${encodeURIComponent(id)}`);
    supplyCurrentDetail = shipment;
    const readiness = getSupplyShipmentReadiness(shipment);
    target.innerHTML = `
      <div class="section-head">
        <div><h2>${escapeHtml(shipment.shipmentNumber)}</h2><p class="muted-text">${escapeHtml(shipment.supplierName)} / ${escapeHtml(shipment.invoiceNumber || "-")}</p></div>
        <div class="inline-actions">
          <span class="status-pill ${shipment.status === "Received" ? "status-ok" : shipment.status === "Cancelled" ? "status-muted" : "status-warn"}">${escapeHtml(supplyStatusLabel(shipment.status))}</span>
          ${canWrite && shipment.status === "Draft" ? `<button class="button secondary" type="button" id="supply-edit">${supplyText("Edit", "تعديل")}</button><button class="button primary" type="button" id="supply-confirm" ${readiness.canConfirm ? "" : `disabled title="${escapeHtml(readiness.message)}"`}>${supplyText("Confirm receipt", "تأكيد الاستلام")}</button><button class="button secondary" type="button" id="supply-cancel">${supplyText("Cancel", "إلغاء")}</button>` : ""}
          ${shipment.inventoryReceiptOperationId ? `<button class="button secondary" type="button" data-print-report="operation-bill" data-print-id="${escapeHtml(shipment.inventoryReceiptOperationId)}" data-print-code="${escapeHtml(shipment.shipmentNumber)}">${supplyText("Print receipt", "طباعة الإيصال")}</button>` : ""}
        </div>
      </div>
      ${shipment.status === "Draft" && !readiness.canConfirm ? `<p class="form-error inline-warning">${escapeHtml(readiness.message)}</p>` : ""}
      <div class="detail-grid supply-readiness-grid">
        <div><span>${supplyText("Destination warehouse", "مخزن الوصول")}</span><strong>${escapeHtml(shipment.destinationLocationName || shortId(shipment.destinationLocationId, "LOC"))}</strong></div>
        <div><span>${supplyText("Shipment date", "تاريخ الشحنة")}</span><strong>${escapeHtml(formatDateTime(shipment.shipmentDate))}</strong></div>
        <div><span>${supplyText("Products", "المنتجات")}</span><strong>${escapeHtml(formatMoney(shipment.productSubtotal))}</strong></div>
        <div><span>${supplyText("Import costs", "تكاليف الاستيراد")}</span><strong>${escapeHtml(formatMoney(shipment.costSubtotal))}</strong></div>
        <div><span>${supplyText("Landed total", "الإجمالي بعد التكلفة")}</span><strong>${escapeHtml(formatMoney(shipment.landedTotal))}</strong></div>
        <div><span>${supplyText("Readiness", "جاهزية التأكيد")}</span><strong class="${readiness.canConfirm ? "status-ok" : "status-warn"}">${escapeHtml(readiness.label)}</strong></div>
      </div>
      ${shipment.inventoryReceiptOperationId ? `<p class="muted-text">${supplyText("Inventory receipt operation", "عملية إيصال المخزون")}: <strong>${escapeHtml(shortId(shipment.inventoryReceiptOperationId, "OP"))}</strong></p>` : ""}
      <h3>${supplyText("Lines", "البنود")}</h3>
      <div class="table-wrap compact-table"><table><thead><tr><th>SKU</th><th>${supplyText("Qty", "الكمية")}</th><th>${supplyText("Unit price", "سعر الوحدة")}</th><th>${supplyText("Line", "البند")}</th><th>${supplyText("Allocated", "الموزع")}</th><th>${supplyText("Landed unit", "تكلفة الوحدة النهائية")}</th><th>${supplyText("Batch", "التشغيلة")}</th></tr></thead><tbody>${shipment.lines.map((line) => `
        <tr class="${line.unitPrice == null || line.unitPrice <= 0 ? "supply-line-incomplete-row" : ""}"><td><strong>${escapeHtml(line.skuCode)}</strong><span class="muted-cell">${escapeHtml(line.productName)}</span></td><td>${escapeHtml(line.quantity)}</td><td>${line.unitPrice == null ? `<span class="status-pill status-warn">${supplyText("Blank", "فارغ")}</span>` : escapeHtml(formatMoney(line.unitPrice))}</td><td>${escapeHtml(formatMoney(line.lineSubtotal))}</td><td>${escapeHtml(formatMoney(line.allocatedCost))}</td><td>${escapeHtml(formatMoney(line.landedUnitCost))}</td><td>${escapeHtml(line.lotNumber || "-")} / ${escapeHtml(line.expiryDate || "-")}</td></tr>`).join("")}</tbody></table></div>
      <h3>${supplyText("Cost breakdown", "تفصيل التكاليف")}</h3>
      <div class="table-wrap compact-table"><table><thead><tr><th>${supplyText("Type", "النوع")}</th><th>${supplyText("Description", "الوصف")}</th><th>${supplyText("Amount", "المبلغ")}</th></tr></thead><tbody>${shipment.costs.length === 0 ? `<tr><td colspan="3">${supplyText("No costs.", "لا توجد تكاليف.")}</td></tr>` : shipment.costs.map((cost) => `<tr><td>${escapeHtml(supplyCostTypeLabel(cost.costType))}</td><td>${escapeHtml(cost.description || "-")}</td><td>${escapeHtml(formatMoney(cost.amount))}</td></tr>`).join("")}</tbody></table></div>
      <h3>${supplyText("History", "السجل")}</h3>
      <div class="table-wrap compact-table"><table><thead><tr><th>${supplyText("Action", "الإجراء")}</th><th>${supplyText("Time", "الوقت")}</th><th>${supplyText("Summary", "الملخص")}</th></tr></thead><tbody>${shipment.history.length === 0 ? `<tr><td colspan="3">${supplyText("No history.", "لا يوجد سجل حتى الآن.")}</td></tr>` : shipment.history.map((item) => `<tr><td>${escapeHtml(item.action)}</td><td>${escapeHtml(formatDateTime(item.createdAt))}</td><td>${escapeHtml(item.summary || "-")}</td></tr>`).join("")}</tbody></table></div>`;

    document.getElementById("supply-edit")?.addEventListener("click", () => fillSupplyForm(shipment));
    document.getElementById("supply-confirm")?.addEventListener("click", () => confirmSupplyShipment(shipment.id));
    document.getElementById("supply-cancel")?.addEventListener("click", () => cancelSupplyShipment(shipment.id));
    bindPrintReportButtons(target);
    await loadSupplyShipments();
  } catch (exception) {
    target.innerHTML = `<h2>${supplyText("Shipment detail", "تفاصيل الشحنة")}</h2><p>${escapeHtml(getFriendlyWorkspaceError(exception))}</p>`;
  }
}

function fillSupplyForm(shipment) {
  document.getElementById("supply-id").value = shipment.id;
  document.getElementById("supply-supplier").value = shipment.supplierName || "";
  document.getElementById("supply-invoice").value = shipment.invoiceNumber || "";
  document.getElementById("supply-date").value = shipment.shipmentDate ? shipment.shipmentDate.slice(0, 16) : "";
  document.getElementById("supply-location").value = shipment.destinationLocationId;
  document.getElementById("supply-notes").value = shipment.notes || "";
  document.getElementById("supply-lines").innerHTML = "";
  shipment.lines.forEach((line) => addSupplyLine(line));
  document.getElementById("supply-costs").innerHTML = "";
  shipment.costs.forEach((cost) => addSupplyCost(cost));
  clearSupplyValidation();
  updateSupplyFormSummary();
  document.getElementById("supply-form-panel")?.scrollIntoView({ behavior: "smooth", block: "start" });
}

async function saveSupplyShipment(event) {
  event.preventDefault();
  const error = document.getElementById("supply-form-error");
  error.hidden = true;
  const id = document.getElementById("supply-id").value;
  clearSupplyValidation();
  const payload = collectSupplyFormPayload();
  const validation = validateSupplyFormPayload(payload);
  if (validation.length > 0) {
    showSupplyValidation(validation);
    error.textContent = supplyText("Fix the highlighted shipment values before saving.", "راجع القيم المحددة قبل حفظ الشحنة.");
    error.hidden = false;
    return;
  }

  try {
    await request(id ? `/api/v1/supply/shipments/${encodeURIComponent(id)}` : "/api/v1/supply/shipments", {
      method: id ? "PUT" : "POST",
      body: JSON.stringify(stripSupplyPayloadInternals(payload))
    });
    notice(supplyText("Supply shipment saved.", "تم حفظ شحنة التوريد."), "success");
    resetSupplyForm();
    await loadSupplyShipments();
    if (selectedSupplyShipmentId) {
      await showSupplyDetail(selectedSupplyShipmentId);
    }
  } catch (exception) {
    showSupplyValidation(problemDetailsToList(exception));
    error.textContent = getFriendlyWorkspaceError(exception);
    error.hidden = false;
  }
}

async function confirmSupplyShipment(id) {
  try {
    await request(`/api/v1/supply/shipments/${encodeURIComponent(id)}/confirm`, { method: "POST" });
    notice(supplyText("Supply shipment received into inventory.", "تم استلام الشحنة في المخزون."), "success");
    await showSupplyDetail(id);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function cancelSupplyShipment(id) {
  try {
    await request(`/api/v1/supply/shipments/${encodeURIComponent(id)}/cancel`, { method: "POST" });
    notice(supplyText("Supply shipment cancelled.", "تم إلغاء شحنة التوريد."), "success");
    await showSupplyDetail(id);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function loadSupplyLocations() {
  inventoryLocations = await request("/api/v1/inventory/locations");
}

function collectSupplyFormPayload() {
  return {
    supplierName: document.getElementById("supply-supplier").value.trim(),
    invoiceNumber: document.getElementById("supply-invoice").value.trim() || null,
    shipmentDate: document.getElementById("supply-date").value || null,
    destinationLocationId: document.getElementById("supply-location").value,
    notes: document.getElementById("supply-notes").value.trim() || null,
    lines: [...document.querySelectorAll(".supply-line-row")].map((row) => {
      const priceValue = row.querySelector(".supply-line-price").value.trim();
      return {
        skuId: row.querySelector(".supply-line-sku").value,
        quantity: Number(row.querySelector(".supply-line-qty").value || 0),
        unitPrice: priceValue === "" ? null : Number(priceValue),
        lotNumber: row.querySelector(".supply-line-lot").value.trim() || null,
        expiryDate: row.querySelector(".supply-line-expiry").value || null,
        notes: row.querySelector(".supply-line-notes").value.trim() || null,
        _row: row
      };
    }),
    costs: [...document.querySelectorAll(".supply-cost-row")].map((row) => ({
      costType: row.querySelector(".supply-cost-type").value,
      description: row.querySelector(".supply-cost-description").value.trim() || null,
      amount: Number(row.querySelector(".supply-cost-amount").value || 0),
      _row: row
    }))
  };
}

function validateSupplyFormPayload(payload) {
  const messages = [];
  if (!payload.supplierName) {
    messages.push(supplyText("Supplier is required.", "اسم المورد مطلوب."));
  }
  if (!payload.destinationLocationId) {
    messages.push(supplyText("Destination warehouse is required.", "مخزن الوصول مطلوب."));
  }
  if (payload.lines.length === 0) {
    messages.push(supplyText("At least one SKU line is required.", "يجب إضافة بند SKU واحد على الأقل."));
  }

  const duplicateKeys = new Set();
  payload.lines.forEach((line, index) => {
    line._row.classList.remove("supply-line-invalid");
    if (!line.skuId) {
      line._row.classList.add("supply-line-invalid");
      messages.push(supplyText(`Line ${index + 1}: select a SKU.`, `البند ${index + 1}: اختر SKU.`));
    }
    if (!Number.isFinite(line.quantity) || line.quantity <= 0) {
      line._row.classList.add("supply-line-invalid");
      messages.push(supplyText(`Line ${index + 1}: quantity must be greater than zero.`, `البند ${index + 1}: الكمية يجب أن تكون أكبر من صفر.`));
    }
    if (line.unitPrice !== null && (!Number.isFinite(line.unitPrice) || line.unitPrice <= 0)) {
      line._row.classList.add("supply-line-invalid");
      messages.push(supplyText(`Line ${index + 1}: unit price must be greater than zero when entered.`, `البند ${index + 1}: سعر الوحدة يجب أن يكون أكبر من صفر عند إدخاله.`));
    }
    const duplicateKey = `${line.skuId}|${(line.lotNumber || "").toUpperCase()}|${line.expiryDate || ""}`;
    if (line.skuId && duplicateKeys.has(duplicateKey)) {
      line._row.classList.add("supply-line-invalid");
      messages.push(supplyText(`Line ${index + 1}: duplicate SKU, lot, and expiry must be combined.`, `البند ${index + 1}: يجب دمج نفس SKU مع نفس التشغيلة والصلاحية في بند واحد.`));
    }
    duplicateKeys.add(duplicateKey);
  });

  payload.costs.forEach((cost, index) => {
    cost._row.classList.remove("supply-line-invalid");
    if (!["Customs", "Freight", "Clearance", "Handling", "Insurance", "Other"].includes(cost.costType)) {
      cost._row.classList.add("supply-line-invalid");
      messages.push(supplyText(`Cost ${index + 1}: select a valid cost type.`, `التكلفة ${index + 1}: اختر نوع تكلفة صحيح.`));
    }
    if (!Number.isFinite(cost.amount) || cost.amount < 0) {
      cost._row.classList.add("supply-line-invalid");
      messages.push(supplyText(`Cost ${index + 1}: amount cannot be negative.`, `التكلفة ${index + 1}: المبلغ لا يمكن أن يكون سالبا.`));
    }
  });

  return messages;
}

function stripSupplyPayloadInternals(payload) {
  return {
    ...payload,
    lines: payload.lines.map(({ _row, ...line }) => line),
    costs: payload.costs.map(({ _row, ...cost }) => cost)
  };
}

function updateSupplyFormSummary() {
  const form = document.getElementById("supply-form");
  if (!form) {
    return;
  }
  const payload = collectSupplyFormPayload();
  const lines = payload.lines;
  const productTotal = lines.reduce((total, line) => total + (Number.isFinite(line.quantity) && Number.isFinite(line.unitPrice) ? line.quantity * line.unitPrice : 0), 0);
  const costTotal = payload.costs.reduce((total, cost) => total + (Number.isFinite(cost.amount) ? Math.max(0, cost.amount) : 0), 0);
  const incompletePrices = lines.filter((line) => line.unitPrice === null).length;
  const invalidPrices = lines.filter((line) => line.unitPrice !== null && (!Number.isFinite(line.unitPrice) || line.unitPrice <= 0)).length;
  document.getElementById("supply-form-product-total").textContent = formatMoney(productTotal);
  document.getElementById("supply-form-cost-total").textContent = formatMoney(costTotal);
  document.getElementById("supply-form-landed-total").textContent = formatMoney(productTotal + costTotal);
  const readiness = document.getElementById("supply-form-readiness");
  if (invalidPrices > 0) {
    readiness.textContent = supplyText("Invalid prices", "أسعار غير صحيحة");
    readiness.className = "status-danger";
  } else if (incompletePrices > 0 || lines.length === 0) {
    readiness.textContent = supplyText(`${incompletePrices || lines.length} incomplete`, `${incompletePrices || lines.length} ناقص`);
    readiness.className = "status-warn";
  } else {
    readiness.textContent = supplyText("Ready", "جاهزة");
    readiness.className = "status-ok";
  }
}

function updateSupplyPageMetrics(rows = supplyShipments) {
  const draftTotal = rows.filter((row) => row.status === "Draft").reduce((sum, row) => sum + Number(row.landedTotal || 0), 0);
  const readyCount = rows.filter((row) => row.status === "Draft" && Number(row.productSubtotal || 0) > 0).length;
  const draftMetric = document.getElementById("supply-draft-total");
  const readyMetric = document.getElementById("supply-ready-count");
  if (draftMetric) {
    draftMetric.textContent = formatMoney(draftTotal);
    draftMetric.className = draftTotal > 0 ? "status-warn" : "status-muted";
  }
  if (readyMetric) {
    readyMetric.textContent = String(readyCount);
    readyMetric.className = readyCount > 0 ? "status-ok" : "status-muted";
  }
}

function getSupplyShipmentReadiness(shipment) {
  const incomplete = shipment.lines.filter((line) => line.unitPrice == null).length;
  const invalid = shipment.lines.filter((line) => line.unitPrice != null && line.unitPrice <= 0).length;
  if (shipment.status !== "Draft") {
    return { canConfirm: false, label: supplyStatusLabel(shipment.status), message: supplyText("Only draft shipments can be confirmed.", "يمكن تأكيد الشحنات المسودة فقط.") };
  }
  if (shipment.lines.length === 0) {
    return { canConfirm: false, label: supplyText("No lines", "لا توجد بنود"), message: supplyText("At least one SKU line is required.", "يجب إضافة بند SKU واحد على الأقل.") };
  }
  if (invalid > 0) {
    return { canConfirm: false, label: supplyText("Invalid prices", "أسعار غير صحيحة"), message: supplyText("Every SKU price must be greater than zero before confirmation.", "كل أسعار SKU يجب أن تكون أكبر من صفر قبل التأكيد.") };
  }
  if (incomplete > 0) {
    return { canConfirm: false, label: supplyText(`${incomplete} blank`, `${incomplete} سعر ناقص`), message: supplyText("Every SKU line needs a unit price before confirmation.", "كل بند SKU يحتاج سعر وحدة قبل التأكيد.") };
  }
  return { canConfirm: true, label: supplyText("Ready", "جاهزة"), message: supplyText("Ready to confirm.", "جاهزة للتأكيد.") };
}

function clearSupplyValidation() {
  document.querySelectorAll(".supply-line-invalid").forEach((row) => row.classList.remove("supply-line-invalid"));
  const list = document.getElementById("supply-validation-list");
  if (list) {
    list.hidden = true;
    list.innerHTML = "";
  }
}

function showSupplyValidation(messages) {
  const list = document.getElementById("supply-validation-list");
  if (!list || messages.length === 0) {
    return;
  }
  list.innerHTML = `<strong>${supplyText("Review these values", "راجع هذه القيم")}</strong><ul>${messages.map((message) => `<li>${escapeHtml(message)}</li>`).join("")}</ul>`;
  list.hidden = false;
}

function problemDetailsToList(exception) {
  const message = exception instanceof Error ? exception.message : "";
  if (!message || !(message.includes("{") || message.includes("["))) {
    return [];
  }
  try {
    const body = JSON.parse(message);
    return Object.entries(body.errors || {}).flatMap(([field, errors]) => errors.map((error) => `${field}: ${error}`));
  } catch {
    return [];
  }
}

async function renderStocktakes() {
  const isAdmin = isSystemAdminRole(getAuth()?.user.role);
  document.getElementById("view").innerHTML = `
    <section class="catalog-layout">
      <aside class="catalog-side">
        <section class="band">
          <div class="section-head"><div><h2>Stocktake</h2><p class="muted-text">Count physical stock by SKU, lot, and expiry, then confirm reconciliations through the ledger.</p></div><button id="stocktake-refresh" class="button secondary" type="button">Refresh</button></div>
          ${isAdmin ? `
            <form id="stocktake-create-form" class="form">
              <div class="form-error" id="stocktake-error" hidden></div>
              <div class="field"><label for="stocktake-location">Location</label><select id="stocktake-location" class="select"></select></div>
              <div class="field"><label for="stocktake-notes">Notes</label><textarea id="stocktake-notes" class="input" rows="3"></textarea></div>
              <button class="button primary" type="submit">Open session</button>
            </form>` : `<p class="muted-text">Read-only stocktake review.</p>`}
        </section>
      </aside>
      <section class="catalog-main">
        <section class="band">
          <div class="section-head"><h2>Sessions</h2><span id="stocktake-count" class="muted-text">Loading</span></div>
          <div class="table-wrap"><table><thead><tr><th>Session</th><th>Location</th><th>Status</th><th>Counted</th><th>Discrepancy</th><th>Created</th><th>Actions</th></tr></thead><tbody id="stocktake-rows"></tbody></table></div>
        </section>
        <section class="band" id="stocktake-detail"><h2>Session detail</h2><p class="muted-text">Select a session to enter counts or review discrepancies.</p></section>
      </section>
    </section>`;

  document.getElementById("stocktake-refresh").addEventListener("click", loadStocktakes);
  document.getElementById("stocktake-create-form")?.addEventListener("submit", createStocktakeSession);
  await loadStocktakeReferenceData();
  await loadStocktakes();
}

async function loadStocktakeReferenceData() {
  inventoryLocations = await request("/api/v1/inventory/locations");
  const locationSelect = document.getElementById("stocktake-location");
  if (locationSelect) {
    locationSelect.innerHTML = inventoryLocations.map((location) => `<option value="${escapeHtml(location.id)}">${escapeHtml(location.name)}</option>`).join("");
  }
  await hydrateOperationSkus();
  inventorySkuOptions = operationSkuOptions.map((sku) => ({ id: sku.id, label: sku.label || `${sku.skuCode} - ${sku.productName}` }));
}

async function loadStocktakes() {
  const tbody = document.getElementById("stocktake-rows");
  const count = document.getElementById("stocktake-count");
  if (!tbody || !count) {
    return;
  }
  try {
    const result = await request("/api/v1/stocktakes?pageSize=50");
    count.textContent = `${result.totalCount} session(s)`;
    tbody.innerHTML = result.items.length === 0 ? `<tr><td colspan="7">No stocktake sessions yet.</td></tr>` : result.items.map((session) => {
      const location = inventoryLocations.find((value) => value.id === session.locationId);
      return `<tr>
        <td>${escapeHtml(shortId(session.id, "STK"))}</td>
        <td>${escapeHtml(location?.name || shortId(session.locationId, "LOC"))}</td>
        <td>${escapeHtml(session.status)}</td>
        <td>${escapeHtml(session.productsCounted)}</td>
        <td>${escapeHtml(session.totalDiscrepancyUnits)}</td>
        <td>${escapeHtml(formatDateTime(session.createdAt))}</td>
        <td><button class="button secondary table-action" type="button" data-stocktake-detail="${escapeHtml(session.id)}">Details</button><button class="button secondary table-action" type="button" data-print-report="stocktake-summary" data-print-id="${escapeHtml(session.id)}" data-print-code="${escapeHtml(shortId(session.id, "STK"))}">Print</button></td>
      </tr>`;
    }).join("");
    tbody.querySelectorAll("[data-stocktake-detail]").forEach((button) => button.addEventListener("click", () => showStocktakeDetail(button.dataset.stocktakeDetail)));
    bindPrintReportButtons(tbody);
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="7">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function createStocktakeSession(event) {
  event.preventDefault();
  clearFormError("stocktake-error");
  const locationId = document.getElementById("stocktake-location").value;
  if (!locationId) {
    showFormError("stocktake-error", "Location is required.");
    return;
  }

  try {
    const session = await request("/api/v1/stocktakes", {
      method: "POST",
      body: JSON.stringify({ locationId, notes: document.getElementById("stocktake-notes").value.trim() || null })
    });
    notice("Stocktake session opened.", "success");
    await loadStocktakeReferenceData();
    await loadStocktakes();
    await showStocktakeDetail(session.id);
  } catch (exception) {
    showFormError("stocktake-error", getFriendlyWorkspaceError(exception));
  }
}

async function showStocktakeDetail(sessionId) {
  const isAdmin = isSystemAdminRole(getAuth()?.user.role);
  const target = document.getElementById("stocktake-detail");
  const session = await request(`/api/v1/stocktakes/${sessionId}`);
  const location = inventoryLocations.find((value) => value.id === session.locationId);
  const skuOptions = inventorySkuOptions.map((sku) => `<option value="${escapeHtml(sku.id)}">${escapeHtml(sku.label)}</option>`).join("");
  target.innerHTML = `
    <div class="section-head">
      <div><h2>Session ${escapeHtml(shortId(session.id, "STK"))}</h2><p class="muted-text">${escapeHtml(location?.name || shortId(session.locationId, "LOC"))} / ${escapeHtml(session.status)}</p></div>
      ${isAdmin && session.status === "Draft" ? `<button id="stocktake-confirm" class="button primary" type="button">Confirm adjustments</button>` : ""}
    </div>
    <div class="table-wrap compact-table"><table><thead><tr><th>SKU</th><th>Lot</th><th>Expiry</th><th>System</th><th>Physical</th><th>Delta</th><th>Note</th></tr></thead><tbody>${session.lines.length === 0
      ? `<tr><td colspan="7">No counted lines yet.</td></tr>`
      : session.lines.map((line) => `<tr><td>${escapeHtml(stocktakeSkuLabel(line.skuId))}</td><td>${escapeHtml(line.lotNumber || "-")}</td><td>${escapeHtml(line.expiryDate || "-")}</td><td>${escapeHtml(line.systemQtyBefore)}</td><td>${escapeHtml(line.physicalCount)}</td><td>${escapeHtml(line.delta)}</td><td>${escapeHtml(line.lineNote || "-")}</td></tr>`).join("")}</tbody></table></div>
    ${isAdmin && session.status === "Draft" ? `
      <form id="stocktake-lines-form" class="form wide-form compact-form">
        <div class="form-error" id="stocktake-lines-error" hidden></div>
        <div id="stocktake-line-editor" class="line-editor"></div>
        <div class="form-actions"><button id="add-stocktake-line" class="button secondary" type="button">Add line</button><button class="button primary" type="submit">Save counts</button></div>
      </form>` : ""}`;

  if (isAdmin && session.status === "Draft") {
    document.getElementById("stocktake-confirm").addEventListener("click", () => confirmStocktake(session.id));
    const editor = document.getElementById("stocktake-line-editor");
    const addLine = (line = {}) => {
      const row = document.createElement("div");
      row.className = "stocktake-line-row";
      row.innerHTML = `
        <div class="field"><label>SKU</label><select class="select stocktake-line-sku">${skuOptions}</select></div>
        <div class="field"><label>Lot number</label><input class="input stocktake-line-lot" value="${escapeHtml(line.lotNumber || "")}" placeholder="Blank if none"></div>
        <div class="field"><label>Expiry date</label><input class="input stocktake-line-expiry" type="date" value="${escapeHtml(line.expiryDate || "")}"></div>
        <div class="field"><label>Physical count</label><input class="input stocktake-line-count" type="number" min="0" step="1" value="${escapeHtml(line.physicalCount ?? 0)}"></div>
        <div class="field"><label>Note</label><input class="input stocktake-line-note" value="${escapeHtml(line.lineNote || "")}"></div>
        <button class="button secondary" type="button" data-remove-line>Remove</button>`;
      editor.appendChild(row);
      row.querySelector(".stocktake-line-sku").value = line.skuId || inventorySkuOptions[0]?.id || "";
      row.querySelector("[data-remove-line]").addEventListener("click", () => row.remove());
    };
    session.lines.forEach(addLine);
    if (session.lines.length === 0) {
      addLine();
    }
    document.getElementById("add-stocktake-line").addEventListener("click", () => addLine());
    document.getElementById("stocktake-lines-form").addEventListener("submit", (event) => saveStocktakeLines(event, session.id));
  }
}

function stocktakeSkuLabel(skuId) {
  return inventorySkuOptions.find((sku) => sku.id === skuId)?.label || shortId(skuId, "SKU");
}

async function saveStocktakeLines(event, sessionId) {
  event.preventDefault();
  clearFormError("stocktake-lines-error");
  const lines = Array.from(document.querySelectorAll(".stocktake-line-row")).map((row) => ({
    skuId: row.querySelector(".stocktake-line-sku").value,
    lotNumber: row.querySelector(".stocktake-line-lot").value.trim() || null,
    expiryDate: row.querySelector(".stocktake-line-expiry").value || null,
    physicalCount: Number(row.querySelector(".stocktake-line-count").value),
    lineNote: row.querySelector(".stocktake-line-note").value.trim() || null
  }));
  if (lines.some((line) => !line.skuId || !Number.isInteger(line.physicalCount) || line.physicalCount < 0)) {
    showFormError("stocktake-lines-error", "Every stocktake line needs a SKU and non-negative whole-number count.");
    return;
  }
  try {
    await request(`/api/v1/stocktakes/${sessionId}/lines`, { method: "PUT", body: JSON.stringify({ lines }) });
    notice("Stocktake counts saved.", "success");
    await showStocktakeDetail(sessionId);
    await loadStocktakes();
  } catch (exception) {
    showFormError("stocktake-lines-error", getFriendlyWorkspaceError(exception));
  }
}

async function confirmStocktake(sessionId) {
  try {
    await request(`/api/v1/stocktakes/${sessionId}/confirm`, { method: "POST" });
    notice("Stocktake confirmed and ledger adjustments posted.", "success");
    await showStocktakeDetail(sessionId);
    await loadStocktakes();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function renderNotifications() {
  const auth = getAuth();
  const isAdmin = isSystemAdminRole(auth?.user.role);
  const canReadRecalls = ["Admin", "ERPAdmin", "CLevel"].includes(auth?.user.role);
  notificationPageState = { page: 1, pageSize: 10 };
  document.getElementById("view").innerHTML = `
    <section class="band">
      <div class="section-head">
        <div>
          <h2>Notifications</h2>
          <p class="muted-text">Review alerts, workflow updates, targets, and linked records without losing context.</p>
        </div>
        <span id="notification-count" class="status-pill status-muted">Loading</span>
      </div>
      <div class="notification-summary">
        <div class="metric"><span>Visible</span><strong id="notification-visible-count">-</strong></div>
        <div class="metric"><span>Unread</span><strong id="notification-unread-count">-</strong></div>
        <div class="metric"><span>Scope</span><strong>${escapeHtml(roleLabel(auth?.user.role || ""))}</strong></div>
      </div>
      <div class="toolbar notification-toolbar">
        <select id="notification-type-filter" class="select compact-select" aria-label="Notification type">
          <option value="">All types</option>
        </select>
        <label class="inline-check"><input id="notification-unread-filter" type="checkbox"> Unread only</label>
        <button id="notifications-refresh" class="button secondary" type="button">Refresh</button>
        <button id="mark-all-read" class="button secondary" type="button">Mark all read</button>
      </div>
      <div id="notification-list" class="notification-list">Loading</div>
      <div class="pagination-bar" id="notification-pagination" hidden>
        <button class="button secondary table-action" type="button" id="notifications-prev">Previous</button>
        <span class="muted-text" id="notifications-page-label">Page 1 of 1</span>
        <button class="button secondary table-action" type="button" id="notifications-next">Next</button>
      </div>
    </section>
    ${canReadRecalls ? `
      <section class="band" id="merchant-expiry-recalls-section">
        <div class="section-head"><div><h2>Merchant expiry recalls</h2><p class="muted-text">Sold merchant batches inside the configured expiry window, ordered by earliest expiry.</p></div><span id="merchant-recall-count" class="status-pill status-muted">Loading</span></div>
        <div class="table-wrap"><table><thead><tr><th>Merchant</th><th>SKU / product</th><th>Lot</th><th>Expiry</th><th>Sold</th><th>Returned</th><th>Status</th><th>Actions</th></tr></thead><tbody id="merchant-recall-rows"><tr><td colspan="8">Loading recalls</td></tr></tbody></table></div>
        ${isAdmin ? `<form id="merchant-recall-config" class="form grid-form band-subtle"><div class="field"><label for="merchant-recall-months">Global expiry window (months)</label><input id="merchant-recall-months" class="input" type="number" min="1" max="120" value="24" required></div><label class="inline-check"><input id="merchant-recall-active" type="checkbox"> Daily scan active</label><div class="form-actions"><button class="button secondary" type="submit">Save recall settings</button></div></form>` : ""}
      </section>` : ""}
    ${isAdmin ? `
      <section class="band">
        <h2>Manual alert triggers</h2>
        <p class="muted-text">Run alert scans on demand when you want to refresh operational warnings immediately.</p>
        <div class="toolbar">
          <button class="button secondary" type="button" data-alert-run="low-stock">Low stock</button>
          <button class="button secondary" type="button" data-alert-run="expiry">Expiry</button>
          <button class="button secondary" type="button" data-alert-run="unresolved-reserves">Unresolved reserves</button>
          <button class="button secondary" type="button" data-alert-run="open-payment-summary">Generate weekly open-payment summary</button>
        </div>
      </section>` : ""}`;

  document.getElementById("mark-all-read").addEventListener("click", markNotificationsRead);
  document.getElementById("notifications-refresh").addEventListener("click", loadNotifications);
  document.getElementById("notification-type-filter").addEventListener("change", () => loadNotifications(1));
  document.getElementById("notification-unread-filter").addEventListener("change", () => loadNotifications(1));
  document.getElementById("notifications-prev").addEventListener("click", () => loadNotifications(Math.max(1, (notificationPageState.page || 1) - 1)));
  document.getElementById("notifications-next").addEventListener("click", () => loadNotifications((notificationPageState.page || 1) + 1));
  document.querySelectorAll("[data-alert-run]").forEach((button) => button.addEventListener("click", () => runAlert(button.dataset.alertRun)));
  document.getElementById("merchant-recall-config")?.addEventListener("submit", saveMerchantRecallConfig);
  await Promise.all([
    loadNotificationTypes(),
    loadNotifications(),
    canReadRecalls ? loadMerchantExpiryRecalls() : Promise.resolve(),
    isAdmin ? loadMerchantRecallConfig() : Promise.resolve()
  ]);
}

async function loadMerchantExpiryRecalls() {
  const tbody = document.getElementById("merchant-recall-rows");
  const count = document.getElementById("merchant-recall-count");
  if (!tbody || !count) return;
  const canManage = isSystemAdminRole(getAuth()?.user.role);
  try {
    const recalls = await request("/api/v1/merchant-expiry-recalls?status=Active");
    count.textContent = `${recalls.length} recalls`;
    tbody.innerHTML = recalls.length === 0 ? `<tr><td colspan="8">No active merchant expiry recalls.</td></tr>` : recalls.map((recall) => `
      <tr data-merchant-recall-row="${escapeHtml(recall.id)}">
        <td><strong>${escapeHtml(recall.merchantName)}</strong></td>
        <td><strong>${escapeHtml(recall.skuCode || shortId(recall.skuId, "SKU"))}</strong><span class="muted-cell">${escapeHtml(recall.productName || "-")}</span></td>
        <td>${escapeHtml(recall.lotNumber || "-")}</td>
        <td>${expiryBadge(recall.expiryDate)}</td>
        <td>${escapeHtml(recall.soldQuantity)}</td>
        <td>${escapeHtml(recall.returnedQuantity)}</td>
        <td><span class="status-pill ${recall.daysToExpiry < 0 ? "status-warn" : "status-muted"}">${recall.daysToExpiry < 0 ? "Expired" : "Approaching expiry"}</span></td>
        <td>${canManage ? `<button class="button primary table-action" type="button" data-recall-return="${escapeHtml(recall.id)}">Start Return</button><button class="button secondary table-action" type="button" data-recall-no-stock="${escapeHtml(recall.id)}">No Stock at Merchant</button>` : `<span class="muted-text">Read only</span>`}</td>
      </tr>`).join("");
    tbody.querySelectorAll("[data-recall-return]").forEach((button) => button.addEventListener("click", () => startMerchantRecallReturn(recalls.find((recall) => recall.id === button.dataset.recallReturn))));
    tbody.querySelectorAll("[data-recall-no-stock]").forEach((button) => button.addEventListener("click", () => closeMerchantRecallNoStock(button.dataset.recallNoStock, button)));
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="8">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function startMerchantRecallReturn(recall) {
  if (!recall) return;
  try {
    const locations = await request("/api/v1/inventory/locations");
    const values = await merchantRecallReturnDialog(locations, recall);
    if (!values) return;
    const draft = await request(`/api/v1/merchant-expiry-recalls/${recall.id}/return-draft`, { method: "POST", body: JSON.stringify(values) });
    notice(`Return draft ${draft.operationNumber} created.`, "success");
    location.hash = "#/operations";
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function closeMerchantRecallNoStock(recallId, button) {
  const note = await promptDialog({ title: "No Stock at Merchant", label: "Explain how the physical stock was checked.", required: true, multiline: true });
  if (!note) return;
  try {
    await withMutationGuard(`merchant-recall:${recallId}:no-stock`, button, () => request(`/api/v1/merchant-expiry-recalls/${recallId}/no-stock`, { method: "POST", body: JSON.stringify({ note }) }));
    notice("Merchant recall closed as no stock.", "success");
    await Promise.all([loadMerchantExpiryRecalls(), loadNotifications(), loadNotificationTypes()]);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function loadMerchantRecallConfig() {
  const months = document.getElementById("merchant-recall-months");
  const active = document.getElementById("merchant-recall-active");
  if (!months || !active) return;
  try {
    const config = await request("/api/v1/alerts/config/merchant-expiry-recall");
    months.value = config.thresholdValue || 24;
    active.checked = Boolean(config.isActive);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function saveMerchantRecallConfig(event) {
  event.preventDefault();
  try {
    await request("/api/v1/alerts/config/merchant-expiry-recall", { method: "PUT", body: JSON.stringify({ thresholdValue: Number(document.getElementById("merchant-recall-months").value), thresholdUnit: "Months", isActive: document.getElementById("merchant-recall-active").checked }) });
    notice("Merchant recall settings saved.", "success");
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function loadNotificationTypes() {
  const select = document.getElementById("notification-type-filter");
  if (!select) {
    return;
  }

  const selected = select.value;
  try {
    const types = await request("/api/v1/notifications/types");
    select.innerHTML = `<option value="">All types</option>${types.map((type) => `<option value="${escapeHtml(type.alertType)}">${escapeHtml(notificationTypeLabel(type.alertType))} (${escapeHtml(type.count)}${type.unreadCount ? `, ${escapeHtml(type.unreadCount)} unread` : ""})</option>`).join("")}`;
    select.value = types.some((type) => type.alertType === selected) ? selected : "";
  } catch {
    select.innerHTML = `<option value="">All types</option>`;
  }
}

async function loadNotifications(page = notificationPageState.page || 1) {
  const list = document.getElementById("notification-list");
  const count = document.getElementById("notification-count");
  const visible = document.getElementById("notification-visible-count");
  const unread = document.getElementById("notification-unread-count");
  const pagination = document.getElementById("notification-pagination");
  const pageLabel = document.getElementById("notifications-page-label");
  const prev = document.getElementById("notifications-prev");
  const next = document.getElementById("notifications-next");
  const type = document.getElementById("notification-type-filter")?.value || "";
  const unreadOnly = document.getElementById("notification-unread-filter")?.checked;
  const pageSize = notificationPageState.pageSize || 10;
  const requestedPage = Math.max(1, Number(page) || 1);
  const params = new URLSearchParams({ page: String(requestedPage), pageSize: String(pageSize) });
  if (type) {
    params.set("alertType", type);
  }
  if (unreadOnly) {
    params.set("unreadOnly", "true");
  }
  try {
    const [result, unreadResult] = await Promise.all([
      request(`/api/v1/notifications?${params.toString()}`),
      request("/api/v1/notifications/unread-count")
    ]);
    count.textContent = `${result.totalCount} visible`;
    visible.textContent = result.totalCount;
    unread.textContent = unreadResult.count;
    const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
    if (result.items.length === 0 && result.totalCount > 0 && requestedPage > totalPages) {
      notificationPageState = { page: totalPages, pageSize: result.pageSize };
      await loadNotifications(totalPages);
      return;
    }
    notificationPageState = { page: Math.min(result.page, totalPages), pageSize: result.pageSize };
    list.innerHTML = result.items.length === 0
      ? `<div class="empty-state">No notifications match the current filters.</div>`
      : result.items.map(renderNotificationCard).join("");
    if (pagination && pageLabel && prev && next) {
      pagination.hidden = result.totalCount <= result.pageSize;
      pageLabel.textContent = `Page ${notificationPageState.page} of ${totalPages}`;
      prev.disabled = notificationPageState.page <= 1;
      next.disabled = notificationPageState.page >= totalPages;
    }
    list.querySelectorAll("[data-read-notification]").forEach((button) => button.addEventListener("click", () => markNotificationRead(button.dataset.readNotification)));
    list.querySelectorAll("[data-toggle-notification]").forEach((button) => button.addEventListener("click", () => toggleNotificationDetails(button.dataset.toggleNotification)));
    list.querySelectorAll("[data-resolve-notification]").forEach((button) => button.addEventListener("click", () => resolveNotificationDestination(button.dataset.resolveNotification)));
    updateNotificationBadge();
  } catch (exception) {
    count.textContent = "Failed";
    list.innerHTML = `<div class="empty-state">${escapeHtml(getFriendlyWorkspaceError(exception))}</div>`;
    if (pagination) {
      pagination.hidden = true;
    }
  }
}

function renderNotificationCard(item) {
  const tone = item.isRead ? "status-muted" : "status-warning";
  const target = item.targetRole ? roleLabel(item.targetRole) : (item.targetUserId ? `User ${shortId(item.targetUserId, "USR")}` : "Broadcast");
  const actionLabel = item.actionLabel || notificationActionLabel(item);
  const actionButton = item.referenceId
    ? `<button class="button secondary table-action" type="button" data-resolve-notification="${escapeHtml(item.id)}">${escapeHtml(actionLabel)}</button>`
    : "";
  return `
    <article class="notification-card ${item.isRead ? "is-read" : "is-unread"}" data-notification-card="${escapeHtml(item.id)}">
      <div class="notification-main">
        <div>
          <div class="notification-title-row">
            <span class="status-pill ${tone}">${escapeHtml(notificationTypeLabel(item.alertType))}</span>
            <span class="muted-text">${escapeHtml(formatDateTime(item.createdAt))}</span>
          </div>
          <p class="notification-message">${escapeHtml(item.message)}</p>
          ${item.referenceCode ? `<p class="muted-text notification-record-code">${escapeHtml(item.referenceCode)}${item.referenceTitle ? ` / ${escapeHtml(item.referenceTitle)}` : ""}</p>` : ""}
        </div>
        <div class="notification-actions">
          <button class="button secondary table-action" type="button" data-toggle-notification="${escapeHtml(item.id)}">Details</button>
          ${actionButton}
          ${item.isRead ? `<span class="status-pill status-muted">Read</span>` : `<button class="button primary table-action" type="button" data-read-notification="${escapeHtml(item.id)}">Mark read</button>`}
        </div>
      </div>
      <div id="notification-details-${escapeHtml(item.id)}" class="notification-details" hidden>
        <dl>
          <div><dt>Target</dt><dd>${escapeHtml(target)}</dd></div>
          <div><dt>Channel</dt><dd>${escapeHtml(item.channel || "-")}</dd></div>
          <div><dt>Reference</dt><dd>${escapeHtml(item.referenceCode || item.referenceType || "-")}${item.referenceId && !item.referenceCode ? ` / ${escapeHtml(shortId(item.referenceId, referencePrefix(item.referenceType)))}` : ""}</dd></div>
          <div><dt>Event location</dt><dd>${item.referenceId ? escapeHtml(actionLabel) : "-"}</dd></div>
          <div><dt>Status</dt><dd>${item.isRead ? "Read" : "Unread"}</dd></div>
        </dl>
      </div>
    </article>`;
}

function notificationActionUrl(item) {
  if (item.actionUrl) {
    return item.actionUrl;
  }
  const type = (item.referenceType || "").toLowerCase();
  const alertType = (item.alertType || "").toLowerCase();
  if (["stockbalance", "inventorybatch"].includes(type) || ["lowstock", "expiry"].includes(alertType)) {
    return "#/inventory";
  }
  if (type === "paymentlog" || alertType.includes("payment") || alertType === "outstandingbalances") {
    return "#/payments";
  }
  if (type === "operation" || alertType.includes("operation") || alertType === "unresolvedreserves") {
    return "#/operations";
  }
  if (type === "stocktake" || alertType.includes("stocktake")) {
    return "#/stocktakes";
  }
  if (type === "merchant") {
    return "#/crm";
  }
  if (type === "merchantexpiryrecall") {
    return "#/notifications";
  }
  if (alertType.includes("report") || alertType.includes("export")) {
    return "#/reports";
  }
  return "";
}

function notificationActionLabel(item) {
  const labels = {
    stockbalance: "Open inventory balance",
    inventorybatch: "Open inventory batch",
    paymentlog: "Open payment",
    operation: "Open operation",
    stocktake: "Open stocktake",
    supplyshipment: "Open shipment",
    merchant: "Open merchant",
    merchantexpiryrecall: "Open merchant recall",
    exportlog: "Open export"
  };
  return labels[(item.referenceType || "").toLowerCase()] || "Open related record";
}

async function resolveNotificationDestination(id) {
  try {
    const destination = await request(`/api/v1/notifications/${encodeURIComponent(id)}/resolve`);
    if (destination.status !== "Ready") {
      notice(destination.message || "This record is not available.", destination.status === "Forbidden" ? "error" : "warning");
      return;
    }
    if (!destination.navigationReference) {
      notice("This secure link could not be created.", "warning");
      return;
    }
    location.hash = `${destination.route}?ref=${encodeURIComponent(destination.navigationReference)}`;
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function toggleNotificationDetails(id) {
  const details = document.getElementById(`notification-details-${id}`);
  if (details) {
    details.hidden = !details.hidden;
  }
}

function notificationTypeLabel(type) {
  const labels = {
    LowStock: "Low stock",
    Expiry: "Expiry",
    UnresolvedReserves: "Unresolved reserves",
    OpenPaymentWeeklySummary: "Open-payment weekly summary",
    PaymentWorkflow: "Payment workflow",
    OperationStatus: "Operation status",
    StocktakeConfirmed: "Stocktake confirmed",
    MerchantExpiryRecall: "Merchant expiry recall"
  };
  return labels[type] || type || "Notification";
}

async function markNotificationRead(id) {
  await request(`/api/v1/notifications/${id}/read`, { method: "PATCH" });
  await loadNotificationTypes();
  await loadNotifications();
}

async function markNotificationsRead() {
  await request("/api/v1/notifications/read-all", { method: "PATCH" });
  await loadNotificationTypes();
  await loadNotifications();
}

async function runAlert(name) {
  try {
    const result = await request(`/api/v1/alerts/run/${name}`, { method: "POST" });
    notice(`Alert run matched ${result.matchedItems} item(s).`, "success");
    await loadNotificationTypes();
    await loadNotifications();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function renderAdmin() {
  const isAdministrator = getAuth()?.user.role === "Admin";
  const canResetPasswords = isAdministrator;
  document.getElementById("view").innerHTML = `
    <section class="band">
      <div class="section-head">
        <div>
          <h2>Users and access</h2>
          <p class="muted-text">Review employee accounts, assigned locations, and controlled access from one admin surface.</p>
        </div>
        <span id="admin-users-count" class="status-pill status-muted">Loading</span>
      </div>
      ${isAdministrator ? `
        <form id="admin-create-user-form" class="admin-create-user-form band-subtle" novalidate>
          <div class="section-head tight-head">
            <div>
              <h3>Create employee account</h3>
              <p class="muted-text">Set the employee's sign-in name, temporary password, role, and warehouse scope.</p>
            </div>
          </div>
          <div class="form-grid">
            <div class="field"><label for="admin-user-full-name">Full name</label><input class="input" id="admin-user-full-name" name="fullName" autocomplete="name" required></div>
            <div class="field"><label for="admin-user-username">Username</label><input class="input" id="admin-user-username" name="username" autocomplete="username" required></div>
            <div class="field"><label for="admin-user-role">Role</label><select class="select" id="admin-user-role" name="role" required>
              <option value="Admin">Administrator</option>
              <option value="ERPAdmin">ERP administrator</option>
              <option value="CLevel">C-Level</option>
              <option value="Accountant">Accountant</option>
              <option value="WarehouseClerk">Warehouse clerk</option>
            </select></div>
            <div class="field" id="admin-user-location-field" hidden><label for="admin-user-location">Warehouse location</label><select class="select" id="admin-user-location" name="locationId" disabled><option value="">Loading locations...</option></select></div>
            <div class="field"><label for="admin-user-password">Temporary password</label><input class="input" id="admin-user-password" name="password" type="password" autocomplete="new-password" minlength="8" required></div>
            <div class="field"><label for="admin-user-confirm-password">Confirm password</label><input class="input" id="admin-user-confirm-password" name="confirmPassword" type="password" autocomplete="new-password" minlength="8" required></div>
          </div>
          <div class="form-actions"><button class="button primary" type="submit">Create employee account</button></div>
        </form>
        <form id="admin-create-location-form" class="admin-create-user-form band-subtle" novalidate hidden>
          <div class="section-head tight-head">
            <div>
              <h3>Add warehouse</h3>
              <p class="muted-text">Only the primary Administrator can add an active warehouse location.</p>
            </div>
          </div>
          <div class="form-grid">
            <div class="field"><label for="admin-location-name">Warehouse name</label><input class="input" id="admin-location-name" name="name" autocomplete="off" required></div>
            <div class="field"><label for="admin-location-type">Location type</label><select class="select" id="admin-location-type" name="locationType" required>
              <option value="SubWarehouse">Sub-warehouse</option>
              <option value="Retail">Retail</option>
              <option value="Online">Online</option>
              <option value="MainWarehouse">Main warehouse</option>
            </select></div>
          </div>
          <p class="muted-text">There can be only one active Main warehouse.</p>
          <div class="form-actions"><button class="button primary" type="submit">Add warehouse</button></div>
        </form>` : ""}
      <div id="admin-users-error" class="form-error" hidden></div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Username / full name</th>
              <th>Role</th>
              <th>Location</th>
              <th>Status</th>
              ${canResetPasswords ? "<th>New password</th><th>Confirm</th><th>Password</th><th>Account</th>" : ""}
            </tr>
          </thead>
          <tbody id="admin-users-rows"></tbody>
        </table>
      </div>
    </section>`;

  if (isAdministrator) {
    const form = document.getElementById("admin-create-user-form");
    const role = document.getElementById("admin-user-role");
    role?.addEventListener("change", syncAdminCreateUserLocation);
    form?.addEventListener("submit", createAdminUser);
    document.getElementById("admin-create-location-form")?.addEventListener("submit", createAdminLocation);
    syncAdminCreateUserLocation();
  }

  await loadAdminUsers();
}

async function loadAdminUsers() {
  const tbody = document.getElementById("admin-users-rows");
  const count = document.getElementById("admin-users-count");
  const error = document.getElementById("admin-users-error");
  if (!tbody || !count || !error) {
    return;
  }

  error.hidden = true;
  const canResetPasswords = getAuth()?.user.role === "Admin";
  const colspan = canResetPasswords ? 8 : 4;
  tbody.innerHTML = `<tr><td colspan="${colspan}">Loading users...</td></tr>`;

  try {
    const [users, locations] = await Promise.all([
      request("/api/v1/users"),
      request("/api/v1/inventory/locations").catch(() => [])
    ]);
    const locationNames = new Map(locations.map((location) => [location.id, location.name]));
    const currentUserId = getAuth()?.user?.userId;
    const isCurrentPrimaryAdmin = users.some((user) => user.id === currentUserId && user.isPrimaryAdmin);
    const createLocationForm = document.getElementById("admin-create-location-form");
    if (createLocationForm) createLocationForm.hidden = !isCurrentPrimaryAdmin;
    populateAdminCreateUserLocations(locations);
    count.textContent = `${users.length} user${users.length === 1 ? "" : "s"}`;
    tbody.innerHTML = users.length === 0 ? `<tr><td colspan="${colspan}">No users found.</td></tr>` : users.map((user) => `
      <tr data-admin-user-row="${escapeHtml(user.id)}">
        <td><strong>${escapeHtml(user.username)}</strong><br><span class="muted-text">${escapeHtml(user.fullName || "-")}</span></td>
        <td>${escapeHtml(roleLabel(user.role))}${user.isPrimaryAdmin ? '<br><span class="status-pill status-info">Primary Admin</span>' : ""}</td>
        <td>${escapeHtml(user.locationId ? (locationNames.get(user.locationId) || "Unknown location") : "All locations")}</td>
        <td><span class="status-pill ${user.isActive ? "status-ok" : "status-muted"}">${user.isActive ? "Active" : "Inactive"}</span>
          ${isCurrentPrimaryAdmin && user.id !== currentUserId && !user.isPrimaryAdmin ? `<br><button class="button secondary table-action" type="button" data-admin-set-active="${escapeHtml(user.id)}" data-admin-next-active="${String(!user.isActive)}">${user.isActive ? "Deactivate" : "Reactivate"}</button>` : ""}
        </td>
        ${canResetPasswords ? `<td><input class="input compact-input" type="password" autocomplete="new-password" data-admin-password="${escapeHtml(user.id)}" placeholder="8+ characters"></td>
        <td><input class="input compact-input" type="password" autocomplete="new-password" data-admin-confirm-password="${escapeHtml(user.id)}" placeholder="Repeat"></td>
        <td><button class="button primary table-action" type="button" data-admin-change-password="${escapeHtml(user.id)}">Change</button></td>
        <td>
          ${isCurrentPrimaryAdmin && user.isActive && user.role === "Admin" && !user.isPrimaryAdmin ? `<button class="button secondary table-action" type="button" data-admin-transfer-primary="${escapeHtml(user.id)}">Make primary</button>` : ""}
          ${user.canDelete ? `<button class="button secondary table-action" type="button" data-admin-delete-user="${escapeHtml(user.id)}">Delete</button>` : `<button class="button secondary table-action" type="button" disabled title="${escapeHtml(user.deletionBlockedReason || "This account cannot be deleted.")}">Protected</button>`}
        </td>` : ""}
      </tr>`).join("");

    if (canResetPasswords) {
      tbody.querySelectorAll("[data-admin-change-password]").forEach((button) => {
        button.addEventListener("click", () => changeAdminUserPassword(button.dataset.adminChangePassword));
      });
      tbody.querySelectorAll("[data-admin-delete-user]").forEach((button) => {
        button.addEventListener("click", () => deleteAdminUser(button.dataset.adminDeleteUser));
      });
      tbody.querySelectorAll("[data-admin-transfer-primary]").forEach((button) => {
        button.addEventListener("click", () => transferPrimaryAdmin(button.dataset.adminTransferPrimary));
      });
      tbody.querySelectorAll("[data-admin-set-active]").forEach((button) => {
        button.addEventListener("click", () => setAdminUserActiveStatus(button.dataset.adminSetActive, button.dataset.adminNextActive === "true"));
      });
    }
  } catch (exception) {
    count.textContent = "Failed";
    tbody.innerHTML = `<tr><td colspan="${colspan}">Could not load users.</td></tr>`;
    error.textContent = getFriendlyWorkspaceError(exception);
    error.hidden = false;
  }
}

function syncAdminCreateUserLocation() {
  const role = document.getElementById("admin-user-role");
  const locationField = document.getElementById("admin-user-location-field");
  const location = document.getElementById("admin-user-location");
  const needsLocation = role?.value === "WarehouseClerk";

  if (locationField) locationField.hidden = !needsLocation;
  if (location) {
    location.disabled = !needsLocation;
    location.required = needsLocation;
    if (!needsLocation) location.value = "";
  }
}

function populateAdminCreateUserLocations(locations) {
  const location = document.getElementById("admin-user-location");
  if (!location) return;

  const selectedId = location.value;
  location.innerHTML = `<option value="">Select warehouse location</option>${locations
    .map((item) => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.name)}</option>`)
    .join("")}`;
  location.value = locations.some((item) => item.id === selectedId) ? selectedId : "";
}

async function createAdminUser(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const values = new FormData(form);
  const username = String(values.get("username") || "").trim();
  const fullName = String(values.get("fullName") || "").trim();
  const password = String(values.get("password") || "");
  const confirmPassword = String(values.get("confirmPassword") || "");
  const role = String(values.get("role") || "");
  const locationId = String(values.get("locationId") || "");

  if (!fullName || !username) {
    notice("Full name and username are required.", "error");
    (!fullName ? document.getElementById("admin-user-full-name") : document.getElementById("admin-user-username"))?.focus();
    return;
  }
  if (password.length < 8) {
    notice("Password must be at least 8 characters.", "error");
    document.getElementById("admin-user-password")?.focus();
    return;
  }
  if (password !== confirmPassword) {
    notice("Password confirmation does not match.", "error");
    document.getElementById("admin-user-confirm-password")?.focus();
    return;
  }
  if (role === "WarehouseClerk" && !locationId) {
    notice("Warehouse clerks must be assigned to a warehouse location.", "error");
    document.getElementById("admin-user-location")?.focus();
    return;
  }

  const submit = form.querySelector("button[type='submit']");
  await withMutationGuard("admin-create-user", submit, async () => {
    try {
      const user = await request("/api/v1/users", {
        method: "POST",
        body: JSON.stringify({
          username,
          fullName,
          password,
          role,
          locationId: role === "WarehouseClerk" ? locationId : null
        })
      });
      form.reset();
      syncAdminCreateUserLocation();
      notice(`Employee account created for ${user.fullName}.`, "success");
      await loadAdminUsers();
    } catch (exception) {
      notice(getFriendlyWorkspaceError(exception), "error");
    }
  });
}

async function createAdminLocation(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const values = new FormData(form);
  const name = String(values.get("name") || "").trim();
  const locationType = String(values.get("locationType") || "");
  if (!name) {
    notice("Warehouse name is required.", "error");
    document.getElementById("admin-location-name")?.focus();
    return;
  }

  const submit = form.querySelector("button[type='submit']");
  await withMutationGuard("admin-create-location", submit, async () => {
    try {
      const location = await request("/api/v1/inventory/locations", {
        method: "POST",
        body: JSON.stringify({ name, locationType })
      });
      form.reset();
      notice(`${location.name} was added as an active warehouse.`, "success");
      await loadAdminUsers();
    } catch (exception) {
      notice(getFriendlyWorkspaceError(exception), "error");
    }
  });
}

async function changeAdminUserPassword(userId) {
  const passwordInput = [...document.querySelectorAll("[data-admin-password]")]
    .find((input) => input.dataset.adminPassword === userId);
  const confirmInput = [...document.querySelectorAll("[data-admin-confirm-password]")]
    .find((input) => input.dataset.adminConfirmPassword === userId);
  const row = [...document.querySelectorAll("[data-admin-user-row]")]
    .find((item) => item.dataset.adminUserRow === userId);
  const username = row?.querySelector("strong")?.textContent || "user";
  const newPassword = passwordInput?.value || "";
  const confirmPassword = confirmInput?.value || "";

  if (newPassword.length < 8) {
    notice("Password must be at least 8 characters.", "error");
    passwordInput?.focus();
    return;
  }

  if (newPassword !== confirmPassword) {
    notice("Password confirmation does not match.", "error");
    confirmInput?.focus();
    return;
  }

  try {
    await request(`/api/v1/users/${encodeURIComponent(userId)}/password`, {
      method: "PATCH",
      body: JSON.stringify({ newPassword })
    });
    if (passwordInput) {
      passwordInput.value = "";
    }
    if (confirmInput) {
      confirmInput.value = "";
    }
    notice(`Password changed for ${username}. Active sessions were revoked.`, "success");
    await loadAdminUsers();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function renderListPattern(title, headers) {
  document.getElementById("view").innerHTML = `<section class="band"><h2>${escapeHtml(title)}</h2><p class="muted-text">No rows are available for this workspace yet.</p><div class="table-wrap"><table><thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead><tbody><tr>${headers.map(() => "<td>-</td>").join("")}</tr></tbody></table></div></section>`;
}

function renderForbidden() {
  document.getElementById("page-title").textContent = "Forbidden";
  document.getElementById("route-label").textContent = "Authorization";
  renderNav(getAuth());
  renderSession(getAuth());
  document.getElementById("view").innerHTML = `<section class="band"><h2>Access denied</h2><p>This session cannot open that workspace.</p></section>`;
}

async function logout() {
  try {
    await request("/api/v1/auth/logout", { method: "POST", body: JSON.stringify({}) });
  } finally {
    clearAuth();
    location.hash = "/login";
  }
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" })[character]);
}

function roleLabel(role) {
  return { CLevel: "C-Level", ERPAdmin: "ERP Admin", WarehouseClerk: "Warehouse Clerk" }[role] || role;
}

function referencePrefix(type) {
  const prefixes = {
    paymentlog: "PAY", paymentsublog: "PAY", cashrecord: "PAY", financialadjustment: "PAY",
    stocktake: "STK", operation: "OP", supplyshipment: "SUP", sku: "SKU", inventorybatch: "BAT",
    stockbalance: "STK", location: "LOC", user: "USR", merchant: "MER", representative: "REP",
    notification: "NTF", audit: "AUD", category: "CAT", product: "PRD", brand: "BRD"
  };

  if (operationsUiState.mode === "revise" && operationsUiState.operationId && operationsUiState.revisionFingerprint === canonicalOperationPayload(body)) {
    notice("No changes detected; operation was not revised.", "success");
    resetOperationEditorMode();
    return;
  }
  return prefixes[String(type || "").replace(/[^a-z]/gi, "").toLowerCase()] || "REF";
}

function canonicalOperationPayload(body) {
  const type = canonicalSystemValue(body.operationType);
  const lines = (body.lines || []).map((line) => {
    const entryMode = canonicalSystemValue(line.entryMode || "Packs");
    const quantity = entryMode === "Pieces" ? Number(line.pieceQuantity ?? line.packQuantity ?? 0) : Number(line.packQuantity ?? 0);
    const bonus = ["WholesaleSale", "RetailSale"].includes(type) && line.isBonus === true;
    return { skuId: line.skuId, section: type === "Change" ? canonicalSystemValue(line.section || "ChangeOut") : "Standard", entryMode, quantity, bonusQuantity: bonus ? quantity : 0, unitPrice: bonus ? 0 : Number(line.unitPrice || 0), lotNumber: String(line.lotNumber || "").trim() || null, expiryDate: line.expiryDate || null, notes: String(line.notes || "").trim() || null };
  }).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
  return JSON.stringify({ operationType: type, sourceLocationId: body.sourceLocationId || null, destinationLocationId: body.destinationLocationId || null, merchantId: body.merchantId || null, buyerName: body.merchantId ? null : String(body.buyerName || "").trim() || null, representativeId: body.representativeId || null, paymentMethod: canonicalSystemValue(body.paymentMethod || "") || null, buyerPhone: String(body.buyerPhone || "").trim() || null, notes: String(body.notes || "").trim() || null, receipt: body.receipt ? { supplierName: String(body.receipt.supplierName || "Supplier").trim() || "Supplier", invoiceNumber: String(body.receipt.invoiceNumber || "").trim() || null } : null, lines });
}

async function deleteAdminUser(userId) {
  const row = [...document.querySelectorAll("[data-admin-user-row]")]
    .find((item) => item.dataset.adminUserRow === userId);
  const username = row?.querySelector("strong")?.textContent || "this user";
  const fullName = row?.querySelector(".muted-text")?.textContent?.trim();
  const accountLabel = fullName ? `${username} (${fullName})` : username;
  if (!window.confirm(uiText(`Delete account ${accountLabel}? This cannot be undone.`))) return;

  try {
    await request(`/api/v1/users/${encodeURIComponent(userId)}`, { method: "DELETE" });
    notice(`Account ${accountLabel} deleted.`, "success");
    await loadAdminUsers();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function setAdminUserActiveStatus(userId, isActive) {
  const row = [...document.querySelectorAll("[data-admin-user-row]")]
    .find((item) => item.dataset.adminUserRow === userId);
  const username = row?.querySelector("strong")?.textContent || "this user";
  const action = isActive ? "reactivate" : "deactivate";
  if (!window.confirm(uiText(`${action[0].toUpperCase()}${action.slice(1)} account ${username}?`))) return;

  try {
    await request(`/api/v1/users/${encodeURIComponent(userId)}/${isActive ? "activate" : "deactivate"}`, { method: "PATCH", body: JSON.stringify({}) });
    notice(`${username} is now ${isActive ? "active" : "inactive"}.`, "success");
    await loadAdminUsers();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function transferPrimaryAdmin(userId) {
  const row = [...document.querySelectorAll("[data-admin-user-row]")]
    .find((item) => item.dataset.adminUserRow === userId);
  const username = row?.querySelector("strong")?.textContent || "this Administrator";
  const fullName = row?.querySelector(".muted-text")?.textContent?.trim();
  const accountLabel = fullName ? `${fullName} (${username})` : username;
  if (!window.confirm(uiText(`Make ${accountLabel} the primary Administrator? You will no longer be able to delete Administrator accounts.`))) return;

  try {
    await request(`/api/v1/users/${encodeURIComponent(userId)}/transfer-primary`, { method: "POST", body: JSON.stringify({}) });
    notice(`${accountLabel} is now the primary Administrator.`, "success");
    await loadAdminUsers();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function renderAudit() {
  auditPageState = { page: 1, pageSize: 50 };
  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: "Oversight",
      title: "Audit history",
      body: "Review successful system activity by person, time, section, and related record.",
      metrics: scenarioCard("Events", "Loading", "status-muted", "audit-count")
    })}
    <section class="band">
      <div class="section-head"><div><h2>System activity</h2><p class="muted-text">The trail remains available even when the original account or record has been removed.</p></div><button class="button secondary" id="audit-refresh" type="button">Refresh</button></div>
      <div class="form-grid audit-filters">
        <div class="field"><label for="audit-search">Find activity</label><input class="input" id="audit-search" placeholder="Person, record name, action, or saved value"></div>
        <div class="field"><label for="audit-section">Area</label><select class="select" id="audit-section"><option value="">All areas</option><option value="User">Employee accounts</option><option value="Product">Catalog</option><option value="Operation">Operations</option><option value="Payment">Payments</option><option value="SupplyShipment">Supply</option><option value="Stocktake">Stocktake</option><option value="ShopifyWebhookEvent">Online intake</option></select></div>
        <div class="field"><label for="audit-from">From</label><input class="input" id="audit-from" type="date"></div>
        <div class="field"><label for="audit-to">To</label><input class="input" id="audit-to" type="date"></div>
      </div>
      <div class="table-wrap"><table><thead><tr><th>When</th><th>Full name &amp; role</th><th>Activity</th><th>Record</th><th>Area</th><th></th></tr></thead><tbody id="audit-rows"><tr><td colspan="6">Loading audit history</td></tr></tbody></table></div>
      <div class="pagination-bar" id="audit-pagination"></div>
    </section>
    <section class="band" id="audit-detail"><h2>Event detail</h2><p class="muted-text">Select an event to inspect the recorded details.</p></section>`;

  document.getElementById("audit-refresh").addEventListener("click", () => loadAuditHistory());
  document.getElementById("audit-search").addEventListener("input", debounce(() => { auditPageState.page = 1; loadAuditHistory(); }, 300));
  ["audit-section", "audit-from", "audit-to"].forEach((id) => document.getElementById(id).addEventListener("change", () => { auditPageState.page = 1; loadAuditHistory(); }));
  await loadAuditHistory();
}

async function loadAuditHistory() {
  const rows = document.getElementById("audit-rows");
  const count = document.getElementById("audit-count");
  if (!rows || !count) return;
  const params = new URLSearchParams({ page: String(auditPageState.page), pageSize: String(auditPageState.pageSize) });
  const search = document.getElementById("audit-search")?.value.trim();
  const entityType = document.getElementById("audit-section")?.value;
  const from = document.getElementById("audit-from")?.value;
  const to = document.getElementById("audit-to")?.value;
  if (search) params.set("search", search);
  if (entityType) params.set("entityType", entityType);
  if (from) params.set("from", `${from}T00:00:00`);
  if (to) params.set("to", `${to}T23:59:59`);
  rows.innerHTML = `<tr><td colspan="6">Loading audit history</td></tr>`;
  try {
    const result = await request(`/api/v1/audit?${params}`);
    count.textContent = `${result.totalCount} event${result.totalCount === 1 ? "" : "s"}`;
    rows.innerHTML = result.items.length ? result.items.map((event) => `
      <tr><td>${escapeHtml(formatDateTime(event.happenedAt))}</td><td><strong>${escapeHtml(displaySafeText(event.actorName || "Historical actor unavailable", "USR"))}</strong><br><span class="muted-text">${escapeHtml(auditActorRole(event.actorType))}</span></td><td><strong>${escapeHtml(displaySafeText(event.summary || auditSummaryFallback(event), "AUD"))}</strong></td><td>${escapeHtml(displaySafeText(event.recordName || "Related record", referencePrefix(event.entityType)))}</td><td>${escapeHtml(auditSectionLabel(event.section))}</td><td><button class="button secondary table-action" type="button" data-audit-detail="${escapeHtml(event.id)}">View details</button><button class="button secondary table-action" type="button" data-audit-source="${escapeHtml(event.id)}">Open record</button></td></tr>`).join("") : `<tr><td colspan="6">No audit events match these filters.</td></tr>`;
    rows.querySelectorAll("[data-audit-detail]").forEach((button) => button.addEventListener("click", () => showAuditDetail(button.dataset.auditDetail)));
    rows.querySelectorAll("[data-audit-source]").forEach((button) => button.addEventListener("click", () => openAuditSource(button.dataset.auditSource)));
    renderAuditPagination(result);
  } catch (exception) {
    count.textContent = "Failed";
    rows.innerHTML = `<tr><td colspan="6">Could not load audit history.</td></tr>`;
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function renderAuditPagination(result) {
  const area = document.getElementById("audit-pagination");
  if (!area) return;
  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
  area.innerHTML = `<span>Page ${result.page} of ${totalPages}</span><div><button class="button secondary table-action" type="button" id="audit-previous" ${result.page <= 1 ? "disabled" : ""}>Previous</button><button class="button secondary table-action" type="button" id="audit-next" ${result.page >= totalPages ? "disabled" : ""}>Next</button></div>`;
  document.getElementById("audit-previous")?.addEventListener("click", () => { auditPageState.page -= 1; loadAuditHistory(); });
  document.getElementById("audit-next")?.addEventListener("click", () => { auditPageState.page += 1; loadAuditHistory(); });
}

async function showAuditDetail(id) {
  const detail = document.getElementById("audit-detail");
  if (!detail) return;
  detail.innerHTML = `<h2>Event detail</h2><p>Loading event</p>`;
  try {
    const event = await request(`/api/v1/audit/${encodeURIComponent(id)}`);
    const changes = Array.isArray(event.changes) ? event.changes : [];
    const savedValues = changes.length ? `<div class="audit-change-list">${changes.map((change) => `<article class="audit-change"><strong>${escapeHtml(change.field)}</strong>${change.before ? `<span>Was: ${escapeHtml(displaySafeText(change.before, "AUD"))}</span>` : ""}<span>${change.before ? "Now" : "Saved"}: ${escapeHtml(displaySafeText(change.after || "Cleared", "AUD"))}</span></article>`).join("")}</div>` : `<p class="muted-text audit-empty-values">No individual field values were saved for this event.</p>`;
    detail.innerHTML = `<div class="section-head"><div><p class="eyebrow">Recorded activity</p><h2>${escapeHtml(event.summary || auditSummaryFallback(event))}</h2><p class="muted-text">${escapeHtml(formatDateTime(event.happenedAt))} by ${escapeHtml(event.actorName)}</p></div><button class="button secondary" type="button" id="audit-open-detail-source">Open related record</button></div><div class="detail-grid"><div><span>Record</span><strong>${escapeHtml(event.recordName || "Related record")}</strong></div><div><span>Performed by</span><strong>${escapeHtml(event.actorName || "Historical actor unavailable")} · ${escapeHtml(auditActorRole(event.actorType))}</strong></div><div><span>Area</span><strong>${escapeHtml(auditSectionLabel(event.section))}</strong></div><div><span>Time</span><strong>${escapeHtml(formatDateTime(event.happenedAt))}</strong></div></div><section class="audit-saved-values"><h3>Saved values</h3><p class="muted-text">These are the values recorded when the activity was completed.</p>${savedValues}</section>`;
    document.getElementById("audit-open-detail-source")?.addEventListener("click", () => openAuditSource(event.id));
  } catch (exception) {
    detail.innerHTML = `<h2>Event detail</h2><p class="form-error">${escapeHtml(getFriendlyWorkspaceError(exception))}</p>`;
  }
}

function auditSummaryFallback(event) {
  return `${String(event.action || "Changed").replace(/([a-z])([A-Z])/g, "$1 $2")} ${String(event.entityType || "record").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase()}.`;
}

function auditSectionLabel(section) {
  const labels = { admin: "Administration", catalog: "Catalog", crm: "CRM", inventory: "Inventory", operations: "Operations", payments: "Payments", supply: "Supply", stocktakes: "Stocktake", notifications: "Notifications", integrations: "Online intake", reports: "Reports", dashboard: "System" };
  return labels[String(section || "").toLowerCase()] || "System";
}

function auditActorRole(actorType) {
  const value = String(actorType || "").trim();
  return value && value !== "User" ? value : "Role not recorded";
}

async function openAuditSource(auditEventId) {
  try {
    const destination = await request(`/api/v1/audit/${encodeURIComponent(auditEventId)}/navigation-reference`);
    location.hash = `${destination.route}?ref=${encodeURIComponent(destination.navigationReference)}`;
  } catch {
    notice("The related record is unavailable or no longer permitted.", "warning");
  }
}

async function renderShopifyIntegration() {
  shopifyIntegrationPageState = { page: 1, pageSize: 25 };
  shopifySkuPageState = { page: 1, pageSize: 50 };
  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: "Online intake",
      title: "Shopify intake desk",
      body: "Review online orders, repair mappings, and resolve exceptions before they reach warehouse fulfillment.",
      metrics: `${scenarioCard("Receiver", "Checking", "status-muted", "shopify-receiver-state")}${scenarioCard("Queue", "Loading", "status-muted", "shopify-queue-count")}${scenarioCard("Payload access", "Protected", "status-ok")}`
    })}
    <section class="integration-command-band">
      <div class="integration-command-copy"><span class="eyebrow">Delivery queue</span><h2>Protect the commercial record. Allocate stock only after review.</h2><p>Webhook content is never shown here. Temporary legacy-path deliveries are explicitly marked until you upgrade to signed webhooks.</p></div>
      <button id="shopify-refresh" class="button primary" type="button">Refresh intake</button>
    </section>
    <section class="band">
      <div class="section-head tight-head"><div><h2>Integration events</h2><p>Queued events process automatically. Exceptions require a deliberate retry or resolution note.</p></div><select id="shopify-event-status" class="select compact-select"><option value="">All states</option><option value="Queued">Queued</option><option value="Processing">Processing</option><option value="Retrying">Retrying</option><option value="RequiresAttention">Needs review</option><option value="Resolved">Resolved</option><option value="Succeeded">Succeeded</option><option value="Imported">Imported</option></select></div>
      <div id="shopify-event-list" class="integration-event-list">Loading integration events…</div>
    </section>
    <section class="band">
      <div class="section-head tight-head"><div><h2>ERP SKUs for Shopify</h2><p>Copy the ERP SKU into each Shopify variant. Orders match SKU only; each quantity is an individual lens piece.</p></div></div>
      <div class="integration-mapping-form">
        <div class="field"><label for="shopify-sku-search">Find SKU or product</label><input id="shopify-sku-search" class="input" autocomplete="off" placeholder="SKU or product name"></div>
        <div class="field"><label for="shopify-sku-product">Product</label><select id="shopify-sku-product" class="select"><option value="">All catalog products</option></select></div>
        <div class="field"><label for="shopify-sku-wear-cycle">Wear cycle</label><select id="shopify-sku-wear-cycle" class="select"><option value="">All wear cycles</option><option value="Daily">Daily</option><option value="Monthly">Monthly</option><option value="Annual">Annual</option></select></div>
        <div class="field"><label for="shopify-sku-status">Readiness</label><select id="shopify-sku-status" class="select"><option value="">All active SKUs</option><option value="Ready">Ready to publish</option><option value="NeedsWearCycle">Set Lens cycle</option><option value="PieceSaleDisabled">Piece sale disabled</option><option value="UnsupportedProduct">Unsupported product</option></select></div>
        <button id="shopify-sku-search-button" class="button secondary" type="button">Check catalog</button>
      </div>
      <div id="shopify-sku-readiness" class="table-wrap compact-table">Loading ERP SKU readiness…</div>
      <div class="pagination-bar" id="shopify-sku-pagination" hidden>
        <label class="muted-text" for="shopify-sku-page-size">Rows per page</label>
        <select id="shopify-sku-page-size" class="select compact-select" aria-label="ERP SKUs per page">
          <option value="50">50</option>
          <option value="100">100</option>
        </select>
        <span class="muted-text" id="shopify-sku-page-label">Showing 0 ERP SKUs</span>
        <button class="button secondary table-action" type="button" id="shopify-sku-prev">Previous</button>
        <button class="button secondary table-action" type="button" id="shopify-sku-next">Next</button>
      </div>
    </section>`;
  document.getElementById("shopify-refresh").addEventListener("click", () => loadShopifyIntegration());
  document.getElementById("shopify-event-status").addEventListener("change", () => loadShopifyEvents());
  document.getElementById("shopify-sku-search-button").addEventListener("click", () => loadShopifySkuReadiness(1));
  document.getElementById("shopify-sku-search").addEventListener("keydown", (event) => {
    if (event.key === "Enter") loadShopifySkuReadiness(1);
  });
  document.getElementById("shopify-sku-product").addEventListener("change", () => loadShopifySkuReadiness(1));
  document.getElementById("shopify-sku-wear-cycle").addEventListener("change", () => loadShopifySkuReadiness(1));
  document.getElementById("shopify-sku-status").addEventListener("change", () => loadShopifySkuReadiness(1));
  document.getElementById("shopify-sku-page-size").addEventListener("change", (event) => {
    shopifySkuPageState.pageSize = Number(event.target.value) || 50;
    loadShopifySkuReadiness(1);
  });
  document.getElementById("shopify-sku-prev").addEventListener("click", () => loadShopifySkuReadiness(Math.max(1, shopifySkuPageState.page - 1)));
  document.getElementById("shopify-sku-next").addEventListener("click", () => loadShopifySkuReadiness(shopifySkuPageState.page + 1));
  await loadShopifyIntegration();
}

async function loadShopifyIntegration() {
  const receiver = document.getElementById("shopify-receiver-state");
  try {
    const status = await request("/api/v1/integrations/shopify/status");
    receiver.textContent = status.isConfigured ? "Signed receiver ready" : (status.isLegacyWebhookConfigured ? "Temporary legacy receiver" : "Configuration required");
    receiver.className = `status-pill ${status.isConfigured ? "status-ok" : "status-warn"}`;
  } catch (exception) {
    receiver.textContent = "Unavailable";
    receiver.className = "status-pill status-warn";
  }
  await Promise.all([loadShopifyEvents(), loadShopifySkuProducts(), loadShopifySkuReadiness()]);
}

async function loadShopifyEvents() {
  const list = document.getElementById("shopify-event-list");
  const count = document.getElementById("shopify-queue-count");
  if (!list) return;
  const status = document.getElementById("shopify-event-status")?.value || "";
  const params = new URLSearchParams({ page: "1", pageSize: String(shopifyIntegrationPageState.pageSize) });
  if (status) params.set("status", status);
  try {
    const result = await request(`/api/v1/integrations/shopify/events?${params}`);
    count.textContent = `${result.totalCount} events`;
    list.innerHTML = result.items.length === 0 ? `<div class="empty-state">No Shopify events match this view.</div>` : result.items.map(renderShopifyEvent).join("");
    list.querySelectorAll("[data-shopify-retry]").forEach((button) => button.addEventListener("click", () => retryShopifyEvent(button.dataset.shopifyRetry)));
    list.querySelectorAll("[data-shopify-resolve]").forEach((button) => button.addEventListener("click", () => resolveShopifyEvent(button.dataset.shopifyResolve)));
  } catch (exception) {
    count.textContent = "Unavailable";
    list.innerHTML = `<div class="empty-state">${escapeHtml(getFriendlyWorkspaceError(exception))}</div>`;
  }
}

function renderShopifyEvent(event) {
  const statusClass = event.status === "RequiresAttention" ? "status-warn" : (event.status === "Imported" || event.status === "Succeeded" ? "status-ok" : "status-muted");
  const canManage = ["Admin", "ERPAdmin"].includes(getAuth()?.user?.role) || (getAuth()?.user?.role === "WarehouseClerk" && getAuth()?.user?.locationType === "Online");
  const actions = canManage && event.status === "RequiresAttention"
    ? `<button class="button secondary table-action" type="button" data-shopify-retry="${escapeHtml(event.id)}" ${event.payloadAvailable ? "" : "disabled"}>Retry</button><button class="button secondary table-action" type="button" data-shopify-resolve="${escapeHtml(event.id)}">Resolve</button>`
    : "";
  const trust = event.verificationMode === "Hmac" ? "Signed HMAC" : "Temporary legacy path";
  return `<article class="integration-event-card"><div class="integration-event-main"><div><div class="notification-title-row"><span class="status-pill ${statusClass}">${escapeHtml(event.status)}</span><strong>${escapeHtml(event.topic)}</strong><span class="muted-text">${escapeHtml(formatDateTime(event.receivedAt))}</span></div><p>${escapeHtml(displaySafeText(event.detail || "Delivery accepted for processing."))}</p></div><div class="integration-event-actions">${event.operationId ? `<a class="button secondary table-action" href="#/operations">Operation ${escapeHtml(shortId(event.operationId, "OP"))}</a>` : ""}${actions}</div></div><dl class="integration-event-facts"><div><dt>Trust</dt><dd>${escapeHtml(trust)}</dd></div><div><dt>Order</dt><dd>${escapeHtml(event.shopifyOrderId || "Not parsed")}</dd></div><div><dt>Store</dt><dd>${escapeHtml(event.shopDomain)}</dd></div><div><dt>Attempts</dt><dd>${escapeHtml(event.attemptCount)}</dd></div><div><dt>Payload</dt><dd>${event.payloadAvailable ? "Retained securely" : "Retention expired"}</dd></div>${event.resolutionNote ? `<div><dt>Resolution</dt><dd>${escapeHtml(displaySafeText(event.resolutionNote))}</dd></div>` : ""}</dl></article>`;
}

async function retryShopifyEvent(id) {
  try {
    await request(`/api/v1/integrations/shopify/events/${id}/retry`, { method: "POST" });
    notice("Shopify event queued for retry.", "success");
    await loadShopifyEvents();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function resolveShopifyEvent(id) {
  const note = window.prompt(uiText("Resolution note"));
  if (!note?.trim()) return;
  try {
    await request(`/api/v1/integrations/shopify/events/${id}/resolve`, { method: "POST", body: JSON.stringify({ note: note.trim() }) });
    notice("Shopify event resolved.", "success");
    await loadShopifyEvents();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function loadShopifySkuProducts() {
  const select = document.getElementById("shopify-sku-product");
  if (!select) return;

  const selected = select.value;
  try {
    const products = await request("/api/v1/integrations/shopify/sku-readiness/products");
    select.innerHTML = `<option value="">All catalog products</option>${products.map((product) => `<option value="${escapeHtml(product.id)}">${escapeHtml(product.name)}</option>`).join("")}`;
    select.value = products.some((product) => product.id === selected) ? selected : "";
  } catch {
    select.innerHTML = `<option value="">Product list unavailable</option>`;
  }
}

async function loadShopifySkuReadiness(page = shopifySkuPageState.page || 1) {
  const list = document.getElementById("shopify-sku-readiness");
  const pagination = document.getElementById("shopify-sku-pagination");
  const pageLabel = document.getElementById("shopify-sku-page-label");
  const previous = document.getElementById("shopify-sku-prev");
  const next = document.getElementById("shopify-sku-next");
  if (!list) return;
  const requestedPage = Math.max(1, Number(page) || 1);
  try {
    const search = document.getElementById("shopify-sku-search")?.value.trim() || "";
    const productId = document.getElementById("shopify-sku-product")?.value || "";
    const wearCycle = document.getElementById("shopify-sku-wear-cycle")?.value || "";
    const status = document.getElementById("shopify-sku-status")?.value || "";
    const query = new URLSearchParams({ page: String(requestedPage), pageSize: String(shopifySkuPageState.pageSize || 50) });
    if (search) query.set("search", search);
    if (productId) query.set("productId", productId);
    if (wearCycle) query.set("wearCycle", wearCycle);
    if (status) query.set("status", status);
    const result = await request(`/api/v1/integrations/shopify/sku-readiness?${query}`);
    const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
    if (result.items.length === 0 && result.totalCount > 0 && requestedPage > totalPages) {
      shopifySkuPageState.page = totalPages;
      await loadShopifySkuReadiness(totalPages);
      return;
    }
    shopifySkuPageState = { page: Math.min(result.page, totalPages), pageSize: result.pageSize };
    list.innerHTML = `<table><thead><tr><th>ERP SKU</th><th>Product / attributes</th><th>Wear cycle</th><th>Pieces per pack</th><th>Sell mode</th><th>Readiness</th><th></th></tr></thead><tbody>${result.items.length === 0 ? `<tr><td colspan="7">No active ERP SKUs match this view.</td></tr>` : result.items.map((sku) => `<tr><td><strong>${escapeHtml(sku.skuCode)}</strong></td><td>${escapeHtml(sku.productName)}<div class="muted-cell">${escapeHtml([formatPower(sku), sku.colorName, sku.size].filter(Boolean).join(" / ") || "No variant attributes")}</div></td><td>${renderWearCycle(sku.wearCycle, sku.wearDuration)}</td><td>${escapeHtml(sku.piecesPerPack || "-")}</td><td>${escapeHtml(sku.sellMode || "Not set")}</td><td>${renderShopifySkuReadiness(sku.status)}</td><td><button class="button secondary table-action" type="button" data-copy-shopify-sku="${escapeHtml(sku.skuCode)}">Copy SKU</button></td></tr>`).join("")}</tbody></table>`;
    if (pagination && pageLabel && previous && next) {
      const first = result.totalCount === 0 ? 0 : ((shopifySkuPageState.page - 1) * result.pageSize) + 1;
      const last = Math.min(shopifySkuPageState.page * result.pageSize, result.totalCount);
      pagination.hidden = result.totalCount <= result.pageSize;
      pageLabel.textContent = `Showing ${first}–${last} of ${result.totalCount} ERP SKUs · Page ${shopifySkuPageState.page} of ${totalPages}`;
      previous.disabled = shopifySkuPageState.page <= 1;
      next.disabled = shopifySkuPageState.page >= totalPages;
    }
    list.querySelectorAll("[data-copy-shopify-sku]").forEach((button) => button.addEventListener("click", () => copyShopifySku(button.dataset.copyShopifySku)));
  } catch (exception) {
    list.innerHTML = `<div class="empty-state">${escapeHtml(getFriendlyWorkspaceError(exception))}</div>`;
    if (pagination) pagination.hidden = true;
  }
}

function renderShopifySkuReadiness(status) {
  if (status === "Ready") return `<span class="status-pill status-ok">Ready</span>`;
  if (status === "NeedsWearCycle") return `<span class="status-pill status-warn">Set Lens cycle</span>`;
  if (status === "PieceSaleDisabled") return `<span class="status-pill status-warn">Enable piece sales</span>`;
  return `<span class="status-pill status-muted">Lens products only</span>`;
}

async function copyShopifySku(sku) {
  try {
    await navigator.clipboard.writeText(sku);
    notice("ERP SKU copied. Paste it into the Shopify variant SKU field.", "success");
  } catch {
    notice("Could not copy the SKU. Copy it manually from the table.", "error");
  }
}

function formatPackHint(product) {
  return product.piecesPerPack ? `${escapeHtml(product.sellMode || "Pack")} / ${escapeHtml(product.piecesPerPack)} pcs` : escapeHtml(product.sellMode || "-");
}

function formatPower(sku) {
  return sku.powerValue === null || sku.powerValue === undefined ? "-" : `${sku.powerSign || ""}${sku.powerValue}`;
}

function formatDateTime(value) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat(currentLanguage === "ar" ? "ar-EG-u-nu-latn" : "en-US", {
      dateStyle: "medium",
      timeStyle: "short"
    }).format(date);
}

function formatMoney(value) {
  return new Intl.NumberFormat(currentLanguage === "ar" ? "ar-EG-u-nu-latn" : "en-US", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  }).format(Number(value || 0));
}

function getFriendlyLoginError(exception) {
  const message = exception instanceof Error ? exception.message : "";
  if (message.includes("Failed to fetch")) {
    return "Cannot reach the API. Check the API base URL and whether the host is running.";
  }
  if (message.includes("401") || message.includes("Unauthorized")) {
    return "Username or password is incorrect.";
  }
  if (message.includes("28P01")) {
    return "The API cannot connect to PostgreSQL. Check the database connection and restart the backend if needed.";
  }
  return "Sign in failed. Check the account credentials and try again.";
}

function getFriendlyApiError(exception) {
  const status = exception?.status;
  if (status === 401) {
    return "Session expired. Sign in again.";
  }
  if (status === 403) {
    return "You do not have access to this catalog action.";
  }
  return "Could not load catalog data.";
}

function getFriendlyCatalogWriteError(exception) {
  const message = exception instanceof Error ? exception.message : "";
  if (message.includes("errors")) {
    try {
      const body = JSON.parse(message);
      return Object.values(body.errors || {}).flat().join(" ") || "Check the catalog form values.";
    } catch {
      return "Check the catalog form values.";
    }
  }
  if (exception?.status === 409 || message.includes("Conflict")) {
    return "That SKU code already exists.";
  }
  if (exception?.status === 403) {
    return "You do not have permission to change catalog data.";
  }
  return "Catalog change failed.";
}

function getFriendlyInventoryError(exception) {
  const problem = parseProblemDetails(exception);
  if (problem) {
    return problem;
  }
  const status = exception?.status;
  if (status === 401) {
    return "Session expired. Sign in again.";
  }
  if (status === 403) {
    return "You do not have access to this inventory action.";
  }
  if (status === 400) {
    return "Check the inventory filters or target packs.";
  }
  return "Could not load inventory data.";
}

function getFriendlyWorkspaceError(exception) {
  const problem = parseProblemDetails(exception);
  if (problem) {
    return problem;
  }
  if (exception?.status === 401) {
    return "Session expired. Sign in again.";
  }
  if (exception?.status === 403) {
    return "This account does not have permission for that action.";
  }
  if (exception?.status === 400) {
    return "Check the request values.";
  }
  return "The workspace request failed.";
}

function parseProblemDetails(exception) {
  const message = exception instanceof Error ? exception.message : "";
  if (!message || !(message.includes("{") || message.includes("["))) {
    return "";
  }

  try {
    const body = JSON.parse(message);
    const errors = Object.values(body.errors || {}).flat().filter(Boolean);
    if (errors.length > 0) {
      return displaySafeText(errors.join(" "));
    }
    return displaySafeText(body.detail || body.message || body.title || "");
  } catch {
    return "";
  }
}

function debounce(callback, delay) {
  let timeout;
  return (...args) => {
    window.clearTimeout(timeout);
    timeout = window.setTimeout(() => callback(...args), delay);
  };
}

