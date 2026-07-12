import { expect, test, type ConsoleMessage, type Request, type Response } from '@playwright/test';

const apiHost = new URL(
  process.env.PLAYWRIGHT_API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5102',
).host;

test.describe('Gate 5 smoke UI', () => {
  test('login and operator pages talk to .NET :5102 only', async ({ page }) => {
    test.setTimeout(120_000);

    const consoleErrors: string[] = [];
    const badNest: string[] = [];
    const serverErrors: string[] = [];
    const apiHits: string[] = [];

    page.on('console', (msg: ConsoleMessage) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    page.on('request', (req: Request) => {
      const url = req.url();
      if (url.includes(':3001')) badNest.push(url);
      if (url.includes(apiHost) || url.includes(':5102')) apiHits.push(url);
    });

    page.on('response', (res: Response) => {
      if (res.status() >= 500) {
        serverErrors.push(`${res.status()} ${res.url()}`);
      }
    });

    await page.goto('/login');
    await page.getByLabel(/tenant slug/i).fill('acme');
    await page.getByLabel(/^email$/i).fill('admin@acme.test');
    await page.getByLabel(/^password$/i).fill('ChangeMe123!');
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page).toHaveURL(/\/orders/, { timeout: 45_000 });

    for (const path of ['/orders', '/inventory', '/warehouse', '/logistics', '/pos']) {
      await page.goto(path);
      await page.waitForLoadState('domcontentloaded');
      await expect(page.locator('main')).toBeVisible({ timeout: 15_000 });
      await expect(
        page
          .getByText(/sign out|orders|inventory|warehouse|logistics|pos|terminal|retail register/i)
          .first(),
      ).toBeVisible({ timeout: 15_000 });
    }

    // POS: unique terminal — open via UI controls when present.
    await page.goto('/pos');
    await page.waitForLoadState('domcontentloaded');
    const terminalId = `e2e-pos-${Date.now()}`;
    const terminal = page.getByLabel(/terminal/i);
    if (await terminal.count()) {
      await terminal.fill(terminalId);
    }
    const loadOrOpen = page.getByRole('button', { name: /cargar|open session|abrir/i });
    if (await loadOrOpen.count()) {
      await loadOrOpen.first().click();
      await page.waitForTimeout(1_000);
    }

    expect(badNest, `unexpected Nest :3001 traffic: ${badNest.join(', ')}`).toEqual([]);
    expect(serverErrors, `unexpected 5xx: ${serverErrors.join(', ')}`).toEqual([]);
    expect(apiHits.length, 'expected at least one request to .NET API').toBeGreaterThan(0);

    const unexpectedConsole = consoleErrors.filter(
      (t) =>
        !/favicon|Download the React DevTools/i.test(t) &&
        !/status of 409/i.test(t) &&
        !/status of 40[034]/i.test(t),
    );
    expect(unexpectedConsole, `console errors: ${unexpectedConsole.join(' | ')}`).toEqual([]);
  });
});
