import { expect, test, type Page } from '@playwright/test';

async function expectNoHorizontalOverflow(page: Page) {
    const widths = await page.evaluate(() => ({
        viewport: window.innerWidth,
        document: document.documentElement.scrollWidth,
    }));
    expect(widths.document).toBeLessThanOrEqual(widths.viewport + 1);
}

test('browses the real catalog and preserves the selected currency', async ({ page }, testInfo) => {
    await page.goto('/?currency=USD');
    await expect(page.getByRole('heading', { name: /Everything useful/ })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Shop by category' })).toBeVisible();
    await expect(page.locator('.product-card')).not.toHaveCount(0);
    await expect(page.locator('.product-card__price').first()).toContainText('USD');
    await expect
        .poll(() =>
            page
                .locator('.product-card__image img')
                .first()
                .evaluate((image) => (image as HTMLImageElement).naturalWidth),
        )
        .toBeGreaterThan(0);
    await expectNoHorizontalOverflow(page);

    if (testInfo.project.name !== 'desktop-chromium') {
        await page.getByRole('button', { name: 'Open navigation' }).click();
        await expect(page.getByRole('navigation', { name: 'Shop categories' })).toBeVisible();
        await page.getByRole('button', { name: 'Close navigation' }).click();
    }

    await page.locator('.product-card__image').first().click();
    await expect(page).toHaveURL(/\/products\/[^?]+\?currency=USD/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('.purchase-price')).toContainText('USD');
    await expectNoHorizontalOverflow(page);
});

test('filters, sorts, and searches through URL-addressable listing state', async ({ page }) => {
    await page.goto('/products?currency=USD&sort=PRICE_ASC');
    await expect(page.getByRole('heading', { name: 'Explore all products' })).toBeVisible();
    await expect(page.locator('.product-card')).toHaveCount(24);
    await expect(page.getByLabel('Sort by')).toHaveValue('PRICE_ASC');

    await page.getByLabel('Search products').fill('Notebook');
    await page.getByRole('button', { name: 'Search' }).click();
    await expect(page).toHaveURL(/q=Notebook/);
    await expect(page).toHaveURL(/currency=USD/);
    await expect(page.getByRole('heading', { name: 'Results for “Notebook”' })).toBeVisible();
    await expect(page.locator('.product-card')).not.toHaveCount(0);
});

test('signs in with the Shop realm and adds a live-priced item to the cart', async ({
    page,
}, testInfo) => {
    const username = process.env.E2E_SHOP_USERNAME;
    const password = process.env.E2E_SHOP_PASSWORD;
    test.skip(
        !username || !password,
        'Set isolated local Shop E2E credentials to run authenticated flows.',
    );
    test.skip(
        testInfo.project.name === 'phone-landscape',
        'The authenticated journey is covered on desktop and small-phone layouts.',
    );

    await page.goto('/cart');
    await expect(page.getByRole('heading', { name: 'Sign in to view your cart' })).toBeVisible();
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await page.locator('#username').fill(username!);
    await page.locator('#password').fill(password!);
    await page.locator('#kc-login').click();
    await expect(page).toHaveURL(/\/cart/);

    await page.goto('/products?currency=USD&sort=NEWEST');
    await page.locator('.product-card__image').first().click();
    await page.getByRole('button', { name: 'Add to cart' }).click();
    await expect(page.getByText('Added to your cart.')).toBeVisible();
    await page.getByRole('link', { name: 'View cart' }).click();
    await expect(page.getByRole('heading', { name: 'Shopping cart' })).toBeVisible();
    await expect(page.locator('.cart-line')).not.toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Continue to checkout' })).toBeEnabled();
});
