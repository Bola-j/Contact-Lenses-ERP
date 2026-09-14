const { test, expect } = require("@playwright/test");

test("mobile: Arabic login is RTL, localized, and free of horizontal overflow", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("lensee.language", "ar"));
  await page.goto("/#/login", { waitUntil: "domcontentloaded" });

  await expect(page.locator("html")).toHaveAttribute("lang", "ar-EG");
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page.locator("#login-form")).toContainText("تسجيل الدخول");
  await expect(page.locator("label[for='username']")).toHaveText("اسم المستخدم");
  await expect(page.locator("label[for='password']")).toHaveText("كلمة المرور");

  const hasHorizontalOverflow = await page.evaluate(() =>
    document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(hasHorizontalOverflow).toBe(false);

  await page.locator("#login-language-toggle").click();
  await expect(page.locator("html")).toHaveAttribute("dir", "ltr");
  await expect(page.locator("#login-form")).toContainText("Sign in");
});
