import {
  canonicalSelectValue as readCanonicalSelectValue,
  canonicalSystemValue as readCanonicalSystemValue,
  contextualReference,
  findVisibleUuidLeaks,
  sanitizeVisibleText
} from "./localization.js?v=20260913-perf1";
import { getLanguage as getFoundationLanguage, setLanguage as setFoundationLanguage, t as foundationT } from "./i18n/index.js?v=20260924-i18n-final";
import enMessages from "./i18n/en.js?v=20260924-i18n-final";

// Keep API calls same-origin. Deployment-specific routing belongs to the reverse proxy.
let apiBase = "";
let activeAuth = null;
let activeRouteController = new AbortController();
const pendingGetRequests = new Map();
const referenceResponseCache = new Map();
const heavyRequestQueue = [];
let activeHeavyRequests = 0;
const maxHeavyRequests = 2;
// Remove values written by the former client-side token/API configuration.
localStorage.removeItem("lensee.auth");
localStorage.removeItem("lensee.apiBase");
const ngrokSkipHeader = "ngrok-skip-browser-warning";
const requestMarkerHeader = "X-Lensee-Request";
const mutationEventName = "lensee:data-mutated";
const authEventName = "lensee:auth-changed";

let catalogCategories = [];
let categoryTree = [];
let catalogBrands = [];
let selectedProductId = null;
let inventoryLocations = [];
let inventorySkuOptions = [];
let selectedInventoryLocationId = "";
let inventoryPageState = { balances: 1, batches: 1, transactions: 1, blocked: 1, replenishment: 1 };
let inventoryRefreshGeneration = 0;
let inventoryPanelObserver = null;
const loadedInventoryPanels = new Set();
let operationLocations = [];
let operationSkuOptions = [];
let operationProductOptions = [];
let operationAvailableSkuIds = null;
let operationSkuLoadPromise = null;
const loadedOperationProductIds = new Set();
const skuSearchRequests = new WeakMap();
const operationStockOptionRequests = new WeakMap();
const operationBatchOptionCache = new Map();
let operationMerchantOptions = [];
let operationEditorLines = [];
let operationEditorLineById = new Map();
let operationEditorPage = 1;
const operationEditorPageSize = 50;
let operationEditorRendering = false;
const operationSearchTimers = new WeakMap();
let routeRefreshCooldownUntil = 0;
let selectedSupplyShipmentId = null;
let supplyShipments = [];
let supplyCurrentDetail = null;
let supplySkuLoadPromise = null;
let supplySkuSearchIndex = [];
let supplyEditorLines = [];
let supplyEditorLineById = new Map();
let supplyEditorPage = 1;
const supplyEditorPageSize = 50;
let supplyEditorStats = { productTotal: 0, incompletePrices: 0, invalidPrices: 0 };
let supplyEditorStatsDirty = true;
let supplyListPage = 1;
let supplyDetailLinePage = 1;
let operationsUiState = {
  mode: "create",
  operationId: null,
  concurrencyVersion: null,
  operationType: "WarehouseTransfer",
  revisionReason: "",
  revisionFingerprint: null,
  openDetailIds: []
};
let operationListPage = 1;
let paymentMerchants = [];
let paymentAccountants = [];
let paymentHistoryRows = [];
let paymentPageState = { merchant: 1, other: 1, history: 1, audit: 1 };
const loadedPaymentPanels = new Set();
let reportOperationRows = [];
let reportPaymentRows = [];
let reportMerchantRows = [];
let reportStocktakeRows = [];
let reportSupplyRows = [];
let reportCatalogEntries = [];
let reportPageState = { stock: 1, operations: 1, payments: 1, supply: 1, merchantBalances: 1 };
let selectedMerchantId = null;
let auditPageState = { page: 1, pageSize: 50 };
let notificationPageState = { page: 1, pageSize: 10 };
let notificationLoadGeneration = 0;
let shopifyIntegrationPageState = { page: 1, pageSize: 25 };
let shopifySkuPageState = { page: 1, pageSize: 50 };
let activeRefreshTimer = null;
let activeRefreshController = null;
let activeRefreshInFlight = false;
let routeRenderGeneration = 0;
let notificationBadgeInFlight = false;
let refreshSessionPromise = null;
let noticeSequence = 0;
const mutationLocks = new Set();

const syncChannel = "BroadcastChannel" in window ? new BroadcastChannel("lensee-sync") : null;
const syncStorageKey = "lensee.sync";
const refreshLockName = "lensee-auth-refresh";
const refreshLockStorageKey = "lensee.refresh.lock";
const refreshLockLeaseMs = 30000;
const refreshLockWaitMs = 35000;

const languageKey = "lensee.language";
let currentLanguage = getFoundationLanguage();
let applyingLanguage = false;

const arabicTranslations = Object.freeze({
  "Sign In": "تسجيل الدخول",
  "Identity": "الهوية وتسجيل الدخول",
  "Overview": "نظرة عامة",
  "Dashboard": "لوحة التحكم",
  "Catalog": "الكتالوج",
  "Inventory": "المخزون",
  "Supply": "التوريد",
  "CRM": "عميل و شركة",
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
  "Notice": "إشعار",
  "Success": "تم بنجاح",
  "Warning": "تحذير",
  "Error": "خطأ",
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
  "Loading details...": "جارٍ تحميل التفاصيل...",
  "No results": "لا توجد نتائج",
  "Name": "الاسم",
  "Business name": "اسم النشاط",
  "Contact person": "مسؤول التواصل",
  "Phone": "رقم الهاتف",
  "Email": "البريد الإلكتروني",
  "Address": "العنوان",
  "Business type": "نوع النشاط",
  "Merchant": "تاجر",
  "Merchants": "التجار",
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
  "Power": "مقاس العدسة",
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
  "Inventory receipt": "استلام مخزون",
  "Warehouse transfer": "تحويل مخزون",
  "Wholesale sale": "بيع جملة",
  "Retail/online sale": "بيع قطاعي / أونلاين",
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
  "Payment assigned": "تم إسناد الدفع",
  "Payment log initialized": "تم تهيئة سجل الدفع",
  "Payment reassigned": "تم إعادة إسناد الدفع",
  "Collection reassigned": "تم إعادة إسناد التحصيل",
  "Payment sub-log submitted": "تم إرسال سجل الدفع الفرعي للاعتماد",
  "Payment sub-log approved": "تم اعتماد سجل الدفع الفرعي",
  "Payment sub-log rejected": "تم رفض سجل الدفع الفرعي",
  "Cash receipt submitted": "تم إرسال إيصال النقدية للاعتماد",
  "Financial adjustment requested": "تم طلب التسوية المالية",
  "Financial adjustment approved": "تم اعتماد التسوية المالية",
  "Financial adjustment rejected": "تم رفض التسوية المالية",
  "Cash receipt rejected": "تم رفض إيصال النقدية",
  "Merchant collection drafted": "تم إعداد تحصيل التاجر كمسودة",
  "Merchant collection submitted": "تم إرسال تحصيل التاجر للاعتماد",
  "Merchant collection approved": "تم اعتماد تحصيل التاجر",
  "Merchant collection rejected": "تم رفض تحصيل التاجر",
  "Collection drafted": "تم إعداد التحصيل كمسودة",
  "Collection approved": "تم اعتماد التحصيل",
  "Collection rejected": "تم رفض التحصيل",
  "Refund paid": "تم دفع الاسترداد",
  "Reconciliation completed": "اكتملت المطابقة",
  "Rejected": "مرفوض",
  "Approved": "معتمد",
  "Cash hand to hand": "نقدي مباشر",
  "Cash transaction": "تحويل أو إيداع نقدي",
  "Merchant account": "حساب التاجر",
  "MerchantAccount": "حساب التاجر",
  "Account details": "تفاصيل الحساب",
  "Account health": "حالة الحساب",
  "Account notes": "ملاحظات الحساب",
  "Excellent": "ممتاز",
  "Good": "جيد",
  "Fair": "متوسط",
  "Weak": "ضعيف",
  "Critical": "حرج",
  "Not rated": "غير مصنف",
  "Provisional grade": "تصنيف مبدئي",
  "Added": "مضاف",
  "Balance": "الرصيد",
  "Choose a merchant to see their account.": "اختر تاجرًا لعرض حسابه.",
  "Every confirmed amount added to or removed from this merchant account.": "كل مبلغ مؤكد أضيف إلى حساب التاجر أو خُصم منه.",
  "How": "الطريقة",
  "Latest activity": "آخر حركة",
  "Money received": "المبلغ المستلم",
  "Money refunded": "المبلغ المسترد",
  "Money waiting for approval": "مبلغ بانتظار الاعتماد",
  "No confirmed account activity yet.": "لا توجد حركة مؤكدة على الحساب حتى الآن.",
  "Recent activity": "آخر الحركات",
  "Reduced": "مخفض",
  "Refund waiting to be paid": "استرداد بانتظار الدفع",
  "Returns and reductions": "المرتجعات والتخفيضات",
  "See what the merchant owes, what we owe back, and the latest account activity.": "اعرف ما يستحق على التاجر وما يستحق له وآخر حركة على الحساب.",
  "Show a merchant account to view activity.": "اعرض حساب تاجر لعرض الحركات.",
  "Show account": "عرض الحساب",
  "Search merchants": "ابحث عن تاجر",
  "Waiting for accountant review": "بانتظار مراجعة المحاسب",
  "Waiting for Admin approval": "بانتظار اعتماد الإدارة",
  "Waiting for admin review": "بانتظار مراجعة المدير",
  "Show an account to view money waiting for approval.": "اعرض حسابًا لرؤية المبالغ بانتظار الاعتماد.",
  "What happened": "ما الذي حدث",
  "Installment": "تقسيط",
  "Payments and remaining": "المدفوعات والمتبقي",
  "Merchant account payments": "مدفوعات حسابات التجار",
  "Other payments": "مدفوعات أخرى",
  "Account collections and merchant operation balances.": "تحصيلات الحساب وأرصدة عمليات التجار.",
  "Direct retail collections without a registered merchant account.": "تحصيلات البيع المباشر دون حساب تاجر مسجل.",
  "Direct operation payments, receipts, and review stages.": "مدفوعات العمليات المباشرة والإيصالات ومراحل المراجعة.",
  "Other payment history": "سجل المدفوعات الأخرى",
  "Approval inbox": "صندوق اعتماد التحصيلات",
  "Assigned collection work from merchant accounts and direct operations.": "أعمال التحصيل المسندة من حسابات التجار والعمليات المباشرة.",
  "Refresh inbox": "تحديث صندوق الاعتماد",
  "Loading approval work": "جارٍ تحميل أعمال الاعتماد",
  "Source": "المصدر",
  "Opening balance": "الرصيد الافتتاحي",
  "Closing balance": "الرصيد الختامي",
  "Period amount due": "المبلغ المستحق للفترة",
  "Period merchant credit": "الرصيد الدائن للتاجر للفترة",
  "Merchant account payments are kept with the merchant statement.": "مدفوعات حساب التاجر تظهر داخل كشف حساب التاجر.",
  "Other payments stay linked to their operation and receipt.": "المدفوعات الأخرى تظل مرتبطة بالعملية والإيصال.",
  "Amount": "المبلغ",
  "Assign": "إسناد",
  "Approve": "اعتماد",
  "Reject": "رفض",
  "Load remaining": "تحميل المتبقي",
  "Reports and exports": "التقارير والتصدير",
  "Download": "تنزيل",
  "Export log": "سجل التصدير",
  "Analytical reports": "التقارير التحليلية",
  "Assigned by server": "يحدده الخادم",
  "Bilingual": "ثنائي اللغة",
  "Arabic + English": "العربية + الإنجليزية",
  "Current authorized scope": "النطاق المصرح الحالي",
  "Document language": "لغة المستند",
  "Export docket": "بيان التصدير",
  "EXPORT DOCKET": "بيان التصدير",
  "export formats": "صيغ التصدير",
  "Filename": "اسم الملف",
  "Format": "الصيغة",
  "From date": "من تاريخ",
  "Inventory posted": "تم ترحيل المخزون",
  "Language": "اللغة",
  "Live previews with catalog-approved exports.": "معاينات مباشرة مع صيغ تصدير معتمدة من الدليل.",
  "No export selected": "لم يتم اختيار تصدير",
  "Official documents": "المستندات الرسمية",
  "OFFICIAL REPORT REGISTER": "سجل التقارير الرسمي",
  "One controlled workspace for analytical reports and official business documents.": "مساحة عمل موحدة للتقارير التحليلية ومستندات الأعمال الرسمية.",
  "Operation type": "نوع العملية",
  "PDF · XLSX · CSV": "PDF · XLSX · CSV",
  "Ready for export.": "جاهز للتصدير.",
  "Language updated. Ready for export.": "تم تحديث اللغة. جاهز للتصدير.",
  "Preparing the authorized export…": "جارٍ تجهيز التصدير المصرح…",
  "Export completed and downloaded.": "اكتمل التصدير وتم تنزيله.",
  "Rendering the official document…": "جارٍ إعداد المستند الرسمي…",
  "Official document downloaded.": "تم تنزيل المستند الرسمي.",
  "Refresh register": "تحديث السجل",
  "Report filters": "مرشحات التقرير",
  "Retail sale": "بيع قطاعي",
  "Search a business record, then export its controlled document.": "ابحث عن سجل أعمال ثم صدّر مستنده المعتمد.",
  "Supply status": "حالة التوريد",
  "To date": "إلى تاريخ",
  "Payment": "المدفوعات",
  "Merchant remaining": "المتبقي على التجار",
  "Stocktake sessions": "جلسات الجرد",
  "No users found.": "لم يتم العثور على مستخدمين.",
  "No merchants yet.": "لا يوجد تجار بعد.",
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
  "event": "حدث",
  "events": "أحداث",
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
  "Toggle": "توسيع أو طي",
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
  "Power sign": "إشارة مقاس العدسة",
  "Power value": "قيمة مقاس العدسة",
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
  "Loading batches...": "جارٍ تحميل دفعات المخزون...",
  "Failed to load batches": "تعذر تحميل دفعات المخزون",
  "Create new batch": "إنشاء دفعة جديدة",
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
  "Healthy": "سليم",
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
  "Business name and contact person are required.": "اسم النشاط ومسؤول التواصل حقول مطلوبة.",
  "Merchant updated.": "تم تحديث بيانات التاجر.",
  "Merchant created.": "تم إنشاء التاجر.",
  "Merchant deactivated.": "تم إيقاف التاجر.",
  "Merchant reactivated.": "تمت إعادة تفعيل التاجر.",
  "Add Merchant Note": "إضافة ملاحظة للتاجر",
  "Write a short note for this merchant profile.": "اكتب ملاحظة قصيرة في ملف التاجر.",
  "Note added.": "تمت إضافة الملاحظة.",
  "Sold packs": "العبوات المباعة",
  "Sold pieces": "القطع المباعة",
  "Adjusted amount": "المبلغ بعد التسويات",
  "Amount due": "المبلغ المستحق",
  "Classification": "التصنيف",
  "Collection confirmed and posted to the merchant account.": "تم اعتماد التحصيل وترحيله إلى حساب التاجر.",
  "Confirmed collections": "التحصيلات المؤكدة",
  "Credit": "دائن",
  "Credit available": "الرصيد الدائن المتاح",
  "Debit": "مدين",
  "Electronic payments require a transaction reference.": "المدفوعات الإلكترونية تتطلب مرجع معاملة.",
  "Flags": "التنبيهات",
  "Load a merchant account statement.": "حمّل كشف حساب التاجر.",
  "Load statement": "تحميل الكشف",
  "Merchant account statement": "كشف حساب التاجر",
  "No posted account movements.": "لا توجد حركات مرحلة على الحساب.",
  "One running account for sales, returns, exchanges, collections, adjustments, and refunds.": "حساب جارٍ موحد للمبيعات والمرتجعات والاستبدالات والتحصيلات والتسويات والاستردادات.",
  "Operation / payment": "العملية / المدفوعة",
  "Pending collections": "تحصيلات معلقة",
  "Refund payouts": "مدفوعات الاسترداد",
  "Reserved refund": "استرداد محجوز",
  "Returns / reductions": "المرتجعات / التخفيضات",
  "Total sales": "إجمالي المبيعات",
  "Net collected": "صافي المحصل",
  "Remaining owed": "المتبقي المستحق",
  "Refunds paid": "المبالغ المستردة المدفوعة",
  "Refunds approved / due": "استردادات معتمدة / مستحقة الدفع",
  "Refunds applied to orders": "استردادات مطبقة على الطلبات",
  "Accepted return value": "قيمة المرتجعات المقبولة",
  "Accepted return value applied": "قيمة المرتجعات المطبقة",
  "Approved additional charges": "الرسوم الإضافية المعتمدة",
  "Approved amount reductions": "التخفيضات المعتمدة",
  "Orders and mini-invoices": "الطلبات والفواتير المصغرة",
  "Sale total": "إجمالي البيع",
  "Collected": "المحصل",
  "Rows": "الصفوف",
  "Running balance": "الرصيد الجاري",
  "Select a merchant and payment method.": "اختر تاجرًا وطريقة دفع.",
  "Remaining to collect": "المتبقي للتحصيل",
  "Refund due": "استرداد مستحق",
  "Pending collection": "تحصيل قيد الاعتماد",
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
  "Merchant detail": "تفاصيل التاجر",
  "Commercial profile": "الملف التجاري",
  "Start a new operation draft.": "ابدأ مسودة عملية جديدة.",
  "This role can inspect operations but cannot create or revise drafts.": "يمكن لهذا الدور عرض العمليات فقط، ولا يمكنه إنشاء المسودات أو تعديلها.",
  "No.": "الرقم",
  "Route": "المسار",
  "Receipt only": "خاص بعمليات الاستلام",
  "Used for receipt flows": "يُستخدم في مسارات الاستلام",
  "Required for revisions": "مطلوب عند مراجعة عملية",
  "Select product attributes to resolve SKU.": "اختر خصائص المنتج لتحديد رمز الصنف.",
  "Select product, power, and color to resolve SKU.": "اختر المنتج ومقاس العدسة واللون لتحديد رمز الصنف.",
  "Try another color, power, package, or source location.": "جرّب لونًا أو مقاسًا أو عبوة أو موقع صرف آخر.",
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
  "Reserve is temporarily unavailable.": "الحجز غير متاح مؤقتًا.",
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
  "Representative reserve": "حجز للمندوب",
  "WriteOff": "إعدام / تسوية مخزون",
  "CashHandToHand": "نقدي مباشر",
  "CashTransaction": "تحويل أو إيداع نقدي",
  "BankTransfer": "تحويل بنكي",
  "Wallet": "محفظة إلكترونية",
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
  "Record collection": "تسجيل تحصيل",
  "Collect toward": "التحصيل لصالح",
  "Operation or payment reference": "مرجع العملية أو المدفوعة",
  "Amount received": "المبلغ المستلم",
  "How was it received?": "كيف تم استلامه؟",
  "Choose method...": "اختر الطريقة...",
  "Transaction reference": "مرجع المعاملة",
  "Required for electronic payments": "مطلوب للمدفوعات الإلكترونية",
  "Send for approval": "إرسال للاعتماد",
  "Saving keeps the entry editable. Sending reserves the amount and places it in the Admin review queue. The balance changes only after approval.": "يحافظ الحفظ على إمكانية تعديل الحركة. الإرسال يحجز المبلغ ويضعه في قائمة مراجعة الإدارة، ولا يتغير الرصيد إلا بعد الاعتماد.",
  "Admin approval required": "يتطلب اعتماد الإدارة",
  "Use the same form for merchant accounts, cash sales, electronic payments, and installments.": "استخدم النموذج نفسه لحسابات التجار والمبيعات النقدية والمدفوعات الإلكترونية والتقسيط.",
  "Every adjustment is linked to its source operation. Cash refunds are approved first, then recorded when cash is actually paid.": "كل تسوية مرتبطة بعملية مصدرها. تتم الموافقة على الاسترداد النقدي أولًا ثم تسجيله عند دفع النقد فعليًا.",
  "Enter an operation first": "أدخل عملية أولًا",
  "Request adjustment": "طلب تسوية",
  "Payments audit": "سجل تدقيق المدفوعات",
  "Refresh audit": "تحديث سجل التدقيق",
  "Append-only financial workflow events, including assignment, submission, approval, rejection, refunds, and reconciliation.": "أحداث سير العمل المالي غير القابلة للتعديل، وتشمل الإسناد والإرسال والاعتماد والرفض والاسترداد والمطابقة.",
  "All collections are assigned and submitted for approval before posting.": "تُسند جميع التحصيلات وتُرسل للاعتماد قبل ترحيلها.",
  "Record confirmed cash": "تسجيل نقدية معتمدة",
  "Merchant-account collections are assigned, then require Admin or ERPAdmin approval before posting.": "يتم إسناد تحصيلات حساب التاجر ثم تتطلب اعتماد المدير أو مدير ERP قبل ترحيلها.",
  "Submit collection for approval": "إرسال التحصيل للاعتماد",
  "Collection submitted for Admin approval. It is not posted yet.": "تم إرسال التحصيل لاعتماد الإدارة ولم يُرحّل بعد.",
  "Collection approved and posted.": "تم اعتماد التحصيل وترحيله.",
  "Collection rejected.": "تم رفض التحصيل.",
  "Rejection reason": "سبب الرفض",
  "Reject collection": "رفض التحصيل",
  "Record the reason. Rejected collections remain visible in the account work history.": "سجّل سبب الرفض. ستظل التحصيلات المرفوضة ظاهرة في سجل أعمال الحساب.",
  "No account collections awaiting review.": "لا توجد تحصيلات حساب بانتظار المراجعة.",
  "No other payment confirmations are waiting.": "لا توجد تأكيدات مدفوعات أخرى بانتظار المراجعة.",
  "Additional charge": "رسوم إضافية",
  "No Payments audit events yet.": "لا توجد أحداث تدقيق للمدفوعات بعد.",
  "Load a merchant to view collection work.": "حمّل التاجر لعرض أعمال التحصيل.",
  "Load the audit history.": "حمّل سجل التدقيق.",
  "Submitted": "تاريخ الإرسال",
  "Cash / refund record": "حركة نقدية / استرداد",
  "Use source-linked additional charges, remaining reductions, and separately paid cash refunds.": "استخدم الرسوم الإضافية المرتبطة بالمصدر وتخفيضات المتبقي والاستردادات النقدية التي تُصرف بشكل منفصل.",
  "Operation ID": "معرّف العملية",
  "Use": "استخدام",
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
  "Choose the merchant order affected by this adjustment.": "اختر طلب التاجر المتأثر بهذه التسوية.",
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
  "Record cash refund payout": "\u062a\u0633\u062c\u064a\u0644 \u0635\u0631\u0641 \u0627\u0644\u0627\u0633\u062a\u0631\u062f\u0627\u062f \u0627\u0644\u0646\u0642\u062f\u064a",
  "Enter the amount that was actually paid to the merchant.": "\u0623\u062f\u062e\u0644 \u0627\u0644\u0645\u0628\u0644\u063a \u0627\u0644\u0630\u064a \u062a\u0645 \u0635\u0631\u0641\u0647 \u0641\u0639\u0644\u0627\u064b \u0644\u0644\u062a\u0627\u062c\u0631.",
  "Cash refund payout recorded.": "\u062a\u0645 \u062a\u0633\u062c\u064a\u0644 \u0635\u0631\u0641 \u0627\u0644\u0627\u0633\u062a\u0631\u062f\u0627\u062f \u0627\u0644\u0646\u0642\u062f\u064a.",
  "Record payout": "\u062a\u0633\u062c\u064a\u0644 \u0627\u0644\u0635\u0631\u0641",
  "Reject Financial Adjustment": "\u0631\u0641\u0636 \u0627\u0644\u062a\u0633\u0648\u064a\u0629 \u0627\u0644\u0645\u0627\u0644\u064a\u0629",
  "Record the reason. Rejected adjustments remain visible in the log.": "\u0633\u062c\u0644 \u0633\u0628\u0628 \u0627\u0644\u0631\u0641\u0636. \u062a\u0628\u0642\u0649 \u0627\u0644\u062a\u0633\u0648\u064a\u0627\u062a \u0627\u0644\u0645\u0631\u0641\u0648\u0636\u0629 \u0638\u0627\u0647\u0631\u0629 \u0641\u064a \u0627\u0644\u0633\u062c\u0644.",
  "Cash record saved.": "تم حفظ الحركة النقدية.",
  "Loaded": "تم التحميل",
  "PendingAdmin": "بانتظار الإدارة",
  "PendingAccountant": "بانتظار المحاسب",
  "CashReceived": "تحصيل نقدي",
  "CashRefund": "استرداد نقدي",
  "AdditionalCharge": "رسوم إضافية",
  "BalanceReduction": "تخفيض المتبقي على التاجر",
  "Required for cash refund": "مطلوب عند الاسترداد النقدي",
  "Download operational, inventory, payment, and statement outputs in CSV and PDF formats.": "نزّل تقارير العمليات والمخزون والمدفوعات وكشوف الحساب بصيغ CSV وPDF.",
  "CSV": "CSV",
  "Sales": "المبيعات",
  "The confirmed amount still to collect.": "المبلغ المؤكد المتبقي تحصيله.",
  "Available for a later sale or an approved refund.": "متاح لعملية بيع لاحقة أو لاسترداد معتمد.",
  "Money received minus completed refunds.": "الأموال المحصلة بعد خصم الاستردادات المصروفة.",
  "Collection work": "متابعة التحصيلات",
  "Accepted returns credited to the account.": "المرتجعات المقبولة المضافة إلى الحساب.",
  "Additional charges": "الرسوم الإضافية",
  "Amount reductions": "تخفيضات المبلغ",
  "Approved charges.": "الرسوم المعتمدة.",
  "Approved reductions.": "التخفيضات المعتمدة.",
  "Approved and paid cash refunds.": "الاستردادات النقدية المعتمدة والمدفوعة.",
  "Approved charges, including higher-value exchanges.": "الرسوم المعتمدة، بما فيها فروق الاستبدال الأعلى قيمة.",
  "Approved reductions applied to the account.": "تخفيضات معتمدة مطبقة على الحساب.",
  "Approved return and exchange credits.": "أرصدة الإرجاع والاستبدال المعتمدة.",
  "Collection sent for Admin approval. The balance has not changed yet.": "تم إرسال التحصيل لاعتماد الإدارة. لم يتغير الرصيد بعد.",
  "Optional: link this adjustment to one mini-invoice.": "اختياري: اربط هذا التعديل بفاتورة مصغرة واحدة.",
  "Collection submitted. Some payment totals will refresh on the next reload.": "تم إرسال التحصيل. ستتحدث بعض إجماليات المدفوعات عند إعادة التحميل.",
  "Completed merchant sales.": "مبيعات التاجر المكتملة.",
  "Confirmed collections minus completed cash refunds.": "التحصيلات المؤكدة مطروحًا منها الاستردادات النقدية المصروفة.",
  "Each wholesale order, its collections, and its remaining amount.": "كل طلب جملة وتحصيلاته ومبلغه المتبقي.",
  "Items": "الأصناف",
  "Exchange": "استبدال",
  "Merchant records": "سجلات التجار",
  "Operation, merchant, buyer, SKU, or payment reference": "العملية أو التاجر أو المشتري أو الصنف أو مرجع الدفع",
  "Paid refunds; approved/due amounts are in account details.": "الاستردادات المدفوعة؛ تظهر المبالغ المعتمدة والمستحقة في تفاصيل الحساب.",
  "Refunds": "الاستردادات",
  "Returns": "المرتجعات",
  "Charges": "الرسوم",
  "Reductions": "التخفيضات",
  "Refunds applied as reductions": "الاستردادات المطبقة كتخفيضات",
  "Show an account to view orders.": "اعرض حسابًا لمراجعة الطلبات.",
  "Saved drafts and amounts waiting for Admin approval.": "المسودات المحفوظة والمبالغ التي تنتظر اعتماد الإدارة.",
  "Show an account to view collection work.": "اعرض حساب التاجر لمراجعة التحصيلات.",
  "Collection sent for Admin approval.": "تم إرسال التحصيل لاعتماد الإدارة.",
  "Reject cash receipt": "رفض التحصيل النقدي",
  "Record the reason. The rejected receipt remains in the payment history.": "سجّل سبب الرفض. سيظل التحصيل المرفوض ظاهرًا في سجل المدفوعات.",
  "Cash receipt rejected.": "تم رفض التحصيل النقدي.",
  "Status / reason": "الحالة / السبب",
  "Returns / adjustments": "المرتجعات / التسويات",
  "Account adjustments": "تسويات الحساب",
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
  "Product, color, power, SKU": "المنتج أو اللون أو مقاس العدسة أو رمز الصنف",
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
  "Actual total I have": "الإجمالي الفعلي لدي",
  "Batch expiry is required for products with batch expiry tracking.": "تاريخ انتهاء الدفعة مطلوب للمنتجات التي تعتمد تتبع انتهاء الدفعات.",
  "Imported shipments, landed costs, and receipts.": "الشحنات المستوردة، تكلفة الوصول، وإيصالات المخزون.",
  "Imported shipments": "الشحنات المستوردة",
  "Shipments": "الشحنات",
  "Shipment": "الشحنة",
  "Draft value": "قيمة المسودات",
  "Ready to confirm": "جاهزة للتأكيد",
  "Register imported shipments, allocate customs and import costs, then post controlled inventory receipts.": "سجل الشحنات المستوردة، وزع الجمارك ومصاريف الاستيراد، ثم أنشئ إيصالات مخزون مضبوطة.",
  "Search by shipment, supplier, or invoice.": "ابحث برقم الشحنة أو المورد أو الفاتورة.",
  "Search shipments": "بحث في الشحنات",
  "All statuses": "كل الحالات",
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
  "SKU lines": "بنود SKU",
  "Prices can stay blank while drafting and must be completed before confirmation.": "يمكن ترك السعر فارغا في المسودة، ويجب إكماله قبل التأكيد.",
  "Import cost breakdown": "تفصيل تكاليف الاستيراد",
  "Add cost": "إضافة تكلفة",
  "Product subtotal": "إجمالي المنتجات",
  "Import costs": "تكاليف الاستيراد",
  "Landed total": "الإجمالي بعد التكلفة",
  "Confirmation readiness": "جاهزية التأكيد",
  "Incomplete prices": "أسعار ناقصة",
  "Find SKU": "بحث SKU",
  "Product, color, power, SKU code": "المنتج، اللون، القوة، كود SKU",
  "Product, color, power, or SKU code": "المنتج أو اللون أو القوة أو كود SKU",
  "Search and select a SKU.": "ابحث واختر SKU.",
  "Draft blank": "فارغ في المسودة",
  "Required before confirmation.": "مطلوب قبل التأكيد.",
  "Line notes": "ملاحظات البند",
  "Price must be greater than zero.": "السعر يجب أن يكون أكبر من صفر.",
  "Selected SKU": "SKU محدد",
  "Cost type": "نوع التكلفة",
  "Customs": "جمارك",
  "Freight": "شحن",
  "Clearance": "تخليص",
  "Handling": "مناولة",
  "Insurance": "تأمين",
  "Description": "الوصف",
  "Remove cost": "حذف التكلفة",
  "Loading shipments...": "جار تحميل الشحنات...",
  "No supply shipments match the current filters.": "لا توجد شحنات مطابقة للفلاتر الحالية.",
  "Loading shipment...": "جار تحميل الشحنة...",
  "Confirm receipt": "تأكيد الاستلام",
  "Print receipt": "طباعة الإيصال",
  "Readiness": "جاهزية التأكيد",
  "Inventory receipt operation": "عملية إيصال المخزون",
  "Lines": "البنود",
  "Unit": "الوحدة",
  "Line": "البند",
  "Allocated": "الموزع",
  "Landed unit": "تكلفة الوحدة النهائية",
  "Blank": "فارغ",
  "Cost breakdown": "تفصيل التكاليف",
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
  "Delete account": "حذف الحساب",
  "Confirm account status": "تأكيد حالة الحساب",
  "Transfer primary Administrator": "نقل المسؤول الرئيسي",
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
  "Resolve Shopify event": "تسوية حدث Shopify",
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
  "Source Location Id": "معرّف موقع المصدر",
  "Destination Location Id": "معرّف موقع الوجهة",
  "Is Active": "نشط",
  "Related record": "السجل المرتبط",
  "Audit record": "سجل التدقيق",
  "Inventory batch": "دفعة مخزون",
  "Payment record": "سجل الدفع",
  "Stocktake session": "جلسة الجرد",
  "Supply shipment": "شحنة التوريد",
  "Employee": "الموظف",
  "Internal reference hidden": "تم إخفاء المعرّف الداخلي",
  "Specific employee": "موظف محدد",
  "Open operation": "فتح العملية"
});

// During the incremental migration, prefer the semantic foundation dictionary
// whenever an existing render-time phrase already has a canonical key. The
// phrase dictionary remains only as a compatibility fallback for dynamic or
// legacy route copy that has not yet been assigned a semantic key.
const foundationKeyByEnglish = Object.freeze(Object.entries(enMessages).reduce((lookup, [key, message]) => {
  if (typeof message === "string" && !lookup[message]) lookup[message] = key;
  return lookup;
}, Object.create(null)));

function uiText(english) {
  const value = String(english ?? "");
  const foundationKey = foundationKeyByEnglish[value];
  if (foundationKey) return foundationT(foundationKey).trim();
  const language = document.documentElement.dir === "rtl" ? "ar" : getFoundationLanguage();
  return (language === "ar" ? (arabicTranslations[value] || value) : value).trim();
}

function applyLanguage(root = document.body) {
  if (applyingLanguage) return;
  applyingLanguage = true;

  const isArabic = currentLanguage === "ar";
  const localizedElements = [
    ...(root.matches?.("[data-i18n]") ? [root] : []),
    ...(root.querySelectorAll?.("[data-i18n]") || [])
  ];
  localizedElements.forEach((element) => {
    element.textContent = foundationT(element.dataset.i18n);
  });
  const localizedAttributeElements = [
    ...(root.matches?.("[data-i18n-placeholder], [data-i18n-title], [data-i18n-aria-label]") ? [root] : []),
    ...(root.querySelectorAll?.("[data-i18n-placeholder], [data-i18n-title], [data-i18n-aria-label]") || [])
  ];
  localizedAttributeElements.forEach((element) => {
    for (const [attribute, dataAttribute] of [["placeholder", "i18nPlaceholder"], ["title", "i18nTitle"], ["aria-label", "i18nAriaLabel"]]) {
      if (element.dataset[dataAttribute]) element.setAttribute(attribute, foundationT(element.dataset[dataAttribute]));
    }
  });
  if (root === document.body) {
    document.documentElement.lang = isArabic ? "ar-EG" : "en";
    document.documentElement.dir = isArabic ? "rtl" : "ltr";
    document.body.classList.toggle("lang-ar", isArabic);
    const route = routes[currentPath()];
    document.title = route ? `Lensee - ${foundationT(route.i18nTitle ?? "routes.dashboard.title")}` : "Lensee";
    if (route) {
      document.getElementById("page-title")?.replaceChildren(document.createTextNode(foundationT(route.i18nTitle)));
      document.getElementById("route-label")?.replaceChildren(document.createTextNode(foundationT(route.i18nLabel)));
    }
    document.querySelectorAll("#language-toggle, #login-language-toggle").forEach((toggle) => {
      toggle.setAttribute("data-no-translate", "");
      toggle.textContent = isArabic ? "English" : "العربية";
      toggle.setAttribute("aria-label", isArabic ? "التبديل إلى الإنجليزية" : "Switch to Arabic");
      toggle.title = isArabic ? "التبديل إلى الإنجليزية" : "Switch to Arabic";
    });
    applyStaticShellLanguage();
  }
  applyingLanguage = false;
}

function setLanguage(language) {
  const preservedFormState = captureLanguageSwitchState();
  currentLanguage = setFoundationLanguage(language);
  localStorage.setItem(languageKey, currentLanguage);
  applyLanguage();
  if (getAuth()) renderNav(getAuth());
  const localizedWorkspaceRoutes = new Set(["/dashboard", "/catalog", "/crm", "/operations", "/inventory", "/supply", "/payments", "/notifications", "/reports", "/stocktake", "/admin", "/audit", "/integrations"]);
  const path = currentPath();
  if (path === "/login") {
    void routes[path].render().then(() => applyLanguage());
    return;
  }
  if (path === "/crm" && getAuth()) {
    if (preservedFormState.selectedMerchantId) void showMerchantDetail(preservedFormState.selectedMerchantId);
    applyLanguage();
    return;
  }
  if (path === "/admin" && getAuth()) {
    // Keep unsaved account-management fields in place while switching the
    // presentation language; the form's canonical option values are already
    // independent of their visible labels.
    applyLanguage();
    return;
  }
  if (localizedWorkspaceRoutes.has(path) && getAuth()) {
    Promise.resolve(routes[path].render())
      .then(() => {
        restoreLanguageSwitchState(preservedFormState, true);
        if (path === "/supply" && preservedFormState.selectedSupplyShipmentId)
          void showSupplyDetail(preservedFormState.selectedSupplyShipmentId);
        // Async route hydration (notably Admin locations and user lists) can
        // repopulate controls after the route promise resolves. Reapply the
        // captured values once more on the next task so a language switch
        // cannot erase unsaved form work.
        window.setTimeout(() => restoreLanguageSwitchState(preservedFormState, false), 50);
        if (path === "/crm" && preservedFormState.selectedMerchantId) {
          void showMerchantDetail(preservedFormState.selectedMerchantId);
        }
        applyLanguage();
      })
      .catch((exception) => notice(getFriendlyWorkspaceError(exception), "error"));
  }
}

function applyStaticShellLanguage() {
  const shellBindings = [
    ["#logout-button", "common.signOut"],
    ["#sidebar-toggle", "navigation.open"],
    [".sidebar", "navigation.primary"]
  ];
  shellBindings.forEach(([selector, key]) => {
    const element = document.querySelector(selector);
    if (!element) return;
    if (element.matches("button")) {
      element.textContent = foundationT(key);
      element.setAttribute("aria-label", foundationT(key));
      element.setAttribute("title", foundationT(key));
    }
    if (element.matches(".sidebar")) element.setAttribute("aria-label", foundationT(key));
  });
  const brand = document.querySelector(".brand");
  if (brand) brand.setAttribute("aria-label", foundationT("navigation.dashboard"));
}

function captureLanguageSwitchState() {
  const operationForm = document.getElementById("operation-form");
  if (operationForm) syncCurrentOperationPage();
  const operationEditor = operationForm ? {
    lines: operationEditorLines.map((line) => ({ ...line })),
    page: operationEditorPage,
    uiState: { ...operationsUiState, openDetailIds: [...operationsUiState.openDetailIds] }
  } : null;
  const supplyForm = document.getElementById("supply-form");
  if (supplyForm) syncCurrentSupplyPage();
  const supplyEditor = supplyForm ? {
    lines: supplyEditorLines.map((line) => ({ ...line })),
    page: supplyEditorPage,
    costs: [...document.querySelectorAll(".supply-cost-row")].map((row) => ({
      costType: row.querySelector(".supply-cost-type")?.value || "Other",
      description: row.querySelector(".supply-cost-description")?.value || "",
      amount: row.querySelector(".supply-cost-amount")?.value || "0"
    }))
  } : null;
  const values = [];
  const stocktakeForm = document.getElementById("stocktake-lines-form");
  const stocktakeEditor = stocktakeForm ? {
    sessionId: document.getElementById("stocktake-detail")?.dataset.sessionId || null,
    lines: [...document.querySelectorAll(".stocktake-line-row")].map((row) => ({
      skuId: row.querySelector(".stocktake-line-sku")?.value || "",
      search: row.querySelector(".stocktake-line-search")?.value || "",
      lotNumber: row.querySelector(".stocktake-line-lot")?.value || "",
      expiryDate: row.querySelector(".stocktake-line-expiry")?.value || "",
      physicalPackCount: row.querySelector(".stocktake-line-pack-count")?.value || "0",
      physicalPieceCount: row.querySelector(".stocktake-line-piece-count")?.value || "0",
      lineNote: row.querySelector(".stocktake-line-note")?.value || ""
    }))
  } : null;
  document.querySelectorAll("#view input, #view select, #view textarea").forEach((element, index) => {
    const key = element.id || `${element.name || element.tagName}:${index}`;
    values.push({ key, value: element.value, checked: element.checked, selectedIndex: element.selectedIndex });
  });
  return { values, hash: window.location.hash, path: currentPath(), scrollY: window.scrollY, selectedMerchantId,
    expenseEditId: document.getElementById("finance-expense-form")?.dataset.editId || null,
    repaymentEditId: document.getElementById("finance-repayment-form")?.dataset.correctId || null,
    operationEditor, operationListPage, reportPageState: { ...reportPageState },
    supplyEditor, supplyListPage, selectedSupplyShipmentId, stocktakeEditor };
}

function restoreLanguageSwitchState(state, reload = false) {
  if (!state) return;
  if (state.operationEditor && document.getElementById("operation-form")) {
    operationsUiState = { ...state.operationEditor.uiState, openDetailIds: [...state.operationEditor.uiState.openDetailIds] };
    operationEditorLines = state.operationEditor.lines.map((line) => ({ ...line }));
    operationEditorLineById = new Map(operationEditorLines.map((line) => [line._clientId, line]));
    operationEditorPage = state.operationEditor.page;
    renderOperationEditorPage();
    applyOperationEditorMode();
  }
  if (state.reportPageState) reportPageState = { ...state.reportPageState };
  if (state.operationListPage) operationListPage = state.operationListPage;
  if (state.supplyListPage) supplyListPage = state.supplyListPage;
  if (state.supplyEditor && document.getElementById("supply-form")) {
    supplyEditorLines = state.supplyEditor.lines.map((line) => ({ ...line }));
    supplyEditorLineById = new Map(supplyEditorLines.map((line) => [line._clientId, line]));
    supplyEditorPage = state.supplyEditor.page;
    supplyEditorStatsDirty = true;
    renderSupplyEditorPage();
    document.getElementById("supply-costs")?.replaceChildren();
    state.supplyEditor.costs.forEach((cost) => addSupplyCost(cost));
  }
  if (state.stocktakeEditor?.sessionId && document.getElementById("stocktake-detail")) {
    void showStocktakeDetail(state.stocktakeEditor.sessionId).then(() => {
      const rows = [...document.querySelectorAll(".stocktake-line-row")];
      state.stocktakeEditor.lines.forEach((line, index) => {
        const row = rows[index];
        if (!row) return;
        row.querySelector(".stocktake-line-search").value = line.search;
        row.querySelector(".stocktake-line-lot").value = line.lotNumber;
        row.querySelector(".stocktake-line-expiry").value = line.expiryDate;
        row.querySelector(".stocktake-line-pack-count").value = line.physicalPackCount;
        row.querySelector(".stocktake-line-piece-count").value = line.physicalPieceCount;
        row.querySelector(".stocktake-line-note").value = line.lineNote;
        if (line.skuId) seedStocktakeLineSkuSelection(row, line.skuId);
      });
    });
  }
  const expenseForm = document.getElementById("finance-expense-form");
  if (expenseForm && state.expenseEditId) {
    expenseForm.dataset.editId = state.expenseEditId;
    const submit = expenseForm.querySelector('button[type="submit"]');
    if (submit) submit.textContent = financeT("expenses.savePending");
  }
  const repaymentForm = document.getElementById("finance-repayment-form");
  if (repaymentForm && state.repaymentEditId) {
    repaymentForm.dataset.correctId = state.repaymentEditId;
    const note = document.getElementById("finance-repayment-correction-note");
    if (note) { note.closest("label").hidden = false; note.required = true; }
    const submit = repaymentForm.querySelector('button[type="submit"]');
    if (submit) submit.textContent = financeT("repayments.saveCorrection");
  }
  state.values.forEach(({ key, value, checked, selectedIndex }) => {
    const element = document.getElementById(key) || [...document.querySelectorAll("#view input, #view select, #view textarea")]
      .find((candidate, index) => `${candidate.name || candidate.tagName}:${index}` === key);
    if (!element) return;
    if (element.tagName === "SELECT") {
      element.value = value;
      if (element.value !== value) element.selectedIndex = selectedIndex;
    }
    else {
      element.value = value;
      if (typeof checked === "boolean") element.checked = checked;
    }
    const isListFilter = (state.path === "/reports" && element.id.startsWith("report-filter-")) ||
      (state.path === "/operations" && element.id.startsWith("operations-")) ||
      (state.path === "/supply" && ["supply-search", "supply-status"].includes(element.id));
    if (!isListFilter) element.dispatchEvent(new Event("change", { bubbles: true }));
  });
  if (state.reportPageState) reportPageState = { ...state.reportPageState };
  if (state.operationListPage) operationListPage = state.operationListPage;
  if (state.supplyListPage) supplyListPage = state.supplyListPage;
  if (reload && state.path === "/reports") void loadReports();
  if (reload && state.path === "/operations") void loadOperations();
  if (reload && state.path === "/supply") void loadSupplyShipments();
  if (state.hash && window.location.hash !== state.hash) window.location.hash = state.hash;
  window.scrollTo({ top: state.scrollY, behavior: "auto" });
}

function canonicalSystemValue(value, domain, options) {
  return readCanonicalSystemValue(value, domain, options);
}

function canonicalSelectValue(id, domain, options) {
  return readCanonicalSelectValue(document, id, domain, options);
}

const routes = {
  "/login": { title: "Sign In", label: "Identity", i18nTitle: "routes.login.title", i18nLabel: "routes.login.label", roles: [], render: renderLogin },
  "/dashboard": { title: "Overview", label: "Dashboard", i18nTitle: "routes.dashboard.title", i18nLabel: "routes.dashboard.label", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderDashboard },
  "/catalog": { title: "Catalog", label: "Catalog", i18nTitle: "routes.catalog.title", i18nLabel: "routes.catalog.label", roles: ["CLevel", "Admin", "ERPAdmin", "WarehouseClerk"], render: renderCatalog },
  "/inventory": { title: "Inventory", label: "Inventory", i18nTitle: "routes.inventory.title", i18nLabel: "routes.inventory.label", roles: ["CLevel", "Admin", "ERPAdmin", "WarehouseClerk"], render: renderInventory },
  "/supply": { title: "Supply", label: "Supply", i18nTitle: "routes.supply.title", i18nLabel: "routes.supply.label", roles: ["CLevel", "Admin"], render: renderSupply },
  "/crm": { title: "CRM", label: "CRM", i18nTitle: "routes.crm.title", i18nLabel: "routes.crm.label", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderCrm },
  "/operations": { title: "Operations", label: "Operations", i18nTitle: "routes.operations.title", i18nLabel: "routes.operations.label", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderOperations },
  "/payments": { title: "Payments", label: "Payments", i18nTitle: "routes.payments.title", i18nLabel: "routes.payments.label", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant"], render: renderPayments },
  "/notifications": { title: "Notifications", label: "Notifications", i18nTitle: "routes.notifications.title", i18nLabel: "routes.notifications.label", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant", "WarehouseClerk"], render: renderNotifications },
  "/integrations": { title: "Online intake", label: "Online intake", i18nTitle: "routes.integrations.title", i18nLabel: "routes.integrations.label", roles: ["CLevel", "Admin", "ERPAdmin", "WarehouseClerk"], render: renderShopifyIntegration },
  "/reports": { title: "Reports", label: "Reports", i18nTitle: "routes.reports.title", i18nLabel: "routes.reports.label", roles: ["CLevel", "Admin", "ERPAdmin", "Accountant"], render: renderReports },
  "/stocktakes": { title: "Stocktake", label: "Stocktake", i18nTitle: "routes.stocktakes.title", i18nLabel: "routes.stocktakes.label", roles: ["CLevel", "Admin", "ERPAdmin"], render: renderStocktakes },
  "/audit": { title: "Audit history", label: "Audit history", i18nTitle: "routes.audit.title", i18nLabel: "routes.audit.label", roles: ["Admin", "ERPAdmin"], render: renderAudit },
  "/admin": { title: "Administration", label: "Admin", i18nTitle: "routes.admin.title", i18nLabel: "routes.admin.label", roles: ["Admin", "ERPAdmin"], render: renderAdmin }
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

const navItemTranslationKeys = new Map(navItems.map(([href]) => [href, routes[href]?.i18nLabel]));
const navGroupTranslationKeys = new Map([
  ["Daily work", "navigation.dailyWork"],
  ["Money", "navigation.money"],
  ["Stock", "navigation.stock"],
  ["Oversight", "navigation.oversight"]
]);

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
      if (segment.dataset.paymentView) {
        applyPaymentsView(segment.dataset.paymentView);
        void loadPaymentViewPanel(segment.dataset.paymentView);
      }
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
  refreshAfterAttentionChange("focus");
});
document.addEventListener("visibilitychange", () => {
  if (!document.hidden) refreshAfterAttentionChange("visible");
});

function refreshAfterAttentionChange(reason) {
  if (document.hidden || Date.now() < routeRefreshCooldownUntil) return;
  // Browsers commonly emit focus and visibilitychange together after a tab is
  // restored.  One refresh is enough and keeps large workspaces responsive.
  routeRefreshCooldownUntil = Date.now() + 750;
  refreshActiveView({ reason });
}
const debouncedMutationRefresh = debounce(() => {
  refreshActiveView({ reason: "local-mutation" });
  updateNotificationBadge();
}, 200);
window.addEventListener(mutationEventName, () => {
  debouncedMutationRefresh();
});
window.addEventListener("storage", (event) => {
  if (!syncChannel && event.key === syncStorageKey && event.newValue) {
    try { handleExternalSync(JSON.parse(event.newValue)); } catch { /* Ignore malformed sync payloads. */ }
  }
});
window.addEventListener(authEventName, renderRoute);
syncChannel?.addEventListener("message", (event) => handleExternalSync(event.data));
checkHealth();
restoreSessionFromCookie().finally(() => {
  renderRoute();
  applyLanguage();
});

function getAuth() {
  return activeAuth;
}
window.__lenseeGetAuth = getAuth;

function setAuth(auth, { broadcast = true } = {}) {
  activeAuth = auth ? { user: auth.user } : null;
  if (broadcast) publishSync({ type: "auth-signed-in", source: tabId });
}

function clearAuth({ broadcast = true } = {}) {
  activeAuth = null;
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

function buildRequestHeaders(options = {}) {
  const headers = new Headers(options.headers || {});
  applyApiHeaders(headers);
  if (options.body !== undefined && !(options.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
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
  let headers = buildRequestHeaders(options);
  let response = await fetch(`${apiBase}${path}`, { ...options, headers, credentials: "include" });
  if (response.status !== 401) {
    return response;
  }

  const refreshed = await refreshSession();
  if (!refreshed) {
    return response;
  }

  headers = buildRequestHeaders(options);
  return fetch(`${apiBase}${path}`, { ...options, headers, credentials: "include" });
}

function request(path, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  if (method !== "GET") return executeRequest(path, options);
  const requestOptions = options.signal ? options : { ...options, signal: activeRouteController.signal };
  const cached = referenceResponseCache.get(path);
  if (cached && cached.expiresAt > Date.now()) return Promise.resolve(cached.payload);
  const key = `${path}|${requestOptions.signal === activeRouteController.signal ? routeRenderGeneration : "external"}`;
  if (pendingGetRequests.has(key)) return pendingGetRequests.get(key);
  const execute = () => executeRequest(path, requestOptions).then((payload) => {
    if (isStableReferenceRequest(path)) referenceResponseCache.set(path, { payload, expiresAt: Date.now() + 15000 });
    return payload;
  });
  const pending = (isHeavyRequest(path) ? enqueueHeavyRequest(execute, requestOptions.signal) : execute()).finally(() => pendingGetRequests.delete(key));
  pendingGetRequests.set(key, pending);
  return pending;
}

function isStableReferenceRequest(path) {
  return path === "/api/v1/inventory/locations" || /^\/api\/v1\/catalog\/skus\/[0-9a-f-]{36}$/i.test(path);
}

function isHeavyRequest(path) {
  return path.includes("/editor") || path.includes("/product-totals") || path.includes("/replenishment") || /\/(supply\/shipments|operations)\/[0-9a-f-]{36}/i.test(path);
}

function enqueueHeavyRequest(execute, signal) {
  return new Promise((resolve, reject) => {
    const task = { execute, resolve, reject, signal };
    heavyRequestQueue.push(task);
    drainHeavyRequestQueue();
  });
}

function drainHeavyRequestQueue() {
  while (activeHeavyRequests < maxHeavyRequests && heavyRequestQueue.length > 0) {
    const task = heavyRequestQueue.shift();
    if (task.signal?.aborted) {
      task.reject(new DOMException("Request aborted", "AbortError"));
      continue;
    }
    activeHeavyRequests += 1;
    task.execute().then(task.resolve, task.reject).finally(() => {
      activeHeavyRequests -= 1;
      drainHeavyRequestQueue();
    });
  }
}

async function executeRequest(path, options = {}) {
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
    // notify: false lets a caller that already performs its own targeted
    // refresh (e.g. reloading just one table after a small write) skip the
    // global "refresh everything on this page" listener below, so a
    // single-row edit doesn't also trigger a full six-table page rebuild
    // in THIS tab. Other tabs still get told, via publishSync, so they
    // stay in sync.
    if (options.notify !== false) {
      window.dispatchEvent(new CustomEvent(mutationEventName, { detail: { path, method } }));
    }
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
  const serverFileName = getContentDispositionFileName(response.headers.get("content-disposition"));
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = serverFileName || fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  return serverFileName || fileName;
}

function getContentDispositionFileName(headerValue) {
  if (!headerValue) return null;
  const encoded = headerValue.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  if (encoded) {
    try { return decodeURIComponent(encoded.replace(/^"|"$/g, "")); } catch { return encoded; }
  }
  return headerValue.match(/filename="?([^";]+)"?/i)?.[1] || null;
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
    pill.textContent = foundationT(health.status === "Healthy" ? "app.status.apiHealthy" : "app.status.apiDegraded");
    pill.className = `status-pill ${health.status === "Healthy" ? "status-ok" : "status-warn"}`;
  } catch {
    pill.textContent = foundationT("app.message.aPIOffline");
    pill.className = "status-pill status-warn";
  }
}

function fetchHealth(baseUrl = "") {
  return fetch(`${baseUrl}/health`, { headers: apiHeaders(), credentials: "include", cache: "no-store" });
}

async function resolveApiBase() {
  apiBase = "";
  return apiBase;
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
  const renderGeneration = ++routeRenderGeneration;
  activeRouteController.abort();
  activeRouteController = new AbortController();
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

  // Hash changes and auth restoration can overlap. A superseded render must
  // never replace the workspace selected by the latest route.
  if (renderGeneration !== routeRenderGeneration || path !== currentPath()) {
    return;
  }

  document.getElementById("page-title").textContent = foundationT(route.i18nTitle);
  document.getElementById("route-label").textContent = foundationT(route.i18nLabel);
  renderNav(auth);
  renderSession(auth);
  updateNotificationBadge();
  await route.render();
  if (renderGeneration !== routeRenderGeneration || path !== currentPath()) {
    return;
  }
  const view = document.getElementById("view");
  // Route rendering can replace the view after the initial shell pass. Apply
  // the semantic dictionary to the shell again so static controls never leak
  // translation keys (for example `common.signOut`) into the live UI.
  applyLanguage(document.body);
  applyStaticShellLanguage();
  applyLanguage(view);
  sanitizeVisibleIdentifiers(view);
  scheduleRouteRefresh(path);
  await applyNotificationFocus();
  sanitizeVisibleIdentifiers(view);
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
      notice(foundationT("app.message.thisSecureLinkIsUnavailableOrHasExpired"), "warning");
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
  nav.replaceChildren();
  if (!auth) {
    const signIn = document.createElement("a");
    signIn.href = "#/login";
    signIn.setAttribute("aria-current", "page");
    signIn.textContent = foundationT("common.signIn");
    nav.appendChild(signIn);
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
    const groupLabel = document.createElement("p");
    groupLabel.className = "nav-group-label";
    groupLabel.textContent = foundationT(navGroupTranslationKeys.get(group.label) || group.label);
    groupNode.appendChild(groupLabel);

    for (const href of visibleItems) {
      const link = document.createElement("a");
      link.href = `#${href}`;
      link.textContent = foundationT(navItemTranslationKeys.get(href) || routes[href].i18nLabel || "common.unknown");
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
  session.textContent = auth ? `${roleLabel(auth.user.role)}${auth.user.locationId ? ` - ${foundationT("app.status.locationScoped")}` : ""}` : foundationT("app.status.notSignedIn");
  document.getElementById("logout-button").hidden = !auth;
  document.getElementById("sidebar-toggle").hidden = !auth;
}

function setSidebarOpen(open) {
  document.body.classList.toggle("sidebar-open", open);
  const toggle = document.getElementById("sidebar-toggle");
  if (!toggle) return;
  toggle.setAttribute("aria-expanded", String(open));
  const label = foundationT(open ? "navigation.close" : "navigation.open");
  toggle.setAttribute("aria-label", label);
  toggle.title = label;
}

function pageIntro({ eyebrow, title, body = "", metrics = "" }) {
  return `
    <section class="page-intro">
      <div>
        <p class="eyebrow">${escapeHtml(uiText(eyebrow))}</p>
        <h2>${escapeHtml(uiText(title))}</h2>
        ${body ? `<p>${escapeHtml(uiText(body))}</p>` : ""}
      </div>
      ${metrics ? `<div class="rail-metrics">${metrics}</div>` : ""}
    </section>`;
}

function statusChip(label, tone = "muted", id = null) {
  const idAttribute = id ? ` id="${escapeHtml(id)}"` : "";
  return `<span${idAttribute} class="status-pill status-${escapeHtml(tone)}">${escapeHtml(uiText(label))}</span>`;
}

function emptyState(message, actionHtml = "") {
  return `<div class="empty-state"><span>${escapeHtml(uiText(message))}</span>${actionHtml}</div>`;
}

function segmentedControl(items) {
  return `<div class="segmented-control" role="tablist">${items.map((item, index) => `<button type="button" data-scroll-target="${escapeHtml(item.target)}"${item.view ? ` data-payment-view="${escapeHtml(item.view)}"` : ""} role="tab" aria-selected="${index === 0 ? "true" : "false"}">${escapeHtml(uiText(item.label))}</button>`).join("")}</div>`;
}

function applyPaymentsView(view = "merchant") {
  document.querySelectorAll("[data-payment-panel]").forEach((panel) => {
    const views = String(panel.dataset.paymentPanel || "").split(/\s+/).filter(Boolean);
    panel.hidden = !views.includes(view);
  });
  const tools = document.getElementById("payment-tools-section");
  if (tools) tools.hidden = !["merchant", "tools", "audit"].includes(view);
}

function notice(message, tone = "info") {
  const area = document.getElementById("notification-area");
  if (!area) return;
  const id = `notice-${++noticeSequence}`;
  const title = ({ success: "Success", error: "Error", warning: "Warning" }[tone]) || "Notice";
  const node = document.createElement("div");
  node.className = `notice notice-${tone}`;
  node.id = id;
  node.setAttribute("role", tone === "error" ? "alertdialog" : "dialog");
  node.setAttribute("aria-live", tone === "error" ? "assertive" : "polite");
  node.setAttribute("aria-label", uiText(title));
  node.innerHTML = `
    <div class="notice-content">
      <strong class="notice-title">${escapeHtml(uiText(title))}</strong>
      <span>${escapeHtml(displaySafeText(uiText(message)))}</span>
    </div>
    <button class="notice-close" type="button" aria-label="${escapeHtml(foundationT("app.dismissNotice"))}">x</button>`;
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
          <div><h2>${escapeHtml(uiText(title))}</h2><p class="muted-text">${escapeHtml(uiText(label))}</p></div>
        </div>
        <div class="field">
          ${multiline
            ? `<textarea class="input dialog-input" rows="4">${escapeHtml(defaultValue)}</textarea>`
            : `<input class="input dialog-input" type="${escapeHtml(inputType)}" value="${escapeHtml(defaultValue)}">`}
        </div>
        <div class="form-actions">
          <button class="button primary" type="submit">${escapeHtml(foundationT("app.continue"))}</button>
          <button class="button secondary" type="button" data-dialog-cancel>${escapeHtml(foundationT("common.cancel"))}</button>
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
  headers.set(requestMarkerHeader, "fetch");
}

function confirmDialog({ title, message, confirmLabel = "Confirm", cancelLabel = "Cancel", tone = "default", bodyHtml = "", translateMessage = true, translateTitle = true }) {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.className = "dialog-overlay";
    overlay.innerHTML = `
      <section class="dialog-card confirm-dialog ${tone === "warning" ? "confirm-dialog-warning" : ""}" role="dialog" aria-modal="true" aria-labelledby="confirm-dialog-title">
        <div class="section-head tight-head">
          <div>
            <h2 id="confirm-dialog-title">${escapeHtml(translateTitle ? uiText(title) : String(title ?? ""))}</h2>
            <p class="muted-text">${escapeHtml(translateMessage ? uiText(message) : String(message ?? ""))}</p>
          </div>
        </div>
        ${bodyHtml ? `<div class="confirm-dialog-body">${bodyHtml}</div>` : ""}
        <div class="form-actions">
          <button class="button primary" type="button" data-dialog-confirm>${escapeHtml(uiText(confirmLabel))}</button>
          <button class="button secondary" type="button" data-dialog-cancel>${escapeHtml(uiText(cancelLabel))}</button>
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
    if (document.hidden) return;
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
        await loadMerchants();
        break;
      case "/operations":
        await loadOperations();
        break;
      case "/payments":
        await Promise.all([loadPayments(), loadPaymentHistory()]);
        break;
      case "/notifications":
        // renderNotifications already performs the initial notification load.
        // Starting a second concurrent load here can replace the card DOM after
        // the user has begun interacting with it.
        if (["Admin", "ERPAdmin", "CLevel"].includes(getAuth()?.user.role)) {
          await loadMerchantExpiryRecalls();
        }
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
    link.textContent = count > 0 ? foundationT("app.status.notificationsCount", { count }) : foundationT("navigation.notifications");
  } catch (error) {
    link.textContent = foundationT("navigation.notifications");
    if (error?.status === 401) {
      clearAuth();
    }
  } finally {
    notificationBadgeInFlight = false;
  }
}

function renderLogin() {
  document.getElementById("notification-area")?.replaceChildren();
  document.getElementById("view").innerHTML = `
    <section class="auth-layout">
      <div class="auth-copy">
        <div class="auth-copy-top">
          <span class="auth-wordmark">Lensee</span>
          <button class="button secondary auth-language-toggle" id="login-language-toggle" type="button">${currentLanguage === "ar" ? "English" : "العربية"}</button>
        </div>
        <div class="auth-kicker">${escapeHtml(foundationT("app.operationsErp"))}</div>
        <h2>Lensee</h2>
        <p>${escapeHtml(foundationT("app.secureErpAccess"))}</p>
        <div class="auth-status"><span id="login-health-dot" class="health-dot"></span><span id="login-health-text">${escapeHtml(foundationT("app.checkingApi"))}</span></div>
      </div>
      <form class="auth-panel" id="login-form">
        <div class="auth-panel-head">
          <span>${escapeHtml(foundationT("app.authorizedSession"))}</span>
          <strong>${escapeHtml(foundationT("common.signIn"))}</strong>
        </div>
        <div class="field"><label for="username">${escapeHtml(foundationT("app.username"))}</label><input class="input" id="username" name="username" autocomplete="username" required autofocus></div>
        <div class="field"><label for="password">${escapeHtml(foundationT("app.password"))}</label><div class="password-field"><input class="input" id="password" name="password" type="password" autocomplete="current-password" required><button class="button secondary inline-icon password-toggle" id="toggle-password" type="button" aria-label="${escapeHtml(foundationT("app.showPassword"))}" title="${escapeHtml(foundationT("app.showPassword"))}">${escapeHtml(foundationT("app.show"))}</button></div></div>
        <div class="login-error" id="login-error" role="alert" hidden></div>
        <button class="button auth-submit" id="login-submit" type="submit">${escapeHtml(foundationT("common.signIn"))}</button>
      </form>
    </section>`;

  checkLoginHealth();
  document.getElementById("toggle-password").addEventListener("click", () => {
    const password = document.getElementById("password");
    const isHidden = password.type === "password";
    password.type = isHidden ? "text" : "password";
    document.getElementById("toggle-password").textContent = foundationT(isHidden ? "common.hide" : "common.show");
  });
  document.getElementById("login-form").addEventListener("submit", login);
}

async function login(event) {
  event.preventDefault();
  const submit = document.getElementById("login-submit");
  const error = document.getElementById("login-error");
  const form = new FormData(event.currentTarget);
  error.hidden = true;
  submit.disabled = true;
  submit.textContent = foundationT("app.message.signingIn");
  try {
    const auth = await loginRequest(apiBase, {
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
    submit.textContent = foundationT("common.signIn");
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
    text.textContent = foundationT(health.status === "Healthy" ? "app.status.apiHealthy" : "app.status.apiDegraded");
  } catch {
    dot.className = "health-dot health-warn";
    text.textContent = foundationT("app.message.aPIOffline");
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
        "/catalog": foundationT("dashboard.workspace.catalog"),
        "/inventory": foundationT("dashboard.workspace.inventory"),
        "/supply": foundationT("dashboard.workspace.supply"),
        "/crm": foundationT("dashboard.workspace.crm"),
        "/operations": foundationT("dashboard.workspace.operations"),
        "/payments": foundationT("dashboard.workspace.payments"),
        "/finance": foundationT("dashboard.workspace.finance"),
        "/notifications": foundationT("dashboard.workspace.notifications"),
        "/reports": foundationT("dashboard.workspace.reports"),
        "/stocktakes": foundationT("dashboard.workspace.stocktakes"),
        "/admin": foundationT("dashboard.workspace.admin")
      };
      return workspaceCard(href, label, descriptions[href] || foundationT("dashboard.workspace.open"), workspaceTone(href));
    })
    .join("");

  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: foundationT("dashboard.overview"),
      title: foundationT("dashboard.title"),
      body: foundationT("dashboard.intro", { role: dashboardPrimaryResponsibility(currentRole) }),
      metrics: `
        ${scenarioCard("Open work", "Loading", "status-muted", "dashboard-open-work")}
        ${scenarioCard("Open confirmations", "Loading", "status-muted", "dashboard-open-confirmations")}
        ${scenarioCard("Unread alerts", "Loading", "status-muted", "dashboard-unread-alerts")}
        ${["Admin", "CLevel"].includes(currentRole) ? `${scenarioCard("Total sales", "Loading", "status-muted", "dashboard-total-sales")}${scenarioCard("Actual total I have", "Loading", "status-muted", "dashboard-actual-collected")}${scenarioCard("Remaining", "Loading", "status-muted", "dashboard-remaining-receivable")}` : ""}
      `
    })}
    
    ${["Admin", "CLevel"].includes(currentRole) ? `<section class="workspace-panel"><div class="section-head"><h3>${financeT("executive.title")}</h3><div class="form-grid compact-form"><label>${financeT("executive.period")}<select id="finance-summary-period" class="select"><option value="daily">${financeT("executive.daily")}</option><option value="monthly">${financeT("executive.monthly")}</option></select></label><label>${financeT("businessDate")}<input id="finance-summary-date" class="input" type="date"></label></div></div><div id="finance-executive-summary">${foundationT("common.loading")}</div></section>` : ""}
    <section class="command-grid">
      <a class="command-tile command-tile-daily" href="#/operations">
        <span>${foundationT("dashboard.dailyWork")}</span>
        <strong>${foundationT("dashboard.operationsQueue")}</strong>
        <small>${foundationT("dashboard.operationsHelp")}</small>
      </a>
      ${routes["/payments"].roles.includes(currentRole) ? `<a class="command-tile command-tile-money" href="#/payments"><span>${foundationT("dashboard.money")}</span><strong>${foundationT("dashboard.confirmationsQueue")}</strong><small>${foundationT("dashboard.paymentsHelp")}</small></a>` : ""}
      <a class="command-tile command-tile-stock" href="#/inventory">
        <span>${foundationT("dashboard.stock")}</span>
        <strong>${foundationT("dashboard.stockAttention")}</strong>
        <small>${foundationT("dashboard.stockHelp")}</small>
      </a>
      <a class="command-tile command-tile-oversight" href="#/reports">
        <span>${foundationT("dashboard.oversight")}</span>
        <strong>${foundationT("dashboard.reportsExports")}</strong>
        <small>${foundationT("dashboard.reportsHelp")}</small>
      </a>
    </section>

    <section class="band rail-band">
      <div class="section-head">
        <div>
          <h2>${foundationT("dashboard.workspaceMap")}</h2>
        </div>
      </div>
      <div class="workspace-card-grid">${visibleWorkspaces}</div>
    </section>`;

  if (["Admin", "CLevel"].includes(currentRole)) {
    loadDashboardFinancialSummary();
    loadFinanceExecutiveSummary();
    document.getElementById("finance-summary-period")?.addEventListener("change", loadFinanceExecutiveSummary);
    document.getElementById("finance-summary-date")?.addEventListener("change", loadFinanceExecutiveSummary);
  }
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
    sales.textContent = foundationT("app.message.unavailable");
    actual.textContent = foundationT("app.message.unavailable");
    remaining.textContent = foundationT("app.message.unavailable");
  }
}

function renderCatalog() {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  document.getElementById("view").innerHTML = `
    <section class="catalog-hero">
      <div>
        <p class="eyebrow">${escapeHtml(foundationT("navigation.catalog"))}</p>
        <h2>${escapeHtml(foundationT("app.catalogMasterData"))}</h2>
        <p>${escapeHtml(foundationT("app.catalogManageDescription"))}</p>
      </div>
      <div class="scenario-grid">
        ${scenarioCard(foundationT("app.catalogRole"), canWrite ? foundationT("app.catalogCanEdit") : foundationT("app.catalogViewOnly"), canWrite ? "status-ok" : "status-muted")}
        ${scenarioCard(foundationT("app.catalogProductScope"), foundationT("app.catalogProductsAndSkus"), "status-muted")}
        ${scenarioCard(foundationT("app.catalogReferenceData"), foundationT("app.catalogCategoriesAndBrands"), "status-muted")}
      </div>
    </section>

    <section class="catalog-layout">
      <aside class="catalog-side">
        <section class="band compact-band">
          <div class="section-head"><h2>${escapeHtml(foundationT("app.catalogFilters"))}</h2><button id="catalog-refresh" class="button secondary" type="button">${escapeHtml(foundationT("common.refresh"))}</button></div>
          <div class="field"><label for="catalog-search">${escapeHtml(foundationT("app.catalogSearch"))}</label><input id="catalog-search" class="input" placeholder="${escapeHtml(foundationT("app.catalogSearchPlaceholder"))}"></div>
          <label class="check-field"><input id="catalog-include-inactive" type="checkbox" checked><span>${escapeHtml(foundationT("app.catalogShowInactive"))}</span></label>
          <div class="muted-text" id="catalog-count">${escapeHtml(foundationT("common.loading"))}</div>
        </section>
      </aside>

      <section class="catalog-main">
        <section class="band">
          <div class="section-head"><h2>${escapeHtml(foundationT("app.catalogProducts"))}</h2><span class="status-pill ${canWrite ? "status-ok" : "status-muted"}">${escapeHtml(uiText(canWrite ? "Writable" : "Read only"))}</span></div>
          <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.catalogName"))}</th><th>${escapeHtml(foundationT("payments.type"))}</th><th>${escapeHtml(foundationT("app.catalogBrand"))}</th><th>${escapeHtml(foundationT("finance.expenses.category"))}</th><th>${escapeHtml(foundationT("app.catalogPack"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th>${canWrite ? `<th>${escapeHtml(foundationT("payments.actions"))}</th>` : ""}</tr></thead><tbody id="catalog-products"><tr><td colspan="${canWrite ? 7 : 6}">${escapeHtml(foundationT("app.catalogLoading"))}</td></tr></tbody></table></div>
        </section>
        <section class="catalog-detail-grid">
          <section class="band" id="catalog-detail"><h2>${escapeHtml(foundationT("app.catalogProductDetail"))}</h2><p class="muted-text">${escapeHtml(foundationT("app.catalogSelectProduct"))}</p></section>
          ${canWrite ? renderCatalogWritePanel() : `<section class="band"><h2>${escapeHtml(foundationT("app.catalogAccess"))}</h2><p class="muted-text">${escapeHtml(foundationT("app.catalogReadOnlyHelp"))}</p></section>`}
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
    openWork.textContent = foundationT("app.message.unavailable");
    openConfirmations.textContent = foundationT("app.message.unavailable");
    unreadAlerts.textContent = foundationT("app.message.unavailable");
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
      ["MerchantAccount", "CashHandToHand", "CashTransaction"].includes(log.paymentMethod) &&
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
  return `<div class="scenario-card"><span>${escapeHtml(uiText(title))}</span><strong${idAttribute} class="${escapeHtml(tone)}">${escapeHtml(uiText(value))}</strong></div>`;
}

function workspaceCard(href, title, description, tone = "neutral") {
  return `<a class="workspace-card workspace-card-${escapeHtml(tone)}" href="#${escapeHtml(href)}"><strong>${escapeHtml(uiText(title))}</strong><span>${escapeHtml(uiText(description))}</span></a>`;
}

function workspaceTone(href) {
  if (["/operations", "/notifications"].includes(href)) return "daily";
  if (["/payments", "/reports"].includes(href)) return "money";
  if (["/inventory", "/catalog", "/stocktakes"].includes(href)) return "stock";
  return "oversight";
}

function dashboardPrimaryResponsibility(role) {
  return {
    Admin: foundationT("dashboard.role.admin"),
    ERPAdmin: foundationT("dashboard.role.admin"),
    CLevel: foundationT("dashboard.role.executive"),
    Accountant: foundationT("dashboard.role.accountant"),
    WarehouseClerk: foundationT("dashboard.role.warehouse")
  }[role] || foundationT("dashboard.role.default");
}

function isSystemAdminRole(role) {
  // C-Level users have executive Finance read access, but
  // they are not catalogue, inventory, user, or configuration administrators.
  // Those paths are governed by their dedicated permissions and must never be
  // unlocked by a broad presentation-only role predicate.
  return role === "Admin" || role === "ERPAdmin";
}

function renderCatalogWritePanel() {
  return `
    <section class="write-stack">
      <section class="band">
        <div class="section-head"><h2>${escapeHtml(foundationT("app.catalogProductEditor"))}</h2><button class="button secondary" id="product-reset" type="button">${escapeHtml(foundationT("app.catalogNew"))}</button></div>
        <form class="form wide-form" id="product-form">
          <input type="hidden" id="product-id">
          <div class="form-error" id="product-error" hidden></div>
          <div class="form-grid">
            <div class="field"><label for="product-name">${escapeHtml(foundationT("app.catalogName"))}</label><input id="product-name" class="input" required></div>
            <div class="field"><label for="product-type">${escapeHtml(foundationT("payments.type"))}</label><select id="product-type" class="select"><option value="Lens">${escapeHtml(foundationT("app.catalogLens"))}</option><option value="Solution">${escapeHtml(foundationT("app.catalogSolution"))}</option></select></div>
            <div class="field"><label for="product-category">${escapeHtml(foundationT("finance.expenses.category"))}</label><select id="product-category" class="select" required></select></div>
            <div class="field"><label for="product-brand">${escapeHtml(foundationT("app.catalogBrand"))}</label><select id="product-brand" class="select" required></select></div>
            <div class="field"><label for="product-sell-mode">${escapeHtml(foundationT("app.sellMode"))}</label><select id="product-sell-mode" class="select"><option value="SinglePiece">${escapeHtml(foundationT("app.catalogSinglePiece"))}</option><option value="SealedPackOnly">${escapeHtml(foundationT("app.catalogSealedPackOnly"))}</option><option value="Both">${escapeHtml(foundationT("app.catalogBoth"))}</option></select></div>
            <div class="field"><label for="product-pieces">${escapeHtml(foundationT("app.piecesPerPack"))}</label><input id="product-pieces" class="input" type="number" min="1" value="1"></div>
            <div class="field"><label for="product-expiry">${escapeHtml(foundationT("app.catalogExpirySource"))}</label><select id="product-expiry" class="select"><option value="Batch">${escapeHtml(foundationT("app.catalogBatchExpiry"))}</option><option value="None">${escapeHtml(foundationT("app.catalogNoBatchExpiry"))}</option></select></div>
            <div class="field"><label for="product-duration-value">${escapeHtml(foundationT("app.catalogValidFor"))}</label><input id="product-duration-value" class="input" type="number" min="1" step="1" value="6"></div>
            <div class="field"><label for="product-duration-unit">${escapeHtml(foundationT("app.catalogDurationUnit"))}</label><select id="product-duration-unit" class="select"><option value="Daily">${escapeHtml(foundationT("app.catalogDays"))}</option><option value="Monthly" selected>${escapeHtml(foundationT("app.catalogMonths"))}</option><option value="Annual">${escapeHtml(foundationT("app.catalogYears"))}</option></select></div>
            <input type="hidden" id="product-clinical">
          </div>
          
          
            <div class="form-actions"><button class="button" id="product-submit" type="submit">${escapeHtml(foundationT("app.catalogCreateProduct"))}</button><span class="muted-text" id="product-mode">${escapeHtml(foundationT("app.catalogNewProduct"))}</span></div>
        </form>
      </section>

      <section class="catalog-admin-grid">
        <section class="band compact-band">
          <div class="section-head"><h2>${escapeHtml(foundationT("app.inline.categories"))}</h2><button class="button secondary" id="category-reset" type="button">${escapeHtml(foundationT("app.catalogNew"))}</button></div>
          <form class="form" id="category-form">
            <input type="hidden" id="category-id">
            <div class="form-error" id="category-error" hidden></div>
            <div class="field"><label for="category-name">${escapeHtml(foundationT("app.catalogName"))}</label><input id="category-name" class="input" required></div>
            <div class="field"><label for="category-parent">${escapeHtml(foundationT("app.inline.parent"))}</label><select id="category-parent" class="select"><option value="">${escapeHtml(foundationT("app.inline.none"))}</option></select></div>
            <div class="form-actions"><button class="button" id="category-submit" type="submit">${escapeHtml(foundationT("app.inline.createCategory"))}</button><span class="muted-text" id="category-mode">${escapeHtml(foundationT("app.inline.newCategory"))}</span></div>
          </form>
          <div class="tree-list" id="category-list"></div>
        </section>
        <section class="band compact-band">
          <div class="section-head"><h2>${escapeHtml(foundationT("app.inline.brands"))}</h2><button class="button secondary" id="brand-reset" type="button">${escapeHtml(foundationT("app.catalogNew"))}</button></div>
          <form class="form" id="brand-form">
            <input type="hidden" id="brand-id">
            <div class="form-error" id="brand-error" hidden></div>
            <div class="field"><label for="brand-name">${escapeHtml(foundationT("app.catalogName"))}</label><input id="brand-name" class="input" required></div>
            <div class="form-actions"><button class="button" id="brand-submit" type="submit">${escapeHtml(foundationT("app.inline.createBrand"))}</button><span class="muted-text" id="brand-mode">${escapeHtml(foundationT("app.inline.newBrand"))}</span></div>
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
    categoryList.querySelectorAll("[data-category-toggle]").forEach((toggle) => {
      toggle.addEventListener("click", () => {
        const children = document.getElementById(toggle.dataset.categoryToggle);
        if (!children) return;
        const expanded = toggle.getAttribute("aria-expanded") === "true";
        toggle.setAttribute("aria-expanded", String(!expanded));
        toggle.closest(".category-tree-node")?.setAttribute("aria-expanded", String(!expanded));
        children.hidden = expanded;
        toggle.querySelector(".category-tree-chevron")?.classList.toggle("is-collapsed", expanded);
      });
    });
    categoryList.querySelectorAll("[data-category-id]").forEach((button) => {
      button.addEventListener("click", () => {
        const category = catalogCategories.find((value) => value.id === button.dataset.categoryId);
        if (category) {
          document.getElementById("category-id").value = category.id;
          document.getElementById("category-name").value = category.name;
          document.getElementById("category-parent").value = category.parentId || "";
          document.getElementById("category-submit").textContent = foundationT("app.message.updateCategory");
          document.getElementById("category-mode").textContent = foundationT("app.editingNamedRecord", { name: category.name });
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
          document.getElementById("brand-submit").textContent = foundationT("app.message.updateBrand");
          document.getElementById("brand-mode").textContent = foundationT("app.editingNamedRecord", { name: brand.name });
          clearFormError("brand-error");
          document.getElementById("brand-name").focus();
        }
      });
    });
  }
}

function renderCategoryTree(nodes) {
  if (nodes.length === 0) return `<p class="muted-text">${escapeHtml(foundationT("app.inline.noCategories"))}</p>`;
  return `<ul class="category-tree" role="tree">${nodes.map((node) => renderCategoryTreeNode(node, 0)).join("")}</ul>`;
}

function renderCategoryTreeNode(node, depth) {
  const children = node.children || [];
  const hasChildren = children.length > 0;
  const childrenId = `category-children-${node.id}`;
  return `
    <li class="category-tree-node${depth > 0 ? " is-nested" : ""}" role="treeitem" aria-level="${depth + 1}" aria-expanded="${hasChildren ? "true" : "false"}">
      <div class="category-tree-item">
        ${hasChildren
          ? `<button class="category-tree-toggle" type="button" data-category-toggle="${escapeHtml(childrenId)}" aria-expanded="true" aria-controls="${escapeHtml(childrenId)}"><span class="category-tree-chevron" aria-hidden="true">▾</span><span class="sr-only">${escapeHtml(foundationT("app.toggle"))} ${escapeHtml(node.name)}</span></button>`
          : `<span class="category-tree-spacer" aria-hidden="true"></span>`}
        <span class="category-tree-name">${escapeHtml(node.name)}</span>
        <button class="chip category-tree-edit" type="button" data-category-id="${escapeHtml(node.id)}">${escapeHtml(foundationT("app.catalogEdit"))}</button>
      </div>
      ${hasChildren ? `<ul class="category-tree-children" id="${escapeHtml(childrenId)}" role="group">${children.map((child) => renderCategoryTreeNode(child, depth + 1)).join("")}</ul>` : ""}
    </li>`;
}

function fillCategorySelect(select, includeEmpty) {
  const current = select.value;
  const options = [];
  flattenCategoryOptions(categoryTree, options);
  select.innerHTML = includeEmpty ? `<option value="">${escapeHtml(foundationT("app.inline.none"))}</option>` : "";
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

  tbody.innerHTML = `<tr><td colspan="${canWrite ? 7 : 6}">${escapeHtml(foundationT("app.catalogLoading"))}</td></tr>`;
  try {
    const result = await request(`/api/v1/catalog/products?${params}`);
    count.textContent = foundationT(result.totalCount === 1 ? "app.count.product" : "app.count.products", { count: result.totalCount });
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="${canWrite ? 7 : 6}">${escapeHtml(foundationT("app.catalogNoProducts"))}</td></tr>`
      : result.items.map((product) => `
        <tr class="click-row ${product.id === selectedProductId ? "selected-row" : ""}" data-product-id="${escapeHtml(product.id)}">
          <td>${escapeHtml(product.name)}</td><td>${escapeHtml(uiText(product.productType))}</td><td>${escapeHtml(product.brandName)}</td>
          <td>${escapeHtml(product.categoryName)}</td><td>${formatPackHint(product)}</td>
          <td><span class="status-pill ${product.isActive ? "status-ok" : "status-muted"}">${escapeHtml(uiText(product.isActive ? "Active" : "Inactive"))}</span></td>
          ${canWrite ? `<td><button class="button secondary table-action" type="button" data-product-edit="${escapeHtml(product.id)}">${escapeHtml(foundationT("app.catalogEdit"))}</button></td>` : ""}
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
  detail.innerHTML = `<h2>${escapeHtml(foundationT("app.catalogProductDetail"))}</h2><p>${escapeHtml(foundationT("app.catalogLoadingProduct"))}</p>`;
  try {
    const product = await request(`/api/v1/catalog/products/${productId}`);
    detail.innerHTML = `
      <div class="section-head">
        <div><h2>${escapeHtml(product.name)}</h2><p class="muted-text">${escapeHtml(product.brandName)} - ${escapeHtml(product.categoryName)}</p></div>
        <div class="inline-actions">
          <span class="status-pill ${product.isActive ? "status-ok" : "status-muted"}">${escapeHtml(uiText(product.isActive ? "Active" : "Inactive"))}</span>
          ${canWrite ? `<button class="button secondary" id="edit-product" type="button">${escapeHtml(foundationT("app.catalogEdit"))}</button><button class="button secondary" id="toggle-product" type="button">${escapeHtml(uiText(product.isActive ? "Deactivate" : "Reactivate"))}</button>` : ""}
        </div>
      </div>
      <div class="detail-grid">
        <div><span>${escapeHtml(foundationT("payments.type"))}</span><strong>${escapeHtml(uiText(product.productType))}</strong></div>
        <div><span>${escapeHtml(foundationT("app.sellMode"))}</span><strong>${escapeHtml(product.sellMode ? uiText(product.sellMode) : foundationT("app.notSet"))}</strong></div>
        <div><span>${escapeHtml(foundationT("app.piecesPerPack"))}</span><strong>${escapeHtml(product.piecesPerPack || foundationT("app.notSet"))}</strong></div>
        <div><span>${escapeHtml(foundationT("app.catalogExpiry"))}</span><strong>${escapeHtml(product.expiryType ? uiText(product.expiryType) : foundationT("app.notSet"))}</strong></div>
        <div><span>${escapeHtml(foundationT("app.catalogOpeningValidity"))}</span><strong>${escapeHtml(formatOpeningValidity(product))}</strong></div>
      </div>
      <p class="muted-text">${escapeHtml(foundationT("app.catalogBatchExpiryHelp"))}</p>
      ${renderSkuSection(product, canWrite)}`;
    if (canWrite) {
      wireProductAdminActions(product);
    }
    await loadCatalogProducts();
  } catch (exception) {
    detail.innerHTML = `<h2>${escapeHtml(foundationT("app.catalogProductDetail"))}</h2><p>${escapeHtml(getFriendlyApiError(exception))}</p>`;
  }
}

function formatOpeningValidity(product) {
  const opened = product.openedExpiryDuration || product.sealedExpiryDuration;
  const rate = product.openedExpiryRate;
  if (opened && rate) return `${opened} (${rate})`;
  return opened || rate || foundationT("app.notSet");
}

function renderSkuSection(product, canWrite) {
  return `
    <h3>${escapeHtml(foundationT("app.inline.sKUs"))}</h3>
    ${canWrite ? `
      <form class="form wide-form compact-form" id="sku-form">
        <input type="hidden" id="sku-id"><div class="form-error" id="sku-error" hidden></div>
        <div class="form-grid">
          <div class="sku-preview"><span>${escapeHtml(foundationT("app.inline.generatedSKU"))}</span><strong id="sku-code-preview">${escapeHtml(foundationT("app.inline.derivedAfterSave"))}</strong></div>
          <div class="field"><label for="sku-power-sign">${escapeHtml(foundationT("app.inline.powerSign"))}</label><select id="sku-power-sign" class="select"><option value="">${escapeHtml(foundationT("app.inline.none"))}</option><option value="+">+</option><option value="-">-</option></select></div>
          <div class="field"><label for="sku-power-value">${escapeHtml(foundationT("app.inline.powerValue"))}</label><input id="sku-power-value" class="input" type="number" step="0.25" min="0"></div>
          <div class="field"><label for="sku-color">${escapeHtml(foundationT("app.inline.color"))}</label><input id="sku-color" class="input"></div>
          <div class="field"><label for="sku-size">${escapeHtml(foundationT("app.inline.size"))}</label><input id="sku-size" class="input"></div>
          <div class="field"><label for="sku-barcode">${escapeHtml(foundationT("app.inline.barcode"))}</label><input id="sku-barcode" class="input"></div>
        </div>
        <div class="form-actions"><button class="button" type="submit">${escapeHtml(foundationT("app.inline.saveSKU"))}</button><button class="button secondary" id="sku-reset" type="button">${escapeHtml(foundationT("app.inline.clear"))}</button></div>
      </form>` : ""}
    <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("app.inline.power"))}</th><th>${escapeHtml(foundationT("app.inline.color"))}</th><th>${escapeHtml(foundationT("app.inline.size"))}</th><th>${escapeHtml(foundationT("app.inline.barcode"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th>${canWrite ? `<th>${escapeHtml(foundationT("payments.actions"))}</th>` : ""}</tr></thead><tbody>
      ${product.skus.length === 0 ? `<tr><td colspan="${canWrite ? 7 : 6}">${escapeHtml(foundationT("app.inline.noSKUs"))}</td></tr>` : product.skus.map((sku) => `
        <tr><td>${escapeHtml(sku.skuCode)}</td><td>${escapeHtml(formatPower(sku))}</td><td>${escapeHtml(sku.colorName || "-")}</td><td>${escapeHtml(sku.size || "-")}</td><td>${escapeHtml(sku.barcode || "-")}</td>
        <td><span class="status-pill ${sku.isActive ? "status-ok" : "status-muted"}">${escapeHtml(uiText(sku.isActive ? "Active" : "Inactive"))}</span></td>
        ${canWrite ? `<td><button class="button secondary table-action" type="button" data-edit-sku="${escapeHtml(sku.id)}">${escapeHtml(foundationT("app.catalogEdit"))}</button><button class="button secondary table-action" type="button" data-toggle-sku="${escapeHtml(sku.id)}">${escapeHtml(uiText(sku.isActive ? "Deactivate" : "Reactivate"))}</button></td>` : ""}</tr>`).join("")}
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
    invalidateOperationSkuCaches();
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

function invalidateOperationSkuCaches() {
  operationSkuOptions = [];
  operationProductOptions = [];
  operationAvailableSkuIds = null;
  supplySkuSearchIndex = [];
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
    return foundationT("app.validation.mustBeValidJson", { label: uiText(label) });
  }
}

function readProductForm() {
  const type = canonicalSelectValue("product-type", "productType");
  const pieces = document.getElementById("product-pieces").value;
  const clinicalParams = buildClinicalParamsFromForm();
  const durationValue = document.getElementById("product-duration-value")?.value;
  const openedExpiryRate = canonicalSelectValue("product-duration-unit", "durationUnit");
  return {
    categoryId: document.getElementById("product-category").value,
    brandId: document.getElementById("product-brand").value,
    name: document.getElementById("product-name").value,
    productType: type,
    expiryType: canonicalSelectValue("product-expiry", "expiryType"),
    sealedExpiryDuration: null,
    openedExpiryRate: type === "Solution" ? null : openedExpiryRate,
    openedExpiryDuration: type === "Solution" || !durationValue ? null : buildDuration(durationValue, openedExpiryRate),
    piecesPerPack: pieces ? Number(pieces) : null,
    sellMode: canonicalSelectValue("product-sell-mode", "sellMode"),
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
  document.getElementById("product-submit").textContent = foundationT("app.message.updateProduct");
  document.getElementById("product-mode").textContent = foundationT("app.editingNamedRecord", { name: product.name });
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
  document.getElementById("product-submit").textContent = foundationT("app.catalogCreateProduct");
  document.getElementById("product-mode").textContent = foundationT("app.catalogNewProduct");
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
    preview.textContent = foundationT("app.inline.derivedAfterSave");
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
  document.getElementById("category-submit").textContent = foundationT("app.inline.createCategory");
  document.getElementById("category-mode").textContent = foundationT("app.inline.newCategory");
  clearFormError("category-error");
}

function resetBrandForm() {
  document.getElementById("brand-id").value = "";
  document.getElementById("brand-name").value = "";
  document.getElementById("brand-submit").textContent = foundationT("app.inline.createBrand");
  document.getElementById("brand-mode").textContent = foundationT("app.inline.newBrand");
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
  element.textContent = uiText(message);
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
        <p class="eyebrow">${escapeHtml(foundationT("navigation.inventory"))}</p>
        <h2>${escapeHtml(foundationT("app.inventoryHero"))}</h2>
        <p>${escapeHtml(foundationT("app.inventoryHeroHelp"))}</p>
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
          <div class="section-head"><h2>${escapeHtml(foundationT("app.catalogFilters"))}</h2><button id="inventory-refresh" class="button secondary" type="button">${escapeHtml(foundationT("common.refresh"))}</button></div>
          <div class="field"><label for="inventory-location">${escapeHtml(foundationT("app.inventoryLocation"))}</label><select id="inventory-location" class="select"><option value="">${escapeHtml(foundationT("app.inventoryAllAvailable"))}</option></select></div>
          <div class="field inventory-sku-picker"><label for="inventory-sku-search">${escapeHtml(foundationT("app.sku"))}</label>
            <input id="inventory-sku" type="hidden" value="">
            <input id="inventory-sku-search" class="input" type="search" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="inventory-sku-results" placeholder="${escapeHtml(foundationT("app.inventorySearchSku"))}">
            <div id="inventory-sku-results" class="op-line-search-results" role="listbox" hidden></div>
          </div>
          <label class="check-field"><input id="inventory-include-zero-stock" type="checkbox"><span>${escapeHtml(foundationT("app.inventoryShowZero"))}</span></label>
          <label class="check-field"><input id="inventory-include-empty" type="checkbox"><span>${escapeHtml(foundationT("app.inventoryShowEmpty"))}</span></label>
        </section>
        <section class="band compact-band">
          <h2>${escapeHtml(foundationT("app.inventoryLocations"))}</h2>
          <div id="inventory-locations" class="reference-list"><span class="muted-text">${escapeHtml(foundationT("common.loading"))}</span></div>
        </section>
      </aside>

      <section class="catalog-main">
        <section class="band">
          <div class="section-head"><h2>${escapeHtml(foundationT("app.inventoryProductTotals"))}</h2><span id="inventory-product-total-count" class="muted-text">${escapeHtml(foundationT("common.loading"))}</span></div>
          <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.product"))}</th><th>${escapeHtml(foundationT("app.inventorySkuCount"))}</th><th>${escapeHtml(foundationT("app.inventoryTotalPacks"))}</th><th>${escapeHtml(foundationT("app.inventoryTotalPieces"))}</th><th>${escapeHtml(foundationT("app.inventoryBreakdown"))}</th></tr></thead><tbody id="inventory-product-totals"><tr><td colspan="5">${escapeHtml(foundationT("app.inventoryLoadingTotals"))}</td></tr></tbody></table></div>
        </section>
        <section class="band">
          <div class="section-head"><h2>${escapeHtml(foundationT("app.inventoryStockBalances"))}</h2><span id="inventory-balance-count" class="muted-text">${escapeHtml(foundationT("common.loading"))}</span></div>
          <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.inventoryLocation"))}</th><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("payments.available"))}</th><th>${escapeHtml(foundationT("app.inline.reserved"))}</th><th>${escapeHtml(foundationT("app.inline.meantToBe"))}</th><th>${escapeHtml(foundationT("app.inline.needed"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("payments.updated"))}</th>${canWrite ? `<th>${escapeHtml(foundationT("payments.actions"))}</th>` : ""}</tr></thead><tbody id="inventory-balances"><tr><td colspan="${canWrite ? 9 : 8}">${escapeHtml(foundationT("app.inline.loadingStock"))}</td></tr></tbody></table></div>
        </section>
        <section class="band">
          <div class="section-head">
            <div><h2>${escapeHtml(foundationT("app.inventoryDailyReplenishment"))}</h2><p>${escapeHtml(foundationT("app.inventoryReplenishmentHelp"))}</p></div>
            <div class="inline-actions">
              <span id="inventory-replenishment-count" class="muted-text">${escapeHtml(foundationT("common.loading"))}</span>
              ${canWrite ? `<button id="reserve-replenishment" class="button secondary" type="button">${escapeHtml(foundationT("app.inventoryRunReplenishment"))}</button>` : ""}
            </div>
          </div>
          <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.inline.destination"))}</th><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("payments.available"))}</th><th>${escapeHtml(foundationT("app.inline.incoming"))}</th><th>${escapeHtml(foundationT("app.inline.meantToBe"))}</th><th>${escapeHtml(foundationT("app.inline.needed"))}</th><th>${escapeHtml(foundationT("app.inline.mainAvailable"))}</th></tr></thead><tbody id="inventory-replenishment"><tr><td colspan="7">${escapeHtml(foundationT("app.inline.loadingReplenishment"))}</td></tr></tbody></table></div>
        </section>
        <section class="band">
          <div class="section-head">
            <div><h2>${escapeHtml(foundationT("app.inventoryExpiredBatches"))}</h2><p>${escapeHtml(foundationT("app.inventoryExpiredHelp"))}</p></div>
            <span id="inventory-blocked-count" class="muted-text">${escapeHtml(foundationT("common.loading"))}</span>
          </div>
          <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.inventoryLocation"))}</th><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("reports.sortQuantity"))}</th><th>${escapeHtml(foundationT("app.catalogExpiry"))}</th><th>${escapeHtml(foundationT("payments.reason"))}</th></tr></thead><tbody id="inventory-blocked-batches"><tr><td colspan="6">${escapeHtml(foundationT("app.inline.loadingExpiredBatches"))}</td></tr></tbody></table></div>
        </section>
        <section class="catalog-detail-grid">
          <section class="band">
            <div class="section-head"><h2>${escapeHtml(foundationT("app.inventoryBatches"))}</h2><span id="inventory-batch-count" class="muted-text">${escapeHtml(foundationT("common.loading"))}</span></div>
            <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("app.inventoryLocation"))}</th><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("reports.sortQuantity"))}</th><th>${escapeHtml(foundationT("app.inline.expiryDate"))}</th><th>${escapeHtml(foundationT("payments.notes"))}</th></tr></thead><tbody id="inventory-batches"><tr><td colspan="6">${escapeHtml(foundationT("app.inline.loadingBatches"))}</td></tr></tbody></table></div>
          </section>
          <section class="band">
            <div class="section-head"><h2>${escapeHtml(foundationT("app.inventoryTransactions"))}</h2><span id="inventory-transaction-count" class="muted-text">${escapeHtml(foundationT("common.loading"))}</span></div>
            <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("payments.type"))}</th><th>${escapeHtml(foundationT("app.inventoryLocation"))}</th><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("app.inline.change"))}</th><th>${escapeHtml(foundationT("app.created"))}</th></tr></thead><tbody id="inventory-transactions"><tr><td colspan="5">${escapeHtml(foundationT("app.inline.loadingTransactions"))}</td></tr></tbody></table></div>
          </section>
        </section>
      </section>
    </section>`;

  document.getElementById("inventory-refresh").addEventListener("click", refreshInventoryWorkspace);
  document.getElementById("inventory-location").addEventListener("change", () => {
    selectedInventoryLocationId = document.getElementById("inventory-location").value;
    inventoryPageState = { balances: 1, batches: 1, transactions: 1, blocked: 1, replenishment: 1 };
    refreshInventoryTables();
  });
  document.getElementById("inventory-sku-search").addEventListener("input", debounce(() => {
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
    void renderInventorySkuSearchResults();
  }, 250));
  document.getElementById("inventory-sku-search").addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      hideInventorySkuSearchResults();
      event.currentTarget.blur();
    }
  });
  document.getElementById("inventory-include-zero-stock").addEventListener("change", () => { inventoryPageState.balances = 1; void loadInventoryBalances(); });
  document.getElementById("inventory-include-empty").addEventListener("change", () => { inventoryPageState.batches = 1; void loadInventoryBatches(); });
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
  const generation = ++inventoryRefreshGeneration;
  inventoryPanelObserver?.disconnect();
  loadedInventoryPanels.clear();
  await loadInventoryBalances(generation);
  if (generation !== inventoryRefreshGeneration) return;

  const panels = [
    ["inventory-product-totals", "productTotals", loadInventoryProductTotals],
    ["inventory-replenishment", "replenishment", loadInventoryReplenishment],
    ["inventory-blocked-batches", "blocked", loadTransferBlockedBatches],
    ["inventory-batches", "batches", loadInventoryBatches],
    ["inventory-transactions", "transactions", loadInventoryTransactions]
  ];
  const loadPanel = (name, loader) => {
    if (loadedInventoryPanels.has(name) || generation !== inventoryRefreshGeneration) return;
    loadedInventoryPanels.add(name);
    void loader(generation);
  };
  if (!("IntersectionObserver" in window)) {
    panels.forEach(([, name, loader]) => loadPanel(name, loader));
    return;
  }
  inventoryPanelObserver = new IntersectionObserver((entries) => {
    entries.filter((entry) => entry.isIntersecting).forEach((entry) => {
      const panel = panels.find(([id]) => id === entry.target.id);
      if (panel) loadPanel(panel[1], panel[2]);
    });
  }, { rootMargin: "300px 0px" });
  panels.forEach(([id]) => {
    const target = document.getElementById(id);
    if (target) inventoryPanelObserver.observe(target);
  });
}

async function loadInventoryLocations() {
  const select = document.getElementById("inventory-location");
  const list = document.getElementById("inventory-locations");
  try {
    inventoryLocations = await request("/api/v1/inventory/locations");
    select.innerHTML = `<option value="">${escapeHtml(foundationT("app.inventoryAllAvailable"))}</option>${inventoryLocations.map((location) => `<option value="${escapeHtml(location.id)}">${escapeHtml(location.name)}</option>`).join("")}`;
    if (getAuth()?.user.locationId && !selectedInventoryLocationId) {
      selectedInventoryLocationId = getAuth().user.locationId;
    }
    select.value = selectedInventoryLocationId;
    select.disabled = Boolean(getAuth()?.user.locationId);
    list.innerHTML = inventoryLocations.length === 0
      ? `<span class="muted-text">${escapeHtml(foundationT("app.inventoryNoLocations"))}</span>`
      : inventoryLocations.map((location) => `<button class="reference-item" type="button" data-location-id="${escapeHtml(location.id)}"><strong>${escapeHtml(location.name)}</strong><span>${escapeHtml(uiText(location.locationType))} ${escapeHtml(uiText(location.isActive ? "Active" : "Inactive"))}</span></button>`).join("");
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
    const current = filter?.value;
    if (current) {
      const sku = await ensureSkuOption(current);
      if (sku) inventorySkuOptions = [sku];
    }
    updateInventorySkuSearchLabel();
  } catch (exception) {
    if (filter) {
      filter.value = "";
    }
    if (search) {
      search.value = "";
      search.placeholder = foundationT("app.inventoryCatalogUnavailable");
    }
  }
}

function merchantRecallReturnDialog(locations, recall) {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.className = "dialog-overlay";
    overlay.innerHTML = `
      <form class="dialog-card" novalidate>
        <div class="section-head tight-head"><div><h2>${escapeHtml(foundationT("app.inline.startMerchantReturn"))}</h2><p class="muted-text">${escapeHtml(recall.merchantName)} / ${escapeHtml(recall.skuCode || recall.productName || "SKU")}</p></div></div>
        <div class="field"><label>${escapeHtml(foundationT("app.inline.receivingLocation"))}</label><select class="select" data-recall-location required><option value="">${escapeHtml(foundationT("app.inline.selectALocation"))}</option>${locations.filter((location) => location.isActive !== false).map((location) => `<option value="${escapeHtml(location.id)}">${escapeHtml(location.name)}</option>`).join("")}</select></div>
        <div class="field"><label>${escapeHtml(foundationT("app.inline.physicalQuantity"))}</label><input class="input" data-recall-quantity type="number" min="1" step="1" required></div>
        <div class="field"><label>${escapeHtml(foundationT("payments.notes"))}</label><textarea class="input" data-recall-notes rows="3"></textarea></div>
        <div class="form-error" data-recall-error hidden></div>
        <div class="form-actions"><button class="button primary" type="submit">${escapeHtml(foundationT("app.inline.createReturnDraft"))}</button><button class="button secondary" type="button" data-dialog-cancel>${escapeHtml(foundationT("common.cancel"))}</button></div>
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
        error.textContent = foundationT("app.message.selectAReceivingLocationAndEnterAPositiveWhole");
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
        <div class="section-head tight-head"><div><h2 id="merchant-sales-variance-title">${escapeHtml(uiText(gate.title || foundationT("app.inline.recordedSalesWarning")))}</h2><p class="muted-text">${escapeHtml(uiText(gate.detail || foundationT("app.inline.reviewRecordedMerchantSalesBeforeContinuing")))}</p></div></div>
        <div class="table-wrap sales-variance-dialog-table"><table><thead><tr><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("app.catalogExpiry"))}</th><th>${escapeHtml(foundationT("app.inline.soldToMerchant"))}</th><th>${escapeHtml(foundationT("app.inline.alreadyReturned"))}</th><th>${escapeHtml(foundationT("app.inline.requestedNow"))}</th><th>${escapeHtml(foundationT("app.inline.aboveRecordedBalance"))}</th></tr></thead><tbody>${warningRows}</tbody></table></div>
        ${gate.canBypass ? `
          <div class="field"><label for="merchant-sales-variance-reason">${escapeHtml(foundationT("app.inline.exceptionReason"))}</label><textarea id="merchant-sales-variance-reason" class="input" rows="3" maxlength="500" placeholder="${escapeHtml(foundationT("app.attribute.explainWhyThisReturnShouldContinue"))}" required></textarea></div>
          <div class="form-error" data-variance-error hidden></div>` : `<p class="form-error">${escapeHtml(foundationT("app.inline.thisAccountCanReviewTheWarningButCannotBypass"))}</p>`}
        <div class="form-actions">
          ${gate.canBypass ? `<button class="button primary" type="submit" data-dialog-confirm>${escapeHtml(foundationT("app.inline.confirmWithException"))}</button>` : ""}
          <button class="button secondary" type="button" data-dialog-cancel>${escapeHtml(uiText(gate.canBypass ? "Cancel" : "Close"))}</button>
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
          error.textContent = foundationT("app.message.exceptionReasonIsRequired");
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
    results.replaceChildren();
  }
  search?.setAttribute("aria-expanded", "false");
}

function clearInventorySkuFilter() {
  const filter = document.getElementById("inventory-sku");
  const search = document.getElementById("inventory-sku-search");
  if (filter) filter.value = "";
  if (search) search.value = "";
  inventoryPageState = { balances: 1, batches: 1, transactions: 1, blocked: 1, replenishment: 1 };
  hideInventorySkuSearchResults();
  refreshInventoryTables();
}

async function renderInventorySkuSearchResults() {
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

  const requestId = (skuSearchRequests.get(search) || 0) + 1;
  skuSearchRequests.set(search, requestId);
  let matches;
  try {
    matches = await searchSkuOptions(query, 20);
  } catch {
    matches = [];
  }
  if (skuSearchRequests.get(search) !== requestId || search.value.trim().toLowerCase() !== query) return;
  inventorySkuOptions = matches;
  setupAdaptiveSearchResultDismissal();
  collapseAdaptiveSearchResults(results);
  results.hidden = false;
  search.setAttribute("aria-expanded", "true");
  results.innerHTML = matches.length === 0
    ? `<button type="button" class="op-line-search-result" disabled>${escapeHtml(foundationT("app.inventoryNoMatchingSku"))}</button>`
    : matches.map((sku) => `
        <button type="button" class="op-line-search-result" role="option" data-inventory-sku-id="${escapeHtml(sku.id)}">
          <strong>${escapeHtml(sku.productName)}</strong>
          <span>${escapeHtml(formatOperationPowerKey(operationPowerKey(sku)))} / ${escapeHtml(sku.colorName || "-")} / ${escapeHtml(sku.size || "-")}</span>
          <small>${escapeHtml(sku.skuCode)}</small>
        </button>`).join("");
  results.querySelectorAll("[data-inventory-sku-id]").forEach((button) => button.addEventListener("click", () => {
    filter.value = button.dataset.inventorySkuId || "";
    inventoryPageState = { balances: 1, batches: 1, transactions: 1, blocked: 1, replenishment: 1 };
    updateInventorySkuSearchLabel();
    hideInventorySkuSearchResults();
    refreshInventoryTables();
  }));
}

async function loadInventoryProductTotals(generation = inventoryRefreshGeneration) {
  const tbody = document.getElementById("inventory-product-totals");
  const count = document.getElementById("inventory-product-total-count");
  if (!tbody || !count) {
    return;
  }

  const params = new URLSearchParams();
  if (selectedInventoryLocationId) {
    params.set("locationId", selectedInventoryLocationId);
  }
  params.set("includeProducts", "false");

  try {
    const rows = await request(`/api/v1/inventory/product-totals?${params.toString()}`);
    if (generation !== inventoryRefreshGeneration) return;
    count.textContent = foundationT(rows.length === 1 ? "app.count.category" : "app.count.categories", { count: rows.length });
    tbody.innerHTML = rows.length === 0
      ? `<tr><td colspan="5">${escapeHtml(foundationT("app.inventoryNoAvailableStock"))}</td></tr>`
      : rows.map((row, index) => {
        const detailId = `inventory-product-category-${index}`;
        const products = Array.isArray(row.products) ? row.products : [];
        return `
        <tr class="product-total-row">
          <td><strong>${escapeHtml(row.categoryName || row.productName || shortId(row.categoryId || row.productId, row.categoryId ? "CAT" : "PRD"))}</strong><span class="muted-cell">${escapeHtml(foundationT((row.productCount ?? products.length) === 1 ? "app.count.product" : "app.count.products", { count: row.productCount ?? products.length }))}</span></td>
          <td>${escapeHtml(row.skuCount)}</td>
          <td>${escapeHtml(row.totalPacks)}</td>
          <td>${row.totalPieces == null ? "-" : escapeHtml(row.totalPieces)}</td>
          <td><button class="button secondary table-action" type="button" data-product-total-toggle="${escapeHtml(detailId)}" data-category-id="${escapeHtml(row.categoryId)}" aria-expanded="false">${escapeHtml(foundationT("payments.details"))}</button></td>
        </tr>
        <tr class="product-rate-row" id="${escapeHtml(detailId)}" hidden>
          <td colspan="5">${products.length ? renderCategoryProductTotals(products) : `<span class="muted-text">${escapeHtml(foundationT("app.inline.loadDetailsWhenExpanded"))}</span>`}</td>
        </tr>`;
      }).join("");
    tbody.querySelectorAll("[data-product-total-toggle]").forEach((button) => {
      button.addEventListener("click", () => toggleProductTotalDetails(button));
    });
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="5">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

function renderCategoryProductTotals(products) {
  return `
    <div class="table-wrap compact-table product-rate-table">
      <table>
        <thead><tr><th>${escapeHtml(foundationT("app.product"))}</th><th>${escapeHtml(foundationT("app.catalogOpeningValidity"))}</th><th>${escapeHtml(foundationT("app.inline.rate"))}</th><th>${escapeHtml(foundationT("app.inventorySkuCount"))}</th><th>${escapeHtml(foundationT("app.inventoryTotalPacks"))}</th><th>${escapeHtml(foundationT("app.inventoryTotalPieces"))}</th></tr></thead>
        <tbody>${products.length === 0
          ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.noValidityBreakdown"))}</td></tr>`
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

async function toggleProductTotalDetails(button) {
  const row = document.getElementById(button.dataset.productTotalToggle);
  if (!row) return;
  const expanded = button.getAttribute("aria-expanded") === "true";
  button.setAttribute("aria-expanded", String(!expanded));
  button.closest(".product-total-row")?.setAttribute("aria-expanded", String(!expanded));
  button.textContent = foundationT(expanded ? "payments.details" : "payments.hide");
  row.hidden = expanded;
  if (!expanded && row.dataset.loaded !== "true") {
    const params = new URLSearchParams({ categoryId: button.dataset.categoryId, includeProducts: "true" });
    if (selectedInventoryLocationId) params.set("locationId", selectedInventoryLocationId);
    const cell = row.querySelector("td");
    const loading = document.createElement("span");
    loading.className = "muted-text";
    loading.textContent = foundationT("app.loadingDetails");
    cell.replaceChildren(loading);
    try {
      const categories = await request(`/api/v1/inventory/product-totals?${params.toString()}`);
      const parsed = new DOMParser().parseFromString(renderCategoryProductTotals(categories[0]?.products || []), "text/html");
      cell.replaceChildren(...parsed.body.childNodes);
      row.dataset.loaded = "true";
    } catch (exception) {
      cell.textContent = getFriendlyInventoryError(exception);
    }
  }
}

async function loadInventoryBalances(generation = inventoryRefreshGeneration) {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  const tbody = document.getElementById("inventory-balances");
  const count = document.getElementById("inventory-balance-count");
  const includeZeroStock = document.getElementById("inventory-include-zero-stock");
  if (!tbody || !count || !includeZeroStock) {
    return;
  }
  const params = inventoryParams();
  params.set("page", String(inventoryPageState.balances));
  params.set("pageSize", "50");
  params.set("includeZeroStock", String(includeZeroStock.checked));
  try {
    const result = await request(`/api/v1/inventory/stock-balances?${params.toString()}`);
    if (generation !== inventoryRefreshGeneration) return;
    count.textContent = foundationT(result.totalCount === 1 ? "app.count.balance" : "app.count.balances", { count: result.totalCount });
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="${canWrite ? 9 : 8}">${escapeHtml(foundationT("app.inventoryNoBalances"))}</td></tr>`
      : result.items.map((balance) => `
        <tr>
          <td>${escapeHtml(balance.locationName)}</td>
          <td><strong>${escapeHtml(balance.skuCode || uiText("Unknown SKU"))}</strong>${skuStatusBadge(balance.skuIsActive)}<span class="muted-cell">${escapeHtml(balance.productName || shortId(balance.skuId, "SKU"))}</span></td>
          <td>${quantityStack(balance.availablePacks, balance.availablePieces, balance.locationType)}</td>
          <td>${quantityStack(balance.reservedInWarehousePacks + balance.reservedWithRepPacks, addNullable(balance.reservedInWarehousePieces, balance.reservedWithRepPieces), balance.locationType)}</td>
          <td>${quantityStack(balance.targetPacks, balance.targetPieces, balance.locationType)}</td>
          <td>${quantityStack(inventoryShortagePacks(balance), inventoryShortagePieces(balance), balance.locationType)}</td>
          <td>${inventoryStockStatus(balance)}</td>
          <td>${escapeHtml(formatDateTime(balance.lastUpdated))}</td>
          ${canWrite ? `<td><button class="button secondary table-action" type="button" data-target-location="${escapeHtml(balance.locationId)}" data-target-sku="${escapeHtml(balance.skuId)}" data-target-current="${escapeHtml(balance.targetPacks ?? "")}">${escapeHtml(foundationT("app.inline.setTarget"))}</button></td>` : ""}
        </tr>`).join("");
    tbody.querySelectorAll("[data-target-location]").forEach((button) => button.addEventListener("click", () => setInventoryTarget(button)));
    renderInventoryPager(tbody, "balances", result, loadInventoryBalances);
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="${canWrite ? 9 : 8}">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

async function loadInventoryReplenishment(generation = inventoryRefreshGeneration) {
  const tbody = document.getElementById("inventory-replenishment");
  const count = document.getElementById("inventory-replenishment-count");
  if (!tbody || !count) {
    return;
  }

  const params = inventoryParams();
  try {
    params.set("paged", "true");
    params.set("page", String(inventoryPageState.replenishment));
    params.set("pageSize", "50");
    const result = await request(`/api/v1/operations/replenishment?${params.toString()}`);
    if (generation !== inventoryRefreshGeneration) return;
    const rows = result.items || [];
    const shortages = rows.filter((row) => row.shortagePacks > 0);
    count.textContent = foundationT(result.totalCount === 1 ? "app.count.targetRow" : "app.count.targetRows", { count: result.totalCount });
    tbody.innerHTML = rows.length === 0
      ? `<tr><td colspan="7">${escapeHtml(foundationT("app.inventoryNoTargets"))}</td></tr>`
      : rows.map((row) => `
        <tr>
          <td>${escapeHtml(row.destinationLocationName)}</td>
          <td><strong>${escapeHtml(row.skuCode || uiText("Unknown SKU"))}</strong><span class="muted-cell">${escapeHtml(row.productName || shortId(row.skuId, "SKU"))}</span></td>
          <td>${quantityStack(row.availablePacks, row.availablePieces, row.destinationLocationType)}</td>
          <td>${quantityStack(row.incomingPacks, row.incomingPieces, row.destinationLocationType)}</td>
          <td>${quantityStack(row.targetPacks, row.targetPieces, row.destinationLocationType)}</td>
          <td>${row.shortagePacks > 0 ? `<span class="status-pill status-warn">${quantityText(row.shortagePacks, row.shortagePieces, row.destinationLocationType)}</span>` : `<span class="status-pill status-ok">${escapeHtml(foundationT("app.inline.covered"))}</span>`}</td>
          <td>${quantityStack(row.mainAvailablePacks, null, "MainWarehouse")}</td>
        </tr>`).join("");
    renderInventoryPager(tbody, "replenishment", result, loadInventoryReplenishment);
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="7">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function loadInventoryBatches(generation = inventoryRefreshGeneration) {
  const tbody = document.getElementById("inventory-batches");
  const count = document.getElementById("inventory-batch-count");
  const params = inventoryParams();
  params.set("page", String(inventoryPageState.batches));
  params.set("pageSize", "50");
  params.set("includeEmpty", String(document.getElementById("inventory-include-empty").checked));
  try {
    const result = await request(`/api/v1/inventory/batches?${params.toString()}`);
    if (generation !== inventoryRefreshGeneration) return;
    count.textContent = foundationT(result.totalCount === 1 ? "app.count.batch" : "app.count.batches", { count: result.totalCount });
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inventoryNoBatches"))}</td></tr>`
      : result.items.map((batch) => `
        <tr>
          <td>${escapeHtml(batch.lotNumber || "-")}</td>
          <td>${escapeHtml(batch.locationName)}</td>
          <td><strong>${escapeHtml(batch.skuCode || uiText("Unknown SKU"))}</strong>${skuStatusBadge(batch.skuIsActive)}<span class="muted-cell">${escapeHtml(batch.productName || shortId(batch.skuId, "SKU"))}</span></td>
          <td>${quantityStack(batch.packQuantity, batch.pieceQuantity, batch.locationType)}</td>
          <td>${expiryBadge(batch.expiryDate)}</td>
          <td>${escapeHtml(batch.notes || "-")}</td>
        </tr>`).join("");
    renderInventoryPager(tbody, "batches", result, loadInventoryBatches);
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

async function loadTransferBlockedBatches(generation = inventoryRefreshGeneration) {
  const tbody = document.getElementById("inventory-blocked-batches");
  const count = document.getElementById("inventory-blocked-count");
  if (!tbody || !count) {
    return;
  }

  const params = inventoryParams();
  params.set("page", String(inventoryPageState.blocked));
  params.set("paged", "true");
  params.set("pageSize", "50");
  try {
    const result = await request(`/api/v1/inventory/transfer-blocked-batches?${params.toString()}`);
    if (generation !== inventoryRefreshGeneration) return;
    const rows = result.items || [];
    count.textContent = foundationT("inventory.expiredCount", { count: result.totalCount });
    tbody.innerHTML = rows.length === 0
      ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inventoryNoExpired"))}</td></tr>`
      : rows.map((batch) => `
        <tr>
          <td>${escapeHtml(batch.locationName)}</td>
          <td><strong>${escapeHtml(batch.skuCode || uiText("Unknown SKU"))}</strong><span class="muted-cell">${escapeHtml(batch.productName || shortId(batch.skuId, "SKU"))}</span></td>
          <td>${escapeHtml(batch.lotNumber || "-")}</td>
          <td>${quantityStack(batch.packQuantity, batch.pieceQuantity, batch.locationType)}</td>
          <td>${expiryBadge(batch.expiryDate)}</td>
          <td><span class="status-pill status-warn">${escapeHtml(uiText(batch.reason || "Blocked"))}</span></td>
        </tr>`).join("");
    renderInventoryPager(tbody, "blocked", result, loadTransferBlockedBatches);
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

async function loadInventoryTransactions(generation = inventoryRefreshGeneration) {
  const tbody = document.getElementById("inventory-transactions");
  const count = document.getElementById("inventory-transaction-count");
  const params = inventoryParams();
  params.set("page", String(inventoryPageState.transactions));
  params.set("pageSize", "50");
  try {
    const result = await request(`/api/v1/inventory/transactions?${params.toString()}`);
    if (generation !== inventoryRefreshGeneration) return;
    count.textContent = foundationT(result.totalCount === 1 ? "app.count.transaction" : "app.count.transactions", { count: result.totalCount });
    tbody.innerHTML = result.items.length === 0
      ? `<tr><td colspan="5">${escapeHtml(foundationT("app.inventoryNoTransactions"))}</td></tr>`
      : result.items.map((transaction) => `
        <tr>
          <td>${escapeHtml(uiText(transaction.transactionType))}</td>
          <td>${escapeHtml(transaction.locationName)}</td>
          <td><strong>${escapeHtml(transaction.skuCode || "Unknown SKU")}</strong>${skuStatusBadge(transaction.skuIsActive)}<span class="muted-cell">${escapeHtml(transaction.productName || shortId(transaction.skuId, "SKU"))}</span></td>
          <td>${quantityStack(transaction.packChange, transaction.pieceChange, transaction.locationType)}</td>
          <td>${escapeHtml(formatDateTime(transaction.createdAt))}</td>
        </tr>`).join("");
    renderInventoryPager(tbody, "transactions", result, loadInventoryTransactions);
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="5">${escapeHtml(getFriendlyInventoryError(exception))}</td></tr>`;
  }
}

function inventoryStockStatus(balance) {
  if (balance.targetPacks === null || balance.targetPacks === undefined) {
    return `<span class="status-pill status-muted">${escapeHtml(foundationT("app.inventoryNoTarget"))}</span>`;
  }
  if (balance.availablePacks < balance.targetPacks) {
    return `<span class="status-pill status-warn">${escapeHtml(foundationT("app.inventoryLowStock"))}</span>`;
  }
  return `<span class="status-pill status-ok">${escapeHtml(foundationT("app.inventoryHealthy"))}</span>`;
}

function skuStatusBadge(isActive) {
  if (isActive === false) {
    return ` <span class="status-pill status-muted">${escapeHtml(foundationT("app.inventoryInactiveSku"))}</span>`;
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
  const packText = packs === null || packs === undefined ? "-" : foundationT("app.inline.packCount", { count: packs });
  if (locationType === "MainWarehouse") {
    return `<strong>${escapeHtml(packText)}</strong>`;
  }
  const pieceText = pieces === null || pieces === undefined ? foundationT("app.inline.piecesNotSet") : foundationT("app.inline.pieceCount", { count: pieces });
  return `<strong>${escapeHtml(packText)}</strong><span class="muted-cell">${escapeHtml(pieceText)}</span>`;
}

function quantityText(packs, pieces, locationType) {
  const packText = foundationT("app.inline.packCount", { count: packs });
  if (locationType === "MainWarehouse" || pieces === null || pieces === undefined) {
    return packText;
  }

  return `${packText} / ${foundationT("app.inline.pieceCount", { count: pieces })}`;
}

function addNullable(left, right) {
  if (left === null || left === undefined || right === null || right === undefined) {
    return null;
  }
  return left + right;
}

function expiryBadge(expiryDate) {
  if (!expiryDate) {
    return `<span class="status-pill status-muted">${escapeHtml(foundationT("app.inline.noExpiry"))}</span>`;
  }
  const today = new Date();
  const expiry = new Date(`${expiryDate}T00:00:00`);
  const days = Math.ceil((expiry - today) / 86400000);
  if (days < 0) {
    return `<span class="status-pill status-warn">${escapeHtml(foundationT("app.inline.expiryDateExpired", { date: expiryDate }))}</span>`;
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
    title: foundationT("app.prompt.setTargetPacks"),
    label: foundationT("app.prompt.targetPacksHelp"),
    defaultValue: current,
    inputType: "number",
    required: true
  });
  if (raw === null) {
    return;
  }
  const value = raw.trim();
  if (value && (!Number.isInteger(Number(value)) || Number(value) < 0)) {
    notice(foundationT("app.message.targetPacksMustBeANonNegativeWholeNumber"), "error");
    return;
  }

  button.disabled = true;
  try {
    await request(`/api/v1/inventory/stock-balances/${button.dataset.targetLocation}/${button.dataset.targetSku}/target`, {
      method: "PUT",
      body: JSON.stringify({ targetPacks: value ? Number(value) : null }),
      notify: false
    });
    notice(foundationT("app.message.targetPacksUpdated"), "success");
    await loadInventoryBalances();
  } catch (exception) {
    notice(getFriendlyInventoryError(exception), "error");
  } finally {
    button.disabled = false;
  }
}

async function reserveInventoryReplenishment() {
  const button = document.getElementById("reserve-replenishment");
  if (button) button.disabled = true;
  try {
    const locationId = document.getElementById("inventory-location")?.value || null;
    const skuId = document.getElementById("inventory-sku")?.value || null;
    const result = await request("/api/v1/operations/replenishment/daily-reset", {
      method: "POST",
      body: JSON.stringify({ locationId, skuId }),
      notify: false
    });
    const alertText = result.alerts?.length
      ? foundationT("app.message.replenishmentAlert", { alerts: result.alerts.map((alert) => `${alert.skuCode || alert.skuId} @ ${alert.destinationLocationName}: ${uiText(alert.message)}`).join(" | ") })
      : "";
    notice(foundationT("app.message.replenishmentCreated", { created: result.createdOperations, unfilled: result.unfilledPacks, alerts: alertText }), result.unfilledPacks > 0 ? "info" : "success");
    await refreshInventoryTables();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  } finally {
    if (button) button.disabled = false;
  }
}

async function renderCrm() {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  selectedMerchantId = null;
  document.getElementById("view").innerHTML = `
    <section class="catalog-hero">
      <div>
        <p class="eyebrow">${escapeHtml(foundationT("routes.crm.title"))}</p>
        <h2>${escapeHtml(foundationT("app.inline.merchantRecords"))}</h2>
        <p>${escapeHtml(foundationT("app.inline.maintainCommercialRelationshipsOperationalNotesAndMerchantContextUsed"))}</p>
      </div>
      <div class="scenario-grid">
        ${scenarioCard("Role", canWrite ? "Can edit CRM records" : "Read only", canWrite ? "status-ok" : "status-muted")}
        ${scenarioCard("Merchant context", foundationT("app.inline.batchHistoryAndNotes"), "status-muted")}
        ${scenarioCard("Operations link", foundationT("app.inline.sharedAcrossWorkflows"), "status-muted")}
      </div>
    </section>
    <section class="band">
      <div class="section-head">
        <div>
          <h2>${escapeHtml(foundationT("app.inline.merchants"))}</h2>
          <p>${escapeHtml(foundationT("app.inline.profilesCommercialContactsRemainingContextAndOperationalHistory"))}</p>
        </div>
        <span id="crm-count" class="status-pill status-muted">${escapeHtml(foundationT("common.loading"))}</span>
      </div>
      ${canWrite ? `
        <form id="merchant-form" class="form grid-form">
          <input id="merchant-id" type="hidden">
          <div class="field"><label for="merchant-name">${escapeHtml(foundationT("app.inline.businessName"))}</label><input id="merchant-name" class="input" required></div>
          <div class="field"><label for="merchant-contact">${escapeHtml(foundationT("payments.contactPerson"))}</label><input id="merchant-contact" class="input" required></div>
          <div class="field"><label for="merchant-phone">${escapeHtml(foundationT("payments.phone"))}</label><input id="merchant-phone" class="input"></div>
          <div class="field"><label for="merchant-type">${escapeHtml(foundationT("payments.businessType"))}</label><select id="merchant-type" class="select"><option value="Merchant">${escapeHtml(foundationT("customer.merchant"))}</option><option value="Pharmacy">${escapeHtml(foundationT("app.inline.pharmacy"))}</option><option value="Oculist">${escapeHtml(foundationT("app.inline.oculist"))}</option><option value="BeautyCenter">${escapeHtml(foundationT("app.inline.beautyCenter"))}</option><option value="Other">${escapeHtml(foundationT("finance.expenses.other"))}</option></select></div>
          <div class="toolbar full-span">
            <button id="merchant-save-button" class="button primary" type="submit">${escapeHtml(foundationT("app.inline.createMerchant"))}</button>
            <button id="merchant-reset-button" class="button secondary" type="button">${escapeHtml(foundationT("app.inline.clear"))}</button>
          </div>
        </form>` : ""}
      <div class="table-wrap">
        <table><thead><tr><th>${escapeHtml(foundationT("app.inline.business"))}</th><th>${escapeHtml(foundationT("app.inline.contact"))}</th><th>${escapeHtml(foundationT("payments.phone"))}</th><th>${escapeHtml(foundationT("payments.type"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("payments.actions"))}</th></tr></thead><tbody id="merchant-rows"></tbody></table>
      </div>
      <div id="merchant-detail-panel" class="detail-panel" hidden></div>
    </section>`;

  if (canWrite) {
    document.getElementById("merchant-form").addEventListener("submit", saveMerchant);
    document.getElementById("merchant-reset-button").addEventListener("click", resetMerchantForm);
  }
  await loadMerchants();
}

async function loadMerchants(search = "") {
  const auth = getAuth();
  const canWrite = isSystemAdminRole(auth?.user.role);
  const canReadBatchHistory = ["Admin", "ERPAdmin", "CLevel"].includes(auth?.user.role);
  const tbody = document.getElementById("merchant-rows");
  const count = document.getElementById("crm-count");
  try {
    const result = await fetchMerchantList(search);
    count.textContent = foundationT("crm.merchantCount", { count: result.totalCount });
    tbody.innerHTML = result.items.length === 0 ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.noMerchantsYet"))}</td></tr>` : result.items.map((merchant) => `
      <tr>
        <td>${escapeHtml(merchant.businessName)}</td>
        <td>${escapeHtml(merchant.contactPersonName)}</td>
        <td>${escapeHtml((merchant.phoneNumbers || []).join(", ") || "-")}</td>
        <td>${escapeHtml(uiText(merchant.businessType))}</td>
        <td><span class="status-pill ${merchant.status === "Active" ? "status-ok" : "status-muted"}">${escapeHtml(uiText(merchant.status))}</span></td>
        <td>
          <button class="button secondary table-action" type="button" data-view-merchant="${escapeHtml(merchant.id)}">${escapeHtml(foundationT("app.inline.detail"))}</button>
          <button class="button secondary table-action" type="button" data-print-report="merchant-statement" data-print-id="${escapeHtml(merchant.id)}" data-print-code="${escapeHtml(merchant.businessName)}">${escapeHtml(foundationT("payments.print"))}</button>
          ${canWrite ? `<button class="button secondary table-action" type="button" data-edit-merchant="${escapeHtml(merchant.id)}">${escapeHtml(foundationT("app.catalogEdit"))}</button>` : ""}
          ${canWrite ? `<button class="button secondary table-action" type="button" data-status-merchant="${escapeHtml(merchant.id)}" data-next-status="${merchant.status === "Active" ? "deactivate" : "reactivate"}">${escapeHtml(uiText(merchant.status === "Active" ? "Deactivate" : "Reactivate"))}</button>` : ""}
          ${canWrite ? `<button class="button secondary table-action" type="button" data-note-merchant="${escapeHtml(merchant.id)}">${escapeHtml(foundationT("app.inline.addNote"))}</button>` : ""}
          ${canReadBatchHistory ? `<button class="button secondary table-action" type="button" data-batch-history-merchant="${escapeHtml(merchant.id)}">${escapeHtml(foundationT("app.inline.batchHistory"))}</button>` : ""}
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
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function saveMerchant(event) {
  event.preventDefault();
  const businessName = document.getElementById("merchant-name").value.trim();
  const contactPersonName = document.getElementById("merchant-contact").value.trim();
  const merchantId = document.getElementById("merchant-id").value;
  if (!businessName || !contactPersonName) {
    notice(foundationT("app.message.businessNameAndContactPersonAreRequired"), "error");
    return;
  }

  try {
    await request(merchantId ? `/api/v1/crm/merchants/${merchantId}` : "/api/v1/crm/merchants", {
      method: merchantId ? "PUT" : "POST",
      body: JSON.stringify({
        businessName,
        contactPersonName,
        phoneNumbers: document.getElementById("merchant-phone").value.trim() ? [document.getElementById("merchant-phone").value.trim()] : [],
        businessType: canonicalSelectValue("merchant-type", "businessType")
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

function resetMerchantForm() {
  const form = document.getElementById("merchant-form");
  if (!form) {
    return;
  }
  form.reset();
  document.getElementById("merchant-id").value = "";
  document.getElementById("merchant-save-button").textContent = foundationT("app.inline.createMerchant");
  selectedMerchantId = null;
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
  document.getElementById("merchant-save-button").textContent = foundationT("app.message.updateMerchant");
  selectedMerchantId = merchant.id;
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

async function showMerchantDetail(merchantId, existingDetail = null) {
  selectedMerchantId = merchantId;
  const panel = document.getElementById("merchant-detail-panel");
  if (!panel) {
    return;
  }
  panel.hidden = false;
  panel.innerHTML = `<span class="muted-text">${escapeHtml(foundationT("app.inline.loadingMerchantDetail"))}</span>`;
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
        <span class="status-pill ${merchant.status === "Active" ? "status-ok" : "status-muted"}">${escapeHtml(uiText(merchant.status))}</span>
      </div>
      ${merchant.notes ? `<p class="muted-text"><strong>${escapeHtml(foundationT("common.notes"))}:</strong> ${escapeHtml(merchant.notes)}</p>` : ""}
      <div class="operation-detail-grid">
        <div class="metric"><span>${escapeHtml(foundationT("navigation.operations"))}</span><strong>${escapeHtml(summary.operationCount || 0)}</strong></div>
        <div class="metric"><span>${escapeHtml(foundationT("app.soldPacks"))}</span><strong>${escapeHtml(summary.soldPacks || 0)}</strong></div>
        <div class="metric"><span>${escapeHtml(foundationT("app.soldPieces"))}</span><strong>${escapeHtml(summary.soldPieces || 0)}</strong></div>
        <div class="metric"><span>${escapeHtml(foundationT("payments.remaining"))}</span><strong>${escapeHtml(formatMoney(balance))}</strong></div>
      </div>
      <div class="table-wrap compact-table"><table><thead><tr><th>${escapeHtml(foundationT("payments.operation"))}</th><th>${escapeHtml(foundationT("payments.type"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("payments.payment"))}</th><th>${escapeHtml(foundationT("supply.qty"))}</th><th>${escapeHtml(foundationT("app.inline.bonus"))}</th><th>${escapeHtml(foundationT("payments.total"))}</th><th>${escapeHtml(foundationT("app.created"))}</th></tr></thead><tbody>${operations.length === 0
        ? `<tr><td colspan="8">${escapeHtml(foundationT("app.inline.noOperationsForThisMerchantYet"))}</td></tr>`
        : operations.map((operation) => `<tr>
            <td><strong>${escapeHtml(operation.operationNumber)}</strong></td>
            <td>${escapeHtml(uiText(operation.operationType))}</td>
            <td><span class="status-pill ${operationStatusClass(operation.status)}">${escapeHtml(uiText(operation.status))}</span></td>
            <td>${escapeHtml(movementMethodLabel(operation.paymentMethod))}</td>
            <td>${escapeHtml(operation.quantity || 0)}</td>
            <td>${escapeHtml(operation.bonusQuantity || 0)}</td>
            <td>${escapeHtml(formatMoney(operation.total || 0))}</td>
            <td>${escapeHtml(formatDateTime(operation.createdAt))}</td>
          </tr>`).join("")}</tbody></table></div>
      ${canReadBatchHistory ? `<div class="section-head tight-head"><h3>${escapeHtml(foundationT("app.merchantBatchHistory"))}</h3><span class="muted-text">${escapeHtml(foundationT("app.recordedSalesReturnsDetailed"))}</span></div>
      ${renderMerchantBatchHistoryTable(batchRows)}` : ""}
      <div class="table-wrap compact-table"><table><thead><tr><th>${escapeHtml(foundationT("app.latestNotes"))}</th><th>${escapeHtml(foundationT("app.created"))}</th></tr></thead><tbody>${notes.length === 0
        ? `<tr><td colspan="2">${escapeHtml(foundationT("app.noNotesYet"))}</td></tr>`
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
        <div class="section-head tight-head"><h3>${escapeHtml(foundationT("app.merchantBatchHistory"))}</h3><span class="muted-text">${escapeHtml(foundationT("app.recordedSalesReturns"))}</span></div>
        ${renderMerchantBatchHistoryTable(rows)}`;
    }
    notice(rows.length === 0 ? "No merchant batch history yet." : "Merchant batch history loaded.", "info");
    await loadMerchants();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function renderMerchantBatchHistoryTable(rows) {
  return `<div class="table-wrap compact-table"><table><thead><tr><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("app.product"))}</th><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("app.batchExpiry"))}</th><th>${escapeHtml(foundationT("app.sold"))}</th><th>${escapeHtml(foundationT("app.returned"))}</th><th>${escapeHtml(foundationT("app.expiryStatus"))}</th></tr></thead><tbody>${rows.length === 0
    ? `<tr><td colspan="7">${escapeHtml(foundationT("app.noMerchantSalesReturns"))}</td></tr>`
    : rows.map((row) => `<tr>
          <td><strong>${escapeHtml(row.skuCode || shortId(row.skuId, "SKU"))}</strong></td>
          <td>${escapeHtml(row.productName || "-")}</td>
          <td>${escapeHtml(row.lotNumber || "-")}</td>
          <td>${row.expiryDate ? expiryBadge(row.expiryDate) : `<span class="status-pill status-muted">-</span>`}</td>
          <td>${escapeHtml(row.soldQuantity || 0)}</td>
          <td>${escapeHtml(row.returnedQuantity || 0)}</td>
          <td><span class="status-pill ${row.expiryStatus === "Expired" ? "status-warn" : "status-muted"}">${escapeHtml(row.expiryStatus ? uiText(row.expiryStatus) : "-")}</span></td>
        </tr>`).join("")}</tbody></table></div>`;
}

async function addMerchantNote(merchantId) {
  const note = await promptDialog({
    title: foundationT("app.prompt.addMerchantNote"),
    label: foundationT("app.prompt.merchantNoteHelp"),
    multiline: true,
    required: true
  });
  if (!note?.trim()) {
    return;
  }
  try {
    await request(`/api/v1/crm/merchants/${merchantId}/notes`, { method: "POST", body: JSON.stringify({ note }) });
    notice(foundationT("app.message.noteAdded"), "success");
    await loadMerchants();
    await showMerchantDetail(merchantId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function renderOperations() {
  const auth = getAuth();
  const canWrite = ["Admin", "ERPAdmin", "WarehouseClerk"].includes(auth?.user.role);
  const userOperationTypes = ["InventoryReceipt", "WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve", "Return", "Change", "WriteOff"];
  operationsUiState.operationType = userOperationTypes.includes(operationsUiState.operationType) ? operationsUiState.operationType : "WarehouseTransfer";
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
        <form id="operation-form" class="form operation-form-layout" novalidate>
          <aside class="workflow-rail">
            <div class="operation-editor-banner">
              <div>
                <strong id="operation-editor-title">${escapeHtml(foundationT("app.inline.createDraft"))}</strong>
                <p id="operation-editor-hint" class="muted-text">${escapeHtml(foundationT("app.inline.startANewOperationDraft"))}</p>
              </div>
              <span id="operation-editor-mode" class="status-pill status-muted">${escapeHtml(foundationT("app.inline.create"))}</span>
            </div>
          <div class="field"><label for="op-type">${escapeHtml(foundationT("payments.type"))}</label><select id="op-type" class="select"><option value="InventoryReceipt">${escapeHtml(foundationT("payments.inventoryReceipt"))}</option><option value="WarehouseTransfer">${escapeHtml(foundationT("payments.warehouseTransfer"))}</option><option value="WholesaleSale">${escapeHtml(foundationT("payments.wholesaleSale"))}</option><option value="RetailSale">${escapeHtml(foundationT("app.inline.retailOnlineSale"))}</option><option value="Reserve">${escapeHtml(foundationT("app.inline.representativeReserve"))}</option><option value="Return">${escapeHtml(foundationT("payments.return"))}</option><option value="Change">${escapeHtml(foundationT("app.inline.change"))}</option><option value="WriteOff">${escapeHtml(foundationT("payments.writeOff"))}</option></select></div>
            <div class="field"><label for="op-source">${escapeHtml(foundationT("app.inline.sourceLocation"))}</label><select id="op-source" class="select"></select></div>
            <div class="field"><label for="op-destination">${escapeHtml(foundationT("app.inline.destinationLocation"))}</label><select id="op-destination" class="select"></select></div>
            <div class="field op-merchant-field"><label for="op-merchant">${escapeHtml(foundationT("customer.merchant"))}</label><select id="op-merchant" class="select"></select></div>
            <div class="field op-buyer-field"><label for="op-buyer">${escapeHtml(foundationT("customer.buyerName"))}</label><input id="op-buyer" class="input" autocomplete="off"></div>
            <div class="field op-buyer-field"><label for="op-buyer-phone">${escapeHtml(foundationT("customer.buyerPhone"))}</label><input id="op-buyer-phone" class="input" autocomplete="off"></div>
            <div class="field op-payment-field"><label for="op-payment">${escapeHtml(foundationT("payments.paymentMethod"))} <span aria-hidden="true">*</span></label><select id="op-payment" class="select"><option value="">${escapeHtml(foundationT("app.inline.chooseMethod"))}</option><option value="CashHandToHand">${escapeHtml(foundationT("payments.cashInHand"))}</option><option value="CashTransaction">${escapeHtml(foundationT("payments.cashTransaction"))}</option><option value="BankTransfer">${escapeHtml(foundationT("payments.bankTransfer"))}</option><option value="Wallet">${escapeHtml(foundationT("payments.wallet"))}</option></select></div>
            <div class="field op-payment-field"><label for="op-finance-account">${escapeHtml(foundationT("app.inline.receivingAccount"))}</label><select id="op-finance-account" class="select"><option value="">${paymentT("chooseFinanceAccount")}</option></select></div>
            <div class="field"><label for="op-supplier">${escapeHtml(foundationT("supply.supplier"))}</label><input id="op-supplier" class="input" autocomplete="off" placeholder="${escapeHtml(foundationT("app.attribute.receiptOnly"))}"></div>
            <div class="field"><label for="op-invoice">${escapeHtml(foundationT("app.inline.invoice"))}</label><input id="op-invoice" class="input" autocomplete="off" placeholder="${escapeHtml(foundationT("app.attribute.usedForReceiptFlows"))}"></div>
            <div class="field"><label for="op-notes">${escapeHtml(foundationT("payments.notes"))}</label><input id="op-notes" class="input" autocomplete="off"></div>
            <div class="field" id="op-revision-reason-field" hidden><label for="op-revision-reason">${escapeHtml(foundationT("app.inline.revisionReason"))}</label><input id="op-revision-reason" class="input" autocomplete="off" placeholder="${escapeHtml(foundationT("app.attribute.requiredForRevisions"))}"></div>
            <div class="rail-actions">
              <button class="button primary" id="operation-submit-button" type="submit">${escapeHtml(foundationT("common.saveDraft"))}</button>
              <button id="operation-editor-reset" class="button secondary" type="button">${escapeHtml(foundationT("app.inline.reset"))}</button>
            </div>
          </aside>
          <section class="band operation-line-panel">
            <div class="section-head tight-head"><div><h2>${escapeHtml(foundationT("app.inline.operationLines"))}</h2><p>${escapeHtml(foundationT("app.inline.searchStockFirstOrChooseProductAttributesToResolve"))}</p></div><button id="op-add-line" class="button secondary" type="button">${escapeHtml(foundationT("supply.addLine"))}</button></div>
            <div id="op-lines" class="line-editor"></div>
            <div id="operation-line-pagination" class="pagination"></div>
          </section>
        </form>` : `<p class="muted-text">${escapeHtml(foundationT("app.inline.thisRoleCanInspectOperationsButCannotCreateOr"))}</p>`}
    </section>
    <section class="band rail-band">
      <div class="section-head">
        <div><h2>${escapeHtml(foundationT("app.inline.queue"))}</h2><p>${escapeHtml(foundationT("app.inline.activeOperationsStayCompactHereUseDetailsToInspect"))}</p></div>
      </div>
      <div class="toolbar">
        <label class="field"><span>${escapeHtml(foundationT("app.catalogSearch"))}</span><input id="operations-search" class="input" type="search" placeholder="${escapeHtml(foundationT("app.attribute.operationMerchantBuyerSKUOrPaymentReference"))}"></label>
        <label class="field"><span>${escapeHtml(foundationT("payments.type"))}</span><select id="operations-type" class="select"><option value="">${escapeHtml(foundationT("reports.allTypes"))}</option><option value="InventoryReceipt">${escapeHtml(foundationT("payments.inventoryReceipt"))}</option><option value="WarehouseTransfer">${escapeHtml(foundationT("payments.warehouseTransfer"))}</option><option value="WholesaleSale">${escapeHtml(foundationT("payments.wholesaleSale"))}</option><option value="RetailSale">${escapeHtml(foundationT("payments.retailSale"))}</option><option value="Reserve">${escapeHtml(foundationT("app.inline.representativeReserve"))}</option><option value="Return">${escapeHtml(foundationT("payments.return"))}</option><option value="Change">${escapeHtml(foundationT("payments.exchange"))}</option><option value="WriteOff">${escapeHtml(foundationT("payments.writeOff"))}</option></select></label>
        <label class="field"><span>${escapeHtml(foundationT("payments.status"))}</span><select id="operations-status" class="select"><option value="">${escapeHtml(foundationT("reports.allStatuses"))}</option><option value="Draft">${escapeHtml(foundationT("payments.draft"))}</option><option value="Confirmed">${escapeHtml(foundationT("payments.confirmed"))}</option><option value="Reserved">${escapeHtml(foundationT("app.inline.reserved"))}</option><option value="Shipped">${escapeHtml(foundationT("app.inline.shipped"))}</option><option value="Received">${escapeHtml(foundationT("reports.received"))}</option><option value="Completed">${escapeHtml(foundationT("payments.completed"))}</option><option value="Cancelled">${escapeHtml(foundationT("payments.cancelled"))}</option></select></label>
        <label class="field"><span>${escapeHtml(foundationT("payments.from"))}</span><input id="operations-from" class="input" type="date"></label>
        <label class="field"><span>${escapeHtml(foundationT("payments.to"))}</span><input id="operations-to" class="input" type="date"></label>
        <label class="field"><span>${escapeHtml(foundationT("app.inline.rows"))}</span><select id="operations-page-size" class="select"><option value="10">10</option><option value="25">25</option><option value="50" selected>50</option><option value="100">100</option><option value="250">250</option><option value="500">500</option></select></label>
        <label class="check-field"><input id="operations-show-completed" type="checkbox"><span>${escapeHtml(foundationT("app.inline.showCompletedReceivedCancelledHistory"))}</span></label>
      </div>
      <div class="table-wrap">
        <table><thead><tr><th>${escapeHtml(foundationT("app.inline.no"))}</th><th>${escapeHtml(foundationT("payments.type"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("app.inline.route"))}</th><th>${escapeHtml(foundationT("app.created"))}</th><th>${escapeHtml(foundationT("payments.action"))}</th></tr></thead><tbody id="operation-rows"></tbody></table>
      </div>
      <div id="operation-list-pagination" class="pagination"></div>
    </section>`;

  if (canWrite) {
    // The queue is useful before an operator starts a new document.  Keep
    // editor-only catalog and CRM data out of this first paint.
    await hydrateOperationLocations();
    const operationFinanceAccounts = await loadFinanceAccounts();
    const operationFinanceSelect = document.getElementById("op-finance-account");
    if (operationFinanceSelect) operationFinanceSelect.innerHTML = `<option value="">${paymentT("chooseFinanceAccount")}</option>${operationFinanceAccounts.map((account) => `<option value="${escapeHtml(account.id)}">${escapeHtml(account.name)} (${escapeHtml(financeValueLabel("accountType", account.type))})</option>`).join("")}`;
    const typeControl = document.getElementById("op-type");
    if (!typeControl) {
      return;
    }
    typeControl.value = operationsUiState.operationType;
    typeControl.addEventListener("change", () => {
      syncOperationTypeControls();
      const type = typeControl.value;
      if (["WholesaleSale", "RetailSale", "Return", "Change"].includes(type)) {
        void hydrateOperationCrmOptions();
      }
    });
    document.getElementById("op-source").addEventListener("change", () => {
      lockOperationRouteIfSelected();
      void refreshOperationSkuAvailability();
      primeAllOperationStockOptions();
    });
    document.getElementById("op-destination").addEventListener("change", () => {
      lockOperationRouteIfSelected();
      primeAllOperationStockOptions();
    });
    document.getElementById("op-merchant").addEventListener("change", primeAllOperationStockOptions);
    document.getElementById("op-add-line").addEventListener("click", async () => {
      await hydrateOperationSkus();
      addOperationLine();
    });
    document.getElementById("operation-editor-reset").addEventListener("click", resetOperationEditorMode);
    wireOperationLineEditor();
    operationEditorLines = [];
    operationEditorLineById.clear();
    operationEditorPage = 1;
    addOperationLine();
    syncOperationTypeControls();
    applyOperationEditorMode();
    document.getElementById("operation-form").addEventListener("submit", submitOperationEditor);
  }
  const operationQuery = currentRouteQuery();
  for (const [id, key] of [["operations-search", "search"], ["operations-type", "operationType"], ["operations-status", "status"], ["operations-from", "createdFrom"], ["operations-to", "createdTo"], ["operations-page-size", "pageSize"]]) {
    const value = operationQuery.get(key);
    const control = document.getElementById(id);
    if (control && value) control.value = value;
  }
  const completedQuery = operationQuery.get("includeCompleted");
  if (completedQuery !== null) document.getElementById("operations-show-completed").checked = completedQuery === "true";
  document.getElementById("operations-show-completed").addEventListener("change", () => { operationListPage = 1; void loadOperations(); });
  ["operations-search", "operations-type", "operations-status", "operations-from", "operations-to", "operations-page-size"].forEach((id) => {
    document.getElementById(id)?.addEventListener("input", (event) => {
      if (event.target.id !== "operations-search") { operationListPage = 1; void loadOperations(); return; }
      clearTimeout(window.__operationQueueSearchTimer);
      window.__operationQueueSearchTimer = setTimeout(() => { operationListPage = 1; void loadOperations(); }, 250);
    });
    document.getElementById(id)?.addEventListener("change", () => { operationListPage = 1; void loadOperations(); });
  });
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
  if (operationSkuLoadPromise) {
    await operationSkuLoadPromise;
    return;
  }

  operationSkuLoadPromise = hydrateOperationSkusCore()
    .finally(() => {
      operationSkuLoadPromise = null;
    });
  await operationSkuLoadPromise;
}

async function hydrateOperationSkusCore() {
  const products = [];
  let page = 1;
  let totalCount = 0;
  do {
    const result = await request(`/api/v1/catalog/products?includeInactive=false&page=${page}&pageSize=100`);
    products.push(...(result.items || []));
    totalCount = result.totalCount || products.length;
    page += 1;
  } while (products.length < totalCount);

  operationProductOptions = products.map((product) => ({
    id: product.id,
    name: product.name,
    brandName: product.brandName,
    categoryName: product.categoryName,
    productType: product.productType,
    piecesPerPack: product.piecesPerPack,
    sellMode: product.sellMode,
    label: `${product.brandName} / ${product.name}`
  })).sort((a, b) => a.label.localeCompare(b.label));
  document.querySelectorAll(".line-editor-row").forEach((row) => {
    const skuId = row.querySelector(".op-line-sku")?.value;
    populateOperationProductOptions(row);
    if (skuId) {
      seedOperationLineSkuSelection(row, skuId);
    }
    const search = row.querySelector(".op-line-search");
    if (search?.value.trim()) {
      renderOperationSkuSearchResults(row);
    }
  });
}

async function hydrateOperationCrmOptions() {
  try {
    const merchants = await fetchMerchantList("");
    operationMerchantOptions = (merchants.items || []).filter((merchant) => merchant.status === "Active");
    const merchantSelect = document.getElementById("op-merchant");
    if (merchantSelect) {
      merchantSelect.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.selectMerchant"))}</option>${operationMerchantOptions.map((merchant) => `<option value="${escapeHtml(merchant.id)}">${escapeHtml(merchant.businessName)}</option>`).join("")}`;
    }
  } catch {
    operationMerchantOptions = [];
  }
}

function addOperationLine(line = {}, target = null) {
  const container = document.getElementById("op-lines");
  if (!container) {
    return;
  }

  const model = {
    _clientId: line._clientId || line.operationLineId || createUuid(),
    operationLineId: line.operationLineId || null,
    skuId: line.skuId || "",
    entryMode: line.entryMode || "Packs",
    section: line.section || "ChangeOut",
    packQuantity: line.packQuantity ?? 1,
    pieceQuantity: line.pieceQuantity ?? null,
    unitPrice: line.unitPrice ?? 0,
    isBonus: Boolean(line.isBonus),
    lotNumber: line.lotNumber || null,
    expiryDate: line.expiryDate || null,
    notes: line.notes || null,
    skuCode: line.skuCode || null,
    productName: line.productName || null
  };
  if (!line._fromModel) {
    syncCurrentOperationPage();
    operationEditorLines.push(model);
    operationEditorLineById.set(model._clientId, model);
    operationEditorPage = Math.max(1, Math.ceil(operationEditorLines.length / operationEditorPageSize));
    renderOperationEditorPage();
    return;
  }
  line = model;
  const row = document.createElement("div");
  row.className = "line-editor-row";
  row.dataset.operationLineKey = line._clientId;
  row.dataset.operationLineId = line.operationLineId || "";
  row.innerHTML = `
    <input class="op-line-sku" type="hidden" value="">
    <div class="field op-line-finder"><label>${escapeHtml(foundationT("app.inline.findStock"))}</label><input class="input op-line-search" autocomplete="off" placeholder="${escapeHtml(foundationT("app.attribute.productColorPowerSKU"))}"><div class="op-line-search-results" hidden></div></div>
    <div class="field"><label>${escapeHtml(foundationT("app.product"))}</label><select class="select op-line-product" required></select></div>
    <div class="field"><label>${escapeHtml(foundationT("app.inline.power"))}</label><select class="select op-line-power" required><option value="">${escapeHtml(foundationT("app.inline.power"))}</option></select></div>
    <div class="field"><label>${escapeHtml(foundationT("app.inline.color"))}</label><select class="select op-line-color" required><option value="">${escapeHtml(foundationT("app.inline.color"))}</option></select></div>
    <div class="field"><label>${escapeHtml(foundationT("app.inline.package"))}</label><select class="select op-line-size"><option value="">${escapeHtml(foundationT("app.inline.package"))}</option></select></div>
    <div class="op-line-resolved full-span"><span class="muted-text">${escapeHtml(foundationT("app.inline.selectProductAttributesToResolveSKU"))}</span></div>
    <div class="field op-line-section-field"><label>${escapeHtml(foundationT("app.inline.side"))}</label><select class="select op-line-section"><option value="ChangeOut">${escapeHtml(foundationT("app.returned"))}</option><option value="ChangeIn">${escapeHtml(foundationT("app.inline.replacement"))}</option></select></div>
    <div class="field"><label>${escapeHtml(foundationT("app.inline.mode"))}</label><select class="select op-line-entry-mode"><option value="Packs">${escapeHtml(foundationT("app.inline.packs"))}</option><option value="Pieces">${escapeHtml(foundationT("app.inline.pieces"))}</option></select></div>
    <div class="field"><label>${escapeHtml(foundationT("reports.sortQuantity"))}</label><input class="input op-line-qty" type="number" min="1" step="1" value="${escapeHtml(line.packQuantity || line.pieceQuantity || 1)}" required></div>
    <div class="field op-line-sale-field"><label>${escapeHtml(foundationT("supply.unitPrice"))}</label><input class="input op-line-price" type="number" min="0" step="0.01" value="${escapeHtml(line.unitPrice || 0)}"></div>
    <label class="check-field op-line-sale-field"><input class="op-line-bonus" type="checkbox" ${line.isBonus ? "checked" : ""}><span>${escapeHtml(foundationT("app.inline.bonus"))}</span></label>
    <div class="field op-line-stock-field"><label>${escapeHtml(foundationT("app.inline.batchExpiry"))}</label><select class="select op-line-stock-option"><option value="">${escapeHtml(foundationT("app.inline.selectSourceAndSKU"))}</option></select></div>
    <div class="field op-line-receipt-field"><label>${escapeHtml(foundationT("app.lot"))}</label><input class="input op-line-lot" maxlength="100" value="${escapeHtml(line.lotNumber || "")}"></div>
    <div class="field op-line-receipt-field"><label>${escapeHtml(foundationT("app.batchExpiry"))}</label><input class="input op-line-expiry" type="date" value="${escapeHtml(line.expiryDate || "")}"></div>
    <button class="icon-button op-remove-line" type="button" title="${escapeHtml(foundationT("supply.removeLine"))}">x</button>`;
  populateOperationProductOptions(row);
  row.querySelector(".op-line-section").value = line.section || "ChangeOut";
  row.querySelector(".op-line-entry-mode").value = line.entryMode || "Packs";
  (target || container).appendChild(row);
  if (line.skuId) {
    seedOperationLineSkuSelection(row, line.skuId);
  } else {
    populateOperationAttributeOptions(row);
    resolveOperationLineSku(row);
  }
  if (!operationEditorRendering) {
    syncOperationLineControls(document.getElementById("op-type")?.value || operationsUiState.operationType || "WarehouseTransfer");
  }
  if (line.lotNumber !== undefined || line.expiryDate !== undefined) {
    row.querySelector(".op-line-lot").value = line.lotNumber || "";
    row.querySelector(".op-line-expiry").value = line.expiryDate || "";
    primeOperationStockOption(row);
  }
}

function isOperationStockConsumingType(type) {
  return ["WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve", "WriteOff"].includes(type);
}

function isOperationBatchSelectionType(type) {
  return ["WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve", "WriteOff"].includes(type);
}

function readOperationLineRow(row) {
  const mode = row.querySelector(".op-line-entry-mode").value;
  const skuId = row.querySelector(".op-line-sku").value;
  const existing = operationEditorLineById.get(row.dataset.operationLineKey);
  const selectedSku = operationSkuOptions.find((value) => value.id === skuId);
  return {
    operationLineId: row.dataset.operationLineId || null,
    skuId,
    packQuantity: mode === "Pieces" ? 0 : Number(row.querySelector(".op-line-qty").value),
    pieceQuantity: mode === "Pieces" ? Number(row.querySelector(".op-line-qty").value) : null,
    entryMode: mode,
    section: row.querySelector(".op-line-section").value,
    unitPrice: Number(row.querySelector(".op-line-price").value || 0),
    isBonus: row.querySelector(".op-line-bonus").checked,
    lotNumber: row.querySelector(".op-line-lot").value.trim() || null,
    expiryDate: row.querySelector(".op-line-expiry").value || null,
    notes: existing?.notes || null,
    skuCode: selectedSku?.skuCode || (existing?.skuId === skuId ? existing?.skuCode : null),
    productName: selectedSku?.productName || (existing?.skuId === skuId ? existing?.productName : null)
  };
}

function syncCurrentOperationPage() {
  document.querySelectorAll("#op-lines .line-editor-row").forEach((row) => {
    const model = operationEditorLineById.get(row.dataset.operationLineKey);
    if (model) Object.assign(model, readOperationLineRow(row));
  });
}

function renderOperationEditorPage() {
  const container = document.getElementById("op-lines");
  if (!container) return;
  const start = (operationEditorPage - 1) * operationEditorPageSize;
  const fragment = document.createDocumentFragment();
  operationEditorRendering = true;
  try {
    operationEditorLines.slice(start, start + operationEditorPageSize)
      .forEach((line) => addOperationLine({ ...line, _fromModel: true }, fragment));
  } finally {
    operationEditorRendering = false;
  }
  container.replaceChildren(fragment);
  const pager = document.getElementById("operation-line-pagination");
  if (!pager) return;
  const pages = Math.max(1, Math.ceil(operationEditorLines.length / operationEditorPageSize));
  pager.hidden = pages <= 1;
  setPagerContents(pager, foundationT("pagination.previous"), foundationT("pagination.showingResults", { from: start + 1, to: Math.min(start + operationEditorPageSize, operationEditorLines.length), total: operationEditorLines.length }), foundationT("pagination.next"), operationEditorPage <= 1, operationEditorPage >= pages, (delta) => {
    syncCurrentOperationPage();
    operationEditorPage += delta;
    renderOperationEditorPage();
  });
  syncOperationLineControls(document.getElementById("op-type")?.value || operationsUiState.operationType);
  applyShopifyCommercialLocks();
}

function renderInventoryPager(tbody, kind, result, loader) {
  const tableWrap = tbody.closest(".table-wrap");
  if (!tableWrap) return;
  let pager = tableWrap.nextElementSibling;
  if (!pager?.matches?.(`[data-inventory-pager="${kind}"]`)) {
    pager = document.createElement("div");
    pager.className = "pagination";
    pager.dataset.inventoryPager = kind;
    tableWrap.insertAdjacentElement("afterend", pager);
  }
  const totalPages = Math.max(1, result.totalPages || Math.ceil((result.totalCount || 0) / (result.pageSize || 50)));
  const page = Math.min(result.page || inventoryPageState[kind] || 1, totalPages);
  inventoryPageState[kind] = page;
  pager.hidden = totalPages <= 1;
  setPagerContents(pager, foundationT("pagination.previous"), foundationT("pagination.pageOf", { page, pages: totalPages }), foundationT("pagination.next"), page <= 1, page >= totalPages, (delta) => {
    inventoryPageState[kind] = page + delta;
    void loader();
  });
}

function setPagerContents(pager, previousLabel, summary, nextLabel, previousDisabled, nextDisabled, onMove) {
  const previous = document.createElement("button");
  previous.className = "button secondary";
  previous.type = "button";
  previous.disabled = previousDisabled;
  previous.textContent = previousLabel;
  previous.addEventListener("click", () => onMove(-1));
  const label = document.createElement("span");
  label.textContent = summary;
  const next = document.createElement("button");
  next.className = "button secondary";
  next.type = "button";
  next.disabled = nextDisabled;
  next.textContent = nextLabel;
  next.addEventListener("click", () => onMove(1));
  pager.replaceChildren(previous, label, next);
}

function isInboundOperationLine(type, row) {
  return type === "InventoryReceipt" || type === "Return" ||
    (type === "Change" && row.querySelector(".op-line-section").value === "ChangeOut");
}

function operationLineBatchSource(type, row) {
  if (type === "InventoryReceipt") {
    return "inventory-inbound";
  }
  if (type === "Return" || (type === "Change" && row.querySelector(".op-line-section").value === "ChangeOut")) {
    return "merchant-inbound";
  }
  return "inventory-outbound";
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
  const products = operationProductOptions;

  select.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.selectProduct"))}</option>${products.map((product) =>
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
    resolved.innerHTML = `<span class="muted-text">${escapeHtml(foundationT("app.inline.selectProductAttributesToResolveSKU"))}</span>`;
    return null;
  }

  const isSolution = product?.productType === "Solution";
  if (!isSolution && (!powerKey || !colorName)) {
    resolved.innerHTML = `<span class="muted-text">${escapeHtml(foundationT("app.inline.selectProductPowerAndColorToResolveSKU"))}</span>`;
    return null;
  }

  const type = document.getElementById("op-type")?.value || operationsUiState.operationType;
  const matches = availableOperationSkusForType(type).filter((sku) =>
    sku.productId === productId &&
    (isSolution || operationPowerKey(sku) === powerKey) &&
    (isSolution || (sku.colorName || "") === colorName) &&
    ((sku.size || "") === (size || "") || (!sku.size && !size)));

  if (matches.length === 0) {
    resolved.innerHTML = `<span class="status-pill status-warn">${escapeHtml(foundationT("app.inventoryNoMatchingSku"))}</span><span class="muted-cell">${escapeHtml(foundationT("app.inline.tryAnotherColorPowerPackageOrSourceLocation"))}</span>`;
    return null;
  }

  if (matches.length > 1) {
    resolved.innerHTML = `<span class="status-pill status-warn">${escapeHtml(foundationT("app.inline.sKUConflict"))}</span><span class="muted-cell">${escapeHtml(foundationT("app.inline.sKUConflictDetail", { count: matches.length }))}</span>`;
    return null;
  }

  const sku = matches[0];
  hidden.value = sku.id;
  resolved.innerHTML = `<span class="status-pill status-ok">${escapeHtml(foundationT("app.inline.resolvedSKU"))}</span><strong>${escapeHtml(sku.skuCode)}</strong><span class="muted-cell">${escapeHtml(sku.productName)}</span>`;
  void refreshOperationStockOptions(row);
  return sku;
}

function seedOperationLineSkuSelection(row, skuId) {
  const sku = operationSkuOptions.find((value) => value.id === skuId);
  if (!sku) {
    const model = operationEditorLineById.get(row.dataset.operationLineKey);
    row.querySelector(".op-line-sku").value = skuId || "";
    row.querySelector(".op-line-resolved").innerHTML = model?.skuCode
      ? `<span class="status-pill status-ok">${escapeHtml(foundationT("app.inline.resolvedSKU"))}</span><strong>${escapeHtml(model.skuCode)}</strong><span class="muted-cell">${escapeHtml(model.productName || "")}</span>`
      : `<span class="status-pill status-warn">${escapeHtml(foundationT("supply.unknownSKU"))}</span><span class="muted-cell">${escapeHtml(shortId(skuId, "SKU"))}</span>`;
    if (model?.skuCode) return;
    void ensureSkuOption(skuId).then((loaded) => {
      if (loaded && row.isConnected) seedOperationLineSkuSelection(row, skuId);
    });
    return;
  }

  row.querySelector(".op-line-product").value = sku.productId;
  populateOperationAttributeOptions(row, {
    powerKey: operationPowerKey(sku),
    colorName: sku.colorName || "",
    size: sku.size || ""
  });
  row.querySelector(".op-line-sku").value = sku.id;
  row.querySelector(".op-line-resolved").innerHTML = `<span class="status-pill status-ok">${escapeHtml(foundationT("app.inline.resolvedSKU"))}</span><strong>${escapeHtml(sku.skuCode)}</strong><span class="muted-cell">${escapeHtml(sku.productName)}</span>`;
}

async function renderOperationSkuSearchResults(row) {
  const input = row.querySelector(".op-line-search");
  const results = row.querySelector(".op-line-search-results");
  const query = input.value.trim().toLowerCase();
  if (!query) {
    results.hidden = true;
    results.replaceChildren();
    return;
  }

  const requestId = (skuSearchRequests.get(row) || 0) + 1;
  skuSearchRequests.set(row, requestId);
  let matches;
  try {
    matches = await searchSkuOptions(query, 20);
  } catch {
    matches = [];
  }
  if (skuSearchRequests.get(row) !== requestId || input.value.trim().toLowerCase() !== query) return;
  const type = document.getElementById("op-type")?.value || operationsUiState.operationType;
  matches = matches.filter((sku) => !isOperationStockConsumingType(type) || operationAvailableSkuIds === null || operationAvailableSkuIds.has(sku.id)).slice(0, 8);

  setupAdaptiveSearchResultDismissal();
  collapseAdaptiveSearchResults(results);
  results.hidden = false;
  results.innerHTML = matches.length === 0
    ? `<button type="button" class="op-line-search-result" disabled>${escapeHtml(foundationT("app.inline.noMatches"))}</button>`
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
    results.replaceChildren();
      clearOperationLineStockFields(row);
      void refreshOperationStockOptions(row);
    });
  });
}

function clearOperationLineStockFields(row) {
  const stockSelect = row.querySelector(".op-line-stock-option");
  if (stockSelect) {
    stockSelect.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.selectBatchExpiry"))}</option>`;
    stockSelect.value = "";
  }
  row.querySelector(".op-line-lot").value = "";
  row.querySelector(".op-line-expiry").value = "";
}

function syncOperationTypeControls() {
  const type = canonicalSelectValue("op-type", "operationType");
  operationsUiState.operationType = type;
  const source = document.getElementById("op-source");
  const destination = document.getElementById("op-destination");
  const previousSource = source.value;
  const previousDestination = destination.value;
  const main = operationLocations.find((location) => location.locationType === "MainWarehouse");
  const nonMain = operationLocations.filter((location) => location.locationType !== "MainWarehouse");

  if (type === "InventoryReceipt") {
    setSelectOptionsPreservingValue(source, [{ value: "", label: foundationT("app.inline.externalSupplier") }], previousSource);
    setSelectOptionsPreservingValue(destination, main ? [{ value: main.id, label: main.name }] : [{ value: "", label: foundationT("app.inline.mainWarehouseUnavailable") }], previousDestination);
    source.disabled = true;
    destination.disabled = true;
    document.getElementById("op-supplier").disabled = false;
    document.getElementById("op-invoice").disabled = false;
    setOperationFieldGroupVisibility({ merchant: false, rep: false, buyer: false, payment: false, receipt: true });
    syncOperationLineControls(type);
    applyOperationEditorMode();
    return;
  }

  setSelectOptionsPreservingValue(source, main ? [{ value: main.id, label: main.name }] : [{ value: "", label: foundationT("app.inline.mainWarehouseUnavailable") }], previousSource);
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
    primeAllOperationStockOptions();
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
    primeAllOperationStockOptions();
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
  select.innerHTML = options.map((option) => `<option value="${escapeHtml(option.value)}">${escapeHtml(uiText(option.label))}</option>`).join("");
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
      source.title = foundationT("operations.routeFixed");
    }
    if (destination.value) {
      destination.title = foundationT("operations.routeFixed");
    }
  }
}

function setOperationFieldGroupVisibility({ merchant, rep, buyer, payment, receipt }) {
  setFieldGroupState(".op-merchant-field", merchant);
  setFieldGroupState(".op-rep-field", rep);
  setFieldGroupState(".op-buyer-field", buyer);
  setFieldGroupState(".op-payment-field", payment);
  setRequiredWhenVisible(document.getElementById("op-payment"), payment);
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

function setRequiredWhenVisible(control, required) {
  if (!control) {
    return;
  }
  control.required = Boolean(required);
  if (!required) {
    control.removeAttribute("required");
    control.setAttribute("aria-required", "false");
    control.value = "";
    return;
  }
  control.setAttribute("required", "");
  control.setAttribute("aria-required", "true");
}

function syncOperationLineControls(type) {
  const isSale = ["WholesaleSale", "RetailSale"].includes(type);
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

    stockField.hidden = false;
    stockSelect.disabled = false;
    syncOperationBatchEntryFields(row);

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
  if (isOperationBatchSelectionType(type)) {
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

function primeOperationStockOption(row) {
  const select = row.querySelector(".op-line-stock-option");
  if (!select) return;
  operationStockOptionRequests.get(select)?.abort();
  operationStockOptionRequests.delete(select);
  delete select.dataset.lookupKey;
  const lotNumber = row.querySelector(".op-line-lot")?.value || "";
  const expiryDate = row.querySelector(".op-line-expiry")?.value || "";
  const current = encodeStockOption({ lotNumber, expiryDate });
  select.replaceChildren();
  const option = document.createElement("option");
  option.value = current;
  option.textContent = current ? `${expiryDate || foundationT("app.inline.noExpiry")} / ${lotNumber || foundationT("app.inline.noLot")}` : foundationT("app.inline.openToLoadAvailableBatches");
  select.appendChild(option);
}

function primeAllOperationStockOptions() {
  document.querySelectorAll("#op-lines .line-editor-row").forEach(primeOperationStockOption);
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
    primeAllOperationStockOptions();
    return;
  }

  try {
    const ids = await request(`/api/v1/inventory/available-sku-ids?locationId=${encodeURIComponent(sourceId)}`);
    operationAvailableSkuIds = new Set(ids || []);
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
  primeAllOperationStockOptions();
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
    title.textContent = foundationT("app.message.editDraft");
    hint.textContent = document.getElementById("operation-form")?.dataset.shopifyDraft === "true"
      ? "Shopify commercial data is read-only. Select the required batch and expiry, then fulfill the draft."
      : "Update the existing draft without changing its operation type.";
    mode.textContent = foundationT("app.message.draftEdit");
    submit.textContent = foundationT("app.message.saveDraftChanges");
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
    title.textContent = foundationT("app.message.reviseOperation");
    hint.textContent = foundationT("app.message.reapplyThisOperationWithARequiredReasonStockAnd");
    mode.textContent = foundationT("app.message.revision");
    submit.textContent = foundationT("app.message.submitRevision");
    typeControl.disabled = true;
    if (revisionField) {
      revisionField.hidden = false;
    }
    return;
  }

  title.textContent = foundationT("app.inline.createDraft");
  hint.textContent = foundationT("app.inline.startANewOperationDraft");
  mode.textContent = foundationT("app.inline.create");
  submit.textContent = foundationT("common.saveDraft");
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
  ["op-source", "op-destination", "op-merchant", "op-buyer", "op-buyer-phone", "op-payment", "op-supplier", "op-invoice", "op-notes", "op-add-line"].forEach((id) => {
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
  operationsUiState.concurrencyVersion = null;
  operationsUiState.revisionFingerprint = null;
  operationsUiState.revisionReason = "";
  const form = document.getElementById("operation-form");
  if (form) {
    form.reset();
    delete form.dataset.shopifyDraft;
  }
  const lines = document.getElementById("op-lines");
  if (lines) {
  lines.replaceChildren();
    operationEditorLines = [];
    operationEditorLineById.clear();
    operationEditorPage = 1;
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
  operationsUiState.concurrencyVersion = detail.concurrencyVersion;
  operationsUiState.operationType = detail.operationType;
  operationsUiState.revisionFingerprint = mode === "revise" ? canonicalOperationPayload({
    operationType: detail.operationType,
    sourceLocationId: detail.sourceLocationId,
    destinationLocationId: detail.destinationLocationId,
    merchantId: detail.clientId,
    representativeId: null,
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
  const buyer = document.getElementById("op-buyer");
  const buyerPhone = document.getElementById("op-buyer-phone");
  const payment = document.getElementById("op-payment");
  const notes = document.getElementById("op-notes");
  if (merchant) {
    // CRM options hydrate asynchronously when the editor first renders. Keep
    // an explicitly selected merchant addressable while editing a persisted
    // operation instead of silently clearing the canonical ClientId.
    if (detail.clientId && !merchant.querySelector(`option[value="${CSS.escape(detail.clientId)}"]`)) {
      const option = document.createElement("option");
      option.value = detail.clientId;
      option.textContent = detail.clientName || detail.merchantName || detail.clientId;
      merchant.appendChild(option);
    }
    merchant.value = detail.clientId || "";
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
  const financeAccount = document.getElementById("op-finance-account");
  if (financeAccount) financeAccount.value = detail.financeAccountId || "";
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

  operationEditorLines = (detail.lines || []).map((line) => ({
    _clientId: line.id || createUuid(),
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
    notes: line.notes,
    skuCode: line.skuCode,
    productName: line.productName,
    lineTotal: line.lineTotal,
    merchantNameSnapshot: line.merchantNameSnapshot,
    representativeNameSnapshot: line.representativeNameSnapshot,
    shopifyLineItemId: line.shopifyLineItemId,
    shopifyVariantId: line.shopifyVariantId,
    shopifySku: line.shopifySku,
    shopifyTitle: line.shopifyTitle,
    shopifyVariantTitle: line.shopifyVariantTitle,
    shopifyProperties: line.shopifyProperties
  }));
  operationEditorLineById = new Map(operationEditorLines.map((line) => [line._clientId, line]));
  operationEditorPage = 1;
  if (operationEditorLines.length === 0) {
    addOperationLine();
  } else {
    renderOperationEditorPage();
  }
  primeAllOperationStockOptions();
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
    const detail = await request(`/api/v1/operations/${operationId}/editor`);
    seedOperationEditor(detail, mode);
    notice(mode === "edit" ? "Draft loaded into the editor." : "Operation loaded for revision.", "success");
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function refreshOperationStockOptions(row) {
  const type = document.getElementById("op-type")?.value;
  const select = row.querySelector(".op-line-stock-option");
  if (!select || !type) {
    return;
  }

  const batchSource = operationLineBatchSource(type, row);
  const locationId = batchSource === "inventory-inbound"
    ? document.getElementById("op-destination")?.value
    : document.getElementById("op-source")?.value;
  const merchantId = document.getElementById("op-merchant")?.value;
  const skuId = row.querySelector(".op-line-sku")?.value;
  const entryMode = type === "RetailSale" ? row.querySelector(".op-line-entry-mode").value : "Packs";
  const current = encodeStockOption({
    lotNumber: row.querySelector(".op-line-lot").value || null,
    expiryDate: row.querySelector(".op-line-expiry").value || null
  });
  const lookupKey = `${batchSource}|${locationId || ""}|${merchantId || ""}|${skuId || ""}|${entryMode}`;
  if (select.dataset.lookupKey === lookupKey && select.options.length > 1) return;

  if (!skuId || (batchSource === "merchant-inbound" ? !merchantId : !locationId)) {
    select.innerHTML = `<option value="">${escapeHtml(foundationT(batchSource === "merchant-inbound" ? "app.inline.selectMerchantAndSKU" : "app.inline.selectLocationAndSKU"))}</option>`;
    syncOperationBatchEntryFields(row);
    return;
  }

  select.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.loadingBatches2"))}</option>`;
  operationStockOptionRequests.get(select)?.abort();
  const controller = new AbortController();
  operationStockOptionRequests.set(select, controller);
  const signal = typeof AbortSignal.any === "function" ? AbortSignal.any([controller.signal, activeRouteController.signal]) : controller.signal;
  const loadCached = async (loader) => {
    const cached = operationBatchOptionCache.get(lookupKey);
    if (cached?.expiresAt > Date.now()) return cached.value;
    const value = await loader();
    operationBatchOptionCache.set(lookupKey, { value, expiresAt: Date.now() + 15000 });
    return value;
  };
  try {
    let options;
    if (batchSource === "merchant-inbound") {
      const history = await loadCached(() => request(`/api/v1/operations/batch-options/merchant?merchantId=${encodeURIComponent(merchantId)}&skuId=${encodeURIComponent(skuId)}&locationId=${encodeURIComponent(locationId || "")}`, { signal }));
      options = history
        .map((option) => ({
          lotNumber: option.lotNumber,
          expiryDate: option.expiryDate,
          label: `${option.expiryDate} / ${option.lotNumber} / ${foundationT("app.inline.recordedBalance", { count: option.recordedBalanceQuantity || 0 })}`
        }));
    } else if (batchSource === "inventory-inbound") {
      const result = await loadCached(() => request(`/api/v1/inventory/batches?locationId=${encodeURIComponent(locationId)}&skuId=${encodeURIComponent(skuId)}&includeEmpty=true&pageSize=100`, { signal }));
      options = (result.items || [])
        .filter((option) => option.lotNumber && option.expiryDate)
        .map((option) => ({
          lotNumber: option.lotNumber,
          expiryDate: option.expiryDate,
          label: `${option.expiryDate} / ${option.lotNumber} / ${foundationT("app.inline.packCount", { count: option.packQuantity || 0 })}`
        }));
    } else {
      options = await loadCached(() => request(`/api/v1/inventory/stock-options?locationId=${encodeURIComponent(locationId)}&skuId=${encodeURIComponent(skuId)}&entryMode=${encodeURIComponent(entryMode)}`, { signal }));
    }

    if (controller.signal.aborted || operationStockOptionRequests.get(select) !== controller) return;
    select.dataset.lookupKey = lookupKey;

    const uniqueOptions = Array.from(new Map(options.map((option) => [encodeStockOption(option), option])).values());
    const createNewOption = isInboundOperationLine(type, row)
      ? `<option value="__new_batch__">${escapeHtml(foundationT("app.inline.createNewBatch"))}</option>`
      : "";
    select.innerHTML = `<option value="">${escapeHtml(uniqueOptions.length ? foundationT("app.inline.selectBatchExpiry") : foundationT("app.inline.noMatchingBatch"))}</option>${uniqueOptions.map((option) => {
      const value = encodeStockOption(option);
      const quantity = batchSource === "inventory-outbound" && entryMode === "Pieces" && option.pieceQuantity != null
        ? foundationT("app.inline.pieceCount", { count: option.pieceQuantity })
        : foundationT("app.inline.packCount", { count: option.packQuantity });
      const loose = batchSource === "inventory-outbound" && entryMode === "Pieces" && option.loosePieceQuantity > 0 ? `, ${foundationT("app.inline.loosePieceCount", { count: option.loosePieceQuantity })}` : "";
      const label = option.label || `${option.expiryDate} / ${option.lotNumber} / ${quantity}${loose}`;
      return `<option value="${escapeHtml(value)}">${escapeHtml(label)}</option>`;
    }).join("")}${createNewOption}`;
    if (current && Array.from(select.options).some((option) => option.value === current)) {
      select.value = current;
    } else if (current && isInboundOperationLine(type, row)) {
      select.value = "__new_batch__";
    } else {
      row.querySelector(".op-line-lot").value = "";
      row.querySelector(".op-line-expiry").value = "";
    }
    syncOperationBatchEntryFields(row);
  } catch (exception) {
    if (exception?.name === "AbortError") return;
    select.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.failedToLoadBatches"))}</option>${isInboundOperationLine(type, row) ? `<option value="__new_batch__">${escapeHtml(foundationT("app.inline.createNewBatch"))}</option>` : ""}`;
    syncOperationBatchEntryFields(row);
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function applySelectedStockOption(row) {
  const select = row.querySelector(".op-line-stock-option");
  if (select?.value === "__new_batch__") {
    row.querySelector(".op-line-lot").value = "";
    row.querySelector(".op-line-expiry").value = "";
    syncOperationBatchEntryFields(row);
    row.querySelector(".op-line-lot").focus();
    return;
  }
  if (!select?.value) {
    row.querySelector(".op-line-lot").value = "";
    row.querySelector(".op-line-expiry").value = "";
    syncOperationBatchEntryFields(row);
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
  syncOperationBatchEntryFields(row);
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
    const showCompleted = document.getElementById("operations-show-completed")?.checked;
    const params = new URLSearchParams({
      page: String(operationListPage),
      pageSize: document.getElementById("operations-page-size")?.value || "50",
      includeCompleted: showCompleted ? "true" : "false"
    });
    const search = document.getElementById("operations-search")?.value.trim();
    const type = document.getElementById("operations-type")?.value;
    const status = document.getElementById("operations-status")?.value;
    const from = document.getElementById("operations-from")?.value;
    const to = document.getElementById("operations-to")?.value;
    if (search) params.set("search", search);
    if (type) params.set("operationType", type);
    if (status) params.set("status", status);
    if (from) params.set("createdFrom", from);
    if (to) params.set("createdTo", to);
    if (currentPath() === "/operations") {
      history.replaceState(null, "", `#/operations?${params.toString()}`);
    }
    const result = await request(`/api/v1/operations?${params.toString()}`);
    const items = result.items;
    count.textContent = foundationT(showCompleted ? "app.count.operations" : "app.count.activeOperations", { count: result.totalCount });
    tbody.innerHTML = items.length === 0 ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.noActiveOperations"))}</td></tr>` : items.map((operation) => `
      <tr data-operation-id="${escapeHtml(operation.id)}" data-operation-number="${escapeHtml(operation.operationNumber)}" data-operation-type="${escapeHtml(operation.operationType)}" data-operation-status="${escapeHtml(operation.status)}">
        <td><strong>${escapeHtml(operation.operationNumber)}</strong>${operation.salesChannel === "Shopify" ? `<span class="status-pill status-warn">Shopify${operation.shopifyOrderNumber ? ` ${escapeHtml(operation.shopifyOrderNumber)}` : ""}</span>` : ""}${operation.allocationPending ? `<span class="status-pill status-muted">${escapeHtml(foundationT("app.inline.allocationPending"))}</span>` : ""}</td>
        <td>${escapeHtml(uiText(operation.operationType))}</td>
        <td><span class="status-pill ${operationStatusClass(operation.status)}">${escapeHtml(uiText(operation.status))}</span></td>
        <td>${escapeHtml(formatOperationRoute(operation))}</td>
        <td>${escapeHtml(formatDateTime(operation.createdAt))}</td>
        <td>${renderOperationActions(operation, canWrite)}</td>
      </tr>
      <tr class="operation-detail-row" id="operation-detail-${escapeHtml(operation.id)}" hidden><td colspan="6"><div class="operation-detail">${escapeHtml(foundationT("common.loading"))}</div></td></tr>`).join("");
    tbody.querySelectorAll("[data-op-toggle]").forEach((button) => button.addEventListener("click", () => toggleOperationDetails(button.dataset.opId, button)));
    tbody.querySelectorAll("[data-op-action]").forEach((button) => button.addEventListener("click", () => runOperationAction(button.dataset.opAction, button.dataset.opId, button)));
    tbody.querySelectorAll("[data-op-edit]").forEach((button) => button.addEventListener("click", () => startOperationEditorMode(button.dataset.opEdit, "edit")));
    tbody.querySelectorAll("[data-op-revise]").forEach((button) => button.addEventListener("click", () => startOperationEditorMode(button.dataset.opRevise, "revise")));
    bindPrintReportButtons(tbody);
    const pager = document.getElementById("operation-list-pagination");
    if (pager) {
      const pages = Math.max(1, result.totalPages || 1);
      operationListPage = Math.min(result.page || operationListPage, pages);
      pager.hidden = pages <= 1;
      setPagerContents(pager, foundationT("pagination.previous"), `${operationListPage} / ${pages}`, foundationT("pagination.next"), operationListPage <= 1, operationListPage >= pages, (delta) => { operationListPage += delta; void loadOperations(); });
    }
    for (const operationId of operationsUiState.openDetailIds) {
      const toggle = tbody.querySelector(`[data-op-toggle][data-op-id="${operationId}"]`);
      if (toggle) {
        // eslint-disable-next-line no-await-in-loop
        await toggleOperationDetails(operationId, toggle, true);
      }
    }
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="6">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function submitOperationEditor(event) {
  event.preventDefault();
  const type = canonicalSelectValue("op-type", "operationType");
  const lines = readOperationLines(type);
  const isShopifyDraft = operationsUiState.mode === "edit" && document.getElementById("operation-form")?.dataset.shopifyDraft === "true";
  if (isShopifyDraft && operationsUiState.operationId) {
    if (lines.some((line) => !line.operationLineId || !line.lotNumber || !line.expiryDate || !line.stockOptionSelected)) {
      navigateToInvalidOperationLine(lines.findIndex((line) => !line.operationLineId || !line.lotNumber || !line.expiryDate || !line.stockOptionSelected));
      notice(foundationT("app.message.selectABatchAndExpiryForEveryShopifyLine"), "error");
      return;
    }
    try {
      await request(`/api/v1/operations/${operationsUiState.operationId}/shopify-allocation`, {
        method: "PUT",
        body: JSON.stringify({ expectedVersion: operationsUiState.concurrencyVersion, lines: lines.map((line) => ({ operationLineId: line.operationLineId, lotNumber: line.lotNumber, expiryDate: line.expiryDate })) })
      });
      notice(foundationT("app.message.shopifyBatchAllocationSaved"), "success");
      resetOperationEditorMode();
      await loadOperations();
    } catch (exception) {
      notice(getFriendlyWorkspaceError(exception), "error");
    }
    return;
  }
  const validationMessage = validateOperationForm(type, lines);
  if (validationMessage) {
    navigateToInvalidOperationLine(findInvalidOperationLineIndex(type, lines));
    notice(validationMessage, "error");
    return;
  }
  const payloadLines = lines.map(({ stockOptionSelected, ...line }) => line);

  const body = {
    operationType: type,
    sourceLocationId: document.getElementById("op-source").value || null,
    destinationLocationId: document.getElementById("op-destination").value || null,
    merchantId: ["WholesaleSale", "RetailSale", "Return", "Change"].includes(type) ? document.getElementById("op-merchant").value || null : null,
    representativeId: null,
    buyerName: type === "RetailSale" ? document.getElementById("op-buyer").value || null : null,
    buyerPhone: type === "RetailSale" ? document.getElementById("op-buyer-phone").value || null : null,
    paymentMethod: ["WholesaleSale", "RetailSale", "Return", "Change"].includes(type) ? canonicalSelectValue("op-payment", "paymentMethod", { allowEmpty: true }) || null : null,
    financeAccountId: ["WholesaleSale", "RetailSale"].includes(type) ? document.getElementById("op-finance-account")?.value || null : null,
    notes: document.getElementById("op-notes").value || null,
    receipt: type === "InventoryReceipt" ? { supplierName: document.getElementById("op-supplier").value || "Supplier", invoiceNumber: document.getElementById("op-invoice").value || null } : null,
    lines: payloadLines,
    expectedVersion: ["edit", "revise"].includes(operationsUiState.mode) ? operationsUiState.concurrencyVersion : null
  };

  if (operationsUiState.mode === "revise" && operationsUiState.operationId) {
    const reason = document.getElementById("op-revision-reason")?.value?.trim();
    if (!reason) {
      notice(foundationT("app.message.revisionReasonIsRequired"), "error");
      return;
    }
  }

  if (operationsUiState.mode === "revise" && operationsUiState.operationId && operationsUiState.revisionFingerprint === canonicalOperationPayload(body)) {
    notice(foundationT("app.message.noChangesDetectedOperationWasNotRevised"), "success");
    resetOperationEditorMode();
    return;
  }

  try {
    if (operationsUiState.mode === "edit" && operationsUiState.operationId) {
      await request(`/api/v1/operations/${operationsUiState.operationId}`, { method: "PUT", body: JSON.stringify(body) });
      notice(foundationT("app.message.draftUpdated"), "success");
    } else if (operationsUiState.mode === "revise" && operationsUiState.operationId) {
      const reason = document.getElementById("op-revision-reason").value?.trim();
      await request(`/api/v1/operations/${operationsUiState.operationId}/revise`, {
        method: "POST",
        body: JSON.stringify({ operation: body, reason })
      });
      notice(foundationT("app.message.operationRevised"), "success");
    } else {
      await request("/api/v1/operations", { method: "POST", body: JSON.stringify(body) });
      notice(foundationT("app.message.draftSaved"), "success");
    }
    resetOperationEditorMode();
    await loadOperations();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function findInvalidOperationLineIndex(type, lines) {
  const keys = new Map();
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    const quantity = line.entryMode === "Pieces" ? line.pieceQuantity : line.packQuantity;
    if (!line.skuId || !line.stockOptionSelected || !line.lotNumber || !line.expiryDate || !Number.isInteger(quantity) || quantity < 1) return index;
    if (["WholesaleSale", "RetailSale"].includes(type) && !line.isBonus && (!Number.isFinite(line.unitPrice) || line.unitPrice <= 0)) return index;
    const key = operationLineUniquenessKey(type, line);
    if (keys.has(key)) return index;
    keys.set(key, index);
  }
  return -1;
}

function navigateToInvalidOperationLine(index) {
  if (index < 0) return;
  operationEditorPage = Math.floor(index / operationEditorPageSize) + 1;
  renderOperationEditorPage();
  document.querySelectorAll("#op-lines .line-editor-row")[index % operationEditorPageSize]?.classList.add("supply-line-invalid");
  document.getElementById("operation-line-pagination")?.scrollIntoView({ behavior: "smooth", block: "center" });
}

function readOperationLines(type) {
  syncCurrentOperationPage();
  return operationEditorLines.map((line) => ({
    operationLineId: line.operationLineId || null,
    skuId: line.skuId,
    packQuantity: line.entryMode === "Pieces" ? 0 : Number(line.packQuantity),
    pieceQuantity: line.entryMode === "Pieces" ? Number(line.pieceQuantity) : null,
    entryMode: type === "RetailSale" ? canonicalSystemValue(line.entryMode, "entryMode") : "Packs",
    section: type === "Change" ? canonicalSystemValue(line.section, "lineSection") : null,
    unitPrice: line.isBonus ? 0 : Number(line.unitPrice || 0),
    isBonus: ["WholesaleSale", "RetailSale"].includes(type) ? line.isBonus : false,
    stockOptionSelected: Boolean(line.lotNumber || line.expiryDate),
    expiryDate: line.expiryDate || null,
    lotNumber: line.lotNumber || null,
    notes: line.notes || null
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
  if (lines.some((line) => !line.stockOptionSelected || !line.lotNumber || !line.expiryDate)) {
    return foundationT("app.message.selectBatchExpiryEveryOperationLine");
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
  if (["InventoryReceipt", "WarehouseTransfer"].includes(type) && !main) {
    return "MainWarehouse must exist before operations can be created.";
  }
  if (type === "InventoryReceipt" && destination !== main.id) {
    return "Inventory receipt destination must be MainWarehouse.";
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
  if (type === "Return" && !document.getElementById("op-merchant").value) {
    return "Return requires a merchant.";
  }
  if (type === "Change") {
    if (!document.getElementById("op-merchant").value) {
      return "Change requires a merchant.";
    }
    if (!lines.some((line) => line.section === "ChangeOut") || !lines.some((line) => line.section === "ChangeIn")) {
      return "Change needs at least one returned line and one replacement line.";
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
  const detailButton = `<button class="button secondary table-action" type="button" data-op-toggle="details" data-op-id="${escapeHtml(operation.id)}">${escapeHtml(foundationT("payments.details"))}</button>`;
  const printButton = `<button class="button secondary table-action" type="button" data-print-report="operation-bill" data-print-id="${escapeHtml(operation.id)}" data-print-code="${escapeHtml(operation.operationNumber)}">${escapeHtml(foundationT("payments.print"))}</button>`;
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
        return `<button class="button secondary table-action" type="button" data-op-edit="${escapeHtml(operation.id)}">${escapeHtml(uiText(label))}</button>`;
      }
      if (action === "revise") {
        return `<button class="button secondary table-action" type="button" data-op-revise="${escapeHtml(operation.id)}">${escapeHtml(uiText(label))}</button>`;
      }
      return `<button class="button secondary table-action" type="button" data-op-action="${action}" data-op-id="${escapeHtml(operation.id)}">${escapeHtml(uiText(label))}</button>`;
    }).join(" ")}`;
}

async function toggleOperationDetails(operationId, button, forceOpen = false) {
  const row = document.getElementById(`operation-detail-${operationId}`);
  if (!row) {
    return;
  }

  if (!row.hidden && !forceOpen) {
    row.hidden = true;
    button.textContent = foundationT("payments.details");
    operationsUiState.openDetailIds = operationsUiState.openDetailIds.filter((value) => value !== operationId);
    return;
  }

  row.hidden = false;
  button.textContent = foundationT("payments.hide");
  if (!operationsUiState.openDetailIds.includes(operationId)) {
    operationsUiState.openDetailIds.push(operationId);
  }
  const target = row.querySelector(".operation-detail");
  if (target.dataset.loaded === "true") {
    return;
  }

  target.innerHTML = `<span class="muted-text">${escapeHtml(foundationT("app.inline.loadingOperationDetails"))}</span>`;
  try {
    await loadOperationDetailPage(operationId, target, 1);
    target.dataset.loaded = "true";
  } catch (exception) {
    target.innerHTML = `<span class="muted-text">${escapeHtml(getFriendlyWorkspaceError(exception))}</span>`;
  }
}

async function loadOperationDetailPage(operationId, target, page) {
    const [detail, lineResult, allocationResult, versionResult] = await Promise.all([
      request(`/api/v1/operations/${operationId}?includeCollections=false`),
      request(`/api/v1/operations/${operationId}/lines?page=${page}&pageSize=50`),
      request(`/api/v1/operations/${operationId}/allocations?page=1&pageSize=50`),
      request(`/api/v1/operations/${operationId}/versions?page=1&pageSize=25`)
    ]);
    detail.lines = lineResult.items || [];
    detail.allocations = allocationResult.items || [];
    detail.versions = versionResult.items || [];
    target.innerHTML = renderOperationDetail(detail);
    const pages = Math.max(1, lineResult.totalPages || 1);
    if (pages > 1) {
      const pager = document.createElement("div");
      pager.className = "pagination";
      setPagerContents(pager, foundationT("pagination.previous"), `${lineResult.page} / ${pages}`, foundationT("pagination.next"), lineResult.page <= 1, lineResult.page >= pages, (delta) => void loadOperationDetailPage(operationId, target, lineResult.page + delta));
      target.appendChild(pager);
    }
}

function renderOperationDetail(detail) {
  const lines = detail.lines || [];
  const allocations = dedupeOperationAllocations(detail.allocations || []);
  const versions = detail.versions || [];
  return `
    <div class="operation-detail-grid">
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.operationCode"))}</span><strong>${escapeHtml(detail.operationNumber)}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("payments.status"))}</span><strong>${escapeHtml(uiText(detail.status))}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("payments.type"))}</span><strong>${escapeHtml(uiText(detail.operationType))}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("app.created"))}</span><strong>${escapeHtml(formatDateTime(detail.createdAt))}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("payments.confirmed"))}</span><strong>${escapeHtml(formatDateTime(detail.confirmedAt) || "-")}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("payments.createdBy"))}</span><strong>${escapeHtml(detail.createdByName || detail.createdBy || "-")}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.confirmedBy"))}</span><strong>${escapeHtml(detail.confirmedByName || detail.confirmedBy || "-")}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.lastEditedBy"))}</span><strong>${escapeHtml(detail.lastEditedByName || "-")}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.route"))}</span><strong>${escapeHtml(formatOperationRoute(detail))}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.merchantBuyer"))}</span><strong>${escapeHtml(detail.clientName || "-")}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("payments.payment"))}</span><strong>${escapeHtml(movementMethodLabel(detail.paymentMethod))}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.channel"))}</span><strong>${escapeHtml(uiText(detail.salesChannel || foundationT("app.inline.manual")))}${detail.shopifyOrderNumber ? ` / ${escapeHtml(detail.shopifyOrderNumber)}` : ""}</strong></div>
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.buyerContact"))}</span><strong>${escapeHtml([detail.buyerPhone, detail.buyerEmail].filter(Boolean).join(" / ") || "-")}</strong></div>
      ${detail.shippingAddress ? `<div class="metric"><span>${escapeHtml(foundationT("app.inline.shippingAddress"))}</span><strong>${escapeHtml(detail.shippingAddress)}</strong></div>` : ""}
      ${detail.allocationPending ? `<div class="metric"><span>${escapeHtml(foundationT("payments.allocation"))}</span><strong><span class="status-pill status-warn">${escapeHtml(foundationT("app.inline.batchAllocationPending"))}</span></strong></div>` : ""}
      <div class="metric"><span>${escapeHtml(foundationT("app.inline.currentVersion"))}</span><strong>${escapeHtml(detail.currentVersionNumber || "-")}</strong></div>
    </div>
    ${detail.notes ? `<p class="muted-text">${escapeHtml(detail.notes)}</p>` : ""}
    ${detail.correctionReason ? `<p class="muted-text"><strong>${escapeHtml(foundationT("operations.correctionReason"))}:</strong> ${escapeHtml(detail.correctionReason)}</p>` : ""}
    <div class="table-wrap compact-table"><table><thead><tr><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("app.product"))}</th><th>${escapeHtml(foundationT("app.inline.shopifyLine"))}</th><th>${escapeHtml(foundationT("app.wearCycle"))}</th><th>${escapeHtml(foundationT("app.inline.side"))}</th><th>${escapeHtml(foundationT("reports.sortQuantity"))}</th><th>${escapeHtml(foundationT("app.inline.bonus"))}</th><th>${escapeHtml(foundationT("supply.unitPrice"))}</th><th>${escapeHtml(foundationT("payments.total"))}</th><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("app.batchExpiry"))}</th><th>${escapeHtml(foundationT("payments.notes"))}</th></tr></thead><tbody>${lines.length === 0
      ? `<tr><td colspan="12">${escapeHtml(foundationT("app.inline.noLines"))}</td></tr>`
      : lines.map((line) => `<tr>
          <td><strong>${escapeHtml(line.skuCode)}</strong></td>
          <td>${escapeHtml(line.productName)}</td>
          <td>${renderShopifyLineMetadata(line)}</td>
          <td>${renderWearCycle(line.wearCycle, line.wearDuration)}</td>
          <td>${escapeHtml(formatOperationLineSection(line.section))}</td>
          <td>${escapeHtml(line.quantity)} ${escapeHtml(uiText(line.entryMode || "Packs"))}</td>
          <td>${line.bonusQuantity ? `<span class="status-pill status-warn">${escapeHtml(line.bonusQuantity)}</span>` : "-"}</td>
          <td>${escapeHtml(formatMoney(line.unitPrice || 0))}</td>
          <td>${escapeHtml(formatMoney(line.lineTotal || 0))}</td>
          <td>${escapeHtml(line.lotNumber || "-")}</td>
          <td>${line.expiryDate ? expiryBadge(line.expiryDate) : `<span class="status-pill status-muted">-</span>`}</td>
          <td>${escapeHtml(line.notes || "-")}</td>
        </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>${escapeHtml(foundationT("app.inline.allocatedSKU"))}</th><th>${escapeHtml(foundationT("reports.sortQuantity"))}</th><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("app.batchExpiry"))}</th></tr></thead><tbody>${allocations.length === 0
      ? `<tr><td colspan="4">${escapeHtml(foundationT("app.inline.noBatchAllocationSnapshot"))}</td></tr>`
      : allocations.map((allocation) => `<tr>
          <td><strong>${escapeHtml(allocation.skuCode || shortId(allocation.skuId, "SKU"))}</strong>${allocation.productName ? `<span class="muted-cell"> / ${escapeHtml(allocation.productName)}</span>` : ""}</td>
          <td>${escapeHtml(foundationT("app.inline.packCount", { count: allocation.quantity }))}</td>
          <td>${escapeHtml(allocation.lotNumber || "-")}</td>
          <td>${allocation.expiryDate ? expiryBadge(allocation.expiryDate) : `<span class="status-pill status-muted">${escapeHtml(foundationT("app.inline.noExpiry"))}</span>`}</td>
        </tr>`).join("")}</tbody></table></div>
    <div class="operation-version-list">${versions.length === 0
      ? `<span class="muted-text">${escapeHtml(foundationT("app.inline.noVersions"))}</span>`
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
  const fallbackLabel = `${foundationT("supply.line")} ${line.shopifyLineItemId}`;
  return `<strong>${escapeHtml(line.shopifySku || "-")}</strong><div class="muted-cell">${escapeHtml(label || fallbackLabel)}</div>${propertyText ? `<div class="muted-cell">${escapeHtml(propertyText)}</div>` : ""}`;
}

function formatOperationLineSection(section) {
  if (section === "ChangeOut") {
    return foundationT("app.returned");
  }
  if (section === "ChangeIn") {
    return foundationT("app.inline.replacement");
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
  const mainWarehouse = foundationT("locations.mainWarehouse");
  if (operation.operationType === "InventoryReceipt") {
    return `${foundationT("locations.external")} → ${operation.destinationLocationName || mainWarehouse}`;
  }
  return `${operation.sourceLocationName || mainWarehouse} → ${operation.destinationLocationName || "-"}`;
}

async function runOperationAction(action, operationId, button, options = {}) {
  return withMutationGuard(`operation:${operationId}:${action}`, button, async () => {
    const previousLabel = button?.textContent;
    if (button) {
      button.textContent = foundationT("app.message.working");
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

function paymentT(key, params) {
  return foundationT(`payments.${key}`, params);
}

async function renderPayments() {
  const auth = getAuth();
  const isAdmin = ["Admin", "ERPAdmin"].includes(auth?.user.role);
  const canDraft = ["Admin", "ERPAdmin", "Accountant"].includes(auth?.user.role);
  const merchants = await loadPaymentMerchants();
  paymentMerchants = merchants;
  paymentAccountants = isAdmin ? await loadPaymentAccountants() : [];
  const financeAccounts = canDraft ? await loadFinanceAccounts() : [];
  const accountantOptions = paymentAccountants.map((user) => `<option value="${escapeHtml(user.id)}">${escapeHtml(user.fullName || user.username)} (${escapeHtml(user.username)})</option>`).join("");
  const financeAccountOptions = financeAccounts.map((account) => `<option value="${escapeHtml(account.id)}">${escapeHtml(account.name)} (${escapeHtml(financeValueLabel("accountType", account.type))})</option>`).join("");
  document.getElementById("view").innerHTML = `
    ${pageIntro({
      eyebrow: paymentT("eyebrow"),
      title: paymentT("title"),
      body: paymentT("intro"),
      metrics: `
        ${scenarioCard(paymentT("merchantAccount"), paymentT("loading"), "status-muted", "merchant-payment-count")}
        ${scenarioCard(paymentT("otherPayments"), paymentT("loading"), "status-muted", "payment-count")}
        ${scenarioCard(paymentT("ledger"), paymentT("loading"), "status-muted", "payment-history-count")}
        ${scenarioCard(paymentT("tools"), canDraft || isAdmin ? paymentT("available") : paymentT("readOnly"), canDraft || isAdmin ? "status-ok" : "status-muted")}
      `
    })}
    ${segmentedControl([
      { target: "merchant-payment-section", label: paymentT("merchantAccountPayments"), view: "merchant" },
      { target: "payment-queue-section", label: paymentT("otherPayments"), view: "other" },
      { target: "payment-review-section", label: paymentT("approvalInbox"), view: "review" },
      { target: "payment-ledger-section", label: paymentT("ledger"), view: "ledger" },
      { target: "payment-tools-section", label: paymentT("tools"), view: "tools" },
      { target: "payment-audit-section", label: paymentT("audit"), view: "audit" }
    ])}
    <section id="merchant-payment-section" data-payment-panel="merchant" class="band payment-queue-band payment-scope-band">
      <div class="section-head">
        <div><h2>${paymentT("merchantAccountPayments")}</h2><p>${paymentT("accountCollections")}</p></div>
      </div>
      <div class="table-wrap"><table><thead><tr><th>${paymentT("payment")}</th><th>${paymentT("merchant")}</th><th>${paymentT("operation")}</th><th>${paymentT("method")}</th><th>${paymentT("total")}</th><th>${paymentT("paid")}</th><th>${paymentT("remaining")}</th><th>${paymentT("status")}</th><th>${paymentT("actions")}</th></tr></thead><tbody id="merchant-payment-rows"><tr><td colspan="9">${paymentT("loadingPayments")}</td></tr></tbody></table></div><div id="merchant-payment-pagination" class="pagination" hidden></div>
    </section>
    <section id="payment-queue-section" data-payment-panel="other" class="band payment-queue-band payment-scope-band">
      <div class="section-head">
        <div><h2>${paymentT("otherPayments")}</h2><p>${paymentT("directRetailCollections")}</p></div>
      </div>
      <div class="toolbar">
        <button id="payments-refresh" class="button secondary" type="button">${foundationT("common.refresh")}</button>
        ${canDraft ? `<button id="other-collection-toggle" class="button" type="button">${paymentT("recordCollection")}</button>` : ""}
        ${isAdmin ? `<select id="payment-accountant" class="select"><option value="">${paymentT("assignTo")}...</option>${accountantOptions}</select>` : ""}
      </div>
      <div class="table-wrap"><table><thead><tr><th>${paymentT("payment")}</th><th>${paymentT("buyer")}</th><th>${paymentT("operation")}</th><th>${paymentT("method")}</th><th>${paymentT("total")}</th><th>${paymentT("paid")}</th><th>${paymentT("remaining")}</th><th>${paymentT("status")}</th><th>${paymentT("actions")}</th></tr></thead><tbody id="payment-rows"><tr><td colspan="9">${paymentT("loadingPayments")}</td></tr></tbody></table></div><div id="payment-pagination" class="pagination" hidden></div>
    </section>
    <section id="payment-review-section" data-payment-panel="review" class="band payment-queue-band payment-scope-band">
      <div class="section-head"><div><h2>${paymentT("approvalInbox")}</h2><p>${paymentT("assignedWork")}</p></div><button id="payment-review-refresh" class="button secondary" type="button">${paymentT("refreshInbox")}</button></div>
      <div class="table-wrap"><table><thead><tr><th>${paymentT("scope")}</th><th>${paymentT("reference")}</th><th>${paymentT("source")}</th><th>${paymentT("assignedTo")}</th><th>${paymentT("amount")}</th><th>${paymentT("method")}</th><th>${paymentT("status")}</th><th>${paymentT("action")}</th></tr></thead><tbody id="payment-review-rows"><tr><td colspan="8">${paymentT("loadingApprovalWork")}</td></tr></tbody></table></div>
    </section>
    <section id="payment-ledger-section" data-payment-panel="other ledger" class="band payment-ledger-band payment-scope-band">
      <div class="section-head">
        <div><h2>${paymentT("otherHistory")}</h2><p>${paymentT("directStages")}</p></div>
      </div>
      <div class="table-wrap"><table><thead><tr><th>${paymentT("updated")}</th><th>${paymentT("payment")}</th><th>${paymentT("buyerMerchant")}</th><th>${paymentT("operation")}</th><th>${paymentT("method")}</th><th>${paymentT("total")}</th><th>${paymentT("status")}</th><th>${paymentT("actor")}</th><th>${paymentT("stages")}</th></tr></thead><tbody id="payment-history-rows"><tr><td colspan="9">${paymentT("loadingHistory")}</td></tr></tbody></table></div><div id="payment-history-pagination" class="pagination" hidden></div>
    </section>
    <section id="payment-tools-section" class="payment-tools-grid">
    ${canDraft ? `
      <section id="unified-collection-card" data-payment-panel="tools" class="band compact-band payment-tool-card unified-collection-card" hidden>
        <div class="section-head"><div><h2>${paymentT("recordCollection")}</h2><p>${paymentT("collectionHelp")}</p></div><span class="status-pill status-warn">${paymentT("adminApprovalRequired")}</span></div>
        <form id="unified-collection-form" class="form grid-form" novalidate>
          <div class="form-error full-span" id="unified-collection-error" hidden></div>
          <div class="field"><label for="collection-source-kind">${paymentT("collectToward")}</label><select id="collection-source-kind" class="select" required><option value="OtherPayments">${paymentT("operationPayment")}</option><option value="MerchantAccount">${paymentT("merchantAccount")}</option></select></div>
          <div class="field" id="collection-source-reference-field"><label for="collection-source-reference">${paymentT("operationReference")}</label><input id="collection-source-reference" class="input" placeholder="${paymentT("referencePlaceholder")}"></div>
          <input id="collection-source-operation-id" type="hidden">
          <div class="field" id="collection-merchant-field" hidden><label for="collection-merchant">${paymentT("merchant")} *</label><select id="collection-merchant" class="select"><option value="">${paymentT("chooseMerchant")}</option>${merchants.map((merchant) => `<option value="${escapeHtml(merchant.id)}">${escapeHtml(merchant.businessName)}</option>`).join("")}</select></div>
          <div class="field"><label for="collection-amount">${paymentT("amountReceived")}</label><input id="collection-amount" class="input" type="number" min="0.01" step="0.01" required></div>
          <div class="field"><label for="collection-method">${paymentT("receivedMethod")}</label><select id="collection-method" class="select" required><option value="">${paymentT("chooseMethod")}</option><option value="CashHandToHand">${paymentT("cashInHand")}</option><option value="CashTransaction">${paymentT("cashTransaction")}</option><option value="BankTransfer">${paymentT("bankTransfer")}</option><option value="Wallet">${paymentT("wallet")}</option></select></div>
          <div class="field"><label for="collection-finance-account">${paymentT("financeAccount")} *</label><select id="collection-finance-account" class="select" required><option value="">${paymentT("chooseFinanceAccount")}</option>${financeAccountOptions}</select></div>
          <div class="field"><label for="collection-transaction-reference">${paymentT("transactionReference")}</label><input id="collection-transaction-reference" class="input" placeholder="${paymentT("electronicReferenceRequired")}"></div>
          <div class="field"><label for="collection-date">${paymentT("dateReceived")}</label><input id="collection-date" class="input" type="date"></div>
          <div class="field full-span"><label for="collection-notes">${paymentT("notes")}</label><input id="collection-notes" class="input"></div>
          <p class="muted-text full-span" id="collection-workflow-help">${paymentT("collectionWorkflowHelp")}</p>
          <div class="form-actions full-span"><button class="button" type="submit" name="collection-action" value="submit">${paymentT("sendForApproval")}</button></div>
        </form>
      </section>` : ""}
      ${canDraft ? `<section data-payment-panel="tools" class="band compact-band payment-tool-card">
        <h2>${paymentT("financialAdjustment")}</h2>
        <p class="muted-text">${paymentT("adjustmentHelp")}</p>
        <form id="financial-adjustment-form" class="form grid-form">
          <div class="form-error full-span" id="financial-adjustment-error" hidden></div>
          <div class="field"><label for="adjustment-merchant">${paymentT("merchant")}</label><select id="adjustment-merchant" class="select" required><option value="">${paymentT("chooseMerchant")}</option>${merchants.map((merchant) => `<option value="${escapeHtml(merchant.id)}">${escapeHtml(merchant.businessName)}</option>`).join("")}</select></div>
          <div class="field"><label for="adjustment-type">${paymentT("type")}</label><select id="adjustment-type" class="select"><option value="AdditionalCharge">${paymentT("additionalCharge")}</option><option value="BalanceReduction">${paymentT("remainingReduction")}</option><option value="CashRefund">${paymentT("cashRefund")}</option></select></div>
          <div class="field"><label for="adjustment-operation-id">${paymentT("affectedOrder")}</label><input id="adjustment-operation-id" class="input" placeholder="${paymentT("orderReferencePlaceholder")}"><p id="adjustment-order-preview" class="muted-text">${paymentT("optionalMiniInvoice")}</p></div>
          <div class="field"><label for="adjustment-amount">${paymentT("amount")}</label><input id="adjustment-amount" class="input" type="number" min="0.01" step="0.01" required></div>
          <div class="field full-span"><label for="adjustment-notes">${paymentT("notes")}</label><input id="adjustment-notes" class="input"></div>
          <button class="button" type="submit">${paymentT("requestAdjustment")}</button>
        </form>
      </section>` : ""}
    <section data-payment-panel="merchant" class="band compact-band payment-tool-card merchant-tool-card">
      <div class="section-head"><div><h2>${paymentT("merchantAccount")}</h2><p>${paymentT("accountSummary")}</p></div><span id="merchant-balance-status" class="muted-text">${paymentT("selectMerchant")}</span></div>
      <div class="toolbar merchant-account-picker"><input id="payment-merchant-search" class="input" type="search" placeholder="${paymentT("searchMerchants")}" aria-label="${paymentT("searchMerchants")}"><select id="payment-merchant" class="select">${merchants.map((merchant) => `<option value="${escapeHtml(merchant.id)}">${escapeHtml(merchant.businessName)}</option>`).join("")}</select><label class="inline-field"><span>${paymentT("from")}</span><input id="merchant-statement-from" class="input" type="date"></label><label class="inline-field"><span>${paymentT("to")}</span><input id="merchant-statement-to" class="input" type="date"></label><button id="load-merchant-balance" class="button secondary" type="button">${paymentT("showAccount")}</button></div>
      <div id="merchant-balance-panel" class="merchant-account-summary"><p class="muted-text">${paymentT("chooseMerchantAccount")}</p></div>
      <section class="workspace-panel"><div class="section-head"><h3>${foundationT("payments.merchantOpening.title")}</h3>${canDraft ? `<button id="merchant-opening-create" class="button secondary" type="button">${foundationT("payments.merchantOpening.add")}</button>` : ""}</div><div id="merchant-opening-history" class="table-wrap compact-table"></div></section>
      <div class="merchant-account-actions">
        ${canDraft ? `<button id="merchant-collection-toggle" class="button" type="button">${paymentT("recordCollection")}</button>` : ""}
        <button id="merchant-account-details-toggle" class="button secondary" type="button">${paymentT("accountDetails")}</button>
      </div>
      <section class="merchant-collection-review">
        <div class="section-head tight-head"><div><h3>${paymentT("collectionWork")}</h3><p>${paymentT("savedDrafts")}</p></div></div>
        <div class="table-wrap"><table><thead><tr><th>${paymentT("submitted")}</th><th>${paymentT("reference")}</th><th>${paymentT("assignedTo")}</th><th>${paymentT("amount")}</th><th>${paymentT("method")}</th><th>${paymentT("statusReason")}</th><th>${paymentT("action")}</th></tr></thead><tbody id="merchant-collection-draft-rows"><tr><td colspan="7">${paymentT("showAccountCollectionWork")}</td></tr></tbody></table></div>
      </section>
      <section id="merchant-account-details" class="merchant-account-details" hidden>
        <div id="merchant-account-detail-panel" class="detail-grid"></div>
      </section>
      <section class="merchant-order-ledger">
        <div class="section-head tight-head"><div><h3>${paymentT("ordersMiniInvoices")}</h3><p>${paymentT("orderLedgerHelp")}</p></div></div>
        <div class="table-wrap"><table><thead><tr><th>${paymentT("order")}</th><th>${paymentT("date")}</th><th>${paymentT("items")}</th><th>${paymentT("saleTotal")}</th><th>${paymentT("collected")}</th><th>${paymentT("returns")}</th><th>${paymentT("charges")}</th><th>${paymentT("reductions")}</th><th>${paymentT("refunds")}</th><th>${paymentT("remaining")}</th><th>${paymentT("status")}</th></tr></thead><tbody id="merchant-order-rows"><tr><td colspan="11">${paymentT("showAccountOrders")}</td></tr></tbody></table></div>
      </section>
      <section class="merchant-financial-closure" id="merchant-financial-closure" hidden>
        <div class="section-head tight-head"><div><h3>${paymentT("financialClosure")}</h3><p>${paymentT("financialClosureHelp")}</p></div><button id="merchant-closure-submit" class="button primary" type="button" hidden>${paymentT("sendFinancialClosure")}</button></div>
        <div class="table-wrap"><table><thead><tr><th>${paymentT("select")}</th><th>${paymentT("operationNumber")}</th><th>${paymentT("saleValue")}</th><th>${paymentT("settled")}</th><th>${paymentT("remaining")}</th></tr></thead><tbody id="merchant-closure-rows"><tr><td colspan="5">${paymentT("noEligibleSales")}</td></tr></tbody></table></div>
        <div id="merchant-closure-review" hidden></div>
      </section>
      <div class="section-head merchant-activity-head"><div><h3>${paymentT("recentActivity")}</h3><p>${paymentT("activityHelp")}</p></div></div>
      <div class="table-wrap statement-table-wrap"><table><thead><tr><th>${paymentT("when")}</th><th>${paymentT("whatHappened")}</th><th>${paymentT("relatedRecord")}</th><th>${paymentT("method")}</th><th>${paymentT("added")}</th><th>${paymentT("reduced")}</th><th>${paymentT("balance")}</th></tr></thead><tbody id="merchant-statement-rows"><tr><td colspan="7">${paymentT("showAccountActivity")}</td></tr></tbody></table></div>
    </section>
    <section id="payment-audit-section" data-payment-panel="audit" class="band payment-audit-band">
      <div class="section-head"><div><h2>${paymentT("audit")}</h2><p>${paymentT("auditHelp")}</p></div><button id="payment-audit-refresh" class="button secondary" type="button">${paymentT("refreshAudit")}</button></div>
      <div class="table-wrap"><table><thead><tr><th>${paymentT("when")}</th><th>${paymentT("action")}</th><th>${paymentT("status")}</th><th>${paymentT("amount")}</th><th>${paymentT("method")}</th><th>${paymentT("reason")}</th><th>${paymentT("details")}</th></tr></thead><tbody id="payment-audit-rows"><tr><td colspan="7">${paymentT("loadAudit")}</td></tr></tbody></table></div><div id="payment-audit-pagination" class="pagination" hidden></div>
    </section>
    </section>`;

  applyPaymentsView("merchant");

  const statementFrom = document.getElementById("merchant-statement-from");
  const statementTo = document.getElementById("merchant-statement-to");
  if (statementFrom && statementTo) {
    const today = new Date();
    const iso = (value) => value.toISOString().slice(0, 10);
    statementTo.value = iso(today);
    statementFrom.value = iso(new Date(today.getFullYear(), today.getMonth(), 1));
  }

  document.getElementById("payment-merchant-search")?.addEventListener("input", (event) => {
    const query = String(event.target.value || "").trim().toLocaleLowerCase();
    const select = document.getElementById("payment-merchant");
    if (!select) return;
    const selectedValue = select.value;
    select.replaceChildren(...paymentMerchants
      .filter((merchant) => !query || String(merchant.businessName || "").toLocaleLowerCase().includes(query))
      .map((merchant) => {
        const option = document.createElement("option");
        option.value = merchant.id;
        option.textContent = merchant.businessName;
        return option;
      }));
    if ([...select.options].some((option) => option.value === selectedValue)) select.value = selectedValue;
  });

  document.getElementById("payments-refresh").addEventListener("click", () => loadPayments("other"));
  bindTransactionReferenceField("collection-method", "collection-transaction-reference");
  document.getElementById("collection-source-kind")?.addEventListener("change", syncUnifiedCollectionSource);
  document.getElementById("merchant-collection-toggle")?.addEventListener("click", () => {
    openUnifiedCollectionForm("MerchantAccount", document.getElementById("payment-merchant")?.value || "");
  });
  document.getElementById("other-collection-toggle")?.addEventListener("click", () => openUnifiedCollectionForm("OtherPayments"));
  document.getElementById("merchant-account-details-toggle")?.addEventListener("click", () => {
    const details = document.getElementById("merchant-account-details");
    if (details) details.hidden = !details.hidden;
  });
  document.getElementById("unified-collection-form")?.addEventListener("submit", recordUnifiedCollection);
  document.getElementById("financial-adjustment-form")?.addEventListener("submit", createFinancialAdjustment);
  document.getElementById("adjustment-operation-id")?.addEventListener("change", resolveAdjustmentOperation);
  document.getElementById("adjustment-operation-id")?.addEventListener("blur", resolveAdjustmentOperation);
  document.getElementById("payment-merchant")?.addEventListener("change", () => {
    void loadMerchantBalance();
  });
  document.getElementById("load-merchant-balance").addEventListener("click", loadMerchantBalance);
  document.getElementById("payment-audit-refresh")?.addEventListener("click", loadPaymentAudit);
  document.getElementById("payment-review-refresh")?.addEventListener("click", loadCollectionWorkInbox);
  loadedPaymentPanels.clear();
  void loadPaymentKpis();
  await loadPaymentViewPanel("merchant");
}

async function loadPaymentKpis() {
  try {
    const kpis = await request("/api/v1/payments/kpis");
    const total = Number(kpis?.totalActivity?.erpSales || 0) + Number(kpis?.totalActivity?.openingReceivable || 0);
    const collected = Number(kpis?.actuallyCollected?.cash || 0) + Number(kpis?.actuallyCollected?.bank || 0) + Number(kpis?.actuallyCollected?.wallet || 0);
    const remaining = Number(kpis?.remaining?.openingBalance || 0) + Number(kpis?.remaining?.erpMerchantObligations || 0) + Number(kpis?.remaining?.otherPayments || 0);
    const set = (id, value) => { const node = document.getElementById(id); if (node) node.textContent = formatMoney(value); };
    set("merchant-payment-count", kpis?.totalActivity?.merchantSales || 0);
    set("payment-count", kpis?.totalActivity?.otherPaymentsSales || 0);
    set("payment-history-count", remaining);
    const tools = document.querySelector("[data-payment-kpi-summary]");
    if (tools) tools.textContent = foundationT("payments.kpiSummary", { total: formatMoney(total), collected: formatMoney(collected), remaining: formatMoney(remaining) });
  } catch {
    // The payment workspace remains usable when KPI data is unavailable.
  }
}

async function loadPaymentViewPanel(view) {
  if (!document.getElementById("merchant-payment-rows") || loadedPaymentPanels.has(view)) return;
  loadedPaymentPanels.add(view);
  try {
    if (view === "merchant") await loadPayments("merchant");
    else if (view === "other") await loadPayments("other");
    else if (view === "ledger") await loadPaymentHistory();
    else if (view === "review") await loadCollectionWorkInbox();
    else if (view === "audit") await loadPaymentAudit();
  } catch {
    loadedPaymentPanels.delete(view);
  }
}

function renderPaymentPager(elementId, result, onPage) {
  const pager = document.getElementById(elementId);
  if (!pager) return;
  const totalPages = Math.max(1, result.totalPages || 1);
  pager.hidden = totalPages <= 1;
  setPagerContents(pager, foundationT("pagination.previous"), foundationT("payments.pageOf", { page: result.page, pages: totalPages }), foundationT("pagination.next"), result.page <= 1, result.page >= totalPages, (delta) => onPage(result.page + delta));
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

async function loadPayments(scope = null) {
  const auth = getAuth();
  const isAdmin = ["Admin", "ERPAdmin"].includes(auth?.user.role);
  const canDraft = ["Admin", "ERPAdmin", "Accountant"].includes(auth?.user.role);
  const queues = [
    { key: "merchant", endpoint: "/api/v1/payments/merchant-account-payments", tbodyId: "merchant-payment-rows", countId: "merchant-payment-count", pagerId: "merchant-payment-pagination", emptyKey: "noMerchantConfirmations" },
    { key: "other", endpoint: "/api/v1/payments/other-payments", tbodyId: "payment-rows", countId: "payment-count", pagerId: "payment-pagination", emptyKey: "noOtherConfirmations" }
  ].filter((queue) => !scope || queue.key === scope);
  await Promise.all(queues.map(async (queue) => {
    const tbody = document.getElementById(queue.tbodyId);
    const count = document.getElementById(queue.countId);
    if (!tbody || !count) return;
    try {
      const result = await request(`${queue.endpoint}?openOnly=true&page=${paymentPageState[queue.key]}&pageSize=50`);
      const queueItems = result.items || [];
      count.textContent = paymentT("openConfirmations", { count: result.totalCount });
      tbody.innerHTML = queueItems.length === 0
        ? `<tr><td colspan="9">${escapeHtml(paymentT(queue.emptyKey))}</td></tr>`
        : queueItems.map((log) => `
        <tr data-payment-id="${escapeHtml(log.id)}" data-payment-operation-id="${escapeHtml(log.operationId)}" data-payment-operation-number="${escapeHtml(log.operationNumber || "")}" data-payment-merchant-id="${escapeHtml(log.merchantId || "")}" data-payment-method="${escapeHtml(log.paymentMethod)}" data-payment-status="${escapeHtml(log.status)}">
          <td>${canDraft ? `<button class="button secondary table-action" type="button" data-payment-use="${escapeHtml(log.id)}" data-payment-use-scope="${log.merchantId ? "MerchantAccount" : "OtherPayments"}" data-payment-use-merchant="${escapeHtml(log.merchantId || "")}">${paymentT("use")}</button>` : ""}<strong>${escapeHtml(paymentReference(log))}</strong></td>
          <td><strong dir="auto">${escapeHtml(log.buyerName || paymentT("unknownBuyer"))}</strong></td>
          <td><strong>${escapeHtml(operationReference(log))}</strong><div class="muted-cell">${escapeHtml(operationTypeLabel(log.operationType))}</div></td>
          <td>${escapeHtml(movementMethodLabel(log.paymentMethod))}</td>
          <td>${escapeHtml(formatMoney(log.totalAmount))}</td>
          <td>${escapeHtml(formatMoney(log.amountPaid))}</td>
          <td>${escapeHtml(formatMoney(log.remainingAmount))}</td>
          <td><span class="status-pill ${log.status === "Completed" ? "status-ok" : "status-warn"}">${escapeHtml(paymentWorkflowStatusLabel(log.status))}</span><div class="muted-cell">${escapeHtml(paymentT("by", { name: log.initializedByName || log.lastModifiedByName || "-" }))}</div></td>
          <td><button class="button secondary table-action" type="button" data-payment-detail="${escapeHtml(log.id)}">${paymentT("details")}</button><button class="button secondary table-action" type="button" data-print-report="${log.paymentMethod === "CashHandToHand" ? "cash-receipt" : "payment-receipt"}" data-print-id="${escapeHtml(log.id)}" data-print-code="${escapeHtml(paymentReference(log))}">${paymentT("print")}</button>${isAdmin && log.status !== "Completed" ? `<button class="button secondary table-action" type="button" data-payment-assign="${escapeHtml(log.id)}">${paymentT("assign")}</button>` : ""}${isAdmin && log.paymentMethod === "CashHandToHand" && log.status === "PendingAccountant" ? `<button class="button secondary table-action" type="button" data-cash-approve="${escapeHtml(log.id)}">${paymentT("approveCash")}</button>` : ""}</td>
        </tr>
        <tr class="operation-detail-row" id="payment-detail-${escapeHtml(log.id)}" hidden><td colspan="9"><div class="operation-detail">${paymentT("loadingDetails")}</div></td></tr>`).join("");
      tbody.querySelectorAll("[data-payment-use]").forEach((button) => button.addEventListener("click", () => {
      const scope = button.dataset.paymentUseScope || "OtherPayments";
      const operationId = button.closest("tr")?.dataset.paymentOperationId || "";
      openUnifiedCollectionForm(scope, button.dataset.paymentUseMerchant || "", operationId, button.dataset.paymentUse || "");
      }));
      tbody.querySelectorAll("[data-payment-detail]").forEach((button) => button.addEventListener("click", () => togglePaymentDetails(button.dataset.paymentDetail, button)));
      tbody.querySelectorAll("[data-payment-assign]").forEach((button) => button.addEventListener("click", () => assignPaymentLog(button.dataset.paymentAssign)));
      tbody.querySelectorAll("[data-cash-approve]").forEach((button) => button.addEventListener("click", () => approveCashReceipt(button.dataset.cashApprove)));
      bindPrintReportButtons(tbody);
      renderPaymentPager(queue.pagerId, result, (page) => {
        paymentPageState[queue.key] = page;
        void loadPayments(queue.key);
      });
    } catch (exception) {
      count.textContent = paymentT("failed");
      tbody.innerHTML = `<tr><td colspan="9">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
    }
  }));
}

async function loadPaymentHistory() {
  const tbody = document.getElementById("payment-history-rows");
  const count = document.getElementById("payment-history-count");
  if (!tbody || !count) {
    return;
  }

  try {
    const result = await request(`/api/v1/payments/other-payments/history?page=${paymentPageState.history}&pageSize=50`);
    paymentHistoryRows = result.items || [];
    count.textContent = paymentT("records", { count: result.totalCount });
    tbody.innerHTML = paymentHistoryRows.length === 0
      ? `<tr><td colspan="9">${paymentT("noHistory")}</td></tr>`
      : paymentHistoryRows.map((row) => `
        <tr data-payment-id="${escapeHtml(row.id)}" data-payment-operation-id="${escapeHtml(row.operationId)}" data-payment-operation-number="${escapeHtml(row.operationNumber || "")}" data-payment-merchant-id="${escapeHtml(row.merchantId || "")}" data-payment-method="${escapeHtml(row.paymentMethod || "")}" data-payment-status="${escapeHtml(row.status || "")}">
          <td>${escapeHtml(formatDateTime(row.lastModifiedAt))}</td>
          <td><strong>${escapeHtml(paymentReference(row))}</strong><div class="muted-cell">${escapeHtml(paymentWorkflowStatusLabel(row.status))}</div></td>
          <td><strong dir="auto">${escapeHtml(row.merchantName || row.buyerName || paymentT("unknownBuyer"))}</strong></td>
          <td><strong>${escapeHtml(operationReference(row))}</strong><div class="muted-cell">${escapeHtml(operationTypeLabel(row.operationType))}</div></td>
          <td>${escapeHtml(movementMethodLabel(row.paymentMethod))}</td>
          <td>${escapeHtml(formatMoney(row.totalAmount))}</td>
          <td><span class="status-pill ${paymentHistoryStatusClass(row.status)}">${escapeHtml(paymentWorkflowStatusLabel(row.status))}</span></td>
          <td>${escapeHtml(row.lastModifiedByName || row.initializedByName || "-")}</td>
          <td><button class="button secondary table-action" type="button" data-payment-history-detail="${escapeHtml(row.id)}">${paymentT("details")}</button><button class="button secondary table-action" type="button" data-print-report="${row.paymentMethod === "CashHandToHand" ? "cash-receipt" : "payment-receipt"}" data-print-id="${escapeHtml(row.id)}" data-print-code="${escapeHtml(paymentReference(row))}">${paymentT("print")}</button></td>
        </tr>
        <tr class="operation-detail-row" id="payment-history-detail-${escapeHtml(row.id)}" hidden><td colspan="9"><div class="operation-detail">${paymentT("loadingDetails")}</div></td></tr>`).join("");
    tbody.querySelectorAll("[data-payment-history-detail]").forEach((button) => button.addEventListener("click", () => togglePaymentHistoryDetails(button.dataset.paymentHistoryDetail, button)));
    bindPrintReportButtons(tbody);
    renderPaymentPager("payment-history-pagination", result, (page) => {
      paymentPageState.history = page;
      void loadPaymentHistory();
    });
  } catch (exception) {
    count.textContent = paymentT("failed");
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

function movementMethodLabel(method) {
  const keys = { CashHandToHand: "cashInHand", CashTransaction: "cashTransaction", BankTransfer: "bankTransfer", Wallet: "wallet", MerchantAccount: "merchantAccount", Installment: "merchantAccount", Installlaugment: "merchantAccount" };
  return keys[method] ? paymentT(keys[method]) : method || "-";
}

function operationTypeLabel(type) {
  const keys = { WholesaleSale: "wholesaleSale", RetailSale: "retailSale", Return: "return", Change: "exchange", InventoryReceipt: "inventoryReceipt", WarehouseTransfer: "warehouseTransfer", Reserve: "reserve", WriteOff: "writeOff" };
  return keys[type] ? paymentT(keys[type]) : type || "-";
}

async function togglePaymentDetails(id, button) {
  const row = document.getElementById(`payment-detail-${id}`);
  if (!row) {
    return;
  }
  if (!row.hidden) {
    row.hidden = true;
    button.textContent = paymentT("details");
    return;
  }
  row.hidden = false;
  button.textContent = paymentT("hide");
  const target = row.querySelector(".operation-detail");
  target.innerHTML = `<span class="muted-text">${paymentT("loadingPaymentDetails")}</span>`;
  try {
    const detail = await request(`/api/v1/payments/${id}`);
    target.innerHTML = renderPaymentDetail(detail);
    target.querySelectorAll("[data-sublog-submit]").forEach((submit) => submit.addEventListener("click", () => submitSubLog(submit.dataset.sublogSubmit, submit.dataset.paymentLogId)));
    target.querySelectorAll("[data-sublog-approve]").forEach((approve) => approve.addEventListener("click", () => approveSubLog(approve.dataset.sublogApprove, approve.dataset.paymentLogId)));
    target.querySelectorAll("[data-sublog-reject]").forEach((reject) => reject.addEventListener("click", () => rejectSubLog(reject.dataset.sublogReject, reject.dataset.paymentLogId)));
    target.querySelectorAll("[data-cash-detail-approve]").forEach((approve) => approve.addEventListener("click", () => approveCashReceipt(approve.dataset.paymentLogId)));
    target.querySelectorAll("[data-cash-detail-reject]").forEach((reject) => reject.addEventListener("click", () => rejectCashReceipt(reject.dataset.paymentLogId)));
    target.querySelectorAll("[data-adjustment-approve]").forEach((approve) => approve.addEventListener("click", () => approveAdjustment(approve.dataset.adjustmentApprove, id)));
    target.querySelectorAll("[data-adjustment-reject]").forEach((reject) => reject.addEventListener("click", () => rejectAdjustment(reject.dataset.adjustmentReject, id)));
    target.querySelectorAll("[data-adjustment-payout]").forEach((payout) => payout.addEventListener("click", () => payoutCashRefund(payout.dataset.adjustmentPayout, id)));
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
    button.textContent = paymentT("details");
    return;
  }
  row.hidden = false;
  button.textContent = paymentT("hide");
  const target = row.querySelector(".operation-detail");
  target.innerHTML = `<span class="muted-text">${paymentT("loadingPaymentDetails")}</span>`;
  try {
    const detail = await request(`/api/v1/payments/${id}`);
    target.innerHTML = renderPaymentDetail(detail);
    target.querySelectorAll("[data-sublog-submit]").forEach((submit) => submit.addEventListener("click", () => submitSubLog(submit.dataset.sublogSubmit, submit.dataset.paymentLogId)));
    target.querySelectorAll("[data-sublog-approve]").forEach((approve) => approve.addEventListener("click", () => approveSubLog(approve.dataset.sublogApprove, approve.dataset.paymentLogId)));
    target.querySelectorAll("[data-sublog-reject]").forEach((reject) => reject.addEventListener("click", () => rejectSubLog(reject.dataset.sublogReject, reject.dataset.paymentLogId)));
    target.querySelectorAll("[data-cash-detail-approve]").forEach((approve) => approve.addEventListener("click", () => approveCashReceipt(approve.dataset.paymentLogId)));
    target.querySelectorAll("[data-cash-detail-reject]").forEach((reject) => reject.addEventListener("click", () => rejectCashReceipt(reject.dataset.paymentLogId)));
    target.querySelectorAll("[data-adjustment-approve]").forEach((approve) => approve.addEventListener("click", () => approveAdjustment(approve.dataset.adjustmentApprove, id)));
    target.querySelectorAll("[data-adjustment-reject]").forEach((reject) => reject.addEventListener("click", () => rejectAdjustment(reject.dataset.adjustmentReject, id)));
  } catch (exception) {
    target.innerHTML = `<span class="muted-text">${escapeHtml(getFriendlyWorkspaceError(exception))}</span>`;
  }
}

function renderPaymentDetail(detail) {
  const role = getAuth()?.user.role;
  const isAdmin = ["Admin", "ERPAdmin"].includes(role);
  const canPrepareCollection = ["Accountant", "Admin", "ERPAdmin"].includes(role);
  const canApproveAdjustments = ["Admin", "ERPAdmin", "CLevel"].includes(getAuth()?.user.role);
  const subLogs = detail.subLogs || [];
  const cashRecords = detail.cashRecords || [];
  const adjustments = detail.adjustments || [];
  const stages = detail.stages || [];
  const log = detail.log || {};
  return `<div class="detail-stack">
    ${detail.notes ? `<p class="muted-text"><strong>${paymentT("notes")}:</strong> ${escapeHtml(detail.notes)}</p>` : ""}
    <div class="operation-detail-grid">
      <div class="metric"><span>${paymentT("initializedBy")}</span><strong>${escapeHtml(log.initializedByName || "-")}</strong></div>
      <div class="metric"><span>${paymentT("assignedTo")}</span><strong>${escapeHtml(log.assignedToName || "-")}</strong></div>
      <div class="metric"><span>${paymentT("lastModifiedBy")}</span><strong>${escapeHtml(log.lastModifiedByName || "-")}</strong></div>
      <div class="metric"><span>${paymentT("status")}</span><strong>${escapeHtml(paymentWorkflowStatusLabel(log.status))}</strong></div>
      <div class="metric"><span>${paymentT("adjustedAmount")}</span><strong>${escapeHtml(formatMoney(log.adjustedAmount))}</strong></div>
      <div class="metric"><span>${paymentT("remainingToCollect")}</span><strong>${escapeHtml(formatMoney(log.remainingAmount))}</strong></div>
      <div class="metric"><span>${paymentT("refundDue")}</span><strong>${escapeHtml(formatMoney(log.refundDue))}</strong></div>
      <div class="metric"><span>${paymentT("pendingCollection")}</span><strong>${escapeHtml(formatMoney(log.pendingCollections))}</strong></div>
    </div>
    <div class="table-wrap compact-table"><table><thead><tr><th>${paymentT("stage")}</th><th>${paymentT("when")}</th><th>${paymentT("actorLabel")}</th><th>${paymentT("method")}</th><th>${paymentT("amount")}</th><th>${paymentT("status")}</th><th>${paymentT("notes")}</th></tr></thead><tbody>${stages.length === 0
    ? `<tr><td colspan="7">${paymentT("noStageHistory")}</td></tr>`
    : stages.map((stage) => `<tr>
        <td>${escapeHtml(paymentStageLabel(stage.stageType))}</td>
        <td>${escapeHtml(formatDateTime(stage.happenedAt))}</td>
        <td>${escapeHtml(stage.actorName || "-")}</td>
        <td>${escapeHtml(movementMethodLabel(stage.paymentMethod))}</td>
        <td>${escapeHtml(formatMoney(stage.amount))}</td>
        <td><span class="status-pill ${paymentHistoryStatusClass(stage.status)}">${escapeHtml(paymentWorkflowStatusLabel(stage.status))}</span></td>
        <td>${escapeHtml(stage.notes || "-")}</td>
      </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>${paymentT("amount")}</th><th>${paymentT("method")}</th><th>${paymentT("date")}</th><th>${paymentT("status")}</th><th>${paymentT("drafted")}</th><th>${paymentT("decision")}</th><th>${paymentT("actions")}</th></tr></thead><tbody>${subLogs.length === 0
    ? `<tr><td colspan="7">${paymentT("noSubLogs")}</td></tr>`
    : subLogs.map((sub) => `<tr>
        <td>${escapeHtml(formatMoney(sub.amount))}</td>
        <td>${escapeHtml(movementMethodLabel(sub.paymentMethod))}</td>
        <td>${escapeHtml(sub.dateReceived || "-")}</td>
        <td><span class="status-pill ${sub.status === "Confirmed" ? "status-ok" : sub.status === "Rejected" ? "status-muted" : "status-warn"}">${escapeHtml(paymentWorkflowStatusLabel(sub.status))}</span></td>
        <td>${escapeHtml(formatDateTime(sub.draftedAt))}<div class="muted-cell">${escapeHtml(sub.draftedByName || "-")}</div></td>
        <td>${escapeHtml(sub.rejectionReason || formatDateTime(sub.confirmedAt) || "-")}<div class="muted-cell">${escapeHtml(sub.confirmedByName || "-")}</div>${sub.notes ? `<div class="muted-cell">${escapeHtml(sub.notes)}</div>` : ""}</td>
        <td>${sub.status === "Draft" && canPrepareCollection
          ? `<button class="button secondary table-action" type="button" data-payment-log-id="${escapeHtml(log.id)}" data-sublog-submit="${escapeHtml(sub.id)}">${paymentT("sendForApproval")}</button>`
          : isAdmin && sub.status === "PendingAdminReview"
            ? `<button class="button secondary table-action" type="button" data-payment-log-id="${escapeHtml(log.id)}" data-sublog-approve="${escapeHtml(sub.id)}">${paymentT("approve")}</button><button class="button danger table-action" type="button" data-payment-log-id="${escapeHtml(log.id)}" data-sublog-reject="${escapeHtml(sub.id)}">${paymentT("reject")}</button>`
            : "-"}</td>
      </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>${paymentT("cashRecord")}</th><th>${paymentT("amount")}</th><th>${paymentT("date")}</th><th>${paymentT("status")}</th><th>${paymentT("createdBy")}</th><th>${paymentT("notes")}</th><th>${paymentT("actions")}</th></tr></thead><tbody>${cashRecords.length === 0
    ? `<tr><td colspan="7">${paymentT("noCashRecords")}</td></tr>`
    : cashRecords.map((record) => `<tr>
        <td>${escapeHtml(uiText(record.paymentType || "-"))}<div class="muted-cell">${escapeHtml(uiText(record.subType || "-"))}</div></td>
        <td>${escapeHtml(formatMoney(record.amount))}</td>
        <td>${escapeHtml(formatDateTime(record.paymentDate))}</td>
        <td><span class="status-pill ${paymentHistoryStatusClass(record.status)}">${escapeHtml(paymentWorkflowStatusLabel(record.status))}</span></td>
        <td>${escapeHtml(record.createdByName || "-")}</td>
        <td>${escapeHtml(record.notes || "-")}</td>
        <td>${isAdmin && ["PendingAccountant", "PendingAdminReview"].includes(record.status)
          ? `<button class="button secondary table-action" type="button" data-payment-log-id="${escapeHtml(log.id)}" data-cash-detail-approve="${escapeHtml(record.id)}">${paymentT("approve")}</button><button class="button danger table-action" type="button" data-payment-log-id="${escapeHtml(log.id)}" data-cash-detail-reject="${escapeHtml(record.id)}">${paymentT("reject")}</button>`
          : "-"}</td>
      </tr>`).join("")}</tbody></table></div>
    <div class="table-wrap compact-table"><table><thead><tr><th>${paymentT("adjustment")}</th><th>${paymentT("amount")}</th><th>${paymentT("date")}</th><th>${paymentT("status")}</th><th>${paymentT("createdBy")}</th><th>${paymentT("notes")}</th><th>${paymentT("actions")}</th></tr></thead><tbody>${adjustments.length === 0
    ? `<tr><td colspan="7">${paymentT("noAdjustments")}</td></tr>`
    : adjustments.map((adjustment) => `<tr>
        <td>${escapeHtml(paymentStageLabel(adjustment.adjustmentType))}</td>
        <td>${escapeHtml(formatMoney(adjustment.amount))}</td>
        <td>${escapeHtml(formatDateTime(adjustment.createdAt))}</td>
        <td><span class="status-pill ${paymentHistoryStatusClass(adjustment.status)}">${escapeHtml(paymentWorkflowStatusLabel(adjustment.status))}</span></td>
        <td>${escapeHtml(adjustment.createdByName || "-")}</td>
        <td>${escapeHtml(adjustment.notes || "-")}</td>
        <td>${canApproveAdjustments && adjustment.status === "PendingApproval" ? `<button class="button secondary table-action" type="button" data-adjustment-approve="${escapeHtml(adjustment.id)}">${paymentT("approve")}</button><button class="button secondary table-action" type="button" data-adjustment-reject="${escapeHtml(adjustment.id)}">${paymentT("reject")}</button>` : canApproveAdjustments && adjustment.adjustmentType === "CashRefund" && adjustment.status === "Approved" ? `<button class="button secondary table-action" type="button" data-adjustment-payout="${escapeHtml(adjustment.id)}">${paymentT("recordPayout")}</button>` : "-"}</td>
      </tr>`).join("")}</tbody></table></div>
  </div>`;
}

function paymentStageLabel(stageType) {
  const keys = {
    PaymentLogOpened: "paymentLogOpened",
    PaymentAssigned: "assignedAccountant",
    InstallmentDrafted: "collectionDrafted",
    InstallmentApproved: "collectionApproved",
    InstallmentRejected: "collectionRejected",
    CashReceiptRecorded: "cashReceiptRecorded",
    CashReceiptApproved: "cashReceiptApproved",
    CashRefundRecorded: "cashRefundRecorded",
    AdditionalCharge: "additionalCharge",
    MerchantCredit: "additionalCharge",
    BalanceReduction: "remainingReduction",
    CashRefund: "financialCashRefund"
  };
  return keys[stageType] ? paymentT(keys[stageType]) : stageType || "-";
}

function openUnifiedCollectionForm(scope = "OtherPayments", merchantId = "", operationId = "", reference = "") {
  const card = document.getElementById("unified-collection-card");
  const tools = document.getElementById("payment-tools-section");
  const source = document.getElementById("collection-source-kind");
  const merchant = document.getElementById("collection-merchant");
  const operation = document.getElementById("collection-source-operation-id");
  const sourceReference = document.getElementById("collection-source-reference");
  if (card) card.hidden = false;
  if (tools) tools.hidden = false;
  if (source) source.value = scope;
  if (merchant && scope === "MerchantAccount") merchant.value = merchantId;
  if (operation) operation.value = operationId;
  if (sourceReference && scope === "OtherPayments") sourceReference.value = reference;
  syncUnifiedCollectionSource();
  document.getElementById("unified-collection-form")?.scrollIntoView({ behavior: "smooth", block: "center" });
}

function syncUnifiedCollectionSource() {
  const isMerchantAccount = document.getElementById("collection-source-kind")?.value === "MerchantAccount";
  const referenceField = document.getElementById("collection-source-reference-field");
  const merchantField = document.getElementById("collection-merchant-field");
  const reference = document.getElementById("collection-source-reference");
  const merchant = document.getElementById("collection-merchant");
  if (referenceField) referenceField.hidden = isMerchantAccount;
  if (merchantField) merchantField.hidden = !isMerchantAccount;
  if (reference) reference.required = !isMerchantAccount;
  if (merchant) merchant.required = isMerchantAccount;
  const help = document.getElementById("collection-workflow-help");
  if (help) help.textContent = isMerchantAccount
    ? paymentT("merchantCollectionHelp")
    : paymentT("paymentCollectionHelp");
}

function resolveCollectionPaymentLog(reference) {
  const normalized = String(reference || "").trim().toLowerCase();
  if (!normalized) return null;
  return paymentHistoryRows.find((row) =>
    String(row.id || "").toLowerCase() === normalized ||
    String(row.operationId || "").toLowerCase() === normalized ||
    String(row.operationNumber || "").toLowerCase() === normalized ||
    paymentReference(row).toLowerCase() === normalized) || null;
}

async function recordUnifiedCollection(event) {
  event.preventDefault();
  clearFormError("unified-collection-error");
    // Collection form always creates and submits its draft in one action.
    const submitForReview = true;
  const sourceKind = document.getElementById("collection-source-kind")?.value;
  const method = canonicalSelectValue("collection-method", "movementMethod", { allowEmpty: true });
  const transactionReference = document.getElementById("collection-transaction-reference")?.value.trim() || null;
  const amount = Number(document.getElementById("collection-amount")?.value);
  const notes = document.getElementById("collection-notes")?.value.trim() || null;
  const financeAccountId = document.getElementById("collection-finance-account")?.value || null;
  if (!method || !Number.isFinite(amount) || amount <= 0) {
    showFormError("unified-collection-error", foundationT("payments.errors.collectionAmountAndMethod"));
    return;
  }
  if (!financeAccountId) {
    showFormError("unified-collection-error", foundationT("payments.errors.collectionAccount"));
    return;
  }
  if (["CashTransaction", "BankTransfer", "Wallet"].includes(method) && !transactionReference) {
    showFormError("unified-collection-error", foundationT("payments.errors.collectionReference"));
    return;
  }

  const idempotencyKey = createUuid();
  const mutationOptions = { headers: { "Idempotency-Key": idempotencyKey }, notify: false };
  const submit = async (path, body) => {
    const options = { ...mutationOptions, method: "POST", body: JSON.stringify(body) };
    try {
      return await request(path, options);
    } catch (firstError) {
      // A response can be lost after the server commits. Replaying the same
      // key is safe and returns the original result instead of duplicating it.
      if (sourceKind !== "MerchantAccount") throw firstError;
      try {
        return await request(path, options);
      } catch (secondError) {
        // If both responses were lost, ask the server whether the original
        // mutation committed before surfacing an error to the accountant.
        const resolved = await request(`/api/v1/payments/collections/resolve?key=${encodeURIComponent(idempotencyKey)}`);
        if (resolved && (resolved.status === "Completed" || resolved.reference || resolved.id)) return resolved;
        throw secondError;
      }
    }
  };
  try {
    if (sourceKind === "MerchantAccount") {
      const merchantId = document.getElementById("collection-merchant")?.value;
      if (!merchantId) throw new Error(foundationT("payments.errors.collectionMerchant"));
      await submit(`/api/v1/payments/merchant-accounts/${encodeURIComponent(merchantId)}/collections`, { sourceOperationId: document.getElementById("collection-source-operation-id")?.value || null, amount, paymentMethod: method, transactionReference, financeAccountId, notes, submitForReview });
    } else {
      const reference = document.getElementById("collection-source-reference")?.value.trim();
      const operationId = document.getElementById("collection-source-operation-id")?.value || null;
      const payment = operationId ? null : resolveCollectionPaymentLog(reference);
      if (!operationId && !payment) throw new Error(foundationT("payments.errors.collectionPaymentNotFound"));
      await submit("/api/v1/payments/collections", {
          scope: "OtherPayments",
          operationId: operationId || payment.operationId,
          amount,
          paymentMethod: method,
          transactionReference,
          financeAccountId,
          dateReceived: document.getElementById("collection-date")?.value || null,
          notes,
          submitForReview
      });
    }
  } catch (exception) {
    showFormError("unified-collection-error", getFriendlyWorkspaceError(exception));
    return;
  }

  notice(paymentT("collectionSubmittedNoChange"), "success");
  try {
    event.currentTarget.reset();
    syncUnifiedCollectionSource();
  } catch (exception) {
    console.warn("Collection form cleanup failed after successful submit.", exception);
  }

  // Refresh is secondary. A committed collection must never be shown as a
  // failed submission because one read request was cancelled or unavailable.
  try {
    await Promise.allSettled([loadPayments(), loadPaymentHistory(), loadPaymentAudit()]);
    if (document.getElementById("payment-merchant")?.value) await loadMerchantBalance();
  } catch {
    notice(paymentT("collectionSubmitted"), "info");
  }
}

async function approveSubLog(id, paymentLogId = null) {
  try {
    await request(`/api/v1/payments/collections/${encodeURIComponent(id)}/approve`, { method: "POST" });
    notice(paymentT("paymentApproved"), "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function submitSubLog(id, paymentLogId = null) {
  try {
    await request(`/api/v1/payments/collections/${encodeURIComponent(id)}/submit`, { method: "POST" });
    notice(paymentT("collectionSentForApproval"), "success");
    await Promise.all([loadPayments(), loadPaymentHistory(), loadPaymentAudit()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function approveCashReceipt(id) {
  try {
    await request(`/api/v1/payments/cash-receipts/${encodeURIComponent(id)}/approve`, { method: "POST" });
    notice(paymentT("cashReceiptApprovedNotice"), "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function rejectCashReceipt(id) {
  const reason = await promptDialog({
    title: foundationT("app.prompt.rejectCashReceipt"),
    label: foundationT("app.prompt.rejectCashReceiptHelp"),
    multiline: true,
    required: true
  });
  if (!reason) return;
  try {
    await request(`/api/v1/payments/cash-receipts/${encodeURIComponent(id)}/reject`, {
      method: "POST",
      body: JSON.stringify({ reason })
    });
    notice(paymentT("cashReceiptRejectedNotice"), "success");
    await Promise.all([loadPayments(), loadPaymentHistory(), loadPaymentAudit()]);
    await reopenPaymentDetail(id);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function rejectSubLog(id, paymentLogId = null) {
  const reason = await promptDialog({
    title: foundationT("app.prompt.rejectPaymentEntry"),
    label: foundationT("app.prompt.rejectPaymentEntryHelp"),
    multiline: true,
    required: true
  });
  if (!reason) {
    return;
  }
  try {
    await request(`/api/v1/payments/collections/${encodeURIComponent(id)}/reject`, { method: "POST", body: JSON.stringify({ reason }) });
    notice(paymentT("paymentRejectedNotice"), "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function approveAdjustment(id, paymentLogId = null) {
  try {
    await request(`/api/v1/payments/adjustments/${encodeURIComponent(id)}/approve`, { method: "POST" });
    notice(paymentT("adjustmentApprovedNotice"), "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function syncOperationBatchEntryFields(row) {
  const creatingNew = row.querySelector(".op-line-stock-option")?.value === "__new_batch__";
  row.querySelectorAll(".op-line-receipt-field").forEach((field) => {
    field.hidden = !creatingNew;
    field.querySelectorAll("input").forEach((input) => {
      input.disabled = !creatingNew;
    });
  });
}

function mapSkuOption(sku) {
  return {
    id: sku.id,
    productId: sku.productId,
    productName: sku.productName,
    brandName: sku.brandName,
    categoryName: sku.categoryName,
    productType: sku.productType,
    expiryType: sku.expiryType,
    piecesPerPack: sku.piecesPerPack,
    sellMode: sku.sellMode,
    skuCode: sku.skuCode,
    powerSign: sku.powerSign,
    powerValue: sku.powerValue,
    colorName: sku.colorName,
    size: sku.size,
    label: `${sku.productName} / ${sku.skuCode}`
  };
}

function mergeOperationSkuOptions(rows) {
  const byId = new Map(operationSkuOptions.map((sku) => [sku.id, sku]));
  rows.forEach((row) => byId.set(row.id, mapSkuOption(row)));
  operationSkuOptions = [...byId.values()];
  supplySkuSearchIndex = [];
  return rows.map((row) => byId.get(row.id));
}

async function searchSkuOptions(search, pageSize = 25) {
  const params = new URLSearchParams({ search, page: "1", pageSize: String(pageSize), includeInactive: "false" });
  const result = await request(`/api/v1/catalog/skus?${params}`);
  return mergeOperationSkuOptions(result.items || []);
}

async function ensureSkuOption(skuId) {
  let sku = operationSkuOptions.find((value) => value.id === skuId);
  if (sku || !skuId) return sku;
  try {
    sku = mergeOperationSkuOptions([await request(`/api/v1/catalog/skus/${encodeURIComponent(skuId)}`)])[0];
    return sku;
  } catch {
    return null;
  }
}

async function loadProductSkuOptions(productId) {
  if (!productId || loadedOperationProductIds.has(productId)) return;
  const rows = [];
  let page = 1;
  let totalCount = 0;
  do {
    const params = new URLSearchParams({ productId, page: String(page), pageSize: "100", includeInactive: "false" });
    const result = await request(`/api/v1/catalog/skus?${params}`);
    rows.push(...(result.items || []));
    totalCount = result.totalCount || rows.length;
    page += 1;
  } while (rows.length < totalCount);
  mergeOperationSkuOptions(rows);
  loadedOperationProductIds.add(productId);
}

async function loadAllSkuOptions() {
  const rows = [];
  let page = 1;
  let totalCount = 0;
  do {
    const result = await request(`/api/v1/catalog/skus?page=${page}&pageSize=100&includeInactive=false`);
    rows.push(...(result.items || []));
    totalCount = result.totalCount || rows.length;
    page += 1;
  } while (rows.length < totalCount);
  return mergeOperationSkuOptions(rows);
}

async function payoutCashRefund(id, paymentLogId = null) {
  const amount = await promptDialog({
    title: foundationT("app.prompt.recordRefundPayout"),
    label: foundationT("app.prompt.recordRefundPayoutHelp"),
    required: true,
    inputType: "number"
  });
  const value = Number(amount);
  if (!Number.isFinite(value) || value <= 0) return;
  try {
    await request(`/api/v1/payments/adjustments/${encodeURIComponent(id)}/payout`, {
      method: "POST",
      body: JSON.stringify({ amount: value })
    });
    notice(paymentT("refundPayoutRecordedNotice"), "success");
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    await reopenPaymentDetail(paymentLogId);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function rejectAdjustment(id, paymentLogId = null) {
  const reason = await promptDialog({
    title: foundationT("app.prompt.rejectFinancialAdjustment"),
    label: foundationT("app.prompt.rejectFinancialAdjustmentHelp"),
    multiline: true,
    required: true
  });
  if (!reason) {
    return;
  }

  try {
    await request(`/api/v1/payments/adjustments/${encodeURIComponent(id)}/reject`, { method: "POST", body: JSON.stringify({ reason }) });
    notice(paymentT("adjustmentRejectedNotice"), "success");
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
    notice(paymentT("chooseAccountant"), "error");
    return;
  }
  try {
    await request(`/api/v1/payments/${id}/assign`, { method: "POST", body: JSON.stringify({ accountantUserId: accountantId }) });
    await Promise.all([loadPayments(), loadPaymentHistory()]);
    notice(paymentT("paymentAssignedNotice"), "success");
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function createFinancialAdjustment(event) {
  event.preventDefault();
  clearFormError("financial-adjustment-error");
  const adjustmentType = canonicalSelectValue("adjustment-type", "adjustmentType");
  const operationId = document.getElementById("adjustment-operation-id").value.trim();
  const amount = Number(document.getElementById("adjustment-amount").value);
  if (operationId && !await resolveAdjustmentOperation()) {
    return;
  }
  if (!document.getElementById("adjustment-merchant").value || !Number.isFinite(amount) || amount <= 0) {
    showFormError("financial-adjustment-error", foundationT("payments.errors.adjustmentMerchantAmount"));
    return;
  }
  if (adjustmentType === "AdditionalCharge" && !document.getElementById("adjustment-notes").value.trim()) {
    showFormError("financial-adjustment-error", foundationT("payments.errors.additionalChargeReason"));
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
    notice(paymentT("adjustmentRequestedNotice"), "success");
    event.target.reset();
    await Promise.allSettled([loadPayments(), loadPaymentHistory(), loadPaymentAudit()]);
    if (document.getElementById("payment-merchant")?.value) await loadMerchantBalance();
  } catch (exception) {
    showFormError("financial-adjustment-error", getFriendlyWorkspaceError(exception));
  }
}

async function resolveAdjustmentOperation() {
  const errorId = "financial-adjustment-error";
  const operationInput = document.getElementById("adjustment-operation-id");
  const merchantSelect = document.getElementById("adjustment-merchant");
  const preview = document.getElementById("adjustment-order-preview");
  const reference = operationInput?.value.trim();
  if (!operationInput || !merchantSelect || !reference) {
    if (preview) preview.textContent = paymentT("optionalMiniInvoiceShort");
    return false;
  }

  try {
    const operation = await request(`/api/v1/payments/operations/resolve?reference=${encodeURIComponent(reference)}`);
    if (!operation.merchantId || !merchantSelect.querySelector(`option[value="${CSS.escape(operation.merchantId)}"]`)) {
      merchantSelect.value = "";
      showFormError(errorId, foundationT("payments.errors.adjustmentMerchantUnavailable"));
      return false;
    }

    merchantSelect.value = operation.merchantId;
    merchantSelect.disabled = false;
    if (preview) preview.textContent = foundationT("payments.adjustmentAffectedOrder", { order: operation.operationNumber || reference });
    clearFormError(errorId);
    return true;
  } catch (exception) {
    merchantSelect.value = "";
    showFormError(errorId, getFriendlyWorkspaceError(exception));
    return false;
  }
}

async function loadMerchantBalance() {
  const merchantId = document.getElementById("payment-merchant").value;
  const status = document.getElementById("merchant-balance-status");
  const panel = document.getElementById("merchant-balance-panel");
  if (!merchantId) {
    status.textContent = paymentT("selectMerchant");
    return;
  }
  status.textContent = paymentT("loading");
  try {
    const from = document.getElementById("merchant-statement-from")?.value;
    const to = document.getElementById("merchant-statement-to")?.value;
    const statementQuery = new URLSearchParams({ take: "200" });
    if (from) statementQuery.set("from", from);
    if (to) statementQuery.set("to", to);
    const [account, statement, collections, orders, openingHistory] = await Promise.all([
      request(`/api/v1/payments/merchant-accounts/${merchantId}`),
      request(`/api/v1/payments/merchant-accounts/${merchantId}/statement?${statementQuery.toString()}&includeSummary=true`),
      request(`/api/v1/payments/collection-work?scope=MerchantAccount&merchantId=${encodeURIComponent(merchantId)}`),
      request(`/api/v1/payments/merchant-accounts/${merchantId}/orders`),
      request(`/api/v1/payments/merchant-accounts/${merchantId}/opening-balances`)
    ]);
    if (document.getElementById("payment-merchant")?.value !== merchantId) return;
    const balance = account.balance || {};
    const statementRows = Array.isArray(statement) ? statement : (statement.items || []);
    const breakdown = account.breakdown || {};
    const classification = account.classification || {};
    status.textContent = paymentT("loaded");
    const amountDue = Number(breakdown.remainingOwed ?? balance.amountDue ?? 0);
    const moneyRefunded = Number(breakdown.refunds ?? breakdown.cashRefunded ?? 0);
    const netCollected = Number(breakdown.netCollected ?? account.netCollected ?? 0);
    const totalSales = Number(breakdown.totalSales ?? breakdown.saleTotal ?? 0);
    const acceptedReturns = Number(breakdown.acceptedReturnValue ?? breakdown.returnTotal ?? 0);
    const additionalCharges = Number(breakdown.additionalCharges || 0);
    const amountReductions = Number(breakdown.balanceReductions || 0);
    panel.innerHTML = `
      <article class="merchant-account-answer"><span>${paymentT("totalSales")}</span><strong>${escapeHtml(formatMoney(totalSales))}</strong><p>${paymentT("totalSalesCompleted")}</p></article>
      <article class="merchant-account-answer is-net"><span>${paymentT("netCollected")}</span><strong>${escapeHtml(formatMoney(netCollected))}</strong><p>${paymentT("netCollectedHelp")}</p></article>
      <article class="merchant-account-answer is-due"><span>${paymentT("remainingOwed")}</span><strong>${escapeHtml(formatMoney(amountDue))}</strong><p>${paymentT("remainingOwedHelp")}</p></article>
      <article class="merchant-account-answer"><span>${paymentT("refunds")}</span><strong>${escapeHtml(formatMoney(moneyRefunded))}</strong><p>${paymentT("refundsPaidHelp")}</p></article>
      <article class="merchant-account-answer"><span>${paymentT("acceptedReturnValue")}</span><strong>${escapeHtml(formatMoney(acceptedReturns))}</strong><p>${paymentT("acceptedReturnHelp")}</p></article>
      <article class="merchant-account-answer"><span>${paymentT("additionalCharges")}</span><strong>${escapeHtml(formatMoney(additionalCharges))}</strong><p>${paymentT("additionalChargesHelp")}</p></article>
      <article class="merchant-account-answer"><span>${paymentT("amountReductions")}</span><strong>${escapeHtml(formatMoney(amountReductions))}</strong><p>${paymentT("amountReductionsHelp")}</p></article>`;
    const openingTarget = document.getElementById("merchant-opening-history");
    if (openingTarget) {
      const canApproveOpening = ["Admin", "ERPAdmin"].includes(getAuth()?.user?.role);
      const canCorrectOpening = ["Admin", "ERPAdmin", "Accountant"].includes(getAuth()?.user?.role);
      openingTarget.innerHTML = financeTable(openingHistory, [foundationT("payments.merchantOpening.amount"), foundationT("payments.merchantOpening.date"), foundationT("payments.merchantOpening.status"), foundationT("payments.merchantOpening.note"), foundationT("payments.merchantOpening.actions")], (item) => `<tr><td>${escapeHtml(formatMoney(item.amount))}</td><td>${escapeHtml(item.asOfDate)}</td><td>${escapeHtml(paymentWorkflowStatusLabel(item.status))}</td><td>${escapeHtml(item.description || "—")}${item.reviewReason ? `<small>${escapeHtml(item.reviewReason)}</small>` : ""}</td><td>${canApproveOpening && item.status === "PendingReview" ? `<button class="button secondary table-action" type="button" data-merchant-opening-approve="${escapeHtml(item.id)}">${foundationT("payments.merchantOpening.approve")}</button>` : ""}${canCorrectOpening && item.status === "Posted" && !item.replacedByChargeId ? `<button class="button secondary table-action" type="button" data-merchant-opening-correct="${escapeHtml(item.id)}">${foundationT("payments.merchantOpening.correct")}</button>` : ""}</td></tr>`);
      const openingCreate = document.getElementById("merchant-opening-create");
      if (openingCreate) {
        openingCreate.hidden = openingHistory.some((item) => !item.reversesChargeId && item.status !== "Rejected");
        openingCreate.onclick = () => void createMerchantOpening(merchantId);
      }
      openingTarget.querySelectorAll("[data-merchant-opening-approve]").forEach((button) => button.addEventListener("click", () => void approveMerchantOpening(button.dataset.merchantOpeningApprove)));
      openingTarget.querySelectorAll("[data-merchant-opening-correct]").forEach((button) => button.addEventListener("click", () => void correctMerchantOpening(openingHistory.find((item) => item.id === button.dataset.merchantOpeningCorrect))));
    }
    const detailPanel = document.getElementById("merchant-account-detail-panel");
    if (detailPanel) {
      const profile = account.profile || {};
      const profilePhone = Array.isArray(profile.phoneNumbers) ? profile.phoneNumbers.join(" / ") : (profile.phoneNumbers || "-");
      detailPanel.innerHTML = `
        <div><span>${paymentT("businessType")}</span><strong>${escapeHtml(profile.businessType || "-")}</strong></div>
        <div><span>${paymentT("contactPerson")}</span><strong>${escapeHtml(profile.contactPersonName || "-")}</strong></div>
        <div><span>${paymentT("phone")}</span><strong>${escapeHtml(profilePhone)}</strong></div>
        <div><span>${paymentT("email")}</span><strong>${escapeHtml(profile.email || "-")}</strong></div>
        <div class="full-span"><span>${paymentT("address")}</span><strong>${escapeHtml(profile.address || "-")}</strong></div>
        <div><span>${paymentT("totalSales")}</span><strong>${escapeHtml(formatMoney(totalSales))}</strong></div>
        <div><span>${paymentT("netCollected")}</span><strong>${escapeHtml(formatMoney(netCollected))}</strong></div>
        <div><span>${paymentT("remainingOwed")}</span><strong>${escapeHtml(formatMoney(amountDue))}</strong></div>
        <div><span>${paymentT("refunds")}</span><strong>${escapeHtml(formatMoney(moneyRefunded))}</strong></div>
        <div><span>${paymentT("acceptedReturnValue")}</span><strong>${escapeHtml(formatMoney(acceptedReturns))}</strong></div>
        <div><span>${paymentT("additionalCharges")}</span><strong>${escapeHtml(formatMoney(additionalCharges))}</strong></div>
        <div><span>${paymentT("amountReductions")}</span><strong>${escapeHtml(formatMoney(amountReductions))}</strong></div>
        <div><span>${paymentT("accountHealth")}</span><strong>${escapeHtml(merchantAccountGrade(classification))}</strong></div>
        <div class="full-span"><span>${paymentT("accountNotes")}</span><strong>${escapeHtml(merchantAccountFlags(classification.flags || []))}</strong></div>`;
    }
    const rows = document.getElementById("merchant-statement-rows");
    if (rows) {
      rows.innerHTML = statementRows.length ? statementRows.map((row) => `<tr><td>${escapeHtml(formatDateTime(row.postedAt) || "-")}</td><td><strong>${escapeHtml(merchantStatementEvent(row))}</strong>${row.notes ? `<small>${escapeHtml(row.notes)}</small>` : ""}</td><td>${escapeHtml(row.sourceReference || paymentT("relatedAccountActivity"))}</td><td>${escapeHtml(row.methodLabel || "-")}${row.transactionReference ? `<small>${escapeHtml(row.transactionReference)}</small>` : ""}</td><td>${escapeHtml(formatMoney(row.debitAmount || 0))}</td><td>${escapeHtml(formatMoney(row.creditAmount || 0))}</td><td><strong>${escapeHtml(formatMoney(row.runningBalance || 0))}</strong></td></tr>`).join("") : `<tr><td colspan="7">${paymentT("noConfirmedActivity")}</td></tr>`;
    }
    const collectionRows = document.getElementById("merchant-collection-draft-rows");
    if (collectionRows) {
      const canReview = ["Admin", "ERPAdmin"].includes(getAuth()?.user?.role);
      collectionRows.replaceChildren(...(collections.length ? collections.map((item) => paymentCollectionDraftRow(item, canReview)) : [paymentEmptyTableRow(7, paymentT("noAccountCollections"))]));
    }
    const orderRows = document.getElementById("merchant-order-rows");
    if (orderRows) {
      orderRows.replaceChildren(...(orders.length ? orders.map((order) => {
        const row = document.createElement("tr");
        const items = (order.lines || []).map((line) => `${line.productName || line.skuCode || paymentT("item")} × ${line.quantity}`).join(", ");
        const financialState = order.financialClosureStatus === "FinanciallyClosed" ? ` · ${paymentT("financiallyClosed")}` : "";
        [order.operationNumber || shortId(order.operationId, "OP"), formatDateTime(order.date), items || "-", formatMoney(order.saleTotal), formatMoney(order.collectionsAllocated), formatMoney(order.acceptedReturns), formatMoney(order.additionalCharges), formatMoney(order.amountReductions), formatMoney(order.refunds), formatMoney(order.remaining), paymentWorkflowStatusLabel(order.status) + financialState].forEach((value) => { const cell = document.createElement("td"); cell.textContent = value; row.append(cell); });
        return row;
      }) : [paymentEmptyTableRow(11, paymentT("noCompletedOrders"))]));
    }
    void loadMerchantFinancialClosure(merchantId);
  } catch (exception) {
    status.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadFinanceAccounts() {
  try {
    const result = await request("/api/v1/finance/accounts");
    return Array.isArray(result) ? result : (Array.isArray(result?.accounts) ? result.accounts : []);
  } catch {
    return [];
  }
}

async function createMerchantOpening(merchantId) {
  const amount = await promptDialog({ title: foundationT("payments.merchantOpening.add"), label: foundationT("payments.merchantOpening.amount"), inputType: "number", required: true });
  if (amount === null) return;
  const asOfDate = await promptDialog({ title: foundationT("payments.merchantOpening.add"), label: foundationT("payments.merchantOpening.date"), inputType: "date", required: true });
  if (asOfDate === null) return;
  const description = await promptDialog({ title: foundationT("payments.merchantOpening.add"), label: foundationT("payments.merchantOpening.note"), multiline: true, required: true });
  if (description === null) return;
  try {
    await request(`/api/v1/payments/merchant-accounts/${merchantId}/opening-balances`, { method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ amount: Number(amount), asOfDate, description, submitForReview: true }) });
    notice(foundationT("payments.merchantOpening.submitted"), "success"); await loadMerchantBalance();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function approveMerchantOpening(id) {
  try {
    await request(`/api/v1/payments/opening-balances/${id}/approve`, { method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() } });
    notice(foundationT("payments.merchantOpening.posted"), "success"); await loadMerchantBalance();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function correctMerchantOpening(opening) {
  if (!opening) return;
  const amount = await promptDialog({ title: foundationT("payments.merchantOpening.correct"), label: foundationT("payments.merchantOpening.amount"), defaultValue: String(opening.amount), inputType: "number", required: true });
  if (amount === null) return;
  const description = await promptDialog({ title: foundationT("payments.merchantOpening.correct"), label: foundationT("payments.merchantOpening.note"), multiline: true, required: true });
  if (description === null) return;
  try {
    await request(`/api/v1/payments/opening-balances/${opening.id}/correct`, { method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ amount: Number(amount), asOfDate: opening.asOfDate, description }) });
    notice(foundationT("payments.merchantOpening.posted"), "success"); await loadMerchantBalance();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function loadMerchantFinancialClosure(merchantId) {
  const section = document.getElementById("merchant-financial-closure");
  const rows = document.getElementById("merchant-closure-rows");
  const submit = document.getElementById("merchant-closure-submit");
  if (!section || !rows || !submit) return;
  try {
    const eligible = await request(`/api/v1/payments/merchant-accounts/${merchantId}/financial-closure/eligible`);
    if (document.getElementById("payment-merchant")?.value !== merchantId) return;
    const items = Array.isArray(eligible) ? eligible : [];
    section.hidden = false;
    rows.replaceChildren(...(items.length ? items.map((item) => {
      const row = document.createElement("tr");
      row.innerHTML = `<td><input type="checkbox" data-closure-operation="${escapeHtml(item.id)}" aria-label="${escapeHtml(paymentT("select"))} ${escapeHtml(item.operationNumber)}"></td><td>${escapeHtml(item.operationNumber)}</td><td>${escapeHtml(formatMoney(item.settlementAmount))}</td><td>${escapeHtml(formatMoney(item.allocatedAmount))}</td><td>${escapeHtml(formatMoney(item.remainingAmount))}</td>`;
      return row;
    }) : [paymentEmptyTableRow(5, paymentT("noEligibleSales"))]));
    const canSubmit = ["Accountant", "Admin", "ERPAdmin"].includes(getAuth()?.user?.role);
    submit.hidden = !canSubmit || !items.length;
    const review = document.getElementById("merchant-closure-review");
    if (review) {
      review.hidden = !["Admin", "ERPAdmin"].includes(getAuth()?.user?.role);
      if (!review.hidden) {
        try {
          const proposals = await request(`/api/v1/payments/merchant-accounts/${merchantId}/financial-closure/proposals`);
          const pending = (Array.isArray(proposals) ? proposals : []).filter((proposal) => proposal.status === "PendingAdminReview");
          review.innerHTML = pending.length ? `<h4>${paymentT("closureRequests")}</h4>${pending.map((proposal) => {
            const itemMarkup = (proposal.items || []).map((item) => `<label><input type="checkbox" data-review-operation="${escapeHtml(item.operationId)}" checked> ${escapeHtml(item.operationNumber)} — ${escapeHtml(formatMoney(item.settlementAmount))}</label>`).join(" ");
            return `<div class="closure-review-card" data-closure-proposal="${escapeHtml(proposal.id)}"><p>${escapeHtml(paymentT("requestedAt", { date: formatDateTime(proposal.submittedAt) }))}</p>${proposal.notes ? `<p class="muted-text">${escapeHtml(proposal.notes)}</p>` : ""}<div>${itemMarkup}</div><button class="button secondary" type="button" data-review-closure="${escapeHtml(proposal.id)}">${paymentT("approveSelectionRejectRest")}</button></div>`;
          }).join("")}` : "";
          review.querySelectorAll("[data-review-closure]").forEach((button) => button.addEventListener("click", async () => {
            const card = button.closest("[data-closure-proposal]");
            const approvedOperationIds = [...card.querySelectorAll("[data-review-operation]:checked")].map((input) => input.dataset.reviewOperation);
            try {
              await request(`/api/v1/payments/financial-closure/proposals/${button.dataset.reviewClosure}/review`, { method: "POST", body: JSON.stringify({ approvedOperationIds, rejectionReason: paymentT("rejectedByAdminSelection") }) });
              notice(paymentT("selectionApproved"), "success");
              await loadMerchantFinancialClosure(merchantId);
            } catch (exception) { notice(getFriendlyWorkspaceError(exception), "error"); }
          }));
        } catch (exception) { review.textContent = getFriendlyWorkspaceError(exception); }
      }
    }
    submit.onclick = async () => {
      const operationIds = [...document.querySelectorAll("[data-closure-operation]:checked")].map((node) => node.dataset.closureOperation);
      if (!operationIds.length) { notice(paymentT("selectAtLeastOne"), "error"); return; }
      try {
        await request(`/api/v1/payments/merchant-accounts/${merchantId}/financial-closure/proposals`, { method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ operationIds, notes: null }) });
        notice(paymentT("submittedForClosure"), "success");
        await loadMerchantFinancialClosure(merchantId);
      } catch (exception) { notice(getFriendlyWorkspaceError(exception), "error"); }
    };
  } catch (exception) {
    section.hidden = true;
  }
}

function merchantStatementEvent(row) {
  const keys = { SaleCharge: "saleAdded", ReturnCredit: "returnAccepted", ExchangeSurcharge: "exchangeAmountAdded", ExchangeCredit: "exchangeCreditAdded", Collection: "moneyReceived", RefundPayout: "moneyRefunded", AdditionalCharge: "additionalChargeAdded", BalanceReduction: "amountReduced" };
  return row?.eventLabel || (keys[row?.entryType] ? paymentT(keys[row.entryType]) : paymentT("accountActivity"));
}

function merchantAccountFlags(flags) {
  if (!flags.length) return paymentT("noAccountWarnings");
  const keys = { CreditAvailable: "creditAvailable", ReservedRefund: "reservedRefund", LowCollectionCoverage: "lowCollectionCoverage", WeakPaymentDiscipline: "weakPaymentDiscipline", Provisional: "provisionalWarning", HighExposure: "highExposure" };
  return flags.map((flag) => keys[flag] ? paymentT(keys[flag]) : flag).join(" ");
}

function merchantAccountGrade(classification) {
  const gradeKeys = { A: "excellent", B: "good", C: "fair", D: "weak", E: "critical" };
  const label = classification.gradeLabel || (gradeKeys[classification.grade] ? paymentT(gradeKeys[classification.grade]) : classification.grade) || paymentT("notRated");
  const score = Number(classification.score ?? 0).toFixed(2);
  const provisional = (classification.flags || []).includes("Provisional");
  const gradeLabel = label;
  const provisionalLabel = paymentT("provisionalGrade");
  return `${gradeLabel} · ${score}${provisional ? ` · ${provisionalLabel}` : ""}`;
}

function shortId(value, prefix = "REF") {
  const raw = String(value || "").trim();
  if (!raw) return "";
  return contextualReference({ businessReference: raw, prefix, language: currentLanguage });
}

async function reviewMerchantAccountCollection(id, approved) {
  const reason = approved ? null : await promptDialog({
    title: paymentT("rejectCollectionTitle"),
    label: paymentT("rejectCollectionLabel"),
    multiline: true,
    required: true
  });
  if (!approved && !reason) return;
  try {
    await request(`/api/v1/payments/collections/${id}/${approved ? "approve" : "reject"}`, {
      method: "POST",
      body: approved ? undefined : JSON.stringify({ reason })
    });
    notice(approved ? paymentT("collectionApprovedPosted") : paymentT("collectionRejected"), "success");
    await Promise.all([loadMerchantBalance(), loadPayments(), loadPaymentHistory(), loadPaymentAudit()]);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function loadPaymentAudit() {
  const rows = document.getElementById("payment-audit-rows");
  if (!rows) return;
  try {
    const result = await request(`/api/v1/payments/audit?page=${paymentPageState.audit}&pageSize=50`);
    const items = result.items || [];
    if (!items.length) {
      rows.replaceChildren(paymentEmptyTableRow(7, paymentT("noAuditEvents")));
    } else {
      rows.replaceChildren(...items.map((item) => {
        const row = paymentAuditRow(item);
        row.querySelector("[data-payment-audit-details]")?.addEventListener("click", () => togglePaymentAuditDetails(row, item));
        return row;
      }));
    }
    renderPaymentPager("payment-audit-pagination", result, (page) => {
      paymentPageState.audit = page;
      void loadPaymentAudit();
    });
  } catch (exception) {
    rows.replaceChildren(paymentEmptyTableRow(7, getFriendlyWorkspaceError(exception)));
  }
}

async function loadCollectionWorkInbox() {
  const rows = document.getElementById("payment-review-rows");
  if (!rows) return;
  try {
    const result = await request("/api/v1/payments/collection-work?pageSize=200");
    const items = Array.isArray(result) ? result : (result.items || []);
    const canReview = ["Admin", "ERPAdmin"].includes(getAuth()?.user?.role);
    const rendered = items.map((item) => {
      const row = paymentTableRow([
        item.scope === "MerchantAccount" ? paymentT("merchantAccount") : paymentT("otherPayments"),
        item.reference || shortId(item.id, "COL"),
        item.operationNumber || item.source || "-",
        item.assignedToName || "Shared Admin queue",
        formatMoney(item.amount || 0),
        movementMethodLabel(item.movementMethod || item.paymentMethod),
        [item.rejectionReason ? `${paymentWorkflowStatusLabel(item.status)}: ${item.rejectionReason}` : paymentWorkflowStatusLabel(item.status), item.notes].filter(Boolean).join(" · ")
      ]);
      const action = document.createElement("td");
      if (canReview && item.status === "PendingAdminReview") {
        const approve = document.createElement("button");
        approve.className = "button secondary table-action";
        approve.type = "button";
        approve.textContent = foundationT("payments.approve");
        approve.addEventListener("click", () => reviewMerchantAccountCollection(item.id, true));
        const reject = document.createElement("button");
        reject.className = "button danger table-action";
        reject.type = "button";
        reject.textContent = foundationT("payments.reject");
        reject.addEventListener("click", () => reviewMerchantAccountCollection(item.id, false));
        action.append(approve, document.createTextNode(" "), reject);
      } else {
        action.textContent = "-";
      }
      row.append(action);
      return row;
    });
    rows.replaceChildren(...(rendered.length ? rendered : [paymentEmptyTableRow(8, paymentT("noCollectionWork"))]));
  } catch (exception) {
    rows.replaceChildren(paymentEmptyTableRow(8, getFriendlyWorkspaceError(exception)));
  }
}

function paymentEmptyTableRow(columnCount, message) {
  const row = document.createElement("tr");
  const cell = document.createElement("td");
  cell.colSpan = columnCount;
  cell.textContent = message;
  row.append(cell);
  return row;
}

function paymentAuditRow(item) {
  const row = paymentTableRow([
    formatDateTime(item.occurredAt) || "-",
    paymentAuditActionLabel(item.action),
    `${paymentWorkflowStatusLabel(item.previousStatus)} → ${paymentWorkflowStatusLabel(item.newStatus)}`,
    item.amount == null ? "-" : formatMoney(item.amount),
    movementMethodLabel(item.paymentMethod),
    item.reason || "-",
    paymentT("details")
  ]);
  const details = row.lastElementChild;
  if (details) {
    details.textContent = "";
    const button = document.createElement("button");
    button.className = "button secondary table-action";
    button.type = "button";
    button.textContent = paymentT("details");
    button.dataset.paymentAuditDetails = item.id;
    details.append(button);
  }
  return row;
}

function togglePaymentAuditDetails(row, item) {
  const existing = row.nextElementSibling;
  if (existing?.dataset.paymentAuditDetail === item.id) {
    existing.remove();
    return;
  }
  const detail = document.createElement("tr");
  detail.dataset.paymentAuditDetail = item.id;
  const cell = document.createElement("td");
  cell.colSpan = 7;
  const fields = [
    [paymentT("actorLabel"), `${item.actorName || (item.actorId ? shortId(item.actorId, "USR") : paymentT("historicalActorUnavailable"))}${item.actorRole ? ` · ${item.actorRole}` : ""}`],
    [paymentT("merchant"), item.merchantName || (item.merchantId ? shortId(item.merchantId, "MER") : paymentT("outsideMerchantAccount"))],
    [paymentT("buyer"), item.buyerName || "-"],
    [paymentT("operation"), item.operationNumber || (item.operationId ? shortId(item.operationId, "OP") : "-")],
    [paymentT("payment"), item.paymentLogId ? shortId(item.paymentLogId, "PAY") : "-"],
    [paymentT("collection"), item.collectionDraftId ? shortId(item.collectionDraftId, "COL") : "-"],
    [paymentT("scope"), item.scope === "MerchantAccount" ? paymentT("merchantAccount") : paymentT("otherPayments")],
    [paymentT("method"), movementMethodLabel(item.paymentMethod)],
    [paymentT("transactionReferenceLabel"), item.transactionReference || "-"],
    [paymentT("status"), `${paymentWorkflowStatusLabel(item.previousStatus)} → ${paymentWorkflowStatusLabel(item.newStatus)}`],
    [foundationT("common.notes"), item.reason || "-"],
    [paymentT("correlation"), item.correlationId || "-"],
    [paymentT("idempotency"), item.idempotencyKey || "-"],
    [paymentT("recordedDetails"), item.dataJson ? displaySafeText(item.dataJson, "AUD") : "-" ]
  ];
  cell.innerHTML = `<div class="detail-grid audit-payment-detail">${fields.map(([label, value]) => `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`).join("")}</div>`;
  detail.append(cell);
  row.after(detail);
}

function paymentAuditActionLabel(action) {
  const keys = {
    PaymentLogOpened: "paymentLogOpened", PaymentLogInitialized: "paymentLogInitialized", PaymentAssigned: "paymentAssigned", PaymentReassigned: "paymentReassigned", CollectionReassigned: "collectionReassigned", InstallmentDrafted: "collectionDrafted", InstallmentApproved: "collectionApproved", InstallmentRejected: "collectionRejected", PaymentSubLogSubmittedForApproval: "paymentSubLogSubmitted", PaymentSubLogApproved: "paymentSubLogApproved", PaymentSubLogRejected: "paymentSubLogRejected", CashReceiptRecorded: "cashReceiptRecorded", CashReceiptSubmittedForApproval: "cashReceiptSubmitted", CashReceiptApproved: "cashReceiptApproved", CashReceiptRejected: "cashReceiptRejected", MerchantAccountCollectionDrafted: "merchantCollectionDrafted", MerchantAccountCollectionSubmitted: "merchantCollectionSubmitted", MerchantAccountCollectionApproved: "merchantCollectionApproved", MerchantAccountCollectionRejected: "merchantCollectionRejected", FinancialAdjustmentRequested: "adjustmentRequested", FinancialAdjustmentApproved: "adjustmentApproved", FinancialAdjustmentRejected: "adjustmentRejected", CashRefundPaidOut: "refundPaid", ReconciliationCompleted: "reconciliationCompleted"
  };
  return keys[action] ? paymentT(keys[action]) : action || "-";
}

function paymentWorkflowStatusLabel(status) {
  const keys = { PendingAccountant: "waitingAccountant", PendingAdminReview: "waitingAdminApproval", PendingAdmin: "waitingAdminReview", PendingApproval: "pendingApproval", Completed: "completed", Confirmed: "confirmed", Rejected: "rejected", Draft: "draft", Cancelled: "cancelled" };
  return keys[status] ? paymentT(keys[status]) : status || "-";
}

function paymentCollectionDraftRow(item, canReview) {
  const status = item.rejectionReason ? `${paymentWorkflowStatusLabel(item.status)}: ${item.rejectionReason}` : paymentWorkflowStatusLabel(item.status);
  const row = paymentTableRow([
    formatDateTime(item.draftedAt) || "-",
    item.reference || shortId(item.id, "COL"),
    item.assignedToName || paymentT("sharedAdminQueue"),
    formatMoney(item.amount || 0),
    movementMethodLabel(item.paymentMethod),
    status
  ]);
  const action = document.createElement("td");
  if (canReview && item.status === "PendingAdminReview") {
    const approve = document.createElement("button");
    approve.className = "button secondary table-action";
    approve.type = "button";
    approve.textContent = paymentT("approve");
    approve.addEventListener("click", () => reviewMerchantAccountCollection(item.id, true));
    const reject = document.createElement("button");
    reject.className = "button danger table-action";
    reject.type = "button";
    reject.textContent = paymentT("reject");
    reject.addEventListener("click", () => reviewMerchantAccountCollection(item.id, false));
    action.append(approve, document.createTextNode(" "), reject);
  } else if (item.status === "Draft") {
    const submit = document.createElement("button");
    submit.className = "button secondary table-action";
    submit.type = "button";
    submit.textContent = paymentT("sendForApproval");
    submit.addEventListener("click", () => submitMerchantAccountCollection(item.id));
    action.append(submit);
  } else {
    action.textContent = "-";
  }
  row.append(action);
  return row;
}

async function submitMerchantAccountCollection(id) {
  try {
    await request(`/api/v1/payments/collections/${encodeURIComponent(id)}/submit`, { method: "POST" });
    notice(paymentT("collectionSentForApproval"), "success");
    await Promise.all([loadMerchantBalance(), loadPayments(), loadPaymentHistory(), loadPaymentAudit()]);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function paymentTableRow(values) {
  const row = document.createElement("tr");
  values.forEach((value) => {
    const cell = document.createElement("td");
    cell.textContent = value;
    row.append(cell);
  });
  return row;
}

function operationReference(record) {
  return contextualReference({
    businessReference: record?.operationNumber,
    context: record?.shopifyOrderNumber ? `Shopify ${record.shopifyOrderNumber}` : "",
    prefix: "OP",
    language: currentLanguage
  });
}

function paymentReference(record) {
  const operation = operationReference(record);
  return `${paymentT("payment")} — ${operation}`;
}

function stocktakeReference(session, location = null) {
  const locationName = location?.name || inventoryLocations.find((item) => item.id === session?.locationId)?.name;
  const happenedAt = session?.sessionDate || session?.createdAt;
  const context = [locationName, happenedAt ? formatDateTime(happenedAt) : ""].filter(Boolean).join(" / ");
  return contextualReference({ context, prefix: "STK", language: currentLanguage });
}

function displaySafeText(value, prefix = "REF") {
  return sanitizeVisibleText(value, { prefix, language: currentLanguage });
}

function sanitizeVisibleIdentifiers(root) {
  if (!root) return;
  const elements = [root, ...root.querySelectorAll("*")];
  elements.forEach((element) => {
    element.childNodes.forEach((node) => {
      if (node.nodeType !== Node.TEXT_NODE) return;
      const safeText = displaySafeText(node.nodeValue);
      if (safeText !== node.nodeValue) node.nodeValue = safeText;
    });
  });
}

window.__lenseeFindVisibleUuidLeaks = () => findVisibleUuidLeaks(document.getElementById("view")).map((node) => node.nodeValue);
window.__lenseeSanitizeVisibleIdentifiers = () => sanitizeVisibleIdentifiers(document.getElementById("view"));

function reportCatalogEntry(key) {
  return reportCatalogEntries.find((entry) => entry.key === key);
}

function renderReportFormatButtons(key) {
  const formats = reportCatalogEntry(key)?.formats || ["csv"];
  return formats.map((format) => `<button class="button secondary table-action" type="button" data-download-report="${escapeHtml(key)}" data-export-format="${escapeHtml(format)}">${escapeHtml(format.toUpperCase())}</button>`).join("");
}

function renderAnalyticalReportRow(key, title, description, targetId) {
  return `<article class="report-ledger-row" data-report-key="${escapeHtml(key)}">
    <div class="report-ledger-row-head"><div><h4>${escapeHtml(title)}</h4><p>${escapeHtml(description)}</p></div><div class="report-format-actions" aria-label="${escapeHtml(title)} export formats">${renderReportFormatButtons(key)}</div></div>
    <div id="${escapeHtml(targetId)}" class="table-wrap compact-table">${escapeHtml(foundationT("common.loading"))}</div>
  </article>`;
}

function updateExportDocket({ title, format, fileName, status }) {
  const language = getReportExportLanguage();
  const languageLabels = { ar: foundationT("reports.arabic"), en: foundationT("reports.english"), bi: "العربية + الإنجليزية" };
  const setText = (id, value) => { const node = document.getElementById(id); if (node) node.textContent = uiText(value); };
  if (title) setText("export-docket-title", title);
  if (format) setText("export-docket-format", format.toUpperCase());
  setText("export-docket-language", languageLabels[language] || language);
  if (fileName) setText("export-docket-filename", fileName);
  if (status) setText("report-export-status", status);
}

function financeT(key, params = {}) {
  return foundationT(`finance.${key}`, params);
}

function reportsT(key, params = {}) {
  return foundationT(`reports.${key}`, params);
}

function financeValueLabel(kind, value) {
  const key = String(value || "").trim();
  if (!key) return "—";
  const labels = {
    accountType: { CashOnHand: "accountTypes.cash", BankAccount: "accountTypes.bank", Wallet: "accountTypes.wallet" },
    direction: { Credit: "directions.inflow", Debit: "directions.outflow" },
    status: { Draft: "statuses.draft", PendingReview: "statuses.pendingReview", Pending: "statuses.pending", Paid: "statuses.paid", Posted: "statuses.posted", Rejected: "statuses.rejected", Corrected: "statuses.corrected" },
    category: { MerchantCollection: "categories.merchantCollection", InstallmentCollection: "categories.installmentCollection", OtherPayment: "categories.otherPayment", OperatingExpense: "categories.operatingExpense", CLevelWithdrawal: "categories.cLevelWithdrawal", FinanceOpeningBalance: "categories.openingBalance", TreasuryOpeningBalance: "categories.openingBalance", RefundPayout: "categories.refundPayout", SupplyPayment: "categories.supplyPayment" }
  };
  const translationKey = labels[kind]?.[key];
  return translationKey ? financeT(translationKey) : key;
}

async function renderFinance() {
  const role = getAuth()?.user?.role;
  const canCreateExpense = role === "Admin";
  const canCreateOpening = ["Admin", "Accountant"].includes(role);
  const canManageAccounts = role === "Admin";
  const canAssignWithdrawal = role === "Admin";
  const canApprove = role === "Admin";
  const view = document.getElementById("view");
  if (!view) {
    throw new Error("Finance workspace container is unavailable.");
  }
  view.innerHTML = `<section class="workspace finance-workspace">
     <div class="workspace-intro"><div><p class="eyebrow">${financeT("accounts.kicker")}</p><h2>${financeT("accounts.title")}</h2><p>${financeT("accounts.setupDescription")}</p></div><button id="finance-refresh" class="button secondary" type="button">${foundationT("common.refresh")}</button></div>
     ${["Admin", "CLevel"].includes(role) ? `<section class="workspace-panel" hidden><div class="section-head"><h3>${financeT("executive.title")}</h3><div class="form-grid compact-form"><label>${financeT("executive.period")}<select id="finance-summary-period" class="select"><option value="daily">${financeT("executive.daily")}</option><option value="monthly">${financeT("executive.monthly")}</option></select></label><label>${financeT("businessDate")}<input id="finance-summary-date" class="input" type="date"></label></div></div><div id="finance-executive-summary">${foundationT("common.loading")}</div></section>` : ""}
     ${canManageAccounts ? `<section class="workspace-panel" hidden><div class="section-head"><h3>${financeT("transfers.title")}</h3></div><form id="finance-transfer-form" class="form-grid compact-form"><label>${financeT("transfers.source")}<select id="finance-transfer-source" class="select" required></select></label><label>${financeT("transfers.destination")}<select id="finance-transfer-destination" class="select" required></select></label><label>${financeT("amount")}<input id="finance-transfer-amount" class="input" type="number" min="0.0001" step="0.0001" required></label><label>${financeT("transfers.fee")}<input id="finance-transfer-fee" class="input" type="number" min="0" step="0.0001" value="0"></label><label>${financeT("businessDate")}<input id="finance-transfer-date" class="input" type="date" required></label><label>${financeT("description")}<input id="finance-transfer-notes" class="input" maxlength="1000"></label><button class="button primary" type="submit">${financeT("transfers.create")}</button></form><div id="finance-transfers" class="table-wrap compact-table"></div></section>` : ""}
     ${canManageAccounts ? `<section class="workspace-panel" hidden><div class="section-head"><h3>${financeT("categories.title")}</h3></div><form id="finance-category-form" class="form-grid compact-form"><label>${financeT("categories.kind")}<select id="finance-category-kind" class="select"><option value="Expense">${financeT("expenses.title")}</option><option value="Withdrawal">${financeT("withdrawals.title")}</option></select></label><label>${financeT("categories.code")}<input id="finance-category-code" class="input" maxlength="60" required></label><label>${financeT("categories.english")}<input id="finance-category-english" class="input" maxlength="150" required></label><label>${financeT("categories.arabic")}<input id="finance-category-arabic" class="input" maxlength="150" required></label><label>${financeT("categories.order")}<input id="finance-category-order" class="input" type="number" min="0" value="100" required></label><button class="button primary" type="submit">${financeT("categories.create")}</button></form><div id="finance-categories" class="table-wrap compact-table"></div></section>` : ""}
     ${canManageAccounts ? `<section class="workspace-panel" hidden><div class="section-head"><h3>${financeT("repayments.title")}</h3></div><form id="finance-repayment-form" class="form-grid compact-form"><label>${financeT("withdrawals.title")}<select id="finance-repayment-withdrawal" class="select" required></select></label><label>${financeT("account")}<select id="finance-repayment-account" class="select" required></select></label><label>${financeT("amount")}<input id="finance-repayment-amount" class="input" type="number" min="0.0001" step="0.0001" required></label><label>${financeT("method")}<select id="finance-repayment-method" class="select"><option value="CashHandToHand">${financeT("methods.cash")}</option><option value="BankTransfer">${financeT("methods.bank")}</option><option value="Wallet">${financeT("methods.wallet")}</option></select></label><label>${financeT("businessDate")}<input id="finance-repayment-date" class="input" type="date" required></label><label>${financeT("description")}<input id="finance-repayment-notes" class="input" maxlength="1000"></label><button class="button primary" type="submit">${financeT("repayments.create")}</button></form></section>` : ""}
    <section class="workspace-panel finance-accounts-panel"><div class="section-head"><div><p class="ledger-kicker">${financeT("accounts.kicker")}</p><h3>${financeT("accounts.title")}</h3></div></div>
      ${canManageAccounts ? `<form id="finance-account-form" class="form-grid compact-form"><div class="field"><label for="finance-account-name">${financeT("accounts.name")}</label><input id="finance-account-name" class="input" maxlength="150" required></div><div class="field"><label for="finance-account-type">${financeT("accounts.type")}</label><select id="finance-account-type" class="select"><option value="CashOnHand">${financeT("accounts.cashOnHand")}</option><option value="BankAccount">${financeT("accounts.bankAccount")}</option><option value="Wallet">${financeT("accounts.wallet")}</option></select></div><div class="field"><label for="finance-account-reference">${financeT("accounts.reference")}</label><input id="finance-account-reference" class="input" maxlength="200"></div><div class="field"><label for="finance-account-details">${financeT("accounts.details")}</label><input id="finance-account-details" class="input" maxlength="1000"></div><div class="form-actions"><button class="button primary" type="submit">${financeT("accounts.create")}</button></div></form>` : `<p class="muted-text">${financeT("readOnly")}</p>`}
      <div id="finance-accounts" class="table-wrap compact-table">${foundationT("common.loading")}</div>
      <div class="section-head tight-head"><div><p class="ledger-kicker">${financeT("openingBalances.kicker")}</p><h3>${financeT("openingBalances.title")}</h3></div></div>
      ${canCreateOpening ? `<form id="finance-opening-form" class="form-grid compact-form"><div class="field"><label for="finance-opening-account">${financeT("account")}</label><select id="finance-opening-account" class="select" required></select></div><div class="field"><label for="finance-opening-amount">${financeT("amount")}</label><input id="finance-opening-amount" class="input" type="number" min="0.0001" step="0.0001" required></div><div class="field"><label for="finance-opening-date">${financeT("openingBalances.asOfDate")}</label><input id="finance-opening-date" class="input" type="date" required></div><div class="field full-span"><label for="finance-opening-description">${financeT("openingBalances.description")}</label><input id="finance-opening-description" class="input" maxlength="1000" required></div><div class="form-actions"><button class="button primary" type="submit">${financeT("openingBalances.create")}</button></div></form>` : ""}
      <div id="finance-opening-balances" class="table-wrap compact-table">${foundationT("common.loading")}</div></section>
     <div class="scenario-grid finance-metrics"><article class="scenario-card"><span>${financeT("expectedCash")}</span><strong id="finance-cash">—</strong></article><article class="scenario-card"><span>${financeT("expectedBank")}</span><strong id="finance-bank">—</strong></article><article class="scenario-card"><span>${financeT("expectedWallet")}</span><strong id="finance-wallet">—</strong></article><article class="scenario-card"><span>${financeT("totalLiquidFunds")}</span><strong id="finance-total">—</strong></article><article class="scenario-card" hidden><span>${financeT("executive.merchantReceivables")}</span><strong id="finance-receivables">—</strong></article></div>
     <div class="split-workspace finance-ledger-layout" hidden>
      <section class="workspace-panel"><div class="section-head"><div><p class="ledger-kicker">${financeT("expenses.kicker")}</p><h3>${financeT("expenses.title")}</h3></div></div>
        ${canCreateExpense ? `<form id="finance-expense-form" class="form-grid compact-form"><div class="field"><label for="finance-expense-category">${financeT("expenses.category")}</label><select id="finance-expense-category" class="select"><option value="Salary">${financeT("expenses.salary")}</option><option value="Rent">${financeT("expenses.rent")}</option><option value="SocialMedia">${financeT("expenses.socialMedia")}</option><option value="SoftwareAndTechnologySubscriptions">${financeT("expenses.softwareTechnology")}</option><option value="Other">${financeT("expenses.other")}</option></select></div><div class="field"><label for="finance-expense-account">${financeT("account")}</label><select id="finance-expense-account" class="select" required></select></div><div class="field"><label for="finance-expense-method">${financeT("method")}</label><select id="finance-expense-method" class="select"><option value="CashHandToHand">${financeT("methods.cash")}</option><option value="BankTransfer">${financeT("methods.bank")}</option><option value="Wallet">${financeT("methods.wallet")}</option></select></div><div class="field"><label for="finance-expense-amount">${financeT("amount")}</label><input id="finance-expense-amount" class="input" type="number" min="0.0001" step="0.0001" required></div><div class="field"><label for="finance-expense-date">${financeT("businessDate")}</label><input id="finance-expense-date" class="input" type="date" required></div><div class="field"><label for="finance-expense-description">${financeT("description")}</label><input id="finance-expense-description" class="input" maxlength="1000"></div><div class="form-actions"><button class="button primary" type="submit">${financeT("expenses.create")}</button></div></form>` : `<p class="muted-text">${financeT("readOnly")}</p>`}
        <div id="finance-expenses" class="table-wrap compact-table">${foundationT("common.loading")}</div></section>
      <section class="workspace-panel"><div class="section-head"><div><p class="ledger-kicker">${financeT("withdrawals.kicker")}</p><h3>${financeT("withdrawals.title")}</h3></div></div>
        ${canAssignWithdrawal ? `<form id="finance-withdrawal-form" class="form-grid compact-form"><div class="field"><label for="finance-withdrawal-user">${financeT("withdrawals.assignedTo")}</label><select id="finance-withdrawal-user" class="select" required></select></div><div class="field"><label for="finance-withdrawal-account">${financeT("account")}</label><select id="finance-withdrawal-account" class="select" required></select></div><div class="field"><label for="finance-withdrawal-method">${financeT("method")}</label><select id="finance-withdrawal-method" class="select"><option value="CashHandToHand">${financeT("methods.cash")}</option><option value="BankTransfer">${financeT("methods.bank")}</option><option value="Wallet">${financeT("methods.wallet")}</option></select></div><div class="field"><label for="finance-withdrawal-amount">${financeT("amount")}</label><input id="finance-withdrawal-amount" class="input" type="number" min="0.0001" step="0.0001" required></div><div class="field"><label for="finance-withdrawal-date">${financeT("businessDate")}</label><input id="finance-withdrawal-date" class="input" type="date" required></div><div class="field"><label for="finance-withdrawal-reason">${financeT("withdrawals.reason")}</label><input id="finance-withdrawal-reason" class="input" maxlength="1000" required></div><div class="form-actions"><button class="button primary" type="submit">${financeT("withdrawals.create")}</button></div></form>` : `<p class="muted-text">${canApprove ? financeT("withdrawals.reviewOnly") : financeT("readOnly")}</p>`}
        <div id="finance-withdrawals" class="table-wrap compact-table">${foundationT("common.loading")}</div></section>
     </div><section class="workspace-panel" hidden><div class="section-head"><div><p class="ledger-kicker">${financeT("ledger.kicker")}</p><h3>${financeT("ledger.title")}</h3></div></div><div id="finance-ledger" class="table-wrap compact-table">${foundationT("common.loading")}</div></section><section class="workspace-panel" hidden><div class="section-head"><div><p class="ledger-kicker">${financeT("reconciliation.kicker")}</p><h3>${financeT("reconciliation.title")}</h3><p class="muted-text">${financeT("reconciliation.help")}</p></div></div><div id="finance-reconciliation" class="table-wrap compact-table">${foundationT("common.loading")}</div></section></section>`;
  document.getElementById("finance-refresh")?.addEventListener("click", loadFinanceWorkspace);
  document.getElementById("finance-account-form")?.addEventListener("submit", submitFinanceAccount);
  document.getElementById("finance-opening-form")?.addEventListener("submit", submitFinanceOpeningBalance);
  document.getElementById("finance-expense-form")?.addEventListener("submit", submitFinanceExpense);
  document.getElementById("finance-withdrawal-form")?.addEventListener("submit", submitFinanceWithdrawal);
  document.getElementById("finance-transfer-form")?.addEventListener("submit", submitFinanceTransfer);
  document.getElementById("finance-category-form")?.addEventListener("submit", submitFinanceCategory);
  const repaymentForm = document.getElementById("finance-repayment-form");
  if (repaymentForm) {
    const reasonLabel = document.createElement("label");
    reasonLabel.textContent = financeT("repayments.correctionNote");
    reasonLabel.hidden = true;
    const reasonInput = document.createElement("input");
    reasonInput.id = "finance-repayment-correction-note";
    reasonInput.className = "input";
    reasonInput.maxLength = 1000;
    reasonLabel.append(reasonInput);
    repaymentForm.insertBefore(reasonLabel, repaymentForm.querySelector('button[type="submit"]'));
    const history = document.createElement("div");
    history.id = "finance-repayment-history";
    history.className = "table-wrap compact-table";
    repaymentForm.after(history);
    repaymentForm.addEventListener("submit", submitFinanceRepayment);
    document.getElementById("finance-repayment-withdrawal")?.addEventListener("change", loadFinanceRepaymentHistory);
  }
  document.getElementById("finance-summary-period")?.addEventListener("change", loadFinanceExecutiveSummary);
  document.getElementById("finance-summary-date")?.addEventListener("change", loadFinanceExecutiveSummary);
  if (document.getElementById("finance-withdrawal-form")) {
    const categoryField = document.createElement("div");
    categoryField.className = "field";
    const categoryLabel = document.createElement("label");
    categoryLabel.htmlFor = "finance-withdrawal-category";
    categoryLabel.textContent = financeT("expenses.category");
    const categorySelect = document.createElement("select");
    categorySelect.id = "finance-withdrawal-category";
    categorySelect.className = "select";
    categorySelect.required = true;
    categoryField.append(categoryLabel, categorySelect);
    document.getElementById("finance-withdrawal-user")?.closest(".field")?.after(categoryField);
  }
  await loadFinanceWorkspace();
  await loadFinanceExecutiveSummary();
}

async function loadFinanceWorkspace() {
  try {
    const [overview, accounts, expenses, withdrawals, ledger, reconciliation, openingBalances, cLevels, categories, transfers] = await Promise.all([
      request("/api/v1/finance/overview"), request("/api/v1/finance/accounts"), request("/api/v1/finance/expenses?pageSize=25"), request("/api/v1/finance/withdrawals?pageSize=25"), request("/api/v1/finance/ledger?pageSize=50"), request("/api/v1/finance/reconciliation?pageSize=50").catch(() => ({ items: [] })), request("/api/v1/finance/opening-balances?pageSize=25").catch(() => ({ items: [] })), getAuth()?.user?.role === "Admin" ? request("/api/v1/finance/c-level-users").catch(() => []) : Promise.resolve([])
      ,request("/api/v1/finance/categories").catch(() => []), getAuth()?.user?.role === "Admin" ? request("/api/v1/finance/transfers?pageSize=25").catch(() => ({ items: [] })) : Promise.resolve({ items: [] })
    ]);
    const money = (value) => formatMoney(Number(value || 0));
    document.getElementById("finance-cash").textContent = money(overview.expectedCash); document.getElementById("finance-bank").textContent = money(overview.expectedBank); document.getElementById("finance-wallet").textContent = money(overview.expectedWallet); document.getElementById("finance-total").textContent = money(overview.totalLiquidFunds);
    const receivables = document.getElementById("finance-receivables"); if (receivables) receivables.textContent = money(overview.outstandingMerchantReceivable ?? overview.merchantReceivables ?? 0);
    const accountOptions = `<option value="">${financeT("chooseAccount")}</option>${accounts.filter((account) => account.isActive).map((account) => `<option value="${escapeHtml(account.id)}">${escapeHtml(account.name)} · ${escapeHtml(financeValueLabel("accountType", account.type))}</option>`).join("")}`;
    ["finance-expense-account", "finance-withdrawal-account", "finance-opening-account", "finance-transfer-source", "finance-transfer-destination", "finance-repayment-account"].forEach((id) => { const select = document.getElementById(id); if (select) select.innerHTML = accountOptions; });
    [["finance-expense-account", "finance-expense-method"], ["finance-withdrawal-account", "finance-withdrawal-method"], ["finance-repayment-account", "finance-repayment-method"]].forEach(([accountId, methodId]) => {
      const accountSelect = document.getElementById(accountId);
      const methodSelect = document.getElementById(methodId);
      if (!accountSelect || !methodSelect) return;
      methodSelect.value = "BankTransfer";
      accountSelect.onchange = () => {
        const type = accounts.find((item) => item.id === accountSelect.value)?.type;
        methodSelect.value = type === "Wallet" ? "Wallet" : type === "CashOnHand" ? "CashHandToHand" : "BankTransfer";
      };
    });
    const repaymentWithdrawalSelect = document.getElementById("finance-repayment-withdrawal");
    if (repaymentWithdrawalSelect) repaymentWithdrawalSelect.innerHTML = `<option value="">${financeT("repayments.chooseWithdrawal")}</option>${(withdrawals.items || []).map((item) => item.withdrawal || item).filter((item) => item.status === "Posted").map((item) => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.reason)} · ${escapeHtml(money(item.amount))}</option>`).join("")}`;
    const categoryName = (item) => currentLanguage === "ar" ? item.arabicName : item.englishName;
    const expenseCategories = categories.filter((item) => item.kind === "Expense" && item.isActive);
    const expenseCategorySelect = document.getElementById("finance-expense-category");
    if (expenseCategorySelect) expenseCategorySelect.innerHTML = expenseCategories.map((item) => `<option value="${escapeHtml(item.code)}">${escapeHtml(categoryName(item))}</option>`).join("");
    const withdrawalCategorySelect = document.getElementById("finance-withdrawal-category");
    if (withdrawalCategorySelect) withdrawalCategorySelect.innerHTML = categories.filter((item) => item.kind === "Withdrawal" && item.isActive).map((item) => `<option value="${escapeHtml(item.id)}">${escapeHtml(categoryName(item))}</option>`).join("");
    const categoryTable = document.getElementById("finance-categories");
    if (categoryTable) {
      categoryTable.innerHTML = financeTable(categories, [financeT("categories.kind"), financeT("categories.code"), financeT("categories.english"), financeT("categories.arabic"), financeT("categories.order"), financeT("status"), financeT("actions")], (item) => `<tr><td>${escapeHtml(item.kind === "Expense" ? financeT("expenses.title") : item.kind === "Withdrawal" ? financeT("withdrawals.title") : uiText(item.kind))}</td><td>${escapeHtml(item.code)}</td><td>${escapeHtml(item.englishName)}</td><td>${escapeHtml(item.arabicName)}</td><td>${escapeHtml(item.sortOrder)}</td><td>${escapeHtml(item.isActive ? financeT("accounts.active") : financeT("accounts.inactive"))}</td><td><button class="button secondary table-action" data-finance-category-id="${escapeHtml(item.id)}" type="button">${financeT("categories.edit")}</button><button class="button secondary table-action" data-finance-category-toggle="${escapeHtml(item.id)}" type="button">${item.isActive ? financeT("categories.deactivate") : financeT("categories.activate")}</button></td></tr>`);
      categoryTable.querySelectorAll("[data-finance-category-id]").forEach((button) => button.addEventListener("click", () => editFinanceCategory(categories.find((item) => item.id === button.dataset.financeCategoryId))));
      categoryTable.querySelectorAll("[data-finance-category-toggle]").forEach((button) => button.addEventListener("click", () => void toggleFinanceCategory(categories.find((item) => item.id === button.dataset.financeCategoryToggle))));
    }
    const transferTable = document.getElementById("finance-transfers");
    if (transferTable) transferTable.innerHTML = financeTable(transfers.items || [], [financeT("transfers.source"), financeT("transfers.destination"), financeT("amount"), financeT("transfers.fee"), financeT("businessDate"), financeT("description")], (item) => `<tr><td>${escapeHtml(accounts.find((account) => account.id === item.sourceAccountId)?.name || "—")}</td><td>${escapeHtml(accounts.find((account) => account.id === item.destinationAccountId)?.name || "—")}</td><td>${escapeHtml(money(item.amount))}</td><td>${escapeHtml(money(item.feeAmount))}</td><td>${escapeHtml(item.businessDate)}</td><td>${escapeHtml(item.notes || "—")}</td></tr>`);
    document.getElementById("finance-accounts").innerHTML = financeTable(accounts || [], [financeT("accounts.name"), financeT("accounts.type"), financeT("accounts.reference"), financeT("status"), financeT("actions")], (item) => `<tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(financeValueLabel("accountType", item.type))}</td><td><bdi>${escapeHtml(item.reference || "—")}</bdi></td><td>${escapeHtml(item.isActive ? financeT("accounts.active") : financeT("accounts.inactive"))}</td><td><button class="button secondary table-action" type="button" data-finance-account-detail="${escapeHtml(item.id)}">${financeT("details")}</button></td></tr>`);
    document.querySelectorAll("[data-finance-account-detail]").forEach((button) => button.addEventListener("click", () => {
      const account = accounts.find((item) => item.id === button.dataset.financeAccountDetail);
      if (account) void confirmDialog({ title: account.name, message: account.details || financeT("empty"), confirmLabel: financeT("close"), translateMessage: false, translateTitle: false });
    }));
    const cLevelSelect = document.getElementById("finance-withdrawal-user"); if (cLevelSelect) cLevelSelect.innerHTML = `<option value="">${financeT("withdrawals.chooseUser")}</option>${cLevels.map((user) => `<option value="${escapeHtml(user.id)}">${escapeHtml(user.fullName)}</option>`).join("")}`;
    const canApprove = getAuth()?.user?.role === "Admin";
    document.getElementById("finance-opening-balances").innerHTML = financeTable(openingBalances.items || [], [financeT("account"), financeT("amount"), financeT("openingBalances.direction"), financeT("openingBalances.asOfDate"), financeT("status"), financeT("actions")], (item) => `<tr><td>${escapeHtml(accounts.find((account) => account.id === item.financeAccountId)?.name || contextualReference({ context: item.financeAccountId, prefix: "REF", language: currentLanguage }))}</td><td>${escapeHtml(money(item.amount))}</td><td>${escapeHtml(financeValueLabel("direction", item.direction))}</td><td><bdi>${escapeHtml(item.asOfDate)}</bdi></td><td>${escapeHtml(financeValueLabel("status", item.status))}</td><td><button class="button secondary table-action" type="button" data-finance-opening-note="${escapeHtml(item.id)}">${financeT("details")}</button>${canApprove && item.status === "PendingReview" ? `<button class="button secondary table-action" type="button" data-finance-action="approve" data-finance-kind="opening-balances" data-finance-id="${escapeHtml(item.id)}">${escapeHtml(financeT("openingBalances.approve"))}</button><button class="button secondary table-action" type="button" data-finance-action="reject" data-finance-kind="opening-balances" data-finance-id="${escapeHtml(item.id)}">${escapeHtml(financeT("openingBalances.reject"))}</button>` : ""}${["Admin", "Accountant"].includes(getAuth()?.user?.role) && item.status === "Posted" && !item.replacedByOpeningBalanceId ? `<button class="button secondary table-action" type="button" data-finance-opening-correct="${escapeHtml(item.id)}">${financeT("openingBalances.correct")}</button>` : ""}</td></tr>`);
    document.getElementById("finance-expenses").innerHTML = financeTable(expenses.items || [], [financeT("expenses.category"), financeT("amount"), financeT("status"), financeT("businessDate"), financeT("actions")], (item) => `<tr><td>${escapeHtml(categoryName(categories.find((category) => category.id === item.categoryId) || { englishName: item.category, arabicName: item.category }))}</td><td>${escapeHtml(money(item.amount))}</td><td>${escapeHtml(financeValueLabel("status", item.status))}</td><td><bdi>${escapeHtml(item.businessDate)}</bdi></td><td><button class="button secondary table-action" type="button" data-finance-detail="expense" data-finance-id="${escapeHtml(item.id)}">${financeT("details")}</button>${canApprove && item.status === "Pending" ? `<button class="button secondary table-action" type="button" data-finance-edit-expense="${escapeHtml(item.id)}">${financeT("expenses.editPending")}</button>` : ""}${canApprove && ["Pending", "PendingReview", "Draft"].includes(item.status) ? `<button class="button secondary table-action" type="button" data-finance-action="confirm" data-finance-kind="expenses" data-finance-id="${escapeHtml(item.id)}">${escapeHtml(financeT("expenses.confirmPayment"))}</button>` : ""}${canApprove && ["Paid", "Posted"].includes(item.status) && !item.replacedByExpenseId ? `<button class="button secondary table-action" type="button" data-finance-correct-expense="${escapeHtml(item.id)}">${financeT("openingBalances.correct")}</button>` : ""}</td></tr>`);
    document.getElementById("finance-withdrawals").innerHTML = financeTable(withdrawals.items || [], [financeT("withdrawals.assignedTo"), financeT("amount"), financeT("status"), financeT("businessDate"), financeT("actions")], (item) => { const withdrawal = item.withdrawal || item; return `<tr><td>${escapeHtml(item.assignedToName || withdrawal.assignedToCLevelUserId || "—")}</td><td>${escapeHtml(money(withdrawal.amount))}</td><td>${escapeHtml(financeValueLabel("status", withdrawal.status))}</td><td><bdi>${escapeHtml(withdrawal.businessDate || "")}</bdi></td><td><button class="button secondary table-action" type="button" data-finance-detail="withdrawal" data-finance-id="${escapeHtml(withdrawal.id)}">${financeT("details")}</button>${canApprove && withdrawal.status === "PendingReview" ? `<button class="button secondary table-action" type="button" data-finance-action="approve" data-finance-kind="withdrawals" data-finance-id="${escapeHtml(withdrawal.id)}">${escapeHtml(financeT("approve"))}</button>` : ""}${canApprove && withdrawal.status === "Posted" && !withdrawal.replacedByWithdrawalId ? `<button class="button secondary table-action" type="button" data-finance-correct-withdrawal="${escapeHtml(withdrawal.id)}">${financeT("openingBalances.correct")}</button>` : ""}</td></tr>`; });
    const accountNames = new Map(accounts.map((account) => [account.id, account.name]));
    document.getElementById("finance-ledger").innerHTML = financeTable(ledger.rows || [], [financeT("ledger.category"), financeT("ledger.direction"), financeT("amount"), financeT("account"), financeT("businessDate")], (item) => `<tr><td>${escapeHtml(financeValueLabel("category", item.category))}</td><td>${escapeHtml(financeValueLabel("direction", item.direction))}</td><td>${escapeHtml(money(item.amount))}</td><td>${escapeHtml(accountNames.get(item.financeAccountId) || contextualReference({ businessReference: item.financeAccountReference, context: item.financeAccountId, prefix: "REF", language: currentLanguage }))}</td><td><bdi>${escapeHtml(item.businessDate)}</bdi></td></tr>`);
    document.getElementById("finance-reconciliation").innerHTML = financeTable(reconciliation.items || [], [financeT("reconciliation.account"), financeT("reconciliation.inflow"), financeT("reconciliation.outflow"), financeT("reconciliation.net"), financeT("reconciliation.expected"), financeT("reconciliation.entries"), financeT("reconciliation.history")], (item) => `<tr><td>${escapeHtml(item.accountName)}<span class="muted-cell">${escapeHtml(financeValueLabel("accountType", item.accountType))}</span></td><td>${escapeHtml(money(item.postedInflow))}</td><td>${escapeHtml(money(item.postedOutflow))}</td><td>${escapeHtml(money(item.netMovement))}</td><td>${escapeHtml(money(item.expectedBalance))}</td><td>${escapeHtml(item.postedEntryCount)}</td><td>${item.historicalNegativeBalance ? `${financeT("reconciliation.historicalNegative")} (${escapeHtml(money(item.minimumPostedBalance))})` : "—"}</td></tr>`);
    document.querySelectorAll("[data-finance-action]").forEach((button) => button.addEventListener("click", () => void postFinanceReviewAction(button)));
    document.querySelectorAll("[data-finance-opening-note]").forEach((button) => button.addEventListener("click", () => {
      const opening = (openingBalances.items || []).find((item) => item.id === button.dataset.financeOpeningNote);
      if (opening) void confirmDialog({ title: financeT("details"), message: opening.description || financeT("empty"), confirmLabel: financeT("close"), translateMessage: false });
    }));
    document.querySelectorAll("[data-finance-opening-correct]").forEach((button) => button.addEventListener("click", () => void correctFinanceOpening((openingBalances.items || []).find((item) => item.id === button.dataset.financeOpeningCorrect))));
    document.querySelectorAll("[data-finance-edit-expense]").forEach((button) => button.addEventListener("click", () => {
      const expense = (expenses.items || []).find((item) => item.id === button.dataset.financeEditExpense);
      const form = document.getElementById("finance-expense-form");
      if (!expense || !form) return;
      form.dataset.editId = expense.id;
      document.getElementById("finance-expense-category").value = expense.category;
      document.getElementById("finance-expense-account").value = expense.financeAccountId;
      document.getElementById("finance-expense-method").value = expense.movementMethod;
      document.getElementById("finance-expense-amount").value = expense.amount;
      document.getElementById("finance-expense-date").value = expense.businessDate;
      document.getElementById("finance-expense-description").value = expense.description || "";
      form.querySelector('button[type="submit"]').textContent = financeT("expenses.savePending");
      form.scrollIntoView({ behavior: "smooth", block: "center" });
    }));
    document.querySelectorAll("[data-finance-correct-expense]").forEach((button) => button.addEventListener("click", () => void correctFinanceExpense((expenses.items || []).find((item) => item.id === button.dataset.financeCorrectExpense))));
    document.querySelectorAll("[data-finance-correct-withdrawal]").forEach((button) => button.addEventListener("click", () => void correctFinanceWithdrawal((withdrawals.items || []).map((item) => item.withdrawal || item).find((item) => item.id === button.dataset.financeCorrectWithdrawal))));
    document.querySelectorAll("[data-finance-detail]").forEach((button) => button.addEventListener("click", async () => {
      const kind = button.dataset.financeDetail;
      const source = kind === "expense" ? (expenses.items || []).find((item) => item.id === button.dataset.financeId) :
        (withdrawals.items || []).map((item) => item.withdrawal || item).find((item) => item.id === button.dataset.financeId);
      if (!source) return;
      const details = kind === "expense" ? [source.description, source.correctionNote] : [source.reason, source.correctionNote];
      if (kind === "withdrawal") {
        try { const repayment = await request(`/api/v1/finance/withdrawals/${source.id}/repayments`); details.push(`${financeT("repayments.remaining")}: ${money(repayment.remaining)}`); for (const item of repayment.items || []) details.push(`${financeT("repayments.title")} ${money(item.amount)}${item.replacedByRepaymentId ? ` · ${financeT("repayments.corrected")}` : ""}${item.notes ? ` · ${item.notes}` : ""}${item.correctionNote ? ` · ${item.correctionNote}` : ""}`); }
        catch { /* The withdrawal note remains available if repayment loading fails. */ }
      }
      void confirmDialog({ title: financeT("details"), message: details.filter(Boolean).join(" · ") || financeT("empty"), confirmLabel: financeT("close"), translateMessage: false });
    }));
  } catch (exception) { notice(getFriendlyWorkspaceError(exception), "error"); }
}

function financeTable(rows, headers, row) { return `<table><thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead><tbody>${rows.length ? rows.map(row).join("") : `<tr><td colspan="${headers.length}">${escapeHtml(financeT("empty"))}</td></tr>`}</tbody></table>`; }
async function submitFinanceAccount(event) { event.preventDefault(); const name = document.getElementById("finance-account-name").value.trim(); if (!name) return; await request("/api/v1/finance/accounts", { method: "POST", body: JSON.stringify({ name, type: canonicalSelectValue("finance-account-type", "financeAccountType"), reference: document.getElementById("finance-account-reference").value.trim() || null, details: document.getElementById("finance-account-details").value.trim() || null }) }); notice(financeT("accounts.created"), "success"); document.getElementById("finance-account-form")?.reset(); await loadFinanceWorkspace(); }
async function submitFinanceOpeningBalance(event) { event.preventDefault(); const payload = { financeAccountId: document.getElementById("finance-opening-account").value, amount: Number(document.getElementById("finance-opening-amount").value), direction: "Credit", asOfDate: document.getElementById("finance-opening-date").value, description: document.getElementById("finance-opening-description").value.trim() }; await request("/api/v1/finance/opening-balances", { method: "POST", body: JSON.stringify(payload) }); notice(financeT("posted"), "success"); document.getElementById("finance-opening-form")?.reset(); await loadFinanceWorkspace(); }
async function correctFinanceOpening(opening) {
  if (!opening) return;
  const amountText = await promptDialog({ title: financeT("openingBalances.correct"), label: financeT("amount"), defaultValue: String(opening.amount), inputType: "number", required: true });
  if (amountText === null) return;
  const note = await promptDialog({ title: financeT("openingBalances.correct"), label: financeT("openingBalances.correctionNote"), required: true, multiline: true });
  if (note === null) return;
  try {
    await request(`/api/v1/finance/opening-balances/${opening.id}/correct`, { method: "POST", body: JSON.stringify({ amount: Number(amountText), description: note }) });
    notice(financeT(getAuth()?.user?.role === "Admin" ? "posted" : "statuses.pendingReview"), "success");
    await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function correctFinanceExpense(expense) {
  if (!expense) return;
  const amountText = await promptDialog({ title: financeT("expenses.correct"), label: financeT("amount"), defaultValue: String(expense.amount), inputType: "number", required: true });
  if (amountText === null) return;
  const note = await promptDialog({ title: financeT("expenses.correct"), label: financeT("openingBalances.correctionNote"), required: true, multiline: true });
  if (note === null) return;
  try {
    await request(`/api/v1/finance/expenses/${expense.id}/correct`, { method: "POST", body: JSON.stringify({
      financeAccountId: expense.financeAccountId, amount: Number(amountText), category: expense.category,
      movementMethod: expense.movementMethod, businessDate: expense.businessDate, description: note
    }) });
    notice(financeT("posted"), "success"); await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function correctFinanceWithdrawal(withdrawal) {
  if (!withdrawal) return;
  const amountText = await promptDialog({ title: financeT("withdrawals.correct"), label: financeT("amount"), defaultValue: String(withdrawal.amount), inputType: "number", required: true });
  if (amountText === null) return;
  const note = await promptDialog({ title: financeT("withdrawals.correct"), label: financeT("openingBalances.correctionNote"), required: true, multiline: true });
  if (note === null) return;
  try {
    await request(`/api/v1/finance/withdrawals/${withdrawal.id}/correct`, { method: "POST", body: JSON.stringify({
      assignedToCLevelUserId: withdrawal.assignedToCLevelUserId, financeAccountId: withdrawal.financeAccountId,
      amount: Number(amountText), movementMethod: withdrawal.movementMethod, businessDate: withdrawal.businessDate, reason: note
    }) });
    notice(financeT("posted"), "success"); await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}
async function submitFinanceExpense(event) { event.preventDefault(); const category = document.getElementById("finance-expense-category").value; const description = document.getElementById("finance-expense-description").value.trim(); if (category === "Other" && !description) { notice(financeT("expenses.otherDescriptionRequired"), "error"); return; } const payload = { financeAccountId: document.getElementById("finance-expense-account").value, amount: Number(document.getElementById("finance-expense-amount").value), category, movementMethod: canonicalSelectValue("finance-expense-method", "movementMethod"), businessDate: document.getElementById("finance-expense-date").value, description }; const form = document.getElementById("finance-expense-form"); const editId = form?.dataset.editId; await request(editId ? `/api/v1/finance/expenses/${editId}` : "/api/v1/finance/expenses", { method: editId ? "PUT" : "POST", body: JSON.stringify(payload) }); notice(financeT("statuses.pending"), "success"); await loadFinanceWorkspace(); }
async function submitFinanceWithdrawal(event) { event.preventDefault(); const payload = { assignedToCLevelUserId: document.getElementById("finance-withdrawal-user").value, financeAccountId: document.getElementById("finance-withdrawal-account").value, amount: Number(document.getElementById("finance-withdrawal-amount").value), movementMethod: canonicalSelectValue("finance-withdrawal-method", "movementMethod"), businessDate: document.getElementById("finance-withdrawal-date").value, reason: document.getElementById("finance-withdrawal-reason").value.trim(), categoryId: document.getElementById("finance-withdrawal-category")?.value || null }; await request("/api/v1/finance/withdrawals", { method: "POST", body: JSON.stringify(payload) }); notice(financeT("posted"), "success"); await loadFinanceWorkspace(); }
async function postFinanceReviewAction(button) { const action = button.dataset.financeAction; const kind = button.dataset.financeKind; const id = button.dataset.financeId; if (!action || !kind || !id) return; const reason = action === "reject" ? await promptDialog({ title: financeT("openingBalances.reject"), label: financeT("openingBalances.correctionNote"), required: true, multiline: true }) : null; if (action === "reject" && !reason) return; button.disabled = true; try { await request(`/api/v1/finance/${kind}/${id}/${action}`, { method: "POST", ...(reason ? { body: JSON.stringify({ reason }) } : {}) }); notice(financeT(action === "reject" ? "rejected" : "posted"), "success"); await loadFinanceWorkspace(); } catch (exception) { notice(getFriendlyWorkspaceError(exception), "error"); button.disabled = false; } }

async function submitFinanceTransfer(event) {
  event.preventDefault();
  const form = document.getElementById("finance-transfer-form");
  form.dataset.requestId ||= crypto.randomUUID();
  const payload = {
    clientRequestId: form.dataset.requestId,
    sourceAccountId: document.getElementById("finance-transfer-source").value,
    destinationAccountId: document.getElementById("finance-transfer-destination").value,
    amount: Number(document.getElementById("finance-transfer-amount").value),
    feeAmount: Number(document.getElementById("finance-transfer-fee").value || 0),
    businessDate: document.getElementById("finance-transfer-date").value,
    notes: document.getElementById("finance-transfer-notes").value.trim() || null
  };
  try {
    await request("/api/v1/finance/transfers", { method: "POST", body: JSON.stringify(payload) });
    notice(financeT("transfers.posted"), "success");
    form.reset(); delete form.dataset.requestId;
    await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function submitFinanceCategory(event) {
  event.preventDefault();
  const payload = {
    kind: document.getElementById("finance-category-kind").value,
    code: document.getElementById("finance-category-code").value.trim(),
    englishName: document.getElementById("finance-category-english").value.trim(),
    arabicName: document.getElementById("finance-category-arabic").value.trim(),
    sortOrder: Number(document.getElementById("finance-category-order").value), parentId: null
  };
  try {
    await request("/api/v1/finance/categories", { method: "POST", body: JSON.stringify(payload) });
    notice(financeT("categories.created"), "success");
    document.getElementById("finance-category-form").reset();
    await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function editFinanceCategory(category) {
  if (!category) return;
  const englishName = await promptDialog({ title: financeT("categories.edit"), label: financeT("categories.english"), defaultValue: category.englishName, required: true });
  if (englishName === null) return;
  const arabicName = await promptDialog({ title: financeT("categories.edit"), label: financeT("categories.arabic"), defaultValue: category.arabicName, required: true });
  if (arabicName === null) return;
  const orderValue = await promptDialog({ title: financeT("categories.edit"), label: financeT("categories.order"), defaultValue: String(category.sortOrder), inputType: "number", required: true });
  if (orderValue === null) return;
  try {
    await request(`/api/v1/finance/categories/${category.id}`, { method: "PUT", body: JSON.stringify({ englishName, arabicName, sortOrder: Number(orderValue), isActive: category.isActive }) });
    notice(financeT("categories.updated"), "success");
    await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function toggleFinanceCategory(category) {
  if (!category) return;
  try {
    await request(`/api/v1/finance/categories/${category.id}`, { method: "PUT", body: JSON.stringify({ englishName: category.englishName, arabicName: category.arabicName, sortOrder: category.sortOrder, isActive: !category.isActive }) });
    notice(financeT("categories.updated"), "success");
    await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function submitFinanceRepayment(event) {
  event.preventDefault();
  const form = document.getElementById("finance-repayment-form");
  form.dataset.requestId ||= crypto.randomUUID();
  const id = document.getElementById("finance-repayment-withdrawal").value;
  const payload = {
    clientRequestId: form.dataset.requestId,
    financeAccountId: document.getElementById("finance-repayment-account").value,
    amount: Number(document.getElementById("finance-repayment-amount").value),
    movementMethod: canonicalSelectValue("finance-repayment-method", "movementMethod"),
    businessDate: document.getElementById("finance-repayment-date").value,
    notes: document.getElementById("finance-repayment-notes").value.trim() || null
  };
  const correctionId = form.dataset.correctId;
  if (correctionId) payload.reason = document.getElementById("finance-repayment-correction-note").value.trim();
  if (correctionId && !payload.reason) { notice(financeT("repayments.correctionNoteRequired"), "error"); return; }
  try {
    await request(`/api/v1/finance/withdrawals/${id}/repayments${correctionId ? `/${correctionId}/correct` : ""}`, { method: "POST", body: JSON.stringify(payload) });
    notice(financeT("repayments.posted"), "success");
    form.reset(); delete form.dataset.requestId; delete form.dataset.correctId;
    await loadFinanceWorkspace();
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function loadFinanceRepaymentHistory() {
  const withdrawalId = document.getElementById("finance-repayment-withdrawal")?.value;
  const target = document.getElementById("finance-repayment-history");
  if (!target) return;
  if (!withdrawalId) { target.replaceChildren(); return; }
  try {
    const history = await request(`/api/v1/finance/withdrawals/${withdrawalId}/repayments`);
    const table = document.createElement("table");
    const head = table.createTHead().insertRow();
    [financeT("businessDate"), financeT("amount"), financeT("account"), financeT("description"), financeT("status"), financeT("actions")]
      .forEach((label) => { const cell = document.createElement("th"); cell.textContent = label; head.append(cell); });
    const body = table.createTBody();
    for (const item of history.items || []) {
      const row = body.insertRow();
      const accountOption = [...document.querySelectorAll("#finance-repayment-account option")]
        .find((option) => option.value === item.financeAccountId);
      [item.businessDate, formatMoney(item.amount), accountOption?.textContent || item.financeAccountId,
        [item.notes, item.correctionNote].filter(Boolean).join(" · ") || "—",
        item.replacedByRepaymentId ? financeT("repayments.corrected") : financeT("statuses.posted")]
        .forEach((value) => { const cell = row.insertCell(); cell.textContent = value; });
      const action = row.insertCell();
      if (item.replacedByRepaymentId) continue;
      const button = document.createElement("button");
      button.className = "button secondary table-action";
      button.type = "button";
      button.textContent = financeT("repayments.correct");
      action.append(button);
      button.addEventListener("click", () => {
      const form = document.getElementById("finance-repayment-form");
      if (!item || !form) return;
      form.dataset.correctId = item.id;
      delete form.dataset.requestId;
      document.getElementById("finance-repayment-account").value = item.financeAccountId;
      document.getElementById("finance-repayment-method").value = item.movementMethod;
      document.getElementById("finance-repayment-amount").value = item.amount;
      document.getElementById("finance-repayment-date").value = item.businessDate;
      document.getElementById("finance-repayment-notes").value = item.notes || "";
      const reason = document.getElementById("finance-repayment-correction-note");
      reason.closest("label").hidden = false;
      reason.required = true;
      reason.value = "";
      form.querySelector('button[type="submit"]').textContent = financeT("repayments.saveCorrection");
      form.scrollIntoView({ behavior: "smooth", block: "center" });
      });
    }
    target.replaceChildren(table);
  } catch (error) { notice(getFriendlyWorkspaceError(error), "error"); }
}

async function loadFinanceExecutiveSummary() {
  const target = document.getElementById("finance-executive-summary");
  if (!target) return;
  const period = document.getElementById("finance-summary-period")?.value || "daily";
  const date = document.getElementById("finance-summary-date")?.value;
  try {
    const summary = await request(`/api/v1/reports/executive-summary?period=${encodeURIComponent(period)}${date ? `&date=${encodeURIComponent(date)}` : ""}`);
    const tiles = [
      [financeT("cash"), summary.currentBalances.cash],
      [financeT("bank"), summary.currentBalances.bank],
      [financeT("wallet"), summary.currentBalances.wallet],
      [financeT("executive.availableFunds"), summary.currentBalances.availableFunds],
      [financeT("executive.merchantReceivables"), summary.currentBalances.merchantReceivables],
      [financeT("executive.netSales"), summary.flows.netSales],
      [financeT("executive.collections"), summary.flows.actualCollections],
      [financeT("executive.expenses"), summary.flows.paidExpenses],
      [financeT("executive.withdrawals"), summary.flows.withdrawals],
      [financeT("executive.repayments"), summary.flows.repayments],
      [financeT("executive.supplyPayments"), summary.flows.postedSupplyPayments],
      [financeT("executive.netCashMovement"), summary.flows.netExternalCashMovement],
      [financeT("executive.paidPieces"), summary.units.paidPiecesSold],
      [financeT("executive.bonusPieces"), summary.units.bonusPieces]
    ];
    target.innerHTML = `<p class="muted-text">${escapeHtml(summary.start)} – ${escapeHtml(summary.endExclusive)} · ${financeT("executive.currentBalances")}${summary.units.estimated ? ` · ${financeT("executive.estimated")}` : ""}${summary.units.unresolvedPackLines ? ` · ${financeT("executive.incomplete")}: ${escapeHtml(summary.units.unresolvedPackLines)}` : ""}</p><div class="scenario-grid">${tiles.map(([label, value]) => `<article class="scenario-card"><span>${escapeHtml(label)}</span><strong>${escapeHtml(label === financeT("executive.paidPieces") || label === financeT("executive.bonusPieces") ? Number(value || 0).toLocaleString(currentLanguage === "ar" ? "ar-EG" : "en-US") : formatMoney(Number(value || 0)))}</strong></article>`).join("")}</div>`;
  } catch (error) { target.textContent = getFriendlyWorkspaceError(error); }
}

async function renderReports() {
  const role = getAuth()?.user.role;
  const canSeeStock = role !== "Accountant";
  try {
    reportCatalogEntries = await request("/api/v1/reports/catalog");
  } catch {
    reportCatalogEntries = [];
  }
  document.getElementById("view").innerHTML = `
    <section class="band reporting-ledger">
      <header class="report-register-header">
        <div><p class="ledger-kicker">${reportsT("register")}</p><h2>${reportsT("title")}</h2><p class="muted-text">${reportsT("subtitle")}</p></div>
        <button id="reports-refresh" class="button secondary" type="button">${reportsT("refresh")}</button>
      </header>
      <div class="report-workspace">
        <main class="report-register">
        <section class="report-panel report-controls">
          <div class="section-head tight-head"><h3>${reportsT("documentLanguage")}</h3><span class="muted-text">${escapeHtml(foundationT("app.inline.pDFXLSXCSV"))}</span></div>
          <div class="segmented-control" role="radiogroup" aria-label="${escapeHtml(foundationT("app.attribute.exportLanguage"))}">
            <label><input type="radio" name="report-export-language" value="ar" ${currentLanguage === "ar" ? "checked" : ""}>${reportsT("arabic")}</label>
            <label><input type="radio" name="report-export-language" value="en" ${currentLanguage === "en" ? "checked" : ""}>${reportsT("english")}</label>
            <label><input type="radio" name="report-export-language" value="bi">${reportsT("bilingual")}</label>
          </div>
          <div class="report-filter-register" aria-label="${escapeHtml(foundationT("app.attribute.reportFilters"))}">
            ${canSeeStock ? `<label class="field"><span>${reportsT("stockLocation")}</span><select id="report-filter-location" class="input"><option value="">${reportsT("allAuthorizedLocations")}</option></select></label>` : ""}
            <label class="field"><span>${reportsT("fromDate")}</span><input id="report-filter-from" class="input" type="date"></label>
            <label class="field"><span>${reportsT("toDate")}</span><input id="report-filter-to" class="input" type="date"></label>
            <label class="field"><span>${reportsT("operationType")}</span><select id="report-filter-operation-type" class="input"><option value="">${reportsT("allTypes")}</option><option value="WholesaleSale">${reportsT("wholesaleSale")}</option><option value="RetailSale">${reportsT("retailSale")}</option><option value="Return">${reportsT("return")}</option></select></label>
            <label class="field"><span>${foundationT("reports.sort")}</span><select id="report-filter-sort" class="input"><option value="createdAt">${foundationT("reports.sortCreated")}</option><option value="operationNumber">${foundationT("reports.sortOperationNumber")}</option><option value="total">${foundationT("reports.sortTotal")}</option><option value="quantity">${foundationT("reports.sortQuantity")}</option></select></label>
            <label class="field"><span>${foundationT("reports.direction")}</span><select id="report-filter-direction" class="input"><option value="desc">${foundationT("reports.descending")}</option><option value="asc">${foundationT("reports.ascending")}</option></select></label>
            <label class="field"><span>${reportsT("supplyStatus")}</span><select id="report-filter-supply-status" class="input"><option value="">${reportsT("allStatuses")}</option><option value="Draft">${reportsT("draft")}</option><option value="Received">${reportsT("received")}</option><option value="Cancelled">${reportsT("cancelled")}</option></select></label>
          </div>
        </section>
        <section class="report-ledger-section"><div class="ledger-section-title"><span>01</span><div><h3>${reportsT("analyticalReports")}</h3><p>${reportsT("analyticalHelp")}</p></div></div>
        ${canSeeStock ? renderAnalyticalReportRow("stock", reportsT("stock"), reportsT("stockDescription"), "report-stock") : ""}
        ${renderAnalyticalReportRow("operations", reportsT("operations"), reportsT("operationsDescription"), "report-operations")}
        ${renderAnalyticalReportRow("payments", reportsT("payments"), reportsT("paymentsDescription"), "report-payments")}
        ${renderAnalyticalReportRow("supply", reportsT("supply"), reportsT("supplyDescription"), "report-supply")}
        ${renderAnalyticalReportRow("merchant-balances", reportsT("merchantBalances"), reportsT("merchantBalancesDescription"), "report-balances")}
        </section>
        <section class="report-panel report-download-panel">
          <div class="ledger-section-title"><span>02</span><div><h3>${reportsT("officialDocuments")}</h3><p>${reportsT("officialDocumentsHelp")}</p></div></div>
          <div class="download-grid">
            ${renderReportSearchPicker("operation-bill", reportsT("operationBill"), reportsT("operationBillPlaceholder"), reportsT("downloadBill"))}
            ${renderReportSearchPicker("payment-receipt", reportsT("paymentReceipt"), reportsT("paymentReceiptPlaceholder"), reportsT("downloadReceipt"))}
            ${renderReportSearchPicker("cash-receipt", reportsT("cashReceipt"), reportsT("cashReceiptPlaceholder"), reportsT("downloadCashReceipt"))}
            ${renderReportSearchPicker("supply-landed-cost", reportsT("supplyLandedCost"), reportsT("supplyLandedCostPlaceholder"), reportsT("downloadLandedCost"))}
            ${renderReportSearchPicker("merchant-statement", reportsT("merchantStatement"), reportsT("merchantStatementPlaceholder"), reportsT("downloadStatement"))}
            ${renderReportSearchPicker("stocktake-summary", reportsT("stocktakeSummary"), reportsT("stocktakeSummaryPlaceholder"), reportsT("downloadSummary"))}
          </div>
        </section>
        <section class="report-panel"><div class="section-head tight-head"><h3>${reportsT("exportLog")}</h3><span id="report-export-count" class="muted-text">${reportsT("loading")}</span></div><div id="report-exports" class="table-wrap compact-table">${reportsT("loading")}</div></section>
        </main>
        <aside class="export-docket" aria-label="${escapeHtml(foundationT("reports.exportDocket"))}">
          <p class="ledger-kicker">${reportsT("exportDocket")}</p>
          <h3 id="export-docket-title">${reportsT("noExportSelected")}</h3>
          <dl><div><dt>${reportsT("language")}</dt><dd id="export-docket-language">${currentLanguage === "ar" ? reportsT("arabic") : reportsT("english")}</dd></div><div><dt>${reportsT("format")}</dt><dd id="export-docket-format">—</dd></div><div><dt>${reportsT("scope")}</dt><dd id="export-docket-scope">${reportsT("authorizedScope")}</dd></div><div><dt>${reportsT("filename")}</dt><dd id="export-docket-filename">${reportsT("assignedByServer")}</dd></div></dl>
          <p id="report-export-status" class="docket-status" role="status" aria-live="polite">${reportsT("readyForExport")}</p>
        </aside>
      </div>
    </section>`;

  document.getElementById("reports-refresh").addEventListener("click", loadReports);
  document.querySelectorAll("#report-filter-location, #report-filter-from, #report-filter-to, #report-filter-operation-type, #report-filter-sort, #report-filter-direction, #report-filter-supply-status").forEach((control) => control?.addEventListener("change", () => {
    reportPageState = { stock: 1, operations: 1, payments: 1, supply: 1, merchantBalances: 1 };
    void loadReports();
  }));
  document.querySelectorAll("[data-download-report]").forEach((button) => button.addEventListener("click", () => downloadReport(button.dataset.downloadReport, button.dataset.exportFormat, button)));
  document.querySelectorAll("[data-pdf-report]").forEach((button) => button.addEventListener("click", () => downloadReportPdf(button.dataset.pdfReport, button.dataset.exportFormat, button)));
  document.querySelectorAll('input[name="report-export-language"]').forEach((control) => control.addEventListener("change", () => updateExportDocket({ status: "Language updated. Ready for export." })));
  await loadReports();
}

async function loadReports() {
  const role = getAuth()?.user.role;
  const canSeeStock = role !== "Accountant";
  await Promise.all([
    canSeeStock ? loadReportLocations() : Promise.resolve(),
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
    const result = await request(`/api/v1/reports/stock?${reportListParams("stock", reportPageState.stock)}`);
    const rows = result.items || [];
    target.innerHTML = `<table><thead><tr><th>${escapeHtml(foundationT("app.inventoryLocation"))}</th><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("payments.available"))}</th><th>${escapeHtml(foundationT("app.inline.reserved"))}</th><th>${escapeHtml(foundationT("app.inline.target"))}</th><th>${escapeHtml(foundationT("payments.updated"))}</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.noStockRows"))}</td></tr>`
      : rows.map((row) => `<tr>
          <td>${escapeHtml(row.locationName)}</td>
          <td><strong>${escapeHtml(row.skuCode || uiText("Unknown SKU"))}</strong><span class="muted-cell">${escapeHtml(row.productName || "")}</span></td>
          <td>${escapeHtml(row.availableQty)}</td>
          <td>${escapeHtml(Number(row.reservedInWarehouseQty || 0) + Number(row.reservedWithRepQty || 0))}</td>
          <td>${escapeHtml(row.targetQty ?? "-")}</td>
          <td>${escapeHtml(formatDateTime(row.lastUpdated))}</td>
        </tr>`).join("")}</tbody></table>${renderReportPager("stock", result, loadStockReport)}`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadOperationsReport() {
  const target = document.getElementById("report-operations");
  try {
    const result = await request(`/api/v1/reports/operations?${reportListParams("operations", reportPageState.operations)}`);
    const rows = result.items || [];
    reportOperationRows = rows;
    target.innerHTML = `<table><thead><tr><th>${escapeHtml(foundationT("payments.operation"))}</th><th>${escapeHtml(foundationT("payments.type"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("supply.qty"))}</th><th>${escapeHtml(foundationT("payments.total"))}</th><th>${escapeHtml(foundationT("app.created"))}</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.noOperations"))}</td></tr>`
      : rows.slice(0, 12).map((row) => `<tr><td>${escapeHtml(row.operationNumber)}</td><td>${escapeHtml(operationTypeLabel(row.operationType))}</td><td>${escapeHtml(uiText(row.status))}</td><td>${escapeHtml(row.quantity)}</td><td>${escapeHtml(formatMoney(row.total))}</td><td>${escapeHtml(formatDateTime(row.createdAt))}</td></tr>`).join("")}</tbody></table>${renderReportPager("operations", result, loadOperationsReport)}`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadPaymentsReport() {
  const target = document.getElementById("report-payments");
  try {
    const result = await request(`/api/v1/reports/payments?page=${reportPageState.payments}&pageSize=50`);
    const rows = result.items || [];
    reportPaymentRows = rows;
    target.innerHTML = `<table><thead><tr><th>${escapeHtml(foundationT("payments.payment"))}</th><th>${escapeHtml(foundationT("payments.operation"))}</th><th>${escapeHtml(foundationT("payments.method"))}</th><th>${escapeHtml(foundationT("payments.total"))}</th><th>${escapeHtml(foundationT("payments.paid"))}</th><th>${escapeHtml(foundationT("payments.remaining"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="7">${escapeHtml(foundationT("app.inline.noPaymentLogs"))}</td></tr>`
      : rows.slice(0, 12).map((row) => `<tr><td>${escapeHtml(paymentReference(row))}</td><td>${escapeHtml(operationReference(row))}</td><td>${escapeHtml(movementMethodLabel(row.paymentMethod))}</td><td>${escapeHtml(formatMoney(row.totalAmount))}</td><td>${escapeHtml(formatMoney(row.amountPaid))}</td><td>${escapeHtml(formatMoney(row.remainingAmount))}</td><td>${escapeHtml(paymentWorkflowStatusLabel(row.status))}</td></tr>`).join("")}</tbody></table>${renderReportPager("payments", result, loadPaymentsReport)}`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

function renderWearCycle(cycle, duration) {
  if (!cycle) return `<span class="status-pill status-warn">${escapeHtml(foundationT("app.inline.needsSetup"))}</span>`;
  if (cycle === "NotApplicable") return `<span class="status-pill status-muted">${escapeHtml(foundationT("app.inline.notApplicable"))}</span>`;
  return `<span class="status-pill status-ok">${escapeHtml(uiText(cycle))}</span>${duration ? `<span class="muted-cell"> ${escapeHtml(duration)}</span>` : ""}`;
}

async function loadSupplyReport() {
  const target = document.getElementById("report-supply");
  try {
    const result = await request(`/api/v1/reports/supply?${reportListParams("supply", reportPageState.supply)}`);
    const rows = result.items || [];
    reportSupplyRows = rows;
    target.innerHTML = `<table><thead><tr><th>${escapeHtml(foundationT("supply.shipment"))}</th><th>${escapeHtml(foundationT("supply.supplier"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("supply.qty"))}</th><th>${escapeHtml(foundationT("app.inline.landed"))}</th><th>${escapeHtml(foundationT("app.inline.receipt"))}</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.noSupplyShipments"))}</td></tr>`
      : rows.slice(0, 12).map((row) => `<tr><td>${escapeHtml(row.shipmentNumber)}<span class="muted-cell">${escapeHtml(row.invoiceNumber || "-")}</span></td><td dir="auto">${escapeHtml(row.supplierName)}</td><td>${escapeHtml(supplyStatusLabel(row.status))}</td><td>${escapeHtml(row.quantity)}</td><td>${escapeHtml(formatMoney(row.landedTotal))}</td><td>${escapeHtml(row.inventoryReceiptOperationNumber || (row.inventoryReceiptOperationId ? shortId(row.inventoryReceiptOperationId, "OP") : "-"))}</td></tr>`).join("")}</tbody></table>${renderReportPager("supply", result, loadSupplyReport)}`;
  } catch (exception) {
    reportSupplyRows = [];
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function loadMerchantBalancesReport() {
  const target = document.getElementById("report-balances");
  try {
    const result = await request(`/api/v1/reports/merchant-balances?page=${reportPageState.merchantBalances}&pageSize=50`);
    const rows = result.items || [];
    reportMerchantRows = rows;
    target.innerHTML = `<table><thead><tr><th>${escapeHtml(foundationT("customer.merchant"))}</th><th>${escapeHtml(foundationT("payments.remaining"))}</th><th>${escapeHtml(foundationT("app.inline.sales"))}</th><th>${escapeHtml(foundationT("payments.netCollected"))}</th><th>${escapeHtml(foundationT("app.inline.accountAdjustments"))}</th></tr></thead><tbody>${rows.length === 0
      ? `<tr><td colspan="5">${escapeHtml(foundationT("app.inline.noMerchantRemaining"))}</td></tr>`
      : rows.slice(0, 12).map((row) => {
        const corrections = Number(row.acceptedReturnValue || 0) + Number(row.additionalCharges || 0) - Number(row.amountReductions || 0);
        return `<tr><td>${escapeHtml(row.businessName)}</td><td>${escapeHtml(formatMoney(row.remainingOwed))}</td><td>${escapeHtml(formatMoney(row.totalSales))}</td><td>${escapeHtml(formatMoney(row.netCollected))}</td><td>${escapeHtml(formatMoney(corrections))}</td></tr>`;
      }).join("")}</tbody></table>${renderReportPager("merchantBalances", result, loadMerchantBalancesReport)}`;
  } catch (exception) {
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

function renderReportPager(key, result, loader) {
  const page = Number(result.page || 1);
  const totalPages = Math.max(1, Number(result.totalPages || 1));
  queueMicrotask(() => {
    document.querySelectorAll(`[data-report-page="${key}"]`).forEach((button) => button.addEventListener("click", () => {
      const next = page + Number(button.dataset.delta || 0);
      if (next < 1 || next > totalPages) return;
      reportPageState[key] = next;
      void loader();
    }));
  });
  return `<div class="table-pager" aria-label="${escapeHtml(foundationT("reports.pagination"))}"><button class="button secondary table-action" type="button" data-report-page="${escapeHtml(key)}" data-delta="-1" ${page <= 1 ? "disabled" : ""}>${escapeHtml(foundationT("pagination.previous"))}</button><span>${escapeHtml(foundationT("pagination.pageOf", { page, pages: totalPages }))}</span><button class="button secondary table-action" type="button" data-report-page="${escapeHtml(key)}" data-delta="1" ${page >= totalPages ? "disabled" : ""}>${escapeHtml(foundationT("pagination.next"))}</button></div>`;
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
  setupReportSearchPicker("operation-bill", reportOperationRows, (row) => row.id, (row) => `${row.operationNumber} / ${operationTypeLabel(row.operationType)} / ${row.status} / ${row.clientName || "-"}`);
  setupReportSearchPicker("payment-receipt", reportPaymentRows, (row) => row.id, (row) => `${paymentReference(row)} / ${movementMethodLabel(row.paymentMethod)} / ${formatMoney(row.remainingAmount)} remaining`);
  setupReportSearchPicker("cash-receipt", reportPaymentRows.filter((row) => row.paymentMethod === "CashHandToHand"), (row) => row.id, (row) => `${paymentReference(row)} / ${row.status} / ${formatMoney(row.totalAmount)}`);
  setupReportSearchPicker("supply-landed-cost", reportSupplyRows, (row) => row.id, (row) => `${row.shipmentNumber} / ${row.supplierName} / ${row.invoiceNumber || "-"} / ${formatMoney(row.landedTotal)}`);
  setupReportSearchPicker("merchant-statement", reportMerchantRows, (row) => row.merchantId, (row) => `${row.businessName} / ${formatMoney(row.balance)}`);
  setupReportSearchPicker("stocktake-summary", reportStocktakeRows, (row) => row.id, (row) => `${stocktakeReference(row)} / ${row.status}`);
}

function setReportSelect(id, rows, valueSelector, labelSelector) {
  const select = document.getElementById(id);
  if (!select) {
    return;
  }
  select.innerHTML = rows.length === 0
    ? `<option value="">${escapeHtml(foundationT("app.inline.noRowsAvailable"))}</option>`
    : `<option value="">${escapeHtml(foundationT("app.inline.select"))}</option>${rows.map((row) => `<option value="${escapeHtml(valueSelector(row))}">${escapeHtml(labelSelector(row))}</option>`).join("")}`;
}

function renderReportSearchPicker(reportType, label, placeholder, buttonLabel) {
  const id = `report-picker-${reportType}`;
  const formats = reportCatalogEntry(reportType)?.formats || ["pdf"];
  const actions = formats.map((format) => `<button class="button secondary" type="button" data-pdf-report="${escapeHtml(reportType)}" data-export-format="${escapeHtml(format)}">${escapeHtml(format.toUpperCase())}</button>`).join("");
  return `<div class="field report-search-field">
    <label for="${id}-search">${escapeHtml(label)}</label>
    <input id="${id}-value" type="hidden">
    <input id="${id}-search" class="input report-picker-search" data-report-picker="${escapeHtml(reportType)}" type="search" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="${id}-results" placeholder="${escapeHtml(placeholder)}">
    <div id="${id}-results" class="op-line-search-results report-picker-results" role="listbox" hidden></div>
    <div class="report-format-actions" aria-label="${escapeHtml(buttonLabel)}">${actions}</div>
  </div>`;
}

async function loadReportLocations() {
  const select = document.getElementById("report-filter-location");
  if (!select) return;
  try {
    if (select.dataset.loaded === "true") return;
    const result = await request("/api/v1/inventory/locations?pageSize=200");
    const rows = Array.isArray(result) ? result : (result.items || []);
    select.insertAdjacentHTML("beforeend", rows.map((row) => `<option value="${escapeHtml(row.id)}">${escapeHtml(row.name)}</option>`).join(""));
    select.dataset.loaded = "true";
  } catch {
    select.disabled = true;
  }
}

function bindTransactionReferenceField(methodId, referenceId) {
  const method = document.getElementById(methodId);
  const reference = document.getElementById(referenceId);
  if (!method || !reference) return;
  const sync = () => {
    const required = ["CashTransaction", "BankTransfer", "Wallet"].includes(canonicalSelectValue(methodId, "movementMethod", { allowEmpty: true }));
    reference.required = required;
    reference.closest(".field")?.toggleAttribute("hidden", !required);
    if (!required) reference.value = "";
  };
  method.addEventListener("change", sync);
  sync();
}

function reportExportFilterParams(reportName) {
  const params = new URLSearchParams();
  const value = (id) => document.getElementById(id)?.value?.trim() || "";
  if (reportName === "stock" && value("report-filter-location")) params.set("locationId", value("report-filter-location"));
  if (reportName === "operations") {
    if (value("report-filter-from")) params.set("from", value("report-filter-from"));
    if (value("report-filter-to")) params.set("to", value("report-filter-to"));
    if (value("report-filter-operation-type")) params.set("operationType", value("report-filter-operation-type"));
    if (value("report-filter-sort")) params.set("sortBy", value("report-filter-sort"));
    if (value("report-filter-direction")) params.set("sortDirection", value("report-filter-direction"));
  }
  if (reportName === "supply") {
    if (value("report-filter-from")) params.set("from", value("report-filter-from"));
    if (value("report-filter-to")) params.set("to", value("report-filter-to"));
    if (value("report-filter-supply-status")) params.set("status", value("report-filter-supply-status"));
  }
  return params;
}

function reportListParams(reportName, page) {
  const params = reportExportFilterParams(reportName);
  params.set("page", String(page));
  params.set("pageSize", "50");
  return params;
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
      ? `<button class="op-line-search-result" type="button" disabled>${escapeHtml(foundationT("app.inline.noMatchingRecords"))}</button>`
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
  results.replaceChildren();
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
    count.textContent = foundationT("payments.loggedCount", { count: result.totalCount });
    target.innerHTML = `<table><thead><tr><th>${escapeHtml(foundationT("app.inline.report"))}</th><th>${escapeHtml(foundationT("app.inline.requestedBy"))}</th><th>${escapeHtml(foundationT("app.created"))}</th></tr></thead><tbody>${result.items.length === 0
      ? `<tr><td colspan="3">${escapeHtml(foundationT("app.inline.noExportLogsYet"))}</td></tr>`
      : result.items.map((row) => `<tr><td>${escapeHtml(uiText(row.reportType))}</td><td>${escapeHtml(row.requestedByRole ? roleLabel(row.requestedByRole) : foundationT("app.inline.system"))}</td><td>${escapeHtml(formatDateTime(row.createdAt))}</td></tr>`).join("")}</tbody></table>`;
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    target.textContent = getFriendlyWorkspaceError(exception);
  }
}

async function logReportExport(reportType) {
  try {
    await request("/api/v1/reports/exports", { method: "POST", body: JSON.stringify({ reportType }) });
    notice(foundationT("app.message.exportIntentLogged"), "success");
    await loadExportLogs();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function downloadReport(reportName, format = "csv", trigger = null) {
  try {
    const language = getReportExportLanguage();
    updateExportDocket({ title: reportName, format, status: "Preparing the authorized export…" });
    if (trigger) trigger.disabled = true;
    const params = reportExportFilterParams(reportName);
    params.set("format", format);
    params.set("language", language);
    const fileName = await downloadFile(`/api/v1/reports/${encodeURIComponent(reportName)}/export?${params}`, `lensee-${language}-${reportName}.${format}`);
    updateExportDocket({ title: reportName, format, fileName, status: "Export completed and downloaded." });
    notice(foundationT("app.message.reportDownloaded"), "success");
    await loadExportLogs();
  } catch (exception) {
    updateExportDocket({ title: reportName, format, status: getFriendlyWorkspaceError(exception) });
    notice(getFriendlyWorkspaceError(exception), "error");
  } finally {
    if (trigger) trigger.disabled = false;
  }
}

async function downloadReportPdf(reportType, format = "pdf", trigger = null) {
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
    notice(foundationT("app.message.selectADocumentRowBeforeDownloading"), "error");
    return;
  }

  try {
    if (trigger) trigger.disabled = true;
    const language = getReportExportLanguage();
    updateExportDocket({ title: reportType, format, status: "Rendering the official document…" });
    const fileName = await downloadFile(`/api/v1/documents/${encodeURIComponent(reportType)}/${encodeURIComponent(id.trim())}?format=${encodeURIComponent(format)}&language=${encodeURIComponent(language)}`, `lensee-${language}-${reportType}.${format}`);
    updateExportDocket({ title: reportType, format, fileName, status: "Official document downloaded." });
    notice(`${format.toUpperCase()} downloaded.`, "success");
    await loadExportLogs();
  } catch (exception) {
    updateExportDocket({ title: reportType, format, status: getFriendlyWorkspaceError(exception) });
    notice(getFriendlyWorkspaceError(exception), "error");
  } finally {
    if (trigger) trigger.disabled = false;
  }
}

function getReportExportLanguage() {
  const value = document.querySelector('input[name="report-export-language"]:checked')?.value;
  return ["ar", "en", "bi"].includes(value) ? value : "ar";
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
  const query = new URLSearchParams({ language });
  if (reportType === "merchant-statement") {
    const from = document.getElementById("merchant-statement-from")?.value;
    const to = document.getElementById("merchant-statement-to")?.value;
    if (from) query.set("from", from);
    if (to) query.set("to", to);
  }
  await downloadFile(`${paths[reportType]}?${query.toString()}`, `lensee-${language}-${reportType}-${code}.pdf`);
}

function getReportFileCode(reportType, id) {
  const cleanId = String(id || "").trim();
  if (reportType === "operation-bill") {
    return sanitizeFileCode(reportOperationRows.find((row) => row.id === cleanId)?.operationNumber || cleanId);
  }
  if (reportType === "payment-receipt" || reportType === "cash-receipt") {
    return sanitizeFileCode(paymentReference(reportPaymentRows.find((row) => row.id === cleanId)));
  }
  if (reportType === "merchant-statement") {
    return sanitizeFileCode(reportMerchantRows.find((row) => row.merchantId === cleanId)?.businessName || cleanId);
  }
  if (reportType === "supply-landed-cost") {
    return sanitizeFileCode(reportSupplyRows.find((row) => row.id === cleanId)?.shipmentNumber || cleanId);
  }
  if (reportType === "stocktake-summary") {
    return sanitizeFileCode(stocktakeReference(reportStocktakeRows.find((row) => row.id === cleanId)));
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
      notice(foundationT("app.message.pDFDownloaded"), "success");
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
    Draft: foundationT("supply.draft"),
    Received: foundationT("supply.received"),
    Cancelled: foundationT("supply.cancelled")
  };
  return labels[status] || status || "-";
}

function supplyCostTypeLabel(type) {
  const labels = {
    Customs: foundationT("supply.customs"),
    Freight: foundationT("supply.freight"),
    Clearance: foundationT("supply.clearance"),
    Handling: foundationT("supply.handling"),
    Insurance: foundationT("supply.insurance"),
    Other: foundationT("supply.other")
  };
  return labels[type] || type || "-";
}

async function hydrateSupplySkus() {
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
      eyebrow: foundationT("supply.eyebrow"),
      title: foundationT("supply.title"),
      body: foundationT("supply.subtitle"),
      metrics: `
        ${scenarioCard(foundationT("supply.shipments"), foundationT("supply.loading"), "status-muted", "supply-count")}
        ${scenarioCard(foundationT("supply.draftValue"), foundationT("supply.loading"), "status-muted", "supply-draft-total")}
        ${scenarioCard(foundationT("supply.readyToConfirm"), foundationT("supply.loading"), "status-muted", "supply-ready-count")}
        ${scenarioCard(foundationT("supply.access"), canWrite ? foundationT("supply.admin") : foundationT("supply.readOnly"), canWrite ? "status-ok" : "status-muted")}
      `
    })}
    <section class="supply-workspace">
      <main class="supply-main-pane">
        ${canWrite ? renderSupplyForm() : ""}
        <section class="supply-list-pane">
        <section class="band compact-band">
          <div class="section-head tight-head">
            <div><h2>${foundationT("supply.shipments")}</h2><p class="muted-text">${foundationT("supply.searchHelp")}</p></div>
            <button id="supply-refresh" class="button secondary" type="button">${foundationT("supply.refresh")}</button>
          </div>
          <div class="toolbar">
            <input id="supply-search" class="input" autocomplete="off" placeholder="${foundationT("supply.searchPlaceholder")}">
            <select id="supply-status" class="select compact-select">
              <option value="">${foundationT("supply.allStatuses")}</option>
              <option value="Draft">${foundationT("supply.draft")}</option>
              <option value="Received">${foundationT("supply.received")}</option>
              <option value="Cancelled">${foundationT("supply.cancelled")}</option>
            </select>
          </div>
          <div class="table-wrap compact-table">
            <table><thead><tr><th>${foundationT("supply.shipment")}</th><th>${foundationT("supply.supplier")}</th><th>${foundationT("supply.status")}</th><th>${foundationT("supply.total")}</th><th></th></tr></thead><tbody id="supply-rows"></tbody></table>
          </div>
          <div id="supply-list-pagination" class="pagination" hidden></div>
        </section>
        </section>
        <section class="band supply-detail-pane" id="supply-detail">
          <h2>${foundationT("supply.detail")}</h2>
          <p class="muted-text">${foundationT("supply.detailHelp")}</p>
        </section>
      </main>
    </section>`;

  document.getElementById("supply-refresh").addEventListener("click", loadSupplyShipments);
  document.getElementById("supply-search").addEventListener("input", debounce(() => { supplyListPage = 1; void loadSupplyShipments(); }, 250));
  document.getElementById("supply-status").addEventListener("change", () => { supplyListPage = 1; void loadSupplyShipments(); });
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
          <span class="supply-form-badge">${foundationT("supply.receipt")}</span>
          <h2>${foundationT("supply.register")}</h2>
          <p class="muted-text">${foundationT("supply.formHelp")}</p>
        </div>
        <button id="supply-reset" class="button secondary" type="button">${foundationT("supply.newShipment")}</button>
      </div>
      <form id="supply-form" class="form wide-form compact-form supply-receipt-form">
        <div class="form-error" id="supply-form-error" hidden></div>
        <div class="supply-validation-list full-span" id="supply-validation-list" hidden></div>
        <input id="supply-id" type="hidden">
        <section class="supply-document-block supply-header-block full-span">
          <div class="supply-block-title"><span>${foundationT("supply.shipmentData")}</span><strong>${foundationT("supply.receiptDraft")}</strong></div>
          <div class="supply-header-grid">
            <div class="field"><label for="supply-supplier">${foundationT("supply.supplier")}</label><input id="supply-supplier" class="input" maxlength="255" required></div>
            <div class="field"><label for="supply-invoice">${foundationT("supply.invoiceNumber")}</label><input id="supply-invoice" class="input" maxlength="100"></div>
            <div class="field"><label for="supply-date">${foundationT("supply.shipmentDate")}</label><input id="supply-date" class="input" type="datetime-local"></div>
            <div class="field"><label for="supply-location">${foundationT("supply.destinationWarehouse")}</label><select id="supply-location" class="select">${inventoryLocations.filter((location) => location.isActive).map((location) => `<option value="${escapeHtml(location.id)}" ${location.id === mainWarehouse?.id ? "selected" : ""}>${escapeHtml(location.name)}</option>`).join("")}</select></div>
            <div class="field full-span"><label for="supply-notes">${foundationT("supply.notes")}</label><textarea id="supply-notes" class="input" rows="2" maxlength="4000"></textarea></div>
          </div>
        </section>
        <section class="operation-line-panel supply-document-block full-span">
          <div class="section-head tight-head"><div><h2>${foundationT("supply.skuLines")}</h2><p class="muted-text">${foundationT("supply.priceHelp")}</p></div><button id="supply-add-line" class="button secondary" type="button">${foundationT("supply.addLine")}</button></div>
          <div id="supply-lines" class="line-editor"></div>
          <div id="supply-line-pagination" class="pagination" hidden></div>
        </section>
        <section class="operation-line-panel supply-document-block full-span">
          <div class="section-head tight-head"><div><h2>${foundationT("supply.costBreakdown")}</h2></div><button id="supply-add-cost" class="button secondary" type="button">${foundationT("supply.addCost")}</button></div>
          <div id="supply-costs" class="line-editor"></div>
        </section>
        <section class="supply-summary-panel full-span" id="supply-summary-panel">
          <div><span>${foundationT("supply.productSubtotal")}</span><strong id="supply-form-product-total">0.00</strong></div>
          <div><span>${foundationT("supply.importCosts")}</span><strong id="supply-form-cost-total">0.00</strong></div>
          <div><span>${foundationT("supply.landedTotal")}</span><strong id="supply-form-landed-total">0.00</strong></div>
          <div><span>${foundationT("supply.confirmationReadiness")}</span><strong id="supply-form-readiness" class="status-warn">${foundationT("supply.incompletePrices")}</strong></div>
        </section>
        <div class="form-actions full-span">
          <button class="button primary" type="submit">${foundationT("supply.saveDraft")}</button>
        </div>
      </form>
    </section>`;
}

function wireSupplyForm() {
  document.getElementById("supply-form").addEventListener("submit", saveSupplyShipment);
  document.getElementById("supply-reset").addEventListener("click", resetSupplyForm);
  document.getElementById("supply-add-line").addEventListener("click", () => addSupplyLine());
  document.getElementById("supply-add-cost").addEventListener("click", () => addSupplyCost());
  document.getElementById("supply-form").addEventListener("input", handleSupplyFormChange);
  document.getElementById("supply-form").addEventListener("change", handleSupplyFormChange);
  resetSupplyForm();
}

function resetSupplyForm() {
  const form = document.getElementById("supply-form");
  if (!form) {
    return;
  }
  form.reset();
  document.getElementById("supply-id").value = "";
  document.getElementById("supply-lines")?.replaceChildren();
  supplyEditorLines = [];
  supplyEditorLineById.clear();
  supplyEditorPage = 1;
  document.getElementById("supply-costs")?.replaceChildren();
  document.getElementById("supply-validation-list").hidden = true;
  document.getElementById("supply-form-error").hidden = true;
  addSupplyLine();
  addSupplyCost({ costType: "Customs" });
  updateSupplyFormSummary();
}

function addSupplyLine(line = {}, target = null) {
  const container = document.getElementById("supply-lines");
  if (!container) {
    return;
  }
  const model = {
    _clientId: line._clientId || createUuid(),
    skuId: line.skuId || "",
    quantity: line.quantity || 1,
    unitPrice: line.unitPrice ?? null,
    lotNumber: line.lotNumber || null,
    expiryDate: line.expiryDate || null,
    notes: line.notes || null,
    skuCode: line.skuCode || null,
    productName: line.productName || null
  };
  if (!line._fromModel) {
    syncCurrentSupplyPage();
    supplyEditorLines.push(model);
    supplyEditorLineById.set(model._clientId, model);
    supplyEditorStatsDirty = true;
    supplyEditorPage = Math.max(1, Math.ceil(supplyEditorLines.length / supplyEditorPageSize));
    renderSupplyEditorPage();
    return;
  }
  const unitPriceValue = model.unitPrice ?? "";
  const row = document.createElement("div");
  row.className = "line-editor-row supply-line-row";
  row.dataset.supplyLineKey = model._clientId;
  row.innerHTML = `
    <input class="supply-line-sku" type="hidden" value="${escapeHtml(model.skuId)}">
    <div class="field op-line-finder"><label>${foundationT("supply.findSKU")}</label><input class="input supply-line-search" autocomplete="off" placeholder="${foundationT("supply.productColorPowerSKUCode")}"><div class="op-line-search-results" hidden></div></div>
    <div class="op-line-resolved full-span"><span class="muted-text">${foundationT("supply.searchAndSelectASKU")}</span></div>
    <div class="field"><label>${foundationT("supply.quantity")}</label><input class="input supply-line-qty" type="number" min="1" step="1" value="${escapeHtml(model.quantity)}" required></div>
    <div class="field"><label>${foundationT("supply.unitPrice")}</label><input class="input supply-line-price" type="number" min="0.01" step="0.01" value="${escapeHtml(unitPriceValue)}" placeholder="${foundationT("supply.draftBlank")}"><span class="field-hint supply-price-hint" hidden>${foundationT("supply.requiredBeforeConfirmation")}</span></div>
    <div class="field"><label>${foundationT("supply.lot")}</label><input class="input supply-line-lot" maxlength="100" value="${escapeHtml(model.lotNumber || "")}"></div>
    <div class="field"><label>${foundationT("supply.expiry")}</label><input class="input supply-line-expiry" type="date" value="${escapeHtml(model.expiryDate || "")}"></div>
    <div class="field full-span"><label>${foundationT("supply.lineNotes")}</label><input class="input supply-line-notes" maxlength="1000" value="${escapeHtml(model.notes || "")}"></div>
    <button class="icon-button supply-remove-line" type="button" title="${foundationT("supply.removeLine")}">x</button>`;
  row.querySelector(".supply-line-search").addEventListener("input", debounce(() => { void renderSupplySkuSearchResults(row); }, 250));
  row.querySelector(".supply-line-price").addEventListener("input", () => updateSupplyLinePriceState(row, false));
  row.querySelector(".supply-remove-line").addEventListener("click", () => {
    syncCurrentSupplyPage();
    if (supplyEditorLines.length > 1) {
      supplyEditorLines = supplyEditorLines.filter((item) => item._clientId !== model._clientId);
      supplyEditorLineById.delete(model._clientId);
      supplyEditorStatsDirty = true;
      supplyEditorPage = Math.min(supplyEditorPage, Math.max(1, Math.ceil(supplyEditorLines.length / supplyEditorPageSize)));
      renderSupplyEditorPage();
    }
  });
  (target || container).appendChild(row);
  if (model.skuId) {
    seedSupplyLineSkuSelection(row, model.skuId);
  }
  updateSupplyLinePriceState(row, false);
}

function readSupplyLineRow(row) {
  const priceValue = row.querySelector(".supply-line-price").value.trim();
  const existing = supplyEditorLineById.get(row.dataset.supplyLineKey);
  return {
    _clientId: row.dataset.supplyLineKey,
    skuId: row.querySelector(".supply-line-sku").value,
    quantity: Number(row.querySelector(".supply-line-qty").value || 0),
    unitPrice: priceValue === "" ? null : Number(priceValue),
    lotNumber: row.querySelector(".supply-line-lot").value.trim() || null,
    expiryDate: row.querySelector(".supply-line-expiry").value || null,
    notes: row.querySelector(".supply-line-notes").value.trim() || null,
    skuCode: existing?.skuCode || null,
    productName: existing?.productName || null
  };
}

function syncCurrentSupplyPage() {
  document.querySelectorAll(".supply-line-row").forEach((row) => {
    syncSupplyLineRow(row);
  });
}

function syncSupplyLineRow(row) {
  const existing = supplyEditorLineById.get(row.dataset.supplyLineKey);
  if (!existing) return;
  Object.assign(existing, readSupplyLineRow(row));
}

function supplyLineMetrics(line) {
  const quantity = Number(line?.quantity);
  const unitPrice = line?.unitPrice;
  return {
    productTotal: Number.isFinite(quantity) && Number.isFinite(unitPrice) ? quantity * unitPrice : 0,
    incompletePrices: unitPrice === null ? 1 : 0,
    invalidPrices: unitPrice !== null && (!Number.isFinite(unitPrice) || unitPrice <= 0) ? 1 : 0
  };
}

function rebuildSupplyEditorStats() {
  supplyEditorStats = supplyEditorLines.reduce((total, line) => {
    const metrics = supplyLineMetrics(line);
    total.productTotal += metrics.productTotal;
    total.incompletePrices += metrics.incompletePrices;
    total.invalidPrices += metrics.invalidPrices;
    return total;
  }, { productTotal: 0, incompletePrices: 0, invalidPrices: 0 });
  supplyEditorStatsDirty = false;
}

function handleSupplyFormChange(event) {
  const row = event.target.closest?.(".supply-line-row");
  if (row) {
    const existing = supplyEditorLineById.get(row.dataset.supplyLineKey);
    const before = supplyLineMetrics(existing);
    syncSupplyLineRow(row);
    const after = supplyLineMetrics(existing);
    supplyEditorStats.productTotal += after.productTotal - before.productTotal;
    supplyEditorStats.incompletePrices += after.incompletePrices - before.incompletePrices;
    supplyEditorStats.invalidPrices += after.invalidPrices - before.invalidPrices;
  }
  updateSupplyFormSummary();
}

function renderSupplyEditorPage() {
  const container = document.getElementById("supply-lines");
  if (!container) return;
  if (supplyEditorStatsDirty) rebuildSupplyEditorStats();
  container.replaceChildren();
  const start = (supplyEditorPage - 1) * supplyEditorPageSize;
  const fragment = document.createDocumentFragment();
  supplyEditorLines.slice(start, start + supplyEditorPageSize)
    .forEach((line) => addSupplyLine({ ...line, _fromModel: true }, fragment));
  container.replaceChildren(fragment);
  updateSupplyFormSummary();
  const pager = document.getElementById("supply-line-pagination");
  if (!pager) return;
  const pages = Math.max(1, Math.ceil(supplyEditorLines.length / supplyEditorPageSize));
  pager.hidden = pages <= 1;
  const summary = foundationT("supply.lineRange", { start: start + 1, end: Math.min(start + supplyEditorPageSize, supplyEditorLines.length), total: supplyEditorLines.length });
  setPagerContents(pager, foundationT("supply.previous"), summary, foundationT("supply.next"), supplyEditorPage <= 1, supplyEditorPage >= pages, (delta) => {
    syncCurrentSupplyPage();
    supplyEditorPage += delta;
    renderSupplyEditorPage();
  });
}

function updateSupplyLinePriceState(row, refreshSummary = true) {
  const priceInput = row.querySelector(".supply-line-price");
  const hint = row.querySelector(".supply-price-hint");
  const isBlank = priceInput.value.trim() === "";
  const value = Number(priceInput.value);
  const isInvalid = !isBlank && (!Number.isFinite(value) || value <= 0);
  row.classList.toggle("supply-line-incomplete", isBlank);
  row.classList.toggle("supply-line-invalid", isInvalid);
  if (hint) {
    hint.hidden = !isBlank && !isInvalid;
    hint.textContent = isInvalid ? foundationT("supply.priceMustBeGreaterThanZero") : foundationT("supply.requiredBeforeConfirmation");
  }
  if (refreshSummary) updateSupplyFormSummary();
}

async function renderSupplySkuSearchResults(row) {
  const input = row.querySelector(".supply-line-search");
  const results = row.querySelector(".op-line-search-results");
  const terms = input.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) {
    results.hidden = true;
    results.replaceChildren();
    return;
  }

  const query = terms.join(" ");
  const requestId = (skuSearchRequests.get(row) || 0) + 1;
  skuSearchRequests.set(row, requestId);
  let matches;
  try {
    matches = (await searchSkuOptions(query, 20)).slice(0, 8);
  } catch {
    matches = [];
  }
  if (skuSearchRequests.get(row) !== requestId || input.value.trim().toLowerCase() !== query) return;

  setupAdaptiveSearchResultDismissal();
  collapseAdaptiveSearchResults(results);
  results.hidden = false;
  results.innerHTML = matches.length === 0
    ? `<button type="button" class="op-line-search-result" disabled>${foundationT("supply.noResults")}</button>`
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
    results.replaceChildren();
      updateSupplyFormSummary();
    });
  });
}

function seedSupplyLineSkuSelection(row, skuId) {
  const model = supplyEditorLineById.get(row.dataset.supplyLineKey);
  const sku = operationSkuOptions.find((value) => value.id === skuId) ||
    (model?.skuCode ? { id: skuId, skuCode: model.skuCode, productName: model.productName || "" } : null);
  row.querySelector(".supply-line-sku").value = skuId || "";
  row.querySelector(".op-line-resolved").innerHTML = sku
    ? `<span class="status-pill status-ok">${foundationT("supply.selectedSKU")}</span><strong>${escapeHtml(sku.skuCode)}</strong><span class="muted-cell">${escapeHtml(sku.productName)}</span>`
    : `<span class="status-pill status-warn">${foundationT("supply.unknownSKU")}</span><span class="muted-cell">${escapeHtml(shortId(skuId, "SKU"))}</span>`;
  if (!sku && skuId) {
    void ensureSkuOption(skuId).then((loaded) => {
      if (loaded && row.isConnected) seedSupplyLineSkuSelection(row, skuId);
    });
  }
}

function addSupplyCost(cost = {}) {
  const container = document.getElementById("supply-costs");
  if (!container) {
    return;
  }
  const row = document.createElement("div");
  row.className = "line-editor-row supply-cost-row";
  row.innerHTML = `
    <div class="field"><label>${foundationT("supply.costType")}</label><select class="select supply-cost-type">
      ${["Customs", "Freight", "Clearance", "Handling", "Insurance", "Other"].map((item) => `<option value="${escapeHtml(item)}">${escapeHtml(supplyCostTypeLabel(item))}</option>`).join("")}
    </select></div>
    <div class="field"><label>${foundationT("supply.description")}</label><input class="input supply-cost-description" maxlength="255" value="${escapeHtml(cost.description || "")}"></div>
    <div class="field"><label>${foundationT("supply.amount")}</label><input class="input supply-cost-amount" type="number" min="0" step="0.01" value="${escapeHtml(cost.amount || 0)}"></div>
    <button class="icon-button supply-remove-cost" type="button" title="${foundationT("supply.removeCost")}">x</button>`;
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
  tbody.innerHTML = `<tr><td colspan="5">${foundationT("supply.loadingShipments")}</td></tr>`;
  const params = new URLSearchParams();
  params.set("paged", "true");
  params.set("page", String(supplyListPage));
  params.set("pageSize", "25");
  const search = document.getElementById("supply-search")?.value.trim();
  const status = document.getElementById("supply-status")?.value;
  if (search) params.set("search", search);
  if (status) params.set("status", status);
  try {
    const result = await request(`/api/v1/supply/shipments?${params}`);
    const rows = result.items || [];
    supplyShipments = rows;
    updateSupplyPageMetrics(rows);
    if (count) {
      count.textContent = foundationT(result.totalCount === 1 ? "app.count.shipment" : "app.count.shipments", { count: result.totalCount });
    }
    tbody.innerHTML = rows.length === 0 ? `<tr><td colspan="5">${foundationT("supply.noSupplyShipmentsMatchTheCurrentFilters")}</td></tr>` : rows.map((row) => `
      <tr class="click-row ${row.id === selectedSupplyShipmentId ? "selected-row" : ""}" data-supply-id="${escapeHtml(row.id)}">
        <td><strong>${escapeHtml(row.shipmentNumber)}</strong><span class="muted-cell">${escapeHtml(row.invoiceNumber || foundationT("supply.noInvoice"))}</span></td>
        <td>${escapeHtml(row.supplierName)}<span class="muted-cell">${escapeHtml(row.destinationLocationName || "-")} / ${escapeHtml(row.quantity || 0)} ${foundationT("supply.packs")}</span></td>
        <td><span class="status-pill ${row.status === "Received" ? "status-ok" : row.status === "Cancelled" ? "status-muted" : "status-warn"}">${escapeHtml(supplyStatusLabel(row.status))}</span></td>
        <td><strong>${escapeHtml(formatMoney(row.landedTotal))}</strong><span class="muted-cell">${escapeHtml(formatMoney(row.costSubtotal))} ${foundationT("supply.costs")}</span></td>
        <td><button class="button secondary table-action" type="button" data-supply-detail="${escapeHtml(row.id)}">${foundationT("supply.details")}</button></td>
      </tr>`).join("");
    tbody.querySelectorAll("[data-supply-detail], [data-supply-id]").forEach((element) => {
      element.addEventListener("click", () => showSupplyDetail(element.dataset.supplyDetail || element.dataset.supplyId));
    });
    const pager = document.getElementById("supply-list-pagination");
    if (pager) {
      const pages = Math.max(1, result.totalPages || Math.ceil(result.totalCount / result.pageSize));
      supplyListPage = Math.min(result.page, pages);
      pager.hidden = pages <= 1;
      setPagerContents(pager, foundationT("supply.previous"), `${supplyListPage} / ${pages}`, foundationT("supply.next"), supplyListPage <= 1, supplyListPage >= pages, (delta) => {
        supplyListPage += delta;
        void loadSupplyShipments();
      });
    }
  } catch (exception) {
    if (count) count.textContent = foundationT("supply.failed");
    updateSupplyPageMetrics([]);
    tbody.innerHTML = `<tr><td colspan="5">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function showSupplyDetail(id, linePage = 1) {
  selectedSupplyShipmentId = id;
  supplyDetailLinePage = linePage;
  const target = document.getElementById("supply-detail");
  const canWrite = getAuth()?.user.role === "Admin";
  target.innerHTML = `<h2>${foundationT("supply.detail")}</h2><p>${foundationT("supply.loadingShipment")}</p>`;
  try {
    const shipment = await request(`/api/v1/supply/shipments/${encodeURIComponent(id)}?includeCollections=false`);
    const [lineResult, costs, history] = await Promise.all([
      request(`/api/v1/supply/shipments/${encodeURIComponent(id)}/lines?page=${supplyDetailLinePage}&pageSize=50`),
      request(`/api/v1/supply/shipments/${encodeURIComponent(id)}/costs`),
      request(`/api/v1/supply/shipments/${encodeURIComponent(id)}/history`)
    ]);
    shipment.lines = lineResult.items || [];
    shipment.costs = costs || [];
    shipment.history = history || [];
    supplyCurrentDetail = shipment;
    const readiness = getSupplyShipmentReadiness(shipment);
    target.innerHTML = `
      <div class="section-head">
        <div><h2>${escapeHtml(shipment.shipmentNumber)}</h2><p class="muted-text">${escapeHtml(shipment.supplierName)} / ${escapeHtml(shipment.invoiceNumber || "-")}</p></div>
        <div class="inline-actions">
          <span class="status-pill ${shipment.status === "Received" ? "status-ok" : shipment.status === "Cancelled" ? "status-muted" : "status-warn"}">${escapeHtml(supplyStatusLabel(shipment.status))}</span>
          ${canWrite && shipment.status === "Draft" ? `<button class="button secondary" type="button" id="supply-edit">${foundationT("supply.edit")}</button><button class="button primary" type="button" id="supply-confirm" ${readiness.canConfirm ? "" : `disabled title="${escapeHtml(readiness.message)}"`}>${foundationT("supply.confirmReceipt")}</button><button class="button secondary" type="button" id="supply-cancel">${foundationT("supply.cancel")}</button>` : ""}
          ${shipment.inventoryReceiptOperationId ? `<button class="button secondary" type="button" data-print-report="operation-bill" data-print-id="${escapeHtml(shipment.inventoryReceiptOperationId)}" data-print-code="${escapeHtml(shipment.shipmentNumber)}">${foundationT("supply.printReceipt")}</button>` : ""}
        </div>
      </div>
      ${shipment.status === "Draft" && !readiness.canConfirm ? `<p class="form-error inline-warning">${escapeHtml(readiness.message)}</p>` : ""}
      ${shipment.notes ? `<p class="muted-text"><strong>${foundationT("supply.notes")}:</strong> ${escapeHtml(shipment.notes)}</p>` : ""}
      <div class="detail-grid supply-readiness-grid">
        <div><span>${foundationT("supply.destinationWarehouse")}</span><strong>${escapeHtml(shipment.destinationLocationName || shortId(shipment.destinationLocationId, "LOC"))}</strong></div>
        <div><span>${foundationT("supply.shipmentDate")}</span><strong>${escapeHtml(formatDateTime(shipment.shipmentDate))}</strong></div>
        <div><span>${foundationT("supply.products")}</span><strong>${escapeHtml(formatMoney(shipment.productSubtotal))}</strong></div>
        <div><span>${foundationT("supply.importCosts")}</span><strong>${escapeHtml(formatMoney(shipment.costSubtotal))}</strong></div>
        <div><span>${foundationT("supply.landedTotal")}</span><strong>${escapeHtml(formatMoney(shipment.landedTotal))}</strong></div>
        <div><span>${foundationT("supply.readiness")}</span><strong class="${readiness.canConfirm ? "status-ok" : "status-warn"}">${escapeHtml(uiText(readiness.label))}</strong></div>
      </div>
      ${shipment.inventoryReceiptOperationId ? `<p class="muted-text">${foundationT("supply.inventoryReceiptOperation")}: <strong>${escapeHtml(shipment.inventoryReceiptOperationNumber || shortId(shipment.inventoryReceiptOperationId, "OP"))}</strong></p>` : ""}
      <h3>${foundationT("supply.lines")} <span class="muted-text">${escapeHtml(lineResult.totalCount)}</span></h3>
      <div class="table-wrap compact-table"><table><thead><tr><th>${escapeHtml(foundationT("app.sku"))}</th><th>${foundationT("supply.qty")}</th><th>${foundationT("supply.unitPrice")}</th><th>${foundationT("supply.line")}</th><th>${foundationT("supply.allocated")}</th><th>${foundationT("supply.landedUnit")}</th><th>${foundationT("supply.batch")}</th></tr></thead><tbody>${shipment.lines.map((line) => `
        <tr class="${line.unitPrice == null || line.unitPrice <= 0 ? "supply-line-incomplete-row" : ""}"><td><strong>${escapeHtml(line.skuCode)}</strong><span class="muted-cell">${escapeHtml(line.productName)}</span>${line.notes ? `<span class="muted-cell">${escapeHtml(line.notes)}</span>` : ""}</td><td>${escapeHtml(line.quantity)}</td><td>${line.unitPrice == null ? `<span class="status-pill status-warn">${foundationT("supply.blank")}</span>` : escapeHtml(formatMoney(line.unitPrice))}</td><td>${escapeHtml(formatMoney(line.lineSubtotal))}</td><td>${escapeHtml(formatMoney(line.allocatedCost))}</td><td>${escapeHtml(formatMoney(line.landedUnitCost))}</td><td>${escapeHtml(line.lotNumber || "-")} / ${escapeHtml(line.expiryDate || "-")}</td></tr>`).join("")}</tbody></table></div>
      <h3>${foundationT("supply.costBreakdown2")}</h3>
      <div class="table-wrap compact-table"><table><thead><tr><th>${foundationT("supply.type")}</th><th>${foundationT("supply.description")}</th><th>${foundationT("supply.amount")}</th></tr></thead><tbody>${shipment.costs.length === 0 ? `<tr><td colspan="3">${foundationT("supply.noCosts")}</td></tr>` : shipment.costs.map((cost) => `<tr><td>${escapeHtml(supplyCostTypeLabel(cost.costType))}</td><td>${escapeHtml(cost.description || "-")}</td><td>${escapeHtml(formatMoney(cost.amount))}</td></tr>`).join("")}</tbody></table></div>
      <h3>${foundationT("supply.history")}</h3>
      <div class="table-wrap compact-table"><table><thead><tr><th>${foundationT("supply.action")}</th><th>${foundationT("supply.time")}</th><th>${foundationT("supply.summary")}</th></tr></thead><tbody>${shipment.history.length === 0 ? `<tr><td colspan="3">${foundationT("supply.noHistory")}</td></tr>` : shipment.history.map((item) => `<tr><td>${escapeHtml(uiText(item.action))}</td><td>${escapeHtml(formatDateTime(item.createdAt))}</td><td>${escapeHtml(uiText(item.summary || "-"))}</td></tr>`).join("")}</tbody></table></div>
      <div class="pagination" id="supply-detail-line-pagination"></div>`;

    const detailPager = document.getElementById("supply-detail-line-pagination");
    const detailPages = Math.max(1, lineResult.totalPages || 1);
    detailPager.hidden = detailPages <= 1;
    setPagerContents(detailPager, foundationT("supply.previous"), `${lineResult.page} / ${detailPages}`, foundationT("supply.next"), lineResult.page <= 1, lineResult.page >= detailPages, (delta) => void showSupplyDetail(id, lineResult.page + delta));
    document.getElementById("supply-edit")?.addEventListener("click", async () => {
      const editor = await request(`/api/v1/supply/shipments/${encodeURIComponent(id)}/editor`);
      fillSupplyForm(editor);
    });
    document.getElementById("supply-confirm")?.addEventListener("click", () => confirmSupplyShipment(shipment.id));
    document.getElementById("supply-cancel")?.addEventListener("click", () => cancelSupplyShipment(shipment.id));
    bindPrintReportButtons(target);
  } catch (exception) {
    target.innerHTML = `<h2>${foundationT("supply.detail")}</h2><p>${escapeHtml(getFriendlyWorkspaceError(exception))}</p>`;
  }
}

function fillSupplyForm(shipment) {
  document.getElementById("supply-id").value = shipment.id;
  document.getElementById("supply-supplier").value = shipment.supplierName || "";
  document.getElementById("supply-invoice").value = shipment.invoiceNumber || "";
  document.getElementById("supply-date").value = shipment.shipmentDate ? shipment.shipmentDate.slice(0, 16) : "";
  document.getElementById("supply-location").value = shipment.destinationLocationId;
  document.getElementById("supply-notes").value = shipment.notes || "";
  supplyEditorLines = shipment.lines.map((line) => ({
    _clientId: line.id || createUuid(),
    skuId: line.skuId,
    quantity: line.quantity,
    unitPrice: line.unitPrice,
    lotNumber: line.lotNumber,
    expiryDate: line.expiryDate,
    notes: line.notes,
    skuCode: line.skuCode,
    productName: line.productName
  }));
  supplyEditorLineById = new Map(supplyEditorLines.map((line) => [line._clientId, line]));
  supplyEditorStatsDirty = true;
  supplyEditorPage = 1;
  renderSupplyEditorPage();
  document.getElementById("supply-costs")?.replaceChildren();
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
  if (id) {
    payload.expectedVersion = supplyCurrentDetail?.id === id ? supplyCurrentDetail.concurrencyVersion : null;
  }
  const validation = validateSupplyFormPayload(payload);
  if (validation.length > 0) {
    const firstInvalidLine = payload.lines.findIndex((line) =>
      !line.skuId || !Number.isFinite(line.quantity) || line.quantity <= 0 ||
      (line.unitPrice !== null && (!Number.isFinite(line.unitPrice) || line.unitPrice <= 0)));
    if (firstInvalidLine >= 0) {
      supplyEditorPage = Math.floor(firstInvalidLine / supplyEditorPageSize) + 1;
      renderSupplyEditorPage();
      document.querySelectorAll(".supply-line-row")[firstInvalidLine % supplyEditorPageSize]?.classList.add("supply-line-invalid");
    }
    showSupplyValidation(validation);
    error.textContent = foundationT("supply.fixTheHighlightedShipmentValuesBeforeSaving");
    error.hidden = false;
    return;
  }

  try {
    await request(id ? `/api/v1/supply/shipments/${encodeURIComponent(id)}` : "/api/v1/supply/shipments", {
      method: id ? "PUT" : "POST",
      body: JSON.stringify(stripSupplyPayloadInternals(payload))
    });
    notice(foundationT("supply.supplyShipmentSaved"), "success");
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
    notice(foundationT("supply.supplyShipmentReceivedIntoInventory"), "success");
    await showSupplyDetail(id);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function cancelSupplyShipment(id) {
  try {
    await request(`/api/v1/supply/shipments/${encodeURIComponent(id)}/cancel`, { method: "POST" });
    notice(foundationT("supply.supplyShipmentCancelled"), "success");
    await showSupplyDetail(id);
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function loadSupplyLocations() {
  inventoryLocations = await request("/api/v1/inventory/locations");
}

function collectSupplyFormPayload() {
  syncCurrentSupplyPage();
  const visibleRows = new Map([...document.querySelectorAll(".supply-line-row")].map((row) => [row.dataset.supplyLineKey, row]));
  return {
    supplierName: document.getElementById("supply-supplier").value.trim(),
    invoiceNumber: document.getElementById("supply-invoice").value.trim() || null,
    shipmentDate: document.getElementById("supply-date").value || null,
    destinationLocationId: document.getElementById("supply-location").value,
    notes: document.getElementById("supply-notes").value.trim() || null,
    lines: supplyEditorLines.map((line) => ({ ...line, _row: visibleRows.get(line._clientId) })),
    costs: [...document.querySelectorAll(".supply-cost-row")].map((row) => ({
      costType: canonicalSystemValue(row.querySelector(".supply-cost-type").value, "supplyCostType"),
      description: row.querySelector(".supply-cost-description").value.trim() || null,
      amount: Number(row.querySelector(".supply-cost-amount").value || 0),
      _row: row
    }))
  };
}

function validateSupplyFormPayload(payload) {
  const messages = [];
  if (!payload.supplierName) {
    messages.push(foundationT("supply.supplierIsRequired"));
  }
  if (!payload.destinationLocationId) {
    messages.push(foundationT("supply.destinationWarehouseIsRequired"));
  }
  if (payload.lines.length === 0) {
    messages.push(foundationT("supply.atLeastOneSKULineIsRequired"));
  }

  const duplicateKeys = new Set();
  payload.lines.forEach((line, index) => {
    line._row?.classList.remove("supply-line-invalid");
    if (!line.skuId) {
      line._row?.classList.add("supply-line-invalid");
      messages.push(foundationT("supply.validation.selectSku", { line: index + 1 }));
    }
    if (!Number.isFinite(line.quantity) || line.quantity <= 0) {
      line._row?.classList.add("supply-line-invalid");
      messages.push(foundationT("supply.validation.positiveQuantity", { line: index + 1 }));
    }
    if (line.unitPrice !== null && (!Number.isFinite(line.unitPrice) || line.unitPrice <= 0)) {
      line._row?.classList.add("supply-line-invalid");
      messages.push(foundationT("supply.validation.positiveUnitPrice", { line: index + 1 }));
    }
    const duplicateKey = `${line.skuId}|${(line.lotNumber || "").toUpperCase()}|${line.expiryDate || ""}`;
    if (line.skuId && duplicateKeys.has(duplicateKey)) {
      line._row?.classList.add("supply-line-invalid");
      messages.push(foundationT("supply.validation.duplicateBatch", { line: index + 1 }));
    }
    duplicateKeys.add(duplicateKey);
  });

  payload.costs.forEach((cost, index) => {
    cost._row.classList.remove("supply-line-invalid");
    if (!["Customs", "Freight", "Clearance", "Handling", "Insurance", "Other"].includes(cost.costType)) {
      cost._row.classList.add("supply-line-invalid");
      messages.push(foundationT("supply.validation.costType", { cost: index + 1 }));
    }
    if (!Number.isFinite(cost.amount) || cost.amount < 0) {
      cost._row.classList.add("supply-line-invalid");
      messages.push(foundationT("supply.validation.negativeCost", { cost: index + 1 }));
    }
  });

  return messages;
}

function stripSupplyPayloadInternals(payload) {
  return {
    ...payload,
    lines: payload.lines.map(({ _row, _clientId, skuCode, productName, ...line }) => line),
    costs: payload.costs.map(({ _row, ...cost }) => cost)
  };
}

function updateSupplyFormSummary() {
  const form = document.getElementById("supply-form");
  if (!form) {
    return;
  }
  const lines = supplyEditorLines;
  const productTotal = supplyEditorStats.productTotal;
  const costTotal = [...document.querySelectorAll(".supply-cost-amount")].reduce((total, input) => total + Math.max(0, Number(input.value || 0) || 0), 0);
  const incompletePrices = supplyEditorStats.incompletePrices;
  const invalidPrices = supplyEditorStats.invalidPrices;
  document.getElementById("supply-form-product-total").textContent = formatMoney(productTotal);
  document.getElementById("supply-form-cost-total").textContent = formatMoney(costTotal);
  document.getElementById("supply-form-landed-total").textContent = formatMoney(productTotal + costTotal);
  const readiness = document.getElementById("supply-form-readiness");
  if (invalidPrices > 0) {
    readiness.textContent = foundationT("supply.invalidPrices");
    readiness.className = "status-danger";
  } else if (incompletePrices > 0 || lines.length === 0) {
    readiness.textContent = foundationT("supply.incompleteCount", { count: incompletePrices || lines.length });
    readiness.className = "status-warn";
  } else {
    readiness.textContent = foundationT("supply.ready");
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
  const lineCount = shipment.lineCount ?? shipment.lines?.length ?? 0;
  const incomplete = shipment.incompletePriceCount ?? shipment.lines?.filter((line) => line.unitPrice == null).length ?? 0;
  const invalid = shipment.invalidPriceCount ?? shipment.lines?.filter((line) => line.unitPrice != null && line.unitPrice <= 0).length ?? 0;
  if (shipment.status !== "Draft") {
    return { canConfirm: false, label: supplyStatusLabel(shipment.status), message: foundationT("supply.onlyDraftShipmentsCanBeConfirmed") };
  }
  if (lineCount === 0) {
    return { canConfirm: false, label: foundationT("supply.noLines"), message: foundationT("supply.atLeastOneSKULineIsRequired") };
  }
  if (invalid > 0) {
    return { canConfirm: false, label: foundationT("supply.invalidPrices"), message: foundationT("supply.everySKUPriceMustBeGreaterThanZeroBeforeConfirmation") };
  }
  if (incomplete > 0) {
    return { canConfirm: false, label: foundationT("supply.blankPriceCount", { count: incomplete }), message: foundationT("supply.everySKULineNeedsAUnitPriceBeforeConfirmation") };
  }
  return { canConfirm: true, label: foundationT("supply.ready"), message: foundationT("supply.readyToConfirm2") };
}

function clearSupplyValidation() {
  document.querySelectorAll(".supply-line-invalid").forEach((row) => row.classList.remove("supply-line-invalid"));
  const list = document.getElementById("supply-validation-list");
  if (list) {
    list.hidden = true;
    list.replaceChildren();
  }
}

function showSupplyValidation(messages) {
  const list = document.getElementById("supply-validation-list");
  if (!list || messages.length === 0) {
    return;
  }
  list.innerHTML = `<strong>${foundationT("supply.reviewTheseValues")}</strong><ul>${messages.map((message) => `<li>${escapeHtml(message)}</li>`).join("")}</ul>`;
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
          <div class="section-head"><div><h2>${escapeHtml(foundationT("navigation.stocktake"))}</h2><p class="muted-text">${escapeHtml(foundationT("app.inline.countPhysicalStockBySKULotAndExpiryThen"))}</p></div><button id="stocktake-refresh" class="button secondary" type="button">${escapeHtml(foundationT("common.refresh"))}</button></div>
          ${isAdmin ? `
            <form id="stocktake-create-form" class="form">
              <div class="form-error" id="stocktake-error" hidden></div>
              <div class="field"><label for="stocktake-location">${escapeHtml(foundationT("app.inventoryLocation"))}</label><select id="stocktake-location" class="select"></select></div>
              <div class="field"><label for="stocktake-notes">${escapeHtml(foundationT("payments.notes"))}</label><textarea id="stocktake-notes" class="input" rows="3"></textarea></div>
              <button class="button primary" type="submit">${escapeHtml(foundationT("app.inline.openSession"))}</button>
            </form>` : `<p class="muted-text">${escapeHtml(foundationT("app.inline.readOnlyStocktakeReview"))}</p>`}
        </section>
      </aside>
      <section class="catalog-main">
        <section class="band">
          <div class="section-head"><h2>${escapeHtml(foundationT("app.inline.sessions"))}</h2><span id="stocktake-count" class="muted-text">${escapeHtml(foundationT("common.loading"))}</span></div>
          <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("app.inline.session"))}</th><th>${escapeHtml(foundationT("app.inventoryLocation"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("app.inline.counted"))}</th><th>${escapeHtml(foundationT("app.inline.discrepancy"))}</th><th>${escapeHtml(foundationT("app.created"))}</th><th>${escapeHtml(foundationT("payments.actions"))}</th></tr></thead><tbody id="stocktake-rows"></tbody></table></div>
        </section>
        <section class="band" id="stocktake-detail"><h2>${escapeHtml(foundationT("app.inline.sessionDetail"))}</h2><p class="muted-text">${escapeHtml(foundationT("app.inline.selectASessionToEnterCountsOrReviewDiscrepancies"))}</p></section>
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
    count.textContent = foundationT("stocktake.sessionCount", { count: result.totalCount });
    tbody.innerHTML = result.items.length === 0 ? `<tr><td colspan="7">${escapeHtml(foundationT("app.inline.noStocktakeSessionsYet"))}</td></tr>` : result.items.map((session) => {
      const location = inventoryLocations.find((value) => value.id === session.locationId);
      return `<tr>
        <td>${escapeHtml(stocktakeReference(session, location))}</td>
        <td>${escapeHtml(location?.name || shortId(session.locationId, "LOC"))}</td>
        <td>${escapeHtml(uiText(session.status))}</td>
        <td>${escapeHtml(session.productsCounted)}</td>
        <td>${escapeHtml(session.totalDiscrepancyUnits)}</td>
        <td>${escapeHtml(formatDateTime(session.createdAt))}</td>
        <td><button class="button secondary table-action" type="button" data-stocktake-detail="${escapeHtml(session.id)}">${escapeHtml(foundationT("payments.details"))}</button><button class="button secondary table-action" type="button" data-print-report="stocktake-summary" data-print-id="${escapeHtml(session.id)}" data-print-code="${escapeHtml(stocktakeReference(session, location))}">${escapeHtml(foundationT("payments.print"))}</button></td>
      </tr>`;
    }).join("");
    tbody.querySelectorAll("[data-stocktake-detail]").forEach((button) => button.addEventListener("click", () => showStocktakeDetail(button.dataset.stocktakeDetail)));
    bindPrintReportButtons(tbody);
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="7">${escapeHtml(getFriendlyWorkspaceError(exception))}</td></tr>`;
  }
}

async function createStocktakeSession(event) {
  event.preventDefault();
  clearFormError("stocktake-error");
  const locationId = document.getElementById("stocktake-location").value;
  if (!locationId) {
    showFormError("stocktake-error", foundationT("stocktake.errors.locationRequired"));
    return;
  }

  try {
    const session = await request("/api/v1/stocktakes", {
      method: "POST",
      body: JSON.stringify({ locationId, notes: document.getElementById("stocktake-notes").value.trim() || null })
    });
    notice(foundationT("app.message.stocktakeSessionOpened"), "success");
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
  target.dataset.sessionId = sessionId;
  const location = inventoryLocations.find((value) => value.id === session.locationId);
  target.innerHTML = `
    <div class="section-head">
      <div><h2>${escapeHtml(stocktakeReference(session, location))}</h2><p class="muted-text">${escapeHtml(uiText(session.status))}</p></div>
      ${isAdmin && session.status === "Draft" ? `<button id="stocktake-confirm" class="button primary" type="button">${escapeHtml(foundationT("app.inline.confirmAdjustments"))}</button>` : ""}
    </div>
    ${session.notes ? `<p class="muted-text"><strong>${escapeHtml(foundationT("payments.notes"))}:</strong> ${escapeHtml(session.notes)}</p>` : ""}
    <div class="table-wrap compact-table"><table><thead><tr><th>${escapeHtml(foundationT("app.sku"))}</th><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("app.catalogExpiry"))}</th><th>${escapeHtml(foundationT("stocktake.packs"))}</th><th>${escapeHtml(foundationT("stocktake.pieces"))}</th><th>${escapeHtml(foundationT("stocktake.physicalPacks"))}</th><th>${escapeHtml(foundationT("stocktake.physicalPieces"))}</th><th>${escapeHtml(foundationT("stocktake.delta"))}</th><th>${escapeHtml(foundationT("payments.merchantOpening.note"))}</th></tr></thead><tbody>${session.lines.length === 0
      ? `<tr><td colspan="9">${escapeHtml(foundationT("app.inline.noCountedLinesYet"))}</td></tr>`
      : session.lines.map((line) => `<tr><td>${escapeHtml(stocktakeSkuLabel(line.skuId))}</td><td>${escapeHtml(line.lotNumber || "-")}</td><td>${escapeHtml(line.expiryDate || "-")}</td><td>${escapeHtml(line.systemPackCount ?? line.systemQtyBefore)}</td><td>${escapeHtml(line.systemPieceCount ?? 0)}</td><td>${escapeHtml(line.physicalPackCount ?? line.physicalCount)}</td><td>${escapeHtml(line.physicalPieceCount ?? 0)}</td><td>${escapeHtml(`${line.deltaPackCount ?? line.delta} / ${line.deltaPieceCount ?? 0}`)}</td><td>${escapeHtml(line.lineNote || "-")}</td></tr>`).join("")}</tbody></table></div>
    ${isAdmin && session.status === "Draft" ? `
      <form id="stocktake-lines-form" class="form wide-form compact-form">
        <div class="form-error" id="stocktake-lines-error" hidden></div>
        <div id="stocktake-line-editor" class="line-editor"></div>
        <div class="form-actions"><button id="add-stocktake-line" class="button secondary" type="button">${escapeHtml(foundationT("supply.addLine"))}</button><button class="button primary" type="submit">${escapeHtml(foundationT("app.inline.saveCounts"))}</button></div>
      </form>` : ""}`;

  if (isAdmin && session.status === "Draft") {
    document.getElementById("stocktake-confirm").addEventListener("click", () => confirmStocktake(session.id));
    const editor = document.getElementById("stocktake-line-editor");
    const addLine = (line = {}) => {
      const row = document.createElement("div");
      row.className = "stocktake-line-row";
      row.innerHTML = `
        <input class="stocktake-line-sku" type="hidden" value="${escapeHtml(line.skuId || "")}">
        <div class="field op-line-finder"><label>${escapeHtml(foundationT("supply.findSKU"))}</label><input class="input stocktake-line-search" type="search" autocomplete="off" placeholder="${escapeHtml(foundationT("app.attribute.productColorPowerOrSKUCode"))}"><div class="op-line-search-results" hidden></div><div class="stocktake-line-resolved muted-text">${escapeHtml(foundationT("supply.searchAndSelectASKU"))}</div></div>
        <div class="field"><label>${escapeHtml(foundationT("app.inline.lotNumber"))}</label><input class="input stocktake-line-lot" value="${escapeHtml(line.lotNumber || "")}" placeholder="${escapeHtml(foundationT("app.attribute.blankIfNone"))}"></div>
        <div class="field"><label>${escapeHtml(foundationT("app.inline.expiryDate"))}</label><input class="input stocktake-line-expiry" type="date" value="${escapeHtml(line.expiryDate || "")}"></div>
        <div class="field"><label>${escapeHtml(foundationT("stocktake.physicalPacks"))}</label><input class="input stocktake-line-pack-count" type="number" min="0" step="1" value="${escapeHtml(line.physicalPackCount ?? line.physicalCount ?? 0)}"></div>
        <div class="field"><label>${escapeHtml(foundationT("stocktake.physicalPieces"))}</label><input class="input stocktake-line-piece-count" type="number" min="0" step="1" value="${escapeHtml(line.physicalPieceCount ?? 0)}"></div>
        <div class="field"><label>${escapeHtml(foundationT("payments.merchantOpening.note"))}</label><input class="input stocktake-line-note" value="${escapeHtml(line.lineNote || "")}"></div>
        <button class="button secondary" type="button" data-remove-line>${escapeHtml(foundationT("app.inline.remove"))}</button>`;
      editor.appendChild(row);
      row.querySelector(".stocktake-line-search").addEventListener("input", debounce(() => { void renderStocktakeSkuSearchResults(row); }, 250));
      if (line.skuId) seedStocktakeLineSkuSelection(row, line.skuId);
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
    physicalCount: Number(row.querySelector(".stocktake-line-pack-count").value),
    physicalPackCount: Number(row.querySelector(".stocktake-line-pack-count").value),
    physicalPieceCount: Number(row.querySelector(".stocktake-line-piece-count").value),
    lineNote: row.querySelector(".stocktake-line-note").value.trim() || null
  }));
  if (lines.some((line) => !line.skuId || !Number.isInteger(line.physicalPackCount) || line.physicalPackCount < 0 || !Number.isInteger(line.physicalPieceCount) || line.physicalPieceCount < 0)) {
    showFormError("stocktake-lines-error", foundationT("stocktake.errors.invalidLines"));
    return;
  }
  try {
    await request(`/api/v1/stocktakes/${sessionId}/lines`, { method: "PUT", body: JSON.stringify({ lines }) });
    notice(foundationT("app.message.stocktakeCountsSaved"), "success");
    await showStocktakeDetail(sessionId);
    await loadStocktakes();
  } catch (exception) {
    showFormError("stocktake-lines-error", getFriendlyWorkspaceError(exception));
  }
}

async function confirmStocktake(sessionId) {
  try {
    await request(`/api/v1/stocktakes/${sessionId}/confirm`, { method: "POST" });
    notice(foundationT("app.message.stocktakeConfirmedAndLedgerAdjustmentsPosted"), "success");
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
          <h2>${escapeHtml(foundationT("navigation.notifications"))}</h2>
          <p class="muted-text">${escapeHtml(foundationT("app.inline.reviewAlertsWorkflowUpdatesTargetsAndLinkedRecordsWithout"))}</p>
        </div>
        <span id="notification-count" class="status-pill status-muted">${escapeHtml(foundationT("common.loading"))}</span>
      </div>
      <div class="notification-summary">
        <div class="metric"><span>${escapeHtml(foundationT("app.inline.visible"))}</span><strong id="notification-visible-count">-</strong></div>
        <div class="metric"><span>${escapeHtml(foundationT("app.inline.unread"))}</span><strong id="notification-unread-count">-</strong></div>
        <div class="metric"><span>${escapeHtml(foundationT("payments.scope"))}</span><strong>${escapeHtml(roleLabel(auth?.user.role || ""))}</strong></div>
      </div>
      <div class="toolbar notification-toolbar">
        <select id="notification-type-filter" class="select compact-select" aria-label="${escapeHtml(foundationT("app.attribute.notificationType"))}">
          <option value="">${escapeHtml(foundationT("reports.allTypes"))}</option>
        </select>
        <label class="inline-check"><input id="notification-unread-filter" type="checkbox"> ${escapeHtml(foundationT("app.inline.unreadOnly"))}</label>
        <button id="notifications-refresh" class="button secondary" type="button">${escapeHtml(foundationT("common.refresh"))}</button>
        <button id="mark-all-read" class="button secondary" type="button">${escapeHtml(foundationT("app.inline.markAllRead"))}</button>
      </div>
      <div id="notification-list" class="notification-list">${escapeHtml(foundationT("common.loading"))}</div>
      <div class="pagination-bar" id="notification-pagination" hidden>
        <button class="button secondary table-action" type="button" id="notifications-prev">${escapeHtml(foundationT("pagination.previous"))}</button>
        <span class="muted-text" id="notifications-page-label">${escapeHtml(foundationT("app.inline.page1Of1"))}</span>
        <button class="button secondary table-action" type="button" id="notifications-next">${escapeHtml(foundationT("pagination.next"))}</button>
      </div>
    </section>
    ${canReadRecalls ? `
      <section class="band" id="merchant-expiry-recalls-section">
        <div class="section-head"><div><h2>${escapeHtml(foundationT("app.inline.merchantExpiryRecalls"))}</h2><p class="muted-text">${escapeHtml(foundationT("app.inline.soldMerchantBatchesInsideTheConfiguredExpiryWindowOrdered"))}</p></div><span id="merchant-recall-count" class="status-pill status-muted">${escapeHtml(foundationT("common.loading"))}</span></div>
        <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("customer.merchant"))}</th><th>${escapeHtml(foundationT("app.inline.sKUProduct"))}</th><th>${escapeHtml(foundationT("app.lot"))}</th><th>${escapeHtml(foundationT("app.catalogExpiry"))}</th><th>${escapeHtml(foundationT("app.sold"))}</th><th>${escapeHtml(foundationT("app.returned"))}</th><th>${escapeHtml(foundationT("payments.status"))}</th><th>${escapeHtml(foundationT("payments.actions"))}</th></tr></thead><tbody id="merchant-recall-rows"><tr><td colspan="8">${escapeHtml(foundationT("app.inline.loadingRecalls"))}</td></tr></tbody></table></div>
        ${isAdmin ? `<form id="merchant-recall-config" class="form grid-form band-subtle"><div class="field"><label for="merchant-recall-months">${escapeHtml(foundationT("app.inline.globalExpiryWindowMonths"))}</label><input id="merchant-recall-months" class="input" type="number" min="1" max="120" value="24" required></div><label class="inline-check"><input id="merchant-recall-active" type="checkbox"> ${escapeHtml(foundationT("app.inline.dailyScanActive"))}</label><div class="form-actions"><button class="button secondary" type="submit">${escapeHtml(foundationT("app.inline.saveRecallSettings"))}</button></div></form>` : ""}
      </section>` : ""}
    ${isAdmin ? `
      <section class="band">
        <h2>${escapeHtml(foundationT("app.inline.manualAlertTriggers"))}</h2>
        <p class="muted-text">${escapeHtml(foundationT("app.inline.runAlertScansOnDemandWhenYouWantTo"))}</p>
        <div class="toolbar">
          <button class="button secondary" type="button" data-alert-run="low-stock">${escapeHtml(foundationT("app.inventoryLowStock"))}</button>
          <button class="button secondary" type="button" data-alert-run="expiry">${escapeHtml(foundationT("app.catalogExpiry"))}</button>
          <button class="button secondary" type="button" data-alert-run="unresolved-reserves">${escapeHtml(foundationT("app.inline.unresolvedReserves"))}</button>
          <button class="button secondary" type="button" data-alert-run="open-payment-summary">${escapeHtml(foundationT("app.inline.generateWeeklyOpenPaymentSummary"))}</button>
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
    count.textContent = foundationT("inventory.recallCount", { count: recalls.length });
    tbody.innerHTML = recalls.length === 0 ? `<tr><td colspan="8">${escapeHtml(foundationT("app.inline.noActiveMerchantExpiryRecalls"))}</td></tr>` : recalls.map((recall) => `
      <tr data-merchant-recall-row="${escapeHtml(recall.id)}">
        <td><strong>${escapeHtml(recall.merchantName)}</strong></td>
        <td><strong>${escapeHtml(recall.skuCode || shortId(recall.skuId, "SKU"))}</strong><span class="muted-cell">${escapeHtml(recall.productName || "-")}</span></td>
        <td>${escapeHtml(recall.lotNumber || "-")}</td>
        <td>${expiryBadge(recall.expiryDate)}</td>
        <td>${escapeHtml(recall.soldQuantity)}</td>
        <td>${escapeHtml(recall.returnedQuantity)}</td>
        <td><span class="status-pill ${recall.daysToExpiry < 0 ? "status-warn" : "status-muted"}">${escapeHtml(foundationT(recall.daysToExpiry < 0 ? "app.inline.expired" : "app.inline.approachingExpiry"))}</span></td>
        <td>${canManage ? `<button class="button primary table-action" type="button" data-recall-return="${escapeHtml(recall.id)}">${escapeHtml(foundationT("app.inline.startReturn"))}</button><button class="button secondary table-action" type="button" data-recall-no-stock="${escapeHtml(recall.id)}">${escapeHtml(foundationT("app.inline.noStockAtMerchant"))}</button>` : `<span class="muted-text">${escapeHtml(foundationT("payments.readOnly"))}</span>`}</td>
      </tr>`).join("");
    tbody.querySelectorAll("[data-recall-return]").forEach((button) => button.addEventListener("click", () => startMerchantRecallReturn(recalls.find((recall) => recall.id === button.dataset.recallReturn))));
    tbody.querySelectorAll("[data-recall-no-stock]").forEach((button) => button.addEventListener("click", () => closeMerchantRecallNoStock(button.dataset.recallNoStock, button)));
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
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
  const note = await promptDialog({ title: foundationT("app.prompt.noStockAtMerchant"), label: foundationT("app.prompt.noStockHelp"), required: true, multiline: true });
  if (!note) return;
  try {
    await withMutationGuard(`merchant-recall:${recallId}:no-stock`, button, () => request(`/api/v1/merchant-expiry-recalls/${recallId}/no-stock`, { method: "POST", body: JSON.stringify({ note }) }));
    notice(foundationT("app.message.merchantRecallClosedAsNoStock"), "success");
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
    notice(foundationT("app.message.merchantRecallSettingsSaved"), "success");
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
    select.innerHTML = `<option value="">${escapeHtml(foundationT("reports.allTypes"))}</option>${types.map((type) => `<option value="${escapeHtml(type.alertType)}">${escapeHtml(notificationTypeLabel(type.alertType))} (${escapeHtml(type.count)}${type.unreadCount ? `, ${escapeHtml(foundationT("app.inline.unreadCount", { count: type.unreadCount }))}` : ""})</option>`).join("")}`;
    select.value = types.some((type) => type.alertType === selected) ? selected : "";
  } catch {
    select.replaceChildren(new Option(foundationT("reports.allTypes"), ""));
  }
}

async function loadNotifications(page = notificationPageState.page || 1) {
  const loadGeneration = ++notificationLoadGeneration;
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
    // A refresh, route change, or mark-all-read can start another load while
    // this request is in flight. Never let a stale response write into a
    // detached notification view.
    if (loadGeneration !== notificationLoadGeneration || document.getElementById("notification-list") !== list) {
      return;
    }
    count.textContent = foundationT("notifications.visibleCount", { count: result.totalCount });
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
      ? `<div class="empty-state">${escapeHtml(foundationT("app.inline.noNotificationsMatchTheCurrentFilters"))}</div>`
      : result.items.map(renderNotificationCard).join("");
    if (pagination && pageLabel && prev && next) {
      pagination.hidden = result.totalCount <= result.pageSize;
      pageLabel.textContent = foundationT("pagination.pageOf", { page: notificationPageState.page, pages: totalPages });
      prev.disabled = notificationPageState.page <= 1;
      next.disabled = notificationPageState.page >= totalPages;
    }
    list.querySelectorAll("[data-read-notification]").forEach((button) => button.addEventListener("click", () => markNotificationRead(button.dataset.readNotification)));
    list.querySelectorAll("[data-toggle-notification]").forEach((button) => button.addEventListener("click", () => toggleNotificationDetails(button.dataset.toggleNotification)));
    list.querySelectorAll("[data-resolve-notification]").forEach((button) => button.addEventListener("click", () => resolveNotificationDestination(button.dataset.resolveNotification)));
    updateNotificationBadge();
  } catch (exception) {
    if (!count || !list || loadGeneration !== notificationLoadGeneration || document.getElementById("notification-list") !== list) {
      return;
    }
    count.textContent = foundationT("payments.failed");
    list.innerHTML = `<div class="empty-state">${escapeHtml(getFriendlyWorkspaceError(exception))}</div>`;
    if (pagination) {
      pagination.hidden = true;
    }
  }
}

function renderNotificationCard(item) {
  const tone = item.isRead ? "status-muted" : "status-warning";
  const target = item.targetRole ? roleLabel(item.targetRole) : (item.targetUserId ? foundationT("app.specificEmployee") : foundationT("notifications.broadcast"));
  const actionLabel = uiText(item.actionLabel || notificationActionLabel(item));
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
          <p class="notification-message">${escapeHtml(uiText(item.message))}</p>
          ${item.referenceCode ? `<p class="muted-text notification-record-code">${escapeHtml(item.referenceCode)}${item.referenceTitle ? ` / ${escapeHtml(item.referenceTitle)}` : ""}</p>` : ""}
        </div>
        <div class="notification-actions">
          <button class="button secondary table-action" type="button" data-toggle-notification="${escapeHtml(item.id)}">${escapeHtml(foundationT("payments.details"))}</button>
          ${actionButton}
          ${item.isRead ? `<span class="status-pill status-muted">${escapeHtml(foundationT("app.inline.read"))}</span>` : `<button class="button primary table-action" type="button" data-read-notification="${escapeHtml(item.id)}">${escapeHtml(foundationT("app.inline.markRead"))}</button>`}
        </div>
      </div>
      <div id="notification-details-${escapeHtml(item.id)}" class="notification-details" hidden>
        <dl>
          <div><dt>${escapeHtml(foundationT("app.inline.target"))}</dt><dd>${escapeHtml(target)}</dd></div>
          <div><dt>${escapeHtml(foundationT("app.inline.channel"))}</dt><dd>${escapeHtml(item.channel ? uiText(item.channel) : "-")}</dd></div>
          <div><dt>${escapeHtml(foundationT("payments.reference"))}</dt><dd>${escapeHtml(item.referenceCode || item.referenceType || "-")}${item.referenceId && !item.referenceCode ? ` / ${escapeHtml(shortId(item.referenceId, referencePrefix(item.referenceType)))}` : ""}</dd></div>
          <div><dt>${escapeHtml(foundationT("app.inline.eventLocation"))}</dt><dd>${item.referenceId ? escapeHtml(actionLabel) : "-"}</dd></div>
          <div><dt>${escapeHtml(foundationT("payments.status"))}</dt><dd>${item.isRead ? foundationT("app.inline.read") : foundationT("app.inline.unread")}</dd></div>
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
  const key = {
    stockbalance: "stockBalance",
    inventorybatch: "inventoryBatch",
    paymentlog: "payment",
    operation: "operation",
    stocktake: "stocktake",
    supplyshipment: "shipment",
    merchant: "merchant",
    merchantexpiryrecall: "merchantRecall",
    exportlog: "export"
  }[(item.referenceType || "").toLowerCase()] || "relatedRecord";
  return foundationT(`notifications.action.${key}`);
}

async function resolveNotificationDestination(id) {
  try {
    const destination = await request(`/api/v1/notifications/${encodeURIComponent(id)}/resolve`);
    if (destination.status !== "Ready") {
      notice(uiText(destination.message || foundationT("app.message.theRelatedRecordIsUnavailableOrNoLongerPermitted")), destination.status === "Forbidden" ? "error" : "warning");
      return;
    }
    if (!destination.navigationReference) {
      notice(foundationT("app.message.thisSecureLinkCouldNotBeCreated"), "warning");
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
  const keys = {
    LowStock: "lowStock",
    Expiry: "expiry",
    UnresolvedReserves: "unresolvedReserves",
    OpenPaymentWeeklySummary: "openPaymentWeeklySummary",
    PaymentWorkflow: "paymentWorkflow",
    OperationStatus: "operationStatus",
    StocktakeConfirmed: "stocktakeConfirmed",
    MerchantExpiryRecall: "merchantExpiryRecall"
  };
  return keys[type] ? foundationT(`notifications.type.${keys[type]}`) : (type ? uiText(type) : foundationT("notifications.type.notification"));
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
    await loadNotificationTypes();
    await loadNotifications();
    notice(`Alert run matched ${result.matchedItems} item(s).`, "success");
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
          <h2>${escapeHtml(foundationT("app.inline.usersAndAccess"))}</h2>
          <p class="muted-text">${escapeHtml(foundationT("app.inline.reviewEmployeeAccountsAssignedLocationsAndControlledAccessFrom"))}</p>
        </div>
        <span id="admin-users-count" class="status-pill status-muted">${escapeHtml(foundationT("common.loading"))}</span>
      </div>
      ${isAdministrator ? `
        <form id="admin-create-user-form" class="admin-create-user-form band-subtle" novalidate>
          <div class="section-head tight-head">
            <div>
              <h3>${escapeHtml(foundationT("app.inline.createEmployeeAccount"))}</h3>
              <p class="muted-text">${escapeHtml(foundationT("app.inline.setTheEmployeeSSignInNameTemporaryPassword"))}</p>
            </div>
          </div>
          <div class="form-grid">
            <div class="field"><label for="admin-user-full-name">${escapeHtml(foundationT("app.inline.fullName"))}</label><input class="input" id="admin-user-full-name" name="fullName" autocomplete="name" required></div>
            <div class="field"><label for="admin-user-username">${escapeHtml(foundationT("app.username"))}</label><input class="input" id="admin-user-username" name="username" autocomplete="username" required></div>
            <div class="field"><label for="admin-user-role">${escapeHtml(foundationT("app.catalogRole"))}</label><select class="select" id="admin-user-role" name="role" required>
              <option value="Admin">${escapeHtml(foundationT("app.inline.administrator"))}</option>
              <option value="ERPAdmin">${escapeHtml(foundationT("app.inline.eRPAdministrator"))}</option>
              <option value="CLevel">${escapeHtml(foundationT("app.inline.cLevel"))}</option>
              <option value="Accountant">${escapeHtml(foundationT("app.inline.accountant"))}</option>
              <option value="WarehouseClerk">${escapeHtml(foundationT("app.inline.warehouseClerk"))}</option>
            </select></div>
            <div class="field" id="admin-user-location-field" hidden><label for="admin-user-location">${escapeHtml(foundationT("app.inline.warehouseLocation"))}</label><select class="select" id="admin-user-location" name="locationId" disabled><option value="">${escapeHtml(foundationT("app.inline.loadingLocations"))}</option></select></div>
            <div class="field"><label for="admin-user-password">${escapeHtml(foundationT("app.inline.temporaryPassword"))}</label><input class="input" id="admin-user-password" name="password" type="password" autocomplete="new-password" minlength="8" required></div>
            <div class="field"><label for="admin-user-confirm-password">${escapeHtml(foundationT("app.inline.confirmPassword"))}</label><input class="input" id="admin-user-confirm-password" name="confirmPassword" type="password" autocomplete="new-password" minlength="8" required></div>
          </div>
          <div class="form-actions"><button class="button primary" type="submit">${escapeHtml(foundationT("app.inline.createEmployeeAccount"))}</button></div>
        </form>
        <form id="admin-create-location-form" class="admin-create-user-form band-subtle" novalidate hidden>
          <div class="section-head tight-head">
            <div>
              <h3>${escapeHtml(foundationT("app.inline.addWarehouse"))}</h3>
              <p class="muted-text">${escapeHtml(foundationT("app.inline.onlyThePrimaryAdministratorCanAddAnActiveWarehouse"))}</p>
            </div>
          </div>
          <div class="form-grid">
            <div class="field"><label for="admin-location-name">${escapeHtml(foundationT("app.inline.warehouseName"))}</label><input class="input" id="admin-location-name" name="name" autocomplete="off" required></div>
            <div class="field"><label for="admin-location-type">${escapeHtml(foundationT("app.inline.locationType"))}</label><select class="select" id="admin-location-type" name="locationType" required>
              <option value="SubWarehouse">${escapeHtml(foundationT("app.inline.subWarehouse"))}</option>
              <option value="Retail">${escapeHtml(foundationT("app.inline.retail"))}</option>
              <option value="Online">${escapeHtml(foundationT("app.inline.online"))}</option>
              <option value="MainWarehouse">${escapeHtml(foundationT("app.inline.mainWarehouse"))}</option>
            </select></div>
          </div>
          <p class="muted-text">${escapeHtml(foundationT("app.inline.thereCanBeOnlyOneActiveMainWarehouse"))}</p>
          <div class="form-actions"><button class="button primary" type="submit">${escapeHtml(foundationT("app.inline.addWarehouse"))}</button></div>
        </form>` : ""}
      <div id="admin-users-error" class="form-error" hidden></div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>${escapeHtml(foundationT("app.inline.usernameFullName"))}</th>
              <th>${escapeHtml(foundationT("app.catalogRole"))}</th>
              <th>${escapeHtml(foundationT("app.inventoryLocation"))}</th>
              <th>${escapeHtml(foundationT("payments.status"))}</th>
              ${canResetPasswords ? `<th>${escapeHtml(foundationT("admin.newPassword"))}</th><th>${escapeHtml(foundationT("app.inline.confirmPassword"))}</th><th>${escapeHtml(foundationT("app.password"))}</th><th>${escapeHtml(foundationT("finance.reconciliation.account"))}</th>` : ""}
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
  tbody.innerHTML = `<tr><td colspan="${colspan}">${escapeHtml(foundationT("app.inline.loadingUsers"))}</td></tr>`;

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
    count.textContent = foundationT(users.length === 1 ? "app.count.user" : "app.count.users", { count: users.length });
    tbody.innerHTML = users.length === 0 ? `<tr><td colspan="${colspan}">${escapeHtml(foundationT("app.inline.noUsersFound"))}</td></tr>` : users.map((user) => `
      <tr data-admin-user-row="${escapeHtml(user.id)}">
        <td><strong>${escapeHtml(user.username)}</strong><br><span class="muted-text">${escapeHtml(user.fullName || "-")}</span></td>
        <td>${escapeHtml(roleLabel(user.role))}${user.isPrimaryAdmin ? `<br><span class="status-pill status-info">${escapeHtml(foundationT("admin.primaryAdmin"))}</span>` : ""}</td>
        <td>${escapeHtml(user.locationId ? (locationNames.get(user.locationId) || foundationT("app.inline.unknownLocation")) : foundationT("app.inline.allLocations"))}</td>
        <td><span class="status-pill ${user.isActive ? "status-ok" : "status-muted"}">${escapeHtml(uiText(user.isActive ? "Active" : "Inactive"))}</span>
          ${isCurrentPrimaryAdmin && user.id !== currentUserId && !user.isPrimaryAdmin ? `<br><button class="button secondary table-action" type="button" data-admin-set-active="${escapeHtml(user.id)}" data-admin-next-active="${String(!user.isActive)}">${escapeHtml(uiText(user.isActive ? "Deactivate" : "Reactivate"))}</button>` : ""}
        </td>
        ${canResetPasswords ? `<td><input class="input compact-input" type="password" autocomplete="new-password" data-admin-password="${escapeHtml(user.id)}" placeholder="${escapeHtml(foundationT("app.attribute.8Characters"))}"></td>
        <td><input class="input compact-input" type="password" autocomplete="new-password" data-admin-confirm-password="${escapeHtml(user.id)}" placeholder="${escapeHtml(foundationT("app.attribute.repeat"))}"></td>
        <td><button class="button primary table-action" type="button" data-admin-change-password="${escapeHtml(user.id)}">${escapeHtml(foundationT("app.inline.change"))}</button></td>
        <td>
          ${isCurrentPrimaryAdmin && user.isActive && user.role === "Admin" && !user.isPrimaryAdmin ? `<button class="button secondary table-action" type="button" data-admin-transfer-primary="${escapeHtml(user.id)}">${escapeHtml(foundationT("app.inline.makePrimary"))}</button>` : ""}
          ${user.canDelete ? `<button class="button secondary table-action" type="button" data-admin-delete-user="${escapeHtml(user.id)}">${escapeHtml(foundationT("app.inline.delete"))}</button>` : `<button class="button secondary table-action" type="button" disabled title="${escapeHtml(user.deletionBlockedReason || "This account cannot be deleted.")}">${escapeHtml(foundationT("app.inline.protected"))}</button>`}
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
    count.textContent = foundationT("payments.failed");
    tbody.innerHTML = `<tr><td colspan="${colspan}">${escapeHtml(foundationT("app.inline.couldNotLoadUsers"))}</td></tr>`;
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
  location.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.selectWarehouseLocation"))}</option>${locations
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
  const role = canonicalSystemValue(values.get("role"), "role");
  const locationId = String(values.get("locationId") || "");

  if (!fullName || !username) {
    notice(foundationT("app.message.fullNameAndUsernameAreRequired"), "error");
    (!fullName ? document.getElementById("admin-user-full-name") : document.getElementById("admin-user-username"))?.focus();
    return;
  }
  if (password.length < 8) {
    notice(foundationT("app.message.passwordMustBeAtLeast8Characters"), "error");
    document.getElementById("admin-user-password")?.focus();
    return;
  }
  if (password !== confirmPassword) {
    notice(foundationT("app.message.passwordConfirmationDoesNotMatch"), "error");
    document.getElementById("admin-user-confirm-password")?.focus();
    return;
  }
  if (role === "WarehouseClerk" && !locationId) {
    notice(foundationT("app.message.warehouseClerksMustBeAssignedToAWarehouseLocation"), "error");
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
  const locationType = canonicalSystemValue(values.get("locationType"), "locationType");
  if (!name) {
    notice(foundationT("app.message.warehouseNameIsRequired"), "error");
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
    notice(foundationT("app.message.passwordMustBeAtLeast8Characters"), "error");
    passwordInput?.focus();
    return;
  }

  if (newPassword !== confirmPassword) {
    notice(foundationT("app.message.passwordConfirmationDoesNotMatch"), "error");
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
  document.getElementById("view").innerHTML = `<section class="band"><h2>${escapeHtml(title)}</h2><p class="muted-text">${escapeHtml(foundationT("app.inline.noRowsAreAvailableForThisWorkspaceYet"))}</p><div class="table-wrap"><table><thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead><tbody><tr>${headers.map(() => "<td>-</td>").join("")}</tr></tbody></table></div></section>`;
}

function renderForbidden() {
  document.getElementById("page-title").textContent = foundationT("app.message.forbidden");
  document.getElementById("route-label").textContent = foundationT("app.message.authorization");
  renderNav(getAuth());
  renderSession(getAuth());
  document.getElementById("view").innerHTML = `<section class="band"><h2>${escapeHtml(foundationT("app.inline.accessDenied"))}</h2><p>${escapeHtml(foundationT("app.inline.thisSessionCannotOpenThatWorkspace"))}</p></section>`;
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
  const keys = {
    Admin: "app.inline.administrator",
    ERPAdmin: "app.inline.eRPAdministrator",
    CLevel: "app.inline.cLevel",
    Accountant: "app.inline.accountant",
    WarehouseClerk: "app.inline.warehouseClerk"
  };
  return keys[role] ? foundationT(keys[role]) : role;
}

function referencePrefix(type) {
  const prefixes = {
    paymentlog: "PAY", paymentsublog: "PAY", cashrecord: "PAY", financialadjustment: "PAY",
    stocktake: "STK", operation: "OP", supplyshipment: "SUP", sku: "SKU", inventorybatch: "BAT",
    stockbalance: "STK", location: "LOC", user: "USR", merchant: "MER",
    notification: "NTF", audit: "AUD", category: "CAT", product: "PRD", brand: "BRD"
  };

  return prefixes[String(type || "").replace(/[^a-z]/gi, "").toLowerCase()] || "REF";
}

async function renderStocktakeSkuSearchResults(row) {
  const input = row.querySelector(".stocktake-line-search");
  const results = row.querySelector(".op-line-search-results");
  const query = input.value.trim().toLowerCase();
  if (!query) { results.hidden = true; results.replaceChildren(); return; }
  const requestId = (skuSearchRequests.get(row) || 0) + 1;
  skuSearchRequests.set(row, requestId);
  let matches = [];
  try { matches = (await searchSkuOptions(query, 20)).slice(0, 8); } catch { matches = []; }
  if (skuSearchRequests.get(row) !== requestId || input.value.trim().toLowerCase() !== query) return;
  results.hidden = false;
  const buttons = (matches.length === 0 ? [null] : matches).map((sku) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "op-line-search-result";
    if (!sku) { button.disabled = true; button.textContent = foundationT("supply.noResults"); return button; }
    button.dataset.stocktakeSkuId = sku.id;
    const name = document.createElement("strong");
    name.textContent = sku.productName;
    const attributes = document.createElement("span");
    attributes.textContent = `${formatOperationPowerKey(operationPowerKey(sku))} / ${sku.colorName || "-"}`;
    const code = document.createElement("small");
    code.textContent = sku.skuCode;
    button.append(name, attributes, code);
    return button;
  });
  results.replaceChildren(...buttons);
  results.querySelectorAll("[data-stocktake-sku-id]").forEach((button) => button.addEventListener("click", () => {
    seedStocktakeLineSkuSelection(row, button.dataset.stocktakeSkuId);
    input.value = "";
    results.hidden = true;
  }));
}

function seedStocktakeLineSkuSelection(row, skuId) {
  const sku = operationSkuOptions.find((value) => value.id === skuId);
  row.querySelector(".stocktake-line-sku").value = skuId || "";
  const resolved = row.querySelector(".stocktake-line-resolved");
  resolved.textContent = sku ? `${sku.skuCode} - ${sku.productName}` : shortId(skuId, "SKU");
  if (!sku && skuId) void ensureSkuOption(skuId).then((loaded) => { if (loaded && row.isConnected) seedStocktakeLineSkuSelection(row, skuId); });
}

function canonicalOperationPayload(body) {
  const type = canonicalSystemValue(body.operationType, "operationType");
  const lines = (body.lines || []).map((line) => {
    const entryMode = canonicalSystemValue(line.entryMode || "Packs", "entryMode");
    const quantity = entryMode === "Pieces" ? Number(line.pieceQuantity ?? line.packQuantity ?? 0) : Number(line.packQuantity ?? 0);
    const bonus = ["WholesaleSale", "RetailSale"].includes(type) && line.isBonus === true;
    return { skuId: line.skuId, section: type === "Change" ? canonicalSystemValue(line.section || "ChangeOut", "lineSection") : "Standard", entryMode, quantity, bonusQuantity: bonus ? quantity : 0, unitPrice: bonus ? 0 : Number(line.unitPrice || 0), lotNumber: String(line.lotNumber || "").trim() || null, expiryDate: line.expiryDate || null, notes: String(line.notes || "").trim() || null };
  }).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
  return JSON.stringify({ operationType: type, sourceLocationId: body.sourceLocationId || null, destinationLocationId: body.destinationLocationId || null, merchantId: body.merchantId || null, buyerName: body.merchantId ? null : String(body.buyerName || "").trim() || null, representativeId: null, paymentMethod: canonicalSystemValue(body.paymentMethod || "", "paymentMethod", { allowEmpty: true }) || null, buyerPhone: String(body.buyerPhone || "").trim() || null, notes: String(body.notes || "").trim() || null, receipt: body.receipt ? { supplierName: String(body.receipt.supplierName || "Supplier").trim() || "Supplier", invoiceNumber: String(body.receipt.invoiceNumber || "").trim() || null } : null, lines });
}

async function deleteAdminUser(userId) {
  const row = [...document.querySelectorAll("[data-admin-user-row]")]
    .find((item) => item.dataset.adminUserRow === userId);
  const username = row?.querySelector("strong")?.textContent || foundationT("admin.thisUser");
  const fullName = row?.querySelector(".muted-text")?.textContent?.trim();
  const accountLabel = fullName ? `${username} (${fullName})` : username;
  if (!await confirmDialog({
    title: foundationT("app.confirm.deleteAccount"),
    message: foundationT("app.confirm.deleteAccountMessage", { account: accountLabel }),
    confirmLabel: "Delete",
    cancelLabel: "Cancel",
    tone: "warning"
  })) return;

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
  const username = row?.querySelector("strong")?.textContent || foundationT("admin.thisUser");
  const action = isActive ? "reactivate" : "deactivate";
  if (!await confirmDialog({
    title: foundationT("app.confirm.accountStatus"),
    message: foundationT(isActive ? "app.confirm.activateAccountMessage" : "app.confirm.deactivateAccountMessage", { account: username }),
    confirmLabel: "Confirm",
    cancelLabel: "Cancel",
    tone: isActive ? "default" : "warning"
  })) return;

  try {
    await request(`/api/v1/users/${encodeURIComponent(userId)}/${isActive ? "activate" : "deactivate"}`, { method: "PATCH", body: JSON.stringify({}) });
    notice(foundationT("admin.userStatusChanged", { user: username, status: uiText(isActive ? "Active" : "Inactive") }), "success");
    await loadAdminUsers();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function transferPrimaryAdmin(userId) {
  const row = [...document.querySelectorAll("[data-admin-user-row]")]
    .find((item) => item.dataset.adminUserRow === userId);
  const username = row?.querySelector("strong")?.textContent || foundationT("admin.thisAdministrator");
  const fullName = row?.querySelector(".muted-text")?.textContent?.trim();
  const accountLabel = fullName ? `${fullName} (${username})` : username;
  if (!await confirmDialog({
    title: foundationT("app.confirm.transferPrimaryAdmin"),
    message: foundationT("app.confirm.transferPrimaryAdminMessage", { account: accountLabel }),
    confirmLabel: "Confirm",
    cancelLabel: "Cancel",
    tone: "warning"
  })) return;

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
      <div class="section-head"><div><h2>${escapeHtml(foundationT("app.systemActivity"))}</h2><p class="muted-text">${escapeHtml(foundationT("app.inline.theTrailRemainsAvailableEvenWhenTheOriginalAccount"))}</p></div><button class="button secondary" id="audit-refresh" type="button">${escapeHtml(foundationT("common.refresh"))}</button></div>
      <div class="form-grid audit-filters">
        <div class="field"><label for="audit-search">${escapeHtml(foundationT("app.findActivity"))}</label><input class="input" id="audit-search" placeholder="${escapeHtml(foundationT("app.activitySearchPlaceholder"))}"></div>
        <div class="field"><label for="audit-section">${escapeHtml(foundationT("app.area"))}</label><select class="select" id="audit-section"><option value="">${escapeHtml(foundationT("app.inline.allAreas"))}</option><option value="User">${escapeHtml(foundationT("app.inline.employeeAccounts"))}</option><option value="Product">${escapeHtml(foundationT("navigation.catalog"))}</option><option value="Operation">${escapeHtml(foundationT("navigation.operations"))}</option><option value="Payment">${escapeHtml(foundationT("navigation.payments"))}</option><option value="SupplyShipment">${escapeHtml(foundationT("navigation.supply"))}</option><option value="Stocktake">${escapeHtml(foundationT("navigation.stocktake"))}</option><option value="ShopifyWebhookEvent">${escapeHtml(foundationT("navigation.integrations"))}</option></select></div>
        <div class="field"><label for="audit-from">${escapeHtml(foundationT("payments.from"))}</label><input class="input" id="audit-from" type="date"></div>
        <div class="field"><label for="audit-to">${escapeHtml(foundationT("payments.to"))}</label><input class="input" id="audit-to" type="date"></div>
      </div>
      <div class="table-wrap"><table><thead><tr><th>${escapeHtml(foundationT("payments.when"))}</th><th>${escapeHtml(foundationT("app.inline.fullNameAndRole"))}</th><th>${escapeHtml(foundationT("app.inline.activity"))}</th><th>${escapeHtml(foundationT("app.record"))}</th><th>${escapeHtml(foundationT("app.area"))}</th><th></th></tr></thead><tbody id="audit-rows"><tr><td colspan="6">${escapeHtml(foundationT("app.inline.loadingAuditHistory"))}</td></tr></tbody></table></div>
      <div class="pagination-bar" id="audit-pagination"></div>
    </section>
    <section class="band" id="audit-detail"><h2>${escapeHtml(foundationT("app.inline.eventDetail"))}</h2><p class="muted-text">${escapeHtml(foundationT("app.inline.selectAnEventToInspectTheRecordedDetails"))}</p></section>`;

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
  rows.innerHTML = `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.loadingAuditHistory"))}</td></tr>`;
  try {
    const result = await request(`/api/v1/audit?${params}`);
    count.textContent = foundationT(result.totalCount === 1 ? "app.count.event" : "app.count.events", { count: result.totalCount });
    rows.innerHTML = result.items.length ? result.items.map((event) => `
      <tr><td>${escapeHtml(formatDateTime(event.happenedAt))}</td><td><strong>${escapeHtml(displaySafeText(event.actorName || foundationT("payments.historicalActorUnavailable"), "USR"))}</strong><br><span class="muted-text">${escapeHtml(auditActorRole(event.actorType))}</span></td><td><strong>${escapeHtml(displaySafeText(auditSummaryText(event), "AUD"))}</strong></td><td>${escapeHtml(displaySafeText(event.recordName || foundationT("payments.relatedRecord"), referencePrefix(event.entityType)))}</td><td>${escapeHtml(auditSectionLabel(event.section))}</td><td><button class="button secondary table-action" type="button" data-audit-detail="${escapeHtml(event.id)}">${escapeHtml(foundationT("app.inline.viewDetails"))}</button><button class="button secondary table-action" type="button" data-audit-source="${escapeHtml(event.id)}">${escapeHtml(foundationT("app.inline.openRecord"))}</button></td></tr>`).join("") : `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.noAuditEventsMatchTheseFilters"))}</td></tr>`;
    rows.querySelectorAll("[data-audit-detail]").forEach((button) => button.addEventListener("click", () => showAuditDetail(button.dataset.auditDetail)));
    rows.querySelectorAll("[data-audit-source]").forEach((button) => button.addEventListener("click", () => openAuditSource(button.dataset.auditSource)));
    renderAuditPagination(result);
  } catch (exception) {
    count.textContent = foundationT("payments.failed");
    rows.innerHTML = `<tr><td colspan="6">${escapeHtml(foundationT("app.inline.couldNotLoadAuditHistory"))}</td></tr>`;
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

function renderAuditPagination(result) {
  const area = document.getElementById("audit-pagination");
  if (!area) return;
  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
  area.innerHTML = `<span>${escapeHtml(foundationT("pagination.pageOf", { page: result.page, pages: totalPages }))}</span><div><button class="button secondary table-action" type="button" id="audit-previous" ${result.page <= 1 ? "disabled" : ""}>${escapeHtml(foundationT("pagination.previous"))}</button><button class="button secondary table-action" type="button" id="audit-next" ${result.page >= totalPages ? "disabled" : ""}>${escapeHtml(foundationT("pagination.next"))}</button></div>`;
  document.getElementById("audit-previous")?.addEventListener("click", () => { auditPageState.page -= 1; loadAuditHistory(); });
  document.getElementById("audit-next")?.addEventListener("click", () => { auditPageState.page += 1; loadAuditHistory(); });
}

async function showAuditDetail(id) {
  const detail = document.getElementById("audit-detail");
  if (!detail) return;
  detail.innerHTML = `<h2>${escapeHtml(foundationT("app.inline.eventDetail"))}</h2><p>${escapeHtml(foundationT("app.inline.loadingEvent"))}</p>`;
  try {
    const event = await request(`/api/v1/audit/${encodeURIComponent(id)}`);
    const changes = Array.isArray(event.changes) ? event.changes : [];
    const savedValues = changes.length ? `<div class="audit-change-list">${changes.map((change) => `<article class="audit-change"><strong>${escapeHtml(change.field)}</strong>${change.before ? `<span>${escapeHtml(foundationT("audit.was"))}: ${escapeHtml(displaySafeText(change.before, "AUD"))}</span>` : ""}<span>${escapeHtml(foundationT(change.before ? "audit.now" : "audit.saved"))}: ${escapeHtml(displaySafeText(change.after || foundationT("audit.cleared"), "AUD"))}</span></article>`).join("")}</div>` : `<p class="muted-text audit-empty-values">${escapeHtml(foundationT("app.inline.noIndividualFieldValuesWereSavedForThisEvent"))}</p>`;
    detail.innerHTML = `<div class="section-head"><div><p class="eyebrow">${escapeHtml(foundationT("app.recordedActivity"))}</p><h2>${escapeHtml(auditSummaryText(event))}</h2><p class="muted-text">${escapeHtml(formatDateTime(event.happenedAt))} ${escapeHtml(foundationT("app.by"))} ${escapeHtml(event.actorName)}</p></div><button class="button secondary" type="button" id="audit-open-detail-source">${escapeHtml(foundationT("app.openRelatedRecord"))}</button></div><div class="detail-grid"><div><span>${escapeHtml(foundationT("app.record"))}</span><strong>${escapeHtml(event.recordName || foundationT("payments.relatedRecord"))}</strong></div><div><span>${escapeHtml(foundationT("app.performedBy"))}</span><strong>${escapeHtml(event.actorName || foundationT("payments.historicalActorUnavailable"))} · ${escapeHtml(auditActorRole(event.actorType))}</strong></div><div><span>${escapeHtml(foundationT("app.area"))}</span><strong>${escapeHtml(auditSectionLabel(event.section))}</strong></div><div><span>${escapeHtml(foundationT("payments.scope"))}</span><strong>${escapeHtml(event.entityType ? uiText(event.entityType) : foundationT("app.systemActivity"))}</strong></div><div><span>${escapeHtml(foundationT("app.time"))}</span><strong>${escapeHtml(formatDateTime(event.happenedAt))}</strong></div>${event.stockDeltaApplied == null ? "" : `<div><span>${escapeHtml(foundationT("app.stockChange"))}</span><strong>${escapeHtml(String(event.stockDeltaApplied))}</strong></div>`}</div><section class="audit-saved-values"><h3>${escapeHtml(foundationT("app.savedValues"))}</h3><p class="muted-text">${escapeHtml(foundationT("app.activityValueExplanation"))}</p>${savedValues}</section>`;
    document.getElementById("audit-open-detail-source")?.addEventListener("click", () => openAuditSource(event.id));
  } catch (exception) {
    detail.innerHTML = `<h2>${escapeHtml(foundationT("app.inline.eventDetail"))}</h2><p class="form-error">${escapeHtml(getFriendlyWorkspaceError(exception))}</p>`;
  }
}

function auditSummaryFallback(event) {
  return `${String(event.action || "Changed").replace(/([a-z])([A-Z])/g, "$1 $2")} ${String(event.entityType || "record").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase()}.`;
}

function auditSummaryText(event) {
  const summary = String(event.summary || auditSummaryFallback(event));
  const employeeCreated = summary.match(/^Created employee account (.+)\.$/);
  if (employeeCreated && document.documentElement.dir === "rtl") {
    return foundationT("audit.employeeCreated", { employee: employeeCreated[1] });
  }
  return currentLanguage === "ar" ? (arabicTranslations[summary] || summary) : summary;
}

function auditSectionLabel(section) {
  const keys = { admin: "routes.admin.title", catalog: "routes.catalog.title", crm: "routes.crm.title", inventory: "routes.inventory.title", operations: "routes.operations.title", payments: "routes.payments.title", supply: "routes.supply.title", stocktakes: "routes.stocktakes.title", notifications: "routes.notifications.title", integrations: "routes.integrations.title", reports: "routes.reports.title", dashboard: "app.inline.system" };
  return foundationT(keys[String(section || "").toLowerCase()] || "app.inline.system");
}

function auditActorRole(actorType) {
  const value = String(actorType || "").trim();
  return value && value !== "User" ? roleLabel(value) : foundationT("audit.roleNotRecorded");
}

async function openAuditSource(auditEventId) {
  try {
    const destination = await request(`/api/v1/audit/${encodeURIComponent(auditEventId)}/navigation-reference`);
    location.hash = `${destination.route}?ref=${encodeURIComponent(destination.navigationReference)}`;
  } catch {
    notice(foundationT("app.message.theRelatedRecordIsUnavailableOrNoLongerPermitted"), "warning");
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
      <div class="integration-command-copy"><span class="eyebrow">${escapeHtml(foundationT("app.inline.deliveryQueue"))}</span><h2>${escapeHtml(foundationT("app.inline.protectTheCommercialRecordAllocateStockOnlyAfterReview"))}</h2><p>${escapeHtml(foundationT("app.inline.webhookContentIsNeverShownHereTemporaryLegacyPath"))}</p></div>
      <button id="shopify-refresh" class="button primary" type="button">${escapeHtml(foundationT("app.inline.refreshIntake"))}</button>
    </section>
    <section class="band">
      <div class="section-head tight-head"><div><h2>${escapeHtml(foundationT("app.inline.integrationEvents"))}</h2><p>${escapeHtml(foundationT("app.inline.queuedEventsProcessAutomaticallyExceptionsRequireADeliberateRetry"))}</p></div><select id="shopify-event-status" class="select compact-select"><option value="">${escapeHtml(foundationT("app.inline.allStates"))}</option><option value="Queued">${escapeHtml(foundationT("app.inline.queued"))}</option><option value="Processing">${escapeHtml(foundationT("app.inline.processing"))}</option><option value="Retrying">${escapeHtml(foundationT("app.inline.retrying"))}</option><option value="RequiresAttention">${escapeHtml(foundationT("app.inline.needsReview"))}</option><option value="Resolved">${escapeHtml(foundationT("app.inline.resolved"))}</option><option value="Succeeded">${escapeHtml(foundationT("app.inline.succeeded"))}</option><option value="Imported">${escapeHtml(foundationT("app.inline.imported"))}</option></select></div>
      <div id="shopify-event-list" class="integration-event-list">${escapeHtml(foundationT("app.inline.loadingIntegrationEvents"))}</div>
    </section>
    <section class="band">
      <div class="section-head tight-head"><div><h2>${escapeHtml(foundationT("app.inline.eRPSKUsForShopify"))}</h2><p>${escapeHtml(foundationT("app.inline.copyTheERPSKUIntoEachShopifyVariantOrders"))}</p></div></div>
      <div class="integration-mapping-form">
        <div class="field"><label for="shopify-sku-search">${escapeHtml(foundationT("app.inline.findSKUOrProduct"))}</label><input id="shopify-sku-search" class="input" autocomplete="off" placeholder="${escapeHtml(foundationT("app.attribute.sKUOrProductName"))}"></div>
        <div class="field"><label for="shopify-sku-product">${escapeHtml(foundationT("app.product"))}</label><select id="shopify-sku-product" class="select"><option value="">${escapeHtml(foundationT("app.inline.allCatalogProducts"))}</option></select></div>
        <div class="field"><label for="shopify-sku-wear-cycle">${escapeHtml(foundationT("app.wearCycle"))}</label><select id="shopify-sku-wear-cycle" class="select"><option value="">${escapeHtml(foundationT("app.inline.allWearCycles"))}</option><option value="Daily">${escapeHtml(foundationT("finance.executive.daily"))}</option><option value="Monthly">${escapeHtml(foundationT("finance.executive.monthly"))}</option><option value="Annual">${escapeHtml(foundationT("app.inline.annual"))}</option></select></div>
        <div class="field"><label for="shopify-sku-status">${escapeHtml(foundationT("app.readiness"))}</label><select id="shopify-sku-status" class="select"><option value="">${escapeHtml(foundationT("app.inline.allActiveSKUs"))}</option><option value="Ready">${escapeHtml(foundationT("app.inline.readyToPublish"))}</option><option value="NeedsWearCycle">${escapeHtml(foundationT("app.inline.setLensCycle"))}</option><option value="PieceSaleDisabled">${escapeHtml(foundationT("app.inline.pieceSaleDisabled"))}</option><option value="UnsupportedProduct">${escapeHtml(foundationT("app.inline.unsupportedProduct"))}</option></select></div>
        <button id="shopify-sku-search-button" class="button secondary" type="button">${escapeHtml(foundationT("app.inline.checkCatalog"))}</button>
      </div>
      <div id="shopify-sku-readiness" class="table-wrap compact-table">${escapeHtml(foundationT("app.inline.loadingERPSKUReadiness"))}</div>
      <div class="pagination-bar" id="shopify-sku-pagination" hidden>
        <label class="muted-text" for="shopify-sku-page-size">${escapeHtml(foundationT("app.inline.rowsPerPage"))}</label>
        <select id="shopify-sku-page-size" class="select compact-select" aria-label="${escapeHtml(foundationT("app.attribute.eRPSKUsPerPage"))}">
          <option value="50">50</option>
          <option value="100">100</option>
        </select>
        <span class="muted-text" id="shopify-sku-page-label">${escapeHtml(foundationT("app.inline.showing0ERPSKUs"))}</span>
        <button class="button secondary table-action" type="button" id="shopify-sku-prev">${escapeHtml(foundationT("pagination.previous"))}</button>
        <button class="button secondary table-action" type="button" id="shopify-sku-next">${escapeHtml(foundationT("pagination.next"))}</button>
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
    receiver.textContent = foundationT(status.isConfigured ? "app.signedReceiverReady" : (status.isLegacyWebhookConfigured ? "app.temporaryLegacyReceiver" : "app.configurationRequired"));
    receiver.className = `status-pill ${status.isConfigured ? "status-ok" : "status-warn"}`;
  } catch (exception) {
    receiver.textContent = foundationT("app.message.unavailable");
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
    count.textContent = foundationT(result.totalCount === 1 ? "app.count.event" : "app.count.events", { count: result.totalCount });
    list.innerHTML = result.items.length === 0 ? `<div class="empty-state">${escapeHtml(foundationT("app.inline.noShopifyEventsMatchThisView"))}</div>` : result.items.map(renderShopifyEvent).join("");
    list.querySelectorAll("[data-shopify-retry]").forEach((button) => button.addEventListener("click", () => retryShopifyEvent(button.dataset.shopifyRetry)));
    list.querySelectorAll("[data-shopify-resolve]").forEach((button) => button.addEventListener("click", () => resolveShopifyEvent(button.dataset.shopifyResolve)));
  } catch (exception) {
    count.textContent = foundationT("app.message.unavailable");
    list.innerHTML = `<div class="empty-state">${escapeHtml(getFriendlyWorkspaceError(exception))}</div>`;
  }
}

function renderShopifyEvent(event) {
  const statusClass = event.status === "RequiresAttention" ? "status-warn" : (event.status === "Imported" || event.status === "Succeeded" ? "status-ok" : "status-muted");
  const canManage = ["Admin", "ERPAdmin"].includes(getAuth()?.user?.role) || (getAuth()?.user?.role === "WarehouseClerk" && getAuth()?.user?.locationType === "Online");
  const actions = canManage && event.status === "RequiresAttention"
    ? `<button class="button secondary table-action" type="button" data-shopify-retry="${escapeHtml(event.id)}" ${event.payloadAvailable ? "" : "disabled"}>${escapeHtml(foundationT("app.inline.retry"))}</button><button class="button secondary table-action" type="button" data-shopify-resolve="${escapeHtml(event.id)}">${escapeHtml(foundationT("app.inline.resolve"))}</button>`
    : "";
  const trust = event.verificationMode === "Hmac" ? "Signed HMAC" : "Temporary legacy path";
  const statusLabel = { RequiresAttention: "Needs review", Queued: "Queued", Processing: "Processing", Retrying: "Retrying", Resolved: "Resolved", Succeeded: "Succeeded", Imported: "Imported" }[event.status] || event.status;
  const visibleStatus = document.documentElement.dir === "rtl" ? uiText(statusLabel) : event.status;
  return `<article class="integration-event-card"><div class="integration-event-main"><div><div class="notification-title-row"><span class="status-pill ${statusClass}">${escapeHtml(visibleStatus)}</span><strong>${escapeHtml(event.topic)}</strong><span class="muted-text">${escapeHtml(formatDateTime(event.receivedAt))}</span></div><p>${escapeHtml(uiText(event.detail || "Delivery accepted for processing."))}</p></div><div class="integration-event-actions">${event.operationId ? `<a class="button secondary table-action" href="#/operations">${escapeHtml(foundationT("app.openOperation"))}${event.shopifyOrderId ? ` · Shopify ${escapeHtml(event.shopifyOrderId)}` : ""}</a>` : ""}${actions}</div></div><dl class="integration-event-facts"><div><dt>${escapeHtml(foundationT("app.trust"))}</dt><dd>${escapeHtml(uiText(trust))}</dd></div><div><dt>${escapeHtml(foundationT("payments.order"))}</dt><dd>${escapeHtml(event.shopifyOrderId || foundationT("app.notParsed"))}</dd></div><div><dt>${escapeHtml(foundationT("app.store"))}</dt><dd>${escapeHtml(event.shopDomain)}</dd></div><div><dt>${escapeHtml(foundationT("app.attempts"))}</dt><dd>${escapeHtml(event.attemptCount)}</dd></div><div><dt>${escapeHtml(foundationT("app.payload"))}</dt><dd>${event.payloadAvailable ? escapeHtml(foundationT("app.retainedSecurely")) : escapeHtml(foundationT("app.retentionExpired"))}</dd></div>${event.resolutionNote ? `<div><dt>${escapeHtml(foundationT("app.resolution"))}</dt><dd>${escapeHtml(event.resolutionNote)}</dd></div>` : ""}</dl></article>`;
}

async function retryShopifyEvent(id) {
  try {
    await request(`/api/v1/integrations/shopify/events/${id}/retry`, { method: "POST" });
    notice(foundationT("app.message.shopifyEventQueuedForRetry"), "success");
    await loadShopifyEvents();
  } catch (exception) {
    notice(getFriendlyWorkspaceError(exception), "error");
  }
}

async function resolveShopifyEvent(id) {
  const note = await promptDialog({
    title: foundationT("app.prompt.resolveShopifyEvent"),
    label: foundationT("app.prompt.resolutionNote"),
    multiline: true,
    required: true
  });
  if (!note?.trim()) return;
  try {
    await request(`/api/v1/integrations/shopify/events/${id}/resolve`, { method: "POST", body: JSON.stringify({ note: note.trim() }) });
    notice(foundationT("app.message.shopifyEventResolved"), "success");
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
    select.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.allCatalogProducts"))}</option>${products.map((product) => `<option value="${escapeHtml(product.id)}">${escapeHtml(product.name)}</option>`).join("")}`;
    select.value = products.some((product) => product.id === selected) ? selected : "";
  } catch {
    select.innerHTML = `<option value="">${escapeHtml(foundationT("app.inline.productListUnavailable"))}</option>`;
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
    list.innerHTML = `<table><thead><tr><th>${escapeHtml(foundationT("app.erpSku"))}</th><th>${escapeHtml(foundationT("app.productAttributes"))}</th><th>${escapeHtml(foundationT("app.wearCycle"))}</th><th>${escapeHtml(foundationT("app.piecesPerPack"))}</th><th>${escapeHtml(foundationT("app.sellMode"))}</th><th>${escapeHtml(foundationT("app.readiness"))}</th><th></th></tr></thead><tbody>${result.items.length === 0 ? `<tr><td colspan="7">${escapeHtml(foundationT("app.noActiveErpSkus"))}</td></tr>` : result.items.map((sku) => `<tr><td><strong>${escapeHtml(sku.skuCode)}</strong></td><td>${escapeHtml(sku.productName)}<div class="muted-cell">${escapeHtml([formatPower(sku), sku.colorName, sku.size].filter(Boolean).join(" / ") || foundationT("app.noVariantAttributes"))}</div></td><td>${renderWearCycle(sku.wearCycle, sku.wearDuration)}</td><td>${escapeHtml(sku.piecesPerPack || "-")}</td><td>${escapeHtml(sku.sellMode || foundationT("app.notSet"))}</td><td>${renderShopifySkuReadiness(sku.status)}</td><td><button class="button secondary table-action" type="button" data-copy-shopify-sku="${escapeHtml(sku.skuCode)}">${escapeHtml(foundationT("app.copySku"))}</button></td></tr>`).join("")}</tbody></table>`;
    if (pagination && pageLabel && previous && next) {
      const first = result.totalCount === 0 ? 0 : ((shopifySkuPageState.page - 1) * result.pageSize) + 1;
      const last = Math.min(shopifySkuPageState.page * result.pageSize, result.totalCount);
      pagination.hidden = result.totalCount <= result.pageSize;
      pageLabel.textContent = foundationT("integrations.skuPage", { from: first, to: last, total: result.totalCount, page: shopifySkuPageState.page, pages: totalPages });
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
  if (status === "Ready") return `<span class="status-pill status-ok">${escapeHtml(foundationT("supply.ready"))}</span>`;
  if (status === "NeedsWearCycle") return `<span class="status-pill status-warn">${escapeHtml(foundationT("app.inline.setLensCycle"))}</span>`;
  if (status === "PieceSaleDisabled") return `<span class="status-pill status-warn">${escapeHtml(foundationT("app.inline.enablePieceSales"))}</span>`;
  return `<span class="status-pill status-muted">${escapeHtml(foundationT("app.inline.lensProductsOnly"))}</span>`;
}

async function copyShopifySku(sku) {
  try {
    await navigator.clipboard.writeText(sku);
    notice(foundationT("app.message.eRPSKUCopiedPasteItIntoTheShopifyVariant"), "success");
  } catch {
    notice(foundationT("app.message.couldNotCopyTheSKUCopyItManuallyFrom"), "error");
  }
}

function formatPackHint(product) {
  const sellMode = product.sellMode ? uiText(product.sellMode) : foundationT("app.catalogPack");
  const pieces = foundationT("app.inline.pieces");
  return product.piecesPerPack
    ? `${escapeHtml(sellMode)} / ${escapeHtml(product.piecesPerPack)} ${escapeHtml(pieces)}`
    : escapeHtml(product.sellMode ? uiText(product.sellMode) : "-");
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
    return foundationT("errors.login.apiUnavailable");
  }
  if (message.includes("401") || message.includes("Unauthorized")) {
    return foundationT("errors.login.invalidCredentials");
  }
  if (message.includes("28P01")) {
    return foundationT("errors.login.databaseUnavailable");
  }
  return foundationT("errors.login.failed");
}

function getFriendlyApiError(exception) {
  const status = exception?.status;
  if (status === 401) {
    return foundationT("errors.sessionExpired");
  }
  if (status === 403) {
    return foundationT("errors.catalog.forbidden");
  }
  return foundationT("errors.catalog.loadFailed");
}

function getFriendlyCatalogWriteError(exception) {
  const message = exception instanceof Error ? exception.message : "";
  if (message.includes("errors")) {
    try {
      const body = JSON.parse(message);
      return Object.values(body.errors || {}).flat().join(" ") || foundationT("errors.catalog.invalidForm");
    } catch {
      return foundationT("errors.catalog.invalidForm");
    }
  }
  if (exception?.status === 409 || message.includes("Conflict")) {
    return foundationT("errors.catalog.duplicateSku");
  }
  if (exception?.status === 403) {
    return foundationT("errors.catalog.writeForbidden");
  }
  return foundationT("errors.catalog.changeFailed");
}

function getFriendlyInventoryError(exception) {
  const problem = parseProblemDetails(exception);
  if (problem) {
    return problem;
  }
  const status = exception?.status;
  if (status === 401) {
    return foundationT("errors.sessionExpired");
  }
  if (status === 403) {
    return foundationT("errors.inventory.forbidden");
  }
  if (status === 400) {
    return foundationT("errors.inventory.invalidRequest");
  }
  return foundationT("errors.inventory.loadFailed");
}

function getFriendlyWorkspaceError(exception) {
  const problem = parseProblemDetails(exception);
  if (problem) {
    return problem;
  }
  if (exception?.status === 401) {
    return foundationT("errors.sessionExpired");
  }
  if (exception?.status === 403) {
    return foundationT("errors.workspace.forbidden");
  }
  if (exception?.status === 400) {
    return foundationT("errors.workspace.invalidRequest");
  }
  return foundationT("errors.workspace.requestFailed");
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

function wireOperationLineEditor() {
  const container = document.getElementById("op-lines");
  if (!container || container.dataset.delegated === "true") return;
  container.dataset.delegated = "true";

  container.addEventListener("input", (event) => {
    const input = event.target.closest?.(".op-line-search");
    if (!input) return;
    const row = input.closest(".line-editor-row");
    if (!row) return;
    clearTimeout(operationSearchTimers.get(input));
    operationSearchTimers.set(input, setTimeout(() => { void renderOperationSkuSearchResults(row); }, 250));
  });

  container.addEventListener("change", async (event) => {
    const control = event.target;
    const row = control.closest?.(".line-editor-row");
    if (!row) return;
    if (control.matches(".op-line-bonus, .op-line-section, .op-line-entry-mode")) {
      syncOperationLineControls(document.getElementById("op-type")?.value || operationsUiState.operationType);
      await refreshOperationStockOptions(row);
      return;
    }
    if (control.matches(".op-line-product")) {
      await loadProductSkuOptions(control.value);
      populateOperationAttributeOptions(row);
      resolveOperationLineSku(row);
      return;
    }
    if (control.matches(".op-line-power, .op-line-color, .op-line-size")) {
      resolveOperationLineSku(row);
      return;
    }
    if (control.matches(".op-line-stock-option")) applySelectedStockOption(row);
  });

  container.addEventListener("focusin", async (event) => {
    const product = event.target.closest?.(".op-line-product");
    if (product && operationProductOptions.length === 0) {
      product.disabled = true;
      product.innerHTML = `<option value="">${foundationT("supply.loadingProducts")}</option>`;
      try {
        await hydrateOperationSkus();
        const row = product.closest(".line-editor-row");
        if (row?.isConnected) populateOperationProductOptions(row);
      } finally {
        if (product.isConnected) product.disabled = false;
      }
      return;
    }
    const stockOption = event.target.closest?.(".op-line-stock-option");
    const row = stockOption?.closest(".line-editor-row");
    if (row) void refreshOperationStockOptions(row);
  });

  container.addEventListener("click", (event) => {
    const remove = event.target.closest?.(".op-remove-line");
    if (!remove) return;
    const row = remove.closest(".line-editor-row");
    if (!row || operationEditorLines.length <= 1) return;
    syncCurrentOperationPage();
    operationEditorLines = operationEditorLines.filter((item) => item._clientId !== row.dataset.operationLineKey);
    operationEditorLineById.delete(row.dataset.operationLineKey);
    operationEditorPage = Math.min(operationEditorPage, Math.max(1, Math.ceil(operationEditorLines.length / operationEditorPageSize)));
    renderOperationEditorPage();
  });
}
