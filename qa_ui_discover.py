import os
from playwright.sync_api import sync_playwright


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1440, "height": 1100})
    page.goto("http://localhost:3001/", wait_until="domcontentloaded")
    try:
        page.wait_for_load_state("networkidle", timeout=5000)
    except Exception:
        # The live ERP keeps background health activity open; the rendered login form is the stable readiness signal.
        page.locator("#login-form").wait_for()
    page.locator("#username").fill("admin")
    page.locator("#password").fill(os.environ["QA_TEST_PASSWORD"])
    page.locator("#login-submit").click()
    page.wait_for_timeout(500)
    page.locator('a[href="#/catalog"]').first.click()
    page.wait_for_timeout(500)

    category_name = "QA Automation Category 20260804"
    brand_name = "QA Automation Brand 20260804"
    product_name = "QA Trace Lens 20260804"

    page.locator("#category-name").fill(category_name)
    page.locator("#category-submit").click()
    page.locator("#product-category option").filter(has_text=category_name).wait_for()

    page.locator("#brand-name").fill(brand_name)
    page.locator("#brand-submit").click()
    page.locator("#product-brand option").filter(has_text=brand_name).wait_for()

    page.locator("#product-name").fill(product_name)
    page.locator("#product-type").select_option("Lens")
    page.locator("#product-category").select_option(label=category_name)
    page.locator("#product-brand").select_option(label=brand_name)
    page.locator("#product-sell-mode").select_option("Both")
    page.locator("#product-pieces").fill("1")
    page.locator("#product-expiry").select_option("Batch")
    page.locator("#product-duration-value").fill("6")
    page.locator("#product-duration-unit").select_option("Monthly")
    page.locator("#product-submit").click()
    page.get_by_text(product_name, exact=True).wait_for()
    page.screenshot(path="qa_catalog_after_create.png", full_page=True)
    print("PRODUCT_CREATED", product_name)
    print(page.locator("body").inner_text())

    browser.close()
