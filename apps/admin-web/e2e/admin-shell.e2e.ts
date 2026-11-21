import { expect, test } from 'playwright/test';

test('shows the real Backoffice authentication entry point', async ({ page }) => {
  await page.goto('/catalog/products');
  await expect(page.getByText('MKBJ', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sign in to continue' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign in securely' })).toBeEnabled();
  await page.getByRole('button', { name: 'Sign in securely' }).click();
  await expect(page).toHaveURL(/\/realms\/Backoffice\/protocol\/openid-connect\/auth/);
  await expect(page.locator('#username')).toBeVisible();
  await expect(page.locator('#password')).toBeVisible();
  await expect(page.locator('#kc-login')).toBeVisible();
});

test('authenticates and opens the Catalog when credentials are supplied', async ({ page }, testInfo) => {
  const username = process.env.E2E_ADMIN_USERNAME;
  const password = process.env.E2E_ADMIN_PASSWORD;
  test.skip(!username || !password, 'Set isolated local Backoffice E2E credentials to run authenticated flows.');

  await page.goto('/catalog/products');
  await page.getByRole('button', { name: 'Sign in securely' }).click();
  await page.locator('#username').fill(username!);
  await page.locator('#password').fill(password!);
  await page.locator('#kc-login').click();
  await expect(page).toHaveURL(/\/catalog\/products/);
  await expect(page.getByRole('heading', { name: 'Products' })).toBeVisible();
  await expect(page.getByText('160 products', { exact: true })).toBeVisible();

  await page.goto('/catalog/products/new');
  await expect(page.getByRole('heading', { name: 'Create product' })).toBeVisible();
  await expect(page.getByLabel(/Primary category/)).toBeVisible();

  if (testInfo.project.name === 'mobile-chromium') {
    await page.getByRole('button', { name: 'Open navigation' }).click();
    await expect(page.getByRole('navigation', { name: 'Primary navigation' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();
  } else {
    await expect(page.getByRole('navigation', { name: 'Primary navigation' })).toBeVisible();
  }
});
