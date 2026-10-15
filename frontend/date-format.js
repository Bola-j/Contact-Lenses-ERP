const DATE_ONLY_FORMAT = /^([0-3]\d)\/([01]\d)\/(\d{4})$/;
const DATE_TIME_FORMAT = /^([0-3]\d)\/([01]\d)\/(\d{4}) ([0-2]\d):([0-5]\d):([0-5]\d)$/;
const ISO_DATE_FORMAT = /^(\d{4})-(\d{2})-(\d{2})/;

function validParts(year, month, day) {
  if (!Number.isInteger(year) || !Number.isInteger(month) || !Number.isInteger(day)) return false;
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

export function parseDateOnly(value) {
  const input = String(value ?? "").trim();
  if (!input) return null;
  let match = DATE_ONLY_FORMAT.exec(input);
  if (match) {
    const [, dayText, monthText, yearText] = match;
    const parts = { day: Number(dayText), month: Number(monthText), year: Number(yearText) };
    return validParts(parts.year, parts.month, parts.day) ? parts : null;
  }
  match = ISO_DATE_FORMAT.exec(input);
  if (!match) return null;
  const [, yearText, monthText, dayText] = match;
  const parts = { day: Number(dayText), month: Number(monthText), year: Number(yearText) };
  return validParts(parts.year, parts.month, parts.day) ? parts : null;
}

export function formatDateOnly(value) {
  const parts = parseDateOnly(value);
  return parts ? `${String(parts.day).padStart(2, "0")}/${String(parts.month).padStart(2, "0")}/${String(parts.year).padStart(4, "0")}` : String(value ?? "");
}

export function formatDateTime(value) {
  if (!value) return "-";
  const input = String(value).trim();
  const formattedDateTime = /^([0-3]\d\/[01]\d\/\d{4}) ([0-2]\d:[0-5]\d:[0-5]\d)$/.exec(input);
  if (formattedDateTime) return input;
  const parts = parseDateOnly(input);
  if (!parts) return input;
  const dateText = formatDateOnly(input);
  const date = new Date(input);
  if (Number.isNaN(date.getTime())) return dateText;
  const time = `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}:${String(date.getSeconds()).padStart(2, "0")}`;
  return input.length <= 10 ? dateText : `${dateText} ${time}`;
}

export function isValidDateOnly(value) {
  return !String(value ?? "").trim() || parseDateOnly(value) !== null;
}

export function isValidDateTime(value) {
  const input = String(value ?? "").trim();
  if (!input) return true;
  const match = DATE_TIME_FORMAT.exec(input);
  if (!match) return false;
  const [, day, month, year, hour] = match;
  return Number(hour) <= 23 && validParts(Number(year), Number(month), Number(day));
}
