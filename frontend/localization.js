const UUID_SOURCE = "[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}";

export const uuidPattern = new RegExp(`^${UUID_SOURCE}$`, "i");
export const visibleUuidPattern = new RegExp(`\\b${UUID_SOURCE}\\b`, "gi");

export function interpolate(message, parameters = {}) {
  return String(message ?? "").replace(/\{([A-Za-z0-9_]+)\}/g, (match, key) =>
    Object.prototype.hasOwnProperty.call(parameters, key) ? String(parameters[key]) : match);
}

export function createLocalizer({ messages = {}, language = () => "en" } = {}) {
  const text = (key, parameters = {}) => {
    const locale = language() === "ar" ? "ar" : "en";
    const translated = locale === "ar" ? messages[key] : key;
    return interpolate(translated ?? key, parameters);
  };
  const locale = () => language() === "ar" ? "ar-EG" : "en-US";
  return Object.freeze({
    text,
    number: (value, options) => new Intl.NumberFormat(locale(), options).format(value),
    date: (value, options = { dateStyle: "medium", timeStyle: "short" }) =>
      new Intl.DateTimeFormat(locale(), options).format(value instanceof Date ? value : new Date(value)),
    bind(root) {
      root?.querySelectorAll("[data-i18n]").forEach((element) => {
        element.textContent = text(element.dataset.i18n);
      });
      for (const attribute of ["placeholder", "title", "aria-label"]) {
        root?.querySelectorAll(`[data-i18n-${attribute}]`).forEach((element) => {
          element.setAttribute(attribute, text(element.getAttribute(`data-i18n-${attribute}`)));
        });
      }
    }
  });
}

export const canonicalValueSets = Object.freeze({
  productType: Object.freeze(["Lens", "Solution"]),
  sellMode: Object.freeze(["SinglePiece", "SealedPackOnly", "Both"]),
  expiryType: Object.freeze(["Batch", "None"]),
  durationUnit: Object.freeze(["Daily", "Monthly", "Annual"]),
  businessType: Object.freeze(["Merchant", "Pharmacy", "Oculist", "BeautyCenter", "Other"]),
  operationType: Object.freeze(["InventoryReceipt", "WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve", "Return", "Change", "WriteOff"]),
  paymentMethod: Object.freeze(["CashHandToHand", "CashTransaction", "MerchantAccount"]),
  movementMethod: Object.freeze(["CashHandToHand", "CashTransaction", "BankTransfer", "Wallet"]),
  entryMode: Object.freeze(["Packs", "Pieces"]),
  lineSection: Object.freeze(["Standard", "ChangeOut", "ChangeIn"]),
  paymentType: Object.freeze(["CashReceived", "CashRefund"]),
  adjustmentType: Object.freeze(["AdditionalCharge", "BalanceReduction", "CashRefund"]),
  supplyCostType: Object.freeze(["Customs", "Freight", "Clearance", "Handling", "Insurance", "Other"]),
  role: Object.freeze(["Admin", "ERPAdmin", "CLevel", "Accountant", "WarehouseClerk"]),
  locationType: Object.freeze(["MainWarehouse", "SubWarehouse", "Retail", "Online"]),
  thresholdUnit: Object.freeze(["Days", "Months", "Years"])
});

const allCanonicalValues = new Set(Object.values(canonicalValueSets).flat());

export function canonicalSystemValue(value, domain = null, { allowEmpty = false } = {}) {
  const text = String(value ?? "").trim();
  if (!text && allowEmpty) return "";
  const allowed = domain ? canonicalValueSets[domain] : allCanonicalValues;
  const isAllowed = allowed instanceof Set ? allowed.has(text) : allowed?.includes(text);
  if (!isAllowed) {
    throw new Error(`Non-canonical system value${domain ? ` for ${domain}` : ""}.`);
  }
  return text;
}

export function canonicalSelectValue(root, id, domain, options = {}) {
  const element = root.getElementById(id);
  return canonicalSystemValue(element?.value, domain, options);
}

const hiddenReferenceLabels = Object.freeze({
  en: Object.freeze({
    REF: "Related record",
    AUD: "Audit record",
    BAT: "Inventory batch",
    BRD: "Brand",
    CAT: "Category",
    LOC: "Location",
    MER: "Merchant",
    NTF: "Notification",
    OP: "Operation",
    PAY: "Payment record",
    PRD: "Product",
    SKU: "SKU",
    STK: "Stocktake session",
    SUP: "Supply shipment",
    USR: "Employee"
  }),
  ar: Object.freeze({
    REF: "السجل المرتبط",
    AUD: "سجل التدقيق",
    BAT: "دفعة مخزون",
    BRD: "العلامة التجارية",
    CAT: "التصنيف",
    LOC: "الموقع",
    MER: "التاجر",
    NTF: "التنبيه",
    OP: "العملية",
    PAY: "سجل الدفع",
    PRD: "المنتج",
    SKU: "رمز الصنف",
    STK: "جلسة الجرد",
    SUP: "شحنة التوريد",
    USR: "الموظف"
  })
});

export function contextualReference({ businessReference, context, prefix = "REF", language = "en" } = {}) {
  const preferred = String(businessReference ?? "").trim();
  if (preferred && !uuidPattern.test(preferred)) return preferred;
  const meaningfulContext = String(context ?? "").trim();
  if (meaningfulContext && !uuidPattern.test(meaningfulContext)) return meaningfulContext;
  const locale = language === "ar" ? "ar" : "en";
  return hiddenReferenceLabels[locale][String(prefix || "REF").toUpperCase()] || hiddenReferenceLabels[locale].REF;
}

export function sanitizeVisibleText(value, { prefix = "REF", language = "en", context = "" } = {}) {
  const replacement = contextualReference({ prefix, language, context });
  return String(value ?? "").replace(visibleUuidPattern, replacement);
}

export function findVisibleUuidLeaks(root) {
  if (!root || !root.ownerDocument?.createTreeWalker) return [];
  const leaks = [];
  const walker = root.ownerDocument.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    visibleUuidPattern.lastIndex = 0;
    if (visibleUuidPattern.test(walker.currentNode.nodeValue || "")) leaks.push(walker.currentNode);
  }
  visibleUuidPattern.lastIndex = 0;
  return leaks;
}
